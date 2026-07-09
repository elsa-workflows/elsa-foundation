import Ke, { useMemo as de, useState as K, useEffect as Q, memo as Ee, forwardRef as Yc, useRef as oe, useCallback as se, useContext as Yn, createContext as Gr, useLayoutEffect as df, lazy as ff, Suspense as pf, useReducer as hf, useId as qc } from "react";
import { Boxes as qn, Zap as gf, Play as Jt, Terminal as yf, ListTree as Jr, GitBranch as Uc, ListChecks as mf, Save as Zc, EyeOff as rr, Shield as Bs, AlertTriangle as Hi, SlidersHorizontal as Qr, Activity as Gc, Search as io, X as es, Check as ln, Plus as Qt, Trash2 as Dn, AlertCircle as vt, Wrench as xf, Copy as wf, Sparkles as ut, RotateCcw as ts, ChevronDown as Jc, ChevronRight as mt, GripVertical as Qc, Maximize2 as Tn, ChevronUp as vf, Repeat2 as bf, Package as el, Undo2 as Nf, Redo2 as jf, Network as Sf, Download as Cf, ChevronLeft as $n, Minimize2 as Cr, Workflow as tl, Code2 as Ef } from "lucide-react";
import { useQuery as ns, useQueryClient as kf, useMutation as If } from "@tanstack/react-query";
import { useTablistKeyboard as Af } from "@elsa-workflows/studio-ui";
function _f(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var sr = { exports: {} }, mn = {};
var Fs;
function Df() {
  if (Fs) return mn;
  Fs = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function n(i, o, s) {
    var a = null;
    if (s !== void 0 && (a = "" + s), o.key !== void 0 && (a = "" + o.key), "key" in o) {
      s = {};
      for (var c in o)
        c !== "key" && (s[c] = o[c]);
    } else s = o;
    return o = s.ref, {
      $$typeof: e,
      type: i,
      key: a,
      ref: o !== void 0 ? o : null,
      props: s
    };
  }
  return mn.Fragment = t, mn.jsx = n, mn.jsxs = n, mn;
}
var Ks;
function Tf() {
  return Ks || (Ks = 1, sr.exports = Df()), sr.exports;
}
var r = Tf();
let nl;
function $f(e) {
  nl = e;
}
function Xs() {
  return nl;
}
const Pf = "String", Mf = "singleline";
function Rf(e) {
  return e === "Single" || e === "Array" || e === "List" || e === "HashSet";
}
function is(e, t = "Single") {
  return { alias: (e ?? "").trim() || Pf, collectionKind: t };
}
function os(e) {
  const t = e.type ?? e.Type;
  if (Pn(t))
    return { alias: typeof t.alias == "string" ? t.alias : typeof t.typeName == "string" ? t.typeName : "", collectionKind: Rf(t.collectionKind) ? t.collectionKind : "Single" };
  const n = e.typeInformation ?? e.TypeInformation;
  return Pn(n) ? { alias: typeof n.typeName == "string" ? n.typeName : "", collectionKind: Ys(e) ? "Array" : "Single" } : { alias: typeof t == "string" ? t : "", collectionKind: Ys(e) ? "Array" : "Single" };
}
function Ys(e) {
  const t = e.isArray ?? e.IsArray;
  return t === !0 || t === "true";
}
function qs(e) {
  return (e.alias ?? e.typeName ?? "").trim();
}
function oo() {
  return typeof crypto < "u" && typeof crypto.randomUUID == "function" ? crypto.randomUUID() : `id-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
}
function zf(e, t) {
  const n = new Set(t);
  let i = 1, o = `${e}${i}`;
  for (; n.has(o); )
    i += 1, o = `${e}${i}`;
  return o;
}
function Lf(e) {
  return {
    referenceKey: oo(),
    name: e.name,
    type: is(e.alias),
    storageDriverType: e.storageDriver?.trim() || null,
    default: null
  };
}
function Vf(e, t) {
  return { ...e, ...t };
}
function Of(e) {
  return e === "" ? null : { value: e, expressionType: "Literal" };
}
function Hf(e) {
  return e == null || e.value == null ? "" : typeof e.value == "object" ? JSON.stringify(e.value) : String(e.value);
}
function Wf(e) {
  return {
    referenceKey: oo(),
    name: e.name,
    type: is(e.alias),
    displayName: e.name,
    description: "",
    category: "",
    uiHint: Mf,
    storageDriverType: e.storageDriver ?? null,
    isRequired: !1
  };
}
function Bf(e, t) {
  return { ...e, ...t };
}
function Ff(e) {
  return {
    referenceKey: oo(),
    name: e.name,
    type: is(e.alias),
    displayName: e.name,
    description: "",
    category: ""
  };
}
function Kf(e, t) {
  return { ...e, ...t };
}
function Xf(e) {
  const t = e.split(",")[0].trim();
  return (t.split(".").pop() ?? t).split("`")[0];
}
function il(e, t) {
  return e && !e.includes(",") && !e.includes(".") ? e : Xf(e || t);
}
function Yf(e, t) {
  return il(e, t).replace(/StorageDriver$/, "");
}
function Pn(e) {
  return !!e && typeof e == "object" && !Array.isArray(e);
}
function ct(e, t) {
  for (const n of t) {
    const i = e[n];
    if (i != null && typeof i != "object") return String(i);
  }
  return "";
}
const qf = ["name", "Name"], rs = ["name", "Name"], Uf = ["storageDriverType", "StorageDriverType"], ol = ["referenceKey", "ReferenceKey"], Zf = /* @__PURE__ */ new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);
function Un(e) {
  return rl(e, ep);
}
function ro(e) {
  return rl(e, tp);
}
function rl(e, t) {
  if (!e) return e;
  const n = { ...e };
  return e.rootActivity && (n.rootActivity = Er(e.rootActivity, t)), Array.isArray(e.variables) && (n.variables = _i(e.variables, kr)), Array.isArray(e.inputs) && (n.inputs = _i(e.inputs, kr)), Array.isArray(e.outputs) && (n.outputs = _i(e.outputs, (i) => sl(i, !1))), n;
}
function Er(e, t) {
  const n = t(e), i = n.structure;
  if (!i || !it(i.payload)) return n;
  let o = !1;
  const s = { ...i.payload };
  for (const [a, c] of Object.entries(i.payload))
    Zs(c) ? (s[a] = Er(c, t), o = !0) : Array.isArray(c) && c.length > 0 && c.every(Zs) && (s[a] = c.map((u) => Er(u, t)), o = !0);
  return Array.isArray(i.payload.variables) && i.payload.variables.length > 0 && (s.variables = _i(i.payload.variables, kr), o = !0), o ? { ...n, structure: { ...i, payload: s } } : n;
}
function _i(e, t) {
  return e.map((n) => it(n) && !Array.isArray(n) ? t(n) : n);
}
function kr(e) {
  return sl(e, !0);
}
const Gf = [
  "type",
  "Type",
  "typeInformation",
  "TypeInformation",
  "isArray",
  "IsArray",
  "storageDriverType",
  "StorageDriverType",
  "defaultValue",
  "DefaultValue",
  "defaultSyntax",
  "DefaultSyntax",
  "isReadOnly",
  "IsReadOnly"
];
function sl(e, t) {
  const n = Jf(e, Gf);
  return ct(e, ol).trim() || (n.referenceKey = oo()), n.type = os(e), t && (n.storageDriverType = Qf(e.storageDriverType ?? e.StorageDriverType)), n;
}
function Jf(e, t) {
  const n = new Set(t), i = {};
  for (const [o, s] of Object.entries(e))
    n.has(o) || (i[o] = s);
  return i;
}
function Qf(e) {
  if (typeof e == "string") return e.trim() ? e : null;
  if (it(e)) {
    const t = typeof e.typeName == "string" ? e.typeName : "";
    if (!t) return null;
    const n = typeof e.namespace == "string" ? e.namespace : "";
    return n ? `${n}.${t}` : t;
  }
  return null;
}
function ep(e) {
  const t = [], n = {};
  for (const [o, s] of Object.entries(e))
    Zf.has(o) || (rp(s) ? t.push({
      referenceKey: np(o),
      value: op(s.expression)
    }) : n[o] = s);
  const i = Array.isArray(e.inputs) ? e.inputs : [];
  return {
    ...n,
    nodeId: e.nodeId,
    activityVersionId: e.activityVersionId,
    inputs: [...i, ...t],
    outputs: Array.isArray(e.outputs) ? e.outputs : [],
    structure: e.structure
  };
}
function tp(e) {
  const t = Array.isArray(e.inputs) ? e.inputs : [], n = {};
  for (const i of t) {
    if (!it(i) || typeof i.referenceKey != "string") continue;
    const o = it(i.value) ? i.value : {};
    n[ip(i.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof o.expressionType == "string" ? o.expressionType : "Literal",
        value: o.value ?? ""
      }
    };
  }
  return { ...e, ...n, inputs: [] };
}
function np(e) {
  return e && e.charAt(0).toUpperCase() + e.slice(1);
}
function ip(e) {
  return e && e.charAt(0).toLowerCase() + e.slice(1);
}
function op(e) {
  const t = e.type || "Literal";
  return t === "Variable" && it(e.value) ? { value: e.value, expressionType: t } : t === "Literal" && it(e.value) ? { value: Us(e.value), expressionType: "Object" } : { value: Us(e.value), expressionType: t };
}
function Us(e) {
  return e == null ? null : typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function rp(e) {
  if (!it(e) || Array.isArray(e)) return !1;
  const t = e.expression;
  return it(t) && typeof t.type == "string";
}
function Zs(e) {
  return it(e) && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function it(e) {
  return typeof e == "object" && e !== null;
}
const Zn = "elsa.sequence.structure", un = "elsa.flowchart.structure";
function al(e, t, n) {
  if (!e) return null;
  let i = e;
  for (const o of t) {
    const s = ap(i, o.ownerNodeId, n);
    if (!s) return null;
    i = s;
  }
  return i;
}
function sp(e, t, n = (o) => o.nodeId, i) {
  if (!e) return null;
  if (e.nodeId === t) return [];
  const o = (s, a) => {
    const c = ze(s, i);
    for (const u of c) {
      if (!u.activities.some((d) => d.nodeId === t)) continue;
      const l = a.at(-1);
      return l ? [...a.slice(0, -1), { ...l, slotId: u.id }] : u.id === c[0]?.id ? a : null;
    }
    for (const u of c)
      for (const l of u.activities) {
        const f = ze(l, i)[0]?.id ?? u.id, p = o(l, [...a, { ownerNodeId: l.nodeId, slotId: f, label: n(l) }]);
        if (p) return p;
      }
    return null;
  };
  return o(e, []);
}
function cl(e, t, n) {
  const i = ze(e, n), o = t.at(-1);
  return o ? i.find((s) => s.id === o.slotId) ?? null : i[0] ?? null;
}
function Mn(e, t, n) {
  const i = al(e, t, n);
  if (!i) return null;
  const o = cl(i, t, n);
  return o ? { owner: i, slot: o } : null;
}
function ll(e, t, n) {
  for (const i of ze(e, n)) {
    const o = i.activities.find((s) => s.nodeId === t);
    if (o) return { slot: i, child: o };
  }
  return null;
}
function ap(e, t, n) {
  return ll(e, t, n)?.child ?? null;
}
function ze(e, t) {
  const n = e.structure;
  if (!n || !n.payload || typeof n.payload != "object") return [];
  const i = n.payload, o = ss(ul(e, t));
  if (o?.kind === n.kind) return up(n, o);
  const s = Dp(n), a = bn(i.activities);
  return a ? [{
    id: `${n.kind}:activities`,
    label: Tp(n),
    property: "activities",
    cardinality: "many",
    mode: s,
    activities: a
  }] : Object.entries(i).filter(([, c]) => bn(c) || Fi(c)).map(([c, u]) => ({
    id: `${n.kind}:${c}`,
    label: Pp(c),
    property: c,
    cardinality: bn(u) ? "many" : "single",
    mode: "generic",
    activities: bn(u) ?? (Fi(u) ? [u] : [])
  }));
}
function ss(e) {
  for (const t of e?.designFacets ?? []) {
    if (!ot(t)) continue;
    const n = typeof t.kind == "string" ? t.kind : "", i = typeof t.schemaVersion == "string" ? t.schemaVersion : "";
    if (!n || !i || !ot(t.payload)) continue;
    const o = cp(t.payload);
    if (o) return { kind: n, schemaVersion: i, payload: o };
  }
  return null;
}
function cp(e) {
  const t = e.mode;
  if (t !== "sequence" && t !== "flowchart" && t !== "generic" || typeof e.supportsScopedVariables != "boolean" || !Array.isArray(e.slots) || !ot(e.initialPayload)) return null;
  const n = e.slots.map(lp).filter((i) => i !== null);
  return {
    mode: t,
    supportsScopedVariables: e.supportsScopedVariables,
    slots: n,
    initialPayload: e.initialPayload
  };
}
function lp(e) {
  if (!ot(e)) return null;
  const t = typeof e.name == "string" ? e.name : "", n = typeof e.property == "string" ? e.property : "", i = typeof e.displayName == "string" ? e.displayName : "", o = e.cardinality;
  return !t || !n || !i || o !== "single" && o !== "many" ? null : {
    name: t,
    property: n,
    displayName: i,
    cardinality: o,
    collectionProperty: Ft(e.collectionProperty),
    childProperty: Ft(e.childProperty),
    labelProperty: Ft(e.labelProperty),
    slotNameTemplate: Ft(e.slotNameTemplate)
  };
}
function up(e, t) {
  return t.payload.slots.flatMap((n) => {
    if (n.collectionProperty && n.childProperty) {
      const i = n.childProperty, o = e.payload[n.collectionProperty];
      return Array.isArray(o) ? o.flatMap((s, a) => {
        if (!ot(s)) return [];
        const c = n.labelProperty ? Ft(s[n.labelProperty]) : void 0;
        return [Gs(e.kind, n, s[i], t.payload.mode, a, c)];
      }) : [];
    }
    return [Gs(e.kind, n, e.payload[n.property], t.payload.mode)];
  });
}
function Gs(e, t, n, i, o, s) {
  const a = o === void 0 ? "" : `:${t.collectionProperty}:${t.childProperty}:${o}`;
  return {
    id: `${e}:${t.property}${a}`,
    label: o === void 0 ? t.displayName : fp(t, o, s),
    property: t.property,
    cardinality: t.cardinality,
    mode: i,
    activities: dp(n, t.cardinality),
    collectionProperty: t.collectionProperty,
    childProperty: t.childProperty,
    labelProperty: t.labelProperty,
    slotNameTemplate: t.slotNameTemplate,
    collectionIndex: o,
    collectionItemLabel: s
  };
}
function dp(e, t) {
  return t === "many" ? bn(e) ?? [] : Fi(e) ? [e] : [];
}
function fp(e, t, n) {
  return e.slotNameTemplate ? e.slotNameTemplate.replaceAll("{name}", e.name).replaceAll("{displayName}", e.displayName).replaceAll("{label}", n ?? String(t + 1)).replaceAll("{index}", String(t + 1)) : n ? `${e.displayName}: ${n}` : `${e.displayName} ${t + 1}`;
}
function ul(e, t) {
  if (t)
    return t instanceof Map ? t.get(e.activityVersionId) : Array.isArray(t) ? t.find((n) => n.activityVersionId === e.activityVersionId) : t.activityVersionId === e.activityVersionId ? t : void 0;
}
function pp(e, t) {
  if (t.labelProperty && t.collectionItemLabel) {
    const n = e.map((i, o) => ot(i) && Ft(i[t.labelProperty]) === t.collectionItemLabel ? o : -1).filter((i) => i >= 0);
    if (n.length === 1) return n[0];
  }
  return typeof t.collectionIndex == "number" && t.collectionIndex >= 0 && t.collectionIndex < e.length ? t.collectionIndex : -1;
}
function dl(e, t, n) {
  const i = new Map(t.map((a) => [a.activityVersionId, a])), o = new Map(n.map((a) => [a.nodeId, a])), s = e.slot.activities.map((a, c) => {
    const u = i.get(a.activityVersionId), l = o.get(a.nodeId) ?? $p(e.slot.mode, c);
    return gl(a, u, { x: l.x, y: l.y });
  });
  return {
    nodes: s,
    edges: e.slot.mode === "flowchart" ? ml(e.owner) : Sp(e.slot, s)
  };
}
function fl(e, t, n) {
  const i = t.find((s) => s.activityVersionId === e.activityVersionId), o = n.find((s) => s.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [gl(e, i, { x: o.x, y: o.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function hp(e, t, n, i = null) {
  const o = new Map(t.map((c) => [c.activityExecutionId, c])), s = ea(t, (c) => c.authoredActivityId || c.executableNodeId), a = ea(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? o.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const u = s.get(c.id) ?? [], l = a.get(c.id) ?? [];
    if (u.length === 0 && l.length === 0) return c;
    const d = wl(u), f = i === c.id || u.some((h) => h.activityExecutionId === i) || l.some((h) => h.incidentId === i), p = {
      status: d?.status,
      subStatus: d?.subStatus,
      activityExecutionId: d?.activityExecutionId,
      faultCount: u.reduce((h, y) => h + y.faultCount + y.aggregateFaultCount, 0),
      incidentCount: l.length,
      hasBlockingIncident: l.some((h) => h.isBlocking),
      selected: f
    };
    return {
      ...c,
      selected: f,
      className: f ? "wf-runtime-node-selected" : c.className,
      data: {
        ...c.data,
        runtime: p
      }
    };
  });
}
function pl(e, t) {
  if (e?.structure?.kind === un || vp(t)) return "flowchart";
  if (e?.structure?.kind === Zn || bp(t)) return "sequence";
  if (e) {
    const n = ze(e, t)[0];
    if (n) return n.mode === "flowchart" ? "flowchart" : "sequence";
  }
  return "unsupported";
}
function Js(e, t, n, i) {
  return as(e, t, i, (o) => {
    const s = cl(o, t, i);
    return s ? dn(o, s, n) : o;
  });
}
function gp(e, t, n, i) {
  return t.length === 0 ? n : as(e, t, i, () => n);
}
function as(e, t, n, i) {
  if (t.length === 0) return i(e);
  const [o, ...s] = t, a = ll(e, o.ownerNodeId, n);
  if (!a) return e;
  const c = as(a.child, s, n, i);
  if (c === a.child) return e;
  const u = a.slot.activities.map((l) => l.nodeId === o.ownerNodeId ? c : l);
  return dn(e, a.slot, u);
}
function Ir(e, t, n, i) {
  if (e.nodeId === t) return n(e);
  const o = ze(e, i);
  if (o.length === 0) return e;
  let s = !1, a = e;
  for (const c of o) {
    const u = c.activities.map((l) => {
      const d = Ir(l, t, n, i);
      return d !== l && (s = !0), d;
    });
    s && (a = dn(a, c, u));
  }
  return s ? a : e;
}
function dn(e, t, n) {
  if (!e.structure) return e;
  const i = t.cardinality === "single" ? n[0] ?? null : n;
  if (t.collectionProperty && t.childProperty) {
    const o = e.structure.payload[t.collectionProperty];
    if (!Array.isArray(o)) return e;
    const s = pp(o, t);
    if (s < 0) return e;
    const a = o[s];
    if (!ot(a)) return e;
    const c = [...o];
    return c[s] = {
      ...a,
      [t.childProperty]: i
    }, {
      ...e,
      structure: {
        ...e.structure,
        payload: {
          ...e.structure.payload,
          [t.collectionProperty]: c
        }
      }
    };
  }
  return {
    ...e,
    structure: {
      ...e.structure,
      payload: {
        ...e.structure.payload,
        [t.property]: i
      }
    }
  };
}
function yp(e, t, n, i = []) {
  const o = new Map(e.slot.activities.map((a) => [a.nodeId, a]));
  for (const a of i)
    o.set(a.nodeId, a);
  const s = t.map((a) => o.get(a.id)).filter((a) => !!a);
  return e.slot.mode === "sequence" && s.sort((a, c) => {
    const u = t.find((d) => d.id === a.nodeId), l = t.find((d) => d.id === c.nodeId);
    return (u?.position.x ?? 0) - (l?.position.x ?? 0);
  }), dn(e.owner, e.slot, s);
}
function mp(e, t) {
  return {
    ...e,
    structure: jp(e.structure, t)
  };
}
function xp(e, t) {
  const n = new Map(e.map((i) => [i.nodeId, i]));
  for (const i of t)
    n.set(i.id, {
      ...n.get(i.id) ?? { nodeId: i.id },
      nodeId: i.id,
      x: Math.round(i.position.x),
      y: Math.round(i.position.y)
    });
  return [...n.values()];
}
function Wi(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: yl(e)
  };
}
function hl(e, t) {
  if (!e) return null;
  const n = ul(e, t), i = e.structure ?? (n ? yl(n) : null);
  let o = i === e.structure ? e : { ...e, structure: i };
  const s = ze(o, t);
  for (const a of s) {
    const c = a.activities.map((u) => hl(u, t) ?? u);
    c.some((u, l) => u !== a.activities[l]) && (o = dn(o, a, c));
  }
  return o;
}
function Ce(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? Np(t) : n;
}
function gl(e, t, n, i = {}) {
  return {
    id: e.nodeId,
    type: "workflowActivity",
    position: n,
    connectable: i.connectable,
    deletable: i.deletable,
    draggable: i.draggable,
    data: {
      label: t ? Ce(t) : e.activityVersionId,
      activityVersionId: e.activityVersionId,
      activityTypeKey: t?.activityTypeKey,
      category: t?.category,
      executionType: t?.executionType,
      icon: fn(t),
      childSlots: ze(e, t),
      acceptsInbound: Cp(e, t),
      sourcePorts: i.suppressFlowPorts ? [] : xl(e, t),
      suppressFlowPorts: i.suppressFlowPorts
    }
  };
}
function fn(e) {
  if (!e) return "activity";
  const t = wp(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), i = Ce(e).toLowerCase(), o = e.category?.toLowerCase() ?? "", s = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || i === "flowchart" ? "flowchart" : n.endsWith(".sequence") || i === "sequence" ? "sequence" : n.includes("writeline") || i.includes("write line") ? "terminal" : o.includes("runtime") ? "runtime" : s === "trigger" ? "trigger" : "activity";
}
function wp(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function vp(e) {
  return !!e && (Ce(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function bp(e) {
  return !!e && (Ce(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function Np(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function yl(e) {
  const t = ss(e);
  return t ? {
    kind: t.kind,
    schemaVersion: t.schemaVersion,
    payload: Ap(t.payload.initialPayload)
  } : e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: Zn,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: un,
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
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], i = /* @__PURE__ */ new Map();
  for (const o of n) {
    if (!ot(o)) continue;
    const s = o.id;
    typeof s == "string" && i.set(s, o);
  }
  return {
    ...e,
    payload: {
      ...e.payload,
      connections: t.map((o) => {
        const s = i.get(o.id) ?? {}, a = o.data?.vertices, { vertices: c, ...u } = s;
        return {
          ...u,
          id: o.id,
          source: { nodeId: o.source, port: o.sourceHandle ?? "Done" },
          target: o.targetHandle ? { nodeId: o.target, port: o.targetHandle } : { nodeId: o.target },
          ...a?.length ? { vertices: a.map((l) => ({ x: Math.round(l.x), y: Math.round(l.y) })) } : {}
        };
      })
    }
  };
}
function Sp(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, i) => ({
    id: `sequence-${n.id}-${t[i + 1].id}`,
    source: n.id,
    target: t[i + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function ml(e) {
  if (e.structure?.kind !== un) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, i) => {
    if (!n || typeof n != "object") return null;
    const o = n.source, s = n.target;
    if (!o?.nodeId || !s?.nodeId) return null;
    const a = Array.isArray(n.vertices) ? n.vertices.filter(_p) : [];
    return {
      id: typeof n.id == "string" ? String(n.id) : `flow-${i}-${o.nodeId}-${s.nodeId}`,
      source: o.nodeId,
      target: s.nodeId,
      sourceHandle: o.port,
      targetHandle: s.port && s.port !== "Done" ? s.port : void 0,
      type: "workflow",
      label: o.port && o.port !== "Done" ? o.port : void 0,
      data: a.length ? { vertices: a } : void 0
    };
  }).filter((n) => n !== null) : [];
}
function xl(e, t) {
  const n = Qs(e.cases);
  if (kp(e, t) && n.length > 0)
    return [...n.map((s) => ({ name: s, displayName: s })), { name: "Default", displayName: "Default" }];
  const i = [
    ...Di(t?.designFacets),
    ...Di(t?.ports),
    ...Di(t?.outputs)
  ];
  if (i.length > 0) return Ip(i);
  const o = Qs(e.outcomes);
  return o.length > 0 ? o.map((s) => ({ name: s, displayName: s })) : [{ name: "Done", displayName: "Done" }];
}
function Cp(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function Bi(e, t, n, i) {
  const o = n ?? "Done";
  return {
    id: `flow-${e}-${t}-${o}-${crypto.randomUUID().slice(0, 8)}`,
    source: e,
    target: t,
    sourceHandle: o,
    targetHandle: i ?? void 0,
    type: "workflow",
    label: o !== "Done" ? o : void 0
  };
}
function Ep(e, t, n) {
  const i = Bi(t.source, n, t.sourceHandle ?? "Done", void 0), o = Bi(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((s) => s.id !== t.id).concat(i, o);
}
function bn(e) {
  return Array.isArray(e) ? e.filter(Fi) : null;
}
function kp(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, i = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || i === "FlowSwitch";
}
function Di(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!ot(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...Di(n.ports));
      continue;
    }
    const i = typeof n.type == "string" ? n.type : typeof n.portType == "string" ? n.portType : "", o = n.isBrowsable !== !1 && n.browsable !== !1, s = typeof n.name == "string" ? n.name : typeof n.id == "string" ? n.id : "";
    if (o && i.toLowerCase() === "flow" && s) {
      const a = typeof n.displayName == "string" ? n.displayName : s;
      t.push({ name: s, displayName: a });
    }
  }
  return t;
}
function Ip(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function Qs(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function Ft(e) {
  return typeof e == "string" && e.length > 0 ? e : void 0;
}
function Ap(e) {
  return JSON.parse(JSON.stringify(e));
}
function ea(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e) {
    const o = t(i);
    o && n.set(o, [...n.get(o) ?? [], i]);
  }
  return n;
}
function wl(e) {
  return [...e].sort((t, n) => ta(n).localeCompare(ta(t)))[0];
}
function ta(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function _p(e) {
  return ot(e) && typeof e.x == "number" && typeof e.y == "number";
}
function ot(e) {
  return typeof e == "object" && e !== null;
}
function Fi(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Dp(e) {
  return e.kind === Zn ? "sequence" : e.kind === un ? "flowchart" : "generic";
}
function Tp(e) {
  return e.kind === Zn || e.kind === un, "Activities";
}
function $p(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function Pp(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Ki = "workflow", Mp = /* @__PURE__ */ new Set([Zn, un]);
function Rp(e, t) {
  const n = e?.structure?.kind;
  if (!n) return !1;
  if (Mp.has(n)) return !0;
  const i = t?.activityVersionId === e?.activityVersionId ? ss(t) : null;
  return i?.kind === n && i.payload.supportsScopedVariables;
}
function vl(e) {
  const t = e?.structure?.payload?.variables;
  return Array.isArray(t) ? t.filter(Pn) : [];
}
function zp(e, t) {
  return e.structure ? {
    ...e,
    structure: {
      ...e.structure,
      payload: { ...e.structure.payload, variables: t }
    }
  } : e;
}
function Lp(e, t) {
  return {
    referenceKey: e,
    declaringScopeId: t && t !== Ki ? t : Ki
  };
}
function bl(e) {
  if (e && typeof e == "object") {
    const t = e;
    return typeof t.referenceKey == "string" && t.referenceKey ? { referenceKey: t.referenceKey, declaringScopeId: typeof t.declaringScopeId == "string" ? t.declaringScopeId : null } : null;
  }
  if (typeof e == "string") {
    const t = e.trim();
    if (t.startsWith("{"))
      try {
        return bl(JSON.parse(t));
      } catch {
      }
    return t ? { referenceKey: t, declaringScopeId: null } : null;
  }
  return null;
}
function Vp(e, t) {
  if (!e) return "";
  const n = [`workflow:${na(e.variables)}`], i = (o) => {
    const s = ze(o, t), a = s.flatMap((c) => c.activities.map((u) => u.nodeId));
    n.push(`${o.nodeId}:${na(vl(o))}>${a.join(",")}`), s.forEach((c) => c.activities.forEach(i));
  };
  return e.rootActivity && i(e.rootActivity), n.join(";");
}
function na(e) {
  return (e ?? []).map((t) => `${t.referenceKey}=${t.name}`).join(",");
}
function Op(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e ?? [])
    i.scopeId === t && n.set(i.referenceKey, `Shadows "${i.name}" declared in an outer scope.`);
  return n;
}
const Se = "/_elsa/workflow-management", Hp = "/publishing", Ut = {
  activities: ["workflows", "activities"],
  activityAvailabilitySettings: ["workflows", "activity-availability", "settings"],
  activityAvailabilityDiagnostics: ["workflows", "activity-availability", "diagnostics"]
};
function Wp(e) {
  return ns({
    queryKey: Ut.activities,
    queryFn: () => so(e),
    staleTime: 6e4
  });
}
function Bp(e) {
  return ns({
    queryKey: Ut.activityAvailabilitySettings,
    queryFn: () => uh(e)
  });
}
function Fp(e) {
  return ns({
    queryKey: Ut.activityAvailabilityDiagnostics,
    queryFn: () => Sl(e)
  });
}
function Kp(e) {
  const t = kf();
  return If({
    mutationFn: (n) => dh(e, n),
    onSuccess: (n) => {
      t.setQueryData(Ut.activityAvailabilitySettings, n), t.invalidateQueries({ queryKey: Ut.activityAvailabilityDiagnostics }), t.invalidateQueries({ queryKey: Ut.activities });
    }
  });
}
async function Xp(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), i = t.search.trim();
  return i && n.set("search", i), e.http.getJson(`${Se}/definitions?${n.toString()}`);
}
async function Yp(e, t) {
  const n = await e.http.getJson(`${Se}/definitions/${encodeURIComponent(t)}`);
  return n.draft ? { ...n, draft: { ...n.draft, state: ro(n.draft.state) } } : n;
}
async function qp(e, t, n) {
  const i = await e.http.postJson(
    `${Se}/design/scoped-variables/analyze`,
    { state: Un(t), nodeId: n }
  );
  return {
    visibleVariables: Array.isArray(i?.visibleVariables) ? i.visibleVariables : [],
    shadowingWarnings: Array.isArray(i?.shadowingWarnings) ? i.shadowingWarnings : []
  };
}
const ar = (e) => ({ visibleVariables: [], shadowingWarnings: [], status: e });
function Up(e, t, n, i) {
  const o = de(() => Vp(t, i), [i, t]), [s, a] = K(() => ar("loading"));
  return Q(() => {
    if (!t) {
      a(ar("unavailable"));
      return;
    }
    let c = !1;
    return a((u) => ({ ...u, status: "loading" })), qp(e, t, n).then(
      (u) => {
        c || a({ ...u, status: "ready" });
      },
      () => {
        c || a(ar("unavailable"));
      }
    ), () => {
      c = !0;
    };
  }, [e, n, o]), s;
}
async function Zp(e, t) {
  const n = await e.http.getJson(`${Se}/versions/${encodeURIComponent(t)}`);
  return { ...n, state: ro(n.state) };
}
async function Gp(e, t) {
  return e.http.postJson(`${Se}/definitions`, t);
}
async function Jp(e, t) {
  await e.http.deleteJson(`${Se}/definitions/${encodeURIComponent(t)}`);
}
async function Qp(e, t) {
  await e.http.postJson(`${Se}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function eh(e, t) {
  await e.http.deleteJson(`${Se}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function th(e, t, n) {
  return e.http.requestJson(
    `${Se}/definitions/${encodeURIComponent(t)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(n)
    }
  );
}
async function nh(e, t) {
  const n = await e.http.putJson(
    `${Se}/drafts/${encodeURIComponent(t.id)}`,
    { state: Un(t.state), layout: t.layout }
  );
  return { ...n, state: ro(n.state) };
}
async function ih(e, t) {
  return e.http.postJson(`${Se}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function oh(e, t) {
  return e.http.postJson(`${Se}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function rh(e, t) {
  const n = { ...t, state: Un(t.state) };
  try {
    return await e.http.postJson(`${Hp}/workflows/drafts/test-runs`, n);
  } catch (i) {
    const o = xh(i);
    if (o) return o;
    throw i;
  }
}
async function Nl(e, t) {
  return e.http.postJson(`${Se}/executables/${encodeURIComponent(t)}/run`, {});
}
async function jl(e) {
  const t = [`${Se}/executables`, "/_demo/workflows/executables"], n = [];
  for (const i of t)
    try {
      const o = await e.http.getJson(i);
      return sh(o);
    } catch (o) {
      n.push(o);
    }
  if (n.length > 0 && n.every(ia)) return [];
  throw n.find((i) => !ia(i)) ?? n[n.length - 1] ?? new Error("Workflow executables could not be loaded.");
}
function sh(e) {
  return Array.isArray(e) ? e : e.executables ?? [];
}
function ia(e) {
  if (!(e instanceof Error)) return !1;
  const t = e.message.toLowerCase();
  return /\b404\b/.test(t) || t.includes("not found");
}
async function ah(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.runKind && n.set("runKind", t.runKind), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const i = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${i ? `?${i}` : ""}`);
}
async function ch(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function lh(e, t, n) {
  return e.http.getJson(
    `/runtime/workflows/instances/${encodeURIComponent(t)}/activity-executions/${encodeURIComponent(n)}`
  );
}
async function so(e) {
  return e.http.getJson(`${Se}/activities`);
}
async function uh(e) {
  return e.http.getJson(`${Se}/activities/availability/settings`);
}
async function dh(e, t) {
  return e.http.putJson(`${Se}/activities/availability/settings`, t);
}
async function Sl(e) {
  return e.http.getJson(`${Se}/activities/availability/diagnostics`);
}
async function fh(e) {
  const t = await ao(e, [
    `${Se}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? oa(t) : oa(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function ph(e) {
  const t = await ao(e, [
    `${Se}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : Ti;
}
async function hh(e) {
  const t = await ao(e, [
    `${Se}/descriptors/variables`,
    "/descriptors/variables"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((i) => gh(i));
}
function gh(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = typeof t.alias == "string" && t.alias.length > 0, i = typeof t.typeName == "string" && t.typeName.length > 0;
  return n || i;
}
async function yh(e) {
  const t = await ao(e, [
    `${Se}/descriptors/storage-drivers`,
    "/descriptors/storage-drivers"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((i) => mh(i));
}
function mh(e) {
  return !!e && typeof e == "object" && typeof e.typeName == "string" && e.typeName.length > 0;
}
async function ao(e, t) {
  let n;
  for (const i of t)
    try {
      return await e.http.getJson(i);
    } catch (o) {
      n = o;
    }
  throw n;
}
function oa(e) {
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
function xh(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = ra(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return ra(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function ra(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const Ti = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function Cl(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e) {
    const o = t(i)?.trim() || "Uncategorized", s = n.get(o);
    s ? s.push(i) : n.set(o, [i]);
  }
  return [...n.entries()].map(([i, o]) => ({ category: i, items: o })).sort((i, o) => i.category.localeCompare(o.category));
}
const wh = [
  "Available",
  "BlockedByHostBaseline",
  "HiddenByManagementSettings",
  "RemovedFromCatalog",
  "UnresolvedReference"
], vh = {
  Available: "Available",
  BlockedByHostBaseline: "Host blocked",
  HiddenByManagementSettings: "Management hidden",
  RemovedFromCatalog: "Removed",
  UnresolvedReference: "Unresolved"
}, El = {
  Catalog: "Catalog",
  HostBaseline: "Host baseline",
  ManagementSettings: "Management settings"
}, bh = Object.keys(El);
function Nh(e) {
  const t = typeof e == "number" ? bh[e] : e;
  return t && El[t] || t?.toString() || "";
}
function kt(e) {
  return typeof e == "string" ? e : typeof e == "number" ? wh[e] ?? "Available" : "Available";
}
function Rn(e) {
  const t = kt(e);
  return vh[t] ?? t;
}
function kl(e) {
  return kt(e).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Il(e) {
  return kt(e) !== "Available";
}
function Al(e) {
  return kt(e.state) === "BlockedByHostBaseline";
}
function jh(e) {
  return e === "Only" || e === 1 ? "Only" : "AllExcept";
}
function Sh(e) {
  return e === "Only" ? 1 : 0;
}
function Ar(e) {
  const t = e?.rules;
  return {
    mode: jh(e?.mode),
    activityTypes: t?.activityTypes ?? [],
    sets: t?.sets ?? []
  };
}
function Ch(e) {
  return e.referenceKind === 0 || e.referenceKind === "ActivityType";
}
function Nn(e) {
  return e.activityTypeKey ?? e.activityDefinitionId ?? "";
}
function Eh(e) {
  return [...e?.items ?? []].filter(Ch).filter((t) => t.activityTypeKey && t.activityDefinitionId).sort((t, n) => zn(t).localeCompare(zn(n)));
}
function kh(e) {
  return [...e?.items ?? []].filter((t) => {
    const n = kt(t.state);
    return n === "RemovedFromCatalog" || n === "UnresolvedReference";
  }).sort((t, n) => (t.referenceName ?? "").localeCompare(n.referenceName ?? ""));
}
function Ih(e) {
  return Cl(e, (t) => t.category).map((t) => ({ category: t.category, entries: t.items }));
}
function Ah(e, t, n) {
  const i = e[t].includes(n);
  return e.mode === "Only" ? i : !i;
}
function cr(e, t, n, i) {
  const o = e.mode === "Only" ? i : !i;
  return e[t].includes(n) === o ? e : { ...e, [t]: Th(e[t], n) };
}
function _h(e, t, n) {
  const i = new Set(n.activityTypes), o = new Set(n.sets), s = /* @__PURE__ */ new Map();
  for (const c of t)
    if (o.has(c.name))
      for (const u of c.activityTypeKeys ?? [])
        s.has(u) || s.set(u, c.name);
  const a = /* @__PURE__ */ new Map();
  for (const c of e) {
    const u = Nn(c);
    if (Al(c)) {
      a.set(u, { enabled: !1, lockedBy: "host-baseline" });
      continue;
    }
    const l = s.get(u), d = i.has(u) || l !== void 0, f = n.mode === "Only" ? d : !d;
    a.set(u, l ? { enabled: f, lockedBy: "set-rule", governingSet: l } : { enabled: f, lockedBy: null });
  }
  return a;
}
function Dh(e, t) {
  const n = Ar(t), i = (o) => [...o].sort((s, a) => s.localeCompare(a)).join(`
`);
  return e.mode !== n.mode || i(e.activityTypes) !== i(n.activityTypes) || i(e.sets) !== i(n.sets);
}
function Th(e, t) {
  return e.includes(t) ? e.filter((n) => n !== t) : [...e, t].sort((n, i) => n.localeCompare(i));
}
function zn(e) {
  const t = e?.displayName?.trim();
  if (t && t.toLowerCase() !== "activity") return t;
  const n = Ph(e?.activityTypeKey);
  return $h(n) || t || e?.activityTypeKey || "Activity";
}
function sa(e) {
  const t = _l(e?.activityTypeKey);
  if (t.length === 0) return "";
  const n = t[t.length - 1], i = t[t.length - 2];
  return n === "Activity" && i ? `${i}.${n}` : n;
}
function aa(e) {
  const t = e.description?.trim();
  if (t) return t;
  const n = e.reason?.trim();
  return !n || !Il(e.state) ? "" : n;
}
function $h(e) {
  return e.replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/\bHttp\b/g, "HTTP").replace(/\bJson\b/g, "JSON").replace(/\bJava Script\b/g, "JavaScript").replace(/\bUrl\b/g, "URL").replace(/\bXml\b/g, "XML").trim();
}
function Ph(e) {
  const t = _l(e), n = t[t.length - 1] ?? "";
  return n === "Activity" && t.length > 1 ? t[t.length - 2] : n;
}
function _l(e) {
  return e?.split(".").filter(Boolean) ?? [];
}
function ca(e) {
  return (e ?? []).flatMap((t) => {
    if (!Pn(t)) return [];
    const n = (ct(t, ["displayName", "DisplayName"]) || ct(t, rs)).trim();
    if (!n) return [];
    const i = (os(t).alias || ct(t, ["typeName", "TypeName", "alias", "Alias"])).trim();
    return [{ name: n, typeName: i, description: ct(t, ["description", "Description"]).trim() }];
  });
}
function Mh(e, t) {
  const n = new Set(e.filter((i) => !!i));
  return (t?.items ?? []).find((i) => Il(i.state) ? [i.activityDefinitionId, i.activityTypeKey, i.referenceName].some((o) => o && n.has(o)) : !1) ?? null;
}
const Rh = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function Dl(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function cs(e) {
  return Dl(e.name);
}
function zh(e, t) {
  const n = cs(t), i = e[n];
  return t.isWrapped === !1 ? i ?? t.defaultValue ?? "" : $l(i, t);
}
function Tl(e, t) {
  return $l(e[cs(t)], t);
}
function Lh(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function Vh(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function la(e, t, n) {
  return {
    ...e,
    [cs(t)]: n
  };
}
function Oh(e, t) {
  return t.isWrapped === !1 ? zh(e, t) : Tl(e, t).expression.value;
}
function $l(e, t) {
  return qh(e) ? {
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
const Hh = /* @__PURE__ */ new Set([
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
function Wh(e) {
  const t = e?.trim();
  if (!t) return null;
  const n = /^([\w.+]+)\[\]/.exec(t);
  if (n) return { elementTypeName: n[1] };
  const i = t.indexOf("`");
  if (i < 0) return null;
  const o = t.slice(0, i), s = (o.split(".").pop() ?? o).toLowerCase();
  return Hh.has(s) ? { elementTypeName: Bh(t.slice(i)) } : null;
}
function Bh(e) {
  const t = /\[\[([\w.+]+)/.exec(e);
  if (t) return t[1];
  const n = /\[([\w.+]+)/.exec(e);
  return n ? n[1] : null;
}
function Fh(e) {
  const t = e.uiHint?.toLowerCase();
  return t === "json" || t === "code" ? !0 : e.uiSpecifications?.repeater === !1;
}
function Kh(e) {
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
function Xh(e) {
  const t = (e ?? "").toLowerCase();
  return t === "system.boolean" || t === "boolean" || t === "bool" ? !1 : "";
}
function Yh(e, t) {
  return {
    ...e,
    typeName: t ?? "System.String",
    isWrapped: !1,
    // The description documents the collection as a whole; repeating it on every row is noise.
    description: null
  };
}
function lr(e, t, n) {
  if (t === n || t < 0 || n < 0 || t >= e.length || n >= e.length) return e;
  const i = [...e], [o] = i.splice(t, 1);
  return i.splice(n, 0, o), i;
}
function qh(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
function ls(e) {
  return us(e?.trim() ?? "") || e;
}
function us(e) {
  if (!e) return "";
  const t = Uh(e);
  if (!t) return "";
  const n = /^(.*)((?:\[\])+)$/.exec(t);
  if (n) return `${us(n[1])}${n[2]}`;
  const i = t.indexOf("`");
  if (i >= 0) {
    const o = ua(t.slice(0, i)), s = Zh(t.slice(i));
    return s.length > 0 ? `${o}<${s.join(", ")}>` : o;
  }
  return ua(t);
}
function Uh(e) {
  let t = 0;
  for (let n = 0; n < e.length; n++) {
    const i = e[n];
    if (i === "[") t++;
    else if (i === "]") t--;
    else if (i === "," && t === 0) return e.slice(0, n).trim();
  }
  return e.trim();
}
function ua(e) {
  const t = e.split(".").filter(Boolean).at(-1) ?? e;
  return t.split("+").filter(Boolean).at(-1) ?? t;
}
function Zh(e) {
  const t = e.indexOf("[");
  if (t < 0) return [];
  const n = da(e, t);
  return n == null ? [] : Gh(n).map((i) => {
    const o = i.trim(), s = o.startsWith("[") ? da(o, 0) ?? o : o;
    return us(s);
  }).filter(Boolean);
}
function da(e, t) {
  let n = 0;
  for (let i = t; i < e.length; i++)
    if (e[i] === "[") n++;
    else if (e[i] === "]" && --n === 0) return e.slice(t + 1, i);
  return null;
}
function Gh(e) {
  const t = [];
  let n = 0, i = 0;
  for (let o = 0; o < e.length; o++) {
    const s = e[o];
    s === "[" ? n++ : s === "]" ? n-- : s === "," && n === 0 && (t.push(e.slice(i, o)), i = o + 1);
  }
  return t.push(e.slice(i)), t.map((o) => o.trim()).filter(Boolean);
}
function Re(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function ds(e, t) {
  if (!e || !t) return "";
  const n = Date.parse(e), i = Date.parse(t);
  if (Number.isNaN(n) || Number.isNaN(i) || i < n) return "";
  const o = Math.round((i - n) / 1e3);
  if (o < 60) return `${o}s`;
  const s = Math.floor(o / 60), a = o % 60;
  if (s < 60) return a ? `${s}m ${a}s` : `${s}m`;
  const c = Math.floor(s / 60), u = s % 60;
  return u ? `${c}h ${u}m` : `${c}h`;
}
function It(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function co(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ r.jsx(Uc, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ r.jsx(Jr, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ r.jsx(yf, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ r.jsx(Jt, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ r.jsx(gf, { size: 15 });
    default:
      return /* @__PURE__ */ r.jsx(qn, { size: 15 });
  }
}
function fa(e) {
  return `${e.activityTypeKey ?? ""}|${e.activityDefinitionId ?? ""}`;
}
function Jh(e, t) {
  const n = (a) => (a ?? "").split(".").map((c) => Number.parseInt(c, 10) || 0), i = n(e), o = n(t), s = Math.max(i.length, o.length);
  for (let a = 0; a < s; a++) {
    const c = (i[a] ?? 0) - (o[a] ?? 0);
    if (c !== 0) return c;
  }
  return 0;
}
function Qh(e) {
  if (e.lockedBy === "host-baseline") return "Blocked by the host baseline";
  if (e.lockedBy === "set-rule") return `Controlled by the "${e.governingSet}" set rule`;
}
function Pl({ icon: e }) {
  return /* @__PURE__ */ r.jsx("span", { className: "wf-activity-icon", "data-icon": e, "aria-hidden": "true", children: co(e) });
}
function eg({ context: e }) {
  const t = Bp(e), n = Fp(e), i = Wp(e), o = Kp(e), s = t.data ?? null, a = n.data ?? null, c = t.isLoading || n.isLoading, u = o.isPending, [l, d] = K(() => Ar(s)), [f, p] = K(""), [h, y] = K(!1), [v, w] = K(null), [x, b] = K(null);
  Q(() => {
    d(Ar(s));
  }, [s]);
  const g = de(() => Eh(a), [a]), m = de(() => kh(a), [a]), j = de(() => a?.sets ?? [], [a]), N = de(
    () => _h(g, j, l),
    [g, j, l]
  ), S = (V) => N.get(Nn(V)) ?? { enabled: !0, lockedBy: null }, k = de(() => {
    const V = /* @__PURE__ */ new Map();
    for (const Y of i.data?.activities ?? []) {
      if (!Y.activityTypeKey) continue;
      const J = V.get(Y.activityTypeKey);
      (!J || Jh(Y.version, J.version) > 0) && V.set(Y.activityTypeKey, Y);
    }
    return V;
  }, [i.data]), _ = (V) => k.get(V.activityTypeKey ?? ""), M = de(() => {
    const V = f.trim().toLowerCase(), Y = h ? g.filter((J) => !(N.get(Nn(J))?.enabled ?? !0)) : g;
    return V ? Y.filter((J) => [
      zn(J),
      sa(J),
      aa(J),
      J.activityTypeKey,
      J.category
    ].some((Z) => (Z ?? "").toLowerCase().includes(V))) : Y;
  }, [g, f, h, N]), I = de(() => Ih(M), [M]), $ = x ? g.find((V) => fa(V) === x) ?? null : null, L = g.filter(Al).length, C = g.filter((V) => kt(V.state) === "HiddenByManagementSettings").length, A = de(() => Dh(l, s), [l, s]), E = o.error ?? t.error ?? n.error, D = E instanceof Error ? E.message : E ? "Activity availability could not be loaded." : null, P = (V) => {
    w(null), d(V);
  }, T = (V) => P((Y) => ({ ...Y, mode: V })), B = (V, Y) => P((J) => cr(J, "activityTypes", V, Y)), W = (V, Y) => P((J) => V.reduce((Z, R) => cr(Z, "activityTypes", R, Y), J)), H = (V, Y) => P((J) => cr(J, "sets", V, Y)), q = () => {
    w(null), o.mutate(
      {
        scope: s?.scope ?? "host-default",
        mode: Sh(l.mode),
        rules: { activityTypes: l.activityTypes, sets: l.sets }
      },
      { onSuccess: () => w("Activity availability saved.") }
    );
  };
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-page availability-page", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-page-header", children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("div", { className: "wf-kicker", children: "Workflows" }),
        /* @__PURE__ */ r.jsxs("h2", { children: [
          /* @__PURE__ */ r.jsx(mf, { size: 18 }),
          " Activity availability"
        ] }),
        /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Control which activities can be added to new workflows. Host baseline rules always take precedence." })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "wf-actions", children: [
        A && !u && /* @__PURE__ */ r.jsx("span", { className: "wf-chip availability-dirty", children: "Unsaved changes" }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", className: "availability-save", onClick: q, disabled: c || u || !A, children: [
          /* @__PURE__ */ r.jsx(Zc, { size: 15 }),
          u ? "Saving…" : "Save"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "availability-body", children: [
      D && /* @__PURE__ */ r.jsx("div", { className: "availability-banner availability-banner-error", children: D }),
      v && !D && /* @__PURE__ */ r.jsx("div", { className: "availability-banner availability-banner-success", children: v }),
      /* @__PURE__ */ r.jsxs("div", { className: "availability-mode", role: "group", "aria-label": "Activity availability mode", children: [
        /* @__PURE__ */ r.jsxs("button", { type: "button", className: l.mode === "AllExcept" ? "active" : "", onClick: () => T("AllExcept"), disabled: c || u, children: [
          /* @__PURE__ */ r.jsx(rr, { size: 15 }),
          /* @__PURE__ */ r.jsxs("span", { children: [
            /* @__PURE__ */ r.jsx("strong", { children: "All except" }),
            /* @__PURE__ */ r.jsx("em", { children: "Activities are available unless you turn them off" })
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", className: l.mode === "Only" ? "active" : "", onClick: () => T("Only"), disabled: c || u, children: [
          /* @__PURE__ */ r.jsx(Bs, { size: 15 }),
          /* @__PURE__ */ r.jsxs("span", { children: [
            /* @__PURE__ */ r.jsx("strong", { children: "Only" }),
            /* @__PURE__ */ r.jsx("em", { children: "Activities are hidden unless you turn them on" })
          ] })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "availability-counts", children: [
        /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx(Bs, { size: 14 }),
          " ",
          L,
          " host blocked"
        ] }),
        /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx(rr, { size: 14 }),
          " ",
          C,
          " management hidden"
        ] }),
        /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx(Hi, { size: 14 }),
          " ",
          m.length,
          " unresolved"
        ] })
      ] }),
      j.length > 0 && /* @__PURE__ */ r.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ r.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ r.jsx(Qr, { size: 14 }),
          " Sets"
        ] }),
        /* @__PURE__ */ r.jsx("div", { className: "availability-set-list", children: j.map((V) => /* @__PURE__ */ r.jsxs("label", { className: "availability-set-option", children: [
          /* @__PURE__ */ r.jsx(
            "input",
            {
              type: "checkbox",
              role: "switch",
              className: "wf-switch-input",
              "aria-label": `Activities in the ${V.name} set available in new workflows`,
              checked: Ah(l, "sets", V.name),
              disabled: c || u,
              onChange: (Y) => H(V.name, Y.target.checked)
            }
          ),
          /* @__PURE__ */ r.jsx("span", { children: V.name }),
          /* @__PURE__ */ r.jsx("code", { children: (V.activityTypeKeys ?? []).length })
        ] }, V.name)) })
      ] }),
      /* @__PURE__ */ r.jsxs("section", { className: "availability-section availability-section-grow", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "availability-section-head", children: [
          /* @__PURE__ */ r.jsxs("h3", { className: "wf-section-label", children: [
            /* @__PURE__ */ r.jsx(Gc, { size: 14 }),
            " Activities"
          ] }),
          /* @__PURE__ */ r.jsxs("div", { className: "availability-section-tools", children: [
            /* @__PURE__ */ r.jsxs(
              "button",
              {
                type: "button",
                className: `availability-filter-toggle${h ? " active" : ""}`,
                "aria-pressed": h,
                title: "Show only activities that are turned off or blocked",
                onClick: () => y((V) => !V),
                children: [
                  /* @__PURE__ */ r.jsx(rr, { size: 13 }),
                  "Hidden only"
                ]
              }
            ),
            /* @__PURE__ */ r.jsxs("div", { className: "wf-search availability-search", children: [
              /* @__PURE__ */ r.jsx(io, { size: 14 }),
              /* @__PURE__ */ r.jsx("input", { type: "search", value: f, placeholder: "Filter activities…", onChange: (V) => p(V.target.value) })
            ] })
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("div", { className: "availability-activity-browser", children: [
          /* @__PURE__ */ r.jsxs("div", { className: "availability-activity-list", children: [
            c && g.length === 0 && /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Loading availability…" }),
            !c && g.length === 0 && /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No availability diagnostics reported." }),
            !c && g.length > 0 && M.length === 0 && /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: h && !f.trim() ? "No hidden activities — everything is turned on." : "No activities match the filter." }),
            I.map((V) => {
              const Y = V.entries.filter((X) => S(X).lockedBy === null), J = Y.filter((X) => S(X).enabled).length, Z = Y.length > 0 && J === Y.length, R = J > 0 && !Z;
              return /* @__PURE__ */ r.jsxs("div", { className: "availability-group", children: [
                /* @__PURE__ */ r.jsxs("div", { className: "availability-group-header", children: [
                  /* @__PURE__ */ r.jsxs("span", { className: "availability-group-title", children: [
                    V.category,
                    /* @__PURE__ */ r.jsx("small", { children: V.entries.length })
                  ] }),
                  Y.length > 0 && /* @__PURE__ */ r.jsx(
                    "input",
                    {
                      type: "checkbox",
                      role: "switch",
                      className: "wf-switch-input",
                      "aria-label": `Toggle the ${Y.length} listed ${V.category} activities`,
                      checked: Z,
                      ref: (X) => {
                        X && (X.indeterminate = R);
                      },
                      disabled: c || u,
                      onChange: () => W(Y.map(Nn), !Z)
                    }
                  )
                ] }),
                V.entries.map((X) => {
                  const le = Nn(X), ce = fa(X), ee = kt(X.state), ne = S(X), fe = ne.lockedBy !== null, O = fn(_(X)), te = zn(X), ge = sa(X), ye = aa(X), _e = x === ce;
                  return /* @__PURE__ */ r.jsxs("div", { className: `availability-activity-row${_e ? " selected" : ""}${fe ? " disabled" : ""}`, children: [
                    /* @__PURE__ */ r.jsxs(
                      "button",
                      {
                        type: "button",
                        className: "availability-activity-open",
                        "aria-expanded": _e,
                        title: "Show activity details",
                        onClick: () => b(_e ? null : ce),
                        children: [
                          /* @__PURE__ */ r.jsx(Pl, { icon: O }),
                          /* @__PURE__ */ r.jsxs("span", { className: "availability-activity-main", children: [
                            /* @__PURE__ */ r.jsxs("span", { className: "availability-activity-title-line", children: [
                              /* @__PURE__ */ r.jsx("strong", { children: te }),
                              ge && /* @__PURE__ */ r.jsx("code", { title: X.activityTypeKey ?? void 0, children: ge })
                            ] }),
                            ye && /* @__PURE__ */ r.jsx("span", { className: "availability-activity-description", title: ye, children: ye })
                          ] })
                        ]
                      }
                    ),
                    ee !== "Available" && /* @__PURE__ */ r.jsx("em", { className: `availability-state ${kl(X.state)}`, children: Rn(X.state) }),
                    /* @__PURE__ */ r.jsx(
                      "input",
                      {
                        type: "checkbox",
                        role: "switch",
                        className: "wf-switch-input",
                        "aria-label": `${te} available in new workflows`,
                        title: Qh(ne),
                        checked: ne.enabled,
                        disabled: c || u || fe,
                        onChange: (De) => B(le, De.target.checked)
                      }
                    )
                  ] }, ce);
                })
              ] }, V.category);
            })
          ] }),
          $ && /* @__PURE__ */ r.jsx(
            tg,
            {
              entry: $,
              catalogItem: _($),
              onClose: () => b(null)
            }
          )
        ] })
      ] }),
      m.length > 0 && /* @__PURE__ */ r.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ r.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ r.jsx(Hi, { size: 14 }),
          " Unresolved references"
        ] }),
        /* @__PURE__ */ r.jsx("div", { className: "availability-unresolved-list", children: m.map((V) => /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx("strong", { children: V.referenceName }),
          /* @__PURE__ */ r.jsx("em", { children: Rn(V.state) })
        ] }, `${V.layer}-${V.referenceKind}-${V.referenceName}`)) })
      ] })
    ] })
  ] });
}
function tg({
  entry: e,
  catalogItem: t,
  onClose: n
}) {
  const i = zn(e), o = e.description?.trim() || t?.description?.trim(), s = de(() => ca(t?.inputs), [t]), a = de(() => ca(t?.outputs), [t]), c = de(() => JSON.stringify({ diagnostic: e, catalog: t }, null, 2), [e, t]), u = [
    ["Type", e.activityTypeKey],
    ["Definition ID", e.activityDefinitionId],
    ["Category", e.category],
    ["Version", t?.version],
    ["Execution", t?.executionType],
    ["Policy layer", Nh(e.layer)]
  ];
  return /* @__PURE__ */ r.jsxs("aside", { className: "availability-details", "aria-label": `${i} details`, children: [
    /* @__PURE__ */ r.jsxs("header", { className: "availability-details-header", children: [
      /* @__PURE__ */ r.jsx(Pl, { icon: fn(t) }),
      /* @__PURE__ */ r.jsx("h4", { children: i }),
      /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-icon-button availability-details-close", "aria-label": "Close details", onClick: n, children: /* @__PURE__ */ r.jsx(es, { size: 14 }) })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "availability-details-body", children: [
      /* @__PURE__ */ r.jsxs("p", { className: "availability-details-status", children: [
        /* @__PURE__ */ r.jsx("em", { className: `availability-state ${kl(e.state)}`, children: Rn(e.state) }),
        e.reason?.trim() && /* @__PURE__ */ r.jsx("span", { children: e.reason })
      ] }),
      o && /* @__PURE__ */ r.jsx("p", { className: "availability-details-description", children: o }),
      /* @__PURE__ */ r.jsx("dl", { className: "availability-details-meta", children: u.map(([l, d]) => d?.trim() ? /* @__PURE__ */ r.jsxs(Ke.Fragment, { children: [
        /* @__PURE__ */ r.jsx("dt", { children: l }),
        /* @__PURE__ */ r.jsx("dd", { children: /* @__PURE__ */ r.jsx("code", { children: d }) })
      ] }, l) : null) }),
      s.length > 0 && /* @__PURE__ */ r.jsx(pa, { title: "Inputs", items: s }),
      a.length > 0 && /* @__PURE__ */ r.jsx(pa, { title: "Outputs", items: a }),
      !t && /* @__PURE__ */ r.jsx("p", { className: "wf-muted availability-details-note", children: "Catalog metadata is unavailable for this activity — it is not exposed by the currently saved policy." }),
      /* @__PURE__ */ r.jsxs("details", { className: "availability-details-raw", children: [
        /* @__PURE__ */ r.jsx("summary", { children: "Raw metadata" }),
        /* @__PURE__ */ r.jsx("pre", { children: c })
      ] })
    ] })
  ] });
}
function pa({ title: e, items: t }) {
  return /* @__PURE__ */ r.jsxs("section", { className: "availability-details-arguments", children: [
    /* @__PURE__ */ r.jsx("h5", { children: e }),
    /* @__PURE__ */ r.jsx("ul", { children: t.map((n) => /* @__PURE__ */ r.jsxs("li", { title: n.description || void 0, children: [
      /* @__PURE__ */ r.jsx("strong", { children: n.name }),
      n.typeName && /* @__PURE__ */ r.jsx("code", { children: ls(n.typeName) })
    ] }, n.name)) })
  ] });
}
function ke(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, i; n < e.length; n++)
      (i = ke(e[n])) !== "" && (t += (t && " ") + i);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var ng = { value: () => {
} };
function lo() {
  for (var e = 0, t = arguments.length, n = {}, i; e < t; ++e) {
    if (!(i = arguments[e] + "") || i in n || /[\s.]/.test(i)) throw new Error("illegal type: " + i);
    n[i] = [];
  }
  return new $i(n);
}
function $i(e) {
  this._ = e;
}
function ig(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var i = "", o = n.indexOf(".");
    if (o >= 0 && (i = n.slice(o + 1), n = n.slice(0, o)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: i };
  });
}
$i.prototype = lo.prototype = {
  constructor: $i,
  on: function(e, t) {
    var n = this._, i = ig(e + "", n), o, s = -1, a = i.length;
    if (arguments.length < 2) {
      for (; ++s < a; ) if ((o = (e = i[s]).type) && (o = og(n[o], e.name))) return o;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++s < a; )
      if (o = (e = i[s]).type) n[o] = ha(n[o], e.name, t);
      else if (t == null) for (o in n) n[o] = ha(n[o], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new $i(e);
  },
  call: function(e, t) {
    if ((o = arguments.length - 2) > 0) for (var n = new Array(o), i = 0, o, s; i < o; ++i) n[i] = arguments[i + 2];
    if (!this._.hasOwnProperty(e)) throw new Error("unknown type: " + e);
    for (s = this._[e], i = 0, o = s.length; i < o; ++i) s[i].value.apply(t, n);
  },
  apply: function(e, t, n) {
    if (!this._.hasOwnProperty(e)) throw new Error("unknown type: " + e);
    for (var i = this._[e], o = 0, s = i.length; o < s; ++o) i[o].value.apply(t, n);
  }
};
function og(e, t) {
  for (var n = 0, i = e.length, o; n < i; ++n)
    if ((o = e[n]).name === t)
      return o.value;
}
function ha(e, t, n) {
  for (var i = 0, o = e.length; i < o; ++i)
    if (e[i].name === t) {
      e[i] = ng, e = e.slice(0, i).concat(e.slice(i + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var _r = "http://www.w3.org/1999/xhtml";
const ga = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: _r,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function uo(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), ga.hasOwnProperty(t) ? { space: ga[t], local: e } : e;
}
function rg(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === _r && t.documentElement.namespaceURI === _r ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function sg(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function Ml(e) {
  var t = uo(e);
  return (t.local ? sg : rg)(t);
}
function ag() {
}
function fs(e) {
  return e == null ? ag : function() {
    return this.querySelector(e);
  };
}
function cg(e) {
  typeof e != "function" && (e = fs(e));
  for (var t = this._groups, n = t.length, i = new Array(n), o = 0; o < n; ++o)
    for (var s = t[o], a = s.length, c = i[o] = new Array(a), u, l, d = 0; d < a; ++d)
      (u = s[d]) && (l = e.call(u, u.__data__, d, s)) && ("__data__" in u && (l.__data__ = u.__data__), c[d] = l);
  return new Xe(i, this._parents);
}
function lg(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function ug() {
  return [];
}
function Rl(e) {
  return e == null ? ug : function() {
    return this.querySelectorAll(e);
  };
}
function dg(e) {
  return function() {
    return lg(e.apply(this, arguments));
  };
}
function fg(e) {
  typeof e == "function" ? e = dg(e) : e = Rl(e);
  for (var t = this._groups, n = t.length, i = [], o = [], s = 0; s < n; ++s)
    for (var a = t[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && (i.push(e.call(u, u.__data__, l, a)), o.push(u));
  return new Xe(i, o);
}
function zl(e) {
  return function() {
    return this.matches(e);
  };
}
function Ll(e) {
  return function(t) {
    return t.matches(e);
  };
}
var pg = Array.prototype.find;
function hg(e) {
  return function() {
    return pg.call(this.children, e);
  };
}
function gg() {
  return this.firstElementChild;
}
function yg(e) {
  return this.select(e == null ? gg : hg(typeof e == "function" ? e : Ll(e)));
}
var mg = Array.prototype.filter;
function xg() {
  return Array.from(this.children);
}
function wg(e) {
  return function() {
    return mg.call(this.children, e);
  };
}
function vg(e) {
  return this.selectAll(e == null ? xg : wg(typeof e == "function" ? e : Ll(e)));
}
function bg(e) {
  typeof e != "function" && (e = zl(e));
  for (var t = this._groups, n = t.length, i = new Array(n), o = 0; o < n; ++o)
    for (var s = t[o], a = s.length, c = i[o] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new Xe(i, this._parents);
}
function Vl(e) {
  return new Array(e.length);
}
function Ng() {
  return new Xe(this._enter || this._groups.map(Vl), this._parents);
}
function Xi(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
Xi.prototype = {
  constructor: Xi,
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
function jg(e) {
  return function() {
    return e;
  };
}
function Sg(e, t, n, i, o, s) {
  for (var a = 0, c, u = t.length, l = s.length; a < l; ++a)
    (c = t[a]) ? (c.__data__ = s[a], i[a] = c) : n[a] = new Xi(e, s[a]);
  for (; a < u; ++a)
    (c = t[a]) && (o[a] = c);
}
function Cg(e, t, n, i, o, s, a) {
  var c, u, l = /* @__PURE__ */ new Map(), d = t.length, f = s.length, p = new Array(d), h;
  for (c = 0; c < d; ++c)
    (u = t[c]) && (p[c] = h = a.call(u, u.__data__, c, t) + "", l.has(h) ? o[c] = u : l.set(h, u));
  for (c = 0; c < f; ++c)
    h = a.call(e, s[c], c, s) + "", (u = l.get(h)) ? (i[c] = u, u.__data__ = s[c], l.delete(h)) : n[c] = new Xi(e, s[c]);
  for (c = 0; c < d; ++c)
    (u = t[c]) && l.get(p[c]) === u && (o[c] = u);
}
function Eg(e) {
  return e.__data__;
}
function kg(e, t) {
  if (!arguments.length) return Array.from(this, Eg);
  var n = t ? Cg : Sg, i = this._parents, o = this._groups;
  typeof e != "function" && (e = jg(e));
  for (var s = o.length, a = new Array(s), c = new Array(s), u = new Array(s), l = 0; l < s; ++l) {
    var d = i[l], f = o[l], p = f.length, h = Ig(e.call(d, d && d.__data__, l, i)), y = h.length, v = c[l] = new Array(y), w = a[l] = new Array(y), x = u[l] = new Array(p);
    n(d, f, v, w, x, h, t);
    for (var b = 0, g = 0, m, j; b < y; ++b)
      if (m = v[b]) {
        for (b >= g && (g = b + 1); !(j = w[g]) && ++g < y; ) ;
        m._next = j || null;
      }
  }
  return a = new Xe(a, i), a._enter = c, a._exit = u, a;
}
function Ig(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function Ag() {
  return new Xe(this._exit || this._groups.map(Vl), this._parents);
}
function _g(e, t, n) {
  var i = this.enter(), o = this, s = this.exit();
  return typeof e == "function" ? (i = e(i), i && (i = i.selection())) : i = i.append(e + ""), t != null && (o = t(o), o && (o = o.selection())), n == null ? s.remove() : n(s), i && o ? i.merge(o).order() : o;
}
function Dg(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, i = t._groups, o = n.length, s = i.length, a = Math.min(o, s), c = new Array(o), u = 0; u < a; ++u)
    for (var l = n[u], d = i[u], f = l.length, p = c[u] = new Array(f), h, y = 0; y < f; ++y)
      (h = l[y] || d[y]) && (p[y] = h);
  for (; u < o; ++u)
    c[u] = n[u];
  return new Xe(c, this._parents);
}
function Tg() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var i = e[t], o = i.length - 1, s = i[o], a; --o >= 0; )
      (a = i[o]) && (s && a.compareDocumentPosition(s) ^ 4 && s.parentNode.insertBefore(a, s), s = a);
  return this;
}
function $g(e) {
  e || (e = Pg);
  function t(f, p) {
    return f && p ? e(f.__data__, p.__data__) : !f - !p;
  }
  for (var n = this._groups, i = n.length, o = new Array(i), s = 0; s < i; ++s) {
    for (var a = n[s], c = a.length, u = o[s] = new Array(c), l, d = 0; d < c; ++d)
      (l = a[d]) && (u[d] = l);
    u.sort(t);
  }
  return new Xe(o, this._parents).order();
}
function Pg(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function Mg() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function Rg() {
  return Array.from(this);
}
function zg() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var i = e[t], o = 0, s = i.length; o < s; ++o) {
      var a = i[o];
      if (a) return a;
    }
  return null;
}
function Lg() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function Vg() {
  return !this.node();
}
function Og(e) {
  for (var t = this._groups, n = 0, i = t.length; n < i; ++n)
    for (var o = t[n], s = 0, a = o.length, c; s < a; ++s)
      (c = o[s]) && e.call(c, c.__data__, s, o);
  return this;
}
function Hg(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Wg(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Bg(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function Fg(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function Kg(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function Xg(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function Yg(e, t) {
  var n = uo(e);
  if (arguments.length < 2) {
    var i = this.node();
    return n.local ? i.getAttributeNS(n.space, n.local) : i.getAttribute(n);
  }
  return this.each((t == null ? n.local ? Wg : Hg : typeof t == "function" ? n.local ? Xg : Kg : n.local ? Fg : Bg)(n, t));
}
function Ol(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function qg(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Ug(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function Zg(e, t, n) {
  return function() {
    var i = t.apply(this, arguments);
    i == null ? this.style.removeProperty(e) : this.style.setProperty(e, i, n);
  };
}
function Gg(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? qg : typeof t == "function" ? Zg : Ug)(e, t, n ?? "")) : en(this.node(), e);
}
function en(e, t) {
  return e.style.getPropertyValue(t) || Ol(e).getComputedStyle(e, null).getPropertyValue(t);
}
function Jg(e) {
  return function() {
    delete this[e];
  };
}
function Qg(e, t) {
  return function() {
    this[e] = t;
  };
}
function ey(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function ty(e, t) {
  return arguments.length > 1 ? this.each((t == null ? Jg : typeof t == "function" ? ey : Qg)(e, t)) : this.node()[e];
}
function Hl(e) {
  return e.trim().split(/^|\s+/);
}
function ps(e) {
  return e.classList || new Wl(e);
}
function Wl(e) {
  this._node = e, this._names = Hl(e.getAttribute("class") || "");
}
Wl.prototype = {
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
function Bl(e, t) {
  for (var n = ps(e), i = -1, o = t.length; ++i < o; ) n.add(t[i]);
}
function Fl(e, t) {
  for (var n = ps(e), i = -1, o = t.length; ++i < o; ) n.remove(t[i]);
}
function ny(e) {
  return function() {
    Bl(this, e);
  };
}
function iy(e) {
  return function() {
    Fl(this, e);
  };
}
function oy(e, t) {
  return function() {
    (t.apply(this, arguments) ? Bl : Fl)(this, e);
  };
}
function ry(e, t) {
  var n = Hl(e + "");
  if (arguments.length < 2) {
    for (var i = ps(this.node()), o = -1, s = n.length; ++o < s; ) if (!i.contains(n[o])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? oy : t ? ny : iy)(n, t));
}
function sy() {
  this.textContent = "";
}
function ay(e) {
  return function() {
    this.textContent = e;
  };
}
function cy(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function ly(e) {
  return arguments.length ? this.each(e == null ? sy : (typeof e == "function" ? cy : ay)(e)) : this.node().textContent;
}
function uy() {
  this.innerHTML = "";
}
function dy(e) {
  return function() {
    this.innerHTML = e;
  };
}
function fy(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function py(e) {
  return arguments.length ? this.each(e == null ? uy : (typeof e == "function" ? fy : dy)(e)) : this.node().innerHTML;
}
function hy() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function gy() {
  return this.each(hy);
}
function yy() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function my() {
  return this.each(yy);
}
function xy(e) {
  var t = typeof e == "function" ? e : Ml(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function wy() {
  return null;
}
function vy(e, t) {
  var n = typeof e == "function" ? e : Ml(e), i = t == null ? wy : typeof t == "function" ? t : fs(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), i.apply(this, arguments) || null);
  });
}
function by() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Ny() {
  return this.each(by);
}
function jy() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Sy() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Cy(e) {
  return this.select(e ? Sy : jy);
}
function Ey(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function ky(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function Iy(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", i = t.indexOf(".");
    return i >= 0 && (n = t.slice(i + 1), t = t.slice(0, i)), { type: t, name: n };
  });
}
function Ay(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, i = -1, o = t.length, s; n < o; ++n)
        s = t[n], (!e.type || s.type === e.type) && s.name === e.name ? this.removeEventListener(s.type, s.listener, s.options) : t[++i] = s;
      ++i ? t.length = i : delete this.__on;
    }
  };
}
function _y(e, t, n) {
  return function() {
    var i = this.__on, o, s = ky(t);
    if (i) {
      for (var a = 0, c = i.length; a < c; ++a)
        if ((o = i[a]).type === e.type && o.name === e.name) {
          this.removeEventListener(o.type, o.listener, o.options), this.addEventListener(o.type, o.listener = s, o.options = n), o.value = t;
          return;
        }
    }
    this.addEventListener(e.type, s, n), o = { type: e.type, name: e.name, value: t, listener: s, options: n }, i ? i.push(o) : this.__on = [o];
  };
}
function Dy(e, t, n) {
  var i = Iy(e + ""), o, s = i.length, a;
  if (arguments.length < 2) {
    var c = this.node().__on;
    if (c) {
      for (var u = 0, l = c.length, d; u < l; ++u)
        for (o = 0, d = c[u]; o < s; ++o)
          if ((a = i[o]).type === d.type && a.name === d.name)
            return d.value;
    }
    return;
  }
  for (c = t ? _y : Ay, o = 0; o < s; ++o) this.each(c(i[o], t, n));
  return this;
}
function Kl(e, t, n) {
  var i = Ol(e), o = i.CustomEvent;
  typeof o == "function" ? o = new o(t, n) : (o = i.document.createEvent("Event"), n ? (o.initEvent(t, n.bubbles, n.cancelable), o.detail = n.detail) : o.initEvent(t, !1, !1)), e.dispatchEvent(o);
}
function Ty(e, t) {
  return function() {
    return Kl(this, e, t);
  };
}
function $y(e, t) {
  return function() {
    return Kl(this, e, t.apply(this, arguments));
  };
}
function Py(e, t) {
  return this.each((typeof t == "function" ? $y : Ty)(e, t));
}
function* My() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var i = e[t], o = 0, s = i.length, a; o < s; ++o)
      (a = i[o]) && (yield a);
}
var Xl = [null];
function Xe(e, t) {
  this._groups = e, this._parents = t;
}
function Gn() {
  return new Xe([[document.documentElement]], Xl);
}
function Ry() {
  return this;
}
Xe.prototype = Gn.prototype = {
  constructor: Xe,
  select: cg,
  selectAll: fg,
  selectChild: yg,
  selectChildren: vg,
  filter: bg,
  data: kg,
  enter: Ng,
  exit: Ag,
  join: _g,
  merge: Dg,
  selection: Ry,
  order: Tg,
  sort: $g,
  call: Mg,
  nodes: Rg,
  node: zg,
  size: Lg,
  empty: Vg,
  each: Og,
  attr: Yg,
  style: Gg,
  property: ty,
  classed: ry,
  text: ly,
  html: py,
  raise: gy,
  lower: my,
  append: xy,
  insert: vy,
  remove: Ny,
  clone: Cy,
  datum: Ey,
  on: Dy,
  dispatch: Py,
  [Symbol.iterator]: My
};
function Be(e) {
  return typeof e == "string" ? new Xe([[document.querySelector(e)]], [document.documentElement]) : new Xe([[e]], Xl);
}
function zy(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Ze(e, t) {
  if (e = zy(e), t === void 0 && (t = e.currentTarget), t) {
    var n = t.ownerSVGElement || t;
    if (n.createSVGPoint) {
      var i = n.createSVGPoint();
      return i.x = e.clientX, i.y = e.clientY, i = i.matrixTransform(t.getScreenCTM().inverse()), [i.x, i.y];
    }
    if (t.getBoundingClientRect) {
      var o = t.getBoundingClientRect();
      return [e.clientX - o.left - t.clientLeft, e.clientY - o.top - t.clientTop];
    }
  }
  return [e.pageX, e.pageY];
}
const Ly = { passive: !1 }, Ln = { capture: !0, passive: !1 };
function ur(e) {
  e.stopImmediatePropagation();
}
function Zt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Yl(e) {
  var t = e.document.documentElement, n = Be(e).on("dragstart.drag", Zt, Ln);
  "onselectstart" in t ? n.on("selectstart.drag", Zt, Ln) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function ql(e, t) {
  var n = e.document.documentElement, i = Be(e).on("dragstart.drag", null);
  t && (i.on("click.drag", Zt, Ln), setTimeout(function() {
    i.on("click.drag", null);
  }, 0)), "onselectstart" in n ? i.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const wi = (e) => () => e;
function Dr(e, {
  sourceEvent: t,
  subject: n,
  target: i,
  identifier: o,
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
    target: { value: i, enumerable: !0, configurable: !0 },
    identifier: { value: o, enumerable: !0, configurable: !0 },
    active: { value: s, enumerable: !0, configurable: !0 },
    x: { value: a, enumerable: !0, configurable: !0 },
    y: { value: c, enumerable: !0, configurable: !0 },
    dx: { value: u, enumerable: !0, configurable: !0 },
    dy: { value: l, enumerable: !0, configurable: !0 },
    _: { value: d }
  });
}
Dr.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function Vy(e) {
  return !e.ctrlKey && !e.button;
}
function Oy() {
  return this.parentNode;
}
function Hy(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function Wy() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Ul() {
  var e = Vy, t = Oy, n = Hy, i = Wy, o = {}, s = lo("start", "drag", "end"), a = 0, c, u, l, d, f = 0;
  function p(m) {
    m.on("mousedown.drag", h).filter(i).on("touchstart.drag", w).on("touchmove.drag", x, Ly).on("touchend.drag touchcancel.drag", b).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function h(m, j) {
    if (!(d || !e.call(this, m, j))) {
      var N = g(this, t.call(this, m, j), m, j, "mouse");
      N && (Be(m.view).on("mousemove.drag", y, Ln).on("mouseup.drag", v, Ln), Yl(m.view), ur(m), l = !1, c = m.clientX, u = m.clientY, N("start", m));
    }
  }
  function y(m) {
    if (Zt(m), !l) {
      var j = m.clientX - c, N = m.clientY - u;
      l = j * j + N * N > f;
    }
    o.mouse("drag", m);
  }
  function v(m) {
    Be(m.view).on("mousemove.drag mouseup.drag", null), ql(m.view, l), Zt(m), o.mouse("end", m);
  }
  function w(m, j) {
    if (e.call(this, m, j)) {
      var N = m.changedTouches, S = t.call(this, m, j), k = N.length, _, M;
      for (_ = 0; _ < k; ++_)
        (M = g(this, S, m, j, N[_].identifier, N[_])) && (ur(m), M("start", m, N[_]));
    }
  }
  function x(m) {
    var j = m.changedTouches, N = j.length, S, k;
    for (S = 0; S < N; ++S)
      (k = o[j[S].identifier]) && (Zt(m), k("drag", m, j[S]));
  }
  function b(m) {
    var j = m.changedTouches, N = j.length, S, k;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), S = 0; S < N; ++S)
      (k = o[j[S].identifier]) && (ur(m), k("end", m, j[S]));
  }
  function g(m, j, N, S, k, _) {
    var M = s.copy(), I = Ze(_ || N, j), $, L, C;
    if ((C = n.call(m, new Dr("beforestart", {
      sourceEvent: N,
      target: p,
      identifier: k,
      active: a,
      x: I[0],
      y: I[1],
      dx: 0,
      dy: 0,
      dispatch: M
    }), S)) != null)
      return $ = C.x - I[0] || 0, L = C.y - I[1] || 0, function A(E, D, P) {
        var T = I, B;
        switch (E) {
          case "start":
            o[k] = A, B = a++;
            break;
          case "end":
            delete o[k], --a;
          // falls through
          case "drag":
            I = Ze(P || D, j), B = a;
            break;
        }
        M.call(
          E,
          m,
          new Dr(E, {
            sourceEvent: D,
            subject: C,
            target: p,
            identifier: k,
            active: B,
            x: I[0] + $,
            y: I[1] + L,
            dx: I[0] - T[0],
            dy: I[1] - T[1],
            dispatch: M
          }),
          S
        );
      };
  }
  return p.filter = function(m) {
    return arguments.length ? (e = typeof m == "function" ? m : wi(!!m), p) : e;
  }, p.container = function(m) {
    return arguments.length ? (t = typeof m == "function" ? m : wi(m), p) : t;
  }, p.subject = function(m) {
    return arguments.length ? (n = typeof m == "function" ? m : wi(m), p) : n;
  }, p.touchable = function(m) {
    return arguments.length ? (i = typeof m == "function" ? m : wi(!!m), p) : i;
  }, p.on = function() {
    var m = s.on.apply(s, arguments);
    return m === s ? p : m;
  }, p.clickDistance = function(m) {
    return arguments.length ? (f = (m = +m) * m, p) : Math.sqrt(f);
  }, p;
}
function hs(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function Zl(e, t) {
  var n = Object.create(e.prototype);
  for (var i in t) n[i] = t[i];
  return n;
}
function Jn() {
}
var Vn = 0.7, Yi = 1 / Vn, Gt = "\\s*([+-]?\\d+)\\s*", On = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", nt = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", By = /^#([0-9a-f]{3,8})$/, Fy = new RegExp(`^rgb\\(${Gt},${Gt},${Gt}\\)$`), Ky = new RegExp(`^rgb\\(${nt},${nt},${nt}\\)$`), Xy = new RegExp(`^rgba\\(${Gt},${Gt},${Gt},${On}\\)$`), Yy = new RegExp(`^rgba\\(${nt},${nt},${nt},${On}\\)$`), qy = new RegExp(`^hsl\\(${On},${nt},${nt}\\)$`), Uy = new RegExp(`^hsla\\(${On},${nt},${nt},${On}\\)$`), ya = {
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
hs(Jn, At, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: ma,
  // Deprecated! Use color.formatHex.
  formatHex: ma,
  formatHex8: Zy,
  formatHsl: Gy,
  formatRgb: xa,
  toString: xa
});
function ma() {
  return this.rgb().formatHex();
}
function Zy() {
  return this.rgb().formatHex8();
}
function Gy() {
  return Gl(this).formatHsl();
}
function xa() {
  return this.rgb().formatRgb();
}
function At(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = By.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? wa(t) : n === 3 ? new Oe(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? vi(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? vi(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = Fy.exec(e)) ? new Oe(t[1], t[2], t[3], 1) : (t = Ky.exec(e)) ? new Oe(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = Xy.exec(e)) ? vi(t[1], t[2], t[3], t[4]) : (t = Yy.exec(e)) ? vi(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = qy.exec(e)) ? Na(t[1], t[2] / 100, t[3] / 100, 1) : (t = Uy.exec(e)) ? Na(t[1], t[2] / 100, t[3] / 100, t[4]) : ya.hasOwnProperty(e) ? wa(ya[e]) : e === "transparent" ? new Oe(NaN, NaN, NaN, 0) : null;
}
function wa(e) {
  return new Oe(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function vi(e, t, n, i) {
  return i <= 0 && (e = t = n = NaN), new Oe(e, t, n, i);
}
function Jy(e) {
  return e instanceof Jn || (e = At(e)), e ? (e = e.rgb(), new Oe(e.r, e.g, e.b, e.opacity)) : new Oe();
}
function Tr(e, t, n, i) {
  return arguments.length === 1 ? Jy(e) : new Oe(e, t, n, i ?? 1);
}
function Oe(e, t, n, i) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +i;
}
hs(Oe, Tr, Zl(Jn, {
  brighter(e) {
    return e = e == null ? Yi : Math.pow(Yi, e), new Oe(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Vn : Math.pow(Vn, e), new Oe(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new Oe(St(this.r), St(this.g), St(this.b), qi(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: va,
  // Deprecated! Use color.formatHex.
  formatHex: va,
  formatHex8: Qy,
  formatRgb: ba,
  toString: ba
}));
function va() {
  return `#${jt(this.r)}${jt(this.g)}${jt(this.b)}`;
}
function Qy() {
  return `#${jt(this.r)}${jt(this.g)}${jt(this.b)}${jt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function ba() {
  const e = qi(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${St(this.r)}, ${St(this.g)}, ${St(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function qi(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function St(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function jt(e) {
  return e = St(e), (e < 16 ? "0" : "") + e.toString(16);
}
function Na(e, t, n, i) {
  return i <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Ge(e, t, n, i);
}
function Gl(e) {
  if (e instanceof Ge) return new Ge(e.h, e.s, e.l, e.opacity);
  if (e instanceof Jn || (e = At(e)), !e) return new Ge();
  if (e instanceof Ge) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, i = e.b / 255, o = Math.min(t, n, i), s = Math.max(t, n, i), a = NaN, c = s - o, u = (s + o) / 2;
  return c ? (t === s ? a = (n - i) / c + (n < i) * 6 : n === s ? a = (i - t) / c + 2 : a = (t - n) / c + 4, c /= u < 0.5 ? s + o : 2 - s - o, a *= 60) : c = u > 0 && u < 1 ? 0 : a, new Ge(a, c, u, e.opacity);
}
function em(e, t, n, i) {
  return arguments.length === 1 ? Gl(e) : new Ge(e, t, n, i ?? 1);
}
function Ge(e, t, n, i) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +i;
}
hs(Ge, em, Zl(Jn, {
  brighter(e) {
    return e = e == null ? Yi : Math.pow(Yi, e), new Ge(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Vn : Math.pow(Vn, e), new Ge(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, i = n + (n < 0.5 ? n : 1 - n) * t, o = 2 * n - i;
    return new Oe(
      dr(e >= 240 ? e - 240 : e + 120, o, i),
      dr(e, o, i),
      dr(e < 120 ? e + 240 : e - 120, o, i),
      this.opacity
    );
  },
  clamp() {
    return new Ge(ja(this.h), bi(this.s), bi(this.l), qi(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = qi(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${ja(this.h)}, ${bi(this.s) * 100}%, ${bi(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function ja(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function bi(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function dr(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const gs = (e) => () => e;
function tm(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function nm(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(i) {
    return Math.pow(e + i * t, n);
  };
}
function im(e) {
  return (e = +e) == 1 ? Jl : function(t, n) {
    return n - t ? nm(t, n, e) : gs(isNaN(t) ? n : t);
  };
}
function Jl(e, t) {
  var n = t - e;
  return n ? tm(e, n) : gs(isNaN(e) ? t : e);
}
const Ui = (function e(t) {
  var n = im(t);
  function i(o, s) {
    var a = n((o = Tr(o)).r, (s = Tr(s)).r), c = n(o.g, s.g), u = n(o.b, s.b), l = Jl(o.opacity, s.opacity);
    return function(d) {
      return o.r = a(d), o.g = c(d), o.b = u(d), o.opacity = l(d), o + "";
    };
  }
  return i.gamma = e, i;
})(1);
function om(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, i = t.slice(), o;
  return function(s) {
    for (o = 0; o < n; ++o) i[o] = e[o] * (1 - s) + t[o] * s;
    return i;
  };
}
function rm(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function sm(e, t) {
  var n = t ? t.length : 0, i = e ? Math.min(n, e.length) : 0, o = new Array(i), s = new Array(n), a;
  for (a = 0; a < i; ++a) o[a] = An(e[a], t[a]);
  for (; a < n; ++a) s[a] = t[a];
  return function(c) {
    for (a = 0; a < i; ++a) s[a] = o[a](c);
    return s;
  };
}
function am(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(i) {
    return n.setTime(e * (1 - i) + t * i), n;
  };
}
function tt(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function cm(e, t) {
  var n = {}, i = {}, o;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (o in t)
    o in e ? n[o] = An(e[o], t[o]) : i[o] = t[o];
  return function(s) {
    for (o in n) i[o] = n[o](s);
    return i;
  };
}
var $r = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, fr = new RegExp($r.source, "g");
function lm(e) {
  return function() {
    return e;
  };
}
function um(e) {
  return function(t) {
    return e(t) + "";
  };
}
function Ql(e, t) {
  var n = $r.lastIndex = fr.lastIndex = 0, i, o, s, a = -1, c = [], u = [];
  for (e = e + "", t = t + ""; (i = $r.exec(e)) && (o = fr.exec(t)); )
    (s = o.index) > n && (s = t.slice(n, s), c[a] ? c[a] += s : c[++a] = s), (i = i[0]) === (o = o[0]) ? c[a] ? c[a] += o : c[++a] = o : (c[++a] = null, u.push({ i: a, x: tt(i, o) })), n = fr.lastIndex;
  return n < t.length && (s = t.slice(n), c[a] ? c[a] += s : c[++a] = s), c.length < 2 ? u[0] ? um(u[0].x) : lm(t) : (t = u.length, function(l) {
    for (var d = 0, f; d < t; ++d) c[(f = u[d]).i] = f.x(l);
    return c.join("");
  });
}
function An(e, t) {
  var n = typeof t, i;
  return t == null || n === "boolean" ? gs(t) : (n === "number" ? tt : n === "string" ? (i = At(t)) ? (t = i, Ui) : Ql : t instanceof At ? Ui : t instanceof Date ? am : rm(t) ? om : Array.isArray(t) ? sm : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? cm : tt)(e, t);
}
var Sa = 180 / Math.PI, Pr = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function eu(e, t, n, i, o, s) {
  var a, c, u;
  return (a = Math.sqrt(e * e + t * t)) && (e /= a, t /= a), (u = e * n + t * i) && (n -= e * u, i -= t * u), (c = Math.sqrt(n * n + i * i)) && (n /= c, i /= c, u /= c), e * i < t * n && (e = -e, t = -t, u = -u, a = -a), {
    translateX: o,
    translateY: s,
    rotate: Math.atan2(t, e) * Sa,
    skewX: Math.atan(u) * Sa,
    scaleX: a,
    scaleY: c
  };
}
var Ni;
function dm(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Pr : eu(t.a, t.b, t.c, t.d, t.e, t.f);
}
function fm(e) {
  return e == null || (Ni || (Ni = document.createElementNS("http://www.w3.org/2000/svg", "g")), Ni.setAttribute("transform", e), !(e = Ni.transform.baseVal.consolidate())) ? Pr : (e = e.matrix, eu(e.a, e.b, e.c, e.d, e.e, e.f));
}
function tu(e, t, n, i) {
  function o(l) {
    return l.length ? l.pop() + " " : "";
  }
  function s(l, d, f, p, h, y) {
    if (l !== f || d !== p) {
      var v = h.push("translate(", null, t, null, n);
      y.push({ i: v - 4, x: tt(l, f) }, { i: v - 2, x: tt(d, p) });
    } else (f || p) && h.push("translate(" + f + t + p + n);
  }
  function a(l, d, f, p) {
    l !== d ? (l - d > 180 ? d += 360 : d - l > 180 && (l += 360), p.push({ i: f.push(o(f) + "rotate(", null, i) - 2, x: tt(l, d) })) : d && f.push(o(f) + "rotate(" + d + i);
  }
  function c(l, d, f, p) {
    l !== d ? p.push({ i: f.push(o(f) + "skewX(", null, i) - 2, x: tt(l, d) }) : d && f.push(o(f) + "skewX(" + d + i);
  }
  function u(l, d, f, p, h, y) {
    if (l !== f || d !== p) {
      var v = h.push(o(h) + "scale(", null, ",", null, ")");
      y.push({ i: v - 4, x: tt(l, f) }, { i: v - 2, x: tt(d, p) });
    } else (f !== 1 || p !== 1) && h.push(o(h) + "scale(" + f + "," + p + ")");
  }
  return function(l, d) {
    var f = [], p = [];
    return l = e(l), d = e(d), s(l.translateX, l.translateY, d.translateX, d.translateY, f, p), a(l.rotate, d.rotate, f, p), c(l.skewX, d.skewX, f, p), u(l.scaleX, l.scaleY, d.scaleX, d.scaleY, f, p), l = d = null, function(h) {
      for (var y = -1, v = p.length, w; ++y < v; ) f[(w = p[y]).i] = w.x(h);
      return f.join("");
    };
  };
}
var pm = tu(dm, "px, ", "px)", "deg)"), hm = tu(fm, ", ", ")", ")"), gm = 1e-12;
function Ca(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function ym(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function mm(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const Pi = (function e(t, n, i) {
  function o(s, a) {
    var c = s[0], u = s[1], l = s[2], d = a[0], f = a[1], p = a[2], h = d - c, y = f - u, v = h * h + y * y, w, x;
    if (v < gm)
      x = Math.log(p / l) / t, w = function(S) {
        return [
          c + S * h,
          u + S * y,
          l * Math.exp(t * S * x)
        ];
      };
    else {
      var b = Math.sqrt(v), g = (p * p - l * l + i * v) / (2 * l * n * b), m = (p * p - l * l - i * v) / (2 * p * n * b), j = Math.log(Math.sqrt(g * g + 1) - g), N = Math.log(Math.sqrt(m * m + 1) - m);
      x = (N - j) / t, w = function(S) {
        var k = S * x, _ = Ca(j), M = l / (n * b) * (_ * mm(t * k + j) - ym(j));
        return [
          c + M * h,
          u + M * y,
          l * _ / Ca(t * k + j)
        ];
      };
    }
    return w.duration = x * 1e3 * t / Math.SQRT2, w;
  }
  return o.rho = function(s) {
    var a = Math.max(1e-3, +s), c = a * a, u = c * c;
    return e(a, c, u);
  }, o;
})(Math.SQRT2, 2, 4);
var tn = 0, jn = 0, xn = 0, nu = 1e3, Zi, Sn, Gi = 0, _t = 0, fo = 0, Hn = typeof performance == "object" && performance.now ? performance : Date, iu = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function ys() {
  return _t || (iu(xm), _t = Hn.now() + fo);
}
function xm() {
  _t = 0;
}
function Ji() {
  this._call = this._time = this._next = null;
}
Ji.prototype = ou.prototype = {
  constructor: Ji,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? ys() : +n) + (t == null ? 0 : +t), !this._next && Sn !== this && (Sn ? Sn._next = this : Zi = this, Sn = this), this._call = e, this._time = n, Mr();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Mr());
  }
};
function ou(e, t, n) {
  var i = new Ji();
  return i.restart(e, t, n), i;
}
function wm() {
  ys(), ++tn;
  for (var e = Zi, t; e; )
    (t = _t - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --tn;
}
function Ea() {
  _t = (Gi = Hn.now()) + fo, tn = jn = 0;
  try {
    wm();
  } finally {
    tn = 0, bm(), _t = 0;
  }
}
function vm() {
  var e = Hn.now(), t = e - Gi;
  t > nu && (fo -= t, Gi = e);
}
function bm() {
  for (var e, t = Zi, n, i = 1 / 0; t; )
    t._call ? (i > t._time && (i = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Zi = n);
  Sn = e, Mr(i);
}
function Mr(e) {
  if (!tn) {
    jn && (jn = clearTimeout(jn));
    var t = e - _t;
    t > 24 ? (e < 1 / 0 && (jn = setTimeout(Ea, e - Hn.now() - fo)), xn && (xn = clearInterval(xn))) : (xn || (Gi = Hn.now(), xn = setInterval(vm, nu)), tn = 1, iu(Ea));
  }
}
function ka(e, t, n) {
  var i = new Ji();
  return t = t == null ? 0 : +t, i.restart((o) => {
    i.stop(), e(o + t);
  }, t, n), i;
}
var Nm = lo("start", "end", "cancel", "interrupt"), jm = [], ru = 0, Ia = 1, Rr = 2, Mi = 3, Aa = 4, zr = 5, Ri = 6;
function po(e, t, n, i, o, s) {
  var a = e.__transition;
  if (!a) e.__transition = {};
  else if (n in a) return;
  Sm(e, n, {
    name: t,
    index: i,
    // For context during callback.
    group: o,
    // For context during callback.
    on: Nm,
    tween: jm,
    time: s.time,
    delay: s.delay,
    duration: s.duration,
    ease: s.ease,
    timer: null,
    state: ru
  });
}
function ms(e, t) {
  var n = et(e, t);
  if (n.state > ru) throw new Error("too late; already scheduled");
  return n;
}
function rt(e, t) {
  var n = et(e, t);
  if (n.state > Mi) throw new Error("too late; already running");
  return n;
}
function et(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function Sm(e, t, n) {
  var i = e.__transition, o;
  i[t] = n, n.timer = ou(s, 0, n.time);
  function s(l) {
    n.state = Ia, n.timer.restart(a, n.delay, n.time), n.delay <= l && a(l - n.delay);
  }
  function a(l) {
    var d, f, p, h;
    if (n.state !== Ia) return u();
    for (d in i)
      if (h = i[d], h.name === n.name) {
        if (h.state === Mi) return ka(a);
        h.state === Aa ? (h.state = Ri, h.timer.stop(), h.on.call("interrupt", e, e.__data__, h.index, h.group), delete i[d]) : +d < t && (h.state = Ri, h.timer.stop(), h.on.call("cancel", e, e.__data__, h.index, h.group), delete i[d]);
      }
    if (ka(function() {
      n.state === Mi && (n.state = Aa, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = Rr, n.on.call("start", e, e.__data__, n.index, n.group), n.state === Rr) {
      for (n.state = Mi, o = new Array(p = n.tween.length), d = 0, f = -1; d < p; ++d)
        (h = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (o[++f] = h);
      o.length = f + 1;
    }
  }
  function c(l) {
    for (var d = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(u), n.state = zr, 1), f = -1, p = o.length; ++f < p; )
      o[f].call(e, d);
    n.state === zr && (n.on.call("end", e, e.__data__, n.index, n.group), u());
  }
  function u() {
    n.state = Ri, n.timer.stop(), delete i[t];
    for (var l in i) return;
    delete e.__transition;
  }
}
function zi(e, t) {
  var n = e.__transition, i, o, s = !0, a;
  if (n) {
    t = t == null ? null : t + "";
    for (a in n) {
      if ((i = n[a]).name !== t) {
        s = !1;
        continue;
      }
      o = i.state > Rr && i.state < zr, i.state = Ri, i.timer.stop(), i.on.call(o ? "interrupt" : "cancel", e, e.__data__, i.index, i.group), delete n[a];
    }
    s && delete e.__transition;
  }
}
function Cm(e) {
  return this.each(function() {
    zi(this, e);
  });
}
function Em(e, t) {
  var n, i;
  return function() {
    var o = rt(this, e), s = o.tween;
    if (s !== n) {
      i = n = s;
      for (var a = 0, c = i.length; a < c; ++a)
        if (i[a].name === t) {
          i = i.slice(), i.splice(a, 1);
          break;
        }
    }
    o.tween = i;
  };
}
function km(e, t, n) {
  var i, o;
  if (typeof n != "function") throw new Error();
  return function() {
    var s = rt(this, e), a = s.tween;
    if (a !== i) {
      o = (i = a).slice();
      for (var c = { name: t, value: n }, u = 0, l = o.length; u < l; ++u)
        if (o[u].name === t) {
          o[u] = c;
          break;
        }
      u === l && o.push(c);
    }
    s.tween = o;
  };
}
function Im(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var i = et(this.node(), n).tween, o = 0, s = i.length, a; o < s; ++o)
      if ((a = i[o]).name === e)
        return a.value;
    return null;
  }
  return this.each((t == null ? Em : km)(n, e, t));
}
function xs(e, t, n) {
  var i = e._id;
  return e.each(function() {
    var o = rt(this, i);
    (o.value || (o.value = {}))[t] = n.apply(this, arguments);
  }), function(o) {
    return et(o, i).value[t];
  };
}
function su(e, t) {
  var n;
  return (typeof t == "number" ? tt : t instanceof At ? Ui : (n = At(t)) ? (t = n, Ui) : Ql)(e, t);
}
function Am(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function _m(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Dm(e, t, n) {
  var i, o = n + "", s;
  return function() {
    var a = this.getAttribute(e);
    return a === o ? null : a === i ? s : s = t(i = a, n);
  };
}
function Tm(e, t, n) {
  var i, o = n + "", s;
  return function() {
    var a = this.getAttributeNS(e.space, e.local);
    return a === o ? null : a === i ? s : s = t(i = a, n);
  };
}
function $m(e, t, n) {
  var i, o, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttribute(e) : (a = this.getAttribute(e), u = c + "", a === u ? null : a === i && u === o ? s : (o = u, s = t(i = a, c)));
  };
}
function Pm(e, t, n) {
  var i, o, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (a = this.getAttributeNS(e.space, e.local), u = c + "", a === u ? null : a === i && u === o ? s : (o = u, s = t(i = a, c)));
  };
}
function Mm(e, t) {
  var n = uo(e), i = n === "transform" ? hm : su;
  return this.attrTween(e, typeof t == "function" ? (n.local ? Pm : $m)(n, i, xs(this, "attr." + e, t)) : t == null ? (n.local ? _m : Am)(n) : (n.local ? Tm : Dm)(n, i, t));
}
function Rm(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function zm(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function Lm(e, t) {
  var n, i;
  function o() {
    var s = t.apply(this, arguments);
    return s !== i && (n = (i = s) && zm(e, s)), n;
  }
  return o._value = t, o;
}
function Vm(e, t) {
  var n, i;
  function o() {
    var s = t.apply(this, arguments);
    return s !== i && (n = (i = s) && Rm(e, s)), n;
  }
  return o._value = t, o;
}
function Om(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var i = uo(e);
  return this.tween(n, (i.local ? Lm : Vm)(i, t));
}
function Hm(e, t) {
  return function() {
    ms(this, e).delay = +t.apply(this, arguments);
  };
}
function Wm(e, t) {
  return t = +t, function() {
    ms(this, e).delay = t;
  };
}
function Bm(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Hm : Wm)(t, e)) : et(this.node(), t).delay;
}
function Fm(e, t) {
  return function() {
    rt(this, e).duration = +t.apply(this, arguments);
  };
}
function Km(e, t) {
  return t = +t, function() {
    rt(this, e).duration = t;
  };
}
function Xm(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Fm : Km)(t, e)) : et(this.node(), t).duration;
}
function Ym(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    rt(this, e).ease = t;
  };
}
function qm(e) {
  var t = this._id;
  return arguments.length ? this.each(Ym(t, e)) : et(this.node(), t).ease;
}
function Um(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    rt(this, e).ease = n;
  };
}
function Zm(e) {
  if (typeof e != "function") throw new Error();
  return this.each(Um(this._id, e));
}
function Gm(e) {
  typeof e != "function" && (e = zl(e));
  for (var t = this._groups, n = t.length, i = new Array(n), o = 0; o < n; ++o)
    for (var s = t[o], a = s.length, c = i[o] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new dt(i, this._parents, this._name, this._id);
}
function Jm(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, i = t.length, o = n.length, s = Math.min(i, o), a = new Array(i), c = 0; c < s; ++c)
    for (var u = t[c], l = n[c], d = u.length, f = a[c] = new Array(d), p, h = 0; h < d; ++h)
      (p = u[h] || l[h]) && (f[h] = p);
  for (; c < i; ++c)
    a[c] = t[c];
  return new dt(a, this._parents, this._name, this._id);
}
function Qm(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function ex(e, t, n) {
  var i, o, s = Qm(t) ? ms : rt;
  return function() {
    var a = s(this, e), c = a.on;
    c !== i && (o = (i = c).copy()).on(t, n), a.on = o;
  };
}
function tx(e, t) {
  var n = this._id;
  return arguments.length < 2 ? et(this.node(), n).on.on(e) : this.each(ex(n, e, t));
}
function nx(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function ix() {
  return this.on("end.remove", nx(this._id));
}
function ox(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = fs(e));
  for (var i = this._groups, o = i.length, s = new Array(o), a = 0; a < o; ++a)
    for (var c = i[a], u = c.length, l = s[a] = new Array(u), d, f, p = 0; p < u; ++p)
      (d = c[p]) && (f = e.call(d, d.__data__, p, c)) && ("__data__" in d && (f.__data__ = d.__data__), l[p] = f, po(l[p], t, n, p, l, et(d, n)));
  return new dt(s, this._parents, t, n);
}
function rx(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Rl(e));
  for (var i = this._groups, o = i.length, s = [], a = [], c = 0; c < o; ++c)
    for (var u = i[c], l = u.length, d, f = 0; f < l; ++f)
      if (d = u[f]) {
        for (var p = e.call(d, d.__data__, f, u), h, y = et(d, n), v = 0, w = p.length; v < w; ++v)
          (h = p[v]) && po(h, t, n, v, p, y);
        s.push(p), a.push(d);
      }
  return new dt(s, a, t, n);
}
var sx = Gn.prototype.constructor;
function ax() {
  return new sx(this._groups, this._parents);
}
function cx(e, t) {
  var n, i, o;
  return function() {
    var s = en(this, e), a = (this.style.removeProperty(e), en(this, e));
    return s === a ? null : s === n && a === i ? o : o = t(n = s, i = a);
  };
}
function au(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function lx(e, t, n) {
  var i, o = n + "", s;
  return function() {
    var a = en(this, e);
    return a === o ? null : a === i ? s : s = t(i = a, n);
  };
}
function ux(e, t, n) {
  var i, o, s;
  return function() {
    var a = en(this, e), c = n(this), u = c + "";
    return c == null && (u = c = (this.style.removeProperty(e), en(this, e))), a === u ? null : a === i && u === o ? s : (o = u, s = t(i = a, c));
  };
}
function dx(e, t) {
  var n, i, o, s = "style." + t, a = "end." + s, c;
  return function() {
    var u = rt(this, e), l = u.on, d = u.value[s] == null ? c || (c = au(t)) : void 0;
    (l !== n || o !== d) && (i = (n = l).copy()).on(a, o = d), u.on = i;
  };
}
function fx(e, t, n) {
  var i = (e += "") == "transform" ? pm : su;
  return t == null ? this.styleTween(e, cx(e, i)).on("end.style." + e, au(e)) : typeof t == "function" ? this.styleTween(e, ux(e, i, xs(this, "style." + e, t))).each(dx(this._id, e)) : this.styleTween(e, lx(e, i, t), n).on("end.style." + e, null);
}
function px(e, t, n) {
  return function(i) {
    this.style.setProperty(e, t.call(this, i), n);
  };
}
function hx(e, t, n) {
  var i, o;
  function s() {
    var a = t.apply(this, arguments);
    return a !== o && (i = (o = a) && px(e, a, n)), i;
  }
  return s._value = t, s;
}
function gx(e, t, n) {
  var i = "style." + (e += "");
  if (arguments.length < 2) return (i = this.tween(i)) && i._value;
  if (t == null) return this.tween(i, null);
  if (typeof t != "function") throw new Error();
  return this.tween(i, hx(e, t, n ?? ""));
}
function yx(e) {
  return function() {
    this.textContent = e;
  };
}
function mx(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function xx(e) {
  return this.tween("text", typeof e == "function" ? mx(xs(this, "text", e)) : yx(e == null ? "" : e + ""));
}
function wx(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function vx(e) {
  var t, n;
  function i() {
    var o = e.apply(this, arguments);
    return o !== n && (t = (n = o) && wx(o)), t;
  }
  return i._value = e, i;
}
function bx(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, vx(e));
}
function Nx() {
  for (var e = this._name, t = this._id, n = cu(), i = this._groups, o = i.length, s = 0; s < o; ++s)
    for (var a = i[s], c = a.length, u, l = 0; l < c; ++l)
      if (u = a[l]) {
        var d = et(u, t);
        po(u, e, n, l, a, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new dt(i, this._parents, e, n);
}
function jx() {
  var e, t, n = this, i = n._id, o = n.size();
  return new Promise(function(s, a) {
    var c = { value: a }, u = { value: function() {
      --o === 0 && s();
    } };
    n.each(function() {
      var l = rt(this, i), d = l.on;
      d !== e && (t = (e = d).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(u)), l.on = t;
    }), o === 0 && s();
  });
}
var Sx = 0;
function dt(e, t, n, i) {
  this._groups = e, this._parents = t, this._name = n, this._id = i;
}
function cu() {
  return ++Sx;
}
var at = Gn.prototype;
dt.prototype = {
  constructor: dt,
  select: ox,
  selectAll: rx,
  selectChild: at.selectChild,
  selectChildren: at.selectChildren,
  filter: Gm,
  merge: Jm,
  selection: ax,
  transition: Nx,
  call: at.call,
  nodes: at.nodes,
  node: at.node,
  size: at.size,
  empty: at.empty,
  each: at.each,
  on: tx,
  attr: Mm,
  attrTween: Om,
  style: fx,
  styleTween: gx,
  text: xx,
  textTween: bx,
  remove: ix,
  tween: Im,
  delay: Bm,
  duration: Xm,
  ease: qm,
  easeVarying: Zm,
  end: jx,
  [Symbol.iterator]: at[Symbol.iterator]
};
function Cx(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var Ex = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: Cx
};
function kx(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function Ix(e) {
  var t, n;
  e instanceof dt ? (t = e._id, e = e._name) : (t = cu(), (n = Ex).time = ys(), e = e == null ? null : e + "");
  for (var i = this._groups, o = i.length, s = 0; s < o; ++s)
    for (var a = i[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && po(u, e, t, l, a, n || kx(u, t));
  return new dt(i, this._parents, e, t);
}
Gn.prototype.interrupt = Cm;
Gn.prototype.transition = Ix;
const ji = (e) => () => e;
function Ax(e, {
  sourceEvent: t,
  target: n,
  transform: i,
  dispatch: o
}) {
  Object.defineProperties(this, {
    type: { value: e, enumerable: !0, configurable: !0 },
    sourceEvent: { value: t, enumerable: !0, configurable: !0 },
    target: { value: n, enumerable: !0, configurable: !0 },
    transform: { value: i, enumerable: !0, configurable: !0 },
    _: { value: o }
  });
}
function lt(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
lt.prototype = {
  constructor: lt,
  scale: function(e) {
    return e === 1 ? this : new lt(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new lt(this.k, this.x + this.k * e, this.y + this.k * t);
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
var ho = new lt(1, 0, 0);
lu.prototype = lt.prototype;
function lu(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return ho;
  return e.__zoom;
}
function pr(e) {
  e.stopImmediatePropagation();
}
function wn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function _x(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function Dx() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function _a() {
  return this.__zoom || ho;
}
function Tx(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function $x() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Px(e, t, n) {
  var i = e.invertX(t[0][0]) - n[0][0], o = e.invertX(t[1][0]) - n[1][0], s = e.invertY(t[0][1]) - n[0][1], a = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    o > i ? (i + o) / 2 : Math.min(0, i) || Math.max(0, o),
    a > s ? (s + a) / 2 : Math.min(0, s) || Math.max(0, a)
  );
}
function uu() {
  var e = _x, t = Dx, n = Px, i = Tx, o = $x, s = [0, 1 / 0], a = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, u = Pi, l = lo("start", "zoom", "end"), d, f, p, h = 500, y = 150, v = 0, w = 10;
  function x(C) {
    C.property("__zoom", _a).on("wheel.zoom", k, { passive: !1 }).on("mousedown.zoom", _).on("dblclick.zoom", M).filter(o).on("touchstart.zoom", I).on("touchmove.zoom", $).on("touchend.zoom touchcancel.zoom", L).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  x.transform = function(C, A, E, D) {
    var P = C.selection ? C.selection() : C;
    P.property("__zoom", _a), C !== P ? j(C, A, E, D) : P.interrupt().each(function() {
      N(this, arguments).event(D).start().zoom(null, typeof A == "function" ? A.apply(this, arguments) : A).end();
    });
  }, x.scaleBy = function(C, A, E, D) {
    x.scaleTo(C, function() {
      var P = this.__zoom.k, T = typeof A == "function" ? A.apply(this, arguments) : A;
      return P * T;
    }, E, D);
  }, x.scaleTo = function(C, A, E, D) {
    x.transform(C, function() {
      var P = t.apply(this, arguments), T = this.__zoom, B = E == null ? m(P) : typeof E == "function" ? E.apply(this, arguments) : E, W = T.invert(B), H = typeof A == "function" ? A.apply(this, arguments) : A;
      return n(g(b(T, H), B, W), P, a);
    }, E, D);
  }, x.translateBy = function(C, A, E, D) {
    x.transform(C, function() {
      return n(this.__zoom.translate(
        typeof A == "function" ? A.apply(this, arguments) : A,
        typeof E == "function" ? E.apply(this, arguments) : E
      ), t.apply(this, arguments), a);
    }, null, D);
  }, x.translateTo = function(C, A, E, D, P) {
    x.transform(C, function() {
      var T = t.apply(this, arguments), B = this.__zoom, W = D == null ? m(T) : typeof D == "function" ? D.apply(this, arguments) : D;
      return n(ho.translate(W[0], W[1]).scale(B.k).translate(
        typeof A == "function" ? -A.apply(this, arguments) : -A,
        typeof E == "function" ? -E.apply(this, arguments) : -E
      ), T, a);
    }, D, P);
  };
  function b(C, A) {
    return A = Math.max(s[0], Math.min(s[1], A)), A === C.k ? C : new lt(A, C.x, C.y);
  }
  function g(C, A, E) {
    var D = A[0] - E[0] * C.k, P = A[1] - E[1] * C.k;
    return D === C.x && P === C.y ? C : new lt(C.k, D, P);
  }
  function m(C) {
    return [(+C[0][0] + +C[1][0]) / 2, (+C[0][1] + +C[1][1]) / 2];
  }
  function j(C, A, E, D) {
    C.on("start.zoom", function() {
      N(this, arguments).event(D).start();
    }).on("interrupt.zoom end.zoom", function() {
      N(this, arguments).event(D).end();
    }).tween("zoom", function() {
      var P = this, T = arguments, B = N(P, T).event(D), W = t.apply(P, T), H = E == null ? m(W) : typeof E == "function" ? E.apply(P, T) : E, q = Math.max(W[1][0] - W[0][0], W[1][1] - W[0][1]), V = P.__zoom, Y = typeof A == "function" ? A.apply(P, T) : A, J = u(V.invert(H).concat(q / V.k), Y.invert(H).concat(q / Y.k));
      return function(Z) {
        if (Z === 1) Z = Y;
        else {
          var R = J(Z), X = q / R[2];
          Z = new lt(X, H[0] - R[0] * X, H[1] - R[1] * X);
        }
        B.zoom(null, Z);
      };
    });
  }
  function N(C, A, E) {
    return !E && C.__zooming || new S(C, A);
  }
  function S(C, A) {
    this.that = C, this.args = A, this.active = 0, this.sourceEvent = null, this.extent = t.apply(C, A), this.taps = 0;
  }
  S.prototype = {
    event: function(C) {
      return C && (this.sourceEvent = C), this;
    },
    start: function() {
      return ++this.active === 1 && (this.that.__zooming = this, this.emit("start")), this;
    },
    zoom: function(C, A) {
      return this.mouse && C !== "mouse" && (this.mouse[1] = A.invert(this.mouse[0])), this.touch0 && C !== "touch" && (this.touch0[1] = A.invert(this.touch0[0])), this.touch1 && C !== "touch" && (this.touch1[1] = A.invert(this.touch1[0])), this.that.__zoom = A, this.emit("zoom"), this;
    },
    end: function() {
      return --this.active === 0 && (delete this.that.__zooming, this.emit("end")), this;
    },
    emit: function(C) {
      var A = Be(this.that).datum();
      l.call(
        C,
        this.that,
        new Ax(C, {
          sourceEvent: this.sourceEvent,
          target: x,
          transform: this.that.__zoom,
          dispatch: l
        }),
        A
      );
    }
  };
  function k(C, ...A) {
    if (!e.apply(this, arguments)) return;
    var E = N(this, A).event(C), D = this.__zoom, P = Math.max(s[0], Math.min(s[1], D.k * Math.pow(2, i.apply(this, arguments)))), T = Ze(C);
    if (E.wheel)
      (E.mouse[0][0] !== T[0] || E.mouse[0][1] !== T[1]) && (E.mouse[1] = D.invert(E.mouse[0] = T)), clearTimeout(E.wheel);
    else {
      if (D.k === P) return;
      E.mouse = [T, D.invert(T)], zi(this), E.start();
    }
    wn(C), E.wheel = setTimeout(B, y), E.zoom("mouse", n(g(b(D, P), E.mouse[0], E.mouse[1]), E.extent, a));
    function B() {
      E.wheel = null, E.end();
    }
  }
  function _(C, ...A) {
    if (p || !e.apply(this, arguments)) return;
    var E = C.currentTarget, D = N(this, A, !0).event(C), P = Be(C.view).on("mousemove.zoom", H, !0).on("mouseup.zoom", q, !0), T = Ze(C, E), B = C.clientX, W = C.clientY;
    Yl(C.view), pr(C), D.mouse = [T, this.__zoom.invert(T)], zi(this), D.start();
    function H(V) {
      if (wn(V), !D.moved) {
        var Y = V.clientX - B, J = V.clientY - W;
        D.moved = Y * Y + J * J > v;
      }
      D.event(V).zoom("mouse", n(g(D.that.__zoom, D.mouse[0] = Ze(V, E), D.mouse[1]), D.extent, a));
    }
    function q(V) {
      P.on("mousemove.zoom mouseup.zoom", null), ql(V.view, D.moved), wn(V), D.event(V).end();
    }
  }
  function M(C, ...A) {
    if (e.apply(this, arguments)) {
      var E = this.__zoom, D = Ze(C.changedTouches ? C.changedTouches[0] : C, this), P = E.invert(D), T = E.k * (C.shiftKey ? 0.5 : 2), B = n(g(b(E, T), D, P), t.apply(this, A), a);
      wn(C), c > 0 ? Be(this).transition().duration(c).call(j, B, D, C) : Be(this).call(x.transform, B, D, C);
    }
  }
  function I(C, ...A) {
    if (e.apply(this, arguments)) {
      var E = C.touches, D = E.length, P = N(this, A, C.changedTouches.length === D).event(C), T, B, W, H;
      for (pr(C), B = 0; B < D; ++B)
        W = E[B], H = Ze(W, this), H = [H, this.__zoom.invert(H), W.identifier], P.touch0 ? !P.touch1 && P.touch0[2] !== H[2] && (P.touch1 = H, P.taps = 0) : (P.touch0 = H, T = !0, P.taps = 1 + !!d);
      d && (d = clearTimeout(d)), T && (P.taps < 2 && (f = H[0], d = setTimeout(function() {
        d = null;
      }, h)), zi(this), P.start());
    }
  }
  function $(C, ...A) {
    if (this.__zooming) {
      var E = N(this, A).event(C), D = C.changedTouches, P = D.length, T, B, W, H;
      for (wn(C), T = 0; T < P; ++T)
        B = D[T], W = Ze(B, this), E.touch0 && E.touch0[2] === B.identifier ? E.touch0[0] = W : E.touch1 && E.touch1[2] === B.identifier && (E.touch1[0] = W);
      if (B = E.that.__zoom, E.touch1) {
        var q = E.touch0[0], V = E.touch0[1], Y = E.touch1[0], J = E.touch1[1], Z = (Z = Y[0] - q[0]) * Z + (Z = Y[1] - q[1]) * Z, R = (R = J[0] - V[0]) * R + (R = J[1] - V[1]) * R;
        B = b(B, Math.sqrt(Z / R)), W = [(q[0] + Y[0]) / 2, (q[1] + Y[1]) / 2], H = [(V[0] + J[0]) / 2, (V[1] + J[1]) / 2];
      } else if (E.touch0) W = E.touch0[0], H = E.touch0[1];
      else return;
      E.zoom("touch", n(g(B, W, H), E.extent, a));
    }
  }
  function L(C, ...A) {
    if (this.__zooming) {
      var E = N(this, A).event(C), D = C.changedTouches, P = D.length, T, B;
      for (pr(C), p && clearTimeout(p), p = setTimeout(function() {
        p = null;
      }, h), T = 0; T < P; ++T)
        B = D[T], E.touch0 && E.touch0[2] === B.identifier ? delete E.touch0 : E.touch1 && E.touch1[2] === B.identifier && delete E.touch1;
      if (E.touch1 && !E.touch0 && (E.touch0 = E.touch1, delete E.touch1), E.touch0) E.touch0[1] = this.__zoom.invert(E.touch0[0]);
      else if (E.end(), E.taps === 2 && (B = Ze(B, this), Math.hypot(f[0] - B[0], f[1] - B[1]) < w)) {
        var W = Be(this).on("dblclick.zoom");
        W && W.apply(this, arguments);
      }
    }
  }
  return x.wheelDelta = function(C) {
    return arguments.length ? (i = typeof C == "function" ? C : ji(+C), x) : i;
  }, x.filter = function(C) {
    return arguments.length ? (e = typeof C == "function" ? C : ji(!!C), x) : e;
  }, x.touchable = function(C) {
    return arguments.length ? (o = typeof C == "function" ? C : ji(!!C), x) : o;
  }, x.extent = function(C) {
    return arguments.length ? (t = typeof C == "function" ? C : ji([[+C[0][0], +C[0][1]], [+C[1][0], +C[1][1]]]), x) : t;
  }, x.scaleExtent = function(C) {
    return arguments.length ? (s[0] = +C[0], s[1] = +C[1], x) : [s[0], s[1]];
  }, x.translateExtent = function(C) {
    return arguments.length ? (a[0][0] = +C[0][0], a[1][0] = +C[1][0], a[0][1] = +C[0][1], a[1][1] = +C[1][1], x) : [[a[0][0], a[0][1]], [a[1][0], a[1][1]]];
  }, x.constrain = function(C) {
    return arguments.length ? (n = C, x) : n;
  }, x.duration = function(C) {
    return arguments.length ? (c = +C, x) : c;
  }, x.interpolate = function(C) {
    return arguments.length ? (u = C, x) : u;
  }, x.on = function() {
    var C = l.on.apply(l, arguments);
    return C === l ? x : C;
  }, x.clickDistance = function(C) {
    return arguments.length ? (v = (C = +C) * C, x) : Math.sqrt(v);
  }, x.tapDistance = function(C) {
    return arguments.length ? (w = +C, x) : w;
  }, x;
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
  error008: (e, { id: t, sourceHandle: n, targetHandle: i }) => `Couldn't create edge for ${e} handle id: "${e === "source" ? n : i}", edge id: ${t}.`,
  error010: () => "Handle: No node id found. Make sure to only use a Handle inside a custom Node.",
  error011: (e) => `Edge type "${e}" not found. Using fallback type "default".`,
  error012: (e) => `Node with id "${e}" does not exist, it may have been removed. This can happen when a node is deleted before the "onNodeClick" handler is called.`,
  error013: (e = "react") => `It seems that you haven't loaded the styles. Please import '@xyflow/${e}/dist/style.css' or base.css to make sure everything is working properly.`,
  error014: () => "useNodeConnections: No node ID found. Call useNodeConnections inside a custom Node or provide a node ID.",
  error015: () => "It seems that you are trying to drag a node that is not initialized. Please use onNodesChange as explained in the docs.",
  error016: (e) => `Edge with id "${e}" does not exist, it may have been removed. This can happen when an edge is deleted before the "onEdgeClick" handler is called.`
}, Wn = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], du = ["Enter", " ", "Escape"], fu = {
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
var nn;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(nn || (nn = {}));
var Ct;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(Ct || (Ct = {}));
var Bn;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Bn || (Bn = {}));
const pu = {
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
var yt;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(yt || (yt = {}));
var Qi;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(Qi || (Qi = {}));
var re;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(re || (re = {}));
const Da = {
  [re.Left]: re.Right,
  [re.Right]: re.Left,
  [re.Top]: re.Bottom,
  [re.Bottom]: re.Top
};
function hu(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const gu = (e) => "id" in e && "source" in e && "target" in e, Mx = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), ws = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), Qn = (e, t = [0, 0]) => {
  const { width: n, height: i } = ft(e), o = e.origin ?? t, s = n * o[0], a = i * o[1];
  return {
    x: e.position.x - s,
    y: e.position.y - a
  };
}, Rx = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((i, o) => {
    const s = typeof o == "string";
    let a = !t.nodeLookup && !s ? o : void 0;
    t.nodeLookup && (a = s ? t.nodeLookup.get(o) : ws(o) ? o : t.nodeLookup.get(o.id));
    const c = a ? eo(a, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return go(i, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return yo(n);
}, ei = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, i = !1;
  return e.forEach((o) => {
    (t.filter === void 0 || t.filter(o)) && (n = go(n, eo(o)), i = !0);
  }), i ? yo(n) : { x: 0, y: 0, width: 0, height: 0 };
}, vs = (e, t, [n, i, o] = [0, 0, 1], s = !1, a = !1) => {
  const c = {
    ...pn(t, [n, i, o]),
    width: t.width / o,
    height: t.height / o
  }, u = [];
  for (const l of e.values()) {
    const { measured: d, selectable: f = !0, hidden: p = !1 } = l;
    if (a && !f || p)
      continue;
    const h = d.width ?? l.width ?? l.initialWidth ?? null, y = d.height ?? l.height ?? l.initialHeight ?? null, v = Fn(c, rn(l)), w = (h ?? 0) * (y ?? 0), x = s && v > 0;
    (!l.internals.handleBounds || x || v >= w || l.dragging) && u.push(l);
  }
  return u;
}, zx = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((i) => {
    n.add(i.id);
  }), t.filter((i) => n.has(i.source) || n.has(i.target));
};
function Lx(e, t) {
  const n = /* @__PURE__ */ new Map(), i = t?.nodes ? new Set(t.nodes.map((o) => o.id)) : null;
  return e.forEach((o) => {
    o.measured.width && o.measured.height && (t?.includeHiddenNodes || !o.hidden) && (!i || i.has(o.id)) && n.set(o.id, o);
  }), n;
}
async function Vx({ nodes: e, width: t, height: n, panZoom: i, minZoom: o, maxZoom: s }, a) {
  if (e.size === 0)
    return !0;
  const c = Lx(e, a), u = ei(c), l = Ns(u, t, n, a?.minZoom ?? o, a?.maxZoom ?? s, a?.padding ?? 0.1);
  return await i.setViewport(l, {
    duration: a?.duration,
    ease: a?.ease,
    interpolate: a?.interpolate
  }), !0;
}
function yu({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: i = [0, 0], nodeExtent: o, onError: s }) {
  const a = n.get(e), c = a.parentId ? n.get(a.parentId) : void 0, { x: u, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, d = a.origin ?? i;
  let f = a.extent || o;
  if (a.extent === "parent" && !a.expandParent)
    if (!c)
      s?.("005", Ye.error005());
    else {
      const h = c.measured.width, y = c.measured.height;
      h && y && (f = [
        [u, l],
        [u + h, l + y]
      ]);
    }
  else c && Tt(a.extent) && (f = [
    [a.extent[0][0] + u, a.extent[0][1] + l],
    [a.extent[1][0] + u, a.extent[1][1] + l]
  ]);
  const p = Tt(f) ? Dt(t, f, a.measured) : t;
  return (a.measured.width === void 0 || a.measured.height === void 0) && s?.("015", Ye.error015()), {
    position: {
      x: p.x - u + (a.measured.width ?? 0) * d[0],
      y: p.y - l + (a.measured.height ?? 0) * d[1]
    },
    positionAbsolute: p
  };
}
async function Ox({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: i, onBeforeDelete: o }) {
  const s = new Set(e.map((p) => p.id)), a = [];
  for (const p of n) {
    if (p.deletable === !1)
      continue;
    const h = s.has(p.id), y = !h && p.parentId && a.find((v) => v.id === p.parentId);
    (h || y) && a.push(p);
  }
  const c = new Set(t.map((p) => p.id)), u = i.filter((p) => p.deletable !== !1), d = zx(a, u);
  for (const p of u)
    c.has(p.id) && !d.find((y) => y.id === p.id) && d.push(p);
  if (!o)
    return {
      edges: d,
      nodes: a
    };
  const f = await o({
    nodes: a,
    edges: d
  });
  return typeof f == "boolean" ? f ? { edges: d, nodes: a } : { edges: [], nodes: [] } : f;
}
const on = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), Dt = (e = { x: 0, y: 0 }, t, n) => ({
  x: on(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: on(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function mu(e, t, n) {
  const { width: i, height: o } = ft(n), { x: s, y: a } = n.internals.positionAbsolute;
  return Dt(e, [
    [s, a],
    [s + i, a + o]
  ], t);
}
const Ta = (e, t, n) => e < t ? on(Math.abs(e - t), 1, t) / t : e > n ? -on(Math.abs(e - n), 1, t) / t : 0, bs = (e, t, n = 15, i = 40) => {
  const o = Ta(e.x, i, t.width - i) * n, s = Ta(e.y, i, t.height - i) * n;
  return [o, s];
}, go = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), Lr = ({ x: e, y: t, width: n, height: i }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + i
}), yo = ({ x: e, y: t, x2: n, y2: i }) => ({
  x: e,
  y: t,
  width: n - e,
  height: i - t
}), rn = (e, t = [0, 0]) => {
  const { x: n, y: i } = ws(e) ? e.internals.positionAbsolute : Qn(e, t);
  return {
    x: n,
    y: i,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, eo = (e, t = [0, 0]) => {
  const { x: n, y: i } = ws(e) ? e.internals.positionAbsolute : Qn(e, t);
  return {
    x: n,
    y: i,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: i + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, xu = (e, t) => yo(go(Lr(e), Lr(t))), Fn = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), i = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * i);
}, $a = (e) => Je(e.width) && Je(e.height) && Je(e.x) && Je(e.y), Je = (e) => !isNaN(e) && isFinite(e), wu = (e, t) => (n, i) => {
}, ti = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), pn = ({ x: e, y: t }, [n, i, o], s = !1, a = [1, 1]) => {
  const c = {
    x: (e - n) / o,
    y: (t - i) / o
  };
  return s ? ti(c, a) : c;
}, sn = ({ x: e, y: t }, [n, i, o]) => ({
  x: e * o + n,
  y: t * o + i
});
function Bt(e, t) {
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
function Hx(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const i = Bt(e, n), o = Bt(e, t);
    return {
      top: i,
      right: o,
      bottom: i,
      left: o,
      x: o * 2,
      y: i * 2
    };
  }
  if (typeof e == "object") {
    const i = Bt(e.top ?? e.y ?? 0, n), o = Bt(e.bottom ?? e.y ?? 0, n), s = Bt(e.left ?? e.x ?? 0, t), a = Bt(e.right ?? e.x ?? 0, t);
    return { top: i, right: a, bottom: o, left: s, x: s + a, y: i + o };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function Wx(e, t, n, i, o, s) {
  const { x: a, y: c } = sn(e, [t, n, i]), { x: u, y: l } = sn({ x: e.x + e.width, y: e.y + e.height }, [t, n, i]), d = o - u, f = s - l;
  return {
    left: Math.floor(a),
    top: Math.floor(c),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const Ns = (e, t, n, i, o, s) => {
  const a = Hx(s, t, n), c = (t - a.x) / e.width, u = (n - a.y) / e.height, l = Math.min(c, u), d = on(l, i, o), f = e.x + e.width / 2, p = e.y + e.height / 2, h = t / 2 - f * d, y = n / 2 - p * d, v = Wx(e, h, y, d, t, n), w = {
    left: Math.min(v.left - a.left, 0),
    top: Math.min(v.top - a.top, 0),
    right: Math.min(v.right - a.right, 0),
    bottom: Math.min(v.bottom - a.bottom, 0)
  };
  return {
    x: h - w.left + w.right,
    y: y - w.top + w.bottom,
    zoom: d
  };
}, Kn = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function Tt(e) {
  return e != null && e !== "parent";
}
function ft(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function vu(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function bu(e, t = { width: 0, height: 0 }, n, i, o) {
  const s = { ...e }, a = i.get(n);
  if (a) {
    const c = a.origin || o;
    s.x += a.internals.positionAbsolute.x - (t.width ?? 0) * c[0], s.y += a.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return s;
}
function Pa(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function Bx() {
  let e, t;
  return { promise: new Promise((i, o) => {
    e = i, t = o;
  }), resolve: e, reject: t };
}
function Fx(e) {
  return { ...fu, ...e || {} };
}
function _n(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: i, containerBounds: o }) {
  const { x: s, y: a } = Qe(e), c = pn({ x: s - (o?.left ?? 0), y: a - (o?.top ?? 0) }, i), { x: u, y: l } = n ? ti(c, t) : c;
  return {
    xSnapped: u,
    ySnapped: l,
    ...c
  };
}
const js = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), Nu = (e) => e?.getRootNode?.() || window?.document, Kx = ["INPUT", "SELECT", "TEXTAREA"];
function ju(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : Kx.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const Su = (e) => "clientX" in e, Qe = (e, t) => {
  const n = Su(e), i = n ? e.clientX : e.touches?.[0].clientX, o = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: i - (t?.left ?? 0),
    y: o - (t?.top ?? 0)
  };
}, Ma = (e, t, n, i, o) => {
  const s = t.querySelectorAll(`.${e}`);
  return !s || !s.length ? null : Array.from(s).map((a) => {
    const c = a.getBoundingClientRect();
    return {
      id: a.getAttribute("data-handleid"),
      type: e,
      nodeId: o,
      position: a.getAttribute("data-handlepos"),
      x: (c.left - n.left) / i,
      y: (c.top - n.top) / i,
      ...js(a)
    };
  });
};
function Cu({ sourceX: e, sourceY: t, targetX: n, targetY: i, sourceControlX: o, sourceControlY: s, targetControlX: a, targetControlY: c }) {
  const u = e * 0.125 + o * 0.375 + a * 0.375 + n * 0.125, l = t * 0.125 + s * 0.375 + c * 0.375 + i * 0.125, d = Math.abs(u - e), f = Math.abs(l - t);
  return [u, l, d, f];
}
function Si(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function Ra({ pos: e, x1: t, y1: n, x2: i, y2: o, c: s }) {
  switch (e) {
    case re.Left:
      return [t - Si(t - i, s), n];
    case re.Right:
      return [t + Si(i - t, s), n];
    case re.Top:
      return [t, n - Si(n - o, s)];
    case re.Bottom:
      return [t, n + Si(o - n, s)];
  }
}
function Eu({ sourceX: e, sourceY: t, sourcePosition: n = re.Bottom, targetX: i, targetY: o, targetPosition: s = re.Top, curvature: a = 0.25 }) {
  const [c, u] = Ra({
    pos: n,
    x1: e,
    y1: t,
    x2: i,
    y2: o,
    c: a
  }), [l, d] = Ra({
    pos: s,
    x1: i,
    y1: o,
    x2: e,
    y2: t,
    c: a
  }), [f, p, h, y] = Cu({
    sourceX: e,
    sourceY: t,
    targetX: i,
    targetY: o,
    sourceControlX: c,
    sourceControlY: u,
    targetControlX: l,
    targetControlY: d
  });
  return [
    `M${e},${t} C${c},${u} ${l},${d} ${i},${o}`,
    f,
    p,
    h,
    y
  ];
}
function ku({ sourceX: e, sourceY: t, targetX: n, targetY: i }) {
  const o = Math.abs(n - e) / 2, s = n < e ? n + o : n - o, a = Math.abs(i - t) / 2, c = i < t ? i + a : i - a;
  return [s, c, o, a];
}
function Xx({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: i = 0, elevateOnSelect: o = !1, zIndexMode: s = "basic" }) {
  if (s === "manual")
    return i;
  const a = o && n ? i + 1e3 : i, c = Math.max(e.parentId || o && e.selected ? e.internals.z : 0, t.parentId || o && t.selected ? t.internals.z : 0);
  return a + c;
}
function Yx({ sourceNode: e, targetNode: t, width: n, height: i, transform: o }) {
  const s = go(eo(e), eo(t));
  s.x === s.x2 && (s.x2 += 1), s.y === s.y2 && (s.y2 += 1);
  const a = {
    x: -o[0] / o[2],
    y: -o[1] / o[2],
    width: n / o[2],
    height: i / o[2]
  };
  return Fn(a, yo(s)) > 0;
}
const Iu = ({ source: e, sourceHandle: t, target: n, targetHandle: i }) => `xy-edge__${e}${t || ""}-${n}${i || ""}`, qx = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), Ux = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Ye.error006()), t;
  const i = n.getEdgeId || Iu;
  let o;
  return gu(e) ? o = { ...e } : o = {
    ...e,
    id: i(e)
  }, qx(o, t) ? t : (o.sourceHandle === null && delete o.sourceHandle, o.targetHandle === null && delete o.targetHandle, t.concat(o));
}, Zx = (e, t, n, i = { shouldReplaceId: !0 }) => {
  const { id: o, ...s } = e;
  if (!t.source || !t.target)
    return i.onError?.("006", Ye.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return i.onError?.("007", Ye.error007(o)), n;
  const c = i.getEdgeId || Iu, u = {
    ...s,
    id: i.shouldReplaceId ? c(t) : o,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== o).concat(u);
};
function Au({ sourceX: e, sourceY: t, targetX: n, targetY: i }) {
  const [o, s, a, c] = ku({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: i
  });
  return [`M ${e},${t}L ${n},${i}`, o, s, a, c];
}
const za = {
  [re.Left]: { x: -1, y: 0 },
  [re.Right]: { x: 1, y: 0 },
  [re.Top]: { x: 0, y: -1 },
  [re.Bottom]: { x: 0, y: 1 }
}, Gx = ({ source: e, sourcePosition: t = re.Bottom, target: n }) => t === re.Left || t === re.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, La = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Jx({ source: e, sourcePosition: t = re.Bottom, target: n, targetPosition: i = re.Top, center: o, offset: s, stepPosition: a }) {
  const c = za[t], u = za[i], l = { x: e.x + c.x * s, y: e.y + c.y * s }, d = { x: n.x + u.x * s, y: n.y + u.y * s }, f = Gx({
    source: l,
    sourcePosition: t,
    target: d
  }), p = f.x !== 0 ? "x" : "y", h = f[p];
  let y = [], v, w;
  const x = { x: 0, y: 0 }, b = { x: 0, y: 0 }, [, , g, m] = ku({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[p] * u[p] === -1) {
    p === "x" ? (v = o.x ?? l.x + (d.x - l.x) * a, w = o.y ?? (l.y + d.y) / 2) : (v = o.x ?? (l.x + d.x) / 2, w = o.y ?? l.y + (d.y - l.y) * a);
    const k = [
      { x: v, y: l.y },
      { x: v, y: d.y }
    ], _ = [
      { x: l.x, y: w },
      { x: d.x, y: w }
    ];
    c[p] === h ? y = p === "x" ? k : _ : y = p === "x" ? _ : k;
  } else {
    const k = [{ x: l.x, y: d.y }], _ = [{ x: d.x, y: l.y }];
    if (p === "x" ? y = c.x === h ? _ : k : y = c.y === h ? k : _, t === i) {
      const C = Math.abs(e[p] - n[p]);
      if (C <= s) {
        const A = Math.min(s - 1, s - C);
        c[p] === h ? x[p] = (l[p] > e[p] ? -1 : 1) * A : b[p] = (d[p] > n[p] ? -1 : 1) * A;
      }
    }
    if (t !== i) {
      const C = p === "x" ? "y" : "x", A = c[p] === u[C], E = l[C] > d[C], D = l[C] < d[C];
      (c[p] === 1 && (!A && E || A && D) || c[p] !== 1 && (!A && D || A && E)) && (y = p === "x" ? k : _);
    }
    const M = { x: l.x + x.x, y: l.y + x.y }, I = { x: d.x + b.x, y: d.y + b.y }, $ = Math.max(Math.abs(M.x - y[0].x), Math.abs(I.x - y[0].x)), L = Math.max(Math.abs(M.y - y[0].y), Math.abs(I.y - y[0].y));
    $ >= L ? (v = (M.x + I.x) / 2, w = y[0].y) : (v = y[0].x, w = (M.y + I.y) / 2);
  }
  const j = { x: l.x + x.x, y: l.y + x.y }, N = { x: d.x + b.x, y: d.y + b.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...j.x !== y[0].x || j.y !== y[0].y ? [j] : [],
    ...y,
    ...N.x !== y[y.length - 1].x || N.y !== y[y.length - 1].y ? [N] : [],
    n
  ], v, w, g, m];
}
function Qx(e, t, n, i) {
  const o = Math.min(La(e, t) / 2, La(t, n) / 2, i), { x: s, y: a } = t;
  if (e.x === s && s === n.x || e.y === a && a === n.y)
    return `L${s} ${a}`;
  if (e.y === a) {
    const l = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${s + o * l},${a}Q ${s},${a} ${s},${a + o * d}`;
  }
  const c = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${s},${a + o * u}Q ${s},${a} ${s + o * c},${a}`;
}
function to({ sourceX: e, sourceY: t, sourcePosition: n = re.Bottom, targetX: i, targetY: o, targetPosition: s = re.Top, borderRadius: a = 5, centerX: c, centerY: u, offset: l = 20, stepPosition: d = 0.5 }) {
  const [f, p, h, y, v] = Jx({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: i, y: o },
    targetPosition: s,
    center: { x: c, y: u },
    offset: l,
    stepPosition: d
  });
  let w = `M${f[0].x} ${f[0].y}`;
  for (let x = 1; x < f.length - 1; x++)
    w += Qx(f[x - 1], f[x], f[x + 1], a);
  return w += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [w, p, h, y, v];
}
function Va(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function ew(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!Va(t) || !Va(n))
    return null;
  const i = t.internals.handleBounds || Oa(t.handles), o = n.internals.handleBounds || Oa(n.handles), s = Ha(i?.source ?? [], e.sourceHandle), a = Ha(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === nn.Strict ? o?.target ?? [] : (o?.target ?? []).concat(o?.source ?? []),
    e.targetHandle
  );
  if (!s || !a)
    return e.onError?.("008", Ye.error008(s ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = s?.position || re.Bottom, u = a?.position || re.Top, l = $t(t, s, c), d = $t(n, a, u);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: d.x,
    targetY: d.y,
    sourcePosition: c,
    targetPosition: u
  };
}
function Oa(e) {
  if (!e)
    return null;
  const t = [], n = [];
  for (const i of e)
    i.width = i.width ?? 1, i.height = i.height ?? 1, i.type === "source" ? t.push(i) : i.type === "target" && n.push(i);
  return {
    source: t,
    target: n
  };
}
function $t(e, t, n = re.Left, i = !1) {
  const o = (t?.x ?? 0) + e.internals.positionAbsolute.x, s = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: a, height: c } = t ?? ft(e);
  if (i)
    return { x: o + a / 2, y: s + c / 2 };
  switch (t?.position ?? n) {
    case re.Top:
      return { x: o + a / 2, y: s };
    case re.Right:
      return { x: o + a, y: s + c / 2 };
    case re.Bottom:
      return { x: o + a / 2, y: s + c };
    case re.Left:
      return { x: o, y: s + c / 2 };
  }
}
function Ha(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Vr(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((i) => `${i}=${e[i]}`).join("&")}` : "";
}
function tw(e, { id: t, defaultColor: n, defaultMarkerStart: i, defaultMarkerEnd: o }) {
  const s = /* @__PURE__ */ new Set();
  return e.reduce((a, c) => ([c.markerStart || i, c.markerEnd || o].forEach((u) => {
    if (u && typeof u == "object") {
      const l = Vr(u, t);
      s.has(l) || (a.push({ id: l, color: u.color || n, ...u }), s.add(l));
    }
  }), a), []).sort((a, c) => a.id.localeCompare(c.id));
}
const _u = 1e3, nw = 10, Ss = {
  nodeOrigin: [0, 0],
  nodeExtent: Wn,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, iw = {
  ...Ss,
  checkEquality: !0
};
function Cs(e, t) {
  const n = { ...e };
  for (const i in t)
    t[i] !== void 0 && (n[i] = t[i]);
  return n;
}
function ow(e, t, n) {
  const i = Cs(Ss, n);
  for (const o of e.values())
    if (o.parentId)
      ks(o, e, t, i);
    else {
      const s = Qn(o, i.nodeOrigin), a = Tt(o.extent) ? o.extent : i.nodeExtent, c = Dt(s, a, ft(o));
      o.internals.positionAbsolute = c;
    }
}
function rw(e, t) {
  if (!e.handles)
    return e.measured ? t?.internals.handleBounds : void 0;
  const n = [], i = [];
  for (const o of e.handles) {
    const s = {
      id: o.id,
      width: o.width ?? 1,
      height: o.height ?? 1,
      nodeId: e.id,
      x: o.x,
      y: o.y,
      position: o.position,
      type: o.type
    };
    o.type === "source" ? n.push(s) : o.type === "target" && i.push(s);
  }
  return {
    source: n,
    target: i
  };
}
function Es(e) {
  return e === "manual";
}
function Or(e, t, n, i = {}) {
  const o = Cs(iw, i), s = { i: 0 }, a = new Map(t), c = o?.elevateNodesOnSelect && !Es(o.zIndexMode) ? _u : 0;
  let u = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = a.get(d.id);
    if (o.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const p = Qn(d, o.nodeOrigin), h = Tt(d.extent) ? d.extent : o.nodeExtent, y = Dt(p, h, ft(d));
      f = {
        ...o.defaults,
        ...d,
        measured: {
          width: d.measured?.width,
          height: d.measured?.height
        },
        internals: {
          positionAbsolute: y,
          // if user re-initializes the node or removes `measured` for whatever reason, we reset the handleBounds so that the node gets re-measured
          handleBounds: rw(d, f),
          z: Du(d, c, o.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (u = !1), d.parentId && ks(f, t, n, i, s), l ||= d.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: l };
}
function sw(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function ks(e, t, n, i, o) {
  const { elevateNodesOnSelect: s, nodeOrigin: a, nodeExtent: c, zIndexMode: u } = Cs(Ss, i), l = e.parentId, d = t.get(l);
  if (!d) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  sw(e, n), o && !d.parentId && d.internals.rootParentIndex === void 0 && u === "auto" && (d.internals.rootParentIndex = ++o.i, d.internals.z = d.internals.z + o.i * nw), o && d.internals.rootParentIndex !== void 0 && (o.i = d.internals.rootParentIndex);
  const f = s && !Es(u) ? _u : 0, { x: p, y: h, z: y } = aw(e, d, a, c, f, u), { positionAbsolute: v } = e.internals, w = p !== v.x || h !== v.y;
  (w || y !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: w ? { x: p, y: h } : v,
      z: y
    }
  });
}
function Du(e, t, n) {
  const i = Je(e.zIndex) ? e.zIndex : 0;
  return Es(n) ? i : i + (e.selected ? t : 0);
}
function aw(e, t, n, i, o, s) {
  const { x: a, y: c } = t.internals.positionAbsolute, u = ft(e), l = Qn(e, n), d = Tt(e.extent) ? Dt(l, e.extent, u) : l;
  let f = Dt({ x: a + d.x, y: c + d.y }, i, u);
  e.extent === "parent" && (f = mu(f, u, t));
  const p = Du(e, o, s), h = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: h >= p ? h + 1 : p
  };
}
function Is(e, t, n, i = [0, 0]) {
  const o = [], s = /* @__PURE__ */ new Map();
  for (const a of e) {
    const c = t.get(a.parentId);
    if (!c)
      continue;
    const u = s.get(a.parentId)?.expandedRect ?? rn(c), l = xu(u, a.rect);
    s.set(a.parentId, { expandedRect: l, parent: c });
  }
  return s.size > 0 && s.forEach(({ expandedRect: a, parent: c }, u) => {
    const l = c.internals.positionAbsolute, d = ft(c), f = c.origin ?? i, p = a.x < l.x ? Math.round(Math.abs(l.x - a.x)) : 0, h = a.y < l.y ? Math.round(Math.abs(l.y - a.y)) : 0, y = Math.max(d.width, Math.round(a.width)), v = Math.max(d.height, Math.round(a.height)), w = (y - d.width) * f[0], x = (v - d.height) * f[1];
    (p > 0 || h > 0 || w || x) && (o.push({
      id: u,
      type: "position",
      position: {
        x: c.position.x - p + w,
        y: c.position.y - h + x
      }
    }), n.get(u)?.forEach((b) => {
      e.some((g) => g.id === b.id) || o.push({
        id: b.id,
        type: "position",
        position: {
          x: b.position.x + p,
          y: b.position.y + h
        }
      });
    })), (d.width < a.width || d.height < a.height || p || h) && o.push({
      id: u,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: y + (p ? f[0] * p - w : 0),
        height: v + (h ? f[1] * h - x : 0)
      }
    });
  }), o;
}
function cw(e, t, n, i, o, s, a) {
  const c = i?.querySelector(".xyflow__viewport");
  let u = !1;
  if (!c)
    return { changes: [], updatedInternals: u };
  const l = [], d = window.getComputedStyle(c), { m22: f } = new window.DOMMatrixReadOnly(d.transform), p = [];
  for (const h of e.values()) {
    const y = t.get(h.id);
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
    const v = js(h.nodeElement), w = y.measured.width !== v.width || y.measured.height !== v.height;
    if (!!(v.width && v.height && (w || !y.internals.handleBounds || h.force))) {
      const b = h.nodeElement.getBoundingClientRect(), g = Tt(y.extent) ? y.extent : s;
      let { positionAbsolute: m } = y.internals;
      y.parentId && y.extent === "parent" ? m = mu(m, v, t.get(y.parentId)) : g && (m = Dt(m, g, v));
      const j = {
        ...y,
        measured: v,
        internals: {
          ...y.internals,
          positionAbsolute: m,
          handleBounds: {
            source: Ma("source", h.nodeElement, b, f, y.id),
            target: Ma("target", h.nodeElement, b, f, y.id)
          }
        }
      };
      t.set(y.id, j), y.parentId && ks(j, t, n, { nodeOrigin: o, zIndexMode: a }), u = !0, w && (l.push({
        id: y.id,
        type: "dimensions",
        dimensions: v
      }), y.expandParent && y.parentId && p.push({
        id: y.id,
        parentId: y.parentId,
        rect: rn(j, o)
      }));
    }
  }
  if (p.length > 0) {
    const h = Is(p, t, n, o);
    l.push(...h);
  }
  return { changes: l, updatedInternals: u };
}
async function lw({ delta: e, panZoom: t, transform: n, translateExtent: i, width: o, height: s }) {
  if (!t || !e.x && !e.y)
    return !1;
  const a = await t.setViewportConstrained({
    x: n[0] + e.x,
    y: n[1] + e.y,
    zoom: n[2]
  }, [
    [0, 0],
    [o, s]
  ], i);
  return !!a && (a.x !== n[0] || a.y !== n[1] || a.k !== n[2]);
}
function Wa(e, t, n, i, o, s) {
  let a = o;
  const c = i.get(a) || /* @__PURE__ */ new Map();
  i.set(a, c.set(n, t)), a = `${o}-${e}`;
  const u = i.get(a) || /* @__PURE__ */ new Map();
  if (i.set(a, u.set(n, t)), s) {
    a = `${o}-${e}-${s}`;
    const l = i.get(a) || /* @__PURE__ */ new Map();
    i.set(a, l.set(n, t));
  }
}
function Tu(e, t, n) {
  e.clear(), t.clear();
  for (const i of n) {
    const { source: o, target: s, sourceHandle: a = null, targetHandle: c = null } = i, u = { edgeId: i.id, source: o, target: s, sourceHandle: a, targetHandle: c }, l = `${o}-${a}--${s}-${c}`, d = `${s}-${c}--${o}-${a}`;
    Wa("source", u, d, e, o, a), Wa("target", u, l, e, s, c), t.set(i.id, i);
  }
}
function $u(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : $u(n, t) : !1;
}
function Ba(e, t, n) {
  let i = e;
  do {
    if (i?.matches?.(t))
      return !0;
    if (i === n)
      return !1;
    i = i?.parentElement;
  } while (i);
  return !1;
}
function uw(e, t, n, i) {
  const o = /* @__PURE__ */ new Map();
  for (const [s, a] of e)
    if ((a.selected || a.id === i) && (!a.parentId || !$u(a, e)) && (a.draggable || t && typeof a.draggable > "u")) {
      const c = e.get(s);
      c && o.set(s, {
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
  return o;
}
function hr({ nodeId: e, dragItems: t, nodeLookup: n, dragging: i = !0 }) {
  const o = [];
  for (const [a, c] of t) {
    const u = n.get(a)?.internals.userNode;
    u && o.push({
      ...u,
      position: c.position,
      dragging: i
    });
  }
  if (!e)
    return [o[0], o];
  const s = n.get(e)?.internals.userNode;
  return [
    s ? {
      ...s,
      position: t.get(e)?.position || s.position,
      dragging: i
    } : o[0],
    o
  ];
}
function dw({ dragItems: e, snapGrid: t, x: n, y: i }) {
  const o = e.values().next().value;
  if (!o)
    return null;
  const s = {
    x: n - o.distance.x,
    y: i - o.distance.y
  }, a = ti(s, t);
  return {
    x: a.x - s.x,
    y: a.y - s.y
  };
}
function fw({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: i, onDragStop: o }) {
  let s = { x: null, y: null }, a = 0, c = /* @__PURE__ */ new Map(), u = !1, l = { x: 0, y: 0 }, d = null, f = !1, p = null, h = !1, y = !1, v = null;
  function w({ noDragClassName: b, handleSelector: g, domNode: m, isSelectable: j, nodeId: N, nodeClickDistance: S = 0 }) {
    p = Be(m);
    function k({ x: $, y: L }) {
      const { nodeLookup: C, nodeExtent: A, snapGrid: E, snapToGrid: D, nodeOrigin: P, onNodeDrag: T, onSelectionDrag: B, onError: W, updateNodePositions: H } = t();
      s = { x: $, y: L };
      let q = !1;
      const V = c.size > 1, Y = V && A ? Lr(ei(c)) : null, J = V && D ? dw({
        dragItems: c,
        snapGrid: E,
        x: $,
        y: L
      }) : null;
      for (const [Z, R] of c) {
        if (!C.has(Z))
          continue;
        let X = { x: $ - R.distance.x, y: L - R.distance.y };
        D && (X = J ? {
          x: Math.round(X.x + J.x),
          y: Math.round(X.y + J.y)
        } : ti(X, E));
        let le = null;
        if (V && A && !R.extent && Y) {
          const { positionAbsolute: ne } = R.internals, fe = ne.x - Y.x + A[0][0], O = ne.x + R.measured.width - Y.x2 + A[1][0], te = ne.y - Y.y + A[0][1], ge = ne.y + R.measured.height - Y.y2 + A[1][1];
          le = [
            [fe, te],
            [O, ge]
          ];
        }
        const { position: ce, positionAbsolute: ee } = yu({
          nodeId: Z,
          nextPosition: X,
          nodeLookup: C,
          nodeExtent: le || A,
          nodeOrigin: P,
          onError: W
        });
        q = q || R.position.x !== ce.x || R.position.y !== ce.y, R.position = ce, R.internals.positionAbsolute = ee;
      }
      if (y = y || q, !!q && (H(c, !0), v && (i || T || !N && B))) {
        const [Z, R] = hr({
          nodeId: N,
          dragItems: c,
          nodeLookup: C
        });
        i?.(v, c, Z, R), T?.(v, Z, R), N || B?.(v, R);
      }
    }
    async function _() {
      if (!d)
        return;
      const { transform: $, panBy: L, autoPanSpeed: C, autoPanOnNodeDrag: A } = t();
      if (!A) {
        u = !1, cancelAnimationFrame(a);
        return;
      }
      const [E, D] = bs(l, d, C);
      (E !== 0 || D !== 0) && (s.x = (s.x ?? 0) - E / $[2], s.y = (s.y ?? 0) - D / $[2], await L({ x: E, y: D }) && k(s)), a = requestAnimationFrame(_);
    }
    function M($) {
      const { nodeLookup: L, multiSelectionActive: C, nodesDraggable: A, transform: E, snapGrid: D, snapToGrid: P, selectNodesOnDrag: T, onNodeDragStart: B, onSelectionDragStart: W, unselectNodesAndEdges: H } = t();
      f = !0, (!T || !j) && !C && N && (L.get(N)?.selected || H()), j && T && N && e?.(N);
      const q = _n($.sourceEvent, { transform: E, snapGrid: D, snapToGrid: P, containerBounds: d });
      if (s = q, c = uw(L, A, q, N), c.size > 0 && (n || B || !N && W)) {
        const [V, Y] = hr({
          nodeId: N,
          dragItems: c,
          nodeLookup: L
        });
        n?.($.sourceEvent, c, V, Y), B?.($.sourceEvent, V, Y), N || W?.($.sourceEvent, Y);
      }
    }
    const I = Ul().clickDistance(S).on("start", ($) => {
      const { domNode: L, nodeDragThreshold: C, transform: A, snapGrid: E, snapToGrid: D } = t();
      d = L?.getBoundingClientRect() || null, h = !1, y = !1, v = $.sourceEvent, C === 0 && M($), s = _n($.sourceEvent, { transform: A, snapGrid: E, snapToGrid: D, containerBounds: d }), l = Qe($.sourceEvent, d);
    }).on("drag", ($) => {
      const { autoPanOnNodeDrag: L, transform: C, snapGrid: A, snapToGrid: E, nodeDragThreshold: D, nodeLookup: P } = t(), T = _n($.sourceEvent, { transform: C, snapGrid: A, snapToGrid: E, containerBounds: d });
      if (v = $.sourceEvent, ($.sourceEvent.type === "touchmove" && $.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      N && !P.has(N)) && (h = !0), !h) {
        if (!u && L && f && (u = !0, _()), !f) {
          const B = Qe($.sourceEvent, d), W = B.x - l.x, H = B.y - l.y;
          Math.sqrt(W * W + H * H) > D && M($);
        }
        (s.x !== T.xSnapped || s.y !== T.ySnapped) && c && f && (l = Qe($.sourceEvent, d), k(T));
      }
    }).on("end", ($) => {
      if (!f || h) {
        h && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (u = !1, f = !1, cancelAnimationFrame(a), c.size > 0) {
        const { nodeLookup: L, updateNodePositions: C, onNodeDragStop: A, onSelectionDragStop: E } = t();
        if (y && (C(c, !1), y = !1), o || A || !N && E) {
          const [D, P] = hr({
            nodeId: N,
            dragItems: c,
            nodeLookup: L,
            dragging: !1
          });
          o?.($.sourceEvent, c, D, P), A?.($.sourceEvent, D, P), N || E?.($.sourceEvent, P);
        }
      }
    }).filter(($) => {
      const L = $.target;
      return !$.button && (!b || !Ba(L, `.${b}`, m)) && (!g || Ba(L, g, m));
    });
    p.call(I);
  }
  function x() {
    p?.on(".drag", null);
  }
  return {
    update: w,
    destroy: x
  };
}
function pw(e, t, n) {
  const i = [], o = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const s of t.values())
    Fn(o, rn(s)) > 0 && i.push(s);
  return i;
}
const hw = 250;
function gw(e, t, n, i) {
  let o = [], s = 1 / 0;
  const a = pw(e, n, t + hw);
  for (const c of a) {
    const u = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of u) {
      if (i.nodeId === l.nodeId && i.type === l.type && i.id === l.id)
        continue;
      const { x: d, y: f } = $t(c, l, l.position, !0), p = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(f - e.y, 2));
      p > t || (p < s ? (o = [{ ...l, x: d, y: f }], s = p) : p === s && o.push({ ...l, x: d, y: f }));
    }
  }
  if (!o.length)
    return null;
  if (o.length > 1) {
    const c = i.type === "source" ? "target" : "source";
    return o.find((u) => u.type === c) ?? o[0];
  }
  return o[0];
}
function Pu(e, t, n, i, o, s = !1) {
  const a = i.get(e);
  if (!a)
    return null;
  const c = o === "strict" ? a.internals.handleBounds?.[t] : [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []], u = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return u && s ? { ...u, ...$t(a, u, u.position, !0) } : u;
}
function Mu(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function yw(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const Ru = () => !0;
function mw(e, { connectionMode: t, connectionRadius: n, handleId: i, nodeId: o, edgeUpdaterType: s, isTarget: a, domNode: c, nodeLookup: u, lib: l, autoPanOnConnect: d, flowId: f, panBy: p, cancelConnection: h, onConnectStart: y, onConnect: v, onConnectEnd: w, isValidConnection: x = Ru, onReconnectEnd: b, updateConnection: g, getTransform: m, getFromHandle: j, autoPanSpeed: N, dragThreshold: S = 1, handleDomNode: k }) {
  const _ = Nu(e.target);
  let M = 0, I;
  const { x: $, y: L } = Qe(e), C = Mu(s, k), A = c?.getBoundingClientRect();
  let E = !1;
  if (!A || !C)
    return;
  const D = Pu(o, C, i, u, t);
  if (!D)
    return;
  let P = Qe(e, A), T = !1, B = null, W = !1, H = null;
  function q() {
    if (!d || !A)
      return;
    const [ce, ee] = bs(P, A, N);
    p({ x: ce, y: ee }), M = requestAnimationFrame(q);
  }
  const V = {
    ...D,
    nodeId: o,
    type: C,
    position: D.position
  }, Y = u.get(o);
  let Z = {
    inProgress: !0,
    isValid: null,
    from: $t(Y, V, re.Left, !0),
    fromHandle: V,
    fromPosition: V.position,
    fromNode: Y,
    to: P,
    toHandle: null,
    toPosition: Da[V.position],
    toNode: null,
    pointer: P
  };
  function R() {
    E = !0, g(Z), y?.(e, { nodeId: o, handleId: i, handleType: C });
  }
  S === 0 && R();
  function X(ce) {
    if (!E) {
      const { x: ge, y: ye } = Qe(ce), _e = ge - $, De = ye - L;
      if (!(_e * _e + De * De > S * S))
        return;
      R();
    }
    if (!j() || !V) {
      le(ce);
      return;
    }
    const ee = m();
    P = Qe(ce, A), I = gw(pn(P, ee, !1, [1, 1]), n, u, V), T || (q(), T = !0);
    const ne = zu(ce, {
      handle: I,
      connectionMode: t,
      fromNodeId: o,
      fromHandleId: i,
      fromType: a ? "target" : "source",
      isValidConnection: x,
      doc: _,
      lib: l,
      flowId: f,
      nodeLookup: u
    });
    H = ne.handleDomNode, B = ne.connection, W = yw(!!I, ne.isValid);
    const fe = u.get(o), O = fe ? $t(fe, V, re.Left, !0) : Z.from, te = {
      ...Z,
      from: O,
      isValid: W,
      to: ne.toHandle && W ? sn({ x: ne.toHandle.x, y: ne.toHandle.y }, ee) : P,
      toHandle: ne.toHandle,
      toPosition: W && ne.toHandle ? ne.toHandle.position : Da[V.position],
      toNode: ne.toHandle ? u.get(ne.toHandle.nodeId) : null,
      pointer: P
    };
    g(te), Z = te;
  }
  function le(ce) {
    if (!("touches" in ce && ce.touches.length > 0)) {
      if (E) {
        (I || H) && B && W && v?.(B);
        const { inProgress: ee, ...ne } = Z, fe = {
          ...ne,
          toPosition: Z.toHandle ? Z.toPosition : null
        };
        w?.(ce, fe), s && b?.(ce, fe);
      }
      h(), cancelAnimationFrame(M), T = !1, W = !1, B = null, H = null, _.removeEventListener("mousemove", X), _.removeEventListener("mouseup", le), _.removeEventListener("touchmove", X), _.removeEventListener("touchend", le);
    }
  }
  _.addEventListener("mousemove", X), _.addEventListener("mouseup", le), _.addEventListener("touchmove", X), _.addEventListener("touchend", le);
}
function zu(e, { handle: t, connectionMode: n, fromNodeId: i, fromHandleId: o, fromType: s, doc: a, lib: c, flowId: u, isValidConnection: l = Ru, nodeLookup: d }) {
  const f = s === "target", p = t ? a.querySelector(`.${c}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: h, y } = Qe(e), v = a.elementFromPoint(h, y), w = v?.classList.contains(`${c}-flow__handle`) ? v : p, x = {
    handleDomNode: w,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (w) {
    const b = Mu(void 0, w), g = w.getAttribute("data-nodeid"), m = w.getAttribute("data-handleid"), j = w.classList.contains("connectable"), N = w.classList.contains("connectableend");
    if (!g || !b)
      return x;
    const S = {
      source: f ? g : i,
      sourceHandle: f ? m : o,
      target: f ? i : g,
      targetHandle: f ? o : m
    };
    x.connection = S;
    const _ = j && N && (n === nn.Strict ? f && b === "source" || !f && b === "target" : g !== i || m !== o);
    x.isValid = _ && l(S), x.toHandle = Pu(g, b, m, d, n, !0);
  }
  return x;
}
const Hr = {
  onPointerDown: mw,
  isValid: zu
};
function xw({ domNode: e, panZoom: t, getTransform: n, getViewScale: i }) {
  const o = Be(e);
  function s({ translateExtent: c, width: u, height: l, zoomStep: d = 1, pannable: f = !0, zoomable: p = !0, inversePan: h = !1 }) {
    const y = (g) => {
      if (g.sourceEvent.type !== "wheel" || !t)
        return;
      const m = n(), j = g.sourceEvent.ctrlKey && Kn() ? 10 : 1, N = -g.sourceEvent.deltaY * (g.sourceEvent.deltaMode === 1 ? 0.05 : g.sourceEvent.deltaMode ? 1 : 2e-3) * d, S = m[2] * Math.pow(2, N * j);
      t.scaleTo(S);
    };
    let v = [0, 0];
    const w = (g) => {
      (g.sourceEvent.type === "mousedown" || g.sourceEvent.type === "touchstart") && (v = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ]);
    }, x = (g) => {
      const m = n();
      if (g.sourceEvent.type !== "mousemove" && g.sourceEvent.type !== "touchmove" || !t)
        return;
      const j = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ], N = [j[0] - v[0], j[1] - v[1]];
      v = j;
      const S = i() * Math.max(m[2], Math.log(m[2])) * (h ? -1 : 1), k = {
        x: m[0] - N[0] * S,
        y: m[1] - N[1] * S
      }, _ = [
        [0, 0],
        [u, l]
      ];
      t.setViewportConstrained({
        x: k.x,
        y: k.y,
        zoom: m[2]
      }, _, c);
    }, b = uu().on("start", w).on("zoom", f ? x : null).on("zoom.wheel", p ? y : null);
    o.call(b, {});
  }
  function a() {
    o.on("zoom", null);
  }
  return {
    update: s,
    destroy: a,
    pointer: Ze
  };
}
const mo = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), gr = ({ x: e, y: t, zoom: n }) => ho.translate(e, t).scale(n), Kt = (e, t) => e.target.closest(`.${t}`), Lu = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), ww = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, yr = (e, t = 0, n = ww, i = () => {
}) => {
  const o = typeof t == "number" && t > 0;
  return o || i(), o ? e.transition().duration(t).ease(n).on("end", i) : e;
}, Vu = (e) => {
  const t = e.ctrlKey && Kn() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function vw({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: i, panOnScrollMode: o, panOnScrollSpeed: s, zoomOnPinch: a, onPanZoomStart: c, onPanZoom: u, onPanZoomEnd: l }) {
  return (d) => {
    if (Kt(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && a) {
      const w = Ze(d), x = Vu(d), b = f * Math.pow(2, x);
      i.scaleTo(n, b, w, d);
      return;
    }
    const p = d.deltaMode === 1 ? 20 : 1;
    let h = o === Ct.Vertical ? 0 : d.deltaX * p, y = o === Ct.Horizontal ? 0 : d.deltaY * p;
    !Kn() && d.shiftKey && o !== Ct.Vertical && (h = d.deltaY * p, y = 0), i.translateBy(
      n,
      -(h / f) * s,
      -(y / f) * s,
      // @ts-ignore
      { internal: !0 }
    );
    const v = mo(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(d, v), e.panScrollTimeout = setTimeout(() => {
      l?.(d, v), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(d, v));
  };
}
function bw({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(i, o) {
    const s = i.type === "wheel", a = !t && s && !i.ctrlKey, c = Kt(i, e);
    if (i.ctrlKey && s && c && i.preventDefault(), a || c)
      return null;
    i.preventDefault(), n.call(this, i, o);
  };
}
function Nw({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (i) => {
    if (i.sourceEvent?.internal)
      return;
    const o = mo(i.transform);
    e.mouseButton = i.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = o, i.sourceEvent?.type === "mousedown" && t(!0), n && n?.(i.sourceEvent, o);
  };
}
function jw({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: i, onPanZoom: o }) {
  return (s) => {
    e.usedRightMouseButton = !!(n && Lu(t, e.mouseButton ?? 0)), s.sourceEvent?.sync || i([s.transform.x, s.transform.y, s.transform.k]), o && !s.sourceEvent?.internal && o?.(s.sourceEvent, mo(s.transform));
  };
}
function Sw({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: i, onPanZoomEnd: o, onPaneContextMenu: s }) {
  return (a) => {
    if (!a.sourceEvent?.internal && (e.isZoomingOrPanning = !1, s && Lu(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && a.sourceEvent && s(a.sourceEvent), e.usedRightMouseButton = !1, i(!1), o)) {
      const c = mo(a.transform);
      e.prevViewport = c, clearTimeout(e.timerId), e.timerId = setTimeout(
        () => {
          o?.(a.sourceEvent, c);
        },
        // we need a setTimeout for panOnScroll to suppress multiple end events fired during scroll
        n ? 150 : 0
      );
    }
  };
}
function Cw({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: i, panOnScroll: o, zoomOnDoubleClick: s, userSelectionActive: a, noWheelClassName: c, noPanClassName: u, lib: l, connectionInProgress: d }) {
  return (f) => {
    const p = e || t, h = n && f.ctrlKey, y = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (Kt(f, `${l}-flow__node`) || Kt(f, `${l}-flow__edge`)))
      return !0;
    if (!i && !p && !o && !s && !n || a || d && !y || Kt(f, c) && y || Kt(f, u) && (!y || o && y && !e) || !n && f.ctrlKey && y)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!p && !o && !h && y || !i && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(i) && !i.includes(f.button) && f.type === "mousedown")
      return !1;
    const v = Array.isArray(i) && i.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || y) && v;
  };
}
function Ew({ domNode: e, minZoom: t, maxZoom: n, translateExtent: i, viewport: o, onPanZoom: s, onPanZoomStart: a, onPanZoomEnd: c, onDraggingChange: u }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = uu().scaleExtent([t, n]).translateExtent(i), p = Be(e).call(f);
  b({
    x: o.x,
    y: o.y,
    zoom: on(o.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], i);
  const h = p.on("wheel.zoom"), y = p.on("dblclick.zoom");
  f.wheelDelta(Vu);
  async function v(I, $) {
    return p ? new Promise((L) => {
      f?.interpolate($?.interpolate === "linear" ? An : Pi).transform(yr(p, $?.duration, $?.ease, () => L(!0)), I);
    }) : !1;
  }
  function w({ noWheelClassName: I, noPanClassName: $, onPaneContextMenu: L, userSelectionActive: C, panOnScroll: A, panOnDrag: E, panOnScrollMode: D, panOnScrollSpeed: P, preventScrolling: T, zoomOnPinch: B, zoomOnScroll: W, zoomOnDoubleClick: H, zoomActivationKeyPressed: q, lib: V, onTransformChange: Y, connectionInProgress: J, paneClickDistance: Z, selectionOnDrag: R }) {
    C && !l.isZoomingOrPanning && x();
    const X = A && !q && !C;
    f.clickDistance(R ? 1 / 0 : !Je(Z) || Z < 0 ? 0 : Z);
    const le = X ? vw({
      zoomPanValues: l,
      noWheelClassName: I,
      d3Selection: p,
      d3Zoom: f,
      panOnScrollMode: D,
      panOnScrollSpeed: P,
      zoomOnPinch: B,
      onPanZoomStart: a,
      onPanZoom: s,
      onPanZoomEnd: c
    }) : bw({
      noWheelClassName: I,
      preventScrolling: T,
      d3ZoomHandler: h
    });
    p.on("wheel.zoom", le, { passive: !1 });
    const ce = Nw({
      zoomPanValues: l,
      onDraggingChange: u,
      onPanZoomStart: a
    });
    f.on("start", ce);
    const ee = jw({
      zoomPanValues: l,
      panOnDrag: E,
      onPaneContextMenu: !!L,
      onPanZoom: s,
      onTransformChange: Y
    });
    f.on("zoom", ee);
    const ne = Sw({
      zoomPanValues: l,
      panOnDrag: E,
      panOnScroll: A,
      onPaneContextMenu: L,
      onPanZoomEnd: c,
      onDraggingChange: u
    });
    f.on("end", ne);
    const fe = Cw({
      zoomActivationKeyPressed: q,
      panOnDrag: E,
      zoomOnScroll: W,
      panOnScroll: A,
      zoomOnDoubleClick: H,
      zoomOnPinch: B,
      userSelectionActive: C,
      noPanClassName: $,
      noWheelClassName: I,
      lib: V,
      connectionInProgress: J
    });
    f.filter(fe), H ? p.on("dblclick.zoom", y) : p.on("dblclick.zoom", null);
  }
  function x() {
    f.on("zoom", null);
  }
  async function b(I, $, L) {
    const C = gr(I), A = f?.constrain()(C, $, L);
    return A && await v(A), A;
  }
  async function g(I, $) {
    const L = gr(I);
    return await v(L, $), L;
  }
  function m(I) {
    if (p) {
      const $ = gr(I), L = p.property("__zoom");
      (L.k !== I.zoom || L.x !== I.x || L.y !== I.y) && f?.transform(p, $, null, { sync: !0 });
    }
  }
  function j() {
    const I = p ? lu(p.node()) : { x: 0, y: 0, k: 1 };
    return { x: I.x, y: I.y, zoom: I.k };
  }
  async function N(I, $) {
    return p ? new Promise((L) => {
      f?.interpolate($?.interpolate === "linear" ? An : Pi).scaleTo(yr(p, $?.duration, $?.ease, () => L(!0)), I);
    }) : !1;
  }
  async function S(I, $) {
    return p ? new Promise((L) => {
      f?.interpolate($?.interpolate === "linear" ? An : Pi).scaleBy(yr(p, $?.duration, $?.ease, () => L(!0)), I);
    }) : !1;
  }
  function k(I) {
    f?.scaleExtent(I);
  }
  function _(I) {
    f?.translateExtent(I);
  }
  function M(I) {
    const $ = !Je(I) || I < 0 ? 0 : I;
    f?.clickDistance($);
  }
  return {
    update: w,
    destroy: x,
    setViewport: g,
    setViewportConstrained: b,
    getViewport: j,
    scaleTo: N,
    scaleBy: S,
    setScaleExtent: k,
    setTranslateExtent: _,
    syncViewport: m,
    setClickDistance: M
  };
}
var an;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(an || (an = {}));
function kw({ width: e, prevWidth: t, height: n, prevHeight: i, affectsX: o, affectsY: s }) {
  const a = e - t, c = n - i, u = [a > 0 ? 1 : a < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return a && o && (u[0] = u[0] * -1), c && s && (u[1] = u[1] * -1), u;
}
function Fa(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), i = e.includes("left"), o = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: i,
    affectsY: o
  };
}
function ht(e, t) {
  return Math.max(0, t - e);
}
function gt(e, t) {
  return Math.max(0, e - t);
}
function Ci(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function Ka(e, t) {
  return e ? !t : t;
}
function Iw(e, t, n, i, o, s, a, c) {
  let { affectsX: u, affectsY: l } = t;
  const { isHorizontal: d, isVertical: f } = t, p = d && f, { xSnapped: h, ySnapped: y } = n, { minWidth: v, maxWidth: w, minHeight: x, maxHeight: b } = i, { x: g, y: m, width: j, height: N, aspectRatio: S } = e;
  let k = Math.floor(d ? h - e.pointerX : 0), _ = Math.floor(f ? y - e.pointerY : 0);
  const M = j + (u ? -k : k), I = N + (l ? -_ : _), $ = -s[0] * j, L = -s[1] * N;
  let C = Ci(M, v, w), A = Ci(I, x, b);
  if (a) {
    let P = 0, T = 0;
    u && k < 0 ? P = ht(g + k + $, a[0][0]) : !u && k > 0 && (P = gt(g + M + $, a[1][0])), l && _ < 0 ? T = ht(m + _ + L, a[0][1]) : !l && _ > 0 && (T = gt(m + I + L, a[1][1])), C = Math.max(C, P), A = Math.max(A, T);
  }
  if (c) {
    let P = 0, T = 0;
    u && k > 0 ? P = gt(g + k, c[0][0]) : !u && k < 0 && (P = ht(g + M, c[1][0])), l && _ > 0 ? T = gt(m + _, c[0][1]) : !l && _ < 0 && (T = ht(m + I, c[1][1])), C = Math.max(C, P), A = Math.max(A, T);
  }
  if (o) {
    if (d) {
      const P = Ci(M / S, x, b) * S;
      if (C = Math.max(C, P), a) {
        let T = 0;
        !u && !l || u && !l && p ? T = gt(m + L + M / S, a[1][1]) * S : T = ht(m + L + (u ? k : -k) / S, a[0][1]) * S, C = Math.max(C, T);
      }
      if (c) {
        let T = 0;
        !u && !l || u && !l && p ? T = ht(m + M / S, c[1][1]) * S : T = gt(m + (u ? k : -k) / S, c[0][1]) * S, C = Math.max(C, T);
      }
    }
    if (f) {
      const P = Ci(I * S, v, w) / S;
      if (A = Math.max(A, P), a) {
        let T = 0;
        !u && !l || l && !u && p ? T = gt(g + I * S + $, a[1][0]) / S : T = ht(g + (l ? _ : -_) * S + $, a[0][0]) / S, A = Math.max(A, T);
      }
      if (c) {
        let T = 0;
        !u && !l || l && !u && p ? T = ht(g + I * S, c[1][0]) / S : T = gt(g + (l ? _ : -_) * S, c[0][0]) / S, A = Math.max(A, T);
      }
    }
  }
  _ = _ + (_ < 0 ? A : -A), k = k + (k < 0 ? C : -C), o && (p ? M > I * S ? _ = (Ka(u, l) ? -k : k) / S : k = (Ka(u, l) ? -_ : _) * S : d ? (_ = k / S, l = u) : (k = _ * S, u = l));
  const E = u ? g + k : g, D = l ? m + _ : m;
  return {
    width: j + (u ? -k : k),
    height: N + (l ? -_ : _),
    x: s[0] * k * (u ? -1 : 1) + E,
    y: s[1] * _ * (l ? -1 : 1) + D
  };
}
const Ou = { width: 0, height: 0, x: 0, y: 0 }, Aw = {
  ...Ou,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function _w(e, t, n) {
  const i = t.position.x + e.position.x, o = t.position.y + e.position.y, s = e.measured.width ?? 0, a = e.measured.height ?? 0, c = n[0] * s, u = n[1] * a;
  return [
    [i - c, o - u],
    [i + s - c, o + a - u]
  ];
}
function Dw({ domNode: e, nodeId: t, getStoreItems: n, onChange: i, onEnd: o }) {
  const s = Be(e);
  let a = {
    controlDirection: Fa("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: l, boundaries: d, keepAspectRatio: f, resizeDirection: p, onResizeStart: h, onResize: y, onResizeEnd: v, shouldResize: w }) {
    let x = { ...Ou }, b = { ...Aw };
    a = {
      boundaries: d,
      resizeDirection: p,
      keepAspectRatio: f,
      controlDirection: Fa(l)
    };
    let g, m = null, j = [], N, S, k, _ = !1;
    const M = Ul().on("start", (I) => {
      const { nodeLookup: $, transform: L, snapGrid: C, snapToGrid: A, nodeOrigin: E, paneDomNode: D } = n();
      if (g = $.get(t), !g)
        return;
      m = D?.getBoundingClientRect() ?? null;
      const { xSnapped: P, ySnapped: T } = _n(I.sourceEvent, {
        transform: L,
        snapGrid: C,
        snapToGrid: A,
        containerBounds: m
      });
      x = {
        width: g.measured.width ?? 0,
        height: g.measured.height ?? 0,
        x: g.position.x ?? 0,
        y: g.position.y ?? 0
      }, b = {
        ...x,
        pointerX: P,
        pointerY: T,
        aspectRatio: x.width / x.height
      }, N = void 0, S = Tt(g.extent) ? g.extent : void 0, g.parentId && (g.extent === "parent" || g.expandParent) && (N = $.get(g.parentId)), N && g.extent === "parent" && (S = [
        [0, 0],
        [N.measured.width, N.measured.height]
      ]), j = [], k = void 0;
      for (const [B, W] of $)
        if (W.parentId === t && (j.push({
          id: B,
          position: { ...W.position },
          extent: W.extent
        }), W.extent === "parent" || W.expandParent)) {
          const H = _w(W, g, W.origin ?? E);
          k ? k = [
            [Math.min(H[0][0], k[0][0]), Math.min(H[0][1], k[0][1])],
            [Math.max(H[1][0], k[1][0]), Math.max(H[1][1], k[1][1])]
          ] : k = H;
        }
      h?.(I, { ...x });
    }).on("drag", (I) => {
      const { transform: $, snapGrid: L, snapToGrid: C, nodeOrigin: A } = n(), E = _n(I.sourceEvent, {
        transform: $,
        snapGrid: L,
        snapToGrid: C,
        containerBounds: m
      }), D = [];
      if (!g)
        return;
      const { x: P, y: T, width: B, height: W } = x, H = {}, q = g.origin ?? A, { width: V, height: Y, x: J, y: Z } = Iw(b, a.controlDirection, E, a.boundaries, a.keepAspectRatio, q, S, k), R = V !== B, X = Y !== W, le = J !== P && R, ce = Z !== T && X;
      if (!le && !ce && !R && !X)
        return;
      if ((le || ce || q[0] === 1 || q[1] === 1) && (H.x = le ? J : x.x, H.y = ce ? Z : x.y, x.x = H.x, x.y = H.y, j.length > 0)) {
        const O = J - P, te = Z - T;
        for (const ge of j)
          ge.position = {
            x: ge.position.x - O + q[0] * (V - B),
            y: ge.position.y - te + q[1] * (Y - W)
          }, D.push(ge);
      }
      if ((R || X) && (H.width = R && (!a.resizeDirection || a.resizeDirection === "horizontal") ? V : x.width, H.height = X && (!a.resizeDirection || a.resizeDirection === "vertical") ? Y : x.height, x.width = H.width, x.height = H.height), N && g.expandParent) {
        const O = q[0] * (H.width ?? 0);
        H.x && H.x < O && (x.x = O, b.x = b.x - (H.x - O));
        const te = q[1] * (H.height ?? 0);
        H.y && H.y < te && (x.y = te, b.y = b.y - (H.y - te));
      }
      const ee = kw({
        width: x.width,
        prevWidth: B,
        height: x.height,
        prevHeight: W,
        affectsX: a.controlDirection.affectsX,
        affectsY: a.controlDirection.affectsY
      }), ne = { ...x, direction: ee };
      w?.(I, ne) !== !1 && (_ = !0, y?.(I, ne), i(H, D));
    }).on("end", (I) => {
      _ && (v?.(I, { ...x }), o?.({ ...x }), _ = !1);
    });
    s.call(M);
  }
  function u() {
    s.on(".drag", null);
  }
  return {
    update: c,
    destroy: u
  };
}
var mr = { exports: {} }, xr = {}, wr = { exports: {} }, vr = {};
var Xa;
function Tw() {
  if (Xa) return vr;
  Xa = 1;
  var e = Ke;
  function t(f, p) {
    return f === p && (f !== 0 || 1 / f === 1 / p) || f !== f && p !== p;
  }
  var n = typeof Object.is == "function" ? Object.is : t, i = e.useState, o = e.useEffect, s = e.useLayoutEffect, a = e.useDebugValue;
  function c(f, p) {
    var h = p(), y = i({ inst: { value: h, getSnapshot: p } }), v = y[0].inst, w = y[1];
    return s(
      function() {
        v.value = h, v.getSnapshot = p, u(v) && w({ inst: v });
      },
      [f, h, p]
    ), o(
      function() {
        return u(v) && w({ inst: v }), f(function() {
          u(v) && w({ inst: v });
        });
      },
      [f]
    ), a(h), h;
  }
  function u(f) {
    var p = f.getSnapshot;
    f = f.value;
    try {
      var h = p();
      return !n(f, h);
    } catch {
      return !0;
    }
  }
  function l(f, p) {
    return p();
  }
  var d = typeof window > "u" || typeof window.document > "u" || typeof window.document.createElement > "u" ? l : c;
  return vr.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : d, vr;
}
var Ya;
function $w() {
  return Ya || (Ya = 1, wr.exports = Tw()), wr.exports;
}
var qa;
function Pw() {
  if (qa) return xr;
  qa = 1;
  var e = Ke, t = $w();
  function n(l, d) {
    return l === d && (l !== 0 || 1 / l === 1 / d) || l !== l && d !== d;
  }
  var i = typeof Object.is == "function" ? Object.is : n, o = t.useSyncExternalStore, s = e.useRef, a = e.useEffect, c = e.useMemo, u = e.useDebugValue;
  return xr.useSyncExternalStoreWithSelector = function(l, d, f, p, h) {
    var y = s(null);
    if (y.current === null) {
      var v = { hasValue: !1, value: null };
      y.current = v;
    } else v = y.current;
    y = c(
      function() {
        function x(N) {
          if (!b) {
            if (b = !0, g = N, N = p(N), h !== void 0 && v.hasValue) {
              var S = v.value;
              if (h(S, N))
                return m = S;
            }
            return m = N;
          }
          if (S = m, i(g, N)) return S;
          var k = p(N);
          return h !== void 0 && h(S, k) ? (g = N, S) : (g = N, m = k);
        }
        var b = !1, g, m, j = f === void 0 ? null : f;
        return [
          function() {
            return x(d());
          },
          j === null ? void 0 : function() {
            return x(j());
          }
        ];
      },
      [d, f, p, h]
    );
    var w = o(l, y[0], y[1]);
    return a(
      function() {
        v.hasValue = !0, v.value = w;
      },
      [w]
    ), u(w), w;
  }, xr;
}
var Ua;
function Mw() {
  return Ua || (Ua = 1, mr.exports = Pw()), mr.exports;
}
var Rw = Mw();
const zw = /* @__PURE__ */ _f(Rw), Lw = {}, Za = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), i = (d, f) => {
    const p = typeof d == "function" ? d(t) : d;
    if (!Object.is(p, t)) {
      const h = t;
      t = f ?? (typeof p != "object" || p === null) ? p : Object.assign({}, t, p), n.forEach((y) => y(t, h));
    }
  }, o = () => t, u = { setState: i, getState: o, getInitialState: () => l, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (Lw ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(i, o, u);
  return u;
}, Vw = (e) => e ? Za(e) : Za, { useDebugValue: Ow } = Ke, { useSyncExternalStoreWithSelector: Hw } = zw, Ww = (e) => e;
function Hu(e, t = Ww, n) {
  const i = Hw(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return Ow(i), i;
}
const Ga = (e, t) => {
  const n = Vw(e), i = (o, s = t) => Hu(n, o, s);
  return Object.assign(i, n), i;
}, Bw = (e, t) => e ? Ga(e, t) : Ga;
function ve(e, t) {
  if (Object.is(e, t))
    return !0;
  if (typeof e != "object" || e === null || typeof t != "object" || t === null)
    return !1;
  if (e instanceof Map && t instanceof Map) {
    if (e.size !== t.size) return !1;
    for (const [i, o] of e)
      if (!Object.is(o, t.get(i)))
        return !1;
    return !0;
  }
  if (e instanceof Set && t instanceof Set) {
    if (e.size !== t.size) return !1;
    for (const i of e)
      if (!t.has(i))
        return !1;
    return !0;
  }
  const n = Object.keys(e);
  if (n.length !== Object.keys(t).length)
    return !1;
  for (const i of n)
    if (!Object.prototype.hasOwnProperty.call(t, i) || !Object.is(e[i], t[i]))
      return !1;
  return !0;
}
var br = { exports: {} }, Te = {};
var Ja;
function Fw() {
  if (Ja) return Te;
  Ja = 1;
  var e = Ke;
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
  var i = {
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
  }, o = /* @__PURE__ */ Symbol.for("react.portal");
  function s(u, l, d) {
    var f = 3 < arguments.length && arguments[3] !== void 0 ? arguments[3] : null;
    return {
      $$typeof: o,
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
  return Te.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = i, Te.createPortal = function(u, l) {
    var d = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!l || l.nodeType !== 1 && l.nodeType !== 9 && l.nodeType !== 11)
      throw Error(t(299));
    return s(u, l, null, d);
  }, Te.flushSync = function(u) {
    var l = a.T, d = i.p;
    try {
      if (a.T = null, i.p = 2, u) return u();
    } finally {
      a.T = l, i.p = d, i.d.f();
    }
  }, Te.preconnect = function(u, l) {
    typeof u == "string" && (l ? (l = l.crossOrigin, l = typeof l == "string" ? l === "use-credentials" ? l : "" : void 0) : l = null, i.d.C(u, l));
  }, Te.prefetchDNS = function(u) {
    typeof u == "string" && i.d.D(u);
  }, Te.preinit = function(u, l) {
    if (typeof u == "string" && l && typeof l.as == "string") {
      var d = l.as, f = c(d, l.crossOrigin), p = typeof l.integrity == "string" ? l.integrity : void 0, h = typeof l.fetchPriority == "string" ? l.fetchPriority : void 0;
      d === "style" ? i.d.S(
        u,
        typeof l.precedence == "string" ? l.precedence : void 0,
        {
          crossOrigin: f,
          integrity: p,
          fetchPriority: h
        }
      ) : d === "script" && i.d.X(u, {
        crossOrigin: f,
        integrity: p,
        fetchPriority: h,
        nonce: typeof l.nonce == "string" ? l.nonce : void 0
      });
    }
  }, Te.preinitModule = function(u, l) {
    if (typeof u == "string")
      if (typeof l == "object" && l !== null) {
        if (l.as == null || l.as === "script") {
          var d = c(
            l.as,
            l.crossOrigin
          );
          i.d.M(u, {
            crossOrigin: d,
            integrity: typeof l.integrity == "string" ? l.integrity : void 0,
            nonce: typeof l.nonce == "string" ? l.nonce : void 0
          });
        }
      } else l == null && i.d.M(u);
  }, Te.preload = function(u, l) {
    if (typeof u == "string" && typeof l == "object" && l !== null && typeof l.as == "string") {
      var d = l.as, f = c(d, l.crossOrigin);
      i.d.L(u, d, {
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
  }, Te.preloadModule = function(u, l) {
    if (typeof u == "string")
      if (l) {
        var d = c(l.as, l.crossOrigin);
        i.d.m(u, {
          as: typeof l.as == "string" && l.as !== "script" ? l.as : void 0,
          crossOrigin: d,
          integrity: typeof l.integrity == "string" ? l.integrity : void 0
        });
      } else i.d.m(u);
  }, Te.requestFormReset = function(u) {
    i.d.r(u);
  }, Te.unstable_batchedUpdates = function(u, l) {
    return u(l);
  }, Te.useFormState = function(u, l, d) {
    return a.H.useFormState(u, l, d);
  }, Te.useFormStatus = function() {
    return a.H.useHostTransitionStatus();
  }, Te.version = "19.2.7", Te;
}
var Qa;
function Kw() {
  if (Qa) return br.exports;
  Qa = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), br.exports = Fw(), br.exports;
}
var Xw = Kw();
const xo = Gr(null), Yw = xo.Provider, Wu = Ye.error001("react");
function he(e, t) {
  const n = Yn(xo);
  if (n === null)
    throw new Error(Wu);
  return Hu(n, e, t);
}
function be() {
  const e = Yn(xo);
  if (e === null)
    throw new Error(Wu);
  return de(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const ec = { display: "none" }, qw = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, Bu = "react-flow__node-desc", Fu = "react-flow__edge-desc", Uw = "react-flow__aria-live", Zw = (e) => e.ariaLiveMessage, Gw = (e) => e.ariaLabelConfig;
function Jw({ rfId: e }) {
  const t = he(Zw);
  return r.jsx("div", { id: `${Uw}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: qw, children: t });
}
function Qw({ rfId: e, disableKeyboardA11y: t }) {
  const n = he(Gw);
  return r.jsxs(r.Fragment, { children: [r.jsx("div", { id: `${Bu}-${e}`, style: ec, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), r.jsx("div", { id: `${Fu}-${e}`, style: ec, children: n["edge.a11yDescription.default"] }), !t && r.jsx(Jw, { rfId: e })] });
}
const wo = Yc(({ position: e = "top-left", children: t, className: n, style: i, ...o }, s) => {
  const a = `${e}`.split("-");
  return r.jsx("div", { className: ke(["react-flow__panel", n, ...a]), style: i, ref: s, ...o, children: t });
});
wo.displayName = "Panel";
function ev({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : r.jsx(wo, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: r.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const tv = (e) => {
  const t = [], n = [];
  for (const [, i] of e.nodeLookup)
    i.selected && t.push(i.internals.userNode);
  for (const [, i] of e.edgeLookup)
    i.selected && n.push(i);
  return { selectedNodes: t, selectedEdges: n };
}, Ei = (e) => e.id;
function nv(e, t) {
  return ve(e.selectedNodes.map(Ei), t.selectedNodes.map(Ei)) && ve(e.selectedEdges.map(Ei), t.selectedEdges.map(Ei));
}
function iv({ onSelectionChange: e }) {
  const t = be(), { selectedNodes: n, selectedEdges: i } = he(tv, nv);
  return Q(() => {
    const o = { nodes: n, edges: i };
    e?.(o), t.getState().onSelectionChangeHandlers.forEach((s) => s(o));
  }, [n, i, e]), null;
}
const ov = (e) => !!e.onSelectionChangeHandlers;
function rv({ onSelectionChange: e }) {
  const t = he(ov);
  return e || t ? r.jsx(iv, { onSelectionChange: e }) : null;
}
const Ku = [0, 0], sv = { x: 0, y: 0, zoom: 1 }, av = [
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
], tc = [...av, "rfId"], cv = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), nc = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: Wn,
  nodeOrigin: Ku,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function lv(e) {
  const { setNodes: t, setEdges: n, setMinZoom: i, setMaxZoom: o, setTranslateExtent: s, setNodeExtent: a, reset: c, setDefaultNodesAndEdges: u } = he(cv, ve), l = be();
  Q(() => (u(e.defaultNodes, e.defaultEdges), () => {
    d.current = nc, c();
  }), []);
  const d = oe(nc);
  return Q(
    () => {
      for (const f of tc) {
        const p = e[f], h = d.current[f];
        p !== h && (typeof e[f] > "u" || (f === "nodes" ? t(p) : f === "edges" ? n(p) : f === "minZoom" ? i(p) : f === "maxZoom" ? o(p) : f === "translateExtent" ? s(p) : f === "nodeExtent" ? a(p) : f === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: Fx(p) }) : f === "fitView" ? l.setState({ fitViewQueued: p }) : f === "fitViewOptions" ? l.setState({ fitViewOptions: p }) : l.setState({ [f]: p })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    tc.map((f) => e[f])
  ), null;
}
function ic() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function uv(e) {
  const [t, n] = K(e === "system" ? null : e);
  return Q(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const i = ic(), o = () => n(i?.matches ? "dark" : "light");
    return o(), i?.addEventListener("change", o), () => {
      i?.removeEventListener("change", o);
    };
  }, [e]), t !== null ? t : ic()?.matches ? "dark" : "light";
}
const oc = typeof document < "u" ? document : null;
function Xn(e = null, t = { target: oc, actInsideInputWithModifier: !0 }) {
  const [n, i] = K(!1), o = oe(!1), s = oe(/* @__PURE__ */ new Set([])), [a, c] = de(() => {
    if (e !== null) {
      const l = (Array.isArray(e) ? e : [e]).filter((f) => typeof f == "string").map((f) => f.replace("+", `
`).replace(`

`, `
+`).split(`
`)), d = l.reduce((f, p) => f.concat(...p), []);
      return [l, d];
    }
    return [[], []];
  }, [e]);
  return Q(() => {
    const u = t?.target ?? oc, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (h) => {
        if (o.current = h.ctrlKey || h.metaKey || h.shiftKey || h.altKey, (!o.current || o.current && !l) && ju(h))
          return !1;
        const v = sc(h.code, c);
        if (s.current.add(h[v]), rc(a, s.current, !1)) {
          const w = h.composedPath?.()?.[0] || h.target, x = w?.nodeName === "BUTTON" || w?.nodeName === "A";
          t.preventDefault !== !1 && (o.current || !x) && h.preventDefault(), i(!0);
        }
      }, f = (h) => {
        const y = sc(h.code, c);
        rc(a, s.current, !0) ? (i(!1), s.current.clear()) : s.current.delete(h[y]), h.key === "Meta" && s.current.clear(), o.current = !1;
      }, p = () => {
        s.current.clear(), i(!1);
      };
      return u?.addEventListener("keydown", d), u?.addEventListener("keyup", f), window.addEventListener("blur", p), window.addEventListener("contextmenu", p), () => {
        u?.removeEventListener("keydown", d), u?.removeEventListener("keyup", f), window.removeEventListener("blur", p), window.removeEventListener("contextmenu", p);
      };
    }
  }, [e, i]), n;
}
function rc(e, t, n) {
  return e.filter((i) => n || i.length === t.size).some((i) => i.every((o) => t.has(o)));
}
function sc(e, t) {
  return t.includes(e) ? "code" : "key";
}
const dv = () => {
  const e = be();
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
      const { panZoom: i } = e.getState();
      return i ? i.scaleTo(t, n) : !1;
    },
    getZoom: () => e.getState().transform[2],
    setViewport: async (t, n) => {
      const { transform: [i, o, s], panZoom: a } = e.getState();
      return a ? (await a.setViewport({
        x: t.x ?? i,
        y: t.y ?? o,
        zoom: t.zoom ?? s
      }, n), !0) : !1;
    },
    getViewport: () => {
      const [t, n, i] = e.getState().transform;
      return { x: t, y: n, zoom: i };
    },
    setCenter: async (t, n, i) => e.getState().setCenter(t, n, i),
    fitBounds: async (t, n) => {
      const { width: i, height: o, minZoom: s, maxZoom: a, panZoom: c } = e.getState(), u = Ns(t, i, o, s, a, n?.padding ?? 0.1);
      return c ? (await c.setViewport(u, {
        duration: n?.duration,
        ease: n?.ease,
        interpolate: n?.interpolate
      }), !0) : !1;
    },
    screenToFlowPosition: (t, n = {}) => {
      const { transform: i, snapGrid: o, snapToGrid: s, domNode: a } = e.getState();
      if (!a)
        return t;
      const { x: c, y: u } = a.getBoundingClientRect(), l = {
        x: t.x - c,
        y: t.y - u
      }, d = n.snapGrid ?? o, f = n.snapToGrid ?? s;
      return pn(l, i, f, d);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: i } = e.getState();
      if (!i)
        return t;
      const { x: o, y: s } = i.getBoundingClientRect(), a = sn(t, n);
      return {
        x: a.x + o,
        y: a.y + s
      };
    }
  }), []);
};
function Xu(e, t) {
  const n = [], i = /* @__PURE__ */ new Map(), o = [];
  for (const s of e)
    if (s.type === "add") {
      o.push(s);
      continue;
    } else if (s.type === "remove" || s.type === "replace")
      i.set(s.id, [s]);
    else {
      const a = i.get(s.id);
      a ? a.push(s) : i.set(s.id, [s]);
    }
  for (const s of t) {
    const a = i.get(s.id);
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
      fv(u, c);
    n.push(c);
  }
  return o.length && o.forEach((s) => {
    s.index !== void 0 ? n.splice(s.index, 0, { ...s.item }) : n.push({ ...s.item });
  }), n;
}
function fv(e, t) {
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
function Yu(e, t) {
  return Xu(e, t);
}
function qu(e, t) {
  return Xu(e, t);
}
function Nt(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function Xt(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const i = [];
  for (const [o, s] of e) {
    const a = t.has(o);
    !(s.selected === void 0 && !a) && s.selected !== a && (n && (s.selected = a), i.push(Nt(s.id, a)));
  }
  return i;
}
function ac({ items: e = [], lookup: t }) {
  const n = [], i = new Map(e.map((o) => [o.id, o]));
  for (const [o, s] of e.entries()) {
    const a = t.get(s.id), c = a?.internals?.userNode ?? a;
    c !== void 0 && c !== s && n.push({ id: s.id, item: s, type: "replace" }), c === void 0 && n.push({ item: s, type: "add", index: o });
  }
  for (const [o] of t)
    i.get(o) === void 0 && n.push({ id: o, type: "remove" });
  return n;
}
function cc(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const Uu = wu();
function Zu(e, t, n = {}) {
  return Ux(e, t, {
    ...n,
    onError: n.onError ?? Uu
  });
}
function pv(e, t, n, i = { shouldReplaceId: !0 }) {
  return Zx(e, t, n, {
    ...i,
    onError: i.onError ?? Uu
  });
}
const lc = (e) => Mx(e), hv = (e) => gu(e);
function Gu(e) {
  return Yc(e);
}
const gv = typeof window < "u" ? df : Q;
function uc(e) {
  const [t, n] = K(BigInt(0)), [i] = K(() => yv(() => n((o) => o + BigInt(1))));
  return gv(() => {
    const o = i.get();
    o.length && (e(o), i.reset());
  }, [t]), i;
}
function yv(e) {
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
const Ju = Gr(null);
function mv({ children: e }) {
  const t = be(), n = se((c) => {
    const { nodes: u = [], setNodes: l, hasDefaultNodes: d, onNodesChange: f, nodeLookup: p, fitViewQueued: h, onNodesChangeMiddlewareMap: y } = t.getState();
    let v = u;
    for (const x of c)
      v = typeof x == "function" ? x(v) : x;
    let w = ac({
      items: v,
      lookup: p
    });
    for (const x of y.values())
      w = x(w);
    d && l(v), w.length > 0 ? f?.(w) : h && window.requestAnimationFrame(() => {
      const { fitViewQueued: x, nodes: b, setNodes: g } = t.getState();
      x && g(b);
    });
  }, []), i = uc(n), o = se((c) => {
    const { edges: u = [], setEdges: l, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: p } = t.getState();
    let h = u;
    for (const y of c)
      h = typeof y == "function" ? y(h) : y;
    d ? l(h) : f && f(ac({
      items: h,
      lookup: p
    }));
  }, []), s = uc(o), a = de(() => ({ nodeQueue: i, edgeQueue: s }), []);
  return r.jsx(Ju.Provider, { value: a, children: e });
}
function xv() {
  const e = Yn(Ju);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const wv = (e) => !!e.panZoom;
function As() {
  const e = dv(), t = be(), n = xv(), i = he(wv), o = de(() => {
    const s = (f) => t.getState().nodeLookup.get(f), a = (f) => {
      n.nodeQueue.push(f);
    }, c = (f) => {
      n.edgeQueue.push(f);
    }, u = (f) => {
      const { nodeLookup: p, nodeOrigin: h } = t.getState(), y = lc(f) ? f : p.get(f.id), v = y.parentId ? bu(y.position, y.measured, y.parentId, p, h) : y.position, w = {
        ...y,
        position: v,
        width: y.measured?.width ?? y.width,
        height: y.measured?.height ?? y.height
      };
      return rn(w);
    }, l = (f, p, h = { replace: !1 }) => {
      a((y) => y.map((v) => {
        if (v.id === f) {
          const w = typeof p == "function" ? p(v) : p;
          return h.replace && lc(w) ? w : { ...v, ...w };
        }
        return v;
      }));
    }, d = (f, p, h = { replace: !1 }) => {
      c((y) => y.map((v) => {
        if (v.id === f) {
          const w = typeof p == "function" ? p(v) : p;
          return h.replace && hv(w) ? w : { ...v, ...w };
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
        return f.map((p) => ({ ...p }));
      },
      getEdge: (f) => t.getState().edgeLookup.get(f),
      setNodes: a,
      setEdges: c,
      addNodes: (f) => {
        const p = Array.isArray(f) ? f : [f];
        n.nodeQueue.push((h) => [...h, ...p]);
      },
      addEdges: (f) => {
        const p = Array.isArray(f) ? f : [f];
        n.edgeQueue.push((h) => [...h, ...p]);
      },
      toObject: () => {
        const { nodes: f = [], edges: p = [], transform: h } = t.getState(), [y, v, w] = h;
        return {
          nodes: f.map((x) => ({ ...x })),
          edges: p.map((x) => ({ ...x })),
          viewport: {
            x: y,
            y: v,
            zoom: w
          }
        };
      },
      deleteElements: async ({ nodes: f = [], edges: p = [] }) => {
        const { nodes: h, edges: y, onNodesDelete: v, onEdgesDelete: w, triggerNodeChanges: x, triggerEdgeChanges: b, onDelete: g, onBeforeDelete: m } = t.getState(), { nodes: j, edges: N } = await Ox({
          nodesToRemove: f,
          edgesToRemove: p,
          nodes: h,
          edges: y,
          onBeforeDelete: m
        }), S = N.length > 0, k = j.length > 0;
        if (S) {
          const _ = N.map(cc);
          w?.(N), b(_);
        }
        if (k) {
          const _ = j.map(cc);
          v?.(j), x(_);
        }
        return (k || S) && g?.({ nodes: j, edges: N }), { deletedNodes: j, deletedEdges: N };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, p = !0, h) => {
        const y = $a(f), v = y ? f : u(f), w = h !== void 0;
        return v ? (h || t.getState().nodes).filter((x) => {
          const b = t.getState().nodeLookup.get(x.id);
          if (b && !y && (x.id === f.id || !b.internals.positionAbsolute))
            return !1;
          const g = rn(w ? x : b), m = Fn(g, v);
          return p && m > 0 || m >= g.width * g.height || m >= v.width * v.height;
        }) : [];
      },
      isNodeIntersecting: (f, p, h = !0) => {
        const v = $a(f) ? f : u(f);
        if (!v)
          return !1;
        const w = Fn(v, p);
        return h && w > 0 || w >= p.width * p.height || w >= v.width * v.height;
      },
      updateNode: l,
      updateNodeData: (f, p, h = { replace: !1 }) => {
        l(f, (y) => {
          const v = typeof p == "function" ? p(y) : p;
          return h.replace ? { ...y, data: v } : { ...y, data: { ...y.data, ...v } };
        }, h);
      },
      updateEdge: d,
      updateEdgeData: (f, p, h = { replace: !1 }) => {
        d(f, (y) => {
          const v = typeof p == "function" ? p(y) : p;
          return h.replace ? { ...y, data: v } : { ...y, data: { ...y.data, ...v } };
        }, h);
      },
      getNodesBounds: (f) => {
        const { nodeLookup: p, nodeOrigin: h } = t.getState();
        return Rx(f, { nodeLookup: p, nodeOrigin: h });
      },
      getHandleConnections: ({ type: f, id: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}-${f}${p ? `-${p}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}${f ? p ? `-${f}-${p}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const p = t.getState().fitViewResolver ?? Bx();
        return t.setState({ fitViewQueued: !0, fitViewOptions: f, fitViewResolver: p }), n.nodeQueue.push((h) => [...h]), p.promise;
      }
    };
  }, []);
  return de(() => ({
    ...o,
    ...e,
    viewportInitialized: i
  }), [i]);
}
const dc = (e) => e.selected, vv = typeof window < "u" ? window : void 0;
function bv({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = be(), { deleteElements: i } = As(), o = Xn(e, { actInsideInputWithModifier: !1 }), s = Xn(t, { target: vv });
  Q(() => {
    if (o) {
      const { edges: a, nodes: c } = n.getState();
      i({ nodes: c.filter(dc), edges: a.filter(dc) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [o]), Q(() => {
    n.setState({ multiSelectionActive: s });
  }, [s]);
}
function Nv(e) {
  const t = be();
  Q(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const i = js(e.current);
      (i.height === 0 || i.width === 0) && t.getState().onError?.("004", Ye.error004()), t.setState({ width: i.width || 500, height: i.height || 500 });
    };
    if (e.current) {
      n(), window.addEventListener("resize", n);
      const i = new ResizeObserver(() => n());
      return i.observe(e.current), () => {
        window.removeEventListener("resize", n), i && e.current && i.unobserve(e.current);
      };
    }
  }, []);
}
const vo = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, jv = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function Sv({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: i = !1, panOnScrollSpeed: o = 0.5, panOnScrollMode: s = Ct.Free, zoomOnDoubleClick: a = !0, panOnDrag: c = !0, defaultViewport: u, translateExtent: l, minZoom: d, maxZoom: f, zoomActivationKeyCode: p, preventScrolling: h = !0, children: y, noWheelClassName: v, noPanClassName: w, onViewportChange: x, isControlledViewport: b, paneClickDistance: g, selectionOnDrag: m }) {
  const j = be(), N = oe(null), { userSelectionActive: S, lib: k, connectionInProgress: _ } = he(jv, ve), M = Xn(p), I = oe();
  Nv(N);
  const $ = se((L) => {
    x?.({ x: L[0], y: L[1], zoom: L[2] }), b || j.setState({ transform: L });
  }, [x, b]);
  return Q(() => {
    if (N.current) {
      I.current = Ew({
        domNode: N.current,
        minZoom: d,
        maxZoom: f,
        translateExtent: l,
        viewport: u,
        onDraggingChange: (E) => j.setState((D) => D.paneDragging === E ? D : { paneDragging: E }),
        onPanZoomStart: (E, D) => {
          const { onViewportChangeStart: P, onMoveStart: T } = j.getState();
          T?.(E, D), P?.(D);
        },
        onPanZoom: (E, D) => {
          const { onViewportChange: P, onMove: T } = j.getState();
          T?.(E, D), P?.(D);
        },
        onPanZoomEnd: (E, D) => {
          const { onViewportChangeEnd: P, onMoveEnd: T } = j.getState();
          T?.(E, D), P?.(D);
        }
      });
      const { x: L, y: C, zoom: A } = I.current.getViewport();
      return j.setState({
        panZoom: I.current,
        transform: [L, C, A],
        domNode: N.current.closest(".react-flow")
      }), () => {
        I.current?.destroy();
      };
    }
  }, []), Q(() => {
    I.current?.update({
      onPaneContextMenu: e,
      zoomOnScroll: t,
      zoomOnPinch: n,
      panOnScroll: i,
      panOnScrollSpeed: o,
      panOnScrollMode: s,
      zoomOnDoubleClick: a,
      panOnDrag: c,
      zoomActivationKeyPressed: M,
      preventScrolling: h,
      noPanClassName: w,
      userSelectionActive: S,
      noWheelClassName: v,
      lib: k,
      onTransformChange: $,
      connectionInProgress: _,
      selectionOnDrag: m,
      paneClickDistance: g
    });
  }, [
    e,
    t,
    n,
    i,
    o,
    s,
    a,
    c,
    M,
    h,
    w,
    S,
    v,
    k,
    $,
    _,
    m,
    g
  ]), r.jsx("div", { className: "react-flow__renderer", ref: N, style: vo, children: y });
}
const Cv = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function Ev() {
  const { userSelectionActive: e, userSelectionRect: t } = he(Cv, ve);
  return e && t ? r.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const Nr = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, kv = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function Iv({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Bn.Full, panOnDrag: i, autoPanOnSelection: o, paneClickDistance: s, selectionOnDrag: a, onSelectionStart: c, onSelectionEnd: u, onPaneClick: l, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: p, onPaneMouseMove: h, onPaneMouseLeave: y, children: v }) {
  const w = oe(0), x = be(), { userSelectionActive: b, elementsSelectable: g, dragging: m, connectionInProgress: j, panBy: N, autoPanSpeed: S } = he(kv, ve), k = g && (e || b), _ = oe(null), M = oe(), I = oe(/* @__PURE__ */ new Set()), $ = oe(/* @__PURE__ */ new Set()), L = oe(!1), C = oe({ x: 0, y: 0 }), A = oe(!1), E = (R) => {
    if (L.current || j) {
      L.current = !1;
      return;
    }
    l?.(R), x.getState().resetSelectedElements(), x.setState({ nodesSelectionActive: !1 });
  }, D = (R) => {
    if (Array.isArray(i) && i?.includes(2)) {
      R.preventDefault();
      return;
    }
    d?.(R);
  }, P = f ? (R) => f(R) : void 0, T = (R) => {
    L.current && (R.stopPropagation(), L.current = !1);
  }, B = (R) => {
    const { domNode: X, transform: le } = x.getState();
    if (M.current = X?.getBoundingClientRect(), !M.current)
      return;
    const ce = R.target === _.current;
    if (!ce && !!R.target.closest(".nokey") || !e || !(a && ce || t) || R.button !== 0 || !R.isPrimary)
      return;
    R.target?.setPointerCapture?.(R.pointerId), L.current = !1;
    const { x: fe, y: O } = Qe(R.nativeEvent, M.current), te = pn({ x: fe, y: O }, le);
    x.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: te.x,
        startY: te.y,
        x: fe,
        y: O
      }
    }), ce || (R.stopPropagation(), R.preventDefault());
  };
  function W(R, X) {
    const { userSelectionRect: le } = x.getState();
    if (!le)
      return;
    const { transform: ce, nodeLookup: ee, edgeLookup: ne, connectionLookup: fe, triggerNodeChanges: O, triggerEdgeChanges: te, defaultEdgeOptions: ge } = x.getState(), ye = { x: le.startX, y: le.startY }, { x: _e, y: De } = sn(ye, ce), $e = {
      startX: ye.x,
      startY: ye.y,
      x: R < _e ? R : _e,
      y: X < De ? X : De,
      width: Math.abs(R - _e),
      height: Math.abs(X - De)
    }, st = I.current, qe = $.current;
    I.current = new Set(vs(ee, $e, ce, n === Bn.Partial, !0).map((Pe) => Pe.id)), $.current = /* @__PURE__ */ new Set();
    const Ue = ge?.selectable ?? !0;
    for (const Pe of I.current) {
      const He = fe.get(Pe);
      if (He)
        for (const { edgeId: We } of He.values()) {
          const Ne = ne.get(We);
          Ne && (Ne.selectable ?? Ue) && $.current.add(We);
        }
    }
    if (!Pa(st, I.current)) {
      const Pe = Xt(ee, I.current, !0);
      O(Pe);
    }
    if (!Pa(qe, $.current)) {
      const Pe = Xt(ne, $.current);
      te(Pe);
    }
    x.setState({
      userSelectionRect: $e,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function H() {
    if (!o || !M.current)
      return;
    const [R, X] = bs(C.current, M.current, S);
    N({ x: R, y: X }).then((le) => {
      if (!L.current || !le) {
        w.current = requestAnimationFrame(H);
        return;
      }
      const { x: ce, y: ee } = C.current;
      W(ce, ee), w.current = requestAnimationFrame(H);
    });
  }
  const q = () => {
    cancelAnimationFrame(w.current), w.current = 0, A.current = !1;
  };
  Q(() => () => q(), []);
  const V = (R) => {
    const { userSelectionRect: X, transform: le, resetSelectedElements: ce } = x.getState();
    if (!M.current || !X)
      return;
    const { x: ee, y: ne } = Qe(R.nativeEvent, M.current);
    C.current = { x: ee, y: ne };
    const fe = sn({ x: X.startX, y: X.startY }, le);
    if (!L.current) {
      const O = t ? 0 : s;
      if (Math.hypot(ee - fe.x, ne - fe.y) <= O)
        return;
      ce(), c?.(R);
    }
    L.current = !0, A.current || (H(), A.current = !0), W(ee, ne);
  }, Y = (R) => {
    R.button === 0 && (R.target?.releasePointerCapture?.(R.pointerId), !b && R.target === _.current && x.getState().userSelectionRect && E?.(R), x.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), L.current && (u?.(R), x.setState({
      nodesSelectionActive: I.current.size > 0
    })), q());
  }, J = (R) => {
    R.target?.releasePointerCapture?.(R.pointerId), q();
  }, Z = i === !0 || Array.isArray(i) && i.includes(0);
  return r.jsxs("div", { className: ke(["react-flow__pane", { draggable: Z, dragging: m, selection: e }]), onClick: k ? void 0 : Nr(E, _), onContextMenu: Nr(D, _), onWheel: Nr(P, _), onPointerEnter: k ? void 0 : p, onPointerMove: k ? V : h, onPointerUp: k ? Y : void 0, onPointerCancel: k ? J : void 0, onPointerDownCapture: k ? B : void 0, onClickCapture: k ? T : void 0, onPointerLeave: y, ref: _, style: vo, children: [v, r.jsx(Ev, {})] });
}
function Wr({ id: e, store: t, unselect: n = !1, nodeRef: i }) {
  const { addSelectedNodes: o, unselectNodesAndEdges: s, multiSelectionActive: a, nodeLookup: c, onError: u } = t.getState(), l = c.get(e);
  if (!l) {
    u?.("012", Ye.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && a) && (s({ nodes: [l], edges: [] }), requestAnimationFrame(() => i?.current?.blur())) : o([e]);
}
function Qu({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: i, nodeId: o, isSelectable: s, nodeClickDistance: a }) {
  const c = be(), [u, l] = K(!1), d = oe();
  return Q(() => {
    d.current = fw({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (f) => {
        Wr({
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
  }, []), Q(() => {
    if (!(t || !e.current || !d.current))
      return d.current.update({
        noDragClassName: n,
        handleSelector: i,
        domNode: e.current,
        isSelectable: s,
        nodeId: o,
        nodeClickDistance: a
      }), () => {
        d.current?.destroy();
      };
  }, [n, i, t, s, e, o, a]), u;
}
const Av = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function ed() {
  const e = be();
  return se((n) => {
    const { nodeExtent: i, snapToGrid: o, snapGrid: s, nodesDraggable: a, onError: c, updateNodePositions: u, nodeLookup: l, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), p = Av(a), h = o ? s[0] : 5, y = o ? s[1] : 5, v = n.direction.x * h * n.factor, w = n.direction.y * y * n.factor;
    for (const [, x] of l) {
      if (!p(x))
        continue;
      let b = {
        x: x.internals.positionAbsolute.x + v,
        y: x.internals.positionAbsolute.y + w
      };
      o && (b = ti(b, s));
      const { position: g, positionAbsolute: m } = yu({
        nodeId: x.id,
        nextPosition: b,
        nodeLookup: l,
        nodeExtent: i,
        nodeOrigin: d,
        onError: c
      });
      x.position = g, x.internals.positionAbsolute = m, f.set(x.id, x);
    }
    u(f);
  }, []);
}
const _s = Gr(null), _v = _s.Provider;
_s.Consumer;
const td = () => Yn(_s), Dv = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), Tv = (e, t, n) => (i) => {
  const { connectionClickStartHandle: o, connectionMode: s, connection: a } = i, { fromHandle: c, toHandle: u, isValid: l } = a, d = u?.nodeId === e && u?.id === t && u?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: d,
    clickConnecting: o?.nodeId === e && o?.id === t && o?.type === n,
    isPossibleEndHandle: s === nn.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!o,
    valid: d && l
  };
};
function $v({ type: e = "source", position: t = re.Top, isValidConnection: n, isConnectable: i = !0, isConnectableStart: o = !0, isConnectableEnd: s = !0, id: a, onConnect: c, children: u, className: l, onMouseDown: d, onTouchStart: f, ...p }, h) {
  const y = a || null, v = e === "target", w = be(), x = td(), { connectOnClick: b, noPanClassName: g, rfId: m } = he(Dv, ve), { connectingFrom: j, connectingTo: N, clickConnecting: S, isPossibleEndHandle: k, connectionInProcess: _, clickConnectionInProcess: M, valid: I } = he(Tv(x, y, e), ve);
  x || w.getState().onError?.("010", Ye.error010());
  const $ = (A) => {
    const { defaultEdgeOptions: E, onConnect: D, hasDefaultEdges: P } = w.getState(), T = {
      ...E,
      ...A
    };
    if (P) {
      const { edges: B, setEdges: W, onError: H } = w.getState();
      W(Zu(T, B, { onError: H }));
    }
    D?.(T), c?.(T);
  }, L = (A) => {
    if (!x)
      return;
    const E = Su(A.nativeEvent);
    if (o && (E && A.button === 0 || !E)) {
      const D = w.getState();
      Hr.onPointerDown(A.nativeEvent, {
        handleDomNode: A.currentTarget,
        autoPanOnConnect: D.autoPanOnConnect,
        connectionMode: D.connectionMode,
        connectionRadius: D.connectionRadius,
        domNode: D.domNode,
        nodeLookup: D.nodeLookup,
        lib: D.lib,
        isTarget: v,
        handleId: y,
        nodeId: x,
        flowId: D.rfId,
        panBy: D.panBy,
        cancelConnection: D.cancelConnection,
        onConnectStart: D.onConnectStart,
        onConnectEnd: (...P) => w.getState().onConnectEnd?.(...P),
        updateConnection: D.updateConnection,
        onConnect: $,
        isValidConnection: n || ((...P) => w.getState().isValidConnection?.(...P) ?? !0),
        getTransform: () => w.getState().transform,
        getFromHandle: () => w.getState().connection.fromHandle,
        autoPanSpeed: D.autoPanSpeed,
        dragThreshold: D.connectionDragThreshold
      });
    }
    E ? d?.(A) : f?.(A);
  }, C = (A) => {
    const { onClickConnectStart: E, onClickConnectEnd: D, connectionClickStartHandle: P, connectionMode: T, isValidConnection: B, lib: W, rfId: H, nodeLookup: q, connection: V } = w.getState();
    if (!x || !P && !o)
      return;
    if (!P) {
      E?.(A.nativeEvent, { nodeId: x, handleId: y, handleType: e }), w.setState({ connectionClickStartHandle: { nodeId: x, type: e, id: y } });
      return;
    }
    const Y = Nu(A.target), J = n || B, { connection: Z, isValid: R } = Hr.isValid(A.nativeEvent, {
      handle: {
        nodeId: x,
        id: y,
        type: e
      },
      connectionMode: T,
      fromNodeId: P.nodeId,
      fromHandleId: P.id || null,
      fromType: P.type,
      isValidConnection: J,
      flowId: H,
      doc: Y,
      lib: W,
      nodeLookup: q
    });
    R && Z && $(Z);
    const X = structuredClone(V);
    delete X.inProgress, X.toPosition = X.toHandle ? X.toHandle.position : null, D?.(A, X), w.setState({ connectionClickStartHandle: null });
  };
  return r.jsx("div", { "data-handleid": y, "data-nodeid": x, "data-handlepos": t, "data-id": `${m}-${x}-${y}-${e}`, className: ke([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    g,
    l,
    {
      source: !v,
      target: v,
      connectable: i,
      connectablestart: o,
      connectableend: s,
      clickconnecting: S,
      connectingfrom: j,
      connectingto: N,
      valid: I,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: i && (!_ || k) && (_ || M ? s : o)
    }
  ]), onMouseDown: L, onTouchStart: L, onClick: b ? C : void 0, ref: h, ...p, children: u });
}
const cn = Ee(Gu($v));
function Pv({ data: e, isConnectable: t, sourcePosition: n = re.Bottom }) {
  return r.jsxs(r.Fragment, { children: [e?.label, r.jsx(cn, { type: "source", position: n, isConnectable: t })] });
}
function Mv({ data: e, isConnectable: t, targetPosition: n = re.Top, sourcePosition: i = re.Bottom }) {
  return r.jsxs(r.Fragment, { children: [r.jsx(cn, { type: "target", position: n, isConnectable: t }), e?.label, r.jsx(cn, { type: "source", position: i, isConnectable: t })] });
}
function Rv() {
  return null;
}
function zv({ data: e, isConnectable: t, targetPosition: n = re.Top }) {
  return r.jsxs(r.Fragment, { children: [r.jsx(cn, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const no = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, fc = {
  input: Pv,
  default: Mv,
  output: zv,
  group: Rv
};
function Lv(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const Vv = (e) => {
  const { width: t, height: n, x: i, y: o } = ei(e.nodeLookup, {
    filter: (s) => !!s.selected
  });
  return {
    width: Je(t) ? t : null,
    height: Je(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${i}px,${o}px)`
  };
};
function Ov({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const i = be(), { width: o, height: s, transformString: a, userSelectionActive: c } = he(Vv, ve), u = ed(), l = oe(null);
  Q(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !c && o !== null && s !== null;
  if (Qu({
    nodeRef: l,
    disabled: !d
  }), !d)
    return null;
  const f = e ? (h) => {
    const y = i.getState().nodes.filter((v) => v.selected);
    e(h, y);
  } : void 0, p = (h) => {
    Object.prototype.hasOwnProperty.call(no, h.key) && (h.preventDefault(), u({
      direction: no[h.key],
      factor: h.shiftKey ? 4 : 1
    }));
  };
  return r.jsx("div", { className: ke(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: a
  }, children: r.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : p, style: {
    width: o,
    height: s
  } }) });
}
const pc = typeof window < "u" ? window : void 0, Hv = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function nd({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: i, onPaneMouseLeave: o, onPaneContextMenu: s, onPaneScroll: a, paneClickDistance: c, deleteKeyCode: u, selectionKeyCode: l, selectionOnDrag: d, selectionMode: f, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: y, panActivationKeyCode: v, zoomActivationKeyCode: w, elementsSelectable: x, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: m, panOnScrollSpeed: j, panOnScrollMode: N, zoomOnDoubleClick: S, panOnDrag: k, autoPanOnSelection: _, defaultViewport: M, translateExtent: I, minZoom: $, maxZoom: L, preventScrolling: C, onSelectionContextMenu: A, noWheelClassName: E, noPanClassName: D, disableKeyboardA11y: P, onViewportChange: T, isControlledViewport: B }) {
  const { nodesSelectionActive: W, userSelectionActive: H } = he(Hv, ve), q = Xn(l, { target: pc }), V = Xn(v, { target: pc }), Y = V || k, J = V || m, Z = d && Y !== !0, R = q || H || Z;
  return bv({ deleteKeyCode: u, multiSelectionKeyCode: y }), r.jsx(Sv, { onPaneContextMenu: s, elementsSelectable: x, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: J, panOnScrollSpeed: j, panOnScrollMode: N, zoomOnDoubleClick: S, panOnDrag: !q && Y, defaultViewport: M, translateExtent: I, minZoom: $, maxZoom: L, zoomActivationKeyCode: w, preventScrolling: C, noWheelClassName: E, noPanClassName: D, onViewportChange: T, isControlledViewport: B, paneClickDistance: c, selectionOnDrag: Z, children: r.jsxs(Iv, { onSelectionStart: p, onSelectionEnd: h, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: i, onPaneMouseLeave: o, onPaneContextMenu: s, onPaneScroll: a, panOnDrag: Y, autoPanOnSelection: _, isSelecting: !!R, selectionMode: f, selectionKeyPressed: q, paneClickDistance: c, selectionOnDrag: Z, children: [e, W && r.jsx(Ov, { onSelectionContextMenu: A, noPanClassName: D, disableKeyboardA11y: P })] }) });
}
nd.displayName = "FlowRenderer";
const Wv = Ee(nd), Bv = (e) => (t) => e ? vs(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function Fv(e) {
  return he(se(Bv(e), [e]), ve);
}
const Kv = (e) => e.updateNodeInternals;
function Xv() {
  const e = he(Kv), [t] = K(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
    const i = /* @__PURE__ */ new Map();
    n.forEach((o) => {
      const s = o.target.getAttribute("data-id");
      i.set(s, {
        id: s,
        nodeElement: o.target,
        force: !0
      });
    }), e(i);
  }));
  return Q(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function Yv({ node: e, nodeType: t, hasDimensions: n, resizeObserver: i }) {
  const o = be(), s = oe(null), a = oe(null), c = oe(e.sourcePosition), u = oe(e.targetPosition), l = oe(t), d = n && !!e.internals.handleBounds;
  return Q(() => {
    s.current && !e.hidden && (!d || a.current !== s.current) && (a.current && i?.unobserve(a.current), i?.observe(s.current), a.current = s.current);
  }, [d, e.hidden]), Q(() => () => {
    a.current && (i?.unobserve(a.current), a.current = null);
  }, []), Q(() => {
    if (s.current) {
      const f = l.current !== t, p = c.current !== e.sourcePosition, h = u.current !== e.targetPosition;
      (f || p || h) && (l.current = t, c.current = e.sourcePosition, u.current = e.targetPosition, o.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: s.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), s;
}
function qv({ id: e, onClick: t, onMouseEnter: n, onMouseMove: i, onMouseLeave: o, onContextMenu: s, onDoubleClick: a, nodesDraggable: c, elementsSelectable: u, nodesConnectable: l, nodesFocusable: d, resizeObserver: f, noDragClassName: p, noPanClassName: h, disableKeyboardA11y: y, rfId: v, nodeTypes: w, nodeClickDistance: x, onError: b }) {
  const { node: g, internals: m, isParent: j } = he((R) => {
    const X = R.nodeLookup.get(e), le = R.parentLookup.has(e);
    return {
      node: X,
      internals: X.internals,
      isParent: le
    };
  }, ve);
  let N = g.type || "default", S = w?.[N] || fc[N];
  S === void 0 && (b?.("003", Ye.error003(N)), N = "default", S = w?.default || fc.default);
  const k = !!(g.draggable || c && typeof g.draggable > "u"), _ = !!(g.selectable || u && typeof g.selectable > "u"), M = !!(g.connectable || l && typeof g.connectable > "u"), I = !!(g.focusable || d && typeof g.focusable > "u"), $ = be(), L = vu(g), C = Yv({ node: g, nodeType: N, hasDimensions: L, resizeObserver: f }), A = Qu({
    nodeRef: C,
    disabled: g.hidden || !k,
    noDragClassName: p,
    handleSelector: g.dragHandle,
    nodeId: e,
    isSelectable: _,
    nodeClickDistance: x
  }), E = ed();
  if (g.hidden)
    return null;
  const D = ft(g), P = Lv(g), T = _ || k || t || n || i || o, B = n ? (R) => n(R, { ...m.userNode }) : void 0, W = i ? (R) => i(R, { ...m.userNode }) : void 0, H = o ? (R) => o(R, { ...m.userNode }) : void 0, q = s ? (R) => s(R, { ...m.userNode }) : void 0, V = a ? (R) => a(R, { ...m.userNode }) : void 0, Y = (R) => {
    const { selectNodesOnDrag: X, nodeDragThreshold: le } = $.getState();
    _ && (!X || !k || le > 0) && Wr({
      id: e,
      store: $,
      nodeRef: C
    }), t && t(R, { ...m.userNode });
  }, J = (R) => {
    if (!(ju(R.nativeEvent) || y)) {
      if (du.includes(R.key) && _) {
        const X = R.key === "Escape";
        Wr({
          id: e,
          store: $,
          unselect: X,
          nodeRef: C
        });
      } else if (k && g.selected && Object.prototype.hasOwnProperty.call(no, R.key)) {
        R.preventDefault();
        const { ariaLabelConfig: X } = $.getState();
        $.setState({
          ariaLiveMessage: X["node.a11yDescription.ariaLiveMessage"]({
            direction: R.key.replace("Arrow", "").toLowerCase(),
            x: ~~m.positionAbsolute.x,
            y: ~~m.positionAbsolute.y
          })
        }), E({
          direction: no[R.key],
          factor: R.shiftKey ? 4 : 1
        });
      }
    }
  }, Z = () => {
    if (y || !C.current?.matches(":focus-visible"))
      return;
    const { transform: R, width: X, height: le, autoPanOnNodeFocus: ce, setCenter: ee } = $.getState();
    if (!ce)
      return;
    vs(/* @__PURE__ */ new Map([[e, g]]), { x: 0, y: 0, width: X, height: le }, R, !0).length > 0 || ee(g.position.x + D.width / 2, g.position.y + D.height / 2, {
      zoom: R[2]
    });
  };
  return r.jsx("div", { className: ke([
    "react-flow__node",
    `react-flow__node-${N}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [h]: k
    },
    g.className,
    {
      selected: g.selected,
      selectable: _,
      parent: j,
      draggable: k,
      dragging: A
    }
  ]), ref: C, style: {
    zIndex: m.z,
    transform: `translate(${m.positionAbsolute.x}px,${m.positionAbsolute.y}px)`,
    pointerEvents: T ? "all" : "none",
    visibility: L ? "visible" : "hidden",
    ...g.style,
    ...P
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: B, onMouseMove: W, onMouseLeave: H, onContextMenu: q, onClick: Y, onDoubleClick: V, onKeyDown: I ? J : void 0, tabIndex: I ? 0 : void 0, onFocus: I ? Z : void 0, role: g.ariaRole ?? (I ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": y ? void 0 : `${Bu}-${v}`, "aria-label": g.ariaLabel, ...g.domAttributes, children: r.jsx(_v, { value: e, children: r.jsx(S, { id: e, data: g.data, type: N, positionAbsoluteX: m.positionAbsolute.x, positionAbsoluteY: m.positionAbsolute.y, selected: g.selected ?? !1, selectable: _, draggable: k, deletable: g.deletable ?? !0, isConnectable: M, sourcePosition: g.sourcePosition, targetPosition: g.targetPosition, dragging: A, dragHandle: g.dragHandle, zIndex: m.z, parentId: g.parentId, ...D }) }) });
}
var Uv = Ee(qv);
const Zv = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function id(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: i, elementsSelectable: o, onError: s } = he(Zv, ve), a = Fv(e.onlyRenderVisibleElements), c = Xv();
  return r.jsx("div", { className: "react-flow__nodes", style: vo, children: a.map((u) => (
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
    r.jsx(Uv, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: i, elementsSelectable: o, nodeClickDistance: e.nodeClickDistance, onError: s }, u)
  )) });
}
id.displayName = "NodeRenderer";
const Gv = Ee(id);
function Jv(e) {
  return he(se((n) => {
    if (!e)
      return n.edges.map((o) => o.id);
    const i = [];
    if (n.width && n.height)
      for (const o of n.edges) {
        const s = n.nodeLookup.get(o.source), a = n.nodeLookup.get(o.target);
        s && a && Yx({
          sourceNode: s,
          targetNode: a,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && i.push(o.id);
      }
    return i;
  }, [e]), ve);
}
const Qv = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return r.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, e0 = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return r.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, hc = {
  [Qi.Arrow]: Qv,
  [Qi.ArrowClosed]: e0
};
function t0(e) {
  const t = be();
  return de(() => Object.prototype.hasOwnProperty.call(hc, e) ? hc[e] : (t.getState().onError?.("009", Ye.error009(e)), null), [e]);
}
const n0 = ({ id: e, type: t, color: n, width: i = 12.5, height: o = 12.5, markerUnits: s = "strokeWidth", strokeWidth: a, orient: c = "auto-start-reverse" }) => {
  const u = t0(t);
  return u ? r.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${i}`, markerHeight: `${o}`, viewBox: "-10 -10 20 20", markerUnits: s, orient: c, refX: "0", refY: "0", children: r.jsx(u, { color: n, strokeWidth: a }) }) : null;
}, od = ({ defaultColor: e, rfId: t }) => {
  const n = he((s) => s.edges), i = he((s) => s.defaultEdgeOptions), o = de(() => tw(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: i?.markerStart,
    defaultMarkerEnd: i?.markerEnd
  }), [n, i, t, e]);
  return o.length ? r.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: r.jsx("defs", { children: o.map((s) => r.jsx(n0, { id: s.id, type: s.type, color: s.color, width: s.width, height: s.height, markerUnits: s.markerUnits, strokeWidth: s.strokeWidth, orient: s.orient }, s.id)) }) }) : null;
};
od.displayName = "MarkerDefinitions";
var i0 = Ee(od);
function rd({ x: e, y: t, label: n, labelStyle: i, labelShowBg: o = !0, labelBgStyle: s, labelBgPadding: a = [2, 4], labelBgBorderRadius: c = 2, children: u, className: l, ...d }) {
  const [f, p] = K({ x: 1, y: 0, width: 0, height: 0 }), h = ke(["react-flow__edge-textwrapper", l]), y = oe(null);
  return Q(() => {
    if (y.current) {
      const v = y.current.getBBox();
      p({
        x: v.x,
        y: v.y,
        width: v.width,
        height: v.height
      });
    }
  }, [n]), n ? r.jsxs("g", { transform: `translate(${e - f.width / 2} ${t - f.height / 2})`, className: h, visibility: f.width ? "visible" : "hidden", ...d, children: [o && r.jsx("rect", { width: f.width + 2 * a[0], x: -a[0], y: -a[1], height: f.height + 2 * a[1], className: "react-flow__edge-textbg", style: s, rx: c, ry: c }), r.jsx("text", { className: "react-flow__edge-text", y: f.height / 2, dy: "0.3em", ref: y, style: i, children: n }), u] }) : null;
}
rd.displayName = "EdgeText";
const o0 = Ee(rd);
function ni({ path: e, labelX: t, labelY: n, label: i, labelStyle: o, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u, interactionWidth: l = 20, ...d }) {
  return r.jsxs(r.Fragment, { children: [r.jsx("path", { ...d, d: e, fill: "none", className: ke(["react-flow__edge-path", d.className]) }), l ? r.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, i && Je(t) && Je(n) ? r.jsx(o0, { x: t, y: n, label: i, labelStyle: o, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u }) : null] });
}
function gc({ pos: e, x1: t, y1: n, x2: i, y2: o }) {
  return e === re.Left || e === re.Right ? [0.5 * (t + i), n] : [t, 0.5 * (n + o)];
}
function sd({ sourceX: e, sourceY: t, sourcePosition: n = re.Bottom, targetX: i, targetY: o, targetPosition: s = re.Top }) {
  const [a, c] = gc({
    pos: n,
    x1: e,
    y1: t,
    x2: i,
    y2: o
  }), [u, l] = gc({
    pos: s,
    x1: i,
    y1: o,
    x2: e,
    y2: t
  }), [d, f, p, h] = Cu({
    sourceX: e,
    sourceY: t,
    targetX: i,
    targetY: o,
    sourceControlX: a,
    sourceControlY: c,
    targetControlX: u,
    targetControlY: l
  });
  return [
    `M${e},${t} C${a},${c} ${u},${l} ${i},${o}`,
    d,
    f,
    p,
    h
  ];
}
function ad(e) {
  return Ee(({ id: t, sourceX: n, sourceY: i, targetX: o, targetY: s, sourcePosition: a, targetPosition: c, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: v, markerStart: w, interactionWidth: x }) => {
    const [b, g, m] = sd({
      sourceX: n,
      sourceY: i,
      sourcePosition: a,
      targetX: o,
      targetY: s,
      targetPosition: c
    }), j = e.isInternal ? void 0 : t;
    return r.jsx(ni, { id: j, path: b, labelX: g, labelY: m, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: v, markerStart: w, interactionWidth: x });
  });
}
const r0 = ad({ isInternal: !1 }), cd = ad({ isInternal: !0 });
r0.displayName = "SimpleBezierEdge";
cd.displayName = "SimpleBezierEdgeInternal";
function ld(e) {
  return Ee(({ id: t, sourceX: n, sourceY: i, targetX: o, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, sourcePosition: h = re.Bottom, targetPosition: y = re.Top, markerEnd: v, markerStart: w, pathOptions: x, interactionWidth: b }) => {
    const [g, m, j] = to({
      sourceX: n,
      sourceY: i,
      sourcePosition: h,
      targetX: o,
      targetY: s,
      targetPosition: y,
      borderRadius: x?.borderRadius,
      offset: x?.offset,
      stepPosition: x?.stepPosition
    }), N = e.isInternal ? void 0 : t;
    return r.jsx(ni, { id: N, path: g, labelX: m, labelY: j, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: v, markerStart: w, interactionWidth: b });
  });
}
const ud = ld({ isInternal: !1 }), dd = ld({ isInternal: !0 });
ud.displayName = "SmoothStepEdge";
dd.displayName = "SmoothStepEdgeInternal";
function fd(e) {
  return Ee(({ id: t, ...n }) => {
    const i = e.isInternal ? void 0 : t;
    return r.jsx(ud, { ...n, id: i, pathOptions: de(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const s0 = fd({ isInternal: !1 }), pd = fd({ isInternal: !0 });
s0.displayName = "StepEdge";
pd.displayName = "StepEdgeInternal";
function hd(e) {
  return Ee(({ id: t, sourceX: n, sourceY: i, targetX: o, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: y, interactionWidth: v }) => {
    const [w, x, b] = Au({ sourceX: n, sourceY: i, targetX: o, targetY: s }), g = e.isInternal ? void 0 : t;
    return r.jsx(ni, { id: g, path: w, labelX: x, labelY: b, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: y, interactionWidth: v });
  });
}
const a0 = hd({ isInternal: !1 }), gd = hd({ isInternal: !0 });
a0.displayName = "StraightEdge";
gd.displayName = "StraightEdgeInternal";
function yd(e) {
  return Ee(({ id: t, sourceX: n, sourceY: i, targetX: o, targetY: s, sourcePosition: a = re.Bottom, targetPosition: c = re.Top, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: v, markerStart: w, pathOptions: x, interactionWidth: b }) => {
    const [g, m, j] = Eu({
      sourceX: n,
      sourceY: i,
      sourcePosition: a,
      targetX: o,
      targetY: s,
      targetPosition: c,
      curvature: x?.curvature
    }), N = e.isInternal ? void 0 : t;
    return r.jsx(ni, { id: N, path: g, labelX: m, labelY: j, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: v, markerStart: w, interactionWidth: b });
  });
}
const c0 = yd({ isInternal: !1 }), md = yd({ isInternal: !0 });
c0.displayName = "BezierEdge";
md.displayName = "BezierEdgeInternal";
const yc = {
  default: md,
  straight: gd,
  step: pd,
  smoothstep: dd,
  simplebezier: cd
}, mc = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, l0 = (e, t, n) => n === re.Left ? e - t : n === re.Right ? e + t : e, u0 = (e, t, n) => n === re.Top ? e - t : n === re.Bottom ? e + t : e, xc = "react-flow__edgeupdater";
function wc({ position: e, centerX: t, centerY: n, radius: i = 10, onMouseDown: o, onMouseEnter: s, onMouseOut: a, type: c }) {
  return r.jsx("circle", { onMouseDown: o, onMouseEnter: s, onMouseOut: a, className: ke([xc, `${xc}-${c}`]), cx: l0(t, i, e), cy: u0(n, i, e), r: i, stroke: "transparent", fill: "transparent" });
}
function d0({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: i, sourceY: o, targetX: s, targetY: a, sourcePosition: c, targetPosition: u, onReconnect: l, onReconnectStart: d, onReconnectEnd: f, setReconnecting: p, setUpdateHover: h }) {
  const y = be(), v = (m, j) => {
    if (m.button !== 0)
      return;
    const { autoPanOnConnect: N, domNode: S, connectionMode: k, connectionRadius: _, lib: M, onConnectStart: I, cancelConnection: $, nodeLookup: L, rfId: C, panBy: A, updateConnection: E } = y.getState(), D = j.type === "target", P = (W, H) => {
      p(!1), f?.(W, n, j.type, H);
    }, T = (W) => l?.(n, W), B = (W, H) => {
      p(!0), d?.(m, n, j.type), I?.(W, H);
    };
    Hr.onPointerDown(m.nativeEvent, {
      autoPanOnConnect: N,
      connectionMode: k,
      connectionRadius: _,
      domNode: S,
      handleId: j.id,
      nodeId: j.nodeId,
      nodeLookup: L,
      isTarget: D,
      edgeUpdaterType: j.type,
      lib: M,
      flowId: C,
      cancelConnection: $,
      panBy: A,
      isValidConnection: (...W) => y.getState().isValidConnection?.(...W) ?? !0,
      onConnect: T,
      onConnectStart: B,
      onConnectEnd: (...W) => y.getState().onConnectEnd?.(...W),
      onReconnectEnd: P,
      updateConnection: E,
      getTransform: () => y.getState().transform,
      getFromHandle: () => y.getState().connection.fromHandle,
      dragThreshold: y.getState().connectionDragThreshold,
      handleDomNode: m.currentTarget
    });
  }, w = (m) => v(m, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), x = (m) => v(m, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), b = () => h(!0), g = () => h(!1);
  return r.jsxs(r.Fragment, { children: [(e === !0 || e === "source") && r.jsx(wc, { position: c, centerX: i, centerY: o, radius: t, onMouseDown: w, onMouseEnter: b, onMouseOut: g, type: "source" }), (e === !0 || e === "target") && r.jsx(wc, { position: u, centerX: s, centerY: a, radius: t, onMouseDown: x, onMouseEnter: b, onMouseOut: g, type: "target" })] });
}
function f0({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: i, onClick: o, onDoubleClick: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, rfId: y, edgeTypes: v, noPanClassName: w, onError: x, disableKeyboardA11y: b }) {
  let g = he((ee) => ee.edgeLookup.get(e));
  const m = he((ee) => ee.defaultEdgeOptions);
  g = m ? { ...m, ...g } : g;
  let j = g.type || "default", N = v?.[j] || yc[j];
  N === void 0 && (x?.("011", Ye.error011(j)), j = "default", N = v?.default || yc.default);
  const S = !!(g.focusable || t && typeof g.focusable > "u"), k = typeof f < "u" && (g.reconnectable || n && typeof g.reconnectable > "u"), _ = !!(g.selectable || i && typeof g.selectable > "u"), M = oe(null), [I, $] = K(!1), [L, C] = K(!1), A = be(), { zIndex: E, sourceX: D, sourceY: P, targetX: T, targetY: B, sourcePosition: W, targetPosition: H } = he(se((ee) => {
    const ne = ee.nodeLookup.get(g.source), fe = ee.nodeLookup.get(g.target);
    if (!ne || !fe)
      return {
        zIndex: g.zIndex,
        ...mc
      };
    const O = ew({
      id: e,
      sourceNode: ne,
      targetNode: fe,
      sourceHandle: g.sourceHandle || null,
      targetHandle: g.targetHandle || null,
      connectionMode: ee.connectionMode,
      onError: x
    });
    return {
      zIndex: Xx({
        selected: g.selected,
        zIndex: g.zIndex,
        sourceNode: ne,
        targetNode: fe,
        elevateOnSelect: ee.elevateEdgesOnSelect,
        zIndexMode: ee.zIndexMode
      }),
      ...O || mc
    };
  }, [g.source, g.target, g.sourceHandle, g.targetHandle, g.selected, g.zIndex]), ve), q = de(() => g.markerStart ? `url('#${Vr(g.markerStart, y)}')` : void 0, [g.markerStart, y]), V = de(() => g.markerEnd ? `url('#${Vr(g.markerEnd, y)}')` : void 0, [g.markerEnd, y]);
  if (g.hidden || D === null || P === null || T === null || B === null)
    return null;
  const Y = (ee) => {
    const { addSelectedEdges: ne, unselectNodesAndEdges: fe, multiSelectionActive: O } = A.getState();
    _ && (A.setState({ nodesSelectionActive: !1 }), g.selected && O ? (fe({ nodes: [], edges: [g] }), M.current?.blur()) : ne([e])), o && o(ee, g);
  }, J = s ? (ee) => {
    s(ee, { ...g });
  } : void 0, Z = a ? (ee) => {
    a(ee, { ...g });
  } : void 0, R = c ? (ee) => {
    c(ee, { ...g });
  } : void 0, X = u ? (ee) => {
    u(ee, { ...g });
  } : void 0, le = l ? (ee) => {
    l(ee, { ...g });
  } : void 0, ce = (ee) => {
    if (!b && du.includes(ee.key) && _) {
      const { unselectNodesAndEdges: ne, addSelectedEdges: fe } = A.getState();
      ee.key === "Escape" ? (M.current?.blur(), ne({ edges: [g] })) : fe([e]);
    }
  };
  return r.jsx("svg", { style: { zIndex: E }, children: r.jsxs("g", { className: ke([
    "react-flow__edge",
    `react-flow__edge-${j}`,
    g.className,
    w,
    {
      selected: g.selected,
      animated: g.animated,
      inactive: !_ && !o,
      updating: I,
      selectable: _
    }
  ]), onClick: Y, onDoubleClick: J, onContextMenu: Z, onMouseEnter: R, onMouseMove: X, onMouseLeave: le, onKeyDown: S ? ce : void 0, tabIndex: S ? 0 : void 0, role: g.ariaRole ?? (S ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": g.ariaLabel === null ? void 0 : g.ariaLabel || `Edge from ${g.source} to ${g.target}`, "aria-describedby": S ? `${Fu}-${y}` : void 0, ref: M, ...g.domAttributes, children: [!L && r.jsx(N, { id: e, source: g.source, target: g.target, type: g.type, selected: g.selected, animated: g.animated, selectable: _, deletable: g.deletable ?? !0, label: g.label, labelStyle: g.labelStyle, labelShowBg: g.labelShowBg, labelBgStyle: g.labelBgStyle, labelBgPadding: g.labelBgPadding, labelBgBorderRadius: g.labelBgBorderRadius, sourceX: D, sourceY: P, targetX: T, targetY: B, sourcePosition: W, targetPosition: H, data: g.data, style: g.style, sourceHandleId: g.sourceHandle, targetHandleId: g.targetHandle, markerStart: q, markerEnd: V, pathOptions: "pathOptions" in g ? g.pathOptions : void 0, interactionWidth: g.interactionWidth }), k && r.jsx(d0, { edge: g, isReconnectable: k, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, sourceX: D, sourceY: P, targetX: T, targetY: B, sourcePosition: W, targetPosition: H, setUpdateHover: $, setReconnecting: C })] }) });
}
var p0 = Ee(f0);
const h0 = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function xd({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: i, noPanClassName: o, onReconnect: s, onEdgeContextMenu: a, onEdgeMouseEnter: c, onEdgeMouseMove: u, onEdgeMouseLeave: l, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: p, onReconnectStart: h, onReconnectEnd: y, disableKeyboardA11y: v }) {
  const { edgesFocusable: w, edgesReconnectable: x, elementsSelectable: b, onError: g } = he(h0, ve), m = Jv(t);
  return r.jsxs("div", { className: "react-flow__edges", children: [r.jsx(i0, { defaultColor: e, rfId: n }), m.map((j) => r.jsx(p0, { id: j, edgesFocusable: w, edgesReconnectable: x, elementsSelectable: b, noPanClassName: o, onReconnect: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, onClick: d, reconnectRadius: f, onDoubleClick: p, onReconnectStart: h, onReconnectEnd: y, rfId: n, onError: g, edgeTypes: i, disableKeyboardA11y: v }, j))] });
}
xd.displayName = "EdgeRenderer";
const g0 = Ee(xd), y0 = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function m0({ children: e }) {
  const t = he(y0);
  return r.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function x0(e) {
  const t = As(), n = oe(!1);
  Q(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const w0 = (e) => e.panZoom?.syncViewport;
function v0(e) {
  const t = he(w0), n = be();
  return Q(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function b0(e) {
  return e.connection.inProgress ? { ...e.connection, to: pn(e.connection.to, e.transform) } : { ...e.connection };
}
function N0(e) {
  return b0;
}
function j0(e) {
  const t = N0();
  return he(t, ve);
}
const S0 = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function C0({ containerStyle: e, style: t, type: n, component: i }) {
  const { nodesConnectable: o, width: s, height: a, isValid: c, inProgress: u } = he(S0, ve);
  return !(s && o && u) ? null : r.jsx("svg", { style: e, width: s, height: a, className: "react-flow__connectionline react-flow__container", children: r.jsx("g", { className: ke(["react-flow__connection", hu(c)]), children: r.jsx(wd, { style: t, type: n, CustomComponent: i, isValid: c }) }) });
}
const wd = ({ style: e, type: t = yt.Bezier, CustomComponent: n, isValid: i }) => {
  const { inProgress: o, from: s, fromNode: a, fromHandle: c, fromPosition: u, to: l, toNode: d, toHandle: f, toPosition: p, pointer: h } = j0();
  if (!o)
    return;
  if (n)
    return r.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: a, fromHandle: c, fromX: s.x, fromY: s.y, toX: l.x, toY: l.y, fromPosition: u, toPosition: p, connectionStatus: hu(i), toNode: d, toHandle: f, pointer: h });
  let y = "";
  const v = {
    sourceX: s.x,
    sourceY: s.y,
    sourcePosition: u,
    targetX: l.x,
    targetY: l.y,
    targetPosition: p
  };
  switch (t) {
    case yt.Bezier:
      [y] = Eu(v);
      break;
    case yt.SimpleBezier:
      [y] = sd(v);
      break;
    case yt.Step:
      [y] = to({
        ...v,
        borderRadius: 0
      });
      break;
    case yt.SmoothStep:
      [y] = to(v);
      break;
    default:
      [y] = Au(v);
  }
  return r.jsx("path", { d: y, fill: "none", className: "react-flow__connection-path", style: e });
};
wd.displayName = "ConnectionLine";
const E0 = {};
function vc(e = E0) {
  oe(e), be(), Q(() => {
  }, [e]);
}
function k0() {
  be(), oe(!1), Q(() => {
  }, []);
}
function vd({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: i, onEdgeClick: o, onNodeDoubleClick: s, onEdgeDoubleClick: a, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: p, onSelectionEnd: h, connectionLineType: y, connectionLineStyle: v, connectionLineComponent: w, connectionLineContainerStyle: x, selectionKeyCode: b, selectionOnDrag: g, selectionMode: m, multiSelectionKeyCode: j, panActivationKeyCode: N, zoomActivationKeyCode: S, deleteKeyCode: k, onlyRenderVisibleElements: _, elementsSelectable: M, defaultViewport: I, translateExtent: $, minZoom: L, maxZoom: C, preventScrolling: A, defaultMarkerColor: E, zoomOnScroll: D, zoomOnPinch: P, panOnScroll: T, panOnScrollSpeed: B, panOnScrollMode: W, zoomOnDoubleClick: H, panOnDrag: q, autoPanOnSelection: V, onPaneClick: Y, onPaneMouseEnter: J, onPaneMouseMove: Z, onPaneMouseLeave: R, onPaneScroll: X, onPaneContextMenu: le, paneClickDistance: ce, nodeClickDistance: ee, onEdgeContextMenu: ne, onEdgeMouseEnter: fe, onEdgeMouseMove: O, onEdgeMouseLeave: te, reconnectRadius: ge, onReconnect: ye, onReconnectStart: _e, onReconnectEnd: De, noDragClassName: $e, noWheelClassName: st, noPanClassName: qe, disableKeyboardA11y: Ue, nodeExtent: Pe, rfId: He, viewport: We, onViewportChange: Ne }) {
  return vc(e), vc(t), k0(), x0(n), v0(We), r.jsx(Wv, { onPaneClick: Y, onPaneMouseEnter: J, onPaneMouseMove: Z, onPaneMouseLeave: R, onPaneContextMenu: le, onPaneScroll: X, paneClickDistance: ce, deleteKeyCode: k, selectionKeyCode: b, selectionOnDrag: g, selectionMode: m, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: j, panActivationKeyCode: N, zoomActivationKeyCode: S, elementsSelectable: M, zoomOnScroll: D, zoomOnPinch: P, zoomOnDoubleClick: H, panOnScroll: T, panOnScrollSpeed: B, panOnScrollMode: W, panOnDrag: q, autoPanOnSelection: V, defaultViewport: I, translateExtent: $, minZoom: L, maxZoom: C, onSelectionContextMenu: f, preventScrolling: A, noDragClassName: $e, noWheelClassName: st, noPanClassName: qe, disableKeyboardA11y: Ue, onViewportChange: Ne, isControlledViewport: !!We, children: r.jsxs(m0, { children: [r.jsx(g0, { edgeTypes: t, onEdgeClick: o, onEdgeDoubleClick: a, onReconnect: ye, onReconnectStart: _e, onReconnectEnd: De, onlyRenderVisibleElements: _, onEdgeContextMenu: ne, onEdgeMouseEnter: fe, onEdgeMouseMove: O, onEdgeMouseLeave: te, reconnectRadius: ge, defaultMarkerColor: E, noPanClassName: qe, disableKeyboardA11y: Ue, rfId: He }), r.jsx(C0, { style: v, type: y, component: w, containerStyle: x }), r.jsx("div", { className: "react-flow__edgelabel-renderer" }), r.jsx(Gv, { nodeTypes: e, onNodeClick: i, onNodeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, nodeClickDistance: ee, onlyRenderVisibleElements: _, noPanClassName: qe, noDragClassName: $e, disableKeyboardA11y: Ue, nodeExtent: Pe, rfId: He }), r.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
vd.displayName = "GraphView";
const I0 = Ee(vd), A0 = wu(), bc = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, width: o, height: s, fitView: a, fitViewOptions: c, minZoom: u = 0.5, maxZoom: l = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: p = "basic" } = {}) => {
  const h = /* @__PURE__ */ new Map(), y = /* @__PURE__ */ new Map(), v = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), x = i ?? t ?? [], b = n ?? e ?? [], g = d ?? [0, 0], m = f ?? Wn;
  Tu(v, w, x);
  const { nodesInitialized: j } = Or(b, h, y, {
    nodeOrigin: g,
    nodeExtent: m,
    zIndexMode: p
  });
  let N = [0, 0, 1];
  if (a && o && s) {
    const S = ei(h, {
      filter: (I) => !!((I.width || I.initialWidth) && (I.height || I.initialHeight))
    }), { x: k, y: _, zoom: M } = Ns(S, o, s, u, l, c?.padding ?? 0.1);
    N = [k, _, M];
  }
  return {
    rfId: "1",
    width: o ?? 0,
    height: s ?? 0,
    transform: N,
    nodes: b,
    nodesInitialized: j,
    nodeLookup: h,
    parentLookup: y,
    edges: x,
    edgeLookup: w,
    connectionLookup: v,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: i !== void 0,
    panZoom: null,
    minZoom: u,
    maxZoom: l,
    translateExtent: Wn,
    nodeExtent: m,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: nn.Strict,
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
    connection: { ...pu },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: A0,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: fu,
    zIndexMode: p,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, _0 = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, width: o, height: s, fitView: a, fitViewOptions: c, minZoom: u, maxZoom: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p }) => Bw((h, y) => {
  async function v() {
    const { nodeLookup: w, panZoom: x, fitViewOptions: b, fitViewResolver: g, width: m, height: j, minZoom: N, maxZoom: S } = y();
    x && (await Vx({
      nodes: w,
      width: m,
      height: j,
      panZoom: x,
      minZoom: N,
      maxZoom: S
    }, b), g?.resolve(!0), h({ fitViewResolver: null }));
  }
  return {
    ...bc({
      nodes: e,
      edges: t,
      width: o,
      height: s,
      fitView: a,
      fitViewOptions: c,
      minZoom: u,
      maxZoom: l,
      nodeOrigin: d,
      nodeExtent: f,
      defaultNodes: n,
      defaultEdges: i,
      zIndexMode: p
    }),
    setNodes: (w) => {
      const { nodeLookup: x, parentLookup: b, nodeOrigin: g, elevateNodesOnSelect: m, fitViewQueued: j, zIndexMode: N, nodesSelectionActive: S } = y(), { nodesInitialized: k, hasSelectedNodes: _ } = Or(w, x, b, {
        nodeOrigin: g,
        nodeExtent: f,
        elevateNodesOnSelect: m,
        checkEquality: !0,
        zIndexMode: N
      }), M = S && _;
      j && k ? (v(), h({
        nodes: w,
        nodesInitialized: k,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: M
      })) : h({ nodes: w, nodesInitialized: k, nodesSelectionActive: M });
    },
    setEdges: (w) => {
      const { connectionLookup: x, edgeLookup: b } = y();
      Tu(x, b, w), h({ edges: w });
    },
    setDefaultNodesAndEdges: (w, x) => {
      if (w) {
        const { setNodes: b } = y();
        b(w), h({ hasDefaultNodes: !0 });
      }
      if (x) {
        const { setEdges: b } = y();
        b(x), h({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (w) => {
      const { triggerNodeChanges: x, nodeLookup: b, parentLookup: g, domNode: m, nodeOrigin: j, nodeExtent: N, debug: S, fitViewQueued: k, zIndexMode: _ } = y(), { changes: M, updatedInternals: I } = cw(w, b, g, m, j, N, _);
      I && (ow(b, g, { nodeOrigin: j, nodeExtent: N, zIndexMode: _ }), k ? (v(), h({ fitViewQueued: !1, fitViewOptions: void 0 })) : h({}), M?.length > 0 && (S && console.log("React Flow: trigger node changes", M), x?.(M)));
    },
    updateNodePositions: (w, x = !1) => {
      const b = [];
      let g = [];
      const { nodeLookup: m, triggerNodeChanges: j, connection: N, updateConnection: S, onNodesChangeMiddlewareMap: k } = y();
      for (const [_, M] of w) {
        const I = m.get(_), $ = !!(I?.expandParent && I?.parentId && M?.position), L = {
          id: _,
          type: "position",
          position: $ ? {
            x: Math.max(0, M.position.x),
            y: Math.max(0, M.position.y)
          } : M.position,
          dragging: x
        };
        if (I && N.inProgress && N.fromNode.id === I.id) {
          const C = $t(I, N.fromHandle, re.Left, !0);
          S({ ...N, from: C });
        }
        $ && I.parentId && b.push({
          id: _,
          parentId: I.parentId,
          rect: {
            ...M.internals.positionAbsolute,
            width: M.measured.width ?? 0,
            height: M.measured.height ?? 0
          }
        }), g.push(L);
      }
      if (b.length > 0) {
        const { parentLookup: _, nodeOrigin: M } = y(), I = Is(b, m, _, M);
        g.push(...I);
      }
      for (const _ of k.values())
        g = _(g);
      j(g);
    },
    triggerNodeChanges: (w) => {
      const { onNodesChange: x, setNodes: b, nodes: g, hasDefaultNodes: m, debug: j } = y();
      if (w?.length) {
        if (m) {
          const N = Yu(w, g);
          b(N);
        }
        j && console.log("React Flow: trigger node changes", w), x?.(w);
      }
    },
    triggerEdgeChanges: (w) => {
      const { onEdgesChange: x, setEdges: b, edges: g, hasDefaultEdges: m, debug: j } = y();
      if (w?.length) {
        if (m) {
          const N = qu(w, g);
          b(N);
        }
        j && console.log("React Flow: trigger edge changes", w), x?.(w);
      }
    },
    addSelectedNodes: (w) => {
      const { multiSelectionActive: x, edgeLookup: b, nodeLookup: g, triggerNodeChanges: m, triggerEdgeChanges: j } = y();
      if (x) {
        const N = w.map((S) => Nt(S, !0));
        m(N);
        return;
      }
      m(Xt(g, /* @__PURE__ */ new Set([...w]), !0)), j(Xt(b));
    },
    addSelectedEdges: (w) => {
      const { multiSelectionActive: x, edgeLookup: b, nodeLookup: g, triggerNodeChanges: m, triggerEdgeChanges: j } = y();
      if (x) {
        const N = w.map((S) => Nt(S, !0));
        j(N);
        return;
      }
      j(Xt(b, /* @__PURE__ */ new Set([...w]))), m(Xt(g, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: w, edges: x } = {}) => {
      const { edges: b, nodes: g, nodeLookup: m, triggerNodeChanges: j, triggerEdgeChanges: N } = y(), S = w || g, k = x || b, _ = [];
      for (const I of S) {
        if (!I.selected)
          continue;
        const $ = m.get(I.id);
        $ && ($.selected = !1), _.push(Nt(I.id, !1));
      }
      const M = [];
      for (const I of k)
        I.selected && M.push(Nt(I.id, !1));
      j(_), N(M);
    },
    setMinZoom: (w) => {
      const { panZoom: x, maxZoom: b } = y();
      x?.setScaleExtent([w, b]), h({ minZoom: w });
    },
    setMaxZoom: (w) => {
      const { panZoom: x, minZoom: b } = y();
      x?.setScaleExtent([b, w]), h({ maxZoom: w });
    },
    setTranslateExtent: (w) => {
      y().panZoom?.setTranslateExtent(w), h({ translateExtent: w });
    },
    resetSelectedElements: () => {
      const { edges: w, nodes: x, triggerNodeChanges: b, triggerEdgeChanges: g, elementsSelectable: m } = y();
      if (!m)
        return;
      const j = x.reduce((S, k) => k.selected ? [...S, Nt(k.id, !1)] : S, []), N = w.reduce((S, k) => k.selected ? [...S, Nt(k.id, !1)] : S, []);
      b(j), g(N);
    },
    setNodeExtent: (w) => {
      const { nodes: x, nodeLookup: b, parentLookup: g, nodeOrigin: m, elevateNodesOnSelect: j, nodeExtent: N, zIndexMode: S } = y();
      w[0][0] === N[0][0] && w[0][1] === N[0][1] && w[1][0] === N[1][0] && w[1][1] === N[1][1] || (Or(x, b, g, {
        nodeOrigin: m,
        nodeExtent: w,
        elevateNodesOnSelect: j,
        checkEquality: !1,
        zIndexMode: S
      }), h({ nodeExtent: w }));
    },
    panBy: (w) => {
      const { transform: x, width: b, height: g, panZoom: m, translateExtent: j } = y();
      return lw({ delta: w, panZoom: m, transform: x, translateExtent: j, width: b, height: g });
    },
    setCenter: async (w, x, b) => {
      const { width: g, height: m, maxZoom: j, panZoom: N } = y();
      if (!N)
        return !1;
      const S = typeof b?.zoom < "u" ? b.zoom : j;
      return await N.setViewport({
        x: g / 2 - w * S,
        y: m / 2 - x * S,
        zoom: S
      }, { duration: b?.duration, ease: b?.ease, interpolate: b?.interpolate }), !0;
    },
    cancelConnection: () => {
      h({
        connection: { ...pu }
      });
    },
    updateConnection: (w) => {
      h({ connection: w });
    },
    reset: () => h({ ...bc() })
  };
}, Object.is);
function D0({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: i, initialWidth: o, initialHeight: s, initialMinZoom: a, initialMaxZoom: c, initialFitViewOptions: u, fitView: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p, children: h }) {
  const [y] = K(() => _0({
    nodes: e,
    edges: t,
    defaultNodes: n,
    defaultEdges: i,
    width: o,
    height: s,
    fitView: l,
    minZoom: a,
    maxZoom: c,
    fitViewOptions: u,
    nodeOrigin: d,
    nodeExtent: f,
    zIndexMode: p
  }));
  return r.jsx(Yw, { value: y, children: r.jsx(mv, { children: h }) });
}
function T0({ children: e, nodes: t, edges: n, defaultNodes: i, defaultEdges: o, width: s, height: a, fitView: c, fitViewOptions: u, minZoom: l, maxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h }) {
  return Yn(xo) ? r.jsx(r.Fragment, { children: e }) : r.jsx(D0, { initialNodes: t, initialEdges: n, defaultNodes: i, defaultEdges: o, initialWidth: s, initialHeight: a, fitView: c, initialFitViewOptions: u, initialMinZoom: l, initialMaxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h, children: e });
}
const $0 = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function P0({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, className: o, nodeTypes: s, edgeTypes: a, onNodeClick: c, onEdgeClick: u, onInit: l, onMove: d, onMoveStart: f, onMoveEnd: p, onConnect: h, onConnectStart: y, onConnectEnd: v, onClickConnectStart: w, onClickConnectEnd: x, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: m, onNodeContextMenu: j, onNodeDoubleClick: N, onNodeDragStart: S, onNodeDrag: k, onNodeDragStop: _, onNodesDelete: M, onEdgesDelete: I, onDelete: $, onSelectionChange: L, onSelectionDragStart: C, onSelectionDrag: A, onSelectionDragStop: E, onSelectionContextMenu: D, onSelectionStart: P, onSelectionEnd: T, onBeforeDelete: B, connectionMode: W, connectionLineType: H = yt.Bezier, connectionLineStyle: q, connectionLineComponent: V, connectionLineContainerStyle: Y, deleteKeyCode: J = "Backspace", selectionKeyCode: Z = "Shift", selectionOnDrag: R = !1, selectionMode: X = Bn.Full, panActivationKeyCode: le = "Space", multiSelectionKeyCode: ce = Kn() ? "Meta" : "Control", zoomActivationKeyCode: ee = Kn() ? "Meta" : "Control", snapToGrid: ne, snapGrid: fe, onlyRenderVisibleElements: O = !1, selectNodesOnDrag: te, nodesDraggable: ge, autoPanOnNodeFocus: ye, nodesConnectable: _e, nodesFocusable: De, nodeOrigin: $e = Ku, edgesFocusable: st, edgesReconnectable: qe, elementsSelectable: Ue = !0, defaultViewport: Pe = sv, minZoom: He = 0.5, maxZoom: We = 2, translateExtent: Ne = Wn, preventScrolling: zt = !0, nodeExtent: Ie, defaultMarkerColor: gn = "#b1b1b7", zoomOnScroll: z = !0, zoomOnPinch: F = !0, panOnScroll: U = !1, panOnScrollSpeed: ie = 0.5, panOnScrollMode: ue = Ct.Free, zoomOnDoubleClick: pe = !0, panOnDrag: xe = !0, onPaneClick: Ae, onPaneMouseEnter: je, onPaneMouseMove: Le, onPaneMouseLeave: we, onPaneScroll: Lt, onPaneContextMenu: jo, paneClickDistance: oi = 1, nodeClickDistance: So = 0, children: Co, onReconnect: pt, onReconnectStart: Eo, onReconnectEnd: Vt, onEdgeContextMenu: ko, onEdgeDoubleClick: Ot, onEdgeMouseEnter: Io, onEdgeMouseMove: ri, onEdgeMouseLeave: si, reconnectRadius: Ao = 10, onNodesChange: Ht, onEdgesChange: _o, noDragClassName: Do = "nodrag", noWheelClassName: To = "nowheel", noPanClassName: Wt = "nopan", fitView: ai, fitViewOptions: ci, connectOnClick: $o, attributionPosition: Po, proOptions: Mo, defaultEdgeOptions: Ro, elevateNodesOnSelect: zo = !0, elevateEdgesOnSelect: Lo = !1, disableKeyboardA11y: li = !1, autoPanOnConnect: Vo, autoPanOnNodeDrag: Oo, autoPanOnSelection: Ho = !0, autoPanSpeed: Wo, connectionRadius: Bo, isValidConnection: ui, onError: di, style: fi, id: yn, nodeDragThreshold: Fo, connectionDragThreshold: Ko, viewport: Xo, onViewportChange: Yo, width: qo, height: Uo, colorMode: Zo = "light", debug: Go, onScroll: pi, ariaLabelConfig: Jo, zIndexMode: hi = "basic", ...Qo }, er) {
  const bt = yn || "1", tr = uv(Zo), nr = se((gi) => {
    gi.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), pi?.(gi);
  }, [pi]);
  return r.jsx("div", { "data-testid": "rf__wrapper", ...Qo, onScroll: nr, style: { ...fi, ...$0 }, ref: er, className: ke(["react-flow", o, tr]), id: yn, role: "application", children: r.jsxs(T0, { nodes: e, edges: t, width: qo, height: Uo, fitView: ai, fitViewOptions: ci, minZoom: He, maxZoom: We, nodeOrigin: $e, nodeExtent: Ie, zIndexMode: hi, children: [r.jsx(lv, { nodes: e, edges: t, defaultNodes: n, defaultEdges: i, onConnect: h, onConnectStart: y, onConnectEnd: v, onClickConnectStart: w, onClickConnectEnd: x, nodesDraggable: ge, autoPanOnNodeFocus: ye, nodesConnectable: _e, nodesFocusable: De, edgesFocusable: st, edgesReconnectable: qe, elementsSelectable: Ue, elevateNodesOnSelect: zo, elevateEdgesOnSelect: Lo, minZoom: He, maxZoom: We, nodeExtent: Ie, onNodesChange: Ht, onEdgesChange: _o, snapToGrid: ne, snapGrid: fe, connectionMode: W, translateExtent: Ne, connectOnClick: $o, defaultEdgeOptions: Ro, fitView: ai, fitViewOptions: ci, onNodesDelete: M, onEdgesDelete: I, onDelete: $, onNodeDragStart: S, onNodeDrag: k, onNodeDragStop: _, onSelectionDrag: A, onSelectionDragStart: C, onSelectionDragStop: E, onMove: d, onMoveStart: f, onMoveEnd: p, noPanClassName: Wt, nodeOrigin: $e, rfId: bt, autoPanOnConnect: Vo, autoPanOnNodeDrag: Oo, autoPanSpeed: Wo, onError: di, connectionRadius: Bo, isValidConnection: ui, selectNodesOnDrag: te, nodeDragThreshold: Fo, connectionDragThreshold: Ko, onBeforeDelete: B, debug: Go, ariaLabelConfig: Jo, zIndexMode: hi }), r.jsx(I0, { onInit: l, onNodeClick: c, onEdgeClick: u, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: m, onNodeContextMenu: j, onNodeDoubleClick: N, nodeTypes: s, edgeTypes: a, connectionLineType: H, connectionLineStyle: q, connectionLineComponent: V, connectionLineContainerStyle: Y, selectionKeyCode: Z, selectionOnDrag: R, selectionMode: X, deleteKeyCode: J, multiSelectionKeyCode: ce, panActivationKeyCode: le, zoomActivationKeyCode: ee, onlyRenderVisibleElements: O, defaultViewport: Pe, translateExtent: Ne, minZoom: He, maxZoom: We, preventScrolling: zt, zoomOnScroll: z, zoomOnPinch: F, zoomOnDoubleClick: pe, panOnScroll: U, panOnScrollSpeed: ie, panOnScrollMode: ue, panOnDrag: xe, autoPanOnSelection: Ho, onPaneClick: Ae, onPaneMouseEnter: je, onPaneMouseMove: Le, onPaneMouseLeave: we, onPaneScroll: Lt, onPaneContextMenu: jo, paneClickDistance: oi, nodeClickDistance: So, onSelectionContextMenu: D, onSelectionStart: P, onSelectionEnd: T, onReconnect: pt, onReconnectStart: Eo, onReconnectEnd: Vt, onEdgeContextMenu: ko, onEdgeDoubleClick: Ot, onEdgeMouseEnter: Io, onEdgeMouseMove: ri, onEdgeMouseLeave: si, reconnectRadius: Ao, defaultMarkerColor: gn, noDragClassName: Do, noWheelClassName: To, noPanClassName: Wt, rfId: bt, disableKeyboardA11y: li, nodeExtent: Ie, viewport: Xo, onViewportChange: Yo }), r.jsx(rv, { onSelectionChange: L }), Co, r.jsx(ev, { proOptions: Mo, position: Po }), r.jsx(Qw, { rfId: bt, disableKeyboardA11y: li })] }) });
}
var bd = Gu(P0);
const M0 = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function R0({ children: e }) {
  const t = he(M0);
  return t ? Xw.createPortal(e, t) : null;
}
function z0({ dimensions: e, lineWidth: t, variant: n, className: i }) {
  return r.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: ke(["react-flow__background-pattern", n, i]) });
}
function L0({ radius: e, className: t }) {
  return r.jsx("circle", { cx: e, cy: e, r: e, className: ke(["react-flow__background-pattern", "dots", t]) });
}
var xt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(xt || (xt = {}));
const V0 = {
  [xt.Dots]: 1,
  [xt.Lines]: 1,
  [xt.Cross]: 6
}, O0 = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function Nd({
  id: e,
  variant: t = xt.Dots,
  // only used for dots and cross
  gap: n = 20,
  // only used for lines and cross
  size: i,
  lineWidth: o = 1,
  offset: s = 0,
  color: a,
  bgColor: c,
  style: u,
  className: l,
  patternClassName: d
}) {
  const f = oe(null), { transform: p, patternId: h } = he(O0, ve), y = i || V0[t], v = t === xt.Dots, w = t === xt.Cross, x = Array.isArray(n) ? n : [n, n], b = [x[0] * p[2] || 1, x[1] * p[2] || 1], g = y * p[2], m = Array.isArray(s) ? s : [s, s], j = w ? [g, g] : b, N = [
    m[0] * p[2] || 1 + j[0] / 2,
    m[1] * p[2] || 1 + j[1] / 2
  ], S = `${h}${e || ""}`;
  return r.jsxs("svg", { className: ke(["react-flow__background", l]), style: {
    ...u,
    ...vo,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": a
  }, ref: f, "data-testid": "rf__background", children: [r.jsx("pattern", { id: S, x: p[0] % b[0], y: p[1] % b[1], width: b[0], height: b[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${N[0]},-${N[1]})`, children: v ? r.jsx(L0, { radius: g / 2, className: d }) : r.jsx(z0, { dimensions: j, lineWidth: o, variant: t, className: d }) }), r.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${S})` })] });
}
Nd.displayName = "Background";
const jd = Ee(Nd);
function H0() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: r.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function W0() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: r.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function B0() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: r.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function F0() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: r.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function K0() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: r.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function ki({ children: e, className: t, ...n }) {
  return r.jsx("button", { type: "button", className: ke(["react-flow__controls-button", t]), ...n, children: e });
}
const X0 = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function Sd({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: i = !0, fitViewOptions: o, onZoomIn: s, onZoomOut: a, onFitView: c, onInteractiveChange: u, className: l, children: d, position: f = "bottom-left", orientation: p = "vertical", "aria-label": h }) {
  const y = be(), { isInteractive: v, minZoomReached: w, maxZoomReached: x, ariaLabelConfig: b } = he(X0, ve), { zoomIn: g, zoomOut: m, fitView: j } = As(), N = () => {
    g(), s?.();
  }, S = () => {
    m(), a?.();
  }, k = () => {
    j(o), c?.();
  }, _ = () => {
    y.setState({
      nodesDraggable: !v,
      nodesConnectable: !v,
      elementsSelectable: !v
    }), u?.(!v);
  }, M = p === "horizontal" ? "horizontal" : "vertical";
  return r.jsxs(wo, { className: ke(["react-flow__controls", M, l]), position: f, style: e, "data-testid": "rf__controls", "aria-label": h ?? b["controls.ariaLabel"], children: [t && r.jsxs(r.Fragment, { children: [r.jsx(ki, { onClick: N, className: "react-flow__controls-zoomin", title: b["controls.zoomIn.ariaLabel"], "aria-label": b["controls.zoomIn.ariaLabel"], disabled: x, children: r.jsx(H0, {}) }), r.jsx(ki, { onClick: S, className: "react-flow__controls-zoomout", title: b["controls.zoomOut.ariaLabel"], "aria-label": b["controls.zoomOut.ariaLabel"], disabled: w, children: r.jsx(W0, {}) })] }), n && r.jsx(ki, { className: "react-flow__controls-fitview", onClick: k, title: b["controls.fitView.ariaLabel"], "aria-label": b["controls.fitView.ariaLabel"], children: r.jsx(B0, {}) }), i && r.jsx(ki, { className: "react-flow__controls-interactive", onClick: _, title: b["controls.interactive.ariaLabel"], "aria-label": b["controls.interactive.ariaLabel"], children: v ? r.jsx(K0, {}) : r.jsx(F0, {}) }), d] });
}
Sd.displayName = "Controls";
const Cd = Ee(Sd);
function Y0({ id: e, x: t, y: n, width: i, height: o, style: s, color: a, strokeColor: c, strokeWidth: u, className: l, borderRadius: d, shapeRendering: f, selected: p, onClick: h }) {
  const { background: y, backgroundColor: v } = s || {}, w = a || y || v;
  return r.jsx("rect", { className: ke(["react-flow__minimap-node", { selected: p }, l]), x: t, y: n, rx: d, ry: d, width: i, height: o, style: {
    fill: w,
    stroke: c,
    strokeWidth: u
  }, shapeRendering: f, onClick: h ? (x) => h(x, e) : void 0 });
}
const q0 = Ee(Y0), U0 = (e) => e.nodes.map((t) => t.id), jr = (e) => e instanceof Function ? e : () => e;
function Z0({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: i = 5,
  nodeStrokeWidth: o,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: s = q0,
  onClick: a
}) {
  const c = he(U0, ve), u = jr(t), l = jr(e), d = jr(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return r.jsx(r.Fragment, { children: c.map((p) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    r.jsx(J0, { id: p, nodeColorFunc: u, nodeStrokeColorFunc: l, nodeClassNameFunc: d, nodeBorderRadius: i, nodeStrokeWidth: o, NodeComponent: s, onClick: a, shapeRendering: f }, p)
  )) });
}
function G0({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: i, nodeBorderRadius: o, nodeStrokeWidth: s, shapeRendering: a, NodeComponent: c, onClick: u }) {
  const { node: l, x: d, y: f, width: p, height: h } = he((y) => {
    const v = y.nodeLookup.get(e);
    if (!v)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const w = v.internals.userNode, { x, y: b } = v.internals.positionAbsolute, { width: g, height: m } = ft(w);
    return {
      node: w,
      x,
      y: b,
      width: g,
      height: m
    };
  }, ve);
  return !l || l.hidden || !vu(l) ? null : r.jsx(c, { x: d, y: f, width: p, height: h, style: l.style, selected: !!l.selected, className: i(l), color: t(l), borderRadius: o, strokeColor: n(l), strokeWidth: s, shapeRendering: a, onClick: u, id: l.id });
}
const J0 = Ee(G0);
var Q0 = Ee(Z0);
const eb = 200, tb = 150, nb = (e) => !e.hidden, ib = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? xu(ei(e.nodeLookup, { filter: nb }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, ob = "react-flow__minimap-desc";
function Ed({
  style: e,
  className: t,
  nodeStrokeColor: n,
  nodeColor: i,
  nodeClassName: o = "",
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
  position: p = "bottom-right",
  onClick: h,
  onNodeClick: y,
  pannable: v = !1,
  zoomable: w = !1,
  ariaLabel: x,
  inversePan: b,
  zoomStep: g = 1,
  offsetScale: m = 5
}) {
  const j = be(), N = oe(null), { boundingRect: S, viewBB: k, rfId: _, panZoom: M, translateExtent: I, flowWidth: $, flowHeight: L, ariaLabelConfig: C } = he(ib, ve), A = e?.width ?? eb, E = e?.height ?? tb, D = S.width / A, P = S.height / E, T = Math.max(D, P), B = T * A, W = T * E, H = m * T, q = S.x - (B - S.width) / 2 - H, V = S.y - (W - S.height) / 2 - H, Y = B + H * 2, J = W + H * 2, Z = `${ob}-${_}`, R = oe(0), X = oe();
  R.current = T, Q(() => {
    if (N.current && M)
      return X.current = xw({
        domNode: N.current,
        panZoom: M,
        getTransform: () => j.getState().transform,
        getViewScale: () => R.current
      }), () => {
        X.current?.destroy();
      };
  }, [M]), Q(() => {
    X.current?.update({
      translateExtent: I,
      width: $,
      height: L,
      inversePan: b,
      pannable: v,
      zoomStep: g,
      zoomable: w
    });
  }, [v, w, b, g, I, $, L]);
  const le = h ? (ne) => {
    const [fe, O] = X.current?.pointer(ne) || [0, 0];
    h(ne, { x: fe, y: O });
  } : void 0, ce = y ? se((ne, fe) => {
    const O = j.getState().nodeLookup.get(fe).internals.userNode;
    y(ne, O);
  }, []) : void 0, ee = x ?? C["minimap.ariaLabel"];
  return r.jsx(wo, { position: p, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * T : void 0,
    "--xy-minimap-node-background-color-props": typeof i == "string" ? i : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof a == "number" ? a : void 0
  }, className: ke(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: r.jsxs("svg", { width: A, height: E, viewBox: `${q} ${V} ${Y} ${J}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": Z, ref: N, onClick: le, children: [ee && r.jsx("title", { id: Z, children: ee }), r.jsx(Q0, { onClick: ce, nodeColor: i, nodeStrokeColor: n, nodeBorderRadius: s, nodeClassName: o, nodeStrokeWidth: a, nodeComponent: c }), r.jsx("path", { className: "react-flow__minimap-mask", d: `M${q - H},${V - H}h${Y + H * 2}v${J + H * 2}h${-Y - H * 2}z
        M${k.x},${k.y}h${k.width}v${k.height}h${-k.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
Ed.displayName = "MiniMap";
const kd = Ee(Ed), rb = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, sb = {
  [an.Line]: "right",
  [an.Handle]: "bottom-right"
};
function ab({ nodeId: e, position: t, variant: n = an.Handle, className: i, style: o = void 0, children: s, color: a, minWidth: c = 10, minHeight: u = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: p, autoScale: h = !0, shouldResize: y, onResizeStart: v, onResize: w, onResizeEnd: x }) {
  const b = td(), g = typeof e == "string" ? e : b, m = be(), j = oe(null), N = n === an.Handle, S = he(se(rb(N && h), [N, h]), ve), k = oe(null), _ = t ?? sb[n];
  Q(() => {
    if (!(!j.current || !g))
      return k.current || (k.current = Dw({
        domNode: j.current,
        nodeId: g,
        getStoreItems: () => {
          const { nodeLookup: I, transform: $, snapGrid: L, snapToGrid: C, nodeOrigin: A, domNode: E } = m.getState();
          return {
            nodeLookup: I,
            transform: $,
            snapGrid: L,
            snapToGrid: C,
            nodeOrigin: A,
            paneDomNode: E
          };
        },
        onChange: (I, $) => {
          const { triggerNodeChanges: L, nodeLookup: C, parentLookup: A, nodeOrigin: E } = m.getState(), D = [], P = { x: I.x, y: I.y }, T = C.get(g);
          if (T && T.expandParent && T.parentId) {
            const B = T.origin ?? E, W = I.width ?? T.measured.width ?? 0, H = I.height ?? T.measured.height ?? 0, q = {
              id: T.id,
              parentId: T.parentId,
              rect: {
                width: W,
                height: H,
                ...bu({
                  x: I.x ?? T.position.x,
                  y: I.y ?? T.position.y
                }, { width: W, height: H }, T.parentId, C, B)
              }
            }, V = Is([q], C, A, E);
            D.push(...V), P.x = I.x ? Math.max(B[0] * W, I.x) : void 0, P.y = I.y ? Math.max(B[1] * H, I.y) : void 0;
          }
          if (P.x !== void 0 && P.y !== void 0) {
            const B = {
              id: g,
              type: "position",
              position: { ...P }
            };
            D.push(B);
          }
          if (I.width !== void 0 && I.height !== void 0) {
            const W = {
              id: g,
              type: "dimensions",
              resizing: !0,
              setAttributes: p ? p === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: I.width,
                height: I.height
              }
            };
            D.push(W);
          }
          for (const B of $) {
            const W = {
              ...B,
              type: "position"
            };
            D.push(W);
          }
          L(D);
        },
        onEnd: ({ width: I, height: $ }) => {
          const L = {
            id: g,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: I,
              height: $
            }
          };
          m.getState().triggerNodeChanges([L]);
        }
      })), k.current.update({
        controlPosition: _,
        boundaries: {
          minWidth: c,
          minHeight: u,
          maxWidth: l,
          maxHeight: d
        },
        keepAspectRatio: f,
        resizeDirection: p,
        onResizeStart: v,
        onResize: w,
        onResizeEnd: x,
        shouldResize: y
      }), () => {
        k.current?.destroy();
      };
  }, [
    _,
    c,
    u,
    l,
    d,
    f,
    v,
    w,
    x,
    y
  ]);
  const M = _.split("-");
  return r.jsx("div", { className: ke(["react-flow__resize-control", "nodrag", ...M, n, i]), ref: j, style: {
    ...o,
    scale: S,
    ...a && { [N ? "backgroundColor" : "borderColor"]: a }
  }, children: s });
}
Ee(ab);
function cb(e, t) {
  return {
    ...t ? { name: t } : {},
    definitionId: e.definitionId,
    state: Un(e.state),
    layout: e.layout
  };
}
function lb(e) {
  return JSON.stringify(
    {
      state: Un(e.state),
      layout: e.layout
    },
    null,
    2
  );
}
function ub(e, t) {
  let n;
  try {
    n = JSON.parse(e);
  } catch (o) {
    return { ok: !1, error: o instanceof Error ? o.message : String(o) };
  }
  if (!n || typeof n != "object")
    return { ok: !1, error: "Workflow JSON must be an object with a 'state' property." };
  const i = n;
  return !i.state || typeof i.state != "object" ? { ok: !1, error: "Workflow JSON is missing a valid 'state' object." } : i.layout !== void 0 && !Array.isArray(i.layout) ? { ok: !1, error: "'layout' must be an array when present." } : {
    ok: !0,
    draft: {
      ...t,
      state: ro(i.state),
      layout: i.layout ?? t.layout
    }
  };
}
function db(e, t) {
  const n = (t ?? "workflow").trim().replace(/[^\w.-]+/g, "-") || "workflow", i = new Blob([JSON.stringify(e, null, 2)], { type: "application/json" }), o = URL.createObjectURL(i), s = document.createElement("a");
  s.href = o, s.download = `${n}.json`, document.body.appendChild(s), s.click(), s.remove(), URL.revokeObjectURL(o);
}
function Nc({
  document: e,
  readOnly: t,
  minHeight: n,
  ariaLabel: i,
  onChange: o
}) {
  const s = (a) => {
    t || o({ ...e, value: a.target.value });
  };
  return /* @__PURE__ */ r.jsx(
    "textarea",
    {
      "aria-label": i,
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
function fb({
  document: e,
  diagnostics: t = [],
  readOnly: n = !1,
  theme: i = "studio",
  minHeight: o = "220px",
  ariaLabel: s,
  languageAdapter: a,
  onChange: c
}) {
  const u = t.filter((p) => !p.uri || p.uri === e.uri), l = a?.displayName ?? e.language, d = a?.loadEditor, f = de(
    () => d ? ff(d) : null,
    [d]
  );
  return /* @__PURE__ */ r.jsxs(
    "section",
    {
      className: "studio-code-editor",
      "data-language": e.language,
      "data-theme": i,
      "data-readonly": n,
      children: [
        /* @__PURE__ */ r.jsxs("div", { className: "studio-code-editor-header", children: [
          /* @__PURE__ */ r.jsx("span", { children: l }),
          /* @__PURE__ */ r.jsx("code", { children: e.uri })
        ] }),
        f ? /* @__PURE__ */ r.jsx(pf, { fallback: /* @__PURE__ */ r.jsx(
          Nc,
          {
            document: e,
            readOnly: n,
            minHeight: o,
            ariaLabel: s,
            onChange: c
          }
        ), children: /* @__PURE__ */ r.jsx(
          f,
          {
            document: e,
            readOnly: n,
            theme: i,
            minHeight: o,
            ariaLabel: s,
            onChange: c
          }
        ) }) : /* @__PURE__ */ r.jsx(
          Nc,
          {
            document: e,
            readOnly: n,
            minHeight: o,
            ariaLabel: s,
            onChange: c
          }
        ),
        /* @__PURE__ */ r.jsx(pb, { diagnostics: u })
      ]
    }
  );
}
function pb({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ r.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const i = t.severity ?? "info", o = hb(t);
    return /* @__PURE__ */ r.jsxs(
      "p",
      {
        className: `studio-code-editor-diagnostic ${i}`,
        children: [
          t.code ? /* @__PURE__ */ r.jsx("span", { children: t.code }) : null,
          o ? /* @__PURE__ */ r.jsx("small", { children: o }) : null,
          t.message
        ]
      },
      `${t.uri ?? "document"}-${t.code ?? "diagnostic"}-${n}`
    );
  }) });
}
function hb(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const gb = { language: "json", displayName: "JSON" };
function yb({ draft: e, onApply: t }) {
  const n = de(() => lb(e), [e]), [i, o] = K(n), [s, a] = K(n), [c, u] = K(null);
  Q(() => {
    o(n), a(n), u(null);
  }, [n]);
  const l = i !== s, d = c ? [{ severity: "error", message: c }] : [], f = () => u(t(i));
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-code-view", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-code-view-toolbar", children: [
      /* @__PURE__ */ r.jsx("span", { className: "wf-muted", children: "Edit the workflow definition as JSON. Changes apply to the draft when you click Apply." }),
      /* @__PURE__ */ r.jsxs("span", { className: "wf-code-view-actions", children: [
        /* @__PURE__ */ r.jsx("button", { type: "button", disabled: !l, onClick: () => {
          o(s), u(null);
        }, children: "Reset" }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: !l, onClick: f, children: [
          /* @__PURE__ */ r.jsx(ln, { size: 14 }),
          " Apply"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ r.jsx("div", { className: "wf-code-view-editor", children: /* @__PURE__ */ r.jsx(
      fb,
      {
        ariaLabel: "Workflow JSON",
        document: { uri: "elsa://workflows/definition.json", language: "json", value: i },
        languageAdapter: gb,
        diagnostics: d,
        minHeight: "100%",
        theme: "studio",
        onChange: (p) => {
          o(p.value), c && u(null);
        }
      }
    ) })
  ] });
}
const mb = ["Single", "Array", "List", "HashSet"];
function Id(e) {
  const [t, n] = K(null), [i, o] = K(null);
  Q(() => {
    let u = !1;
    return hh(e).then(
      (l) => {
        u || n(l);
      },
      () => {
        u || n([]);
      }
    ), yh(e).then(
      (l) => {
        u || o(l);
      },
      () => {
        u || o([]);
      }
    ), () => {
      u = !0;
    };
  }, [e]);
  const s = de(
    () => t && t.length > 0 ? t.map((u) => {
      const l = qs(u);
      return {
        value: l,
        label: il(u.displayName, l),
        group: u.category?.trim() || "Other"
      };
    }) : null,
    [t]
  ), a = de(
    () => i && i.length > 0 ? i.filter((u) => !u.deprecated).map((u) => ({
      value: u.typeName,
      label: Yf(u.displayName, u.typeName)
    })) : null,
    [i]
  ), c = de(() => {
    const u = /* @__PURE__ */ new Map();
    for (const l of t ?? []) {
      const d = qs(l), f = l.defaultEditor?.trim();
      d && u.set(d, f && f.length > 0 ? f : "text");
    }
    return (l) => u.get(l) ?? "text";
  }, [t]);
  return { typeOptions: s, storageOptions: a, editorForAlias: c };
}
function xb(e) {
  return !e || e.length === 0 ? void 0 : (e.find((n) => /(^|\.)String$/i.test(n.value) || n.label.toLowerCase() === "string") ?? e[0]).value;
}
function wb(e, t, n) {
  return {
    add: () => {
      const i = zf(n.namePrefix, e.map((o) => ct(o, n.nameKeys)));
      t([...e, n.create(i)]);
    },
    update: (i, o) => t(e.map((s, a) => a === i ? n.patch(s, o) : s)),
    remove: (i) => t(e.filter((o, s) => s !== i))
  };
}
function jc({ value: e, options: t, placeholder: n, allowEmpty: i, ariaLabel: o, onChange: s }) {
  if (!t)
    return /* @__PURE__ */ r.jsx(
      "input",
      {
        type: "text",
        "aria-label": o,
        value: e,
        placeholder: n,
        onChange: (l) => s(l.target.value)
      }
    );
  const a = e === "" || t.some((l) => l.value === e), c = Array.from(new Set(t.map((l) => l.group).filter((l) => !!l))), u = c.length > 0;
  return /* @__PURE__ */ r.jsxs("select", { "aria-label": o, value: e, onChange: (l) => s(l.target.value), children: [
    i ? /* @__PURE__ */ r.jsx("option", { value: "", children: n ?? "—" }) : null,
    a ? null : /* @__PURE__ */ r.jsxs("option", { value: e, disabled: !0, children: [
      e,
      " (unresolved)"
    ] }),
    u ? c.map((l) => /* @__PURE__ */ r.jsx("optgroup", { label: l, children: t.filter((d) => d.group === l).map((d) => /* @__PURE__ */ r.jsx("option", { value: d.value, children: d.label }, d.value)) }, l)) : t.map((l) => /* @__PURE__ */ r.jsx("option", { value: l.value, children: l.label }, l.value))
  ] });
}
const vb = {
  Single: "Single",
  Array: "Array",
  List: "List",
  HashSet: "Hash set"
};
function bb({ value: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ r.jsx("select", { "aria-label": t, value: e, onChange: (i) => n(i.target.value), children: mb.map((i) => /* @__PURE__ */ r.jsx("option", { value: i, children: vb[i] }, i)) });
}
function Nb(e, t) {
  return t === "" ? !0 : e === "checkbox" ? t === "true" || t === "false" || t === "True" || t === "False" : e === "number" ? Number.isFinite(Number(t.trim())) && t.trim() !== "" : e === "date" ? /^\d{4}-\d{2}-\d{2}$/.test(t) : !0;
}
function jb({ value: e, editor: t, ariaLabel: n, onChange: i }) {
  const o = Nb(t, e);
  return o && t === "checkbox" ? /* @__PURE__ */ r.jsx(
    "input",
    {
      type: "checkbox",
      "aria-label": n,
      checked: e === "true" || e === "True",
      onChange: (s) => i(s.target.checked ? "true" : "false")
    }
  ) : o && (t === "number" || t === "date") ? /* @__PURE__ */ r.jsx(
    "input",
    {
      type: t,
      "aria-label": n,
      value: e,
      placeholder: "(empty)",
      onChange: (s) => i(s.target.value)
    }
  ) : /* @__PURE__ */ r.jsx(
    "input",
    {
      type: "text",
      "aria-label": n,
      value: e,
      placeholder: "(empty)",
      disabled: t === "none",
      onChange: (s) => i(s.target.value)
    }
  );
}
function Sb({ title: e, addLabel: t, emptyLabel: n, headers: i, isEmpty: o, onAdd: s, children: a }) {
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-properties-section-head", children: [
      /* @__PURE__ */ r.jsx("h3", { children: e }),
      /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-properties-add", onClick: s, children: [
        /* @__PURE__ */ r.jsx(Qt, { size: 14 }),
        " ",
        t
      ] })
    ] }),
    o ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: n }) : /* @__PURE__ */ r.jsxs("table", { className: "wf-properties-table", children: [
      /* @__PURE__ */ r.jsx("thead", { children: /* @__PURE__ */ r.jsxs("tr", { children: [
        i.map((c) => /* @__PURE__ */ r.jsx("th", { children: c }, c)),
        /* @__PURE__ */ r.jsx("th", { "aria-label": "Actions" })
      ] }) }),
      /* @__PURE__ */ r.jsx("tbody", { children: a })
    ] })
  ] });
}
function Cb({ label: e, onRemove: t }) {
  return /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-properties-remove", "aria-label": e, title: e, onClick: t, children: /* @__PURE__ */ r.jsx(Dn, { size: 14 }) }) });
}
function Eb({ checked: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ r.jsx("input", { type: "checkbox", "aria-label": t, checked: e, onChange: (i) => n(i.target.checked) });
}
function Ds({
  items: e,
  typeOptions: t,
  storageOptions: n,
  editorForAlias: i,
  namePrefix: o,
  nameKeys: s,
  title: a,
  addLabel: c,
  emptyLabel: u,
  create: l,
  patch: d,
  columns: f,
  warnings: p,
  onChange: h
}) {
  const { add: y, update: v, remove: w } = wb(e, h, {
    namePrefix: o,
    nameKeys: s,
    create: (g) => l(g, xb(t)),
    patch: d
  }), x = ["Name", "Type", "Collection", ...f.default ? ["Default"] : [], ...f.storage ? ["Storage"] : [], ...f.required ? ["Required"] : []], b = o.toLowerCase();
  return /* @__PURE__ */ r.jsx(
    Sb,
    {
      title: a,
      addLabel: c,
      emptyLabel: u,
      headers: x,
      isEmpty: e.length === 0,
      onAdd: y,
      children: e.map((g, m) => {
        const j = ct(g, s), N = os(g), S = ct(g, ol), k = S ? p?.get(S) : void 0, _ = N.collectionKind === "Single" ? i(N.alias) : "text";
        return /* @__PURE__ */ r.jsxs("tr", { children: [
          /* @__PURE__ */ r.jsxs("td", { children: [
            /* @__PURE__ */ r.jsx("input", { type: "text", "aria-label": `${o} name`, value: j, onChange: (M) => v(m, { name: M.target.value }) }),
            k ? /* @__PURE__ */ r.jsx("span", { className: "wf-properties-warning", role: "note", title: k, children: k }) : null
          ] }),
          /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            jc,
            {
              ariaLabel: `${o} type`,
              value: N.alias,
              options: t,
              placeholder: "Type",
              onChange: (M) => v(m, { type: { alias: M, collectionKind: N.collectionKind } })
            }
          ) }),
          /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            bb,
            {
              ariaLabel: `${o} collection kind`,
              value: N.collectionKind,
              onChange: (M) => v(m, { type: { alias: N.alias, collectionKind: M } })
            }
          ) }),
          f.default ? /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            jb,
            {
              ariaLabel: `${o} default value`,
              value: Hf(g.default),
              editor: _,
              onChange: (M) => v(m, { default: Of(M) })
            }
          ) }) : null,
          f.storage ? /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            jc,
            {
              ariaLabel: `${o} storage driver`,
              value: ct(g, Uf),
              options: n,
              placeholder: "—",
              allowEmpty: !0,
              onChange: (M) => v(m, { storageDriverType: M || null })
            }
          ) }) : null,
          f.required ? /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            Eb,
            {
              ariaLabel: `${o} required`,
              checked: g.isRequired === !0,
              onChange: (M) => v(m, { isRequired: M })
            }
          ) }) : null,
          /* @__PURE__ */ r.jsx(Cb, { label: `Remove ${b} ${j || m + 1}`, onRemove: () => w(m) })
        ] }, m);
      })
    }
  );
}
function Ad({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, title: o = "Variables", addLabel: s = "Add variable", emptyLabel: a = "No variables defined.", warnings: c, onChange: u }) {
  return /* @__PURE__ */ r.jsx(
    Ds,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Variable",
      nameKeys: qf,
      title: o,
      addLabel: s,
      emptyLabel: a,
      create: (l, d) => Lf({ name: l, alias: d }),
      patch: (l, d) => Vf(l, d),
      columns: { default: !0, storage: !0 },
      warnings: c,
      onChange: u
    }
  );
}
function kb({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, onChange: o }) {
  return /* @__PURE__ */ r.jsx(
    Ds,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Input",
      nameKeys: rs,
      title: "Inputs",
      addLabel: "Add input",
      emptyLabel: "No inputs defined.",
      create: (s, a) => Wf({ name: s, alias: a }),
      patch: (s, a) => Bf(s, a),
      columns: { default: !1, storage: !0, required: !0 },
      onChange: o
    }
  );
}
function Ib({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, onChange: o }) {
  return /* @__PURE__ */ r.jsx(
    Ds,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Output",
      nameKeys: rs,
      title: "Outputs",
      addLabel: "Add output",
      emptyLabel: "No outputs defined.",
      create: (s, a) => Ff({ name: s, alias: a }),
      patch: (s, a) => Kf(s, a),
      columns: { default: !1, storage: !1 },
      onChange: o
    }
  );
}
function Li(e) {
  return (e ?? []).filter(Pn);
}
function Ab({ context: e, variables: t, title: n, addLabel: i, emptyLabel: o, warnings: s, onChange: a }) {
  const { typeOptions: c, storageOptions: u, editorForAlias: l } = Id(e);
  return /* @__PURE__ */ r.jsx(
    Ad,
    {
      items: Li(t),
      typeOptions: c,
      storageOptions: u,
      editorForAlias: l,
      title: n,
      addLabel: i,
      emptyLabel: o,
      warnings: s,
      onChange: a
    }
  );
}
function _b({ definition: e, definitionId: t, onMetaChange: n }) {
  const i = !!n, [o, s] = K(e?.name ?? ""), [a, c] = K(e?.description ?? "");
  Q(() => {
    s(e?.name ?? "");
  }, [e?.name]), Q(() => {
    c(e?.description ?? "");
  }, [e?.description]);
  const u = () => {
    const d = o.trim();
    d && d !== (e?.name ?? "") ? n?.({ name: d }) : d || s(e?.name ?? "");
  }, l = () => {
    a !== (e?.description ?? "") && n?.({ description: a });
  };
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ r.jsx("h3", { children: "Information" }),
    /* @__PURE__ */ r.jsxs("dl", { className: "wf-properties-info", children: [
      /* @__PURE__ */ r.jsx("dt", { children: /* @__PURE__ */ r.jsx("label", { htmlFor: "wf-def-name", children: "Name" }) }),
      /* @__PURE__ */ r.jsx("dd", { children: i ? /* @__PURE__ */ r.jsx(
        "input",
        {
          id: "wf-def-name",
          type: "text",
          "aria-label": "Workflow name",
          value: o,
          onChange: (d) => s(d.target.value),
          onBlur: u
        }
      ) : e?.name ?? "—" }),
      /* @__PURE__ */ r.jsx("dt", { children: /* @__PURE__ */ r.jsx("label", { htmlFor: "wf-def-description", children: "Description" }) }),
      /* @__PURE__ */ r.jsx("dd", { children: i ? /* @__PURE__ */ r.jsx(
        "textarea",
        {
          id: "wf-def-description",
          "aria-label": "Workflow description",
          rows: 2,
          value: a,
          placeholder: "No description",
          onChange: (d) => c(d.target.value),
          onBlur: l
        }
      ) : e?.description?.trim() ? e.description : /* @__PURE__ */ r.jsx("span", { className: "wf-muted", children: "No description" }) }),
      /* @__PURE__ */ r.jsx("dt", { children: "Definition ID" }),
      /* @__PURE__ */ r.jsx("dd", { children: /* @__PURE__ */ r.jsx("code", { children: t }) })
    ] })
  ] });
}
function Db({ details: e, draft: t, context: n, onStateChange: i, onDefinitionMetaChange: o }) {
  const { typeOptions: s, storageOptions: a, editorForAlias: c } = Id(n), u = Li(t.state.variables), l = Li(t.state.inputs), d = Li(t.state.outputs), f = e?.versions ?? [];
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-properties-view", children: [
    /* @__PURE__ */ r.jsx(
      _b,
      {
        definition: e?.definition,
        definitionId: t.definitionId,
        onMetaChange: o
      }
    ),
    /* @__PURE__ */ r.jsx(
      Ad,
      {
        items: u,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, variables: p }))
      }
    ),
    /* @__PURE__ */ r.jsx(
      kb,
      {
        items: l,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, inputs: p }))
      }
    ),
    /* @__PURE__ */ r.jsx(
      Ib,
      {
        items: d,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, outputs: p }))
      }
    ),
    /* @__PURE__ */ r.jsxs("section", { className: "wf-properties-section", children: [
      /* @__PURE__ */ r.jsx("h3", { children: "Versions" }),
      f.length === 0 ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No published versions yet." }) : /* @__PURE__ */ r.jsx("ul", { className: "wf-properties-versions", children: f.map((p) => /* @__PURE__ */ r.jsxs("li", { children: [
        /* @__PURE__ */ r.jsxs("span", { className: "wf-properties-version-tag", children: [
          "v",
          p.version
        ] }),
        /* @__PURE__ */ r.jsx("time", { children: Re(p.createdAt) })
      ] }, p.id)) })
    ] })
  ] });
}
const Sc = "application/x-elsa-activity-version-id", Tb = 6, $b = 1200, Pb = 250, Mb = [10, 25, 50], Rb = 10, Cc = "elsa-studio-workflow-palette-width", Ec = "elsa-studio-workflow-inspector-width", kc = "elsa-studio-workflow-palette-collapsed", Ic = "elsa-studio-workflow-inspector-collapsed", _d = "elsa-studio-workflow-side-panel-maximized", Cn = 180, En = 460, zb = 260, Yt = 260, qt = 560, Lb = 320, Ac = 42, Ii = 16, Dd = Ke.createContext(null), Td = Ke.createContext(null), $d = Ke.createContext(null);
function Vb(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function Pd(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Pt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function Mt(e, t, n) {
  const i = t.createPrompt(n);
  return i ? (e.dispatchPrompt(i), !0) : !1;
}
function Ob(e) {
  if (!e) return null;
  const t = [], n = e.match(/```(?:json)?\s*([\s\S]*?)```/i);
  n && t.push(n[1]);
  const i = e.match(/\{[\s\S]*?\}/);
  i && t.push(i[0]);
  for (const a of t)
    try {
      const c = JSON.parse(a.trim()), u = _c(c.name, c.description);
      if (u) return u;
    } catch {
    }
  const o = e.match(/name\s*[:-]\s*(.+)/i)?.[1], s = e.match(/description\s*[:-]\s*(.+)/i)?.[1];
  return _c(o, s);
}
function _c(e, t) {
  const n = typeof e == "string" ? Dc(e) : void 0, i = typeof t == "string" ? Dc(t) : void 0;
  return n || i ? { name: n || void 0, description: i || void 0 } : null;
}
function Dc(e) {
  return e.trim().replace(/,$/, "").trim().replace(/^["']/, "").replace(/["']$/, "").trim();
}
function Hb(e, t) {
  return e.rootActivityVersionId ?? Md(t, e.rootKind)?.activityVersionId ?? null;
}
function Md(e, t) {
  return e.find((n) => Wb(n) === t);
}
function Wb(e) {
  return e ? Bb(e) ? "flowchart" : Fb(e) ? "sequence" : null : null;
}
function Br(e) {
  return Cl(e, (t) => t.category).map((t) => ({
    category: t.category,
    activities: t.items.sort((n, i) => Ce(n).localeCompare(Ce(i)))
  }));
}
function Bb(e) {
  return Ce(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function Fb(e) {
  return Ce(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function Kb(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function Rd(e) {
  return Zb(e.rootActivityType) || e.rootActivityType;
}
function Xb(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function Yb(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function qb(e, t) {
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
async function Ub(e) {
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
function Zb(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function Gb(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    Ai(t, n.typeName, n), Ai(t, n.name, n), Ai(t, n.displayName, n);
    const i = n.typeName.split(".").filter(Boolean).at(-1);
    Ai(t, i, n);
  }
  return t;
}
function Jb(e, t, n) {
  const i = t.get(e.activityVersionId);
  return n.get(kn(i?.activityTypeKey)) ?? n.get(kn(It(i?.activityTypeKey))) ?? n.get(kn(i?.displayName)) ?? n.get(kn(e.activityVersionId)) ?? null;
}
function Ai(e, t, n) {
  const i = kn(t);
  i && !e.has(i) && e.set(i, n);
}
function kn(e) {
  return e?.trim().toLowerCase() ?? "";
}
function $c(e, t, n, i) {
  const o = bo();
  if (!o) return t;
  const s = o.getItem(e);
  if (s == null) return t;
  const a = Number(s);
  return Number.isFinite(a) ? Vi(a, n, i) : t;
}
function Pc(e, t) {
  const n = bo();
  if (!n) return t;
  const i = n.getItem(e);
  return i === "true" ? !0 : i === "false" ? !1 : t;
}
function Qb() {
  const e = bo();
  if (!e) return null;
  const t = e.getItem(_d);
  return t === "palette" || t === "inspector" ? t : null;
}
function bo() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function vn(e, t) {
  const n = bo();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function Vi(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function Vd(e) {
  switch (eN(e)) {
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
function eN(e) {
  return (e ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}
function tN(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function Fr(e) {
  return `${Ce(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function Mc(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function nN(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function Od(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function iN(e) {
  const t = Od(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function oN(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function Fe(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function rN(e) {
  return Bd(Fe(e));
}
function sN(e, t) {
  const n = t?.get(e.activityVersionId);
  return n ? Ce(n) : It(e.activityVersionId) ?? e.activityVersionId;
}
function aN(e, t) {
  if (e.cardinality === "single") {
    const i = e.activities[0];
    return i ? sN(i, t) : "Empty — click to choose";
  }
  const n = e.activities.length;
  return `${n} activit${n === 1 ? "y" : "ies"}`;
}
function Hd(e, t, n = []) {
  if (!e) return n;
  const i = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: i?.activityTypeKey ?? e.activityVersionId,
    displayName: i ? Ce(i) : void 0
  });
  for (const o of ze(e, t))
    for (const s of o.activities) Hd(s, t, n);
  return n;
}
function Wd(e, t, n = []) {
  if (!e) return n;
  for (const i of ml(e))
    n.push({ source: i.source, target: i.target, sourcePort: i.sourceHandle ?? void 0, targetPort: i.targetHandle ?? void 0 });
  for (const i of ze(e, t))
    for (const o of i.activities) Wd(o, t, n);
  return n;
}
function In(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function cN(e) {
  return `${e.id}-${Bd(JSON.stringify(e.state))}`;
}
function Bd(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function Ts(e) {
  return e.status.toLowerCase() === "rejected";
}
function lN(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function uN(e, t) {
  const n = e instanceof Error ? e.message : String(e);
  return dN(e, n) ? `Run ${t} was not found.` : n;
}
function dN(e, t) {
  if ((typeof e == "object" && e ? e.response?.status ?? e.status : void 0) === 404 || /\b404\b/.test(t)) return !0;
  try {
    const i = JSON.parse(t);
    return [i.error, i.title, i.detail].some((o) => typeof o == "string" && /not found/i.test(o));
  } catch {
    return /not found/i.test(t);
  }
}
function hn({ status: e, subStatus: t }) {
  return /* @__PURE__ */ r.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
const Fd = { workflowActivity: fN }, Kd = { workflow: hN };
function fN({ id: e, data: t, selected: n }) {
  const i = t, o = i.runtime, s = !i.suppressFlowPorts, a = s ? i.sourcePorts.length > 0 ? i.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], c = pN(i), l = Ke.useContext(Td)?.({ activityVersionId: i.activityVersionId, activityTypeKey: i.activityTypeKey }) ?? null, d = Ke.useContext($d), f = i.onEnterSlot ?? (d ? (p) => d(e, i.label, p) : void 0);
  return /* @__PURE__ */ r.jsxs(
    "div",
    {
      className: ["wf-node", n ? "selected" : "", o ? "wf-node-runtime" : "", o?.hasBlockingIncident ? "faulted" : "", l ? "wf-node-unavailable" : ""].filter(Boolean).join(" "),
      "data-icon": i.icon ?? "activity",
      children: [
        s && i.acceptsInbound ? /* @__PURE__ */ r.jsx(cn, { type: "target", position: re.Left }) : null,
        l ? /* @__PURE__ */ r.jsx("span", { className: "wf-node-availability", title: `No longer available for new use · ${Rn(l.state)}`, children: /* @__PURE__ */ r.jsx(Hi, { size: 13 }) }) : null,
        /* @__PURE__ */ r.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ r.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: co(i.icon) }),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-node-copy", children: [
            /* @__PURE__ */ r.jsx("strong", { children: i.label }),
            c ? /* @__PURE__ */ r.jsx("small", { children: c }) : null
          ] })
        ] }),
        i.childSlots.length > 0 ? f ? /* @__PURE__ */ r.jsx("span", { className: "wf-node-slot-list", children: i.childSlots.map((p) => /* @__PURE__ */ r.jsx(
          "button",
          {
            type: "button",
            className: "wf-node-slot-badge nodrag",
            onClick: (h) => {
              h.stopPropagation(), f(p);
            },
            children: p.label
          },
          p.id
        )) }) : /* @__PURE__ */ r.jsxs("span", { className: "wf-node-slot-badge", children: [
          i.childSlots.length,
          " slot",
          i.childSlots.length === 1 ? "" : "s"
        ] }) : null,
        o ? /* @__PURE__ */ r.jsxs("div", { className: "wf-node-runtime-strip", children: [
          o.status ? /* @__PURE__ */ r.jsx(hn, { status: o.status, subStatus: o.subStatus }) : null,
          o.incidentCount > 0 ? /* @__PURE__ */ r.jsxs("span", { className: "wf-node-runtime-count", children: [
            o.incidentCount,
            " incident",
            o.incidentCount === 1 ? "" : "s"
          ] }) : null,
          o.faultCount > 0 ? /* @__PURE__ */ r.jsxs("span", { className: "wf-node-runtime-count", children: [
            o.faultCount,
            " faults"
          ] }) : null
        ] }) : null,
        a.map((p, h) => {
          const y = `${(h + 1) / (a.length + 1) * 100}%`;
          return /* @__PURE__ */ r.jsxs(Ke.Fragment, { children: [
            /* @__PURE__ */ r.jsx("span", { className: "wf-node-port-label", style: { top: y }, children: p.displayName }),
            /* @__PURE__ */ r.jsx(cn, { type: "source", position: re.Right, id: p.name, style: { top: y } })
          ] }, p.name);
        })
      ]
    }
  );
}
function pN(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((o) => !!o).join(" · ");
}
function hN(e) {
  const {
    id: t,
    sourceX: n,
    sourceY: i,
    targetX: o,
    targetY: s,
    sourcePosition: a,
    targetPosition: c,
    markerEnd: u,
    style: l,
    label: d,
    labelStyle: f
  } = e, p = Ke.useContext(Dd), [h, y] = K(!1), [v, w, x] = to({ sourceX: n, sourceY: i, targetX: o, targetY: s, sourcePosition: a, targetPosition: c }), b = p?.highlightedEdgeId === t;
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsx(
      ni,
      {
        id: t,
        path: v,
        markerEnd: u,
        style: {
          ...l,
          strokeWidth: b ? 2.5 : l?.strokeWidth
        },
        label: d,
        labelX: w,
        labelY: x,
        labelStyle: f,
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1)
      }
    ),
    p ? /* @__PURE__ */ r.jsx(R0, { children: /* @__PURE__ */ r.jsxs(
      "div",
      {
        className: ["wf-edge-actions", h ? "visible" : "", b ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${w}px, ${x}px)` },
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1),
        children: [
          /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (g) => p.requestInsertActivity(t, g.clientX, g.clientY), children: /* @__PURE__ */ r.jsx(Qt, { size: 12 }) }),
          /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => p.deleteEdge(t), children: /* @__PURE__ */ r.jsx(Dn, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function Xd({ clientX: e, clientY: t, activities: n, onPick: i, onClose: o }) {
  const [s, a] = K(""), [c, u] = K(0), l = oe(null), d = oe(null), f = de(() => {
    const b = s.trim().toLowerCase(), g = n.filter(Kb);
    return b ? g.filter((m) => Ce(m).toLowerCase().includes(b) || m.activityTypeKey.toLowerCase().includes(b) || (m.category ?? "").toLowerCase().includes(b) || (m.description ?? "").toLowerCase().includes(b)) : g;
  }, [n, s]), p = de(() => Br(f), [f]), h = de(() => p.flatMap((b) => b.activities), [p]);
  Q(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), Q(() => {
    const b = (m) => {
      l.current?.contains(m.target) || o();
    }, g = (m) => {
      m.key === "Escape" && o();
    };
    return document.addEventListener("mousedown", b, !0), document.addEventListener("keydown", g), () => {
      document.removeEventListener("mousedown", b, !0), document.removeEventListener("keydown", g);
    };
  }, [o]);
  const y = (b) => {
    if (b.key === "ArrowDown")
      b.preventDefault(), u((g) => Math.min(g + 1, h.length - 1));
    else if (b.key === "ArrowUp")
      b.preventDefault(), u((g) => Math.max(g - 1, 0));
    else if (b.key === "Enter") {
      b.preventDefault();
      const g = h[c];
      g && i(g);
    }
  }, v = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), w = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let x = -1;
  return /* @__PURE__ */ r.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: v, top: w }, onMouseDown: (b) => b.stopPropagation(), onClick: (b) => b.stopPropagation(), children: [
    /* @__PURE__ */ r.jsx(
      "input",
      {
        ref: d,
        type: "search",
        value: s,
        placeholder: "Search activities...",
        "aria-label": "Search activities",
        onChange: (b) => {
          a(b.target.value), u(0);
        },
        onKeyDown: y
      }
    ),
    /* @__PURE__ */ r.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: p.length === 0 ? /* @__PURE__ */ r.jsx("p", { children: "No matching activities." }) : p.map((b) => /* @__PURE__ */ r.jsxs("section", { children: [
      /* @__PURE__ */ r.jsx("h4", { children: b.category }),
      b.activities.map((g) => {
        x += 1;
        const m = x, j = m === c;
        return /* @__PURE__ */ r.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": j,
            className: j ? "active" : "",
            onMouseEnter: () => u(m),
            onClick: () => i(g),
            children: [
              /* @__PURE__ */ r.jsx("strong", { children: Ce(g) }),
              /* @__PURE__ */ r.jsx("small", { children: g.category || g.activityTypeKey })
            ]
          },
          g.activityVersionId
        );
      })
    ] }, b.category)) })
  ] });
}
function Oi({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: i
}) {
  const o = Af(t.map((s) => s.id), n, i);
  return /* @__PURE__ */ r.jsx("div", { className: "wf-panel-tab-list", role: "tablist", "aria-label": e, onKeyDown: o, children: t.map((s) => {
    const a = s.id === n;
    return /* @__PURE__ */ r.jsxs(
      "button",
      {
        "data-tab-id": s.id,
        type: "button",
        role: "tab",
        "aria-selected": a,
        tabIndex: a ? 0 : -1,
        className: a ? "active" : "",
        title: s.title,
        onClick: () => i(s.id),
        children: [
          s.icon ? /* @__PURE__ */ r.jsx("span", { className: "wf-panel-tab-icon", "aria-hidden": "true", children: s.icon }) : null,
          /* @__PURE__ */ r.jsx("span", { children: s.title })
        ]
      },
      s.id
    );
  }) });
}
function Rc(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
const gN = "Expressions/UnresolvedVariable";
function yN(e) {
  return String(e.type ?? e.code ?? "");
}
function mN(e) {
  return yN(e) === gN;
}
function xN(e) {
  const t = String(e ?? "").trim().split("/").filter(Boolean), [n, i, ...o] = t, s = n === "$workflow";
  return {
    nodeId: !n || s ? null : n,
    isWorkflowScope: s,
    bag: i === "inputs" || i === "outputs" || i === "variables" ? i : null,
    referenceKey: o.length > 0 ? o.join("/") : null
  };
}
function wN(e) {
  return (e ?? []).filter(mN).map((t) => ({
    error: t,
    path: xN(t.path),
    message: t.message ?? "Variable reference is not visible from this activity's scope."
  }));
}
function vN({ draft: e, onRepair: t }) {
  const n = e.validationErrors;
  if (!n.length)
    return /* @__PURE__ */ r.jsxs("div", { className: "wf-validation ok", children: [
      /* @__PURE__ */ r.jsx(ln, { size: 14 }),
      " No validation errors"
    ] });
  const i = wN(n), o = new Map(i.map((s) => [s.error, s]));
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-validation-summary", children: [
      /* @__PURE__ */ r.jsx(vt, { size: 14 }),
      n.length,
      " validation issue",
      n.length === 1 ? "" : "s",
      i.length > 0 ? /* @__PURE__ */ r.jsxs("span", { className: "wf-validation-variable-count", children: [
        " · ",
        i.length,
        " invalid variable reference",
        i.length === 1 ? "" : "s"
      ] }) : null
    ] }),
    /* @__PURE__ */ r.jsx("ul", { className: "wf-validation-list", children: n.map((s, a) => {
      const c = o.get(s);
      return /* @__PURE__ */ r.jsxs("li", { className: c ? "wf-validation-item repairable" : "wf-validation-item", children: [
        /* @__PURE__ */ r.jsx("span", { className: "wf-validation-message", children: s.message ?? "Validation issue." }),
        c?.path.nodeId ? /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-validation-repair", onClick: () => t(c.path.nodeId), children: [
          /* @__PURE__ */ r.jsx(xf, { size: 12 }),
          " Repair"
        ] }) : null
      ] }, a);
    }) })
  ] });
}
function bN({
  testRun: e,
  onOpenDetails: t
}) {
  const n = Ts(e);
  return /* @__PURE__ */ r.jsx("div", { className: "wf-test-run-status", "data-state": n ? "rejected" : "accepted", children: /* @__PURE__ */ r.jsxs(
    "button",
    {
      type: "button",
      className: "wf-test-run-trigger",
      onClick: t,
      children: [
        n ? /* @__PURE__ */ r.jsx(vt, { size: 16 }) : /* @__PURE__ */ r.jsx(ln, { size: 16 }),
        n ? "Test run rejected" : "Test run dispatched"
      ]
    }
  ) });
}
function NN({ testRun: e, onOpenRun: t }) {
  if (!e)
    return /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Run the draft to see Runtime Evidence." }) });
  const n = Ts(e), i = e.workflowExecutionId;
  return /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ r.jsxs("section", { className: "wf-runtime-card", "data-state": n ? "rejected" : "accepted", children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("span", { children: "Latest Test Run" }),
        /* @__PURE__ */ r.jsx("h3", { children: n ? "Rejected by the server" : "Transient run accepted" })
      ] }),
      /* @__PURE__ */ r.jsx(hn, { status: e.status, subStatus: e.commandDispatchStatus ?? void 0 })
    ] }),
    /* @__PURE__ */ r.jsx("p", { children: "Ephemeral - not saved, promoted, or published." }),
    n && e.reason ? /* @__PURE__ */ r.jsxs("div", { className: "wf-runtime-reason", children: [
      /* @__PURE__ */ r.jsx(vt, { size: 14 }),
      " ",
      e.reason
    ] }) : null,
    /* @__PURE__ */ r.jsxs("dl", { className: "wf-runtime-meta", children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Dispatch" }),
        /* @__PURE__ */ r.jsx("dd", { title: e.commandDispatchStatus ?? e.status, children: e.commandDispatchStatus ?? e.status })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Test Run" }),
        /* @__PURE__ */ r.jsx("dd", { title: e.testRunId, children: e.testRunId })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Artifact" }),
        /* @__PURE__ */ r.jsx("dd", { title: e.artifactId ?? "None", children: e.artifactId ?? "None" })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Run / Instance" }),
        /* @__PURE__ */ r.jsx("dd", { title: i ?? "None", children: i ? /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => t(i), children: i }) : "None" })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Activities" }),
        /* @__PURE__ */ r.jsx("dd", { children: zc(e.activityCount, "activity") })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ r.jsx("dd", { children: zc(e.incidentCount, "incident") })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ r.jsx("dd", { title: e.expiresAt ? Re(e.expiresAt) : "None", children: e.expiresAt ? Re(e.expiresAt) : "None" })
      ] })
    ] })
  ] }) });
}
function zc(e, t) {
  return typeof e != "number" ? "Available on linked Run" : `${e} ${t}${e === 1 ? "" : "s"}`;
}
function $s({ rows: e = 5 }) {
  return /* @__PURE__ */ r.jsx("div", { className: "wf-grid", "aria-busy": "true", "aria-label": "Loading", children: Array.from({ length: e }).map((t, n) => /* @__PURE__ */ r.jsx("div", { className: "wf-skeleton wf-skeleton-row", style: { width: `${90 - n % 3 * 12}%` } }, n)) });
}
function Ps({ icon: e, title: t, description: n, action: i }) {
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-empty-state", role: "status", children: [
    /* @__PURE__ */ r.jsx("div", { className: "wf-empty-state-icon", "aria-hidden": !0, children: e ?? /* @__PURE__ */ r.jsx(qn, { size: 22 }) }),
    /* @__PURE__ */ r.jsx("h3", { children: t }),
    n ? /* @__PURE__ */ r.jsx("p", { children: n }) : null,
    i ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty-state-action", children: i }) : null
  ] });
}
function ii({ message: e, title: t = "Something went wrong" }) {
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-error-card", role: "alert", children: [
    /* @__PURE__ */ r.jsx(vt, { size: 18 }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-error-card-body", children: [
      /* @__PURE__ */ r.jsx("strong", { children: t }),
      /* @__PURE__ */ r.jsx("span", { children: e || "Please try again, or check that the Elsa server is reachable." })
    ] })
  ] });
}
function Yd({ status: e, run: t, compact: n = !1 }) {
  const i = () => {
    t?.workflowExecutionId && (window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(t.workflowExecutionId)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  };
  return /* @__PURE__ */ r.jsxs("div", { className: `wf-status-line${n ? " compact" : ""}`, children: [
    /* @__PURE__ */ r.jsx(ln, { size: n ? 13 : 14 }),
    /* @__PURE__ */ r.jsx("span", { children: e }),
    t?.workflowExecutionId ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: i, children: [
      "Open Run ",
      t.workflowExecutionId
    ] }) : null
  ] });
}
function Et({ value: e, ariaLabel: t, copiedLabel: n, onCopied: i, onCopyFailed: o }) {
  if (!e) return null;
  const s = async (a) => {
    a.preventDefault(), a.stopPropagation();
    try {
      await Ub(e), i(n);
    } catch {
      o(n);
    }
  };
  return /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (a) => {
    s(a);
  }, children: /* @__PURE__ */ r.jsx(wf, { size: 12 }) });
}
function jN({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: i }) {
  const [o, s] = K("loading"), [a, c] = K(""), [u, l] = K(""), [d, f] = K(null), [p, h] = K([]), y = n?.trim().toLowerCase() ?? "", v = de(
    () => y ? p.filter((S) => Xb(S, y)) : p,
    [y, p]
  ), w = de(
    () => Array.from(new Set(p.flatMap((S) => [
      S.definitionId,
      S.definitionVersionId,
      S.sourceId
    ]).filter((S) => !!S))).sort((S, k) => S.localeCompare(k)),
    [p]
  ), x = Pt(t, "weaver.workflows.explain-executable"), b = se(async () => {
    s("loading"), c("");
    try {
      h(await jl(e)), s("ready");
    } catch (S) {
      c(S instanceof Error ? S.message : String(S)), s("failed");
    }
  }, [e]);
  Q(() => {
    b();
  }, [b]);
  const g = async (S) => {
    l(""), f(null), c("");
    try {
      const k = await Nl(e, S.artifactId), _ = Ld(k);
      f({ artifactId: S.artifactId, workflowExecutionId: _ }), l(`Started ${S.artifactId}`);
    } catch (k) {
      c(k instanceof Error ? k.message : String(k));
    }
  }, m = (S) => {
    x && Mt(t, x, S) && (c(""), f(null), l(`Sent ${S.artifactId} to Weaver`));
  }, j = (S) => {
    c(""), f(null), l(`Copied ${S}`);
  }, N = (S) => {
    l(""), f(null), c(`Could not copy ${S}.`);
  };
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => {
        b();
      }, children: "Refresh" }),
      /* @__PURE__ */ r.jsxs("label", { className: "wf-search wf-executable-definition-filter", children: [
        /* @__PURE__ */ r.jsx(io, { size: 14 }),
        /* @__PURE__ */ r.jsx(
          "input",
          {
            "aria-label": "Filter executables by workflow definition",
            list: "wf-executable-definition-options",
            placeholder: "Filter by definition ID",
            value: n ?? "",
            onChange: (S) => i(S.currentTarget.value || null)
          }
        )
      ] }),
      /* @__PURE__ */ r.jsx("datalist", { id: "wf-executable-definition-options", children: w.map((S) => /* @__PURE__ */ r.jsx("option", { value: S }, S)) }),
      n ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => i(null), children: [
        /* @__PURE__ */ r.jsx(es, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    o === "failed" ? /* @__PURE__ */ r.jsx(ii, { message: a }) : null,
    u ? /* @__PURE__ */ r.jsx(Yd, { status: u, run: d }) : null,
    o === "loading" ? /* @__PURE__ */ r.jsx($s, {}) : null,
    o === "ready" && v.length === 0 ? /* @__PURE__ */ r.jsx(
      Ps,
      {
        icon: /* @__PURE__ */ r.jsx(Jt, { size: 22 }),
        title: "No workflow executables",
        description: n ? "No executables match this definition filter." : "Publish a workflow definition to make it executable."
      }
    ) : null,
    o === "ready" && v.length > 0 ? /* @__PURE__ */ r.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ r.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ r.jsx("span", { children: "Version" }),
        /* @__PURE__ */ r.jsx("span", { children: "Source" }),
        /* @__PURE__ */ r.jsx("span", { children: "Root" }),
        /* @__PURE__ */ r.jsx("span", { children: "Published" }),
        /* @__PURE__ */ r.jsx("span", { children: "Actions" })
      ] }),
      v.map((S) => /* @__PURE__ */ r.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ r.jsxs("span", { className: "wf-artifact-cell", children: [
          /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line", children: [
            /* @__PURE__ */ r.jsx("strong", { title: S.artifactId, children: S.artifactId }),
            /* @__PURE__ */ r.jsx(Et, { value: S.artifactId, ariaLabel: `Copy artifact ID ${S.artifactId}`, copiedLabel: "artifact ID", onCopied: j, onCopyFailed: N })
          ] }),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
            /* @__PURE__ */ r.jsx("small", { title: S.artifactHash, children: S.artifactHash }),
            /* @__PURE__ */ r.jsx(Et, { value: S.artifactHash, ariaLabel: `Copy artifact hash ${S.artifactHash}`, copiedLabel: "artifact hash", onCopied: j, onCopyFailed: N })
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line wf-version-cell", children: [
          /* @__PURE__ */ r.jsx("span", { children: S.artifactVersion }),
          /* @__PURE__ */ r.jsx(Et, { value: S.artifactVersion, ariaLabel: `Copy artifact version ${S.artifactVersion}`, copiedLabel: "artifact version", onCopied: j, onCopyFailed: N })
        ] }),
        /* @__PURE__ */ r.jsx(SN, { executable: S, onCopied: j, onCopyFailed: N }),
        /* @__PURE__ */ r.jsx("span", { children: Rd(S) }),
        /* @__PURE__ */ r.jsx("span", { children: Re(S.publishedAt ?? S.createdAt) }),
        /* @__PURE__ */ r.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
            g(S);
          }, children: [
            /* @__PURE__ */ r.jsx(Jt, { size: 13 }),
            " Run"
          ] }),
          x ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => m(S), children: [
            /* @__PURE__ */ r.jsx(ut, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, S.artifactId))
    ] }) : null
  ] });
}
function SN({ executable: e, onCopied: t, onCopyFailed: n }) {
  const i = e.sourceId || e.definitionVersionId || e.definitionId, o = e.sourceVersion;
  return /* @__PURE__ */ r.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ r.jsx("span", { className: "wf-source-kind", children: zd(e.sourceKind) }),
    i ? /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line", children: [
      /* @__PURE__ */ r.jsx("code", { title: i, children: i }),
      /* @__PURE__ */ r.jsx(Et, { value: i, ariaLabel: `Copy source ID ${i}`, copiedLabel: "source ID", onCopied: t, onCopyFailed: n })
    ] }) : null,
    o ? /* @__PURE__ */ r.jsxs("small", { children: [
      "Version ",
      o
    ] }) : null
  ] });
}
function CN({ context: e, ai: t, definitionId: n, publishedArtifactId: i }) {
  const [o, s] = K("loading"), [a, c] = K(""), [u, l] = K(""), [d, f] = K(null), [p, h] = K([]), y = Pt(t, "weaver.workflows.explain-executable"), v = se(async () => {
    s("loading"), c("");
    try {
      const j = await jl(e);
      h(j.filter((N) => Yb(N, n)).sort(qb)), s("ready");
    } catch (j) {
      c(j instanceof Error ? j.message : String(j)), h([]), s("failed");
    }
  }, [e, n]);
  Q(() => {
    v();
  }, [v, i]);
  const w = async (j) => {
    l(""), f(null), c("");
    try {
      const N = await Nl(e, j.artifactId);
      f({ artifactId: j.artifactId, workflowExecutionId: Ld(N) }), l(`Started ${j.artifactId}`);
    } catch (N) {
      c(N instanceof Error ? N.message : String(N));
    }
  }, x = (j) => {
    y && Mt(t, y, j) && (c(""), f(null), l(`Sent ${j.artifactId} to Weaver`));
  }, b = () => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(n)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, g = (j) => {
    c(""), f(null), l(`Copied ${j}`);
  }, m = (j) => {
    l(""), f(null), c(`Could not copy ${j}.`);
  };
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-artifacts-panel", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-artifacts-toolbar", children: [
      /* @__PURE__ */ r.jsxs("span", { children: [
        p.length,
        " artifact",
        p.length === 1 ? "" : "s"
      ] }),
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
        v();
      }, children: [
        /* @__PURE__ */ r.jsx(ts, { size: 13 }),
        " Refresh"
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: b, children: "Open list" })
    ] }),
    o === "failed" ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ r.jsx(vt, { size: 14 }),
      " ",
      a
    ] }) : null,
    u ? /* @__PURE__ */ r.jsx(Yd, { status: u, run: d, compact: !0 }) : null,
    o === "loading" ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Loading artifacts..." }) : null,
    o === "ready" && p.length === 0 ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No published artifacts for this workflow yet." }) : null,
    o === "ready" && p.length > 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-artifact-list", role: "list", "aria-label": "Workflow artifacts", children: p.map((j) => /* @__PURE__ */ r.jsxs("article", { className: "wf-artifact-card", role: "listitem", "data-active": j.artifactId === i ? "true" : void 0, children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-artifact-card-heading", children: [
        /* @__PURE__ */ r.jsxs("div", { children: [
          /* @__PURE__ */ r.jsxs("span", { className: "wf-artifact-version", children: [
            "Version ",
            j.artifactVersion
          ] }),
          j.artifactId === i ? /* @__PURE__ */ r.jsx("span", { className: "wf-chip", children: "Latest publish" }) : null
        ] }),
        /* @__PURE__ */ r.jsx("span", { children: Re(j.publishedAt ?? j.createdAt) })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "wf-artifact-card-values", children: [
        /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line", children: [
          /* @__PURE__ */ r.jsx("code", { title: j.artifactId, children: j.artifactId }),
          /* @__PURE__ */ r.jsx(Et, { value: j.artifactId, ariaLabel: `Copy artifact ID ${j.artifactId}`, copiedLabel: "artifact ID", onCopied: g, onCopyFailed: m })
        ] }),
        /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ r.jsx("code", { title: j.artifactHash, children: j.artifactHash }),
          /* @__PURE__ */ r.jsx(Et, { value: j.artifactHash, ariaLabel: `Copy artifact hash ${j.artifactHash}`, copiedLabel: "artifact hash", onCopied: g, onCopyFailed: m })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("dl", { children: [
        /* @__PURE__ */ r.jsxs("div", { children: [
          /* @__PURE__ */ r.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ r.jsxs("dd", { children: [
            zd(j.sourceKind),
            " ",
            j.sourceVersion ? `v${j.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("div", { children: [
          /* @__PURE__ */ r.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ r.jsx("dd", { children: Rd(j) })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
          w(j);
        }, children: [
          /* @__PURE__ */ r.jsx(Jt, { size: 13 }),
          " Run"
        ] }),
        y ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => x(j), children: [
          /* @__PURE__ */ r.jsx(ut, { size: 13 }),
          " Explain"
        ] }) : null
      ] })
    ] }, j.artifactId)) }) : null
  ] });
}
function qd() {
  const [e, t] = K(() => $c(Cc, zb, Cn, En)), [n, i] = K(() => $c(Ec, Lb, Yt, qt)), [o, s] = K(() => Pc(kc, !1)), [a, c] = K(() => Pc(Ic, !1)), [u, l] = K(Qb);
  Q(() => {
    vn(Cc, String(e));
  }, [e]), Q(() => {
    vn(Ec, String(n));
  }, [n]), Q(() => {
    vn(kc, String(o));
  }, [o]), Q(() => {
    vn(Ic, String(a));
  }, [a]), Q(() => {
    vn(_d, u);
  }, [u]), Q(() => {
    if (!u) return;
    const g = (m) => {
      m.key === "Escape" && l(null);
    };
    return window.addEventListener("keydown", g), () => window.removeEventListener("keydown", g);
  }, [u]);
  const d = se((g) => {
    l((m) => m === g ? null : m), g === "palette" ? s((m) => !m) : c((m) => !m);
  }, []), f = se((g) => {
    g === "palette" ? s(!1) : c(!1), l((m) => m === g ? null : g);
  }, []), p = se((g, m) => {
    l(null), g === "palette" ? (s(!1), t((j) => Vi(j + m, Cn, En))) : (c(!1), i((j) => Vi(j + m, Yt, qt)));
  }, []), h = se((g, m) => {
    m.preventDefault(), l(null), g === "palette" ? s(!1) : c(!1);
    const j = m.clientX, N = g === "palette" ? e : n, S = g === "palette" ? Cn : Yt, k = g === "palette" ? En : qt;
    document.body.classList.add("wf-side-panel-resizing");
    const _ = (I) => {
      const $ = g === "palette" ? I.clientX - j : j - I.clientX, L = Vi(N + $, S, k);
      g === "palette" ? t(L) : i(L);
    }, M = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", _), window.removeEventListener("pointerup", M), window.removeEventListener("pointercancel", M);
    };
    window.addEventListener("pointermove", _), window.addEventListener("pointerup", M), window.addEventListener("pointercancel", M);
  }, [n, e]), y = se((g, m) => {
    m.key === "ArrowLeft" ? (m.preventDefault(), p(g, g === "palette" ? -Ii : Ii)) : m.key === "ArrowRight" ? (m.preventDefault(), p(g, g === "palette" ? Ii : -Ii)) : m.key === "Home" ? (m.preventDefault(), g === "palette" ? t(Cn) : i(Yt)) : m.key === "End" && (m.preventDefault(), g === "palette" ? t(En) : i(qt));
  }, [p]), v = !o && u !== "inspector", w = !a && u !== "palette", x = [
    "wf-editor-body",
    o ? "palette-collapsed" : "",
    a ? "inspector-collapsed" : "",
    u === "palette" ? "palette-maximized" : "",
    u === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), b = {
    "--wf-palette-width": `${o ? Ac : e}px`,
    "--wf-inspector-width": `${a ? Ac : n}px`
  };
  return {
    paletteWidth: e,
    inspectorWidth: n,
    paletteCollapsed: o,
    inspectorCollapsed: a,
    maximizedSidePanel: u,
    setInspectorCollapsed: c,
    paletteExpanded: v,
    inspectorExpanded: w,
    editorBodyClassName: x,
    editorBodyStyle: b,
    toggleSidePanelCollapsed: d,
    toggleSidePanelMaximized: f,
    startSidePanelResize: h,
    handleSidePanelResizeKeyDown: y
  };
}
const EN = 50;
function Lc() {
  return { past: [], future: [] };
}
function kN(e) {
  return e.past.length > 0;
}
function IN(e) {
  return e.future.length > 0;
}
function Vc(e, t, n = EN) {
  const i = [...e.past, t];
  return i.length > n && i.splice(0, i.length - n), { past: i, future: [] };
}
function AN(e, t) {
  if (e.past.length === 0) return null;
  const n = e.past.slice(), i = n.pop();
  return { history: { past: n, future: [...e.future, t] }, snapshot: i };
}
function _N(e, t) {
  if (e.future.length === 0) return null;
  const n = e.future.slice(), i = n.pop();
  return { history: { past: [...e.past, t], future: n }, snapshot: i };
}
function DN({ draft: e, restoreDraft: t }) {
  const n = oe(Lc()), i = oe(null), o = oe(""), s = oe(!1), [a, c] = K(0), u = se((v) => {
    n.current = Lc(), i.current = v ? In(v) : null, o.current = v ? Fe(v) : "", s.current = !1, c(0);
  }, []);
  Q(() => {
    if (!e) return;
    if (s.current) {
      s.current = !1;
      return;
    }
    const v = Fe(e);
    if (v === o.current) return;
    const w = window.setTimeout(() => {
      const x = i.current;
      x && (n.current = Vc(n.current, x), c((b) => b + 1)), i.current = In(e), o.current = v;
    }, Pb);
    return () => window.clearTimeout(w);
  }, [e]);
  const l = se(() => {
    if (!e) return;
    const v = Fe(e);
    if (v === o.current) return;
    const w = i.current;
    w && (n.current = Vc(n.current, w)), i.current = In(e), o.current = v;
  }, [e]), d = se((v) => {
    s.current = !0, i.current = In(v), o.current = Fe(v), t(v), c((w) => w + 1);
  }, [t]), f = se(() => {
    if (!e) return;
    l();
    const v = AN(n.current, e);
    v && (n.current = v.history, d(v.snapshot));
  }, [e, l, d]), p = se(() => {
    if (!e) return;
    l();
    const v = _N(n.current, e);
    v && (n.current = v.history, d(v.snapshot));
  }, [e, l, d]), { canUndoNow: h, canRedoNow: y } = de(() => {
    const v = !!e && !!i.current && Fe(e) !== o.current;
    return {
      canUndoNow: kN(n.current) || v,
      canRedoNow: IN(n.current) && !v
    };
  }, [e, a]);
  return { resetHistory: u, undo: f, redo: p, canUndoNow: h, canRedoNow: y };
}
const TN = {
  draft: null,
  frames: [],
  selectedNodeId: null,
  testRun: null,
  publishedArtifactId: null
};
function $N(e, t) {
  switch (t.type) {
    case "draftLoaded":
      return { ...e, draft: t.draft, frames: [], selectedNodeId: null };
    case "draftReplacedByBatch":
      return {
        ...e,
        draft: t.draft,
        frames: [],
        selectedNodeId: t.selectedNodeId,
        testRun: null,
        publishedArtifactId: null
      };
    case "draftEdited": {
      const n = t.recipe(e);
      return n ? { ...e, draft: n } : e;
    }
    case "draftEditedAndSelected": {
      const n = t.recipe(e);
      return { ...e, draft: n ?? e.draft, selectedNodeId: t.selectedNodeId };
    }
    case "selectionChanged":
      return e.selectedNodeId === t.selectedNodeId ? e : { ...e, selectedNodeId: t.selectedNodeId };
    case "scopeNavigated":
      return { ...e, frames: t.frames, selectedNodeId: t.selectedNodeId };
    case "slotEntered":
      return { ...e, frames: [...e.frames, t.frame], selectedNodeId: t.selectedNodeId };
    case "testRunStarted":
      return { ...e, testRun: t.testRun };
    case "testRunCleared":
      return e.testRun === null ? e : { ...e, testRun: null };
    case "publishedArtifactChanged":
      return { ...e, publishedArtifactId: t.publishedArtifactId };
    default:
      return e;
  }
}
function PN() {
  const [e, t] = hf($N, TN), n = de(() => ({
    // Load a freshly fetched / applied draft (or clear it). Used by load(), JSON apply, and undo/redo.
    loadDraft(i) {
      t({ type: "draftLoaded", draft: i });
    },
    // Replace the draft from a Weaver batch apply/undo, invalidating scope, selection, run and artifact.
    replaceDraftByBatch(i, o) {
      t({ type: "draftReplacedByBatch", draft: i, selectedNodeId: o });
    },
    // In-place draft edit that keeps scope + selection.
    editDraft(i) {
      t({ type: "draftEdited", recipe: i });
    },
    // Draft edit that also selects a node (add/wrap root, add scoped activity).
    editDraftAndSelect(i, o) {
      t({ type: "draftEditedAndSelected", recipe: i, selectedNodeId: o });
    },
    select(i) {
      t({ type: "selectionChanged", selectedNodeId: i });
    },
    // Jump to an explicit breadcrumb path (breadcrumb clicks, variable-repair navigation).
    navigateToScope(i, o = null) {
      t({ type: "scopeNavigated", frames: i, selectedNodeId: o });
    },
    resetToRoot() {
      t({ type: "scopeNavigated", frames: [], selectedNodeId: null });
    },
    enterSlot(i, o, s, a = null) {
      t({ type: "slotEntered", frame: { ownerNodeId: i, slotId: o, label: s }, selectedNodeId: a });
    },
    startTestRun(i) {
      t({ type: "testRunStarted", testRun: i });
    },
    clearTestRun() {
      t({ type: "testRunCleared" });
    },
    setPublishedArtifact(i) {
      t({ type: "publishedArtifactChanged", publishedArtifactId: i });
    }
  }), []);
  return { state: e, ...n };
}
const MN = 320, RN = 140;
function zN(e, t, n) {
  return n === "sequence" ? LN(e) : VN(e, t);
}
function LN(e) {
  const t = /* @__PURE__ */ new Map();
  return e.forEach((n, i) => {
    t.set(n.id, { x: i * 280, y: 0 });
  }), t;
}
function VN(e, t) {
  const n = /* @__PURE__ */ new Map();
  if (e.length === 0) return n;
  const i = new Set(e.map((d) => d.id)), o = t.filter((d) => i.has(d.source) && i.has(d.target)), s = /* @__PURE__ */ new Set();
  for (const d of o)
    s.add(d.source), s.add(d.target);
  const a = /* @__PURE__ */ new Map();
  for (const d of e) a.set(d.id, 0);
  for (let d = 0; d < e.length; d += 1) {
    let f = !1;
    for (const p of o) {
      const h = (a.get(p.source) ?? 0) + 1;
      h > (a.get(p.target) ?? 0) && h <= e.length && (a.set(p.target, h), f = !0);
    }
    if (!f) break;
  }
  const c = Math.max(0, ...e.filter((d) => s.has(d.id)).map((d) => a.get(d.id) ?? 0)), u = s.size > 0 ? c + 1 : 0, l = /* @__PURE__ */ new Map();
  for (const d of e) {
    const f = s.has(d.id) ? a.get(d.id) ?? 0 : u, p = l.get(f);
    p ? p.push(d.id) : l.set(f, [d.id]);
  }
  for (const [d, f] of l)
    f.forEach((p, h) => {
      n.set(p, { x: d * MN, y: h * RN });
    });
  return n;
}
function ON(e, t, n, i, o) {
  if (!e) return { kind: "becomeRoot" };
  const s = Mn(e, t, o);
  if (!s)
    return t.length > 0 ? { kind: "staleFrames" } : ze(n, i)[0] ? { kind: "wrapRoot" } : { kind: "leafError" };
  const a = s.slot.cardinality === "single" ? s.slot.activities.find((c) => c.nodeId !== n.nodeId) ?? null : null;
  return { kind: "addToSlot", slot: s.slot, replacedActivity: a };
}
const HN = "root";
function WN(e) {
  return e.length === 0 ? HN : e.map((t) => `${t.ownerNodeId}:${t.slotId}`).join("/");
}
function BN(e, t) {
  return e.length === t.length && e.every((n, i) => n.id === t[i]);
}
function FN({
  draft: e,
  scope: t,
  scopeOwner: n,
  frames: i,
  catalog: o,
  catalogByVersion: s,
  isUnsupportedDesigner: a,
  isFlowchartDesigner: c,
  canAddActivitiesToCanvas: u,
  selectedNodeId: l,
  editDraft: d,
  editDraftAndSelect: f,
  select: p,
  resetToRoot: h,
  setStatus: y,
  setError: v
}) {
  const [w, x] = K([]), [b, g] = K([]), [m, j] = K(null), [N, S] = K(null), [k, _] = K(null), M = oe(null), I = oe(null), $ = oe(/* @__PURE__ */ new Map()), L = oe(/* @__PURE__ */ new Set()), C = oe(null), A = oe([]), E = oe(null), D = oe(null), P = oe(!1), T = de(() => WN(i), [i]);
  Q(() => () => {
    m && $.current.set(T, m.getViewport());
  }, [m, T]), Q(() => {
    if (C.current = T, !n) {
      A.current = [], x([]), g([]);
      return;
    }
    const z = a ? fl(n, o, e?.layout ?? []) : t ? dl(t, o, e?.layout ?? []) : { nodes: [], edges: [] };
    A.current = z.nodes.map((F) => F.id), x(z.nodes), g(z.edges);
  }, [o, e?.layout, a, t, n, T]), Q(() => {
    if (!m || C.current !== T || !BN(w, A.current)) return;
    C.current = null;
    const z = $.current.get(T), F = L.current.has(T);
    L.current.add(T), window.requestAnimationFrame(() => {
      z ? m.setViewport(z) : !F && w.length > 0 && m.fitView({ padding: 0.2 });
    });
  }, [w, m, T]);
  const B = se((z, F, U) => U ? [
    ...z.filter((ie) => ie.nodeId !== F),
    { nodeId: F, x: Math.round(U.x), y: Math.round(U.y) }
  ] : z, []), W = se((z, F) => {
    if (e?.state.rootActivity && a)
      return;
    const U = Wi(z, Fr(z)), ie = ON(e?.state.rootActivity, i, U, z, s);
    if (ie.kind === "becomeRoot") {
      f(
        ({ draft: ue }) => ue ? { ...ue, state: { ...ue.state, rootActivity: U } } : null,
        U.nodeId
      );
      return;
    }
    if (ie.kind === "leafError") {
      y(""), v("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
      return;
    }
    if (ie.kind === "staleFrames") {
      y(""), v("This slot could not be resolved — returning to the workflow root."), h();
      return;
    }
    if (ie.kind === "wrapRoot") {
      f(({ draft: ue }) => {
        const pe = ue?.state.rootActivity;
        return pe ? {
          ...ue,
          layout: B(ue.layout, pe.nodeId, F),
          state: { ...ue.state, rootActivity: Js(U, [], [pe], z) }
        } : null;
      }, e?.state.rootActivity?.nodeId ?? null), v(""), y(`Wrapped root in ${Ce(z)}`);
      return;
    }
    f(({ draft: ue, frames: pe }) => {
      if (!ue?.state.rootActivity) return null;
      const xe = Mn(ue.state.rootActivity, pe, s);
      if (!xe) return null;
      const Ae = xe.slot.cardinality === "single" ? [U] : [...xe.slot.activities, U], je = Js(ue.state.rootActivity, pe, Ae, s);
      return {
        ...ue,
        layout: B(ue.layout, U.nodeId, F),
        state: { ...ue.state, rootActivity: je }
      };
    }, U.nodeId), ie.replacedActivity && (v(""), y(`Replaced ${ie.slot.label} content`));
  }, [s, e?.state.rootActivity, i, a, f, h, B, v, y]), H = se((z, F) => {
    const U = Wi(z, Fr(z)), ie = {
      id: U.nodeId,
      type: "workflowActivity",
      position: F,
      selected: !0,
      data: {
        label: Ce(z),
        activityVersionId: z.activityVersionId,
        activityTypeKey: z.activityTypeKey,
        category: z.category,
        executionType: z.executionType,
        icon: fn(z),
        childSlots: ze(U, z),
        acceptsInbound: String(z.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: xl(U, z)
      }
    };
    return { activityNode: U, node: ie };
  }, []), q = se((z, F, U = []) => {
    a || d(({ draft: ie, frames: ue }) => {
      if (!ie) return null;
      const pe = xp(ie.layout, z), xe = ie.state.rootActivity;
      if (!xe) return { ...ie, layout: pe };
      const Ae = Mn(xe, ue, s);
      if (!Ae) return { ...ie, layout: pe };
      const je = yp(Ae, z, F, U), Le = Ae.slot.mode === "flowchart" ? mp(je, F) : je;
      return {
        ...ie,
        layout: pe,
        state: {
          ...ie.state,
          rootActivity: gp(xe, ue, Le, s)
        }
      };
    });
  }, [s, a, d]), V = se((z, F) => {
    if (!M.current) return null;
    const U = M.current.getBoundingClientRect();
    return m ? m.screenToFlowPosition({ x: z, y: F }) : {
      x: z - U.left,
      y: F - U.top
    };
  }, [m]), Y = se((z, F) => document.elementFromPoint(z, F)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), J = se((z, F, U) => {
    const ie = w.find((we) => we.id === F.source), ue = w.find((we) => we.id === F.target), pe = ie && ue ? nN(ie, ue) : ie ? Mc(ie) : U, xe = H(z, pe), je = [...w.map((we) => we.selected ? { ...we, selected: !1 } : we), xe.node], Le = Ep(b, F, xe.node.id);
    x(je), g(Le), p(xe.node.id), q(je, Le, [xe.activityNode]);
  }, [q, H, b, w, p]), Z = se((z, F, U) => {
    if (!u || !M.current) return !1;
    const ie = M.current.getBoundingClientRect();
    if (!(F >= ie.left && F <= ie.right && U >= ie.top && U <= ie.bottom)) return !1;
    const pe = V(F, U);
    if (!pe) return !1;
    if (c) {
      const xe = Y(F, U), Ae = xe ? b.find((je) => je.id === xe) : void 0;
      if (Ae)
        return J(z, Ae, pe), !0;
    }
    return W(z, pe), !0;
  }, [W, u, b, Y, c, J, V]);
  Q(() => {
    const z = (U) => {
      const ie = E.current;
      if (!ie) return;
      Math.hypot(U.clientX - ie.startX, U.clientY - ie.startY) >= Tb && (ie.dragging = !0);
    }, F = (U) => {
      const ie = E.current;
      if (E.current = null, !ie?.dragging || !M.current || D.current) return;
      const ue = M.current.getBoundingClientRect();
      U.clientX >= ue.left && U.clientX <= ue.right && U.clientY >= ue.top && U.clientY <= ue.bottom && (P.current = !0, window.setTimeout(() => {
        P.current = !1;
      }, 0), Z(ie.activity, U.clientX, U.clientY));
    };
    return window.addEventListener("pointermove", z), window.addEventListener("pointerup", F), window.addEventListener("pointercancel", F), () => {
      window.removeEventListener("pointermove", z), window.removeEventListener("pointerup", F), window.removeEventListener("pointercancel", F);
    };
  }, [m, Z]);
  const R = (z, F) => {
    D.current = { activityVersionId: F.activityVersionId, handledDrop: !1 }, z.dataTransfer.setData(Sc, F.activityVersionId), z.dataTransfer.setData("text/plain", F.activityVersionId), z.dataTransfer.effectAllowed = "copy";
  }, X = (z, F) => {
    const U = D.current;
    D.current = null, !U?.handledDrop && (z.clientX === 0 && z.clientY === 0 || Z(F, z.clientX, z.clientY) && (P.current = !0, window.setTimeout(() => {
      P.current = !1;
    }, 0)));
  }, le = (z, F) => {
    z.button === 0 && (E.current = {
      activity: F,
      startX: z.clientX,
      startY: z.clientY,
      dragging: !1
    });
  }, ce = (z) => {
    P.current || u && W(z);
  }, ee = (z) => {
    if (!u) {
      z.dataTransfer.dropEffect = "none";
      return;
    }
    if (z.preventDefault(), z.dataTransfer.dropEffect = "copy", !c) return;
    const F = Y(z.clientX, z.clientY);
    _(F);
  }, ne = (z) => {
    if (!M.current) return;
    const F = z.relatedTarget;
    F && M.current.contains(F) || _(null);
  }, fe = (z) => {
    z.preventDefault(), _(null);
    const F = z.dataTransfer.getData(Sc) || z.dataTransfer.getData("text/plain");
    if (!F || (z.stopPropagation(), D.current?.activityVersionId === F && (D.current.handledDrop = !0), !u)) return;
    const U = s.get(F);
    U && Z(U, z.clientX, z.clientY);
  }, O = (z) => {
    if (!u) return;
    if (z) {
      S({ kind: "fromEmpty", clientX: z.clientX, clientY: z.clientY });
      return;
    }
    const F = M.current?.getBoundingClientRect();
    F && S({
      kind: "fromEmpty",
      clientX: F.left + F.width / 2,
      clientY: F.top + F.height / 2
    });
  }, te = (z) => {
    const F = a ? z.filter((U) => U.type === "select") : z;
    F.length !== 0 && x((U) => Yu(F, U));
  }, ge = (z) => {
    a || g((F) => qu(z, F));
  }, ye = (z) => !z.source || !z.target || z.source === z.target || !c ? !1 : !z.targetHandle, _e = (z) => {
    if (!e?.state.rootActivity || !t || !c || !ye(z)) return;
    const F = Bi(z.source, z.target, z.sourceHandle ?? "Done", z.targetHandle ?? void 0), U = Zu(F, b);
    g(U), q(w, U);
  }, De = () => {
    q(w, b);
  }, $e = !a && w.length > 0, st = se(() => {
    if (a || w.length === 0) return;
    const z = t?.slot.mode === "sequence" ? "sequence" : "flowchart", F = zN(w, b, z), U = w.map((ie) => {
      const ue = F.get(ie.id);
      return ue ? { ...ie, position: ue } : ie;
    });
    x(U), q(U, b), window.requestAnimationFrame(() => m?.fitView({ padding: 0.2 })), y("Rearranged the canvas.");
  }, [b, w, t, a, q, m, y]), qe = (z, F) => {
    if (!F.nodeId || F.handleType === "target") {
      I.current = null;
      return;
    }
    I.current = {
      nodeId: F.nodeId,
      handleId: F.handleId ?? null
    };
  }, Ue = (z, F) => {
    const U = oN(I.current, F);
    if (I.current = null, !U || !c || F.toNode || F.toHandle || iN(z)) return;
    const ie = Od(z);
    S({
      kind: "fromPort",
      sourceNodeId: U.nodeId,
      sourceHandleId: U.handleId,
      clientX: ie.x,
      clientY: ie.y
    });
  }, Pe = (z, F) => {
    if (!c || !ye(F)) return;
    const U = pv(z, {
      ...F,
      sourceHandle: F.sourceHandle ?? "Done",
      // Connection.targetHandle is string | null; null and undefined are indistinguishable downstream
      // (every consumer checks truthiness), so keep the type-correct null.
      targetHandle: F.targetHandle ?? null
    }, b, { shouldReplaceId: !1 });
    g(U), q(w, U);
  }, He = (z) => {
    if (a || z.length === 0) return;
    const F = new Set(z.map((ue) => ue.id)), U = w.filter((ue) => !F.has(ue.id)), ie = b.filter((ue) => !F.has(ue.source) && !F.has(ue.target));
    x(U), g(ie), l && F.has(l) && p(null), q(U, ie);
  }, We = (z) => {
    if (a || z.length === 0) return;
    const F = new Set(z.map((ie) => ie.id)), U = b.filter((ie) => !F.has(ie.id));
    g(U), q(w, U);
  }, Ne = se((z) => {
    if (a) return;
    const F = b.filter((U) => U.id !== z);
    g(F), q(w, F);
  }, [q, b, a, w]), zt = se((z, F, U) => {
    c && S({ kind: "spliceEdge", edgeId: z, clientX: F, clientY: U });
  }, [c]), Ie = (z) => {
    const F = N;
    if (!F) return;
    S(null);
    const U = V(F.clientX, F.clientY) ?? { x: 0, y: 0 };
    if (F.kind === "fromEmpty") {
      const ue = H(z, U), xe = [...w.map((Ae) => Ae.selected ? { ...Ae, selected: !1 } : Ae), ue.node];
      x(xe), p(ue.node.id), q(xe, b, [ue.activityNode]);
      return;
    }
    if (F.kind === "fromPort") {
      const ue = w.find((we) => we.id === F.sourceNodeId), pe = ue ? Mc(ue) : U, xe = H(z, pe), je = [...w.map((we) => we.selected ? { ...we, selected: !1 } : we), xe.node], Le = [...b, Bi(F.sourceNodeId, xe.node.id, F.sourceHandleId ?? "Done")];
      x(je), g(Le), p(xe.node.id), q(je, Le, [xe.activityNode]);
      return;
    }
    const ie = b.find((ue) => ue.id === F.edgeId);
    ie && J(z, ie, U);
  }, gn = de(() => ({
    highlightedEdgeId: k,
    deleteEdge: Ne,
    requestInsertActivity: zt
  }), [Ne, k, zt]);
  return {
    nodes: w,
    edges: b,
    canvasRef: M,
    setReactFlowInstance: j,
    connectMenu: N,
    setConnectMenu: S,
    edgeActions: gn,
    onNodesChange: te,
    onEdgesChange: ge,
    onNodesDelete: He,
    onEdgesDelete: We,
    isValidConnection: ye,
    onConnect: _e,
    onConnectStart: qe,
    onConnectEnd: Ue,
    onReconnect: Pe,
    commitLayout: De,
    canAutoLayout: $e,
    autoLayout: st,
    onCanvasDragOver: ee,
    onCanvasDragLeave: ne,
    onCanvasDrop: fe,
    openEmptyConnectMenu: O,
    onConnectMenuPick: Ie,
    addActivity: W,
    onPaletteClick: ce,
    onPaletteDragStart: R,
    onPaletteDragEnd: X,
    onPalettePointerDown: le
  };
}
const Oc = "elsa-studio:apply-workflow-graph-operation-batch", Hc = "elsa-studio:undo-workflow-graph-operation-batch", KN = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function XN(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const i = ej(e), o = Zd(i.state.rootActivity), s = /* @__PURE__ */ new Map(), a = /* @__PURE__ */ new Map(), c = [];
  for (const u of t.operations) {
    const l = QN(u.kind), d = u.parameters ?? {};
    if (l === "add-activity") {
      const f = Ve(d.activityId) ?? u.temporaryReferences?.[0], p = JN(f ?? Ve(d.displayName) ?? Ve(d.activityType) ?? "weaver-activity", o), h = YN(u, p, n);
      a.set(p, h), c.push(p), f && s.set(f, p), i.state.rootActivity && qN(i.state.rootActivity, h);
      const y = wt(d.position) ? Kr(d.position, { x: 280, y: 160 }) : null;
      y && (i.layout = Wc(i.layout, p, y));
      continue;
    }
    if (l === "set-root") {
      const f = Sr(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown root activity.");
      i.state.rootActivity = f;
      continue;
    }
    if (l === "set-designer-position") {
      const f = Rt(d.activityId, s);
      if (!f || !Ms(i.state.rootActivity, f)) throw new Error("Weaver batch referenced an unknown activity position.");
      i.layout = Wc(i.layout, f, Kr(d, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const f = Sr(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity property target.");
      GN(f, Ve(d.propertyName) ?? "Value", d.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const f = Sr(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity update target.");
      const p = wt(d.patch) ? d.patch : d;
      Object.assign(f, p);
      continue;
    }
    if (l === "remove-activity") {
      const f = Rt(d.activityId, s);
      if (!f) throw new Error("Weaver batch referenced an unknown activity remove target.");
      i.state.rootActivity = Ud(i.state.rootActivity, f), i.layout = i.layout.filter((p) => p.nodeId !== f);
      continue;
    }
    if (l === "connect-activities") {
      UN(i, d, s);
      continue;
    }
    if (l === "disconnect-activities") {
      ZN(i, d, s);
      continue;
    }
    throw new Error(`Weaver batch operation '${String(u.kind || "unknown")}' is not supported by this designer apply path.`);
  }
  if (!i.state.rootActivity) throw new Error("Weaver batch did not produce a root activity.");
  return i.sourceVersionId = null, {
    draft: i,
    appliedCount: t.operations.length,
    finalActivityIds: c,
    temporaryReferences: Object.fromEntries(s),
    summary: `Applied ${t.operations.length} workflow operation${t.operations.length === 1 ? "" : "s"} to the working draft.`
  };
}
function YN(e, t, n) {
  const i = e.parameters ?? {}, o = Ve(i.activityVersionId) ?? Ve(i.activityType) ?? "Elsa.Workflows.Activity", s = n.find((a) => a.activityVersionId === o || a.activityTypeKey === o || a.displayName === Ve(i.displayName));
  return s ? Wi(s, t) : {
    nodeId: t,
    // catalogItem is provably undefined here (the truthy branch returned above), so use the id directly.
    activityVersionId: o,
    inputs: [],
    outputs: [],
    ...Ve(i.displayName) ? { displayName: Ve(i.displayName) } : {},
    designer: { position: Kr(i.position, { x: 280, y: 160 }) }
  };
}
function qN(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = Rs(e);
  n && !n.some((i) => i.nodeId === t.nodeId) && n.push(t);
}
function UN(e, t, n) {
  const i = e.state.rootActivity;
  if (!i?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const o = Rt(t.sourceActivityId ?? t.sourceId ?? t.from, n), s = Rt(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!o || !s) throw new Error("Weaver batch connection is missing source or target activity.");
  const a = i.structure.payload, c = Array.isArray(a.connections) ? a.connections : [], u = Ve(t.connectionId) ?? `flow-${o}-${s}`;
  a.connections = [
    ...c.filter((l) => !wt(l) || l.id !== u),
    {
      id: u,
      source: { nodeId: o, port: Ve(t.outcome) ?? Ve(t.sourcePort) ?? "Done" },
      target: { nodeId: s }
    }
  ];
}
function ZN(e, t, n) {
  const i = e.state.rootActivity, o = i?.structure?.payload.connections;
  if (!Array.isArray(o)) return;
  const s = Ve(t.connectionId), a = Rt(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = Rt(t.targetActivityId ?? t.targetId ?? t.to, n);
  i.structure.payload.connections = o.filter((u) => {
    if (!wt(u)) return !0;
    if (s && u.id === s) return !1;
    const l = wt(u.source) ? u.source.nodeId : void 0, d = wt(u.target) ? u.target.nodeId : void 0;
    return l !== a || d !== c;
  });
}
function GN(e, t, n) {
  const i = wt(n);
  e[Dl(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: i ? "Object" : "Literal", value: n }
  };
}
function Sr(e, t, n, i) {
  const o = Rt(t, n);
  return o ? Ms(e.state.rootActivity, o) ?? i.get(o) ?? null : null;
}
function Rt(e, t) {
  const n = Ve(e);
  return n ? t.get(n) ?? n : null;
}
function Ms(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of Gd(e)) {
    const i = Ms(n, t);
    if (i) return i;
  }
  return null;
}
function Ud(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = Rs(e);
  if (n) {
    const i = n.map((o) => Ud(o, t)).filter((o) => !!o);
    n.splice(0, n.length, ...i);
  }
  return e;
}
function Zd(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of Gd(e)) Zd(n, t);
  return t;
}
function Gd(e) {
  return Rs(e) ?? [];
}
function Rs(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function Wc(e, t, n) {
  return [
    ...e.filter((i) => i.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function Kr(e, t) {
  const n = wt(e) ? e : {}, i = Number(n.x), o = Number(n.y);
  return {
    x: Number.isFinite(i) ? Math.max(40, Math.round(i)) : t.x,
    y: Number.isFinite(o) ? Math.max(40, Math.round(o)) : t.y
  };
}
function JN(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let i = n, o = 2;
  for (; t.has(i); )
    i = `${n}-${o}`, o += 1;
  return t.add(i), i;
}
function QN(e) {
  return typeof e == "number" ? KN[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Ve(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function ej(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function wt(e) {
  return typeof e == "object" && e !== null;
}
function tj({
  draft: e,
  details: t,
  catalog: n,
  replaceDraftByBatch: i,
  setStatus: o,
  setError: s
}) {
  const a = oe(/* @__PURE__ */ new Map());
  Q(() => {
    const c = (l) => {
      const d = l.detail;
      if (!d?.batch || !d.respond) return;
      if (!e || !t) {
        d.respond({ ok: !1, message: "No active workflow draft is open." });
        return;
      }
      const f = d.batch.workflowDefinitionId;
      if (f && f !== "active-draft" && f !== t.definition.id) {
        d.respond({ ok: !1, message: `Batch targets workflow '${f}', but '${t.definition.id}' is active.` });
        return;
      }
      try {
        const p = In(e), h = XN(e, d.batch, n), y = `weaver-batch-${Date.now()}`;
        a.current.set(y, p), i(h.draft, h.finalActivityIds.at(-1) ?? null), o(h.summary), s(""), d.respond({ ok: !0, result: { ...h, undoToken: y } });
      } catch (p) {
        const h = p instanceof Error ? p.message : String(p);
        s(h), d.respond({ ok: !1, message: h });
      }
    }, u = (l) => {
      const d = l.detail;
      if (!d?.undoToken || !d.respond) return;
      const f = a.current.get(d.undoToken);
      if (!f) {
        d.respond({ ok: !1, message: "The Weaver batch undo point is no longer available." });
        return;
      }
      a.current.delete(d.undoToken), i(f, null), o("Restored workflow draft before Weaver batch."), s(""), d.respond({ ok: !0, summary: "Restored workflow draft before Weaver batch." });
    };
    return window.addEventListener(Oc, c), window.addEventListener(Hc, u), () => {
      window.removeEventListener(Oc, c), window.removeEventListener(Hc, u);
    };
  }, [n, t, e, i, o, s]);
}
function nj({ context: e, draft: t, autosaveEnabledByDefault: n = !0, editDraft: i, setStatus: o, setError: s }) {
  const [a, c] = K(n), u = oe(""), l = oe(0), d = oe(Promise.resolve()), f = se((h) => {
    u.current = h ? Fe(h) : "";
  }, []), p = se(async (h, y) => {
    const v = async () => {
      const x = ++l.current, b = Fe(h);
      s("");
      try {
        const g = await nh(e, h), m = Fe(g);
        return u.current = m, i(({ draft: j }) => !j || j.id !== g.id ? null : Fe(j) === b ? g : { ...j, validationErrors: g.validationErrors }), x === l.current && o(y), g;
      } catch (g) {
        throw x === l.current && (o(""), s(g instanceof Error ? g.message : String(g))), g;
      }
    }, w = d.current.then(v, v);
    return d.current = w.catch(() => {
    }), w;
  }, [e, i, o, s]);
  return Q(() => {
    if (!a || !t || Fe(t) === u.current) return;
    o("Autosaving...");
    const y = window.setTimeout(() => {
      p(t, "Autosaved").catch(() => {
      });
    }, $b);
    return () => window.clearTimeout(y);
  }, [a, t, p, o]), { saveDraft: p, autosaveEnabled: a, setAutosaveEnabled: c, markSaved: f };
}
function ij({ context: e, definitionId: t, resetHistory: n, loadDraft: i, markSaved: o, setError: s }) {
  const [a, c] = K(null), [u, l] = K([]), [d, f] = K([]), [p, h] = K(null), [y, v] = K(Ti), [w, x] = K("loading"), b = se(async () => {
    s(""), x("loading");
    const [g, m, j, N, S] = await Promise.all([
      Yp(e, t),
      so(e),
      fh(e).then(
        (_) => ({ ok: !0, descriptors: _ }),
        () => ({ ok: !1, descriptors: [] })
      ),
      ph(e).then(
        (_) => ({ ok: !0, descriptors: _ }),
        () => ({ ok: !1, descriptors: Ti })
      ),
      // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
      Sl(e).then(
        (_) => _,
        () => null
      )
    ]), k = g.draft ?? null;
    c(g), o(k), n(k), i(k), l(m.activities ?? []), f(j.descriptors), h(S), v(N.descriptors.length > 0 ? N.descriptors : Ti), x(j.ok ? "ready" : "failed");
  }, [e, t, n, i, o, s]);
  return Q(() => {
    b().catch((g) => s(g instanceof Error ? g.message : String(g)));
  }, [b, s]), {
    details: a,
    setDetails: c,
    catalog: u,
    activityDescriptors: d,
    availabilityDiagnostics: p,
    expressionDescriptors: y,
    descriptorStatus: w,
    reload: b
  };
}
function oj({ context: e, details: t, setDetails: n, setStatus: i }) {
  const o = oe(null), s = oe(null), a = oe({});
  Q(() => {
    o.current = t;
  }, [t]);
  const c = se(() => {
    s.current !== null && (window.clearTimeout(s.current), s.current = null);
    const l = a.current;
    a.current = {};
    const d = o.current?.definition;
    !d || l.name === void 0 && l.description === void 0 || th(e, d.id, {
      name: l.name ?? d.name,
      description: l.description ?? d.description ?? null
    }).then((f) => n((p) => p && p.definition.id === f.definition.id ? { ...p, definition: f.definition } : p)).catch(() => i("Couldn't save name/description."));
  }, [e, n, i]), u = se((l) => {
    n((d) => d && { ...d, definition: { ...d.definition, ...l } }), a.current = { ...a.current, ...l }, s.current !== null && window.clearTimeout(s.current), s.current = window.setTimeout(c, 800);
  }, [c, n]);
  return Q(() => () => {
    c();
  }, [c]), { updateDefinitionMeta: u };
}
function rj({
  context: e,
  draft: t,
  details: n,
  busy: i,
  saveDraft: o,
  reload: s,
  startTestRun: a,
  clearTestRun: c,
  setPublishedArtifact: u,
  setOperation: l,
  setStatus: d,
  setError: f,
  setActiveRightPanelId: p,
  setInspectorCollapsed: h
}) {
  const y = se(() => {
    if (!t) return;
    const b = n?.definition.name;
    db(cb(t, b), b), d("Exported workflow as JSON.");
  }, [t, n, d]), v = se(async () => {
    if (!(!t || i)) {
      l("saving"), d("Saving...");
      try {
        await o(t, "Saved");
      } catch {
      } finally {
        l("idle");
      }
    }
  }, [t, i, o, l, d]), w = se(async () => {
    if (!(!t || i)) {
      l("promoting"), d("Saving...");
      try {
        await o(t, "Saved"), d("Promoting...");
        const b = await ih(e, t.id), g = await oh(e, b.versionId);
        u(g.artifactId), d(`Published ${g.artifactVersion}`), await s();
      } catch (b) {
        d(""), f(b instanceof Error ? b.message : String(b));
      } finally {
        l("idle");
      }
    }
  }, [t, i, e, o, s, u, l, d, f]), x = se(async () => {
    if (!t?.state.rootActivity || i) return;
    const b = t, g = Fe(b);
    c(), d("Preparing test run...");
    try {
      l("testRunPreparing"), d("Preparing test run...");
      const m = cN(b);
      l("testRunStarting"), d("Starting test run...");
      const j = await rh(e, {
        definitionId: b.definitionId,
        snapshotId: m,
        state: b.state
      });
      a({ draftSignature: g, view: j }), p("runtime"), h(!1), d(Ts(j) ? "Test run rejected" : "Test run dispatched");
    } catch (m) {
      d(""), f(m instanceof Error ? m.message : String(m));
    } finally {
      l("idle");
    }
  }, [t, i, e, c, a, p, h, l, d, f]);
  return { exportJson: y, save: v, promoteAndPublish: w, run: x };
}
function sj({
  context: e,
  draft: t,
  frames: n,
  selectedNodeId: i,
  catalog: o,
  activityDescriptors: s,
  availabilityDiagnostics: a
}) {
  const c = t?.state.rootActivity ?? null, u = de(() => new Map(o.map((S) => [S.activityVersionId, S])), [o]), l = se(
    (S) => Mh([S.activityVersionId, S.activityTypeKey], a),
    [a]
  ), d = de(() => Gb(s), [s]), f = de(() => al(c, n, u), [c, n, u]), p = pl(f, f ? u.get(f.activityVersionId) : void 0), h = !!f && p === "unsupported", y = de(() => h ? null : Mn(c, n, u), [c, n, u, h]), v = de(() => h && f?.nodeId === i ? f : y?.slot.activities.find((S) => S.nodeId === i) ?? null, [h, y, f, i]), w = de(
    () => v ? Jb(v, u, d) : null,
    [u, d, v]
  ), x = de(
    () => v ? l({ activityVersionId: v.activityVersionId, activityTypeKey: u.get(v.activityVersionId)?.activityTypeKey }) : null,
    [l, u, v]
  ), b = v ? ze(v, u) : [], g = v ? Rp(v, u.get(v.activityVersionId)) : !1, m = Up(e, t?.state, i, u), j = !h && y?.slot.mode === "flowchart";
  return {
    catalogByVersion: u,
    availabilityLookup: l,
    scopeOwner: f,
    isUnsupportedDesigner: h,
    scope: y,
    selectedNode: v,
    selectedDescriptor: w,
    selectedNodeAvailability: x,
    selectedSlots: b,
    selectedSupportsScopedVariables: g,
    scopedVariableAnalysis: m,
    isFlowchartDesigner: j,
    canAddActivitiesToCanvas: !c || !h
  };
}
function aj({
  details: e,
  draft: t,
  selectedNode: n,
  selectedNodeId: i,
  selectedDescriptor: o,
  catalogByVersion: s
}) {
  Q(() => {
    if (!(!e || !t))
      return window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
        workflowId: e.definition.id,
        workflowDefinitionId: e.definition.id,
        workflowVersionId: t.sourceVersionId ?? null,
        draftId: t.id,
        revision: rN(t),
        selectedNodeId: i,
        selectedActivityType: o?.typeName ?? (n ? s.get(n.activityVersionId)?.activityTypeKey ?? n.activityVersionId : null),
        summary: e.definition.name,
        activities: Hd(t.state.rootActivity, s),
        connections: Wd(t.state.rootActivity, s),
        diagnostics: t.validationErrors.map((a) => ({ severity: a.code ?? "warning", message: a.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === e.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [s, e, t, o, n, i]);
}
function cj({
  paletteSearch: e,
  onSearchChange: t,
  groups: n,
  expandedCategories: i,
  onToggleCategory: o,
  onActivityClick: s,
  onActivityDragStart: a,
  onActivityDragEnd: c,
  onActivityPointerDown: u
}) {
  const l = e.trim().length > 0;
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-palette-body", children: [
    /* @__PURE__ */ r.jsxs("label", { className: "wf-palette-search", children: [
      /* @__PURE__ */ r.jsx(io, { size: 14, "aria-hidden": "true" }),
      /* @__PURE__ */ r.jsx(
        "input",
        {
          type: "search",
          value: e,
          placeholder: "Search activities",
          "aria-label": "Search activity palette",
          onChange: (d) => t(d.target.value)
        }
      )
    ] }),
    /* @__PURE__ */ r.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: n.length === 0 ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted wf-palette-empty", children: "No matching activities." }) : n.map((d) => {
      const f = l || i.has(d.category);
      return /* @__PURE__ */ r.jsxs("div", { className: "wf-palette-category", children: [
        /* @__PURE__ */ r.jsxs(
          "button",
          {
            type: "button",
            className: "wf-palette-category-toggle",
            role: "treeitem",
            "aria-expanded": f,
            onClick: () => o(d.category),
            children: [
              f ? /* @__PURE__ */ r.jsx(Jc, { size: 14 }) : /* @__PURE__ */ r.jsx(mt, { size: 14 }),
              /* @__PURE__ */ r.jsx("span", { children: d.category }),
              /* @__PURE__ */ r.jsx("small", { children: d.activities.length })
            ]
          }
        ),
        f ? /* @__PURE__ */ r.jsx("div", { className: "wf-palette-activities", role: "group", children: d.activities.map((p) => {
          const h = p.description?.trim(), y = h ? `wf-palette-description-${p.activityVersionId}` : void 0, v = Ce(p), w = fn(p);
          return /* @__PURE__ */ r.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-activity",
              role: "treeitem",
              draggable: !0,
              title: h || Ce(p),
              "aria-describedby": y,
              onClick: () => s(p),
              onDragStart: (x) => a(x, p),
              onDragEnd: (x) => c(x, p),
              onPointerDown: (x) => u(x, p),
              children: [
                /* @__PURE__ */ r.jsx("span", { className: "wf-activity-icon", "data-icon": w, "aria-hidden": "true", children: co(w) }),
                /* @__PURE__ */ r.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ r.jsx("strong", { children: v }),
                  h ? /* @__PURE__ */ r.jsx("small", { id: y, children: h }) : null
                ] }),
                /* @__PURE__ */ r.jsx(Qc, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            p.activityVersionId
          );
        }) }) : null
      ] }, d.category);
    }) })
  ] });
}
const Jd = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]), lj = "Variable";
function uj({
  activity: e,
  descriptor: t,
  editors: n,
  expressionEditors: i,
  expressionDescriptors: o,
  descriptorStatus: s,
  visibleVariables: a,
  scopeStatus: c,
  onChange: u
}) {
  if (s === "loading")
    return /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Loading activity properties..." });
  if (!t)
    return /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No activity descriptor is available for this activity." });
  const l = t.inputs.filter((p) => p.isBrowsable !== !1).sort((p, h) => (p.order ?? 0) - (h.order ?? 0) || p.name.localeCompare(h.name));
  if (l.length === 0)
    return /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "This activity does not expose editable properties." });
  const d = xj(l), f = o.length > 0 ? o : Rh;
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ r.jsx("span", { className: "wf-section-label", children: "Properties" }),
    d.map((p) => /* @__PURE__ */ r.jsxs("section", { className: "wf-property-group", children: [
      d.length > 1 ? /* @__PURE__ */ r.jsx("h4", { children: p.category }) : null,
      p.inputs.map((h) => /* @__PURE__ */ r.jsx(
        dj,
        {
          activity: e,
          input: h,
          editors: n,
          expressionEditors: i,
          expressionDescriptors: f,
          visibleVariables: a,
          scopeStatus: c,
          onChange: u
        },
        h.name
      ))
    ] }, p.category))
  ] });
}
function dj({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: i,
  expressionDescriptors: o,
  visibleVariables: s,
  scopeStatus: a,
  onChange: c
}) {
  const u = t.isReadOnly === !0, l = { activity: e, expressionDescriptors: o, readOnly: u }, d = Ur(n, t, l), f = d?.component, p = t.isWrapped !== !1 ? Tl(e, t) : null, h = p?.expression.type ?? "Literal", y = Oh(e, t), v = h.toLowerCase(), x = p && (v === "literal" || v === "object") && !Fh(t) ? Wh(t.typeName) : null, b = x ? Ur(n, t, { ...l, scope: "collection" }) : void 0, g = p ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: o,
    readOnly: u,
    surface: "inline",
    syntax: h
  } : null, m = g ? ef(i, g) : null, j = m?.surfaces.inline, N = m && g ? tf(m, g, y) : [], S = x != null, k = !!(p && !S && wj(t, d?.id)), _ = !!(p && !S && vj(t, d?.id)), [M, I] = K(!1), $ = (E) => {
    const D = p ? Lh(p, E) : E;
    c(la(e, t, D));
  }, L = (E) => {
    p && c(la(e, t, Vh(p, E)));
  }, C = x ? b ? Xr(b.component, t, y, u, { ...l, scope: "collection" }, $) : /* @__PURE__ */ r.jsx(
    pj,
    {
      input: t,
      elementTypeName: x.elementTypeName,
      value: y,
      editors: n,
      context: l,
      disabled: u,
      onChange: $
    }
  ) : null, A = h === lj && p ? /* @__PURE__ */ r.jsx(
    yj,
    {
      value: y,
      visibleVariables: s,
      scopeStatus: a,
      disabled: u,
      onChange: $
    }
  ) : C ?? (j && g ? /* @__PURE__ */ r.jsx(
    j,
    {
      descriptor: t,
      syntax: h,
      value: y,
      disabled: u,
      context: g,
      onChange: $
    }
  ) : Xr(f, t, y, u, l, $));
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ r.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ r.jsx("span", { children: ls(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ r.jsx("p", { children: t.description }) : null,
    p && !k ? /* @__PURE__ */ r.jsx(
      Yr,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: h,
        descriptors: o,
        disabled: u,
        onChange: L
      }
    ) : null,
    k ? /* @__PURE__ */ r.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-expression-editor", children: [
        A,
        Zr(N)
      ] }),
      /* @__PURE__ */ r.jsx(
        Yr,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: h,
          descriptors: o,
          disabled: u,
          variant: "inline",
          onChange: L
        }
      ),
      _ ? /* @__PURE__ */ r.jsx(
        "button",
        {
          type: "button",
          className: "wf-expression-expand-button",
          "aria-label": `Open expanded ${t.displayName || t.name} editor`,
          title: "Open expanded editor",
          onClick: () => I(!0),
          children: /* @__PURE__ */ r.jsx(Tn, { size: 13 })
        }
      ) : null
    ] }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
      A,
      Zr(N)
    ] }),
    _ && !k ? /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => I(!0),
        children: [
          /* @__PURE__ */ r.jsx(Tn, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    M ? /* @__PURE__ */ r.jsx(
      hj,
      {
        input: t,
        value: y,
        syntax: h,
        descriptors: o,
        activity: e,
        expressionEditors: i,
        disabled: u,
        onChange: $,
        onSyntaxChange: L,
        onClose: () => I(!1)
      }
    ) : null
  ] });
}
function fj(e, t, n) {
  return [
    "wf-collection-item",
    t === e ? "dragging" : "",
    t !== null && t !== e && n === e ? "drop-target" : ""
  ].filter(Boolean).join(" ");
}
function pj({
  input: e,
  elementTypeName: t,
  value: n,
  editors: i,
  context: o,
  disabled: s,
  onChange: a
}) {
  const c = Kh(n), u = Yh(e, t), l = { ...o, scope: "element" }, d = Ur(i, u, l)?.component, f = e.displayName || e.name, p = (j, N) => a(c.map((S, k) => k === j ? N : S)), [h, y] = K(null), [v, w] = K(null), x = () => {
    y(null), w(null);
  }, b = (j) => (N) => {
    y(j), N.dataTransfer.effectAllowed = "move", N.dataTransfer.setData("text/plain", String(j));
  }, g = (j) => (N) => {
    h !== null && (N.preventDefault(), N.dataTransfer.dropEffect = "move", v !== j && w(j));
  }, m = (j) => (N) => {
    N.preventDefault(), h !== null && h !== j && a(lr(c, h, j)), x();
  };
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-collection-editor", children: [
    c.length === 0 ? /* @__PURE__ */ r.jsx("p", { className: "wf-collection-empty", children: "No items yet." }) : /* @__PURE__ */ r.jsx("ul", { className: "wf-collection-items", children: c.map((j, N) => /* @__PURE__ */ r.jsxs(
      "li",
      {
        className: fj(N, h, v),
        onDragOver: g(N),
        onDrop: m(N),
        children: [
          /* @__PURE__ */ r.jsx(
            "span",
            {
              className: "wf-collection-item-handle",
              draggable: !s,
              "aria-label": `Drag ${f} item ${N + 1} to reorder`,
              title: "Drag to reorder",
              onDragStart: b(N),
              onDragEnd: x,
              children: /* @__PURE__ */ r.jsx(Qc, { size: 13, "aria-hidden": "true" })
            }
          ),
          /* @__PURE__ */ r.jsx("div", { className: "wf-collection-item-editor", children: Xr(d, u, j, s, l, (S) => p(N, S)) }),
          /* @__PURE__ */ r.jsxs("div", { className: "wf-collection-item-actions", children: [
            /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${N + 1} up`,
                disabled: s || N === 0,
                onClick: () => a(lr(c, N, N - 1)),
                children: /* @__PURE__ */ r.jsx(vf, { size: 13 })
              }
            ),
            /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${N + 1} down`,
                disabled: s || N === c.length - 1,
                onClick: () => a(lr(c, N, N + 1)),
                children: /* @__PURE__ */ r.jsx(Jc, { size: 13 })
              }
            ),
            /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button danger",
                "aria-label": `Remove ${f} item ${N + 1}`,
                disabled: s,
                onClick: () => a(c.filter((S, k) => k !== N)),
                children: /* @__PURE__ */ r.jsx(Dn, { size: 13 })
              }
            )
          ] })
        ]
      },
      N
    )) }),
    /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-collection-add",
        disabled: s,
        onClick: () => a([...c, Xh(t)]),
        children: [
          /* @__PURE__ */ r.jsx(Qt, { size: 13 }),
          " Add item"
        ]
      }
    )
  ] });
}
function hj({
  input: e,
  value: t,
  syntax: n,
  descriptors: i,
  activity: o,
  expressionEditors: s,
  disabled: a,
  onChange: c,
  onSyntaxChange: u,
  onClose: l
}) {
  const d = qc(), f = e.displayName || e.name, p = {
    activity: o,
    descriptor: e,
    expressionDescriptors: i,
    readOnly: a,
    surface: "expanded",
    syntax: n
  }, h = ef(s, p), y = h?.surfaces.expanded, v = h ? tf(h, p, t) : [], w = y ? null : mj(s, p);
  return Q(() => {
    const x = (b) => {
      b.key === "Escape" && l();
    };
    return window.addEventListener("keydown", x), () => window.removeEventListener("keydown", x);
  }, [l]), /* @__PURE__ */ r.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ r.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": d, children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ r.jsx("h3", { id: d, children: f })
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": `Close ${f} editor`, onClick: l, children: /* @__PURE__ */ r.jsx(es, { size: 16 }) })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ r.jsx(
          Yr,
          {
            label: `${f} expression syntax`,
            value: n,
            descriptors: i,
            disabled: a,
            onChange: u
          }
        ),
        /* @__PURE__ */ r.jsx("span", { children: ls(e.typeName) })
      ] }),
      e.description ? /* @__PURE__ */ r.jsx("p", { children: e.description }) : null,
      y ? /* @__PURE__ */ r.jsx(
        y,
        {
          descriptor: e,
          syntax: n,
          value: t,
          disabled: a,
          context: p,
          onChange: c
        }
      ) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
        w ? /* @__PURE__ */ r.jsx("p", { className: "wf-expression-editor-hint", children: w }) : null,
        /* @__PURE__ */ r.jsx(
          "textarea",
          {
            "aria-label": `${f} expanded value`,
            value: t == null ? "" : String(t),
            disabled: a,
            spellCheck: !1,
            onChange: (x) => c(x.target.value)
          }
        )
      ] }),
      Zr(v)
    ] }),
    /* @__PURE__ */ r.jsxs("footer", { children: [
      /* @__PURE__ */ r.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: l, children: "Close" })
    ] })
  ] }) });
}
function Xr(e, t, n, i, o, s) {
  return e ? /* @__PURE__ */ r.jsx(
    e,
    {
      descriptor: t,
      value: n,
      disabled: i,
      context: o,
      onChange: s
    }
  ) : /* @__PURE__ */ r.jsx("input", { type: "text", value: n == null ? "" : String(n), disabled: i, onChange: (a) => s(a.target.value) });
}
function Yr({
  label: e,
  value: t,
  descriptors: n,
  disabled: i,
  variant: o = "block",
  onChange: s
}) {
  const [a, c] = K(!1), u = qc(), l = n.find((f) => f.type === t), d = [
    "wf-syntax-picker-trigger",
    o === "inline" ? "inline" : "",
    a ? "open" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ r.jsxs("div", { className: o === "inline" ? "wf-syntax-picker inline" : "wf-syntax-picker", onBlur: (f) => {
    f.currentTarget.contains(f.relatedTarget) || c(!1);
  }, children: [
    /* @__PURE__ */ r.jsx(
      "button",
      {
        type: "button",
        className: d,
        "aria-label": e,
        "aria-haspopup": "listbox",
        "aria-expanded": a,
        "aria-controls": u,
        disabled: i,
        onClick: () => c((f) => !f),
        children: /* @__PURE__ */ r.jsx("span", { children: l?.displayName || l?.type || t })
      }
    ),
    a ? /* @__PURE__ */ r.jsx("div", { id: u, role: "listbox", className: "wf-syntax-picker-menu", "aria-label": e, children: n.map((f) => {
      const p = f.displayName || f.type, h = f.type === t;
      return /* @__PURE__ */ r.jsx(
        "button",
        {
          type: "button",
          role: "option",
          "aria-selected": h,
          className: h ? "selected" : "",
          onClick: () => {
            s(f.type), c(!1);
          },
          children: p
        },
        f.type
      );
    }) }) : null
  ] });
}
const qr = "::";
function Qd(e) {
  return !e || e === Ki ? Ki : e;
}
function Bc(e, t) {
  return `${Qd(t)}${qr}${e}`;
}
function gj(e) {
  const t = e.indexOf(qr);
  if (t < 0) return null;
  const n = e.slice(t + qr.length);
  return n ? { scopeId: e.slice(0, t), referenceKey: n } : null;
}
function yj({ value: e, visibleVariables: t, scopeStatus: n, disabled: i, onChange: o }) {
  const s = bl(e), a = !!e && typeof e == "object" || typeof e == "string" && e.trim().startsWith("{"), c = s && (a || t.some((d) => d.referenceKey === s.referenceKey)) ? s : null, u = c ? Bc(c.referenceKey, c.declaringScopeId) : "", l = !!c && t.some(
    (d) => d.referenceKey === c.referenceKey && d.scopeId === Qd(c.declaringScopeId)
  );
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-variable-picker", children: [
    /* @__PURE__ */ r.jsxs(
      "select",
      {
        "aria-label": "Variable reference",
        value: u,
        disabled: i,
        onChange: (d) => {
          const f = gj(d.target.value);
          f && o(Lp(f.referenceKey, f.scopeId));
        },
        children: [
          /* @__PURE__ */ r.jsx("option", { value: "", children: "Select a variable…" }),
          c && !l ? /* @__PURE__ */ r.jsxs("option", { value: u, children: [
            c.referenceKey,
            " (not visible from this scope)"
          ] }) : null,
          t.map((d) => {
            const f = Bc(d.referenceKey, d.scopeId);
            return /* @__PURE__ */ r.jsxs("option", { value: f, children: [
              d.name,
              d.isWorkflowScope ? " · workflow" : " · container"
            ] }, f);
          })
        ]
      }
    ),
    n === "unavailable" ? /* @__PURE__ */ r.jsx("p", { className: "wf-variable-picker-note", children: "Variable scope information is unavailable (pending backend support). Existing references are preserved." }) : n === "ready" && t.length === 0 ? /* @__PURE__ */ r.jsx("p", { className: "wf-variable-picker-note", children: "No variables are visible here. Declare one on the workflow or a container scope." }) : null
  ] });
}
function Ur(e, t, n) {
  return [...e].sort((i, o) => (i.order ?? 500) - (o.order ?? 500)).find((i) => i.supports(t, n));
}
function ef(e, t) {
  return [...e].sort((n, i) => (n.order ?? 500) - (i.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function tf(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function mj(e, t) {
  if (t.syntax.toLowerCase() === "literal") return null;
  const n = [...e].sort((a, c) => (a.order ?? 500) - (c.order ?? 500)).find((a) => a.supports(t) && a.metadata)?.metadata;
  if (!n) return `No enhanced editor is registered for ${t.syntax}. Using the generic text editor.`;
  const i = n.displayName?.trim() || "enhanced editor", o = n.installHint?.trim(), s = `No ${i} is registered for ${t.syntax}. Using the generic text editor.`;
  return o ? `${s} ${o}` : s;
}
function Zr(e) {
  return e.length === 0 ? null : /* @__PURE__ */ r.jsx("div", { className: "wf-expression-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const i = t.severity ?? "info";
    return /* @__PURE__ */ r.jsxs("p", { className: `wf-expression-editor-diagnostic ${i}`, children: [
      t.code ? /* @__PURE__ */ r.jsx("span", { children: t.code }) : null,
      t.message
    ] }, `${t.code ?? "diagnostic"}-${n}`);
  }) });
}
function xj(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const i = n.category?.trim() || "General";
    t.set(i, [...t.get(i) ?? [], n]);
  }
  return [...t.entries()].map(([n, i]) => ({ category: n, inputs: i }));
}
function wj(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !Jd.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function vj(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !Jd.has(t) && n !== "multiline") return !1;
  const i = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(i) || n === "singleline" || n === "multiline";
}
function bj({
  context: e,
  selectedNode: t,
  selectedNodeLabel: n,
  selectedActivityType: i,
  selectedDescriptor: o,
  selectedNodeAvailability: s,
  selectedSlots: a,
  catalog: c,
  catalogByVersion: u,
  selectedSupportsScopedVariables: l,
  propertyEditors: d,
  expressionEditors: f,
  expressionDescriptors: p,
  descriptorStatus: h,
  scopedVariableAnalysis: y,
  onSelectedActivityChange: v,
  onEnterSlot: w,
  onReplaceSlotActivity: x
}) {
  const [b, g] = K(null);
  return b && b.nodeId !== t?.nodeId && g(null), t ? /* @__PURE__ */ r.jsxs("div", { className: "wf-inspector-content", children: [
    /* @__PURE__ */ r.jsx("h3", { children: n }),
    /* @__PURE__ */ r.jsxs("dl", { children: [
      /* @__PURE__ */ r.jsx("dt", { children: "Node ID" }),
      /* @__PURE__ */ r.jsx("dd", { children: t.nodeId }),
      /* @__PURE__ */ r.jsx("dt", { children: "Activity type" }),
      /* @__PURE__ */ r.jsx("dd", { children: i }),
      /* @__PURE__ */ r.jsx("dt", { children: "Activity version" }),
      /* @__PURE__ */ r.jsx("dd", { children: t.activityVersionId })
    ] }),
    s ? /* @__PURE__ */ r.jsxs("div", { className: "wf-availability-notice", children: [
      /* @__PURE__ */ r.jsx(Hi, { size: 14 }),
      /* @__PURE__ */ r.jsxs("span", { children: [
        "No longer available for new use · ",
        Rn(s.state)
      ] })
    ] }) : null,
    /* @__PURE__ */ r.jsx(
      uj,
      {
        activity: t,
        descriptor: o,
        editors: d,
        expressionEditors: f,
        expressionDescriptors: p,
        descriptorStatus: h,
        visibleVariables: y.visibleVariables,
        scopeStatus: y.status,
        onChange: v
      }
    ),
    l ? /* @__PURE__ */ r.jsx("div", { className: "wf-container-variables", children: /* @__PURE__ */ r.jsx(
      Ab,
      {
        context: e,
        variables: vl(t),
        title: "Container variables",
        addLabel: "Add container variable",
        emptyLabel: "No container variables declared on this activity.",
        warnings: Op(y.shadowingWarnings, t.nodeId),
        onChange: (m) => v(zp(t, m))
      }
    ) }) : null,
    a.length > 0 ? /* @__PURE__ */ r.jsxs("div", { className: "wf-slot-list", children: [
      /* @__PURE__ */ r.jsx("span", { children: "Embedded slots" }),
      a.map((m) => {
        const j = `${n} / ${m.label}`;
        return /* @__PURE__ */ r.jsxs("div", { className: "wf-slot-row", children: [
          /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => w(t.nodeId, m, j), children: [
            m.label,
            /* @__PURE__ */ r.jsx("small", { children: aN(m, u) })
          ] }),
          m.cardinality === "single" ? /* @__PURE__ */ r.jsx(
            "button",
            {
              type: "button",
              className: "wf-slot-change",
              "aria-label": `${m.activities.length > 0 ? "Change" : "Choose"} ${m.label} activity`,
              title: m.activities.length > 0 ? "Change activity" : "Choose activity",
              onClick: (N) => g({ nodeId: t.nodeId, slotId: m.id, clientX: N.clientX, clientY: N.clientY }),
              children: /* @__PURE__ */ r.jsx(bf, { size: 14 })
            }
          ) : null
        ] }, m.id);
      })
    ] }) : /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." }),
    b ? /* @__PURE__ */ r.jsx(
      Xd,
      {
        clientX: b.clientX,
        clientY: b.clientY,
        activities: c,
        onPick: (m) => {
          g(null);
          const j = a.find((N) => N.id === b.slotId);
          j && x(t.nodeId, j, `${n} / ${j.label}`, m);
        },
        onClose: () => g(null)
      }
    ) : null
  ] }) : /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
}
const nf = [
  { value: "flowchart", label: "Flowchart", hint: "Free-form graph of connected activities." },
  { value: "sequence", label: "Sequence", hint: "Ordered list of activities that run top to bottom." }
];
function of({ label: e, hint: t }) {
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsx("span", { className: "wf-root-card-title", children: e }),
    /* @__PURE__ */ r.jsx("span", { className: "wf-root-card-hint", children: t })
  ] });
}
function Nj({ value: e, onChange: t }) {
  return /* @__PURE__ */ r.jsx("div", { className: "wf-root-cards", role: "radiogroup", "aria-label": "Root activity", children: nf.map((n) => {
    const i = e === n.value;
    return /* @__PURE__ */ r.jsxs("label", { className: "wf-root-card", "data-checked": i || void 0, children: [
      /* @__PURE__ */ r.jsx(
        "input",
        {
          type: "radio",
          name: "wf-root-kind",
          "aria-label": n.label,
          value: n.value,
          checked: i,
          onChange: () => t(n.value)
        }
      ),
      /* @__PURE__ */ r.jsx(of, { label: n.label, hint: n.hint })
    ] }, n.value);
  }) });
}
function jj({ onPick: e }) {
  return /* @__PURE__ */ r.jsx("div", { className: "wf-root-cards", role: "group", "aria-label": "Fill this slot", children: nf.map((t) => /* @__PURE__ */ r.jsx(
    "button",
    {
      type: "button",
      className: "wf-root-card",
      "aria-label": t.label,
      onClick: () => e(t.value),
      children: /* @__PURE__ */ r.jsx(of, { label: t.label, hint: t.hint })
    },
    t.value
  )) });
}
function Sj({ slotLabel: e, catalog: t, onPickActivity: n, onBrowseAll: i }) {
  const o = (s) => {
    const a = Md(t, s);
    a && n(a);
  };
  return /* @__PURE__ */ r.jsx("div", { className: "wf-slot-empty", role: "group", "aria-label": `Fill ${e}`, children: /* @__PURE__ */ r.jsxs("div", { className: "wf-slot-empty-card", children: [
    /* @__PURE__ */ r.jsx("p", { className: "wf-slot-empty-title", children: "This slot is empty" }),
    /* @__PURE__ */ r.jsxs("p", { className: "wf-slot-empty-hint", children: [
      "Choose a container for ",
      /* @__PURE__ */ r.jsx("strong", { children: e }),
      ", or pick any activity."
    ] }),
    /* @__PURE__ */ r.jsx(jj, { onPick: o }),
    /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-slot-empty-browse",
        onClick: (s) => i({ clientX: s.clientX, clientY: s.clientY }),
        children: [
          /* @__PURE__ */ r.jsx(qn, { size: 15 }),
          " Browse all activities…"
        ]
      }
    ),
    /* @__PURE__ */ r.jsx("p", { className: "wf-slot-empty-drag-hint", children: "Tip: you can also drag activities from the palette onto the canvas." })
  ] }) });
}
function Cj({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: i,
  expressionEditors: o,
  workflowDesignerPanels: s,
  autosaveEnabledByDefault: a,
  onBack: c
}) {
  const u = PN(), { draft: l, frames: d, selectedNodeId: f, testRun: p, publishedArtifactId: h } = u.state, {
    loadDraft: y,
    replaceDraftByBatch: v,
    editDraft: w,
    editDraftAndSelect: x,
    select: b,
    navigateToScope: g,
    resetToRoot: m,
    enterSlot: j,
    startTestRun: N,
    clearTestRun: S,
    setPublishedArtifact: k
  } = u, [_, M] = K(""), [I, $] = K(""), [L, C] = K("idle"), [A, E] = K(() => /* @__PURE__ */ new Set()), [D, P] = K(""), [T, B] = K("activities"), [W, H] = K("inspector"), [q, V] = K("designer"), {
    paletteWidth: Y,
    inspectorWidth: J,
    paletteCollapsed: Z,
    inspectorCollapsed: R,
    maximizedSidePanel: X,
    setInspectorCollapsed: le,
    paletteExpanded: ce,
    inspectorExpanded: ee,
    editorBodyClassName: ne,
    editorBodyStyle: fe,
    toggleSidePanelCollapsed: O,
    toggleSidePanelMaximized: te,
    startSidePanelResize: ge,
    handleSidePanelResizeKeyDown: ye
  } = qd(), { resetHistory: _e, undo: De, redo: $e, canUndoNow: st, canRedoNow: qe } = DN({ draft: l, restoreDraft: y }), { saveDraft: Ue, autosaveEnabled: Pe, setAutosaveEnabled: He, markSaved: We } = nj({ context: e, draft: l, autosaveEnabledByDefault: a, editDraft: w, setStatus: $, setError: M }), {
    details: Ne,
    setDetails: zt,
    catalog: Ie,
    activityDescriptors: gn,
    availabilityDiagnostics: z,
    expressionDescriptors: F,
    descriptorStatus: U,
    reload: ie
  } = ij({ context: e, definitionId: t, resetHistory: _e, loadDraft: y, markSaved: We, setError: M }), { updateDefinitionMeta: ue } = oj({ context: e, details: Ne, setDetails: zt, setStatus: $ }), {
    catalogByVersion: pe,
    availabilityLookup: xe,
    scopeOwner: Ae,
    isUnsupportedDesigner: je,
    scope: Le,
    selectedNode: we,
    selectedDescriptor: Lt,
    selectedNodeAvailability: jo,
    selectedSlots: oi,
    selectedSupportsScopedVariables: So,
    scopedVariableAnalysis: Co,
    isFlowchartDesigner: pt,
    canAddActivitiesToCanvas: Eo
  } = sj({ context: e, draft: l, frames: d, selectedNodeId: f, catalog: Ie, activityDescriptors: gn, availabilityDiagnostics: z }), Vt = de(() => Br(Ie), [Ie]), ko = de(() => {
    const G = D.trim().toLowerCase();
    if (!G) return Vt;
    const ae = Ie.filter((me) => Ce(me).toLowerCase().includes(G) || me.activityTypeKey.toLowerCase().includes(G) || (me.category ?? "").toLowerCase().includes(G) || (me.description ?? "").toLowerCase().includes(G));
    return Br(ae);
  }, [Ie, D, Vt]), Ot = L !== "idle", Io = !!l?.state.rootActivity && !Ot, ri = Pt(n, "weaver.workflows.find-draft-risks"), si = Pt(n, "weaver.workflows.propose-update"), Ao = FN({
    draft: l,
    scope: Le,
    scopeOwner: Ae,
    frames: d,
    catalog: Ie,
    catalogByVersion: pe,
    isUnsupportedDesigner: je,
    isFlowchartDesigner: pt,
    canAddActivitiesToCanvas: Eo,
    selectedNodeId: f,
    editDraft: w,
    editDraftAndSelect: x,
    select: b,
    resetToRoot: m,
    setStatus: $,
    setError: M
  }), {
    nodes: Ht,
    edges: _o,
    canvasRef: Do,
    setReactFlowInstance: To,
    connectMenu: Wt,
    setConnectMenu: ai,
    edgeActions: ci,
    onNodesChange: $o,
    onEdgesChange: Po,
    onNodesDelete: Mo,
    onEdgesDelete: Ro,
    isValidConnection: zo,
    onConnect: Lo,
    onConnectStart: li,
    onConnectEnd: Vo,
    onReconnect: Oo,
    commitLayout: Ho,
    canAutoLayout: Wo,
    autoLayout: Bo,
    onCanvasDragOver: ui,
    onCanvasDragLeave: di,
    onCanvasDrop: fi,
    openEmptyConnectMenu: yn,
    onConnectMenuPick: Fo,
    addActivity: Ko,
    onPaletteClick: Xo,
    onPaletteDragStart: Yo,
    onPaletteDragEnd: qo,
    onPalettePointerDown: Uo
  } = Ao, Zo = !je && d.length > 0 && !!Le && Le.slot.activities.length === 0 && Ht.length === 0;
  tj({ draft: l, details: Ne, catalog: Ie, replaceDraftByBatch: v, setStatus: $, setError: M }), Q(() => {
    !l?.state.rootActivity || Ie.length === 0 || w(({ draft: G }) => {
      if (!G?.state.rootActivity) return null;
      const ae = hl(G.state.rootActivity, pe);
      return !ae || ae === G.state.rootActivity ? null : {
        ...G,
        state: {
          ...G.state,
          rootActivity: ae
        }
      };
    });
  }, [Ie.length, pe, l?.state.rootActivity, w]), aj({ details: Ne, draft: l, selectedNode: we, selectedNodeId: f, selectedDescriptor: Lt, catalogByVersion: pe }), Q(() => {
    E((G) => {
      let ae = !1;
      const me = new Set(G);
      for (const Me of Vt)
        me.has(Me.category) || (me.add(Me.category), ae = !0);
      return ae ? me : G;
    });
  }, [Vt]);
  const { exportJson: Go, save: pi, promoteAndPublish: Jo, run: hi } = rj({
    context: e,
    draft: l,
    details: Ne,
    busy: Ot,
    saveDraft: Ue,
    reload: ie,
    startTestRun: N,
    clearTestRun: S,
    setPublishedArtifact: k,
    setOperation: C,
    setStatus: $,
    setError: M,
    setActiveRightPanelId: H,
    setInspectorCollapsed: le
  }), Qo = se((G) => {
    w(({ draft: ae }) => ae ? { ...ae, state: G(ae.state) } : null);
  }, [w]), er = se((G) => {
    if (!l) return "No draft is loaded.";
    const ae = ub(G, l);
    return ae.ok ? (y(ae.draft), $("Applied workflow JSON."), null) : ae.error;
  }, [l, y]);
  Q(() => {
    const G = (ae) => {
      if (q !== "designer" || !(ae.metaKey || ae.ctrlKey)) return;
      const me = ae.target;
      if (me && (me.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(me.tagName))) return;
      const Me = ae.key.toLowerCase();
      Me === "z" && !ae.shiftKey ? (ae.preventDefault(), De()) : (Me === "z" && ae.shiftKey || Me === "y") && (ae.preventDefault(), $e());
    };
    return window.addEventListener("keydown", G), () => window.removeEventListener("keydown", G);
  }, [q, De, $e]);
  const bt = se((G, ae, me) => {
    const Me = ae.cardinality === "single" ? ae.activities[0]?.nodeId ?? null : null;
    j(G, ae.id, me, Me);
  }, [j]), tr = se((G, ae, me, Me) => {
    const mi = ae.activities.length > 0, Hs = Wi(Me, Fr(Me));
    w(({ draft: xi }) => {
      const Ws = xi?.state.rootActivity;
      return !xi || !Ws ? null : {
        ...xi,
        state: {
          ...xi.state,
          rootActivity: Ir(Ws, G, (uf) => dn(uf, ae, [Hs]), pe)
        }
      };
    }), j(G, ae.id, me, Hs.nodeId), M(""), $(mi ? `Replaced ${ae.label} content` : `Assigned ${Ce(Me)} to ${ae.label}`);
  }, [pe, w, j]), nr = de(() => je ? null : (G, ae, me) => bt(G, me, `${ae} / ${me.label}`), [bt, je]), gi = se((G) => {
    w(({ draft: ae }) => {
      const me = ae?.state.rootActivity;
      return !ae || !me ? null : {
        ...ae,
        state: {
          ...ae.state,
          rootActivity: Ir(me, G.nodeId, () => G, pe)
        }
      };
    });
  }, [pe, w]), rf = se((G) => {
    if (!G) return;
    const ae = l?.state.rootActivity;
    if (!ae) return;
    const me = sp(ae, G, (Me) => {
      const mi = pe.get(Me.activityVersionId);
      return mi ? Ce(mi) : Me.nodeId;
    }, pe);
    me && (V("designer"), g(me, G), le(!1));
  }, [l?.state.rootActivity, pe, le, g]), sf = (G) => {
    E((ae) => {
      const me = new Set(ae);
      return me.has(G) ? me.delete(G) : me.add(G), me;
    });
  };
  if (!Ne || !l)
    return /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: _ || "Loading workflow editor..." });
  const yi = p?.draftSignature === Fe(l) ? p.view : null, zs = yi && I.startsWith("Test run") ? "" : I, af = (G) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(G)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, cf = {
    definition: Ne.definition,
    draft: l,
    selectedActivity: we,
    selectedActivityDescriptor: Lt,
    selectedActivitySlots: oi,
    catalog: Ie,
    currentScopeOwner: Ae,
    frames: d
  }, Ls = s.map((G) => {
    const ae = G.component;
    return {
      id: G.id,
      title: G.title,
      side: G.side,
      order: G.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ r.jsx(ae, { context: cf })
    };
  }), ir = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ r.jsx(qn, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(
        cj,
        {
          paletteSearch: D,
          onSearchChange: P,
          groups: ko,
          expandedCategories: A,
          onToggleCategory: sf,
          onActivityClick: Xo,
          onActivityDragStart: Yo,
          onActivityDragEnd: qo,
          onActivityPointerDown: Uo
        }
      )
    },
    ...Ls.filter((G) => G.side === "left")
  ].sort(Rc), or = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ r.jsx(Jr, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(
        bj,
        {
          context: e,
          selectedNode: we,
          selectedNodeLabel: we ? Ht.find((G) => G.id === we.nodeId)?.data.label ?? we.nodeId : "",
          selectedActivityType: we ? Lt?.typeName ?? pe.get(we.activityVersionId)?.activityTypeKey ?? "Unknown" : "",
          selectedDescriptor: Lt,
          selectedNodeAvailability: jo,
          selectedSlots: oi,
          catalog: Ie,
          catalogByVersion: pe,
          selectedSupportsScopedVariables: So,
          propertyEditors: i,
          expressionEditors: o,
          expressionDescriptors: F,
          descriptorStatus: U,
          scopedVariableAnalysis: Co,
          onSelectedActivityChange: gi,
          onEnterSlot: bt,
          onReplaceSlotActivity: tr
        }
      )
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: /* @__PURE__ */ r.jsx(Jt, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(NN, { testRun: yi, onOpenRun: af })
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ r.jsx(el, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(
        CN,
        {
          context: e,
          ai: n,
          definitionId: Ne.definition.id,
          publishedArtifactId: h
        }
      )
    },
    ...Ls.filter((G) => G.side === "right")
  ].sort(Rc), Vs = ir.find((G) => G.id === T) ?? ir[0], Os = or.find((G) => G.id === W) ?? or[0], lf = [
    { id: "designer", title: "Designer", order: 0, icon: /* @__PURE__ */ r.jsx(tl, { size: 14 }), render: () => null },
    { id: "code", title: "Code", order: 1, icon: /* @__PURE__ */ r.jsx(Ef, { size: 14 }), render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: /* @__PURE__ */ r.jsx(Qr, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-link-button", onClick: c, children: "Definitions" }),
      /* @__PURE__ */ r.jsx(mt, { size: 14 }),
      /* @__PURE__ */ r.jsx("strong", { children: Ne.definition.name }),
      /* @__PURE__ */ r.jsx("span", { className: "wf-chip", children: "Draft" }),
      zs ? /* @__PURE__ */ r.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ r.jsx(ln, { size: 13 }),
        " ",
        zs
      ] }) : null,
      /* @__PURE__ */ r.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-canvas-tools", role: "group", "aria-label": "Canvas tools", children: [
          /* @__PURE__ */ r.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Undo",
              title: "Undo (Ctrl+Z)",
              disabled: !st,
              onClick: De,
              children: /* @__PURE__ */ r.jsx(Nf, { size: 16 })
            }
          ),
          /* @__PURE__ */ r.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Redo",
              title: "Redo (Ctrl+Shift+Z)",
              disabled: !qe,
              onClick: $e,
              children: /* @__PURE__ */ r.jsx(jf, { size: 16 })
            }
          ),
          /* @__PURE__ */ r.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Auto-layout",
              title: "Auto-layout the canvas",
              disabled: !Wo,
              onClick: Bo,
              children: /* @__PURE__ */ r.jsx(Sf, { size: 16 })
            }
          )
        ] }),
        /* @__PURE__ */ r.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ r.jsx("input", { className: "wf-switch-input", type: "checkbox", checked: Pe, onChange: (G) => He(G.target.checked) }),
          /* @__PURE__ */ r.jsx("span", { children: "Autosave" })
        ] }),
        ri ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => Mt(n, ri, { definition: Ne.definition, draft: l }), children: [
          /* @__PURE__ */ r.jsx(ut, { size: 15 }),
          " Risks"
        ] }) : null,
        si ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => Mt(n, si, { definition: Ne.definition, draft: l }), children: [
          /* @__PURE__ */ r.jsx(ut, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ r.jsxs("button", { type: "button", title: "Export workflow as JSON", onClick: Go, children: [
          /* @__PURE__ */ r.jsx(Cf, { size: 15 }),
          " Export"
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: Ot, onClick: () => {
          pi();
        }, children: [
          /* @__PURE__ */ r.jsx(Zc, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: Ot, onClick: () => {
          Jo();
        }, children: [
          /* @__PURE__ */ r.jsx(Uc, { size: 15 }),
          " Promote"
        ] }),
        yi ? /* @__PURE__ */ r.jsx(
          bN,
          {
            testRun: yi,
            onOpenDetails: () => {
              H("runtime"), le(!1);
            }
          }
        ) : null,
        /* @__PURE__ */ r.jsxs(
          "button",
          {
            type: "button",
            disabled: !Io,
            title: l.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              hi();
            },
            children: [
              /* @__PURE__ */ r.jsx(Jt, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    _ ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ r.jsx(vt, { size: 16 }),
      " ",
      _
    ] }) : null,
    /* @__PURE__ */ r.jsxs("div", { className: ne, style: fe, children: [
      /* @__PURE__ */ r.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ r.jsx(
            Oi,
            {
              label: "Activities panel tabs",
              tabs: ir,
              activeTabId: Vs.id,
              onSelect: B
            }
          ),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": Z ? "Expand activities panel" : "Collapse activities panel",
                title: Z ? "Expand" : "Collapse",
                onClick: () => O("palette"),
                children: Z ? /* @__PURE__ */ r.jsx(mt, { size: 14 }) : /* @__PURE__ */ r.jsx($n, { size: 14 })
              }
            ),
            Z ? null : /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": X === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: X === "palette" ? "Restore" : "Maximize",
                onClick: () => te("palette"),
                children: X === "palette" ? /* @__PURE__ */ r.jsx(Cr, { size: 14 }) : /* @__PURE__ */ r.jsx(Tn, { size: 14 })
              }
            )
          ] })
        ] }),
        ce ? Vs.render() : null
      ] }),
      ce && !X ? /* @__PURE__ */ r.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Cn,
          "aria-valuemax": En,
          "aria-valuenow": Y,
          tabIndex: 0,
          onPointerDown: (G) => ge("palette", G),
          onKeyDown: (G) => ye("palette", G)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ r.jsx("div", { className: "wf-canvas-tabs", children: /* @__PURE__ */ r.jsx(
          Oi,
          {
            label: "Editor view tabs",
            tabs: lf,
            activeTabId: q,
            onSelect: (G) => V(G)
          }
        ) }),
        q === "code" ? /* @__PURE__ */ r.jsx(yb, { draft: l, onApply: er }) : q === "properties" ? /* @__PURE__ */ r.jsx(Db, { details: Ne, draft: l, context: e, onStateChange: Qo, onDefinitionMetaChange: ue }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
          /* @__PURE__ */ r.jsxs("div", { className: "wf-breadcrumb", children: [
            /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => m(), children: "Root" }),
            d.map((G, ae) => /* @__PURE__ */ r.jsxs(Ke.Fragment, { children: [
              /* @__PURE__ */ r.jsx(mt, { size: 13 }),
              /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => g(d.slice(0, ae + 1), null), children: G.label })
            ] }, `${G.ownerNodeId}-${G.slotId}-${ae}`))
          ] }),
          /* @__PURE__ */ r.jsxs("div", { className: "wf-canvas", ref: Do, onDragOver: ui, onDragLeave: di, onDrop: fi, children: [
            /* @__PURE__ */ r.jsx(Dd.Provider, { value: ci, children: /* @__PURE__ */ r.jsx(Td.Provider, { value: xe, children: /* @__PURE__ */ r.jsx($d.Provider, { value: nr, children: /* @__PURE__ */ r.jsxs(
              bd,
              {
                nodes: Ht,
                edges: _o,
                nodeTypes: Fd,
                edgeTypes: Kd,
                onInit: To,
                onNodesChange: $o,
                onEdgesChange: Po,
                onNodesDelete: Mo,
                onEdgesDelete: Ro,
                onConnect: Lo,
                onConnectStart: pt ? li : void 0,
                onConnectEnd: pt ? Vo : void 0,
                onReconnect: pt ? Oo : void 0,
                isValidConnection: zo,
                onDragOver: ui,
                onDragLeave: di,
                onDrop: fi,
                onPaneClick: () => b(null),
                onNodeClick: (G, ae) => b(ae.id),
                onNodeDragStop: je ? void 0 : Ho,
                minZoom: 0.2,
                maxZoom: 1.8,
                nodesConnectable: pt,
                nodesDraggable: !je,
                selectionOnDrag: !0,
                multiSelectionKeyCode: ["Shift", "Meta", "Control"],
                deleteKeyCode: je ? null : ["Backspace", "Delete"],
                panActivationKeyCode: null,
                defaultEdgeOptions: { type: "workflow" },
                children: [
                  /* @__PURE__ */ r.jsx(jd, { gap: 18, size: 1 }),
                  /* @__PURE__ */ r.jsx(Cd, {}),
                  /* @__PURE__ */ r.jsx(kd, { pannable: !0, zoomable: !0 })
                ]
              }
            ) }) }) }),
            Zo ? /* @__PURE__ */ r.jsx(
              Sj,
              {
                slotLabel: Le?.slot.label ?? "this slot",
                catalog: Ie,
                onPickActivity: Ko,
                onBrowseAll: yn
              }
            ) : pt && Ht.length === 0 ? /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => yn(), children: [
              /* @__PURE__ */ r.jsx(Qt, { size: 15 }),
              " Add activity"
            ] }) : null,
            Wt ? /* @__PURE__ */ r.jsx(
              Xd,
              {
                clientX: Wt.clientX,
                clientY: Wt.clientY,
                activities: Ie,
                onPick: Fo,
                onClose: () => ai(null)
              }
            ) : null
          ] }),
          /* @__PURE__ */ r.jsx(vN, { draft: l, onRepair: rf })
        ] })
      ] }),
      ee && !X ? /* @__PURE__ */ r.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Yt,
          "aria-valuemax": qt,
          "aria-valuenow": J,
          tabIndex: 0,
          onPointerDown: (G) => ge("inspector", G),
          onKeyDown: (G) => ye("inspector", G)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ r.jsx(
            Oi,
            {
              label: "Inspector panel tabs",
              tabs: or,
              activeTabId: Os.id,
              onSelect: H
            }
          ),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": R ? "Expand inspector panel" : "Collapse inspector panel",
                title: R ? "Expand" : "Collapse",
                onClick: () => O("inspector"),
                children: R ? /* @__PURE__ */ r.jsx($n, { size: 14 }) : /* @__PURE__ */ r.jsx(mt, { size: 14 })
              }
            ),
            R ? null : /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": X === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: X === "inspector" ? "Restore" : "Maximize",
                onClick: () => te("inspector"),
                children: X === "inspector" ? /* @__PURE__ */ r.jsx(Cr, { size: 14 }) : /* @__PURE__ */ r.jsx(Tn, { size: 14 })
              }
            )
          ] })
        ] }),
        ee ? Os.render() : null
      ] })
    ] })
  ] });
}
function Ej({ page: e, pageSize: t, totalCount: n, onPageChange: i, onPageSizeChange: o }) {
  const s = Pd(n, t), a = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-pagination", "aria-label": "Workflow definition pagination", children: [
    /* @__PURE__ */ r.jsxs("span", { className: "wf-pagination-summary", "aria-live": "polite", children: [
      "Showing ",
      a,
      "-",
      c,
      " of ",
      n
    ] }),
    /* @__PURE__ */ r.jsxs("label", { className: "wf-page-size", children: [
      "Rows",
      /* @__PURE__ */ r.jsx("select", { value: t, onChange: (u) => o(Number(u.target.value)), children: Mb.map((u) => /* @__PURE__ */ r.jsx("option", { value: u, children: u }, u)) })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => i(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ r.jsx($n, { size: 14 }),
        " Previous"
      ] }),
      /* @__PURE__ */ r.jsxs("span", { children: [
        "Page ",
        e,
        " of ",
        s
      ] }),
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => i(e + 1), disabled: e >= s, "aria-label": "Next page", title: "Next page", children: [
        "Next ",
        /* @__PURE__ */ r.jsx(mt, { size: 14 })
      ] })
    ] })
  ] });
}
function kj({ draft: e, creating: t, ai: n, suggestMetadataAction: i, onChange: o, onClose: s, onSubmit: a }) {
  const [c, u] = K(!1), [l, d] = K(""), [f, p] = K(!1), [h, y] = K(null), [v, w] = K(null), x = oe(null), b = oe(e);
  b.current = e;
  const g = oe(o);
  g.current = o;
  const m = se((N) => {
    const S = { ...b.current };
    N.name && (S.name = N.name), N.description && (S.description = N.description), g.current(S), y(null), w(null);
  }, []);
  Q(() => {
    if (i)
      return n.onPromptResult((N) => {
        if (N.requestId !== x.current) return;
        if (x.current = null, p(!1), N.status !== "completed") {
          w(N.status === "cancelled" ? "Weaver needs more detail — continue in the assistant panel." : "Weaver couldn't generate a suggestion. Try again or fill the fields manually.");
          return;
        }
        const S = Ob(N.text);
        if (!S) {
          w("Couldn't read a suggestion from Weaver's reply. See the assistant panel.");
          return;
        }
        N.autoApply ? m(S) : y(S);
      });
  }, [n, i, m]);
  const j = () => {
    if (!i) return;
    const N = i.createPrompt({ draft: b.current, intent: l });
    if (!N) return;
    const S = `wf-suggest-${Date.now()}-${Math.round(Math.random() * 1e6)}`;
    x.current = S, p(!0), y(null), w(null), n.dispatchPrompt({ ...N, requestId: S });
  };
  return /* @__PURE__ */ r.jsx("div", { className: "wf-dialog-backdrop", role: "presentation", children: /* @__PURE__ */ r.jsx("section", { className: "wf-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "workflow-create-title", children: /* @__PURE__ */ r.jsxs(
    "form",
    {
      onSubmit: (N) => {
        N.preventDefault(), a();
      },
      children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-dialog-heading", children: [
          /* @__PURE__ */ r.jsx("h3", { id: "workflow-create-title", children: "Create Workflow" }),
          i ? /* @__PURE__ */ r.jsxs(
            "button",
            {
              type: "button",
              className: "wf-ai-action",
              "aria-expanded": c,
              onClick: () => u((N) => !N),
              title: i.description ?? i.label,
              children: [
                /* @__PURE__ */ r.jsx(ut, { size: 13 }),
                " ",
                i.label
              ]
            }
          ) : null
        ] }),
        i && c ? /* @__PURE__ */ r.jsxs("div", { className: "wf-ai-suggest", role: "group", "aria-label": "Suggest name and description", children: [
          /* @__PURE__ */ r.jsxs("label", { className: "wf-form-field", children: [
            /* @__PURE__ */ r.jsx("span", { children: "What should this workflow do?" }),
            /* @__PURE__ */ r.jsx(
              "textarea",
              {
                autoFocus: !0,
                "aria-label": "Workflow intent",
                rows: 2,
                placeholder: "e.g. Route incoming support tickets to the right team and notify on SLA breach (optional)",
                value: l,
                disabled: f,
                onChange: (N) => d(N.target.value),
                onKeyDown: (N) => {
                  (N.metaKey || N.ctrlKey) && N.key === "Enter" && (N.preventDefault(), j());
                }
              }
            )
          ] }),
          /* @__PURE__ */ r.jsx("div", { className: "wf-ai-suggest-actions", children: /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-ai-action", onClick: j, disabled: f, children: [
            /* @__PURE__ */ r.jsx(ut, { size: 13 }),
            " ",
            f ? "Generating…" : "Generate"
          ] }) }),
          v ? /* @__PURE__ */ r.jsx("p", { className: "wf-ai-suggest-error", role: "alert", children: v }) : null,
          h ? /* @__PURE__ */ r.jsxs("div", { className: "wf-ai-suggest-preview", children: [
            h.name ? /* @__PURE__ */ r.jsx("p", { children: /* @__PURE__ */ r.jsx("strong", { children: h.name }) }) : null,
            h.description ? /* @__PURE__ */ r.jsx("p", { children: h.description }) : null,
            /* @__PURE__ */ r.jsxs("div", { className: "wf-ai-suggest-actions", children: [
              /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => m(h), children: "Apply" }),
              /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => y(null), children: "Dismiss" })
            ] })
          ] }) : null
        ] }) : null,
        /* @__PURE__ */ r.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ r.jsx("span", { children: "Display name" }),
          /* @__PURE__ */ r.jsx(
            "input",
            {
              autoFocus: !0,
              "aria-label": "Display name",
              value: e.name,
              onChange: (N) => o({ ...e, name: N.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ r.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ r.jsx("span", { children: "Description" }),
          /* @__PURE__ */ r.jsx(
            "textarea",
            {
              "aria-label": "Description",
              rows: 3,
              value: e.description,
              onChange: (N) => o({ ...e, description: N.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ r.jsxs("fieldset", { className: "wf-form-field wf-root-field", children: [
          /* @__PURE__ */ r.jsx("legend", { children: "Root activity" }),
          /* @__PURE__ */ r.jsx(
            Nj,
            {
              value: e.rootKind,
              onChange: (N) => o({ ...e, rootKind: N, rootActivityVersionId: null })
            }
          )
        ] }),
        /* @__PURE__ */ r.jsxs("div", { className: "wf-dialog-actions", children: [
          /* @__PURE__ */ r.jsx("button", { type: "button", onClick: s, disabled: t, children: "Cancel" }),
          /* @__PURE__ */ r.jsx("button", { type: "submit", disabled: t || !e.name.trim(), children: t ? "Creating..." : "Create" })
        ] })
      ]
    }
  ) }) });
}
function Ij({ context: e, ai: t, onOpen: n }) {
  const [i, o] = K(""), [s, a] = K("active"), [c, u] = K(1), [l, d] = K(Rb), [f, p] = K("loading"), [h, y] = K(""), [v, w] = K(""), [x, b] = K([]), [g, m] = K(0), [j, N] = K(() => /* @__PURE__ */ new Set()), [S, k] = K(null), [_, M] = K(!1), [I, $] = K([]), [L, C] = K("idle"), A = oe(null), E = de(() => x.map((O) => O.id), [x]), D = Pt(t, "weaver.workflows.suggest-create-metadata"), P = Pt(t, "weaver.workflows.explain-definition"), T = E.filter((O) => j.has(O)).length, B = E.length > 0 && T === E.length, W = se(async () => {
    p("loading"), y("");
    try {
      const O = await Xp(e, { search: i, state: s, page: c, pageSize: l }), te = typeof O.totalCount == "number", ge = O.totalCount ?? O.definitions.length, ye = Pd(ge, l);
      if (ge > 0 && c > ye) {
        u(ye);
        return;
      }
      b(te ? O.definitions : Vb(O.definitions, c, l)), m(ge), p("ready");
    } catch (O) {
      y(O instanceof Error ? O.message : String(O)), p("failed");
    }
  }, [e, i, s, c, l]);
  Q(() => {
    W();
  }, [W]), Q(() => {
    A.current && (A.current.indeterminate = T > 0 && !B);
  }, [B, T]);
  const H = se(async () => {
    if (!(L === "loading" || L === "ready")) {
      C("loading");
      try {
        const O = await so(e);
        $(O.activities ?? []), C("ready");
      } catch (O) {
        C("failed"), y(O instanceof Error ? O.message : String(O));
      }
    }
  }, [L, e]), q = () => {
    y(""), w(""), k({ name: "", description: "", rootKind: "flowchart" }), H();
  }, V = async () => {
    if (S?.name.trim()) {
      M(!0), y(""), w("");
      try {
        const O = await Gp(e, {
          name: S.name.trim(),
          description: S.description.trim() || null,
          rootKind: S.rootKind,
          rootActivityVersionId: Hb(S, I)
        });
        k(null), n(O.definition.id);
      } catch (O) {
        y(O instanceof Error ? O.message : String(O));
      } finally {
        M(!1);
      }
    }
  }, Y = (O) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(O)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, J = async () => {
    if (x.length === 1 && c > 1) {
      u(c - 1);
      return;
    }
    await W();
  }, Z = () => N(/* @__PURE__ */ new Set()), R = (O, te) => {
    N((ge) => {
      const ye = new Set(ge);
      return te ? ye.add(O) : ye.delete(O), ye;
    });
  }, X = (O) => {
    N((te) => {
      const ge = new Set(te);
      for (const ye of E)
        O ? ge.add(ye) : ge.delete(ye);
      return ge;
    });
  }, le = (O) => {
    a(O), u(1), Z();
  }, ce = (O) => {
    o(O), u(1), Z();
  }, ee = async (O) => {
    if (await Xs().confirm({ message: `Delete workflow definition "${O.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" })) {
      w(""), y("");
      try {
        await Jp(e, O.id), R(O.id, !1), w(`Deleted ${O.name}`), await J();
      } catch (te) {
        y(te instanceof Error ? te.message : String(te));
      }
    }
  }, ne = async (O) => {
    w(""), y("");
    try {
      await Qp(e, O.id), R(O.id, !1), w(`Restored ${O.name}`), await J();
    } catch (te) {
      y(te instanceof Error ? te.message : String(te));
    }
  }, fe = async (O) => {
    if (await Xs().confirm({ message: `Permanently delete workflow definition "${O.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) {
      w(""), y("");
      try {
        await eh(e, O.id), R(O.id, !1), w(`Permanently deleted ${O.name}`), await J();
      } catch (te) {
        y(te instanceof Error ? te.message : String(te));
      }
    }
  };
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ r.jsx("button", { type: "button", className: s === "active" ? "active" : "", "aria-selected": s === "active", onClick: () => le("active"), children: "Active" }),
        /* @__PURE__ */ r.jsx("button", { type: "button", className: s === "deleted" ? "active" : "", "aria-selected": s === "deleted", onClick: () => le("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ r.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ r.jsx(io, { size: 15 }),
        /* @__PURE__ */ r.jsx("input", { value: i, onChange: (O) => ce(O.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => {
        W();
      }, children: "Refresh" }),
      /* @__PURE__ */ r.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ r.jsxs("button", { type: "button", title: "Create workflow", onClick: q, children: [
        /* @__PURE__ */ r.jsx(Qt, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ r.jsx(ii, { message: h, title: "Couldn't load workflow definitions" }) : null,
    f !== "failed" && h ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ r.jsx(vt, { size: 16 }),
      " ",
      h
    ] }) : null,
    v ? /* @__PURE__ */ r.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ r.jsx(ln, { size: 14 }),
      " ",
      v
    ] }) : null,
    j.size > 0 ? /* @__PURE__ */ r.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ r.jsxs("span", { children: [
        j.size,
        " selected"
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: Z, children: "Clear selection" })
    ] }) : null,
    f === "loading" ? /* @__PURE__ */ r.jsx($s, {}) : null,
    f === "ready" && x.length === 0 ? /* @__PURE__ */ r.jsx(
      Ps,
      {
        icon: /* @__PURE__ */ r.jsx(el, { size: 22 }),
        title: `No ${s} workflow definitions`,
        description: "Create a workflow to start designing automation, or adjust your filters to see more.",
        action: /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-link-button", onClick: q, children: [
          /* @__PURE__ */ r.jsx(Qt, { size: 15 }),
          " Create workflow"
        ] })
      }
    ) : null,
    f === "ready" && x.length > 0 ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ r.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ r.jsx(
            "input",
            {
              ref: A,
              type: "checkbox",
              checked: B,
              onChange: (O) => X(O.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ r.jsx("span", { children: "Name" }),
          /* @__PURE__ */ r.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ r.jsx("span", { children: s === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ r.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ r.jsx("span", { children: "Actions" })
        ] }),
        x.map((O) => /* @__PURE__ */ r.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${O.name}`,
            "aria-selected": j.has(O.id),
            tabIndex: 0,
            onClick: () => n(O.id),
            onKeyDown: (te) => {
              te.currentTarget === te.target && (te.key !== "Enter" && te.key !== " " || (te.preventDefault(), n(O.id)));
            },
            children: [
              /* @__PURE__ */ r.jsx("label", { className: "wf-row-select", onClick: (te) => te.stopPropagation(), children: /* @__PURE__ */ r.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: j.has(O.id),
                  onChange: (te) => R(O.id, te.target.checked),
                  "aria-label": `Select workflow definition ${O.name}`
                }
              ) }),
              /* @__PURE__ */ r.jsxs("span", { children: [
                /* @__PURE__ */ r.jsx("strong", { children: O.name }),
                /* @__PURE__ */ r.jsx("small", { children: O.description || O.id })
              ] }),
              /* @__PURE__ */ r.jsx("span", { children: O.latestVersion ?? "No version" }),
              /* @__PURE__ */ r.jsx("span", { children: s === "deleted" ? Re(O.deletedAt) : O.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ r.jsx("span", { children: Re(O.lastModifiedAt) }),
              /* @__PURE__ */ r.jsx("span", { className: "wf-row-actions", onClick: (te) => te.stopPropagation(), children: s === "active" ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
                /* @__PURE__ */ r.jsx("button", { type: "button", onClick: (te) => {
                  te.stopPropagation(), n(O.id);
                }, children: "Open" }),
                /* @__PURE__ */ r.jsx("button", { type: "button", onClick: (te) => {
                  te.stopPropagation(), Y(O.id);
                }, children: "Artifacts" }),
                P ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => Mt(t, P, O), children: [
                  /* @__PURE__ */ r.jsx(ut, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ r.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  ee(O);
                }, children: [
                  /* @__PURE__ */ r.jsx(Dn, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
                /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
                  ne(O);
                }, children: [
                  /* @__PURE__ */ r.jsx(ts, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ r.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  fe(O);
                }, children: [
                  /* @__PURE__ */ r.jsx(Dn, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          O.id
        ))
      ] }),
      /* @__PURE__ */ r.jsx(
        Ej,
        {
          page: c,
          pageSize: l,
          totalCount: g,
          onPageChange: u,
          onPageSizeChange: (O) => {
            d(O), u(1);
          }
        }
      )
    ] }) : null,
    S ? /* @__PURE__ */ r.jsx(
      kj,
      {
        draft: S,
        creating: _,
        ai: t,
        suggestMetadataAction: D,
        onChange: (O) => k(O),
        onClose: () => k(null),
        onSubmit: V
      }
    ) : null
  ] });
}
function Aj({ activities: e, activityCatalog: t, selectedEvidenceId: n = null, onSelectEvidence: i }) {
  const o = de(
    () => new Map(t.map((a) => [a.activityTypeKey, a])),
    [t]
  ), s = de(() => Dj(e), [e]);
  return s.length === 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty wf-timeline-empty", children: "No activity executions recorded yet." }) : /* @__PURE__ */ r.jsx("ol", { className: "wf-timeline", "aria-label": "Execution timeline", children: s.map((a) => {
    const c = o.get(a.activityType), u = fn(c), l = c ? Ce(c) : It(a.activityType) ?? a.activityType, d = It(a.activityType) ?? a.activityType, f = Tj(a.startedAt ?? a.scheduledAt), p = ds(a.startedAt, a.completedAt);
    return /* @__PURE__ */ r.jsx("li", { children: /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-timeline-entry",
        "data-selected": a.activityExecutionId === n,
        onClick: () => i?.(a.activityExecutionId),
        children: [
          /* @__PURE__ */ r.jsx("span", { className: "wf-timeline-icon wf-activity-icon", "data-icon": u, "aria-hidden": "true", children: co(u) }),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-timeline-body", children: [
            /* @__PURE__ */ r.jsx("strong", { title: l, children: l }),
            /* @__PURE__ */ r.jsx("small", { title: d, children: d })
          ] }),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-timeline-meta", children: [
            f ? /* @__PURE__ */ r.jsx("time", { children: f }) : null,
            p ? /* @__PURE__ */ r.jsxs("small", { children: [
              "took ",
              p
            ] }) : null
          ] }),
          /* @__PURE__ */ r.jsx(_j, { status: a.status })
        ]
      }
    ) }, a.activityExecutionId);
  }) });
}
function _j({ status: e }) {
  const t = e.toLowerCase(), n = t === "completed" ? "OK" : e;
  return /* @__PURE__ */ r.jsx("span", { className: "wf-status-badge wf-timeline-pill", "data-status": t, children: n });
}
function Dj(e) {
  return e.map((t, n) => ({ activity: t, index: n })).sort((t, n) => Fc(t.activity) - Fc(n.activity) || t.index - n.index).map((t) => t.activity);
}
function Fc(e) {
  const t = e.startedAt ?? e.scheduledAt, n = t ? Date.parse(t) : Number.NaN;
  return Number.isNaN(n) ? Number.MAX_SAFE_INTEGER : n;
}
function Tj(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? "" : t.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: !1 });
}
function $j({ context: e }) {
  const [t, n] = K("loading"), [i, o] = K(""), [s, a] = K(""), [c, u] = K(""), [l, d] = K([]), f = se(async () => {
    n("loading"), o("");
    try {
      const h = await ah(e, {
        status: s || void 0,
        runKind: c || void 0,
        take: 100
      });
      d(h), n("ready");
    } catch (h) {
      o(h instanceof Error ? h.message : String(h)), d([]), n("failed");
    }
  }, [e, c, s]);
  Q(() => {
    f();
  }, [f]);
  const p = (h) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(h)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => {
        f();
      }, children: "Refresh" }),
      /* @__PURE__ */ r.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ r.jsx("span", { children: "Status" }),
        /* @__PURE__ */ r.jsxs("select", { "aria-label": "Workflow run status", value: s, onChange: (h) => a(h.target.value), children: [
          /* @__PURE__ */ r.jsx("option", { value: "", children: "All statuses" }),
          /* @__PURE__ */ r.jsx("option", { value: "Pending", children: "Pending" }),
          /* @__PURE__ */ r.jsx("option", { value: "Running", children: "Running" }),
          /* @__PURE__ */ r.jsx("option", { value: "Suspended", children: "Suspended" }),
          /* @__PURE__ */ r.jsx("option", { value: "Completed", children: "Completed" }),
          /* @__PURE__ */ r.jsx("option", { value: "Faulted", children: "Faulted" }),
          /* @__PURE__ */ r.jsx("option", { value: "Cancelled", children: "Cancelled" })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ r.jsx("span", { children: "Kind" }),
        /* @__PURE__ */ r.jsxs("select", { "aria-label": "Run Kind", value: c, onChange: (h) => u(h.target.value), children: [
          /* @__PURE__ */ r.jsx("option", { value: "", children: "All kinds" }),
          /* @__PURE__ */ r.jsx("option", { value: "TestRun", children: "Test Run" }),
          /* @__PURE__ */ r.jsx("option", { value: "PublishedRun", children: "Published Run" }),
          /* @__PURE__ */ r.jsx("option", { value: "BackgroundWeaverRun", children: "Background Weaver Run" }),
          /* @__PURE__ */ r.jsx("option", { value: "Unknown", children: "Unknown / legacy" })
        ] })
      ] })
    ] }),
    t === "failed" ? /* @__PURE__ */ r.jsx(ii, { message: i }) : null,
    t === "loading" ? /* @__PURE__ */ r.jsx($s, {}) : null,
    t === "ready" && l.length === 0 ? /* @__PURE__ */ r.jsx(
      Ps,
      {
        icon: /* @__PURE__ */ r.jsx(qn, { size: 22 }),
        title: "No workflow runs yet",
        description: "Run a published workflow executable to create execution history here."
      }
    ) : null,
    t === "ready" && l.length > 0 ? /* @__PURE__ */ r.jsxs("div", { className: "wf-grid wf-instance-grid", role: "table", "aria-label": "Workflow runs", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ r.jsx("span", { children: "Run" }),
        /* @__PURE__ */ r.jsx("span", { children: "Kind" }),
        /* @__PURE__ */ r.jsx("span", { children: "Status" }),
        /* @__PURE__ */ r.jsx("span", { children: "Definition" }),
        /* @__PURE__ */ r.jsx("span", { children: "Activity" }),
        /* @__PURE__ */ r.jsx("span", { children: "Started" }),
        /* @__PURE__ */ r.jsx("span", { children: "Duration" })
      ] }),
      l.map((h) => /* @__PURE__ */ r.jsxs(
        "button",
        {
          type: "button",
          className: "wf-grid-row",
          role: "row",
          "aria-label": `Inspect workflow run ${h.workflowExecutionId}`,
          onClick: () => p(h.workflowExecutionId),
          children: [
            /* @__PURE__ */ r.jsxs("span", { children: [
              /* @__PURE__ */ r.jsx("strong", { children: h.workflowExecutionId }),
              /* @__PURE__ */ r.jsx("small", { children: h.artifactId })
            ] }),
            /* @__PURE__ */ r.jsx("span", { children: Vd(h.runKind) }),
            /* @__PURE__ */ r.jsx("span", { children: /* @__PURE__ */ r.jsx(hn, { status: h.status, subStatus: h.subStatus }) }),
            /* @__PURE__ */ r.jsxs("span", { children: [
              /* @__PURE__ */ r.jsx("strong", { children: h.definitionId }),
              /* @__PURE__ */ r.jsx("small", { children: h.definitionVersionId })
            ] }),
            /* @__PURE__ */ r.jsxs("span", { children: [
              /* @__PURE__ */ r.jsxs("strong", { children: [
                h.activityCount,
                " activities"
              ] }),
              /* @__PURE__ */ r.jsxs("small", { children: [
                h.incidentCount,
                " incidents"
              ] })
            ] }),
            /* @__PURE__ */ r.jsx("span", { children: Re(h.startedAt ?? h.createdAt) }),
            /* @__PURE__ */ r.jsx("span", { children: ds(h.startedAt ?? h.createdAt, h.completedAt ?? h.updatedAt) })
          ]
        },
        h.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function Pj({ context: e, ai: t, workflowExecutionId: n }) {
  const [i, o] = K("loading"), [s, a] = K(""), [c, u] = K(null), [l, d] = K(null), [f, p] = K([]), {
    inspectorWidth: h,
    inspectorCollapsed: y,
    maximizedSidePanel: v,
    inspectorExpanded: w,
    editorBodyStyle: x,
    toggleSidePanelCollapsed: b,
    toggleSidePanelMaximized: g,
    startSidePanelResize: m,
    handleSidePanelResizeKeyDown: j
  } = qd(), N = Pt(t, "weaver.workflows.explain-instance"), S = se(async () => {
    if (!n) {
      a("No workflow execution id was provided."), o("failed");
      return;
    }
    o("loading"), a("");
    try {
      const I = await ch(e, n), [$, L] = await Promise.all([
        Zp(e, I.instance.definitionVersionId).then(
          (C) => ({ definitionVersion: C, error: "" }),
          (C) => ({ definitionVersion: null, error: C instanceof Error ? C.message : String(C) })
        ),
        so(e)
      ]);
      u({
        details: I,
        definitionVersion: $.definitionVersion,
        definitionVersionError: $.error,
        activityCatalog: L.activities
      }), d(null), p([]), o("ready");
    } catch (I) {
      u(null), a(uN(I, n)), o("failed");
    }
  }, [e, n]);
  Q(() => {
    S();
  }, [S]);
  const k = () => {
    window.history.pushState({}, "", "/workflows/instances"), window.dispatchEvent(new PopStateEvent("popstate"));
  }, _ = () => {
    const I = c?.details.instance.definitionId;
    I && (window.history.pushState({}, "", `/workflows/definitions?definition=${encodeURIComponent(I)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  }, M = [
    "wf-instance-detail-workbench",
    y ? "inspector-collapsed" : "",
    v === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: k, children: [
        /* @__PURE__ */ r.jsx($n, { size: 14 }),
        " Runs"
      ] }),
      c?.details.instance.definitionId ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: _, children: [
        /* @__PURE__ */ r.jsx(tl, { size: 14 }),
        " Designer"
      ] }) : null,
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
        S();
      }, children: [
        /* @__PURE__ */ r.jsx(ts, { size: 14 }),
        " Refresh"
      ] }),
      c && N ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => Mt(t, N, c.details), children: [
        /* @__PURE__ */ r.jsx(ut, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    i === "loading" ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Loading workflow run..." }) : null,
    i === "failed" ? /* @__PURE__ */ r.jsx(ii, { message: s }) : null,
    i === "ready" && c ? /* @__PURE__ */ r.jsxs("div", { className: M, style: x, children: [
      /* @__PURE__ */ r.jsx(
        Mj,
        {
          definitionVersion: c.definitionVersion,
          definitionVersionError: c.definitionVersionError,
          activityCatalog: c.activityCatalog,
          details: c.details,
          selectedEvidenceId: l,
          onSelectEvidence: d,
          frames: f,
          onNavigateToScope: p
        }
      ),
      w && !v ? /* @__PURE__ */ r.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize run details panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Yt,
          "aria-valuemax": qt,
          "aria-valuenow": h,
          tabIndex: 0,
          onPointerDown: (I) => m("inspector", I),
          onKeyDown: (I) => j("inspector", I)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsx(
        Lj,
        {
          context: e,
          ai: t,
          action: N ?? void 0,
          summary: c.details.instance,
          details: c.details,
          state: "ready",
          error: "",
          selectedEvidenceId: l,
          onSelectEvidence: d,
          activityCatalog: c.activityCatalog,
          graphNodeIds: c.definitionVersion ? zj(c.definitionVersion, c.activityCatalog) : void 0,
          rootNodeId: c.definitionVersion?.state.rootActivity?.nodeId,
          collapsed: y,
          expanded: w,
          maximized: v === "inspector",
          onToggleCollapsed: () => b("inspector"),
          onToggleMaximized: () => g("inspector")
        }
      )
    ] }) : null
  ] });
}
function Mj({
  definitionVersion: e,
  definitionVersionError: t,
  activityCatalog: n,
  details: i,
  selectedEvidenceId: o,
  onSelectEvidence: s,
  frames: a,
  onNavigateToScope: c
}) {
  const u = Rj(a), l = de(() => {
    if (!e) return { nodes: [], edges: [] };
    const d = e.state.rootActivity;
    if (!d) return { nodes: [], edges: [] };
    const f = Mn(d, a, n), p = f?.owner ?? d, h = n.find((x) => x.activityVersionId === p.activityVersionId), v = pl(p, h) === "unsupported" || !f ? fl(p, n, e.layout) : dl(f, n, e.layout), w = v.nodes.map((x) => ({
      ...x,
      draggable: !1,
      connectable: !1,
      deletable: !1,
      data: {
        ...x.data,
        onEnterSlot: (b) => {
          c([...a, {
            ownerNodeId: x.id,
            slotId: b.id,
            label: `${x.data.label} / ${b.label}`
          }]);
        }
      }
    }));
    return {
      nodes: hp(w, i.activities, i.incidents, o),
      edges: v.edges.map((x) => ({ ...x, deletable: !1 }))
    };
  }, [n, e, i, a, c, o]);
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-canvas-shell", "aria-label": "Workflow run canvas", children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("span", { children: "Definition version" }),
        /* @__PURE__ */ r.jsx("h3", { children: e ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
          e.definition.name,
          " ",
          /* @__PURE__ */ r.jsx("small", { children: e.version })
        ] }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
          "Definition graph unavailable ",
          /* @__PURE__ */ r.jsx("small", { children: i.instance.definitionVersionId })
        ] }) })
      ] }),
      /* @__PURE__ */ r.jsx(hn, { status: i.instance.status, subStatus: i.instance.subStatus })
    ] }),
    e ? /* @__PURE__ */ r.jsxs("div", { className: "wf-breadcrumb wf-instance-breadcrumb", children: [
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => c([]), children: "Root" }),
      a.map((d, f) => /* @__PURE__ */ r.jsxs("span", { className: "wf-breadcrumb-segment", children: [
        /* @__PURE__ */ r.jsx(mt, { size: 13 }),
        /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => c(a.slice(0, f + 1)), children: d.label })
      ] }, `${d.ownerNodeId}-${d.slotId}-${f}`))
    ] }) : null,
    /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ r.jsxs("div", { className: "wf-empty", children: [
        "The workflow run loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ r.jsx("small", { children: lN(t) }) : null
      ] }),
      e && l.nodes.length === 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      l.nodes.length > 0 ? /* @__PURE__ */ r.jsxs(
        bd,
        {
          nodes: l.nodes,
          edges: l.edges,
          nodeTypes: Fd,
          edgeTypes: Kd,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (d, f) => s(f.id),
          onPaneClick: () => s(null),
          children: [
            /* @__PURE__ */ r.jsx(jd, {}),
            /* @__PURE__ */ r.jsx(kd, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ r.jsx(Cd, {})
          ]
        },
        u
      ) : null
    ] })
  ] });
}
function Rj(e) {
  return e.length === 0 ? "root" : e.map((t) => `${t.ownerNodeId}:${t.slotId}`).join("/");
}
function zj(e, t) {
  const n = /* @__PURE__ */ new Set(), i = (o) => {
    if (o) {
      n.add(o.nodeId);
      for (const s of ze(o, t))
        s.activities.forEach(i);
    }
  };
  return i(e.state.rootActivity), n;
}
function Lj({
  context: e,
  ai: t,
  action: n,
  summary: i,
  details: o,
  state: s,
  error: a,
  selectedEvidenceId: c = null,
  onSelectEvidence: u,
  graphNodeIds: l,
  rootNodeId: d,
  activityCatalog: f = [],
  collapsed: p = !1,
  expanded: h = !0,
  maximized: y = !1,
  onToggleCollapsed: v,
  onToggleMaximized: w
}) {
  const [x, b] = K("timeline");
  if (!i)
    return /* @__PURE__ */ r.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Select a workflow run to inspect its timeline." }) });
  const g = o?.incidents.length ?? 0, m = Vj(o?.activities ?? [], c), j = (S) => {
    u?.(S), b("activity");
  }, N = [
    { id: "timeline", title: "Timeline", order: 0, icon: /* @__PURE__ */ r.jsx(Jr, { size: 14 }), render: () => null },
    { id: "activity", title: "Activity", order: 1, icon: /* @__PURE__ */ r.jsx(Gc, { size: 14 }), render: () => null },
    { id: "issues", title: g > 0 ? `Issues (${g})` : "Issues", order: 2, icon: /* @__PURE__ */ r.jsx(vt, { size: 14 }), render: () => null },
    { id: "details", title: "Details", order: 3, icon: /* @__PURE__ */ r.jsx(Qr, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ r.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Run details panel", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title wf-instance-panel-title", children: [
      /* @__PURE__ */ r.jsx(Oi, { label: "Run details tabs", tabs: N, activeTabId: x, onSelect: (S) => b(S) }),
      /* @__PURE__ */ r.jsxs("span", { className: "wf-panel-actions", children: [
        /* @__PURE__ */ r.jsx(
          "button",
          {
            type: "button",
            className: "wf-panel-action-button",
            "aria-label": p ? "Expand run details panel" : "Collapse run details panel",
            title: p ? "Expand" : "Collapse",
            onClick: v,
            children: p ? /* @__PURE__ */ r.jsx($n, { size: 14 }) : /* @__PURE__ */ r.jsx(mt, { size: 14 })
          }
        ),
        p ? null : /* @__PURE__ */ r.jsx(
          "button",
          {
            type: "button",
            className: "wf-panel-action-button",
            "aria-label": y ? "Restore run details panel" : "Maximize run details panel",
            title: y ? "Restore" : "Maximize",
            onClick: w,
            children: y ? /* @__PURE__ */ r.jsx(Cr, { size: 14 }) : /* @__PURE__ */ r.jsx(Tn, { size: 14 })
          }
        )
      ] })
    ] }),
    h ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
      /* @__PURE__ */ r.jsxs("header", { children: [
        /* @__PURE__ */ r.jsxs("div", { children: [
          /* @__PURE__ */ r.jsx("span", { children: "Workflow Instance ID" }),
          /* @__PURE__ */ r.jsx("h3", { children: i.workflowExecutionId })
        ] }),
        n ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => Mt(t, n, o ?? i), children: [
          /* @__PURE__ */ r.jsx(ut, { size: 13 }),
          " Explain"
        ] }) : null
      ] }),
      s === "loading" ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Loading run details..." }) : null,
      s === "failed" ? /* @__PURE__ */ r.jsx(ii, { message: a }) : null,
      s === "ready" && o ? /* @__PURE__ */ r.jsx("div", { className: "wf-instance-tab-content", children: x === "timeline" ? /* @__PURE__ */ r.jsx(
        Aj,
        {
          activities: o.activities,
          activityCatalog: f,
          selectedEvidenceId: c,
          onSelectEvidence: j
        }
      ) : x === "activity" ? /* @__PURE__ */ r.jsx(Oj, { context: e, activity: m, activityCatalog: f }) : x === "issues" ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
        /* @__PURE__ */ r.jsx(qj, { incidents: o.incidents, selectedEvidenceId: c, onSelectEvidence: u }),
        /* @__PURE__ */ r.jsx(Qj, { details: o, graphNodeIds: l, rootNodeId: d })
      ] }) : /* @__PURE__ */ r.jsxs("dl", { className: "wf-instance-meta", children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ r.jsx("dd", { children: /* @__PURE__ */ r.jsx(hn, { status: i.status, subStatus: i.subStatus }) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Run Kind" }),
        /* @__PURE__ */ r.jsx("dd", { children: Vd(i.runKind) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Artifact" }),
        /* @__PURE__ */ r.jsxs("dd", { children: [
          i.artifactId,
          " ",
          /* @__PURE__ */ r.jsx("small", { children: i.artifactVersion })
        ] }),
        /* @__PURE__ */ r.jsx("dt", { children: "Definition" }),
        /* @__PURE__ */ r.jsxs("dd", { children: [
          i.definitionId,
          " ",
          /* @__PURE__ */ r.jsx("small", { children: i.definitionVersionId })
        ] }),
        /* @__PURE__ */ r.jsx("dt", { children: "Created" }),
        /* @__PURE__ */ r.jsx("dd", { children: Re(i.createdAt) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Started" }),
        /* @__PURE__ */ r.jsx("dd", { children: Re(i.startedAt) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Completed" }),
        /* @__PURE__ */ r.jsx("dd", { children: Re(i.completedAt) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Correlation" }),
        /* @__PURE__ */ r.jsx("dd", { children: i.correlationId || "None" })
      ] }) }) : null
    ] }) : null
  ] });
}
function Vj(e, t) {
  if (!t) return null;
  const n = e.find((o) => o.activityExecutionId === t);
  if (n) return n;
  const i = e.filter((o) => o.executableNodeId === t || o.authoredActivityId === t);
  return i.length > 0 ? wl(i) : null;
}
function Oj({ context: e, activity: t, activityCatalog: n }) {
  const i = t?.activityExecutionId ?? null, o = t?.workflowExecutionId ?? null, [s, a] = K({
    activityExecutionId: null,
    status: "idle",
    inspection: null,
    error: ""
  });
  if (Q(() => {
    if (!i || !o) {
      a({ activityExecutionId: null, status: "idle", inspection: null, error: "" });
      return;
    }
    let f = !1;
    const p = i;
    return a({ activityExecutionId: p, status: "loading", inspection: null, error: "" }), lh(e, o, p).then(
      (h) => {
        f || a({ activityExecutionId: p, status: "ready", inspection: h, error: "" });
      },
      (h) => {
        f || a({
          activityExecutionId: p,
          status: "failed",
          inspection: null,
          error: h instanceof Error ? h.message : String(h)
        });
      }
    ), () => {
      f = !0;
    };
  }, [e, i, o]), !t)
    return /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
      /* @__PURE__ */ r.jsx("h4", { children: "Activity" }),
      /* @__PURE__ */ r.jsx("p", { children: "No activity selected." })
    ] });
  const u = n.find((f) => f.activityTypeKey === t.activityType)?.displayName || It(t.activityType) || t.activityType, l = t.bookmarkIds?.length ?? 0, d = t.incidentIds?.length ?? 0;
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
      /* @__PURE__ */ r.jsx("h4", { children: "Activity" }),
      /* @__PURE__ */ r.jsxs("dl", { className: "wf-instance-meta", children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Name" }),
        /* @__PURE__ */ r.jsx("dd", { children: u }),
        /* @__PURE__ */ r.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ r.jsx("dd", { children: /* @__PURE__ */ r.jsx(hn, { status: t.status, subStatus: t.subStatus }) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Activity Execution ID" }),
        /* @__PURE__ */ r.jsx("dd", { children: t.activityExecutionId }),
        /* @__PURE__ */ r.jsx("dt", { children: "Authored Activity ID" }),
        /* @__PURE__ */ r.jsx("dd", { children: t.authoredActivityId }),
        /* @__PURE__ */ r.jsx("dt", { children: "Type" }),
        /* @__PURE__ */ r.jsxs("dd", { children: [
          It(t.activityType) ?? t.activityType,
          " ",
          /* @__PURE__ */ r.jsx("small", { children: t.activityTypeVersion })
        ] }),
        /* @__PURE__ */ r.jsx("dt", { children: "Started" }),
        /* @__PURE__ */ r.jsx("dd", { children: Re(t.startedAt) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Completed" }),
        /* @__PURE__ */ r.jsx("dd", { children: Re(t.completedAt) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Duration" }),
        /* @__PURE__ */ r.jsx("dd", { children: ds(t.startedAt, t.completedAt) || "Unknown" }),
        /* @__PURE__ */ r.jsx("dt", { children: "Bookmarks" }),
        /* @__PURE__ */ r.jsx("dd", { children: l }),
        /* @__PURE__ */ r.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ r.jsx("dd", { children: d })
      ] })
    ] }),
    /* @__PURE__ */ r.jsx(Hj, { state: s })
  ] });
}
function Hj({ state: e }) {
  if (e.status === "idle") return null;
  if (e.status === "loading")
    return /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
      /* @__PURE__ */ r.jsx("h4", { children: "Inputs" }),
      /* @__PURE__ */ r.jsx("p", { children: "Loading runtime input evidence..." })
    ] });
  if (e.status === "failed")
    return /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
      /* @__PURE__ */ r.jsx("h4", { children: "Inputs" }),
      /* @__PURE__ */ r.jsx("p", { children: "Runtime input evidence is unavailable." }),
      e.error ? /* @__PURE__ */ r.jsx("p", { className: "wf-instance-note", children: e.error }) : null
    ] });
  const t = (e.inspection?.valueSnapshots ?? []).filter((n) => n.subject === "ActivityInput");
  return t.length === 0 ? /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ r.jsx("h4", { children: "Inputs" }),
    /* @__PURE__ */ r.jsx("p", { children: "No runtime input snapshots were recorded for this execution." })
  ] }) : /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ r.jsx("h4", { children: "Inputs" }),
    /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-input-list", children: t.map((n) => /* @__PURE__ */ r.jsx(Wj, { snapshot: n }, `${n.name}:${n.capturedAt}:${n.captureMode}`)) })
  ] });
}
function Wj({ snapshot: e }) {
  const t = e.type?.displayName || e.type?.typeName || e.type?.alias || "Unknown", n = e.captureMode === "Payload";
  return /* @__PURE__ */ r.jsxs("article", { className: "wf-runtime-input", children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("span", { children: [
        /* @__PURE__ */ r.jsx("strong", { children: e.name }),
        /* @__PURE__ */ r.jsx("small", { children: t })
      ] }),
      /* @__PURE__ */ r.jsx("span", { className: "wf-runtime-capture-mode", children: Fj(e.captureMode) })
    ] }),
    n ? /* @__PURE__ */ r.jsx(Bj, { payload: e.payload }) : /* @__PURE__ */ r.jsx("p", { children: e.captureReason || "The runtime capture policy did not include this input value." }),
    e.isSensitive ? /* @__PURE__ */ r.jsx("p", { className: "wf-instance-note", children: "Marked sensitive by runtime evidence." }) : null
  ] });
}
function Bj({ payload: e }) {
  const t = Xj(e);
  return t.length <= 160 && !t.includes(`
`) ? /* @__PURE__ */ r.jsx("code", { className: "wf-runtime-input-value", children: t }) : /* @__PURE__ */ r.jsxs("details", { className: "wf-runtime-input-value-details", children: [
    /* @__PURE__ */ r.jsx("summary", { children: Kj(t) }),
    /* @__PURE__ */ r.jsx("pre", { children: t })
  ] });
}
function Fj(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2");
}
function Kj(e) {
  const t = e.split(`
`, 1)[0] || e;
  return t.length > 120 ? `${t.slice(0, 117)}...` : t;
}
function Xj(e) {
  if (e === null) return "null";
  if (e === void 0) return "undefined";
  if (typeof e == "string") return e;
  if (typeof e == "number" || typeof e == "boolean" || typeof e == "bigint") return String(e);
  try {
    return JSON.stringify(e, null, 2);
  } catch {
    return String(e);
  }
}
const Yj = [
  "runtime.faultStackTrace",
  "runtime.exceptionStackTrace",
  "runtime.stackTrace",
  "faultStackTrace",
  "exceptionStackTrace",
  "stackTrace"
];
function qj({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
  const [i, o] = K("");
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ r.jsx("h4", { children: "Incidents" }),
    e.length === 0 ? /* @__PURE__ */ r.jsx("p", { children: "No incidents recorded." }) : null,
    e.map((s) => /* @__PURE__ */ r.jsxs(
      "article",
      {
        className: "wf-instance-incident",
        "data-severity": s.severity.toLowerCase(),
        "data-selected": s.incidentId === t,
        children: [
          /* @__PURE__ */ r.jsxs(
            "button",
            {
              type: "button",
              className: "wf-instance-incident-summary",
              "aria-label": `Select incident ${s.failureType}`,
              onClick: () => n?.(s.incidentId),
              children: [
                /* @__PURE__ */ r.jsx("strong", { children: s.failureType }),
                /* @__PURE__ */ r.jsxs("span", { children: [
                  s.status,
                  " · ",
                  s.severity
                ] }),
                /* @__PURE__ */ r.jsx("p", { children: s.message })
              ]
            }
          ),
          /* @__PURE__ */ r.jsx(
            Et,
            {
              value: eS(s),
              ariaLabel: `Copy incident ${s.failureType}`,
              copiedLabel: "incident",
              onCopied: (a) => o(`Copied ${a}`),
              onCopyFailed: (a) => o(`Could not copy ${a}.`)
            }
          ),
          /* @__PURE__ */ r.jsx(Uj, { incident: s })
        ]
      },
      s.incidentId
    )),
    i ? /* @__PURE__ */ r.jsx("p", { className: "wf-copy-status", role: "status", children: i }) : null
  ] });
}
function Uj({ incident: e }) {
  const t = Zj(e);
  return t ? /* @__PURE__ */ r.jsxs("details", { className: "wf-incident-stacktrace", children: [
    /* @__PURE__ */ r.jsx("summary", { children: Jj(t) }),
    /* @__PURE__ */ r.jsx("pre", { children: t })
  ] }) : null;
}
function Zj(e) {
  const t = Gj(e.stackTrace, e.exceptionStackTrace);
  if (t) return t;
  for (const n of Yj) {
    const i = e.metadata?.[n];
    if (i && i.trim()) return i;
  }
  return null;
}
function Gj(...e) {
  return e.find((t) => t?.trim()) ?? null;
}
function Jj(e) {
  const n = (e.split(`
`).find((i) => i.trim()) ?? e).trim();
  return n.length > 120 ? `${n.slice(0, 117)}...` : n;
}
function Qj({ details: e, graphNodeIds: t, rootNodeId: n }) {
  if (!t) return null;
  const i = e.activities.filter((o) => {
    const s = tN(o);
    return s && s !== n && !t.has(s);
  });
  return i.length === 0 ? null : /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ r.jsx("h4", { children: "Executions outside canvas" }),
    /* @__PURE__ */ r.jsx("div", { className: "wf-instance-unmatched-list", children: i.map((o) => /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-unmatched", children: [
      /* @__PURE__ */ r.jsx("strong", { children: It(o.activityType) ?? o.activityType }),
      /* @__PURE__ */ r.jsx("small", { children: o.activityExecutionId })
    ] }, `activity-${o.activityExecutionId}`)) })
  ] });
}
function eS(e) {
  const t = [
    `Incident: ${e.failureType}`,
    `Incident ID: ${e.incidentId}`,
    `Workflow execution ID: ${e.workflowExecutionId}`,
    e.activityExecutionId ? `Activity execution ID: ${e.activityExecutionId}` : "",
    e.executableNodeId ? `Executable node ID: ${e.executableNodeId}` : "",
    `Status: ${e.status}`,
    `Severity: ${e.severity}`,
    `Blocking: ${e.isBlocking ? "Yes" : "No"}`,
    e.resolutionAction ? `Resolution action: ${e.resolutionAction}` : "",
    `Created: ${Re(e.createdAt)}`,
    e.resolvedAt ? `Resolved: ${Re(e.resolvedAt)}` : "",
    "",
    e.message
  ].filter(Boolean);
  return e.metadata && Object.keys(e.metadata).length > 0 && t.push("", "Metadata:", JSON.stringify(e.metadata, null, 2)), t.join(`
`);
}
function tS({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: i,
  workflowDesignerPanels: o,
  autosaveEnabledByDefault: s
}) {
  const [a, c] = K(Kc);
  Q(() => {
    const l = () => c(Kc());
    return window.addEventListener("popstate", l), () => window.removeEventListener("popstate", l);
  }, []);
  const u = (l) => {
    const d = l ? `/workflows/definitions?definition=${encodeURIComponent(l)}` : "/workflows/definitions";
    window.history.pushState({}, "", d), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return a ? /* @__PURE__ */ r.jsx(Cj, { context: e, definitionId: a, ai: t, propertyEditors: n, expressionEditors: i, workflowDesignerPanels: o, autosaveEnabledByDefault: s, onBack: () => u(null) }) : /* @__PURE__ */ r.jsx(No, { title: "Definitions", children: /* @__PURE__ */ r.jsx(Ij, { context: e, ai: t, onOpen: u }) });
}
function nS({ context: e, ai: t }) {
  const [n, i] = K(Xc);
  Q(() => {
    const s = () => i(Xc());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const o = se((s) => {
    const a = s?.trim() ?? "", c = new URL(window.location.href);
    a ? c.searchParams.set("definition", a) : c.searchParams.delete("definition"), i(a || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ r.jsx(No, { title: "Executables", children: /* @__PURE__ */ r.jsx(jN, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: o }) });
}
function iS({ context: e }) {
  return /* @__PURE__ */ r.jsx(No, { title: "Runs", children: /* @__PURE__ */ r.jsx($j, { context: e }) });
}
function oS({ context: e, ai: t }) {
  const n = rS();
  return /* @__PURE__ */ r.jsx(No, { title: "Run", children: /* @__PURE__ */ r.jsx(Pj, { context: e, ai: t, workflowExecutionId: n }) });
}
function No({ title: e, children: t }) {
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ r.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ r.jsxs("div", { children: [
      /* @__PURE__ */ r.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ r.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function Kc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Xc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function rS() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function uS(e) {
  $f(e.dialogs), e.featureAreas.add({
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
        component: () => /* @__PURE__ */ r.jsx(tS, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list(), autosaveEnabledByDefault: e.runtime.workflows?.autosaveEnabledByDefault ?? !0 })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ r.jsx(nS, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => /* @__PURE__ */ r.jsx(iS, { context: e.backend })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => /* @__PURE__ */ r.jsx(oS, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => /* @__PURE__ */ r.jsx(eg, { context: e.backend })
      }
    ]
  });
}
export {
  iN as isConnectEndOverExistingWorkflowNode,
  uS as register,
  oN as resolveConnectEndSource
};
