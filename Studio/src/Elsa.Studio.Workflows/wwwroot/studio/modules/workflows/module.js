import ot, { memo as xe, forwardRef as Vn, useRef as ae, useEffect as ce, useCallback as he, useContext as en, useMemo as ye, useState as ne, createContext as Go, useLayoutEffect as vc, createElement as $o } from "react";
function bc(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var yo = { exports: {} }, zt = {};
var Mr;
function Sc() {
  if (Mr) return zt;
  Mr = 1;
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
  return zt.Fragment = t, zt.jsx = n, zt.jsxs = n, zt;
}
var Ar;
function Ec() {
  return Ar || (Ar = 1, yo.exports = Sc()), yo.exports;
}
var h = Ec();
function we(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, o; n < e.length; n++)
      (o = we(e[n])) !== "" && (t += (t && " ") + o);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var Nc = { value: () => {
} };
function On() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new vn(n);
}
function vn(e) {
  this._ = e;
}
function _c(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
vn.prototype = On.prototype = {
  constructor: vn,
  on: function(e, t) {
    var n = this._, o = _c(e + "", n), r, i = -1, s = o.length;
    if (arguments.length < 2) {
      for (; ++i < s; ) if ((r = (e = o[i]).type) && (r = Cc(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++i < s; )
      if (r = (e = o[i]).type) n[r] = Dr(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = Dr(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new vn(e);
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
function Cc(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function Dr(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = Nc, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var To = "http://www.w3.org/1999/xhtml";
const jr = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: To,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function Bn(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), jr.hasOwnProperty(t) ? { space: jr[t], local: e } : e;
}
function kc(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === To && t.documentElement.namespaceURI === To ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Ic(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function qi(e) {
  var t = Bn(e);
  return (t.local ? Ic : kc)(t);
}
function Mc() {
}
function Qo(e) {
  return e == null ? Mc : function() {
    return this.querySelector(e);
  };
}
function Ac(e) {
  typeof e != "function" && (e = Qo(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = new Array(s), l, c, u = 0; u < s; ++u)
      (l = i[u]) && (c = e.call(l, l.__data__, u, i)) && ("__data__" in l && (c.__data__ = l.__data__), a[u] = c);
  return new De(o, this._parents);
}
function Dc(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function jc() {
  return [];
}
function Zi(e) {
  return e == null ? jc : function() {
    return this.querySelectorAll(e);
  };
}
function Pc(e) {
  return function() {
    return Dc(e.apply(this, arguments));
  };
}
function $c(e) {
  typeof e == "function" ? e = Pc(e) : e = Zi(e);
  for (var t = this._groups, n = t.length, o = [], r = [], i = 0; i < n; ++i)
    for (var s = t[i], a = s.length, l, c = 0; c < a; ++c)
      (l = s[c]) && (o.push(e.call(l, l.__data__, c, s)), r.push(l));
  return new De(o, r);
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
var Tc = Array.prototype.find;
function zc(e) {
  return function() {
    return Tc.call(this.children, e);
  };
}
function Rc() {
  return this.firstElementChild;
}
function Lc(e) {
  return this.select(e == null ? Rc : zc(typeof e == "function" ? e : Ki(e)));
}
var Hc = Array.prototype.filter;
function Vc() {
  return Array.from(this.children);
}
function Oc(e) {
  return function() {
    return Hc.call(this.children, e);
  };
}
function Bc(e) {
  return this.selectAll(e == null ? Vc : Oc(typeof e == "function" ? e : Ki(e)));
}
function Fc(e) {
  typeof e != "function" && (e = Ui(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = [], l, c = 0; c < s; ++c)
      (l = i[c]) && e.call(l, l.__data__, c, i) && a.push(l);
  return new De(o, this._parents);
}
function Gi(e) {
  return new Array(e.length);
}
function Yc() {
  return new De(this._enter || this._groups.map(Gi), this._parents);
}
function kn(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
kn.prototype = {
  constructor: kn,
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
function Xc(e) {
  return function() {
    return e;
  };
}
function Wc(e, t, n, o, r, i) {
  for (var s = 0, a, l = t.length, c = i.length; s < c; ++s)
    (a = t[s]) ? (a.__data__ = i[s], o[s] = a) : n[s] = new kn(e, i[s]);
  for (; s < l; ++s)
    (a = t[s]) && (r[s] = a);
}
function qc(e, t, n, o, r, i, s) {
  var a, l, c = /* @__PURE__ */ new Map(), u = t.length, d = i.length, f = new Array(u), m;
  for (a = 0; a < u; ++a)
    (l = t[a]) && (f[a] = m = s.call(l, l.__data__, a, t) + "", c.has(m) ? r[a] = l : c.set(m, l));
  for (a = 0; a < d; ++a)
    m = s.call(e, i[a], a, i) + "", (l = c.get(m)) ? (o[a] = l, l.__data__ = i[a], c.delete(m)) : n[a] = new kn(e, i[a]);
  for (a = 0; a < u; ++a)
    (l = t[a]) && c.get(f[a]) === l && (r[a] = l);
}
function Zc(e) {
  return e.__data__;
}
function Uc(e, t) {
  if (!arguments.length) return Array.from(this, Zc);
  var n = t ? qc : Wc, o = this._parents, r = this._groups;
  typeof e != "function" && (e = Xc(e));
  for (var i = r.length, s = new Array(i), a = new Array(i), l = new Array(i), c = 0; c < i; ++c) {
    var u = o[c], d = r[c], f = d.length, m = Kc(e.call(u, u && u.__data__, c, o)), g = m.length, y = a[c] = new Array(g), w = s[c] = new Array(g), x = l[c] = new Array(f);
    n(u, d, y, w, x, m, t);
    for (var S = 0, p = 0, v, N; S < g; ++S)
      if (v = y[S]) {
        for (S >= p && (p = S + 1); !(N = w[p]) && ++p < g; ) ;
        v._next = N || null;
      }
  }
  return s = new De(s, o), s._enter = a, s._exit = l, s;
}
function Kc(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function Gc() {
  return new De(this._exit || this._groups.map(Gi), this._parents);
}
function Qc(e, t, n) {
  var o = this.enter(), r = this, i = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? i.remove() : n(i), o && r ? o.merge(r).order() : r;
}
function Jc(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, i = o.length, s = Math.min(r, i), a = new Array(r), l = 0; l < s; ++l)
    for (var c = n[l], u = o[l], d = c.length, f = a[l] = new Array(d), m, g = 0; g < d; ++g)
      (m = c[g] || u[g]) && (f[g] = m);
  for (; l < r; ++l)
    a[l] = n[l];
  return new De(a, this._parents);
}
function el() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, i = o[r], s; --r >= 0; )
      (s = o[r]) && (i && s.compareDocumentPosition(i) ^ 4 && i.parentNode.insertBefore(s, i), i = s);
  return this;
}
function tl(e) {
  e || (e = nl);
  function t(d, f) {
    return d && f ? e(d.__data__, f.__data__) : !d - !f;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), i = 0; i < o; ++i) {
    for (var s = n[i], a = s.length, l = r[i] = new Array(a), c, u = 0; u < a; ++u)
      (c = s[u]) && (l[u] = c);
    l.sort(t);
  }
  return new De(r, this._parents).order();
}
function nl(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function ol() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function rl() {
  return Array.from(this);
}
function il() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length; r < i; ++r) {
      var s = o[r];
      if (s) return s;
    }
  return null;
}
function sl() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function al() {
  return !this.node();
}
function cl(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], i = 0, s = r.length, a; i < s; ++i)
      (a = r[i]) && e.call(a, a.__data__, i, r);
  return this;
}
function ll(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function ul(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function dl(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function fl(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function hl(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function pl(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function gl(e, t) {
  var n = Bn(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? ul : ll : typeof t == "function" ? n.local ? pl : hl : n.local ? fl : dl)(n, t));
}
function Qi(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function ml(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function yl(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function xl(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function wl(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? ml : typeof t == "function" ? xl : yl)(e, t, n ?? "")) : bt(this.node(), e);
}
function bt(e, t) {
  return e.style.getPropertyValue(t) || Qi(e).getComputedStyle(e, null).getPropertyValue(t);
}
function vl(e) {
  return function() {
    delete this[e];
  };
}
function bl(e, t) {
  return function() {
    this[e] = t;
  };
}
function Sl(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function El(e, t) {
  return arguments.length > 1 ? this.each((t == null ? vl : typeof t == "function" ? Sl : bl)(e, t)) : this.node()[e];
}
function Ji(e) {
  return e.trim().split(/^|\s+/);
}
function Jo(e) {
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
  for (var n = Jo(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function ns(e, t) {
  for (var n = Jo(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function Nl(e) {
  return function() {
    ts(this, e);
  };
}
function _l(e) {
  return function() {
    ns(this, e);
  };
}
function Cl(e, t) {
  return function() {
    (t.apply(this, arguments) ? ts : ns)(this, e);
  };
}
function kl(e, t) {
  var n = Ji(e + "");
  if (arguments.length < 2) {
    for (var o = Jo(this.node()), r = -1, i = n.length; ++r < i; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? Cl : t ? Nl : _l)(n, t));
}
function Il() {
  this.textContent = "";
}
function Ml(e) {
  return function() {
    this.textContent = e;
  };
}
function Al(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function Dl(e) {
  return arguments.length ? this.each(e == null ? Il : (typeof e == "function" ? Al : Ml)(e)) : this.node().textContent;
}
function jl() {
  this.innerHTML = "";
}
function Pl(e) {
  return function() {
    this.innerHTML = e;
  };
}
function $l(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function Tl(e) {
  return arguments.length ? this.each(e == null ? jl : (typeof e == "function" ? $l : Pl)(e)) : this.node().innerHTML;
}
function zl() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function Rl() {
  return this.each(zl);
}
function Ll() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Hl() {
  return this.each(Ll);
}
function Vl(e) {
  var t = typeof e == "function" ? e : qi(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Ol() {
  return null;
}
function Bl(e, t) {
  var n = typeof e == "function" ? e : qi(e), o = t == null ? Ol : typeof t == "function" ? t : Qo(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function Fl() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Yl() {
  return this.each(Fl);
}
function Xl() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Wl() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function ql(e) {
  return this.select(e ? Wl : Xl);
}
function Zl(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function Ul(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function Kl(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function Gl(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, i; n < r; ++n)
        i = t[n], (!e.type || i.type === e.type) && i.name === e.name ? this.removeEventListener(i.type, i.listener, i.options) : t[++o] = i;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function Ql(e, t, n) {
  return function() {
    var o = this.__on, r, i = Ul(t);
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
function Jl(e, t, n) {
  var o = Kl(e + ""), r, i = o.length, s;
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
  for (a = t ? Ql : Gl, r = 0; r < i; ++r) this.each(a(o[r], t, n));
  return this;
}
function os(e, t, n) {
  var o = Qi(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function eu(e, t) {
  return function() {
    return os(this, e, t);
  };
}
function tu(e, t) {
  return function() {
    return os(this, e, t.apply(this, arguments));
  };
}
function nu(e, t) {
  return this.each((typeof t == "function" ? tu : eu)(e, t));
}
function* ou() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length, s; r < i; ++r)
      (s = o[r]) && (yield s);
}
var rs = [null];
function De(e, t) {
  this._groups = e, this._parents = t;
}
function tn() {
  return new De([[document.documentElement]], rs);
}
function ru() {
  return this;
}
De.prototype = tn.prototype = {
  constructor: De,
  select: Ac,
  selectAll: $c,
  selectChild: Lc,
  selectChildren: Bc,
  filter: Fc,
  data: Uc,
  enter: Yc,
  exit: Gc,
  join: Qc,
  merge: Jc,
  selection: ru,
  order: el,
  sort: tl,
  call: ol,
  nodes: rl,
  node: il,
  size: sl,
  empty: al,
  each: cl,
  attr: gl,
  style: wl,
  property: El,
  classed: kl,
  text: Dl,
  html: Tl,
  raise: Rl,
  lower: Hl,
  append: Vl,
  insert: Bl,
  remove: Yl,
  clone: ql,
  datum: Zl,
  on: Jl,
  dispatch: nu,
  [Symbol.iterator]: ou
};
function Me(e) {
  return typeof e == "string" ? new De([[document.querySelector(e)]], [document.documentElement]) : new De([[e]], rs);
}
function iu(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function ze(e, t) {
  if (e = iu(e), t === void 0 && (t = e.currentTarget), t) {
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
const su = { passive: !1 }, Yt = { capture: !0, passive: !1 };
function xo(e) {
  e.stopImmediatePropagation();
}
function wt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function is(e) {
  var t = e.document.documentElement, n = Me(e).on("dragstart.drag", wt, Yt);
  "onselectstart" in t ? n.on("selectstart.drag", wt, Yt) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function ss(e, t) {
  var n = e.document.documentElement, o = Me(e).on("dragstart.drag", null);
  t && (o.on("click.drag", wt, Yt), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const dn = (e) => () => e;
function zo(e, {
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
zo.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function au(e) {
  return !e.ctrlKey && !e.button;
}
function cu() {
  return this.parentNode;
}
function lu(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function uu() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function as() {
  var e = au, t = cu, n = lu, o = uu, r = {}, i = On("start", "drag", "end"), s = 0, a, l, c, u, d = 0;
  function f(v) {
    v.on("mousedown.drag", m).filter(o).on("touchstart.drag", w).on("touchmove.drag", x, su).on("touchend.drag touchcancel.drag", S).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function m(v, N) {
    if (!(u || !e.call(this, v, N))) {
      var E = p(this, t.call(this, v, N), v, N, "mouse");
      E && (Me(v.view).on("mousemove.drag", g, Yt).on("mouseup.drag", y, Yt), is(v.view), xo(v), c = !1, a = v.clientX, l = v.clientY, E("start", v));
    }
  }
  function g(v) {
    if (wt(v), !c) {
      var N = v.clientX - a, E = v.clientY - l;
      c = N * N + E * E > d;
    }
    r.mouse("drag", v);
  }
  function y(v) {
    Me(v.view).on("mousemove.drag mouseup.drag", null), ss(v.view, c), wt(v), r.mouse("end", v);
  }
  function w(v, N) {
    if (e.call(this, v, N)) {
      var E = v.changedTouches, M = t.call(this, v, N), D = E.length, j, F;
      for (j = 0; j < D; ++j)
        (F = p(this, M, v, N, E[j].identifier, E[j])) && (xo(v), F("start", v, E[j]));
    }
  }
  function x(v) {
    var N = v.changedTouches, E = N.length, M, D;
    for (M = 0; M < E; ++M)
      (D = r[N[M].identifier]) && (wt(v), D("drag", v, N[M]));
  }
  function S(v) {
    var N = v.changedTouches, E = N.length, M, D;
    for (u && clearTimeout(u), u = setTimeout(function() {
      u = null;
    }, 500), M = 0; M < E; ++M)
      (D = r[N[M].identifier]) && (xo(v), D("end", v, N[M]));
  }
  function p(v, N, E, M, D, j) {
    var F = i.copy(), k = ze(j || E, N), z, H, b;
    if ((b = n.call(v, new zo("beforestart", {
      sourceEvent: E,
      target: f,
      identifier: D,
      active: s,
      x: k[0],
      y: k[1],
      dx: 0,
      dy: 0,
      dispatch: F
    }), M)) != null)
      return z = b.x - k[0] || 0, H = b.y - k[1] || 0, function C(_, A, $) {
        var P = k, B;
        switch (_) {
          case "start":
            r[D] = C, B = s++;
            break;
          case "end":
            delete r[D], --s;
          // falls through
          case "drag":
            k = ze($ || A, N), B = s;
            break;
        }
        F.call(
          _,
          v,
          new zo(_, {
            sourceEvent: A,
            subject: b,
            target: f,
            identifier: D,
            active: B,
            x: k[0] + z,
            y: k[1] + H,
            dx: k[0] - P[0],
            dy: k[1] - P[1],
            dispatch: F
          }),
          M
        );
      };
  }
  return f.filter = function(v) {
    return arguments.length ? (e = typeof v == "function" ? v : dn(!!v), f) : e;
  }, f.container = function(v) {
    return arguments.length ? (t = typeof v == "function" ? v : dn(v), f) : t;
  }, f.subject = function(v) {
    return arguments.length ? (n = typeof v == "function" ? v : dn(v), f) : n;
  }, f.touchable = function(v) {
    return arguments.length ? (o = typeof v == "function" ? v : dn(!!v), f) : o;
  }, f.on = function() {
    var v = i.on.apply(i, arguments);
    return v === i ? f : v;
  }, f.clickDistance = function(v) {
    return arguments.length ? (d = (v = +v) * v, f) : Math.sqrt(d);
  }, f;
}
function er(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function cs(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function nn() {
}
var Xt = 0.7, In = 1 / Xt, vt = "\\s*([+-]?\\d+)\\s*", Wt = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", Fe = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", du = /^#([0-9a-f]{3,8})$/, fu = new RegExp(`^rgb\\(${vt},${vt},${vt}\\)$`), hu = new RegExp(`^rgb\\(${Fe},${Fe},${Fe}\\)$`), pu = new RegExp(`^rgba\\(${vt},${vt},${vt},${Wt}\\)$`), gu = new RegExp(`^rgba\\(${Fe},${Fe},${Fe},${Wt}\\)$`), mu = new RegExp(`^hsl\\(${Wt},${Fe},${Fe}\\)$`), yu = new RegExp(`^hsla\\(${Wt},${Fe},${Fe},${Wt}\\)$`), Pr = {
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
er(nn, dt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: $r,
  // Deprecated! Use color.formatHex.
  formatHex: $r,
  formatHex8: xu,
  formatHsl: wu,
  formatRgb: Tr,
  toString: Tr
});
function $r() {
  return this.rgb().formatHex();
}
function xu() {
  return this.rgb().formatHex8();
}
function wu() {
  return ls(this).formatHsl();
}
function Tr() {
  return this.rgb().formatRgb();
}
function dt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = du.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? zr(t) : n === 3 ? new ke(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? fn(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? fn(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = fu.exec(e)) ? new ke(t[1], t[2], t[3], 1) : (t = hu.exec(e)) ? new ke(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = pu.exec(e)) ? fn(t[1], t[2], t[3], t[4]) : (t = gu.exec(e)) ? fn(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = mu.exec(e)) ? Hr(t[1], t[2] / 100, t[3] / 100, 1) : (t = yu.exec(e)) ? Hr(t[1], t[2] / 100, t[3] / 100, t[4]) : Pr.hasOwnProperty(e) ? zr(Pr[e]) : e === "transparent" ? new ke(NaN, NaN, NaN, 0) : null;
}
function zr(e) {
  return new ke(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function fn(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new ke(e, t, n, o);
}
function vu(e) {
  return e instanceof nn || (e = dt(e)), e ? (e = e.rgb(), new ke(e.r, e.g, e.b, e.opacity)) : new ke();
}
function Ro(e, t, n, o) {
  return arguments.length === 1 ? vu(e) : new ke(e, t, n, o ?? 1);
}
function ke(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
er(ke, Ro, cs(nn, {
  brighter(e) {
    return e = e == null ? In : Math.pow(In, e), new ke(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Xt : Math.pow(Xt, e), new ke(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new ke(lt(this.r), lt(this.g), lt(this.b), Mn(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: Rr,
  // Deprecated! Use color.formatHex.
  formatHex: Rr,
  formatHex8: bu,
  formatRgb: Lr,
  toString: Lr
}));
function Rr() {
  return `#${ct(this.r)}${ct(this.g)}${ct(this.b)}`;
}
function bu() {
  return `#${ct(this.r)}${ct(this.g)}${ct(this.b)}${ct((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function Lr() {
  const e = Mn(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${lt(this.r)}, ${lt(this.g)}, ${lt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Mn(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function lt(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function ct(e) {
  return e = lt(e), (e < 16 ? "0" : "") + e.toString(16);
}
function Hr(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Re(e, t, n, o);
}
function ls(e) {
  if (e instanceof Re) return new Re(e.h, e.s, e.l, e.opacity);
  if (e instanceof nn || (e = dt(e)), !e) return new Re();
  if (e instanceof Re) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), i = Math.max(t, n, o), s = NaN, a = i - r, l = (i + r) / 2;
  return a ? (t === i ? s = (n - o) / a + (n < o) * 6 : n === i ? s = (o - t) / a + 2 : s = (t - n) / a + 4, a /= l < 0.5 ? i + r : 2 - i - r, s *= 60) : a = l > 0 && l < 1 ? 0 : s, new Re(s, a, l, e.opacity);
}
function Su(e, t, n, o) {
  return arguments.length === 1 ? ls(e) : new Re(e, t, n, o ?? 1);
}
function Re(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
er(Re, Su, cs(nn, {
  brighter(e) {
    return e = e == null ? In : Math.pow(In, e), new Re(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Xt : Math.pow(Xt, e), new Re(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new ke(
      wo(e >= 240 ? e - 240 : e + 120, r, o),
      wo(e, r, o),
      wo(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new Re(Vr(this.h), hn(this.s), hn(this.l), Mn(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Mn(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${Vr(this.h)}, ${hn(this.s) * 100}%, ${hn(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function Vr(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function hn(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function wo(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const tr = (e) => () => e;
function Eu(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function Nu(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function _u(e) {
  return (e = +e) == 1 ? us : function(t, n) {
    return n - t ? Nu(t, n, e) : tr(isNaN(t) ? n : t);
  };
}
function us(e, t) {
  var n = t - e;
  return n ? Eu(e, n) : tr(isNaN(e) ? t : e);
}
const An = (function e(t) {
  var n = _u(t);
  function o(r, i) {
    var s = n((r = Ro(r)).r, (i = Ro(i)).r), a = n(r.g, i.g), l = n(r.b, i.b), c = us(r.opacity, i.opacity);
    return function(u) {
      return r.r = s(u), r.g = a(u), r.b = l(u), r.opacity = c(u), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function Cu(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(i) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - i) + t[r] * i;
    return o;
  };
}
function ku(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Iu(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), i = new Array(n), s;
  for (s = 0; s < o; ++s) r[s] = Bt(e[s], t[s]);
  for (; s < n; ++s) i[s] = t[s];
  return function(a) {
    for (s = 0; s < o; ++s) i[s] = r[s](a);
    return i;
  };
}
function Mu(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(o) {
    return n.setTime(e * (1 - o) + t * o), n;
  };
}
function Be(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function Au(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = Bt(e[r], t[r]) : o[r] = t[r];
  return function(i) {
    for (r in n) o[r] = n[r](i);
    return o;
  };
}
var Lo = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, vo = new RegExp(Lo.source, "g");
function Du(e) {
  return function() {
    return e;
  };
}
function ju(e) {
  return function(t) {
    return e(t) + "";
  };
}
function ds(e, t) {
  var n = Lo.lastIndex = vo.lastIndex = 0, o, r, i, s = -1, a = [], l = [];
  for (e = e + "", t = t + ""; (o = Lo.exec(e)) && (r = vo.exec(t)); )
    (i = r.index) > n && (i = t.slice(n, i), a[s] ? a[s] += i : a[++s] = i), (o = o[0]) === (r = r[0]) ? a[s] ? a[s] += r : a[++s] = r : (a[++s] = null, l.push({ i: s, x: Be(o, r) })), n = vo.lastIndex;
  return n < t.length && (i = t.slice(n), a[s] ? a[s] += i : a[++s] = i), a.length < 2 ? l[0] ? ju(l[0].x) : Du(t) : (t = l.length, function(c) {
    for (var u = 0, d; u < t; ++u) a[(d = l[u]).i] = d.x(c);
    return a.join("");
  });
}
function Bt(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? tr(t) : (n === "number" ? Be : n === "string" ? (o = dt(t)) ? (t = o, An) : ds : t instanceof dt ? An : t instanceof Date ? Mu : ku(t) ? Cu : Array.isArray(t) ? Iu : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Au : Be)(e, t);
}
var Or = 180 / Math.PI, Ho = {
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
    rotate: Math.atan2(t, e) * Or,
    skewX: Math.atan(l) * Or,
    scaleX: s,
    scaleY: a
  };
}
var pn;
function Pu(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Ho : fs(t.a, t.b, t.c, t.d, t.e, t.f);
}
function $u(e) {
  return e == null || (pn || (pn = document.createElementNS("http://www.w3.org/2000/svg", "g")), pn.setAttribute("transform", e), !(e = pn.transform.baseVal.consolidate())) ? Ho : (e = e.matrix, fs(e.a, e.b, e.c, e.d, e.e, e.f));
}
function hs(e, t, n, o) {
  function r(c) {
    return c.length ? c.pop() + " " : "";
  }
  function i(c, u, d, f, m, g) {
    if (c !== d || u !== f) {
      var y = m.push("translate(", null, t, null, n);
      g.push({ i: y - 4, x: Be(c, d) }, { i: y - 2, x: Be(u, f) });
    } else (d || f) && m.push("translate(" + d + t + f + n);
  }
  function s(c, u, d, f) {
    c !== u ? (c - u > 180 ? u += 360 : u - c > 180 && (c += 360), f.push({ i: d.push(r(d) + "rotate(", null, o) - 2, x: Be(c, u) })) : u && d.push(r(d) + "rotate(" + u + o);
  }
  function a(c, u, d, f) {
    c !== u ? f.push({ i: d.push(r(d) + "skewX(", null, o) - 2, x: Be(c, u) }) : u && d.push(r(d) + "skewX(" + u + o);
  }
  function l(c, u, d, f, m, g) {
    if (c !== d || u !== f) {
      var y = m.push(r(m) + "scale(", null, ",", null, ")");
      g.push({ i: y - 4, x: Be(c, d) }, { i: y - 2, x: Be(u, f) });
    } else (d !== 1 || f !== 1) && m.push(r(m) + "scale(" + d + "," + f + ")");
  }
  return function(c, u) {
    var d = [], f = [];
    return c = e(c), u = e(u), i(c.translateX, c.translateY, u.translateX, u.translateY, d, f), s(c.rotate, u.rotate, d, f), a(c.skewX, u.skewX, d, f), l(c.scaleX, c.scaleY, u.scaleX, u.scaleY, d, f), c = u = null, function(m) {
      for (var g = -1, y = f.length, w; ++g < y; ) d[(w = f[g]).i] = w.x(m);
      return d.join("");
    };
  };
}
var Tu = hs(Pu, "px, ", "px)", "deg)"), zu = hs($u, ", ", ")", ")"), Ru = 1e-12;
function Br(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Lu(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Hu(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const bn = (function e(t, n, o) {
  function r(i, s) {
    var a = i[0], l = i[1], c = i[2], u = s[0], d = s[1], f = s[2], m = u - a, g = d - l, y = m * m + g * g, w, x;
    if (y < Ru)
      x = Math.log(f / c) / t, w = function(M) {
        return [
          a + M * m,
          l + M * g,
          c * Math.exp(t * M * x)
        ];
      };
    else {
      var S = Math.sqrt(y), p = (f * f - c * c + o * y) / (2 * c * n * S), v = (f * f - c * c - o * y) / (2 * f * n * S), N = Math.log(Math.sqrt(p * p + 1) - p), E = Math.log(Math.sqrt(v * v + 1) - v);
      x = (E - N) / t, w = function(M) {
        var D = M * x, j = Br(N), F = c / (n * S) * (j * Hu(t * D + N) - Lu(N));
        return [
          a + F * m,
          l + F * g,
          c * j / Br(t * D + N)
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
var St = 0, Vt = 0, Rt = 0, ps = 1e3, Dn, Ot, jn = 0, ft = 0, Fn = 0, qt = typeof performance == "object" && performance.now ? performance : Date, gs = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function nr() {
  return ft || (gs(Vu), ft = qt.now() + Fn);
}
function Vu() {
  ft = 0;
}
function Pn() {
  this._call = this._time = this._next = null;
}
Pn.prototype = ms.prototype = {
  constructor: Pn,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? nr() : +n) + (t == null ? 0 : +t), !this._next && Ot !== this && (Ot ? Ot._next = this : Dn = this, Ot = this), this._call = e, this._time = n, Vo();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Vo());
  }
};
function ms(e, t, n) {
  var o = new Pn();
  return o.restart(e, t, n), o;
}
function Ou() {
  nr(), ++St;
  for (var e = Dn, t; e; )
    (t = ft - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --St;
}
function Fr() {
  ft = (jn = qt.now()) + Fn, St = Vt = 0;
  try {
    Ou();
  } finally {
    St = 0, Fu(), ft = 0;
  }
}
function Bu() {
  var e = qt.now(), t = e - jn;
  t > ps && (Fn -= t, jn = e);
}
function Fu() {
  for (var e, t = Dn, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Dn = n);
  Ot = e, Vo(o);
}
function Vo(e) {
  if (!St) {
    Vt && (Vt = clearTimeout(Vt));
    var t = e - ft;
    t > 24 ? (e < 1 / 0 && (Vt = setTimeout(Fr, e - qt.now() - Fn)), Rt && (Rt = clearInterval(Rt))) : (Rt || (jn = qt.now(), Rt = setInterval(Bu, ps)), St = 1, gs(Fr));
  }
}
function Yr(e, t, n) {
  var o = new Pn();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var Yu = On("start", "end", "cancel", "interrupt"), Xu = [], ys = 0, Xr = 1, Oo = 2, Sn = 3, Wr = 4, Bo = 5, En = 6;
function Yn(e, t, n, o, r, i) {
  var s = e.__transition;
  if (!s) e.__transition = {};
  else if (n in s) return;
  Wu(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: Yu,
    tween: Xu,
    time: i.time,
    delay: i.delay,
    duration: i.duration,
    ease: i.ease,
    timer: null,
    state: ys
  });
}
function or(e, t) {
  var n = Ve(e, t);
  if (n.state > ys) throw new Error("too late; already scheduled");
  return n;
}
function Xe(e, t) {
  var n = Ve(e, t);
  if (n.state > Sn) throw new Error("too late; already running");
  return n;
}
function Ve(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function Wu(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = ms(i, 0, n.time);
  function i(c) {
    n.state = Xr, n.timer.restart(s, n.delay, n.time), n.delay <= c && s(c - n.delay);
  }
  function s(c) {
    var u, d, f, m;
    if (n.state !== Xr) return l();
    for (u in o)
      if (m = o[u], m.name === n.name) {
        if (m.state === Sn) return Yr(s);
        m.state === Wr ? (m.state = En, m.timer.stop(), m.on.call("interrupt", e, e.__data__, m.index, m.group), delete o[u]) : +u < t && (m.state = En, m.timer.stop(), m.on.call("cancel", e, e.__data__, m.index, m.group), delete o[u]);
      }
    if (Yr(function() {
      n.state === Sn && (n.state = Wr, n.timer.restart(a, n.delay, n.time), a(c));
    }), n.state = Oo, n.on.call("start", e, e.__data__, n.index, n.group), n.state === Oo) {
      for (n.state = Sn, r = new Array(f = n.tween.length), u = 0, d = -1; u < f; ++u)
        (m = n.tween[u].value.call(e, e.__data__, n.index, n.group)) && (r[++d] = m);
      r.length = d + 1;
    }
  }
  function a(c) {
    for (var u = c < n.duration ? n.ease.call(null, c / n.duration) : (n.timer.restart(l), n.state = Bo, 1), d = -1, f = r.length; ++d < f; )
      r[d].call(e, u);
    n.state === Bo && (n.on.call("end", e, e.__data__, n.index, n.group), l());
  }
  function l() {
    n.state = En, n.timer.stop(), delete o[t];
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
      r = o.state > Oo && o.state < Bo, o.state = En, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[s];
    }
    i && delete e.__transition;
  }
}
function qu(e) {
  return this.each(function() {
    Nn(this, e);
  });
}
function Zu(e, t) {
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
function Uu(e, t, n) {
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
function Ku(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = Ve(this.node(), n).tween, r = 0, i = o.length, s; r < i; ++r)
      if ((s = o[r]).name === e)
        return s.value;
    return null;
  }
  return this.each((t == null ? Zu : Uu)(n, e, t));
}
function rr(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = Xe(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return Ve(r, o).value[t];
  };
}
function xs(e, t) {
  var n;
  return (typeof t == "number" ? Be : t instanceof dt ? An : (n = dt(t)) ? (t = n, An) : ds)(e, t);
}
function Gu(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Qu(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Ju(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttribute(e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function ed(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttributeNS(e.space, e.local);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function td(e, t, n) {
  var o, r, i;
  return function() {
    var s, a = n(this), l;
    return a == null ? void this.removeAttribute(e) : (s = this.getAttribute(e), l = a + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a)));
  };
}
function nd(e, t, n) {
  var o, r, i;
  return function() {
    var s, a = n(this), l;
    return a == null ? void this.removeAttributeNS(e.space, e.local) : (s = this.getAttributeNS(e.space, e.local), l = a + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a)));
  };
}
function od(e, t) {
  var n = Bn(e), o = n === "transform" ? zu : xs;
  return this.attrTween(e, typeof t == "function" ? (n.local ? nd : td)(n, o, rr(this, "attr." + e, t)) : t == null ? (n.local ? Qu : Gu)(n) : (n.local ? ed : Ju)(n, o, t));
}
function rd(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function id(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function sd(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && id(e, i)), n;
  }
  return r._value = t, r;
}
function ad(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && rd(e, i)), n;
  }
  return r._value = t, r;
}
function cd(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = Bn(e);
  return this.tween(n, (o.local ? sd : ad)(o, t));
}
function ld(e, t) {
  return function() {
    or(this, e).delay = +t.apply(this, arguments);
  };
}
function ud(e, t) {
  return t = +t, function() {
    or(this, e).delay = t;
  };
}
function dd(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? ld : ud)(t, e)) : Ve(this.node(), t).delay;
}
function fd(e, t) {
  return function() {
    Xe(this, e).duration = +t.apply(this, arguments);
  };
}
function hd(e, t) {
  return t = +t, function() {
    Xe(this, e).duration = t;
  };
}
function pd(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? fd : hd)(t, e)) : Ve(this.node(), t).duration;
}
function gd(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    Xe(this, e).ease = t;
  };
}
function md(e) {
  var t = this._id;
  return arguments.length ? this.each(gd(t, e)) : Ve(this.node(), t).ease;
}
function yd(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    Xe(this, e).ease = n;
  };
}
function xd(e) {
  if (typeof e != "function") throw new Error();
  return this.each(yd(this._id, e));
}
function wd(e) {
  typeof e != "function" && (e = Ui(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = [], l, c = 0; c < s; ++c)
      (l = i[c]) && e.call(l, l.__data__, c, i) && a.push(l);
  return new Ge(o, this._parents, this._name, this._id);
}
function vd(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, i = Math.min(o, r), s = new Array(o), a = 0; a < i; ++a)
    for (var l = t[a], c = n[a], u = l.length, d = s[a] = new Array(u), f, m = 0; m < u; ++m)
      (f = l[m] || c[m]) && (d[m] = f);
  for (; a < o; ++a)
    s[a] = t[a];
  return new Ge(s, this._parents, this._name, this._id);
}
function bd(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function Sd(e, t, n) {
  var o, r, i = bd(t) ? or : Xe;
  return function() {
    var s = i(this, e), a = s.on;
    a !== o && (r = (o = a).copy()).on(t, n), s.on = r;
  };
}
function Ed(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Ve(this.node(), n).on.on(e) : this.each(Sd(n, e, t));
}
function Nd(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function _d() {
  return this.on("end.remove", Nd(this._id));
}
function Cd(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Qo(e));
  for (var o = this._groups, r = o.length, i = new Array(r), s = 0; s < r; ++s)
    for (var a = o[s], l = a.length, c = i[s] = new Array(l), u, d, f = 0; f < l; ++f)
      (u = a[f]) && (d = e.call(u, u.__data__, f, a)) && ("__data__" in u && (d.__data__ = u.__data__), c[f] = d, Yn(c[f], t, n, f, c, Ve(u, n)));
  return new Ge(i, this._parents, t, n);
}
function kd(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Zi(e));
  for (var o = this._groups, r = o.length, i = [], s = [], a = 0; a < r; ++a)
    for (var l = o[a], c = l.length, u, d = 0; d < c; ++d)
      if (u = l[d]) {
        for (var f = e.call(u, u.__data__, d, l), m, g = Ve(u, n), y = 0, w = f.length; y < w; ++y)
          (m = f[y]) && Yn(m, t, n, y, f, g);
        i.push(f), s.push(u);
      }
  return new Ge(i, s, t, n);
}
var Id = tn.prototype.constructor;
function Md() {
  return new Id(this._groups, this._parents);
}
function Ad(e, t) {
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
function Dd(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = bt(this, e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function jd(e, t, n) {
  var o, r, i;
  return function() {
    var s = bt(this, e), a = n(this), l = a + "";
    return a == null && (l = a = (this.style.removeProperty(e), bt(this, e))), s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a));
  };
}
function Pd(e, t) {
  var n, o, r, i = "style." + t, s = "end." + i, a;
  return function() {
    var l = Xe(this, e), c = l.on, u = l.value[i] == null ? a || (a = ws(t)) : void 0;
    (c !== n || r !== u) && (o = (n = c).copy()).on(s, r = u), l.on = o;
  };
}
function $d(e, t, n) {
  var o = (e += "") == "transform" ? Tu : xs;
  return t == null ? this.styleTween(e, Ad(e, o)).on("end.style." + e, ws(e)) : typeof t == "function" ? this.styleTween(e, jd(e, o, rr(this, "style." + e, t))).each(Pd(this._id, e)) : this.styleTween(e, Dd(e, o, t), n).on("end.style." + e, null);
}
function Td(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function zd(e, t, n) {
  var o, r;
  function i() {
    var s = t.apply(this, arguments);
    return s !== r && (o = (r = s) && Td(e, s, n)), o;
  }
  return i._value = t, i;
}
function Rd(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, zd(e, t, n ?? ""));
}
function Ld(e) {
  return function() {
    this.textContent = e;
  };
}
function Hd(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function Vd(e) {
  return this.tween("text", typeof e == "function" ? Hd(rr(this, "text", e)) : Ld(e == null ? "" : e + ""));
}
function Od(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Bd(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Od(r)), t;
  }
  return o._value = e, o;
}
function Fd(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, Bd(e));
}
function Yd() {
  for (var e = this._name, t = this._id, n = vs(), o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], a = s.length, l, c = 0; c < a; ++c)
      if (l = s[c]) {
        var u = Ve(l, t);
        Yn(l, e, n, c, s, {
          time: u.time + u.delay + u.duration,
          delay: 0,
          duration: u.duration,
          ease: u.ease
        });
      }
  return new Ge(o, this._parents, e, n);
}
function Xd() {
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
var Wd = 0;
function Ge(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function vs() {
  return ++Wd;
}
var Ue = tn.prototype;
Ge.prototype = {
  constructor: Ge,
  select: Cd,
  selectAll: kd,
  selectChild: Ue.selectChild,
  selectChildren: Ue.selectChildren,
  filter: wd,
  merge: vd,
  selection: Md,
  transition: Yd,
  call: Ue.call,
  nodes: Ue.nodes,
  node: Ue.node,
  size: Ue.size,
  empty: Ue.empty,
  each: Ue.each,
  on: Ed,
  attr: od,
  attrTween: cd,
  style: $d,
  styleTween: Rd,
  text: Vd,
  textTween: Fd,
  remove: _d,
  tween: Ku,
  delay: dd,
  duration: pd,
  ease: md,
  easeVarying: xd,
  end: Xd,
  [Symbol.iterator]: Ue[Symbol.iterator]
};
function qd(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var Zd = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: qd
};
function Ud(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function Kd(e) {
  var t, n;
  e instanceof Ge ? (t = e._id, e = e._name) : (t = vs(), (n = Zd).time = nr(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], a = s.length, l, c = 0; c < a; ++c)
      (l = s[c]) && Yn(l, e, t, c, s, n || Ud(l, t));
  return new Ge(o, this._parents, e, t);
}
tn.prototype.interrupt = qu;
tn.prototype.transition = Kd;
const gn = (e) => () => e;
function Gd(e, {
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
var Xn = new Ke(1, 0, 0);
bs.prototype = Ke.prototype;
function bs(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return Xn;
  return e.__zoom;
}
function bo(e) {
  e.stopImmediatePropagation();
}
function Lt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Qd(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function Jd() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function qr() {
  return this.__zoom || Xn;
}
function ef(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function tf() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function nf(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], i = e.invertY(t[0][1]) - n[0][1], s = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    s > i ? (i + s) / 2 : Math.min(0, i) || Math.max(0, s)
  );
}
function Ss() {
  var e = Qd, t = Jd, n = nf, o = ef, r = tf, i = [0, 1 / 0], s = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], a = 250, l = bn, c = On("start", "zoom", "end"), u, d, f, m = 500, g = 150, y = 0, w = 10;
  function x(b) {
    b.property("__zoom", qr).on("wheel.zoom", D, { passive: !1 }).on("mousedown.zoom", j).on("dblclick.zoom", F).filter(r).on("touchstart.zoom", k).on("touchmove.zoom", z).on("touchend.zoom touchcancel.zoom", H).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  x.transform = function(b, C, _, A) {
    var $ = b.selection ? b.selection() : b;
    $.property("__zoom", qr), b !== $ ? N(b, C, _, A) : $.interrupt().each(function() {
      E(this, arguments).event(A).start().zoom(null, typeof C == "function" ? C.apply(this, arguments) : C).end();
    });
  }, x.scaleBy = function(b, C, _, A) {
    x.scaleTo(b, function() {
      var $ = this.__zoom.k, P = typeof C == "function" ? C.apply(this, arguments) : C;
      return $ * P;
    }, _, A);
  }, x.scaleTo = function(b, C, _, A) {
    x.transform(b, function() {
      var $ = t.apply(this, arguments), P = this.__zoom, B = _ == null ? v($) : typeof _ == "function" ? _.apply(this, arguments) : _, V = P.invert(B), O = typeof C == "function" ? C.apply(this, arguments) : C;
      return n(p(S(P, O), B, V), $, s);
    }, _, A);
  }, x.translateBy = function(b, C, _, A) {
    x.transform(b, function() {
      return n(this.__zoom.translate(
        typeof C == "function" ? C.apply(this, arguments) : C,
        typeof _ == "function" ? _.apply(this, arguments) : _
      ), t.apply(this, arguments), s);
    }, null, A);
  }, x.translateTo = function(b, C, _, A, $) {
    x.transform(b, function() {
      var P = t.apply(this, arguments), B = this.__zoom, V = A == null ? v(P) : typeof A == "function" ? A.apply(this, arguments) : A;
      return n(Xn.translate(V[0], V[1]).scale(B.k).translate(
        typeof C == "function" ? -C.apply(this, arguments) : -C,
        typeof _ == "function" ? -_.apply(this, arguments) : -_
      ), P, s);
    }, A, $);
  };
  function S(b, C) {
    return C = Math.max(i[0], Math.min(i[1], C)), C === b.k ? b : new Ke(C, b.x, b.y);
  }
  function p(b, C, _) {
    var A = C[0] - _[0] * b.k, $ = C[1] - _[1] * b.k;
    return A === b.x && $ === b.y ? b : new Ke(b.k, A, $);
  }
  function v(b) {
    return [(+b[0][0] + +b[1][0]) / 2, (+b[0][1] + +b[1][1]) / 2];
  }
  function N(b, C, _, A) {
    b.on("start.zoom", function() {
      E(this, arguments).event(A).start();
    }).on("interrupt.zoom end.zoom", function() {
      E(this, arguments).event(A).end();
    }).tween("zoom", function() {
      var $ = this, P = arguments, B = E($, P).event(A), V = t.apply($, P), O = _ == null ? v(V) : typeof _ == "function" ? _.apply($, P) : _, U = Math.max(V[1][0] - V[0][0], V[1][1] - V[0][1]), K = $.__zoom, X = typeof C == "function" ? C.apply($, P) : C, ie = l(K.invert(O).concat(U / K.k), X.invert(O).concat(U / X.k));
      return function(G) {
        if (G === 1) G = X;
        else {
          var T = ie(G), Z = U / T[2];
          G = new Ke(Z, O[0] - T[0] * Z, O[1] - T[1] * Z);
        }
        B.zoom(null, G);
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
      var C = Me(this.that).datum();
      c.call(
        b,
        this.that,
        new Gd(b, {
          sourceEvent: this.sourceEvent,
          target: x,
          transform: this.that.__zoom,
          dispatch: c
        }),
        C
      );
    }
  };
  function D(b, ...C) {
    if (!e.apply(this, arguments)) return;
    var _ = E(this, C).event(b), A = this.__zoom, $ = Math.max(i[0], Math.min(i[1], A.k * Math.pow(2, o.apply(this, arguments)))), P = ze(b);
    if (_.wheel)
      (_.mouse[0][0] !== P[0] || _.mouse[0][1] !== P[1]) && (_.mouse[1] = A.invert(_.mouse[0] = P)), clearTimeout(_.wheel);
    else {
      if (A.k === $) return;
      _.mouse = [P, A.invert(P)], Nn(this), _.start();
    }
    Lt(b), _.wheel = setTimeout(B, g), _.zoom("mouse", n(p(S(A, $), _.mouse[0], _.mouse[1]), _.extent, s));
    function B() {
      _.wheel = null, _.end();
    }
  }
  function j(b, ...C) {
    if (f || !e.apply(this, arguments)) return;
    var _ = b.currentTarget, A = E(this, C, !0).event(b), $ = Me(b.view).on("mousemove.zoom", O, !0).on("mouseup.zoom", U, !0), P = ze(b, _), B = b.clientX, V = b.clientY;
    is(b.view), bo(b), A.mouse = [P, this.__zoom.invert(P)], Nn(this), A.start();
    function O(K) {
      if (Lt(K), !A.moved) {
        var X = K.clientX - B, ie = K.clientY - V;
        A.moved = X * X + ie * ie > y;
      }
      A.event(K).zoom("mouse", n(p(A.that.__zoom, A.mouse[0] = ze(K, _), A.mouse[1]), A.extent, s));
    }
    function U(K) {
      $.on("mousemove.zoom mouseup.zoom", null), ss(K.view, A.moved), Lt(K), A.event(K).end();
    }
  }
  function F(b, ...C) {
    if (e.apply(this, arguments)) {
      var _ = this.__zoom, A = ze(b.changedTouches ? b.changedTouches[0] : b, this), $ = _.invert(A), P = _.k * (b.shiftKey ? 0.5 : 2), B = n(p(S(_, P), A, $), t.apply(this, C), s);
      Lt(b), a > 0 ? Me(this).transition().duration(a).call(N, B, A, b) : Me(this).call(x.transform, B, A, b);
    }
  }
  function k(b, ...C) {
    if (e.apply(this, arguments)) {
      var _ = b.touches, A = _.length, $ = E(this, C, b.changedTouches.length === A).event(b), P, B, V, O;
      for (bo(b), B = 0; B < A; ++B)
        V = _[B], O = ze(V, this), O = [O, this.__zoom.invert(O), V.identifier], $.touch0 ? !$.touch1 && $.touch0[2] !== O[2] && ($.touch1 = O, $.taps = 0) : ($.touch0 = O, P = !0, $.taps = 1 + !!u);
      u && (u = clearTimeout(u)), P && ($.taps < 2 && (d = O[0], u = setTimeout(function() {
        u = null;
      }, m)), Nn(this), $.start());
    }
  }
  function z(b, ...C) {
    if (this.__zooming) {
      var _ = E(this, C).event(b), A = b.changedTouches, $ = A.length, P, B, V, O;
      for (Lt(b), P = 0; P < $; ++P)
        B = A[P], V = ze(B, this), _.touch0 && _.touch0[2] === B.identifier ? _.touch0[0] = V : _.touch1 && _.touch1[2] === B.identifier && (_.touch1[0] = V);
      if (B = _.that.__zoom, _.touch1) {
        var U = _.touch0[0], K = _.touch0[1], X = _.touch1[0], ie = _.touch1[1], G = (G = X[0] - U[0]) * G + (G = X[1] - U[1]) * G, T = (T = ie[0] - K[0]) * T + (T = ie[1] - K[1]) * T;
        B = S(B, Math.sqrt(G / T)), V = [(U[0] + X[0]) / 2, (U[1] + X[1]) / 2], O = [(K[0] + ie[0]) / 2, (K[1] + ie[1]) / 2];
      } else if (_.touch0) V = _.touch0[0], O = _.touch0[1];
      else return;
      _.zoom("touch", n(p(B, V, O), _.extent, s));
    }
  }
  function H(b, ...C) {
    if (this.__zooming) {
      var _ = E(this, C).event(b), A = b.changedTouches, $ = A.length, P, B;
      for (bo(b), f && clearTimeout(f), f = setTimeout(function() {
        f = null;
      }, m), P = 0; P < $; ++P)
        B = A[P], _.touch0 && _.touch0[2] === B.identifier ? delete _.touch0 : _.touch1 && _.touch1[2] === B.identifier && delete _.touch1;
      if (_.touch1 && !_.touch0 && (_.touch0 = _.touch1, delete _.touch1), _.touch0) _.touch0[1] = this.__zoom.invert(_.touch0[0]);
      else if (_.end(), _.taps === 2 && (B = ze(B, this), Math.hypot(d[0] - B[0], d[1] - B[1]) < w)) {
        var V = Me(this).on("dblclick.zoom");
        V && V.apply(this, arguments);
      }
    }
  }
  return x.wheelDelta = function(b) {
    return arguments.length ? (o = typeof b == "function" ? b : gn(+b), x) : o;
  }, x.filter = function(b) {
    return arguments.length ? (e = typeof b == "function" ? b : gn(!!b), x) : e;
  }, x.touchable = function(b) {
    return arguments.length ? (r = typeof b == "function" ? b : gn(!!b), x) : r;
  }, x.extent = function(b) {
    return arguments.length ? (t = typeof b == "function" ? b : gn([[+b[0][0], +b[0][1]], [+b[1][0], +b[1][1]]]), x) : t;
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
}, Zt = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], Es = ["Enter", " ", "Escape"], Ns = {
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
var Ut;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Ut || (Ut = {}));
const _s = {
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
var tt;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(tt || (tt = {}));
var $n;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})($n || ($n = {}));
var J;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(J || (J = {}));
const Zr = {
  [J.Left]: J.Right,
  [J.Right]: J.Left,
  [J.Top]: J.Bottom,
  [J.Bottom]: J.Top
};
function Cs(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const ks = (e) => "id" in e && "source" in e && "target" in e, of = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), ir = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), on = (e, t = [0, 0]) => {
  const { width: n, height: o } = Qe(e), r = e.origin ?? t, i = n * r[0], s = o * r[1];
  return {
    x: e.position.x - i,
    y: e.position.y - s
  };
}, rf = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const i = typeof r == "string";
    let s = !t.nodeLookup && !i ? r : void 0;
    t.nodeLookup && (s = i ? t.nodeLookup.get(r) : ir(r) ? r : t.nodeLookup.get(r.id));
    const a = s ? Tn(s, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return Wn(o, a);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return qn(n);
}, rn = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = Wn(n, Tn(r)), o = !0);
  }), o ? qn(n) : { x: 0, y: 0, width: 0, height: 0 };
}, sr = (e, t, [n, o, r] = [0, 0, 1], i = !1, s = !1) => {
  const a = {
    ...jt(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, l = [];
  for (const c of e.values()) {
    const { measured: u, selectable: d = !0, hidden: f = !1 } = c;
    if (s && !d || f)
      continue;
    const m = u.width ?? c.width ?? c.initialWidth ?? null, g = u.height ?? c.height ?? c.initialHeight ?? null, y = Kt(a, _t(c)), w = (m ?? 0) * (g ?? 0), x = i && y > 0;
    (!c.internals.handleBounds || x || y >= w || c.dragging) && l.push(c);
  }
  return l;
}, sf = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function af(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function cf({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: i }, s) {
  if (e.size === 0)
    return !0;
  const a = af(e, s), l = rn(a), c = cr(l, t, n, s?.minZoom ?? r, s?.maxZoom ?? i, s?.padding ?? 0.1);
  return await o.setViewport(c, {
    duration: s?.duration,
    ease: s?.ease,
    interpolate: s?.interpolate
  }), !0;
}
function Is({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: i }) {
  const s = n.get(e), a = s.parentId ? n.get(s.parentId) : void 0, { x: l, y: c } = a ? a.internals.positionAbsolute : { x: 0, y: 0 }, u = s.origin ?? o;
  let d = s.extent || r;
  if (s.extent === "parent" && !s.expandParent)
    if (!a)
      i?.("005", je.error005());
    else {
      const m = a.measured.width, g = a.measured.height;
      m && g && (d = [
        [l, c],
        [l + m, c + g]
      ]);
    }
  else a && pt(s.extent) && (d = [
    [s.extent[0][0] + l, s.extent[0][1] + c],
    [s.extent[1][0] + l, s.extent[1][1] + c]
  ]);
  const f = pt(d) ? ht(t, d, s.measured) : t;
  return (s.measured.width === void 0 || s.measured.height === void 0) && i?.("015", je.error015()), {
    position: {
      x: f.x - l + (s.measured.width ?? 0) * u[0],
      y: f.y - c + (s.measured.height ?? 0) * u[1]
    },
    positionAbsolute: f
  };
}
async function lf({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const i = new Set(e.map((f) => f.id)), s = [];
  for (const f of n) {
    if (f.deletable === !1)
      continue;
    const m = i.has(f.id), g = !m && f.parentId && s.find((y) => y.id === f.parentId);
    (m || g) && s.push(f);
  }
  const a = new Set(t.map((f) => f.id)), l = o.filter((f) => f.deletable !== !1), u = sf(s, l);
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
const Nt = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), ht = (e = { x: 0, y: 0 }, t, n) => ({
  x: Nt(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: Nt(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Ms(e, t, n) {
  const { width: o, height: r } = Qe(n), { x: i, y: s } = n.internals.positionAbsolute;
  return ht(e, [
    [i, s],
    [i + o, s + r]
  ], t);
}
const Ur = (e, t, n) => e < t ? Nt(Math.abs(e - t), 1, t) / t : e > n ? -Nt(Math.abs(e - n), 1, t) / t : 0, ar = (e, t, n = 15, o = 40) => {
  const r = Ur(e.x, o, t.width - o) * n, i = Ur(e.y, o, t.height - o) * n;
  return [r, i];
}, Wn = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), Fo = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), qn = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), _t = (e, t = [0, 0]) => {
  const { x: n, y: o } = ir(e) ? e.internals.positionAbsolute : on(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, Tn = (e, t = [0, 0]) => {
  const { x: n, y: o } = ir(e) ? e.internals.positionAbsolute : on(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, As = (e, t) => qn(Wn(Fo(e), Fo(t))), Kt = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, Kr = (e) => Le(e.width) && Le(e.height) && Le(e.x) && Le(e.y), Le = (e) => !isNaN(e) && isFinite(e), Ds = (e, t) => (n, o) => {
}, sn = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), jt = ({ x: e, y: t }, [n, o, r], i = !1, s = [1, 1]) => {
  const a = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return i ? sn(a, s) : a;
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
function uf(e, t, n) {
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
function df(e, t, n, o, r, i) {
  const { x: s, y: a } = Ct(e, [t, n, o]), { x: l, y: c } = Ct({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), u = r - l, d = i - c;
  return {
    left: Math.floor(s),
    top: Math.floor(a),
    right: Math.floor(u),
    bottom: Math.floor(d)
  };
}
const cr = (e, t, n, o, r, i) => {
  const s = uf(i, t, n), a = (t - s.x) / e.width, l = (n - s.y) / e.height, c = Math.min(a, l), u = Nt(c, o, r), d = e.x + e.width / 2, f = e.y + e.height / 2, m = t / 2 - d * u, g = n / 2 - f * u, y = df(e, m, g, u, t, n), w = {
    left: Math.min(y.left - s.left, 0),
    top: Math.min(y.top - s.top, 0),
    right: Math.min(y.right - s.right, 0),
    bottom: Math.min(y.bottom - s.bottom, 0)
  };
  return {
    x: m - w.left + w.right,
    y: g - w.top + w.bottom,
    zoom: u
  };
}, Gt = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function pt(e) {
  return e != null && e !== "parent";
}
function Qe(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function js(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function Ps(e, t = { width: 0, height: 0 }, n, o, r) {
  const i = { ...e }, s = o.get(n);
  if (s) {
    const a = s.origin || r;
    i.x += s.internals.positionAbsolute.x - (t.width ?? 0) * a[0], i.y += s.internals.positionAbsolute.y - (t.height ?? 0) * a[1];
  }
  return i;
}
function Gr(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function ff() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function hf(e) {
  return { ...Ns, ...e || {} };
}
function Ft(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: i, y: s } = He(e), a = jt({ x: i - (r?.left ?? 0), y: s - (r?.top ?? 0) }, o), { x: l, y: c } = n ? sn(a, t) : a;
  return {
    xSnapped: l,
    ySnapped: c,
    ...a
  };
}
const lr = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), $s = (e) => e?.getRootNode?.() || window?.document, pf = ["INPUT", "SELECT", "TEXTAREA"];
function Ts(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : pf.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const zs = (e) => "clientX" in e, He = (e, t) => {
  const n = zs(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, Qr = (e, t, n, o, r) => {
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
      ...lr(s)
    };
  });
};
function Rs({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: i, targetControlX: s, targetControlY: a }) {
  const l = e * 0.125 + r * 0.375 + s * 0.375 + n * 0.125, c = t * 0.125 + i * 0.375 + a * 0.375 + o * 0.125, u = Math.abs(l - e), d = Math.abs(c - t);
  return [l, c, u, d];
}
function mn(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function Jr({ pos: e, x1: t, y1: n, x2: o, y2: r, c: i }) {
  switch (e) {
    case J.Left:
      return [t - mn(t - o, i), n];
    case J.Right:
      return [t + mn(o - t, i), n];
    case J.Top:
      return [t, n - mn(n - r, i)];
    case J.Bottom:
      return [t, n + mn(r - n, i)];
  }
}
function Ls({ sourceX: e, sourceY: t, sourcePosition: n = J.Bottom, targetX: o, targetY: r, targetPosition: i = J.Top, curvature: s = 0.25 }) {
  const [a, l] = Jr({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: s
  }), [c, u] = Jr({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: s
  }), [d, f, m, g] = Rs({
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
    m,
    g
  ];
}
function Hs({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, i = n < e ? n + r : n - r, s = Math.abs(o - t) / 2, a = o < t ? o + s : o - s;
  return [i, a, r, s];
}
function gf({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: i = "basic" }) {
  if (i === "manual")
    return o;
  const s = r && n ? o + 1e3 : o, a = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return s + a;
}
function mf({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const i = Wn(Tn(e), Tn(t));
  i.x === i.x2 && (i.x2 += 1), i.y === i.y2 && (i.y2 += 1);
  const s = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return Kt(s, qn(i)) > 0;
}
const Vs = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, yf = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), xf = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", je.error006()), t;
  const o = n.getEdgeId || Vs;
  let r;
  return ks(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, yf(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, wf = (e, t, n, o = { shouldReplaceId: !0 }) => {
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
const ei = {
  [J.Left]: { x: -1, y: 0 },
  [J.Right]: { x: 1, y: 0 },
  [J.Top]: { x: 0, y: -1 },
  [J.Bottom]: { x: 0, y: 1 }
}, vf = ({ source: e, sourcePosition: t = J.Bottom, target: n }) => t === J.Left || t === J.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, ti = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function bf({ source: e, sourcePosition: t = J.Bottom, target: n, targetPosition: o = J.Top, center: r, offset: i, stepPosition: s }) {
  const a = ei[t], l = ei[o], c = { x: e.x + a.x * i, y: e.y + a.y * i }, u = { x: n.x + l.x * i, y: n.y + l.y * i }, d = vf({
    source: c,
    sourcePosition: t,
    target: u
  }), f = d.x !== 0 ? "x" : "y", m = d[f];
  let g = [], y, w;
  const x = { x: 0, y: 0 }, S = { x: 0, y: 0 }, [, , p, v] = Hs({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (a[f] * l[f] === -1) {
    f === "x" ? (y = r.x ?? c.x + (u.x - c.x) * s, w = r.y ?? (c.y + u.y) / 2) : (y = r.x ?? (c.x + u.x) / 2, w = r.y ?? c.y + (u.y - c.y) * s);
    const D = [
      { x: y, y: c.y },
      { x: y, y: u.y }
    ], j = [
      { x: c.x, y: w },
      { x: u.x, y: w }
    ];
    a[f] === m ? g = f === "x" ? D : j : g = f === "x" ? j : D;
  } else {
    const D = [{ x: c.x, y: u.y }], j = [{ x: u.x, y: c.y }];
    if (f === "x" ? g = a.x === m ? j : D : g = a.y === m ? D : j, t === o) {
      const b = Math.abs(e[f] - n[f]);
      if (b <= i) {
        const C = Math.min(i - 1, i - b);
        a[f] === m ? x[f] = (c[f] > e[f] ? -1 : 1) * C : S[f] = (u[f] > n[f] ? -1 : 1) * C;
      }
    }
    if (t !== o) {
      const b = f === "x" ? "y" : "x", C = a[f] === l[b], _ = c[b] > u[b], A = c[b] < u[b];
      (a[f] === 1 && (!C && _ || C && A) || a[f] !== 1 && (!C && A || C && _)) && (g = f === "x" ? D : j);
    }
    const F = { x: c.x + x.x, y: c.y + x.y }, k = { x: u.x + S.x, y: u.y + S.y }, z = Math.max(Math.abs(F.x - g[0].x), Math.abs(k.x - g[0].x)), H = Math.max(Math.abs(F.y - g[0].y), Math.abs(k.y - g[0].y));
    z >= H ? (y = (F.x + k.x) / 2, w = g[0].y) : (y = g[0].x, w = (F.y + k.y) / 2);
  }
  const N = { x: c.x + x.x, y: c.y + x.y }, E = { x: u.x + S.x, y: u.y + S.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...N.x !== g[0].x || N.y !== g[0].y ? [N] : [],
    ...g,
    ...E.x !== g[g.length - 1].x || E.y !== g[g.length - 1].y ? [E] : [],
    n
  ], y, w, p, v];
}
function Sf(e, t, n, o) {
  const r = Math.min(ti(e, t) / 2, ti(t, n) / 2, o), { x: i, y: s } = t;
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
  const [d, f, m, g, y] = bf({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: i,
    center: { x: a, y: l },
    offset: c,
    stepPosition: u
  });
  let w = `M${d[0].x} ${d[0].y}`;
  for (let x = 1; x < d.length - 1; x++)
    w += Sf(d[x - 1], d[x], d[x + 1], s);
  return w += `L${d[d.length - 1].x} ${d[d.length - 1].y}`, [w, f, m, g, y];
}
function ni(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function Ef(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!ni(t) || !ni(n))
    return null;
  const o = t.internals.handleBounds || oi(t.handles), r = n.internals.handleBounds || oi(n.handles), i = ri(o?.source ?? [], e.sourceHandle), s = ri(
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
  const a = i?.position || J.Bottom, l = s?.position || J.Top, c = gt(t, i, a), u = gt(n, s, l);
  return {
    sourceX: c.x,
    sourceY: c.y,
    targetX: u.x,
    targetY: u.y,
    sourcePosition: a,
    targetPosition: l
  };
}
function oi(e) {
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
function gt(e, t, n = J.Left, o = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, i = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: s, height: a } = t ?? Qe(e);
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
function ri(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Yo(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function Nf(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const i = /* @__PURE__ */ new Set();
  return e.reduce((s, a) => ([a.markerStart || o, a.markerEnd || r].forEach((l) => {
    if (l && typeof l == "object") {
      const c = Yo(l, t);
      i.has(c) || (s.push({ id: c, color: l.color || n, ...l }), i.add(c));
    }
  }), s), []).sort((s, a) => s.id.localeCompare(a.id));
}
const Bs = 1e3, _f = 10, ur = {
  nodeOrigin: [0, 0],
  nodeExtent: Zt,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Cf = {
  ...ur,
  checkEquality: !0
};
function dr(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function kf(e, t, n) {
  const o = dr(ur, n);
  for (const r of e.values())
    if (r.parentId)
      hr(r, e, t, o);
    else {
      const i = on(r, o.nodeOrigin), s = pt(r.extent) ? r.extent : o.nodeExtent, a = ht(i, s, Qe(r));
      r.internals.positionAbsolute = a;
    }
}
function If(e, t) {
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
function fr(e) {
  return e === "manual";
}
function Xo(e, t, n, o = {}) {
  const r = dr(Cf, o), i = { i: 0 }, s = new Map(t), a = r?.elevateNodesOnSelect && !fr(r.zIndexMode) ? Bs : 0;
  let l = e.length > 0, c = !1;
  t.clear(), n.clear();
  for (const u of e) {
    let d = s.get(u.id);
    if (r.checkEquality && u === d?.internals.userNode)
      t.set(u.id, d);
    else {
      const f = on(u, r.nodeOrigin), m = pt(u.extent) ? u.extent : r.nodeExtent, g = ht(f, m, Qe(u));
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
          handleBounds: If(u, d),
          z: Fs(u, a, r.zIndexMode),
          userNode: u
        }
      }, t.set(u.id, d);
    }
    (d.measured === void 0 || d.measured.width === void 0 || d.measured.height === void 0) && !d.hidden && (l = !1), u.parentId && hr(d, t, n, o, i), c ||= u.selected ?? !1;
  }
  return { nodesInitialized: l, hasSelectedNodes: c };
}
function Mf(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function hr(e, t, n, o, r) {
  const { elevateNodesOnSelect: i, nodeOrigin: s, nodeExtent: a, zIndexMode: l } = dr(ur, o), c = e.parentId, u = t.get(c);
  if (!u) {
    console.warn(`Parent node ${c} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Mf(e, n), r && !u.parentId && u.internals.rootParentIndex === void 0 && l === "auto" && (u.internals.rootParentIndex = ++r.i, u.internals.z = u.internals.z + r.i * _f), r && u.internals.rootParentIndex !== void 0 && (r.i = u.internals.rootParentIndex);
  const d = i && !fr(l) ? Bs : 0, { x: f, y: m, z: g } = Af(e, u, s, a, d, l), { positionAbsolute: y } = e.internals, w = f !== y.x || m !== y.y;
  (w || g !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: w ? { x: f, y: m } : y,
      z: g
    }
  });
}
function Fs(e, t, n) {
  const o = Le(e.zIndex) ? e.zIndex : 0;
  return fr(n) ? o : o + (e.selected ? t : 0);
}
function Af(e, t, n, o, r, i) {
  const { x: s, y: a } = t.internals.positionAbsolute, l = Qe(e), c = on(e, n), u = pt(e.extent) ? ht(c, e.extent, l) : c;
  let d = ht({ x: s + u.x, y: a + u.y }, o, l);
  e.extent === "parent" && (d = Ms(d, l, t));
  const f = Fs(e, r, i), m = t.internals.z ?? 0;
  return {
    x: d.x,
    y: d.y,
    z: m >= f ? m + 1 : f
  };
}
function pr(e, t, n, o = [0, 0]) {
  const r = [], i = /* @__PURE__ */ new Map();
  for (const s of e) {
    const a = t.get(s.parentId);
    if (!a)
      continue;
    const l = i.get(s.parentId)?.expandedRect ?? _t(a), c = As(l, s.rect);
    i.set(s.parentId, { expandedRect: c, parent: a });
  }
  return i.size > 0 && i.forEach(({ expandedRect: s, parent: a }, l) => {
    const c = a.internals.positionAbsolute, u = Qe(a), d = a.origin ?? o, f = s.x < c.x ? Math.round(Math.abs(c.x - s.x)) : 0, m = s.y < c.y ? Math.round(Math.abs(c.y - s.y)) : 0, g = Math.max(u.width, Math.round(s.width)), y = Math.max(u.height, Math.round(s.height)), w = (g - u.width) * d[0], x = (y - u.height) * d[1];
    (f > 0 || m > 0 || w || x) && (r.push({
      id: l,
      type: "position",
      position: {
        x: a.position.x - f + w,
        y: a.position.y - m + x
      }
    }), n.get(l)?.forEach((S) => {
      e.some((p) => p.id === S.id) || r.push({
        id: S.id,
        type: "position",
        position: {
          x: S.position.x + f,
          y: S.position.y + m
        }
      });
    })), (u.width < s.width || u.height < s.height || f || m) && r.push({
      id: l,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: g + (f ? d[0] * f - w : 0),
        height: y + (m ? d[1] * m - x : 0)
      }
    });
  }), r;
}
function Df(e, t, n, o, r, i, s) {
  const a = o?.querySelector(".xyflow__viewport");
  let l = !1;
  if (!a)
    return { changes: [], updatedInternals: l };
  const c = [], u = window.getComputedStyle(a), { m22: d } = new window.DOMMatrixReadOnly(u.transform), f = [];
  for (const m of e.values()) {
    const g = t.get(m.id);
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
    const y = lr(m.nodeElement), w = g.measured.width !== y.width || g.measured.height !== y.height;
    if (!!(y.width && y.height && (w || !g.internals.handleBounds || m.force))) {
      const S = m.nodeElement.getBoundingClientRect(), p = pt(g.extent) ? g.extent : i;
      let { positionAbsolute: v } = g.internals;
      g.parentId && g.extent === "parent" ? v = Ms(v, y, t.get(g.parentId)) : p && (v = ht(v, p, y));
      const N = {
        ...g,
        measured: y,
        internals: {
          ...g.internals,
          positionAbsolute: v,
          handleBounds: {
            source: Qr("source", m.nodeElement, S, d, g.id),
            target: Qr("target", m.nodeElement, S, d, g.id)
          }
        }
      };
      t.set(g.id, N), g.parentId && hr(N, t, n, { nodeOrigin: r, zIndexMode: s }), l = !0, w && (c.push({
        id: g.id,
        type: "dimensions",
        dimensions: y
      }), g.expandParent && g.parentId && f.push({
        id: g.id,
        parentId: g.parentId,
        rect: _t(N, r)
      }));
    }
  }
  if (f.length > 0) {
    const m = pr(f, t, n, r);
    c.push(...m);
  }
  return { changes: c, updatedInternals: l };
}
async function jf({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: i }) {
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
function ii(e, t, n, o, r, i) {
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
function Ys(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: i, sourceHandle: s = null, targetHandle: a = null } = o, l = { edgeId: o.id, source: r, target: i, sourceHandle: s, targetHandle: a }, c = `${r}-${s}--${i}-${a}`, u = `${i}-${a}--${r}-${s}`;
    ii("source", l, u, e, r, s), ii("target", l, c, e, i, a), t.set(o.id, o);
  }
}
function Xs(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : Xs(n, t) : !1;
}
function si(e, t, n) {
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
function Pf(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [i, s] of e)
    if ((s.selected || s.id === o) && (!s.parentId || !Xs(s, e)) && (s.draggable || t && typeof s.draggable > "u")) {
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
function So({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
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
function $f({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const i = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, s = sn(i, t);
  return {
    x: s.x - i.x,
    y: s.y - i.y
  };
}
function Tf({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let i = { x: null, y: null }, s = 0, a = /* @__PURE__ */ new Map(), l = !1, c = { x: 0, y: 0 }, u = null, d = !1, f = null, m = !1, g = !1, y = null;
  function w({ noDragClassName: S, handleSelector: p, domNode: v, isSelectable: N, nodeId: E, nodeClickDistance: M = 0 }) {
    f = Me(v);
    function D({ x: z, y: H }) {
      const { nodeLookup: b, nodeExtent: C, snapGrid: _, snapToGrid: A, nodeOrigin: $, onNodeDrag: P, onSelectionDrag: B, onError: V, updateNodePositions: O } = t();
      i = { x: z, y: H };
      let U = !1;
      const K = a.size > 1, X = K && C ? Fo(rn(a)) : null, ie = K && A ? $f({
        dragItems: a,
        snapGrid: _,
        x: z,
        y: H
      }) : null;
      for (const [G, T] of a) {
        if (!b.has(G))
          continue;
        let Z = { x: z - T.distance.x, y: H - T.distance.y };
        A && (Z = ie ? {
          x: Math.round(Z.x + ie.x),
          y: Math.round(Z.y + ie.y)
        } : sn(Z, _));
        let re = null;
        if (K && C && !T.extent && X) {
          const { positionAbsolute: te } = T.internals, se = te.x - X.x + C[0][0], L = te.x + T.measured.width - X.x2 + C[1][0], W = te.y - X.y + C[0][1], fe = te.y + T.measured.height - X.y2 + C[1][1];
          re = [
            [se, W],
            [L, fe]
          ];
        }
        const { position: oe, positionAbsolute: Q } = Is({
          nodeId: G,
          nextPosition: Z,
          nodeLookup: b,
          nodeExtent: re || C,
          nodeOrigin: $,
          onError: V
        });
        U = U || T.position.x !== oe.x || T.position.y !== oe.y, T.position = oe, T.internals.positionAbsolute = Q;
      }
      if (g = g || U, !!U && (O(a, !0), y && (o || P || !E && B))) {
        const [G, T] = So({
          nodeId: E,
          dragItems: a,
          nodeLookup: b
        });
        o?.(y, a, G, T), P?.(y, G, T), E || B?.(y, T);
      }
    }
    async function j() {
      if (!u)
        return;
      const { transform: z, panBy: H, autoPanSpeed: b, autoPanOnNodeDrag: C } = t();
      if (!C) {
        l = !1, cancelAnimationFrame(s);
        return;
      }
      const [_, A] = ar(c, u, b);
      (_ !== 0 || A !== 0) && (i.x = (i.x ?? 0) - _ / z[2], i.y = (i.y ?? 0) - A / z[2], await H({ x: _, y: A }) && D(i)), s = requestAnimationFrame(j);
    }
    function F(z) {
      const { nodeLookup: H, multiSelectionActive: b, nodesDraggable: C, transform: _, snapGrid: A, snapToGrid: $, selectNodesOnDrag: P, onNodeDragStart: B, onSelectionDragStart: V, unselectNodesAndEdges: O } = t();
      d = !0, (!P || !N) && !b && E && (H.get(E)?.selected || O()), N && P && E && e?.(E);
      const U = Ft(z.sourceEvent, { transform: _, snapGrid: A, snapToGrid: $, containerBounds: u });
      if (i = U, a = Pf(H, C, U, E), a.size > 0 && (n || B || !E && V)) {
        const [K, X] = So({
          nodeId: E,
          dragItems: a,
          nodeLookup: H
        });
        n?.(z.sourceEvent, a, K, X), B?.(z.sourceEvent, K, X), E || V?.(z.sourceEvent, X);
      }
    }
    const k = as().clickDistance(M).on("start", (z) => {
      const { domNode: H, nodeDragThreshold: b, transform: C, snapGrid: _, snapToGrid: A } = t();
      u = H?.getBoundingClientRect() || null, m = !1, g = !1, y = z.sourceEvent, b === 0 && F(z), i = Ft(z.sourceEvent, { transform: C, snapGrid: _, snapToGrid: A, containerBounds: u }), c = He(z.sourceEvent, u);
    }).on("drag", (z) => {
      const { autoPanOnNodeDrag: H, transform: b, snapGrid: C, snapToGrid: _, nodeDragThreshold: A, nodeLookup: $ } = t(), P = Ft(z.sourceEvent, { transform: b, snapGrid: C, snapToGrid: _, containerBounds: u });
      if (y = z.sourceEvent, (z.sourceEvent.type === "touchmove" && z.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      E && !$.has(E)) && (m = !0), !m) {
        if (!l && H && d && (l = !0, j()), !d) {
          const B = He(z.sourceEvent, u), V = B.x - c.x, O = B.y - c.y;
          Math.sqrt(V * V + O * O) > A && F(z);
        }
        (i.x !== P.xSnapped || i.y !== P.ySnapped) && a && d && (c = He(z.sourceEvent, u), D(P));
      }
    }).on("end", (z) => {
      if (!d || m) {
        m && a.size > 0 && t().updateNodePositions(a, !1);
        return;
      }
      if (l = !1, d = !1, cancelAnimationFrame(s), a.size > 0) {
        const { nodeLookup: H, updateNodePositions: b, onNodeDragStop: C, onSelectionDragStop: _ } = t();
        if (g && (b(a, !1), g = !1), r || C || !E && _) {
          const [A, $] = So({
            nodeId: E,
            dragItems: a,
            nodeLookup: H,
            dragging: !1
          });
          r?.(z.sourceEvent, a, A, $), C?.(z.sourceEvent, A, $), E || _?.(z.sourceEvent, $);
        }
      }
    }).filter((z) => {
      const H = z.target;
      return !z.button && (!S || !si(H, `.${S}`, v)) && (!p || si(H, p, v));
    });
    f.call(k);
  }
  function x() {
    f?.on(".drag", null);
  }
  return {
    update: w,
    destroy: x
  };
}
function zf(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const i of t.values())
    Kt(r, _t(i)) > 0 && o.push(i);
  return o;
}
const Rf = 250;
function Lf(e, t, n, o) {
  let r = [], i = 1 / 0;
  const s = zf(e, n, t + Rf);
  for (const a of s) {
    const l = [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []];
    for (const c of l) {
      if (o.nodeId === c.nodeId && o.type === c.type && o.id === c.id)
        continue;
      const { x: u, y: d } = gt(a, c, c.position, !0), f = Math.sqrt(Math.pow(u - e.x, 2) + Math.pow(d - e.y, 2));
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
function Ws(e, t, n, o, r, i = !1) {
  const s = o.get(e);
  if (!s)
    return null;
  const a = r === "strict" ? s.internals.handleBounds?.[t] : [...s.internals.handleBounds?.source ?? [], ...s.internals.handleBounds?.target ?? []], l = (n ? a?.find((c) => c.id === n) : a?.[0]) ?? null;
  return l && i ? { ...l, ...gt(s, l, l.position, !0) } : l;
}
function qs(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Hf(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const Zs = () => !0;
function Vf(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: i, isTarget: s, domNode: a, nodeLookup: l, lib: c, autoPanOnConnect: u, flowId: d, panBy: f, cancelConnection: m, onConnectStart: g, onConnect: y, onConnectEnd: w, isValidConnection: x = Zs, onReconnectEnd: S, updateConnection: p, getTransform: v, getFromHandle: N, autoPanSpeed: E, dragThreshold: M = 1, handleDomNode: D }) {
  const j = $s(e.target);
  let F = 0, k;
  const { x: z, y: H } = He(e), b = qs(i, D), C = a?.getBoundingClientRect();
  let _ = !1;
  if (!C || !b)
    return;
  const A = Ws(r, b, o, l, t);
  if (!A)
    return;
  let $ = He(e, C), P = !1, B = null, V = !1, O = null;
  function U() {
    if (!u || !C)
      return;
    const [oe, Q] = ar($, C, E);
    f({ x: oe, y: Q }), F = requestAnimationFrame(U);
  }
  const K = {
    ...A,
    nodeId: r,
    type: b,
    position: A.position
  }, X = l.get(r);
  let G = {
    inProgress: !0,
    isValid: null,
    from: gt(X, K, J.Left, !0),
    fromHandle: K,
    fromPosition: K.position,
    fromNode: X,
    to: $,
    toHandle: null,
    toPosition: Zr[K.position],
    toNode: null,
    pointer: $
  };
  function T() {
    _ = !0, p(G), g?.(e, { nodeId: r, handleId: o, handleType: b });
  }
  M === 0 && T();
  function Z(oe) {
    if (!_) {
      const { x: fe, y: me } = He(oe), _e = fe - z, Se = me - H;
      if (!(_e * _e + Se * Se > M * M))
        return;
      T();
    }
    if (!N() || !K) {
      re(oe);
      return;
    }
    const Q = v();
    $ = He(oe, C), k = Lf(jt($, Q, !1, [1, 1]), n, l, K), P || (U(), P = !0);
    const te = Us(oe, {
      handle: k,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: s ? "target" : "source",
      isValidConnection: x,
      doc: j,
      lib: c,
      flowId: d,
      nodeLookup: l
    });
    O = te.handleDomNode, B = te.connection, V = Hf(!!k, te.isValid);
    const se = l.get(r), L = se ? gt(se, K, J.Left, !0) : G.from, W = {
      ...G,
      from: L,
      isValid: V,
      to: te.toHandle && V ? Ct({ x: te.toHandle.x, y: te.toHandle.y }, Q) : $,
      toHandle: te.toHandle,
      toPosition: V && te.toHandle ? te.toHandle.position : Zr[K.position],
      toNode: te.toHandle ? l.get(te.toHandle.nodeId) : null,
      pointer: $
    };
    p(W), G = W;
  }
  function re(oe) {
    if (!("touches" in oe && oe.touches.length > 0)) {
      if (_) {
        (k || O) && B && V && y?.(B);
        const { inProgress: Q, ...te } = G, se = {
          ...te,
          toPosition: G.toHandle ? G.toPosition : null
        };
        w?.(oe, se), i && S?.(oe, se);
      }
      m(), cancelAnimationFrame(F), P = !1, V = !1, B = null, O = null, j.removeEventListener("mousemove", Z), j.removeEventListener("mouseup", re), j.removeEventListener("touchmove", Z), j.removeEventListener("touchend", re);
    }
  }
  j.addEventListener("mousemove", Z), j.addEventListener("mouseup", re), j.addEventListener("touchmove", Z), j.addEventListener("touchend", re);
}
function Us(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: i, doc: s, lib: a, flowId: l, isValidConnection: c = Zs, nodeLookup: u }) {
  const d = i === "target", f = t ? s.querySelector(`.${a}-flow__handle[data-id="${l}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: m, y: g } = He(e), y = s.elementFromPoint(m, g), w = y?.classList.contains(`${a}-flow__handle`) ? y : f, x = {
    handleDomNode: w,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (w) {
    const S = qs(void 0, w), p = w.getAttribute("data-nodeid"), v = w.getAttribute("data-handleid"), N = w.classList.contains("connectable"), E = w.classList.contains("connectableend");
    if (!p || !S)
      return x;
    const M = {
      source: d ? p : o,
      sourceHandle: d ? v : r,
      target: d ? o : p,
      targetHandle: d ? r : v
    };
    x.connection = M;
    const j = N && E && (n === Et.Strict ? d && S === "source" || !d && S === "target" : p !== o || v !== r);
    x.isValid = j && c(M), x.toHandle = Ws(p, S, v, u, n, !0);
  }
  return x;
}
const Wo = {
  onPointerDown: Vf,
  isValid: Us
};
function Of({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = Me(e);
  function i({ translateExtent: a, width: l, height: c, zoomStep: u = 1, pannable: d = !0, zoomable: f = !0, inversePan: m = !1 }) {
    const g = (p) => {
      if (p.sourceEvent.type !== "wheel" || !t)
        return;
      const v = n(), N = p.sourceEvent.ctrlKey && Gt() ? 10 : 1, E = -p.sourceEvent.deltaY * (p.sourceEvent.deltaMode === 1 ? 0.05 : p.sourceEvent.deltaMode ? 1 : 2e-3) * u, M = v[2] * Math.pow(2, E * N);
      t.scaleTo(M);
    };
    let y = [0, 0];
    const w = (p) => {
      (p.sourceEvent.type === "mousedown" || p.sourceEvent.type === "touchstart") && (y = [
        p.sourceEvent.clientX ?? p.sourceEvent.touches[0].clientX,
        p.sourceEvent.clientY ?? p.sourceEvent.touches[0].clientY
      ]);
    }, x = (p) => {
      const v = n();
      if (p.sourceEvent.type !== "mousemove" && p.sourceEvent.type !== "touchmove" || !t)
        return;
      const N = [
        p.sourceEvent.clientX ?? p.sourceEvent.touches[0].clientX,
        p.sourceEvent.clientY ?? p.sourceEvent.touches[0].clientY
      ], E = [N[0] - y[0], N[1] - y[1]];
      y = N;
      const M = o() * Math.max(v[2], Math.log(v[2])) * (m ? -1 : 1), D = {
        x: v[0] - E[0] * M,
        y: v[1] - E[1] * M
      }, j = [
        [0, 0],
        [l, c]
      ];
      t.setViewportConstrained({
        x: D.x,
        y: D.y,
        zoom: v[2]
      }, j, a);
    }, S = Ss().on("start", w).on("zoom", d ? x : null).on("zoom.wheel", f ? g : null);
    r.call(S, {});
  }
  function s() {
    r.on("zoom", null);
  }
  return {
    update: i,
    destroy: s,
    pointer: ze
  };
}
const Zn = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), Eo = ({ x: e, y: t, zoom: n }) => Xn.translate(e, t).scale(n), yt = (e, t) => e.target.closest(`.${t}`), Ks = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Bf = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, No = (e, t = 0, n = Bf, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, Gs = (e) => {
  const t = e.ctrlKey && Gt() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Ff({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: i, zoomOnPinch: s, onPanZoomStart: a, onPanZoom: l, onPanZoomEnd: c }) {
  return (u) => {
    if (yt(u, t))
      return u.ctrlKey && u.preventDefault(), !1;
    u.preventDefault(), u.stopImmediatePropagation();
    const d = n.property("__zoom").k || 1;
    if (u.ctrlKey && s) {
      const w = ze(u), x = Gs(u), S = d * Math.pow(2, x);
      o.scaleTo(n, S, w, u);
      return;
    }
    const f = u.deltaMode === 1 ? 20 : 1;
    let m = r === ut.Vertical ? 0 : u.deltaX * f, g = r === ut.Horizontal ? 0 : u.deltaY * f;
    !Gt() && u.shiftKey && r !== ut.Vertical && (m = u.deltaY * f, g = 0), o.translateBy(
      n,
      -(m / d) * i,
      -(g / d) * i,
      // @ts-ignore
      { internal: !0 }
    );
    const y = Zn(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (l?.(u, y), e.panScrollTimeout = setTimeout(() => {
      c?.(u, y), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, a?.(u, y));
  };
}
function Yf({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const i = o.type === "wheel", s = !t && i && !o.ctrlKey, a = yt(o, e);
    if (o.ctrlKey && i && a && o.preventDefault(), s || a)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function Xf({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = Zn(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function Wf({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (i) => {
    e.usedRightMouseButton = !!(n && Ks(t, e.mouseButton ?? 0)), i.sourceEvent?.sync || o([i.transform.x, i.transform.y, i.transform.k]), r && !i.sourceEvent?.internal && r?.(i.sourceEvent, Zn(i.transform));
  };
}
function qf({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: i }) {
  return (s) => {
    if (!s.sourceEvent?.internal && (e.isZoomingOrPanning = !1, i && Ks(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && s.sourceEvent && i(s.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
      const a = Zn(s.transform);
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
function Zf({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: i, userSelectionActive: s, noWheelClassName: a, noPanClassName: l, lib: c, connectionInProgress: u }) {
  return (d) => {
    const f = e || t, m = n && d.ctrlKey, g = d.type === "wheel";
    if (d.button === 1 && d.type === "mousedown" && (yt(d, `${c}-flow__node`) || yt(d, `${c}-flow__edge`)))
      return !0;
    if (!o && !f && !r && !i && !n || s || u && !g || yt(d, a) && g || yt(d, l) && (!g || r && g && !e) || !n && d.ctrlKey && g)
      return !1;
    if (!n && d.type === "touchstart" && d.touches?.length > 1)
      return d.preventDefault(), !1;
    if (!f && !r && !m && g || !o && (d.type === "mousedown" || d.type === "touchstart") || Array.isArray(o) && !o.includes(d.button) && d.type === "mousedown")
      return !1;
    const y = Array.isArray(o) && o.includes(d.button) || !d.button || d.button <= 1;
    return (!d.ctrlKey || g) && y;
  };
}
function Uf({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: i, onPanZoomStart: s, onPanZoomEnd: a, onDraggingChange: l }) {
  const c = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, u = e.getBoundingClientRect(), d = Ss().scaleExtent([t, n]).translateExtent(o), f = Me(e).call(d);
  S({
    x: r.x,
    y: r.y,
    zoom: Nt(r.zoom, t, n)
  }, [
    [0, 0],
    [u.width, u.height]
  ], o);
  const m = f.on("wheel.zoom"), g = f.on("dblclick.zoom");
  d.wheelDelta(Gs);
  async function y(k, z) {
    return f ? new Promise((H) => {
      d?.interpolate(z?.interpolate === "linear" ? Bt : bn).transform(No(f, z?.duration, z?.ease, () => H(!0)), k);
    }) : !1;
  }
  function w({ noWheelClassName: k, noPanClassName: z, onPaneContextMenu: H, userSelectionActive: b, panOnScroll: C, panOnDrag: _, panOnScrollMode: A, panOnScrollSpeed: $, preventScrolling: P, zoomOnPinch: B, zoomOnScroll: V, zoomOnDoubleClick: O, zoomActivationKeyPressed: U, lib: K, onTransformChange: X, connectionInProgress: ie, paneClickDistance: G, selectionOnDrag: T }) {
    b && !c.isZoomingOrPanning && x();
    const Z = C && !U && !b;
    d.clickDistance(T ? 1 / 0 : !Le(G) || G < 0 ? 0 : G);
    const re = Z ? Ff({
      zoomPanValues: c,
      noWheelClassName: k,
      d3Selection: f,
      d3Zoom: d,
      panOnScrollMode: A,
      panOnScrollSpeed: $,
      zoomOnPinch: B,
      onPanZoomStart: s,
      onPanZoom: i,
      onPanZoomEnd: a
    }) : Yf({
      noWheelClassName: k,
      preventScrolling: P,
      d3ZoomHandler: m
    });
    f.on("wheel.zoom", re, { passive: !1 });
    const oe = Xf({
      zoomPanValues: c,
      onDraggingChange: l,
      onPanZoomStart: s
    });
    d.on("start", oe);
    const Q = Wf({
      zoomPanValues: c,
      panOnDrag: _,
      onPaneContextMenu: !!H,
      onPanZoom: i,
      onTransformChange: X
    });
    d.on("zoom", Q);
    const te = qf({
      zoomPanValues: c,
      panOnDrag: _,
      panOnScroll: C,
      onPaneContextMenu: H,
      onPanZoomEnd: a,
      onDraggingChange: l
    });
    d.on("end", te);
    const se = Zf({
      zoomActivationKeyPressed: U,
      panOnDrag: _,
      zoomOnScroll: V,
      panOnScroll: C,
      zoomOnDoubleClick: O,
      zoomOnPinch: B,
      userSelectionActive: b,
      noPanClassName: z,
      noWheelClassName: k,
      lib: K,
      connectionInProgress: ie
    });
    d.filter(se), O ? f.on("dblclick.zoom", g) : f.on("dblclick.zoom", null);
  }
  function x() {
    d.on("zoom", null);
  }
  async function S(k, z, H) {
    const b = Eo(k), C = d?.constrain()(b, z, H);
    return C && await y(C), C;
  }
  async function p(k, z) {
    const H = Eo(k);
    return await y(H, z), H;
  }
  function v(k) {
    if (f) {
      const z = Eo(k), H = f.property("__zoom");
      (H.k !== k.zoom || H.x !== k.x || H.y !== k.y) && d?.transform(f, z, null, { sync: !0 });
    }
  }
  function N() {
    const k = f ? bs(f.node()) : { x: 0, y: 0, k: 1 };
    return { x: k.x, y: k.y, zoom: k.k };
  }
  async function E(k, z) {
    return f ? new Promise((H) => {
      d?.interpolate(z?.interpolate === "linear" ? Bt : bn).scaleTo(No(f, z?.duration, z?.ease, () => H(!0)), k);
    }) : !1;
  }
  async function M(k, z) {
    return f ? new Promise((H) => {
      d?.interpolate(z?.interpolate === "linear" ? Bt : bn).scaleBy(No(f, z?.duration, z?.ease, () => H(!0)), k);
    }) : !1;
  }
  function D(k) {
    d?.scaleExtent(k);
  }
  function j(k) {
    d?.translateExtent(k);
  }
  function F(k) {
    const z = !Le(k) || k < 0 ? 0 : k;
    d?.clickDistance(z);
  }
  return {
    update: w,
    destroy: x,
    setViewport: p,
    setViewportConstrained: S,
    getViewport: N,
    scaleTo: E,
    scaleBy: M,
    setScaleExtent: D,
    setTranslateExtent: j,
    syncViewport: v,
    setClickDistance: F
  };
}
var kt;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(kt || (kt = {}));
function Kf({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: i }) {
  const s = e - t, a = n - o, l = [s > 0 ? 1 : s < 0 ? -1 : 0, a > 0 ? 1 : a < 0 ? -1 : 0];
  return s && r && (l[0] = l[0] * -1), a && i && (l[1] = l[1] * -1), l;
}
function ai(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: r
  };
}
function Je(e, t) {
  return Math.max(0, t - e);
}
function et(e, t) {
  return Math.max(0, e - t);
}
function yn(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function ci(e, t) {
  return e ? !t : t;
}
function Gf(e, t, n, o, r, i, s, a) {
  let { affectsX: l, affectsY: c } = t;
  const { isHorizontal: u, isVertical: d } = t, f = u && d, { xSnapped: m, ySnapped: g } = n, { minWidth: y, maxWidth: w, minHeight: x, maxHeight: S } = o, { x: p, y: v, width: N, height: E, aspectRatio: M } = e;
  let D = Math.floor(u ? m - e.pointerX : 0), j = Math.floor(d ? g - e.pointerY : 0);
  const F = N + (l ? -D : D), k = E + (c ? -j : j), z = -i[0] * N, H = -i[1] * E;
  let b = yn(F, y, w), C = yn(k, x, S);
  if (s) {
    let $ = 0, P = 0;
    l && D < 0 ? $ = Je(p + D + z, s[0][0]) : !l && D > 0 && ($ = et(p + F + z, s[1][0])), c && j < 0 ? P = Je(v + j + H, s[0][1]) : !c && j > 0 && (P = et(v + k + H, s[1][1])), b = Math.max(b, $), C = Math.max(C, P);
  }
  if (a) {
    let $ = 0, P = 0;
    l && D > 0 ? $ = et(p + D, a[0][0]) : !l && D < 0 && ($ = Je(p + F, a[1][0])), c && j > 0 ? P = et(v + j, a[0][1]) : !c && j < 0 && (P = Je(v + k, a[1][1])), b = Math.max(b, $), C = Math.max(C, P);
  }
  if (r) {
    if (u) {
      const $ = yn(F / M, x, S) * M;
      if (b = Math.max(b, $), s) {
        let P = 0;
        !l && !c || l && !c && f ? P = et(v + H + F / M, s[1][1]) * M : P = Je(v + H + (l ? D : -D) / M, s[0][1]) * M, b = Math.max(b, P);
      }
      if (a) {
        let P = 0;
        !l && !c || l && !c && f ? P = Je(v + F / M, a[1][1]) * M : P = et(v + (l ? D : -D) / M, a[0][1]) * M, b = Math.max(b, P);
      }
    }
    if (d) {
      const $ = yn(k * M, y, w) / M;
      if (C = Math.max(C, $), s) {
        let P = 0;
        !l && !c || c && !l && f ? P = et(p + k * M + z, s[1][0]) / M : P = Je(p + (c ? j : -j) * M + z, s[0][0]) / M, C = Math.max(C, P);
      }
      if (a) {
        let P = 0;
        !l && !c || c && !l && f ? P = Je(p + k * M, a[1][0]) / M : P = et(p + (c ? j : -j) * M, a[0][0]) / M, C = Math.max(C, P);
      }
    }
  }
  j = j + (j < 0 ? C : -C), D = D + (D < 0 ? b : -b), r && (f ? F > k * M ? j = (ci(l, c) ? -D : D) / M : D = (ci(l, c) ? -j : j) * M : u ? (j = D / M, c = l) : (D = j * M, l = c));
  const _ = l ? p + D : p, A = c ? v + j : v;
  return {
    width: N + (l ? -D : D),
    height: E + (c ? -j : j),
    x: i[0] * D * (l ? -1 : 1) + _,
    y: i[1] * j * (c ? -1 : 1) + A
  };
}
const Qs = { width: 0, height: 0, x: 0, y: 0 }, Qf = {
  ...Qs,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function Jf(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, i = e.measured.width ?? 0, s = e.measured.height ?? 0, a = n[0] * i, l = n[1] * s;
  return [
    [o - a, r - l],
    [o + i - a, r + s - l]
  ];
}
function eh({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const i = Me(e);
  let s = {
    controlDirection: ai("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function a({ controlPosition: c, boundaries: u, keepAspectRatio: d, resizeDirection: f, onResizeStart: m, onResize: g, onResizeEnd: y, shouldResize: w }) {
    let x = { ...Qs }, S = { ...Qf };
    s = {
      boundaries: u,
      resizeDirection: f,
      keepAspectRatio: d,
      controlDirection: ai(c)
    };
    let p, v = null, N = [], E, M, D, j = !1;
    const F = as().on("start", (k) => {
      const { nodeLookup: z, transform: H, snapGrid: b, snapToGrid: C, nodeOrigin: _, paneDomNode: A } = n();
      if (p = z.get(t), !p)
        return;
      v = A?.getBoundingClientRect() ?? null;
      const { xSnapped: $, ySnapped: P } = Ft(k.sourceEvent, {
        transform: H,
        snapGrid: b,
        snapToGrid: C,
        containerBounds: v
      });
      x = {
        width: p.measured.width ?? 0,
        height: p.measured.height ?? 0,
        x: p.position.x ?? 0,
        y: p.position.y ?? 0
      }, S = {
        ...x,
        pointerX: $,
        pointerY: P,
        aspectRatio: x.width / x.height
      }, E = void 0, M = pt(p.extent) ? p.extent : void 0, p.parentId && (p.extent === "parent" || p.expandParent) && (E = z.get(p.parentId)), E && p.extent === "parent" && (M = [
        [0, 0],
        [E.measured.width, E.measured.height]
      ]), N = [], D = void 0;
      for (const [B, V] of z)
        if (V.parentId === t && (N.push({
          id: B,
          position: { ...V.position },
          extent: V.extent
        }), V.extent === "parent" || V.expandParent)) {
          const O = Jf(V, p, V.origin ?? _);
          D ? D = [
            [Math.min(O[0][0], D[0][0]), Math.min(O[0][1], D[0][1])],
            [Math.max(O[1][0], D[1][0]), Math.max(O[1][1], D[1][1])]
          ] : D = O;
        }
      m?.(k, { ...x });
    }).on("drag", (k) => {
      const { transform: z, snapGrid: H, snapToGrid: b, nodeOrigin: C } = n(), _ = Ft(k.sourceEvent, {
        transform: z,
        snapGrid: H,
        snapToGrid: b,
        containerBounds: v
      }), A = [];
      if (!p)
        return;
      const { x: $, y: P, width: B, height: V } = x, O = {}, U = p.origin ?? C, { width: K, height: X, x: ie, y: G } = Gf(S, s.controlDirection, _, s.boundaries, s.keepAspectRatio, U, M, D), T = K !== B, Z = X !== V, re = ie !== $ && T, oe = G !== P && Z;
      if (!re && !oe && !T && !Z)
        return;
      if ((re || oe || U[0] === 1 || U[1] === 1) && (O.x = re ? ie : x.x, O.y = oe ? G : x.y, x.x = O.x, x.y = O.y, N.length > 0)) {
        const L = ie - $, W = G - P;
        for (const fe of N)
          fe.position = {
            x: fe.position.x - L + U[0] * (K - B),
            y: fe.position.y - W + U[1] * (X - V)
          }, A.push(fe);
      }
      if ((T || Z) && (O.width = T && (!s.resizeDirection || s.resizeDirection === "horizontal") ? K : x.width, O.height = Z && (!s.resizeDirection || s.resizeDirection === "vertical") ? X : x.height, x.width = O.width, x.height = O.height), E && p.expandParent) {
        const L = U[0] * (O.width ?? 0);
        O.x && O.x < L && (x.x = L, S.x = S.x - (O.x - L));
        const W = U[1] * (O.height ?? 0);
        O.y && O.y < W && (x.y = W, S.y = S.y - (O.y - W));
      }
      const Q = Kf({
        width: x.width,
        prevWidth: B,
        height: x.height,
        prevHeight: V,
        affectsX: s.controlDirection.affectsX,
        affectsY: s.controlDirection.affectsY
      }), te = { ...x, direction: Q };
      w?.(k, te) !== !1 && (j = !0, g?.(k, te), o(O, A));
    }).on("end", (k) => {
      j && (y?.(k, { ...x }), r?.({ ...x }), j = !1);
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
var _o = { exports: {} }, Co = {}, ko = { exports: {} }, Io = {};
var li;
function th() {
  if (li) return Io;
  li = 1;
  var e = ot;
  function t(d, f) {
    return d === f && (d !== 0 || 1 / d === 1 / f) || d !== d && f !== f;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, i = e.useLayoutEffect, s = e.useDebugValue;
  function a(d, f) {
    var m = f(), g = o({ inst: { value: m, getSnapshot: f } }), y = g[0].inst, w = g[1];
    return i(
      function() {
        y.value = m, y.getSnapshot = f, l(y) && w({ inst: y });
      },
      [d, m, f]
    ), r(
      function() {
        return l(y) && w({ inst: y }), d(function() {
          l(y) && w({ inst: y });
        });
      },
      [d]
    ), s(m), m;
  }
  function l(d) {
    var f = d.getSnapshot;
    d = d.value;
    try {
      var m = f();
      return !n(d, m);
    } catch {
      return !0;
    }
  }
  function c(d, f) {
    return f();
  }
  var u = typeof window > "u" || typeof window.document > "u" || typeof window.document.createElement > "u" ? c : a;
  return Io.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : u, Io;
}
var ui;
function nh() {
  return ui || (ui = 1, ko.exports = th()), ko.exports;
}
var di;
function oh() {
  if (di) return Co;
  di = 1;
  var e = ot, t = nh();
  function n(c, u) {
    return c === u && (c !== 0 || 1 / c === 1 / u) || c !== c && u !== u;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, i = e.useRef, s = e.useEffect, a = e.useMemo, l = e.useDebugValue;
  return Co.useSyncExternalStoreWithSelector = function(c, u, d, f, m) {
    var g = i(null);
    if (g.current === null) {
      var y = { hasValue: !1, value: null };
      g.current = y;
    } else y = g.current;
    g = a(
      function() {
        function x(E) {
          if (!S) {
            if (S = !0, p = E, E = f(E), m !== void 0 && y.hasValue) {
              var M = y.value;
              if (m(M, E))
                return v = M;
            }
            return v = E;
          }
          if (M = v, o(p, E)) return M;
          var D = f(E);
          return m !== void 0 && m(M, D) ? (p = E, M) : (p = E, v = D);
        }
        var S = !1, p, v, N = d === void 0 ? null : d;
        return [
          function() {
            return x(u());
          },
          N === null ? void 0 : function() {
            return x(N());
          }
        ];
      },
      [u, d, f, m]
    );
    var w = r(c, g[0], g[1]);
    return s(
      function() {
        y.hasValue = !0, y.value = w;
      },
      [w]
    ), l(w), w;
  }, Co;
}
var fi;
function rh() {
  return fi || (fi = 1, _o.exports = oh()), _o.exports;
}
var ih = rh();
const sh = /* @__PURE__ */ bc(ih), ah = {}, hi = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (u, d) => {
    const f = typeof u == "function" ? u(t) : u;
    if (!Object.is(f, t)) {
      const m = t;
      t = d ?? (typeof f != "object" || f === null) ? f : Object.assign({}, t, f), n.forEach((g) => g(t, m));
    }
  }, r = () => t, l = { setState: o, getState: r, getInitialState: () => c, subscribe: (u) => (n.add(u), () => n.delete(u)), destroy: () => {
    (ah ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, c = t = e(o, r, l);
  return l;
}, ch = (e) => e ? hi(e) : hi, { useDebugValue: lh } = ot, { useSyncExternalStoreWithSelector: uh } = sh, dh = (e) => e;
function Js(e, t = dh, n) {
  const o = uh(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return lh(o), o;
}
const pi = (e, t) => {
  const n = ch(e), o = (r, i = t) => Js(n, r, i);
  return Object.assign(o, n), o;
}, fh = (e, t) => e ? pi(e, t) : pi;
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
var Mo = { exports: {} }, be = {};
var gi;
function hh() {
  if (gi) return be;
  gi = 1;
  var e = ot;
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
  return be.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, be.createPortal = function(l, c) {
    var u = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!c || c.nodeType !== 1 && c.nodeType !== 9 && c.nodeType !== 11)
      throw Error(t(299));
    return i(l, c, null, u);
  }, be.flushSync = function(l) {
    var c = s.T, u = o.p;
    try {
      if (s.T = null, o.p = 2, l) return l();
    } finally {
      s.T = c, o.p = u, o.d.f();
    }
  }, be.preconnect = function(l, c) {
    typeof l == "string" && (c ? (c = c.crossOrigin, c = typeof c == "string" ? c === "use-credentials" ? c : "" : void 0) : c = null, o.d.C(l, c));
  }, be.prefetchDNS = function(l) {
    typeof l == "string" && o.d.D(l);
  }, be.preinit = function(l, c) {
    if (typeof l == "string" && c && typeof c.as == "string") {
      var u = c.as, d = a(u, c.crossOrigin), f = typeof c.integrity == "string" ? c.integrity : void 0, m = typeof c.fetchPriority == "string" ? c.fetchPriority : void 0;
      u === "style" ? o.d.S(
        l,
        typeof c.precedence == "string" ? c.precedence : void 0,
        {
          crossOrigin: d,
          integrity: f,
          fetchPriority: m
        }
      ) : u === "script" && o.d.X(l, {
        crossOrigin: d,
        integrity: f,
        fetchPriority: m,
        nonce: typeof c.nonce == "string" ? c.nonce : void 0
      });
    }
  }, be.preinitModule = function(l, c) {
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
  }, be.preload = function(l, c) {
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
  }, be.preloadModule = function(l, c) {
    if (typeof l == "string")
      if (c) {
        var u = a(c.as, c.crossOrigin);
        o.d.m(l, {
          as: typeof c.as == "string" && c.as !== "script" ? c.as : void 0,
          crossOrigin: u,
          integrity: typeof c.integrity == "string" ? c.integrity : void 0
        });
      } else o.d.m(l);
  }, be.requestFormReset = function(l) {
    o.d.r(l);
  }, be.unstable_batchedUpdates = function(l, c) {
    return l(c);
  }, be.useFormState = function(l, c, u) {
    return s.H.useFormState(l, c, u);
  }, be.useFormStatus = function() {
    return s.H.useHostTransitionStatus();
  }, be.version = "19.2.7", be;
}
var mi;
function ph() {
  if (mi) return Mo.exports;
  mi = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), Mo.exports = hh(), Mo.exports;
}
var gh = ph();
const Un = Go(null), mh = Un.Provider, ea = je.error001("react");
function le(e, t) {
  const n = en(Un);
  if (n === null)
    throw new Error(ea);
  return Js(n, e, t);
}
function ge() {
  const e = en(Un);
  if (e === null)
    throw new Error(ea);
  return ye(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const yi = { display: "none" }, yh = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, ta = "react-flow__node-desc", na = "react-flow__edge-desc", xh = "react-flow__aria-live", wh = (e) => e.ariaLiveMessage, vh = (e) => e.ariaLabelConfig;
function bh({ rfId: e }) {
  const t = le(wh);
  return h.jsx("div", { id: `${xh}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: yh, children: t });
}
function Sh({ rfId: e, disableKeyboardA11y: t }) {
  const n = le(vh);
  return h.jsxs(h.Fragment, { children: [h.jsx("div", { id: `${ta}-${e}`, style: yi, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), h.jsx("div", { id: `${na}-${e}`, style: yi, children: n["edge.a11yDescription.default"] }), !t && h.jsx(bh, { rfId: e })] });
}
const Kn = Vn(({ position: e = "top-left", children: t, className: n, style: o, ...r }, i) => {
  const s = `${e}`.split("-");
  return h.jsx("div", { className: we(["react-flow__panel", n, ...s]), style: o, ref: i, ...r, children: t });
});
Kn.displayName = "Panel";
function Eh({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : h.jsx(Kn, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: h.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const Nh = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, xn = (e) => e.id;
function _h(e, t) {
  return pe(e.selectedNodes.map(xn), t.selectedNodes.map(xn)) && pe(e.selectedEdges.map(xn), t.selectedEdges.map(xn));
}
function Ch({ onSelectionChange: e }) {
  const t = ge(), { selectedNodes: n, selectedEdges: o } = le(Nh, _h);
  return ce(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((i) => i(r));
  }, [n, o, e]), null;
}
const kh = (e) => !!e.onSelectionChangeHandlers;
function Ih({ onSelectionChange: e }) {
  const t = le(kh);
  return e || t ? h.jsx(Ch, { onSelectionChange: e }) : null;
}
const oa = [0, 0], Mh = { x: 0, y: 0, zoom: 1 }, Ah = [
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
], xi = [...Ah, "rfId"], Dh = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), wi = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: Zt,
  nodeOrigin: oa,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function jh(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: i, setNodeExtent: s, reset: a, setDefaultNodesAndEdges: l } = le(Dh, pe), c = ge();
  ce(() => (l(e.defaultNodes, e.defaultEdges), () => {
    u.current = wi, a();
  }), []);
  const u = ae(wi);
  return ce(
    () => {
      for (const d of xi) {
        const f = e[d], m = u.current[d];
        f !== m && (typeof e[d] > "u" || (d === "nodes" ? t(f) : d === "edges" ? n(f) : d === "minZoom" ? o(f) : d === "maxZoom" ? r(f) : d === "translateExtent" ? i(f) : d === "nodeExtent" ? s(f) : d === "ariaLabelConfig" ? c.setState({ ariaLabelConfig: hf(f) }) : d === "fitView" ? c.setState({ fitViewQueued: f }) : d === "fitViewOptions" ? c.setState({ fitViewOptions: f }) : c.setState({ [d]: f })));
      }
      u.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    xi.map((d) => e[d])
  ), null;
}
function vi() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function Ph(e) {
  const [t, n] = ne(e === "system" ? null : e);
  return ce(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = vi(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : vi()?.matches ? "dark" : "light";
}
const bi = typeof document < "u" ? document : null;
function Qt(e = null, t = { target: bi, actInsideInputWithModifier: !0 }) {
  const [n, o] = ne(!1), r = ae(!1), i = ae(/* @__PURE__ */ new Set([])), [s, a] = ye(() => {
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
  return ce(() => {
    const l = t?.target ?? bi, c = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const u = (m) => {
        if (r.current = m.ctrlKey || m.metaKey || m.shiftKey || m.altKey, (!r.current || r.current && !c) && Ts(m))
          return !1;
        const y = Ei(m.code, a);
        if (i.current.add(m[y]), Si(s, i.current, !1)) {
          const w = m.composedPath?.()?.[0] || m.target, x = w?.nodeName === "BUTTON" || w?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !x) && m.preventDefault(), o(!0);
        }
      }, d = (m) => {
        const g = Ei(m.code, a);
        Si(s, i.current, !0) ? (o(!1), i.current.clear()) : i.current.delete(m[g]), m.key === "Meta" && i.current.clear(), r.current = !1;
      }, f = () => {
        i.current.clear(), o(!1);
      };
      return l?.addEventListener("keydown", u), l?.addEventListener("keyup", d), window.addEventListener("blur", f), window.addEventListener("contextmenu", f), () => {
        l?.removeEventListener("keydown", u), l?.removeEventListener("keyup", d), window.removeEventListener("blur", f), window.removeEventListener("contextmenu", f);
      };
    }
  }, [e, o]), n;
}
function Si(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function Ei(e, t) {
  return t.includes(e) ? "code" : "key";
}
const $h = () => {
  const e = ge();
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
      const { width: o, height: r, minZoom: i, maxZoom: s, panZoom: a } = e.getState(), l = cr(t, o, r, i, s, n?.padding ?? 0.1);
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
      return jt(c, o, d, u);
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
      Th(l, a);
    n.push(a);
  }
  return r.length && r.forEach((i) => {
    i.index !== void 0 ? n.splice(i.index, 0, { ...i.item }) : n.push({ ...i.item });
  }), n;
}
function Th(e, t) {
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
function _i(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const aa = Ds();
function ca(e, t, n = {}) {
  return xf(e, t, {
    ...n,
    onError: n.onError ?? aa
  });
}
function zh(e, t, n, o = { shouldReplaceId: !0 }) {
  return wf(e, t, n, {
    ...o,
    onError: o.onError ?? aa
  });
}
const Ci = (e) => of(e), Rh = (e) => ks(e);
function la(e) {
  return Vn(e);
}
const Lh = typeof window < "u" ? vc : ce;
function ki(e) {
  const [t, n] = ne(BigInt(0)), [o] = ne(() => Hh(() => n((r) => r + BigInt(1))));
  return Lh(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function Hh(e) {
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
const ua = Go(null);
function Vh({ children: e }) {
  const t = ge(), n = he((a) => {
    const { nodes: l = [], setNodes: c, hasDefaultNodes: u, onNodesChange: d, nodeLookup: f, fitViewQueued: m, onNodesChangeMiddlewareMap: g } = t.getState();
    let y = l;
    for (const x of a)
      y = typeof x == "function" ? x(y) : x;
    let w = Ni({
      items: y,
      lookup: f
    });
    for (const x of g.values())
      w = x(w);
    u && c(y), w.length > 0 ? d?.(w) : m && window.requestAnimationFrame(() => {
      const { fitViewQueued: x, nodes: S, setNodes: p } = t.getState();
      x && p(S);
    });
  }, []), o = ki(n), r = he((a) => {
    const { edges: l = [], setEdges: c, hasDefaultEdges: u, onEdgesChange: d, edgeLookup: f } = t.getState();
    let m = l;
    for (const g of a)
      m = typeof g == "function" ? g(m) : g;
    u ? c(m) : d && d(Ni({
      items: m,
      lookup: f
    }));
  }, []), i = ki(r), s = ye(() => ({ nodeQueue: o, edgeQueue: i }), []);
  return h.jsx(ua.Provider, { value: s, children: e });
}
function Oh() {
  const e = en(ua);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Bh = (e) => !!e.panZoom;
function gr() {
  const e = $h(), t = ge(), n = Oh(), o = le(Bh), r = ye(() => {
    const i = (d) => t.getState().nodeLookup.get(d), s = (d) => {
      n.nodeQueue.push(d);
    }, a = (d) => {
      n.edgeQueue.push(d);
    }, l = (d) => {
      const { nodeLookup: f, nodeOrigin: m } = t.getState(), g = Ci(d) ? d : f.get(d.id), y = g.parentId ? Ps(g.position, g.measured, g.parentId, f, m) : g.position, w = {
        ...g,
        position: y,
        width: g.measured?.width ?? g.width,
        height: g.measured?.height ?? g.height
      };
      return _t(w);
    }, c = (d, f, m = { replace: !1 }) => {
      s((g) => g.map((y) => {
        if (y.id === d) {
          const w = typeof f == "function" ? f(y) : f;
          return m.replace && Ci(w) ? w : { ...y, ...w };
        }
        return y;
      }));
    }, u = (d, f, m = { replace: !1 }) => {
      a((g) => g.map((y) => {
        if (y.id === d) {
          const w = typeof f == "function" ? f(y) : f;
          return m.replace && Rh(w) ? w : { ...y, ...w };
        }
        return y;
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
        n.nodeQueue.push((m) => [...m, ...f]);
      },
      addEdges: (d) => {
        const f = Array.isArray(d) ? d : [d];
        n.edgeQueue.push((m) => [...m, ...f]);
      },
      toObject: () => {
        const { nodes: d = [], edges: f = [], transform: m } = t.getState(), [g, y, w] = m;
        return {
          nodes: d.map((x) => ({ ...x })),
          edges: f.map((x) => ({ ...x })),
          viewport: {
            x: g,
            y,
            zoom: w
          }
        };
      },
      deleteElements: async ({ nodes: d = [], edges: f = [] }) => {
        const { nodes: m, edges: g, onNodesDelete: y, onEdgesDelete: w, triggerNodeChanges: x, triggerEdgeChanges: S, onDelete: p, onBeforeDelete: v } = t.getState(), { nodes: N, edges: E } = await lf({
          nodesToRemove: d,
          edgesToRemove: f,
          nodes: m,
          edges: g,
          onBeforeDelete: v
        }), M = E.length > 0, D = N.length > 0;
        if (M) {
          const j = E.map(_i);
          w?.(E), S(j);
        }
        if (D) {
          const j = N.map(_i);
          y?.(N), x(j);
        }
        return (D || M) && p?.({ nodes: N, edges: E }), { deletedNodes: N, deletedEdges: E };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (d, f = !0, m) => {
        const g = Kr(d), y = g ? d : l(d), w = m !== void 0;
        return y ? (m || t.getState().nodes).filter((x) => {
          const S = t.getState().nodeLookup.get(x.id);
          if (S && !g && (x.id === d.id || !S.internals.positionAbsolute))
            return !1;
          const p = _t(w ? x : S), v = Kt(p, y);
          return f && v > 0 || v >= p.width * p.height || v >= y.width * y.height;
        }) : [];
      },
      isNodeIntersecting: (d, f, m = !0) => {
        const y = Kr(d) ? d : l(d);
        if (!y)
          return !1;
        const w = Kt(y, f);
        return m && w > 0 || w >= f.width * f.height || w >= y.width * y.height;
      },
      updateNode: c,
      updateNodeData: (d, f, m = { replace: !1 }) => {
        c(d, (g) => {
          const y = typeof f == "function" ? f(g) : f;
          return m.replace ? { ...g, data: y } : { ...g, data: { ...g.data, ...y } };
        }, m);
      },
      updateEdge: u,
      updateEdgeData: (d, f, m = { replace: !1 }) => {
        u(d, (g) => {
          const y = typeof f == "function" ? f(g) : f;
          return m.replace ? { ...g, data: y } : { ...g, data: { ...g.data, ...y } };
        }, m);
      },
      getNodesBounds: (d) => {
        const { nodeLookup: f, nodeOrigin: m } = t.getState();
        return rf(d, { nodeLookup: f, nodeOrigin: m });
      },
      getHandleConnections: ({ type: d, id: f, nodeId: m }) => Array.from(t.getState().connectionLookup.get(`${m}-${d}${f ? `-${f}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: d, handleId: f, nodeId: m }) => Array.from(t.getState().connectionLookup.get(`${m}${d ? f ? `-${d}-${f}` : `-${d}` : ""}`)?.values() ?? []),
      fitView: async (d) => {
        const f = t.getState().fitViewResolver ?? ff();
        return t.setState({ fitViewQueued: !0, fitViewOptions: d, fitViewResolver: f }), n.nodeQueue.push((m) => [...m]), f.promise;
      }
    };
  }, []);
  return ye(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const Ii = (e) => e.selected, Fh = typeof window < "u" ? window : void 0;
function Yh({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = ge(), { deleteElements: o } = gr(), r = Qt(e, { actInsideInputWithModifier: !1 }), i = Qt(t, { target: Fh });
  ce(() => {
    if (r) {
      const { edges: s, nodes: a } = n.getState();
      o({ nodes: a.filter(Ii), edges: s.filter(Ii) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), ce(() => {
    n.setState({ multiSelectionActive: i });
  }, [i]);
}
function Xh(e) {
  const t = ge();
  ce(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = lr(e.current);
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
const Gn = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, Wh = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function qh({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: i = ut.Free, zoomOnDoubleClick: s = !0, panOnDrag: a = !0, defaultViewport: l, translateExtent: c, minZoom: u, maxZoom: d, zoomActivationKeyCode: f, preventScrolling: m = !0, children: g, noWheelClassName: y, noPanClassName: w, onViewportChange: x, isControlledViewport: S, paneClickDistance: p, selectionOnDrag: v }) {
  const N = ge(), E = ae(null), { userSelectionActive: M, lib: D, connectionInProgress: j } = le(Wh, pe), F = Qt(f), k = ae();
  Xh(E);
  const z = he((H) => {
    x?.({ x: H[0], y: H[1], zoom: H[2] }), S || N.setState({ transform: H });
  }, [x, S]);
  return ce(() => {
    if (E.current) {
      k.current = Uf({
        domNode: E.current,
        minZoom: u,
        maxZoom: d,
        translateExtent: c,
        viewport: l,
        onDraggingChange: (_) => N.setState((A) => A.paneDragging === _ ? A : { paneDragging: _ }),
        onPanZoomStart: (_, A) => {
          const { onViewportChangeStart: $, onMoveStart: P } = N.getState();
          P?.(_, A), $?.(A);
        },
        onPanZoom: (_, A) => {
          const { onViewportChange: $, onMove: P } = N.getState();
          P?.(_, A), $?.(A);
        },
        onPanZoomEnd: (_, A) => {
          const { onViewportChangeEnd: $, onMoveEnd: P } = N.getState();
          P?.(_, A), $?.(A);
        }
      });
      const { x: H, y: b, zoom: C } = k.current.getViewport();
      return N.setState({
        panZoom: k.current,
        transform: [H, b, C],
        domNode: E.current.closest(".react-flow")
      }), () => {
        k.current?.destroy();
      };
    }
  }, []), ce(() => {
    k.current?.update({
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
      onTransformChange: z,
      connectionInProgress: j,
      selectionOnDrag: v,
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
    F,
    m,
    w,
    M,
    y,
    D,
    z,
    j,
    v,
    p
  ]), h.jsx("div", { className: "react-flow__renderer", ref: E, style: Gn, children: g });
}
const Zh = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function Uh() {
  const { userSelectionActive: e, userSelectionRect: t } = le(Zh, pe);
  return e && t ? h.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const Ao = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, Kh = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function Gh({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Ut.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: i, selectionOnDrag: s, onSelectionStart: a, onSelectionEnd: l, onPaneClick: c, onPaneContextMenu: u, onPaneScroll: d, onPaneMouseEnter: f, onPaneMouseMove: m, onPaneMouseLeave: g, children: y }) {
  const w = ae(0), x = ge(), { userSelectionActive: S, elementsSelectable: p, dragging: v, connectionInProgress: N, panBy: E, autoPanSpeed: M } = le(Kh, pe), D = p && (e || S), j = ae(null), F = ae(), k = ae(/* @__PURE__ */ new Set()), z = ae(/* @__PURE__ */ new Set()), H = ae(!1), b = ae({ x: 0, y: 0 }), C = ae(!1), _ = (T) => {
    if (H.current || N) {
      H.current = !1;
      return;
    }
    c?.(T), x.getState().resetSelectedElements(), x.setState({ nodesSelectionActive: !1 });
  }, A = (T) => {
    if (Array.isArray(o) && o?.includes(2)) {
      T.preventDefault();
      return;
    }
    u?.(T);
  }, $ = d ? (T) => d(T) : void 0, P = (T) => {
    H.current && (T.stopPropagation(), H.current = !1);
  }, B = (T) => {
    const { domNode: Z, transform: re } = x.getState();
    if (F.current = Z?.getBoundingClientRect(), !F.current)
      return;
    const oe = T.target === j.current;
    if (!oe && !!T.target.closest(".nokey") || !e || !(s && oe || t) || T.button !== 0 || !T.isPrimary)
      return;
    T.target?.setPointerCapture?.(T.pointerId), H.current = !1;
    const { x: se, y: L } = He(T.nativeEvent, F.current), W = jt({ x: se, y: L }, re);
    x.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: W.x,
        startY: W.y,
        x: se,
        y: L
      }
    }), oe || (T.stopPropagation(), T.preventDefault());
  };
  function V(T, Z) {
    const { userSelectionRect: re } = x.getState();
    if (!re)
      return;
    const { transform: oe, nodeLookup: Q, edgeLookup: te, connectionLookup: se, triggerNodeChanges: L, triggerEdgeChanges: W, defaultEdgeOptions: fe } = x.getState(), me = { x: re.startX, y: re.startY }, { x: _e, y: Se } = Ct(me, oe), Te = {
      startX: me.x,
      startY: me.y,
      x: T < _e ? T : _e,
      y: Z < Se ? Z : Se,
      width: Math.abs(T - _e),
      height: Math.abs(Z - Se)
    }, it = k.current, We = z.current;
    k.current = new Set(sr(Q, Te, oe, n === Ut.Partial, !0).map((Ce) => Ce.id)), z.current = /* @__PURE__ */ new Set();
    const qe = fe?.selectable ?? !0;
    for (const Ce of k.current) {
      const Pe = se.get(Ce);
      if (Pe)
        for (const { edgeId: $e } of Pe.values()) {
          const Ze = te.get($e);
          Ze && (Ze.selectable ?? qe) && z.current.add($e);
        }
    }
    if (!Gr(it, k.current)) {
      const Ce = xt(Q, k.current, !0);
      L(Ce);
    }
    if (!Gr(We, z.current)) {
      const Ce = xt(te, z.current);
      W(Ce);
    }
    x.setState({
      userSelectionRect: Te,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function O() {
    if (!r || !F.current)
      return;
    const [T, Z] = ar(b.current, F.current, M);
    E({ x: T, y: Z }).then((re) => {
      if (!H.current || !re) {
        w.current = requestAnimationFrame(O);
        return;
      }
      const { x: oe, y: Q } = b.current;
      V(oe, Q), w.current = requestAnimationFrame(O);
    });
  }
  const U = () => {
    cancelAnimationFrame(w.current), w.current = 0, C.current = !1;
  };
  ce(() => () => U(), []);
  const K = (T) => {
    const { userSelectionRect: Z, transform: re, resetSelectedElements: oe } = x.getState();
    if (!F.current || !Z)
      return;
    const { x: Q, y: te } = He(T.nativeEvent, F.current);
    b.current = { x: Q, y: te };
    const se = Ct({ x: Z.startX, y: Z.startY }, re);
    if (!H.current) {
      const L = t ? 0 : i;
      if (Math.hypot(Q - se.x, te - se.y) <= L)
        return;
      oe(), a?.(T);
    }
    H.current = !0, C.current || (O(), C.current = !0), V(Q, te);
  }, X = (T) => {
    T.button === 0 && (T.target?.releasePointerCapture?.(T.pointerId), !S && T.target === j.current && x.getState().userSelectionRect && _?.(T), x.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), H.current && (l?.(T), x.setState({
      nodesSelectionActive: k.current.size > 0
    })), U());
  }, ie = (T) => {
    T.target?.releasePointerCapture?.(T.pointerId), U();
  }, G = o === !0 || Array.isArray(o) && o.includes(0);
  return h.jsxs("div", { className: we(["react-flow__pane", { draggable: G, dragging: v, selection: e }]), onClick: D ? void 0 : Ao(_, j), onContextMenu: Ao(A, j), onWheel: Ao($, j), onPointerEnter: D ? void 0 : f, onPointerMove: D ? K : m, onPointerUp: D ? X : void 0, onPointerCancel: D ? ie : void 0, onPointerDownCapture: D ? B : void 0, onClickCapture: D ? P : void 0, onPointerLeave: g, ref: j, style: Gn, children: [y, h.jsx(Uh, {})] });
}
function qo({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: i, multiSelectionActive: s, nodeLookup: a, onError: l } = t.getState(), c = a.get(e);
  if (!c) {
    l?.("012", je.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), c.selected ? (n || c.selected && s) && (i({ nodes: [c], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function da({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: i, nodeClickDistance: s }) {
  const a = ge(), [l, c] = ne(!1), u = ae();
  return ce(() => {
    u.current = Tf({
      getStoreItems: () => a.getState(),
      onNodeMouseDown: (d) => {
        qo({
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
const Qh = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function fa() {
  const e = ge();
  return he((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: i, nodesDraggable: s, onError: a, updateNodePositions: l, nodeLookup: c, nodeOrigin: u } = e.getState(), d = /* @__PURE__ */ new Map(), f = Qh(s), m = r ? i[0] : 5, g = r ? i[1] : 5, y = n.direction.x * m * n.factor, w = n.direction.y * g * n.factor;
    for (const [, x] of c) {
      if (!f(x))
        continue;
      let S = {
        x: x.internals.positionAbsolute.x + y,
        y: x.internals.positionAbsolute.y + w
      };
      r && (S = sn(S, i));
      const { position: p, positionAbsolute: v } = Is({
        nodeId: x.id,
        nextPosition: S,
        nodeLookup: c,
        nodeExtent: o,
        nodeOrigin: u,
        onError: a
      });
      x.position = p, x.internals.positionAbsolute = v, d.set(x.id, x);
    }
    l(d);
  }, []);
}
const mr = Go(null), Jh = mr.Provider;
mr.Consumer;
const ha = () => en(mr), ep = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), tp = (e, t, n) => (o) => {
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
function np({ type: e = "source", position: t = J.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: i = !0, id: s, onConnect: a, children: l, className: c, onMouseDown: u, onTouchStart: d, ...f }, m) {
  const g = s || null, y = e === "target", w = ge(), x = ha(), { connectOnClick: S, noPanClassName: p, rfId: v } = le(ep, pe), { connectingFrom: N, connectingTo: E, clickConnecting: M, isPossibleEndHandle: D, connectionInProcess: j, clickConnectionInProcess: F, valid: k } = le(tp(x, g, e), pe);
  x || w.getState().onError?.("010", je.error010());
  const z = (C) => {
    const { defaultEdgeOptions: _, onConnect: A, hasDefaultEdges: $ } = w.getState(), P = {
      ..._,
      ...C
    };
    if ($) {
      const { edges: B, setEdges: V, onError: O } = w.getState();
      V(ca(P, B, { onError: O }));
    }
    A?.(P), a?.(P);
  }, H = (C) => {
    if (!x)
      return;
    const _ = zs(C.nativeEvent);
    if (r && (_ && C.button === 0 || !_)) {
      const A = w.getState();
      Wo.onPointerDown(C.nativeEvent, {
        handleDomNode: C.currentTarget,
        autoPanOnConnect: A.autoPanOnConnect,
        connectionMode: A.connectionMode,
        connectionRadius: A.connectionRadius,
        domNode: A.domNode,
        nodeLookup: A.nodeLookup,
        lib: A.lib,
        isTarget: y,
        handleId: g,
        nodeId: x,
        flowId: A.rfId,
        panBy: A.panBy,
        cancelConnection: A.cancelConnection,
        onConnectStart: A.onConnectStart,
        onConnectEnd: (...$) => w.getState().onConnectEnd?.(...$),
        updateConnection: A.updateConnection,
        onConnect: z,
        isValidConnection: n || ((...$) => w.getState().isValidConnection?.(...$) ?? !0),
        getTransform: () => w.getState().transform,
        getFromHandle: () => w.getState().connection.fromHandle,
        autoPanSpeed: A.autoPanSpeed,
        dragThreshold: A.connectionDragThreshold
      });
    }
    _ ? u?.(C) : d?.(C);
  }, b = (C) => {
    const { onClickConnectStart: _, onClickConnectEnd: A, connectionClickStartHandle: $, connectionMode: P, isValidConnection: B, lib: V, rfId: O, nodeLookup: U, connection: K } = w.getState();
    if (!x || !$ && !r)
      return;
    if (!$) {
      _?.(C.nativeEvent, { nodeId: x, handleId: g, handleType: e }), w.setState({ connectionClickStartHandle: { nodeId: x, type: e, id: g } });
      return;
    }
    const X = $s(C.target), ie = n || B, { connection: G, isValid: T } = Wo.isValid(C.nativeEvent, {
      handle: {
        nodeId: x,
        id: g,
        type: e
      },
      connectionMode: P,
      fromNodeId: $.nodeId,
      fromHandleId: $.id || null,
      fromType: $.type,
      isValidConnection: ie,
      flowId: O,
      doc: X,
      lib: V,
      nodeLookup: U
    });
    T && G && z(G);
    const Z = structuredClone(K);
    delete Z.inProgress, Z.toPosition = Z.toHandle ? Z.toHandle.position : null, A?.(C, Z), w.setState({ connectionClickStartHandle: null });
  };
  return h.jsx("div", { "data-handleid": g, "data-nodeid": x, "data-handlepos": t, "data-id": `${v}-${x}-${g}-${e}`, className: we([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    p,
    c,
    {
      source: !y,
      target: y,
      connectable: o,
      connectablestart: r,
      connectableend: i,
      clickconnecting: M,
      connectingfrom: N,
      connectingto: E,
      valid: k,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!j || D) && (j || F ? i : r)
    }
  ]), onMouseDown: H, onTouchStart: H, onClick: S ? b : void 0, ref: m, ...f, children: l });
}
const It = xe(la(np));
function op({ data: e, isConnectable: t, sourcePosition: n = J.Bottom }) {
  return h.jsxs(h.Fragment, { children: [e?.label, h.jsx(It, { type: "source", position: n, isConnectable: t })] });
}
function rp({ data: e, isConnectable: t, targetPosition: n = J.Top, sourcePosition: o = J.Bottom }) {
  return h.jsxs(h.Fragment, { children: [h.jsx(It, { type: "target", position: n, isConnectable: t }), e?.label, h.jsx(It, { type: "source", position: o, isConnectable: t })] });
}
function ip() {
  return null;
}
function sp({ data: e, isConnectable: t, targetPosition: n = J.Top }) {
  return h.jsxs(h.Fragment, { children: [h.jsx(It, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const Rn = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, Mi = {
  input: op,
  default: rp,
  output: sp,
  group: ip
};
function ap(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const cp = (e) => {
  const { width: t, height: n, x: o, y: r } = rn(e.nodeLookup, {
    filter: (i) => !!i.selected
  });
  return {
    width: Le(t) ? t : null,
    height: Le(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function lp({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = ge(), { width: r, height: i, transformString: s, userSelectionActive: a } = le(cp, pe), l = fa(), c = ae(null);
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
  const d = e ? (m) => {
    const g = o.getState().nodes.filter((y) => y.selected);
    e(m, g);
  } : void 0, f = (m) => {
    Object.prototype.hasOwnProperty.call(Rn, m.key) && (m.preventDefault(), l({
      direction: Rn[m.key],
      factor: m.shiftKey ? 4 : 1
    }));
  };
  return h.jsx("div", { className: we(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: s
  }, children: h.jsx("div", { ref: c, className: "react-flow__nodesselection-rect", onContextMenu: d, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : f, style: {
    width: r,
    height: i
  } }) });
}
const Ai = typeof window < "u" ? window : void 0, up = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function pa({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, paneClickDistance: a, deleteKeyCode: l, selectionKeyCode: c, selectionOnDrag: u, selectionMode: d, onSelectionStart: f, onSelectionEnd: m, multiSelectionKeyCode: g, panActivationKeyCode: y, zoomActivationKeyCode: w, elementsSelectable: x, zoomOnScroll: S, zoomOnPinch: p, panOnScroll: v, panOnScrollSpeed: N, panOnScrollMode: E, zoomOnDoubleClick: M, panOnDrag: D, autoPanOnSelection: j, defaultViewport: F, translateExtent: k, minZoom: z, maxZoom: H, preventScrolling: b, onSelectionContextMenu: C, noWheelClassName: _, noPanClassName: A, disableKeyboardA11y: $, onViewportChange: P, isControlledViewport: B }) {
  const { nodesSelectionActive: V, userSelectionActive: O } = le(up, pe), U = Qt(c, { target: Ai }), K = Qt(y, { target: Ai }), X = K || D, ie = K || v, G = u && X !== !0, T = U || O || G;
  return Yh({ deleteKeyCode: l, multiSelectionKeyCode: g }), h.jsx(qh, { onPaneContextMenu: i, elementsSelectable: x, zoomOnScroll: S, zoomOnPinch: p, panOnScroll: ie, panOnScrollSpeed: N, panOnScrollMode: E, zoomOnDoubleClick: M, panOnDrag: !U && X, defaultViewport: F, translateExtent: k, minZoom: z, maxZoom: H, zoomActivationKeyCode: w, preventScrolling: b, noWheelClassName: _, noPanClassName: A, onViewportChange: P, isControlledViewport: B, paneClickDistance: a, selectionOnDrag: G, children: h.jsxs(Gh, { onSelectionStart: f, onSelectionEnd: m, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, panOnDrag: X, autoPanOnSelection: j, isSelecting: !!T, selectionMode: d, selectionKeyPressed: U, paneClickDistance: a, selectionOnDrag: G, children: [e, V && h.jsx(lp, { onSelectionContextMenu: C, noPanClassName: A, disableKeyboardA11y: $ })] }) });
}
pa.displayName = "FlowRenderer";
const dp = xe(pa), fp = (e) => (t) => e ? sr(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function hp(e) {
  return le(he(fp(e), [e]), pe);
}
const pp = (e) => e.updateNodeInternals;
function gp() {
  const e = le(pp), [t] = ne(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
function mp({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = ge(), i = ae(null), s = ae(null), a = ae(e.sourcePosition), l = ae(e.targetPosition), c = ae(t), u = n && !!e.internals.handleBounds;
  return ce(() => {
    i.current && !e.hidden && (!u || s.current !== i.current) && (s.current && o?.unobserve(s.current), o?.observe(i.current), s.current = i.current);
  }, [u, e.hidden]), ce(() => () => {
    s.current && (o?.unobserve(s.current), s.current = null);
  }, []), ce(() => {
    if (i.current) {
      const d = c.current !== t, f = a.current !== e.sourcePosition, m = l.current !== e.targetPosition;
      (d || f || m) && (c.current = t, a.current = e.sourcePosition, l.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: i.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), i;
}
function yp({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: i, onDoubleClick: s, nodesDraggable: a, elementsSelectable: l, nodesConnectable: c, nodesFocusable: u, resizeObserver: d, noDragClassName: f, noPanClassName: m, disableKeyboardA11y: g, rfId: y, nodeTypes: w, nodeClickDistance: x, onError: S }) {
  const { node: p, internals: v, isParent: N } = le((T) => {
    const Z = T.nodeLookup.get(e), re = T.parentLookup.has(e);
    return {
      node: Z,
      internals: Z.internals,
      isParent: re
    };
  }, pe);
  let E = p.type || "default", M = w?.[E] || Mi[E];
  M === void 0 && (S?.("003", je.error003(E)), E = "default", M = w?.default || Mi.default);
  const D = !!(p.draggable || a && typeof p.draggable > "u"), j = !!(p.selectable || l && typeof p.selectable > "u"), F = !!(p.connectable || c && typeof p.connectable > "u"), k = !!(p.focusable || u && typeof p.focusable > "u"), z = ge(), H = js(p), b = mp({ node: p, nodeType: E, hasDimensions: H, resizeObserver: d }), C = da({
    nodeRef: b,
    disabled: p.hidden || !D,
    noDragClassName: f,
    handleSelector: p.dragHandle,
    nodeId: e,
    isSelectable: j,
    nodeClickDistance: x
  }), _ = fa();
  if (p.hidden)
    return null;
  const A = Qe(p), $ = ap(p), P = j || D || t || n || o || r, B = n ? (T) => n(T, { ...v.userNode }) : void 0, V = o ? (T) => o(T, { ...v.userNode }) : void 0, O = r ? (T) => r(T, { ...v.userNode }) : void 0, U = i ? (T) => i(T, { ...v.userNode }) : void 0, K = s ? (T) => s(T, { ...v.userNode }) : void 0, X = (T) => {
    const { selectNodesOnDrag: Z, nodeDragThreshold: re } = z.getState();
    j && (!Z || !D || re > 0) && qo({
      id: e,
      store: z,
      nodeRef: b
    }), t && t(T, { ...v.userNode });
  }, ie = (T) => {
    if (!(Ts(T.nativeEvent) || g)) {
      if (Es.includes(T.key) && j) {
        const Z = T.key === "Escape";
        qo({
          id: e,
          store: z,
          unselect: Z,
          nodeRef: b
        });
      } else if (D && p.selected && Object.prototype.hasOwnProperty.call(Rn, T.key)) {
        T.preventDefault();
        const { ariaLabelConfig: Z } = z.getState();
        z.setState({
          ariaLiveMessage: Z["node.a11yDescription.ariaLiveMessage"]({
            direction: T.key.replace("Arrow", "").toLowerCase(),
            x: ~~v.positionAbsolute.x,
            y: ~~v.positionAbsolute.y
          })
        }), _({
          direction: Rn[T.key],
          factor: T.shiftKey ? 4 : 1
        });
      }
    }
  }, G = () => {
    if (g || !b.current?.matches(":focus-visible"))
      return;
    const { transform: T, width: Z, height: re, autoPanOnNodeFocus: oe, setCenter: Q } = z.getState();
    if (!oe)
      return;
    sr(/* @__PURE__ */ new Map([[e, p]]), { x: 0, y: 0, width: Z, height: re }, T, !0).length > 0 || Q(p.position.x + A.width / 2, p.position.y + A.height / 2, {
      zoom: T[2]
    });
  };
  return h.jsx("div", { className: we([
    "react-flow__node",
    `react-flow__node-${E}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [m]: D
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
    zIndex: v.z,
    transform: `translate(${v.positionAbsolute.x}px,${v.positionAbsolute.y}px)`,
    pointerEvents: P ? "all" : "none",
    visibility: H ? "visible" : "hidden",
    ...p.style,
    ...$
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: B, onMouseMove: V, onMouseLeave: O, onContextMenu: U, onClick: X, onDoubleClick: K, onKeyDown: k ? ie : void 0, tabIndex: k ? 0 : void 0, onFocus: k ? G : void 0, role: p.ariaRole ?? (k ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": g ? void 0 : `${ta}-${y}`, "aria-label": p.ariaLabel, ...p.domAttributes, children: h.jsx(Jh, { value: e, children: h.jsx(M, { id: e, data: p.data, type: E, positionAbsoluteX: v.positionAbsolute.x, positionAbsoluteY: v.positionAbsolute.y, selected: p.selected ?? !1, selectable: j, draggable: D, deletable: p.deletable ?? !0, isConnectable: F, sourcePosition: p.sourcePosition, targetPosition: p.targetPosition, dragging: C, dragHandle: p.dragHandle, zIndex: v.z, parentId: p.parentId, ...A }) }) });
}
var xp = xe(yp);
const wp = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function ga(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: i } = le(wp, pe), s = hp(e.onlyRenderVisibleElements), a = gp();
  return h.jsx("div", { className: "react-flow__nodes", style: Gn, children: s.map((l) => (
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
    h.jsx(xp, { id: l, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: a, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: i }, l)
  )) });
}
ga.displayName = "NodeRenderer";
const vp = xe(ga);
function bp(e) {
  return le(he((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const i = n.nodeLookup.get(r.source), s = n.nodeLookup.get(r.target);
        i && s && mf({
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
const Sp = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return h.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, Ep = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return h.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Di = {
  [$n.Arrow]: Sp,
  [$n.ArrowClosed]: Ep
};
function Np(e) {
  const t = ge();
  return ye(() => Object.prototype.hasOwnProperty.call(Di, e) ? Di[e] : (t.getState().onError?.("009", je.error009(e)), null), [e]);
}
const _p = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: i = "strokeWidth", strokeWidth: s, orient: a = "auto-start-reverse" }) => {
  const l = Np(t);
  return l ? h.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: i, orient: a, refX: "0", refY: "0", children: h.jsx(l, { color: n, strokeWidth: s }) }) : null;
}, ma = ({ defaultColor: e, rfId: t }) => {
  const n = le((i) => i.edges), o = le((i) => i.defaultEdgeOptions), r = ye(() => Nf(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? h.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: h.jsx("defs", { children: r.map((i) => h.jsx(_p, { id: i.id, type: i.type, color: i.color, width: i.width, height: i.height, markerUnits: i.markerUnits, strokeWidth: i.strokeWidth, orient: i.orient }, i.id)) }) }) : null;
};
ma.displayName = "MarkerDefinitions";
var Cp = xe(ma);
function ya({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: i, labelBgPadding: s = [2, 4], labelBgBorderRadius: a = 2, children: l, className: c, ...u }) {
  const [d, f] = ne({ x: 1, y: 0, width: 0, height: 0 }), m = we(["react-flow__edge-textwrapper", c]), g = ae(null);
  return ce(() => {
    if (g.current) {
      const y = g.current.getBBox();
      f({
        x: y.x,
        y: y.y,
        width: y.width,
        height: y.height
      });
    }
  }, [n]), n ? h.jsxs("g", { transform: `translate(${e - d.width / 2} ${t - d.height / 2})`, className: m, visibility: d.width ? "visible" : "hidden", ...u, children: [r && h.jsx("rect", { width: d.width + 2 * s[0], x: -s[0], y: -s[1], height: d.height + 2 * s[1], className: "react-flow__edge-textbg", style: i, rx: a, ry: a }), h.jsx("text", { className: "react-flow__edge-text", y: d.height / 2, dy: "0.3em", ref: g, style: o, children: n }), l] }) : null;
}
ya.displayName = "EdgeText";
const kp = xe(ya);
function an({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: a, labelBgBorderRadius: l, interactionWidth: c = 20, ...u }) {
  return h.jsxs(h.Fragment, { children: [h.jsx("path", { ...u, d: e, fill: "none", className: we(["react-flow__edge-path", u.className]) }), c ? h.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: c, className: "react-flow__edge-interaction" }) : null, o && Le(t) && Le(n) ? h.jsx(kp, { x: t, y: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: a, labelBgBorderRadius: l }) : null] });
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
  }), [u, d, f, m] = Rs({
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
    m
  ];
}
function wa(e) {
  return xe(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: a, label: l, labelStyle: c, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: m, style: g, markerEnd: y, markerStart: w, interactionWidth: x }) => {
    const [S, p, v] = xa({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: a
    }), N = e.isInternal ? void 0 : t;
    return h.jsx(an, { id: N, path: S, labelX: p, labelY: v, label: l, labelStyle: c, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: m, style: g, markerEnd: y, markerStart: w, interactionWidth: x });
  });
}
const Ip = wa({ isInternal: !1 }), va = wa({ isInternal: !0 });
Ip.displayName = "SimpleBezierEdge";
va.displayName = "SimpleBezierEdgeInternal";
function ba(e) {
  return xe(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: u, labelBgBorderRadius: d, style: f, sourcePosition: m = J.Bottom, targetPosition: g = J.Top, markerEnd: y, markerStart: w, pathOptions: x, interactionWidth: S }) => {
    const [p, v, N] = zn({
      sourceX: n,
      sourceY: o,
      sourcePosition: m,
      targetX: r,
      targetY: i,
      targetPosition: g,
      borderRadius: x?.borderRadius,
      offset: x?.offset,
      stepPosition: x?.stepPosition
    }), E = e.isInternal ? void 0 : t;
    return h.jsx(an, { id: E, path: p, labelX: v, labelY: N, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: u, labelBgBorderRadius: d, style: f, markerEnd: y, markerStart: w, interactionWidth: S });
  });
}
const Sa = ba({ isInternal: !1 }), Ea = ba({ isInternal: !0 });
Sa.displayName = "SmoothStepEdge";
Ea.displayName = "SmoothStepEdgeInternal";
function Na(e) {
  return xe(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return h.jsx(Sa, { ...n, id: o, pathOptions: ye(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Mp = Na({ isInternal: !1 }), _a = Na({ isInternal: !0 });
Mp.displayName = "StepEdge";
_a.displayName = "StepEdgeInternal";
function Ca(e) {
  return xe(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: u, labelBgBorderRadius: d, style: f, markerEnd: m, markerStart: g, interactionWidth: y }) => {
    const [w, x, S] = Os({ sourceX: n, sourceY: o, targetX: r, targetY: i }), p = e.isInternal ? void 0 : t;
    return h.jsx(an, { id: p, path: w, labelX: x, labelY: S, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: u, labelBgBorderRadius: d, style: f, markerEnd: m, markerStart: g, interactionWidth: y });
  });
}
const Ap = Ca({ isInternal: !1 }), ka = Ca({ isInternal: !0 });
Ap.displayName = "StraightEdge";
ka.displayName = "StraightEdgeInternal";
function Ia(e) {
  return xe(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s = J.Bottom, targetPosition: a = J.Top, label: l, labelStyle: c, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: m, style: g, markerEnd: y, markerStart: w, pathOptions: x, interactionWidth: S }) => {
    const [p, v, N] = Ls({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: a,
      curvature: x?.curvature
    }), E = e.isInternal ? void 0 : t;
    return h.jsx(an, { id: E, path: p, labelX: v, labelY: N, label: l, labelStyle: c, labelShowBg: u, labelBgStyle: d, labelBgPadding: f, labelBgBorderRadius: m, style: g, markerEnd: y, markerStart: w, interactionWidth: S });
  });
}
const Dp = Ia({ isInternal: !1 }), Ma = Ia({ isInternal: !0 });
Dp.displayName = "BezierEdge";
Ma.displayName = "BezierEdgeInternal";
const Pi = {
  default: Ma,
  straight: ka,
  step: _a,
  smoothstep: Ea,
  simplebezier: va
}, $i = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, jp = (e, t, n) => n === J.Left ? e - t : n === J.Right ? e + t : e, Pp = (e, t, n) => n === J.Top ? e - t : n === J.Bottom ? e + t : e, Ti = "react-flow__edgeupdater";
function zi({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: i, onMouseOut: s, type: a }) {
  return h.jsx("circle", { onMouseDown: r, onMouseEnter: i, onMouseOut: s, className: we([Ti, `${Ti}-${a}`]), cx: jp(t, o, e), cy: Pp(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function $p({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: i, targetY: s, sourcePosition: a, targetPosition: l, onReconnect: c, onReconnectStart: u, onReconnectEnd: d, setReconnecting: f, setUpdateHover: m }) {
  const g = ge(), y = (v, N) => {
    if (v.button !== 0)
      return;
    const { autoPanOnConnect: E, domNode: M, connectionMode: D, connectionRadius: j, lib: F, onConnectStart: k, cancelConnection: z, nodeLookup: H, rfId: b, panBy: C, updateConnection: _ } = g.getState(), A = N.type === "target", $ = (V, O) => {
      f(!1), d?.(V, n, N.type, O);
    }, P = (V) => c?.(n, V), B = (V, O) => {
      f(!0), u?.(v, n, N.type), k?.(V, O);
    };
    Wo.onPointerDown(v.nativeEvent, {
      autoPanOnConnect: E,
      connectionMode: D,
      connectionRadius: j,
      domNode: M,
      handleId: N.id,
      nodeId: N.nodeId,
      nodeLookup: H,
      isTarget: A,
      edgeUpdaterType: N.type,
      lib: F,
      flowId: b,
      cancelConnection: z,
      panBy: C,
      isValidConnection: (...V) => g.getState().isValidConnection?.(...V) ?? !0,
      onConnect: P,
      onConnectStart: B,
      onConnectEnd: (...V) => g.getState().onConnectEnd?.(...V),
      onReconnectEnd: $,
      updateConnection: _,
      getTransform: () => g.getState().transform,
      getFromHandle: () => g.getState().connection.fromHandle,
      dragThreshold: g.getState().connectionDragThreshold,
      handleDomNode: v.currentTarget
    });
  }, w = (v) => y(v, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), x = (v) => y(v, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), S = () => m(!0), p = () => m(!1);
  return h.jsxs(h.Fragment, { children: [(e === !0 || e === "source") && h.jsx(zi, { position: a, centerX: o, centerY: r, radius: t, onMouseDown: w, onMouseEnter: S, onMouseOut: p, type: "source" }), (e === !0 || e === "target") && h.jsx(zi, { position: l, centerX: i, centerY: s, radius: t, onMouseDown: x, onMouseEnter: S, onMouseOut: p, type: "target" })] });
}
function Tp({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: i, onContextMenu: s, onMouseEnter: a, onMouseMove: l, onMouseLeave: c, reconnectRadius: u, onReconnect: d, onReconnectStart: f, onReconnectEnd: m, rfId: g, edgeTypes: y, noPanClassName: w, onError: x, disableKeyboardA11y: S }) {
  let p = le((Q) => Q.edgeLookup.get(e));
  const v = le((Q) => Q.defaultEdgeOptions);
  p = v ? { ...v, ...p } : p;
  let N = p.type || "default", E = y?.[N] || Pi[N];
  E === void 0 && (x?.("011", je.error011(N)), N = "default", E = y?.default || Pi.default);
  const M = !!(p.focusable || t && typeof p.focusable > "u"), D = typeof d < "u" && (p.reconnectable || n && typeof p.reconnectable > "u"), j = !!(p.selectable || o && typeof p.selectable > "u"), F = ae(null), [k, z] = ne(!1), [H, b] = ne(!1), C = ge(), { zIndex: _, sourceX: A, sourceY: $, targetX: P, targetY: B, sourcePosition: V, targetPosition: O } = le(he((Q) => {
    const te = Q.nodeLookup.get(p.source), se = Q.nodeLookup.get(p.target);
    if (!te || !se)
      return {
        zIndex: p.zIndex,
        ...$i
      };
    const L = Ef({
      id: e,
      sourceNode: te,
      targetNode: se,
      sourceHandle: p.sourceHandle || null,
      targetHandle: p.targetHandle || null,
      connectionMode: Q.connectionMode,
      onError: x
    });
    return {
      zIndex: gf({
        selected: p.selected,
        zIndex: p.zIndex,
        sourceNode: te,
        targetNode: se,
        elevateOnSelect: Q.elevateEdgesOnSelect,
        zIndexMode: Q.zIndexMode
      }),
      ...L || $i
    };
  }, [p.source, p.target, p.sourceHandle, p.targetHandle, p.selected, p.zIndex]), pe), U = ye(() => p.markerStart ? `url('#${Yo(p.markerStart, g)}')` : void 0, [p.markerStart, g]), K = ye(() => p.markerEnd ? `url('#${Yo(p.markerEnd, g)}')` : void 0, [p.markerEnd, g]);
  if (p.hidden || A === null || $ === null || P === null || B === null)
    return null;
  const X = (Q) => {
    const { addSelectedEdges: te, unselectNodesAndEdges: se, multiSelectionActive: L } = C.getState();
    j && (C.setState({ nodesSelectionActive: !1 }), p.selected && L ? (se({ nodes: [], edges: [p] }), F.current?.blur()) : te([e])), r && r(Q, p);
  }, ie = i ? (Q) => {
    i(Q, { ...p });
  } : void 0, G = s ? (Q) => {
    s(Q, { ...p });
  } : void 0, T = a ? (Q) => {
    a(Q, { ...p });
  } : void 0, Z = l ? (Q) => {
    l(Q, { ...p });
  } : void 0, re = c ? (Q) => {
    c(Q, { ...p });
  } : void 0, oe = (Q) => {
    if (!S && Es.includes(Q.key) && j) {
      const { unselectNodesAndEdges: te, addSelectedEdges: se } = C.getState();
      Q.key === "Escape" ? (F.current?.blur(), te({ edges: [p] })) : se([e]);
    }
  };
  return h.jsx("svg", { style: { zIndex: _ }, children: h.jsxs("g", { className: we([
    "react-flow__edge",
    `react-flow__edge-${N}`,
    p.className,
    w,
    {
      selected: p.selected,
      animated: p.animated,
      inactive: !j && !r,
      updating: k,
      selectable: j
    }
  ]), onClick: X, onDoubleClick: ie, onContextMenu: G, onMouseEnter: T, onMouseMove: Z, onMouseLeave: re, onKeyDown: M ? oe : void 0, tabIndex: M ? 0 : void 0, role: p.ariaRole ?? (M ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": p.ariaLabel === null ? void 0 : p.ariaLabel || `Edge from ${p.source} to ${p.target}`, "aria-describedby": M ? `${na}-${g}` : void 0, ref: F, ...p.domAttributes, children: [!H && h.jsx(E, { id: e, source: p.source, target: p.target, type: p.type, selected: p.selected, animated: p.animated, selectable: j, deletable: p.deletable ?? !0, label: p.label, labelStyle: p.labelStyle, labelShowBg: p.labelShowBg, labelBgStyle: p.labelBgStyle, labelBgPadding: p.labelBgPadding, labelBgBorderRadius: p.labelBgBorderRadius, sourceX: A, sourceY: $, targetX: P, targetY: B, sourcePosition: V, targetPosition: O, data: p.data, style: p.style, sourceHandleId: p.sourceHandle, targetHandleId: p.targetHandle, markerStart: U, markerEnd: K, pathOptions: "pathOptions" in p ? p.pathOptions : void 0, interactionWidth: p.interactionWidth }), D && h.jsx($p, { edge: p, isReconnectable: D, reconnectRadius: u, onReconnect: d, onReconnectStart: f, onReconnectEnd: m, sourceX: A, sourceY: $, targetX: P, targetY: B, sourcePosition: V, targetPosition: O, setUpdateHover: z, setReconnecting: b })] }) });
}
var zp = xe(Tp);
const Rp = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function Aa({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: i, onEdgeContextMenu: s, onEdgeMouseEnter: a, onEdgeMouseMove: l, onEdgeMouseLeave: c, onEdgeClick: u, reconnectRadius: d, onEdgeDoubleClick: f, onReconnectStart: m, onReconnectEnd: g, disableKeyboardA11y: y }) {
  const { edgesFocusable: w, edgesReconnectable: x, elementsSelectable: S, onError: p } = le(Rp, pe), v = bp(t);
  return h.jsxs("div", { className: "react-flow__edges", children: [h.jsx(Cp, { defaultColor: e, rfId: n }), v.map((N) => h.jsx(zp, { id: N, edgesFocusable: w, edgesReconnectable: x, elementsSelectable: S, noPanClassName: r, onReconnect: i, onContextMenu: s, onMouseEnter: a, onMouseMove: l, onMouseLeave: c, onClick: u, reconnectRadius: d, onDoubleClick: f, onReconnectStart: m, onReconnectEnd: g, rfId: n, onError: p, edgeTypes: o, disableKeyboardA11y: y }, N))] });
}
Aa.displayName = "EdgeRenderer";
const Lp = xe(Aa), Hp = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function Vp({ children: e }) {
  const t = le(Hp);
  return h.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function Op(e) {
  const t = gr(), n = ae(!1);
  ce(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Bp = (e) => e.panZoom?.syncViewport;
function Fp(e) {
  const t = le(Bp), n = ge();
  return ce(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function Yp(e) {
  return e.connection.inProgress ? { ...e.connection, to: jt(e.connection.to, e.transform) } : { ...e.connection };
}
function Xp(e) {
  return Yp;
}
function Wp(e) {
  const t = Xp();
  return le(t, pe);
}
const qp = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function Zp({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: i, height: s, isValid: a, inProgress: l } = le(qp, pe);
  return !(i && r && l) ? null : h.jsx("svg", { style: e, width: i, height: s, className: "react-flow__connectionline react-flow__container", children: h.jsx("g", { className: we(["react-flow__connection", Cs(a)]), children: h.jsx(Da, { style: t, type: n, CustomComponent: o, isValid: a }) }) });
}
const Da = ({ style: e, type: t = tt.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: i, fromNode: s, fromHandle: a, fromPosition: l, to: c, toNode: u, toHandle: d, toPosition: f, pointer: m } = Wp();
  if (!r)
    return;
  if (n)
    return h.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: s, fromHandle: a, fromX: i.x, fromY: i.y, toX: c.x, toY: c.y, fromPosition: l, toPosition: f, connectionStatus: Cs(o), toNode: u, toHandle: d, pointer: m });
  let g = "";
  const y = {
    sourceX: i.x,
    sourceY: i.y,
    sourcePosition: l,
    targetX: c.x,
    targetY: c.y,
    targetPosition: f
  };
  switch (t) {
    case tt.Bezier:
      [g] = Ls(y);
      break;
    case tt.SimpleBezier:
      [g] = xa(y);
      break;
    case tt.Step:
      [g] = zn({
        ...y,
        borderRadius: 0
      });
      break;
    case tt.SmoothStep:
      [g] = zn(y);
      break;
    default:
      [g] = Os(y);
  }
  return h.jsx("path", { d: g, fill: "none", className: "react-flow__connection-path", style: e });
};
Da.displayName = "ConnectionLine";
const Up = {};
function Ri(e = Up) {
  ae(e), ge(), ce(() => {
  }, [e]);
}
function Kp() {
  ge(), ae(!1), ce(() => {
  }, []);
}
function ja({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: i, onEdgeDoubleClick: s, onNodeMouseEnter: a, onNodeMouseMove: l, onNodeMouseLeave: c, onNodeContextMenu: u, onSelectionContextMenu: d, onSelectionStart: f, onSelectionEnd: m, connectionLineType: g, connectionLineStyle: y, connectionLineComponent: w, connectionLineContainerStyle: x, selectionKeyCode: S, selectionOnDrag: p, selectionMode: v, multiSelectionKeyCode: N, panActivationKeyCode: E, zoomActivationKeyCode: M, deleteKeyCode: D, onlyRenderVisibleElements: j, elementsSelectable: F, defaultViewport: k, translateExtent: z, minZoom: H, maxZoom: b, preventScrolling: C, defaultMarkerColor: _, zoomOnScroll: A, zoomOnPinch: $, panOnScroll: P, panOnScrollSpeed: B, panOnScrollMode: V, zoomOnDoubleClick: O, panOnDrag: U, autoPanOnSelection: K, onPaneClick: X, onPaneMouseEnter: ie, onPaneMouseMove: G, onPaneMouseLeave: T, onPaneScroll: Z, onPaneContextMenu: re, paneClickDistance: oe, nodeClickDistance: Q, onEdgeContextMenu: te, onEdgeMouseEnter: se, onEdgeMouseMove: L, onEdgeMouseLeave: W, reconnectRadius: fe, onReconnect: me, onReconnectStart: _e, onReconnectEnd: Se, noDragClassName: Te, noWheelClassName: it, noPanClassName: We, disableKeyboardA11y: qe, nodeExtent: Ce, rfId: Pe, viewport: $e, onViewportChange: Ze }) {
  return Ri(e), Ri(t), Kp(), Op(n), Fp($e), h.jsx(dp, { onPaneClick: X, onPaneMouseEnter: ie, onPaneMouseMove: G, onPaneMouseLeave: T, onPaneContextMenu: re, onPaneScroll: Z, paneClickDistance: oe, deleteKeyCode: D, selectionKeyCode: S, selectionOnDrag: p, selectionMode: v, onSelectionStart: f, onSelectionEnd: m, multiSelectionKeyCode: N, panActivationKeyCode: E, zoomActivationKeyCode: M, elementsSelectable: F, zoomOnScroll: A, zoomOnPinch: $, zoomOnDoubleClick: O, panOnScroll: P, panOnScrollSpeed: B, panOnScrollMode: V, panOnDrag: U, autoPanOnSelection: K, defaultViewport: k, translateExtent: z, minZoom: H, maxZoom: b, onSelectionContextMenu: d, preventScrolling: C, noDragClassName: Te, noWheelClassName: it, noPanClassName: We, disableKeyboardA11y: qe, onViewportChange: Ze, isControlledViewport: !!$e, children: h.jsxs(Vp, { children: [h.jsx(Lp, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: s, onReconnect: me, onReconnectStart: _e, onReconnectEnd: Se, onlyRenderVisibleElements: j, onEdgeContextMenu: te, onEdgeMouseEnter: se, onEdgeMouseMove: L, onEdgeMouseLeave: W, reconnectRadius: fe, defaultMarkerColor: _, noPanClassName: We, disableKeyboardA11y: qe, rfId: Pe }), h.jsx(Zp, { style: y, type: g, component: w, containerStyle: x }), h.jsx("div", { className: "react-flow__edgelabel-renderer" }), h.jsx(vp, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: i, onNodeMouseEnter: a, onNodeMouseMove: l, onNodeMouseLeave: c, onNodeContextMenu: u, nodeClickDistance: Q, onlyRenderVisibleElements: j, noPanClassName: We, noDragClassName: Te, disableKeyboardA11y: qe, nodeExtent: Ce, rfId: Pe }), h.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
ja.displayName = "GraphView";
const Gp = xe(ja), Qp = Ds(), Li = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: a, minZoom: l = 0.5, maxZoom: c = 2, nodeOrigin: u, nodeExtent: d, zIndexMode: f = "basic" } = {}) => {
  const m = /* @__PURE__ */ new Map(), g = /* @__PURE__ */ new Map(), y = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), x = o ?? t ?? [], S = n ?? e ?? [], p = u ?? [0, 0], v = d ?? Zt;
  Ys(y, w, x);
  const { nodesInitialized: N } = Xo(S, m, g, {
    nodeOrigin: p,
    nodeExtent: v,
    zIndexMode: f
  });
  let E = [0, 0, 1];
  if (s && r && i) {
    const M = rn(m, {
      filter: (k) => !!((k.width || k.initialWidth) && (k.height || k.initialHeight))
    }), { x: D, y: j, zoom: F } = cr(M, r, i, l, c, a?.padding ?? 0.1);
    E = [D, j, F];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: i ?? 0,
    transform: E,
    nodes: S,
    nodesInitialized: N,
    nodeLookup: m,
    parentLookup: g,
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
    translateExtent: Zt,
    nodeExtent: v,
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
    connection: { ..._s },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: Qp,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Ns,
    zIndexMode: f,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, Jp = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: a, minZoom: l, maxZoom: c, nodeOrigin: u, nodeExtent: d, zIndexMode: f }) => fh((m, g) => {
  async function y() {
    const { nodeLookup: w, panZoom: x, fitViewOptions: S, fitViewResolver: p, width: v, height: N, minZoom: E, maxZoom: M } = g();
    x && (await cf({
      nodes: w,
      width: v,
      height: N,
      panZoom: x,
      minZoom: E,
      maxZoom: M
    }, S), p?.resolve(!0), m({ fitViewResolver: null }));
  }
  return {
    ...Li({
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
      const { nodeLookup: x, parentLookup: S, nodeOrigin: p, elevateNodesOnSelect: v, fitViewQueued: N, zIndexMode: E, nodesSelectionActive: M } = g(), { nodesInitialized: D, hasSelectedNodes: j } = Xo(w, x, S, {
        nodeOrigin: p,
        nodeExtent: d,
        elevateNodesOnSelect: v,
        checkEquality: !0,
        zIndexMode: E
      }), F = M && j;
      N && D ? (y(), m({
        nodes: w,
        nodesInitialized: D,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: F
      })) : m({ nodes: w, nodesInitialized: D, nodesSelectionActive: F });
    },
    setEdges: (w) => {
      const { connectionLookup: x, edgeLookup: S } = g();
      Ys(x, S, w), m({ edges: w });
    },
    setDefaultNodesAndEdges: (w, x) => {
      if (w) {
        const { setNodes: S } = g();
        S(w), m({ hasDefaultNodes: !0 });
      }
      if (x) {
        const { setEdges: S } = g();
        S(x), m({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (w) => {
      const { triggerNodeChanges: x, nodeLookup: S, parentLookup: p, domNode: v, nodeOrigin: N, nodeExtent: E, debug: M, fitViewQueued: D, zIndexMode: j } = g(), { changes: F, updatedInternals: k } = Df(w, S, p, v, N, E, j);
      k && (kf(S, p, { nodeOrigin: N, nodeExtent: E, zIndexMode: j }), D ? (y(), m({ fitViewQueued: !1, fitViewOptions: void 0 })) : m({}), F?.length > 0 && (M && console.log("React Flow: trigger node changes", F), x?.(F)));
    },
    updateNodePositions: (w, x = !1) => {
      const S = [];
      let p = [];
      const { nodeLookup: v, triggerNodeChanges: N, connection: E, updateConnection: M, onNodesChangeMiddlewareMap: D } = g();
      for (const [j, F] of w) {
        const k = v.get(j), z = !!(k?.expandParent && k?.parentId && F?.position), H = {
          id: j,
          type: "position",
          position: z ? {
            x: Math.max(0, F.position.x),
            y: Math.max(0, F.position.y)
          } : F.position,
          dragging: x
        };
        if (k && E.inProgress && E.fromNode.id === k.id) {
          const b = gt(k, E.fromHandle, J.Left, !0);
          M({ ...E, from: b });
        }
        z && k.parentId && S.push({
          id: j,
          parentId: k.parentId,
          rect: {
            ...F.internals.positionAbsolute,
            width: F.measured.width ?? 0,
            height: F.measured.height ?? 0
          }
        }), p.push(H);
      }
      if (S.length > 0) {
        const { parentLookup: j, nodeOrigin: F } = g(), k = pr(S, v, j, F);
        p.push(...k);
      }
      for (const j of D.values())
        p = j(p);
      N(p);
    },
    triggerNodeChanges: (w) => {
      const { onNodesChange: x, setNodes: S, nodes: p, hasDefaultNodes: v, debug: N } = g();
      if (w?.length) {
        if (v) {
          const E = ia(w, p);
          S(E);
        }
        N && console.log("React Flow: trigger node changes", w), x?.(w);
      }
    },
    triggerEdgeChanges: (w) => {
      const { onEdgesChange: x, setEdges: S, edges: p, hasDefaultEdges: v, debug: N } = g();
      if (w?.length) {
        if (v) {
          const E = sa(w, p);
          S(E);
        }
        N && console.log("React Flow: trigger edge changes", w), x?.(w);
      }
    },
    addSelectedNodes: (w) => {
      const { multiSelectionActive: x, edgeLookup: S, nodeLookup: p, triggerNodeChanges: v, triggerEdgeChanges: N } = g();
      if (x) {
        const E = w.map((M) => at(M, !0));
        v(E);
        return;
      }
      v(xt(p, /* @__PURE__ */ new Set([...w]), !0)), N(xt(S));
    },
    addSelectedEdges: (w) => {
      const { multiSelectionActive: x, edgeLookup: S, nodeLookup: p, triggerNodeChanges: v, triggerEdgeChanges: N } = g();
      if (x) {
        const E = w.map((M) => at(M, !0));
        N(E);
        return;
      }
      N(xt(S, /* @__PURE__ */ new Set([...w]))), v(xt(p, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: w, edges: x } = {}) => {
      const { edges: S, nodes: p, nodeLookup: v, triggerNodeChanges: N, triggerEdgeChanges: E } = g(), M = w || p, D = x || S, j = [];
      for (const k of M) {
        if (!k.selected)
          continue;
        const z = v.get(k.id);
        z && (z.selected = !1), j.push(at(k.id, !1));
      }
      const F = [];
      for (const k of D)
        k.selected && F.push(at(k.id, !1));
      N(j), E(F);
    },
    setMinZoom: (w) => {
      const { panZoom: x, maxZoom: S } = g();
      x?.setScaleExtent([w, S]), m({ minZoom: w });
    },
    setMaxZoom: (w) => {
      const { panZoom: x, minZoom: S } = g();
      x?.setScaleExtent([S, w]), m({ maxZoom: w });
    },
    setTranslateExtent: (w) => {
      g().panZoom?.setTranslateExtent(w), m({ translateExtent: w });
    },
    resetSelectedElements: () => {
      const { edges: w, nodes: x, triggerNodeChanges: S, triggerEdgeChanges: p, elementsSelectable: v } = g();
      if (!v)
        return;
      const N = x.reduce((M, D) => D.selected ? [...M, at(D.id, !1)] : M, []), E = w.reduce((M, D) => D.selected ? [...M, at(D.id, !1)] : M, []);
      S(N), p(E);
    },
    setNodeExtent: (w) => {
      const { nodes: x, nodeLookup: S, parentLookup: p, nodeOrigin: v, elevateNodesOnSelect: N, nodeExtent: E, zIndexMode: M } = g();
      w[0][0] === E[0][0] && w[0][1] === E[0][1] && w[1][0] === E[1][0] && w[1][1] === E[1][1] || (Xo(x, S, p, {
        nodeOrigin: v,
        nodeExtent: w,
        elevateNodesOnSelect: N,
        checkEquality: !1,
        zIndexMode: M
      }), m({ nodeExtent: w }));
    },
    panBy: (w) => {
      const { transform: x, width: S, height: p, panZoom: v, translateExtent: N } = g();
      return jf({ delta: w, panZoom: v, transform: x, translateExtent: N, width: S, height: p });
    },
    setCenter: async (w, x, S) => {
      const { width: p, height: v, maxZoom: N, panZoom: E } = g();
      if (!E)
        return !1;
      const M = typeof S?.zoom < "u" ? S.zoom : N;
      return await E.setViewport({
        x: p / 2 - w * M,
        y: v / 2 - x * M,
        zoom: M
      }, { duration: S?.duration, ease: S?.ease, interpolate: S?.interpolate }), !0;
    },
    cancelConnection: () => {
      m({
        connection: { ..._s }
      });
    },
    updateConnection: (w) => {
      m({ connection: w });
    },
    reset: () => m({ ...Li() })
  };
}, Object.is);
function eg({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: i, initialMinZoom: s, initialMaxZoom: a, initialFitViewOptions: l, fitView: c, nodeOrigin: u, nodeExtent: d, zIndexMode: f, children: m }) {
  const [g] = ne(() => Jp({
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
  return h.jsx(mh, { value: g, children: h.jsx(Vh, { children: m }) });
}
function tg({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: i, height: s, fitView: a, fitViewOptions: l, minZoom: c, maxZoom: u, nodeOrigin: d, nodeExtent: f, zIndexMode: m }) {
  return en(Un) ? h.jsx(h.Fragment, { children: e }) : h.jsx(eg, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: i, initialHeight: s, fitView: a, initialFitViewOptions: l, initialMinZoom: c, initialMaxZoom: u, nodeOrigin: d, nodeExtent: f, zIndexMode: m, children: e });
}
const ng = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function og({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: i, edgeTypes: s, onNodeClick: a, onEdgeClick: l, onInit: c, onMove: u, onMoveStart: d, onMoveEnd: f, onConnect: m, onConnectStart: g, onConnectEnd: y, onClickConnectStart: w, onClickConnectEnd: x, onNodeMouseEnter: S, onNodeMouseMove: p, onNodeMouseLeave: v, onNodeContextMenu: N, onNodeDoubleClick: E, onNodeDragStart: M, onNodeDrag: D, onNodeDragStop: j, onNodesDelete: F, onEdgesDelete: k, onDelete: z, onSelectionChange: H, onSelectionDragStart: b, onSelectionDrag: C, onSelectionDragStop: _, onSelectionContextMenu: A, onSelectionStart: $, onSelectionEnd: P, onBeforeDelete: B, connectionMode: V, connectionLineType: O = tt.Bezier, connectionLineStyle: U, connectionLineComponent: K, connectionLineContainerStyle: X, deleteKeyCode: ie = "Backspace", selectionKeyCode: G = "Shift", selectionOnDrag: T = !1, selectionMode: Z = Ut.Full, panActivationKeyCode: re = "Space", multiSelectionKeyCode: oe = Gt() ? "Meta" : "Control", zoomActivationKeyCode: Q = Gt() ? "Meta" : "Control", snapToGrid: te, snapGrid: se, onlyRenderVisibleElements: L = !1, selectNodesOnDrag: W, nodesDraggable: fe, autoPanOnNodeFocus: me, nodesConnectable: _e, nodesFocusable: Se, nodeOrigin: Te = oa, edgesFocusable: it, edgesReconnectable: We, elementsSelectable: qe = !0, defaultViewport: Ce = Mh, minZoom: Pe = 0.5, maxZoom: $e = 2, translateExtent: Ze = Zt, preventScrolling: Pt = !0, nodeExtent: $t, defaultMarkerColor: eo = "#b1b1b7", zoomOnScroll: to = !0, zoomOnPinch: no = !0, panOnScroll: oo = !1, panOnScrollSpeed: Tt = 0.5, panOnScrollMode: ro = ut.Free, zoomOnDoubleClick: io = !0, panOnDrag: so = !0, onPaneClick: ao, onPaneMouseEnter: co, onPaneMouseMove: lo, onPaneMouseLeave: uo, onPaneScroll: ln, onPaneContextMenu: un, paneClickDistance: fo = 1, nodeClickDistance: ho = 0, children: po, onReconnect: go, onReconnectStart: I, onReconnectEnd: R, onEdgeContextMenu: Y, onEdgeDoubleClick: q, onEdgeMouseEnter: ee, onEdgeMouseMove: de, onEdgeMouseLeave: ue, reconnectRadius: ve = 10, onNodesChange: Ie, onEdgesChange: st, noDragClassName: Ee = "nodrag", noWheelClassName: Wa = "nowheel", noPanClassName: br = "nopan", fitView: Sr, fitViewOptions: Er, connectOnClick: qa, attributionPosition: Za, proOptions: Ua, defaultEdgeOptions: Ka, elevateNodesOnSelect: Ga = !0, elevateEdgesOnSelect: Qa = !1, disableKeyboardA11y: Nr = !1, autoPanOnConnect: Ja, autoPanOnNodeDrag: ec, autoPanOnSelection: tc = !0, autoPanSpeed: nc, connectionRadius: oc, isValidConnection: rc, onError: ic, style: sc, id: _r, nodeDragThreshold: ac, connectionDragThreshold: cc, viewport: lc, onViewportChange: uc, width: dc, height: fc, colorMode: hc = "light", debug: pc, onScroll: Cr, ariaLabelConfig: gc, zIndexMode: kr = "basic", ...mc }, yc) {
  const mo = _r || "1", xc = Ph(hc), wc = he((Ir) => {
    Ir.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), Cr?.(Ir);
  }, [Cr]);
  return h.jsx("div", { "data-testid": "rf__wrapper", ...mc, onScroll: wc, style: { ...sc, ...ng }, ref: yc, className: we(["react-flow", r, xc]), id: _r, role: "application", children: h.jsxs(tg, { nodes: e, edges: t, width: dc, height: fc, fitView: Sr, fitViewOptions: Er, minZoom: Pe, maxZoom: $e, nodeOrigin: Te, nodeExtent: $t, zIndexMode: kr, children: [h.jsx(jh, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: m, onConnectStart: g, onConnectEnd: y, onClickConnectStart: w, onClickConnectEnd: x, nodesDraggable: fe, autoPanOnNodeFocus: me, nodesConnectable: _e, nodesFocusable: Se, edgesFocusable: it, edgesReconnectable: We, elementsSelectable: qe, elevateNodesOnSelect: Ga, elevateEdgesOnSelect: Qa, minZoom: Pe, maxZoom: $e, nodeExtent: $t, onNodesChange: Ie, onEdgesChange: st, snapToGrid: te, snapGrid: se, connectionMode: V, translateExtent: Ze, connectOnClick: qa, defaultEdgeOptions: Ka, fitView: Sr, fitViewOptions: Er, onNodesDelete: F, onEdgesDelete: k, onDelete: z, onNodeDragStart: M, onNodeDrag: D, onNodeDragStop: j, onSelectionDrag: C, onSelectionDragStart: b, onSelectionDragStop: _, onMove: u, onMoveStart: d, onMoveEnd: f, noPanClassName: br, nodeOrigin: Te, rfId: mo, autoPanOnConnect: Ja, autoPanOnNodeDrag: ec, autoPanSpeed: nc, onError: ic, connectionRadius: oc, isValidConnection: rc, selectNodesOnDrag: W, nodeDragThreshold: ac, connectionDragThreshold: cc, onBeforeDelete: B, debug: pc, ariaLabelConfig: gc, zIndexMode: kr }), h.jsx(Gp, { onInit: c, onNodeClick: a, onEdgeClick: l, onNodeMouseEnter: S, onNodeMouseMove: p, onNodeMouseLeave: v, onNodeContextMenu: N, onNodeDoubleClick: E, nodeTypes: i, edgeTypes: s, connectionLineType: O, connectionLineStyle: U, connectionLineComponent: K, connectionLineContainerStyle: X, selectionKeyCode: G, selectionOnDrag: T, selectionMode: Z, deleteKeyCode: ie, multiSelectionKeyCode: oe, panActivationKeyCode: re, zoomActivationKeyCode: Q, onlyRenderVisibleElements: L, defaultViewport: Ce, translateExtent: Ze, minZoom: Pe, maxZoom: $e, preventScrolling: Pt, zoomOnScroll: to, zoomOnPinch: no, zoomOnDoubleClick: io, panOnScroll: oo, panOnScrollSpeed: Tt, panOnScrollMode: ro, panOnDrag: so, autoPanOnSelection: tc, onPaneClick: ao, onPaneMouseEnter: co, onPaneMouseMove: lo, onPaneMouseLeave: uo, onPaneScroll: ln, onPaneContextMenu: un, paneClickDistance: fo, nodeClickDistance: ho, onSelectionContextMenu: A, onSelectionStart: $, onSelectionEnd: P, onReconnect: go, onReconnectStart: I, onReconnectEnd: R, onEdgeContextMenu: Y, onEdgeDoubleClick: q, onEdgeMouseEnter: ee, onEdgeMouseMove: de, onEdgeMouseLeave: ue, reconnectRadius: ve, defaultMarkerColor: eo, noDragClassName: Ee, noWheelClassName: Wa, noPanClassName: br, rfId: mo, disableKeyboardA11y: Nr, nodeExtent: $t, viewport: lc, onViewportChange: uc }), h.jsx(Ih, { onSelectionChange: H }), po, h.jsx(Eh, { proOptions: Ua, position: Za }), h.jsx(Sh, { rfId: mo, disableKeyboardA11y: Nr })] }) });
}
var rg = la(og);
const ig = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function sg({ children: e }) {
  const t = le(ig);
  return t ? gh.createPortal(e, t) : null;
}
function ag({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return h.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: we(["react-flow__background-pattern", n, o]) });
}
function cg({ radius: e, className: t }) {
  return h.jsx("circle", { cx: e, cy: e, r: e, className: we(["react-flow__background-pattern", "dots", t]) });
}
var nt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(nt || (nt = {}));
const lg = {
  [nt.Dots]: 1,
  [nt.Lines]: 1,
  [nt.Cross]: 6
}, ug = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function Pa({
  id: e,
  variant: t = nt.Dots,
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
  const d = ae(null), { transform: f, patternId: m } = le(ug, pe), g = o || lg[t], y = t === nt.Dots, w = t === nt.Cross, x = Array.isArray(n) ? n : [n, n], S = [x[0] * f[2] || 1, x[1] * f[2] || 1], p = g * f[2], v = Array.isArray(i) ? i : [i, i], N = w ? [p, p] : S, E = [
    v[0] * f[2] || 1 + N[0] / 2,
    v[1] * f[2] || 1 + N[1] / 2
  ], M = `${m}${e || ""}`;
  return h.jsxs("svg", { className: we(["react-flow__background", c]), style: {
    ...l,
    ...Gn,
    "--xy-background-color-props": a,
    "--xy-background-pattern-color-props": s
  }, ref: d, "data-testid": "rf__background", children: [h.jsx("pattern", { id: M, x: f[0] % S[0], y: f[1] % S[1], width: S[0], height: S[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${E[0]},-${E[1]})`, children: y ? h.jsx(cg, { radius: p / 2, className: u }) : h.jsx(ag, { dimensions: N, lineWidth: r, variant: t, className: u }) }), h.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${M})` })] });
}
Pa.displayName = "Background";
const dg = xe(Pa);
function fg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: h.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function hg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: h.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function pg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: h.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function gg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: h.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function mg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: h.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function wn({ children: e, className: t, ...n }) {
  return h.jsx("button", { type: "button", className: we(["react-flow__controls-button", t]), ...n, children: e });
}
const yg = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function $a({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: i, onZoomOut: s, onFitView: a, onInteractiveChange: l, className: c, children: u, position: d = "bottom-left", orientation: f = "vertical", "aria-label": m }) {
  const g = ge(), { isInteractive: y, minZoomReached: w, maxZoomReached: x, ariaLabelConfig: S } = le(yg, pe), { zoomIn: p, zoomOut: v, fitView: N } = gr(), E = () => {
    p(), i?.();
  }, M = () => {
    v(), s?.();
  }, D = () => {
    N(r), a?.();
  }, j = () => {
    g.setState({
      nodesDraggable: !y,
      nodesConnectable: !y,
      elementsSelectable: !y
    }), l?.(!y);
  }, F = f === "horizontal" ? "horizontal" : "vertical";
  return h.jsxs(Kn, { className: we(["react-flow__controls", F, c]), position: d, style: e, "data-testid": "rf__controls", "aria-label": m ?? S["controls.ariaLabel"], children: [t && h.jsxs(h.Fragment, { children: [h.jsx(wn, { onClick: E, className: "react-flow__controls-zoomin", title: S["controls.zoomIn.ariaLabel"], "aria-label": S["controls.zoomIn.ariaLabel"], disabled: x, children: h.jsx(fg, {}) }), h.jsx(wn, { onClick: M, className: "react-flow__controls-zoomout", title: S["controls.zoomOut.ariaLabel"], "aria-label": S["controls.zoomOut.ariaLabel"], disabled: w, children: h.jsx(hg, {}) })] }), n && h.jsx(wn, { className: "react-flow__controls-fitview", onClick: D, title: S["controls.fitView.ariaLabel"], "aria-label": S["controls.fitView.ariaLabel"], children: h.jsx(pg, {}) }), o && h.jsx(wn, { className: "react-flow__controls-interactive", onClick: j, title: S["controls.interactive.ariaLabel"], "aria-label": S["controls.interactive.ariaLabel"], children: y ? h.jsx(mg, {}) : h.jsx(gg, {}) }), u] });
}
$a.displayName = "Controls";
const xg = xe($a);
function wg({ id: e, x: t, y: n, width: o, height: r, style: i, color: s, strokeColor: a, strokeWidth: l, className: c, borderRadius: u, shapeRendering: d, selected: f, onClick: m }) {
  const { background: g, backgroundColor: y } = i || {}, w = s || g || y;
  return h.jsx("rect", { className: we(["react-flow__minimap-node", { selected: f }, c]), x: t, y: n, rx: u, ry: u, width: o, height: r, style: {
    fill: w,
    stroke: a,
    strokeWidth: l
  }, shapeRendering: d, onClick: m ? (x) => m(x, e) : void 0 });
}
const vg = xe(wg), bg = (e) => e.nodes.map((t) => t.id), Do = (e) => e instanceof Function ? e : () => e;
function Sg({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: i = vg,
  onClick: s
}) {
  const a = le(bg, pe), l = Do(t), c = Do(e), u = Do(n), d = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return h.jsx(h.Fragment, { children: a.map((f) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    h.jsx(Ng, { id: f, nodeColorFunc: l, nodeStrokeColorFunc: c, nodeClassNameFunc: u, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: i, onClick: s, shapeRendering: d }, f)
  )) });
}
function Eg({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: i, shapeRendering: s, NodeComponent: a, onClick: l }) {
  const { node: c, x: u, y: d, width: f, height: m } = le((g) => {
    const y = g.nodeLookup.get(e);
    if (!y)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const w = y.internals.userNode, { x, y: S } = y.internals.positionAbsolute, { width: p, height: v } = Qe(w);
    return {
      node: w,
      x,
      y: S,
      width: p,
      height: v
    };
  }, pe);
  return !c || c.hidden || !js(c) ? null : h.jsx(a, { x: u, y: d, width: f, height: m, style: c.style, selected: !!c.selected, className: o(c), color: t(c), borderRadius: r, strokeColor: n(c), strokeWidth: i, shapeRendering: s, onClick: l, id: c.id });
}
const Ng = xe(Eg);
var _g = xe(Sg);
const Cg = 200, kg = 150, Ig = (e) => !e.hidden, Mg = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? As(rn(e.nodeLookup, { filter: Ig }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, Ag = "react-flow__minimap-desc";
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
  maskStrokeWidth: d,
  position: f = "bottom-right",
  onClick: m,
  onNodeClick: g,
  pannable: y = !1,
  zoomable: w = !1,
  ariaLabel: x,
  inversePan: S,
  zoomStep: p = 1,
  offsetScale: v = 5
}) {
  const N = ge(), E = ae(null), { boundingRect: M, viewBB: D, rfId: j, panZoom: F, translateExtent: k, flowWidth: z, flowHeight: H, ariaLabelConfig: b } = le(Mg, pe), C = e?.width ?? Cg, _ = e?.height ?? kg, A = M.width / C, $ = M.height / _, P = Math.max(A, $), B = P * C, V = P * _, O = v * P, U = M.x - (B - M.width) / 2 - O, K = M.y - (V - M.height) / 2 - O, X = B + O * 2, ie = V + O * 2, G = `${Ag}-${j}`, T = ae(0), Z = ae();
  T.current = P, ce(() => {
    if (E.current && F)
      return Z.current = Of({
        domNode: E.current,
        panZoom: F,
        getTransform: () => N.getState().transform,
        getViewScale: () => T.current
      }), () => {
        Z.current?.destroy();
      };
  }, [F]), ce(() => {
    Z.current?.update({
      translateExtent: k,
      width: z,
      height: H,
      inversePan: S,
      pannable: y,
      zoomStep: p,
      zoomable: w
    });
  }, [y, w, S, p, k, z, H]);
  const re = m ? (te) => {
    const [se, L] = Z.current?.pointer(te) || [0, 0];
    m(te, { x: se, y: L });
  } : void 0, oe = g ? he((te, se) => {
    const L = N.getState().nodeLookup.get(se).internals.userNode;
    g(te, L);
  }, []) : void 0, Q = x ?? b["minimap.ariaLabel"];
  return h.jsx(Kn, { position: f, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-background-color-props": typeof c == "string" ? c : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof d == "number" ? d * P : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof s == "number" ? s : void 0
  }, className: we(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: h.jsxs("svg", { width: C, height: _, viewBox: `${U} ${K} ${X} ${ie}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": G, ref: E, onClick: re, children: [Q && h.jsx("title", { id: G, children: Q }), h.jsx(_g, { onClick: oe, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: i, nodeClassName: r, nodeStrokeWidth: s, nodeComponent: a }), h.jsx("path", { className: "react-flow__minimap-mask", d: `M${U - O},${K - O}h${X + O * 2}v${ie + O * 2}h${-X - O * 2}z
        M${D.x},${D.y}h${D.width}v${D.height}h${-D.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
Ta.displayName = "MiniMap";
const Dg = xe(Ta), jg = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, Pg = {
  [kt.Line]: "right",
  [kt.Handle]: "bottom-right"
};
function $g({ nodeId: e, position: t, variant: n = kt.Handle, className: o, style: r = void 0, children: i, color: s, minWidth: a = 10, minHeight: l = 10, maxWidth: c = Number.MAX_VALUE, maxHeight: u = Number.MAX_VALUE, keepAspectRatio: d = !1, resizeDirection: f, autoScale: m = !0, shouldResize: g, onResizeStart: y, onResize: w, onResizeEnd: x }) {
  const S = ha(), p = typeof e == "string" ? e : S, v = ge(), N = ae(null), E = n === kt.Handle, M = le(he(jg(E && m), [E, m]), pe), D = ae(null), j = t ?? Pg[n];
  ce(() => {
    if (!(!N.current || !p))
      return D.current || (D.current = eh({
        domNode: N.current,
        nodeId: p,
        getStoreItems: () => {
          const { nodeLookup: k, transform: z, snapGrid: H, snapToGrid: b, nodeOrigin: C, domNode: _ } = v.getState();
          return {
            nodeLookup: k,
            transform: z,
            snapGrid: H,
            snapToGrid: b,
            nodeOrigin: C,
            paneDomNode: _
          };
        },
        onChange: (k, z) => {
          const { triggerNodeChanges: H, nodeLookup: b, parentLookup: C, nodeOrigin: _ } = v.getState(), A = [], $ = { x: k.x, y: k.y }, P = b.get(p);
          if (P && P.expandParent && P.parentId) {
            const B = P.origin ?? _, V = k.width ?? P.measured.width ?? 0, O = k.height ?? P.measured.height ?? 0, U = {
              id: P.id,
              parentId: P.parentId,
              rect: {
                width: V,
                height: O,
                ...Ps({
                  x: k.x ?? P.position.x,
                  y: k.y ?? P.position.y
                }, { width: V, height: O }, P.parentId, b, B)
              }
            }, K = pr([U], b, C, _);
            A.push(...K), $.x = k.x ? Math.max(B[0] * V, k.x) : void 0, $.y = k.y ? Math.max(B[1] * O, k.y) : void 0;
          }
          if ($.x !== void 0 && $.y !== void 0) {
            const B = {
              id: p,
              type: "position",
              position: { ...$ }
            };
            A.push(B);
          }
          if (k.width !== void 0 && k.height !== void 0) {
            const V = {
              id: p,
              type: "dimensions",
              resizing: !0,
              setAttributes: f ? f === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: k.width,
                height: k.height
              }
            };
            A.push(V);
          }
          for (const B of z) {
            const V = {
              ...B,
              type: "position"
            };
            A.push(V);
          }
          H(A);
        },
        onEnd: ({ width: k, height: z }) => {
          const H = {
            id: p,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: k,
              height: z
            }
          };
          v.getState().triggerNodeChanges([H]);
        }
      })), D.current.update({
        controlPosition: j,
        boundaries: {
          minWidth: a,
          minHeight: l,
          maxWidth: c,
          maxHeight: u
        },
        keepAspectRatio: d,
        resizeDirection: f,
        onResizeStart: y,
        onResize: w,
        onResizeEnd: x,
        shouldResize: g
      }), () => {
        D.current?.destroy();
      };
  }, [
    j,
    a,
    l,
    c,
    u,
    d,
    y,
    w,
    x,
    g
  ]);
  const F = j.split("-");
  return h.jsx("div", { className: we(["react-flow__resize-control", "nodrag", ...F, n, o]), ref: N, style: {
    ...r,
    scale: M,
    ...s && { [E ? "backgroundColor" : "borderColor"]: s }
  }, children: i });
}
xe($g);
const Tg = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), za = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var zg = {
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
const Rg = Vn(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: o,
    className: r = "",
    children: i,
    iconNode: s,
    ...a
  }, l) => $o(
    "svg",
    {
      ref: l,
      ...zg,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: za("lucide", r),
      ...a
    },
    [
      ...s.map(([c, u]) => $o(c, u)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const Ne = (e, t) => {
  const n = Vn(
    ({ className: o, ...r }, i) => $o(Rg, {
      ref: i,
      iconNode: t,
      className: za(`lucide-${Tg(e)}`, o),
      ...r
    })
  );
  return n.displayName = `${e}`, n;
};
const Lg = Ne("Boxes", [
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
const Qn = Ne("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const Hg = Ne("ChevronDown", [
  ["path", { d: "m6 9 6 6 6-6", key: "qrunsl" }]
]);
const Vg = Ne("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const _n = Ne("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const Jt = Ne("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const Og = Ne("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const Bg = Ne("ListTree", [
  ["path", { d: "M21 12h-8", key: "1bmf0i" }],
  ["path", { d: "M21 6H8", key: "1pqkrb" }],
  ["path", { d: "M21 18h-8", key: "1tm79t" }],
  ["path", { d: "M3 6v4c0 1.1.9 2 2 2h3", key: "1ywdgy" }],
  ["path", { d: "M3 10v6c0 1.1.9 2 2 2h3", key: "2wc746" }]
]);
const Ra = Ne("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const yr = Ne("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const Fg = Ne("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const Yg = Ne("Save", [
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
const Xg = Ne("Search", [
  ["circle", { cx: "11", cy: "11", r: "8", key: "4ej97u" }],
  ["path", { d: "m21 21-4.3-4.3", key: "1qie3q" }]
]);
const Mt = Ne("Sparkles", [
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
const Zo = Ne("Trash2", [
  ["path", { d: "M3 6h18", key: "d0wm0j" }],
  ["path", { d: "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", key: "4alrt4" }],
  ["path", { d: "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", key: "v07s0e" }],
  ["line", { x1: "10", x2: "10", y1: "11", y2: "17", key: "1uufr5" }],
  ["line", { x1: "14", x2: "14", y1: "11", y2: "17", key: "xtxkd" }]
]), Oe = "/_elsa/workflow-management", Hi = 1e4;
async function Wg(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), rt(e, `${Oe}/definitions?${n.toString()}`);
}
async function qg(e, t) {
  return rt(e, `${Oe}/definitions/${encodeURIComponent(t)}`);
}
async function Zg(e, t) {
  return cn(e, `${Oe}/definitions`, t);
}
async function Ug(e, t) {
  await rt(e, `${Oe}/definitions/${encodeURIComponent(t)}`, {
    method: "DELETE"
  });
}
async function Kg(e, t) {
  await cn(e, `${Oe}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function Gg(e, t) {
  await rt(e, `${Oe}/definitions/${encodeURIComponent(t)}/permanent`, {
    method: "DELETE"
  });
}
async function Qg(e, t) {
  return rt(e, `${Oe}/drafts/${encodeURIComponent(t.id)}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify({ state: t.state, layout: t.layout })
  });
}
async function Jg(e, t) {
  return cn(e, `${Oe}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function em(e, t) {
  return cn(e, `${Oe}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function La(e, t) {
  return cn(e, `${Oe}/executables/${encodeURIComponent(t)}/run`, {});
}
async function tm(e) {
  return rt(e, "/_demo/workflows/executables");
}
async function Ha(e) {
  return rt(e, `${Oe}/activities`);
}
async function cn(e, t, n) {
  return rt(e, t, {
    method: "POST",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify(n)
  });
}
async function rt(e, t, n) {
  const o = new URL(t, e.baseUrl).toString(), r = new AbortController(), i = window.setTimeout(() => r.abort(), Hi);
  let s;
  try {
    s = await fetch(o, {
      ...n,
      signal: r.signal
    });
  } catch (l) {
    throw r.signal.aborted ? new Error(`Request to ${o} timed out after ${Hi / 1e3} seconds. Check Studio:BackendBaseUrl and make sure the backend workflow-management API is responding.`) : l;
  } finally {
    window.clearTimeout(i);
  }
  const a = await s.text();
  if (!s.ok)
    throw new Error(nm(a) || `Request failed with ${s.status}.`);
  return a ? JSON.parse(a) : {};
}
function nm(e) {
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
const xr = "elsa.sequence.structure", Jn = "elsa.flowchart.structure";
function jo(e, t) {
  if (!e) return null;
  let n = e, o = Ye(n)[0];
  if (!o) return null;
  for (const r of t) {
    const i = Ye(n).find((a) => a.id === r.slotId);
    if (!i) return null;
    const s = i.activities.find((a) => a.nodeId === r.ownerNodeId);
    if (!s || (n = s, o = Ye(n)[0], !o)) return null;
  }
  return { owner: n, slot: o };
}
function Ye(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = xm(t), r = Po(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: wm(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, i]) => Po(i)).map(([i, s]) => ({
    id: `${t.kind}:${i}`,
    label: bm(i),
    property: i,
    mode: "generic",
    activities: Po(s) ?? []
  }));
}
function om(e, t, n) {
  const o = new Map(t.map((s) => [s.activityVersionId, s])), r = new Map(n.map((s) => [s.nodeId, s])), i = e.slot.activities.map((s, a) => {
    const l = o.get(s.activityVersionId), c = r.get(s.nodeId) ?? vm(e.slot.mode, a);
    return {
      id: s.nodeId,
      type: "workflowActivity",
      position: { x: c.x, y: c.y },
      data: {
        label: l?.displayName ?? s.activityVersionId,
        activityVersionId: s.activityVersionId,
        activityTypeKey: l?.activityTypeKey,
        childSlots: Ye(s),
        acceptsInbound: fm(s, l),
        sourcePorts: Oa(s, l)
      }
    };
  });
  return {
    nodes: i,
    edges: e.slot.mode === "flowchart" ? dm(e.owner) : um(e.slot, i)
  };
}
function Uo(e, t, n) {
  if (t.length === 0) {
    const a = Ye(e)[0];
    return a ? Ln(e, a, n) : e;
  }
  const [o, ...r] = t, i = Ye(e).find((a) => a.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((a) => a.nodeId === o.ownerNodeId ? Uo(a, r, n) : a);
  return Ln(e, i, s);
}
function Va(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, i = Ye(e).find((a) => a.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((a) => a.nodeId === o.ownerNodeId ? Va(a, r, n) : a);
  return Ln(e, i, s);
}
function Ln(e, t, n) {
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
function rm(e, t, n, o = []) {
  const r = new Map(e.slot.activities.map((s) => [s.nodeId, s]));
  for (const s of o)
    r.set(s.nodeId, s);
  const i = t.map((s) => r.get(s.id)).filter((s) => !!s);
  return e.slot.mode === "sequence" && i.sort((s, a) => {
    const l = t.find((u) => u.id === s.nodeId), c = t.find((u) => u.id === a.nodeId);
    return (l?.position.x ?? 0) - (c?.position.x ?? 0);
  }), Ln(e.owner, e.slot, i);
}
function im(e, t) {
  return {
    ...e,
    structure: lm(e.structure, t)
  };
}
function sm(e, t) {
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
    structure: cm(e)
  };
}
function Ae(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? am(t) : n;
}
function am(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function cm(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: xr,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: Jn,
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
function lm(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], o = /* @__PURE__ */ new Map();
  for (const r of n) {
    if (!wr(r)) continue;
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
function um(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function dm(e) {
  if (e.structure?.kind !== Jn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, i = n.target;
    if (!r?.nodeId || !i?.nodeId) return null;
    const s = Array.isArray(n.vertices) ? n.vertices.filter(mm) : [];
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
function Oa(e, t) {
  const n = Oi(e.cases);
  if (pm(e, t) && n.length > 0)
    return [...n.map((i) => ({ name: i, displayName: i })), { name: "Default", displayName: "Default" }];
  const o = [
    ...Cn(t?.designFacets),
    ...Cn(t?.ports),
    ...Cn(t?.outputs)
  ];
  if (o.length > 0) return gm(o);
  const r = Oi(e.outcomes);
  return r.length > 0 ? r.map((i) => ({ name: i, displayName: i })) : [{ name: "Done", displayName: "Done" }];
}
function fm(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function Hn(e, t, n, o) {
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
function hm(e, t, n) {
  const o = Hn(t.source, n, t.sourceHandle ?? "Done", void 0), r = Hn(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((i) => i.id !== t.id).concat(o, r);
}
function Po(e) {
  return Array.isArray(e) ? e.filter(ym) : null;
}
function pm(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function Cn(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!wr(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...Cn(n.ports));
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
function gm(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function Oi(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function mm(e) {
  return wr(e) && typeof e.x == "number" && typeof e.y == "number";
}
function wr(e) {
  return typeof e == "object" && e !== null;
}
function ym(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function xm(e) {
  return e.kind === xr ? "sequence" : e.kind === Jn ? "flowchart" : "generic";
}
function wm(e) {
  return e.kind === xr || e.kind === Jn, "Activities";
}
function vm(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function bm(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Sm = { workflowActivity: Bm }, Em = { workflow: Fm }, Bi = "application/x-elsa-activity-version-id", Nm = 6, _m = 1200, Cm = [10, 25, 50], km = 10, Ba = ot.createContext(null);
function Um(e) {
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
    component: () => /* @__PURE__ */ h.jsx(Im, { context: e.backend, ai: e.ai })
  }), e.routes.add({
    id: "workflows-executables",
    path: "/workflows/executables",
    label: "Workflow executables",
    component: () => /* @__PURE__ */ h.jsx(Mm, { context: e.backend, ai: e.ai })
  }), e.routes.add({
    id: "workflows-instances",
    path: "/workflows/instances",
    label: "Workflow instances",
    component: () => /* @__PURE__ */ h.jsx(Am, { ai: e.ai })
  });
}
function Im({ context: e, ai: t }) {
  const [n, o] = ne(Fi);
  ce(() => {
    const i = () => o(Fi());
    return window.addEventListener("popstate", i), () => window.removeEventListener("popstate", i);
  }, []);
  const r = (i) => {
    const s = i ? `/workflows/definitions?definition=${encodeURIComponent(i)}` : "/workflows/definitions";
    window.history.pushState({}, "", s), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return n ? /* @__PURE__ */ h.jsx(Om, { context: e, definitionId: n, ai: t, onBack: () => r(null) }) : /* @__PURE__ */ h.jsx(vr, { activePath: "/workflows/definitions", title: "Definitions", children: /* @__PURE__ */ h.jsx(Dm, { context: e, ai: t, onOpen: r }) });
}
function Mm({ context: e, ai: t }) {
  const [n, o] = ne(Yi);
  return ce(() => {
    const r = () => o(Yi());
    return window.addEventListener("popstate", r), () => window.removeEventListener("popstate", r);
  }, []), /* @__PURE__ */ h.jsx(vr, { activePath: "/workflows/executables", title: "Executables", children: /* @__PURE__ */ h.jsx(Pm, { context: e, ai: t, definitionFilter: n }) });
}
function Am({ ai: e }) {
  const t = At(e, "weaver.workflows.explain-instance");
  return /* @__PURE__ */ h.jsx(vr, { activePath: "/workflows/instances", title: "Instances", children: /* @__PURE__ */ h.jsxs("div", { className: "wf-empty", children: [
    "Workflow instance history will appear here when the runtime exposes an instance query endpoint.",
    t ? /* @__PURE__ */ h.jsxs("button", { type: "button", className: "wf-ai-inline-action", onClick: () => Dt(e, t, { scope: "workflow-instances" }), children: [
      /* @__PURE__ */ h.jsx(Mt, { size: 13 }),
      " Ask Weaver about instances"
    ] }) : null
  ] }) });
}
function vr({ activePath: e, title: t, children: n }) {
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
function Yi() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Dm({ context: e, ai: t, onOpen: n }) {
  const [o, r] = ne(""), [i, s] = ne("active"), [a, l] = ne(1), [c, u] = ne(km), [d, f] = ne("loading"), [m, g] = ne(""), [y, w] = ne(""), [x, S] = ne([]), [p, v] = ne(0), [N, E] = ne(() => /* @__PURE__ */ new Set()), [M, D] = ne(null), [j, F] = ne(!1), [k, z] = ne([]), [H, b] = ne("idle"), C = ae(null), _ = ye(() => x.map((L) => L.id), [x]), A = At(t, "weaver.workflows.suggest-create-metadata"), $ = At(t, "weaver.workflows.explain-definition"), P = _.filter((L) => N.has(L)).length, B = _.length > 0 && P === _.length, V = he(async () => {
    f("loading"), g("");
    try {
      const L = await Wg(e, { search: o, state: i, page: a, pageSize: c }), W = typeof L.totalCount == "number", fe = L.totalCount ?? L.definitions.length, me = Fa(fe, c);
      if (fe > 0 && a > me) {
        l(me);
        return;
      }
      S(W ? L.definitions : Tm(L.definitions, a, c)), v(fe), f("ready");
    } catch (L) {
      g(L instanceof Error ? L.message : String(L)), f("failed");
    }
  }, [e, o, i, a, c]);
  ce(() => {
    V();
  }, [V]), ce(() => {
    C.current && (C.current.indeterminate = P > 0 && !B);
  }, [B, P]);
  const O = he(async () => {
    if (!(H === "loading" || H === "ready")) {
      b("loading");
      try {
        const L = await Ha(e);
        z(L.activities ?? []), b("ready");
      } catch (L) {
        b("failed"), g(L instanceof Error ? L.message : String(L));
      }
    }
  }, [H, e]), U = () => {
    g(""), w(""), D({ name: "", description: "", rootKind: "flowchart" }), O();
  }, K = async () => {
    if (M?.name.trim()) {
      F(!0), g(""), w("");
      try {
        const L = await Zg(e, {
          name: M.name.trim(),
          description: M.description.trim() || null,
          rootKind: M.rootKind
        });
        D(null), n(L.definition.id);
      } catch (L) {
        g(L instanceof Error ? L.message : String(L));
      } finally {
        F(!1);
      }
    }
  }, X = (L) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(L)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, ie = async () => {
    if (x.length === 1 && a > 1) {
      l(a - 1);
      return;
    }
    await V();
  }, G = () => E(/* @__PURE__ */ new Set()), T = (L, W) => {
    E((fe) => {
      const me = new Set(fe);
      return W ? me.add(L) : me.delete(L), me;
    });
  }, Z = (L) => {
    E((W) => {
      const fe = new Set(W);
      for (const me of _)
        L ? fe.add(me) : fe.delete(me);
      return fe;
    });
  }, re = (L) => {
    s(L), l(1), G();
  }, oe = (L) => {
    r(L), l(1), G();
  }, Q = async (L) => {
    if (window.confirm(`Delete workflow definition "${L.name}"? You can restore it from the Deleted view.`)) {
      w(""), g("");
      try {
        await Ug(e, L.id), T(L.id, !1), w(`Deleted ${L.name}`), await ie();
      } catch (W) {
        g(W instanceof Error ? W.message : String(W));
      }
    }
  }, te = async (L) => {
    w(""), g("");
    try {
      await Kg(e, L.id), T(L.id, !1), w(`Restored ${L.name}`), await ie();
    } catch (W) {
      g(W instanceof Error ? W.message : String(W));
    }
  }, se = async (L) => {
    if (window.confirm(`Permanently delete workflow definition "${L.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`)) {
      w(""), g("");
      try {
        await Gg(e, L.id), T(L.id, !1), w(`Permanently deleted ${L.name}`), await ie();
      } catch (W) {
        g(W instanceof Error ? W.message : String(W));
      }
    }
  };
  return /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
    /* @__PURE__ */ h.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ h.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ h.jsx("button", { type: "button", className: i === "active" ? "active" : "", "aria-selected": i === "active", onClick: () => re("active"), children: "Active" }),
        /* @__PURE__ */ h.jsx("button", { type: "button", className: i === "deleted" ? "active" : "", "aria-selected": i === "deleted", onClick: () => re("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ h.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ h.jsx(Xg, { size: 15 }),
        /* @__PURE__ */ h.jsx("input", { value: o, onChange: (L) => oe(L.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
        V();
      }, children: "Refresh" }),
      /* @__PURE__ */ h.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ h.jsxs("button", { type: "button", title: "Create workflow", onClick: U, children: [
        /* @__PURE__ */ h.jsx(yr, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    d === "failed" ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Jt, { size: 16 }),
      " ",
      m
    ] }) : null,
    d !== "failed" && m ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Jt, { size: 16 }),
      " ",
      m
    ] }) : null,
    y ? /* @__PURE__ */ h.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ h.jsx(Qn, { size: 14 }),
      " ",
      y
    ] }) : null,
    N.size > 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ h.jsxs("span", { children: [
        N.size,
        " selected"
      ] }),
      /* @__PURE__ */ h.jsx("button", { type: "button", onClick: G, children: "Clear selection" })
    ] }) : null,
    d === "loading" ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: "Loading workflow definitions..." }) : null,
    d === "ready" && x.length === 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-empty", children: [
      "No ",
      i,
      " workflow definitions found."
    ] }) : null,
    d === "ready" && x.length > 0 ? /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
      /* @__PURE__ */ h.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ h.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ h.jsx(
            "input",
            {
              ref: C,
              type: "checkbox",
              checked: B,
              onChange: (L) => Z(L.target.checked),
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
            "aria-selected": N.has(L.id),
            tabIndex: 0,
            onClick: () => n(L.id),
            onKeyDown: (W) => {
              W.currentTarget === W.target && (W.key !== "Enter" && W.key !== " " || (W.preventDefault(), n(L.id)));
            },
            children: [
              /* @__PURE__ */ h.jsx("label", { className: "wf-row-select", onClick: (W) => W.stopPropagation(), children: /* @__PURE__ */ h.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: N.has(L.id),
                  onChange: (W) => T(L.id, W.target.checked),
                  "aria-label": `Select workflow definition ${L.name}`
                }
              ) }),
              /* @__PURE__ */ h.jsxs("span", { children: [
                /* @__PURE__ */ h.jsx("strong", { children: L.name }),
                /* @__PURE__ */ h.jsx("small", { children: L.description || L.id })
              ] }),
              /* @__PURE__ */ h.jsx("span", { children: L.latestVersion ?? "No version" }),
              /* @__PURE__ */ h.jsx("span", { children: i === "deleted" ? Ko(L.deletedAt) : L.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ h.jsx("span", { children: Ko(L.lastModifiedAt) }),
              /* @__PURE__ */ h.jsx("span", { className: "wf-row-actions", onClick: (W) => W.stopPropagation(), children: i === "active" ? /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
                /* @__PURE__ */ h.jsx("button", { type: "button", onClick: (W) => {
                  W.stopPropagation(), n(L.id);
                }, children: "Open" }),
                /* @__PURE__ */ h.jsx("button", { type: "button", onClick: (W) => {
                  W.stopPropagation(), X(L.id);
                }, children: "Artifacts" }),
                $ ? /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => Dt(t, $, L), children: [
                  /* @__PURE__ */ h.jsx(Mt, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ h.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  Q(L);
                }, children: [
                  /* @__PURE__ */ h.jsx(Zo, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
                /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
                  te(L);
                }, children: [
                  /* @__PURE__ */ h.jsx(Fg, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ h.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  se(L);
                }, children: [
                  /* @__PURE__ */ h.jsx(Zo, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          L.id
        ))
      ] }),
      /* @__PURE__ */ h.jsx(
        $m,
        {
          page: a,
          pageSize: c,
          totalCount: p,
          onPageChange: l,
          onPageSizeChange: (L) => {
            u(L), l(1);
          }
        }
      )
    ] }) : null,
    M ? /* @__PURE__ */ h.jsx(
      jm,
      {
        draft: M,
        activities: k,
        catalogState: H,
        creating: j,
        suggestMetadataAction: A,
        onSuggestMetadata: A ? () => Dt(t, A, { draft: M, activities: k }) : void 0,
        onChange: (L) => D(L),
        onClose: () => D(null),
        onSubmit: K
      }
    ) : null
  ] });
}
function jm({ draft: e, activities: t, catalogState: n, creating: o, suggestMetadataAction: r, onSuggestMetadata: i, onChange: s, onClose: a, onSubmit: l }) {
  const c = ye(() => zm(t), [t]);
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
                c.otherCategories.map((u) => /* @__PURE__ */ h.jsx("optgroup", { label: u.name, children: u.activities.map((d) => /* @__PURE__ */ h.jsx("option", { value: `unsupported:${d.activityVersionId}`, disabled: !0, children: Ae(d) }, d.activityVersionId)) }, u.name))
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
function Pm({ context: e, ai: t, definitionFilter: n }) {
  const [o, r] = ne("loading"), [i, s] = ne(""), [a, l] = ne(""), [c, u] = ne([]), d = ye(
    () => n ? c.filter((y) => y.definitionId === n || y.sourceId === n) : c,
    [n, c]
  ), f = At(t, "weaver.workflows.explain-executable"), m = he(async () => {
    r("loading"), s("");
    try {
      u(await tm(e)), r("ready");
    } catch (y) {
      s(y instanceof Error ? y.message : String(y)), r("failed");
    }
  }, [e]);
  ce(() => {
    m();
  }, [m]);
  const g = async (y) => {
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
      /* @__PURE__ */ h.jsx(Jt, { size: 16 }),
      " ",
      i
    ] }) : null,
    a ? /* @__PURE__ */ h.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ h.jsx(Qn, { size: 14 }),
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
      d.map((y) => /* @__PURE__ */ h.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ h.jsxs("span", { children: [
          /* @__PURE__ */ h.jsx("strong", { children: y.artifactId }),
          /* @__PURE__ */ h.jsx("small", { children: y.artifactHash })
        ] }),
        /* @__PURE__ */ h.jsx("span", { children: y.artifactVersion }),
        /* @__PURE__ */ h.jsx("span", { children: Lm(y) }),
        /* @__PURE__ */ h.jsx("span", { children: Hm(y) }),
        /* @__PURE__ */ h.jsx("span", { children: Ko(y.publishedAt ?? y.createdAt) }),
        /* @__PURE__ */ h.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
            g(y);
          }, children: [
            /* @__PURE__ */ h.jsx(Ra, { size: 13 }),
            " Run"
          ] }),
          f ? /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => Dt(t, f, y), children: [
            /* @__PURE__ */ h.jsx(Mt, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, y.artifactId))
    ] }) : null
  ] });
}
function $m({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const i = Fa(n, t), s = n === 0 ? 0 : (e - 1) * t + 1, a = Math.min(e * t, n);
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
      /* @__PURE__ */ h.jsx("select", { value: t, onChange: (l) => r(Number(l.target.value)), children: Cm.map((l) => /* @__PURE__ */ h.jsx("option", { value: l, children: l }, l)) })
    ] }),
    /* @__PURE__ */ h.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ h.jsx(Vg, { size: 14 }),
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
        /* @__PURE__ */ h.jsx(_n, { size: 14 })
      ] })
    ] })
  ] });
}
function Tm(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function Fa(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function At(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function Dt(e, t, n) {
  const o = t.createPrompt(n);
  o && e.dispatchPrompt(o);
}
function zm(e) {
  const t = [
    { value: "flowchart", label: "Flowchart" },
    { value: "sequence", label: "Sequence" }
  ], n = /* @__PURE__ */ new Map();
  for (const r of e.filter(Xa)) {
    if (Rm(r)) continue;
    const i = r.category || "Uncategorized";
    n.set(i, [...n.get(i) ?? [], r]);
  }
  const o = Array.from(n.entries()).sort(([r], [i]) => r.localeCompare(i)).map(([r, i]) => ({
    name: r,
    activities: i.sort((s, a) => Ae(s).localeCompare(Ae(a)))
  }));
  return { compositeRoots: t, otherCategories: o };
}
function Ya(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "Uncategorized";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [o]) => n.localeCompare(o)).map(([n, o]) => ({
    category: n,
    activities: o.sort((r, i) => Ae(r).localeCompare(Ae(i)))
  }));
}
function Rm(e) {
  const t = Ae(e);
  return t === "Flowchart" || t === "Sequence" || e.activityTypeKey.endsWith(".Flowchart") || e.activityTypeKey.endsWith(".Sequence");
}
function Xa(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function Lm(e) {
  return e.sourceKind || e.sourceId || e.sourceVersion ? [e.sourceKind, e.sourceId, e.sourceVersion].filter(Boolean).join(" / ") : e.definitionId;
}
function Hm(e) {
  return Vm(e.rootActivityType) || e.rootActivityType;
}
function Vm(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function Om({ context: e, definitionId: t, ai: n, onBack: o }) {
  const [r, i] = ne(null), [s, a] = ne(null), [l, c] = ne([]), [u, d] = ne([]), [f, m] = ne([]), [g, y] = ne([]), [w, x] = ne(null), [S, p] = ne(null), [v, N] = ne(null), [E, M] = ne(null), [D, j] = ne(""), [F, k] = ne(""), [z, H] = ne(!1), [b, C] = ne(null), [_, A] = ne(() => /* @__PURE__ */ new Set()), $ = ae(null), P = ae(null), B = ae(""), V = ae(0), O = ae(null), U = ae(!1), K = s?.state.rootActivity ?? null, X = ye(() => jo(K, u), [K, u]), ie = ye(() => new Map(l.map((I) => [I.activityVersionId, I])), [l]), G = ye(() => Ya(l), [l]), T = ye(() => X?.slot.activities.find((I) => I.nodeId === S) ?? null, [X, S]), Z = T ? Ye(T) : [], re = At(n, "weaver.workflows.find-draft-risks"), oe = At(n, "weaver.workflows.propose-update"), Q = he(async () => {
    j("");
    const [I, R] = await Promise.all([
      qg(e, t),
      Ha(e)
    ]), Y = I.draft ?? null;
    i(I), B.current = Y ? Ht(Y) : "", a(Y), c(R.activities ?? []), d([]), p(null);
  }, [e, t]);
  ce(() => {
    Q().catch((I) => j(I instanceof Error ? I.message : String(I)));
  }, [Q]), ce(() => {
    A((I) => {
      let R = !1;
      const Y = new Set(I);
      for (const q of G)
        Y.has(q.category) || (Y.add(q.category), R = !0);
      return R ? Y : I;
    });
  }, [G]), ce(() => {
    if (!X) {
      m([]), y([]);
      return;
    }
    const I = om(X, l, s?.layout ?? []);
    m(I.nodes), y(I.edges);
  }, [X, l, s?.layout]);
  const te = (I) => {
    a((R) => R && { ...R, state: { ...R.state, rootActivity: I } });
  }, se = he((I, R) => {
    const Y = Vi(I, Xi(I));
    if (!s?.state.rootActivity) {
      te(Y), p(Y.nodeId);
      return;
    }
    if (!X) {
      if (!Ye(Y)[0]) {
        k(""), j("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      a((ee) => {
        if (!ee?.state.rootActivity) return ee;
        const de = ee.state.rootActivity, ue = Uo(Y, [], [de]), ve = R ? [
          ...ee.layout.filter((Ie) => Ie.nodeId !== de.nodeId),
          {
            nodeId: de.nodeId,
            x: Math.round(R.x),
            y: Math.round(R.y)
          }
        ] : ee.layout;
        return {
          ...ee,
          layout: ve,
          state: {
            ...ee.state,
            rootActivity: ue
          }
        };
      }), p(s.state.rootActivity.nodeId), j(""), k(`Wrapped root in ${Ae(I)}`);
      return;
    }
    a((q) => {
      if (!q?.state.rootActivity) return q;
      const ee = jo(q.state.rootActivity, u);
      if (!ee) return q;
      const de = Uo(q.state.rootActivity, u, [...ee.slot.activities, Y]), ue = R ? [
        ...q.layout.filter((ve) => ve.nodeId !== Y.nodeId),
        {
          nodeId: Y.nodeId,
          x: Math.round(R.x),
          y: Math.round(R.y)
        }
      ] : q.layout;
      return {
        ...q,
        layout: ue,
        state: {
          ...q.state,
          rootActivity: de
        }
      };
    }), p(Y.nodeId);
  }, [s?.state.rootActivity, u, X]), L = he((I, R) => {
    const Y = Vi(I, Xi(I)), q = {
      id: Y.nodeId,
      type: "workflowActivity",
      position: R,
      selected: !0,
      data: {
        label: Ae(I),
        activityVersionId: I.activityVersionId,
        activityTypeKey: I.activityTypeKey,
        childSlots: Ye(Y),
        acceptsInbound: String(I.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: Oa(Y, I)
      }
    };
    return { activityNode: Y, node: q };
  }, []), W = he((I, R, Y = []) => {
    a((q) => {
      if (!q) return q;
      const ee = sm(q.layout, I), de = q.state.rootActivity;
      if (!de) return { ...q, layout: ee };
      const ue = jo(de, u);
      if (!ue) return { ...q, layout: ee };
      const ve = rm(ue, I, R, Y), Ie = ue.slot.mode === "flowchart" ? im(ve, R) : ve;
      return {
        ...q,
        layout: ee,
        state: {
          ...q.state,
          rootActivity: Va(de, u, Ie)
        }
      };
    });
  }, [u]), fe = he((I, R) => {
    if (!$.current) return null;
    const Y = $.current.getBoundingClientRect();
    return w ? w.screenToFlowPosition({ x: I, y: R }) : {
      x: I - Y.left,
      y: R - Y.top
    };
  }, [w]), me = he((I, R) => document.elementFromPoint(I, R)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), _e = he((I, R, Y) => {
    const q = f.find((Ee) => Ee.id === R.source), ee = f.find((Ee) => Ee.id === R.target), de = q && ee ? Wm(q, ee) : q ? Wi(q) : Y, ue = L(I, de), Ie = [...f.map((Ee) => Ee.selected ? { ...Ee, selected: !1 } : Ee), ue.node], st = hm(g, R, ue.node.id);
    m(Ie), y(st), p(ue.node.id), W(Ie, st, [ue.activityNode]);
  }, [W, L, g, f]), Se = he((I, R, Y) => {
    if (!$.current) return !1;
    const q = $.current.getBoundingClientRect();
    if (!(R >= q.left && R <= q.right && Y >= q.top && Y <= q.bottom)) return !1;
    const de = fe(R, Y);
    if (!de) return !1;
    if (X?.slot.mode === "flowchart") {
      const ue = me(R, Y), ve = ue ? g.find((Ie) => Ie.id === ue) : void 0;
      if (ve)
        return _e(I, ve, de), !0;
    }
    return se(I, de), !0;
  }, [se, g, me, X?.slot.mode, _e, fe]);
  ce(() => {
    const I = (Y) => {
      const q = O.current;
      if (!q) return;
      Math.hypot(Y.clientX - q.startX, Y.clientY - q.startY) >= Nm && (q.dragging = !0);
    }, R = (Y) => {
      const q = O.current;
      if (O.current = null, !q?.dragging || !$.current) return;
      const ee = $.current.getBoundingClientRect();
      Y.clientX >= ee.left && Y.clientX <= ee.right && Y.clientY >= ee.top && Y.clientY <= ee.bottom && (U.current = !0, window.setTimeout(() => {
        U.current = !1;
      }, 0), Se(q.activity, Y.clientX, Y.clientY));
    };
    return window.addEventListener("pointermove", I), window.addEventListener("pointerup", R), window.addEventListener("pointercancel", R), () => {
      window.removeEventListener("pointermove", I), window.removeEventListener("pointerup", R), window.removeEventListener("pointercancel", R);
    };
  }, [w, Se]);
  const Te = (I, R) => {
    I.dataTransfer.setData(Bi, R.activityVersionId), I.dataTransfer.setData("text/plain", R.activityVersionId), I.dataTransfer.effectAllowed = "copy";
  }, it = (I, R) => {
    I.clientX === 0 && I.clientY === 0 || Se(R, I.clientX, I.clientY) && (U.current = !0, window.setTimeout(() => {
      U.current = !1;
    }, 0));
  }, We = (I, R) => {
    I.button === 0 && (O.current = {
      activity: R,
      startX: I.clientX,
      startY: I.clientY,
      dragging: !1
    });
  }, qe = (I) => {
    U.current || se(I);
  }, Ce = (I) => {
    if (I.preventDefault(), I.dataTransfer.dropEffect = "copy", X?.slot.mode !== "flowchart") return;
    const R = me(I.clientX, I.clientY);
    M(R);
  }, Pe = (I) => {
    if (!$.current) return;
    const R = I.relatedTarget;
    R && $.current.contains(R) || M(null);
  }, $e = (I) => {
    I.preventDefault(), M(null);
    const R = I.dataTransfer.getData(Bi) || I.dataTransfer.getData("text/plain"), Y = ie.get(R);
    Y && Se(Y, I.clientX, I.clientY);
  }, Ze = () => {
    const I = $.current?.getBoundingClientRect();
    I && N({
      kind: "fromEmpty",
      clientX: I.left + I.width / 2,
      clientY: I.top + I.height / 2
    });
  }, Pt = he(async (I, R) => {
    const Y = ++V.current, q = Ht(I);
    j("");
    try {
      const ee = await Qg(e, I), de = Ht(ee);
      B.current = de, a((ue) => !ue || ue.id !== ee.id ? ue : Ht(ue) === q ? ee : { ...ue, validationErrors: ee.validationErrors }), Y === V.current && k(R);
    } catch (ee) {
      Y === V.current && (k(""), j(ee instanceof Error ? ee.message : String(ee)));
    }
  }, [e]);
  ce(() => {
    if (!z || !s || Ht(s) === B.current) return;
    k("Autosaving...");
    const R = window.setTimeout(() => {
      Pt(s, "Autosaved");
    }, _m);
    return () => window.clearTimeout(R);
  }, [z, s, Pt]);
  const $t = async () => {
    s && (k("Saving..."), await Pt(s, "Saved"));
  }, eo = async () => {
    if (s) {
      k("Promoting...");
      try {
        const I = await Jg(e, s.id), R = await em(e, I.versionId);
        C(R.artifactId), k(`Published ${R.artifactVersion}`), await Q();
      } catch (I) {
        k(""), j(I instanceof Error ? I.message : String(I));
      }
    }
  }, to = async () => {
    if (b) {
      k("Running...");
      try {
        await La(e, b), k("Run dispatched");
      } catch (I) {
        k(""), j(I instanceof Error ? I.message : String(I));
      }
    }
  }, no = (I) => m((R) => ia(I, R)), oo = (I) => y((R) => sa(I, R)), Tt = (I) => !I.source || !I.target || I.source === I.target || X?.slot.mode !== "flowchart" ? !1 : !I.targetHandle, ro = (I) => {
    if (!s?.state.rootActivity || !X || X.slot.mode !== "flowchart" || !Tt(I)) return;
    const R = Hn(I.source, I.target, I.sourceHandle ?? "Done", I.targetHandle ?? void 0), Y = ca(R, g);
    y(Y), W(f, Y);
  }, io = () => {
    W(f, g);
  }, so = (I, R) => {
    if (!R.nodeId || R.handleType === "target") {
      P.current = null;
      return;
    }
    P.current = {
      nodeId: R.nodeId,
      handleId: R.handleId ?? null
    };
  }, ao = (I) => {
    const R = P.current;
    if (P.current = null, !R || X?.slot.mode !== "flowchart" || I.target?.closest(".react-flow__handle, .react-flow__node")) return;
    const q = qm(I);
    N({
      kind: "fromPort",
      sourceNodeId: R.nodeId,
      sourceHandleId: R.handleId,
      clientX: q.x,
      clientY: q.y
    });
  }, co = (I, R) => {
    if (!Tt(R)) return;
    const Y = zh(I, {
      ...R,
      sourceHandle: R.sourceHandle ?? "Done",
      targetHandle: R.targetHandle ?? void 0
    }, g, { shouldReplaceId: !1 });
    y(Y), W(f, Y);
  }, lo = (I) => {
    if (I.length === 0) return;
    const R = new Set(I.map((ee) => ee.id)), Y = f.filter((ee) => !R.has(ee.id)), q = g.filter((ee) => !R.has(ee.source) && !R.has(ee.target));
    m(Y), y(q), S && R.has(S) && p(null), W(Y, q);
  }, uo = (I) => {
    if (I.length === 0) return;
    const R = new Set(I.map((q) => q.id)), Y = g.filter((q) => !R.has(q.id));
    y(Y), W(f, Y);
  }, ln = he((I) => {
    const R = g.filter((Y) => Y.id !== I);
    y(R), W(f, R);
  }, [W, g, f]), un = he((I, R, Y) => {
    N({ kind: "spliceEdge", edgeId: I, clientX: R, clientY: Y });
  }, []), fo = (I) => {
    const R = v;
    if (!R) return;
    N(null);
    const Y = fe(R.clientX, R.clientY) ?? { x: 0, y: 0 };
    if (R.kind === "fromEmpty") {
      const ee = L(I, Y), ue = [...f.map((ve) => ve.selected ? { ...ve, selected: !1 } : ve), ee.node];
      m(ue), p(ee.node.id), W(ue, g, [ee.activityNode]);
      return;
    }
    if (R.kind === "fromPort") {
      const ee = f.find((Ee) => Ee.id === R.sourceNodeId), de = ee ? Wi(ee) : Y, ue = L(I, de), Ie = [...f.map((Ee) => Ee.selected ? { ...Ee, selected: !1 } : Ee), ue.node], st = [...g, Hn(R.sourceNodeId, ue.node.id, R.sourceHandleId ?? "Done")];
      m(Ie), y(st), p(ue.node.id), W(Ie, st, [ue.activityNode]);
      return;
    }
    const q = g.find((ee) => ee.id === R.edgeId);
    q && _e(I, q, Y);
  }, ho = ye(() => ({
    highlightedEdgeId: E,
    deleteEdge: ln,
    requestInsertActivity: un
  }), [ln, E, un]), po = (I, R, Y) => {
    d((q) => [...q, { ownerNodeId: I.nodeId, slotId: R, label: Y }]), p(null);
  }, go = (I) => {
    A((R) => {
      const Y = new Set(R);
      return Y.has(I) ? Y.delete(I) : Y.add(I), Y;
    });
  };
  return !r || !s ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: D || "Loading workflow editor..." }) : /* @__PURE__ */ h.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ h.jsx("button", { type: "button", className: "wf-link-button", onClick: o, children: "Definitions" }),
      /* @__PURE__ */ h.jsx(_n, { size: 14 }),
      /* @__PURE__ */ h.jsx("strong", { children: r.definition.name }),
      /* @__PURE__ */ h.jsx("span", { className: "wf-chip", children: "Draft" }),
      F ? /* @__PURE__ */ h.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ h.jsx(Qn, { size: 13 }),
        " ",
        F
      ] }) : null,
      /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ h.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ h.jsx("input", { type: "checkbox", checked: z, onChange: (I) => H(I.target.checked) }),
          /* @__PURE__ */ h.jsx("span", { children: "Autosave" })
        ] }),
        re ? /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => Dt(n, re, { definition: r.definition, draft: s }), children: [
          /* @__PURE__ */ h.jsx(Mt, { size: 15 }),
          " Risks"
        ] }) : null,
        oe ? /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => Dt(n, oe, { definition: r.definition, draft: s }), children: [
          /* @__PURE__ */ h.jsx(Mt, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
          $t();
        }, children: [
          /* @__PURE__ */ h.jsx(Yg, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
          eo();
        }, children: [
          /* @__PURE__ */ h.jsx(Og, { size: 15 }),
          " Promote"
        ] }),
        /* @__PURE__ */ h.jsxs("button", { type: "button", disabled: !b, onClick: () => {
          to();
        }, children: [
          /* @__PURE__ */ h.jsx(Ra, { size: 15 }),
          " Run"
        ] })
      ] })
    ] }),
    D ? /* @__PURE__ */ h.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ h.jsx(Jt, { size: 16 }),
      " ",
      D
    ] }) : null,
    /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-body", children: [
      /* @__PURE__ */ h.jsxs("aside", { className: "wf-palette", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ h.jsx(Lg, { size: 15 }),
          " Activities"
        ] }),
        /* @__PURE__ */ h.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: G.map((I) => {
          const R = _.has(I.category);
          return /* @__PURE__ */ h.jsxs("div", { className: "wf-palette-category", children: [
            /* @__PURE__ */ h.jsxs(
              "button",
              {
                type: "button",
                className: "wf-palette-category-toggle",
                role: "treeitem",
                "aria-expanded": R,
                onClick: () => go(I.category),
                children: [
                  R ? /* @__PURE__ */ h.jsx(Hg, { size: 14 }) : /* @__PURE__ */ h.jsx(_n, { size: 14 }),
                  /* @__PURE__ */ h.jsx("span", { children: I.category }),
                  /* @__PURE__ */ h.jsx("small", { children: I.activities.length })
                ]
              }
            ),
            R ? /* @__PURE__ */ h.jsx("div", { className: "wf-palette-activities", role: "group", children: I.activities.map((Y) => {
              const q = Y.description?.trim(), ee = q ? `wf-palette-description-${Y.activityVersionId}` : void 0;
              return /* @__PURE__ */ h.jsxs(
                "button",
                {
                  type: "button",
                  className: "wf-palette-activity",
                  role: "treeitem",
                  draggable: !0,
                  title: q || Ae(Y),
                  "aria-describedby": ee,
                  onClick: () => qe(Y),
                  onDragStart: (de) => Te(de, Y),
                  onDragEnd: (de) => it(de, Y),
                  onPointerDown: (de) => We(de, Y),
                  children: [
                    /* @__PURE__ */ h.jsx("strong", { children: Ae(Y) }),
                    q ? /* @__PURE__ */ h.jsx("small", { id: ee, children: q }) : null
                  ]
                },
                Y.activityVersionId
              );
            }) }) : null
          ] }, I.category);
        }) })
      ] }),
      /* @__PURE__ */ h.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-breadcrumb", children: [
          /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
            d([]), p(null);
          }, children: "Root" }),
          u.map((I, R) => /* @__PURE__ */ h.jsxs(ot.Fragment, { children: [
            /* @__PURE__ */ h.jsx(_n, { size: 13 }),
            /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
              d(u.slice(0, R + 1)), p(null);
            }, children: I.label })
          ] }, `${I.ownerNodeId}-${I.slotId}-${R}`))
        ] }),
        /* @__PURE__ */ h.jsxs("div", { className: "wf-canvas", ref: $, onDragOver: Ce, onDragLeave: Pe, onDrop: $e, children: [
          /* @__PURE__ */ h.jsx(Ba.Provider, { value: ho, children: /* @__PURE__ */ h.jsxs(
            rg,
            {
              nodes: f,
              edges: g,
              nodeTypes: Sm,
              edgeTypes: Em,
              onInit: x,
              onNodesChange: no,
              onEdgesChange: oo,
              onNodesDelete: lo,
              onEdgesDelete: uo,
              onConnect: ro,
              onConnectStart: X?.slot.mode === "flowchart" ? so : void 0,
              onConnectEnd: X?.slot.mode === "flowchart" ? ao : void 0,
              onReconnect: X?.slot.mode === "flowchart" ? co : void 0,
              isValidConnection: Tt,
              onDragOver: Ce,
              onDragLeave: Pe,
              onDrop: $e,
              onPaneClick: () => p(null),
              onNodeClick: (I, R) => p(R.id),
              onNodeDragStop: io,
              fitView: !0,
              minZoom: 0.2,
              maxZoom: 1.8,
              nodesConnectable: X?.slot.mode === "flowchart",
              selectionOnDrag: !0,
              multiSelectionKeyCode: ["Shift", "Meta", "Control"],
              deleteKeyCode: ["Backspace", "Delete"],
              panActivationKeyCode: null,
              defaultEdgeOptions: { type: "workflow" },
              children: [
                /* @__PURE__ */ h.jsx(dg, { gap: 18, size: 1 }),
                /* @__PURE__ */ h.jsx(xg, {}),
                /* @__PURE__ */ h.jsx(Dg, { pannable: !0, zoomable: !0 })
              ]
            }
          ) }),
          X?.slot.mode === "flowchart" && f.length === 0 ? /* @__PURE__ */ h.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => Ze(), children: [
            /* @__PURE__ */ h.jsx(yr, { size: 15 }),
            " Add activity"
          ] }) : null,
          v ? /* @__PURE__ */ h.jsx(
            Ym,
            {
              clientX: v.clientX,
              clientY: v.clientY,
              activities: l,
              onPick: fo,
              onClose: () => N(null)
            }
          ) : null
        ] }),
        /* @__PURE__ */ h.jsx(Xm, { draft: s })
      ] }),
      /* @__PURE__ */ h.jsxs("aside", { className: "wf-inspector", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ h.jsx(Bg, { size: 15 }),
          " Inspector"
        ] }),
        T ? /* @__PURE__ */ h.jsxs("div", { className: "wf-inspector-content", children: [
          /* @__PURE__ */ h.jsx("h3", { children: f.find((I) => I.id === T.nodeId)?.data.label ?? T.nodeId }),
          /* @__PURE__ */ h.jsxs("dl", { children: [
            /* @__PURE__ */ h.jsx("dt", { children: "Node ID" }),
            /* @__PURE__ */ h.jsx("dd", { children: T.nodeId }),
            /* @__PURE__ */ h.jsx("dt", { children: "Activity version" }),
            /* @__PURE__ */ h.jsx("dd", { children: T.activityVersionId })
          ] }),
          Z.length > 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-slot-list", children: [
            /* @__PURE__ */ h.jsx("span", { children: "Embedded slots" }),
            Z.map((I) => /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => po(T, I.id, `${f.find((R) => R.id === T.nodeId)?.data.label ?? T.nodeId} / ${I.label}`), children: [
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
function Bm({ data: e, selected: t }) {
  const n = e, o = n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }];
  return /* @__PURE__ */ h.jsxs("div", { className: t ? "wf-node selected" : "wf-node", children: [
    n.acceptsInbound ? /* @__PURE__ */ h.jsx(It, { type: "target", position: J.Left }) : null,
    /* @__PURE__ */ h.jsx("strong", { children: n.label }),
    /* @__PURE__ */ h.jsx("small", { children: n.activityTypeKey ?? n.activityVersionId }),
    n.childSlots.length > 0 ? /* @__PURE__ */ h.jsxs("span", { children: [
      n.childSlots.length,
      " embedded slot",
      n.childSlots.length === 1 ? "" : "s"
    ] }) : null,
    o.map((r, i) => {
      const s = `${(i + 1) / (o.length + 1) * 100}%`;
      return /* @__PURE__ */ h.jsxs(ot.Fragment, { children: [
        /* @__PURE__ */ h.jsx("span", { className: "wf-node-port-label", style: { top: s }, children: r.displayName }),
        /* @__PURE__ */ h.jsx(It, { type: "source", position: J.Right, id: r.name, style: { top: s } })
      ] }, r.name);
    })
  ] });
}
function Fm(e) {
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
    labelStyle: d
  } = e, f = ot.useContext(Ba), [m, g] = ne(!1), [y, w, x] = zn({ sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: a }), S = f?.highlightedEdgeId === t;
  return /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
    /* @__PURE__ */ h.jsx(
      an,
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
        labelStyle: d,
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1)
      }
    ),
    f ? /* @__PURE__ */ h.jsx(sg, { children: /* @__PURE__ */ h.jsxs(
      "div",
      {
        className: ["wf-edge-actions", m ? "visible" : "", S ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${w}px, ${x}px)` },
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1),
        children: [
          /* @__PURE__ */ h.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (p) => f.requestInsertActivity(t, p.clientX, p.clientY), children: /* @__PURE__ */ h.jsx(yr, { size: 12 }) }),
          /* @__PURE__ */ h.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => f.deleteEdge(t), children: /* @__PURE__ */ h.jsx(Zo, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function Ym({ clientX: e, clientY: t, activities: n, onPick: o, onClose: r }) {
  const [i, s] = ne(""), [a, l] = ne(0), c = ae(null), u = ae(null), d = ye(() => {
    const S = i.trim().toLowerCase(), p = n.filter(Xa);
    return S ? p.filter((v) => Ae(v).toLowerCase().includes(S) || v.activityTypeKey.toLowerCase().includes(S) || (v.category ?? "").toLowerCase().includes(S) || (v.description ?? "").toLowerCase().includes(S)) : p;
  }, [n, i]), f = ye(() => Ya(d), [d]), m = ye(() => f.flatMap((S) => S.activities), [f]);
  ce(() => {
    requestAnimationFrame(() => u.current?.focus());
  }, []), ce(() => {
    const S = (v) => {
      c.current?.contains(v.target) || r();
    }, p = (v) => {
      v.key === "Escape" && r();
    };
    return document.addEventListener("mousedown", S, !0), document.addEventListener("keydown", p), () => {
      document.removeEventListener("mousedown", S, !0), document.removeEventListener("keydown", p);
    };
  }, [r]);
  const g = (S) => {
    if (S.key === "ArrowDown")
      S.preventDefault(), l((p) => Math.min(p + 1, m.length - 1));
    else if (S.key === "ArrowUp")
      S.preventDefault(), l((p) => Math.max(p - 1, 0));
    else if (S.key === "Enter") {
      S.preventDefault();
      const p = m[a];
      p && o(p);
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
        onKeyDown: g
      }
    ),
    /* @__PURE__ */ h.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: f.length === 0 ? /* @__PURE__ */ h.jsx("p", { children: "No matching activities." }) : f.map((S) => /* @__PURE__ */ h.jsxs("section", { children: [
      /* @__PURE__ */ h.jsx("h4", { children: S.category }),
      S.activities.map((p) => {
        x += 1;
        const v = x, N = v === a;
        return /* @__PURE__ */ h.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": N,
            className: N ? "active" : "",
            onMouseEnter: () => l(v),
            onClick: () => o(p),
            children: [
              /* @__PURE__ */ h.jsx("strong", { children: Ae(p) }),
              /* @__PURE__ */ h.jsx("small", { children: p.category || p.activityTypeKey })
            ]
          },
          p.activityVersionId
        );
      })
    ] }, S.category)) })
  ] });
}
function Xm({ draft: e }) {
  return e.validationErrors.length ? /* @__PURE__ */ h.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ h.jsx(Jt, { size: 14 }),
    e.validationErrors.length,
    " validation issue",
    e.validationErrors.length === 1 ? "" : "s"
  ] }) : /* @__PURE__ */ h.jsxs("div", { className: "wf-validation ok", children: [
    /* @__PURE__ */ h.jsx(Qn, { size: 14 }),
    " No validation errors"
  ] });
}
function Xi(e) {
  return `${Ae(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function Wi(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function Wm(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function qm(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function Ht(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function Ko(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
export {
  Um as register
};
