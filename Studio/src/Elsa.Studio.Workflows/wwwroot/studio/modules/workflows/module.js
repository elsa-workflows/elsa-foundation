import it, { memo as we, forwardRef as Bn, useRef as ae, useEffect as ce, useCallback as he, useContext as Jt, useMemo as me, useState as re, createContext as nr, useLayoutEffect as Ec, createElement as Ho } from "react";
import "@tanstack/react-query";
function _c(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var So = { exports: {} }, Tt = {};
var Ar;
function Nc() {
  if (Ar) return Tt;
  Ar = 1;
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
  return Tt.Fragment = t, Tt.jsx = n, Tt.jsxs = n, Tt;
}
var Dr;
function Cc() {
  return Dr || (Dr = 1, So.exports = Nc()), So.exports;
}
var h = Cc();
function ve(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, o; n < e.length; n++)
      (o = ve(e[n])) !== "" && (t += (t && " ") + o);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var kc = { value: () => {
} };
function Fn() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new bn(n);
}
function bn(e) {
  this._ = e;
}
function Ic(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
bn.prototype = Fn.prototype = {
  constructor: bn,
  on: function(e, t) {
    var n = this._, o = Ic(e + "", n), r, i = -1, s = o.length;
    if (arguments.length < 2) {
      for (; ++i < s; ) if ((r = (e = o[i]).type) && (r = Mc(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++i < s; )
      if (r = (e = o[i]).type) n[r] = Pr(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = Pr(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new bn(e);
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
function Mc(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function Pr(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = kc, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var Vo = "http://www.w3.org/1999/xhtml";
const jr = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: Vo,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function Wn(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), jr.hasOwnProperty(t) ? { space: jr[t], local: e } : e;
}
function Ac(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === Vo && t.documentElement.namespaceURI === Vo ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Dc(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function qi(e) {
  var t = Wn(e);
  return (t.local ? Dc : Ac)(t);
}
function Pc() {
}
function or(e) {
  return e == null ? Pc : function() {
    return this.querySelector(e);
  };
}
function jc(e) {
  typeof e != "function" && (e = or(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = new Array(s), l, c, u = 0; u < s; ++u)
      (l = i[u]) && (c = e.call(l, l.__data__, u, i)) && ("__data__" in l && (c.__data__ = l.__data__), a[u] = c);
  return new Pe(o, this._parents);
}
function $c(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function Tc() {
  return [];
}
function Zi(e) {
  return e == null ? Tc : function() {
    return this.querySelectorAll(e);
  };
}
function Rc(e) {
  return function() {
    return $c(e.apply(this, arguments));
  };
}
function zc(e) {
  typeof e == "function" ? e = Rc(e) : e = Zi(e);
  for (var t = this._groups, n = t.length, o = [], r = [], i = 0; i < n; ++i)
    for (var s = t[i], a = s.length, l, c = 0; c < a; ++c)
      (l = s[c]) && (o.push(e.call(l, l.__data__, c, s)), r.push(l));
  return new Pe(o, r);
}
function Ui(e) {
  return function() {
    return this.matches(e);
  };
}
function Ki(e) {
  return function(t) {
    return t.matches(e);
  };
}
var Lc = Array.prototype.find;
function Hc(e) {
  return function() {
    return Lc.call(this.children, e);
  };
}
function Vc() {
  return this.firstElementChild;
}
function Oc(e) {
  return this.select(e == null ? Vc : Hc(typeof e == "function" ? e : Ki(e)));
}
var Bc = Array.prototype.filter;
function Fc() {
  return Array.from(this.children);
}
function Wc(e) {
  return function() {
    return Bc.call(this.children, e);
  };
}
function Yc(e) {
  return this.selectAll(e == null ? Fc : Wc(typeof e == "function" ? e : Ki(e)));
}
function Xc(e) {
  typeof e != "function" && (e = Ui(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = [], l, c = 0; c < s; ++c)
      (l = i[c]) && e.call(l, l.__data__, c, i) && a.push(l);
  return new Pe(o, this._parents);
}
function Gi(e) {
  return new Array(e.length);
}
function qc() {
  return new Pe(this._enter || this._groups.map(Gi), this._parents);
}
function In(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
In.prototype = {
  constructor: In,
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
function Zc(e) {
  return function() {
    return e;
  };
}
function Uc(e, t, n, o, r, i) {
  for (var s = 0, a, l = t.length, c = i.length; s < c; ++s)
    (a = t[s]) ? (a.__data__ = i[s], o[s] = a) : n[s] = new In(e, i[s]);
  for (; s < l; ++s)
    (a = t[s]) && (r[s] = a);
}
function Kc(e, t, n, o, r, i, s) {
  var a, l, c = /* @__PURE__ */ new Map(), u = t.length, f = i.length, d = new Array(u), m;
  for (a = 0; a < u; ++a)
    (l = t[a]) && (d[a] = m = s.call(l, l.__data__, a, t) + "", c.has(m) ? r[a] = l : c.set(m, l));
  for (a = 0; a < f; ++a)
    m = s.call(e, i[a], a, i) + "", (l = c.get(m)) ? (o[a] = l, l.__data__ = i[a], c.delete(m)) : n[a] = new In(e, i[a]);
  for (a = 0; a < u; ++a)
    (l = t[a]) && c.get(d[a]) === l && (r[a] = l);
}
function Gc(e) {
  return e.__data__;
}
function Qc(e, t) {
  if (!arguments.length) return Array.from(this, Gc);
  var n = t ? Kc : Uc, o = this._parents, r = this._groups;
  typeof e != "function" && (e = Zc(e));
  for (var i = r.length, s = new Array(i), a = new Array(i), l = new Array(i), c = 0; c < i; ++c) {
    var u = o[c], f = r[c], d = f.length, m = Jc(e.call(u, u && u.__data__, c, o)), p = m.length, y = a[c] = new Array(p), w = s[c] = new Array(p), x = l[c] = new Array(d);
    n(u, f, y, w, x, m, t);
    for (var S = 0, g = 0, v, _; S < p; ++S)
      if (v = y[S]) {
        for (S >= g && (g = S + 1); !(_ = w[g]) && ++g < p; ) ;
        v._next = _ || null;
      }
  }
  return s = new Pe(s, o), s._enter = a, s._exit = l, s;
}
function Jc(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function el() {
  return new Pe(this._exit || this._groups.map(Gi), this._parents);
}
function tl(e, t, n) {
  var o = this.enter(), r = this, i = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? i.remove() : n(i), o && r ? o.merge(r).order() : r;
}
function nl(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, i = o.length, s = Math.min(r, i), a = new Array(r), l = 0; l < s; ++l)
    for (var c = n[l], u = o[l], f = c.length, d = a[l] = new Array(f), m, p = 0; p < f; ++p)
      (m = c[p] || u[p]) && (d[p] = m);
  for (; l < r; ++l)
    a[l] = n[l];
  return new Pe(a, this._parents);
}
function ol() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, i = o[r], s; --r >= 0; )
      (s = o[r]) && (i && s.compareDocumentPosition(i) ^ 4 && i.parentNode.insertBefore(s, i), i = s);
  return this;
}
function rl(e) {
  e || (e = il);
  function t(f, d) {
    return f && d ? e(f.__data__, d.__data__) : !f - !d;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), i = 0; i < o; ++i) {
    for (var s = n[i], a = s.length, l = r[i] = new Array(a), c, u = 0; u < a; ++u)
      (c = s[u]) && (l[u] = c);
    l.sort(t);
  }
  return new Pe(r, this._parents).order();
}
function il(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function sl() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function al() {
  return Array.from(this);
}
function cl() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length; r < i; ++r) {
      var s = o[r];
      if (s) return s;
    }
  return null;
}
function ll() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function ul() {
  return !this.node();
}
function dl(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], i = 0, s = r.length, a; i < s; ++i)
      (a = r[i]) && e.call(a, a.__data__, i, r);
  return this;
}
function fl(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function hl(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function pl(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function gl(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function ml(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function yl(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function xl(e, t) {
  var n = Wn(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? hl : fl : typeof t == "function" ? n.local ? yl : ml : n.local ? gl : pl)(n, t));
}
function Qi(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function wl(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function vl(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function bl(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function Sl(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? wl : typeof t == "function" ? bl : vl)(e, t, n ?? "")) : bt(this.node(), e);
}
function bt(e, t) {
  return e.style.getPropertyValue(t) || Qi(e).getComputedStyle(e, null).getPropertyValue(t);
}
function El(e) {
  return function() {
    delete this[e];
  };
}
function _l(e, t) {
  return function() {
    this[e] = t;
  };
}
function Nl(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function Cl(e, t) {
  return arguments.length > 1 ? this.each((t == null ? El : typeof t == "function" ? Nl : _l)(e, t)) : this.node()[e];
}
function Ji(e) {
  return e.trim().split(/^|\s+/);
}
function rr(e) {
  return e.classList || new es(e);
}
function es(e) {
  this._node = e, this._names = Ji(e.getAttribute("class") || "");
}
es.prototype = {
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
function ts(e, t) {
  for (var n = rr(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function ns(e, t) {
  for (var n = rr(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function kl(e) {
  return function() {
    ts(this, e);
  };
}
function Il(e) {
  return function() {
    ns(this, e);
  };
}
function Ml(e, t) {
  return function() {
    (t.apply(this, arguments) ? ts : ns)(this, e);
  };
}
function Al(e, t) {
  var n = Ji(e + "");
  if (arguments.length < 2) {
    for (var o = rr(this.node()), r = -1, i = n.length; ++r < i; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? Ml : t ? kl : Il)(n, t));
}
function Dl() {
  this.textContent = "";
}
function Pl(e) {
  return function() {
    this.textContent = e;
  };
}
function jl(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function $l(e) {
  return arguments.length ? this.each(e == null ? Dl : (typeof e == "function" ? jl : Pl)(e)) : this.node().textContent;
}
function Tl() {
  this.innerHTML = "";
}
function Rl(e) {
  return function() {
    this.innerHTML = e;
  };
}
function zl(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function Ll(e) {
  return arguments.length ? this.each(e == null ? Tl : (typeof e == "function" ? zl : Rl)(e)) : this.node().innerHTML;
}
function Hl() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function Vl() {
  return this.each(Hl);
}
function Ol() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Bl() {
  return this.each(Ol);
}
function Fl(e) {
  var t = typeof e == "function" ? e : qi(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Wl() {
  return null;
}
function Yl(e, t) {
  var n = typeof e == "function" ? e : qi(e), o = t == null ? Wl : typeof t == "function" ? t : or(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function Xl() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function ql() {
  return this.each(Xl);
}
function Zl() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Ul() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Kl(e) {
  return this.select(e ? Ul : Zl);
}
function Gl(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function Ql(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function Jl(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function eu(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, i; n < r; ++n)
        i = t[n], (!e.type || i.type === e.type) && i.name === e.name ? this.removeEventListener(i.type, i.listener, i.options) : t[++o] = i;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function tu(e, t, n) {
  return function() {
    var o = this.__on, r, i = Ql(t);
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
function nu(e, t, n) {
  var o = Jl(e + ""), r, i = o.length, s;
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
  for (a = t ? tu : eu, r = 0; r < i; ++r) this.each(a(o[r], t, n));
  return this;
}
function os(e, t, n) {
  var o = Qi(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function ou(e, t) {
  return function() {
    return os(this, e, t);
  };
}
function ru(e, t) {
  return function() {
    return os(this, e, t.apply(this, arguments));
  };
}
function iu(e, t) {
  return this.each((typeof t == "function" ? ru : ou)(e, t));
}
function* su() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length, s; r < i; ++r)
      (s = o[r]) && (yield s);
}
var rs = [null];
function Pe(e, t) {
  this._groups = e, this._parents = t;
}
function en() {
  return new Pe([[document.documentElement]], rs);
}
function au() {
  return this;
}
Pe.prototype = en.prototype = {
  constructor: Pe,
  select: jc,
  selectAll: zc,
  selectChild: Oc,
  selectChildren: Yc,
  filter: Xc,
  data: Qc,
  enter: qc,
  exit: el,
  join: tl,
  merge: nl,
  selection: au,
  order: ol,
  sort: rl,
  call: sl,
  nodes: al,
  node: cl,
  size: ll,
  empty: ul,
  each: dl,
  attr: xl,
  style: Sl,
  property: Cl,
  classed: Al,
  text: $l,
  html: Ll,
  raise: Vl,
  lower: Bl,
  append: Fl,
  insert: Yl,
  remove: ql,
  clone: Kl,
  datum: Gl,
  on: nu,
  dispatch: iu,
  [Symbol.iterator]: su
};
function De(e) {
  return typeof e == "string" ? new Pe([[document.querySelector(e)]], [document.documentElement]) : new Pe([[e]], rs);
}
function cu(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Le(e, t) {
  if (e = cu(e), t === void 0 && (t = e.currentTarget), t) {
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
const lu = { passive: !1 }, Ft = { capture: !0, passive: !1 };
function Eo(e) {
  e.stopImmediatePropagation();
}
function wt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function is(e) {
  var t = e.document.documentElement, n = De(e).on("dragstart.drag", wt, Ft);
  "onselectstart" in t ? n.on("selectstart.drag", wt, Ft) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function ss(e, t) {
  var n = e.document.documentElement, o = De(e).on("dragstart.drag", null);
  t && (o.on("click.drag", wt, Ft), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const fn = (e) => () => e;
function Oo(e, {
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
Oo.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function uu(e) {
  return !e.ctrlKey && !e.button;
}
function du() {
  return this.parentNode;
}
function fu(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function hu() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function as() {
  var e = uu, t = du, n = fu, o = hu, r = {}, i = Fn("start", "drag", "end"), s = 0, a, l, c, u, f = 0;
  function d(v) {
    v.on("mousedown.drag", m).filter(o).on("touchstart.drag", w).on("touchmove.drag", x, lu).on("touchend.drag touchcancel.drag", S).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function m(v, _) {
    if (!(u || !e.call(this, v, _))) {
      var E = g(this, t.call(this, v, _), v, _, "mouse");
      E && (De(v.view).on("mousemove.drag", p, Ft).on("mouseup.drag", y, Ft), is(v.view), Eo(v), c = !1, a = v.clientX, l = v.clientY, E("start", v));
    }
  }
  function p(v) {
    if (wt(v), !c) {
      var _ = v.clientX - a, E = v.clientY - l;
      c = _ * _ + E * E > f;
    }
    r.mouse("drag", v);
  }
  function y(v) {
    De(v.view).on("mousemove.drag mouseup.drag", null), ss(v.view, c), wt(v), r.mouse("end", v);
  }
  function w(v, _) {
    if (e.call(this, v, _)) {
      var E = v.changedTouches, M = t.call(this, v, _), D = E.length, P, F;
      for (P = 0; P < D; ++P)
        (F = g(this, M, v, _, E[P].identifier, E[P])) && (Eo(v), F("start", v, E[P]));
    }
  }
  function x(v) {
    var _ = v.changedTouches, E = _.length, M, D;
    for (M = 0; M < E; ++M)
      (D = r[_[M].identifier]) && (wt(v), D("drag", v, _[M]));
  }
  function S(v) {
    var _ = v.changedTouches, E = _.length, M, D;
    for (u && clearTimeout(u), u = setTimeout(function() {
      u = null;
    }, 500), M = 0; M < E; ++M)
      (D = r[_[M].identifier]) && (Eo(v), D("end", v, _[M]));
  }
  function g(v, _, E, M, D, P) {
    var F = i.copy(), C = Le(P || E, _), R, H, b;
    if ((b = n.call(v, new Oo("beforestart", {
      sourceEvent: E,
      target: d,
      identifier: D,
      active: s,
      x: C[0],
      y: C[1],
      dx: 0,
      dy: 0,
      dispatch: F
    }), M)) != null)
      return R = b.x - C[0] || 0, H = b.y - C[1] || 0, function k(N, A, T) {
        var j = C, B;
        switch (N) {
          case "start":
            r[D] = k, B = s++;
            break;
          case "end":
            delete r[D], --s;
          // falls through
          case "drag":
            C = Le(T || A, _), B = s;
            break;
        }
        F.call(
          N,
          v,
          new Oo(N, {
            sourceEvent: A,
            subject: b,
            target: d,
            identifier: D,
            active: B,
            x: C[0] + R,
            y: C[1] + H,
            dx: C[0] - j[0],
            dy: C[1] - j[1],
            dispatch: F
          }),
          M
        );
      };
  }
  return d.filter = function(v) {
    return arguments.length ? (e = typeof v == "function" ? v : fn(!!v), d) : e;
  }, d.container = function(v) {
    return arguments.length ? (t = typeof v == "function" ? v : fn(v), d) : t;
  }, d.subject = function(v) {
    return arguments.length ? (n = typeof v == "function" ? v : fn(v), d) : n;
  }, d.touchable = function(v) {
    return arguments.length ? (o = typeof v == "function" ? v : fn(!!v), d) : o;
  }, d.on = function() {
    var v = i.on.apply(i, arguments);
    return v === i ? d : v;
  }, d.clickDistance = function(v) {
    return arguments.length ? (f = (v = +v) * v, d) : Math.sqrt(f);
  }, d;
}
function ir(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function cs(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function tn() {
}
var Wt = 0.7, Mn = 1 / Wt, vt = "\\s*([+-]?\\d+)\\s*", Yt = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", Ye = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", pu = /^#([0-9a-f]{3,8})$/, gu = new RegExp(`^rgb\\(${vt},${vt},${vt}\\)$`), mu = new RegExp(`^rgb\\(${Ye},${Ye},${Ye}\\)$`), yu = new RegExp(`^rgba\\(${vt},${vt},${vt},${Yt}\\)$`), xu = new RegExp(`^rgba\\(${Ye},${Ye},${Ye},${Yt}\\)$`), wu = new RegExp(`^hsl\\(${Yt},${Ye},${Ye}\\)$`), vu = new RegExp(`^hsla\\(${Yt},${Ye},${Ye},${Yt}\\)$`), $r = {
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
ir(tn, ut, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Tr,
  // Deprecated! Use color.formatHex.
  formatHex: Tr,
  formatHex8: bu,
  formatHsl: Su,
  formatRgb: Rr,
  toString: Rr
});
function Tr() {
  return this.rgb().formatHex();
}
function bu() {
  return this.rgb().formatHex8();
}
function Su() {
  return ls(this).formatHsl();
}
function Rr() {
  return this.rgb().formatRgb();
}
function ut(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = pu.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? zr(t) : n === 3 ? new Ae(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? hn(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? hn(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = gu.exec(e)) ? new Ae(t[1], t[2], t[3], 1) : (t = mu.exec(e)) ? new Ae(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = yu.exec(e)) ? hn(t[1], t[2], t[3], t[4]) : (t = xu.exec(e)) ? hn(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = wu.exec(e)) ? Vr(t[1], t[2] / 100, t[3] / 100, 1) : (t = vu.exec(e)) ? Vr(t[1], t[2] / 100, t[3] / 100, t[4]) : $r.hasOwnProperty(e) ? zr($r[e]) : e === "transparent" ? new Ae(NaN, NaN, NaN, 0) : null;
}
function zr(e) {
  return new Ae(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function hn(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new Ae(e, t, n, o);
}
function Eu(e) {
  return e instanceof tn || (e = ut(e)), e ? (e = e.rgb(), new Ae(e.r, e.g, e.b, e.opacity)) : new Ae();
}
function Bo(e, t, n, o) {
  return arguments.length === 1 ? Eu(e) : new Ae(e, t, n, o ?? 1);
}
function Ae(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
ir(Ae, Bo, cs(tn, {
  brighter(e) {
    return e = e == null ? Mn : Math.pow(Mn, e), new Ae(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Wt : Math.pow(Wt, e), new Ae(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new Ae(ct(this.r), ct(this.g), ct(this.b), An(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: Lr,
  // Deprecated! Use color.formatHex.
  formatHex: Lr,
  formatHex8: _u,
  formatRgb: Hr,
  toString: Hr
}));
function Lr() {
  return `#${at(this.r)}${at(this.g)}${at(this.b)}`;
}
function _u() {
  return `#${at(this.r)}${at(this.g)}${at(this.b)}${at((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function Hr() {
  const e = An(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${ct(this.r)}, ${ct(this.g)}, ${ct(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function An(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function ct(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function at(e) {
  return e = ct(e), (e < 16 ? "0" : "") + e.toString(16);
}
function Vr(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new He(e, t, n, o);
}
function ls(e) {
  if (e instanceof He) return new He(e.h, e.s, e.l, e.opacity);
  if (e instanceof tn || (e = ut(e)), !e) return new He();
  if (e instanceof He) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), i = Math.max(t, n, o), s = NaN, a = i - r, l = (i + r) / 2;
  return a ? (t === i ? s = (n - o) / a + (n < o) * 6 : n === i ? s = (o - t) / a + 2 : s = (t - n) / a + 4, a /= l < 0.5 ? i + r : 2 - i - r, s *= 60) : a = l > 0 && l < 1 ? 0 : s, new He(s, a, l, e.opacity);
}
function Nu(e, t, n, o) {
  return arguments.length === 1 ? ls(e) : new He(e, t, n, o ?? 1);
}
function He(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
ir(He, Nu, cs(tn, {
  brighter(e) {
    return e = e == null ? Mn : Math.pow(Mn, e), new He(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Wt : Math.pow(Wt, e), new He(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new Ae(
      _o(e >= 240 ? e - 240 : e + 120, r, o),
      _o(e, r, o),
      _o(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new He(Or(this.h), pn(this.s), pn(this.l), An(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = An(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${Or(this.h)}, ${pn(this.s) * 100}%, ${pn(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function Or(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function pn(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function _o(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const sr = (e) => () => e;
function Cu(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function ku(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function Iu(e) {
  return (e = +e) == 1 ? us : function(t, n) {
    return n - t ? ku(t, n, e) : sr(isNaN(t) ? n : t);
  };
}
function us(e, t) {
  var n = t - e;
  return n ? Cu(e, n) : sr(isNaN(e) ? t : e);
}
const Dn = (function e(t) {
  var n = Iu(t);
  function o(r, i) {
    var s = n((r = Bo(r)).r, (i = Bo(i)).r), a = n(r.g, i.g), l = n(r.b, i.b), c = us(r.opacity, i.opacity);
    return function(u) {
      return r.r = s(u), r.g = a(u), r.b = l(u), r.opacity = c(u), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function Mu(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(i) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - i) + t[r] * i;
    return o;
  };
}
function Au(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Du(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), i = new Array(n), s;
  for (s = 0; s < o; ++s) r[s] = Ot(e[s], t[s]);
  for (; s < n; ++s) i[s] = t[s];
  return function(a) {
    for (s = 0; s < o; ++s) i[s] = r[s](a);
    return i;
  };
}
function Pu(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(o) {
    return n.setTime(e * (1 - o) + t * o), n;
  };
}
function We(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function ju(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = Ot(e[r], t[r]) : o[r] = t[r];
  return function(i) {
    for (r in n) o[r] = n[r](i);
    return o;
  };
}
var Fo = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, No = new RegExp(Fo.source, "g");
function $u(e) {
  return function() {
    return e;
  };
}
function Tu(e) {
  return function(t) {
    return e(t) + "";
  };
}
function ds(e, t) {
  var n = Fo.lastIndex = No.lastIndex = 0, o, r, i, s = -1, a = [], l = [];
  for (e = e + "", t = t + ""; (o = Fo.exec(e)) && (r = No.exec(t)); )
    (i = r.index) > n && (i = t.slice(n, i), a[s] ? a[s] += i : a[++s] = i), (o = o[0]) === (r = r[0]) ? a[s] ? a[s] += r : a[++s] = r : (a[++s] = null, l.push({ i: s, x: We(o, r) })), n = No.lastIndex;
  return n < t.length && (i = t.slice(n), a[s] ? a[s] += i : a[++s] = i), a.length < 2 ? l[0] ? Tu(l[0].x) : $u(t) : (t = l.length, function(c) {
    for (var u = 0, f; u < t; ++u) a[(f = l[u]).i] = f.x(c);
    return a.join("");
  });
}
function Ot(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? sr(t) : (n === "number" ? We : n === "string" ? (o = ut(t)) ? (t = o, Dn) : ds : t instanceof ut ? Dn : t instanceof Date ? Pu : Au(t) ? Mu : Array.isArray(t) ? Du : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? ju : We)(e, t);
}
var Br = 180 / Math.PI, Wo = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function fs(e, t, n, o, r, i) {
  var s, a, l;
  return (s = Math.sqrt(e * e + t * t)) && (e /= s, t /= s), (l = e * n + t * o) && (n -= e * l, o -= t * l), (a = Math.sqrt(n * n + o * o)) && (n /= a, o /= a, l /= a), e * o < t * n && (e = -e, t = -t, l = -l, s = -s), {
    translateX: r,
    translateY: i,
    rotate: Math.atan2(t, e) * Br,
    skewX: Math.atan(l) * Br,
    scaleX: s,
    scaleY: a
  };
}
var gn;
function Ru(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Wo : fs(t.a, t.b, t.c, t.d, t.e, t.f);
}
function zu(e) {
  return e == null || (gn || (gn = document.createElementNS("http://www.w3.org/2000/svg", "g")), gn.setAttribute("transform", e), !(e = gn.transform.baseVal.consolidate())) ? Wo : (e = e.matrix, fs(e.a, e.b, e.c, e.d, e.e, e.f));
}
function hs(e, t, n, o) {
  function r(c) {
    return c.length ? c.pop() + " " : "";
  }
  function i(c, u, f, d, m, p) {
    if (c !== f || u !== d) {
      var y = m.push("translate(", null, t, null, n);
      p.push({ i: y - 4, x: We(c, f) }, { i: y - 2, x: We(u, d) });
    } else (f || d) && m.push("translate(" + f + t + d + n);
  }
  function s(c, u, f, d) {
    c !== u ? (c - u > 180 ? u += 360 : u - c > 180 && (c += 360), d.push({ i: f.push(r(f) + "rotate(", null, o) - 2, x: We(c, u) })) : u && f.push(r(f) + "rotate(" + u + o);
  }
  function a(c, u, f, d) {
    c !== u ? d.push({ i: f.push(r(f) + "skewX(", null, o) - 2, x: We(c, u) }) : u && f.push(r(f) + "skewX(" + u + o);
  }
  function l(c, u, f, d, m, p) {
    if (c !== f || u !== d) {
      var y = m.push(r(m) + "scale(", null, ",", null, ")");
      p.push({ i: y - 4, x: We(c, f) }, { i: y - 2, x: We(u, d) });
    } else (f !== 1 || d !== 1) && m.push(r(m) + "scale(" + f + "," + d + ")");
  }
  return function(c, u) {
    var f = [], d = [];
    return c = e(c), u = e(u), i(c.translateX, c.translateY, u.translateX, u.translateY, f, d), s(c.rotate, u.rotate, f, d), a(c.skewX, u.skewX, f, d), l(c.scaleX, c.scaleY, u.scaleX, u.scaleY, f, d), c = u = null, function(m) {
      for (var p = -1, y = d.length, w; ++p < y; ) f[(w = d[p]).i] = w.x(m);
      return f.join("");
    };
  };
}
var Lu = hs(Ru, "px, ", "px)", "deg)"), Hu = hs(zu, ", ", ")", ")"), Vu = 1e-12;
function Fr(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Ou(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Bu(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const Sn = (function e(t, n, o) {
  function r(i, s) {
    var a = i[0], l = i[1], c = i[2], u = s[0], f = s[1], d = s[2], m = u - a, p = f - l, y = m * m + p * p, w, x;
    if (y < Vu)
      x = Math.log(d / c) / t, w = function(M) {
        return [
          a + M * m,
          l + M * p,
          c * Math.exp(t * M * x)
        ];
      };
    else {
      var S = Math.sqrt(y), g = (d * d - c * c + o * y) / (2 * c * n * S), v = (d * d - c * c - o * y) / (2 * d * n * S), _ = Math.log(Math.sqrt(g * g + 1) - g), E = Math.log(Math.sqrt(v * v + 1) - v);
      x = (E - _) / t, w = function(M) {
        var D = M * x, P = Fr(_), F = c / (n * S) * (P * Bu(t * D + _) - Ou(_));
        return [
          a + F * m,
          l + F * p,
          c * P / Fr(t * D + _)
        ];
      };
    }
    return w.duration = x * 1e3 * t / Math.SQRT2, w;
  }
  return r.rho = function(i) {
    var s = Math.max(1e-3, +i), a = s * s, l = a * a;
    return e(s, a, l);
  }, r;
})(Math.SQRT2, 2, 4);
var St = 0, Ht = 0, Rt = 0, ps = 1e3, Pn, Vt, jn = 0, dt = 0, Yn = 0, Xt = typeof performance == "object" && performance.now ? performance : Date, gs = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function ar() {
  return dt || (gs(Fu), dt = Xt.now() + Yn);
}
function Fu() {
  dt = 0;
}
function $n() {
  this._call = this._time = this._next = null;
}
$n.prototype = ms.prototype = {
  constructor: $n,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? ar() : +n) + (t == null ? 0 : +t), !this._next && Vt !== this && (Vt ? Vt._next = this : Pn = this, Vt = this), this._call = e, this._time = n, Yo();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Yo());
  }
};
function ms(e, t, n) {
  var o = new $n();
  return o.restart(e, t, n), o;
}
function Wu() {
  ar(), ++St;
  for (var e = Pn, t; e; )
    (t = dt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --St;
}
function Wr() {
  dt = (jn = Xt.now()) + Yn, St = Ht = 0;
  try {
    Wu();
  } finally {
    St = 0, Xu(), dt = 0;
  }
}
function Yu() {
  var e = Xt.now(), t = e - jn;
  t > ps && (Yn -= t, jn = e);
}
function Xu() {
  for (var e, t = Pn, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Pn = n);
  Vt = e, Yo(o);
}
function Yo(e) {
  if (!St) {
    Ht && (Ht = clearTimeout(Ht));
    var t = e - dt;
    t > 24 ? (e < 1 / 0 && (Ht = setTimeout(Wr, e - Xt.now() - Yn)), Rt && (Rt = clearInterval(Rt))) : (Rt || (jn = Xt.now(), Rt = setInterval(Yu, ps)), St = 1, gs(Wr));
  }
}
function Yr(e, t, n) {
  var o = new $n();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var qu = Fn("start", "end", "cancel", "interrupt"), Zu = [], ys = 0, Xr = 1, Xo = 2, En = 3, qr = 4, qo = 5, _n = 6;
function Xn(e, t, n, o, r, i) {
  var s = e.__transition;
  if (!s) e.__transition = {};
  else if (n in s) return;
  Uu(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: qu,
    tween: Zu,
    time: i.time,
    delay: i.delay,
    duration: i.duration,
    ease: i.ease,
    timer: null,
    state: ys
  });
}
function cr(e, t) {
  var n = Be(e, t);
  if (n.state > ys) throw new Error("too late; already scheduled");
  return n;
}
function Xe(e, t) {
  var n = Be(e, t);
  if (n.state > En) throw new Error("too late; already running");
  return n;
}
function Be(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function Uu(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = ms(i, 0, n.time);
  function i(c) {
    n.state = Xr, n.timer.restart(s, n.delay, n.time), n.delay <= c && s(c - n.delay);
  }
  function s(c) {
    var u, f, d, m;
    if (n.state !== Xr) return l();
    for (u in o)
      if (m = o[u], m.name === n.name) {
        if (m.state === En) return Yr(s);
        m.state === qr ? (m.state = _n, m.timer.stop(), m.on.call("interrupt", e, e.__data__, m.index, m.group), delete o[u]) : +u < t && (m.state = _n, m.timer.stop(), m.on.call("cancel", e, e.__data__, m.index, m.group), delete o[u]);
      }
    if (Yr(function() {
      n.state === En && (n.state = qr, n.timer.restart(a, n.delay, n.time), a(c));
    }), n.state = Xo, n.on.call("start", e, e.__data__, n.index, n.group), n.state === Xo) {
      for (n.state = En, r = new Array(d = n.tween.length), u = 0, f = -1; u < d; ++u)
        (m = n.tween[u].value.call(e, e.__data__, n.index, n.group)) && (r[++f] = m);
      r.length = f + 1;
    }
  }
  function a(c) {
    for (var u = c < n.duration ? n.ease.call(null, c / n.duration) : (n.timer.restart(l), n.state = qo, 1), f = -1, d = r.length; ++f < d; )
      r[f].call(e, u);
    n.state === qo && (n.on.call("end", e, e.__data__, n.index, n.group), l());
  }
  function l() {
    n.state = _n, n.timer.stop(), delete o[t];
    for (var c in o) return;
    delete e.__transition;
  }
}
function Nn(e, t) {
  var n = e.__transition, o, r, i = !0, s;
  if (n) {
    t = t == null ? null : t + "";
    for (s in n) {
      if ((o = n[s]).name !== t) {
        i = !1;
        continue;
      }
      r = o.state > Xo && o.state < qo, o.state = _n, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[s];
    }
    i && delete e.__transition;
  }
}
function Ku(e) {
  return this.each(function() {
    Nn(this, e);
  });
}
function Gu(e, t) {
  var n, o;
  return function() {
    var r = Xe(this, e), i = r.tween;
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
function Qu(e, t, n) {
  var o, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var i = Xe(this, e), s = i.tween;
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
function Ju(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = Be(this.node(), n).tween, r = 0, i = o.length, s; r < i; ++r)
      if ((s = o[r]).name === e)
        return s.value;
    return null;
  }
  return this.each((t == null ? Gu : Qu)(n, e, t));
}
function lr(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = Xe(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return Be(r, o).value[t];
  };
}
function xs(e, t) {
  var n;
  return (typeof t == "number" ? We : t instanceof ut ? Dn : (n = ut(t)) ? (t = n, Dn) : ds)(e, t);
}
function ed(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function td(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function nd(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttribute(e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function od(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttributeNS(e.space, e.local);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function rd(e, t, n) {
  var o, r, i;
  return function() {
    var s, a = n(this), l;
    return a == null ? void this.removeAttribute(e) : (s = this.getAttribute(e), l = a + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a)));
  };
}
function id(e, t, n) {
  var o, r, i;
  return function() {
    var s, a = n(this), l;
    return a == null ? void this.removeAttributeNS(e.space, e.local) : (s = this.getAttributeNS(e.space, e.local), l = a + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a)));
  };
}
function sd(e, t) {
  var n = Wn(e), o = n === "transform" ? Hu : xs;
  return this.attrTween(e, typeof t == "function" ? (n.local ? id : rd)(n, o, lr(this, "attr." + e, t)) : t == null ? (n.local ? td : ed)(n) : (n.local ? od : nd)(n, o, t));
}
function ad(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function cd(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function ld(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && cd(e, i)), n;
  }
  return r._value = t, r;
}
function ud(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && ad(e, i)), n;
  }
  return r._value = t, r;
}
function dd(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = Wn(e);
  return this.tween(n, (o.local ? ld : ud)(o, t));
}
function fd(e, t) {
  return function() {
    cr(this, e).delay = +t.apply(this, arguments);
  };
}
function hd(e, t) {
  return t = +t, function() {
    cr(this, e).delay = t;
  };
}
function pd(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? fd : hd)(t, e)) : Be(this.node(), t).delay;
}
function gd(e, t) {
  return function() {
    Xe(this, e).duration = +t.apply(this, arguments);
  };
}
function md(e, t) {
  return t = +t, function() {
    Xe(this, e).duration = t;
  };
}
function yd(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? gd : md)(t, e)) : Be(this.node(), t).duration;
}
function xd(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    Xe(this, e).ease = t;
  };
}
function wd(e) {
  var t = this._id;
  return arguments.length ? this.each(xd(t, e)) : Be(this.node(), t).ease;
}
function vd(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    Xe(this, e).ease = n;
  };
}
function bd(e) {
  if (typeof e != "function") throw new Error();
  return this.each(vd(this._id, e));
}
function Sd(e) {
  typeof e != "function" && (e = Ui(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = [], l, c = 0; c < s; ++c)
      (l = i[c]) && e.call(l, l.__data__, c, i) && a.push(l);
  return new Qe(o, this._parents, this._name, this._id);
}
function Ed(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, i = Math.min(o, r), s = new Array(o), a = 0; a < i; ++a)
    for (var l = t[a], c = n[a], u = l.length, f = s[a] = new Array(u), d, m = 0; m < u; ++m)
      (d = l[m] || c[m]) && (f[m] = d);
  for (; a < o; ++a)
    s[a] = t[a];
  return new Qe(s, this._parents, this._name, this._id);
}
function _d(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function Nd(e, t, n) {
  var o, r, i = _d(t) ? cr : Xe;
  return function() {
    var s = i(this, e), a = s.on;
    a !== o && (r = (o = a).copy()).on(t, n), s.on = r;
  };
}
function Cd(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Be(this.node(), n).on.on(e) : this.each(Nd(n, e, t));
}
function kd(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function Id() {
  return this.on("end.remove", kd(this._id));
}
function Md(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = or(e));
  for (var o = this._groups, r = o.length, i = new Array(r), s = 0; s < r; ++s)
    for (var a = o[s], l = a.length, c = i[s] = new Array(l), u, f, d = 0; d < l; ++d)
      (u = a[d]) && (f = e.call(u, u.__data__, d, a)) && ("__data__" in u && (f.__data__ = u.__data__), c[d] = f, Xn(c[d], t, n, d, c, Be(u, n)));
  return new Qe(i, this._parents, t, n);
}
function Ad(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Zi(e));
  for (var o = this._groups, r = o.length, i = [], s = [], a = 0; a < r; ++a)
    for (var l = o[a], c = l.length, u, f = 0; f < c; ++f)
      if (u = l[f]) {
        for (var d = e.call(u, u.__data__, f, l), m, p = Be(u, n), y = 0, w = d.length; y < w; ++y)
          (m = d[y]) && Xn(m, t, n, y, d, p);
        i.push(d), s.push(u);
      }
  return new Qe(i, s, t, n);
}
var Dd = en.prototype.constructor;
function Pd() {
  return new Dd(this._groups, this._parents);
}
function jd(e, t) {
  var n, o, r;
  return function() {
    var i = bt(this, e), s = (this.style.removeProperty(e), bt(this, e));
    return i === s ? null : i === n && s === o ? r : r = t(n = i, o = s);
  };
}
function ws(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function $d(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = bt(this, e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Td(e, t, n) {
  var o, r, i;
  return function() {
    var s = bt(this, e), a = n(this), l = a + "";
    return a == null && (l = a = (this.style.removeProperty(e), bt(this, e))), s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a));
  };
}
function Rd(e, t) {
  var n, o, r, i = "style." + t, s = "end." + i, a;
  return function() {
    var l = Xe(this, e), c = l.on, u = l.value[i] == null ? a || (a = ws(t)) : void 0;
    (c !== n || r !== u) && (o = (n = c).copy()).on(s, r = u), l.on = o;
  };
}
function zd(e, t, n) {
  var o = (e += "") == "transform" ? Lu : xs;
  return t == null ? this.styleTween(e, jd(e, o)).on("end.style." + e, ws(e)) : typeof t == "function" ? this.styleTween(e, Td(e, o, lr(this, "style." + e, t))).each(Rd(this._id, e)) : this.styleTween(e, $d(e, o, t), n).on("end.style." + e, null);
}
function Ld(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function Hd(e, t, n) {
  var o, r;
  function i() {
    var s = t.apply(this, arguments);
    return s !== r && (o = (r = s) && Ld(e, s, n)), o;
  }
  return i._value = t, i;
}
function Vd(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, Hd(e, t, n ?? ""));
}
function Od(e) {
  return function() {
    this.textContent = e;
  };
}
function Bd(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function Fd(e) {
  return this.tween("text", typeof e == "function" ? Bd(lr(this, "text", e)) : Od(e == null ? "" : e + ""));
}
function Wd(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Yd(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Wd(r)), t;
  }
  return o._value = e, o;
}
function Xd(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, Yd(e));
}
function qd() {
  for (var e = this._name, t = this._id, n = vs(), o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], a = s.length, l, c = 0; c < a; ++c)
      if (l = s[c]) {
        var u = Be(l, t);
        Xn(l, e, n, c, s, {
          time: u.time + u.delay + u.duration,
          delay: 0,
          duration: u.duration,
          ease: u.ease
        });
      }
  return new Qe(o, this._parents, e, n);
}
function Zd() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(i, s) {
    var a = { value: s }, l = { value: function() {
      --r === 0 && i();
    } };
    n.each(function() {
      var c = Xe(this, o), u = c.on;
      u !== e && (t = (e = u).copy(), t._.cancel.push(a), t._.interrupt.push(a), t._.end.push(l)), c.on = t;
    }), r === 0 && i();
  });
}
var Ud = 0;
function Qe(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function vs() {
  return ++Ud;
}
var Ue = en.prototype;
Qe.prototype = {
  constructor: Qe,
  select: Md,
  selectAll: Ad,
  selectChild: Ue.selectChild,
  selectChildren: Ue.selectChildren,
  filter: Sd,
  merge: Ed,
  selection: Pd,
  transition: qd,
  call: Ue.call,
  nodes: Ue.nodes,
  node: Ue.node,
  size: Ue.size,
  empty: Ue.empty,
  each: Ue.each,
  on: Cd,
  attr: sd,
  attrTween: dd,
  style: zd,
  styleTween: Vd,
  text: Fd,
  textTween: Xd,
  remove: Id,
  tween: Ju,
  delay: pd,
  duration: yd,
  ease: wd,
  easeVarying: bd,
  end: Zd,
  [Symbol.iterator]: Ue[Symbol.iterator]
};
function Kd(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var Gd = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: Kd
};
function Qd(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function Jd(e) {
  var t, n;
  e instanceof Qe ? (t = e._id, e = e._name) : (t = vs(), (n = Gd).time = ar(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], a = s.length, l, c = 0; c < a; ++c)
      (l = s[c]) && Xn(l, e, t, c, s, n || Qd(l, t));
  return new Qe(o, this._parents, e, t);
}
en.prototype.interrupt = Ku;
en.prototype.transition = Jd;
const mn = (e) => () => e;
function ef(e, {
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
var qn = new Ke(1, 0, 0);
bs.prototype = Ke.prototype;
function bs(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return qn;
  return e.__zoom;
}
function Co(e) {
  e.stopImmediatePropagation();
}
function zt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function tf(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function nf() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function Zr() {
  return this.__zoom || qn;
}
function of(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function rf() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function sf(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], i = e.invertY(t[0][1]) - n[0][1], s = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    s > i ? (i + s) / 2 : Math.min(0, i) || Math.max(0, s)
  );
}
function Ss() {
  var e = tf, t = nf, n = sf, o = of, r = rf, i = [0, 1 / 0], s = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], a = 250, l = Sn, c = Fn("start", "zoom", "end"), u, f, d, m = 500, p = 150, y = 0, w = 10;
  function x(b) {
    b.property("__zoom", Zr).on("wheel.zoom", D, { passive: !1 }).on("mousedown.zoom", P).on("dblclick.zoom", F).filter(r).on("touchstart.zoom", C).on("touchmove.zoom", R).on("touchend.zoom touchcancel.zoom", H).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  x.transform = function(b, k, N, A) {
    var T = b.selection ? b.selection() : b;
    T.property("__zoom", Zr), b !== T ? _(b, k, N, A) : T.interrupt().each(function() {
      E(this, arguments).event(A).start().zoom(null, typeof k == "function" ? k.apply(this, arguments) : k).end();
    });
  }, x.scaleBy = function(b, k, N, A) {
    x.scaleTo(b, function() {
      var T = this.__zoom.k, j = typeof k == "function" ? k.apply(this, arguments) : k;
      return T * j;
    }, N, A);
  }, x.scaleTo = function(b, k, N, A) {
    x.transform(b, function() {
      var T = t.apply(this, arguments), j = this.__zoom, B = N == null ? v(T) : typeof N == "function" ? N.apply(this, arguments) : N, V = j.invert(B), O = typeof k == "function" ? k.apply(this, arguments) : k;
      return n(g(S(j, O), B, V), T, s);
    }, N, A);
  }, x.translateBy = function(b, k, N, A) {
    x.transform(b, function() {
      return n(this.__zoom.translate(
        typeof k == "function" ? k.apply(this, arguments) : k,
        typeof N == "function" ? N.apply(this, arguments) : N
      ), t.apply(this, arguments), s);
    }, null, A);
  }, x.translateTo = function(b, k, N, A, T) {
    x.transform(b, function() {
      var j = t.apply(this, arguments), B = this.__zoom, V = A == null ? v(j) : typeof A == "function" ? A.apply(this, arguments) : A;
      return n(qn.translate(V[0], V[1]).scale(B.k).translate(
        typeof k == "function" ? -k.apply(this, arguments) : -k,
        typeof N == "function" ? -N.apply(this, arguments) : -N
      ), j, s);
    }, A, T);
  };
  function S(b, k) {
    return k = Math.max(i[0], Math.min(i[1], k)), k === b.k ? b : new Ke(k, b.x, b.y);
  }
  function g(b, k, N) {
    var A = k[0] - N[0] * b.k, T = k[1] - N[1] * b.k;
    return A === b.x && T === b.y ? b : new Ke(b.k, A, T);
  }
  function v(b) {
    return [(+b[0][0] + +b[1][0]) / 2, (+b[0][1] + +b[1][1]) / 2];
  }
  function _(b, k, N, A) {
    b.on("start.zoom", function() {
      E(this, arguments).event(A).start();
    }).on("interrupt.zoom end.zoom", function() {
      E(this, arguments).event(A).end();
    }).tween("zoom", function() {
      var T = this, j = arguments, B = E(T, j).event(A), V = t.apply(T, j), O = N == null ? v(V) : typeof N == "function" ? N.apply(T, j) : N, U = Math.max(V[1][0] - V[0][0], V[1][1] - V[0][1]), X = T.__zoom, ee = typeof k == "function" ? k.apply(T, j) : k, oe = l(X.invert(O).concat(U / X.k), ee.invert(O).concat(U / ee.k));
      return function(K) {
        if (K === 1) K = ee;
        else {
          var $ = oe(K), Y = U / $[2];
          K = new Ke(Y, O[0] - $[0] * Y, O[1] - $[1] * Y);
        }
        B.zoom(null, K);
      };
    });
  }
  function E(b, k, N) {
    return !N && b.__zooming || new M(b, k);
  }
  function M(b, k) {
    this.that = b, this.args = k, this.active = 0, this.sourceEvent = null, this.extent = t.apply(b, k), this.taps = 0;
  }
  M.prototype = {
    event: function(b) {
      return b && (this.sourceEvent = b), this;
    },
    start: function() {
      return ++this.active === 1 && (this.that.__zooming = this, this.emit("start")), this;
    },
    zoom: function(b, k) {
      return this.mouse && b !== "mouse" && (this.mouse[1] = k.invert(this.mouse[0])), this.touch0 && b !== "touch" && (this.touch0[1] = k.invert(this.touch0[0])), this.touch1 && b !== "touch" && (this.touch1[1] = k.invert(this.touch1[0])), this.that.__zoom = k, this.emit("zoom"), this;
    },
    end: function() {
      return --this.active === 0 && (delete this.that.__zooming, this.emit("end")), this;
    },
    emit: function(b) {
      var k = De(this.that).datum();
      c.call(
        b,
        this.that,
        new ef(b, {
          sourceEvent: this.sourceEvent,
          target: x,
          transform: this.that.__zoom,
          dispatch: c
        }),
        k
      );
    }
  };
  function D(b, ...k) {
    if (!e.apply(this, arguments)) return;
    var N = E(this, k).event(b), A = this.__zoom, T = Math.max(i[0], Math.min(i[1], A.k * Math.pow(2, o.apply(this, arguments)))), j = Le(b);
    if (N.wheel)
      (N.mouse[0][0] !== j[0] || N.mouse[0][1] !== j[1]) && (N.mouse[1] = A.invert(N.mouse[0] = j)), clearTimeout(N.wheel);
    else {
      if (A.k === T) return;
      N.mouse = [j, A.invert(j)], Nn(this), N.start();
    }
    zt(b), N.wheel = setTimeout(B, p), N.zoom("mouse", n(g(S(A, T), N.mouse[0], N.mouse[1]), N.extent, s));
    function B() {
      N.wheel = null, N.end();
    }
  }
  function P(b, ...k) {
    if (d || !e.apply(this, arguments)) return;
    var N = b.currentTarget, A = E(this, k, !0).event(b), T = De(b.view).on("mousemove.zoom", O, !0).on("mouseup.zoom", U, !0), j = Le(b, N), B = b.clientX, V = b.clientY;
    is(b.view), Co(b), A.mouse = [j, this.__zoom.invert(j)], Nn(this), A.start();
    function O(X) {
      if (zt(X), !A.moved) {
        var ee = X.clientX - B, oe = X.clientY - V;
        A.moved = ee * ee + oe * oe > y;
      }
      A.event(X).zoom("mouse", n(g(A.that.__zoom, A.mouse[0] = Le(X, N), A.mouse[1]), A.extent, s));
    }
    function U(X) {
      T.on("mousemove.zoom mouseup.zoom", null), ss(X.view, A.moved), zt(X), A.event(X).end();
    }
  }
  function F(b, ...k) {
    if (e.apply(this, arguments)) {
      var N = this.__zoom, A = Le(b.changedTouches ? b.changedTouches[0] : b, this), T = N.invert(A), j = N.k * (b.shiftKey ? 0.5 : 2), B = n(g(S(N, j), A, T), t.apply(this, k), s);
      zt(b), a > 0 ? De(this).transition().duration(a).call(_, B, A, b) : De(this).call(x.transform, B, A, b);
    }
  }
  function C(b, ...k) {
    if (e.apply(this, arguments)) {
      var N = b.touches, A = N.length, T = E(this, k, b.changedTouches.length === A).event(b), j, B, V, O;
      for (Co(b), B = 0; B < A; ++B)
        V = N[B], O = Le(V, this), O = [O, this.__zoom.invert(O), V.identifier], T.touch0 ? !T.touch1 && T.touch0[2] !== O[2] && (T.touch1 = O, T.taps = 0) : (T.touch0 = O, j = !0, T.taps = 1 + !!u);
      u && (u = clearTimeout(u)), j && (T.taps < 2 && (f = O[0], u = setTimeout(function() {
        u = null;
      }, m)), Nn(this), T.start());
    }
  }
  function R(b, ...k) {
    if (this.__zooming) {
      var N = E(this, k).event(b), A = b.changedTouches, T = A.length, j, B, V, O;
      for (zt(b), j = 0; j < T; ++j)
        B = A[j], V = Le(B, this), N.touch0 && N.touch0[2] === B.identifier ? N.touch0[0] = V : N.touch1 && N.touch1[2] === B.identifier && (N.touch1[0] = V);
      if (B = N.that.__zoom, N.touch1) {
        var U = N.touch0[0], X = N.touch0[1], ee = N.touch1[0], oe = N.touch1[1], K = (K = ee[0] - U[0]) * K + (K = ee[1] - U[1]) * K, $ = ($ = oe[0] - X[0]) * $ + ($ = oe[1] - X[1]) * $;
        B = S(B, Math.sqrt(K / $)), V = [(U[0] + ee[0]) / 2, (U[1] + ee[1]) / 2], O = [(X[0] + oe[0]) / 2, (X[1] + oe[1]) / 2];
      } else if (N.touch0) V = N.touch0[0], O = N.touch0[1];
      else return;
      N.zoom("touch", n(g(B, V, O), N.extent, s));
    }
  }
  function H(b, ...k) {
    if (this.__zooming) {
      var N = E(this, k).event(b), A = b.changedTouches, T = A.length, j, B;
      for (Co(b), d && clearTimeout(d), d = setTimeout(function() {
        d = null;
      }, m), j = 0; j < T; ++j)
        B = A[j], N.touch0 && N.touch0[2] === B.identifier ? delete N.touch0 : N.touch1 && N.touch1[2] === B.identifier && delete N.touch1;
      if (N.touch1 && !N.touch0 && (N.touch0 = N.touch1, delete N.touch1), N.touch0) N.touch0[1] = this.__zoom.invert(N.touch0[0]);
      else if (N.end(), N.taps === 2 && (B = Le(B, this), Math.hypot(f[0] - B[0], f[1] - B[1]) < w)) {
        var V = De(this).on("dblclick.zoom");
        V && V.apply(this, arguments);
      }
    }
  }
  return x.wheelDelta = function(b) {
    return arguments.length ? (o = typeof b == "function" ? b : mn(+b), x) : o;
  }, x.filter = function(b) {
    return arguments.length ? (e = typeof b == "function" ? b : mn(!!b), x) : e;
  }, x.touchable = function(b) {
    return arguments.length ? (r = typeof b == "function" ? b : mn(!!b), x) : r;
  }, x.extent = function(b) {
    return arguments.length ? (t = typeof b == "function" ? b : mn([[+b[0][0], +b[0][1]], [+b[1][0], +b[1][1]]]), x) : t;
  }, x.scaleExtent = function(b) {
    return arguments.length ? (i[0] = +b[0], i[1] = +b[1], x) : [i[0], i[1]];
  }, x.translateExtent = function(b) {
    return arguments.length ? (s[0][0] = +b[0][0], s[1][0] = +b[1][0], s[0][1] = +b[0][1], s[1][1] = +b[1][1], x) : [[s[0][0], s[0][1]], [s[1][0], s[1][1]]];
  }, x.constrain = function(b) {
    return arguments.length ? (n = b, x) : n;
  }, x.duration = function(b) {
    return arguments.length ? (a = +b, x) : a;
  }, x.interpolate = function(b) {
    return arguments.length ? (l = b, x) : l;
  }, x.on = function() {
    var b = c.on.apply(c, arguments);
    return b === c ? x : b;
  }, x.clickDistance = function(b) {
    return arguments.length ? (y = (b = +b) * b, x) : Math.sqrt(y);
  }, x.tapDistance = function(b) {
    return arguments.length ? (w = +b, x) : w;
  }, x;
}
const je = {
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
}, qt = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], Es = ["Enter", " ", "Escape"], _s = {
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
var lt;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(lt || (lt = {}));
var Zt;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Zt || (Zt = {}));
const Ns = {
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
var ot;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(ot || (ot = {}));
var Tn;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(Tn || (Tn = {}));
var J;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(J || (J = {}));
const Ur = {
  [J.Left]: J.Right,
  [J.Right]: J.Left,
  [J.Top]: J.Bottom,
  [J.Bottom]: J.Top
};
function Cs(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const ks = (e) => "id" in e && "source" in e && "target" in e, af = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), ur = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), nn = (e, t = [0, 0]) => {
  const { width: n, height: o } = Je(e), r = e.origin ?? t, i = n * r[0], s = o * r[1];
  return {
    x: e.position.x - i,
    y: e.position.y - s
  };
}, cf = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const i = typeof r == "string";
    let s = !t.nodeLookup && !i ? r : void 0;
    t.nodeLookup && (s = i ? t.nodeLookup.get(r) : ur(r) ? r : t.nodeLookup.get(r.id));
    const a = s ? Rn(s, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return Zn(o, a);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return Un(n);
}, on = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = Zn(n, Rn(r)), o = !0);
  }), o ? Un(n) : { x: 0, y: 0, width: 0, height: 0 };
}, dr = (e, t, [n, o, r] = [0, 0, 1], i = !1, s = !1) => {
  const a = {
    ...Pt(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, l = [];
  for (const c of e.values()) {
    const { measured: u, selectable: f = !0, hidden: d = !1 } = c;
    if (s && !f || d)
      continue;
    const m = u.width ?? c.width ?? c.initialWidth ?? null, p = u.height ?? c.height ?? c.initialHeight ?? null, y = Ut(a, Nt(c)), w = (m ?? 0) * (p ?? 0), x = i && y > 0;
    (!c.internals.handleBounds || x || y >= w || c.dragging) && l.push(c);
  }
  return l;
}, lf = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function uf(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function df({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: i }, s) {
  if (e.size === 0)
    return !0;
  const a = uf(e, s), l = on(a), c = hr(l, t, n, s?.minZoom ?? r, s?.maxZoom ?? i, s?.padding ?? 0.1);
  return await o.setViewport(c, {
    duration: s?.duration,
    ease: s?.ease,
    interpolate: s?.interpolate
  }), !0;
}
function Is({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: i }) {
  const s = n.get(e), a = s.parentId ? n.get(s.parentId) : void 0, { x: l, y: c } = a ? a.internals.positionAbsolute : { x: 0, y: 0 }, u = s.origin ?? o;
  let f = s.extent || r;
  if (s.extent === "parent" && !s.expandParent)
    if (!a)
      i?.("005", je.error005());
    else {
      const m = a.measured.width, p = a.measured.height;
      m && p && (f = [
        [l, c],
        [l + m, c + p]
      ]);
    }
  else a && ht(s.extent) && (f = [
    [s.extent[0][0] + l, s.extent[0][1] + c],
    [s.extent[1][0] + l, s.extent[1][1] + c]
  ]);
  const d = ht(f) ? ft(t, f, s.measured) : t;
  return (s.measured.width === void 0 || s.measured.height === void 0) && i?.("015", je.error015()), {
    position: {
      x: d.x - l + (s.measured.width ?? 0) * u[0],
      y: d.y - c + (s.measured.height ?? 0) * u[1]
    },
    positionAbsolute: d
  };
}
async function ff({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const i = new Set(e.map((d) => d.id)), s = [];
  for (const d of n) {
    if (d.deletable === !1)
      continue;
    const m = i.has(d.id), p = !m && d.parentId && s.find((y) => y.id === d.parentId);
    (m || p) && s.push(d);
  }
  const a = new Set(t.map((d) => d.id)), l = o.filter((d) => d.deletable !== !1), u = lf(s, l);
  for (const d of l)
    a.has(d.id) && !u.find((p) => p.id === d.id) && u.push(d);
  if (!r)
    return {
      edges: u,
      nodes: s
    };
  const f = await r({
    nodes: s,
    edges: u
  });
  return typeof f == "boolean" ? f ? { edges: u, nodes: s } : { edges: [], nodes: [] } : f;
}
const _t = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), ft = (e = { x: 0, y: 0 }, t, n) => ({
  x: _t(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: _t(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Ms(e, t, n) {
  const { width: o, height: r } = Je(n), { x: i, y: s } = n.internals.positionAbsolute;
  return ft(e, [
    [i, s],
    [i + o, s + r]
  ], t);
}
const Kr = (e, t, n) => e < t ? _t(Math.abs(e - t), 1, t) / t : e > n ? -_t(Math.abs(e - n), 1, t) / t : 0, fr = (e, t, n = 15, o = 40) => {
  const r = Kr(e.x, o, t.width - o) * n, i = Kr(e.y, o, t.height - o) * n;
  return [r, i];
}, Zn = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), Zo = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), Un = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), Nt = (e, t = [0, 0]) => {
  const { x: n, y: o } = ur(e) ? e.internals.positionAbsolute : nn(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, Rn = (e, t = [0, 0]) => {
  const { x: n, y: o } = ur(e) ? e.internals.positionAbsolute : nn(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, As = (e, t) => Un(Zn(Zo(e), Zo(t))), Ut = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, Gr = (e) => Ve(e.width) && Ve(e.height) && Ve(e.x) && Ve(e.y), Ve = (e) => !isNaN(e) && isFinite(e), Ds = (e, t) => (n, o) => {
}, rn = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), Pt = ({ x: e, y: t }, [n, o, r], i = !1, s = [1, 1]) => {
  const a = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return i ? rn(a, s) : a;
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
function hf(e, t, n) {
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
function pf(e, t, n, o, r, i) {
  const { x: s, y: a } = Ct(e, [t, n, o]), { x: l, y: c } = Ct({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), u = r - l, f = i - c;
  return {
    left: Math.floor(s),
    top: Math.floor(a),
    right: Math.floor(u),
    bottom: Math.floor(f)
  };
}
const hr = (e, t, n, o, r, i) => {
  const s = hf(i, t, n), a = (t - s.x) / e.width, l = (n - s.y) / e.height, c = Math.min(a, l), u = _t(c, o, r), f = e.x + e.width / 2, d = e.y + e.height / 2, m = t / 2 - f * u, p = n / 2 - d * u, y = pf(e, m, p, u, t, n), w = {
    left: Math.min(y.left - s.left, 0),
    top: Math.min(y.top - s.top, 0),
    right: Math.min(y.right - s.right, 0),
    bottom: Math.min(y.bottom - s.bottom, 0)
  };
  return {
    x: m - w.left + w.right,
    y: p - w.top + w.bottom,
    zoom: u
  };
}, Kt = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function ht(e) {
  return e != null && e !== "parent";
}
function Je(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function Ps(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function js(e, t = { width: 0, height: 0 }, n, o, r) {
  const i = { ...e }, s = o.get(n);
  if (s) {
    const a = s.origin || r;
    i.x += s.internals.positionAbsolute.x - (t.width ?? 0) * a[0], i.y += s.internals.positionAbsolute.y - (t.height ?? 0) * a[1];
  }
  return i;
}
function Qr(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function gf() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function mf(e) {
  return { ..._s, ...e || {} };
}
function Bt(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: i, y: s } = Oe(e), a = Pt({ x: i - (r?.left ?? 0), y: s - (r?.top ?? 0) }, o), { x: l, y: c } = n ? rn(a, t) : a;
  return {
    xSnapped: l,
    ySnapped: c,
    ...a
  };
}
const pr = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), $s = (e) => e?.getRootNode?.() || window?.document, yf = ["INPUT", "SELECT", "TEXTAREA"];
function Ts(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : yf.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const Rs = (e) => "clientX" in e, Oe = (e, t) => {
  const n = Rs(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, Jr = (e, t, n, o, r) => {
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
      ...pr(s)
    };
  });
};
function zs({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: i, targetControlX: s, targetControlY: a }) {
  const l = e * 0.125 + r * 0.375 + s * 0.375 + n * 0.125, c = t * 0.125 + i * 0.375 + a * 0.375 + o * 0.125, u = Math.abs(l - e), f = Math.abs(c - t);
  return [l, c, u, f];
}
function yn(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function ei({ pos: e, x1: t, y1: n, x2: o, y2: r, c: i }) {
  switch (e) {
    case J.Left:
      return [t - yn(t - o, i), n];
    case J.Right:
      return [t + yn(o - t, i), n];
    case J.Top:
      return [t, n - yn(n - r, i)];
    case J.Bottom:
      return [t, n + yn(r - n, i)];
  }
}
function Ls({ sourceX: e, sourceY: t, sourcePosition: n = J.Bottom, targetX: o, targetY: r, targetPosition: i = J.Top, curvature: s = 0.25 }) {
  const [a, l] = ei({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: s
  }), [c, u] = ei({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: s
  }), [f, d, m, p] = zs({
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
    f,
    d,
    m,
    p
  ];
}
function Hs({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, i = n < e ? n + r : n - r, s = Math.abs(o - t) / 2, a = o < t ? o + s : o - s;
  return [i, a, r, s];
}
function xf({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: i = "basic" }) {
  if (i === "manual")
    return o;
  const s = r && n ? o + 1e3 : o, a = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return s + a;
}
function wf({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const i = Zn(Rn(e), Rn(t));
  i.x === i.x2 && (i.x2 += 1), i.y === i.y2 && (i.y2 += 1);
  const s = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return Ut(s, Un(i)) > 0;
}
const Vs = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, vf = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), bf = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", je.error006()), t;
  const o = n.getEdgeId || Vs;
  let r;
  return ks(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, vf(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, Sf = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: r, ...i } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", je.error006()), n;
  if (!n.find((c) => c.id === e.id))
    return o.onError?.("007", je.error007(r)), n;
  const a = o.getEdgeId || Vs, l = {
    ...i,
    id: o.shouldReplaceId ? a(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((c) => c.id !== r).concat(l);
};
function Os({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, i, s, a] = Hs({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, i, s, a];
}
const ti = {
  [J.Left]: { x: -1, y: 0 },
  [J.Right]: { x: 1, y: 0 },
  [J.Top]: { x: 0, y: -1 },
  [J.Bottom]: { x: 0, y: 1 }
}, Ef = ({ source: e, sourcePosition: t = J.Bottom, target: n }) => t === J.Left || t === J.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, ni = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function _f({ source: e, sourcePosition: t = J.Bottom, target: n, targetPosition: o = J.Top, center: r, offset: i, stepPosition: s }) {
  const a = ti[t], l = ti[o], c = { x: e.x + a.x * i, y: e.y + a.y * i }, u = { x: n.x + l.x * i, y: n.y + l.y * i }, f = Ef({
    source: c,
    sourcePosition: t,
    target: u
  }), d = f.x !== 0 ? "x" : "y", m = f[d];
  let p = [], y, w;
  const x = { x: 0, y: 0 }, S = { x: 0, y: 0 }, [, , g, v] = Hs({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (a[d] * l[d] === -1) {
    d === "x" ? (y = r.x ?? c.x + (u.x - c.x) * s, w = r.y ?? (c.y + u.y) / 2) : (y = r.x ?? (c.x + u.x) / 2, w = r.y ?? c.y + (u.y - c.y) * s);
    const D = [
      { x: y, y: c.y },
      { x: y, y: u.y }
    ], P = [
      { x: c.x, y: w },
      { x: u.x, y: w }
    ];
    a[d] === m ? p = d === "x" ? D : P : p = d === "x" ? P : D;
  } else {
    const D = [{ x: c.x, y: u.y }], P = [{ x: u.x, y: c.y }];
    if (d === "x" ? p = a.x === m ? P : D : p = a.y === m ? D : P, t === o) {
      const b = Math.abs(e[d] - n[d]);
      if (b <= i) {
        const k = Math.min(i - 1, i - b);
        a[d] === m ? x[d] = (c[d] > e[d] ? -1 : 1) * k : S[d] = (u[d] > n[d] ? -1 : 1) * k;
      }
    }
    if (t !== o) {
      const b = d === "x" ? "y" : "x", k = a[d] === l[b], N = c[b] > u[b], A = c[b] < u[b];
      (a[d] === 1 && (!k && N || k && A) || a[d] !== 1 && (!k && A || k && N)) && (p = d === "x" ? D : P);
    }
    const F = { x: c.x + x.x, y: c.y + x.y }, C = { x: u.x + S.x, y: u.y + S.y }, R = Math.max(Math.abs(F.x - p[0].x), Math.abs(C.x - p[0].x)), H = Math.max(Math.abs(F.y - p[0].y), Math.abs(C.y - p[0].y));
    R >= H ? (y = (F.x + C.x) / 2, w = p[0].y) : (y = p[0].x, w = (F.y + C.y) / 2);
  }
  const _ = { x: c.x + x.x, y: c.y + x.y }, E = { x: u.x + S.x, y: u.y + S.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ..._.x !== p[0].x || _.y !== p[0].y ? [_] : [],
    ...p,
    ...E.x !== p[p.length - 1].x || E.y !== p[p.length - 1].y ? [E] : [],
    n
  ], y, w, g, v];
}
function Nf(e, t, n, o) {
  const r = Math.min(ni(e, t) / 2, ni(t, n) / 2, o), { x: i, y: s } = t;
  if (e.x === i && i === n.x || e.y === s && s === n.y)
    return `L${i} ${s}`;
  if (e.y === s) {
    const c = e.x < n.x ? -1 : 1, u = e.y < n.y ? 1 : -1;
    return `L ${i + r * c},${s}Q ${i},${s} ${i},${s + r * u}`;
  }
  const a = e.x < n.x ? 1 : -1, l = e.y < n.y ? -1 : 1;
  return `L ${i},${s + r * l}Q ${i},${s} ${i + r * a},${s}`;
}
function zn({ sourceX: e, sourceY: t, sourcePosition: n = J.Bottom, targetX: o, targetY: r, targetPosition: i = J.Top, borderRadius: s = 5, centerX: a, centerY: l, offset: c = 20, stepPosition: u = 0.5 }) {
  const [f, d, m, p, y] = _f({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: i,
    center: { x: a, y: l },
    offset: c,
    stepPosition: u
  });
  let w = `M${f[0].x} ${f[0].y}`;
  for (let x = 1; x < f.length - 1; x++)
    w += Nf(f[x - 1], f[x], f[x + 1], s);
  return w += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [w, d, m, p, y];
}
function oi(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function Cf(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!oi(t) || !oi(n))
    return null;
  const o = t.internals.handleBounds || ri(t.handles), r = n.internals.handleBounds || ri(n.handles), i = ii(o?.source ?? [], e.sourceHandle), s = ii(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === Et.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!i || !s)
    return e.onError?.("008", je.error008(i ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const a = i?.position || J.Bottom, l = s?.position || J.Top, c = pt(t, i, a), u = pt(n, s, l);
  return {
    sourceX: c.x,
    sourceY: c.y,
    targetX: u.x,
    targetY: u.y,
    sourcePosition: a,
    targetPosition: l
  };
}
function ri(e) {
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
function pt(e, t, n = J.Left, o = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, i = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: s, height: a } = t ?? Je(e);
  if (o)
    return { x: r + s / 2, y: i + a / 2 };
  switch (t?.position ?? n) {
    case J.Top:
      return { x: r + s / 2, y: i };
    case J.Right:
      return { x: r + s, y: i + a / 2 };
    case J.Bottom:
      return { x: r + s / 2, y: i + a };
    case J.Left:
      return { x: r, y: i + a / 2 };
  }
}
function ii(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Uo(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function kf(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const i = /* @__PURE__ */ new Set();
  return e.reduce((s, a) => ([a.markerStart || o, a.markerEnd || r].forEach((l) => {
    if (l && typeof l == "object") {
      const c = Uo(l, t);
      i.has(c) || (s.push({ id: c, color: l.color || n, ...l }), i.add(c));
    }
  }), s), []).sort((s, a) => s.id.localeCompare(a.id));
}
const Bs = 1e3, If = 10, gr = {
  nodeOrigin: [0, 0],
  nodeExtent: qt,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Mf = {
  ...gr,
  checkEquality: !0
};
function mr(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function Af(e, t, n) {
  const o = mr(gr, n);
  for (const r of e.values())
    if (r.parentId)
      xr(r, e, t, o);
    else {
      const i = nn(r, o.nodeOrigin), s = ht(r.extent) ? r.extent : o.nodeExtent, a = ft(i, s, Je(r));
      r.internals.positionAbsolute = a;
    }
}
function Df(e, t) {
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
function yr(e) {
  return e === "manual";
}
function Ko(e, t, n, o = {}) {
  const r = mr(Mf, o), i = { i: 0 }, s = new Map(t), a = r?.elevateNodesOnSelect && !yr(r.zIndexMode) ? Bs : 0;
  let l = e.length > 0, c = !1;
  t.clear(), n.clear();
  for (const u of e) {
    let f = s.get(u.id);
    if (r.checkEquality && u === f?.internals.userNode)
      t.set(u.id, f);
    else {
      const d = nn(u, r.nodeOrigin), m = ht(u.extent) ? u.extent : r.nodeExtent, p = ft(d, m, Je(u));
      f = {
        ...r.defaults,
        ...u,
        measured: {
          width: u.measured?.width,
          height: u.measured?.height
        },
        internals: {
          positionAbsolute: p,
          // if user re-initializes the node or removes `measured` for whatever reason, we reset the handleBounds so that the node gets re-measured
          handleBounds: Df(u, f),
          z: Fs(u, a, r.zIndexMode),
          userNode: u
        }
      }, t.set(u.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (l = !1), u.parentId && xr(f, t, n, o, i), c ||= u.selected ?? !1;
  }
  return { nodesInitialized: l, hasSelectedNodes: c };
}
function Pf(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function xr(e, t, n, o, r) {
  const { elevateNodesOnSelect: i, nodeOrigin: s, nodeExtent: a, zIndexMode: l } = mr(gr, o), c = e.parentId, u = t.get(c);
  if (!u) {
    console.warn(`Parent node ${c} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Pf(e, n), r && !u.parentId && u.internals.rootParentIndex === void 0 && l === "auto" && (u.internals.rootParentIndex = ++r.i, u.internals.z = u.internals.z + r.i * If), r && u.internals.rootParentIndex !== void 0 && (r.i = u.internals.rootParentIndex);
  const f = i && !yr(l) ? Bs : 0, { x: d, y: m, z: p } = jf(e, u, s, a, f, l), { positionAbsolute: y } = e.internals, w = d !== y.x || m !== y.y;
  (w || p !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: w ? { x: d, y: m } : y,
      z: p
    }
  });
}
function Fs(e, t, n) {
  const o = Ve(e.zIndex) ? e.zIndex : 0;
  return yr(n) ? o : o + (e.selected ? t : 0);
}
function jf(e, t, n, o, r, i) {
  const { x: s, y: a } = t.internals.positionAbsolute, l = Je(e), c = nn(e, n), u = ht(e.extent) ? ft(c, e.extent, l) : c;
  let f = ft({ x: s + u.x, y: a + u.y }, o, l);
  e.extent === "parent" && (f = Ms(f, l, t));
  const d = Fs(e, r, i), m = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: m >= d ? m + 1 : d
  };
}
function wr(e, t, n, o = [0, 0]) {
  const r = [], i = /* @__PURE__ */ new Map();
  for (const s of e) {
    const a = t.get(s.parentId);
    if (!a)
      continue;
    const l = i.get(s.parentId)?.expandedRect ?? Nt(a), c = As(l, s.rect);
    i.set(s.parentId, { expandedRect: c, parent: a });
  }
  return i.size > 0 && i.forEach(({ expandedRect: s, parent: a }, l) => {
    const c = a.internals.positionAbsolute, u = Je(a), f = a.origin ?? o, d = s.x < c.x ? Math.round(Math.abs(c.x - s.x)) : 0, m = s.y < c.y ? Math.round(Math.abs(c.y - s.y)) : 0, p = Math.max(u.width, Math.round(s.width)), y = Math.max(u.height, Math.round(s.height)), w = (p - u.width) * f[0], x = (y - u.height) * f[1];
    (d > 0 || m > 0 || w || x) && (r.push({
      id: l,
      type: "position",
      position: {
        x: a.position.x - d + w,
        y: a.position.y - m + x
      }
    }), n.get(l)?.forEach((S) => {
      e.some((g) => g.id === S.id) || r.push({
        id: S.id,
        type: "position",
        position: {
          x: S.position.x + d,
          y: S.position.y + m
        }
      });
    })), (u.width < s.width || u.height < s.height || d || m) && r.push({
      id: l,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: p + (d ? f[0] * d - w : 0),
        height: y + (m ? f[1] * m - x : 0)
      }
    });
  }), r;
}
function $f(e, t, n, o, r, i, s) {
  const a = o?.querySelector(".xyflow__viewport");
  let l = !1;
  if (!a)
    return { changes: [], updatedInternals: l };
  const c = [], u = window.getComputedStyle(a), { m22: f } = new window.DOMMatrixReadOnly(u.transform), d = [];
  for (const m of e.values()) {
    const p = t.get(m.id);
    if (!p)
      continue;
    if (p.hidden) {
      t.set(p.id, {
        ...p,
        internals: {
          ...p.internals,
          handleBounds: void 0
        }
      }), l = !0;
      continue;
    }
    const y = pr(m.nodeElement), w = p.measured.width !== y.width || p.measured.height !== y.height;
    if (!!(y.width && y.height && (w || !p.internals.handleBounds || m.force))) {
      const S = m.nodeElement.getBoundingClientRect(), g = ht(p.extent) ? p.extent : i;
      let { positionAbsolute: v } = p.internals;
      p.parentId && p.extent === "parent" ? v = Ms(v, y, t.get(p.parentId)) : g && (v = ft(v, g, y));
      const _ = {
        ...p,
        measured: y,
        internals: {
          ...p.internals,
          positionAbsolute: v,
          handleBounds: {
            source: Jr("source", m.nodeElement, S, f, p.id),
            target: Jr("target", m.nodeElement, S, f, p.id)
          }
        }
      };
      t.set(p.id, _), p.parentId && xr(_, t, n, { nodeOrigin: r, zIndexMode: s }), l = !0, w && (c.push({
        id: p.id,
        type: "dimensions",
        dimensions: y
      }), p.expandParent && p.parentId && d.push({
        id: p.id,
        parentId: p.parentId,
        rect: Nt(_, r)
      }));
    }
  }
  if (d.length > 0) {
    const m = wr(d, t, n, r);
    c.push(...m);
  }
  return { changes: c, updatedInternals: l };
}
async function Tf({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: i }) {
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
function si(e, t, n, o, r, i) {
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
function Ws(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: i, sourceHandle: s = null, targetHandle: a = null } = o, l = { edgeId: o.id, source: r, target: i, sourceHandle: s, targetHandle: a }, c = `${r}-${s}--${i}-${a}`, u = `${i}-${a}--${r}-${s}`;
    si("source", l, u, e, r, s), si("target", l, c, e, i, a), t.set(o.id, o);
  }
}
function Ys(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : Ys(n, t) : !1;
}
function ai(e, t, n) {
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
function Rf(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [i, s] of e)
    if ((s.selected || s.id === o) && (!s.parentId || !Ys(s, e)) && (s.draggable || t && typeof s.draggable > "u")) {
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
function ko({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
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
function zf({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const i = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, s = rn(i, t);
  return {
    x: s.x - i.x,
    y: s.y - i.y
  };
}
function Lf({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let i = { x: null, y: null }, s = 0, a = /* @__PURE__ */ new Map(), l = !1, c = { x: 0, y: 0 }, u = null, f = !1, d = null, m = !1, p = !1, y = null;
  function w({ noDragClassName: S, handleSelector: g, domNode: v, isSelectable: _, nodeId: E, nodeClickDistance: M = 0 }) {
    d = De(v);
    function D({ x: R, y: H }) {
      const { nodeLookup: b, nodeExtent: k, snapGrid: N, snapToGrid: A, nodeOrigin: T, onNodeDrag: j, onSelectionDrag: B, onError: V, updateNodePositions: O } = t();
      i = { x: R, y: H };
      let U = !1;
      const X = a.size > 1, ee = X && k ? Zo(on(a)) : null, oe = X && A ? zf({
        dragItems: a,
        snapGrid: N,
        x: R,
        y: H
      }) : null;
      for (const [K, $] of a) {
        if (!b.has(K))
          continue;
        let Y = { x: R - $.distance.x, y: H - $.distance.y };
        A && (Y = oe ? {
          x: Math.round(Y.x + oe.x),
          y: Math.round(Y.y + oe.y)
        } : rn(Y, N));
        let ie = null;
        if (X && k && !$.extent && ee) {
          const { positionAbsolute: q } = $.internals, se = q.x - ee.x + k[0][0], L = q.x + $.measured.width - ee.x2 + k[1][0], Q = q.y - ee.y + k[0][1], fe = q.y + $.measured.height - ee.y2 + k[1][1];
          ie = [
            [se, Q],
            [L, fe]
          ];
        }
        const { position: te, positionAbsolute: G } = Is({
          nodeId: K,
          nextPosition: Y,
          nodeLookup: b,
          nodeExtent: ie || k,
          nodeOrigin: T,
          onError: V
        });
        U = U || $.position.x !== te.x || $.position.y !== te.y, $.position = te, $.internals.positionAbsolute = G;
      }
      if (p = p || U, !!U && (O(a, !0), y && (o || j || !E && B))) {
        const [K, $] = ko({
          nodeId: E,
          dragItems: a,
          nodeLookup: b
        });
        o?.(y, a, K, $), j?.(y, K, $), E || B?.(y, $);
      }
    }
    async function P() {
      if (!u)
        return;
      const { transform: R, panBy: H, autoPanSpeed: b, autoPanOnNodeDrag: k } = t();
      if (!k) {
        l = !1, cancelAnimationFrame(s);
        return;
      }
      const [N, A] = fr(c, u, b);
      (N !== 0 || A !== 0) && (i.x = (i.x ?? 0) - N / R[2], i.y = (i.y ?? 0) - A / R[2], await H({ x: N, y: A }) && D(i)), s = requestAnimationFrame(P);
    }
    function F(R) {
      const { nodeLookup: H, multiSelectionActive: b, nodesDraggable: k, transform: N, snapGrid: A, snapToGrid: T, selectNodesOnDrag: j, onNodeDragStart: B, onSelectionDragStart: V, unselectNodesAndEdges: O } = t();
      f = !0, (!j || !_) && !b && E && (H.get(E)?.selected || O()), _ && j && E && e?.(E);
      const U = Bt(R.sourceEvent, { transform: N, snapGrid: A, snapToGrid: T, containerBounds: u });
      if (i = U, a = Rf(H, k, U, E), a.size > 0 && (n || B || !E && V)) {
        const [X, ee] = ko({
          nodeId: E,
          dragItems: a,
          nodeLookup: H
        });
        n?.(R.sourceEvent, a, X, ee), B?.(R.sourceEvent, X, ee), E || V?.(R.sourceEvent, ee);
      }
    }
    const C = as().clickDistance(M).on("start", (R) => {
      const { domNode: H, nodeDragThreshold: b, transform: k, snapGrid: N, snapToGrid: A } = t();
      u = H?.getBoundingClientRect() || null, m = !1, p = !1, y = R.sourceEvent, b === 0 && F(R), i = Bt(R.sourceEvent, { transform: k, snapGrid: N, snapToGrid: A, containerBounds: u }), c = Oe(R.sourceEvent, u);
    }).on("drag", (R) => {
      const { autoPanOnNodeDrag: H, transform: b, snapGrid: k, snapToGrid: N, nodeDragThreshold: A, nodeLookup: T } = t(), j = Bt(R.sourceEvent, { transform: b, snapGrid: k, snapToGrid: N, containerBounds: u });
      if (y = R.sourceEvent, (R.sourceEvent.type === "touchmove" && R.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      E && !T.has(E)) && (m = !0), !m) {
        if (!l && H && f && (l = !0, P()), !f) {
          const B = Oe(R.sourceEvent, u), V = B.x - c.x, O = B.y - c.y;
          Math.sqrt(V * V + O * O) > A && F(R);
        }
        (i.x !== j.xSnapped || i.y !== j.ySnapped) && a && f && (c = Oe(R.sourceEvent, u), D(j));
      }
    }).on("end", (R) => {
      if (!f || m) {
        m && a.size > 0 && t().updateNodePositions(a, !1);
        return;
      }
      if (l = !1, f = !1, cancelAnimationFrame(s), a.size > 0) {
        const { nodeLookup: H, updateNodePositions: b, onNodeDragStop: k, onSelectionDragStop: N } = t();
        if (p && (b(a, !1), p = !1), r || k || !E && N) {
          const [A, T] = ko({
            nodeId: E,
            dragItems: a,
            nodeLookup: H,
            dragging: !1
          });
          r?.(R.sourceEvent, a, A, T), k?.(R.sourceEvent, A, T), E || N?.(R.sourceEvent, T);
        }
      }
    }).filter((R) => {
      const H = R.target;
      return !R.button && (!S || !ai(H, `.${S}`, v)) && (!g || ai(H, g, v));
    });
    d.call(C);
  }
  function x() {
    d?.on(".drag", null);
  }
  return {
    update: w,
    destroy: x
  };
}
function Hf(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const i of t.values())
    Ut(r, Nt(i)) > 0 && o.push(i);
  return o;
}
const Vf = 250;
function Of(e, t, n, o) {
  let r = [], i = 1 / 0;
  const s = Hf(e, n, t + Vf);
  for (const a of s) {
    const l = [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []];
    for (const c of l) {
      if (o.nodeId === c.nodeId && o.type === c.type && o.id === c.id)
        continue;
      const { x: u, y: f } = pt(a, c, c.position, !0), d = Math.sqrt(Math.pow(u - e.x, 2) + Math.pow(f - e.y, 2));
      d > t || (d < i ? (r = [{ ...c, x: u, y: f }], i = d) : d === i && r.push({ ...c, x: u, y: f }));
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
function Xs(e, t, n, o, r, i = !1) {
  const s = o.get(e);
  if (!s)
    return null;
  const a = r === "strict" ? s.internals.handleBounds?.[t] : [...s.internals.handleBounds?.source ?? [], ...s.internals.handleBounds?.target ?? []], l = (n ? a?.find((c) => c.id === n) : a?.[0]) ?? null;
  return l && i ? { ...l, ...pt(s, l, l.position, !0) } : l;
}
function qs(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Bf(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const Zs = () => !0;
function Ff(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: i, isTarget: s, domNode: a, nodeLookup: l, lib: c, autoPanOnConnect: u, flowId: f, panBy: d, cancelConnection: m, onConnectStart: p, onConnect: y, onConnectEnd: w, isValidConnection: x = Zs, onReconnectEnd: S, updateConnection: g, getTransform: v, getFromHandle: _, autoPanSpeed: E, dragThreshold: M = 1, handleDomNode: D }) {
  const P = $s(e.target);
  let F = 0, C;
  const { x: R, y: H } = Oe(e), b = qs(i, D), k = a?.getBoundingClientRect();
  let N = !1;
  if (!k || !b)
    return;
  const A = Xs(r, b, o, l, t);
  if (!A)
    return;
  let T = Oe(e, k), j = !1, B = null, V = !1, O = null;
  function U() {
    if (!u || !k)
      return;
    const [te, G] = fr(T, k, E);
    d({ x: te, y: G }), F = requestAnimationFrame(U);
  }
  const X = {
    ...A,
    nodeId: r,
    type: b,
    position: A.position
  }, ee = l.get(r);
  let K = {
    inProgress: !0,
    isValid: null,
    from: pt(ee, X, J.Left, !0),
    fromHandle: X,
    fromPosition: X.position,
    fromNode: ee,
    to: T,
    toHandle: null,
    toPosition: Ur[X.position],
    toNode: null,
    pointer: T
  };
  function $() {
    N = !0, g(K), p?.(e, { nodeId: r, handleId: o, handleType: b });
  }
  M === 0 && $();
  function Y(te) {
    if (!N) {
      const { x: fe, y: ye } = Oe(te), Ie = fe - R, Ne = ye - H;
      if (!(Ie * Ie + Ne * Ne > M * M))
        return;
      $();
    }
    if (!_() || !X) {
      ie(te);
      return;
    }
    const G = v();
    T = Oe(te, k), C = Of(Pt(T, G, !1, [1, 1]), n, l, X), j || (U(), j = !0);
    const q = Us(te, {
      handle: C,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: s ? "target" : "source",
      isValidConnection: x,
      doc: P,
      lib: c,
      flowId: f,
      nodeLookup: l
    });
    O = q.handleDomNode, B = q.connection, V = Bf(!!C, q.isValid);
    const se = l.get(r), L = se ? pt(se, X, J.Left, !0) : K.from, Q = {
      ...K,
      from: L,
      isValid: V,
      to: q.toHandle && V ? Ct({ x: q.toHandle.x, y: q.toHandle.y }, G) : T,
      toHandle: q.toHandle,
      toPosition: V && q.toHandle ? q.toHandle.position : Ur[X.position],
      toNode: q.toHandle ? l.get(q.toHandle.nodeId) : null,
      pointer: T
    };
    g(Q), K = Q;
  }
  function ie(te) {
    if (!("touches" in te && te.touches.length > 0)) {
      if (N) {
        (C || O) && B && V && y?.(B);
        const { inProgress: G, ...q } = K, se = {
          ...q,
          toPosition: K.toHandle ? K.toPosition : null
        };
        w?.(te, se), i && S?.(te, se);
      }
      m(), cancelAnimationFrame(F), j = !1, V = !1, B = null, O = null, P.removeEventListener("mousemove", Y), P.removeEventListener("mouseup", ie), P.removeEventListener("touchmove", Y), P.removeEventListener("touchend", ie);
    }
  }
  P.addEventListener("mousemove", Y), P.addEventListener("mouseup", ie), P.addEventListener("touchmove", Y), P.addEventListener("touchend", ie);
}
function Us(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: i, doc: s, lib: a, flowId: l, isValidConnection: c = Zs, nodeLookup: u }) {
  const f = i === "target", d = t ? s.querySelector(`.${a}-flow__handle[data-id="${l}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: m, y: p } = Oe(e), y = s.elementFromPoint(m, p), w = y?.classList.contains(`${a}-flow__handle`) ? y : d, x = {
    handleDomNode: w,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (w) {
    const S = qs(void 0, w), g = w.getAttribute("data-nodeid"), v = w.getAttribute("data-handleid"), _ = w.classList.contains("connectable"), E = w.classList.contains("connectableend");
    if (!g || !S)
      return x;
    const M = {
      source: f ? g : o,
      sourceHandle: f ? v : r,
      target: f ? o : g,
      targetHandle: f ? r : v
    };
    x.connection = M;
    const P = _ && E && (n === Et.Strict ? f && S === "source" || !f && S === "target" : g !== o || v !== r);
    x.isValid = P && c(M), x.toHandle = Xs(g, S, v, u, n, !0);
  }
  return x;
}
const Go = {
  onPointerDown: Ff,
  isValid: Us
};
function Wf({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = De(e);
  function i({ translateExtent: a, width: l, height: c, zoomStep: u = 1, pannable: f = !0, zoomable: d = !0, inversePan: m = !1 }) {
    const p = (g) => {
      if (g.sourceEvent.type !== "wheel" || !t)
        return;
      const v = n(), _ = g.sourceEvent.ctrlKey && Kt() ? 10 : 1, E = -g.sourceEvent.deltaY * (g.sourceEvent.deltaMode === 1 ? 0.05 : g.sourceEvent.deltaMode ? 1 : 2e-3) * u, M = v[2] * Math.pow(2, E * _);
      t.scaleTo(M);
    };
    let y = [0, 0];
    const w = (g) => {
      (g.sourceEvent.type === "mousedown" || g.sourceEvent.type === "touchstart") && (y = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ]);
    }, x = (g) => {
      const v = n();
      if (g.sourceEvent.type !== "mousemove" && g.sourceEvent.type !== "touchmove" || !t)
        return;
      const _ = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ], E = [_[0] - y[0], _[1] - y[1]];
      y = _;
      const M = o() * Math.max(v[2], Math.log(v[2])) * (m ? -1 : 1), D = {
        x: v[0] - E[0] * M,
        y: v[1] - E[1] * M
      }, P = [
        [0, 0],
        [l, c]
      ];
      t.setViewportConstrained({
        x: D.x,
        y: D.y,
        zoom: v[2]
      }, P, a);
    }, S = Ss().on("start", w).on("zoom", f ? x : null).on("zoom.wheel", d ? p : null);
    r.call(S, {});
  }
  function s() {
    r.on("zoom", null);
  }
  return {
    update: i,
    destroy: s,
    pointer: Le
  };
}
const Kn = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), Io = ({ x: e, y: t, zoom: n }) => qn.translate(e, t).scale(n), yt = (e, t) => e.target.closest(`.${t}`), Ks = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Yf = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, Mo = (e, t = 0, n = Yf, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, Gs = (e) => {
  const t = e.ctrlKey && Kt() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Xf({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: i, zoomOnPinch: s, onPanZoomStart: a, onPanZoom: l, onPanZoomEnd: c }) {
  return (u) => {
    if (yt(u, t))
      return u.ctrlKey && u.preventDefault(), !1;
    u.preventDefault(), u.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (u.ctrlKey && s) {
      const w = Le(u), x = Gs(u), S = f * Math.pow(2, x);
      o.scaleTo(n, S, w, u);
      return;
    }
    const d = u.deltaMode === 1 ? 20 : 1;
    let m = r === lt.Vertical ? 0 : u.deltaX * d, p = r === lt.Horizontal ? 0 : u.deltaY * d;
    !Kt() && u.shiftKey && r !== lt.Vertical && (m = u.deltaY * d, p = 0), o.translateBy(
      n,
      -(m / f) * i,
      -(p / f) * i,
      // @ts-ignore
      { internal: !0 }
    );
    const y = Kn(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (l?.(u, y), e.panScrollTimeout = setTimeout(() => {
      c?.(u, y), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, a?.(u, y));
  };
}
function qf({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const i = o.type === "wheel", s = !t && i && !o.ctrlKey, a = yt(o, e);
    if (o.ctrlKey && i && a && o.preventDefault(), s || a)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function Zf({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = Kn(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function Uf({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (i) => {
    e.usedRightMouseButton = !!(n && Ks(t, e.mouseButton ?? 0)), i.sourceEvent?.sync || o([i.transform.x, i.transform.y, i.transform.k]), r && !i.sourceEvent?.internal && r?.(i.sourceEvent, Kn(i.transform));
  };
}
function Kf({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: i }) {
  return (s) => {
    if (!s.sourceEvent?.internal && (e.isZoomingOrPanning = !1, i && Ks(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && s.sourceEvent && i(s.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
      const a = Kn(s.transform);
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
function Gf({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: i, userSelectionActive: s, noWheelClassName: a, noPanClassName: l, lib: c, connectionInProgress: u }) {
  return (f) => {
    const d = e || t, m = n && f.ctrlKey, p = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (yt(f, `${c}-flow__node`) || yt(f, `${c}-flow__edge`)))
      return !0;
    if (!o && !d && !r && !i && !n || s || u && !p || yt(f, a) && p || yt(f, l) && (!p || r && p && !e) || !n && f.ctrlKey && p)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!d && !r && !m && p || !o && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(o) && !o.includes(f.button) && f.type === "mousedown")
      return !1;
    const y = Array.isArray(o) && o.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || p) && y;
  };
}
function Qf({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: i, onPanZoomStart: s, onPanZoomEnd: a, onDraggingChange: l }) {
  const c = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, u = e.getBoundingClientRect(), f = Ss().scaleExtent([t, n]).translateExtent(o), d = De(e).call(f);
  S({
    x: r.x,
    y: r.y,
    zoom: _t(r.zoom, t, n)
  }, [
    [0, 0],
    [u.width, u.height]
  ], o);
  const m = d.on("wheel.zoom"), p = d.on("dblclick.zoom");
  f.wheelDelta(Gs);
  async function y(C, R) {
    return d ? new Promise((H) => {
      f?.interpolate(R?.interpolate === "linear" ? Ot : Sn).transform(Mo(d, R?.duration, R?.ease, () => H(!0)), C);
    }) : !1;
  }
  function w({ noWheelClassName: C, noPanClassName: R, onPaneContextMenu: H, userSelectionActive: b, panOnScroll: k, panOnDrag: N, panOnScrollMode: A, panOnScrollSpeed: T, preventScrolling: j, zoomOnPinch: B, zoomOnScroll: V, zoomOnDoubleClick: O, zoomActivationKeyPressed: U, lib: X, onTransformChange: ee, connectionInProgress: oe, paneClickDistance: K, selectionOnDrag: $ }) {
    b && !c.isZoomingOrPanning && x();
    const Y = k && !U && !b;
    f.clickDistance($ ? 1 / 0 : !Ve(K) || K < 0 ? 0 : K);
    const ie = Y ? Xf({
      zoomPanValues: c,
      noWheelClassName: C,
      d3Selection: d,
      d3Zoom: f,
      panOnScrollMode: A,
      panOnScrollSpeed: T,
      zoomOnPinch: B,
      onPanZoomStart: s,
      onPanZoom: i,
      onPanZoomEnd: a
    }) : qf({
      noWheelClassName: C,
      preventScrolling: j,
      d3ZoomHandler: m
    });
    d.on("wheel.zoom", ie, { passive: !1 });
    const te = Zf({
      zoomPanValues: c,
      onDraggingChange: l,
      onPanZoomStart: s
    });
    f.on("start", te);
    const G = Uf({
      zoomPanValues: c,
      panOnDrag: N,
      onPaneContextMenu: !!H,
      onPanZoom: i,
      onTransformChange: ee
    });
    f.on("zoom", G);
    const q = Kf({
      zoomPanValues: c,
      panOnDrag: N,
      panOnScroll: k,
      onPaneContextMenu: H,
      onPanZoomEnd: a,
      onDraggingChange: l
    });
    f.on("end", q);
    const se = Gf({
      zoomActivationKeyPressed: U,
      panOnDrag: N,
      zoomOnScroll: V,
      panOnScroll: k,
      zoomOnDoubleClick: O,
      zoomOnPinch: B,
      userSelectionActive: b,
      noPanClassName: R,
      noWheelClassName: C,
      lib: X,
      connectionInProgress: oe
    });
    f.filter(se), O ? d.on("dblclick.zoom", p) : d.on("dblclick.zoom", null);
  }
  function x() {
    f.on("zoom", null);
  }
  async function S(C, R, H) {
    const b = Io(C), k = f?.constrain()(b, R, H);
    return k && await y(k), k;
  }
  async function g(C, R) {
    const H = Io(C);
    return await y(H, R), H;
  }
  function v(C) {
    if (d) {
      const R = Io(C), H = d.property("__zoom");
      (H.k !== C.zoom || H.x !== C.x || H.y !== C.y) && f?.transform(d, R, null, { sync: !0 });
    }
  }
  function _() {
    const C = d ? bs(d.node()) : { x: 0, y: 0, k: 1 };
    return { x: C.x, y: C.y, zoom: C.k };
  }
  async function E(C, R) {
    return d ? new Promise((H) => {
      f?.interpolate(R?.interpolate === "linear" ? Ot : Sn).scaleTo(Mo(d, R?.duration, R?.ease, () => H(!0)), C);
    }) : !1;
  }
  async function M(C, R) {
    return d ? new Promise((H) => {
      f?.interpolate(R?.interpolate === "linear" ? Ot : Sn).scaleBy(Mo(d, R?.duration, R?.ease, () => H(!0)), C);
    }) : !1;
  }
  function D(C) {
    f?.scaleExtent(C);
  }
  function P(C) {
    f?.translateExtent(C);
  }
  function F(C) {
    const R = !Ve(C) || C < 0 ? 0 : C;
    f?.clickDistance(R);
  }
  return {
    update: w,
    destroy: x,
    setViewport: g,
    setViewportConstrained: S,
    getViewport: _,
    scaleTo: E,
    scaleBy: M,
    setScaleExtent: D,
    setTranslateExtent: P,
    syncViewport: v,
    setClickDistance: F
  };
}
var kt;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(kt || (kt = {}));
function Jf({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: i }) {
  const s = e - t, a = n - o, l = [s > 0 ? 1 : s < 0 ? -1 : 0, a > 0 ? 1 : a < 0 ? -1 : 0];
  return s && r && (l[0] = l[0] * -1), a && i && (l[1] = l[1] * -1), l;
}
function ci(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: r
  };
}
function tt(e, t) {
  return Math.max(0, t - e);
}
function nt(e, t) {
  return Math.max(0, e - t);
}
function xn(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function li(e, t) {
  return e ? !t : t;
}
function eh(e, t, n, o, r, i, s, a) {
  let { affectsX: l, affectsY: c } = t;
  const { isHorizontal: u, isVertical: f } = t, d = u && f, { xSnapped: m, ySnapped: p } = n, { minWidth: y, maxWidth: w, minHeight: x, maxHeight: S } = o, { x: g, y: v, width: _, height: E, aspectRatio: M } = e;
  let D = Math.floor(u ? m - e.pointerX : 0), P = Math.floor(f ? p - e.pointerY : 0);
  const F = _ + (l ? -D : D), C = E + (c ? -P : P), R = -i[0] * _, H = -i[1] * E;
  let b = xn(F, y, w), k = xn(C, x, S);
  if (s) {
    let T = 0, j = 0;
    l && D < 0 ? T = tt(g + D + R, s[0][0]) : !l && D > 0 && (T = nt(g + F + R, s[1][0])), c && P < 0 ? j = tt(v + P + H, s[0][1]) : !c && P > 0 && (j = nt(v + C + H, s[1][1])), b = Math.max(b, T), k = Math.max(k, j);
  }
  if (a) {
    let T = 0, j = 0;
    l && D > 0 ? T = nt(g + D, a[0][0]) : !l && D < 0 && (T = tt(g + F, a[1][0])), c && P > 0 ? j = nt(v + P, a[0][1]) : !c && P < 0 && (j = tt(v + C, a[1][1])), b = Math.max(b, T), k = Math.max(k, j);
  }
  if (r) {
    if (u) {
      const T = xn(F / M, x, S) * M;
      if (b = Math.max(b, T), s) {
        let j = 0;
        !l && !c || l && !c && d ? j = nt(v + H + F / M, s[1][1]) * M : j = tt(v + H + (l ? D : -D) / M, s[0][1]) * M, b = Math.max(b, j);
      }
      if (a) {
        let j = 0;
        !l && !c || l && !c && d ? j = tt(v + F / M, a[1][1]) * M : j = nt(v + (l ? D : -D) / M, a[0][1]) * M, b = Math.max(b, j);
      }
    }
    if (f) {
      const T = xn(C * M, y, w) / M;
      if (k = Math.max(k, T), s) {
        let j = 0;
        !l && !c || c && !l && d ? j = nt(g + C * M + R, s[1][0]) / M : j = tt(g + (c ? P : -P) * M + R, s[0][0]) / M, k = Math.max(k, j);
      }
      if (a) {
        let j = 0;
        !l && !c || c && !l && d ? j = tt(g + C * M, a[1][0]) / M : j = nt(g + (c ? P : -P) * M, a[0][0]) / M, k = Math.max(k, j);
      }
    }
  }
  P = P + (P < 0 ? k : -k), D = D + (D < 0 ? b : -b), r && (d ? F > C * M ? P = (li(l, c) ? -D : D) / M : D = (li(l, c) ? -P : P) * M : u ? (P = D / M, c = l) : (D = P * M, l = c));
  const N = l ? g + D : g, A = c ? v + P : v;
  return {
    width: _ + (l ? -D : D),
    height: E + (c ? -P : P),
    x: i[0] * D * (l ? -1 : 1) + N,
    y: i[1] * P * (c ? -1 : 1) + A
  };
}
const Qs = { width: 0, height: 0, x: 0, y: 0 }, th = {
  ...Qs,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function nh(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, i = e.measured.width ?? 0, s = e.measured.height ?? 0, a = n[0] * i, l = n[1] * s;
  return [
    [o - a, r - l],
    [o + i - a, r + s - l]
  ];
}
function oh({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const i = De(e);
  let s = {
    controlDirection: ci("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function a({ controlPosition: c, boundaries: u, keepAspectRatio: f, resizeDirection: d, onResizeStart: m, onResize: p, onResizeEnd: y, shouldResize: w }) {
    let x = { ...Qs }, S = { ...th };
    s = {
      boundaries: u,
      resizeDirection: d,
      keepAspectRatio: f,
      controlDirection: ci(c)
    };
    let g, v = null, _ = [], E, M, D, P = !1;
    const F = as().on("start", (C) => {
      const { nodeLookup: R, transform: H, snapGrid: b, snapToGrid: k, nodeOrigin: N, paneDomNode: A } = n();
      if (g = R.get(t), !g)
        return;
      v = A?.getBoundingClientRect() ?? null;
      const { xSnapped: T, ySnapped: j } = Bt(C.sourceEvent, {
        transform: H,
        snapGrid: b,
        snapToGrid: k,
        containerBounds: v
      });
      x = {
        width: g.measured.width ?? 0,
        height: g.measured.height ?? 0,
        x: g.position.x ?? 0,
        y: g.position.y ?? 0
      }, S = {
        ...x,
        pointerX: T,
        pointerY: j,
        aspectRatio: x.width / x.height
      }, E = void 0, M = ht(g.extent) ? g.extent : void 0, g.parentId && (g.extent === "parent" || g.expandParent) && (E = R.get(g.parentId)), E && g.extent === "parent" && (M = [
        [0, 0],
        [E.measured.width, E.measured.height]
      ]), _ = [], D = void 0;
      for (const [B, V] of R)
        if (V.parentId === t && (_.push({
          id: B,
          position: { ...V.position },
          extent: V.extent
        }), V.extent === "parent" || V.expandParent)) {
          const O = nh(V, g, V.origin ?? N);
          D ? D = [
            [Math.min(O[0][0], D[0][0]), Math.min(O[0][1], D[0][1])],
            [Math.max(O[1][0], D[1][0]), Math.max(O[1][1], D[1][1])]
          ] : D = O;
        }
      m?.(C, { ...x });
    }).on("drag", (C) => {
      const { transform: R, snapGrid: H, snapToGrid: b, nodeOrigin: k } = n(), N = Bt(C.sourceEvent, {
        transform: R,
        snapGrid: H,
        snapToGrid: b,
        containerBounds: v
      }), A = [];
      if (!g)
        return;
      const { x: T, y: j, width: B, height: V } = x, O = {}, U = g.origin ?? k, { width: X, height: ee, x: oe, y: K } = eh(S, s.controlDirection, N, s.boundaries, s.keepAspectRatio, U, M, D), $ = X !== B, Y = ee !== V, ie = oe !== T && $, te = K !== j && Y;
      if (!ie && !te && !$ && !Y)
        return;
      if ((ie || te || U[0] === 1 || U[1] === 1) && (O.x = ie ? oe : x.x, O.y = te ? K : x.y, x.x = O.x, x.y = O.y, _.length > 0)) {
        const L = oe - T, Q = K - j;
        for (const fe of _)
          fe.position = {
            x: fe.position.x - L + U[0] * (X - B),
            y: fe.position.y - Q + U[1] * (ee - V)
          }, A.push(fe);
      }
      if (($ || Y) && (O.width = $ && (!s.resizeDirection || s.resizeDirection === "horizontal") ? X : x.width, O.height = Y && (!s.resizeDirection || s.resizeDirection === "vertical") ? ee : x.height, x.width = O.width, x.height = O.height), E && g.expandParent) {
        const L = U[0] * (O.width ?? 0);
        O.x && O.x < L && (x.x = L, S.x = S.x - (O.x - L));
        const Q = U[1] * (O.height ?? 0);
        O.y && O.y < Q && (x.y = Q, S.y = S.y - (O.y - Q));
      }
      const G = Jf({
        width: x.width,
        prevWidth: B,
        height: x.height,
        prevHeight: V,
        affectsX: s.controlDirection.affectsX,
        affectsY: s.controlDirection.affectsY
      }), q = { ...x, direction: G };
      w?.(C, q) !== !1 && (P = !0, p?.(C, q), o(O, A));
    }).on("end", (C) => {
      P && (y?.(C, { ...x }), r?.({ ...x }), P = !1);
    });
    i.call(F);
  }
  function l() {
    i.on(".drag", null);
  }
  return {
    update: a,
    destroy: l
  };
}
var Ao = { exports: {} }, Do = {}, Po = { exports: {} }, jo = {};
var ui;
function rh() {
  if (ui) return jo;
  ui = 1;
  var e = it;
  function t(f, d) {
    return f === d && (f !== 0 || 1 / f === 1 / d) || f !== f && d !== d;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, i = e.useLayoutEffect, s = e.useDebugValue;
  function a(f, d) {
    var m = d(), p = o({ inst: { value: m, getSnapshot: d } }), y = p[0].inst, w = p[1];
    return i(
      function() {
        y.value = m, y.getSnapshot = d, l(y) && w({ inst: y });
      },
      [f, m, d]
    ), r(
      function() {
        return l(y) && w({ inst: y }), f(function() {
          l(y) && w({ inst: y });
        });
      },
      [f]
    ), s(m), m;
  }
  function l(f) {
    var d = f.getSnapshot;
    f = f.value;
    try {
      var m = d();
      return !n(f, m);
    } catch {
      return !0;
    }
  }
  function c(f, d) {
    return d();
  }
  var u = typeof window > "u" || typeof window.document > "u" || typeof window.document.createElement > "u" ? c : a;
  return jo.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : u, jo;
}
var di;
function ih() {
  return di || (di = 1, Po.exports = rh()), Po.exports;
}
var fi;
function sh() {
  if (fi) return Do;
  fi = 1;
  var e = it, t = ih();
  function n(c, u) {
    return c === u && (c !== 0 || 1 / c === 1 / u) || c !== c && u !== u;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, i = e.useRef, s = e.useEffect, a = e.useMemo, l = e.useDebugValue;
  return Do.useSyncExternalStoreWithSelector = function(c, u, f, d, m) {
    var p = i(null);
    if (p.current === null) {
      var y = { hasValue: !1, value: null };
      p.current = y;
    } else y = p.current;
    p = a(
      function() {
        function x(E) {
          if (!S) {
            if (S = !0, g = E, E = d(E), m !== void 0 && y.hasValue) {
              var M = y.value;
              if (m(M, E))
                return v = M;
            }
            return v = E;
          }
          if (M = v, o(g, E)) return M;
          var D = d(E);
          return m !== void 0 && m(M, D) ? (g = E, M) : (g = E, v = D);
        }
        var S = !1, g, v, _ = f === void 0 ? null : f;
        return [
          function() {
            return x(u());
          },
          _ === null ? void 0 : function() {
            return x(_());
          }
        ];
      },
      [u, f, d, m]
    );
    var w = r(c, p[0], p[1]);
    return s(
      function() {
        y.hasValue = !0, y.value = w;
      },
      [w]
    ), l(w), w;
  }, Do;
}
var hi;
function ah() {
  return hi || (hi = 1, Ao.exports = sh()), Ao.exports;
}
var ch = ah();
const lh = /* @__PURE__ */ _c(ch), uh = {}, pi = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (u, f) => {
    const d = typeof u == "function" ? u(t) : u;
    if (!Object.is(d, t)) {
      const m = t;
      t = f ?? (typeof d != "object" || d === null) ? d : Object.assign({}, t, d), n.forEach((p) => p(t, m));
    }
  }, r = () => t, l = { setState: o, getState: r, getInitialState: () => c, subscribe: (u) => (n.add(u), () => n.delete(u)), destroy: () => {
    (uh ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, c = t = e(o, r, l);
  return l;
}, dh = (e) => e ? pi(e) : pi, { useDebugValue: fh } = it, { useSyncExternalStoreWithSelector: hh } = lh, ph = (e) => e;
function Js(e, t = ph, n) {
  const o = hh(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return fh(o), o;
}
const gi = (e, t) => {
  const n = dh(e), o = (r, i = t) => Js(n, r, i);
  return Object.assign(o, n), o;
}, gh = (e, t) => e ? gi(e, t) : gi;
function pe(e, t) {
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
var $o = { exports: {} }, _e = {};
var mi;
function mh() {
  if (mi) return _e;
  mi = 1;
  var e = it;
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
    var f = 3 < arguments.length && arguments[3] !== void 0 ? arguments[3] : null;
    return {
      $$typeof: r,
      key: f == null ? null : "" + f,
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
  return _e.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, _e.createPortal = function(l, c) {
    var u = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!c || c.nodeType !== 1 && c.nodeType !== 9 && c.nodeType !== 11)
      throw Error(t(299));
    return i(l, c, null, u);
  }, _e.flushSync = function(l) {
    var c = s.T, u = o.p;
    try {
      if (s.T = null, o.p = 2, l) return l();
    } finally {
      s.T = c, o.p = u, o.d.f();
    }
  }, _e.preconnect = function(l, c) {
    typeof l == "string" && (c ? (c = c.crossOrigin, c = typeof c == "string" ? c === "use-credentials" ? c : "" : void 0) : c = null, o.d.C(l, c));
  }, _e.prefetchDNS = function(l) {
    typeof l == "string" && o.d.D(l);
  }, _e.preinit = function(l, c) {
    if (typeof l == "string" && c && typeof c.as == "string") {
      var u = c.as, f = a(u, c.crossOrigin), d = typeof c.integrity == "string" ? c.integrity : void 0, m = typeof c.fetchPriority == "string" ? c.fetchPriority : void 0;
      u === "style" ? o.d.S(
        l,
        typeof c.precedence == "string" ? c.precedence : void 0,
        {
          crossOrigin: f,
          integrity: d,
          fetchPriority: m
        }
      ) : u === "script" && o.d.X(l, {
        crossOrigin: f,
        integrity: d,
        fetchPriority: m,
        nonce: typeof c.nonce == "string" ? c.nonce : void 0
      });
    }
  }, _e.preinitModule = function(l, c) {
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
  }, _e.preload = function(l, c) {
    if (typeof l == "string" && typeof c == "object" && c !== null && typeof c.as == "string") {
      var u = c.as, f = a(u, c.crossOrigin);
      o.d.L(l, u, {
        crossOrigin: f,
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
  }, _e.preloadModule = function(l, c) {
    if (typeof l == "string")
      if (c) {
        var u = a(c.as, c.crossOrigin);
        o.d.m(l, {
          as: typeof c.as == "string" && c.as !== "script" ? c.as : void 0,
          crossOrigin: u,
          integrity: typeof c.integrity == "string" ? c.integrity : void 0
        });
      } else o.d.m(l);
  }, _e.requestFormReset = function(l) {
    o.d.r(l);
  }, _e.unstable_batchedUpdates = function(l, c) {
    return l(c);
  }, _e.useFormState = function(l, c, u) {
    return s.H.useFormState(l, c, u);
  }, _e.useFormStatus = function() {
    return s.H.useHostTransitionStatus();
  }, _e.version = "19.2.7", _e;
}
var yi;
function yh() {
  if (yi) return $o.exports;
  yi = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), $o.exports = mh(), $o.exports;
}
var xh = yh();
const Gn = nr(null), wh = Gn.Provider, ea = je.error001("react");
function le(e, t) {
  const n = Jt(Gn);
  if (n === null)
    throw new Error(ea);
  return Js(n, e, t);
}
function ge() {
  const e = Jt(Gn);
  if (e === null)
    throw new Error(ea);
  return me(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const xi = { display: "none" }, vh = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, ta = "react-flow__node-desc", na = "react-flow__edge-desc", bh = "react-flow__aria-live", Sh = (e) => e.ariaLiveMessage, Eh = (e) => e.ariaLabelConfig;
function _h({ rfId: e }) {
  const t = le(Sh);
  return h.jsx("div", { id: `${bh}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: vh, children: t });
}
function Nh({ rfId: e, disableKeyboardA11y: t }) {
  const n = le(Eh);
  return h.jsxs(h.Fragment, { children: [h.jsx("div", { id: `${ta}-${e}`, style: xi, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), h.jsx("div", { id: `${na}-${e}`, style: xi, children: n["edge.a11yDescription.default"] }), !t && h.jsx(_h, { rfId: e })] });
}
const Qn = Bn(({ position: e = "top-left", children: t, className: n, style: o, ...r }, i) => {
  const s = `${e}`.split("-");
  return h.jsx("div", { className: ve(["react-flow__panel", n, ...s]), style: o, ref: i, ...r, children: t });
});
Qn.displayName = "Panel";
function Ch({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : h.jsx(Qn, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: h.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const kh = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, wn = (e) => e.id;
function Ih(e, t) {
  return pe(e.selectedNodes.map(wn), t.selectedNodes.map(wn)) && pe(e.selectedEdges.map(wn), t.selectedEdges.map(wn));
}
function Mh({ onSelectionChange: e }) {
  const t = ge(), { selectedNodes: n, selectedEdges: o } = le(kh, Ih);
  return ce(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((i) => i(r));
  }, [n, o, e]), null;
}
const Ah = (e) => !!e.onSelectionChangeHandlers;
function Dh({ onSelectionChange: e }) {
  const t = le(Ah);
  return e || t ? h.jsx(Mh, { onSelectionChange: e }) : null;
}
const oa = [0, 0], Ph = { x: 0, y: 0, zoom: 1 }, jh = [
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
], wi = [...jh, "rfId"], $h = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), vi = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: qt,
  nodeOrigin: oa,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function Th(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: i, setNodeExtent: s, reset: a, setDefaultNodesAndEdges: l } = le($h, pe), c = ge();
  ce(() => (l(e.defaultNodes, e.defaultEdges), () => {
    u.current = vi, a();
  }), []);
  const u = ae(vi);
  return ce(
    () => {
      for (const f of wi) {
        const d = e[f], m = u.current[f];
        d !== m && (typeof e[f] > "u" || (f === "nodes" ? t(d) : f === "edges" ? n(d) : f === "minZoom" ? o(d) : f === "maxZoom" ? r(d) : f === "translateExtent" ? i(d) : f === "nodeExtent" ? s(d) : f === "ariaLabelConfig" ? c.setState({ ariaLabelConfig: mf(d) }) : f === "fitView" ? c.setState({ fitViewQueued: d }) : f === "fitViewOptions" ? c.setState({ fitViewOptions: d }) : c.setState({ [f]: d })));
      }
      u.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    wi.map((f) => e[f])
  ), null;
}
function bi() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function Rh(e) {
  const [t, n] = re(e === "system" ? null : e);
  return ce(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = bi(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : bi()?.matches ? "dark" : "light";
}
const Si = typeof document < "u" ? document : null;
function Gt(e = null, t = { target: Si, actInsideInputWithModifier: !0 }) {
  const [n, o] = re(!1), r = ae(!1), i = ae(/* @__PURE__ */ new Set([])), [s, a] = me(() => {
    if (e !== null) {
      const c = (Array.isArray(e) ? e : [e]).filter((f) => typeof f == "string").map((f) => f.replace("+", `
`).replace(`

`, `
+`).split(`
`)), u = c.reduce((f, d) => f.concat(...d), []);
      return [c, u];
    }
    return [[], []];
  }, [e]);
  return ce(() => {
    const l = t?.target ?? Si, c = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const u = (m) => {
        if (r.current = m.ctrlKey || m.metaKey || m.shiftKey || m.altKey, (!r.current || r.current && !c) && Ts(m))
          return !1;
        const y = _i(m.code, a);
        if (i.current.add(m[y]), Ei(s, i.current, !1)) {
          const w = m.composedPath?.()?.[0] || m.target, x = w?.nodeName === "BUTTON" || w?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !x) && m.preventDefault(), o(!0);
        }
      }, f = (m) => {
        const p = _i(m.code, a);
        Ei(s, i.current, !0) ? (o(!1), i.current.clear()) : i.current.delete(m[p]), m.key === "Meta" && i.current.clear(), r.current = !1;
      }, d = () => {
        i.current.clear(), o(!1);
      };
      return l?.addEventListener("keydown", u), l?.addEventListener("keyup", f), window.addEventListener("blur", d), window.addEventListener("contextmenu", d), () => {
        l?.removeEventListener("keydown", u), l?.removeEventListener("keyup", f), window.removeEventListener("blur", d), window.removeEventListener("contextmenu", d);
      };
    }
  }, [e, o]), n;
}
function Ei(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function _i(e, t) {
  return t.includes(e) ? "code" : "key";
}
const zh = () => {
  const e = ge();
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
      const { width: o, height: r, minZoom: i, maxZoom: s, panZoom: a } = e.getState(), l = hr(t, o, r, i, s, n?.padding ?? 0.1);
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
      }, u = n.snapGrid ?? r, f = n.snapToGrid ?? i;
      return Pt(c, o, f, u);
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
function ra(e, t) {
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
      Lh(l, a);
    n.push(a);
  }
  return r.length && r.forEach((i) => {
    i.index !== void 0 ? n.splice(i.index, 0, { ...i.item }) : n.push({ ...i.item });
  }), n;
}
function Lh(e, t) {
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
function ia(e, t) {
  return ra(e, t);
}
function sa(e, t) {
  return ra(e, t);
}
function st(e, t) {
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
    !(i.selected === void 0 && !s) && i.selected !== s && (n && (i.selected = s), o.push(st(i.id, s)));
  }
  return o;
}
function Ni({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((r) => [r.id, r]));
  for (const [r, i] of e.entries()) {
    const s = t.get(i.id), a = s?.internals?.userNode ?? s;
    a !== void 0 && a !== i && n.push({ id: i.id, item: i, type: "replace" }), a === void 0 && n.push({ item: i, type: "add", index: r });
  }
  for (const [r] of t)
    o.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function Ci(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const aa = Ds();
function ca(e, t, n = {}) {
  return bf(e, t, {
    ...n,
    onError: n.onError ?? aa
  });
}
function Hh(e, t, n, o = { shouldReplaceId: !0 }) {
  return Sf(e, t, n, {
    ...o,
    onError: o.onError ?? aa
  });
}
const ki = (e) => af(e), Vh = (e) => ks(e);
function la(e) {
  return Bn(e);
}
const Oh = typeof window < "u" ? Ec : ce;
function Ii(e) {
  const [t, n] = re(BigInt(0)), [o] = re(() => Bh(() => n((r) => r + BigInt(1))));
  return Oh(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function Bh(e) {
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
const ua = nr(null);
function Fh({ children: e }) {
  const t = ge(), n = he((a) => {
    const { nodes: l = [], setNodes: c, hasDefaultNodes: u, onNodesChange: f, nodeLookup: d, fitViewQueued: m, onNodesChangeMiddlewareMap: p } = t.getState();
    let y = l;
    for (const x of a)
      y = typeof x == "function" ? x(y) : x;
    let w = Ni({
      items: y,
      lookup: d
    });
    for (const x of p.values())
      w = x(w);
    u && c(y), w.length > 0 ? f?.(w) : m && window.requestAnimationFrame(() => {
      const { fitViewQueued: x, nodes: S, setNodes: g } = t.getState();
      x && g(S);
    });
  }, []), o = Ii(n), r = he((a) => {
    const { edges: l = [], setEdges: c, hasDefaultEdges: u, onEdgesChange: f, edgeLookup: d } = t.getState();
    let m = l;
    for (const p of a)
      m = typeof p == "function" ? p(m) : p;
    u ? c(m) : f && f(Ni({
      items: m,
      lookup: d
    }));
  }, []), i = Ii(r), s = me(() => ({ nodeQueue: o, edgeQueue: i }), []);
  return h.jsx(ua.Provider, { value: s, children: e });
}
function Wh() {
  const e = Jt(ua);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Yh = (e) => !!e.panZoom;
function vr() {
  const e = zh(), t = ge(), n = Wh(), o = le(Yh), r = me(() => {
    const i = (f) => t.getState().nodeLookup.get(f), s = (f) => {
      n.nodeQueue.push(f);
    }, a = (f) => {
      n.edgeQueue.push(f);
    }, l = (f) => {
      const { nodeLookup: d, nodeOrigin: m } = t.getState(), p = ki(f) ? f : d.get(f.id), y = p.parentId ? js(p.position, p.measured, p.parentId, d, m) : p.position, w = {
        ...p,
        position: y,
        width: p.measured?.width ?? p.width,
        height: p.measured?.height ?? p.height
      };
      return Nt(w);
    }, c = (f, d, m = { replace: !1 }) => {
      s((p) => p.map((y) => {
        if (y.id === f) {
          const w = typeof d == "function" ? d(y) : d;
          return m.replace && ki(w) ? w : { ...y, ...w };
        }
        return y;
      }));
    }, u = (f, d, m = { replace: !1 }) => {
      a((p) => p.map((y) => {
        if (y.id === f) {
          const w = typeof d == "function" ? d(y) : d;
          return m.replace && Vh(w) ? w : { ...y, ...w };
        }
        return y;
      }));
    };
    return {
      getNodes: () => t.getState().nodes.map((f) => ({ ...f })),
      getNode: (f) => i(f)?.internals.userNode,
      getInternalNode: i,
      getEdges: () => {
        const { edges: f = [] } = t.getState();
        return f.map((d) => ({ ...d }));
      },
      getEdge: (f) => t.getState().edgeLookup.get(f),
      setNodes: s,
      setEdges: a,
      addNodes: (f) => {
        const d = Array.isArray(f) ? f : [f];
        n.nodeQueue.push((m) => [...m, ...d]);
      },
      addEdges: (f) => {
        const d = Array.isArray(f) ? f : [f];
        n.edgeQueue.push((m) => [...m, ...d]);
      },
      toObject: () => {
        const { nodes: f = [], edges: d = [], transform: m } = t.getState(), [p, y, w] = m;
        return {
          nodes: f.map((x) => ({ ...x })),
          edges: d.map((x) => ({ ...x })),
          viewport: {
            x: p,
            y,
            zoom: w
          }
        };
      },
      deleteElements: async ({ nodes: f = [], edges: d = [] }) => {
        const { nodes: m, edges: p, onNodesDelete: y, onEdgesDelete: w, triggerNodeChanges: x, triggerEdgeChanges: S, onDelete: g, onBeforeDelete: v } = t.getState(), { nodes: _, edges: E } = await ff({
          nodesToRemove: f,
          edgesToRemove: d,
          nodes: m,
          edges: p,
          onBeforeDelete: v
        }), M = E.length > 0, D = _.length > 0;
        if (M) {
          const P = E.map(Ci);
          w?.(E), S(P);
        }
        if (D) {
          const P = _.map(Ci);
          y?.(_), x(P);
        }
        return (D || M) && g?.({ nodes: _, edges: E }), { deletedNodes: _, deletedEdges: E };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, d = !0, m) => {
        const p = Gr(f), y = p ? f : l(f), w = m !== void 0;
        return y ? (m || t.getState().nodes).filter((x) => {
          const S = t.getState().nodeLookup.get(x.id);
          if (S && !p && (x.id === f.id || !S.internals.positionAbsolute))
            return !1;
          const g = Nt(w ? x : S), v = Ut(g, y);
          return d && v > 0 || v >= g.width * g.height || v >= y.width * y.height;
        }) : [];
      },
      isNodeIntersecting: (f, d, m = !0) => {
        const y = Gr(f) ? f : l(f);
        if (!y)
          return !1;
        const w = Ut(y, d);
        return m && w > 0 || w >= d.width * d.height || w >= y.width * y.height;
      },
      updateNode: c,
      updateNodeData: (f, d, m = { replace: !1 }) => {
        c(f, (p) => {
          const y = typeof d == "function" ? d(p) : d;
          return m.replace ? { ...p, data: y } : { ...p, data: { ...p.data, ...y } };
        }, m);
      },
      updateEdge: u,
      updateEdgeData: (f, d, m = { replace: !1 }) => {
        u(f, (p) => {
          const y = typeof d == "function" ? d(p) : d;
          return m.replace ? { ...p, data: y } : { ...p, data: { ...p.data, ...y } };
        }, m);
      },
      getNodesBounds: (f) => {
        const { nodeLookup: d, nodeOrigin: m } = t.getState();
        return cf(f, { nodeLookup: d, nodeOrigin: m });
      },
      getHandleConnections: ({ type: f, id: d, nodeId: m }) => Array.from(t.getState().connectionLookup.get(`${m}-${f}${d ? `-${d}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: d, nodeId: m }) => Array.from(t.getState().connectionLookup.get(`${m}${f ? d ? `-${f}-${d}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const d = t.getState().fitViewResolver ?? gf();
        return t.setState({ fitViewQueued: !0, fitViewOptions: f, fitViewResolver: d }), n.nodeQueue.push((m) => [...m]), d.promise;
      }
    };
  }, []);
  return me(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const Mi = (e) => e.selected, Xh = typeof window < "u" ? window : void 0;
function qh({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = ge(), { deleteElements: o } = vr(), r = Gt(e, { actInsideInputWithModifier: !1 }), i = Gt(t, { target: Xh });
  ce(() => {
    if (r) {
      const { edges: s, nodes: a } = n.getState();
      o({ nodes: a.filter(Mi), edges: s.filter(Mi) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), ce(() => {
    n.setState({ multiSelectionActive: i });
  }, [i]);
}
function Zh(e) {
  const t = ge();
  ce(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = pr(e.current);
      (o.height === 0 || o.width === 0) && t.getState().onError?.("004", je.error004()), t.setState({ width: o.width || 500, height: o.height || 500 });
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
const Jn = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, Uh = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function Kh({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: i = lt.Free, zoomOnDoubleClick: s = !0, panOnDrag: a = !0, defaultViewport: l, translateExtent: c, minZoom: u, maxZoom: f, zoomActivationKeyCode: d, preventScrolling: m = !0, children: p, noWheelClassName: y, noPanClassName: w, onViewportChange: x, isControlledViewport: S, paneClickDistance: g, selectionOnDrag: v }) {
  const _ = ge(), E = ae(null), { userSelectionActive: M, lib: D, connectionInProgress: P } = le(Uh, pe), F = Gt(d), C = ae();
  Zh(E);
  const R = he((H) => {
    x?.({ x: H[0], y: H[1], zoom: H[2] }), S || _.setState({ transform: H });
  }, [x, S]);
  return ce(() => {
    if (E.current) {
      C.current = Qf({
        domNode: E.current,
        minZoom: u,
        maxZoom: f,
        translateExtent: c,
        viewport: l,
        onDraggingChange: (N) => _.setState((A) => A.paneDragging === N ? A : { paneDragging: N }),
        onPanZoomStart: (N, A) => {
          const { onViewportChangeStart: T, onMoveStart: j } = _.getState();
          j?.(N, A), T?.(A);
        },
        onPanZoom: (N, A) => {
          const { onViewportChange: T, onMove: j } = _.getState();
          j?.(N, A), T?.(A);
        },
        onPanZoomEnd: (N, A) => {
          const { onViewportChangeEnd: T, onMoveEnd: j } = _.getState();
          j?.(N, A), T?.(A);
        }
      });
      const { x: H, y: b, zoom: k } = C.current.getViewport();
      return _.setState({
        panZoom: C.current,
        transform: [H, b, k],
        domNode: E.current.closest(".react-flow")
      }), () => {
        C.current?.destroy();
      };
    }
  }, []), ce(() => {
    C.current?.update({
      onPaneContextMenu: e,
      zoomOnScroll: t,
      zoomOnPinch: n,
      panOnScroll: o,
      panOnScrollSpeed: r,
      panOnScrollMode: i,
      zoomOnDoubleClick: s,
      panOnDrag: a,
      zoomActivationKeyPressed: F,
      preventScrolling: m,
      noPanClassName: w,
      userSelectionActive: M,
      noWheelClassName: y,
      lib: D,
      onTransformChange: R,
      connectionInProgress: P,
      selectionOnDrag: v,
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
    a,
    F,
    m,
    w,
    M,
    y,
    D,
    R,
    P,
    v,
    g
  ]), h.jsx("div", { className: "react-flow__renderer", ref: E, style: Jn, children: p });
}
const Gh = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function Qh() {
  const { userSelectionActive: e, userSelectionRect: t } = le(Gh, pe);
  return e && t ? h.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const To = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, Jh = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function ep({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Zt.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: i, selectionOnDrag: s, onSelectionStart: a, onSelectionEnd: l, onPaneClick: c, onPaneContextMenu: u, onPaneScroll: f, onPaneMouseEnter: d, onPaneMouseMove: m, onPaneMouseLeave: p, children: y }) {
  const w = ae(0), x = ge(), { userSelectionActive: S, elementsSelectable: g, dragging: v, connectionInProgress: _, panBy: E, autoPanSpeed: M } = le(Jh, pe), D = g && (e || S), P = ae(null), F = ae(), C = ae(/* @__PURE__ */ new Set()), R = ae(/* @__PURE__ */ new Set()), H = ae(!1), b = ae({ x: 0, y: 0 }), k = ae(!1), N = ($) => {
    if (H.current || _) {
      H.current = !1;
      return;
    }
    c?.($), x.getState().resetSelectedElements(), x.setState({ nodesSelectionActive: !1 });
  }, A = ($) => {
    if (Array.isArray(o) && o?.includes(2)) {
      $.preventDefault();
      return;
    }
    u?.($);
  }, T = f ? ($) => f($) : void 0, j = ($) => {
    H.current && ($.stopPropagation(), H.current = !1);
  }, B = ($) => {
    const { domNode: Y, transform: ie } = x.getState();
    if (F.current = Y?.getBoundingClientRect(), !F.current)
      return;
    const te = $.target === P.current;
    if (!te && !!$.target.closest(".nokey") || !e || !(s && te || t) || $.button !== 0 || !$.isPrimary)
      return;
    $.target?.setPointerCapture?.($.pointerId), H.current = !1;
    const { x: se, y: L } = Oe($.nativeEvent, F.current), Q = Pt({ x: se, y: L }, ie);
    x.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: Q.x,
        startY: Q.y,
        x: se,
        y: L
      }
    }), te || ($.stopPropagation(), $.preventDefault());
  };
  function V($, Y) {
    const { userSelectionRect: ie } = x.getState();
    if (!ie)
      return;
    const { transform: te, nodeLookup: G, edgeLookup: q, connectionLookup: se, triggerNodeChanges: L, triggerEdgeChanges: Q, defaultEdgeOptions: fe } = x.getState(), ye = { x: ie.startX, y: ie.startY }, { x: Ie, y: Ne } = Ct(ye, te), xe = {
      startX: ye.x,
      startY: ye.y,
      x: $ < Ie ? $ : Ie,
      y: Y < Ne ? Y : Ne,
      width: Math.abs($ - Ie),
      height: Math.abs(Y - Ne)
    }, qe = C.current, $e = R.current;
    C.current = new Set(dr(G, xe, te, n === Zt.Partial, !0).map((Ee) => Ee.id)), R.current = /* @__PURE__ */ new Set();
    const Te = fe?.selectable ?? !0;
    for (const Ee of C.current) {
      const Re = se.get(Ee);
      if (Re)
        for (const { edgeId: ze } of Re.values()) {
          const Ze = q.get(ze);
          Ze && (Ze.selectable ?? Te) && R.current.add(ze);
        }
    }
    if (!Qr(qe, C.current)) {
      const Ee = xt(G, C.current, !0);
      L(Ee);
    }
    if (!Qr($e, R.current)) {
      const Ee = xt(q, R.current);
      Q(Ee);
    }
    x.setState({
      userSelectionRect: xe,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function O() {
    if (!r || !F.current)
      return;
    const [$, Y] = fr(b.current, F.current, M);
    E({ x: $, y: Y }).then((ie) => {
      if (!H.current || !ie) {
        w.current = requestAnimationFrame(O);
        return;
      }
      const { x: te, y: G } = b.current;
      V(te, G), w.current = requestAnimationFrame(O);
    });
  }
  const U = () => {
    cancelAnimationFrame(w.current), w.current = 0, k.current = !1;
  };
  ce(() => () => U(), []);
  const X = ($) => {
    const { userSelectionRect: Y, transform: ie, resetSelectedElements: te } = x.getState();
    if (!F.current || !Y)
      return;
    const { x: G, y: q } = Oe($.nativeEvent, F.current);
    b.current = { x: G, y: q };
    const se = Ct({ x: Y.startX, y: Y.startY }, ie);
    if (!H.current) {
      const L = t ? 0 : i;
      if (Math.hypot(G - se.x, q - se.y) <= L)
        return;
      te(), a?.($);
    }
    H.current = !0, k.current || (O(), k.current = !0), V(G, q);
  }, ee = ($) => {
    $.button === 0 && ($.target?.releasePointerCapture?.($.pointerId), !S && $.target === P.current && x.getState().userSelectionRect && N?.($), x.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), H.current && (l?.($), x.setState({
      nodesSelectionActive: C.current.size > 0
    })), U());
  }, oe = ($) => {
    $.target?.releasePointerCapture?.($.pointerId), U();
  }, K = o === !0 || Array.isArray(o) && o.includes(0);
  return h.jsxs("div", { className: ve(["react-flow__pane", { draggable: K, dragging: v, selection: e }]), onClick: D ? void 0 : To(N, P), onContextMenu: To(A, P), onWheel: To(T, P), onPointerEnter: D ? void 0 : d, onPointerMove: D ? X : m, onPointerUp: D ? ee : void 0, onPointerCancel: D ? oe : void 0, onPointerDownCapture: D ? B : void 0, onClickCapture: D ? j : void 0, onPointerLeave: p, ref: P, style: Jn, children: [y, h.jsx(Qh, {})] });
}
function Qo({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: i, multiSelectionActive: s, nodeLookup: a, onError: l } = t.getState(), c = a.get(e);
  if (!c) {
    l?.("012", je.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), c.selected ? (n || c.selected && s) && (i({ nodes: [c], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function da({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: i, nodeClickDistance: s }) {
  const a = ge(), [l, c] = re(!1), u = ae();
  return ce(() => {
    u.current = Lf({
      getStoreItems: () => a.getState(),
      onNodeMouseDown: (f) => {
        Qo({
          id: f,
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
  }, []), ce(() => {
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
const tp = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function fa() {
  const e = ge();
  return he((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: i, nodesDraggable: s, onError: a, updateNodePositions: l, nodeLookup: c, nodeOrigin: u } = e.getState(), f = /* @__PURE__ */ new Map(), d = tp(s), m = r ? i[0] : 5, p = r ? i[1] : 5, y = n.direction.x * m * n.factor, w = n.direction.y * p * n.factor;
    for (const [, x] of c) {
      if (!d(x))
        continue;
      let S = {
        x: x.internals.positionAbsolute.x + y,
        y: x.internals.positionAbsolute.y + w
      };
      r && (S = rn(S, i));
      const { position: g, positionAbsolute: v } = Is({
        nodeId: x.id,
        nextPosition: S,
        nodeLookup: c,
        nodeExtent: o,
        nodeOrigin: u,
        onError: a
      });
      x.position = g, x.internals.positionAbsolute = v, f.set(x.id, x);
    }
    l(f);
  }, []);
}
const br = nr(null), np = br.Provider;
br.Consumer;
const ha = () => Jt(br), op = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), rp = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: i, connection: s } = o, { fromHandle: a, toHandle: l, isValid: c } = s, u = l?.nodeId === e && l?.id === t && l?.type === n;
  return {
    connectingFrom: a?.nodeId === e && a?.id === t && a?.type === n,
    connectingTo: u,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: i === Et.Strict ? a?.type !== n : e !== a?.nodeId || t !== a?.id,
    connectionInProcess: !!a,
    clickConnectionInProcess: !!r,
    valid: u && c
  };
};
function ip({ type: e = "source", position: t = J.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: i = !0, id: s, onConnect: a, children: l, className: c, onMouseDown: u, onTouchStart: f, ...d }, m) {
  const p = s || null, y = e === "target", w = ge(), x = ha(), { connectOnClick: S, noPanClassName: g, rfId: v } = le(op, pe), { connectingFrom: _, connectingTo: E, clickConnecting: M, isPossibleEndHandle: D, connectionInProcess: P, clickConnectionInProcess: F, valid: C } = le(rp(x, p, e), pe);
  x || w.getState().onError?.("010", je.error010());
  const R = (k) => {
    const { defaultEdgeOptions: N, onConnect: A, hasDefaultEdges: T } = w.getState(), j = {
      ...N,
      ...k
    };
    if (T) {
      const { edges: B, setEdges: V, onError: O } = w.getState();
      V(ca(j, B, { onError: O }));
    }
    A?.(j), a?.(j);
  }, H = (k) => {
    if (!x)
      return;
    const N = Rs(k.nativeEvent);
    if (r && (N && k.button === 0 || !N)) {
      const A = w.getState();
      Go.onPointerDown(k.nativeEvent, {
        handleDomNode: k.currentTarget,
        autoPanOnConnect: A.autoPanOnConnect,
        connectionMode: A.connectionMode,
        connectionRadius: A.connectionRadius,
        domNode: A.domNode,
        nodeLookup: A.nodeLookup,
        lib: A.lib,
        isTarget: y,
        handleId: p,
        nodeId: x,
        flowId: A.rfId,
        panBy: A.panBy,
        cancelConnection: A.cancelConnection,
        onConnectStart: A.onConnectStart,
        onConnectEnd: (...T) => w.getState().onConnectEnd?.(...T),
        updateConnection: A.updateConnection,
        onConnect: R,
        isValidConnection: n || ((...T) => w.getState().isValidConnection?.(...T) ?? !0),
        getTransform: () => w.getState().transform,
        getFromHandle: () => w.getState().connection.fromHandle,
        autoPanSpeed: A.autoPanSpeed,
        dragThreshold: A.connectionDragThreshold
      });
    }
    N ? u?.(k) : f?.(k);
  }, b = (k) => {
    const { onClickConnectStart: N, onClickConnectEnd: A, connectionClickStartHandle: T, connectionMode: j, isValidConnection: B, lib: V, rfId: O, nodeLookup: U, connection: X } = w.getState();
    if (!x || !T && !r)
      return;
    if (!T) {
      N?.(k.nativeEvent, { nodeId: x, handleId: p, handleType: e }), w.setState({ connectionClickStartHandle: { nodeId: x, type: e, id: p } });
      return;
    }
    const ee = $s(k.target), oe = n || B, { connection: K, isValid: $ } = Go.isValid(k.nativeEvent, {
      handle: {
        nodeId: x,
        id: p,
        type: e
      },
      connectionMode: j,
      fromNodeId: T.nodeId,
      fromHandleId: T.id || null,
      fromType: T.type,
      isValidConnection: oe,
      flowId: O,
      doc: ee,
      lib: V,
      nodeLookup: U
    });
    $ && K && R(K);
    const Y = structuredClone(X);
    delete Y.inProgress, Y.toPosition = Y.toHandle ? Y.toHandle.position : null, A?.(k, Y), w.setState({ connectionClickStartHandle: null });
  };
  return h.jsx("div", { "data-handleid": p, "data-nodeid": x, "data-handlepos": t, "data-id": `${v}-${x}-${p}-${e}`, className: ve([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    g,
    c,
    {
      source: !y,
      target: y,
      connectable: o,
      connectablestart: r,
      connectableend: i,
      clickconnecting: M,
      connectingfrom: _,
      connectingto: E,
      valid: C,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!P || D) && (P || F ? i : r)
    }
  ]), onMouseDown: H, onTouchStart: H, onClick: S ? b : void 0, ref: m, ...d, children: l });
}
const It = we(la(ip));
function sp({ data: e, isConnectable: t, sourcePosition: n = J.Bottom }) {
  return h.jsxs(h.Fragment, { children: [e?.label, h.jsx(It, { type: "source", position: n, isConnectable: t })] });
}
function ap({ data: e, isConnectable: t, targetPosition: n = J.Top, sourcePosition: o = J.Bottom }) {
  return h.jsxs(h.Fragment, { children: [h.jsx(It, { type: "target", position: n, isConnectable: t }), e?.label, h.jsx(It, { type: "source", position: o, isConnectable: t })] });
}
function cp() {
  return null;
}
function lp({ data: e, isConnectable: t, targetPosition: n = J.Top }) {
  return h.jsxs(h.Fragment, { children: [h.jsx(It, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const Ln = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, Ai = {
  input: sp,
  default: ap,
  output: lp,
  group: cp
};
function up(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const dp = (e) => {
  const { width: t, height: n, x: o, y: r } = on(e.nodeLookup, {
    filter: (i) => !!i.selected
  });
  return {
    width: Ve(t) ? t : null,
    height: Ve(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function fp({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = ge(), { width: r, height: i, transformString: s, userSelectionActive: a } = le(dp, pe), l = fa(), c = ae(null);
  ce(() => {
    n || c.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const u = !a && r !== null && i !== null;
  if (da({
    nodeRef: c,
    disabled: !u
  }), !u)
    return null;
  const f = e ? (m) => {
    const p = o.getState().nodes.filter((y) => y.selected);
    e(m, p);
  } : void 0, d = (m) => {
    Object.prototype.hasOwnProperty.call(Ln, m.key) && (m.preventDefault(), l({
      direction: Ln[m.key],
      factor: m.shiftKey ? 4 : 1
    }));
  };
  return h.jsx("div", { className: ve(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: s
  }, children: h.jsx("div", { ref: c, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : d, style: {
    width: r,
    height: i
  } }) });
}
const Di = typeof window < "u" ? window : void 0, hp = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function pa({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, paneClickDistance: a, deleteKeyCode: l, selectionKeyCode: c, selectionOnDrag: u, selectionMode: f, onSelectionStart: d, onSelectionEnd: m, multiSelectionKeyCode: p, panActivationKeyCode: y, zoomActivationKeyCode: w, elementsSelectable: x, zoomOnScroll: S, zoomOnPinch: g, panOnScroll: v, panOnScrollSpeed: _, panOnScrollMode: E, zoomOnDoubleClick: M, panOnDrag: D, autoPanOnSelection: P, defaultViewport: F, translateExtent: C, minZoom: R, maxZoom: H, preventScrolling: b, onSelectionContextMenu: k, noWheelClassName: N, noPanClassName: A, disableKeyboardA11y: T, onViewportChange: j, isControlledViewport: B }) {
  const { nodesSelectionActive: V, userSelectionActive: O } = le(hp, pe), U = Gt(c, { target: Di }), X = Gt(y, { target: Di }), ee = X || D, oe = X || v, K = u && ee !== !0, $ = U || O || K;
  return qh({ deleteKeyCode: l, multiSelectionKeyCode: p }), h.jsx(Kh, { onPaneContextMenu: i, elementsSelectable: x, zoomOnScroll: S, zoomOnPinch: g, panOnScroll: oe, panOnScrollSpeed: _, panOnScrollMode: E, zoomOnDoubleClick: M, panOnDrag: !U && ee, defaultViewport: F, translateExtent: C, minZoom: R, maxZoom: H, zoomActivationKeyCode: w, preventScrolling: b, noWheelClassName: N, noPanClassName: A, onViewportChange: j, isControlledViewport: B, paneClickDistance: a, selectionOnDrag: K, children: h.jsxs(ep, { onSelectionStart: d, onSelectionEnd: m, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, panOnDrag: ee, autoPanOnSelection: P, isSelecting: !!$, selectionMode: f, selectionKeyPressed: U, paneClickDistance: a, selectionOnDrag: K, children: [e, V && h.jsx(fp, { onSelectionContextMenu: k, noPanClassName: A, disableKeyboardA11y: T })] }) });
}
pa.displayName = "FlowRenderer";
const pp = we(pa), gp = (e) => (t) => e ? dr(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function mp(e) {
  return le(he(gp(e), [e]), pe);
}
const yp = (e) => e.updateNodeInternals;
function xp() {
  const e = le(yp), [t] = re(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
  return ce(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function wp({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = ge(), i = ae(null), s = ae(null), a = ae(e.sourcePosition), l = ae(e.targetPosition), c = ae(t), u = n && !!e.internals.handleBounds;
  return ce(() => {
    i.current && !e.hidden && (!u || s.current !== i.current) && (s.current && o?.unobserve(s.current), o?.observe(i.current), s.current = i.current);
  }, [u, e.hidden]), ce(() => () => {
    s.current && (o?.unobserve(s.current), s.current = null);
  }, []), ce(() => {
    if (i.current) {
      const f = c.current !== t, d = a.current !== e.sourcePosition, m = l.current !== e.targetPosition;
      (f || d || m) && (c.current = t, a.current = e.sourcePosition, l.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: i.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), i;
}
function vp({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: i, onDoubleClick: s, nodesDraggable: a, elementsSelectable: l, nodesConnectable: c, nodesFocusable: u, resizeObserver: f, noDragClassName: d, noPanClassName: m, disableKeyboardA11y: p, rfId: y, nodeTypes: w, nodeClickDistance: x, onError: S }) {
  const { node: g, internals: v, isParent: _ } = le(($) => {
    const Y = $.nodeLookup.get(e), ie = $.parentLookup.has(e);
    return {
      node: Y,
      internals: Y.internals,
      isParent: ie
    };
  }, pe);
  let E = g.type || "default", M = w?.[E] || Ai[E];
  M === void 0 && (S?.("003", je.error003(E)), E = "default", M = w?.default || Ai.default);
  const D = !!(g.draggable || a && typeof g.draggable > "u"), P = !!(g.selectable || l && typeof g.selectable > "u"), F = !!(g.connectable || c && typeof g.connectable > "u"), C = !!(g.focusable || u && typeof g.focusable > "u"), R = ge(), H = Ps(g), b = wp({ node: g, nodeType: E, hasDimensions: H, resizeObserver: f }), k = da({
    nodeRef: b,
    disabled: g.hidden || !D,
    noDragClassName: d,
    handleSelector: g.dragHandle,
    nodeId: e,
    isSelectable: P,
    nodeClickDistance: x
  }), N = fa();
  if (g.hidden)
    return null;
  const A = Je(g), T = up(g), j = P || D || t || n || o || r, B = n ? ($) => n($, { ...v.userNode }) : void 0, V = o ? ($) => o($, { ...v.userNode }) : void 0, O = r ? ($) => r($, { ...v.userNode }) : void 0, U = i ? ($) => i($, { ...v.userNode }) : void 0, X = s ? ($) => s($, { ...v.userNode }) : void 0, ee = ($) => {
    const { selectNodesOnDrag: Y, nodeDragThreshold: ie } = R.getState();
    P && (!Y || !D || ie > 0) && Qo({
      id: e,
      store: R,
      nodeRef: b
    }), t && t($, { ...v.userNode });
  }, oe = ($) => {
    if (!(Ts($.nativeEvent) || p)) {
      if (Es.includes($.key) && P) {
        const Y = $.key === "Escape";
        Qo({
          id: e,
          store: R,
          unselect: Y,
          nodeRef: b
        });
      } else if (D && g.selected && Object.prototype.hasOwnProperty.call(Ln, $.key)) {
        $.preventDefault();
        const { ariaLabelConfig: Y } = R.getState();
        R.setState({
          ariaLiveMessage: Y["node.a11yDescription.ariaLiveMessage"]({
            direction: $.key.replace("Arrow", "").toLowerCase(),
            x: ~~v.positionAbsolute.x,
            y: ~~v.positionAbsolute.y
          })
        }), N({
          direction: Ln[$.key],
          factor: $.shiftKey ? 4 : 1
        });
      }
    }
  }, K = () => {
    if (p || !b.current?.matches(":focus-visible"))
      return;
    const { transform: $, width: Y, height: ie, autoPanOnNodeFocus: te, setCenter: G } = R.getState();
    if (!te)
      return;
    dr(/* @__PURE__ */ new Map([[e, g]]), { x: 0, y: 0, width: Y, height: ie }, $, !0).length > 0 || G(g.position.x + A.width / 2, g.position.y + A.height / 2, {
      zoom: $[2]
    });
  };
  return h.jsx("div", { className: ve([
    "react-flow__node",
    `react-flow__node-${E}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [m]: D
    },
    g.className,
    {
      selected: g.selected,
      selectable: P,
      parent: _,
      draggable: D,
      dragging: k
    }
  ]), ref: b, style: {
    zIndex: v.z,
    transform: `translate(${v.positionAbsolute.x}px,${v.positionAbsolute.y}px)`,
    pointerEvents: j ? "all" : "none",
    visibility: H ? "visible" : "hidden",
    ...g.style,
    ...T
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: B, onMouseMove: V, onMouseLeave: O, onContextMenu: U, onClick: ee, onDoubleClick: X, onKeyDown: C ? oe : void 0, tabIndex: C ? 0 : void 0, onFocus: C ? K : void 0, role: g.ariaRole ?? (C ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": p ? void 0 : `${ta}-${y}`, "aria-label": g.ariaLabel, ...g.domAttributes, children: h.jsx(np, { value: e, children: h.jsx(M, { id: e, data: g.data, type: E, positionAbsoluteX: v.positionAbsolute.x, positionAbsoluteY: v.positionAbsolute.y, selected: g.selected ?? !1, selectable: P, draggable: D, deletable: g.deletable ?? !0, isConnectable: F, sourcePosition: g.sourcePosition, targetPosition: g.targetPosition, dragging: k, dragHandle: g.dragHandle, zIndex: v.z, parentId: g.parentId, ...A }) }) });
}
var bp = we(vp);
const Sp = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function ga(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: i } = le(Sp, pe), s = mp(e.onlyRenderVisibleElements), a = xp();
  return h.jsx("div", { className: "react-flow__nodes", style: Jn, children: s.map((l) => (
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
    h.jsx(bp, { id: l, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: a, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: i }, l)
  )) });
}
ga.displayName = "NodeRenderer";
const Ep = we(ga);
function _p(e) {
  return le(he((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const i = n.nodeLookup.get(r.source), s = n.nodeLookup.get(r.target);
        i && s && wf({
          sourceNode: i,
          targetNode: s,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && o.push(r.id);
      }
    return o;
  }, [e]), pe);
}
const Np = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return h.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, Cp = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return h.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Pi = {
  [Tn.Arrow]: Np,
  [Tn.ArrowClosed]: Cp
};
function kp(e) {
  const t = ge();
  return me(() => Object.prototype.hasOwnProperty.call(Pi, e) ? Pi[e] : (t.getState().onError?.("009", je.error009(e)), null), [e]);
}
const Ip = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: i = "strokeWidth", strokeWidth: s, orient: a = "auto-start-reverse" }) => {
  const l = kp(t);
  return l ? h.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: i, orient: a, refX: "0", refY: "0", children: h.jsx(l, { color: n, strokeWidth: s }) }) : null;
}, ma = ({ defaultColor: e, rfId: t }) => {
  const n = le((i) => i.edges), o = le((i) => i.defaultEdgeOptions), r = me(() => kf(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? h.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: h.jsx("defs", { children: r.map((i) => h.jsx(Ip, { id: i.id, type: i.type, color: i.color, width: i.width, height: i.height, markerUnits: i.markerUnits, strokeWidth: i.strokeWidth, orient: i.orient }, i.id)) }) }) : null;
};
ma.displayName = "MarkerDefinitions";
var Mp = we(ma);
function ya({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: i, labelBgPadding: s = [2, 4], labelBgBorderRadius: a = 2, children: l, className: c, ...u }) {
  const [f, d] = re({ x: 1, y: 0, width: 0, height: 0 }), m = ve(["react-flow__edge-textwrapper", c]), p = ae(null);
  return ce(() => {
    if (p.current) {
      const y = p.current.getBBox();
      d({
        x: y.x,
        y: y.y,
        width: y.width,
        height: y.height
      });
    }
  }, [n]), n ? h.jsxs("g", { transform: `translate(${e - f.width / 2} ${t - f.height / 2})`, className: m, visibility: f.width ? "visible" : "hidden", ...u, children: [r && h.jsx("rect", { width: f.width + 2 * s[0], x: -s[0], y: -s[1], height: f.height + 2 * s[1], className: "react-flow__edge-textbg", style: i, rx: a, ry: a }), h.jsx("text", { className: "react-flow__edge-text", y: f.height / 2, dy: "0.3em", ref: p, style: o, children: n }), l] }) : null;
}
ya.displayName = "EdgeText";
const Ap = we(ya);
function sn({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: a, labelBgBorderRadius: l, interactionWidth: c = 20, ...u }) {
  return h.jsxs(h.Fragment, { children: [h.jsx("path", { ...u, d: e, fill: "none", className: ve(["react-flow__edge-path", u.className]) }), c ? h.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: c, className: "react-flow__edge-interaction" }) : null, o && Ve(t) && Ve(n) ? h.jsx(Ap, { x: t, y: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: a, labelBgBorderRadius: l }) : null] });
}
function ji({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === J.Left || e === J.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function xa({ sourceX: e, sourceY: t, sourcePosition: n = J.Bottom, targetX: o, targetY: r, targetPosition: i = J.Top }) {
  const [s, a] = ji({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [l, c] = ji({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [u, f, d, m] = zs({
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
    f,
    d,
    m
  ];
}
function wa(e) {
  return we(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: a, label: l, labelStyle: c, labelShowBg: u, labelBgStyle: f, labelBgPadding: d, labelBgBorderRadius: m, style: p, markerEnd: y, markerStart: w, interactionWidth: x }) => {
    const [S, g, v] = xa({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: a
    }), _ = e.isInternal ? void 0 : t;
    return h.jsx(sn, { id: _, path: S, labelX: g, labelY: v, label: l, labelStyle: c, labelShowBg: u, labelBgStyle: f, labelBgPadding: d, labelBgBorderRadius: m, style: p, markerEnd: y, markerStart: w, interactionWidth: x });
  });
}
const Dp = wa({ isInternal: !1 }), va = wa({ isInternal: !0 });
Dp.displayName = "SimpleBezierEdge";
va.displayName = "SimpleBezierEdgeInternal";
function ba(e) {
  return we(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: u, labelBgBorderRadius: f, style: d, sourcePosition: m = J.Bottom, targetPosition: p = J.Top, markerEnd: y, markerStart: w, pathOptions: x, interactionWidth: S }) => {
    const [g, v, _] = zn({
      sourceX: n,
      sourceY: o,
      sourcePosition: m,
      targetX: r,
      targetY: i,
      targetPosition: p,
      borderRadius: x?.borderRadius,
      offset: x?.offset,
      stepPosition: x?.stepPosition
    }), E = e.isInternal ? void 0 : t;
    return h.jsx(sn, { id: E, path: g, labelX: v, labelY: _, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: u, labelBgBorderRadius: f, style: d, markerEnd: y, markerStart: w, interactionWidth: S });
  });
}
const Sa = ba({ isInternal: !1 }), Ea = ba({ isInternal: !0 });
Sa.displayName = "SmoothStepEdge";
Ea.displayName = "SmoothStepEdgeInternal";
function _a(e) {
  return we(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return h.jsx(Sa, { ...n, id: o, pathOptions: me(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Pp = _a({ isInternal: !1 }), Na = _a({ isInternal: !0 });
Pp.displayName = "StepEdge";
Na.displayName = "StepEdgeInternal";
function Ca(e) {
  return we(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: u, labelBgBorderRadius: f, style: d, markerEnd: m, markerStart: p, interactionWidth: y }) => {
    const [w, x, S] = Os({ sourceX: n, sourceY: o, targetX: r, targetY: i }), g = e.isInternal ? void 0 : t;
    return h.jsx(sn, { id: g, path: w, labelX: x, labelY: S, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: u, labelBgBorderRadius: f, style: d, markerEnd: m, markerStart: p, interactionWidth: y });
  });
}
const jp = Ca({ isInternal: !1 }), ka = Ca({ isInternal: !0 });
jp.displayName = "StraightEdge";
ka.displayName = "StraightEdgeInternal";
function Ia(e) {
  return we(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s = J.Bottom, targetPosition: a = J.Top, label: l, labelStyle: c, labelShowBg: u, labelBgStyle: f, labelBgPadding: d, labelBgBorderRadius: m, style: p, markerEnd: y, markerStart: w, pathOptions: x, interactionWidth: S }) => {
    const [g, v, _] = Ls({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: a,
      curvature: x?.curvature
    }), E = e.isInternal ? void 0 : t;
    return h.jsx(sn, { id: E, path: g, labelX: v, labelY: _, label: l, labelStyle: c, labelShowBg: u, labelBgStyle: f, labelBgPadding: d, labelBgBorderRadius: m, style: p, markerEnd: y, markerStart: w, interactionWidth: S });
  });
}
const $p = Ia({ isInternal: !1 }), Ma = Ia({ isInternal: !0 });
$p.displayName = "BezierEdge";
Ma.displayName = "BezierEdgeInternal";
const $i = {
  default: Ma,
  straight: ka,
  step: Na,
  smoothstep: Ea,
  simplebezier: va
}, Ti = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, Tp = (e, t, n) => n === J.Left ? e - t : n === J.Right ? e + t : e, Rp = (e, t, n) => n === J.Top ? e - t : n === J.Bottom ? e + t : e, Ri = "react-flow__edgeupdater";
function zi({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: i, onMouseOut: s, type: a }) {
  return h.jsx("circle", { onMouseDown: r, onMouseEnter: i, onMouseOut: s, className: ve([Ri, `${Ri}-${a}`]), cx: Tp(t, o, e), cy: Rp(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function zp({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: i, targetY: s, sourcePosition: a, targetPosition: l, onReconnect: c, onReconnectStart: u, onReconnectEnd: f, setReconnecting: d, setUpdateHover: m }) {
  const p = ge(), y = (v, _) => {
    if (v.button !== 0)
      return;
    const { autoPanOnConnect: E, domNode: M, connectionMode: D, connectionRadius: P, lib: F, onConnectStart: C, cancelConnection: R, nodeLookup: H, rfId: b, panBy: k, updateConnection: N } = p.getState(), A = _.type === "target", T = (V, O) => {
      d(!1), f?.(V, n, _.type, O);
    }, j = (V) => c?.(n, V), B = (V, O) => {
      d(!0), u?.(v, n, _.type), C?.(V, O);
    };
    Go.onPointerDown(v.nativeEvent, {
      autoPanOnConnect: E,
      connectionMode: D,
      connectionRadius: P,
      domNode: M,
      handleId: _.id,
      nodeId: _.nodeId,
      nodeLookup: H,
      isTarget: A,
      edgeUpdaterType: _.type,
      lib: F,
      flowId: b,
      cancelConnection: R,
      panBy: k,
      isValidConnection: (...V) => p.getState().isValidConnection?.(...V) ?? !0,
      onConnect: j,
      onConnectStart: B,
      onConnectEnd: (...V) => p.getState().onConnectEnd?.(...V),
      onReconnectEnd: T,
      updateConnection: N,
      getTransform: () => p.getState().transform,
      getFromHandle: () => p.getState().connection.fromHandle,
      dragThreshold: p.getState().connectionDragThreshold,
      handleDomNode: v.currentTarget
    });
  }, w = (v) => y(v, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), x = (v) => y(v, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), S = () => m(!0), g = () => m(!1);
  return h.jsxs(h.Fragment, { children: [(e === !0 || e === "source") && h.jsx(zi, { position: a, centerX: o, centerY: r, radius: t, onMouseDown: w, onMouseEnter: S, onMouseOut: g, type: "source" }), (e === !0 || e === "target") && h.jsx(zi, { position: l, centerX: i, centerY: s, radius: t, onMouseDown: x, onMouseEnter: S, onMouseOut: g, type: "target" })] });
}
function Lp({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: i, onContextMenu: s, onMouseEnter: a, onMouseMove: l, onMouseLeave: c, reconnectRadius: u, onReconnect: f, onReconnectStart: d, onReconnectEnd: m, rfId: p, edgeTypes: y, noPanClassName: w, onError: x, disableKeyboardA11y: S }) {
  let g = le((G) => G.edgeLookup.get(e));
  const v = le((G) => G.defaultEdgeOptions);
  g = v ? { ...v, ...g } : g;
  let _ = g.type || "default", E = y?.[_] || $i[_];
  E === void 0 && (x?.("011", je.error011(_)), _ = "default", E = y?.default || $i.default);
  const M = !!(g.focusable || t && typeof g.focusable > "u"), D = typeof f < "u" && (g.reconnectable || n && typeof g.reconnectable > "u"), P = !!(g.selectable || o && typeof g.selectable > "u"), F = ae(null), [C, R] = re(!1), [H, b] = re(!1), k = ge(), { zIndex: N, sourceX: A, sourceY: T, targetX: j, targetY: B, sourcePosition: V, targetPosition: O } = le(he((G) => {
    const q = G.nodeLookup.get(g.source), se = G.nodeLookup.get(g.target);
    if (!q || !se)
      return {
        zIndex: g.zIndex,
        ...Ti
      };
    const L = Cf({
      id: e,
      sourceNode: q,
      targetNode: se,
      sourceHandle: g.sourceHandle || null,
      targetHandle: g.targetHandle || null,
      connectionMode: G.connectionMode,
      onError: x
    });
    return {
      zIndex: xf({
        selected: g.selected,
        zIndex: g.zIndex,
        sourceNode: q,
        targetNode: se,
        elevateOnSelect: G.elevateEdgesOnSelect,
        zIndexMode: G.zIndexMode
      }),
      ...L || Ti
    };
  }, [g.source, g.target, g.sourceHandle, g.targetHandle, g.selected, g.zIndex]), pe), U = me(() => g.markerStart ? `url('#${Uo(g.markerStart, p)}')` : void 0, [g.markerStart, p]), X = me(() => g.markerEnd ? `url('#${Uo(g.markerEnd, p)}')` : void 0, [g.markerEnd, p]);
  if (g.hidden || A === null || T === null || j === null || B === null)
    return null;
  const ee = (G) => {
    const { addSelectedEdges: q, unselectNodesAndEdges: se, multiSelectionActive: L } = k.getState();
    P && (k.setState({ nodesSelectionActive: !1 }), g.selected && L ? (se({ nodes: [], edges: [g] }), F.current?.blur()) : q([e])), r && r(G, g);
  }, oe = i ? (G) => {
    i(G, { ...g });
  } : void 0, K = s ? (G) => {
    s(G, { ...g });
  } : void 0, $ = a ? (G) => {
    a(G, { ...g });
  } : void 0, Y = l ? (G) => {
    l(G, { ...g });
  } : void 0, ie = c ? (G) => {
    c(G, { ...g });
  } : void 0, te = (G) => {
    if (!S && Es.includes(G.key) && P) {
      const { unselectNodesAndEdges: q, addSelectedEdges: se } = k.getState();
      G.key === "Escape" ? (F.current?.blur(), q({ edges: [g] })) : se([e]);
    }
  };
  return h.jsx("svg", { style: { zIndex: N }, children: h.jsxs("g", { className: ve([
    "react-flow__edge",
    `react-flow__edge-${_}`,
    g.className,
    w,
    {
      selected: g.selected,
      animated: g.animated,
      inactive: !P && !r,
      updating: C,
      selectable: P
    }
  ]), onClick: ee, onDoubleClick: oe, onContextMenu: K, onMouseEnter: $, onMouseMove: Y, onMouseLeave: ie, onKeyDown: M ? te : void 0, tabIndex: M ? 0 : void 0, role: g.ariaRole ?? (M ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": g.ariaLabel === null ? void 0 : g.ariaLabel || `Edge from ${g.source} to ${g.target}`, "aria-describedby": M ? `${na}-${p}` : void 0, ref: F, ...g.domAttributes, children: [!H && h.jsx(E, { id: e, source: g.source, target: g.target, type: g.type, selected: g.selected, animated: g.animated, selectable: P, deletable: g.deletable ?? !0, label: g.label, labelStyle: g.labelStyle, labelShowBg: g.labelShowBg, labelBgStyle: g.labelBgStyle, labelBgPadding: g.labelBgPadding, labelBgBorderRadius: g.labelBgBorderRadius, sourceX: A, sourceY: T, targetX: j, targetY: B, sourcePosition: V, targetPosition: O, data: g.data, style: g.style, sourceHandleId: g.sourceHandle, targetHandleId: g.targetHandle, markerStart: U, markerEnd: X, pathOptions: "pathOptions" in g ? g.pathOptions : void 0, interactionWidth: g.interactionWidth }), D && h.jsx(zp, { edge: g, isReconnectable: D, reconnectRadius: u, onReconnect: f, onReconnectStart: d, onReconnectEnd: m, sourceX: A, sourceY: T, targetX: j, targetY: B, sourcePosition: V, targetPosition: O, setUpdateHover: R, setReconnecting: b })] }) });
}
var Hp = we(Lp);
const Vp = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function Aa({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: i, onEdgeContextMenu: s, onEdgeMouseEnter: a, onEdgeMouseMove: l, onEdgeMouseLeave: c, onEdgeClick: u, reconnectRadius: f, onEdgeDoubleClick: d, onReconnectStart: m, onReconnectEnd: p, disableKeyboardA11y: y }) {
  const { edgesFocusable: w, edgesReconnectable: x, elementsSelectable: S, onError: g } = le(Vp, pe), v = _p(t);
  return h.jsxs("div", { className: "react-flow__edges", children: [h.jsx(Mp, { defaultColor: e, rfId: n }), v.map((_) => h.jsx(Hp, { id: _, edgesFocusable: w, edgesReconnectable: x, elementsSelectable: S, noPanClassName: r, onReconnect: i, onContextMenu: s, onMouseEnter: a, onMouseMove: l, onMouseLeave: c, onClick: u, reconnectRadius: f, onDoubleClick: d, onReconnectStart: m, onReconnectEnd: p, rfId: n, onError: g, edgeTypes: o, disableKeyboardA11y: y }, _))] });
}
Aa.displayName = "EdgeRenderer";
const Op = we(Aa), Bp = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function Fp({ children: e }) {
  const t = le(Bp);
  return h.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function Wp(e) {
  const t = vr(), n = ae(!1);
  ce(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Yp = (e) => e.panZoom?.syncViewport;
function Xp(e) {
  const t = le(Yp), n = ge();
  return ce(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function qp(e) {
  return e.connection.inProgress ? { ...e.connection, to: Pt(e.connection.to, e.transform) } : { ...e.connection };
}
function Zp(e) {
  return qp;
}
function Up(e) {
  const t = Zp();
  return le(t, pe);
}
const Kp = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function Gp({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: i, height: s, isValid: a, inProgress: l } = le(Kp, pe);
  return !(i && r && l) ? null : h.jsx("svg", { style: e, width: i, height: s, className: "react-flow__connectionline react-flow__container", children: h.jsx("g", { className: ve(["react-flow__connection", Cs(a)]), children: h.jsx(Da, { style: t, type: n, CustomComponent: o, isValid: a }) }) });
}
const Da = ({ style: e, type: t = ot.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: i, fromNode: s, fromHandle: a, fromPosition: l, to: c, toNode: u, toHandle: f, toPosition: d, pointer: m } = Up();
  if (!r)
    return;
  if (n)
    return h.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: s, fromHandle: a, fromX: i.x, fromY: i.y, toX: c.x, toY: c.y, fromPosition: l, toPosition: d, connectionStatus: Cs(o), toNode: u, toHandle: f, pointer: m });
  let p = "";
  const y = {
    sourceX: i.x,
    sourceY: i.y,
    sourcePosition: l,
    targetX: c.x,
    targetY: c.y,
    targetPosition: d
  };
  switch (t) {
    case ot.Bezier:
      [p] = Ls(y);
      break;
    case ot.SimpleBezier:
      [p] = xa(y);
      break;
    case ot.Step:
      [p] = zn({
        ...y,
        borderRadius: 0
      });
      break;
    case ot.SmoothStep:
      [p] = zn(y);
      break;
    default:
      [p] = Os(y);
  }
  return h.jsx("path", { d: p, fill: "none", className: "react-flow__connection-path", style: e });
};
Da.displayName = "ConnectionLine";
const Qp = {};
function Li(e = Qp) {
  ae(e), ge(), ce(() => {
  }, [e]);
}
function Jp() {
  ge(), ae(!1), ce(() => {
  }, []);
}
function Pa({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: i, onEdgeDoubleClick: s, onNodeMouseEnter: a, onNodeMouseMove: l, onNodeMouseLeave: c, onNodeContextMenu: u, onSelectionContextMenu: f, onSelectionStart: d, onSelectionEnd: m, connectionLineType: p, connectionLineStyle: y, connectionLineComponent: w, connectionLineContainerStyle: x, selectionKeyCode: S, selectionOnDrag: g, selectionMode: v, multiSelectionKeyCode: _, panActivationKeyCode: E, zoomActivationKeyCode: M, deleteKeyCode: D, onlyRenderVisibleElements: P, elementsSelectable: F, defaultViewport: C, translateExtent: R, minZoom: H, maxZoom: b, preventScrolling: k, defaultMarkerColor: N, zoomOnScroll: A, zoomOnPinch: T, panOnScroll: j, panOnScrollSpeed: B, panOnScrollMode: V, zoomOnDoubleClick: O, panOnDrag: U, autoPanOnSelection: X, onPaneClick: ee, onPaneMouseEnter: oe, onPaneMouseMove: K, onPaneMouseLeave: $, onPaneScroll: Y, onPaneContextMenu: ie, paneClickDistance: te, nodeClickDistance: G, onEdgeContextMenu: q, onEdgeMouseEnter: se, onEdgeMouseMove: L, onEdgeMouseLeave: Q, reconnectRadius: fe, onReconnect: ye, onReconnectStart: Ie, onReconnectEnd: Ne, noDragClassName: xe, noWheelClassName: qe, noPanClassName: $e, disableKeyboardA11y: Te, nodeExtent: Ee, rfId: Re, viewport: ze, onViewportChange: Ze }) {
  return Li(e), Li(t), Jp(), Wp(n), Xp(ze), h.jsx(pp, { onPaneClick: ee, onPaneMouseEnter: oe, onPaneMouseMove: K, onPaneMouseLeave: $, onPaneContextMenu: ie, onPaneScroll: Y, paneClickDistance: te, deleteKeyCode: D, selectionKeyCode: S, selectionOnDrag: g, selectionMode: v, onSelectionStart: d, onSelectionEnd: m, multiSelectionKeyCode: _, panActivationKeyCode: E, zoomActivationKeyCode: M, elementsSelectable: F, zoomOnScroll: A, zoomOnPinch: T, zoomOnDoubleClick: O, panOnScroll: j, panOnScrollSpeed: B, panOnScrollMode: V, panOnDrag: U, autoPanOnSelection: X, defaultViewport: C, translateExtent: R, minZoom: H, maxZoom: b, onSelectionContextMenu: f, preventScrolling: k, noDragClassName: xe, noWheelClassName: qe, noPanClassName: $e, disableKeyboardA11y: Te, onViewportChange: Ze, isControlledViewport: !!ze, children: h.jsxs(Fp, { children: [h.jsx(Op, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: s, onReconnect: ye, onReconnectStart: Ie, onReconnectEnd: Ne, onlyRenderVisibleElements: P, onEdgeContextMenu: q, onEdgeMouseEnter: se, onEdgeMouseMove: L, onEdgeMouseLeave: Q, reconnectRadius: fe, defaultMarkerColor: N, noPanClassName: $e, disableKeyboardA11y: Te, rfId: Re }), h.jsx(Gp, { style: y, type: p, component: w, containerStyle: x }), h.jsx("div", { className: "react-flow__edgelabel-renderer" }), h.jsx(Ep, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: i, onNodeMouseEnter: a, onNodeMouseMove: l, onNodeMouseLeave: c, onNodeContextMenu: u, nodeClickDistance: G, onlyRenderVisibleElements: P, noPanClassName: $e, noDragClassName: xe, disableKeyboardA11y: Te, nodeExtent: Ee, rfId: Re }), h.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Pa.displayName = "GraphView";
const eg = we(Pa), tg = Ds(), Hi = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: a, minZoom: l = 0.5, maxZoom: c = 2, nodeOrigin: u, nodeExtent: f, zIndexMode: d = "basic" } = {}) => {
  const m = /* @__PURE__ */ new Map(), p = /* @__PURE__ */ new Map(), y = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), x = o ?? t ?? [], S = n ?? e ?? [], g = u ?? [0, 0], v = f ?? qt;
  Ws(y, w, x);
  const { nodesInitialized: _ } = Ko(S, m, p, {
    nodeOrigin: g,
    nodeExtent: v,
    zIndexMode: d
  });
  let E = [0, 0, 1];
  if (s && r && i) {
    const M = on(m, {
      filter: (C) => !!((C.width || C.initialWidth) && (C.height || C.initialHeight))
    }), { x: D, y: P, zoom: F } = hr(M, r, i, l, c, a?.padding ?? 0.1);
    E = [D, P, F];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: i ?? 0,
    transform: E,
    nodes: S,
    nodesInitialized: _,
    nodeLookup: m,
    parentLookup: p,
    edges: x,
    edgeLookup: w,
    connectionLookup: y,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: o !== void 0,
    panZoom: null,
    minZoom: l,
    maxZoom: c,
    translateExtent: qt,
    nodeExtent: v,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: Et.Strict,
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
    fitViewOptions: a,
    fitViewResolver: null,
    connection: { ...Ns },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: tg,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: _s,
    zIndexMode: d,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, ng = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: a, minZoom: l, maxZoom: c, nodeOrigin: u, nodeExtent: f, zIndexMode: d }) => gh((m, p) => {
  async function y() {
    const { nodeLookup: w, panZoom: x, fitViewOptions: S, fitViewResolver: g, width: v, height: _, minZoom: E, maxZoom: M } = p();
    x && (await df({
      nodes: w,
      width: v,
      height: _,
      panZoom: x,
      minZoom: E,
      maxZoom: M
    }, S), g?.resolve(!0), m({ fitViewResolver: null }));
  }
  return {
    ...Hi({
      nodes: e,
      edges: t,
      width: r,
      height: i,
      fitView: s,
      fitViewOptions: a,
      minZoom: l,
      maxZoom: c,
      nodeOrigin: u,
      nodeExtent: f,
      defaultNodes: n,
      defaultEdges: o,
      zIndexMode: d
    }),
    setNodes: (w) => {
      const { nodeLookup: x, parentLookup: S, nodeOrigin: g, elevateNodesOnSelect: v, fitViewQueued: _, zIndexMode: E, nodesSelectionActive: M } = p(), { nodesInitialized: D, hasSelectedNodes: P } = Ko(w, x, S, {
        nodeOrigin: g,
        nodeExtent: f,
        elevateNodesOnSelect: v,
        checkEquality: !0,
        zIndexMode: E
      }), F = M && P;
      _ && D ? (y(), m({
        nodes: w,
        nodesInitialized: D,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: F
      })) : m({ nodes: w, nodesInitialized: D, nodesSelectionActive: F });
    },
    setEdges: (w) => {
      const { connectionLookup: x, edgeLookup: S } = p();
      Ws(x, S, w), m({ edges: w });
    },
    setDefaultNodesAndEdges: (w, x) => {
      if (w) {
        const { setNodes: S } = p();
        S(w), m({ hasDefaultNodes: !0 });
      }
      if (x) {
        const { setEdges: S } = p();
        S(x), m({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (w) => {
      const { triggerNodeChanges: x, nodeLookup: S, parentLookup: g, domNode: v, nodeOrigin: _, nodeExtent: E, debug: M, fitViewQueued: D, zIndexMode: P } = p(), { changes: F, updatedInternals: C } = $f(w, S, g, v, _, E, P);
      C && (Af(S, g, { nodeOrigin: _, nodeExtent: E, zIndexMode: P }), D ? (y(), m({ fitViewQueued: !1, fitViewOptions: void 0 })) : m({}), F?.length > 0 && (M && console.log("React Flow: trigger node changes", F), x?.(F)));
    },
    updateNodePositions: (w, x = !1) => {
      const S = [];
      let g = [];
      const { nodeLookup: v, triggerNodeChanges: _, connection: E, updateConnection: M, onNodesChangeMiddlewareMap: D } = p();
      for (const [P, F] of w) {
        const C = v.get(P), R = !!(C?.expandParent && C?.parentId && F?.position), H = {
          id: P,
          type: "position",
          position: R ? {
            x: Math.max(0, F.position.x),
            y: Math.max(0, F.position.y)
          } : F.position,
          dragging: x
        };
        if (C && E.inProgress && E.fromNode.id === C.id) {
          const b = pt(C, E.fromHandle, J.Left, !0);
          M({ ...E, from: b });
        }
        R && C.parentId && S.push({
          id: P,
          parentId: C.parentId,
          rect: {
            ...F.internals.positionAbsolute,
            width: F.measured.width ?? 0,
            height: F.measured.height ?? 0
          }
        }), g.push(H);
      }
      if (S.length > 0) {
        const { parentLookup: P, nodeOrigin: F } = p(), C = wr(S, v, P, F);
        g.push(...C);
      }
      for (const P of D.values())
        g = P(g);
      _(g);
    },
    triggerNodeChanges: (w) => {
      const { onNodesChange: x, setNodes: S, nodes: g, hasDefaultNodes: v, debug: _ } = p();
      if (w?.length) {
        if (v) {
          const E = ia(w, g);
          S(E);
        }
        _ && console.log("React Flow: trigger node changes", w), x?.(w);
      }
    },
    triggerEdgeChanges: (w) => {
      const { onEdgesChange: x, setEdges: S, edges: g, hasDefaultEdges: v, debug: _ } = p();
      if (w?.length) {
        if (v) {
          const E = sa(w, g);
          S(E);
        }
        _ && console.log("React Flow: trigger edge changes", w), x?.(w);
      }
    },
    addSelectedNodes: (w) => {
      const { multiSelectionActive: x, edgeLookup: S, nodeLookup: g, triggerNodeChanges: v, triggerEdgeChanges: _ } = p();
      if (x) {
        const E = w.map((M) => st(M, !0));
        v(E);
        return;
      }
      v(xt(g, /* @__PURE__ */ new Set([...w]), !0)), _(xt(S));
    },
    addSelectedEdges: (w) => {
      const { multiSelectionActive: x, edgeLookup: S, nodeLookup: g, triggerNodeChanges: v, triggerEdgeChanges: _ } = p();
      if (x) {
        const E = w.map((M) => st(M, !0));
        _(E);
        return;
      }
      _(xt(S, /* @__PURE__ */ new Set([...w]))), v(xt(g, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: w, edges: x } = {}) => {
      const { edges: S, nodes: g, nodeLookup: v, triggerNodeChanges: _, triggerEdgeChanges: E } = p(), M = w || g, D = x || S, P = [];
      for (const C of M) {
        if (!C.selected)
          continue;
        const R = v.get(C.id);
        R && (R.selected = !1), P.push(st(C.id, !1));
      }
      const F = [];
      for (const C of D)
        C.selected && F.push(st(C.id, !1));
      _(P), E(F);
    },
    setMinZoom: (w) => {
      const { panZoom: x, maxZoom: S } = p();
      x?.setScaleExtent([w, S]), m({ minZoom: w });
    },
    setMaxZoom: (w) => {
      const { panZoom: x, minZoom: S } = p();
      x?.setScaleExtent([S, w]), m({ maxZoom: w });
    },
    setTranslateExtent: (w) => {
      p().panZoom?.setTranslateExtent(w), m({ translateExtent: w });
    },
    resetSelectedElements: () => {
      const { edges: w, nodes: x, triggerNodeChanges: S, triggerEdgeChanges: g, elementsSelectable: v } = p();
      if (!v)
        return;
      const _ = x.reduce((M, D) => D.selected ? [...M, st(D.id, !1)] : M, []), E = w.reduce((M, D) => D.selected ? [...M, st(D.id, !1)] : M, []);
      S(_), g(E);
    },
    setNodeExtent: (w) => {
      const { nodes: x, nodeLookup: S, parentLookup: g, nodeOrigin: v, elevateNodesOnSelect: _, nodeExtent: E, zIndexMode: M } = p();
      w[0][0] === E[0][0] && w[0][1] === E[0][1] && w[1][0] === E[1][0] && w[1][1] === E[1][1] || (Ko(x, S, g, {
        nodeOrigin: v,
        nodeExtent: w,
        elevateNodesOnSelect: _,
        checkEquality: !1,
        zIndexMode: M
      }), m({ nodeExtent: w }));
    },
    panBy: (w) => {
      const { transform: x, width: S, height: g, panZoom: v, translateExtent: _ } = p();
      return Tf({ delta: w, panZoom: v, transform: x, translateExtent: _, width: S, height: g });
    },
    setCenter: async (w, x, S) => {
      const { width: g, height: v, maxZoom: _, panZoom: E } = p();
      if (!E)
        return !1;
      const M = typeof S?.zoom < "u" ? S.zoom : _;
      return await E.setViewport({
        x: g / 2 - w * M,
        y: v / 2 - x * M,
        zoom: M
      }, { duration: S?.duration, ease: S?.ease, interpolate: S?.interpolate }), !0;
    },
    cancelConnection: () => {
      m({
        connection: { ...Ns }
      });
    },
    updateConnection: (w) => {
      m({ connection: w });
    },
    reset: () => m({ ...Hi() })
  };
}, Object.is);
function og({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: i, initialMinZoom: s, initialMaxZoom: a, initialFitViewOptions: l, fitView: c, nodeOrigin: u, nodeExtent: f, zIndexMode: d, children: m }) {
  const [p] = re(() => ng({
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
    nodeExtent: f,
    zIndexMode: d
  }));
  return h.jsx(wh, { value: p, children: h.jsx(Fh, { children: m }) });
}
function rg({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: i, height: s, fitView: a, fitViewOptions: l, minZoom: c, maxZoom: u, nodeOrigin: f, nodeExtent: d, zIndexMode: m }) {
  return Jt(Gn) ? h.jsx(h.Fragment, { children: e }) : h.jsx(og, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: i, initialHeight: s, fitView: a, initialFitViewOptions: l, initialMinZoom: c, initialMaxZoom: u, nodeOrigin: f, nodeExtent: d, zIndexMode: m, children: e });
}
const ig = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function sg({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: i, edgeTypes: s, onNodeClick: a, onEdgeClick: l, onInit: c, onMove: u, onMoveStart: f, onMoveEnd: d, onConnect: m, onConnectStart: p, onConnectEnd: y, onClickConnectStart: w, onClickConnectEnd: x, onNodeMouseEnter: S, onNodeMouseMove: g, onNodeMouseLeave: v, onNodeContextMenu: _, onNodeDoubleClick: E, onNodeDragStart: M, onNodeDrag: D, onNodeDragStop: P, onNodesDelete: F, onEdgesDelete: C, onDelete: R, onSelectionChange: H, onSelectionDragStart: b, onSelectionDrag: k, onSelectionDragStop: N, onSelectionContextMenu: A, onSelectionStart: T, onSelectionEnd: j, onBeforeDelete: B, connectionMode: V, connectionLineType: O = ot.Bezier, connectionLineStyle: U, connectionLineComponent: X, connectionLineContainerStyle: ee, deleteKeyCode: oe = "Backspace", selectionKeyCode: K = "Shift", selectionOnDrag: $ = !1, selectionMode: Y = Zt.Full, panActivationKeyCode: ie = "Space", multiSelectionKeyCode: te = Kt() ? "Meta" : "Control", zoomActivationKeyCode: G = Kt() ? "Meta" : "Control", snapToGrid: q, snapGrid: se, onlyRenderVisibleElements: L = !1, selectNodesOnDrag: Q, nodesDraggable: fe, autoPanOnNodeFocus: ye, nodesConnectable: Ie, nodesFocusable: Ne, nodeOrigin: xe = oa, edgesFocusable: qe, edgesReconnectable: $e, elementsSelectable: Te = !0, defaultViewport: Ee = Ph, minZoom: Re = 0.5, maxZoom: ze = 2, translateExtent: Ze = qt, preventScrolling: no = !0, nodeExtent: gt, defaultMarkerColor: cn = "#b1b1b7", zoomOnScroll: ln = !0, zoomOnPinch: oo = !0, panOnScroll: jt = !1, panOnScrollSpeed: ro = 0.5, panOnScrollMode: io = lt.Free, zoomOnDoubleClick: so = !0, panOnDrag: ao = !0, onPaneClick: co, onPaneMouseEnter: $t, onPaneMouseMove: lo, onPaneMouseLeave: uo, onPaneScroll: fo, onPaneContextMenu: ho, paneClickDistance: po = 1, nodeClickDistance: go = 0, children: mo, onReconnect: un, onReconnectStart: dn, onReconnectEnd: yo, onEdgeContextMenu: xo, onEdgeDoubleClick: wo, onEdgeMouseEnter: vo, onEdgeMouseMove: I, onEdgeMouseLeave: z, reconnectRadius: W = 10, onNodesChange: Z, onEdgesChange: ne, noDragClassName: de = "nodrag", noWheelClassName: ue = "nowheel", noPanClassName: be = "nopan", fitView: Me, fitViewOptions: et, connectOnClick: Ce, attributionPosition: Ga, proOptions: Qa, defaultEdgeOptions: Ja, elevateNodesOnSelect: ec = !0, elevateEdgesOnSelect: tc = !1, disableKeyboardA11y: Nr = !1, autoPanOnConnect: nc, autoPanOnNodeDrag: oc, autoPanOnSelection: rc = !0, autoPanSpeed: ic, connectionRadius: sc, isValidConnection: ac, onError: cc, style: lc, id: Cr, nodeDragThreshold: uc, connectionDragThreshold: dc, viewport: fc, onViewportChange: hc, width: pc, height: gc, colorMode: mc = "light", debug: yc, onScroll: kr, ariaLabelConfig: xc, zIndexMode: Ir = "basic", ...wc }, vc) {
  const bo = Cr || "1", bc = Rh(mc), Sc = he((Mr) => {
    Mr.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), kr?.(Mr);
  }, [kr]);
  return h.jsx("div", { "data-testid": "rf__wrapper", ...wc, onScroll: Sc, style: { ...lc, ...ig }, ref: vc, className: ve(["react-flow", r, bc]), id: Cr, role: "application", children: h.jsxs(rg, { nodes: e, edges: t, width: pc, height: gc, fitView: Me, fitViewOptions: et, minZoom: Re, maxZoom: ze, nodeOrigin: xe, nodeExtent: gt, zIndexMode: Ir, children: [h.jsx(Th, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: m, onConnectStart: p, onConnectEnd: y, onClickConnectStart: w, onClickConnectEnd: x, nodesDraggable: fe, autoPanOnNodeFocus: ye, nodesConnectable: Ie, nodesFocusable: Ne, edgesFocusable: qe, edgesReconnectable: $e, elementsSelectable: Te, elevateNodesOnSelect: ec, elevateEdgesOnSelect: tc, minZoom: Re, maxZoom: ze, nodeExtent: gt, onNodesChange: Z, onEdgesChange: ne, snapToGrid: q, snapGrid: se, connectionMode: V, translateExtent: Ze, connectOnClick: Ce, defaultEdgeOptions: Ja, fitView: Me, fitViewOptions: et, onNodesDelete: F, onEdgesDelete: C, onDelete: R, onNodeDragStart: M, onNodeDrag: D, onNodeDragStop: P, onSelectionDrag: k, onSelectionDragStart: b, onSelectionDragStop: N, onMove: u, onMoveStart: f, onMoveEnd: d, noPanClassName: be, nodeOrigin: xe, rfId: bo, autoPanOnConnect: nc, autoPanOnNodeDrag: oc, autoPanSpeed: ic, onError: cc, connectionRadius: sc, isValidConnection: ac, selectNodesOnDrag: Q, nodeDragThreshold: uc, connectionDragThreshold: dc, onBeforeDelete: B, debug: yc, ariaLabelConfig: xc, zIndexMode: Ir }), h.jsx(eg, { onInit: c, onNodeClick: a, onEdgeClick: l, onNodeMouseEnter: S, onNodeMouseMove: g, onNodeMouseLeave: v, onNodeContextMenu: _, onNodeDoubleClick: E, nodeTypes: i, edgeTypes: s, connectionLineType: O, connectionLineStyle: U, connectionLineComponent: X, connectionLineContainerStyle: ee, selectionKeyCode: K, selectionOnDrag: $, selectionMode: Y, deleteKeyCode: oe, multiSelectionKeyCode: te, panActivationKeyCode: ie, zoomActivationKeyCode: G, onlyRenderVisibleElements: L, defaultViewport: Ee, translateExtent: Ze, minZoom: Re, maxZoom: ze, preventScrolling: no, zoomOnScroll: ln, zoomOnPinch: oo, zoomOnDoubleClick: so, panOnScroll: jt, panOnScrollSpeed: ro, panOnScrollMode: io, panOnDrag: ao, autoPanOnSelection: rc, onPaneClick: co, onPaneMouseEnter: $t, onPaneMouseMove: lo, onPaneMouseLeave: uo, onPaneScroll: fo, onPaneContextMenu: ho, paneClickDistance: po, nodeClickDistance: go, onSelectionContextMenu: A, onSelectionStart: T, onSelectionEnd: j, onReconnect: un, onReconnectStart: dn, onReconnectEnd: yo, onEdgeContextMenu: xo, onEdgeDoubleClick: wo, onEdgeMouseEnter: vo, onEdgeMouseMove: I, onEdgeMouseLeave: z, reconnectRadius: W, defaultMarkerColor: cn, noDragClassName: de, noWheelClassName: ue, noPanClassName: be, rfId: bo, disableKeyboardA11y: Nr, nodeExtent: gt, viewport: fc, onViewportChange: hc }), h.jsx(Dh, { onSelectionChange: H }), mo, h.jsx(Ch, { proOptions: Qa, position: Ga }), h.jsx(Nh, { rfId: bo, disableKeyboardA11y: Nr })] }) });
}
var ag = la(sg);
const cg = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function lg({ children: e }) {
  const t = le(cg);
  return t ? xh.createPortal(e, t) : null;
}
function ug({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return h.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: ve(["react-flow__background-pattern", n, o]) });
}
function dg({ radius: e, className: t }) {
  return h.jsx("circle", { cx: e, cy: e, r: e, className: ve(["react-flow__background-pattern", "dots", t]) });
}
var rt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(rt || (rt = {}));
const fg = {
  [rt.Dots]: 1,
  [rt.Lines]: 1,
  [rt.Cross]: 6
}, hg = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function ja({
  id: e,
  variant: t = rt.Dots,
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
  const f = ae(null), { transform: d, patternId: m } = le(hg, pe), p = o || fg[t], y = t === rt.Dots, w = t === rt.Cross, x = Array.isArray(n) ? n : [n, n], S = [x[0] * d[2] || 1, x[1] * d[2] || 1], g = p * d[2], v = Array.isArray(i) ? i : [i, i], _ = w ? [g, g] : S, E = [
    v[0] * d[2] || 1 + _[0] / 2,
    v[1] * d[2] || 1 + _[1] / 2
  ], M = `${m}${e || ""}`;
  return h.jsxs("svg", { className: ve(["react-flow__background", c]), style: {
    ...l,
    ...Jn,
    "--xy-background-color-props": a,
    "--xy-background-pattern-color-props": s
  }, ref: f, "data-testid": "rf__background", children: [h.jsx("pattern", { id: M, x: d[0] % S[0], y: d[1] % S[1], width: S[0], height: S[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${E[0]},-${E[1]})`, children: y ? h.jsx(dg, { radius: g / 2, className: u }) : h.jsx(ug, { dimensions: _, lineWidth: r, variant: t, className: u }) }), h.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${M})` })] });
}
ja.displayName = "Background";
const pg = we(ja);
function gg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: h.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function mg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: h.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function yg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: h.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function xg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: h.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function wg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: h.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function vn({ children: e, className: t, ...n }) {
  return h.jsx("button", { type: "button", className: ve(["react-flow__controls-button", t]), ...n, children: e });
}
const vg = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function $a({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: i, onZoomOut: s, onFitView: a, onInteractiveChange: l, className: c, children: u, position: f = "bottom-left", orientation: d = "vertical", "aria-label": m }) {
  const p = ge(), { isInteractive: y, minZoomReached: w, maxZoomReached: x, ariaLabelConfig: S } = le(vg, pe), { zoomIn: g, zoomOut: v, fitView: _ } = vr(), E = () => {
    g(), i?.();
  }, M = () => {
    v(), s?.();
  }, D = () => {
    _(r), a?.();
  }, P = () => {
    p.setState({
      nodesDraggable: !y,
      nodesConnectable: !y,
      elementsSelectable: !y
    }), l?.(!y);
  }, F = d === "horizontal" ? "horizontal" : "vertical";
  return h.jsxs(Qn, { className: ve(["react-flow__controls", F, c]), position: f, style: e, "data-testid": "rf__controls", "aria-label": m ?? S["controls.ariaLabel"], children: [t && h.jsxs(h.Fragment, { children: [h.jsx(vn, { onClick: E, className: "react-flow__controls-zoomin", title: S["controls.zoomIn.ariaLabel"], "aria-label": S["controls.zoomIn.ariaLabel"], disabled: x, children: h.jsx(gg, {}) }), h.jsx(vn, { onClick: M, className: "react-flow__controls-zoomout", title: S["controls.zoomOut.ariaLabel"], "aria-label": S["controls.zoomOut.ariaLabel"], disabled: w, children: h.jsx(mg, {}) })] }), n && h.jsx(vn, { className: "react-flow__controls-fitview", onClick: D, title: S["controls.fitView.ariaLabel"], "aria-label": S["controls.fitView.ariaLabel"], children: h.jsx(yg, {}) }), o && h.jsx(vn, { className: "react-flow__controls-interactive", onClick: P, title: S["controls.interactive.ariaLabel"], "aria-label": S["controls.interactive.ariaLabel"], children: y ? h.jsx(wg, {}) : h.jsx(xg, {}) }), u] });
}
$a.displayName = "Controls";
const bg = we($a);
function Sg({ id: e, x: t, y: n, width: o, height: r, style: i, color: s, strokeColor: a, strokeWidth: l, className: c, borderRadius: u, shapeRendering: f, selected: d, onClick: m }) {
  const { background: p, backgroundColor: y } = i || {}, w = s || p || y;
  return h.jsx("rect", { className: ve(["react-flow__minimap-node", { selected: d }, c]), x: t, y: n, rx: u, ry: u, width: o, height: r, style: {
    fill: w,
    stroke: a,
    strokeWidth: l
  }, shapeRendering: f, onClick: m ? (x) => m(x, e) : void 0 });
}
const Eg = we(Sg), _g = (e) => e.nodes.map((t) => t.id), Ro = (e) => e instanceof Function ? e : () => e;
function Ng({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: i = Eg,
  onClick: s
}) {
  const a = le(_g, pe), l = Ro(t), c = Ro(e), u = Ro(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return h.jsx(h.Fragment, { children: a.map((d) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    h.jsx(kg, { id: d, nodeColorFunc: l, nodeStrokeColorFunc: c, nodeClassNameFunc: u, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: i, onClick: s, shapeRendering: f }, d)
  )) });
}
function Cg({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: i, shapeRendering: s, NodeComponent: a, onClick: l }) {
  const { node: c, x: u, y: f, width: d, height: m } = le((p) => {
    const y = p.nodeLookup.get(e);
    if (!y)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const w = y.internals.userNode, { x, y: S } = y.internals.positionAbsolute, { width: g, height: v } = Je(w);
    return {
      node: w,
      x,
      y: S,
      width: g,
      height: v
    };
  }, pe);
  return !c || c.hidden || !Ps(c) ? null : h.jsx(a, { x: u, y: f, width: d, height: m, style: c.style, selected: !!c.selected, className: o(c), color: t(c), borderRadius: r, strokeColor: n(c), strokeWidth: i, shapeRendering: s, onClick: l, id: c.id });
}
const kg = we(Cg);
var Ig = we(Ng);
const Mg = 200, Ag = 150, Dg = (e) => !e.hidden, Pg = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? As(on(e.nodeLookup, { filter: Dg }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, jg = "react-flow__minimap-desc";
function Ta({
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
  maskStrokeWidth: f,
  position: d = "bottom-right",
  onClick: m,
  onNodeClick: p,
  pannable: y = !1,
  zoomable: w = !1,
  ariaLabel: x,
  inversePan: S,
  zoomStep: g = 1,
  offsetScale: v = 5
}) {
  const _ = ge(), E = ae(null), { boundingRect: M, viewBB: D, rfId: P, panZoom: F, translateExtent: C, flowWidth: R, flowHeight: H, ariaLabelConfig: b } = le(Pg, pe), k = e?.width ?? Mg, N = e?.height ?? Ag, A = M.width / k, T = M.height / N, j = Math.max(A, T), B = j * k, V = j * N, O = v * j, U = M.x - (B - M.width) / 2 - O, X = M.y - (V - M.height) / 2 - O, ee = B + O * 2, oe = V + O * 2, K = `${jg}-${P}`, $ = ae(0), Y = ae();
  $.current = j, ce(() => {
    if (E.current && F)
      return Y.current = Wf({
        domNode: E.current,
        panZoom: F,
        getTransform: () => _.getState().transform,
        getViewScale: () => $.current
      }), () => {
        Y.current?.destroy();
      };
  }, [F]), ce(() => {
    Y.current?.update({
      translateExtent: C,
      width: R,
      height: H,
      inversePan: S,
      pannable: y,
      zoomStep: g,
      zoomable: w
    });
  }, [y, w, S, g, C, R, H]);
  const ie = m ? (q) => {
    const [se, L] = Y.current?.pointer(q) || [0, 0];
    m(q, { x: se, y: L });
  } : void 0, te = p ? he((q, se) => {
    const L = _.getState().nodeLookup.get(se).internals.userNode;
    p(q, L);
  }, []) : void 0, G = x ?? b["minimap.ariaLabel"];
  return h.jsx(Qn, { position: d, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-background-color-props": typeof c == "string" ? c : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * j : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof s == "number" ? s : void 0
  }, className: ve(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: h.jsxs("svg", { width: k, height: N, viewBox: `${U} ${X} ${ee} ${oe}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": K, ref: E, onClick: ie, children: [G && h.jsx("title", { id: K, children: G }), h.jsx(Ig, { onClick: te, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: i, nodeClassName: r, nodeStrokeWidth: s, nodeComponent: a }), h.jsx("path", { className: "react-flow__minimap-mask", d: `M${U - O},${X - O}h${ee + O * 2}v${oe + O * 2}h${-ee - O * 2}z
        M${D.x},${D.y}h${D.width}v${D.height}h${-D.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
Ta.displayName = "MiniMap";
const $g = we(Ta), Tg = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, Rg = {
  [kt.Line]: "right",
  [kt.Handle]: "bottom-right"
};
function zg({ nodeId: e, position: t, variant: n = kt.Handle, className: o, style: r = void 0, children: i, color: s, minWidth: a = 10, minHeight: l = 10, maxWidth: c = Number.MAX_VALUE, maxHeight: u = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: d, autoScale: m = !0, shouldResize: p, onResizeStart: y, onResize: w, onResizeEnd: x }) {
  const S = ha(), g = typeof e == "string" ? e : S, v = ge(), _ = ae(null), E = n === kt.Handle, M = le(he(Tg(E && m), [E, m]), pe), D = ae(null), P = t ?? Rg[n];
  ce(() => {
    if (!(!_.current || !g))
      return D.current || (D.current = oh({
        domNode: _.current,
        nodeId: g,
        getStoreItems: () => {
          const { nodeLookup: C, transform: R, snapGrid: H, snapToGrid: b, nodeOrigin: k, domNode: N } = v.getState();
          return {
            nodeLookup: C,
            transform: R,
            snapGrid: H,
            snapToGrid: b,
            nodeOrigin: k,
            paneDomNode: N
          };
        },
        onChange: (C, R) => {
          const { triggerNodeChanges: H, nodeLookup: b, parentLookup: k, nodeOrigin: N } = v.getState(), A = [], T = { x: C.x, y: C.y }, j = b.get(g);
          if (j && j.expandParent && j.parentId) {
            const B = j.origin ?? N, V = C.width ?? j.measured.width ?? 0, O = C.height ?? j.measured.height ?? 0, U = {
              id: j.id,
              parentId: j.parentId,
              rect: {
                width: V,
                height: O,
                ...js({
                  x: C.x ?? j.position.x,
                  y: C.y ?? j.position.y
                }, { width: V, height: O }, j.parentId, b, B)
              }
            }, X = wr([U], b, k, N);
            A.push(...X), T.x = C.x ? Math.max(B[0] * V, C.x) : void 0, T.y = C.y ? Math.max(B[1] * O, C.y) : void 0;
          }
          if (T.x !== void 0 && T.y !== void 0) {
            const B = {
              id: g,
              type: "position",
              position: { ...T }
            };
            A.push(B);
          }
          if (C.width !== void 0 && C.height !== void 0) {
            const V = {
              id: g,
              type: "dimensions",
              resizing: !0,
              setAttributes: d ? d === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: C.width,
                height: C.height
              }
            };
            A.push(V);
          }
          for (const B of R) {
            const V = {
              ...B,
              type: "position"
            };
            A.push(V);
          }
          H(A);
        },
        onEnd: ({ width: C, height: R }) => {
          const H = {
            id: g,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: C,
              height: R
            }
          };
          v.getState().triggerNodeChanges([H]);
        }
      })), D.current.update({
        controlPosition: P,
        boundaries: {
          minWidth: a,
          minHeight: l,
          maxWidth: c,
          maxHeight: u
        },
        keepAspectRatio: f,
        resizeDirection: d,
        onResizeStart: y,
        onResize: w,
        onResizeEnd: x,
        shouldResize: p
      }), () => {
        D.current?.destroy();
      };
  }, [
    P,
    a,
    l,
    c,
    u,
    f,
    y,
    w,
    x,
    p
  ]);
  const F = P.split("-");
  return h.jsx("div", { className: ve(["react-flow__resize-control", "nodrag", ...F, n, o]), ref: _, style: {
    ...r,
    scale: M,
    ...s && { [E ? "backgroundColor" : "borderColor"]: s }
  }, children: i });
}
we(zg);
const Lg = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), Ra = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var Hg = {
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
const Vg = Bn(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: o,
    className: r = "",
    children: i,
    iconNode: s,
    ...a
  }, l) => Ho(
    "svg",
    {
      ref: l,
      ...Hg,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: Ra("lucide", r),
      ...a
    },
    [
      ...s.map(([c, u]) => Ho(c, u)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const ke = (e, t) => {
  const n = Bn(
    ({ className: o, ...r }, i) => Ho(Vg, {
      ref: i,
      iconNode: t,
      className: Ra(`lucide-${Lg(e)}`, o),
      ...r
    })
  );
  return n.displayName = `${e}`, n;
};
const Og = ke("Boxes", [
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
const eo = ke("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const Bg = ke("ChevronDown", [
  ["path", { d: "m6 9 6 6 6-6", key: "qrunsl" }]
]);
const Fg = ke("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const Cn = ke("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const Qt = ke("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const Wg = ke("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const Yg = ke("ListTree", [
  ["path", { d: "M21 12h-8", key: "1bmf0i" }],
  ["path", { d: "M21 6H8", key: "1pqkrb" }],
  ["path", { d: "M21 18h-8", key: "1tm79t" }],
  ["path", { d: "M3 6v4c0 1.1.9 2 2 2h3", key: "1ywdgy" }],
  ["path", { d: "M3 10v6c0 1.1.9 2 2 2h3", key: "2wc746" }]
]);
const za = ke("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const Sr = ke("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const Xg = ke("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const qg = ke("Save", [
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
const Zg = ke("Search", [
  ["circle", { cx: "11", cy: "11", r: "8", key: "4ej97u" }],
  ["path", { d: "m21 21-4.3-4.3", key: "1qie3q" }]
]);
const Mt = ke("Sparkles", [
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
const Jo = ke("Trash2", [
  ["path", { d: "M3 6h18", key: "d0wm0j" }],
  ["path", { d: "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", key: "4alrt4" }],
  ["path", { d: "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", key: "v07s0e" }],
  ["line", { x1: "10", x2: "10", y1: "11", y2: "17", key: "1uufr5" }],
  ["line", { x1: "14", x2: "14", y1: "11", y2: "17", key: "xtxkd" }]
]), Fe = "/_elsa/workflow-management";
async function Ug(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${Fe}/definitions?${n.toString()}`);
}
async function Kg(e, t) {
  return e.http.getJson(`${Fe}/definitions/${encodeURIComponent(t)}`);
}
async function Gg(e, t) {
  return e.http.postJson(`${Fe}/definitions`, t);
}
async function Qg(e, t) {
  await e.http.deleteJson(`${Fe}/definitions/${encodeURIComponent(t)}`);
}
async function Jg(e, t) {
  await e.http.postJson(`${Fe}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function em(e, t) {
  await e.http.deleteJson(`${Fe}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function tm(e, t) {
  return e.http.putJson(`${Fe}/drafts/${encodeURIComponent(t.id)}`, { state: t.state, layout: t.layout });
}
async function nm(e, t) {
  return e.http.postJson(`${Fe}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function om(e, t) {
  return e.http.postJson(`${Fe}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function La(e, t) {
  return e.http.postJson(`${Fe}/executables/${encodeURIComponent(t)}/run`, {});
}
async function rm(e) {
  return e.http.getJson("/_demo/workflows/executables");
}
async function Ha(e) {
  return e.http.getJson(`${Fe}/activities`);
}
const to = "elsa.sequence.structure", an = "elsa.flowchart.structure";
function Va(e, t) {
  if (!e) return null;
  let n = e;
  for (const o of t) {
    const r = Ge(n).find((s) => s.id === o.slotId);
    if (!r) return null;
    const i = r.activities.find((s) => s.nodeId === o.ownerNodeId);
    if (!i) return null;
    n = i;
  }
  return n;
}
function zo(e, t) {
  const n = Va(e, t);
  if (!n) return null;
  let o = Ge(n)[0];
  return o ? { owner: n, slot: o } : null;
}
function Ge(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = _m(t), r = Lo(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: Nm(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, i]) => Lo(i)).map(([i, s]) => ({
    id: `${t.kind}:${i}`,
    label: km(i),
    property: i,
    mode: "generic",
    activities: Lo(s) ?? []
  }));
}
function im(e, t, n) {
  const o = new Map(t.map((s) => [s.activityVersionId, s])), r = new Map(n.map((s) => [s.nodeId, s])), i = e.slot.activities.map((s, a) => {
    const l = o.get(s.activityVersionId), c = r.get(s.nodeId) ?? Cm(e.slot.mode, a);
    return Ba(s, l, { x: c.x, y: c.y });
  });
  return {
    nodes: i,
    edges: e.slot.mode === "flowchart" ? ym(e.owner) : mm(e.slot, i)
  };
}
function sm(e, t, n) {
  const o = t.find((i) => i.activityVersionId === e.activityVersionId), r = n.find((i) => i.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [Ba(e, o, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function am(e, t) {
  return e?.structure?.kind === an || dm(t) ? "flowchart" : e?.structure?.kind === to || fm(t) ? "sequence" : "unsupported";
}
function er(e, t, n) {
  if (t.length === 0) {
    const a = Ge(e)[0];
    return a ? Hn(e, a, n) : e;
  }
  const [o, ...r] = t, i = Ge(e).find((a) => a.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((a) => a.nodeId === o.ownerNodeId ? er(a, r, n) : a);
  return Hn(e, i, s);
}
function Oa(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, i = Ge(e).find((a) => a.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((a) => a.nodeId === o.ownerNodeId ? Oa(a, r, n) : a);
  return Hn(e, i, s);
}
function Hn(e, t, n) {
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
function cm(e, t, n, o = []) {
  const r = new Map(e.slot.activities.map((s) => [s.nodeId, s]));
  for (const s of o)
    r.set(s.nodeId, s);
  const i = t.map((s) => r.get(s.id)).filter((s) => !!s);
  return e.slot.mode === "sequence" && i.sort((s, a) => {
    const l = t.find((u) => u.id === s.nodeId), c = t.find((u) => u.id === a.nodeId);
    return (l?.position.x ?? 0) - (c?.position.x ?? 0);
  }), Hn(e.owner, e.slot, i);
}
function lm(e, t) {
  return {
    ...e,
    structure: gm(e.structure, t)
  };
}
function um(e, t) {
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
function Vi(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: pm(e)
  };
}
function Se(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? hm(t) : n;
}
function Ba(e, t, n, o = {}) {
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
      childSlots: Ge(e),
      acceptsInbound: xm(e, t),
      sourcePorts: o.suppressFlowPorts ? [] : Fa(e, t),
      suppressFlowPorts: o.suppressFlowPorts
    }
  };
}
function dm(e) {
  return !!e && (Se(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function fm(e) {
  return !!e && (Se(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function hm(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function pm(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: to,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: an,
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
function gm(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], o = /* @__PURE__ */ new Map();
  for (const r of n) {
    if (!Er(r)) continue;
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
function mm(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function ym(e) {
  if (e.structure?.kind !== an) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, i = n.target;
    if (!r?.nodeId || !i?.nodeId) return null;
    const s = Array.isArray(n.vertices) ? n.vertices.filter(Sm) : [];
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
function Fa(e, t) {
  const n = Oi(e.cases);
  if (vm(e, t) && n.length > 0)
    return [...n.map((i) => ({ name: i, displayName: i })), { name: "Default", displayName: "Default" }];
  const o = [
    ...kn(t?.designFacets),
    ...kn(t?.ports),
    ...kn(t?.outputs)
  ];
  if (o.length > 0) return bm(o);
  const r = Oi(e.outcomes);
  return r.length > 0 ? r.map((i) => ({ name: i, displayName: i })) : [{ name: "Done", displayName: "Done" }];
}
function xm(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function Vn(e, t, n, o) {
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
function wm(e, t, n) {
  const o = Vn(t.source, n, t.sourceHandle ?? "Done", void 0), r = Vn(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((i) => i.id !== t.id).concat(o, r);
}
function Lo(e) {
  return Array.isArray(e) ? e.filter(Em) : null;
}
function vm(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function kn(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!Er(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...kn(n.ports));
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
function bm(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function Oi(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function Sm(e) {
  return Er(e) && typeof e.x == "number" && typeof e.y == "number";
}
function Er(e) {
  return typeof e == "object" && e !== null;
}
function Em(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function _m(e) {
  return e.kind === to ? "sequence" : e.kind === an ? "flowchart" : "generic";
}
function Nm(e) {
  return e.kind === to || e.kind === an, "Activities";
}
function Cm(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function km(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Im = { workflowActivity: Km }, Mm = { workflow: Gm }, Bi = "application/x-elsa-activity-version-id", Am = 6, Dm = 1200, Pm = [10, 25, 50], jm = 10, Wa = it.createContext(null);
function r0(e) {
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
        component: () => /* @__PURE__ */ h.jsx($m, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ h.jsx(Tm, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow instances",
        component: () => /* @__PURE__ */ h.jsx(Rm, { ai: e.ai })
      }
    ]
  });
}
function $m({ context: e, ai: t }) {
  const [n, o] = re(Fi);
  ce(() => {
    const i = () => o(Fi());
    return window.addEventListener("popstate", i), () => window.removeEventListener("popstate", i);
  }, []);
  const r = (i) => {
    const s = i ? `/workflows/definitions?definition=${encodeURIComponent(i)}` : "/workflows/definitions";
    window.history.pushState({}, "", s), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return n ? /* @__PURE__ */ h.jsx(Um, { context: e, definitionId: n, ai: t, onBack: () => r(null) }) : /* @__PURE__ */ h.jsx(_r, { activePath: "/workflows/definitions", title: "Definitions", children: /* @__PURE__ */ h.jsx(zm, { context: e, ai: t, onOpen: r }) });
}
function Tm({ context: e, ai: t }) {
  const [n, o] = re(Wi);
  return ce(() => {
    const r = () => o(Wi());
    return window.addEventListener("popstate", r), () => window.removeEventListener("popstate", r);
  }, []), /* @__PURE__ */ h.jsx(_r, { activePath: "/workflows/executables", title: "Executables", children: /* @__PURE__ */ h.jsx(Hm, { context: e, ai: t, definitionFilter: n }) });
}
function Rm({ ai: e }) {
  const t = At(e, "weaver.workflows.explain-instance");
  return /* @__PURE__ */ h.jsx(_r, { activePath: "/workflows/instances", title: "Instances", children: /* @__PURE__ */ h.jsxs("div", { className: "wf-empty", children: [
    "Workflow instance history will appear here when the runtime exposes an instance query endpoint.",
    t ? /* @__PURE__ */ h.jsxs("button", { type: "button", className: "wf-ai-inline-action", onClick: () => Dt(e, t, { scope: "workflow-instances" }), children: [
      /* @__PURE__ */ h.jsx(Mt, { size: 13 }),
      " Ask Weaver about instances"
    ] }) : null
  ] }) });
}
function _r({ activePath: e, title: t, children: n }) {
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
function Fi() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Wi() {
  return new URLSearchParams(window.location.search).get("definition");
}
function zm({ context: e, ai: t, onOpen: n }) {
  const [o, r] = re(""), [i, s] = re("active"), [a, l] = re(1), [c, u] = re(jm), [f, d] = re("loading"), [m, p] = re(""), [y, w] = re(""), [x, S] = re([]), [g, v] = re(0), [_, E] = re(() => /* @__PURE__ */ new Set()), [M, D] = re(null), [P, F] = re(!1), [C, R] = re([]), [H, b] = re("idle"), k = ae(null), N = me(() => x.map((L) => L.id), [x]), A = At(t, "weaver.workflows.suggest-create-metadata"), T = At(t, "weaver.workflows.explain-definition"), j = N.filter((L) => _.has(L)).length, B = N.length > 0 && j === N.length, V = he(async () => {
    d("loading"), p("");
    try {
      const L = await Ug(e, { search: o, state: i, page: a, pageSize: c }), Q = typeof L.totalCount == "number", fe = L.totalCount ?? L.definitions.length, ye = Ya(fe, c);
      if (fe > 0 && a > ye) {
        l(ye);
        return;
      }
      S(Q ? L.definitions : Om(L.definitions, a, c)), v(fe), d("ready");
    } catch (L) {
      p(L instanceof Error ? L.message : String(L)), d("failed");
    }
  }, [e, o, i, a, c]);
  ce(() => {
    V();
  }, [V]), ce(() => {
    k.current && (k.current.indeterminate = j > 0 && !B);
  }, [B, j]);
  const O = he(async () => {
    if (!(H === "loading" || H === "ready")) {
      b("loading");
      try {
        const L = await Ha(e);
        R(L.activities ?? []), b("ready");
      } catch (L) {
        b("failed"), p(L instanceof Error ? L.message : String(L));
      }
    }
  }, [H, e]), U = () => {
    p(""), w(""), D({ name: "", description: "", rootKind: "flowchart" }), O();
  }, X = async () => {
    if (M?.name.trim()) {
      F(!0), p(""), w("");
      try {
        const L = await Gg(e, {
          name: M.name.trim(),
          description: M.description.trim() || null,
          rootKind: M.rootKind,
          rootActivityVersionId: Wm(M, C)
        });
        D(null), n(L.definition.id);
      } catch (L) {
        p(L instanceof Error ? L.message : String(L));
      } finally {
        F(!1);
      }
    }
  }, ee = (L) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(L)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, oe = async () => {
    if (x.length === 1 && a > 1) {
      l(a - 1);
      return;
    }
    await V();
  }, K = () => E(/* @__PURE__ */ new Set()), $ = (L, Q) => {
    E((fe) => {
      const ye = new Set(fe);
      return Q ? ye.add(L) : ye.delete(L), ye;
    });
  }, Y = (L) => {
    E((Q) => {
      const fe = new Set(Q);
      for (const ye of N)
        L ? fe.add(ye) : fe.delete(ye);
      return fe;
    });
  }, ie = (L) => {
    s(L), l(1), K();
  }, te = (L) => {
    r(L), l(1), K();
  }, G = async (L) => {
    if (window.confirm(`Delete workflow definition "${L.name}"? You can restore it from the Deleted view.`)) {
      w(""), p("");
      try {
        await Qg(e, L.id), $(L.id, !1), w(`Deleted ${L.name}`), await oe();
      } catch (Q) {
        p(Q instanceof Error ? Q.message : String(Q));
      }
    }
  }, q = async (L) => {
    w(""), p("");
    try {
      await Jg(e, L.id), $(L.id, !1), w(`Restored ${L.name}`), await oe();
    } catch (Q) {
      p(Q instanceof Error ? Q.message : String(Q));
    }
  }, se = async (L) => {
    if (window.confirm(`Permanently delete workflow definition "${L.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`)) {
      w(""), p("");
      try {
        await em(e, L.id), $(L.id, !1), w(`Permanently deleted ${L.name}`), await oe();
      } catch (Q) {
        p(Q instanceof Error ? Q.message : String(Q));
      }
    }
  };
  return /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
    /* @__PURE__ */ h.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ h.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ h.jsx("button", { type: "button", className: i === "active" ? "active" : "", "aria-selected": i === "active", onClick: () => ie("active"), children: "Active" }),
        /* @__PURE__ */ h.jsx("button", { type: "button", className: i === "deleted" ? "active" : "", "aria-selected": i === "deleted", onClick: () => ie("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ h.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ h.jsx(Zg, { size: 15 }),
        /* @__PURE__ */ h.jsx("input", { value: o, onChange: (L) => te(L.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
        V();
      }, children: "Refresh" }),
      /* @__PURE__ */ h.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ h.jsxs("button", { type: "button", title: "Create workflow", onClick: U, children: [
        /* @__PURE__ */ h.jsx(Sr, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Qt, { size: 16 }),
      " ",
      m
    ] }) : null,
    f !== "failed" && m ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Qt, { size: 16 }),
      " ",
      m
    ] }) : null,
    y ? /* @__PURE__ */ h.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ h.jsx(eo, { size: 14 }),
      " ",
      y
    ] }) : null,
    _.size > 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ h.jsxs("span", { children: [
        _.size,
        " selected"
      ] }),
      /* @__PURE__ */ h.jsx("button", { type: "button", onClick: K, children: "Clear selection" })
    ] }) : null,
    f === "loading" ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: "Loading workflow definitions..." }) : null,
    f === "ready" && x.length === 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-empty", children: [
      "No ",
      i,
      " workflow definitions found."
    ] }) : null,
    f === "ready" && x.length > 0 ? /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
      /* @__PURE__ */ h.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ h.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ h.jsx(
            "input",
            {
              ref: k,
              type: "checkbox",
              checked: B,
              onChange: (L) => Y(L.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ h.jsx("span", { children: "Name" }),
          /* @__PURE__ */ h.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ h.jsx("span", { children: i === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ h.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ h.jsx("span", { children: "Actions" })
        ] }),
        x.map((L) => /* @__PURE__ */ h.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${L.name}`,
            "aria-selected": _.has(L.id),
            tabIndex: 0,
            onClick: () => n(L.id),
            onKeyDown: (Q) => {
              Q.currentTarget === Q.target && (Q.key !== "Enter" && Q.key !== " " || (Q.preventDefault(), n(L.id)));
            },
            children: [
              /* @__PURE__ */ h.jsx("label", { className: "wf-row-select", onClick: (Q) => Q.stopPropagation(), children: /* @__PURE__ */ h.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: _.has(L.id),
                  onChange: (Q) => $(L.id, Q.target.checked),
                  "aria-label": `Select workflow definition ${L.name}`
                }
              ) }),
              /* @__PURE__ */ h.jsxs("span", { children: [
                /* @__PURE__ */ h.jsx("strong", { children: L.name }),
                /* @__PURE__ */ h.jsx("small", { children: L.description || L.id })
              ] }),
              /* @__PURE__ */ h.jsx("span", { children: L.latestVersion ?? "No version" }),
              /* @__PURE__ */ h.jsx("span", { children: i === "deleted" ? tr(L.deletedAt) : L.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ h.jsx("span", { children: tr(L.lastModifiedAt) }),
              /* @__PURE__ */ h.jsx("span", { className: "wf-row-actions", onClick: (Q) => Q.stopPropagation(), children: i === "active" ? /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
                /* @__PURE__ */ h.jsx("button", { type: "button", onClick: (Q) => {
                  Q.stopPropagation(), n(L.id);
                }, children: "Open" }),
                /* @__PURE__ */ h.jsx("button", { type: "button", onClick: (Q) => {
                  Q.stopPropagation(), ee(L.id);
                }, children: "Artifacts" }),
                T ? /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => Dt(t, T, L), children: [
                  /* @__PURE__ */ h.jsx(Mt, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ h.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  G(L);
                }, children: [
                  /* @__PURE__ */ h.jsx(Jo, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
                /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
                  q(L);
                }, children: [
                  /* @__PURE__ */ h.jsx(Xg, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ h.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  se(L);
                }, children: [
                  /* @__PURE__ */ h.jsx(Jo, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          L.id
        ))
      ] }),
      /* @__PURE__ */ h.jsx(
        Vm,
        {
          page: a,
          pageSize: c,
          totalCount: g,
          onPageChange: l,
          onPageSizeChange: (L) => {
            u(L), l(1);
          }
        }
      )
    ] }) : null,
    M ? /* @__PURE__ */ h.jsx(
      Lm,
      {
        draft: M,
        activities: C,
        catalogState: H,
        creating: P,
        suggestMetadataAction: A,
        onSuggestMetadata: A ? () => Dt(t, A, { draft: M, activities: C }) : void 0,
        onChange: (L) => D(L),
        onClose: () => D(null),
        onSubmit: X
      }
    ) : null
  ] });
}
function Lm({ draft: e, activities: t, catalogState: n, creating: o, suggestMetadataAction: r, onSuggestMetadata: i, onChange: s, onClose: a, onSubmit: l }) {
  const c = me(() => Bm(t), [t]), u = Fm(e, t), f = (d) => {
    if (d.startsWith("kind:")) {
      s({ ...e, rootKind: d.slice(5), rootActivityVersionId: null });
      return;
    }
    const m = t.find((p) => p.activityVersionId === d);
    s({
      ...e,
      rootKind: Xa(m) ?? e.rootKind,
      rootActivityVersionId: d
    });
  };
  return /* @__PURE__ */ h.jsx("div", { className: "wf-dialog-backdrop", role: "presentation", children: /* @__PURE__ */ h.jsx("section", { className: "wf-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "workflow-create-title", children: /* @__PURE__ */ h.jsxs(
    "form",
    {
      onSubmit: (d) => {
        d.preventDefault(), l();
      },
      children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-dialog-heading", children: [
          /* @__PURE__ */ h.jsx("h3", { id: "workflow-create-title", children: "Create Workflow" }),
          r ? /* @__PURE__ */ h.jsxs("button", { type: "button", className: "wf-ai-action", onClick: i, title: r.description ?? r.label, children: [
            /* @__PURE__ */ h.jsx(Mt, { size: 13 }),
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
              onChange: (d) => s({ ...e, name: d.target.value })
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
              onChange: (d) => s({ ...e, description: d.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ h.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ h.jsx("span", { children: "Root activity" }),
          /* @__PURE__ */ h.jsxs(
            "select",
            {
              "aria-label": "Root activity",
              value: u,
              onChange: (d) => f(d.target.value),
              disabled: n === "loading",
              children: [
                /* @__PURE__ */ h.jsx("optgroup", { label: "Composite roots", children: c.compositeRoots.map((d) => /* @__PURE__ */ h.jsx("option", { value: d.value, children: d.label }, d.value)) }),
                c.otherCategories.map((d) => /* @__PURE__ */ h.jsx("optgroup", { label: d.name, children: d.activities.map((m) => /* @__PURE__ */ h.jsx("option", { value: m.activityVersionId, children: Se(m) }, m.activityVersionId)) }, d.name))
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
function Hm({ context: e, ai: t, definitionFilter: n }) {
  const [o, r] = re("loading"), [i, s] = re(""), [a, l] = re(""), [c, u] = re([]), f = me(
    () => n ? c.filter((y) => y.definitionId === n || y.sourceId === n) : c,
    [n, c]
  ), d = At(t, "weaver.workflows.explain-executable"), m = he(async () => {
    r("loading"), s("");
    try {
      u(await rm(e)), r("ready");
    } catch (y) {
      s(y instanceof Error ? y.message : String(y)), r("failed");
    }
  }, [e]);
  ce(() => {
    m();
  }, [m]);
  const p = async (y) => {
    l(""), s("");
    try {
      await La(e, y.artifactId), l(`Started ${y.artifactId}`);
    } catch (w) {
      s(w instanceof Error ? w.message : String(w));
    }
  };
  return /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
    /* @__PURE__ */ h.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
        m();
      }, children: "Refresh" }),
      n ? /* @__PURE__ */ h.jsxs("span", { className: "wf-filter-chip", children: [
        "Definition ",
        n
      ] }) : null
    ] }),
    o === "failed" ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Qt, { size: 16 }),
      " ",
      i
    ] }) : null,
    a ? /* @__PURE__ */ h.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ h.jsx(eo, { size: 14 }),
      " ",
      a
    ] }) : null,
    o === "loading" ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: "Loading workflow executables..." }) : null,
    o === "ready" && f.length === 0 ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: n ? "No workflow executables found for this definition." : "No workflow executables found. Publish a workflow definition to create one." }) : null,
    o === "ready" && f.length > 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ h.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ h.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ h.jsx("span", { children: "Version" }),
        /* @__PURE__ */ h.jsx("span", { children: "Source" }),
        /* @__PURE__ */ h.jsx("span", { children: "Root" }),
        /* @__PURE__ */ h.jsx("span", { children: "Published" }),
        /* @__PURE__ */ h.jsx("span", { children: "Actions" })
      ] }),
      f.map((y) => /* @__PURE__ */ h.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ h.jsxs("span", { children: [
          /* @__PURE__ */ h.jsx("strong", { children: y.artifactId }),
          /* @__PURE__ */ h.jsx("small", { children: y.artifactHash })
        ] }),
        /* @__PURE__ */ h.jsx("span", { children: y.artifactVersion }),
        /* @__PURE__ */ h.jsx("span", { children: Xm(y) }),
        /* @__PURE__ */ h.jsx("span", { children: qm(y) }),
        /* @__PURE__ */ h.jsx("span", { children: tr(y.publishedAt ?? y.createdAt) }),
        /* @__PURE__ */ h.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
            p(y);
          }, children: [
            /* @__PURE__ */ h.jsx(za, { size: 13 }),
            " Run"
          ] }),
          d ? /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => Dt(t, d, y), children: [
            /* @__PURE__ */ h.jsx(Mt, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, y.artifactId))
    ] }) : null
  ] });
}
function Vm({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const i = Ya(n, t), s = n === 0 ? 0 : (e - 1) * t + 1, a = Math.min(e * t, n);
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
      /* @__PURE__ */ h.jsx("select", { value: t, onChange: (l) => r(Number(l.target.value)), children: Pm.map((l) => /* @__PURE__ */ h.jsx("option", { value: l, children: l }, l)) })
    ] }),
    /* @__PURE__ */ h.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ h.jsx(Fg, { size: 14 }),
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
        /* @__PURE__ */ h.jsx(Cn, { size: 14 })
      ] })
    ] })
  ] });
}
function Om(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function Ya(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function At(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function Dt(e, t, n) {
  const o = t.createPrompt(n);
  o && e.dispatchPrompt(o);
}
function Bm(e) {
  const t = On(e, "flowchart"), n = On(e, "sequence"), o = [
    { value: t?.activityVersionId ?? "kind:flowchart", label: "Flowchart" },
    { value: n?.activityVersionId ?? "kind:sequence", label: "Sequence" }
  ], r = /* @__PURE__ */ new Map();
  for (const s of e.filter(Ka)) {
    if (Ym(s)) continue;
    const a = s.category || "Uncategorized";
    r.set(a, [...r.get(a) ?? [], s]);
  }
  const i = Array.from(r.entries()).sort(([s], [a]) => s.localeCompare(a)).map(([s, a]) => ({
    name: s,
    activities: a.sort((l, c) => Se(l).localeCompare(Se(c)))
  }));
  return { compositeRoots: o, otherCategories: i };
}
function Fm(e, t) {
  return e.rootActivityVersionId ?? On(t, e.rootKind)?.activityVersionId ?? `kind:${e.rootKind}`;
}
function Wm(e, t) {
  return e.rootActivityVersionId ?? On(t, e.rootKind)?.activityVersionId ?? null;
}
function On(e, t) {
  return e.find((n) => Xa(n) === t);
}
function Xa(e) {
  return e ? Za(e) ? "flowchart" : Ua(e) ? "sequence" : null : null;
}
function qa(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "Uncategorized";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [o]) => n.localeCompare(o)).map(([n, o]) => ({
    category: n,
    activities: o.sort((r, i) => Se(r).localeCompare(Se(i)))
  }));
}
function Ym(e) {
  return Za(e) || Ua(e);
}
function Za(e) {
  return Se(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function Ua(e) {
  return Se(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function Ka(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function Xm(e) {
  return e.sourceKind || e.sourceId || e.sourceVersion ? [e.sourceKind, e.sourceId, e.sourceVersion].filter(Boolean).join(" / ") : e.definitionId;
}
function qm(e) {
  return Zm(e.rootActivityType) || e.rootActivityType;
}
function Zm(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function Um({ context: e, definitionId: t, ai: n, onBack: o }) {
  const [r, i] = re(null), [s, a] = re(null), [l, c] = re([]), [u, f] = re([]), [d, m] = re([]), [p, y] = re([]), [w, x] = re(null), [S, g] = re(null), [v, _] = re(null), [E, M] = re(null), [D, P] = re(""), [F, C] = re(""), [R, H] = re(!1), [b, k] = re(null), [N, A] = re(() => /* @__PURE__ */ new Set()), T = ae(null), j = ae(null), B = ae(""), V = ae(0), O = ae(null), U = ae(!1), X = s?.state.rootActivity ?? null, ee = me(() => new Map(l.map((I) => [I.activityVersionId, I])), [l]), oe = me(() => Va(X, u), [X, u]), K = am(oe, oe ? ee.get(oe.activityVersionId) : void 0), $ = !!oe && K === "unsupported", Y = me(() => $ ? null : zo(X, u), [X, u, $]), ie = me(() => qa(l), [l]), te = me(() => $ && oe?.nodeId === S ? oe : Y?.slot.activities.find((I) => I.nodeId === S) ?? null, [$, Y, oe, S]), G = te ? Ge(te) : [], q = K === "flowchart" && Y?.slot.mode === "flowchart", se = !X || !$, L = At(n, "weaver.workflows.find-draft-risks"), Q = At(n, "weaver.workflows.propose-update"), fe = he(async () => {
    P("");
    const [I, z] = await Promise.all([
      Kg(e, t),
      Ha(e)
    ]), W = I.draft ?? null;
    i(I), B.current = W ? Lt(W) : "", a(W), c(z.activities ?? []), f([]), g(null);
  }, [e, t]);
  ce(() => {
    fe().catch((I) => P(I instanceof Error ? I.message : String(I)));
  }, [fe]), ce(() => {
    A((I) => {
      let z = !1;
      const W = new Set(I);
      for (const Z of ie)
        W.has(Z.category) || (W.add(Z.category), z = !0);
      return z ? W : I;
    });
  }, [ie]), ce(() => {
    if (!oe) {
      m([]), y([]);
      return;
    }
    const I = $ ? sm(oe, l, s?.layout ?? []) : Y ? im(Y, l, s?.layout ?? []) : { nodes: [], edges: [] };
    m(I.nodes), y(I.edges);
  }, [l, s?.layout, $, Y, oe]);
  const ye = (I) => {
    a((z) => z && { ...z, state: { ...z.state, rootActivity: I } });
  }, Ie = he((I, z) => {
    if (s?.state.rootActivity && $)
      return;
    const W = Vi(I, Yi(I));
    if (!s?.state.rootActivity) {
      ye(W), g(W.nodeId);
      return;
    }
    if (!Y) {
      if (!Ge(W)[0]) {
        C(""), P("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      a((ne) => {
        if (!ne?.state.rootActivity) return ne;
        const de = ne.state.rootActivity, ue = er(W, [], [de]), be = z ? [
          ...ne.layout.filter((Me) => Me.nodeId !== de.nodeId),
          {
            nodeId: de.nodeId,
            x: Math.round(z.x),
            y: Math.round(z.y)
          }
        ] : ne.layout;
        return {
          ...ne,
          layout: be,
          state: {
            ...ne.state,
            rootActivity: ue
          }
        };
      }), g(s.state.rootActivity.nodeId), P(""), C(`Wrapped root in ${Se(I)}`);
      return;
    }
    a((Z) => {
      if (!Z?.state.rootActivity) return Z;
      const ne = zo(Z.state.rootActivity, u);
      if (!ne) return Z;
      const de = er(Z.state.rootActivity, u, [...ne.slot.activities, W]), ue = z ? [
        ...Z.layout.filter((be) => be.nodeId !== W.nodeId),
        {
          nodeId: W.nodeId,
          x: Math.round(z.x),
          y: Math.round(z.y)
        }
      ] : Z.layout;
      return {
        ...Z,
        layout: ue,
        state: {
          ...Z.state,
          rootActivity: de
        }
      };
    }), g(W.nodeId);
  }, [s?.state.rootActivity, u, $, Y]), Ne = he((I, z) => {
    const W = Vi(I, Yi(I)), Z = {
      id: W.nodeId,
      type: "workflowActivity",
      position: z,
      selected: !0,
      data: {
        label: Se(I),
        activityVersionId: I.activityVersionId,
        activityTypeKey: I.activityTypeKey,
        childSlots: Ge(W),
        acceptsInbound: String(I.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: Fa(W, I)
      }
    };
    return { activityNode: W, node: Z };
  }, []), xe = he((I, z, W = []) => {
    $ || a((Z) => {
      if (!Z) return Z;
      const ne = um(Z.layout, I), de = Z.state.rootActivity;
      if (!de) return { ...Z, layout: ne };
      const ue = zo(de, u);
      if (!ue) return { ...Z, layout: ne };
      const be = cm(ue, I, z, W), Me = ue.slot.mode === "flowchart" ? lm(be, z) : be;
      return {
        ...Z,
        layout: ne,
        state: {
          ...Z.state,
          rootActivity: Oa(de, u, Me)
        }
      };
    });
  }, [u, $]), qe = he((I, z) => {
    if (!T.current) return null;
    const W = T.current.getBoundingClientRect();
    return w ? w.screenToFlowPosition({ x: I, y: z }) : {
      x: I - W.left,
      y: z - W.top
    };
  }, [w]), $e = he((I, z) => document.elementFromPoint(I, z)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), Te = he((I, z, W) => {
    const Z = d.find((Ce) => Ce.id === z.source), ne = d.find((Ce) => Ce.id === z.target), de = Z && ne ? e0(Z, ne) : Z ? Xi(Z) : W, ue = Ne(I, de), Me = [...d.map((Ce) => Ce.selected ? { ...Ce, selected: !1 } : Ce), ue.node], et = wm(p, z, ue.node.id);
    m(Me), y(et), g(ue.node.id), xe(Me, et, [ue.activityNode]);
  }, [xe, Ne, p, d]), Ee = he((I, z, W) => {
    if (!se || !T.current) return !1;
    const Z = T.current.getBoundingClientRect();
    if (!(z >= Z.left && z <= Z.right && W >= Z.top && W <= Z.bottom)) return !1;
    const de = qe(z, W);
    if (!de) return !1;
    if (q) {
      const ue = $e(z, W), be = ue ? p.find((Me) => Me.id === ue) : void 0;
      if (be)
        return Te(I, be, de), !0;
    }
    return Ie(I, de), !0;
  }, [Ie, se, p, $e, q, Te, qe]);
  ce(() => {
    const I = (W) => {
      const Z = O.current;
      if (!Z) return;
      Math.hypot(W.clientX - Z.startX, W.clientY - Z.startY) >= Am && (Z.dragging = !0);
    }, z = (W) => {
      const Z = O.current;
      if (O.current = null, !Z?.dragging || !T.current) return;
      const ne = T.current.getBoundingClientRect();
      W.clientX >= ne.left && W.clientX <= ne.right && W.clientY >= ne.top && W.clientY <= ne.bottom && (U.current = !0, window.setTimeout(() => {
        U.current = !1;
      }, 0), Ee(Z.activity, W.clientX, W.clientY));
    };
    return window.addEventListener("pointermove", I), window.addEventListener("pointerup", z), window.addEventListener("pointercancel", z), () => {
      window.removeEventListener("pointermove", I), window.removeEventListener("pointerup", z), window.removeEventListener("pointercancel", z);
    };
  }, [w, Ee]);
  const Re = (I, z) => {
    I.dataTransfer.setData(Bi, z.activityVersionId), I.dataTransfer.setData("text/plain", z.activityVersionId), I.dataTransfer.effectAllowed = "copy";
  }, ze = (I, z) => {
    I.clientX === 0 && I.clientY === 0 || Ee(z, I.clientX, I.clientY) && (U.current = !0, window.setTimeout(() => {
      U.current = !1;
    }, 0));
  }, Ze = (I, z) => {
    I.button === 0 && (O.current = {
      activity: z,
      startX: I.clientX,
      startY: I.clientY,
      dragging: !1
    });
  }, no = (I) => {
    U.current || se && Ie(I);
  }, gt = (I) => {
    if (!se) {
      I.dataTransfer.dropEffect = "none";
      return;
    }
    if (I.preventDefault(), I.dataTransfer.dropEffect = "copy", !q) return;
    const z = $e(I.clientX, I.clientY);
    M(z);
  }, cn = (I) => {
    if (!T.current) return;
    const z = I.relatedTarget;
    z && T.current.contains(z) || M(null);
  }, ln = (I) => {
    if (I.preventDefault(), M(null), !se) return;
    const z = I.dataTransfer.getData(Bi) || I.dataTransfer.getData("text/plain"), W = ee.get(z);
    W && Ee(W, I.clientX, I.clientY);
  }, oo = () => {
    if (!q) return;
    const I = T.current?.getBoundingClientRect();
    I && _({
      kind: "fromEmpty",
      clientX: I.left + I.width / 2,
      clientY: I.top + I.height / 2
    });
  }, jt = he(async (I, z) => {
    const W = ++V.current, Z = Lt(I);
    P("");
    try {
      const ne = await tm(e, I), de = Lt(ne);
      B.current = de, a((ue) => !ue || ue.id !== ne.id ? ue : Lt(ue) === Z ? ne : { ...ue, validationErrors: ne.validationErrors }), W === V.current && C(z);
    } catch (ne) {
      W === V.current && (C(""), P(ne instanceof Error ? ne.message : String(ne)));
    }
  }, [e]);
  ce(() => {
    if (!R || !s || Lt(s) === B.current) return;
    C("Autosaving...");
    const z = window.setTimeout(() => {
      jt(s, "Autosaved");
    }, Dm);
    return () => window.clearTimeout(z);
  }, [R, s, jt]);
  const ro = async () => {
    s && (C("Saving..."), await jt(s, "Saved"));
  }, io = async () => {
    if (s) {
      C("Promoting...");
      try {
        const I = await nm(e, s.id), z = await om(e, I.versionId);
        k(z.artifactId), C(`Published ${z.artifactVersion}`), await fe();
      } catch (I) {
        C(""), P(I instanceof Error ? I.message : String(I));
      }
    }
  }, so = async () => {
    if (b) {
      C("Running...");
      try {
        await La(e, b), C("Run dispatched");
      } catch (I) {
        C(""), P(I instanceof Error ? I.message : String(I));
      }
    }
  }, ao = (I) => {
    const z = $ ? I.filter((W) => W.type === "select") : I;
    z.length !== 0 && m((W) => ia(z, W));
  }, co = (I) => {
    $ || y((z) => sa(I, z));
  }, $t = (I) => !I.source || !I.target || I.source === I.target || !q ? !1 : !I.targetHandle, lo = (I) => {
    if (!s?.state.rootActivity || !Y || !q || !$t(I)) return;
    const z = Vn(I.source, I.target, I.sourceHandle ?? "Done", I.targetHandle ?? void 0), W = ca(z, p);
    y(W), xe(d, W);
  }, uo = () => {
    xe(d, p);
  }, fo = (I, z) => {
    if (!z.nodeId || z.handleType === "target") {
      j.current = null;
      return;
    }
    j.current = {
      nodeId: z.nodeId,
      handleId: z.handleId ?? null
    };
  }, ho = (I) => {
    const z = j.current;
    if (j.current = null, !z || !q || I.target?.closest(".react-flow__handle, .react-flow__node")) return;
    const Z = t0(I);
    _({
      kind: "fromPort",
      sourceNodeId: z.nodeId,
      sourceHandleId: z.handleId,
      clientX: Z.x,
      clientY: Z.y
    });
  }, po = (I, z) => {
    if (!q || !$t(z)) return;
    const W = Hh(I, {
      ...z,
      sourceHandle: z.sourceHandle ?? "Done",
      targetHandle: z.targetHandle ?? void 0
    }, p, { shouldReplaceId: !1 });
    y(W), xe(d, W);
  }, go = (I) => {
    if ($ || I.length === 0) return;
    const z = new Set(I.map((ne) => ne.id)), W = d.filter((ne) => !z.has(ne.id)), Z = p.filter((ne) => !z.has(ne.source) && !z.has(ne.target));
    m(W), y(Z), S && z.has(S) && g(null), xe(W, Z);
  }, mo = (I) => {
    if ($ || I.length === 0) return;
    const z = new Set(I.map((Z) => Z.id)), W = p.filter((Z) => !z.has(Z.id));
    y(W), xe(d, W);
  }, un = he((I) => {
    if ($) return;
    const z = p.filter((W) => W.id !== I);
    y(z), xe(d, z);
  }, [xe, p, $, d]), dn = he((I, z, W) => {
    q && _({ kind: "spliceEdge", edgeId: I, clientX: z, clientY: W });
  }, [q]), yo = (I) => {
    const z = v;
    if (!z) return;
    _(null);
    const W = qe(z.clientX, z.clientY) ?? { x: 0, y: 0 };
    if (z.kind === "fromEmpty") {
      const ne = Ne(I, W), ue = [...d.map((be) => be.selected ? { ...be, selected: !1 } : be), ne.node];
      m(ue), g(ne.node.id), xe(ue, p, [ne.activityNode]);
      return;
    }
    if (z.kind === "fromPort") {
      const ne = d.find((Ce) => Ce.id === z.sourceNodeId), de = ne ? Xi(ne) : W, ue = Ne(I, de), Me = [...d.map((Ce) => Ce.selected ? { ...Ce, selected: !1 } : Ce), ue.node], et = [...p, Vn(z.sourceNodeId, ue.node.id, z.sourceHandleId ?? "Done")];
      m(Me), y(et), g(ue.node.id), xe(Me, et, [ue.activityNode]);
      return;
    }
    const Z = p.find((ne) => ne.id === z.edgeId);
    Z && Te(I, Z, W);
  }, xo = me(() => ({
    highlightedEdgeId: E,
    deleteEdge: un,
    requestInsertActivity: dn
  }), [un, E, dn]), wo = (I, z, W) => {
    f((Z) => [...Z, { ownerNodeId: I.nodeId, slotId: z, label: W }]), g(null);
  }, vo = (I) => {
    A((z) => {
      const W = new Set(z);
      return W.has(I) ? W.delete(I) : W.add(I), W;
    });
  };
  return !r || !s ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: D || "Loading workflow editor..." }) : /* @__PURE__ */ h.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ h.jsx("button", { type: "button", className: "wf-link-button", onClick: o, children: "Definitions" }),
      /* @__PURE__ */ h.jsx(Cn, { size: 14 }),
      /* @__PURE__ */ h.jsx("strong", { children: r.definition.name }),
      /* @__PURE__ */ h.jsx("span", { className: "wf-chip", children: "Draft" }),
      F ? /* @__PURE__ */ h.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ h.jsx(eo, { size: 13 }),
        " ",
        F
      ] }) : null,
      /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ h.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ h.jsx("input", { type: "checkbox", checked: R, onChange: (I) => H(I.target.checked) }),
          /* @__PURE__ */ h.jsx("span", { children: "Autosave" })
        ] }),
        L ? /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => Dt(n, L, { definition: r.definition, draft: s }), children: [
          /* @__PURE__ */ h.jsx(Mt, { size: 15 }),
          " Risks"
        ] }) : null,
        Q ? /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => Dt(n, Q, { definition: r.definition, draft: s }), children: [
          /* @__PURE__ */ h.jsx(Mt, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
          ro();
        }, children: [
          /* @__PURE__ */ h.jsx(qg, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
          io();
        }, children: [
          /* @__PURE__ */ h.jsx(Wg, { size: 15 }),
          " Promote"
        ] }),
        /* @__PURE__ */ h.jsxs("button", { type: "button", disabled: !b, onClick: () => {
          so();
        }, children: [
          /* @__PURE__ */ h.jsx(za, { size: 15 }),
          " Run"
        ] })
      ] })
    ] }),
    D ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Qt, { size: 16 }),
      " ",
      D
    ] }) : null,
    /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-body", children: [
      /* @__PURE__ */ h.jsxs("aside", { className: "wf-palette", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ h.jsx(Og, { size: 15 }),
          " Activities"
        ] }),
        /* @__PURE__ */ h.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: ie.map((I) => {
          const z = N.has(I.category);
          return /* @__PURE__ */ h.jsxs("div", { className: "wf-palette-category", children: [
            /* @__PURE__ */ h.jsxs(
              "button",
              {
                type: "button",
                className: "wf-palette-category-toggle",
                role: "treeitem",
                "aria-expanded": z,
                onClick: () => vo(I.category),
                children: [
                  z ? /* @__PURE__ */ h.jsx(Bg, { size: 14 }) : /* @__PURE__ */ h.jsx(Cn, { size: 14 }),
                  /* @__PURE__ */ h.jsx("span", { children: I.category }),
                  /* @__PURE__ */ h.jsx("small", { children: I.activities.length })
                ]
              }
            ),
            z ? /* @__PURE__ */ h.jsx("div", { className: "wf-palette-activities", role: "group", children: I.activities.map((W) => {
              const Z = W.description?.trim(), ne = Z ? `wf-palette-description-${W.activityVersionId}` : void 0;
              return /* @__PURE__ */ h.jsxs(
                "button",
                {
                  type: "button",
                  className: "wf-palette-activity",
                  role: "treeitem",
                  draggable: !0,
                  title: Z || Se(W),
                  "aria-describedby": ne,
                  onClick: () => no(W),
                  onDragStart: (de) => Re(de, W),
                  onDragEnd: (de) => ze(de, W),
                  onPointerDown: (de) => Ze(de, W),
                  children: [
                    /* @__PURE__ */ h.jsx("strong", { children: Se(W) }),
                    Z ? /* @__PURE__ */ h.jsx("small", { id: ne, children: Z }) : null
                  ]
                },
                W.activityVersionId
              );
            }) }) : null
          ] }, I.category);
        }) })
      ] }),
      /* @__PURE__ */ h.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-breadcrumb", children: [
          /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
            f([]), g(null);
          }, children: "Root" }),
          u.map((I, z) => /* @__PURE__ */ h.jsxs(it.Fragment, { children: [
            /* @__PURE__ */ h.jsx(Cn, { size: 13 }),
            /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
              f(u.slice(0, z + 1)), g(null);
            }, children: I.label })
          ] }, `${I.ownerNodeId}-${I.slotId}-${z}`))
        ] }),
        /* @__PURE__ */ h.jsxs("div", { className: "wf-canvas", ref: T, onDragOver: gt, onDragLeave: cn, onDrop: ln, children: [
          /* @__PURE__ */ h.jsx(Wa.Provider, { value: xo, children: /* @__PURE__ */ h.jsxs(
            ag,
            {
              nodes: d,
              edges: p,
              nodeTypes: Im,
              edgeTypes: Mm,
              onInit: x,
              onNodesChange: ao,
              onEdgesChange: co,
              onNodesDelete: go,
              onEdgesDelete: mo,
              onConnect: lo,
              onConnectStart: q ? fo : void 0,
              onConnectEnd: q ? ho : void 0,
              onReconnect: q ? po : void 0,
              isValidConnection: $t,
              onDragOver: gt,
              onDragLeave: cn,
              onDrop: ln,
              onPaneClick: () => g(null),
              onNodeClick: (I, z) => g(z.id),
              onNodeDragStop: $ ? void 0 : uo,
              fitView: !0,
              minZoom: 0.2,
              maxZoom: 1.8,
              nodesConnectable: q,
              nodesDraggable: !$,
              selectionOnDrag: !0,
              multiSelectionKeyCode: ["Shift", "Meta", "Control"],
              deleteKeyCode: $ ? null : ["Backspace", "Delete"],
              panActivationKeyCode: null,
              defaultEdgeOptions: { type: "workflow" },
              children: [
                /* @__PURE__ */ h.jsx(pg, { gap: 18, size: 1 }),
                /* @__PURE__ */ h.jsx(bg, {}),
                /* @__PURE__ */ h.jsx($g, { pannable: !0, zoomable: !0 })
              ]
            }
          ) }),
          q && d.length === 0 ? /* @__PURE__ */ h.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => oo(), children: [
            /* @__PURE__ */ h.jsx(Sr, { size: 15 }),
            " Add activity"
          ] }) : null,
          v ? /* @__PURE__ */ h.jsx(
            Qm,
            {
              clientX: v.clientX,
              clientY: v.clientY,
              activities: l,
              onPick: yo,
              onClose: () => _(null)
            }
          ) : null
        ] }),
        /* @__PURE__ */ h.jsx(Jm, { draft: s })
      ] }),
      /* @__PURE__ */ h.jsxs("aside", { className: "wf-inspector", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ h.jsx(Yg, { size: 15 }),
          " Inspector"
        ] }),
        te ? /* @__PURE__ */ h.jsxs("div", { className: "wf-inspector-content", children: [
          /* @__PURE__ */ h.jsx("h3", { children: d.find((I) => I.id === te.nodeId)?.data.label ?? te.nodeId }),
          /* @__PURE__ */ h.jsxs("dl", { children: [
            /* @__PURE__ */ h.jsx("dt", { children: "Node ID" }),
            /* @__PURE__ */ h.jsx("dd", { children: te.nodeId }),
            /* @__PURE__ */ h.jsx("dt", { children: "Activity version" }),
            /* @__PURE__ */ h.jsx("dd", { children: te.activityVersionId })
          ] }),
          G.length > 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-slot-list", children: [
            /* @__PURE__ */ h.jsx("span", { children: "Embedded slots" }),
            G.map((I) => /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => wo(te, I.id, `${d.find((z) => z.id === te.nodeId)?.data.label ?? te.nodeId} / ${I.label}`), children: [
              I.label,
              /* @__PURE__ */ h.jsxs("small", { children: [
                I.activities.length,
                " activit",
                I.activities.length === 1 ? "y" : "ies"
              ] })
            ] }, I.id))
          ] }) : /* @__PURE__ */ h.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
        ] }) : /* @__PURE__ */ h.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." })
      ] })
    ] })
  ] });
}
function Km({ data: e, selected: t }) {
  const n = e, o = !n.suppressFlowPorts, r = o ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [];
  return /* @__PURE__ */ h.jsxs("div", { className: t ? "wf-node selected" : "wf-node", children: [
    o && n.acceptsInbound ? /* @__PURE__ */ h.jsx(It, { type: "target", position: J.Left }) : null,
    /* @__PURE__ */ h.jsx("strong", { children: n.label }),
    /* @__PURE__ */ h.jsx("small", { children: n.activityTypeKey ?? n.activityVersionId }),
    n.childSlots.length > 0 ? /* @__PURE__ */ h.jsxs("span", { children: [
      n.childSlots.length,
      " embedded slot",
      n.childSlots.length === 1 ? "" : "s"
    ] }) : null,
    r.map((i, s) => {
      const a = `${(s + 1) / (r.length + 1) * 100}%`;
      return /* @__PURE__ */ h.jsxs(it.Fragment, { children: [
        /* @__PURE__ */ h.jsx("span", { className: "wf-node-port-label", style: { top: a }, children: i.displayName }),
        /* @__PURE__ */ h.jsx(It, { type: "source", position: J.Right, id: i.name, style: { top: a } })
      ] }, i.name);
    })
  ] });
}
function Gm(e) {
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
    label: u,
    labelStyle: f
  } = e, d = it.useContext(Wa), [m, p] = re(!1), [y, w, x] = zn({ sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: a }), S = d?.highlightedEdgeId === t;
  return /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
    /* @__PURE__ */ h.jsx(
      sn,
      {
        id: t,
        path: y,
        markerEnd: l,
        style: {
          ...c,
          strokeWidth: S ? 2.5 : c?.strokeWidth
        },
        label: u,
        labelX: w,
        labelY: x,
        labelStyle: f,
        onMouseEnter: () => p(!0),
        onMouseLeave: () => p(!1)
      }
    ),
    d ? /* @__PURE__ */ h.jsx(lg, { children: /* @__PURE__ */ h.jsxs(
      "div",
      {
        className: ["wf-edge-actions", m ? "visible" : "", S ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${w}px, ${x}px)` },
        onMouseEnter: () => p(!0),
        onMouseLeave: () => p(!1),
        children: [
          /* @__PURE__ */ h.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (g) => d.requestInsertActivity(t, g.clientX, g.clientY), children: /* @__PURE__ */ h.jsx(Sr, { size: 12 }) }),
          /* @__PURE__ */ h.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => d.deleteEdge(t), children: /* @__PURE__ */ h.jsx(Jo, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function Qm({ clientX: e, clientY: t, activities: n, onPick: o, onClose: r }) {
  const [i, s] = re(""), [a, l] = re(0), c = ae(null), u = ae(null), f = me(() => {
    const S = i.trim().toLowerCase(), g = n.filter(Ka);
    return S ? g.filter((v) => Se(v).toLowerCase().includes(S) || v.activityTypeKey.toLowerCase().includes(S) || (v.category ?? "").toLowerCase().includes(S) || (v.description ?? "").toLowerCase().includes(S)) : g;
  }, [n, i]), d = me(() => qa(f), [f]), m = me(() => d.flatMap((S) => S.activities), [d]);
  ce(() => {
    requestAnimationFrame(() => u.current?.focus());
  }, []), ce(() => {
    const S = (v) => {
      c.current?.contains(v.target) || r();
    }, g = (v) => {
      v.key === "Escape" && r();
    };
    return document.addEventListener("mousedown", S, !0), document.addEventListener("keydown", g), () => {
      document.removeEventListener("mousedown", S, !0), document.removeEventListener("keydown", g);
    };
  }, [r]);
  const p = (S) => {
    if (S.key === "ArrowDown")
      S.preventDefault(), l((g) => Math.min(g + 1, m.length - 1));
    else if (S.key === "ArrowUp")
      S.preventDefault(), l((g) => Math.max(g - 1, 0));
    else if (S.key === "Enter") {
      S.preventDefault();
      const g = m[a];
      g && o(g);
    }
  }, y = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), w = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let x = -1;
  return /* @__PURE__ */ h.jsxs("div", { ref: c, className: "wf-connect-menu", style: { left: y, top: w }, onMouseDown: (S) => S.stopPropagation(), onClick: (S) => S.stopPropagation(), children: [
    /* @__PURE__ */ h.jsx(
      "input",
      {
        ref: u,
        type: "search",
        value: i,
        placeholder: "Search activities...",
        "aria-label": "Search activities",
        onChange: (S) => {
          s(S.target.value), l(0);
        },
        onKeyDown: p
      }
    ),
    /* @__PURE__ */ h.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: d.length === 0 ? /* @__PURE__ */ h.jsx("p", { children: "No matching activities." }) : d.map((S) => /* @__PURE__ */ h.jsxs("section", { children: [
      /* @__PURE__ */ h.jsx("h4", { children: S.category }),
      S.activities.map((g) => {
        x += 1;
        const v = x, _ = v === a;
        return /* @__PURE__ */ h.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": _,
            className: _ ? "active" : "",
            onMouseEnter: () => l(v),
            onClick: () => o(g),
            children: [
              /* @__PURE__ */ h.jsx("strong", { children: Se(g) }),
              /* @__PURE__ */ h.jsx("small", { children: g.category || g.activityTypeKey })
            ]
          },
          g.activityVersionId
        );
      })
    ] }, S.category)) })
  ] });
}
function Jm({ draft: e }) {
  return e.validationErrors.length ? /* @__PURE__ */ h.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ h.jsx(Qt, { size: 14 }),
    e.validationErrors.length,
    " validation issue",
    e.validationErrors.length === 1 ? "" : "s"
  ] }) : /* @__PURE__ */ h.jsxs("div", { className: "wf-validation ok", children: [
    /* @__PURE__ */ h.jsx(eo, { size: 14 }),
    " No validation errors"
  ] });
}
function Yi(e) {
  return `${Se(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function Xi(e) {
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
function Lt(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function tr(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
export {
  r0 as register
};
