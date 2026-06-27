import { j as La } from "./module-BVvtQwcp.js";
import { useState as Ut, useLayoutEffect as Ef, useEffect as _t, forwardRef as Rf, useRef as Pf, useImperativeHandle as If, useCallback as Nf } from "react";
function sr() {
  return sr = Object.assign ? Object.assign.bind() : function(n) {
    for (var e = 1; e < arguments.length; e++) {
      var t = arguments[e];
      for (var i in t) ({}).hasOwnProperty.call(t, i) && (n[i] = t[i]);
    }
    return n;
  }, sr.apply(null, arguments);
}
function Wf(n, e) {
  if (n == null) return {};
  var t = {};
  for (var i in n) if ({}.hasOwnProperty.call(n, i)) {
    if (e.indexOf(i) !== -1) continue;
    t[i] = n[i];
  }
  return t;
}
let rr = [], Ea = [];
(() => {
  let n = "lc,34,7n,7,7b,19,,,,2,,2,,,20,b,1c,l,g,,2t,7,2,6,2,2,,4,z,,u,r,2j,b,1m,9,9,,o,4,,9,,3,,5,17,3,3b,f,,w,1j,,,,4,8,4,,3,7,a,2,t,,1m,,,,2,4,8,,9,,a,2,q,,2,2,1l,,4,2,4,2,2,3,3,,u,2,3,,b,2,1l,,4,5,,2,4,,k,2,m,6,,,1m,,,2,,4,8,,7,3,a,2,u,,1n,,,,c,,9,,14,,3,,1l,3,5,3,,4,7,2,b,2,t,,1m,,2,,2,,3,,5,2,7,2,b,2,s,2,1l,2,,,2,4,8,,9,,a,2,t,,20,,4,,2,3,,,8,,29,,2,7,c,8,2q,,2,9,b,6,22,2,r,,,,,,1j,e,,5,,2,5,b,,10,9,,2u,4,,6,,2,2,2,p,2,4,3,g,4,d,,2,2,6,,f,,jj,3,qa,3,t,3,t,2,u,2,1s,2,,7,8,,2,b,9,,19,3,3b,2,y,,3a,3,4,2,9,,6,3,63,2,2,,1m,,,7,,,,,2,8,6,a,2,,1c,h,1r,4,1c,7,,,5,,14,9,c,2,w,4,2,2,,3,1k,,,2,3,,,3,1m,8,2,2,48,3,,d,,7,4,,6,,3,2,5i,1m,,5,ek,,5f,x,2da,3,3x,,2o,w,fe,6,2x,2,n9w,4,,a,w,2,28,2,7k,,3,,4,,p,2,5,,47,2,q,i,d,,12,8,p,b,1a,3,1c,,2,4,2,2,13,,1v,6,2,2,2,2,c,,8,,1b,,1f,,,3,2,2,5,2,,,16,2,8,,6m,,2,,4,,fn4,,kh,g,g,g,a6,2,gt,,6a,,45,5,1ae,3,,2,5,4,14,3,4,,4l,2,fx,4,ar,2,49,b,4w,,1i,f,1k,3,1d,4,2,2,1x,3,10,5,,8,1q,,c,2,1g,9,a,4,2,,2n,3,2,,,2,6,,4g,,3,8,l,2,1l,2,,,,,m,,e,7,3,5,5f,8,2,3,,,n,,29,,2,6,,,2,,,2,,2,6j,,2,4,6,2,,2,r,2,2d,8,2,,,2,2y,,,,2,6,,,2t,3,2,4,,5,77,9,,2,6t,,a,2,,,4,,40,4,2,2,4,,w,a,14,6,2,4,8,,9,6,2,3,1a,d,,2,ba,7,,6,,,2a,m,2,7,,2,,2,3e,6,3,,,2,,7,,,20,2,3,,,,9n,2,f0b,5,1n,7,t4,,1r,4,29,,f5k,2,43q,,,3,4,5,8,8,2,7,u,4,44,3,1iz,1j,4,1e,8,,e,,m,5,,f,11s,7,,h,2,7,,2,,5,79,7,c5,4,15s,7,31,7,240,5,gx7k,2o,3k,6o".split(",").map((e) => e ? parseInt(e, 36) : 1);
  for (let e = 0, t = 0; e < n.length; e++)
    (e % 2 ? Ea : rr).push(t = t + n[e]);
})();
function Hf(n) {
  if (n < 768) return !1;
  for (let e = 0, t = rr.length; ; ) {
    let i = e + t >> 1;
    if (n < rr[i]) t = i;
    else if (n >= Ea[i]) e = i + 1;
    else return !0;
    if (e == t) return !1;
  }
}
function Vo(n) {
  return n >= 127462 && n <= 127487;
}
const zo = 8205;
function Ff(n, e, t = !0, i = !0) {
  return (t ? Ra : Vf)(n, e, i);
}
function Ra(n, e, t) {
  if (e == n.length) return e;
  e && Pa(n.charCodeAt(e)) && Ia(n.charCodeAt(e - 1)) && e--;
  let i = Ts(n, e);
  for (e += qo(i); e < n.length; ) {
    let s = Ts(n, e);
    if (i == zo || s == zo || t && Hf(s))
      e += qo(s), i = s;
    else if (Vo(s)) {
      let r = 0, o = e - 2;
      for (; o >= 0 && Vo(Ts(n, o)); )
        r++, o -= 2;
      if (r % 2 == 0) break;
      e += 2;
    } else
      break;
  }
  return e;
}
function Vf(n, e, t) {
  for (; e > 0; ) {
    let i = Ra(n, e - 2, t);
    if (i < e) return i;
    e--;
  }
  return 0;
}
function Ts(n, e) {
  let t = n.charCodeAt(e);
  if (!Ia(t) || e + 1 == n.length) return t;
  let i = n.charCodeAt(e + 1);
  return Pa(i) ? (t - 55296 << 10) + (i - 56320) + 65536 : t;
}
function Pa(n) {
  return n >= 56320 && n < 57344;
}
function Ia(n) {
  return n >= 55296 && n < 56320;
}
function qo(n) {
  return n < 65536 ? 1 : 2;
}
class z {
  /**
  Get the line description around the given position.
  */
  lineAt(e) {
    if (e < 0 || e > this.length)
      throw new RangeError(`Invalid position ${e} in document of length ${this.length}`);
    return this.lineInner(e, !1, 1, 0);
  }
  /**
  Get the description for the given (1-based) line number.
  */
  line(e) {
    if (e < 1 || e > this.lines)
      throw new RangeError(`Invalid line number ${e} in ${this.lines}-line document`);
    return this.lineInner(e, !0, 1, 0);
  }
  /**
  Replace a range of the text with the given content.
  */
  replace(e, t, i) {
    [e, t] = li(this, e, t);
    let s = [];
    return this.decompose(
      0,
      e,
      s,
      2
      /* Open.To */
    ), i.length && i.decompose(
      0,
      i.length,
      s,
      3
      /* Open.To */
    ), this.decompose(
      t,
      this.length,
      s,
      1
      /* Open.From */
    ), Ze.from(s, this.length - (t - e) + i.length);
  }
  /**
  Append another document to this one.
  */
  append(e) {
    return this.replace(this.length, this.length, e);
  }
  /**
  Retrieve the text between the given points.
  */
  slice(e, t = this.length) {
    [e, t] = li(this, e, t);
    let i = [];
    return this.decompose(e, t, i, 0), Ze.from(i, t - e);
  }
  /**
  Test whether this text is equal to another instance.
  */
  eq(e) {
    if (e == this)
      return !0;
    if (e.length != this.length || e.lines != this.lines)
      return !1;
    let t = this.scanIdentical(e, 1), i = this.length - this.scanIdentical(e, -1), s = new Ti(this), r = new Ti(e);
    for (let o = t, l = t; ; ) {
      if (s.next(o), r.next(o), o = 0, s.lineBreak != r.lineBreak || s.done != r.done || s.value != r.value)
        return !1;
      if (l += s.value.length, s.done || l >= i)
        return !0;
    }
  }
  /**
  Iterate over the text. When `dir` is `-1`, iteration happens
  from end to start. This will return lines and the breaks between
  them as separate strings.
  */
  iter(e = 1) {
    return new Ti(this, e);
  }
  /**
  Iterate over a range of the text. When `from` > `to`, the
  iterator will run in reverse.
  */
  iterRange(e, t = this.length) {
    return new Na(this, e, t);
  }
  /**
  Return a cursor that iterates over the given range of lines,
  _without_ returning the line breaks between, and yielding empty
  strings for empty lines.
  
  When `from` and `to` are given, they should be 1-based line numbers.
  */
  iterLines(e, t) {
    let i;
    if (e == null)
      i = this.iter();
    else {
      t == null && (t = this.lines + 1);
      let s = this.line(e).from;
      i = this.iterRange(s, Math.max(s, t == this.lines + 1 ? this.length : t <= 1 ? 0 : this.line(t - 1).to));
    }
    return new Wa(i);
  }
  /**
  Return the document as a string, using newline characters to
  separate lines.
  */
  toString() {
    return this.sliceString(0);
  }
  /**
  Convert the document to an array of lines (which can be
  deserialized again via [`Text.of`](https://codemirror.net/6/docs/ref/#state.Text^of)).
  */
  toJSON() {
    let e = [];
    return this.flatten(e), e;
  }
  /**
  @internal
  */
  constructor() {
  }
  /**
  Create a `Text` instance for the given array of lines.
  */
  static of(e) {
    if (e.length == 0)
      throw new RangeError("A document must have at least one line");
    return e.length == 1 && !e[0] ? z.empty : e.length <= 32 ? new ee(e) : Ze.from(ee.split(e, []));
  }
}
class ee extends z {
  constructor(e, t = zf(e)) {
    super(), this.text = e, this.length = t;
  }
  get lines() {
    return this.text.length;
  }
  get children() {
    return null;
  }
  lineInner(e, t, i, s) {
    for (let r = 0; ; r++) {
      let o = this.text[r], l = s + o.length;
      if ((t ? i : l) >= e)
        return new qf(s, l, i, o);
      s = l + 1, i++;
    }
  }
  decompose(e, t, i, s) {
    let r = e <= 0 && t >= this.length ? this : new ee($o(this.text, e, t), Math.min(t, this.length) - Math.max(0, e));
    if (s & 1) {
      let o = i.pop(), l = Rn(r.text, o.text.slice(), 0, r.length);
      if (l.length <= 32)
        i.push(new ee(l, o.length + r.length));
      else {
        let a = l.length >> 1;
        i.push(new ee(l.slice(0, a)), new ee(l.slice(a)));
      }
    } else
      i.push(r);
  }
  replace(e, t, i) {
    if (!(i instanceof ee))
      return super.replace(e, t, i);
    [e, t] = li(this, e, t);
    let s = Rn(this.text, Rn(i.text, $o(this.text, 0, e)), t), r = this.length + i.length - (t - e);
    return s.length <= 32 ? new ee(s, r) : Ze.from(ee.split(s, []), r);
  }
  sliceString(e, t = this.length, i = `
`) {
    [e, t] = li(this, e, t);
    let s = "";
    for (let r = 0, o = 0; r <= t && o < this.text.length; o++) {
      let l = this.text[o], a = r + l.length;
      r > e && o && (s += i), e < a && t > r && (s += l.slice(Math.max(0, e - r), t - r)), r = a + 1;
    }
    return s;
  }
  flatten(e) {
    for (let t of this.text)
      e.push(t);
  }
  scanIdentical() {
    return 0;
  }
  static split(e, t) {
    let i = [], s = -1;
    for (let r of e)
      i.push(r), s += r.length + 1, i.length == 32 && (t.push(new ee(i, s)), i = [], s = -1);
    return s > -1 && t.push(new ee(i, s)), t;
  }
}
class Ze extends z {
  constructor(e, t) {
    super(), this.children = e, this.length = t, this.lines = 0;
    for (let i of e)
      this.lines += i.lines;
  }
  lineInner(e, t, i, s) {
    for (let r = 0; ; r++) {
      let o = this.children[r], l = s + o.length, a = i + o.lines - 1;
      if ((t ? a : l) >= e)
        return o.lineInner(e, t, i, s);
      s = l + 1, i = a + 1;
    }
  }
  decompose(e, t, i, s) {
    for (let r = 0, o = 0; o <= t && r < this.children.length; r++) {
      let l = this.children[r], a = o + l.length;
      if (e <= a && t >= o) {
        let h = s & ((o <= e ? 1 : 0) | (a >= t ? 2 : 0));
        o >= e && a <= t && !h ? i.push(l) : l.decompose(e - o, t - o, i, h);
      }
      o = a + 1;
    }
  }
  replace(e, t, i) {
    if ([e, t] = li(this, e, t), i.lines < this.lines)
      for (let s = 0, r = 0; s < this.children.length; s++) {
        let o = this.children[s], l = r + o.length;
        if (e >= r && t <= l) {
          let a = o.replace(e - r, t - r, i), h = this.lines - o.lines + a.lines;
          if (a.lines < h >> 4 && a.lines > h >> 6) {
            let c = this.children.slice();
            return c[s] = a, new Ze(c, this.length - (t - e) + i.length);
          }
          return super.replace(r, l, a);
        }
        r = l + 1;
      }
    return super.replace(e, t, i);
  }
  sliceString(e, t = this.length, i = `
`) {
    [e, t] = li(this, e, t);
    let s = "";
    for (let r = 0, o = 0; r < this.children.length && o <= t; r++) {
      let l = this.children[r], a = o + l.length;
      o > e && r && (s += i), e < a && t > o && (s += l.sliceString(e - o, t - o, i)), o = a + 1;
    }
    return s;
  }
  flatten(e) {
    for (let t of this.children)
      t.flatten(e);
  }
  scanIdentical(e, t) {
    if (!(e instanceof Ze))
      return 0;
    let i = 0, [s, r, o, l] = t > 0 ? [0, 0, this.children.length, e.children.length] : [this.children.length - 1, e.children.length - 1, -1, -1];
    for (; ; s += t, r += t) {
      if (s == o || r == l)
        return i;
      let a = this.children[s], h = e.children[r];
      if (a != h)
        return i + a.scanIdentical(h, t);
      i += a.length + 1;
    }
  }
  static from(e, t = e.reduce((i, s) => i + s.length + 1, -1)) {
    let i = 0;
    for (let d of e)
      i += d.lines;
    if (i < 32) {
      let d = [];
      for (let p of e)
        p.flatten(d);
      return new ee(d, t);
    }
    let s = Math.max(
      32,
      i >> 5
      /* Tree.BranchShift */
    ), r = s << 1, o = s >> 1, l = [], a = 0, h = -1, c = [];
    function f(d) {
      let p;
      if (d.lines > r && d instanceof Ze)
        for (let m of d.children)
          f(m);
      else d.lines > o && (a > o || !a) ? (u(), l.push(d)) : d instanceof ee && a && (p = c[c.length - 1]) instanceof ee && d.lines + p.lines <= 32 ? (a += d.lines, h += d.length + 1, c[c.length - 1] = new ee(p.text.concat(d.text), p.length + 1 + d.length)) : (a + d.lines > s && u(), a += d.lines, h += d.length + 1, c.push(d));
    }
    function u() {
      a != 0 && (l.push(c.length == 1 ? c[0] : Ze.from(c, h)), h = -1, a = c.length = 0);
    }
    for (let d of e)
      f(d);
    return u(), l.length == 1 ? l[0] : new Ze(l, t);
  }
}
z.empty = /* @__PURE__ */ new ee([""], 0);
function zf(n) {
  let e = -1;
  for (let t of n)
    e += t.length + 1;
  return e;
}
function Rn(n, e, t = 0, i = 1e9) {
  for (let s = 0, r = 0, o = !0; r < n.length && s <= i; r++) {
    let l = n[r], a = s + l.length;
    a >= t && (a > i && (l = l.slice(0, i - s)), s < t && (l = l.slice(t - s)), o ? (e[e.length - 1] += l, o = !1) : e.push(l)), s = a + 1;
  }
  return e;
}
function $o(n, e, t) {
  return Rn(n, [""], e, t);
}
class Ti {
  constructor(e, t = 1) {
    this.dir = t, this.done = !1, this.lineBreak = !1, this.value = "", this.nodes = [e], this.offsets = [t > 0 ? 1 : (e instanceof ee ? e.text.length : e.children.length) << 1];
  }
  nextInner(e, t) {
    for (this.done = this.lineBreak = !1; ; ) {
      let i = this.nodes.length - 1, s = this.nodes[i], r = this.offsets[i], o = r >> 1, l = s instanceof ee ? s.text.length : s.children.length;
      if (o == (t > 0 ? l : 0)) {
        if (i == 0)
          return this.done = !0, this.value = "", this;
        t > 0 && this.offsets[i - 1]++, this.nodes.pop(), this.offsets.pop();
      } else if ((r & 1) == (t > 0 ? 0 : 1)) {
        if (this.offsets[i] += t, e == 0)
          return this.lineBreak = !0, this.value = `
`, this;
        e--;
      } else if (s instanceof ee) {
        let a = s.text[o + (t < 0 ? -1 : 0)];
        if (this.offsets[i] += t, a.length > Math.max(0, e))
          return this.value = e == 0 ? a : t > 0 ? a.slice(e) : a.slice(0, a.length - e), this;
        e -= a.length;
      } else {
        let a = s.children[o + (t < 0 ? -1 : 0)];
        e > a.length ? (e -= a.length, this.offsets[i] += t) : (t < 0 && this.offsets[i]--, this.nodes.push(a), this.offsets.push(t > 0 ? 1 : (a instanceof ee ? a.text.length : a.children.length) << 1));
      }
    }
  }
  next(e = 0) {
    return e < 0 && (this.nextInner(-e, -this.dir), e = this.value.length), this.nextInner(e, this.dir);
  }
}
class Na {
  constructor(e, t, i) {
    this.value = "", this.done = !1, this.cursor = new Ti(e, t > i ? -1 : 1), this.pos = t > i ? e.length : 0, this.from = Math.min(t, i), this.to = Math.max(t, i);
  }
  nextInner(e, t) {
    if (t < 0 ? this.pos <= this.from : this.pos >= this.to)
      return this.value = "", this.done = !0, this;
    e += Math.max(0, t < 0 ? this.pos - this.to : this.from - this.pos);
    let i = t < 0 ? this.pos - this.from : this.to - this.pos;
    e > i && (e = i), i -= e;
    let { value: s } = this.cursor.next(e);
    return this.pos += (s.length + e) * t, this.value = s.length <= i ? s : t < 0 ? s.slice(s.length - i) : s.slice(0, i), this.done = !this.value, this;
  }
  next(e = 0) {
    return e < 0 ? e = Math.max(e, this.from - this.pos) : e > 0 && (e = Math.min(e, this.to - this.pos)), this.nextInner(e, this.cursor.dir);
  }
  get lineBreak() {
    return this.cursor.lineBreak && this.value != "";
  }
}
class Wa {
  constructor(e) {
    this.inner = e, this.afterBreak = !0, this.value = "", this.done = !1;
  }
  next(e = 0) {
    let { done: t, lineBreak: i, value: s } = this.inner.next(e);
    return t && this.afterBreak ? (this.value = "", this.afterBreak = !1) : t ? (this.done = !0, this.value = "") : i ? this.afterBreak ? this.value = "" : (this.afterBreak = !0, this.next()) : (this.value = s, this.afterBreak = !1), this;
  }
  get lineBreak() {
    return !1;
  }
}
typeof Symbol < "u" && (z.prototype[Symbol.iterator] = function() {
  return this.iter();
}, Ti.prototype[Symbol.iterator] = Na.prototype[Symbol.iterator] = Wa.prototype[Symbol.iterator] = function() {
  return this;
});
class qf {
  /**
  @internal
  */
  constructor(e, t, i, s) {
    this.from = e, this.to = t, this.number = i, this.text = s;
  }
  /**
  The length of the line (not including any line break after it).
  */
  get length() {
    return this.to - this.from;
  }
}
function li(n, e, t) {
  return e = Math.max(0, Math.min(n.length, e)), [e, Math.max(e, Math.min(n.length, t))];
}
function oe(n, e, t = !0, i = !0) {
  return Ff(n, e, t, i);
}
function $f(n) {
  return n >= 56320 && n < 57344;
}
function Kf(n) {
  return n >= 55296 && n < 56320;
}
function ke(n, e) {
  let t = n.charCodeAt(e);
  if (!Kf(t) || e + 1 == n.length)
    return t;
  let i = n.charCodeAt(e + 1);
  return $f(i) ? (t - 55296 << 10) + (i - 56320) + 65536 : t;
}
function Zr(n) {
  return n <= 65535 ? String.fromCharCode(n) : (n -= 65536, String.fromCharCode((n >> 10) + 55296, (n & 1023) + 56320));
}
function et(n) {
  return n < 65536 ? 1 : 2;
}
const or = /\r\n?|\n/;
var fe = /* @__PURE__ */ (function(n) {
  return n[n.Simple = 0] = "Simple", n[n.TrackDel = 1] = "TrackDel", n[n.TrackBefore = 2] = "TrackBefore", n[n.TrackAfter = 3] = "TrackAfter", n;
})(fe || (fe = {}));
class ot {
  // Sections are encoded as pairs of integers. The first is the
  // length in the current document, and the second is -1 for
  // unaffected sections, and the length of the replacement content
  // otherwise. So an insertion would be (0, n>0), a deletion (n>0,
  // 0), and a replacement two positive numbers.
  /**
  @internal
  */
  constructor(e) {
    this.sections = e;
  }
  /**
  The length of the document before the change.
  */
  get length() {
    let e = 0;
    for (let t = 0; t < this.sections.length; t += 2)
      e += this.sections[t];
    return e;
  }
  /**
  The length of the document after the change.
  */
  get newLength() {
    let e = 0;
    for (let t = 0; t < this.sections.length; t += 2) {
      let i = this.sections[t + 1];
      e += i < 0 ? this.sections[t] : i;
    }
    return e;
  }
  /**
  False when there are actual changes in this set.
  */
  get empty() {
    return this.sections.length == 0 || this.sections.length == 2 && this.sections[1] < 0;
  }
  /**
  Iterate over the unchanged parts left by these changes. `posA`
  provides the position of the range in the old document, `posB`
  the new position in the changed document.
  */
  iterGaps(e) {
    for (let t = 0, i = 0, s = 0; t < this.sections.length; ) {
      let r = this.sections[t++], o = this.sections[t++];
      o < 0 ? (e(i, s, r), s += r) : s += o, i += r;
    }
  }
  /**
  Iterate over the ranges changed by these changes. (See
  [`ChangeSet.iterChanges`](https://codemirror.net/6/docs/ref/#state.ChangeSet.iterChanges) for a
  variant that also provides you with the inserted text.)
  `fromA`/`toA` provides the extent of the change in the starting
  document, `fromB`/`toB` the extent of the replacement in the
  changed document.
  
  When `individual` is true, adjacent changes (which are kept
  separate for [position mapping](https://codemirror.net/6/docs/ref/#state.ChangeDesc.mapPos)) are
  reported separately.
  */
  iterChangedRanges(e, t = !1) {
    lr(this, e, t);
  }
  /**
  Get a description of the inverted form of these changes.
  */
  get invertedDesc() {
    let e = [];
    for (let t = 0; t < this.sections.length; ) {
      let i = this.sections[t++], s = this.sections[t++];
      s < 0 ? e.push(i, s) : e.push(s, i);
    }
    return new ot(e);
  }
  /**
  Compute the combined effect of applying another set of changes
  after this one. The length of the document after this set should
  match the length before `other`.
  */
  composeDesc(e) {
    return this.empty ? e : e.empty ? this : Ha(this, e);
  }
  /**
  Map this description, which should start with the same document
  as `other`, over another set of changes, so that it can be
  applied after it. When `before` is true, map as if the changes
  in `this` happened before the ones in `other`.
  */
  mapDesc(e, t = !1) {
    return e.empty ? this : ar(this, e, t);
  }
  mapPos(e, t = -1, i = fe.Simple) {
    let s = 0, r = 0;
    for (let o = 0; o < this.sections.length; ) {
      let l = this.sections[o++], a = this.sections[o++], h = s + l;
      if (a < 0) {
        if (h > e)
          return r + (e - s);
        r += l;
      } else {
        if (i != fe.Simple && h >= e && (i == fe.TrackDel && s < e && h > e || i == fe.TrackBefore && s < e || i == fe.TrackAfter && h > e))
          return null;
        if (h > e || h == e && t < 0 && !l)
          return e == s || t < 0 ? r : r + a;
        r += a;
      }
      s = h;
    }
    if (e > s)
      throw new RangeError(`Position ${e} is out of range for changeset of length ${s}`);
    return r;
  }
  /**
  Check whether these changes touch a given range. When one of the
  changes entirely covers the range, the string `"cover"` is
  returned.
  */
  touchesRange(e, t = e) {
    for (let i = 0, s = 0; i < this.sections.length && s <= t; ) {
      let r = this.sections[i++], o = this.sections[i++], l = s + r;
      if (o >= 0 && s <= t && l >= e)
        return s < e && l > t ? "cover" : !0;
      s = l;
    }
    return !1;
  }
  /**
  @internal
  */
  toString() {
    let e = "";
    for (let t = 0; t < this.sections.length; ) {
      let i = this.sections[t++], s = this.sections[t++];
      e += (e ? " " : "") + i + (s >= 0 ? ":" + s : "");
    }
    return e;
  }
  /**
  Serialize this change desc to a JSON-representable value.
  */
  toJSON() {
    return this.sections;
  }
  /**
  Create a change desc from its JSON representation (as produced
  by [`toJSON`](https://codemirror.net/6/docs/ref/#state.ChangeDesc.toJSON).
  */
  static fromJSON(e) {
    if (!Array.isArray(e) || e.length % 2 || e.some((t) => typeof t != "number"))
      throw new RangeError("Invalid JSON representation of ChangeDesc");
    return new ot(e);
  }
  /**
  @internal
  */
  static create(e) {
    return new ot(e);
  }
}
class ne extends ot {
  constructor(e, t) {
    super(e), this.inserted = t;
  }
  /**
  Apply the changes to a document, returning the modified
  document.
  */
  apply(e) {
    if (this.length != e.length)
      throw new RangeError("Applying change set to a document with the wrong length");
    return lr(this, (t, i, s, r, o) => e = e.replace(s, s + (i - t), o), !1), e;
  }
  mapDesc(e, t = !1) {
    return ar(this, e, t, !0);
  }
  /**
  Given the document as it existed _before_ the changes, return a
  change set that represents the inverse of this set, which could
  be used to go from the document created by the changes back to
  the document as it existed before the changes.
  */
  invert(e) {
    let t = this.sections.slice(), i = [];
    for (let s = 0, r = 0; s < t.length; s += 2) {
      let o = t[s], l = t[s + 1];
      if (l >= 0) {
        t[s] = l, t[s + 1] = o;
        let a = s >> 1;
        for (; i.length < a; )
          i.push(z.empty);
        i.push(o ? e.slice(r, r + o) : z.empty);
      }
      r += o;
    }
    return new ne(t, i);
  }
  /**
  Combine two subsequent change sets into a single set. `other`
  must start in the document produced by `this`. If `this` goes
  `docA` → `docB` and `other` represents `docB` → `docC`, the
  returned value will represent the change `docA` → `docC`.
  */
  compose(e) {
    return this.empty ? e : e.empty ? this : Ha(this, e, !0);
  }
  /**
  Given another change set starting in the same document, maps this
  change set over the other, producing a new change set that can be
  applied to the document produced by applying `other`. When
  `before` is `true`, order changes as if `this` comes before
  `other`, otherwise (the default) treat `other` as coming first.
  
  Given two changes `A` and `B`, `A.compose(B.map(A))` and
  `B.compose(A.map(B, true))` will produce the same document. This
  provides a basic form of [operational
  transformation](https://en.wikipedia.org/wiki/Operational_transformation),
  and can be used for collaborative editing.
  */
  map(e, t = !1) {
    return e.empty ? this : ar(this, e, t, !0);
  }
  /**
  Iterate over the changed ranges in the document, calling `f` for
  each, with the range in the original document (`fromA`-`toA`)
  and the range that replaces it in the new document
  (`fromB`-`toB`).
  
  When `individual` is true, adjacent changes are reported
  separately.
  */
  iterChanges(e, t = !1) {
    lr(this, e, t);
  }
  /**
  Get a [change description](https://codemirror.net/6/docs/ref/#state.ChangeDesc) for this change
  set.
  */
  get desc() {
    return ot.create(this.sections);
  }
  /**
  @internal
  */
  filter(e) {
    let t = [], i = [], s = [], r = new Ii(this);
    e: for (let o = 0, l = 0; ; ) {
      let a = o == e.length ? 1e9 : e[o++];
      for (; l < a || l == a && r.len == 0; ) {
        if (r.done)
          break e;
        let c = Math.min(r.len, a - l);
        ge(s, c, -1);
        let f = r.ins == -1 ? -1 : r.off == 0 ? r.ins : 0;
        ge(t, c, f), f > 0 && bt(i, t, r.text), r.forward(c), l += c;
      }
      let h = e[o++];
      for (; l < h; ) {
        if (r.done)
          break e;
        let c = Math.min(r.len, h - l);
        ge(t, c, -1), ge(s, c, r.ins == -1 ? -1 : r.off == 0 ? r.ins : 0), r.forward(c), l += c;
      }
    }
    return {
      changes: new ne(t, i),
      filtered: ot.create(s)
    };
  }
  /**
  Serialize this change set to a JSON-representable value.
  */
  toJSON() {
    let e = [];
    for (let t = 0; t < this.sections.length; t += 2) {
      let i = this.sections[t], s = this.sections[t + 1];
      s < 0 ? e.push(i) : s == 0 ? e.push([i]) : e.push([i].concat(this.inserted[t >> 1].toJSON()));
    }
    return e;
  }
  /**
  Create a change set for the given changes, for a document of the
  given length, using `lineSep` as line separator.
  */
  static of(e, t, i) {
    let s = [], r = [], o = 0, l = null;
    function a(c = !1) {
      if (!c && !s.length)
        return;
      o < t && ge(s, t - o, -1);
      let f = new ne(s, r);
      l = l ? l.compose(f.map(l)) : f, s = [], r = [], o = 0;
    }
    function h(c) {
      if (Array.isArray(c))
        for (let f of c)
          h(f);
      else if (c instanceof ne) {
        if (c.length != t)
          throw new RangeError(`Mismatched change set length (got ${c.length}, expected ${t})`);
        a(), l = l ? l.compose(c.map(l)) : c;
      } else {
        let { from: f, to: u = f, insert: d } = c;
        if (f > u || f < 0 || u > t)
          throw new RangeError(`Invalid change range ${f} to ${u} (in doc of length ${t})`);
        let p = d ? typeof d == "string" ? z.of(d.split(i || or)) : d : z.empty, m = p.length;
        if (f == u && m == 0)
          return;
        f < o && a(), f > o && ge(s, f - o, -1), ge(s, u - f, m), bt(r, s, p), o = u;
      }
    }
    return h(e), a(!l), l;
  }
  /**
  Create an empty changeset of the given length.
  */
  static empty(e) {
    return new ne(e ? [e, -1] : [], []);
  }
  /**
  Create a changeset from its JSON representation (as produced by
  [`toJSON`](https://codemirror.net/6/docs/ref/#state.ChangeSet.toJSON).
  */
  static fromJSON(e) {
    if (!Array.isArray(e))
      throw new RangeError("Invalid JSON representation of ChangeSet");
    let t = [], i = [];
    for (let s = 0; s < e.length; s++) {
      let r = e[s];
      if (typeof r == "number")
        t.push(r, -1);
      else {
        if (!Array.isArray(r) || typeof r[0] != "number" || r.some((o, l) => l && typeof o != "string"))
          throw new RangeError("Invalid JSON representation of ChangeSet");
        if (r.length == 1)
          t.push(r[0], 0);
        else {
          for (; i.length < s; )
            i.push(z.empty);
          i[s] = z.of(r.slice(1)), t.push(r[0], i[s].length);
        }
      }
    }
    return new ne(t, i);
  }
  /**
  @internal
  */
  static createSet(e, t) {
    return new ne(e, t);
  }
}
function ge(n, e, t, i = !1) {
  if (e == 0 && t <= 0)
    return;
  let s = n.length - 2;
  s >= 0 && t <= 0 && t == n[s + 1] ? n[s] += e : s >= 0 && e == 0 && n[s] == 0 ? n[s + 1] += t : i ? (n[s] += e, n[s + 1] += t) : n.push(e, t);
}
function bt(n, e, t) {
  if (t.length == 0)
    return;
  let i = e.length - 2 >> 1;
  if (i < n.length)
    n[n.length - 1] = n[n.length - 1].append(t);
  else {
    for (; n.length < i; )
      n.push(z.empty);
    n.push(t);
  }
}
function lr(n, e, t) {
  let i = n.inserted;
  for (let s = 0, r = 0, o = 0; o < n.sections.length; ) {
    let l = n.sections[o++], a = n.sections[o++];
    if (a < 0)
      s += l, r += l;
    else {
      let h = s, c = r, f = z.empty;
      for (; h += l, c += a, a && i && (f = f.append(i[o - 2 >> 1])), !(t || o == n.sections.length || n.sections[o + 1] < 0); )
        l = n.sections[o++], a = n.sections[o++];
      e(s, h, r, c, f), s = h, r = c;
    }
  }
}
function ar(n, e, t, i = !1) {
  let s = [], r = i ? [] : null, o = new Ii(n), l = new Ii(e);
  for (let a = -1; ; ) {
    if (o.done && l.len || l.done && o.len)
      throw new Error("Mismatched change set lengths");
    if (o.ins == -1 && l.ins == -1) {
      let h = Math.min(o.len, l.len);
      ge(s, h, -1), o.forward(h), l.forward(h);
    } else if (l.ins >= 0 && (o.ins < 0 || a == o.i || o.off == 0 && (l.len < o.len || l.len == o.len && !t))) {
      let h = l.len;
      for (ge(s, l.ins, -1); h; ) {
        let c = Math.min(o.len, h);
        o.ins >= 0 && a < o.i && o.len <= c && (ge(s, 0, o.ins), r && bt(r, s, o.text), a = o.i), o.forward(c), h -= c;
      }
      l.next();
    } else if (o.ins >= 0) {
      let h = 0, c = o.len;
      for (; c; )
        if (l.ins == -1) {
          let f = Math.min(c, l.len);
          h += f, c -= f, l.forward(f);
        } else if (l.ins == 0 && l.len < c)
          c -= l.len, l.next();
        else
          break;
      ge(s, h, a < o.i ? o.ins : 0), r && a < o.i && bt(r, s, o.text), a = o.i, o.forward(o.len - c);
    } else {
      if (o.done && l.done)
        return r ? ne.createSet(s, r) : ot.create(s);
      throw new Error("Mismatched change set lengths");
    }
  }
}
function Ha(n, e, t = !1) {
  let i = [], s = t ? [] : null, r = new Ii(n), o = new Ii(e);
  for (let l = !1; ; ) {
    if (r.done && o.done)
      return s ? ne.createSet(i, s) : ot.create(i);
    if (r.ins == 0)
      ge(i, r.len, 0, l), r.next();
    else if (o.len == 0 && !o.done)
      ge(i, 0, o.ins, l), s && bt(s, i, o.text), o.next();
    else {
      if (r.done || o.done)
        throw new Error("Mismatched change set lengths");
      {
        let a = Math.min(r.len2, o.len), h = i.length;
        if (r.ins == -1) {
          let c = o.ins == -1 ? -1 : o.off ? 0 : o.ins;
          ge(i, a, c, l), s && c && bt(s, i, o.text);
        } else o.ins == -1 ? (ge(i, r.off ? 0 : r.len, a, l), s && bt(s, i, r.textBit(a))) : (ge(i, r.off ? 0 : r.len, o.off ? 0 : o.ins, l), s && !o.off && bt(s, i, o.text));
        l = (r.ins > a || o.ins >= 0 && o.len > a) && (l || i.length > h), r.forward2(a), o.forward(a);
      }
    }
  }
}
class Ii {
  constructor(e) {
    this.set = e, this.i = 0, this.next();
  }
  next() {
    let { sections: e } = this.set;
    this.i < e.length ? (this.len = e[this.i++], this.ins = e[this.i++]) : (this.len = 0, this.ins = -2), this.off = 0;
  }
  get done() {
    return this.ins == -2;
  }
  get len2() {
    return this.ins < 0 ? this.len : this.ins;
  }
  get text() {
    let { inserted: e } = this.set, t = this.i - 2 >> 1;
    return t >= e.length ? z.empty : e[t];
  }
  textBit(e) {
    let { inserted: t } = this.set, i = this.i - 2 >> 1;
    return i >= t.length && !e ? z.empty : t[i].slice(this.off, e == null ? void 0 : this.off + e);
  }
  forward(e) {
    e == this.len ? this.next() : (this.len -= e, this.off += e);
  }
  forward2(e) {
    this.ins == -1 ? this.forward(e) : e == this.ins ? this.next() : (this.ins -= e, this.off += e);
  }
}
class gt {
  constructor(e, t, i, s) {
    this.from = e, this.to = t, this.flags = i, this.goalColumn = s;
  }
  /**
  The anchor of the range—the side that doesn't move when you
  extend it.
  */
  get anchor() {
    return this.flags & 32 ? this.to : this.from;
  }
  /**
  The head of the range, which is moved when the range is
  [extended](https://codemirror.net/6/docs/ref/#state.SelectionRange.extend).
  */
  get head() {
    return this.flags & 32 ? this.from : this.to;
  }
  /**
  True when `anchor` and `head` are at the same position.
  */
  get empty() {
    return this.from == this.to;
  }
  /**
  If this is a cursor that is explicitly associated with the
  character on one of its sides, this returns the side. -1 means
  the character before its position, 1 the character after, and 0
  means no association.
  */
  get assoc() {
    return this.flags & 8 ? -1 : this.flags & 16 ? 1 : 0;
  }
  /**
  A flag that, when set, makes some selection-extending commands
  treat the range's head and anchor as exchangeable, so that for
  example Shift-ArrowUp will make the lower side of the selection
  the anchor, even if that was the head before. Used to implement
  MacOS-style undirectional selections.
  */
  get undirectional() {
    return (this.flags & 64) > 0;
  }
  /**
  The bidirectional text level associated with this cursor, if
  any.
  */
  get bidiLevel() {
    let e = this.flags & 7;
    return e == 7 ? null : e;
  }
  /**
  Map this range through a change, producing a valid range in the
  updated document.
  */
  map(e, t = -1) {
    let i, s;
    return this.empty ? i = s = e.mapPos(this.from, t) : (i = e.mapPos(this.from, 1), s = e.mapPos(this.to, -1)), i == this.from && s == this.to ? this : new gt(i, s, this.flags, this.goalColumn);
  }
  /**
  Extend this range to cover at least `from` to `to`.
  */
  extend(e, t = e, i = 0) {
    if (e <= this.anchor && t >= this.anchor)
      return b.range(e, t, void 0, void 0, i);
    let s = Math.abs(e - this.anchor) > Math.abs(t - this.anchor) ? e : t;
    return b.range(this.anchor, s, void 0, void 0, i);
  }
  /**
  Compare this range to another range.
  */
  eq(e, t = !1) {
    return this.anchor == e.anchor && this.head == e.head && this.goalColumn == e.goalColumn && (!t || !this.empty || this.assoc == e.assoc);
  }
  /**
  Return a JSON-serializable object representing the range.
  */
  toJSON() {
    return { anchor: this.anchor, head: this.head };
  }
  /**
  Convert a JSON representation of a range to a `SelectionRange`
  instance.
  */
  static fromJSON(e) {
    if (!e || typeof e.anchor != "number" || typeof e.head != "number")
      throw new RangeError("Invalid JSON representation for SelectionRange");
    return b.range(e.anchor, e.head);
  }
  /**
  @internal
  */
  static create(e, t, i, s) {
    return new gt(e, t, i, s);
  }
}
class b {
  constructor(e, t) {
    this.ranges = e, this.mainIndex = t;
  }
  /**
  Map a selection through a change. Used to adjust the selection
  position for changes.
  */
  map(e, t = -1) {
    return e.empty ? this : b.create(this.ranges.map((i) => i.map(e, t)), this.mainIndex);
  }
  /**
  Compare this selection to another selection. By default, ranges
  are compared only by position. When `includeAssoc` is true,
  cursor ranges must also have the same
  [`assoc`](https://codemirror.net/6/docs/ref/#state.SelectionRange.assoc) value.
  */
  eq(e, t = !1) {
    if (this.ranges.length != e.ranges.length || this.mainIndex != e.mainIndex)
      return !1;
    for (let i = 0; i < this.ranges.length; i++)
      if (!this.ranges[i].eq(e.ranges[i], t))
        return !1;
    return !0;
  }
  /**
  Get the primary selection range. Usually, you should make sure
  your code applies to _all_ ranges, by using methods like
  [`changeByRange`](https://codemirror.net/6/docs/ref/#state.EditorState.changeByRange).
  */
  get main() {
    return this.ranges[this.mainIndex];
  }
  /**
  Make sure the selection only has one range. Returns a selection
  holding only the main range from this selection.
  */
  asSingle() {
    return this.ranges.length == 1 ? this : new b([this.main], 0);
  }
  /**
  Extend this selection with an extra range.
  */
  addRange(e, t = !0) {
    return b.create([e].concat(this.ranges), t ? 0 : this.mainIndex + 1);
  }
  /**
  Replace a given range with another range, and then normalize the
  selection to merge and sort ranges if necessary.
  */
  replaceRange(e, t = this.mainIndex) {
    let i = this.ranges.slice();
    return i[t] = e, b.create(i, this.mainIndex);
  }
  /**
  Convert this selection to an object that can be serialized to
  JSON.
  */
  toJSON() {
    return { ranges: this.ranges.map((e) => e.toJSON()), main: this.mainIndex };
  }
  /**
  Create a selection from a JSON representation.
  */
  static fromJSON(e) {
    if (!e || !Array.isArray(e.ranges) || typeof e.main != "number" || e.main >= e.ranges.length)
      throw new RangeError("Invalid JSON representation for EditorSelection");
    return new b(e.ranges.map((t) => gt.fromJSON(t)), e.main);
  }
  /**
  Create a selection holding a single range.
  */
  static single(e, t = e) {
    return new b([b.range(e, t)], 0);
  }
  /**
  Sort and merge the given set of ranges, creating a valid
  selection.
  */
  static create(e, t = 0) {
    if (e.length == 0)
      throw new RangeError("A selection needs at least one range");
    for (let i = 0, s = 0; s < e.length; s++) {
      let r = e[s];
      if (r.empty ? r.from <= i : r.from < i)
        return b.normalized(e.slice(), t);
      i = r.to;
    }
    return new b(e, t);
  }
  /**
  Create a cursor selection range at the given position. You can
  safely ignore the optional arguments in most situations.
  */
  static cursor(e, t = 0, i, s) {
    return gt.create(e, e, (t == 0 ? 0 : t < 0 ? 8 : 16) | (i == null ? 7 : Math.min(6, i)), s);
  }
  /**
  Create a selection range.
  */
  static range(e, t, i, s, r) {
    let o = s == null ? 7 : Math.min(6, s);
    return !r && e != t && (r = t < e ? 1 : -1), r && (o |= r < 0 ? 8 : 16), t < e ? gt.create(t, e, o | 32, i) : gt.create(e, t, o, i);
  }
  /**
  Create an [undirectional](https://codemirror.net/6/docs/ref/#state.SelectionRange.undirectional)
  selection range.
  */
  static undirectionalRange(e, t) {
    return gt.create(e, t, 64, void 0);
  }
  /**
  @internal
  */
  static normalized(e, t = 0) {
    let i = e[t];
    e.sort((s, r) => s.from - r.from), t = e.indexOf(i);
    for (let s = 1; s < e.length; s++) {
      let r = e[s], o = e[s - 1];
      if (r.empty ? r.from <= o.to : r.from < o.to) {
        let l = o.from, a = Math.max(r.to, o.to);
        s <= t && t--, e.splice(--s, 2, r.anchor > r.head ? b.range(a, l) : b.range(l, a));
      }
    }
    return new b(e, t);
  }
}
function Fa(n, e) {
  for (let t of n.ranges)
    if (t.to > e)
      throw new RangeError("Selection points outside of document");
}
let eo = 0;
class T {
  constructor(e, t, i, s, r) {
    this.combine = e, this.compareInput = t, this.compare = i, this.isStatic = s, this.id = eo++, this.default = e([]), this.extensions = typeof r == "function" ? r(this) : r;
  }
  /**
  Returns a facet reader for this facet, which can be used to
  [read](https://codemirror.net/6/docs/ref/#state.EditorState.facet) it but not to define values for it.
  */
  get reader() {
    return this;
  }
  /**
  Define a new facet.
  */
  static define(e = {}) {
    return new T(e.combine || ((t) => t), e.compareInput || ((t, i) => t === i), e.compare || (e.combine ? (t, i) => t === i : to), !!e.static, e.enables);
  }
  /**
  Returns an extension that adds the given value to this facet.
  */
  of(e) {
    return new Pn([], this, 0, e);
  }
  /**
  Create an extension that computes a value for the facet from a
  state. You must take care to declare the parts of the state that
  this value depends on, since your function is only called again
  for a new state when one of those parts changed.
  
  In cases where your value depends only on a single field, you'll
  want to use the [`from`](https://codemirror.net/6/docs/ref/#state.Facet.from) method instead.
  */
  compute(e, t) {
    if (this.isStatic)
      throw new Error("Can't compute a static facet");
    return new Pn(e, this, 1, t);
  }
  /**
  Create an extension that computes zero or more values for this
  facet from a state.
  */
  computeN(e, t) {
    if (this.isStatic)
      throw new Error("Can't compute a static facet");
    return new Pn(e, this, 2, t);
  }
  from(e, t) {
    return t || (t = (i) => i), this.compute([e], (i) => t(i.field(e)));
  }
}
function to(n, e) {
  return n == e || n.length == e.length && n.every((t, i) => t === e[i]);
}
class Pn {
  constructor(e, t, i, s) {
    this.dependencies = e, this.facet = t, this.type = i, this.value = s, this.id = eo++;
  }
  dynamicSlot(e) {
    var t;
    let i = this.value, s = this.facet.compareInput, r = this.id, o = e[r] >> 1, l = this.type == 2, a = !1, h = !1, c = [];
    for (let f of this.dependencies)
      f == "doc" ? a = !0 : f == "selection" ? h = !0 : (((t = e[f.id]) !== null && t !== void 0 ? t : 1) & 1) == 0 && c.push(e[f.id]);
    return {
      create(f) {
        return f.values[o] = i(f), 1;
      },
      update(f, u) {
        if (a && u.docChanged || h && (u.docChanged || u.selection) || hr(f, c)) {
          let d = i(f);
          if (l ? !Ko(d, f.values[o], s) : !s(d, f.values[o]))
            return f.values[o] = d, 1;
        }
        return 0;
      },
      reconfigure: (f, u) => {
        let d, p = u.config.address[r];
        if (p != null) {
          let m = qn(u, p);
          if (this.dependencies.every((g) => g instanceof T ? u.facet(g) === f.facet(g) : g instanceof me ? u.field(g, !1) == f.field(g, !1) : !0) || (l ? Ko(d = i(f), m, s) : s(d = i(f), m)))
            return f.values[o] = m, 0;
        } else
          d = i(f);
        return f.values[o] = d, 1;
      }
    };
  }
}
function Ko(n, e, t) {
  if (n.length != e.length)
    return !1;
  for (let i = 0; i < n.length; i++)
    if (!t(n[i], e[i]))
      return !1;
  return !0;
}
function hr(n, e) {
  let t = !1;
  for (let i of e)
    Di(n, i) & 1 && (t = !0);
  return t;
}
function jf(n, e, t) {
  let i = t.map((a) => n[a.id]), s = t.map((a) => a.type), r = i.filter((a) => !(a & 1)), o = n[e.id] >> 1;
  function l(a) {
    let h = [];
    for (let c = 0; c < i.length; c++) {
      let f = qn(a, i[c]);
      if (s[c] == 2)
        for (let u of f)
          h.push(u);
      else
        h.push(f);
    }
    return e.combine(h);
  }
  return {
    create(a) {
      for (let h of i)
        Di(a, h);
      return a.values[o] = l(a), 1;
    },
    update(a, h) {
      if (!hr(a, r))
        return 0;
      let c = l(a);
      return e.compare(c, a.values[o]) ? 0 : (a.values[o] = c, 1);
    },
    reconfigure(a, h) {
      let c = hr(a, i), f = h.config.facets[e.id], u = h.facet(e);
      if (f && !c && to(t, f))
        return a.values[o] = u, 0;
      let d = l(a);
      return e.compare(d, u) ? (a.values[o] = u, 0) : (a.values[o] = d, 1);
    }
  };
}
const cn = /* @__PURE__ */ T.define({ static: !0 });
class me {
  constructor(e, t, i, s, r) {
    this.id = e, this.createF = t, this.updateF = i, this.compareF = s, this.spec = r, this.provides = void 0;
  }
  /**
  Define a state field.
  */
  static define(e) {
    let t = new me(eo++, e.create, e.update, e.compare || ((i, s) => i === s), e);
    return e.provide && (t.provides = e.provide(t)), t;
  }
  create(e) {
    let t = e.facet(cn).find((i) => i.field == this);
    return (t?.create || this.createF)(e);
  }
  /**
  @internal
  */
  slot(e) {
    let t = e[this.id] >> 1;
    return {
      create: (i) => (i.values[t] = this.create(i), 1),
      update: (i, s) => {
        let r = i.values[t], o = this.updateF(r, s);
        return this.compareF(r, o) ? 0 : (i.values[t] = o, 1);
      },
      reconfigure: (i, s) => {
        let r = i.facet(cn), o = s.facet(cn), l;
        return (l = r.find((a) => a.field == this)) && l != o.find((a) => a.field == this) ? (i.values[t] = l.create(i), 1) : s.config.address[this.id] != null ? (i.values[t] = s.field(this), 0) : (i.values[t] = this.create(i), 1);
      }
    };
  }
  /**
  Returns an extension that enables this field and overrides the
  way it is initialized. Can be useful when you need to provide a
  non-default starting value for the field.
  */
  init(e) {
    return [this, cn.of({ field: this, create: e })];
  }
  /**
  State field instances can be used as
  [`Extension`](https://codemirror.net/6/docs/ref/#state.Extension) values to enable the field in a
  given state.
  */
  get extension() {
    return this;
  }
}
const Pt = { lowest: 4, low: 3, default: 2, high: 1, highest: 0 };
function xi(n) {
  return (e) => new Va(e, n);
}
const Tt = {
  /**
  The highest precedence level, for extensions that should end up
  near the start of the precedence ordering.
  */
  highest: /* @__PURE__ */ xi(Pt.highest),
  /**
  A higher-than-default precedence, for extensions that should
  come before those with default precedence.
  */
  high: /* @__PURE__ */ xi(Pt.high),
  /**
  The default precedence, which is also used for extensions
  without an explicit precedence.
  */
  default: /* @__PURE__ */ xi(Pt.default),
  /**
  A lower-than-default precedence.
  */
  low: /* @__PURE__ */ xi(Pt.low),
  /**
  The lowest precedence level. Meant for things that should end up
  near the end of the extension order.
  */
  lowest: /* @__PURE__ */ xi(Pt.lowest)
};
class Va {
  constructor(e, t) {
    this.inner = e, this.prec = t;
  }
}
class fs {
  /**
  Create an instance of this compartment to add to your [state
  configuration](https://codemirror.net/6/docs/ref/#state.EditorStateConfig.extensions).
  */
  of(e) {
    return new cr(this, e);
  }
  /**
  Create an [effect](https://codemirror.net/6/docs/ref/#state.TransactionSpec.effects) that
  reconfigures this compartment.
  */
  reconfigure(e) {
    return fs.reconfigure.of({ compartment: this, extension: e });
  }
  /**
  Get the current content of the compartment in the state, or
  `undefined` if it isn't present.
  */
  get(e) {
    return e.config.compartments.get(this);
  }
}
class cr {
  constructor(e, t) {
    this.compartment = e, this.inner = t;
  }
}
class zn {
  constructor(e, t, i, s, r, o) {
    for (this.base = e, this.compartments = t, this.dynamicSlots = i, this.address = s, this.staticValues = r, this.facets = o, this.statusTemplate = []; this.statusTemplate.length < i.length; )
      this.statusTemplate.push(
        0
        /* SlotStatus.Unresolved */
      );
  }
  staticFacet(e) {
    let t = this.address[e.id];
    return t == null ? e.default : this.staticValues[t >> 1];
  }
  static resolve(e, t, i) {
    let s = [], r = /* @__PURE__ */ Object.create(null), o = /* @__PURE__ */ new Map();
    for (let u of Uf(e, t, o))
      u instanceof me ? s.push(u) : (r[u.facet.id] || (r[u.facet.id] = [])).push(u);
    let l = /* @__PURE__ */ Object.create(null), a = [], h = [];
    for (let u of s)
      l[u.id] = h.length << 1, h.push((d) => u.slot(d));
    let c = i?.config.facets;
    for (let u in r) {
      let d = r[u], p = d[0].facet, m = c && c[u] || [];
      if (d.every(
        (g) => g.type == 0
        /* Provider.Static */
      ))
        if (l[p.id] = a.length << 1 | 1, to(m, d))
          a.push(i.facet(p));
        else {
          let g = p.combine(d.map((y) => y.value));
          a.push(i && p.compare(g, i.facet(p)) ? i.facet(p) : g);
        }
      else {
        for (let g of d)
          g.type == 0 ? (l[g.id] = a.length << 1 | 1, a.push(g.value)) : (l[g.id] = h.length << 1, h.push((y) => g.dynamicSlot(y)));
        l[p.id] = h.length << 1, h.push((g) => jf(g, p, d));
      }
    }
    let f = h.map((u) => u(l));
    return new zn(e, o, f, l, a, r);
  }
}
function Uf(n, e, t) {
  let i = [[], [], [], [], []], s = /* @__PURE__ */ new Map();
  function r(o, l) {
    let a = s.get(o);
    if (a != null) {
      if (a <= l)
        return;
      let h = i[a].indexOf(o);
      h > -1 && i[a].splice(h, 1), o instanceof cr && t.delete(o.compartment);
    }
    if (s.set(o, l), Array.isArray(o))
      for (let h of o)
        r(h, l);
    else if (o instanceof cr) {
      if (t.has(o.compartment))
        throw new RangeError("Duplicate use of compartment in extensions");
      let h = e.get(o.compartment) || o.inner;
      t.set(o.compartment, h), r(h, l);
    } else if (o instanceof Va)
      r(o.inner, o.prec);
    else if (o instanceof me)
      i[l].push(o), o.provides && r(o.provides, l);
    else if (o instanceof Pn)
      i[l].push(o), o.facet.extensions && r(o.facet.extensions, Pt.default);
    else {
      let h = o.extension;
      if (!h)
        throw new Error(`Unrecognized extension value in extension set (${o}). This sometimes happens because multiple instances of @codemirror/state are loaded, breaking instanceof checks.`);
      r(h, l);
    }
  }
  return r(n, Pt.default), i.reduce((o, l) => o.concat(l));
}
function Di(n, e) {
  if (e & 1)
    return 2;
  let t = e >> 1, i = n.status[t];
  if (i == 4)
    throw new Error("Cyclic dependency between fields and/or facets");
  if (i & 2)
    return i;
  n.status[t] = 4;
  let s = n.computeSlot(n, n.config.dynamicSlots[t]);
  return n.status[t] = 2 | s;
}
function qn(n, e) {
  return e & 1 ? n.config.staticValues[e >> 1] : n.values[e >> 1];
}
const za = /* @__PURE__ */ T.define(), fr = /* @__PURE__ */ T.define({
  combine: (n) => n.some((e) => e),
  static: !0
}), qa = /* @__PURE__ */ T.define({
  combine: (n) => n.length ? n[0] : void 0,
  static: !0
}), $a = /* @__PURE__ */ T.define(), Ka = /* @__PURE__ */ T.define(), ja = /* @__PURE__ */ T.define(), Ua = /* @__PURE__ */ T.define({
  combine: (n) => n.length ? n[0] : !1
});
class lt {
  /**
  @internal
  */
  constructor(e, t) {
    this.type = e, this.value = t;
  }
  /**
  Define a new type of annotation.
  */
  static define() {
    return new _f();
  }
}
class _f {
  /**
  Create an instance of this annotation.
  */
  of(e) {
    return new lt(this, e);
  }
}
class Gf {
  /**
  @internal
  */
  constructor(e) {
    this.map = e;
  }
  /**
  Create a [state effect](https://codemirror.net/6/docs/ref/#state.StateEffect) instance of this
  type.
  */
  of(e) {
    return new R(this, e);
  }
}
class R {
  /**
  @internal
  */
  constructor(e, t) {
    this.type = e, this.value = t;
  }
  /**
  Map this effect through a position mapping. Will return
  `undefined` when that ends up deleting the effect.
  */
  map(e) {
    let t = this.type.map(this.value, e);
    return t === void 0 ? void 0 : t == this.value ? this : new R(this.type, t);
  }
  /**
  Tells you whether this effect object is of a given
  [type](https://codemirror.net/6/docs/ref/#state.StateEffectType).
  */
  is(e) {
    return this.type == e;
  }
  /**
  Define a new effect type. The type parameter indicates the type
  of values that his effect holds. It should be a type that
  doesn't include `undefined`, since that is used in
  [mapping](https://codemirror.net/6/docs/ref/#state.StateEffect.map) to indicate that an effect is
  removed.
  */
  static define(e = {}) {
    return new Gf(e.map || ((t) => t));
  }
  /**
  Map an array of effects through a change set.
  */
  static mapEffects(e, t) {
    if (!e.length)
      return e;
    let i = [];
    for (let s of e) {
      let r = s.map(t);
      r && i.push(r);
    }
    return i;
  }
}
R.reconfigure = /* @__PURE__ */ R.define();
R.appendConfig = /* @__PURE__ */ R.define();
class ie {
  constructor(e, t, i, s, r, o) {
    this.startState = e, this.changes = t, this.selection = i, this.effects = s, this.annotations = r, this.scrollIntoView = o, this._doc = null, this._state = null, i && Fa(i, t.newLength), r.some((l) => l.type == ie.time) || (this.annotations = r.concat(ie.time.of(Date.now())));
  }
  /**
  @internal
  */
  static create(e, t, i, s, r, o) {
    return new ie(e, t, i, s, r, o);
  }
  /**
  The new document produced by the transaction. Contrary to
  [`.state`](https://codemirror.net/6/docs/ref/#state.Transaction.state)`.doc`, accessing this won't
  force the entire new state to be computed right away, so it is
  recommended that [transaction
  filters](https://codemirror.net/6/docs/ref/#state.EditorState^transactionFilter) use this getter
  when they need to look at the new document.
  */
  get newDoc() {
    return this._doc || (this._doc = this.changes.apply(this.startState.doc));
  }
  /**
  The new selection produced by the transaction. If
  [`this.selection`](https://codemirror.net/6/docs/ref/#state.Transaction.selection) is undefined,
  this will [map](https://codemirror.net/6/docs/ref/#state.EditorSelection.map) the start state's
  current selection through the changes made by the transaction.
  */
  get newSelection() {
    return this.selection || this.startState.selection.map(this.changes);
  }
  /**
  The new state created by the transaction. Computed on demand
  (but retained for subsequent access), so it is recommended not to
  access it in [transaction
  filters](https://codemirror.net/6/docs/ref/#state.EditorState^transactionFilter) when possible.
  */
  get state() {
    return this._state || this.startState.applyTransaction(this), this._state;
  }
  /**
  Get the value of the given annotation type, if any.
  */
  annotation(e) {
    for (let t of this.annotations)
      if (t.type == e)
        return t.value;
  }
  /**
  Indicates whether the transaction changed the document.
  */
  get docChanged() {
    return !this.changes.empty;
  }
  /**
  Indicates whether this transaction reconfigures the state
  (through a [configuration compartment](https://codemirror.net/6/docs/ref/#state.Compartment) or
  with a top-level configuration
  [effect](https://codemirror.net/6/docs/ref/#state.StateEffect^reconfigure).
  */
  get reconfigured() {
    return this.startState.config != this.state.config;
  }
  /**
  Returns true if the transaction has a [user
  event](https://codemirror.net/6/docs/ref/#state.Transaction^userEvent) annotation that is equal to
  or more specific than `event`. For example, if the transaction
  has `"select.pointer"` as user event, `"select"` and
  `"select.pointer"` will match it.
  */
  isUserEvent(e) {
    let t = this.annotation(ie.userEvent);
    return !!(t && (t == e || t.length > e.length && t.slice(0, e.length) == e && t[e.length] == "."));
  }
}
ie.time = /* @__PURE__ */ lt.define();
ie.userEvent = /* @__PURE__ */ lt.define();
ie.addToHistory = /* @__PURE__ */ lt.define();
ie.remote = /* @__PURE__ */ lt.define();
function Yf(n, e) {
  let t = [];
  for (let i = 0, s = 0; ; ) {
    let r, o;
    if (i < n.length && (s == e.length || e[s] >= n[i]))
      r = n[i++], o = n[i++];
    else if (s < e.length)
      r = e[s++], o = e[s++];
    else
      return t;
    !t.length || t[t.length - 1] < r ? t.push(r, o) : t[t.length - 1] < o && (t[t.length - 1] = o);
  }
}
function _a(n, e, t) {
  var i;
  let s, r, o;
  return t ? (s = e.changes, r = ne.empty(e.changes.length), o = n.changes.compose(e.changes)) : (s = e.changes.map(n.changes), r = n.changes.mapDesc(e.changes, !0), o = n.changes.compose(s)), {
    changes: o,
    selection: e.selection ? e.selection.map(r) : (i = n.selection) === null || i === void 0 ? void 0 : i.map(s),
    effects: R.mapEffects(n.effects, s).concat(R.mapEffects(e.effects, r)),
    annotations: n.annotations.length ? n.annotations.concat(e.annotations) : e.annotations,
    scrollIntoView: n.scrollIntoView || e.scrollIntoView
  };
}
function ur(n, e, t) {
  let i = e.selection, s = ti(e.annotations);
  return e.userEvent && (s = s.concat(ie.userEvent.of(e.userEvent))), {
    changes: e.changes instanceof ne ? e.changes : ne.of(e.changes || [], t, n.facet(qa)),
    selection: i && (i instanceof b ? i : b.single(i.anchor, i.head)),
    effects: ti(e.effects),
    annotations: s,
    scrollIntoView: !!e.scrollIntoView
  };
}
function Ga(n, e, t) {
  let i = ur(n, e.length ? e[0] : {}, n.doc.length);
  e.length && e[0].filter === !1 && (t = !1);
  for (let r = 1; r < e.length; r++) {
    e[r].filter === !1 && (t = !1);
    let o = !!e[r].sequential;
    i = _a(i, ur(n, e[r], o ? i.changes.newLength : n.doc.length), o);
  }
  let s = ie.create(n, i.changes, i.selection, i.effects, i.annotations, i.scrollIntoView);
  return Xf(t ? Jf(s) : s);
}
function Jf(n) {
  let e = n.startState, t = !0;
  for (let s of e.facet($a)) {
    let r = s(n);
    if (r === !1) {
      t = !1;
      break;
    }
    Array.isArray(r) && (t = t === !0 ? r : Yf(t, r));
  }
  if (t !== !0) {
    let s, r;
    if (t === !1)
      r = n.changes.invertedDesc, s = ne.empty(e.doc.length);
    else {
      let o = n.changes.filter(t);
      s = o.changes, r = o.filtered.mapDesc(o.changes).invertedDesc;
    }
    n = ie.create(e, s, n.selection && n.selection.map(r), R.mapEffects(n.effects, r), n.annotations, n.scrollIntoView);
  }
  let i = e.facet(Ka);
  for (let s = i.length - 1; s >= 0; s--) {
    let r = i[s](n);
    r instanceof ie ? n = r : Array.isArray(r) && r.length == 1 && r[0] instanceof ie ? n = r[0] : n = Ga(e, ti(r), !1);
  }
  return n;
}
function Xf(n) {
  let e = n.startState, t = e.facet(ja), i = n;
  for (let s = t.length - 1; s >= 0; s--) {
    let r = t[s](n);
    r && Object.keys(r).length && (i = _a(i, ur(e, r, n.changes.newLength), !0));
  }
  return i == n ? n : ie.create(e, n.changes, n.selection, i.effects, i.annotations, i.scrollIntoView);
}
const Qf = [];
function ti(n) {
  return n == null ? Qf : Array.isArray(n) ? n : [n];
}
var Y = /* @__PURE__ */ (function(n) {
  return n[n.Word = 0] = "Word", n[n.Space = 1] = "Space", n[n.Other = 2] = "Other", n;
})(Y || (Y = {}));
const Zf = /[\u00df\u0587\u0590-\u05f4\u0600-\u06ff\u3040-\u309f\u30a0-\u30ff\u3400-\u4db5\u4e00-\u9fcc\uac00-\ud7af]/;
let dr;
try {
  dr = /* @__PURE__ */ new RegExp("[\\p{Alphabetic}\\p{Number}_]", "u");
} catch {
}
function eu(n) {
  if (dr)
    return dr.test(n);
  for (let e = 0; e < n.length; e++) {
    let t = n[e];
    if (/\w/.test(t) || t > "" && (t.toUpperCase() != t.toLowerCase() || Zf.test(t)))
      return !0;
  }
  return !1;
}
function tu(n) {
  return (e) => {
    if (!/\S/.test(e))
      return Y.Space;
    if (eu(e))
      return Y.Word;
    for (let t = 0; t < n.length; t++)
      if (e.indexOf(n[t]) > -1)
        return Y.Word;
    return Y.Other;
  };
}
class F {
  constructor(e, t, i, s, r, o) {
    this.config = e, this.doc = t, this.selection = i, this.values = s, this.status = e.statusTemplate.slice(), this.computeSlot = r, o && (o._state = this);
    for (let l = 0; l < this.config.dynamicSlots.length; l++)
      Di(this, l << 1);
    this.computeSlot = null;
  }
  field(e, t = !0) {
    let i = this.config.address[e.id];
    if (i == null) {
      if (t)
        throw new RangeError("Field is not present in this state");
      return;
    }
    return Di(this, i), qn(this, i);
  }
  /**
  Create a [transaction](https://codemirror.net/6/docs/ref/#state.Transaction) that updates this
  state. Any number of [transaction specs](https://codemirror.net/6/docs/ref/#state.TransactionSpec)
  can be passed. Unless
  [`sequential`](https://codemirror.net/6/docs/ref/#state.TransactionSpec.sequential) is set, the
  [changes](https://codemirror.net/6/docs/ref/#state.TransactionSpec.changes) (if any) of each spec
  are assumed to start in the _current_ document (not the document
  produced by previous specs), and its
  [selection](https://codemirror.net/6/docs/ref/#state.TransactionSpec.selection) and
  [effects](https://codemirror.net/6/docs/ref/#state.TransactionSpec.effects) are assumed to refer
  to the document created by its _own_ changes. The resulting
  transaction contains the combined effect of all the different
  specs. For [selection](https://codemirror.net/6/docs/ref/#state.TransactionSpec.selection), later
  specs take precedence over earlier ones.
  */
  update(...e) {
    return Ga(this, e, !0);
  }
  /**
  @internal
  */
  applyTransaction(e) {
    let t = this.config, { base: i, compartments: s } = t;
    for (let l of e.effects)
      l.is(fs.reconfigure) ? (t && (s = /* @__PURE__ */ new Map(), t.compartments.forEach((a, h) => s.set(h, a)), t = null), s.set(l.value.compartment, l.value.extension)) : l.is(R.reconfigure) ? (t = null, i = l.value) : l.is(R.appendConfig) && (t = null, i = ti(i).concat(l.value));
    let r;
    t ? r = e.startState.values.slice() : (t = zn.resolve(i, s, this), r = new F(t, this.doc, this.selection, t.dynamicSlots.map(() => null), (a, h) => h.reconfigure(a, this), null).values);
    let o = e.startState.facet(fr) ? e.newSelection : e.newSelection.asSingle();
    new F(t, e.newDoc, o, r, (l, a) => a.update(l, e), e);
  }
  /**
  Create a [transaction spec](https://codemirror.net/6/docs/ref/#state.TransactionSpec) that
  replaces every selection range with the given content.
  */
  replaceSelection(e) {
    return typeof e == "string" && (e = this.toText(e)), this.changeByRange((t) => ({
      changes: { from: t.from, to: t.to, insert: e },
      range: b.cursor(t.from + e.length)
    }));
  }
  /**
  Create a set of changes and a new selection by running the given
  function for each range in the active selection. The function
  can return an optional set of changes (in the coordinate space
  of the start document), plus an updated range (in the coordinate
  space of the document produced by the call's own changes). This
  method will merge all the changes and ranges into a single
  changeset and selection, and return it as a [transaction
  spec](https://codemirror.net/6/docs/ref/#state.TransactionSpec), which can be passed to
  [`update`](https://codemirror.net/6/docs/ref/#state.EditorState.update).
  */
  changeByRange(e) {
    let t = this.selection, i = e(t.ranges[0]), s = this.changes(i.changes), r = [i.range], o = ti(i.effects);
    for (let l = 1; l < t.ranges.length; l++) {
      let a = e(t.ranges[l]), h = this.changes(a.changes), c = h.map(s);
      for (let u = 0; u < l; u++)
        r[u] = r[u].map(c);
      let f = s.mapDesc(h, !0);
      r.push(a.range.map(f)), s = s.compose(c), o = R.mapEffects(o, c).concat(R.mapEffects(ti(a.effects), f));
    }
    return {
      changes: s,
      selection: b.create(r, t.mainIndex),
      effects: o
    };
  }
  /**
  Create a [change set](https://codemirror.net/6/docs/ref/#state.ChangeSet) from the given change
  description, taking the state's document length and line
  separator into account.
  */
  changes(e = []) {
    return e instanceof ne ? e : ne.of(e, this.doc.length, this.facet(F.lineSeparator));
  }
  /**
  Using the state's [line
  separator](https://codemirror.net/6/docs/ref/#state.EditorState^lineSeparator), create a
  [`Text`](https://codemirror.net/6/docs/ref/#state.Text) instance from the given string.
  */
  toText(e) {
    return z.of(e.split(this.facet(F.lineSeparator) || or));
  }
  /**
  Return the given range of the document as a string.
  */
  sliceDoc(e = 0, t = this.doc.length) {
    return this.doc.sliceString(e, t, this.lineBreak);
  }
  /**
  Get the value of a state [facet](https://codemirror.net/6/docs/ref/#state.Facet).
  */
  facet(e) {
    let t = this.config.address[e.id];
    return t == null ? e.default : (Di(this, t), qn(this, t));
  }
  /**
  Convert this state to a JSON-serializable object. When custom
  fields should be serialized, you can pass them in as an object
  mapping property names (in the resulting object, which should
  not use `doc` or `selection`) to fields.
  */
  toJSON(e) {
    let t = {
      doc: this.sliceDoc(),
      selection: this.selection.toJSON()
    };
    if (e)
      for (let i in e) {
        let s = e[i];
        s instanceof me && this.config.address[s.id] != null && (t[i] = s.spec.toJSON(this.field(e[i]), this));
      }
    return t;
  }
  /**
  Deserialize a state from its JSON representation. When custom
  fields should be deserialized, pass the same object you passed
  to [`toJSON`](https://codemirror.net/6/docs/ref/#state.EditorState.toJSON) when serializing as
  third argument.
  */
  static fromJSON(e, t = {}, i) {
    if (!e || typeof e.doc != "string")
      throw new RangeError("Invalid JSON representation for EditorState");
    let s = [];
    if (i) {
      for (let r in i)
        if (Object.prototype.hasOwnProperty.call(e, r)) {
          let o = i[r], l = e[r];
          s.push(o.init((a) => o.spec.fromJSON(l, a)));
        }
    }
    return F.create({
      doc: e.doc,
      selection: b.fromJSON(e.selection),
      extensions: t.extensions ? s.concat([t.extensions]) : s
    });
  }
  /**
  Create a new state. You'll usually only need this when
  initializing an editor—updated states are created by applying
  transactions.
  */
  static create(e = {}) {
    let t = zn.resolve(e.extensions || [], /* @__PURE__ */ new Map()), i = e.doc instanceof z ? e.doc : z.of((e.doc || "").split(t.staticFacet(F.lineSeparator) || or)), s = e.selection ? e.selection instanceof b ? e.selection : b.single(e.selection.anchor, e.selection.head) : b.single(0);
    return Fa(s, i.length), t.staticFacet(fr) || (s = s.asSingle()), new F(t, i, s, t.dynamicSlots.map(() => null), (r, o) => o.create(r), null);
  }
  /**
  The size (in columns) of a tab in the document, determined by
  the [`tabSize`](https://codemirror.net/6/docs/ref/#state.EditorState^tabSize) facet.
  */
  get tabSize() {
    return this.facet(F.tabSize);
  }
  /**
  Get the proper [line-break](https://codemirror.net/6/docs/ref/#state.EditorState^lineSeparator)
  string for this state.
  */
  get lineBreak() {
    return this.facet(F.lineSeparator) || `
`;
  }
  /**
  Returns true when the editor is
  [configured](https://codemirror.net/6/docs/ref/#state.EditorState^readOnly) to be read-only.
  */
  get readOnly() {
    return this.facet(Ua);
  }
  /**
  Look up a translation for the given phrase (via the
  [`phrases`](https://codemirror.net/6/docs/ref/#state.EditorState^phrases) facet), or return the
  original string if no translation is found.
  
  If additional arguments are passed, they will be inserted in
  place of markers like `$1` (for the first value) and `$2`, etc.
  A single `$` is equivalent to `$1`, and `$$` will produce a
  literal dollar sign.
  */
  phrase(e, ...t) {
    for (let i of this.facet(F.phrases))
      if (Object.prototype.hasOwnProperty.call(i, e)) {
        e = i[e];
        break;
      }
    return t.length && (e = e.replace(/\$(\$|\d*)/g, (i, s) => {
      if (s == "$")
        return "$";
      let r = +(s || 1);
      return !r || r > t.length ? i : t[r - 1];
    })), e;
  }
  /**
  Find the values for a given language data field, provided by the
  the [`languageData`](https://codemirror.net/6/docs/ref/#state.EditorState^languageData) facet.
  
  Examples of language data fields are...
  
  - [`"commentTokens"`](https://codemirror.net/6/docs/ref/#commands.CommentTokens) for specifying
    comment syntax.
  - [`"autocomplete"`](https://codemirror.net/6/docs/ref/#autocomplete.autocompletion^config.override)
    for providing language-specific completion sources.
  - [`"wordChars"`](https://codemirror.net/6/docs/ref/#state.EditorState.charCategorizer) for adding
    characters that should be considered part of words in this
    language.
  - [`"closeBrackets"`](https://codemirror.net/6/docs/ref/#autocomplete.CloseBracketConfig) controls
    bracket closing behavior.
  */
  languageDataAt(e, t, i = -1) {
    let s = [];
    for (let r of this.facet(za))
      for (let o of r(this, t, i))
        Object.prototype.hasOwnProperty.call(o, e) && s.push(o[e]);
    return s;
  }
  /**
  Return a function that can categorize strings (expected to
  represent a single [grapheme cluster](https://codemirror.net/6/docs/ref/#state.findClusterBreak))
  into one of:
  
   - Word (contains an alphanumeric character or a character
     explicitly listed in the local language's `"wordChars"`
     language data, which should be a string)
   - Space (contains only whitespace)
   - Other (anything else)
  */
  charCategorizer(e) {
    let t = this.languageDataAt("wordChars", e);
    return tu(t.length ? t[0] : "");
  }
  /**
  Find the word at the given position, meaning the range
  containing all [word](https://codemirror.net/6/docs/ref/#state.CharCategory.Word) characters
  around it. If no word characters are adjacent to the position,
  this returns null.
  */
  wordAt(e) {
    let { text: t, from: i, length: s } = this.doc.lineAt(e), r = this.charCategorizer(e), o = e - i, l = e - i;
    for (; o > 0; ) {
      let a = oe(t, o, !1);
      if (r(t.slice(a, o)) != Y.Word)
        break;
      o = a;
    }
    for (; l < s; ) {
      let a = oe(t, l);
      if (r(t.slice(l, a)) != Y.Word)
        break;
      l = a;
    }
    return o == l ? null : b.range(o + i, l + i);
  }
}
F.allowMultipleSelections = fr;
F.tabSize = /* @__PURE__ */ T.define({
  combine: (n) => n.length ? n[0] : 4
});
F.lineSeparator = qa;
F.readOnly = Ua;
F.phrases = /* @__PURE__ */ T.define({
  compare(n, e) {
    let t = Object.keys(n), i = Object.keys(e);
    return t.length == i.length && t.every((s) => n[s] == e[s]);
  }
});
F.languageData = za;
F.changeFilter = $a;
F.transactionFilter = Ka;
F.transactionExtender = ja;
fs.reconfigure = /* @__PURE__ */ R.define();
function at(n, e, t = {}) {
  let i = {};
  for (let s of n)
    for (let r of Object.keys(s)) {
      let o = s[r], l = i[r];
      if (l === void 0)
        i[r] = o;
      else if (!(l === o || o === void 0)) if (Object.hasOwnProperty.call(t, r))
        i[r] = t[r](l, o);
      else
        throw new Error("Config merge conflict for field " + r);
    }
  for (let s in e)
    i[s] === void 0 && (i[s] = e[s]);
  return i;
}
class wt {
  /**
  Compare this value with another value. Used when comparing
  rangesets. The default implementation compares by identity.
  Unless you are only creating a fixed number of unique instances
  of your value type, it is a good idea to implement this
  properly.
  */
  eq(e) {
    return this == e;
  }
  /**
  Create a [range](https://codemirror.net/6/docs/ref/#state.Range) with this value.
  */
  range(e, t = e) {
    return pr.create(e, t, this);
  }
}
wt.prototype.startSide = wt.prototype.endSide = 0;
wt.prototype.point = !1;
wt.prototype.mapMode = fe.TrackDel;
function io(n, e) {
  return n == e || n.constructor == e.constructor && n.eq(e);
}
let pr = class Ya {
  constructor(e, t, i) {
    this.from = e, this.to = t, this.value = i;
  }
  /**
  @internal
  */
  static create(e, t, i) {
    return new Ya(e, t, i);
  }
};
function mr(n, e) {
  return n.from - e.from || n.value.startSide - e.value.startSide;
}
class no {
  constructor(e, t, i, s) {
    this.from = e, this.to = t, this.value = i, this.maxPoint = s;
  }
  get length() {
    return this.to[this.to.length - 1];
  }
  // Find the index of the given position and side. Use the ranges'
  // `from` pos when `end == false`, `to` when `end == true`.
  findIndex(e, t, i, s = 0) {
    let r = i ? this.to : this.from;
    for (let o = s, l = r.length; ; ) {
      if (o == l)
        return o;
      let a = o + l >> 1, h = r[a] - e || (i ? this.value[a].endSide : this.value[a].startSide) - t;
      if (a == o)
        return h >= 0 ? o : l;
      h >= 0 ? l = a : o = a + 1;
    }
  }
  between(e, t, i, s) {
    for (let r = this.findIndex(t, -1e9, !0), o = this.findIndex(i, 1e9, !1, r); r < o; r++)
      if (s(this.from[r] + e, this.to[r] + e, this.value[r]) === !1)
        return !1;
  }
  map(e, t) {
    let i = [], s = [], r = [], o = -1, l = -1;
    for (let a = 0; a < this.value.length; a++) {
      let h = this.value[a], c = this.from[a] + e, f = this.to[a] + e, u, d;
      if (c == f) {
        let p = t.mapPos(c, h.startSide, h.mapMode);
        if (p == null || (u = d = p, h.startSide != h.endSide && (d = t.mapPos(c, h.endSide), d < u)))
          continue;
      } else if (u = t.mapPos(c, h.startSide), d = t.mapPos(f, h.endSide), u > d || u == d && h.startSide > 0 && h.endSide <= 0)
        continue;
      (d - u || h.endSide - h.startSide) < 0 || (o < 0 && (o = u), h.point && (l = Math.max(l, d - u)), i.push(h), s.push(u - o), r.push(d - o));
    }
    return { mapped: i.length ? new no(s, r, i, l) : null, pos: o };
  }
}
class W {
  constructor(e, t, i, s) {
    this.chunkPos = e, this.chunk = t, this.nextLayer = i, this.maxPoint = s;
  }
  /**
  @internal
  */
  static create(e, t, i, s) {
    return new W(e, t, i, s);
  }
  /**
  @internal
  */
  get length() {
    let e = this.chunk.length - 1;
    return e < 0 ? 0 : Math.max(this.chunkEnd(e), this.nextLayer.length);
  }
  /**
  The number of ranges in the set.
  */
  get size() {
    if (this.isEmpty)
      return 0;
    let e = this.nextLayer.size;
    for (let t of this.chunk)
      e += t.value.length;
    return e;
  }
  /**
  @internal
  */
  chunkEnd(e) {
    return this.chunkPos[e] + this.chunk[e].length;
  }
  /**
  Update the range set, optionally adding new ranges or filtering
  out existing ones.
  
  (Note: The type parameter is just there as a kludge to work
  around TypeScript variance issues that prevented `RangeSet<X>`
  from being a subtype of `RangeSet<Y>` when `X` is a subtype of
  `Y`.)
  */
  update(e) {
    let { add: t = [], sort: i = !1, filterFrom: s = 0, filterTo: r = this.length } = e, o = e.filter;
    if (t.length == 0 && !o)
      return this;
    if (i && (t = t.slice().sort(mr)), this.isEmpty)
      return t.length ? W.of(t) : this;
    let l = new Ja(this, null, -1).goto(0), a = 0, h = [], c = new ft();
    for (; l.value || a < t.length; )
      if (a < t.length && (l.from - t[a].from || l.startSide - t[a].value.startSide) >= 0) {
        let f = t[a++];
        c.addInner(f.from, f.to, f.value) || h.push(f);
      } else l.rangeIndex == 1 && l.chunkIndex < this.chunk.length && (a == t.length || this.chunkEnd(l.chunkIndex) < t[a].from) && (!o || s > this.chunkEnd(l.chunkIndex) || r < this.chunkPos[l.chunkIndex]) && c.addChunk(this.chunkPos[l.chunkIndex], this.chunk[l.chunkIndex]) ? l.nextChunk() : ((!o || s > l.to || r < l.from || o(l.from, l.to, l.value)) && (c.addInner(l.from, l.to, l.value) || h.push(pr.create(l.from, l.to, l.value))), l.next());
    return c.finishInner(this.nextLayer.isEmpty && !h.length ? W.empty : this.nextLayer.update({ add: h, filter: o, filterFrom: s, filterTo: r }));
  }
  /**
  Map this range set through a set of changes, return the new set.
  */
  map(e) {
    if (e.empty || this.isEmpty)
      return this;
    let t = [], i = [], s = -1;
    for (let o = 0; o < this.chunk.length; o++) {
      let l = this.chunkPos[o], a = this.chunk[o], h = e.touchesRange(l, l + a.length);
      if (h === !1)
        s = Math.max(s, a.maxPoint), t.push(a), i.push(e.mapPos(l));
      else if (h === !0) {
        let { mapped: c, pos: f } = a.map(l, e);
        c && (s = Math.max(s, c.maxPoint), t.push(c), i.push(f));
      }
    }
    let r = this.nextLayer.map(e);
    return t.length == 0 ? r : new W(i, t, r || W.empty, s);
  }
  /**
  Iterate over the ranges that touch the region `from` to `to`,
  calling `f` for each. There is no guarantee that the ranges will
  be reported in any specific order. When the callback returns
  `false`, iteration stops.
  */
  between(e, t, i) {
    if (!this.isEmpty) {
      for (let s = 0; s < this.chunk.length; s++) {
        let r = this.chunkPos[s], o = this.chunk[s];
        if (t >= r && e <= r + o.length && o.between(r, e - r, t - r, i) === !1)
          return;
      }
      this.nextLayer.between(e, t, i);
    }
  }
  /**
  Iterate over the ranges in this set, in order, including all
  ranges that end at or after `from`.
  */
  iter(e = 0) {
    return Ni.from([this]).goto(e);
  }
  /**
  @internal
  */
  get isEmpty() {
    return this.nextLayer == this;
  }
  /**
  Iterate over the ranges in a collection of sets, in order,
  starting from `from`.
  */
  static iter(e, t = 0) {
    return Ni.from(e).goto(t);
  }
  /**
  Iterate over two groups of sets, calling methods on `comparator`
  to notify it of possible differences.
  */
  static compare(e, t, i, s, r = -1) {
    let o = e.filter((f) => f.maxPoint > 0 || !f.isEmpty && f.maxPoint >= r), l = t.filter((f) => f.maxPoint > 0 || !f.isEmpty && f.maxPoint >= r), a = jo(o, l, i), h = new wi(o, a, r), c = new wi(l, a, r);
    i.iterGaps((f, u, d) => Uo(h, f, c, u, d, s)), i.empty && i.length == 0 && Uo(h, 0, c, 0, 0, s);
  }
  /**
  Compare the contents of two groups of range sets, returning true
  if they are equivalent in the given range.
  */
  static eq(e, t, i = 0, s) {
    s == null && (s = 999999999);
    let r = e.filter((c) => !c.isEmpty && t.indexOf(c) < 0), o = t.filter((c) => !c.isEmpty && e.indexOf(c) < 0);
    if (r.length != o.length)
      return !1;
    if (!r.length)
      return !0;
    let l = jo(r, o), a = new wi(r, l, 0).goto(i), h = new wi(o, l, 0).goto(i);
    for (; ; ) {
      if (a.to != h.to || !gr(a.active, h.active) || a.point && (!h.point || !io(a.point, h.point)))
        return !1;
      if (a.to > s)
        return !0;
      a.next(), h.next();
    }
  }
  /**
  Iterate over a group of range sets at the same time, notifying
  the iterator about the ranges covering every given piece of
  content. Returns the open count (see
  [`SpanIterator.span`](https://codemirror.net/6/docs/ref/#state.SpanIterator.span)) at the end
  of the iteration.
  */
  static spans(e, t, i, s, r = -1) {
    let o = new wi(e, null, r).goto(t), l = t, a = o.openStart;
    for (; ; ) {
      let h = Math.min(o.to, i);
      if (o.point) {
        let c = o.activeForPoint(o.to), f = o.pointFrom < t ? c.length + 1 : o.point.startSide < 0 ? c.length : Math.min(c.length, a);
        s.point(l, h, o.point, c, f, o.pointRank), a = Math.min(o.openEnd(h), c.length);
      } else h > l && (s.span(l, h, o.active, a), a = o.openEnd(h));
      if (o.to > i)
        return a + (o.point && o.to > i ? 1 : 0);
      l = o.to, o.next();
    }
  }
  /**
  Create a range set for the given range or array of ranges. By
  default, this expects the ranges to be _sorted_ (by start
  position and, if two start at the same position,
  `value.startSide`). You can pass `true` as second argument to
  cause the method to sort them.
  */
  static of(e, t = !1) {
    let i = new ft();
    for (let s of e instanceof pr ? [e] : t ? iu(e) : e)
      i.add(s.from, s.to, s.value);
    return i.finish();
  }
  /**
  Join an array of range sets into a single set.
  */
  static join(e) {
    if (!e.length)
      return W.empty;
    let t = e[e.length - 1];
    for (let i = e.length - 2; i >= 0; i--)
      for (let s = e[i]; s != W.empty; s = s.nextLayer)
        t = new W(s.chunkPos, s.chunk, t, Math.max(s.maxPoint, t.maxPoint));
    return t;
  }
}
W.empty = /* @__PURE__ */ new W([], [], null, -1);
function iu(n) {
  if (n.length > 1)
    for (let e = n[0], t = 1; t < n.length; t++) {
      let i = n[t];
      if (mr(e, i) > 0)
        return n.slice().sort(mr);
      e = i;
    }
  return n;
}
W.empty.nextLayer = W.empty;
class ft {
  finishChunk(e) {
    this.chunks.push(new no(this.from, this.to, this.value, this.maxPoint)), this.chunkPos.push(this.chunkStart), this.chunkStart = -1, this.setMaxPoint = Math.max(this.setMaxPoint, this.maxPoint), this.maxPoint = -1, e && (this.from = [], this.to = [], this.value = []);
  }
  /**
  Create an empty builder.
  */
  constructor() {
    this.chunks = [], this.chunkPos = [], this.chunkStart = -1, this.last = null, this.lastFrom = -1e9, this.lastTo = -1e9, this.from = [], this.to = [], this.value = [], this.maxPoint = -1, this.setMaxPoint = -1, this.nextLayer = null;
  }
  /**
  Add a range. Ranges should be added in sorted (by `from` and
  `value.startSide`) order.
  */
  add(e, t, i) {
    this.addInner(e, t, i) || (this.nextLayer || (this.nextLayer = new ft())).add(e, t, i);
  }
  /**
  @internal
  */
  addInner(e, t, i) {
    let s = e - this.lastTo || i.startSide - this.last.endSide;
    if (s <= 0 && (e - this.lastFrom || i.startSide - this.last.startSide) < 0)
      throw new Error("Ranges must be added sorted by `from` position and `startSide`");
    return s < 0 ? !1 : (this.from.length == 250 && this.finishChunk(!0), this.chunkStart < 0 && (this.chunkStart = e), this.from.push(e - this.chunkStart), this.to.push(t - this.chunkStart), this.last = i, this.lastFrom = e, this.lastTo = t, this.value.push(i), i.point && (this.maxPoint = Math.max(this.maxPoint, t - e)), !0);
  }
  /**
  @internal
  */
  addChunk(e, t) {
    if ((e - this.lastTo || t.value[0].startSide - this.last.endSide) < 0)
      return !1;
    this.from.length && this.finishChunk(!0), this.setMaxPoint = Math.max(this.setMaxPoint, t.maxPoint), this.chunks.push(t), this.chunkPos.push(e);
    let i = t.value.length - 1;
    return this.last = t.value[i], this.lastFrom = t.from[i] + e, this.lastTo = t.to[i] + e, !0;
  }
  /**
  Finish the range set. Returns the new set. The builder can't be
  used anymore after this has been called.
  */
  finish() {
    return this.finishInner(W.empty);
  }
  /**
  @internal
  */
  finishInner(e) {
    if (this.from.length && this.finishChunk(!1), this.chunks.length == 0)
      return e;
    let t = W.create(this.chunkPos, this.chunks, this.nextLayer ? this.nextLayer.finishInner(e) : e, this.setMaxPoint);
    return this.from = null, t;
  }
}
function jo(n, e, t) {
  let i = /* @__PURE__ */ new Map();
  for (let r of n)
    for (let o = 0; o < r.chunk.length; o++)
      r.chunk[o].maxPoint <= 0 && i.set(r.chunk[o], r.chunkPos[o]);
  let s = /* @__PURE__ */ new Set();
  for (let r of e)
    for (let o = 0; o < r.chunk.length; o++) {
      let l = i.get(r.chunk[o]);
      l != null && (t ? t.mapPos(l) : l) == r.chunkPos[o] && !t?.touchesRange(l, l + r.chunk[o].length) && s.add(r.chunk[o]);
    }
  return s;
}
class Ja {
  constructor(e, t, i, s = 0) {
    this.layer = e, this.skip = t, this.minPoint = i, this.rank = s;
  }
  get startSide() {
    return this.value ? this.value.startSide : 0;
  }
  get endSide() {
    return this.value ? this.value.endSide : 0;
  }
  goto(e, t = -1e9) {
    return this.chunkIndex = this.rangeIndex = 0, this.gotoInner(e, t, !1), this;
  }
  gotoInner(e, t, i) {
    for (; this.chunkIndex < this.layer.chunk.length; ) {
      let s = this.layer.chunk[this.chunkIndex];
      if (!(this.skip && this.skip.has(s) || this.layer.chunkEnd(this.chunkIndex) < e || s.maxPoint < this.minPoint))
        break;
      this.chunkIndex++, i = !1;
    }
    if (this.chunkIndex < this.layer.chunk.length) {
      let s = this.layer.chunk[this.chunkIndex].findIndex(e - this.layer.chunkPos[this.chunkIndex], t, !0);
      (!i || this.rangeIndex < s) && this.setRangeIndex(s);
    }
    this.next();
  }
  forward(e, t) {
    (this.to - e || this.endSide - t) < 0 && this.gotoInner(e, t, !0);
  }
  next() {
    for (; ; )
      if (this.chunkIndex == this.layer.chunk.length) {
        this.from = this.to = 1e9, this.value = null;
        break;
      } else {
        let e = this.layer.chunkPos[this.chunkIndex], t = this.layer.chunk[this.chunkIndex], i = e + t.from[this.rangeIndex];
        if (this.from = i, this.to = e + t.to[this.rangeIndex], this.value = t.value[this.rangeIndex], this.setRangeIndex(this.rangeIndex + 1), this.minPoint < 0 || this.value.point && this.to - this.from >= this.minPoint)
          break;
      }
  }
  setRangeIndex(e) {
    if (e == this.layer.chunk[this.chunkIndex].value.length) {
      if (this.chunkIndex++, this.skip)
        for (; this.chunkIndex < this.layer.chunk.length && this.skip.has(this.layer.chunk[this.chunkIndex]); )
          this.chunkIndex++;
      this.rangeIndex = 0;
    } else
      this.rangeIndex = e;
  }
  nextChunk() {
    this.chunkIndex++, this.rangeIndex = 0, this.next();
  }
  compare(e) {
    return this.from - e.from || this.startSide - e.startSide || this.rank - e.rank || this.to - e.to || this.endSide - e.endSide;
  }
}
class Ni {
  constructor(e) {
    this.heap = e;
  }
  static from(e, t = null, i = -1) {
    let s = [];
    for (let r = 0; r < e.length; r++)
      for (let o = e[r]; !o.isEmpty; o = o.nextLayer)
        o.maxPoint >= i && s.push(new Ja(o, t, i, r));
    return s.length == 1 ? s[0] : new Ni(s);
  }
  get startSide() {
    return this.value ? this.value.startSide : 0;
  }
  goto(e, t = -1e9) {
    for (let i of this.heap)
      i.goto(e, t);
    for (let i = this.heap.length >> 1; i >= 0; i--)
      Ds(this.heap, i);
    return this.next(), this;
  }
  forward(e, t) {
    for (let i of this.heap)
      i.forward(e, t);
    for (let i = this.heap.length >> 1; i >= 0; i--)
      Ds(this.heap, i);
    (this.to - e || this.value.endSide - t) < 0 && this.next();
  }
  next() {
    if (this.heap.length == 0)
      this.from = this.to = 1e9, this.value = null, this.rank = -1;
    else {
      let e = this.heap[0];
      this.from = e.from, this.to = e.to, this.value = e.value, this.rank = e.rank, e.value && e.next(), Ds(this.heap, 0);
    }
  }
}
function Ds(n, e) {
  for (let t = n[e]; ; ) {
    let i = (e << 1) + 1;
    if (i >= n.length)
      break;
    let s = n[i];
    if (i + 1 < n.length && s.compare(n[i + 1]) >= 0 && (s = n[i + 1], i++), t.compare(s) < 0)
      break;
    n[i] = t, n[e] = s, e = i;
  }
}
class wi {
  constructor(e, t, i) {
    this.minPoint = i, this.active = [], this.activeTo = [], this.activeRank = [], this.minActive = -1, this.point = null, this.pointFrom = 0, this.pointRank = 0, this.to = -1e9, this.endSide = 0, this.openStart = -1, this.cursor = Ni.from(e, t, i);
  }
  goto(e, t = -1e9) {
    return this.cursor.goto(e, t), this.active.length = this.activeTo.length = this.activeRank.length = 0, this.minActive = -1, this.to = e, this.endSide = t, this.openStart = -1, this.next(), this;
  }
  forward(e, t) {
    for (; this.minActive > -1 && (this.activeTo[this.minActive] - e || this.active[this.minActive].endSide - t) < 0; )
      this.removeActive(this.minActive);
    this.cursor.forward(e, t);
  }
  removeActive(e) {
    fn(this.active, e), fn(this.activeTo, e), fn(this.activeRank, e), this.minActive = _o(this.active, this.activeTo);
  }
  addActive(e) {
    let t = 0, { value: i, to: s, rank: r } = this.cursor;
    for (; t < this.activeRank.length && (r - this.activeRank[t] || s - this.activeTo[t]) > 0; )
      t++;
    un(this.active, t, i), un(this.activeTo, t, s), un(this.activeRank, t, r), e && un(e, t, this.cursor.from), this.minActive = _o(this.active, this.activeTo);
  }
  // After calling this, if `this.point` != null, the next range is a
  // point. Otherwise, it's a regular range, covered by `this.active`.
  next() {
    let e = this.to, t = this.point;
    this.point = null;
    let i = this.openStart < 0 ? [] : null;
    for (; ; ) {
      let s = this.minActive;
      if (s > -1 && (this.activeTo[s] - this.cursor.from || this.active[s].endSide - this.cursor.startSide) < 0) {
        if (this.activeTo[s] > e) {
          this.to = this.activeTo[s], this.endSide = this.active[s].endSide;
          break;
        }
        this.removeActive(s), i && fn(i, s);
      } else if (this.cursor.value)
        if (this.cursor.from > e) {
          this.to = this.cursor.from, this.endSide = this.cursor.startSide;
          break;
        } else {
          let r = this.cursor.value;
          if (!r.point)
            this.addActive(i), this.cursor.next();
          else if (t && this.cursor.to == this.to && this.cursor.from < this.cursor.to)
            this.cursor.next();
          else {
            this.point = r, this.pointFrom = this.cursor.from, this.pointRank = this.cursor.rank, this.to = this.cursor.to, this.endSide = r.endSide, this.cursor.next(), this.forward(this.to, this.endSide);
            break;
          }
        }
      else {
        this.to = this.endSide = 1e9;
        break;
      }
    }
    if (i) {
      this.openStart = 0;
      for (let s = i.length - 1; s >= 0 && i[s] < e; s--)
        this.openStart++;
    }
  }
  activeForPoint(e) {
    if (!this.active.length)
      return this.active;
    let t = [];
    for (let i = this.active.length - 1; i >= 0 && !(this.activeRank[i] < this.pointRank); i--)
      (this.activeTo[i] > e || this.activeTo[i] == e && this.active[i].endSide >= this.point.endSide) && t.push(this.active[i]);
    return t.reverse();
  }
  openEnd(e) {
    let t = 0;
    for (let i = this.activeTo.length - 1; i >= 0 && this.activeTo[i] > e; i--)
      t++;
    return t;
  }
}
function Uo(n, e, t, i, s, r) {
  n.goto(e), t.goto(i);
  let o = i + s, l = i, a = i - e, h = !!r.boundChange;
  for (let c = !1; ; ) {
    let f = n.to + a - t.to, u = f || n.endSide - t.endSide, d = u < 0 ? n.to + a : t.to, p = Math.min(d, o);
    if (n.point || t.point ? (n.point && t.point && io(n.point, t.point) && gr(n.activeForPoint(n.to), t.activeForPoint(t.to)) || r.comparePoint(l, p, n.point, t.point), c = !1) : (c && r.boundChange(l), p > l && !gr(n.active, t.active) && r.compareRange(l, p, n.active, t.active), h && p < o && (f || n.openEnd(d) != t.openEnd(d)) && (c = !0)), d > o)
      break;
    l = d, u <= 0 && n.next(), u >= 0 && t.next();
  }
}
function gr(n, e) {
  if (n.length != e.length)
    return !1;
  for (let t = 0; t < n.length; t++)
    if (n[t] != e[t] && !io(n[t], e[t]))
      return !1;
  return !0;
}
function fn(n, e) {
  for (let t = e, i = n.length - 1; t < i; t++)
    n[t] = n[t + 1];
  n.pop();
}
function un(n, e, t) {
  for (let i = n.length - 1; i >= e; i--)
    n[i + 1] = n[i];
  n[e] = t;
}
function _o(n, e) {
  let t = -1, i = 1e9;
  for (let s = 0; s < e.length; s++)
    (e[s] - i || n[s].endSide - n[t].endSide) < 0 && (t = s, i = e[s]);
  return t;
}
function pi(n, e, t = n.length) {
  let i = 0;
  for (let s = 0; s < t && s < n.length; )
    n.charCodeAt(s) == 9 ? (i += e - i % e, s++) : (i++, s = oe(n, s));
  return i;
}
function yr(n, e, t, i) {
  for (let s = 0, r = 0; ; ) {
    if (r >= e)
      return s;
    if (s == n.length)
      break;
    r += n.charCodeAt(s) == 9 ? t - r % t : 1, s = oe(n, s);
  }
  return i === !0 ? -1 : n.length;
}
const br = "ͼ", Go = typeof Symbol > "u" ? "__" + br : Symbol.for(br), xr = typeof Symbol > "u" ? "__styleSet" + Math.floor(Math.random() * 1e8) : /* @__PURE__ */ Symbol("styleSet"), Yo = typeof globalThis < "u" ? globalThis : typeof window < "u" ? window : {};
class vt {
  // :: (Object<Style>, ?{finish: ?(string) → string})
  // Create a style module from the given spec.
  //
  // When `finish` is given, it is called on regular (non-`@`)
  // selectors (after `&` expansion) to compute the final selector.
  constructor(e, t) {
    this.rules = [];
    let { finish: i } = t || {};
    function s(o) {
      return /^@/.test(o) ? [o] : o.split(/,\s*/);
    }
    function r(o, l, a, h) {
      let c = [], f = /^@(\w+)\b/.exec(o[0]), u = f && f[1] == "keyframes";
      if (f && l == null) return a.push(o[0] + ";");
      for (let d in l) {
        let p = l[d];
        if (/&/.test(d))
          r(
            d.split(/,\s*/).map((m) => o.map((g) => m.replace(/&/, g))).reduce((m, g) => m.concat(g)),
            p,
            a
          );
        else if (p && typeof p == "object") {
          if (!f) throw new RangeError("The value of a property (" + d + ") should be a primitive value.");
          r(s(d), p, c, u);
        } else p != null && c.push(d.replace(/_.*/, "").replace(/[A-Z]/g, (m) => "-" + m.toLowerCase()) + ": " + p + ";");
      }
      (c.length || u) && a.push((i && !f && !h ? o.map(i) : o).join(", ") + " {" + c.join(" ") + "}");
    }
    for (let o in e) r(s(o), e[o], this.rules);
  }
  // :: () → string
  // Returns a string containing the module's CSS rules.
  getRules() {
    return this.rules.join(`
`);
  }
  // :: () → string
  // Generate a new unique CSS class name.
  static newName() {
    let e = Yo[Go] || 1;
    return Yo[Go] = e + 1, br + e.toString(36);
  }
  // :: (union<Document, ShadowRoot>, union<[StyleModule], StyleModule>, ?{nonce: ?string})
  //
  // Mount the given set of modules in the given DOM root, which ensures
  // that the CSS rules defined by the module are available in that
  // context.
  //
  // Rules are only added to the document once per root.
  //
  // Rule order will follow the order of the modules, so that rules from
  // modules later in the array take precedence of those from earlier
  // modules. If you call this function multiple times for the same root
  // in a way that changes the order of already mounted modules, the old
  // order will be changed.
  //
  // If a Content Security Policy nonce is provided, it is added to
  // the `<style>` tag generated by the library.
  static mount(e, t, i) {
    let s = e[xr], r = i && i.nonce;
    s ? r && s.setNonce(r) : s = new nu(e, r), s.mount(Array.isArray(t) ? t : [t], e);
  }
}
let Jo = /* @__PURE__ */ new Map();
class nu {
  constructor(e, t) {
    let i = e.ownerDocument || e, s = i.defaultView;
    if (!e.head && e.adoptedStyleSheets && s.CSSStyleSheet) {
      let r = Jo.get(i);
      if (r) return e[xr] = r;
      this.sheet = new s.CSSStyleSheet(), Jo.set(i, this);
    } else
      this.styleTag = i.createElement("style"), t && this.styleTag.setAttribute("nonce", t);
    this.modules = [], e[xr] = this;
  }
  mount(e, t) {
    let i = this.sheet, s = 0, r = 0;
    for (let o = 0; o < e.length; o++) {
      let l = e[o], a = this.modules.indexOf(l);
      if (a < r && a > -1 && (this.modules.splice(a, 1), r--, a = -1), a == -1) {
        if (this.modules.splice(r++, 0, l), i) for (let h = 0; h < l.rules.length; h++)
          i.insertRule(l.rules[h], s++);
      } else {
        for (; r < a; ) s += this.modules[r++].rules.length;
        s += l.rules.length, r++;
      }
    }
    if (i)
      t.adoptedStyleSheets.indexOf(this.sheet) < 0 && (t.adoptedStyleSheets = [this.sheet, ...t.adoptedStyleSheets]);
    else {
      let o = "";
      for (let a = 0; a < this.modules.length; a++)
        o += this.modules[a].getRules() + `
`;
      this.styleTag.textContent = o;
      let l = t.head || t;
      this.styleTag.parentNode != l && l.insertBefore(this.styleTag, l.firstChild);
    }
  }
  setNonce(e) {
    this.styleTag && this.styleTag.getAttribute("nonce") != e && this.styleTag.setAttribute("nonce", e);
  }
}
var kt = {
  8: "Backspace",
  9: "Tab",
  10: "Enter",
  12: "NumLock",
  13: "Enter",
  16: "Shift",
  17: "Control",
  18: "Alt",
  20: "CapsLock",
  27: "Escape",
  32: " ",
  33: "PageUp",
  34: "PageDown",
  35: "End",
  36: "Home",
  37: "ArrowLeft",
  38: "ArrowUp",
  39: "ArrowRight",
  40: "ArrowDown",
  44: "PrintScreen",
  45: "Insert",
  46: "Delete",
  59: ";",
  61: "=",
  91: "Meta",
  92: "Meta",
  106: "*",
  107: "+",
  108: ",",
  109: "-",
  110: ".",
  111: "/",
  144: "NumLock",
  145: "ScrollLock",
  160: "Shift",
  161: "Shift",
  162: "Control",
  163: "Control",
  164: "Alt",
  165: "Alt",
  173: "-",
  186: ";",
  187: "=",
  188: ",",
  189: "-",
  190: ".",
  191: "/",
  192: "`",
  219: "[",
  220: "\\",
  221: "]",
  222: "'"
}, Wi = {
  48: ")",
  49: "!",
  50: "@",
  51: "#",
  52: "$",
  53: "%",
  54: "^",
  55: "&",
  56: "*",
  57: "(",
  59: ":",
  61: "+",
  173: "_",
  186: ":",
  187: "+",
  188: "<",
  189: "_",
  190: ">",
  191: "?",
  192: "~",
  219: "{",
  220: "|",
  221: "}",
  222: '"'
}, su = typeof navigator < "u" && /Mac/.test(navigator.platform), ru = typeof navigator < "u" && /MSIE \d|Trident\/(?:[7-9]|\d{2,})\..*rv:(\d+)/.exec(navigator.userAgent);
for (var ce = 0; ce < 10; ce++) kt[48 + ce] = kt[96 + ce] = String(ce);
for (var ce = 1; ce <= 24; ce++) kt[ce + 111] = "F" + ce;
for (var ce = 65; ce <= 90; ce++)
  kt[ce] = String.fromCharCode(ce + 32), Wi[ce] = String.fromCharCode(ce);
for (var Os in kt) Wi.hasOwnProperty(Os) || (Wi[Os] = kt[Os]);
function ou(n) {
  var e = su && n.metaKey && n.shiftKey && !n.ctrlKey && !n.altKey || ru && n.shiftKey && n.key && n.key.length == 1 || n.key == "Unidentified", t = !e && n.key || (n.shiftKey ? Wi : kt)[n.keyCode] || n.key || "Unidentified";
  return t == "Esc" && (t = "Escape"), t == "Del" && (t = "Delete"), t == "Left" && (t = "ArrowLeft"), t == "Up" && (t = "ArrowUp"), t == "Right" && (t = "ArrowRight"), t == "Down" && (t = "ArrowDown"), t;
}
function K() {
  var n = arguments[0];
  typeof n == "string" && (n = document.createElement(n));
  var e = 1, t = arguments[1];
  if (t && typeof t == "object" && t.nodeType == null && !Array.isArray(t)) {
    for (var i in t) if (Object.prototype.hasOwnProperty.call(t, i)) {
      var s = t[i];
      typeof s == "string" ? n.setAttribute(i, s) : s != null && (n[i] = s);
    }
    e++;
  }
  for (; e < arguments.length; e++) Xa(n, arguments[e]);
  return n;
}
function Xa(n, e) {
  if (typeof e == "string")
    n.appendChild(document.createTextNode(e));
  else if (e != null) if (e.nodeType != null)
    n.appendChild(e);
  else if (Array.isArray(e))
    for (var t = 0; t < e.length; t++) Xa(n, e[t]);
  else
    throw new RangeError("Unsupported child node: " + e);
}
let we = typeof navigator < "u" ? navigator : { userAgent: "", vendor: "", platform: "" }, wr = typeof document < "u" ? document : { documentElement: { style: {} } };
const vr = /* @__PURE__ */ /Edge\/(\d+)/.exec(we.userAgent), Qa = /* @__PURE__ */ /MSIE \d/.test(we.userAgent), kr = /* @__PURE__ */ /Trident\/(?:[7-9]|\d{2,})\..*rv:(\d+)/.exec(we.userAgent), us = !!(Qa || kr || vr), Xo = !us && /* @__PURE__ */ /gecko\/(\d+)/i.test(we.userAgent), Bs = !us && /* @__PURE__ */ /Chrome\/(\d+)/.exec(we.userAgent), Qo = "webkitFontSmoothing" in wr.documentElement.style, Sr = !us && /* @__PURE__ */ /Apple Computer/.test(we.vendor), Zo = Sr && (/* @__PURE__ */ /Mobile\/\w+/.test(we.userAgent) || we.maxTouchPoints > 2);
var D = {
  mac: Zo || /* @__PURE__ */ /Mac/.test(we.platform),
  windows: /* @__PURE__ */ /Win/.test(we.platform),
  linux: /* @__PURE__ */ /Linux|X11/.test(we.platform),
  ie: us,
  ie_version: Qa ? wr.documentMode || 6 : kr ? +kr[1] : vr ? +vr[1] : 0,
  gecko: Xo,
  gecko_version: Xo ? +(/* @__PURE__ */ /Firefox\/(\d+)/.exec(we.userAgent) || [0, 0])[1] : 0,
  chrome: !!Bs,
  chrome_version: Bs ? +Bs[1] : 0,
  ios: Zo,
  android: /* @__PURE__ */ /Android\b/.test(we.userAgent),
  webkit: Qo,
  webkit_version: Qo ? +(/* @__PURE__ */ /\bAppleWebKit\/(\d+)/.exec(we.userAgent) || [0, 0])[1] : 0,
  safari: Sr,
  safari_version: Sr ? +(/* @__PURE__ */ /\bVersion\/(\d+(\.\d+)?)/.exec(we.userAgent) || [0, 0])[1] : 0,
  tabSize: wr.documentElement.style.tabSize != null ? "tab-size" : "-moz-tab-size"
};
function so(n, e) {
  for (let t in n)
    t == "class" && e.class ? e.class += " " + n.class : t == "style" && e.style ? e.style += ";" + n.style : e[t] = n[t];
  return e;
}
const $n = /* @__PURE__ */ Object.create(null);
function ro(n, e, t) {
  if (n == e)
    return !0;
  n || (n = $n), e || (e = $n);
  let i = Object.keys(n), s = Object.keys(e);
  if (i.length - 0 != s.length - 0)
    return !1;
  for (let r of i)
    if (r != t && (s.indexOf(r) == -1 || n[r] !== e[r]))
      return !1;
  return !0;
}
function lu(n, e) {
  for (let t = n.attributes.length - 1; t >= 0; t--) {
    let i = n.attributes[t].name;
    e[i] == null && n.removeAttribute(i);
  }
  for (let t in e) {
    let i = e[t];
    t == "style" ? n.style.cssText = i : n.getAttribute(t) != i && n.setAttribute(t, i);
  }
}
function el(n, e, t) {
  let i = !1;
  if (e)
    for (let s in e)
      t && s in t || (i = !0, s == "style" ? n.style.cssText = "" : n.removeAttribute(s));
  if (t)
    for (let s in t)
      e && e[s] == t[s] || (i = !0, s == "style" ? n.style.cssText = t[s] : n.setAttribute(s, t[s]));
  return i;
}
function au(n) {
  let e = /* @__PURE__ */ Object.create(null);
  for (let t = 0; t < n.attributes.length; t++) {
    let i = n.attributes[t];
    e[i.name] = i.value;
  }
  return e;
}
class Ke {
  /**
  Compare this instance to another instance of the same type.
  (TypeScript can't express this, but only instances of the same
  specific class will be passed to this method.) This is used to
  avoid redrawing widgets when they are replaced by a new
  decoration of the same type. The default implementation just
  returns `false`, which will cause new instances of the widget to
  always be redrawn.
  */
  eq(e) {
    return !1;
  }
  /**
  Update a DOM element created by a widget of the same type (but
  different, non-`eq` content) to reflect this widget. May return
  true to indicate that it could update, false to indicate it
  couldn't (in which case the widget will be redrawn). The default
  implementation just returns false.
  */
  updateDOM(e, t, i) {
    return !1;
  }
  /**
  @internal
  */
  compare(e) {
    return this == e || this.constructor == e.constructor && this.eq(e);
  }
  /**
  The estimated height this widget will have, to be used when
  estimating the height of content that hasn't been drawn. May
  return -1 to indicate you don't know. The default implementation
  returns -1.
  */
  get estimatedHeight() {
    return -1;
  }
  /**
  For inline widgets that are displayed inline (as opposed to
  `inline-block`) and introduce line breaks (through `<br>` tags
  or textual newlines), this must indicate the amount of line
  breaks they introduce. Defaults to 0.
  */
  get lineBreaks() {
    return 0;
  }
  /**
  Can be used to configure which kinds of events inside the widget
  should be ignored by the editor. The default is to ignore all
  events.
  */
  ignoreEvent(e) {
    return !0;
  }
  /**
  Override the way screen coordinates for positions at/in the
  widget are found. `pos` will be the offset into the widget, and
  `side` the side of the position that is being queried—less than
  zero for before, greater than zero for after, and zero for
  directly at that position.
  */
  coordsAt(e, t, i) {
    return null;
  }
  /**
  @internal
  */
  get isHidden() {
    return !1;
  }
  /**
  @internal
  */
  get editable() {
    return !1;
  }
  /**
  This is called when the an instance of the widget is removed
  from the editor view.
  */
  destroy(e) {
  }
}
var de = /* @__PURE__ */ (function(n) {
  return n[n.Text = 0] = "Text", n[n.WidgetBefore = 1] = "WidgetBefore", n[n.WidgetAfter = 2] = "WidgetAfter", n[n.WidgetRange = 3] = "WidgetRange", n;
})(de || (de = {}));
class B extends wt {
  constructor(e, t, i, s) {
    super(), this.startSide = e, this.endSide = t, this.widget = i, this.spec = s;
  }
  /**
  @internal
  */
  get heightRelevant() {
    return !1;
  }
  /**
  Create a mark decoration, which influences the styling of the
  content in its range. Nested mark decorations will cause nested
  DOM elements to be created. Nesting order is determined by
  precedence of the [facet](https://codemirror.net/6/docs/ref/#view.EditorView^decorations), with
  the higher-precedence decorations creating the inner DOM nodes.
  Such elements are split on line boundaries and on the boundaries
  of lower-precedence decorations.
  */
  static mark(e) {
    return new Qi(e);
  }
  /**
  Create a widget decoration, which displays a DOM element at the
  given position.
  */
  static widget(e) {
    let t = Math.max(-1e4, Math.min(1e4, e.side || 0)), i = !!e.block;
    return t += i && !e.inlineOrder ? t > 0 ? 3e8 : -4e8 : t > 0 ? 1e8 : -1e8, new zt(e, t, t, i, e.widget || null, !1);
  }
  /**
  Create a replace decoration which replaces the given range with
  a widget, or simply hides it.
  */
  static replace(e) {
    let t = !!e.block, i, s;
    if (e.isBlockGap)
      i = -5e8, s = 4e8;
    else {
      let { start: r, end: o } = Za(e, t);
      i = (r ? t ? -3e8 : -1 : 5e8) - 1, s = (o ? t ? 2e8 : 1 : -6e8) + 1;
    }
    return new zt(e, i, s, t, e.widget || null, !0);
  }
  /**
  Create a line decoration, which can add DOM attributes to the
  line starting at the given position.
  */
  static line(e) {
    return new Zi(e);
  }
  /**
  Build a [`DecorationSet`](https://codemirror.net/6/docs/ref/#view.DecorationSet) from the given
  decorated range or ranges. If the ranges aren't already sorted,
  pass `true` for `sort` to make the library sort them for you.
  */
  static set(e, t = !1) {
    return W.of(e, t);
  }
  /**
  @internal
  */
  hasHeight() {
    return this.widget ? this.widget.estimatedHeight > -1 : !1;
  }
}
B.none = W.empty;
class Qi extends B {
  constructor(e) {
    let { start: t, end: i } = Za(e);
    super(t ? -1 : 5e8, i ? 1 : -6e8, null, e), this.tagName = e.tagName || "span", this.attrs = e.class && e.attributes ? so(e.attributes, { class: e.class }) : e.class ? { class: e.class } : e.attributes || $n;
  }
  eq(e) {
    return this == e || e instanceof Qi && this.tagName == e.tagName && ro(this.attrs, e.attrs);
  }
  range(e, t = e) {
    if (e >= t)
      throw new RangeError("Mark decorations may not be empty");
    return super.range(e, t);
  }
}
Qi.prototype.point = !1;
class Zi extends B {
  constructor(e) {
    super(-2e8, -2e8, null, e);
  }
  eq(e) {
    return e instanceof Zi && this.spec.class == e.spec.class && ro(this.spec.attributes, e.spec.attributes);
  }
  range(e, t = e) {
    if (t != e)
      throw new RangeError("Line decoration ranges must be zero-length");
    return super.range(e, t);
  }
}
Zi.prototype.mapMode = fe.TrackBefore;
Zi.prototype.point = !0;
class zt extends B {
  constructor(e, t, i, s, r, o) {
    super(t, i, r, e), this.block = s, this.isReplace = o, this.mapMode = s ? t <= 0 ? fe.TrackBefore : fe.TrackAfter : fe.TrackDel;
  }
  // Only relevant when this.block == true
  get type() {
    return this.startSide != this.endSide ? de.WidgetRange : this.startSide <= 0 ? de.WidgetBefore : de.WidgetAfter;
  }
  get heightRelevant() {
    return this.block || !!this.widget && (this.widget.estimatedHeight >= 5 || this.widget.lineBreaks > 0);
  }
  eq(e) {
    return e instanceof zt && hu(this.widget, e.widget) && this.block == e.block && this.startSide == e.startSide && this.endSide == e.endSide;
  }
  range(e, t = e) {
    if (this.isReplace && (e > t || e == t && this.startSide > 0 && this.endSide <= 0))
      throw new RangeError("Invalid range for replacement decoration");
    if (!this.isReplace && t != e)
      throw new RangeError("Widget decorations can only have zero-length ranges");
    return super.range(e, t);
  }
}
zt.prototype.point = !0;
function Za(n, e = !1) {
  let { inclusiveStart: t, inclusiveEnd: i } = n;
  return t == null && (t = n.inclusive), i == null && (i = n.inclusive), { start: t ?? e, end: i ?? e };
}
function hu(n, e) {
  return n == e || !!(n && e && n.compare(e));
}
function ii(n, e, t, i = 0) {
  let s = t.length - 1;
  s >= 0 && t[s] + i >= n ? t[s] = Math.max(t[s], e) : t.push(n, e);
}
class Hi extends wt {
  constructor(e, t, i) {
    super(), this.tagName = e, this.attributes = t, this.rank = i;
  }
  eq(e) {
    return e == this || e instanceof Hi && this.tagName == e.tagName && ro(this.attributes, e.attributes);
  }
  /**
  Create a block wrapper object with the given tag name and
  attributes.
  */
  static create(e) {
    return new Hi(e.tagName, e.attributes || $n, e.rank == null ? 50 : Math.max(0, Math.min(e.rank, 100)));
  }
  /**
  Create a range set from the given block wrapper ranges.
  */
  static set(e, t = !1) {
    return W.of(e, t);
  }
}
Hi.prototype.startSide = Hi.prototype.endSide = -1;
function Fi(n) {
  let e;
  return n.nodeType == 11 ? e = n.getSelection ? n : n.ownerDocument : e = n, e.getSelection();
}
function Cr(n, e) {
  return e ? n == e || n.contains(e.nodeType != 1 ? e.parentNode : e) : !1;
}
function Oi(n, e) {
  if (!e.anchorNode)
    return !1;
  try {
    return Cr(n, e.anchorNode);
  } catch {
    return !1;
  }
}
function Bi(n) {
  return n.nodeType == 3 ? zi(n, 0, n.nodeValue.length).getClientRects() : n.nodeType == 1 ? n.getClientRects() : [];
}
function Li(n, e, t, i) {
  return t ? tl(n, e, t, i, -1) || tl(n, e, t, i, 1) : !1;
}
function St(n) {
  for (var e = 0; ; e++)
    if (n = n.previousSibling, !n)
      return e;
}
function Kn(n) {
  return n.nodeType == 1 && /^(DIV|P|LI|UL|OL|BLOCKQUOTE|DD|DT|H\d|SECTION|PRE)$/.test(n.nodeName);
}
function tl(n, e, t, i, s) {
  for (; ; ) {
    if (n == t && e == i)
      return !0;
    if (e == (s < 0 ? 0 : ut(n))) {
      if (n.nodeName == "DIV")
        return !1;
      let r = n.parentNode;
      if (!r || r.nodeType != 1)
        return !1;
      e = St(n) + (s < 0 ? 0 : 1), n = r;
    } else if (n.nodeType == 1) {
      if (n = n.childNodes[e + (s < 0 ? -1 : 0)], n.nodeType == 1 && n.contentEditable == "false")
        return !1;
      e = s < 0 ? ut(n) : 0;
    } else
      return !1;
  }
}
function ut(n) {
  return n.nodeType == 3 ? n.nodeValue.length : n.childNodes.length;
}
function Vi(n, e) {
  let t = e ? n.left : n.right;
  return { left: t, right: t, top: n.top, bottom: n.bottom };
}
function cu(n) {
  let e = n.visualViewport;
  return e ? {
    left: 0,
    right: e.width,
    top: 0,
    bottom: e.height
  } : {
    left: 0,
    right: n.innerWidth,
    top: 0,
    bottom: n.innerHeight
  };
}
function eh(n, e) {
  let t = e.width / n.offsetWidth, i = e.height / n.offsetHeight;
  return (t > 0.995 && t < 1.005 || !isFinite(t) || Math.abs(e.width - n.offsetWidth) < 1) && (t = 1), (i > 0.995 && i < 1.005 || !isFinite(i) || Math.abs(e.height - n.offsetHeight) < 1) && (i = 1), { scaleX: t, scaleY: i };
}
function fu(n, e, t, i, s, r, o, l) {
  let a = n.ownerDocument, h = a.defaultView || window;
  for (let c = n, f = !1; c && !f; )
    if (c.nodeType == 1) {
      let u, d = c == a.body, p = 1, m = 1;
      if (d)
        u = cu(h);
      else {
        if (/^(fixed|sticky)$/.test(getComputedStyle(c).position) && (f = !0), c.scrollHeight <= c.clientHeight && c.scrollWidth <= c.clientWidth) {
          c = c.assignedSlot || c.parentNode;
          continue;
        }
        let w = c.getBoundingClientRect();
        ({ scaleX: p, scaleY: m } = eh(c, w)), u = {
          left: w.left,
          right: w.left + c.clientWidth * p,
          top: w.top,
          bottom: w.top + c.clientHeight * m
        };
      }
      let g = 0, y = 0;
      if (s == "nearest")
        e.top < u.top + o ? (y = e.top - (u.top + o), t > 0 && e.bottom > u.bottom + y && (y = e.bottom - u.bottom + o)) : e.bottom > u.bottom - o && (y = e.bottom - u.bottom + o, t < 0 && e.top - y < u.top && (y = e.top - (u.top + o)));
      else {
        let w = e.bottom - e.top, v = u.bottom - u.top;
        y = (s == "center" && w <= v ? e.top + w / 2 - v / 2 : s == "start" || s == "center" && t < 0 ? e.top - o : e.bottom - v + o) - u.top;
      }
      if (i == "nearest" ? e.left < u.left + r ? (g = e.left - (u.left + r), t > 0 && e.right > u.right + g && (g = e.right - u.right + r)) : e.right > u.right - r && (g = e.right - u.right + r, t < 0 && e.left < u.left + g && (g = e.left - (u.left + r))) : g = (i == "center" ? e.left + (e.right - e.left) / 2 - (u.right - u.left) / 2 : i == "start" == l ? e.left - r : e.right - (u.right - u.left) + r) - u.left, g || y)
        if (d)
          h.scrollBy(g, y);
        else {
          let w = 0, v = 0;
          if (y) {
            let O = c.scrollTop;
            c.scrollTop += y / m, v = (c.scrollTop - O) * m;
          }
          if (g) {
            let O = c.scrollLeft;
            c.scrollLeft += g / p, w = (c.scrollLeft - O) * p;
          }
          e = {
            left: e.left - w,
            top: e.top - v,
            right: e.right - w,
            bottom: e.bottom - v
          }, w && Math.abs(w - g) < 1 && (i = "nearest"), v && Math.abs(v - y) < 1 && (s = "nearest");
        }
      if (d)
        break;
      (e.top < u.top || e.bottom > u.bottom || e.left < u.left || e.right > u.right) && (e = {
        left: Math.max(e.left, u.left),
        right: Math.min(e.right, u.right),
        top: Math.max(e.top, u.top),
        bottom: Math.min(e.bottom, u.bottom)
      }), c = c.assignedSlot || c.parentNode;
    } else if (c.nodeType == 11)
      c = c.host;
    else
      break;
}
function th(n, e = !0) {
  let t = n.ownerDocument, i = null, s = null;
  for (let r = n.parentNode; r && !(r == t.body || (!e || i) && s); )
    if (r.nodeType == 1)
      !s && r.scrollHeight > r.clientHeight && (s = r), e && !i && r.scrollWidth > r.clientWidth && (i = r), r = r.assignedSlot || r.parentNode;
    else if (r.nodeType == 11)
      r = r.host;
    else
      break;
  return { x: i, y: s };
}
class uu {
  constructor() {
    this.anchorNode = null, this.anchorOffset = 0, this.focusNode = null, this.focusOffset = 0;
  }
  eq(e) {
    return this.anchorNode == e.anchorNode && this.anchorOffset == e.anchorOffset && this.focusNode == e.focusNode && this.focusOffset == e.focusOffset;
  }
  setRange(e) {
    let { anchorNode: t, focusNode: i } = e;
    this.set(t, Math.min(e.anchorOffset, t ? ut(t) : 0), i, Math.min(e.focusOffset, i ? ut(i) : 0));
  }
  set(e, t, i, s) {
    this.anchorNode = e, this.anchorOffset = t, this.focusNode = i, this.focusOffset = s;
  }
}
let Rt = null;
D.safari && D.safari_version >= 26 && (Rt = !1);
function ih(n) {
  if (n.setActive)
    return n.setActive();
  if (Rt)
    return n.focus(Rt);
  let e = [];
  for (let t = n; t && (e.push(t, t.scrollTop, t.scrollLeft), t != t.ownerDocument); t = t.parentNode)
    ;
  if (n.focus(Rt == null ? {
    get preventScroll() {
      return Rt = { preventScroll: !0 }, !0;
    }
  } : void 0), !Rt) {
    Rt = !1;
    for (let t = 0; t < e.length; ) {
      let i = e[t++], s = e[t++], r = e[t++];
      i.scrollTop != s && (i.scrollTop = s), i.scrollLeft != r && (i.scrollLeft = r);
    }
  }
}
let il;
function zi(n, e, t = e) {
  let i = il || (il = document.createRange());
  return i.setEnd(n, t), i.setStart(n, e), i;
}
function ni(n, e, t, i) {
  let s = { key: e, code: e, keyCode: t, which: t, cancelable: !0 };
  i && ({ altKey: s.altKey, ctrlKey: s.ctrlKey, shiftKey: s.shiftKey, metaKey: s.metaKey } = i);
  let r = new KeyboardEvent("keydown", s);
  r.synthetic = !0, n.dispatchEvent(r);
  let o = new KeyboardEvent("keyup", s);
  return o.synthetic = !0, n.dispatchEvent(o), r.defaultPrevented || o.defaultPrevented;
}
function du(n) {
  for (; n; ) {
    if (n && (n.nodeType == 9 || n.nodeType == 11 && n.host))
      return n;
    n = n.assignedSlot || n.parentNode;
  }
  return null;
}
function pu(n, e) {
  let t = e.focusNode, i = e.focusOffset;
  if (!t || e.anchorNode != t || e.anchorOffset != i)
    return !1;
  for (i = Math.min(i, ut(t)); ; )
    if (i) {
      if (t.nodeType != 1)
        return !1;
      let s = t.childNodes[i - 1];
      s.contentEditable == "false" ? i-- : (t = s, i = ut(t));
    } else {
      if (t == n)
        return !0;
      i = St(t), t = t.parentNode;
    }
}
function nh(n) {
  return n instanceof Window ? n.pageYOffset > Math.max(0, n.document.documentElement.scrollHeight - n.innerHeight - 4) : n.scrollTop > Math.max(1, n.scrollHeight - n.clientHeight - 4);
}
function sh(n, e) {
  for (let t = n, i = e; ; ) {
    if (t.nodeType == 3 && i > 0)
      return { node: t, offset: i };
    if (t.nodeType == 1 && i > 0) {
      if (t.contentEditable == "false")
        return null;
      t = t.childNodes[i - 1], i = ut(t);
    } else if (t.parentNode && !Kn(t))
      i = St(t), t = t.parentNode;
    else
      return null;
  }
}
function rh(n, e) {
  for (let t = n, i = e; ; ) {
    if (t.nodeType == 3 && i < t.nodeValue.length)
      return { node: t, offset: i };
    if (t.nodeType == 1 && i < t.childNodes.length) {
      if (t.contentEditable == "false")
        return null;
      t = t.childNodes[i], i = 0;
    } else if (t.parentNode && !Kn(t))
      i = St(t) + 1, t = t.parentNode;
    else
      return null;
  }
}
class ze {
  constructor(e, t, i = !0) {
    this.node = e, this.offset = t, this.precise = i;
  }
  static before(e, t) {
    return new ze(e.parentNode, St(e), t);
  }
  static after(e, t) {
    return new ze(e.parentNode, St(e) + 1, t);
  }
}
var _ = /* @__PURE__ */ (function(n) {
  return n[n.LTR = 0] = "LTR", n[n.RTL = 1] = "RTL", n;
})(_ || (_ = {}));
const qt = _.LTR, oo = _.RTL;
function oh(n) {
  let e = [];
  for (let t = 0; t < n.length; t++)
    e.push(1 << +n[t]);
  return e;
}
const mu = /* @__PURE__ */ oh("88888888888888888888888888888888888666888888787833333333337888888000000000000000000000000008888880000000000000000000000000088888888888888888888888888888888888887866668888088888663380888308888800000000000000000000000800000000000000000000000000000008"), gu = /* @__PURE__ */ oh("4444448826627288999999999992222222222222222222222222222222222222222222222229999999999999999999994444444444644222822222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222222999999949999999229989999223333333333"), Ar = /* @__PURE__ */ Object.create(null), Ye = [];
for (let n of ["()", "[]", "{}"]) {
  let e = /* @__PURE__ */ n.charCodeAt(0), t = /* @__PURE__ */ n.charCodeAt(1);
  Ar[e] = t, Ar[t] = -e;
}
function lh(n) {
  return n <= 247 ? mu[n] : 1424 <= n && n <= 1524 ? 2 : 1536 <= n && n <= 1785 ? gu[n - 1536] : 1774 <= n && n <= 2220 ? 4 : 8192 <= n && n <= 8204 ? 256 : 64336 <= n && n <= 65023 ? 4 : 1;
}
const yu = /[\u0590-\u05f4\u0600-\u06ff\u0700-\u08ac\ufb50-\ufdff]/;
class it {
  /**
  The direction of this span.
  */
  get dir() {
    return this.level % 2 ? oo : qt;
  }
  /**
  @internal
  */
  constructor(e, t, i) {
    this.from = e, this.to = t, this.level = i;
  }
  /**
  @internal
  */
  side(e, t) {
    return this.dir == t == e ? this.to : this.from;
  }
  /**
  @internal
  */
  forward(e, t) {
    return e == (this.dir == t);
  }
  /**
  @internal
  */
  static find(e, t, i, s) {
    let r = -1;
    for (let o = 0; o < e.length; o++) {
      let l = e[o];
      if (l.from <= t && l.to >= t) {
        if (l.level == i)
          return o;
        (r < 0 || (s != 0 ? s < 0 ? l.from < t : l.to > t : e[r].level > l.level)) && (r = o);
      }
    }
    if (r < 0)
      throw new RangeError("Index out of range");
    return r;
  }
}
function ah(n, e) {
  if (n.length != e.length)
    return !1;
  for (let t = 0; t < n.length; t++) {
    let i = n[t], s = e[t];
    if (i.from != s.from || i.to != s.to || i.direction != s.direction || !ah(i.inner, s.inner))
      return !1;
  }
  return !0;
}
const U = [];
function bu(n, e, t, i, s) {
  for (let r = 0; r <= i.length; r++) {
    let o = r ? i[r - 1].to : e, l = r < i.length ? i[r].from : t, a = r ? 256 : s;
    for (let h = o, c = a, f = a; h < l; h++) {
      let u = lh(n.charCodeAt(h));
      u == 512 ? u = c : u == 8 && f == 4 && (u = 16), U[h] = u == 4 ? 2 : u, u & 7 && (f = u), c = u;
    }
    for (let h = o, c = a, f = a; h < l; h++) {
      let u = U[h];
      if (u == 128)
        h < l - 1 && c == U[h + 1] && c & 24 ? u = U[h] = c : U[h] = 256;
      else if (u == 64) {
        let d = h + 1;
        for (; d < l && U[d] == 64; )
          d++;
        let p = h && c == 8 || d < t && U[d] == 8 ? f == 1 ? 1 : 8 : 256;
        for (let m = h; m < d; m++)
          U[m] = p;
        h = d - 1;
      } else u == 8 && f == 1 && (U[h] = 1);
      c = u, u & 7 && (f = u);
    }
  }
}
function xu(n, e, t, i, s) {
  let r = s == 1 ? 2 : 1;
  for (let o = 0, l = 0, a = 0; o <= i.length; o++) {
    let h = o ? i[o - 1].to : e, c = o < i.length ? i[o].from : t;
    for (let f = h, u, d, p; f < c; f++)
      if (d = Ar[u = n.charCodeAt(f)])
        if (d < 0) {
          for (let m = l - 3; m >= 0; m -= 3)
            if (Ye[m + 1] == -d) {
              let g = Ye[m + 2], y = g & 2 ? s : g & 4 ? g & 1 ? r : s : 0;
              y && (U[f] = U[Ye[m]] = y), l = m;
              break;
            }
        } else {
          if (Ye.length == 189)
            break;
          Ye[l++] = f, Ye[l++] = u, Ye[l++] = a;
        }
      else if ((p = U[f]) == 2 || p == 1) {
        let m = p == s;
        a = m ? 0 : 1;
        for (let g = l - 3; g >= 0; g -= 3) {
          let y = Ye[g + 2];
          if (y & 2)
            break;
          if (m)
            Ye[g + 2] |= 2;
          else {
            if (y & 4)
              break;
            Ye[g + 2] |= 4;
          }
        }
      }
  }
}
function wu(n, e, t, i) {
  for (let s = 0, r = i; s <= t.length; s++) {
    let o = s ? t[s - 1].to : n, l = s < t.length ? t[s].from : e;
    for (let a = o; a < l; ) {
      let h = U[a];
      if (h == 256) {
        let c = a + 1;
        for (; ; )
          if (c == l) {
            if (s == t.length)
              break;
            c = t[s++].to, l = s < t.length ? t[s].from : e;
          } else if (U[c] == 256)
            c++;
          else
            break;
        let f = r == 1, u = (c < e ? U[c] : i) == 1, d = f == u ? f ? 1 : 2 : i;
        for (let p = c, m = s, g = m ? t[m - 1].to : n; p > a; )
          p == g && (p = t[--m].from, g = m ? t[m - 1].to : n), U[--p] = d;
        a = c;
      } else
        r = h, a++;
    }
  }
}
function Mr(n, e, t, i, s, r, o) {
  let l = i % 2 ? 2 : 1;
  if (i % 2 == s % 2)
    for (let a = e, h = 0; a < t; ) {
      let c = !0, f = !1;
      if (h == r.length || a < r[h].from) {
        let m = U[a];
        m != l && (c = !1, f = m == 16);
      }
      let u = !c && l == 1 ? [] : null, d = c ? i : i + 1, p = a;
      e: for (; ; )
        if (h < r.length && p == r[h].from) {
          if (f)
            break e;
          let m = r[h];
          if (!c)
            for (let g = m.to, y = h + 1; ; ) {
              if (g == t)
                break e;
              if (y < r.length && r[y].from == g)
                g = r[y++].to;
              else {
                if (U[g] == l)
                  break e;
                break;
              }
            }
          if (h++, u)
            u.push(m);
          else {
            m.from > a && o.push(new it(a, m.from, d));
            let g = m.direction == qt != !(d % 2);
            Tr(n, g ? i + 1 : i, s, m.inner, m.from, m.to, o), a = m.to;
          }
          p = m.to;
        } else {
          if (p == t || (c ? U[p] != l : U[p] == l))
            break;
          p++;
        }
      u ? Mr(n, a, p, i + 1, s, u, o) : a < p && o.push(new it(a, p, d)), a = p;
    }
  else
    for (let a = t, h = r.length; a > e; ) {
      let c = !0, f = !1;
      if (!h || a > r[h - 1].to) {
        let m = U[a - 1];
        m != l && (c = !1, f = m == 16);
      }
      let u = !c && l == 1 ? [] : null, d = c ? i : i + 1, p = a;
      e: for (; ; )
        if (h && p == r[h - 1].to) {
          if (f)
            break e;
          let m = r[--h];
          if (!c)
            for (let g = m.from, y = h; ; ) {
              if (g == e)
                break e;
              if (y && r[y - 1].to == g)
                g = r[--y].from;
              else {
                if (U[g - 1] == l)
                  break e;
                break;
              }
            }
          if (u)
            u.push(m);
          else {
            m.to < a && o.push(new it(m.to, a, d));
            let g = m.direction == qt != !(d % 2);
            Tr(n, g ? i + 1 : i, s, m.inner, m.from, m.to, o), a = m.from;
          }
          p = m.from;
        } else {
          if (p == e || (c ? U[p - 1] != l : U[p - 1] == l))
            break;
          p--;
        }
      u ? Mr(n, p, a, i + 1, s, u, o) : p < a && o.push(new it(p, a, d)), a = p;
    }
}
function Tr(n, e, t, i, s, r, o) {
  let l = e % 2 ? 2 : 1;
  bu(n, s, r, i, l), xu(n, s, r, i, l), wu(s, r, i, l), Mr(n, s, r, e, t, i, o);
}
function vu(n, e, t) {
  if (!n)
    return [new it(0, 0, e == oo ? 1 : 0)];
  if (e == qt && !t.length && !yu.test(n))
    return hh(n.length);
  if (t.length)
    for (; n.length > U.length; )
      U[U.length] = 256;
  let i = [], s = e == qt ? 0 : 1;
  return Tr(n, s, s, t, 0, n.length, i), i;
}
function hh(n) {
  return [new it(0, n, 0)];
}
let ch = "";
function ku(n, e, t, i, s) {
  var r;
  let o = i.head - n.from, l = it.find(e, o, (r = i.bidiLevel) !== null && r !== void 0 ? r : -1, i.assoc), a = e[l], h = a.side(s, t);
  if (o == h) {
    let u = l += s ? 1 : -1;
    if (u < 0 || u >= e.length)
      return null;
    a = e[l = u], o = a.side(!s, t), h = a.side(s, t);
  }
  let c = oe(n.text, o, a.forward(s, t));
  (c < a.from || c > a.to) && (c = h), ch = n.text.slice(Math.min(o, c), Math.max(o, c));
  let f = l == (s ? e.length - 1 : 0) ? null : e[l + (s ? 1 : -1)];
  return f && c == h && f.level + (s ? 0 : 1) < a.level ? b.cursor(f.side(!s, t) + n.from, f.forward(s, t) ? 1 : -1, f.level) : b.cursor(c + n.from, a.forward(s, t) ? -1 : 1, a.level);
}
function Su(n, e, t) {
  for (let i = e; i < t; i++) {
    let s = lh(n.charCodeAt(i));
    if (s == 1)
      return qt;
    if (s == 2 || s == 4)
      return oo;
  }
  return qt;
}
const fh = /* @__PURE__ */ T.define(), uh = /* @__PURE__ */ T.define(), dh = /* @__PURE__ */ T.define(), ph = /* @__PURE__ */ T.define(), Dr = /* @__PURE__ */ T.define(), mh = /* @__PURE__ */ T.define(), gh = /* @__PURE__ */ T.define(), lo = /* @__PURE__ */ T.define(), ao = /* @__PURE__ */ T.define(), yh = /* @__PURE__ */ T.define({
  combine: (n) => n.some((e) => e)
}), bh = /* @__PURE__ */ T.define({
  combine: (n) => n.some((e) => e)
}), xh = /* @__PURE__ */ T.define();
class si {
  constructor(e, t, i, s, r, o = !1) {
    this.range = e, this.y = t, this.x = i, this.yMargin = s, this.xMargin = r, this.isSnapshot = o;
  }
  map(e) {
    return e.empty ? this : new si(this.range.map(e), this.y, this.x, this.yMargin, this.xMargin, this.isSnapshot);
  }
  clip(e) {
    return this.range.to <= e.doc.length ? this : new si(b.cursor(e.doc.length), this.y, this.x, this.yMargin, this.xMargin, this.isSnapshot);
  }
}
const dn = /* @__PURE__ */ R.define({ map: (n, e) => n.map(e) }), wh = /* @__PURE__ */ R.define();
function Ae(n, e, t) {
  let i = n.facet(ph);
  i.length ? i[0](e) : window.onerror && window.onerror(String(e), t, void 0, void 0, e) || (t ? console.error(t + ":", e) : console.error(e));
}
const ht = /* @__PURE__ */ T.define({ combine: (n) => n.length ? n[0] : !0 });
let Cu = 0;
const Xt = /* @__PURE__ */ T.define({
  combine(n) {
    return n.filter((e, t) => {
      for (let i = 0; i < t; i++)
        if (n[i].plugin == e.plugin)
          return !1;
      return !0;
    });
  }
});
class Q {
  constructor(e, t, i, s, r) {
    this.id = e, this.create = t, this.domEventHandlers = i, this.domEventObservers = s, this.baseExtensions = r(this), this.extension = this.baseExtensions.concat(Xt.of({ plugin: this, arg: void 0 }));
  }
  /**
  Create an extension for this plugin with the given argument.
  */
  of(e) {
    return this.baseExtensions.concat(Xt.of({ plugin: this, arg: e }));
  }
  /**
  Define a plugin from a constructor function that creates the
  plugin's value, given an editor view.
  */
  static define(e, t) {
    const { eventHandlers: i, eventObservers: s, provide: r, decorations: o } = t || {};
    return new Q(Cu++, e, i, s, (l) => {
      let a = [];
      return o && a.push(ds.of((h) => {
        let c = h.plugin(l);
        return c ? o(c) : B.none;
      })), r && a.push(r(l)), a;
    });
  }
  /**
  Create a plugin for a class whose constructor takes a single
  editor view as argument.
  */
  static fromClass(e, t) {
    return Q.define((i, s) => new e(i, s), t);
  }
}
class Ls {
  constructor(e) {
    this.spec = e, this.mustUpdate = null, this.value = null;
  }
  get plugin() {
    return this.spec && this.spec.plugin;
  }
  update(e) {
    if (this.value) {
      if (this.mustUpdate) {
        let t = this.mustUpdate;
        if (this.mustUpdate = null, this.value.update)
          try {
            this.value.update(t);
          } catch (i) {
            if (Ae(t.state, i, "CodeMirror plugin crashed"), this.value.destroy)
              try {
                this.value.destroy();
              } catch {
              }
            this.deactivate();
          }
      }
    } else if (this.spec)
      try {
        this.value = this.spec.plugin.create(e, this.spec.arg);
      } catch (t) {
        Ae(e.state, t, "CodeMirror plugin crashed"), this.deactivate();
      }
    return this;
  }
  destroy(e) {
    var t;
    if (!((t = this.value) === null || t === void 0) && t.destroy)
      try {
        this.value.destroy();
      } catch (i) {
        Ae(e.state, i, "CodeMirror plugin crashed");
      }
  }
  deactivate() {
    this.spec = this.value = null;
  }
}
const vh = /* @__PURE__ */ T.define(), ho = /* @__PURE__ */ T.define(), ds = /* @__PURE__ */ T.define(), kh = /* @__PURE__ */ T.define(), co = /* @__PURE__ */ T.define(), en = /* @__PURE__ */ T.define(), Sh = /* @__PURE__ */ T.define();
function nl(n, e) {
  let t = n.state.facet(Sh);
  if (!t.length)
    return t;
  let i = t.map((r) => r instanceof Function ? r(n) : r), s = [];
  return W.spans(i, e.from, e.to, {
    point() {
    },
    span(r, o, l, a) {
      let h = r - e.from, c = o - e.from, f = s;
      for (let u = l.length - 1; u >= 0; u--, a--) {
        let d = l[u].spec.bidiIsolate, p;
        if (d == null && (d = Su(e.text, h, c)), a > 0 && f.length && (p = f[f.length - 1]).to == h && p.direction == d)
          p.to = c, f = p.inner;
        else {
          let m = { from: h, to: c, direction: d, inner: [] };
          f.push(m), f = m.inner;
        }
      }
    }
  }), s;
}
const Ch = /* @__PURE__ */ T.define();
function fo(n) {
  let e = 0, t = 0, i = 0, s = 0;
  for (let r of n.state.facet(Ch)) {
    let o = r(n);
    o && (o.left != null && (e = Math.max(e, o.left)), o.right != null && (t = Math.max(t, o.right)), o.top != null && (i = Math.max(i, o.top)), o.bottom != null && (s = Math.max(s, o.bottom)));
  }
  return { left: e, right: t, top: i, bottom: s };
}
const Ci = /* @__PURE__ */ T.define();
class Ie {
  constructor(e, t, i, s) {
    this.fromA = e, this.toA = t, this.fromB = i, this.toB = s;
  }
  join(e) {
    return new Ie(Math.min(this.fromA, e.fromA), Math.max(this.toA, e.toA), Math.min(this.fromB, e.fromB), Math.max(this.toB, e.toB));
  }
  addToSet(e) {
    let t = e.length, i = this;
    for (; t > 0; t--) {
      let s = e[t - 1];
      if (!(s.fromA > i.toA)) {
        if (s.toA < i.fromA)
          break;
        i = i.join(s), e.splice(t - 1, 1);
      }
    }
    return e.splice(t, 0, i), e;
  }
  // Extend a set to cover all the content in `ranges`, which is a
  // flat array with each pair of numbers representing fromB/toB
  // positions. These pairs are generated in unchanged ranges, so the
  // offset between doc A and doc B is the same for their start and
  // end points.
  static extendWithRanges(e, t) {
    if (t.length == 0)
      return e;
    let i = [];
    for (let s = 0, r = 0, o = 0; ; ) {
      let l = s < e.length ? e[s].fromB : 1e9, a = r < t.length ? t[r] : 1e9, h = Math.min(l, a);
      if (h == 1e9)
        break;
      let c = h + o, f = h, u = c;
      for (; ; )
        if (r < t.length && t[r] <= f) {
          let d = t[r + 1];
          r += 2, f = Math.max(f, d);
          for (let p = s; p < e.length && e[p].fromB <= f; p++)
            o = e[p].toA - e[p].toB;
          u = Math.max(u, d + o);
        } else if (s < e.length && e[s].fromB <= f) {
          let d = e[s++];
          f = Math.max(f, d.toB), u = Math.max(u, d.toA), o = d.toA - d.toB;
        } else
          break;
      i.push(new Ie(c, u, h, f));
    }
    return i;
  }
}
class jn {
  constructor(e, t, i) {
    this.view = e, this.state = t, this.transactions = i, this.flags = 0, this.startState = e.state, this.changes = ne.empty(this.startState.doc.length);
    for (let r of i)
      this.changes = this.changes.compose(r.changes);
    let s = [];
    this.changes.iterChangedRanges((r, o, l, a) => s.push(new Ie(r, o, l, a))), this.changedRanges = s;
  }
  /**
  @internal
  */
  static create(e, t, i) {
    return new jn(e, t, i);
  }
  /**
  Tells you whether the [viewport](https://codemirror.net/6/docs/ref/#view.EditorView.viewport) or
  [visible ranges](https://codemirror.net/6/docs/ref/#view.EditorView.visibleRanges) changed in this
  update.
  */
  get viewportChanged() {
    return (this.flags & 4) > 0;
  }
  /**
  Returns true when
  [`viewportChanged`](https://codemirror.net/6/docs/ref/#view.ViewUpdate.viewportChanged) is true
  and the viewport change is not just the result of mapping it in
  response to document changes.
  */
  get viewportMoved() {
    return (this.flags & 8) > 0;
  }
  /**
  Indicates whether the height of a block element in the editor
  changed in this update.
  */
  get heightChanged() {
    return (this.flags & 2) > 0;
  }
  /**
  Returns true when the document was modified or the size of the
  editor, or elements within the editor, changed.
  */
  get geometryChanged() {
    return this.docChanged || (this.flags & 18) > 0;
  }
  /**
  True when this update indicates a focus change.
  */
  get focusChanged() {
    return (this.flags & 1) > 0;
  }
  /**
  Whether the document changed in this update.
  */
  get docChanged() {
    return !this.changes.empty;
  }
  /**
  Whether the selection was explicitly set in this update.
  */
  get selectionSet() {
    return this.transactions.some((e) => e.selection);
  }
  /**
  @internal
  */
  get empty() {
    return this.flags == 0 && this.transactions.length == 0;
  }
}
const Au = [];
class X {
  constructor(e, t, i = 0) {
    this.dom = e, this.length = t, this.flags = i, this.parent = null, e.cmTile = this;
  }
  get breakAfter() {
    return this.flags & 1;
  }
  get children() {
    return Au;
  }
  isWidget() {
    return !1;
  }
  get isHidden() {
    return !1;
  }
  isComposite() {
    return !1;
  }
  isLine() {
    return !1;
  }
  isText() {
    return !1;
  }
  isBlock() {
    return !1;
  }
  get domAttrs() {
    return null;
  }
  sync(e) {
    if (this.flags |= 2, this.flags & 4) {
      this.flags &= -5;
      let t = this.domAttrs;
      t && lu(this.dom, t);
    }
  }
  toString() {
    return this.constructor.name + (this.children.length ? `(${this.children})` : "") + (this.breakAfter ? "#" : "");
  }
  destroy() {
    this.parent = null;
  }
  setDOM(e) {
    this.dom = e, e.cmTile = this;
  }
  get posAtStart() {
    return this.parent ? this.parent.posBefore(this) : 0;
  }
  get posAtEnd() {
    return this.posAtStart + this.length;
  }
  posBefore(e, t = this.posAtStart) {
    let i = t;
    for (let s of this.children) {
      if (s == e)
        return i;
      i += s.length + s.breakAfter;
    }
    throw new RangeError("Invalid child in posBefore");
  }
  posAfter(e) {
    return this.posBefore(e) + e.length;
  }
  covers(e) {
    return !0;
  }
  coordsIn(e, t) {
    return null;
  }
  domPosFor(e, t) {
    let i = St(this.dom), s = this.length ? e > 0 : t > 0;
    return new ze(this.parent.dom, i + (s ? 1 : 0), e == 0 || e == this.length);
  }
  markDirty(e) {
    this.flags &= -3, e && (this.flags |= 4), this.parent && this.parent.flags & 2 && this.parent.markDirty(!1);
  }
  get overrideDOMText() {
    return null;
  }
  get root() {
    for (let e = this; e; e = e.parent)
      if (e instanceof ms)
        return e;
    return null;
  }
  static get(e) {
    return e.cmTile;
  }
}
class ps extends X {
  constructor(e) {
    super(e, 0), this._children = [];
  }
  isComposite() {
    return !0;
  }
  get children() {
    return this._children;
  }
  get lastChild() {
    return this.children.length ? this.children[this.children.length - 1] : null;
  }
  append(e) {
    this.children.push(e), e.parent = this;
  }
  sync(e) {
    if (this.flags & 2)
      return;
    super.sync(e);
    let t = this.dom, i = null, s, r = e?.node == t ? e : null, o = 0;
    for (let l of this.children) {
      if (l.sync(e), o += l.length + l.breakAfter, s = i ? i.nextSibling : t.firstChild, r && s != l.dom && (r.written = !0), l.dom.parentNode == t)
        for (; s && s != l.dom; )
          s = sl(s);
      else
        t.insertBefore(l.dom, s);
      i = l.dom;
    }
    for (s = i ? i.nextSibling : t.firstChild, r && s && (r.written = !0); s; )
      s = sl(s);
    this.length = o;
  }
}
function sl(n) {
  let e = n.nextSibling;
  return n.parentNode.removeChild(n), e;
}
class ms extends ps {
  constructor(e, t) {
    super(t), this.view = e;
  }
  owns(e) {
    for (; e; e = e.parent)
      if (e == this)
        return !0;
    return !1;
  }
  isBlock() {
    return !0;
  }
  nearest(e) {
    for (; ; ) {
      if (!e)
        return null;
      let t = X.get(e);
      if (t && this.owns(t))
        return t;
      e = e.parentNode;
    }
  }
  blockTiles(e) {
    for (let t = [], i = this, s = 0, r = 0; ; )
      if (s == i.children.length) {
        if (!t.length)
          return;
        i = i.parent, i.breakAfter && r++, s = t.pop();
      } else {
        let o = i.children[s++];
        if (o instanceof ct)
          t.push(s), i = o, s = 0;
        else {
          let l = r + o.length, a = e(o, r);
          if (a !== void 0)
            return a;
          r = l + o.breakAfter;
        }
      }
  }
  // Find the block at the given position. If side < -1, make sure to
  // stay before block widgets at that position, if side > 1, after
  // such widgets (used for selection drawing, which needs to be able
  // to get coordinates for positions that aren't valid cursor positions).
  resolveBlock(e, t) {
    let i, s = -1, r, o = -1;
    if (this.blockTiles((l, a) => {
      let h = a + l.length;
      if (e >= a && e <= h) {
        if (l.isWidget() && t >= -1 && t <= 1) {
          if (l.flags & 32)
            return !0;
          l.flags & 16 && (i = void 0);
        }
        (a < e || e == h && (t < -1 ? l.length : l.covers(1))) && (!i || !l.isWidget() && i.isWidget()) && (i = l, s = e - a), (h > e || e == a && (t > 1 ? l.length : l.covers(-1))) && (!r || !l.isWidget() && r.isWidget()) && (r = l, o = e - a);
      }
    }), !i && !r)
      throw new Error("No tile at position " + e);
    return i && t < 0 || !r ? { tile: i, offset: s } : { tile: r, offset: o };
  }
}
class ct extends ps {
  constructor(e, t) {
    super(e), this.wrapper = t;
  }
  isBlock() {
    return !0;
  }
  covers(e) {
    return this.children.length ? e < 0 ? this.children[0].covers(-1) : this.lastChild.covers(1) : !1;
  }
  get domAttrs() {
    return this.wrapper.attributes;
  }
  static of(e, t) {
    let i = new ct(t || document.createElement(e.tagName), e);
    return t || (i.flags |= 4), i;
  }
}
class ai extends ps {
  constructor(e, t) {
    super(e), this.attrs = t;
  }
  isLine() {
    return !0;
  }
  static start(e, t, i) {
    let s = new ai(t || document.createElement("div"), e);
    return (!t || !i) && (s.flags |= 4), s;
  }
  get domAttrs() {
    return this.attrs;
  }
  // Find the tile associated with a given position in this line.
  resolveInline(e, t, i) {
    let s = null, r = -1, o = null, l = -1;
    function a(c, f) {
      for (let u = 0, d = 0; u < c.children.length && d <= f; u++) {
        let p = c.children[u], m = d + p.length;
        m >= f && (p.isComposite() ? a(p, f - d) : (!o || o.isHidden && (t > 0 || i && Tu(o, p))) && (m > f || p.flags & 32) ? (o = p, l = f - d) : (d < f || p.flags & 16 && !p.isHidden) && (s = p, r = f - d)), d = m;
      }
    }
    a(this, e);
    let h = (t < 0 ? s : o) || s || o;
    return h ? { tile: h, offset: h == s ? r : l } : null;
  }
  coordsIn(e, t) {
    let i = this.resolveInline(e, t, !0);
    return i ? i.tile.coordsIn(Math.max(0, i.offset), t) : Mu(this);
  }
  domIn(e, t) {
    let i = this.resolveInline(e, t);
    if (i) {
      let { tile: s, offset: r } = i;
      if (this.dom.contains(s.dom))
        return s.isText() ? new ze(s.dom, Math.min(s.dom.nodeValue.length, r)) : s.domPosFor(r, s.flags & 16 ? 1 : s.flags & 32 ? -1 : t);
      let o = i.tile.parent, l = !1;
      for (let a of o.children) {
        if (l)
          return new ze(a.dom, 0);
        a == i.tile && (l = !0);
      }
    }
    return new ze(this.dom, 0);
  }
}
function Mu(n) {
  let e = n.dom.lastChild;
  if (!e)
    return n.dom.getBoundingClientRect();
  let t = Bi(e);
  return t[t.length - 1] || null;
}
function Tu(n, e) {
  let t = n.coordsIn(0, 1), i = e.coordsIn(0, 1);
  return t && i && i.top < t.bottom;
}
class Ce extends ps {
  constructor(e, t) {
    super(e), this.mark = t;
  }
  get domAttrs() {
    return this.mark.attrs;
  }
  static of(e, t) {
    let i = new Ce(t || document.createElement(e.tagName), e);
    return t || (i.flags |= 4), i;
  }
}
class Nt extends X {
  constructor(e, t) {
    super(e, t.length), this.text = t;
  }
  sync(e) {
    this.flags & 2 || (super.sync(e), this.dom.nodeValue != this.text && (e && e.node == this.dom && (e.written = !0), this.dom.nodeValue = this.text));
  }
  isText() {
    return !0;
  }
  toString() {
    return JSON.stringify(this.text);
  }
  coordsIn(e, t) {
    let i = this.dom.nodeValue.length;
    e > i && (e = i);
    let s = e, r = e, o = 0;
    e == 0 && t < 0 || e == i && t >= 0 ? D.chrome || D.gecko || (e ? (s--, o = 1) : r < i && (r++, o = -1)) : t < 0 ? s-- : r < i && r++;
    let l = zi(this.dom, s, r).getClientRects();
    if (!l.length)
      return null;
    let a = l[(o ? o < 0 : t >= 0) ? 0 : l.length - 1];
    return D.safari && !o && a.width == 0 && (a = Array.prototype.find.call(l, (h) => h.width) || a), o ? Vi(a, o < 0) : a || null;
  }
  static of(e, t) {
    let i = new Nt(t || document.createTextNode(e), e);
    return t || (i.flags |= 2), i;
  }
}
class $t extends X {
  constructor(e, t, i, s) {
    super(e, t, s), this.widget = i;
  }
  isWidget() {
    return !0;
  }
  get isHidden() {
    return this.widget.isHidden;
  }
  covers(e) {
    return this.flags & 48 ? !1 : (this.flags & (e < 0 ? 64 : 128)) > 0;
  }
  coordsIn(e, t) {
    return this.coordsInWidget(e, t, !1);
  }
  coordsInWidget(e, t, i) {
    let s = this.widget.coordsAt(this.dom, e, t);
    if (s)
      return s;
    if (i)
      return Vi(this.dom.getBoundingClientRect(), this.length ? e == 0 : t <= 0);
    {
      let r = this.dom.getClientRects(), o = null;
      if (!r.length)
        return null;
      let l = this.flags & 16 ? !0 : this.flags & 32 ? !1 : e > 0;
      for (let a = l ? r.length - 1 : 0; o = r[a], !(e > 0 ? a == 0 : a == r.length - 1 || o.top < o.bottom); a += l ? -1 : 1)
        ;
      return Vi(o, !l);
    }
  }
  get overrideDOMText() {
    if (!this.length)
      return z.empty;
    let { root: e } = this;
    if (!e)
      return z.empty;
    let t = this.posAtStart;
    return e.view.state.doc.slice(t, t + this.length);
  }
  destroy() {
    super.destroy(), this.widget.destroy(this.dom);
  }
  static of(e, t, i, s, r) {
    return r || (r = e.toDOM(t), e.editable || (r.contentEditable = "false")), new $t(r, i, e, s);
  }
}
class Un extends X {
  constructor(e) {
    let t = document.createElement("img");
    t.className = "cm-widgetBuffer", t.setAttribute("aria-hidden", "true"), super(t, 0, e);
  }
  get isHidden() {
    return !0;
  }
  get overrideDOMText() {
    return z.empty;
  }
  coordsIn(e) {
    return this.dom.getBoundingClientRect();
  }
}
class Du {
  constructor(e) {
    this.index = 0, this.beforeBreak = !1, this.parents = [], this.tile = e;
  }
  // Advance by the given distance. If side is -1, stop leaving or
  // entering tiles, or skipping zero-length tiles, once the distance
  // has been traversed. When side is 1, leave, enter, or skip
  // everything at the end position.
  advance(e, t, i) {
    let { tile: s, index: r, beforeBreak: o, parents: l } = this;
    for (; e || t > 0; )
      if (s.isComposite())
        if (o) {
          if (!e)
            break;
          i && i.break(), e--, o = !1;
        } else if (r == s.children.length) {
          if (!e && !l.length)
            break;
          i && i.leave(s), o = !!s.breakAfter, { tile: s, index: r } = l.pop(), r++;
        } else {
          let a = s.children[r], h = a.breakAfter;
          (t > 0 ? a.length <= e : a.length < e) && (!i || i.skip(a, 0, a.length) !== !1 || !a.isComposite) ? (o = !!h, r++, e -= a.length) : (l.push({ tile: s, index: r }), s = a, r = 0, i && a.isComposite() && i.enter(a));
        }
      else if (r == s.length)
        o = !!s.breakAfter, { tile: s, index: r } = l.pop(), r++;
      else if (e) {
        let a = Math.min(e, s.length - r);
        i && i.skip(s, r, r + a), e -= a, r += a;
      } else
        break;
    return this.tile = s, this.index = r, this.beforeBreak = o, this;
  }
  get root() {
    return this.parents.length ? this.parents[0].tile : this.tile;
  }
}
class Ou {
  constructor(e, t, i, s) {
    this.from = e, this.to = t, this.wrapper = i, this.rank = s;
  }
}
class Bu {
  constructor(e, t, i) {
    this.cache = e, this.root = t, this.blockWrappers = i, this.curLine = null, this.lastBlock = null, this.afterWidget = null, this.pos = 0, this.wrappers = [], this.wrapperPos = 0;
  }
  addText(e, t, i, s) {
    var r;
    this.flushBuffer();
    let o = this.ensureMarks(t, i), l = o.lastChild;
    if (l && l.isText() && !(l.flags & 8) && l.length + e.length < 512) {
      this.cache.reused.set(
        l,
        2
        /* Reused.DOM */
      );
      let a = o.children[o.children.length - 1] = new Nt(l.dom, l.text + e);
      a.parent = o;
    } else
      o.append(s || Nt.of(e, (r = this.cache.find(Nt)) === null || r === void 0 ? void 0 : r.dom));
    this.pos += e.length, this.afterWidget = null;
  }
  addComposition(e, t) {
    let i = this.curLine;
    i.dom != t.line.dom && (i.setDOM(this.cache.reused.has(t.line) ? Es(t.line.dom) : t.line.dom), this.cache.reused.set(
      t.line,
      2
      /* Reused.DOM */
    ));
    let s = i;
    for (let l = t.marks.length - 1; l >= 0; l--) {
      let a = t.marks[l], h = s.lastChild;
      if (h instanceof Ce && h.mark.eq(a.mark))
        h.dom != a.dom && h.setDOM(Es(a.dom)), s = h;
      else {
        if (this.cache.reused.get(a)) {
          let f = X.get(a.dom);
          f && f.setDOM(Es(a.dom));
        }
        let c = Ce.of(a.mark, a.dom);
        s.append(c), s = c;
      }
      this.cache.reused.set(
        a,
        2
        /* Reused.DOM */
      );
    }
    let r = X.get(e.text);
    r && this.cache.reused.set(
      r,
      2
      /* Reused.DOM */
    );
    let o = new Nt(e.text, e.text.nodeValue);
    o.flags |= 8, this.pos = e.range.toB, s.append(o);
  }
  addInlineWidget(e, t, i) {
    let s = this.afterWidget && e.flags & 48 && (this.afterWidget.flags & 48) == (e.flags & 48);
    s || this.flushBuffer();
    let r = this.ensureMarks(t, i);
    !s && !(e.flags & 16) && r.append(this.getBuffer(1)), r.append(e), this.pos += e.length, this.afterWidget = e;
  }
  addMark(e, t, i) {
    this.flushBuffer(), this.ensureMarks(t, i).append(e), this.pos += e.length, this.afterWidget = null;
  }
  addBlockWidget(e) {
    this.getBlockPos().append(e), this.pos += e.length, this.lastBlock = e, this.endLine();
  }
  continueWidget(e) {
    let t = this.afterWidget || this.lastBlock;
    t.length += e, this.pos += e;
  }
  addLineStart(e, t) {
    var i;
    e || (e = Ah);
    let s = ai.start(e, t || ((i = this.cache.find(ai)) === null || i === void 0 ? void 0 : i.dom), !!t);
    this.getBlockPos().append(this.lastBlock = this.curLine = s);
  }
  addLine(e) {
    this.getBlockPos().append(e), this.pos += e.length, this.lastBlock = e, this.endLine();
  }
  addBreak() {
    this.lastBlock.flags |= 1, this.endLine(), this.pos++;
  }
  addLineStartIfNotCovered(e) {
    this.blockPosCovered() || this.addLineStart(e);
  }
  ensureLine(e) {
    this.curLine || this.addLineStart(e);
  }
  ensureMarks(e, t) {
    var i;
    let s = this.curLine;
    for (let r = e.length - 1; r >= 0; r--) {
      let o = e[r], l;
      if (t > 0 && (l = s.lastChild) && l instanceof Ce && l.mark.eq(o))
        s = l, t--;
      else {
        let a = Ce.of(o, (i = this.cache.find(Ce, (h) => h.mark.eq(o))) === null || i === void 0 ? void 0 : i.dom);
        s.append(a), s = a, t = 0;
      }
    }
    return s;
  }
  endLine() {
    if (this.curLine) {
      this.flushBuffer();
      let e = this.curLine.lastChild;
      (!e || !rl(this.curLine, !1) || e.dom.nodeName != "BR" && e.isWidget() && !(D.ios && rl(this.curLine, !0))) && this.curLine.append(this.cache.findWidget(
        Rs,
        0,
        32
        /* TileFlag.After */
      ) || new $t(
        Rs.toDOM(),
        0,
        Rs,
        32
        /* TileFlag.After */
      )), this.curLine = this.afterWidget = null;
    }
  }
  updateBlockWrappers() {
    this.wrapperPos > this.pos + 1e4 && (this.blockWrappers.goto(this.pos), this.wrappers.length = 0);
    for (let e = this.wrappers.length - 1; e >= 0; e--)
      this.wrappers[e].to < this.pos && this.wrappers.splice(e, 1);
    for (let e = this.blockWrappers; e.value && e.from <= this.pos; e.next())
      if (e.to >= this.pos) {
        let t = e.rank * 102 + e.value.rank, i = new Ou(e.from, e.to, e.value, t), s = this.wrappers.length;
        for (; s > 0 && (this.wrappers[s - 1].rank - i.rank || this.wrappers[s - 1].to - i.to) < 0; )
          s--;
        this.wrappers.splice(s, 0, i);
      }
    this.wrapperPos = this.pos;
  }
  getBlockPos() {
    var e;
    this.updateBlockWrappers();
    let t = this.root;
    for (let i of this.wrappers) {
      let s = t.lastChild;
      if (i.from < this.pos && s instanceof ct && s.wrapper.eq(i.wrapper))
        t = s;
      else {
        let r = ct.of(i.wrapper, (e = this.cache.find(ct, (o) => o.wrapper.eq(i.wrapper))) === null || e === void 0 ? void 0 : e.dom);
        t.append(r), t = r;
      }
    }
    return t;
  }
  blockPosCovered() {
    let e = this.lastBlock;
    return e != null && !e.breakAfter && (!e.isWidget() || (e.flags & 160) > 0);
  }
  getBuffer(e) {
    let t = 2 | (e < 0 ? 16 : 32), i = this.cache.find(
      Un,
      void 0,
      1
      /* Reused.Full */
    );
    return i && (i.flags = t), i || new Un(t);
  }
  flushBuffer() {
    this.afterWidget && !(this.afterWidget.flags & 32) && (this.afterWidget.parent.append(this.getBuffer(-1)), this.afterWidget = null);
  }
}
class Lu {
  constructor(e) {
    this.skipCount = 0, this.text = "", this.textOff = 0, this.cursor = e.iter();
  }
  skip(e) {
    this.textOff + e <= this.text.length ? this.textOff += e : (this.skipCount += e - (this.text.length - this.textOff), this.text = "", this.textOff = 0);
  }
  next(e) {
    if (this.textOff == this.text.length) {
      let { value: s, lineBreak: r, done: o } = this.cursor.next(this.skipCount);
      if (this.skipCount = 0, o)
        throw new Error("Ran out of text content when drawing inline views");
      this.text = s;
      let l = this.textOff = Math.min(e, s.length);
      return r ? null : s.slice(0, l);
    }
    let t = Math.min(this.text.length, this.textOff + e), i = this.text.slice(this.textOff, t);
    return this.textOff = t, i;
  }
}
const _n = [$t, ai, Nt, Ce, Un, ct, ms];
for (let n = 0; n < _n.length; n++)
  _n[n].bucket = n;
class Eu {
  constructor(e) {
    this.view = e, this.buckets = _n.map(() => []), this.index = _n.map(() => 0), this.reused = /* @__PURE__ */ new Map();
  }
  // Put a tile in the cache.
  add(e) {
    let t = e.constructor.bucket, i = this.buckets[t];
    i.length < 6 ? i.push(e) : i[
      this.index[t] = (this.index[t] + 1) % 6
      /* C.Bucket */
    ] = e;
  }
  find(e, t, i = 2) {
    let s = e.bucket, r = this.buckets[s], o = this.index[s];
    for (let l = r.length - 1; l >= 0; l--) {
      let a = (l + o) % r.length, h = r[a];
      if ((!t || t(h)) && !this.reused.has(h))
        return r.splice(a, 1), a < o && this.index[s]--, this.reused.set(h, i), h;
    }
    return null;
  }
  findWidget(e, t, i) {
    let s = this.buckets[0];
    if (s.length)
      for (let r = 0, o = 0; ; r++) {
        if (r == s.length) {
          if (o)
            return null;
          o = 1, r = 0;
        }
        let l = s[r];
        if (!this.reused.has(l) && (o == 0 ? l.widget.compare(e) : l.widget.constructor == e.constructor && e.updateDOM(l.dom, this.view, l.widget)))
          return s.splice(r, 1), r < this.index[0] && this.index[0]--, l.widget == e && l.length == t && (l.flags & 497) == i ? (this.reused.set(
            l,
            1
            /* Reused.Full */
          ), l) : (this.reused.set(
            l,
            2
            /* Reused.DOM */
          ), new $t(l.dom, t, e, l.flags & -498 | i));
      }
  }
  reuse(e) {
    return this.reused.set(
      e,
      1
      /* Reused.Full */
    ), e;
  }
  maybeReuse(e, t = 2) {
    if (!this.reused.has(e))
      return this.reused.set(e, t), e.dom;
  }
  clear() {
    for (let e = 0; e < this.buckets.length; e++)
      this.buckets[e].length = this.index[e] = 0;
  }
}
class Ru {
  constructor(e, t, i, s, r) {
    this.view = e, this.decorations = s, this.disallowBlockEffectsFor = r, this.openWidget = !1, this.openMarks = 0, this.cache = new Eu(e), this.text = new Lu(e.state.doc), this.builder = new Bu(this.cache, new ms(e, e.contentDOM), W.iter(i)), this.cache.reused.set(
      t,
      2
      /* Reused.DOM */
    ), this.old = new Du(t), this.reuseWalker = {
      skip: (o, l, a) => {
        if (this.cache.add(o), o.isComposite())
          return !1;
      },
      enter: (o) => this.cache.add(o),
      leave: () => {
      },
      break: () => {
      }
    };
  }
  run(e, t) {
    let i = t && this.getCompositionContext(t.text);
    for (let s = 0, r = 0, o = 0; ; ) {
      let l = o < e.length ? e[o++] : null, a = l ? l.fromA : this.old.root.length;
      if (a > s) {
        let h = a - s;
        this.preserve(h, !o, !l), s = a, r += h;
      }
      if (!l)
        break;
      t && l.fromA <= t.range.fromA && l.toA >= t.range.toA ? (this.forward(l.fromA, t.range.fromA, t.range.fromA < t.range.toA ? 1 : -1), this.emit(r, t.range.fromB), this.builder.flushBuffer(), this.cache.clear(), this.builder.addComposition(t, i), this.text.skip(t.range.toB - t.range.fromB), this.forward(t.range.fromA, l.toA), this.emit(t.range.toB, l.toB)) : (this.forward(l.fromA, l.toA), this.emit(r, l.toB)), r = l.toB, s = l.toA;
    }
    return this.builder.curLine && this.builder.endLine(), this.builder.root;
  }
  preserve(e, t, i) {
    let s = Nu(this.old), r = this.openMarks;
    this.old.advance(e, i ? 1 : -1, {
      skip: (o, l, a) => {
        if (o.isWidget())
          if (this.openWidget)
            this.builder.continueWidget(a - l);
          else {
            let h = a > 0 || l < o.length ? $t.of(o.widget, this.view, a - l, o.flags & 496, this.cache.maybeReuse(o)) : this.cache.reuse(o);
            h.flags & 256 ? (h.flags &= -2, this.builder.addBlockWidget(h)) : (this.builder.ensureLine(null), this.builder.addInlineWidget(h, s, r), r = s.length);
          }
        else if (o.isText())
          this.builder.ensureLine(null), !l && a == o.length && !this.cache.reused.has(o) ? this.builder.addText(o.text, s, r, this.cache.reuse(o)) : (this.cache.add(o), this.builder.addText(o.text.slice(l, a), s, r)), r = s.length;
        else if (o.isLine())
          o.flags &= -2, this.cache.reused.set(
            o,
            1
            /* Reused.Full */
          ), this.builder.addLine(o);
        else if (o instanceof Un)
          this.cache.add(o);
        else if (o instanceof Ce)
          this.builder.ensureLine(null), this.builder.addMark(o, s, r), this.cache.reused.set(
            o,
            1
            /* Reused.Full */
          ), r = s.length;
        else
          return !1;
        this.openWidget = !1;
      },
      enter: (o) => {
        o.isLine() ? this.builder.addLineStart(o.attrs, this.cache.maybeReuse(o)) : (this.cache.add(o), o instanceof Ce && s.unshift(o.mark)), this.openWidget = !1;
      },
      leave: (o) => {
        o.isLine() ? s.length && (s.length = r = 0) : o instanceof Ce && (s.shift(), r = Math.min(r, s.length));
      },
      break: () => {
        this.builder.addBreak(), this.openWidget = !1;
      }
    }), this.text.skip(e);
  }
  emit(e, t) {
    let i = null, s = this.builder, r = 0, o = W.spans(this.decorations, e, t, {
      point: (l, a, h, c, f, u) => {
        if (h instanceof zt) {
          if (this.disallowBlockEffectsFor[u]) {
            if (h.block)
              throw new RangeError("Block decorations may not be specified via plugins");
            if (a > this.view.state.doc.lineAt(l).to)
              throw new RangeError("Decorations that replace line breaks may not be specified via plugins");
          }
          if (r = c.length, f > c.length)
            s.continueWidget(a - l);
          else {
            let d = h.widget || (h.block ? hi.block : hi.inline), p = Pu(h), m = this.cache.findWidget(d, a - l, p) || $t.of(d, this.view, a - l, p);
            h.block ? (h.startSide > 0 && s.addLineStartIfNotCovered(i), s.addBlockWidget(m)) : (s.ensureLine(i), s.addInlineWidget(m, c, f));
          }
          i = null;
        } else
          i = Iu(i, h);
        a > l && this.text.skip(a - l);
      },
      span: (l, a, h, c) => {
        for (let f = l; f < a; ) {
          let u = this.text.next(Math.min(512, a - f));
          u == null ? (s.addLineStartIfNotCovered(i), s.addBreak(), f++) : (s.ensureLine(i), s.addText(u, h, f == l ? c : h.length), f += u.length), i = null;
        }
      }
    });
    this.openWidget = o > r, this.openWidget || s.addLineStartIfNotCovered(i), this.openMarks = o;
  }
  forward(e, t, i = 1) {
    t - e <= 10 ? this.old.advance(t - e, i, this.reuseWalker) : (this.old.advance(5, -1, this.reuseWalker), this.old.advance(t - e - 10, -1), this.old.advance(5, i, this.reuseWalker));
  }
  getCompositionContext(e) {
    let t = [], i = null;
    for (let s = e.parentNode; ; s = s.parentNode) {
      let r = X.get(s);
      if (s == this.view.contentDOM)
        break;
      r instanceof Ce ? t.push(r) : r?.isLine() ? i = r : r instanceof ct || (s.nodeName == "DIV" && !i && s != this.view.contentDOM ? i = new ai(s, Ah) : i || t.push(Ce.of(new Qi({ tagName: s.nodeName.toLowerCase(), attributes: au(s) }), s)));
    }
    return { line: i, marks: t };
  }
}
function rl(n, e) {
  let t = (i) => {
    for (let s of i.children)
      if ((e ? s.isText() : s.length) || t(s))
        return !0;
    return !1;
  };
  return t(n);
}
function Pu(n) {
  let e = n.isReplace ? (n.startSide < 0 ? 64 : 0) | (n.endSide > 0 ? 128 : 0) : n.startSide > 0 ? 32 : 16;
  return n.block && (e |= 256), e;
}
const Ah = { class: "cm-line" };
function Iu(n, e) {
  let t = e.spec.attributes, i = e.spec.class;
  return !t && !i || (n || (n = { class: "cm-line" }), t && so(t, n), i && (n.class += " " + i)), n;
}
function Nu(n) {
  let e = [];
  for (let t = n.parents.length; t > 1; t--) {
    let i = t == n.parents.length ? n.tile : n.parents[t].tile;
    i instanceof Ce && e.push(i.mark);
  }
  return e;
}
function Es(n) {
  let e = X.get(n);
  return e && e.setDOM(n.cloneNode()), n;
}
class hi extends Ke {
  constructor(e) {
    super(), this.tag = e;
  }
  eq(e) {
    return e.tag == this.tag;
  }
  toDOM() {
    return document.createElement(this.tag);
  }
  updateDOM(e) {
    return e.nodeName.toLowerCase() == this.tag;
  }
  get isHidden() {
    return !0;
  }
}
hi.inline = /* @__PURE__ */ new hi("span");
hi.block = /* @__PURE__ */ new hi("div");
const Rs = /* @__PURE__ */ new class extends Ke {
  toDOM() {
    return document.createElement("br");
  }
  get isHidden() {
    return !0;
  }
  get editable() {
    return !0;
  }
}();
class ol {
  constructor(e) {
    this.view = e, this.decorations = [], this.blockWrappers = [], this.dynamicDecorationMap = [!1], this.domChanged = null, this.hasComposition = null, this.editContextFormatting = B.none, this.lastCompositionAfterCursor = !1, this.minWidth = 0, this.minWidthFrom = 0, this.minWidthTo = 0, this.impreciseAnchor = null, this.impreciseHead = null, this.forceSelection = !1, this.lastUpdate = Date.now(), this.updateDeco(), this.tile = new ms(e, e.contentDOM), this.updateInner([new Ie(0, 0, 0, e.state.doc.length)], null);
  }
  // Update the document view to a given state.
  update(e) {
    var t;
    let i = e.changedRanges;
    this.minWidth > 0 && i.length && (i.every(({ fromA: c, toA: f }) => f < this.minWidthFrom || c > this.minWidthTo) ? (this.minWidthFrom = e.changes.mapPos(this.minWidthFrom, 1), this.minWidthTo = e.changes.mapPos(this.minWidthTo, 1)) : this.minWidth = this.minWidthFrom = this.minWidthTo = 0), this.updateEditContextFormatting(e);
    let s = -1;
    this.view.inputState.composing >= 0 && !this.view.observer.editContext && (!((t = this.domChanged) === null || t === void 0) && t.newSel ? s = this.domChanged.newSel.head : !ju(e.changes, this.hasComposition) && !e.selectionSet && (s = e.state.selection.main.head));
    let r = s > -1 ? Hu(this.view, e.changes, s) : null;
    if (this.domChanged = null, this.hasComposition) {
      let { from: c, to: f } = this.hasComposition;
      i = new Ie(c, f, e.changes.mapPos(c, -1), e.changes.mapPos(f, 1)).addToSet(i.slice());
    }
    this.hasComposition = r ? { from: r.range.fromB, to: r.range.toB } : null, (D.ie || D.chrome) && !r && e && e.state.doc.lines != e.startState.doc.lines && (this.forceSelection = !0);
    let o = this.decorations, l = this.blockWrappers;
    this.updateDeco();
    let a = zu(o, this.decorations, e.changes);
    a.length && (i = Ie.extendWithRanges(i, a));
    let h = $u(l, this.blockWrappers, e.changes);
    return h.length && (i = Ie.extendWithRanges(i, h)), r && !i.some((c) => c.fromA <= r.range.fromA && c.toA >= r.range.toA) && (i = r.range.addToSet(i.slice())), this.tile.flags & 2 && i.length == 0 ? !1 : (this.updateInner(i, r), e.transactions.length && (this.lastUpdate = Date.now()), !0);
  }
  // Used by update and the constructor do perform the actual DOM
  // update
  updateInner(e, t) {
    this.view.viewState.mustMeasureContent = !0;
    let { observer: i } = this.view;
    i.ignore(() => {
      if (t || e.length) {
        let o = this.tile, l = new Ru(this.view, o, this.blockWrappers, this.decorations, this.dynamicDecorationMap);
        t && X.get(t.text) && l.cache.reused.set(
          X.get(t.text),
          2
          /* Reused.DOM */
        ), this.tile = l.run(e, t), Or(o, l.cache.reused);
      }
      this.tile.dom.style.height = this.view.viewState.contentHeight / this.view.scaleY + "px", this.tile.dom.style.flexBasis = this.minWidth ? this.minWidth + "px" : "";
      let r = D.chrome || D.ios ? { node: i.selectionRange.focusNode, written: !1 } : void 0;
      this.tile.sync(r), r && (r.written || i.selectionRange.focusNode != r.node || !this.tile.dom.contains(r.node)) && (this.forceSelection = !0), this.tile.dom.style.height = "";
    });
    let s = [];
    if (this.view.viewport.from || this.view.viewport.to < this.view.state.doc.length)
      for (let r of this.tile.children)
        r.isWidget() && r.widget instanceof Ps && s.push(r.dom);
    i.updateGaps(s);
  }
  updateEditContextFormatting(e) {
    this.editContextFormatting = this.editContextFormatting.map(e.changes);
    for (let t of e.transactions)
      for (let i of t.effects)
        i.is(wh) && (this.editContextFormatting = i.value);
  }
  // Sync the DOM selection to this.state.selection
  updateSelection(e = !1, t = !1) {
    (e || !this.view.observer.selectionRange.focusNode) && this.view.observer.readSelectionRange();
    let { dom: i } = this.tile, s = this.view.root.activeElement, r = s == i, o = !r && !(this.view.state.facet(ht) || i.tabIndex > -1) && Oi(i, this.view.observer.selectionRange) && !(s && i.contains(s));
    if (!(r || t || o))
      return;
    let l = this.forceSelection;
    this.forceSelection = !1;
    let a = this.view.state.selection.main, h, c;
    if (a.empty ? c = h = this.inlineDOMNearPos(a.anchor, a.assoc || 1) : (c = this.inlineDOMNearPos(a.head, a.head == a.from ? 1 : -1), h = this.inlineDOMNearPos(a.anchor, a.anchor == a.from ? 1 : -1)), D.gecko && a.empty && !this.hasComposition && Wu(h)) {
      let u = document.createTextNode("");
      this.view.observer.ignore(() => h.node.insertBefore(u, h.node.childNodes[h.offset] || null)), h = c = new ze(u, 0), l = !0;
    }
    let f = this.view.observer.selectionRange;
    (l || !f.focusNode || (!Li(h.node, h.offset, f.anchorNode, f.anchorOffset) || !Li(c.node, c.offset, f.focusNode, f.focusOffset)) && !this.suppressWidgetCursorChange(f, a)) && (this.view.observer.ignore(() => {
      D.android && D.chrome && i.contains(f.focusNode) && Ku(f.focusNode, i) && (i.blur(), i.focus({ preventScroll: !0 }));
      let u = Fi(this.view.root);
      if (u) if (a.empty) {
        if (D.gecko) {
          let d = Fu(h.node, h.offset);
          if (d && d != 3) {
            let p = (d == 1 ? sh : rh)(h.node, h.offset);
            p && (h = new ze(p.node, p.offset));
          }
        }
        u.collapse(h.node, h.offset), a.bidiLevel != null && u.caretBidiLevel !== void 0 && (u.caretBidiLevel = a.bidiLevel);
      } else if (u.extend) {
        u.collapse(h.node, h.offset);
        try {
          u.extend(c.node, c.offset);
        } catch {
        }
      } else {
        let d = document.createRange();
        a.anchor > a.head && ([h, c] = [c, h]), d.setEnd(c.node, c.offset), d.setStart(h.node, h.offset), u.removeAllRanges(), u.addRange(d);
      }
      o && this.view.root.activeElement == i && (i.blur(), s && s.focus());
    }), this.view.observer.setSelectionRange(h, c)), this.impreciseAnchor = h.precise ? null : new ze(f.anchorNode, f.anchorOffset), this.impreciseHead = c.precise ? null : new ze(f.focusNode, f.focusOffset);
  }
  // If a zero-length widget is inserted next to the cursor during
  // composition, avoid moving it across it and disrupting the
  // composition.
  suppressWidgetCursorChange(e, t) {
    return this.hasComposition && t.empty && Li(e.focusNode, e.focusOffset, e.anchorNode, e.anchorOffset) && this.posFromDOM(e.focusNode, e.focusOffset) == t.head;
  }
  enforceCursorAssoc() {
    if (this.hasComposition)
      return;
    let { view: e } = this, t = e.state.selection.main, i = Fi(e.root), { anchorNode: s, anchorOffset: r } = e.observer.selectionRange;
    if (!i || !t.empty || !t.assoc || !i.modify)
      return;
    let o = this.lineAt(t.head, t.assoc);
    if (!o)
      return;
    let l = o.posAtStart;
    if (t.head == l || t.head == l + o.length)
      return;
    let a = this.coordsAt(t.head, -1), h = this.coordsAt(t.head, 1);
    if (!a || !h || a.bottom > h.top)
      return;
    let c = this.domAtPos(t.head + t.assoc, t.assoc);
    i.collapse(c.node, c.offset), i.modify("move", t.assoc < 0 ? "forward" : "backward", "lineboundary"), e.observer.readSelectionRange();
    let f = e.observer.selectionRange;
    e.docView.posFromDOM(f.anchorNode, f.anchorOffset) != t.from && i.collapse(s, r);
  }
  posFromDOM(e, t) {
    let i = this.tile.nearest(e);
    if (!i)
      return this.tile.dom.compareDocumentPosition(e) & 2 ? 0 : this.view.state.doc.length;
    let s = i.posAtStart;
    if (i.isComposite()) {
      let r;
      if (e == i.dom)
        r = i.dom.childNodes[t];
      else {
        let o = ut(e) == 0 ? 0 : t == 0 ? -1 : 1;
        for (; ; ) {
          let l = e.parentNode;
          if (l == i.dom)
            break;
          o == 0 && l.firstChild != l.lastChild && (e == l.firstChild ? o = -1 : o = 1), e = l;
        }
        o < 0 ? r = e : r = e.nextSibling;
      }
      if (r == i.dom.firstChild)
        return s;
      for (; r && !X.get(r); )
        r = r.nextSibling;
      if (!r)
        return s + i.length;
      for (let o = 0, l = s; ; o++) {
        let a = i.children[o];
        if (a.dom == r)
          return l;
        l += a.length + a.breakAfter;
      }
    } else return i.isText() ? e == i.dom ? s + t : s + (t ? i.length : 0) : s;
  }
  domAtPos(e, t) {
    let { tile: i, offset: s } = this.tile.resolveBlock(e, t);
    return i.isWidget() ? i.domPosFor(e, t) : i.domIn(s, t);
  }
  inlineDOMNearPos(e, t) {
    let i, s = -1, r = !1, o, l = -1, a = !1;
    return this.tile.blockTiles((h, c) => {
      if (h.isWidget()) {
        if (h.flags & 32 && c >= e)
          return !0;
        h.flags & 16 && (r = !0);
      } else {
        let f = c + h.length;
        if (c <= e && (i = h, s = e - c, r = f < e), f >= e && !o && (o = h, l = e - c, a = c > e), c > e && o)
          return !0;
      }
    }), !i && !o ? this.domAtPos(e, t) : (r && o ? i = null : a && i && (o = null), i && t < 0 || !o ? i.domIn(s, t) : o.domIn(l, t));
  }
  coordsAt(e, t) {
    let { tile: i, offset: s } = this.tile.resolveBlock(e, t);
    return i.isWidget() ? i.widget instanceof Ps ? null : i.coordsInWidget(s, t, !0) : i.coordsIn(s, t);
  }
  lineAt(e, t) {
    let { tile: i } = this.tile.resolveBlock(e, t);
    return i.isLine() ? i : null;
  }
  coordsForChar(e) {
    let { tile: t, offset: i } = this.tile.resolveBlock(e, 1);
    if (!t.isLine())
      return null;
    function s(r, o) {
      if (r.isComposite())
        for (let l of r.children) {
          if (l.length >= o) {
            let a = s(l, o);
            if (a)
              return a;
          }
          if (o -= l.length, o < 0)
            break;
        }
      else if (r.isText() && o < r.length) {
        let l = oe(r.text, o);
        if (l == o)
          return null;
        let a = zi(r.dom, o, l).getClientRects();
        for (let h = 0; h < a.length; h++) {
          let c = a[h];
          if (h == a.length - 1 || c.top < c.bottom && c.left < c.right)
            return c;
        }
      }
      return null;
    }
    return s(t, i);
  }
  measureVisibleLineHeights(e) {
    let t = [], { from: i, to: s } = e, r = this.view.contentDOM.clientWidth, o = r > Math.max(this.view.scrollDOM.clientWidth, this.minWidth) + 1, l = -1, a = this.view.textDirection == _.LTR, h = 0, c = (f, u, d) => {
      for (let p = 0; p < f.children.length && !(u > s); p++) {
        let m = f.children[p], g = u + m.length, y = m.dom.getBoundingClientRect(), { height: w } = y;
        if (d && !p && (h += y.top - d.top), m instanceof ct)
          g > i && c(m, u, y);
        else if (u >= i && (h > 0 && t.push(-h), t.push(w + h), h = 0, o)) {
          let v = m.dom.lastChild, O = v ? Bi(v) : [];
          if (O.length) {
            let k = O[O.length - 1], S = a ? k.right - y.left : y.right - k.left;
            S > l && (l = S, this.minWidth = r, this.minWidthFrom = u, this.minWidthTo = g);
          }
        }
        d && p == f.children.length - 1 && (h += d.bottom - y.bottom), u = g + m.breakAfter;
      }
    };
    return c(this.tile, 0, null), t;
  }
  textDirectionAt(e) {
    let { tile: t } = this.tile.resolveBlock(e, 1);
    return getComputedStyle(t.dom).direction == "rtl" ? _.RTL : _.LTR;
  }
  measureTextSize() {
    let e = this.tile.blockTiles((o) => {
      if (o.isLine() && o.children.length && o.length <= 20) {
        let l = 0, a;
        for (let h of o.children) {
          if (!h.isText() || /[^ -~]/.test(h.text))
            return;
          let c = Bi(h.dom);
          if (c.length != 1)
            return;
          l += c[0].width, a = c[0].height;
        }
        if (l)
          return {
            lineHeight: o.dom.getBoundingClientRect().height,
            charWidth: l / o.length,
            textHeight: a
          };
      }
    });
    if (e)
      return e;
    let t = document.createElement("div"), i, s, r;
    return t.className = "cm-line", t.style.width = "99999px", t.style.position = "absolute", t.textContent = "abc def ghi jkl mno pqr stu", this.view.observer.ignore(() => {
      this.tile.dom.appendChild(t);
      let o = Bi(t.firstChild)[0];
      i = t.getBoundingClientRect().height, s = o && o.width ? o.width / 27 : 7, r = o && o.height ? o.height : i, t.remove();
    }), { lineHeight: i, charWidth: s, textHeight: r };
  }
  computeBlockGapDeco() {
    let e = [], t = this.view.viewState;
    for (let i = 0, s = 0; ; s++) {
      let r = s == t.viewports.length ? null : t.viewports[s], o = r ? r.from - 1 : this.view.state.doc.length;
      if (o > i) {
        let l = (t.lineBlockAt(o).bottom - t.lineBlockAt(i).top) / this.view.scaleY;
        e.push(B.replace({
          widget: new Ps(l),
          block: !0,
          inclusive: !0,
          isBlockGap: !0
        }).range(i, o));
      }
      if (!r)
        break;
      i = r.to + 1;
    }
    return B.set(e);
  }
  updateDeco() {
    let e = 1, t = this.view.state.facet(ds).map((r) => (this.dynamicDecorationMap[e++] = typeof r == "function") ? r(this.view) : r), i = !1, s = this.view.state.facet(co).map((r, o) => {
      let l = typeof r == "function";
      return l && (i = !0), l ? r(this.view) : r;
    });
    for (s.length && (this.dynamicDecorationMap[e++] = i, t.push(W.join(s))), this.decorations = [
      this.editContextFormatting,
      ...t,
      this.computeBlockGapDeco(),
      this.view.viewState.lineGapDeco
    ]; e < this.decorations.length; )
      this.dynamicDecorationMap[e++] = !1;
    this.blockWrappers = this.view.state.facet(kh).map((r) => typeof r == "function" ? r(this.view) : r);
  }
  scrollIntoView(e) {
    var t;
    if (e.isSnapshot) {
      let c = this.view.viewState.lineBlockAt(e.range.head);
      this.view.scrollDOM.scrollTop = c.top - e.yMargin, this.view.scrollDOM.scrollLeft = e.xMargin;
      return;
    }
    for (let c of this.view.state.facet(xh))
      try {
        if (c(this.view, e.range, e))
          return !0;
      } catch (f) {
        Ae(this.view.state, f, "scroll handler");
      }
    let { range: i } = e, s = this.coordsAt(i.head, (t = i.assoc) !== null && t !== void 0 ? t : i.empty ? 0 : i.head > i.anchor ? -1 : 1), r;
    if (!s)
      return;
    !i.empty && (r = this.coordsAt(i.anchor, i.anchor > i.head ? -1 : 1)) && (s = {
      left: Math.min(s.left, r.left),
      top: Math.min(s.top, r.top),
      right: Math.max(s.right, r.right),
      bottom: Math.max(s.bottom, r.bottom)
    });
    let o = fo(this.view), l = {
      left: s.left - o.left,
      top: s.top - o.top,
      right: s.right + o.right,
      bottom: s.bottom + o.bottom
    }, { offsetWidth: a, offsetHeight: h } = this.view.scrollDOM;
    if (fu(this.view.scrollDOM, l, i.head < i.anchor ? -1 : 1, e.x, e.y, Math.max(Math.min(e.xMargin, a), -a), Math.max(Math.min(e.yMargin, h), -h), this.view.textDirection == _.LTR), window.visualViewport && window.innerHeight - window.visualViewport.height > 1 && (s.top > window.pageYOffset + window.visualViewport.offsetTop + window.visualViewport.height || s.bottom < window.pageYOffset + window.visualViewport.offsetTop)) {
      let c = this.view.docView.lineAt(i.head, 1);
      c && c.dom.scrollIntoView({ block: "nearest" });
    }
  }
  lineHasWidget(e) {
    let t = (i) => i.isWidget() || i.children.some(t);
    return t(this.tile.resolveBlock(e, 1).tile);
  }
  destroy() {
    Or(this.tile);
  }
}
function Or(n, e) {
  let t = e?.get(n);
  if (t != 1) {
    t == null && n.destroy();
    for (let i of n.children)
      Or(i, e);
  }
}
function Wu(n) {
  return n.node.nodeType == 1 && n.node.firstChild && (n.offset == 0 || n.node.childNodes[n.offset - 1].contentEditable == "false") && (n.offset == n.node.childNodes.length || n.node.childNodes[n.offset].contentEditable == "false");
}
function Mh(n, e) {
  let t = n.observer.selectionRange;
  if (!t.focusNode)
    return null;
  let i = sh(t.focusNode, t.focusOffset), s = rh(t.focusNode, t.focusOffset), r = i || s;
  if (s && i && s.node != i.node) {
    let l = X.get(s.node);
    if (!l || l.isText() && l.text != s.node.nodeValue)
      r = s;
    else if (n.docView.lastCompositionAfterCursor) {
      let a = X.get(i.node);
      !a || a.isText() && a.text != i.node.nodeValue || (r = s);
    }
  }
  if (n.docView.lastCompositionAfterCursor = r != i, !r)
    return null;
  let o = e - r.offset;
  return { from: o, to: o + r.node.nodeValue.length, node: r.node };
}
function Hu(n, e, t) {
  let i = Mh(n, t);
  if (!i)
    return null;
  let { node: s, from: r, to: o } = i, l = s.nodeValue;
  if (/[\n\r]/.test(l) || n.state.doc.sliceString(i.from, i.to) != l)
    return null;
  let a = e.invertedDesc;
  return { range: new Ie(a.mapPos(r), a.mapPos(o), r, o), text: s };
}
function Fu(n, e) {
  return n.nodeType != 1 ? 0 : (e && n.childNodes[e - 1].contentEditable == "false" ? 1 : 0) | (e < n.childNodes.length && n.childNodes[e].contentEditable == "false" ? 2 : 0);
}
let Vu = class {
  constructor() {
    this.changes = [];
  }
  compareRange(e, t) {
    ii(e, t, this.changes);
  }
  comparePoint(e, t) {
    ii(e, t, this.changes);
  }
  boundChange(e) {
    ii(e, e, this.changes);
  }
};
function zu(n, e, t) {
  let i = new Vu();
  return W.compare(n, e, t, i), i.changes;
}
class qu {
  constructor() {
    this.changes = [];
  }
  compareRange(e, t) {
    ii(e, t, this.changes);
  }
  comparePoint() {
  }
  boundChange(e) {
    ii(e, e, this.changes);
  }
}
function $u(n, e, t) {
  let i = new qu();
  return W.compare(n, e, t, i), i.changes;
}
function Ku(n, e) {
  for (let t = n; t && t != e; t = t.assignedSlot || t.parentNode)
    if (t.nodeType == 1 && t.contentEditable == "false")
      return !0;
  return !1;
}
function ju(n, e) {
  let t = !1;
  return e && n.iterChangedRanges((i, s) => {
    i < e.to && s > e.from && (t = !0);
  }), t;
}
class Ps extends Ke {
  constructor(e) {
    super(), this.height = e;
  }
  toDOM() {
    let e = document.createElement("div");
    return e.className = "cm-gap", this.updateDOM(e), e;
  }
  eq(e) {
    return e.height == this.height;
  }
  updateDOM(e) {
    return e.style.height = this.height + "px", !0;
  }
  get editable() {
    return !0;
  }
  get estimatedHeight() {
    return this.height;
  }
  ignoreEvent() {
    return !1;
  }
}
function Uu(n, e, t = 1) {
  let i = n.charCategorizer(e), s = n.doc.lineAt(e), r = e - s.from;
  if (s.length == 0)
    return b.cursor(e);
  r == 0 ? t = 1 : r == s.length && (t = -1);
  let o = r, l = r;
  t < 0 ? o = oe(s.text, r, !1) : l = oe(s.text, r);
  let a = i(s.text.slice(o, l));
  for (; o > 0; ) {
    let h = oe(s.text, o, !1);
    if (i(s.text.slice(h, o)) != a)
      break;
    o = h;
  }
  for (; l < s.length; ) {
    let h = oe(s.text, l);
    if (i(s.text.slice(l, h)) != a)
      break;
    l = h;
  }
  return b.undirectionalRange(o + s.from, l + s.from);
}
function _u(n, e, t, i, s) {
  let r = Math.round((i - e.left) * n.defaultCharacterWidth);
  if (n.lineWrapping && t.height > n.defaultLineHeight * 1.5) {
    let l = n.viewState.heightOracle.textHeight, a = Math.floor((s - t.top - (n.defaultLineHeight - l) * 0.5) / l);
    r += a * n.viewState.heightOracle.lineLength;
  }
  let o = n.state.sliceDoc(t.from, t.to);
  return t.from + yr(o, r, n.state.tabSize);
}
function Br(n, e, t) {
  let i = n.lineBlockAt(e);
  if (Array.isArray(i.type)) {
    let s;
    for (let r of i.type) {
      if (r.from > e)
        break;
      if (!(r.to < e)) {
        if (r.from < e && r.to > e)
          return r;
        (!s || r.type == de.Text && (s.type != r.type || (t < 0 ? r.from < e : r.to > e))) && (s = r);
      }
    }
    return s || i;
  }
  return i;
}
function Gu(n, e, t, i) {
  let s = Br(n, e.head, e.assoc || -1), r = !i || s.type != de.Text || !(n.lineWrapping || s.widgetLineBreaks) ? null : n.coordsAtPos(e.assoc < 0 && e.head > s.from ? e.head - 1 : e.head);
  if (r) {
    let o = n.dom.getBoundingClientRect(), l = n.textDirectionAt(s.from), a = n.posAtCoords({
      x: t == (l == _.LTR) ? o.right - 1 : o.left + 1,
      y: (r.top + r.bottom) / 2
    });
    if (a != null)
      return b.cursor(a, t ? -1 : 1);
  }
  return b.cursor(t ? s.to : s.from, t ? -1 : 1);
}
function ll(n, e, t, i) {
  let s = n.state.doc.lineAt(e.head), r = n.bidiSpans(s), o = n.textDirectionAt(s.from);
  for (let l = e, a = null; ; ) {
    let h = ku(s, r, o, l, t), c = ch;
    if (!h) {
      if (s.number == (t ? n.state.doc.lines : 1))
        return l;
      c = `
`, s = n.state.doc.line(s.number + (t ? 1 : -1)), r = n.bidiSpans(s), h = n.visualLineSide(s, !t);
    }
    if (a) {
      if (!a(c))
        return l;
    } else {
      if (!i)
        return h;
      a = i(c);
    }
    l = h;
  }
}
function Yu(n, e, t) {
  let i = n.state.charCategorizer(e), s = i(t);
  return (r) => {
    let o = i(r);
    return s == Y.Space && (s = o), s == o;
  };
}
function Ju(n, e, t, i) {
  let s = e.head, r = t ? 1 : -1;
  if (s == (t ? n.state.doc.length : 0))
    return b.cursor(s, e.assoc);
  let o = e.goalColumn, l, a = n.contentDOM.getBoundingClientRect(), h = n.coordsAtPos(s, e.assoc || ((e.empty ? t : e.head == e.from) ? 1 : -1)), c = n.documentTop;
  if (h)
    o == null && (o = h.left - a.left), l = r < 0 ? h.top : h.bottom;
  else {
    let p = n.viewState.lineBlockAt(s);
    o == null && (o = Math.min(a.right - a.left, n.defaultCharacterWidth * (s - p.from))), l = (r < 0 ? p.top : p.bottom) + c;
  }
  let f = a.left + o, u = n.viewState.heightOracle.textHeight >> 1, d = i ?? u;
  for (let p = 0; ; p += u) {
    let m = l + (d + p) * r, g = Lr(n, { x: f, y: m }, !1, r);
    if (t ? m > a.bottom : m < a.top)
      return b.cursor(g.pos, g.assoc);
    let y = n.coordsAtPos(g.pos, g.assoc), w = y ? (y.top + y.bottom) / 2 : 0;
    if (!y || (t ? w > l : w < l))
      return b.cursor(g.pos, g.assoc, void 0, o);
  }
}
function Ei(n, e, t) {
  for (; ; ) {
    let i = 0;
    for (let s of n)
      s.between(e - 1, e + 1, (r, o, l) => {
        if (e > r && e < o) {
          let a = i || t || (e - r < o - e ? -1 : 1);
          e = a < 0 ? r : o, i = a;
        }
      });
    if (!i)
      return e;
  }
}
function Th(n, e) {
  let t = null;
  for (let i = 0; i < e.ranges.length; i++) {
    let s = e.ranges[i], r = null;
    if (s.empty) {
      let o = Ei(n, s.from, 0);
      o != s.from && (r = b.cursor(o, -1));
    } else {
      let o = Ei(n, s.from, -1), l = Ei(n, s.to, 1);
      (o != s.from || l != s.to) && (s.undirectional ? r = b.undirectionalRange(s.from, s.to) : r = b.range(s.from == s.anchor ? o : l, s.from == s.head ? o : l));
    }
    r && (t || (t = e.ranges.slice()), t[i] = r);
  }
  return t ? b.create(t, e.mainIndex) : e;
}
function Is(n, e, t) {
  let i = Ei(n.state.facet(en).map((s) => s(n)), t.from, e.head > t.from ? -1 : 1);
  return i == t.from ? t : b.cursor(i, i < t.from ? 1 : -1);
}
class tt {
  constructor(e, t) {
    this.pos = e, this.assoc = t;
  }
}
function Lr(n, e, t, i) {
  let s = n.contentDOM.getBoundingClientRect(), r = s.top + n.viewState.paddingTop, { x: o, y: l } = e, a = l - r, h;
  for (; ; ) {
    if (a < 0)
      return new tt(0, 1);
    if (a > n.viewState.docHeight)
      return new tt(n.state.doc.length, -1);
    if (h = n.elementAtHeight(a), i == null)
      break;
    if (h.type == de.Text) {
      if (i < 0 ? h.to < n.viewport.from : h.from > n.viewport.to)
        break;
      let u = n.docView.coordsAt(i < 0 ? h.from : h.to, i > 0 ? -1 : 1);
      if (u && (i < 0 ? u.top <= a + r : u.bottom >= a + r))
        break;
    }
    let f = n.viewState.heightOracle.textHeight / 2;
    a = i > 0 ? h.bottom + f : h.top - f;
  }
  if (n.viewport.from >= h.to || n.viewport.to <= h.from) {
    if (t)
      return null;
    if (h.type == de.Text) {
      let f = _u(n, s, h, o, l);
      return new tt(f, f == h.from ? 1 : -1);
    }
  }
  if (h.type != de.Text)
    return a < (h.top + h.bottom) / 2 ? new tt(h.from, 1) : new tt(h.to, -1);
  let c = n.docView.lineAt(h.from, 2);
  return (!c || c.length != h.length) && (c = n.docView.lineAt(h.from, -2)), new Xu(n, o, l, n.textDirectionAt(h.from)).scanTile(c, h.from);
}
class Xu {
  constructor(e, t, i, s) {
    this.view = e, this.x = t, this.y = i, this.baseDir = s, this.line = null, this.spans = null;
  }
  bidiSpansAt(e) {
    return (!this.line || this.line.from > e || this.line.to < e) && (this.line = this.view.state.doc.lineAt(e), this.spans = this.view.bidiSpans(this.line)), this;
  }
  baseDirAt(e, t) {
    let { line: i, spans: s } = this.bidiSpansAt(e);
    return s[it.find(s, e - i.from, -1, t)].level == this.baseDir;
  }
  dirAt(e, t) {
    let { line: i, spans: s } = this.bidiSpansAt(e);
    return s[it.find(s, e - i.from, -1, t)].dir;
  }
  // Used to short-circuit bidi tests for content with a uniform direction
  bidiIn(e, t) {
    let { spans: i, line: s } = this.bidiSpansAt(e);
    return i.length > 1 || i.length && (i[0].level != this.baseDir || i[0].to + s.from < t);
  }
  // Scan through the rectangles for the content of a tile with inline
  // content, looking for one that overlaps the queried position
  // vertically and is closest horizontally. The caller is responsible
  // for dividing its content into N pieces, and pass an array with
  // N+1 positions (including the position after the last piece). For
  // a text tile, these will be character clusters, for a composite
  // tile, these will be child tiles.
  scan(e, t, i = !1) {
    let s = 0, r = e.length - 1, o = /* @__PURE__ */ new Set(), l = this.bidiIn(e[0], e[r]), a, h, c = -1, f = 1e9, u;
    e: for (; s < r; ) {
      let p = r - s, m = s + r >> 1;
      t: if (o.has(m)) {
        let y = s + Math.floor(Math.random() * p);
        for (let w = 0; w < p; w++) {
          if (!o.has(y)) {
            m = y;
            break t;
          }
          y++, y == r && (y = s);
        }
        break e;
      }
      o.add(m);
      let g = t(m);
      if (g)
        for (let y = 0; y < g.length; y++) {
          let w = g[y], v = 0;
          if (!(w.width == 0 && g.length > 1)) {
            if (w.bottom < this.y)
              (!a || a.bottom < w.bottom) && (a = w), v = 1;
            else if (w.top > this.y)
              (!h || h.top > w.top) && (h = w), v = -1;
            else {
              let O = w.left > this.x ? this.x - w.left : w.right < this.x ? this.x - w.right : 0, k = Math.abs(O);
              k < f && (c = m, f = k, u = w), O && (v = O < 0 == (this.baseDir == _.LTR) ? -1 : 1);
            }
            v == -1 && (!l || this.baseDirAt(e[m], 1)) ? r = m : v == 1 && (!l || this.baseDirAt(e[m + 1], -1)) && (s = m + 1);
          }
        }
    }
    if (!u) {
      if (!h && !a)
        return { i: e[0], after: !1 };
      let p = a && (!h || this.y - a.bottom < h.top - this.y) ? a : h;
      return this.y = (p.top + p.bottom) / 2, this.scan(e, t, !0);
    }
    if (f && !i) {
      let { top: p, bottom: m } = u;
      if (a && a.bottom > (p + p + m) / 3)
        return this.y = a.bottom - 1, this.scan(e, t, !0);
      if (h && h.top < (p + m + m) / 3)
        return this.y = h.top + 1, this.scan(e, t, !0);
    }
    let d = (l ? this.dirAt(e[c], 1) : this.baseDir) == _.LTR;
    return {
      i: c,
      // Test whether x is closes to the start or end of this element
      after: this.x > (u.left + u.right) / 2 == d
    };
  }
  scanText(e, t) {
    let i = [];
    for (let r = 0; r < e.length; r = oe(e.text, r))
      i.push(t + r);
    i.push(t + e.length);
    let s = this.scan(i, (r) => {
      let o = i[r] - t, l = i[r + 1] - t;
      return zi(e.dom, o, l).getClientRects();
    });
    return s.after ? new tt(i[s.i + 1], -1) : new tt(i[s.i], 1);
  }
  scanTile(e, t) {
    if (!e.length)
      return new tt(t, 1);
    if (e.children.length == 1) {
      let l = e.children[0];
      if (l.isText())
        return this.scanText(l, t);
      if (l.isComposite())
        return this.scanTile(l, t);
    }
    let i = [t];
    for (let l = 0, a = t; l < e.children.length; l++)
      i.push(a += e.children[l].length);
    let s = this.scan(i, (l) => {
      let a = e.children[l];
      return a.flags & 48 ? null : (a.dom.nodeType == 1 ? a.dom : zi(a.dom, 0, a.length)).getClientRects();
    }), r = e.children[s.i], o = i[s.i];
    return r.isText() ? this.scanText(r, o) : r.isComposite() ? this.scanTile(r, o) : s.after ? new tt(i[s.i + 1], -1) : new tt(o, 1);
  }
}
const Gt = "￿";
class Qu {
  constructor(e, t) {
    this.points = e, this.view = t, this.text = "", this.lineSeparator = t.state.facet(F.lineSeparator);
  }
  append(e) {
    this.text += e;
  }
  lineBreak() {
    this.text += Gt;
  }
  readRange(e, t) {
    if (!e)
      return this;
    let i = e.parentNode;
    for (let s = e; ; ) {
      this.findPointBefore(i, s);
      let r = this.text.length;
      this.readNode(s);
      let o = X.get(s), l = s.nextSibling;
      if (l == t) {
        o?.breakAfter && !l && i != this.view.contentDOM && this.lineBreak();
        break;
      }
      let a = X.get(l);
      (o && a ? o.breakAfter : (o ? o.breakAfter : Kn(s)) || Kn(l) && (s.nodeName != "BR" || o?.isWidget()) && this.text.length > r) && !ed(l, t) && this.lineBreak(), s = l;
    }
    return this.findPointBefore(i, t), this;
  }
  readTextNode(e) {
    let t = e.nodeValue;
    for (let i of this.points)
      i.node == e && (i.pos = this.text.length + Math.min(i.offset, t.length));
    for (let i = 0, s = this.lineSeparator ? null : /\r\n?|\n/g; ; ) {
      let r = -1, o = 1, l;
      if (this.lineSeparator ? (r = t.indexOf(this.lineSeparator, i), o = this.lineSeparator.length) : (l = s.exec(t)) && (r = l.index, o = l[0].length), this.append(t.slice(i, r < 0 ? t.length : r)), r < 0)
        break;
      if (this.lineBreak(), o > 1)
        for (let a of this.points)
          a.node == e && a.pos > this.text.length && (a.pos -= o - 1);
      i = r + o;
    }
  }
  readNode(e) {
    let t = X.get(e), i = t && t.overrideDOMText;
    if (i != null) {
      this.findPointInside(e, i.length);
      for (let s = i.iter(); !s.next().done; )
        s.lineBreak ? this.lineBreak() : this.append(s.value);
    } else e.nodeType == 3 ? this.readTextNode(e) : e.nodeName == "BR" ? e.nextSibling && this.lineBreak() : e.nodeType == 1 && this.readRange(e.firstChild, null);
  }
  findPointBefore(e, t) {
    for (let i of this.points)
      i.node == e && e.childNodes[i.offset] == t && (i.pos = this.text.length);
  }
  findPointInside(e, t) {
    for (let i of this.points)
      (e.nodeType == 3 ? i.node == e : e.contains(i.node)) && (i.pos = this.text.length + (Zu(e, i.node, i.offset) ? t : 0));
  }
}
function Zu(n, e, t) {
  for (; ; ) {
    if (!e || t < ut(e))
      return !1;
    if (e == n)
      return !0;
    t = St(e) + 1, e = e.parentNode;
  }
}
function ed(n, e) {
  let t;
  for (; !(n == e || !n); n = n.nextSibling) {
    let i = X.get(n);
    if (!i?.isWidget())
      return !1;
    i && (t || (t = [])).push(i);
  }
  if (t)
    for (let i of t) {
      let s = i.overrideDOMText;
      if (s?.length)
        return !1;
    }
  return !0;
}
class al {
  constructor(e, t) {
    this.node = e, this.offset = t, this.pos = -1;
  }
}
class td {
  constructor(e, t, i, s) {
    this.typeOver = s, this.bounds = null, this.text = "", this.domChanged = t > -1;
    let { impreciseHead: r, impreciseAnchor: o } = e.docView, l = e.state.selection;
    if (e.state.readOnly && t > -1)
      this.newSel = null;
    else if (t > -1 && (this.bounds = Dh(e.docView.tile, t, i, 0))) {
      let a = r || o ? [] : nd(e), h = new Qu(a, e);
      h.readRange(this.bounds.startDOM, this.bounds.endDOM), this.text = h.text, this.newSel = sd(a, this.bounds.from);
    } else {
      let a = e.observer.selectionRange, h = r && r.node == a.focusNode && r.offset == a.focusOffset || !Cr(e.contentDOM, a.focusNode) ? l.main.head : e.docView.posFromDOM(a.focusNode, a.focusOffset), c = o && o.node == a.anchorNode && o.offset == a.anchorOffset || !Cr(e.contentDOM, a.anchorNode) ? l.main.anchor : e.docView.posFromDOM(a.anchorNode, a.anchorOffset), f = e.viewport;
      if ((D.ios || D.chrome) && h != c && Math.min(h, c) <= l.main.from && Math.max(h, c) >= l.main.to && (f.from > 0 || f.to < e.state.doc.length)) {
        let u = Math.min(h, c), d = Math.max(h, c), p = f.from - u, m = f.to - d;
        (p == 0 || p == 1 || u == 0) && (m == 0 || m == -1 || d == e.state.doc.length) && (h = 0, c = e.state.doc.length);
      }
      if (e.inputState.composing > -1 && l.ranges.length > 1)
        this.newSel = l.replaceRange(b.range(c, h));
      else if (e.lineWrapping && c == h && !(l.main.empty && l.main.head == h) && e.inputState.lastTouchTime > Date.now() - 100) {
        let u = e.coordsAtPos(h, -1), d = 0;
        u && (d = e.inputState.lastTouchY <= u.bottom ? -1 : 1), this.newSel = b.create([b.cursor(h, d)]);
      } else
        this.newSel = b.single(c, h);
    }
  }
}
function Dh(n, e, t, i) {
  if (n.isComposite()) {
    let s = -1, r = -1, o = -1, l = -1;
    for (let a = 0, h = i, c = i; a < n.children.length; a++) {
      let f = n.children[a], u = h + f.length;
      if (h < e && u > t)
        return Dh(f, e, t, h);
      if (u >= e && s == -1 && (s = a, r = h), h > t && f.dom.parentNode == n.dom) {
        o = a, l = c;
        break;
      }
      c = u, h = u + f.breakAfter;
    }
    return {
      from: r,
      to: l < 0 ? i + n.length : l,
      startDOM: (s ? n.children[s - 1].dom.nextSibling : null) || n.dom.firstChild,
      endDOM: o < n.children.length && o >= 0 ? n.children[o].dom : null
    };
  } else return n.isText() ? { from: i, to: i + n.length, startDOM: n.dom, endDOM: n.dom.nextSibling } : null;
}
function Oh(n, e) {
  let t, { newSel: i } = e, { state: s } = n, r = s.selection.main, o = n.inputState.lastKeyTime > Date.now() - 100 ? n.inputState.lastKeyCode : -1;
  if (e.bounds) {
    let { from: l, to: a } = e.bounds, h = r.from, c = null;
    (o === 8 || D.android && e.text.length < a - l) && (h = r.to, c = "end");
    let f = s.doc.sliceString(l, a, Gt), u, d;
    !r.empty && r.from >= l && r.to <= a && (e.typeOver || f != e.text) && f.slice(0, r.from - l) == e.text.slice(0, r.from - l) && f.slice(r.to - l) == e.text.slice(u = e.text.length - (f.length - (r.to - l))) ? t = {
      from: r.from,
      to: r.to,
      insert: z.of(e.text.slice(r.from - l, u).split(Gt))
    } : (d = Bh(f, e.text, h - l, c)) && (D.chrome && o == 13 && d.toB == d.from + 2 && e.text.slice(d.from, d.toB) == Gt + Gt && d.toB--, t = {
      from: l + d.from,
      to: l + d.toA,
      insert: z.of(e.text.slice(d.from, d.toB).split(Gt))
    });
  } else i && (!n.hasFocus && s.facet(ht) || Gn(i, r)) && (i = null);
  if (!t && !i)
    return !1;
  if ((D.mac || D.android) && t && t.from == t.to && t.from == r.head - 1 && /^\. ?$/.test(t.insert.toString()) && n.contentDOM.getAttribute("autocorrect") == "off" ? (i && t.insert.length == 2 && (i = b.single(i.main.anchor - 1, i.main.head - 1)), t = { from: t.from, to: t.to, insert: z.of([t.insert.toString().replace(".", " ")]) }) : s.doc.lineAt(r.from).to < r.to && n.docView.lineHasWidget(r.to) && n.inputState.insertingTextAt > Date.now() - 50 ? t = {
    from: r.from,
    to: r.to,
    insert: s.toText(n.inputState.insertingText)
  } : D.chrome && t && t.from == t.to && t.from == r.head && t.insert.toString() == `
 ` && n.lineWrapping && (i && (i = b.single(i.main.anchor - 1, i.main.head - 1)), t = { from: r.from, to: r.to, insert: z.of([" "]) }), t)
    return uo(n, t, i, o);
  if (i && !Gn(i, r)) {
    let l = !1, a = "select";
    return n.inputState.lastSelectionTime > Date.now() - 50 && (n.inputState.lastSelectionOrigin == "select" && (l = !0), a = n.inputState.lastSelectionOrigin, a == "select.pointer" && (i = Th(s.facet(en).map((h) => h(n)), i))), n.dispatch({ selection: i, scrollIntoView: l, userEvent: a }), !0;
  } else
    return !1;
}
function uo(n, e, t, i = -1) {
  if (D.ios && n.inputState.flushIOSKey(e))
    return !0;
  let s = n.state.selection.main;
  if (D.android && (e.to == s.to && // GBoard will sometimes remove a space it just inserted
  // after a completion when you press enter
  (e.from == s.from || e.from == s.from - 1 && n.state.sliceDoc(e.from, s.from) == " ") && e.insert.length == 1 && e.insert.lines == 2 && ni(n.contentDOM, "Enter", 13) || (e.from == s.from - 1 && e.to == s.to && e.insert.length == 0 || i == 8 && e.insert.length < e.to - e.from && e.to > s.head) && ni(n.contentDOM, "Backspace", 8) || e.from == s.from && e.to == s.to + 1 && e.insert.length == 0 && ni(n.contentDOM, "Delete", 46)))
    return !0;
  let r = e.insert.toString();
  n.inputState.composing >= 0 && n.inputState.composing++;
  let o, l = () => o || (o = id(n, e, t));
  return n.state.facet(mh).some((a) => a(n, e.from, e.to, r, l)) || n.dispatch(l()), !0;
}
function id(n, e, t) {
  let i, s = n.state, r = s.selection.main, o = -1;
  if (e.from == e.to && e.from < r.from || e.from > r.to) {
    let a = e.from < r.from ? -1 : 1, h = a < 0 ? r.from : r.to, c = Ei(s.facet(en).map((f) => f(n)), h, a);
    e.from == c && (o = c);
  }
  if (o > -1)
    i = {
      changes: e,
      selection: b.cursor(e.from + e.insert.length, -1)
    };
  else if (e.from >= r.from && e.to <= r.to && e.to - e.from >= (r.to - r.from) / 3 && (!t || t.main.empty && t.main.from == e.from + e.insert.length) && n.inputState.composing < 0) {
    let a = r.from < e.from ? s.sliceDoc(r.from, e.from) : "", h = r.to > e.to ? s.sliceDoc(e.to, r.to) : "";
    i = s.replaceSelection(n.state.toText(a + e.insert.sliceString(0, void 0, n.state.lineBreak) + h));
  } else {
    let a = s.changes(e), h = t && t.main.to <= a.newLength ? t.main : void 0;
    if (s.selection.ranges.length > 1 && (n.inputState.composing >= 0 || n.inputState.compositionPendingChange) && e.to <= r.to + 10 && e.to >= r.to - 10) {
      let c = n.state.sliceDoc(e.from, e.to), f, u = t && Mh(n, t.main.head);
      if (u) {
        let p = e.insert.length - (e.to - e.from);
        f = { from: u.from, to: u.to - p };
      } else
        f = n.state.doc.lineAt(r.head);
      let d = r.to - e.to;
      i = s.changeByRange((p) => {
        if (p.from == r.from && p.to == r.to)
          return { changes: a, range: h || p.map(a) };
        let m = p.to - d, g = m - c.length;
        if (n.state.sliceDoc(g, m) != c || // Unfortunately, there's no way to make multiple
        // changes in the same node work without aborting
        // composition, so cursors in the composition range are
        // ignored.
        m >= f.from && g <= f.to)
          return { range: p };
        let y = s.changes({ from: g, to: m, insert: e.insert }), w = p.to - r.to;
        return {
          changes: y,
          range: h ? b.range(Math.max(0, h.anchor + w), Math.max(0, h.head + w)) : p.map(y)
        };
      });
    } else
      i = {
        changes: a,
        selection: h && s.selection.replaceRange(h)
      };
  }
  let l = "input.type";
  return (n.composing || n.inputState.compositionPendingChange && n.inputState.compositionEndedAt > Date.now() - 50) && (n.inputState.compositionPendingChange = !1, l += ".compose", n.inputState.compositionFirstChange && (l += ".start", n.inputState.compositionFirstChange = !1)), s.update(i, { userEvent: l, scrollIntoView: !0 });
}
function Bh(n, e, t, i) {
  let s = Math.min(n.length, e.length), r = 0;
  for (; r < s && n.charCodeAt(r) == e.charCodeAt(r); )
    r++;
  if (r == s && n.length == e.length)
    return null;
  let o = n.length, l = e.length;
  for (; o > 0 && l > 0 && n.charCodeAt(o - 1) == e.charCodeAt(l - 1); )
    o--, l--;
  if (i == "end") {
    let a = Math.max(0, r - Math.min(o, l));
    t -= o + a - r;
  }
  if (o < r && n.length < e.length) {
    let a = t <= r && t >= o ? r - t : 0;
    r -= a, l = r + (l - o), o = r;
  } else if (l < r) {
    let a = t <= r && t >= l ? r - t : 0;
    r -= a, o = r + (o - l), l = r;
  }
  return { from: r, toA: o, toB: l };
}
function nd(n) {
  let e = [];
  if (n.root.activeElement != n.contentDOM)
    return e;
  let { anchorNode: t, anchorOffset: i, focusNode: s, focusOffset: r } = n.observer.selectionRange;
  return t && (e.push(new al(t, i)), (s != t || r != i) && e.push(new al(s, r))), e;
}
function sd(n, e) {
  if (n.length == 0)
    return null;
  let t = n[0].pos, i = n.length == 2 ? n[1].pos : t;
  return t > -1 && i > -1 ? b.single(t + e, i + e) : null;
}
function Gn(n, e) {
  return e.head == n.main.head && e.anchor == n.main.anchor;
}
class rd {
  setSelectionOrigin(e) {
    this.lastSelectionOrigin = e, this.lastSelectionTime = Date.now();
  }
  constructor(e) {
    this.view = e, this.lastKeyCode = 0, this.lastKeyTime = 0, this.lastTouchTime = 0, this.lastTouchX = 0, this.lastTouchY = 0, this.lastFocusTime = 0, this.lastScrollTop = 0, this.lastScrollLeft = 0, this.lastWheelEvent = 0, this.pendingIOSKey = void 0, this.tabFocusMode = -1, this.lastSelectionOrigin = null, this.lastSelectionTime = 0, this.lastContextMenu = 0, this.scrollHandlers = [], this.handlers = /* @__PURE__ */ Object.create(null), this.composing = -1, this.compositionFirstChange = null, this.compositionEndedAt = 0, this.compositionPendingKey = !1, this.compositionPendingChange = !1, this.insertingText = "", this.insertingTextAt = 0, this.mouseSelection = null, this.draggedContent = null, this.handleEvent = this.handleEvent.bind(this), this.notifiedFocused = e.hasFocus, D.safari && e.contentDOM.addEventListener("input", () => null), D.gecko && vd(e.contentDOM.ownerDocument);
  }
  handleEvent(e) {
    !pd(this.view, e) || this.ignoreDuringComposition(e) || e.type == "keydown" && this.keydown(e) || (this.view.updateState != 0 ? Promise.resolve().then(() => this.runHandlers(e.type, e)) : this.runHandlers(e.type, e));
  }
  runHandlers(e, t) {
    let i = this.handlers[e];
    if (i) {
      for (let s of i.observers)
        s(this.view, t);
      for (let s of i.handlers) {
        if (t.defaultPrevented)
          break;
        if (s(this.view, t)) {
          t.preventDefault();
          break;
        }
      }
    }
  }
  ensureHandlers(e) {
    let t = ld(e), i = this.handlers, s = this.view.contentDOM;
    for (let r in t)
      if (r != "scroll") {
        let o = !t[r].handlers.length, l = i[r];
        l && o != !l.handlers.length && (s.removeEventListener(r, this.handleEvent), l = null), l || s.addEventListener(r, this.handleEvent, { passive: o });
      }
    for (let r in i)
      r != "scroll" && !t[r] && s.removeEventListener(r, this.handleEvent);
    this.handlers = t;
  }
  keydown(e) {
    if (this.lastKeyCode = e.keyCode, this.lastKeyTime = Date.now(), e.keyCode == 9 && this.tabFocusMode > -1 && (!this.tabFocusMode || Date.now() <= this.tabFocusMode))
      return !0;
    if (this.tabFocusMode > 0 && e.keyCode != 27 && Eh.indexOf(e.keyCode) < 0 && (this.tabFocusMode = -1), D.android && D.chrome && !e.synthetic && (e.keyCode == 13 || e.keyCode == 8))
      return this.view.observer.delayAndroidKey(e.key, e.keyCode), !0;
    if (D.ios && !e.synthetic && !e.altKey && !e.metaKey && (Lh.some((t) => t.keyCode == e.keyCode) && !e.ctrlKey || ad.indexOf(e.key) > -1 && e.ctrlKey)) {
      let t = { ctrlKey: e.ctrlKey, altKey: e.altKey, metaKey: e.metaKey, shiftKey: e.shiftKey };
      return t.shiftKey && D.ios && !/^(off|none)$/.test(this.view.contentDOM.autocapitalize) && od(this.view.win) && (t.shiftKey = !1), this.pendingIOSKey = { key: e.key, keyCode: e.keyCode, mods: t }, setTimeout(() => this.flushIOSKey(), 250), !0;
    }
    return e.keyCode != 229 && this.view.observer.forceFlush(), !1;
  }
  flushIOSKey(e) {
    let t = this.pendingIOSKey;
    return !t || t.key == "Enter" && e && e.from < e.to && /^\S+$/.test(e.insert.toString()) ? !1 : (this.pendingIOSKey = void 0, ni(this.view.contentDOM, t.key, t.keyCode, t.mods));
  }
  ignoreDuringComposition(e) {
    return !/^key/.test(e.type) || e.synthetic ? !1 : this.composing > 0 ? !0 : D.safari && !D.ios && this.compositionPendingKey && Date.now() - this.compositionEndedAt < 100 ? (this.compositionPendingKey = !1, !0) : !1;
  }
  startMouseSelection(e) {
    this.mouseSelection && this.mouseSelection.destroy(), this.mouseSelection = e;
  }
  update(e) {
    this.view.observer.update(e), this.mouseSelection && this.mouseSelection.update(e), this.draggedContent && e.docChanged && (this.draggedContent = this.draggedContent.map(e.changes)), e.transactions.length && (this.lastKeyCode = this.lastSelectionTime = 0);
  }
  destroy() {
    this.mouseSelection && this.mouseSelection.destroy();
  }
}
function od(n) {
  return n.visualViewport ? n.visualViewport.height * n.visualViewport.scale / n.document.documentElement.clientHeight < 0.85 : !1;
}
function hl(n, e) {
  return (t, i) => {
    try {
      return e.call(n, i, t);
    } catch (s) {
      Ae(t.state, s);
    }
  };
}
function ld(n) {
  let e = /* @__PURE__ */ Object.create(null);
  function t(i) {
    return e[i] || (e[i] = { observers: [], handlers: [] });
  }
  for (let i of n) {
    let s = i.spec, r = s && s.plugin.domEventHandlers, o = s && s.plugin.domEventObservers;
    if (r)
      for (let l in r) {
        let a = r[l];
        a && t(l).handlers.push(hl(i.value, a));
      }
    if (o)
      for (let l in o) {
        let a = o[l];
        a && t(l).observers.push(hl(i.value, a));
      }
  }
  for (let i in $e)
    t(i).handlers.push($e[i]);
  for (let i in De)
    t(i).observers.push(De[i]);
  return e;
}
const Lh = [
  { key: "Backspace", keyCode: 8, inputType: "deleteContentBackward" },
  { key: "Enter", keyCode: 13, inputType: "insertParagraph" },
  { key: "Enter", keyCode: 13, inputType: "insertLineBreak" },
  { key: "Delete", keyCode: 46, inputType: "deleteContentForward" }
], ad = "dthko", Eh = [16, 17, 18, 20, 91, 92, 224, 225], pn = 6;
function mn(n) {
  return Math.max(0, n) * 0.7 + 8;
}
function hd(n, e) {
  return Math.max(Math.abs(n.clientX - e.clientX), Math.abs(n.clientY - e.clientY));
}
class cd {
  constructor(e, t, i, s) {
    this.view = e, this.startEvent = t, this.style = i, this.mustSelect = s, this.scrollSpeed = { x: 0, y: 0 }, this.scrolling = -1, this.lastEvent = t, this.scrollParents = th(e.contentDOM), this.atoms = e.state.facet(en).map((o) => o(e));
    let r = e.contentDOM.ownerDocument;
    r.addEventListener("mousemove", this.move = this.move.bind(this)), r.addEventListener("mouseup", this.up = this.up.bind(this)), this.extend = t.shiftKey, this.multiple = e.state.facet(F.allowMultipleSelections) && fd(e, t), this.dragging = dd(e, t) && Ih(t) == 1 ? null : !1;
  }
  start(e) {
    this.dragging === !1 && this.select(e);
  }
  move(e) {
    if (e.buttons == 0)
      return this.destroy();
    if (this.dragging || this.dragging == null && hd(this.startEvent, e) < 10)
      return;
    this.select(this.lastEvent = e);
    let t = 0, i = 0, s = 0, r = 0, o = this.view.win.innerWidth, l = this.view.win.innerHeight;
    this.scrollParents.x && ({ left: s, right: o } = this.scrollParents.x.getBoundingClientRect()), this.scrollParents.y && ({ top: r, bottom: l } = this.scrollParents.y.getBoundingClientRect());
    let a = fo(this.view);
    e.clientX - a.left <= s + pn ? t = -mn(s - e.clientX) : e.clientX + a.right >= o - pn && (t = mn(e.clientX - o)), e.clientY - a.top <= r + pn ? i = -mn(r - e.clientY) : e.clientY + a.bottom >= l - pn && (i = mn(e.clientY - l)), this.setScrollSpeed(t, i);
  }
  up(e) {
    this.dragging == null && this.select(this.lastEvent), this.dragging || e.preventDefault(), this.destroy();
  }
  destroy() {
    this.setScrollSpeed(0, 0);
    let e = this.view.contentDOM.ownerDocument;
    e.removeEventListener("mousemove", this.move), e.removeEventListener("mouseup", this.up), this.view.inputState.mouseSelection = this.view.inputState.draggedContent = null;
  }
  setScrollSpeed(e, t) {
    this.scrollSpeed = { x: e, y: t }, e || t ? this.scrolling < 0 && (this.scrolling = setInterval(() => this.scroll(), 50)) : this.scrolling > -1 && (clearInterval(this.scrolling), this.scrolling = -1);
  }
  scroll() {
    let { x: e, y: t } = this.scrollSpeed;
    e && this.scrollParents.x && (this.scrollParents.x.scrollLeft += e, e = 0), t && this.scrollParents.y && (this.scrollParents.y.scrollTop += t, t = 0), (e || t) && this.view.win.scrollBy(e, t), this.dragging === !1 && this.select(this.lastEvent);
  }
  select(e) {
    let { view: t } = this, i = Th(this.atoms, this.style.get(e, this.extend, this.multiple));
    (this.mustSelect || !i.eq(t.state.selection, this.dragging === !1)) && this.view.dispatch({
      selection: i,
      userEvent: "select.pointer"
    }), this.mustSelect = !1;
  }
  update(e) {
    e.transactions.some((t) => t.isUserEvent("input.type")) ? this.destroy() : this.style.update(e) && setTimeout(() => this.select(this.lastEvent), 20);
  }
}
function fd(n, e) {
  let t = n.state.facet(fh);
  return t.length ? t[0](e) : D.mac ? e.metaKey : e.ctrlKey;
}
function ud(n, e) {
  let t = n.state.facet(uh);
  return t.length ? t[0](e) : D.mac ? !e.altKey : !e.ctrlKey;
}
function dd(n, e) {
  let { main: t } = n.state.selection;
  if (t.empty)
    return !1;
  let i = Fi(n.root);
  if (!i || i.rangeCount == 0)
    return !0;
  let s = i.getRangeAt(0).getClientRects();
  for (let r = 0; r < s.length; r++) {
    let o = s[r];
    if (o.left <= e.clientX && o.right >= e.clientX && o.top <= e.clientY && o.bottom >= e.clientY)
      return !0;
  }
  return !1;
}
function pd(n, e) {
  if (!e.bubbles)
    return !0;
  if (e.defaultPrevented)
    return !1;
  for (let t = e.target, i; t != n.contentDOM; t = t.parentNode)
    if (!t || t.nodeType == 11 || (i = X.get(t)) && i.isWidget() && !i.isHidden && i.widget.ignoreEvent(e))
      return !1;
  return !0;
}
const $e = /* @__PURE__ */ Object.create(null), De = /* @__PURE__ */ Object.create(null), Rh = D.ie && D.ie_version < 15 || D.ios && D.webkit_version < 604;
function md(n) {
  let e = n.dom.parentNode;
  if (!e)
    return;
  let t = e.appendChild(document.createElement("textarea"));
  t.style.cssText = "position: fixed; left: -10000px; top: 10px", t.focus(), setTimeout(() => {
    n.focus(), t.remove(), Ph(n, t.value);
  }, 50);
}
function gs(n, e, t) {
  for (let i of n.facet(e))
    t = i(t, n);
  return t;
}
function Ph(n, e) {
  e = gs(n.state, lo, e);
  let { state: t } = n, i, s = 1, r = t.toText(e), o = r.lines == t.selection.ranges.length;
  if (Er != null && t.selection.ranges.every((a) => a.empty) && Er == r.toString()) {
    let a = -1;
    i = t.changeByRange((h) => {
      let c = t.doc.lineAt(h.from);
      if (c.from == a)
        return { range: h };
      a = c.from;
      let f = t.toText((o ? r.line(s++).text : e) + t.lineBreak);
      return {
        changes: { from: c.from, insert: f },
        range: b.cursor(h.from + f.length)
      };
    });
  } else o ? i = t.changeByRange((a) => {
    let h = r.line(s++);
    return {
      changes: { from: a.from, to: a.to, insert: h.text },
      range: b.cursor(a.from + h.length)
    };
  }) : i = t.replaceSelection(r);
  n.dispatch(i, {
    userEvent: "input.paste",
    scrollIntoView: !0
  });
}
De.scroll = (n) => {
  n.inputState.lastScrollTop = n.scrollDOM.scrollTop, n.inputState.lastScrollLeft = n.scrollDOM.scrollLeft;
};
De.wheel = De.mousewheel = (n) => {
  n.inputState.lastWheelEvent = Date.now();
};
$e.keydown = (n, e) => (n.inputState.setSelectionOrigin("select"), e.keyCode == 27 && n.inputState.tabFocusMode != 0 && (n.inputState.tabFocusMode = Date.now() + 2e3), !1);
De.touchstart = (n, e) => {
  let t = n.inputState, i = e.targetTouches[0];
  t.lastTouchTime = Date.now(), i && (t.lastTouchX = i.clientX, t.lastTouchY = i.clientY), t.setSelectionOrigin("select.pointer");
};
De.touchmove = (n) => {
  n.inputState.setSelectionOrigin("select.pointer");
};
$e.mousedown = (n, e) => {
  if (n.observer.flush(), n.inputState.lastTouchTime > Date.now() - 2e3)
    return !1;
  let t = null;
  for (let i of n.state.facet(dh))
    if (t = i(n, e), t)
      break;
  if (!t && e.button == 0 && (t = yd(n, e)), t) {
    let i = !n.hasFocus;
    n.inputState.startMouseSelection(new cd(n, e, t, i)), i && n.observer.ignore(() => {
      ih(n.contentDOM);
      let r = n.root.activeElement;
      r && !r.contains(n.contentDOM) && r.blur();
    });
    let s = n.inputState.mouseSelection;
    if (s)
      return s.start(e), s.dragging === !1;
  } else
    n.inputState.setSelectionOrigin("select.pointer");
  return !1;
};
function cl(n, e, t, i) {
  if (i == 1)
    return b.cursor(e, t);
  if (i == 2)
    return Uu(n.state, e, t);
  {
    let s = n.docView.lineAt(e, t), r = n.state.doc.lineAt(s ? s.posAtEnd : e), o = s ? s.posAtStart : r.from, l = s ? s.posAtEnd : r.to;
    return l < n.state.doc.length && l == r.to && l++, b.undirectionalRange(o, l);
  }
}
const gd = D.ie && D.ie_version <= 11;
let fl = null, ul = 0, dl = 0;
function Ih(n) {
  if (!gd)
    return n.detail;
  let e = fl, t = dl;
  return fl = n, dl = Date.now(), ul = !e || t > Date.now() - 400 && Math.abs(e.clientX - n.clientX) < 2 && Math.abs(e.clientY - n.clientY) < 2 ? (ul + 1) % 3 : 1;
}
function yd(n, e) {
  let t = n.posAndSideAtCoords({ x: e.clientX, y: e.clientY }, !1), i = Ih(e), s = n.state.selection;
  return {
    update(r) {
      r.docChanged && (t.pos = r.changes.mapPos(t.pos), s = s.map(r.changes));
    },
    get(r, o, l) {
      let a = n.posAndSideAtCoords({ x: r.clientX, y: r.clientY }, !1), h, c = cl(n, a.pos, a.assoc, i);
      if (t.pos != a.pos && !o) {
        let f = cl(n, t.pos, t.assoc, i), u = Math.min(f.from, c.from), d = Math.max(f.to, c.to);
        c = u < c.from ? b.range(u, d, c.assoc) : b.range(d, u, c.assoc);
      }
      return o ? s.replaceRange(s.main.extend(c.from, c.to, c.assoc)) : l && i == 1 && s.ranges.length > 1 && (h = bd(s, a.pos)) ? h : l ? s.addRange(c) : b.create([c]);
    }
  };
}
function bd(n, e) {
  for (let t = 0; t < n.ranges.length; t++) {
    let { from: i, to: s } = n.ranges[t];
    if (i <= e && s >= e)
      return b.create(n.ranges.slice(0, t).concat(n.ranges.slice(t + 1)), n.mainIndex == t ? 0 : n.mainIndex - (n.mainIndex > t ? 1 : 0));
  }
  return null;
}
$e.dragstart = (n, e) => {
  let { selection: { main: t } } = n.state;
  if (e.target.draggable) {
    let s = n.docView.tile.nearest(e.target);
    if (s && s.isWidget()) {
      let r = s.posAtStart, o = r + s.length;
      (r >= t.to || o <= t.from) && (t = b.undirectionalRange(r, o));
    }
  }
  let { inputState: i } = n;
  return i.mouseSelection && (i.mouseSelection.dragging = !0), i.draggedContent = t, e.dataTransfer && (e.dataTransfer.setData("Text", gs(n.state, ao, n.state.sliceDoc(t.from, t.to))), e.dataTransfer.effectAllowed = "copyMove"), !1;
};
$e.dragend = (n) => (n.inputState.draggedContent = null, !1);
function pl(n, e, t, i) {
  if (t = gs(n.state, lo, t), !t)
    return;
  let s = n.posAtCoords({ x: e.clientX, y: e.clientY }, !1), { draggedContent: r } = n.inputState, o = i && r && ud(n, e) ? { from: r.from, to: r.to } : null, l = { from: s, insert: t }, a = n.state.changes(o ? [o, l] : l);
  n.focus(), n.dispatch({
    changes: a,
    selection: { anchor: a.mapPos(s, -1), head: a.mapPos(s, 1) },
    userEvent: o ? "move.drop" : "input.drop"
  }), n.inputState.draggedContent = null;
}
$e.drop = (n, e) => {
  if (!e.dataTransfer)
    return !1;
  if (n.state.readOnly)
    return !0;
  let t = e.dataTransfer.files;
  if (t && t.length) {
    let i = Array(t.length), s = 0, r = () => {
      ++s == t.length && pl(n, e, i.filter((o) => o != null).join(n.state.lineBreak), !1);
    };
    for (let o = 0; o < t.length; o++) {
      let l = new FileReader();
      l.onerror = r, l.onload = () => {
        /[\x00-\x08\x0e-\x1f]{2}/.test(l.result) || (i[o] = l.result), r();
      }, l.readAsText(t[o]);
    }
    return !0;
  } else {
    let i = e.dataTransfer.getData("Text");
    if (i)
      return pl(n, e, i, !0), !0;
  }
  return !1;
};
$e.paste = (n, e) => {
  if (n.state.readOnly)
    return !0;
  n.observer.flush();
  let t = Rh ? null : e.clipboardData;
  return t ? (Ph(n, t.getData("text/plain") || t.getData("text/uri-list")), !0) : (md(n), !1);
};
function xd(n, e) {
  let t = n.dom.parentNode;
  if (!t)
    return;
  let i = t.appendChild(document.createElement("textarea"));
  i.style.cssText = "position: fixed; left: -10000px; top: 10px", i.value = e, i.focus(), i.selectionEnd = e.length, i.selectionStart = 0, setTimeout(() => {
    i.remove(), n.focus();
  }, 50);
}
function wd(n) {
  let e = [], t = [], i = !1;
  for (let s of n.selection.ranges)
    s.empty || (e.push(n.sliceDoc(s.from, s.to)), t.push(s));
  if (!e.length) {
    let s = -1;
    for (let { from: r } of n.selection.ranges) {
      let o = n.doc.lineAt(r);
      o.number > s && (e.push(o.text), t.push({ from: o.from, to: Math.min(n.doc.length, o.to + 1) })), s = o.number;
    }
    i = !0;
  }
  return { text: gs(n, ao, e.join(n.lineBreak)), ranges: t, linewise: i };
}
let Er = null;
$e.copy = $e.cut = (n, e) => {
  if (!Oi(n.contentDOM, n.observer.selectionRange))
    return !1;
  let { text: t, ranges: i, linewise: s } = wd(n.state);
  if (!t && !s)
    return !1;
  Er = s ? t : null, e.type == "cut" && !n.state.readOnly && n.dispatch({
    changes: i,
    scrollIntoView: !0,
    userEvent: "delete.cut"
  });
  let r = Rh ? null : e.clipboardData;
  return r ? (r.clearData(), r.setData("text/plain", t), !0) : (xd(n, t), !1);
};
const Nh = /* @__PURE__ */ lt.define();
function Wh(n, e) {
  let t = [];
  for (let i of n.facet(gh)) {
    let s = i(n, e);
    s && t.push(s);
  }
  return t.length ? n.update({ effects: t, annotations: Nh.of(!0) }) : null;
}
function Hh(n) {
  setTimeout(() => {
    let e = n.hasFocus;
    if (e != n.inputState.notifiedFocused) {
      let t = Wh(n.state, e);
      t ? n.dispatch(t) : n.update([]);
    }
  }, 10);
}
De.focus = (n) => {
  n.inputState.lastFocusTime = Date.now(), !n.scrollDOM.scrollTop && (n.inputState.lastScrollTop || n.inputState.lastScrollLeft) && (n.scrollDOM.scrollTop = n.inputState.lastScrollTop, n.scrollDOM.scrollLeft = n.inputState.lastScrollLeft), Hh(n);
};
De.blur = (n) => {
  n.observer.clearSelectionRange(), Hh(n);
};
De.compositionstart = De.compositionupdate = (n) => {
  n.observer.editContext || (n.inputState.compositionFirstChange == null && (n.inputState.compositionFirstChange = !0), n.inputState.composing < 0 && (n.inputState.composing = 0));
};
De.compositionend = (n) => {
  n.observer.editContext || (n.inputState.composing = -1, n.inputState.compositionEndedAt = Date.now(), n.inputState.compositionPendingKey = !0, n.inputState.compositionPendingChange = n.observer.pendingRecords().length > 0, n.inputState.compositionFirstChange = null, D.chrome && D.android ? n.observer.flushSoon() : n.inputState.compositionPendingChange ? Promise.resolve().then(() => n.observer.flush()) : setTimeout(() => {
    n.inputState.composing < 0 && n.docView.hasComposition && n.update([]);
  }, 50));
};
De.contextmenu = (n) => {
  n.inputState.lastContextMenu = Date.now();
};
$e.beforeinput = (n, e) => {
  var t, i;
  if ((e.inputType == "insertText" || e.inputType == "insertCompositionText") && (n.inputState.insertingText = e.data, n.inputState.insertingTextAt = Date.now()), e.inputType == "insertReplacementText" && n.observer.editContext) {
    let r = (t = e.dataTransfer) === null || t === void 0 ? void 0 : t.getData("text/plain"), o = e.getTargetRanges();
    if (r && o.length) {
      let l = o[0], a = n.posAtDOM(l.startContainer, l.startOffset), h = n.posAtDOM(l.endContainer, l.endOffset);
      return uo(n, { from: a, to: h, insert: n.state.toText(r) }, null), !0;
    }
  }
  let s;
  if (D.chrome && D.android && (s = Lh.find((r) => r.inputType == e.inputType)) && (n.observer.delayAndroidKey(s.key, s.keyCode), s.key == "Backspace" || s.key == "Delete")) {
    let r = ((i = window.visualViewport) === null || i === void 0 ? void 0 : i.height) || 0;
    setTimeout(() => {
      var o;
      (((o = window.visualViewport) === null || o === void 0 ? void 0 : o.height) || 0) > r + 10 && n.hasFocus && (n.contentDOM.blur(), n.focus());
    }, 100);
  }
  return D.ios && e.inputType == "deleteContentForward" && n.observer.flushSoon(), D.safari && e.inputType == "insertText" && n.inputState.composing >= 0 && setTimeout(() => De.compositionend(n, e), 20), !1;
};
const ml = /* @__PURE__ */ new Set();
function vd(n) {
  ml.has(n) || (ml.add(n), n.addEventListener("copy", () => {
  }), n.addEventListener("cut", () => {
  }));
}
const gl = ["pre-wrap", "normal", "pre-line", "break-spaces"];
let ci = !1;
function yl() {
  ci = !1;
}
class kd {
  constructor(e) {
    this.lineWrapping = e, this.doc = z.empty, this.heightSamples = {}, this.lineHeight = 14, this.charWidth = 7, this.textHeight = 14, this.lineLength = 30;
  }
  heightForGap(e, t) {
    let i = this.doc.lineAt(t).number - this.doc.lineAt(e).number + 1;
    return this.lineWrapping && (i += Math.max(0, Math.ceil((t - e - i * this.lineLength * 0.5) / this.lineLength))), this.lineHeight * i;
  }
  heightForLine(e) {
    return this.lineWrapping ? (1 + Math.max(0, Math.ceil((e - this.lineLength) / Math.max(1, this.lineLength - 5)))) * this.lineHeight : this.lineHeight;
  }
  setDoc(e) {
    return this.doc = e, this;
  }
  mustRefreshForWrapping(e) {
    return gl.indexOf(e) > -1 != this.lineWrapping;
  }
  mustRefreshForHeights(e) {
    let t = !1;
    for (let i = 0; i < e.length; i++) {
      let s = e[i];
      s < 0 ? i++ : this.heightSamples[Math.floor(s * 10)] || (t = !0, this.heightSamples[Math.floor(s * 10)] = !0);
    }
    return t;
  }
  refresh(e, t, i, s, r, o) {
    let l = gl.indexOf(e) > -1, a = Math.abs(t - this.lineHeight) > 0.3 || this.lineWrapping != l;
    if (this.lineWrapping = l, this.lineHeight = t, this.charWidth = i, this.textHeight = s, this.lineLength = r, a) {
      this.heightSamples = {};
      for (let h = 0; h < o.length; h++) {
        let c = o[h];
        c < 0 ? h++ : this.heightSamples[Math.floor(c * 10)] = !0;
      }
    }
    return a;
  }
}
class Sd {
  constructor(e, t) {
    this.from = e, this.heights = t, this.index = 0;
  }
  get more() {
    return this.index < this.heights.length;
  }
}
class Ve {
  /**
  @internal
  */
  constructor(e, t, i, s, r) {
    this.from = e, this.length = t, this.top = i, this.height = s, this._content = r;
  }
  /**
  The type of element this is. When querying lines, this may be
  an array of all the blocks that make up the line.
  */
  get type() {
    return typeof this._content == "number" ? de.Text : Array.isArray(this._content) ? this._content : this._content.type;
  }
  /**
  The end of the element as a document position.
  */
  get to() {
    return this.from + this.length;
  }
  /**
  The bottom position of the element.
  */
  get bottom() {
    return this.top + this.height;
  }
  /**
  If this is a widget block, this will return the widget
  associated with it.
  */
  get widget() {
    return this._content instanceof zt ? this._content.widget : null;
  }
  /**
  If this is a textblock, this holds the number of line breaks
  that appear in widgets inside the block.
  */
  get widgetLineBreaks() {
    return typeof this._content == "number" ? this._content : 0;
  }
  /**
  @internal
  */
  join(e) {
    let t = (Array.isArray(this._content) ? this._content : [this]).concat(Array.isArray(e._content) ? e._content : [e]);
    return new Ve(this.from, this.length + e.length, this.top, this.height + e.height, t);
  }
}
var G = /* @__PURE__ */ (function(n) {
  return n[n.ByPos = 0] = "ByPos", n[n.ByHeight = 1] = "ByHeight", n[n.ByPosNoHeight = 2] = "ByPosNoHeight", n;
})(G || (G = {}));
const In = 1e-3;
class ve {
  constructor(e, t, i = 2) {
    this.length = e, this.height = t, this.flags = i;
  }
  get outdated() {
    return (this.flags & 2) > 0;
  }
  set outdated(e) {
    this.flags = (e ? 2 : 0) | this.flags & -3;
  }
  setHeight(e) {
    this.height != e && (Math.abs(this.height - e) > In && (ci = !0), this.height = e);
  }
  // Base case is to replace a leaf node, which simply builds a tree
  // from the new nodes and returns that (HeightMapBranch and
  // HeightMapGap override this to actually use from/to)
  replace(e, t, i) {
    return ve.of(i);
  }
  // Again, these are base cases, and are overridden for branch and gap nodes.
  decomposeLeft(e, t) {
    t.push(this);
  }
  decomposeRight(e, t) {
    t.push(this);
  }
  applyChanges(e, t, i, s) {
    let r = this, o = i.doc;
    for (let l = s.length - 1; l >= 0; l--) {
      let { fromA: a, toA: h, fromB: c, toB: f } = s[l], u = r.lineAt(a, G.ByPosNoHeight, i.setDoc(t), 0, 0), d = u.to >= h ? u : r.lineAt(h, G.ByPosNoHeight, i, 0, 0);
      for (f += d.to - h, h = d.to; l > 0 && u.from <= s[l - 1].toA; )
        a = s[l - 1].fromA, c = s[l - 1].fromB, l--, a < u.from && (u = r.lineAt(a, G.ByPosNoHeight, i, 0, 0));
      c += u.from - a, a = u.from;
      let p = po.build(i.setDoc(o), e, c, f);
      r = Yn(r, r.replace(a, h, p));
    }
    return r.updateHeight(i, 0);
  }
  static empty() {
    return new Le(0, 0, 0);
  }
  // nodes uses null values to indicate the position of line breaks.
  // There are never line breaks at the start or end of the array, or
  // two line breaks next to each other, and the array isn't allowed
  // to be empty (same restrictions as return value from the builder).
  static of(e) {
    if (e.length == 1)
      return e[0];
    let t = 0, i = e.length, s = 0, r = 0;
    for (; ; )
      if (t == i)
        if (s > r * 2) {
          let l = e[t - 1];
          l.break ? e.splice(--t, 1, l.left, null, l.right) : e.splice(--t, 1, l.left, l.right), i += 1 + l.break, s -= l.size;
        } else if (r > s * 2) {
          let l = e[i];
          l.break ? e.splice(i, 1, l.left, null, l.right) : e.splice(i, 1, l.left, l.right), i += 2 + l.break, r -= l.size;
        } else
          break;
      else if (s < r) {
        let l = e[t++];
        l && (s += l.size);
      } else {
        let l = e[--i];
        l && (r += l.size);
      }
    let o = 0;
    return e[t - 1] == null ? (o = 1, t--) : e[t] == null && (o = 1, i++), new Ad(ve.of(e.slice(0, t)), o, ve.of(e.slice(i)));
  }
}
function Yn(n, e) {
  return n == e ? n : (n.constructor != e.constructor && (ci = !0), e);
}
ve.prototype.size = 1;
const Cd = /* @__PURE__ */ B.replace({});
class Fh extends ve {
  constructor(e, t, i) {
    super(e, t), this.deco = i, this.spaceAbove = 0;
  }
  mainBlock(e, t) {
    return new Ve(t, this.length, e + this.spaceAbove, this.height - this.spaceAbove, this.deco || 0);
  }
  blockAt(e, t, i, s) {
    return this.spaceAbove && e < i + this.spaceAbove ? new Ve(s, 0, i, this.spaceAbove, Cd) : this.mainBlock(i, s);
  }
  lineAt(e, t, i, s, r) {
    let o = this.mainBlock(s, r);
    return this.spaceAbove ? this.blockAt(0, i, s, r).join(o) : o;
  }
  forEachLine(e, t, i, s, r, o) {
    e <= r + this.length && t >= r && o(this.lineAt(0, G.ByPos, i, s, r));
  }
  setMeasuredHeight(e) {
    let t = e.heights[e.index++];
    t < 0 ? (this.spaceAbove = -t, t = e.heights[e.index++]) : this.spaceAbove = 0, this.setHeight(t);
  }
  updateHeight(e, t = 0, i = !1, s) {
    return s && s.from <= t && s.more && this.setMeasuredHeight(s), this.outdated = !1, this;
  }
  toString() {
    return `block(${this.length})`;
  }
}
class Le extends Fh {
  constructor(e, t, i) {
    super(e, t, null), this.collapsed = 0, this.widgetHeight = 0, this.breaks = 0, this.spaceAbove = i;
  }
  mainBlock(e, t) {
    return new Ve(t, this.length, e + this.spaceAbove, this.height - this.spaceAbove, this.breaks);
  }
  replace(e, t, i) {
    let s = i[0];
    return i.length == 1 && (s instanceof Le || s instanceof he && s.flags & 4) && Math.abs(this.length - s.length) < 10 ? (s instanceof he ? s = new Le(s.length, this.height, this.spaceAbove) : s.height = this.height, this.outdated || (s.outdated = !1), s) : ve.of(i);
  }
  updateHeight(e, t = 0, i = !1, s) {
    return s && s.from <= t && s.more ? this.setMeasuredHeight(s) : (i || this.outdated) && (this.spaceAbove = 0, this.setHeight(Math.max(this.widgetHeight, e.heightForLine(this.length - this.collapsed)) + this.breaks * e.lineHeight)), this.outdated = !1, this;
  }
  toString() {
    return `line(${this.length}${this.collapsed ? -this.collapsed : ""}${this.widgetHeight ? ":" + this.widgetHeight : ""})`;
  }
}
class he extends ve {
  constructor(e) {
    super(e, 0);
  }
  heightMetrics(e, t) {
    let i = e.doc.lineAt(t).number, s = e.doc.lineAt(t + this.length).number, r = s - i + 1, o, l = 0;
    if (e.lineWrapping) {
      let a = Math.min(this.height, e.lineHeight * r);
      o = a / r, this.length > r + 1 && (l = (this.height - a) / (this.length - r - 1));
    } else
      o = this.height / r;
    return { firstLine: i, lastLine: s, perLine: o, perChar: l };
  }
  blockAt(e, t, i, s) {
    let { firstLine: r, lastLine: o, perLine: l, perChar: a } = this.heightMetrics(t, s);
    if (t.lineWrapping) {
      let h = s + (e < t.lineHeight ? 0 : Math.round(Math.max(0, Math.min(1, (e - i) / this.height)) * this.length)), c = t.doc.lineAt(h), f = l + c.length * a, u = Math.max(i, e - f / 2);
      return new Ve(c.from, c.length, u, f, 0);
    } else {
      let h = Math.max(0, Math.min(o - r, Math.floor((e - i) / l))), { from: c, length: f } = t.doc.line(r + h);
      return new Ve(c, f, i + l * h, l, 0);
    }
  }
  lineAt(e, t, i, s, r) {
    if (t == G.ByHeight)
      return this.blockAt(e, i, s, r);
    if (t == G.ByPosNoHeight) {
      let { from: d, to: p } = i.doc.lineAt(e);
      return new Ve(d, p - d, 0, 0, 0);
    }
    let { firstLine: o, perLine: l, perChar: a } = this.heightMetrics(i, r), h = i.doc.lineAt(e), c = l + h.length * a, f = h.number - o, u = s + l * f + a * (h.from - r - f);
    return new Ve(h.from, h.length, Math.max(s, Math.min(u, s + this.height - c)), c, 0);
  }
  forEachLine(e, t, i, s, r, o) {
    e = Math.max(e, r), t = Math.min(t, r + this.length);
    let { firstLine: l, perLine: a, perChar: h } = this.heightMetrics(i, r);
    for (let c = e, f = s; c <= t; ) {
      let u = i.doc.lineAt(c);
      if (c == e) {
        let p = u.number - l;
        f += a * p + h * (e - r - p);
      }
      let d = a + h * u.length;
      o(new Ve(u.from, u.length, f, d, 0)), f += d, c = u.to + 1;
    }
  }
  replace(e, t, i) {
    let s = this.length - t;
    if (s > 0) {
      let r = i[i.length - 1];
      r instanceof he ? i[i.length - 1] = new he(r.length + s) : i.push(null, new he(s - 1));
    }
    if (e > 0) {
      let r = i[0];
      r instanceof he ? i[0] = new he(e + r.length) : i.unshift(new he(e - 1), null);
    }
    return ve.of(i);
  }
  decomposeLeft(e, t) {
    t.push(new he(e - 1), null);
  }
  decomposeRight(e, t) {
    t.push(null, new he(this.length - e - 1));
  }
  updateHeight(e, t = 0, i = !1, s) {
    let r = t + this.length;
    if (s && s.from <= t + this.length && s.more) {
      let o = [], l = Math.max(t, s.from), a = -1;
      for (s.from > t && o.push(new he(s.from - t - 1).updateHeight(e, t)); l <= r && s.more; ) {
        let c = e.doc.lineAt(l).length;
        o.length && o.push(null);
        let f = s.heights[s.index++], u = 0;
        f < 0 && (u = -f, f = s.heights[s.index++]), a == -1 ? a = f : Math.abs(f - a) >= In && (a = -2);
        let d = new Le(c, f, u);
        d.outdated = !1, o.push(d), l += c + 1;
      }
      l <= r && o.push(null, new he(r - l).updateHeight(e, l));
      let h = ve.of(o);
      return (a < 0 || Math.abs(h.height - this.height) >= In || Math.abs(a - this.heightMetrics(e, t).perLine) >= In) && (ci = !0), Yn(this, h);
    } else (i || this.outdated) && (this.setHeight(e.heightForGap(t, t + this.length)), this.outdated = !1);
    return this;
  }
  toString() {
    return `gap(${this.length})`;
  }
}
class Ad extends ve {
  constructor(e, t, i) {
    super(e.length + t + i.length, e.height + i.height, t | (e.outdated || i.outdated ? 2 : 0)), this.left = e, this.right = i, this.size = e.size + i.size;
  }
  get break() {
    return this.flags & 1;
  }
  blockAt(e, t, i, s) {
    let r = i + this.left.height;
    return e < r ? this.left.blockAt(e, t, i, s) : this.right.blockAt(e, t, r, s + this.left.length + this.break);
  }
  lineAt(e, t, i, s, r) {
    let o = s + this.left.height, l = r + this.left.length + this.break, a = t == G.ByHeight ? e < o : e < l, h = a ? this.left.lineAt(e, t, i, s, r) : this.right.lineAt(e, t, i, o, l);
    if (this.break || (a ? h.to < l : h.from > l))
      return h;
    let c = t == G.ByPosNoHeight ? G.ByPosNoHeight : G.ByPos;
    return a ? h.join(this.right.lineAt(l, c, i, o, l)) : this.left.lineAt(l, c, i, s, r).join(h);
  }
  forEachLine(e, t, i, s, r, o) {
    let l = s + this.left.height, a = r + this.left.length + this.break;
    if (this.break)
      e < a && this.left.forEachLine(e, t, i, s, r, o), t >= a && this.right.forEachLine(e, t, i, l, a, o);
    else {
      let h = this.lineAt(a, G.ByPos, i, s, r);
      e < h.from && this.left.forEachLine(e, h.from - 1, i, s, r, o), h.to >= e && h.from <= t && o(h), t > h.to && this.right.forEachLine(h.to + 1, t, i, l, a, o);
    }
  }
  replace(e, t, i) {
    let s = this.left.length + this.break;
    if (t < s)
      return this.balanced(this.left.replace(e, t, i), this.right);
    if (e > this.left.length)
      return this.balanced(this.left, this.right.replace(e - s, t - s, i));
    let r = [];
    e > 0 && this.decomposeLeft(e, r);
    let o = r.length;
    for (let l of i)
      r.push(l);
    if (e > 0 && bl(r, o - 1), t < this.length) {
      let l = r.length;
      this.decomposeRight(t, r), bl(r, l);
    }
    return ve.of(r);
  }
  decomposeLeft(e, t) {
    let i = this.left.length;
    if (e <= i)
      return this.left.decomposeLeft(e, t);
    t.push(this.left), this.break && (i++, e >= i && t.push(null)), e > i && this.right.decomposeLeft(e - i, t);
  }
  decomposeRight(e, t) {
    let i = this.left.length, s = i + this.break;
    if (e >= s)
      return this.right.decomposeRight(e - s, t);
    e < i && this.left.decomposeRight(e, t), this.break && e < s && t.push(null), t.push(this.right);
  }
  balanced(e, t) {
    return e.size > 2 * t.size || t.size > 2 * e.size ? ve.of(this.break ? [e, null, t] : [e, t]) : (this.left = Yn(this.left, e), this.right = Yn(this.right, t), this.setHeight(e.height + t.height), this.outdated = e.outdated || t.outdated, this.size = e.size + t.size, this.length = e.length + this.break + t.length, this);
  }
  updateHeight(e, t = 0, i = !1, s) {
    let { left: r, right: o } = this, l = t + r.length + this.break, a = null;
    return s && s.from <= t + r.length && s.more ? a = r = r.updateHeight(e, t, i, s) : r.updateHeight(e, t, i), s && s.from <= l + o.length && s.more ? a = o = o.updateHeight(e, l, i, s) : o.updateHeight(e, l, i), a ? this.balanced(r, o) : (this.height = this.left.height + this.right.height, this.outdated = !1, this);
  }
  toString() {
    return this.left + (this.break ? " " : "-") + this.right;
  }
}
function bl(n, e) {
  let t, i;
  n[e] == null && (t = n[e - 1]) instanceof he && (i = n[e + 1]) instanceof he && n.splice(e - 1, 3, new he(t.length + 1 + i.length));
}
const Md = 5;
class po {
  constructor(e, t) {
    this.pos = e, this.oracle = t, this.nodes = [], this.lineStart = -1, this.lineEnd = -1, this.covering = null, this.writtenTo = e;
  }
  get isCovered() {
    return this.covering && this.nodes[this.nodes.length - 1] == this.covering;
  }
  span(e, t) {
    if (this.lineStart > -1) {
      let i = Math.min(t, this.lineEnd), s = this.nodes[this.nodes.length - 1];
      s instanceof Le ? s.length += i - this.pos : (i > this.pos || !this.isCovered) && this.nodes.push(new Le(i - this.pos, -1, 0)), this.writtenTo = i, t > i && (this.nodes.push(null), this.writtenTo++, this.lineStart = -1);
    }
    this.pos = t;
  }
  point(e, t, i) {
    if (e < t || i.heightRelevant) {
      let s = i.widget ? i.widget.estimatedHeight : 0, r = i.widget ? i.widget.lineBreaks : 0;
      s < 0 && (s = this.oracle.lineHeight);
      let o = t - e;
      i.block ? this.addBlock(new Fh(o, s, i)) : (o || r || s >= Md) && this.addLineDeco(s, r, o);
    } else t > e && this.span(e, t);
    this.lineEnd > -1 && this.lineEnd < this.pos && (this.lineEnd = this.oracle.doc.lineAt(this.pos).to);
  }
  enterLine() {
    if (this.lineStart > -1)
      return;
    let { from: e, to: t } = this.oracle.doc.lineAt(this.pos);
    this.lineStart = e, this.lineEnd = t, this.writtenTo < e && ((this.writtenTo < e - 1 || this.nodes[this.nodes.length - 1] == null) && this.nodes.push(this.blankContent(this.writtenTo, e - 1)), this.nodes.push(null)), this.pos > e && this.nodes.push(new Le(this.pos - e, -1, 0)), this.writtenTo = this.pos;
  }
  blankContent(e, t) {
    let i = new he(t - e);
    return this.oracle.doc.lineAt(e).to == t && (i.flags |= 4), i;
  }
  ensureLine() {
    this.enterLine();
    let e = this.nodes.length ? this.nodes[this.nodes.length - 1] : null;
    if (e instanceof Le)
      return e;
    let t = new Le(0, -1, 0);
    return this.nodes.push(t), t;
  }
  addBlock(e) {
    this.enterLine();
    let t = e.deco;
    t && t.startSide > 0 && !this.isCovered && this.ensureLine(), this.nodes.push(e), this.writtenTo = this.pos = this.pos + e.length, t && t.endSide > 0 && (this.covering = e);
  }
  addLineDeco(e, t, i) {
    let s = this.ensureLine();
    s.length += i, s.collapsed += i, s.widgetHeight = Math.max(s.widgetHeight, e), s.breaks += t, this.writtenTo = this.pos = this.pos + i;
  }
  finish(e) {
    let t = this.nodes.length == 0 ? null : this.nodes[this.nodes.length - 1];
    this.lineStart > -1 && !(t instanceof Le) && !this.isCovered ? this.nodes.push(new Le(0, -1, 0)) : (this.writtenTo < this.pos || t == null) && this.nodes.push(this.blankContent(this.writtenTo, this.pos));
    let i = e;
    for (let s of this.nodes)
      s instanceof Le && s.updateHeight(this.oracle, i), i += s ? s.length : 1;
    return this.nodes;
  }
  // Always called with a region that on both sides either stretches
  // to a line break or the end of the document.
  // The returned array uses null to indicate line breaks, but never
  // starts or ends in a line break, or has multiple line breaks next
  // to each other.
  static build(e, t, i, s) {
    let r = new po(i, e);
    return W.spans(t, i, s, r, 0), r.finish(i);
  }
}
function Td(n, e, t) {
  let i = new Dd();
  return W.compare(n, e, t, i, 0), i.changes;
}
class Dd {
  constructor() {
    this.changes = [];
  }
  compareRange() {
  }
  comparePoint(e, t, i, s) {
    (e < t || i && i.heightRelevant || s && s.heightRelevant) && ii(e, t, this.changes, 5);
  }
}
function Od(n, e) {
  let t = n.getBoundingClientRect(), i = n.ownerDocument, s = i.defaultView || window, r = Math.max(0, t.left), o = Math.min(s.innerWidth, t.right), l = Math.max(0, t.top), a = Math.min(s.innerHeight, t.bottom);
  for (let h = n.parentNode; h && h != i.body; )
    if (h.nodeType == 1) {
      let c = h, f = window.getComputedStyle(c);
      if ((c.scrollHeight > c.clientHeight || c.scrollWidth > c.clientWidth) && f.overflow != "visible") {
        let u = c.getBoundingClientRect();
        r = Math.max(r, u.left), o = Math.min(o, u.right), l = Math.max(l, u.top), a = Math.min(h == n.parentNode ? s.innerHeight : a, u.bottom);
      }
      h = f.position == "absolute" || f.position == "fixed" ? c.offsetParent : c.parentNode;
    } else if (h.nodeType == 11)
      h = h.host;
    else
      break;
  return {
    left: r - t.left,
    right: Math.max(r, o) - t.left,
    top: l - (t.top + e),
    bottom: Math.max(l, a) - (t.top + e)
  };
}
function Bd(n) {
  let e = n.getBoundingClientRect(), t = n.ownerDocument.defaultView || window;
  return e.left < t.innerWidth && e.right > 0 && e.top < t.innerHeight && e.bottom > 0;
}
function Ld(n, e) {
  let t = n.getBoundingClientRect();
  return {
    left: 0,
    right: t.right - t.left,
    top: e,
    bottom: t.bottom - (t.top + e)
  };
}
class Ns {
  constructor(e, t, i, s) {
    this.from = e, this.to = t, this.size = i, this.displaySize = s;
  }
  static same(e, t) {
    if (e.length != t.length)
      return !1;
    for (let i = 0; i < e.length; i++) {
      let s = e[i], r = t[i];
      if (s.from != r.from || s.to != r.to || s.size != r.size)
        return !1;
    }
    return !0;
  }
  draw(e, t) {
    return B.replace({
      widget: new Ed(this.displaySize * (t ? e.scaleY : e.scaleX), t)
    }).range(this.from, this.to);
  }
}
class Ed extends Ke {
  constructor(e, t) {
    super(), this.size = e, this.vertical = t;
  }
  eq(e) {
    return e.size == this.size && e.vertical == this.vertical;
  }
  toDOM() {
    let e = document.createElement("div");
    return this.vertical ? e.style.height = this.size + "px" : (e.style.width = this.size + "px", e.style.height = "2px", e.style.display = "inline-block"), e;
  }
  get estimatedHeight() {
    return this.vertical ? this.size : -1;
  }
}
class xl {
  constructor(e, t) {
    this.view = e, this.state = t, this.pixelViewport = { left: 0, right: window.innerWidth, top: 0, bottom: 0 }, this.inView = !0, this.paddingTop = 0, this.paddingBottom = 0, this.contentDOMWidth = 0, this.contentDOMHeight = 0, this.editorHeight = 0, this.editorWidth = 0, this.scaleX = 1, this.scaleY = 1, this.scrollOffset = 0, this.scrolledToBottom = !1, this.scrollAnchorPos = 0, this.scrollAnchorHeight = -1, this.scaler = wl, this.scrollTarget = null, this.printing = !1, this.mustMeasureContent = !0, this.defaultTextDirection = _.LTR, this.visibleRanges = [], this.mustEnforceCursorAssoc = !1;
    let i = t.facet(ho).some((s) => typeof s != "function" && s.class == "cm-lineWrapping");
    this.heightOracle = new kd(i), this.stateDeco = vl(t), this.heightMap = ve.empty().applyChanges(this.stateDeco, z.empty, this.heightOracle.setDoc(t.doc), [new Ie(0, 0, 0, t.doc.length)]);
    for (let s = 0; s < 2 && (this.viewport = this.getViewport(0, null), !!this.updateForViewport()); s++)
      ;
    this.updateViewportLines(), this.lineGaps = this.ensureLineGaps([]), this.lineGapDeco = B.set(this.lineGaps.map((s) => s.draw(this, !1))), this.scrollParent = e.scrollDOM, this.computeVisibleRanges();
  }
  updateForViewport() {
    let e = [this.viewport], { main: t } = this.state.selection;
    for (let i = 0; i <= 1; i++) {
      let s = i ? t.head : t.anchor;
      if (!e.some(({ from: r, to: o }) => s >= r && s <= o)) {
        let { from: r, to: o } = this.lineBlockAt(s);
        e.push(new gn(r, o));
      }
    }
    return this.viewports = e.sort((i, s) => i.from - s.from), this.updateScaler();
  }
  updateScaler() {
    let e = this.scaler;
    return this.scaler = this.heightMap.height <= 7e6 ? wl : new mo(this.heightOracle, this.heightMap, this.viewports), e.eq(this.scaler) ? 0 : 2;
  }
  updateViewportLines() {
    this.viewportLines = [], this.heightMap.forEachLine(this.viewport.from, this.viewport.to, this.heightOracle.setDoc(this.state.doc), 0, 0, (e) => {
      this.viewportLines.push(Ai(e, this.scaler));
    });
  }
  update(e, t = null) {
    this.state = e.state;
    let i = this.stateDeco;
    this.stateDeco = vl(this.state);
    let s = e.changedRanges, r = Ie.extendWithRanges(s, Td(i, this.stateDeco, e ? e.changes : ne.empty(this.state.doc.length))), o = this.heightMap.height, l = this.scrolledToBottom ? null : this.scrollAnchorAt(this.scrollOffset);
    yl(), this.heightMap = this.heightMap.applyChanges(this.stateDeco, e.startState.doc, this.heightOracle.setDoc(this.state.doc), r), (this.heightMap.height != o || ci) && (e.flags |= 2), l ? (this.scrollAnchorPos = e.changes.mapPos(l.from, -1), this.scrollAnchorHeight = l.top) : (this.scrollAnchorPos = -1, this.scrollAnchorHeight = o);
    let a = r.length ? this.mapViewport(this.viewport, e.changes) : this.viewport;
    (t && (t.range.head < a.from || t.range.head > a.to) || !this.viewportIsAppropriate(a)) && (a = this.getViewport(0, t));
    let h = a.from != this.viewport.from || a.to != this.viewport.to;
    this.viewport = a, e.flags |= this.updateForViewport(), (h || !e.changes.empty || e.flags & 2) && this.updateViewportLines(), (this.lineGaps.length || this.viewport.to - this.viewport.from > 4e3) && this.updateLineGaps(this.ensureLineGaps(this.mapLineGaps(this.lineGaps, e.changes))), e.flags |= this.computeVisibleRanges(e.changes), t && (this.scrollTarget = t), !this.mustEnforceCursorAssoc && (e.selectionSet || e.focusChanged) && e.view.lineWrapping && e.state.selection.main.empty && e.state.selection.main.assoc && !e.state.facet(bh) && (this.mustEnforceCursorAssoc = !0);
  }
  measure() {
    let { view: e } = this, t = e.contentDOM, i = window.getComputedStyle(t), s = this.heightOracle, r = i.whiteSpace;
    this.defaultTextDirection = i.direction == "rtl" ? _.RTL : _.LTR;
    let o = this.heightOracle.mustRefreshForWrapping(r) || this.mustMeasureContent === "refresh", l = t.getBoundingClientRect(), a = o || this.mustMeasureContent || this.contentDOMHeight != l.height;
    this.contentDOMHeight = l.height, this.mustMeasureContent = !1;
    let h = 0, c = 0;
    if (l.width && l.height) {
      let { scaleX: k, scaleY: S } = eh(t, l);
      (k > 5e-3 && Math.abs(this.scaleX - k) > 5e-3 || S > 5e-3 && Math.abs(this.scaleY - S) > 5e-3) && (this.scaleX = k, this.scaleY = S, h |= 16, o = a = !0);
    }
    let f = (parseInt(i.paddingTop) || 0) * this.scaleY, u = (parseInt(i.paddingBottom) || 0) * this.scaleY;
    (this.paddingTop != f || this.paddingBottom != u) && (this.paddingTop = f, this.paddingBottom = u, h |= 18), this.editorWidth != e.scrollDOM.clientWidth && (s.lineWrapping && (a = !0), this.editorWidth = e.scrollDOM.clientWidth, h |= 16);
    let d = th(this.view.contentDOM, !1).y;
    d != this.scrollParent && (this.scrollParent = d, this.scrollAnchorHeight = -1, this.scrollOffset = 0);
    let p = this.getScrollOffset();
    this.scrollOffset != p && (this.scrollAnchorHeight = -1, this.scrollOffset = p), this.scrolledToBottom = nh(this.scrollParent || e.win);
    let m = (this.printing ? Ld : Od)(t, this.paddingTop), g = m.top - this.pixelViewport.top, y = m.bottom - this.pixelViewport.bottom;
    this.pixelViewport = m;
    let w = this.pixelViewport.bottom > this.pixelViewport.top && this.pixelViewport.right > this.pixelViewport.left;
    if (w != this.inView && (this.inView = w, w && (a = !0)), !this.inView && !this.scrollTarget && !Bd(e.dom))
      return 0;
    let v = l.width;
    if ((this.contentDOMWidth != v || this.editorHeight != e.scrollDOM.clientHeight) && (this.contentDOMWidth = l.width, this.editorHeight = e.scrollDOM.clientHeight, h |= 16), a) {
      let k = e.docView.measureVisibleLineHeights(this.viewport);
      if (s.mustRefreshForHeights(k) && (o = !0), o || s.lineWrapping && Math.abs(v - this.contentDOMWidth) > s.charWidth) {
        let { lineHeight: S, charWidth: C, textHeight: E } = e.docView.measureTextSize();
        o = S > 0 && s.refresh(r, S, C, E, Math.max(5, v / C), k), o && (e.docView.minWidth = 0, h |= 16);
      }
      g > 0 && y > 0 ? c = Math.max(g, y) : g < 0 && y < 0 && (c = Math.min(g, y)), yl();
      for (let S of this.viewports) {
        let C = S.from == this.viewport.from ? k : e.docView.measureVisibleLineHeights(S);
        this.heightMap = (o ? ve.empty().applyChanges(this.stateDeco, z.empty, this.heightOracle, [new Ie(0, 0, 0, e.state.doc.length)]) : this.heightMap).updateHeight(s, 0, o, new Sd(S.from, C));
      }
      ci && (h |= 2);
    }
    let O = !this.viewportIsAppropriate(this.viewport, c) || this.scrollTarget && (this.scrollTarget.range.head < this.viewport.from || this.scrollTarget.range.head > this.viewport.to);
    return O && (h & 2 && (h |= this.updateScaler()), this.viewport = this.getViewport(c, this.scrollTarget), h |= this.updateForViewport()), (h & 2 || O) && this.updateViewportLines(), (this.lineGaps.length || this.viewport.to - this.viewport.from > 4e3) && this.updateLineGaps(this.ensureLineGaps(o ? [] : this.lineGaps, e)), h |= this.computeVisibleRanges(), this.mustEnforceCursorAssoc && (this.mustEnforceCursorAssoc = !1, e.docView.enforceCursorAssoc()), h;
  }
  get visibleTop() {
    return this.scaler.fromDOM(this.pixelViewport.top);
  }
  get visibleBottom() {
    return this.scaler.fromDOM(this.pixelViewport.bottom);
  }
  getViewport(e, t) {
    let i = 0.5 - Math.max(-0.5, Math.min(0.5, e / 1e3 / 2)), s = this.heightMap, r = this.heightOracle, { visibleTop: o, visibleBottom: l } = this, a = new gn(s.lineAt(o - i * 1e3, G.ByHeight, r, 0, 0).from, s.lineAt(l + (1 - i) * 1e3, G.ByHeight, r, 0, 0).to);
    if (t) {
      let { head: h } = t.range;
      if (h < a.from || h > a.to) {
        let c = Math.min(this.editorHeight, this.pixelViewport.bottom - this.pixelViewport.top), f = s.lineAt(h, G.ByPos, r, 0, 0), u;
        t.y == "center" ? u = (f.top + f.bottom) / 2 - c / 2 : t.y == "start" || t.y == "nearest" && h < a.from ? u = f.top : u = f.bottom - c, a = new gn(s.lineAt(u - 1e3 / 2, G.ByHeight, r, 0, 0).from, s.lineAt(u + c + 1e3 / 2, G.ByHeight, r, 0, 0).to);
      }
    }
    return a;
  }
  mapViewport(e, t) {
    let i = t.mapPos(e.from, -1), s = t.mapPos(e.to, 1);
    return new gn(this.heightMap.lineAt(i, G.ByPos, this.heightOracle, 0, 0).from, this.heightMap.lineAt(s, G.ByPos, this.heightOracle, 0, 0).to);
  }
  // Checks if a given viewport covers the visible part of the
  // document and not too much beyond that.
  viewportIsAppropriate({ from: e, to: t }, i = 0) {
    if (!this.inView)
      return !0;
    let { top: s } = this.heightMap.lineAt(e, G.ByPos, this.heightOracle, 0, 0), { bottom: r } = this.heightMap.lineAt(t, G.ByPos, this.heightOracle, 0, 0), { visibleTop: o, visibleBottom: l } = this;
    return (e == 0 || s <= o - Math.max(10, Math.min(
      -i,
      250
      /* VP.MaxCoverMargin */
    ))) && (t == this.state.doc.length || r >= l + Math.max(10, Math.min(
      i,
      250
      /* VP.MaxCoverMargin */
    ))) && s > o - 2 * 1e3 && r < l + 2 * 1e3;
  }
  mapLineGaps(e, t) {
    if (!e.length || t.empty)
      return e;
    let i = [];
    for (let s of e)
      t.touchesRange(s.from, s.to) || i.push(new Ns(t.mapPos(s.from), t.mapPos(s.to), s.size, s.displaySize));
    return i;
  }
  // Computes positions in the viewport where the start or end of a
  // line should be hidden, trying to reuse existing line gaps when
  // appropriate to avoid unneccesary redraws.
  // Uses crude character-counting for the positioning and sizing,
  // since actual DOM coordinates aren't always available and
  // predictable. Relies on generous margins (see LG.Margin) to hide
  // the artifacts this might produce from the user.
  ensureLineGaps(e, t) {
    let i = this.heightOracle.lineWrapping, s = i ? 1e4 : 2e3, r = s >> 1, o = s << 1;
    if (this.defaultTextDirection != _.LTR && !i)
      return [];
    let l = [], a = (c, f, u, d) => {
      if (f - c < r)
        return;
      let p = this.state.selection.main, m = [p.from];
      p.empty || m.push(p.to);
      for (let y of m)
        if (y > c && y < f) {
          a(c, y - 10, u, d), a(y + 10, f, u, d);
          return;
        }
      let g = Pd(e, (y) => y.from >= u.from && y.to <= u.to && Math.abs(y.from - c) < r && Math.abs(y.to - f) < r && !m.some((w) => y.from < w && y.to > w));
      if (!g) {
        if (f < u.to && t && i && t.visibleRanges.some((v) => v.from <= f && v.to >= f)) {
          let v = t.moveToLineBoundary(b.cursor(f), !1, !0).head;
          v > c && (f = v);
        }
        let y = this.gapSize(u, c, f, d), w = i || y < 2e6 ? y : 2e6;
        g = new Ns(c, f, y, w);
      }
      l.push(g);
    }, h = (c) => {
      if (c.length < o || c.type != de.Text)
        return;
      let f = Rd(c.from, c.to, this.stateDeco);
      if (f.total < o)
        return;
      let u = this.scrollTarget ? this.scrollTarget.range.head : null, d, p;
      if (i) {
        let m = s / this.heightOracle.lineLength * this.heightOracle.lineHeight, g, y;
        if (u != null) {
          let w = bn(f, u), v = ((this.visibleBottom - this.visibleTop) / 2 + m) / c.height;
          g = w - v, y = w + v;
        } else
          g = (this.visibleTop - c.top - m) / c.height, y = (this.visibleBottom - c.top + m) / c.height;
        d = yn(f, g), p = yn(f, y);
      } else {
        let m = f.total * this.heightOracle.charWidth, g = s * this.heightOracle.charWidth, y = 0;
        if (m > 2e6)
          for (let S of e)
            S.from >= c.from && S.from < c.to && S.size != S.displaySize && S.from * this.heightOracle.charWidth + y < this.pixelViewport.left && (y = S.size - S.displaySize);
        let w = this.pixelViewport.left + y, v = this.pixelViewport.right + y, O, k;
        if (u != null) {
          let S = bn(f, u), C = ((v - w) / 2 + g) / m;
          O = S - C, k = S + C;
        } else
          O = (w - g) / m, k = (v + g) / m;
        d = yn(f, O), p = yn(f, k);
      }
      d > c.from && a(c.from, d, c, f), p < c.to && a(p, c.to, c, f);
    };
    for (let c of this.viewportLines)
      Array.isArray(c.type) ? c.type.forEach(h) : h(c);
    return l;
  }
  gapSize(e, t, i, s) {
    let r = bn(s, i) - bn(s, t);
    return this.heightOracle.lineWrapping ? e.height * r : s.total * this.heightOracle.charWidth * r;
  }
  updateLineGaps(e) {
    Ns.same(e, this.lineGaps) || (this.lineGaps = e, this.lineGapDeco = B.set(e.map((t) => t.draw(this, this.heightOracle.lineWrapping))));
  }
  computeVisibleRanges(e) {
    let t = this.stateDeco;
    this.lineGaps.length && (t = t.concat(this.lineGapDeco));
    let i = [];
    W.spans(t, this.viewport.from, this.viewport.to, {
      span(r, o) {
        i.push({ from: r, to: o });
      },
      point() {
      }
    }, 20);
    let s = 0;
    if (i.length != this.visibleRanges.length)
      s = 12;
    else
      for (let r = 0; r < i.length && !(s & 8); r++) {
        let o = this.visibleRanges[r], l = i[r];
        (o.from != l.from || o.to != l.to) && (s |= 4, e && e.mapPos(o.from, -1) == l.from && e.mapPos(o.to, 1) == l.to || (s |= 8));
      }
    return this.visibleRanges = i, s;
  }
  lineBlockAt(e) {
    return e >= this.viewport.from && e <= this.viewport.to && this.viewportLines.find((t) => t.from <= e && t.to >= e) || Ai(this.heightMap.lineAt(e, G.ByPos, this.heightOracle, 0, 0), this.scaler);
  }
  lineBlockAtHeight(e) {
    return e >= this.viewportLines[0].top && e <= this.viewportLines[this.viewportLines.length - 1].bottom && this.viewportLines.find((t) => t.top <= e && t.bottom >= e) || Ai(this.heightMap.lineAt(this.scaler.fromDOM(e), G.ByHeight, this.heightOracle, 0, 0), this.scaler);
  }
  getScrollOffset() {
    return (this.scrollParent == this.view.scrollDOM ? this.scrollParent.scrollTop : (this.scrollParent ? this.scrollParent.getBoundingClientRect().top : 0) - this.view.contentDOM.getBoundingClientRect().top) * this.scaleY;
  }
  scrollAnchorAt(e) {
    let t = this.lineBlockAtHeight(e + 8);
    return t.from >= this.viewport.from || this.viewportLines[0].top - e > 200 ? t : this.viewportLines[0];
  }
  elementAtHeight(e) {
    return Ai(this.heightMap.blockAt(this.scaler.fromDOM(e), this.heightOracle, 0, 0), this.scaler);
  }
  get docHeight() {
    return this.scaler.toDOM(this.heightMap.height);
  }
  get contentHeight() {
    return this.docHeight + this.paddingTop + this.paddingBottom;
  }
}
class gn {
  constructor(e, t) {
    this.from = e, this.to = t;
  }
}
function Rd(n, e, t) {
  let i = [], s = n, r = 0;
  return W.spans(t, n, e, {
    span() {
    },
    point(o, l) {
      o > s && (i.push({ from: s, to: o }), r += o - s), s = l;
    }
  }, 20), s < e && (i.push({ from: s, to: e }), r += e - s), { total: r, ranges: i };
}
function yn({ total: n, ranges: e }, t) {
  if (t <= 0)
    return e[0].from;
  if (t >= 1)
    return e[e.length - 1].to;
  let i = Math.floor(n * t);
  for (let s = 0; ; s++) {
    let { from: r, to: o } = e[s], l = o - r;
    if (i <= l)
      return r + i;
    i -= l;
  }
}
function bn(n, e) {
  let t = 0;
  for (let { from: i, to: s } of n.ranges) {
    if (e <= s) {
      t += e - i;
      break;
    }
    t += s - i;
  }
  return t / n.total;
}
function Pd(n, e) {
  for (let t of n)
    if (e(t))
      return t;
}
const wl = {
  toDOM(n) {
    return n;
  },
  fromDOM(n) {
    return n;
  },
  scale: 1,
  eq(n) {
    return n == this;
  }
};
function vl(n) {
  let e = n.facet(ds).filter((i) => typeof i != "function"), t = n.facet(co).filter((i) => typeof i != "function");
  return t.length && e.push(W.join(t)), e;
}
class mo {
  constructor(e, t, i) {
    let s = 0, r = 0, o = 0;
    this.viewports = i.map(({ from: l, to: a }) => {
      let h = t.lineAt(l, G.ByPos, e, 0, 0).top, c = t.lineAt(a, G.ByPos, e, 0, 0).bottom;
      return s += c - h, { from: l, to: a, top: h, bottom: c, domTop: 0, domBottom: 0 };
    }), this.scale = (7e6 - s) / (t.height - s);
    for (let l of this.viewports)
      l.domTop = o + (l.top - r) * this.scale, o = l.domBottom = l.domTop + (l.bottom - l.top), r = l.bottom;
  }
  toDOM(e) {
    for (let t = 0, i = 0, s = 0; ; t++) {
      let r = t < this.viewports.length ? this.viewports[t] : null;
      if (!r || e < r.top)
        return s + (e - i) * this.scale;
      if (e <= r.bottom)
        return r.domTop + (e - r.top);
      i = r.bottom, s = r.domBottom;
    }
  }
  fromDOM(e) {
    for (let t = 0, i = 0, s = 0; ; t++) {
      let r = t < this.viewports.length ? this.viewports[t] : null;
      if (!r || e < r.domTop)
        return i + (e - s) / this.scale;
      if (e <= r.domBottom)
        return r.top + (e - r.domTop);
      i = r.bottom, s = r.domBottom;
    }
  }
  eq(e) {
    return e instanceof mo ? this.scale == e.scale && this.viewports.length == e.viewports.length && this.viewports.every((t, i) => t.from == e.viewports[i].from && t.to == e.viewports[i].to) : !1;
  }
}
function Ai(n, e) {
  if (e.scale == 1)
    return n;
  let t = e.toDOM(n.top), i = e.toDOM(n.bottom);
  return new Ve(n.from, n.length, t, i - t, Array.isArray(n._content) ? n._content.map((s) => Ai(s, e)) : n._content);
}
const xn = /* @__PURE__ */ T.define({ combine: (n) => n.join(" ") }), Rr = /* @__PURE__ */ T.define({ combine: (n) => n.indexOf(!0) > -1 }), Pr = /* @__PURE__ */ vt.newName(), Vh = /* @__PURE__ */ vt.newName(), zh = /* @__PURE__ */ vt.newName(), qh = { "&light": "." + Vh, "&dark": "." + zh };
function Ir(n, e, t) {
  return new vt(e, {
    finish(i) {
      return /&/.test(i) ? i.replace(/&\w*/, (s) => {
        if (s == "&")
          return n;
        if (!t || !t[s])
          throw new RangeError(`Unsupported selector: ${s}`);
        return t[s];
      }) : n + " " + i;
    }
  });
}
const Id = /* @__PURE__ */ Ir("." + Pr, {
  "&": {
    position: "relative !important",
    boxSizing: "border-box",
    "&.cm-focused": {
      // Provide a simple default outline to make sure a focused
      // editor is visually distinct. Can't leave the default behavior
      // because that will apply to the content element, which is
      // inside the scrollable container and doesn't include the
      // gutters. We also can't use an 'auto' outline, since those
      // are, for some reason, drawn behind the element content, which
      // will cause things like the active line background to cover
      // the outline (#297).
      outline: "1px dotted #212121"
    },
    display: "flex !important",
    flexDirection: "column"
  },
  ".cm-scroller": {
    display: "flex !important",
    alignItems: "flex-start !important",
    fontFamily: "monospace",
    lineHeight: 1.4,
    height: "100%",
    overflowX: "auto",
    position: "relative",
    zIndex: 0,
    overflowAnchor: "none"
  },
  ".cm-content": {
    margin: 0,
    flexGrow: 2,
    flexShrink: 0,
    display: "block",
    whiteSpace: "pre",
    wordWrap: "normal",
    // Issue #456
    boxSizing: "border-box",
    minHeight: "100%",
    padding: "4px 0",
    outline: "none",
    "&[contenteditable=true]": {
      WebkitUserModify: "read-write-plaintext-only"
    }
  },
  ".cm-lineWrapping": {
    whiteSpace_fallback: "pre-wrap",
    // For IE
    whiteSpace: "break-spaces",
    wordBreak: "break-word",
    // For Safari, which doesn't support overflow-wrap: anywhere
    overflowWrap: "anywhere",
    flexShrink: 1
  },
  "&light .cm-content": { caretColor: "black" },
  "&dark .cm-content": { caretColor: "white" },
  ".cm-line": {
    display: "block",
    padding: "0 2px 0 6px"
  },
  ".cm-layer": {
    userSelect: "none",
    // #1708
    position: "absolute",
    left: 0,
    top: 0,
    contain: "size style",
    "& > *": {
      position: "absolute"
    }
  },
  "&light .cm-selectionBackground": {
    background: "#d9d9d9"
  },
  "&dark .cm-selectionBackground": {
    background: "#222"
  },
  "&light.cm-focused > .cm-scroller > .cm-selectionLayer .cm-selectionBackground": {
    background: "#d7d4f0"
  },
  "&dark.cm-focused > .cm-scroller > .cm-selectionLayer .cm-selectionBackground": {
    background: "#233"
  },
  ".cm-cursorLayer": {
    pointerEvents: "none"
  },
  "&.cm-focused > .cm-scroller > .cm-cursorLayer": {
    animation: "steps(1) cm-blink 1.2s infinite"
  },
  // Two animations defined so that we can switch between them to
  // restart the animation without forcing another style
  // recomputation.
  "@keyframes cm-blink": { "0%": {}, "50%": { opacity: 0 }, "100%": {} },
  "@keyframes cm-blink2": { "0%": {}, "50%": { opacity: 0 }, "100%": {} },
  ".cm-cursor, .cm-dropCursor": {
    borderLeft: "1.2px solid black",
    marginLeft: "-0.6px",
    pointerEvents: "none"
  },
  ".cm-cursor": {
    display: "none"
  },
  "&dark .cm-cursor": {
    borderLeftColor: "#ddd"
  },
  ".cm-selectionHandle": {
    backgroundColor: "currentColor",
    width: "1.5px"
  },
  ".cm-selectionHandle-start::before, .cm-selectionHandle-end::before": {
    content: '""',
    backgroundColor: "inherit",
    borderRadius: "50%",
    width: "8px",
    height: "8px",
    position: "absolute",
    left: "-3.25px"
  },
  ".cm-selectionHandle-start::before": { top: "-8px" },
  ".cm-selectionHandle-end::before": { bottom: "-8px" },
  ".cm-dropCursor": {
    position: "absolute"
  },
  "&.cm-focused > .cm-scroller > .cm-cursorLayer .cm-cursor": {
    display: "block"
  },
  ".cm-iso": {
    unicodeBidi: "isolate"
  },
  ".cm-announced": {
    position: "fixed",
    top: "-10000px"
  },
  "@media print": {
    ".cm-announced": { display: "none" }
  },
  "&light .cm-activeLine": { backgroundColor: "#cceeff44" },
  "&dark .cm-activeLine": { backgroundColor: "#99eeff33" },
  "&light .cm-specialChar": { color: "red" },
  "&dark .cm-specialChar": { color: "#f78" },
  ".cm-gutters": {
    flexShrink: 0,
    display: "flex",
    height: "100%",
    boxSizing: "border-box",
    zIndex: 200
  },
  ".cm-gutters-before": { insetInlineStart: 0 },
  ".cm-gutters-after": { insetInlineEnd: 0 },
  "&light .cm-gutters": {
    backgroundColor: "#f5f5f5",
    color: "#6c6c6c",
    border: "0px solid #ddd",
    "&.cm-gutters-before": { borderRightWidth: "1px" },
    "&.cm-gutters-after": { borderLeftWidth: "1px" }
  },
  "&dark .cm-gutters": {
    backgroundColor: "#333338",
    color: "#ccc"
  },
  ".cm-gutter": {
    display: "flex !important",
    // Necessary -- prevents margin collapsing
    flexDirection: "column",
    flexShrink: 0,
    boxSizing: "border-box",
    minHeight: "100%",
    overflow: "hidden"
  },
  ".cm-gutterElement": {
    boxSizing: "border-box"
  },
  ".cm-lineNumbers .cm-gutterElement": {
    padding: "0 3px 0 5px",
    minWidth: "20px",
    textAlign: "right",
    whiteSpace: "nowrap"
  },
  "&light .cm-activeLineGutter": {
    backgroundColor: "#e2f2ff"
  },
  "&dark .cm-activeLineGutter": {
    backgroundColor: "#222227"
  },
  ".cm-panels": {
    boxSizing: "border-box",
    position: "sticky",
    left: 0,
    right: 0,
    zIndex: 300
  },
  "&light .cm-panels": {
    backgroundColor: "#f5f5f5",
    color: "black"
  },
  "&light .cm-panels-top": {
    borderBottom: "1px solid #ddd"
  },
  "&light .cm-panels-bottom": {
    borderTop: "1px solid #ddd"
  },
  "&dark .cm-panels": {
    backgroundColor: "#333338",
    color: "white"
  },
  ".cm-dialog": {
    padding: "2px 19px 4px 6px",
    position: "relative",
    "& label": { fontSize: "80%" }
  },
  ".cm-dialog-close": {
    position: "absolute",
    top: "3px",
    right: "4px",
    backgroundColor: "inherit",
    border: "none",
    font: "inherit",
    fontSize: "14px",
    padding: "0"
  },
  ".cm-tab": {
    display: "inline-block",
    overflow: "hidden",
    verticalAlign: "bottom"
  },
  ".cm-widgetBuffer": {
    verticalAlign: "text-top",
    height: "1em",
    width: 0,
    display: "inline"
  },
  ".cm-placeholder": {
    color: "#888",
    display: "inline-block",
    verticalAlign: "top",
    userSelect: "none"
  },
  ".cm-highlightSpace": {
    backgroundImage: "radial-gradient(circle at 50% 55%, #aaa 20%, transparent 5%)",
    backgroundPosition: "center"
  },
  ".cm-highlightTab": {
    backgroundImage: `url('data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" width="200" height="20"><path stroke="%23888" stroke-width="1" fill="none" d="M1 10H196L190 5M190 15L196 10M197 4L197 16"/></svg>')`,
    backgroundSize: "auto 100%",
    backgroundPosition: "right 90%",
    backgroundRepeat: "no-repeat"
  },
  ".cm-trailingSpace": {
    backgroundColor: "#ff332255"
  },
  ".cm-button": {
    verticalAlign: "middle",
    color: "inherit",
    fontSize: "70%",
    padding: ".2em 1em",
    borderRadius: "1px"
  },
  "&light .cm-button": {
    backgroundImage: "linear-gradient(#eff1f5, #d9d9df)",
    border: "1px solid #888",
    "&:active": {
      backgroundImage: "linear-gradient(#b4b4b4, #d0d3d6)"
    }
  },
  "&dark .cm-button": {
    backgroundImage: "linear-gradient(#393939, #111)",
    border: "1px solid #888",
    "&:active": {
      backgroundImage: "linear-gradient(#111, #333)"
    }
  },
  ".cm-textfield": {
    verticalAlign: "middle",
    color: "inherit",
    fontSize: "70%",
    border: "1px solid silver",
    padding: ".2em .5em"
  },
  "&light .cm-textfield": {
    backgroundColor: "white"
  },
  "&dark .cm-textfield": {
    border: "1px solid #555",
    backgroundColor: "inherit"
  }
}, qh), Nd = {
  childList: !0,
  characterData: !0,
  subtree: !0,
  attributes: !0,
  characterDataOldValue: !0
}, Ws = D.ie && D.ie_version <= 11;
class Wd {
  constructor(e) {
    this.view = e, this.active = !1, this.editContext = null, this.selectionRange = new uu(), this.selectionChanged = !1, this.delayedFlush = -1, this.resizeTimeout = -1, this.queue = [], this.delayedAndroidKey = null, this.flushingAndroidKey = -1, this.lastChange = 0, this.scrollTargets = [], this.intersection = null, this.resizeScroll = null, this.intersecting = !1, this.gapIntersection = null, this.gaps = [], this.printQuery = null, this.parentCheck = -1, this.dom = e.contentDOM, this.observer = new MutationObserver((t) => {
      for (let i of t)
        this.queue.push(i);
      (D.ie && D.ie_version <= 11 || D.ios && e.composing) && t.some((i) => i.type == "childList" && i.removedNodes.length || i.type == "characterData" && i.oldValue.length > i.target.nodeValue.length) ? this.flushSoon() : this.flush();
    }), window.EditContext && D.android && e.constructor.EDIT_CONTEXT !== !1 && // Chrome <126 doesn't support inverted selections in edit context (#1392)
    !(D.chrome && D.chrome_version < 126) && (this.editContext = new Fd(e), e.state.facet(ht) && (e.contentDOM.editContext = this.editContext.editContext)), Ws && (this.onCharData = (t) => {
      this.queue.push({
        target: t.target,
        type: "characterData",
        oldValue: t.prevValue
      }), this.flushSoon();
    }), this.onSelectionChange = this.onSelectionChange.bind(this), this.onResize = this.onResize.bind(this), this.onPrint = this.onPrint.bind(this), this.onScroll = this.onScroll.bind(this), window.matchMedia && (this.printQuery = window.matchMedia("print")), typeof ResizeObserver == "function" && (this.resizeScroll = new ResizeObserver(() => {
      var t;
      ((t = this.view.docView) === null || t === void 0 ? void 0 : t.lastUpdate) < Date.now() - 75 && this.onResize();
    }), this.resizeScroll.observe(e.scrollDOM)), this.addWindowListeners(this.win = e.win), this.start(), typeof IntersectionObserver == "function" && (this.intersection = new IntersectionObserver((t) => {
      this.parentCheck < 0 && (this.parentCheck = setTimeout(this.listenForScroll.bind(this), 1e3)), t.length > 0 && t[t.length - 1].intersectionRatio > 0 != this.intersecting && (this.intersecting = !this.intersecting, this.intersecting != this.view.inView && this.onScrollChanged(document.createEvent("Event")));
    }, { threshold: [0, 1e-3] }), this.intersection.observe(this.dom), this.gapIntersection = new IntersectionObserver((t) => {
      t.length > 0 && t[t.length - 1].intersectionRatio > 0 && this.onScrollChanged(document.createEvent("Event"));
    }, {})), this.listenForScroll(), this.readSelectionRange();
  }
  onScrollChanged(e) {
    this.view.inputState.runHandlers("scroll", e), this.intersecting && this.view.measure();
  }
  onScroll(e) {
    this.intersecting && this.flush(!1), this.editContext && this.view.requestMeasure(this.editContext.measureReq), this.onScrollChanged(e);
  }
  onResize() {
    this.resizeTimeout < 0 && (this.resizeTimeout = setTimeout(() => {
      this.resizeTimeout = -1, this.view.requestMeasure();
    }, 50));
  }
  onPrint(e) {
    (e.type == "change" || !e.type) && !e.matches || (this.view.viewState.printing = !0, this.view.measure(), setTimeout(() => {
      this.view.viewState.printing = !1, this.view.requestMeasure();
    }, 500));
  }
  updateGaps(e) {
    if (this.gapIntersection && (e.length != this.gaps.length || this.gaps.some((t, i) => t != e[i]))) {
      this.gapIntersection.disconnect();
      for (let t of e)
        this.gapIntersection.observe(t);
      this.gaps = e;
    }
  }
  onSelectionChange(e) {
    let t = this.selectionChanged;
    if (!this.readSelectionRange() || this.delayedAndroidKey)
      return;
    let { view: i } = this, s = this.selectionRange;
    if (i.state.facet(ht) ? i.root.activeElement != this.dom : !Oi(this.dom, s))
      return;
    let r = s.anchorNode && i.docView.tile.nearest(s.anchorNode);
    if (r && r.isWidget() && r.widget.ignoreEvent(e)) {
      t || (this.selectionChanged = !1);
      return;
    }
    (D.ie && D.ie_version <= 11 || D.android && D.chrome) && !i.state.selection.main.empty && // (Selection.isCollapsed isn't reliable on IE)
    s.focusNode && Li(s.focusNode, s.focusOffset, s.anchorNode, s.anchorOffset) ? this.flushSoon() : this.flush(!1);
  }
  readSelectionRange() {
    let { view: e } = this, t = Fi(e.root);
    if (!t)
      return !1;
    let i = D.safari && e.root.nodeType == 11 && e.root.activeElement == this.dom && Hd(this.view, t) || t;
    if (!i || this.selectionRange.eq(i))
      return !1;
    let s = Oi(this.dom, i);
    return s && !this.selectionChanged && e.inputState.lastFocusTime > Date.now() - 200 && e.inputState.lastTouchTime < Date.now() - 300 && pu(this.dom, i) ? (this.view.inputState.lastFocusTime = 0, e.docView.updateSelection(), !1) : (this.selectionRange.setRange(i), s && (this.selectionChanged = !0), !0);
  }
  setSelectionRange(e, t) {
    this.selectionRange.set(e.node, e.offset, t.node, t.offset), this.selectionChanged = !1;
  }
  clearSelectionRange() {
    this.selectionRange.set(null, 0, null, 0);
  }
  listenForScroll() {
    this.parentCheck = -1;
    let e = 0, t = null;
    for (let i = this.dom; i; )
      if (i.nodeType == 1)
        !t && e < this.scrollTargets.length && this.scrollTargets[e] == i ? e++ : t || (t = this.scrollTargets.slice(0, e)), t && t.push(i), i = i.assignedSlot || i.parentNode;
      else if (i.nodeType == 11)
        i = i.host;
      else
        break;
    if (e < this.scrollTargets.length && !t && (t = this.scrollTargets.slice(0, e)), t) {
      for (let i of this.scrollTargets)
        i.removeEventListener("scroll", this.onScroll);
      for (let i of this.scrollTargets = t)
        i.addEventListener("scroll", this.onScroll);
    }
  }
  ignore(e) {
    if (!this.active)
      return e();
    try {
      return this.stop(), e();
    } finally {
      this.start(), this.clear();
    }
  }
  start() {
    this.active || (this.observer.observe(this.dom, Nd), Ws && this.dom.addEventListener("DOMCharacterDataModified", this.onCharData), this.active = !0);
  }
  stop() {
    this.active && (this.active = !1, this.observer.disconnect(), Ws && this.dom.removeEventListener("DOMCharacterDataModified", this.onCharData));
  }
  // Throw away any pending changes
  clear() {
    this.processRecords(), this.queue.length = 0, this.selectionChanged = !1;
  }
  // Chrome Android, especially in combination with GBoard, not only
  // doesn't reliably fire regular key events, but also often
  // surrounds the effect of enter or backspace with a bunch of
  // composition events that, when interrupted, cause text duplication
  // or other kinds of corruption. This hack makes the editor back off
  // from handling DOM changes for a moment when such a key is
  // detected (via beforeinput or keydown), and then tries to flush
  // them or, if that has no effect, dispatches the given key.
  delayAndroidKey(e, t) {
    var i;
    if (!this.delayedAndroidKey) {
      let s = () => {
        let r = this.delayedAndroidKey;
        r && (this.clearDelayedAndroidKey(), this.view.inputState.lastKeyCode = r.keyCode, this.view.inputState.lastKeyTime = Date.now(), !this.flush() && r.force && ni(this.dom, r.key, r.keyCode));
      };
      this.flushingAndroidKey = this.view.win.requestAnimationFrame(s);
    }
    (!this.delayedAndroidKey || e == "Enter") && (this.delayedAndroidKey = {
      key: e,
      keyCode: t,
      // Only run the key handler when no changes are detected if
      // this isn't coming right after another change, in which case
      // it is probably part of a weird chain of updates, and should
      // be ignored if it returns the DOM to its previous state.
      force: this.lastChange < Date.now() - 50 || !!(!((i = this.delayedAndroidKey) === null || i === void 0) && i.force)
    });
  }
  clearDelayedAndroidKey() {
    this.win.cancelAnimationFrame(this.flushingAndroidKey), this.delayedAndroidKey = null, this.flushingAndroidKey = -1;
  }
  flushSoon() {
    this.delayedFlush < 0 && (this.delayedFlush = this.view.win.requestAnimationFrame(() => {
      this.delayedFlush = -1, this.flush();
    }));
  }
  forceFlush() {
    this.delayedFlush >= 0 && (this.view.win.cancelAnimationFrame(this.delayedFlush), this.delayedFlush = -1), this.flush();
  }
  pendingRecords() {
    for (let e of this.observer.takeRecords())
      this.queue.push(e);
    return this.queue;
  }
  processRecords() {
    let e = this.pendingRecords();
    e.length && (this.queue = []);
    let t = -1, i = -1, s = !1;
    for (let r of e) {
      let o = this.readMutation(r);
      o && (o.typeOver && (s = !0), t == -1 ? { from: t, to: i } = o : (t = Math.min(o.from, t), i = Math.max(o.to, i)));
    }
    return { from: t, to: i, typeOver: s };
  }
  readChange() {
    let { from: e, to: t, typeOver: i } = this.processRecords(), s = this.selectionChanged && Oi(this.dom, this.selectionRange);
    if (e < 0 && !s)
      return null;
    e > -1 && (this.lastChange = Date.now()), this.view.inputState.lastFocusTime = 0, this.selectionChanged = !1;
    let r = new td(this.view, e, t, i);
    return this.view.docView.domChanged = { newSel: r.newSel ? r.newSel.main : null }, r;
  }
  // Apply pending changes, if any
  flush(e = !0) {
    if (this.delayedFlush >= 0 || this.delayedAndroidKey)
      return !1;
    e && this.readSelectionRange();
    let t = this.readChange();
    if (!t)
      return this.view.requestMeasure(), !1;
    let i = this.view.state, s = Oh(this.view, t);
    return this.view.state == i && (t.domChanged || t.newSel && !Gn(this.view.state.selection, t.newSel.main)) && this.view.update([]), s;
  }
  readMutation(e) {
    let t = this.view.docView.tile.nearest(e.target);
    if (!t || t.isWidget())
      return null;
    if (t.markDirty(e.type == "attributes"), e.type == "childList") {
      let i = kl(t, e.previousSibling || e.target.previousSibling, -1), s = kl(t, e.nextSibling || e.target.nextSibling, 1);
      return {
        from: i ? t.posAfter(i) : t.posAtStart,
        to: s ? t.posBefore(s) : t.posAtEnd,
        typeOver: !1
      };
    } else return e.type == "characterData" ? { from: t.posAtStart, to: t.posAtEnd, typeOver: e.target.nodeValue == e.oldValue } : null;
  }
  setWindow(e) {
    e != this.win && (this.removeWindowListeners(this.win), this.win = e, this.addWindowListeners(this.win));
  }
  addWindowListeners(e) {
    e.addEventListener("resize", this.onResize), this.printQuery ? this.printQuery.addEventListener ? this.printQuery.addEventListener("change", this.onPrint) : this.printQuery.addListener(this.onPrint) : e.addEventListener("beforeprint", this.onPrint), e.addEventListener("scroll", this.onScroll), e.document.addEventListener("selectionchange", this.onSelectionChange);
  }
  removeWindowListeners(e) {
    e.removeEventListener("scroll", this.onScroll), e.removeEventListener("resize", this.onResize), this.printQuery ? this.printQuery.removeEventListener ? this.printQuery.removeEventListener("change", this.onPrint) : this.printQuery.removeListener(this.onPrint) : e.removeEventListener("beforeprint", this.onPrint), e.document.removeEventListener("selectionchange", this.onSelectionChange);
  }
  update(e) {
    this.editContext && (this.editContext.update(e), e.startState.facet(ht) != e.state.facet(ht) && (e.view.contentDOM.editContext = e.state.facet(ht) ? this.editContext.editContext : null));
  }
  destroy() {
    var e, t, i;
    this.stop(), (e = this.intersection) === null || e === void 0 || e.disconnect(), (t = this.gapIntersection) === null || t === void 0 || t.disconnect(), (i = this.resizeScroll) === null || i === void 0 || i.disconnect();
    for (let s of this.scrollTargets)
      s.removeEventListener("scroll", this.onScroll);
    this.removeWindowListeners(this.win), clearTimeout(this.parentCheck), clearTimeout(this.resizeTimeout), this.win.cancelAnimationFrame(this.delayedFlush), this.win.cancelAnimationFrame(this.flushingAndroidKey), this.editContext && (this.view.contentDOM.editContext = null, this.editContext.destroy());
  }
}
function kl(n, e, t) {
  for (; e; ) {
    let i = X.get(e);
    if (i && i.parent == n)
      return i;
    let s = e.parentNode;
    e = s != n.dom ? s : t > 0 ? e.nextSibling : e.previousSibling;
  }
  return null;
}
function Sl(n, e) {
  let t = e.startContainer, i = e.startOffset, s = e.endContainer, r = e.endOffset, o = n.docView.domAtPos(n.state.selection.main.anchor, 1);
  return Li(o.node, o.offset, s, r) && ([t, i, s, r] = [s, r, t, i]), { anchorNode: t, anchorOffset: i, focusNode: s, focusOffset: r };
}
function Hd(n, e) {
  if (e.getComposedRanges) {
    let s = e.getComposedRanges(n.root)[0];
    if (s)
      return Sl(n, s);
  }
  let t = null;
  function i(s) {
    s.preventDefault(), s.stopImmediatePropagation(), t = s.getTargetRanges()[0];
  }
  return n.contentDOM.addEventListener("beforeinput", i, !0), n.dom.ownerDocument.execCommand("indent"), n.contentDOM.removeEventListener("beforeinput", i, !0), t ? Sl(n, t) : null;
}
class Fd {
  constructor(e) {
    this.from = 0, this.to = 0, this.pendingContextChange = null, this.handlers = /* @__PURE__ */ Object.create(null), this.composing = null, this.resetRange(e.state);
    let t = this.editContext = new window.EditContext({
      text: e.state.doc.sliceString(this.from, this.to),
      selectionStart: this.toContextPos(Math.max(this.from, Math.min(this.to, e.state.selection.main.anchor))),
      selectionEnd: this.toContextPos(e.state.selection.main.head)
    });
    this.handlers.textupdate = (i) => {
      let s = e.state.selection.main, { anchor: r, head: o } = s, l = this.toEditorPos(i.updateRangeStart), a = this.toEditorPos(i.updateRangeEnd);
      e.inputState.composing >= 0 && !this.composing && (this.composing = { contextBase: i.updateRangeStart, editorBase: l, drifted: !1 });
      let h = a - l > i.text.length;
      l == this.from && r < this.from ? l = r : a == this.to && r > this.to && (a = r);
      let c = Bh(e.state.sliceDoc(l, a), i.text, (h ? s.from : s.to) - l, h ? "end" : null);
      if (!c) {
        let u = b.single(this.toEditorPos(i.selectionStart), this.toEditorPos(i.selectionEnd));
        Gn(u, s) || e.dispatch({ selection: u, userEvent: "select" });
        return;
      }
      let f = {
        from: c.from + l,
        to: c.toA + l,
        insert: z.of(i.text.slice(c.from, c.toB).split(`
`))
      };
      if ((D.mac || D.android) && f.from == o - 1 && /^\. ?$/.test(i.text) && e.contentDOM.getAttribute("autocorrect") == "off" && (f = { from: l, to: a, insert: z.of([i.text.replace(".", " ")]) }), this.pendingContextChange = f, !e.state.readOnly) {
        let u = this.to - this.from + (f.to - f.from + f.insert.length);
        uo(e, f, b.single(this.toEditorPos(i.selectionStart, u), this.toEditorPos(i.selectionEnd, u)));
      }
      this.pendingContextChange && (this.revertPending(e.state), this.setSelection(e.state)), f.from < f.to && !f.insert.length && e.inputState.composing >= 0 && !/[\\p{Alphabetic}\\p{Number}_]/.test(t.text.slice(Math.max(0, i.updateRangeStart - 1), Math.min(t.text.length, i.updateRangeStart + 1))) && this.handlers.compositionend(i);
    }, this.handlers.characterboundsupdate = (i) => {
      let s = [], r = null;
      for (let o = this.toEditorPos(i.rangeStart), l = this.toEditorPos(i.rangeEnd); o < l; o++) {
        let a = e.coordsForChar(o);
        r = a && new DOMRect(a.left, a.top, a.right - a.left, a.bottom - a.top) || r || new DOMRect(), s.push(r);
      }
      t.updateCharacterBounds(i.rangeStart, s);
    }, this.handlers.textformatupdate = (i) => {
      let s = [];
      for (let r of i.getTextFormats()) {
        let o = r.underlineStyle, l = r.underlineThickness;
        if (!/none/i.test(o) && !/none/i.test(l)) {
          let a = this.toEditorPos(r.rangeStart), h = this.toEditorPos(r.rangeEnd);
          if (a < h) {
            let c = `text-decoration: underline ${/^[a-z]/.test(o) ? o + " " : o == "Dashed" ? "dashed " : o == "Squiggle" ? "wavy " : ""}${/thin/i.test(l) ? 1 : 2}px`;
            s.push(B.mark({ attributes: { style: c } }).range(a, h));
          }
        }
      }
      e.dispatch({ effects: wh.of(B.set(s)) });
    }, this.handlers.compositionstart = () => {
      e.inputState.composing < 0 && (e.inputState.composing = 0, e.inputState.compositionFirstChange = !0);
    }, this.handlers.compositionend = () => {
      if (e.inputState.composing = -1, e.inputState.compositionFirstChange = null, this.composing) {
        let { drifted: i } = this.composing;
        this.composing = null, i && this.reset(e.state);
      }
    };
    for (let i in this.handlers)
      t.addEventListener(i, this.handlers[i]);
    this.measureReq = { read: (i) => {
      let s = Fi(i.root);
      s && s.rangeCount && this.editContext.updateSelectionBounds(s.getRangeAt(0).getBoundingClientRect());
    } };
  }
  applyEdits(e) {
    let t = 0, i = !1, s = this.pendingContextChange;
    return e.changes.iterChanges((r, o, l, a, h) => {
      if (i)
        return;
      let c = h.length - (o - r);
      if (s && o >= s.to)
        if (s.from == r && s.to == o && s.insert.eq(h)) {
          s = this.pendingContextChange = null, t += c, this.to += c;
          return;
        } else
          s = null, this.revertPending(e.state);
      if (r += t, o += t, o <= this.from)
        this.from += c, this.to += c;
      else if (r < this.to) {
        if (r < this.from || o > this.to || this.to - this.from + h.length > 3e4) {
          i = !0;
          return;
        }
        this.editContext.updateText(this.toContextPos(r), this.toContextPos(o), h.toString()), this.to += c;
      }
      t += c;
    }), s && !i && this.revertPending(e.state), !i;
  }
  update(e) {
    let t = this.pendingContextChange, i = e.startState.selection.main;
    this.composing && (this.composing.drifted || !e.changes.touchesRange(i.from, i.to) && e.transactions.some((s) => !s.isUserEvent("input.type") && s.changes.touchesRange(this.from, this.to))) ? (this.composing.drifted = !0, this.composing.editorBase = e.changes.mapPos(this.composing.editorBase)) : !this.applyEdits(e) || !this.rangeIsValid(e.state) ? (this.pendingContextChange = null, this.reset(e.state)) : (e.docChanged || e.selectionSet || t) && this.setSelection(e.state), (e.geometryChanged || e.docChanged || e.selectionSet) && e.view.requestMeasure(this.measureReq);
  }
  resetRange(e) {
    let { head: t } = e.selection.main;
    this.from = Math.max(
      0,
      t - 1e4
      /* CxVp.Margin */
    ), this.to = Math.min(
      e.doc.length,
      t + 1e4
      /* CxVp.Margin */
    );
  }
  reset(e) {
    this.resetRange(e), this.editContext.updateText(0, this.editContext.text.length, e.doc.sliceString(this.from, this.to)), this.setSelection(e);
  }
  revertPending(e) {
    let t = this.pendingContextChange;
    this.pendingContextChange = null, this.editContext.updateText(this.toContextPos(t.from), this.toContextPos(t.from + t.insert.length), e.doc.sliceString(t.from, t.to));
  }
  setSelection(e) {
    let { main: t } = e.selection, i = this.toContextPos(Math.max(this.from, Math.min(this.to, t.anchor))), s = this.toContextPos(t.head);
    (this.editContext.selectionStart != i || this.editContext.selectionEnd != s) && this.editContext.updateSelection(i, s);
  }
  rangeIsValid(e) {
    let { head: t } = e.selection.main;
    return !(this.from > 0 && t - this.from < 500 || this.to < e.doc.length && this.to - t < 500 || this.to - this.from > 1e4 * 3);
  }
  toEditorPos(e, t = this.to - this.from) {
    e = Math.min(e, t);
    let i = this.composing;
    return i && i.drifted ? i.editorBase + (e - i.contextBase) : e + this.from;
  }
  toContextPos(e) {
    let t = this.composing;
    return t && t.drifted ? t.contextBase + (e - t.editorBase) : e - this.from;
  }
  destroy() {
    for (let e in this.handlers)
      this.editContext.removeEventListener(e, this.handlers[e]);
  }
}
class M {
  /**
  The current editor state.
  */
  get state() {
    return this.viewState.state;
  }
  /**
  To be able to display large documents without consuming too much
  memory or overloading the browser, CodeMirror only draws the
  code that is visible (plus a margin around it) to the DOM. This
  property tells you the extent of the current drawn viewport, in
  document positions.
  */
  get viewport() {
    return this.viewState.viewport;
  }
  /**
  When there are, for example, large collapsed ranges in the
  viewport, its size can be a lot bigger than the actual visible
  content. Thus, if you are doing something like styling the
  content in the viewport, it is preferable to only do so for
  these ranges, which are the subset of the viewport that is
  actually drawn.
  */
  get visibleRanges() {
    return this.viewState.visibleRanges;
  }
  /**
  Returns false when the editor is entirely scrolled out of view
  or otherwise hidden.
  */
  get inView() {
    return this.viewState.inView;
  }
  /**
  Indicates whether the user is currently composing text via
  [IME](https://en.wikipedia.org/wiki/Input_method), and at least
  one change has been made in the current composition.
  */
  get composing() {
    return !!this.inputState && this.inputState.composing > 0;
  }
  /**
  Indicates whether the user is currently in composing state. Note
  that on some platforms, like Android, this will be the case a
  lot, since just putting the cursor on a word starts a
  composition there.
  */
  get compositionStarted() {
    return !!this.inputState && this.inputState.composing >= 0;
  }
  /**
  The document or shadow root that the view lives in.
  */
  get root() {
    return this._root;
  }
  /**
  @internal
  */
  get win() {
    return this.dom.ownerDocument.defaultView || window;
  }
  /**
  Construct a new view. You'll want to either provide a `parent`
  option, or put `view.dom` into your document after creating a
  view, so that the user can see the editor.
  */
  constructor(e = {}) {
    var t;
    this.plugins = [], this.pluginMap = /* @__PURE__ */ new Map(), this.editorAttrs = {}, this.contentAttrs = {}, this.bidiCache = [], this.destroyed = !1, this.updateState = 2, this.measureScheduled = -1, this.measureRequests = [], this.contentDOM = document.createElement("div"), this.scrollDOM = document.createElement("div"), this.scrollDOM.tabIndex = -1, this.scrollDOM.className = "cm-scroller", this.scrollDOM.appendChild(this.contentDOM), this.announceDOM = document.createElement("div"), this.announceDOM.className = "cm-announced", this.announceDOM.setAttribute("aria-live", "polite"), this.dom = document.createElement("div"), this.dom.appendChild(this.announceDOM), this.dom.appendChild(this.scrollDOM), e.parent && e.parent.appendChild(this.dom);
    let { dispatch: i } = e;
    this.dispatchTransactions = e.dispatchTransactions || i && ((s) => s.forEach((r) => i(r, this))) || ((s) => this.update(s)), this.dispatch = this.dispatch.bind(this), this._root = e.root || du(e.parent) || document, this.viewState = new xl(this, e.state || F.create(e)), e.scrollTo && e.scrollTo.is(dn) && (this.viewState.scrollTarget = e.scrollTo.value.clip(this.viewState.state)), this.plugins = this.state.facet(Xt).map((s) => new Ls(s));
    for (let s of this.plugins)
      s.update(this);
    this.observer = new Wd(this), this.inputState = new rd(this), this.inputState.ensureHandlers(this.plugins), this.docView = new ol(this), this.mountStyles(), this.updateAttrs(), this.updateState = 0, this.requestMeasure(), !((t = document.fonts) === null || t === void 0) && t.ready && document.fonts.ready.then(() => {
      this.viewState.mustMeasureContent = "refresh", this.requestMeasure();
    });
  }
  dispatch(...e) {
    let t = e.length == 1 && e[0] instanceof ie ? e : e.length == 1 && Array.isArray(e[0]) ? e[0] : [this.state.update(...e)];
    this.dispatchTransactions(t, this);
  }
  /**
  Update the view for the given array of transactions. This will
  update the visible document and selection to match the state
  produced by the transactions, and notify view plugins of the
  change. You should usually call
  [`dispatch`](https://codemirror.net/6/docs/ref/#view.EditorView.dispatch) instead, which uses this
  as a primitive.
  */
  update(e) {
    if (this.updateState != 0)
      throw new Error("Calls to EditorView.update are not allowed while an update is in progress");
    let t = !1, i = !1, s, r = this.state;
    for (let u of e) {
      if (u.startState != r)
        throw new RangeError("Trying to update state with a transaction that doesn't start from the previous state.");
      r = u.state;
    }
    if (this.destroyed) {
      this.viewState.state = r;
      return;
    }
    let o = this.hasFocus, l = 0, a = null;
    e.some((u) => u.annotation(Nh)) ? (this.inputState.notifiedFocused = o, l = 1) : o != this.inputState.notifiedFocused && (this.inputState.notifiedFocused = o, a = Wh(r, o), a || (l = 1));
    let h = this.observer.delayedAndroidKey, c = null;
    if (h ? (this.observer.clearDelayedAndroidKey(), c = this.observer.readChange(), (c && !this.state.doc.eq(r.doc) || !this.state.selection.eq(r.selection)) && (c = null)) : this.observer.clear(), r.facet(F.phrases) != this.state.facet(F.phrases))
      return this.setState(r);
    s = jn.create(this, r, e), s.flags |= l;
    let f = this.viewState.scrollTarget;
    try {
      this.updateState = 2;
      for (let u of e) {
        if (f && (f = f.map(u.changes)), u.scrollIntoView) {
          let { main: d } = u.state.selection, { x: p, y: m } = this.state.facet(M.cursorScrollMargin);
          f = new si(d.empty ? d : b.cursor(d.head, d.head > d.anchor ? -1 : 1), "nearest", "nearest", m, p);
        }
        for (let d of u.effects)
          d.is(dn) && (f = d.value.clip(this.state));
      }
      this.viewState.update(s, f), this.bidiCache = Jn.update(this.bidiCache, s.changes), s.empty || (this.updatePlugins(s), this.inputState.update(s)), t = this.docView.update(s), this.state.facet(Ci) != this.styleModules && this.mountStyles(), i = this.updateAttrs(), this.showAnnouncements(e), this.docView.updateSelection(t, e.some((u) => u.isUserEvent("select.pointer")));
    } finally {
      this.updateState = 0;
    }
    if (s.startState.facet(xn) != s.state.facet(xn) && (this.viewState.mustMeasureContent = !0), (t || i || f || this.viewState.mustEnforceCursorAssoc || this.viewState.mustMeasureContent) && this.requestMeasure(), t && this.docViewUpdate(), !s.empty)
      for (let u of this.state.facet(Dr))
        try {
          u(s);
        } catch (d) {
          Ae(this.state, d, "update listener");
        }
    (a || c) && Promise.resolve().then(() => {
      a && this.state == a.startState && this.dispatch(a), c && !Oh(this, c) && h.force && ni(this.contentDOM, h.key, h.keyCode);
    });
  }
  /**
  Reset the view to the given state. (This will cause the entire
  document to be redrawn and all view plugins to be reinitialized,
  so you should probably only use it when the new state isn't
  derived from the old state. Otherwise, use
  [`dispatch`](https://codemirror.net/6/docs/ref/#view.EditorView.dispatch) instead.)
  */
  setState(e) {
    if (this.updateState != 0)
      throw new Error("Calls to EditorView.setState are not allowed while an update is in progress");
    if (this.destroyed) {
      this.viewState.state = e;
      return;
    }
    this.updateState = 2;
    let t = this.hasFocus;
    try {
      for (let i of this.plugins)
        i.destroy(this);
      this.viewState = new xl(this, e), this.plugins = e.facet(Xt).map((i) => new Ls(i)), this.pluginMap.clear();
      for (let i of this.plugins)
        i.update(this);
      this.docView.destroy(), this.docView = new ol(this), this.inputState.ensureHandlers(this.plugins), this.mountStyles(), this.updateAttrs(), this.bidiCache = [];
    } finally {
      this.updateState = 0;
    }
    t && this.focus(), this.requestMeasure();
  }
  updatePlugins(e) {
    let t = e.startState.facet(Xt), i = e.state.facet(Xt);
    if (t != i) {
      let s = [];
      for (let r of i) {
        let o = t.indexOf(r);
        if (o < 0)
          s.push(new Ls(r));
        else {
          let l = this.plugins[o];
          l.mustUpdate = e, s.push(l);
        }
      }
      for (let r of this.plugins)
        r.mustUpdate != e && r.destroy(this);
      this.plugins = s, this.pluginMap.clear();
    } else
      for (let s of this.plugins)
        s.mustUpdate = e;
    for (let s = 0; s < this.plugins.length; s++)
      this.plugins[s].update(this);
    t != i && this.inputState.ensureHandlers(this.plugins);
  }
  docViewUpdate() {
    for (let e of this.plugins) {
      let t = e.value;
      if (t && t.docViewUpdate)
        try {
          t.docViewUpdate(this);
        } catch (i) {
          Ae(this.state, i, "doc view update listener");
        }
    }
  }
  /**
  @internal
  */
  measure(e = !0) {
    if (this.destroyed)
      return;
    if (this.measureScheduled > -1 && this.win.cancelAnimationFrame(this.measureScheduled), this.observer.delayedAndroidKey) {
      this.measureScheduled = -1, this.requestMeasure();
      return;
    }
    this.measureScheduled = 0, e && this.observer.forceFlush();
    let t = null, i = this.viewState.scrollParent, s = this.viewState.getScrollOffset(), { scrollAnchorPos: r, scrollAnchorHeight: o } = this.viewState;
    Math.abs(s - this.viewState.scrollOffset) > 1 && (o = -1), this.viewState.scrollAnchorHeight = -1;
    try {
      for (let l = 0; ; l++) {
        if (o < 0)
          if (nh(i || this.win))
            r = -1, o = this.viewState.heightMap.height;
          else {
            let d = this.viewState.scrollAnchorAt(s);
            r = d.from, o = d.top;
          }
        this.updateState = 1;
        let a = this.viewState.measure();
        if (!a && !this.measureRequests.length && this.viewState.scrollTarget == null)
          break;
        if (l > 5) {
          console.warn(this.measureRequests.length ? "Measure loop restarted more than 5 times" : "Viewport failed to stabilize");
          break;
        }
        let h = [];
        a & 4 || ([this.measureRequests, h] = [h, this.measureRequests]);
        let c = h.map((d) => {
          try {
            return d.read(this);
          } catch (p) {
            return Ae(this.state, p), Cl;
          }
        }), f = jn.create(this, this.state, []), u = !1;
        f.flags |= a, t ? t.flags |= a : t = f, this.updateState = 2, f.empty || (this.updatePlugins(f), this.inputState.update(f), this.updateAttrs(), u = this.docView.update(f), u && this.docViewUpdate());
        for (let d = 0; d < h.length; d++)
          if (c[d] != Cl)
            try {
              let p = h[d];
              p.write && p.write(c[d], this);
            } catch (p) {
              Ae(this.state, p);
            }
        if (u && this.docView.updateSelection(!0), !f.viewportChanged && this.measureRequests.length == 0) {
          if (this.viewState.editorHeight)
            if (this.viewState.scrollTarget) {
              this.docView.scrollIntoView(this.viewState.scrollTarget), this.viewState.scrollTarget = null, o = -1;
              continue;
            } else {
              let p = ((r < 0 ? this.viewState.heightMap.height : this.viewState.lineBlockAt(r).top) - o) / this.scaleY;
              if ((p > 1 || p < -1) && (i == this.scrollDOM || this.hasFocus || Math.max(this.inputState.lastWheelEvent, this.inputState.lastTouchTime) > Date.now() - 100)) {
                s = s + p, i ? i.scrollTop += p : this.win.scrollBy(0, p), o = -1;
                continue;
              }
            }
          break;
        }
      }
    } finally {
      this.updateState = 0, this.measureScheduled = -1;
    }
    if (t && !t.empty)
      for (let l of this.state.facet(Dr))
        l(t);
  }
  /**
  Get the CSS classes for the currently active editor themes.
  */
  get themeClasses() {
    return Pr + " " + (this.state.facet(Rr) ? zh : Vh) + " " + this.state.facet(xn);
  }
  updateAttrs() {
    let e = Al(this, vh, {
      class: "cm-editor" + (this.hasFocus ? " cm-focused " : " ") + this.themeClasses
    }), t = {
      spellcheck: "false",
      autocorrect: "off",
      autocapitalize: "off",
      writingsuggestions: "false",
      translate: "no",
      contenteditable: this.state.facet(ht) ? "true" : "false",
      class: "cm-content",
      style: `${D.tabSize}: ${this.state.tabSize}`,
      role: "textbox",
      "aria-multiline": "true"
    };
    this.state.readOnly && (t["aria-readonly"] = "true"), Al(this, ho, t);
    let i = this.observer.ignore(() => {
      let s = el(this.contentDOM, this.contentAttrs, t), r = el(this.dom, this.editorAttrs, e);
      return s || r;
    });
    return this.editorAttrs = e, this.contentAttrs = t, i;
  }
  showAnnouncements(e) {
    let t = !0;
    for (let i of e)
      for (let s of i.effects)
        if (s.is(M.announce)) {
          t && (this.announceDOM.textContent = ""), t = !1;
          let r = this.announceDOM.appendChild(document.createElement("div"));
          r.textContent = s.value;
        }
  }
  mountStyles() {
    this.styleModules = this.state.facet(Ci);
    let e = this.state.facet(M.cspNonce);
    vt.mount(this.root, this.styleModules.concat(Id).reverse(), e ? { nonce: e } : void 0);
  }
  readMeasured() {
    if (this.updateState == 2)
      throw new Error("Reading the editor layout isn't allowed during an update");
    this.updateState == 0 && this.measureScheduled > -1 && this.measure(!1);
  }
  /**
  Schedule a layout measurement, optionally providing callbacks to
  do custom DOM measuring followed by a DOM write phase. Using
  this is preferable reading DOM layout directly from, for
  example, an event handler, because it'll make sure measuring and
  drawing done by other components is synchronized, avoiding
  unnecessary DOM layout computations.
  */
  requestMeasure(e) {
    if (this.measureScheduled < 0 && (this.measureScheduled = this.win.requestAnimationFrame(() => this.measure())), e) {
      if (this.measureRequests.indexOf(e) > -1)
        return;
      if (e.key != null) {
        for (let t = 0; t < this.measureRequests.length; t++)
          if (this.measureRequests[t].key === e.key) {
            this.measureRequests[t] = e;
            return;
          }
      }
      this.measureRequests.push(e);
    }
  }
  /**
  Get the value of a specific plugin, if present. Note that
  plugins that crash can be dropped from a view, so even when you
  know you registered a given plugin, it is recommended to check
  the return value of this method.
  */
  plugin(e) {
    let t = this.pluginMap.get(e);
    return (t === void 0 || t && t.plugin != e) && this.pluginMap.set(e, t = this.plugins.find((i) => i.plugin == e) || null), t && t.update(this).value;
  }
  /**
  The top position of the document, in screen coordinates. This
  may be negative when the editor is scrolled down. Points
  directly to the top of the first line, not above the padding.
  */
  get documentTop() {
    return this.contentDOM.getBoundingClientRect().top + this.viewState.paddingTop;
  }
  /**
  Reports the padding above and below the document.
  */
  get documentPadding() {
    return { top: this.viewState.paddingTop, bottom: this.viewState.paddingBottom };
  }
  /**
  If the editor is transformed with CSS, this provides the scale
  along the X axis. Otherwise, it will just be 1. Note that
  transforms other than translation and scaling are not supported.
  */
  get scaleX() {
    return this.viewState.scaleX;
  }
  /**
  Provide the CSS transformed scale along the Y axis.
  */
  get scaleY() {
    return this.viewState.scaleY;
  }
  /**
  Find the text line or block widget at the given vertical
  position (which is interpreted as relative to the [top of the
  document](https://codemirror.net/6/docs/ref/#view.EditorView.documentTop)).
  */
  elementAtHeight(e) {
    return this.readMeasured(), this.viewState.elementAtHeight(e);
  }
  /**
  Find the line block (see
  [`lineBlockAt`](https://codemirror.net/6/docs/ref/#view.EditorView.lineBlockAt)) at the given
  height, again interpreted relative to the [top of the
  document](https://codemirror.net/6/docs/ref/#view.EditorView.documentTop).
  */
  lineBlockAtHeight(e) {
    return this.readMeasured(), this.viewState.lineBlockAtHeight(e);
  }
  /**
  Get the extent and vertical position of all [line
  blocks](https://codemirror.net/6/docs/ref/#view.EditorView.lineBlockAt) in the viewport. Positions
  are relative to the [top of the
  document](https://codemirror.net/6/docs/ref/#view.EditorView.documentTop);
  */
  get viewportLineBlocks() {
    return this.viewState.viewportLines;
  }
  /**
  Find the line block around the given document position. A line
  block is a range delimited on both sides by either a
  non-[hidden](https://codemirror.net/6/docs/ref/#view.Decoration^replace) line break, or the
  start/end of the document. It will usually just hold a line of
  text, but may be broken into multiple textblocks by block
  widgets.
  */
  lineBlockAt(e) {
    return this.viewState.lineBlockAt(e);
  }
  /**
  The editor's total content height.
  */
  get contentHeight() {
    return this.viewState.contentHeight;
  }
  /**
  Move a cursor position by [grapheme
  cluster](https://codemirror.net/6/docs/ref/#state.findClusterBreak). `forward` determines whether
  the motion is away from the line start, or towards it. In
  bidirectional text, the line is traversed in visual order, using
  the editor's [text direction](https://codemirror.net/6/docs/ref/#view.EditorView.textDirection).
  When the start position was the last one on the line, the
  returned position will be across the line break. If there is no
  further line, the original position is returned.
  
  By default, this method moves over a single cluster. The
  optional `by` argument can be used to move across more. It will
  be called with the first cluster as argument, and should return
  a predicate that determines, for each subsequent cluster,
  whether it should also be moved over.
  */
  moveByChar(e, t, i) {
    return Is(this, e, ll(this, e, t, i));
  }
  /**
  Move a cursor position across the next group of either
  [letters](https://codemirror.net/6/docs/ref/#state.EditorState.charCategorizer) or non-letter
  non-whitespace characters.
  */
  moveByGroup(e, t) {
    return Is(this, e, ll(this, e, t, (i) => Yu(this, e.head, i)));
  }
  /**
  Get the cursor position visually at the start or end of a line.
  Note that this may differ from the _logical_ position at its
  start or end (which is simply at `line.from`/`line.to`) if text
  at the start or end goes against the line's base text direction.
  */
  visualLineSide(e, t) {
    let i = this.bidiSpans(e), s = this.textDirectionAt(e.from), r = i[t ? i.length - 1 : 0];
    return b.cursor(r.side(t, s) + e.from, r.forward(!t, s) ? 1 : -1);
  }
  /**
  Move to the next line boundary in the given direction. If
  `includeWrap` is true, line wrapping is on, and there is a
  further wrap point on the current line, the wrap point will be
  returned. Otherwise this function will return the start or end
  of the line.
  */
  moveToLineBoundary(e, t, i = !0) {
    return Gu(this, e, t, i);
  }
  /**
  Move a cursor position vertically. When `distance` isn't given,
  it defaults to moving to the next line (including wrapped
  lines). Otherwise, `distance` should provide a positive distance
  in pixels.
  
  When `start` has a
  [`goalColumn`](https://codemirror.net/6/docs/ref/#state.SelectionRange.goalColumn), the vertical
  motion will use that as a target horizontal position. Otherwise,
  the cursor's own horizontal position is used. The returned
  cursor will have its goal column set to whichever column was
  used.
  */
  moveVertically(e, t, i) {
    return Is(this, e, Ju(this, e, t, i));
  }
  /**
  Find the DOM parent node and offset (child offset if `node` is
  an element, character offset when it is a text node) at the
  given document position.
  
  Note that for positions that aren't currently in
  `visibleRanges`, the resulting DOM position isn't necessarily
  meaningful (it may just point before or after a placeholder
  element).
  */
  domAtPos(e, t = 1) {
    return this.docView.domAtPos(e, t);
  }
  /**
  Find the document position at the given DOM node. Can be useful
  for associating positions with DOM events. Will raise an error
  when `node` isn't part of the editor content.
  */
  posAtDOM(e, t = 0) {
    return this.docView.posFromDOM(e, t);
  }
  posAtCoords(e, t = !0) {
    this.readMeasured();
    let i = Lr(this, e, t);
    return i && i.pos;
  }
  posAndSideAtCoords(e, t = !0) {
    return this.readMeasured(), Lr(this, e, t);
  }
  /**
  Get the screen coordinates at the given document position.
  `side` determines whether the coordinates are based on the
  element before (-1) or after (1) the position (if no element is
  available on the given side, the method will transparently use
  another strategy to get reasonable coordinates).
  */
  coordsAtPos(e, t = 1) {
    this.readMeasured();
    let i = this.docView.coordsAt(e, t);
    if (!i || i.left == i.right)
      return i;
    let s = this.state.doc.lineAt(e), r = this.bidiSpans(s), o = r[it.find(r, e - s.from, -1, t)];
    return Vi(i, o.dir == _.LTR == t > 0);
  }
  /**
  Return the rectangle around a given character. If `pos` does not
  point in front of a character that is in the viewport and
  rendered (i.e. not replaced, not a line break), this will return
  null. For space characters that are a line wrap point, this will
  return the position before the line break.
  */
  coordsForChar(e) {
    return this.readMeasured(), this.docView.coordsForChar(e);
  }
  /**
  The default width of a character in the editor. May not
  accurately reflect the width of all characters (given variable
  width fonts or styling of invididual ranges).
  */
  get defaultCharacterWidth() {
    return this.viewState.heightOracle.charWidth;
  }
  /**
  The default height of a line in the editor. May not be accurate
  for all lines.
  */
  get defaultLineHeight() {
    return this.viewState.heightOracle.lineHeight;
  }
  /**
  The text direction
  ([`direction`](https://developer.mozilla.org/en-US/docs/Web/CSS/direction)
  CSS property) of the editor's content element.
  */
  get textDirection() {
    return this.viewState.defaultTextDirection;
  }
  /**
  Find the text direction of the block at the given position, as
  assigned by CSS. If
  [`perLineTextDirection`](https://codemirror.net/6/docs/ref/#view.EditorView^perLineTextDirection)
  isn't enabled, or the given position is outside of the viewport,
  this will always return the same as
  [`textDirection`](https://codemirror.net/6/docs/ref/#view.EditorView.textDirection). Note that
  this may trigger a DOM layout.
  */
  textDirectionAt(e) {
    return !this.state.facet(yh) || e < this.viewport.from || e > this.viewport.to ? this.textDirection : (this.readMeasured(), this.docView.textDirectionAt(e));
  }
  /**
  Whether this editor [wraps lines](https://codemirror.net/6/docs/ref/#view.EditorView.lineWrapping)
  (as determined by the
  [`white-space`](https://developer.mozilla.org/en-US/docs/Web/CSS/white-space)
  CSS property of its content element).
  */
  get lineWrapping() {
    return this.viewState.heightOracle.lineWrapping;
  }
  /**
  Returns the bidirectional text structure of the given line
  (which should be in the current document) as an array of span
  objects. The order of these spans matches the [text
  direction](https://codemirror.net/6/docs/ref/#view.EditorView.textDirection)—if that is
  left-to-right, the leftmost spans come first, otherwise the
  rightmost spans come first.
  */
  bidiSpans(e) {
    if (e.length > Vd)
      return hh(e.length);
    let t = this.textDirectionAt(e.from), i;
    for (let r of this.bidiCache)
      if (r.from == e.from && r.dir == t && (r.fresh || ah(r.isolates, i = nl(this, e))))
        return r.order;
    i || (i = nl(this, e));
    let s = vu(e.text, t, i);
    return this.bidiCache.push(new Jn(e.from, e.to, t, i, !0, s)), s;
  }
  /**
  Check whether the editor has focus.
  */
  get hasFocus() {
    var e;
    return (this.dom.ownerDocument.hasFocus() || D.safari && ((e = this.inputState) === null || e === void 0 ? void 0 : e.lastContextMenu) > Date.now() - 3e4) && this.root.activeElement == this.contentDOM;
  }
  /**
  Put focus on the editor.
  */
  focus() {
    this.observer.ignore(() => {
      ih(this.contentDOM), this.docView.updateSelection();
    });
  }
  /**
  Update the [root](https://codemirror.net/6/docs/ref/##view.EditorViewConfig.root) in which the editor lives. This is only
  necessary when moving the editor's existing DOM to a new window or shadow root.
  */
  setRoot(e) {
    this._root != e && (this._root = e, this.observer.setWindow((e.nodeType == 9 ? e : e.ownerDocument).defaultView || window), this.mountStyles());
  }
  /**
  Clean up this editor view, removing its element from the
  document, unregistering event handlers, and notifying
  plugins. The view instance can no longer be used after
  calling this.
  */
  destroy() {
    this.root.activeElement == this.contentDOM && this.contentDOM.blur();
    for (let e of this.plugins)
      e.destroy(this);
    this.plugins = [], this.inputState.destroy(), this.docView.destroy(), this.dom.remove(), this.observer.destroy(), this.measureScheduled > -1 && this.win.cancelAnimationFrame(this.measureScheduled), this.destroyed = !0;
  }
  /**
  Returns an effect that can be
  [added](https://codemirror.net/6/docs/ref/#state.TransactionSpec.effects) to a transaction to
  cause it to scroll the given position or range into view.
  */
  static scrollIntoView(e, t = {}) {
    var i, s, r, o;
    return dn.of(new si(typeof e == "number" ? b.cursor(e) : e, (i = t.y) !== null && i !== void 0 ? i : "nearest", (s = t.x) !== null && s !== void 0 ? s : "nearest", (r = t.yMargin) !== null && r !== void 0 ? r : 5, (o = t.xMargin) !== null && o !== void 0 ? o : 5));
  }
  /**
  Return an effect that resets the editor to its current (at the
  time this method was called) scroll position. Note that this
  only affects the editor's own scrollable element, not parents.
  See also
  [`EditorViewConfig.scrollTo`](https://codemirror.net/6/docs/ref/#view.EditorViewConfig.scrollTo).
  
  The effect should be used with a document identical to the one
  it was created for. Failing to do so is not an error, but may
  not scroll to the expected position. You can
  [map](https://codemirror.net/6/docs/ref/#state.StateEffect.map) the effect to account for changes.
  */
  scrollSnapshot() {
    let { scrollTop: e, scrollLeft: t } = this.scrollDOM, i = this.viewState.scrollAnchorAt(e);
    return dn.of(new si(b.cursor(i.from), "start", "start", i.top - e, t, !0));
  }
  /**
  Enable or disable tab-focus mode, which disables key bindings
  for Tab and Shift-Tab, letting the browser's default
  focus-changing behavior go through instead. This is useful to
  prevent trapping keyboard users in your editor.
  
  Without argument, this toggles the mode. With a boolean, it
  enables (true) or disables it (false). Given a number, it
  temporarily enables the mode until that number of milliseconds
  have passed or another non-Tab key is pressed.
  */
  setTabFocusMode(e) {
    e == null ? this.inputState.tabFocusMode = this.inputState.tabFocusMode < 0 ? 0 : -1 : typeof e == "boolean" ? this.inputState.tabFocusMode = e ? 0 : -1 : this.inputState.tabFocusMode != 0 && (this.inputState.tabFocusMode = Date.now() + e);
  }
  /**
  Returns an extension that can be used to add DOM event handlers.
  The value should be an object mapping event names to handler
  functions. For any given event, such functions are ordered by
  extension precedence, and the first handler to return true will
  be assumed to have handled that event, and no other handlers or
  built-in behavior will be activated for it. These are registered
  on the [content element](https://codemirror.net/6/docs/ref/#view.EditorView.contentDOM), except
  for `scroll` handlers, which will be called any time the
  editor's [scroll element](https://codemirror.net/6/docs/ref/#view.EditorView.scrollDOM) or one of
  its parent nodes is scrolled.
  */
  static domEventHandlers(e) {
    return Q.define(() => ({}), { eventHandlers: e });
  }
  /**
  Create an extension that registers DOM event observers. Contrary
  to event [handlers](https://codemirror.net/6/docs/ref/#view.EditorView^domEventHandlers),
  observers can't be prevented from running by a higher-precedence
  handler returning true. They also don't prevent other handlers
  and observers from running when they return true, and should not
  call `preventDefault`.
  */
  static domEventObservers(e) {
    return Q.define(() => ({}), { eventObservers: e });
  }
  /**
  Create a theme extension. The first argument can be a
  [`style-mod`](https://code.haverbeke.berlin/marijn/style-mod#documentation)
  style spec providing the styles for the theme. These will be
  prefixed with a generated class for the style.
  
  Because the selectors will be prefixed with a scope class, rule
  that directly match the editor's [wrapper
  element](https://codemirror.net/6/docs/ref/#view.EditorView.dom)—to which the scope class will be
  added—need to be explicitly differentiated by adding an `&` to
  the selector for that element—for example
  `&.cm-focused`.
  
  When `dark` is set to true, the theme will be marked as dark,
  which will cause the `&dark` rules from [base
  themes](https://codemirror.net/6/docs/ref/#view.EditorView^baseTheme) to be used (as opposed to
  `&light` when a light theme is active).
  */
  static theme(e, t) {
    let i = vt.newName(), s = [xn.of(i), Ci.of(Ir(`.${i}`, e))];
    return t && t.dark && s.push(Rr.of(!0)), s;
  }
  /**
  Create an extension that adds styles to the base theme. Like
  with [`theme`](https://codemirror.net/6/docs/ref/#view.EditorView^theme), use `&` to indicate the
  place of the editor wrapper element when directly targeting
  that. You can also use `&dark` or `&light` instead to only
  target editors with a dark or light theme.
  */
  static baseTheme(e) {
    return Tt.lowest(Ci.of(Ir("." + Pr, e, qh)));
  }
  /**
  Retrieve an editor view instance from the view's DOM
  representation.
  */
  static findFromDOM(e) {
    var t;
    let i = e.querySelector(".cm-content"), s = i && X.get(i) || X.get(e);
    return ((t = s?.root) === null || t === void 0 ? void 0 : t.view) || null;
  }
}
M.styleModule = Ci;
M.inputHandler = mh;
M.clipboardInputFilter = lo;
M.clipboardOutputFilter = ao;
M.scrollHandler = xh;
M.focusChangeEffect = gh;
M.perLineTextDirection = yh;
M.exceptionSink = ph;
M.updateListener = Dr;
M.editable = ht;
M.mouseSelectionStyle = dh;
M.dragMovesSelection = uh;
M.clickAddsSelectionRange = fh;
M.decorations = ds;
M.blockWrappers = kh;
M.outerDecorations = co;
M.atomicRanges = en;
M.bidiIsolatedRanges = Sh;
M.cursorScrollMargin = /* @__PURE__ */ T.define({
  combine: (n) => {
    let e = 5, t = 5;
    for (let i of n)
      typeof i == "number" ? e = t = i : { x: e, y: t } = i;
    return { x: e, y: t };
  }
});
M.scrollMargins = Ch;
M.darkTheme = Rr;
M.cspNonce = /* @__PURE__ */ T.define({ combine: (n) => n.length ? n[0] : "" });
M.contentAttributes = ho;
M.editorAttributes = vh;
M.lineWrapping = /* @__PURE__ */ M.contentAttributes.of({ class: "cm-lineWrapping" });
M.announce = /* @__PURE__ */ R.define();
const Vd = 4096, Cl = {};
class Jn {
  constructor(e, t, i, s, r, o) {
    this.from = e, this.to = t, this.dir = i, this.isolates = s, this.fresh = r, this.order = o;
  }
  static update(e, t) {
    if (t.empty && !e.some((r) => r.fresh))
      return e;
    let i = [], s = e.length ? e[e.length - 1].dir : _.LTR;
    for (let r = Math.max(0, e.length - 10); r < e.length; r++) {
      let o = e[r];
      o.dir == s && !t.touchesRange(o.from, o.to) && i.push(new Jn(t.mapPos(o.from, 1), t.mapPos(o.to, -1), o.dir, o.isolates, !1, o.order));
    }
    return i;
  }
}
function Al(n, e, t) {
  for (let i = n.state.facet(e), s = i.length - 1; s >= 0; s--) {
    let r = i[s], o = typeof r == "function" ? r(n) : r;
    o && so(o, t);
  }
  return t;
}
const zd = D.mac ? "mac" : D.windows ? "win" : D.linux ? "linux" : "key";
function qd(n, e) {
  const t = n.split(/-(?!$)/);
  let i = t[t.length - 1];
  i == "Space" && (i = " ");
  let s, r, o, l;
  for (let a = 0; a < t.length - 1; ++a) {
    const h = t[a];
    if (/^(cmd|meta|m)$/i.test(h))
      l = !0;
    else if (/^a(lt)?$/i.test(h))
      s = !0;
    else if (/^(c|ctrl|control)$/i.test(h))
      r = !0;
    else if (/^s(hift)?$/i.test(h))
      o = !0;
    else if (/^mod$/i.test(h))
      e == "mac" ? l = !0 : r = !0;
    else
      throw new Error("Unrecognized modifier name: " + h);
  }
  return s && (i = "Alt-" + i), r && (i = "Ctrl-" + i), l && (i = "Meta-" + i), o && (i = "Shift-" + i), i;
}
function wn(n, e, t) {
  return e.altKey && (n = "Alt-" + n), e.ctrlKey && (n = "Ctrl-" + n), e.metaKey && (n = "Meta-" + n), t !== !1 && e.shiftKey && (n = "Shift-" + n), n;
}
const $d = /* @__PURE__ */ Tt.default(/* @__PURE__ */ M.domEventHandlers({
  keydown(n, e) {
    return Kh($h(e.state), n, e, "editor");
  }
})), tn = /* @__PURE__ */ T.define({ enables: $d }), Ml = /* @__PURE__ */ new WeakMap();
function $h(n) {
  let e = n.facet(tn), t = Ml.get(e);
  return t || Ml.set(e, t = Ud(e.reduce((i, s) => i.concat(s), []))), t;
}
function Kd(n, e, t) {
  return Kh($h(n.state), e, n, t);
}
let yt = null;
const jd = 4e3;
function Ud(n, e = zd) {
  let t = /* @__PURE__ */ Object.create(null), i = /* @__PURE__ */ Object.create(null), s = (o, l) => {
    let a = i[o];
    if (a == null)
      i[o] = l;
    else if (a != l)
      throw new Error("Key binding " + o + " is used both as a regular binding and as a multi-stroke prefix");
  }, r = (o, l, a, h, c) => {
    var f, u;
    let d = t[o] || (t[o] = /* @__PURE__ */ Object.create(null)), p = l.split(/ (?!$)/).map((y) => qd(y, e));
    for (let y = 1; y < p.length; y++) {
      let w = p.slice(0, y).join(" ");
      s(w, !0), d[w] || (d[w] = {
        preventDefault: !0,
        stopPropagation: !1,
        run: [(v) => {
          let O = yt = { view: v, prefix: w, scope: o };
          return setTimeout(() => {
            yt == O && (yt = null);
          }, jd), !0;
        }]
      });
    }
    let m = p.join(" ");
    s(m, !1);
    let g = d[m] || (d[m] = {
      preventDefault: !1,
      stopPropagation: !1,
      run: ((u = (f = d._any) === null || f === void 0 ? void 0 : f.run) === null || u === void 0 ? void 0 : u.slice()) || []
    });
    a && g.run.push(a), h && (g.preventDefault = !0), c && (g.stopPropagation = !0);
  };
  for (let o of n) {
    let l = o.scope ? o.scope.split(" ") : ["editor"];
    if (o.any)
      for (let h of l) {
        let c = t[h] || (t[h] = /* @__PURE__ */ Object.create(null));
        c._any || (c._any = { preventDefault: !1, stopPropagation: !1, run: [] });
        let { any: f } = o;
        for (let u in c)
          c[u].run.push((d) => f(d, Nr));
      }
    let a = o[e] || o.key;
    if (a)
      for (let h of l)
        r(h, a, o.run, o.preventDefault, o.stopPropagation), o.shift && r(h, "Shift-" + a, o.shift, o.preventDefault, o.stopPropagation);
  }
  return t;
}
let Nr = null;
function Kh(n, e, t, i) {
  Nr = e;
  let s = ou(e), r = ke(s, 0), o = et(r) == s.length && s != " ", l = "", a = !1, h = !1, c = !1;
  yt && yt.view == t && yt.scope == i && (l = yt.prefix + " ", Eh.indexOf(e.keyCode) < 0 && (h = !0, yt = null));
  let f = /* @__PURE__ */ new Set(), u = (g) => {
    if (g) {
      for (let y of g.run)
        if (!f.has(y) && (f.add(y), y(t)))
          return g.stopPropagation && (c = !0), !0;
      g.preventDefault && (g.stopPropagation && (c = !0), h = !0);
    }
    return !1;
  }, d = n[i], p, m;
  return d && (u(d[l + wn(s, e, !o)]) ? a = !0 : o && (e.altKey || e.metaKey || e.ctrlKey) && // Ctrl-Alt may be used for AltGr on Windows
  !(D.windows && e.ctrlKey && e.altKey) && // Alt-combinations on macOS tend to be typed characters
  !(D.mac && e.altKey && !(e.ctrlKey || e.metaKey)) && (p = kt[e.keyCode]) && p != s ? (u(d[l + wn(p, e, !0)]) || e.shiftKey && (m = Wi[e.keyCode]) != s && m != p && u(d[l + wn(m, e, !1)])) && (a = !0) : o && e.shiftKey && u(d[l + wn(s, e, !0)]) && (a = !0), !a && u(d._any) && (a = !0)), h && (a = !0), a && c && e.stopPropagation(), Nr = null, a;
}
class Ht {
  /**
  Create a marker with the given class and dimensions. If `width`
  is null, the DOM element will get no width style.
  */
  constructor(e, t, i, s, r) {
    this.className = e, this.left = t, this.top = i, this.width = s, this.height = r;
  }
  draw() {
    let e = document.createElement("div");
    return e.className = this.className, this.adjust(e), e;
  }
  update(e, t) {
    return t.className != this.className ? !1 : (this.adjust(e), !0);
  }
  adjust(e) {
    e.style.left = this.left + "px", e.style.top = this.top + "px", this.width != null && (e.style.width = this.width + "px"), e.style.height = this.height + "px";
  }
  eq(e) {
    return this.left == e.left && this.top == e.top && this.width == e.width && this.height == e.height && this.className == e.className;
  }
  /**
  Create a set of rectangles for the given selection range,
  assigning them theclass`className`. Will create a single
  rectangle for empty ranges, and a set of selection-style
  rectangles covering the range's content (in a bidi-aware
  way) for non-empty ones.
  */
  static forRange(e, t, i) {
    if (i.empty) {
      let s = e.coordsAtPos(i.head, i.assoc || 1);
      if (!s)
        return [];
      let r = jh(e);
      return [new Ht(t, s.left - r.left, s.top - r.top, null, s.bottom - s.top)];
    } else
      return _d(e, t, i);
  }
}
function jh(n) {
  let e = n.scrollDOM.getBoundingClientRect();
  return { left: (n.textDirection == _.LTR ? e.left : e.right - n.scrollDOM.clientWidth * n.scaleX) - n.scrollDOM.scrollLeft * n.scaleX, top: e.top - n.scrollDOM.scrollTop * n.scaleY };
}
function Tl(n, e, t, i) {
  let s = n.coordsAtPos(e, t * 2);
  if (!s)
    return i;
  let r = n.dom.getBoundingClientRect(), o = (s.top + s.bottom) / 2, l = n.posAtCoords({ x: r.left + 1, y: o }), a = n.posAtCoords({ x: r.right - 1, y: o });
  return l == null || a == null ? i : { from: Math.max(i.from, Math.min(l, a)), to: Math.min(i.to, Math.max(l, a)) };
}
function _d(n, e, t) {
  if (t.to <= n.viewport.from || t.from >= n.viewport.to)
    return [];
  let i = Math.max(t.from, n.viewport.from), s = Math.min(t.to, n.viewport.to), r = n.textDirection == _.LTR, o = n.contentDOM, l = o.getBoundingClientRect(), a = jh(n), h = o.querySelector(".cm-line"), c = h && window.getComputedStyle(h), f = l.left + (c ? parseInt(c.paddingLeft) + Math.min(0, parseInt(c.textIndent)) : 0), u = l.right - (c ? parseInt(c.paddingRight) : 0), d = Br(n, i, 1), p = Br(n, s, -1), m = d.type == de.Text ? d : null, g = p.type == de.Text ? p : null;
  if (m && (n.lineWrapping || d.widgetLineBreaks) && (m = Tl(n, i, 1, m)), g && (n.lineWrapping || p.widgetLineBreaks) && (g = Tl(n, s, -1, g)), m && g && m.from == g.from && m.to == g.to)
    return w(v(t.from, t.to, m));
  {
    let k = m ? v(t.from, null, m) : O(d, !1), S = g ? v(null, t.to, g) : O(p, !0), C = [];
    return (m || d).to < (g || p).from - (m && g ? 1 : 0) || d.widgetLineBreaks > 1 && k.bottom + n.defaultLineHeight / 2 < S.top ? C.push(y(f, k.bottom, u, S.top)) : k.bottom < S.top && n.elementAtHeight((k.bottom + S.top) / 2).type == de.Text && (k.bottom = S.top = (k.bottom + S.top) / 2), w(k).concat(C).concat(w(S));
  }
  function y(k, S, C, E) {
    return new Ht(e, k - a.left, S - a.top, Math.max(0, C - k), E - S);
  }
  function w({ top: k, bottom: S, horizontal: C }) {
    let E = [];
    for (let I = 0; I < C.length; I += 2)
      E.push(y(C[I], k, C[I + 1], S));
    return E;
  }
  function v(k, S, C) {
    let E = 1e9, I = -1e9, q = [];
    function P(N, $, Z, se, be) {
      let J = n.coordsAtPos(N, N == C.to ? -2 : 2), le = n.coordsAtPos(Z, Z == C.from ? 2 : -2);
      !J || !le || (E = Math.min(J.top, le.top, E), I = Math.max(J.bottom, le.bottom, I), be == _.LTR ? q.push(r && $ ? f : J.left, r && se ? u : le.right) : q.push(!r && se ? f : le.left, !r && $ ? u : J.right));
    }
    let L = k ?? C.from, H = S ?? C.to;
    for (let N of n.visibleRanges)
      if (N.to > L && N.from < H)
        for (let $ = Math.max(N.from, L), Z = Math.min(N.to, H); ; ) {
          let se = n.state.doc.lineAt($);
          for (let be of n.bidiSpans(se)) {
            let J = be.from + se.from, le = be.to + se.from;
            if (J >= Z)
              break;
            le > $ && P(Math.max(J, $), k == null && J <= L, Math.min(le, Z), S == null && le >= H, be.dir);
          }
          if ($ = se.to + 1, $ >= Z)
            break;
        }
    return q.length == 0 && P(L, k == null, H, S == null, n.textDirection), { top: E, bottom: I, horizontal: q };
  }
  function O(k, S) {
    let C = l.top + (S ? k.top : k.bottom);
    return { top: C, bottom: C, horizontal: [] };
  }
}
function Gd(n, e) {
  return n.constructor == e.constructor && n.eq(e);
}
class Yd {
  constructor(e, t) {
    this.view = e, this.layer = t, this.drawn = [], this.scaleX = 1, this.scaleY = 1, this.measureReq = { read: this.measure.bind(this), write: this.draw.bind(this) }, this.dom = e.scrollDOM.appendChild(document.createElement("div")), this.dom.classList.add("cm-layer"), t.above && this.dom.classList.add("cm-layer-above"), t.class && this.dom.classList.add(t.class), this.scale(), this.dom.setAttribute("aria-hidden", "true"), this.setOrder(e.state), e.requestMeasure(this.measureReq), t.mount && t.mount(this.dom, e);
  }
  update(e) {
    e.startState.facet(Nn) != e.state.facet(Nn) && this.setOrder(e.state), (this.layer.update(e, this.dom) || e.geometryChanged) && (this.scale(), e.view.requestMeasure(this.measureReq));
  }
  docViewUpdate(e) {
    this.layer.updateOnDocViewUpdate !== !1 && e.requestMeasure(this.measureReq);
  }
  setOrder(e) {
    let t = 0, i = e.facet(Nn);
    for (; t < i.length && i[t] != this.layer; )
      t++;
    this.dom.style.zIndex = String((this.layer.above ? 150 : -1) - t);
  }
  measure() {
    return this.layer.markers(this.view);
  }
  scale() {
    let { scaleX: e, scaleY: t } = this.view;
    (e != this.scaleX || t != this.scaleY) && (this.scaleX = e, this.scaleY = t, this.dom.style.transform = `scale(${1 / e}, ${1 / t})`);
  }
  draw(e) {
    if (e.length != this.drawn.length || e.some((t, i) => !Gd(t, this.drawn[i]))) {
      let t = this.dom.firstChild, i = 0;
      for (let s of e)
        s.update && t && s.constructor && this.drawn[i].constructor && s.update(t, this.drawn[i]) ? (t = t.nextSibling, i++) : this.dom.insertBefore(s.draw(), t);
      for (; t; ) {
        let s = t.nextSibling;
        t.remove(), t = s;
      }
      this.drawn = e, D.webkit && (this.dom.style.display = this.dom.firstChild ? "" : "none");
    }
  }
  destroy() {
    this.layer.destroy && this.layer.destroy(this.dom, this.view), this.dom.remove();
  }
}
const Nn = /* @__PURE__ */ T.define();
function Uh(n) {
  return [
    Q.define((e) => new Yd(e, n)),
    Nn.of(n)
  ];
}
const fi = /* @__PURE__ */ T.define({
  combine(n) {
    return at(n, {
      cursorBlinkRate: 1200,
      drawRangeCursor: !0,
      iosSelectionHandles: !0
    }, {
      cursorBlinkRate: (e, t) => Math.min(e, t),
      drawRangeCursor: (e, t) => e || t
    });
  }
});
function Jd(n = {}) {
  return [
    fi.of(n),
    Xd,
    Qd,
    Zd,
    bh.of(!0)
  ];
}
function _h(n) {
  return n.startState.facet(fi) != n.state.facet(fi);
}
const Xd = /* @__PURE__ */ Uh({
  above: !0,
  markers(n) {
    let { state: e } = n, t = e.facet(fi), i = [];
    for (let s of e.selection.ranges) {
      let r = s == e.selection.main;
      if (s.empty || t.drawRangeCursor && !(r && D.ios && t.iosSelectionHandles)) {
        let o = r ? "cm-cursor cm-cursor-primary" : "cm-cursor cm-cursor-secondary", l = s.empty ? s : b.cursor(s.head, s.assoc);
        for (let a of Ht.forRange(n, o, l))
          i.push(a);
      }
    }
    return i;
  },
  update(n, e) {
    n.transactions.some((i) => i.selection) && (e.style.animationName = e.style.animationName == "cm-blink" ? "cm-blink2" : "cm-blink");
    let t = _h(n);
    return t && Dl(n.state, e), n.docChanged || n.selectionSet || t;
  },
  mount(n, e) {
    Dl(e.state, n);
  },
  class: "cm-cursorLayer"
});
function Dl(n, e) {
  e.style.animationDuration = n.facet(fi).cursorBlinkRate + "ms";
}
const Qd = /* @__PURE__ */ Uh({
  above: !1,
  markers(n) {
    let e = [], { main: t, ranges: i } = n.state.selection;
    for (let s of i)
      if (!s.empty)
        for (let r of Ht.forRange(n, "cm-selectionBackground", s))
          e.push(r);
    if (D.ios && !t.empty && n.state.facet(fi).iosSelectionHandles) {
      for (let s of Ht.forRange(n, "cm-selectionHandle cm-selectionHandle-start", b.cursor(t.from, 1)))
        e.push(s);
      for (let s of Ht.forRange(n, "cm-selectionHandle cm-selectionHandle-end", b.cursor(t.to, 1)))
        e.push(s);
    }
    return e;
  },
  update(n, e) {
    return n.docChanged || n.selectionSet || n.viewportChanged || _h(n);
  },
  class: "cm-selectionLayer"
}), Zd = /* @__PURE__ */ Tt.highest(/* @__PURE__ */ M.theme({
  ".cm-line": {
    "& ::selection, &::selection": { backgroundColor: "transparent !important" },
    caretColor: "transparent !important"
  },
  ".cm-content": {
    caretColor: "transparent !important",
    "& :focus": {
      caretColor: "initial !important",
      "&::selection, & ::selection": {
        backgroundColor: "Highlight !important"
      }
    }
  }
})), Gh = /* @__PURE__ */ R.define({
  map(n, e) {
    return n == null ? null : e.mapPos(n);
  }
}), Mi = /* @__PURE__ */ me.define({
  create() {
    return null;
  },
  update(n, e) {
    return n != null && (n = e.changes.mapPos(n)), e.effects.reduce((t, i) => i.is(Gh) ? i.value : t, n);
  }
}), ep = /* @__PURE__ */ Q.fromClass(class {
  constructor(n) {
    this.view = n, this.cursor = null, this.measureReq = { read: this.readPos.bind(this), write: this.drawCursor.bind(this) };
  }
  update(n) {
    var e;
    let t = n.state.field(Mi);
    t == null ? this.cursor != null && ((e = this.cursor) === null || e === void 0 || e.remove(), this.cursor = null) : (this.cursor || (this.cursor = this.view.scrollDOM.appendChild(document.createElement("div")), this.cursor.className = "cm-dropCursor"), (n.startState.field(Mi) != t || n.docChanged || n.geometryChanged) && this.view.requestMeasure(this.measureReq));
  }
  readPos() {
    let { view: n } = this, e = n.state.field(Mi), t = e != null && n.coordsAtPos(e);
    if (!t)
      return null;
    let i = n.scrollDOM.getBoundingClientRect();
    return {
      left: t.left - i.left + n.scrollDOM.scrollLeft * n.scaleX,
      top: t.top - i.top + n.scrollDOM.scrollTop * n.scaleY,
      height: t.bottom - t.top
    };
  }
  drawCursor(n) {
    if (this.cursor) {
      let { scaleX: e, scaleY: t } = this.view;
      n ? (this.cursor.style.left = n.left / e + "px", this.cursor.style.top = n.top / t + "px", this.cursor.style.height = n.height / t + "px") : this.cursor.style.left = "-100000px";
    }
  }
  destroy() {
    this.cursor && this.cursor.remove();
  }
  setDropPos(n) {
    this.view.state.field(Mi) != n && this.view.dispatch({ effects: Gh.of(n) });
  }
}, {
  eventObservers: {
    dragover(n) {
      this.setDropPos(this.view.posAtCoords({ x: n.clientX, y: n.clientY }));
    },
    dragleave(n) {
      (n.target == this.view.contentDOM || !this.view.contentDOM.contains(n.relatedTarget)) && this.setDropPos(null);
    },
    dragend() {
      this.setDropPos(null);
    },
    drop() {
      this.setDropPos(null);
    }
  }
});
function tp() {
  return [Mi, ep];
}
function Ol(n, e, t, i, s) {
  e.lastIndex = 0;
  for (let r = n.iterRange(t, i), o = t, l; !r.next().done; o += r.value.length)
    if (!r.lineBreak)
      for (; l = e.exec(r.value); )
        s(o + l.index, l);
}
function ip(n, e) {
  let t = n.visibleRanges;
  if (t.length == 1 && t[0].from == n.viewport.from && t[0].to == n.viewport.to)
    return t;
  let i = [];
  for (let { from: s, to: r } of t)
    s = Math.max(n.state.doc.lineAt(s).from, s - e), r = Math.min(n.state.doc.lineAt(r).to, r + e), i.length && i[i.length - 1].to >= s ? i[i.length - 1].to = r : i.push({ from: s, to: r });
  return i;
}
class np {
  /**
  Create a decorator.
  */
  constructor(e) {
    const { regexp: t, decoration: i, decorate: s, boundary: r, maxLength: o = 1e3 } = e;
    if (!t.global)
      throw new RangeError("The regular expression given to MatchDecorator should have its 'g' flag set");
    if (this.regexp = t, s)
      this.addMatch = (l, a, h, c) => s(c, h, h + l[0].length, l, a);
    else if (typeof i == "function")
      this.addMatch = (l, a, h, c) => {
        let f = i(l, a, h);
        f && c(h, h + l[0].length, f);
      };
    else if (i)
      this.addMatch = (l, a, h, c) => c(h, h + l[0].length, i);
    else
      throw new RangeError("Either 'decorate' or 'decoration' should be provided to MatchDecorator");
    this.boundary = r, this.maxLength = o;
  }
  /**
  Compute the full set of decorations for matches in the given
  view's viewport. You'll want to call this when initializing your
  plugin.
  */
  createDeco(e) {
    let t = new ft(), i = t.add.bind(t);
    for (let { from: s, to: r } of ip(e, this.maxLength))
      Ol(e.state.doc, this.regexp, s, r, (o, l) => this.addMatch(l, e, o, i));
    return t.finish();
  }
  /**
  Update a set of decorations for a view update. `deco` _must_ be
  the set of decorations produced by _this_ `MatchDecorator` for
  the view state before the update.
  */
  updateDeco(e, t) {
    let i = 1e9, s = -1;
    return e.docChanged && e.changes.iterChanges((r, o, l, a) => {
      a >= e.view.viewport.from && l <= e.view.viewport.to && (i = Math.min(l, i), s = Math.max(a, s));
    }), e.viewportMoved || s - i > 1e3 ? this.createDeco(e.view) : s > -1 ? this.updateRange(e.view, t.map(e.changes), i, s) : t;
  }
  updateRange(e, t, i, s) {
    for (let r of e.visibleRanges) {
      let o = Math.max(r.from, i), l = Math.min(r.to, s);
      if (l >= o) {
        let a = e.state.doc.lineAt(o), h = a.to < l ? e.state.doc.lineAt(l) : a, c = Math.max(r.from, a.from), f = Math.min(r.to, h.to);
        if (this.boundary) {
          for (; o > a.from; o--)
            if (this.boundary.test(a.text[o - 1 - a.from])) {
              c = o;
              break;
            }
          for (; l < h.to; l++)
            if (this.boundary.test(h.text[l - h.from])) {
              f = l;
              break;
            }
        }
        let u = [], d, p = (m, g, y) => u.push(y.range(m, g));
        if (a == h)
          for (this.regexp.lastIndex = c - a.from; (d = this.regexp.exec(a.text)) && d.index < f - a.from; )
            this.addMatch(d, e, d.index + a.from, p);
        else
          Ol(e.state.doc, this.regexp, c, f, (m, g) => this.addMatch(g, e, m, p));
        t = t.update({ filterFrom: c, filterTo: f, filter: (m, g) => m < c || g > f, add: u });
      }
    }
    return t;
  }
}
const Wr = /x/.unicode != null ? "gu" : "g", sp = /* @__PURE__ */ new RegExp(`[\0-\b
--­؜​‎‏\u2028\u2029‭‮⁦⁧⁩\uFEFF￹-￼]`, Wr), rp = {
  0: "null",
  7: "bell",
  8: "backspace",
  10: "newline",
  11: "vertical tab",
  13: "carriage return",
  27: "escape",
  8203: "zero width space",
  8204: "zero width non-joiner",
  8205: "zero width joiner",
  8206: "left-to-right mark",
  8207: "right-to-left mark",
  8232: "line separator",
  8237: "left-to-right override",
  8238: "right-to-left override",
  8294: "left-to-right isolate",
  8295: "right-to-left isolate",
  8297: "pop directional isolate",
  8233: "paragraph separator",
  65279: "zero width no-break space",
  65532: "object replacement"
};
let Hs = null;
function op() {
  var n;
  if (Hs == null && typeof document < "u" && document.body) {
    let e = document.body.style;
    Hs = ((n = e.tabSize) !== null && n !== void 0 ? n : e.MozTabSize) != null;
  }
  return Hs || !1;
}
const Wn = /* @__PURE__ */ T.define({
  combine(n) {
    let e = at(n, {
      render: null,
      specialChars: sp,
      addSpecialChars: null
    });
    return (e.replaceTabs = !op()) && (e.specialChars = new RegExp("	|" + e.specialChars.source, Wr)), e.addSpecialChars && (e.specialChars = new RegExp(e.specialChars.source + "|" + e.addSpecialChars.source, Wr)), e;
  }
});
function lp(n = {}) {
  return [Wn.of(n), ap()];
}
let Bl = null;
function ap() {
  return Bl || (Bl = Q.fromClass(class {
    constructor(n) {
      this.view = n, this.decorations = B.none, this.decorationCache = /* @__PURE__ */ Object.create(null), this.decorator = this.makeDecorator(n.state.facet(Wn)), this.decorations = this.decorator.createDeco(n);
    }
    makeDecorator(n) {
      return new np({
        regexp: n.specialChars,
        decoration: (e, t, i) => {
          let { doc: s } = t.state, r = ke(e[0], 0);
          if (r == 9) {
            let o = s.lineAt(i), l = t.state.tabSize, a = pi(o.text, l, i - o.from);
            return B.replace({
              widget: new up((l - a % l) * this.view.defaultCharacterWidth / this.view.scaleX)
            });
          }
          return this.decorationCache[r] || (this.decorationCache[r] = B.replace({ widget: new fp(n, r) }));
        },
        boundary: n.replaceTabs ? void 0 : /[^]/
      });
    }
    update(n) {
      let e = n.state.facet(Wn);
      n.startState.facet(Wn) != e ? (this.decorator = this.makeDecorator(e), this.decorations = this.decorator.createDeco(n.view)) : this.decorations = this.decorator.updateDeco(n, this.decorations);
    }
  }, {
    decorations: (n) => n.decorations
  }));
}
const hp = "•";
function cp(n) {
  return n >= 32 ? hp : n == 10 ? "␤" : String.fromCharCode(9216 + n);
}
class fp extends Ke {
  constructor(e, t) {
    super(), this.options = e, this.code = t;
  }
  eq(e) {
    return e.code == this.code;
  }
  toDOM(e) {
    let t = cp(this.code), i = e.state.phrase("Control character") + " " + (rp[this.code] || "0x" + this.code.toString(16)), s = this.options.render && this.options.render(this.code, i, t);
    if (s)
      return s;
    let r = document.createElement("span");
    return r.textContent = t, r.title = i, r.setAttribute("aria-label", i), r.className = "cm-specialChar", r;
  }
  ignoreEvent() {
    return !1;
  }
}
class up extends Ke {
  constructor(e) {
    super(), this.width = e;
  }
  eq(e) {
    return e.width == this.width;
  }
  toDOM() {
    let e = document.createElement("span");
    return e.textContent = "	", e.className = "cm-tab", e.style.width = this.width + "px", e;
  }
  ignoreEvent() {
    return !1;
  }
}
function dp() {
  return mp;
}
const pp = /* @__PURE__ */ B.line({ class: "cm-activeLine" }), mp = /* @__PURE__ */ Q.fromClass(class {
  constructor(n) {
    this.decorations = this.getDeco(n);
  }
  update(n) {
    (n.docChanged || n.selectionSet) && (this.decorations = this.getDeco(n.view));
  }
  getDeco(n) {
    let e = -1, t = [];
    for (let i of n.state.selection.ranges) {
      let s = n.lineBlockAt(i.head);
      s.from > e && (t.push(pp.range(s.from)), e = s.from);
    }
    return B.set(t);
  }
}, {
  decorations: (n) => n.decorations
});
class gp extends Ke {
  constructor(e) {
    super(), this.content = e;
  }
  toDOM(e) {
    let t = document.createElement("span");
    return t.className = "cm-placeholder", t.style.pointerEvents = "none", t.appendChild(typeof this.content == "string" ? document.createTextNode(this.content) : typeof this.content == "function" ? this.content(e) : this.content.cloneNode(!0)), t.setAttribute("aria-hidden", "true"), t;
  }
  coordsAt(e) {
    let t = e.firstChild ? Bi(e.firstChild) : [];
    if (!t.length)
      return null;
    let i = window.getComputedStyle(e.parentNode), s = Vi(t[0], i.direction != "rtl"), r = parseInt(i.lineHeight);
    return s.bottom - s.top > r * 1.5 ? { left: s.left, right: s.right, top: s.top, bottom: s.top + r } : s;
  }
  ignoreEvent() {
    return !1;
  }
}
function yp(n) {
  let e = Q.fromClass(class {
    constructor(t) {
      this.view = t, this.placeholder = n ? B.set([B.widget({ widget: new gp(n), side: 1 }).range(0)]) : B.none;
    }
    get decorations() {
      return this.view.state.doc.length ? B.none : this.placeholder;
    }
  }, { decorations: (t) => t.decorations });
  return typeof n == "string" ? [
    e,
    M.contentAttributes.of({ "aria-placeholder": n })
  ] : e;
}
const Hr = 2e3;
function bp(n, e, t) {
  let i = Math.min(e.line, t.line), s = Math.max(e.line, t.line), r = [];
  if (e.off > Hr || t.off > Hr || e.col < 0 || t.col < 0) {
    let o = Math.min(e.off, t.off), l = Math.max(e.off, t.off);
    for (let a = i; a <= s; a++) {
      let h = n.doc.line(a);
      h.length <= l && r.push(b.range(h.from + o, h.to + l));
    }
  } else {
    let o = Math.min(e.col, t.col), l = Math.max(e.col, t.col);
    for (let a = i; a <= s; a++) {
      let h = n.doc.line(a), c = yr(h.text, o, n.tabSize, !0);
      if (c < 0)
        r.push(b.cursor(h.to));
      else {
        let f = yr(h.text, l, n.tabSize);
        r.push(b.range(h.from + c, h.from + f));
      }
    }
  }
  return r;
}
function xp(n, e) {
  let t = n.coordsAtPos(n.viewport.from);
  return t ? Math.round(Math.abs((t.left - e) / n.defaultCharacterWidth)) : -1;
}
function Ll(n, e) {
  let t = n.posAtCoords({ x: e.clientX, y: e.clientY }, !1), i = n.state.doc.lineAt(t), s = t - i.from, r = s > Hr ? -1 : s == i.length ? xp(n, e.clientX) : pi(i.text, n.state.tabSize, t - i.from);
  return { line: i.number, col: r, off: s };
}
function wp(n, e) {
  let t = Ll(n, e), i = n.state.selection;
  return t ? {
    update(s) {
      if (s.docChanged) {
        let r = s.changes.mapPos(s.startState.doc.line(t.line).from), o = s.state.doc.lineAt(r);
        t = { line: o.number, col: t.col, off: Math.min(t.off, o.length) }, i = i.map(s.changes);
      }
    },
    get(s, r, o) {
      let l = Ll(n, s);
      if (!l)
        return i;
      let a = bp(n.state, t, l);
      return a.length ? o ? b.create(a.concat(i.ranges)) : b.create(a) : i;
    }
  } : null;
}
function vp(n) {
  let e = ((t) => t.altKey && t.button == 0);
  return M.mouseSelectionStyle.of((t, i) => e(i) ? wp(t, i) : null);
}
const kp = {
  Alt: [18, (n) => !!n.altKey],
  Control: [17, (n) => !!n.ctrlKey],
  Shift: [16, (n) => !!n.shiftKey],
  Meta: [91, (n) => !!n.metaKey]
}, Sp = { style: "cursor: crosshair" };
function Cp(n = {}) {
  let [e, t] = kp[n.key || "Alt"], i = Q.fromClass(class {
    constructor(s) {
      this.view = s, this.isDown = !1;
    }
    set(s) {
      this.isDown != s && (this.isDown = s, this.view.update([]));
    }
  }, {
    eventObservers: {
      keydown(s) {
        this.set(s.keyCode == e || t(s));
      },
      keyup(s) {
        (s.keyCode == e || !t(s)) && this.set(!1);
      },
      mousemove(s) {
        this.set(t(s));
      }
    }
  });
  return [
    i,
    M.contentAttributes.of((s) => {
      var r;
      return !((r = s.plugin(i)) === null || r === void 0) && r.isDown ? Sp : null;
    })
  ];
}
const vn = "-10000px";
class Yh {
  constructor(e, t, i, s) {
    this.facet = t, this.createTooltipView = i, this.removeTooltipView = s, this.input = e.state.facet(t), this.tooltips = this.input.filter((o) => o);
    let r = null;
    this.tooltipViews = this.tooltips.map((o) => r = i(o, r));
  }
  update(e, t) {
    var i;
    let s = e.state.facet(this.facet), r = s.filter((a) => a);
    if (s === this.input) {
      for (let a of this.tooltipViews)
        a.update && a.update(e);
      return !1;
    }
    let o = [], l = t ? [] : null;
    for (let a = 0; a < r.length; a++) {
      let h = r[a], c = -1;
      if (h) {
        for (let f = 0; f < this.tooltips.length; f++) {
          let u = this.tooltips[f];
          u && u.create == h.create && (c = f);
        }
        if (c < 0)
          o[a] = this.createTooltipView(h, a ? o[a - 1] : null), l && (l[a] = !!h.above);
        else {
          let f = o[a] = this.tooltipViews[c];
          l && (l[a] = t[c]), f.update && f.update(e);
        }
      }
    }
    for (let a of this.tooltipViews)
      o.indexOf(a) < 0 && (this.removeTooltipView(a), (i = a.destroy) === null || i === void 0 || i.call(a));
    return t && (l.forEach((a, h) => t[h] = a), t.length = l.length), this.input = s, this.tooltips = r, this.tooltipViews = o, !0;
  }
}
function Ap(n) {
  let e = n.dom.ownerDocument.documentElement;
  return { top: 0, left: 0, bottom: e.clientHeight, right: e.clientWidth };
}
const Fs = /* @__PURE__ */ T.define({
  combine: (n) => {
    var e, t, i;
    return {
      position: D.ios ? "absolute" : ((e = n.find((s) => s.position)) === null || e === void 0 ? void 0 : e.position) || "fixed",
      parent: ((t = n.find((s) => s.parent)) === null || t === void 0 ? void 0 : t.parent) || null,
      tooltipSpace: ((i = n.find((s) => s.tooltipSpace)) === null || i === void 0 ? void 0 : i.tooltipSpace) || Ap
    };
  }
}), El = /* @__PURE__ */ new WeakMap(), go = /* @__PURE__ */ Q.fromClass(class {
  constructor(n) {
    this.view = n, this.above = [], this.inView = !0, this.madeAbsolute = !1, this.lastTransaction = 0, this.measureTimeout = -1;
    let e = n.state.facet(Fs);
    this.position = e.position, this.parent = e.parent, this.classes = n.themeClasses, this.createContainer(), this.measureReq = { read: this.readMeasure.bind(this), write: this.writeMeasure.bind(this), key: this }, this.resizeObserver = typeof ResizeObserver == "function" ? new ResizeObserver(() => this.measureSoon()) : null, this.manager = new Yh(n, yo, (t, i) => this.createTooltip(t, i), (t) => {
      this.resizeObserver && this.resizeObserver.unobserve(t.dom), t.dom.remove();
    }), this.above = this.manager.tooltips.map((t) => !!t.above), this.intersectionObserver = typeof IntersectionObserver == "function" ? new IntersectionObserver((t) => {
      Date.now() > this.lastTransaction - 50 && t.length > 0 && t[t.length - 1].intersectionRatio < 1 && this.measureSoon();
    }, { threshold: [1] }) : null, this.observeIntersection(), n.win.addEventListener("resize", this.measureSoon = this.measureSoon.bind(this)), this.maybeMeasure();
  }
  createContainer() {
    this.parent ? (this.container = document.createElement("div"), this.container.style.position = "relative", this.container.className = this.view.themeClasses, this.parent.appendChild(this.container)) : this.container = this.view.dom;
  }
  observeIntersection() {
    if (this.intersectionObserver) {
      this.intersectionObserver.disconnect();
      for (let n of this.manager.tooltipViews)
        this.intersectionObserver.observe(n.dom);
    }
  }
  measureSoon() {
    this.measureTimeout < 0 && (this.measureTimeout = setTimeout(() => {
      this.measureTimeout = -1, this.maybeMeasure();
    }, 50));
  }
  update(n) {
    n.transactions.length && (this.lastTransaction = Date.now());
    let e = this.manager.update(n, this.above);
    e && this.observeIntersection();
    let t = e || n.geometryChanged, i = n.state.facet(Fs);
    if (i.position != this.position && !this.madeAbsolute) {
      this.position = i.position;
      for (let s of this.manager.tooltipViews)
        s.dom.style.position = this.position;
      t = !0;
    }
    if (i.parent != this.parent) {
      this.parent && this.container.remove(), this.parent = i.parent, this.createContainer();
      for (let s of this.manager.tooltipViews)
        this.container.appendChild(s.dom);
      t = !0;
    } else this.parent && this.view.themeClasses != this.classes && (this.classes = this.container.className = this.view.themeClasses);
    t && this.maybeMeasure();
  }
  createTooltip(n, e) {
    let t = n.create(this.view), i = e ? e.dom : null;
    if (t.dom.classList.add("cm-tooltip"), n.arrow && !t.dom.querySelector(".cm-tooltip > .cm-tooltip-arrow")) {
      let s = document.createElement("div");
      s.className = "cm-tooltip-arrow", t.dom.appendChild(s);
    }
    return t.dom.style.position = this.position, t.dom.style.top = vn, t.dom.style.left = "0px", this.container.insertBefore(t.dom, i), t.mount && t.mount(this.view), this.resizeObserver && this.resizeObserver.observe(t.dom), t;
  }
  destroy() {
    var n, e, t;
    this.view.win.removeEventListener("resize", this.measureSoon);
    for (let i of this.manager.tooltipViews)
      i.dom.remove(), (n = i.destroy) === null || n === void 0 || n.call(i);
    this.parent && this.container.remove(), (e = this.resizeObserver) === null || e === void 0 || e.disconnect(), (t = this.intersectionObserver) === null || t === void 0 || t.disconnect(), clearTimeout(this.measureTimeout);
  }
  readMeasure() {
    let n = 1, e = 1, t = !1;
    if (this.position == "fixed" && this.manager.tooltipViews.length) {
      let { dom: r } = this.manager.tooltipViews[0];
      if (D.safari) {
        let o = r.getBoundingClientRect();
        t = Math.abs(o.top + 1e4) > 1 || Math.abs(o.left) > 1;
      } else
        t = !!r.offsetParent && r.offsetParent != this.container.ownerDocument.body;
    }
    if (t || this.position == "absolute")
      if (this.parent) {
        let r = this.parent.getBoundingClientRect();
        r.width && r.height && (n = r.width / this.parent.offsetWidth, e = r.height / this.parent.offsetHeight);
      } else
        ({ scaleX: n, scaleY: e } = this.view.viewState);
    let i = this.view.scrollDOM.getBoundingClientRect(), s = fo(this.view);
    return {
      visible: {
        left: i.left + s.left,
        top: i.top + s.top,
        right: i.right - s.right,
        bottom: i.bottom - s.bottom
      },
      parent: this.parent ? this.container.getBoundingClientRect() : this.view.dom.getBoundingClientRect(),
      pos: this.manager.tooltips.map((r, o) => {
        let l = this.manager.tooltipViews[o];
        return l.getCoords ? l.getCoords(r.pos) : this.view.coordsAtPos(r.pos);
      }),
      size: this.manager.tooltipViews.map(({ dom: r }) => r.getBoundingClientRect()),
      space: this.view.state.facet(Fs).tooltipSpace(this.view),
      scaleX: n,
      scaleY: e,
      makeAbsolute: t
    };
  }
  writeMeasure(n) {
    var e;
    if (n.makeAbsolute) {
      this.madeAbsolute = !0, this.position = "absolute";
      for (let l of this.manager.tooltipViews)
        l.dom.style.position = "absolute";
    }
    let { visible: t, space: i, scaleX: s, scaleY: r } = n, o = [];
    for (let l = 0; l < this.manager.tooltips.length; l++) {
      let a = this.manager.tooltips[l], h = this.manager.tooltipViews[l], { dom: c } = h, f = n.pos[l], u = n.size[l];
      if (!f || a.clip !== !1 && (f.bottom <= Math.max(t.top, i.top) || f.top >= Math.min(t.bottom, i.bottom) || f.right < Math.max(t.left, i.left) - 0.1 || f.left > Math.min(t.right, i.right) + 0.1)) {
        c.style.top = vn;
        continue;
      }
      let d = a.arrow ? h.dom.querySelector(".cm-tooltip-arrow") : null, p = d ? 7 : 0, m = u.right - u.left, g = (e = El.get(h)) !== null && e !== void 0 ? e : u.bottom - u.top, y = h.offset || Tp, w = this.view.textDirection == _.LTR, v = u.width > i.right - i.left ? w ? i.left : i.right - u.width : w ? Math.max(i.left, Math.min(f.left - (d ? 14 : 0) + y.x, i.right - m)) : Math.min(Math.max(i.left, f.left - m + (d ? 14 : 0) - y.x), i.right - m), O = this.above[l];
      !a.strictSide && (O ? f.top - g - p - y.y < i.top : f.bottom + g + p + y.y > i.bottom) && O == i.bottom - f.bottom > f.top - i.top && (O = this.above[l] = !O);
      let k = (O ? f.top - i.top : i.bottom - f.bottom) - p;
      if (k < g && h.resize !== !1) {
        if (k < this.view.defaultLineHeight) {
          c.style.top = vn;
          continue;
        }
        El.set(h, g), c.style.height = (g = k) / r + "px";
      } else c.style.height && (c.style.height = "");
      let S = O ? f.top - g - p - y.y : f.bottom + p + y.y, C = v + m;
      if (h.overlap !== !0)
        for (let E of o)
          E.left < C && E.right > v && E.top < S + g && E.bottom > S && (S = O ? E.top - g - 2 - p : E.bottom + p + 2);
      if (this.position == "absolute" ? (c.style.top = (S - n.parent.top) / r + "px", Rl(c, (v - n.parent.left) / s)) : (c.style.top = S / r + "px", Rl(c, v / s)), d) {
        let E = f.left + (w ? y.x : -y.x) - (v + 14 - 7);
        d.style.left = E / s + "px";
      }
      h.overlap !== !0 && o.push({ left: v, top: S, right: C, bottom: S + g }), c.classList.toggle("cm-tooltip-above", O), c.classList.toggle("cm-tooltip-below", !O), h.positioned && h.positioned(n.space);
    }
  }
  maybeMeasure() {
    if (this.manager.tooltips.length && (this.view.inView && this.view.requestMeasure(this.measureReq), this.inView != this.view.inView && (this.inView = this.view.inView, !this.inView)))
      for (let n of this.manager.tooltipViews)
        n.dom.style.top = vn;
  }
}, {
  eventObservers: {
    scroll() {
      this.maybeMeasure();
    }
  }
});
function Rl(n, e) {
  let t = parseInt(n.style.left, 10);
  (isNaN(t) || Math.abs(e - t) > 1) && (n.style.left = e + "px");
}
const Mp = /* @__PURE__ */ M.baseTheme({
  ".cm-tooltip": {
    zIndex: 500,
    boxSizing: "border-box"
  },
  "&light .cm-tooltip": {
    border: "1px solid #bbb",
    backgroundColor: "#f5f5f5"
  },
  "&light .cm-tooltip-section:not(:first-child)": {
    borderTop: "1px solid #bbb"
  },
  "&dark .cm-tooltip": {
    backgroundColor: "#333338",
    color: "white"
  },
  ".cm-tooltip-arrow": {
    height: "7px",
    width: "14px",
    position: "absolute",
    zIndex: -1,
    overflow: "hidden",
    "&:before, &:after": {
      content: "''",
      position: "absolute",
      width: 0,
      height: 0,
      borderLeft: "7px solid transparent",
      borderRight: "7px solid transparent"
    },
    ".cm-tooltip-above &": {
      bottom: "-7px",
      "&:before": {
        borderTop: "7px solid #bbb"
      },
      "&:after": {
        borderTop: "7px solid #f5f5f5",
        bottom: "1px"
      }
    },
    ".cm-tooltip-below &": {
      top: "-7px",
      "&:before": {
        borderBottom: "7px solid #bbb"
      },
      "&:after": {
        borderBottom: "7px solid #f5f5f5",
        top: "1px"
      }
    }
  },
  "&dark .cm-tooltip .cm-tooltip-arrow": {
    "&:before": {
      borderTopColor: "#333338",
      borderBottomColor: "#333338"
    },
    "&:after": {
      borderTopColor: "transparent",
      borderBottomColor: "transparent"
    }
  }
}), Tp = { x: 0, y: 0 }, yo = /* @__PURE__ */ T.define({
  enables: [go, Mp]
}), Xn = /* @__PURE__ */ T.define({
  combine: (n) => n.reduce((e, t) => e.concat(t), [])
});
class ys {
  // Needs to be static so that host tooltip instances always match
  static create(e) {
    return new ys(e);
  }
  constructor(e) {
    this.view = e, this.mounted = !1, this.dom = document.createElement("div"), this.dom.classList.add("cm-tooltip-hover"), this.manager = new Yh(e, Xn, (t, i) => this.createHostedView(t, i), (t) => t.dom.remove());
  }
  createHostedView(e, t) {
    let i = e.create(this.view);
    return i.dom.classList.add("cm-tooltip-section"), this.dom.insertBefore(i.dom, t ? t.dom.nextSibling : this.dom.firstChild), this.mounted && i.mount && i.mount(this.view), i;
  }
  mount(e) {
    for (let t of this.manager.tooltipViews)
      t.mount && t.mount(e);
    this.mounted = !0;
  }
  positioned(e) {
    for (let t of this.manager.tooltipViews)
      t.positioned && t.positioned(e);
  }
  update(e) {
    this.manager.update(e);
  }
  destroy() {
    var e;
    for (let t of this.manager.tooltipViews)
      (e = t.destroy) === null || e === void 0 || e.call(t);
  }
  passProp(e) {
    let t;
    for (let i of this.manager.tooltipViews) {
      let s = i[e];
      if (s !== void 0) {
        if (t === void 0)
          t = s;
        else if (t !== s)
          return;
      }
    }
    return t;
  }
  get offset() {
    return this.passProp("offset");
  }
  get getCoords() {
    return this.passProp("getCoords");
  }
  get overlap() {
    return this.passProp("overlap");
  }
  get resize() {
    return this.passProp("resize");
  }
}
const Dp = /* @__PURE__ */ yo.compute([Xn], (n) => {
  let e = n.facet(Xn);
  return e.length === 0 ? null : {
    pos: Math.min(...e.map((t) => t.pos)),
    end: Math.max(...e.map((t) => {
      var i;
      return (i = t.end) !== null && i !== void 0 ? i : t.pos;
    })),
    create: ys.create,
    above: e[0].above,
    arrow: e.some((t) => t.arrow)
  };
}), Jh = /* @__PURE__ */ T.define();
class Op {
  constructor(e, t, i, s, r, o) {
    this.view = e, this.source = t, this.field = i, this.locked = s, this.setHover = r, this.hoverTime = o, this.hoverTimeout = -1, this.restartTimeout = -1, this.pending = null, this.lastMove = { x: 0, y: 0, target: e.dom, time: 0 }, this.checkHover = this.checkHover.bind(this), e.dom.addEventListener("mouseleave", this.mouseleave = this.mouseleave.bind(this)), e.dom.addEventListener("mousemove", this.mousemove = this.mousemove.bind(this));
  }
  update(e) {
    this.pending && (this.pending = null, clearTimeout(this.restartTimeout), this.restartTimeout = setTimeout(() => this.startHover(), 20));
  }
  get active() {
    return this.view.state.field(this.field);
  }
  checkHover() {
    if (this.hoverTimeout = -1, this.active.length)
      return;
    let e = Date.now() - this.lastMove.time;
    e < this.hoverTime ? this.hoverTimeout = setTimeout(this.checkHover, this.hoverTime - e) : this.startHover();
  }
  startHover() {
    clearTimeout(this.restartTimeout);
    let { view: e, lastMove: t } = this, i = e.docView.tile.nearest(t.target);
    if (!i)
      return;
    let s, r = 1;
    if (i.isWidget())
      s = i.posAtStart;
    else {
      if (s = e.posAtCoords(t), s == null)
        return;
      let o = e.coordsAtPos(s);
      if (!o || t.y < o.top || t.y > o.bottom || t.x < o.left - e.defaultCharacterWidth || t.x > o.right + e.defaultCharacterWidth)
        return;
      let l = e.bidiSpans(e.state.doc.lineAt(s)).find((h) => h.from <= s && h.to >= s), a = l && l.dir == _.RTL ? -1 : 1;
      r = t.x < o.left ? -a : a;
    }
    this.activateHover(e, s, r);
  }
  activateHover(e, t, i, s) {
    let r = this.source(e, t, i), o = (l) => {
      if (l && !(Array.isArray(l) && !l.length)) {
        let a = Array.isArray(l) ? l : [l];
        s && this.locked.set(a, s), e.dispatch({ effects: this.setHover.of(a) });
      }
    };
    if (r && "then" in r) {
      let l = this.pending = { pos: t };
      r.then((a) => {
        this.pending == l && (this.pending = null, o(a));
      }, (a) => Ae(e.state, a, "hover tooltip"));
    } else
      o(r);
  }
  get tooltip() {
    let e = this.view.plugin(go), t = e ? e.manager.tooltips.findIndex((i) => i.create == ys.create) : -1;
    return t > -1 ? e.manager.tooltipViews[t] : null;
  }
  mousemove(e) {
    var t, i;
    this.lastMove = { x: e.clientX, y: e.clientY, target: e.target, time: Date.now() }, this.hoverTimeout < 0 && (this.hoverTimeout = setTimeout(this.checkHover, this.hoverTime));
    let { active: s, tooltip: r } = this;
    if (s.length && !this.locked.has(s) && r && !Bp(r.dom, e) || this.pending) {
      let { pos: o } = s[0] || this.pending, l = (i = (t = s[0]) === null || t === void 0 ? void 0 : t.end) !== null && i !== void 0 ? i : o;
      (o == l ? this.view.posAtCoords(this.lastMove) != o : !Lp(this.view, o, l, e.clientX, e.clientY)) && (this.view.dispatch({ effects: this.setHover.of([]) }), this.pending = null);
    }
  }
  mouseleave(e) {
    clearTimeout(this.hoverTimeout), this.hoverTimeout = -1;
    let { active: t } = this;
    if (t.length && !this.locked.has(t)) {
      let { tooltip: i } = this;
      i && i.dom.contains(e.relatedTarget) ? this.watchTooltipLeave(i.dom) : this.view.dispatch({ effects: this.setHover.of([]) });
    }
  }
  watchTooltipLeave(e) {
    let t = (i) => {
      e.removeEventListener("mouseleave", t);
      let { active: s } = this;
      s.length && !this.locked.has(s) && !this.view.dom.contains(i.relatedTarget) && this.view.dispatch({ effects: this.setHover.of([]) });
    };
    e.addEventListener("mouseleave", t);
  }
  destroy() {
    clearTimeout(this.hoverTimeout), clearTimeout(this.restartTimeout), this.view.dom.removeEventListener("mouseleave", this.mouseleave), this.view.dom.removeEventListener("mousemove", this.mousemove);
  }
}
const kn = 4;
function Bp(n, e) {
  let { left: t, right: i, top: s, bottom: r } = n.getBoundingClientRect(), o;
  if (o = n.querySelector(".cm-tooltip-arrow")) {
    let l = o.getBoundingClientRect();
    s = Math.min(l.top, s), r = Math.max(l.bottom, r);
  }
  return e.clientX >= t - kn && e.clientX <= i + kn && e.clientY >= s - kn && e.clientY <= r + kn;
}
function Lp(n, e, t, i, s, r) {
  let o = n.scrollDOM.getBoundingClientRect(), l = n.documentTop + n.documentPadding.top + n.contentHeight;
  if (o.left > i || o.right < i || o.top > s || Math.min(o.bottom, l) < s)
    return !1;
  let a = n.posAtCoords({ x: i, y: s }, !1);
  return a >= e && a <= t;
}
function Ep(n, e = {}) {
  let t = R.define(), i = /* @__PURE__ */ new WeakMap(), s = me.define({
    create() {
      return [];
    },
    update(o, l) {
      let a = i.get(o);
      if (o.length && (e.hideOnChange && (l.docChanged || l.selection) ? o = [] : a && a(l) ? o = [] : e.hideOn && (o = o.filter((h) => !e.hideOn(l, h)))), l.docChanged && o.length) {
        let h = [];
        for (let c of o) {
          let f = l.changes.mapPos(c.pos, -1, fe.TrackDel);
          if (f != null) {
            let u = Object.assign(/* @__PURE__ */ Object.create(null), c);
            u.pos = f, u.end != null && (u.end = l.changes.mapPos(u.end)), h.push(u);
          }
        }
        o = h;
      }
      for (let h of l.effects)
        h.is(t) && (o = h.value, a = void 0), (h.is(Pp) && !h.value || h.value == s) && (o = []);
      return o.length && a && i.set(o, a), o;
    },
    provide: (o) => Xn.from(o)
  });
  const r = Q.define((o) => new Op(
    o,
    n,
    s,
    i,
    t,
    e.hoverTime || 300
    /* Hover.Time */
  ));
  return {
    active: s,
    extension: [
      s,
      r,
      Jh.of(r),
      Dp
    ]
  };
}
function Rp(n, e, t, i = {}) {
  var s;
  let r = n.state.facet(Jh).map((o) => n.plugin(o)).filter((o) => !!o);
  if (i.tooltip && i.tooltip.active) {
    let o = r.find((l) => l.field == i.tooltip.active);
    o && (r = [o]);
  }
  for (let o of r)
    o.activateHover(n, e, t, (s = i.until) !== null && s !== void 0 ? s : (() => !1));
}
function Xh(n, e) {
  let t = n.plugin(go);
  if (!t)
    return null;
  let i = t.manager.tooltips.indexOf(e);
  return i < 0 ? null : t.manager.tooltipViews[i];
}
const Pp = /* @__PURE__ */ R.define(), Pl = /* @__PURE__ */ T.define({
  combine(n) {
    let e, t;
    for (let i of n)
      e = e || i.topContainer, t = t || i.bottomContainer;
    return { topContainer: e, bottomContainer: t };
  }
});
function bo(n, e) {
  let t = n.plugin(Qh), i = t ? t.specs.indexOf(e) : -1;
  return i > -1 ? t.panels[i] : null;
}
const Qh = /* @__PURE__ */ Q.fromClass(class {
  constructor(n) {
    this.input = n.state.facet(qi), this.specs = this.input.filter((t) => t), this.panels = this.specs.map((t) => t(n));
    let e = n.state.facet(Pl);
    this.top = new Sn(n, !0, e.topContainer), this.bottom = new Sn(n, !1, e.bottomContainer), this.top.sync(this.panels.filter((t) => t.top)), this.bottom.sync(this.panels.filter((t) => !t.top));
    for (let t of this.panels)
      t.dom.classList.add("cm-panel"), t.mount && t.mount();
  }
  update(n) {
    let e = n.state.facet(Pl);
    this.top.container != e.topContainer && (this.top.sync([]), this.top = new Sn(n.view, !0, e.topContainer)), this.bottom.container != e.bottomContainer && (this.bottom.sync([]), this.bottom = new Sn(n.view, !1, e.bottomContainer)), this.top.syncClasses(), this.bottom.syncClasses();
    let t = n.state.facet(qi);
    if (t != this.input) {
      let i = t.filter((a) => a), s = [], r = [], o = [], l = [];
      for (let a of i) {
        let h = this.specs.indexOf(a), c;
        h < 0 ? (c = a(n.view), l.push(c)) : (c = this.panels[h], c.update && c.update(n)), s.push(c), (c.top ? r : o).push(c);
      }
      this.specs = i, this.panels = s, this.top.sync(r), this.bottom.sync(o);
      for (let a of l)
        a.dom.classList.add("cm-panel"), a.mount && a.mount();
    } else
      for (let i of this.panels)
        i.update && i.update(n);
  }
  destroy() {
    this.top.sync([]), this.bottom.sync([]);
  }
}, {
  provide: (n) => M.scrollMargins.of((e) => {
    let t = e.plugin(n);
    return t && { top: t.top.scrollMargin(), bottom: t.bottom.scrollMargin() };
  })
});
class Sn {
  constructor(e, t, i) {
    this.view = e, this.top = t, this.container = i, this.dom = void 0, this.classes = "", this.panels = [], this.syncClasses();
  }
  sync(e) {
    for (let t of this.panels)
      t.destroy && e.indexOf(t) < 0 && t.destroy();
    this.panels = e, this.syncDOM();
  }
  syncDOM() {
    if (this.panels.length == 0) {
      this.dom && (this.dom.remove(), this.dom = void 0);
      return;
    }
    if (!this.dom) {
      this.dom = document.createElement("div"), this.dom.className = this.top ? "cm-panels cm-panels-top" : "cm-panels cm-panels-bottom", this.dom.style[this.top ? "top" : "bottom"] = "0";
      let t = this.container || this.view.dom;
      t.insertBefore(this.dom, this.top ? t.firstChild : null);
    }
    let e = this.dom.firstChild;
    for (let t of this.panels)
      if (t.dom.parentNode == this.dom) {
        for (; e != t.dom; )
          e = Il(e);
        e = e.nextSibling;
      } else
        this.dom.insertBefore(t.dom, e);
    for (; e; )
      e = Il(e);
  }
  scrollMargin() {
    return !this.dom || this.container ? 0 : Math.max(0, this.top ? this.dom.getBoundingClientRect().bottom - Math.max(0, this.view.scrollDOM.getBoundingClientRect().top) : Math.min(innerHeight, this.view.scrollDOM.getBoundingClientRect().bottom) - this.dom.getBoundingClientRect().top);
  }
  syncClasses() {
    if (!(!this.container || this.classes == this.view.themeClasses)) {
      for (let e of this.classes.split(" "))
        e && this.container.classList.remove(e);
      for (let e of (this.classes = this.view.themeClasses).split(" "))
        e && this.container.classList.add(e);
    }
  }
}
function Il(n) {
  let e = n.nextSibling;
  return n.remove(), e;
}
const qi = /* @__PURE__ */ T.define({
  enables: Qh
});
function Ip(n, e) {
  let t, i = new Promise((o) => t = o), s = (o) => Np(o, e, t);
  n.state.field(Vs, !1) ? n.dispatch({ effects: Zh.of(s) }) : n.dispatch({ effects: R.appendConfig.of(Vs.init(() => [s])) });
  let r = ec.of(s);
  return { close: r, result: i.then((o) => ((n.win.queueMicrotask || ((a) => n.win.setTimeout(a, 10)))(() => {
    n.state.field(Vs).indexOf(s) > -1 && n.dispatch({ effects: r });
  }), o)) };
}
const Vs = /* @__PURE__ */ me.define({
  create() {
    return [];
  },
  update(n, e) {
    for (let t of e.effects)
      t.is(Zh) ? n = [t.value].concat(n) : t.is(ec) && (n = n.filter((i) => i != t.value));
    return n;
  },
  provide: (n) => qi.computeN([n], (e) => e.field(n))
}), Zh = /* @__PURE__ */ R.define(), ec = /* @__PURE__ */ R.define();
function Np(n, e, t) {
  let i = e.content ? e.content(n, () => o(null)) : null;
  if (!i) {
    if (i = K("form"), e.input) {
      let l = K("input", e.input);
      /^(text|password|number|email|tel|url)$/.test(l.type) && l.classList.add("cm-textfield"), l.name || (l.name = "input"), i.appendChild(K("label", (e.label || "") + ": ", l));
    } else
      i.appendChild(document.createTextNode(e.label || ""));
    i.appendChild(document.createTextNode(" ")), i.appendChild(K("button", { class: "cm-button", type: "submit" }, e.submitLabel || "OK"));
  }
  let s = i.nodeName == "FORM" ? [i] : i.querySelectorAll("form");
  for (let l = 0; l < s.length; l++) {
    let a = s[l];
    a.addEventListener("keydown", (h) => {
      h.keyCode == 27 ? (h.preventDefault(), o(null)) : h.keyCode == 13 && (h.preventDefault(), o(a));
    }), a.addEventListener("submit", (h) => {
      h.preventDefault(), o(a);
    });
  }
  let r = K("div", i, K("button", {
    onclick: () => o(null),
    "aria-label": n.state.phrase("close"),
    class: "cm-dialog-close",
    type: "button"
  }, ["×"]));
  e.class && (r.className = e.class), r.classList.add("cm-dialog");
  function o(l) {
    r.contains(r.ownerDocument.activeElement) && n.focus(), t(l);
  }
  return {
    dom: r,
    top: e.top,
    mount: () => {
      if (e.focus) {
        let l;
        typeof e.focus == "string" ? l = i.querySelector(e.focus) : l = i.querySelector("input") || i.querySelector("button"), l && "select" in l ? l.select() : l && "focus" in l && l.focus();
      }
    }
  };
}
class dt extends wt {
  /**
  @internal
  */
  compare(e) {
    return this == e || this.constructor == e.constructor && this.eq(e);
  }
  /**
  Compare this marker to another marker of the same type.
  */
  eq(e) {
    return !1;
  }
  /**
  Called if the marker has a `toDOM` method and its representation
  was removed from a gutter.
  */
  destroy(e) {
  }
}
dt.prototype.elementClass = "";
dt.prototype.toDOM = void 0;
dt.prototype.mapMode = fe.TrackBefore;
dt.prototype.startSide = dt.prototype.endSide = -1;
dt.prototype.point = !0;
const Hn = /* @__PURE__ */ T.define(), Wp = /* @__PURE__ */ T.define(), Hp = {
  class: "",
  renderEmptyElements: !1,
  elementStyle: "",
  markers: () => W.empty,
  lineMarker: () => null,
  widgetMarker: () => null,
  lineMarkerChange: null,
  initialSpacer: null,
  updateSpacer: null,
  domEventHandlers: {},
  side: "before"
}, Ri = /* @__PURE__ */ T.define();
function Fp(n) {
  return [tc(), Ri.of({ ...Hp, ...n })];
}
const Nl = /* @__PURE__ */ T.define({
  combine: (n) => n.some((e) => e)
});
function tc(n) {
  return [
    Vp
  ];
}
const Vp = /* @__PURE__ */ Q.fromClass(class {
  constructor(n) {
    this.view = n, this.domAfter = null, this.prevViewport = n.viewport, this.dom = document.createElement("div"), this.dom.className = "cm-gutters cm-gutters-before", this.dom.setAttribute("aria-hidden", "true"), this.dom.style.minHeight = this.view.contentHeight / this.view.scaleY + "px", this.gutters = n.state.facet(Ri).map((e) => new Hl(n, e)), this.fixed = !n.state.facet(Nl);
    for (let e of this.gutters)
      e.config.side == "after" ? this.getDOMAfter().appendChild(e.dom) : this.dom.appendChild(e.dom);
    this.fixed && (this.dom.style.position = "sticky"), this.syncGutters(!1), n.scrollDOM.insertBefore(this.dom, n.contentDOM);
  }
  getDOMAfter() {
    return this.domAfter || (this.domAfter = document.createElement("div"), this.domAfter.className = "cm-gutters cm-gutters-after", this.domAfter.setAttribute("aria-hidden", "true"), this.domAfter.style.minHeight = this.view.contentHeight / this.view.scaleY + "px", this.domAfter.style.position = this.fixed ? "sticky" : "", this.view.scrollDOM.appendChild(this.domAfter)), this.domAfter;
  }
  update(n) {
    if (this.updateGutters(n)) {
      let e = this.prevViewport, t = n.view.viewport, i = Math.min(e.to, t.to) - Math.max(e.from, t.from);
      this.syncGutters(i < (t.to - t.from) * 0.8);
    }
    if (n.geometryChanged) {
      let e = this.view.contentHeight / this.view.scaleY + "px";
      this.dom.style.minHeight = e, this.domAfter && (this.domAfter.style.minHeight = e);
    }
    this.view.state.facet(Nl) != !this.fixed && (this.fixed = !this.fixed, this.dom.style.position = this.fixed ? "sticky" : "", this.domAfter && (this.domAfter.style.position = this.fixed ? "sticky" : "")), this.prevViewport = n.view.viewport;
  }
  syncGutters(n) {
    let e = this.dom.nextSibling;
    n && (this.dom.remove(), this.domAfter && this.domAfter.remove());
    let t = W.iter(this.view.state.facet(Hn), this.view.viewport.from), i = [], s = this.gutters.map((r) => new zp(r, this.view.viewport, -this.view.documentPadding.top));
    for (let r of this.view.viewportLineBlocks)
      if (i.length && (i = []), Array.isArray(r.type)) {
        let o = !0;
        for (let l of r.type)
          if (l.type == de.Text && o) {
            Fr(t, i, l.from);
            for (let a of s)
              a.line(this.view, l, i);
            o = !1;
          } else if (l.widget)
            for (let a of s)
              a.widget(this.view, l);
      } else if (r.type == de.Text) {
        Fr(t, i, r.from);
        for (let o of s)
          o.line(this.view, r, i);
      } else if (r.widget)
        for (let o of s)
          o.widget(this.view, r);
    for (let r of s)
      r.finish();
    n && (this.view.scrollDOM.insertBefore(this.dom, e), this.domAfter && this.view.scrollDOM.appendChild(this.domAfter));
  }
  updateGutters(n) {
    let e = n.startState.facet(Ri), t = n.state.facet(Ri), i = n.docChanged || n.heightChanged || n.viewportChanged || !W.eq(n.startState.facet(Hn), n.state.facet(Hn), n.view.viewport.from, n.view.viewport.to);
    if (e == t)
      for (let s of this.gutters)
        s.update(n) && (i = !0);
    else {
      i = !0;
      let s = [];
      for (let r of t) {
        let o = e.indexOf(r);
        o < 0 ? s.push(new Hl(this.view, r)) : (this.gutters[o].update(n), s.push(this.gutters[o]));
      }
      for (let r of this.gutters)
        r.dom.remove(), s.indexOf(r) < 0 && r.destroy();
      for (let r of s)
        r.config.side == "after" ? this.getDOMAfter().appendChild(r.dom) : this.dom.appendChild(r.dom);
      this.gutters = s;
    }
    return i;
  }
  destroy() {
    for (let n of this.gutters)
      n.destroy();
    this.dom.remove(), this.domAfter && this.domAfter.remove();
  }
}, {
  provide: (n) => M.scrollMargins.of((e) => {
    let t = e.plugin(n);
    if (!t || t.gutters.length == 0 || !t.fixed)
      return null;
    let i = t.dom.offsetWidth * e.scaleX, s = t.domAfter ? t.domAfter.offsetWidth * e.scaleX : 0;
    return e.textDirection == _.LTR ? { left: i, right: s } : { right: i, left: s };
  })
});
function Wl(n) {
  return Array.isArray(n) ? n : [n];
}
function Fr(n, e, t) {
  for (; n.value && n.from <= t; )
    n.from == t && e.push(n.value), n.next();
}
class zp {
  constructor(e, t, i) {
    this.gutter = e, this.height = i, this.i = 0, this.cursor = W.iter(e.markers, t.from);
  }
  addElement(e, t, i) {
    let { gutter: s } = this, r = (t.top - this.height) / e.scaleY, o = t.height / e.scaleY;
    if (this.i == s.elements.length) {
      let l = new ic(e, o, r, i);
      s.elements.push(l), s.dom.appendChild(l.dom);
    } else
      s.elements[this.i].update(e, o, r, i);
    this.height = t.bottom, this.i++;
  }
  line(e, t, i) {
    let s = [];
    Fr(this.cursor, s, t.from), i.length && (s = s.concat(i));
    let r = this.gutter.config.lineMarker(e, t, s);
    r && s.unshift(r);
    let o = this.gutter;
    s.length == 0 && !o.config.renderEmptyElements || this.addElement(e, t, s);
  }
  widget(e, t) {
    let i = this.gutter.config.widgetMarker(e, t.widget, t), s = i ? [i] : null;
    for (let r of e.state.facet(Wp)) {
      let o = r(e, t.widget, t);
      o && (s || (s = [])).push(o);
    }
    s && this.addElement(e, t, s);
  }
  finish() {
    let e = this.gutter;
    for (; e.elements.length > this.i; ) {
      let t = e.elements.pop();
      e.dom.removeChild(t.dom), t.destroy();
    }
  }
}
class Hl {
  constructor(e, t) {
    this.view = e, this.config = t, this.elements = [], this.spacer = null, this.dom = document.createElement("div"), this.dom.className = "cm-gutter" + (this.config.class ? " " + this.config.class : "");
    for (let i in t.domEventHandlers)
      this.dom.addEventListener(i, (s) => {
        let r = s.target, o;
        if (r != this.dom && this.dom.contains(r)) {
          for (; r.parentNode != this.dom; )
            r = r.parentNode;
          let a = r.getBoundingClientRect();
          o = (a.top + a.bottom) / 2;
        } else
          o = s.clientY;
        let l = e.lineBlockAtHeight(o - e.documentTop);
        t.domEventHandlers[i](e, l, s) && s.preventDefault();
      });
    this.markers = Wl(t.markers(e)), t.initialSpacer && (this.spacer = new ic(e, 0, 0, [t.initialSpacer(e)]), this.dom.appendChild(this.spacer.dom), this.spacer.dom.style.cssText += "visibility: hidden; pointer-events: none");
  }
  update(e) {
    let t = this.markers;
    if (this.markers = Wl(this.config.markers(e.view)), this.spacer && this.config.updateSpacer) {
      let s = this.config.updateSpacer(this.spacer.markers[0], e);
      s != this.spacer.markers[0] && this.spacer.update(e.view, 0, 0, [s]);
    }
    let i = e.view.viewport;
    return !W.eq(this.markers, t, i.from, i.to) || (this.config.lineMarkerChange ? this.config.lineMarkerChange(e) : !1);
  }
  destroy() {
    for (let e of this.elements)
      e.destroy();
  }
}
class ic {
  constructor(e, t, i, s) {
    this.height = -1, this.above = 0, this.markers = [], this.dom = document.createElement("div"), this.dom.className = "cm-gutterElement", this.update(e, t, i, s);
  }
  update(e, t, i, s) {
    this.height != t && (this.height = t, this.dom.style.height = t + "px"), this.above != i && (this.dom.style.marginTop = (this.above = i) ? i + "px" : ""), qp(this.markers, s) || this.setMarkers(e, s);
  }
  setMarkers(e, t) {
    let i = "cm-gutterElement", s = this.dom.firstChild;
    for (let r = 0, o = 0; ; ) {
      let l = o, a = r < t.length ? t[r++] : null, h = !1;
      if (a) {
        let c = a.elementClass;
        c && (i += " " + c);
        for (let f = o; f < this.markers.length; f++)
          if (this.markers[f].compare(a)) {
            l = f, h = !0;
            break;
          }
      } else
        l = this.markers.length;
      for (; o < l; ) {
        let c = this.markers[o++];
        if (c.toDOM) {
          c.destroy(s);
          let f = s.nextSibling;
          s.remove(), s = f;
        }
      }
      if (!a)
        break;
      a.toDOM && (h ? s = s.nextSibling : this.dom.insertBefore(a.toDOM(e), s)), h && o++;
    }
    this.dom.className = i, this.markers = t;
  }
  destroy() {
    this.setMarkers(null, []);
  }
}
function qp(n, e) {
  if (n.length != e.length)
    return !1;
  for (let t = 0; t < n.length; t++)
    if (!n[t].compare(e[t]))
      return !1;
  return !0;
}
const $p = /* @__PURE__ */ T.define(), Kp = /* @__PURE__ */ T.define(), Qt = /* @__PURE__ */ T.define({
  combine(n) {
    return at(n, { formatNumber: String, domEventHandlers: {} }, {
      domEventHandlers(e, t) {
        let i = Object.assign({}, e);
        for (let s in t) {
          let r = i[s], o = t[s];
          i[s] = r ? (l, a, h) => r(l, a, h) || o(l, a, h) : o;
        }
        return i;
      }
    });
  }
});
class zs extends dt {
  constructor(e) {
    super(), this.number = e;
  }
  eq(e) {
    return this.number == e.number;
  }
  toDOM() {
    return document.createTextNode(this.number);
  }
}
function qs(n, e) {
  return n.state.facet(Qt).formatNumber(e, n.state);
}
const jp = /* @__PURE__ */ Ri.compute([Qt], (n) => ({
  class: "cm-lineNumbers",
  renderEmptyElements: !1,
  markers(e) {
    return e.state.facet($p);
  },
  lineMarker(e, t, i) {
    return i.some((s) => s.toDOM) ? null : new zs(qs(e, e.state.doc.lineAt(t.from).number));
  },
  widgetMarker: (e, t, i) => {
    for (let s of e.state.facet(Kp)) {
      let r = s(e, t, i);
      if (r)
        return r;
    }
    return null;
  },
  lineMarkerChange: (e) => e.startState.facet(Qt) != e.state.facet(Qt),
  initialSpacer(e) {
    return new zs(qs(e, Fl(e.state.doc.lines)));
  },
  updateSpacer(e, t) {
    let i = qs(t.view, Fl(t.view.state.doc.lines));
    return i == e.number ? e : new zs(i);
  },
  domEventHandlers: n.facet(Qt).domEventHandlers,
  side: "before"
}));
function Up(n = {}) {
  return [
    Qt.of(n),
    tc(),
    jp
  ];
}
function Fl(n) {
  let e = 9;
  for (; e < n; )
    e = e * 10 + 9;
  return e;
}
const _p = /* @__PURE__ */ new class extends dt {
  constructor() {
    super(...arguments), this.elementClass = "cm-activeLineGutter";
  }
}(), Gp = /* @__PURE__ */ Hn.compute(["selection"], (n) => {
  let e = [], t = -1;
  for (let i of n.selection.ranges) {
    let s = n.doc.lineAt(i.head).from;
    s > t && (t = s, e.push(_p.range(s)));
  }
  return W.of(e);
});
function Yp() {
  return Gp;
}
const Jp = 1024;
let Xp = 0;
class $s {
  constructor(e, t) {
    this.from = e, this.to = t;
  }
}
class V {
  /**
  Create a new node prop type.
  */
  constructor(e = {}) {
    this.id = Xp++, this.perNode = !!e.perNode, this.deserialize = e.deserialize || (() => {
      throw new Error("This node type doesn't define a deserialize function");
    }), this.combine = e.combine || null;
  }
  /**
  This is meant to be used with
  [`NodeSet.extend`](#common.NodeSet.extend) or
  [`LRParser.configure`](#lr.ParserConfig.props) to compute
  prop values for each node type in the set. Takes a [match
  object](#common.NodeType^match) or function that returns undefined
  if the node type doesn't get this prop, and the prop's value if
  it does.
  */
  add(e) {
    if (this.perNode)
      throw new RangeError("Can't add per-node props to node types");
    return typeof e != "function" && (e = Re.match(e)), (t) => {
      let i = e(t);
      return i === void 0 ? null : [this, i];
    };
  }
}
V.closedBy = new V({ deserialize: (n) => n.split(" ") });
V.openedBy = new V({ deserialize: (n) => n.split(" ") });
V.group = new V({ deserialize: (n) => n.split(" ") });
V.isolate = new V({ deserialize: (n) => {
  if (n && n != "rtl" && n != "ltr" && n != "auto")
    throw new RangeError("Invalid value for isolate: " + n);
  return n || "auto";
} });
V.contextHash = new V({ perNode: !0 });
V.lookAhead = new V({ perNode: !0 });
V.mounted = new V({ perNode: !0 });
class Pi {
  constructor(e, t, i, s = !1) {
    this.tree = e, this.overlay = t, this.parser = i, this.bracketed = s;
  }
  /**
  @internal
  */
  static get(e) {
    return e && e.props && e.props[V.mounted.id];
  }
}
const Qp = /* @__PURE__ */ Object.create(null);
class Re {
  /**
  @internal
  */
  constructor(e, t, i, s = 0) {
    this.name = e, this.props = t, this.id = i, this.flags = s;
  }
  /**
  Define a node type.
  */
  static define(e) {
    let t = e.props && e.props.length ? /* @__PURE__ */ Object.create(null) : Qp, i = (e.top ? 1 : 0) | (e.skipped ? 2 : 0) | (e.error ? 4 : 0) | (e.name == null ? 8 : 0), s = new Re(e.name || "", t, e.id, i);
    if (e.props) {
      for (let r of e.props)
        if (Array.isArray(r) || (r = r(s)), r) {
          if (r[0].perNode)
            throw new RangeError("Can't store a per-node prop on a node type");
          t[r[0].id] = r[1];
        }
    }
    return s;
  }
  /**
  Retrieves a node prop for this type. Will return `undefined` if
  the prop isn't present on this node.
  */
  prop(e) {
    return this.props[e.id];
  }
  /**
  True when this is the top node of a grammar.
  */
  get isTop() {
    return (this.flags & 1) > 0;
  }
  /**
  True when this node is produced by a skip rule.
  */
  get isSkipped() {
    return (this.flags & 2) > 0;
  }
  /**
  Indicates whether this is an error node.
  */
  get isError() {
    return (this.flags & 4) > 0;
  }
  /**
  When true, this node type doesn't correspond to a user-declared
  named node, for example because it is used to cache repetition.
  */
  get isAnonymous() {
    return (this.flags & 8) > 0;
  }
  /**
  Returns true when this node's name or one of its
  [groups](#common.NodeProp^group) matches the given string.
  */
  is(e) {
    if (typeof e == "string") {
      if (this.name == e)
        return !0;
      let t = this.prop(V.group);
      return t ? t.indexOf(e) > -1 : !1;
    }
    return this.id == e;
  }
  /**
  Create a function from node types to arbitrary values by
  specifying an object whose property names are node or
  [group](#common.NodeProp^group) names. Often useful with
  [`NodeProp.add`](#common.NodeProp.add). You can put multiple
  names, separated by spaces, in a single property name to map
  multiple node names to a single value.
  */
  static match(e) {
    let t = /* @__PURE__ */ Object.create(null);
    for (let i in e)
      for (let s of i.split(" "))
        t[s] = e[i];
    return (i) => {
      for (let s = i.prop(V.group), r = -1; r < (s ? s.length : 0); r++) {
        let o = t[r < 0 ? i.name : s[r]];
        if (o)
          return o;
      }
    };
  }
}
Re.none = new Re(
  "",
  /* @__PURE__ */ Object.create(null),
  0,
  8
  /* NodeFlag.Anonymous */
);
class nc {
  /**
  Create a set with the given types. The `id` property of each
  type should correspond to its position within the array.
  */
  constructor(e) {
    this.types = e;
    for (let t = 0; t < e.length; t++)
      if (e[t].id != t)
        throw new RangeError("Node type ids should correspond to array positions when creating a node set");
  }
  /**
  Create a copy of this set with some node properties added. The
  arguments to this method can be created with
  [`NodeProp.add`](#common.NodeProp.add).
  */
  extend(...e) {
    let t = [];
    for (let i of this.types) {
      let s = null;
      for (let r of e) {
        let o = r(i);
        if (o) {
          s || (s = Object.assign({}, i.props));
          let l = o[1], a = o[0];
          a.combine && a.id in s && (l = a.combine(s[a.id], l)), s[a.id] = l;
        }
      }
      t.push(s ? new Re(i.name, s, i.id, i.flags) : i);
    }
    return new nc(t);
  }
}
const Cn = /* @__PURE__ */ new WeakMap(), Vl = /* @__PURE__ */ new WeakMap();
var te;
(function(n) {
  n[n.ExcludeBuffers = 1] = "ExcludeBuffers", n[n.IncludeAnonymous = 2] = "IncludeAnonymous", n[n.IgnoreMounts = 4] = "IgnoreMounts", n[n.IgnoreOverlays = 8] = "IgnoreOverlays", n[n.EnterBracketed = 16] = "EnterBracketed";
})(te || (te = {}));
class ue {
  /**
  Construct a new tree. See also [`Tree.build`](#common.Tree^build).
  */
  constructor(e, t, i, s, r) {
    if (this.type = e, this.children = t, this.positions = i, this.length = s, this.props = null, r && r.length) {
      this.props = /* @__PURE__ */ Object.create(null);
      for (let [o, l] of r)
        this.props[typeof o == "number" ? o : o.id] = l;
    }
  }
  /**
  @internal
  */
  toString() {
    let e = Pi.get(this);
    if (e && !e.overlay)
      return e.tree.toString();
    let t = "";
    for (let i of this.children) {
      let s = i.toString();
      s && (t && (t += ","), t += s);
    }
    return this.type.name ? (/\W/.test(this.type.name) && !this.type.isError ? JSON.stringify(this.type.name) : this.type.name) + (t.length ? "(" + t + ")" : "") : t;
  }
  /**
  Get a [tree cursor](#common.TreeCursor) positioned at the top of
  the tree. Mode can be used to [control](#common.IterMode) which
  nodes the cursor visits.
  */
  cursor(e = 0) {
    return new zr(this.topNode, e);
  }
  /**
  Get a [tree cursor](#common.TreeCursor) pointing into this tree
  at the given position and side (see
  [`moveTo`](#common.TreeCursor.moveTo).
  */
  cursorAt(e, t = 0, i = 0) {
    let s = Cn.get(this) || this.topNode, r = new zr(s);
    return r.moveTo(e, t), Cn.set(this, r._tree), r;
  }
  /**
  Get a [syntax node](#common.SyntaxNode) object for the top of the
  tree.
  */
  get topNode() {
    return new Te(this, 0, 0, null);
  }
  /**
  Get the [syntax node](#common.SyntaxNode) at the given position.
  If `side` is -1, this will move into nodes that end at the
  position. If 1, it'll move into nodes that start at the
  position. With 0, it'll only enter nodes that cover the position
  from both sides.
  
  Note that this will not enter
  [overlays](#common.MountedTree.overlay), and you often want
  [`resolveInner`](#common.Tree.resolveInner) instead.
  */
  resolve(e, t = 0) {
    let i = $i(Cn.get(this) || this.topNode, e, t, !1);
    return Cn.set(this, i), i;
  }
  /**
  Like [`resolve`](#common.Tree.resolve), but will enter
  [overlaid](#common.MountedTree.overlay) nodes, producing a syntax node
  pointing into the innermost overlaid tree at the given position
  (with parent links going through all parent structure, including
  the host trees).
  */
  resolveInner(e, t = 0) {
    let i = $i(Vl.get(this) || this.topNode, e, t, !0);
    return Vl.set(this, i), i;
  }
  /**
  In some situations, it can be useful to iterate through all
  nodes around a position, including those in overlays that don't
  directly cover the position. This method gives you an iterator
  that will produce all nodes, from small to big, around the given
  position.
  */
  resolveStack(e, t = 0) {
    return tm(this, e, t);
  }
  /**
  Iterate over the tree and its children, calling `enter` for any
  node that touches the `from`/`to` region (if given) before
  running over such a node's children, and `leave` (if given) when
  leaving the node. When `enter` returns `false`, that node will
  not have its children iterated over (or `leave` called).
  */
  iterate(e) {
    let { enter: t, leave: i, from: s = 0, to: r = this.length } = e, o = e.mode || 0, l = (o & te.IncludeAnonymous) > 0;
    for (let a = this.cursor(o | te.IncludeAnonymous); ; ) {
      let h = !1;
      if (a.from <= r && a.to >= s && (!l && a.type.isAnonymous || t(a) !== !1)) {
        if (a.firstChild())
          continue;
        h = !0;
      }
      for (; h && i && (l || !a.type.isAnonymous) && i(a), !a.nextSibling(); ) {
        if (!a.parent())
          return;
        h = !0;
      }
    }
  }
  /**
  Get the value of the given [node prop](#common.NodeProp) for this
  node. Works with both per-node and per-type props.
  */
  prop(e) {
    return e.perNode ? this.props ? this.props[e.id] : void 0 : this.type.prop(e);
  }
  /**
  Returns the node's [per-node props](#common.NodeProp.perNode) in a
  format that can be passed to the [`Tree`](#common.Tree)
  constructor.
  */
  get propValues() {
    let e = [];
    if (this.props)
      for (let t in this.props)
        e.push([+t, this.props[t]]);
    return e;
  }
  /**
  Balance the direct children of this tree, producing a copy of
  which may have children grouped into subtrees with type
  [`NodeType.none`](#common.NodeType^none).
  */
  balance(e = {}) {
    return this.children.length <= 8 ? this : vo(Re.none, this.children, this.positions, 0, this.children.length, 0, this.length, (t, i, s) => new ue(this.type, t, i, s, this.propValues), e.makeTree || ((t, i, s) => new ue(Re.none, t, i, s)));
  }
  /**
  Build a tree from a postfix-ordered buffer of node information,
  or a cursor over such a buffer.
  */
  static build(e) {
    return im(e);
  }
}
ue.empty = new ue(Re.none, [], [], 0);
class xo {
  constructor(e, t) {
    this.buffer = e, this.index = t;
  }
  get id() {
    return this.buffer[this.index - 4];
  }
  get start() {
    return this.buffer[this.index - 3];
  }
  get end() {
    return this.buffer[this.index - 2];
  }
  get size() {
    return this.buffer[this.index - 1];
  }
  get pos() {
    return this.index;
  }
  next() {
    this.index -= 4;
  }
  fork() {
    return new xo(this.buffer, this.index);
  }
}
class Ct {
  /**
  Create a tree buffer.
  */
  constructor(e, t, i) {
    this.buffer = e, this.length = t, this.set = i;
  }
  /**
  @internal
  */
  get type() {
    return Re.none;
  }
  /**
  @internal
  */
  toString() {
    let e = [];
    for (let t = 0; t < this.buffer.length; )
      e.push(this.childString(t)), t = this.buffer[t + 3];
    return e.join(",");
  }
  /**
  @internal
  */
  childString(e) {
    let t = this.buffer[e], i = this.buffer[e + 3], s = this.set.types[t], r = s.name;
    if (/\W/.test(r) && !s.isError && (r = JSON.stringify(r)), e += 4, i == e)
      return r;
    let o = [];
    for (; e < i; )
      o.push(this.childString(e)), e = this.buffer[e + 3];
    return r + "(" + o.join(",") + ")";
  }
  /**
  @internal
  */
  findChild(e, t, i, s, r) {
    let { buffer: o } = this, l = -1;
    for (let a = e; a != t && !(sc(r, s, o[a + 1], o[a + 2]) && (l = a, i > 0)); a = o[a + 3])
      ;
    return l;
  }
  /**
  @internal
  */
  slice(e, t, i) {
    let s = this.buffer, r = new Uint16Array(t - e), o = 0;
    for (let l = e, a = 0; l < t; ) {
      r[a++] = s[l++], r[a++] = s[l++] - i;
      let h = r[a++] = s[l++] - i;
      r[a++] = s[l++] - e, o = Math.max(o, h);
    }
    return new Ct(r, o, this.set);
  }
}
function sc(n, e, t, i) {
  switch (n) {
    case -2:
      return t < e;
    case -1:
      return i >= e && t < e;
    case 0:
      return t < e && i > e;
    case 1:
      return t <= e && i > e;
    case 2:
      return i > e;
    case 4:
      return !0;
  }
}
function $i(n, e, t, i) {
  for (var s; n.from == n.to || (t < 1 ? n.from >= e : n.from > e) || (t > -1 ? n.to <= e : n.to < e); ) {
    let o = !i && n instanceof Te && n.index < 0 ? null : n.parent;
    if (!o)
      return n;
    n = o;
  }
  let r = i ? 0 : te.IgnoreOverlays;
  if (i)
    for (let o = n, l = o.parent; l; o = l, l = o.parent)
      o instanceof Te && o.index < 0 && ((s = l.enter(e, t, r)) === null || s === void 0 ? void 0 : s.from) != o.from && (n = l);
  for (; ; ) {
    let o = n.enter(e, t, r);
    if (!o)
      return n;
    n = o;
  }
}
class rc {
  cursor(e = 0) {
    return new zr(this, e);
  }
  getChild(e, t = null, i = null) {
    let s = zl(this, e, t, i);
    return s.length ? s[0] : null;
  }
  getChildren(e, t = null, i = null) {
    return zl(this, e, t, i);
  }
  resolve(e, t = 0) {
    return $i(this, e, t, !1);
  }
  resolveInner(e, t = 0) {
    return $i(this, e, t, !0);
  }
  matchContext(e) {
    return Vr(this.parent, e);
  }
  enterUnfinishedNodesBefore(e) {
    let t = this.childBefore(e), i = this;
    for (; t; ) {
      let s = t.lastChild;
      if (!s || s.to != t.to)
        break;
      s.type.isError && s.from == s.to ? (i = t, t = s.prevSibling) : t = s;
    }
    return i;
  }
  get node() {
    return this;
  }
  get next() {
    return this.parent;
  }
}
class Te extends rc {
  constructor(e, t, i, s) {
    super(), this._tree = e, this.from = t, this.index = i, this._parent = s;
  }
  get type() {
    return this._tree.type;
  }
  get name() {
    return this._tree.type.name;
  }
  get to() {
    return this.from + this._tree.length;
  }
  nextChild(e, t, i, s, r = 0) {
    for (let o = this; ; ) {
      for (let { children: l, positions: a } = o._tree, h = t > 0 ? l.length : -1; e != h; e += t) {
        let c = l[e], f = a[e] + o.from, u;
        if (!(!(r & te.EnterBracketed && c instanceof ue && (u = Pi.get(c)) && !u.overlay && u.bracketed && i >= f && i <= f + c.length) && !sc(s, i, f, f + c.length))) {
          if (c instanceof Ct) {
            if (r & te.ExcludeBuffers)
              continue;
            let d = c.findChild(0, c.buffer.length, t, i - f, s);
            if (d > -1)
              return new nt(new Zp(o, c, e, f), null, d);
          } else if (r & te.IncludeAnonymous || !c.type.isAnonymous || wo(c)) {
            let d;
            if (!(r & te.IgnoreMounts) && (d = Pi.get(c)) && !d.overlay)
              return new Te(d.tree, f, e, o);
            let p = new Te(c, f, e, o);
            return r & te.IncludeAnonymous || !p.type.isAnonymous ? p : p.nextChild(t < 0 ? c.children.length - 1 : 0, t, i, s, r);
          }
        }
      }
      if (r & te.IncludeAnonymous || !o.type.isAnonymous || (o.index >= 0 ? e = o.index + t : e = t < 0 ? -1 : o._parent._tree.children.length, o = o._parent, !o))
        return null;
    }
  }
  get firstChild() {
    return this.nextChild(
      0,
      1,
      0,
      4
      /* Side.DontCare */
    );
  }
  get lastChild() {
    return this.nextChild(
      this._tree.children.length - 1,
      -1,
      0,
      4
      /* Side.DontCare */
    );
  }
  childAfter(e) {
    return this.nextChild(
      0,
      1,
      e,
      2
      /* Side.After */
    );
  }
  childBefore(e) {
    return this.nextChild(
      this._tree.children.length - 1,
      -1,
      e,
      -2
      /* Side.Before */
    );
  }
  prop(e) {
    return this._tree.prop(e);
  }
  enter(e, t, i = 0) {
    let s;
    if (!(i & te.IgnoreOverlays) && (s = Pi.get(this._tree)) && s.overlay) {
      let r = e - this.from, o = i & te.EnterBracketed && s.bracketed;
      for (let { from: l, to: a } of s.overlay)
        if ((t > 0 || o ? l <= r : l < r) && (t < 0 || o ? a >= r : a > r))
          return new Te(s.tree, s.overlay[0].from + this.from, -1, this);
    }
    return this.nextChild(0, 1, e, t, i);
  }
  nextSignificantParent() {
    let e = this;
    for (; e.type.isAnonymous && e._parent; )
      e = e._parent;
    return e;
  }
  get parent() {
    return this._parent ? this._parent.nextSignificantParent() : null;
  }
  get nextSibling() {
    return this._parent && this.index >= 0 ? this._parent.nextChild(
      this.index + 1,
      1,
      0,
      4
      /* Side.DontCare */
    ) : null;
  }
  get prevSibling() {
    return this._parent && this.index >= 0 ? this._parent.nextChild(
      this.index - 1,
      -1,
      0,
      4
      /* Side.DontCare */
    ) : null;
  }
  get tree() {
    return this._tree;
  }
  toTree() {
    return this._tree;
  }
  /**
  @internal
  */
  toString() {
    return this._tree.toString();
  }
}
function zl(n, e, t, i) {
  let s = n.cursor(), r = [];
  if (!s.firstChild())
    return r;
  if (t != null) {
    for (let o = !1; !o; )
      if (o = s.type.is(t), !s.nextSibling())
        return r;
  }
  for (; ; ) {
    if (i != null && s.type.is(i))
      return r;
    if (s.type.is(e) && r.push(s.node), !s.nextSibling())
      return i == null ? r : [];
  }
}
function Vr(n, e, t = e.length - 1) {
  for (let i = n; t >= 0; i = i.parent) {
    if (!i)
      return !1;
    if (!i.type.isAnonymous) {
      if (e[t] && e[t] != i.name)
        return !1;
      t--;
    }
  }
  return !0;
}
class Zp {
  constructor(e, t, i, s) {
    this.parent = e, this.buffer = t, this.index = i, this.start = s;
  }
}
class nt extends rc {
  get name() {
    return this.type.name;
  }
  get from() {
    return this.context.start + this.context.buffer.buffer[this.index + 1];
  }
  get to() {
    return this.context.start + this.context.buffer.buffer[this.index + 2];
  }
  constructor(e, t, i) {
    super(), this.context = e, this._parent = t, this.index = i, this.type = e.buffer.set.types[e.buffer.buffer[i]];
  }
  child(e, t, i) {
    let { buffer: s } = this.context, r = s.findChild(this.index + 4, s.buffer[this.index + 3], e, t - this.context.start, i);
    return r < 0 ? null : new nt(this.context, this, r);
  }
  get firstChild() {
    return this.child(
      1,
      0,
      4
      /* Side.DontCare */
    );
  }
  get lastChild() {
    return this.child(
      -1,
      0,
      4
      /* Side.DontCare */
    );
  }
  childAfter(e) {
    return this.child(
      1,
      e,
      2
      /* Side.After */
    );
  }
  childBefore(e) {
    return this.child(
      -1,
      e,
      -2
      /* Side.Before */
    );
  }
  prop(e) {
    return this.type.prop(e);
  }
  enter(e, t, i = 0) {
    if (i & te.ExcludeBuffers)
      return null;
    let { buffer: s } = this.context, r = s.findChild(this.index + 4, s.buffer[this.index + 3], t > 0 ? 1 : -1, e - this.context.start, t);
    return r < 0 ? null : new nt(this.context, this, r);
  }
  get parent() {
    return this._parent || this.context.parent.nextSignificantParent();
  }
  externalSibling(e) {
    return this._parent ? null : this.context.parent.nextChild(
      this.context.index + e,
      e,
      0,
      4
      /* Side.DontCare */
    );
  }
  get nextSibling() {
    let { buffer: e } = this.context, t = e.buffer[this.index + 3];
    return t < (this._parent ? e.buffer[this._parent.index + 3] : e.buffer.length) ? new nt(this.context, this._parent, t) : this.externalSibling(1);
  }
  get prevSibling() {
    let { buffer: e } = this.context, t = this._parent ? this._parent.index + 4 : 0;
    return this.index == t ? this.externalSibling(-1) : new nt(this.context, this._parent, e.findChild(
      t,
      this.index,
      -1,
      0,
      4
      /* Side.DontCare */
    ));
  }
  get tree() {
    return null;
  }
  toTree() {
    let e = [], t = [], { buffer: i } = this.context, s = this.index + 4, r = i.buffer[this.index + 3];
    if (r > s) {
      let o = i.buffer[this.index + 1];
      e.push(i.slice(s, r, o)), t.push(0);
    }
    return new ue(this.type, e, t, this.to - this.from);
  }
  /**
  @internal
  */
  toString() {
    return this.context.buffer.childString(this.index);
  }
}
function oc(n) {
  if (!n.length)
    return null;
  let e = 0, t = n[0];
  for (let r = 1; r < n.length; r++) {
    let o = n[r];
    (o.from > t.from || o.to < t.to) && (t = o, e = r);
  }
  let i = t instanceof Te && t.index < 0 ? null : t.parent, s = n.slice();
  return i ? s[e] = i : s.splice(e, 1), new em(s, t);
}
class em {
  constructor(e, t) {
    this.heads = e, this.node = t;
  }
  get next() {
    return oc(this.heads);
  }
}
function tm(n, e, t) {
  let i = n.resolveInner(e, t), s = null;
  for (let r = i instanceof Te ? i : i.context.parent; r; r = r.parent)
    if (r.index < 0) {
      let o = r.parent;
      (s || (s = [i])).push(o.resolve(e, t)), r = o;
    } else {
      let o = Pi.get(r.tree);
      if (o && o.overlay && o.overlay[0].from <= e && o.overlay[o.overlay.length - 1].to >= e) {
        let l = new Te(o.tree, o.overlay[0].from + r.from, -1, r);
        (s || (s = [i])).push($i(l, e, t, !1));
      }
    }
  return s ? oc(s) : i;
}
class zr {
  /**
  Shorthand for `.type.name`.
  */
  get name() {
    return this.type.name;
  }
  /**
  @internal
  */
  constructor(e, t = 0) {
    if (this.buffer = null, this.stack = [], this.index = 0, this.bufferNode = null, this.mode = t & ~te.EnterBracketed, e instanceof Te)
      this.yieldNode(e);
    else {
      this._tree = e.context.parent, this.buffer = e.context;
      for (let i = e._parent; i; i = i._parent)
        this.stack.unshift(i.index);
      this.bufferNode = e, this.yieldBuf(e.index);
    }
  }
  yieldNode(e) {
    return e ? (this._tree = e, this.type = e.type, this.from = e.from, this.to = e.to, !0) : !1;
  }
  yieldBuf(e, t) {
    this.index = e;
    let { start: i, buffer: s } = this.buffer;
    return this.type = t || s.set.types[s.buffer[e]], this.from = i + s.buffer[e + 1], this.to = i + s.buffer[e + 2], !0;
  }
  /**
  @internal
  */
  yield(e) {
    return e ? e instanceof Te ? (this.buffer = null, this.yieldNode(e)) : (this.buffer = e.context, this.yieldBuf(e.index, e.type)) : !1;
  }
  /**
  @internal
  */
  toString() {
    return this.buffer ? this.buffer.buffer.childString(this.index) : this._tree.toString();
  }
  /**
  @internal
  */
  enterChild(e, t, i) {
    if (!this.buffer)
      return this.yield(this._tree.nextChild(e < 0 ? this._tree._tree.children.length - 1 : 0, e, t, i, this.mode));
    let { buffer: s } = this.buffer, r = s.findChild(this.index + 4, s.buffer[this.index + 3], e, t - this.buffer.start, i);
    return r < 0 ? !1 : (this.stack.push(this.index), this.yieldBuf(r));
  }
  /**
  Move the cursor to this node's first child. When this returns
  false, the node has no child, and the cursor has not been moved.
  */
  firstChild() {
    return this.enterChild(
      1,
      0,
      4
      /* Side.DontCare */
    );
  }
  /**
  Move the cursor to this node's last child.
  */
  lastChild() {
    return this.enterChild(
      -1,
      0,
      4
      /* Side.DontCare */
    );
  }
  /**
  Move the cursor to the first child that ends after `pos`.
  */
  childAfter(e) {
    return this.enterChild(
      1,
      e,
      2
      /* Side.After */
    );
  }
  /**
  Move to the last child that starts before `pos`.
  */
  childBefore(e) {
    return this.enterChild(
      -1,
      e,
      -2
      /* Side.Before */
    );
  }
  /**
  Move the cursor to the child around `pos`. If side is -1 the
  child may end at that position, when 1 it may start there. This
  will also enter [overlaid](#common.MountedTree.overlay)
  [mounted](#common.NodeProp^mounted) trees unless `overlays` is
  set to false.
  */
  enter(e, t, i = this.mode) {
    return this.buffer ? i & te.ExcludeBuffers ? !1 : this.enterChild(1, e, t) : this.yield(this._tree.enter(e, t, i));
  }
  /**
  Move to the node's parent node, if this isn't the top node.
  */
  parent() {
    if (!this.buffer)
      return this.yieldNode(this.mode & te.IncludeAnonymous ? this._tree._parent : this._tree.parent);
    if (this.stack.length)
      return this.yieldBuf(this.stack.pop());
    let e = this.mode & te.IncludeAnonymous ? this.buffer.parent : this.buffer.parent.nextSignificantParent();
    return this.buffer = null, this.yieldNode(e);
  }
  /**
  @internal
  */
  sibling(e) {
    if (!this.buffer)
      return this._tree._parent ? this.yield(this._tree.index < 0 ? null : this._tree._parent.nextChild(this._tree.index + e, e, 0, 4, this.mode)) : !1;
    let { buffer: t } = this.buffer, i = this.stack.length - 1;
    if (e < 0) {
      let s = i < 0 ? 0 : this.stack[i] + 4;
      if (this.index != s)
        return this.yieldBuf(t.findChild(
          s,
          this.index,
          -1,
          0,
          4
          /* Side.DontCare */
        ));
    } else {
      let s = t.buffer[this.index + 3];
      if (s < (i < 0 ? t.buffer.length : t.buffer[this.stack[i] + 3]))
        return this.yieldBuf(s);
    }
    return i < 0 ? this.yield(this.buffer.parent.nextChild(this.buffer.index + e, e, 0, 4, this.mode)) : !1;
  }
  /**
  Move to this node's next sibling, if any.
  */
  nextSibling() {
    return this.sibling(1);
  }
  /**
  Move to this node's previous sibling, if any.
  */
  prevSibling() {
    return this.sibling(-1);
  }
  atLastNode(e) {
    let t, i, { buffer: s } = this;
    if (s) {
      if (e > 0) {
        if (this.index < s.buffer.buffer.length)
          return !1;
      } else
        for (let r = 0; r < this.index; r++)
          if (s.buffer.buffer[r + 3] < this.index)
            return !1;
      ({ index: t, parent: i } = s);
    } else
      ({ index: t, _parent: i } = this._tree);
    for (; i; { index: t, _parent: i } = i)
      if (t > -1)
        for (let r = t + e, o = e < 0 ? -1 : i._tree.children.length; r != o; r += e) {
          let l = i._tree.children[r];
          if (this.mode & te.IncludeAnonymous || l instanceof Ct || !l.type.isAnonymous || wo(l))
            return !1;
        }
    return !0;
  }
  move(e, t) {
    if (t && this.enterChild(
      e,
      0,
      4
      /* Side.DontCare */
    ))
      return !0;
    for (; ; ) {
      if (this.sibling(e))
        return !0;
      if (this.atLastNode(e) || !this.parent())
        return !1;
    }
  }
  /**
  Move to the next node in a
  [pre-order](https://en.wikipedia.org/wiki/Tree_traversal#Pre-order,_NLR)
  traversal, going from a node to its first child or, if the
  current node is empty or `enter` is false, its next sibling or
  the next sibling of the first parent node that has one.
  */
  next(e = !0) {
    return this.move(1, e);
  }
  /**
  Move to the next node in a last-to-first pre-order traversal. A
  node is followed by its last child or, if it has none, its
  previous sibling or the previous sibling of the first parent
  node that has one.
  */
  prev(e = !0) {
    return this.move(-1, e);
  }
  /**
  Move the cursor to the innermost node that covers `pos`. If
  `side` is -1, it will enter nodes that end at `pos`. If it is 1,
  it will enter nodes that start at `pos`.
  */
  moveTo(e, t = 0) {
    for (; (this.from == this.to || (t < 1 ? this.from >= e : this.from > e) || (t > -1 ? this.to <= e : this.to < e)) && this.parent(); )
      ;
    for (; this.enterChild(1, e, t); )
      ;
    return this;
  }
  /**
  Get a [syntax node](#common.SyntaxNode) at the cursor's current
  position.
  */
  get node() {
    if (!this.buffer)
      return this._tree;
    let e = this.bufferNode, t = null, i = 0;
    if (e && e.context == this.buffer)
      e: for (let s = this.index, r = this.stack.length; r >= 0; ) {
        for (let o = e; o; o = o._parent)
          if (o.index == s) {
            if (s == this.index)
              return o;
            t = o, i = r + 1;
            break e;
          }
        s = this.stack[--r];
      }
    for (let s = i; s < this.stack.length; s++)
      t = new nt(this.buffer, t, this.stack[s]);
    return this.bufferNode = new nt(this.buffer, t, this.index);
  }
  /**
  Get the [tree](#common.Tree) that represents the current node, if
  any. Will return null when the node is in a [tree
  buffer](#common.TreeBuffer).
  */
  get tree() {
    return this.buffer ? null : this._tree._tree;
  }
  /**
  Iterate over the current node and all its descendants, calling
  `enter` when entering a node and `leave`, if given, when leaving
  one. When `enter` returns `false`, any children of that node are
  skipped, and `leave` isn't called for it.
  */
  iterate(e, t) {
    for (let i = 0; ; ) {
      let s = !1;
      if (this.type.isAnonymous || e(this) !== !1) {
        if (this.firstChild()) {
          i++;
          continue;
        }
        this.type.isAnonymous || (s = !0);
      }
      for (; ; ) {
        if (s && t && t(this), s = this.type.isAnonymous, !i)
          return;
        if (this.nextSibling())
          break;
        this.parent(), i--, s = !0;
      }
    }
  }
  /**
  Test whether the current node matches a given context—a sequence
  of direct parent node names. Empty strings in the context array
  are treated as wildcards.
  */
  matchContext(e) {
    if (!this.buffer)
      return Vr(this.node.parent, e);
    let { buffer: t } = this.buffer, { types: i } = t.set;
    for (let s = e.length - 1, r = this.stack.length - 1; s >= 0; r--) {
      if (r < 0)
        return Vr(this._tree, e, s);
      let o = i[t.buffer[this.stack[r]]];
      if (!o.isAnonymous) {
        if (e[s] && e[s] != o.name)
          return !1;
        s--;
      }
    }
    return !0;
  }
}
function wo(n) {
  return n.children.some((e) => e instanceof Ct || !e.type.isAnonymous || wo(e));
}
function im(n) {
  var e;
  let { buffer: t, nodeSet: i, maxBufferLength: s = Jp, reused: r = [], minRepeatType: o = i.types.length } = n, l = Array.isArray(t) ? new xo(t, t.length) : t, a = i.types, h = 0, c = 0;
  function f(k, S, C, E, I, q) {
    let { id: P, start: L, end: H, size: N } = l, $ = c, Z = h;
    if (N < 0)
      if (l.next(), N == -1) {
        let ae = r[P];
        C.push(ae), E.push(L - k);
        return;
      } else if (N == -3) {
        h = P;
        return;
      } else if (N == -4) {
        c = P;
        return;
      } else
        throw new RangeError(`Unrecognized record size: ${N}`);
    let se = a[P], be, J, le = L - k;
    if (H - L <= s && (J = g(l.pos - S, I))) {
      let ae = new Uint16Array(J.size - J.skip), xe = l.pos - J.size, Oe = ae.length;
      for (; l.pos > xe; )
        Oe = y(J.start, ae, Oe);
      be = new Ct(ae, H - J.start, i), le = J.start - k;
    } else {
      let ae = l.pos - N;
      l.next();
      let xe = [], Oe = [], j = P >= o ? P : -1, Fe = 0, Ot = H;
      for (; l.pos > ae; )
        j >= 0 && l.id == j && l.size >= 0 ? (l.end <= Ot - s && (p(xe, Oe, L, Fe, l.end, Ot, j, $, Z), Fe = xe.length, Ot = l.end), l.next()) : q > 2500 ? u(L, ae, xe, Oe) : f(L, ae, xe, Oe, j, q + 1);
      if (j >= 0 && Fe > 0 && Fe < xe.length && p(xe, Oe, L, Fe, L, Ot, j, $, Z), xe.reverse(), Oe.reverse(), j > -1 && Fe > 0) {
        let jt = d(se, Z);
        be = vo(se, xe, Oe, 0, xe.length, 0, H - L, jt, jt);
      } else
        be = m(se, xe, Oe, H - L, $ - H, Z);
    }
    C.push(be), E.push(le);
  }
  function u(k, S, C, E) {
    let I = [], q = 0, P = -1;
    for (; l.pos > S; ) {
      let { id: L, start: H, end: N, size: $ } = l;
      if ($ > 4)
        l.next();
      else {
        if (P > -1 && H < P)
          break;
        P < 0 && (P = N - s), I.push(L, H, N), q++, l.next();
      }
    }
    if (q) {
      let L = new Uint16Array(q * 4), H = I[I.length - 2];
      for (let N = I.length - 3, $ = 0; N >= 0; N -= 3)
        L[$++] = I[N], L[$++] = I[N + 1] - H, L[$++] = I[N + 2] - H, L[$++] = $;
      C.push(new Ct(L, I[2] - H, i)), E.push(H - k);
    }
  }
  function d(k, S) {
    return (C, E, I) => {
      let q = 0, P = C.length - 1, L, H;
      if (P >= 0 && (L = C[P]) instanceof ue) {
        if (!P && L.type == k && L.length == I)
          return L;
        (H = L.prop(V.lookAhead)) && (q = E[P] + L.length + H);
      }
      return m(k, C, E, I, q, S);
    };
  }
  function p(k, S, C, E, I, q, P, L, H) {
    let N = [], $ = [];
    for (; k.length > E; )
      N.push(k.pop()), $.push(S.pop() + C - I);
    k.push(m(i.types[P], N, $, q - I, L - q, H)), S.push(I - C);
  }
  function m(k, S, C, E, I, q, P) {
    if (q) {
      let L = [V.contextHash, q];
      P = P ? [L].concat(P) : [L];
    }
    if (I > 25) {
      let L = [V.lookAhead, I];
      P = P ? [L].concat(P) : [L];
    }
    return new ue(k, S, C, E, P);
  }
  function g(k, S) {
    let C = l.fork(), E = 0, I = 0, q = 0, P = C.end - s, L = { size: 0, start: 0, skip: 0 };
    e: for (let H = C.pos - k; C.pos > H; ) {
      let N = C.size;
      if (C.id == S && N >= 0) {
        L.size = E, L.start = I, L.skip = q, q += 4, E += 4, C.next();
        continue;
      }
      let $ = C.pos - N;
      if (N < 0 || $ < H || C.start < P)
        break;
      let Z = C.id >= o ? 4 : 0, se = C.start;
      for (C.next(); C.pos > $; ) {
        if (C.size < 0)
          if (C.size == -3 || C.size == -4)
            Z += 4;
          else
            break e;
        else C.id >= o && (Z += 4);
        C.next();
      }
      I = se, E += N, q += Z;
    }
    return (S < 0 || E == k) && (L.size = E, L.start = I, L.skip = q), L.size > 4 ? L : void 0;
  }
  function y(k, S, C) {
    let { id: E, start: I, end: q, size: P } = l;
    if (l.next(), P >= 0 && E < o) {
      let L = C;
      if (P > 4) {
        let H = l.pos - (P - 4);
        for (; l.pos > H; )
          C = y(k, S, C);
      }
      S[--C] = L, S[--C] = q - k, S[--C] = I - k, S[--C] = E;
    } else P == -3 ? h = E : P == -4 && (c = E);
    return C;
  }
  let w = [], v = [];
  for (; l.pos > 0; )
    f(n.start || 0, n.bufferStart || 0, w, v, -1, 0);
  let O = (e = n.length) !== null && e !== void 0 ? e : w.length ? v[0] + w[0].length : 0;
  return new ue(a[n.topID], w.reverse(), v.reverse(), O);
}
const ql = /* @__PURE__ */ new WeakMap();
function Fn(n, e) {
  if (!n.isAnonymous || e instanceof Ct || e.type != n)
    return 1;
  let t = ql.get(e);
  if (t == null) {
    t = 1;
    for (let i of e.children) {
      if (i.type != n || !(i instanceof ue)) {
        t = 1;
        break;
      }
      t += Fn(n, i);
    }
    ql.set(e, t);
  }
  return t;
}
function vo(n, e, t, i, s, r, o, l, a) {
  let h = 0;
  for (let p = i; p < s; p++)
    h += Fn(n, e[p]);
  let c = Math.ceil(
    h * 1.5 / 8
    /* Balance.BranchFactor */
  ), f = [], u = [];
  function d(p, m, g, y, w) {
    for (let v = g; v < y; ) {
      let O = v, k = m[v], S = Fn(n, p[v]);
      for (v++; v < y; v++) {
        let C = Fn(n, p[v]);
        if (S + C >= c)
          break;
        S += C;
      }
      if (v == O + 1) {
        if (S > c) {
          let C = p[O];
          d(C.children, C.positions, 0, C.children.length, m[O] + w);
          continue;
        }
        f.push(p[O]);
      } else {
        let C = m[v - 1] + p[v - 1].length - k;
        f.push(vo(n, p, m, O, v, k, C, null, a));
      }
      u.push(k + w - r);
    }
  }
  return d(e, t, i, s, 0), (l || a)(f, u, o);
}
class Db {
  constructor() {
    this.map = /* @__PURE__ */ new WeakMap();
  }
  setBuffer(e, t, i) {
    let s = this.map.get(e);
    s || this.map.set(e, s = /* @__PURE__ */ new Map()), s.set(t, i);
  }
  getBuffer(e, t) {
    let i = this.map.get(e);
    return i && i.get(t);
  }
  /**
  Set the value for this syntax node.
  */
  set(e, t) {
    e instanceof nt ? this.setBuffer(e.context.buffer, e.index, t) : e instanceof Te && this.map.set(e.tree, t);
  }
  /**
  Retrieve value for this syntax node, if it exists in the map.
  */
  get(e) {
    return e instanceof nt ? this.getBuffer(e.context.buffer, e.index) : e instanceof Te ? this.map.get(e.tree) : void 0;
  }
  /**
  Set the value for the node that a cursor currently points to.
  */
  cursorSet(e, t) {
    e.buffer ? this.setBuffer(e.buffer.buffer, e.index, t) : this.map.set(e.tree, t);
  }
  /**
  Retrieve the value for the node that a cursor currently points
  to.
  */
  cursorGet(e) {
    return e.buffer ? this.getBuffer(e.buffer.buffer, e.index) : this.map.get(e.tree);
  }
}
class Ft {
  /**
  Construct a tree fragment. You'll usually want to use
  [`addTree`](#common.TreeFragment^addTree) and
  [`applyChanges`](#common.TreeFragment^applyChanges) instead of
  calling this directly.
  */
  constructor(e, t, i, s, r = !1, o = !1) {
    this.from = e, this.to = t, this.tree = i, this.offset = s, this.open = (r ? 1 : 0) | (o ? 2 : 0);
  }
  /**
  Whether the start of the fragment represents the start of a
  parse, or the end of a change. (In the second case, it may not
  be safe to reuse some nodes at the start, depending on the
  parsing algorithm.)
  */
  get openStart() {
    return (this.open & 1) > 0;
  }
  /**
  Whether the end of the fragment represents the end of a
  full-document parse, or the start of a change.
  */
  get openEnd() {
    return (this.open & 2) > 0;
  }
  /**
  Create a set of fragments from a freshly parsed tree, or update
  an existing set of fragments by replacing the ones that overlap
  with a tree with content from the new tree. When `partial` is
  true, the parse is treated as incomplete, and the resulting
  fragment has [`openEnd`](#common.TreeFragment.openEnd) set to
  true.
  */
  static addTree(e, t = [], i = !1) {
    let s = [new Ft(0, e.length, e, 0, !1, i)];
    for (let r of t)
      r.to > e.length && s.push(r);
    return s;
  }
  /**
  Apply a set of edits to an array of fragments, removing or
  splitting fragments as necessary to remove edited ranges, and
  adjusting offsets for fragments that moved.
  */
  static applyChanges(e, t, i = 128) {
    if (!t.length)
      return e;
    let s = [], r = 1, o = e.length ? e[0] : null;
    for (let l = 0, a = 0, h = 0; ; l++) {
      let c = l < t.length ? t[l] : null, f = c ? c.fromA : 1e9;
      if (f - a >= i)
        for (; o && o.from < f; ) {
          let u = o;
          if (a >= u.from || f <= u.to || h) {
            let d = Math.max(u.from, a) - h, p = Math.min(u.to, f) - h;
            u = d >= p ? null : new Ft(d, p, u.tree, u.offset + h, l > 0, !!c);
          }
          if (u && s.push(u), o.to > f)
            break;
          o = r < e.length ? e[r++] : null;
        }
      if (!c)
        break;
      a = c.toA, h = c.toA - c.toB;
    }
    return s;
  }
}
class nm {
  /**
  Start a parse, returning a [partial parse](#common.PartialParse)
  object. [`fragments`](#common.TreeFragment) can be passed in to
  make the parse incremental.
  
  By default, the entire input is parsed. You can pass `ranges`,
  which should be a sorted array of non-empty, non-overlapping
  ranges, to parse only those ranges. The tree returned in that
  case will start at `ranges[0].from`.
  */
  startParse(e, t, i) {
    return typeof e == "string" && (e = new sm(e)), i = i ? i.length ? i.map((s) => new $s(s.from, s.to)) : [new $s(0, 0)] : [new $s(0, e.length)], this.createParse(e, t || [], i);
  }
  /**
  Run a full parse, returning the resulting tree.
  */
  parse(e, t, i) {
    let s = this.startParse(e, t, i);
    for (; ; ) {
      let r = s.advance();
      if (r)
        return r;
    }
  }
}
class sm {
  constructor(e) {
    this.string = e;
  }
  get length() {
    return this.string.length;
  }
  chunk(e) {
    return this.string.slice(e);
  }
  get lineChunks() {
    return !1;
  }
  read(e, t) {
    return this.string.slice(e, t);
  }
}
new V({ perNode: !0 });
let rm = 0;
class Pe {
  /**
  @internal
  */
  constructor(e, t, i, s) {
    this.name = e, this.set = t, this.base = i, this.modified = s, this.id = rm++;
  }
  toString() {
    let { name: e } = this;
    for (let t of this.modified)
      t.name && (e = `${t.name}(${e})`);
    return e;
  }
  static define(e, t) {
    let i = typeof e == "string" ? e : "?";
    if (e instanceof Pe && (t = e), t?.base)
      throw new Error("Can not derive from a modified tag");
    let s = new Pe(i, [], null, []);
    if (s.set.push(s), t)
      for (let r of t.set)
        s.set.push(r);
    return s;
  }
  /**
  Define a tag _modifier_, which is a function that, given a tag,
  will return a tag that is a subtag of the original. Applying the
  same modifier to a twice tag will return the same value (`m1(t1)
  == m1(t1)`) and applying multiple modifiers will, regardless or
  order, produce the same tag (`m1(m2(t1)) == m2(m1(t1))`).
  
  When multiple modifiers are applied to a given base tag, each
  smaller set of modifiers is registered as a parent, so that for
  example `m1(m2(m3(t1)))` is a subtype of `m1(m2(t1))`,
  `m1(m3(t1)`, and so on.
  */
  static defineModifier(e) {
    let t = new Qn(e);
    return (i) => i.modified.indexOf(t) > -1 ? i : Qn.get(i.base || i, i.modified.concat(t).sort((s, r) => s.id - r.id));
  }
}
let om = 0;
class Qn {
  constructor(e) {
    this.name = e, this.instances = [], this.id = om++;
  }
  static get(e, t) {
    if (!t.length)
      return e;
    let i = t[0].instances.find((l) => l.base == e && lm(t, l.modified));
    if (i)
      return i;
    let s = [], r = new Pe(e.name, s, e, t);
    for (let l of t)
      l.instances.push(r);
    let o = am(t);
    for (let l of e.set)
      if (!l.modified.length)
        for (let a of o)
          s.push(Qn.get(l, a));
    return r;
  }
}
function lm(n, e) {
  return n.length == e.length && n.every((t, i) => t == e[i]);
}
function am(n) {
  let e = [[]];
  for (let t = 0; t < n.length; t++)
    for (let i = 0, s = e.length; i < s; i++)
      e.push(e[i].concat(n[t]));
  return e.sort((t, i) => i.length - t.length);
}
function hm(n) {
  let e = /* @__PURE__ */ Object.create(null);
  for (let t in n) {
    let i = n[t];
    Array.isArray(i) || (i = [i]);
    for (let s of t.split(" "))
      if (s) {
        let r = [], o = 2, l = s;
        for (let f = 0; ; ) {
          if (l == "..." && f > 0 && f + 3 == s.length) {
            o = 1;
            break;
          }
          let u = /^"(?:[^"\\]|\\.)*?"|[^\/!]+/.exec(l);
          if (!u)
            throw new RangeError("Invalid path: " + s);
          if (r.push(u[0] == "*" ? "" : u[0][0] == '"' ? JSON.parse(u[0]) : u[0]), f += u[0].length, f == s.length)
            break;
          let d = s[f++];
          if (f == s.length && d == "!") {
            o = 0;
            break;
          }
          if (d != "/")
            throw new RangeError("Invalid path: " + s);
          l = s.slice(f);
        }
        let a = r.length - 1, h = r[a];
        if (!h)
          throw new RangeError("Invalid path: " + s);
        let c = new Ki(i, o, a > 0 ? r.slice(0, a) : null);
        e[h] = c.sort(e[h]);
      }
  }
  return lc.add(e);
}
const lc = new V({
  combine(n, e) {
    let t, i, s;
    for (; n || e; ) {
      if (!n || e && n.depth >= e.depth ? (s = e, e = e.next) : (s = n, n = n.next), t && t.mode == s.mode && !s.context && !t.context)
        continue;
      let r = new Ki(s.tags, s.mode, s.context);
      t ? t.next = r : i = r, t = r;
    }
    return i;
  }
});
class Ki {
  constructor(e, t, i, s) {
    this.tags = e, this.mode = t, this.context = i, this.next = s;
  }
  get opaque() {
    return this.mode == 0;
  }
  get inherit() {
    return this.mode == 1;
  }
  sort(e) {
    return !e || e.depth < this.depth ? (this.next = e, this) : (e.next = this.sort(e.next), e);
  }
  get depth() {
    return this.context ? this.context.length : 0;
  }
}
Ki.empty = new Ki([], 2, null);
function ac(n, e) {
  let t = /* @__PURE__ */ Object.create(null);
  for (let r of n)
    if (!Array.isArray(r.tag))
      t[r.tag.id] = r.class;
    else
      for (let o of r.tag)
        t[o.id] = r.class;
  let { scope: i, all: s = null } = e || {};
  return {
    style: (r) => {
      let o = s;
      for (let l of r)
        for (let a of l.set) {
          let h = t[a.id];
          if (h) {
            o = o ? o + " " + h : h;
            break;
          }
        }
      return o;
    },
    scope: i
  };
}
function cm(n, e) {
  let t = null;
  for (let i of n) {
    let s = i.style(e);
    s && (t = t ? t + " " + s : s);
  }
  return t;
}
function fm(n, e, t, i = 0, s = n.length) {
  let r = new um(i, Array.isArray(e) ? e : [e], t);
  r.highlightRange(n.cursor(), i, s, "", r.highlighters), r.flush(s);
}
class um {
  constructor(e, t, i) {
    this.at = e, this.highlighters = t, this.span = i, this.class = "";
  }
  startSpan(e, t) {
    t != this.class && (this.flush(e), e > this.at && (this.at = e), this.class = t);
  }
  flush(e) {
    e > this.at && this.class && this.span(this.at, e, this.class);
  }
  highlightRange(e, t, i, s, r) {
    let { type: o, from: l, to: a } = e;
    if (l >= i || a <= t)
      return;
    o.isTop && (r = this.highlighters.filter((d) => !d.scope || d.scope(o)));
    let h = s, c = dm(e) || Ki.empty, f = cm(r, c.tags);
    if (f && (h && (h += " "), h += f, c.mode == 1 && (s += (s ? " " : "") + f)), this.startSpan(Math.max(t, l), h), c.opaque)
      return;
    let u = e.tree && e.tree.prop(V.mounted);
    if (u && u.overlay) {
      let d = e.node.enter(u.overlay[0].from + l, 1), p = this.highlighters.filter((g) => !g.scope || g.scope(u.tree.type)), m = e.firstChild();
      for (let g = 0, y = l; ; g++) {
        let w = g < u.overlay.length ? u.overlay[g] : null, v = w ? w.from + l : a, O = Math.max(t, y), k = Math.min(i, v);
        if (O < k && m)
          for (; e.from < k && (this.highlightRange(e, O, k, s, r), this.startSpan(Math.min(k, e.to), h), !(e.to >= v || !e.nextSibling())); )
            ;
        if (!w || v > i)
          break;
        y = w.to + l, y > t && (this.highlightRange(d.cursor(), Math.max(t, w.from + l), Math.min(i, y), "", p), this.startSpan(Math.min(i, y), h));
      }
      m && e.parent();
    } else if (e.firstChild()) {
      u && (s = "");
      do
        if (!(e.to <= t)) {
          if (e.from >= i)
            break;
          this.highlightRange(e, t, i, s, r), this.startSpan(Math.min(i, e.to), h);
        }
      while (e.nextSibling());
      e.parent();
    }
  }
}
function dm(n) {
  let e = n.type.prop(lc);
  for (; e && e.context && !n.matchContext(e.context); )
    e = e.next;
  return e || null;
}
const A = Pe.define, An = A(), pt = A(), $l = A(pt), Kl = A(pt), mt = A(), Mn = A(mt), Ks = A(mt), Qe = A(), Et = A(Qe), Je = A(), Xe = A(), qr = A(), vi = A(qr), Tn = A(), x = {
  /**
  A comment.
  */
  comment: An,
  /**
  A line [comment](#highlight.tags.comment).
  */
  lineComment: A(An),
  /**
  A block [comment](#highlight.tags.comment).
  */
  blockComment: A(An),
  /**
  A documentation [comment](#highlight.tags.comment).
  */
  docComment: A(An),
  /**
  Any kind of identifier.
  */
  name: pt,
  /**
  The [name](#highlight.tags.name) of a variable.
  */
  variableName: A(pt),
  /**
  A type [name](#highlight.tags.name).
  */
  typeName: $l,
  /**
  A tag name (subtag of [`typeName`](#highlight.tags.typeName)).
  */
  tagName: A($l),
  /**
  A property or field [name](#highlight.tags.name).
  */
  propertyName: Kl,
  /**
  An attribute name (subtag of [`propertyName`](#highlight.tags.propertyName)).
  */
  attributeName: A(Kl),
  /**
  The [name](#highlight.tags.name) of a class.
  */
  className: A(pt),
  /**
  A label [name](#highlight.tags.name).
  */
  labelName: A(pt),
  /**
  A namespace [name](#highlight.tags.name).
  */
  namespace: A(pt),
  /**
  The [name](#highlight.tags.name) of a macro.
  */
  macroName: A(pt),
  /**
  A literal value.
  */
  literal: mt,
  /**
  A string [literal](#highlight.tags.literal).
  */
  string: Mn,
  /**
  A documentation [string](#highlight.tags.string).
  */
  docString: A(Mn),
  /**
  A character literal (subtag of [string](#highlight.tags.string)).
  */
  character: A(Mn),
  /**
  An attribute value (subtag of [string](#highlight.tags.string)).
  */
  attributeValue: A(Mn),
  /**
  A number [literal](#highlight.tags.literal).
  */
  number: Ks,
  /**
  An integer [number](#highlight.tags.number) literal.
  */
  integer: A(Ks),
  /**
  A floating-point [number](#highlight.tags.number) literal.
  */
  float: A(Ks),
  /**
  A boolean [literal](#highlight.tags.literal).
  */
  bool: A(mt),
  /**
  Regular expression [literal](#highlight.tags.literal).
  */
  regexp: A(mt),
  /**
  An escape [literal](#highlight.tags.literal), for example a
  backslash escape in a string.
  */
  escape: A(mt),
  /**
  A color [literal](#highlight.tags.literal).
  */
  color: A(mt),
  /**
  A URL [literal](#highlight.tags.literal).
  */
  url: A(mt),
  /**
  A language keyword.
  */
  keyword: Je,
  /**
  The [keyword](#highlight.tags.keyword) for the self or this
  object.
  */
  self: A(Je),
  /**
  The [keyword](#highlight.tags.keyword) for null.
  */
  null: A(Je),
  /**
  A [keyword](#highlight.tags.keyword) denoting some atomic value.
  */
  atom: A(Je),
  /**
  A [keyword](#highlight.tags.keyword) that represents a unit.
  */
  unit: A(Je),
  /**
  A modifier [keyword](#highlight.tags.keyword).
  */
  modifier: A(Je),
  /**
  A [keyword](#highlight.tags.keyword) that acts as an operator.
  */
  operatorKeyword: A(Je),
  /**
  A control-flow related [keyword](#highlight.tags.keyword).
  */
  controlKeyword: A(Je),
  /**
  A [keyword](#highlight.tags.keyword) that defines something.
  */
  definitionKeyword: A(Je),
  /**
  A [keyword](#highlight.tags.keyword) related to defining or
  interfacing with modules.
  */
  moduleKeyword: A(Je),
  /**
  An operator.
  */
  operator: Xe,
  /**
  An [operator](#highlight.tags.operator) that dereferences something.
  */
  derefOperator: A(Xe),
  /**
  Arithmetic-related [operator](#highlight.tags.operator).
  */
  arithmeticOperator: A(Xe),
  /**
  Logical [operator](#highlight.tags.operator).
  */
  logicOperator: A(Xe),
  /**
  Bit [operator](#highlight.tags.operator).
  */
  bitwiseOperator: A(Xe),
  /**
  Comparison [operator](#highlight.tags.operator).
  */
  compareOperator: A(Xe),
  /**
  [Operator](#highlight.tags.operator) that updates its operand.
  */
  updateOperator: A(Xe),
  /**
  [Operator](#highlight.tags.operator) that defines something.
  */
  definitionOperator: A(Xe),
  /**
  Type-related [operator](#highlight.tags.operator).
  */
  typeOperator: A(Xe),
  /**
  Control-flow [operator](#highlight.tags.operator).
  */
  controlOperator: A(Xe),
  /**
  Program or markup punctuation.
  */
  punctuation: qr,
  /**
  [Punctuation](#highlight.tags.punctuation) that separates
  things.
  */
  separator: A(qr),
  /**
  Bracket-style [punctuation](#highlight.tags.punctuation).
  */
  bracket: vi,
  /**
  Angle [brackets](#highlight.tags.bracket) (usually `<` and `>`
  tokens).
  */
  angleBracket: A(vi),
  /**
  Square [brackets](#highlight.tags.bracket) (usually `[` and `]`
  tokens).
  */
  squareBracket: A(vi),
  /**
  Parentheses (usually `(` and `)` tokens). Subtag of
  [bracket](#highlight.tags.bracket).
  */
  paren: A(vi),
  /**
  Braces (usually `{` and `}` tokens). Subtag of
  [bracket](#highlight.tags.bracket).
  */
  brace: A(vi),
  /**
  Content, for example plain text in XML or markup documents.
  */
  content: Qe,
  /**
  [Content](#highlight.tags.content) that represents a heading.
  */
  heading: Et,
  /**
  A level 1 [heading](#highlight.tags.heading).
  */
  heading1: A(Et),
  /**
  A level 2 [heading](#highlight.tags.heading).
  */
  heading2: A(Et),
  /**
  A level 3 [heading](#highlight.tags.heading).
  */
  heading3: A(Et),
  /**
  A level 4 [heading](#highlight.tags.heading).
  */
  heading4: A(Et),
  /**
  A level 5 [heading](#highlight.tags.heading).
  */
  heading5: A(Et),
  /**
  A level 6 [heading](#highlight.tags.heading).
  */
  heading6: A(Et),
  /**
  A prose [content](#highlight.tags.content) separator (such as a horizontal rule).
  */
  contentSeparator: A(Qe),
  /**
  [Content](#highlight.tags.content) that represents a list.
  */
  list: A(Qe),
  /**
  [Content](#highlight.tags.content) that represents a quote.
  */
  quote: A(Qe),
  /**
  [Content](#highlight.tags.content) that is emphasized.
  */
  emphasis: A(Qe),
  /**
  [Content](#highlight.tags.content) that is styled strong.
  */
  strong: A(Qe),
  /**
  [Content](#highlight.tags.content) that is part of a link.
  */
  link: A(Qe),
  /**
  [Content](#highlight.tags.content) that is styled as code or
  monospace.
  */
  monospace: A(Qe),
  /**
  [Content](#highlight.tags.content) that has a strike-through
  style.
  */
  strikethrough: A(Qe),
  /**
  Inserted text in a change-tracking format.
  */
  inserted: A(),
  /**
  Deleted text.
  */
  deleted: A(),
  /**
  Changed text.
  */
  changed: A(),
  /**
  An invalid or unsyntactic element.
  */
  invalid: A(),
  /**
  Metadata or meta-instruction.
  */
  meta: Tn,
  /**
  [Metadata](#highlight.tags.meta) that applies to the entire
  document.
  */
  documentMeta: A(Tn),
  /**
  [Metadata](#highlight.tags.meta) that annotates or adds
  attributes to a given syntactic element.
  */
  annotation: A(Tn),
  /**
  Processing instruction or preprocessor directive. Subtag of
  [meta](#highlight.tags.meta).
  */
  processingInstruction: A(Tn),
  /**
  [Modifier](#highlight.Tag^defineModifier) that indicates that a
  given element is being defined. Expected to be used with the
  various [name](#highlight.tags.name) tags.
  */
  definition: Pe.defineModifier("definition"),
  /**
  [Modifier](#highlight.Tag^defineModifier) that indicates that
  something is constant. Mostly expected to be used with
  [variable names](#highlight.tags.variableName).
  */
  constant: Pe.defineModifier("constant"),
  /**
  [Modifier](#highlight.Tag^defineModifier) used to indicate that
  a [variable](#highlight.tags.variableName) or [property
  name](#highlight.tags.propertyName) is being called or defined
  as a function.
  */
  function: Pe.defineModifier("function"),
  /**
  [Modifier](#highlight.Tag^defineModifier) that can be applied to
  [names](#highlight.tags.name) to indicate that they belong to
  the language's standard environment.
  */
  standard: Pe.defineModifier("standard"),
  /**
  [Modifier](#highlight.Tag^defineModifier) that indicates a given
  [names](#highlight.tags.name) is local to some scope.
  */
  local: Pe.defineModifier("local"),
  /**
  A generic variant [modifier](#highlight.Tag^defineModifier) that
  can be used to tag language-specific alternative variants of
  some common tag. It is recommended for themes to define special
  forms of at least the [string](#highlight.tags.string) and
  [variable name](#highlight.tags.variableName) tags, since those
  come up a lot.
  */
  special: Pe.defineModifier("special")
};
for (let n in x) {
  let e = x[n];
  e instanceof Pe && (e.name = n);
}
ac([
  { tag: x.link, class: "tok-link" },
  { tag: x.heading, class: "tok-heading" },
  { tag: x.emphasis, class: "tok-emphasis" },
  { tag: x.strong, class: "tok-strong" },
  { tag: x.keyword, class: "tok-keyword" },
  { tag: x.atom, class: "tok-atom" },
  { tag: x.bool, class: "tok-bool" },
  { tag: x.url, class: "tok-url" },
  { tag: x.labelName, class: "tok-labelName" },
  { tag: x.inserted, class: "tok-inserted" },
  { tag: x.deleted, class: "tok-deleted" },
  { tag: x.literal, class: "tok-literal" },
  { tag: x.string, class: "tok-string" },
  { tag: x.number, class: "tok-number" },
  { tag: [x.regexp, x.escape, x.special(x.string)], class: "tok-string2" },
  { tag: x.variableName, class: "tok-variableName" },
  { tag: x.local(x.variableName), class: "tok-variableName tok-local" },
  { tag: x.definition(x.variableName), class: "tok-variableName tok-definition" },
  { tag: x.special(x.variableName), class: "tok-variableName2" },
  { tag: x.definition(x.propertyName), class: "tok-propertyName tok-definition" },
  { tag: x.typeName, class: "tok-typeName" },
  { tag: x.namespace, class: "tok-namespace" },
  { tag: x.className, class: "tok-className" },
  { tag: x.macroName, class: "tok-macroName" },
  { tag: x.propertyName, class: "tok-propertyName" },
  { tag: x.operator, class: "tok-operator" },
  { tag: x.comment, class: "tok-comment" },
  { tag: x.meta, class: "tok-meta" },
  { tag: x.invalid, class: "tok-invalid" },
  { tag: x.punctuation, class: "tok-punctuation" }
]);
var js;
const Zt = /* @__PURE__ */ new V();
function pm(n) {
  return T.define({
    combine: n ? (e) => e.concat(n) : void 0
  });
}
const mm = /* @__PURE__ */ new V();
class qe {
  /**
  Construct a language object. If you need to invoke this
  directly, first define a data facet with
  [`defineLanguageFacet`](https://codemirror.net/6/docs/ref/#language.defineLanguageFacet), and then
  configure your parser to [attach](https://codemirror.net/6/docs/ref/#language.languageDataProp) it
  to the language's outer syntax node.
  */
  constructor(e, t, i = [], s = "") {
    this.data = e, this.name = s, F.prototype.hasOwnProperty("tree") || Object.defineProperty(F.prototype, "tree", { get() {
      return pe(this);
    } }), this.parser = t, this.extension = [
      At.of(this),
      F.languageData.of((r, o, l) => {
        let a = jl(r, o, l), h = a.type.prop(Zt);
        if (!h)
          return [];
        let c = r.facet(h), f = a.type.prop(mm);
        if (f) {
          let u = a.resolve(o - a.from, l);
          for (let d of f)
            if (d.test(u, r)) {
              let p = r.facet(d.facet);
              return d.type == "replace" ? p : p.concat(c);
            }
        }
        return c;
      })
    ].concat(i);
  }
  /**
  Query whether this language is active at the given position.
  */
  isActiveAt(e, t, i = -1) {
    return jl(e, t, i).type.prop(Zt) == this.data;
  }
  /**
  Find the document regions that were parsed using this language.
  The returned regions will _include_ any nested languages rooted
  in this language, when those exist.
  */
  findRegions(e) {
    let t = e.facet(At);
    if (t?.data == this.data)
      return [{ from: 0, to: e.doc.length }];
    if (!t || !t.allowsNesting)
      return [];
    let i = [], s = (r, o) => {
      if (r.prop(Zt) == this.data) {
        i.push({ from: o, to: o + r.length });
        return;
      }
      let l = r.prop(V.mounted);
      if (l) {
        if (l.tree.prop(Zt) == this.data) {
          if (l.overlay)
            for (let a of l.overlay)
              i.push({ from: a.from + o, to: a.to + o });
          else
            i.push({ from: o, to: o + r.length });
          return;
        } else if (l.overlay) {
          let a = i.length;
          if (s(l.tree, l.overlay[0].from + o), i.length > a)
            return;
        }
      }
      for (let a = 0; a < r.children.length; a++) {
        let h = r.children[a];
        h instanceof ue && s(h, r.positions[a] + o);
      }
    };
    return s(pe(e), 0), i;
  }
  /**
  Indicates whether this language allows nested languages. The
  default implementation returns true.
  */
  get allowsNesting() {
    return !0;
  }
}
qe.setState = /* @__PURE__ */ R.define();
function jl(n, e, t) {
  let i = n.facet(At), s = pe(n).topNode;
  if (!i || i.allowsNesting)
    for (let r = s; r; r = r.enter(e, t, te.ExcludeBuffers | te.EnterBracketed))
      r.type.isTop && (s = r);
  return s;
}
class $r extends qe {
  constructor(e, t, i) {
    super(e, t, [], i), this.parser = t;
  }
  /**
  Define a language from a parser.
  */
  static define(e) {
    let t = pm(e.languageData);
    return new $r(t, e.parser.configure({
      props: [Zt.add((i) => i.isTop ? t : void 0)]
    }), e.name);
  }
  /**
  Create a new instance of this language with a reconfigured
  version of its parser and optionally a new name.
  */
  configure(e, t) {
    return new $r(this.data, this.parser.configure(e), t || this.name);
  }
  get allowsNesting() {
    return this.parser.hasWrappers();
  }
}
function pe(n) {
  let e = n.field(qe.state, !1);
  return e ? e.tree : ue.empty;
}
class gm {
  /**
  Create an input object for the given document.
  */
  constructor(e) {
    this.doc = e, this.cursorPos = 0, this.string = "", this.cursor = e.iter();
  }
  get length() {
    return this.doc.length;
  }
  syncTo(e) {
    return this.string = this.cursor.next(e - this.cursorPos).value, this.cursorPos = e + this.string.length, this.cursorPos - this.string.length;
  }
  chunk(e) {
    return this.syncTo(e), this.string;
  }
  get lineChunks() {
    return !0;
  }
  read(e, t) {
    let i = this.cursorPos - this.string.length;
    return e < i || t >= this.cursorPos ? this.doc.sliceString(e, t) : this.string.slice(e - i, t - i);
  }
}
let ki = null;
class Zn {
  constructor(e, t, i = [], s, r, o, l, a) {
    this.parser = e, this.state = t, this.fragments = i, this.tree = s, this.treeLen = r, this.viewport = o, this.skipped = l, this.scheduleOn = a, this.parse = null, this.tempSkipped = [];
  }
  /**
  @internal
  */
  static create(e, t, i) {
    return new Zn(e, t, [], ue.empty, 0, i, [], null);
  }
  startParse() {
    return this.parser.startParse(new gm(this.state.doc), this.fragments);
  }
  /**
  @internal
  */
  work(e, t) {
    return t != null && t >= this.state.doc.length && (t = void 0), this.tree != ue.empty && this.isDone(t ?? this.state.doc.length) ? (this.takeTree(), !0) : this.withContext(() => {
      var i;
      if (typeof e == "number") {
        let s = Date.now() + e;
        e = () => Date.now() > s;
      }
      for (this.parse || (this.parse = this.startParse()), t != null && (this.parse.stoppedAt == null || this.parse.stoppedAt > t) && t < this.state.doc.length && this.parse.stopAt(t); ; ) {
        let s = this.parse.advance();
        if (s)
          if (this.fragments = this.withoutTempSkipped(Ft.addTree(s, this.fragments, this.parse.stoppedAt != null)), this.treeLen = (i = this.parse.stoppedAt) !== null && i !== void 0 ? i : this.state.doc.length, this.tree = s, this.parse = null, this.treeLen < (t ?? this.state.doc.length))
            this.parse = this.startParse();
          else
            return !0;
        if (e())
          return !1;
      }
    });
  }
  /**
  @internal
  */
  takeTree() {
    let e, t;
    this.parse && (e = this.parse.parsedPos) >= this.treeLen && ((this.parse.stoppedAt == null || this.parse.stoppedAt > e) && this.parse.stopAt(e), this.withContext(() => {
      for (; !(t = this.parse.advance()); )
        ;
    }), this.treeLen = e, this.tree = t, this.fragments = this.withoutTempSkipped(Ft.addTree(this.tree, this.fragments, !0)), this.parse = null);
  }
  withContext(e) {
    let t = ki;
    ki = this;
    try {
      return e();
    } finally {
      ki = t;
    }
  }
  withoutTempSkipped(e) {
    for (let t; t = this.tempSkipped.pop(); )
      e = Ul(e, t.from, t.to);
    return e;
  }
  /**
  @internal
  */
  changes(e, t) {
    let { fragments: i, tree: s, treeLen: r, viewport: o, skipped: l } = this;
    if (this.takeTree(), !e.empty) {
      let a = [];
      if (e.iterChangedRanges((h, c, f, u) => a.push({ fromA: h, toA: c, fromB: f, toB: u })), i = Ft.applyChanges(i, a), s = ue.empty, r = 0, o = { from: e.mapPos(o.from, -1), to: e.mapPos(o.to, 1) }, this.skipped.length) {
        l = [];
        for (let h of this.skipped) {
          let c = e.mapPos(h.from, 1), f = e.mapPos(h.to, -1);
          c < f && l.push({ from: c, to: f });
        }
      }
    }
    return new Zn(this.parser, t, i, s, r, o, l, this.scheduleOn);
  }
  /**
  @internal
  */
  updateViewport(e) {
    if (this.viewport.from == e.from && this.viewport.to == e.to)
      return !1;
    this.viewport = e;
    let t = this.skipped.length;
    for (let i = 0; i < this.skipped.length; i++) {
      let { from: s, to: r } = this.skipped[i];
      s < e.to && r > e.from && (this.fragments = Ul(this.fragments, s, r), this.skipped.splice(i--, 1));
    }
    return this.skipped.length >= t ? !1 : (this.reset(), !0);
  }
  /**
  @internal
  */
  reset() {
    this.parse && (this.takeTree(), this.parse = null);
  }
  /**
  Notify the parse scheduler that the given region was skipped
  because it wasn't in view, and the parse should be restarted
  when it comes into view.
  */
  skipUntilInView(e, t) {
    this.skipped.push({ from: e, to: t });
  }
  /**
  Returns a parser intended to be used as placeholder when
  asynchronously loading a nested parser. It'll skip its input and
  mark it as not-really-parsed, so that the next update will parse
  it again.
  
  When `until` is given, a reparse will be scheduled when that
  promise resolves.
  */
  static getSkippingParser(e) {
    return new class extends nm {
      createParse(t, i, s) {
        let r = s[0].from, o = s[s.length - 1].to;
        return {
          parsedPos: r,
          advance() {
            let a = ki;
            if (a) {
              for (let h of s)
                a.tempSkipped.push(h);
              e && (a.scheduleOn = a.scheduleOn ? Promise.all([a.scheduleOn, e]) : e);
            }
            return this.parsedPos = o, new ue(Re.none, [], [], o - r);
          },
          stoppedAt: null,
          stopAt() {
          }
        };
      }
    }();
  }
  /**
  @internal
  */
  isDone(e) {
    e = Math.min(e, this.state.doc.length);
    let t = this.fragments;
    return this.treeLen >= e && t.length && t[0].from == 0 && t[0].to >= e;
  }
  /**
  Get the context for the current parse, or `null` if no editor
  parse is in progress.
  */
  static get() {
    return ki;
  }
}
function Ul(n, e, t) {
  return Ft.applyChanges(n, [{ fromA: e, toA: t, fromB: e, toB: t }]);
}
class ui {
  constructor(e) {
    this.context = e, this.tree = e.tree;
  }
  apply(e) {
    if (!e.docChanged && this.tree == this.context.tree)
      return this;
    let t = this.context.changes(e.changes, e.state), i = this.context.treeLen == e.startState.doc.length ? void 0 : Math.max(e.changes.mapPos(this.context.treeLen), t.viewport.to);
    return t.work(20, i) || t.takeTree(), new ui(t);
  }
  static init(e) {
    let t = Math.min(3e3, e.doc.length), i = Zn.create(e.facet(At).parser, e, { from: 0, to: t });
    return i.work(20, t) || i.takeTree(), new ui(i);
  }
}
qe.state = /* @__PURE__ */ me.define({
  create: ui.init,
  update(n, e) {
    for (let t of e.effects)
      if (t.is(qe.setState))
        return t.value;
    return e.startState.facet(At) != e.state.facet(At) ? ui.init(e.state) : n.apply(e);
  }
});
let hc = (n) => {
  let e = setTimeout(
    () => n(),
    500
    /* Work.MaxPause */
  );
  return () => clearTimeout(e);
};
typeof requestIdleCallback < "u" && (hc = (n) => {
  let e = -1, t = setTimeout(
    () => {
      e = requestIdleCallback(n, {
        timeout: 400
        /* Work.MinPause */
      });
    },
    100
    /* Work.MinPause */
  );
  return () => e < 0 ? clearTimeout(t) : cancelIdleCallback(e);
});
const Us = typeof navigator < "u" && (!((js = navigator.scheduling) === null || js === void 0) && js.isInputPending) ? () => navigator.scheduling.isInputPending() : null, ym = /* @__PURE__ */ Q.fromClass(class {
  constructor(e) {
    this.view = e, this.working = null, this.workScheduled = 0, this.chunkEnd = -1, this.chunkBudget = -1, this.work = this.work.bind(this), this.scheduleWork();
  }
  update(e) {
    let t = this.view.state.field(qe.state).context;
    (t.updateViewport(e.view.viewport) || this.view.viewport.to > t.treeLen) && this.scheduleWork(), (e.docChanged || e.selectionSet) && (this.view.hasFocus && (this.chunkBudget += 50), this.scheduleWork()), this.checkAsyncSchedule(t);
  }
  scheduleWork() {
    if (this.working)
      return;
    let { state: e } = this.view, t = e.field(qe.state);
    (t.tree != t.context.tree || !t.context.isDone(e.doc.length)) && (this.working = hc(this.work));
  }
  work(e) {
    this.working = null;
    let t = Date.now();
    if (this.chunkEnd < t && (this.chunkEnd < 0 || this.view.hasFocus) && (this.chunkEnd = t + 3e4, this.chunkBudget = 3e3), this.chunkBudget <= 0)
      return;
    let { state: i, viewport: { to: s } } = this.view, r = i.field(qe.state);
    if (r.tree == r.context.tree && r.context.isDone(
      s + 1e5
      /* Work.MaxParseAhead */
    ))
      return;
    let o = Date.now() + Math.min(this.chunkBudget, 100, e && !Us ? Math.max(25, e.timeRemaining() - 5) : 1e9), l = r.context.treeLen < s && i.doc.length > s + 1e3, a = r.context.work(() => Us && Us() || Date.now() > o, s + (l ? 0 : 1e5));
    this.chunkBudget -= Date.now() - t, (a || this.chunkBudget <= 0) && (r.context.takeTree(), this.view.dispatch({ effects: qe.setState.of(new ui(r.context)) })), this.chunkBudget > 0 && !(a && !l) && this.scheduleWork(), this.checkAsyncSchedule(r.context);
  }
  checkAsyncSchedule(e) {
    e.scheduleOn && (this.workScheduled++, e.scheduleOn.then(() => this.scheduleWork()).catch((t) => Ae(this.view.state, t)).then(() => this.workScheduled--), e.scheduleOn = null);
  }
  destroy() {
    this.working && this.working();
  }
  isWorking() {
    return !!(this.working || this.workScheduled > 0);
  }
}, {
  eventHandlers: { focus() {
    this.scheduleWork();
  } }
}), At = /* @__PURE__ */ T.define({
  combine(n) {
    return n.length ? n[0] : null;
  },
  enables: (n) => [
    qe.state,
    ym,
    M.contentAttributes.compute([n], (e) => {
      let t = e.facet(n);
      return t && t.name ? { "data-language": t.name } : {};
    })
  ]
});
class Bb {
  /**
  Create a language support object.
  */
  constructor(e, t = []) {
    this.language = e, this.support = t, this.extension = [e, t];
  }
}
const bm = /* @__PURE__ */ T.define(), nn = /* @__PURE__ */ T.define({
  combine: (n) => {
    if (!n.length)
      return "  ";
    let e = n[0];
    if (!e || /\S/.test(e) || Array.from(e).some((t) => t != e[0]))
      throw new Error("Invalid indent unit: " + JSON.stringify(n[0]));
    return e;
  }
});
function es(n) {
  let e = n.facet(nn);
  return e.charCodeAt(0) == 9 ? n.tabSize * e.length : e.length;
}
function ji(n, e) {
  let t = "", i = n.tabSize, s = n.facet(nn)[0];
  if (s == "	") {
    for (; e >= i; )
      t += "	", e -= i;
    s = " ";
  }
  for (let r = 0; r < e; r++)
    t += s;
  return t;
}
function ko(n, e) {
  n instanceof F && (n = new bs(n));
  for (let i of n.state.facet(bm)) {
    let s = i(n, e);
    if (s !== void 0)
      return s;
  }
  let t = pe(n.state);
  return t.length >= e ? wm(n, t, e) : null;
}
class bs {
  /**
  Create an indent context.
  */
  constructor(e, t = {}) {
    this.state = e, this.options = t, this.unit = es(e);
  }
  /**
  Get a description of the line at the given position, taking
  [simulated line
  breaks](https://codemirror.net/6/docs/ref/#language.IndentContext.constructor^options.simulateBreak)
  into account. If there is such a break at `pos`, the `bias`
  argument determines whether the part of the line line before or
  after the break is used.
  */
  lineAt(e, t = 1) {
    let i = this.state.doc.lineAt(e), { simulateBreak: s, simulateDoubleBreak: r } = this.options;
    return s != null && s >= i.from && s <= i.to ? r && s == e ? { text: "", from: e } : (t < 0 ? s < e : s <= e) ? { text: i.text.slice(s - i.from), from: s } : { text: i.text.slice(0, s - i.from), from: i.from } : i;
  }
  /**
  Get the text directly after `pos`, either the entire line
  or the next 100 characters, whichever is shorter.
  */
  textAfterPos(e, t = 1) {
    if (this.options.simulateDoubleBreak && e == this.options.simulateBreak)
      return "";
    let { text: i, from: s } = this.lineAt(e, t);
    return i.slice(e - s, Math.min(i.length, e + 100 - s));
  }
  /**
  Find the column for the given position.
  */
  column(e, t = 1) {
    let { text: i, from: s } = this.lineAt(e, t), r = this.countColumn(i, e - s), o = this.options.overrideIndentation ? this.options.overrideIndentation(s) : -1;
    return o > -1 && (r += o - this.countColumn(i, i.search(/\S|$/))), r;
  }
  /**
  Find the column position (taking tabs into account) of the given
  position in the given string.
  */
  countColumn(e, t = e.length) {
    return pi(e, this.state.tabSize, t);
  }
  /**
  Find the indentation column of the line at the given point.
  */
  lineIndent(e, t = 1) {
    let { text: i, from: s } = this.lineAt(e, t), r = this.options.overrideIndentation;
    if (r) {
      let o = r(s);
      if (o > -1)
        return o;
    }
    return this.countColumn(i, i.search(/\S|$/));
  }
  /**
  Returns the [simulated line
  break](https://codemirror.net/6/docs/ref/#language.IndentContext.constructor^options.simulateBreak)
  for this context, if any.
  */
  get simulatedBreak() {
    return this.options.simulateBreak || null;
  }
}
const xm = /* @__PURE__ */ new V();
function wm(n, e, t) {
  let i = e.resolveStack(t), s = e.resolveInner(t, -1).resolve(t, 0).enterUnfinishedNodesBefore(t);
  if (s != i.node) {
    let r = [];
    for (let o = s; o && !(o.from < i.node.from || o.to > i.node.to || o.from == i.node.from && o.type == i.node.type); o = o.parent)
      r.push(o);
    for (let o = r.length - 1; o >= 0; o--)
      i = { node: r[o], next: i };
  }
  return cc(i, n, t);
}
function cc(n, e, t) {
  for (let i = n; i; i = i.next) {
    let s = km(i.node);
    if (s)
      return s(So.create(e, t, i));
  }
  return 0;
}
function vm(n) {
  return n.pos == n.options.simulateBreak && n.options.simulateDoubleBreak;
}
function km(n) {
  let e = n.type.prop(xm);
  if (e)
    return e;
  let t = n.firstChild, i;
  if (t && (i = t.type.prop(V.closedBy))) {
    let s = n.lastChild, r = s && i.indexOf(s.name) > -1;
    return (o) => fc(o, !0, 1, void 0, r && !vm(o) ? s.from : void 0);
  }
  return n.parent == null ? Sm : null;
}
function Sm() {
  return 0;
}
class So extends bs {
  constructor(e, t, i) {
    super(e.state, e.options), this.base = e, this.pos = t, this.context = i;
  }
  /**
  The syntax tree node to which the indentation strategy
  applies.
  */
  get node() {
    return this.context.node;
  }
  /**
  @internal
  */
  static create(e, t, i) {
    return new So(e, t, i);
  }
  /**
  Get the text directly after `this.pos`, either the entire line
  or the next 100 characters, whichever is shorter.
  */
  get textAfter() {
    return this.textAfterPos(this.pos);
  }
  /**
  Get the indentation at the reference line for `this.node`, which
  is the line on which it starts, unless there is a node that is
  _not_ a parent of this node covering the start of that line. If
  so, the line at the start of that node is tried, again skipping
  on if it is covered by another such node.
  */
  get baseIndent() {
    return this.baseIndentFor(this.node);
  }
  /**
  Get the indentation for the reference line of the given node
  (see [`baseIndent`](https://codemirror.net/6/docs/ref/#language.TreeIndentContext.baseIndent)).
  */
  baseIndentFor(e) {
    let t = this.state.doc.lineAt(e.from);
    for (; ; ) {
      let i = e.resolve(t.from);
      for (; i.parent && i.parent.from == i.from; )
        i = i.parent;
      if (Cm(i, e))
        break;
      t = this.state.doc.lineAt(i.from);
    }
    return this.lineIndent(t.from);
  }
  /**
  Continue looking for indentations in the node's parent nodes,
  and return the result of that.
  */
  continue() {
    return cc(this.context.next, this.base, this.pos);
  }
}
function Cm(n, e) {
  for (let t = e; t; t = t.parent)
    if (n == t)
      return !0;
  return !1;
}
function Am(n) {
  let e = n.node, t = e.childAfter(e.from), i = e.lastChild;
  if (!t)
    return null;
  let s = n.options.simulateBreak, r = n.state.doc.lineAt(t.from), o = s == null || s <= r.from ? r.to : Math.min(r.to, s);
  for (let l = t.to; ; ) {
    let a = e.childAfter(l);
    if (!a || a == i)
      return null;
    if (!a.type.isSkipped) {
      if (a.from >= o)
        return null;
      let h = /^ */.exec(r.text.slice(t.to - r.from))[0].length;
      return { from: t.from, to: t.to + h };
    }
    l = a.to;
  }
}
function Lb({ closing: n, align: e = !0, units: t = 1 }) {
  return (i) => fc(i, e, t, n);
}
function fc(n, e, t, i, s) {
  let r = n.textAfter, o = r.match(/^\s*/)[0].length, l = i && r.slice(o, o + i.length) == i || s == n.pos + o, a = e ? Am(n) : null;
  return a ? l ? n.column(a.from) : n.column(a.to) : n.baseIndent + (l ? 0 : n.unit * t);
}
const Eb = (n) => n.baseIndent;
function Rb({ except: n, units: e = 1 } = {}) {
  return (t) => {
    let i = n && n.test(t.textAfter);
    return t.baseIndent + (i ? 0 : e * t.unit);
  };
}
const Mm = 200;
function Tm() {
  return F.transactionFilter.of((n) => {
    if (!n.docChanged || !n.isUserEvent("input.type") && !n.isUserEvent("input.complete"))
      return n;
    let e = n.startState.languageDataAt("indentOnInput", n.startState.selection.main.head);
    if (!e.length)
      return n;
    let t = n.newDoc, { head: i } = n.newSelection.main, s = t.lineAt(i);
    if (i > s.from + Mm)
      return n;
    let r = t.sliceString(s.from, i);
    if (!e.some((h) => h.test(r)))
      return n;
    let { state: o } = n, l = -1, a = [];
    for (let { head: h } of o.selection.ranges) {
      let c = o.doc.lineAt(h);
      if (c.from == l)
        continue;
      l = c.from;
      let f = ko(o, c.from);
      if (f == null)
        continue;
      let u = /^\s*/.exec(c.text)[0], d = ji(o, f);
      u != d && a.push({ from: c.from, to: c.from + u.length, insert: d });
    }
    return a.length ? [n, { changes: a, sequential: !0 }] : n;
  });
}
const Dm = /* @__PURE__ */ T.define(), Om = /* @__PURE__ */ new V();
function Pb(n) {
  let e = n.firstChild, t = n.lastChild;
  return e && e.to < t.from ? { from: e.to, to: t.type.isError ? n.to : t.from } : null;
}
function Bm(n, e, t) {
  let i = pe(n);
  if (i.length < t)
    return null;
  let s = i.resolveStack(t, 1), r = null;
  for (let o = s; o; o = o.next) {
    let l = o.node;
    if (l.to <= t || l.from > t)
      continue;
    if (r && l.from < e)
      break;
    let a = l.type.prop(Om);
    if (a && (l.to < i.length - 50 || i.length == n.doc.length || !Lm(l))) {
      let h = a(l, n);
      h && h.from <= t && h.from >= e && h.to > t && (r = h);
    }
  }
  return r;
}
function Lm(n) {
  let e = n.lastChild;
  return e && e.to == n.to && e.type.isError;
}
function ts(n, e, t) {
  for (let i of n.facet(Dm)) {
    let s = i(n, e, t);
    if (s)
      return s;
  }
  return Bm(n, e, t);
}
function uc(n, e) {
  let t = e.mapPos(n.from, 1), i = e.mapPos(n.to, -1);
  return t >= i ? void 0 : { from: t, to: i };
}
const xs = /* @__PURE__ */ R.define({ map: uc }), sn = /* @__PURE__ */ R.define({ map: uc });
function dc(n) {
  let e = [];
  for (let { head: t } of n.state.selection.ranges)
    e.some((i) => i.from <= t && i.to >= t) || e.push(n.lineBlockAt(t));
  return e;
}
const Kt = /* @__PURE__ */ me.define({
  create() {
    return B.none;
  },
  update(n, e) {
    e.isUserEvent("delete") && e.changes.iterChangedRanges((i, s) => n = _l(n, i, s)), n = n.map(e.changes);
    let t = [];
    for (let i of e.effects)
      i.is(xs) && !Em(n, i.value.from, i.value.to) ? t.push(i.value) : i.is(sn) && (n = n.update({
        filter: (s, r) => i.value.from != s || i.value.to != r,
        filterFrom: i.value.from,
        filterTo: i.value.to
      }));
    if (t.length) {
      let { preparePlaceholder: i } = e.state.facet(gc), s = t.map((r) => (i ? B.replace({ widget: new Fm(i(e.state, r)) }) : Gl).range(r.from, r.to));
      n = n.update({ add: s });
    }
    return e.selection && (n = _l(n, e.selection.main.head)), n;
  },
  provide: (n) => M.decorations.from(n),
  toJSON(n, e) {
    let t = [];
    return n.between(0, e.doc.length, (i, s) => {
      t.push(i, s);
    }), t;
  },
  fromJSON(n) {
    if (!Array.isArray(n) || n.length % 2)
      throw new RangeError("Invalid JSON for fold state");
    let e = [];
    for (let t = 0; t < n.length; ) {
      let i = n[t++], s = n[t++];
      if (typeof i != "number" || typeof s != "number")
        throw new RangeError("Invalid JSON for fold state");
      e.push(Gl.range(i, s));
    }
    return B.set(e, !0);
  }
});
function _l(n, e, t = e) {
  let i = !1;
  return n.between(e, t, (s, r) => {
    s < t && r > e && (i = !0);
  }), i ? n.update({
    filterFrom: e,
    filterTo: t,
    filter: (s, r) => s >= t || r <= e
  }) : n;
}
function is(n, e, t) {
  var i;
  let s = null;
  return (i = n.field(Kt, !1)) === null || i === void 0 || i.between(e, t, (r, o) => {
    (!s || s.from > r) && (s = { from: r, to: o });
  }), s;
}
function Em(n, e, t) {
  let i = !1;
  return n.between(e, e, (s, r) => {
    s == e && r == t && (i = !0);
  }), i;
}
function pc(n, e) {
  return n.field(Kt, !1) ? e : e.concat(R.appendConfig.of(yc()));
}
const Rm = (n) => {
  for (let e of dc(n)) {
    let t = ts(n.state, e.from, e.to);
    if (t)
      return n.dispatch({ effects: pc(n.state, [xs.of(t), mc(n, t)]) }), !0;
  }
  return !1;
}, Pm = (n) => {
  if (!n.state.field(Kt, !1))
    return !1;
  let e = [];
  for (let t of dc(n)) {
    let i = is(n.state, t.from, t.to);
    i && e.push(sn.of(i), mc(n, i, !1));
  }
  return e.length && n.dispatch({ effects: e }), e.length > 0;
};
function mc(n, e, t = !0) {
  let i = n.state.doc.lineAt(e.from).number, s = n.state.doc.lineAt(e.to).number;
  return M.announce.of(`${n.state.phrase(t ? "Folded lines" : "Unfolded lines")} ${i} ${n.state.phrase("to")} ${s}.`);
}
const Im = (n) => {
  let { state: e } = n, t = [];
  for (let i = 0; i < e.doc.length; ) {
    let s = n.lineBlockAt(i), r = ts(e, s.from, s.to);
    r && t.push(xs.of(r)), i = (r ? n.lineBlockAt(r.to) : s).to + 1;
  }
  return t.length && n.dispatch({ effects: pc(n.state, t) }), !!t.length;
}, Nm = (n) => {
  let e = n.state.field(Kt, !1);
  if (!e || !e.size)
    return !1;
  let t = [];
  return e.between(0, n.state.doc.length, (i, s) => {
    t.push(sn.of({ from: i, to: s }));
  }), n.dispatch({ effects: t }), !0;
}, Wm = [
  { key: "Ctrl-Shift-[", mac: "Cmd-Alt-[", run: Rm },
  { key: "Ctrl-Shift-]", mac: "Cmd-Alt-]", run: Pm },
  { key: "Ctrl-Alt-[", run: Im },
  { key: "Ctrl-Alt-]", run: Nm }
], Hm = {
  placeholderDOM: null,
  preparePlaceholder: null,
  placeholderText: "…"
}, gc = /* @__PURE__ */ T.define({
  combine(n) {
    return at(n, Hm);
  }
});
function yc(n) {
  return [Kt, qm];
}
function bc(n, e) {
  let { state: t } = n, i = t.facet(gc), s = (o) => {
    let l = n.lineBlockAt(n.posAtDOM(o.target)), a = is(n.state, l.from, l.to);
    a && n.dispatch({ effects: sn.of(a) }), o.preventDefault();
  };
  if (i.placeholderDOM)
    return i.placeholderDOM(n, s, e);
  let r = document.createElement("span");
  return r.textContent = i.placeholderText, r.setAttribute("aria-label", t.phrase("folded code")), r.title = t.phrase("unfold"), r.className = "cm-foldPlaceholder", r.onclick = s, r;
}
const Gl = /* @__PURE__ */ B.replace({ widget: /* @__PURE__ */ new class extends Ke {
  toDOM(n) {
    return bc(n, null);
  }
}() });
class Fm extends Ke {
  constructor(e) {
    super(), this.value = e;
  }
  eq(e) {
    return this.value == e.value;
  }
  toDOM(e) {
    return bc(e, this.value);
  }
}
const Vm = {
  openText: "⌄",
  closedText: "›",
  markerDOM: null,
  domEventHandlers: {},
  foldingChanged: () => !1
};
class _s extends dt {
  constructor(e, t) {
    super(), this.config = e, this.open = t;
  }
  eq(e) {
    return this.config == e.config && this.open == e.open;
  }
  toDOM(e) {
    if (this.config.markerDOM)
      return this.config.markerDOM(this.open);
    let t = document.createElement("span");
    return t.textContent = this.open ? this.config.openText : this.config.closedText, t.title = e.state.phrase(this.open ? "Fold line" : "Unfold line"), t;
  }
}
function zm(n = {}) {
  let e = { ...Vm, ...n }, t = new _s(e, !0), i = new _s(e, !1), s = Q.fromClass(class {
    constructor(o) {
      this.from = o.viewport.from, this.markers = this.buildMarkers(o);
    }
    update(o) {
      (o.docChanged || o.viewportChanged || o.startState.facet(At) != o.state.facet(At) || o.startState.field(Kt, !1) != o.state.field(Kt, !1) || pe(o.startState) != pe(o.state) || e.foldingChanged(o)) && (this.markers = this.buildMarkers(o.view));
    }
    buildMarkers(o) {
      let l = new ft();
      for (let a of o.viewportLineBlocks) {
        let h = is(o.state, a.from, a.to) ? i : ts(o.state, a.from, a.to) ? t : null;
        h && l.add(a.from, a.from, h);
      }
      return l.finish();
    }
  }), { domEventHandlers: r } = e;
  return [
    s,
    Fp({
      class: "cm-foldGutter",
      markers(o) {
        var l;
        return ((l = o.plugin(s)) === null || l === void 0 ? void 0 : l.markers) || W.empty;
      },
      initialSpacer() {
        return new _s(e, !1);
      },
      domEventHandlers: {
        ...r,
        click: (o, l, a) => {
          if (r.click && r.click(o, l, a))
            return !0;
          let h = is(o.state, l.from, l.to);
          if (h)
            return o.dispatch({ effects: sn.of(h) }), !0;
          let c = ts(o.state, l.from, l.to);
          return c ? (o.dispatch({ effects: xs.of(c) }), !0) : !1;
        }
      }
    }),
    yc()
  ];
}
const qm = /* @__PURE__ */ M.baseTheme({
  ".cm-foldPlaceholder": {
    backgroundColor: "#eee",
    border: "1px solid #ddd",
    color: "#888",
    borderRadius: ".2em",
    margin: "0 1px",
    padding: "0 1px",
    cursor: "pointer"
  },
  ".cm-foldGutter span": {
    padding: "0 1px",
    cursor: "pointer"
  }
});
class rn {
  constructor(e, t) {
    this.specs = e;
    let i;
    function s(l) {
      let a = vt.newName();
      return (i || (i = /* @__PURE__ */ Object.create(null)))["." + a] = l, a;
    }
    const r = typeof t.all == "string" ? t.all : t.all ? s(t.all) : void 0, o = t.scope;
    this.scope = o instanceof qe ? (l) => l.prop(Zt) == o.data : o ? (l) => l == o : void 0, this.style = ac(e.map((l) => ({
      tag: l.tag,
      class: l.class || s(Object.assign({}, l, { tag: null }))
    })), {
      all: r
    }).style, this.module = i ? new vt(i) : null, this.themeType = t.themeType;
  }
  /**
  Create a highlighter style that associates the given styles to
  the given tags. The specs must be objects that hold a style tag
  or array of tags in their `tag` property, and either a single
  `class` property providing a static CSS class (for highlighter
  that rely on external styling), or a
  [`style-mod`](https://code.haverbeke.berlin/marijn/style-mod#documentation)-style
  set of CSS properties (which define the styling for those tags).
  
  The CSS rules created for a highlighter will be emitted in the
  order of the spec's properties. That means that for elements that
  have multiple tags associated with them, styles defined further
  down in the list will have a higher CSS precedence than styles
  defined earlier.
  */
  static define(e, t) {
    return new rn(e, t || {});
  }
}
const Kr = /* @__PURE__ */ T.define(), xc = /* @__PURE__ */ T.define({
  combine(n) {
    return n.length ? [n[0]] : null;
  }
});
function Gs(n) {
  let e = n.facet(Kr);
  return e.length ? e : n.facet(xc);
}
function wc(n, e) {
  let t = [Km], i;
  return n instanceof rn && (n.module && t.push(M.styleModule.of(n.module)), i = n.themeType), e?.fallback ? t.push(xc.of(n)) : i ? t.push(Kr.computeN([M.darkTheme], (s) => s.facet(M.darkTheme) == (i == "dark") ? [n] : [])) : t.push(Kr.of(n)), t;
}
class $m {
  constructor(e) {
    this.markCache = /* @__PURE__ */ Object.create(null), this.tree = pe(e.state), this.decorations = this.buildDeco(e, Gs(e.state)), this.decoratedTo = e.viewport.to;
  }
  update(e) {
    let t = pe(e.state), i = Gs(e.state), s = i != Gs(e.startState), { viewport: r } = e.view, o = e.changes.mapPos(this.decoratedTo, 1);
    t.length < r.to && !s && t.type == this.tree.type && o >= r.to ? (this.decorations = this.decorations.map(e.changes), this.decoratedTo = o) : (t != this.tree || e.viewportChanged || s) && (this.tree = t, this.decorations = this.buildDeco(e.view, i), this.decoratedTo = r.to);
  }
  buildDeco(e, t) {
    if (!t || !this.tree.length)
      return B.none;
    let i = new ft();
    for (let { from: s, to: r } of e.visibleRanges)
      fm(this.tree, t, (o, l, a) => {
        i.add(o, l, this.markCache[a] || (this.markCache[a] = B.mark({ class: a })));
      }, s, r);
    return i.finish();
  }
}
const Km = /* @__PURE__ */ Tt.high(/* @__PURE__ */ Q.fromClass($m, {
  decorations: (n) => n.decorations
})), jm = /* @__PURE__ */ rn.define([
  {
    tag: x.meta,
    color: "#404740"
  },
  {
    tag: x.link,
    textDecoration: "underline"
  },
  {
    tag: x.heading,
    textDecoration: "underline",
    fontWeight: "bold"
  },
  {
    tag: x.emphasis,
    fontStyle: "italic"
  },
  {
    tag: x.strong,
    fontWeight: "bold"
  },
  {
    tag: x.strikethrough,
    textDecoration: "line-through"
  },
  {
    tag: x.keyword,
    color: "#708"
  },
  {
    tag: [x.atom, x.bool, x.url, x.contentSeparator, x.labelName],
    color: "#219"
  },
  {
    tag: [x.literal, x.inserted],
    color: "#164"
  },
  {
    tag: [x.string, x.deleted],
    color: "#a11"
  },
  {
    tag: [x.regexp, x.escape, /* @__PURE__ */ x.special(x.string)],
    color: "#e40"
  },
  {
    tag: /* @__PURE__ */ x.definition(x.variableName),
    color: "#00f"
  },
  {
    tag: /* @__PURE__ */ x.local(x.variableName),
    color: "#30a"
  },
  {
    tag: [x.typeName, x.namespace],
    color: "#085"
  },
  {
    tag: x.className,
    color: "#167"
  },
  {
    tag: [/* @__PURE__ */ x.special(x.variableName), x.macroName],
    color: "#256"
  },
  {
    tag: /* @__PURE__ */ x.definition(x.propertyName),
    color: "#00c"
  },
  {
    tag: x.comment,
    color: "#940"
  },
  {
    tag: x.invalid,
    color: "#f00"
  }
]), Um = /* @__PURE__ */ M.baseTheme({
  "&.cm-focused .cm-matchingBracket": { backgroundColor: "#328c8252" },
  "&.cm-focused .cm-nonmatchingBracket": { backgroundColor: "#bb555544" }
}), vc = 1e4, kc = "()[]{}", Sc = /* @__PURE__ */ T.define({
  combine(n) {
    return at(n, {
      afterCursor: !0,
      brackets: kc,
      maxScanDistance: vc,
      renderMatch: Ym
    });
  }
}), _m = /* @__PURE__ */ B.mark({ class: "cm-matchingBracket" }), Gm = /* @__PURE__ */ B.mark({ class: "cm-nonmatchingBracket" });
function Ym(n) {
  let e = [], t = n.matched ? _m : Gm;
  return e.push(t.range(n.start.from, n.start.to)), n.end && e.push(t.range(n.end.from, n.end.to)), e;
}
function Yl(n) {
  let e = [], t = n.facet(Sc);
  for (let i of n.selection.ranges) {
    if (!i.empty)
      continue;
    let s = st(n, i.head, -1, t) || i.head > 0 && st(n, i.head - 1, 1, t) || t.afterCursor && (st(n, i.head, 1, t) || i.head < n.doc.length && st(n, i.head + 1, -1, t));
    s && (e = e.concat(t.renderMatch(s, n)));
  }
  return B.set(e, !0);
}
const Jm = /* @__PURE__ */ Q.fromClass(class {
  constructor(n) {
    this.paused = !1, this.decorations = Yl(n.state);
  }
  update(n) {
    (n.docChanged || n.selectionSet || this.paused) && (n.view.composing ? (this.decorations = this.decorations.map(n.changes), this.paused = !0) : (this.decorations = Yl(n.state), this.paused = !1));
  }
}, {
  decorations: (n) => n.decorations
}), Xm = [
  Jm,
  Um
];
function Qm(n = {}) {
  return [Sc.of(n), Xm];
}
const Zm = /* @__PURE__ */ new V();
function jr(n, e, t) {
  let i = n.prop(e < 0 ? V.openedBy : V.closedBy);
  if (i)
    return i;
  if (n.name.length == 1) {
    let s = t.indexOf(n.name);
    if (s > -1 && s % 2 == (e < 0 ? 1 : 0))
      return [t[s + e]];
  }
  return null;
}
function Ur(n) {
  let e = n.type.prop(Zm);
  return e ? e(n.node) : n;
}
function st(n, e, t, i = {}) {
  let s = i.maxScanDistance || vc, r = i.brackets || kc, o = pe(n), l = o.resolveInner(e, t);
  for (let a = l; a; a = a.parent) {
    let h = jr(a.type, t, r);
    if (h && a.from < a.to) {
      let c = Ur(a);
      if (c && (t > 0 ? e >= c.from && e < c.to : e > c.from && e <= c.to))
        return eg(n, e, t, a, c, h, r);
    }
  }
  return tg(n, e, t, o, l.type, s, r);
}
function eg(n, e, t, i, s, r, o) {
  let l = i.parent, a = { from: s.from, to: s.to }, h = 0, c = l?.cursor();
  if (c && (t < 0 ? c.childBefore(i.from) : c.childAfter(i.to)))
    do
      if (t < 0 ? c.to <= i.from : c.from >= i.to) {
        if (h == 0 && r.indexOf(c.type.name) > -1 && c.from < c.to) {
          let f = Ur(c);
          return { start: a, end: f ? { from: f.from, to: f.to } : void 0, matched: !0 };
        } else if (jr(c.type, t, o))
          h++;
        else if (jr(c.type, -t, o)) {
          if (h == 0) {
            let f = Ur(c);
            return {
              start: a,
              end: f && f.from < f.to ? { from: f.from, to: f.to } : void 0,
              matched: !1
            };
          }
          h--;
        }
      }
    while (t < 0 ? c.prevSibling() : c.nextSibling());
  return { start: a, matched: !1 };
}
function tg(n, e, t, i, s, r, o) {
  if (t < 0 ? !e : e == n.doc.length)
    return null;
  let l = t < 0 ? n.sliceDoc(e - 1, e) : n.sliceDoc(e, e + 1), a = o.indexOf(l);
  if (a < 0 || a % 2 == 0 != t > 0)
    return null;
  let h = { from: t < 0 ? e - 1 : e, to: t > 0 ? e + 1 : e }, c = n.doc.iterRange(e, t > 0 ? n.doc.length : 0), f = 0;
  for (let u = 0; !c.next().done && u <= r; ) {
    let d = c.value;
    t < 0 && (u += d.length);
    let p = e + u * t;
    for (let m = t > 0 ? 0 : d.length - 1, g = t > 0 ? d.length : -1; m != g; m += t) {
      let y = o.indexOf(d[m]);
      if (!(y < 0 || i.resolveInner(p + m, 1).type != s))
        if (y % 2 == 0 == t > 0)
          f++;
        else {
          if (f == 1)
            return { start: h, end: { from: p + m, to: p + m + 1 }, matched: y >> 1 == a >> 1 };
          f--;
        }
    }
    t > 0 && (u += d.length);
  }
  return c.done ? { start: h, matched: !1 } : null;
}
const ig = /* @__PURE__ */ Object.create(null), Jl = [Re.none], Xl = [], Ql = /* @__PURE__ */ Object.create(null), ng = /* @__PURE__ */ Object.create(null);
for (let [n, e] of [
  ["variable", "variableName"],
  ["variable-2", "variableName.special"],
  ["string-2", "string.special"],
  ["def", "variableName.definition"],
  ["tag", "tagName"],
  ["attribute", "attributeName"],
  ["type", "typeName"],
  ["builtin", "variableName.standard"],
  ["qualifier", "modifier"],
  ["error", "invalid"],
  ["header", "heading"],
  ["property", "propertyName"]
])
  ng[n] = /* @__PURE__ */ sg(ig, e);
function Ys(n, e) {
  Xl.indexOf(n) > -1 || (Xl.push(n), console.warn(e));
}
function sg(n, e) {
  let t = [];
  for (let l of e.split(" ")) {
    let a = [];
    for (let h of l.split(".")) {
      let c = n[h] || x[h];
      c ? typeof c == "function" ? a.length ? a = a.map(c) : Ys(h, `Modifier ${h} used at start of tag`) : a.length ? Ys(h, `Tag ${h} used as modifier`) : a = Array.isArray(c) ? c : [c] : Ys(h, `Unknown highlighting tag ${h}`);
    }
    for (let h of a)
      t.push(h);
  }
  if (!t.length)
    return 0;
  let i = e.replace(/ /g, "_"), s = i + " " + t.map((l) => l.id), r = Ql[s];
  if (r)
    return r.id;
  let o = Ql[s] = Re.define({
    id: Jl.length,
    name: i,
    props: [hm({ [i]: t })]
  });
  return Jl.push(o), o.id;
}
_.RTL, _.LTR;
const rg = (n) => {
  let { state: e } = n, t = e.doc.lineAt(e.selection.main.from), i = Ao(n.state, t.from);
  return i.line ? og(n) : i.block ? ag(n) : !1;
};
function Co(n, e) {
  return ({ state: t, dispatch: i }) => {
    if (t.readOnly)
      return !1;
    let s = n(e, t);
    return s ? (i(t.update(s)), !0) : !1;
  };
}
const og = /* @__PURE__ */ Co(
  fg,
  0
  /* CommentOption.Toggle */
), lg = /* @__PURE__ */ Co(
  Cc,
  0
  /* CommentOption.Toggle */
), ag = /* @__PURE__ */ Co(
  (n, e) => Cc(n, e, cg(e)),
  0
  /* CommentOption.Toggle */
);
function Ao(n, e) {
  let t = n.languageDataAt("commentTokens", e, 1);
  return t.length ? t[0] : {};
}
const Si = 50;
function hg(n, { open: e, close: t }, i, s) {
  let r = n.sliceDoc(i - Si, i), o = n.sliceDoc(s, s + Si), l = /\s*$/.exec(r)[0].length, a = /^\s*/.exec(o)[0].length, h = r.length - l;
  if (r.slice(h - e.length, h) == e && o.slice(a, a + t.length) == t)
    return {
      open: { pos: i - l, margin: l && 1 },
      close: { pos: s + a, margin: a && 1 }
    };
  let c, f;
  s - i <= 2 * Si ? c = f = n.sliceDoc(i, s) : (c = n.sliceDoc(i, i + Si), f = n.sliceDoc(s - Si, s));
  let u = /^\s*/.exec(c)[0].length, d = /\s*$/.exec(f)[0].length, p = f.length - d - t.length;
  return c.slice(u, u + e.length) == e && f.slice(p, p + t.length) == t ? {
    open: {
      pos: i + u + e.length,
      margin: /\s/.test(c.charAt(u + e.length)) ? 1 : 0
    },
    close: {
      pos: s - d - t.length,
      margin: /\s/.test(f.charAt(p - 1)) ? 1 : 0
    }
  } : null;
}
function cg(n) {
  let e = [];
  for (let t of n.selection.ranges) {
    let i = n.doc.lineAt(t.from), s = t.to <= i.to ? i : n.doc.lineAt(t.to);
    s.from > i.from && s.from == t.to && (s = t.to == i.to + 1 ? i : n.doc.lineAt(t.to - 1));
    let r = e.length - 1;
    r >= 0 && e[r].to > i.from ? e[r].to = s.to : e.push({ from: i.from + /^\s*/.exec(i.text)[0].length, to: s.to });
  }
  return e;
}
function Cc(n, e, t = e.selection.ranges) {
  let i = t.map((r) => Ao(e, r.from).block);
  if (!i.every((r) => r))
    return null;
  let s = t.map((r, o) => hg(e, i[o], r.from, r.to));
  if (n != 2 && !s.every((r) => r))
    return { changes: e.changes(t.map((r, o) => s[o] ? [] : [{ from: r.from, insert: i[o].open + " " }, { from: r.to, insert: " " + i[o].close }])) };
  if (n != 1 && s.some((r) => r)) {
    let r = [];
    for (let o = 0, l; o < s.length; o++)
      if (l = s[o]) {
        let a = i[o], { open: h, close: c } = l;
        r.push({ from: h.pos - a.open.length, to: h.pos + h.margin }, { from: c.pos - c.margin, to: c.pos + a.close.length });
      }
    return { changes: r };
  }
  return null;
}
function fg(n, e, t = e.selection.ranges) {
  let i = [], s = -1;
  e: for (let { from: r, to: o } of t) {
    let l = i.length, a = 1e9, h;
    for (let c = r; c <= o; ) {
      let f = e.doc.lineAt(c);
      if (h == null && (h = Ao(e, f.from).line, !h))
        continue e;
      if (f.from > s && (r == o || o > f.from)) {
        s = f.from;
        let u = /^\s*/.exec(f.text)[0].length, d = u == f.length, p = f.text.slice(u, u + h.length) == h ? u : -1;
        u < f.text.length && u < a && (a = u), i.push({ line: f, comment: p, token: h, indent: u, empty: d, single: !1 });
      }
      c = f.to + 1;
    }
    if (a < 1e9)
      for (let c = l; c < i.length; c++)
        i[c].indent < i[c].line.text.length && (i[c].indent = a);
    i.length == l + 1 && (i[l].single = !0);
  }
  if (n != 2 && i.some((r) => r.comment < 0 && (!r.empty || r.single))) {
    let r = [];
    for (let { line: l, token: a, indent: h, empty: c, single: f } of i)
      (f || !c) && r.push({ from: l.from + h, insert: a + " " });
    let o = e.changes(r);
    return { changes: o, selection: e.selection.map(o, 1) };
  } else if (n != 1 && i.some((r) => r.comment >= 0)) {
    let r = [];
    for (let { line: o, comment: l, token: a } of i)
      if (l >= 0) {
        let h = o.from + l, c = h + a.length;
        o.text[c - o.from] == " " && c++, r.push({ from: h, to: c });
      }
    return { changes: r };
  }
  return null;
}
const _r = /* @__PURE__ */ lt.define(), ug = /* @__PURE__ */ lt.define(), dg = /* @__PURE__ */ T.define(), Ac = /* @__PURE__ */ T.define({
  combine(n) {
    return at(n, {
      minDepth: 100,
      newGroupDelay: 500,
      joinToEvent: (e, t) => t
    }, {
      minDepth: Math.max,
      newGroupDelay: Math.min,
      joinToEvent: (e, t) => (i, s) => e(i, s) || t(i, s)
    });
  }
}), Mc = /* @__PURE__ */ me.define({
  create() {
    return rt.empty;
  },
  update(n, e) {
    let t = e.state.facet(Ac), i = e.annotation(_r);
    if (i) {
      let a = Me.fromTransaction(e, i.selection), h = i.side, c = h == 0 ? n.undone : n.done;
      return a ? c = ns(c, c.length, t.minDepth, a) : c = Oc(c, e.startState.selection), new rt(h == 0 ? i.rest : c, h == 0 ? c : i.rest);
    }
    let s = e.annotation(ug);
    if ((s == "full" || s == "before") && (n = n.isolate()), e.annotation(ie.addToHistory) === !1)
      return e.changes.empty ? n : n.addMapping(e.changes.desc);
    let r = Me.fromTransaction(e), o = e.annotation(ie.time), l = e.annotation(ie.userEvent);
    return r ? n = n.addChanges(r, o, l, t, e) : e.selection && (n = n.addSelection(e.startState.selection, o, l, t.newGroupDelay)), (s == "full" || s == "after") && (n = n.isolate()), n;
  },
  toJSON(n) {
    return { done: n.done.map((e) => e.toJSON()), undone: n.undone.map((e) => e.toJSON()) };
  },
  fromJSON(n) {
    return new rt(n.done.map(Me.fromJSON), n.undone.map(Me.fromJSON));
  }
});
function pg(n = {}) {
  return [
    Mc,
    Ac.of(n),
    M.domEventHandlers({
      beforeinput(e, t) {
        let i = e.inputType == "historyUndo" ? Tc : e.inputType == "historyRedo" ? Gr : null;
        return i ? (e.preventDefault(), i(t)) : !1;
      }
    })
  ];
}
function ws(n, e) {
  return function({ state: t, dispatch: i }) {
    if (!e && t.readOnly)
      return !1;
    let s = t.field(Mc, !1);
    if (!s)
      return !1;
    let r = s.pop(n, t, e);
    return r ? (i(r), !0) : !1;
  };
}
const Tc = /* @__PURE__ */ ws(0, !1), Gr = /* @__PURE__ */ ws(1, !1), mg = /* @__PURE__ */ ws(0, !0), gg = /* @__PURE__ */ ws(1, !0);
class Me {
  constructor(e, t, i, s, r) {
    this.changes = e, this.effects = t, this.mapped = i, this.startSelection = s, this.selectionsAfter = r;
  }
  setSelAfter(e) {
    return new Me(this.changes, this.effects, this.mapped, this.startSelection, e);
  }
  toJSON() {
    var e, t, i;
    return {
      changes: (e = this.changes) === null || e === void 0 ? void 0 : e.toJSON(),
      mapped: (t = this.mapped) === null || t === void 0 ? void 0 : t.toJSON(),
      startSelection: (i = this.startSelection) === null || i === void 0 ? void 0 : i.toJSON(),
      selectionsAfter: this.selectionsAfter.map((s) => s.toJSON())
    };
  }
  static fromJSON(e) {
    return new Me(e.changes && ne.fromJSON(e.changes), [], e.mapped && ot.fromJSON(e.mapped), e.startSelection && b.fromJSON(e.startSelection), e.selectionsAfter.map(b.fromJSON));
  }
  // This does not check `addToHistory` and such, it assumes the
  // transaction needs to be converted to an item. Returns null when
  // there are no changes or effects in the transaction.
  static fromTransaction(e, t) {
    let i = Ne;
    for (let s of e.startState.facet(dg)) {
      let r = s(e);
      r.length && (i = i.concat(r));
    }
    return !i.length && e.changes.empty ? null : new Me(e.changes.invert(e.startState.doc), i, void 0, t || e.startState.selection, Ne);
  }
  static selection(e) {
    return new Me(void 0, Ne, void 0, void 0, e);
  }
}
function ns(n, e, t, i) {
  let s = e + 1 > t + 20 ? e - t - 1 : 0, r = n.slice(s, e);
  return r.push(i), r;
}
function yg(n, e) {
  let t = [], i = !1;
  return n.iterChangedRanges((s, r) => t.push(s, r)), e.iterChangedRanges((s, r, o, l) => {
    for (let a = 0; a < t.length; ) {
      let h = t[a++], c = t[a++];
      l >= h && o <= c && (i = !0);
    }
  }), i;
}
function bg(n, e) {
  return n.ranges.length == e.ranges.length && n.ranges.filter((t, i) => t.empty != e.ranges[i].empty).length === 0;
}
function Dc(n, e) {
  return n.length ? e.length ? n.concat(e) : n : e;
}
const Ne = [], xg = 200;
function Oc(n, e) {
  if (n.length) {
    let t = n[n.length - 1], i = t.selectionsAfter.slice(Math.max(0, t.selectionsAfter.length - xg));
    return i.length && i[i.length - 1].eq(e) ? n : (i.push(e), ns(n, n.length - 1, 1e9, t.setSelAfter(i)));
  } else
    return [Me.selection([e])];
}
function wg(n) {
  let e = n[n.length - 1], t = n.slice();
  return t[n.length - 1] = e.setSelAfter(e.selectionsAfter.slice(0, e.selectionsAfter.length - 1)), t;
}
function Js(n, e) {
  if (!n.length)
    return n;
  let t = n.length, i = Ne;
  for (; t; ) {
    let s = vg(n[t - 1], e, i);
    if (s.changes && !s.changes.empty || s.effects.length) {
      let r = n.slice(0, t);
      return r[t - 1] = s, r;
    } else
      e = s.mapped, t--, i = s.selectionsAfter;
  }
  return i.length ? [Me.selection(i)] : Ne;
}
function vg(n, e, t) {
  let i = Dc(n.selectionsAfter.length ? n.selectionsAfter.map((l) => l.map(e)) : Ne, t);
  if (!n.changes)
    return Me.selection(i);
  let s = n.changes.map(e), r = e.mapDesc(n.changes, !0), o = n.mapped ? n.mapped.composeDesc(r) : r;
  return new Me(s, R.mapEffects(n.effects, e), o, n.startSelection.map(r), i);
}
const kg = /^(input\.type|delete)($|\.)/;
class rt {
  constructor(e, t, i = 0, s = void 0) {
    this.done = e, this.undone = t, this.prevTime = i, this.prevUserEvent = s;
  }
  isolate() {
    return this.prevTime ? new rt(this.done, this.undone) : this;
  }
  addChanges(e, t, i, s, r) {
    let o = this.done, l = o[o.length - 1];
    return l && l.changes && !l.changes.empty && e.changes && (!i || kg.test(i)) && (!l.selectionsAfter.length && t - this.prevTime < s.newGroupDelay && s.joinToEvent(r, yg(l.changes, e.changes)) || // For compose (but not compose.start) events, always join with previous event
    i == "input.type.compose") ? o = ns(o, o.length - 1, s.minDepth, new Me(e.changes.compose(l.changes), Dc(R.mapEffects(e.effects, l.changes), l.effects), l.mapped, l.startSelection, Ne)) : o = ns(o, o.length, s.minDepth, e), new rt(o, Ne, t, i);
  }
  addSelection(e, t, i, s) {
    let r = this.done.length ? this.done[this.done.length - 1].selectionsAfter : Ne;
    return r.length > 0 && t - this.prevTime < s && i == this.prevUserEvent && i && /^select($|\.)/.test(i) && bg(r[r.length - 1], e) ? this : new rt(Oc(this.done, e), this.undone, t, i);
  }
  addMapping(e) {
    return new rt(Js(this.done, e), Js(this.undone, e), this.prevTime, this.prevUserEvent);
  }
  pop(e, t, i) {
    let s = e == 0 ? this.done : this.undone;
    if (s.length == 0)
      return null;
    let r = s[s.length - 1], o = r.selectionsAfter[0] || (r.startSelection ? r.startSelection.map(r.changes.invertedDesc, 1) : t.selection);
    if (i && r.selectionsAfter.length)
      return t.update({
        selection: r.selectionsAfter[r.selectionsAfter.length - 1],
        annotations: _r.of({ side: e, rest: wg(s), selection: o }),
        userEvent: e == 0 ? "select.undo" : "select.redo",
        scrollIntoView: !0
      });
    if (r.changes) {
      let l = s.length == 1 ? Ne : s.slice(0, s.length - 1);
      return r.mapped && (l = Js(l, r.mapped)), t.update({
        changes: r.changes,
        selection: r.startSelection,
        effects: r.effects,
        annotations: _r.of({ side: e, rest: l, selection: o }),
        filter: !1,
        userEvent: e == 0 ? "undo" : "redo",
        scrollIntoView: !0
      });
    } else
      return null;
  }
}
rt.empty = /* @__PURE__ */ new rt(Ne, Ne);
const Sg = [
  { key: "Mod-z", run: Tc, preventDefault: !0 },
  { key: "Mod-y", mac: "Mod-Shift-z", run: Gr, preventDefault: !0 },
  { linux: "Ctrl-Shift-z", run: Gr, preventDefault: !0 },
  { key: "Mod-u", run: mg, preventDefault: !0 },
  { key: "Alt-u", mac: "Mod-Shift-u", run: gg, preventDefault: !0 }
];
function mi(n, e) {
  return b.create(n.ranges.map(e), n.mainIndex);
}
function je(n, e) {
  return n.update({ selection: e, scrollIntoView: !0, userEvent: "select" });
}
function Ue({ state: n, dispatch: e }, t) {
  let i = mi(n.selection, t);
  return i.eq(n.selection, !0) ? !1 : (e(je(n, i)), !0);
}
function vs(n, e) {
  return b.cursor(e ? n.to : n.from);
}
function Bc(n, e) {
  return Ue(n, (t) => t.empty ? n.moveByChar(t, e) : vs(t, e));
}
function ye(n) {
  return n.textDirectionAt(n.state.selection.main.head) == _.LTR;
}
const Lc = (n) => Bc(n, !ye(n)), Ec = (n) => Bc(n, ye(n));
function Rc(n, e) {
  return Ue(n, (t) => t.empty ? n.moveByGroup(t, e) : vs(t, e));
}
const Cg = (n) => Rc(n, !ye(n)), Ag = (n) => Rc(n, ye(n));
function Mg(n, e, t) {
  if (e.type.prop(t))
    return !0;
  let i = e.to - e.from;
  return i && (i > 2 || /[^\s,.;:]/.test(n.sliceDoc(e.from, e.to))) || e.firstChild;
}
function ks(n, e, t) {
  let i = pe(n).resolveInner(e.head), s = t ? V.closedBy : V.openedBy;
  for (let a = e.head; ; ) {
    let h = t ? i.childAfter(a) : i.childBefore(a);
    if (!h)
      break;
    Mg(n, h, s) ? i = h : a = t ? h.to : h.from;
  }
  let r = i.type.prop(s), o, l;
  return r && (o = t ? st(n, i.from, 1) : st(n, i.to, -1)) && o.matched ? l = t ? o.end.to : o.end.from : l = t ? i.to : i.from, b.cursor(l, t ? -1 : 1);
}
const Tg = (n) => Ue(n, (e) => ks(n.state, e, !ye(n))), Dg = (n) => Ue(n, (e) => ks(n.state, e, ye(n)));
function Pc(n, e) {
  return Ue(n, (t) => {
    if (!t.empty)
      return vs(t, e);
    let i = n.moveVertically(t, e);
    return i.head != t.head ? i : n.moveToLineBoundary(t, e);
  });
}
const Ic = (n) => Pc(n, !1), Nc = (n) => Pc(n, !0);
function Wc(n) {
  let e = n.scrollDOM.clientHeight < n.scrollDOM.scrollHeight - 2, t = 0, i = 0, s;
  if (e) {
    for (let r of n.state.facet(M.scrollMargins)) {
      let o = r(n);
      o?.top && (t = Math.max(o?.top, t)), o?.bottom && (i = Math.max(o?.bottom, i));
    }
    s = n.scrollDOM.clientHeight - t - i;
  } else
    s = (n.dom.ownerDocument.defaultView || window).innerHeight;
  return {
    marginTop: t,
    marginBottom: i,
    selfScroll: e,
    height: Math.max(n.defaultLineHeight, s - 5)
  };
}
function Hc(n, e) {
  let t = Wc(n), { state: i } = n, s = mi(i.selection, (o) => o.empty ? n.moveVertically(o, e, t.height) : vs(o, e));
  if (s.eq(i.selection))
    return !1;
  let r;
  if (t.selfScroll) {
    let o = n.coordsAtPos(i.selection.main.head), l = n.scrollDOM.getBoundingClientRect(), a = l.top + t.marginTop, h = l.bottom - t.marginBottom;
    o && o.top > a && o.bottom < h && (r = M.scrollIntoView(s.main.head, { y: "start", yMargin: o.top - a }));
  }
  return n.dispatch(je(i, s), { effects: r }), !0;
}
const Zl = (n) => Hc(n, !1), Yr = (n) => Hc(n, !0);
function Dt(n, e, t) {
  let i = n.lineBlockAt(e.head), s = n.moveToLineBoundary(e, t);
  if (s.head == e.head && s.head != (t ? i.to : i.from) && (s = n.moveToLineBoundary(e, t, !1)), !t && s.head == i.from && i.length) {
    let r = /^\s*/.exec(n.state.sliceDoc(i.from, Math.min(i.from + 100, i.to)))[0].length;
    r && e.head != i.from + r && (s = b.cursor(i.from + r));
  }
  return s;
}
const Og = (n) => Ue(n, (e) => Dt(n, e, !0)), Bg = (n) => Ue(n, (e) => Dt(n, e, !1)), Lg = (n) => Ue(n, (e) => Dt(n, e, !ye(n))), Eg = (n) => Ue(n, (e) => Dt(n, e, ye(n))), Rg = (n) => Ue(n, (e) => b.cursor(n.lineBlockAt(e.head).from, 1)), Pg = (n) => Ue(n, (e) => b.cursor(n.lineBlockAt(e.head).to, -1));
function Ig(n, e, t) {
  let i = !1, s = mi(n.selection, (r) => {
    let o = st(n, r.head, -1) || st(n, r.head, 1) || r.head > 0 && st(n, r.head - 1, 1) || r.head < n.doc.length && st(n, r.head + 1, -1);
    if (!o || !o.end)
      return r;
    i = !0;
    let l = o.start.from == r.head ? o.end.to : o.end.from;
    return b.cursor(l);
  });
  return i ? (e(je(n, s)), !0) : !1;
}
const Ng = ({ state: n, dispatch: e }) => Ig(n, e);
function He(n, e, t) {
  let i = mi(n.state.selection, (s) => {
    s.undirectional && s.head >= s.anchor != e && (s = b.range(s.head, s.anchor));
    let r = t(s);
    return b.range(s.anchor, r.head, r.goalColumn, r.bidiLevel || void 0, r.assoc);
  });
  return i.eq(n.state.selection) ? !1 : (n.dispatch(je(n.state, i)), !0);
}
function Fc(n, e) {
  return He(n, e, (t) => n.moveByChar(t, e));
}
const Vc = (n) => Fc(n, !ye(n)), zc = (n) => Fc(n, ye(n));
function qc(n, e) {
  return He(n, e, (t) => n.moveByGroup(t, e));
}
const Wg = (n) => qc(n, !ye(n)), Hg = (n) => qc(n, ye(n)), Fg = (n) => {
  let e = !ye(n);
  return He(n, e, (t) => ks(n.state, t, e));
}, Vg = (n) => {
  let e = ye(n);
  return He(n, e, (t) => ks(n.state, t, e));
};
function $c(n, e) {
  return He(n, e, (t) => n.moveVertically(t, e));
}
const Kc = (n) => $c(n, !1), jc = (n) => $c(n, !0);
function Uc(n, e) {
  return He(n, e, (t) => n.moveVertically(t, e, Wc(n).height));
}
const ea = (n) => Uc(n, !1), ta = (n) => Uc(n, !0), zg = (n) => He(n, !0, (e) => Dt(n, e, !0)), qg = (n) => He(n, !1, (e) => Dt(n, e, !1)), $g = (n) => {
  let e = !ye(n);
  return He(n, e, (t) => Dt(n, t, e));
}, Kg = (n) => {
  let e = ye(n);
  return He(n, e, (t) => Dt(n, t, e));
}, jg = (n) => He(n, !1, (e) => b.cursor(n.lineBlockAt(e.head).from)), Ug = (n) => He(n, !0, (e) => b.cursor(n.lineBlockAt(e.head).to)), ia = ({ state: n, dispatch: e }) => (e(je(n, { anchor: 0 })), !0), na = ({ state: n, dispatch: e }) => (e(je(n, { anchor: n.doc.length })), !0), sa = ({ state: n, dispatch: e }) => (e(je(n, { anchor: n.selection.main.anchor, head: 0 })), !0), ra = ({ state: n, dispatch: e }) => (e(je(n, { anchor: n.selection.main.anchor, head: n.doc.length })), !0), _g = ({ state: n, dispatch: e }) => (e(n.update({ selection: { anchor: 0, head: n.doc.length }, userEvent: "select" })), !0), Gg = ({ state: n, dispatch: e }) => {
  let t = Ss(n).map(({ from: i, to: s }) => b.range(i, Math.min(s + 1, n.doc.length)));
  return e(n.update({ selection: b.create(t), userEvent: "select" })), !0;
}, Yg = ({ state: n, dispatch: e }) => {
  let t = mi(n.selection, (i) => {
    let s = pe(n), r = s.resolveStack(i.from, 1);
    if (i.empty) {
      let o = s.resolveStack(i.from, -1);
      o.node.from >= r.node.from && o.node.to <= r.node.to && (r = o);
    }
    for (let o = r; o; o = o.next) {
      let { node: l } = o;
      if ((l.from < i.from && l.to >= i.to || l.to > i.to && l.from <= i.from) && o.next)
        return b.range(l.to, l.from);
    }
    return i;
  });
  return t.eq(n.selection) ? !1 : (e(je(n, t)), !0);
};
function _c(n, e) {
  let { state: t } = n, i = t.selection, s = t.selection.ranges.slice();
  for (let r of t.selection.ranges) {
    let o = t.doc.lineAt(r.head);
    if (e ? o.to < n.state.doc.length : o.from > 0)
      for (let l = r; ; ) {
        let a = n.moveVertically(l, e);
        if (a.head < o.from || a.head > o.to) {
          s.some((h) => h.head == a.head) || s.push(a);
          break;
        } else {
          if (a.head == l.head)
            break;
          l = a;
        }
      }
  }
  return s.length == i.ranges.length ? !1 : (n.dispatch(je(t, b.create(s, s.length - 1))), !0);
}
const Jg = (n) => _c(n, !1), Xg = (n) => _c(n, !0), Qg = ({ state: n, dispatch: e }) => {
  let t = n.selection, i = null;
  return t.ranges.length > 1 ? i = b.create([t.main]) : t.main.empty || (i = b.create([b.cursor(t.main.head)])), i ? (e(je(n, i)), !0) : !1;
};
function on(n, e) {
  if (n.state.readOnly)
    return !1;
  let t = "delete.selection", { state: i } = n, s = i.changeByRange((r) => {
    let { from: o, to: l } = r;
    if (o == l) {
      let a = e(r);
      a < o ? (t = "delete.backward", a = Dn(n, a, !1)) : a > o && (t = "delete.forward", a = Dn(n, a, !0)), o = Math.min(o, a), l = Math.max(l, a);
    } else
      o = Dn(n, o, !1), l = Dn(n, l, !0);
    return o == l ? { range: r } : { changes: { from: o, to: l }, range: b.cursor(o, o < r.head ? -1 : 1) };
  });
  return s.changes.empty ? !1 : (n.dispatch(i.update(s, {
    scrollIntoView: !0,
    userEvent: t,
    effects: t == "delete.selection" ? M.announce.of(i.phrase("Selection deleted")) : void 0
  })), !0);
}
function Dn(n, e, t) {
  if (n instanceof M)
    for (let i of n.state.facet(M.atomicRanges).map((s) => s(n)))
      i.between(e, e, (s, r) => {
        s < e && r > e && (e = t ? r : s);
      });
  return e;
}
const Gc = (n, e, t) => on(n, (i) => {
  let s = i.from, { state: r } = n, o = r.doc.lineAt(s), l, a;
  if (t && !e && s > o.from && s < o.from + 200 && !/[^ \t]/.test(l = o.text.slice(0, s - o.from))) {
    if (l[l.length - 1] == "	")
      return s - 1;
    let h = pi(l, r.tabSize), c = h % es(r) || es(r);
    for (let f = 0; f < c && l[l.length - 1 - f] == " "; f++)
      s--;
    a = s;
  } else
    a = oe(o.text, s - o.from, e, e) + o.from, a == s && o.number != (e ? r.doc.lines : 1) ? a += e ? 1 : -1 : !e && /[\ufe00-\ufe0f]/.test(o.text.slice(a - o.from, s - o.from)) && (a = oe(o.text, a - o.from, !1, !1) + o.from);
  return a;
}), Jr = (n) => Gc(n, !1, !0), Yc = (n) => Gc(n, !0, !1), Jc = (n, e) => on(n, (t) => {
  let i = t.head, { state: s } = n, r = s.doc.lineAt(i), o = s.charCategorizer(i);
  for (let l = null; ; ) {
    if (i == (e ? r.to : r.from)) {
      i == t.head && r.number != (e ? s.doc.lines : 1) && (i += e ? 1 : -1);
      break;
    }
    let a = oe(r.text, i - r.from, e) + r.from, h = r.text.slice(Math.min(i, a) - r.from, Math.max(i, a) - r.from), c = o(h);
    if (l != null && c != l)
      break;
    (h != " " || i != t.head) && (l = c), i = a;
  }
  return i;
}), Xc = (n) => Jc(n, !1), Zg = (n) => Jc(n, !0), e0 = (n) => on(n, (e) => {
  let t = n.lineBlockAt(e.head).to;
  return e.head < t ? t : Math.min(n.state.doc.length, e.head + 1);
}), t0 = (n) => on(n, (e) => {
  let t = n.moveToLineBoundary(e, !1).head;
  return e.head > t ? t : Math.max(0, e.head - 1);
}), i0 = (n) => on(n, (e) => {
  let t = n.moveToLineBoundary(e, !0).head;
  return e.head < t ? t : Math.min(n.state.doc.length, e.head + 1);
}), n0 = ({ state: n, dispatch: e }) => {
  if (n.readOnly)
    return !1;
  let t = n.changeByRange((i) => ({
    changes: { from: i.from, to: i.to, insert: z.of(["", ""]) },
    range: b.cursor(i.from)
  }));
  return e(n.update(t, { scrollIntoView: !0, userEvent: "input" })), !0;
}, s0 = ({ state: n, dispatch: e }) => {
  if (n.readOnly)
    return !1;
  let t = n.changeByRange((i) => {
    if (!i.empty || i.from == 0 || i.from == n.doc.length)
      return { range: i };
    let s = i.from, r = n.doc.lineAt(s), o = s == r.from ? s - 1 : oe(r.text, s - r.from, !1) + r.from, l = s == r.to ? s + 1 : oe(r.text, s - r.from, !0) + r.from;
    return {
      changes: { from: o, to: l, insert: n.doc.slice(s, l).append(n.doc.slice(o, s)) },
      range: b.cursor(l)
    };
  });
  return t.changes.empty ? !1 : (e(n.update(t, { scrollIntoView: !0, userEvent: "move.character" })), !0);
};
function Ss(n) {
  let e = [], t = -1;
  for (let i of n.selection.ranges) {
    let s = n.doc.lineAt(i.from), r = n.doc.lineAt(i.to);
    if (!i.empty && i.to == r.from && (r = n.doc.lineAt(i.to - 1)), t >= s.number) {
      let o = e[e.length - 1];
      o.to = r.to, o.ranges.push(i);
    } else
      e.push({ from: s.from, to: r.to, ranges: [i] });
    t = r.number + 1;
  }
  return e;
}
function Qc(n, e, t) {
  if (n.readOnly)
    return !1;
  let i = [], s = [];
  for (let r of Ss(n)) {
    if (t ? r.to == n.doc.length : r.from == 0)
      continue;
    let o = n.doc.lineAt(t ? r.to + 1 : r.from - 1), l = o.length + 1;
    if (t) {
      i.push({ from: r.to, to: o.to }, { from: r.from, insert: o.text + n.lineBreak });
      for (let a of r.ranges)
        s.push(b.range(Math.min(n.doc.length, a.anchor + l), Math.min(n.doc.length, a.head + l)));
    } else {
      i.push({ from: o.from, to: r.from }, { from: r.to, insert: n.lineBreak + o.text });
      for (let a of r.ranges)
        s.push(b.range(a.anchor - l, a.head - l));
    }
  }
  return i.length ? (e(n.update({
    changes: i,
    scrollIntoView: !0,
    selection: b.create(s, n.selection.mainIndex),
    userEvent: "move.line"
  })), !0) : !1;
}
const r0 = ({ state: n, dispatch: e }) => Qc(n, e, !1), o0 = ({ state: n, dispatch: e }) => Qc(n, e, !0);
function Zc(n, e, t) {
  if (n.readOnly)
    return !1;
  let i = [];
  for (let r of Ss(n))
    t ? i.push({ from: r.from, insert: n.doc.slice(r.from, r.to) + n.lineBreak }) : i.push({ from: r.to, insert: n.lineBreak + n.doc.slice(r.from, r.to) });
  let s = n.changes(i);
  return e(n.update({
    changes: s,
    selection: n.selection.map(s, t ? 1 : -1),
    scrollIntoView: !0,
    userEvent: "input.copyline"
  })), !0;
}
const l0 = ({ state: n, dispatch: e }) => Zc(n, e, !1), a0 = ({ state: n, dispatch: e }) => Zc(n, e, !0), h0 = (n) => {
  if (n.state.readOnly)
    return !1;
  let { state: e } = n, t = e.changes(Ss(e).map(({ from: s, to: r }) => (s > 0 ? s-- : r < e.doc.length && r++, { from: s, to: r }))), i = mi(e.selection, (s) => {
    let r;
    if (n.lineWrapping) {
      let o = n.lineBlockAt(s.head), l = n.coordsAtPos(s.head, s.assoc || 1);
      l && (r = o.bottom + n.documentTop - l.bottom + n.defaultLineHeight / 2);
    }
    return n.moveVertically(s, !0, r);
  }).map(t);
  return n.dispatch({ changes: t, selection: i, scrollIntoView: !0, userEvent: "delete.line" }), !0;
};
function c0(n, e) {
  if (/\(\)|\[\]|\{\}/.test(n.sliceDoc(e - 1, e + 1)))
    return { from: e, to: e };
  let t = pe(n).resolveInner(e), i = t.childBefore(e), s = t.childAfter(e), r;
  return i && s && i.to <= e && s.from >= e && (r = i.type.prop(V.closedBy)) && r.indexOf(s.name) > -1 && n.doc.lineAt(i.to).from == n.doc.lineAt(s.from).from && !/\S/.test(n.sliceDoc(i.to, s.from)) ? { from: i.to, to: s.from } : null;
}
const oa = /* @__PURE__ */ ef(!1), f0 = /* @__PURE__ */ ef(!0);
function ef(n) {
  return ({ state: e, dispatch: t }) => {
    if (e.readOnly)
      return !1;
    let i = e.changeByRange((s) => {
      let { from: r, to: o } = s, l = e.doc.lineAt(r), a = !n && r == o && c0(e, r);
      n && (r = o = (o <= l.to ? l : e.doc.lineAt(o)).to);
      let h = new bs(e, { simulateBreak: r, simulateDoubleBreak: !!a }), c = ko(h, r);
      for (c == null && (c = pi(/^\s*/.exec(e.doc.lineAt(r).text)[0], e.tabSize)); o < l.to && /\s/.test(l.text[o - l.from]); )
        o++;
      a ? { from: r, to: o } = a : r > l.from && r < l.from + 100 && !/\S/.test(l.text.slice(0, r)) && (r = l.from);
      let f = ["", ji(e, c)];
      return a && f.push(ji(e, h.lineIndent(l.from, -1))), {
        changes: { from: r, to: o, insert: z.of(f) },
        range: b.cursor(r + 1 + f[1].length)
      };
    });
    return t(e.update(i, { scrollIntoView: !0, userEvent: "input" })), !0;
  };
}
function Mo(n, e) {
  let t = -1;
  return n.changeByRange((i) => {
    let s = [];
    for (let o = i.from; o <= i.to; ) {
      let l = n.doc.lineAt(o);
      l.number > t && (i.empty || i.to > l.from) && (e(l, s, i), t = l.number), o = l.to + 1;
    }
    let r = n.changes(s);
    return {
      changes: s,
      range: b.range(r.mapPos(i.anchor, 1), r.mapPos(i.head, 1))
    };
  });
}
const u0 = ({ state: n, dispatch: e }) => {
  if (n.readOnly)
    return !1;
  let t = /* @__PURE__ */ Object.create(null), i = new bs(n, { overrideIndentation: (r) => {
    let o = t[r];
    return o ?? -1;
  } }), s = Mo(n, (r, o, l) => {
    let a = ko(i, r.from);
    if (a == null)
      return;
    /\S/.test(r.text) || (a = 0);
    let h = /^\s*/.exec(r.text)[0], c = ji(n, a);
    (h != c || l.from < r.from + h.length) && (t[r.from] = a, o.push({ from: r.from, to: r.from + h.length, insert: c }));
  });
  return s.changes.empty || e(n.update(s, { userEvent: "indent" })), !0;
}, tf = ({ state: n, dispatch: e }) => n.readOnly ? !1 : (e(n.update(Mo(n, (t, i) => {
  i.push({ from: t.from, insert: n.facet(nn) });
}), { userEvent: "input.indent" })), !0), nf = ({ state: n, dispatch: e }) => n.readOnly ? !1 : (e(n.update(Mo(n, (t, i) => {
  let s = /^\s*/.exec(t.text)[0];
  if (!s)
    return;
  let r = pi(s, n.tabSize), o = 0, l = ji(n, Math.max(0, r - es(n)));
  for (; o < s.length && o < l.length && s.charCodeAt(o) == l.charCodeAt(o); )
    o++;
  i.push({ from: t.from + o, to: t.from + s.length, insert: l.slice(o) });
}), { userEvent: "delete.dedent" })), !0), d0 = (n) => (n.setTabFocusMode(), !0), p0 = [
  { key: "Ctrl-b", run: Lc, shift: Vc, preventDefault: !0 },
  { key: "Ctrl-f", run: Ec, shift: zc },
  { key: "Ctrl-p", run: Ic, shift: Kc },
  { key: "Ctrl-n", run: Nc, shift: jc },
  { key: "Ctrl-a", run: Rg, shift: jg },
  { key: "Ctrl-e", run: Pg, shift: Ug },
  { key: "Ctrl-d", run: Yc },
  { key: "Ctrl-h", run: Jr },
  { key: "Ctrl-k", run: e0 },
  { key: "Ctrl-Alt-h", run: Xc },
  { key: "Ctrl-o", run: n0 },
  { key: "Ctrl-t", run: s0 },
  { key: "Ctrl-v", run: Yr }
], m0 = /* @__PURE__ */ [
  { key: "ArrowLeft", run: Lc, shift: Vc, preventDefault: !0 },
  { key: "Mod-ArrowLeft", mac: "Alt-ArrowLeft", run: Cg, shift: Wg, preventDefault: !0 },
  { mac: "Cmd-ArrowLeft", run: Lg, shift: $g, preventDefault: !0 },
  { key: "ArrowRight", run: Ec, shift: zc, preventDefault: !0 },
  { key: "Mod-ArrowRight", mac: "Alt-ArrowRight", run: Ag, shift: Hg, preventDefault: !0 },
  { mac: "Cmd-ArrowRight", run: Eg, shift: Kg, preventDefault: !0 },
  { key: "ArrowUp", run: Ic, shift: Kc, preventDefault: !0 },
  { mac: "Cmd-ArrowUp", run: ia, shift: sa },
  { mac: "Ctrl-ArrowUp", run: Zl, shift: ea },
  { key: "ArrowDown", run: Nc, shift: jc, preventDefault: !0 },
  { mac: "Cmd-ArrowDown", run: na, shift: ra },
  { mac: "Ctrl-ArrowDown", run: Yr, shift: ta },
  { key: "PageUp", run: Zl, shift: ea },
  { key: "PageDown", run: Yr, shift: ta },
  { key: "Home", run: Bg, shift: qg, preventDefault: !0 },
  { key: "Mod-Home", run: ia, shift: sa },
  { key: "End", run: Og, shift: zg, preventDefault: !0 },
  { key: "Mod-End", run: na, shift: ra },
  { key: "Enter", run: oa, shift: oa },
  { key: "Mod-a", run: _g },
  { key: "Backspace", run: Jr, shift: Jr, preventDefault: !0 },
  { key: "Delete", run: Yc, preventDefault: !0 },
  { key: "Mod-Backspace", mac: "Alt-Backspace", run: Xc, preventDefault: !0 },
  { key: "Mod-Delete", mac: "Alt-Delete", run: Zg, preventDefault: !0 },
  { mac: "Mod-Backspace", run: t0, preventDefault: !0 },
  { mac: "Mod-Delete", run: i0, preventDefault: !0 }
].concat(/* @__PURE__ */ p0.map((n) => ({ mac: n.key, run: n.run, shift: n.shift }))), g0 = /* @__PURE__ */ [
  { key: "Alt-ArrowLeft", mac: "Ctrl-ArrowLeft", run: Tg, shift: Fg },
  { key: "Alt-ArrowRight", mac: "Ctrl-ArrowRight", run: Dg, shift: Vg },
  { key: "Alt-ArrowUp", run: r0 },
  { key: "Shift-Alt-ArrowUp", run: l0 },
  { key: "Alt-ArrowDown", run: o0 },
  { key: "Shift-Alt-ArrowDown", run: a0 },
  { key: "Mod-Alt-ArrowUp", run: Jg },
  { key: "Mod-Alt-ArrowDown", run: Xg },
  { key: "Escape", run: Qg },
  { key: "Mod-Enter", run: f0 },
  { key: "Alt-l", mac: "Ctrl-l", run: Gg },
  { key: "Mod-i", run: Yg, preventDefault: !0 },
  { key: "Mod-[", run: nf },
  { key: "Mod-]", run: tf },
  { key: "Mod-Alt-\\", run: u0 },
  { key: "Shift-Mod-k", run: h0 },
  { key: "Shift-Mod-\\", run: Ng },
  { key: "Mod-/", run: rg },
  { key: "Alt-A", run: lg },
  { key: "Ctrl-m", mac: "Shift-Alt-m", run: d0 }
].concat(m0), y0 = { key: "Tab", run: tf, shift: nf }, la = typeof String.prototype.normalize == "function" ? (n) => n.normalize("NFKD") : (n) => n;
class di {
  /**
  Create a text cursor. The query is the search string, `from` to
  `to` provides the region to search.
  
  When `normalize` is given, it will be called, on both the query
  string and the content it is matched against, before comparing.
  You can, for example, create a case-insensitive search by
  passing `s => s.toLowerCase()`.
  
  Text is always normalized with
  [`.normalize("NFKD")`](https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/String/normalize)
  (when supported).
  */
  constructor(e, t, i = 0, s = e.length, r, o) {
    this.test = o, this.value = { from: 0, to: 0, precise: !1 }, this.done = !1, this.matches = [], this.buffer = "", this.bufferPos = 0, this.iter = e.iterRange(i, s), this.bufferStart = i, this.normalize = r ? (l) => r(la(l)) : la, this.query = this.normalize(t);
  }
  peek() {
    if (this.bufferPos == this.buffer.length) {
      if (this.bufferStart += this.buffer.length, this.iter.next(), this.iter.done)
        return -1;
      this.bufferPos = 0, this.buffer = this.iter.value;
    }
    return ke(this.buffer, this.bufferPos);
  }
  /**
  Look for the next match. Updates the iterator's
  [`value`](https://codemirror.net/6/docs/ref/#search.SearchCursor.value) and
  [`done`](https://codemirror.net/6/docs/ref/#search.SearchCursor.done) properties. Should be called
  at least once before using the cursor.
  */
  next() {
    for (; this.matches.length; )
      this.matches.pop();
    return this.nextOverlapping();
  }
  /**
  The `next` method will ignore matches that partially overlap a
  previous match. This method behaves like `next`, but includes
  such matches.
  */
  nextOverlapping() {
    for (; ; ) {
      let e = this.peek();
      if (e < 0)
        return this.done = !0, this;
      let t = Zr(e), i = this.bufferStart + this.bufferPos;
      this.bufferPos += et(e);
      let s = this.normalize(t);
      if (s.length)
        for (let r = 0, o = i, l = !0; ; r++) {
          let a = s.charCodeAt(r), h = this.match(a, o, l, this.bufferPos + this.bufferStart, r == s.length - 1);
          if (h)
            return this.value = h, this;
          if (r == s.length - 1)
            break;
          l && r < t.length && t.charCodeAt(r) == a ? o++ : l = !1;
        }
    }
  }
  match(e, t, i, s, r) {
    let o = null;
    for (let l = 0; l < this.matches.length; ) {
      let a = this.matches[l], h = !1;
      this.query.charCodeAt(a.index) == e && (a.index == this.query.length - 1 ? o = { from: a.from, to: s, precise: r && a.precise } : (a.index++, h = !0)), h ? l++ : this.matches.splice(l, 1);
    }
    return this.query.charCodeAt(0) == e && (this.query.length == 1 ? o = { from: t, to: s, precise: i && r } : this.matches.push({ from: t, index: 1, precise: i })), o && this.test && !this.test(o.from, o.to, this.buffer, this.bufferStart) && (o = null), o;
  }
}
typeof Symbol < "u" && (di.prototype[Symbol.iterator] = function() {
  return this;
});
const sf = { from: -1, to: -1, match: /* @__PURE__ */ /.*/.exec(""), precise: !0 }, To = "gm" + (/x/.unicode == null ? "" : "u");
class rf {
  /**
  Create a cursor that will search the given range in the given
  document. `query` should be the raw pattern (as you'd pass it to
  `new RegExp`).
  */
  constructor(e, t, i, s = 0, r = e.length) {
    if (this.text = e, this.to = r, this.curLine = "", this.done = !1, this.value = sf, /\\[sWDnr]|\n|\r|\[\^/.test(t))
      return new of(e, t, i, s, r);
    this.re = new RegExp(t, To + (i?.ignoreCase ? "i" : "")), this.test = i?.test, this.iter = e.iter();
    let o = e.lineAt(s);
    this.curLineStart = o.from, this.matchPos = ss(e, s), this.getLine(this.curLineStart);
  }
  getLine(e) {
    this.iter.next(e), this.iter.lineBreak ? this.curLine = "" : (this.curLine = this.iter.value, this.curLineStart + this.curLine.length > this.to && (this.curLine = this.curLine.slice(0, this.to - this.curLineStart)), this.iter.next());
  }
  nextLine() {
    this.curLineStart = this.curLineStart + this.curLine.length + 1, this.curLineStart > this.to ? this.curLine = "" : this.getLine(0);
  }
  /**
  Move to the next match, if there is one.
  */
  next() {
    for (let e = this.matchPos - this.curLineStart; ; ) {
      this.re.lastIndex = e;
      let t = this.matchPos <= this.to && this.re.exec(this.curLine);
      if (t) {
        let i = this.curLineStart + t.index, s = i + t[0].length;
        if (this.matchPos = ss(this.text, s + (i == s ? 1 : 0)), i == this.curLineStart + this.curLine.length && this.nextLine(), (i < s || i > this.value.to) && (!this.test || this.test(i, s, t)))
          return this.value = { from: i, to: s, precise: !0, match: t }, this;
        e = this.matchPos - this.curLineStart;
      } else if (this.curLineStart + this.curLine.length < this.to)
        this.nextLine(), e = 0;
      else
        return this.done = !0, this;
    }
  }
}
const Xs = /* @__PURE__ */ new WeakMap();
class ri {
  constructor(e, t) {
    this.from = e, this.text = t;
  }
  get to() {
    return this.from + this.text.length;
  }
  static get(e, t, i) {
    let s = Xs.get(e);
    if (!s || s.from >= i || s.to <= t) {
      let l = new ri(t, e.sliceString(t, i));
      return Xs.set(e, l), l;
    }
    if (s.from == t && s.to == i)
      return s;
    let { text: r, from: o } = s;
    return o > t && (r = e.sliceString(t, o) + r, o = t), s.to < i && (r += e.sliceString(s.to, i)), Xs.set(e, new ri(o, r)), new ri(t, r.slice(t - o, i - o));
  }
}
class of {
  constructor(e, t, i, s, r) {
    this.text = e, this.to = r, this.done = !1, this.value = sf, this.matchPos = ss(e, s), this.re = new RegExp(t, To + (i?.ignoreCase ? "i" : "")), this.test = i?.test, this.flat = ri.get(e, s, this.chunkEnd(
      s + 5e3
      /* Chunk.Base */
    ));
  }
  chunkEnd(e) {
    return e >= this.to ? this.to : this.text.lineAt(e).to;
  }
  next() {
    for (; ; ) {
      let e = this.re.lastIndex = this.matchPos - this.flat.from, t = this.re.exec(this.flat.text);
      if (t && !t[0] && t.index == e && (this.re.lastIndex = e + 1, t = this.re.exec(this.flat.text)), t) {
        let i = this.flat.from + t.index, s = i + t[0].length;
        if ((this.flat.to >= this.to || t.index + t[0].length <= this.flat.text.length - 10) && (!this.test || this.test(i, s, t)))
          return this.value = { from: i, to: s, precise: !0, match: t }, this.matchPos = ss(this.text, s + (i == s ? 1 : 0)), this;
      }
      if (this.flat.to == this.to)
        return this.done = !0, this;
      this.flat = ri.get(this.text, this.flat.from, this.chunkEnd(this.flat.from + this.flat.text.length * 2));
    }
  }
}
typeof Symbol < "u" && (rf.prototype[Symbol.iterator] = of.prototype[Symbol.iterator] = function() {
  return this;
});
function b0(n) {
  try {
    return new RegExp(n, To), !0;
  } catch {
    return !1;
  }
}
function ss(n, e) {
  if (e >= n.length)
    return e;
  let t = n.lineAt(e), i;
  for (; e < t.to && (i = t.text.charCodeAt(e - t.from)) >= 56320 && i < 57344; )
    e++;
  return e;
}
const x0 = (n) => {
  let { state: e } = n, t = String(e.doc.lineAt(n.state.selection.main.head).number), { close: i, result: s } = Ip(n, {
    label: e.phrase("Go to line"),
    input: { type: "text", name: "line", value: t },
    focus: !0,
    submitLabel: e.phrase("go")
  });
  return s.then((r) => {
    let o = r && /^([+-])?(\d+)?(:\d+)?(%)?$/.exec(r.elements.line.value);
    if (!o) {
      n.dispatch({ effects: i });
      return;
    }
    let l = e.doc.lineAt(e.selection.main.head), [, a, h, c, f] = o, u = c ? +c.slice(1) : 0, d = h ? +h : l.number;
    if (h && f) {
      let g = d / 100;
      a && (g = g * (a == "-" ? -1 : 1) + l.number / e.doc.lines), d = Math.round(e.doc.lines * g);
    } else h && a && (d = d * (a == "-" ? -1 : 1) + l.number);
    let p = e.doc.line(Math.max(1, Math.min(e.doc.lines, d))), m = b.cursor(p.from + Math.max(0, Math.min(u, p.length)));
    n.dispatch({
      effects: [i, M.scrollIntoView(m.from, { y: "center" })],
      selection: m
    });
  }), !0;
}, w0 = {
  highlightWordAroundCursor: !1,
  minSelectionLength: 1,
  maxMatches: 100,
  wholeWords: !1
}, v0 = /* @__PURE__ */ T.define({
  combine(n) {
    return at(n, w0, {
      highlightWordAroundCursor: (e, t) => e || t,
      minSelectionLength: Math.min,
      maxMatches: Math.min
    });
  }
});
function k0(n) {
  return [T0, M0];
}
const S0 = /* @__PURE__ */ B.mark({ class: "cm-selectionMatch" }), C0 = /* @__PURE__ */ B.mark({ class: "cm-selectionMatch cm-selectionMatch-main" });
function aa(n, e, t, i) {
  return (t == 0 || n(e.sliceDoc(t - 1, t)) != Y.Word) && (i == e.doc.length || n(e.sliceDoc(i, i + 1)) != Y.Word);
}
function A0(n, e, t, i) {
  return n(e.sliceDoc(t, t + 1)) == Y.Word && n(e.sliceDoc(i - 1, i)) == Y.Word;
}
const M0 = /* @__PURE__ */ Q.fromClass(class {
  constructor(n) {
    this.decorations = this.getDeco(n);
  }
  update(n) {
    (n.selectionSet || n.docChanged || n.viewportChanged) && (this.decorations = this.getDeco(n.view));
  }
  getDeco(n) {
    let e = n.state.facet(v0), { state: t } = n, i = t.selection;
    if (i.ranges.length > 1)
      return B.none;
    let s = i.main, r, o = null;
    if (s.empty) {
      if (!e.highlightWordAroundCursor)
        return B.none;
      let a = t.wordAt(s.head);
      if (!a)
        return B.none;
      o = t.charCategorizer(s.head), r = t.sliceDoc(a.from, a.to);
    } else {
      let a = s.to - s.from;
      if (a < e.minSelectionLength || a > 200)
        return B.none;
      if (e.wholeWords) {
        if (r = t.sliceDoc(s.from, s.to), o = t.charCategorizer(s.head), !(aa(o, t, s.from, s.to) && A0(o, t, s.from, s.to)))
          return B.none;
      } else if (r = t.sliceDoc(s.from, s.to), !r)
        return B.none;
    }
    let l = [];
    for (let a of n.visibleRanges) {
      let h = new di(t.doc, r, a.from, a.to);
      for (; !h.next().done; ) {
        let { from: c, to: f } = h.value;
        if ((!o || aa(o, t, c, f)) && (s.empty && c <= s.from && f >= s.to ? l.push(C0.range(c, f)) : (c >= s.to || f <= s.from) && l.push(S0.range(c, f)), l.length > e.maxMatches))
          return B.none;
      }
    }
    return B.set(l);
  }
}, {
  decorations: (n) => n.decorations
}), T0 = /* @__PURE__ */ M.baseTheme({
  ".cm-selectionMatch": { backgroundColor: "#99ff7780" },
  ".cm-searchMatch .cm-selectionMatch": { backgroundColor: "transparent" }
}), D0 = ({ state: n, dispatch: e }) => {
  let { selection: t } = n, i = b.create(t.ranges.map((s) => n.wordAt(s.head) || b.cursor(s.head)), t.mainIndex);
  return i.eq(t) ? !1 : (e(n.update({ selection: i })), !0);
};
function O0(n, e) {
  let { main: t, ranges: i } = n.selection, s = n.wordAt(t.head), r = s && s.from == t.from && s.to == t.to;
  for (let o = !1, l = new di(n.doc, e, i[i.length - 1].to); ; )
    if (l.next(), l.done) {
      if (o)
        return null;
      l = new di(n.doc, e, 0, Math.max(0, i[i.length - 1].from - 1)), o = !0;
    } else {
      if (o && i.some((a) => a.from == l.value.from))
        continue;
      if (r) {
        let a = n.wordAt(l.value.from);
        if (!a || a.from != l.value.from || a.to != l.value.to)
          continue;
      }
      return l.value;
    }
}
const B0 = ({ state: n, dispatch: e }) => {
  let { ranges: t } = n.selection;
  if (t.some((r) => r.from === r.to))
    return D0({ state: n, dispatch: e });
  let i = n.sliceDoc(t[0].from, t[0].to);
  if (n.selection.ranges.some((r) => n.sliceDoc(r.from, r.to) != i))
    return !1;
  let s = O0(n, i);
  return s ? (e(n.update({
    selection: n.selection.addRange(b.range(s.from, s.to), !1),
    effects: M.scrollIntoView(s.to)
  })), !0) : !1;
}, gi = /* @__PURE__ */ T.define({
  combine(n) {
    return at(n, {
      top: !1,
      caseSensitive: !1,
      literal: !1,
      regexp: !1,
      wholeWord: !1,
      createPanel: (e) => new K0(e),
      scrollToMatch: (e) => M.scrollIntoView(e)
    });
  }
});
class lf {
  /**
  Create a query object.
  */
  constructor(e) {
    this.search = e.search, this.caseSensitive = !!e.caseSensitive, this.literal = !!e.literal, this.regexp = !!e.regexp, this.replace = e.replace || "", this.valid = !!this.search && (!this.regexp || b0(this.search)), this.unquoted = this.unquote(this.search), this.wholeWord = !!e.wholeWord, this.test = e.test;
  }
  /**
  @internal
  */
  unquote(e) {
    return this.literal ? e : e.replace(/\\([nrt\\])/g, (t, i) => i == "n" ? `
` : i == "r" ? "\r" : i == "t" ? "	" : "\\");
  }
  /**
  Compare this query to another query.
  */
  eq(e) {
    return this.search == e.search && this.replace == e.replace && this.caseSensitive == e.caseSensitive && this.regexp == e.regexp && this.wholeWord == e.wholeWord && this.test == e.test;
  }
  /**
  @internal
  */
  create() {
    return this.regexp ? new N0(this) : new R0(this);
  }
  /**
  Get a search cursor for this query, searching through the given
  range in the given state.
  */
  getCursor(e, t = 0, i) {
    let s = e.doc ? e : F.create({ doc: e });
    return i == null && (i = s.doc.length), this.regexp ? Jt(this, s, t, i) : Yt(this, s, t, i);
  }
}
class af {
  constructor(e) {
    this.spec = e;
  }
}
function L0(n, e, t) {
  return (i, s, r, o) => {
    if (t && !t(i, s, r, o))
      return !1;
    let l = i >= o && s <= o + r.length ? r.slice(i - o, s - o) : e.doc.sliceString(i, s);
    return n(l, e, i, s);
  };
}
function Yt(n, e, t, i) {
  let s;
  return n.wholeWord && (s = E0(e.doc, e.charCategorizer(e.selection.main.head))), n.test && (s = L0(n.test, e, s)), new di(e.doc, n.unquoted, t, i, n.caseSensitive ? void 0 : (r) => r.toLowerCase(), s);
}
function E0(n, e) {
  return (t, i, s, r) => ((r > t || r + s.length < i) && (r = Math.max(0, t - 2), s = n.sliceString(r, Math.min(n.length, i + 2))), (e(rs(s, t - r)) != Y.Word || e(os(s, t - r)) != Y.Word) && (e(os(s, i - r)) != Y.Word || e(rs(s, i - r)) != Y.Word));
}
class R0 extends af {
  constructor(e) {
    super(e);
  }
  nextMatch(e, t, i) {
    let s = Yt(this.spec, e, i, e.doc.length).nextOverlapping();
    if (s.done) {
      let r = Math.min(e.doc.length, t + this.spec.unquoted.length);
      s = Yt(this.spec, e, 0, r).nextOverlapping();
    }
    return s.done || s.value.from == t && s.value.to == i ? null : s.value;
  }
  // Searching in reverse is, rather than implementing an inverted search
  // cursor, done by scanning chunk after chunk forward.
  prevMatchInRange(e, t, i) {
    for (let s = i; ; ) {
      let r = Math.max(t, s - 1e4 - this.spec.unquoted.length), o = Yt(this.spec, e, r, s), l = null;
      for (; !o.nextOverlapping().done; )
        l = o.value;
      if (l)
        return l;
      if (r == t)
        return null;
      s -= 1e4;
    }
  }
  prevMatch(e, t, i) {
    let s = this.prevMatchInRange(e, 0, t);
    return s || (s = this.prevMatchInRange(e, Math.max(0, i - this.spec.unquoted.length), e.doc.length)), s && (s.from != t || s.to != i) ? s : null;
  }
  getReplacement(e) {
    return this.spec.unquote(this.spec.replace);
  }
  matchAll(e, t) {
    let i = Yt(this.spec, e, 0, e.doc.length), s = [];
    for (; !i.next().done; ) {
      if (s.length >= t)
        return null;
      s.push(i.value);
    }
    return s;
  }
  highlight(e, t, i, s) {
    let r = Yt(this.spec, e, Math.max(0, t - this.spec.unquoted.length), Math.min(i + this.spec.unquoted.length, e.doc.length));
    for (; !r.next().done; )
      s(r.value.from, r.value.to);
  }
}
function P0(n, e, t) {
  return (i, s, r) => (!t || t(i, s, r)) && n(r[0], e, i, s);
}
function Jt(n, e, t, i) {
  let s;
  return n.wholeWord && (s = I0(e.charCategorizer(e.selection.main.head))), n.test && (s = P0(n.test, e, s)), new rf(e.doc, n.search, { ignoreCase: !n.caseSensitive, test: s }, t, i);
}
function rs(n, e) {
  return n.slice(oe(n, e, !1), e);
}
function os(n, e) {
  return n.slice(e, oe(n, e));
}
function I0(n) {
  return (e, t, i) => !i[0].length || (n(rs(i.input, i.index)) != Y.Word || n(os(i.input, i.index)) != Y.Word) && (n(os(i.input, i.index + i[0].length)) != Y.Word || n(rs(i.input, i.index + i[0].length)) != Y.Word);
}
class N0 extends af {
  nextMatch(e, t, i) {
    let s = Jt(this.spec, e, i, e.doc.length).next();
    return s.done && (s = Jt(this.spec, e, 0, t).next()), s.done ? null : s.value;
  }
  prevMatchInRange(e, t, i) {
    for (let s = 1; ; s++) {
      let r = Math.max(
        t,
        i - s * 1e4
        /* FindPrev.ChunkSize */
      ), o = Jt(this.spec, e, r, i), l = null;
      for (; !o.next().done; )
        l = o.value;
      if (l && (r == t || l.from > r + 10))
        return l;
      if (r == t)
        return null;
    }
  }
  prevMatch(e, t, i) {
    return this.prevMatchInRange(e, 0, t) || this.prevMatchInRange(e, i, e.doc.length);
  }
  getReplacement(e) {
    return this.spec.unquote(this.spec.replace).replace(/\$([$&]|\d+)/g, (t, i) => {
      if (i == "&")
        return e.match[0];
      if (i == "$")
        return "$";
      for (let s = i.length; s > 0; s--) {
        let r = +i.slice(0, s);
        if (r > 0 && r < e.match.length)
          return e.match[r] + i.slice(s);
      }
      return t;
    });
  }
  matchAll(e, t) {
    let i = Jt(this.spec, e, 0, e.doc.length), s = [];
    for (; !i.next().done; ) {
      if (s.length >= t)
        return null;
      s.push(i.value);
    }
    return s;
  }
  highlight(e, t, i, s) {
    let r = Jt(this.spec, e, Math.max(
      0,
      t - 250
      /* RegExp.HighlightMargin */
    ), Math.min(i + 250, e.doc.length));
    for (; !r.next().done; )
      s(r.value.from, r.value.to);
  }
}
const Ui = /* @__PURE__ */ R.define(), Do = /* @__PURE__ */ R.define(), xt = /* @__PURE__ */ me.define({
  create(n) {
    return new Qs(Xr(n).create(), null);
  },
  update(n, e) {
    for (let t of e.effects)
      t.is(Ui) ? n = new Qs(t.value.create(), n.panel) : t.is(Do) && (n = new Qs(n.query, t.value ? Oo : null));
    return n;
  },
  provide: (n) => qi.from(n, (e) => e.panel)
});
class Qs {
  constructor(e, t) {
    this.query = e, this.panel = t;
  }
}
const W0 = /* @__PURE__ */ B.mark({ class: "cm-searchMatch" }), H0 = /* @__PURE__ */ B.mark({ class: "cm-searchMatch cm-searchMatch-selected" }), F0 = /* @__PURE__ */ Q.fromClass(class {
  constructor(n) {
    this.view = n, this.decorations = this.highlight(n.state.field(xt));
  }
  update(n) {
    let e = n.state.field(xt);
    (e != n.startState.field(xt) || n.docChanged || n.selectionSet || n.viewportChanged) && (this.decorations = this.highlight(e));
  }
  highlight({ query: n, panel: e }) {
    if (!e || !n.spec.valid)
      return B.none;
    let { view: t } = this, i = new ft();
    for (let s = 0, r = t.visibleRanges, o = r.length; s < o; s++) {
      let { from: l, to: a } = r[s];
      for (; s < o - 1 && a > r[s + 1].from - 500; )
        a = r[++s].to;
      n.highlight(t.state, l, a, (h, c) => {
        let f = t.state.selection.ranges.some((u) => u.from == h && u.to == c);
        i.add(h, c, f ? H0 : W0);
      });
    }
    return i.finish();
  }
}, {
  decorations: (n) => n.decorations
});
function ln(n) {
  return (e) => {
    let t = e.state.field(xt, !1);
    return t && t.query.spec.valid ? n(e, t) : ff(e);
  };
}
const ls = /* @__PURE__ */ ln((n, { query: e }) => {
  let { to: t } = n.state.selection.main, i = e.nextMatch(n.state, t, t);
  if (!i)
    return !1;
  let s = b.single(i.from, i.to), r = n.state.facet(gi);
  return n.dispatch({
    selection: s,
    effects: [Bo(n, i), r.scrollToMatch(s.main, n)],
    userEvent: "select.search"
  }), cf(n), !0;
}), as = /* @__PURE__ */ ln((n, { query: e }) => {
  let { state: t } = n, { from: i } = t.selection.main, s = e.prevMatch(t, i, i);
  if (!s)
    return !1;
  let r = b.single(s.from, s.to), o = n.state.facet(gi);
  return n.dispatch({
    selection: r,
    effects: [Bo(n, s), o.scrollToMatch(r.main, n)],
    userEvent: "select.search"
  }), cf(n), !0;
}), V0 = /* @__PURE__ */ ln((n, { query: e }) => {
  let t = e.matchAll(n.state, 1e3);
  return !t || !t.length ? !1 : (n.dispatch({
    selection: b.create(t.map((i) => b.range(i.from, i.to))),
    userEvent: "select.search.matches"
  }), !0);
}), z0 = ({ state: n, dispatch: e }) => {
  let t = n.selection;
  if (t.ranges.length > 1 || t.main.empty)
    return !1;
  let { from: i, to: s } = t.main, r = [], o = 0;
  for (let l = new di(n.doc, n.sliceDoc(i, s)); !l.next().done; ) {
    if (r.length > 1e3)
      return !1;
    l.value.from == i && (o = r.length), r.push(b.range(l.value.from, l.value.to));
  }
  return e(n.update({
    selection: b.create(r, o),
    userEvent: "select.search.matches"
  })), !0;
}, ha = /* @__PURE__ */ ln((n, { query: e }) => {
  let { state: t } = n, { from: i, to: s } = t.selection.main;
  if (t.readOnly)
    return !1;
  let r = e.nextMatch(t, i, i);
  if (!r)
    return !1;
  let o = r, l = [], a, h, c = [];
  o.precise ? o.from == i && o.to == s && (h = t.toText(e.getReplacement(o)), l.push({ from: o.from, to: o.to, insert: h }), o = e.nextMatch(t, o.from, o.to), c.push(M.announce.of(t.phrase("replaced match on line $", t.doc.lineAt(i).number) + "."))) : o = e.nextMatch(t, o.from, o.to);
  let f = n.state.changes(l);
  return o && (a = b.single(o.from, o.to).map(f), c.push(Bo(n, o)), c.push(t.facet(gi).scrollToMatch(a.main, n))), n.dispatch({
    changes: f,
    selection: a,
    effects: c,
    userEvent: "input.replace"
  }), !0;
}), q0 = /* @__PURE__ */ ln((n, { query: e }) => {
  if (n.state.readOnly)
    return !1;
  let t = [];
  for (let s of e.matchAll(n.state, 1e9)) {
    let { from: r, to: o, precise: l } = s;
    l && t.push({ from: r, to: o, insert: e.getReplacement(s) });
  }
  if (!t.length)
    return !1;
  let i = n.state.phrase("replaced $ matches", t.length) + ".";
  return n.dispatch({
    changes: t,
    effects: M.announce.of(i),
    userEvent: "input.replace.all"
  }), !0;
});
function Oo(n) {
  return n.state.facet(gi).createPanel(n);
}
function Xr(n, e) {
  var t, i, s, r, o;
  let l = n.selection.main, a = l.empty || l.to > l.from + 100 ? "" : n.sliceDoc(l.from, l.to);
  if (e && !a)
    return e;
  let h = n.facet(gi);
  return new lf({
    search: ((t = e?.literal) !== null && t !== void 0 ? t : h.literal) ? a : a.replace(/\n/g, "\\n"),
    caseSensitive: (i = e?.caseSensitive) !== null && i !== void 0 ? i : h.caseSensitive,
    literal: (s = e?.literal) !== null && s !== void 0 ? s : h.literal,
    regexp: (r = e?.regexp) !== null && r !== void 0 ? r : h.regexp,
    wholeWord: (o = e?.wholeWord) !== null && o !== void 0 ? o : h.wholeWord
  });
}
function hf(n) {
  let e = bo(n, Oo);
  return e && e.dom.querySelector("[main-field]");
}
function cf(n) {
  let e = hf(n);
  e && e == n.root.activeElement && e.select();
}
const ff = (n) => {
  let e = n.state.field(xt, !1);
  if (e && e.panel) {
    let t = hf(n);
    if (t && t != n.root.activeElement) {
      let i = Xr(n.state, e.query.spec);
      i.valid && n.dispatch({ effects: Ui.of(i) }), t.focus(), t.select();
    }
  } else
    n.dispatch({ effects: [
      Do.of(!0),
      e ? Ui.of(Xr(n.state, e.query.spec)) : R.appendConfig.of(U0)
    ] });
  return !0;
}, uf = (n) => {
  let e = n.state.field(xt, !1);
  if (!e || !e.panel)
    return !1;
  let t = bo(n, Oo);
  return t && t.dom.contains(n.root.activeElement) && n.focus(), n.dispatch({ effects: Do.of(!1) }), !0;
}, $0 = [
  { key: "Mod-f", run: ff, scope: "editor search-panel" },
  { key: "F3", run: ls, shift: as, scope: "editor search-panel", preventDefault: !0 },
  { key: "Mod-g", run: ls, shift: as, scope: "editor search-panel", preventDefault: !0 },
  { key: "Escape", run: uf, scope: "editor search-panel" },
  { key: "Mod-Shift-l", run: z0 },
  { key: "Mod-Alt-g", run: x0 },
  { key: "Mod-d", run: B0, preventDefault: !0 }
];
class K0 {
  constructor(e) {
    this.view = e;
    let t = this.query = e.state.field(xt).query.spec;
    this.commit = this.commit.bind(this), this.searchField = K("input", {
      value: t.search,
      placeholder: Be(e, "Find"),
      "aria-label": Be(e, "Find"),
      class: "cm-textfield",
      name: "search",
      form: "",
      "main-field": "true",
      onchange: this.commit,
      onkeyup: this.commit
    }), this.replaceField = K("input", {
      value: t.replace,
      placeholder: Be(e, "Replace"),
      "aria-label": Be(e, "Replace"),
      class: "cm-textfield",
      name: "replace",
      form: "",
      onchange: this.commit,
      onkeyup: this.commit
    }), this.caseField = K("input", {
      type: "checkbox",
      name: "case",
      form: "",
      checked: t.caseSensitive,
      onchange: this.commit
    }), this.reField = K("input", {
      type: "checkbox",
      name: "re",
      form: "",
      checked: t.regexp,
      onchange: this.commit
    }), this.wordField = K("input", {
      type: "checkbox",
      name: "word",
      form: "",
      checked: t.wholeWord,
      onchange: this.commit
    });
    function i(s, r, o) {
      return K("button", { class: "cm-button", name: s, onclick: r, type: "button" }, o);
    }
    this.dom = K("div", { onkeydown: (s) => this.keydown(s), class: "cm-search" }, [
      this.searchField,
      i("next", () => ls(e), [Be(e, "next")]),
      i("prev", () => as(e), [Be(e, "previous")]),
      i("select", () => V0(e), [Be(e, "all")]),
      K("label", null, [this.caseField, Be(e, "match case")]),
      K("label", null, [this.reField, Be(e, "regexp")]),
      K("label", null, [this.wordField, Be(e, "by word")]),
      ...e.state.readOnly ? [] : [
        K("br"),
        this.replaceField,
        i("replace", () => ha(e), [Be(e, "replace")]),
        i("replaceAll", () => q0(e), [Be(e, "replace all")])
      ],
      K("button", {
        name: "close",
        onclick: () => uf(e),
        "aria-label": Be(e, "close"),
        type: "button"
      }, ["×"])
    ]);
  }
  commit() {
    let e = new lf({
      search: this.searchField.value,
      caseSensitive: this.caseField.checked,
      regexp: this.reField.checked,
      wholeWord: this.wordField.checked,
      replace: this.replaceField.value
    });
    e.eq(this.query) || (this.query = e, this.view.dispatch({ effects: Ui.of(e) }));
  }
  keydown(e) {
    Kd(this.view, e, "search-panel") ? e.preventDefault() : e.keyCode == 13 && e.target == this.searchField ? (e.preventDefault(), (e.shiftKey ? as : ls)(this.view)) : e.keyCode == 13 && e.target == this.replaceField && (e.preventDefault(), ha(this.view));
  }
  update(e) {
    for (let t of e.transactions)
      for (let i of t.effects)
        i.is(Ui) && !i.value.eq(this.query) && this.setQuery(i.value);
  }
  setQuery(e) {
    this.query = e, this.searchField.value = e.search, this.replaceField.value = e.replace, this.caseField.checked = e.caseSensitive, this.reField.checked = e.regexp, this.wordField.checked = e.wholeWord;
  }
  mount() {
    this.searchField.select();
  }
  get pos() {
    return 80;
  }
  get top() {
    return this.view.state.facet(gi).top;
  }
}
function Be(n, e) {
  return n.state.phrase(e);
}
const On = 30, Bn = /[\s\.,:;?!]/;
function Bo(n, { from: e, to: t }) {
  let i = n.state.doc.lineAt(e), s = n.state.doc.lineAt(t).to, r = Math.max(i.from, e - On), o = Math.min(s, t + On), l = n.state.sliceDoc(r, o);
  if (r != i.from) {
    for (let a = 0; a < On; a++)
      if (!Bn.test(l[a + 1]) && Bn.test(l[a])) {
        l = l.slice(a);
        break;
      }
  }
  if (o != s) {
    for (let a = l.length - 1; a > l.length - On; a--)
      if (!Bn.test(l[a - 1]) && Bn.test(l[a])) {
        l = l.slice(0, a);
        break;
      }
  }
  return M.announce.of(`${n.state.phrase("current match")}. ${l} ${n.state.phrase("on line")} ${i.number}.`);
}
const j0 = /* @__PURE__ */ M.baseTheme({
  ".cm-panel.cm-search": {
    padding: "2px 6px 4px",
    position: "relative",
    "& [name=close]": {
      position: "absolute",
      top: "0",
      right: "4px",
      backgroundColor: "inherit",
      border: "none",
      font: "inherit",
      padding: 0,
      margin: 0
    },
    "& input, & button, & label": {
      margin: ".2em .6em .2em 0"
    },
    "& input[type=checkbox]": {
      marginRight: ".2em"
    },
    "& label": {
      fontSize: "80%",
      whiteSpace: "pre"
    }
  },
  "&light .cm-searchMatch": { backgroundColor: "#ffff0054" },
  "&dark .cm-searchMatch": { backgroundColor: "#00ffff8a" },
  "&light .cm-searchMatch-selected": { backgroundColor: "#ff6a0054" },
  "&dark .cm-searchMatch-selected": { backgroundColor: "#ff00ff8a" }
}), U0 = [
  xt,
  /* @__PURE__ */ Tt.low(F0),
  j0
];
class df {
  /**
  Create a new completion context. (Mostly useful for testing
  completion sources—in the editor, the extension will create
  these for you.)
  */
  constructor(e, t, i, s) {
    this.state = e, this.pos = t, this.explicit = i, this.view = s, this.abortListeners = [], this.abortOnDocChange = !1;
  }
  /**
  Get the extent, content, and (if there is a token) type of the
  token before `this.pos`.
  */
  tokenBefore(e) {
    let t = pe(this.state).resolveInner(this.pos, -1);
    for (; t && e.indexOf(t.name) < 0; )
      t = t.parent;
    return t ? {
      from: t.from,
      to: this.pos,
      text: this.state.sliceDoc(t.from, this.pos),
      type: t.type
    } : null;
  }
  /**
  Get the match of the given expression directly before the
  cursor.
  */
  matchBefore(e) {
    let t = this.state.doc.lineAt(this.pos), i = Math.max(t.from, this.pos - 250), s = t.text.slice(i - t.from, this.pos - t.from), r = s.search(pf(e, !1));
    return r < 0 ? null : { from: i + r, to: this.pos, text: s.slice(r) };
  }
  /**
  Yields true when the query has been aborted. Can be useful in
  asynchronous queries to avoid doing work that will be ignored.
  */
  get aborted() {
    return this.abortListeners == null;
  }
  /**
  Allows you to register abort handlers, which will be called when
  the query is
  [aborted](https://codemirror.net/6/docs/ref/#autocomplete.CompletionContext.aborted).
  
  By default, running queries will not be aborted for regular
  typing or backspacing, on the assumption that they are likely to
  return a result with a
  [`validFor`](https://codemirror.net/6/docs/ref/#autocomplete.CompletionResult.validFor) field that
  allows the result to be used after all. Passing `onDocChange:
  true` will cause this query to be aborted for any document
  change.
  */
  addEventListener(e, t, i) {
    e == "abort" && this.abortListeners && (this.abortListeners.push(t), i && i.onDocChange && (this.abortOnDocChange = !0));
  }
}
function ca(n) {
  let e = Object.keys(n).join(""), t = /\w/.test(e);
  return t && (e = e.replace(/\w/g, "")), `[${t ? "\\w" : ""}${e.replace(/[^\w\s]/g, "\\$&")}]`;
}
function _0(n) {
  let e = /* @__PURE__ */ Object.create(null), t = /* @__PURE__ */ Object.create(null);
  for (let { label: s } of n) {
    e[s[0]] = !0;
    for (let r = 1; r < s.length; r++)
      t[s[r]] = !0;
  }
  let i = ca(e) + ca(t) + "*$";
  return [new RegExp("^" + i), new RegExp(i)];
}
function G0(n) {
  let e = n.map((s) => typeof s == "string" ? { label: s } : s), [t, i] = e.every((s) => /^\w+$/.test(s.label)) ? [/\w*$/, /\w+$/] : _0(e);
  return (s) => {
    let r = s.matchBefore(i);
    return r || s.explicit ? { from: r ? r.from : s.pos, options: e, validFor: t } : null;
  };
}
function Ib(n, e) {
  return (t) => {
    for (let i = pe(t.state).resolveInner(t.pos, -1); i; i = i.parent) {
      if (n.indexOf(i.name) > -1)
        return null;
      if (i.type.isTop)
        break;
    }
    return e(t);
  };
}
class fa {
  constructor(e, t, i, s) {
    this.completion = e, this.source = t, this.match = i, this.score = s;
  }
}
function Vt(n) {
  return n.selection.main.from;
}
function pf(n, e) {
  var t;
  let { source: i } = n, s = e && i[0] != "^", r = i[i.length - 1] != "$";
  return !s && !r ? n : new RegExp(`${s ? "^" : ""}(?:${i})${r ? "$" : ""}`, (t = n.flags) !== null && t !== void 0 ? t : n.ignoreCase ? "i" : "");
}
const Lo = /* @__PURE__ */ lt.define();
function Y0(n, e, t, i) {
  let { main: s } = n.selection, r = t - s.from, o = i - s.from;
  return {
    ...n.changeByRange((l) => {
      if (l != s && t != i && n.sliceDoc(l.from + r, l.from + o) != n.sliceDoc(t, i))
        return { range: l };
      let a = n.toText(e);
      return {
        changes: { from: l.from + r, to: i == s.from ? l.to : l.from + o, insert: a },
        range: b.cursor(l.from + r + a.length)
      };
    }),
    scrollIntoView: !0,
    userEvent: "input.complete"
  };
}
const ua = /* @__PURE__ */ new WeakMap();
function J0(n) {
  if (!Array.isArray(n))
    return n;
  let e = ua.get(n);
  return e || ua.set(n, e = G0(n)), e;
}
const hs = /* @__PURE__ */ R.define(), _i = /* @__PURE__ */ R.define();
class X0 {
  constructor(e) {
    this.pattern = e, this.chars = [], this.folded = [], this.any = [], this.precise = [], this.byWord = [], this.score = 0, this.matched = [];
    for (let t = 0; t < e.length; ) {
      let i = ke(e, t), s = et(i);
      this.chars.push(i);
      let r = e.slice(t, t + s), o = r.toUpperCase();
      this.folded.push(ke(o == r ? r.toLowerCase() : o, 0)), t += s;
    }
    this.astral = e.length != this.chars.length;
  }
  ret(e, t) {
    return this.score = e, this.matched = t, this;
  }
  // Matches a given word (completion) against the pattern (input).
  // Will return a boolean indicating whether there was a match and,
  // on success, set `this.score` to the score, `this.matched` to an
  // array of `from, to` pairs indicating the matched parts of `word`.
  //
  // The score is a number that is more negative the worse the match
  // is. See `Penalty` above.
  match(e) {
    if (this.pattern.length == 0)
      return this.ret(-100, []);
    if (e.length < this.pattern.length)
      return null;
    let { chars: t, folded: i, any: s, precise: r, byWord: o } = this;
    if (t.length == 1) {
      let w = ke(e, 0), v = et(w), O = v == e.length ? 0 : -100;
      if (w != t[0]) if (w == i[0])
        O += -200;
      else
        return null;
      return this.ret(O, [0, v]);
    }
    let l = e.indexOf(this.pattern);
    if (l == 0)
      return this.ret(e.length == this.pattern.length ? 0 : -100, [0, this.pattern.length]);
    let a = t.length, h = 0;
    if (l < 0) {
      for (let w = 0, v = Math.min(e.length, 200); w < v && h < a; ) {
        let O = ke(e, w);
        (O == t[h] || O == i[h]) && (s[h++] = w), w += et(O);
      }
      if (h < a)
        return null;
    }
    let c = 0, f = 0, u = !1, d = 0, p = -1, m = -1, g = /[a-z]/.test(e), y = !0;
    for (let w = 0, v = Math.min(e.length, 200), O = 0; w < v && f < a; ) {
      let k = ke(e, w);
      l < 0 && (c < a && k == t[c] && (r[c++] = w), d < a && (k == t[d] || k == i[d] ? (d == 0 && (p = w), m = w + 1, d++) : d = 0));
      let S, C = k < 255 ? k >= 48 && k <= 57 || k >= 97 && k <= 122 ? 2 : k >= 65 && k <= 90 ? 1 : 0 : (S = Zr(k)) != S.toLowerCase() ? 1 : S != S.toUpperCase() ? 2 : 0;
      (!w || C == 1 && g || O == 0 && C != 0) && (t[f] == k || i[f] == k && (u = !0) ? o[f++] = w : o.length && (y = !1)), O = C, w += et(k);
    }
    return f == a && o[0] == 0 && y ? this.result(-100 + (u ? -200 : 0), o, e) : d == a && p == 0 ? this.ret(-200 - e.length + (m == e.length ? 0 : -100), [0, m]) : l > -1 ? this.ret(-700 - e.length, [l, l + this.pattern.length]) : d == a ? this.ret(-900 - e.length, [p, m]) : f == a ? this.result(-100 + (u ? -200 : 0) + -700 + (y ? 0 : -1100), o, e) : t.length == 2 ? null : this.result((s[0] ? -700 : 0) + -200 + -1100, s, e);
  }
  result(e, t, i) {
    let s = [], r = 0;
    for (let o of t) {
      let l = o + (this.astral ? et(ke(i, o)) : 1);
      r && s[r - 1] == o ? s[r - 1] = l : (s[r++] = o, s[r++] = l);
    }
    return this.ret(e - i.length, s);
  }
}
class Q0 {
  constructor(e) {
    this.pattern = e, this.matched = [], this.score = 0, this.folded = e.toLowerCase();
  }
  match(e) {
    if (e.length < this.pattern.length)
      return null;
    let t = e.slice(0, this.pattern.length), i = t == this.pattern ? 0 : t.toLowerCase() == this.folded ? -200 : null;
    return i == null ? null : (this.matched = [0, t.length], this.score = i + (e.length == this.pattern.length ? 0 : -100), this);
  }
}
const re = /* @__PURE__ */ T.define({
  combine(n) {
    return at(n, {
      activateOnTyping: !0,
      activateOnCompletion: () => !1,
      activateOnTypingDelay: 100,
      selectOnOpen: !0,
      override: null,
      closeOnBlur: !0,
      maxRenderedOptions: 100,
      defaultKeymap: !0,
      tooltipClass: () => "",
      optionClass: () => "",
      aboveCursor: !1,
      icons: !0,
      addToOptions: [],
      positionInfo: Z0,
      filterStrict: !1,
      compareCompletions: (e, t) => (e.sortText || e.label).localeCompare(t.sortText || t.label),
      interactionDelay: 75,
      updateSyncTime: 100
    }, {
      defaultKeymap: (e, t) => e && t,
      closeOnBlur: (e, t) => e && t,
      icons: (e, t) => e && t,
      tooltipClass: (e, t) => (i) => da(e(i), t(i)),
      optionClass: (e, t) => (i) => da(e(i), t(i)),
      addToOptions: (e, t) => e.concat(t),
      filterStrict: (e, t) => e || t
    });
  }
});
function da(n, e) {
  return n ? e ? n + " " + e : n : e;
}
function Z0(n, e, t, i, s, r) {
  let o = n.textDirection == _.RTL, l = o, a = !1, h = "top", c, f, u = e.left - s.left, d = s.right - e.right, p = i.right - i.left, m = i.bottom - i.top;
  if (l && u < Math.min(p, d) ? l = !1 : !l && d < Math.min(p, u) && (l = !0), p <= (l ? u : d))
    c = Math.max(s.top, Math.min(t.top, s.bottom - m)) - e.top, f = Math.min(400, l ? u : d);
  else {
    a = !0, f = Math.min(
      400,
      (o ? e.right : s.right - e.left) - 30
      /* Info.Margin */
    );
    let w = s.bottom - e.bottom;
    w >= m || w > e.top ? c = t.bottom - e.top : (h = "bottom", c = e.bottom - t.top);
  }
  let g = (e.bottom - e.top) / r.offsetHeight, y = (e.right - e.left) / r.offsetWidth;
  return {
    style: `${h}: ${c / g}px; max-width: ${f / y}px`,
    class: "cm-completionInfo-" + (a ? o ? "left-narrow" : "right-narrow" : l ? "left" : "right")
  };
}
const Eo = /* @__PURE__ */ R.define();
function ey(n) {
  let e = n.addToOptions.slice();
  return n.icons && e.push({
    render(t) {
      let i = document.createElement("div");
      return i.classList.add("cm-completionIcon"), t.type && i.classList.add(...t.type.split(/\s+/g).map((s) => "cm-completionIcon-" + s)), i.setAttribute("aria-hidden", "true"), i;
    },
    position: 20
  }), e.push({
    render(t, i, s, r) {
      let o = document.createElement("span");
      o.className = "cm-completionLabel";
      let l = t.displayLabel || t.label, a = 0;
      for (let h = 0; h < r.length; ) {
        let c = r[h++], f = r[h++];
        c > a && o.appendChild(document.createTextNode(l.slice(a, c)));
        let u = o.appendChild(document.createElement("span"));
        u.appendChild(document.createTextNode(l.slice(c, f))), u.className = "cm-completionMatchedText", a = f;
      }
      return a < l.length && o.appendChild(document.createTextNode(l.slice(a))), o;
    },
    position: 50
  }, {
    render(t) {
      if (!t.detail)
        return null;
      let i = document.createElement("span");
      return i.className = "cm-completionDetail", i.textContent = t.detail, i;
    },
    position: 80
  }), e.sort((t, i) => t.position - i.position).map((t) => t.render);
}
function Zs(n, e, t) {
  if (n <= t)
    return { from: 0, to: n };
  if (e < 0 && (e = 0), e <= n >> 1) {
    let s = Math.floor(e / t);
    return { from: s * t, to: (s + 1) * t };
  }
  let i = Math.ceil((n - e) / t);
  return { from: n - i * t, to: n - (i - 1) * t };
}
class ty {
  constructor(e, t, i) {
    this.view = e, this.stateField = t, this.applyCompletion = i, this.info = null, this.infoDestroy = null, this.placeInfoReq = {
      read: () => this.measureInfo(),
      write: (a) => this.placeInfo(a),
      key: this
    }, this.space = null, this.currentClass = "";
    let s = e.state.field(t), { options: r, selected: o } = s.open, l = e.state.facet(re);
    this.optionContent = ey(l), this.optionClass = l.optionClass, this.tooltipClass = l.tooltipClass, this.range = Zs(r.length, o, l.maxRenderedOptions), this.dom = document.createElement("div"), this.dom.className = "cm-tooltip-autocomplete", this.updateTooltipClass(e.state), this.dom.addEventListener("mousedown", (a) => {
      let { options: h } = e.state.field(t).open;
      for (let c = a.target, f; c && c != this.dom; c = c.parentNode)
        if (c.nodeName == "LI" && (f = /-(\d+)$/.exec(c.id)) && +f[1] < h.length) {
          this.applyCompletion(e, h[+f[1]]), a.preventDefault();
          return;
        }
      if (a.target == this.list) {
        let c = this.list.classList.contains("cm-completionListIncompleteTop") && a.clientY < this.list.firstChild.getBoundingClientRect().top ? this.range.from - 1 : this.list.classList.contains("cm-completionListIncompleteBottom") && a.clientY > this.list.lastChild.getBoundingClientRect().bottom ? this.range.to : null;
        c != null && (e.dispatch({ effects: Eo.of(c) }), a.preventDefault());
      }
    }), this.dom.addEventListener("focusout", (a) => {
      let h = e.state.field(this.stateField, !1);
      h && h.tooltip && e.state.facet(re).closeOnBlur && a.relatedTarget != e.contentDOM && e.dispatch({ effects: _i.of(null) });
    }), this.showOptions(r, s.id);
  }
  mount() {
    this.updateSel();
  }
  showOptions(e, t) {
    this.list && this.list.remove(), this.list = this.dom.appendChild(this.createListBox(e, t, this.range)), this.list.addEventListener("scroll", () => {
      this.info && this.view.requestMeasure(this.placeInfoReq);
    });
  }
  update(e) {
    var t;
    let i = e.state.field(this.stateField), s = e.startState.field(this.stateField);
    if (this.updateTooltipClass(e.state), i != s) {
      let { options: r, selected: o, disabled: l } = i.open;
      (!s.open || s.open.options != r) && (this.range = Zs(r.length, o, e.state.facet(re).maxRenderedOptions), this.showOptions(r, i.id)), this.updateSel(), l != ((t = s.open) === null || t === void 0 ? void 0 : t.disabled) && this.dom.classList.toggle("cm-tooltip-autocomplete-disabled", !!l);
    }
  }
  updateTooltipClass(e) {
    let t = this.tooltipClass(e);
    if (t != this.currentClass) {
      for (let i of this.currentClass.split(" "))
        i && this.dom.classList.remove(i);
      for (let i of t.split(" "))
        i && this.dom.classList.add(i);
      this.currentClass = t;
    }
  }
  positioned(e) {
    this.space = e, this.info && this.view.requestMeasure(this.placeInfoReq);
  }
  updateSel() {
    let e = this.view.state.field(this.stateField), t = e.open;
    (t.selected > -1 && t.selected < this.range.from || t.selected >= this.range.to) && (this.range = Zs(t.options.length, t.selected, this.view.state.facet(re).maxRenderedOptions), this.showOptions(t.options, e.id));
    let i = this.updateSelectedOption(t.selected);
    if (i) {
      this.destroyInfo();
      let { completion: s } = t.options[t.selected], { info: r } = s;
      if (!r)
        return;
      let o = typeof r == "string" ? document.createTextNode(r) : r(s);
      if (!o)
        return;
      "then" in o ? o.then((l) => {
        l && this.view.state.field(this.stateField, !1) == e && this.addInfoPane(l, s);
      }).catch((l) => Ae(this.view.state, l, "completion info")) : (this.addInfoPane(o, s), i.setAttribute("aria-describedby", this.info.id));
    }
  }
  addInfoPane(e, t) {
    this.destroyInfo();
    let i = this.info = document.createElement("div");
    if (i.className = "cm-tooltip cm-completionInfo", i.id = "cm-completionInfo-" + Math.floor(Math.random() * 65535).toString(16), e.nodeType != null)
      i.appendChild(e), this.infoDestroy = null;
    else {
      let { dom: s, destroy: r } = e;
      i.appendChild(s), this.infoDestroy = r || null;
    }
    this.dom.appendChild(i), this.view.requestMeasure(this.placeInfoReq);
  }
  updateSelectedOption(e) {
    let t = null;
    for (let i = this.list.firstChild, s = this.range.from; i; i = i.nextSibling, s++)
      i.nodeName != "LI" || !i.id ? s-- : s == e ? i.hasAttribute("aria-selected") || (i.setAttribute("aria-selected", "true"), t = i) : i.hasAttribute("aria-selected") && (i.removeAttribute("aria-selected"), i.removeAttribute("aria-describedby"));
    return t && ny(this.list, t), t;
  }
  measureInfo() {
    let e = this.dom.querySelector("[aria-selected]");
    if (!e || !this.info)
      return null;
    let t = this.dom.getBoundingClientRect(), i = this.info.getBoundingClientRect(), s = e.getBoundingClientRect(), r = this.space;
    if (!r) {
      let o = this.dom.ownerDocument.documentElement;
      r = { left: 0, top: 0, right: o.clientWidth, bottom: o.clientHeight };
    }
    return s.top > Math.min(r.bottom, t.bottom) - 10 || s.bottom < Math.max(r.top, t.top) + 10 ? null : this.view.state.facet(re).positionInfo(this.view, t, s, i, r, this.dom);
  }
  placeInfo(e) {
    this.info && (e ? (e.style && (this.info.style.cssText = e.style), this.info.className = "cm-tooltip cm-completionInfo " + (e.class || "")) : this.info.style.cssText = "top: -1e6px");
  }
  createListBox(e, t, i) {
    const s = document.createElement("ul");
    s.id = t, s.setAttribute("role", "listbox"), s.setAttribute("aria-expanded", "true"), s.setAttribute("aria-label", this.view.state.phrase("Completions")), s.addEventListener("mousedown", (o) => {
      o.target == s && o.preventDefault();
    });
    let r = null;
    for (let o = i.from; o < i.to; o++) {
      let { completion: l, match: a } = e[o], { section: h } = l;
      if (h) {
        let u = typeof h == "string" ? h : h.name;
        if (u != r && (o > i.from || i.from == 0))
          if (r = u, typeof h != "string" && h.header)
            s.appendChild(h.header(h));
          else {
            let d = s.appendChild(document.createElement("completion-section"));
            d.textContent = u;
          }
      }
      const c = s.appendChild(document.createElement("li"));
      c.id = t + "-" + o, c.setAttribute("role", "option");
      let f = this.optionClass(l);
      f && (c.className = f);
      for (let u of this.optionContent) {
        let d = u(l, this.view.state, this.view, a);
        d && c.appendChild(d);
      }
    }
    return i.from && s.classList.add("cm-completionListIncompleteTop"), i.to < e.length && s.classList.add("cm-completionListIncompleteBottom"), s;
  }
  destroyInfo() {
    this.info && (this.infoDestroy && this.infoDestroy(), this.info.remove(), this.info = null);
  }
  destroy() {
    this.destroyInfo();
  }
}
function iy(n, e) {
  return (t) => new ty(t, n, e);
}
function ny(n, e) {
  let t = n.getBoundingClientRect(), i = e.getBoundingClientRect(), s = t.height / n.offsetHeight;
  i.top < t.top ? n.scrollTop -= (t.top - i.top) / s : i.bottom > t.bottom && (n.scrollTop += (i.bottom - t.bottom) / s);
}
function pa(n) {
  return (n.boost || 0) * 100 + (n.apply ? 10 : 0) + (n.info ? 5 : 0) + (n.type ? 1 : 0);
}
function sy(n, e) {
  let t = [], i = null, s = null, r = (c) => {
    t.push(c);
    let { section: f } = c.completion;
    if (f) {
      i || (i = []);
      let u = typeof f == "string" ? f : f.name;
      i.some((d) => d.name == u) || i.push(typeof f == "string" ? { name: u } : f);
    }
  }, o = e.facet(re);
  for (let c of n)
    if (c.hasResult()) {
      let f = c.result.getMatch;
      if (c.result.filter === !1)
        for (let u of c.result.options)
          r(new fa(u, c.source, f ? f(u) : [], 1e9 - t.length));
      else {
        let u = e.sliceDoc(c.from, c.to), d, p = o.filterStrict ? new Q0(u) : new X0(u);
        for (let m of c.result.options)
          if (d = p.match(m.label)) {
            let g = m.displayLabel ? f ? f(m, d.matched) : [] : d.matched, y = d.score + (m.boost || 0);
            if (r(new fa(m, c.source, g, y)), typeof m.section == "object" && m.section.rank === "dynamic") {
              let { name: w } = m.section;
              s || (s = /* @__PURE__ */ Object.create(null)), s[w] = Math.max(y, s[w] || -1e9);
            }
          }
      }
    }
  if (i) {
    let c = /* @__PURE__ */ Object.create(null), f = 0, u = (d, p) => (d.rank === "dynamic" && p.rank === "dynamic" ? s[p.name] - s[d.name] : 0) || (typeof d.rank == "number" ? d.rank : 1e9) - (typeof p.rank == "number" ? p.rank : 1e9) || (d.name < p.name ? -1 : 1);
    for (let d of i.sort(u))
      f -= 1e5, c[d.name] = f;
    for (let d of t) {
      let { section: p } = d.completion;
      p && (d.score += c[typeof p == "string" ? p : p.name]);
    }
  }
  let l = [], a = null, h = o.compareCompletions;
  for (let c of t.sort((f, u) => u.score - f.score || h(f.completion, u.completion))) {
    let f = c.completion;
    !a || a.label != f.label || a.detail != f.detail || a.type != null && f.type != null && a.type != f.type || a.apply != f.apply || a.boost != f.boost ? l.push(c) : pa(c.completion) > pa(a) && (l[l.length - 1] = c), a = c.completion;
  }
  return l;
}
class ei {
  constructor(e, t, i, s, r, o) {
    this.options = e, this.attrs = t, this.tooltip = i, this.timestamp = s, this.selected = r, this.disabled = o;
  }
  setSelected(e, t) {
    return e == this.selected || e >= this.options.length ? this : new ei(this.options, ma(t, e), this.tooltip, this.timestamp, e, this.disabled);
  }
  static build(e, t, i, s, r, o) {
    if (s && !o && e.some((h) => h.isPending))
      return s.setDisabled();
    let l = sy(e, t);
    if (!l.length)
      return s && e.some((h) => h.isPending) ? s.setDisabled() : null;
    let a = t.facet(re).selectOnOpen ? 0 : -1;
    if (s && s.selected != a && s.selected != -1) {
      let h = s.options[s.selected].completion;
      for (let c = 0; c < l.length; c++)
        if (l[c].completion == h) {
          a = c;
          break;
        }
    }
    return new ei(l, ma(i, a), {
      pos: e.reduce((h, c) => c.hasResult() ? Math.min(h, c.from) : h, 1e8),
      create: cy,
      above: r.aboveCursor
    }, s ? s.timestamp : Date.now(), a, !1);
  }
  map(e) {
    return new ei(this.options, this.attrs, { ...this.tooltip, pos: e.mapPos(this.tooltip.pos) }, this.timestamp, this.selected, this.disabled);
  }
  setDisabled() {
    return new ei(this.options, this.attrs, this.tooltip, this.timestamp, this.selected, !0);
  }
}
class cs {
  constructor(e, t, i) {
    this.active = e, this.id = t, this.open = i;
  }
  static start() {
    return new cs(ay, "cm-ac-" + Math.floor(Math.random() * 2e6).toString(36), null);
  }
  update(e) {
    let { state: t } = e, i = t.facet(re), r = (i.override || t.languageDataAt("autocomplete", Vt(t)).map(J0)).map((a) => (this.active.find((c) => c.source == a) || new We(
      a,
      this.active.some(
        (c) => c.state != 0
        /* State.Inactive */
      ) ? 1 : 0
      /* State.Inactive */
    )).update(e, i));
    r.length == this.active.length && r.every((a, h) => a == this.active[h]) && (r = this.active);
    let o = this.open, l = e.effects.some((a) => a.is(Ro));
    o && e.docChanged && (o = o.map(e.changes)), e.selection || r.some((a) => a.hasResult() && e.changes.touchesRange(a.from, a.to)) || !ry(r, this.active) || l ? o = ei.build(r, t, this.id, o, i, l) : o && o.disabled && !r.some((a) => a.isPending) && (o = null), !o && r.every((a) => !a.isPending) && r.some((a) => a.hasResult()) && (r = r.map((a) => a.hasResult() ? new We(
      a.source,
      0
      /* State.Inactive */
    ) : a));
    for (let a of e.effects)
      a.is(Eo) && (o = o && o.setSelected(a.value, this.id));
    return r == this.active && o == this.open ? this : new cs(r, this.id, o);
  }
  get tooltip() {
    return this.open ? this.open.tooltip : null;
  }
  get attrs() {
    return this.open ? this.open.attrs : this.active.length ? oy : ly;
  }
}
function ry(n, e) {
  if (n == e)
    return !0;
  for (let t = 0, i = 0; ; ) {
    for (; t < n.length && !n[t].hasResult(); )
      t++;
    for (; i < e.length && !e[i].hasResult(); )
      i++;
    let s = t == n.length, r = i == e.length;
    if (s || r)
      return s == r;
    if (n[t++].result != e[i++].result)
      return !1;
  }
}
const oy = {
  "aria-autocomplete": "list"
}, ly = {};
function ma(n, e) {
  let t = {
    "aria-autocomplete": "list",
    "aria-haspopup": "listbox",
    "aria-controls": n
  };
  return e > -1 && (t["aria-activedescendant"] = n + "-" + e), t;
}
const ay = [];
function mf(n, e) {
  if (n.isUserEvent("input.complete")) {
    let i = n.annotation(Lo);
    if (i && e.activateOnCompletion(i))
      return 12;
  }
  let t = n.isUserEvent("input.type");
  return t && e.activateOnTyping ? 5 : t ? 1 : n.isUserEvent("delete.backward") ? 2 : n.selection ? 8 : n.docChanged ? 16 : 0;
}
class We {
  constructor(e, t, i = !1) {
    this.source = e, this.state = t, this.explicit = i;
  }
  hasResult() {
    return !1;
  }
  get isPending() {
    return this.state == 1;
  }
  update(e, t) {
    let i = mf(e, t), s = this;
    (i & 8 || i & 16 && this.touches(e)) && (s = new We(
      s.source,
      0
      /* State.Inactive */
    )), i & 4 && s.state == 0 && (s = new We(
      this.source,
      1
      /* State.Pending */
    )), s = s.updateFor(e, i);
    for (let r of e.effects)
      if (r.is(hs))
        s = new We(s.source, 1, r.value);
      else if (r.is(_i))
        s = new We(
          s.source,
          0
          /* State.Inactive */
        );
      else if (r.is(Ro))
        for (let o of r.value)
          o.source == s.source && (s = o);
    return s;
  }
  updateFor(e, t) {
    return this.map(e.changes);
  }
  map(e) {
    return this;
  }
  touches(e) {
    return e.changes.touchesRange(Vt(e.state));
  }
}
class oi extends We {
  constructor(e, t, i, s, r, o) {
    super(e, 3, t), this.limit = i, this.result = s, this.from = r, this.to = o;
  }
  hasResult() {
    return !0;
  }
  updateFor(e, t) {
    var i;
    if (!(t & 3))
      return this.map(e.changes);
    let s = this.result;
    s.map && !e.changes.empty && (s = s.map(s, e.changes));
    let r = e.changes.mapPos(this.from), o = e.changes.mapPos(this.to, 1), l = Vt(e.state);
    if (l > o || !s || t & 2 && (Vt(e.startState) == this.from || l < this.limit))
      return new We(
        this.source,
        t & 4 ? 1 : 0
        /* State.Inactive */
      );
    let a = e.changes.mapPos(this.limit);
    return hy(s.validFor, e.state, r, o) ? new oi(this.source, this.explicit, a, s, r, o) : s.update && (s = s.update(s, r, o, new df(e.state, l, !1))) ? new oi(this.source, this.explicit, a, s, s.from, (i = s.to) !== null && i !== void 0 ? i : Vt(e.state)) : new We(this.source, 1, this.explicit);
  }
  map(e) {
    if (e.empty)
      return this;
    let t = this.result.map ? this.result.map(this.result, e) : this.result;
    return t ? new oi(this.source, this.explicit, e.mapPos(this.limit), t, e.mapPos(this.from), e.mapPos(this.to, 1)) : new We(
      this.source,
      0
      /* State.Inactive */
    );
  }
  touches(e) {
    return e.changes.touchesRange(this.from, this.to);
  }
}
function hy(n, e, t, i) {
  if (!n)
    return !1;
  let s = e.sliceDoc(t, i);
  return typeof n == "function" ? n(s, t, i, e) : pf(n, !0).test(s);
}
const Ro = /* @__PURE__ */ R.define({
  map(n, e) {
    return n.map((t) => t.map(e));
  }
}), Se = /* @__PURE__ */ me.define({
  create() {
    return cs.start();
  },
  update(n, e) {
    return n.update(e);
  },
  provide: (n) => [
    yo.from(n, (e) => e.tooltip),
    M.contentAttributes.from(n, (e) => e.attrs)
  ]
});
function Po(n, e) {
  const t = e.completion.apply || e.completion.label;
  let i = n.state.field(Se).active.find((s) => s.source == e.source);
  return i instanceof oi ? (typeof t == "string" ? n.dispatch({
    ...Y0(n.state, t, i.from, i.to),
    annotations: Lo.of(e.completion)
  }) : t(n, e.completion, i.from, i.to), !0) : !1;
}
const cy = /* @__PURE__ */ iy(Se, Po);
function Ln(n, e = "option") {
  return (t) => {
    let i = t.state.field(Se, !1);
    if (!i || !i.open || i.open.disabled || Date.now() - i.open.timestamp < t.state.facet(re).interactionDelay)
      return !1;
    let s = 1, r;
    e == "page" && (r = Xh(t, i.open.tooltip)) && (s = Math.max(2, Math.floor(r.dom.offsetHeight / r.dom.querySelector("li").offsetHeight) - 1));
    let { length: o } = i.open.options, l = i.open.selected > -1 ? i.open.selected + s * (n ? 1 : -1) : n ? 0 : o - 1;
    return l < 0 ? l = e == "page" ? 0 : o - 1 : l >= o && (l = e == "page" ? o - 1 : 0), t.dispatch({ effects: Eo.of(l) }), !0;
  };
}
const fy = (n) => {
  let e = n.state.field(Se, !1);
  return n.state.readOnly || !e || !e.open || e.open.selected < 0 || e.open.disabled || Date.now() - e.open.timestamp < n.state.facet(re).interactionDelay ? !1 : Po(n, e.open.options[e.open.selected]);
}, er = (n) => n.state.field(Se, !1) ? (n.dispatch({ effects: hs.of(!0) }), !0) : !1, uy = (n) => {
  let e = n.state.field(Se, !1);
  return !e || !e.active.some(
    (t) => t.state != 0
    /* State.Inactive */
  ) ? !1 : (n.dispatch({ effects: _i.of(null) }), !0);
};
class dy {
  constructor(e, t) {
    this.active = e, this.context = t, this.time = Date.now(), this.updates = [], this.done = void 0;
  }
}
const py = 50, my = 1e3, gy = /* @__PURE__ */ Q.fromClass(class {
  constructor(n) {
    this.view = n, this.debounceUpdate = -1, this.running = [], this.debounceAccept = -1, this.pendingStart = !1, this.composing = 0;
    for (let e of n.state.field(Se).active)
      e.isPending && this.startQuery(e);
  }
  update(n) {
    let e = n.state.field(Se), t = n.state.facet(re);
    if (!n.selectionSet && !n.docChanged && n.startState.field(Se) == e)
      return;
    let i = n.transactions.some((r) => {
      let o = mf(r, t);
      return o & 8 || (r.selection || r.docChanged) && !(o & 3);
    });
    for (let r = 0; r < this.running.length; r++) {
      let o = this.running[r];
      if (i || o.context.abortOnDocChange && n.docChanged || o.updates.length + n.transactions.length > py && Date.now() - o.time > my) {
        for (let l of o.context.abortListeners)
          try {
            l();
          } catch (a) {
            Ae(this.view.state, a);
          }
        o.context.abortListeners = null, this.running.splice(r--, 1);
      } else
        o.updates.push(...n.transactions);
    }
    this.debounceUpdate > -1 && clearTimeout(this.debounceUpdate), n.transactions.some((r) => r.effects.some((o) => o.is(hs))) && (this.pendingStart = !0);
    let s = this.pendingStart ? 50 : t.activateOnTypingDelay;
    if (this.debounceUpdate = e.active.some((r) => r.isPending && !this.running.some((o) => o.active.source == r.source)) ? setTimeout(() => this.startUpdate(), s) : -1, this.composing != 0)
      for (let r of n.transactions)
        r.isUserEvent("input.type") ? this.composing = 2 : this.composing == 2 && r.selection && (this.composing = 3);
  }
  startUpdate() {
    this.debounceUpdate = -1, this.pendingStart = !1;
    let { state: n } = this.view, e = n.field(Se);
    for (let t of e.active)
      t.isPending && !this.running.some((i) => i.active.source == t.source) && this.startQuery(t);
    this.running.length && e.open && e.open.disabled && (this.debounceAccept = setTimeout(() => this.accept(), this.view.state.facet(re).updateSyncTime));
  }
  startQuery(n) {
    let { state: e } = this.view, t = Vt(e), i = new df(e, t, n.explicit, this.view), s = new dy(n, i);
    this.running.push(s), Promise.resolve(n.source(i)).then((r) => {
      s.context.aborted || (s.done = r || null, this.scheduleAccept());
    }, (r) => {
      this.view.dispatch({ effects: _i.of(null) }), Ae(this.view.state, r);
    });
  }
  scheduleAccept() {
    this.running.every((n) => n.done !== void 0) ? this.accept() : this.debounceAccept < 0 && (this.debounceAccept = setTimeout(() => this.accept(), this.view.state.facet(re).updateSyncTime));
  }
  // For each finished query in this.running, try to create a result
  // or, if appropriate, restart the query.
  accept() {
    var n;
    this.debounceAccept > -1 && clearTimeout(this.debounceAccept), this.debounceAccept = -1;
    let e = [], t = this.view.state.facet(re), i = this.view.state.field(Se);
    for (let s = 0; s < this.running.length; s++) {
      let r = this.running[s];
      if (r.done === void 0)
        continue;
      if (this.running.splice(s--, 1), r.done) {
        let l = Vt(r.updates.length ? r.updates[0].startState : this.view.state), a = Math.min(l, r.done.from + (r.active.explicit ? 0 : 1)), h = new oi(r.active.source, r.active.explicit, a, r.done, r.done.from, (n = r.done.to) !== null && n !== void 0 ? n : l);
        for (let c of r.updates)
          h = h.update(c, t);
        if (h.hasResult()) {
          e.push(h);
          continue;
        }
      }
      let o = i.active.find((l) => l.source == r.active.source);
      if (o && o.isPending)
        if (r.done == null) {
          let l = new We(
            r.active.source,
            0
            /* State.Inactive */
          );
          for (let a of r.updates)
            l = l.update(a, t);
          l.isPending || e.push(l);
        } else
          this.startQuery(o);
    }
    (e.length || i.open && i.open.disabled) && this.view.dispatch({ effects: Ro.of(e) });
  }
}, {
  eventHandlers: {
    blur(n) {
      let e = this.view.state.field(Se, !1);
      if (e && e.tooltip && this.view.state.facet(re).closeOnBlur) {
        let t = e.open && Xh(this.view, e.open.tooltip);
        (!t || !t.dom.contains(n.relatedTarget)) && setTimeout(() => this.view.dispatch({ effects: _i.of(null) }), 10);
      }
    },
    compositionstart() {
      this.composing = 1;
    },
    compositionend() {
      this.composing == 3 && setTimeout(() => this.view.dispatch({ effects: hs.of(!1) }), 20), this.composing = 0;
    }
  }
}), yy = typeof navigator == "object" && /* @__PURE__ */ /Win/.test(navigator.platform), by = /* @__PURE__ */ Tt.highest(/* @__PURE__ */ M.domEventHandlers({
  keydown(n, e) {
    let t = e.state.field(Se, !1);
    if (!t || !t.open || t.open.disabled || t.open.selected < 0 || n.key.length > 1 || n.ctrlKey && !(yy && n.altKey) || n.metaKey)
      return !1;
    let i = t.open.options[t.open.selected], s = t.active.find((o) => o.source == i.source), r = i.completion.commitCharacters || s.result.commitCharacters;
    return r && r.indexOf(n.key) > -1 && Po(e, i), !1;
  }
})), gf = /* @__PURE__ */ M.baseTheme({
  ".cm-tooltip.cm-tooltip-autocomplete": {
    "& > ul": {
      fontFamily: "monospace",
      whiteSpace: "nowrap",
      overflow: "hidden auto",
      maxWidth_fallback: "700px",
      maxWidth: "min(700px, 95vw)",
      minWidth: "250px",
      maxHeight: "10em",
      height: "100%",
      listStyle: "none",
      margin: 0,
      padding: 0,
      "& > li, & > completion-section": {
        padding: "1px 3px",
        lineHeight: 1.2
      },
      "& > li": {
        overflowX: "hidden",
        textOverflow: "ellipsis",
        cursor: "pointer"
      },
      "& > completion-section": {
        display: "list-item",
        borderBottom: "1px solid silver",
        paddingLeft: "0.5em",
        opacity: 0.7
      }
    }
  },
  "&light .cm-tooltip-autocomplete ul li[aria-selected]": {
    background: "#17c",
    color: "white"
  },
  "&light .cm-tooltip-autocomplete-disabled ul li[aria-selected]": {
    background: "#777"
  },
  "&dark .cm-tooltip-autocomplete ul li[aria-selected]": {
    background: "#347",
    color: "white"
  },
  "&dark .cm-tooltip-autocomplete-disabled ul li[aria-selected]": {
    background: "#444"
  },
  ".cm-completionListIncompleteTop:before, .cm-completionListIncompleteBottom:after": {
    content: '"···"',
    opacity: 0.5,
    display: "block",
    textAlign: "center",
    cursor: "pointer"
  },
  ".cm-tooltip.cm-completionInfo": {
    position: "absolute",
    padding: "3px 9px",
    width: "max-content",
    maxWidth: "400px",
    boxSizing: "border-box",
    whiteSpace: "pre-line"
  },
  ".cm-completionInfo.cm-completionInfo-left": { right: "100%" },
  ".cm-completionInfo.cm-completionInfo-right": { left: "100%" },
  ".cm-completionInfo.cm-completionInfo-left-narrow": { right: "30px" },
  ".cm-completionInfo.cm-completionInfo-right-narrow": { left: "30px" },
  "&light .cm-snippetField": { backgroundColor: "#00000022" },
  "&dark .cm-snippetField": { backgroundColor: "#ffffff22" },
  ".cm-snippetFieldPosition": {
    verticalAlign: "text-top",
    width: 0,
    height: "1.15em",
    display: "inline-block",
    margin: "0 -0.7px -.7em",
    borderLeft: "1.4px dotted #888"
  },
  ".cm-completionMatchedText": {
    textDecoration: "underline"
  },
  ".cm-completionDetail": {
    marginLeft: "0.5em",
    fontStyle: "italic"
  },
  ".cm-completionIcon": {
    fontSize: "90%",
    width: ".8em",
    display: "inline-block",
    textAlign: "center",
    paddingRight: ".6em",
    opacity: "0.6",
    boxSizing: "content-box"
  },
  ".cm-completionIcon-function, .cm-completionIcon-method": {
    "&:after": { content: "'ƒ'" }
  },
  ".cm-completionIcon-class": {
    "&:after": { content: "'○'" }
  },
  ".cm-completionIcon-interface": {
    "&:after": { content: "'◌'" }
  },
  ".cm-completionIcon-variable": {
    "&:after": { content: "'𝑥'" }
  },
  ".cm-completionIcon-constant": {
    "&:after": { content: "'𝐶'" }
  },
  ".cm-completionIcon-type": {
    "&:after": { content: "'𝑡'" }
  },
  ".cm-completionIcon-enum": {
    "&:after": { content: "'∪'" }
  },
  ".cm-completionIcon-property": {
    "&:after": { content: "'□'" }
  },
  ".cm-completionIcon-keyword": {
    "&:after": { content: "'🔑︎'" }
    // Disable emoji rendering
  },
  ".cm-completionIcon-namespace": {
    "&:after": { content: "'▢'" }
  },
  ".cm-completionIcon-text": {
    "&:after": { content: "'abc'", fontSize: "50%", verticalAlign: "middle" }
  }
});
class xy {
  constructor(e, t, i, s) {
    this.field = e, this.line = t, this.from = i, this.to = s;
  }
}
class Io {
  constructor(e, t, i) {
    this.field = e, this.from = t, this.to = i;
  }
  map(e) {
    let t = e.mapPos(this.from, -1, fe.TrackDel), i = e.mapPos(this.to, 1, fe.TrackDel);
    return t == null || i == null ? null : new Io(this.field, t, i);
  }
}
class No {
  constructor(e, t) {
    this.lines = e, this.fieldPositions = t;
  }
  instantiate(e, t) {
    let i = [], s = [t], r = e.doc.lineAt(t), o = /^\s*/.exec(r.text)[0];
    for (let a of this.lines) {
      if (i.length) {
        let h = o, c = /^\t*/.exec(a)[0].length;
        for (let f = 0; f < c; f++)
          h += e.facet(nn);
        s.push(t + h.length - c), a = h + a.slice(c);
      }
      i.push(a), t += a.length + 1;
    }
    let l = this.fieldPositions.map((a) => new Io(a.field, s[a.line] + a.from, s[a.line] + a.to));
    return { text: i, ranges: l };
  }
  static parse(e) {
    let t = [], i = [], s = [], r;
    for (let o of e.split(/\r\n?|\n/)) {
      for (; r = /[#$]\{(?:(\d+)(?::([^{}]*))?|((?:\\[{}]|[^{}])*))\}/.exec(o); ) {
        let l = r[1] ? +r[1] : null, a = r[2] || r[3] || "", h = -1;
        l === 0 && (l = 1e9);
        let c = a.replace(/\\[{}]/g, (f) => f[1]);
        for (let f = 0; f < t.length; f++)
          (l != null ? t[f].seq == l : c && t[f].name == c) && (h = f);
        if (h < 0) {
          let f = 0;
          for (; f < t.length && (l == null || t[f].seq != null && t[f].seq < l); )
            f++;
          t.splice(f, 0, { seq: l, name: c }), h = f;
          for (let u of s)
            u.field >= h && u.field++;
        }
        for (let f of s)
          if (f.line == i.length && f.from > r.index) {
            let u = r[2] ? 3 + (r[1] || "").length : 2;
            f.from -= u, f.to -= u;
          }
        s.push(new xy(h, i.length, r.index, r.index + c.length)), o = o.slice(0, r.index) + a + o.slice(r.index + r[0].length);
      }
      o = o.replace(/\\([{}])/g, (l, a, h) => {
        for (let c of s)
          c.line == i.length && c.from > h && (c.from--, c.to--);
        return a;
      }), i.push(o);
    }
    return new No(i, s);
  }
}
let wy = /* @__PURE__ */ B.widget({ widget: /* @__PURE__ */ new class extends Ke {
  toDOM() {
    let n = document.createElement("span");
    return n.className = "cm-snippetFieldPosition", n;
  }
  ignoreEvent() {
    return !1;
  }
}() }), vy = /* @__PURE__ */ B.mark({ class: "cm-snippetField" });
class yi {
  constructor(e, t) {
    this.ranges = e, this.active = t, this.deco = B.set(e.map((i) => (i.from == i.to ? wy : vy).range(i.from, i.to)), !0);
  }
  map(e) {
    let t = [];
    for (let i of this.ranges) {
      let s = i.map(e);
      if (!s)
        return null;
      t.push(s);
    }
    return new yi(t, this.active);
  }
  selectionInsideField(e) {
    return e.ranges.every((t) => this.ranges.some((i) => i.field == this.active && i.from <= t.from && i.to >= t.to));
  }
}
const an = /* @__PURE__ */ R.define({
  map(n, e) {
    return n && n.map(e);
  }
}), ky = /* @__PURE__ */ R.define(), Gi = /* @__PURE__ */ me.define({
  create() {
    return null;
  },
  update(n, e) {
    for (let t of e.effects) {
      if (t.is(an))
        return t.value;
      if (t.is(ky) && n)
        return new yi(n.ranges, t.value);
    }
    return n && e.docChanged && (n = n.map(e.changes)), n && e.selection && !n.selectionInsideField(e.selection) && (n = null), n;
  },
  provide: (n) => M.decorations.from(n, (e) => e ? e.deco : B.none)
});
function Wo(n, e) {
  return b.create(n.filter((t) => t.field == e).map((t) => b.range(t.from, t.to)));
}
function Sy(n) {
  let e = No.parse(n);
  return (t, i, s, r) => {
    let { text: o, ranges: l } = e.instantiate(t.state, s), { main: a } = t.state.selection, h = {
      changes: { from: s, to: r == a.from ? a.to : r, insert: z.of(o) },
      scrollIntoView: !0,
      annotations: i ? [Lo.of(i), ie.userEvent.of("input.complete")] : void 0
    };
    if (l.length && (h.selection = Wo(l, 0)), l.some((c) => c.field > 0)) {
      let c = new yi(l, 0), f = h.effects = [an.of(c)];
      t.state.field(Gi, !1) === void 0 && f.push(R.appendConfig.of([Gi, Dy, Oy, gf]));
    }
    t.dispatch(t.state.update(h));
  };
}
function yf(n) {
  return ({ state: e, dispatch: t }) => {
    let i = e.field(Gi, !1);
    if (!i || n < 0 && i.active == 0)
      return !1;
    let s = i.active + n, r = n > 0 && !i.ranges.some((o) => o.field == s + n);
    return t(e.update({
      selection: Wo(i.ranges, s),
      effects: an.of(r ? null : new yi(i.ranges, s)),
      scrollIntoView: !0
    })), !0;
  };
}
const Cy = ({ state: n, dispatch: e }) => n.field(Gi, !1) ? (e(n.update({ effects: an.of(null) })), !0) : !1, Ay = /* @__PURE__ */ yf(1), My = /* @__PURE__ */ yf(-1), Ty = [
  { key: "Tab", run: Ay, shift: My },
  { key: "Escape", run: Cy }
], ga = /* @__PURE__ */ T.define({
  combine(n) {
    return n.length ? n[0] : Ty;
  }
}), Dy = /* @__PURE__ */ Tt.highest(/* @__PURE__ */ tn.compute([ga], (n) => n.facet(ga)));
function Nb(n, e) {
  return { ...e, apply: Sy(n) };
}
const Oy = /* @__PURE__ */ M.domEventHandlers({
  mousedown(n, e) {
    let t = e.state.field(Gi, !1), i;
    if (!t || (i = e.posAtCoords({ x: n.clientX, y: n.clientY })) == null)
      return !1;
    let s = t.ranges.find((r) => r.from <= i && r.to >= i);
    return !s || s.field == t.active ? !1 : (e.dispatch({
      selection: Wo(t.ranges, s.field),
      effects: an.of(t.ranges.some((r) => r.field > s.field) ? new yi(t.ranges, s.field) : null),
      scrollIntoView: !0
    }), !0);
  }
}), Yi = {
  brackets: ["(", "[", "{", "'", '"'],
  before: ")]}:;>",
  stringPrefixes: []
}, Wt = /* @__PURE__ */ R.define({
  map(n, e) {
    let t = e.mapPos(n, -1, fe.TrackAfter);
    return t ?? void 0;
  }
}), Ho = /* @__PURE__ */ new class extends wt {
}();
Ho.startSide = 1;
Ho.endSide = -1;
const bf = /* @__PURE__ */ me.define({
  create() {
    return W.empty;
  },
  update(n, e) {
    if (n = n.map(e.changes), e.selection) {
      let t = e.state.doc.lineAt(e.selection.main.head);
      n = n.update({ filter: (i) => i >= t.from && i <= t.to });
    }
    for (let t of e.effects)
      t.is(Wt) && (n = n.update({ add: [Ho.range(t.value, t.value + 1)] }));
    return n;
  }
});
function By() {
  return [Ey, bf];
}
const tr = "()[]{}<>«»»«［］｛｝";
function xf(n) {
  for (let e = 0; e < tr.length; e += 2)
    if (tr.charCodeAt(e) == n)
      return tr.charAt(e + 1);
  return Zr(n < 128 ? n : n + 1);
}
function wf(n, e) {
  return n.languageDataAt("closeBrackets", e)[0] || Yi;
}
const Ly = typeof navigator == "object" && /* @__PURE__ */ /Android\b/.test(navigator.userAgent), Ey = /* @__PURE__ */ M.inputHandler.of((n, e, t, i) => {
  if ((Ly ? n.composing : n.compositionStarted) || n.state.readOnly)
    return !1;
  let s = n.state.selection.main;
  if (i.length > 2 || i.length == 2 && et(ke(i, 0)) == 1 || e != s.from || t != s.to)
    return !1;
  let r = Iy(n.state, i);
  return r ? (n.dispatch(r), !0) : !1;
}), Ry = ({ state: n, dispatch: e }) => {
  if (n.readOnly)
    return !1;
  let i = wf(n, n.selection.main.head).brackets || Yi.brackets, s = null, r = n.changeByRange((o) => {
    if (o.empty) {
      let l = Ny(n.doc, o.head);
      for (let a of i)
        if (a == l && Cs(n.doc, o.head) == xf(ke(a, 0)))
          return {
            changes: { from: o.head - a.length, to: o.head + a.length },
            range: b.cursor(o.head - a.length)
          };
    }
    return { range: s = o };
  });
  return s || e(n.update(r, { scrollIntoView: !0, userEvent: "delete.backward" })), !s;
}, Py = [
  { key: "Backspace", run: Ry }
];
function Iy(n, e) {
  let t = wf(n, n.selection.main.head), i = t.brackets || Yi.brackets;
  for (let s of i) {
    let r = xf(ke(s, 0));
    if (e == s)
      return r == s ? Fy(n, s, i.indexOf(s + s + s) > -1, t) : Wy(n, s, r, t.before || Yi.before);
    if (e == r && vf(n, n.selection.main.from))
      return Hy(n, s, r);
  }
  return null;
}
function vf(n, e) {
  let t = !1;
  return n.field(bf).between(0, n.doc.length, (i) => {
    i == e && (t = !0);
  }), t;
}
function Cs(n, e) {
  let t = n.sliceString(e, e + 2);
  return t.slice(0, et(ke(t, 0)));
}
function Ny(n, e) {
  let t = n.sliceString(e - 2, e);
  return et(ke(t, 0)) == t.length ? t : t.slice(1);
}
function Wy(n, e, t, i) {
  let s = null, r = n.changeByRange((o) => {
    if (!o.empty)
      return {
        changes: [{ insert: e, from: o.from }, { insert: t, from: o.to }],
        effects: Wt.of(o.to + e.length),
        range: b.range(o.anchor + e.length, o.head + e.length)
      };
    let l = Cs(n.doc, o.head);
    return !l || /\s/.test(l) || i.indexOf(l) > -1 ? {
      changes: { insert: e + t, from: o.head },
      effects: Wt.of(o.head + e.length),
      range: b.cursor(o.head + e.length)
    } : { range: s = o };
  });
  return s ? null : n.update(r, {
    scrollIntoView: !0,
    userEvent: "input.type"
  });
}
function Hy(n, e, t) {
  let i = null, s = n.changeByRange((r) => r.empty && Cs(n.doc, r.head) == t ? {
    changes: { from: r.head, to: r.head + t.length, insert: t },
    range: b.cursor(r.head + t.length)
  } : i = { range: r });
  return i ? null : n.update(s, {
    scrollIntoView: !0,
    userEvent: "input.type"
  });
}
function Fy(n, e, t, i) {
  let s = i.stringPrefixes || Yi.stringPrefixes, r = null, o = n.changeByRange((l) => {
    if (!l.empty)
      return {
        changes: [{ insert: e, from: l.from }, { insert: e, from: l.to }],
        effects: Wt.of(l.to + e.length),
        range: b.range(l.anchor + e.length, l.head + e.length)
      };
    let a = l.head, h = Cs(n.doc, a), c;
    if (h == e) {
      if (ya(n, a))
        return {
          changes: { insert: e + e, from: a },
          effects: Wt.of(a + e.length),
          range: b.cursor(a + e.length)
        };
      if (vf(n, a)) {
        let u = t && n.sliceDoc(a, a + e.length * 3) == e + e + e ? e + e + e : e;
        return {
          changes: { from: a, to: a + u.length, insert: u },
          range: b.cursor(a + u.length)
        };
      }
    } else {
      if (t && n.sliceDoc(a - 2 * e.length, a) == e + e && (c = ba(n, a - 2 * e.length, s)) > -1 && ya(n, c))
        return {
          changes: { insert: e + e + e + e, from: a },
          effects: Wt.of(a + e.length),
          range: b.cursor(a + e.length)
        };
      if (n.charCategorizer(a)(h) != Y.Word && ba(n, a, s) > -1 && !Vy(n, a, e, s))
        return {
          changes: { insert: e + e, from: a },
          effects: Wt.of(a + e.length),
          range: b.cursor(a + e.length)
        };
    }
    return { range: r = l };
  });
  return r ? null : n.update(o, {
    scrollIntoView: !0,
    userEvent: "input.type"
  });
}
function ya(n, e) {
  let t = pe(n).resolveInner(e + 1);
  return t.parent && t.from == e;
}
function Vy(n, e, t, i) {
  let s = pe(n).resolveInner(e, -1), r = i.reduce((o, l) => Math.max(o, l.length), 0);
  for (let o = 0; o < 5; o++) {
    let l = n.sliceDoc(s.from, Math.min(s.to, s.from + t.length + r)), a = l.indexOf(t);
    if (!a || a > -1 && i.indexOf(l.slice(0, a)) > -1) {
      let c = s.firstChild;
      for (; c && c.from == s.from && c.to - c.from > t.length + a; ) {
        if (n.sliceDoc(c.to - t.length, c.to) == t)
          return !1;
        c = c.firstChild;
      }
      return !0;
    }
    let h = s.to == e && s.parent;
    if (!h)
      break;
    s = h;
  }
  return !1;
}
function ba(n, e, t) {
  let i = n.charCategorizer(e);
  if (i(n.sliceDoc(e - 1, e)) != Y.Word)
    return e;
  for (let s of t) {
    let r = e - s.length;
    if (n.sliceDoc(r, e) == s && i(n.sliceDoc(r - 1, r)) != Y.Word)
      return r;
  }
  return -1;
}
function zy(n = {}) {
  return [
    by,
    Se,
    re.of(n),
    gy,
    qy,
    gf
  ];
}
const kf = [
  { key: "Ctrl-Space", run: er },
  { mac: "Alt-`", run: er },
  { mac: "Alt-i", run: er },
  { key: "Escape", run: uy },
  { key: "ArrowDown", run: /* @__PURE__ */ Ln(!0) },
  { key: "ArrowUp", run: /* @__PURE__ */ Ln(!1) },
  { key: "PageDown", run: /* @__PURE__ */ Ln(!0, "page") },
  { key: "PageUp", run: /* @__PURE__ */ Ln(!1, "page") },
  { key: "Enter", run: fy }
], qy = /* @__PURE__ */ Tt.highest(/* @__PURE__ */ tn.computeN([re], (n) => n.facet(re).defaultKeymap ? [kf] : []));
class xa {
  constructor(e, t, i) {
    this.from = e, this.to = t, this.diagnostic = i;
  }
}
class It {
  constructor(e, t, i) {
    this.diagnostics = e, this.panel = t, this.selected = i;
  }
  static init(e, t, i) {
    let s = i.facet(Ji).markerFilter;
    s && (e = s(e, i));
    let r = e.slice().sort((d, p) => d.from - p.from || d.to - p.to), o = new ft(), l = [], a = 0, h = i.doc.iter(), c = 0, f = i.doc.length;
    for (let d = 0; ; ) {
      let p = d == r.length ? null : r[d];
      if (!p && !l.length)
        break;
      let m, g;
      if (l.length)
        m = a, g = l.reduce((v, O) => Math.min(v, O.to), p && p.from > m ? p.from : 1e8);
      else {
        if (m = p.from, m > f)
          break;
        g = p.to, l.push(p), d++;
      }
      for (; d < r.length; ) {
        let v = r[d];
        if (v.from == m && (v.to > v.from || v.to == m))
          l.push(v), d++, g = Math.min(v.to, g);
        else {
          g = Math.min(v.from, g);
          break;
        }
      }
      g = Math.min(g, f);
      let y = !1;
      if (l.some((v) => v.from == m && (v.to == g || g == f)) && (y = m == g, !y && g - m < 10)) {
        let v = m - (c + h.value.length);
        v > 0 && (h.next(v), c = m);
        for (let O = m; ; ) {
          if (O >= g) {
            y = !0;
            break;
          }
          if (!h.lineBreak && c + h.value.length > O)
            break;
          O = c + h.value.length, c += h.value.length, h.next();
        }
      }
      let w = tb(l);
      if (y)
        o.add(m, m, B.widget({
          widget: new Xy(w),
          diagnostics: l.slice()
        }));
      else {
        let v = l.reduce((O, k) => k.markClass ? O + " " + k.markClass : O, "");
        o.add(m, g, B.mark({
          class: "cm-lintRange cm-lintRange-" + w + v,
          diagnostics: l.slice(),
          inclusiveEnd: l.some((O) => O.to > g)
        }));
      }
      if (a = g, a == f)
        break;
      for (let v = 0; v < l.length; v++)
        l[v].to <= a && l.splice(v--, 1);
    }
    let u = o.finish();
    return new It(u, t, Mt(u));
  }
}
function Mt(n, e = null, t = 0) {
  let i = null;
  return n.between(t, 1e9, (s, r, { spec: o }) => {
    if (!(e && o.diagnostics.indexOf(e) < 0))
      if (!i)
        i = new xa(s, r, e || o.diagnostics[0]);
      else {
        if (o.diagnostics.indexOf(i.diagnostic) < 0)
          return !1;
        i = new xa(i.from, r, i.diagnostic);
      }
  }), i;
}
function $y(n, e) {
  let t = e.pos, i = e.end || t, s = n.state.facet(Ji).hideOn(n, t, i);
  if (s != null)
    return s;
  let r = n.startState.doc.lineAt(e.pos);
  return !!(n.effects.some((o) => o.is(Sf)) || n.changes.touchesRange(r.from, Math.max(r.to, i)));
}
function Ky(n, e) {
  return n.field(Ee, !1) ? e : e.concat(R.appendConfig.of(ib));
}
const Sf = /* @__PURE__ */ R.define(), Fo = /* @__PURE__ */ R.define(), Cf = /* @__PURE__ */ R.define(), Ee = /* @__PURE__ */ me.define({
  create() {
    return new It(B.none, null, null);
  },
  update(n, e) {
    if (e.docChanged && n.diagnostics.size) {
      let t = n.diagnostics.map(e.changes), i = null, s = n.panel;
      if (n.selected) {
        let r = e.changes.mapPos(n.selected.from, 1);
        i = Mt(t, n.selected.diagnostic, r) || Mt(t, null, r);
      }
      !t.size && s && e.state.facet(Ji).autoPanel && (s = null), n = new It(t, s, i);
    }
    for (let t of e.effects)
      if (t.is(Sf)) {
        let i = e.state.facet(Ji).autoPanel ? t.value.length ? Xi.open : null : n.panel;
        n = It.init(t.value, i, e.state);
      } else t.is(Fo) ? n = new It(n.diagnostics, t.value ? Xi.open : null, n.selected) : t.is(Cf) && (n = new It(n.diagnostics, n.panel, t.value));
    return n;
  },
  provide: (n) => [
    qi.from(n, (e) => e.panel),
    M.decorations.from(n, (e) => e.diagnostics)
  ]
}), jy = /* @__PURE__ */ B.mark({ class: "cm-lintRange cm-lintRange-active" });
function Uy(n, e, t) {
  let { diagnostics: i } = n.state.field(Ee), s, r = -1, o = -1;
  i.between(e - (t < 0 ? 1 : 0), e + (t > 0 ? 1 : 0), (a, h, { spec: c }) => {
    if (e >= a && e <= h && (a == h || (e > a || t > 0) && (e < h || t < 0)))
      return s = c.diagnostics, r = a, o = h, !1;
  });
  let l = n.state.facet(Ji).tooltipFilter;
  return s && l && (s = l(s, n.state)), s ? {
    pos: r,
    end: o,
    above: !0,
    create() {
      return { dom: _y(n, s) };
    }
  } : null;
}
function _y(n, e) {
  return K("ul", { class: "cm-tooltip-lint" }, e.map((t) => Mf(n, t, !1)));
}
const Gy = (n) => {
  let e = n.state.field(Ee, !1);
  (!e || !e.panel) && n.dispatch({ effects: Ky(n.state, [Fo.of(!0)]) });
  let t = bo(n, Xi.open);
  return t && t.dom.querySelector(".cm-panel-lint ul").focus(), !0;
}, wa = (n) => {
  let e = n.state.field(Ee, !1);
  return !e || !e.panel ? !1 : (n.dispatch({ effects: Fo.of(!1) }), !0);
}, Yy = (n) => {
  let e = n.state.field(Ee, !1);
  if (!e)
    return !1;
  let t = n.state.selection.main, i = Mt(e.diagnostics, null, t.to + 1);
  return !i && (i = Mt(e.diagnostics, null, 0), !i || i.from == t.from && i.to == t.to) ? !1 : (n.dispatch({ selection: { anchor: i.from, head: i.to }, scrollIntoView: !0 }), Rp(n, i.from, 1, {
    tooltip: Tf,
    until: (s) => s.docChanged || s.newSelection.main.head < i.from || s.newSelection.main.head > i.to
  }), !0);
}, Jy = [
  { key: "Mod-Shift-m", run: Gy, preventDefault: !0 },
  { key: "F8", run: Yy }
], Ji = /* @__PURE__ */ T.define({
  combine(n) {
    return {
      sources: n.map((e) => e.source).filter((e) => e != null),
      ...at(n.map((e) => e.config), {
        delay: 750,
        markerFilter: null,
        tooltipFilter: null,
        needsRefresh: null,
        hideOn: () => null
      }, {
        delay: Math.max,
        markerFilter: va,
        tooltipFilter: va,
        needsRefresh: (e, t) => e ? t ? (i) => e(i) || t(i) : e : t,
        hideOn: (e, t) => e ? t ? (i, s, r) => e(i, s, r) || t(i, s, r) : e : t,
        autoPanel: (e, t) => e || t
      })
    };
  }
});
function va(n, e) {
  return n ? e ? (t, i) => e(n(t, i), i) : n : e;
}
function Af(n) {
  let e = [];
  if (n)
    e: for (let { name: t } of n) {
      for (let i = 0; i < t.length; i++) {
        let s = t[i];
        if (/[a-zA-Z]/.test(s) && !e.some((r) => r.toLowerCase() == s.toLowerCase())) {
          e.push(s);
          continue e;
        }
      }
      e.push("");
    }
  return e;
}
function Mf(n, e, t) {
  var i;
  let s = t ? Af(e.actions) : [];
  return K("li", { class: "cm-diagnostic cm-diagnostic-" + e.severity }, K("span", { class: "cm-diagnosticText" }, e.renderMessage ? e.renderMessage(n) : e.message), (i = e.actions) === null || i === void 0 ? void 0 : i.map((r, o) => {
    let l = !1, a = (d) => {
      if (d.preventDefault(), l)
        return;
      l = !0;
      let p = Mt(n.state.field(Ee).diagnostics, e);
      p && r.apply(n, p.from, p.to);
    }, { name: h } = r, c = s[o] ? h.indexOf(s[o]) : -1, f = c < 0 ? h : [
      h.slice(0, c),
      K("u", h.slice(c, c + 1)),
      h.slice(c + 1)
    ], u = r.markClass ? " " + r.markClass : "";
    return K("button", {
      type: "button",
      class: "cm-diagnosticAction" + u,
      onclick: a,
      onmousedown: a,
      "aria-label": ` Action: ${h}${c < 0 ? "" : ` (access key "${s[o]})"`}.`
    }, f);
  }), e.source && K("div", { class: "cm-diagnosticSource" }, e.source));
}
class Xy extends Ke {
  constructor(e) {
    super(), this.sev = e;
  }
  eq(e) {
    return e.sev == this.sev;
  }
  toDOM() {
    return K("span", { class: "cm-lintPoint cm-lintPoint-" + this.sev });
  }
}
class ka {
  constructor(e, t) {
    this.diagnostic = t, this.id = "item_" + Math.floor(Math.random() * 4294967295).toString(16), this.dom = Mf(e, t, !0), this.dom.id = this.id, this.dom.setAttribute("role", "option");
  }
}
class Xi {
  constructor(e) {
    this.view = e, this.items = [];
    let t = (s) => {
      if (!(s.ctrlKey || s.altKey || s.metaKey)) {
        if (s.keyCode == 27)
          wa(this.view), this.view.focus();
        else if (s.keyCode == 38 || s.keyCode == 33)
          this.moveSelection((this.selectedIndex - 1 + this.items.length) % this.items.length);
        else if (s.keyCode == 40 || s.keyCode == 34)
          this.moveSelection((this.selectedIndex + 1) % this.items.length);
        else if (s.keyCode == 36)
          this.moveSelection(0);
        else if (s.keyCode == 35)
          this.moveSelection(this.items.length - 1);
        else if (s.keyCode == 13)
          this.view.focus();
        else if (s.keyCode >= 65 && s.keyCode <= 90 && this.selectedIndex >= 0) {
          let { diagnostic: r } = this.items[this.selectedIndex], o = Af(r.actions);
          for (let l = 0; l < o.length; l++)
            if (o[l].toUpperCase().charCodeAt(0) == s.keyCode) {
              let a = Mt(this.view.state.field(Ee).diagnostics, r);
              a && r.actions[l].apply(e, a.from, a.to);
            }
        } else
          return;
        s.preventDefault();
      }
    }, i = (s) => {
      for (let r = 0; r < this.items.length; r++)
        this.items[r].dom.contains(s.target) && this.moveSelection(r);
    };
    this.list = K("ul", {
      tabIndex: 0,
      role: "listbox",
      "aria-label": this.view.state.phrase("Diagnostics"),
      onkeydown: t,
      onclick: i
    }), this.dom = K("div", { class: "cm-panel-lint" }, this.list, K("button", {
      type: "button",
      name: "close",
      "aria-label": this.view.state.phrase("close"),
      onclick: () => wa(this.view)
    }, "×")), this.update();
  }
  get selectedIndex() {
    let e = this.view.state.field(Ee).selected;
    if (!e)
      return -1;
    for (let t = 0; t < this.items.length; t++)
      if (this.items[t].diagnostic == e.diagnostic)
        return t;
    return -1;
  }
  update() {
    let { diagnostics: e, selected: t } = this.view.state.field(Ee), i = 0, s = !1, r = null, o = /* @__PURE__ */ new Set();
    for (e.between(0, this.view.state.doc.length, (l, a, { spec: h }) => {
      for (let c of h.diagnostics) {
        if (o.has(c))
          continue;
        o.add(c);
        let f = -1, u;
        for (let d = i; d < this.items.length; d++)
          if (this.items[d].diagnostic == c) {
            f = d;
            break;
          }
        f < 0 ? (u = new ka(this.view, c), this.items.splice(i, 0, u), s = !0) : (u = this.items[f], f > i && (this.items.splice(i, f - i), s = !0)), t && u.diagnostic == t.diagnostic ? u.dom.hasAttribute("aria-selected") || (u.dom.setAttribute("aria-selected", "true"), r = u) : u.dom.hasAttribute("aria-selected") && u.dom.removeAttribute("aria-selected"), i++;
      }
    }); i < this.items.length && !(this.items.length == 1 && this.items[0].diagnostic.from < 0); )
      s = !0, this.items.pop();
    this.items.length == 0 && (this.items.push(new ka(this.view, {
      from: -1,
      to: -1,
      severity: "info",
      message: this.view.state.phrase("No diagnostics")
    })), s = !0), r ? (this.list.setAttribute("aria-activedescendant", r.id), this.view.requestMeasure({
      key: this,
      read: () => ({ sel: r.dom.getBoundingClientRect(), panel: this.list.getBoundingClientRect() }),
      write: ({ sel: l, panel: a }) => {
        let h = a.height / this.list.offsetHeight;
        l.top < a.top ? this.list.scrollTop -= (a.top - l.top) / h : l.bottom > a.bottom && (this.list.scrollTop += (l.bottom - a.bottom) / h);
      }
    })) : this.selectedIndex < 0 && this.list.removeAttribute("aria-activedescendant"), s && this.sync();
  }
  sync() {
    let e = this.list.firstChild;
    function t() {
      let i = e;
      e = i.nextSibling, i.remove();
    }
    for (let i of this.items)
      if (i.dom.parentNode == this.list) {
        for (; e != i.dom; )
          t();
        e = i.dom.nextSibling;
      } else
        this.list.insertBefore(i.dom, e);
    for (; e; )
      t();
  }
  moveSelection(e) {
    if (this.selectedIndex < 0)
      return;
    let t = this.view.state.field(Ee), i = Mt(t.diagnostics, this.items[e].diagnostic);
    i && this.view.dispatch({
      selection: { anchor: i.from, head: i.to },
      scrollIntoView: !0,
      effects: Cf.of(i)
    });
  }
  static open(e) {
    return new Xi(e);
  }
}
function Qy(n, e = 'viewBox="0 0 40 40"') {
  return `url('data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" ${e}>${encodeURIComponent(n)}</svg>')`;
}
function En(n) {
  return Qy(`<path d="m0 2.5 l2 -1.5 l1 0 l2 1.5 l1 0" stroke="${n}" fill="none" stroke-width=".7"/>`, 'width="6" height="3"');
}
const Zy = /* @__PURE__ */ M.baseTheme({
  ".cm-diagnostic": {
    padding: "3px 6px 3px 8px",
    marginLeft: "-1px",
    display: "block",
    whiteSpace: "pre-wrap"
  },
  ".cm-diagnostic-error": { borderLeft: "5px solid #d11" },
  ".cm-diagnostic-warning": { borderLeft: "5px solid orange" },
  ".cm-diagnostic-info": { borderLeft: "5px solid #999" },
  ".cm-diagnostic-hint": { borderLeft: "5px solid #66d" },
  ".cm-diagnosticAction": {
    font: "inherit",
    border: "none",
    padding: "2px 4px",
    backgroundColor: "#444",
    color: "white",
    borderRadius: "3px",
    marginLeft: "8px",
    cursor: "pointer"
  },
  ".cm-diagnosticSource": {
    fontSize: "70%",
    opacity: 0.7
  },
  ".cm-lintRange": {
    backgroundPosition: "left bottom",
    backgroundRepeat: "repeat-x",
    paddingBottom: "0.7px"
  },
  ".cm-lintRange-error": { backgroundImage: /* @__PURE__ */ En("#f11") },
  ".cm-lintRange-warning": { backgroundImage: /* @__PURE__ */ En("orange") },
  ".cm-lintRange-info": { backgroundImage: /* @__PURE__ */ En("#999") },
  ".cm-lintRange-hint": { backgroundImage: /* @__PURE__ */ En("#66d") },
  ".cm-lintRange-active": { backgroundColor: "#ffdd9980" },
  ".cm-tooltip-lint": {
    padding: 0,
    margin: 0
  },
  ".cm-lintPoint": {
    position: "relative",
    "&:after": {
      content: '""',
      position: "absolute",
      bottom: 0,
      left: "-2px",
      borderLeft: "3px solid transparent",
      borderRight: "3px solid transparent",
      borderBottom: "4px solid #d11"
    }
  },
  ".cm-lintPoint-warning": {
    "&:after": { borderBottomColor: "orange" }
  },
  ".cm-lintPoint-info": {
    "&:after": { borderBottomColor: "#999" }
  },
  ".cm-lintPoint-hint": {
    "&:after": { borderBottomColor: "#66d" }
  },
  ".cm-panel.cm-panel-lint": {
    position: "relative",
    "& ul": {
      maxHeight: "100px",
      overflowY: "auto",
      "& [aria-selected]": {
        backgroundColor: "#ddd",
        "& u": { textDecoration: "underline" }
      },
      "&:focus [aria-selected]": {
        background_fallback: "#bdf",
        backgroundColor: "Highlight",
        color_fallback: "white",
        color: "HighlightText"
      },
      "& u": { textDecoration: "none" },
      padding: 0,
      margin: 0
    },
    "& [name=close]": {
      position: "absolute",
      top: "0",
      right: "2px",
      background: "inherit",
      border: "none",
      font: "inherit",
      padding: 0,
      margin: 0
    }
  },
  "&dark .cm-lintRange-active": { backgroundColor: "#86714a80" },
  "&dark .cm-panel.cm-panel-lint ul": {
    "& [aria-selected]": {
      backgroundColor: "#2e343e"
    }
  }
});
function eb(n) {
  return n == "error" ? 4 : n == "warning" ? 3 : n == "info" ? 2 : 1;
}
function tb(n) {
  let e = "hint", t = 1;
  for (let i of n) {
    let s = eb(i.severity);
    s > t && (t = s, e = i.severity);
  }
  return e;
}
const Tf = /* @__PURE__ */ Ep(Uy, { hideOn: $y }), ib = [
  Ee,
  /* @__PURE__ */ M.decorations.compute([Ee], (n) => {
    let { selected: e, panel: t } = n.field(Ee);
    return !e || !t || e.from == e.to ? B.none : B.set([
      jy.range(e.from, e.to)
    ]);
  }),
  Tf,
  Zy
];
var Sa = function(e) {
  e === void 0 && (e = {});
  var t = e, i = t.crosshairCursor, s = i === void 0 ? !1 : i, r = [];
  e.closeBracketsKeymap !== !1 && (r = r.concat(Py)), e.defaultKeymap !== !1 && (r = r.concat(g0)), e.searchKeymap !== !1 && (r = r.concat($0)), e.historyKeymap !== !1 && (r = r.concat(Sg)), e.foldKeymap !== !1 && (r = r.concat(Wm)), e.completionKeymap !== !1 && (r = r.concat(kf)), e.lintKeymap !== !1 && (r = r.concat(Jy));
  var o = [];
  return e.lineNumbers !== !1 && o.push(Up()), e.highlightActiveLineGutter !== !1 && o.push(Yp()), e.highlightSpecialChars !== !1 && o.push(lp()), e.history !== !1 && o.push(pg()), e.foldGutter !== !1 && o.push(zm()), e.drawSelection !== !1 && o.push(Jd()), e.dropCursor !== !1 && o.push(tp()), e.allowMultipleSelections !== !1 && o.push(F.allowMultipleSelections.of(!0)), e.indentOnInput !== !1 && o.push(Tm()), e.syntaxHighlighting !== !1 && o.push(wc(jm, {
    fallback: !0
  })), e.bracketMatching !== !1 && o.push(Qm()), e.closeBrackets !== !1 && o.push(By()), e.autocompletion !== !1 && o.push(zy()), e.rectangularSelection !== !1 && o.push(vp()), s !== !1 && o.push(Cp()), e.highlightActiveLine !== !1 && o.push(dp()), e.highlightSelectionMatches !== !1 && o.push(k0()), e.tabSize && typeof e.tabSize == "number" && o.push(nn.of(" ".repeat(e.tabSize))), o.concat([tn.of(r.flat())]).filter(Boolean);
};
const nb = "#e5c07b", Ca = "#e06c75", sb = "#56b6c2", rb = "#ffffff", Vn = "#abb2bf", Qr = "#7d8799", ob = "#61afef", lb = "#98c379", Aa = "#d19a66", ab = "#c678dd", hb = "#21252b", Ma = "#2c313a", Ta = "#282c34", ir = "#353a42", cb = "#3E4451", Da = "#528bff", fb = /* @__PURE__ */ M.theme({
  "&": {
    color: Vn,
    backgroundColor: Ta
  },
  ".cm-content": {
    caretColor: Da
  },
  ".cm-cursor, .cm-dropCursor": { borderLeftColor: Da },
  "&.cm-focused > .cm-scroller > .cm-selectionLayer .cm-selectionBackground, .cm-selectionBackground, .cm-content ::selection": { backgroundColor: cb },
  ".cm-panels": { backgroundColor: hb, color: Vn },
  ".cm-panels.cm-panels-top": { borderBottom: "2px solid black" },
  ".cm-panels.cm-panels-bottom": { borderTop: "2px solid black" },
  ".cm-searchMatch": {
    backgroundColor: "#72a1ff59",
    outline: "1px solid #457dff"
  },
  ".cm-searchMatch.cm-searchMatch-selected": {
    backgroundColor: "#6199ff2f"
  },
  ".cm-activeLine": { backgroundColor: "#6699ff0b" },
  ".cm-selectionMatch": { backgroundColor: "#aafe661a" },
  "&.cm-focused .cm-matchingBracket, &.cm-focused .cm-nonmatchingBracket": {
    backgroundColor: "#bad0f847"
  },
  ".cm-gutters": {
    backgroundColor: Ta,
    color: Qr,
    border: "none"
  },
  ".cm-activeLineGutter": {
    backgroundColor: Ma
  },
  ".cm-foldPlaceholder": {
    backgroundColor: "transparent",
    border: "none",
    color: "#ddd"
  },
  ".cm-tooltip": {
    border: "none",
    backgroundColor: ir
  },
  ".cm-tooltip .cm-tooltip-arrow:before": {
    borderTopColor: "transparent",
    borderBottomColor: "transparent"
  },
  ".cm-tooltip .cm-tooltip-arrow:after": {
    borderTopColor: ir,
    borderBottomColor: ir
  },
  ".cm-tooltip-autocomplete": {
    "& > ul > li[aria-selected]": {
      backgroundColor: Ma,
      color: Vn
    }
  }
}, { dark: !0 }), ub = /* @__PURE__ */ rn.define([
  {
    tag: x.keyword,
    color: ab
  },
  {
    tag: [x.name, x.deleted, x.character, x.propertyName, x.macroName],
    color: Ca
  },
  {
    tag: [/* @__PURE__ */ x.function(x.variableName), x.labelName],
    color: ob
  },
  {
    tag: [x.color, /* @__PURE__ */ x.constant(x.name), /* @__PURE__ */ x.standard(x.name)],
    color: Aa
  },
  {
    tag: [/* @__PURE__ */ x.definition(x.name), x.separator],
    color: Vn
  },
  {
    tag: [x.typeName, x.className, x.number, x.changed, x.annotation, x.modifier, x.self, x.namespace],
    color: nb
  },
  {
    tag: [x.operator, x.operatorKeyword, x.url, x.escape, x.regexp, x.link, /* @__PURE__ */ x.special(x.string)],
    color: sb
  },
  {
    tag: [x.meta, x.comment],
    color: Qr
  },
  {
    tag: x.strong,
    fontWeight: "bold"
  },
  {
    tag: x.emphasis,
    fontStyle: "italic"
  },
  {
    tag: x.strikethrough,
    textDecoration: "line-through"
  },
  {
    tag: x.link,
    color: Qr,
    textDecoration: "underline"
  },
  {
    tag: x.heading,
    fontWeight: "bold",
    color: Ca
  },
  {
    tag: [x.atom, x.bool, /* @__PURE__ */ x.special(x.variableName)],
    color: Aa
  },
  {
    tag: [x.processingInstruction, x.string, x.inserted],
    color: lb
  },
  {
    tag: x.invalid,
    color: rb
  }
]), db = [fb, /* @__PURE__ */ wc(ub)];
var pb = M.theme({
  "&": {
    backgroundColor: "#fff"
  }
}, {
  dark: !1
}), mb = function(e) {
  e === void 0 && (e = {});
  var t = e, i = t.indentWithTab, s = i === void 0 ? !0 : i, r = t.editable, o = r === void 0 ? !0 : r, l = t.readOnly, a = l === void 0 ? !1 : l, h = t.theme, c = h === void 0 ? "light" : h, f = t.placeholder, u = f === void 0 ? "" : f, d = t.basicSetup, p = d === void 0 ? !0 : d, m = [];
  switch (s && m.unshift(tn.of([y0])), p && (typeof p == "boolean" ? m.unshift(Sa()) : m.unshift(Sa(p))), u && m.unshift(yp(u)), c) {
    case "light":
      m.push(pb);
      break;
    case "dark":
      m.push(db);
      break;
    case "none":
      break;
    default:
      m.push(c);
      break;
  }
  return o === !1 && m.push(M.editable.of(!1)), a && m.push(F.readOnly.of(!0)), [...m];
}, gb = (n) => ({
  line: n.state.doc.lineAt(n.state.selection.main.from),
  lineCount: n.state.doc.lines,
  lineBreak: n.state.lineBreak,
  length: n.state.doc.length,
  readOnly: n.state.readOnly,
  tabSize: n.state.tabSize,
  selection: n.state.selection,
  selectionAsSingle: n.state.selection.asSingle().main,
  ranges: n.state.selection.ranges,
  selectionCode: n.state.sliceDoc(n.state.selection.main.from, n.state.selection.main.to),
  selections: n.state.selection.ranges.map((e) => n.state.sliceDoc(e.from, e.to)),
  selectedText: n.state.selection.ranges.some((e) => !e.empty)
});
class yb {
  constructor(e, t) {
    this.timeLeftMS = void 0, this.timeoutMS = void 0, this.isCancelled = !1, this.isTimeExhausted = !1, this.callbacks = [], this.timeLeftMS = t, this.timeoutMS = t, this.callbacks.push(e);
  }
  tick() {
    if (!this.isCancelled && !this.isTimeExhausted && (this.timeLeftMS--, this.timeLeftMS <= 0)) {
      this.isTimeExhausted = !0;
      var e = this.callbacks.slice();
      this.callbacks.length = 0, e.forEach((t) => {
        try {
          t();
        } catch (i) {
          console.error("TimeoutLatch callback error:", i);
        }
      });
    }
  }
  cancel() {
    this.isCancelled = !0, this.callbacks.length = 0;
  }
  reset() {
    this.timeLeftMS = this.timeoutMS, this.isCancelled = !1, this.isTimeExhausted = !1;
  }
  get isDone() {
    return this.isCancelled || this.isTimeExhausted;
  }
}
class Oa {
  constructor() {
    this.interval = null, this.latches = /* @__PURE__ */ new Set();
  }
  add(e) {
    this.latches.add(e), this.start();
  }
  remove(e) {
    this.latches.delete(e), this.latches.size === 0 && this.stop();
  }
  start() {
    this.interval === null && (this.interval = setInterval(() => {
      this.latches.forEach((e) => {
        e.tick(), e.isDone && this.remove(e);
      });
    }, 1));
  }
  stop() {
    this.interval !== null && (clearInterval(this.interval), this.interval = null);
  }
}
var nr = null, bb = () => typeof window > "u" ? new Oa() : (nr || (nr = new Oa()), nr), Ba = lt.define(), xb = 200, wb = [];
function vb(n) {
  var e = n.value, t = n.selection, i = n.onChange, s = n.onStatistics, r = n.onCreateEditor, o = n.onUpdate, l = n.extensions, a = l === void 0 ? wb : l, h = n.autoFocus, c = n.theme, f = c === void 0 ? "light" : c, u = n.height, d = u === void 0 ? null : u, p = n.minHeight, m = p === void 0 ? null : p, g = n.maxHeight, y = g === void 0 ? null : g, w = n.width, v = w === void 0 ? null : w, O = n.minWidth, k = O === void 0 ? null : O, S = n.maxWidth, C = S === void 0 ? null : S, E = n.placeholder, I = E === void 0 ? "" : E, q = n.editable, P = q === void 0 ? !0 : q, L = n.readOnly, H = L === void 0 ? !1 : L, N = n.indentWithTab, $ = N === void 0 ? !0 : N, Z = n.basicSetup, se = Z === void 0 ? !0 : Z, be = n.root, J = n.initialState, le = Ut(), ae = le[0], xe = le[1], Oe = Ut(), j = Oe[0], Fe = Oe[1], Ot = Ut(), jt = Ot[0], As = Ot[1], _e = Ut(() => ({
    current: null
  }))[0], hn = Ut(() => ({
    current: null
  }))[0], Of = M.theme({
    "&": {
      height: d,
      minHeight: m,
      maxHeight: y,
      width: v,
      minWidth: k,
      maxWidth: C
    },
    "& .cm-scroller": {
      height: "100% !important"
    }
  }), Bf = M.updateListener.of((Ge) => {
    if (Ge.docChanged && typeof i == "function" && // Fix echoing of the remote changes:
    // If transaction is market as remote we don't have to call `onChange` handler again
    !Ge.transactions.some((Ms) => Ms.annotation(Ba))) {
      _e.current ? _e.current.reset() : (_e.current = new yb(() => {
        if (hn.current) {
          var Ms = hn.current;
          hn.current = null, Ms();
        }
        _e.current = null;
      }, xb), bb().add(_e.current));
      var Bt = Ge.state.doc, Lt = Bt.toString();
      i(Lt, Ge);
    }
    s && s(gb(Ge));
  }), Lf = mb({
    theme: f,
    editable: P,
    readOnly: H,
    placeholder: I,
    indentWithTab: $,
    basicSetup: se
  }), bi = [Bf, Of, ...Lf];
  return o && typeof o == "function" && bi.push(M.updateListener.of(o)), bi = bi.concat(a), Ef(() => {
    if (ae && !jt) {
      var Ge = {
        doc: e,
        selection: t,
        extensions: bi
      }, Bt = J ? F.fromJSON(J.json, Ge, J.fields) : F.create(Ge);
      if (As(Bt), !j) {
        var Lt = new M({
          state: Bt,
          parent: ae,
          root: be
        });
        Fe(Lt), r && r(Lt, Bt);
      }
    }
    return () => {
      j && (As(void 0), Fe(void 0));
    };
  }, [ae, jt]), _t(() => {
    n.container && xe(n.container);
  }, [n.container]), _t(() => () => {
    j && (j.destroy(), Fe(void 0)), _e.current && (_e.current.cancel(), _e.current = null);
  }, [j]), _t(() => {
    h && j && j.focus();
  }, [h, j]), _t(() => {
    j && j.dispatch({
      effects: R.reconfigure.of(bi)
    });
  }, [f, a, d, m, y, v, k, C, I, P, H, $, se, i, o]), _t(() => {
    if (e !== void 0) {
      var Ge = j ? j.state.doc.toString() : "";
      if (j && e !== Ge) {
        var Bt = _e.current && !_e.current.isDone, Lt = () => {
          j && e !== j.state.doc.toString() && j.dispatch({
            changes: {
              from: 0,
              to: j.state.doc.toString().length,
              insert: e || ""
            },
            annotations: [Ba.of(!0)]
          });
        };
        Bt ? hn.current = Lt : Lt();
      }
    }
  }, [e, j]), {
    state: jt,
    setState: As,
    view: j,
    setView: Fe,
    container: ae,
    setContainer: xe
  };
}
var kb = ["className", "value", "selection", "extensions", "onChange", "onStatistics", "onCreateEditor", "onUpdate", "autoFocus", "theme", "height", "minHeight", "maxHeight", "width", "minWidth", "maxWidth", "basicSetup", "placeholder", "indentWithTab", "editable", "readOnly", "root", "initialState"], Df = /* @__PURE__ */ Rf((n, e) => {
  var t = n.className, i = n.value, s = i === void 0 ? "" : i, r = n.selection, o = n.extensions, l = o === void 0 ? [] : o, a = n.onChange, h = n.onStatistics, c = n.onCreateEditor, f = n.onUpdate, u = n.autoFocus, d = n.theme, p = d === void 0 ? "light" : d, m = n.height, g = n.minHeight, y = n.maxHeight, w = n.width, v = n.minWidth, O = n.maxWidth, k = n.basicSetup, S = n.placeholder, C = n.indentWithTab, E = n.editable, I = n.readOnly, q = n.root, P = n.initialState, L = Wf(n, kb), H = Pf(null), N = vb({
    root: q,
    value: s,
    autoFocus: u,
    theme: p,
    height: m,
    minHeight: g,
    maxHeight: y,
    width: w,
    minWidth: v,
    maxWidth: O,
    basicSetup: k,
    placeholder: S,
    indentWithTab: C,
    editable: E,
    readOnly: I,
    selection: r,
    onChange: a,
    onStatistics: h,
    onCreateEditor: c,
    onUpdate: f,
    extensions: l,
    initialState: P
  }), $ = N.state, Z = N.view, se = N.container, be = N.setContainer;
  If(e, () => ({
    editor: H.current,
    state: $,
    view: Z
  }), [H, se, $, Z]);
  var J = Nf((ae) => {
    H.current = ae, be(ae);
  }, [be]);
  if (typeof s != "string")
    throw new Error("value must be typeof string but got " + typeof s);
  var le = typeof p == "string" ? "cm-theme-" + p : "cm-theme";
  return /* @__PURE__ */ La.jsx("div", sr({
    ref: J,
    className: "" + le + (t ? " " + t : "")
  }, L));
});
Df.displayName = "CodeMirror";
async function Sb(n) {
  const e = n.trim().toLowerCase();
  return e === "javascript" || e === "typescript" ? (await import("./javascriptCodeMirror-AeICnAPs.js")).createJavaScriptCodeMirrorExtensions() : [];
}
function Cb({
  document: n,
  readOnly: e = !1,
  theme: t = "studio",
  minHeight: i = "220px",
  ariaLabel: s,
  onChange: r
}) {
  const [o, l] = Ut([]);
  return _t(() => {
    let a = !1;
    return Sb(n.language).then((h) => {
      a || l(h);
    }), () => {
      a = !0;
    };
  }, [n.language]), /* @__PURE__ */ La.jsx(
    Df,
    {
      "aria-label": s,
      className: "studio-code-editor-rich",
      value: n.value,
      minHeight: i,
      basicSetup: {
        lineNumbers: !0,
        foldGutter: !0,
        highlightActiveLine: !0,
        bracketMatching: !0,
        autocompletion: !0
      },
      editable: !e,
      readOnly: e,
      theme: t === "dark" ? "dark" : "light",
      extensions: o,
      onChange: (a) => {
        e || r({ ...n, value: a });
      }
    }
  );
}
const Wb = /* @__PURE__ */ Object.freeze(/* @__PURE__ */ Object.defineProperty({
  __proto__: null,
  CodeMirrorStudioCodeEditor: Cb
}, Symbol.toStringTag, { value: "Module" }));
export {
  Wb as C,
  Jp as D,
  M as E,
  te as I,
  Bb as L,
  nc as N,
  nm as P,
  ue as T,
  Re as a,
  V as b,
  G0 as c,
  mm as d,
  $r as e,
  xm as f,
  Eb as g,
  Rb as h,
  Ib as i,
  Lb as j,
  Om as k,
  Pb as l,
  Nb as m,
  pe as n,
  b as o,
  pm as p,
  Db as q,
  hm as s,
  x as t
};
