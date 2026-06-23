import ot, { memo as xe, forwardRef as Hn, useRef as ae, useEffect as ce, useCallback as he, useContext as Jt, useMemo as ye, useState as ne, createContext as Ko, useLayoutEffect as bc, createElement as Po } from "react";
import "@tanstack/react-query";
function Sc(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var mo = { exports: {} }, Tt = {};
var Ir;
function Ec() {
  if (Ir) return Tt;
  Ir = 1;
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
var Mr;
function _c() {
  return Mr || (Mr = 1, mo.exports = Ec()), mo.exports;
}
var h = _c();
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
function Vn() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new xn(n);
}
function xn(e) {
  this._ = e;
}
function Cc(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
xn.prototype = Vn.prototype = {
  constructor: xn,
  on: function(e, t) {
    var n = this._, o = Cc(e + "", n), r, i = -1, s = o.length;
    if (arguments.length < 2) {
      for (; ++i < s; ) if ((r = (e = o[i]).type) && (r = kc(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++i < s; )
      if (r = (e = o[i]).type) n[r] = Ar(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = Ar(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new xn(e);
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
function kc(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function Ar(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = Nc, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var $o = "http://www.w3.org/1999/xhtml";
const Dr = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: $o,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function On(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Dr.hasOwnProperty(t) ? { space: Dr[t], local: e } : e;
}
function Ic(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === $o && t.documentElement.namespaceURI === $o ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Mc(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function Xi(e) {
  var t = On(e);
  return (t.local ? Mc : Ic)(t);
}
function Ac() {
}
function Go(e) {
  return e == null ? Ac : function() {
    return this.querySelector(e);
  };
}
function Dc(e) {
  typeof e != "function" && (e = Go(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = new Array(s), l, c, d = 0; d < s; ++d)
      (l = i[d]) && (c = e.call(l, l.__data__, d, i)) && ("__data__" in l && (c.__data__ = l.__data__), a[d] = c);
  return new De(o, this._parents);
}
function jc(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function Pc() {
  return [];
}
function Wi(e) {
  return e == null ? Pc : function() {
    return this.querySelectorAll(e);
  };
}
function $c(e) {
  return function() {
    return jc(e.apply(this, arguments));
  };
}
function Tc(e) {
  typeof e == "function" ? e = $c(e) : e = Wi(e);
  for (var t = this._groups, n = t.length, o = [], r = [], i = 0; i < n; ++i)
    for (var s = t[i], a = s.length, l, c = 0; c < a; ++c)
      (l = s[c]) && (o.push(e.call(l, l.__data__, c, s)), r.push(l));
  return new De(o, r);
}
function qi(e) {
  return function() {
    return this.matches(e);
  };
}
function Zi(e) {
  return function(t) {
    return t.matches(e);
  };
}
var Rc = Array.prototype.find;
function zc(e) {
  return function() {
    return Rc.call(this.children, e);
  };
}
function Lc() {
  return this.firstElementChild;
}
function Hc(e) {
  return this.select(e == null ? Lc : zc(typeof e == "function" ? e : Zi(e)));
}
var Vc = Array.prototype.filter;
function Oc() {
  return Array.from(this.children);
}
function Bc(e) {
  return function() {
    return Vc.call(this.children, e);
  };
}
function Fc(e) {
  return this.selectAll(e == null ? Oc : Bc(typeof e == "function" ? e : Zi(e)));
}
function Yc(e) {
  typeof e != "function" && (e = qi(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = [], l, c = 0; c < s; ++c)
      (l = i[c]) && e.call(l, l.__data__, c, i) && a.push(l);
  return new De(o, this._parents);
}
function Ui(e) {
  return new Array(e.length);
}
function Xc() {
  return new De(this._enter || this._groups.map(Ui), this._parents);
}
function Nn(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
Nn.prototype = {
  constructor: Nn,
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
function Wc(e) {
  return function() {
    return e;
  };
}
function qc(e, t, n, o, r, i) {
  for (var s = 0, a, l = t.length, c = i.length; s < c; ++s)
    (a = t[s]) ? (a.__data__ = i[s], o[s] = a) : n[s] = new Nn(e, i[s]);
  for (; s < l; ++s)
    (a = t[s]) && (r[s] = a);
}
function Zc(e, t, n, o, r, i, s) {
  var a, l, c = /* @__PURE__ */ new Map(), d = t.length, f = i.length, u = new Array(d), m;
  for (a = 0; a < d; ++a)
    (l = t[a]) && (u[a] = m = s.call(l, l.__data__, a, t) + "", c.has(m) ? r[a] = l : c.set(m, l));
  for (a = 0; a < f; ++a)
    m = s.call(e, i[a], a, i) + "", (l = c.get(m)) ? (o[a] = l, l.__data__ = i[a], c.delete(m)) : n[a] = new Nn(e, i[a]);
  for (a = 0; a < d; ++a)
    (l = t[a]) && c.get(u[a]) === l && (r[a] = l);
}
function Uc(e) {
  return e.__data__;
}
function Kc(e, t) {
  if (!arguments.length) return Array.from(this, Uc);
  var n = t ? Zc : qc, o = this._parents, r = this._groups;
  typeof e != "function" && (e = Wc(e));
  for (var i = r.length, s = new Array(i), a = new Array(i), l = new Array(i), c = 0; c < i; ++c) {
    var d = o[c], f = r[c], u = f.length, m = Gc(e.call(d, d && d.__data__, c, o)), p = m.length, y = a[c] = new Array(p), w = s[c] = new Array(p), x = l[c] = new Array(u);
    n(d, f, y, w, x, m, t);
    for (var S = 0, g = 0, v, _; S < p; ++S)
      if (v = y[S]) {
        for (S >= g && (g = S + 1); !(_ = w[g]) && ++g < p; ) ;
        v._next = _ || null;
      }
  }
  return s = new De(s, o), s._enter = a, s._exit = l, s;
}
function Gc(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function Qc() {
  return new De(this._exit || this._groups.map(Ui), this._parents);
}
function Jc(e, t, n) {
  var o = this.enter(), r = this, i = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? i.remove() : n(i), o && r ? o.merge(r).order() : r;
}
function el(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, i = o.length, s = Math.min(r, i), a = new Array(r), l = 0; l < s; ++l)
    for (var c = n[l], d = o[l], f = c.length, u = a[l] = new Array(f), m, p = 0; p < f; ++p)
      (m = c[p] || d[p]) && (u[p] = m);
  for (; l < r; ++l)
    a[l] = n[l];
  return new De(a, this._parents);
}
function tl() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, i = o[r], s; --r >= 0; )
      (s = o[r]) && (i && s.compareDocumentPosition(i) ^ 4 && i.parentNode.insertBefore(s, i), i = s);
  return this;
}
function nl(e) {
  e || (e = ol);
  function t(f, u) {
    return f && u ? e(f.__data__, u.__data__) : !f - !u;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), i = 0; i < o; ++i) {
    for (var s = n[i], a = s.length, l = r[i] = new Array(a), c, d = 0; d < a; ++d)
      (c = s[d]) && (l[d] = c);
    l.sort(t);
  }
  return new De(r, this._parents).order();
}
function ol(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function rl() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function il() {
  return Array.from(this);
}
function sl() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length; r < i; ++r) {
      var s = o[r];
      if (s) return s;
    }
  return null;
}
function al() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function cl() {
  return !this.node();
}
function ll(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], i = 0, s = r.length, a; i < s; ++i)
      (a = r[i]) && e.call(a, a.__data__, i, r);
  return this;
}
function ul(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function dl(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function fl(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function hl(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function pl(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function gl(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function ml(e, t) {
  var n = On(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? dl : ul : typeof t == "function" ? n.local ? gl : pl : n.local ? hl : fl)(n, t));
}
function Ki(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function yl(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function xl(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function wl(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function vl(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? yl : typeof t == "function" ? wl : xl)(e, t, n ?? "")) : vt(this.node(), e);
}
function vt(e, t) {
  return e.style.getPropertyValue(t) || Ki(e).getComputedStyle(e, null).getPropertyValue(t);
}
function bl(e) {
  return function() {
    delete this[e];
  };
}
function Sl(e, t) {
  return function() {
    this[e] = t;
  };
}
function El(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function _l(e, t) {
  return arguments.length > 1 ? this.each((t == null ? bl : typeof t == "function" ? El : Sl)(e, t)) : this.node()[e];
}
function Gi(e) {
  return e.trim().split(/^|\s+/);
}
function Qo(e) {
  return e.classList || new Qi(e);
}
function Qi(e) {
  this._node = e, this._names = Gi(e.getAttribute("class") || "");
}
Qi.prototype = {
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
function Ji(e, t) {
  for (var n = Qo(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function es(e, t) {
  for (var n = Qo(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function Nl(e) {
  return function() {
    Ji(this, e);
  };
}
function Cl(e) {
  return function() {
    es(this, e);
  };
}
function kl(e, t) {
  return function() {
    (t.apply(this, arguments) ? Ji : es)(this, e);
  };
}
function Il(e, t) {
  var n = Gi(e + "");
  if (arguments.length < 2) {
    for (var o = Qo(this.node()), r = -1, i = n.length; ++r < i; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? kl : t ? Nl : Cl)(n, t));
}
function Ml() {
  this.textContent = "";
}
function Al(e) {
  return function() {
    this.textContent = e;
  };
}
function Dl(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function jl(e) {
  return arguments.length ? this.each(e == null ? Ml : (typeof e == "function" ? Dl : Al)(e)) : this.node().textContent;
}
function Pl() {
  this.innerHTML = "";
}
function $l(e) {
  return function() {
    this.innerHTML = e;
  };
}
function Tl(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function Rl(e) {
  return arguments.length ? this.each(e == null ? Pl : (typeof e == "function" ? Tl : $l)(e)) : this.node().innerHTML;
}
function zl() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function Ll() {
  return this.each(zl);
}
function Hl() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Vl() {
  return this.each(Hl);
}
function Ol(e) {
  var t = typeof e == "function" ? e : Xi(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Bl() {
  return null;
}
function Fl(e, t) {
  var n = typeof e == "function" ? e : Xi(e), o = t == null ? Bl : typeof t == "function" ? t : Go(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function Yl() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Xl() {
  return this.each(Yl);
}
function Wl() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function ql() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Zl(e) {
  return this.select(e ? ql : Wl);
}
function Ul(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function Kl(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function Gl(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function Ql(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, i; n < r; ++n)
        i = t[n], (!e.type || i.type === e.type) && i.name === e.name ? this.removeEventListener(i.type, i.listener, i.options) : t[++o] = i;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function Jl(e, t, n) {
  return function() {
    var o = this.__on, r, i = Kl(t);
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
function eu(e, t, n) {
  var o = Gl(e + ""), r, i = o.length, s;
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
  for (a = t ? Jl : Ql, r = 0; r < i; ++r) this.each(a(o[r], t, n));
  return this;
}
function ts(e, t, n) {
  var o = Ki(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function tu(e, t) {
  return function() {
    return ts(this, e, t);
  };
}
function nu(e, t) {
  return function() {
    return ts(this, e, t.apply(this, arguments));
  };
}
function ou(e, t) {
  return this.each((typeof t == "function" ? nu : tu)(e, t));
}
function* ru() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length, s; r < i; ++r)
      (s = o[r]) && (yield s);
}
var ns = [null];
function De(e, t) {
  this._groups = e, this._parents = t;
}
function en() {
  return new De([[document.documentElement]], ns);
}
function iu() {
  return this;
}
De.prototype = en.prototype = {
  constructor: De,
  select: Dc,
  selectAll: Tc,
  selectChild: Hc,
  selectChildren: Fc,
  filter: Yc,
  data: Kc,
  enter: Xc,
  exit: Qc,
  join: Jc,
  merge: el,
  selection: iu,
  order: tl,
  sort: nl,
  call: rl,
  nodes: il,
  node: sl,
  size: al,
  empty: cl,
  each: ll,
  attr: ml,
  style: vl,
  property: _l,
  classed: Il,
  text: jl,
  html: Rl,
  raise: Ll,
  lower: Vl,
  append: Ol,
  insert: Fl,
  remove: Xl,
  clone: Zl,
  datum: Ul,
  on: eu,
  dispatch: ou,
  [Symbol.iterator]: ru
};
function Ae(e) {
  return typeof e == "string" ? new De([[document.querySelector(e)]], [document.documentElement]) : new De([[e]], ns);
}
function su(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Re(e, t) {
  if (e = su(e), t === void 0 && (t = e.currentTarget), t) {
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
const au = { passive: !1 }, Ft = { capture: !0, passive: !1 };
function yo(e) {
  e.stopImmediatePropagation();
}
function xt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function os(e) {
  var t = e.document.documentElement, n = Ae(e).on("dragstart.drag", xt, Ft);
  "onselectstart" in t ? n.on("selectstart.drag", xt, Ft) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function rs(e, t) {
  var n = e.document.documentElement, o = Ae(e).on("dragstart.drag", null);
  t && (o.on("click.drag", xt, Ft), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const ln = (e) => () => e;
function To(e, {
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
To.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function cu(e) {
  return !e.ctrlKey && !e.button;
}
function lu() {
  return this.parentNode;
}
function uu(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function du() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function is() {
  var e = cu, t = lu, n = uu, o = du, r = {}, i = Vn("start", "drag", "end"), s = 0, a, l, c, d, f = 0;
  function u(v) {
    v.on("mousedown.drag", m).filter(o).on("touchstart.drag", w).on("touchmove.drag", x, au).on("touchend.drag touchcancel.drag", S).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function m(v, _) {
    if (!(d || !e.call(this, v, _))) {
      var E = g(this, t.call(this, v, _), v, _, "mouse");
      E && (Ae(v.view).on("mousemove.drag", p, Ft).on("mouseup.drag", y, Ft), os(v.view), yo(v), c = !1, a = v.clientX, l = v.clientY, E("start", v));
    }
  }
  function p(v) {
    if (xt(v), !c) {
      var _ = v.clientX - a, E = v.clientY - l;
      c = _ * _ + E * E > f;
    }
    r.mouse("drag", v);
  }
  function y(v) {
    Ae(v.view).on("mousemove.drag mouseup.drag", null), rs(v.view, c), xt(v), r.mouse("end", v);
  }
  function w(v, _) {
    if (e.call(this, v, _)) {
      var E = v.changedTouches, M = t.call(this, v, _), D = E.length, j, F;
      for (j = 0; j < D; ++j)
        (F = g(this, M, v, _, E[j].identifier, E[j])) && (yo(v), F("start", v, E[j]));
    }
  }
  function x(v) {
    var _ = v.changedTouches, E = _.length, M, D;
    for (M = 0; M < E; ++M)
      (D = r[_[M].identifier]) && (xt(v), D("drag", v, _[M]));
  }
  function S(v) {
    var _ = v.changedTouches, E = _.length, M, D;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), M = 0; M < E; ++M)
      (D = r[_[M].identifier]) && (yo(v), D("end", v, _[M]));
  }
  function g(v, _, E, M, D, j) {
    var F = i.copy(), C = Re(j || E, _), R, H, b;
    if ((b = n.call(v, new To("beforestart", {
      sourceEvent: E,
      target: u,
      identifier: D,
      active: s,
      x: C[0],
      y: C[1],
      dx: 0,
      dy: 0,
      dispatch: F
    }), M)) != null)
      return R = b.x - C[0] || 0, H = b.y - C[1] || 0, function k(N, A, $) {
        var P = C, B;
        switch (N) {
          case "start":
            r[D] = k, B = s++;
            break;
          case "end":
            delete r[D], --s;
          // falls through
          case "drag":
            C = Re($ || A, _), B = s;
            break;
        }
        F.call(
          N,
          v,
          new To(N, {
            sourceEvent: A,
            subject: b,
            target: u,
            identifier: D,
            active: B,
            x: C[0] + R,
            y: C[1] + H,
            dx: C[0] - P[0],
            dy: C[1] - P[1],
            dispatch: F
          }),
          M
        );
      };
  }
  return u.filter = function(v) {
    return arguments.length ? (e = typeof v == "function" ? v : ln(!!v), u) : e;
  }, u.container = function(v) {
    return arguments.length ? (t = typeof v == "function" ? v : ln(v), u) : t;
  }, u.subject = function(v) {
    return arguments.length ? (n = typeof v == "function" ? v : ln(v), u) : n;
  }, u.touchable = function(v) {
    return arguments.length ? (o = typeof v == "function" ? v : ln(!!v), u) : o;
  }, u.on = function() {
    var v = i.on.apply(i, arguments);
    return v === i ? u : v;
  }, u.clickDistance = function(v) {
    return arguments.length ? (f = (v = +v) * v, u) : Math.sqrt(f);
  }, u;
}
function Jo(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function ss(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function tn() {
}
var Yt = 0.7, Cn = 1 / Yt, wt = "\\s*([+-]?\\d+)\\s*", Xt = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", Fe = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", fu = /^#([0-9a-f]{3,8})$/, hu = new RegExp(`^rgb\\(${wt},${wt},${wt}\\)$`), pu = new RegExp(`^rgb\\(${Fe},${Fe},${Fe}\\)$`), gu = new RegExp(`^rgba\\(${wt},${wt},${wt},${Xt}\\)$`), mu = new RegExp(`^rgba\\(${Fe},${Fe},${Fe},${Xt}\\)$`), yu = new RegExp(`^hsl\\(${Xt},${Fe},${Fe}\\)$`), xu = new RegExp(`^hsla\\(${Xt},${Fe},${Fe},${Xt}\\)$`), jr = {
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
Jo(tn, ut, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Pr,
  // Deprecated! Use color.formatHex.
  formatHex: Pr,
  formatHex8: wu,
  formatHsl: vu,
  formatRgb: $r,
  toString: $r
});
function Pr() {
  return this.rgb().formatHex();
}
function wu() {
  return this.rgb().formatHex8();
}
function vu() {
  return as(this).formatHsl();
}
function $r() {
  return this.rgb().formatRgb();
}
function ut(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = fu.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? Tr(t) : n === 3 ? new ke(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? un(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? un(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = hu.exec(e)) ? new ke(t[1], t[2], t[3], 1) : (t = pu.exec(e)) ? new ke(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = gu.exec(e)) ? un(t[1], t[2], t[3], t[4]) : (t = mu.exec(e)) ? un(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = yu.exec(e)) ? Lr(t[1], t[2] / 100, t[3] / 100, 1) : (t = xu.exec(e)) ? Lr(t[1], t[2] / 100, t[3] / 100, t[4]) : jr.hasOwnProperty(e) ? Tr(jr[e]) : e === "transparent" ? new ke(NaN, NaN, NaN, 0) : null;
}
function Tr(e) {
  return new ke(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function un(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new ke(e, t, n, o);
}
function bu(e) {
  return e instanceof tn || (e = ut(e)), e ? (e = e.rgb(), new ke(e.r, e.g, e.b, e.opacity)) : new ke();
}
function Ro(e, t, n, o) {
  return arguments.length === 1 ? bu(e) : new ke(e, t, n, o ?? 1);
}
function ke(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
Jo(ke, Ro, ss(tn, {
  brighter(e) {
    return e = e == null ? Cn : Math.pow(Cn, e), new ke(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Yt : Math.pow(Yt, e), new ke(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new ke(ct(this.r), ct(this.g), ct(this.b), kn(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: Rr,
  // Deprecated! Use color.formatHex.
  formatHex: Rr,
  formatHex8: Su,
  formatRgb: zr,
  toString: zr
}));
function Rr() {
  return `#${at(this.r)}${at(this.g)}${at(this.b)}`;
}
function Su() {
  return `#${at(this.r)}${at(this.g)}${at(this.b)}${at((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function zr() {
  const e = kn(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${ct(this.r)}, ${ct(this.g)}, ${ct(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function kn(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function ct(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function at(e) {
  return e = ct(e), (e < 16 ? "0" : "") + e.toString(16);
}
function Lr(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new ze(e, t, n, o);
}
function as(e) {
  if (e instanceof ze) return new ze(e.h, e.s, e.l, e.opacity);
  if (e instanceof tn || (e = ut(e)), !e) return new ze();
  if (e instanceof ze) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), i = Math.max(t, n, o), s = NaN, a = i - r, l = (i + r) / 2;
  return a ? (t === i ? s = (n - o) / a + (n < o) * 6 : n === i ? s = (o - t) / a + 2 : s = (t - n) / a + 4, a /= l < 0.5 ? i + r : 2 - i - r, s *= 60) : a = l > 0 && l < 1 ? 0 : s, new ze(s, a, l, e.opacity);
}
function Eu(e, t, n, o) {
  return arguments.length === 1 ? as(e) : new ze(e, t, n, o ?? 1);
}
function ze(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
Jo(ze, Eu, ss(tn, {
  brighter(e) {
    return e = e == null ? Cn : Math.pow(Cn, e), new ze(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Yt : Math.pow(Yt, e), new ze(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new ke(
      xo(e >= 240 ? e - 240 : e + 120, r, o),
      xo(e, r, o),
      xo(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new ze(Hr(this.h), dn(this.s), dn(this.l), kn(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = kn(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${Hr(this.h)}, ${dn(this.s) * 100}%, ${dn(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function Hr(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function dn(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function xo(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const er = (e) => () => e;
function _u(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function Nu(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function Cu(e) {
  return (e = +e) == 1 ? cs : function(t, n) {
    return n - t ? Nu(t, n, e) : er(isNaN(t) ? n : t);
  };
}
function cs(e, t) {
  var n = t - e;
  return n ? _u(e, n) : er(isNaN(e) ? t : e);
}
const In = (function e(t) {
  var n = Cu(t);
  function o(r, i) {
    var s = n((r = Ro(r)).r, (i = Ro(i)).r), a = n(r.g, i.g), l = n(r.b, i.b), c = cs(r.opacity, i.opacity);
    return function(d) {
      return r.r = s(d), r.g = a(d), r.b = l(d), r.opacity = c(d), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function ku(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(i) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - i) + t[r] * i;
    return o;
  };
}
function Iu(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Mu(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), i = new Array(n), s;
  for (s = 0; s < o; ++s) r[s] = Ot(e[s], t[s]);
  for (; s < n; ++s) i[s] = t[s];
  return function(a) {
    for (s = 0; s < o; ++s) i[s] = r[s](a);
    return i;
  };
}
function Au(e, t) {
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
function Du(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = Ot(e[r], t[r]) : o[r] = t[r];
  return function(i) {
    for (r in n) o[r] = n[r](i);
    return o;
  };
}
var zo = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, wo = new RegExp(zo.source, "g");
function ju(e) {
  return function() {
    return e;
  };
}
function Pu(e) {
  return function(t) {
    return e(t) + "";
  };
}
function ls(e, t) {
  var n = zo.lastIndex = wo.lastIndex = 0, o, r, i, s = -1, a = [], l = [];
  for (e = e + "", t = t + ""; (o = zo.exec(e)) && (r = wo.exec(t)); )
    (i = r.index) > n && (i = t.slice(n, i), a[s] ? a[s] += i : a[++s] = i), (o = o[0]) === (r = r[0]) ? a[s] ? a[s] += r : a[++s] = r : (a[++s] = null, l.push({ i: s, x: Be(o, r) })), n = wo.lastIndex;
  return n < t.length && (i = t.slice(n), a[s] ? a[s] += i : a[++s] = i), a.length < 2 ? l[0] ? Pu(l[0].x) : ju(t) : (t = l.length, function(c) {
    for (var d = 0, f; d < t; ++d) a[(f = l[d]).i] = f.x(c);
    return a.join("");
  });
}
function Ot(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? er(t) : (n === "number" ? Be : n === "string" ? (o = ut(t)) ? (t = o, In) : ls : t instanceof ut ? In : t instanceof Date ? Au : Iu(t) ? ku : Array.isArray(t) ? Mu : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Du : Be)(e, t);
}
var Vr = 180 / Math.PI, Lo = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function us(e, t, n, o, r, i) {
  var s, a, l;
  return (s = Math.sqrt(e * e + t * t)) && (e /= s, t /= s), (l = e * n + t * o) && (n -= e * l, o -= t * l), (a = Math.sqrt(n * n + o * o)) && (n /= a, o /= a, l /= a), e * o < t * n && (e = -e, t = -t, l = -l, s = -s), {
    translateX: r,
    translateY: i,
    rotate: Math.atan2(t, e) * Vr,
    skewX: Math.atan(l) * Vr,
    scaleX: s,
    scaleY: a
  };
}
var fn;
function $u(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Lo : us(t.a, t.b, t.c, t.d, t.e, t.f);
}
function Tu(e) {
  return e == null || (fn || (fn = document.createElementNS("http://www.w3.org/2000/svg", "g")), fn.setAttribute("transform", e), !(e = fn.transform.baseVal.consolidate())) ? Lo : (e = e.matrix, us(e.a, e.b, e.c, e.d, e.e, e.f));
}
function ds(e, t, n, o) {
  function r(c) {
    return c.length ? c.pop() + " " : "";
  }
  function i(c, d, f, u, m, p) {
    if (c !== f || d !== u) {
      var y = m.push("translate(", null, t, null, n);
      p.push({ i: y - 4, x: Be(c, f) }, { i: y - 2, x: Be(d, u) });
    } else (f || u) && m.push("translate(" + f + t + u + n);
  }
  function s(c, d, f, u) {
    c !== d ? (c - d > 180 ? d += 360 : d - c > 180 && (c += 360), u.push({ i: f.push(r(f) + "rotate(", null, o) - 2, x: Be(c, d) })) : d && f.push(r(f) + "rotate(" + d + o);
  }
  function a(c, d, f, u) {
    c !== d ? u.push({ i: f.push(r(f) + "skewX(", null, o) - 2, x: Be(c, d) }) : d && f.push(r(f) + "skewX(" + d + o);
  }
  function l(c, d, f, u, m, p) {
    if (c !== f || d !== u) {
      var y = m.push(r(m) + "scale(", null, ",", null, ")");
      p.push({ i: y - 4, x: Be(c, f) }, { i: y - 2, x: Be(d, u) });
    } else (f !== 1 || u !== 1) && m.push(r(m) + "scale(" + f + "," + u + ")");
  }
  return function(c, d) {
    var f = [], u = [];
    return c = e(c), d = e(d), i(c.translateX, c.translateY, d.translateX, d.translateY, f, u), s(c.rotate, d.rotate, f, u), a(c.skewX, d.skewX, f, u), l(c.scaleX, c.scaleY, d.scaleX, d.scaleY, f, u), c = d = null, function(m) {
      for (var p = -1, y = u.length, w; ++p < y; ) f[(w = u[p]).i] = w.x(m);
      return f.join("");
    };
  };
}
var Ru = ds($u, "px, ", "px)", "deg)"), zu = ds(Tu, ", ", ")", ")"), Lu = 1e-12;
function Or(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Hu(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Vu(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const wn = (function e(t, n, o) {
  function r(i, s) {
    var a = i[0], l = i[1], c = i[2], d = s[0], f = s[1], u = s[2], m = d - a, p = f - l, y = m * m + p * p, w, x;
    if (y < Lu)
      x = Math.log(u / c) / t, w = function(M) {
        return [
          a + M * m,
          l + M * p,
          c * Math.exp(t * M * x)
        ];
      };
    else {
      var S = Math.sqrt(y), g = (u * u - c * c + o * y) / (2 * c * n * S), v = (u * u - c * c - o * y) / (2 * u * n * S), _ = Math.log(Math.sqrt(g * g + 1) - g), E = Math.log(Math.sqrt(v * v + 1) - v);
      x = (E - _) / t, w = function(M) {
        var D = M * x, j = Or(_), F = c / (n * S) * (j * Vu(t * D + _) - Hu(_));
        return [
          a + F * m,
          l + F * p,
          c * j / Or(t * D + _)
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
var bt = 0, Ht = 0, Rt = 0, fs = 1e3, Mn, Vt, An = 0, dt = 0, Bn = 0, Wt = typeof performance == "object" && performance.now ? performance : Date, hs = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function tr() {
  return dt || (hs(Ou), dt = Wt.now() + Bn);
}
function Ou() {
  dt = 0;
}
function Dn() {
  this._call = this._time = this._next = null;
}
Dn.prototype = ps.prototype = {
  constructor: Dn,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? tr() : +n) + (t == null ? 0 : +t), !this._next && Vt !== this && (Vt ? Vt._next = this : Mn = this, Vt = this), this._call = e, this._time = n, Ho();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Ho());
  }
};
function ps(e, t, n) {
  var o = new Dn();
  return o.restart(e, t, n), o;
}
function Bu() {
  tr(), ++bt;
  for (var e = Mn, t; e; )
    (t = dt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --bt;
}
function Br() {
  dt = (An = Wt.now()) + Bn, bt = Ht = 0;
  try {
    Bu();
  } finally {
    bt = 0, Yu(), dt = 0;
  }
}
function Fu() {
  var e = Wt.now(), t = e - An;
  t > fs && (Bn -= t, An = e);
}
function Yu() {
  for (var e, t = Mn, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Mn = n);
  Vt = e, Ho(o);
}
function Ho(e) {
  if (!bt) {
    Ht && (Ht = clearTimeout(Ht));
    var t = e - dt;
    t > 24 ? (e < 1 / 0 && (Ht = setTimeout(Br, e - Wt.now() - Bn)), Rt && (Rt = clearInterval(Rt))) : (Rt || (An = Wt.now(), Rt = setInterval(Fu, fs)), bt = 1, hs(Br));
  }
}
function Fr(e, t, n) {
  var o = new Dn();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var Xu = Vn("start", "end", "cancel", "interrupt"), Wu = [], gs = 0, Yr = 1, Vo = 2, vn = 3, Xr = 4, Oo = 5, bn = 6;
function Fn(e, t, n, o, r, i) {
  var s = e.__transition;
  if (!s) e.__transition = {};
  else if (n in s) return;
  qu(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: Xu,
    tween: Wu,
    time: i.time,
    delay: i.delay,
    duration: i.duration,
    ease: i.ease,
    timer: null,
    state: gs
  });
}
function nr(e, t) {
  var n = Ve(e, t);
  if (n.state > gs) throw new Error("too late; already scheduled");
  return n;
}
function Xe(e, t) {
  var n = Ve(e, t);
  if (n.state > vn) throw new Error("too late; already running");
  return n;
}
function Ve(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function qu(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = ps(i, 0, n.time);
  function i(c) {
    n.state = Yr, n.timer.restart(s, n.delay, n.time), n.delay <= c && s(c - n.delay);
  }
  function s(c) {
    var d, f, u, m;
    if (n.state !== Yr) return l();
    for (d in o)
      if (m = o[d], m.name === n.name) {
        if (m.state === vn) return Fr(s);
        m.state === Xr ? (m.state = bn, m.timer.stop(), m.on.call("interrupt", e, e.__data__, m.index, m.group), delete o[d]) : +d < t && (m.state = bn, m.timer.stop(), m.on.call("cancel", e, e.__data__, m.index, m.group), delete o[d]);
      }
    if (Fr(function() {
      n.state === vn && (n.state = Xr, n.timer.restart(a, n.delay, n.time), a(c));
    }), n.state = Vo, n.on.call("start", e, e.__data__, n.index, n.group), n.state === Vo) {
      for (n.state = vn, r = new Array(u = n.tween.length), d = 0, f = -1; d < u; ++d)
        (m = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (r[++f] = m);
      r.length = f + 1;
    }
  }
  function a(c) {
    for (var d = c < n.duration ? n.ease.call(null, c / n.duration) : (n.timer.restart(l), n.state = Oo, 1), f = -1, u = r.length; ++f < u; )
      r[f].call(e, d);
    n.state === Oo && (n.on.call("end", e, e.__data__, n.index, n.group), l());
  }
  function l() {
    n.state = bn, n.timer.stop(), delete o[t];
    for (var c in o) return;
    delete e.__transition;
  }
}
function Sn(e, t) {
  var n = e.__transition, o, r, i = !0, s;
  if (n) {
    t = t == null ? null : t + "";
    for (s in n) {
      if ((o = n[s]).name !== t) {
        i = !1;
        continue;
      }
      r = o.state > Vo && o.state < Oo, o.state = bn, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[s];
    }
    i && delete e.__transition;
  }
}
function Zu(e) {
  return this.each(function() {
    Sn(this, e);
  });
}
function Uu(e, t) {
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
function Ku(e, t, n) {
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
function Gu(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = Ve(this.node(), n).tween, r = 0, i = o.length, s; r < i; ++r)
      if ((s = o[r]).name === e)
        return s.value;
    return null;
  }
  return this.each((t == null ? Uu : Ku)(n, e, t));
}
function or(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = Xe(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return Ve(r, o).value[t];
  };
}
function ms(e, t) {
  var n;
  return (typeof t == "number" ? Be : t instanceof ut ? In : (n = ut(t)) ? (t = n, In) : ls)(e, t);
}
function Qu(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Ju(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function ed(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttribute(e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function td(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttributeNS(e.space, e.local);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function nd(e, t, n) {
  var o, r, i;
  return function() {
    var s, a = n(this), l;
    return a == null ? void this.removeAttribute(e) : (s = this.getAttribute(e), l = a + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a)));
  };
}
function od(e, t, n) {
  var o, r, i;
  return function() {
    var s, a = n(this), l;
    return a == null ? void this.removeAttributeNS(e.space, e.local) : (s = this.getAttributeNS(e.space, e.local), l = a + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a)));
  };
}
function rd(e, t) {
  var n = On(e), o = n === "transform" ? zu : ms;
  return this.attrTween(e, typeof t == "function" ? (n.local ? od : nd)(n, o, or(this, "attr." + e, t)) : t == null ? (n.local ? Ju : Qu)(n) : (n.local ? td : ed)(n, o, t));
}
function id(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function sd(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function ad(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && sd(e, i)), n;
  }
  return r._value = t, r;
}
function cd(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && id(e, i)), n;
  }
  return r._value = t, r;
}
function ld(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = On(e);
  return this.tween(n, (o.local ? ad : cd)(o, t));
}
function ud(e, t) {
  return function() {
    nr(this, e).delay = +t.apply(this, arguments);
  };
}
function dd(e, t) {
  return t = +t, function() {
    nr(this, e).delay = t;
  };
}
function fd(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? ud : dd)(t, e)) : Ve(this.node(), t).delay;
}
function hd(e, t) {
  return function() {
    Xe(this, e).duration = +t.apply(this, arguments);
  };
}
function pd(e, t) {
  return t = +t, function() {
    Xe(this, e).duration = t;
  };
}
function gd(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? hd : pd)(t, e)) : Ve(this.node(), t).duration;
}
function md(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    Xe(this, e).ease = t;
  };
}
function yd(e) {
  var t = this._id;
  return arguments.length ? this.each(md(t, e)) : Ve(this.node(), t).ease;
}
function xd(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    Xe(this, e).ease = n;
  };
}
function wd(e) {
  if (typeof e != "function") throw new Error();
  return this.each(xd(this._id, e));
}
function vd(e) {
  typeof e != "function" && (e = qi(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = [], l, c = 0; c < s; ++c)
      (l = i[c]) && e.call(l, l.__data__, c, i) && a.push(l);
  return new Ge(o, this._parents, this._name, this._id);
}
function bd(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, i = Math.min(o, r), s = new Array(o), a = 0; a < i; ++a)
    for (var l = t[a], c = n[a], d = l.length, f = s[a] = new Array(d), u, m = 0; m < d; ++m)
      (u = l[m] || c[m]) && (f[m] = u);
  for (; a < o; ++a)
    s[a] = t[a];
  return new Ge(s, this._parents, this._name, this._id);
}
function Sd(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function Ed(e, t, n) {
  var o, r, i = Sd(t) ? nr : Xe;
  return function() {
    var s = i(this, e), a = s.on;
    a !== o && (r = (o = a).copy()).on(t, n), s.on = r;
  };
}
function _d(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Ve(this.node(), n).on.on(e) : this.each(Ed(n, e, t));
}
function Nd(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function Cd() {
  return this.on("end.remove", Nd(this._id));
}
function kd(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Go(e));
  for (var o = this._groups, r = o.length, i = new Array(r), s = 0; s < r; ++s)
    for (var a = o[s], l = a.length, c = i[s] = new Array(l), d, f, u = 0; u < l; ++u)
      (d = a[u]) && (f = e.call(d, d.__data__, u, a)) && ("__data__" in d && (f.__data__ = d.__data__), c[u] = f, Fn(c[u], t, n, u, c, Ve(d, n)));
  return new Ge(i, this._parents, t, n);
}
function Id(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Wi(e));
  for (var o = this._groups, r = o.length, i = [], s = [], a = 0; a < r; ++a)
    for (var l = o[a], c = l.length, d, f = 0; f < c; ++f)
      if (d = l[f]) {
        for (var u = e.call(d, d.__data__, f, l), m, p = Ve(d, n), y = 0, w = u.length; y < w; ++y)
          (m = u[y]) && Fn(m, t, n, y, u, p);
        i.push(u), s.push(d);
      }
  return new Ge(i, s, t, n);
}
var Md = en.prototype.constructor;
function Ad() {
  return new Md(this._groups, this._parents);
}
function Dd(e, t) {
  var n, o, r;
  return function() {
    var i = vt(this, e), s = (this.style.removeProperty(e), vt(this, e));
    return i === s ? null : i === n && s === o ? r : r = t(n = i, o = s);
  };
}
function ys(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function jd(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = vt(this, e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function Pd(e, t, n) {
  var o, r, i;
  return function() {
    var s = vt(this, e), a = n(this), l = a + "";
    return a == null && (l = a = (this.style.removeProperty(e), vt(this, e))), s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a));
  };
}
function $d(e, t) {
  var n, o, r, i = "style." + t, s = "end." + i, a;
  return function() {
    var l = Xe(this, e), c = l.on, d = l.value[i] == null ? a || (a = ys(t)) : void 0;
    (c !== n || r !== d) && (o = (n = c).copy()).on(s, r = d), l.on = o;
  };
}
function Td(e, t, n) {
  var o = (e += "") == "transform" ? Ru : ms;
  return t == null ? this.styleTween(e, Dd(e, o)).on("end.style." + e, ys(e)) : typeof t == "function" ? this.styleTween(e, Pd(e, o, or(this, "style." + e, t))).each($d(this._id, e)) : this.styleTween(e, jd(e, o, t), n).on("end.style." + e, null);
}
function Rd(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function zd(e, t, n) {
  var o, r;
  function i() {
    var s = t.apply(this, arguments);
    return s !== r && (o = (r = s) && Rd(e, s, n)), o;
  }
  return i._value = t, i;
}
function Ld(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, zd(e, t, n ?? ""));
}
function Hd(e) {
  return function() {
    this.textContent = e;
  };
}
function Vd(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function Od(e) {
  return this.tween("text", typeof e == "function" ? Vd(or(this, "text", e)) : Hd(e == null ? "" : e + ""));
}
function Bd(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Fd(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Bd(r)), t;
  }
  return o._value = e, o;
}
function Yd(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, Fd(e));
}
function Xd() {
  for (var e = this._name, t = this._id, n = xs(), o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], a = s.length, l, c = 0; c < a; ++c)
      if (l = s[c]) {
        var d = Ve(l, t);
        Fn(l, e, n, c, s, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new Ge(o, this._parents, e, n);
}
function Wd() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(i, s) {
    var a = { value: s }, l = { value: function() {
      --r === 0 && i();
    } };
    n.each(function() {
      var c = Xe(this, o), d = c.on;
      d !== e && (t = (e = d).copy(), t._.cancel.push(a), t._.interrupt.push(a), t._.end.push(l)), c.on = t;
    }), r === 0 && i();
  });
}
var qd = 0;
function Ge(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function xs() {
  return ++qd;
}
var Ue = en.prototype;
Ge.prototype = {
  constructor: Ge,
  select: kd,
  selectAll: Id,
  selectChild: Ue.selectChild,
  selectChildren: Ue.selectChildren,
  filter: vd,
  merge: bd,
  selection: Ad,
  transition: Xd,
  call: Ue.call,
  nodes: Ue.nodes,
  node: Ue.node,
  size: Ue.size,
  empty: Ue.empty,
  each: Ue.each,
  on: _d,
  attr: rd,
  attrTween: ld,
  style: Td,
  styleTween: Ld,
  text: Od,
  textTween: Yd,
  remove: Cd,
  tween: Gu,
  delay: fd,
  duration: gd,
  ease: yd,
  easeVarying: wd,
  end: Wd,
  [Symbol.iterator]: Ue[Symbol.iterator]
};
function Zd(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var Ud = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: Zd
};
function Kd(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function Gd(e) {
  var t, n;
  e instanceof Ge ? (t = e._id, e = e._name) : (t = xs(), (n = Ud).time = tr(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], a = s.length, l, c = 0; c < a; ++c)
      (l = s[c]) && Fn(l, e, t, c, s, n || Kd(l, t));
  return new Ge(o, this._parents, e, t);
}
en.prototype.interrupt = Zu;
en.prototype.transition = Gd;
const hn = (e) => () => e;
function Qd(e, {
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
var Yn = new Ke(1, 0, 0);
ws.prototype = Ke.prototype;
function ws(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return Yn;
  return e.__zoom;
}
function vo(e) {
  e.stopImmediatePropagation();
}
function zt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Jd(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function ef() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function Wr() {
  return this.__zoom || Yn;
}
function tf(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function nf() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function of(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], i = e.invertY(t[0][1]) - n[0][1], s = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    s > i ? (i + s) / 2 : Math.min(0, i) || Math.max(0, s)
  );
}
function vs() {
  var e = Jd, t = ef, n = of, o = tf, r = nf, i = [0, 1 / 0], s = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], a = 250, l = wn, c = Vn("start", "zoom", "end"), d, f, u, m = 500, p = 150, y = 0, w = 10;
  function x(b) {
    b.property("__zoom", Wr).on("wheel.zoom", D, { passive: !1 }).on("mousedown.zoom", j).on("dblclick.zoom", F).filter(r).on("touchstart.zoom", C).on("touchmove.zoom", R).on("touchend.zoom touchcancel.zoom", H).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  x.transform = function(b, k, N, A) {
    var $ = b.selection ? b.selection() : b;
    $.property("__zoom", Wr), b !== $ ? _(b, k, N, A) : $.interrupt().each(function() {
      E(this, arguments).event(A).start().zoom(null, typeof k == "function" ? k.apply(this, arguments) : k).end();
    });
  }, x.scaleBy = function(b, k, N, A) {
    x.scaleTo(b, function() {
      var $ = this.__zoom.k, P = typeof k == "function" ? k.apply(this, arguments) : k;
      return $ * P;
    }, N, A);
  }, x.scaleTo = function(b, k, N, A) {
    x.transform(b, function() {
      var $ = t.apply(this, arguments), P = this.__zoom, B = N == null ? v($) : typeof N == "function" ? N.apply(this, arguments) : N, V = P.invert(B), O = typeof k == "function" ? k.apply(this, arguments) : k;
      return n(g(S(P, O), B, V), $, s);
    }, N, A);
  }, x.translateBy = function(b, k, N, A) {
    x.transform(b, function() {
      return n(this.__zoom.translate(
        typeof k == "function" ? k.apply(this, arguments) : k,
        typeof N == "function" ? N.apply(this, arguments) : N
      ), t.apply(this, arguments), s);
    }, null, A);
  }, x.translateTo = function(b, k, N, A, $) {
    x.transform(b, function() {
      var P = t.apply(this, arguments), B = this.__zoom, V = A == null ? v(P) : typeof A == "function" ? A.apply(this, arguments) : A;
      return n(Yn.translate(V[0], V[1]).scale(B.k).translate(
        typeof k == "function" ? -k.apply(this, arguments) : -k,
        typeof N == "function" ? -N.apply(this, arguments) : -N
      ), P, s);
    }, A, $);
  };
  function S(b, k) {
    return k = Math.max(i[0], Math.min(i[1], k)), k === b.k ? b : new Ke(k, b.x, b.y);
  }
  function g(b, k, N) {
    var A = k[0] - N[0] * b.k, $ = k[1] - N[1] * b.k;
    return A === b.x && $ === b.y ? b : new Ke(b.k, A, $);
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
      var $ = this, P = arguments, B = E($, P).event(A), V = t.apply($, P), O = N == null ? v(V) : typeof N == "function" ? N.apply($, P) : N, U = Math.max(V[1][0] - V[0][0], V[1][1] - V[0][1]), K = $.__zoom, X = typeof k == "function" ? k.apply($, P) : k, ie = l(K.invert(O).concat(U / K.k), X.invert(O).concat(U / X.k));
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
      var k = Ae(this.that).datum();
      c.call(
        b,
        this.that,
        new Qd(b, {
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
    var N = E(this, k).event(b), A = this.__zoom, $ = Math.max(i[0], Math.min(i[1], A.k * Math.pow(2, o.apply(this, arguments)))), P = Re(b);
    if (N.wheel)
      (N.mouse[0][0] !== P[0] || N.mouse[0][1] !== P[1]) && (N.mouse[1] = A.invert(N.mouse[0] = P)), clearTimeout(N.wheel);
    else {
      if (A.k === $) return;
      N.mouse = [P, A.invert(P)], Sn(this), N.start();
    }
    zt(b), N.wheel = setTimeout(B, p), N.zoom("mouse", n(g(S(A, $), N.mouse[0], N.mouse[1]), N.extent, s));
    function B() {
      N.wheel = null, N.end();
    }
  }
  function j(b, ...k) {
    if (u || !e.apply(this, arguments)) return;
    var N = b.currentTarget, A = E(this, k, !0).event(b), $ = Ae(b.view).on("mousemove.zoom", O, !0).on("mouseup.zoom", U, !0), P = Re(b, N), B = b.clientX, V = b.clientY;
    os(b.view), vo(b), A.mouse = [P, this.__zoom.invert(P)], Sn(this), A.start();
    function O(K) {
      if (zt(K), !A.moved) {
        var X = K.clientX - B, ie = K.clientY - V;
        A.moved = X * X + ie * ie > y;
      }
      A.event(K).zoom("mouse", n(g(A.that.__zoom, A.mouse[0] = Re(K, N), A.mouse[1]), A.extent, s));
    }
    function U(K) {
      $.on("mousemove.zoom mouseup.zoom", null), rs(K.view, A.moved), zt(K), A.event(K).end();
    }
  }
  function F(b, ...k) {
    if (e.apply(this, arguments)) {
      var N = this.__zoom, A = Re(b.changedTouches ? b.changedTouches[0] : b, this), $ = N.invert(A), P = N.k * (b.shiftKey ? 0.5 : 2), B = n(g(S(N, P), A, $), t.apply(this, k), s);
      zt(b), a > 0 ? Ae(this).transition().duration(a).call(_, B, A, b) : Ae(this).call(x.transform, B, A, b);
    }
  }
  function C(b, ...k) {
    if (e.apply(this, arguments)) {
      var N = b.touches, A = N.length, $ = E(this, k, b.changedTouches.length === A).event(b), P, B, V, O;
      for (vo(b), B = 0; B < A; ++B)
        V = N[B], O = Re(V, this), O = [O, this.__zoom.invert(O), V.identifier], $.touch0 ? !$.touch1 && $.touch0[2] !== O[2] && ($.touch1 = O, $.taps = 0) : ($.touch0 = O, P = !0, $.taps = 1 + !!d);
      d && (d = clearTimeout(d)), P && ($.taps < 2 && (f = O[0], d = setTimeout(function() {
        d = null;
      }, m)), Sn(this), $.start());
    }
  }
  function R(b, ...k) {
    if (this.__zooming) {
      var N = E(this, k).event(b), A = b.changedTouches, $ = A.length, P, B, V, O;
      for (zt(b), P = 0; P < $; ++P)
        B = A[P], V = Re(B, this), N.touch0 && N.touch0[2] === B.identifier ? N.touch0[0] = V : N.touch1 && N.touch1[2] === B.identifier && (N.touch1[0] = V);
      if (B = N.that.__zoom, N.touch1) {
        var U = N.touch0[0], K = N.touch0[1], X = N.touch1[0], ie = N.touch1[1], G = (G = X[0] - U[0]) * G + (G = X[1] - U[1]) * G, T = (T = ie[0] - K[0]) * T + (T = ie[1] - K[1]) * T;
        B = S(B, Math.sqrt(G / T)), V = [(U[0] + X[0]) / 2, (U[1] + X[1]) / 2], O = [(K[0] + ie[0]) / 2, (K[1] + ie[1]) / 2];
      } else if (N.touch0) V = N.touch0[0], O = N.touch0[1];
      else return;
      N.zoom("touch", n(g(B, V, O), N.extent, s));
    }
  }
  function H(b, ...k) {
    if (this.__zooming) {
      var N = E(this, k).event(b), A = b.changedTouches, $ = A.length, P, B;
      for (vo(b), u && clearTimeout(u), u = setTimeout(function() {
        u = null;
      }, m), P = 0; P < $; ++P)
        B = A[P], N.touch0 && N.touch0[2] === B.identifier ? delete N.touch0 : N.touch1 && N.touch1[2] === B.identifier && delete N.touch1;
      if (N.touch1 && !N.touch0 && (N.touch0 = N.touch1, delete N.touch1), N.touch0) N.touch0[1] = this.__zoom.invert(N.touch0[0]);
      else if (N.end(), N.taps === 2 && (B = Re(B, this), Math.hypot(f[0] - B[0], f[1] - B[1]) < w)) {
        var V = Ae(this).on("dblclick.zoom");
        V && V.apply(this, arguments);
      }
    }
  }
  return x.wheelDelta = function(b) {
    return arguments.length ? (o = typeof b == "function" ? b : hn(+b), x) : o;
  }, x.filter = function(b) {
    return arguments.length ? (e = typeof b == "function" ? b : hn(!!b), x) : e;
  }, x.touchable = function(b) {
    return arguments.length ? (r = typeof b == "function" ? b : hn(!!b), x) : r;
  }, x.extent = function(b) {
    return arguments.length ? (t = typeof b == "function" ? b : hn([[+b[0][0], +b[0][1]], [+b[1][0], +b[1][1]]]), x) : t;
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
], bs = ["Enter", " ", "Escape"], Ss = {
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
var St;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(St || (St = {}));
var lt;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(lt || (lt = {}));
var Zt;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Zt || (Zt = {}));
const Es = {
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
var jn;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(jn || (jn = {}));
var J;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(J || (J = {}));
const qr = {
  [J.Left]: J.Right,
  [J.Right]: J.Left,
  [J.Top]: J.Bottom,
  [J.Bottom]: J.Top
};
function _s(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Ns = (e) => "id" in e && "source" in e && "target" in e, rf = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), rr = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), nn = (e, t = [0, 0]) => {
  const { width: n, height: o } = Qe(e), r = e.origin ?? t, i = n * r[0], s = o * r[1];
  return {
    x: e.position.x - i,
    y: e.position.y - s
  };
}, sf = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const i = typeof r == "string";
    let s = !t.nodeLookup && !i ? r : void 0;
    t.nodeLookup && (s = i ? t.nodeLookup.get(r) : rr(r) ? r : t.nodeLookup.get(r.id));
    const a = s ? Pn(s, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return Xn(o, a);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return Wn(n);
}, on = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = Xn(n, Pn(r)), o = !0);
  }), o ? Wn(n) : { x: 0, y: 0, width: 0, height: 0 };
}, ir = (e, t, [n, o, r] = [0, 0, 1], i = !1, s = !1) => {
  const a = {
    ...Dt(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, l = [];
  for (const c of e.values()) {
    const { measured: d, selectable: f = !0, hidden: u = !1 } = c;
    if (s && !f || u)
      continue;
    const m = d.width ?? c.width ?? c.initialWidth ?? null, p = d.height ?? c.height ?? c.initialHeight ?? null, y = Ut(a, _t(c)), w = (m ?? 0) * (p ?? 0), x = i && y > 0;
    (!c.internals.handleBounds || x || y >= w || c.dragging) && l.push(c);
  }
  return l;
}, af = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function cf(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function lf({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: i }, s) {
  if (e.size === 0)
    return !0;
  const a = cf(e, s), l = on(a), c = ar(l, t, n, s?.minZoom ?? r, s?.maxZoom ?? i, s?.padding ?? 0.1);
  return await o.setViewport(c, {
    duration: s?.duration,
    ease: s?.ease,
    interpolate: s?.interpolate
  }), !0;
}
function Cs({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: i }) {
  const s = n.get(e), a = s.parentId ? n.get(s.parentId) : void 0, { x: l, y: c } = a ? a.internals.positionAbsolute : { x: 0, y: 0 }, d = s.origin ?? o;
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
  const u = ht(f) ? ft(t, f, s.measured) : t;
  return (s.measured.width === void 0 || s.measured.height === void 0) && i?.("015", je.error015()), {
    position: {
      x: u.x - l + (s.measured.width ?? 0) * d[0],
      y: u.y - c + (s.measured.height ?? 0) * d[1]
    },
    positionAbsolute: u
  };
}
async function uf({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const i = new Set(e.map((u) => u.id)), s = [];
  for (const u of n) {
    if (u.deletable === !1)
      continue;
    const m = i.has(u.id), p = !m && u.parentId && s.find((y) => y.id === u.parentId);
    (m || p) && s.push(u);
  }
  const a = new Set(t.map((u) => u.id)), l = o.filter((u) => u.deletable !== !1), d = af(s, l);
  for (const u of l)
    a.has(u.id) && !d.find((p) => p.id === u.id) && d.push(u);
  if (!r)
    return {
      edges: d,
      nodes: s
    };
  const f = await r({
    nodes: s,
    edges: d
  });
  return typeof f == "boolean" ? f ? { edges: d, nodes: s } : { edges: [], nodes: [] } : f;
}
const Et = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), ft = (e = { x: 0, y: 0 }, t, n) => ({
  x: Et(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: Et(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function ks(e, t, n) {
  const { width: o, height: r } = Qe(n), { x: i, y: s } = n.internals.positionAbsolute;
  return ft(e, [
    [i, s],
    [i + o, s + r]
  ], t);
}
const Zr = (e, t, n) => e < t ? Et(Math.abs(e - t), 1, t) / t : e > n ? -Et(Math.abs(e - n), 1, t) / t : 0, sr = (e, t, n = 15, o = 40) => {
  const r = Zr(e.x, o, t.width - o) * n, i = Zr(e.y, o, t.height - o) * n;
  return [r, i];
}, Xn = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), Bo = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), Wn = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), _t = (e, t = [0, 0]) => {
  const { x: n, y: o } = rr(e) ? e.internals.positionAbsolute : nn(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, Pn = (e, t = [0, 0]) => {
  const { x: n, y: o } = rr(e) ? e.internals.positionAbsolute : nn(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, Is = (e, t) => Wn(Xn(Bo(e), Bo(t))), Ut = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, Ur = (e) => Le(e.width) && Le(e.height) && Le(e.x) && Le(e.y), Le = (e) => !isNaN(e) && isFinite(e), Ms = (e, t) => (n, o) => {
}, rn = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), Dt = ({ x: e, y: t }, [n, o, r], i = !1, s = [1, 1]) => {
  const a = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return i ? rn(a, s) : a;
}, Nt = ({ x: e, y: t }, [n, o, r]) => ({
  x: e * r + n,
  y: t * r + o
});
function gt(e, t) {
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
function df(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = gt(e, n), r = gt(e, t);
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
    const o = gt(e.top ?? e.y ?? 0, n), r = gt(e.bottom ?? e.y ?? 0, n), i = gt(e.left ?? e.x ?? 0, t), s = gt(e.right ?? e.x ?? 0, t);
    return { top: o, right: s, bottom: r, left: i, x: i + s, y: o + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function ff(e, t, n, o, r, i) {
  const { x: s, y: a } = Nt(e, [t, n, o]), { x: l, y: c } = Nt({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), d = r - l, f = i - c;
  return {
    left: Math.floor(s),
    top: Math.floor(a),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const ar = (e, t, n, o, r, i) => {
  const s = df(i, t, n), a = (t - s.x) / e.width, l = (n - s.y) / e.height, c = Math.min(a, l), d = Et(c, o, r), f = e.x + e.width / 2, u = e.y + e.height / 2, m = t / 2 - f * d, p = n / 2 - u * d, y = ff(e, m, p, d, t, n), w = {
    left: Math.min(y.left - s.left, 0),
    top: Math.min(y.top - s.top, 0),
    right: Math.min(y.right - s.right, 0),
    bottom: Math.min(y.bottom - s.bottom, 0)
  };
  return {
    x: m - w.left + w.right,
    y: p - w.top + w.bottom,
    zoom: d
  };
}, Kt = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function ht(e) {
  return e != null && e !== "parent";
}
function Qe(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function As(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function Ds(e, t = { width: 0, height: 0 }, n, o, r) {
  const i = { ...e }, s = o.get(n);
  if (s) {
    const a = s.origin || r;
    i.x += s.internals.positionAbsolute.x - (t.width ?? 0) * a[0], i.y += s.internals.positionAbsolute.y - (t.height ?? 0) * a[1];
  }
  return i;
}
function Kr(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function hf() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function pf(e) {
  return { ...Ss, ...e || {} };
}
function Bt(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: i, y: s } = He(e), a = Dt({ x: i - (r?.left ?? 0), y: s - (r?.top ?? 0) }, o), { x: l, y: c } = n ? rn(a, t) : a;
  return {
    xSnapped: l,
    ySnapped: c,
    ...a
  };
}
const cr = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), js = (e) => e?.getRootNode?.() || window?.document, gf = ["INPUT", "SELECT", "TEXTAREA"];
function Ps(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : gf.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const $s = (e) => "clientX" in e, He = (e, t) => {
  const n = $s(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, Gr = (e, t, n, o, r) => {
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
      ...cr(s)
    };
  });
};
function Ts({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: i, targetControlX: s, targetControlY: a }) {
  const l = e * 0.125 + r * 0.375 + s * 0.375 + n * 0.125, c = t * 0.125 + i * 0.375 + a * 0.375 + o * 0.125, d = Math.abs(l - e), f = Math.abs(c - t);
  return [l, c, d, f];
}
function pn(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function Qr({ pos: e, x1: t, y1: n, x2: o, y2: r, c: i }) {
  switch (e) {
    case J.Left:
      return [t - pn(t - o, i), n];
    case J.Right:
      return [t + pn(o - t, i), n];
    case J.Top:
      return [t, n - pn(n - r, i)];
    case J.Bottom:
      return [t, n + pn(r - n, i)];
  }
}
function Rs({ sourceX: e, sourceY: t, sourcePosition: n = J.Bottom, targetX: o, targetY: r, targetPosition: i = J.Top, curvature: s = 0.25 }) {
  const [a, l] = Qr({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: s
  }), [c, d] = Qr({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: s
  }), [f, u, m, p] = Ts({
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
    f,
    u,
    m,
    p
  ];
}
function zs({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, i = n < e ? n + r : n - r, s = Math.abs(o - t) / 2, a = o < t ? o + s : o - s;
  return [i, a, r, s];
}
function mf({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: i = "basic" }) {
  if (i === "manual")
    return o;
  const s = r && n ? o + 1e3 : o, a = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return s + a;
}
function yf({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const i = Xn(Pn(e), Pn(t));
  i.x === i.x2 && (i.x2 += 1), i.y === i.y2 && (i.y2 += 1);
  const s = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return Ut(s, Wn(i)) > 0;
}
const Ls = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, xf = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), wf = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", je.error006()), t;
  const o = n.getEdgeId || Ls;
  let r;
  return Ns(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, xf(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, vf = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: r, ...i } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", je.error006()), n;
  if (!n.find((c) => c.id === e.id))
    return o.onError?.("007", je.error007(r)), n;
  const a = o.getEdgeId || Ls, l = {
    ...i,
    id: o.shouldReplaceId ? a(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((c) => c.id !== r).concat(l);
};
function Hs({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, i, s, a] = zs({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, i, s, a];
}
const Jr = {
  [J.Left]: { x: -1, y: 0 },
  [J.Right]: { x: 1, y: 0 },
  [J.Top]: { x: 0, y: -1 },
  [J.Bottom]: { x: 0, y: 1 }
}, bf = ({ source: e, sourcePosition: t = J.Bottom, target: n }) => t === J.Left || t === J.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, ei = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Sf({ source: e, sourcePosition: t = J.Bottom, target: n, targetPosition: o = J.Top, center: r, offset: i, stepPosition: s }) {
  const a = Jr[t], l = Jr[o], c = { x: e.x + a.x * i, y: e.y + a.y * i }, d = { x: n.x + l.x * i, y: n.y + l.y * i }, f = bf({
    source: c,
    sourcePosition: t,
    target: d
  }), u = f.x !== 0 ? "x" : "y", m = f[u];
  let p = [], y, w;
  const x = { x: 0, y: 0 }, S = { x: 0, y: 0 }, [, , g, v] = zs({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (a[u] * l[u] === -1) {
    u === "x" ? (y = r.x ?? c.x + (d.x - c.x) * s, w = r.y ?? (c.y + d.y) / 2) : (y = r.x ?? (c.x + d.x) / 2, w = r.y ?? c.y + (d.y - c.y) * s);
    const D = [
      { x: y, y: c.y },
      { x: y, y: d.y }
    ], j = [
      { x: c.x, y: w },
      { x: d.x, y: w }
    ];
    a[u] === m ? p = u === "x" ? D : j : p = u === "x" ? j : D;
  } else {
    const D = [{ x: c.x, y: d.y }], j = [{ x: d.x, y: c.y }];
    if (u === "x" ? p = a.x === m ? j : D : p = a.y === m ? D : j, t === o) {
      const b = Math.abs(e[u] - n[u]);
      if (b <= i) {
        const k = Math.min(i - 1, i - b);
        a[u] === m ? x[u] = (c[u] > e[u] ? -1 : 1) * k : S[u] = (d[u] > n[u] ? -1 : 1) * k;
      }
    }
    if (t !== o) {
      const b = u === "x" ? "y" : "x", k = a[u] === l[b], N = c[b] > d[b], A = c[b] < d[b];
      (a[u] === 1 && (!k && N || k && A) || a[u] !== 1 && (!k && A || k && N)) && (p = u === "x" ? D : j);
    }
    const F = { x: c.x + x.x, y: c.y + x.y }, C = { x: d.x + S.x, y: d.y + S.y }, R = Math.max(Math.abs(F.x - p[0].x), Math.abs(C.x - p[0].x)), H = Math.max(Math.abs(F.y - p[0].y), Math.abs(C.y - p[0].y));
    R >= H ? (y = (F.x + C.x) / 2, w = p[0].y) : (y = p[0].x, w = (F.y + C.y) / 2);
  }
  const _ = { x: c.x + x.x, y: c.y + x.y }, E = { x: d.x + S.x, y: d.y + S.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ..._.x !== p[0].x || _.y !== p[0].y ? [_] : [],
    ...p,
    ...E.x !== p[p.length - 1].x || E.y !== p[p.length - 1].y ? [E] : [],
    n
  ], y, w, g, v];
}
function Ef(e, t, n, o) {
  const r = Math.min(ei(e, t) / 2, ei(t, n) / 2, o), { x: i, y: s } = t;
  if (e.x === i && i === n.x || e.y === s && s === n.y)
    return `L${i} ${s}`;
  if (e.y === s) {
    const c = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${i + r * c},${s}Q ${i},${s} ${i},${s + r * d}`;
  }
  const a = e.x < n.x ? 1 : -1, l = e.y < n.y ? -1 : 1;
  return `L ${i},${s + r * l}Q ${i},${s} ${i + r * a},${s}`;
}
function $n({ sourceX: e, sourceY: t, sourcePosition: n = J.Bottom, targetX: o, targetY: r, targetPosition: i = J.Top, borderRadius: s = 5, centerX: a, centerY: l, offset: c = 20, stepPosition: d = 0.5 }) {
  const [f, u, m, p, y] = Sf({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: i,
    center: { x: a, y: l },
    offset: c,
    stepPosition: d
  });
  let w = `M${f[0].x} ${f[0].y}`;
  for (let x = 1; x < f.length - 1; x++)
    w += Ef(f[x - 1], f[x], f[x + 1], s);
  return w += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [w, u, m, p, y];
}
function ti(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function _f(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!ti(t) || !ti(n))
    return null;
  const o = t.internals.handleBounds || ni(t.handles), r = n.internals.handleBounds || ni(n.handles), i = oi(o?.source ?? [], e.sourceHandle), s = oi(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === St.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!i || !s)
    return e.onError?.("008", je.error008(i ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const a = i?.position || J.Bottom, l = s?.position || J.Top, c = pt(t, i, a), d = pt(n, s, l);
  return {
    sourceX: c.x,
    sourceY: c.y,
    targetX: d.x,
    targetY: d.y,
    sourcePosition: a,
    targetPosition: l
  };
}
function ni(e) {
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
function oi(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Fo(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function Nf(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const i = /* @__PURE__ */ new Set();
  return e.reduce((s, a) => ([a.markerStart || o, a.markerEnd || r].forEach((l) => {
    if (l && typeof l == "object") {
      const c = Fo(l, t);
      i.has(c) || (s.push({ id: c, color: l.color || n, ...l }), i.add(c));
    }
  }), s), []).sort((s, a) => s.id.localeCompare(a.id));
}
const Vs = 1e3, Cf = 10, lr = {
  nodeOrigin: [0, 0],
  nodeExtent: qt,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, kf = {
  ...lr,
  checkEquality: !0
};
function ur(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function If(e, t, n) {
  const o = ur(lr, n);
  for (const r of e.values())
    if (r.parentId)
      fr(r, e, t, o);
    else {
      const i = nn(r, o.nodeOrigin), s = ht(r.extent) ? r.extent : o.nodeExtent, a = ft(i, s, Qe(r));
      r.internals.positionAbsolute = a;
    }
}
function Mf(e, t) {
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
function dr(e) {
  return e === "manual";
}
function Yo(e, t, n, o = {}) {
  const r = ur(kf, o), i = { i: 0 }, s = new Map(t), a = r?.elevateNodesOnSelect && !dr(r.zIndexMode) ? Vs : 0;
  let l = e.length > 0, c = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = s.get(d.id);
    if (r.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const u = nn(d, r.nodeOrigin), m = ht(d.extent) ? d.extent : r.nodeExtent, p = ft(u, m, Qe(d));
      f = {
        ...r.defaults,
        ...d,
        measured: {
          width: d.measured?.width,
          height: d.measured?.height
        },
        internals: {
          positionAbsolute: p,
          // if user re-initializes the node or removes `measured` for whatever reason, we reset the handleBounds so that the node gets re-measured
          handleBounds: Mf(d, f),
          z: Os(d, a, r.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (l = !1), d.parentId && fr(f, t, n, o, i), c ||= d.selected ?? !1;
  }
  return { nodesInitialized: l, hasSelectedNodes: c };
}
function Af(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function fr(e, t, n, o, r) {
  const { elevateNodesOnSelect: i, nodeOrigin: s, nodeExtent: a, zIndexMode: l } = ur(lr, o), c = e.parentId, d = t.get(c);
  if (!d) {
    console.warn(`Parent node ${c} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Af(e, n), r && !d.parentId && d.internals.rootParentIndex === void 0 && l === "auto" && (d.internals.rootParentIndex = ++r.i, d.internals.z = d.internals.z + r.i * Cf), r && d.internals.rootParentIndex !== void 0 && (r.i = d.internals.rootParentIndex);
  const f = i && !dr(l) ? Vs : 0, { x: u, y: m, z: p } = Df(e, d, s, a, f, l), { positionAbsolute: y } = e.internals, w = u !== y.x || m !== y.y;
  (w || p !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: w ? { x: u, y: m } : y,
      z: p
    }
  });
}
function Os(e, t, n) {
  const o = Le(e.zIndex) ? e.zIndex : 0;
  return dr(n) ? o : o + (e.selected ? t : 0);
}
function Df(e, t, n, o, r, i) {
  const { x: s, y: a } = t.internals.positionAbsolute, l = Qe(e), c = nn(e, n), d = ht(e.extent) ? ft(c, e.extent, l) : c;
  let f = ft({ x: s + d.x, y: a + d.y }, o, l);
  e.extent === "parent" && (f = ks(f, l, t));
  const u = Os(e, r, i), m = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: m >= u ? m + 1 : u
  };
}
function hr(e, t, n, o = [0, 0]) {
  const r = [], i = /* @__PURE__ */ new Map();
  for (const s of e) {
    const a = t.get(s.parentId);
    if (!a)
      continue;
    const l = i.get(s.parentId)?.expandedRect ?? _t(a), c = Is(l, s.rect);
    i.set(s.parentId, { expandedRect: c, parent: a });
  }
  return i.size > 0 && i.forEach(({ expandedRect: s, parent: a }, l) => {
    const c = a.internals.positionAbsolute, d = Qe(a), f = a.origin ?? o, u = s.x < c.x ? Math.round(Math.abs(c.x - s.x)) : 0, m = s.y < c.y ? Math.round(Math.abs(c.y - s.y)) : 0, p = Math.max(d.width, Math.round(s.width)), y = Math.max(d.height, Math.round(s.height)), w = (p - d.width) * f[0], x = (y - d.height) * f[1];
    (u > 0 || m > 0 || w || x) && (r.push({
      id: l,
      type: "position",
      position: {
        x: a.position.x - u + w,
        y: a.position.y - m + x
      }
    }), n.get(l)?.forEach((S) => {
      e.some((g) => g.id === S.id) || r.push({
        id: S.id,
        type: "position",
        position: {
          x: S.position.x + u,
          y: S.position.y + m
        }
      });
    })), (d.width < s.width || d.height < s.height || u || m) && r.push({
      id: l,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: p + (u ? f[0] * u - w : 0),
        height: y + (m ? f[1] * m - x : 0)
      }
    });
  }), r;
}
function jf(e, t, n, o, r, i, s) {
  const a = o?.querySelector(".xyflow__viewport");
  let l = !1;
  if (!a)
    return { changes: [], updatedInternals: l };
  const c = [], d = window.getComputedStyle(a), { m22: f } = new window.DOMMatrixReadOnly(d.transform), u = [];
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
    const y = cr(m.nodeElement), w = p.measured.width !== y.width || p.measured.height !== y.height;
    if (!!(y.width && y.height && (w || !p.internals.handleBounds || m.force))) {
      const S = m.nodeElement.getBoundingClientRect(), g = ht(p.extent) ? p.extent : i;
      let { positionAbsolute: v } = p.internals;
      p.parentId && p.extent === "parent" ? v = ks(v, y, t.get(p.parentId)) : g && (v = ft(v, g, y));
      const _ = {
        ...p,
        measured: y,
        internals: {
          ...p.internals,
          positionAbsolute: v,
          handleBounds: {
            source: Gr("source", m.nodeElement, S, f, p.id),
            target: Gr("target", m.nodeElement, S, f, p.id)
          }
        }
      };
      t.set(p.id, _), p.parentId && fr(_, t, n, { nodeOrigin: r, zIndexMode: s }), l = !0, w && (c.push({
        id: p.id,
        type: "dimensions",
        dimensions: y
      }), p.expandParent && p.parentId && u.push({
        id: p.id,
        parentId: p.parentId,
        rect: _t(_, r)
      }));
    }
  }
  if (u.length > 0) {
    const m = hr(u, t, n, r);
    c.push(...m);
  }
  return { changes: c, updatedInternals: l };
}
async function Pf({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: i }) {
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
function ri(e, t, n, o, r, i) {
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
function Bs(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: i, sourceHandle: s = null, targetHandle: a = null } = o, l = { edgeId: o.id, source: r, target: i, sourceHandle: s, targetHandle: a }, c = `${r}-${s}--${i}-${a}`, d = `${i}-${a}--${r}-${s}`;
    ri("source", l, d, e, r, s), ri("target", l, c, e, i, a), t.set(o.id, o);
  }
}
function Fs(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : Fs(n, t) : !1;
}
function ii(e, t, n) {
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
function $f(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [i, s] of e)
    if ((s.selected || s.id === o) && (!s.parentId || !Fs(s, e)) && (s.draggable || t && typeof s.draggable > "u")) {
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
function bo({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
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
function Tf({ dragItems: e, snapGrid: t, x: n, y: o }) {
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
function Rf({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let i = { x: null, y: null }, s = 0, a = /* @__PURE__ */ new Map(), l = !1, c = { x: 0, y: 0 }, d = null, f = !1, u = null, m = !1, p = !1, y = null;
  function w({ noDragClassName: S, handleSelector: g, domNode: v, isSelectable: _, nodeId: E, nodeClickDistance: M = 0 }) {
    u = Ae(v);
    function D({ x: R, y: H }) {
      const { nodeLookup: b, nodeExtent: k, snapGrid: N, snapToGrid: A, nodeOrigin: $, onNodeDrag: P, onSelectionDrag: B, onError: V, updateNodePositions: O } = t();
      i = { x: R, y: H };
      let U = !1;
      const K = a.size > 1, X = K && k ? Bo(on(a)) : null, ie = K && A ? Tf({
        dragItems: a,
        snapGrid: N,
        x: R,
        y: H
      }) : null;
      for (const [G, T] of a) {
        if (!b.has(G))
          continue;
        let Z = { x: R - T.distance.x, y: H - T.distance.y };
        A && (Z = ie ? {
          x: Math.round(Z.x + ie.x),
          y: Math.round(Z.y + ie.y)
        } : rn(Z, N));
        let re = null;
        if (K && k && !T.extent && X) {
          const { positionAbsolute: te } = T.internals, se = te.x - X.x + k[0][0], L = te.x + T.measured.width - X.x2 + k[1][0], W = te.y - X.y + k[0][1], fe = te.y + T.measured.height - X.y2 + k[1][1];
          re = [
            [se, W],
            [L, fe]
          ];
        }
        const { position: oe, positionAbsolute: Q } = Cs({
          nodeId: G,
          nextPosition: Z,
          nodeLookup: b,
          nodeExtent: re || k,
          nodeOrigin: $,
          onError: V
        });
        U = U || T.position.x !== oe.x || T.position.y !== oe.y, T.position = oe, T.internals.positionAbsolute = Q;
      }
      if (p = p || U, !!U && (O(a, !0), y && (o || P || !E && B))) {
        const [G, T] = bo({
          nodeId: E,
          dragItems: a,
          nodeLookup: b
        });
        o?.(y, a, G, T), P?.(y, G, T), E || B?.(y, T);
      }
    }
    async function j() {
      if (!d)
        return;
      const { transform: R, panBy: H, autoPanSpeed: b, autoPanOnNodeDrag: k } = t();
      if (!k) {
        l = !1, cancelAnimationFrame(s);
        return;
      }
      const [N, A] = sr(c, d, b);
      (N !== 0 || A !== 0) && (i.x = (i.x ?? 0) - N / R[2], i.y = (i.y ?? 0) - A / R[2], await H({ x: N, y: A }) && D(i)), s = requestAnimationFrame(j);
    }
    function F(R) {
      const { nodeLookup: H, multiSelectionActive: b, nodesDraggable: k, transform: N, snapGrid: A, snapToGrid: $, selectNodesOnDrag: P, onNodeDragStart: B, onSelectionDragStart: V, unselectNodesAndEdges: O } = t();
      f = !0, (!P || !_) && !b && E && (H.get(E)?.selected || O()), _ && P && E && e?.(E);
      const U = Bt(R.sourceEvent, { transform: N, snapGrid: A, snapToGrid: $, containerBounds: d });
      if (i = U, a = $f(H, k, U, E), a.size > 0 && (n || B || !E && V)) {
        const [K, X] = bo({
          nodeId: E,
          dragItems: a,
          nodeLookup: H
        });
        n?.(R.sourceEvent, a, K, X), B?.(R.sourceEvent, K, X), E || V?.(R.sourceEvent, X);
      }
    }
    const C = is().clickDistance(M).on("start", (R) => {
      const { domNode: H, nodeDragThreshold: b, transform: k, snapGrid: N, snapToGrid: A } = t();
      d = H?.getBoundingClientRect() || null, m = !1, p = !1, y = R.sourceEvent, b === 0 && F(R), i = Bt(R.sourceEvent, { transform: k, snapGrid: N, snapToGrid: A, containerBounds: d }), c = He(R.sourceEvent, d);
    }).on("drag", (R) => {
      const { autoPanOnNodeDrag: H, transform: b, snapGrid: k, snapToGrid: N, nodeDragThreshold: A, nodeLookup: $ } = t(), P = Bt(R.sourceEvent, { transform: b, snapGrid: k, snapToGrid: N, containerBounds: d });
      if (y = R.sourceEvent, (R.sourceEvent.type === "touchmove" && R.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      E && !$.has(E)) && (m = !0), !m) {
        if (!l && H && f && (l = !0, j()), !f) {
          const B = He(R.sourceEvent, d), V = B.x - c.x, O = B.y - c.y;
          Math.sqrt(V * V + O * O) > A && F(R);
        }
        (i.x !== P.xSnapped || i.y !== P.ySnapped) && a && f && (c = He(R.sourceEvent, d), D(P));
      }
    }).on("end", (R) => {
      if (!f || m) {
        m && a.size > 0 && t().updateNodePositions(a, !1);
        return;
      }
      if (l = !1, f = !1, cancelAnimationFrame(s), a.size > 0) {
        const { nodeLookup: H, updateNodePositions: b, onNodeDragStop: k, onSelectionDragStop: N } = t();
        if (p && (b(a, !1), p = !1), r || k || !E && N) {
          const [A, $] = bo({
            nodeId: E,
            dragItems: a,
            nodeLookup: H,
            dragging: !1
          });
          r?.(R.sourceEvent, a, A, $), k?.(R.sourceEvent, A, $), E || N?.(R.sourceEvent, $);
        }
      }
    }).filter((R) => {
      const H = R.target;
      return !R.button && (!S || !ii(H, `.${S}`, v)) && (!g || ii(H, g, v));
    });
    u.call(C);
  }
  function x() {
    u?.on(".drag", null);
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
    Ut(r, _t(i)) > 0 && o.push(i);
  return o;
}
const Lf = 250;
function Hf(e, t, n, o) {
  let r = [], i = 1 / 0;
  const s = zf(e, n, t + Lf);
  for (const a of s) {
    const l = [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []];
    for (const c of l) {
      if (o.nodeId === c.nodeId && o.type === c.type && o.id === c.id)
        continue;
      const { x: d, y: f } = pt(a, c, c.position, !0), u = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(f - e.y, 2));
      u > t || (u < i ? (r = [{ ...c, x: d, y: f }], i = u) : u === i && r.push({ ...c, x: d, y: f }));
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
function Ys(e, t, n, o, r, i = !1) {
  const s = o.get(e);
  if (!s)
    return null;
  const a = r === "strict" ? s.internals.handleBounds?.[t] : [...s.internals.handleBounds?.source ?? [], ...s.internals.handleBounds?.target ?? []], l = (n ? a?.find((c) => c.id === n) : a?.[0]) ?? null;
  return l && i ? { ...l, ...pt(s, l, l.position, !0) } : l;
}
function Xs(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Vf(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const Ws = () => !0;
function Of(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: i, isTarget: s, domNode: a, nodeLookup: l, lib: c, autoPanOnConnect: d, flowId: f, panBy: u, cancelConnection: m, onConnectStart: p, onConnect: y, onConnectEnd: w, isValidConnection: x = Ws, onReconnectEnd: S, updateConnection: g, getTransform: v, getFromHandle: _, autoPanSpeed: E, dragThreshold: M = 1, handleDomNode: D }) {
  const j = js(e.target);
  let F = 0, C;
  const { x: R, y: H } = He(e), b = Xs(i, D), k = a?.getBoundingClientRect();
  let N = !1;
  if (!k || !b)
    return;
  const A = Ys(r, b, o, l, t);
  if (!A)
    return;
  let $ = He(e, k), P = !1, B = null, V = !1, O = null;
  function U() {
    if (!d || !k)
      return;
    const [oe, Q] = sr($, k, E);
    u({ x: oe, y: Q }), F = requestAnimationFrame(U);
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
    from: pt(X, K, J.Left, !0),
    fromHandle: K,
    fromPosition: K.position,
    fromNode: X,
    to: $,
    toHandle: null,
    toPosition: qr[K.position],
    toNode: null,
    pointer: $
  };
  function T() {
    N = !0, g(G), p?.(e, { nodeId: r, handleId: o, handleType: b });
  }
  M === 0 && T();
  function Z(oe) {
    if (!N) {
      const { x: fe, y: me } = He(oe), Ne = fe - R, Se = me - H;
      if (!(Ne * Ne + Se * Se > M * M))
        return;
      T();
    }
    if (!_() || !K) {
      re(oe);
      return;
    }
    const Q = v();
    $ = He(oe, k), C = Hf(Dt($, Q, !1, [1, 1]), n, l, K), P || (U(), P = !0);
    const te = qs(oe, {
      handle: C,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: s ? "target" : "source",
      isValidConnection: x,
      doc: j,
      lib: c,
      flowId: f,
      nodeLookup: l
    });
    O = te.handleDomNode, B = te.connection, V = Vf(!!C, te.isValid);
    const se = l.get(r), L = se ? pt(se, K, J.Left, !0) : G.from, W = {
      ...G,
      from: L,
      isValid: V,
      to: te.toHandle && V ? Nt({ x: te.toHandle.x, y: te.toHandle.y }, Q) : $,
      toHandle: te.toHandle,
      toPosition: V && te.toHandle ? te.toHandle.position : qr[K.position],
      toNode: te.toHandle ? l.get(te.toHandle.nodeId) : null,
      pointer: $
    };
    g(W), G = W;
  }
  function re(oe) {
    if (!("touches" in oe && oe.touches.length > 0)) {
      if (N) {
        (C || O) && B && V && y?.(B);
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
function qs(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: i, doc: s, lib: a, flowId: l, isValidConnection: c = Ws, nodeLookup: d }) {
  const f = i === "target", u = t ? s.querySelector(`.${a}-flow__handle[data-id="${l}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: m, y: p } = He(e), y = s.elementFromPoint(m, p), w = y?.classList.contains(`${a}-flow__handle`) ? y : u, x = {
    handleDomNode: w,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (w) {
    const S = Xs(void 0, w), g = w.getAttribute("data-nodeid"), v = w.getAttribute("data-handleid"), _ = w.classList.contains("connectable"), E = w.classList.contains("connectableend");
    if (!g || !S)
      return x;
    const M = {
      source: f ? g : o,
      sourceHandle: f ? v : r,
      target: f ? o : g,
      targetHandle: f ? r : v
    };
    x.connection = M;
    const j = _ && E && (n === St.Strict ? f && S === "source" || !f && S === "target" : g !== o || v !== r);
    x.isValid = j && c(M), x.toHandle = Ys(g, S, v, d, n, !0);
  }
  return x;
}
const Xo = {
  onPointerDown: Of,
  isValid: qs
};
function Bf({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = Ae(e);
  function i({ translateExtent: a, width: l, height: c, zoomStep: d = 1, pannable: f = !0, zoomable: u = !0, inversePan: m = !1 }) {
    const p = (g) => {
      if (g.sourceEvent.type !== "wheel" || !t)
        return;
      const v = n(), _ = g.sourceEvent.ctrlKey && Kt() ? 10 : 1, E = -g.sourceEvent.deltaY * (g.sourceEvent.deltaMode === 1 ? 0.05 : g.sourceEvent.deltaMode ? 1 : 2e-3) * d, M = v[2] * Math.pow(2, E * _);
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
      }, j = [
        [0, 0],
        [l, c]
      ];
      t.setViewportConstrained({
        x: D.x,
        y: D.y,
        zoom: v[2]
      }, j, a);
    }, S = vs().on("start", w).on("zoom", f ? x : null).on("zoom.wheel", u ? p : null);
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
const qn = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), So = ({ x: e, y: t, zoom: n }) => Yn.translate(e, t).scale(n), mt = (e, t) => e.target.closest(`.${t}`), Zs = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Ff = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, Eo = (e, t = 0, n = Ff, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, Us = (e) => {
  const t = e.ctrlKey && Kt() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Yf({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: i, zoomOnPinch: s, onPanZoomStart: a, onPanZoom: l, onPanZoomEnd: c }) {
  return (d) => {
    if (mt(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && s) {
      const w = Re(d), x = Us(d), S = f * Math.pow(2, x);
      o.scaleTo(n, S, w, d);
      return;
    }
    const u = d.deltaMode === 1 ? 20 : 1;
    let m = r === lt.Vertical ? 0 : d.deltaX * u, p = r === lt.Horizontal ? 0 : d.deltaY * u;
    !Kt() && d.shiftKey && r !== lt.Vertical && (m = d.deltaY * u, p = 0), o.translateBy(
      n,
      -(m / f) * i,
      -(p / f) * i,
      // @ts-ignore
      { internal: !0 }
    );
    const y = qn(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (l?.(d, y), e.panScrollTimeout = setTimeout(() => {
      c?.(d, y), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, a?.(d, y));
  };
}
function Xf({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const i = o.type === "wheel", s = !t && i && !o.ctrlKey, a = mt(o, e);
    if (o.ctrlKey && i && a && o.preventDefault(), s || a)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function Wf({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = qn(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function qf({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (i) => {
    e.usedRightMouseButton = !!(n && Zs(t, e.mouseButton ?? 0)), i.sourceEvent?.sync || o([i.transform.x, i.transform.y, i.transform.k]), r && !i.sourceEvent?.internal && r?.(i.sourceEvent, qn(i.transform));
  };
}
function Zf({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: i }) {
  return (s) => {
    if (!s.sourceEvent?.internal && (e.isZoomingOrPanning = !1, i && Zs(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && s.sourceEvent && i(s.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
      const a = qn(s.transform);
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
function Uf({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: i, userSelectionActive: s, noWheelClassName: a, noPanClassName: l, lib: c, connectionInProgress: d }) {
  return (f) => {
    const u = e || t, m = n && f.ctrlKey, p = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (mt(f, `${c}-flow__node`) || mt(f, `${c}-flow__edge`)))
      return !0;
    if (!o && !u && !r && !i && !n || s || d && !p || mt(f, a) && p || mt(f, l) && (!p || r && p && !e) || !n && f.ctrlKey && p)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!u && !r && !m && p || !o && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(o) && !o.includes(f.button) && f.type === "mousedown")
      return !1;
    const y = Array.isArray(o) && o.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || p) && y;
  };
}
function Kf({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: i, onPanZoomStart: s, onPanZoomEnd: a, onDraggingChange: l }) {
  const c = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = vs().scaleExtent([t, n]).translateExtent(o), u = Ae(e).call(f);
  S({
    x: r.x,
    y: r.y,
    zoom: Et(r.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], o);
  const m = u.on("wheel.zoom"), p = u.on("dblclick.zoom");
  f.wheelDelta(Us);
  async function y(C, R) {
    return u ? new Promise((H) => {
      f?.interpolate(R?.interpolate === "linear" ? Ot : wn).transform(Eo(u, R?.duration, R?.ease, () => H(!0)), C);
    }) : !1;
  }
  function w({ noWheelClassName: C, noPanClassName: R, onPaneContextMenu: H, userSelectionActive: b, panOnScroll: k, panOnDrag: N, panOnScrollMode: A, panOnScrollSpeed: $, preventScrolling: P, zoomOnPinch: B, zoomOnScroll: V, zoomOnDoubleClick: O, zoomActivationKeyPressed: U, lib: K, onTransformChange: X, connectionInProgress: ie, paneClickDistance: G, selectionOnDrag: T }) {
    b && !c.isZoomingOrPanning && x();
    const Z = k && !U && !b;
    f.clickDistance(T ? 1 / 0 : !Le(G) || G < 0 ? 0 : G);
    const re = Z ? Yf({
      zoomPanValues: c,
      noWheelClassName: C,
      d3Selection: u,
      d3Zoom: f,
      panOnScrollMode: A,
      panOnScrollSpeed: $,
      zoomOnPinch: B,
      onPanZoomStart: s,
      onPanZoom: i,
      onPanZoomEnd: a
    }) : Xf({
      noWheelClassName: C,
      preventScrolling: P,
      d3ZoomHandler: m
    });
    u.on("wheel.zoom", re, { passive: !1 });
    const oe = Wf({
      zoomPanValues: c,
      onDraggingChange: l,
      onPanZoomStart: s
    });
    f.on("start", oe);
    const Q = qf({
      zoomPanValues: c,
      panOnDrag: N,
      onPaneContextMenu: !!H,
      onPanZoom: i,
      onTransformChange: X
    });
    f.on("zoom", Q);
    const te = Zf({
      zoomPanValues: c,
      panOnDrag: N,
      panOnScroll: k,
      onPaneContextMenu: H,
      onPanZoomEnd: a,
      onDraggingChange: l
    });
    f.on("end", te);
    const se = Uf({
      zoomActivationKeyPressed: U,
      panOnDrag: N,
      zoomOnScroll: V,
      panOnScroll: k,
      zoomOnDoubleClick: O,
      zoomOnPinch: B,
      userSelectionActive: b,
      noPanClassName: R,
      noWheelClassName: C,
      lib: K,
      connectionInProgress: ie
    });
    f.filter(se), O ? u.on("dblclick.zoom", p) : u.on("dblclick.zoom", null);
  }
  function x() {
    f.on("zoom", null);
  }
  async function S(C, R, H) {
    const b = So(C), k = f?.constrain()(b, R, H);
    return k && await y(k), k;
  }
  async function g(C, R) {
    const H = So(C);
    return await y(H, R), H;
  }
  function v(C) {
    if (u) {
      const R = So(C), H = u.property("__zoom");
      (H.k !== C.zoom || H.x !== C.x || H.y !== C.y) && f?.transform(u, R, null, { sync: !0 });
    }
  }
  function _() {
    const C = u ? ws(u.node()) : { x: 0, y: 0, k: 1 };
    return { x: C.x, y: C.y, zoom: C.k };
  }
  async function E(C, R) {
    return u ? new Promise((H) => {
      f?.interpolate(R?.interpolate === "linear" ? Ot : wn).scaleTo(Eo(u, R?.duration, R?.ease, () => H(!0)), C);
    }) : !1;
  }
  async function M(C, R) {
    return u ? new Promise((H) => {
      f?.interpolate(R?.interpolate === "linear" ? Ot : wn).scaleBy(Eo(u, R?.duration, R?.ease, () => H(!0)), C);
    }) : !1;
  }
  function D(C) {
    f?.scaleExtent(C);
  }
  function j(C) {
    f?.translateExtent(C);
  }
  function F(C) {
    const R = !Le(C) || C < 0 ? 0 : C;
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
    setTranslateExtent: j,
    syncViewport: v,
    setClickDistance: F
  };
}
var Ct;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(Ct || (Ct = {}));
function Gf({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: i }) {
  const s = e - t, a = n - o, l = [s > 0 ? 1 : s < 0 ? -1 : 0, a > 0 ? 1 : a < 0 ? -1 : 0];
  return s && r && (l[0] = l[0] * -1), a && i && (l[1] = l[1] * -1), l;
}
function si(e) {
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
function gn(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function ai(e, t) {
  return e ? !t : t;
}
function Qf(e, t, n, o, r, i, s, a) {
  let { affectsX: l, affectsY: c } = t;
  const { isHorizontal: d, isVertical: f } = t, u = d && f, { xSnapped: m, ySnapped: p } = n, { minWidth: y, maxWidth: w, minHeight: x, maxHeight: S } = o, { x: g, y: v, width: _, height: E, aspectRatio: M } = e;
  let D = Math.floor(d ? m - e.pointerX : 0), j = Math.floor(f ? p - e.pointerY : 0);
  const F = _ + (l ? -D : D), C = E + (c ? -j : j), R = -i[0] * _, H = -i[1] * E;
  let b = gn(F, y, w), k = gn(C, x, S);
  if (s) {
    let $ = 0, P = 0;
    l && D < 0 ? $ = Je(g + D + R, s[0][0]) : !l && D > 0 && ($ = et(g + F + R, s[1][0])), c && j < 0 ? P = Je(v + j + H, s[0][1]) : !c && j > 0 && (P = et(v + C + H, s[1][1])), b = Math.max(b, $), k = Math.max(k, P);
  }
  if (a) {
    let $ = 0, P = 0;
    l && D > 0 ? $ = et(g + D, a[0][0]) : !l && D < 0 && ($ = Je(g + F, a[1][0])), c && j > 0 ? P = et(v + j, a[0][1]) : !c && j < 0 && (P = Je(v + C, a[1][1])), b = Math.max(b, $), k = Math.max(k, P);
  }
  if (r) {
    if (d) {
      const $ = gn(F / M, x, S) * M;
      if (b = Math.max(b, $), s) {
        let P = 0;
        !l && !c || l && !c && u ? P = et(v + H + F / M, s[1][1]) * M : P = Je(v + H + (l ? D : -D) / M, s[0][1]) * M, b = Math.max(b, P);
      }
      if (a) {
        let P = 0;
        !l && !c || l && !c && u ? P = Je(v + F / M, a[1][1]) * M : P = et(v + (l ? D : -D) / M, a[0][1]) * M, b = Math.max(b, P);
      }
    }
    if (f) {
      const $ = gn(C * M, y, w) / M;
      if (k = Math.max(k, $), s) {
        let P = 0;
        !l && !c || c && !l && u ? P = et(g + C * M + R, s[1][0]) / M : P = Je(g + (c ? j : -j) * M + R, s[0][0]) / M, k = Math.max(k, P);
      }
      if (a) {
        let P = 0;
        !l && !c || c && !l && u ? P = Je(g + C * M, a[1][0]) / M : P = et(g + (c ? j : -j) * M, a[0][0]) / M, k = Math.max(k, P);
      }
    }
  }
  j = j + (j < 0 ? k : -k), D = D + (D < 0 ? b : -b), r && (u ? F > C * M ? j = (ai(l, c) ? -D : D) / M : D = (ai(l, c) ? -j : j) * M : d ? (j = D / M, c = l) : (D = j * M, l = c));
  const N = l ? g + D : g, A = c ? v + j : v;
  return {
    width: _ + (l ? -D : D),
    height: E + (c ? -j : j),
    x: i[0] * D * (l ? -1 : 1) + N,
    y: i[1] * j * (c ? -1 : 1) + A
  };
}
const Ks = { width: 0, height: 0, x: 0, y: 0 }, Jf = {
  ...Ks,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function eh(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, i = e.measured.width ?? 0, s = e.measured.height ?? 0, a = n[0] * i, l = n[1] * s;
  return [
    [o - a, r - l],
    [o + i - a, r + s - l]
  ];
}
function th({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const i = Ae(e);
  let s = {
    controlDirection: si("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function a({ controlPosition: c, boundaries: d, keepAspectRatio: f, resizeDirection: u, onResizeStart: m, onResize: p, onResizeEnd: y, shouldResize: w }) {
    let x = { ...Ks }, S = { ...Jf };
    s = {
      boundaries: d,
      resizeDirection: u,
      keepAspectRatio: f,
      controlDirection: si(c)
    };
    let g, v = null, _ = [], E, M, D, j = !1;
    const F = is().on("start", (C) => {
      const { nodeLookup: R, transform: H, snapGrid: b, snapToGrid: k, nodeOrigin: N, paneDomNode: A } = n();
      if (g = R.get(t), !g)
        return;
      v = A?.getBoundingClientRect() ?? null;
      const { xSnapped: $, ySnapped: P } = Bt(C.sourceEvent, {
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
        pointerX: $,
        pointerY: P,
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
          const O = eh(V, g, V.origin ?? N);
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
      const { x: $, y: P, width: B, height: V } = x, O = {}, U = g.origin ?? k, { width: K, height: X, x: ie, y: G } = Qf(S, s.controlDirection, N, s.boundaries, s.keepAspectRatio, U, M, D), T = K !== B, Z = X !== V, re = ie !== $ && T, oe = G !== P && Z;
      if (!re && !oe && !T && !Z)
        return;
      if ((re || oe || U[0] === 1 || U[1] === 1) && (O.x = re ? ie : x.x, O.y = oe ? G : x.y, x.x = O.x, x.y = O.y, _.length > 0)) {
        const L = ie - $, W = G - P;
        for (const fe of _)
          fe.position = {
            x: fe.position.x - L + U[0] * (K - B),
            y: fe.position.y - W + U[1] * (X - V)
          }, A.push(fe);
      }
      if ((T || Z) && (O.width = T && (!s.resizeDirection || s.resizeDirection === "horizontal") ? K : x.width, O.height = Z && (!s.resizeDirection || s.resizeDirection === "vertical") ? X : x.height, x.width = O.width, x.height = O.height), E && g.expandParent) {
        const L = U[0] * (O.width ?? 0);
        O.x && O.x < L && (x.x = L, S.x = S.x - (O.x - L));
        const W = U[1] * (O.height ?? 0);
        O.y && O.y < W && (x.y = W, S.y = S.y - (O.y - W));
      }
      const Q = Gf({
        width: x.width,
        prevWidth: B,
        height: x.height,
        prevHeight: V,
        affectsX: s.controlDirection.affectsX,
        affectsY: s.controlDirection.affectsY
      }), te = { ...x, direction: Q };
      w?.(C, te) !== !1 && (j = !0, p?.(C, te), o(O, A));
    }).on("end", (C) => {
      j && (y?.(C, { ...x }), r?.({ ...x }), j = !1);
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
var _o = { exports: {} }, No = {}, Co = { exports: {} }, ko = {};
var ci;
function nh() {
  if (ci) return ko;
  ci = 1;
  var e = ot;
  function t(f, u) {
    return f === u && (f !== 0 || 1 / f === 1 / u) || f !== f && u !== u;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, i = e.useLayoutEffect, s = e.useDebugValue;
  function a(f, u) {
    var m = u(), p = o({ inst: { value: m, getSnapshot: u } }), y = p[0].inst, w = p[1];
    return i(
      function() {
        y.value = m, y.getSnapshot = u, l(y) && w({ inst: y });
      },
      [f, m, u]
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
    var u = f.getSnapshot;
    f = f.value;
    try {
      var m = u();
      return !n(f, m);
    } catch {
      return !0;
    }
  }
  function c(f, u) {
    return u();
  }
  var d = typeof window > "u" || typeof window.document > "u" || typeof window.document.createElement > "u" ? c : a;
  return ko.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : d, ko;
}
var li;
function oh() {
  return li || (li = 1, Co.exports = nh()), Co.exports;
}
var ui;
function rh() {
  if (ui) return No;
  ui = 1;
  var e = ot, t = oh();
  function n(c, d) {
    return c === d && (c !== 0 || 1 / c === 1 / d) || c !== c && d !== d;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, i = e.useRef, s = e.useEffect, a = e.useMemo, l = e.useDebugValue;
  return No.useSyncExternalStoreWithSelector = function(c, d, f, u, m) {
    var p = i(null);
    if (p.current === null) {
      var y = { hasValue: !1, value: null };
      p.current = y;
    } else y = p.current;
    p = a(
      function() {
        function x(E) {
          if (!S) {
            if (S = !0, g = E, E = u(E), m !== void 0 && y.hasValue) {
              var M = y.value;
              if (m(M, E))
                return v = M;
            }
            return v = E;
          }
          if (M = v, o(g, E)) return M;
          var D = u(E);
          return m !== void 0 && m(M, D) ? (g = E, M) : (g = E, v = D);
        }
        var S = !1, g, v, _ = f === void 0 ? null : f;
        return [
          function() {
            return x(d());
          },
          _ === null ? void 0 : function() {
            return x(_());
          }
        ];
      },
      [d, f, u, m]
    );
    var w = r(c, p[0], p[1]);
    return s(
      function() {
        y.hasValue = !0, y.value = w;
      },
      [w]
    ), l(w), w;
  }, No;
}
var di;
function ih() {
  return di || (di = 1, _o.exports = rh()), _o.exports;
}
var sh = ih();
const ah = /* @__PURE__ */ Sc(sh), ch = {}, fi = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (d, f) => {
    const u = typeof d == "function" ? d(t) : d;
    if (!Object.is(u, t)) {
      const m = t;
      t = f ?? (typeof u != "object" || u === null) ? u : Object.assign({}, t, u), n.forEach((p) => p(t, m));
    }
  }, r = () => t, l = { setState: o, getState: r, getInitialState: () => c, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (ch ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, c = t = e(o, r, l);
  return l;
}, lh = (e) => e ? fi(e) : fi, { useDebugValue: uh } = ot, { useSyncExternalStoreWithSelector: dh } = ah, fh = (e) => e;
function Gs(e, t = fh, n) {
  const o = dh(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return uh(o), o;
}
const hi = (e, t) => {
  const n = lh(e), o = (r, i = t) => Gs(n, r, i);
  return Object.assign(o, n), o;
}, hh = (e, t) => e ? hi(e, t) : hi;
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
var Io = { exports: {} }, be = {};
var pi;
function ph() {
  if (pi) return be;
  pi = 1;
  var e = ot;
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
    var f = 3 < arguments.length && arguments[3] !== void 0 ? arguments[3] : null;
    return {
      $$typeof: r,
      key: f == null ? null : "" + f,
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
  return be.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, be.createPortal = function(l, c) {
    var d = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!c || c.nodeType !== 1 && c.nodeType !== 9 && c.nodeType !== 11)
      throw Error(t(299));
    return i(l, c, null, d);
  }, be.flushSync = function(l) {
    var c = s.T, d = o.p;
    try {
      if (s.T = null, o.p = 2, l) return l();
    } finally {
      s.T = c, o.p = d, o.d.f();
    }
  }, be.preconnect = function(l, c) {
    typeof l == "string" && (c ? (c = c.crossOrigin, c = typeof c == "string" ? c === "use-credentials" ? c : "" : void 0) : c = null, o.d.C(l, c));
  }, be.prefetchDNS = function(l) {
    typeof l == "string" && o.d.D(l);
  }, be.preinit = function(l, c) {
    if (typeof l == "string" && c && typeof c.as == "string") {
      var d = c.as, f = a(d, c.crossOrigin), u = typeof c.integrity == "string" ? c.integrity : void 0, m = typeof c.fetchPriority == "string" ? c.fetchPriority : void 0;
      d === "style" ? o.d.S(
        l,
        typeof c.precedence == "string" ? c.precedence : void 0,
        {
          crossOrigin: f,
          integrity: u,
          fetchPriority: m
        }
      ) : d === "script" && o.d.X(l, {
        crossOrigin: f,
        integrity: u,
        fetchPriority: m,
        nonce: typeof c.nonce == "string" ? c.nonce : void 0
      });
    }
  }, be.preinitModule = function(l, c) {
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
  }, be.preload = function(l, c) {
    if (typeof l == "string" && typeof c == "object" && c !== null && typeof c.as == "string") {
      var d = c.as, f = a(d, c.crossOrigin);
      o.d.L(l, d, {
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
  }, be.preloadModule = function(l, c) {
    if (typeof l == "string")
      if (c) {
        var d = a(c.as, c.crossOrigin);
        o.d.m(l, {
          as: typeof c.as == "string" && c.as !== "script" ? c.as : void 0,
          crossOrigin: d,
          integrity: typeof c.integrity == "string" ? c.integrity : void 0
        });
      } else o.d.m(l);
  }, be.requestFormReset = function(l) {
    o.d.r(l);
  }, be.unstable_batchedUpdates = function(l, c) {
    return l(c);
  }, be.useFormState = function(l, c, d) {
    return s.H.useFormState(l, c, d);
  }, be.useFormStatus = function() {
    return s.H.useHostTransitionStatus();
  }, be.version = "19.2.7", be;
}
var gi;
function gh() {
  if (gi) return Io.exports;
  gi = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), Io.exports = ph(), Io.exports;
}
var mh = gh();
const Zn = Ko(null), yh = Zn.Provider, Qs = je.error001("react");
function le(e, t) {
  const n = Jt(Zn);
  if (n === null)
    throw new Error(Qs);
  return Gs(n, e, t);
}
function ge() {
  const e = Jt(Zn);
  if (e === null)
    throw new Error(Qs);
  return ye(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const mi = { display: "none" }, xh = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, Js = "react-flow__node-desc", ea = "react-flow__edge-desc", wh = "react-flow__aria-live", vh = (e) => e.ariaLiveMessage, bh = (e) => e.ariaLabelConfig;
function Sh({ rfId: e }) {
  const t = le(vh);
  return h.jsx("div", { id: `${wh}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: xh, children: t });
}
function Eh({ rfId: e, disableKeyboardA11y: t }) {
  const n = le(bh);
  return h.jsxs(h.Fragment, { children: [h.jsx("div", { id: `${Js}-${e}`, style: mi, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), h.jsx("div", { id: `${ea}-${e}`, style: mi, children: n["edge.a11yDescription.default"] }), !t && h.jsx(Sh, { rfId: e })] });
}
const Un = Hn(({ position: e = "top-left", children: t, className: n, style: o, ...r }, i) => {
  const s = `${e}`.split("-");
  return h.jsx("div", { className: we(["react-flow__panel", n, ...s]), style: o, ref: i, ...r, children: t });
});
Un.displayName = "Panel";
function _h({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : h.jsx(Un, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: h.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const Nh = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, mn = (e) => e.id;
function Ch(e, t) {
  return pe(e.selectedNodes.map(mn), t.selectedNodes.map(mn)) && pe(e.selectedEdges.map(mn), t.selectedEdges.map(mn));
}
function kh({ onSelectionChange: e }) {
  const t = ge(), { selectedNodes: n, selectedEdges: o } = le(Nh, Ch);
  return ce(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((i) => i(r));
  }, [n, o, e]), null;
}
const Ih = (e) => !!e.onSelectionChangeHandlers;
function Mh({ onSelectionChange: e }) {
  const t = le(Ih);
  return e || t ? h.jsx(kh, { onSelectionChange: e }) : null;
}
const ta = [0, 0], Ah = { x: 0, y: 0, zoom: 1 }, Dh = [
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
], yi = [...Dh, "rfId"], jh = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), xi = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: qt,
  nodeOrigin: ta,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function Ph(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: i, setNodeExtent: s, reset: a, setDefaultNodesAndEdges: l } = le(jh, pe), c = ge();
  ce(() => (l(e.defaultNodes, e.defaultEdges), () => {
    d.current = xi, a();
  }), []);
  const d = ae(xi);
  return ce(
    () => {
      for (const f of yi) {
        const u = e[f], m = d.current[f];
        u !== m && (typeof e[f] > "u" || (f === "nodes" ? t(u) : f === "edges" ? n(u) : f === "minZoom" ? o(u) : f === "maxZoom" ? r(u) : f === "translateExtent" ? i(u) : f === "nodeExtent" ? s(u) : f === "ariaLabelConfig" ? c.setState({ ariaLabelConfig: pf(u) }) : f === "fitView" ? c.setState({ fitViewQueued: u }) : f === "fitViewOptions" ? c.setState({ fitViewOptions: u }) : c.setState({ [f]: u })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    yi.map((f) => e[f])
  ), null;
}
function wi() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function $h(e) {
  const [t, n] = ne(e === "system" ? null : e);
  return ce(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = wi(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : wi()?.matches ? "dark" : "light";
}
const vi = typeof document < "u" ? document : null;
function Gt(e = null, t = { target: vi, actInsideInputWithModifier: !0 }) {
  const [n, o] = ne(!1), r = ae(!1), i = ae(/* @__PURE__ */ new Set([])), [s, a] = ye(() => {
    if (e !== null) {
      const c = (Array.isArray(e) ? e : [e]).filter((f) => typeof f == "string").map((f) => f.replace("+", `
`).replace(`

`, `
+`).split(`
`)), d = c.reduce((f, u) => f.concat(...u), []);
      return [c, d];
    }
    return [[], []];
  }, [e]);
  return ce(() => {
    const l = t?.target ?? vi, c = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (m) => {
        if (r.current = m.ctrlKey || m.metaKey || m.shiftKey || m.altKey, (!r.current || r.current && !c) && Ps(m))
          return !1;
        const y = Si(m.code, a);
        if (i.current.add(m[y]), bi(s, i.current, !1)) {
          const w = m.composedPath?.()?.[0] || m.target, x = w?.nodeName === "BUTTON" || w?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !x) && m.preventDefault(), o(!0);
        }
      }, f = (m) => {
        const p = Si(m.code, a);
        bi(s, i.current, !0) ? (o(!1), i.current.clear()) : i.current.delete(m[p]), m.key === "Meta" && i.current.clear(), r.current = !1;
      }, u = () => {
        i.current.clear(), o(!1);
      };
      return l?.addEventListener("keydown", d), l?.addEventListener("keyup", f), window.addEventListener("blur", u), window.addEventListener("contextmenu", u), () => {
        l?.removeEventListener("keydown", d), l?.removeEventListener("keyup", f), window.removeEventListener("blur", u), window.removeEventListener("contextmenu", u);
      };
    }
  }, [e, o]), n;
}
function bi(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function Si(e, t) {
  return t.includes(e) ? "code" : "key";
}
const Th = () => {
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
      const { width: o, height: r, minZoom: i, maxZoom: s, panZoom: a } = e.getState(), l = ar(t, o, r, i, s, n?.padding ?? 0.1);
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
      }, d = n.snapGrid ?? r, f = n.snapToGrid ?? i;
      return Dt(c, o, f, d);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: r, y: i } = o.getBoundingClientRect(), s = Nt(t, n);
      return {
        x: s.x + r,
        y: s.y + i
      };
    }
  }), []);
};
function na(e, t) {
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
      Rh(l, a);
    n.push(a);
  }
  return r.length && r.forEach((i) => {
    i.index !== void 0 ? n.splice(i.index, 0, { ...i.item }) : n.push({ ...i.item });
  }), n;
}
function Rh(e, t) {
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
function oa(e, t) {
  return na(e, t);
}
function ra(e, t) {
  return na(e, t);
}
function st(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function yt(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [r, i] of e) {
    const s = t.has(r);
    !(i.selected === void 0 && !s) && i.selected !== s && (n && (i.selected = s), o.push(st(i.id, s)));
  }
  return o;
}
function Ei({ items: e = [], lookup: t }) {
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
const ia = Ms();
function sa(e, t, n = {}) {
  return wf(e, t, {
    ...n,
    onError: n.onError ?? ia
  });
}
function zh(e, t, n, o = { shouldReplaceId: !0 }) {
  return vf(e, t, n, {
    ...o,
    onError: o.onError ?? ia
  });
}
const Ni = (e) => rf(e), Lh = (e) => Ns(e);
function aa(e) {
  return Hn(e);
}
const Hh = typeof window < "u" ? bc : ce;
function Ci(e) {
  const [t, n] = ne(BigInt(0)), [o] = ne(() => Vh(() => n((r) => r + BigInt(1))));
  return Hh(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function Vh(e) {
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
const ca = Ko(null);
function Oh({ children: e }) {
  const t = ge(), n = he((a) => {
    const { nodes: l = [], setNodes: c, hasDefaultNodes: d, onNodesChange: f, nodeLookup: u, fitViewQueued: m, onNodesChangeMiddlewareMap: p } = t.getState();
    let y = l;
    for (const x of a)
      y = typeof x == "function" ? x(y) : x;
    let w = Ei({
      items: y,
      lookup: u
    });
    for (const x of p.values())
      w = x(w);
    d && c(y), w.length > 0 ? f?.(w) : m && window.requestAnimationFrame(() => {
      const { fitViewQueued: x, nodes: S, setNodes: g } = t.getState();
      x && g(S);
    });
  }, []), o = Ci(n), r = he((a) => {
    const { edges: l = [], setEdges: c, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: u } = t.getState();
    let m = l;
    for (const p of a)
      m = typeof p == "function" ? p(m) : p;
    d ? c(m) : f && f(Ei({
      items: m,
      lookup: u
    }));
  }, []), i = Ci(r), s = ye(() => ({ nodeQueue: o, edgeQueue: i }), []);
  return h.jsx(ca.Provider, { value: s, children: e });
}
function Bh() {
  const e = Jt(ca);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Fh = (e) => !!e.panZoom;
function pr() {
  const e = Th(), t = ge(), n = Bh(), o = le(Fh), r = ye(() => {
    const i = (f) => t.getState().nodeLookup.get(f), s = (f) => {
      n.nodeQueue.push(f);
    }, a = (f) => {
      n.edgeQueue.push(f);
    }, l = (f) => {
      const { nodeLookup: u, nodeOrigin: m } = t.getState(), p = Ni(f) ? f : u.get(f.id), y = p.parentId ? Ds(p.position, p.measured, p.parentId, u, m) : p.position, w = {
        ...p,
        position: y,
        width: p.measured?.width ?? p.width,
        height: p.measured?.height ?? p.height
      };
      return _t(w);
    }, c = (f, u, m = { replace: !1 }) => {
      s((p) => p.map((y) => {
        if (y.id === f) {
          const w = typeof u == "function" ? u(y) : u;
          return m.replace && Ni(w) ? w : { ...y, ...w };
        }
        return y;
      }));
    }, d = (f, u, m = { replace: !1 }) => {
      a((p) => p.map((y) => {
        if (y.id === f) {
          const w = typeof u == "function" ? u(y) : u;
          return m.replace && Lh(w) ? w : { ...y, ...w };
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
        return f.map((u) => ({ ...u }));
      },
      getEdge: (f) => t.getState().edgeLookup.get(f),
      setNodes: s,
      setEdges: a,
      addNodes: (f) => {
        const u = Array.isArray(f) ? f : [f];
        n.nodeQueue.push((m) => [...m, ...u]);
      },
      addEdges: (f) => {
        const u = Array.isArray(f) ? f : [f];
        n.edgeQueue.push((m) => [...m, ...u]);
      },
      toObject: () => {
        const { nodes: f = [], edges: u = [], transform: m } = t.getState(), [p, y, w] = m;
        return {
          nodes: f.map((x) => ({ ...x })),
          edges: u.map((x) => ({ ...x })),
          viewport: {
            x: p,
            y,
            zoom: w
          }
        };
      },
      deleteElements: async ({ nodes: f = [], edges: u = [] }) => {
        const { nodes: m, edges: p, onNodesDelete: y, onEdgesDelete: w, triggerNodeChanges: x, triggerEdgeChanges: S, onDelete: g, onBeforeDelete: v } = t.getState(), { nodes: _, edges: E } = await uf({
          nodesToRemove: f,
          edgesToRemove: u,
          nodes: m,
          edges: p,
          onBeforeDelete: v
        }), M = E.length > 0, D = _.length > 0;
        if (M) {
          const j = E.map(_i);
          w?.(E), S(j);
        }
        if (D) {
          const j = _.map(_i);
          y?.(_), x(j);
        }
        return (D || M) && g?.({ nodes: _, edges: E }), { deletedNodes: _, deletedEdges: E };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, u = !0, m) => {
        const p = Ur(f), y = p ? f : l(f), w = m !== void 0;
        return y ? (m || t.getState().nodes).filter((x) => {
          const S = t.getState().nodeLookup.get(x.id);
          if (S && !p && (x.id === f.id || !S.internals.positionAbsolute))
            return !1;
          const g = _t(w ? x : S), v = Ut(g, y);
          return u && v > 0 || v >= g.width * g.height || v >= y.width * y.height;
        }) : [];
      },
      isNodeIntersecting: (f, u, m = !0) => {
        const y = Ur(f) ? f : l(f);
        if (!y)
          return !1;
        const w = Ut(y, u);
        return m && w > 0 || w >= u.width * u.height || w >= y.width * y.height;
      },
      updateNode: c,
      updateNodeData: (f, u, m = { replace: !1 }) => {
        c(f, (p) => {
          const y = typeof u == "function" ? u(p) : u;
          return m.replace ? { ...p, data: y } : { ...p, data: { ...p.data, ...y } };
        }, m);
      },
      updateEdge: d,
      updateEdgeData: (f, u, m = { replace: !1 }) => {
        d(f, (p) => {
          const y = typeof u == "function" ? u(p) : u;
          return m.replace ? { ...p, data: y } : { ...p, data: { ...p.data, ...y } };
        }, m);
      },
      getNodesBounds: (f) => {
        const { nodeLookup: u, nodeOrigin: m } = t.getState();
        return sf(f, { nodeLookup: u, nodeOrigin: m });
      },
      getHandleConnections: ({ type: f, id: u, nodeId: m }) => Array.from(t.getState().connectionLookup.get(`${m}-${f}${u ? `-${u}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: u, nodeId: m }) => Array.from(t.getState().connectionLookup.get(`${m}${f ? u ? `-${f}-${u}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const u = t.getState().fitViewResolver ?? hf();
        return t.setState({ fitViewQueued: !0, fitViewOptions: f, fitViewResolver: u }), n.nodeQueue.push((m) => [...m]), u.promise;
      }
    };
  }, []);
  return ye(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const ki = (e) => e.selected, Yh = typeof window < "u" ? window : void 0;
function Xh({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = ge(), { deleteElements: o } = pr(), r = Gt(e, { actInsideInputWithModifier: !1 }), i = Gt(t, { target: Yh });
  ce(() => {
    if (r) {
      const { edges: s, nodes: a } = n.getState();
      o({ nodes: a.filter(ki), edges: s.filter(ki) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), ce(() => {
    n.setState({ multiSelectionActive: i });
  }, [i]);
}
function Wh(e) {
  const t = ge();
  ce(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = cr(e.current);
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
const Kn = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, qh = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function Zh({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: i = lt.Free, zoomOnDoubleClick: s = !0, panOnDrag: a = !0, defaultViewport: l, translateExtent: c, minZoom: d, maxZoom: f, zoomActivationKeyCode: u, preventScrolling: m = !0, children: p, noWheelClassName: y, noPanClassName: w, onViewportChange: x, isControlledViewport: S, paneClickDistance: g, selectionOnDrag: v }) {
  const _ = ge(), E = ae(null), { userSelectionActive: M, lib: D, connectionInProgress: j } = le(qh, pe), F = Gt(u), C = ae();
  Wh(E);
  const R = he((H) => {
    x?.({ x: H[0], y: H[1], zoom: H[2] }), S || _.setState({ transform: H });
  }, [x, S]);
  return ce(() => {
    if (E.current) {
      C.current = Kf({
        domNode: E.current,
        minZoom: d,
        maxZoom: f,
        translateExtent: c,
        viewport: l,
        onDraggingChange: (N) => _.setState((A) => A.paneDragging === N ? A : { paneDragging: N }),
        onPanZoomStart: (N, A) => {
          const { onViewportChangeStart: $, onMoveStart: P } = _.getState();
          P?.(N, A), $?.(A);
        },
        onPanZoom: (N, A) => {
          const { onViewportChange: $, onMove: P } = _.getState();
          P?.(N, A), $?.(A);
        },
        onPanZoomEnd: (N, A) => {
          const { onViewportChangeEnd: $, onMoveEnd: P } = _.getState();
          P?.(N, A), $?.(A);
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
      connectionInProgress: j,
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
    j,
    v,
    g
  ]), h.jsx("div", { className: "react-flow__renderer", ref: E, style: Kn, children: p });
}
const Uh = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function Kh() {
  const { userSelectionActive: e, userSelectionRect: t } = le(Uh, pe);
  return e && t ? h.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const Mo = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, Gh = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function Qh({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Zt.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: i, selectionOnDrag: s, onSelectionStart: a, onSelectionEnd: l, onPaneClick: c, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: u, onPaneMouseMove: m, onPaneMouseLeave: p, children: y }) {
  const w = ae(0), x = ge(), { userSelectionActive: S, elementsSelectable: g, dragging: v, connectionInProgress: _, panBy: E, autoPanSpeed: M } = le(Gh, pe), D = g && (e || S), j = ae(null), F = ae(), C = ae(/* @__PURE__ */ new Set()), R = ae(/* @__PURE__ */ new Set()), H = ae(!1), b = ae({ x: 0, y: 0 }), k = ae(!1), N = (T) => {
    if (H.current || _) {
      H.current = !1;
      return;
    }
    c?.(T), x.getState().resetSelectedElements(), x.setState({ nodesSelectionActive: !1 });
  }, A = (T) => {
    if (Array.isArray(o) && o?.includes(2)) {
      T.preventDefault();
      return;
    }
    d?.(T);
  }, $ = f ? (T) => f(T) : void 0, P = (T) => {
    H.current && (T.stopPropagation(), H.current = !1);
  }, B = (T) => {
    const { domNode: Z, transform: re } = x.getState();
    if (F.current = Z?.getBoundingClientRect(), !F.current)
      return;
    const oe = T.target === j.current;
    if (!oe && !!T.target.closest(".nokey") || !e || !(s && oe || t) || T.button !== 0 || !T.isPrimary)
      return;
    T.target?.setPointerCapture?.(T.pointerId), H.current = !1;
    const { x: se, y: L } = He(T.nativeEvent, F.current), W = Dt({ x: se, y: L }, re);
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
    const { transform: oe, nodeLookup: Q, edgeLookup: te, connectionLookup: se, triggerNodeChanges: L, triggerEdgeChanges: W, defaultEdgeOptions: fe } = x.getState(), me = { x: re.startX, y: re.startY }, { x: Ne, y: Se } = Nt(me, oe), Te = {
      startX: me.x,
      startY: me.y,
      x: T < Ne ? T : Ne,
      y: Z < Se ? Z : Se,
      width: Math.abs(T - Ne),
      height: Math.abs(Z - Se)
    }, rt = C.current, We = R.current;
    C.current = new Set(ir(Q, Te, oe, n === Zt.Partial, !0).map((Ce) => Ce.id)), R.current = /* @__PURE__ */ new Set();
    const qe = fe?.selectable ?? !0;
    for (const Ce of C.current) {
      const Pe = se.get(Ce);
      if (Pe)
        for (const { edgeId: $e } of Pe.values()) {
          const Ze = te.get($e);
          Ze && (Ze.selectable ?? qe) && R.current.add($e);
        }
    }
    if (!Kr(rt, C.current)) {
      const Ce = yt(Q, C.current, !0);
      L(Ce);
    }
    if (!Kr(We, R.current)) {
      const Ce = yt(te, R.current);
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
    const [T, Z] = sr(b.current, F.current, M);
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
    cancelAnimationFrame(w.current), w.current = 0, k.current = !1;
  };
  ce(() => () => U(), []);
  const K = (T) => {
    const { userSelectionRect: Z, transform: re, resetSelectedElements: oe } = x.getState();
    if (!F.current || !Z)
      return;
    const { x: Q, y: te } = He(T.nativeEvent, F.current);
    b.current = { x: Q, y: te };
    const se = Nt({ x: Z.startX, y: Z.startY }, re);
    if (!H.current) {
      const L = t ? 0 : i;
      if (Math.hypot(Q - se.x, te - se.y) <= L)
        return;
      oe(), a?.(T);
    }
    H.current = !0, k.current || (O(), k.current = !0), V(Q, te);
  }, X = (T) => {
    T.button === 0 && (T.target?.releasePointerCapture?.(T.pointerId), !S && T.target === j.current && x.getState().userSelectionRect && N?.(T), x.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), H.current && (l?.(T), x.setState({
      nodesSelectionActive: C.current.size > 0
    })), U());
  }, ie = (T) => {
    T.target?.releasePointerCapture?.(T.pointerId), U();
  }, G = o === !0 || Array.isArray(o) && o.includes(0);
  return h.jsxs("div", { className: we(["react-flow__pane", { draggable: G, dragging: v, selection: e }]), onClick: D ? void 0 : Mo(N, j), onContextMenu: Mo(A, j), onWheel: Mo($, j), onPointerEnter: D ? void 0 : u, onPointerMove: D ? K : m, onPointerUp: D ? X : void 0, onPointerCancel: D ? ie : void 0, onPointerDownCapture: D ? B : void 0, onClickCapture: D ? P : void 0, onPointerLeave: p, ref: j, style: Kn, children: [y, h.jsx(Kh, {})] });
}
function Wo({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: i, multiSelectionActive: s, nodeLookup: a, onError: l } = t.getState(), c = a.get(e);
  if (!c) {
    l?.("012", je.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), c.selected ? (n || c.selected && s) && (i({ nodes: [c], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function la({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: i, nodeClickDistance: s }) {
  const a = ge(), [l, c] = ne(!1), d = ae();
  return ce(() => {
    d.current = Rf({
      getStoreItems: () => a.getState(),
      onNodeMouseDown: (f) => {
        Wo({
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
const Jh = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function ua() {
  const e = ge();
  return he((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: i, nodesDraggable: s, onError: a, updateNodePositions: l, nodeLookup: c, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), u = Jh(s), m = r ? i[0] : 5, p = r ? i[1] : 5, y = n.direction.x * m * n.factor, w = n.direction.y * p * n.factor;
    for (const [, x] of c) {
      if (!u(x))
        continue;
      let S = {
        x: x.internals.positionAbsolute.x + y,
        y: x.internals.positionAbsolute.y + w
      };
      r && (S = rn(S, i));
      const { position: g, positionAbsolute: v } = Cs({
        nodeId: x.id,
        nextPosition: S,
        nodeLookup: c,
        nodeExtent: o,
        nodeOrigin: d,
        onError: a
      });
      x.position = g, x.internals.positionAbsolute = v, f.set(x.id, x);
    }
    l(f);
  }, []);
}
const gr = Ko(null), ep = gr.Provider;
gr.Consumer;
const da = () => Jt(gr), tp = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), np = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: i, connection: s } = o, { fromHandle: a, toHandle: l, isValid: c } = s, d = l?.nodeId === e && l?.id === t && l?.type === n;
  return {
    connectingFrom: a?.nodeId === e && a?.id === t && a?.type === n,
    connectingTo: d,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: i === St.Strict ? a?.type !== n : e !== a?.nodeId || t !== a?.id,
    connectionInProcess: !!a,
    clickConnectionInProcess: !!r,
    valid: d && c
  };
};
function op({ type: e = "source", position: t = J.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: i = !0, id: s, onConnect: a, children: l, className: c, onMouseDown: d, onTouchStart: f, ...u }, m) {
  const p = s || null, y = e === "target", w = ge(), x = da(), { connectOnClick: S, noPanClassName: g, rfId: v } = le(tp, pe), { connectingFrom: _, connectingTo: E, clickConnecting: M, isPossibleEndHandle: D, connectionInProcess: j, clickConnectionInProcess: F, valid: C } = le(np(x, p, e), pe);
  x || w.getState().onError?.("010", je.error010());
  const R = (k) => {
    const { defaultEdgeOptions: N, onConnect: A, hasDefaultEdges: $ } = w.getState(), P = {
      ...N,
      ...k
    };
    if ($) {
      const { edges: B, setEdges: V, onError: O } = w.getState();
      V(sa(P, B, { onError: O }));
    }
    A?.(P), a?.(P);
  }, H = (k) => {
    if (!x)
      return;
    const N = $s(k.nativeEvent);
    if (r && (N && k.button === 0 || !N)) {
      const A = w.getState();
      Xo.onPointerDown(k.nativeEvent, {
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
        onConnectEnd: (...$) => w.getState().onConnectEnd?.(...$),
        updateConnection: A.updateConnection,
        onConnect: R,
        isValidConnection: n || ((...$) => w.getState().isValidConnection?.(...$) ?? !0),
        getTransform: () => w.getState().transform,
        getFromHandle: () => w.getState().connection.fromHandle,
        autoPanSpeed: A.autoPanSpeed,
        dragThreshold: A.connectionDragThreshold
      });
    }
    N ? d?.(k) : f?.(k);
  }, b = (k) => {
    const { onClickConnectStart: N, onClickConnectEnd: A, connectionClickStartHandle: $, connectionMode: P, isValidConnection: B, lib: V, rfId: O, nodeLookup: U, connection: K } = w.getState();
    if (!x || !$ && !r)
      return;
    if (!$) {
      N?.(k.nativeEvent, { nodeId: x, handleId: p, handleType: e }), w.setState({ connectionClickStartHandle: { nodeId: x, type: e, id: p } });
      return;
    }
    const X = js(k.target), ie = n || B, { connection: G, isValid: T } = Xo.isValid(k.nativeEvent, {
      handle: {
        nodeId: x,
        id: p,
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
    T && G && R(G);
    const Z = structuredClone(K);
    delete Z.inProgress, Z.toPosition = Z.toHandle ? Z.toHandle.position : null, A?.(k, Z), w.setState({ connectionClickStartHandle: null });
  };
  return h.jsx("div", { "data-handleid": p, "data-nodeid": x, "data-handlepos": t, "data-id": `${v}-${x}-${p}-${e}`, className: we([
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
      connectionindicator: o && (!j || D) && (j || F ? i : r)
    }
  ]), onMouseDown: H, onTouchStart: H, onClick: S ? b : void 0, ref: m, ...u, children: l });
}
const kt = xe(aa(op));
function rp({ data: e, isConnectable: t, sourcePosition: n = J.Bottom }) {
  return h.jsxs(h.Fragment, { children: [e?.label, h.jsx(kt, { type: "source", position: n, isConnectable: t })] });
}
function ip({ data: e, isConnectable: t, targetPosition: n = J.Top, sourcePosition: o = J.Bottom }) {
  return h.jsxs(h.Fragment, { children: [h.jsx(kt, { type: "target", position: n, isConnectable: t }), e?.label, h.jsx(kt, { type: "source", position: o, isConnectable: t })] });
}
function sp() {
  return null;
}
function ap({ data: e, isConnectable: t, targetPosition: n = J.Top }) {
  return h.jsxs(h.Fragment, { children: [h.jsx(kt, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const Tn = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, Ii = {
  input: rp,
  default: ip,
  output: ap,
  group: sp
};
function cp(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const lp = (e) => {
  const { width: t, height: n, x: o, y: r } = on(e.nodeLookup, {
    filter: (i) => !!i.selected
  });
  return {
    width: Le(t) ? t : null,
    height: Le(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function up({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = ge(), { width: r, height: i, transformString: s, userSelectionActive: a } = le(lp, pe), l = ua(), c = ae(null);
  ce(() => {
    n || c.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !a && r !== null && i !== null;
  if (la({
    nodeRef: c,
    disabled: !d
  }), !d)
    return null;
  const f = e ? (m) => {
    const p = o.getState().nodes.filter((y) => y.selected);
    e(m, p);
  } : void 0, u = (m) => {
    Object.prototype.hasOwnProperty.call(Tn, m.key) && (m.preventDefault(), l({
      direction: Tn[m.key],
      factor: m.shiftKey ? 4 : 1
    }));
  };
  return h.jsx("div", { className: we(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: s
  }, children: h.jsx("div", { ref: c, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : u, style: {
    width: r,
    height: i
  } }) });
}
const Mi = typeof window < "u" ? window : void 0, dp = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function fa({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, paneClickDistance: a, deleteKeyCode: l, selectionKeyCode: c, selectionOnDrag: d, selectionMode: f, onSelectionStart: u, onSelectionEnd: m, multiSelectionKeyCode: p, panActivationKeyCode: y, zoomActivationKeyCode: w, elementsSelectable: x, zoomOnScroll: S, zoomOnPinch: g, panOnScroll: v, panOnScrollSpeed: _, panOnScrollMode: E, zoomOnDoubleClick: M, panOnDrag: D, autoPanOnSelection: j, defaultViewport: F, translateExtent: C, minZoom: R, maxZoom: H, preventScrolling: b, onSelectionContextMenu: k, noWheelClassName: N, noPanClassName: A, disableKeyboardA11y: $, onViewportChange: P, isControlledViewport: B }) {
  const { nodesSelectionActive: V, userSelectionActive: O } = le(dp, pe), U = Gt(c, { target: Mi }), K = Gt(y, { target: Mi }), X = K || D, ie = K || v, G = d && X !== !0, T = U || O || G;
  return Xh({ deleteKeyCode: l, multiSelectionKeyCode: p }), h.jsx(Zh, { onPaneContextMenu: i, elementsSelectable: x, zoomOnScroll: S, zoomOnPinch: g, panOnScroll: ie, panOnScrollSpeed: _, panOnScrollMode: E, zoomOnDoubleClick: M, panOnDrag: !U && X, defaultViewport: F, translateExtent: C, minZoom: R, maxZoom: H, zoomActivationKeyCode: w, preventScrolling: b, noWheelClassName: N, noPanClassName: A, onViewportChange: P, isControlledViewport: B, paneClickDistance: a, selectionOnDrag: G, children: h.jsxs(Qh, { onSelectionStart: u, onSelectionEnd: m, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, panOnDrag: X, autoPanOnSelection: j, isSelecting: !!T, selectionMode: f, selectionKeyPressed: U, paneClickDistance: a, selectionOnDrag: G, children: [e, V && h.jsx(up, { onSelectionContextMenu: k, noPanClassName: A, disableKeyboardA11y: $ })] }) });
}
fa.displayName = "FlowRenderer";
const fp = xe(fa), hp = (e) => (t) => e ? ir(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function pp(e) {
  return le(he(hp(e), [e]), pe);
}
const gp = (e) => e.updateNodeInternals;
function mp() {
  const e = le(gp), [t] = ne(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
function yp({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = ge(), i = ae(null), s = ae(null), a = ae(e.sourcePosition), l = ae(e.targetPosition), c = ae(t), d = n && !!e.internals.handleBounds;
  return ce(() => {
    i.current && !e.hidden && (!d || s.current !== i.current) && (s.current && o?.unobserve(s.current), o?.observe(i.current), s.current = i.current);
  }, [d, e.hidden]), ce(() => () => {
    s.current && (o?.unobserve(s.current), s.current = null);
  }, []), ce(() => {
    if (i.current) {
      const f = c.current !== t, u = a.current !== e.sourcePosition, m = l.current !== e.targetPosition;
      (f || u || m) && (c.current = t, a.current = e.sourcePosition, l.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: i.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), i;
}
function xp({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: i, onDoubleClick: s, nodesDraggable: a, elementsSelectable: l, nodesConnectable: c, nodesFocusable: d, resizeObserver: f, noDragClassName: u, noPanClassName: m, disableKeyboardA11y: p, rfId: y, nodeTypes: w, nodeClickDistance: x, onError: S }) {
  const { node: g, internals: v, isParent: _ } = le((T) => {
    const Z = T.nodeLookup.get(e), re = T.parentLookup.has(e);
    return {
      node: Z,
      internals: Z.internals,
      isParent: re
    };
  }, pe);
  let E = g.type || "default", M = w?.[E] || Ii[E];
  M === void 0 && (S?.("003", je.error003(E)), E = "default", M = w?.default || Ii.default);
  const D = !!(g.draggable || a && typeof g.draggable > "u"), j = !!(g.selectable || l && typeof g.selectable > "u"), F = !!(g.connectable || c && typeof g.connectable > "u"), C = !!(g.focusable || d && typeof g.focusable > "u"), R = ge(), H = As(g), b = yp({ node: g, nodeType: E, hasDimensions: H, resizeObserver: f }), k = la({
    nodeRef: b,
    disabled: g.hidden || !D,
    noDragClassName: u,
    handleSelector: g.dragHandle,
    nodeId: e,
    isSelectable: j,
    nodeClickDistance: x
  }), N = ua();
  if (g.hidden)
    return null;
  const A = Qe(g), $ = cp(g), P = j || D || t || n || o || r, B = n ? (T) => n(T, { ...v.userNode }) : void 0, V = o ? (T) => o(T, { ...v.userNode }) : void 0, O = r ? (T) => r(T, { ...v.userNode }) : void 0, U = i ? (T) => i(T, { ...v.userNode }) : void 0, K = s ? (T) => s(T, { ...v.userNode }) : void 0, X = (T) => {
    const { selectNodesOnDrag: Z, nodeDragThreshold: re } = R.getState();
    j && (!Z || !D || re > 0) && Wo({
      id: e,
      store: R,
      nodeRef: b
    }), t && t(T, { ...v.userNode });
  }, ie = (T) => {
    if (!(Ps(T.nativeEvent) || p)) {
      if (bs.includes(T.key) && j) {
        const Z = T.key === "Escape";
        Wo({
          id: e,
          store: R,
          unselect: Z,
          nodeRef: b
        });
      } else if (D && g.selected && Object.prototype.hasOwnProperty.call(Tn, T.key)) {
        T.preventDefault();
        const { ariaLabelConfig: Z } = R.getState();
        R.setState({
          ariaLiveMessage: Z["node.a11yDescription.ariaLiveMessage"]({
            direction: T.key.replace("Arrow", "").toLowerCase(),
            x: ~~v.positionAbsolute.x,
            y: ~~v.positionAbsolute.y
          })
        }), N({
          direction: Tn[T.key],
          factor: T.shiftKey ? 4 : 1
        });
      }
    }
  }, G = () => {
    if (p || !b.current?.matches(":focus-visible"))
      return;
    const { transform: T, width: Z, height: re, autoPanOnNodeFocus: oe, setCenter: Q } = R.getState();
    if (!oe)
      return;
    ir(/* @__PURE__ */ new Map([[e, g]]), { x: 0, y: 0, width: Z, height: re }, T, !0).length > 0 || Q(g.position.x + A.width / 2, g.position.y + A.height / 2, {
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
    g.className,
    {
      selected: g.selected,
      selectable: j,
      parent: _,
      draggable: D,
      dragging: k
    }
  ]), ref: b, style: {
    zIndex: v.z,
    transform: `translate(${v.positionAbsolute.x}px,${v.positionAbsolute.y}px)`,
    pointerEvents: P ? "all" : "none",
    visibility: H ? "visible" : "hidden",
    ...g.style,
    ...$
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: B, onMouseMove: V, onMouseLeave: O, onContextMenu: U, onClick: X, onDoubleClick: K, onKeyDown: C ? ie : void 0, tabIndex: C ? 0 : void 0, onFocus: C ? G : void 0, role: g.ariaRole ?? (C ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": p ? void 0 : `${Js}-${y}`, "aria-label": g.ariaLabel, ...g.domAttributes, children: h.jsx(ep, { value: e, children: h.jsx(M, { id: e, data: g.data, type: E, positionAbsoluteX: v.positionAbsolute.x, positionAbsoluteY: v.positionAbsolute.y, selected: g.selected ?? !1, selectable: j, draggable: D, deletable: g.deletable ?? !0, isConnectable: F, sourcePosition: g.sourcePosition, targetPosition: g.targetPosition, dragging: k, dragHandle: g.dragHandle, zIndex: v.z, parentId: g.parentId, ...A }) }) });
}
var wp = xe(xp);
const vp = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function ha(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: i } = le(vp, pe), s = pp(e.onlyRenderVisibleElements), a = mp();
  return h.jsx("div", { className: "react-flow__nodes", style: Kn, children: s.map((l) => (
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
    h.jsx(wp, { id: l, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: a, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: i }, l)
  )) });
}
ha.displayName = "NodeRenderer";
const bp = xe(ha);
function Sp(e) {
  return le(he((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const i = n.nodeLookup.get(r.source), s = n.nodeLookup.get(r.target);
        i && s && yf({
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
const Ep = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return h.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, _p = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return h.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Ai = {
  [jn.Arrow]: Ep,
  [jn.ArrowClosed]: _p
};
function Np(e) {
  const t = ge();
  return ye(() => Object.prototype.hasOwnProperty.call(Ai, e) ? Ai[e] : (t.getState().onError?.("009", je.error009(e)), null), [e]);
}
const Cp = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: i = "strokeWidth", strokeWidth: s, orient: a = "auto-start-reverse" }) => {
  const l = Np(t);
  return l ? h.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: i, orient: a, refX: "0", refY: "0", children: h.jsx(l, { color: n, strokeWidth: s }) }) : null;
}, pa = ({ defaultColor: e, rfId: t }) => {
  const n = le((i) => i.edges), o = le((i) => i.defaultEdgeOptions), r = ye(() => Nf(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? h.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: h.jsx("defs", { children: r.map((i) => h.jsx(Cp, { id: i.id, type: i.type, color: i.color, width: i.width, height: i.height, markerUnits: i.markerUnits, strokeWidth: i.strokeWidth, orient: i.orient }, i.id)) }) }) : null;
};
pa.displayName = "MarkerDefinitions";
var kp = xe(pa);
function ga({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: i, labelBgPadding: s = [2, 4], labelBgBorderRadius: a = 2, children: l, className: c, ...d }) {
  const [f, u] = ne({ x: 1, y: 0, width: 0, height: 0 }), m = we(["react-flow__edge-textwrapper", c]), p = ae(null);
  return ce(() => {
    if (p.current) {
      const y = p.current.getBBox();
      u({
        x: y.x,
        y: y.y,
        width: y.width,
        height: y.height
      });
    }
  }, [n]), n ? h.jsxs("g", { transform: `translate(${e - f.width / 2} ${t - f.height / 2})`, className: m, visibility: f.width ? "visible" : "hidden", ...d, children: [r && h.jsx("rect", { width: f.width + 2 * s[0], x: -s[0], y: -s[1], height: f.height + 2 * s[1], className: "react-flow__edge-textbg", style: i, rx: a, ry: a }), h.jsx("text", { className: "react-flow__edge-text", y: f.height / 2, dy: "0.3em", ref: p, style: o, children: n }), l] }) : null;
}
ga.displayName = "EdgeText";
const Ip = xe(ga);
function sn({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: a, labelBgBorderRadius: l, interactionWidth: c = 20, ...d }) {
  return h.jsxs(h.Fragment, { children: [h.jsx("path", { ...d, d: e, fill: "none", className: we(["react-flow__edge-path", d.className]) }), c ? h.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: c, className: "react-flow__edge-interaction" }) : null, o && Le(t) && Le(n) ? h.jsx(Ip, { x: t, y: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: a, labelBgBorderRadius: l }) : null] });
}
function Di({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === J.Left || e === J.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function ma({ sourceX: e, sourceY: t, sourcePosition: n = J.Bottom, targetX: o, targetY: r, targetPosition: i = J.Top }) {
  const [s, a] = Di({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [l, c] = Di({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [d, f, u, m] = Ts({
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
    f,
    u,
    m
  ];
}
function ya(e) {
  return xe(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: a, label: l, labelStyle: c, labelShowBg: d, labelBgStyle: f, labelBgPadding: u, labelBgBorderRadius: m, style: p, markerEnd: y, markerStart: w, interactionWidth: x }) => {
    const [S, g, v] = ma({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: a
    }), _ = e.isInternal ? void 0 : t;
    return h.jsx(sn, { id: _, path: S, labelX: g, labelY: v, label: l, labelStyle: c, labelShowBg: d, labelBgStyle: f, labelBgPadding: u, labelBgBorderRadius: m, style: p, markerEnd: y, markerStart: w, interactionWidth: x });
  });
}
const Mp = ya({ isInternal: !1 }), xa = ya({ isInternal: !0 });
Mp.displayName = "SimpleBezierEdge";
xa.displayName = "SimpleBezierEdgeInternal";
function wa(e) {
  return xe(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: d, labelBgBorderRadius: f, style: u, sourcePosition: m = J.Bottom, targetPosition: p = J.Top, markerEnd: y, markerStart: w, pathOptions: x, interactionWidth: S }) => {
    const [g, v, _] = $n({
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
    return h.jsx(sn, { id: E, path: g, labelX: v, labelY: _, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: d, labelBgBorderRadius: f, style: u, markerEnd: y, markerStart: w, interactionWidth: S });
  });
}
const va = wa({ isInternal: !1 }), ba = wa({ isInternal: !0 });
va.displayName = "SmoothStepEdge";
ba.displayName = "SmoothStepEdgeInternal";
function Sa(e) {
  return xe(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return h.jsx(va, { ...n, id: o, pathOptions: ye(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Ap = Sa({ isInternal: !1 }), Ea = Sa({ isInternal: !0 });
Ap.displayName = "StepEdge";
Ea.displayName = "StepEdgeInternal";
function _a(e) {
  return xe(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: d, labelBgBorderRadius: f, style: u, markerEnd: m, markerStart: p, interactionWidth: y }) => {
    const [w, x, S] = Hs({ sourceX: n, sourceY: o, targetX: r, targetY: i }), g = e.isInternal ? void 0 : t;
    return h.jsx(sn, { id: g, path: w, labelX: x, labelY: S, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: d, labelBgBorderRadius: f, style: u, markerEnd: m, markerStart: p, interactionWidth: y });
  });
}
const Dp = _a({ isInternal: !1 }), Na = _a({ isInternal: !0 });
Dp.displayName = "StraightEdge";
Na.displayName = "StraightEdgeInternal";
function Ca(e) {
  return xe(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s = J.Bottom, targetPosition: a = J.Top, label: l, labelStyle: c, labelShowBg: d, labelBgStyle: f, labelBgPadding: u, labelBgBorderRadius: m, style: p, markerEnd: y, markerStart: w, pathOptions: x, interactionWidth: S }) => {
    const [g, v, _] = Rs({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: a,
      curvature: x?.curvature
    }), E = e.isInternal ? void 0 : t;
    return h.jsx(sn, { id: E, path: g, labelX: v, labelY: _, label: l, labelStyle: c, labelShowBg: d, labelBgStyle: f, labelBgPadding: u, labelBgBorderRadius: m, style: p, markerEnd: y, markerStart: w, interactionWidth: S });
  });
}
const jp = Ca({ isInternal: !1 }), ka = Ca({ isInternal: !0 });
jp.displayName = "BezierEdge";
ka.displayName = "BezierEdgeInternal";
const ji = {
  default: ka,
  straight: Na,
  step: Ea,
  smoothstep: ba,
  simplebezier: xa
}, Pi = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, Pp = (e, t, n) => n === J.Left ? e - t : n === J.Right ? e + t : e, $p = (e, t, n) => n === J.Top ? e - t : n === J.Bottom ? e + t : e, $i = "react-flow__edgeupdater";
function Ti({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: i, onMouseOut: s, type: a }) {
  return h.jsx("circle", { onMouseDown: r, onMouseEnter: i, onMouseOut: s, className: we([$i, `${$i}-${a}`]), cx: Pp(t, o, e), cy: $p(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function Tp({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: i, targetY: s, sourcePosition: a, targetPosition: l, onReconnect: c, onReconnectStart: d, onReconnectEnd: f, setReconnecting: u, setUpdateHover: m }) {
  const p = ge(), y = (v, _) => {
    if (v.button !== 0)
      return;
    const { autoPanOnConnect: E, domNode: M, connectionMode: D, connectionRadius: j, lib: F, onConnectStart: C, cancelConnection: R, nodeLookup: H, rfId: b, panBy: k, updateConnection: N } = p.getState(), A = _.type === "target", $ = (V, O) => {
      u(!1), f?.(V, n, _.type, O);
    }, P = (V) => c?.(n, V), B = (V, O) => {
      u(!0), d?.(v, n, _.type), C?.(V, O);
    };
    Xo.onPointerDown(v.nativeEvent, {
      autoPanOnConnect: E,
      connectionMode: D,
      connectionRadius: j,
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
      onConnect: P,
      onConnectStart: B,
      onConnectEnd: (...V) => p.getState().onConnectEnd?.(...V),
      onReconnectEnd: $,
      updateConnection: N,
      getTransform: () => p.getState().transform,
      getFromHandle: () => p.getState().connection.fromHandle,
      dragThreshold: p.getState().connectionDragThreshold,
      handleDomNode: v.currentTarget
    });
  }, w = (v) => y(v, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), x = (v) => y(v, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), S = () => m(!0), g = () => m(!1);
  return h.jsxs(h.Fragment, { children: [(e === !0 || e === "source") && h.jsx(Ti, { position: a, centerX: o, centerY: r, radius: t, onMouseDown: w, onMouseEnter: S, onMouseOut: g, type: "source" }), (e === !0 || e === "target") && h.jsx(Ti, { position: l, centerX: i, centerY: s, radius: t, onMouseDown: x, onMouseEnter: S, onMouseOut: g, type: "target" })] });
}
function Rp({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: i, onContextMenu: s, onMouseEnter: a, onMouseMove: l, onMouseLeave: c, reconnectRadius: d, onReconnect: f, onReconnectStart: u, onReconnectEnd: m, rfId: p, edgeTypes: y, noPanClassName: w, onError: x, disableKeyboardA11y: S }) {
  let g = le((Q) => Q.edgeLookup.get(e));
  const v = le((Q) => Q.defaultEdgeOptions);
  g = v ? { ...v, ...g } : g;
  let _ = g.type || "default", E = y?.[_] || ji[_];
  E === void 0 && (x?.("011", je.error011(_)), _ = "default", E = y?.default || ji.default);
  const M = !!(g.focusable || t && typeof g.focusable > "u"), D = typeof f < "u" && (g.reconnectable || n && typeof g.reconnectable > "u"), j = !!(g.selectable || o && typeof g.selectable > "u"), F = ae(null), [C, R] = ne(!1), [H, b] = ne(!1), k = ge(), { zIndex: N, sourceX: A, sourceY: $, targetX: P, targetY: B, sourcePosition: V, targetPosition: O } = le(he((Q) => {
    const te = Q.nodeLookup.get(g.source), se = Q.nodeLookup.get(g.target);
    if (!te || !se)
      return {
        zIndex: g.zIndex,
        ...Pi
      };
    const L = _f({
      id: e,
      sourceNode: te,
      targetNode: se,
      sourceHandle: g.sourceHandle || null,
      targetHandle: g.targetHandle || null,
      connectionMode: Q.connectionMode,
      onError: x
    });
    return {
      zIndex: mf({
        selected: g.selected,
        zIndex: g.zIndex,
        sourceNode: te,
        targetNode: se,
        elevateOnSelect: Q.elevateEdgesOnSelect,
        zIndexMode: Q.zIndexMode
      }),
      ...L || Pi
    };
  }, [g.source, g.target, g.sourceHandle, g.targetHandle, g.selected, g.zIndex]), pe), U = ye(() => g.markerStart ? `url('#${Fo(g.markerStart, p)}')` : void 0, [g.markerStart, p]), K = ye(() => g.markerEnd ? `url('#${Fo(g.markerEnd, p)}')` : void 0, [g.markerEnd, p]);
  if (g.hidden || A === null || $ === null || P === null || B === null)
    return null;
  const X = (Q) => {
    const { addSelectedEdges: te, unselectNodesAndEdges: se, multiSelectionActive: L } = k.getState();
    j && (k.setState({ nodesSelectionActive: !1 }), g.selected && L ? (se({ nodes: [], edges: [g] }), F.current?.blur()) : te([e])), r && r(Q, g);
  }, ie = i ? (Q) => {
    i(Q, { ...g });
  } : void 0, G = s ? (Q) => {
    s(Q, { ...g });
  } : void 0, T = a ? (Q) => {
    a(Q, { ...g });
  } : void 0, Z = l ? (Q) => {
    l(Q, { ...g });
  } : void 0, re = c ? (Q) => {
    c(Q, { ...g });
  } : void 0, oe = (Q) => {
    if (!S && bs.includes(Q.key) && j) {
      const { unselectNodesAndEdges: te, addSelectedEdges: se } = k.getState();
      Q.key === "Escape" ? (F.current?.blur(), te({ edges: [g] })) : se([e]);
    }
  };
  return h.jsx("svg", { style: { zIndex: N }, children: h.jsxs("g", { className: we([
    "react-flow__edge",
    `react-flow__edge-${_}`,
    g.className,
    w,
    {
      selected: g.selected,
      animated: g.animated,
      inactive: !j && !r,
      updating: C,
      selectable: j
    }
  ]), onClick: X, onDoubleClick: ie, onContextMenu: G, onMouseEnter: T, onMouseMove: Z, onMouseLeave: re, onKeyDown: M ? oe : void 0, tabIndex: M ? 0 : void 0, role: g.ariaRole ?? (M ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": g.ariaLabel === null ? void 0 : g.ariaLabel || `Edge from ${g.source} to ${g.target}`, "aria-describedby": M ? `${ea}-${p}` : void 0, ref: F, ...g.domAttributes, children: [!H && h.jsx(E, { id: e, source: g.source, target: g.target, type: g.type, selected: g.selected, animated: g.animated, selectable: j, deletable: g.deletable ?? !0, label: g.label, labelStyle: g.labelStyle, labelShowBg: g.labelShowBg, labelBgStyle: g.labelBgStyle, labelBgPadding: g.labelBgPadding, labelBgBorderRadius: g.labelBgBorderRadius, sourceX: A, sourceY: $, targetX: P, targetY: B, sourcePosition: V, targetPosition: O, data: g.data, style: g.style, sourceHandleId: g.sourceHandle, targetHandleId: g.targetHandle, markerStart: U, markerEnd: K, pathOptions: "pathOptions" in g ? g.pathOptions : void 0, interactionWidth: g.interactionWidth }), D && h.jsx(Tp, { edge: g, isReconnectable: D, reconnectRadius: d, onReconnect: f, onReconnectStart: u, onReconnectEnd: m, sourceX: A, sourceY: $, targetX: P, targetY: B, sourcePosition: V, targetPosition: O, setUpdateHover: R, setReconnecting: b })] }) });
}
var zp = xe(Rp);
const Lp = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function Ia({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: i, onEdgeContextMenu: s, onEdgeMouseEnter: a, onEdgeMouseMove: l, onEdgeMouseLeave: c, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: u, onReconnectStart: m, onReconnectEnd: p, disableKeyboardA11y: y }) {
  const { edgesFocusable: w, edgesReconnectable: x, elementsSelectable: S, onError: g } = le(Lp, pe), v = Sp(t);
  return h.jsxs("div", { className: "react-flow__edges", children: [h.jsx(kp, { defaultColor: e, rfId: n }), v.map((_) => h.jsx(zp, { id: _, edgesFocusable: w, edgesReconnectable: x, elementsSelectable: S, noPanClassName: r, onReconnect: i, onContextMenu: s, onMouseEnter: a, onMouseMove: l, onMouseLeave: c, onClick: d, reconnectRadius: f, onDoubleClick: u, onReconnectStart: m, onReconnectEnd: p, rfId: n, onError: g, edgeTypes: o, disableKeyboardA11y: y }, _))] });
}
Ia.displayName = "EdgeRenderer";
const Hp = xe(Ia), Vp = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function Op({ children: e }) {
  const t = le(Vp);
  return h.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function Bp(e) {
  const t = pr(), n = ae(!1);
  ce(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Fp = (e) => e.panZoom?.syncViewport;
function Yp(e) {
  const t = le(Fp), n = ge();
  return ce(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function Xp(e) {
  return e.connection.inProgress ? { ...e.connection, to: Dt(e.connection.to, e.transform) } : { ...e.connection };
}
function Wp(e) {
  return Xp;
}
function qp(e) {
  const t = Wp();
  return le(t, pe);
}
const Zp = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function Up({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: i, height: s, isValid: a, inProgress: l } = le(Zp, pe);
  return !(i && r && l) ? null : h.jsx("svg", { style: e, width: i, height: s, className: "react-flow__connectionline react-flow__container", children: h.jsx("g", { className: we(["react-flow__connection", _s(a)]), children: h.jsx(Ma, { style: t, type: n, CustomComponent: o, isValid: a }) }) });
}
const Ma = ({ style: e, type: t = tt.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: i, fromNode: s, fromHandle: a, fromPosition: l, to: c, toNode: d, toHandle: f, toPosition: u, pointer: m } = qp();
  if (!r)
    return;
  if (n)
    return h.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: s, fromHandle: a, fromX: i.x, fromY: i.y, toX: c.x, toY: c.y, fromPosition: l, toPosition: u, connectionStatus: _s(o), toNode: d, toHandle: f, pointer: m });
  let p = "";
  const y = {
    sourceX: i.x,
    sourceY: i.y,
    sourcePosition: l,
    targetX: c.x,
    targetY: c.y,
    targetPosition: u
  };
  switch (t) {
    case tt.Bezier:
      [p] = Rs(y);
      break;
    case tt.SimpleBezier:
      [p] = ma(y);
      break;
    case tt.Step:
      [p] = $n({
        ...y,
        borderRadius: 0
      });
      break;
    case tt.SmoothStep:
      [p] = $n(y);
      break;
    default:
      [p] = Hs(y);
  }
  return h.jsx("path", { d: p, fill: "none", className: "react-flow__connection-path", style: e });
};
Ma.displayName = "ConnectionLine";
const Kp = {};
function Ri(e = Kp) {
  ae(e), ge(), ce(() => {
  }, [e]);
}
function Gp() {
  ge(), ae(!1), ce(() => {
  }, []);
}
function Aa({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: i, onEdgeDoubleClick: s, onNodeMouseEnter: a, onNodeMouseMove: l, onNodeMouseLeave: c, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: u, onSelectionEnd: m, connectionLineType: p, connectionLineStyle: y, connectionLineComponent: w, connectionLineContainerStyle: x, selectionKeyCode: S, selectionOnDrag: g, selectionMode: v, multiSelectionKeyCode: _, panActivationKeyCode: E, zoomActivationKeyCode: M, deleteKeyCode: D, onlyRenderVisibleElements: j, elementsSelectable: F, defaultViewport: C, translateExtent: R, minZoom: H, maxZoom: b, preventScrolling: k, defaultMarkerColor: N, zoomOnScroll: A, zoomOnPinch: $, panOnScroll: P, panOnScrollSpeed: B, panOnScrollMode: V, zoomOnDoubleClick: O, panOnDrag: U, autoPanOnSelection: K, onPaneClick: X, onPaneMouseEnter: ie, onPaneMouseMove: G, onPaneMouseLeave: T, onPaneScroll: Z, onPaneContextMenu: re, paneClickDistance: oe, nodeClickDistance: Q, onEdgeContextMenu: te, onEdgeMouseEnter: se, onEdgeMouseMove: L, onEdgeMouseLeave: W, reconnectRadius: fe, onReconnect: me, onReconnectStart: Ne, onReconnectEnd: Se, noDragClassName: Te, noWheelClassName: rt, noPanClassName: We, disableKeyboardA11y: qe, nodeExtent: Ce, rfId: Pe, viewport: $e, onViewportChange: Ze }) {
  return Ri(e), Ri(t), Gp(), Bp(n), Yp($e), h.jsx(fp, { onPaneClick: X, onPaneMouseEnter: ie, onPaneMouseMove: G, onPaneMouseLeave: T, onPaneContextMenu: re, onPaneScroll: Z, paneClickDistance: oe, deleteKeyCode: D, selectionKeyCode: S, selectionOnDrag: g, selectionMode: v, onSelectionStart: u, onSelectionEnd: m, multiSelectionKeyCode: _, panActivationKeyCode: E, zoomActivationKeyCode: M, elementsSelectable: F, zoomOnScroll: A, zoomOnPinch: $, zoomOnDoubleClick: O, panOnScroll: P, panOnScrollSpeed: B, panOnScrollMode: V, panOnDrag: U, autoPanOnSelection: K, defaultViewport: C, translateExtent: R, minZoom: H, maxZoom: b, onSelectionContextMenu: f, preventScrolling: k, noDragClassName: Te, noWheelClassName: rt, noPanClassName: We, disableKeyboardA11y: qe, onViewportChange: Ze, isControlledViewport: !!$e, children: h.jsxs(Op, { children: [h.jsx(Hp, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: s, onReconnect: me, onReconnectStart: Ne, onReconnectEnd: Se, onlyRenderVisibleElements: j, onEdgeContextMenu: te, onEdgeMouseEnter: se, onEdgeMouseMove: L, onEdgeMouseLeave: W, reconnectRadius: fe, defaultMarkerColor: N, noPanClassName: We, disableKeyboardA11y: qe, rfId: Pe }), h.jsx(Up, { style: y, type: p, component: w, containerStyle: x }), h.jsx("div", { className: "react-flow__edgelabel-renderer" }), h.jsx(bp, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: i, onNodeMouseEnter: a, onNodeMouseMove: l, onNodeMouseLeave: c, onNodeContextMenu: d, nodeClickDistance: Q, onlyRenderVisibleElements: j, noPanClassName: We, noDragClassName: Te, disableKeyboardA11y: qe, nodeExtent: Ce, rfId: Pe }), h.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Aa.displayName = "GraphView";
const Qp = xe(Aa), Jp = Ms(), zi = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: a, minZoom: l = 0.5, maxZoom: c = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: u = "basic" } = {}) => {
  const m = /* @__PURE__ */ new Map(), p = /* @__PURE__ */ new Map(), y = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), x = o ?? t ?? [], S = n ?? e ?? [], g = d ?? [0, 0], v = f ?? qt;
  Bs(y, w, x);
  const { nodesInitialized: _ } = Yo(S, m, p, {
    nodeOrigin: g,
    nodeExtent: v,
    zIndexMode: u
  });
  let E = [0, 0, 1];
  if (s && r && i) {
    const M = on(m, {
      filter: (C) => !!((C.width || C.initialWidth) && (C.height || C.initialHeight))
    }), { x: D, y: j, zoom: F } = ar(M, r, i, l, c, a?.padding ?? 0.1);
    E = [D, j, F];
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
    connectionMode: St.Strict,
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
    connection: { ...Es },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: Jp,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Ss,
    zIndexMode: u,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, eg = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: a, minZoom: l, maxZoom: c, nodeOrigin: d, nodeExtent: f, zIndexMode: u }) => hh((m, p) => {
  async function y() {
    const { nodeLookup: w, panZoom: x, fitViewOptions: S, fitViewResolver: g, width: v, height: _, minZoom: E, maxZoom: M } = p();
    x && (await lf({
      nodes: w,
      width: v,
      height: _,
      panZoom: x,
      minZoom: E,
      maxZoom: M
    }, S), g?.resolve(!0), m({ fitViewResolver: null }));
  }
  return {
    ...zi({
      nodes: e,
      edges: t,
      width: r,
      height: i,
      fitView: s,
      fitViewOptions: a,
      minZoom: l,
      maxZoom: c,
      nodeOrigin: d,
      nodeExtent: f,
      defaultNodes: n,
      defaultEdges: o,
      zIndexMode: u
    }),
    setNodes: (w) => {
      const { nodeLookup: x, parentLookup: S, nodeOrigin: g, elevateNodesOnSelect: v, fitViewQueued: _, zIndexMode: E, nodesSelectionActive: M } = p(), { nodesInitialized: D, hasSelectedNodes: j } = Yo(w, x, S, {
        nodeOrigin: g,
        nodeExtent: f,
        elevateNodesOnSelect: v,
        checkEquality: !0,
        zIndexMode: E
      }), F = M && j;
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
      Bs(x, S, w), m({ edges: w });
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
      const { triggerNodeChanges: x, nodeLookup: S, parentLookup: g, domNode: v, nodeOrigin: _, nodeExtent: E, debug: M, fitViewQueued: D, zIndexMode: j } = p(), { changes: F, updatedInternals: C } = jf(w, S, g, v, _, E, j);
      C && (If(S, g, { nodeOrigin: _, nodeExtent: E, zIndexMode: j }), D ? (y(), m({ fitViewQueued: !1, fitViewOptions: void 0 })) : m({}), F?.length > 0 && (M && console.log("React Flow: trigger node changes", F), x?.(F)));
    },
    updateNodePositions: (w, x = !1) => {
      const S = [];
      let g = [];
      const { nodeLookup: v, triggerNodeChanges: _, connection: E, updateConnection: M, onNodesChangeMiddlewareMap: D } = p();
      for (const [j, F] of w) {
        const C = v.get(j), R = !!(C?.expandParent && C?.parentId && F?.position), H = {
          id: j,
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
          id: j,
          parentId: C.parentId,
          rect: {
            ...F.internals.positionAbsolute,
            width: F.measured.width ?? 0,
            height: F.measured.height ?? 0
          }
        }), g.push(H);
      }
      if (S.length > 0) {
        const { parentLookup: j, nodeOrigin: F } = p(), C = hr(S, v, j, F);
        g.push(...C);
      }
      for (const j of D.values())
        g = j(g);
      _(g);
    },
    triggerNodeChanges: (w) => {
      const { onNodesChange: x, setNodes: S, nodes: g, hasDefaultNodes: v, debug: _ } = p();
      if (w?.length) {
        if (v) {
          const E = oa(w, g);
          S(E);
        }
        _ && console.log("React Flow: trigger node changes", w), x?.(w);
      }
    },
    triggerEdgeChanges: (w) => {
      const { onEdgesChange: x, setEdges: S, edges: g, hasDefaultEdges: v, debug: _ } = p();
      if (w?.length) {
        if (v) {
          const E = ra(w, g);
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
      v(yt(g, /* @__PURE__ */ new Set([...w]), !0)), _(yt(S));
    },
    addSelectedEdges: (w) => {
      const { multiSelectionActive: x, edgeLookup: S, nodeLookup: g, triggerNodeChanges: v, triggerEdgeChanges: _ } = p();
      if (x) {
        const E = w.map((M) => st(M, !0));
        _(E);
        return;
      }
      _(yt(S, /* @__PURE__ */ new Set([...w]))), v(yt(g, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: w, edges: x } = {}) => {
      const { edges: S, nodes: g, nodeLookup: v, triggerNodeChanges: _, triggerEdgeChanges: E } = p(), M = w || g, D = x || S, j = [];
      for (const C of M) {
        if (!C.selected)
          continue;
        const R = v.get(C.id);
        R && (R.selected = !1), j.push(st(C.id, !1));
      }
      const F = [];
      for (const C of D)
        C.selected && F.push(st(C.id, !1));
      _(j), E(F);
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
      w[0][0] === E[0][0] && w[0][1] === E[0][1] && w[1][0] === E[1][0] && w[1][1] === E[1][1] || (Yo(x, S, g, {
        nodeOrigin: v,
        nodeExtent: w,
        elevateNodesOnSelect: _,
        checkEquality: !1,
        zIndexMode: M
      }), m({ nodeExtent: w }));
    },
    panBy: (w) => {
      const { transform: x, width: S, height: g, panZoom: v, translateExtent: _ } = p();
      return Pf({ delta: w, panZoom: v, transform: x, translateExtent: _, width: S, height: g });
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
        connection: { ...Es }
      });
    },
    updateConnection: (w) => {
      m({ connection: w });
    },
    reset: () => m({ ...zi() })
  };
}, Object.is);
function tg({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: i, initialMinZoom: s, initialMaxZoom: a, initialFitViewOptions: l, fitView: c, nodeOrigin: d, nodeExtent: f, zIndexMode: u, children: m }) {
  const [p] = ne(() => eg({
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
    nodeExtent: f,
    zIndexMode: u
  }));
  return h.jsx(yh, { value: p, children: h.jsx(Oh, { children: m }) });
}
function ng({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: i, height: s, fitView: a, fitViewOptions: l, minZoom: c, maxZoom: d, nodeOrigin: f, nodeExtent: u, zIndexMode: m }) {
  return Jt(Zn) ? h.jsx(h.Fragment, { children: e }) : h.jsx(tg, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: i, initialHeight: s, fitView: a, initialFitViewOptions: l, initialMinZoom: c, initialMaxZoom: d, nodeOrigin: f, nodeExtent: u, zIndexMode: m, children: e });
}
const og = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function rg({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: i, edgeTypes: s, onNodeClick: a, onEdgeClick: l, onInit: c, onMove: d, onMoveStart: f, onMoveEnd: u, onConnect: m, onConnectStart: p, onConnectEnd: y, onClickConnectStart: w, onClickConnectEnd: x, onNodeMouseEnter: S, onNodeMouseMove: g, onNodeMouseLeave: v, onNodeContextMenu: _, onNodeDoubleClick: E, onNodeDragStart: M, onNodeDrag: D, onNodeDragStop: j, onNodesDelete: F, onEdgesDelete: C, onDelete: R, onSelectionChange: H, onSelectionDragStart: b, onSelectionDrag: k, onSelectionDragStop: N, onSelectionContextMenu: A, onSelectionStart: $, onSelectionEnd: P, onBeforeDelete: B, connectionMode: V, connectionLineType: O = tt.Bezier, connectionLineStyle: U, connectionLineComponent: K, connectionLineContainerStyle: X, deleteKeyCode: ie = "Backspace", selectionKeyCode: G = "Shift", selectionOnDrag: T = !1, selectionMode: Z = Zt.Full, panActivationKeyCode: re = "Space", multiSelectionKeyCode: oe = Kt() ? "Meta" : "Control", zoomActivationKeyCode: Q = Kt() ? "Meta" : "Control", snapToGrid: te, snapGrid: se, onlyRenderVisibleElements: L = !1, selectNodesOnDrag: W, nodesDraggable: fe, autoPanOnNodeFocus: me, nodesConnectable: Ne, nodesFocusable: Se, nodeOrigin: Te = ta, edgesFocusable: rt, edgesReconnectable: We, elementsSelectable: qe = !0, defaultViewport: Ce = Ah, minZoom: Pe = 0.5, maxZoom: $e = 2, translateExtent: Ze = qt, preventScrolling: jt = !0, nodeExtent: Pt, defaultMarkerColor: Jn = "#b1b1b7", zoomOnScroll: eo = !0, zoomOnPinch: to = !0, panOnScroll: no = !1, panOnScrollSpeed: $t = 0.5, panOnScrollMode: oo = lt.Free, zoomOnDoubleClick: ro = !0, panOnDrag: io = !0, onPaneClick: so, onPaneMouseEnter: ao, onPaneMouseMove: co, onPaneMouseLeave: lo, onPaneScroll: an, onPaneContextMenu: cn, paneClickDistance: uo = 1, nodeClickDistance: fo = 0, children: ho, onReconnect: po, onReconnectStart: I, onReconnectEnd: z, onEdgeContextMenu: Y, onEdgeDoubleClick: q, onEdgeMouseEnter: ee, onEdgeMouseMove: de, onEdgeMouseLeave: ue, reconnectRadius: ve = 10, onNodesChange: Me, onEdgesChange: it, noDragClassName: Ee = "nodrag", noWheelClassName: qa = "nowheel", noPanClassName: vr = "nopan", fitView: br, fitViewOptions: Sr, connectOnClick: Za, attributionPosition: Ua, proOptions: Ka, defaultEdgeOptions: Ga, elevateNodesOnSelect: Qa = !0, elevateEdgesOnSelect: Ja = !1, disableKeyboardA11y: Er = !1, autoPanOnConnect: ec, autoPanOnNodeDrag: tc, autoPanOnSelection: nc = !0, autoPanSpeed: oc, connectionRadius: rc, isValidConnection: ic, onError: sc, style: ac, id: _r, nodeDragThreshold: cc, connectionDragThreshold: lc, viewport: uc, onViewportChange: dc, width: fc, height: hc, colorMode: pc = "light", debug: gc, onScroll: Nr, ariaLabelConfig: mc, zIndexMode: Cr = "basic", ...yc }, xc) {
  const go = _r || "1", wc = $h(pc), vc = he((kr) => {
    kr.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), Nr?.(kr);
  }, [Nr]);
  return h.jsx("div", { "data-testid": "rf__wrapper", ...yc, onScroll: vc, style: { ...ac, ...og }, ref: xc, className: we(["react-flow", r, wc]), id: _r, role: "application", children: h.jsxs(ng, { nodes: e, edges: t, width: fc, height: hc, fitView: br, fitViewOptions: Sr, minZoom: Pe, maxZoom: $e, nodeOrigin: Te, nodeExtent: Pt, zIndexMode: Cr, children: [h.jsx(Ph, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: m, onConnectStart: p, onConnectEnd: y, onClickConnectStart: w, onClickConnectEnd: x, nodesDraggable: fe, autoPanOnNodeFocus: me, nodesConnectable: Ne, nodesFocusable: Se, edgesFocusable: rt, edgesReconnectable: We, elementsSelectable: qe, elevateNodesOnSelect: Qa, elevateEdgesOnSelect: Ja, minZoom: Pe, maxZoom: $e, nodeExtent: Pt, onNodesChange: Me, onEdgesChange: it, snapToGrid: te, snapGrid: se, connectionMode: V, translateExtent: Ze, connectOnClick: Za, defaultEdgeOptions: Ga, fitView: br, fitViewOptions: Sr, onNodesDelete: F, onEdgesDelete: C, onDelete: R, onNodeDragStart: M, onNodeDrag: D, onNodeDragStop: j, onSelectionDrag: k, onSelectionDragStart: b, onSelectionDragStop: N, onMove: d, onMoveStart: f, onMoveEnd: u, noPanClassName: vr, nodeOrigin: Te, rfId: go, autoPanOnConnect: ec, autoPanOnNodeDrag: tc, autoPanSpeed: oc, onError: sc, connectionRadius: rc, isValidConnection: ic, selectNodesOnDrag: W, nodeDragThreshold: cc, connectionDragThreshold: lc, onBeforeDelete: B, debug: gc, ariaLabelConfig: mc, zIndexMode: Cr }), h.jsx(Qp, { onInit: c, onNodeClick: a, onEdgeClick: l, onNodeMouseEnter: S, onNodeMouseMove: g, onNodeMouseLeave: v, onNodeContextMenu: _, onNodeDoubleClick: E, nodeTypes: i, edgeTypes: s, connectionLineType: O, connectionLineStyle: U, connectionLineComponent: K, connectionLineContainerStyle: X, selectionKeyCode: G, selectionOnDrag: T, selectionMode: Z, deleteKeyCode: ie, multiSelectionKeyCode: oe, panActivationKeyCode: re, zoomActivationKeyCode: Q, onlyRenderVisibleElements: L, defaultViewport: Ce, translateExtent: Ze, minZoom: Pe, maxZoom: $e, preventScrolling: jt, zoomOnScroll: eo, zoomOnPinch: to, zoomOnDoubleClick: ro, panOnScroll: no, panOnScrollSpeed: $t, panOnScrollMode: oo, panOnDrag: io, autoPanOnSelection: nc, onPaneClick: so, onPaneMouseEnter: ao, onPaneMouseMove: co, onPaneMouseLeave: lo, onPaneScroll: an, onPaneContextMenu: cn, paneClickDistance: uo, nodeClickDistance: fo, onSelectionContextMenu: A, onSelectionStart: $, onSelectionEnd: P, onReconnect: po, onReconnectStart: I, onReconnectEnd: z, onEdgeContextMenu: Y, onEdgeDoubleClick: q, onEdgeMouseEnter: ee, onEdgeMouseMove: de, onEdgeMouseLeave: ue, reconnectRadius: ve, defaultMarkerColor: Jn, noDragClassName: Ee, noWheelClassName: qa, noPanClassName: vr, rfId: go, disableKeyboardA11y: Er, nodeExtent: Pt, viewport: uc, onViewportChange: dc }), h.jsx(Mh, { onSelectionChange: H }), ho, h.jsx(_h, { proOptions: Ka, position: Ua }), h.jsx(Eh, { rfId: go, disableKeyboardA11y: Er })] }) });
}
var ig = aa(rg);
const sg = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function ag({ children: e }) {
  const t = le(sg);
  return t ? mh.createPortal(e, t) : null;
}
function cg({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return h.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: we(["react-flow__background-pattern", n, o]) });
}
function lg({ radius: e, className: t }) {
  return h.jsx("circle", { cx: e, cy: e, r: e, className: we(["react-flow__background-pattern", "dots", t]) });
}
var nt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(nt || (nt = {}));
const ug = {
  [nt.Dots]: 1,
  [nt.Lines]: 1,
  [nt.Cross]: 6
}, dg = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function Da({
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
  patternClassName: d
}) {
  const f = ae(null), { transform: u, patternId: m } = le(dg, pe), p = o || ug[t], y = t === nt.Dots, w = t === nt.Cross, x = Array.isArray(n) ? n : [n, n], S = [x[0] * u[2] || 1, x[1] * u[2] || 1], g = p * u[2], v = Array.isArray(i) ? i : [i, i], _ = w ? [g, g] : S, E = [
    v[0] * u[2] || 1 + _[0] / 2,
    v[1] * u[2] || 1 + _[1] / 2
  ], M = `${m}${e || ""}`;
  return h.jsxs("svg", { className: we(["react-flow__background", c]), style: {
    ...l,
    ...Kn,
    "--xy-background-color-props": a,
    "--xy-background-pattern-color-props": s
  }, ref: f, "data-testid": "rf__background", children: [h.jsx("pattern", { id: M, x: u[0] % S[0], y: u[1] % S[1], width: S[0], height: S[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${E[0]},-${E[1]})`, children: y ? h.jsx(lg, { radius: g / 2, className: d }) : h.jsx(cg, { dimensions: _, lineWidth: r, variant: t, className: d }) }), h.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${M})` })] });
}
Da.displayName = "Background";
const fg = xe(Da);
function hg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: h.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function pg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: h.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function gg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: h.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function mg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: h.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function yg() {
  return h.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: h.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function yn({ children: e, className: t, ...n }) {
  return h.jsx("button", { type: "button", className: we(["react-flow__controls-button", t]), ...n, children: e });
}
const xg = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function ja({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: i, onZoomOut: s, onFitView: a, onInteractiveChange: l, className: c, children: d, position: f = "bottom-left", orientation: u = "vertical", "aria-label": m }) {
  const p = ge(), { isInteractive: y, minZoomReached: w, maxZoomReached: x, ariaLabelConfig: S } = le(xg, pe), { zoomIn: g, zoomOut: v, fitView: _ } = pr(), E = () => {
    g(), i?.();
  }, M = () => {
    v(), s?.();
  }, D = () => {
    _(r), a?.();
  }, j = () => {
    p.setState({
      nodesDraggable: !y,
      nodesConnectable: !y,
      elementsSelectable: !y
    }), l?.(!y);
  }, F = u === "horizontal" ? "horizontal" : "vertical";
  return h.jsxs(Un, { className: we(["react-flow__controls", F, c]), position: f, style: e, "data-testid": "rf__controls", "aria-label": m ?? S["controls.ariaLabel"], children: [t && h.jsxs(h.Fragment, { children: [h.jsx(yn, { onClick: E, className: "react-flow__controls-zoomin", title: S["controls.zoomIn.ariaLabel"], "aria-label": S["controls.zoomIn.ariaLabel"], disabled: x, children: h.jsx(hg, {}) }), h.jsx(yn, { onClick: M, className: "react-flow__controls-zoomout", title: S["controls.zoomOut.ariaLabel"], "aria-label": S["controls.zoomOut.ariaLabel"], disabled: w, children: h.jsx(pg, {}) })] }), n && h.jsx(yn, { className: "react-flow__controls-fitview", onClick: D, title: S["controls.fitView.ariaLabel"], "aria-label": S["controls.fitView.ariaLabel"], children: h.jsx(gg, {}) }), o && h.jsx(yn, { className: "react-flow__controls-interactive", onClick: j, title: S["controls.interactive.ariaLabel"], "aria-label": S["controls.interactive.ariaLabel"], children: y ? h.jsx(yg, {}) : h.jsx(mg, {}) }), d] });
}
ja.displayName = "Controls";
const wg = xe(ja);
function vg({ id: e, x: t, y: n, width: o, height: r, style: i, color: s, strokeColor: a, strokeWidth: l, className: c, borderRadius: d, shapeRendering: f, selected: u, onClick: m }) {
  const { background: p, backgroundColor: y } = i || {}, w = s || p || y;
  return h.jsx("rect", { className: we(["react-flow__minimap-node", { selected: u }, c]), x: t, y: n, rx: d, ry: d, width: o, height: r, style: {
    fill: w,
    stroke: a,
    strokeWidth: l
  }, shapeRendering: f, onClick: m ? (x) => m(x, e) : void 0 });
}
const bg = xe(vg), Sg = (e) => e.nodes.map((t) => t.id), Ao = (e) => e instanceof Function ? e : () => e;
function Eg({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: i = bg,
  onClick: s
}) {
  const a = le(Sg, pe), l = Ao(t), c = Ao(e), d = Ao(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return h.jsx(h.Fragment, { children: a.map((u) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    h.jsx(Ng, { id: u, nodeColorFunc: l, nodeStrokeColorFunc: c, nodeClassNameFunc: d, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: i, onClick: s, shapeRendering: f }, u)
  )) });
}
function _g({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: i, shapeRendering: s, NodeComponent: a, onClick: l }) {
  const { node: c, x: d, y: f, width: u, height: m } = le((p) => {
    const y = p.nodeLookup.get(e);
    if (!y)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const w = y.internals.userNode, { x, y: S } = y.internals.positionAbsolute, { width: g, height: v } = Qe(w);
    return {
      node: w,
      x,
      y: S,
      width: g,
      height: v
    };
  }, pe);
  return !c || c.hidden || !As(c) ? null : h.jsx(a, { x: d, y: f, width: u, height: m, style: c.style, selected: !!c.selected, className: o(c), color: t(c), borderRadius: r, strokeColor: n(c), strokeWidth: i, shapeRendering: s, onClick: l, id: c.id });
}
const Ng = xe(_g);
var Cg = xe(Eg);
const kg = 200, Ig = 150, Mg = (e) => !e.hidden, Ag = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? Is(on(e.nodeLookup, { filter: Mg }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, Dg = "react-flow__minimap-desc";
function Pa({
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
  maskStrokeWidth: f,
  position: u = "bottom-right",
  onClick: m,
  onNodeClick: p,
  pannable: y = !1,
  zoomable: w = !1,
  ariaLabel: x,
  inversePan: S,
  zoomStep: g = 1,
  offsetScale: v = 5
}) {
  const _ = ge(), E = ae(null), { boundingRect: M, viewBB: D, rfId: j, panZoom: F, translateExtent: C, flowWidth: R, flowHeight: H, ariaLabelConfig: b } = le(Ag, pe), k = e?.width ?? kg, N = e?.height ?? Ig, A = M.width / k, $ = M.height / N, P = Math.max(A, $), B = P * k, V = P * N, O = v * P, U = M.x - (B - M.width) / 2 - O, K = M.y - (V - M.height) / 2 - O, X = B + O * 2, ie = V + O * 2, G = `${Dg}-${j}`, T = ae(0), Z = ae();
  T.current = P, ce(() => {
    if (E.current && F)
      return Z.current = Bf({
        domNode: E.current,
        panZoom: F,
        getTransform: () => _.getState().transform,
        getViewScale: () => T.current
      }), () => {
        Z.current?.destroy();
      };
  }, [F]), ce(() => {
    Z.current?.update({
      translateExtent: C,
      width: R,
      height: H,
      inversePan: S,
      pannable: y,
      zoomStep: g,
      zoomable: w
    });
  }, [y, w, S, g, C, R, H]);
  const re = m ? (te) => {
    const [se, L] = Z.current?.pointer(te) || [0, 0];
    m(te, { x: se, y: L });
  } : void 0, oe = p ? he((te, se) => {
    const L = _.getState().nodeLookup.get(se).internals.userNode;
    p(te, L);
  }, []) : void 0, Q = x ?? b["minimap.ariaLabel"];
  return h.jsx(Un, { position: u, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-background-color-props": typeof c == "string" ? c : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * P : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof s == "number" ? s : void 0
  }, className: we(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: h.jsxs("svg", { width: k, height: N, viewBox: `${U} ${K} ${X} ${ie}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": G, ref: E, onClick: re, children: [Q && h.jsx("title", { id: G, children: Q }), h.jsx(Cg, { onClick: oe, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: i, nodeClassName: r, nodeStrokeWidth: s, nodeComponent: a }), h.jsx("path", { className: "react-flow__minimap-mask", d: `M${U - O},${K - O}h${X + O * 2}v${ie + O * 2}h${-X - O * 2}z
        M${D.x},${D.y}h${D.width}v${D.height}h${-D.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
Pa.displayName = "MiniMap";
const jg = xe(Pa), Pg = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, $g = {
  [Ct.Line]: "right",
  [Ct.Handle]: "bottom-right"
};
function Tg({ nodeId: e, position: t, variant: n = Ct.Handle, className: o, style: r = void 0, children: i, color: s, minWidth: a = 10, minHeight: l = 10, maxWidth: c = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: u, autoScale: m = !0, shouldResize: p, onResizeStart: y, onResize: w, onResizeEnd: x }) {
  const S = da(), g = typeof e == "string" ? e : S, v = ge(), _ = ae(null), E = n === Ct.Handle, M = le(he(Pg(E && m), [E, m]), pe), D = ae(null), j = t ?? $g[n];
  ce(() => {
    if (!(!_.current || !g))
      return D.current || (D.current = th({
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
          const { triggerNodeChanges: H, nodeLookup: b, parentLookup: k, nodeOrigin: N } = v.getState(), A = [], $ = { x: C.x, y: C.y }, P = b.get(g);
          if (P && P.expandParent && P.parentId) {
            const B = P.origin ?? N, V = C.width ?? P.measured.width ?? 0, O = C.height ?? P.measured.height ?? 0, U = {
              id: P.id,
              parentId: P.parentId,
              rect: {
                width: V,
                height: O,
                ...Ds({
                  x: C.x ?? P.position.x,
                  y: C.y ?? P.position.y
                }, { width: V, height: O }, P.parentId, b, B)
              }
            }, K = hr([U], b, k, N);
            A.push(...K), $.x = C.x ? Math.max(B[0] * V, C.x) : void 0, $.y = C.y ? Math.max(B[1] * O, C.y) : void 0;
          }
          if ($.x !== void 0 && $.y !== void 0) {
            const B = {
              id: g,
              type: "position",
              position: { ...$ }
            };
            A.push(B);
          }
          if (C.width !== void 0 && C.height !== void 0) {
            const V = {
              id: g,
              type: "dimensions",
              resizing: !0,
              setAttributes: u ? u === "horizontal" ? "width" : "height" : !0,
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
        controlPosition: j,
        boundaries: {
          minWidth: a,
          minHeight: l,
          maxWidth: c,
          maxHeight: d
        },
        keepAspectRatio: f,
        resizeDirection: u,
        onResizeStart: y,
        onResize: w,
        onResizeEnd: x,
        shouldResize: p
      }), () => {
        D.current?.destroy();
      };
  }, [
    j,
    a,
    l,
    c,
    d,
    f,
    y,
    w,
    x,
    p
  ]);
  const F = j.split("-");
  return h.jsx("div", { className: we(["react-flow__resize-control", "nodrag", ...F, n, o]), ref: _, style: {
    ...r,
    scale: M,
    ...s && { [E ? "backgroundColor" : "borderColor"]: s }
  }, children: i });
}
xe(Tg);
const Rg = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), $a = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
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
const Lg = Hn(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: o,
    className: r = "",
    children: i,
    iconNode: s,
    ...a
  }, l) => Po(
    "svg",
    {
      ref: l,
      ...zg,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: $a("lucide", r),
      ...a
    },
    [
      ...s.map(([c, d]) => Po(c, d)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const _e = (e, t) => {
  const n = Hn(
    ({ className: o, ...r }, i) => Po(Lg, {
      ref: i,
      iconNode: t,
      className: $a(`lucide-${Rg(e)}`, o),
      ...r
    })
  );
  return n.displayName = `${e}`, n;
};
const Hg = _e("Boxes", [
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
const Gn = _e("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const Vg = _e("ChevronDown", [
  ["path", { d: "m6 9 6 6 6-6", key: "qrunsl" }]
]);
const Og = _e("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const En = _e("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const Qt = _e("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const Bg = _e("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const Fg = _e("ListTree", [
  ["path", { d: "M21 12h-8", key: "1bmf0i" }],
  ["path", { d: "M21 6H8", key: "1pqkrb" }],
  ["path", { d: "M21 18h-8", key: "1tm79t" }],
  ["path", { d: "M3 6v4c0 1.1.9 2 2 2h3", key: "1ywdgy" }],
  ["path", { d: "M3 10v6c0 1.1.9 2 2 2h3", key: "2wc746" }]
]);
const Ta = _e("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const mr = _e("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const Yg = _e("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const Xg = _e("Save", [
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
const Wg = _e("Search", [
  ["circle", { cx: "11", cy: "11", r: "8", key: "4ej97u" }],
  ["path", { d: "m21 21-4.3-4.3", key: "1qie3q" }]
]);
const It = _e("Sparkles", [
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
const qo = _e("Trash2", [
  ["path", { d: "M3 6h18", key: "d0wm0j" }],
  ["path", { d: "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", key: "4alrt4" }],
  ["path", { d: "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", key: "v07s0e" }],
  ["line", { x1: "10", x2: "10", y1: "11", y2: "17", key: "1uufr5" }],
  ["line", { x1: "14", x2: "14", y1: "11", y2: "17", key: "xtxkd" }]
]), Oe = "/_elsa/workflow-management";
async function qg(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${Oe}/definitions?${n.toString()}`);
}
async function Zg(e, t) {
  return e.http.getJson(`${Oe}/definitions/${encodeURIComponent(t)}`);
}
async function Ug(e, t) {
  return e.http.postJson(`${Oe}/definitions`, t);
}
async function Kg(e, t) {
  await e.http.deleteJson(`${Oe}/definitions/${encodeURIComponent(t)}`);
}
async function Gg(e, t) {
  await e.http.postJson(`${Oe}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function Qg(e, t) {
  await e.http.deleteJson(`${Oe}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function Jg(e, t) {
  return e.http.putJson(`${Oe}/drafts/${encodeURIComponent(t.id)}`, { state: t.state, layout: t.layout });
}
async function em(e, t) {
  return e.http.postJson(`${Oe}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function tm(e, t) {
  return e.http.postJson(`${Oe}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function Ra(e, t) {
  return e.http.postJson(`${Oe}/executables/${encodeURIComponent(t)}/run`, {});
}
async function nm(e) {
  return e.http.getJson("/_demo/workflows/executables");
}
async function za(e) {
  return e.http.getJson(`${Oe}/activities`);
}
const yr = "elsa.sequence.structure", Qn = "elsa.flowchart.structure";
function Do(e, t) {
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
  const n = t.payload, o = xm(t), r = jo(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: wm(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, i]) => jo(i)).map(([i, s]) => ({
    id: `${t.kind}:${i}`,
    label: bm(i),
    property: i,
    mode: "generic",
    activities: jo(s) ?? []
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
        sourcePorts: Ha(s, l)
      }
    };
  });
  return {
    nodes: i,
    edges: e.slot.mode === "flowchart" ? dm(e.owner) : um(e.slot, i)
  };
}
function Zo(e, t, n) {
  if (t.length === 0) {
    const a = Ye(e)[0];
    return a ? Rn(e, a, n) : e;
  }
  const [o, ...r] = t, i = Ye(e).find((a) => a.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((a) => a.nodeId === o.ownerNodeId ? Zo(a, r, n) : a);
  return Rn(e, i, s);
}
function La(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, i = Ye(e).find((a) => a.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((a) => a.nodeId === o.ownerNodeId ? La(a, r, n) : a);
  return Rn(e, i, s);
}
function Rn(e, t, n) {
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
    const l = t.find((d) => d.id === s.nodeId), c = t.find((d) => d.id === a.nodeId);
    return (l?.position.x ?? 0) - (c?.position.x ?? 0);
  }), Rn(e.owner, e.slot, i);
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
function Li(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: cm(e)
  };
}
function Ie(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? am(t) : n;
}
function am(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function cm(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: yr,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: Qn,
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
    if (!xr(r)) continue;
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
  if (e.structure?.kind !== Qn) return [];
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
function Ha(e, t) {
  const n = Hi(e.cases);
  if (pm(e, t) && n.length > 0)
    return [...n.map((i) => ({ name: i, displayName: i })), { name: "Default", displayName: "Default" }];
  const o = [
    ..._n(t?.designFacets),
    ..._n(t?.ports),
    ..._n(t?.outputs)
  ];
  if (o.length > 0) return gm(o);
  const r = Hi(e.outcomes);
  return r.length > 0 ? r.map((i) => ({ name: i, displayName: i })) : [{ name: "Done", displayName: "Done" }];
}
function fm(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function zn(e, t, n, o) {
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
  const o = zn(t.source, n, t.sourceHandle ?? "Done", void 0), r = zn(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((i) => i.id !== t.id).concat(o, r);
}
function jo(e) {
  return Array.isArray(e) ? e.filter(ym) : null;
}
function pm(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function _n(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!xr(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(..._n(n.ports));
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
function Hi(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function mm(e) {
  return xr(e) && typeof e.x == "number" && typeof e.y == "number";
}
function xr(e) {
  return typeof e == "object" && e !== null;
}
function ym(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function xm(e) {
  return e.kind === yr ? "sequence" : e.kind === Qn ? "flowchart" : "generic";
}
function wm(e) {
  return e.kind === yr || e.kind === Qn, "Activities";
}
function vm(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function bm(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Sm = { workflowActivity: Ym }, Em = { workflow: Xm }, Vi = "application/x-elsa-activity-version-id", _m = 6, Nm = 1200, Cm = [10, 25, 50], km = 10, Va = ot.createContext(null);
function Qm(e) {
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
        component: () => /* @__PURE__ */ h.jsx(Im, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ h.jsx(Mm, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow instances",
        component: () => /* @__PURE__ */ h.jsx(Am, { ai: e.ai })
      }
    ]
  });
}
function Im({ context: e, ai: t }) {
  const [n, o] = ne(Oi);
  ce(() => {
    const i = () => o(Oi());
    return window.addEventListener("popstate", i), () => window.removeEventListener("popstate", i);
  }, []);
  const r = (i) => {
    const s = i ? `/workflows/definitions?definition=${encodeURIComponent(i)}` : "/workflows/definitions";
    window.history.pushState({}, "", s), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return n ? /* @__PURE__ */ h.jsx(Fm, { context: e, definitionId: n, ai: t, onBack: () => r(null) }) : /* @__PURE__ */ h.jsx(wr, { activePath: "/workflows/definitions", title: "Definitions", children: /* @__PURE__ */ h.jsx(Dm, { context: e, ai: t, onOpen: r }) });
}
function Mm({ context: e, ai: t }) {
  const [n, o] = ne(Bi);
  return ce(() => {
    const r = () => o(Bi());
    return window.addEventListener("popstate", r), () => window.removeEventListener("popstate", r);
  }, []), /* @__PURE__ */ h.jsx(wr, { activePath: "/workflows/executables", title: "Executables", children: /* @__PURE__ */ h.jsx(Pm, { context: e, ai: t, definitionFilter: n }) });
}
function Am({ ai: e }) {
  const t = Mt(e, "weaver.workflows.explain-instance");
  return /* @__PURE__ */ h.jsx(wr, { activePath: "/workflows/instances", title: "Instances", children: /* @__PURE__ */ h.jsxs("div", { className: "wf-empty", children: [
    "Workflow instance history will appear here when the runtime exposes an instance query endpoint.",
    t ? /* @__PURE__ */ h.jsxs("button", { type: "button", className: "wf-ai-inline-action", onClick: () => At(e, t, { scope: "workflow-instances" }), children: [
      /* @__PURE__ */ h.jsx(It, { size: 13 }),
      " Ask Weaver about instances"
    ] }) : null
  ] }) });
}
function wr({ activePath: e, title: t, children: n }) {
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
function Oi() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Bi() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Dm({ context: e, ai: t, onOpen: n }) {
  const [o, r] = ne(""), [i, s] = ne("active"), [a, l] = ne(1), [c, d] = ne(km), [f, u] = ne("loading"), [m, p] = ne(""), [y, w] = ne(""), [x, S] = ne([]), [g, v] = ne(0), [_, E] = ne(() => /* @__PURE__ */ new Set()), [M, D] = ne(null), [j, F] = ne(!1), [C, R] = ne([]), [H, b] = ne("idle"), k = ae(null), N = ye(() => x.map((L) => L.id), [x]), A = Mt(t, "weaver.workflows.suggest-create-metadata"), $ = Mt(t, "weaver.workflows.explain-definition"), P = N.filter((L) => _.has(L)).length, B = N.length > 0 && P === N.length, V = he(async () => {
    u("loading"), p("");
    try {
      const L = await qg(e, { search: o, state: i, page: a, pageSize: c }), W = typeof L.totalCount == "number", fe = L.totalCount ?? L.definitions.length, me = Oa(fe, c);
      if (fe > 0 && a > me) {
        l(me);
        return;
      }
      S(W ? L.definitions : Tm(L.definitions, a, c)), v(fe), u("ready");
    } catch (L) {
      p(L instanceof Error ? L.message : String(L)), u("failed");
    }
  }, [e, o, i, a, c]);
  ce(() => {
    V();
  }, [V]), ce(() => {
    k.current && (k.current.indeterminate = P > 0 && !B);
  }, [B, P]);
  const O = he(async () => {
    if (!(H === "loading" || H === "ready")) {
      b("loading");
      try {
        const L = await za(e);
        R(L.activities ?? []), b("ready");
      } catch (L) {
        b("failed"), p(L instanceof Error ? L.message : String(L));
      }
    }
  }, [H, e]), U = () => {
    p(""), w(""), D({ name: "", description: "", rootKind: "flowchart" }), O();
  }, K = async () => {
    if (M?.name.trim()) {
      F(!0), p(""), w("");
      try {
        const L = await Ug(e, {
          name: M.name.trim(),
          description: M.description.trim() || null,
          rootKind: M.rootKind,
          rootActivityVersionId: Lm(M, C)
        });
        D(null), n(L.definition.id);
      } catch (L) {
        p(L instanceof Error ? L.message : String(L));
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
      for (const me of N)
        L ? fe.add(me) : fe.delete(me);
      return fe;
    });
  }, re = (L) => {
    s(L), l(1), G();
  }, oe = (L) => {
    r(L), l(1), G();
  }, Q = async (L) => {
    if (window.confirm(`Delete workflow definition "${L.name}"? You can restore it from the Deleted view.`)) {
      w(""), p("");
      try {
        await Kg(e, L.id), T(L.id, !1), w(`Deleted ${L.name}`), await ie();
      } catch (W) {
        p(W instanceof Error ? W.message : String(W));
      }
    }
  }, te = async (L) => {
    w(""), p("");
    try {
      await Gg(e, L.id), T(L.id, !1), w(`Restored ${L.name}`), await ie();
    } catch (W) {
      p(W instanceof Error ? W.message : String(W));
    }
  }, se = async (L) => {
    if (window.confirm(`Permanently delete workflow definition "${L.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`)) {
      w(""), p("");
      try {
        await Qg(e, L.id), T(L.id, !1), w(`Permanently deleted ${L.name}`), await ie();
      } catch (W) {
        p(W instanceof Error ? W.message : String(W));
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
        /* @__PURE__ */ h.jsx(Wg, { size: 15 }),
        /* @__PURE__ */ h.jsx("input", { value: o, onChange: (L) => oe(L.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
        V();
      }, children: "Refresh" }),
      /* @__PURE__ */ h.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ h.jsxs("button", { type: "button", title: "Create workflow", onClick: U, children: [
        /* @__PURE__ */ h.jsx(mr, { size: 15 }),
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
      /* @__PURE__ */ h.jsx(Gn, { size: 14 }),
      " ",
      y
    ] }) : null,
    _.size > 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ h.jsxs("span", { children: [
        _.size,
        " selected"
      ] }),
      /* @__PURE__ */ h.jsx("button", { type: "button", onClick: G, children: "Clear selection" })
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
            "aria-selected": _.has(L.id),
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
                  checked: _.has(L.id),
                  onChange: (W) => T(L.id, W.target.checked),
                  "aria-label": `Select workflow definition ${L.name}`
                }
              ) }),
              /* @__PURE__ */ h.jsxs("span", { children: [
                /* @__PURE__ */ h.jsx("strong", { children: L.name }),
                /* @__PURE__ */ h.jsx("small", { children: L.description || L.id })
              ] }),
              /* @__PURE__ */ h.jsx("span", { children: L.latestVersion ?? "No version" }),
              /* @__PURE__ */ h.jsx("span", { children: i === "deleted" ? Uo(L.deletedAt) : L.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ h.jsx("span", { children: Uo(L.lastModifiedAt) }),
              /* @__PURE__ */ h.jsx("span", { className: "wf-row-actions", onClick: (W) => W.stopPropagation(), children: i === "active" ? /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
                /* @__PURE__ */ h.jsx("button", { type: "button", onClick: (W) => {
                  W.stopPropagation(), n(L.id);
                }, children: "Open" }),
                /* @__PURE__ */ h.jsx("button", { type: "button", onClick: (W) => {
                  W.stopPropagation(), X(L.id);
                }, children: "Artifacts" }),
                $ ? /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => At(t, $, L), children: [
                  /* @__PURE__ */ h.jsx(It, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ h.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  Q(L);
                }, children: [
                  /* @__PURE__ */ h.jsx(qo, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ h.jsxs(h.Fragment, { children: [
                /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
                  te(L);
                }, children: [
                  /* @__PURE__ */ h.jsx(Yg, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ h.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  se(L);
                }, children: [
                  /* @__PURE__ */ h.jsx(qo, { size: 13 }),
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
          totalCount: g,
          onPageChange: l,
          onPageSizeChange: (L) => {
            d(L), l(1);
          }
        }
      )
    ] }) : null,
    M ? /* @__PURE__ */ h.jsx(
      jm,
      {
        draft: M,
        activities: C,
        catalogState: H,
        creating: j,
        suggestMetadataAction: A,
        onSuggestMetadata: A ? () => At(t, A, { draft: M, activities: C }) : void 0,
        onChange: (L) => D(L),
        onClose: () => D(null),
        onSubmit: K
      }
    ) : null
  ] });
}
function jm({ draft: e, activities: t, catalogState: n, creating: o, suggestMetadataAction: r, onSuggestMetadata: i, onChange: s, onClose: a, onSubmit: l }) {
  const c = ye(() => Rm(t), [t]), d = zm(e, t), f = (u) => {
    if (u.startsWith("kind:")) {
      s({ ...e, rootKind: u.slice(5), rootActivityVersionId: null });
      return;
    }
    const m = t.find((p) => p.activityVersionId === u);
    s({
      ...e,
      rootKind: Ba(m) ?? e.rootKind,
      rootActivityVersionId: u
    });
  };
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
            /* @__PURE__ */ h.jsx(It, { size: 13 }),
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
              value: d,
              onChange: (u) => f(u.target.value),
              disabled: n === "loading",
              children: [
                /* @__PURE__ */ h.jsx("optgroup", { label: "Composite roots", children: c.compositeRoots.map((u) => /* @__PURE__ */ h.jsx("option", { value: u.value, children: u.label }, u.value)) }),
                c.otherCategories.map((u) => /* @__PURE__ */ h.jsx("optgroup", { label: u.name, children: u.activities.map((m) => /* @__PURE__ */ h.jsx("option", { value: m.activityVersionId, children: Ie(m) }, m.activityVersionId)) }, u.name))
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
  const [o, r] = ne("loading"), [i, s] = ne(""), [a, l] = ne(""), [c, d] = ne([]), f = ye(
    () => n ? c.filter((y) => y.definitionId === n || y.sourceId === n) : c,
    [n, c]
  ), u = Mt(t, "weaver.workflows.explain-executable"), m = he(async () => {
    r("loading"), s("");
    try {
      d(await nm(e)), r("ready");
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
      await Ra(e, y.artifactId), l(`Started ${y.artifactId}`);
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
      /* @__PURE__ */ h.jsx(Gn, { size: 14 }),
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
        /* @__PURE__ */ h.jsx("span", { children: Vm(y) }),
        /* @__PURE__ */ h.jsx("span", { children: Om(y) }),
        /* @__PURE__ */ h.jsx("span", { children: Uo(y.publishedAt ?? y.createdAt) }),
        /* @__PURE__ */ h.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
            p(y);
          }, children: [
            /* @__PURE__ */ h.jsx(Ta, { size: 13 }),
            " Run"
          ] }),
          u ? /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => At(t, u, y), children: [
            /* @__PURE__ */ h.jsx(It, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, y.artifactId))
    ] }) : null
  ] });
}
function $m({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const i = Oa(n, t), s = n === 0 ? 0 : (e - 1) * t + 1, a = Math.min(e * t, n);
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
        /* @__PURE__ */ h.jsx(Og, { size: 14 }),
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
        /* @__PURE__ */ h.jsx(En, { size: 14 })
      ] })
    ] })
  ] });
}
function Tm(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function Oa(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Mt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function At(e, t, n) {
  const o = t.createPrompt(n);
  o && e.dispatchPrompt(o);
}
function Rm(e) {
  const t = Ln(e, "flowchart"), n = Ln(e, "sequence"), o = [
    { value: t?.activityVersionId ?? "kind:flowchart", label: "Flowchart" },
    { value: n?.activityVersionId ?? "kind:sequence", label: "Sequence" }
  ], r = /* @__PURE__ */ new Map();
  for (const s of e.filter(Wa)) {
    if (Hm(s)) continue;
    const a = s.category || "Uncategorized";
    r.set(a, [...r.get(a) ?? [], s]);
  }
  const i = Array.from(r.entries()).sort(([s], [a]) => s.localeCompare(a)).map(([s, a]) => ({
    name: s,
    activities: a.sort((l, c) => Ie(l).localeCompare(Ie(c)))
  }));
  return { compositeRoots: o, otherCategories: i };
}
function zm(e, t) {
  return e.rootActivityVersionId ?? Ln(t, e.rootKind)?.activityVersionId ?? `kind:${e.rootKind}`;
}
function Lm(e, t) {
  return e.rootActivityVersionId ?? Ln(t, e.rootKind)?.activityVersionId ?? null;
}
function Ln(e, t) {
  return e.find((n) => Ba(n) === t);
}
function Ba(e) {
  return e ? Ya(e) ? "flowchart" : Xa(e) ? "sequence" : null : null;
}
function Fa(e) {
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
function Hm(e) {
  return Ya(e) || Xa(e);
}
function Ya(e) {
  return Ie(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function Xa(e) {
  return Ie(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function Wa(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function Vm(e) {
  return e.sourceKind || e.sourceId || e.sourceVersion ? [e.sourceKind, e.sourceId, e.sourceVersion].filter(Boolean).join(" / ") : e.definitionId;
}
function Om(e) {
  return Bm(e.rootActivityType) || e.rootActivityType;
}
function Bm(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function Fm({ context: e, definitionId: t, ai: n, onBack: o }) {
  const [r, i] = ne(null), [s, a] = ne(null), [l, c] = ne([]), [d, f] = ne([]), [u, m] = ne([]), [p, y] = ne([]), [w, x] = ne(null), [S, g] = ne(null), [v, _] = ne(null), [E, M] = ne(null), [D, j] = ne(""), [F, C] = ne(""), [R, H] = ne(!1), [b, k] = ne(null), [N, A] = ne(() => /* @__PURE__ */ new Set()), $ = ae(null), P = ae(null), B = ae(""), V = ae(0), O = ae(null), U = ae(!1), K = s?.state.rootActivity ?? null, X = ye(() => Do(K, d), [K, d]), ie = ye(() => new Map(l.map((I) => [I.activityVersionId, I])), [l]), G = ye(() => Fa(l), [l]), T = ye(() => X?.slot.activities.find((I) => I.nodeId === S) ?? null, [X, S]), Z = T ? Ye(T) : [], re = Mt(n, "weaver.workflows.find-draft-risks"), oe = Mt(n, "weaver.workflows.propose-update"), Q = he(async () => {
    j("");
    const [I, z] = await Promise.all([
      Zg(e, t),
      za(e)
    ]), Y = I.draft ?? null;
    i(I), B.current = Y ? Lt(Y) : "", a(Y), c(z.activities ?? []), f([]), g(null);
  }, [e, t]);
  ce(() => {
    Q().catch((I) => j(I instanceof Error ? I.message : String(I)));
  }, [Q]), ce(() => {
    A((I) => {
      let z = !1;
      const Y = new Set(I);
      for (const q of G)
        Y.has(q.category) || (Y.add(q.category), z = !0);
      return z ? Y : I;
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
    a((z) => z && { ...z, state: { ...z.state, rootActivity: I } });
  }, se = he((I, z) => {
    const Y = Li(I, Fi(I));
    if (!s?.state.rootActivity) {
      te(Y), g(Y.nodeId);
      return;
    }
    if (!X) {
      if (!Ye(Y)[0]) {
        C(""), j("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      a((ee) => {
        if (!ee?.state.rootActivity) return ee;
        const de = ee.state.rootActivity, ue = Zo(Y, [], [de]), ve = z ? [
          ...ee.layout.filter((Me) => Me.nodeId !== de.nodeId),
          {
            nodeId: de.nodeId,
            x: Math.round(z.x),
            y: Math.round(z.y)
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
      }), g(s.state.rootActivity.nodeId), j(""), C(`Wrapped root in ${Ie(I)}`);
      return;
    }
    a((q) => {
      if (!q?.state.rootActivity) return q;
      const ee = Do(q.state.rootActivity, d);
      if (!ee) return q;
      const de = Zo(q.state.rootActivity, d, [...ee.slot.activities, Y]), ue = z ? [
        ...q.layout.filter((ve) => ve.nodeId !== Y.nodeId),
        {
          nodeId: Y.nodeId,
          x: Math.round(z.x),
          y: Math.round(z.y)
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
    }), g(Y.nodeId);
  }, [s?.state.rootActivity, d, X]), L = he((I, z) => {
    const Y = Li(I, Fi(I)), q = {
      id: Y.nodeId,
      type: "workflowActivity",
      position: z,
      selected: !0,
      data: {
        label: Ie(I),
        activityVersionId: I.activityVersionId,
        activityTypeKey: I.activityTypeKey,
        childSlots: Ye(Y),
        acceptsInbound: String(I.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: Ha(Y, I)
      }
    };
    return { activityNode: Y, node: q };
  }, []), W = he((I, z, Y = []) => {
    a((q) => {
      if (!q) return q;
      const ee = sm(q.layout, I), de = q.state.rootActivity;
      if (!de) return { ...q, layout: ee };
      const ue = Do(de, d);
      if (!ue) return { ...q, layout: ee };
      const ve = rm(ue, I, z, Y), Me = ue.slot.mode === "flowchart" ? im(ve, z) : ve;
      return {
        ...q,
        layout: ee,
        state: {
          ...q.state,
          rootActivity: La(de, d, Me)
        }
      };
    });
  }, [d]), fe = he((I, z) => {
    if (!$.current) return null;
    const Y = $.current.getBoundingClientRect();
    return w ? w.screenToFlowPosition({ x: I, y: z }) : {
      x: I - Y.left,
      y: z - Y.top
    };
  }, [w]), me = he((I, z) => document.elementFromPoint(I, z)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), Ne = he((I, z, Y) => {
    const q = u.find((Ee) => Ee.id === z.source), ee = u.find((Ee) => Ee.id === z.target), de = q && ee ? Zm(q, ee) : q ? Yi(q) : Y, ue = L(I, de), Me = [...u.map((Ee) => Ee.selected ? { ...Ee, selected: !1 } : Ee), ue.node], it = hm(p, z, ue.node.id);
    m(Me), y(it), g(ue.node.id), W(Me, it, [ue.activityNode]);
  }, [W, L, p, u]), Se = he((I, z, Y) => {
    if (!$.current) return !1;
    const q = $.current.getBoundingClientRect();
    if (!(z >= q.left && z <= q.right && Y >= q.top && Y <= q.bottom)) return !1;
    const de = fe(z, Y);
    if (!de) return !1;
    if (X?.slot.mode === "flowchart") {
      const ue = me(z, Y), ve = ue ? p.find((Me) => Me.id === ue) : void 0;
      if (ve)
        return Ne(I, ve, de), !0;
    }
    return se(I, de), !0;
  }, [se, p, me, X?.slot.mode, Ne, fe]);
  ce(() => {
    const I = (Y) => {
      const q = O.current;
      if (!q) return;
      Math.hypot(Y.clientX - q.startX, Y.clientY - q.startY) >= _m && (q.dragging = !0);
    }, z = (Y) => {
      const q = O.current;
      if (O.current = null, !q?.dragging || !$.current) return;
      const ee = $.current.getBoundingClientRect();
      Y.clientX >= ee.left && Y.clientX <= ee.right && Y.clientY >= ee.top && Y.clientY <= ee.bottom && (U.current = !0, window.setTimeout(() => {
        U.current = !1;
      }, 0), Se(q.activity, Y.clientX, Y.clientY));
    };
    return window.addEventListener("pointermove", I), window.addEventListener("pointerup", z), window.addEventListener("pointercancel", z), () => {
      window.removeEventListener("pointermove", I), window.removeEventListener("pointerup", z), window.removeEventListener("pointercancel", z);
    };
  }, [w, Se]);
  const Te = (I, z) => {
    I.dataTransfer.setData(Vi, z.activityVersionId), I.dataTransfer.setData("text/plain", z.activityVersionId), I.dataTransfer.effectAllowed = "copy";
  }, rt = (I, z) => {
    I.clientX === 0 && I.clientY === 0 || Se(z, I.clientX, I.clientY) && (U.current = !0, window.setTimeout(() => {
      U.current = !1;
    }, 0));
  }, We = (I, z) => {
    I.button === 0 && (O.current = {
      activity: z,
      startX: I.clientX,
      startY: I.clientY,
      dragging: !1
    });
  }, qe = (I) => {
    U.current || se(I);
  }, Ce = (I) => {
    if (I.preventDefault(), I.dataTransfer.dropEffect = "copy", X?.slot.mode !== "flowchart") return;
    const z = me(I.clientX, I.clientY);
    M(z);
  }, Pe = (I) => {
    if (!$.current) return;
    const z = I.relatedTarget;
    z && $.current.contains(z) || M(null);
  }, $e = (I) => {
    I.preventDefault(), M(null);
    const z = I.dataTransfer.getData(Vi) || I.dataTransfer.getData("text/plain"), Y = ie.get(z);
    Y && Se(Y, I.clientX, I.clientY);
  }, Ze = () => {
    const I = $.current?.getBoundingClientRect();
    I && _({
      kind: "fromEmpty",
      clientX: I.left + I.width / 2,
      clientY: I.top + I.height / 2
    });
  }, jt = he(async (I, z) => {
    const Y = ++V.current, q = Lt(I);
    j("");
    try {
      const ee = await Jg(e, I), de = Lt(ee);
      B.current = de, a((ue) => !ue || ue.id !== ee.id ? ue : Lt(ue) === q ? ee : { ...ue, validationErrors: ee.validationErrors }), Y === V.current && C(z);
    } catch (ee) {
      Y === V.current && (C(""), j(ee instanceof Error ? ee.message : String(ee)));
    }
  }, [e]);
  ce(() => {
    if (!R || !s || Lt(s) === B.current) return;
    C("Autosaving...");
    const z = window.setTimeout(() => {
      jt(s, "Autosaved");
    }, Nm);
    return () => window.clearTimeout(z);
  }, [R, s, jt]);
  const Pt = async () => {
    s && (C("Saving..."), await jt(s, "Saved"));
  }, Jn = async () => {
    if (s) {
      C("Promoting...");
      try {
        const I = await em(e, s.id), z = await tm(e, I.versionId);
        k(z.artifactId), C(`Published ${z.artifactVersion}`), await Q();
      } catch (I) {
        C(""), j(I instanceof Error ? I.message : String(I));
      }
    }
  }, eo = async () => {
    if (b) {
      C("Running...");
      try {
        await Ra(e, b), C("Run dispatched");
      } catch (I) {
        C(""), j(I instanceof Error ? I.message : String(I));
      }
    }
  }, to = (I) => m((z) => oa(I, z)), no = (I) => y((z) => ra(I, z)), $t = (I) => !I.source || !I.target || I.source === I.target || X?.slot.mode !== "flowchart" ? !1 : !I.targetHandle, oo = (I) => {
    if (!s?.state.rootActivity || !X || X.slot.mode !== "flowchart" || !$t(I)) return;
    const z = zn(I.source, I.target, I.sourceHandle ?? "Done", I.targetHandle ?? void 0), Y = sa(z, p);
    y(Y), W(u, Y);
  }, ro = () => {
    W(u, p);
  }, io = (I, z) => {
    if (!z.nodeId || z.handleType === "target") {
      P.current = null;
      return;
    }
    P.current = {
      nodeId: z.nodeId,
      handleId: z.handleId ?? null
    };
  }, so = (I) => {
    const z = P.current;
    if (P.current = null, !z || X?.slot.mode !== "flowchart" || I.target?.closest(".react-flow__handle, .react-flow__node")) return;
    const q = Um(I);
    _({
      kind: "fromPort",
      sourceNodeId: z.nodeId,
      sourceHandleId: z.handleId,
      clientX: q.x,
      clientY: q.y
    });
  }, ao = (I, z) => {
    if (!$t(z)) return;
    const Y = zh(I, {
      ...z,
      sourceHandle: z.sourceHandle ?? "Done",
      targetHandle: z.targetHandle ?? void 0
    }, p, { shouldReplaceId: !1 });
    y(Y), W(u, Y);
  }, co = (I) => {
    if (I.length === 0) return;
    const z = new Set(I.map((ee) => ee.id)), Y = u.filter((ee) => !z.has(ee.id)), q = p.filter((ee) => !z.has(ee.source) && !z.has(ee.target));
    m(Y), y(q), S && z.has(S) && g(null), W(Y, q);
  }, lo = (I) => {
    if (I.length === 0) return;
    const z = new Set(I.map((q) => q.id)), Y = p.filter((q) => !z.has(q.id));
    y(Y), W(u, Y);
  }, an = he((I) => {
    const z = p.filter((Y) => Y.id !== I);
    y(z), W(u, z);
  }, [W, p, u]), cn = he((I, z, Y) => {
    _({ kind: "spliceEdge", edgeId: I, clientX: z, clientY: Y });
  }, []), uo = (I) => {
    const z = v;
    if (!z) return;
    _(null);
    const Y = fe(z.clientX, z.clientY) ?? { x: 0, y: 0 };
    if (z.kind === "fromEmpty") {
      const ee = L(I, Y), ue = [...u.map((ve) => ve.selected ? { ...ve, selected: !1 } : ve), ee.node];
      m(ue), g(ee.node.id), W(ue, p, [ee.activityNode]);
      return;
    }
    if (z.kind === "fromPort") {
      const ee = u.find((Ee) => Ee.id === z.sourceNodeId), de = ee ? Yi(ee) : Y, ue = L(I, de), Me = [...u.map((Ee) => Ee.selected ? { ...Ee, selected: !1 } : Ee), ue.node], it = [...p, zn(z.sourceNodeId, ue.node.id, z.sourceHandleId ?? "Done")];
      m(Me), y(it), g(ue.node.id), W(Me, it, [ue.activityNode]);
      return;
    }
    const q = p.find((ee) => ee.id === z.edgeId);
    q && Ne(I, q, Y);
  }, fo = ye(() => ({
    highlightedEdgeId: E,
    deleteEdge: an,
    requestInsertActivity: cn
  }), [an, E, cn]), ho = (I, z, Y) => {
    f((q) => [...q, { ownerNodeId: I.nodeId, slotId: z, label: Y }]), g(null);
  }, po = (I) => {
    A((z) => {
      const Y = new Set(z);
      return Y.has(I) ? Y.delete(I) : Y.add(I), Y;
    });
  };
  return !r || !s ? /* @__PURE__ */ h.jsx("div", { className: "wf-empty", children: D || "Loading workflow editor..." }) : /* @__PURE__ */ h.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ h.jsx("button", { type: "button", className: "wf-link-button", onClick: o, children: "Definitions" }),
      /* @__PURE__ */ h.jsx(En, { size: 14 }),
      /* @__PURE__ */ h.jsx("strong", { children: r.definition.name }),
      /* @__PURE__ */ h.jsx("span", { className: "wf-chip", children: "Draft" }),
      F ? /* @__PURE__ */ h.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ h.jsx(Gn, { size: 13 }),
        " ",
        F
      ] }) : null,
      /* @__PURE__ */ h.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ h.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ h.jsx("input", { type: "checkbox", checked: R, onChange: (I) => H(I.target.checked) }),
          /* @__PURE__ */ h.jsx("span", { children: "Autosave" })
        ] }),
        re ? /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => At(n, re, { definition: r.definition, draft: s }), children: [
          /* @__PURE__ */ h.jsx(It, { size: 15 }),
          " Risks"
        ] }) : null,
        oe ? /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => At(n, oe, { definition: r.definition, draft: s }), children: [
          /* @__PURE__ */ h.jsx(It, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
          Pt();
        }, children: [
          /* @__PURE__ */ h.jsx(Xg, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => {
          Jn();
        }, children: [
          /* @__PURE__ */ h.jsx(Bg, { size: 15 }),
          " Promote"
        ] }),
        /* @__PURE__ */ h.jsxs("button", { type: "button", disabled: !b, onClick: () => {
          eo();
        }, children: [
          /* @__PURE__ */ h.jsx(Ta, { size: 15 }),
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
          /* @__PURE__ */ h.jsx(Hg, { size: 15 }),
          " Activities"
        ] }),
        /* @__PURE__ */ h.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: G.map((I) => {
          const z = N.has(I.category);
          return /* @__PURE__ */ h.jsxs("div", { className: "wf-palette-category", children: [
            /* @__PURE__ */ h.jsxs(
              "button",
              {
                type: "button",
                className: "wf-palette-category-toggle",
                role: "treeitem",
                "aria-expanded": z,
                onClick: () => po(I.category),
                children: [
                  z ? /* @__PURE__ */ h.jsx(Vg, { size: 14 }) : /* @__PURE__ */ h.jsx(En, { size: 14 }),
                  /* @__PURE__ */ h.jsx("span", { children: I.category }),
                  /* @__PURE__ */ h.jsx("small", { children: I.activities.length })
                ]
              }
            ),
            z ? /* @__PURE__ */ h.jsx("div", { className: "wf-palette-activities", role: "group", children: I.activities.map((Y) => {
              const q = Y.description?.trim(), ee = q ? `wf-palette-description-${Y.activityVersionId}` : void 0;
              return /* @__PURE__ */ h.jsxs(
                "button",
                {
                  type: "button",
                  className: "wf-palette-activity",
                  role: "treeitem",
                  draggable: !0,
                  title: q || Ie(Y),
                  "aria-describedby": ee,
                  onClick: () => qe(Y),
                  onDragStart: (de) => Te(de, Y),
                  onDragEnd: (de) => rt(de, Y),
                  onPointerDown: (de) => We(de, Y),
                  children: [
                    /* @__PURE__ */ h.jsx("strong", { children: Ie(Y) }),
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
            f([]), g(null);
          }, children: "Root" }),
          d.map((I, z) => /* @__PURE__ */ h.jsxs(ot.Fragment, { children: [
            /* @__PURE__ */ h.jsx(En, { size: 13 }),
            /* @__PURE__ */ h.jsx("button", { type: "button", onClick: () => {
              f(d.slice(0, z + 1)), g(null);
            }, children: I.label })
          ] }, `${I.ownerNodeId}-${I.slotId}-${z}`))
        ] }),
        /* @__PURE__ */ h.jsxs("div", { className: "wf-canvas", ref: $, onDragOver: Ce, onDragLeave: Pe, onDrop: $e, children: [
          /* @__PURE__ */ h.jsx(Va.Provider, { value: fo, children: /* @__PURE__ */ h.jsxs(
            ig,
            {
              nodes: u,
              edges: p,
              nodeTypes: Sm,
              edgeTypes: Em,
              onInit: x,
              onNodesChange: to,
              onEdgesChange: no,
              onNodesDelete: co,
              onEdgesDelete: lo,
              onConnect: oo,
              onConnectStart: X?.slot.mode === "flowchart" ? io : void 0,
              onConnectEnd: X?.slot.mode === "flowchart" ? so : void 0,
              onReconnect: X?.slot.mode === "flowchart" ? ao : void 0,
              isValidConnection: $t,
              onDragOver: Ce,
              onDragLeave: Pe,
              onDrop: $e,
              onPaneClick: () => g(null),
              onNodeClick: (I, z) => g(z.id),
              onNodeDragStop: ro,
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
                /* @__PURE__ */ h.jsx(fg, { gap: 18, size: 1 }),
                /* @__PURE__ */ h.jsx(wg, {}),
                /* @__PURE__ */ h.jsx(jg, { pannable: !0, zoomable: !0 })
              ]
            }
          ) }),
          X?.slot.mode === "flowchart" && u.length === 0 ? /* @__PURE__ */ h.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => Ze(), children: [
            /* @__PURE__ */ h.jsx(mr, { size: 15 }),
            " Add activity"
          ] }) : null,
          v ? /* @__PURE__ */ h.jsx(
            Wm,
            {
              clientX: v.clientX,
              clientY: v.clientY,
              activities: l,
              onPick: uo,
              onClose: () => _(null)
            }
          ) : null
        ] }),
        /* @__PURE__ */ h.jsx(qm, { draft: s })
      ] }),
      /* @__PURE__ */ h.jsxs("aside", { className: "wf-inspector", children: [
        /* @__PURE__ */ h.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ h.jsx(Fg, { size: 15 }),
          " Inspector"
        ] }),
        T ? /* @__PURE__ */ h.jsxs("div", { className: "wf-inspector-content", children: [
          /* @__PURE__ */ h.jsx("h3", { children: u.find((I) => I.id === T.nodeId)?.data.label ?? T.nodeId }),
          /* @__PURE__ */ h.jsxs("dl", { children: [
            /* @__PURE__ */ h.jsx("dt", { children: "Node ID" }),
            /* @__PURE__ */ h.jsx("dd", { children: T.nodeId }),
            /* @__PURE__ */ h.jsx("dt", { children: "Activity version" }),
            /* @__PURE__ */ h.jsx("dd", { children: T.activityVersionId })
          ] }),
          Z.length > 0 ? /* @__PURE__ */ h.jsxs("div", { className: "wf-slot-list", children: [
            /* @__PURE__ */ h.jsx("span", { children: "Embedded slots" }),
            Z.map((I) => /* @__PURE__ */ h.jsxs("button", { type: "button", onClick: () => ho(T, I.id, `${u.find((z) => z.id === T.nodeId)?.data.label ?? T.nodeId} / ${I.label}`), children: [
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
function Ym({ data: e, selected: t }) {
  const n = e, o = n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }];
  return /* @__PURE__ */ h.jsxs("div", { className: t ? "wf-node selected" : "wf-node", children: [
    n.acceptsInbound ? /* @__PURE__ */ h.jsx(kt, { type: "target", position: J.Left }) : null,
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
        /* @__PURE__ */ h.jsx(kt, { type: "source", position: J.Right, id: r.name, style: { top: s } })
      ] }, r.name);
    })
  ] });
}
function Xm(e) {
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
    labelStyle: f
  } = e, u = ot.useContext(Va), [m, p] = ne(!1), [y, w, x] = $n({ sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: a }), S = u?.highlightedEdgeId === t;
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
        label: d,
        labelX: w,
        labelY: x,
        labelStyle: f,
        onMouseEnter: () => p(!0),
        onMouseLeave: () => p(!1)
      }
    ),
    u ? /* @__PURE__ */ h.jsx(ag, { children: /* @__PURE__ */ h.jsxs(
      "div",
      {
        className: ["wf-edge-actions", m ? "visible" : "", S ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${w}px, ${x}px)` },
        onMouseEnter: () => p(!0),
        onMouseLeave: () => p(!1),
        children: [
          /* @__PURE__ */ h.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (g) => u.requestInsertActivity(t, g.clientX, g.clientY), children: /* @__PURE__ */ h.jsx(mr, { size: 12 }) }),
          /* @__PURE__ */ h.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => u.deleteEdge(t), children: /* @__PURE__ */ h.jsx(qo, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function Wm({ clientX: e, clientY: t, activities: n, onPick: o, onClose: r }) {
  const [i, s] = ne(""), [a, l] = ne(0), c = ae(null), d = ae(null), f = ye(() => {
    const S = i.trim().toLowerCase(), g = n.filter(Wa);
    return S ? g.filter((v) => Ie(v).toLowerCase().includes(S) || v.activityTypeKey.toLowerCase().includes(S) || (v.category ?? "").toLowerCase().includes(S) || (v.description ?? "").toLowerCase().includes(S)) : g;
  }, [n, i]), u = ye(() => Fa(f), [f]), m = ye(() => u.flatMap((S) => S.activities), [u]);
  ce(() => {
    requestAnimationFrame(() => d.current?.focus());
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
        ref: d,
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
    /* @__PURE__ */ h.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: u.length === 0 ? /* @__PURE__ */ h.jsx("p", { children: "No matching activities." }) : u.map((S) => /* @__PURE__ */ h.jsxs("section", { children: [
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
              /* @__PURE__ */ h.jsx("strong", { children: Ie(g) }),
              /* @__PURE__ */ h.jsx("small", { children: g.category || g.activityTypeKey })
            ]
          },
          g.activityVersionId
        );
      })
    ] }, S.category)) })
  ] });
}
function qm({ draft: e }) {
  return e.validationErrors.length ? /* @__PURE__ */ h.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ h.jsx(Qt, { size: 14 }),
    e.validationErrors.length,
    " validation issue",
    e.validationErrors.length === 1 ? "" : "s"
  ] }) : /* @__PURE__ */ h.jsxs("div", { className: "wf-validation ok", children: [
    /* @__PURE__ */ h.jsx(Gn, { size: 14 }),
    " No validation errors"
  ] });
}
function Fi(e) {
  return `${Ie(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function Yi(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function Zm(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function Um(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function Lt(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function Uo(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
export {
  Qm as register
};
