import st, { memo as Se, forwardRef as lo, useRef as ae, useEffect as ce, useCallback as ge, useContext as dn, useMemo as ye, useState as ee, createContext as Sr, useLayoutEffect as $c, createElement as sr, useId as Tc } from "react";
import "@tanstack/react-query";
function Rc(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var Fo = { exports: {} }, Yt = {};
var Ur;
function zc() {
  if (Ur) return Yt;
  Ur = 1;
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
  return Yt.Fragment = t, Yt.jsx = n, Yt.jsxs = n, Yt;
}
var Kr;
function Lc() {
  return Kr || (Kr = 1, Fo.exports = zc()), Fo.exports;
}
var u = Lc();
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
var Hc = { value: () => {
} };
function uo() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new On(n);
}
function On(e) {
  this._ = e;
}
function Vc(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
On.prototype = uo.prototype = {
  constructor: On,
  on: function(e, t) {
    var n = this._, o = Vc(e + "", n), r, i = -1, s = o.length;
    if (arguments.length < 2) {
      for (; ++i < s; ) if ((r = (e = o[i]).type) && (r = Oc(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++i < s; )
      if (r = (e = o[i]).type) n[r] = Gr(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = Gr(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new On(e);
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
function Oc(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function Gr(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = Hc, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var ar = "http://www.w3.org/1999/xhtml";
const Qr = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: ar,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function fo(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Qr.hasOwnProperty(t) ? { space: Qr[t], local: e } : e;
}
function Bc(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === ar && t.documentElement.namespaceURI === ar ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Fc(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function gs(e) {
  var t = fo(e);
  return (t.local ? Fc : Bc)(t);
}
function Wc() {
}
function Nr(e) {
  return e == null ? Wc : function() {
    return this.querySelector(e);
  };
}
function Yc(e) {
  typeof e != "function" && (e = Nr(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = new Array(s), l, c, d = 0; d < s; ++d)
      (l = i[d]) && (c = e.call(l, l.__data__, d, i)) && ("__data__" in l && (c.__data__ = l.__data__), a[d] = c);
  return new ze(o, this._parents);
}
function Xc(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function qc() {
  return [];
}
function ys(e) {
  return e == null ? qc : function() {
    return this.querySelectorAll(e);
  };
}
function Zc(e) {
  return function() {
    return Xc(e.apply(this, arguments));
  };
}
function Uc(e) {
  typeof e == "function" ? e = Zc(e) : e = ys(e);
  for (var t = this._groups, n = t.length, o = [], r = [], i = 0; i < n; ++i)
    for (var s = t[i], a = s.length, l, c = 0; c < a; ++c)
      (l = s[c]) && (o.push(e.call(l, l.__data__, c, s)), r.push(l));
  return new ze(o, r);
}
function ms(e) {
  return function() {
    return this.matches(e);
  };
}
function xs(e) {
  return function(t) {
    return t.matches(e);
  };
}
var Kc = Array.prototype.find;
function Gc(e) {
  return function() {
    return Kc.call(this.children, e);
  };
}
function Qc() {
  return this.firstElementChild;
}
function Jc(e) {
  return this.select(e == null ? Qc : Gc(typeof e == "function" ? e : xs(e)));
}
var el = Array.prototype.filter;
function tl() {
  return Array.from(this.children);
}
function nl(e) {
  return function() {
    return el.call(this.children, e);
  };
}
function ol(e) {
  return this.selectAll(e == null ? tl : nl(typeof e == "function" ? e : xs(e)));
}
function rl(e) {
  typeof e != "function" && (e = ms(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = [], l, c = 0; c < s; ++c)
      (l = i[c]) && e.call(l, l.__data__, c, i) && a.push(l);
  return new ze(o, this._parents);
}
function ws(e) {
  return new Array(e.length);
}
function il() {
  return new ze(this._enter || this._groups.map(ws), this._parents);
}
function Un(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
Un.prototype = {
  constructor: Un,
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
function sl(e) {
  return function() {
    return e;
  };
}
function al(e, t, n, o, r, i) {
  for (var s = 0, a, l = t.length, c = i.length; s < c; ++s)
    (a = t[s]) ? (a.__data__ = i[s], o[s] = a) : n[s] = new Un(e, i[s]);
  for (; s < l; ++s)
    (a = t[s]) && (r[s] = a);
}
function cl(e, t, n, o, r, i, s) {
  var a, l, c = /* @__PURE__ */ new Map(), d = t.length, f = i.length, h = new Array(d), y;
  for (a = 0; a < d; ++a)
    (l = t[a]) && (h[a] = y = s.call(l, l.__data__, a, t) + "", c.has(y) ? r[a] = l : c.set(y, l));
  for (a = 0; a < f; ++a)
    y = s.call(e, i[a], a, i) + "", (l = c.get(y)) ? (o[a] = l, l.__data__ = i[a], c.delete(y)) : n[a] = new Un(e, i[a]);
  for (a = 0; a < d; ++a)
    (l = t[a]) && c.get(h[a]) === l && (r[a] = l);
}
function ll(e) {
  return e.__data__;
}
function ul(e, t) {
  if (!arguments.length) return Array.from(this, ll);
  var n = t ? cl : al, o = this._parents, r = this._groups;
  typeof e != "function" && (e = sl(e));
  for (var i = r.length, s = new Array(i), a = new Array(i), l = new Array(i), c = 0; c < i; ++c) {
    var d = o[c], f = r[c], h = f.length, y = dl(e.call(d, d && d.__data__, c, o)), g = y.length, x = a[c] = new Array(g), w = s[c] = new Array(g), m = l[c] = new Array(h);
    n(d, f, x, w, m, y, t);
    for (var S = 0, p = 0, v, E; S < g; ++S)
      if (v = x[S]) {
        for (S >= p && (p = S + 1); !(E = w[p]) && ++p < g; ) ;
        v._next = E || null;
      }
  }
  return s = new ze(s, o), s._enter = a, s._exit = l, s;
}
function dl(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function fl() {
  return new ze(this._exit || this._groups.map(ws), this._parents);
}
function hl(e, t, n) {
  var o = this.enter(), r = this, i = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? i.remove() : n(i), o && r ? o.merge(r).order() : r;
}
function pl(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, i = o.length, s = Math.min(r, i), a = new Array(r), l = 0; l < s; ++l)
    for (var c = n[l], d = o[l], f = c.length, h = a[l] = new Array(f), y, g = 0; g < f; ++g)
      (y = c[g] || d[g]) && (h[g] = y);
  for (; l < r; ++l)
    a[l] = n[l];
  return new ze(a, this._parents);
}
function gl() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, i = o[r], s; --r >= 0; )
      (s = o[r]) && (i && s.compareDocumentPosition(i) ^ 4 && i.parentNode.insertBefore(s, i), i = s);
  return this;
}
function yl(e) {
  e || (e = ml);
  function t(f, h) {
    return f && h ? e(f.__data__, h.__data__) : !f - !h;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), i = 0; i < o; ++i) {
    for (var s = n[i], a = s.length, l = r[i] = new Array(a), c, d = 0; d < a; ++d)
      (c = s[d]) && (l[d] = c);
    l.sort(t);
  }
  return new ze(r, this._parents).order();
}
function ml(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function xl() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function wl() {
  return Array.from(this);
}
function vl() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length; r < i; ++r) {
      var s = o[r];
      if (s) return s;
    }
  return null;
}
function bl() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function Sl() {
  return !this.node();
}
function Nl(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], i = 0, s = r.length, a; i < s; ++i)
      (a = r[i]) && e.call(a, a.__data__, i, r);
  return this;
}
function El(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function _l(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Cl(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function kl(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function Il(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function Al(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function Ml(e, t) {
  var n = fo(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? _l : El : typeof t == "function" ? n.local ? Al : Il : n.local ? kl : Cl)(n, t));
}
function vs(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function jl(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Dl(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function Pl(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function $l(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? jl : typeof t == "function" ? Pl : Dl)(e, t, n ?? "")) : Et(this.node(), e);
}
function Et(e, t) {
  return e.style.getPropertyValue(t) || vs(e).getComputedStyle(e, null).getPropertyValue(t);
}
function Tl(e) {
  return function() {
    delete this[e];
  };
}
function Rl(e, t) {
  return function() {
    this[e] = t;
  };
}
function zl(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function Ll(e, t) {
  return arguments.length > 1 ? this.each((t == null ? Tl : typeof t == "function" ? zl : Rl)(e, t)) : this.node()[e];
}
function bs(e) {
  return e.trim().split(/^|\s+/);
}
function Er(e) {
  return e.classList || new Ss(e);
}
function Ss(e) {
  this._node = e, this._names = bs(e.getAttribute("class") || "");
}
Ss.prototype = {
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
function Ns(e, t) {
  for (var n = Er(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function Es(e, t) {
  for (var n = Er(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function Hl(e) {
  return function() {
    Ns(this, e);
  };
}
function Vl(e) {
  return function() {
    Es(this, e);
  };
}
function Ol(e, t) {
  return function() {
    (t.apply(this, arguments) ? Ns : Es)(this, e);
  };
}
function Bl(e, t) {
  var n = bs(e + "");
  if (arguments.length < 2) {
    for (var o = Er(this.node()), r = -1, i = n.length; ++r < i; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? Ol : t ? Hl : Vl)(n, t));
}
function Fl() {
  this.textContent = "";
}
function Wl(e) {
  return function() {
    this.textContent = e;
  };
}
function Yl(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function Xl(e) {
  return arguments.length ? this.each(e == null ? Fl : (typeof e == "function" ? Yl : Wl)(e)) : this.node().textContent;
}
function ql() {
  this.innerHTML = "";
}
function Zl(e) {
  return function() {
    this.innerHTML = e;
  };
}
function Ul(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function Kl(e) {
  return arguments.length ? this.each(e == null ? ql : (typeof e == "function" ? Ul : Zl)(e)) : this.node().innerHTML;
}
function Gl() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function Ql() {
  return this.each(Gl);
}
function Jl() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function eu() {
  return this.each(Jl);
}
function tu(e) {
  var t = typeof e == "function" ? e : gs(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function nu() {
  return null;
}
function ou(e, t) {
  var n = typeof e == "function" ? e : gs(e), o = t == null ? nu : typeof t == "function" ? t : Nr(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function ru() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function iu() {
  return this.each(ru);
}
function su() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function au() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function cu(e) {
  return this.select(e ? au : su);
}
function lu(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function uu(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function du(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function fu(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, i; n < r; ++n)
        i = t[n], (!e.type || i.type === e.type) && i.name === e.name ? this.removeEventListener(i.type, i.listener, i.options) : t[++o] = i;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function hu(e, t, n) {
  return function() {
    var o = this.__on, r, i = uu(t);
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
function pu(e, t, n) {
  var o = du(e + ""), r, i = o.length, s;
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
  for (a = t ? hu : fu, r = 0; r < i; ++r) this.each(a(o[r], t, n));
  return this;
}
function _s(e, t, n) {
  var o = vs(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function gu(e, t) {
  return function() {
    return _s(this, e, t);
  };
}
function yu(e, t) {
  return function() {
    return _s(this, e, t.apply(this, arguments));
  };
}
function mu(e, t) {
  return this.each((typeof t == "function" ? yu : gu)(e, t));
}
function* xu() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length, s; r < i; ++r)
      (s = o[r]) && (yield s);
}
var Cs = [null];
function ze(e, t) {
  this._groups = e, this._parents = t;
}
function fn() {
  return new ze([[document.documentElement]], Cs);
}
function wu() {
  return this;
}
ze.prototype = fn.prototype = {
  constructor: ze,
  select: Yc,
  selectAll: Uc,
  selectChild: Jc,
  selectChildren: ol,
  filter: rl,
  data: ul,
  enter: il,
  exit: fl,
  join: hl,
  merge: pl,
  selection: wu,
  order: gl,
  sort: yl,
  call: xl,
  nodes: wl,
  node: vl,
  size: bl,
  empty: Sl,
  each: Nl,
  attr: Ml,
  style: $l,
  property: Ll,
  classed: Bl,
  text: Xl,
  html: Kl,
  raise: Ql,
  lower: eu,
  append: tu,
  insert: ou,
  remove: iu,
  clone: cu,
  datum: lu,
  on: pu,
  dispatch: mu,
  [Symbol.iterator]: xu
};
function Re(e) {
  return typeof e == "string" ? new ze([[document.querySelector(e)]], [document.documentElement]) : new ze([[e]], Cs);
}
function vu(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Be(e, t) {
  if (e = vu(e), t === void 0 && (t = e.currentTarget), t) {
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
const bu = { passive: !1 }, en = { capture: !0, passive: !1 };
function Wo(e) {
  e.stopImmediatePropagation();
}
function St(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function ks(e) {
  var t = e.document.documentElement, n = Re(e).on("dragstart.drag", St, en);
  "onselectstart" in t ? n.on("selectstart.drag", St, en) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function Is(e, t) {
  var n = e.document.documentElement, o = Re(e).on("dragstart.drag", null);
  t && (o.on("click.drag", St, en), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const jn = (e) => () => e;
function cr(e, {
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
cr.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function Su(e) {
  return !e.ctrlKey && !e.button;
}
function Nu() {
  return this.parentNode;
}
function Eu(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function _u() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function As() {
  var e = Su, t = Nu, n = Eu, o = _u, r = {}, i = uo("start", "drag", "end"), s = 0, a, l, c, d, f = 0;
  function h(v) {
    v.on("mousedown.drag", y).filter(o).on("touchstart.drag", w).on("touchmove.drag", m, bu).on("touchend.drag touchcancel.drag", S).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function y(v, E) {
    if (!(d || !e.call(this, v, E))) {
      var N = p(this, t.call(this, v, E), v, E, "mouse");
      N && (Re(v.view).on("mousemove.drag", g, en).on("mouseup.drag", x, en), ks(v.view), Wo(v), c = !1, a = v.clientX, l = v.clientY, N("start", v));
    }
  }
  function g(v) {
    if (St(v), !c) {
      var E = v.clientX - a, N = v.clientY - l;
      c = E * E + N * N > f;
    }
    r.mouse("drag", v);
  }
  function x(v) {
    Re(v.view).on("mousemove.drag mouseup.drag", null), Is(v.view, c), St(v), r.mouse("end", v);
  }
  function w(v, E) {
    if (e.call(this, v, E)) {
      var N = v.changedTouches, I = t.call(this, v, E), j = N.length, D, H;
      for (D = 0; D < j; ++D)
        (H = p(this, I, v, E, N[D].identifier, N[D])) && (Wo(v), H("start", v, N[D]));
    }
  }
  function m(v) {
    var E = v.changedTouches, N = E.length, I, j;
    for (I = 0; I < N; ++I)
      (j = r[E[I].identifier]) && (St(v), j("drag", v, E[I]));
  }
  function S(v) {
    var E = v.changedTouches, N = E.length, I, j;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), I = 0; I < N; ++I)
      (j = r[E[I].identifier]) && (Wo(v), j("end", v, E[I]));
  }
  function p(v, E, N, I, j, D) {
    var H = i.copy(), A = Be(D || N, E), R, V, b;
    if ((b = n.call(v, new cr("beforestart", {
      sourceEvent: N,
      target: h,
      identifier: j,
      active: s,
      x: A[0],
      y: A[1],
      dx: 0,
      dy: 0,
      dispatch: H
    }), I)) != null)
      return R = b.x - A[0] || 0, V = b.y - A[1] || 0, function C(_, M, P) {
        var $ = A, O;
        switch (_) {
          case "start":
            r[j] = C, O = s++;
            break;
          case "end":
            delete r[j], --s;
          // falls through
          case "drag":
            A = Be(P || M, E), O = s;
            break;
        }
        H.call(
          _,
          v,
          new cr(_, {
            sourceEvent: M,
            subject: b,
            target: h,
            identifier: j,
            active: O,
            x: A[0] + R,
            y: A[1] + V,
            dx: A[0] - $[0],
            dy: A[1] - $[1],
            dispatch: H
          }),
          I
        );
      };
  }
  return h.filter = function(v) {
    return arguments.length ? (e = typeof v == "function" ? v : jn(!!v), h) : e;
  }, h.container = function(v) {
    return arguments.length ? (t = typeof v == "function" ? v : jn(v), h) : t;
  }, h.subject = function(v) {
    return arguments.length ? (n = typeof v == "function" ? v : jn(v), h) : n;
  }, h.touchable = function(v) {
    return arguments.length ? (o = typeof v == "function" ? v : jn(!!v), h) : o;
  }, h.on = function() {
    var v = i.on.apply(i, arguments);
    return v === i ? h : v;
  }, h.clickDistance = function(v) {
    return arguments.length ? (f = (v = +v) * v, h) : Math.sqrt(f);
  }, h;
}
function _r(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function Ms(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function hn() {
}
var tn = 0.7, Kn = 1 / tn, Nt = "\\s*([+-]?\\d+)\\s*", nn = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", Ze = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", Cu = /^#([0-9a-f]{3,8})$/, ku = new RegExp(`^rgb\\(${Nt},${Nt},${Nt}\\)$`), Iu = new RegExp(`^rgb\\(${Ze},${Ze},${Ze}\\)$`), Au = new RegExp(`^rgba\\(${Nt},${Nt},${Nt},${nn}\\)$`), Mu = new RegExp(`^rgba\\(${Ze},${Ze},${Ze},${nn}\\)$`), ju = new RegExp(`^hsl\\(${nn},${Ze},${Ze}\\)$`), Du = new RegExp(`^hsla\\(${nn},${Ze},${Ze},${nn}\\)$`), Jr = {
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
_r(hn, ft, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: ei,
  // Deprecated! Use color.formatHex.
  formatHex: ei,
  formatHex8: Pu,
  formatHsl: $u,
  formatRgb: ti,
  toString: ti
});
function ei() {
  return this.rgb().formatHex();
}
function Pu() {
  return this.rgb().formatHex8();
}
function $u() {
  return js(this).formatHsl();
}
function ti() {
  return this.rgb().formatRgb();
}
function ft(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = Cu.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? ni(t) : n === 3 ? new je(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? Dn(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? Dn(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = ku.exec(e)) ? new je(t[1], t[2], t[3], 1) : (t = Iu.exec(e)) ? new je(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = Au.exec(e)) ? Dn(t[1], t[2], t[3], t[4]) : (t = Mu.exec(e)) ? Dn(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = ju.exec(e)) ? ii(t[1], t[2] / 100, t[3] / 100, 1) : (t = Du.exec(e)) ? ii(t[1], t[2] / 100, t[3] / 100, t[4]) : Jr.hasOwnProperty(e) ? ni(Jr[e]) : e === "transparent" ? new je(NaN, NaN, NaN, 0) : null;
}
function ni(e) {
  return new je(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function Dn(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new je(e, t, n, o);
}
function Tu(e) {
  return e instanceof hn || (e = ft(e)), e ? (e = e.rgb(), new je(e.r, e.g, e.b, e.opacity)) : new je();
}
function lr(e, t, n, o) {
  return arguments.length === 1 ? Tu(e) : new je(e, t, n, o ?? 1);
}
function je(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
_r(je, lr, Ms(hn, {
  brighter(e) {
    return e = e == null ? Kn : Math.pow(Kn, e), new je(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? tn : Math.pow(tn, e), new je(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new je(ut(this.r), ut(this.g), ut(this.b), Gn(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: oi,
  // Deprecated! Use color.formatHex.
  formatHex: oi,
  formatHex8: Ru,
  formatRgb: ri,
  toString: ri
}));
function oi() {
  return `#${lt(this.r)}${lt(this.g)}${lt(this.b)}`;
}
function Ru() {
  return `#${lt(this.r)}${lt(this.g)}${lt(this.b)}${lt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function ri() {
  const e = Gn(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${ut(this.r)}, ${ut(this.g)}, ${ut(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Gn(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function ut(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function lt(e) {
  return e = ut(e), (e < 16 ? "0" : "") + e.toString(16);
}
function ii(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Fe(e, t, n, o);
}
function js(e) {
  if (e instanceof Fe) return new Fe(e.h, e.s, e.l, e.opacity);
  if (e instanceof hn || (e = ft(e)), !e) return new Fe();
  if (e instanceof Fe) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), i = Math.max(t, n, o), s = NaN, a = i - r, l = (i + r) / 2;
  return a ? (t === i ? s = (n - o) / a + (n < o) * 6 : n === i ? s = (o - t) / a + 2 : s = (t - n) / a + 4, a /= l < 0.5 ? i + r : 2 - i - r, s *= 60) : a = l > 0 && l < 1 ? 0 : s, new Fe(s, a, l, e.opacity);
}
function zu(e, t, n, o) {
  return arguments.length === 1 ? js(e) : new Fe(e, t, n, o ?? 1);
}
function Fe(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
_r(Fe, zu, Ms(hn, {
  brighter(e) {
    return e = e == null ? Kn : Math.pow(Kn, e), new Fe(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? tn : Math.pow(tn, e), new Fe(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new je(
      Yo(e >= 240 ? e - 240 : e + 120, r, o),
      Yo(e, r, o),
      Yo(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new Fe(si(this.h), Pn(this.s), Pn(this.l), Gn(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Gn(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${si(this.h)}, ${Pn(this.s) * 100}%, ${Pn(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function si(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function Pn(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function Yo(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const Cr = (e) => () => e;
function Lu(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function Hu(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function Vu(e) {
  return (e = +e) == 1 ? Ds : function(t, n) {
    return n - t ? Hu(t, n, e) : Cr(isNaN(t) ? n : t);
  };
}
function Ds(e, t) {
  var n = t - e;
  return n ? Lu(e, n) : Cr(isNaN(e) ? t : e);
}
const Qn = (function e(t) {
  var n = Vu(t);
  function o(r, i) {
    var s = n((r = lr(r)).r, (i = lr(i)).r), a = n(r.g, i.g), l = n(r.b, i.b), c = Ds(r.opacity, i.opacity);
    return function(d) {
      return r.r = s(d), r.g = a(d), r.b = l(d), r.opacity = c(d), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function Ou(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(i) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - i) + t[r] * i;
    return o;
  };
}
function Bu(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Fu(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), i = new Array(n), s;
  for (s = 0; s < o; ++s) r[s] = Qt(e[s], t[s]);
  for (; s < n; ++s) i[s] = t[s];
  return function(a) {
    for (s = 0; s < o; ++s) i[s] = r[s](a);
    return i;
  };
}
function Wu(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(o) {
    return n.setTime(e * (1 - o) + t * o), n;
  };
}
function qe(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function Yu(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = Qt(e[r], t[r]) : o[r] = t[r];
  return function(i) {
    for (r in n) o[r] = n[r](i);
    return o;
  };
}
var ur = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, Xo = new RegExp(ur.source, "g");
function Xu(e) {
  return function() {
    return e;
  };
}
function qu(e) {
  return function(t) {
    return e(t) + "";
  };
}
function Ps(e, t) {
  var n = ur.lastIndex = Xo.lastIndex = 0, o, r, i, s = -1, a = [], l = [];
  for (e = e + "", t = t + ""; (o = ur.exec(e)) && (r = Xo.exec(t)); )
    (i = r.index) > n && (i = t.slice(n, i), a[s] ? a[s] += i : a[++s] = i), (o = o[0]) === (r = r[0]) ? a[s] ? a[s] += r : a[++s] = r : (a[++s] = null, l.push({ i: s, x: qe(o, r) })), n = Xo.lastIndex;
  return n < t.length && (i = t.slice(n), a[s] ? a[s] += i : a[++s] = i), a.length < 2 ? l[0] ? qu(l[0].x) : Xu(t) : (t = l.length, function(c) {
    for (var d = 0, f; d < t; ++d) a[(f = l[d]).i] = f.x(c);
    return a.join("");
  });
}
function Qt(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? Cr(t) : (n === "number" ? qe : n === "string" ? (o = ft(t)) ? (t = o, Qn) : Ps : t instanceof ft ? Qn : t instanceof Date ? Wu : Bu(t) ? Ou : Array.isArray(t) ? Fu : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Yu : qe)(e, t);
}
var ai = 180 / Math.PI, dr = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function $s(e, t, n, o, r, i) {
  var s, a, l;
  return (s = Math.sqrt(e * e + t * t)) && (e /= s, t /= s), (l = e * n + t * o) && (n -= e * l, o -= t * l), (a = Math.sqrt(n * n + o * o)) && (n /= a, o /= a, l /= a), e * o < t * n && (e = -e, t = -t, l = -l, s = -s), {
    translateX: r,
    translateY: i,
    rotate: Math.atan2(t, e) * ai,
    skewX: Math.atan(l) * ai,
    scaleX: s,
    scaleY: a
  };
}
var $n;
function Zu(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? dr : $s(t.a, t.b, t.c, t.d, t.e, t.f);
}
function Uu(e) {
  return e == null || ($n || ($n = document.createElementNS("http://www.w3.org/2000/svg", "g")), $n.setAttribute("transform", e), !(e = $n.transform.baseVal.consolidate())) ? dr : (e = e.matrix, $s(e.a, e.b, e.c, e.d, e.e, e.f));
}
function Ts(e, t, n, o) {
  function r(c) {
    return c.length ? c.pop() + " " : "";
  }
  function i(c, d, f, h, y, g) {
    if (c !== f || d !== h) {
      var x = y.push("translate(", null, t, null, n);
      g.push({ i: x - 4, x: qe(c, f) }, { i: x - 2, x: qe(d, h) });
    } else (f || h) && y.push("translate(" + f + t + h + n);
  }
  function s(c, d, f, h) {
    c !== d ? (c - d > 180 ? d += 360 : d - c > 180 && (c += 360), h.push({ i: f.push(r(f) + "rotate(", null, o) - 2, x: qe(c, d) })) : d && f.push(r(f) + "rotate(" + d + o);
  }
  function a(c, d, f, h) {
    c !== d ? h.push({ i: f.push(r(f) + "skewX(", null, o) - 2, x: qe(c, d) }) : d && f.push(r(f) + "skewX(" + d + o);
  }
  function l(c, d, f, h, y, g) {
    if (c !== f || d !== h) {
      var x = y.push(r(y) + "scale(", null, ",", null, ")");
      g.push({ i: x - 4, x: qe(c, f) }, { i: x - 2, x: qe(d, h) });
    } else (f !== 1 || h !== 1) && y.push(r(y) + "scale(" + f + "," + h + ")");
  }
  return function(c, d) {
    var f = [], h = [];
    return c = e(c), d = e(d), i(c.translateX, c.translateY, d.translateX, d.translateY, f, h), s(c.rotate, d.rotate, f, h), a(c.skewX, d.skewX, f, h), l(c.scaleX, c.scaleY, d.scaleX, d.scaleY, f, h), c = d = null, function(y) {
      for (var g = -1, x = h.length, w; ++g < x; ) f[(w = h[g]).i] = w.x(y);
      return f.join("");
    };
  };
}
var Ku = Ts(Zu, "px, ", "px)", "deg)"), Gu = Ts(Uu, ", ", ")", ")"), Qu = 1e-12;
function ci(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Ju(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function ed(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const Bn = (function e(t, n, o) {
  function r(i, s) {
    var a = i[0], l = i[1], c = i[2], d = s[0], f = s[1], h = s[2], y = d - a, g = f - l, x = y * y + g * g, w, m;
    if (x < Qu)
      m = Math.log(h / c) / t, w = function(I) {
        return [
          a + I * y,
          l + I * g,
          c * Math.exp(t * I * m)
        ];
      };
    else {
      var S = Math.sqrt(x), p = (h * h - c * c + o * x) / (2 * c * n * S), v = (h * h - c * c - o * x) / (2 * h * n * S), E = Math.log(Math.sqrt(p * p + 1) - p), N = Math.log(Math.sqrt(v * v + 1) - v);
      m = (N - E) / t, w = function(I) {
        var j = I * m, D = ci(E), H = c / (n * S) * (D * ed(t * j + E) - Ju(E));
        return [
          a + H * y,
          l + H * g,
          c * D / ci(t * j + E)
        ];
      };
    }
    return w.duration = m * 1e3 * t / Math.SQRT2, w;
  }
  return r.rho = function(i) {
    var s = Math.max(1e-3, +i), a = s * s, l = a * a;
    return e(s, a, l);
  }, r;
})(Math.SQRT2, 2, 4);
var _t = 0, Ut = 0, Xt = 0, Rs = 1e3, Jn, Kt, eo = 0, ht = 0, ho = 0, on = typeof performance == "object" && performance.now ? performance : Date, zs = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function kr() {
  return ht || (zs(td), ht = on.now() + ho);
}
function td() {
  ht = 0;
}
function to() {
  this._call = this._time = this._next = null;
}
to.prototype = Ls.prototype = {
  constructor: to,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? kr() : +n) + (t == null ? 0 : +t), !this._next && Kt !== this && (Kt ? Kt._next = this : Jn = this, Kt = this), this._call = e, this._time = n, fr();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, fr());
  }
};
function Ls(e, t, n) {
  var o = new to();
  return o.restart(e, t, n), o;
}
function nd() {
  kr(), ++_t;
  for (var e = Jn, t; e; )
    (t = ht - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --_t;
}
function li() {
  ht = (eo = on.now()) + ho, _t = Ut = 0;
  try {
    nd();
  } finally {
    _t = 0, rd(), ht = 0;
  }
}
function od() {
  var e = on.now(), t = e - eo;
  t > Rs && (ho -= t, eo = e);
}
function rd() {
  for (var e, t = Jn, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Jn = n);
  Kt = e, fr(o);
}
function fr(e) {
  if (!_t) {
    Ut && (Ut = clearTimeout(Ut));
    var t = e - ht;
    t > 24 ? (e < 1 / 0 && (Ut = setTimeout(li, e - on.now() - ho)), Xt && (Xt = clearInterval(Xt))) : (Xt || (eo = on.now(), Xt = setInterval(od, Rs)), _t = 1, zs(li));
  }
}
function ui(e, t, n) {
  var o = new to();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var id = uo("start", "end", "cancel", "interrupt"), sd = [], Hs = 0, di = 1, hr = 2, Fn = 3, fi = 4, pr = 5, Wn = 6;
function po(e, t, n, o, r, i) {
  var s = e.__transition;
  if (!s) e.__transition = {};
  else if (n in s) return;
  ad(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: id,
    tween: sd,
    time: i.time,
    delay: i.delay,
    duration: i.duration,
    ease: i.ease,
    timer: null,
    state: Hs
  });
}
function Ir(e, t) {
  var n = Xe(e, t);
  if (n.state > Hs) throw new Error("too late; already scheduled");
  return n;
}
function Ke(e, t) {
  var n = Xe(e, t);
  if (n.state > Fn) throw new Error("too late; already running");
  return n;
}
function Xe(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function ad(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = Ls(i, 0, n.time);
  function i(c) {
    n.state = di, n.timer.restart(s, n.delay, n.time), n.delay <= c && s(c - n.delay);
  }
  function s(c) {
    var d, f, h, y;
    if (n.state !== di) return l();
    for (d in o)
      if (y = o[d], y.name === n.name) {
        if (y.state === Fn) return ui(s);
        y.state === fi ? (y.state = Wn, y.timer.stop(), y.on.call("interrupt", e, e.__data__, y.index, y.group), delete o[d]) : +d < t && (y.state = Wn, y.timer.stop(), y.on.call("cancel", e, e.__data__, y.index, y.group), delete o[d]);
      }
    if (ui(function() {
      n.state === Fn && (n.state = fi, n.timer.restart(a, n.delay, n.time), a(c));
    }), n.state = hr, n.on.call("start", e, e.__data__, n.index, n.group), n.state === hr) {
      for (n.state = Fn, r = new Array(h = n.tween.length), d = 0, f = -1; d < h; ++d)
        (y = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (r[++f] = y);
      r.length = f + 1;
    }
  }
  function a(c) {
    for (var d = c < n.duration ? n.ease.call(null, c / n.duration) : (n.timer.restart(l), n.state = pr, 1), f = -1, h = r.length; ++f < h; )
      r[f].call(e, d);
    n.state === pr && (n.on.call("end", e, e.__data__, n.index, n.group), l());
  }
  function l() {
    n.state = Wn, n.timer.stop(), delete o[t];
    for (var c in o) return;
    delete e.__transition;
  }
}
function Yn(e, t) {
  var n = e.__transition, o, r, i = !0, s;
  if (n) {
    t = t == null ? null : t + "";
    for (s in n) {
      if ((o = n[s]).name !== t) {
        i = !1;
        continue;
      }
      r = o.state > hr && o.state < pr, o.state = Wn, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[s];
    }
    i && delete e.__transition;
  }
}
function cd(e) {
  return this.each(function() {
    Yn(this, e);
  });
}
function ld(e, t) {
  var n, o;
  return function() {
    var r = Ke(this, e), i = r.tween;
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
function ud(e, t, n) {
  var o, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var i = Ke(this, e), s = i.tween;
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
function dd(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = Xe(this.node(), n).tween, r = 0, i = o.length, s; r < i; ++r)
      if ((s = o[r]).name === e)
        return s.value;
    return null;
  }
  return this.each((t == null ? ld : ud)(n, e, t));
}
function Ar(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = Ke(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return Xe(r, o).value[t];
  };
}
function Vs(e, t) {
  var n;
  return (typeof t == "number" ? qe : t instanceof ft ? Qn : (n = ft(t)) ? (t = n, Qn) : Ps)(e, t);
}
function fd(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function hd(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function pd(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttribute(e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function gd(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = this.getAttributeNS(e.space, e.local);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function yd(e, t, n) {
  var o, r, i;
  return function() {
    var s, a = n(this), l;
    return a == null ? void this.removeAttribute(e) : (s = this.getAttribute(e), l = a + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a)));
  };
}
function md(e, t, n) {
  var o, r, i;
  return function() {
    var s, a = n(this), l;
    return a == null ? void this.removeAttributeNS(e.space, e.local) : (s = this.getAttributeNS(e.space, e.local), l = a + "", s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a)));
  };
}
function xd(e, t) {
  var n = fo(e), o = n === "transform" ? Gu : Vs;
  return this.attrTween(e, typeof t == "function" ? (n.local ? md : yd)(n, o, Ar(this, "attr." + e, t)) : t == null ? (n.local ? hd : fd)(n) : (n.local ? gd : pd)(n, o, t));
}
function wd(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function vd(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function bd(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && vd(e, i)), n;
  }
  return r._value = t, r;
}
function Sd(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && wd(e, i)), n;
  }
  return r._value = t, r;
}
function Nd(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = fo(e);
  return this.tween(n, (o.local ? bd : Sd)(o, t));
}
function Ed(e, t) {
  return function() {
    Ir(this, e).delay = +t.apply(this, arguments);
  };
}
function _d(e, t) {
  return t = +t, function() {
    Ir(this, e).delay = t;
  };
}
function Cd(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Ed : _d)(t, e)) : Xe(this.node(), t).delay;
}
function kd(e, t) {
  return function() {
    Ke(this, e).duration = +t.apply(this, arguments);
  };
}
function Id(e, t) {
  return t = +t, function() {
    Ke(this, e).duration = t;
  };
}
function Ad(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? kd : Id)(t, e)) : Xe(this.node(), t).duration;
}
function Md(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    Ke(this, e).ease = t;
  };
}
function jd(e) {
  var t = this._id;
  return arguments.length ? this.each(Md(t, e)) : Xe(this.node(), t).ease;
}
function Dd(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    Ke(this, e).ease = n;
  };
}
function Pd(e) {
  if (typeof e != "function") throw new Error();
  return this.each(Dd(this._id, e));
}
function $d(e) {
  typeof e != "function" && (e = ms(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], s = i.length, a = o[r] = [], l, c = 0; c < s; ++c)
      (l = i[c]) && e.call(l, l.__data__, c, i) && a.push(l);
  return new Je(o, this._parents, this._name, this._id);
}
function Td(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, i = Math.min(o, r), s = new Array(o), a = 0; a < i; ++a)
    for (var l = t[a], c = n[a], d = l.length, f = s[a] = new Array(d), h, y = 0; y < d; ++y)
      (h = l[y] || c[y]) && (f[y] = h);
  for (; a < o; ++a)
    s[a] = t[a];
  return new Je(s, this._parents, this._name, this._id);
}
function Rd(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function zd(e, t, n) {
  var o, r, i = Rd(t) ? Ir : Ke;
  return function() {
    var s = i(this, e), a = s.on;
    a !== o && (r = (o = a).copy()).on(t, n), s.on = r;
  };
}
function Ld(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Xe(this.node(), n).on.on(e) : this.each(zd(n, e, t));
}
function Hd(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function Vd() {
  return this.on("end.remove", Hd(this._id));
}
function Od(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Nr(e));
  for (var o = this._groups, r = o.length, i = new Array(r), s = 0; s < r; ++s)
    for (var a = o[s], l = a.length, c = i[s] = new Array(l), d, f, h = 0; h < l; ++h)
      (d = a[h]) && (f = e.call(d, d.__data__, h, a)) && ("__data__" in d && (f.__data__ = d.__data__), c[h] = f, po(c[h], t, n, h, c, Xe(d, n)));
  return new Je(i, this._parents, t, n);
}
function Bd(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = ys(e));
  for (var o = this._groups, r = o.length, i = [], s = [], a = 0; a < r; ++a)
    for (var l = o[a], c = l.length, d, f = 0; f < c; ++f)
      if (d = l[f]) {
        for (var h = e.call(d, d.__data__, f, l), y, g = Xe(d, n), x = 0, w = h.length; x < w; ++x)
          (y = h[x]) && po(y, t, n, x, h, g);
        i.push(h), s.push(d);
      }
  return new Je(i, s, t, n);
}
var Fd = fn.prototype.constructor;
function Wd() {
  return new Fd(this._groups, this._parents);
}
function Yd(e, t) {
  var n, o, r;
  return function() {
    var i = Et(this, e), s = (this.style.removeProperty(e), Et(this, e));
    return i === s ? null : i === n && s === o ? r : r = t(n = i, o = s);
  };
}
function Os(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Xd(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var s = Et(this, e);
    return s === r ? null : s === o ? i : i = t(o = s, n);
  };
}
function qd(e, t, n) {
  var o, r, i;
  return function() {
    var s = Et(this, e), a = n(this), l = a + "";
    return a == null && (l = a = (this.style.removeProperty(e), Et(this, e))), s === l ? null : s === o && l === r ? i : (r = l, i = t(o = s, a));
  };
}
function Zd(e, t) {
  var n, o, r, i = "style." + t, s = "end." + i, a;
  return function() {
    var l = Ke(this, e), c = l.on, d = l.value[i] == null ? a || (a = Os(t)) : void 0;
    (c !== n || r !== d) && (o = (n = c).copy()).on(s, r = d), l.on = o;
  };
}
function Ud(e, t, n) {
  var o = (e += "") == "transform" ? Ku : Vs;
  return t == null ? this.styleTween(e, Yd(e, o)).on("end.style." + e, Os(e)) : typeof t == "function" ? this.styleTween(e, qd(e, o, Ar(this, "style." + e, t))).each(Zd(this._id, e)) : this.styleTween(e, Xd(e, o, t), n).on("end.style." + e, null);
}
function Kd(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function Gd(e, t, n) {
  var o, r;
  function i() {
    var s = t.apply(this, arguments);
    return s !== r && (o = (r = s) && Kd(e, s, n)), o;
  }
  return i._value = t, i;
}
function Qd(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, Gd(e, t, n ?? ""));
}
function Jd(e) {
  return function() {
    this.textContent = e;
  };
}
function ef(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function tf(e) {
  return this.tween("text", typeof e == "function" ? ef(Ar(this, "text", e)) : Jd(e == null ? "" : e + ""));
}
function nf(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function of(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && nf(r)), t;
  }
  return o._value = e, o;
}
function rf(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, of(e));
}
function sf() {
  for (var e = this._name, t = this._id, n = Bs(), o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], a = s.length, l, c = 0; c < a; ++c)
      if (l = s[c]) {
        var d = Xe(l, t);
        po(l, e, n, c, s, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new Je(o, this._parents, e, n);
}
function af() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(i, s) {
    var a = { value: s }, l = { value: function() {
      --r === 0 && i();
    } };
    n.each(function() {
      var c = Ke(this, o), d = c.on;
      d !== e && (t = (e = d).copy(), t._.cancel.push(a), t._.interrupt.push(a), t._.end.push(l)), c.on = t;
    }), r === 0 && i();
  });
}
var cf = 0;
function Je(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function Bs() {
  return ++cf;
}
var Ge = fn.prototype;
Je.prototype = {
  constructor: Je,
  select: Od,
  selectAll: Bd,
  selectChild: Ge.selectChild,
  selectChildren: Ge.selectChildren,
  filter: $d,
  merge: Td,
  selection: Wd,
  transition: sf,
  call: Ge.call,
  nodes: Ge.nodes,
  node: Ge.node,
  size: Ge.size,
  empty: Ge.empty,
  each: Ge.each,
  on: Ld,
  attr: xd,
  attrTween: Nd,
  style: Ud,
  styleTween: Qd,
  text: tf,
  textTween: rf,
  remove: Vd,
  tween: dd,
  delay: Cd,
  duration: Ad,
  ease: jd,
  easeVarying: Pd,
  end: af,
  [Symbol.iterator]: Ge[Symbol.iterator]
};
function lf(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var uf = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: lf
};
function df(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function ff(e) {
  var t, n;
  e instanceof Je ? (t = e._id, e = e._name) : (t = Bs(), (n = uf).time = kr(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var s = o[i], a = s.length, l, c = 0; c < a; ++c)
      (l = s[c]) && po(l, e, t, c, s, n || df(l, t));
  return new Je(o, this._parents, e, t);
}
fn.prototype.interrupt = cd;
fn.prototype.transition = ff;
const Tn = (e) => () => e;
function hf(e, {
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
function Qe(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
Qe.prototype = {
  constructor: Qe,
  scale: function(e) {
    return e === 1 ? this : new Qe(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new Qe(this.k, this.x + this.k * e, this.y + this.k * t);
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
var go = new Qe(1, 0, 0);
Fs.prototype = Qe.prototype;
function Fs(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return go;
  return e.__zoom;
}
function qo(e) {
  e.stopImmediatePropagation();
}
function qt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function pf(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function gf() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function hi() {
  return this.__zoom || go;
}
function yf(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function mf() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function xf(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], i = e.invertY(t[0][1]) - n[0][1], s = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    s > i ? (i + s) / 2 : Math.min(0, i) || Math.max(0, s)
  );
}
function Ws() {
  var e = pf, t = gf, n = xf, o = yf, r = mf, i = [0, 1 / 0], s = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], a = 250, l = Bn, c = uo("start", "zoom", "end"), d, f, h, y = 500, g = 150, x = 0, w = 10;
  function m(b) {
    b.property("__zoom", hi).on("wheel.zoom", j, { passive: !1 }).on("mousedown.zoom", D).on("dblclick.zoom", H).filter(r).on("touchstart.zoom", A).on("touchmove.zoom", R).on("touchend.zoom touchcancel.zoom", V).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  m.transform = function(b, C, _, M) {
    var P = b.selection ? b.selection() : b;
    P.property("__zoom", hi), b !== P ? E(b, C, _, M) : P.interrupt().each(function() {
      N(this, arguments).event(M).start().zoom(null, typeof C == "function" ? C.apply(this, arguments) : C).end();
    });
  }, m.scaleBy = function(b, C, _, M) {
    m.scaleTo(b, function() {
      var P = this.__zoom.k, $ = typeof C == "function" ? C.apply(this, arguments) : C;
      return P * $;
    }, _, M);
  }, m.scaleTo = function(b, C, _, M) {
    m.transform(b, function() {
      var P = t.apply(this, arguments), $ = this.__zoom, O = _ == null ? v(P) : typeof _ == "function" ? _.apply(this, arguments) : _, B = $.invert(O), F = typeof C == "function" ? C.apply(this, arguments) : C;
      return n(p(S($, F), O, B), P, s);
    }, _, M);
  }, m.translateBy = function(b, C, _, M) {
    m.transform(b, function() {
      return n(this.__zoom.translate(
        typeof C == "function" ? C.apply(this, arguments) : C,
        typeof _ == "function" ? _.apply(this, arguments) : _
      ), t.apply(this, arguments), s);
    }, null, M);
  }, m.translateTo = function(b, C, _, M, P) {
    m.transform(b, function() {
      var $ = t.apply(this, arguments), O = this.__zoom, B = M == null ? v($) : typeof M == "function" ? M.apply(this, arguments) : M;
      return n(go.translate(B[0], B[1]).scale(O.k).translate(
        typeof C == "function" ? -C.apply(this, arguments) : -C,
        typeof _ == "function" ? -_.apply(this, arguments) : -_
      ), $, s);
    }, M, P);
  };
  function S(b, C) {
    return C = Math.max(i[0], Math.min(i[1], C)), C === b.k ? b : new Qe(C, b.x, b.y);
  }
  function p(b, C, _) {
    var M = C[0] - _[0] * b.k, P = C[1] - _[1] * b.k;
    return M === b.x && P === b.y ? b : new Qe(b.k, M, P);
  }
  function v(b) {
    return [(+b[0][0] + +b[1][0]) / 2, (+b[0][1] + +b[1][1]) / 2];
  }
  function E(b, C, _, M) {
    b.on("start.zoom", function() {
      N(this, arguments).event(M).start();
    }).on("interrupt.zoom end.zoom", function() {
      N(this, arguments).event(M).end();
    }).tween("zoom", function() {
      var P = this, $ = arguments, O = N(P, $).event(M), B = t.apply(P, $), F = _ == null ? v(B) : typeof _ == "function" ? _.apply(P, $) : _, Z = Math.max(B[1][0] - B[0][0], B[1][1] - B[0][1]), q = P.__zoom, te = typeof C == "function" ? C.apply(P, $) : C, ie = l(q.invert(F).concat(Z / q.k), te.invert(F).concat(Z / te.k));
      return function(G) {
        if (G === 1) G = te;
        else {
          var z = ie(G), Y = Z / z[2];
          G = new Qe(Y, F[0] - z[0] * Y, F[1] - z[1] * Y);
        }
        O.zoom(null, G);
      };
    });
  }
  function N(b, C, _) {
    return !_ && b.__zooming || new I(b, C);
  }
  function I(b, C) {
    this.that = b, this.args = C, this.active = 0, this.sourceEvent = null, this.extent = t.apply(b, C), this.taps = 0;
  }
  I.prototype = {
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
      var C = Re(this.that).datum();
      c.call(
        b,
        this.that,
        new hf(b, {
          sourceEvent: this.sourceEvent,
          target: m,
          transform: this.that.__zoom,
          dispatch: c
        }),
        C
      );
    }
  };
  function j(b, ...C) {
    if (!e.apply(this, arguments)) return;
    var _ = N(this, C).event(b), M = this.__zoom, P = Math.max(i[0], Math.min(i[1], M.k * Math.pow(2, o.apply(this, arguments)))), $ = Be(b);
    if (_.wheel)
      (_.mouse[0][0] !== $[0] || _.mouse[0][1] !== $[1]) && (_.mouse[1] = M.invert(_.mouse[0] = $)), clearTimeout(_.wheel);
    else {
      if (M.k === P) return;
      _.mouse = [$, M.invert($)], Yn(this), _.start();
    }
    qt(b), _.wheel = setTimeout(O, g), _.zoom("mouse", n(p(S(M, P), _.mouse[0], _.mouse[1]), _.extent, s));
    function O() {
      _.wheel = null, _.end();
    }
  }
  function D(b, ...C) {
    if (h || !e.apply(this, arguments)) return;
    var _ = b.currentTarget, M = N(this, C, !0).event(b), P = Re(b.view).on("mousemove.zoom", F, !0).on("mouseup.zoom", Z, !0), $ = Be(b, _), O = b.clientX, B = b.clientY;
    ks(b.view), qo(b), M.mouse = [$, this.__zoom.invert($)], Yn(this), M.start();
    function F(q) {
      if (qt(q), !M.moved) {
        var te = q.clientX - O, ie = q.clientY - B;
        M.moved = te * te + ie * ie > x;
      }
      M.event(q).zoom("mouse", n(p(M.that.__zoom, M.mouse[0] = Be(q, _), M.mouse[1]), M.extent, s));
    }
    function Z(q) {
      P.on("mousemove.zoom mouseup.zoom", null), Is(q.view, M.moved), qt(q), M.event(q).end();
    }
  }
  function H(b, ...C) {
    if (e.apply(this, arguments)) {
      var _ = this.__zoom, M = Be(b.changedTouches ? b.changedTouches[0] : b, this), P = _.invert(M), $ = _.k * (b.shiftKey ? 0.5 : 2), O = n(p(S(_, $), M, P), t.apply(this, C), s);
      qt(b), a > 0 ? Re(this).transition().duration(a).call(E, O, M, b) : Re(this).call(m.transform, O, M, b);
    }
  }
  function A(b, ...C) {
    if (e.apply(this, arguments)) {
      var _ = b.touches, M = _.length, P = N(this, C, b.changedTouches.length === M).event(b), $, O, B, F;
      for (qo(b), O = 0; O < M; ++O)
        B = _[O], F = Be(B, this), F = [F, this.__zoom.invert(F), B.identifier], P.touch0 ? !P.touch1 && P.touch0[2] !== F[2] && (P.touch1 = F, P.taps = 0) : (P.touch0 = F, $ = !0, P.taps = 1 + !!d);
      d && (d = clearTimeout(d)), $ && (P.taps < 2 && (f = F[0], d = setTimeout(function() {
        d = null;
      }, y)), Yn(this), P.start());
    }
  }
  function R(b, ...C) {
    if (this.__zooming) {
      var _ = N(this, C).event(b), M = b.changedTouches, P = M.length, $, O, B, F;
      for (qt(b), $ = 0; $ < P; ++$)
        O = M[$], B = Be(O, this), _.touch0 && _.touch0[2] === O.identifier ? _.touch0[0] = B : _.touch1 && _.touch1[2] === O.identifier && (_.touch1[0] = B);
      if (O = _.that.__zoom, _.touch1) {
        var Z = _.touch0[0], q = _.touch0[1], te = _.touch1[0], ie = _.touch1[1], G = (G = te[0] - Z[0]) * G + (G = te[1] - Z[1]) * G, z = (z = ie[0] - q[0]) * z + (z = ie[1] - q[1]) * z;
        O = S(O, Math.sqrt(G / z)), B = [(Z[0] + te[0]) / 2, (Z[1] + te[1]) / 2], F = [(q[0] + ie[0]) / 2, (q[1] + ie[1]) / 2];
      } else if (_.touch0) B = _.touch0[0], F = _.touch0[1];
      else return;
      _.zoom("touch", n(p(O, B, F), _.extent, s));
    }
  }
  function V(b, ...C) {
    if (this.__zooming) {
      var _ = N(this, C).event(b), M = b.changedTouches, P = M.length, $, O;
      for (qo(b), h && clearTimeout(h), h = setTimeout(function() {
        h = null;
      }, y), $ = 0; $ < P; ++$)
        O = M[$], _.touch0 && _.touch0[2] === O.identifier ? delete _.touch0 : _.touch1 && _.touch1[2] === O.identifier && delete _.touch1;
      if (_.touch1 && !_.touch0 && (_.touch0 = _.touch1, delete _.touch1), _.touch0) _.touch0[1] = this.__zoom.invert(_.touch0[0]);
      else if (_.end(), _.taps === 2 && (O = Be(O, this), Math.hypot(f[0] - O[0], f[1] - O[1]) < w)) {
        var B = Re(this).on("dblclick.zoom");
        B && B.apply(this, arguments);
      }
    }
  }
  return m.wheelDelta = function(b) {
    return arguments.length ? (o = typeof b == "function" ? b : Tn(+b), m) : o;
  }, m.filter = function(b) {
    return arguments.length ? (e = typeof b == "function" ? b : Tn(!!b), m) : e;
  }, m.touchable = function(b) {
    return arguments.length ? (r = typeof b == "function" ? b : Tn(!!b), m) : r;
  }, m.extent = function(b) {
    return arguments.length ? (t = typeof b == "function" ? b : Tn([[+b[0][0], +b[0][1]], [+b[1][0], +b[1][1]]]), m) : t;
  }, m.scaleExtent = function(b) {
    return arguments.length ? (i[0] = +b[0], i[1] = +b[1], m) : [i[0], i[1]];
  }, m.translateExtent = function(b) {
    return arguments.length ? (s[0][0] = +b[0][0], s[1][0] = +b[1][0], s[0][1] = +b[0][1], s[1][1] = +b[1][1], m) : [[s[0][0], s[0][1]], [s[1][0], s[1][1]]];
  }, m.constrain = function(b) {
    return arguments.length ? (n = b, m) : n;
  }, m.duration = function(b) {
    return arguments.length ? (a = +b, m) : a;
  }, m.interpolate = function(b) {
    return arguments.length ? (l = b, m) : l;
  }, m.on = function() {
    var b = c.on.apply(c, arguments);
    return b === c ? m : b;
  }, m.clickDistance = function(b) {
    return arguments.length ? (x = (b = +b) * b, m) : Math.sqrt(x);
  }, m.tapDistance = function(b) {
    return arguments.length ? (w = +b, m) : w;
  }, m;
}
const Le = {
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
}, rn = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], Ys = ["Enter", " ", "Escape"], Xs = {
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
var Ct;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(Ct || (Ct = {}));
var dt;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(dt || (dt = {}));
var sn;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(sn || (sn = {}));
const qs = {
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
var rt;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(rt || (rt = {}));
var no;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(no || (no = {}));
var Q;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(Q || (Q = {}));
const pi = {
  [Q.Left]: Q.Right,
  [Q.Right]: Q.Left,
  [Q.Top]: Q.Bottom,
  [Q.Bottom]: Q.Top
};
function Zs(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Us = (e) => "id" in e && "source" in e && "target" in e, wf = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), Mr = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), pn = (e, t = [0, 0]) => {
  const { width: n, height: o } = et(e), r = e.origin ?? t, i = n * r[0], s = o * r[1];
  return {
    x: e.position.x - i,
    y: e.position.y - s
  };
}, vf = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const i = typeof r == "string";
    let s = !t.nodeLookup && !i ? r : void 0;
    t.nodeLookup && (s = i ? t.nodeLookup.get(r) : Mr(r) ? r : t.nodeLookup.get(r.id));
    const a = s ? oo(s, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return yo(o, a);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return mo(n);
}, gn = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = yo(n, oo(r)), o = !0);
  }), o ? mo(n) : { x: 0, y: 0, width: 0, height: 0 };
}, jr = (e, t, [n, o, r] = [0, 0, 1], i = !1, s = !1) => {
  const a = {
    ...Rt(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, l = [];
  for (const c of e.values()) {
    const { measured: d, selectable: f = !0, hidden: h = !1 } = c;
    if (s && !f || h)
      continue;
    const y = d.width ?? c.width ?? c.initialWidth ?? null, g = d.height ?? c.height ?? c.initialHeight ?? null, x = an(a, It(c)), w = (y ?? 0) * (g ?? 0), m = i && x > 0;
    (!c.internals.handleBounds || m || x >= w || c.dragging) && l.push(c);
  }
  return l;
}, bf = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function Sf(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function Nf({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: i }, s) {
  if (e.size === 0)
    return !0;
  const a = Sf(e, s), l = gn(a), c = Pr(l, t, n, s?.minZoom ?? r, s?.maxZoom ?? i, s?.padding ?? 0.1);
  return await o.setViewport(c, {
    duration: s?.duration,
    ease: s?.ease,
    interpolate: s?.interpolate
  }), !0;
}
function Ks({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: i }) {
  const s = n.get(e), a = s.parentId ? n.get(s.parentId) : void 0, { x: l, y: c } = a ? a.internals.positionAbsolute : { x: 0, y: 0 }, d = s.origin ?? o;
  let f = s.extent || r;
  if (s.extent === "parent" && !s.expandParent)
    if (!a)
      i?.("005", Le.error005());
    else {
      const y = a.measured.width, g = a.measured.height;
      y && g && (f = [
        [l, c],
        [l + y, c + g]
      ]);
    }
  else a && gt(s.extent) && (f = [
    [s.extent[0][0] + l, s.extent[0][1] + c],
    [s.extent[1][0] + l, s.extent[1][1] + c]
  ]);
  const h = gt(f) ? pt(t, f, s.measured) : t;
  return (s.measured.width === void 0 || s.measured.height === void 0) && i?.("015", Le.error015()), {
    position: {
      x: h.x - l + (s.measured.width ?? 0) * d[0],
      y: h.y - c + (s.measured.height ?? 0) * d[1]
    },
    positionAbsolute: h
  };
}
async function Ef({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const i = new Set(e.map((h) => h.id)), s = [];
  for (const h of n) {
    if (h.deletable === !1)
      continue;
    const y = i.has(h.id), g = !y && h.parentId && s.find((x) => x.id === h.parentId);
    (y || g) && s.push(h);
  }
  const a = new Set(t.map((h) => h.id)), l = o.filter((h) => h.deletable !== !1), d = bf(s, l);
  for (const h of l)
    a.has(h.id) && !d.find((g) => g.id === h.id) && d.push(h);
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
const kt = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), pt = (e = { x: 0, y: 0 }, t, n) => ({
  x: kt(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: kt(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Gs(e, t, n) {
  const { width: o, height: r } = et(n), { x: i, y: s } = n.internals.positionAbsolute;
  return pt(e, [
    [i, s],
    [i + o, s + r]
  ], t);
}
const gi = (e, t, n) => e < t ? kt(Math.abs(e - t), 1, t) / t : e > n ? -kt(Math.abs(e - n), 1, t) / t : 0, Dr = (e, t, n = 15, o = 40) => {
  const r = gi(e.x, o, t.width - o) * n, i = gi(e.y, o, t.height - o) * n;
  return [r, i];
}, yo = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), gr = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), mo = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), It = (e, t = [0, 0]) => {
  const { x: n, y: o } = Mr(e) ? e.internals.positionAbsolute : pn(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, oo = (e, t = [0, 0]) => {
  const { x: n, y: o } = Mr(e) ? e.internals.positionAbsolute : pn(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, Qs = (e, t) => mo(yo(gr(e), gr(t))), an = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, yi = (e) => We(e.width) && We(e.height) && We(e.x) && We(e.y), We = (e) => !isNaN(e) && isFinite(e), Js = (e, t) => (n, o) => {
}, yn = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), Rt = ({ x: e, y: t }, [n, o, r], i = !1, s = [1, 1]) => {
  const a = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return i ? yn(a, s) : a;
}, At = ({ x: e, y: t }, [n, o, r]) => ({
  x: e * r + n,
  y: t * r + o
});
function wt(e, t) {
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
function _f(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = wt(e, n), r = wt(e, t);
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
    const o = wt(e.top ?? e.y ?? 0, n), r = wt(e.bottom ?? e.y ?? 0, n), i = wt(e.left ?? e.x ?? 0, t), s = wt(e.right ?? e.x ?? 0, t);
    return { top: o, right: s, bottom: r, left: i, x: i + s, y: o + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function Cf(e, t, n, o, r, i) {
  const { x: s, y: a } = At(e, [t, n, o]), { x: l, y: c } = At({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), d = r - l, f = i - c;
  return {
    left: Math.floor(s),
    top: Math.floor(a),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const Pr = (e, t, n, o, r, i) => {
  const s = _f(i, t, n), a = (t - s.x) / e.width, l = (n - s.y) / e.height, c = Math.min(a, l), d = kt(c, o, r), f = e.x + e.width / 2, h = e.y + e.height / 2, y = t / 2 - f * d, g = n / 2 - h * d, x = Cf(e, y, g, d, t, n), w = {
    left: Math.min(x.left - s.left, 0),
    top: Math.min(x.top - s.top, 0),
    right: Math.min(x.right - s.right, 0),
    bottom: Math.min(x.bottom - s.bottom, 0)
  };
  return {
    x: y - w.left + w.right,
    y: g - w.top + w.bottom,
    zoom: d
  };
}, cn = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function gt(e) {
  return e != null && e !== "parent";
}
function et(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function ea(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function ta(e, t = { width: 0, height: 0 }, n, o, r) {
  const i = { ...e }, s = o.get(n);
  if (s) {
    const a = s.origin || r;
    i.x += s.internals.positionAbsolute.x - (t.width ?? 0) * a[0], i.y += s.internals.positionAbsolute.y - (t.height ?? 0) * a[1];
  }
  return i;
}
function mi(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function kf() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function If(e) {
  return { ...Xs, ...e || {} };
}
function Jt(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: i, y: s } = Ye(e), a = Rt({ x: i - (r?.left ?? 0), y: s - (r?.top ?? 0) }, o), { x: l, y: c } = n ? yn(a, t) : a;
  return {
    xSnapped: l,
    ySnapped: c,
    ...a
  };
}
const $r = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), na = (e) => e?.getRootNode?.() || window?.document, Af = ["INPUT", "SELECT", "TEXTAREA"];
function oa(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : Af.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const ra = (e) => "clientX" in e, Ye = (e, t) => {
  const n = ra(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, xi = (e, t, n, o, r) => {
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
      ...$r(s)
    };
  });
};
function ia({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: i, targetControlX: s, targetControlY: a }) {
  const l = e * 0.125 + r * 0.375 + s * 0.375 + n * 0.125, c = t * 0.125 + i * 0.375 + a * 0.375 + o * 0.125, d = Math.abs(l - e), f = Math.abs(c - t);
  return [l, c, d, f];
}
function Rn(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function wi({ pos: e, x1: t, y1: n, x2: o, y2: r, c: i }) {
  switch (e) {
    case Q.Left:
      return [t - Rn(t - o, i), n];
    case Q.Right:
      return [t + Rn(o - t, i), n];
    case Q.Top:
      return [t, n - Rn(n - r, i)];
    case Q.Bottom:
      return [t, n + Rn(r - n, i)];
  }
}
function sa({ sourceX: e, sourceY: t, sourcePosition: n = Q.Bottom, targetX: o, targetY: r, targetPosition: i = Q.Top, curvature: s = 0.25 }) {
  const [a, l] = wi({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: s
  }), [c, d] = wi({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: s
  }), [f, h, y, g] = ia({
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
    h,
    y,
    g
  ];
}
function aa({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, i = n < e ? n + r : n - r, s = Math.abs(o - t) / 2, a = o < t ? o + s : o - s;
  return [i, a, r, s];
}
function Mf({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: i = "basic" }) {
  if (i === "manual")
    return o;
  const s = r && n ? o + 1e3 : o, a = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return s + a;
}
function jf({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const i = yo(oo(e), oo(t));
  i.x === i.x2 && (i.x2 += 1), i.y === i.y2 && (i.y2 += 1);
  const s = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return an(s, mo(i)) > 0;
}
const ca = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, Df = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), Pf = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Le.error006()), t;
  const o = n.getEdgeId || ca;
  let r;
  return Us(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, Df(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, $f = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: r, ...i } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", Le.error006()), n;
  if (!n.find((c) => c.id === e.id))
    return o.onError?.("007", Le.error007(r)), n;
  const a = o.getEdgeId || ca, l = {
    ...i,
    id: o.shouldReplaceId ? a(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((c) => c.id !== r).concat(l);
};
function la({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, i, s, a] = aa({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, i, s, a];
}
const vi = {
  [Q.Left]: { x: -1, y: 0 },
  [Q.Right]: { x: 1, y: 0 },
  [Q.Top]: { x: 0, y: -1 },
  [Q.Bottom]: { x: 0, y: 1 }
}, Tf = ({ source: e, sourcePosition: t = Q.Bottom, target: n }) => t === Q.Left || t === Q.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, bi = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Rf({ source: e, sourcePosition: t = Q.Bottom, target: n, targetPosition: o = Q.Top, center: r, offset: i, stepPosition: s }) {
  const a = vi[t], l = vi[o], c = { x: e.x + a.x * i, y: e.y + a.y * i }, d = { x: n.x + l.x * i, y: n.y + l.y * i }, f = Tf({
    source: c,
    sourcePosition: t,
    target: d
  }), h = f.x !== 0 ? "x" : "y", y = f[h];
  let g = [], x, w;
  const m = { x: 0, y: 0 }, S = { x: 0, y: 0 }, [, , p, v] = aa({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (a[h] * l[h] === -1) {
    h === "x" ? (x = r.x ?? c.x + (d.x - c.x) * s, w = r.y ?? (c.y + d.y) / 2) : (x = r.x ?? (c.x + d.x) / 2, w = r.y ?? c.y + (d.y - c.y) * s);
    const j = [
      { x, y: c.y },
      { x, y: d.y }
    ], D = [
      { x: c.x, y: w },
      { x: d.x, y: w }
    ];
    a[h] === y ? g = h === "x" ? j : D : g = h === "x" ? D : j;
  } else {
    const j = [{ x: c.x, y: d.y }], D = [{ x: d.x, y: c.y }];
    if (h === "x" ? g = a.x === y ? D : j : g = a.y === y ? j : D, t === o) {
      const b = Math.abs(e[h] - n[h]);
      if (b <= i) {
        const C = Math.min(i - 1, i - b);
        a[h] === y ? m[h] = (c[h] > e[h] ? -1 : 1) * C : S[h] = (d[h] > n[h] ? -1 : 1) * C;
      }
    }
    if (t !== o) {
      const b = h === "x" ? "y" : "x", C = a[h] === l[b], _ = c[b] > d[b], M = c[b] < d[b];
      (a[h] === 1 && (!C && _ || C && M) || a[h] !== 1 && (!C && M || C && _)) && (g = h === "x" ? j : D);
    }
    const H = { x: c.x + m.x, y: c.y + m.y }, A = { x: d.x + S.x, y: d.y + S.y }, R = Math.max(Math.abs(H.x - g[0].x), Math.abs(A.x - g[0].x)), V = Math.max(Math.abs(H.y - g[0].y), Math.abs(A.y - g[0].y));
    R >= V ? (x = (H.x + A.x) / 2, w = g[0].y) : (x = g[0].x, w = (H.y + A.y) / 2);
  }
  const E = { x: c.x + m.x, y: c.y + m.y }, N = { x: d.x + S.x, y: d.y + S.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...E.x !== g[0].x || E.y !== g[0].y ? [E] : [],
    ...g,
    ...N.x !== g[g.length - 1].x || N.y !== g[g.length - 1].y ? [N] : [],
    n
  ], x, w, p, v];
}
function zf(e, t, n, o) {
  const r = Math.min(bi(e, t) / 2, bi(t, n) / 2, o), { x: i, y: s } = t;
  if (e.x === i && i === n.x || e.y === s && s === n.y)
    return `L${i} ${s}`;
  if (e.y === s) {
    const c = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${i + r * c},${s}Q ${i},${s} ${i},${s + r * d}`;
  }
  const a = e.x < n.x ? 1 : -1, l = e.y < n.y ? -1 : 1;
  return `L ${i},${s + r * l}Q ${i},${s} ${i + r * a},${s}`;
}
function ro({ sourceX: e, sourceY: t, sourcePosition: n = Q.Bottom, targetX: o, targetY: r, targetPosition: i = Q.Top, borderRadius: s = 5, centerX: a, centerY: l, offset: c = 20, stepPosition: d = 0.5 }) {
  const [f, h, y, g, x] = Rf({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: i,
    center: { x: a, y: l },
    offset: c,
    stepPosition: d
  });
  let w = `M${f[0].x} ${f[0].y}`;
  for (let m = 1; m < f.length - 1; m++)
    w += zf(f[m - 1], f[m], f[m + 1], s);
  return w += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [w, h, y, g, x];
}
function Si(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function Lf(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!Si(t) || !Si(n))
    return null;
  const o = t.internals.handleBounds || Ni(t.handles), r = n.internals.handleBounds || Ni(n.handles), i = Ei(o?.source ?? [], e.sourceHandle), s = Ei(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === Ct.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!i || !s)
    return e.onError?.("008", Le.error008(i ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const a = i?.position || Q.Bottom, l = s?.position || Q.Top, c = yt(t, i, a), d = yt(n, s, l);
  return {
    sourceX: c.x,
    sourceY: c.y,
    targetX: d.x,
    targetY: d.y,
    sourcePosition: a,
    targetPosition: l
  };
}
function Ni(e) {
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
function yt(e, t, n = Q.Left, o = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, i = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: s, height: a } = t ?? et(e);
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
function Ei(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function yr(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function Hf(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const i = /* @__PURE__ */ new Set();
  return e.reduce((s, a) => ([a.markerStart || o, a.markerEnd || r].forEach((l) => {
    if (l && typeof l == "object") {
      const c = yr(l, t);
      i.has(c) || (s.push({ id: c, color: l.color || n, ...l }), i.add(c));
    }
  }), s), []).sort((s, a) => s.id.localeCompare(a.id));
}
const ua = 1e3, Vf = 10, Tr = {
  nodeOrigin: [0, 0],
  nodeExtent: rn,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Of = {
  ...Tr,
  checkEquality: !0
};
function Rr(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function Bf(e, t, n) {
  const o = Rr(Tr, n);
  for (const r of e.values())
    if (r.parentId)
      Lr(r, e, t, o);
    else {
      const i = pn(r, o.nodeOrigin), s = gt(r.extent) ? r.extent : o.nodeExtent, a = pt(i, s, et(r));
      r.internals.positionAbsolute = a;
    }
}
function Ff(e, t) {
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
function zr(e) {
  return e === "manual";
}
function mr(e, t, n, o = {}) {
  const r = Rr(Of, o), i = { i: 0 }, s = new Map(t), a = r?.elevateNodesOnSelect && !zr(r.zIndexMode) ? ua : 0;
  let l = e.length > 0, c = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = s.get(d.id);
    if (r.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const h = pn(d, r.nodeOrigin), y = gt(d.extent) ? d.extent : r.nodeExtent, g = pt(h, y, et(d));
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
          handleBounds: Ff(d, f),
          z: da(d, a, r.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (l = !1), d.parentId && Lr(f, t, n, o, i), c ||= d.selected ?? !1;
  }
  return { nodesInitialized: l, hasSelectedNodes: c };
}
function Wf(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function Lr(e, t, n, o, r) {
  const { elevateNodesOnSelect: i, nodeOrigin: s, nodeExtent: a, zIndexMode: l } = Rr(Tr, o), c = e.parentId, d = t.get(c);
  if (!d) {
    console.warn(`Parent node ${c} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Wf(e, n), r && !d.parentId && d.internals.rootParentIndex === void 0 && l === "auto" && (d.internals.rootParentIndex = ++r.i, d.internals.z = d.internals.z + r.i * Vf), r && d.internals.rootParentIndex !== void 0 && (r.i = d.internals.rootParentIndex);
  const f = i && !zr(l) ? ua : 0, { x: h, y, z: g } = Yf(e, d, s, a, f, l), { positionAbsolute: x } = e.internals, w = h !== x.x || y !== x.y;
  (w || g !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: w ? { x: h, y } : x,
      z: g
    }
  });
}
function da(e, t, n) {
  const o = We(e.zIndex) ? e.zIndex : 0;
  return zr(n) ? o : o + (e.selected ? t : 0);
}
function Yf(e, t, n, o, r, i) {
  const { x: s, y: a } = t.internals.positionAbsolute, l = et(e), c = pn(e, n), d = gt(e.extent) ? pt(c, e.extent, l) : c;
  let f = pt({ x: s + d.x, y: a + d.y }, o, l);
  e.extent === "parent" && (f = Gs(f, l, t));
  const h = da(e, r, i), y = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: y >= h ? y + 1 : h
  };
}
function Hr(e, t, n, o = [0, 0]) {
  const r = [], i = /* @__PURE__ */ new Map();
  for (const s of e) {
    const a = t.get(s.parentId);
    if (!a)
      continue;
    const l = i.get(s.parentId)?.expandedRect ?? It(a), c = Qs(l, s.rect);
    i.set(s.parentId, { expandedRect: c, parent: a });
  }
  return i.size > 0 && i.forEach(({ expandedRect: s, parent: a }, l) => {
    const c = a.internals.positionAbsolute, d = et(a), f = a.origin ?? o, h = s.x < c.x ? Math.round(Math.abs(c.x - s.x)) : 0, y = s.y < c.y ? Math.round(Math.abs(c.y - s.y)) : 0, g = Math.max(d.width, Math.round(s.width)), x = Math.max(d.height, Math.round(s.height)), w = (g - d.width) * f[0], m = (x - d.height) * f[1];
    (h > 0 || y > 0 || w || m) && (r.push({
      id: l,
      type: "position",
      position: {
        x: a.position.x - h + w,
        y: a.position.y - y + m
      }
    }), n.get(l)?.forEach((S) => {
      e.some((p) => p.id === S.id) || r.push({
        id: S.id,
        type: "position",
        position: {
          x: S.position.x + h,
          y: S.position.y + y
        }
      });
    })), (d.width < s.width || d.height < s.height || h || y) && r.push({
      id: l,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: g + (h ? f[0] * h - w : 0),
        height: x + (y ? f[1] * y - m : 0)
      }
    });
  }), r;
}
function Xf(e, t, n, o, r, i, s) {
  const a = o?.querySelector(".xyflow__viewport");
  let l = !1;
  if (!a)
    return { changes: [], updatedInternals: l };
  const c = [], d = window.getComputedStyle(a), { m22: f } = new window.DOMMatrixReadOnly(d.transform), h = [];
  for (const y of e.values()) {
    const g = t.get(y.id);
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
    const x = $r(y.nodeElement), w = g.measured.width !== x.width || g.measured.height !== x.height;
    if (!!(x.width && x.height && (w || !g.internals.handleBounds || y.force))) {
      const S = y.nodeElement.getBoundingClientRect(), p = gt(g.extent) ? g.extent : i;
      let { positionAbsolute: v } = g.internals;
      g.parentId && g.extent === "parent" ? v = Gs(v, x, t.get(g.parentId)) : p && (v = pt(v, p, x));
      const E = {
        ...g,
        measured: x,
        internals: {
          ...g.internals,
          positionAbsolute: v,
          handleBounds: {
            source: xi("source", y.nodeElement, S, f, g.id),
            target: xi("target", y.nodeElement, S, f, g.id)
          }
        }
      };
      t.set(g.id, E), g.parentId && Lr(E, t, n, { nodeOrigin: r, zIndexMode: s }), l = !0, w && (c.push({
        id: g.id,
        type: "dimensions",
        dimensions: x
      }), g.expandParent && g.parentId && h.push({
        id: g.id,
        parentId: g.parentId,
        rect: It(E, r)
      }));
    }
  }
  if (h.length > 0) {
    const y = Hr(h, t, n, r);
    c.push(...y);
  }
  return { changes: c, updatedInternals: l };
}
async function qf({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: i }) {
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
function _i(e, t, n, o, r, i) {
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
function fa(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: i, sourceHandle: s = null, targetHandle: a = null } = o, l = { edgeId: o.id, source: r, target: i, sourceHandle: s, targetHandle: a }, c = `${r}-${s}--${i}-${a}`, d = `${i}-${a}--${r}-${s}`;
    _i("source", l, d, e, r, s), _i("target", l, c, e, i, a), t.set(o.id, o);
  }
}
function ha(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : ha(n, t) : !1;
}
function Ci(e, t, n) {
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
function Zf(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [i, s] of e)
    if ((s.selected || s.id === o) && (!s.parentId || !ha(s, e)) && (s.draggable || t && typeof s.draggable > "u")) {
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
function Zo({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
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
function Uf({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const i = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, s = yn(i, t);
  return {
    x: s.x - i.x,
    y: s.y - i.y
  };
}
function Kf({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let i = { x: null, y: null }, s = 0, a = /* @__PURE__ */ new Map(), l = !1, c = { x: 0, y: 0 }, d = null, f = !1, h = null, y = !1, g = !1, x = null;
  function w({ noDragClassName: S, handleSelector: p, domNode: v, isSelectable: E, nodeId: N, nodeClickDistance: I = 0 }) {
    h = Re(v);
    function j({ x: R, y: V }) {
      const { nodeLookup: b, nodeExtent: C, snapGrid: _, snapToGrid: M, nodeOrigin: P, onNodeDrag: $, onSelectionDrag: O, onError: B, updateNodePositions: F } = t();
      i = { x: R, y: V };
      let Z = !1;
      const q = a.size > 1, te = q && C ? gr(gn(a)) : null, ie = q && M ? Uf({
        dragItems: a,
        snapGrid: _,
        x: R,
        y: V
      }) : null;
      for (const [G, z] of a) {
        if (!b.has(G))
          continue;
        let Y = { x: R - z.distance.x, y: V - z.distance.y };
        M && (Y = ie ? {
          x: Math.round(Y.x + ie.x),
          y: Math.round(Y.y + ie.y)
        } : yn(Y, _));
        let re = null;
        if (q && C && !z.extent && te) {
          const { positionAbsolute: J } = z.internals, se = J.x - te.x + C[0][0], L = J.x + z.measured.width - te.x2 + C[1][0], K = J.y - te.y + C[0][1], he = J.y + z.measured.height - te.y2 + C[1][1];
          re = [
            [se, K],
            [L, he]
          ];
        }
        const { position: oe, positionAbsolute: U } = Ks({
          nodeId: G,
          nextPosition: Y,
          nodeLookup: b,
          nodeExtent: re || C,
          nodeOrigin: P,
          onError: B
        });
        Z = Z || z.position.x !== oe.x || z.position.y !== oe.y, z.position = oe, z.internals.positionAbsolute = U;
      }
      if (g = g || Z, !!Z && (F(a, !0), x && (o || $ || !N && O))) {
        const [G, z] = Zo({
          nodeId: N,
          dragItems: a,
          nodeLookup: b
        });
        o?.(x, a, G, z), $?.(x, G, z), N || O?.(x, z);
      }
    }
    async function D() {
      if (!d)
        return;
      const { transform: R, panBy: V, autoPanSpeed: b, autoPanOnNodeDrag: C } = t();
      if (!C) {
        l = !1, cancelAnimationFrame(s);
        return;
      }
      const [_, M] = Dr(c, d, b);
      (_ !== 0 || M !== 0) && (i.x = (i.x ?? 0) - _ / R[2], i.y = (i.y ?? 0) - M / R[2], await V({ x: _, y: M }) && j(i)), s = requestAnimationFrame(D);
    }
    function H(R) {
      const { nodeLookup: V, multiSelectionActive: b, nodesDraggable: C, transform: _, snapGrid: M, snapToGrid: P, selectNodesOnDrag: $, onNodeDragStart: O, onSelectionDragStart: B, unselectNodesAndEdges: F } = t();
      f = !0, (!$ || !E) && !b && N && (V.get(N)?.selected || F()), E && $ && N && e?.(N);
      const Z = Jt(R.sourceEvent, { transform: _, snapGrid: M, snapToGrid: P, containerBounds: d });
      if (i = Z, a = Zf(V, C, Z, N), a.size > 0 && (n || O || !N && B)) {
        const [q, te] = Zo({
          nodeId: N,
          dragItems: a,
          nodeLookup: V
        });
        n?.(R.sourceEvent, a, q, te), O?.(R.sourceEvent, q, te), N || B?.(R.sourceEvent, te);
      }
    }
    const A = As().clickDistance(I).on("start", (R) => {
      const { domNode: V, nodeDragThreshold: b, transform: C, snapGrid: _, snapToGrid: M } = t();
      d = V?.getBoundingClientRect() || null, y = !1, g = !1, x = R.sourceEvent, b === 0 && H(R), i = Jt(R.sourceEvent, { transform: C, snapGrid: _, snapToGrid: M, containerBounds: d }), c = Ye(R.sourceEvent, d);
    }).on("drag", (R) => {
      const { autoPanOnNodeDrag: V, transform: b, snapGrid: C, snapToGrid: _, nodeDragThreshold: M, nodeLookup: P } = t(), $ = Jt(R.sourceEvent, { transform: b, snapGrid: C, snapToGrid: _, containerBounds: d });
      if (x = R.sourceEvent, (R.sourceEvent.type === "touchmove" && R.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      N && !P.has(N)) && (y = !0), !y) {
        if (!l && V && f && (l = !0, D()), !f) {
          const O = Ye(R.sourceEvent, d), B = O.x - c.x, F = O.y - c.y;
          Math.sqrt(B * B + F * F) > M && H(R);
        }
        (i.x !== $.xSnapped || i.y !== $.ySnapped) && a && f && (c = Ye(R.sourceEvent, d), j($));
      }
    }).on("end", (R) => {
      if (!f || y) {
        y && a.size > 0 && t().updateNodePositions(a, !1);
        return;
      }
      if (l = !1, f = !1, cancelAnimationFrame(s), a.size > 0) {
        const { nodeLookup: V, updateNodePositions: b, onNodeDragStop: C, onSelectionDragStop: _ } = t();
        if (g && (b(a, !1), g = !1), r || C || !N && _) {
          const [M, P] = Zo({
            nodeId: N,
            dragItems: a,
            nodeLookup: V,
            dragging: !1
          });
          r?.(R.sourceEvent, a, M, P), C?.(R.sourceEvent, M, P), N || _?.(R.sourceEvent, P);
        }
      }
    }).filter((R) => {
      const V = R.target;
      return !R.button && (!S || !Ci(V, `.${S}`, v)) && (!p || Ci(V, p, v));
    });
    h.call(A);
  }
  function m() {
    h?.on(".drag", null);
  }
  return {
    update: w,
    destroy: m
  };
}
function Gf(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const i of t.values())
    an(r, It(i)) > 0 && o.push(i);
  return o;
}
const Qf = 250;
function Jf(e, t, n, o) {
  let r = [], i = 1 / 0;
  const s = Gf(e, n, t + Qf);
  for (const a of s) {
    const l = [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []];
    for (const c of l) {
      if (o.nodeId === c.nodeId && o.type === c.type && o.id === c.id)
        continue;
      const { x: d, y: f } = yt(a, c, c.position, !0), h = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(f - e.y, 2));
      h > t || (h < i ? (r = [{ ...c, x: d, y: f }], i = h) : h === i && r.push({ ...c, x: d, y: f }));
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
function pa(e, t, n, o, r, i = !1) {
  const s = o.get(e);
  if (!s)
    return null;
  const a = r === "strict" ? s.internals.handleBounds?.[t] : [...s.internals.handleBounds?.source ?? [], ...s.internals.handleBounds?.target ?? []], l = (n ? a?.find((c) => c.id === n) : a?.[0]) ?? null;
  return l && i ? { ...l, ...yt(s, l, l.position, !0) } : l;
}
function ga(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function eh(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const ya = () => !0;
function th(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: i, isTarget: s, domNode: a, nodeLookup: l, lib: c, autoPanOnConnect: d, flowId: f, panBy: h, cancelConnection: y, onConnectStart: g, onConnect: x, onConnectEnd: w, isValidConnection: m = ya, onReconnectEnd: S, updateConnection: p, getTransform: v, getFromHandle: E, autoPanSpeed: N, dragThreshold: I = 1, handleDomNode: j }) {
  const D = na(e.target);
  let H = 0, A;
  const { x: R, y: V } = Ye(e), b = ga(i, j), C = a?.getBoundingClientRect();
  let _ = !1;
  if (!C || !b)
    return;
  const M = pa(r, b, o, l, t);
  if (!M)
    return;
  let P = Ye(e, C), $ = !1, O = null, B = !1, F = null;
  function Z() {
    if (!d || !C)
      return;
    const [oe, U] = Dr(P, C, N);
    h({ x: oe, y: U }), H = requestAnimationFrame(Z);
  }
  const q = {
    ...M,
    nodeId: r,
    type: b,
    position: M.position
  }, te = l.get(r);
  let G = {
    inProgress: !0,
    isValid: null,
    from: yt(te, q, Q.Left, !0),
    fromHandle: q,
    fromPosition: q.position,
    fromNode: te,
    to: P,
    toHandle: null,
    toPosition: pi[q.position],
    toNode: null,
    pointer: P
  };
  function z() {
    _ = !0, p(G), g?.(e, { nodeId: r, handleId: o, handleType: b });
  }
  I === 0 && z();
  function Y(oe) {
    if (!_) {
      const { x: he, y: we } = Ye(oe), be = he - R, De = we - V;
      if (!(be * be + De * De > I * I))
        return;
      z();
    }
    if (!E() || !q) {
      re(oe);
      return;
    }
    const U = v();
    P = Ye(oe, C), A = Jf(Rt(P, U, !1, [1, 1]), n, l, q), $ || (Z(), $ = !0);
    const J = ma(oe, {
      handle: A,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: s ? "target" : "source",
      isValidConnection: m,
      doc: D,
      lib: c,
      flowId: f,
      nodeLookup: l
    });
    F = J.handleDomNode, O = J.connection, B = eh(!!A, J.isValid);
    const se = l.get(r), L = se ? yt(se, q, Q.Left, !0) : G.from, K = {
      ...G,
      from: L,
      isValid: B,
      to: J.toHandle && B ? At({ x: J.toHandle.x, y: J.toHandle.y }, U) : P,
      toHandle: J.toHandle,
      toPosition: B && J.toHandle ? J.toHandle.position : pi[q.position],
      toNode: J.toHandle ? l.get(J.toHandle.nodeId) : null,
      pointer: P
    };
    p(K), G = K;
  }
  function re(oe) {
    if (!("touches" in oe && oe.touches.length > 0)) {
      if (_) {
        (A || F) && O && B && x?.(O);
        const { inProgress: U, ...J } = G, se = {
          ...J,
          toPosition: G.toHandle ? G.toPosition : null
        };
        w?.(oe, se), i && S?.(oe, se);
      }
      y(), cancelAnimationFrame(H), $ = !1, B = !1, O = null, F = null, D.removeEventListener("mousemove", Y), D.removeEventListener("mouseup", re), D.removeEventListener("touchmove", Y), D.removeEventListener("touchend", re);
    }
  }
  D.addEventListener("mousemove", Y), D.addEventListener("mouseup", re), D.addEventListener("touchmove", Y), D.addEventListener("touchend", re);
}
function ma(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: i, doc: s, lib: a, flowId: l, isValidConnection: c = ya, nodeLookup: d }) {
  const f = i === "target", h = t ? s.querySelector(`.${a}-flow__handle[data-id="${l}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: y, y: g } = Ye(e), x = s.elementFromPoint(y, g), w = x?.classList.contains(`${a}-flow__handle`) ? x : h, m = {
    handleDomNode: w,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (w) {
    const S = ga(void 0, w), p = w.getAttribute("data-nodeid"), v = w.getAttribute("data-handleid"), E = w.classList.contains("connectable"), N = w.classList.contains("connectableend");
    if (!p || !S)
      return m;
    const I = {
      source: f ? p : o,
      sourceHandle: f ? v : r,
      target: f ? o : p,
      targetHandle: f ? r : v
    };
    m.connection = I;
    const D = E && N && (n === Ct.Strict ? f && S === "source" || !f && S === "target" : p !== o || v !== r);
    m.isValid = D && c(I), m.toHandle = pa(p, S, v, d, n, !0);
  }
  return m;
}
const xr = {
  onPointerDown: th,
  isValid: ma
};
function nh({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = Re(e);
  function i({ translateExtent: a, width: l, height: c, zoomStep: d = 1, pannable: f = !0, zoomable: h = !0, inversePan: y = !1 }) {
    const g = (p) => {
      if (p.sourceEvent.type !== "wheel" || !t)
        return;
      const v = n(), E = p.sourceEvent.ctrlKey && cn() ? 10 : 1, N = -p.sourceEvent.deltaY * (p.sourceEvent.deltaMode === 1 ? 0.05 : p.sourceEvent.deltaMode ? 1 : 2e-3) * d, I = v[2] * Math.pow(2, N * E);
      t.scaleTo(I);
    };
    let x = [0, 0];
    const w = (p) => {
      (p.sourceEvent.type === "mousedown" || p.sourceEvent.type === "touchstart") && (x = [
        p.sourceEvent.clientX ?? p.sourceEvent.touches[0].clientX,
        p.sourceEvent.clientY ?? p.sourceEvent.touches[0].clientY
      ]);
    }, m = (p) => {
      const v = n();
      if (p.sourceEvent.type !== "mousemove" && p.sourceEvent.type !== "touchmove" || !t)
        return;
      const E = [
        p.sourceEvent.clientX ?? p.sourceEvent.touches[0].clientX,
        p.sourceEvent.clientY ?? p.sourceEvent.touches[0].clientY
      ], N = [E[0] - x[0], E[1] - x[1]];
      x = E;
      const I = o() * Math.max(v[2], Math.log(v[2])) * (y ? -1 : 1), j = {
        x: v[0] - N[0] * I,
        y: v[1] - N[1] * I
      }, D = [
        [0, 0],
        [l, c]
      ];
      t.setViewportConstrained({
        x: j.x,
        y: j.y,
        zoom: v[2]
      }, D, a);
    }, S = Ws().on("start", w).on("zoom", f ? m : null).on("zoom.wheel", h ? g : null);
    r.call(S, {});
  }
  function s() {
    r.on("zoom", null);
  }
  return {
    update: i,
    destroy: s,
    pointer: Be
  };
}
const xo = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), Uo = ({ x: e, y: t, zoom: n }) => go.translate(e, t).scale(n), vt = (e, t) => e.target.closest(`.${t}`), xa = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), oh = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, Ko = (e, t = 0, n = oh, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, wa = (e) => {
  const t = e.ctrlKey && cn() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function rh({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: i, zoomOnPinch: s, onPanZoomStart: a, onPanZoom: l, onPanZoomEnd: c }) {
  return (d) => {
    if (vt(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && s) {
      const w = Be(d), m = wa(d), S = f * Math.pow(2, m);
      o.scaleTo(n, S, w, d);
      return;
    }
    const h = d.deltaMode === 1 ? 20 : 1;
    let y = r === dt.Vertical ? 0 : d.deltaX * h, g = r === dt.Horizontal ? 0 : d.deltaY * h;
    !cn() && d.shiftKey && r !== dt.Vertical && (y = d.deltaY * h, g = 0), o.translateBy(
      n,
      -(y / f) * i,
      -(g / f) * i,
      // @ts-ignore
      { internal: !0 }
    );
    const x = xo(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (l?.(d, x), e.panScrollTimeout = setTimeout(() => {
      c?.(d, x), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, a?.(d, x));
  };
}
function ih({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const i = o.type === "wheel", s = !t && i && !o.ctrlKey, a = vt(o, e);
    if (o.ctrlKey && i && a && o.preventDefault(), s || a)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function sh({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = xo(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function ah({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (i) => {
    e.usedRightMouseButton = !!(n && xa(t, e.mouseButton ?? 0)), i.sourceEvent?.sync || o([i.transform.x, i.transform.y, i.transform.k]), r && !i.sourceEvent?.internal && r?.(i.sourceEvent, xo(i.transform));
  };
}
function ch({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: i }) {
  return (s) => {
    if (!s.sourceEvent?.internal && (e.isZoomingOrPanning = !1, i && xa(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && s.sourceEvent && i(s.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
      const a = xo(s.transform);
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
function lh({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: i, userSelectionActive: s, noWheelClassName: a, noPanClassName: l, lib: c, connectionInProgress: d }) {
  return (f) => {
    const h = e || t, y = n && f.ctrlKey, g = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (vt(f, `${c}-flow__node`) || vt(f, `${c}-flow__edge`)))
      return !0;
    if (!o && !h && !r && !i && !n || s || d && !g || vt(f, a) && g || vt(f, l) && (!g || r && g && !e) || !n && f.ctrlKey && g)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!h && !r && !y && g || !o && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(o) && !o.includes(f.button) && f.type === "mousedown")
      return !1;
    const x = Array.isArray(o) && o.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || g) && x;
  };
}
function uh({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: i, onPanZoomStart: s, onPanZoomEnd: a, onDraggingChange: l }) {
  const c = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = Ws().scaleExtent([t, n]).translateExtent(o), h = Re(e).call(f);
  S({
    x: r.x,
    y: r.y,
    zoom: kt(r.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], o);
  const y = h.on("wheel.zoom"), g = h.on("dblclick.zoom");
  f.wheelDelta(wa);
  async function x(A, R) {
    return h ? new Promise((V) => {
      f?.interpolate(R?.interpolate === "linear" ? Qt : Bn).transform(Ko(h, R?.duration, R?.ease, () => V(!0)), A);
    }) : !1;
  }
  function w({ noWheelClassName: A, noPanClassName: R, onPaneContextMenu: V, userSelectionActive: b, panOnScroll: C, panOnDrag: _, panOnScrollMode: M, panOnScrollSpeed: P, preventScrolling: $, zoomOnPinch: O, zoomOnScroll: B, zoomOnDoubleClick: F, zoomActivationKeyPressed: Z, lib: q, onTransformChange: te, connectionInProgress: ie, paneClickDistance: G, selectionOnDrag: z }) {
    b && !c.isZoomingOrPanning && m();
    const Y = C && !Z && !b;
    f.clickDistance(z ? 1 / 0 : !We(G) || G < 0 ? 0 : G);
    const re = Y ? rh({
      zoomPanValues: c,
      noWheelClassName: A,
      d3Selection: h,
      d3Zoom: f,
      panOnScrollMode: M,
      panOnScrollSpeed: P,
      zoomOnPinch: O,
      onPanZoomStart: s,
      onPanZoom: i,
      onPanZoomEnd: a
    }) : ih({
      noWheelClassName: A,
      preventScrolling: $,
      d3ZoomHandler: y
    });
    h.on("wheel.zoom", re, { passive: !1 });
    const oe = sh({
      zoomPanValues: c,
      onDraggingChange: l,
      onPanZoomStart: s
    });
    f.on("start", oe);
    const U = ah({
      zoomPanValues: c,
      panOnDrag: _,
      onPaneContextMenu: !!V,
      onPanZoom: i,
      onTransformChange: te
    });
    f.on("zoom", U);
    const J = ch({
      zoomPanValues: c,
      panOnDrag: _,
      panOnScroll: C,
      onPaneContextMenu: V,
      onPanZoomEnd: a,
      onDraggingChange: l
    });
    f.on("end", J);
    const se = lh({
      zoomActivationKeyPressed: Z,
      panOnDrag: _,
      zoomOnScroll: B,
      panOnScroll: C,
      zoomOnDoubleClick: F,
      zoomOnPinch: O,
      userSelectionActive: b,
      noPanClassName: R,
      noWheelClassName: A,
      lib: q,
      connectionInProgress: ie
    });
    f.filter(se), F ? h.on("dblclick.zoom", g) : h.on("dblclick.zoom", null);
  }
  function m() {
    f.on("zoom", null);
  }
  async function S(A, R, V) {
    const b = Uo(A), C = f?.constrain()(b, R, V);
    return C && await x(C), C;
  }
  async function p(A, R) {
    const V = Uo(A);
    return await x(V, R), V;
  }
  function v(A) {
    if (h) {
      const R = Uo(A), V = h.property("__zoom");
      (V.k !== A.zoom || V.x !== A.x || V.y !== A.y) && f?.transform(h, R, null, { sync: !0 });
    }
  }
  function E() {
    const A = h ? Fs(h.node()) : { x: 0, y: 0, k: 1 };
    return { x: A.x, y: A.y, zoom: A.k };
  }
  async function N(A, R) {
    return h ? new Promise((V) => {
      f?.interpolate(R?.interpolate === "linear" ? Qt : Bn).scaleTo(Ko(h, R?.duration, R?.ease, () => V(!0)), A);
    }) : !1;
  }
  async function I(A, R) {
    return h ? new Promise((V) => {
      f?.interpolate(R?.interpolate === "linear" ? Qt : Bn).scaleBy(Ko(h, R?.duration, R?.ease, () => V(!0)), A);
    }) : !1;
  }
  function j(A) {
    f?.scaleExtent(A);
  }
  function D(A) {
    f?.translateExtent(A);
  }
  function H(A) {
    const R = !We(A) || A < 0 ? 0 : A;
    f?.clickDistance(R);
  }
  return {
    update: w,
    destroy: m,
    setViewport: p,
    setViewportConstrained: S,
    getViewport: E,
    scaleTo: N,
    scaleBy: I,
    setScaleExtent: j,
    setTranslateExtent: D,
    syncViewport: v,
    setClickDistance: H
  };
}
var Mt;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(Mt || (Mt = {}));
function dh({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: i }) {
  const s = e - t, a = n - o, l = [s > 0 ? 1 : s < 0 ? -1 : 0, a > 0 ? 1 : a < 0 ? -1 : 0];
  return s && r && (l[0] = l[0] * -1), a && i && (l[1] = l[1] * -1), l;
}
function ki(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: r
  };
}
function nt(e, t) {
  return Math.max(0, t - e);
}
function ot(e, t) {
  return Math.max(0, e - t);
}
function zn(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function Ii(e, t) {
  return e ? !t : t;
}
function fh(e, t, n, o, r, i, s, a) {
  let { affectsX: l, affectsY: c } = t;
  const { isHorizontal: d, isVertical: f } = t, h = d && f, { xSnapped: y, ySnapped: g } = n, { minWidth: x, maxWidth: w, minHeight: m, maxHeight: S } = o, { x: p, y: v, width: E, height: N, aspectRatio: I } = e;
  let j = Math.floor(d ? y - e.pointerX : 0), D = Math.floor(f ? g - e.pointerY : 0);
  const H = E + (l ? -j : j), A = N + (c ? -D : D), R = -i[0] * E, V = -i[1] * N;
  let b = zn(H, x, w), C = zn(A, m, S);
  if (s) {
    let P = 0, $ = 0;
    l && j < 0 ? P = nt(p + j + R, s[0][0]) : !l && j > 0 && (P = ot(p + H + R, s[1][0])), c && D < 0 ? $ = nt(v + D + V, s[0][1]) : !c && D > 0 && ($ = ot(v + A + V, s[1][1])), b = Math.max(b, P), C = Math.max(C, $);
  }
  if (a) {
    let P = 0, $ = 0;
    l && j > 0 ? P = ot(p + j, a[0][0]) : !l && j < 0 && (P = nt(p + H, a[1][0])), c && D > 0 ? $ = ot(v + D, a[0][1]) : !c && D < 0 && ($ = nt(v + A, a[1][1])), b = Math.max(b, P), C = Math.max(C, $);
  }
  if (r) {
    if (d) {
      const P = zn(H / I, m, S) * I;
      if (b = Math.max(b, P), s) {
        let $ = 0;
        !l && !c || l && !c && h ? $ = ot(v + V + H / I, s[1][1]) * I : $ = nt(v + V + (l ? j : -j) / I, s[0][1]) * I, b = Math.max(b, $);
      }
      if (a) {
        let $ = 0;
        !l && !c || l && !c && h ? $ = nt(v + H / I, a[1][1]) * I : $ = ot(v + (l ? j : -j) / I, a[0][1]) * I, b = Math.max(b, $);
      }
    }
    if (f) {
      const P = zn(A * I, x, w) / I;
      if (C = Math.max(C, P), s) {
        let $ = 0;
        !l && !c || c && !l && h ? $ = ot(p + A * I + R, s[1][0]) / I : $ = nt(p + (c ? D : -D) * I + R, s[0][0]) / I, C = Math.max(C, $);
      }
      if (a) {
        let $ = 0;
        !l && !c || c && !l && h ? $ = nt(p + A * I, a[1][0]) / I : $ = ot(p + (c ? D : -D) * I, a[0][0]) / I, C = Math.max(C, $);
      }
    }
  }
  D = D + (D < 0 ? C : -C), j = j + (j < 0 ? b : -b), r && (h ? H > A * I ? D = (Ii(l, c) ? -j : j) / I : j = (Ii(l, c) ? -D : D) * I : d ? (D = j / I, c = l) : (j = D * I, l = c));
  const _ = l ? p + j : p, M = c ? v + D : v;
  return {
    width: E + (l ? -j : j),
    height: N + (c ? -D : D),
    x: i[0] * j * (l ? -1 : 1) + _,
    y: i[1] * D * (c ? -1 : 1) + M
  };
}
const va = { width: 0, height: 0, x: 0, y: 0 }, hh = {
  ...va,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function ph(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, i = e.measured.width ?? 0, s = e.measured.height ?? 0, a = n[0] * i, l = n[1] * s;
  return [
    [o - a, r - l],
    [o + i - a, r + s - l]
  ];
}
function gh({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const i = Re(e);
  let s = {
    controlDirection: ki("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function a({ controlPosition: c, boundaries: d, keepAspectRatio: f, resizeDirection: h, onResizeStart: y, onResize: g, onResizeEnd: x, shouldResize: w }) {
    let m = { ...va }, S = { ...hh };
    s = {
      boundaries: d,
      resizeDirection: h,
      keepAspectRatio: f,
      controlDirection: ki(c)
    };
    let p, v = null, E = [], N, I, j, D = !1;
    const H = As().on("start", (A) => {
      const { nodeLookup: R, transform: V, snapGrid: b, snapToGrid: C, nodeOrigin: _, paneDomNode: M } = n();
      if (p = R.get(t), !p)
        return;
      v = M?.getBoundingClientRect() ?? null;
      const { xSnapped: P, ySnapped: $ } = Jt(A.sourceEvent, {
        transform: V,
        snapGrid: b,
        snapToGrid: C,
        containerBounds: v
      });
      m = {
        width: p.measured.width ?? 0,
        height: p.measured.height ?? 0,
        x: p.position.x ?? 0,
        y: p.position.y ?? 0
      }, S = {
        ...m,
        pointerX: P,
        pointerY: $,
        aspectRatio: m.width / m.height
      }, N = void 0, I = gt(p.extent) ? p.extent : void 0, p.parentId && (p.extent === "parent" || p.expandParent) && (N = R.get(p.parentId)), N && p.extent === "parent" && (I = [
        [0, 0],
        [N.measured.width, N.measured.height]
      ]), E = [], j = void 0;
      for (const [O, B] of R)
        if (B.parentId === t && (E.push({
          id: O,
          position: { ...B.position },
          extent: B.extent
        }), B.extent === "parent" || B.expandParent)) {
          const F = ph(B, p, B.origin ?? _);
          j ? j = [
            [Math.min(F[0][0], j[0][0]), Math.min(F[0][1], j[0][1])],
            [Math.max(F[1][0], j[1][0]), Math.max(F[1][1], j[1][1])]
          ] : j = F;
        }
      y?.(A, { ...m });
    }).on("drag", (A) => {
      const { transform: R, snapGrid: V, snapToGrid: b, nodeOrigin: C } = n(), _ = Jt(A.sourceEvent, {
        transform: R,
        snapGrid: V,
        snapToGrid: b,
        containerBounds: v
      }), M = [];
      if (!p)
        return;
      const { x: P, y: $, width: O, height: B } = m, F = {}, Z = p.origin ?? C, { width: q, height: te, x: ie, y: G } = fh(S, s.controlDirection, _, s.boundaries, s.keepAspectRatio, Z, I, j), z = q !== O, Y = te !== B, re = ie !== P && z, oe = G !== $ && Y;
      if (!re && !oe && !z && !Y)
        return;
      if ((re || oe || Z[0] === 1 || Z[1] === 1) && (F.x = re ? ie : m.x, F.y = oe ? G : m.y, m.x = F.x, m.y = F.y, E.length > 0)) {
        const L = ie - P, K = G - $;
        for (const he of E)
          he.position = {
            x: he.position.x - L + Z[0] * (q - O),
            y: he.position.y - K + Z[1] * (te - B)
          }, M.push(he);
      }
      if ((z || Y) && (F.width = z && (!s.resizeDirection || s.resizeDirection === "horizontal") ? q : m.width, F.height = Y && (!s.resizeDirection || s.resizeDirection === "vertical") ? te : m.height, m.width = F.width, m.height = F.height), N && p.expandParent) {
        const L = Z[0] * (F.width ?? 0);
        F.x && F.x < L && (m.x = L, S.x = S.x - (F.x - L));
        const K = Z[1] * (F.height ?? 0);
        F.y && F.y < K && (m.y = K, S.y = S.y - (F.y - K));
      }
      const U = dh({
        width: m.width,
        prevWidth: O,
        height: m.height,
        prevHeight: B,
        affectsX: s.controlDirection.affectsX,
        affectsY: s.controlDirection.affectsY
      }), J = { ...m, direction: U };
      w?.(A, J) !== !1 && (D = !0, g?.(A, J), o(F, M));
    }).on("end", (A) => {
      D && (x?.(A, { ...m }), r?.({ ...m }), D = !1);
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
var Go = { exports: {} }, Qo = {}, Jo = { exports: {} }, er = {};
var Ai;
function yh() {
  if (Ai) return er;
  Ai = 1;
  var e = st;
  function t(f, h) {
    return f === h && (f !== 0 || 1 / f === 1 / h) || f !== f && h !== h;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, i = e.useLayoutEffect, s = e.useDebugValue;
  function a(f, h) {
    var y = h(), g = o({ inst: { value: y, getSnapshot: h } }), x = g[0].inst, w = g[1];
    return i(
      function() {
        x.value = y, x.getSnapshot = h, l(x) && w({ inst: x });
      },
      [f, y, h]
    ), r(
      function() {
        return l(x) && w({ inst: x }), f(function() {
          l(x) && w({ inst: x });
        });
      },
      [f]
    ), s(y), y;
  }
  function l(f) {
    var h = f.getSnapshot;
    f = f.value;
    try {
      var y = h();
      return !n(f, y);
    } catch {
      return !0;
    }
  }
  function c(f, h) {
    return h();
  }
  var d = typeof window > "u" || typeof window.document > "u" || typeof window.document.createElement > "u" ? c : a;
  return er.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : d, er;
}
var Mi;
function mh() {
  return Mi || (Mi = 1, Jo.exports = yh()), Jo.exports;
}
var ji;
function xh() {
  if (ji) return Qo;
  ji = 1;
  var e = st, t = mh();
  function n(c, d) {
    return c === d && (c !== 0 || 1 / c === 1 / d) || c !== c && d !== d;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, i = e.useRef, s = e.useEffect, a = e.useMemo, l = e.useDebugValue;
  return Qo.useSyncExternalStoreWithSelector = function(c, d, f, h, y) {
    var g = i(null);
    if (g.current === null) {
      var x = { hasValue: !1, value: null };
      g.current = x;
    } else x = g.current;
    g = a(
      function() {
        function m(N) {
          if (!S) {
            if (S = !0, p = N, N = h(N), y !== void 0 && x.hasValue) {
              var I = x.value;
              if (y(I, N))
                return v = I;
            }
            return v = N;
          }
          if (I = v, o(p, N)) return I;
          var j = h(N);
          return y !== void 0 && y(I, j) ? (p = N, I) : (p = N, v = j);
        }
        var S = !1, p, v, E = f === void 0 ? null : f;
        return [
          function() {
            return m(d());
          },
          E === null ? void 0 : function() {
            return m(E());
          }
        ];
      },
      [d, f, h, y]
    );
    var w = r(c, g[0], g[1]);
    return s(
      function() {
        x.hasValue = !0, x.value = w;
      },
      [w]
    ), l(w), w;
  }, Qo;
}
var Di;
function wh() {
  return Di || (Di = 1, Go.exports = xh()), Go.exports;
}
var vh = wh();
const bh = /* @__PURE__ */ Rc(vh), Sh = {}, Pi = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (d, f) => {
    const h = typeof d == "function" ? d(t) : d;
    if (!Object.is(h, t)) {
      const y = t;
      t = f ?? (typeof h != "object" || h === null) ? h : Object.assign({}, t, h), n.forEach((g) => g(t, y));
    }
  }, r = () => t, l = { setState: o, getState: r, getInitialState: () => c, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (Sh ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, c = t = e(o, r, l);
  return l;
}, Nh = (e) => e ? Pi(e) : Pi, { useDebugValue: Eh } = st, { useSyncExternalStoreWithSelector: _h } = bh, Ch = (e) => e;
function ba(e, t = Ch, n) {
  const o = _h(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return Eh(o), o;
}
const $i = (e, t) => {
  const n = Nh(e), o = (r, i = t) => ba(n, r, i);
  return Object.assign(o, n), o;
}, kh = (e, t) => e ? $i(e, t) : $i;
function me(e, t) {
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
var tr = { exports: {} }, ke = {};
var Ti;
function Ih() {
  if (Ti) return ke;
  Ti = 1;
  var e = st;
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
  return ke.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, ke.createPortal = function(l, c) {
    var d = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!c || c.nodeType !== 1 && c.nodeType !== 9 && c.nodeType !== 11)
      throw Error(t(299));
    return i(l, c, null, d);
  }, ke.flushSync = function(l) {
    var c = s.T, d = o.p;
    try {
      if (s.T = null, o.p = 2, l) return l();
    } finally {
      s.T = c, o.p = d, o.d.f();
    }
  }, ke.preconnect = function(l, c) {
    typeof l == "string" && (c ? (c = c.crossOrigin, c = typeof c == "string" ? c === "use-credentials" ? c : "" : void 0) : c = null, o.d.C(l, c));
  }, ke.prefetchDNS = function(l) {
    typeof l == "string" && o.d.D(l);
  }, ke.preinit = function(l, c) {
    if (typeof l == "string" && c && typeof c.as == "string") {
      var d = c.as, f = a(d, c.crossOrigin), h = typeof c.integrity == "string" ? c.integrity : void 0, y = typeof c.fetchPriority == "string" ? c.fetchPriority : void 0;
      d === "style" ? o.d.S(
        l,
        typeof c.precedence == "string" ? c.precedence : void 0,
        {
          crossOrigin: f,
          integrity: h,
          fetchPriority: y
        }
      ) : d === "script" && o.d.X(l, {
        crossOrigin: f,
        integrity: h,
        fetchPriority: y,
        nonce: typeof c.nonce == "string" ? c.nonce : void 0
      });
    }
  }, ke.preinitModule = function(l, c) {
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
  }, ke.preload = function(l, c) {
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
  }, ke.preloadModule = function(l, c) {
    if (typeof l == "string")
      if (c) {
        var d = a(c.as, c.crossOrigin);
        o.d.m(l, {
          as: typeof c.as == "string" && c.as !== "script" ? c.as : void 0,
          crossOrigin: d,
          integrity: typeof c.integrity == "string" ? c.integrity : void 0
        });
      } else o.d.m(l);
  }, ke.requestFormReset = function(l) {
    o.d.r(l);
  }, ke.unstable_batchedUpdates = function(l, c) {
    return l(c);
  }, ke.useFormState = function(l, c, d) {
    return s.H.useFormState(l, c, d);
  }, ke.useFormStatus = function() {
    return s.H.useHostTransitionStatus();
  }, ke.version = "19.2.7", ke;
}
var Ri;
function Ah() {
  if (Ri) return tr.exports;
  Ri = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), tr.exports = Ih(), tr.exports;
}
var Mh = Ah();
const wo = Sr(null), jh = wo.Provider, Sa = Le.error001("react");
function ue(e, t) {
  const n = dn(wo);
  if (n === null)
    throw new Error(Sa);
  return ba(n, e, t);
}
function xe() {
  const e = dn(wo);
  if (e === null)
    throw new Error(Sa);
  return ye(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const zi = { display: "none" }, Dh = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, Na = "react-flow__node-desc", Ea = "react-flow__edge-desc", Ph = "react-flow__aria-live", $h = (e) => e.ariaLiveMessage, Th = (e) => e.ariaLabelConfig;
function Rh({ rfId: e }) {
  const t = ue($h);
  return u.jsx("div", { id: `${Ph}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: Dh, children: t });
}
function zh({ rfId: e, disableKeyboardA11y: t }) {
  const n = ue(Th);
  return u.jsxs(u.Fragment, { children: [u.jsx("div", { id: `${Na}-${e}`, style: zi, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), u.jsx("div", { id: `${Ea}-${e}`, style: zi, children: n["edge.a11yDescription.default"] }), !t && u.jsx(Rh, { rfId: e })] });
}
const vo = lo(({ position: e = "top-left", children: t, className: n, style: o, ...r }, i) => {
  const s = `${e}`.split("-");
  return u.jsx("div", { className: Ee(["react-flow__panel", n, ...s]), style: o, ref: i, ...r, children: t });
});
vo.displayName = "Panel";
function Lh({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : u.jsx(vo, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: u.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const Hh = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, Ln = (e) => e.id;
function Vh(e, t) {
  return me(e.selectedNodes.map(Ln), t.selectedNodes.map(Ln)) && me(e.selectedEdges.map(Ln), t.selectedEdges.map(Ln));
}
function Oh({ onSelectionChange: e }) {
  const t = xe(), { selectedNodes: n, selectedEdges: o } = ue(Hh, Vh);
  return ce(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((i) => i(r));
  }, [n, o, e]), null;
}
const Bh = (e) => !!e.onSelectionChangeHandlers;
function Fh({ onSelectionChange: e }) {
  const t = ue(Bh);
  return e || t ? u.jsx(Oh, { onSelectionChange: e }) : null;
}
const _a = [0, 0], Wh = { x: 0, y: 0, zoom: 1 }, Yh = [
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
], Li = [...Yh, "rfId"], Xh = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), Hi = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: rn,
  nodeOrigin: _a,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function qh(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: i, setNodeExtent: s, reset: a, setDefaultNodesAndEdges: l } = ue(Xh, me), c = xe();
  ce(() => (l(e.defaultNodes, e.defaultEdges), () => {
    d.current = Hi, a();
  }), []);
  const d = ae(Hi);
  return ce(
    () => {
      for (const f of Li) {
        const h = e[f], y = d.current[f];
        h !== y && (typeof e[f] > "u" || (f === "nodes" ? t(h) : f === "edges" ? n(h) : f === "minZoom" ? o(h) : f === "maxZoom" ? r(h) : f === "translateExtent" ? i(h) : f === "nodeExtent" ? s(h) : f === "ariaLabelConfig" ? c.setState({ ariaLabelConfig: If(h) }) : f === "fitView" ? c.setState({ fitViewQueued: h }) : f === "fitViewOptions" ? c.setState({ fitViewOptions: h }) : c.setState({ [f]: h })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    Li.map((f) => e[f])
  ), null;
}
function Vi() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function Zh(e) {
  const [t, n] = ee(e === "system" ? null : e);
  return ce(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = Vi(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : Vi()?.matches ? "dark" : "light";
}
const Oi = typeof document < "u" ? document : null;
function ln(e = null, t = { target: Oi, actInsideInputWithModifier: !0 }) {
  const [n, o] = ee(!1), r = ae(!1), i = ae(/* @__PURE__ */ new Set([])), [s, a] = ye(() => {
    if (e !== null) {
      const c = (Array.isArray(e) ? e : [e]).filter((f) => typeof f == "string").map((f) => f.replace("+", `
`).replace(`

`, `
+`).split(`
`)), d = c.reduce((f, h) => f.concat(...h), []);
      return [c, d];
    }
    return [[], []];
  }, [e]);
  return ce(() => {
    const l = t?.target ?? Oi, c = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (y) => {
        if (r.current = y.ctrlKey || y.metaKey || y.shiftKey || y.altKey, (!r.current || r.current && !c) && oa(y))
          return !1;
        const x = Fi(y.code, a);
        if (i.current.add(y[x]), Bi(s, i.current, !1)) {
          const w = y.composedPath?.()?.[0] || y.target, m = w?.nodeName === "BUTTON" || w?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !m) && y.preventDefault(), o(!0);
        }
      }, f = (y) => {
        const g = Fi(y.code, a);
        Bi(s, i.current, !0) ? (o(!1), i.current.clear()) : i.current.delete(y[g]), y.key === "Meta" && i.current.clear(), r.current = !1;
      }, h = () => {
        i.current.clear(), o(!1);
      };
      return l?.addEventListener("keydown", d), l?.addEventListener("keyup", f), window.addEventListener("blur", h), window.addEventListener("contextmenu", h), () => {
        l?.removeEventListener("keydown", d), l?.removeEventListener("keyup", f), window.removeEventListener("blur", h), window.removeEventListener("contextmenu", h);
      };
    }
  }, [e, o]), n;
}
function Bi(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function Fi(e, t) {
  return t.includes(e) ? "code" : "key";
}
const Uh = () => {
  const e = xe();
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
      const { width: o, height: r, minZoom: i, maxZoom: s, panZoom: a } = e.getState(), l = Pr(t, o, r, i, s, n?.padding ?? 0.1);
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
      return Rt(c, o, f, d);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: r, y: i } = o.getBoundingClientRect(), s = At(t, n);
      return {
        x: s.x + r,
        y: s.y + i
      };
    }
  }), []);
};
function Ca(e, t) {
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
      Kh(l, a);
    n.push(a);
  }
  return r.length && r.forEach((i) => {
    i.index !== void 0 ? n.splice(i.index, 0, { ...i.item }) : n.push({ ...i.item });
  }), n;
}
function Kh(e, t) {
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
function ka(e, t) {
  return Ca(e, t);
}
function Ia(e, t) {
  return Ca(e, t);
}
function ct(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function bt(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [r, i] of e) {
    const s = t.has(r);
    !(i.selected === void 0 && !s) && i.selected !== s && (n && (i.selected = s), o.push(ct(i.id, s)));
  }
  return o;
}
function Wi({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((r) => [r.id, r]));
  for (const [r, i] of e.entries()) {
    const s = t.get(i.id), a = s?.internals?.userNode ?? s;
    a !== void 0 && a !== i && n.push({ id: i.id, item: i, type: "replace" }), a === void 0 && n.push({ item: i, type: "add", index: r });
  }
  for (const [r] of t)
    o.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function Yi(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const Aa = Js();
function Ma(e, t, n = {}) {
  return Pf(e, t, {
    ...n,
    onError: n.onError ?? Aa
  });
}
function Gh(e, t, n, o = { shouldReplaceId: !0 }) {
  return $f(e, t, n, {
    ...o,
    onError: o.onError ?? Aa
  });
}
const Xi = (e) => wf(e), Qh = (e) => Us(e);
function ja(e) {
  return lo(e);
}
const Jh = typeof window < "u" ? $c : ce;
function qi(e) {
  const [t, n] = ee(BigInt(0)), [o] = ee(() => ep(() => n((r) => r + BigInt(1))));
  return Jh(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function ep(e) {
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
const Da = Sr(null);
function tp({ children: e }) {
  const t = xe(), n = ge((a) => {
    const { nodes: l = [], setNodes: c, hasDefaultNodes: d, onNodesChange: f, nodeLookup: h, fitViewQueued: y, onNodesChangeMiddlewareMap: g } = t.getState();
    let x = l;
    for (const m of a)
      x = typeof m == "function" ? m(x) : m;
    let w = Wi({
      items: x,
      lookup: h
    });
    for (const m of g.values())
      w = m(w);
    d && c(x), w.length > 0 ? f?.(w) : y && window.requestAnimationFrame(() => {
      const { fitViewQueued: m, nodes: S, setNodes: p } = t.getState();
      m && p(S);
    });
  }, []), o = qi(n), r = ge((a) => {
    const { edges: l = [], setEdges: c, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: h } = t.getState();
    let y = l;
    for (const g of a)
      y = typeof g == "function" ? g(y) : g;
    d ? c(y) : f && f(Wi({
      items: y,
      lookup: h
    }));
  }, []), i = qi(r), s = ye(() => ({ nodeQueue: o, edgeQueue: i }), []);
  return u.jsx(Da.Provider, { value: s, children: e });
}
function np() {
  const e = dn(Da);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const op = (e) => !!e.panZoom;
function Vr() {
  const e = Uh(), t = xe(), n = np(), o = ue(op), r = ye(() => {
    const i = (f) => t.getState().nodeLookup.get(f), s = (f) => {
      n.nodeQueue.push(f);
    }, a = (f) => {
      n.edgeQueue.push(f);
    }, l = (f) => {
      const { nodeLookup: h, nodeOrigin: y } = t.getState(), g = Xi(f) ? f : h.get(f.id), x = g.parentId ? ta(g.position, g.measured, g.parentId, h, y) : g.position, w = {
        ...g,
        position: x,
        width: g.measured?.width ?? g.width,
        height: g.measured?.height ?? g.height
      };
      return It(w);
    }, c = (f, h, y = { replace: !1 }) => {
      s((g) => g.map((x) => {
        if (x.id === f) {
          const w = typeof h == "function" ? h(x) : h;
          return y.replace && Xi(w) ? w : { ...x, ...w };
        }
        return x;
      }));
    }, d = (f, h, y = { replace: !1 }) => {
      a((g) => g.map((x) => {
        if (x.id === f) {
          const w = typeof h == "function" ? h(x) : h;
          return y.replace && Qh(w) ? w : { ...x, ...w };
        }
        return x;
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
      setNodes: s,
      setEdges: a,
      addNodes: (f) => {
        const h = Array.isArray(f) ? f : [f];
        n.nodeQueue.push((y) => [...y, ...h]);
      },
      addEdges: (f) => {
        const h = Array.isArray(f) ? f : [f];
        n.edgeQueue.push((y) => [...y, ...h]);
      },
      toObject: () => {
        const { nodes: f = [], edges: h = [], transform: y } = t.getState(), [g, x, w] = y;
        return {
          nodes: f.map((m) => ({ ...m })),
          edges: h.map((m) => ({ ...m })),
          viewport: {
            x: g,
            y: x,
            zoom: w
          }
        };
      },
      deleteElements: async ({ nodes: f = [], edges: h = [] }) => {
        const { nodes: y, edges: g, onNodesDelete: x, onEdgesDelete: w, triggerNodeChanges: m, triggerEdgeChanges: S, onDelete: p, onBeforeDelete: v } = t.getState(), { nodes: E, edges: N } = await Ef({
          nodesToRemove: f,
          edgesToRemove: h,
          nodes: y,
          edges: g,
          onBeforeDelete: v
        }), I = N.length > 0, j = E.length > 0;
        if (I) {
          const D = N.map(Yi);
          w?.(N), S(D);
        }
        if (j) {
          const D = E.map(Yi);
          x?.(E), m(D);
        }
        return (j || I) && p?.({ nodes: E, edges: N }), { deletedNodes: E, deletedEdges: N };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, h = !0, y) => {
        const g = yi(f), x = g ? f : l(f), w = y !== void 0;
        return x ? (y || t.getState().nodes).filter((m) => {
          const S = t.getState().nodeLookup.get(m.id);
          if (S && !g && (m.id === f.id || !S.internals.positionAbsolute))
            return !1;
          const p = It(w ? m : S), v = an(p, x);
          return h && v > 0 || v >= p.width * p.height || v >= x.width * x.height;
        }) : [];
      },
      isNodeIntersecting: (f, h, y = !0) => {
        const x = yi(f) ? f : l(f);
        if (!x)
          return !1;
        const w = an(x, h);
        return y && w > 0 || w >= h.width * h.height || w >= x.width * x.height;
      },
      updateNode: c,
      updateNodeData: (f, h, y = { replace: !1 }) => {
        c(f, (g) => {
          const x = typeof h == "function" ? h(g) : h;
          return y.replace ? { ...g, data: x } : { ...g, data: { ...g.data, ...x } };
        }, y);
      },
      updateEdge: d,
      updateEdgeData: (f, h, y = { replace: !1 }) => {
        d(f, (g) => {
          const x = typeof h == "function" ? h(g) : h;
          return y.replace ? { ...g, data: x } : { ...g, data: { ...g.data, ...x } };
        }, y);
      },
      getNodesBounds: (f) => {
        const { nodeLookup: h, nodeOrigin: y } = t.getState();
        return vf(f, { nodeLookup: h, nodeOrigin: y });
      },
      getHandleConnections: ({ type: f, id: h, nodeId: y }) => Array.from(t.getState().connectionLookup.get(`${y}-${f}${h ? `-${h}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: h, nodeId: y }) => Array.from(t.getState().connectionLookup.get(`${y}${f ? h ? `-${f}-${h}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const h = t.getState().fitViewResolver ?? kf();
        return t.setState({ fitViewQueued: !0, fitViewOptions: f, fitViewResolver: h }), n.nodeQueue.push((y) => [...y]), h.promise;
      }
    };
  }, []);
  return ye(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const Zi = (e) => e.selected, rp = typeof window < "u" ? window : void 0;
function ip({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = xe(), { deleteElements: o } = Vr(), r = ln(e, { actInsideInputWithModifier: !1 }), i = ln(t, { target: rp });
  ce(() => {
    if (r) {
      const { edges: s, nodes: a } = n.getState();
      o({ nodes: a.filter(Zi), edges: s.filter(Zi) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), ce(() => {
    n.setState({ multiSelectionActive: i });
  }, [i]);
}
function sp(e) {
  const t = xe();
  ce(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = $r(e.current);
      (o.height === 0 || o.width === 0) && t.getState().onError?.("004", Le.error004()), t.setState({ width: o.width || 500, height: o.height || 500 });
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
const bo = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, ap = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function cp({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: i = dt.Free, zoomOnDoubleClick: s = !0, panOnDrag: a = !0, defaultViewport: l, translateExtent: c, minZoom: d, maxZoom: f, zoomActivationKeyCode: h, preventScrolling: y = !0, children: g, noWheelClassName: x, noPanClassName: w, onViewportChange: m, isControlledViewport: S, paneClickDistance: p, selectionOnDrag: v }) {
  const E = xe(), N = ae(null), { userSelectionActive: I, lib: j, connectionInProgress: D } = ue(ap, me), H = ln(h), A = ae();
  sp(N);
  const R = ge((V) => {
    m?.({ x: V[0], y: V[1], zoom: V[2] }), S || E.setState({ transform: V });
  }, [m, S]);
  return ce(() => {
    if (N.current) {
      A.current = uh({
        domNode: N.current,
        minZoom: d,
        maxZoom: f,
        translateExtent: c,
        viewport: l,
        onDraggingChange: (_) => E.setState((M) => M.paneDragging === _ ? M : { paneDragging: _ }),
        onPanZoomStart: (_, M) => {
          const { onViewportChangeStart: P, onMoveStart: $ } = E.getState();
          $?.(_, M), P?.(M);
        },
        onPanZoom: (_, M) => {
          const { onViewportChange: P, onMove: $ } = E.getState();
          $?.(_, M), P?.(M);
        },
        onPanZoomEnd: (_, M) => {
          const { onViewportChangeEnd: P, onMoveEnd: $ } = E.getState();
          $?.(_, M), P?.(M);
        }
      });
      const { x: V, y: b, zoom: C } = A.current.getViewport();
      return E.setState({
        panZoom: A.current,
        transform: [V, b, C],
        domNode: N.current.closest(".react-flow")
      }), () => {
        A.current?.destroy();
      };
    }
  }, []), ce(() => {
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
      preventScrolling: y,
      noPanClassName: w,
      userSelectionActive: I,
      noWheelClassName: x,
      lib: j,
      onTransformChange: R,
      connectionInProgress: D,
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
    H,
    y,
    w,
    I,
    x,
    j,
    R,
    D,
    v,
    p
  ]), u.jsx("div", { className: "react-flow__renderer", ref: N, style: bo, children: g });
}
const lp = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function up() {
  const { userSelectionActive: e, userSelectionRect: t } = ue(lp, me);
  return e && t ? u.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const nr = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, dp = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function fp({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = sn.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: i, selectionOnDrag: s, onSelectionStart: a, onSelectionEnd: l, onPaneClick: c, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: h, onPaneMouseMove: y, onPaneMouseLeave: g, children: x }) {
  const w = ae(0), m = xe(), { userSelectionActive: S, elementsSelectable: p, dragging: v, connectionInProgress: E, panBy: N, autoPanSpeed: I } = ue(dp, me), j = p && (e || S), D = ae(null), H = ae(), A = ae(/* @__PURE__ */ new Set()), R = ae(/* @__PURE__ */ new Set()), V = ae(!1), b = ae({ x: 0, y: 0 }), C = ae(!1), _ = (z) => {
    if (V.current || E) {
      V.current = !1;
      return;
    }
    c?.(z), m.getState().resetSelectedElements(), m.setState({ nodesSelectionActive: !1 });
  }, M = (z) => {
    if (Array.isArray(o) && o?.includes(2)) {
      z.preventDefault();
      return;
    }
    d?.(z);
  }, P = f ? (z) => f(z) : void 0, $ = (z) => {
    V.current && (z.stopPropagation(), V.current = !1);
  }, O = (z) => {
    const { domNode: Y, transform: re } = m.getState();
    if (H.current = Y?.getBoundingClientRect(), !H.current)
      return;
    const oe = z.target === D.current;
    if (!oe && !!z.target.closest(".nokey") || !e || !(s && oe || t) || z.button !== 0 || !z.isPrimary)
      return;
    z.target?.setPointerCapture?.(z.pointerId), V.current = !1;
    const { x: se, y: L } = Ye(z.nativeEvent, H.current), K = Rt({ x: se, y: L }, re);
    m.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: K.x,
        startY: K.y,
        x: se,
        y: L
      }
    }), oe || (z.stopPropagation(), z.preventDefault());
  };
  function B(z, Y) {
    const { userSelectionRect: re } = m.getState();
    if (!re)
      return;
    const { transform: oe, nodeLookup: U, edgeLookup: J, connectionLookup: se, triggerNodeChanges: L, triggerEdgeChanges: K, defaultEdgeOptions: he } = m.getState(), we = { x: re.startX, y: re.startY }, { x: be, y: De } = At(we, oe), fe = {
      startX: we.x,
      startY: we.y,
      x: z < be ? z : be,
      y: Y < De ? Y : De,
      width: Math.abs(z - be),
      height: Math.abs(Y - De)
    }, Ie = A.current, Oe = R.current;
    A.current = new Set(jr(U, fe, oe, n === sn.Partial, !0).map((Pe) => Pe.id)), R.current = /* @__PURE__ */ new Set();
    const ve = he?.selectable ?? !0;
    for (const Pe of A.current) {
      const Ve = se.get(Pe);
      if (Ve)
        for (const { edgeId: pe } of Ve.values()) {
          const $e = J.get(pe);
          $e && ($e.selectable ?? ve) && R.current.add(pe);
        }
    }
    if (!mi(Ie, A.current)) {
      const Pe = bt(U, A.current, !0);
      L(Pe);
    }
    if (!mi(Oe, R.current)) {
      const Pe = bt(J, R.current);
      K(Pe);
    }
    m.setState({
      userSelectionRect: fe,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function F() {
    if (!r || !H.current)
      return;
    const [z, Y] = Dr(b.current, H.current, I);
    N({ x: z, y: Y }).then((re) => {
      if (!V.current || !re) {
        w.current = requestAnimationFrame(F);
        return;
      }
      const { x: oe, y: U } = b.current;
      B(oe, U), w.current = requestAnimationFrame(F);
    });
  }
  const Z = () => {
    cancelAnimationFrame(w.current), w.current = 0, C.current = !1;
  };
  ce(() => () => Z(), []);
  const q = (z) => {
    const { userSelectionRect: Y, transform: re, resetSelectedElements: oe } = m.getState();
    if (!H.current || !Y)
      return;
    const { x: U, y: J } = Ye(z.nativeEvent, H.current);
    b.current = { x: U, y: J };
    const se = At({ x: Y.startX, y: Y.startY }, re);
    if (!V.current) {
      const L = t ? 0 : i;
      if (Math.hypot(U - se.x, J - se.y) <= L)
        return;
      oe(), a?.(z);
    }
    V.current = !0, C.current || (F(), C.current = !0), B(U, J);
  }, te = (z) => {
    z.button === 0 && (z.target?.releasePointerCapture?.(z.pointerId), !S && z.target === D.current && m.getState().userSelectionRect && _?.(z), m.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), V.current && (l?.(z), m.setState({
      nodesSelectionActive: A.current.size > 0
    })), Z());
  }, ie = (z) => {
    z.target?.releasePointerCapture?.(z.pointerId), Z();
  }, G = o === !0 || Array.isArray(o) && o.includes(0);
  return u.jsxs("div", { className: Ee(["react-flow__pane", { draggable: G, dragging: v, selection: e }]), onClick: j ? void 0 : nr(_, D), onContextMenu: nr(M, D), onWheel: nr(P, D), onPointerEnter: j ? void 0 : h, onPointerMove: j ? q : y, onPointerUp: j ? te : void 0, onPointerCancel: j ? ie : void 0, onPointerDownCapture: j ? O : void 0, onClickCapture: j ? $ : void 0, onPointerLeave: g, ref: D, style: bo, children: [x, u.jsx(up, {})] });
}
function wr({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: i, multiSelectionActive: s, nodeLookup: a, onError: l } = t.getState(), c = a.get(e);
  if (!c) {
    l?.("012", Le.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), c.selected ? (n || c.selected && s) && (i({ nodes: [c], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function Pa({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: i, nodeClickDistance: s }) {
  const a = xe(), [l, c] = ee(!1), d = ae();
  return ce(() => {
    d.current = Kf({
      getStoreItems: () => a.getState(),
      onNodeMouseDown: (f) => {
        wr({
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
const hp = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function $a() {
  const e = xe();
  return ge((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: i, nodesDraggable: s, onError: a, updateNodePositions: l, nodeLookup: c, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), h = hp(s), y = r ? i[0] : 5, g = r ? i[1] : 5, x = n.direction.x * y * n.factor, w = n.direction.y * g * n.factor;
    for (const [, m] of c) {
      if (!h(m))
        continue;
      let S = {
        x: m.internals.positionAbsolute.x + x,
        y: m.internals.positionAbsolute.y + w
      };
      r && (S = yn(S, i));
      const { position: p, positionAbsolute: v } = Ks({
        nodeId: m.id,
        nextPosition: S,
        nodeLookup: c,
        nodeExtent: o,
        nodeOrigin: d,
        onError: a
      });
      m.position = p, m.internals.positionAbsolute = v, f.set(m.id, m);
    }
    l(f);
  }, []);
}
const Or = Sr(null), pp = Or.Provider;
Or.Consumer;
const Ta = () => dn(Or), gp = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), yp = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: i, connection: s } = o, { fromHandle: a, toHandle: l, isValid: c } = s, d = l?.nodeId === e && l?.id === t && l?.type === n;
  return {
    connectingFrom: a?.nodeId === e && a?.id === t && a?.type === n,
    connectingTo: d,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: i === Ct.Strict ? a?.type !== n : e !== a?.nodeId || t !== a?.id,
    connectionInProcess: !!a,
    clickConnectionInProcess: !!r,
    valid: d && c
  };
};
function mp({ type: e = "source", position: t = Q.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: i = !0, id: s, onConnect: a, children: l, className: c, onMouseDown: d, onTouchStart: f, ...h }, y) {
  const g = s || null, x = e === "target", w = xe(), m = Ta(), { connectOnClick: S, noPanClassName: p, rfId: v } = ue(gp, me), { connectingFrom: E, connectingTo: N, clickConnecting: I, isPossibleEndHandle: j, connectionInProcess: D, clickConnectionInProcess: H, valid: A } = ue(yp(m, g, e), me);
  m || w.getState().onError?.("010", Le.error010());
  const R = (C) => {
    const { defaultEdgeOptions: _, onConnect: M, hasDefaultEdges: P } = w.getState(), $ = {
      ..._,
      ...C
    };
    if (P) {
      const { edges: O, setEdges: B, onError: F } = w.getState();
      B(Ma($, O, { onError: F }));
    }
    M?.($), a?.($);
  }, V = (C) => {
    if (!m)
      return;
    const _ = ra(C.nativeEvent);
    if (r && (_ && C.button === 0 || !_)) {
      const M = w.getState();
      xr.onPointerDown(C.nativeEvent, {
        handleDomNode: C.currentTarget,
        autoPanOnConnect: M.autoPanOnConnect,
        connectionMode: M.connectionMode,
        connectionRadius: M.connectionRadius,
        domNode: M.domNode,
        nodeLookup: M.nodeLookup,
        lib: M.lib,
        isTarget: x,
        handleId: g,
        nodeId: m,
        flowId: M.rfId,
        panBy: M.panBy,
        cancelConnection: M.cancelConnection,
        onConnectStart: M.onConnectStart,
        onConnectEnd: (...P) => w.getState().onConnectEnd?.(...P),
        updateConnection: M.updateConnection,
        onConnect: R,
        isValidConnection: n || ((...P) => w.getState().isValidConnection?.(...P) ?? !0),
        getTransform: () => w.getState().transform,
        getFromHandle: () => w.getState().connection.fromHandle,
        autoPanSpeed: M.autoPanSpeed,
        dragThreshold: M.connectionDragThreshold
      });
    }
    _ ? d?.(C) : f?.(C);
  }, b = (C) => {
    const { onClickConnectStart: _, onClickConnectEnd: M, connectionClickStartHandle: P, connectionMode: $, isValidConnection: O, lib: B, rfId: F, nodeLookup: Z, connection: q } = w.getState();
    if (!m || !P && !r)
      return;
    if (!P) {
      _?.(C.nativeEvent, { nodeId: m, handleId: g, handleType: e }), w.setState({ connectionClickStartHandle: { nodeId: m, type: e, id: g } });
      return;
    }
    const te = na(C.target), ie = n || O, { connection: G, isValid: z } = xr.isValid(C.nativeEvent, {
      handle: {
        nodeId: m,
        id: g,
        type: e
      },
      connectionMode: $,
      fromNodeId: P.nodeId,
      fromHandleId: P.id || null,
      fromType: P.type,
      isValidConnection: ie,
      flowId: F,
      doc: te,
      lib: B,
      nodeLookup: Z
    });
    z && G && R(G);
    const Y = structuredClone(q);
    delete Y.inProgress, Y.toPosition = Y.toHandle ? Y.toHandle.position : null, M?.(C, Y), w.setState({ connectionClickStartHandle: null });
  };
  return u.jsx("div", { "data-handleid": g, "data-nodeid": m, "data-handlepos": t, "data-id": `${v}-${m}-${g}-${e}`, className: Ee([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    p,
    c,
    {
      source: !x,
      target: x,
      connectable: o,
      connectablestart: r,
      connectableend: i,
      clickconnecting: I,
      connectingfrom: E,
      connectingto: N,
      valid: A,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!D || j) && (D || H ? i : r)
    }
  ]), onMouseDown: V, onTouchStart: V, onClick: S ? b : void 0, ref: y, ...h, children: l });
}
const jt = Se(ja(mp));
function xp({ data: e, isConnectable: t, sourcePosition: n = Q.Bottom }) {
  return u.jsxs(u.Fragment, { children: [e?.label, u.jsx(jt, { type: "source", position: n, isConnectable: t })] });
}
function wp({ data: e, isConnectable: t, targetPosition: n = Q.Top, sourcePosition: o = Q.Bottom }) {
  return u.jsxs(u.Fragment, { children: [u.jsx(jt, { type: "target", position: n, isConnectable: t }), e?.label, u.jsx(jt, { type: "source", position: o, isConnectable: t })] });
}
function vp() {
  return null;
}
function bp({ data: e, isConnectable: t, targetPosition: n = Q.Top }) {
  return u.jsxs(u.Fragment, { children: [u.jsx(jt, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const io = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, Ui = {
  input: xp,
  default: wp,
  output: bp,
  group: vp
};
function Sp(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const Np = (e) => {
  const { width: t, height: n, x: o, y: r } = gn(e.nodeLookup, {
    filter: (i) => !!i.selected
  });
  return {
    width: We(t) ? t : null,
    height: We(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function Ep({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = xe(), { width: r, height: i, transformString: s, userSelectionActive: a } = ue(Np, me), l = $a(), c = ae(null);
  ce(() => {
    n || c.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !a && r !== null && i !== null;
  if (Pa({
    nodeRef: c,
    disabled: !d
  }), !d)
    return null;
  const f = e ? (y) => {
    const g = o.getState().nodes.filter((x) => x.selected);
    e(y, g);
  } : void 0, h = (y) => {
    Object.prototype.hasOwnProperty.call(io, y.key) && (y.preventDefault(), l({
      direction: io[y.key],
      factor: y.shiftKey ? 4 : 1
    }));
  };
  return u.jsx("div", { className: Ee(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: s
  }, children: u.jsx("div", { ref: c, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : h, style: {
    width: r,
    height: i
  } }) });
}
const Ki = typeof window < "u" ? window : void 0, _p = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function Ra({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, paneClickDistance: a, deleteKeyCode: l, selectionKeyCode: c, selectionOnDrag: d, selectionMode: f, onSelectionStart: h, onSelectionEnd: y, multiSelectionKeyCode: g, panActivationKeyCode: x, zoomActivationKeyCode: w, elementsSelectable: m, zoomOnScroll: S, zoomOnPinch: p, panOnScroll: v, panOnScrollSpeed: E, panOnScrollMode: N, zoomOnDoubleClick: I, panOnDrag: j, autoPanOnSelection: D, defaultViewport: H, translateExtent: A, minZoom: R, maxZoom: V, preventScrolling: b, onSelectionContextMenu: C, noWheelClassName: _, noPanClassName: M, disableKeyboardA11y: P, onViewportChange: $, isControlledViewport: O }) {
  const { nodesSelectionActive: B, userSelectionActive: F } = ue(_p, me), Z = ln(c, { target: Ki }), q = ln(x, { target: Ki }), te = q || j, ie = q || v, G = d && te !== !0, z = Z || F || G;
  return ip({ deleteKeyCode: l, multiSelectionKeyCode: g }), u.jsx(cp, { onPaneContextMenu: i, elementsSelectable: m, zoomOnScroll: S, zoomOnPinch: p, panOnScroll: ie, panOnScrollSpeed: E, panOnScrollMode: N, zoomOnDoubleClick: I, panOnDrag: !Z && te, defaultViewport: H, translateExtent: A, minZoom: R, maxZoom: V, zoomActivationKeyCode: w, preventScrolling: b, noWheelClassName: _, noPanClassName: M, onViewportChange: $, isControlledViewport: O, paneClickDistance: a, selectionOnDrag: G, children: u.jsxs(fp, { onSelectionStart: h, onSelectionEnd: y, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: s, panOnDrag: te, autoPanOnSelection: D, isSelecting: !!z, selectionMode: f, selectionKeyPressed: Z, paneClickDistance: a, selectionOnDrag: G, children: [e, B && u.jsx(Ep, { onSelectionContextMenu: C, noPanClassName: M, disableKeyboardA11y: P })] }) });
}
Ra.displayName = "FlowRenderer";
const Cp = Se(Ra), kp = (e) => (t) => e ? jr(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function Ip(e) {
  return ue(ge(kp(e), [e]), me);
}
const Ap = (e) => e.updateNodeInternals;
function Mp() {
  const e = ue(Ap), [t] = ee(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
function jp({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = xe(), i = ae(null), s = ae(null), a = ae(e.sourcePosition), l = ae(e.targetPosition), c = ae(t), d = n && !!e.internals.handleBounds;
  return ce(() => {
    i.current && !e.hidden && (!d || s.current !== i.current) && (s.current && o?.unobserve(s.current), o?.observe(i.current), s.current = i.current);
  }, [d, e.hidden]), ce(() => () => {
    s.current && (o?.unobserve(s.current), s.current = null);
  }, []), ce(() => {
    if (i.current) {
      const f = c.current !== t, h = a.current !== e.sourcePosition, y = l.current !== e.targetPosition;
      (f || h || y) && (c.current = t, a.current = e.sourcePosition, l.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: i.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), i;
}
function Dp({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: i, onDoubleClick: s, nodesDraggable: a, elementsSelectable: l, nodesConnectable: c, nodesFocusable: d, resizeObserver: f, noDragClassName: h, noPanClassName: y, disableKeyboardA11y: g, rfId: x, nodeTypes: w, nodeClickDistance: m, onError: S }) {
  const { node: p, internals: v, isParent: E } = ue((z) => {
    const Y = z.nodeLookup.get(e), re = z.parentLookup.has(e);
    return {
      node: Y,
      internals: Y.internals,
      isParent: re
    };
  }, me);
  let N = p.type || "default", I = w?.[N] || Ui[N];
  I === void 0 && (S?.("003", Le.error003(N)), N = "default", I = w?.default || Ui.default);
  const j = !!(p.draggable || a && typeof p.draggable > "u"), D = !!(p.selectable || l && typeof p.selectable > "u"), H = !!(p.connectable || c && typeof p.connectable > "u"), A = !!(p.focusable || d && typeof p.focusable > "u"), R = xe(), V = ea(p), b = jp({ node: p, nodeType: N, hasDimensions: V, resizeObserver: f }), C = Pa({
    nodeRef: b,
    disabled: p.hidden || !j,
    noDragClassName: h,
    handleSelector: p.dragHandle,
    nodeId: e,
    isSelectable: D,
    nodeClickDistance: m
  }), _ = $a();
  if (p.hidden)
    return null;
  const M = et(p), P = Sp(p), $ = D || j || t || n || o || r, O = n ? (z) => n(z, { ...v.userNode }) : void 0, B = o ? (z) => o(z, { ...v.userNode }) : void 0, F = r ? (z) => r(z, { ...v.userNode }) : void 0, Z = i ? (z) => i(z, { ...v.userNode }) : void 0, q = s ? (z) => s(z, { ...v.userNode }) : void 0, te = (z) => {
    const { selectNodesOnDrag: Y, nodeDragThreshold: re } = R.getState();
    D && (!Y || !j || re > 0) && wr({
      id: e,
      store: R,
      nodeRef: b
    }), t && t(z, { ...v.userNode });
  }, ie = (z) => {
    if (!(oa(z.nativeEvent) || g)) {
      if (Ys.includes(z.key) && D) {
        const Y = z.key === "Escape";
        wr({
          id: e,
          store: R,
          unselect: Y,
          nodeRef: b
        });
      } else if (j && p.selected && Object.prototype.hasOwnProperty.call(io, z.key)) {
        z.preventDefault();
        const { ariaLabelConfig: Y } = R.getState();
        R.setState({
          ariaLiveMessage: Y["node.a11yDescription.ariaLiveMessage"]({
            direction: z.key.replace("Arrow", "").toLowerCase(),
            x: ~~v.positionAbsolute.x,
            y: ~~v.positionAbsolute.y
          })
        }), _({
          direction: io[z.key],
          factor: z.shiftKey ? 4 : 1
        });
      }
    }
  }, G = () => {
    if (g || !b.current?.matches(":focus-visible"))
      return;
    const { transform: z, width: Y, height: re, autoPanOnNodeFocus: oe, setCenter: U } = R.getState();
    if (!oe)
      return;
    jr(/* @__PURE__ */ new Map([[e, p]]), { x: 0, y: 0, width: Y, height: re }, z, !0).length > 0 || U(p.position.x + M.width / 2, p.position.y + M.height / 2, {
      zoom: z[2]
    });
  };
  return u.jsx("div", { className: Ee([
    "react-flow__node",
    `react-flow__node-${N}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [y]: j
    },
    p.className,
    {
      selected: p.selected,
      selectable: D,
      parent: E,
      draggable: j,
      dragging: C
    }
  ]), ref: b, style: {
    zIndex: v.z,
    transform: `translate(${v.positionAbsolute.x}px,${v.positionAbsolute.y}px)`,
    pointerEvents: $ ? "all" : "none",
    visibility: V ? "visible" : "hidden",
    ...p.style,
    ...P
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: O, onMouseMove: B, onMouseLeave: F, onContextMenu: Z, onClick: te, onDoubleClick: q, onKeyDown: A ? ie : void 0, tabIndex: A ? 0 : void 0, onFocus: A ? G : void 0, role: p.ariaRole ?? (A ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": g ? void 0 : `${Na}-${x}`, "aria-label": p.ariaLabel, ...p.domAttributes, children: u.jsx(pp, { value: e, children: u.jsx(I, { id: e, data: p.data, type: N, positionAbsoluteX: v.positionAbsolute.x, positionAbsoluteY: v.positionAbsolute.y, selected: p.selected ?? !1, selectable: D, draggable: j, deletable: p.deletable ?? !0, isConnectable: H, sourcePosition: p.sourcePosition, targetPosition: p.targetPosition, dragging: C, dragHandle: p.dragHandle, zIndex: v.z, parentId: p.parentId, ...M }) }) });
}
var Pp = Se(Dp);
const $p = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function za(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: i } = ue($p, me), s = Ip(e.onlyRenderVisibleElements), a = Mp();
  return u.jsx("div", { className: "react-flow__nodes", style: bo, children: s.map((l) => (
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
    u.jsx(Pp, { id: l, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: a, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: i }, l)
  )) });
}
za.displayName = "NodeRenderer";
const Tp = Se(za);
function Rp(e) {
  return ue(ge((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const i = n.nodeLookup.get(r.source), s = n.nodeLookup.get(r.target);
        i && s && jf({
          sourceNode: i,
          targetNode: s,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && o.push(r.id);
      }
    return o;
  }, [e]), me);
}
const zp = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return u.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, Lp = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return u.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Gi = {
  [no.Arrow]: zp,
  [no.ArrowClosed]: Lp
};
function Hp(e) {
  const t = xe();
  return ye(() => Object.prototype.hasOwnProperty.call(Gi, e) ? Gi[e] : (t.getState().onError?.("009", Le.error009(e)), null), [e]);
}
const Vp = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: i = "strokeWidth", strokeWidth: s, orient: a = "auto-start-reverse" }) => {
  const l = Hp(t);
  return l ? u.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: i, orient: a, refX: "0", refY: "0", children: u.jsx(l, { color: n, strokeWidth: s }) }) : null;
}, La = ({ defaultColor: e, rfId: t }) => {
  const n = ue((i) => i.edges), o = ue((i) => i.defaultEdgeOptions), r = ye(() => Hf(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? u.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: u.jsx("defs", { children: r.map((i) => u.jsx(Vp, { id: i.id, type: i.type, color: i.color, width: i.width, height: i.height, markerUnits: i.markerUnits, strokeWidth: i.strokeWidth, orient: i.orient }, i.id)) }) }) : null;
};
La.displayName = "MarkerDefinitions";
var Op = Se(La);
function Ha({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: i, labelBgPadding: s = [2, 4], labelBgBorderRadius: a = 2, children: l, className: c, ...d }) {
  const [f, h] = ee({ x: 1, y: 0, width: 0, height: 0 }), y = Ee(["react-flow__edge-textwrapper", c]), g = ae(null);
  return ce(() => {
    if (g.current) {
      const x = g.current.getBBox();
      h({
        x: x.x,
        y: x.y,
        width: x.width,
        height: x.height
      });
    }
  }, [n]), n ? u.jsxs("g", { transform: `translate(${e - f.width / 2} ${t - f.height / 2})`, className: y, visibility: f.width ? "visible" : "hidden", ...d, children: [r && u.jsx("rect", { width: f.width + 2 * s[0], x: -s[0], y: -s[1], height: f.height + 2 * s[1], className: "react-flow__edge-textbg", style: i, rx: a, ry: a }), u.jsx("text", { className: "react-flow__edge-text", y: f.height / 2, dy: "0.3em", ref: g, style: o, children: n }), l] }) : null;
}
Ha.displayName = "EdgeText";
const Bp = Se(Ha);
function mn({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: a, labelBgBorderRadius: l, interactionWidth: c = 20, ...d }) {
  return u.jsxs(u.Fragment, { children: [u.jsx("path", { ...d, d: e, fill: "none", className: Ee(["react-flow__edge-path", d.className]) }), c ? u.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: c, className: "react-flow__edge-interaction" }) : null, o && We(t) && We(n) ? u.jsx(Bp, { x: t, y: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: s, labelBgPadding: a, labelBgBorderRadius: l }) : null] });
}
function Qi({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === Q.Left || e === Q.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function Va({ sourceX: e, sourceY: t, sourcePosition: n = Q.Bottom, targetX: o, targetY: r, targetPosition: i = Q.Top }) {
  const [s, a] = Qi({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [l, c] = Qi({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [d, f, h, y] = ia({
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
    h,
    y
  ];
}
function Oa(e) {
  return Se(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: a, label: l, labelStyle: c, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: y, style: g, markerEnd: x, markerStart: w, interactionWidth: m }) => {
    const [S, p, v] = Va({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: a
    }), E = e.isInternal ? void 0 : t;
    return u.jsx(mn, { id: E, path: S, labelX: p, labelY: v, label: l, labelStyle: c, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: y, style: g, markerEnd: x, markerStart: w, interactionWidth: m });
  });
}
const Fp = Oa({ isInternal: !1 }), Ba = Oa({ isInternal: !0 });
Fp.displayName = "SimpleBezierEdge";
Ba.displayName = "SimpleBezierEdgeInternal";
function Fa(e) {
  return Se(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: d, labelBgBorderRadius: f, style: h, sourcePosition: y = Q.Bottom, targetPosition: g = Q.Top, markerEnd: x, markerStart: w, pathOptions: m, interactionWidth: S }) => {
    const [p, v, E] = ro({
      sourceX: n,
      sourceY: o,
      sourcePosition: y,
      targetX: r,
      targetY: i,
      targetPosition: g,
      borderRadius: m?.borderRadius,
      offset: m?.offset,
      stepPosition: m?.stepPosition
    }), N = e.isInternal ? void 0 : t;
    return u.jsx(mn, { id: N, path: p, labelX: v, labelY: E, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: x, markerStart: w, interactionWidth: S });
  });
}
const Wa = Fa({ isInternal: !1 }), Ya = Fa({ isInternal: !0 });
Wa.displayName = "SmoothStepEdge";
Ya.displayName = "SmoothStepEdgeInternal";
function Xa(e) {
  return Se(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return u.jsx(Wa, { ...n, id: o, pathOptions: ye(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Wp = Xa({ isInternal: !1 }), qa = Xa({ isInternal: !0 });
Wp.displayName = "StepEdge";
qa.displayName = "StepEdgeInternal";
function Za(e) {
  return Se(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: y, markerStart: g, interactionWidth: x }) => {
    const [w, m, S] = la({ sourceX: n, sourceY: o, targetX: r, targetY: i }), p = e.isInternal ? void 0 : t;
    return u.jsx(mn, { id: p, path: w, labelX: m, labelY: S, label: s, labelStyle: a, labelShowBg: l, labelBgStyle: c, labelBgPadding: d, labelBgBorderRadius: f, style: h, markerEnd: y, markerStart: g, interactionWidth: x });
  });
}
const Yp = Za({ isInternal: !1 }), Ua = Za({ isInternal: !0 });
Yp.displayName = "StraightEdge";
Ua.displayName = "StraightEdgeInternal";
function Ka(e) {
  return Se(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s = Q.Bottom, targetPosition: a = Q.Top, label: l, labelStyle: c, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: y, style: g, markerEnd: x, markerStart: w, pathOptions: m, interactionWidth: S }) => {
    const [p, v, E] = sa({
      sourceX: n,
      sourceY: o,
      sourcePosition: s,
      targetX: r,
      targetY: i,
      targetPosition: a,
      curvature: m?.curvature
    }), N = e.isInternal ? void 0 : t;
    return u.jsx(mn, { id: N, path: p, labelX: v, labelY: E, label: l, labelStyle: c, labelShowBg: d, labelBgStyle: f, labelBgPadding: h, labelBgBorderRadius: y, style: g, markerEnd: x, markerStart: w, interactionWidth: S });
  });
}
const Xp = Ka({ isInternal: !1 }), Ga = Ka({ isInternal: !0 });
Xp.displayName = "BezierEdge";
Ga.displayName = "BezierEdgeInternal";
const Ji = {
  default: Ga,
  straight: Ua,
  step: qa,
  smoothstep: Ya,
  simplebezier: Ba
}, es = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, qp = (e, t, n) => n === Q.Left ? e - t : n === Q.Right ? e + t : e, Zp = (e, t, n) => n === Q.Top ? e - t : n === Q.Bottom ? e + t : e, ts = "react-flow__edgeupdater";
function ns({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: i, onMouseOut: s, type: a }) {
  return u.jsx("circle", { onMouseDown: r, onMouseEnter: i, onMouseOut: s, className: Ee([ts, `${ts}-${a}`]), cx: qp(t, o, e), cy: Zp(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function Up({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: i, targetY: s, sourcePosition: a, targetPosition: l, onReconnect: c, onReconnectStart: d, onReconnectEnd: f, setReconnecting: h, setUpdateHover: y }) {
  const g = xe(), x = (v, E) => {
    if (v.button !== 0)
      return;
    const { autoPanOnConnect: N, domNode: I, connectionMode: j, connectionRadius: D, lib: H, onConnectStart: A, cancelConnection: R, nodeLookup: V, rfId: b, panBy: C, updateConnection: _ } = g.getState(), M = E.type === "target", P = (B, F) => {
      h(!1), f?.(B, n, E.type, F);
    }, $ = (B) => c?.(n, B), O = (B, F) => {
      h(!0), d?.(v, n, E.type), A?.(B, F);
    };
    xr.onPointerDown(v.nativeEvent, {
      autoPanOnConnect: N,
      connectionMode: j,
      connectionRadius: D,
      domNode: I,
      handleId: E.id,
      nodeId: E.nodeId,
      nodeLookup: V,
      isTarget: M,
      edgeUpdaterType: E.type,
      lib: H,
      flowId: b,
      cancelConnection: R,
      panBy: C,
      isValidConnection: (...B) => g.getState().isValidConnection?.(...B) ?? !0,
      onConnect: $,
      onConnectStart: O,
      onConnectEnd: (...B) => g.getState().onConnectEnd?.(...B),
      onReconnectEnd: P,
      updateConnection: _,
      getTransform: () => g.getState().transform,
      getFromHandle: () => g.getState().connection.fromHandle,
      dragThreshold: g.getState().connectionDragThreshold,
      handleDomNode: v.currentTarget
    });
  }, w = (v) => x(v, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), m = (v) => x(v, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), S = () => y(!0), p = () => y(!1);
  return u.jsxs(u.Fragment, { children: [(e === !0 || e === "source") && u.jsx(ns, { position: a, centerX: o, centerY: r, radius: t, onMouseDown: w, onMouseEnter: S, onMouseOut: p, type: "source" }), (e === !0 || e === "target") && u.jsx(ns, { position: l, centerX: i, centerY: s, radius: t, onMouseDown: m, onMouseEnter: S, onMouseOut: p, type: "target" })] });
}
function Kp({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: i, onContextMenu: s, onMouseEnter: a, onMouseMove: l, onMouseLeave: c, reconnectRadius: d, onReconnect: f, onReconnectStart: h, onReconnectEnd: y, rfId: g, edgeTypes: x, noPanClassName: w, onError: m, disableKeyboardA11y: S }) {
  let p = ue((U) => U.edgeLookup.get(e));
  const v = ue((U) => U.defaultEdgeOptions);
  p = v ? { ...v, ...p } : p;
  let E = p.type || "default", N = x?.[E] || Ji[E];
  N === void 0 && (m?.("011", Le.error011(E)), E = "default", N = x?.default || Ji.default);
  const I = !!(p.focusable || t && typeof p.focusable > "u"), j = typeof f < "u" && (p.reconnectable || n && typeof p.reconnectable > "u"), D = !!(p.selectable || o && typeof p.selectable > "u"), H = ae(null), [A, R] = ee(!1), [V, b] = ee(!1), C = xe(), { zIndex: _, sourceX: M, sourceY: P, targetX: $, targetY: O, sourcePosition: B, targetPosition: F } = ue(ge((U) => {
    const J = U.nodeLookup.get(p.source), se = U.nodeLookup.get(p.target);
    if (!J || !se)
      return {
        zIndex: p.zIndex,
        ...es
      };
    const L = Lf({
      id: e,
      sourceNode: J,
      targetNode: se,
      sourceHandle: p.sourceHandle || null,
      targetHandle: p.targetHandle || null,
      connectionMode: U.connectionMode,
      onError: m
    });
    return {
      zIndex: Mf({
        selected: p.selected,
        zIndex: p.zIndex,
        sourceNode: J,
        targetNode: se,
        elevateOnSelect: U.elevateEdgesOnSelect,
        zIndexMode: U.zIndexMode
      }),
      ...L || es
    };
  }, [p.source, p.target, p.sourceHandle, p.targetHandle, p.selected, p.zIndex]), me), Z = ye(() => p.markerStart ? `url('#${yr(p.markerStart, g)}')` : void 0, [p.markerStart, g]), q = ye(() => p.markerEnd ? `url('#${yr(p.markerEnd, g)}')` : void 0, [p.markerEnd, g]);
  if (p.hidden || M === null || P === null || $ === null || O === null)
    return null;
  const te = (U) => {
    const { addSelectedEdges: J, unselectNodesAndEdges: se, multiSelectionActive: L } = C.getState();
    D && (C.setState({ nodesSelectionActive: !1 }), p.selected && L ? (se({ nodes: [], edges: [p] }), H.current?.blur()) : J([e])), r && r(U, p);
  }, ie = i ? (U) => {
    i(U, { ...p });
  } : void 0, G = s ? (U) => {
    s(U, { ...p });
  } : void 0, z = a ? (U) => {
    a(U, { ...p });
  } : void 0, Y = l ? (U) => {
    l(U, { ...p });
  } : void 0, re = c ? (U) => {
    c(U, { ...p });
  } : void 0, oe = (U) => {
    if (!S && Ys.includes(U.key) && D) {
      const { unselectNodesAndEdges: J, addSelectedEdges: se } = C.getState();
      U.key === "Escape" ? (H.current?.blur(), J({ edges: [p] })) : se([e]);
    }
  };
  return u.jsx("svg", { style: { zIndex: _ }, children: u.jsxs("g", { className: Ee([
    "react-flow__edge",
    `react-flow__edge-${E}`,
    p.className,
    w,
    {
      selected: p.selected,
      animated: p.animated,
      inactive: !D && !r,
      updating: A,
      selectable: D
    }
  ]), onClick: te, onDoubleClick: ie, onContextMenu: G, onMouseEnter: z, onMouseMove: Y, onMouseLeave: re, onKeyDown: I ? oe : void 0, tabIndex: I ? 0 : void 0, role: p.ariaRole ?? (I ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": p.ariaLabel === null ? void 0 : p.ariaLabel || `Edge from ${p.source} to ${p.target}`, "aria-describedby": I ? `${Ea}-${g}` : void 0, ref: H, ...p.domAttributes, children: [!V && u.jsx(N, { id: e, source: p.source, target: p.target, type: p.type, selected: p.selected, animated: p.animated, selectable: D, deletable: p.deletable ?? !0, label: p.label, labelStyle: p.labelStyle, labelShowBg: p.labelShowBg, labelBgStyle: p.labelBgStyle, labelBgPadding: p.labelBgPadding, labelBgBorderRadius: p.labelBgBorderRadius, sourceX: M, sourceY: P, targetX: $, targetY: O, sourcePosition: B, targetPosition: F, data: p.data, style: p.style, sourceHandleId: p.sourceHandle, targetHandleId: p.targetHandle, markerStart: Z, markerEnd: q, pathOptions: "pathOptions" in p ? p.pathOptions : void 0, interactionWidth: p.interactionWidth }), j && u.jsx(Up, { edge: p, isReconnectable: j, reconnectRadius: d, onReconnect: f, onReconnectStart: h, onReconnectEnd: y, sourceX: M, sourceY: P, targetX: $, targetY: O, sourcePosition: B, targetPosition: F, setUpdateHover: R, setReconnecting: b })] }) });
}
var Gp = Se(Kp);
const Qp = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function Qa({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: i, onEdgeContextMenu: s, onEdgeMouseEnter: a, onEdgeMouseMove: l, onEdgeMouseLeave: c, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: h, onReconnectStart: y, onReconnectEnd: g, disableKeyboardA11y: x }) {
  const { edgesFocusable: w, edgesReconnectable: m, elementsSelectable: S, onError: p } = ue(Qp, me), v = Rp(t);
  return u.jsxs("div", { className: "react-flow__edges", children: [u.jsx(Op, { defaultColor: e, rfId: n }), v.map((E) => u.jsx(Gp, { id: E, edgesFocusable: w, edgesReconnectable: m, elementsSelectable: S, noPanClassName: r, onReconnect: i, onContextMenu: s, onMouseEnter: a, onMouseMove: l, onMouseLeave: c, onClick: d, reconnectRadius: f, onDoubleClick: h, onReconnectStart: y, onReconnectEnd: g, rfId: n, onError: p, edgeTypes: o, disableKeyboardA11y: x }, E))] });
}
Qa.displayName = "EdgeRenderer";
const Jp = Se(Qa), eg = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function tg({ children: e }) {
  const t = ue(eg);
  return u.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function ng(e) {
  const t = Vr(), n = ae(!1);
  ce(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const og = (e) => e.panZoom?.syncViewport;
function rg(e) {
  const t = ue(og), n = xe();
  return ce(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function ig(e) {
  return e.connection.inProgress ? { ...e.connection, to: Rt(e.connection.to, e.transform) } : { ...e.connection };
}
function sg(e) {
  return ig;
}
function ag(e) {
  const t = sg();
  return ue(t, me);
}
const cg = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function lg({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: i, height: s, isValid: a, inProgress: l } = ue(cg, me);
  return !(i && r && l) ? null : u.jsx("svg", { style: e, width: i, height: s, className: "react-flow__connectionline react-flow__container", children: u.jsx("g", { className: Ee(["react-flow__connection", Zs(a)]), children: u.jsx(Ja, { style: t, type: n, CustomComponent: o, isValid: a }) }) });
}
const Ja = ({ style: e, type: t = rt.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: i, fromNode: s, fromHandle: a, fromPosition: l, to: c, toNode: d, toHandle: f, toPosition: h, pointer: y } = ag();
  if (!r)
    return;
  if (n)
    return u.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: s, fromHandle: a, fromX: i.x, fromY: i.y, toX: c.x, toY: c.y, fromPosition: l, toPosition: h, connectionStatus: Zs(o), toNode: d, toHandle: f, pointer: y });
  let g = "";
  const x = {
    sourceX: i.x,
    sourceY: i.y,
    sourcePosition: l,
    targetX: c.x,
    targetY: c.y,
    targetPosition: h
  };
  switch (t) {
    case rt.Bezier:
      [g] = sa(x);
      break;
    case rt.SimpleBezier:
      [g] = Va(x);
      break;
    case rt.Step:
      [g] = ro({
        ...x,
        borderRadius: 0
      });
      break;
    case rt.SmoothStep:
      [g] = ro(x);
      break;
    default:
      [g] = la(x);
  }
  return u.jsx("path", { d: g, fill: "none", className: "react-flow__connection-path", style: e });
};
Ja.displayName = "ConnectionLine";
const ug = {};
function os(e = ug) {
  ae(e), xe(), ce(() => {
  }, [e]);
}
function dg() {
  xe(), ae(!1), ce(() => {
  }, []);
}
function ec({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: i, onEdgeDoubleClick: s, onNodeMouseEnter: a, onNodeMouseMove: l, onNodeMouseLeave: c, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: h, onSelectionEnd: y, connectionLineType: g, connectionLineStyle: x, connectionLineComponent: w, connectionLineContainerStyle: m, selectionKeyCode: S, selectionOnDrag: p, selectionMode: v, multiSelectionKeyCode: E, panActivationKeyCode: N, zoomActivationKeyCode: I, deleteKeyCode: j, onlyRenderVisibleElements: D, elementsSelectable: H, defaultViewport: A, translateExtent: R, minZoom: V, maxZoom: b, preventScrolling: C, defaultMarkerColor: _, zoomOnScroll: M, zoomOnPinch: P, panOnScroll: $, panOnScrollSpeed: O, panOnScrollMode: B, zoomOnDoubleClick: F, panOnDrag: Z, autoPanOnSelection: q, onPaneClick: te, onPaneMouseEnter: ie, onPaneMouseMove: G, onPaneMouseLeave: z, onPaneScroll: Y, onPaneContextMenu: re, paneClickDistance: oe, nodeClickDistance: U, onEdgeContextMenu: J, onEdgeMouseEnter: se, onEdgeMouseMove: L, onEdgeMouseLeave: K, reconnectRadius: he, onReconnect: we, onReconnectStart: be, onReconnectEnd: De, noDragClassName: fe, noWheelClassName: Ie, noPanClassName: Oe, disableKeyboardA11y: ve, nodeExtent: Pe, rfId: Ve, viewport: pe, onViewportChange: $e }) {
  return os(e), os(t), dg(), ng(n), rg(pe), u.jsx(Cp, { onPaneClick: te, onPaneMouseEnter: ie, onPaneMouseMove: G, onPaneMouseLeave: z, onPaneContextMenu: re, onPaneScroll: Y, paneClickDistance: oe, deleteKeyCode: j, selectionKeyCode: S, selectionOnDrag: p, selectionMode: v, onSelectionStart: h, onSelectionEnd: y, multiSelectionKeyCode: E, panActivationKeyCode: N, zoomActivationKeyCode: I, elementsSelectable: H, zoomOnScroll: M, zoomOnPinch: P, zoomOnDoubleClick: F, panOnScroll: $, panOnScrollSpeed: O, panOnScrollMode: B, panOnDrag: Z, autoPanOnSelection: q, defaultViewport: A, translateExtent: R, minZoom: V, maxZoom: b, onSelectionContextMenu: f, preventScrolling: C, noDragClassName: fe, noWheelClassName: Ie, noPanClassName: Oe, disableKeyboardA11y: ve, onViewportChange: $e, isControlledViewport: !!pe, children: u.jsxs(tg, { children: [u.jsx(Jp, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: s, onReconnect: we, onReconnectStart: be, onReconnectEnd: De, onlyRenderVisibleElements: D, onEdgeContextMenu: J, onEdgeMouseEnter: se, onEdgeMouseMove: L, onEdgeMouseLeave: K, reconnectRadius: he, defaultMarkerColor: _, noPanClassName: Oe, disableKeyboardA11y: ve, rfId: Ve }), u.jsx(lg, { style: x, type: g, component: w, containerStyle: m }), u.jsx("div", { className: "react-flow__edgelabel-renderer" }), u.jsx(Tp, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: i, onNodeMouseEnter: a, onNodeMouseMove: l, onNodeMouseLeave: c, onNodeContextMenu: d, nodeClickDistance: U, onlyRenderVisibleElements: D, noPanClassName: Oe, noDragClassName: fe, disableKeyboardA11y: ve, nodeExtent: Pe, rfId: Ve }), u.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
ec.displayName = "GraphView";
const fg = Se(ec), hg = Js(), rs = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: a, minZoom: l = 0.5, maxZoom: c = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: h = "basic" } = {}) => {
  const y = /* @__PURE__ */ new Map(), g = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), m = o ?? t ?? [], S = n ?? e ?? [], p = d ?? [0, 0], v = f ?? rn;
  fa(x, w, m);
  const { nodesInitialized: E } = mr(S, y, g, {
    nodeOrigin: p,
    nodeExtent: v,
    zIndexMode: h
  });
  let N = [0, 0, 1];
  if (s && r && i) {
    const I = gn(y, {
      filter: (A) => !!((A.width || A.initialWidth) && (A.height || A.initialHeight))
    }), { x: j, y: D, zoom: H } = Pr(I, r, i, l, c, a?.padding ?? 0.1);
    N = [j, D, H];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: i ?? 0,
    transform: N,
    nodes: S,
    nodesInitialized: E,
    nodeLookup: y,
    parentLookup: g,
    edges: m,
    edgeLookup: w,
    connectionLookup: x,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: o !== void 0,
    panZoom: null,
    minZoom: l,
    maxZoom: c,
    translateExtent: rn,
    nodeExtent: v,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: Ct.Strict,
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
    connection: { ...qs },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: hg,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Xs,
    zIndexMode: h,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, pg = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: s, fitViewOptions: a, minZoom: l, maxZoom: c, nodeOrigin: d, nodeExtent: f, zIndexMode: h }) => kh((y, g) => {
  async function x() {
    const { nodeLookup: w, panZoom: m, fitViewOptions: S, fitViewResolver: p, width: v, height: E, minZoom: N, maxZoom: I } = g();
    m && (await Nf({
      nodes: w,
      width: v,
      height: E,
      panZoom: m,
      minZoom: N,
      maxZoom: I
    }, S), p?.resolve(!0), y({ fitViewResolver: null }));
  }
  return {
    ...rs({
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
      zIndexMode: h
    }),
    setNodes: (w) => {
      const { nodeLookup: m, parentLookup: S, nodeOrigin: p, elevateNodesOnSelect: v, fitViewQueued: E, zIndexMode: N, nodesSelectionActive: I } = g(), { nodesInitialized: j, hasSelectedNodes: D } = mr(w, m, S, {
        nodeOrigin: p,
        nodeExtent: f,
        elevateNodesOnSelect: v,
        checkEquality: !0,
        zIndexMode: N
      }), H = I && D;
      E && j ? (x(), y({
        nodes: w,
        nodesInitialized: j,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: H
      })) : y({ nodes: w, nodesInitialized: j, nodesSelectionActive: H });
    },
    setEdges: (w) => {
      const { connectionLookup: m, edgeLookup: S } = g();
      fa(m, S, w), y({ edges: w });
    },
    setDefaultNodesAndEdges: (w, m) => {
      if (w) {
        const { setNodes: S } = g();
        S(w), y({ hasDefaultNodes: !0 });
      }
      if (m) {
        const { setEdges: S } = g();
        S(m), y({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (w) => {
      const { triggerNodeChanges: m, nodeLookup: S, parentLookup: p, domNode: v, nodeOrigin: E, nodeExtent: N, debug: I, fitViewQueued: j, zIndexMode: D } = g(), { changes: H, updatedInternals: A } = Xf(w, S, p, v, E, N, D);
      A && (Bf(S, p, { nodeOrigin: E, nodeExtent: N, zIndexMode: D }), j ? (x(), y({ fitViewQueued: !1, fitViewOptions: void 0 })) : y({}), H?.length > 0 && (I && console.log("React Flow: trigger node changes", H), m?.(H)));
    },
    updateNodePositions: (w, m = !1) => {
      const S = [];
      let p = [];
      const { nodeLookup: v, triggerNodeChanges: E, connection: N, updateConnection: I, onNodesChangeMiddlewareMap: j } = g();
      for (const [D, H] of w) {
        const A = v.get(D), R = !!(A?.expandParent && A?.parentId && H?.position), V = {
          id: D,
          type: "position",
          position: R ? {
            x: Math.max(0, H.position.x),
            y: Math.max(0, H.position.y)
          } : H.position,
          dragging: m
        };
        if (A && N.inProgress && N.fromNode.id === A.id) {
          const b = yt(A, N.fromHandle, Q.Left, !0);
          I({ ...N, from: b });
        }
        R && A.parentId && S.push({
          id: D,
          parentId: A.parentId,
          rect: {
            ...H.internals.positionAbsolute,
            width: H.measured.width ?? 0,
            height: H.measured.height ?? 0
          }
        }), p.push(V);
      }
      if (S.length > 0) {
        const { parentLookup: D, nodeOrigin: H } = g(), A = Hr(S, v, D, H);
        p.push(...A);
      }
      for (const D of j.values())
        p = D(p);
      E(p);
    },
    triggerNodeChanges: (w) => {
      const { onNodesChange: m, setNodes: S, nodes: p, hasDefaultNodes: v, debug: E } = g();
      if (w?.length) {
        if (v) {
          const N = ka(w, p);
          S(N);
        }
        E && console.log("React Flow: trigger node changes", w), m?.(w);
      }
    },
    triggerEdgeChanges: (w) => {
      const { onEdgesChange: m, setEdges: S, edges: p, hasDefaultEdges: v, debug: E } = g();
      if (w?.length) {
        if (v) {
          const N = Ia(w, p);
          S(N);
        }
        E && console.log("React Flow: trigger edge changes", w), m?.(w);
      }
    },
    addSelectedNodes: (w) => {
      const { multiSelectionActive: m, edgeLookup: S, nodeLookup: p, triggerNodeChanges: v, triggerEdgeChanges: E } = g();
      if (m) {
        const N = w.map((I) => ct(I, !0));
        v(N);
        return;
      }
      v(bt(p, /* @__PURE__ */ new Set([...w]), !0)), E(bt(S));
    },
    addSelectedEdges: (w) => {
      const { multiSelectionActive: m, edgeLookup: S, nodeLookup: p, triggerNodeChanges: v, triggerEdgeChanges: E } = g();
      if (m) {
        const N = w.map((I) => ct(I, !0));
        E(N);
        return;
      }
      E(bt(S, /* @__PURE__ */ new Set([...w]))), v(bt(p, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: w, edges: m } = {}) => {
      const { edges: S, nodes: p, nodeLookup: v, triggerNodeChanges: E, triggerEdgeChanges: N } = g(), I = w || p, j = m || S, D = [];
      for (const A of I) {
        if (!A.selected)
          continue;
        const R = v.get(A.id);
        R && (R.selected = !1), D.push(ct(A.id, !1));
      }
      const H = [];
      for (const A of j)
        A.selected && H.push(ct(A.id, !1));
      E(D), N(H);
    },
    setMinZoom: (w) => {
      const { panZoom: m, maxZoom: S } = g();
      m?.setScaleExtent([w, S]), y({ minZoom: w });
    },
    setMaxZoom: (w) => {
      const { panZoom: m, minZoom: S } = g();
      m?.setScaleExtent([S, w]), y({ maxZoom: w });
    },
    setTranslateExtent: (w) => {
      g().panZoom?.setTranslateExtent(w), y({ translateExtent: w });
    },
    resetSelectedElements: () => {
      const { edges: w, nodes: m, triggerNodeChanges: S, triggerEdgeChanges: p, elementsSelectable: v } = g();
      if (!v)
        return;
      const E = m.reduce((I, j) => j.selected ? [...I, ct(j.id, !1)] : I, []), N = w.reduce((I, j) => j.selected ? [...I, ct(j.id, !1)] : I, []);
      S(E), p(N);
    },
    setNodeExtent: (w) => {
      const { nodes: m, nodeLookup: S, parentLookup: p, nodeOrigin: v, elevateNodesOnSelect: E, nodeExtent: N, zIndexMode: I } = g();
      w[0][0] === N[0][0] && w[0][1] === N[0][1] && w[1][0] === N[1][0] && w[1][1] === N[1][1] || (mr(m, S, p, {
        nodeOrigin: v,
        nodeExtent: w,
        elevateNodesOnSelect: E,
        checkEquality: !1,
        zIndexMode: I
      }), y({ nodeExtent: w }));
    },
    panBy: (w) => {
      const { transform: m, width: S, height: p, panZoom: v, translateExtent: E } = g();
      return qf({ delta: w, panZoom: v, transform: m, translateExtent: E, width: S, height: p });
    },
    setCenter: async (w, m, S) => {
      const { width: p, height: v, maxZoom: E, panZoom: N } = g();
      if (!N)
        return !1;
      const I = typeof S?.zoom < "u" ? S.zoom : E;
      return await N.setViewport({
        x: p / 2 - w * I,
        y: v / 2 - m * I,
        zoom: I
      }, { duration: S?.duration, ease: S?.ease, interpolate: S?.interpolate }), !0;
    },
    cancelConnection: () => {
      y({
        connection: { ...qs }
      });
    },
    updateConnection: (w) => {
      y({ connection: w });
    },
    reset: () => y({ ...rs() })
  };
}, Object.is);
function gg({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: i, initialMinZoom: s, initialMaxZoom: a, initialFitViewOptions: l, fitView: c, nodeOrigin: d, nodeExtent: f, zIndexMode: h, children: y }) {
  const [g] = ee(() => pg({
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
    zIndexMode: h
  }));
  return u.jsx(jh, { value: g, children: u.jsx(tp, { children: y }) });
}
function yg({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: i, height: s, fitView: a, fitViewOptions: l, minZoom: c, maxZoom: d, nodeOrigin: f, nodeExtent: h, zIndexMode: y }) {
  return dn(wo) ? u.jsx(u.Fragment, { children: e }) : u.jsx(gg, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: i, initialHeight: s, fitView: a, initialFitViewOptions: l, initialMinZoom: c, initialMaxZoom: d, nodeOrigin: f, nodeExtent: h, zIndexMode: y, children: e });
}
const mg = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function xg({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: i, edgeTypes: s, onNodeClick: a, onEdgeClick: l, onInit: c, onMove: d, onMoveStart: f, onMoveEnd: h, onConnect: y, onConnectStart: g, onConnectEnd: x, onClickConnectStart: w, onClickConnectEnd: m, onNodeMouseEnter: S, onNodeMouseMove: p, onNodeMouseLeave: v, onNodeContextMenu: E, onNodeDoubleClick: N, onNodeDragStart: I, onNodeDrag: j, onNodeDragStop: D, onNodesDelete: H, onEdgesDelete: A, onDelete: R, onSelectionChange: V, onSelectionDragStart: b, onSelectionDrag: C, onSelectionDragStop: _, onSelectionContextMenu: M, onSelectionStart: P, onSelectionEnd: $, onBeforeDelete: O, connectionMode: B, connectionLineType: F = rt.Bezier, connectionLineStyle: Z, connectionLineComponent: q, connectionLineContainerStyle: te, deleteKeyCode: ie = "Backspace", selectionKeyCode: G = "Shift", selectionOnDrag: z = !1, selectionMode: Y = sn.Full, panActivationKeyCode: re = "Space", multiSelectionKeyCode: oe = cn() ? "Meta" : "Control", zoomActivationKeyCode: U = cn() ? "Meta" : "Control", snapToGrid: J, snapGrid: se, onlyRenderVisibleElements: L = !1, selectNodesOnDrag: K, nodesDraggable: he, autoPanOnNodeFocus: we, nodesConnectable: be, nodesFocusable: De, nodeOrigin: fe = _a, edgesFocusable: Ie, edgesReconnectable: Oe, elementsSelectable: ve = !0, defaultViewport: Pe = Wh, minZoom: Ve = 0.5, maxZoom: pe = 2, translateExtent: $e = rn, preventScrolling: tt = !0, nodeExtent: zt, defaultMarkerColor: vn = "#b1b1b7", zoomOnScroll: bn = !0, zoomOnPinch: Lt = !0, panOnScroll: No = !1, panOnScrollSpeed: Ht = 0.5, panOnScrollMode: mt = dt.Free, zoomOnDoubleClick: Te = !0, panOnDrag: Vt = !0, onPaneClick: Ot, onPaneMouseEnter: Bt, onPaneMouseMove: xt, onPaneMouseLeave: Eo, onPaneScroll: _o, onPaneContextMenu: Co, paneClickDistance: ko = 1, nodeClickDistance: Sn = 0, children: Nn, onReconnect: En, onReconnectStart: Io, onReconnectEnd: Ft, onEdgeContextMenu: Ao, onEdgeDoubleClick: Mo, onEdgeMouseEnter: jo, onEdgeMouseMove: Do, onEdgeMouseLeave: Po, reconnectRadius: Wt = 10, onNodesChange: $o, onEdgesChange: To, noDragClassName: Ro = "nodrag", noWheelClassName: zo = "nowheel", noPanClassName: _n = "nopan", fitView: Cn, fitViewOptions: kn, connectOnClick: In, attributionPosition: An, proOptions: Lo, defaultEdgeOptions: Ho, elevateNodesOnSelect: Vo = !0, elevateEdgesOnSelect: Oo = !1, disableKeyboardA11y: Mn = !1, autoPanOnConnect: k, autoPanOnNodeDrag: T, autoPanOnSelection: W = !0, autoPanSpeed: X, connectionRadius: ne, isValidConnection: de, onError: le, style: _e, id: Ne, nodeDragThreshold: at, connectionDragThreshold: Ae, viewport: Nc, onViewportChange: Ec, width: _c, height: Cc, colorMode: kc = "light", debug: Ic, onScroll: Xr, ariaLabelConfig: Ac, zIndexMode: qr = "basic", ...Mc }, jc) {
  const Bo = Ne || "1", Dc = Zh(kc), Pc = ge((Zr) => {
    Zr.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), Xr?.(Zr);
  }, [Xr]);
  return u.jsx("div", { "data-testid": "rf__wrapper", ...Mc, onScroll: Pc, style: { ..._e, ...mg }, ref: jc, className: Ee(["react-flow", r, Dc]), id: Ne, role: "application", children: u.jsxs(yg, { nodes: e, edges: t, width: _c, height: Cc, fitView: Cn, fitViewOptions: kn, minZoom: Ve, maxZoom: pe, nodeOrigin: fe, nodeExtent: zt, zIndexMode: qr, children: [u.jsx(qh, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: y, onConnectStart: g, onConnectEnd: x, onClickConnectStart: w, onClickConnectEnd: m, nodesDraggable: he, autoPanOnNodeFocus: we, nodesConnectable: be, nodesFocusable: De, edgesFocusable: Ie, edgesReconnectable: Oe, elementsSelectable: ve, elevateNodesOnSelect: Vo, elevateEdgesOnSelect: Oo, minZoom: Ve, maxZoom: pe, nodeExtent: zt, onNodesChange: $o, onEdgesChange: To, snapToGrid: J, snapGrid: se, connectionMode: B, translateExtent: $e, connectOnClick: In, defaultEdgeOptions: Ho, fitView: Cn, fitViewOptions: kn, onNodesDelete: H, onEdgesDelete: A, onDelete: R, onNodeDragStart: I, onNodeDrag: j, onNodeDragStop: D, onSelectionDrag: C, onSelectionDragStart: b, onSelectionDragStop: _, onMove: d, onMoveStart: f, onMoveEnd: h, noPanClassName: _n, nodeOrigin: fe, rfId: Bo, autoPanOnConnect: k, autoPanOnNodeDrag: T, autoPanSpeed: X, onError: le, connectionRadius: ne, isValidConnection: de, selectNodesOnDrag: K, nodeDragThreshold: at, connectionDragThreshold: Ae, onBeforeDelete: O, debug: Ic, ariaLabelConfig: Ac, zIndexMode: qr }), u.jsx(fg, { onInit: c, onNodeClick: a, onEdgeClick: l, onNodeMouseEnter: S, onNodeMouseMove: p, onNodeMouseLeave: v, onNodeContextMenu: E, onNodeDoubleClick: N, nodeTypes: i, edgeTypes: s, connectionLineType: F, connectionLineStyle: Z, connectionLineComponent: q, connectionLineContainerStyle: te, selectionKeyCode: G, selectionOnDrag: z, selectionMode: Y, deleteKeyCode: ie, multiSelectionKeyCode: oe, panActivationKeyCode: re, zoomActivationKeyCode: U, onlyRenderVisibleElements: L, defaultViewport: Pe, translateExtent: $e, minZoom: Ve, maxZoom: pe, preventScrolling: tt, zoomOnScroll: bn, zoomOnPinch: Lt, zoomOnDoubleClick: Te, panOnScroll: No, panOnScrollSpeed: Ht, panOnScrollMode: mt, panOnDrag: Vt, autoPanOnSelection: W, onPaneClick: Ot, onPaneMouseEnter: Bt, onPaneMouseMove: xt, onPaneMouseLeave: Eo, onPaneScroll: _o, onPaneContextMenu: Co, paneClickDistance: ko, nodeClickDistance: Sn, onSelectionContextMenu: M, onSelectionStart: P, onSelectionEnd: $, onReconnect: En, onReconnectStart: Io, onReconnectEnd: Ft, onEdgeContextMenu: Ao, onEdgeDoubleClick: Mo, onEdgeMouseEnter: jo, onEdgeMouseMove: Do, onEdgeMouseLeave: Po, reconnectRadius: Wt, defaultMarkerColor: vn, noDragClassName: Ro, noWheelClassName: zo, noPanClassName: _n, rfId: Bo, disableKeyboardA11y: Mn, nodeExtent: zt, viewport: Nc, onViewportChange: Ec }), u.jsx(Fh, { onSelectionChange: V }), Nn, u.jsx(Lh, { proOptions: Lo, position: An }), u.jsx(zh, { rfId: Bo, disableKeyboardA11y: Mn })] }) });
}
var wg = ja(xg);
const vg = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function bg({ children: e }) {
  const t = ue(vg);
  return t ? Mh.createPortal(e, t) : null;
}
function Sg({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return u.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ee(["react-flow__background-pattern", n, o]) });
}
function Ng({ radius: e, className: t }) {
  return u.jsx("circle", { cx: e, cy: e, r: e, className: Ee(["react-flow__background-pattern", "dots", t]) });
}
var it;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(it || (it = {}));
const Eg = {
  [it.Dots]: 1,
  [it.Lines]: 1,
  [it.Cross]: 6
}, _g = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function tc({
  id: e,
  variant: t = it.Dots,
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
  const f = ae(null), { transform: h, patternId: y } = ue(_g, me), g = o || Eg[t], x = t === it.Dots, w = t === it.Cross, m = Array.isArray(n) ? n : [n, n], S = [m[0] * h[2] || 1, m[1] * h[2] || 1], p = g * h[2], v = Array.isArray(i) ? i : [i, i], E = w ? [p, p] : S, N = [
    v[0] * h[2] || 1 + E[0] / 2,
    v[1] * h[2] || 1 + E[1] / 2
  ], I = `${y}${e || ""}`;
  return u.jsxs("svg", { className: Ee(["react-flow__background", c]), style: {
    ...l,
    ...bo,
    "--xy-background-color-props": a,
    "--xy-background-pattern-color-props": s
  }, ref: f, "data-testid": "rf__background", children: [u.jsx("pattern", { id: I, x: h[0] % S[0], y: h[1] % S[1], width: S[0], height: S[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${N[0]},-${N[1]})`, children: x ? u.jsx(Ng, { radius: p / 2, className: d }) : u.jsx(Sg, { dimensions: E, lineWidth: r, variant: t, className: d }) }), u.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${I})` })] });
}
tc.displayName = "Background";
const Cg = Se(tc);
function kg() {
  return u.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: u.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function Ig() {
  return u.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: u.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function Ag() {
  return u.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: u.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function Mg() {
  return u.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: u.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function jg() {
  return u.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: u.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function Hn({ children: e, className: t, ...n }) {
  return u.jsx("button", { type: "button", className: Ee(["react-flow__controls-button", t]), ...n, children: e });
}
const Dg = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function nc({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: i, onZoomOut: s, onFitView: a, onInteractiveChange: l, className: c, children: d, position: f = "bottom-left", orientation: h = "vertical", "aria-label": y }) {
  const g = xe(), { isInteractive: x, minZoomReached: w, maxZoomReached: m, ariaLabelConfig: S } = ue(Dg, me), { zoomIn: p, zoomOut: v, fitView: E } = Vr(), N = () => {
    p(), i?.();
  }, I = () => {
    v(), s?.();
  }, j = () => {
    E(r), a?.();
  }, D = () => {
    g.setState({
      nodesDraggable: !x,
      nodesConnectable: !x,
      elementsSelectable: !x
    }), l?.(!x);
  }, H = h === "horizontal" ? "horizontal" : "vertical";
  return u.jsxs(vo, { className: Ee(["react-flow__controls", H, c]), position: f, style: e, "data-testid": "rf__controls", "aria-label": y ?? S["controls.ariaLabel"], children: [t && u.jsxs(u.Fragment, { children: [u.jsx(Hn, { onClick: N, className: "react-flow__controls-zoomin", title: S["controls.zoomIn.ariaLabel"], "aria-label": S["controls.zoomIn.ariaLabel"], disabled: m, children: u.jsx(kg, {}) }), u.jsx(Hn, { onClick: I, className: "react-flow__controls-zoomout", title: S["controls.zoomOut.ariaLabel"], "aria-label": S["controls.zoomOut.ariaLabel"], disabled: w, children: u.jsx(Ig, {}) })] }), n && u.jsx(Hn, { className: "react-flow__controls-fitview", onClick: j, title: S["controls.fitView.ariaLabel"], "aria-label": S["controls.fitView.ariaLabel"], children: u.jsx(Ag, {}) }), o && u.jsx(Hn, { className: "react-flow__controls-interactive", onClick: D, title: S["controls.interactive.ariaLabel"], "aria-label": S["controls.interactive.ariaLabel"], children: x ? u.jsx(jg, {}) : u.jsx(Mg, {}) }), d] });
}
nc.displayName = "Controls";
const Pg = Se(nc);
function $g({ id: e, x: t, y: n, width: o, height: r, style: i, color: s, strokeColor: a, strokeWidth: l, className: c, borderRadius: d, shapeRendering: f, selected: h, onClick: y }) {
  const { background: g, backgroundColor: x } = i || {}, w = s || g || x;
  return u.jsx("rect", { className: Ee(["react-flow__minimap-node", { selected: h }, c]), x: t, y: n, rx: d, ry: d, width: o, height: r, style: {
    fill: w,
    stroke: a,
    strokeWidth: l
  }, shapeRendering: f, onClick: y ? (m) => y(m, e) : void 0 });
}
const Tg = Se($g), Rg = (e) => e.nodes.map((t) => t.id), or = (e) => e instanceof Function ? e : () => e;
function zg({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: i = Tg,
  onClick: s
}) {
  const a = ue(Rg, me), l = or(t), c = or(e), d = or(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return u.jsx(u.Fragment, { children: a.map((h) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    u.jsx(Hg, { id: h, nodeColorFunc: l, nodeStrokeColorFunc: c, nodeClassNameFunc: d, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: i, onClick: s, shapeRendering: f }, h)
  )) });
}
function Lg({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: i, shapeRendering: s, NodeComponent: a, onClick: l }) {
  const { node: c, x: d, y: f, width: h, height: y } = ue((g) => {
    const x = g.nodeLookup.get(e);
    if (!x)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const w = x.internals.userNode, { x: m, y: S } = x.internals.positionAbsolute, { width: p, height: v } = et(w);
    return {
      node: w,
      x: m,
      y: S,
      width: p,
      height: v
    };
  }, me);
  return !c || c.hidden || !ea(c) ? null : u.jsx(a, { x: d, y: f, width: h, height: y, style: c.style, selected: !!c.selected, className: o(c), color: t(c), borderRadius: r, strokeColor: n(c), strokeWidth: i, shapeRendering: s, onClick: l, id: c.id });
}
const Hg = Se(Lg);
var Vg = Se(zg);
const Og = 200, Bg = 150, Fg = (e) => !e.hidden, Wg = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? Qs(gn(e.nodeLookup, { filter: Fg }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, Yg = "react-flow__minimap-desc";
function oc({
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
  position: h = "bottom-right",
  onClick: y,
  onNodeClick: g,
  pannable: x = !1,
  zoomable: w = !1,
  ariaLabel: m,
  inversePan: S,
  zoomStep: p = 1,
  offsetScale: v = 5
}) {
  const E = xe(), N = ae(null), { boundingRect: I, viewBB: j, rfId: D, panZoom: H, translateExtent: A, flowWidth: R, flowHeight: V, ariaLabelConfig: b } = ue(Wg, me), C = e?.width ?? Og, _ = e?.height ?? Bg, M = I.width / C, P = I.height / _, $ = Math.max(M, P), O = $ * C, B = $ * _, F = v * $, Z = I.x - (O - I.width) / 2 - F, q = I.y - (B - I.height) / 2 - F, te = O + F * 2, ie = B + F * 2, G = `${Yg}-${D}`, z = ae(0), Y = ae();
  z.current = $, ce(() => {
    if (N.current && H)
      return Y.current = nh({
        domNode: N.current,
        panZoom: H,
        getTransform: () => E.getState().transform,
        getViewScale: () => z.current
      }), () => {
        Y.current?.destroy();
      };
  }, [H]), ce(() => {
    Y.current?.update({
      translateExtent: A,
      width: R,
      height: V,
      inversePan: S,
      pannable: x,
      zoomStep: p,
      zoomable: w
    });
  }, [x, w, S, p, A, R, V]);
  const re = y ? (J) => {
    const [se, L] = Y.current?.pointer(J) || [0, 0];
    y(J, { x: se, y: L });
  } : void 0, oe = g ? ge((J, se) => {
    const L = E.getState().nodeLookup.get(se).internals.userNode;
    g(J, L);
  }, []) : void 0, U = m ?? b["minimap.ariaLabel"];
  return u.jsx(vo, { position: h, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-background-color-props": typeof c == "string" ? c : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * $ : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof s == "number" ? s : void 0
  }, className: Ee(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: u.jsxs("svg", { width: C, height: _, viewBox: `${Z} ${q} ${te} ${ie}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": G, ref: N, onClick: re, children: [U && u.jsx("title", { id: G, children: U }), u.jsx(Vg, { onClick: oe, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: i, nodeClassName: r, nodeStrokeWidth: s, nodeComponent: a }), u.jsx("path", { className: "react-flow__minimap-mask", d: `M${Z - F},${q - F}h${te + F * 2}v${ie + F * 2}h${-te - F * 2}z
        M${j.x},${j.y}h${j.width}v${j.height}h${-j.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
oc.displayName = "MiniMap";
const Xg = Se(oc), qg = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, Zg = {
  [Mt.Line]: "right",
  [Mt.Handle]: "bottom-right"
};
function Ug({ nodeId: e, position: t, variant: n = Mt.Handle, className: o, style: r = void 0, children: i, color: s, minWidth: a = 10, minHeight: l = 10, maxWidth: c = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: h, autoScale: y = !0, shouldResize: g, onResizeStart: x, onResize: w, onResizeEnd: m }) {
  const S = Ta(), p = typeof e == "string" ? e : S, v = xe(), E = ae(null), N = n === Mt.Handle, I = ue(ge(qg(N && y), [N, y]), me), j = ae(null), D = t ?? Zg[n];
  ce(() => {
    if (!(!E.current || !p))
      return j.current || (j.current = gh({
        domNode: E.current,
        nodeId: p,
        getStoreItems: () => {
          const { nodeLookup: A, transform: R, snapGrid: V, snapToGrid: b, nodeOrigin: C, domNode: _ } = v.getState();
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
          const { triggerNodeChanges: V, nodeLookup: b, parentLookup: C, nodeOrigin: _ } = v.getState(), M = [], P = { x: A.x, y: A.y }, $ = b.get(p);
          if ($ && $.expandParent && $.parentId) {
            const O = $.origin ?? _, B = A.width ?? $.measured.width ?? 0, F = A.height ?? $.measured.height ?? 0, Z = {
              id: $.id,
              parentId: $.parentId,
              rect: {
                width: B,
                height: F,
                ...ta({
                  x: A.x ?? $.position.x,
                  y: A.y ?? $.position.y
                }, { width: B, height: F }, $.parentId, b, O)
              }
            }, q = Hr([Z], b, C, _);
            M.push(...q), P.x = A.x ? Math.max(O[0] * B, A.x) : void 0, P.y = A.y ? Math.max(O[1] * F, A.y) : void 0;
          }
          if (P.x !== void 0 && P.y !== void 0) {
            const O = {
              id: p,
              type: "position",
              position: { ...P }
            };
            M.push(O);
          }
          if (A.width !== void 0 && A.height !== void 0) {
            const B = {
              id: p,
              type: "dimensions",
              resizing: !0,
              setAttributes: h ? h === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: A.width,
                height: A.height
              }
            };
            M.push(B);
          }
          for (const O of R) {
            const B = {
              ...O,
              type: "position"
            };
            M.push(B);
          }
          V(M);
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
          v.getState().triggerNodeChanges([V]);
        }
      })), j.current.update({
        controlPosition: D,
        boundaries: {
          minWidth: a,
          minHeight: l,
          maxWidth: c,
          maxHeight: d
        },
        keepAspectRatio: f,
        resizeDirection: h,
        onResizeStart: x,
        onResize: w,
        onResizeEnd: m,
        shouldResize: g
      }), () => {
        j.current?.destroy();
      };
  }, [
    D,
    a,
    l,
    c,
    d,
    f,
    x,
    w,
    m,
    g
  ]);
  const H = D.split("-");
  return u.jsx("div", { className: Ee(["react-flow__resize-control", "nodrag", ...H, n, o]), ref: E, style: {
    ...r,
    scale: I,
    ...s && { [N ? "backgroundColor" : "borderColor"]: s }
  }, children: i });
}
Se(Ug);
const Kg = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), rc = (...e) => e.filter((t, n, o) => !!t && t.trim() !== "" && o.indexOf(t) === n).join(" ").trim();
var Gg = {
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
const Qg = lo(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: n = 2,
    absoluteStrokeWidth: o,
    className: r = "",
    children: i,
    iconNode: s,
    ...a
  }, l) => sr(
    "svg",
    {
      ref: l,
      ...Gg,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: o ? Number(n) * 24 / Number(t) : n,
      className: rc("lucide", r),
      ...a
    },
    [
      ...s.map(([c, d]) => sr(c, d)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const Me = (e, t) => {
  const n = lo(
    ({ className: o, ...r }, i) => sr(Qg, {
      ref: i,
      iconNode: t,
      className: rc(`lucide-${Kg(e)}`, o),
      ...r
    })
  );
  return n.displayName = `${e}`, n;
};
const Jg = Me("Boxes", [
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
const xn = Me("Check", [["path", { d: "M20 6 9 17l-5-5", key: "1gmf2c" }]]);
const ey = Me("ChevronDown", [
  ["path", { d: "m6 9 6 6 6-6", key: "qrunsl" }]
]);
const ty = Me("ChevronLeft", [
  ["path", { d: "m15 18-6-6 6-6", key: "1wnfg3" }]
]);
const Xn = Me("ChevronRight", [
  ["path", { d: "m9 18 6-6-6-6", key: "mthhwq" }]
]);
const Dt = Me("CircleAlert", [
  ["circle", { cx: "12", cy: "12", r: "10", key: "1mglay" }],
  ["line", { x1: "12", x2: "12", y1: "8", y2: "12", key: "1pkeuh" }],
  ["line", { x1: "12", x2: "12.01", y1: "16", y2: "16", key: "4dfq90" }]
]);
const ny = Me("GitBranch", [
  ["line", { x1: "6", x2: "6", y1: "3", y2: "15", key: "17qcm7" }],
  ["circle", { cx: "18", cy: "6", r: "3", key: "1h7g24" }],
  ["circle", { cx: "6", cy: "18", r: "3", key: "fqmcym" }],
  ["path", { d: "M18 9a9 9 0 0 1-9 9", key: "n2h4wq" }]
]);
const oy = Me("ListTree", [
  ["path", { d: "M21 12h-8", key: "1bmf0i" }],
  ["path", { d: "M21 6H8", key: "1pqkrb" }],
  ["path", { d: "M21 18h-8", key: "1tm79t" }],
  ["path", { d: "M3 6v4c0 1.1.9 2 2 2h3", key: "1ywdgy" }],
  ["path", { d: "M3 10v6c0 1.1.9 2 2 2h3", key: "2wc746" }]
]);
const ic = Me("Play", [
  ["polygon", { points: "6 3 20 12 6 21 6 3", key: "1oa8hb" }]
]);
const Br = Me("Plus", [
  ["path", { d: "M5 12h14", key: "1ays0h" }],
  ["path", { d: "M12 5v14", key: "s699le" }]
]);
const ry = Me("RotateCcw", [
  ["path", { d: "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "1357e3" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }]
]);
const iy = Me("Save", [
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
const sy = Me("Search", [
  ["circle", { cx: "11", cy: "11", r: "8", key: "4ej97u" }],
  ["path", { d: "m21 21-4.3-4.3", key: "1qie3q" }]
]);
const Pt = Me("Sparkles", [
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
const vr = Me("Trash2", [
  ["path", { d: "M3 6h18", key: "d0wm0j" }],
  ["path", { d: "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", key: "4alrt4" }],
  ["path", { d: "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", key: "v07s0e" }],
  ["line", { x1: "10", x2: "10", y1: "11", y2: "17", key: "1uufr5" }],
  ["line", { x1: "14", x2: "14", y1: "11", y2: "17", key: "xtxkd" }]
]), He = "/_elsa/workflow-management", ay = "/_elsa/publishing";
async function cy(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${He}/definitions?${n.toString()}`);
}
async function ly(e, t) {
  return e.http.getJson(`${He}/definitions/${encodeURIComponent(t)}`);
}
async function uy(e, t) {
  return e.http.postJson(`${He}/definitions`, t);
}
async function dy(e, t) {
  await e.http.deleteJson(`${He}/definitions/${encodeURIComponent(t)}`);
}
async function fy(e, t) {
  await e.http.postJson(`${He}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function hy(e, t) {
  await e.http.deleteJson(`${He}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function py(e, t) {
  return e.http.putJson(`${He}/drafts/${encodeURIComponent(t.id)}`, { state: t.state, layout: t.layout });
}
async function gy(e, t) {
  return e.http.postJson(`${He}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function yy(e, t) {
  return e.http.postJson(`${He}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function my(e, t) {
  try {
    return await e.http.postJson(`${ay}/workflows/drafts/test-runs`, t);
  } catch (n) {
    const o = Sy(n);
    if (o) return o;
    throw n;
  }
}
async function xy(e, t) {
  return e.http.postJson(`${He}/executables/${encodeURIComponent(t)}/run`, {});
}
async function wy(e) {
  return e.http.getJson("/_demo/workflows/executables");
}
async function sc(e) {
  return e.http.getJson(`${He}/activities`);
}
async function vy(e) {
  const t = await ac(e, [
    `${He}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? is(t) : is(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function by(e) {
  const t = await ac(e, [
    `${He}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : qn;
}
async function ac(e, t) {
  let n;
  for (const o of t)
    try {
      return await e.http.getJson(o);
    } catch (r) {
      n = r;
    }
  throw n;
}
function is(e) {
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
function Sy(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = ss(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return ss(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function ss(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const qn = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
], So = "elsa.sequence.structure", wn = "elsa.flowchart.structure";
function cc(e, t) {
  if (!e) return null;
  let n = e;
  for (const o of t) {
    const r = Ue(n).find((s) => s.id === o.slotId);
    if (!r) return null;
    const i = r.activities.find((s) => s.nodeId === o.ownerNodeId);
    if (!i) return null;
    n = i;
  }
  return n;
}
function rr(e, t) {
  const n = cc(e, t);
  if (!n) return null;
  let o = Ue(n)[0];
  return o ? { owner: n, slot: o } : null;
}
function Ue(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = By(t), r = ir(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: Fy(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, i]) => ir(i)).map(([i, s]) => ({
    id: `${t.kind}:${i}`,
    label: Yy(i),
    property: i,
    mode: "generic",
    activities: ir(s) ?? []
  }));
}
function Ny(e, t, n) {
  const o = new Map(t.map((s) => [s.activityVersionId, s])), r = new Map(n.map((s) => [s.nodeId, s])), i = e.slot.activities.map((s, a) => {
    const l = o.get(s.activityVersionId), c = r.get(s.nodeId) ?? Wy(e.slot.mode, a);
    return dc(s, l, { x: c.x, y: c.y });
  });
  return {
    nodes: i,
    edges: e.slot.mode === "flowchart" ? Ty(e.owner) : $y(e.slot, i)
  };
}
function Ey(e, t, n) {
  const o = t.find((i) => i.activityVersionId === e.activityVersionId), r = n.find((i) => i.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [dc(e, o, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function _y(e, t) {
  return e?.structure?.kind === wn || Ay(t) ? "flowchart" : e?.structure?.kind === So || My(t) ? "sequence" : "unsupported";
}
function br(e, t, n) {
  if (t.length === 0) {
    const a = Ue(e)[0];
    return a ? un(e, a, n) : e;
  }
  const [o, ...r] = t, i = Ue(e).find((a) => a.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((a) => a.nodeId === o.ownerNodeId ? br(a, r, n) : a);
  return un(e, i, s);
}
function lc(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, i = Ue(e).find((a) => a.id === o.slotId);
  if (!i) return e;
  const s = i.activities.map((a) => a.nodeId === o.ownerNodeId ? lc(a, r, n) : a);
  return un(e, i, s);
}
function uc(e, t, n) {
  if (e.nodeId === t) return n(e);
  const o = Ue(e);
  if (o.length === 0) return e;
  let r = !1, i = e;
  for (const s of o) {
    const a = s.activities.map((l) => {
      const c = uc(l, t, n);
      return c !== l && (r = !0), c;
    });
    r && (i = un(i, s, a));
  }
  return r ? i : e;
}
function un(e, t, n) {
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
function Cy(e, t, n, o = []) {
  const r = new Map(e.slot.activities.map((s) => [s.nodeId, s]));
  for (const s of o)
    r.set(s.nodeId, s);
  const i = t.map((s) => r.get(s.id)).filter((s) => !!s);
  return e.slot.mode === "sequence" && i.sort((s, a) => {
    const l = t.find((d) => d.id === s.nodeId), c = t.find((d) => d.id === a.nodeId);
    return (l?.position.x ?? 0) - (c?.position.x ?? 0);
  }), un(e.owner, e.slot, i);
}
function ky(e, t) {
  return {
    ...e,
    structure: Py(e.structure, t)
  };
}
function Iy(e, t) {
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
function as(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: Dy(e)
  };
}
function Ce(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? jy(t) : n;
}
function dc(e, t, n, o = {}) {
  return {
    id: e.nodeId,
    type: "workflowActivity",
    position: n,
    connectable: o.connectable,
    deletable: o.deletable,
    draggable: o.draggable,
    data: {
      label: t ? Ce(t) : e.activityVersionId,
      activityVersionId: e.activityVersionId,
      activityTypeKey: t?.activityTypeKey,
      childSlots: Ue(e),
      acceptsInbound: Ry(e, t),
      sourcePorts: o.suppressFlowPorts ? [] : fc(e, t),
      suppressFlowPorts: o.suppressFlowPorts
    }
  };
}
function Ay(e) {
  return !!e && (Ce(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function My(e) {
  return !!e && (Ce(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function jy(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function Dy(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: So,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: wn,
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
function Py(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], o = /* @__PURE__ */ new Map();
  for (const r of n) {
    if (!Fr(r)) continue;
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
function $y(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function Ty(e) {
  if (e.structure?.kind !== wn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, i = n.target;
    if (!r?.nodeId || !i?.nodeId) return null;
    const s = Array.isArray(n.vertices) ? n.vertices.filter(Vy) : [];
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
function fc(e, t) {
  const n = cs(e.cases);
  if (Ly(e, t) && n.length > 0)
    return [...n.map((i) => ({ name: i, displayName: i })), { name: "Default", displayName: "Default" }];
  const o = [
    ...Zn(t?.designFacets),
    ...Zn(t?.ports),
    ...Zn(t?.outputs)
  ];
  if (o.length > 0) return Hy(o);
  const r = cs(e.outcomes);
  return r.length > 0 ? r.map((i) => ({ name: i, displayName: i })) : [{ name: "Done", displayName: "Done" }];
}
function Ry(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function so(e, t, n, o) {
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
function zy(e, t, n) {
  const o = so(t.source, n, t.sourceHandle ?? "Done", void 0), r = so(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((i) => i.id !== t.id).concat(o, r);
}
function ir(e) {
  return Array.isArray(e) ? e.filter(Oy) : null;
}
function Ly(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function Zn(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!Fr(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...Zn(n.ports));
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
function Hy(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function cs(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function Vy(e) {
  return Fr(e) && typeof e.x == "number" && typeof e.y == "number";
}
function Fr(e) {
  return typeof e == "object" && e !== null;
}
function Oy(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function By(e) {
  return e.kind === So ? "sequence" : e.kind === wn ? "flowchart" : "generic";
}
function Fy(e) {
  return e.kind === So || e.kind === wn, "Activities";
}
function Wy(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function Yy(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Xy = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function qy(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function Wr(e) {
  return qy(e.name);
}
function Zy(e, t) {
  const n = Wr(t), o = e[n];
  return t.isWrapped === !1 ? o ?? t.defaultValue ?? "" : pc(o, t);
}
function hc(e, t) {
  return pc(e[Wr(t)], t);
}
function Uy(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function Ky(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function ls(e, t, n) {
  return {
    ...e,
    [Wr(t)]: n
  };
}
function Gy(e, t) {
  return t.isWrapped === !1 ? Zy(e, t) : hc(e, t).expression.value;
}
function pc(e, t) {
  return Qy(e) ? {
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
function Qy(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
function Jy({
  activity: e,
  descriptor: t,
  editors: n,
  expressionDescriptors: o,
  descriptorStatus: r,
  onChange: i
}) {
  if (r === "loading")
    return /* @__PURE__ */ u.jsx("p", { className: "wf-muted", children: "Loading activity properties..." });
  if (!t)
    return /* @__PURE__ */ u.jsx("p", { className: "wf-muted", children: "No activity descriptor is available for this activity." });
  const s = t.inputs.filter((c) => c.isBrowsable !== !1).sort((c, d) => (c.order ?? 0) - (d.order ?? 0) || c.name.localeCompare(d.name));
  if (s.length === 0)
    return /* @__PURE__ */ u.jsx("p", { className: "wf-muted", children: "This activity does not expose editable properties." });
  const a = om(s), l = o.length > 0 ? o : Xy;
  return /* @__PURE__ */ u.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ u.jsx("span", { className: "wf-section-label", children: "Properties" }),
    a.map((c) => /* @__PURE__ */ u.jsxs("section", { className: "wf-property-group", children: [
      a.length > 1 ? /* @__PURE__ */ u.jsx("h4", { children: c.category }) : null,
      c.inputs.map((d) => /* @__PURE__ */ u.jsx(
        em,
        {
          activity: e,
          input: d,
          editors: n,
          expressionDescriptors: l,
          onChange: i
        },
        d.name
      ))
    ] }, c.category))
  ] });
}
function em({
  activity: e,
  input: t,
  editors: n,
  expressionDescriptors: o,
  onChange: r
}) {
  const i = t.isReadOnly === !0, s = { activity: e, expressionDescriptors: o, readOnly: i }, l = nm(n, t, s)?.component, c = t.isWrapped !== !1 ? hc(e, t) : null, d = c?.expression.type ?? "Literal", f = Gy(e, t), h = (g) => {
    const x = c ? Uy(c, g) : g;
    r(ls(e, t, x));
  }, y = (g) => {
    c && r(ls(e, t, Ky(c, g)));
  };
  return /* @__PURE__ */ u.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ u.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ u.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ u.jsx("span", { children: rm(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ u.jsx("p", { children: t.description }) : null,
    c ? /* @__PURE__ */ u.jsx(
      tm,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: d,
        descriptors: o,
        disabled: i,
        onChange: y
      }
    ) : null,
    l ? /* @__PURE__ */ u.jsx(
      l,
      {
        descriptor: t,
        value: f,
        disabled: i,
        context: s,
        onChange: h
      }
    ) : /* @__PURE__ */ u.jsx("input", { type: "text", value: f == null ? "" : String(f), disabled: i, onChange: (g) => h(g.target.value) })
  ] });
}
function tm({
  label: e,
  value: t,
  descriptors: n,
  disabled: o,
  onChange: r
}) {
  const [i, s] = ee(!1), a = Tc(), l = n.find((c) => c.type === t);
  return /* @__PURE__ */ u.jsxs("div", { className: "wf-syntax-picker", onBlur: (c) => {
    c.currentTarget.contains(c.relatedTarget) || s(!1);
  }, children: [
    /* @__PURE__ */ u.jsx(
      "button",
      {
        type: "button",
        className: i ? "wf-syntax-picker-trigger open" : "wf-syntax-picker-trigger",
        "aria-label": e,
        "aria-haspopup": "listbox",
        "aria-expanded": i,
        "aria-controls": a,
        disabled: o,
        onClick: () => s((c) => !c),
        children: /* @__PURE__ */ u.jsx("span", { children: l?.displayName || l?.type || t })
      }
    ),
    i ? /* @__PURE__ */ u.jsx("div", { id: a, role: "listbox", className: "wf-syntax-picker-menu", "aria-label": e, children: n.map((c) => {
      const d = c.displayName || c.type, f = c.type === t;
      return /* @__PURE__ */ u.jsx(
        "button",
        {
          type: "button",
          role: "option",
          "aria-selected": f,
          className: f ? "selected" : "",
          onClick: () => {
            r(c.type), s(!1);
          },
          children: d
        },
        c.type
      );
    }) }) : null
  ] });
}
function nm(e, t, n) {
  return [...e].sort((o, r) => (o.order ?? 500) - (r.order ?? 500)).find((o) => o.supports(t, n));
}
function om(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "General";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return [...t.entries()].map(([n, o]) => ({ category: n, inputs: o }));
}
function rm(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
const im = { workflowActivity: Mm }, sm = { workflow: jm }, us = "application/x-elsa-activity-version-id", am = 6, cm = 1200, lm = [10, 25, 50], um = 10, gc = st.createContext(null);
function Om(e) {
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
        component: () => /* @__PURE__ */ u.jsx(dm, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list() })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ u.jsx(fm, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow instances",
        component: () => /* @__PURE__ */ u.jsx(hm, { ai: e.ai })
      }
    ]
  });
}
function dm({
  context: e,
  ai: t,
  propertyEditors: n
}) {
  const [o, r] = ee(ds);
  ce(() => {
    const s = () => r(ds());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const i = (s) => {
    const a = s ? `/workflows/definitions?definition=${encodeURIComponent(s)}` : "/workflows/definitions";
    window.history.pushState({}, "", a), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return o ? /* @__PURE__ */ u.jsx(Am, { context: e, definitionId: o, ai: t, propertyEditors: n, onBack: () => i(null) }) : /* @__PURE__ */ u.jsx(Yr, { activePath: "/workflows/definitions", title: "Definitions", children: /* @__PURE__ */ u.jsx(pm, { context: e, ai: t, onOpen: i }) });
}
function fm({ context: e, ai: t }) {
  const [n, o] = ee(fs);
  return ce(() => {
    const r = () => o(fs());
    return window.addEventListener("popstate", r), () => window.removeEventListener("popstate", r);
  }, []), /* @__PURE__ */ u.jsx(Yr, { activePath: "/workflows/executables", title: "Executables", children: /* @__PURE__ */ u.jsx(ym, { context: e, ai: t, definitionFilter: n }) });
}
function hm({ ai: e }) {
  const t = $t(e, "weaver.workflows.explain-instance");
  return /* @__PURE__ */ u.jsx(Yr, { activePath: "/workflows/instances", title: "Instances", children: /* @__PURE__ */ u.jsxs("div", { className: "wf-empty", children: [
    "Workflow instance history will appear here when the runtime exposes an instance query endpoint.",
    t ? /* @__PURE__ */ u.jsxs("button", { type: "button", className: "wf-ai-inline-action", onClick: () => Tt(e, t, { scope: "workflow-instances" }), children: [
      /* @__PURE__ */ u.jsx(Pt, { size: 13 }),
      " Ask Weaver about instances"
    ] }) : null
  ] }) });
}
function Yr({ activePath: e, title: t, children: n }) {
  const o = (r) => {
    window.history.pushState({}, "", r), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ u.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ u.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ u.jsxs("div", { children: [
      /* @__PURE__ */ u.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ u.jsx("h2", { children: t })
    ] }) }),
    /* @__PURE__ */ u.jsxs("nav", { className: "wf-section-tabs", "aria-label": "Workflow views", children: [
      /* @__PURE__ */ u.jsx("a", { className: e === "/workflows/definitions" ? "active" : "", href: "/workflows/definitions", onClick: (r) => {
        r.preventDefault(), o("/workflows/definitions");
      }, children: "Definitions" }),
      /* @__PURE__ */ u.jsx("a", { className: e === "/workflows/executables" ? "active" : "", href: "/workflows/executables", onClick: (r) => {
        r.preventDefault(), o("/workflows/executables");
      }, children: "Executables" }),
      /* @__PURE__ */ u.jsx("a", { className: e === "/workflows/instances" ? "active" : "", href: "/workflows/instances", onClick: (r) => {
        r.preventDefault(), o("/workflows/instances");
      }, children: "Instances" })
    ] }),
    n
  ] });
}
function ds() {
  return new URLSearchParams(window.location.search).get("definition");
}
function fs() {
  return new URLSearchParams(window.location.search).get("definition");
}
function pm({ context: e, ai: t, onOpen: n }) {
  const [o, r] = ee(""), [i, s] = ee("active"), [a, l] = ee(1), [c, d] = ee(um), [f, h] = ee("loading"), [y, g] = ee(""), [x, w] = ee(""), [m, S] = ee([]), [p, v] = ee(0), [E, N] = ee(() => /* @__PURE__ */ new Set()), [I, j] = ee(null), [D, H] = ee(!1), [A, R] = ee([]), [V, b] = ee("idle"), C = ae(null), _ = ye(() => m.map((L) => L.id), [m]), M = $t(t, "weaver.workflows.suggest-create-metadata"), P = $t(t, "weaver.workflows.explain-definition"), $ = _.filter((L) => E.has(L)).length, O = _.length > 0 && $ === _.length, B = ge(async () => {
    h("loading"), g("");
    try {
      const L = await cy(e, { search: o, state: i, page: a, pageSize: c }), K = typeof L.totalCount == "number", he = L.totalCount ?? L.definitions.length, we = yc(he, c);
      if (he > 0 && a > we) {
        l(we);
        return;
      }
      S(K ? L.definitions : xm(L.definitions, a, c)), v(he), h("ready");
    } catch (L) {
      g(L instanceof Error ? L.message : String(L)), h("failed");
    }
  }, [e, o, i, a, c]);
  ce(() => {
    B();
  }, [B]), ce(() => {
    C.current && (C.current.indeterminate = $ > 0 && !O);
  }, [O, $]);
  const F = ge(async () => {
    if (!(V === "loading" || V === "ready")) {
      b("loading");
      try {
        const L = await sc(e);
        R(L.activities ?? []), b("ready");
      } catch (L) {
        b("failed"), g(L instanceof Error ? L.message : String(L));
      }
    }
  }, [V, e]), Z = () => {
    g(""), w(""), j({ name: "", description: "", rootKind: "flowchart" }), F();
  }, q = async () => {
    if (I?.name.trim()) {
      H(!0), g(""), w("");
      try {
        const L = await uy(e, {
          name: I.name.trim(),
          description: I.description.trim() || null,
          rootKind: I.rootKind,
          rootActivityVersionId: bm(I, A)
        });
        j(null), n(L.definition.id);
      } catch (L) {
        g(L instanceof Error ? L.message : String(L));
      } finally {
        H(!1);
      }
    }
  }, te = (L) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(L)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, ie = async () => {
    if (m.length === 1 && a > 1) {
      l(a - 1);
      return;
    }
    await B();
  }, G = () => N(/* @__PURE__ */ new Set()), z = (L, K) => {
    N((he) => {
      const we = new Set(he);
      return K ? we.add(L) : we.delete(L), we;
    });
  }, Y = (L) => {
    N((K) => {
      const he = new Set(K);
      for (const we of _)
        L ? he.add(we) : he.delete(we);
      return he;
    });
  }, re = (L) => {
    s(L), l(1), G();
  }, oe = (L) => {
    r(L), l(1), G();
  }, U = async (L) => {
    if (window.confirm(`Delete workflow definition "${L.name}"? You can restore it from the Deleted view.`)) {
      w(""), g("");
      try {
        await dy(e, L.id), z(L.id, !1), w(`Deleted ${L.name}`), await ie();
      } catch (K) {
        g(K instanceof Error ? K.message : String(K));
      }
    }
  }, J = async (L) => {
    w(""), g("");
    try {
      await fy(e, L.id), z(L.id, !1), w(`Restored ${L.name}`), await ie();
    } catch (K) {
      g(K instanceof Error ? K.message : String(K));
    }
  }, se = async (L) => {
    if (window.confirm(`Permanently delete workflow definition "${L.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`)) {
      w(""), g("");
      try {
        await hy(e, L.id), z(L.id, !1), w(`Permanently deleted ${L.name}`), await ie();
      } catch (K) {
        g(K instanceof Error ? K.message : String(K));
      }
    }
  };
  return /* @__PURE__ */ u.jsxs(u.Fragment, { children: [
    /* @__PURE__ */ u.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ u.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ u.jsx("button", { type: "button", className: i === "active" ? "active" : "", "aria-selected": i === "active", onClick: () => re("active"), children: "Active" }),
        /* @__PURE__ */ u.jsx("button", { type: "button", className: i === "deleted" ? "active" : "", "aria-selected": i === "deleted", onClick: () => re("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ u.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ u.jsx(sy, { size: 15 }),
        /* @__PURE__ */ u.jsx("input", { value: o, onChange: (L) => oe(L.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ u.jsx("button", { type: "button", onClick: () => {
        B();
      }, children: "Refresh" }),
      /* @__PURE__ */ u.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ u.jsxs("button", { type: "button", title: "Create workflow", onClick: Z, children: [
        /* @__PURE__ */ u.jsx(Br, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ u.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ u.jsx(Dt, { size: 16 }),
      " ",
      y
    ] }) : null,
    f !== "failed" && y ? /* @__PURE__ */ u.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ u.jsx(Dt, { size: 16 }),
      " ",
      y
    ] }) : null,
    x ? /* @__PURE__ */ u.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ u.jsx(xn, { size: 14 }),
      " ",
      x
    ] }) : null,
    E.size > 0 ? /* @__PURE__ */ u.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ u.jsxs("span", { children: [
        E.size,
        " selected"
      ] }),
      /* @__PURE__ */ u.jsx("button", { type: "button", onClick: G, children: "Clear selection" })
    ] }) : null,
    f === "loading" ? /* @__PURE__ */ u.jsx("div", { className: "wf-empty", children: "Loading workflow definitions..." }) : null,
    f === "ready" && m.length === 0 ? /* @__PURE__ */ u.jsxs("div", { className: "wf-empty", children: [
      "No ",
      i,
      " workflow definitions found."
    ] }) : null,
    f === "ready" && m.length > 0 ? /* @__PURE__ */ u.jsxs(u.Fragment, { children: [
      /* @__PURE__ */ u.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ u.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ u.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ u.jsx(
            "input",
            {
              ref: C,
              type: "checkbox",
              checked: O,
              onChange: (L) => Y(L.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ u.jsx("span", { children: "Name" }),
          /* @__PURE__ */ u.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ u.jsx("span", { children: i === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ u.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ u.jsx("span", { children: "Actions" })
        ] }),
        m.map((L) => /* @__PURE__ */ u.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${L.name}`,
            "aria-selected": E.has(L.id),
            tabIndex: 0,
            onClick: () => n(L.id),
            onKeyDown: (K) => {
              K.currentTarget === K.target && (K.key !== "Enter" && K.key !== " " || (K.preventDefault(), n(L.id)));
            },
            children: [
              /* @__PURE__ */ u.jsx("label", { className: "wf-row-select", onClick: (K) => K.stopPropagation(), children: /* @__PURE__ */ u.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: E.has(L.id),
                  onChange: (K) => z(L.id, K.target.checked),
                  "aria-label": `Select workflow definition ${L.name}`
                }
              ) }),
              /* @__PURE__ */ u.jsxs("span", { children: [
                /* @__PURE__ */ u.jsx("strong", { children: L.name }),
                /* @__PURE__ */ u.jsx("small", { children: L.description || L.id })
              ] }),
              /* @__PURE__ */ u.jsx("span", { children: L.latestVersion ?? "No version" }),
              /* @__PURE__ */ u.jsx("span", { children: i === "deleted" ? co(L.deletedAt) : L.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ u.jsx("span", { children: co(L.lastModifiedAt) }),
              /* @__PURE__ */ u.jsx("span", { className: "wf-row-actions", onClick: (K) => K.stopPropagation(), children: i === "active" ? /* @__PURE__ */ u.jsxs(u.Fragment, { children: [
                /* @__PURE__ */ u.jsx("button", { type: "button", onClick: (K) => {
                  K.stopPropagation(), n(L.id);
                }, children: "Open" }),
                /* @__PURE__ */ u.jsx("button", { type: "button", onClick: (K) => {
                  K.stopPropagation(), te(L.id);
                }, children: "Artifacts" }),
                P ? /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => Tt(t, P, L), children: [
                  /* @__PURE__ */ u.jsx(Pt, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ u.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  U(L);
                }, children: [
                  /* @__PURE__ */ u.jsx(vr, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ u.jsxs(u.Fragment, { children: [
                /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => {
                  J(L);
                }, children: [
                  /* @__PURE__ */ u.jsx(ry, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ u.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  se(L);
                }, children: [
                  /* @__PURE__ */ u.jsx(vr, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          L.id
        ))
      ] }),
      /* @__PURE__ */ u.jsx(
        mm,
        {
          page: a,
          pageSize: c,
          totalCount: p,
          onPageChange: l,
          onPageSizeChange: (L) => {
            d(L), l(1);
          }
        }
      )
    ] }) : null,
    I ? /* @__PURE__ */ u.jsx(
      gm,
      {
        draft: I,
        activities: A,
        catalogState: V,
        creating: D,
        suggestMetadataAction: M,
        onSuggestMetadata: M ? () => Tt(t, M, { draft: I, activities: A }) : void 0,
        onChange: (L) => j(L),
        onClose: () => j(null),
        onSubmit: q
      }
    ) : null
  ] });
}
function gm({ draft: e, activities: t, catalogState: n, creating: o, suggestMetadataAction: r, onSuggestMetadata: i, onChange: s, onClose: a, onSubmit: l }) {
  const c = ye(() => wm(t), [t]), d = vm(e, t), f = (h) => {
    if (h.startsWith("kind:")) {
      s({ ...e, rootKind: h.slice(5), rootActivityVersionId: null });
      return;
    }
    const y = t.find((g) => g.activityVersionId === h);
    s({
      ...e,
      rootKind: mc(y) ?? e.rootKind,
      rootActivityVersionId: h
    });
  };
  return /* @__PURE__ */ u.jsx("div", { className: "wf-dialog-backdrop", role: "presentation", children: /* @__PURE__ */ u.jsx("section", { className: "wf-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "workflow-create-title", children: /* @__PURE__ */ u.jsxs(
    "form",
    {
      onSubmit: (h) => {
        h.preventDefault(), l();
      },
      children: [
        /* @__PURE__ */ u.jsxs("div", { className: "wf-dialog-heading", children: [
          /* @__PURE__ */ u.jsx("h3", { id: "workflow-create-title", children: "Create Workflow" }),
          r ? /* @__PURE__ */ u.jsxs("button", { type: "button", className: "wf-ai-action", onClick: i, title: r.description ?? r.label, children: [
            /* @__PURE__ */ u.jsx(Pt, { size: 13 }),
            " ",
            r.label
          ] }) : null
        ] }),
        /* @__PURE__ */ u.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ u.jsx("span", { children: "Display name" }),
          /* @__PURE__ */ u.jsx(
            "input",
            {
              autoFocus: !0,
              "aria-label": "Display name",
              value: e.name,
              onChange: (h) => s({ ...e, name: h.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ u.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ u.jsx("span", { children: "Description" }),
          /* @__PURE__ */ u.jsx(
            "textarea",
            {
              "aria-label": "Description",
              rows: 3,
              value: e.description,
              onChange: (h) => s({ ...e, description: h.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ u.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ u.jsx("span", { children: "Root activity" }),
          /* @__PURE__ */ u.jsxs(
            "select",
            {
              "aria-label": "Root activity",
              value: d,
              onChange: (h) => f(h.target.value),
              disabled: n === "loading",
              children: [
                /* @__PURE__ */ u.jsx("optgroup", { label: "Composite roots", children: c.compositeRoots.map((h) => /* @__PURE__ */ u.jsx("option", { value: h.value, children: h.label }, h.value)) }),
                c.otherCategories.map((h) => /* @__PURE__ */ u.jsx("optgroup", { label: h.name, children: h.activities.map((y) => /* @__PURE__ */ u.jsx("option", { value: y.activityVersionId, children: Ce(y) }, y.activityVersionId)) }, h.name))
              ]
            }
          )
        ] }),
        n === "loading" ? /* @__PURE__ */ u.jsx("div", { className: "wf-dialog-note", children: "Loading activity catalog..." }) : null,
        n === "failed" ? /* @__PURE__ */ u.jsx("div", { className: "wf-dialog-note", children: "Activity catalog could not be loaded. Composite roots remain available." }) : null,
        /* @__PURE__ */ u.jsxs("div", { className: "wf-dialog-actions", children: [
          /* @__PURE__ */ u.jsx("button", { type: "button", onClick: a, disabled: o, children: "Cancel" }),
          /* @__PURE__ */ u.jsx("button", { type: "submit", disabled: o || !e.name.trim(), children: o ? "Creating..." : "Create" })
        ] })
      ]
    }
  ) }) });
}
function ym({ context: e, ai: t, definitionFilter: n }) {
  const [o, r] = ee("loading"), [i, s] = ee(""), [a, l] = ee(""), [c, d] = ee([]), f = ye(
    () => n ? c.filter((x) => x.definitionId === n || x.sourceId === n) : c,
    [n, c]
  ), h = $t(t, "weaver.workflows.explain-executable"), y = ge(async () => {
    r("loading"), s("");
    try {
      d(await wy(e)), r("ready");
    } catch (x) {
      s(x instanceof Error ? x.message : String(x)), r("failed");
    }
  }, [e]);
  ce(() => {
    y();
  }, [y]);
  const g = async (x) => {
    l(""), s("");
    try {
      await xy(e, x.artifactId), l(`Started ${x.artifactId}`);
    } catch (w) {
      s(w instanceof Error ? w.message : String(w));
    }
  };
  return /* @__PURE__ */ u.jsxs(u.Fragment, { children: [
    /* @__PURE__ */ u.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ u.jsx("button", { type: "button", onClick: () => {
        y();
      }, children: "Refresh" }),
      n ? /* @__PURE__ */ u.jsxs("span", { className: "wf-filter-chip", children: [
        "Definition ",
        n
      ] }) : null
    ] }),
    o === "failed" ? /* @__PURE__ */ u.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ u.jsx(Dt, { size: 16 }),
      " ",
      i
    ] }) : null,
    a ? /* @__PURE__ */ u.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ u.jsx(xn, { size: 14 }),
      " ",
      a
    ] }) : null,
    o === "loading" ? /* @__PURE__ */ u.jsx("div", { className: "wf-empty", children: "Loading workflow executables..." }) : null,
    o === "ready" && f.length === 0 ? /* @__PURE__ */ u.jsx("div", { className: "wf-empty", children: n ? "No workflow executables found for this definition." : "No workflow executables found. Publish a workflow definition to create one." }) : null,
    o === "ready" && f.length > 0 ? /* @__PURE__ */ u.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ u.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ u.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ u.jsx("span", { children: "Version" }),
        /* @__PURE__ */ u.jsx("span", { children: "Source" }),
        /* @__PURE__ */ u.jsx("span", { children: "Root" }),
        /* @__PURE__ */ u.jsx("span", { children: "Published" }),
        /* @__PURE__ */ u.jsx("span", { children: "Actions" })
      ] }),
      f.map((x) => /* @__PURE__ */ u.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ u.jsxs("span", { children: [
          /* @__PURE__ */ u.jsx("strong", { children: x.artifactId }),
          /* @__PURE__ */ u.jsx("small", { children: x.artifactHash })
        ] }),
        /* @__PURE__ */ u.jsx("span", { children: x.artifactVersion }),
        /* @__PURE__ */ u.jsx("span", { children: Nm(x) }),
        /* @__PURE__ */ u.jsx("span", { children: Em(x) }),
        /* @__PURE__ */ u.jsx("span", { children: co(x.publishedAt ?? x.createdAt) }),
        /* @__PURE__ */ u.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => {
            g(x);
          }, children: [
            /* @__PURE__ */ u.jsx(ic, { size: 13 }),
            " Run"
          ] }),
          h ? /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => Tt(t, h, x), children: [
            /* @__PURE__ */ u.jsx(Pt, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, x.artifactId))
    ] }) : null
  ] });
}
function mm({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const i = yc(n, t), s = n === 0 ? 0 : (e - 1) * t + 1, a = Math.min(e * t, n);
  return /* @__PURE__ */ u.jsxs("div", { className: "wf-pagination", "aria-label": "Workflow definition pagination", children: [
    /* @__PURE__ */ u.jsxs("span", { className: "wf-pagination-summary", "aria-live": "polite", children: [
      "Showing ",
      s,
      "-",
      a,
      " of ",
      n
    ] }),
    /* @__PURE__ */ u.jsxs("label", { className: "wf-page-size", children: [
      "Rows",
      /* @__PURE__ */ u.jsx("select", { value: t, onChange: (l) => r(Number(l.target.value)), children: lm.map((l) => /* @__PURE__ */ u.jsx("option", { value: l, children: l }, l)) })
    ] }),
    /* @__PURE__ */ u.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ u.jsx(ty, { size: 14 }),
        " Previous"
      ] }),
      /* @__PURE__ */ u.jsxs("span", { children: [
        "Page ",
        e,
        " of ",
        i
      ] }),
      /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => o(e + 1), disabled: e >= i, "aria-label": "Next page", title: "Next page", children: [
        "Next ",
        /* @__PURE__ */ u.jsx(Xn, { size: 14 })
      ] })
    ] })
  ] });
}
function xm(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function yc(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function $t(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function Tt(e, t, n) {
  const o = t.createPrompt(n);
  o && e.dispatchPrompt(o);
}
function wm(e) {
  const t = ao(e, "flowchart"), n = ao(e, "sequence"), o = [
    { value: t?.activityVersionId ?? "kind:flowchart", label: "Flowchart" },
    { value: n?.activityVersionId ?? "kind:sequence", label: "Sequence" }
  ], r = /* @__PURE__ */ new Map();
  for (const s of e.filter(bc)) {
    if (Sm(s)) continue;
    const a = s.category || "Uncategorized";
    r.set(a, [...r.get(a) ?? [], s]);
  }
  const i = Array.from(r.entries()).sort(([s], [a]) => s.localeCompare(a)).map(([s, a]) => ({
    name: s,
    activities: a.sort((l, c) => Ce(l).localeCompare(Ce(c)))
  }));
  return { compositeRoots: o, otherCategories: i };
}
function vm(e, t) {
  return e.rootActivityVersionId ?? ao(t, e.rootKind)?.activityVersionId ?? `kind:${e.rootKind}`;
}
function bm(e, t) {
  return e.rootActivityVersionId ?? ao(t, e.rootKind)?.activityVersionId ?? null;
}
function ao(e, t) {
  return e.find((n) => mc(n) === t);
}
function mc(e) {
  return e ? wc(e) ? "flowchart" : vc(e) ? "sequence" : null : null;
}
function xc(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "Uncategorized";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [o]) => n.localeCompare(o)).map(([n, o]) => ({
    category: n,
    activities: o.sort((r, i) => Ce(r).localeCompare(Ce(i)))
  }));
}
function Sm(e) {
  return wc(e) || vc(e);
}
function wc(e) {
  return Ce(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function vc(e) {
  return Ce(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function bc(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function Nm(e) {
  return e.sourceKind || e.sourceId || e.sourceVersion ? [e.sourceKind, e.sourceId, e.sourceVersion].filter(Boolean).join(" / ") : e.definitionId;
}
function Em(e) {
  return _m(e.rootActivityType) || e.rootActivityType;
}
function _m(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function Cm(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    Vn(t, n.typeName, n), Vn(t, n.name, n), Vn(t, n.displayName, n);
    const o = n.typeName.split(".").filter(Boolean).at(-1);
    Vn(t, o, n);
  }
  return t;
}
function km(e, t, n) {
  const o = t.get(e.activityVersionId);
  return n.get(Gt(o?.activityTypeKey)) ?? n.get(Gt(Im(o?.activityTypeKey))) ?? n.get(Gt(o?.displayName)) ?? n.get(Gt(e.activityVersionId)) ?? null;
}
function Vn(e, t, n) {
  const o = Gt(t);
  o && !e.has(o) && e.set(o, n);
}
function Gt(e) {
  return e?.trim().toLowerCase() ?? "";
}
function Im(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function Am({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: o,
  onBack: r
}) {
  const [i, s] = ee(null), [a, l] = ee(null), [c, d] = ee([]), [f, h] = ee([]), [y, g] = ee(qn), [x, w] = ee("loading"), [m, S] = ee([]), [p, v] = ee([]), [E, N] = ee([]), [I, j] = ee(null), [D, H] = ee(null), [A, R] = ee(null), [V, b] = ee(null), [C, _] = ee(""), [M, P] = ee(""), [$, O] = ee("idle"), [B, F] = ee(null), [Z, q] = ee(!1), [te, ie] = ee(null), [G, z] = ee(() => /* @__PURE__ */ new Set()), Y = ae(null), re = ae(null), oe = ae(""), U = ae(0), J = ae(Promise.resolve()), se = ae(null), L = ae(!1), K = a?.state.rootActivity ?? null, he = ye(() => new Map(c.map((k) => [k.activityVersionId, k])), [c]), we = ye(() => Cm(f), [f]), be = ye(() => cc(K, m), [K, m]), De = _y(be, be ? he.get(be.activityVersionId) : void 0), fe = !!be && De === "unsupported", Ie = ye(() => fe ? null : rr(K, m), [K, m, fe]), Oe = ye(() => xc(c), [c]), ve = ye(() => fe && be?.nodeId === D ? be : Ie?.slot.activities.find((k) => k.nodeId === D) ?? null, [fe, Ie, be, D]), Pe = ye(
    () => ve ? km(ve, he, we) : null,
    [he, we, ve]
  ), Ve = ve ? Ue(ve) : [], pe = De === "flowchart" && Ie?.slot.mode === "flowchart", $e = !K || !fe, tt = $ !== "idle", zt = !!a?.state.rootActivity && !tt, vn = $t(n, "weaver.workflows.find-draft-risks"), bn = $t(n, "weaver.workflows.propose-update"), Lt = ge(async () => {
    _(""), w("loading");
    const [k, T, W, X] = await Promise.all([
      ly(e, t),
      sc(e),
      vy(e).then(
        (de) => ({ ok: !0, descriptors: de }),
        () => ({ ok: !1, descriptors: [] })
      ),
      by(e).then(
        (de) => ({ ok: !0, descriptors: de }),
        () => ({ ok: !1, descriptors: qn })
      )
    ]), ne = k.draft ?? null;
    s(k), oe.current = ne ? Zt(ne) : "", l(ne), d(T.activities ?? []), h(W.descriptors), g(X.descriptors.length > 0 ? X.descriptors : qn), w(W.ok ? "ready" : "failed"), S([]), H(null);
  }, [e, t]);
  ce(() => {
    Lt().catch((k) => _(k instanceof Error ? k.message : String(k)));
  }, [Lt]), ce(() => {
    z((k) => {
      let T = !1;
      const W = new Set(k);
      for (const X of Oe)
        W.has(X.category) || (W.add(X.category), T = !0);
      return T ? W : k;
    });
  }, [Oe]), ce(() => {
    if (!be) {
      v([]), N([]);
      return;
    }
    const k = fe ? Ey(be, c, a?.layout ?? []) : Ie ? Ny(Ie, c, a?.layout ?? []) : { nodes: [], edges: [] };
    v(k.nodes), N(k.edges);
  }, [c, a?.layout, fe, Ie, be]);
  const No = (k) => {
    l((T) => T && { ...T, state: { ...T.state, rootActivity: k } });
  }, Ht = ge((k, T) => {
    if (a?.state.rootActivity && fe)
      return;
    const W = as(k, hs(k));
    if (!a?.state.rootActivity) {
      No(W), H(W.nodeId);
      return;
    }
    if (!Ie) {
      if (!Ue(W)[0]) {
        P(""), _("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      l((ne) => {
        if (!ne?.state.rootActivity) return ne;
        const de = ne.state.rootActivity, le = br(W, [], [de]), _e = T ? [
          ...ne.layout.filter((Ne) => Ne.nodeId !== de.nodeId),
          {
            nodeId: de.nodeId,
            x: Math.round(T.x),
            y: Math.round(T.y)
          }
        ] : ne.layout;
        return {
          ...ne,
          layout: _e,
          state: {
            ...ne.state,
            rootActivity: le
          }
        };
      }), H(a.state.rootActivity.nodeId), _(""), P(`Wrapped root in ${Ce(k)}`);
      return;
    }
    l((X) => {
      if (!X?.state.rootActivity) return X;
      const ne = rr(X.state.rootActivity, m);
      if (!ne) return X;
      const de = br(X.state.rootActivity, m, [...ne.slot.activities, W]), le = T ? [
        ...X.layout.filter((_e) => _e.nodeId !== W.nodeId),
        {
          nodeId: W.nodeId,
          x: Math.round(T.x),
          y: Math.round(T.y)
        }
      ] : X.layout;
      return {
        ...X,
        layout: le,
        state: {
          ...X.state,
          rootActivity: de
        }
      };
    }), H(W.nodeId);
  }, [a?.state.rootActivity, m, fe, Ie]), mt = ge((k, T) => {
    const W = as(k, hs(k)), X = {
      id: W.nodeId,
      type: "workflowActivity",
      position: T,
      selected: !0,
      data: {
        label: Ce(k),
        activityVersionId: k.activityVersionId,
        activityTypeKey: k.activityTypeKey,
        childSlots: Ue(W),
        acceptsInbound: String(k.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: fc(W, k)
      }
    };
    return { activityNode: W, node: X };
  }, []), Te = ge((k, T, W = []) => {
    fe || l((X) => {
      if (!X) return X;
      const ne = Iy(X.layout, k), de = X.state.rootActivity;
      if (!de) return { ...X, layout: ne };
      const le = rr(de, m);
      if (!le) return { ...X, layout: ne };
      const _e = Cy(le, k, T, W), Ne = le.slot.mode === "flowchart" ? ky(_e, T) : _e;
      return {
        ...X,
        layout: ne,
        state: {
          ...X.state,
          rootActivity: lc(de, m, Ne)
        }
      };
    });
  }, [m, fe]), Vt = ge((k, T) => {
    if (!Y.current) return null;
    const W = Y.current.getBoundingClientRect();
    return I ? I.screenToFlowPosition({ x: k, y: T }) : {
      x: k - W.left,
      y: T - W.top
    };
  }, [I]), Ot = ge((k, T) => document.elementFromPoint(k, T)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), Bt = ge((k, T, W) => {
    const X = p.find((Ae) => Ae.id === T.source), ne = p.find((Ae) => Ae.id === T.target), de = X && ne ? Tm(X, ne) : X ? ps(X) : W, le = mt(k, de), Ne = [...p.map((Ae) => Ae.selected ? { ...Ae, selected: !1 } : Ae), le.node], at = zy(E, T, le.node.id);
    v(Ne), N(at), H(le.node.id), Te(Ne, at, [le.activityNode]);
  }, [Te, mt, E, p]), xt = ge((k, T, W) => {
    if (!$e || !Y.current) return !1;
    const X = Y.current.getBoundingClientRect();
    if (!(T >= X.left && T <= X.right && W >= X.top && W <= X.bottom)) return !1;
    const de = Vt(T, W);
    if (!de) return !1;
    if (pe) {
      const le = Ot(T, W), _e = le ? E.find((Ne) => Ne.id === le) : void 0;
      if (_e)
        return Bt(k, _e, de), !0;
    }
    return Ht(k, de), !0;
  }, [Ht, $e, E, Ot, pe, Bt, Vt]);
  ce(() => {
    const k = (W) => {
      const X = se.current;
      if (!X) return;
      Math.hypot(W.clientX - X.startX, W.clientY - X.startY) >= am && (X.dragging = !0);
    }, T = (W) => {
      const X = se.current;
      if (se.current = null, !X?.dragging || !Y.current) return;
      const ne = Y.current.getBoundingClientRect();
      W.clientX >= ne.left && W.clientX <= ne.right && W.clientY >= ne.top && W.clientY <= ne.bottom && (L.current = !0, window.setTimeout(() => {
        L.current = !1;
      }, 0), xt(X.activity, W.clientX, W.clientY));
    };
    return window.addEventListener("pointermove", k), window.addEventListener("pointerup", T), window.addEventListener("pointercancel", T), () => {
      window.removeEventListener("pointermove", k), window.removeEventListener("pointerup", T), window.removeEventListener("pointercancel", T);
    };
  }, [I, xt]);
  const Eo = (k, T) => {
    k.dataTransfer.setData(us, T.activityVersionId), k.dataTransfer.setData("text/plain", T.activityVersionId), k.dataTransfer.effectAllowed = "copy";
  }, _o = (k, T) => {
    k.clientX === 0 && k.clientY === 0 || xt(T, k.clientX, k.clientY) && (L.current = !0, window.setTimeout(() => {
      L.current = !1;
    }, 0));
  }, Co = (k, T) => {
    k.button === 0 && (se.current = {
      activity: T,
      startX: k.clientX,
      startY: k.clientY,
      dragging: !1
    });
  }, ko = (k) => {
    L.current || $e && Ht(k);
  }, Sn = (k) => {
    if (!$e) {
      k.dataTransfer.dropEffect = "none";
      return;
    }
    if (k.preventDefault(), k.dataTransfer.dropEffect = "copy", !pe) return;
    const T = Ot(k.clientX, k.clientY);
    b(T);
  }, Nn = (k) => {
    if (!Y.current) return;
    const T = k.relatedTarget;
    T && Y.current.contains(T) || b(null);
  }, En = (k) => {
    if (k.preventDefault(), b(null), !$e) return;
    const T = k.dataTransfer.getData(us) || k.dataTransfer.getData("text/plain"), W = he.get(T);
    W && xt(W, k.clientX, k.clientY);
  }, Io = () => {
    if (!pe) return;
    const k = Y.current?.getBoundingClientRect();
    k && R({
      kind: "fromEmpty",
      clientX: k.left + k.width / 2,
      clientY: k.top + k.height / 2
    });
  }, Ft = ge(async (k, T) => {
    const W = async () => {
      const ne = ++U.current, de = Zt(k);
      _("");
      try {
        const le = await py(e, k), _e = Zt(le);
        return oe.current = _e, l((Ne) => !Ne || Ne.id !== le.id ? Ne : Zt(Ne) === de ? le : { ...Ne, validationErrors: le.validationErrors }), ne === U.current && P(T), le;
      } catch (le) {
        throw ne === U.current && (P(""), _(le instanceof Error ? le.message : String(le))), le;
      }
    }, X = J.current.then(W, W);
    return J.current = X.catch(() => {
    }), X;
  }, [e]);
  ce(() => {
    if (!Z || !a || Zt(a) === oe.current) return;
    P("Autosaving...");
    const T = window.setTimeout(() => {
      Ft(a, "Autosaved").catch(() => {
      });
    }, cm);
    return () => window.clearTimeout(T);
  }, [Z, a, Ft]);
  const Ao = async () => {
    if (!(!a || tt)) {
      O("saving"), P("Saving...");
      try {
        await Ft(a, "Saved");
      } catch {
      } finally {
        O("idle");
      }
    }
  }, Mo = async () => {
    if (!(!a || tt)) {
      O("promoting"), P("Promoting...");
      try {
        const k = await gy(e, a.id), T = await yy(e, k.versionId);
        ie(T.artifactId), P(`Published ${T.artifactVersion}`), await Lt();
      } catch (k) {
        P(""), _(k instanceof Error ? k.message : String(k));
      } finally {
        O("idle");
      }
    }
  }, jo = async () => {
    if (!a?.state.rootActivity || tt) return;
    const k = a;
    F(null), P("Preparing test run...");
    try {
      O("testRunPreparing"), P("Preparing test run...");
      const T = zm(k);
      O("testRunStarting"), P("Starting test run...");
      const W = await my(e, {
        definitionId: k.definitionId,
        snapshotId: T,
        state: k.state
      });
      F(W), P(Sc(W) ? "Test run rejected" : "Test run dispatched");
    } catch (T) {
      P(""), _(T instanceof Error ? T.message : String(T));
    } finally {
      O("idle");
    }
  }, Do = (k) => {
    const T = fe ? k.filter((W) => W.type === "select") : k;
    T.length !== 0 && v((W) => ka(T, W));
  }, Po = (k) => {
    fe || N((T) => Ia(k, T));
  }, Wt = (k) => !k.source || !k.target || k.source === k.target || !pe ? !1 : !k.targetHandle, $o = (k) => {
    if (!a?.state.rootActivity || !Ie || !pe || !Wt(k)) return;
    const T = so(k.source, k.target, k.sourceHandle ?? "Done", k.targetHandle ?? void 0), W = Ma(T, E);
    N(W), Te(p, W);
  }, To = () => {
    Te(p, E);
  }, Ro = (k, T) => {
    if (!T.nodeId || T.handleType === "target") {
      re.current = null;
      return;
    }
    re.current = {
      nodeId: T.nodeId,
      handleId: T.handleId ?? null
    };
  }, zo = (k) => {
    const T = re.current;
    if (re.current = null, !T || !pe || k.target?.closest(".react-flow__handle, .react-flow__node")) return;
    const X = Rm(k);
    R({
      kind: "fromPort",
      sourceNodeId: T.nodeId,
      sourceHandleId: T.handleId,
      clientX: X.x,
      clientY: X.y
    });
  }, _n = (k, T) => {
    if (!pe || !Wt(T)) return;
    const W = Gh(k, {
      ...T,
      sourceHandle: T.sourceHandle ?? "Done",
      targetHandle: T.targetHandle ?? void 0
    }, E, { shouldReplaceId: !1 });
    N(W), Te(p, W);
  }, Cn = (k) => {
    if (fe || k.length === 0) return;
    const T = new Set(k.map((ne) => ne.id)), W = p.filter((ne) => !T.has(ne.id)), X = E.filter((ne) => !T.has(ne.source) && !T.has(ne.target));
    v(W), N(X), D && T.has(D) && H(null), Te(W, X);
  }, kn = (k) => {
    if (fe || k.length === 0) return;
    const T = new Set(k.map((X) => X.id)), W = E.filter((X) => !T.has(X.id));
    N(W), Te(p, W);
  }, In = ge((k) => {
    if (fe) return;
    const T = E.filter((W) => W.id !== k);
    N(T), Te(p, T);
  }, [Te, E, fe, p]), An = ge((k, T, W) => {
    pe && R({ kind: "spliceEdge", edgeId: k, clientX: T, clientY: W });
  }, [pe]), Lo = (k) => {
    const T = A;
    if (!T) return;
    R(null);
    const W = Vt(T.clientX, T.clientY) ?? { x: 0, y: 0 };
    if (T.kind === "fromEmpty") {
      const ne = mt(k, W), le = [...p.map((_e) => _e.selected ? { ..._e, selected: !1 } : _e), ne.node];
      v(le), H(ne.node.id), Te(le, E, [ne.activityNode]);
      return;
    }
    if (T.kind === "fromPort") {
      const ne = p.find((Ae) => Ae.id === T.sourceNodeId), de = ne ? ps(ne) : W, le = mt(k, de), Ne = [...p.map((Ae) => Ae.selected ? { ...Ae, selected: !1 } : Ae), le.node], at = [...E, so(T.sourceNodeId, le.node.id, T.sourceHandleId ?? "Done")];
      v(Ne), N(at), H(le.node.id), Te(Ne, at, [le.activityNode]);
      return;
    }
    const X = E.find((ne) => ne.id === T.edgeId);
    X && Bt(k, X, W);
  }, Ho = ye(() => ({
    highlightedEdgeId: V,
    deleteEdge: In,
    requestInsertActivity: An
  }), [In, V, An]), Vo = (k, T, W) => {
    S((X) => [...X, { ownerNodeId: k.nodeId, slotId: T, label: W }]), H(null);
  }, Oo = ge((k) => {
    l((T) => {
      const W = T?.state.rootActivity;
      return !T || !W ? T : {
        ...T,
        state: {
          ...T.state,
          rootActivity: uc(W, k.nodeId, () => k)
        }
      };
    });
  }, []), Mn = (k) => {
    z((T) => {
      const W = new Set(T);
      return W.has(k) ? W.delete(k) : W.add(k), W;
    });
  };
  return !i || !a ? /* @__PURE__ */ u.jsx("div", { className: "wf-empty", children: C || "Loading workflow editor..." }) : /* @__PURE__ */ u.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ u.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ u.jsx("button", { type: "button", className: "wf-link-button", onClick: r, children: "Definitions" }),
      /* @__PURE__ */ u.jsx(Xn, { size: 14 }),
      /* @__PURE__ */ u.jsx("strong", { children: i.definition.name }),
      /* @__PURE__ */ u.jsx("span", { className: "wf-chip", children: "Draft" }),
      M ? /* @__PURE__ */ u.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ u.jsx(xn, { size: 13 }),
        " ",
        M
      ] }) : null,
      /* @__PURE__ */ u.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ u.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ u.jsx("input", { type: "checkbox", checked: Z, onChange: (k) => q(k.target.checked) }),
          /* @__PURE__ */ u.jsx("span", { children: "Autosave" })
        ] }),
        vn ? /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => Tt(n, vn, { definition: i.definition, draft: a }), children: [
          /* @__PURE__ */ u.jsx(Pt, { size: 15 }),
          " Risks"
        ] }) : null,
        bn ? /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => Tt(n, bn, { definition: i.definition, draft: a }), children: [
          /* @__PURE__ */ u.jsx(Pt, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ u.jsxs("button", { type: "button", disabled: tt, onClick: () => {
          Ao();
        }, children: [
          /* @__PURE__ */ u.jsx(iy, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ u.jsxs("button", { type: "button", disabled: tt, onClick: () => {
          Mo();
        }, children: [
          /* @__PURE__ */ u.jsx(ny, { size: 15 }),
          " Promote"
        ] }),
        /* @__PURE__ */ u.jsxs(
          "button",
          {
            type: "button",
            disabled: !zt,
            title: a.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              jo();
            },
            children: [
              /* @__PURE__ */ u.jsx(ic, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    C ? /* @__PURE__ */ u.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ u.jsx(Dt, { size: 16 }),
      " ",
      C
    ] }) : null,
    B ? /* @__PURE__ */ u.jsx($m, { testRun: B }) : null,
    /* @__PURE__ */ u.jsxs("div", { className: "wf-editor-body", children: [
      /* @__PURE__ */ u.jsxs("aside", { className: "wf-palette", children: [
        /* @__PURE__ */ u.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ u.jsx(Jg, { size: 15 }),
          " Activities"
        ] }),
        /* @__PURE__ */ u.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: Oe.map((k) => {
          const T = G.has(k.category);
          return /* @__PURE__ */ u.jsxs("div", { className: "wf-palette-category", children: [
            /* @__PURE__ */ u.jsxs(
              "button",
              {
                type: "button",
                className: "wf-palette-category-toggle",
                role: "treeitem",
                "aria-expanded": T,
                onClick: () => Mn(k.category),
                children: [
                  T ? /* @__PURE__ */ u.jsx(ey, { size: 14 }) : /* @__PURE__ */ u.jsx(Xn, { size: 14 }),
                  /* @__PURE__ */ u.jsx("span", { children: k.category }),
                  /* @__PURE__ */ u.jsx("small", { children: k.activities.length })
                ]
              }
            ),
            T ? /* @__PURE__ */ u.jsx("div", { className: "wf-palette-activities", role: "group", children: k.activities.map((W) => {
              const X = W.description?.trim(), ne = X ? `wf-palette-description-${W.activityVersionId}` : void 0;
              return /* @__PURE__ */ u.jsxs(
                "button",
                {
                  type: "button",
                  className: "wf-palette-activity",
                  role: "treeitem",
                  draggable: !0,
                  title: X || Ce(W),
                  "aria-describedby": ne,
                  onClick: () => ko(W),
                  onDragStart: (de) => Eo(de, W),
                  onDragEnd: (de) => _o(de, W),
                  onPointerDown: (de) => Co(de, W),
                  children: [
                    /* @__PURE__ */ u.jsx("strong", { children: Ce(W) }),
                    X ? /* @__PURE__ */ u.jsx("small", { id: ne, children: X }) : null
                  ]
                },
                W.activityVersionId
              );
            }) }) : null
          ] }, k.category);
        }) })
      ] }),
      /* @__PURE__ */ u.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ u.jsxs("div", { className: "wf-breadcrumb", children: [
          /* @__PURE__ */ u.jsx("button", { type: "button", onClick: () => {
            S([]), H(null);
          }, children: "Root" }),
          m.map((k, T) => /* @__PURE__ */ u.jsxs(st.Fragment, { children: [
            /* @__PURE__ */ u.jsx(Xn, { size: 13 }),
            /* @__PURE__ */ u.jsx("button", { type: "button", onClick: () => {
              S(m.slice(0, T + 1)), H(null);
            }, children: k.label })
          ] }, `${k.ownerNodeId}-${k.slotId}-${T}`))
        ] }),
        /* @__PURE__ */ u.jsxs("div", { className: "wf-canvas", ref: Y, onDragOver: Sn, onDragLeave: Nn, onDrop: En, children: [
          /* @__PURE__ */ u.jsx(gc.Provider, { value: Ho, children: /* @__PURE__ */ u.jsxs(
            wg,
            {
              nodes: p,
              edges: E,
              nodeTypes: im,
              edgeTypes: sm,
              onInit: j,
              onNodesChange: Do,
              onEdgesChange: Po,
              onNodesDelete: Cn,
              onEdgesDelete: kn,
              onConnect: $o,
              onConnectStart: pe ? Ro : void 0,
              onConnectEnd: pe ? zo : void 0,
              onReconnect: pe ? _n : void 0,
              isValidConnection: Wt,
              onDragOver: Sn,
              onDragLeave: Nn,
              onDrop: En,
              onPaneClick: () => H(null),
              onNodeClick: (k, T) => H(T.id),
              onNodeDragStop: fe ? void 0 : To,
              fitView: !0,
              minZoom: 0.2,
              maxZoom: 1.8,
              nodesConnectable: pe,
              nodesDraggable: !fe,
              selectionOnDrag: !0,
              multiSelectionKeyCode: ["Shift", "Meta", "Control"],
              deleteKeyCode: fe ? null : ["Backspace", "Delete"],
              panActivationKeyCode: null,
              defaultEdgeOptions: { type: "workflow" },
              children: [
                /* @__PURE__ */ u.jsx(Cg, { gap: 18, size: 1 }),
                /* @__PURE__ */ u.jsx(Pg, {}),
                /* @__PURE__ */ u.jsx(Xg, { pannable: !0, zoomable: !0 })
              ]
            }
          ) }),
          pe && p.length === 0 ? /* @__PURE__ */ u.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => Io(), children: [
            /* @__PURE__ */ u.jsx(Br, { size: 15 }),
            " Add activity"
          ] }) : null,
          A ? /* @__PURE__ */ u.jsx(
            Dm,
            {
              clientX: A.clientX,
              clientY: A.clientY,
              activities: c,
              onPick: Lo,
              onClose: () => R(null)
            }
          ) : null
        ] }),
        /* @__PURE__ */ u.jsx(Pm, { draft: a })
      ] }),
      /* @__PURE__ */ u.jsxs("aside", { className: "wf-inspector", children: [
        /* @__PURE__ */ u.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ u.jsx(oy, { size: 15 }),
          " Inspector"
        ] }),
        ve ? /* @__PURE__ */ u.jsxs("div", { className: "wf-inspector-content", children: [
          /* @__PURE__ */ u.jsx("h3", { children: p.find((k) => k.id === ve.nodeId)?.data.label ?? ve.nodeId }),
          /* @__PURE__ */ u.jsxs("dl", { children: [
            /* @__PURE__ */ u.jsx("dt", { children: "Node ID" }),
            /* @__PURE__ */ u.jsx("dd", { children: ve.nodeId }),
            /* @__PURE__ */ u.jsx("dt", { children: "Activity version" }),
            /* @__PURE__ */ u.jsx("dd", { children: ve.activityVersionId })
          ] }),
          /* @__PURE__ */ u.jsx(
            Jy,
            {
              activity: ve,
              descriptor: Pe,
              editors: o,
              expressionDescriptors: y,
              descriptorStatus: x,
              onChange: Oo
            }
          ),
          Ve.length > 0 ? /* @__PURE__ */ u.jsxs("div", { className: "wf-slot-list", children: [
            /* @__PURE__ */ u.jsx("span", { children: "Embedded slots" }),
            Ve.map((k) => /* @__PURE__ */ u.jsxs("button", { type: "button", onClick: () => Vo(ve, k.id, `${p.find((T) => T.id === ve.nodeId)?.data.label ?? ve.nodeId} / ${k.label}`), children: [
              k.label,
              /* @__PURE__ */ u.jsxs("small", { children: [
                k.activities.length,
                " activit",
                k.activities.length === 1 ? "y" : "ies"
              ] })
            ] }, k.id))
          ] }) : /* @__PURE__ */ u.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
        ] }) : /* @__PURE__ */ u.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." })
      ] })
    ] })
  ] });
}
function Mm({ data: e, selected: t }) {
  const n = e, o = !n.suppressFlowPorts, r = o ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [];
  return /* @__PURE__ */ u.jsxs("div", { className: t ? "wf-node selected" : "wf-node", children: [
    o && n.acceptsInbound ? /* @__PURE__ */ u.jsx(jt, { type: "target", position: Q.Left }) : null,
    /* @__PURE__ */ u.jsx("strong", { children: n.label }),
    /* @__PURE__ */ u.jsx("small", { children: n.activityTypeKey ?? n.activityVersionId }),
    n.childSlots.length > 0 ? /* @__PURE__ */ u.jsxs("span", { children: [
      n.childSlots.length,
      " embedded slot",
      n.childSlots.length === 1 ? "" : "s"
    ] }) : null,
    r.map((i, s) => {
      const a = `${(s + 1) / (r.length + 1) * 100}%`;
      return /* @__PURE__ */ u.jsxs(st.Fragment, { children: [
        /* @__PURE__ */ u.jsx("span", { className: "wf-node-port-label", style: { top: a }, children: i.displayName }),
        /* @__PURE__ */ u.jsx(jt, { type: "source", position: Q.Right, id: i.name, style: { top: a } })
      ] }, i.name);
    })
  ] });
}
function jm(e) {
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
  } = e, h = st.useContext(gc), [y, g] = ee(!1), [x, w, m] = ro({ sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: s, targetPosition: a }), S = h?.highlightedEdgeId === t;
  return /* @__PURE__ */ u.jsxs(u.Fragment, { children: [
    /* @__PURE__ */ u.jsx(
      mn,
      {
        id: t,
        path: x,
        markerEnd: l,
        style: {
          ...c,
          strokeWidth: S ? 2.5 : c?.strokeWidth
        },
        label: d,
        labelX: w,
        labelY: m,
        labelStyle: f,
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1)
      }
    ),
    h ? /* @__PURE__ */ u.jsx(bg, { children: /* @__PURE__ */ u.jsxs(
      "div",
      {
        className: ["wf-edge-actions", y ? "visible" : "", S ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${w}px, ${m}px)` },
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1),
        children: [
          /* @__PURE__ */ u.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (p) => h.requestInsertActivity(t, p.clientX, p.clientY), children: /* @__PURE__ */ u.jsx(Br, { size: 12 }) }),
          /* @__PURE__ */ u.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => h.deleteEdge(t), children: /* @__PURE__ */ u.jsx(vr, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function Dm({ clientX: e, clientY: t, activities: n, onPick: o, onClose: r }) {
  const [i, s] = ee(""), [a, l] = ee(0), c = ae(null), d = ae(null), f = ye(() => {
    const S = i.trim().toLowerCase(), p = n.filter(bc);
    return S ? p.filter((v) => Ce(v).toLowerCase().includes(S) || v.activityTypeKey.toLowerCase().includes(S) || (v.category ?? "").toLowerCase().includes(S) || (v.description ?? "").toLowerCase().includes(S)) : p;
  }, [n, i]), h = ye(() => xc(f), [f]), y = ye(() => h.flatMap((S) => S.activities), [h]);
  ce(() => {
    requestAnimationFrame(() => d.current?.focus());
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
      S.preventDefault(), l((p) => Math.min(p + 1, y.length - 1));
    else if (S.key === "ArrowUp")
      S.preventDefault(), l((p) => Math.max(p - 1, 0));
    else if (S.key === "Enter") {
      S.preventDefault();
      const p = y[a];
      p && o(p);
    }
  }, x = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), w = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let m = -1;
  return /* @__PURE__ */ u.jsxs("div", { ref: c, className: "wf-connect-menu", style: { left: x, top: w }, onMouseDown: (S) => S.stopPropagation(), onClick: (S) => S.stopPropagation(), children: [
    /* @__PURE__ */ u.jsx(
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
        onKeyDown: g
      }
    ),
    /* @__PURE__ */ u.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: h.length === 0 ? /* @__PURE__ */ u.jsx("p", { children: "No matching activities." }) : h.map((S) => /* @__PURE__ */ u.jsxs("section", { children: [
      /* @__PURE__ */ u.jsx("h4", { children: S.category }),
      S.activities.map((p) => {
        m += 1;
        const v = m, E = v === a;
        return /* @__PURE__ */ u.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": E,
            className: E ? "active" : "",
            onMouseEnter: () => l(v),
            onClick: () => o(p),
            children: [
              /* @__PURE__ */ u.jsx("strong", { children: Ce(p) }),
              /* @__PURE__ */ u.jsx("small", { children: p.category || p.activityTypeKey })
            ]
          },
          p.activityVersionId
        );
      })
    ] }, S.category)) })
  ] });
}
function Pm({ draft: e }) {
  return e.validationErrors.length ? /* @__PURE__ */ u.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ u.jsx(Dt, { size: 14 }),
    e.validationErrors.length,
    " validation issue",
    e.validationErrors.length === 1 ? "" : "s"
  ] }) : /* @__PURE__ */ u.jsxs("div", { className: "wf-validation ok", children: [
    /* @__PURE__ */ u.jsx(xn, { size: 14 }),
    " No validation errors"
  ] });
}
function $m({ testRun: e }) {
  const t = Sc(e);
  return /* @__PURE__ */ u.jsxs("section", { className: "wf-test-run-capsule", "data-state": t ? "rejected" : "accepted", "aria-live": "polite", children: [
    /* @__PURE__ */ u.jsxs("div", { className: "wf-test-run-heading", children: [
      t ? /* @__PURE__ */ u.jsx(Dt, { size: 16 }) : /* @__PURE__ */ u.jsx(xn, { size: 16 }),
      /* @__PURE__ */ u.jsxs("div", { children: [
        /* @__PURE__ */ u.jsx("strong", { children: t ? "Test run rejected" : "Test run dispatched" }),
        /* @__PURE__ */ u.jsx("span", { children: "Ephemeral - not promoted" })
      ] })
    ] }),
    t && e.reason ? /* @__PURE__ */ u.jsx("p", { children: e.reason }) : null,
    /* @__PURE__ */ u.jsxs("dl", { children: [
      /* @__PURE__ */ u.jsxs("div", { children: [
        /* @__PURE__ */ u.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ u.jsx("dd", { children: e.status })
      ] }),
      e.commandDispatchStatus ? /* @__PURE__ */ u.jsxs("div", { children: [
        /* @__PURE__ */ u.jsx("dt", { children: "Dispatch" }),
        /* @__PURE__ */ u.jsx("dd", { children: e.commandDispatchStatus })
      ] }) : null,
      /* @__PURE__ */ u.jsxs("div", { children: [
        /* @__PURE__ */ u.jsx("dt", { children: "Test run" }),
        /* @__PURE__ */ u.jsx("dd", { children: e.testRunId })
      ] }),
      e.artifactId ? /* @__PURE__ */ u.jsxs("div", { children: [
        /* @__PURE__ */ u.jsx("dt", { children: "Transient artifact" }),
        /* @__PURE__ */ u.jsx("dd", { children: e.artifactId })
      ] }) : null,
      e.workflowExecutionId ? /* @__PURE__ */ u.jsxs("div", { children: [
        /* @__PURE__ */ u.jsx("dt", { children: "Execution" }),
        /* @__PURE__ */ u.jsx("dd", { children: e.workflowExecutionId })
      ] }) : null,
      e.expiresAt ? /* @__PURE__ */ u.jsxs("div", { children: [
        /* @__PURE__ */ u.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ u.jsx("dd", { children: co(e.expiresAt) })
      ] }) : null
    ] })
  ] });
}
function hs(e) {
  return `${Ce(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function ps(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function Tm(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function Rm(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function Zt(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function zm(e) {
  return `${e.id}-${Lm(JSON.stringify(e.state))}`;
}
function Lm(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function Sc(e) {
  return e.status.toLowerCase() === "rejected";
}
function co(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
export {
  Om as register
};
