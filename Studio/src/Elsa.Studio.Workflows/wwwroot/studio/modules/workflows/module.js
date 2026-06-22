import Wt, { memo as pe, forwardRef as kn, useRef as se, useEffect as ae, useCallback as ye, useContext as qt, useMemo as xe, createContext as No, useState as oe, useLayoutEffect as ic, createElement as so } from "react";
function sc(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var Yn = { exports: {} }, Mt = {};
var tr;
function ac() {
  if (tr) return Mt;
  tr = 1;
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
  return Mt.Fragment = t, Mt.jsx = n, Mt.jsxs = n, Mt;
}
var nr;
function cc() {
  return nr || (nr = 1, Yn.exports = ac()), Yn.exports;
}
var h = cc();
function ge(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, o; n < e.length; n++)
      (o = ge(e[n])) !== "" && (t += (t && " ") + o);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var lc = { value: () => {
} };
function Mn() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new un(n);
}
function un(e) {
  this._ = e;
}
function uc(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
un.prototype = Mn.prototype = {
  constructor: un,
  on: function(e, t) {
    var n = this._, o = uc(e + "", n), r, i = -1, s = o.length;
    if (arguments.length < 2) {
      for (; ++i < s; ) if ((r = (e = o[i]).type) && (r = dc(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++i < s; )
      if (r = (e = o[i]).type) n[r] = or(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = or(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new un(e);
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
function dc(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function or(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = lc, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var ao = "http://www.w3.org/1999/xhtml";
const rr = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: ao,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function In(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), rr.hasOwnProperty(t) ? { space: rr[t], local: e } : e;
}
function fc(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === ao && t.documentElement.namespaceURI === ao ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function hc(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function mi(e) {
  var t = In(e);
  return (t.local ? hc : fc)(t);
}
function pc() {
}
function Co(e) {
  return e == null ? pc : function() {
    return this.querySelector(e);
  };
}
function gc(e) {
  typeof e != "function" && (e = Co(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = new Array(s), l, c, u = 0; u < s; ++u)
      (l = i[u]) && (c = e.call(l, l.__data__, u, i)) && ("__data__" in l && (c.__data__ = l.__data__), a[u] = c);
  return new Ee(o, this._parents);
}
function mc(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function yc() {
  return [];
}
function yi(e) {
  return e == null ? yc : function() {
    return this.querySelectorAll(e);
  };
}
function xc(e) {
  return function() {
    return mc(e.apply(this, arguments));
  };
}
function wc(e) {
  typeof e == "function" ? e = xc(e) : e = yi(e);
  for (var t = this._groups, n = t.length, o = [], r = [], i = 0; i < n; ++i)
    for (var s = t[i], a = s.length, l, c = 0; c < a; ++c)
      (l = s[c]) && (o.push(e.call(l, l.__data__, c, s)), r.push(l));
  return new Ee(o, r);
}
function xi(e) {
  return function() {
    return this.matches(e);
  };
}
function wi(e) {
  return function(t) {
    return t.matches(e);
  };
}
var vc = Array.prototype.find;
function bc(e) {
  return function() {
    return vc.call(this.children, e);
  };
}
function Sc() {
  return this.firstElementChild;
}
function _c(e) {
  return this.select(e == null ? Sc : bc(typeof e == "function" ? e : wi(e)));
}
var Ec = Array.prototype.filter;
function Nc() {
  return Array.from(this.children);
}
function Cc(e) {
  return function() {
    return Ec.call(this.children, e);
  };
}
function kc(e) {
  return this.selectAll(e == null ? Nc : Cc(typeof e == "function" ? e : wi(e)));
}
function Mc(e) {
  typeof e != "function" && (e = xi(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = [], l, c = 0; c < s; ++c)
      (l = i[c]) && e.call(l, l.__data__, c, i) && a.push(l);
  return new Ee(o, this._parents);
}
function vi(e) {
  return new Array(e.length);
}
function Ic() {
  return new Ee(this._enter || this._groups.map(vi), this._parents);
}
function mn(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
mn.prototype = {
  constructor: mn,
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
function Ac(e) {
  return function() {
    return e;
  };
}
function Dc(e, t, n, o, r, i) {
  for (var s = 0, a, l = t.length, c = i.length; s < c; ++s)
    (a = t[s]) ? (a.__data__ = i[s], o[s] = a) : n[s] = new mn(e, i[s]);
  for (; s < l; ++s)
    (a = t[s]) && (r[s] = a);
}
function Pc(e, t, n, o, r, i, s) {
  var a, l, c = /* @__PURE__ */ new Map(), u = t.length, d = i.length, f = new Array(u), p;
  for (a = 0; a < u; ++a)
    (l = t[a]) && (f[a] = p = s.call(l, l.__data__, a, t) + "", c.has(p) ? r[a] = l : c.set(p, l));
  for (a = 0; a < d; ++a)
    p = s.call(e, i[a], a, i) + "", (l = c.get(p)) ? (o[a] = l, l.__data__ = i[a], c.delete(p)) : n[a] = new mn(e, i[a]);
  for (a = 0; a < u; ++a)
    (l = t[a]) && c.get(f[a]) === l && (r[a] = l);
}
function jc(e) {
  return e.__data__;
}
function $c(e, t) {
  if (!arguments.length) return Array.from(this, jc);
  var n = t ? Pc : Dc, o = this._parents, r = this._groups;
  typeof e != "function" && (e = Ac(e));
  for (var i = r.length, s = new Array(i), a = new Array(i), l = new Array(i), c = 0; c < i; ++c) {
    var u = o[c], d = r[c], f = d.length, p = Tc(e.call(u, u && u.__data__, c, o)), g = p.length, x = a[c] = new Array(g), w = s[c] = new Array(g), y = l[c] = new Array(f);
    n(u, d, x, w, y, p, t);
    for (var S = 0, m = 0, b, N; S < g; ++S)
      if (b = x[S]) {
        for (S >= m && (m = S + 1); !(N = w[m]) && ++m < g; ) ;
        b._next = N || null;
      }
  }
  return s = new Ee(s, o), s._enter = a, s._exit = l, s;
}
function Tc(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function zc() {
  return new Ee(this._exit || this._groups.map(vi), this._parents);
}
function Rc(e, t, n) {
  var o = this.enter(), r = this, i = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? i.remove() : n(i), o && r ? o.merge(r).order() : r;
}
function Lc(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, i = o.length, s = Math.min(r, i), a = new Array(r), l = 0; l < s; ++l)
    for (var c = n[l], u = o[l], d = c.length, f = a[l] = new Array(d), p, g = 0; g < d; ++g)
      (p = c[g] || u[g]) && (f[g] = p);
  for (; l < r; ++l)
    a[l] = n[l];
  return new Ee(a, this._parents);
}
function Hc() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, i = o[r], s; --r >= 0; )
      (s = o[r]) && (i && s.compareDocumentPosition(i) ^ 4 && i.parentNode.insertBefore(s, i), i = s);
  return this;
}
function Oc(e) {
  e || (e = Vc);
  function t(d, f) {
    return d && f ? e(d.__data__, f.__data__) : !d - !f;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), i = 0; i < o; ++i) {
    for (var s = n[i], a = s.length, l = r[i] = new Array(a), c, u = 0; u < a; ++u)
      (c = s[u]) && (l[u] = c);
    l.sort(t);
  }
  return new Ee(r, this._parents).order();
}
function Vc(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function Bc() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function Fc() {
  return Array.from(this);
}
function Yc() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length; r < i; ++r) {
      var s = o[r];
      if (s) return s;
    }
  return null;
}
function Xc() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function Wc() {
  return !this.node();
}
function qc(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], i = 0, s = r.length, a; i < s; ++i)
      (a = r[i]) && e.call(a, a.__data__, i, r);
  return this;
}
function Zc(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Uc(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Gc(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function Kc(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function Qc(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function Jc(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function el(e, t) {
  var n = In(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? Uc : Zc : typeof t == "function" ? n.local ? Jc : Qc : n.local ? Kc : Gc)(n, t));
}
function bi(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function tl(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function nl(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function ol(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function rl(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? tl : typeof t == "function" ? ol : nl)(e, t, n ?? "")) : gt(this.node(), e);
}
function gt(e, t) {
  return e.style.getPropertyValue(t) || bi(e).getComputedStyle(e, null).getPropertyValue(t);
}
function il(e) {
  return function() {
    delete this[e];
  };
}
function sl(e, t) {
  return function() {
    this[e] = t;
  };
}
function al(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function cl(e, t) {
  return arguments.length > 1 ? this.each((t == null ? il : typeof t == "function" ? al : sl)(e, t)) : this.node()[e];
}
function Si(e) {
  return e.trim().split(/^|\s+/);
}
function ko(e) {
  return e.classList || new _i(e);
}
function _i(e) {
  this._node = e, this._names = Si(e.getAttribute("class") || "");
}
_i.prototype = {
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
function Ei(e, t) {
  for (var n = ko(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function Ni(e, t) {
  for (var n = ko(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function ll(e) {
  return function() {
    Ei(this, e);
  };
}
function ul(e) {
  return function() {
    Ni(this, e);
  };
}
function dl(e, t) {
  return function() {
    (t.apply(this, arguments) ? Ei : Ni)(this, e);
  };
}
function fl(e, t) {
  var n = Si(e + "");
  if (arguments.length < 2) {
    for (var o = ko(this.node()), r = -1, i = n.length; ++r < i; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? dl : t ? ll : ul)(n, t));
}
function hl() {
  this.textContent = "";
}
function pl(e) {
  return function() {
    this.textContent = e;
  };
}
function gl(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function ml(e) {
  return arguments.length ? this.each(e == null ? hl : (typeof e == "function" ? gl : pl)(e)) : this.node().textContent;
}
function yl() {
  this.innerHTML = "";
}
function xl(e) {
  return function() {
    this.innerHTML = e;
  };
}
function wl(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function vl(e) {
  return arguments.length ? this.each(e == null ? yl : (typeof e == "function" ? wl : xl)(e)) : this.node().innerHTML;
}
function bl() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function Sl() {
  return this.each(bl);
}
function _l() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function El() {
  return this.each(_l);
}
function Nl(e) {
  var t = typeof e == "function" ? e : mi(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Cl() {
  return null;
}
function kl(e, t) {
  var n = typeof e == "function" ? e : mi(e), o = t == null ? Cl : typeof t == "function" ? t : Co(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function Ml() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Il() {
  return this.each(Ml);
}
function Al() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Dl() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Pl(e) {
  return this.select(e ? Dl : Al);
}
function jl(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function $l(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function Tl(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function zl(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, i; n < r; ++n)
        i = t[n], (!e.type || i.type === e.type) && i.name === e.name ? this.removeEventListener(i.type, i.listener, i.options) : t[++o] = i;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function Rl(e, t, n) {
  return function() {
    var o = this.__on, r, i = $l(t);
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
function Ll(e, t, n) {
  var o = Tl(e + ""), r, i = o.length, s;
  if (arguments.length < 2) {
    var a = this.node().__on;
    if (a) {
      for (var l = 0, c = a.length, u; l < c; ++l)
        for (r = 0, u = a[l]; r < i; ++r)
          if ((s = o[r]).type === u.type && s.name === u.name)
            return u.value;
    }
    return;
  }
  for (a = t ? Rl : zl, r = 0; r < i; ++r) this.each(a(o[r], t, n));
  return this;
}
function Ci(e, t, n) {
  var o = bi(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function Hl(e, t) {
  return function() {
    return Ci(this, e, t);
  };
}
function Ol(e, t) {
  return function() {
    return Ci(this, e, t.apply(this, arguments));
  };
}
function Vl(e, t) {
  return this.each((typeof t == "function" ? Ol : Hl)(e, t));
}
function* Bl() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length, s; r < i; ++r)
      (s = o[r]) && (yield s);
}
var ki = [null];
function Ee(e, t) {
  this._groups = e, this._parents = t;
}
function Zt() {
  return new Ee([[document.documentElement]], ki);
}
function Fl() {
  return this;
}
Ee.prototype = Zt.prototype = {
  constructor: Ee,
  select: gc,
  selectAll: wc,
  selectChild: _c,
  selectChildren: kc,
  filter: Mc,
  data: $c,
  enter: Ic,
  exit: zc,
  join: Rc,
  merge: Lc,
  selection: Fl,
  order: Hc,
  sort: Oc,
  call: Bc,
  nodes: Fc,
  node: Yc,
  size: Xc,
  empty: Wc,
  each: qc,
  attr: el,
  style: rl,
  property: cl,
  classed: fl,
  text: ml,
  html: vl,
  raise: Sl,
  lower: El,
  append: Nl,
  insert: kl,
  remove: Il,
  clone: Pl,
  datum: jl,
  on: Ll,
  dispatch: Vl,
  [Symbol.iterator]: Bl
};
function _e(e) {
  return typeof e == "string" ? new Ee([[document.querySelector(e)]], [document.documentElement]) : new Ee([[e]], ki);
}
function Yl(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function De(e, t) {
  if (e = Yl(e), t === void 0 && (t = e.currentTarget), t) {
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
const Xl = { passive: !1 }, zt = { capture: !0, passive: !1 };
function Xn(e) {
  e.stopImmediatePropagation();
}
function ht(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Mi(e) {
  var t = e.document.documentElement, n = _e(e).on("dragstart.drag", ht, zt);
  "onselectstart" in t ? n.on("selectstart.drag", ht, zt) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function Ii(e, t) {
  var n = e.document.documentElement, o = _e(e).on("dragstart.drag", null);
  t && (o.on("click.drag", ht, zt), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const en = (e) => () => e;
function co(e, {
  sourceEvent: t,
  subject: n,
  target: o,
  identifier: r,
  active: i,
  x: s,
  y: a,
  dx: l,
  dy: c,
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
    y: { value: a, enumerable: !0, configurable: !0 },
    dx: { value: l, enumerable: !0, configurable: !0 },
    dy: { value: c, enumerable: !0, configurable: !0 },
    _: { value: u }
  });
}
co.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function Wl(e) {
  return !e.ctrlKey && !e.button;
}
function ql() {
  return this.parentNode;
}
function Zl(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function Ul() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Ai() {
  var e = Wl, t = ql, n = Zl, o = Ul, r = {}, i = Mn("start", "drag", "end"), s = 0, a, l, c, u, d = 0;
  function f(b) {
    b.on("mousedown.drag", p).filter(o).on("touchstart.drag", w).on("touchmove.drag", y, Xl).on("touchend.drag touchcancel.drag", S).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function p(b, N) {
    if (!(u || !e.call(this, b, N))) {
      var _ = m(this, t.call(this, b, N), b, N, "mouse");
      _ && (_e(b.view).on("mousemove.drag", g, zt).on("mouseup.drag", x, zt), Mi(b.view), Xn(b), c = !1, a = b.clientX, l = b.clientY, _("start", b));
    }
  }
  function g(b) {
    if (ht(b), !c) {
      var N = b.clientX - a, _ = b.clientY - l;
      c = N * N + _ * _ > d;
    }
    r.mouse("drag", b);
  }
  function x(b) {
    _e(b.view).on("mousemove.drag mouseup.drag", null), Ii(b.view, c), ht(b), r.mouse("end", b);
  }
  function w(b, N) {
    if (e.call(this, b, N)) {
      var _ = b.changedTouches, C = t.call(this, b, N), A = _.length, D, B;
      for (D = 0; D < A; ++D)
        (B = m(this, C, b, N, _[D].identifier, _[D])) && (Xn(b), B("start", b, _[D]));
    }
  }
  function y(b) {
    var N = b.changedTouches, _ = N.length, C, A;
    for (C = 0; C < _; ++C)
      (A = r[N[C].identifier]) && (ht(b), A("drag", b, N[C]));
  }
  function S(b) {
    var N = b.changedTouches, _ = N.length, C, A;
    for (u && clearTimeout(u), u = setTimeout(function() {
      u = null;
    }, 500), C = 0; C < _; ++C)
      (A = r[N[C].identifier]) && (Xn(b), A("end", b, N[C]));
  }
  function m(b, N, _, C, A, D) {
    var B = i.copy(), I = De(D || _, N), $, H, v;
    if ((v = n.call(b, new co("beforestart", {
      sourceEvent: _,
      target: f,
      identifier: A,
      active: s,
      x: I[0],
      y: I[1],
      dx: 0,
      dy: 0,
      dispatch: B
    }), C)) != null)
      return $ = v.x - I[0] || 0, H = v.y - I[1] || 0, function k(E, M, j) {
        var P = I, L;
        switch (E) {
          case "start":
            r[A] = k, L = s++;
            break;
          case "end":
            delete r[A], --s;
          // falls through
          case "drag":
            I = De(j || M, N), L = s;
            break;
        }
        B.call(
          E,
          b,
          new co(E, {
            sourceEvent: M,
            subject: v,
            target: f,
            identifier: A,
            active: L,
            x: I[0] + $,
            y: I[1] + H,
            dx: I[0] - P[0],
            dy: I[1] - P[1],
            dispatch: B
          }),
          C
        );
      };
  }
  return f.filter = function(b) {
    return arguments.length ? (e = typeof b == "function" ? b : en(!!b), f) : e;
  }, f.container = function(b) {
    return arguments.length ? (t = typeof b == "function" ? b : en(b), f) : t;
  }, f.subject = function(b) {
    return arguments.length ? (n = typeof b == "function" ? b : en(b), f) : n;
  }, f.touchable = function(b) {
    return arguments.length ? (o = typeof b == "function" ? b : en(!!b), f) : o;
  }, f.on = function() {
    var b = i.on.apply(i, arguments);
    return b === i ? f : b;
  }, f.clickDistance = function(b) {
    return arguments.length ? (d = (b = +b) * b, f) : Math.sqrt(d);
  }, f;
}
function Mo(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function Di(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function Ut() {
}
var Rt = 0.7, yn = 1 / Rt, pt = "\\s*([+-]?\\d+)\\s*", Lt = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", He = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", Gl = /^#([0-9a-f]{3,8})$/, Kl = new RegExp(`^rgb\\(${pt},${pt},${pt}\\)$`), Ql = new RegExp(`^rgb\\(${He},${He},${He}\\)$`), Jl = new RegExp(`^rgba\\(${pt},${pt},${pt},${Lt}\\)$`), eu = new RegExp(`^rgba\\(${He},${He},${He},${Lt}\\)$`), tu = new RegExp(`^hsl\\(${Lt},${He},${He}\\)$`), nu = new RegExp(`^hsla\\(${Lt},${He},${He},${Lt}\\)$`), ir = {
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
Mo(Ut, it, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: sr,
  // Deprecated! Use color.formatHex.
  formatHex: sr,
  formatHex8: ou,
  formatHsl: ru,
  formatRgb: ar,
  toString: ar
});
function sr() {
  return this.rgb().formatHex();
}
function ou() {
  return this.rgb().formatHex8();
}
function ru() {
  return Pi(this).formatHsl();
}
function ar() {
  return this.rgb().formatRgb();
}
function it(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = Gl.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? cr(t) : n === 3 ? new be(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? tn(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? tn(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = Kl.exec(e)) ? new be(t[1], t[2], t[3], 1) : (t = Ql.exec(e)) ? new be(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = Jl.exec(e)) ? tn(t[1], t[2], t[3], t[4]) : (t = eu.exec(e)) ? tn(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = tu.exec(e)) ? dr(t[1], t[2] / 100, t[3] / 100, 1) : (t = nu.exec(e)) ? dr(t[1], t[2] / 100, t[3] / 100, t[4]) : ir.hasOwnProperty(e) ? cr(ir[e]) : e === "transparent" ? new be(NaN, NaN, NaN, 0) : null;
}
function cr(e) {
  return new be(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function tn(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new be(e, t, n, o);
}
function iu(e) {
  return e instanceof Ut || (e = it(e)), e ? (e = e.rgb(), new be(e.r, e.g, e.b, e.opacity)) : new be();
}
function lo(e, t, n, o) {
  return arguments.length === 1 ? iu(e) : new be(e, t, n, o ?? 1);
}
function be(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
Mo(be, lo, Di(Ut, {
  brighter(e) {
    return e = e == null ? yn : Math.pow(yn, e), new be(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Rt : Math.pow(Rt, e), new be(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new be(ot(this.r), ot(this.g), ot(this.b), xn(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: lr,
  // Deprecated! Use color.formatHex.
  formatHex: lr,
  formatHex8: su,
  formatRgb: ur,
  toString: ur
}));
function lr() {
  return `#${nt(this.r)}${nt(this.g)}${nt(this.b)}`;
}
function su() {
  return `#${nt(this.r)}${nt(this.g)}${nt(this.b)}${nt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function ur() {
  const e = xn(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${ot(this.r)}, ${ot(this.g)}, ${ot(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function xn(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function ot(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function nt(e) {
  return e = ot(e), (e < 16 ? "0" : "") + e.toString(16);
}
function dr(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Pe(e, t, n, o);
}
function Pi(e) {
  if (e instanceof Pe) return new Pe(e.h, e.s, e.l, e.opacity);
  if (e instanceof Ut || (e = it(e)), !e) return new Pe();
  if (e instanceof Pe) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), i = Math.max(t, n, o), s = NaN, a = i - r, l = (i + r) / 2;
  return a ? (t === i ? s = (n - o) / a + (n < o) * 6 : n === i ? s = (o - t) / a + 2 : s = (t - n) / a + 4, a /= l < 0.5 ? i + r : 2 - i - r, s *= 60) : a = l > 0 && l < 1 ? 0 : s, new Pe(s, a, l, e.opacity);
}
function au(e, t, n, o) {
  return arguments.length === 1 ? Pi(e) : new Pe(e, t, n, o ?? 1);
}
function Pe(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
Mo(Pe, au, Di(Ut, {
  brighter(e) {
    return e = e == null ? yn : Math.pow(yn, e), new Pe(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Rt : Math.pow(Rt, e), new Pe(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new be(
      Wn(e >= 240 ? e - 240 : e + 120, r, o),
      Wn(e, r, o),
      Wn(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new Pe(fr(this.h), nn(this.s), nn(this.l), xn(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = xn(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${fr(this.h)}, ${nn(this.s) * 100}%, ${nn(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function fr(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function nn(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function Wn(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const Io = (e) => () => e;
function cu(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function lu(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function uu(e) {
  return (e = +e) == 1 ? ji : function(t, n) {
    return n - t ? lu(t, n, e) : Io(isNaN(t) ? n : t);
  };
}
function ji(e, t) {
  var n = t - e;
  return n ? cu(e, n) : Io(isNaN(e) ? t : e);
}
const wn = (function e(t) {
  var n = uu(t);
  function o(r, i) {
    var s = n((r = lo(r)).r, (i = lo(i)).r), a = n(r.g, i.g), l = n(r.b, i.b), c = ji(r.opacity, i.opacity);
    return function(u) {
      return r.r = s(u), r.g = a(u), r.b = l(u), r.opacity = c(u), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function du(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(i) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - i) + t[r] * i;
    return o;
  };
}
function fu(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function hu(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), i = new Array(n), s;
  for (s = 0; s < o; ++s) r[s] = $t(e[s], t[s]);
  for (; s < n; ++s) i[s] = t[s];
  return function(a) {
    for (s = 0; s < o; ++s) i[s] = r[s](a);
    return i;
  };
}
function pu(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(o) {
    return n.setTime(e * (1 - o) + t * o), n;
  };
}
function Le(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function gu(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = $t(e[r], t[r]) : o[r] = t[r];
  return function(i) {
    for (r in n) o[r] = n[r](i);
    return o;
  };
}
var uo = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, qn = new RegExp(uo.source, "g");
function mu(e) {
  return function() {
    return e;
  };
}
function yu(e) {
  return function(t) {
    return e(t) + "";
  };
}
function $i(e, t) {
  var n = uo.lastIndex = qn.lastIndex = 0, o, r, i, s = -1, a = [], l = [];
  for (e = e + "", t = t + ""; (o = uo.exec(e)) && (r = qn.exec(t)); )
    (i = r.index) > n && (i = t.slice(n, i), a[s] ? a[s] += i : a[++s] = i), (o = o[0]) === (r = r[0]) ? a[s] ? a[s] += r : a[++s] = r : (a[++s] = null, l.push({ i: s, x: Le(o, r) })), n = qn.lastIndex;
  return n < t.length && (i = t.slice(n), a[s] ? a[s] += i : a[++s] = i), a.length < 2 ? l[0] ? yu(l[0].x) : mu(t) : (t = l.length, function(c) {
    for (var u = 0, d; u < t; ++u) a[(d = l[u]).i] = d.x(c);
    return a.join("");
  });
}
function $t(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? Io(t) : (n === "number" ? Le : n === "string" ? (o = it(t)) ? (t = o, wn) : $i : t instanceof it ? wn : t instanceof Date ? pu : fu(t) ? du : Array.isArray(t) ? hu : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? gu : Le)(e, t);
}
var hr = 180 / Math.PI, fo = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function Ti(e, t, n, o, r, i) {
  var s, a, l;
  return (s = Math.sqrt(e * e + t * t)) && (e /= s, t /= s), (l = e * n + t * o) && (n -= e * l, o -= t * l), (a = Math.sqrt(n * n + o * o)) && (n /= a, o /= a, l /= a), e * o < t * n && (e = -e, t = -t, l = -l, s = -s), {
    translateX: r,
    translateY: i,
    rotate: Math.atan2(t, e) * hr,
    skewX: Math.atan(l) * hr,
    scaleX: s,
    scaleY: a
  };
}
var on;
function xu(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? fo : Ti(t.a, t.b, t.c, t.d, t.e, t.f);
}
function wu(e) {
  return e == null || (on || (on = document.createElementNS("http://www.w3.org/2000/svg", "g")), on.setAttribute("transform", e), !(e = on.transform.baseVal.consolidate())) ? fo : (e = e.matrix, Ti(e.a, e.b, e.c, e.d, e.e, e.f));
}
function zi(e, t, n, o) {
  function r(c) {
    return c.length ? c.pop() + " " : "";
  }
  function i(c, u, d, f, p, g) {
    if (c !== d || u !== f) {
      var x = p.push("translate(", null, t, null, n);
      g.push({ i: x - 4, x: Le(c, d) }, { i: x - 2, x: Le(u, f) });
    } else (d || f) && p.push("translate(" + d + t + f + n);
  }
  function s(c, u, d, f) {
    c !== u ? (c - u > 180 ? u += 360 : u - c > 180 && (c += 360), f.push({ i: d.push(r(d) + "rotate(", null, o) - 2, x: Le(c, u) })) : u && d.push(r(d) + "rotate(" + u + o);
  }
  function a(c, u, d, f) {
    c !== u ? f.push({ i: d.push(r(d) + "skewX(", null, o) - 2, x: Le(c, u) }) : u && d.push(r(d) + "skewX(" + u + o);
  }
  function l(c, u, d, f, p, g) {
    if (c !== d || u !== f) {
      var x = p.push(r(p) + "scale(", null, ",", null, ")");
      g.push({ i: x - 4, x: Le(c, d) }, { i: x - 2, x: Le(u, f) });
    } else (d !== 1 || f !== 1) && p.push(r(p) + "scale(" + d + "," + f + ")");
  }
  return function(c, u) {
    var d = [], f = [];
    return c = e(c), u = e(u), i(c.translateX, c.translateY, u.translateX, u.translateY, d, f), s(c.rotate, u.rotate, d, f), a(c.skewX, u.skewX, d, f), l(c.scaleX, c.scaleY, u.scaleX, u.scaleY, d, f), c = u = null, function(p) {
      for (var g = -1, x = f.length, w; ++g < x; ) d[(w = f[g]).i] = w.x(p);
      return d.join("");
    };
  };
}
var vu = zi(xu, "px, ", "px)", "deg)"), bu = zi(wu, ", ", ")", ")"), Su = 1e-12;
function pr(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function _u(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Eu(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const dn = (function e(t, n, o) {
  function r(i, s) {
    var a = i[0], l = i[1], c = i[2], u = s[0], d = s[1], f = s[2], p = u - a, g = d - l, x = p * p + g * g, w, y;
    if (x < Su)
      y = Math.log(f / c) / t, w = function(C) {
        return [
          a + C * p,
          l + C * g,
          c * Math.exp(t * C * y)
        ];
      };
    else {
      var S = Math.sqrt(x), m = (f * f - c * c + o * x) / (2 * c * n * S), b = (f * f - c * c - o * x) / (2 * f * n * S), N = Math.log(Math.sqrt(m * m + 1) - m), _ = Math.log(Math.sqrt(b * b + 1) - b);
      y = (_ - N) / t, w = function(C) {
        var A = C * y, D = pr(N), B = c / (n * S) * (D * Eu(t * A + N) - _u(N));
        return [
          a + B * p,
          l + B * g,
          c * D / pr(t * A + N)
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
var mt = 0, Pt = 0, It = 0, Ri = 1e3, vn, jt, bn = 0, st = 0, An = 0, Ht = typeof performance == "object" && performance.now ? performance : Date, Li = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function Ao() {
  return st || (Li(Nu), st = Ht.now() + An);
}
function Nu() {
  st = 0;
}
function Sn() {
  this._call = this._time = this._next = null;
}
Sn.prototype = Hi.prototype = {
  constructor: Sn,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? Ao() : +n) + (t == null ? 0 : +t), !this._next && jt !== this && (jt ? jt._next = this : vn = this, jt = this), this._call = e, this._time = n, ho();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, ho());
  }
};
function Hi(e, t, n) {
  var o = new Sn();
  return o.restart(e, t, n), o;
}
function Cu() {
  Ao(), ++mt;
  for (var e = vn, t; e; )
    (t = st - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --mt;
}
function gr() {
  st = (bn = Ht.now()) + An, mt = Pt = 0;
  try {
    Cu();
  } finally {
    mt = 0, Mu(), st = 0;
  }
}
function ku() {
  var e = Ht.now(), t = e - bn;
  t > Ri && (An -= t, bn = e);
}
function Mu() {
  for (var e, t = vn, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : vn = n);
  jt = e, ho(o);
}
function ho(e) {
  if (!mt) {
    Pt && (Pt = clearTimeout(Pt));
    var t = e - st;
    t > 24 ? (e < 1 / 0 && (Pt = setTimeout(gr, e - Ht.now() - An)), It && (It = clearInterval(It))) : (It || (bn = Ht.now(), It = setInterval(ku, Ri)), mt = 1, Li(gr));
  }
}
function mr(e, t, n) {
  var o = new Sn();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var Iu = Mn("start", "end", "cancel", "interrupt"), Au = [], Oi = 0, yr = 1, po = 2, fn = 3, xr = 4, go = 5, hn = 6;
function Dn(e, t, n, o, r, i) {
  var s = e.__transition;
  if (!s) e.__transition = {};
  else if (n in s) return;
  Du(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: Iu,
    tween: Au,
    time: i.time,
    delay: i.delay,
    duration: i.duration,
    ease: i.ease,
    timer: null,
    state: Oi
  });
}
function Do(e, t) {
  var n = ze(e, t);
  if (n.state > Oi) throw new Error("too late; already scheduled");
  return n;
}
function Ve(e, t) {
  var n = ze(e, t);
  if (n.state > fn) throw new Error("too late; already running");
  return n;
}
function ze(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function Du(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = Hi(i, 0, n.time);
  function i(c) {
    n.state = yr, n.timer.restart(s, n.delay, n.time), n.delay <= c && s(c - n.delay);
  }
  function s(c) {
    var u, d, f, p;
    if (n.state !== yr) return l();
    for (u in o)
      if (p = o[u], p.name === n.name) {
        if (p.state === fn) return mr(s);
        p.state === xr ? (p.state = hn, p.timer.stop(), p.on.call("interrupt", e, e.__data__, p.index, p.group), delete o[u]) : +u < t && (p.state = hn, p.timer.stop(), p.on.call("cancel", e, e.__data__, p.index, p.group), delete o[u]);
      }
    if (mr(function() {
      n.state === fn && (n.state = xr, n.timer.restart(a, n.delay, n.time), a(c));
    }), n.state = po, n.on.call("start", e, e.__data__, n.index, n.group), n.state === po) {
      for (n.state = fn, r = new Array(f = n.tween.length), u = 0, d = -1; u < f; ++u)
        (p = n.tween[u].value.call(e, e.__data__, n.index, n.group)) && (r[++d] = p);
      r.length = d + 1;
    }
  }
  function a(c) {
    for (var u = c < n.duration ? n.ease.call(null, c / n.duration) : (n.timer.restart(l), n.state = go, 1), d = -1, f = r.length; ++d < f; )
      r[d].call(e, u);
    n.state === go && (n.on.call("end", e, e.__data__, n.index, n.group), l());
  }
  function l() {
    n.state = hn, n.timer.stop(), delete o[t];
    for (var c in o) return;
    delete e.__transition;
  }
}
function pn(e, t) {
  var n = e.__transition, o, r, i = !0, s;
  if (n) {
    t = t == null ? null : t + "";
    for (s in n) {
      if ((o = n[s]).name !== t) {
        i = !1;
        continue;
      }
      r = o.state > po && o.state < go, o.state = hn, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[s];
    }
    i && delete e.__transition;
  }
}
function Pu(e) {
  return this.each(function() {
    pn(this, e);
  });
}
function ju(e, t) {
  var n, o;
  return function() {
    var r = Ve(this, e), i = r.tween;
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
function $u(e, t, n) {
  var o, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var i = Ve(this, e), s = i.tween;
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
function Tu(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = ze(this.node(), n).tween, r = 0, i = o.length, s; r < i; ++r)
      if ((s = o[r]).name === e)
        return s.value;
    return null;
  }
  return this.each((t == null ? ju : $u)(n, e, t));
}
function Po(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = Ve(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return ze(r, o).value[t];
  };
}
function Vi(e, t) {
  var n;
  return (typeof t == "number" ? Le : t instanceof it ? wn : (n = it(t)) ? (t = n, wn) : $i)(e, t);
}
function zu(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Ru(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Lu(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttribute(e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Hu(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttributeNS(e.space, e.local);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Ou(e, t, n) {
  var o, r, i;
  return function() {
    var s, a = n(this), l;
    return a == null ? void this.removeAttribute(e) : (s = this.getAttribute(e), l = a + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a)));
  };
}
function Vu(e, t, n) {
  var o, r, i;
  return function() {
    var s, a = n(this), l;
    return a == null ? void this.removeAttributeNS(e.space, e.local) : (s = this.getAttributeNS(e.space, e.local), l = a + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a)));
  };
}
function Bu(e, t) {
  var n = In(e), o = n === "transform" ? bu : Vi;
  return this.attrTween(e, typeof t == "function" ? (n.local ? Vu : Ou)(n, o, Po(this, "attr." + e, t)) : t == null ? (n.local ? Ru : zu)(n) : (n.local ? Hu : Lu)(n, o, t));
}
function Fu(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function Yu(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function Xu(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && Yu(e, i)), n;
  }
  return r._value = t, r;
}
function Wu(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && Fu(e, i)), n;
  }
  return r._value = t, r;
}
function qu(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = In(e);
  return this.tween(n, (o.local ? Xu : Wu)(o, t));
}
function Zu(e, t) {
  return function() {
    Do(this, e).delay = +t.apply(this, arguments);
  };
}
function Uu(e, t) {
  return t = +t, function() {
    Do(this, e).delay = t;
  };
}
function Gu(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Zu : Uu)(t, e)) : ze(this.node(), t).delay;
}
function Ku(e, t) {
  return function() {
    Ve(this, e).duration = +t.apply(this, arguments);
  };
}
function Qu(e, t) {
  return t = +t, function() {
    Ve(this, e).duration = t;
  };
}
function Ju(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Ku : Qu)(t, e)) : ze(this.node(), t).duration;
}
function ed(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    Ve(this, e).ease = t;
  };
}
function td(e) {
  var t = this._id;
  return arguments.length ? this.each(ed(t, e)) : ze(this.node(), t).ease;
}
function nd(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    Ve(this, e).ease = n;
  };
}
function od(e) {
  if (typeof e != "function") throw new Error();
  return this.each(nd(this._id, e));
}
function rd(e) {
  typeof e != "function" && (e = xi(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = [], l, c = 0; c < s; ++c)
      (l = i[c]) && e.call(l, l.__data__, c, i) && a.push(l);
  return new qe(o, this._parents, this._name, this._id);
}
function id(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, i = Math.min(o, r), s = new Array(o), a = 0; a < i; ++a)
    for (var l = t[a], c = n[a], u = l.length, d = s[a] = new Array(u), f, p = 0; p < u; ++p)
      (f = l[p] || c[p]) && (d[p] = f);
  for (; a < o; ++a)
    s[a] = t[a];
  return new qe(s, this._parents, this._name, this._id);
}
function sd(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function ad(e, t, n) {
  var o, r, i = sd(t) ? Do : Ve;
  return function() {
    var s = i(this, e), a = s.on;
    a !== o && (r = (o = a).copy()).on(t, n), s.on = r;
  };
}
function cd(e, t) {
  var n = this._id;
  return arguments.length < 2 ? ze(this.node(), n).on.on(e) : this.each(ad(n, e, t));
}
function ld(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function ud() {
  return this.on("end.remove", ld(this._id));
}
function dd(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Co(e));
  for (var o = this._groups, r = o.length, i = new Array(r), s = 0; s < r; ++s)
    for (var a = o[s], l = a.length, c = i[s] = new Array(l), u, d, f = 0; f < l; ++f)
      (u = a[f]) && (d = e.call(u, u.__data__, f, a)) && ("__data__" in u && (d.__data__ = u.__data__), c[f] = d, Dn(c[f], t, n, f, c, ze(u, n)));
  return new qe(i, this._parents, t, n);
}
function fd(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = yi(e));
  for (var o = this._groups, r = o.length, i = [], s = [], a = 0; a < r; ++a)
    for (var l = o[a], c = l.length, u, d = 0; d < c; ++d)
      if (u = l[d]) {
        for (var f = e.call(u, u.__data__, d, l), p, g = ze(u, n), x = 0, w = f.length; x < w; ++x)
          (p = f[x]) && Dn(p, t, n, x, f, g);
        i.push(f), s.push(u);
      }
  return new qe(i, s, t, n);
}
var hd = Zt.prototype.constructor;
function pd() {
  return new hd(this._groups, this._parents);
}
function gd(e, t) {
  var n, o, r;
  return function() {
    var i = gt(this, e), s = (this.style.removeProperty(e), gt(this, e));
    return i === s ? null : i === n && s === o ? r : r = t(n = i, o = s);
  };
}
function Bi(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function md(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = gt(this, e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function yd(e, t, n) {
  var o, r, i;
  return function() {
    var s = gt(this, e), a = n(this), l = a + "";
    return a == null && (l = a = (this.style.removeProperty(e), gt(this, e))), s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a));
  };
}
function xd(e, t) {
  var n, o, r, i = "style." + t, s = "end." + i, a;
  return function() {
    var l = Ve(this, e), c = l.on, u = l.value[i] == null ? a || (a = Bi(t)) : void 0;
    (c !== n || r !== u) && (o = (n = c).copy()).on(s, r = u), l.on = o;
  };
}
function wd(e, t, n) {
  var o = (e += "") == "transform" ? vu : Vi;
  return t == null ? this.styleTween(e, gd(e, o)).on("end.style." + e, Bi(e)) : typeof t == "function" ? this.styleTween(e, yd(e, o, Po(this, "style." + e, t))).each(xd(this._id, e)) : this.styleTween(e, md(e, o, t), n).on("end.style." + e, null);
}
function vd(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function bd(e, t, n) {
  var o, r;
  function i() {
    var s = t.apply(this, arguments);
    return s !== r && (o = (r = s) && vd(e, s, n)), o;
  }
  return i._value = t, i;
}
function Sd(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, bd(e, t, n ?? ""));
}
function _d(e) {
  return function() {
    this.textContent = e;
  };
}
function Ed(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function Nd(e) {
  return this.tween("text", typeof e == "function" ? Ed(Po(this, "text", e)) : _d(e == null ? "" : e + ""));
}
function Cd(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function kd(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Cd(r)), t;
  }
  return o._value = e, o;
}
function Md(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, kd(e));
}
function Id() {
  for (var e = this._name, t = this._id, n = Fi(), o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], a = s.length, l, c = 0; c < a; ++c)
      if (l = s[c]) {
        var u = ze(l, t);
        Dn(l, e, n, c, s, {
          time: u.time + u.delay + u.duration,
          delay: 0,
          duration: u.duration,
          ease: u.ease
        });
      }
  return new qe(o, this._parents, e, n);
}
function Ad() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(i, s) {
    var a = { value: s }, l = { value: function() {
      --r === 0 && i();
    } };
    n.each(function() {
      var c = Ve(this, o), u = c.on;
      u !== e && (t = (e = u).copy(), t._.cancel.push(a), t._.interrupt.push(a), t._.end.push(l)), c.on = t;
    }), r === 0 && i();
  });
}
var Dd = 0;
function qe(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function Fi() {
  return ++Dd;
}
var Ye = Zt.prototype;
qe.prototype = {
  constructor: qe,
  select: dd,
  selectAll: fd,
  selectChild: Ye.selectChild,
  selectChildren: Ye.selectChildren,
  filter: rd,
  merge: id,
  selection: pd,
  transition: Id,
  call: Ye.call,
  nodes: Ye.nodes,
  node: Ye.node,
  size: Ye.size,
  empty: Ye.empty,
  each: Ye.each,
  on: cd,
  attr: Bu,
  attrTween: qu,
  style: wd,
  styleTween: Sd,
  text: Nd,
  textTween: Md,
  remove: ud,
  tween: Tu,
  delay: Gu,
  duration: Ju,
  ease: td,
  easeVarying: od,
  end: Ad,
  [Symbol.iterator]: Ye[Symbol.iterator]
};
function Pd(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var jd = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: Pd
};
function $d(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function Td(e) {
  var t, n;
  e instanceof qe ? (t = e._id, e = e._name) : (t = Fi(), (n = jd).time = Ao(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], a = s.length, l, c = 0; c < a; ++c)
      (l = s[c]) && Dn(l, e, t, c, s, n || $d(l, t));
  return new qe(o, this._parents, e, t);
}
Zt.prototype.interrupt = Pu;
Zt.prototype.transition = Td;
const rn = (e) => () => e;
function zd(e, {
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
function Xe(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
Xe.prototype = {
  constructor: Xe,
  scale: function(e) {
    return e === 1 ? this : new Xe(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new Xe(this.k, this.x + this.k * e, this.y + this.k * t);
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
var Pn = new Xe(1, 0, 0);
Yi.prototype = Xe.prototype;
function Yi(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return Pn;
  return e.__zoom;
}
function Zn(e) {
  e.stopImmediatePropagation();
}
function At(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Rd(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function Ld() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function wr() {
  return this.__zoom || Pn;
}
function Hd(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function Od() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Vd(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], i = e.invertY(t[0][1]) - n[0][1], s = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    s > i ? (i + s) / 2 : Math.min(0, i) || Math.max(0, s)
  );
}
function Xi() {
  var e = Rd, t = Ld, n = Vd, o = Hd, r = Od, i = [0, 1 / 0], s = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], a = 250, l = dn, c = Mn("start", "zoom", "end"), u, d, f, p = 500, g = 150, x = 0, w = 10;
  function y(v) {
    v.property("__zoom", wr).on("wheel.zoom", A, { passive: !1 }).on("mousedown.zoom", D).on("dblclick.zoom", B).filter(r).on("touchstart.zoom", I).on("touchmove.zoom", $).on("touchend.zoom touchcancel.zoom", H).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  y.transform = function(v, k, E, M) {
    var j = v.selection ? v.selection() : v;
    j.property("__zoom", wr), v !== j ? N(v, k, E, M) : j.interrupt().each(function() {
      _(this, arguments).event(M).start().zoom(null, typeof k == "function" ? k.apply(this, arguments) : k).end();
    });
  }, y.scaleBy = function(v, k, E, M) {
    y.scaleTo(v, function() {
      var j = this.__zoom.k, P = typeof k == "function" ? k.apply(this, arguments) : k;
      return j * P;
    }, E, M);
  }, y.scaleTo = function(v, k, E, M) {
    y.transform(v, function() {
      var j = t.apply(this, arguments), P = this.__zoom, L = E == null ? b(j) : typeof E == "function" ? E.apply(this, arguments) : E, V = P.invert(L), O = typeof k == "function" ? k.apply(this, arguments) : k;
      return n(m(S(P, O), L, V), j, s);
    }, E, M);
  }, y.translateBy = function(v, k, E, M) {
    y.transform(v, function() {
      return n(this.__zoom.translate(
        typeof k == "function" ? k.apply(this, arguments) : k,
        typeof E == "function" ? E.apply(this, arguments) : E
      ), t.apply(this, arguments), s);
    }, null, M);
  }, y.translateTo = function(v, k, E, M, j) {
    y.transform(v, function() {
      var P = t.apply(this, arguments), L = this.__zoom, V = M == null ? b(P) : typeof M == "function" ? M.apply(this, arguments) : M;
      return n(Pn.translate(V[0], V[1]).scale(L.k).translate(
        typeof k == "function" ? -k.apply(this, arguments) : -k,
        typeof E == "function" ? -E.apply(this, arguments) : -E
      ), P, s);
    }, M, j);
  };
  function S(v, k) {
    return k = Math.max(i[0], Math.min(i[1], k)), k === v.k ? v : new Xe(k, v.x, v.y);
  }
  function m(v, k, E) {
    var M = k[0] - E[0] * v.k, j = k[1] - E[1] * v.k;
    return M === v.x && j === v.y ? v : new Xe(v.k, M, j);
  }
  function b(v) {
    return [(+v[0][0] + +v[1][0]) / 2, (+v[0][1] + +v[1][1]) / 2];
  }
  function N(v, k, E, M) {
    v.on("start.zoom", function() {
      _(this, arguments).event(M).start();
    }).on("interrupt.zoom end.zoom", function() {
      _(this, arguments).event(M).end();
    }).tween("zoom", function() {
      var j = this, P = arguments, L = _(j, P).event(M), V = t.apply(j, P), O = E == null ? b(V) : typeof E == "function" ? E.apply(j, P) : E, Y = Math.max(V[1][0] - V[0][0], V[1][1] - V[0][1]), W = j.__zoom, Q = typeof k == "function" ? k.apply(j, P) : k, ne = l(W.invert(O).concat(Y / W.k), Q.invert(O).concat(Y / Q.k));
      return function(Z) {
        if (Z === 1) Z = Q;
        else {
          var z = ne(Z), X = Y / z[2];
          Z = new Xe(X, O[0] - z[0] * X, O[1] - z[1] * X);
        }
        L.zoom(null, Z);
      };
    });
  }
  function _(v, k, E) {
    return !E && v.__zooming || new C(v, k);
  }
  function C(v, k) {
    this.that = v, this.args = k, this.active = 0, this.sourceEvent = null, this.extent = t.apply(v, k), this.taps = 0;
  }
  C.prototype = {
    event: function(v) {
      return v && (this.sourceEvent = v), this;
    },
    start: function() {
      return ++this.active === 1 && (this.that.__zooming = this, this.emit("start")), this;
    },
    zoom: function(v, k) {
      return this.mouse && v !== "mouse" && (this.mouse[1] = k.invert(this.mouse[0])), this.touch0 && v !== "touch" && (this.touch0[1] = k.invert(this.touch0[0])), this.touch1 && v !== "touch" && (this.touch1[1] = k.invert(this.touch1[0])), this.that.__zoom = k, this.emit("zoom"), this;
    },
    end: function() {
      return --this.active === 0 && (delete this.that.__zooming, this.emit("end")), this;
    },
    emit: function(v) {
      var k = _e(this.that).datum();
      c.call(
        v,
        this.that,
        new zd(v, {
          sourceEvent: this.sourceEvent,
          target: y,
          transform: this.that.__zoom,
          dispatch: c
        }),
        k
      );
    }
  };
  function A(v, ...k) {
    if (!e.apply(this, arguments)) return;
    var E = _(this, k).event(v), M = this.__zoom, j = Math.max(i[0], Math.min(i[1], M.k * Math.pow(2, o.apply(this, arguments)))), P = De(v);
    if (E.wheel)
      (E.mouse[0][0] !== P[0] || E.mouse[0][1] !== P[1]) && (E.mouse[1] = M.invert(E.mouse[0] = P)), clearTimeout(E.wheel);
    else {
      if (M.k === j) return;
      E.mouse = [P, M.invert(P)], pn(this), E.start();
    }
    At(v), E.wheel = setTimeout(L, g), E.zoom("mouse", n(m(S(M, j), E.mouse[0], E.mouse[1]), E.extent, s));
    function L() {
      E.wheel = null, E.end();
    }
  }
  function D(v, ...k) {
    if (f || !e.apply(this, arguments)) return;
    var E = v.currentTarget, M = _(this, k, !0).event(v), j = _e(v.view).on("mousemove.zoom", O, !0).on("mouseup.zoom", Y, !0), P = De(v, E), L = v.clientX, V = v.clientY;
    Mi(v.view), Zn(v), M.mouse = [P, this.__zoom.invert(P)], pn(this), M.start();
    function O(W) {
      if (At(W), !M.moved) {
        var Q = W.clientX - L, ne = W.clientY - V;
        M.moved = Q * Q + ne * ne > x;
      }
      M.event(W).zoom("mouse", n(m(M.that.__zoom, M.mouse[0] = De(W, E), M.mouse[1]), M.extent, s));
    }
    function Y(W) {
      j.on("mousemove.zoom mouseup.zoom", null), Ii(W.view, M.moved), At(W), M.event(W).end();
    }
  }
  function B(v, ...k) {
    if (e.apply(this, arguments)) {
      var E = this.__zoom, M = De(v.changedTouches ? v.changedTouches[0] : v, this), j = E.invert(M), P = E.k * (v.shiftKey ? 0.5 : 2), L = n(m(S(E, P), M, j), t.apply(this, k), s);
      At(v), a > 0 ? _e(this).transition().duration(a).call(N, L, M, v) : _e(this).call(y.transform, L, M, v);
    }
  }
  function I(v, ...k) {
    if (e.apply(this, arguments)) {
      var E = v.touches, M = E.length, j = _(this, k, v.changedTouches.length === M).event(v), P, L, V, O;
      for (Zn(v), L = 0; L < M; ++L)
        V = E[L], O = De(V, this), O = [O, this.__zoom.invert(O), V.identifier], j.touch0 ? !j.touch1 && j.touch0[2] !== O[2] && (j.touch1 = O, j.taps = 0) : (j.touch0 = O, P = !0, j.taps = 1 + !!u);
      u && (u = clearTimeout(u)), P && (j.taps < 2 && (d = O[0], u = setTimeout(function() {
        u = null;
      }, p)), pn(this), j.start());
    }
  }
  function $(v, ...k) {
    if (this.__zooming) {
      var E = _(this, k).event(v), M = v.changedTouches, j = M.length, P, L, V, O;
      for (At(v), P = 0; P < j; ++P)
        L = M[P], V = De(L, this), E.touch0 && E.touch0[2] === L.identifier ? E.touch0[0] = V : E.touch1 && E.touch1[2] === L.identifier && (E.touch1[0] = V);
      if (L = E.that.__zoom, E.touch1) {
        var Y = E.touch0[0], W = E.touch0[1], Q = E.touch1[0], ne = E.touch1[1], Z = (Z = Q[0] - Y[0]) * Z + (Z = Q[1] - Y[1]) * Z, z = (z = ne[0] - W[0]) * z + (z = ne[1] - W[1]) * z;
        L = S(L, Math.sqrt(Z / z)), V = [(Y[0] + Q[0]) / 2, (Y[1] + Q[1]) / 2], O = [(W[0] + ne[0]) / 2, (W[1] + ne[1]) / 2];
      } else if (E.touch0) V = E.touch0[0], O = E.touch0[1];
      else return;
      E.zoom("touch", n(m(L, V, O), E.extent, s));
    }
  }
  function H(v, ...k) {
    if (this.__zooming) {
      var E = _(this, k).event(v), M = v.changedTouches, j = M.length, P, L;
      for (Zn(v), f && clearTimeout(f), f = setTimeout(function() {
        f = null;
      }, p), P = 0; P < j; ++P)
        L = M[P], E.touch0 && E.touch0[2] === L.identifier ? delete E.touch0 : E.touch1 && E.touch1[2] === L.identifier && delete E.touch1;
      if (E.touch1 && !E.touch0 && (E.touch0 = E.touch1, delete E.touch1), E.touch0) E.touch0[1] = this.__zoom.invert(E.touch0[0]);
      else if (E.end(), E.taps === 2 && (L = De(L, this), Math.hypot(d[0] - L[0], d[1] - L[1]) < w)) {
        var V = _e(this).on("dblclick.zoom");
        V && V.apply(this, arguments);
      }
    }
  }
  return y.wheelDelta = function(v) {
    return arguments.length ? (o = typeof v == "function" ? v : rn(+v), y) : o;
  }, y.filter = function(v) {
    return arguments.length ? (e = typeof v == "function" ? v : rn(!!v), y) : e;
  }, y.touchable = function(v) {
    return arguments.length ? (r = typeof v == "function" ? v : rn(!!v), y) : r;
  }, y.extent = function(v) {
    return arguments.length ? (t = typeof v == "function" ? v : rn([[+v[0][0], +v[0][1]], [+v[1][0], +v[1][1]]]), y) : t;
  }, y.scaleExtent = function(v) {
    return arguments.length ? (i[0] = +v[0], i[1] = +v[1], y) : [i[0], i[1]];
  }, y.translateExtent = function(v) {
    return arguments.length ? (s[0][0] = +v[0][0], s[1][0] = +v[1][0], s[0][1] = +v[0][1], s[1][1] = +v[1][1], y) : [[s[0][0], s[0][1]], [s[1][0], s[1][1]]];
  }, y.constrain = function(v) {
    return arguments.length ? (n = v, y) : n;
  }, y.duration = function(v) {
    return arguments.length ? (a = +v, y) : a;
  }, y.interpolate = function(v) {
    return arguments.length ? (l = v, y) : l;
  }, y.on = function() {
    var v = c.on.apply(c, arguments);
    return v === c ? y : v;
  }, y.clickDistance = function(v) {
    return arguments.length ? (x = (v = +v) * v, y) : Math.sqrt(x);
  }, y.tapDistance = function(v) {
    return arguments.length ? (w = +v, y) : w;
  }, y;
}
const Te = {
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
}, Ot = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], Wi = ["Enter", " ", "Escape"], qi = {
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
var yt;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(yt || (yt = {}));
var rt;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(rt || (rt = {}));
var Vt;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Vt || (Vt = {}));
const Zi = {
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
var Ke;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(Ke || (Ke = {}));
var _n;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(_n || (_n = {}));
var K;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(K || (K = {}));
const vr = {
  [K.Left]: K.Right,
  [K.Right]: K.Left,
  [K.Top]: K.Bottom,
  [K.Bottom]: K.Top
};
function Ui(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Gi = (e) => "id" in e && "source" in e && "target" in e, Bd = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), jo = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), Gt = (e, t = [0, 0]) => {
  const { width: n, height: o } = Ze(e), r = e.origin ?? t, i = n * r[0], s = o * r[1];
  return {
    x: e.position.x - i,
    y: e.position.y - s
  };
}, Fd = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const i = typeof r == "string";
    let s = !t.nodeLookup && !i ? r : void 0;
    t.nodeLookup && (s = i ? t.nodeLookup.get(r) : jo(r) ? r : t.nodeLookup.get(r.id));
    const a = s ? En(s, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return jn(o, a);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return $n(n);
}, Kt = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = jn(n, En(r)), o = !0);
  }), o ? $n(n) : { x: 0, y: 0, width: 0, height: 0 };
}, $o = (e, t, [n, o, r] = [0, 0, 1], i = !1, s = !1) => {
  const a = {
    ...Ct(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, l = [];
  for (const c of e.values()) {
    const { measured: u, selectable: d = !0, hidden: f = !1 } = c;
    if (s && !d || f)
      continue;
    const p = u.width ?? c.width ?? c.initialWidth ?? null, g = u.height ?? c.height ?? c.initialHeight ?? null, x = Bt(a, wt(c)), w = (p ?? 0) * (g ?? 0), y = i && x > 0;
    (!c.internals.handleBounds || y || x >= w || c.dragging) && l.push(c);
  }
  return l;
}, Yd = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function Xd(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function Wd({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: i }, s) {
  if (e.size === 0)
    return !0;
  const a = Xd(e, s), l = Kt(a), c = zo(l, t, n, s?.minZoom ?? r, s?.maxZoom ?? i, s?.padding ?? 0.1);
  return await o.setViewport(c, {
    duration: s?.duration,
    ease: s?.ease,
    interpolate: s?.interpolate
  }), !0;
}
function Ki({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: i }) {
  const s = n.get(e), a = s.parentId ? n.get(s.parentId) : void 0, { x: l, y: c } = a ? a.internals.positionAbsolute : { x: 0, y: 0 }, u = s.origin ?? o;
  let d = s.extent || r;
  if (s.extent === "parent" && !s.expandParent)
    if (!a)
      i?.("005", Te.error005());
    else {
      const p = a.measured.width, g = a.measured.height;
      p && g && (d = [
        [l, c],
        [l + p, c + g]
      ]);
    }
  else a && ct(s.extent) && (d = [
    [s.extent[0][0] + l, s.extent[0][1] + c],
    [s.extent[1][0] + l, s.extent[1][1] + c]
  ]);
  const f = ct(d) ? at(t, d, s.measured) : t;
  return (s.measured.width === void 0 || s.measured.height === void 0) && i?.("015", Te.error015()), {
    position: {
      x: f.x - l + (s.measured.width ?? 0) * u[0],
      y: f.y - c + (s.measured.height ?? 0) * u[1]
    },
    positionAbsolute: f
  };
}
async function qd({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const i = new Set(e.map((f) => f.id)), s = [];
  for (const f of n) {
    if (f.deletable === !1)
      continue;
    const p = i.has(f.id), g = !p && f.parentId && s.find((x) => x.id === f.parentId);
    (p || g) && s.push(f);
  }
  const a = new Set(t.map((f) => f.id)), l = o.filter((f) => f.deletable !== !1), u = Yd(s, l);
  for (const f of l)
    a.has(f.id) && !u.find((g) => g.id === f.id) && u.push(f);
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
const xt = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), at = (e = { x: 0, y: 0 }, t, n) => ({
  x: xt(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: xt(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Qi(e, t, n) {
  const { width: o, height: r } = Ze(n), { x: i, y: s } = n.internals.positionAbsolute;
  return at(e, [
    [i, s],
    [i + o, s + r]
  ], t);
}
const br = (e, t, n) => e < t ? xt(Math.abs(e - t), 1, t) / t : e > n ? -xt(Math.abs(e - n), 1, t) / t : 0, To = (e, t, n = 15, o = 40) => {
  const r = br(e.x, o, t.width - o) * n, i = br(e.y, o, t.height - o) * n;
  return [r, i];
}, jn = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), mo = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), $n = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), wt = (e, t = [0, 0]) => {
  const { x: n, y: o } = jo(e) ? e.internals.positionAbsolute : Gt(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, En = (e, t = [0, 0]) => {
  const { x: n, y: o } = jo(e) ? e.internals.positionAbsolute : Gt(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, Ji = (e, t) => $n(jn(mo(e), mo(t))), Bt = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, Sr = (e) => je(e.width) && je(e.height) && je(e.x) && je(e.y), je = (e) => !isNaN(e) && isFinite(e), es = (e, t) => (n, o) => {
}, Qt = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), Ct = ({ x: e, y: t }, [n, o, r], i = !1, s = [1, 1]) => {
  const a = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return i ? Qt(a, s) : a;
}, vt = ({ x: e, y: t }, [n, o, r]) => ({
  x: e * r + n,
  y: t * r + o
});
function ut(e, t) {
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
function Zd(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = ut(e, n), r = ut(e, t);
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
    const o = ut(e.top ?? e.y ?? 0, n), r = ut(e.bottom ?? e.y ?? 0, n), i = ut(e.left ?? e.x ?? 0, t), s = ut(e.right ?? e.x ?? 0, t);
    return { top: o, right: s, bottom: r, left: i, x: i + s, y: o + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function Ud(e, t, n, o, r, i) {
  const { x: s, y: a } = vt(e, [t, n, o]), { x: l, y: c } = vt({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), u = r - l, d = i - c;
  return {
    left: Math.floor(s),
    top: Math.floor(a),
    right: Math.floor(u),
    bottom: Math.floor(d)
  };
}
const zo = (e, t, n, o, r, i) => {
  const s = Zd(i, t, n), a = (t - s.x) / e.width, l = (n - s.y) / e.height, c = Math.min(a, l), u = xt(c, o, r), d = e.x + e.width / 2, f = e.y + e.height / 2, p = t / 2 - d * u, g = n / 2 - f * u, x = Ud(e, p, g, u, t, n), w = {
    left: Math.min(x.left - s.left, 0),
    top: Math.min(x.top - s.top, 0),
    right: Math.min(x.right - s.right, 0),
    bottom: Math.min(x.bottom - s.bottom, 0)
  };
  return {
    x: p - w.left + w.right,
    y: g - w.top + w.bottom,
    zoom: u
  };
}, Ft = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function ct(e) {
  return e != null && e !== "parent";
}
function Ze(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function ts(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function ns(e, t = { width: 0, height: 0 }, n, o, r) {
  const i = { ...e }, s = o.get(n);
  if (s) {
    const a = s.origin || r;
    i.x += s.internals.positionAbsolute.x - (t.width ?? 0) * a[0], i.y += s.internals.positionAbsolute.y - (t.height ?? 0) * a[1];
  }
  return i;
}
function _r(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function Gd() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function Kd(e) {
  return { ...qi, ...e || {} };
}
function Tt(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: i, y: s } = $e(e), a = Ct({ x: i - (r?.left ?? 0), y: s - (r?.top ?? 0) }, o), { x: l, y: c } = n ? Qt(a, t) : a;
  return {
    xSnapped: l,
    ySnapped: c,
    ...a
  };
}
const Ro = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), os = (e) => e?.getRootNode?.() || window?.document, Qd = ["INPUT", "SELECT", "TEXTAREA"];
function rs(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : Qd.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const is = (e) => "clientX" in e, $e = (e, t) => {
  const n = is(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, Er = (e, t, n, o, r) => {
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
      ...Ro(s)
    };
  });
};
function ss({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: i, targetControlX: s, targetControlY: a }) {
  const l = e * 0.125 + r * 0.375 + s * 0.375 + n * 0.125, c = t * 0.125 + i * 0.375 + a * 0.375 + o * 0.125, u = Math.abs(l - e), d = Math.abs(c - t);
  return [l, c, u, d];
}
function sn(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function Nr({ pos: e, x1: t, y1: n, x2: o, y2: r, c: i }) {
  switch (e) {
    case K.Left:
      return [t - sn(t - o, i), n];
    case K.Right:
      return [t + sn(o - t, i), n];
    case K.Top:
      return [t, n - sn(n - r, i)];
    case K.Bottom:
      return [t, n + sn(r - n, i)];
  }
}
function as({ sourceX: e, sourceY: t, sourcePosition: n = K.Bottom, targetX: o, targetY: r, targetPosition: i = K.Top, curvature: s = 0.25 }) {
  const [a, l] = Nr({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: s
  }), [c, u] = Nr({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: s
  }), [d, f, p, g] = ss({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: r,
    sourceControlX: a,
    sourceControlY: l,
    targetControlX: c,
    targetControlY: u
  });
  return [
    `M${e},${t} C${a},${l} ${c},${u} ${o},${r}`,
    d,
    f,
    p,
    g
  ];
}
function cs({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, i = n < e ? n + r : n - r, s = Math.abs(o - t) / 2, a = o < t ? o + s : o - s;
  return [i, a, r, s];
}
function Jd({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: i = "basic" }) {
  if (i === "manual")
    return o;
  const s = r && n ? o + 1e3 : o, a = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return s + a;
}
function ef({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const i = jn(En(e), En(t));
  i.x === i.x2 && (i.x2 += 1), i.y === i.y2 && (i.y2 += 1);
  const s = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return Bt(s, $n(i)) > 0;
}
const tf = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, nf = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), of = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Te.error006()), t;
  const o = n.getEdgeId || tf;
  let r;
  return Gi(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, nf(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
};
function ls({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, i, s, a] = cs({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, i, s, a];
}
const Cr = {
  [K.Left]: { x: -1, y: 0 },
  [K.Right]: { x: 1, y: 0 },
  [K.Top]: { x: 0, y: -1 },
  [K.Bottom]: { x: 0, y: 1 }
}, rf = ({ source: e, sourcePosition: t = K.Bottom, target: n }) => t === K.Left || t === K.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, kr = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function sf({ source: e, sourcePosition: t = K.Bottom, target: n, targetPosition: o = K.Top, center: r, offset: i, stepPosition: s }) {
  const a = Cr[t], l = Cr[o], c = { x: e.x + a.x * i, y: e.y + a.y * i }, u = { x: n.x + l.x * i, y: n.y + l.y * i }, d = rf({
    source: c,
    sourcePosition: t,
    target: u
  }), f = d.x !== 0 ? "x" : "y", p = d[f];
  let g = [], x, w;
  const y = { x: 0, y: 0 }, S = { x: 0, y: 0 }, [, , m, b] = cs({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (a[f] * l[f] === -1) {
    f === "x" ? (x = r.x ?? c.x + (u.x - c.x) * s, w = r.y ?? (c.y + u.y) / 2) : (x = r.x ?? (c.x + u.x) / 2, w = r.y ?? c.y + (u.y - c.y) * s);
    const A = [
      { x, y: c.y },
      { x, y: u.y }
    ], D = [
      { x: c.x, y: w },
      { x: u.x, y: w }
    ];
    a[f] === p ? g = f === "x" ? A : D : g = f === "x" ? D : A;
  } else {
    const A = [{ x: c.x, y: u.y }], D = [{ x: u.x, y: c.y }];
    if (f === "x" ? g = a.x === p ? D : A : g = a.y === p ? A : D, t === o) {
      const v = Math.abs(e[f] - n[f]);
      if (v <= i) {
        const k = Math.min(i - 1, i - v);
        a[f] === p ? y[f] = (c[f] > e[f] ? -1 : 1) * k : S[f] = (u[f] > n[f] ? -1 : 1) * k;
      }
    }
    if (t !== o) {
      const v = f === "x" ? "y" : "x", k = a[f] === l[v], E = c[v] > u[v], M = c[v] < u[v];
      (a[f] === 1 && (!k && E || k && M) || a[f] !== 1 && (!k && M || k && E)) && (g = f === "x" ? A : D);
    }
    const B = { x: c.x + y.x, y: c.y + y.y }, I = { x: u.x + S.x, y: u.y + S.y }, $ = Math.max(Math.abs(B.x - g[0].x), Math.abs(I.x - g[0].x)), H = Math.max(Math.abs(B.y - g[0].y), Math.abs(I.y - g[0].y));
    $ >= H ? (x = (B.x + I.x) / 2, w = g[0].y) : (x = g[0].x, w = (B.y + I.y) / 2);
  }
  const N = { x: c.x + y.x, y: c.y + y.y }, _ = { x: u.x + S.x, y: u.y + S.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...N.x !== g[0].x || N.y !== g[0].y ? [N] : [],
    ...g,
    ..._.x !== g[g.length - 1].x || _.y !== g[g.length - 1].y ? [_] : [],
    n
  ], x, w, m, b];
}
function af(e, t, n, o) {
  const r = Math.min(kr(e, t) / 2, kr(t, n) / 2, o), { x: i, y: s } = t;
  if (e.x === i && i === n.x || e.y === s && s === n.y)
    return `L${i} ${s}`;
  if (e.y === s) {
    const c = e.x < n.x ? -1 : 1, u = e.y < n.y ? 1 : -1;
    return `L ${i + r * c},${s}Q ${i},${s} ${i},${s + r * u}`;
  }
  const a = e.x < n.x ? 1 : -1, l = e.y < n.y ? -1 : 1;
  return `L ${i},${s + r * l}Q ${i},${s} ${i + r * a},${s}`;
}
function yo({ sourceX: e, sourceY: t, sourcePosition: n = K.Bottom, targetX: o, targetY: r, targetPosition: i = K.Top, borderRadius: s = 5, centerX: a, centerY: l, offset: c = 20, stepPosition: u = 0.5 }) {
  const [d, f, p, g, x] = sf({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: i,
    center: { x: a, y: l },
    offset: c,
    stepPosition: u
  });
  let w = `M${d[0].x} ${d[0].y}`;
  for (let y = 1; y < d.length - 1; y++)
    w += af(d[y - 1], d[y], d[y + 1], s);
  return w += `L${d[d.length - 1].x} ${d[d.length - 1].y}`, [w, f, p, g, x];
}
function Mr(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function cf(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!Mr(t) || !Mr(n))
    return null;
  const o = t.internals.handleBounds || Ir(t.handles), r = n.internals.handleBounds || Ir(n.handles), i = Ar(o?.source ?? [], e.sourceHandle), s = Ar(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === yt.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!i || !s)
    return e.onError?.("008", Te.error008(i ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const a = i?.position || K.Bottom, l = s?.position || K.Top, c = lt(t, i, a), u = lt(n, s, l);
  return {
    sourceX: c.x,
    sourceY: c.y,
    targetX: u.x,
    targetY: u.y,
    sourcePosition: a,
    targetPosition: l
  };
}
function Ir(e) {
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
function lt(e, t, n = K.Left, o = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, i = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: s, height: a } = t ?? Ze(e);
  if (o)
    return { x: r + s / 2, y: i + a / 2 };
  switch (t?.position ?? n) {
    case K.Top:
      return { x: r + s / 2, y: i };
    case K.Right:
      return { x: r + s, y: i + a / 2 };
    case K.Bottom:
      return { x: r + s / 2, y: i + a };
    case K.Left:
      return { x: r, y: i + a / 2 };
  }
}
function Ar(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function xo(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function lf(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const i = /* @__PURE__ */ new Set();
  return e.reduce((s, a) => ([a.markerStart || o, a.markerEnd || r].forEach((l) => {
    if (l && typeof l == "object") {
      const c = xo(l, t);
      i.has(c) || (s.push({ id: c, color: l.color || n, ...l }), i.add(c));
    }
  }), s), []).sort((s, a) => s.id.localeCompare(a.id));
}
const us = 1e3, uf = 10, Lo = {
  nodeOrigin: [0, 0],
  nodeExtent: Ot,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, df = {
  ...Lo,
  checkEquality: !0
};
function Ho(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function ff(e, t, n) {
  const o = Ho(Lo, n);
  for (const r of e.values())
    if (r.parentId)
      Vo(r, e, t, o);
    else {
      const i = Gt(r, o.nodeOrigin), s = ct(r.extent) ? r.extent : o.nodeExtent, a = at(i, s, Ze(r));
      r.internals.positionAbsolute = a;
    }
}
function hf(e, t) {
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
function Oo(e) {
  return e === "manual";
}
function wo(e, t, n, o = {}) {
  const r = Ho(df, o), i = { i: 0 }, s = new Map(t), a = r?.elevateNodesOnSelect && !Oo(r.zIndexMode) ? us : 0;
  let l = e.length > 0, c = !1;
  t.clear(), n.clear();
  for (const u of e) {
    let d = s.get(u.id);
    if (r.checkEquality && u === d?.internals.userNode)
      t.set(u.id, d);
    else {
      const f = Gt(u, r.nodeOrigin), p = ct(u.extent) ? u.extent : r.nodeExtent, g = at(f, p, Ze(u));
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
          handleBounds: hf(u, d),
          z: ds(u, a, r.zIndexMode),
          userNode: u
        }
      }, t.set(u.id, d);
    }
    (d.measured === void 0 || d.measured.width === void 0 || d.measured.height === void 0) && !d.hidden && (l = !1), u.parentId && Vo(d, t, n, o, i), c ||= u.selected ?? !1;
  }
  return { nodesInitialized: l, hasSelectedNodes: c };
}
function pf(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function Vo(e, t, n, o, r) {
  const { elevateNodesOnSelect: i, nodeOrigin: s, nodeExtent: a, zIndexMode: l } = Ho(Lo, o), c = e.parentId, u = t.get(c);
  if (!u) {
    console.warn(`Parent node ${c} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  pf(e, n), r && !u.parentId && u.internals.rootParentIndex === void 0 && l === "auto" && (u.internals.rootParentIndex = ++r.i, u.internals.z = u.internals.z + r.i * uf), r && u.internals.rootParentIndex !== void 0 && (r.i = u.internals.rootParentIndex);
  const d = i && !Oo(l) ? us : 0, { x: f, y: p, z: g } = gf(e, u, s, a, d, l), { positionAbsolute: x } = e.internals, w = f !== x.x || p !== x.y;
  (w || g !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: w ? { x: f, y: p } : x,
      z: g
    }
  });
}
function ds(e, t, n) {
  const o = je(e.zIndex) ? e.zIndex : 0;
  return Oo(n) ? o : o + (e.selected ? t : 0);
}
function gf(e, t, n, o, r, i) {
  const { x: s, y: a } = t.internals.positionAbsolute, l = Ze(e), c = Gt(e, n), u = ct(e.extent) ? at(c, e.extent, l) : c;
  let d = at({ x: s + u.x, y: a + u.y }, o, l);
  e.extent === "parent" && (d = Qi(d, l, t));
  const f = ds(e, r, i), p = t.internals.z ?? 0;
  return {
    x: d.x,
    y: d.y,
    z: p >= f ? p + 1 : f
  };
}
function Bo(e, t, n, o = [0, 0]) {
  const r = [], i = /* @__PURE__ */ new Map();
  for (const s of e) {
    const a = t.get(s.parentId);
    if (!a)
      continue;
    const l = i.get(s.parentId)?.expandedRect ?? wt(a), c = Ji(l, s.rect);
    i.set(s.parentId, { expandedRect: c, parent: a });
  }
  return i.size > 0 && i.forEach(({ expandedRect: s, parent: a }, l) => {
    const c = a.internals.positionAbsolute, u = Ze(a), d = a.origin ?? o, f = s.x < c.x ? Math.round(Math.abs(c.x - s.x)) : 0, p = s.y < c.y ? Math.round(Math.abs(c.y - s.y)) : 0, g = Math.max(u.width, Math.round(s.width)), x = Math.max(u.height, Math.round(s.height)), w = (g - u.width) * d[0], y = (x - u.height) * d[1];
    (f > 0 || p > 0 || w || y) && (r.push({
      id: l,
      type: "position",
      position: {
        x: a.position.x - f + w,
        y: a.position.y - p + y
      }
    }), n.get(l)?.forEach((S) => {
      e.some((m) => m.id === S.id) || r.push({
        id: S.id,
        type: "position",
        position: {
          x: S.position.x + f,
          y: S.position.y + p
        }
      });
    })), (u.width < s.width || u.height < s.height || f || p) && r.push({
      id: l,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: g + (f ? d[0] * f - w : 0),
        height: x + (p ? d[1] * p - y : 0)
      }
    });
  }), r;
}
function mf(e, t, n, o, r, i, s) {
  const a = o?.querySelector(".xyflow__viewport");
  let l = !1;
  if (!a)
    return { changes: [], updatedInternals: l };
  const c = [], u = window.getComputedStyle(a), { m22: d } = new window.DOMMatrixReadOnly(u.transform), f = [];
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
    const x = Ro(p.nodeElement), w = g.measured.width !== x.width || g.measured.height !== x.height;
    if (!!(x.width && x.height && (w || !g.internals.handleBounds || p.force))) {
      const S = p.nodeElement.getBoundingClientRect(), m = ct(g.extent) ? g.extent : i;
      let { positionAbsolute: b } = g.internals;
      g.parentId && g.extent === "parent" ? b = Qi(b, x, t.get(g.parentId)) : m && (b = at(b, m, x));
      const N = {
        ...g,
        measured: x,
        internals: {
          ...g.internals,
          positionAbsolute: b,
          handleBounds: {
            source: Er("source", p.nodeElement, S, d, g.id),
            target: Er("target", p.nodeElement, S, d, g.id)
          }
        }
      };
      t.set(g.id, N), g.parentId && Vo(N, t, n, { nodeOrigin: r, zIndexMode: s }), l = !0, w && (c.push({
        id: g.id,
        type: "dimensions",
        dimensions: x
      }), g.expandParent && g.parentId && f.push({
        id: g.id,
        parentId: g.parentId,
        rect: wt(N, r)
      }));
    }
  }
  if (f.length > 0) {
    const p = Bo(f, t, n, r);
    c.push(...p);
  }
  return { changes: c, updatedInternals: l };
}
async function yf({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: i }) {
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
function Dr(e, t, n, o, r, i) {
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
function fs(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: i, sourceHandle: s = null, targetHandle: a = null } = o, l = { edgeId: o.id, source: r, target: i, sourceHandle: s, targetHandle: a }, c = `${r}-${s}--${i}-${a}`, u = `${i}-${a}--${r}-${s}`;
    Dr("source", l, u, e, r, s), Dr("target", l, c, e, i, a), t.set(o.id, o);
  }
}
function hs(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : hs(n, t) : !1;
}
function Pr(e, t, n) {
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
function xf(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [i, s] of e)
    if ((s.selected || s.id === o) && (!s.parentId || !hs(s, e)) && (s.draggable || t && typeof s.draggable > "u")) {
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
function Un({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
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
function wf({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const i = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, s = Qt(i, t);
  return {
    x: s.x - i.x,
    y: s.y - i.y
  };
}
function vf({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let i = { x: null, y: null }, s = 0, a = /* @__PURE__ */ new Map(), l = !1, c = { x: 0, y: 0 }, u = null, d = !1, f = null, p = !1, g = !1, x = null;
  function w({ noDragClassName: S, handleSelector: m, domNode: b, isSelectable: N, nodeId: _, nodeClickDistance: C = 0 }) {
    f = _e(b);
    function A({ x: $, y: H }) {
      const { nodeLookup: v, nodeExtent: k, snapGrid: E, snapToGrid: M, nodeOrigin: j, onNodeDrag: P, onSelectionDrag: L, onError: V, updateNodePositions: O } = t();
      i = { x: $, y: H };
      let Y = !1;
      const W = a.size > 1, Q = W && k ? mo(Kt(a)) : null, ne = W && M ? wf({
        dragItems: a,
        snapGrid: E,
        x: $,
        y: H
      }) : null;
      for (const [Z, z] of a) {
        if (!v.has(Z))
          continue;
        let X = { x: $ - z.distance.x, y: H - z.distance.y };
        M && (X = ne ? {
          x: Math.round(X.x + ne.x),
          y: Math.round(X.y + ne.y)
        } : Qt(X, E));
        let te = null;
        if (W && k && !z.extent && Q) {
          const { positionAbsolute: J } = z.internals, re = J.x - Q.x + k[0][0], R = J.x + z.measured.width - Q.x2 + k[1][0], G = J.y - Q.y + k[0][1], ue = J.y + z.measured.height - Q.y2 + k[1][1];
          te = [
            [re, G],
            [R, ue]
          ];
        }
        const { position: ee, positionAbsolute: U } = Ki({
          nodeId: Z,
          nextPosition: X,
          nodeLookup: v,
          nodeExtent: te || k,
          nodeOrigin: j,
          onError: V
        });
        Y = Y || z.position.x !== ee.x || z.position.y !== ee.y, z.position = ee, z.internals.positionAbsolute = U;
      }
      if (g = g || Y, !!Y && (O(a, !0), x && (o || P || !_ && L))) {
        const [Z, z] = Un({
          nodeId: _,
          dragItems: a,
          nodeLookup: v
        });
        o?.(x, a, Z, z), P?.(x, Z, z), _ || L?.(x, z);
      }
    }
    async function D() {
      if (!u)
        return;
      const { transform: $, panBy: H, autoPanSpeed: v, autoPanOnNodeDrag: k } = t();
      if (!k) {
        l = !1, cancelAnimationFrame(s);
        return;
      }
      const [E, M] = To(c, u, v);
      (E !== 0 || M !== 0) && (i.x = (i.x ?? 0) - E / $[2], i.y = (i.y ?? 0) - M / $[2], await H({ x: E, y: M }) && A(i)), s = requestAnimationFrame(D);
    }
    function B($) {
      const { nodeLookup: H, multiSelectionActive: v, nodesDraggable: k, transform: E, snapGrid: M, snapToGrid: j, selectNodesOnDrag: P, onNodeDragStart: L, onSelectionDragStart: V, unselectNodesAndEdges: O } = t();
      d = !0, (!P || !N) && !v && _ && (H.get(_)?.selected || O()), N && P && _ && e?.(_);
      const Y = Tt($.sourceEvent, { transform: E, snapGrid: M, snapToGrid: j, containerBounds: u });
      if (i = Y, a = xf(H, k, Y, _), a.size > 0 && (n || L || !_ && V)) {
        const [W, Q] = Un({
          nodeId: _,
          dragItems: a,
          nodeLookup: H
        });
        n?.($.sourceEvent, a, W, Q), L?.($.sourceEvent, W, Q), _ || V?.($.sourceEvent, Q);
      }
    }
    const I = Ai().clickDistance(C).on("start", ($) => {
      const { domNode: H, nodeDragThreshold: v, transform: k, snapGrid: E, snapToGrid: M } = t();
      u = H?.getBoundingClientRect() || null, p = !1, g = !1, x = $.sourceEvent, v === 0 && B($), i = Tt($.sourceEvent, { transform: k, snapGrid: E, snapToGrid: M, containerBounds: u }), c = $e($.sourceEvent, u);
    }).on("drag", ($) => {
      const { autoPanOnNodeDrag: H, transform: v, snapGrid: k, snapToGrid: E, nodeDragThreshold: M, nodeLookup: j } = t(), P = Tt($.sourceEvent, { transform: v, snapGrid: k, snapToGrid: E, containerBounds: u });
      if (x = $.sourceEvent, ($.sourceEvent.type === "touchmove" && $.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      _ && !j.has(_)) && (p = !0), !p) {
        if (!l && H && d && (l = !0, D()), !d) {
          const L = $e($.sourceEvent, u), V = L.x - c.x, O = L.y - c.y;
          Math.sqrt(V * V + O * O) > M && B($);
        }
        (i.x !== P.xSnapped || i.y !== P.ySnapped) && a && d && (c = $e($.sourceEvent, u), A(P));
      }
    }).on("end", ($) => {
      if (!d || p) {
        p && a.size > 0 && t().updateNodePositions(a, !1);
        return;
      }
      if (l = !1, d = !1, cancelAnimationFrame(s), a.size > 0) {
        const { nodeLookup: H, updateNodePositions: v, onNodeDragStop: k, onSelectionDragStop: E } = t();
        if (g && (v(a, !1), g = !1), r || k || !_ && E) {
          const [M, j] = Un({
            nodeId: _,
            dragItems: a,
            nodeLookup: H,
            dragging: !1
          });
          r?.($.sourceEvent, a, M, j), k?.($.sourceEvent, M, j), _ || E?.($.sourceEvent, j);
        }
      }
    }).filter(($) => {
      const H = $.target;
      return !$.button && (!S || !Pr(H, `.${S}`, b)) && (!m || Pr(H, m, b));
    });
    f.call(I);
  }
  function y() {
    f?.on(".drag", null);
  }
  return {
    update: w,
    destroy: y
  };
}
function bf(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const i of t.values())
    Bt(r, wt(i)) > 0 && o.push(i);
  return o;
}
const Sf = 250;
function _f(e, t, n, o) {
  let r = [], i = 1 / 0;
  const s = bf(e, n, t + Sf);
  for (const a of s) {
    const l = [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []];
    for (const c of l) {
      if (o.nodeId === c.nodeId && o.type === c.type && o.id === c.id)
        continue;
      const { x: u, y: d } = lt(a, c, c.position, !0), f = Math.sqrt(Math.pow(u - e.x, 2) + Math.pow(d - e.y, 2));
      f > t || (f < i ? (r = [{ ...c, x: u, y: d }], i = f) : f === i && r.push({ ...c, x: u, y: d }));
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
function ps(e, t, n, o, r, i = !1) {
  const s = o.get(e);
  if (!s)
    return null;
  const a = r === "strict" ? s.internals.handleBounds?.[t] : [...s.internals.handleBounds?.source ?? [], ...s.internals.handleBounds?.target ?? []], l = (n ? a?.find((c) => c.id === n) : a?.[0]) ?? null;
  return l && i ? { ...l, ...lt(s, l, l.position, !0) } : l;
}
function gs(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Ef(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const ms = () => !0;
function Nf(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: i, isTarget: s, domNode: a, nodeLookup: l, lib: c, autoPanOnConnect: u, flowId: d, panBy: f, cancelConnection: p, onConnectStart: g, onConnect: x, onConnectEnd: w, isValidConnection: y = ms, onReconnectEnd: S, updateConnection: m, getTransform: b, getFromHandle: N, autoPanSpeed: _, dragThreshold: C = 1, handleDomNode: A }) {
  const D = os(e.target);
  let B = 0, I;
  const { x: $, y: H } = $e(e), v = gs(i, A), k = a?.getBoundingClientRect();
  let E = !1;
  if (!k || !v)
    return;
  const M = ps(r, v, o, l, t);
  if (!M)
    return;
  let j = $e(e, k), P = !1, L = null, V = !1, O = null;
  function Y() {
    if (!u || !k)
      return;
    const [ee, U] = To(j, k, _);
    f({ x: ee, y: U }), B = requestAnimationFrame(Y);
  }
  const W = {
    ...M,
    nodeId: r,
    type: v,
    position: M.position
  }, Q = l.get(r);
  let Z = {
    inProgress: !0,
    isValid: null,
    from: lt(Q, W, K.Left, !0),
    fromHandle: W,
    fromPosition: W.position,
    fromNode: Q,
    to: j,
    toHandle: null,
    toPosition: vr[W.position],
    toNode: null,
    pointer: j
  };
  function z() {
    E = !0, m(Z), g?.(e, { nodeId: r, handleId: o, handleType: v });
  }
  C === 0 && z();
  function X(ee) {
    if (!E) {
      const { x: ue, y: he } = $e(ee), Ne = ue - $, Ce = he - H;
      if (!(Ne * Ne + Ce * Ce > C * C))
        return;
      z();
    }
    if (!N() || !W) {
      te(ee);
      return;
    }
    const U = b();
    j = $e(ee, k), I = _f(Ct(j, U, !1, [1, 1]), n, l, W), P || (Y(), P = !0);
    const J = ys(ee, {
      handle: I,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: s ? "target" : "source",
      isValidConnection: y,
      doc: D,
      lib: c,
      flowId: d,
      nodeLookup: l
    });
    O = J.handleDomNode, L = J.connection, V = Ef(!!I, J.isValid);
    const re = l.get(r), R = re ? lt(re, W, K.Left, !0) : Z.from, G = {
      ...Z,
      from: R,
      isValid: V,
      to: J.toHandle && V ? vt({ x: J.toHandle.x, y: J.toHandle.y }, U) : j,
      toHandle: J.toHandle,
      toPosition: V && J.toHandle ? J.toHandle.position : vr[W.position],
      toNode: J.toHandle ? l.get(J.toHandle.nodeId) : null,
      pointer: j
    };
    m(G), Z = G;
  }
  function te(ee) {
    if (!("touches" in ee && ee.touches.length > 0)) {
      if (E) {
        (I || O) && L && V && x?.(L);
        const { inProgress: U, ...J } = Z, re = {
          ...J,
          toPosition: Z.toHandle ? Z.toPosition : null
        };
        w?.(ee, re), i && S?.(ee, re);
      }
      p(), cancelAnimationFrame(B), P = !1, V = !1, L = null, O = null, D.removeEventListener("mousemove", X), D.removeEventListener("mouseup", te), D.removeEventListener("touchmove", X), D.removeEventListener("touchend", te);
    }
  }
  D.addEventListener("mousemove", X), D.addEventListener("mouseup", te), D.addEventListener("touchmove", X), D.addEventListener("touchend", te);
}
function ys(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: i, doc: s, lib: a, flowId: l, isValidConnection: c = ms, nodeLookup: u }) {
  const d = i === "target", f = t ? s.querySelector(`.${a}-flow__handle[data-id="${l}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: p, y: g } = $e(e), x = s.elementFromPoint(p, g), w = x?.classList.contains(`${a}-flow__handle`) ? x : f, y = {
    handleDomNode: w,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (w) {
    const S = gs(void 0, w), m = w.getAttribute("data-nodeid"), b = w.getAttribute("data-handleid"), N = w.classList.contains("connectable"), _ = w.classList.contains("connectableend");
    if (!m || !S)
      return y;
    const C = {
      source: d ? m : o,
      sourceHandle: d ? b : r,
      target: d ? o : m,
      targetHandle: d ? r : b
    };
    y.connection = C;
    const D = N && _ && (n === yt.Strict ? d && S === "source" || !d && S === "target" : m !== o || b !== r);
    y.isValid = D && c(C), y.toHandle = ps(m, S, b, u, n, !0);
  }
  return y;
}
const vo = {
  onPointerDown: Nf,
  isValid: ys
};
function Cf({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = _e(e);
  function i({ translateExtent: a, width: l, height: c, zoomStep: u = 1, pannable: d = !0, zoomable: f = !0, inversePan: p = !1 }) {
    const g = (m) => {
      if (m.sourceEvent.type !== "wheel" || !t)
        return;
      const b = n(), N = m.sourceEvent.ctrlKey && Ft() ? 10 : 1, _ = -m.sourceEvent.deltaY * (m.sourceEvent.deltaMode === 1 ? 0.05 : m.sourceEvent.deltaMode ? 1 : 2e-3) * u, C = b[2] * Math.pow(2, _ * N);
      t.scaleTo(C);
    };
    let x = [0, 0];
    const w = (m) => {
      (m.sourceEvent.type === "mousedown" || m.sourceEvent.type === "touchstart") && (x = [
        m.sourceEvent.clientX ?? m.sourceEvent.touches[0].clientX,
        m.sourceEvent.clientY ?? m.sourceEvent.touches[0].clientY
      ]);
    }, y = (m) => {
      const b = n();
      if (m.sourceEvent.type !== "mousemove" && m.sourceEvent.type !== "touchmove" || !t)
        return;
      const N = [
        m.sourceEvent.clientX ?? m.sourceEvent.touches[0].clientX,
        m.sourceEvent.clientY ?? m.sourceEvent.touches[0].clientY
      ], _ = [N[0] - x[0], N[1] - x[1]];
      x = N;
      const C = o() * Math.max(b[2], Math.log(b[2])) * (p ? -1 : 1), A = {
        x: b[0] - _[0] * C,
        y: b[1] - _[1] * C
      }, D = [
        [0, 0],
        [l, c]
      ];
      t.setViewportConstrained({
        x: A.x,
        y: A.y,
        zoom: b[2]
      }, D, a);
    }, S = Xi().on("start", w).on("zoom", d ? y : null).on("zoom.wheel", f ? g : null);
    r.call(S, {});
  }
  function s() {
    r.on("zoom", null);
  }
  return {
    update: i,
    destroy: s,
    pointer: De
  };
}
const Tn = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), Gn = ({ x: e, y: t, zoom: n }) => Pn.translate(e, t).scale(n), dt = (e, t) => e.target.closest(`.${t}`), xs = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), kf = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, Kn = (e, t = 0, n = kf, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, ws = (e) => {
  const t = e.ctrlKey && Ft() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Mf({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: i, zoomOnPinch: s, onPanZoomStart: a, onPanZoom: l, onPanZoomEnd: c }) {
  return (u) => {
    if (dt(u, t))
      return u.ctrlKey && u.preventDefault(), !1;
    u.preventDefault(), u.stopImmediatePropagation();
    const d = n.property("__zoom").k || 1;
    if (u.ctrlKey && s) {
      const w = De(u), y = ws(u), S = d * Math.pow(2, y);
      o.scaleTo(n, S, w, u);
      return;
    }
    const f = u.deltaMode === 1 ? 20 : 1;
    let p = r === rt.Vertical ? 0 : u.deltaX * f, g = r === rt.Horizontal ? 0 : u.deltaY * f;
    !Ft() && u.shiftKey && r !== rt.Vertical && (p = u.deltaY * f, g = 0), o.translateBy(
      n,
      -(p / d) * i,
      -(g / d) * i,
      // @ts-ignore
      { internal: !0 }
    );
    const x = Tn(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (l?.(u, x), e.panScrollTimeout = setTimeout(() => {
      c?.(u, x), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, a?.(u, x));
  };
}
function If({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const i = o.type === "wheel", s = !t && i && !o.ctrlKey, a = dt(o, e);
    if (o.ctrlKey && i && a && o.preventDefault(), s || a)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function Af({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = Tn(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function Df({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (i) => {
    e.usedRightMouseButton = !!(n && xs(t, e.mouseButton ?? 0)), i.sourceEvent?.sync || o([i.transform.x, i.transform.y, i.transform.k]), r && !i.sourceEvent?.internal && r?.(i.sourceEvent, Tn(i.transform));
  };
}
function Pf({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: i }) {
  return (s) => {
    if (!s.sourceEvent?.internal && (e.isZoomingOrPanning = !1, i && xs(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && s.sourceEvent && i(s.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
      const a = Tn(s.transform);
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
function jf({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: i, userSelectionActive: s, noWheelClassName: a, noPanClassName: l, lib: c, connectionInProgress: u }) {
  return (d) => {
    const f = e || t, p = n && d.ctrlKey, g = d.type === "wheel";
    if (d.button === 1 && d.type === "mousedown" && (dt(d, `${c}-flow__node`) || dt(d, `${c}-flow__edge`)))
      return !0;
    if (!o && !f && !r && !i && !n || s || u && !g || dt(d, a) && g || dt(d, l) && (!g || r && g && !e) || !n && d.ctrlKey && g)
      return !1;
    if (!n && d.type === "touchstart" && d.touches?.length > 1)
      return d.preventDefault(), !1;
    if (!f && !r && !p && g || !o && (d.type === "mousedown" || d.type === "touchstart") || Array.isArray(o) && !o.includes(d.button) && d.type === "mousedown")
      return !1;
    const x = Array.isArray(o) && o.includes(d.button) || !d.button || d.button <= 1;
    return (!d.ctrlKey || g) && x;
  };
}
function $f({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: i, onPanZoomStart: s, onPanZoomEnd: a, onDraggingChange: l }) {
  const c = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, u = e.getBoundingClientRect(), d = Xi().scaleExtent([t, n]).translateExtent(o), f = _e(e).call(d);
  S({
    x: r.x,
    y: r.y,
    zoom: xt(r.zoom, t, n)
  }, [
    [0, 0],
    [u.width, u.height]
  ], o);
  const p = f.on("wheel.zoom"), g = f.on("dblclick.zoom");
  d.wheelDelta(ws);
  async function x(I, $) {
    return f ? new Promise((H) => {
      d?.interpolate($?.interpolate === "linear" ? $t : dn).transform(Kn(f, $?.duration, $?.ease, () => H(!0)), I);
    }) : !1;
  }
  function w({ noWheelClassName: I, noPanClassName: $, onPaneContextMenu: H, userSelectionActive: v, panOnScroll: k, panOnDrag: E, panOnScrollMode: M, panOnScrollSpeed: j, preventScrolling: P, zoomOnPinch: L, zoomOnScroll: V, zoomOnDoubleClick: O, zoomActivationKeyPressed: Y, lib: W, onTransformChange: Q, connectionInProgress: ne, paneClickDistance: Z, selectionOnDrag: z }) {
    v && !c.isZoomingOrPanning && y();
    const X = k && !Y && !v;
    d.clickDistance(z ? 1 / 0 : !je(Z) || Z < 0 ? 0 : Z);
    const te = X ? Mf({
      zoomPanValues: c,
      noWheelClassName: I,
      d3Selection: f,
      d3Zoom: d,
      panOnScrollMode: M,
      panOnScrollSpeed: j,
      zoomOnPinch: L,
      onPanZoomStart: s,
      onPanZoom: i,
      onPanZoomEnd: a
    }) : If({
      noWheelClassName: I,
      preventScrolling: P,
      d3ZoomHandler: p
    });
    f.on("wheel.zoom", te, { passive: !1 });
    const ee = Af({
      zoomPanValues: c,
      onDraggingChange: l,
      onPanZoomStart: s
    });
    d.on("start", ee);
    const U = Df({
      zoomPanValues: c,
      panOnDrag: E,
      onPaneContextMenu: !!H,
      onPanZoom: i,
      onTransformChange: Q
    });
    d.on("zoom", U);
    const J = Pf({
      zoomPanValues: c,
      panOnDrag: E,
      panOnScroll: k,
      onPaneContextMenu: H,
      onPanZoomEnd: a,
      onDraggingChange: l
    });
    d.on("end", J);
    const re = jf({
      zoomActivationKeyPressed: Y,
      panOnDrag: E,
      zoomOnScroll: V,
      panOnScroll: k,
      zoomOnDoubleClick: O,
      zoomOnPinch: L,
      userSelectionActive: v,
      noPanClassName: $,
      noWheelClassName: I,
      lib: W,
      connectionInProgress: ne
    });
    d.filter(re), O ? f.on("dblclick.zoom", g) : f.on("dblclick.zoom", null);
  }
  function y() {
    d.on("zoom", null);
  }
  async function S(I, $, H) {
    const v = Gn(I), k = d?.constrain()(v, $, H);
    return k && await x(k), k;
  }
  async function m(I, $) {
    const H = Gn(I);
    return await x(H, $), H;
  }
  function b(I) {
    if (f) {
      const $ = Gn(I), H = f.property("__zoom");
      (H.k !== I.zoom || H.x !== I.x || H.y !== I.y) && d?.transform(f, $, null, { sync: !0 });
    }
  }
  function N() {
    const I = f ? Yi(f.node()) : { x: 0, y: 0, k: 1 };
    return { x: I.x, y: I.y, zoom: I.k };
  }
  async function _(I, $) {
    return f ? new Promise((H) => {
      d?.interpolate($?.interpolate === "linear" ? $t : dn).scaleTo(Kn(f, $?.duration, $?.ease, () => H(!0)), I);
    }) : !1;
  }
  async function C(I, $) {
    return f ? new Promise((H) => {
      d?.interpolate($?.interpolate === "linear" ? $t : dn).scaleBy(Kn(f, $?.duration, $?.ease, () => H(!0)), I);
    }) : !1;
  }
  function A(I) {
    d?.scaleExtent(I);
  }
  function D(I) {
    d?.translateExtent(I);
  }
  function B(I) {
    const $ = !je(I) || I < 0 ? 0 : I;
    d?.clickDistance($);
  }
  return {
    update: w,
    destroy: y,
    setViewport: m,
    setViewportConstrained: S,
    getViewport: N,
    scaleTo: _,
    scaleBy: C,
    setScaleExtent: A,
    setTranslateExtent: D,
    syncViewport: b,
    setClickDistance: B
  };
}
var bt;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(bt || (bt = {}));
function Tf({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: i }) {
  const s = e - t, a = n - o, l = [s > 0 ? 1 : s < 0 ? -1 : 0, a > 0 ? 1 : a < 0 ? -1 : 0];
  return s && r && (l[0] = l[0] * -1), a && i && (l[1] = l[1] * -1), l;
}
function jr(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: r
  };
}
function Ue(e, t) {
  return Math.max(0, t - e);
}
function Ge(e, t) {
  return Math.max(0, e - t);
}
function an(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function $r(e, t) {
  return e ? !t : t;
}
function zf(e, t, n, o, r, i, s, a) {
  let { affectsX: l, affectsY: c } = t;
  const { isHorizontal: u, isVertical: d } = t, f = u && d, { xSnapped: p, ySnapped: g } = n, { minWidth: x, maxWidth: w, minHeight: y, maxHeight: S } = o, { x: m, y: b, width: N, height: _, aspectRatio: C } = e;
  let A = Math.floor(u ? p - e.pointerX : 0), D = Math.floor(d ? g - e.pointerY : 0);
  const B = N + (l ? -A : A), I = _ + (c ? -D : D), $ = -i[0] * N, H = -i[1] * _;
  let v = an(B, x, w), k = an(I, y, S);
  if (s) {
    let j = 0, P = 0;
    l && A < 0 ? j = Ue(m + A + $, s[0][0]) : !l && A > 0 && (j = Ge(m + B + $, s[1][0])), c && D < 0 ? P = Ue(b + D + H, s[0][1]) : !c && D > 0 && (P = Ge(b + I + H, s[1][1])), v = Math.max(v, j), k = Math.max(k, P);
  }
  if (a) {
    let j = 0, P = 0;
    l && A > 0 ? j = Ge(m + A, a[0][0]) : !l && A < 0 && (j = Ue(m + B, a[1][0])), c && D > 0 ? P = Ge(b + D, a[0][1]) : !c && D < 0 && (P = Ue(b + I, a[1][1])), v = Math.max(v, j), k = Math.max(k, P);
  }
  if (r) {
    if (u) {
      const j = an(B / C, y, S) * C;
      if (v = Math.max(v, j), s) {
        let P = 0;
        !l && !c || l && !c && f ? P = Ge(b + H + B / C, s[1][1]) * C : P = Ue(b + H + (l ? A : -A) / C, s[0][1]) * C, v = Math.max(v, P);
      }
      if (a) {
        let P = 0;
        !l && !c || l && !c && f ? P = Ue(b + B / C, a[1][1]) * C : P = Ge(b + (l ? A : -A) / C, a[0][1]) * C, v = Math.max(v, P);
      }
    }
    if (d) {
      const j = an(I * C, x, w) / C;
      if (k = Math.max(k, j), s) {
        let P = 0;
        !l && !c || c && !l && f ? P = Ge(m + I * C + $, s[1][0]) / C : P = Ue(m + (c ? D : -D) * C + $, s[0][0]) / C, k = Math.max(k, P);
      }
      if (a) {
        let P = 0;
        !l && !c || c && !l && f ? P = Ue(m + I * C, a[1][0]) / C : P = Ge(m + (c ? D : -D) * C, a[0][0]) / C, k = Math.max(k, P);
      }
    }
  }
  D = D + (D < 0 ? k : -k), A = A + (A < 0 ? v : -v), r && (f ? B > I * C ? D = ($r(l, c) ? -A : A) / C : A = ($r(l, c) ? -D : D) * C : u ? (D = A / C, c = l) : (A = D * C, l = c));
  const E = l ? m + A : m, M = c ? b + D : b;
  return {
    width: N + (l ? -A : A),
    height: _ + (c ? -D : D),
    x: i[0] * A * (l ? -1 : 1) + E,
    y: i[1] * D * (c ? -1 : 1) + M
  };
}
const vs = { width: 0, height: 0, x: 0, y: 0 }, Rf = {
  ...vs,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function Lf(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, i = e.measured.width ?? 0, s = e.measured.height ?? 0, a = n[0] * i, l = n[1] * s;
  return [
    [o - a, r - l],
    [o + i - a, r + s - l]
  ];
}
function Hf({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const i = _e(e);
  let s = {
    controlDirection: jr("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function a({ controlPosition: c, boundaries: u, keepAspectRatio: d, resizeDirection: f, onResizeStart: p, onResize: g, onResizeEnd: x, shouldResize: w }) {
    let y = { ...vs }, S = { ...Rf };
    s = {
      boundaries: u,
      resizeDirection: f,
      keepAspectRatio: d,
      controlDirection: jr(c)
    };
    let m, b = null, N = [], _, C, A, D = !1;
    const B = Ai().on("start", (I) => {
      const { nodeLookup: $, transform: H, snapGrid: v, snapToGrid: k, nodeOrigin: E, paneDomNode: M } = n();
      if (m = $.get(t), !m)
        return;
      b = M?.getBoundingClientRect() ?? null;
      const { xSnapped: j, ySnapped: P } = Tt(I.sourceEvent, {
        transform: H,
        snapGrid: v,
        snapToGrid: k,
        containerBounds: b
      });
      y = {
        width: m.measured.width ?? 0,
        height: m.measured.height ?? 0,
        x: m.position.x ?? 0,
        y: m.position.y ?? 0
      }, S = {
        ...y,
        pointerX: j,
        pointerY: P,
        aspectRatio: y.width / y.height
      }, _ = void 0, C = ct(m.extent) ? m.extent : void 0, m.parentId && (m.extent === "parent" || m.expandParent) && (_ = $.get(m.parentId)), _ && m.extent === "parent" && (C = [
        [0, 0],
        [_.measured.width, _.measured.height]
      ]), N = [], A = void 0;
      for (const [L, V] of $)
        if (V.parentId === t && (N.push({
          id: L,
          position: { ...V.position },
          extent: V.extent
        }), V.extent === "parent" || V.expandParent)) {
          const O = Lf(V, m, V.origin ?? E);
          A ? A = [
            [Math.min(O[0][0], A[0][0]), Math.min(O[0][1], A[0][1])],
            [Math.max(O[1][0], A[1][0]), Math.max(O[1][1], A[1][1])]
          ] : A = O;
        }
      p?.(I, { ...y });
    }).on("drag", (I) => {
      const { transform: $, snapGrid: H, snapToGrid: v, nodeOrigin: k } = n(), E = Tt(I.sourceEvent, {
        transform: $,
        snapGrid: H,
        snapToGrid: v,
        containerBounds: b
      }), M = [];
      if (!m)
        return;
      const { x: j, y: P, width: L, height: V } = y, O = {}, Y = m.origin ?? k, { width: W, height: Q, x: ne, y: Z } = zf(S, s.controlDirection, E, s.boundaries, s.keepAspectRatio, Y, C, A), z = W !== L, X = Q !== V, te = ne !== j && z, ee = Z !== P && X;
      if (!te && !ee && !z && !X)
        return;
      if ((te || ee || Y[0] === 1 || Y[1] === 1) && (O.x = te ? ne : y.x, O.y = ee ? Z : y.y, y.x = O.x, y.y = O.y, N.length > 0)) {
        const R = ne - j, G = Z - P;
        for (const ue of N)
          ue.position = {
            x: ue.position.x - R + Y[0] * (W - L),
            y: ue.position.y - G + Y[1] * (Q - V)
          }, M.push(ue);
      }
      if ((z || X) && (O.width = z && (!s.resizeDirection || s.resizeDirection === "horizontal") ? W : y.width, O.height = X && (!s.resizeDirection || s.resizeDirection === "vertical") ? Q : y.height, y.width = O.width, y.height = O.height), _ && m.expandParent) {
        const R = Y[0] * (O.width ?? 0);
        O.x && O.x < R && (y.x = R, S.x = S.x - (O.x - R));
        const G = Y[1] * (O.height ?? 0);
        O.y && O.y < G && (y.y = G, S.y = S.y - (O.y - G));
      }
      const U = Tf({
        width: y.width,
        prevWidth: L,
        height: y.height,
        prevHeight: V,
        affectsX: s.controlDirection.affectsX,
        affectsY: s.controlDirection.affectsY
      }), J = { ...y, direction: U };
      w?.(I, J) !== !1 && (D = !0, g?.(I, J), o(O, M));
    }).on("end", (I) => {
      D && (x?.(I, { ...y }), r?.({ ...y }), D = !1);
    });
    i.call(B);
  }
  function l() {
    i.on(".drag", null);
  }
  return {
    update: a,
    destroy: l
  };
}
var Qn = { exports: {} }, Jn = {}, eo = { exports: {} }, to = {};
var Tr;
function Of() {
  if (Tr) return to;
  Tr = 1;
  var e = Wt;
  function t(d, f) {
    return d === f && (d !== 0 || 1 / d === 1 / f) || d !== d && f !== f;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, i = e.useLayoutEffect, s = e.useDebugValue;
  function a(d, f) {
    var p = f(), g = o({ inst: { value: p, getSnapshot: f } }), x = g[0].inst, w = g[1];
    return i(
      function() {
        x.value = p, x.getSnapshot = f, l(x) && w({ inst: x });
      },
      [d, p, f]
    ), r(
      function() {
        return l(x) && w({ inst: x }), d(function() {
          l(x) && w({ inst: x });
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
  function c(d, f) {
    return f();
  }
  var u = typeof window > "u" || typeof window.document > "u" || typeof window.document.createElement > "u" ? c : a;
  return to.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : u, to;
}
var zr;
function Vf() {
  return zr || (zr = 1, eo.exports = Of()), eo.exports;
}
var Rr;
function Bf() {
  if (Rr) return Jn;
  Rr = 1;
  var e = Wt, t = Vf();
  function n(c, u) {
    return c === u && (c !== 0 || 1 / c === 1 / u) || c !== c && u !== u;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, i = e.useRef, s = e.useEffect, a = e.useMemo, l = e.useDebugValue;
  return Jn.useSyncExternalStoreWithSelector = function(c, u, d, f, p) {
    var g = i(null);
    if (g.current === null) {
      var x = { hasValue: !1, value: null };
      g.current = x;
    } else x = g.current;
    g = a(
      function() {
        function y(_) {
          if (!S) {
            if (S = !0, m = _, _ = f(_), p !== void 0 && x.hasValue) {
              var C = x.value;
              if (p(C, _))
                return b = C;
            }
            return b = _;
          }
          if (C = b, o(m, _)) return C;
          var A = f(_);
          return p !== void 0 && p(C, A) ? (m = _, C) : (m = _, b = A);
        }
        var S = !1, m, b, N = d === void 0 ? null : d;
        return [
          function() {
            return y(u());
          },
          N === null ? void 0 : function() {
            return y(N());
          }
        ];
      },
      [u, d, f, p]
    );
    var w = r(c, g[0], g[1]);
    return s(
      function() {
        x.hasValue = !0, x.value = w;
      },
      [w]
    ), l(w), w;
  }, Jn;
}
var Lr;
function Ff() {
  return Lr || (Lr = 1, Qn.exports = Bf()), Qn.exports;
}
var Yf = Ff();
const Xf = /* @__PURE__ */ sc(Yf), Wf = {}, Hr = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (u, d) => {
    const f = typeof u == "function" ? u(t) : u;
    if (!Object.is(f, t)) {
      const p = t;
      t = d ?? (typeof f != "object" || f === null) ? f : Object.assign({}, t, f), n.forEach((g) => g(t, p));
    }
  }, r = () => t, l = { setState: o, getState: r, getInitialState: () => c, subscribe: (u) => (n.add(u), () => n.delete(u)), destroy: () => {
    (Wf ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, c = t = e(o, r, l);
  return l;
}, qf = (e) => e ? Hr(e) : Hr, { useDebugValue: Zf } = Wt, { useSyncExternalStoreWithSelector: Uf } = Xf, Gf = (e) => e;
function bs(e, t = Gf, n) {
  const o = Uf(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return Zf(o), o;
}
const Or = (e, t) => {
  const n = qf(e), o = (r, i = t) => bs(n, r, i);
  return Object.assign(o, n), o;
}, Kf = (e, t) => e ? Or(e, t) : Or;
function de(e, t) {
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
var no = { exports: {} }, we = {};
var Vr;
function Qf() {
  if (Vr) return we;
  Vr = 1;
  var e = Wt;
  function t(l) {
    var c = "https://react.dev/errors/" + l;
    if (1 < arguments.length) {
      c += "?args[]=" + encodeURIComponent(arguments[1]);
      for (var u = 2; u < arguments.length; u++)
        c += "&args[]=" + encodeURIComponent(arguments[u]);
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
  function i(l, c, u) {
    var d = 3 < arguments.length && arguments[3] !== void 0 ? arguments[3] : null;
    return {
      $$typeof: r,
      key: d == null ? null : "" + d,
      children: l,
      containerInfo: c,
      implementation: u
    };
  }
  var s = e.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE;
  function a(l, c) {
    if (l === "font") return "";
    if (typeof c == "string")
      return c === "use-credentials" ? c : "";
  }
  return we.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, we.createPortal = function(l, c) {
    var u = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!c || c.nodeType !== 1 && c.nodeType !== 9 && c.nodeType !== 11)
      throw Error(t(299));
    return i(l, c, null, u);
  }, we.flushSync = function(l) {
    var c = s.T, u = o.p;
    try {
      if (s.T = null, o.p = 2, l) return l();
    } finally {
      s.T = c, o.p = u, o.d.f();
    }
  }, we.preconnect = function(l, c) {
    typeof l == "string" && (c ? (c = c.crossOrigin, c = typeof c == "string" ? c === "use-credentials" ? c : "" : void 0) : c = null, o.d.C(l, c));
  }, we.prefetchDNS = function(l) {
    typeof l == "string" && o.d.D(l);
  }, we.preinit = function(l, c) {
    if (typeof l == "string" && c && typeof c.as == "string") {
      var u = c.as, d = a(u, c.crossOrigin), f = typeof c.integrity == "string" ? c.integrity : void 0, p = typeof c.fetchPriority == "string" ? c.fetchPriority : void 0;
      u === "style" ? o.d.S(
        l,
        typeof c.precedence == "string" ? c.precedence : void 0,
        {
          crossOrigin: d,
          integrity: f,
          fetchPriority: p
        }
      ) : u === "script" && o.d.X(l, {
        crossOrigin: d,
        integrity: f,
        fetchPriority: p,
        nonce: typeof c.nonce == "string" ? c.nonce : void 0
      });
    }
  }, we.preinitModule = function(l, c) {
    if (typeof l == "string")
      if (typeof c == "object" && c !== null) {
        if (c.as == null || c.as === "script") {
          var u = a(
            c.as,
            c.crossOrigin
          );
          o.d.M(l, {
            crossOrigin: u,
            integrity: typeof c.integrity == "string" ? c.integrity : void 0,
            nonce: typeof c.nonce == "string" ? c.nonce : void 0
          });
        }
      } else c == null && o.d.M(l);
  }, we.preload = function(l, c) {
    if (typeof l == "string" && typeof c == "object" && c !== null && typeof c.as == "string") {
      var u = c.as, d = a(u, c.crossOrigin);
      o.d.L(l, u, {
        crossOrigin: d,
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
  }, we.preloadModule = function(l, c) {
    if (typeof l == "string")
      if (c) {
        var u = a(c.as, c.crossOrigin);
        o.d.m(l, {
          as: typeof c.as == "string" && c.as !== "script" ? c.as : void 0,
          crossOrigin: u,
          integrity: typeof c.integrity == "string" ? c.integrity : void 0
        });
      } else o.d.m(l);
  }, we.requestFormReset = function(l) {
    o.d.r(l);
  }, we.unstable_batchedUpdates = function(l, c) {
    return l(c);
  }, we.useFormState = function(l, c, u) {
    return s.H.useFormState(l, c, u);
  }, we.useFormStatus = function() {
    return s.H.useHostTransitionStatus();
  }, we.version = "19.2.7", we;
}
var Br;
function Jf() {
  if (Br) return no.exports;
  Br = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), no.exports = Qf(), no.exports;
}
Jf();
const zn = No(null), eh = zn.Provider, Ss = Te.error001("react");
function ce(e, t) {
  const n = qt(zn);
  if (n === null)
    throw new Error(Ss);
  return bs(n, e, t);
}
function fe() {
  const e = qt(zn);
  if (e === null)
    throw new Error(Ss);
  return xe(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const Fr = { display: "none" }, th = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, _s = "react-flow__node-desc", Es = "react-flow__edge-desc", nh = "react-flow__aria-live", oh = (e) => e.ariaLiveMessage, rh = (e) => e.ariaLabelConfig;
function ih({ rfId: e }) {
  const t = ce(oh);
  return h.jsx("div", { id: `${nh}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: th, children: t });
}
function sh({ rfId: e, disableKeyboardA11y: t }) {
  const n = ce(rh);
  return h.jsxs(h.Fragment, { children: [h.jsx("div", { id: `${_s}-${e}`, style: Fr, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), h.jsx("div", { id: `${Es}-${e}`, style: Fr, children: n["edge.a11yDescription.default"] }), !t && h.jsx(ih, { rfId: e })] });
}
const Rn = kn(({ position: e = "top-left", children: t, className: n, style: o, ...r }, i) => {
  const s = `${e}`.split("-");
  return h.jsx("div", { className: ge(["react-flow__panel", n, ...s]), style: o, ref: i, ...r, children: t });
});
Rn.displayName = "Panel";
function ah({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : h.jsx(Rn, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: h.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const ch = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, cn = (e) => e.id;
function lh(e, t) {
  return de(e.selectedNodes.map(cn), t.selectedNodes.map(cn)) && de(e.selectedEdges.map(cn), t.selectedEdges.map(cn));
}
function uh({ onSelectionChange: e }) {
  const t = fe(), { selectedNodes: n, selectedEdges: o } = ce(ch, lh);
  return ae(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((i) => i(r));
  }, [n, o, e]), null;
}
const dh = (e) => !!e.onSelectionChangeHandlers;
function fh({ onSelectionChange: e }) {
  const t = ce(dh);
  return e || t ? h.jsx(uh, { onSelectionChange: e }) : null;
}
const Ns = [0, 0], hh = { x: 0, y: 0, zoom: 1 }, ph = [
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
], Yr = [...ph, "rfId"], gh = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), Xr = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: Ot,
  nodeOrigin: Ns,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function mh(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: i, setNodeExtent: s, reset: a, setDefaultNodesAndEdges: l } = ce(gh, de), c = fe();
  ae(() => (l(e.defaultNodes, e.defaultEdges), () => {
    u.current = Xr, a();
  }), []);
  const u = se(Xr);
  return ae(
    () => {
      for (const d of Yr) {
        const f = e[d], p = u.current[d];
        f !== p && (typeof e[d] > "u" || (d === "nodes" ? t(f) : d === "edges" ? n(f) : d === "minZoom" ? o(f) : d === "maxZoom" ? r(f) : d === "translateExtent" ? i(f) : d === "nodeExtent" ? s(f) : d === "ariaLabelConfig" ? c.setState({ ariaLabelConfig: Kd(f) }) : d === "fitView" ? c.setState({ fitViewQueued: f }) : d === "fitViewOptions" ? c.setState({ fitViewOptions: f }) : c.setState({ [d]: f })));
      }
      u.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    Yr.map((d) => e[d])
  ), null;
}
function Wr() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function yh(e) {
  const [t, n] = oe(e === "system" ? null : e);
  return ae(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = Wr(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : Wr()?.matches ? "dark" : "light";
}
const qr = typeof document < "u" ? document : null;
function Yt(e = null, t = { target: qr, actInsideInputWithModifier: !0 }) {
  const [n, o] = oe(!1), r = se(!1), i = se(/* @__PURE__ */ new Set([])), [s, a] = xe(() => {
    if (e !== null) {
      const c = (Array.isArray(e) ? e : [e]).filter((d) => typeof d == "string").map((d) => d.replace("+", `
`).replace(`

`, `
+`).split(`
`)), u = c.reduce((d, f) => d.concat(...f), []);
      return [c, u];
    }
    return [[], []];
  }, [e]);
  return ae(() => {
    const l = t?.target ?? qr, c = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const u = (p) => {
        if (r.current = p.ctrlKey || p.metaKey || p.shiftKey || p.altKey, (!r.current || r.current && !c) && rs(p))
          return !1;
        const x = Ur(p.code, a);
        if (i.current.add(p[x]), Zr(s, i.current, !1)) {
          const w = p.composedPath?.()?.[0] || p.target, y = w?.nodeName === "BUTTON" || w?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !y) && p.preventDefault(), o(!0);
        }
      }, d = (p) => {
        const g = Ur(p.code, a);
        Zr(s, i.current, !0) ? (o(!1), i.current.clear()) : i.current.delete(p[g]), p.key === "Meta" && i.current.clear(), r.current = !1;
      }, f = () => {
        i.current.clear(), o(!1);
      };
      return l?.addEventListener("keydown", u), l?.addEventListener("keyup", d), window.addEventListener("blur", f), window.addEventListener("contextmenu", f), () => {
        l?.removeEventListener("keydown", u), l?.removeEventListener("keyup", d), window.removeEventListener("blur", f), window.removeEventListener("contextmenu", f);
      };
    }
  }, [e, o]), n;
}
function Zr(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function Ur(e, t) {
  return t.includes(e) ? "code" : "key";
}
const xh = () => {
  const e = fe();
  return xe(() => ({
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
      const { width: o, height: r, minZoom: i, maxZoom: s, panZoom: a } = e.getState(), l = zo(t, o, r, i, s, n?.padding ?? 0.1);
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
      }, u = n.snapGrid ?? r, d = n.snapToGrid ?? i;
      return Ct(c, o, d, u);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: r, y: i } = o.getBoundingClientRect(), s = vt(t, n);
      return {
        x: s.x + r,
        y: s.y + i
      };
    }
  }), []);
};
function Cs(e, t) {
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
      wh(l, a);
    n.push(a);
  }
  return r.length && r.forEach((i) => {
    i.index !== void 0 ? n.splice(i.index, 0, { ...i.item }) : n.push({ ...i.item });
  }), n;
}
function wh(e, t) {
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
function ks(e, t) {
  return Cs(e, t);
}
function Ms(e, t) {
  return Cs(e, t);
}
function tt(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function ft(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [r, i] of e) {
    const s = t.has(r);
    !(i.selected === void 0 && !s) && i.selected !== s && (n && (i.selected = s), o.push(tt(i.id, s)));
  }
  return o;
}
function Gr({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((r) => [r.id, r]));
  for (const [r, i] of e.entries()) {
    const s = t.get(i.id), a = s?.internals?.userNode ?? s;
    a !== void 0 && a !== i && n.push({ id: i.id, item: i, type: "replace" }), a === void 0 && n.push({ item: i, type: "add", index: r });
  }
  for (const [r] of t)
    o.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function Kr(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const vh = es();
function Is(e, t, n = {}) {
  return of(e, t, {
    ...n,
    onError: n.onError ?? vh
  });
}
const Qr = (e) => Bd(e), bh = (e) => Gi(e);
function As(e) {
  return kn(e);
}
const Sh = typeof window < "u" ? ic : ae;
function Jr(e) {
  const [t, n] = oe(BigInt(0)), [o] = oe(() => _h(() => n((r) => r + BigInt(1))));
  return Sh(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function _h(e) {
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
const Ds = No(null);
function Eh({ children: e }) {
  const t = fe(), n = ye((a) => {
    const { nodes: l = [], setNodes: c, hasDefaultNodes: u, onNodesChange: d, nodeLookup: f, fitViewQueued: p, onNodesChangeMiddlewareMap: g } = t.getState();
    let x = l;
    for (const y of a)
      x = typeof y == "function" ? y(x) : y;
    let w = Gr({
      items: x,
      lookup: f
    });
    for (const y of g.values())
      w = y(w);
    u && c(x), w.length > 0 ? d?.(w) : p && window.requestAnimationFrame(() => {
      const { fitViewQueued: y, nodes: S, setNodes: m } = t.getState();
      y && m(S);
    });
  }, []), o = Jr(n), r = ye((a) => {
    const { edges: l = [], setEdges: c, hasDefaultEdges: u, onEdgesChange: d, edgeLookup: f } = t.getState();
    let p = l;
    for (const g of a)
      p = typeof g == "function" ? g(p) : g;
    u ? c(p) : d && d(Gr({
      items: p,
      lookup: f
    }));
  }, []), i = Jr(r), s = xe(() => ({ nodeQueue: o, edgeQueue: i }), []);
  return h.jsx(Ds.Provider, { value: s, children: e });
}
function Nh() {
  const e = qt(Ds);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Ch = (e) => !!e.panZoom;
function Fo() {
  const e = xh(), t = fe(), n = Nh(), o = ce(Ch), r = xe(() => {
    const i = (d) => t.getState().nodeLookup.get(d), s = (d) => {
      n.nodeQueue.push(d);
    }, a = (d) => {
      n.edgeQueue.push(d);
    }, l = (d) => {
      const { nodeLookup: f, nodeOrigin: p } = t.getState(), g = Qr(d) ? d : f.get(d.id), x = g.parentId ? ns(g.position, g.measured, g.parentId, f, p) : g.position, w = {
        ...g,
        position: x,
        width: g.measured?.width ?? g.width,
        height: g.measured?.height ?? g.height
      };
      return wt(w);
    }, c = (d, f, p = { replace: !1 }) => {
      s((g) => g.map((x) => {
        if (x.id === d) {
          const w = typeof f == "function" ? f(x) : f;
          return p.replace && Qr(w) ? w : { ...x, ...w };
        }
        return x;
      }));
    }, u = (d, f, p = { replace: !1 }) => {
      a((g) => g.map((x) => {
        if (x.id === d) {
          const w = typeof f == "function" ? f(x) : f;
          return p.replace && bh(w) ? w : { ...x, ...w };
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
        return d.map((f) => ({ ...f }));
      },
      getEdge: (d) => t.getState().edgeLookup.get(d),
      setNodes: s,
      setEdges: a,
      addNodes: (d) => {
        const f = Array.isArray(d) ? d : [d];
        n.nodeQueue.push((p) => [...p, ...f]);
      },
      addEdges: (d) => {
        const f = Array.isArray(d) ? d : [d];
        n.edgeQueue.push((p) => [...p, ...f]);
      },
      toObject: () => {
        const { nodes: d = [], edges: f = [], transform: p } = t.getState(), [g, x, w] = p;
        return {
          nodes: d.map((y) => ({ ...y })),
          edges: f.map((y) => ({ ...y })),
          viewport: {
            x: g,
            y: x,
            zoom: w
          }
        };
      },
      deleteElements: async ({ nodes: d = [], edges: f = [] }) => {
        const { nodes: p, edges: g, onNodesDelete: x, onEdgesDelete: w, triggerNodeChanges: y, triggerEdgeChanges: S, onDelete: m, onBeforeDelete: b } = t.getState(), { nodes: N, edges: _ } = await qd({
          nodesToRemove: d,
          edgesToRemove: f,
          nodes: p,
          edges: g,
          onBeforeDelete: b
        }), C = _.length > 0, A = N.length > 0;
        if (C) {
          const D = _.map(Kr);
          w?.(_), S(D);
        }
        if (A) {
          const D = N.map(Kr);
          x?.(N), y(D);
        }
        return (A || C) && m?.({ nodes: N, edges: _ }), { deletedNodes: N, deletedEdges: _ };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (d, f = !0, p) => {
        const g = Sr(d), x = g ? d : l(d), w = p !== void 0;
        return x ? (p || t.getState().nodes).filter((y) => {
          const S = t.getState().nodeLookup.get(y.id);
          if (S && !g && (y.id === d.id || !S.internals.positionAbsolute))
            return !1;
          const m = wt(w ? y : S), b = Bt(m, x);
          return f && b > 0 || b >= m.width * m.height || b >= x.width * x.height;
        }) : [];
      },
      isNodeIntersecting: (d, f, p = !0) => {
        const x = Sr(d) ? d : l(d);
        if (!x)
          return !1;
        const w = Bt(x, f);
        return p && w > 0 || w >= f.width * f.height || w >= x.width * x.height;
      },
      updateNode: c,
      updateNodeData: (d, f, p = { replace: !1 }) => {
        c(d, (g) => {
          const x = typeof f == "function" ? f(g) : f;
          return p.replace ? { ...g, data: x } : { ...g, data: { ...g.data, ...x } };
        }, p);
      },
      updateEdge: u,
      updateEdgeData: (d, f, p = { replace: !1 }) => {
        u(d, (g) => {
          const x = typeof f == "function" ? f(g) : f;
          return p.replace ? { ...g, data: x } : { ...g, data: { ...g.data, ...x } };
        }, p);
      },
      getNodesBounds: (d) => {
        const { nodeLookup: f, nodeOrigin: p } = t.getState();
        return Fd(d, { nodeLookup: f, nodeOrigin: p });
      },
      getHandleConnections: ({ type: d, id: f, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}-${d}${f ? `-${f}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: d, handleId: f, nodeId: p }) => Array.from(t.getState().connectionLookup.get(`${p}${d ? f ? `-${d}-${f}` : `-${d}` : ""}`)?.values() ?? []),
      fitView: async (d) => {
        const f = t.getState().fitViewResolver ?? Gd();
        return t.setState({ fitViewQueued: !0, fitViewOptions: d, fitViewResolver: f }), n.nodeQueue.push((p) => [...p]), f.promise;
      }
    };
  }, []);
  return xe(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const ei = (e) => e.selected, kh = typeof window < "u" ? window : void 0;
function Mh({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = fe(), { deleteElements: o } = Fo(), r = Yt(e, { actInsideInputWithModifier: !1 }), i = Yt(t, { target: kh });
  ae(() => {
    if (r) {
      const { edges: s, nodes: a } = n.getState();
      o({ nodes: a.filter(ei), edges: s.filter(ei) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), ae(() => {
    n.setState({ multiSelectionActive: i });
  }, [i]);
}
function Ih(e) {
  const t = fe();
  ae(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = Ro(e.current);
      (o.height === 0 || o.width === 0) && t.getState().onError?.("004", Te.error004()), t.setState({ width: o.width || 500, height: o.height || 500 });
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
const Ln = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, Ah = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function Dh({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: i = rt.Free, zoomOnDoubleClick: s = !0, panOnDrag: a = !0, defaultViewport: l, translateExtent: c, minZoom: u, maxZoom: d, zoomActivationKeyCode: f, preventScrolling: p = !0, children: g, noWheelClassName: x, noPanClassName: w, onViewportChange: y, isControlledViewport: S, paneClickDistance: m, selectionOnDrag: b }) {
  const N = fe(), _ = se(null), { userSelectionActive: C, lib: A, connectionInProgress: D } = ce(Ah, de), B = Yt(f), I = se();
  Ih(_);
  const $ = ye((H) => {
    y?.({ x: H[0], y: H[1], zoom: H[2] }), S || N.setState({ transform: H });
  }, [y, S]);
  return ae(() => {
    if (_.current) {
      I.current = $f({
        domNode: _.current,
        minZoom: u,
        maxZoom: d,
        translateExtent: c,
        viewport: l,
        onDraggingChange: (E) => N.setState((M) => M.paneDragging === E ? M : { paneDragging: E }),
        onPanZoomStart: (E, M) => {
          const { onViewportChangeStart: j, onMoveStart: P } = N.getState();
          P?.(E, M), j?.(M);
        },
        onPanZoom: (E, M) => {
          const { onViewportChange: j, onMove: P } = N.getState();
          P?.(E, M), j?.(M);
        },
        onPanZoomEnd: (E, M) => {
          const { onViewportChangeEnd: j, onMoveEnd: P } = N.getState();
          P?.(E, M), j?.(M);
        }
      });
      const { x: H, y: v, zoom: k } = I.current.getViewport();
      return N.setState({
        panZoom: I.current,
        transform: [H, v, k],
        domNode: _.current.closest(".react-flow")
      }), () => {
        I.current?.destroy();
      };
    }
  }, []), ae(() => {
    I.current?.update({
      onPaneContextMenu: e,
      zoomOnScroll: t,
      zoomOnPinch: n,
      panOnScroll: o,
      panOnScrollSpeed: r,
      panOnScrollMode: i,
      zoomOnDoubleClick: s,
      panOnDrag: a,
      zoomActivationKeyPressed: B,
      preventScrolling: p,
      noPanClassName: w,
      userSelectionActive: C,
      noWheelClassName: x,
      lib: A,
      onTransformChange: $,
      connectionInProgress: D,
      selectionOnDrag: b,
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
    a,
    B,
    p,
    w,
    C,
    x,
    A,
    $,
    D,
    b,
    m
  ]), h.jsx("div", { className: "react-flow__renderer", ref: _, style: Ln, children: g });
}
const Ph = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function jh() {
  const { userSelectionActive: e, userSelectionRect: t } = ce(Ph, de);
  return e && t ? h.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const oo = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, $h = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function Th({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Vt.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: i, selectionOnDrag: s, onSelectionStart: a, onSelectionEnd: l, onPaneClick: c, onPaneContextMenu: u, onPaneScroll: d, onPaneMouseEnter: f, onPaneMouseMove: p, onPaneMouseLeave: g, children: x }) {
  const w = se(0), y = fe(), { userSelectionActive: S, elementsSelectable: m, dragging: b, connectionInProgress: N, panBy: _, autoPanSpeed: C } = ce($h, de), A = m && (e || S), D = se(null), B = se(), I = se(/* @__PURE__ */ new Set()), $ = se(/* @__PURE__ */ new Set()), H = se(!1), v = se({ x: 0, y: 0 }), k = se(!1), E = (z) => {
    if (H.current || N) {
      H.current = !1;
      return;
    }
    c?.(z), y.getState().resetSelectedElements(), y.setState({ nodesSelectionActive: !1 });
  }, M = (z) => {
    if (Array.isArray(o) && o?.includes(2)) {
      z.preventDefault();
      return;
    }
    u?.(z);
  }, j = d ? (z) => d(z) : void 0, P = (z) => {
    H.current && (z.stopPropagation(), H.current = !1);
  }, L = (z) => {
    const { domNode: X, transform: te } = y.getState();
    if (B.current = X?.getBoundingClientRect(), !B.current)
      return;
    const ee = z.target === D.current;
    if (!ee && !!z.target.closest(".nokey") || !e || !(s && ee || t) || z.button !== 0 || !z.isPrimary)
      return;
    z.target?.setPointerCapture?.(z.pointerId), H.current = !1;
    const { x: re, y: R } = $e(z.nativeEvent, B.current), G = Ct({ x: re, y: R }, te);
    y.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: G.x,
        startY: G.y,
        x: re,
        y: R
      }
    }), ee || (z.stopPropagation(), z.preventDefault());
  };
  function V(z, X) {
    const { userSelectionRect: te } = y.getState();
    if (!te)
      return;
    const { transform: ee, nodeLookup: U, edgeLookup: J, connectionLookup: re, triggerNodeChanges: R, triggerEdgeChanges: G, defaultEdgeOptions: ue } = y.getState(), he = { x: te.startX, y: te.startY }, { x: Ne, y: Ce } = vt(he, ee), ke = {
      startX: he.x,
      startY: he.y,
      x: z < Ne ? z : Ne,
      y: X < Ce ? X : Ce,
      width: Math.abs(z - Ne),
      height: Math.abs(X - Ce)
    }, et = I.current, Be = $.current;
    I.current = new Set($o(U, ke, ee, n === Vt.Partial, !0).map((Se) => Se.id)), $.current = /* @__PURE__ */ new Set();
    const Fe = ue?.selectable ?? !0;
    for (const Se of I.current) {
      const Me = re.get(Se);
      if (Me)
        for (const { edgeId: Ie } of Me.values()) {
          const T = J.get(Ie);
          T && (T.selectable ?? Fe) && $.current.add(Ie);
        }
    }
    if (!_r(et, I.current)) {
      const Se = ft(U, I.current, !0);
      R(Se);
    }
    if (!_r(Be, $.current)) {
      const Se = ft(J, $.current);
      G(Se);
    }
    y.setState({
      userSelectionRect: ke,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function O() {
    if (!r || !B.current)
      return;
    const [z, X] = To(v.current, B.current, C);
    _({ x: z, y: X }).then((te) => {
      if (!H.current || !te) {
        w.current = requestAnimationFrame(O);
        return;
      }
      const { x: ee, y: U } = v.current;
      V(ee, U), w.current = requestAnimationFrame(O);
    });
  }
  const Y = () => {
    cancelAnimationFrame(w.current), w.current = 0, k.current = !1;
  };
  ae(() => () => Y(), []);
  const W = (z) => {
    const { userSelectionRect: X, transform: te, resetSelectedElements: ee } = y.getState();
    if (!B.current || !X)
      return;
    const { x: U, y: J } = $e(z.nativeEvent, B.current);
    v.current = { x: U, y: J };
    const re = vt({ x: X.startX, y: X.startY }, te);
    if (!H.current) {
      const R = t ? 0 : i;
      if (Math.hypot(U - re.x, J - re.y) <= R)
        return;
      ee(), a?.(z);
    }
    H.current = !0, k.current || (O(), k.current = !0), V(U, J);
  }, Q = (z) => {
    z.button === 0 && (z.target?.releasePointerCapture?.(z.pointerId), !S && z.target === D.current && y.getState().userSelectionRect && E?.(z), y.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), H.current && (l?.(z), y.setState({
      nodesSelectionActive: I.current.size > 0
    })), Y());
  }, ne = (z) => {
    z.target?.releasePointerCapture?.(z.pointerId), Y();
  }, Z = o === !0 || Array.isArray(o) && o.includes(0);
  return h.jsxs("div", { className: ge(["react-flow__pane", { draggable: Z, dragging: b, selection: e }]), onClick: A ? void 0 : oo(E, D), onContextMenu: oo(M, D), onWheel: oo(j, D), onPointerEnter: A ? void 0 : f, onPointerMove: A ? W : p, onPointerUp: A ? Q : void 0, onPointerCancel: A ? ne : void 0, onPointerDownCapture: A ? L : void 0, onClickCapture: A ? P : void 0, onPointerLeave: g, ref: D, style: Ln, children: [x, h.jsx(jh, {})] });
}
function bo({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: i, multiSelectionActive: s, nodeLookup: a, onError: l } = t.getState(), c = a.get(e);
  if (!c) {
    l?.("012", Te.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), c.selected ? (n || c.selected && s) && (i({ nodes: [c], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function Ps({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: i, nodeClickDistance: s }) {
  const a = fe(), [l, c] = oe(!1), u = se();
  return ae(() => {
    u.current = vf({
      getStoreItems: () => a.getState(),
      onNodeMouseDown: (d) => {
        bo({
          id: d,
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
const zh = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function js() {
  const e = fe();
  return ye((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: i, nodesDraggable: s, onError: a, updateNodePositions: l, nodeLookup: c, nodeOrigin: u } = e.getState(), d = /* @__PURE__ */ new Map(), f = zh(s), p = r ? i[0] : 5, g = r ? i[1] : 5, x = n.direction.x * p * n.factor, w = n.direction.y * g * n.factor;
    for (const [, y] of c) {
      if (!f(y))
        continue;
      let S = {
        x: y.internals.positionAbsolute.x + x,
        y: y.internals.positionAbsolute.y + w
      };
      r && (S = Qt(S, i));
      const { position: m, positionAbsolute: b } = Ki({
        nodeId: y.id,
        nextPosition: S,
        nodeLookup: c,
        nodeExtent: o,
        nodeOrigin: u,
        onError: a
      });
      y.position = m, y.internals.positionAbsolute = b, d.set(y.id, y);
    }
    l(d);
  }, []);
}
const Yo = No(null), Rh = Yo.Provider;
Yo.Consumer;
const $s = () => qt(Yo), Lh = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), Hh = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: i, connection: s } = o, { fromHandle: a, toHandle: l, isValid: c } = s, u = l?.nodeId === e && l?.id === t && l?.type === n;
  return {
    connectingFrom: a?.nodeId === e && a?.id === t && a?.type === n,
    connectingTo: u,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: i === yt.Strict ? a?.type !== n : e !== a?.nodeId || t !== a?.id,
    connectionInProcess: !!a,
    clickConnectionInProcess: !!r,
    valid: u && c
  };
};
function Oh({ type: e = "source", position: t = K.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: i = !0, id: s, onConnect: a, children: l, className: c, onMouseDown: u, onTouchStart: d, ...f }, p) {
  const g = s || null, x = e === "target", w = fe(), y = $s(), { connectOnClick: S, noPanClassName: m, rfId: b } = ce(Lh, de), { connectingFrom: N, connectingTo: _, clickConnecting: C, isPossibleEndHandle: A, connectionInProcess: D, clickConnectionInProcess: B, valid: I } = ce(Hh(y, g, e), de);
  y || w.getState().onError?.("010", Te.error010());
  const $ = (k) => {
    const { defaultEdgeOptions: E, onConnect: M, hasDefaultEdges: j } = w.getState(), P = {
      ...E,
      ...k
    };
    if (j) {
      const { edges: L, setEdges: V, onError: O } = w.getState();
      V(Is(P, L, { onError: O }));
    }
    M?.(P), a?.(P);
  }, H = (k) => {
    if (!y)
      return;
    const E = is(k.nativeEvent);
    if (r && (E && k.button === 0 || !E)) {
      const M = w.getState();
      vo.onPointerDown(k.nativeEvent, {
        handleDomNode: k.currentTarget,
        autoPanOnConnect: M.autoPanOnConnect,
        connectionMode: M.connectionMode,
        connectionRadius: M.connectionRadius,
        domNode: M.domNode,
        nodeLookup: M.nodeLookup,
        lib: M.lib,
        isTarget: x,
        handleId: g,
        nodeId: y,
        flowId: M.rfId,
        panBy: M.panBy,
        cancelConnection: M.cancelConnection,
        onConnectStart: M.onConnectStart,
        onConnectEnd: (...j) => w.getState().onConnectEnd?.(...j),
        updateConnection: M.updateConnection,
        onConnect: $,
        isValidConnection: n || ((...j) => w.getState().isValidConnection?.(...j) ?? !0),
        getTransform: () => w.getState().transform,
        getFromHandle: () => w.getState().connection.fromHandle,
        autoPanSpeed: M.autoPanSpeed,
        dragThreshold: M.connectionDragThreshold
      });
    }
    E ? u?.(k) : d?.(k);
  }, v = (k) => {
    const { onClickConnectStart: E, onClickConnectEnd: M, connectionClickStartHandle: j, connectionMode: P, isValidConnection: L, lib: V, rfId: O, nodeLookup: Y, connection: W } = w.getState();
    if (!y || !j && !r)
      return;
    if (!j) {
      E?.(k.nativeEvent, { nodeId: y, handleId: g, handleType: e }), w.setState({ connectionClickStartHandle: { nodeId: y, type: e, id: g } });
      return;
    }
    const Q = os(k.target), ne = n || L, { connection: Z, isValid: z } = vo.isValid(k.nativeEvent, {
      handle: {
        nodeId: y,
        id: g,
        type: e
      },
      connectionMode: P,
      fromNodeId: j.nodeId,
      fromHandleId: j.id || null,
      fromType: j.type,
      isValidConnection: ne,
      flowId: O,
      doc: Q,
      lib: V,
      nodeLookup: Y
    });
    z && Z && $(Z);
    const X = structuredClone(W);
    delete X.inProgress, X.toPosition = X.toHandle ? X.toHandle.position : null, M?.(k, X), w.setState({ connectionClickStartHandle: null });
  };
  return h.jsx("div", { "data-handleid": g, "data-nodeid": y, "data-handlepos": t, "data-id": `${b}-${y}-${g}-${e}`, className: ge([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    m,
    c,
    {
      source: !x,
      target: x,
      connectable: o,
      connectablestart: r,
      connectableend: i,
      clickconnecting: C,
      connectingfrom: N,
      connectingto: _,
      valid: I,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!D || A) && (D || B ? i : r)
    }
  ]), onMouseDown: H, onTouchStart: H, onClick: S ? v : void 0, ref: p, ...f, children: l });
}
const St = pe(As(Oh));
function Vh({ data: e, isConnectable: t, sourcePosition: n = K.Bottom }) {
  return h.jsxs(h.Fragment, { children: [e?.label, h.jsx(St, { type: "source", position: n, isConnectable: t })] });
}
function Bh({ data: e, isConnectable: t, targetPosition: n = K.Top, sourcePosition: o = K.Bottom }) {
  return h.jsxs(h.Fragment, { children: [h.jsx(St, { type: "target", position: n, isConnectable: t }), e?.label, h.jsx(St, { type: "source", position: o, isConnectable: t })] });
}
function Fh() {
  return null;
}
function Yh({ data: e, isConnectable: t, targetPosition: n = K.Top }) {
  return h.jsxs(h.Fragment, { children: [h.jsx(St, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const Nn = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, ti = {
  input: Vh,
  default: Bh,
  output: Yh,
  group: Fh
};
function Xh(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const Wh = (e) => {
  const { width: t, height: n, x: o, y: r } = Kt(e.nodeLookup, {
    filter: (i) => !!i.selected
  });
  return {
    width: je(t) ? t : null,
    height: je(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function qh({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = fe(), { width: r, height: i, transformString: s, userSelectionActive: a } = ce(Wh, de), l = js(), c = se(null);
  ae(() => {
    n || c.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const u = !a && r !== null && i !== null;
  if (Ps({
    nodeRef: c,
    disabled: !u
  }), !u)
    return null;
  const d = e ? (p) => {
    const g = o.getState().nodes.filter((x) => x.selected);
    e(p, g);
  } : void 0, f = (p) => {
    Object.prototype.hasOwnProperty.call(Nn, p.key) && (p.preventDefault(), l({
      direction: Nn[p.key],
      factor: p.shiftKey ? 4 : 1
    }));
  };
  return h.jsx("div", { className: ge(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: s
  }, children: h.jsx("div", { ref: c, className: "react-flow__nodesselection-rect", onContextMenu: d, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : f, style: {
    width: r,
    height: i
  } }) });
}
const ni = typeof window < "u" ? window : void 0, Zh = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function Ts({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, paneClickDistance: a, deleteKeyCode: l, selectionKeyCode: c, selectionOnDrag: u, selectionMode: d, onSelectionStart: f, onSelectionEnd: p, multiSelectionKeyCode: g, panActivationKeyCode: x, zoomActivationKeyCode: w, elementsSelectable: y, zoomOnScroll: S, zoomOnPinch: m, panOnScroll: b, panOnScrollSpeed: N, panOnScrollMode: _, zoomOnDoubleClick: C, panOnDrag: A, autoPanOnSelection: D, defaultViewport: B, translateExtent: I, minZoom: $, maxZoom: H, preventScrolling: v, onSelectionContextMenu: k, noWheelClassName: E, noPanClassName: M, disableKeyboardA11y: j, onViewportChange: P, isControlledViewport: L }) {
  const { nodesSelectionActive: V, userSelectionActive: O } = ce(Zh, de), Y = Yt(c, { target: ni }), W = Yt(x, { target: ni }), Q = W || A, ne = W || b, Z = u && Q !== !0, z = Y || O || Z;
  return Mh({ deleteKeyCode: l, multiSelectionKeyCode: g }), h.jsx(Dh, { onPaneContextMenu: i, elementsSelectable: y, zoomOnScroll: S, zoomOnPinch: m, panOnScroll: ne, panOnScrollSpeed: N, panOnScrollMode: _, zoomOnDoubleClick: C, panOnDrag: !Y && Q, defaultViewport: B, translateExtent: I, minZoom: $, maxZoom: H, zoomActivationKeyCode: w, preventScrolling: v, noWheelClassName: E, noPanClassName: M, onViewportChange: P, isControlledViewport: L, paneClickDistance: a, selectionOnDrag: Z, children: h.jsxs(Th, { onSelectionStart: f, onSelectionEnd: p, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, panOnDrag: Q, autoPanOnSelection: D, isSelecting: !!z, selectionMode: d, selectionKeyPressed: Y, paneClickDistance: a, selectionOnDrag: Z, children: [e, V && h.jsx(qh, { onSelectionContextMenu: k, noPanClassName: M, disableKeyboardA11y: j })] }) });
}
Ts.displayName = "FlowRenderer";
const Uh = pe(Ts), Gh = (e) => (t) => e ? $o(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function Kh(e) {
  return ce(ye(Gh(e), [e]), de);
}
const Qh = (e) => e.updateNodeInternals;
function Jh() {
  const e = ce(Qh), [t] = oe(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
function ep({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = fe(), i = se(null), s = se(null), a = se(e.sourcePosition), l = se(e.targetPosition), c = se(t), u = n && !!e.internals.handleBounds;
  return ae(() => {
    i.current && !e.hidden && (!u || s.current !== i.current) && (s.current && o?.unobserve(s.current), o?.observe(i.current), s.current = i.current);
  }, [u, e.hidden]), ae(() => () => {
    s.current && (o?.unobserve(s.current), s.current = null);
  }, []), ae(() => {
    if (i.current) {
      const d = c.current !== t, f = a.current !== e.sourcePosition, p = l.current !== e.targetPosition;
      (d || f || p) && (c.current = t, a.current = e.sourcePosition, l.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: i.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), i;
}
function tp({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: i, onDoubleClick: s, nodesDraggable: a, elementsSelectable: l, nodesConnectable: c, nodesFocusable: u, resizeObserver: d, noDragClassName: f, noPanClassName: p, disableKeyboardA11y: g, rfId: x, nodeTypes: w, nodeClickDistance: y, onError: S }) {
  const { node: m, internals: b, isParent: N } = ce((z) => {
    const X = z.nodeLookup.get(e), te = z.parentLookup.has(e);
    return {
      node: X,
      internals: X.internals,
      isParent: te
    };
  }, de);
  let _ = m.type || "default", C = w?.[_] || ti[_];
  C === void 0 && (S?.("003", Te.error003(_)), _ = "default", C = w?.default || ti.default);
  const A = !!(m.draggable || a && typeof m.draggable > "u"), D = !!(m.selectable || l && typeof m.selectable > "u"), B = !!(m.connectable || c && typeof m.connectable > "u"), I = !!(m.focusable || u && typeof m.focusable > "u"), $ = fe(), H = ts(m), v = ep({ node: m, nodeType: _, hasDimensions: H, resizeObserver: d }), k = Ps({
    nodeRef: v,
    disabled: m.hidden || !A,
    noDragClassName: f,
    handleSelector: m.dragHandle,
    nodeId: e,
    isSelectable: D,
    nodeClickDistance: y
  }), E = js();
  if (m.hidden)
    return null;
  const M = Ze(m), j = Xh(m), P = D || A || t || n || o || r, L = n ? (z) => n(z, { ...b.userNode }) : void 0, V = o ? (z) => o(z, { ...b.userNode }) : void 0, O = r ? (z) => r(z, { ...b.userNode }) : void 0, Y = i ? (z) => i(z, { ...b.userNode }) : void 0, W = s ? (z) => s(z, { ...b.userNode }) : void 0, Q = (z) => {
    const { selectNodesOnDrag: X, nodeDragThreshold: te } = $.getState();
    D && (!X || !A || te > 0) && bo({
      id: e,
      store: $,
      nodeRef: v
    }), t && t(z, { ...b.userNode });
  }, ne = (z) => {
    if (!(rs(z.nativeEvent) || g)) {
      if (Wi.includes(z.key) && D) {
        const X = z.key === "Escape";
        bo({
          id: e,
          store: $,
          unselect: X,
          nodeRef: v
        });
      } else if (A && m.selected && Object.prototype.hasOwnProperty.call(Nn, z.key)) {
        z.preventDefault();
        const { ariaLabelConfig: X } = $.getState();
        $.setState({
          ariaLiveMessage: X["node.a11yDescription.ariaLiveMessage"]({
            direction: z.key.replace("Arrow", "").toLowerCase(),
            x: ~~b.positionAbsolute.x,
            y: ~~b.positionAbsolute.y
          })
        }), E({
          direction: Nn[z.key],
          factor: z.shiftKey ? 4 : 1
        });
      }
    }
  }, Z = () => {
    if (g || !v.current?.matches(":focus-visible"))
      return;
    const { transform: z, width: X, height: te, autoPanOnNodeFocus: ee, setCenter: U } = $.getState();
    if (!ee)
      return;
    $o(/* @__PURE__ */ new Map([[e, m]]), { x: 0, y: 0, width: X, height: te }, z, !0).length > 0 || U(m.position.x + M.width / 2, m.position.y + M.height / 2, {
      zoom: z[2]
    });
  };
  return h.jsx("div", { className: ge([
    "react-flow__node",
    `react-flow__node-${_}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [p]: A
    },
    m.className,
    {
      selected: m.selected,
      selectable: D,
      parent: N,
      draggable: A,
      dragging: k
    }
  ]), ref: v, style: {
    zIndex: b.z,
    transform: `translate(${b.positionAbsolute.x}px,${b.positionAbsolute.y}px)`,
    pointerEvents: P ? "all" : "none",
    visibility: H ? "visible" : "hidden",
    ...m.style,
    ...j
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: L, onMouseMove: V, onMouseLeave: O, onContextMenu: Y, onClick: Q, onDoubleClick: W, onKeyDown: I ? ne : void 0, tabIndex: I ? 0 : void 0, onFocus: I ? Z : void 0, role: m.ariaRole ?? (I ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": g ? void 0 : `${_s}-${x}`, "aria-label": m.ariaLabel, ...m.domAttributes, children: h.jsx(Rh, { value: e, children: h.jsx(C, { id: e, data: m.data, type: _, positionAbsoluteX: b.positionAbsolute.x, positionAbsoluteY: b.positionAbsolute.y, selected: m.selected ?? !1, selectable: D, draggable: A, deletable: m.deletable ?? !0, isConnectable: B, sourcePosition: m.sourcePosition, targetPosition: m.targetPosition, dragging: k, dragHandle: m.dragHandle, zIndex: b.z, parentId: m.parentId, ...M }) }) });
}
var np = pe(tp);
const op = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function zs(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: i } = ce(op, de), s = Kh(e.onlyRenderVisibleElements), a = Jh();
  return h.jsx("div", { className: "react-flow__nodes", style: Ln, children: s.map((l) => (
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
    h.jsx(np, { id: l, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: a, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: i }, l)
  )) });
}
zs.displayName = "NodeRenderer";
const rp = pe(zs);
function ip(e) {
  return ce(ye((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const i = n.nodeLookup.get(r.source), s = n.nodeLookup.get(r.target);
        i && s && ef({
          sourceNode: i,
          targetNode: s,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && o.push(r.id);
      }
    return o;
  }, [e]), de);
}
const sp = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return h.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, ap = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return h.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, oi = {
  [_n.Arrow]: sp,
  [_n.ArrowClosed]: ap
};
function cp(e) {
  const t = fe();
  return xe(() => Object.prototype.hasOwnProperty.call(oi, e) ? oi[e] : (t.getState().onError?.("009", Te.error009(e)), null), [e]);
}
const lp = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: i = "strokeWidth", strokeWidth: s, orient: a = "auto-start-reverse" }) => {
  const l = cp(t);
  return l ? h.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: i, orient: a, refX: "0", refY: "0", children: h.jsx(l, { color: n, strokeWidth: s }) }) : null;
}, Rs = ({ defaultColor: e, rfId: t }) => {
  const n = ce((i) => i.edges), o = ce((i) => i.defaultEdgeOptions), r = xe(() => lf(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? h.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: h.jsx("defs", { children: r.map((i) => h.jsx(lp, { id: i.id, type: i.type, color: i.color, width: i.width, height: i.height, markerUnits: i.markerUnits, strokeWidth: i.strokeWidth, orient: i.orient }, i.id)) }) }) : null;
};
Rs.displayName = "MarkerDefinitions";
var up = pe(Rs);
function Ls({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: i, labelBgPadding: s = [2, 4], labelBgBorderRadius: a = 2, children: l, className: c, ...u }) {
  const [d, f] = oe({ x: 1, y: 0, width: 0, height: 0 }), p = ge(["react-flow__edge-textwrapper", c]), g = se(null);
  return ae(() => {
    if (g.current) {
      const x = g.current.getBBox();
      f({
        x: x.x,
        y: x.y,
        width: x.width,
        height: x.height
      });
    }
  }, [n]), n ? h.jsxs("g", { transform: `translate(${e - d.width / 2} ${t - d.height / 2})`, className: p, visibility: d.width ? "visible" : "hidden", ...u, children: [r && h.jsx("rect", { width: d.width + 2 * s[0], x: -s[0], y: -s[1], height: d.height + 2 * s[1], className: "react-flow__edge-textbg", style: i, rx: a, ry: a }), h.jsx("text", { className: "react-flow__edge-text", y: d.height / 2, dy: "0.3em", ref: g, style: o, children: n }), l] }) : null;
}
Ls.displayName = "EdgeText";
const dp = pe(Ls);
function Hn({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: a, labelBgBorderRadius: l, interactionWidth: c = 20, ...u }) {
  return h.jsxs(h.Fragment, { children: [h.jsx("path", { ...u, d: e, fill: "none", className: ge(["react-flow__edge-path", u.className]) }), c ? h.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: c, className: "react-flow__edge-interaction" }) : null, o && je(t) && je(n) ? h.jsx(dp, { x: t, y: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: a, labelBgBorderRadius: l }) : null] });
}
function ri({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === K.Left || e === K.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function Hs({ sourceX: e, sourceY: t, sourcePosition: n = K.Bottom, targetX: o, targetY: r, targetPosition: i = K.Top }) {
  const [s, a] = ri({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [l, c] = ri({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [u, d, f, p] = ss({
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
    u,
    d,
    f,
    p
  ];
}
function Os(e) {
  return pe(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: a, label: l, labelStyle: c, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: p, style: g, markerEnd: x, markerStart: w, interactionWidth: y }) => {
    const [S, m, b] = Hs({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: a
    }), N = e.isInternal ? void 0 : t;
    return h.jsx(Hn, { id: N, path: S, labelX: m, labelY: b, label: l, labelStyle: c, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: p, style: g, markerEnd: x, markerStart: w, interactionWidth: y });
  });
}
const fp = Os({ isInternal: !1 }), Vs = Os({ isInternal: !0 });
fp.displayName = "SimpleBezierEdge";
Vs.displayName = "SimpleBezierEdgeInternal";
function Bs(e) {
  return pe(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: u, labelBgBorderRadius: d, style: f, sourcePosition: p = K.Bottom, targetPosition: g = K.Top, markerEnd: x, markerStart: w, pathOptions: y, interactionWidth: S }) => {
    const [m, b, N] = yo({
      sourceX: n,
      sourceY: o,
      sourcePosition: p,
      targetX: r,
      targetY: i,
      targetPosition: g,
      borderRadius: y?.borderRadius,
      offset: y?.offset,
      stepPosition: y?.stepPosition
    }), _ = e.isInternal ? void 0 : t;
    return h.jsx(Hn, { id: _, path: m, labelX: b, labelY: N, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: u, labelBgBorderRadius: d, style: f, markerEnd: x, markerStart: w, interactionWidth: S });
  });
}
const Fs = Bs({ isInternal: !1 }), Ys = Bs({ isInternal: !0 });
Fs.displayName = "SmoothStepEdge";
Ys.displayName = "SmoothStepEdgeInternal";
function Xs(e) {
  return pe(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return h.jsx(Fs, { ...n, id: o, pathOptions: xe(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const hp = Xs({ isInternal: !1 }), Ws = Xs({ isInternal: !0 });
hp.displayName = "StepEdge";
Ws.displayName = "StepEdgeInternal";
function qs(e) {
  return pe(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: u, labelBgBorderRadius: d, style: f, markerEnd: p, markerStart: g, interactionWidth: x }) => {
    const [w, y, S] = ls({ sourceX: n, sourceY: o, targetX: r, targetY: i }), m = e.isInternal ? void 0 : t;
    return h.jsx(Hn, { id: m, path: w, labelX: y, labelY: S, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: u, labelBgBorderRadius: d, style: f, markerEnd: p, markerStart: g, interactionWidth: x });
  });
}
const pp = qs({ isInternal: !1 }), Zs = qs({ isInternal: !0 });
pp.displayName = "StraightEdge";
Zs.displayName = "StraightEdgeInternal";
function Us(e) {
  return pe(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s = K.Bottom, targetPosition: a = K.Top, label: l, labelStyle: c, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: p, style: g, markerEnd: x, markerStart: w, pathOptions: y, interactionWidth: S }) => {
    const [m, b, N] = as({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: a,
      curvature: y?.curvature
    }), _ = e.isInternal ? void 0 : t;
    return h.jsx(Hn, { id: _, path: m, labelX: b, labelY: N, label: l, labelStyle: c, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: p, style: g, markerEnd: x, markerStart: w, interactionWidth: S });
  });
}
const gp = Us({ isInternal: !1 }), Gs = Us({ isInternal: !0 });
gp.displayName = "BezierEdge";
Gs.displayName = "BezierEdgeInternal";
const ii = {
  default: Gs,
  straight: Zs,
  step: Ws,
  smoothstep: Ys,
  simplebezier: Vs
}, si = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, mp = (e, t, n) => n === K.Left ? e - t : n === K.Right ? e + t : e, yp = (e, t, n) => n === K.Top ? e - t : n === K.Bottom ? e + t : e, ai = "react-flow__edgeupdater";
function ci({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: i, onMouseOut: s, type: a }) {
  return h.jsx("circle", { onMouseDown: r, onMouseEnter: i, onMouseOut: s, className: ge([ai, `${ai}-${a}`]), cx: mp(t, o, e), cy: yp(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function xp({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: i, targetY: s, sourcePosition: a, targetPosition: l, onReconnect: c, onReconnectStart: u, onReconnectEnd: d, setReconnecting: f, setUpdateHover: p }) {
  const g = fe(), x = (b, N) => {
    if (b.button !== 0)
      return;
    const { autoPanOnConnect: _, domNode: C, connectionMode: A, connectionRadius: D, lib: B, onConnectStart: I, cancelConnection: $, nodeLookup: H, rfId: v, panBy: k, updateConnection: E } = g.getState(), M = N.type === "target", j = (V, O) => {
      f(!1), d?.(V, n, N.type, O);
    }, P = (V) => c?.(n, V), L = (V, O) => {
      f(!0), u?.(b, n, N.type), I?.(V, O);
    };
    vo.onPointerDown(b.nativeEvent, {
      autoPanOnConnect: _,
      connectionMode: A,
      connectionRadius: D,
      domNode: C,
      handleId: N.id,
      nodeId: N.nodeId,
      nodeLookup: H,
      isTarget: M,
      edgeUpdaterType: N.type,
      lib: B,
      flowId: v,
      cancelConnection: $,
      panBy: k,
      isValidConnection: (...V) => g.getState().isValidConnection?.(...V) ?? !0,
      onConnect: P,
      onConnectStart: L,
      onConnectEnd: (...V) => g.getState().onConnectEnd?.(...V),
      onReconnectEnd: j,
      updateConnection: E,
      getTransform: () => g.getState().transform,
      getFromHandle: () => g.getState().connection.fromHandle,
      dragThreshold: g.getState().connectionDragThreshold,
      handleDomNode: b.currentTarget
    });
  }, w = (b) => x(b, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), y = (b) => x(b, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), S = () => p(!0), m = () => p(!1);
  return h.jsxs(h.Fragment, { children: [(e === !0 || e === "source") && h.jsx(ci, { position: a, centerX: o, centerY: r, radius: t, onMouseDown: w, onMouseEnter: S, onMouseOut: m, type: "source" }), (e === !0 || e === "target") && h.jsx(ci, { position: l, centerX: i, centerY: s, radius: t, onMouseDown: y, onMouseEnter: S, onMouseOut: m, type: "target" })] });
}
function wp({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: i, onContextMenu: s, onMouseEnter: a, onMouseMove: l, onMouseLeave: c, reconnectRadius: u, onReconnect: d, onReconnectStart: f, onReconnectEnd: p, rfId: g, edgeTypes: x, noPanClassName: w, onError: y, disableKeyboardA11y: S }) {
  let m = ce((U) => U.edgeLookup.get(e));
  const b = ce((U) => U.defaultEdgeOptions);
  m = b ? { ...b, ...m } : m;
  let N = m.type || "default", _ = x?.[N] || ii[N];
  _ === void 0 && (y?.("011", Te.error011(N)), N = "default", _ = x?.default || ii.default);
  const C = !!(m.focusable || t && typeof m.focusable > "u"), A = typeof d < "u" && (m.reconnectable || n && typeof m.reconnectable > "u"), D = !!(m.selectable || o && typeof m.selectable > "u"), B = se(null), [I, $] = oe(!1), [H, v] = oe(!1), k = fe(), { zIndex: E, sourceX: M, sourceY: j, targetX: P, targetY: L, sourcePosition: V, targetPosition: O } = ce(ye((U) => {
    const J = U.nodeLookup.get(m.source), re = U.nodeLookup.get(m.target);
    if (!J || !re)
      return {
        zIndex: m.zIndex,
        ...si
      };
    const R = cf({
      id: e,
      sourceNode: J,
      targetNode: re,
      sourceHandle: m.sourceHandle || null,
      targetHandle: m.targetHandle || null,
      connectionMode: U.connectionMode,
      onError: y
    });
    return {
      zIndex: Jd({
        selected: m.selected,
        zIndex: m.zIndex,
        sourceNode: J,
        targetNode: re,
        elevateOnSelect: U.elevateEdgesOnSelect,
        zIndexMode: U.zIndexMode
      }),
      ...R || si
    };
  }, [m.source, m.target, m.sourceHandle, m.targetHandle, m.selected, m.zIndex]), de), Y = xe(() => m.markerStart ? `url('#${xo(m.markerStart, g)}')` : void 0, [m.markerStart, g]), W = xe(() => m.markerEnd ? `url('#${xo(m.markerEnd, g)}')` : void 0, [m.markerEnd, g]);
  if (m.hidden || M === null || j === null || P === null || L === null)
    return null;
  const Q = (U) => {
    const { addSelectedEdges: J, unselectNodesAndEdges: re, multiSelectionActive: R } = k.getState();
    D && (k.setState({ nodesSelectionActive: !1 }), m.selected && R ? (re({ nodes: [], edges: [m] }), B.current?.blur()) : J([e])), r && r(U, m);
  }, ne = i ? (U) => {
    i(U, { ...m });
  } : void 0, Z = s ? (U) => {
    s(U, { ...m });
  } : void 0, z = a ? (U) => {
    a(U, { ...m });
  } : void 0, X = l ? (U) => {
    l(U, { ...m });
  } : void 0, te = c ? (U) => {
    c(U, { ...m });
  } : void 0, ee = (U) => {
    if (!S && Wi.includes(U.key) && D) {
      const { unselectNodesAndEdges: J, addSelectedEdges: re } = k.getState();
      U.key === "Escape" ? (B.current?.blur(), J({ edges: [m] })) : re([e]);
    }
  };
  return h.jsx("svg", { style: { zIndex: E }, children: h.jsxs("g", { className: ge([
    "react-flow__edge",
    `react-flow__edge-${N}`,
    m.className,
    w,
    {
      selected: m.selected,
      animated: m.animated,
      inactive: !D && !r,
      updating: I,
      selectable: D
    }
  ]), onClick: Q, onDoubleClick: ne, onContextMenu: Z, onMouseEnter: z, onMouseMove: X, onMouseLeave: te, onKeyDown: C ? ee : void 0, tabIndex: C ? 0 : void 0, role: m.ariaRole ?? (C ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": m.ariaLabel === null ? void 0 : m.ariaLabel || `Edge from ${m.source} to ${m.target}`, "aria-describedby": C ? `${Es}-${g}` : void 0, ref: B, ...m.domAttributes, children: [!H && h.jsx(_, { id: e, source: m.source, target: m.target, type: m.type, selected: m.selected, animated: m.animated, selectable: D, deletable: m.deletable ?? !0, label: m.label, labelStyle: m.labelStyle, labelShowBg: m.labelShowBg, labelBgStyle: m.labelBgStyle, labelBgPadding: m.labelBgPadding, labelBgBorderRadius: m.labelBgBorderRadius, sourceX: M, sourceY: j, targetX: P, targetY: L, sourcePosition: V, targetPosition: O, data: m.data, style: m.style, sourceHandleId: m.sourceHandle, targetHandleId: m.targetHandle, markerStart: Y, markerEnd: W, pathOptions: "pathOptions" in m ? m.pathOptions : void 0, interactionWidth: m.interactionWidth }), A && h.jsx(xp, { edge: m, isReconnectable: A, reconnectRadius: u, onReconnect: d, onReconnectStart: f, onReconnectEnd: p, sourceX: M, sourceY: j, targetX: P, targetY: L, sourcePosition: V, targetPosition: O, setUpdateHover: $, setReconnecting: v })] }) });
}
var vp = pe(wp);
const bp = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function Ks({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: i, onEdgeContextMenu: s, onEdgeMouseEnter: a, onEdgeMouseMove: l, onEdgeMouseLeave: c, onEdgeClick: u, reconnectRadius: d, onEdgeDoubleClick: f, onReconnectStart: p, onReconnectEnd: g, disableKeyboardA11y: x }) {
  const { edgesFocusable: w, edgesReconnectable: y, elementsSelectable: S, onError: m } = ce(bp, de), b = ip(t);
  return h.jsxs("div", { className: "react-flow__edges", children: [h.jsx(up, { defaultColor: e, rfId: n }), b.map((N) => h.jsx(vp, { id: N, edgesFocusable: w, edgesReconnectable: y, elementsSelectable: S, noPanClassName: r, onReconnect: i, onContextMenu: s, onMouseEnter: a, onMouseMove: l, onMouseLeave: c, onClick: u, reconnectRadius: d, onDoubleClick: f, onReconnectStart: p, onReconnectEnd: g, rfId: n, onError: m, edgeTypes: o, disableKeyboardA11y: x }, N))] });
}
Ks.displayName = "EdgeRenderer";
const Sp = pe(Ks), _p = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function Ep({ children: e }) {
  const t = ce(_p);
  return h.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function Np(e) {
  const t = Fo(), n = se(!1);
  ae(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Cp = (e) => e.panZoom?.syncViewport;
function kp(e) {
  const t = ce(Cp), n = fe();
  return ae(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function Mp(e) {
  return e.connection.inProgress ? { ...e.connection, to: Ct(e.connection.to, e.transform) } : { ...e.connection };
}
function Ip(e) {
  return Mp;
}
function Ap(e) {
  const t = Ip();
  return ce(t, de);
}
const Dp = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function Pp({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: i, height: s, isValid: a, inProgress: l } = ce(Dp, de);
  return !(i && r && l) ? null : h.jsx("svg", { style: e, width: i, height: s, className: "react-flow__connectionline react-flow__container", children: h.jsx("g", { className: ge(["react-flow__connection", Ui(a)]), children: h.jsx(Qs, { style: t, type: n, CustomComponent: o, isValid: a }) }) });
}
const Qs = ({ style: e, type: t = Ke.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: i, fromNode: s, fromHandle: a, fromPosition: l, to: c, toNode: u, toHandle: d, toPosition: f, pointer: p } = Ap();
  if (!r)
    return;
  if (n)
    return h.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: s, fromHandle: a, fromX: i.x, fromY: i.y, toX: c.x, toY: c.y, fromPosition: l, toPosition: f, connectionStatus: Ui(o), toNode: u, toHandle: d, pointer: p });
  let g = "";
  const x = {
    sourceX: i.x,
    sourceY: i.y,
    sourcePosition: l,
    targetX: c.x,
    targetY: c.y,
    targetPosition: f
  };
  switch (t) {
    case Ke.Bezier:
      [g] = as(x);
      break;
    case Ke.SimpleBezier:
      [g] = Hs(x);
      break;
    case Ke.Step:
      [g] = yo({
        ...x,
        borderRadius: 0
      });
      break;
    case Ke.SmoothStep:
      [g] = yo(x);
      break;
    default:
      [g] = ls(x);
  }
  return h.jsx("path", { d: g, fill: "none", className: "react-flow__connection-path", style: e });
};
Qs.displayName = "ConnectionLine";
const jp = {};
function li(e = jp) {
  se(e), fe(), ae(() => {
  }, [e]);
}
function $p() {
  fe(), se(!1), ae(() => {
  }, []);
}
function Js({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: i, onEdgeDoubleClick: s, onNodeMouseEnter: a, onNodeMouseMove: l, onNodeMouseLeave: c, onNodeContextMenu: u, onSelectionContextMenu: d, onSelectionStart: f, onSelectionEnd: p, connectionLineType: g, connectionLineStyle: x, connectionLineComponent: w, connectionLineContainerStyle: y, selectionKeyCode: S, selectionOnDrag: m, selectionMode: b, multiSelectionKeyCode: N, panActivationKeyCode: _, zoomActivationKeyCode: C, deleteKeyCode: A, onlyRenderVisibleElements: D, elementsSelectable: B, defaultViewport: I, translateExtent: $, minZoom: H, maxZoom: v, preventScrolling: k, defaultMarkerColor: E, zoomOnScroll: M, zoomOnPinch: j, panOnScroll: P, panOnScrollSpeed: L, panOnScrollMode: V, zoomOnDoubleClick: O, panOnDrag: Y, autoPanOnSelection: W, onPaneClick: Q, onPaneMouseEnter: ne, onPaneMouseMove: Z, onPaneMouseLeave: z, onPaneScroll: X, onPaneContextMenu: te, paneClickDistance: ee, nodeClickDistance: U, onEdgeContextMenu: J, onEdgeMouseEnter: re, onEdgeMouseMove: R, onEdgeMouseLeave: G, reconnectRadius: ue, onReconnect: he, onReconnectStart: Ne, onReconnectEnd: Ce, noDragClassName: ke, noWheelClassName: et, noPanClassName: Be, disableKeyboardA11y: Fe, nodeExtent: Se, rfId: Me, viewport: Ie, onViewportChange: T }) {
  return li(e), li(t), $p(), Np(n), kp(Ie), h.jsx(Uh, { onPaneClick: Q, onPaneMouseEnter: ne, onPaneMouseMove: Z, onPaneMouseLeave: z, onPaneContextMenu: te, onPaneScroll: X, paneClickDistance: ee, deleteKeyCode: A, selectionKeyCode: S, selectionOnDrag: m, selectionMode: b, onSelectionStart: f, onSelectionEnd: p, multiSelectionKeyCode: N, panActivationKeyCode: _, zoomActivationKeyCode: C, elementsSelectable: B, zoomOnScroll: M, zoomOnPinch: j, zoomOnDoubleClick: O, panOnScroll: P, panOnScrollSpeed: L, panOnScrollMode: V, panOnDrag: Y, autoPanOnSelection: W, defaultViewport: I, translateExtent: $, minZoom: H, maxZoom: v, onSelectionContextMenu: d, preventScrolling: k, noDragClassName: ke, noWheelClassName: et, noPanClassName: Be, disableKeyboardA11y: Fe, onViewportChange: T, isControlledViewport: !!Ie, children: h.jsxs(Ep, { children: [h.jsx(Sp, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: s, onReconnect: he, onReconnectStart: Ne, onReconnectEnd: Ce, onlyRenderVisibleElements: D, onEdgeContextMenu: J, onEdgeMouseEnter: re, onEdgeMouseMove: R, onEdgeMouseLeave: G, reconnectRadius: ue, defaultMarkerColor: E, noPanClassName: Be, disableKeyboardA11y: Fe, rfId: Me }), h.jsx(Pp, { style: x, type: g, component: w, containerStyle: y }), h.jsx("div", { className: "react-flow__edgelabel-renderer" }), h.jsx(rp, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: i, onNodeMouseEnter: a, onNodeMouseMove: l, onNodeMouseLeave: c, onNodeContextMenu: u, nodeClickDistance: U, onlyRenderVisibleElements: D, noPanClassName: Be, noDragClassName: ke, disableKeyboardA11y: Fe, nodeExtent: Se, rfId: Me }), h.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Js.displayName = "GraphView";
const Tp = pe(Js), zp = es(), ui = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: a, minZoom: l = 0.5, maxZoom: c = 2, nodeOrigin: u, nodeExtent: d, zIndexMode: f = "basic" } = {}) => {
  const p = /* @__PURE__ */ new Map(), g = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), y = o ?? t ?? [], S = n ?? e ?? [], m = u ?? [0, 0], b = d ?? Ot;
  fs(x, w, y);
  const { nodesInitialized: N } = wo(S, p, g, {
    nodeOrigin: m,
    nodeExtent: b,
    zIndexMode: f
  });
  let _ = [0, 0, 1];
  if (s && r && i) {
    const C = Kt(p, {
      filter: (I) => !!((I.width || I.initialWidth) && (I.height || I.initialHeight))
    }), { x: A, y: D, zoom: B } = zo(C, r, i, l, c, a?.padding ?? 0.1);
    _ = [A, D, B];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: i ?? 0,
    transform: _,
    nodes: S,
    nodesInitialized: N,
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
    minZoom: l,
    maxZoom: c,
    translateExtent: Ot,
    nodeExtent: b,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: yt.Strict,
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
    fitViewOptions: a,
    fitViewResolver: null,
    connection: { ...Zi },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: zp,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: qi,
    zIndexMode: f,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, Rp = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: a, minZoom: l, maxZoom: c, nodeOrigin: u, nodeExtent: d, zIndexMode: f }) => Kf((p, g) => {
  async function x() {
    const { nodeLookup: w, panZoom: y, fitViewOptions: S, fitViewResolver: m, width: b, height: N, minZoom: _, maxZoom: C } = g();
    y && (await Wd({
      nodes: w,
      width: b,
      height: N,
      panZoom: y,
      minZoom: _,
      maxZoom: C
    }, S), m?.resolve(!0), p({ fitViewResolver: null }));
  }
  return {
    ...ui({
      nodes: e,
      edges: t,
      width: r,
      height: i,
      fitView: s,
      fitViewOptions: a,
      minZoom: l,
      maxZoom: c,
      nodeOrigin: u,
      nodeExtent: d,
      defaultNodes: n,
      defaultEdges: o,
      zIndexMode: f
    }),
    setNodes: (w) => {
      const { nodeLookup: y, parentLookup: S, nodeOrigin: m, elevateNodesOnSelect: b, fitViewQueued: N, zIndexMode: _, nodesSelectionActive: C } = g(), { nodesInitialized: A, hasSelectedNodes: D } = wo(w, y, S, {
        nodeOrigin: m,
        nodeExtent: d,
        elevateNodesOnSelect: b,
        checkEquality: !0,
        zIndexMode: _
      }), B = C && D;
      N && A ? (x(), p({
        nodes: w,
        nodesInitialized: A,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: B
      })) : p({ nodes: w, nodesInitialized: A, nodesSelectionActive: B });
    },
    setEdges: (w) => {
      const { connectionLookup: y, edgeLookup: S } = g();
      fs(y, S, w), p({ edges: w });
    },
    setDefaultNodesAndEdges: (w, y) => {
      if (w) {
        const { setNodes: S } = g();
        S(w), p({ hasDefaultNodes: !0 });
      }
      if (y) {
        const { setEdges: S } = g();
        S(y), p({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (w) => {
      const { triggerNodeChanges: y, nodeLookup: S, parentLookup: m, domNode: b, nodeOrigin: N, nodeExtent: _, debug: C, fitViewQueued: A, zIndexMode: D } = g(), { changes: B, updatedInternals: I } = mf(w, S, m, b, N, _, D);
      I && (ff(S, m, { nodeOrigin: N, nodeExtent: _, zIndexMode: D }), A ? (x(), p({ fitViewQueued: !1, fitViewOptions: void 0 })) : p({}), B?.length > 0 && (C && console.log("React Flow: trigger node changes", B), y?.(B)));
    },
    updateNodePositions: (w, y = !1) => {
      const S = [];
      let m = [];
      const { nodeLookup: b, triggerNodeChanges: N, connection: _, updateConnection: C, onNodesChangeMiddlewareMap: A } = g();
      for (const [D, B] of w) {
        const I = b.get(D), $ = !!(I?.expandParent && I?.parentId && B?.position), H = {
          id: D,
          type: "position",
          position: $ ? {
            x: Math.max(0, B.position.x),
            y: Math.max(0, B.position.y)
          } : B.position,
          dragging: y
        };
        if (I && _.inProgress && _.fromNode.id === I.id) {
          const v = lt(I, _.fromHandle, K.Left, !0);
          C({ ..._, from: v });
        }
        $ && I.parentId && S.push({
          id: D,
          parentId: I.parentId,
          rect: {
            ...B.internals.positionAbsolute,
            width: B.measured.width ?? 0,
            height: B.measured.height ?? 0
          }
        }), m.push(H);
      }
      if (S.length > 0) {
        const { parentLookup: D, nodeOrigin: B } = g(), I = Bo(S, b, D, B);
        m.push(...I);
      }
      for (const D of A.values())
        m = D(m);
      N(m);
    },
    triggerNodeChanges: (w) => {
      const { onNodesChange: y, setNodes: S, nodes: m, hasDefaultNodes: b, debug: N } = g();
      if (w?.length) {
        if (b) {
          const _ = ks(w, m);
          S(_);
        }
        N && console.log("React Flow: trigger node changes", w), y?.(w);
      }
    },
    triggerEdgeChanges: (w) => {
      const { onEdgesChange: y, setEdges: S, edges: m, hasDefaultEdges: b, debug: N } = g();
      if (w?.length) {
        if (b) {
          const _ = Ms(w, m);
          S(_);
        }
        N && console.log("React Flow: trigger edge changes", w), y?.(w);
      }
    },
    addSelectedNodes: (w) => {
      const { multiSelectionActive: y, edgeLookup: S, nodeLookup: m, triggerNodeChanges: b, triggerEdgeChanges: N } = g();
      if (y) {
        const _ = w.map((C) => tt(C, !0));
        b(_);
        return;
      }
      b(ft(m, /* @__PURE__ */ new Set([...w]), !0)), N(ft(S));
    },
    addSelectedEdges: (w) => {
      const { multiSelectionActive: y, edgeLookup: S, nodeLookup: m, triggerNodeChanges: b, triggerEdgeChanges: N } = g();
      if (y) {
        const _ = w.map((C) => tt(C, !0));
        N(_);
        return;
      }
      N(ft(S, /* @__PURE__ */ new Set([...w]))), b(ft(m, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: w, edges: y } = {}) => {
      const { edges: S, nodes: m, nodeLookup: b, triggerNodeChanges: N, triggerEdgeChanges: _ } = g(), C = w || m, A = y || S, D = [];
      for (const I of C) {
        if (!I.selected)
          continue;
        const $ = b.get(I.id);
        $ && ($.selected = !1), D.push(tt(I.id, !1));
      }
      const B = [];
      for (const I of A)
        I.selected && B.push(tt(I.id, !1));
      N(D), _(B);
    },
    setMinZoom: (w) => {
      const { panZoom: y, maxZoom: S } = g();
      y?.setScaleExtent([w, S]), p({ minZoom: w });
    },
    setMaxZoom: (w) => {
      const { panZoom: y, minZoom: S } = g();
      y?.setScaleExtent([S, w]), p({ maxZoom: w });
    },
    setTranslateExtent: (w) => {
      g().panZoom?.setTranslateExtent(w), p({ translateExtent: w });
    },
    resetSelectedElements: () => {
      const { edges: w, nodes: y, triggerNodeChanges: S, triggerEdgeChanges: m, elementsSelectable: b } = g();
      if (!b)
        return;
      const N = y.reduce((C, A) => A.selected ? [...C, tt(A.id, !1)] : C, []), _ = w.reduce((C, A) => A.selected ? [...C, tt(A.id, !1)] : C, []);
      S(N), m(_);
    },
    setNodeExtent: (w) => {
      const { nodes: y, nodeLookup: S, parentLookup: m, nodeOrigin: b, elevateNodesOnSelect: N, nodeExtent: _, zIndexMode: C } = g();
      w[0][0] === _[0][0] && w[0][1] === _[0][1] && w[1][0] === _[1][0] && w[1][1] === _[1][1] || (wo(y, S, m, {
        nodeOrigin: b,
        nodeExtent: w,
        elevateNodesOnSelect: N,
        checkEquality: !1,
        zIndexMode: C
      }), p({ nodeExtent: w }));
    },
    panBy: (w) => {
      const { transform: y, width: S, height: m, panZoom: b, translateExtent: N } = g();
      return yf({ delta: w, panZoom: b, transform: y, translateExtent: N, width: S, height: m });
    },
    setCenter: async (w, y, S) => {
      const { width: m, height: b, maxZoom: N, panZoom: _ } = g();
      if (!_)
        return !1;
      const C = typeof S?.zoom < "u" ? S.zoom : N;
      return await _.setViewport({
        x: m / 2 - w * C,
        y: b / 2 - y * C,
        zoom: C
      }, { duration: S?.duration, ease: S?.ease, interpolate: S?.interpolate }), !0;
    },
    cancelConnection: () => {
      p({
        connection: { ...Zi }
      });
    },
    updateConnection: (w) => {
      p({ connection: w });
    },
    reset: () => p({ ...ui() })
  };
}, Object.is);
function Lp({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: i, initialMinZoom: s, initialMaxZoom: a, initialFitViewOptions: l, fitView: c, nodeOrigin: u, nodeExtent: d, zIndexMode: f, children: p }) {
  const [g] = oe(() => Rp({
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
    nodeOrigin: u,
    nodeExtent: d,
    zIndexMode: f
  }));
  return h.jsx(eh, { value: g, children: h.jsx(Eh, { children: p }) });
}
function Hp({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: i, height: s, fitView: a, fitViewOptions: l, minZoom: c, maxZoom: u, nodeOrigin: d, nodeExtent: f, zIndexMode: p }) {
  return qt(zn) ? h.jsx(h.Fragment, { children: e }) : h.jsx(Lp, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: i, initialHeight: s, fitView: a, initialFitViewOptions: l, initialMinZoom: c, initialMaxZoom: u, nodeOrigin: d, nodeExtent: f, zIndexMode: p, children: e });
}
const Op = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function Vp({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: i, edgeTypes: s, onNodeClick: a, onEdgeClick: l, onInit: c, onMove: u, onMoveStart: d, onMoveEnd: f, onConnect: p, onConnectStart: g, onConnectEnd: x, onClickConnectStart: w, onClickConnectEnd: y, onNodeMouseEnter: S, onNodeMouseMove: m, onNodeMouseLeave: b, onNodeContextMenu: N, onNodeDoubleClick: _, onNodeDragStart: C, onNodeDrag: A, onNodeDragStop: D, onNodesDelete: B, onEdgesDelete: I, onDelete: $, onSelectionChange: H, onSelectionDragStart: v, onSelectionDrag: k, onSelectionDragStop: E, onSelectionContextMenu: M, onSelectionStart: j, onSelectionEnd: P, onBeforeDelete: L, connectionMode: V, connectionLineType: O = Ke.Bezier, connectionLineStyle: Y, connectionLineComponent: W, connectionLineContainerStyle: Q, deleteKeyCode: ne = "Backspace", selectionKeyCode: Z = "Shift", selectionOnDrag: z = !1, selectionMode: X = Vt.Full, panActivationKeyCode: te = "Space", multiSelectionKeyCode: ee = Ft() ? "Meta" : "Control", zoomActivationKeyCode: U = Ft() ? "Meta" : "Control", snapToGrid: J, snapGrid: re, onlyRenderVisibleElements: R = !1, selectNodesOnDrag: G, nodesDraggable: ue, autoPanOnNodeFocus: he, nodesConnectable: Ne, nodesFocusable: Ce, nodeOrigin: ke = Ns, edgesFocusable: et, edgesReconnectable: Be, elementsSelectable: Fe = !0, defaultViewport: Se = hh, minZoom: Me = 0.5, maxZoom: Ie = 2, translateExtent: T = Ot, preventScrolling: F = !0, nodeExtent: q, defaultMarkerColor: ie = "#b1b1b7", zoomOnScroll: le = !0, zoomOnPinch: me = !0, panOnScroll: Ae = !1, panOnScrollSpeed: kt = 0.5, panOnScrollMode: Bn = rt.Free, zoomOnDoubleClick: ca = !0, panOnDrag: la = !0, onPaneClick: ua, onPaneMouseEnter: da, onPaneMouseMove: fa, onPaneMouseLeave: ha, onPaneScroll: pa, onPaneContextMenu: ga, paneClickDistance: ma = 1, nodeClickDistance: ya = 0, children: xa, onReconnect: wa, onReconnectStart: va, onReconnectEnd: ba, onEdgeContextMenu: Sa, onEdgeDoubleClick: _a, onEdgeMouseEnter: Ea, onEdgeMouseMove: Na, onEdgeMouseLeave: Ca, reconnectRadius: ka = 10, onNodesChange: Ma, onEdgesChange: Ia, noDragClassName: Aa = "nodrag", noWheelClassName: Da = "nowheel", noPanClassName: qo = "nopan", fitView: Zo, fitViewOptions: Uo, connectOnClick: Pa, attributionPosition: ja, proOptions: $a, defaultEdgeOptions: Ta, elevateNodesOnSelect: za = !0, elevateEdgesOnSelect: Ra = !1, disableKeyboardA11y: Go = !1, autoPanOnConnect: La, autoPanOnNodeDrag: Ha, autoPanOnSelection: Oa = !0, autoPanSpeed: Va, connectionRadius: Ba, isValidConnection: Fa, onError: Ya, style: Xa, id: Ko, nodeDragThreshold: Wa, connectionDragThreshold: qa, viewport: Za, onViewportChange: Ua, width: Ga, height: Ka, colorMode: Qa = "light", debug: Ja, onScroll: Qo, ariaLabelConfig: ec, zIndexMode: Jo = "basic", ...tc }, nc) {
  const Fn = Ko || "1", oc = yh(Qa), rc = ye((er) => {
    er.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), Qo?.(er);
  }, [Qo]);
  return h.jsx("div", { "data-testid": "rf__wrapper", ...tc, onScroll: rc, style: { ...Xa, ...Op }, ref: nc, className: ge(["react-flow", r, oc]), id: Ko, role: "application", children: h.jsxs(Hp, { nodes: e, edges: t, width: Ga, height: Ka, fitView: Zo, fitViewOptions: Uo, minZoom: Me, maxZoom: Ie, nodeOrigin: ke, nodeExtent: q, zIndexMode: Jo, children: [h.jsx(mh, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: p, onConnectStart: g, onConnectEnd: x, onClickConnectStart: w, onClickConnectEnd: y, nodesDraggable: ue, autoPanOnNodeFocus: he, nodesConnectable: Ne, nodesFocusable: Ce, edgesFocusable: et, edgesReconnectable: Be, elementsSelectable: Fe, elevateNodesOnSelect: za, elevateEdgesOnSelect: Ra, minZoom: Me, maxZoom: Ie, nodeExtent: q, onNodesChange: Ma, onEdgesChange: Ia, snapToGrid: J, snapGrid: re, connectionMode: V, translateExtent: T, connectOnClick: Pa, defaultEdgeOptions: Ta, fitView: Zo, fitViewOptions: Uo, onNodesDelete: B, onEdgesDelete: I, onDelete: $, onNodeDragStart: C, onNodeDrag: A, onNodeDragStop: D, onSelectionDrag: k, onSelectionDragStart: v, onSelectionDragStop: E, onMove: u, onMoveStart: d, onMoveEnd: f, noPanClassName: qo, nodeOrigin: ke, rfId: Fn, autoPanOnConnect: La, autoPanOnNodeDrag: Ha, autoPanSpeed: Va, onError: Ya, connectionRadius: Ba, isValidConnection: Fa, selectNodesOnDrag: G, nodeDragThreshold: Wa, connectionDragThreshold: qa, onBeforeDelete: L, debug: Ja, ariaLabelConfig: ec, zIndexMode: Jo }), h.jsx(Tp, { onInit: c, onNodeClick: a, onEdgeClick: l, onNodeMouseEnter: S, onNodeMouseMove: m, onNodeMouseLeave: b, onNodeContextMenu: N, onNodeDoubleClick: _, nodeTypes: i, edgeTypes: s, connectionLineType: O, connectionLineStyle: Y, connectionLineComponent: W, connectionLineContainerStyle: Q, selectionKeyCode: Z, selectionOnDrag: z, selectionMode: X, deleteKeyCode: ne, multiSelectionKeyCode: ee, panActivationKeyCode: te, zoomActivationKeyCode: U, onlyRenderVisibleElements: R, defaultViewport: Se, translateExtent: T, minZoom: Me, maxZoom: Ie, preventScrolling: F, zoomOnScroll: le, zoomOnPinch: me, zoomOnDoubleClick: ca, panOnScroll: Ae, panOnScrollSpeed: kt, panOnScrollMode: Bn, panOnDrag: la, autoPanOnSelection: Oa, onPaneClick: ua, onPaneMouseEnter: da, onPaneMouseMove: fa, onPaneMouseLeave: ha, onPaneScroll: pa, onPaneContextMenu: ga, paneClickDistance: ma, nodeClickDistance: ya, onSelectionContextMenu: M, onSelectionStart: j, onSelectionEnd: P, onReconnect: wa, onReconnectStart: va, onReconnectEnd: ba, onEdgeContextMenu: Sa, onEdgeDoubleClick: _a, onEdgeMouseEnter: Ea, onEdgeMouseMove: Na, onEdgeMouseLeave: Ca, reconnectRadius: ka, defaultMarkerColor: ie, noDragClassName: Aa, noWheelClassName: Da, noPanClassName: qo, rfId: Fn, disableKeyboardA11y: Go, nodeExtent: q, viewport: Za, onViewportChange: Ua }), h.jsx(fh, { onSelectionChange: H }), xa, h.jsx(ah, { proOptions: $a, position: ja }), h.jsx(sh, { rfId: Fn, disableKeyboardA11y: Go })] }) });
}
var Bp = As(Vp);
function Fp({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return h.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: ge(["react-flow__background-pattern", n, o]) });
}
function Yp({ radius: e, className: t }) {
  return h.jsx("circle", { cx: e, cy: e, r: e, className: ge(["react-flow__background-pattern", "dots", t]) });
}
var Qe;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(Qe || (Qe = {}));
const Xp = {
  [Qe.Dots]: 1,
  [Qe.Lines]: 1,
  [Qe.Cross]: 6
}, Wp = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function ea({
  id: e,
  variant: t = Qe.Dots,
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
  patternClassName: u
}) {
  const d = se(null), { transform: f, patternId: p } = ce(Wp, de), g = o || Xp[t], x = t === Qe.Dots, w = t === Qe.Cross, y = Array.isArray(n) ? n : [n, n], S = [y[0] * f[2] || 1, y[1] * f[2] || 1], m = g * f[2], b = Array.isArray(i) ? i : [i, i], N = w ? [m, m] : S, _ = [
    b[0] * f[2] || 1 + N[0] / 2,
    b[1] * f[2] || 1 + N[1] / 2
  ], C = `${p}${e || ""}`;
  return h.jsxs("svg", { className: ge(["react-flow__background", c]), style: {
    ...l,
    ...Ln,
    "--xy-background-color-props": a,
    "--xy-background-pattern-color-props": s
  }, ref: d, "data-testid": "rf__background", children: [h.jsx("pattern", { id: C, x: f[0] % S[0], y: f[1] % S[1], width: S[0], height: S[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${_[0]},-${_[1]})`, children: x ? h.jsx(Yp, { radius: m / 2, className: u }) : h.jsx(Fp, { dimensions: N, lineWidth: r, variant: t, className: u }) }), h.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${C})` })] });
}
ea.displayName = "Background";
const qp = pe(ea);
function Zp() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: h.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function Up() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: h.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function Gp() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: h.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function Kp() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: h.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function Qp() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: h.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function ln({ children: e, className: t, ...n }) {
  return h.jsx("button", { type: "button", className: ge(["react-flow__controls-button", t]), ...n, children: e });
}
const Jp = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function ta({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: i, onZoomOut: s, onFitView: a, onInteractiveChange: l, className: c, children: u, position: d = "bottom-left", orientation: f = "vertical", "aria-label": p }) {
  const g = fe(), { isInteractive: x, minZoomReached: w, maxZoomReached: y, ariaLabelConfig: S } = ce(Jp, de), { zoomIn: m, zoomOut: b, fitView: N } = Fo(), _ = () => {
    m(), i?.();
  }, C = () => {
    b(), s?.();
  }, A = () => {
    N(r), a?.();
  }, D = () => {
    g.setState({
      nodesDraggable: !x,
      nodesConnectable: !x,
      elementsSelectable: !x
    }), l?.(!x);
  }, B = f === "horizontal" ? "horizontal" : "vertical";
  return h.jsxs(Rn, { className: ge(["react-flow__controls", B, c]), position: d, style: e, "data-testid": "rf__controls", "aria-label": p ?? S["controls.ariaLabel"], children: [t && h.jsxs(h.Fragment, { children: [h.jsx(ln, { onClick: _, className: "react-flow__controls-zoomin", title: S["controls.zoomIn.ariaLabel"], "aria-label": S["controls.zoomIn.ariaLabel"], disabled: y, children: h.jsx(Zp, {}) }), h.jsx(ln, { onClick: C, className: "react-flow__controls-zoomout", title: S["controls.zoomOut.ariaLabel"], "aria-label": S["controls.zoomOut.ariaLabel"], disabled: w, children: h.jsx(Up, {}) })] }), n && h.jsx(ln, { className: "react-flow__controls-fitview", onClick: A, title: S["controls.fitView.ariaLabel"], "aria-label": S["controls.fitView.ariaLabel"], children: h.jsx(Gp, {}) }), o && h.jsx(ln, { className: "react-flow__controls-interactive", onClick: D, title: S["controls.interactive.ariaLabel"], "aria-label": S["controls.interactive.ariaLabel"], children: x ? h.jsx(Qp, {}) : h.jsx(Kp, {}) }), u] });
}
ta.displayName = "Controls";
const eg = pe(ta);
function tg({ id: e, x: t, y: n, width: o, height: r, style: i, color: s, strokeColor: a, strokeWidth: l, className: c, borderRadius: u, shapeRendering: d, selected: f, onClick: p }) {
  const { background: g, backgroundColor: x } = i || {}, w = s || g || x;
  return h.jsx("rect", { className: ge(["react-flow__minimap-node", { selected: f }, c]), x: t, y: n, rx: u, ry: u, width: o, height: r, style: {
    fill: w,
    stroke: a,
    strokeWidth: l
  }, shapeRendering: d, onClick: p ? (y) => p(y, e) : void 0 });
}
const ng = pe(tg), og = (e) => e.nodes.map((t) => t.id), ro = (e) => e instanceof Function ? e : () => e;
function rg({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: i = ng,
  onClick: s
}) {
  const a = ce(og, de), l = ro(t), c = ro(e), u = ro(n), d = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return h.jsx(h.Fragment, { children: a.map((f) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    h.jsx(sg, { id: f, nodeColorFunc: l, nodeStrokeColorFunc: c, nodeClassNameFunc: u, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: i, onClick: s, shapeRendering: d }, f)
  )) });
}
function ig({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: i, shapeRendering: s, NodeComponent: a, onClick: l }) {
  const { node: c, x: u, y: d, width: f, height: p } = ce((g) => {
    const x = g.nodeLookup.get(e);
    if (!x)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const w = x.internals.userNode, { x: y, y: S } = x.internals.positionAbsolute, { width: m, height: b } = Ze(w);
    return {
      node: w,
      x: y,
      y: S,
      width: m,
      height: b
    };
  }, de);
  return !c || c.hidden || !ts(c) ? null : h.jsx(a, { x: u, y: d, width: f, height: p, style: c.style, selected: !!c.selected, className: o(c), color: t(c), borderRadius: r, strokeColor: n(c), strokeWidth: i, shapeRendering: s, onClick: l, id: c.id });
}
const sg = pe(ig);
var ag = pe(rg);
const cg = 200, lg = 150, ug = (e) => !e.hidden, dg = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? Ji(Kt(e.nodeLookup, { filter: ug }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, fg = "react-flow__minimap-desc";
function na({
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
  maskStrokeColor: u,
  maskStrokeWidth: d,
  position: f = "bottom-right",
  onClick: p,
  onNodeClick: g,
  pannable: x = !1,
  zoomable: w = !1,
  ariaLabel: y,
  inversePan: S,
  zoomStep: m = 1,
  offsetScale: b = 5
}) {
  const N = fe(), _ = se(null), { boundingRect: C, viewBB: A, rfId: D, panZoom: B, translateExtent: I, flowWidth: $, flowHeight: H, ariaLabelConfig: v } = ce(dg, de), k = e?.width ?? cg, E = e?.height ?? lg, M = C.width / k, j = C.height / E, P = Math.max(M, j), L = P * k, V = P * E, O = b * P, Y = C.x - (L - C.width) / 2 - O, W = C.y - (V - C.height) / 2 - O, Q = L + O * 2, ne = V + O * 2, Z = `${fg}-${D}`, z = se(0), X = se();
  z.current = P, ae(() => {
    if (_.current && B)
      return X.current = Cf({
        domNode: _.current,
        panZoom: B,
        getTransform: () => N.getState().transform,
        getViewScale: () => z.current
      }), () => {
        X.current?.destroy();
      };
  }, [B]), ae(() => {
    X.current?.update({
      translateExtent: I,
      width: $,
      height: H,
      inversePan: S,
      pannable: x,
      zoomStep: m,
      zoomable: w
    });
  }, [x, w, S, m, I, $, H]);
  const te = p ? (J) => {
    const [re, R] = X.current?.pointer(J) || [0, 0];
    p(J, { x: re, y: R });
  } : void 0, ee = g ? ye((J, re) => {
    const R = N.getState().nodeLookup.get(re).internals.userNode;
    g(J, R);
  }, []) : void 0, U = y ?? v["minimap.ariaLabel"];
  return h.jsx(Rn, { position: f, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-background-color-props": typeof c == "string" ? c : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof d == "number" ? d * P : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof s == "number" ? s : void 0
  }, className: ge(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: h.jsxs("svg", { width: k, height: E, viewBox: `${Y} ${W} ${Q} ${ne}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": Z, ref: _, onClick: te, children: [U && h.jsx("title", { id: Z, children: U }), h.jsx(ag, { onClick: ee, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: i, nodeClassName: r, nodeStrokeWidth: s, nodeComponent: a }), h.jsx("path", { className: "react-flow__minimap-mask", d: `M${Y - O},${W - O}h${Q + O * 2}v${ne + O * 2}h${-Q - O * 2}z
        M${A.x},${A.y}h${A.width}v${A.height}h${-A.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
na.displayName = "MiniMap";
const hg = pe(na), pg = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, gg = {
  [bt.Line]: "right",
  [bt.Handle]: "bottom-right"
};
function mg({ nodeId: e, position: t, variant: n = bt.Handle, className: o, style: r = void 0, children: i, color: s, minWidth: a = 10, minHeight: l = 10, maxWidth: c = Number.MAX_VALUE, maxHeight: u = Number.MAX_VALUE, keepAspectRatio: d = !1, resizeDirection: f, autoScale: p = !0, shouldResize: g, onResizeStart: x, onResize: w, onResizeEnd: y }) {
  const S = $s(), m = typeof e == "string" ? e : S, b = fe(), N = se(null), _ = n === bt.Handle, C = ce(ye(pg(_ && p), [_, p]), de), A = se(null), D = t ?? gg[n];
  ae(() => {
    if (!(!N.current || !m))
      return A.current || (A.current = Hf({
        domNode: N.current,
        nodeId: m,
        getStoreItems: () => {
          const { nodeLookup: I, transform: $, snapGrid: H, snapToGrid: v, nodeOrigin: k, domNode: E } = b.getState();
          return {
            nodeLookup: I,
            transform: $,
            snapGrid: H,
            snapToGrid: v,
            nodeOrigin: k,
            paneDomNode: E
          };
        },
        onChange: (I, $) => {
          const { triggerNodeChanges: H, nodeLookup: v, parentLookup: k, nodeOrigin: E } = b.getState(), M = [], j = { x: I.x, y: I.y }, P = v.get(m);
          if (P && P.expandParent && P.parentId) {
            const L = P.origin ?? E, V = I.width ?? P.measured.width ?? 0, O = I.height ?? P.measured.height ?? 0, Y = {
              id: P.id,
              parentId: P.parentId,
              rect: {
                width: V,
                height: O,
                ...ns({
                  x: I.x ?? P.position.x,
                  y: I.y ?? P.position.y
                }, { width: V, height: O }, P.parentId, v, L)
              }
            }, W = Bo([Y], v, k, E);
            M.push(...W), j.x = I.x ? Math.max(L[0] * V, I.x) : void 0, j.y = I.y ? Math.max(L[1] * O, I.y) : void 0;
          }
          if (j.x !== void 0 && j.y !== void 0) {
            const L = {
              id: m,
              type: "position",
              position: { ...j }
            };
            M.push(L);
          }
          if (I.width !== void 0 && I.height !== void 0) {
            const V = {
              id: m,
              type: "dimensions",
              resizing: !0,
              setAttributes: f ? f === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: I.width,
                height: I.height
              }
            };
            M.push(V);
          }
          for (const L of $) {
            const V = {
              ...L,
              type: "position"
            };
            M.push(V);
          }
          H(M);
        },
        onEnd: ({ width: I, height: $ }) => {
          const H = {
            id: m,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: I,
              height: $
            }
          };
          b.getState().triggerNodeChanges([H]);
        }
      })), A.current.update({
        controlPosition: D,
        boundaries: {
          minWidth: a,
          minHeight: l,
          maxWidth: c,
          maxHeight: u
        },
        keepAspectRatio: d,
        resizeDirection: f,
        onResizeStart: x,
        onResize: w,
        onResizeEnd: y,
        shouldResize: g
      }), () => {
        A.current?.destroy();
      };
  }, [
    D,
    a,
    l,
    c,
    u,
    d,
    x,
    w,
    y,
    g
  ]);
  const B = D.split("-");
  return h.jsx("div", { className: ge(["react-flow__resize-control", "nodrag", ...B, n, o]), ref: N, style: {
    ...r,
    scale: C,
    ...s && { [_ ? "backgroundColor" : "borderColor"]: s }
  }, children: i });
}
pe(mg);
const yg = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), oa = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var xg = {
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
const wg = kn(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: o,
    className: r = "",
    children: i,
    iconNode: s,
    ...a
  }, l) => so(
    "svg",
    {
      ref: l,
      ...xg,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: oa("lucide", r),
      ...a
    },
    [
      ...s.map(([c, u]) => so(c, u)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const ve = (e, t) => {
  const n = kn(
    ({ className: o, ...r }, i) => so(wg, {
      ref: i,
      iconNode: t,
      className: oa(`lucide-${yg(e)}`, o),
      ...r
    })
  );
  return n.displayName = `${e}`, n;
};
const vg = ve("Boxes", [
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
const On = ve("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const bg = ve("ChevronDown", [
  ["path", { d: "m6 9 6 6 6-6", key: "qrunsl" }]
]);
const Sg = ve("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const gn = ve("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const Xt = ve("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const _g = ve("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const Eg = ve("ListTree", [
  ["path", { d: "M21 12h-8", key: "1bmf0i" }],
  ["path", { d: "M21 6H8", key: "1pqkrb" }],
  ["path", { d: "M21 18h-8", key: "1tm79t" }],
  ["path", { d: "M3 6v4c0 1.1.9 2 2 2h3", key: "1ywdgy" }],
  ["path", { d: "M3 10v6c0 1.1.9 2 2 2h3", key: "2wc746" }]
]);
const ra = ve("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const Ng = ve("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const Cg = ve("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const kg = ve("Save", [
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
const Mg = ve("Search", [
  ["circle", { cx: "11", cy: "11", r: "8", key: "4ej97u" }],
  ["path", { d: "m21 21-4.3-4.3", key: "1qie3q" }]
]);
const _t = ve("Sparkles", [
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
const di = ve("Trash2", [
  ["path", { d: "M3 6h18", key: "d0wm0j" }],
  ["path", { d: "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", key: "4alrt4" }],
  ["path", { d: "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", key: "v07s0e" }],
  ["line", { x1: "10", x2: "10", y1: "11", y2: "17", key: "1uufr5" }],
  ["line", { x1: "14", x2: "14", y1: "11", y2: "17", key: "xtxkd" }]
]), Re = "/_elsa/workflow-management";
async function Ig(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), Je(e, `${Re}/definitions?${n.toString()}`);
}
async function Ag(e, t) {
  return Je(e, `${Re}/definitions/${encodeURIComponent(t)}`);
}
async function Dg(e, t) {
  return Jt(e, `${Re}/definitions`, t);
}
async function Pg(e, t) {
  await Je(e, `${Re}/definitions/${encodeURIComponent(t)}`, {
    method: "DELETE"
  });
}
async function jg(e, t) {
  await Jt(e, `${Re}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function $g(e, t) {
  await Je(e, `${Re}/definitions/${encodeURIComponent(t)}/permanent`, {
    method: "DELETE"
  });
}
async function Tg(e, t) {
  return Je(e, `${Re}/drafts/${encodeURIComponent(t.id)}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify({ state: t.state, layout: t.layout })
  });
}
async function zg(e, t) {
  return Jt(e, `${Re}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function Rg(e, t) {
  return Jt(e, `${Re}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function ia(e, t) {
  return Jt(e, `${Re}/executables/${encodeURIComponent(t)}/run`, {});
}
async function Lg(e) {
  return Je(e, "/_demo/workflows/executables");
}
async function sa(e) {
  return Je(e, `${Re}/activities`);
}
async function Jt(e, t, n) {
  return Je(e, t, {
    method: "POST",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify(n)
  });
}
async function Je(e, t, n) {
  const o = await fetch(new URL(t, e.baseUrl).toString(), n), r = await o.text();
  if (!o.ok)
    throw new Error(Hg(r) || `Request failed with ${o.status}.`);
  return r ? JSON.parse(r) : {};
}
function Hg(e) {
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
const Xo = "elsa.sequence.structure", Vn = "elsa.flowchart.structure";
function fi(e, t) {
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
  const n = t.payload, o = Kg(t), r = io(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: Qg(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, i]) => io(i)).map(([i, s]) => ({
    id: `${t.kind}:${i}`,
    label: e0(i),
    property: i,
    mode: "generic",
    activities: io(s) ?? []
  }));
}
function Og(e, t, n) {
  const o = new Map(t.map((s) => [s.activityVersionId, s])), r = new Map(n.map((s) => [s.nodeId, s])), i = e.slot.activities.map((s, a) => {
    const l = o.get(s.activityVersionId), c = r.get(s.nodeId) ?? Jg(e.slot.mode, a);
    return {
      id: s.nodeId,
      type: "workflowActivity",
      position: { x: c.x, y: c.y },
      data: {
        label: l?.displayName ?? s.activityVersionId,
        activityVersionId: s.activityVersionId,
        activityTypeKey: l?.activityTypeKey,
        childSlots: We(s)
      }
    };
  });
  return {
    nodes: i,
    edges: e.slot.mode === "flowchart" ? Ug(e.owner) : Zg(e.slot, i)
  };
}
function So(e, t, n) {
  if (t.length === 0) {
    const a = We(e)[0];
    return a ? Cn(e, a, n) : e;
  }
  const [o, ...r] = t, i = We(e).find((a) => a.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((a) => a.nodeId === o.ownerNodeId ? So(a, r, n) : a);
  return Cn(e, i, s);
}
function _o(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, i = We(e).find((a) => a.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((a) => a.nodeId === o.ownerNodeId ? _o(a, r, n) : a);
  return Cn(e, i, s);
}
function Cn(e, t, n) {
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
function Vg(e, t, n) {
  const o = new Map(e.slot.activities.map((i) => [i.nodeId, i])), r = t.map((i) => o.get(i.id)).filter((i) => !!i);
  return e.slot.mode === "sequence" && r.sort((i, s) => {
    const a = t.find((c) => c.id === i.nodeId), l = t.find((c) => c.id === s.nodeId);
    return (a?.position.x ?? 0) - (l?.position.x ?? 0);
  }), Cn(e.owner, e.slot, r);
}
function Bg(e, t) {
  return {
    ...e,
    structure: qg(e.structure, t)
  };
}
function Fg(e, t) {
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
function Yg(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: Wg(e)
  };
}
function Oe(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? Xg(t) : n;
}
function Xg(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function Wg(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: Xo,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: Vn,
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
function qg(e, t) {
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
function Zg(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function Ug(e) {
  if (e.structure?.kind !== Vn) return [];
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
function io(e) {
  return Array.isArray(e) ? e.filter(Gg) : null;
}
function Gg(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Kg(e) {
  return e.kind === Xo ? "sequence" : e.kind === Vn ? "flowchart" : "generic";
}
function Qg(e) {
  return e.kind === Xo || e.kind === Vn, "Activities";
}
function Jg(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function e0(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const t0 = { workflowActivity: b0 }, hi = "application/x-elsa-activity-version-id", n0 = 6, o0 = 1200, r0 = [10, 25, 50], i0 = 10;
function N0(e) {
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
    component: () => /* @__PURE__ */ h.jsx(s0, { context: e.backend, ai: e.ai })
  }), e.routes.add({
    id: "workflows-executables",
    path: "/workflows/executables",
    label: "Workflow executables",
    component: () => /* @__PURE__ */ h.jsx(a0, { context: e.backend, ai: e.ai })
  }), e.routes.add({
    id: "workflows-instances",
    path: "/workflows/instances",
    label: "Workflow instances",
    component: () => /* @__PURE__ */ h.jsx(c0, { ai: e.ai })
  });
}
function s0({ context: e, ai: t }) {
  const [n, o] = oe(pi);
  ae(() => {
    const i = () => o(pi());
    return window.addEventListener("popstate", i), () => window.removeEventListener("popstate", i);
  }, []);
  const r = (i) => {
    const s = i ? `/workflows/definitions?definition=${encodeURIComponent(i)}` : "/workflows/definitions";
    window.history.pushState({}, "", s), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return n ? /* @__PURE__ */ h.jsx(v0, { context: e, definitionId: n, ai: t, onBack: () => r(null) }) : /* @__PURE__ */ h.jsx(Wo, { activePath: "/workflows/definitions", title: "Definitions", children: /* @__PURE__ */ h.jsx(l0, { context: e, ai: t, onOpen: r }) });
}
function a0({ context: e, ai: t }) {
  const [n, o] = oe(gi);
  return ae(() => {
    const r = () => o(gi());
    return window.addEventListener("popstate", r), () => window.removeEventListener("popstate", r);
  }, []), /* @__PURE__ */ h.jsx(Wo, { activePath: "/workflows/executables", title: "Executables", children: /* @__PURE__ */ h.jsx(d0, { context: e, ai: t, definitionFilter: n }) });
}
function c0({ ai: e }) {
  const t = Et(e, "weaver.workflows.explain-instance");
  return /* @__PURE__ */ h.jsx(Wo, { activePath: "/workflows/instances", title: "Instances", children: /* @__PURE__ */ h.jsxs("div", { className: "wf-empty", children: [
    "Workflow instance history will appear here when the runtime exposes an instance query endpoint.",
    t ? /* @__PURE__ */ h.jsxs("button", { type: "button", className: "wf-ai-inline-action", onClick: () => Nt(e, t, { scope: "workflow-instances" }), children: [
      /* @__PURE__ */ h.jsx(_t, { size: 13 }),
      " Ask Weaver about instances"
    ] }) : null
  ] }) });
}
function Wo({ activePath: e, title: t, children: n }) {
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
function pi() {
  return new URLSearchParams(window.location.search).get("definition");
}
function gi() {
  return new URLSearchParams(window.location.search).get("definition");
}
function l0({ context: e, ai: t, onOpen: n }) {
  const [o, r] = oe(""), [i, s] = oe("active"), [a, l] = oe(1), [c, u] = oe(i0), [d, f] = oe("loading"), [p, g] = oe(""), [x, w] = oe(""), [y, S] = oe([]), [m, b] = oe(0), [N, _] = oe(() => /* @__PURE__ */ new Set()), [C, A] = oe(null), [D, B] = oe(!1), [I, $] = oe([]), [H, v] = oe("idle"), k = se(null), E = xe(() => y.map((R) => R.id), [y]), M = Et(t, "weaver.workflows.suggest-create-metadata"), j = Et(t, "weaver.workflows.explain-definition"), P = E.filter((R) => N.has(R)).length, L = E.length > 0 && P === E.length, V = ye(async () => {
    f("loading"), g("");
    try {
      const R = await Ig(e, { search: o, state: i, page: a, pageSize: c }), G = typeof R.totalCount == "number", ue = R.totalCount ?? R.definitions.length, he = aa(ue, c);
      if (ue > 0 && a > he) {
        l(he);
        return;
      }
      S(G ? R.definitions : h0(R.definitions, a, c)), b(ue), f("ready");
    } catch (R) {
      g(R instanceof Error ? R.message : String(R)), f("failed");
    }
  }, [e, o, i, a, c]);
  ae(() => {
    V();
  }, [V]), ae(() => {
    k.current && (k.current.indeterminate = P > 0 && !L);
  }, [L, P]);
  const O = ye(async () => {
    if (!(H === "loading" || H === "ready")) {
      v("loading");
      try {
        const R = await sa(e);
        $(R.activities ?? []), v("ready");
      } catch (R) {
        v("failed"), g(R instanceof Error ? R.message : String(R));
      }
    }
  }, [H, e]), Y = () => {
    g(""), w(""), A({ name: "", description: "", rootKind: "flowchart" }), O();
  }, W = async () => {
    if (C?.name.trim()) {
      B(!0), g(""), w("");
      try {
        const R = await Dg(e, {
          name: C.name.trim(),
          description: C.description.trim() || null,
          rootKind: C.rootKind
        });
        A(null), n(R.definition.id);
      } catch (R) {
        g(R instanceof Error ? R.message : String(R));
      } finally {
        B(!1);
      }
    }
  }, Q = (R) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(R)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, ne = async () => {
    if (y.length === 1 && a > 1) {
      l(a - 1);
      return;
    }
    await V();
  }, Z = () => _(/* @__PURE__ */ new Set()), z = (R, G) => {
    _((ue) => {
      const he = new Set(ue);
      return G ? he.add(R) : he.delete(R), he;
    });
  }, X = (R) => {
    _((G) => {
      const ue = new Set(G);
      for (const he of E)
        R ? ue.add(he) : ue.delete(he);
      return ue;
    });
  }, te = (R) => {
    s(R), l(1), Z();
  }, ee = (R) => {
    r(R), l(1), Z();
  }, U = async (R) => {
    if (window.confirm(`Delete workflow definition "${R.name}"? You can restore it from the Deleted view.`)) {
      w(""), g("");
      try {
        await Pg(e, R.id), z(R.id, !1), w(`Deleted ${R.name}`), await ne();
      } catch (G) {
        g(G instanceof Error ? G.message : String(G));
      }
    }
  }, J = async (R) => {
    w(""), g("");
    try {
      await jg(e, R.id), z(R.id, !1), w(`Restored ${R.name}`), await ne();
    } catch (G) {
      g(G instanceof Error ? G.message : String(G));
    }
  }, re = async (R) => {
    if (window.confirm(`Permanently delete workflow definition "${R.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`)) {
      w(""), g("");
      try {
        await $g(e, R.id), z(R.id, !1), w(`Permanently deleted ${R.name}`), await ne();
      } catch (G) {
        g(G instanceof Error ? G.message : String(G));
      }
    }
  };
  return /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
    /* @__PURE__ */ h.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ h.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ h.jsx("button", { type: "button", className: i === "active" ? "active" : "", "aria-selected": i === "active", onClick: () => te("active"), children: "Active" }),
        /* @__PURE__ */ h.jsx("button", { type: "button", className: i === "deleted" ? "active" : "", "aria-selected": i === "deleted", onClick: () => te("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ h.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ h.jsx(Mg, { size: 15 }),
        /* @__PURE__ */ h.jsx("input", { value: o, onChange: (R) => ee(R.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
        V();
      }, children: "Refresh" }),
      /* @__PURE__ */ h.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ h.jsxs("button", { type: "button", title: "Create workflow", onClick: Y, children: [
        /* @__PURE__ */ h.jsx(Ng, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    d === "failed" ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Xt, { size: 16 }),
      " ",
      p
    ] }) : null,
    d !== "failed" && p ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Xt, { size: 16 }),
      " ",
      p
    ] }) : null,
    x ? /* @__PURE__ */ h.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ h.jsx(On, { size: 14 }),
      " ",
      x
    ] }) : null,
    N.size > 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ h.jsxs("span", { children: [
        N.size,
        " selected"
      ] }),
      /* @__PURE__ */ h.jsx("button", { type: "button", onClick: Z, children: "Clear selection" })
    ] }) : null,
    d === "loading" ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: "Loading workflow definitions..." }) : null,
    d === "ready" && y.length === 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-empty", children: [
      "No ",
      i,
      " workflow definitions found."
    ] }) : null,
    d === "ready" && y.length > 0 ? /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
      /* @__PURE__ */ h.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ h.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ h.jsx(
            "input",
            {
              ref: k,
              type: "checkbox",
              checked: L,
              onChange: (R) => X(R.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ h.jsx("span", { children: "Name" }),
          /* @__PURE__ */ h.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ h.jsx("span", { children: i === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ h.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ h.jsx("span", { children: "Actions" })
        ] }),
        y.map((R) => /* @__PURE__ */ h.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${R.name}`,
            "aria-selected": N.has(R.id),
            tabIndex: 0,
            onClick: () => n(R.id),
            onKeyDown: (G) => {
              G.currentTarget === G.target && (G.key !== "Enter" && G.key !== " " || (G.preventDefault(), n(R.id)));
            },
            children: [
              /* @__PURE__ */ h.jsx("label", { className: "wf-row-select", onClick: (G) => G.stopPropagation(), children: /* @__PURE__ */ h.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: N.has(R.id),
                  onChange: (G) => z(R.id, G.target.checked),
                  "aria-label": `Select workflow definition ${R.name}`
                }
              ) }),
              /* @__PURE__ */ h.jsxs("span", { children: [
                /* @__PURE__ */ h.jsx("strong", { children: R.name }),
                /* @__PURE__ */ h.jsx("small", { children: R.description || R.id })
              ] }),
              /* @__PURE__ */ h.jsx("span", { children: R.latestVersion ?? "No version" }),
              /* @__PURE__ */ h.jsx("span", { children: i === "deleted" ? Eo(R.deletedAt) : R.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ h.jsx("span", { children: Eo(R.lastModifiedAt) }),
              /* @__PURE__ */ h.jsx("span", { className: "wf-row-actions", onClick: (G) => G.stopPropagation(), children: i === "active" ? /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
                /* @__PURE__ */ h.jsx("button", { type: "button", onClick: (G) => {
                  G.stopPropagation(), n(R.id);
                }, children: "Open" }),
                /* @__PURE__ */ h.jsx("button", { type: "button", onClick: (G) => {
                  G.stopPropagation(), Q(R.id);
                }, children: "Artifacts" }),
                j ? /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => Nt(t, j, R), children: [
                  /* @__PURE__ */ h.jsx(_t, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ h.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  U(R);
                }, children: [
                  /* @__PURE__ */ h.jsx(di, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
                /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
                  J(R);
                }, children: [
                  /* @__PURE__ */ h.jsx(Cg, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ h.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  re(R);
                }, children: [
                  /* @__PURE__ */ h.jsx(di, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          R.id
        ))
      ] }),
      /* @__PURE__ */ h.jsx(
        f0,
        {
          page: a,
          pageSize: c,
          totalCount: m,
          onPageChange: l,
          onPageSizeChange: (R) => {
            u(R), l(1);
          }
        }
      )
    ] }) : null,
    C ? /* @__PURE__ */ h.jsx(
      u0,
      {
        draft: C,
        activities: I,
        catalogState: H,
        creating: D,
        suggestMetadataAction: M,
        onSuggestMetadata: M ? () => Nt(t, M, { draft: C, activities: I }) : void 0,
        onChange: (R) => A(R),
        onClose: () => A(null),
        onSubmit: W
      }
    ) : null
  ] });
}
function u0({ draft: e, activities: t, catalogState: n, creating: o, suggestMetadataAction: r, onSuggestMetadata: i, onChange: s, onClose: a, onSubmit: l }) {
  const c = xe(() => p0(t), [t]);
  return /* @__PURE__ */ h.jsx("div", { className: "wf-dialog-backdrop", role: "presentation", children: /* @__PURE__ */ h.jsx("section", { className: "wf-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "workflow-create-title", children: /* @__PURE__ */ h.jsxs(
    "form",
    {
      onSubmit: (u) => {
        u.preventDefault(), l();
      },
      children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-dialog-heading", children: [
          /* @__PURE__ */ h.jsx("h3", { id: "workflow-create-title", children: "Create Workflow" }),
          r ? /* @__PURE__ */ h.jsxs("button", { type: "button", className: "wf-ai-action", onClick: i, title: r.description ?? r.label, children: [
            /* @__PURE__ */ h.jsx(_t, { size: 13 }),
            " ",
            r.label
          ] }) : null
        ] }),
        /* @__PURE__ */ h.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ h.jsx("span", { children: "Display name" }),
          /* @__PURE__ */ h.jsx(
            "input",
            {
              autoFocus: !0,
              "aria-label": "Display name",
              value: e.name,
              onChange: (u) => s({ ...e, name: u.target.value })
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
              onChange: (u) => s({ ...e, description: u.target.value })
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
              onChange: (u) => s({ ...e, rootKind: u.target.value }),
              disabled: n === "loading",
              children: [
                /* @__PURE__ */ h.jsx("optgroup", { label: "Composite roots", children: c.compositeRoots.map((u) => /* @__PURE__ */ h.jsx("option", { value: u.value, children: u.label }, u.value)) }),
                c.otherCategories.map((u) => /* @__PURE__ */ h.jsx("optgroup", { label: u.name, children: u.activities.map((d) => /* @__PURE__ */ h.jsx("option", { value: `unsupported:${d.activityVersionId}`, disabled: !0, children: Oe(d) }, d.activityVersionId)) }, u.name))
              ]
            }
          )
        ] }),
        n === "loading" ? /* @__PURE__ */ h.jsx("div", { className: "wf-dialog-note", children: "Loading activity catalog..." }) : null,
        n === "failed" ? /* @__PURE__ */ h.jsx("div", { className: "wf-dialog-note", children: "Activity catalog could not be loaded. Composite roots remain available." }) : null,
        /* @__PURE__ */ h.jsxs("div", { className: "wf-dialog-actions", children: [
          /* @__PURE__ */ h.jsx("button", { type: "button", onClick: a, disabled: o, children: "Cancel" }),
          /* @__PURE__ */ h.jsx("button", { type: "submit", disabled: o || !e.name.trim(), children: o ? "Creating..." : "Create" })
        ] })
      ]
    }
  ) }) });
}
function d0({ context: e, ai: t, definitionFilter: n }) {
  const [o, r] = oe("loading"), [i, s] = oe(""), [a, l] = oe(""), [c, u] = oe([]), d = xe(
    () => n ? c.filter((x) => x.definitionId === n || x.sourceId === n) : c,
    [n, c]
  ), f = Et(t, "weaver.workflows.explain-executable"), p = ye(async () => {
    r("loading"), s("");
    try {
      u(await Lg(e)), r("ready");
    } catch (x) {
      s(x instanceof Error ? x.message : String(x)), r("failed");
    }
  }, [e]);
  ae(() => {
    p();
  }, [p]);
  const g = async (x) => {
    l(""), s("");
    try {
      await ia(e, x.artifactId), l(`Started ${x.artifactId}`);
    } catch (w) {
      s(w instanceof Error ? w.message : String(w));
    }
  };
  return /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
    /* @__PURE__ */ h.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
        p();
      }, children: "Refresh" }),
      n ? /* @__PURE__ */ h.jsxs("span", { className: "wf-filter-chip", children: [
        "Definition ",
        n
      ] }) : null
    ] }),
    o === "failed" ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Xt, { size: 16 }),
      " ",
      i
    ] }) : null,
    a ? /* @__PURE__ */ h.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ h.jsx(On, { size: 14 }),
      " ",
      a
    ] }) : null,
    o === "loading" ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: "Loading workflow executables..." }) : null,
    o === "ready" && d.length === 0 ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: n ? "No workflow executables found for this definition." : "No workflow executables found. Publish a workflow definition to create one." }) : null,
    o === "ready" && d.length > 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ h.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ h.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ h.jsx("span", { children: "Version" }),
        /* @__PURE__ */ h.jsx("span", { children: "Source" }),
        /* @__PURE__ */ h.jsx("span", { children: "Root" }),
        /* @__PURE__ */ h.jsx("span", { children: "Published" }),
        /* @__PURE__ */ h.jsx("span", { children: "Actions" })
      ] }),
      d.map((x) => /* @__PURE__ */ h.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ h.jsxs("span", { children: [
          /* @__PURE__ */ h.jsx("strong", { children: x.artifactId }),
          /* @__PURE__ */ h.jsx("small", { children: x.artifactHash })
        ] }),
        /* @__PURE__ */ h.jsx("span", { children: x.artifactVersion }),
        /* @__PURE__ */ h.jsx("span", { children: y0(x) }),
        /* @__PURE__ */ h.jsx("span", { children: x0(x) }),
        /* @__PURE__ */ h.jsx("span", { children: Eo(x.publishedAt ?? x.createdAt) }),
        /* @__PURE__ */ h.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
            g(x);
          }, children: [
            /* @__PURE__ */ h.jsx(ra, { size: 13 }),
            " Run"
          ] }),
          f ? /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => Nt(t, f, x), children: [
            /* @__PURE__ */ h.jsx(_t, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, x.artifactId))
    ] }) : null
  ] });
}
function f0({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const i = aa(n, t), s = n === 0 ? 0 : (e - 1) * t + 1, a = Math.min(e * t, n);
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
      /* @__PURE__ */ h.jsx("select", { value: t, onChange: (l) => r(Number(l.target.value)), children: r0.map((l) => /* @__PURE__ */ h.jsx("option", { value: l, children: l }, l)) })
    ] }),
    /* @__PURE__ */ h.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ h.jsx(Sg, { size: 14 }),
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
        /* @__PURE__ */ h.jsx(gn, { size: 14 })
      ] })
    ] })
  ] });
}
function h0(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function aa(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Et(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function Nt(e, t, n) {
  const o = t.createPrompt(n);
  o && e.dispatchPrompt(o);
}
function p0(e) {
  const t = [
    { value: "flowchart", label: "Flowchart" },
    { value: "sequence", label: "Sequence" }
  ], n = /* @__PURE__ */ new Map();
  for (const r of e) {
    if (m0(r)) continue;
    const i = r.category || "Uncategorized";
    n.set(i, [...n.get(i) ?? [], r]);
  }
  const o = Array.from(n.entries()).sort(([r], [i]) => r.localeCompare(i)).map(([r, i]) => ({
    name: r,
    activities: i.sort((s, a) => Oe(s).localeCompare(Oe(a)))
  }));
  return { compositeRoots: t, otherCategories: o };
}
function g0(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "Uncategorized";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [o]) => n.localeCompare(o)).map(([n, o]) => ({
    category: n,
    activities: o.sort((r, i) => Oe(r).localeCompare(Oe(i)))
  }));
}
function m0(e) {
  const t = Oe(e);
  return t === "Flowchart" || t === "Sequence" || e.activityTypeKey.endsWith(".Flowchart") || e.activityTypeKey.endsWith(".Sequence");
}
function y0(e) {
  return e.sourceKind || e.sourceId || e.sourceVersion ? [e.sourceKind, e.sourceId, e.sourceVersion].filter(Boolean).join(" / ") : e.definitionId;
}
function x0(e) {
  return w0(e.rootActivityType) || e.rootActivityType;
}
function w0(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function v0({ context: e, definitionId: t, ai: n, onBack: o }) {
  const [r, i] = oe(null), [s, a] = oe(null), [l, c] = oe([]), [u, d] = oe([]), [f, p] = oe([]), [g, x] = oe([]), [w, y] = oe(null), [S, m] = oe(null), [b, N] = oe(""), [_, C] = oe(""), [A, D] = oe(!1), [B, I] = oe(null), [$, H] = oe(() => /* @__PURE__ */ new Set()), v = se(null), k = se(""), E = se(0), M = se(null), j = se(!1), P = s?.state.rootActivity ?? null, L = xe(() => fi(P, u), [P, u]), V = xe(() => new Map(l.map((T) => [T.activityVersionId, T])), [l]), O = xe(() => g0(l), [l]), Y = xe(() => L?.slot.activities.find((T) => T.nodeId === S) ?? null, [L, S]), W = Y ? We(Y) : [], Q = Et(n, "weaver.workflows.find-draft-risks"), ne = Et(n, "weaver.workflows.propose-update"), Z = ye(async () => {
    N("");
    const [T, F] = await Promise.all([
      Ag(e, t),
      sa(e)
    ]), q = T.draft ?? null;
    i(T), k.current = q ? Dt(q) : "", a(q), c(F.activities ?? []), d([]), m(null);
  }, [e, t]);
  ae(() => {
    Z().catch((T) => N(T instanceof Error ? T.message : String(T)));
  }, [Z]), ae(() => {
    H((T) => {
      let F = !1;
      const q = new Set(T);
      for (const ie of O)
        q.has(ie.category) || (q.add(ie.category), F = !0);
      return F ? q : T;
    });
  }, [O]), ae(() => {
    if (!L) {
      p([]), x([]);
      return;
    }
    const T = Og(L, l, s?.layout ?? []);
    p(T.nodes), x(T.edges);
  }, [L, l, s?.layout]);
  const z = (T) => {
    a((F) => F && { ...F, state: { ...F.state, rootActivity: T } });
  }, X = ye((T, F) => {
    const q = Yg(T, _0(T));
    if (!s?.state.rootActivity) {
      z(q), m(q.nodeId);
      return;
    }
    if (!L) {
      if (!We(q)[0]) {
        C(""), N("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      a((le) => {
        if (!le?.state.rootActivity) return le;
        const me = le.state.rootActivity, Ae = So(q, [], [me]), kt = F ? [
          ...le.layout.filter((Bn) => Bn.nodeId !== me.nodeId),
          {
            nodeId: me.nodeId,
            x: Math.round(F.x),
            y: Math.round(F.y)
          }
        ] : le.layout;
        return {
          ...le,
          layout: kt,
          state: {
            ...le.state,
            rootActivity: Ae
          }
        };
      }), m(s.state.rootActivity.nodeId), N(""), C(`Wrapped root in ${Oe(T)}`);
      return;
    }
    a((ie) => {
      if (!ie?.state.rootActivity) return ie;
      const le = fi(ie.state.rootActivity, u);
      if (!le) return ie;
      const me = So(ie.state.rootActivity, u, [...le.slot.activities, q]), Ae = F ? [
        ...ie.layout.filter((kt) => kt.nodeId !== q.nodeId),
        {
          nodeId: q.nodeId,
          x: Math.round(F.x),
          y: Math.round(F.y)
        }
      ] : ie.layout;
      return {
        ...ie,
        layout: Ae,
        state: {
          ...ie.state,
          rootActivity: me
        }
      };
    }), m(q.nodeId);
  }, [s?.state.rootActivity, u, L]), te = ye((T, F) => {
    if (!v.current) return null;
    const q = v.current.getBoundingClientRect();
    return w ? w.screenToFlowPosition({ x: T, y: F }) : {
      x: T - q.left,
      y: F - q.top
    };
  }, [w]), ee = ye((T, F, q) => {
    if (!v.current) return !1;
    const ie = v.current.getBoundingClientRect();
    if (!(F >= ie.left && F <= ie.right && q >= ie.top && q <= ie.bottom)) return !1;
    const me = te(F, q);
    return me ? (X(T, me), !0) : !1;
  }, [X, te]);
  ae(() => {
    const T = (q) => {
      const ie = M.current;
      if (!ie) return;
      Math.hypot(q.clientX - ie.startX, q.clientY - ie.startY) >= n0 && (ie.dragging = !0);
    }, F = (q) => {
      const ie = M.current;
      if (M.current = null, !ie?.dragging || !v.current) return;
      const le = v.current.getBoundingClientRect();
      q.clientX >= le.left && q.clientX <= le.right && q.clientY >= le.top && q.clientY <= le.bottom && (j.current = !0, window.setTimeout(() => {
        j.current = !1;
      }, 0), ee(ie.activity, q.clientX, q.clientY));
    };
    return window.addEventListener("pointermove", T), window.addEventListener("pointerup", F), window.addEventListener("pointercancel", F), () => {
      window.removeEventListener("pointermove", T), window.removeEventListener("pointerup", F), window.removeEventListener("pointercancel", F);
    };
  }, [w, ee]);
  const U = (T, F) => {
    T.dataTransfer.setData(hi, F.activityVersionId), T.dataTransfer.setData("text/plain", F.activityVersionId), T.dataTransfer.effectAllowed = "copy";
  }, J = (T, F) => {
    T.clientX === 0 && T.clientY === 0 || ee(F, T.clientX, T.clientY) && (j.current = !0, window.setTimeout(() => {
      j.current = !1;
    }, 0));
  }, re = (T, F) => {
    T.button === 0 && (M.current = {
      activity: F,
      startX: T.clientX,
      startY: T.clientY,
      dragging: !1
    });
  }, R = (T) => {
    j.current || X(T);
  }, G = (T) => {
    T.preventDefault(), T.dataTransfer.dropEffect = "copy";
  }, ue = (T) => {
    T.preventDefault();
    const F = T.dataTransfer.getData(hi) || T.dataTransfer.getData("text/plain"), q = V.get(F);
    q && ee(q, T.clientX, T.clientY);
  }, he = ye(async (T, F) => {
    const q = ++E.current, ie = Dt(T);
    N("");
    try {
      const le = await Tg(e, T), me = Dt(le);
      k.current = me, a((Ae) => !Ae || Ae.id !== le.id ? Ae : Dt(Ae) === ie ? le : { ...Ae, validationErrors: le.validationErrors }), q === E.current && C(F);
    } catch (le) {
      q === E.current && (C(""), N(le instanceof Error ? le.message : String(le)));
    }
  }, [e]);
  ae(() => {
    if (!A || !s || Dt(s) === k.current) return;
    C("Autosaving...");
    const F = window.setTimeout(() => {
      he(s, "Autosaved");
    }, o0);
    return () => window.clearTimeout(F);
  }, [A, s, he]);
  const Ne = async () => {
    s && (C("Saving..."), await he(s, "Saved"));
  }, Ce = async () => {
    if (s) {
      C("Promoting...");
      try {
        const T = await zg(e, s.id), F = await Rg(e, T.versionId);
        I(F.artifactId), C(`Published ${F.artifactVersion}`), await Z();
      } catch (T) {
        C(""), N(T instanceof Error ? T.message : String(T));
      }
    }
  }, ke = async () => {
    if (B) {
      C("Running...");
      try {
        await ia(e, B), C("Run dispatched");
      } catch (T) {
        C(""), N(T instanceof Error ? T.message : String(T));
      }
    }
  }, et = (T) => p((F) => ks(T, F)), Be = (T) => x((F) => Ms(T, F)), Fe = (T) => {
    if (!s?.state.rootActivity || !L || L.slot.mode !== "flowchart") return;
    const F = Is(T, g), q = Bg(L.owner, F);
    x(F), z(_o(s.state.rootActivity, u, q));
  }, Se = () => {
    a((T) => {
      if (!T) return T;
      const F = Fg(T.layout, f);
      if (!T.state.rootActivity || !L) return { ...T, layout: F };
      const q = Vg(L, f);
      return {
        ...T,
        layout: F,
        state: {
          ...T.state,
          rootActivity: _o(T.state.rootActivity, u, q)
        }
      };
    });
  }, Me = (T, F, q) => {
    d((ie) => [...ie, { ownerNodeId: T.nodeId, slotId: F, label: q }]), m(null);
  }, Ie = (T) => {
    H((F) => {
      const q = new Set(F);
      return q.has(T) ? q.delete(T) : q.add(T), q;
    });
  };
  return !r || !s ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: b || "Loading workflow editor..." }) : /* @__PURE__ */ h.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ h.jsx("button", { type: "button", className: "wf-link-button", onClick: o, children: "Definitions" }),
      /* @__PURE__ */ h.jsx(gn, { size: 14 }),
      /* @__PURE__ */ h.jsx("strong", { children: r.definition.name }),
      /* @__PURE__ */ h.jsx("span", { className: "wf-chip", children: "Draft" }),
      _ ? /* @__PURE__ */ h.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ h.jsx(On, { size: 13 }),
        " ",
        _
      ] }) : null,
      /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ h.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ h.jsx("input", { type: "checkbox", checked: A, onChange: (T) => D(T.target.checked) }),
          /* @__PURE__ */ h.jsx("span", { children: "Autosave" })
        ] }),
        Q ? /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => Nt(n, Q, { definition: r.definition, draft: s }), children: [
          /* @__PURE__ */ h.jsx(_t, { size: 15 }),
          " Risks"
        ] }) : null,
        ne ? /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => Nt(n, ne, { definition: r.definition, draft: s }), children: [
          /* @__PURE__ */ h.jsx(_t, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
          Ne();
        }, children: [
          /* @__PURE__ */ h.jsx(kg, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
          Ce();
        }, children: [
          /* @__PURE__ */ h.jsx(_g, { size: 15 }),
          " Promote"
        ] }),
        /* @__PURE__ */ h.jsxs("button", { type: "button", disabled: !B, onClick: () => {
          ke();
        }, children: [
          /* @__PURE__ */ h.jsx(ra, { size: 15 }),
          " Run"
        ] })
      ] })
    ] }),
    b ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Xt, { size: 16 }),
      " ",
      b
    ] }) : null,
    /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-body", children: [
      /* @__PURE__ */ h.jsxs("aside", { className: "wf-palette", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ h.jsx(vg, { size: 15 }),
          " Activities"
        ] }),
        /* @__PURE__ */ h.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: O.map((T) => {
          const F = $.has(T.category);
          return /* @__PURE__ */ h.jsxs("div", { className: "wf-palette-category", children: [
            /* @__PURE__ */ h.jsxs(
              "button",
              {
                type: "button",
                className: "wf-palette-category-toggle",
                role: "treeitem",
                "aria-expanded": F,
                onClick: () => Ie(T.category),
                children: [
                  F ? /* @__PURE__ */ h.jsx(bg, { size: 14 }) : /* @__PURE__ */ h.jsx(gn, { size: 14 }),
                  /* @__PURE__ */ h.jsx("span", { children: T.category }),
                  /* @__PURE__ */ h.jsx("small", { children: T.activities.length })
                ]
              }
            ),
            F ? /* @__PURE__ */ h.jsx("div", { className: "wf-palette-activities", role: "group", children: T.activities.map((q) => {
              const ie = q.description?.trim(), le = ie ? `wf-palette-description-${q.activityVersionId}` : void 0;
              return /* @__PURE__ */ h.jsxs(
                "button",
                {
                  type: "button",
                  className: "wf-palette-activity",
                  role: "treeitem",
                  draggable: !0,
                  title: ie || Oe(q),
                  "aria-describedby": le,
                  onClick: () => R(q),
                  onDragStart: (me) => U(me, q),
                  onDragEnd: (me) => J(me, q),
                  onPointerDown: (me) => re(me, q),
                  children: [
                    /* @__PURE__ */ h.jsx("strong", { children: Oe(q) }),
                    ie ? /* @__PURE__ */ h.jsx("small", { id: le, children: ie }) : null
                  ]
                },
                q.activityVersionId
              );
            }) }) : null
          ] }, T.category);
        }) })
      ] }),
      /* @__PURE__ */ h.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-breadcrumb", children: [
          /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
            d([]), m(null);
          }, children: "Root" }),
          u.map((T, F) => /* @__PURE__ */ h.jsxs(Wt.Fragment, { children: [
            /* @__PURE__ */ h.jsx(gn, { size: 13 }),
            /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
              d(u.slice(0, F + 1)), m(null);
            }, children: T.label })
          ] }, `${T.ownerNodeId}-${T.slotId}-${F}`))
        ] }),
        /* @__PURE__ */ h.jsx("div", { className: "wf-canvas", ref: v, onDragOver: G, onDrop: ue, children: /* @__PURE__ */ h.jsxs(
          Bp,
          {
            nodes: f,
            edges: g,
            nodeTypes: t0,
            onInit: y,
            onNodesChange: et,
            onEdgesChange: Be,
            onConnect: Fe,
            onDragOver: G,
            onDrop: ue,
            onNodeClick: (T, F) => m(F.id),
            onNodeDragStop: Se,
            fitView: !0,
            children: [
              /* @__PURE__ */ h.jsx(qp, { gap: 18, size: 1 }),
              /* @__PURE__ */ h.jsx(eg, {}),
              /* @__PURE__ */ h.jsx(hg, { pannable: !0, zoomable: !0 })
            ]
          }
        ) }),
        /* @__PURE__ */ h.jsx(S0, { draft: s })
      ] }),
      /* @__PURE__ */ h.jsxs("aside", { className: "wf-inspector", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ h.jsx(Eg, { size: 15 }),
          " Inspector"
        ] }),
        Y ? /* @__PURE__ */ h.jsxs("div", { className: "wf-inspector-content", children: [
          /* @__PURE__ */ h.jsx("h3", { children: f.find((T) => T.id === Y.nodeId)?.data.label ?? Y.nodeId }),
          /* @__PURE__ */ h.jsxs("dl", { children: [
            /* @__PURE__ */ h.jsx("dt", { children: "Node ID" }),
            /* @__PURE__ */ h.jsx("dd", { children: Y.nodeId }),
            /* @__PURE__ */ h.jsx("dt", { children: "Activity version" }),
            /* @__PURE__ */ h.jsx("dd", { children: Y.activityVersionId })
          ] }),
          W.length > 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-slot-list", children: [
            /* @__PURE__ */ h.jsx("span", { children: "Embedded slots" }),
            W.map((T) => /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => Me(Y, T.id, `${f.find((F) => F.id === Y.nodeId)?.data.label ?? Y.nodeId} / ${T.label}`), children: [
              T.label,
              /* @__PURE__ */ h.jsxs("small", { children: [
                T.activities.length,
                " activit",
                T.activities.length === 1 ? "y" : "ies"
              ] })
            ] }, T.id))
          ] }) : /* @__PURE__ */ h.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
        ] }) : /* @__PURE__ */ h.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." })
      ] })
    ] })
  ] });
}
function b0({ data: e, selected: t }) {
  const n = e;
  return /* @__PURE__ */ h.jsxs("div", { className: t ? "wf-node selected" : "wf-node", children: [
    /* @__PURE__ */ h.jsx(St, { type: "target", position: K.Left }),
    /* @__PURE__ */ h.jsx("strong", { children: n.label }),
    /* @__PURE__ */ h.jsx("small", { children: n.activityTypeKey ?? n.activityVersionId }),
    n.childSlots.length > 0 ? /* @__PURE__ */ h.jsxs("span", { children: [
      n.childSlots.length,
      " embedded slot",
      n.childSlots.length === 1 ? "" : "s"
    ] }) : null,
    /* @__PURE__ */ h.jsx(St, { type: "source", position: K.Right })
  ] });
}
function S0({ draft: e }) {
  return e.validationErrors.length ? /* @__PURE__ */ h.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ h.jsx(Xt, { size: 14 }),
    e.validationErrors.length,
    " validation issue",
    e.validationErrors.length === 1 ? "" : "s"
  ] }) : /* @__PURE__ */ h.jsxs("div", { className: "wf-validation ok", children: [
    /* @__PURE__ */ h.jsx(On, { size: 14 }),
    " No validation errors"
  ] });
}
function _0(e) {
  return `${Oe(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function Dt(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function Eo(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
export {
  N0 as register
};
