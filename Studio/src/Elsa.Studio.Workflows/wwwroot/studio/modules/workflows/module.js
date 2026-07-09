import Je, { useMemo as ue, useState as K, useEffect as Q, memo as Se, forwardRef as Wc, useRef as re, useCallback as ae, useContext as qn, createContext as Xo, useLayoutEffect as tf, lazy as nf, Suspense as rf, useReducer as of, useId as Bc } from "react";
import { Boxes as Yn, Zap as sf, Play as Jt, Terminal as af, ListTree as qo, GitBranch as Fc, ListChecks as cf, Save as Kc, EyeOff as eo, Shield as Ls, AlertTriangle as zi, SlidersHorizontal as Yo, Activity as Xc, Search as Qi, X as Uo, Check as ln, Plus as Qt, Trash2 as Dn, AlertCircle as vt, Wrench as lf, Copy as uf, Sparkles as ut, RotateCcw as Zo, ChevronDown as qc, ChevronRight as mt, GripVertical as Yc, Maximize2 as Tn, ChevronUp as df, Package as Uc, Undo2 as ff, Redo2 as pf, Network as hf, Download as gf, ChevronLeft as $n, Minimize2 as bo, Workflow as Zc, Code2 as yf } from "lucide-react";
import { useQuery as Go, useQueryClient as mf, useMutation as xf } from "@tanstack/react-query";
import { useTablistKeyboard as wf } from "@elsa-workflows/studio-ui";
function vf(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var to = { exports: {} }, mn = {};
var zs;
function bf() {
  if (zs) return mn;
  zs = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function n(i, r, s) {
    var a = null;
    if (s !== void 0 && (a = "" + s), r.key !== void 0 && (a = "" + r.key), "key" in r) {
      s = {};
      for (var c in r)
        c !== "key" && (s[c] = r[c]);
    } else s = r;
    return r = s.ref, {
      $$typeof: e,
      type: i,
      key: a,
      ref: r !== void 0 ? r : null,
      props: s
    };
  }
  return mn.Fragment = t, mn.jsx = n, mn.jsxs = n, mn;
}
var Vs;
function Nf() {
  return Vs || (Vs = 1, to.exports = bf()), to.exports;
}
var o = Nf();
let Gc;
function jf(e) {
  Gc = e;
}
function Os() {
  return Gc;
}
const Sf = "String", Cf = "singleline";
function Ef(e) {
  return e === "Single" || e === "Array" || e === "List" || e === "HashSet";
}
function Jo(e, t = "Single") {
  return { alias: (e ?? "").trim() || Sf, collectionKind: t };
}
function Qo(e) {
  const t = e.type ?? e.Type;
  if (Pn(t))
    return { alias: typeof t.alias == "string" ? t.alias : typeof t.typeName == "string" ? t.typeName : "", collectionKind: Ef(t.collectionKind) ? t.collectionKind : "Single" };
  const n = e.typeInformation ?? e.TypeInformation;
  return Pn(n) ? { alias: typeof n.typeName == "string" ? n.typeName : "", collectionKind: Hs(e) ? "Array" : "Single" } : { alias: typeof t == "string" ? t : "", collectionKind: Hs(e) ? "Array" : "Single" };
}
function Hs(e) {
  const t = e.isArray ?? e.IsArray;
  return t === !0 || t === "true";
}
function Ws(e) {
  return (e.alias ?? e.typeName ?? "").trim();
}
function er() {
  return typeof crypto < "u" && typeof crypto.randomUUID == "function" ? crypto.randomUUID() : `id-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
}
function kf(e, t) {
  const n = new Set(t);
  let i = 1, r = `${e}${i}`;
  for (; n.has(r); )
    i += 1, r = `${e}${i}`;
  return r;
}
function If(e) {
  return {
    referenceKey: er(),
    name: e.name,
    type: Jo(e.alias),
    storageDriverType: e.storageDriver?.trim() || null,
    default: null
  };
}
function Af(e, t) {
  return { ...e, ...t };
}
function _f(e) {
  return e === "" ? null : { value: e, expressionType: "Literal" };
}
function Df(e) {
  return e == null || e.value == null ? "" : typeof e.value == "object" ? JSON.stringify(e.value) : String(e.value);
}
function Tf(e) {
  return {
    referenceKey: er(),
    name: e.name,
    type: Jo(e.alias),
    displayName: e.name,
    description: "",
    category: "",
    uiHint: Cf,
    storageDriverType: e.storageDriver ?? null,
    isRequired: !1
  };
}
function $f(e, t) {
  return { ...e, ...t };
}
function Pf(e) {
  return {
    referenceKey: er(),
    name: e.name,
    type: Jo(e.alias),
    displayName: e.name,
    description: "",
    category: ""
  };
}
function Mf(e, t) {
  return { ...e, ...t };
}
function Rf(e) {
  const t = e.split(",")[0].trim();
  return (t.split(".").pop() ?? t).split("`")[0];
}
function Jc(e, t) {
  return e && !e.includes(",") && !e.includes(".") ? e : Rf(e || t);
}
function Lf(e, t) {
  return Jc(e, t).replace(/StorageDriver$/, "");
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
const zf = ["name", "Name"], es = ["name", "Name"], Vf = ["storageDriverType", "StorageDriverType"], Qc = ["referenceKey", "ReferenceKey"], Of = /* @__PURE__ */ new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);
function Un(e) {
  return el(e, Ff);
}
function tr(e) {
  return el(e, Kf);
}
function el(e, t) {
  if (!e) return e;
  const n = { ...e };
  return e.rootActivity && (n.rootActivity = No(e.rootActivity, t)), Array.isArray(e.variables) && (n.variables = ki(e.variables, jo)), Array.isArray(e.inputs) && (n.inputs = ki(e.inputs, jo)), Array.isArray(e.outputs) && (n.outputs = ki(e.outputs, (i) => tl(i, !1))), n;
}
function No(e, t) {
  const n = t(e), i = n.structure;
  if (!i || !nt(i.payload)) return n;
  let r = !1;
  const s = { ...i.payload };
  for (const [a, c] of Object.entries(i.payload))
    Fs(c) ? (s[a] = No(c, t), r = !0) : Array.isArray(c) && c.length > 0 && c.every(Fs) && (s[a] = c.map((u) => No(u, t)), r = !0);
  return Array.isArray(i.payload.variables) && i.payload.variables.length > 0 && (s.variables = ki(i.payload.variables, jo), r = !0), r ? { ...n, structure: { ...i, payload: s } } : n;
}
function ki(e, t) {
  return e.map((n) => nt(n) && !Array.isArray(n) ? t(n) : n);
}
function jo(e) {
  return tl(e, !0);
}
const Hf = [
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
function tl(e, t) {
  const n = Wf(e, Hf);
  return ct(e, Qc).trim() || (n.referenceKey = er()), n.type = Qo(e), t && (n.storageDriverType = Bf(e.storageDriverType ?? e.StorageDriverType)), n;
}
function Wf(e, t) {
  const n = new Set(t), i = {};
  for (const [r, s] of Object.entries(e))
    n.has(r) || (i[r] = s);
  return i;
}
function Bf(e) {
  if (typeof e == "string") return e.trim() ? e : null;
  if (nt(e)) {
    const t = typeof e.typeName == "string" ? e.typeName : "";
    if (!t) return null;
    const n = typeof e.namespace == "string" ? e.namespace : "";
    return n ? `${n}.${t}` : t;
  }
  return null;
}
function Ff(e) {
  const t = [], n = {};
  for (const [r, s] of Object.entries(e))
    Of.has(r) || (Uf(s) ? t.push({
      referenceKey: Xf(r),
      value: Yf(s.expression)
    }) : n[r] = s);
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
function Kf(e) {
  const t = Array.isArray(e.inputs) ? e.inputs : [], n = {};
  for (const i of t) {
    if (!nt(i) || typeof i.referenceKey != "string") continue;
    const r = nt(i.value) ? i.value : {};
    n[qf(i.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof r.expressionType == "string" ? r.expressionType : "Literal",
        value: r.value ?? ""
      }
    };
  }
  return { ...e, ...n, inputs: [] };
}
function Xf(e) {
  return e && e.charAt(0).toUpperCase() + e.slice(1);
}
function qf(e) {
  return e && e.charAt(0).toLowerCase() + e.slice(1);
}
function Yf(e) {
  const t = e.type || "Literal";
  return t === "Variable" && nt(e.value) ? { value: e.value, expressionType: t } : t === "Literal" && nt(e.value) ? { value: Bs(e.value), expressionType: "Object" } : { value: Bs(e.value), expressionType: t };
}
function Bs(e) {
  return e == null ? null : typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function Uf(e) {
  if (!nt(e) || Array.isArray(e)) return !1;
  const t = e.expression;
  return nt(t) && typeof t.type == "string";
}
function Fs(e) {
  return nt(e) && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function nt(e) {
  return typeof e == "object" && e !== null;
}
const Zn = "elsa.sequence.structure", un = "elsa.flowchart.structure";
function nl(e, t, n) {
  if (!e) return null;
  let i = e;
  for (const r of t) {
    const s = Gf(i, r.ownerNodeId, n);
    if (!s) return null;
    i = s;
  }
  return i;
}
function Zf(e, t, n = (r) => r.nodeId, i) {
  if (!e) return null;
  if (e.nodeId === t) return [];
  const r = (s, a) => {
    const c = Re(s, i);
    for (const u of c) {
      if (!u.activities.some((d) => d.nodeId === t)) continue;
      const l = a.at(-1);
      return l ? [...a.slice(0, -1), { ...l, slotId: u.id }] : u.id === c[0]?.id ? a : null;
    }
    for (const u of c)
      for (const l of u.activities) {
        const f = Re(l, i)[0]?.id ?? u.id, p = r(l, [...a, { ownerNodeId: l.nodeId, slotId: f, label: n(l) }]);
        if (p) return p;
      }
    return null;
  };
  return r(e, []);
}
function il(e, t, n) {
  const i = Re(e, n), r = t.at(-1);
  return r ? i.find((s) => s.id === r.slotId) ?? null : i[0] ?? null;
}
function Mn(e, t, n) {
  const i = nl(e, t, n);
  if (!i) return null;
  const r = il(i, t, n);
  return r ? { owner: i, slot: r } : null;
}
function rl(e, t, n) {
  for (const i of Re(e, n)) {
    const r = i.activities.find((s) => s.nodeId === t);
    if (r) return { slot: i, child: r };
  }
  return null;
}
function Gf(e, t, n) {
  return rl(e, t, n)?.child ?? null;
}
function Re(e, t) {
  const n = e.structure;
  if (!n || !n.payload || typeof n.payload != "object") return [];
  const i = n.payload, r = ts(ol(e, t));
  if (r?.kind === n.kind) return ep(n, r);
  const s = bp(n), a = bn(i.activities);
  return a ? [{
    id: `${n.kind}:activities`,
    label: Np(n),
    property: "activities",
    cardinality: "many",
    mode: s,
    activities: a
  }] : Object.entries(i).filter(([, c]) => bn(c) || Oi(c)).map(([c, u]) => ({
    id: `${n.kind}:${c}`,
    label: Sp(c),
    property: c,
    cardinality: bn(u) ? "many" : "single",
    mode: "generic",
    activities: bn(u) ?? (Oi(u) ? [u] : [])
  }));
}
function ts(e) {
  for (const t of e?.designFacets ?? []) {
    if (!it(t)) continue;
    const n = typeof t.kind == "string" ? t.kind : "", i = typeof t.schemaVersion == "string" ? t.schemaVersion : "";
    if (!n || !i || !it(t.payload)) continue;
    const r = Jf(t.payload);
    if (r) return { kind: n, schemaVersion: i, payload: r };
  }
  return null;
}
function Jf(e) {
  const t = e.mode;
  if (t !== "sequence" && t !== "flowchart" && t !== "generic" || typeof e.supportsScopedVariables != "boolean" || !Array.isArray(e.slots) || !it(e.initialPayload)) return null;
  const n = e.slots.map(Qf).filter((i) => i !== null);
  return {
    mode: t,
    supportsScopedVariables: e.supportsScopedVariables,
    slots: n,
    initialPayload: e.initialPayload
  };
}
function Qf(e) {
  if (!it(e)) return null;
  const t = typeof e.name == "string" ? e.name : "", n = typeof e.property == "string" ? e.property : "", i = typeof e.displayName == "string" ? e.displayName : "", r = e.cardinality;
  return !t || !n || !i || r !== "single" && r !== "many" ? null : {
    name: t,
    property: n,
    displayName: i,
    cardinality: r,
    collectionProperty: Ft(e.collectionProperty),
    childProperty: Ft(e.childProperty),
    labelProperty: Ft(e.labelProperty),
    slotNameTemplate: Ft(e.slotNameTemplate)
  };
}
function ep(e, t) {
  return t.payload.slots.flatMap((n) => {
    if (n.collectionProperty && n.childProperty) {
      const i = n.childProperty, r = e.payload[n.collectionProperty];
      return Array.isArray(r) ? r.flatMap((s, a) => {
        if (!it(s)) return [];
        const c = n.labelProperty ? Ft(s[n.labelProperty]) : void 0;
        return [Ks(e.kind, n, s[i], t.payload.mode, a, c)];
      }) : [];
    }
    return [Ks(e.kind, n, e.payload[n.property], t.payload.mode)];
  });
}
function Ks(e, t, n, i, r, s) {
  const a = r === void 0 ? "" : `:${t.collectionProperty}:${t.childProperty}:${r}`;
  return {
    id: `${e}:${t.property}${a}`,
    label: r === void 0 ? t.displayName : np(t, r, s),
    property: t.property,
    cardinality: t.cardinality,
    mode: i,
    activities: tp(n, t.cardinality),
    collectionProperty: t.collectionProperty,
    childProperty: t.childProperty,
    labelProperty: t.labelProperty,
    slotNameTemplate: t.slotNameTemplate,
    collectionIndex: r,
    collectionItemLabel: s
  };
}
function tp(e, t) {
  return t === "many" ? bn(e) ?? [] : Oi(e) ? [e] : [];
}
function np(e, t, n) {
  return e.slotNameTemplate ? e.slotNameTemplate.replaceAll("{name}", e.name).replaceAll("{displayName}", e.displayName).replaceAll("{label}", n ?? String(t + 1)).replaceAll("{index}", String(t + 1)) : n ? `${e.displayName}: ${n}` : `${e.displayName} ${t + 1}`;
}
function ol(e, t) {
  if (t)
    return t instanceof Map ? t.get(e.activityVersionId) : Array.isArray(t) ? t.find((n) => n.activityVersionId === e.activityVersionId) : t.activityVersionId === e.activityVersionId ? t : void 0;
}
function ip(e, t) {
  if (t.labelProperty && t.collectionItemLabel) {
    const n = e.map((i, r) => it(i) && Ft(i[t.labelProperty]) === t.collectionItemLabel ? r : -1).filter((i) => i >= 0);
    if (n.length === 1) return n[0];
  }
  return typeof t.collectionIndex == "number" && t.collectionIndex >= 0 && t.collectionIndex < e.length ? t.collectionIndex : -1;
}
function sl(e, t, n) {
  const i = new Map(t.map((a) => [a.activityVersionId, a])), r = new Map(n.map((a) => [a.nodeId, a])), s = e.slot.activities.map((a, c) => {
    const u = i.get(a.activityVersionId), l = r.get(a.nodeId) ?? jp(e.slot.mode, c);
    return dl(a, u, { x: l.x, y: l.y });
  });
  return {
    nodes: s,
    edges: e.slot.mode === "flowchart" ? pl(e.owner) : hp(e.slot, s)
  };
}
function al(e, t, n) {
  const i = t.find((s) => s.activityVersionId === e.activityVersionId), r = n.find((s) => s.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [dl(e, i, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function rp(e, t, n, i = null) {
  const r = new Map(t.map((c) => [c.activityExecutionId, c])), s = Ys(t, (c) => c.authoredActivityId || c.executableNodeId), a = Ys(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? r.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const u = s.get(c.id) ?? [], l = a.get(c.id) ?? [];
    if (u.length === 0 && l.length === 0) return c;
    const d = gl(u), f = i === c.id || u.some((h) => h.activityExecutionId === i) || l.some((h) => h.incidentId === i), p = {
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
function cl(e, t) {
  if (e?.structure?.kind === un || up(t)) return "flowchart";
  if (e?.structure?.kind === Zn || dp(t)) return "sequence";
  if (e) {
    const n = Re(e, t)[0];
    if (n) return n.mode === "flowchart" ? "flowchart" : "sequence";
  }
  return "unsupported";
}
function Xs(e, t, n, i) {
  return ns(e, t, i, (r) => {
    const s = il(r, t, i);
    return s ? Gn(r, s, n) : r;
  });
}
function op(e, t, n, i) {
  return t.length === 0 ? n : ns(e, t, i, () => n);
}
function ns(e, t, n, i) {
  if (t.length === 0) return i(e);
  const [r, ...s] = t, a = rl(e, r.ownerNodeId, n);
  if (!a) return e;
  const c = ns(a.child, s, n, i);
  if (c === a.child) return e;
  const u = a.slot.activities.map((l) => l.nodeId === r.ownerNodeId ? c : l);
  return Gn(e, a.slot, u);
}
function ll(e, t, n, i) {
  if (e.nodeId === t) return n(e);
  const r = Re(e, i);
  if (r.length === 0) return e;
  let s = !1, a = e;
  for (const c of r) {
    const u = c.activities.map((l) => {
      const d = ll(l, t, n, i);
      return d !== l && (s = !0), d;
    });
    s && (a = Gn(a, c, u));
  }
  return s ? a : e;
}
function Gn(e, t, n) {
  if (!e.structure) return e;
  const i = t.cardinality === "single" ? n[0] ?? null : n;
  if (t.collectionProperty && t.childProperty) {
    const r = e.structure.payload[t.collectionProperty];
    if (!Array.isArray(r)) return e;
    const s = ip(r, t);
    if (s < 0) return e;
    const a = r[s];
    if (!it(a)) return e;
    const c = [...r];
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
function sp(e, t, n, i = []) {
  const r = new Map(e.slot.activities.map((a) => [a.nodeId, a]));
  for (const a of i)
    r.set(a.nodeId, a);
  const s = t.map((a) => r.get(a.id)).filter((a) => !!a);
  return e.slot.mode === "sequence" && s.sort((a, c) => {
    const u = t.find((d) => d.id === a.nodeId), l = t.find((d) => d.id === c.nodeId);
    return (u?.position.x ?? 0) - (l?.position.x ?? 0);
  }), Gn(e.owner, e.slot, s);
}
function ap(e, t) {
  return {
    ...e,
    structure: pp(e.structure, t)
  };
}
function cp(e, t) {
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
function So(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: fl(e)
  };
}
function ul(e, t) {
  if (!e) return null;
  const n = ol(e, t), i = e.structure ?? (n ? fl(n) : null);
  let r = i === e.structure ? e : { ...e, structure: i };
  const s = Re(r, t);
  for (const a of s) {
    const c = a.activities.map((u) => ul(u, t) ?? u);
    c.some((u, l) => u !== a.activities[l]) && (r = Gn(r, a, c));
  }
  return r;
}
function Ee(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? fp(t) : n;
}
function dl(e, t, n, i = {}) {
  return {
    id: e.nodeId,
    type: "workflowActivity",
    position: n,
    connectable: i.connectable,
    deletable: i.deletable,
    draggable: i.draggable,
    data: {
      label: t ? Ee(t) : e.activityVersionId,
      activityVersionId: e.activityVersionId,
      activityTypeKey: t?.activityTypeKey,
      category: t?.category,
      executionType: t?.executionType,
      icon: dn(t),
      childSlots: Re(e, t),
      acceptsInbound: gp(e, t),
      sourcePorts: i.suppressFlowPorts ? [] : hl(e, t),
      suppressFlowPorts: i.suppressFlowPorts
    }
  };
}
function dn(e) {
  if (!e) return "activity";
  const t = lp(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), i = Ee(e).toLowerCase(), r = e.category?.toLowerCase() ?? "", s = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || i === "flowchart" ? "flowchart" : n.endsWith(".sequence") || i === "sequence" ? "sequence" : n.includes("writeline") || i.includes("write line") ? "terminal" : r.includes("runtime") ? "runtime" : s === "trigger" ? "trigger" : "activity";
}
function lp(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function up(e) {
  return !!e && (Ee(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function dp(e) {
  return !!e && (Ee(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function fp(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function fl(e) {
  const t = ts(e);
  return t ? {
    kind: t.kind,
    schemaVersion: t.schemaVersion,
    payload: wp(t.payload.initialPayload)
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
function pp(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], i = /* @__PURE__ */ new Map();
  for (const r of n) {
    if (!it(r)) continue;
    const s = r.id;
    typeof s == "string" && i.set(s, r);
  }
  return {
    ...e,
    payload: {
      ...e.payload,
      connections: t.map((r) => {
        const s = i.get(r.id) ?? {}, a = r.data?.vertices, { vertices: c, ...u } = s;
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
function hp(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, i) => ({
    id: `sequence-${n.id}-${t[i + 1].id}`,
    source: n.id,
    target: t[i + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function pl(e) {
  if (e.structure?.kind !== un) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, i) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, s = n.target;
    if (!r?.nodeId || !s?.nodeId) return null;
    const a = Array.isArray(n.vertices) ? n.vertices.filter(vp) : [];
    return {
      id: typeof n.id == "string" ? String(n.id) : `flow-${i}-${r.nodeId}-${s.nodeId}`,
      source: r.nodeId,
      target: s.nodeId,
      sourceHandle: r.port,
      targetHandle: s.port && s.port !== "Done" ? s.port : void 0,
      type: "workflow",
      label: r.port && r.port !== "Done" ? r.port : void 0,
      data: a.length ? { vertices: a } : void 0
    };
  }).filter((n) => n !== null) : [];
}
function hl(e, t) {
  const n = qs(e.cases);
  if (mp(e, t) && n.length > 0)
    return [...n.map((s) => ({ name: s, displayName: s })), { name: "Default", displayName: "Default" }];
  const i = [
    ...Ii(t?.designFacets),
    ...Ii(t?.ports),
    ...Ii(t?.outputs)
  ];
  if (i.length > 0) return xp(i);
  const r = qs(e.outcomes);
  return r.length > 0 ? r.map((s) => ({ name: s, displayName: s })) : [{ name: "Done", displayName: "Done" }];
}
function gp(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function Vi(e, t, n, i) {
  const r = n ?? "Done";
  return {
    id: `flow-${e}-${t}-${r}-${crypto.randomUUID().slice(0, 8)}`,
    source: e,
    target: t,
    sourceHandle: r,
    targetHandle: i ?? void 0,
    type: "workflow",
    label: r !== "Done" ? r : void 0
  };
}
function yp(e, t, n) {
  const i = Vi(t.source, n, t.sourceHandle ?? "Done", void 0), r = Vi(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((s) => s.id !== t.id).concat(i, r);
}
function bn(e) {
  return Array.isArray(e) ? e.filter(Oi) : null;
}
function mp(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, i = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || i === "FlowSwitch";
}
function Ii(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!it(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...Ii(n.ports));
      continue;
    }
    const i = typeof n.type == "string" ? n.type : typeof n.portType == "string" ? n.portType : "", r = n.isBrowsable !== !1 && n.browsable !== !1, s = typeof n.name == "string" ? n.name : typeof n.id == "string" ? n.id : "";
    if (r && i.toLowerCase() === "flow" && s) {
      const a = typeof n.displayName == "string" ? n.displayName : s;
      t.push({ name: s, displayName: a });
    }
  }
  return t;
}
function xp(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function qs(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function Ft(e) {
  return typeof e == "string" && e.length > 0 ? e : void 0;
}
function wp(e) {
  return JSON.parse(JSON.stringify(e));
}
function Ys(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e) {
    const r = t(i);
    r && n.set(r, [...n.get(r) ?? [], i]);
  }
  return n;
}
function gl(e) {
  return [...e].sort((t, n) => Us(n).localeCompare(Us(t)))[0];
}
function Us(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function vp(e) {
  return it(e) && typeof e.x == "number" && typeof e.y == "number";
}
function it(e) {
  return typeof e == "object" && e !== null;
}
function Oi(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function bp(e) {
  return e.kind === Zn ? "sequence" : e.kind === un ? "flowchart" : "generic";
}
function Np(e) {
  return e.kind === Zn || e.kind === un, "Activities";
}
function jp(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function Sp(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Hi = "workflow", Cp = /* @__PURE__ */ new Set([Zn, un]);
function Ep(e, t) {
  const n = e?.structure?.kind;
  if (!n) return !1;
  if (Cp.has(n)) return !0;
  const i = t?.activityVersionId === e?.activityVersionId ? ts(t) : null;
  return i?.kind === n && i.payload.supportsScopedVariables;
}
function yl(e) {
  const t = e?.structure?.payload?.variables;
  return Array.isArray(t) ? t.filter(Pn) : [];
}
function kp(e, t) {
  return e.structure ? {
    ...e,
    structure: {
      ...e.structure,
      payload: { ...e.structure.payload, variables: t }
    }
  } : e;
}
function Ip(e, t) {
  return {
    referenceKey: e,
    declaringScopeId: t && t !== Hi ? t : Hi
  };
}
function ml(e) {
  if (e && typeof e == "object") {
    const t = e;
    return typeof t.referenceKey == "string" && t.referenceKey ? { referenceKey: t.referenceKey, declaringScopeId: typeof t.declaringScopeId == "string" ? t.declaringScopeId : null } : null;
  }
  if (typeof e == "string") {
    const t = e.trim();
    if (t.startsWith("{"))
      try {
        return ml(JSON.parse(t));
      } catch {
      }
    return t ? { referenceKey: t, declaringScopeId: null } : null;
  }
  return null;
}
function Ap(e, t) {
  if (!e) return "";
  const n = [`workflow:${Zs(e.variables)}`], i = (r) => {
    const s = Re(r, t), a = s.flatMap((c) => c.activities.map((u) => u.nodeId));
    n.push(`${r.nodeId}:${Zs(yl(r))}>${a.join(",")}`), s.forEach((c) => c.activities.forEach(i));
  };
  return e.rootActivity && i(e.rootActivity), n.join(";");
}
function Zs(e) {
  return (e ?? []).map((t) => `${t.referenceKey}=${t.name}`).join(",");
}
function _p(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e ?? [])
    i.scopeId === t && n.set(i.referenceKey, `Shadows "${i.name}" declared in an outer scope.`);
  return n;
}
const je = "/_elsa/workflow-management", Dp = "/publishing", Ut = {
  activities: ["workflows", "activities"],
  activityAvailabilitySettings: ["workflows", "activity-availability", "settings"],
  activityAvailabilityDiagnostics: ["workflows", "activity-availability", "diagnostics"]
};
function Tp(e) {
  return Go({
    queryKey: Ut.activities,
    queryFn: () => nr(e),
    staleTime: 6e4
  });
}
function $p(e) {
  return Go({
    queryKey: Ut.activityAvailabilitySettings,
    queryFn: () => eh(e)
  });
}
function Pp(e) {
  return Go({
    queryKey: Ut.activityAvailabilityDiagnostics,
    queryFn: () => vl(e)
  });
}
function Mp(e) {
  const t = mf();
  return xf({
    mutationFn: (n) => th(e, n),
    onSuccess: (n) => {
      t.setQueryData(Ut.activityAvailabilitySettings, n), t.invalidateQueries({ queryKey: Ut.activityAvailabilityDiagnostics }), t.invalidateQueries({ queryKey: Ut.activities });
    }
  });
}
async function Rp(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), i = t.search.trim();
  return i && n.set("search", i), e.http.getJson(`${je}/definitions?${n.toString()}`);
}
async function Lp(e, t) {
  const n = await e.http.getJson(`${je}/definitions/${encodeURIComponent(t)}`);
  return n.draft ? { ...n, draft: { ...n.draft, state: tr(n.draft.state) } } : n;
}
async function zp(e, t, n) {
  const i = await e.http.postJson(
    `${je}/design/scoped-variables/analyze`,
    { state: Un(t), nodeId: n }
  );
  return {
    visibleVariables: Array.isArray(i?.visibleVariables) ? i.visibleVariables : [],
    shadowingWarnings: Array.isArray(i?.shadowingWarnings) ? i.shadowingWarnings : []
  };
}
const no = (e) => ({ visibleVariables: [], shadowingWarnings: [], status: e });
function Vp(e, t, n, i) {
  const r = ue(() => Ap(t, i), [i, t]), [s, a] = K(() => no("loading"));
  return Q(() => {
    if (!t) {
      a(no("unavailable"));
      return;
    }
    let c = !1;
    return a((u) => ({ ...u, status: "loading" })), zp(e, t, n).then(
      (u) => {
        c || a({ ...u, status: "ready" });
      },
      () => {
        c || a(no("unavailable"));
      }
    ), () => {
      c = !0;
    };
  }, [e, n, r]), s;
}
async function Op(e, t) {
  const n = await e.http.getJson(`${je}/versions/${encodeURIComponent(t)}`);
  return { ...n, state: tr(n.state) };
}
async function Hp(e, t) {
  return e.http.postJson(`${je}/definitions`, t);
}
async function Wp(e, t) {
  await e.http.deleteJson(`${je}/definitions/${encodeURIComponent(t)}`);
}
async function Bp(e, t) {
  await e.http.postJson(`${je}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function Fp(e, t) {
  await e.http.deleteJson(`${je}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function Kp(e, t, n) {
  return e.http.requestJson(
    `${je}/definitions/${encodeURIComponent(t)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(n)
    }
  );
}
async function Xp(e, t) {
  const n = await e.http.putJson(
    `${je}/drafts/${encodeURIComponent(t.id)}`,
    { state: Un(t.state), layout: t.layout }
  );
  return { ...n, state: tr(n.state) };
}
async function qp(e, t) {
  return e.http.postJson(`${je}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function Yp(e, t) {
  return e.http.postJson(`${je}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function Up(e, t) {
  const n = { ...t, state: Un(t.state) };
  try {
    return await e.http.postJson(`${Dp}/workflows/drafts/test-runs`, n);
  } catch (i) {
    const r = ch(i);
    if (r) return r;
    throw i;
  }
}
async function xl(e, t) {
  return e.http.postJson(`${je}/executables/${encodeURIComponent(t)}/run`, {});
}
async function wl(e) {
  const t = [`${je}/executables`, "/_demo/workflows/executables"], n = [];
  for (const i of t)
    try {
      const r = await e.http.getJson(i);
      return Zp(r);
    } catch (r) {
      n.push(r);
    }
  if (n.length > 0 && n.every(Gs)) return [];
  throw n.find((i) => !Gs(i)) ?? n[n.length - 1] ?? new Error("Workflow executables could not be loaded.");
}
function Zp(e) {
  return Array.isArray(e) ? e : e.executables ?? [];
}
function Gs(e) {
  if (!(e instanceof Error)) return !1;
  const t = e.message.toLowerCase();
  return /\b404\b/.test(t) || t.includes("not found");
}
async function Gp(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.runKind && n.set("runKind", t.runKind), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const i = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${i ? `?${i}` : ""}`);
}
async function Jp(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function Qp(e, t, n) {
  return e.http.getJson(
    `/runtime/workflows/instances/${encodeURIComponent(t)}/activity-executions/${encodeURIComponent(n)}`
  );
}
async function nr(e) {
  return e.http.getJson(`${je}/activities`);
}
async function eh(e) {
  return e.http.getJson(`${je}/activities/availability/settings`);
}
async function th(e, t) {
  return e.http.putJson(`${je}/activities/availability/settings`, t);
}
async function vl(e) {
  return e.http.getJson(`${je}/activities/availability/diagnostics`);
}
async function nh(e) {
  const t = await ir(e, [
    `${je}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? Js(t) : Js(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function ih(e) {
  const t = await ir(e, [
    `${je}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : Ai;
}
async function rh(e) {
  const t = await ir(e, [
    `${je}/descriptors/variables`,
    "/descriptors/variables"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((i) => oh(i));
}
function oh(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = typeof t.alias == "string" && t.alias.length > 0, i = typeof t.typeName == "string" && t.typeName.length > 0;
  return n || i;
}
async function sh(e) {
  const t = await ir(e, [
    `${je}/descriptors/storage-drivers`,
    "/descriptors/storage-drivers"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((i) => ah(i));
}
function ah(e) {
  return !!e && typeof e == "object" && typeof e.typeName == "string" && e.typeName.length > 0;
}
async function ir(e, t) {
  let n;
  for (const i of t)
    try {
      return await e.http.getJson(i);
    } catch (r) {
      n = r;
    }
  throw n;
}
function Js(e) {
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
function ch(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = Qs(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return Qs(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function Qs(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const Ai = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function bl(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e) {
    const r = t(i)?.trim() || "Uncategorized", s = n.get(r);
    s ? s.push(i) : n.set(r, [i]);
  }
  return [...n.entries()].map(([i, r]) => ({ category: i, items: r })).sort((i, r) => i.category.localeCompare(r.category));
}
const lh = [
  "Available",
  "BlockedByHostBaseline",
  "HiddenByManagementSettings",
  "RemovedFromCatalog",
  "UnresolvedReference"
], uh = {
  Available: "Available",
  BlockedByHostBaseline: "Host blocked",
  HiddenByManagementSettings: "Management hidden",
  RemovedFromCatalog: "Removed",
  UnresolvedReference: "Unresolved"
}, Nl = {
  Catalog: "Catalog",
  HostBaseline: "Host baseline",
  ManagementSettings: "Management settings"
}, dh = Object.keys(Nl);
function fh(e) {
  const t = typeof e == "number" ? dh[e] : e;
  return t && Nl[t] || t?.toString() || "";
}
function kt(e) {
  return typeof e == "string" ? e : typeof e == "number" ? lh[e] ?? "Available" : "Available";
}
function Rn(e) {
  const t = kt(e);
  return uh[t] ?? t;
}
function jl(e) {
  return kt(e).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Sl(e) {
  return kt(e) !== "Available";
}
function Cl(e) {
  return kt(e.state) === "BlockedByHostBaseline";
}
function ph(e) {
  return e === "Only" || e === 1 ? "Only" : "AllExcept";
}
function hh(e) {
  return e === "Only" ? 1 : 0;
}
function Co(e) {
  const t = e?.rules;
  return {
    mode: ph(e?.mode),
    activityTypes: t?.activityTypes ?? [],
    sets: t?.sets ?? []
  };
}
function gh(e) {
  return e.referenceKind === 0 || e.referenceKind === "ActivityType";
}
function Nn(e) {
  return e.activityTypeKey ?? e.activityDefinitionId ?? "";
}
function yh(e) {
  return [...e?.items ?? []].filter(gh).filter((t) => t.activityTypeKey && t.activityDefinitionId).sort((t, n) => Ln(t).localeCompare(Ln(n)));
}
function mh(e) {
  return [...e?.items ?? []].filter((t) => {
    const n = kt(t.state);
    return n === "RemovedFromCatalog" || n === "UnresolvedReference";
  }).sort((t, n) => (t.referenceName ?? "").localeCompare(n.referenceName ?? ""));
}
function xh(e) {
  return bl(e, (t) => t.category).map((t) => ({ category: t.category, entries: t.items }));
}
function wh(e, t, n) {
  const i = e[t].includes(n);
  return e.mode === "Only" ? i : !i;
}
function io(e, t, n, i) {
  const r = e.mode === "Only" ? i : !i;
  return e[t].includes(n) === r ? e : { ...e, [t]: Nh(e[t], n) };
}
function vh(e, t, n) {
  const i = new Set(n.activityTypes), r = new Set(n.sets), s = /* @__PURE__ */ new Map();
  for (const c of t)
    if (r.has(c.name))
      for (const u of c.activityTypeKeys ?? [])
        s.has(u) || s.set(u, c.name);
  const a = /* @__PURE__ */ new Map();
  for (const c of e) {
    const u = Nn(c);
    if (Cl(c)) {
      a.set(u, { enabled: !1, lockedBy: "host-baseline" });
      continue;
    }
    const l = s.get(u), d = i.has(u) || l !== void 0, f = n.mode === "Only" ? d : !d;
    a.set(u, l ? { enabled: f, lockedBy: "set-rule", governingSet: l } : { enabled: f, lockedBy: null });
  }
  return a;
}
function bh(e, t) {
  const n = Co(t), i = (r) => [...r].sort((s, a) => s.localeCompare(a)).join(`
`);
  return e.mode !== n.mode || i(e.activityTypes) !== i(n.activityTypes) || i(e.sets) !== i(n.sets);
}
function Nh(e, t) {
  return e.includes(t) ? e.filter((n) => n !== t) : [...e, t].sort((n, i) => n.localeCompare(i));
}
function Ln(e) {
  const t = e?.displayName?.trim();
  if (t && t.toLowerCase() !== "activity") return t;
  const n = Sh(e?.activityTypeKey);
  return jh(n) || t || e?.activityTypeKey || "Activity";
}
function ea(e) {
  const t = El(e?.activityTypeKey);
  if (t.length === 0) return "";
  const n = t[t.length - 1], i = t[t.length - 2];
  return n === "Activity" && i ? `${i}.${n}` : n;
}
function ta(e) {
  const t = e.description?.trim();
  if (t) return t;
  const n = e.reason?.trim();
  return !n || !Sl(e.state) ? "" : n;
}
function jh(e) {
  return e.replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/\bHttp\b/g, "HTTP").replace(/\bJson\b/g, "JSON").replace(/\bJava Script\b/g, "JavaScript").replace(/\bUrl\b/g, "URL").replace(/\bXml\b/g, "XML").trim();
}
function Sh(e) {
  const t = El(e), n = t[t.length - 1] ?? "";
  return n === "Activity" && t.length > 1 ? t[t.length - 2] : n;
}
function El(e) {
  return e?.split(".").filter(Boolean) ?? [];
}
function na(e) {
  return (e ?? []).flatMap((t) => {
    if (!Pn(t)) return [];
    const n = (ct(t, ["displayName", "DisplayName"]) || ct(t, es)).trim();
    if (!n) return [];
    const i = (Qo(t).alias || ct(t, ["typeName", "TypeName", "alias", "Alias"])).trim();
    return [{ name: n, typeName: i, description: ct(t, ["description", "Description"]).trim() }];
  });
}
function Ch(e, t) {
  const n = new Set(e.filter((i) => !!i));
  return (t?.items ?? []).find((i) => Sl(i.state) ? [i.activityDefinitionId, i.activityTypeKey, i.referenceName].some((r) => r && n.has(r)) : !1) ?? null;
}
const Eh = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function kl(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function is(e) {
  return kl(e.name);
}
function kh(e, t) {
  const n = is(t), i = e[n];
  return t.isWrapped === !1 ? i ?? t.defaultValue ?? "" : Al(i, t);
}
function Il(e, t) {
  return Al(e[is(t)], t);
}
function Ih(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function Ah(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function ia(e, t, n) {
  return {
    ...e,
    [is(t)]: n
  };
}
function _h(e, t) {
  return t.isWrapped === !1 ? kh(e, t) : Il(e, t).expression.value;
}
function Al(e, t) {
  return zh(e) ? {
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
const Dh = /* @__PURE__ */ new Set([
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
function Th(e) {
  const t = e?.trim();
  if (!t) return null;
  const n = /^([\w.+]+)\[\]/.exec(t);
  if (n) return { elementTypeName: n[1] };
  const i = t.indexOf("`");
  if (i < 0) return null;
  const r = t.slice(0, i), s = (r.split(".").pop() ?? r).toLowerCase();
  return Dh.has(s) ? { elementTypeName: $h(t.slice(i)) } : null;
}
function $h(e) {
  const t = /\[\[([\w.+]+)/.exec(e);
  if (t) return t[1];
  const n = /\[([\w.+]+)/.exec(e);
  return n ? n[1] : null;
}
function Ph(e) {
  const t = e.uiHint?.toLowerCase();
  return t === "json" || t === "code" ? !0 : e.uiSpecifications?.repeater === !1;
}
function Mh(e) {
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
function Rh(e) {
  const t = (e ?? "").toLowerCase();
  return t === "system.boolean" || t === "boolean" || t === "bool" ? !1 : "";
}
function Lh(e, t) {
  return {
    ...e,
    typeName: t ?? "System.String",
    isWrapped: !1,
    // The description documents the collection as a whole; repeating it on every row is noise.
    description: null
  };
}
function ro(e, t, n) {
  if (t === n || t < 0 || n < 0 || t >= e.length || n >= e.length) return e;
  const i = [...e], [r] = i.splice(t, 1);
  return i.splice(n, 0, r), i;
}
function zh(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
function rs(e) {
  return os(e?.trim() ?? "") || e;
}
function os(e) {
  if (!e) return "";
  const t = Vh(e);
  if (!t) return "";
  const n = /^(.*)((?:\[\])+)$/.exec(t);
  if (n) return `${os(n[1])}${n[2]}`;
  const i = t.indexOf("`");
  if (i >= 0) {
    const r = ra(t.slice(0, i)), s = Oh(t.slice(i));
    return s.length > 0 ? `${r}<${s.join(", ")}>` : r;
  }
  return ra(t);
}
function Vh(e) {
  let t = 0;
  for (let n = 0; n < e.length; n++) {
    const i = e[n];
    if (i === "[") t++;
    else if (i === "]") t--;
    else if (i === "," && t === 0) return e.slice(0, n).trim();
  }
  return e.trim();
}
function ra(e) {
  const t = e.split(".").filter(Boolean).at(-1) ?? e;
  return t.split("+").filter(Boolean).at(-1) ?? t;
}
function Oh(e) {
  const t = e.indexOf("[");
  if (t < 0) return [];
  const n = oa(e, t);
  return n == null ? [] : Hh(n).map((i) => {
    const r = i.trim(), s = r.startsWith("[") ? oa(r, 0) ?? r : r;
    return os(s);
  }).filter(Boolean);
}
function oa(e, t) {
  let n = 0;
  for (let i = t; i < e.length; i++)
    if (e[i] === "[") n++;
    else if (e[i] === "]" && --n === 0) return e.slice(t + 1, i);
  return null;
}
function Hh(e) {
  const t = [];
  let n = 0, i = 0;
  for (let r = 0; r < e.length; r++) {
    const s = e[r];
    s === "[" ? n++ : s === "]" ? n-- : s === "," && n === 0 && (t.push(e.slice(i, r)), i = r + 1);
  }
  return t.push(e.slice(i)), t.map((r) => r.trim()).filter(Boolean);
}
function Me(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function ss(e, t) {
  if (!e || !t) return "";
  const n = Date.parse(e), i = Date.parse(t);
  if (Number.isNaN(n) || Number.isNaN(i) || i < n) return "";
  const r = Math.round((i - n) / 1e3);
  if (r < 60) return `${r}s`;
  const s = Math.floor(r / 60), a = r % 60;
  if (s < 60) return a ? `${s}m ${a}s` : `${s}m`;
  const c = Math.floor(s / 60), u = s % 60;
  return u ? `${c}h ${u}m` : `${c}h`;
}
function It(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function rr(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ o.jsx(Fc, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ o.jsx(qo, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ o.jsx(af, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ o.jsx(Jt, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ o.jsx(sf, { size: 15 });
    default:
      return /* @__PURE__ */ o.jsx(Yn, { size: 15 });
  }
}
function sa(e) {
  return `${e.activityTypeKey ?? ""}|${e.activityDefinitionId ?? ""}`;
}
function Wh(e, t) {
  const n = (a) => (a ?? "").split(".").map((c) => Number.parseInt(c, 10) || 0), i = n(e), r = n(t), s = Math.max(i.length, r.length);
  for (let a = 0; a < s; a++) {
    const c = (i[a] ?? 0) - (r[a] ?? 0);
    if (c !== 0) return c;
  }
  return 0;
}
function Bh(e) {
  if (e.lockedBy === "host-baseline") return "Blocked by the host baseline";
  if (e.lockedBy === "set-rule") return `Controlled by the "${e.governingSet}" set rule`;
}
function _l({ icon: e }) {
  return /* @__PURE__ */ o.jsx("span", { className: "wf-activity-icon", "data-icon": e, "aria-hidden": "true", children: rr(e) });
}
function Fh({ context: e }) {
  const t = $p(e), n = Pp(e), i = Tp(e), r = Mp(e), s = t.data ?? null, a = n.data ?? null, c = t.isLoading || n.isLoading, u = r.isPending, [l, d] = K(() => Co(s)), [f, p] = K(""), [h, y] = K(!1), [w, x] = K(null), [m, b] = K(null);
  Q(() => {
    d(Co(s));
  }, [s]);
  const g = ue(() => yh(a), [a]), v = ue(() => mh(a), [a]), j = ue(() => a?.sets ?? [], [a]), N = ue(
    () => vh(g, j, l),
    [g, j, l]
  ), S = (V) => N.get(Nn(V)) ?? { enabled: !0, lockedBy: null }, k = ue(() => {
    const V = /* @__PURE__ */ new Map();
    for (const q of i.data?.activities ?? []) {
      if (!q.activityTypeKey) continue;
      const G = V.get(q.activityTypeKey);
      (!G || Wh(q.version, G.version) > 0) && V.set(q.activityTypeKey, q);
    }
    return V;
  }, [i.data]), _ = (V) => k.get(V.activityTypeKey ?? ""), M = ue(() => {
    const V = f.trim().toLowerCase(), q = h ? g.filter((G) => !(N.get(Nn(G))?.enabled ?? !0)) : g;
    return V ? q.filter((G) => [
      Ln(G),
      ea(G),
      ta(G),
      G.activityTypeKey,
      G.category
    ].some((Z) => (Z ?? "").toLowerCase().includes(V))) : q;
  }, [g, f, h, N]), I = ue(() => xh(M), [M]), $ = m ? g.find((V) => sa(V) === m) ?? null : null, z = g.filter(Cl).length, C = g.filter((V) => kt(V.state) === "HiddenByManagementSettings").length, A = ue(() => bh(l, s), [l, s]), E = r.error ?? t.error ?? n.error, D = E instanceof Error ? E.message : E ? "Activity availability could not be loaded." : null, P = (V) => {
    x(null), d(V);
  }, T = (V) => P((q) => ({ ...q, mode: V })), B = (V, q) => P((G) => io(G, "activityTypes", V, q)), W = (V, q) => P((G) => V.reduce((Z, R) => io(Z, "activityTypes", R, q), G)), H = (V, q) => P((G) => io(G, "sets", V, q)), Y = () => {
    x(null), r.mutate(
      {
        scope: s?.scope ?? "host-default",
        mode: hh(l.mode),
        rules: { activityTypes: l.activityTypes, sets: l.sets }
      },
      { onSuccess: () => x("Activity availability saved.") }
    );
  };
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-page availability-page", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-page-header", children: [
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("div", { className: "wf-kicker", children: "Workflows" }),
        /* @__PURE__ */ o.jsxs("h2", { children: [
          /* @__PURE__ */ o.jsx(cf, { size: 18 }),
          " Activity availability"
        ] }),
        /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "Control which activities can be added to new workflows. Host baseline rules always take precedence." })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { className: "wf-actions", children: [
        A && !u && /* @__PURE__ */ o.jsx("span", { className: "wf-chip availability-dirty", children: "Unsaved changes" }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", className: "availability-save", onClick: Y, disabled: c || u || !A, children: [
          /* @__PURE__ */ o.jsx(Kc, { size: 15 }),
          u ? "Saving…" : "Save"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "availability-body", children: [
      D && /* @__PURE__ */ o.jsx("div", { className: "availability-banner availability-banner-error", children: D }),
      w && !D && /* @__PURE__ */ o.jsx("div", { className: "availability-banner availability-banner-success", children: w }),
      /* @__PURE__ */ o.jsxs("div", { className: "availability-mode", role: "group", "aria-label": "Activity availability mode", children: [
        /* @__PURE__ */ o.jsxs("button", { type: "button", className: l.mode === "AllExcept" ? "active" : "", onClick: () => T("AllExcept"), disabled: c || u, children: [
          /* @__PURE__ */ o.jsx(eo, { size: 15 }),
          /* @__PURE__ */ o.jsxs("span", { children: [
            /* @__PURE__ */ o.jsx("strong", { children: "All except" }),
            /* @__PURE__ */ o.jsx("em", { children: "Activities are available unless you turn them off" })
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", className: l.mode === "Only" ? "active" : "", onClick: () => T("Only"), disabled: c || u, children: [
          /* @__PURE__ */ o.jsx(Ls, { size: 15 }),
          /* @__PURE__ */ o.jsxs("span", { children: [
            /* @__PURE__ */ o.jsx("strong", { children: "Only" }),
            /* @__PURE__ */ o.jsx("em", { children: "Activities are hidden unless you turn them on" })
          ] })
        ] })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { className: "availability-counts", children: [
        /* @__PURE__ */ o.jsxs("span", { children: [
          /* @__PURE__ */ o.jsx(Ls, { size: 14 }),
          " ",
          z,
          " host blocked"
        ] }),
        /* @__PURE__ */ o.jsxs("span", { children: [
          /* @__PURE__ */ o.jsx(eo, { size: 14 }),
          " ",
          C,
          " management hidden"
        ] }),
        /* @__PURE__ */ o.jsxs("span", { children: [
          /* @__PURE__ */ o.jsx(zi, { size: 14 }),
          " ",
          v.length,
          " unresolved"
        ] })
      ] }),
      j.length > 0 && /* @__PURE__ */ o.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ o.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ o.jsx(Yo, { size: 14 }),
          " Sets"
        ] }),
        /* @__PURE__ */ o.jsx("div", { className: "availability-set-list", children: j.map((V) => /* @__PURE__ */ o.jsxs("label", { className: "availability-set-option", children: [
          /* @__PURE__ */ o.jsx(
            "input",
            {
              type: "checkbox",
              role: "switch",
              className: "wf-switch-input",
              "aria-label": `Activities in the ${V.name} set available in new workflows`,
              checked: wh(l, "sets", V.name),
              disabled: c || u,
              onChange: (q) => H(V.name, q.target.checked)
            }
          ),
          /* @__PURE__ */ o.jsx("span", { children: V.name }),
          /* @__PURE__ */ o.jsx("code", { children: (V.activityTypeKeys ?? []).length })
        ] }, V.name)) })
      ] }),
      /* @__PURE__ */ o.jsxs("section", { className: "availability-section availability-section-grow", children: [
        /* @__PURE__ */ o.jsxs("div", { className: "availability-section-head", children: [
          /* @__PURE__ */ o.jsxs("h3", { className: "wf-section-label", children: [
            /* @__PURE__ */ o.jsx(Xc, { size: 14 }),
            " Activities"
          ] }),
          /* @__PURE__ */ o.jsxs("div", { className: "availability-section-tools", children: [
            /* @__PURE__ */ o.jsxs(
              "button",
              {
                type: "button",
                className: `availability-filter-toggle${h ? " active" : ""}`,
                "aria-pressed": h,
                title: "Show only activities that are turned off or blocked",
                onClick: () => y((V) => !V),
                children: [
                  /* @__PURE__ */ o.jsx(eo, { size: 13 }),
                  "Hidden only"
                ]
              }
            ),
            /* @__PURE__ */ o.jsxs("div", { className: "wf-search availability-search", children: [
              /* @__PURE__ */ o.jsx(Qi, { size: 14 }),
              /* @__PURE__ */ o.jsx("input", { type: "search", value: f, placeholder: "Filter activities…", onChange: (V) => p(V.target.value) })
            ] })
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("div", { className: "availability-activity-browser", children: [
          /* @__PURE__ */ o.jsxs("div", { className: "availability-activity-list", children: [
            c && g.length === 0 && /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "Loading availability…" }),
            !c && g.length === 0 && /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "No availability diagnostics reported." }),
            !c && g.length > 0 && M.length === 0 && /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: h && !f.trim() ? "No hidden activities — everything is turned on." : "No activities match the filter." }),
            I.map((V) => {
              const q = V.entries.filter((X) => S(X).lockedBy === null), G = q.filter((X) => S(X).enabled).length, Z = q.length > 0 && G === q.length, R = G > 0 && !Z;
              return /* @__PURE__ */ o.jsxs("div", { className: "availability-group", children: [
                /* @__PURE__ */ o.jsxs("div", { className: "availability-group-header", children: [
                  /* @__PURE__ */ o.jsxs("span", { className: "availability-group-title", children: [
                    V.category,
                    /* @__PURE__ */ o.jsx("small", { children: V.entries.length })
                  ] }),
                  q.length > 0 && /* @__PURE__ */ o.jsx(
                    "input",
                    {
                      type: "checkbox",
                      role: "switch",
                      className: "wf-switch-input",
                      "aria-label": `Toggle the ${q.length} listed ${V.category} activities`,
                      checked: Z,
                      ref: (X) => {
                        X && (X.indeterminate = R);
                      },
                      disabled: c || u,
                      onChange: () => W(q.map(Nn), !Z)
                    }
                  )
                ] }),
                V.entries.map((X) => {
                  const ce = Nn(X), se = sa(X), ee = kt(X.state), ne = S(X), de = ne.lockedBy !== null, O = dn(_(X)), te = Ln(X), ge = ea(X), ye = ta(X), Ae = m === se;
                  return /* @__PURE__ */ o.jsxs("div", { className: `availability-activity-row${Ae ? " selected" : ""}${de ? " disabled" : ""}`, children: [
                    /* @__PURE__ */ o.jsxs(
                      "button",
                      {
                        type: "button",
                        className: "availability-activity-open",
                        "aria-expanded": Ae,
                        title: "Show activity details",
                        onClick: () => b(Ae ? null : se),
                        children: [
                          /* @__PURE__ */ o.jsx(_l, { icon: O }),
                          /* @__PURE__ */ o.jsxs("span", { className: "availability-activity-main", children: [
                            /* @__PURE__ */ o.jsxs("span", { className: "availability-activity-title-line", children: [
                              /* @__PURE__ */ o.jsx("strong", { children: te }),
                              ge && /* @__PURE__ */ o.jsx("code", { title: X.activityTypeKey ?? void 0, children: ge })
                            ] }),
                            ye && /* @__PURE__ */ o.jsx("span", { className: "availability-activity-description", title: ye, children: ye })
                          ] })
                        ]
                      }
                    ),
                    ee !== "Available" && /* @__PURE__ */ o.jsx("em", { className: `availability-state ${jl(X.state)}`, children: Rn(X.state) }),
                    /* @__PURE__ */ o.jsx(
                      "input",
                      {
                        type: "checkbox",
                        role: "switch",
                        className: "wf-switch-input",
                        "aria-label": `${te} available in new workflows`,
                        title: Bh(ne),
                        checked: ne.enabled,
                        disabled: c || u || de,
                        onChange: (_e) => B(ce, _e.target.checked)
                      }
                    )
                  ] }, se);
                })
              ] }, V.category);
            })
          ] }),
          $ && /* @__PURE__ */ o.jsx(
            Kh,
            {
              entry: $,
              catalogItem: _($),
              onClose: () => b(null)
            }
          )
        ] })
      ] }),
      v.length > 0 && /* @__PURE__ */ o.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ o.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ o.jsx(zi, { size: 14 }),
          " Unresolved references"
        ] }),
        /* @__PURE__ */ o.jsx("div", { className: "availability-unresolved-list", children: v.map((V) => /* @__PURE__ */ o.jsxs("span", { children: [
          /* @__PURE__ */ o.jsx("strong", { children: V.referenceName }),
          /* @__PURE__ */ o.jsx("em", { children: Rn(V.state) })
        ] }, `${V.layer}-${V.referenceKind}-${V.referenceName}`)) })
      ] })
    ] })
  ] });
}
function Kh({
  entry: e,
  catalogItem: t,
  onClose: n
}) {
  const i = Ln(e), r = e.description?.trim() || t?.description?.trim(), s = ue(() => na(t?.inputs), [t]), a = ue(() => na(t?.outputs), [t]), c = ue(() => JSON.stringify({ diagnostic: e, catalog: t }, null, 2), [e, t]), u = [
    ["Type", e.activityTypeKey],
    ["Definition ID", e.activityDefinitionId],
    ["Category", e.category],
    ["Version", t?.version],
    ["Execution", t?.executionType],
    ["Policy layer", fh(e.layer)]
  ];
  return /* @__PURE__ */ o.jsxs("aside", { className: "availability-details", "aria-label": `${i} details`, children: [
    /* @__PURE__ */ o.jsxs("header", { className: "availability-details-header", children: [
      /* @__PURE__ */ o.jsx(_l, { icon: dn(t) }),
      /* @__PURE__ */ o.jsx("h4", { children: i }),
      /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-icon-button availability-details-close", "aria-label": "Close details", onClick: n, children: /* @__PURE__ */ o.jsx(Uo, { size: 14 }) })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "availability-details-body", children: [
      /* @__PURE__ */ o.jsxs("p", { className: "availability-details-status", children: [
        /* @__PURE__ */ o.jsx("em", { className: `availability-state ${jl(e.state)}`, children: Rn(e.state) }),
        e.reason?.trim() && /* @__PURE__ */ o.jsx("span", { children: e.reason })
      ] }),
      r && /* @__PURE__ */ o.jsx("p", { className: "availability-details-description", children: r }),
      /* @__PURE__ */ o.jsx("dl", { className: "availability-details-meta", children: u.map(([l, d]) => d?.trim() ? /* @__PURE__ */ o.jsxs(Je.Fragment, { children: [
        /* @__PURE__ */ o.jsx("dt", { children: l }),
        /* @__PURE__ */ o.jsx("dd", { children: /* @__PURE__ */ o.jsx("code", { children: d }) })
      ] }, l) : null) }),
      s.length > 0 && /* @__PURE__ */ o.jsx(aa, { title: "Inputs", items: s }),
      a.length > 0 && /* @__PURE__ */ o.jsx(aa, { title: "Outputs", items: a }),
      !t && /* @__PURE__ */ o.jsx("p", { className: "wf-muted availability-details-note", children: "Catalog metadata is unavailable for this activity — it is not exposed by the currently saved policy." }),
      /* @__PURE__ */ o.jsxs("details", { className: "availability-details-raw", children: [
        /* @__PURE__ */ o.jsx("summary", { children: "Raw metadata" }),
        /* @__PURE__ */ o.jsx("pre", { children: c })
      ] })
    ] })
  ] });
}
function aa({ title: e, items: t }) {
  return /* @__PURE__ */ o.jsxs("section", { className: "availability-details-arguments", children: [
    /* @__PURE__ */ o.jsx("h5", { children: e }),
    /* @__PURE__ */ o.jsx("ul", { children: t.map((n) => /* @__PURE__ */ o.jsxs("li", { title: n.description || void 0, children: [
      /* @__PURE__ */ o.jsx("strong", { children: n.name }),
      n.typeName && /* @__PURE__ */ o.jsx("code", { children: rs(n.typeName) })
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
var Xh = { value: () => {
} };
function or() {
  for (var e = 0, t = arguments.length, n = {}, i; e < t; ++e) {
    if (!(i = arguments[e] + "") || i in n || /[\s.]/.test(i)) throw new Error("illegal type: " + i);
    n[i] = [];
  }
  return new _i(n);
}
function _i(e) {
  this._ = e;
}
function qh(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var i = "", r = n.indexOf(".");
    if (r >= 0 && (i = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: i };
  });
}
_i.prototype = or.prototype = {
  constructor: _i,
  on: function(e, t) {
    var n = this._, i = qh(e + "", n), r, s = -1, a = i.length;
    if (arguments.length < 2) {
      for (; ++s < a; ) if ((r = (e = i[s]).type) && (r = Yh(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++s < a; )
      if (r = (e = i[s]).type) n[r] = ca(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = ca(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new _i(e);
  },
  call: function(e, t) {
    if ((r = arguments.length - 2) > 0) for (var n = new Array(r), i = 0, r, s; i < r; ++i) n[i] = arguments[i + 2];
    if (!this._.hasOwnProperty(e)) throw new Error("unknown type: " + e);
    for (s = this._[e], i = 0, r = s.length; i < r; ++i) s[i].value.apply(t, n);
  },
  apply: function(e, t, n) {
    if (!this._.hasOwnProperty(e)) throw new Error("unknown type: " + e);
    for (var i = this._[e], r = 0, s = i.length; r < s; ++r) i[r].value.apply(t, n);
  }
};
function Yh(e, t) {
  for (var n = 0, i = e.length, r; n < i; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function ca(e, t, n) {
  for (var i = 0, r = e.length; i < r; ++i)
    if (e[i].name === t) {
      e[i] = Xh, e = e.slice(0, i).concat(e.slice(i + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var Eo = "http://www.w3.org/1999/xhtml";
const la = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: Eo,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function sr(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), la.hasOwnProperty(t) ? { space: la[t], local: e } : e;
}
function Uh(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === Eo && t.documentElement.namespaceURI === Eo ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Zh(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function Dl(e) {
  var t = sr(e);
  return (t.local ? Zh : Uh)(t);
}
function Gh() {
}
function as(e) {
  return e == null ? Gh : function() {
    return this.querySelector(e);
  };
}
function Jh(e) {
  typeof e != "function" && (e = as(e));
  for (var t = this._groups, n = t.length, i = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = i[r] = new Array(a), u, l, d = 0; d < a; ++d)
      (u = s[d]) && (l = e.call(u, u.__data__, d, s)) && ("__data__" in u && (l.__data__ = u.__data__), c[d] = l);
  return new Fe(i, this._parents);
}
function Qh(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function eg() {
  return [];
}
function Tl(e) {
  return e == null ? eg : function() {
    return this.querySelectorAll(e);
  };
}
function tg(e) {
  return function() {
    return Qh(e.apply(this, arguments));
  };
}
function ng(e) {
  typeof e == "function" ? e = tg(e) : e = Tl(e);
  for (var t = this._groups, n = t.length, i = [], r = [], s = 0; s < n; ++s)
    for (var a = t[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && (i.push(e.call(u, u.__data__, l, a)), r.push(u));
  return new Fe(i, r);
}
function $l(e) {
  return function() {
    return this.matches(e);
  };
}
function Pl(e) {
  return function(t) {
    return t.matches(e);
  };
}
var ig = Array.prototype.find;
function rg(e) {
  return function() {
    return ig.call(this.children, e);
  };
}
function og() {
  return this.firstElementChild;
}
function sg(e) {
  return this.select(e == null ? og : rg(typeof e == "function" ? e : Pl(e)));
}
var ag = Array.prototype.filter;
function cg() {
  return Array.from(this.children);
}
function lg(e) {
  return function() {
    return ag.call(this.children, e);
  };
}
function ug(e) {
  return this.selectAll(e == null ? cg : lg(typeof e == "function" ? e : Pl(e)));
}
function dg(e) {
  typeof e != "function" && (e = $l(e));
  for (var t = this._groups, n = t.length, i = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = i[r] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new Fe(i, this._parents);
}
function Ml(e) {
  return new Array(e.length);
}
function fg() {
  return new Fe(this._enter || this._groups.map(Ml), this._parents);
}
function Wi(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
Wi.prototype = {
  constructor: Wi,
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
function pg(e) {
  return function() {
    return e;
  };
}
function hg(e, t, n, i, r, s) {
  for (var a = 0, c, u = t.length, l = s.length; a < l; ++a)
    (c = t[a]) ? (c.__data__ = s[a], i[a] = c) : n[a] = new Wi(e, s[a]);
  for (; a < u; ++a)
    (c = t[a]) && (r[a] = c);
}
function gg(e, t, n, i, r, s, a) {
  var c, u, l = /* @__PURE__ */ new Map(), d = t.length, f = s.length, p = new Array(d), h;
  for (c = 0; c < d; ++c)
    (u = t[c]) && (p[c] = h = a.call(u, u.__data__, c, t) + "", l.has(h) ? r[c] = u : l.set(h, u));
  for (c = 0; c < f; ++c)
    h = a.call(e, s[c], c, s) + "", (u = l.get(h)) ? (i[c] = u, u.__data__ = s[c], l.delete(h)) : n[c] = new Wi(e, s[c]);
  for (c = 0; c < d; ++c)
    (u = t[c]) && l.get(p[c]) === u && (r[c] = u);
}
function yg(e) {
  return e.__data__;
}
function mg(e, t) {
  if (!arguments.length) return Array.from(this, yg);
  var n = t ? gg : hg, i = this._parents, r = this._groups;
  typeof e != "function" && (e = pg(e));
  for (var s = r.length, a = new Array(s), c = new Array(s), u = new Array(s), l = 0; l < s; ++l) {
    var d = i[l], f = r[l], p = f.length, h = xg(e.call(d, d && d.__data__, l, i)), y = h.length, w = c[l] = new Array(y), x = a[l] = new Array(y), m = u[l] = new Array(p);
    n(d, f, w, x, m, h, t);
    for (var b = 0, g = 0, v, j; b < y; ++b)
      if (v = w[b]) {
        for (b >= g && (g = b + 1); !(j = x[g]) && ++g < y; ) ;
        v._next = j || null;
      }
  }
  return a = new Fe(a, i), a._enter = c, a._exit = u, a;
}
function xg(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function wg() {
  return new Fe(this._exit || this._groups.map(Ml), this._parents);
}
function vg(e, t, n) {
  var i = this.enter(), r = this, s = this.exit();
  return typeof e == "function" ? (i = e(i), i && (i = i.selection())) : i = i.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? s.remove() : n(s), i && r ? i.merge(r).order() : r;
}
function bg(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, i = t._groups, r = n.length, s = i.length, a = Math.min(r, s), c = new Array(r), u = 0; u < a; ++u)
    for (var l = n[u], d = i[u], f = l.length, p = c[u] = new Array(f), h, y = 0; y < f; ++y)
      (h = l[y] || d[y]) && (p[y] = h);
  for (; u < r; ++u)
    c[u] = n[u];
  return new Fe(c, this._parents);
}
function Ng() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var i = e[t], r = i.length - 1, s = i[r], a; --r >= 0; )
      (a = i[r]) && (s && a.compareDocumentPosition(s) ^ 4 && s.parentNode.insertBefore(a, s), s = a);
  return this;
}
function jg(e) {
  e || (e = Sg);
  function t(f, p) {
    return f && p ? e(f.__data__, p.__data__) : !f - !p;
  }
  for (var n = this._groups, i = n.length, r = new Array(i), s = 0; s < i; ++s) {
    for (var a = n[s], c = a.length, u = r[s] = new Array(c), l, d = 0; d < c; ++d)
      (l = a[d]) && (u[d] = l);
    u.sort(t);
  }
  return new Fe(r, this._parents).order();
}
function Sg(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function Cg() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function Eg() {
  return Array.from(this);
}
function kg() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var i = e[t], r = 0, s = i.length; r < s; ++r) {
      var a = i[r];
      if (a) return a;
    }
  return null;
}
function Ig() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function Ag() {
  return !this.node();
}
function _g(e) {
  for (var t = this._groups, n = 0, i = t.length; n < i; ++n)
    for (var r = t[n], s = 0, a = r.length, c; s < a; ++s)
      (c = r[s]) && e.call(c, c.__data__, s, r);
  return this;
}
function Dg(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Tg(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function $g(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function Pg(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function Mg(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function Rg(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function Lg(e, t) {
  var n = sr(e);
  if (arguments.length < 2) {
    var i = this.node();
    return n.local ? i.getAttributeNS(n.space, n.local) : i.getAttribute(n);
  }
  return this.each((t == null ? n.local ? Tg : Dg : typeof t == "function" ? n.local ? Rg : Mg : n.local ? Pg : $g)(n, t));
}
function Rl(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function zg(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Vg(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function Og(e, t, n) {
  return function() {
    var i = t.apply(this, arguments);
    i == null ? this.style.removeProperty(e) : this.style.setProperty(e, i, n);
  };
}
function Hg(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? zg : typeof t == "function" ? Og : Vg)(e, t, n ?? "")) : en(this.node(), e);
}
function en(e, t) {
  return e.style.getPropertyValue(t) || Rl(e).getComputedStyle(e, null).getPropertyValue(t);
}
function Wg(e) {
  return function() {
    delete this[e];
  };
}
function Bg(e, t) {
  return function() {
    this[e] = t;
  };
}
function Fg(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function Kg(e, t) {
  return arguments.length > 1 ? this.each((t == null ? Wg : typeof t == "function" ? Fg : Bg)(e, t)) : this.node()[e];
}
function Ll(e) {
  return e.trim().split(/^|\s+/);
}
function cs(e) {
  return e.classList || new zl(e);
}
function zl(e) {
  this._node = e, this._names = Ll(e.getAttribute("class") || "");
}
zl.prototype = {
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
function Vl(e, t) {
  for (var n = cs(e), i = -1, r = t.length; ++i < r; ) n.add(t[i]);
}
function Ol(e, t) {
  for (var n = cs(e), i = -1, r = t.length; ++i < r; ) n.remove(t[i]);
}
function Xg(e) {
  return function() {
    Vl(this, e);
  };
}
function qg(e) {
  return function() {
    Ol(this, e);
  };
}
function Yg(e, t) {
  return function() {
    (t.apply(this, arguments) ? Vl : Ol)(this, e);
  };
}
function Ug(e, t) {
  var n = Ll(e + "");
  if (arguments.length < 2) {
    for (var i = cs(this.node()), r = -1, s = n.length; ++r < s; ) if (!i.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? Yg : t ? Xg : qg)(n, t));
}
function Zg() {
  this.textContent = "";
}
function Gg(e) {
  return function() {
    this.textContent = e;
  };
}
function Jg(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function Qg(e) {
  return arguments.length ? this.each(e == null ? Zg : (typeof e == "function" ? Jg : Gg)(e)) : this.node().textContent;
}
function ey() {
  this.innerHTML = "";
}
function ty(e) {
  return function() {
    this.innerHTML = e;
  };
}
function ny(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function iy(e) {
  return arguments.length ? this.each(e == null ? ey : (typeof e == "function" ? ny : ty)(e)) : this.node().innerHTML;
}
function ry() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function oy() {
  return this.each(ry);
}
function sy() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function ay() {
  return this.each(sy);
}
function cy(e) {
  var t = typeof e == "function" ? e : Dl(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function ly() {
  return null;
}
function uy(e, t) {
  var n = typeof e == "function" ? e : Dl(e), i = t == null ? ly : typeof t == "function" ? t : as(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), i.apply(this, arguments) || null);
  });
}
function dy() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function fy() {
  return this.each(dy);
}
function py() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function hy() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function gy(e) {
  return this.select(e ? hy : py);
}
function yy(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function my(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function xy(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", i = t.indexOf(".");
    return i >= 0 && (n = t.slice(i + 1), t = t.slice(0, i)), { type: t, name: n };
  });
}
function wy(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, i = -1, r = t.length, s; n < r; ++n)
        s = t[n], (!e.type || s.type === e.type) && s.name === e.name ? this.removeEventListener(s.type, s.listener, s.options) : t[++i] = s;
      ++i ? t.length = i : delete this.__on;
    }
  };
}
function vy(e, t, n) {
  return function() {
    var i = this.__on, r, s = my(t);
    if (i) {
      for (var a = 0, c = i.length; a < c; ++a)
        if ((r = i[a]).type === e.type && r.name === e.name) {
          this.removeEventListener(r.type, r.listener, r.options), this.addEventListener(r.type, r.listener = s, r.options = n), r.value = t;
          return;
        }
    }
    this.addEventListener(e.type, s, n), r = { type: e.type, name: e.name, value: t, listener: s, options: n }, i ? i.push(r) : this.__on = [r];
  };
}
function by(e, t, n) {
  var i = xy(e + ""), r, s = i.length, a;
  if (arguments.length < 2) {
    var c = this.node().__on;
    if (c) {
      for (var u = 0, l = c.length, d; u < l; ++u)
        for (r = 0, d = c[u]; r < s; ++r)
          if ((a = i[r]).type === d.type && a.name === d.name)
            return d.value;
    }
    return;
  }
  for (c = t ? vy : wy, r = 0; r < s; ++r) this.each(c(i[r], t, n));
  return this;
}
function Hl(e, t, n) {
  var i = Rl(e), r = i.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = i.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function Ny(e, t) {
  return function() {
    return Hl(this, e, t);
  };
}
function jy(e, t) {
  return function() {
    return Hl(this, e, t.apply(this, arguments));
  };
}
function Sy(e, t) {
  return this.each((typeof t == "function" ? jy : Ny)(e, t));
}
function* Cy() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var i = e[t], r = 0, s = i.length, a; r < s; ++r)
      (a = i[r]) && (yield a);
}
var Wl = [null];
function Fe(e, t) {
  this._groups = e, this._parents = t;
}
function Jn() {
  return new Fe([[document.documentElement]], Wl);
}
function Ey() {
  return this;
}
Fe.prototype = Jn.prototype = {
  constructor: Fe,
  select: Jh,
  selectAll: ng,
  selectChild: sg,
  selectChildren: ug,
  filter: dg,
  data: mg,
  enter: fg,
  exit: wg,
  join: vg,
  merge: bg,
  selection: Ey,
  order: Ng,
  sort: jg,
  call: Cg,
  nodes: Eg,
  node: kg,
  size: Ig,
  empty: Ag,
  each: _g,
  attr: Lg,
  style: Hg,
  property: Kg,
  classed: Ug,
  text: Qg,
  html: iy,
  raise: oy,
  lower: ay,
  append: cy,
  insert: uy,
  remove: fy,
  clone: gy,
  datum: yy,
  on: by,
  dispatch: Sy,
  [Symbol.iterator]: Cy
};
function We(e) {
  return typeof e == "string" ? new Fe([[document.querySelector(e)]], [document.documentElement]) : new Fe([[e]], Wl);
}
function ky(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Ye(e, t) {
  if (e = ky(e), t === void 0 && (t = e.currentTarget), t) {
    var n = t.ownerSVGElement || t;
    if (n.createSVGPoint) {
      var i = n.createSVGPoint();
      return i.x = e.clientX, i.y = e.clientY, i = i.matrixTransform(t.getScreenCTM().inverse()), [i.x, i.y];
    }
    if (t.getBoundingClientRect) {
      var r = t.getBoundingClientRect();
      return [e.clientX - r.left - t.clientLeft, e.clientY - r.top - t.clientTop];
    }
  }
  return [e.pageX, e.pageY];
}
const Iy = { passive: !1 }, zn = { capture: !0, passive: !1 };
function oo(e) {
  e.stopImmediatePropagation();
}
function Zt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Bl(e) {
  var t = e.document.documentElement, n = We(e).on("dragstart.drag", Zt, zn);
  "onselectstart" in t ? n.on("selectstart.drag", Zt, zn) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function Fl(e, t) {
  var n = e.document.documentElement, i = We(e).on("dragstart.drag", null);
  t && (i.on("click.drag", Zt, zn), setTimeout(function() {
    i.on("click.drag", null);
  }, 0)), "onselectstart" in n ? i.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const yi = (e) => () => e;
function ko(e, {
  sourceEvent: t,
  subject: n,
  target: i,
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
    target: { value: i, enumerable: !0, configurable: !0 },
    identifier: { value: r, enumerable: !0, configurable: !0 },
    active: { value: s, enumerable: !0, configurable: !0 },
    x: { value: a, enumerable: !0, configurable: !0 },
    y: { value: c, enumerable: !0, configurable: !0 },
    dx: { value: u, enumerable: !0, configurable: !0 },
    dy: { value: l, enumerable: !0, configurable: !0 },
    _: { value: d }
  });
}
ko.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function Ay(e) {
  return !e.ctrlKey && !e.button;
}
function _y() {
  return this.parentNode;
}
function Dy(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function Ty() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Kl() {
  var e = Ay, t = _y, n = Dy, i = Ty, r = {}, s = or("start", "drag", "end"), a = 0, c, u, l, d, f = 0;
  function p(v) {
    v.on("mousedown.drag", h).filter(i).on("touchstart.drag", x).on("touchmove.drag", m, Iy).on("touchend.drag touchcancel.drag", b).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function h(v, j) {
    if (!(d || !e.call(this, v, j))) {
      var N = g(this, t.call(this, v, j), v, j, "mouse");
      N && (We(v.view).on("mousemove.drag", y, zn).on("mouseup.drag", w, zn), Bl(v.view), oo(v), l = !1, c = v.clientX, u = v.clientY, N("start", v));
    }
  }
  function y(v) {
    if (Zt(v), !l) {
      var j = v.clientX - c, N = v.clientY - u;
      l = j * j + N * N > f;
    }
    r.mouse("drag", v);
  }
  function w(v) {
    We(v.view).on("mousemove.drag mouseup.drag", null), Fl(v.view, l), Zt(v), r.mouse("end", v);
  }
  function x(v, j) {
    if (e.call(this, v, j)) {
      var N = v.changedTouches, S = t.call(this, v, j), k = N.length, _, M;
      for (_ = 0; _ < k; ++_)
        (M = g(this, S, v, j, N[_].identifier, N[_])) && (oo(v), M("start", v, N[_]));
    }
  }
  function m(v) {
    var j = v.changedTouches, N = j.length, S, k;
    for (S = 0; S < N; ++S)
      (k = r[j[S].identifier]) && (Zt(v), k("drag", v, j[S]));
  }
  function b(v) {
    var j = v.changedTouches, N = j.length, S, k;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), S = 0; S < N; ++S)
      (k = r[j[S].identifier]) && (oo(v), k("end", v, j[S]));
  }
  function g(v, j, N, S, k, _) {
    var M = s.copy(), I = Ye(_ || N, j), $, z, C;
    if ((C = n.call(v, new ko("beforestart", {
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
      return $ = C.x - I[0] || 0, z = C.y - I[1] || 0, function A(E, D, P) {
        var T = I, B;
        switch (E) {
          case "start":
            r[k] = A, B = a++;
            break;
          case "end":
            delete r[k], --a;
          // falls through
          case "drag":
            I = Ye(P || D, j), B = a;
            break;
        }
        M.call(
          E,
          v,
          new ko(E, {
            sourceEvent: D,
            subject: C,
            target: p,
            identifier: k,
            active: B,
            x: I[0] + $,
            y: I[1] + z,
            dx: I[0] - T[0],
            dy: I[1] - T[1],
            dispatch: M
          }),
          S
        );
      };
  }
  return p.filter = function(v) {
    return arguments.length ? (e = typeof v == "function" ? v : yi(!!v), p) : e;
  }, p.container = function(v) {
    return arguments.length ? (t = typeof v == "function" ? v : yi(v), p) : t;
  }, p.subject = function(v) {
    return arguments.length ? (n = typeof v == "function" ? v : yi(v), p) : n;
  }, p.touchable = function(v) {
    return arguments.length ? (i = typeof v == "function" ? v : yi(!!v), p) : i;
  }, p.on = function() {
    var v = s.on.apply(s, arguments);
    return v === s ? p : v;
  }, p.clickDistance = function(v) {
    return arguments.length ? (f = (v = +v) * v, p) : Math.sqrt(f);
  }, p;
}
function ls(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function Xl(e, t) {
  var n = Object.create(e.prototype);
  for (var i in t) n[i] = t[i];
  return n;
}
function Qn() {
}
var Vn = 0.7, Bi = 1 / Vn, Gt = "\\s*([+-]?\\d+)\\s*", On = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", tt = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", $y = /^#([0-9a-f]{3,8})$/, Py = new RegExp(`^rgb\\(${Gt},${Gt},${Gt}\\)$`), My = new RegExp(`^rgb\\(${tt},${tt},${tt}\\)$`), Ry = new RegExp(`^rgba\\(${Gt},${Gt},${Gt},${On}\\)$`), Ly = new RegExp(`^rgba\\(${tt},${tt},${tt},${On}\\)$`), zy = new RegExp(`^hsl\\(${On},${tt},${tt}\\)$`), Vy = new RegExp(`^hsla\\(${On},${tt},${tt},${On}\\)$`), ua = {
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
ls(Qn, At, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: da,
  // Deprecated! Use color.formatHex.
  formatHex: da,
  formatHex8: Oy,
  formatHsl: Hy,
  formatRgb: fa,
  toString: fa
});
function da() {
  return this.rgb().formatHex();
}
function Oy() {
  return this.rgb().formatHex8();
}
function Hy() {
  return ql(this).formatHsl();
}
function fa() {
  return this.rgb().formatRgb();
}
function At(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = $y.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? pa(t) : n === 3 ? new Ve(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? mi(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? mi(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = Py.exec(e)) ? new Ve(t[1], t[2], t[3], 1) : (t = My.exec(e)) ? new Ve(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = Ry.exec(e)) ? mi(t[1], t[2], t[3], t[4]) : (t = Ly.exec(e)) ? mi(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = zy.exec(e)) ? ya(t[1], t[2] / 100, t[3] / 100, 1) : (t = Vy.exec(e)) ? ya(t[1], t[2] / 100, t[3] / 100, t[4]) : ua.hasOwnProperty(e) ? pa(ua[e]) : e === "transparent" ? new Ve(NaN, NaN, NaN, 0) : null;
}
function pa(e) {
  return new Ve(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function mi(e, t, n, i) {
  return i <= 0 && (e = t = n = NaN), new Ve(e, t, n, i);
}
function Wy(e) {
  return e instanceof Qn || (e = At(e)), e ? (e = e.rgb(), new Ve(e.r, e.g, e.b, e.opacity)) : new Ve();
}
function Io(e, t, n, i) {
  return arguments.length === 1 ? Wy(e) : new Ve(e, t, n, i ?? 1);
}
function Ve(e, t, n, i) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +i;
}
ls(Ve, Io, Xl(Qn, {
  brighter(e) {
    return e = e == null ? Bi : Math.pow(Bi, e), new Ve(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Vn : Math.pow(Vn, e), new Ve(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new Ve(St(this.r), St(this.g), St(this.b), Fi(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: ha,
  // Deprecated! Use color.formatHex.
  formatHex: ha,
  formatHex8: By,
  formatRgb: ga,
  toString: ga
}));
function ha() {
  return `#${jt(this.r)}${jt(this.g)}${jt(this.b)}`;
}
function By() {
  return `#${jt(this.r)}${jt(this.g)}${jt(this.b)}${jt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function ga() {
  const e = Fi(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${St(this.r)}, ${St(this.g)}, ${St(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Fi(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function St(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function jt(e) {
  return e = St(e), (e < 16 ? "0" : "") + e.toString(16);
}
function ya(e, t, n, i) {
  return i <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Ue(e, t, n, i);
}
function ql(e) {
  if (e instanceof Ue) return new Ue(e.h, e.s, e.l, e.opacity);
  if (e instanceof Qn || (e = At(e)), !e) return new Ue();
  if (e instanceof Ue) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, i = e.b / 255, r = Math.min(t, n, i), s = Math.max(t, n, i), a = NaN, c = s - r, u = (s + r) / 2;
  return c ? (t === s ? a = (n - i) / c + (n < i) * 6 : n === s ? a = (i - t) / c + 2 : a = (t - n) / c + 4, c /= u < 0.5 ? s + r : 2 - s - r, a *= 60) : c = u > 0 && u < 1 ? 0 : a, new Ue(a, c, u, e.opacity);
}
function Fy(e, t, n, i) {
  return arguments.length === 1 ? ql(e) : new Ue(e, t, n, i ?? 1);
}
function Ue(e, t, n, i) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +i;
}
ls(Ue, Fy, Xl(Qn, {
  brighter(e) {
    return e = e == null ? Bi : Math.pow(Bi, e), new Ue(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Vn : Math.pow(Vn, e), new Ue(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, i = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - i;
    return new Ve(
      so(e >= 240 ? e - 240 : e + 120, r, i),
      so(e, r, i),
      so(e < 120 ? e + 240 : e - 120, r, i),
      this.opacity
    );
  },
  clamp() {
    return new Ue(ma(this.h), xi(this.s), xi(this.l), Fi(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Fi(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${ma(this.h)}, ${xi(this.s) * 100}%, ${xi(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function ma(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function xi(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function so(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const us = (e) => () => e;
function Ky(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function Xy(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(i) {
    return Math.pow(e + i * t, n);
  };
}
function qy(e) {
  return (e = +e) == 1 ? Yl : function(t, n) {
    return n - t ? Xy(t, n, e) : us(isNaN(t) ? n : t);
  };
}
function Yl(e, t) {
  var n = t - e;
  return n ? Ky(e, n) : us(isNaN(e) ? t : e);
}
const Ki = (function e(t) {
  var n = qy(t);
  function i(r, s) {
    var a = n((r = Io(r)).r, (s = Io(s)).r), c = n(r.g, s.g), u = n(r.b, s.b), l = Yl(r.opacity, s.opacity);
    return function(d) {
      return r.r = a(d), r.g = c(d), r.b = u(d), r.opacity = l(d), r + "";
    };
  }
  return i.gamma = e, i;
})(1);
function Yy(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, i = t.slice(), r;
  return function(s) {
    for (r = 0; r < n; ++r) i[r] = e[r] * (1 - s) + t[r] * s;
    return i;
  };
}
function Uy(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Zy(e, t) {
  var n = t ? t.length : 0, i = e ? Math.min(n, e.length) : 0, r = new Array(i), s = new Array(n), a;
  for (a = 0; a < i; ++a) r[a] = An(e[a], t[a]);
  for (; a < n; ++a) s[a] = t[a];
  return function(c) {
    for (a = 0; a < i; ++a) s[a] = r[a](c);
    return s;
  };
}
function Gy(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(i) {
    return n.setTime(e * (1 - i) + t * i), n;
  };
}
function et(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function Jy(e, t) {
  var n = {}, i = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = An(e[r], t[r]) : i[r] = t[r];
  return function(s) {
    for (r in n) i[r] = n[r](s);
    return i;
  };
}
var Ao = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, ao = new RegExp(Ao.source, "g");
function Qy(e) {
  return function() {
    return e;
  };
}
function em(e) {
  return function(t) {
    return e(t) + "";
  };
}
function Ul(e, t) {
  var n = Ao.lastIndex = ao.lastIndex = 0, i, r, s, a = -1, c = [], u = [];
  for (e = e + "", t = t + ""; (i = Ao.exec(e)) && (r = ao.exec(t)); )
    (s = r.index) > n && (s = t.slice(n, s), c[a] ? c[a] += s : c[++a] = s), (i = i[0]) === (r = r[0]) ? c[a] ? c[a] += r : c[++a] = r : (c[++a] = null, u.push({ i: a, x: et(i, r) })), n = ao.lastIndex;
  return n < t.length && (s = t.slice(n), c[a] ? c[a] += s : c[++a] = s), c.length < 2 ? u[0] ? em(u[0].x) : Qy(t) : (t = u.length, function(l) {
    for (var d = 0, f; d < t; ++d) c[(f = u[d]).i] = f.x(l);
    return c.join("");
  });
}
function An(e, t) {
  var n = typeof t, i;
  return t == null || n === "boolean" ? us(t) : (n === "number" ? et : n === "string" ? (i = At(t)) ? (t = i, Ki) : Ul : t instanceof At ? Ki : t instanceof Date ? Gy : Uy(t) ? Yy : Array.isArray(t) ? Zy : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Jy : et)(e, t);
}
var xa = 180 / Math.PI, _o = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function Zl(e, t, n, i, r, s) {
  var a, c, u;
  return (a = Math.sqrt(e * e + t * t)) && (e /= a, t /= a), (u = e * n + t * i) && (n -= e * u, i -= t * u), (c = Math.sqrt(n * n + i * i)) && (n /= c, i /= c, u /= c), e * i < t * n && (e = -e, t = -t, u = -u, a = -a), {
    translateX: r,
    translateY: s,
    rotate: Math.atan2(t, e) * xa,
    skewX: Math.atan(u) * xa,
    scaleX: a,
    scaleY: c
  };
}
var wi;
function tm(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? _o : Zl(t.a, t.b, t.c, t.d, t.e, t.f);
}
function nm(e) {
  return e == null || (wi || (wi = document.createElementNS("http://www.w3.org/2000/svg", "g")), wi.setAttribute("transform", e), !(e = wi.transform.baseVal.consolidate())) ? _o : (e = e.matrix, Zl(e.a, e.b, e.c, e.d, e.e, e.f));
}
function Gl(e, t, n, i) {
  function r(l) {
    return l.length ? l.pop() + " " : "";
  }
  function s(l, d, f, p, h, y) {
    if (l !== f || d !== p) {
      var w = h.push("translate(", null, t, null, n);
      y.push({ i: w - 4, x: et(l, f) }, { i: w - 2, x: et(d, p) });
    } else (f || p) && h.push("translate(" + f + t + p + n);
  }
  function a(l, d, f, p) {
    l !== d ? (l - d > 180 ? d += 360 : d - l > 180 && (l += 360), p.push({ i: f.push(r(f) + "rotate(", null, i) - 2, x: et(l, d) })) : d && f.push(r(f) + "rotate(" + d + i);
  }
  function c(l, d, f, p) {
    l !== d ? p.push({ i: f.push(r(f) + "skewX(", null, i) - 2, x: et(l, d) }) : d && f.push(r(f) + "skewX(" + d + i);
  }
  function u(l, d, f, p, h, y) {
    if (l !== f || d !== p) {
      var w = h.push(r(h) + "scale(", null, ",", null, ")");
      y.push({ i: w - 4, x: et(l, f) }, { i: w - 2, x: et(d, p) });
    } else (f !== 1 || p !== 1) && h.push(r(h) + "scale(" + f + "," + p + ")");
  }
  return function(l, d) {
    var f = [], p = [];
    return l = e(l), d = e(d), s(l.translateX, l.translateY, d.translateX, d.translateY, f, p), a(l.rotate, d.rotate, f, p), c(l.skewX, d.skewX, f, p), u(l.scaleX, l.scaleY, d.scaleX, d.scaleY, f, p), l = d = null, function(h) {
      for (var y = -1, w = p.length, x; ++y < w; ) f[(x = p[y]).i] = x.x(h);
      return f.join("");
    };
  };
}
var im = Gl(tm, "px, ", "px)", "deg)"), rm = Gl(nm, ", ", ")", ")"), om = 1e-12;
function wa(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function sm(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function am(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const Di = (function e(t, n, i) {
  function r(s, a) {
    var c = s[0], u = s[1], l = s[2], d = a[0], f = a[1], p = a[2], h = d - c, y = f - u, w = h * h + y * y, x, m;
    if (w < om)
      m = Math.log(p / l) / t, x = function(S) {
        return [
          c + S * h,
          u + S * y,
          l * Math.exp(t * S * m)
        ];
      };
    else {
      var b = Math.sqrt(w), g = (p * p - l * l + i * w) / (2 * l * n * b), v = (p * p - l * l - i * w) / (2 * p * n * b), j = Math.log(Math.sqrt(g * g + 1) - g), N = Math.log(Math.sqrt(v * v + 1) - v);
      m = (N - j) / t, x = function(S) {
        var k = S * m, _ = wa(j), M = l / (n * b) * (_ * am(t * k + j) - sm(j));
        return [
          c + M * h,
          u + M * y,
          l * _ / wa(t * k + j)
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
var tn = 0, jn = 0, xn = 0, Jl = 1e3, Xi, Sn, qi = 0, _t = 0, ar = 0, Hn = typeof performance == "object" && performance.now ? performance : Date, Ql = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function ds() {
  return _t || (Ql(cm), _t = Hn.now() + ar);
}
function cm() {
  _t = 0;
}
function Yi() {
  this._call = this._time = this._next = null;
}
Yi.prototype = eu.prototype = {
  constructor: Yi,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? ds() : +n) + (t == null ? 0 : +t), !this._next && Sn !== this && (Sn ? Sn._next = this : Xi = this, Sn = this), this._call = e, this._time = n, Do();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Do());
  }
};
function eu(e, t, n) {
  var i = new Yi();
  return i.restart(e, t, n), i;
}
function lm() {
  ds(), ++tn;
  for (var e = Xi, t; e; )
    (t = _t - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --tn;
}
function va() {
  _t = (qi = Hn.now()) + ar, tn = jn = 0;
  try {
    lm();
  } finally {
    tn = 0, dm(), _t = 0;
  }
}
function um() {
  var e = Hn.now(), t = e - qi;
  t > Jl && (ar -= t, qi = e);
}
function dm() {
  for (var e, t = Xi, n, i = 1 / 0; t; )
    t._call ? (i > t._time && (i = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Xi = n);
  Sn = e, Do(i);
}
function Do(e) {
  if (!tn) {
    jn && (jn = clearTimeout(jn));
    var t = e - _t;
    t > 24 ? (e < 1 / 0 && (jn = setTimeout(va, e - Hn.now() - ar)), xn && (xn = clearInterval(xn))) : (xn || (qi = Hn.now(), xn = setInterval(um, Jl)), tn = 1, Ql(va));
  }
}
function ba(e, t, n) {
  var i = new Yi();
  return t = t == null ? 0 : +t, i.restart((r) => {
    i.stop(), e(r + t);
  }, t, n), i;
}
var fm = or("start", "end", "cancel", "interrupt"), pm = [], tu = 0, Na = 1, To = 2, Ti = 3, ja = 4, $o = 5, $i = 6;
function cr(e, t, n, i, r, s) {
  var a = e.__transition;
  if (!a) e.__transition = {};
  else if (n in a) return;
  hm(e, n, {
    name: t,
    index: i,
    // For context during callback.
    group: r,
    // For context during callback.
    on: fm,
    tween: pm,
    time: s.time,
    delay: s.delay,
    duration: s.duration,
    ease: s.ease,
    timer: null,
    state: tu
  });
}
function fs(e, t) {
  var n = Qe(e, t);
  if (n.state > tu) throw new Error("too late; already scheduled");
  return n;
}
function rt(e, t) {
  var n = Qe(e, t);
  if (n.state > Ti) throw new Error("too late; already running");
  return n;
}
function Qe(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function hm(e, t, n) {
  var i = e.__transition, r;
  i[t] = n, n.timer = eu(s, 0, n.time);
  function s(l) {
    n.state = Na, n.timer.restart(a, n.delay, n.time), n.delay <= l && a(l - n.delay);
  }
  function a(l) {
    var d, f, p, h;
    if (n.state !== Na) return u();
    for (d in i)
      if (h = i[d], h.name === n.name) {
        if (h.state === Ti) return ba(a);
        h.state === ja ? (h.state = $i, h.timer.stop(), h.on.call("interrupt", e, e.__data__, h.index, h.group), delete i[d]) : +d < t && (h.state = $i, h.timer.stop(), h.on.call("cancel", e, e.__data__, h.index, h.group), delete i[d]);
      }
    if (ba(function() {
      n.state === Ti && (n.state = ja, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = To, n.on.call("start", e, e.__data__, n.index, n.group), n.state === To) {
      for (n.state = Ti, r = new Array(p = n.tween.length), d = 0, f = -1; d < p; ++d)
        (h = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (r[++f] = h);
      r.length = f + 1;
    }
  }
  function c(l) {
    for (var d = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(u), n.state = $o, 1), f = -1, p = r.length; ++f < p; )
      r[f].call(e, d);
    n.state === $o && (n.on.call("end", e, e.__data__, n.index, n.group), u());
  }
  function u() {
    n.state = $i, n.timer.stop(), delete i[t];
    for (var l in i) return;
    delete e.__transition;
  }
}
function Pi(e, t) {
  var n = e.__transition, i, r, s = !0, a;
  if (n) {
    t = t == null ? null : t + "";
    for (a in n) {
      if ((i = n[a]).name !== t) {
        s = !1;
        continue;
      }
      r = i.state > To && i.state < $o, i.state = $i, i.timer.stop(), i.on.call(r ? "interrupt" : "cancel", e, e.__data__, i.index, i.group), delete n[a];
    }
    s && delete e.__transition;
  }
}
function gm(e) {
  return this.each(function() {
    Pi(this, e);
  });
}
function ym(e, t) {
  var n, i;
  return function() {
    var r = rt(this, e), s = r.tween;
    if (s !== n) {
      i = n = s;
      for (var a = 0, c = i.length; a < c; ++a)
        if (i[a].name === t) {
          i = i.slice(), i.splice(a, 1);
          break;
        }
    }
    r.tween = i;
  };
}
function mm(e, t, n) {
  var i, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var s = rt(this, e), a = s.tween;
    if (a !== i) {
      r = (i = a).slice();
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
function xm(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var i = Qe(this.node(), n).tween, r = 0, s = i.length, a; r < s; ++r)
      if ((a = i[r]).name === e)
        return a.value;
    return null;
  }
  return this.each((t == null ? ym : mm)(n, e, t));
}
function ps(e, t, n) {
  var i = e._id;
  return e.each(function() {
    var r = rt(this, i);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return Qe(r, i).value[t];
  };
}
function nu(e, t) {
  var n;
  return (typeof t == "number" ? et : t instanceof At ? Ki : (n = At(t)) ? (t = n, Ki) : Ul)(e, t);
}
function wm(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function vm(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function bm(e, t, n) {
  var i, r = n + "", s;
  return function() {
    var a = this.getAttribute(e);
    return a === r ? null : a === i ? s : s = t(i = a, n);
  };
}
function Nm(e, t, n) {
  var i, r = n + "", s;
  return function() {
    var a = this.getAttributeNS(e.space, e.local);
    return a === r ? null : a === i ? s : s = t(i = a, n);
  };
}
function jm(e, t, n) {
  var i, r, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttribute(e) : (a = this.getAttribute(e), u = c + "", a === u ? null : a === i && u === r ? s : (r = u, s = t(i = a, c)));
  };
}
function Sm(e, t, n) {
  var i, r, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (a = this.getAttributeNS(e.space, e.local), u = c + "", a === u ? null : a === i && u === r ? s : (r = u, s = t(i = a, c)));
  };
}
function Cm(e, t) {
  var n = sr(e), i = n === "transform" ? rm : nu;
  return this.attrTween(e, typeof t == "function" ? (n.local ? Sm : jm)(n, i, ps(this, "attr." + e, t)) : t == null ? (n.local ? vm : wm)(n) : (n.local ? Nm : bm)(n, i, t));
}
function Em(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function km(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function Im(e, t) {
  var n, i;
  function r() {
    var s = t.apply(this, arguments);
    return s !== i && (n = (i = s) && km(e, s)), n;
  }
  return r._value = t, r;
}
function Am(e, t) {
  var n, i;
  function r() {
    var s = t.apply(this, arguments);
    return s !== i && (n = (i = s) && Em(e, s)), n;
  }
  return r._value = t, r;
}
function _m(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var i = sr(e);
  return this.tween(n, (i.local ? Im : Am)(i, t));
}
function Dm(e, t) {
  return function() {
    fs(this, e).delay = +t.apply(this, arguments);
  };
}
function Tm(e, t) {
  return t = +t, function() {
    fs(this, e).delay = t;
  };
}
function $m(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Dm : Tm)(t, e)) : Qe(this.node(), t).delay;
}
function Pm(e, t) {
  return function() {
    rt(this, e).duration = +t.apply(this, arguments);
  };
}
function Mm(e, t) {
  return t = +t, function() {
    rt(this, e).duration = t;
  };
}
function Rm(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Pm : Mm)(t, e)) : Qe(this.node(), t).duration;
}
function Lm(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    rt(this, e).ease = t;
  };
}
function zm(e) {
  var t = this._id;
  return arguments.length ? this.each(Lm(t, e)) : Qe(this.node(), t).ease;
}
function Vm(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    rt(this, e).ease = n;
  };
}
function Om(e) {
  if (typeof e != "function") throw new Error();
  return this.each(Vm(this._id, e));
}
function Hm(e) {
  typeof e != "function" && (e = $l(e));
  for (var t = this._groups, n = t.length, i = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = i[r] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new dt(i, this._parents, this._name, this._id);
}
function Wm(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, i = t.length, r = n.length, s = Math.min(i, r), a = new Array(i), c = 0; c < s; ++c)
    for (var u = t[c], l = n[c], d = u.length, f = a[c] = new Array(d), p, h = 0; h < d; ++h)
      (p = u[h] || l[h]) && (f[h] = p);
  for (; c < i; ++c)
    a[c] = t[c];
  return new dt(a, this._parents, this._name, this._id);
}
function Bm(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function Fm(e, t, n) {
  var i, r, s = Bm(t) ? fs : rt;
  return function() {
    var a = s(this, e), c = a.on;
    c !== i && (r = (i = c).copy()).on(t, n), a.on = r;
  };
}
function Km(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Qe(this.node(), n).on.on(e) : this.each(Fm(n, e, t));
}
function Xm(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function qm() {
  return this.on("end.remove", Xm(this._id));
}
function Ym(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = as(e));
  for (var i = this._groups, r = i.length, s = new Array(r), a = 0; a < r; ++a)
    for (var c = i[a], u = c.length, l = s[a] = new Array(u), d, f, p = 0; p < u; ++p)
      (d = c[p]) && (f = e.call(d, d.__data__, p, c)) && ("__data__" in d && (f.__data__ = d.__data__), l[p] = f, cr(l[p], t, n, p, l, Qe(d, n)));
  return new dt(s, this._parents, t, n);
}
function Um(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Tl(e));
  for (var i = this._groups, r = i.length, s = [], a = [], c = 0; c < r; ++c)
    for (var u = i[c], l = u.length, d, f = 0; f < l; ++f)
      if (d = u[f]) {
        for (var p = e.call(d, d.__data__, f, u), h, y = Qe(d, n), w = 0, x = p.length; w < x; ++w)
          (h = p[w]) && cr(h, t, n, w, p, y);
        s.push(p), a.push(d);
      }
  return new dt(s, a, t, n);
}
var Zm = Jn.prototype.constructor;
function Gm() {
  return new Zm(this._groups, this._parents);
}
function Jm(e, t) {
  var n, i, r;
  return function() {
    var s = en(this, e), a = (this.style.removeProperty(e), en(this, e));
    return s === a ? null : s === n && a === i ? r : r = t(n = s, i = a);
  };
}
function iu(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Qm(e, t, n) {
  var i, r = n + "", s;
  return function() {
    var a = en(this, e);
    return a === r ? null : a === i ? s : s = t(i = a, n);
  };
}
function ex(e, t, n) {
  var i, r, s;
  return function() {
    var a = en(this, e), c = n(this), u = c + "";
    return c == null && (u = c = (this.style.removeProperty(e), en(this, e))), a === u ? null : a === i && u === r ? s : (r = u, s = t(i = a, c));
  };
}
function tx(e, t) {
  var n, i, r, s = "style." + t, a = "end." + s, c;
  return function() {
    var u = rt(this, e), l = u.on, d = u.value[s] == null ? c || (c = iu(t)) : void 0;
    (l !== n || r !== d) && (i = (n = l).copy()).on(a, r = d), u.on = i;
  };
}
function nx(e, t, n) {
  var i = (e += "") == "transform" ? im : nu;
  return t == null ? this.styleTween(e, Jm(e, i)).on("end.style." + e, iu(e)) : typeof t == "function" ? this.styleTween(e, ex(e, i, ps(this, "style." + e, t))).each(tx(this._id, e)) : this.styleTween(e, Qm(e, i, t), n).on("end.style." + e, null);
}
function ix(e, t, n) {
  return function(i) {
    this.style.setProperty(e, t.call(this, i), n);
  };
}
function rx(e, t, n) {
  var i, r;
  function s() {
    var a = t.apply(this, arguments);
    return a !== r && (i = (r = a) && ix(e, a, n)), i;
  }
  return s._value = t, s;
}
function ox(e, t, n) {
  var i = "style." + (e += "");
  if (arguments.length < 2) return (i = this.tween(i)) && i._value;
  if (t == null) return this.tween(i, null);
  if (typeof t != "function") throw new Error();
  return this.tween(i, rx(e, t, n ?? ""));
}
function sx(e) {
  return function() {
    this.textContent = e;
  };
}
function ax(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function cx(e) {
  return this.tween("text", typeof e == "function" ? ax(ps(this, "text", e)) : sx(e == null ? "" : e + ""));
}
function lx(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function ux(e) {
  var t, n;
  function i() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && lx(r)), t;
  }
  return i._value = e, i;
}
function dx(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, ux(e));
}
function fx() {
  for (var e = this._name, t = this._id, n = ru(), i = this._groups, r = i.length, s = 0; s < r; ++s)
    for (var a = i[s], c = a.length, u, l = 0; l < c; ++l)
      if (u = a[l]) {
        var d = Qe(u, t);
        cr(u, e, n, l, a, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new dt(i, this._parents, e, n);
}
function px() {
  var e, t, n = this, i = n._id, r = n.size();
  return new Promise(function(s, a) {
    var c = { value: a }, u = { value: function() {
      --r === 0 && s();
    } };
    n.each(function() {
      var l = rt(this, i), d = l.on;
      d !== e && (t = (e = d).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(u)), l.on = t;
    }), r === 0 && s();
  });
}
var hx = 0;
function dt(e, t, n, i) {
  this._groups = e, this._parents = t, this._name = n, this._id = i;
}
function ru() {
  return ++hx;
}
var at = Jn.prototype;
dt.prototype = {
  constructor: dt,
  select: Ym,
  selectAll: Um,
  selectChild: at.selectChild,
  selectChildren: at.selectChildren,
  filter: Hm,
  merge: Wm,
  selection: Gm,
  transition: fx,
  call: at.call,
  nodes: at.nodes,
  node: at.node,
  size: at.size,
  empty: at.empty,
  each: at.each,
  on: Km,
  attr: Cm,
  attrTween: _m,
  style: nx,
  styleTween: ox,
  text: cx,
  textTween: dx,
  remove: qm,
  tween: xm,
  delay: $m,
  duration: Rm,
  ease: zm,
  easeVarying: Om,
  end: px,
  [Symbol.iterator]: at[Symbol.iterator]
};
function gx(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var yx = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: gx
};
function mx(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function xx(e) {
  var t, n;
  e instanceof dt ? (t = e._id, e = e._name) : (t = ru(), (n = yx).time = ds(), e = e == null ? null : e + "");
  for (var i = this._groups, r = i.length, s = 0; s < r; ++s)
    for (var a = i[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && cr(u, e, t, l, a, n || mx(u, t));
  return new dt(i, this._parents, e, t);
}
Jn.prototype.interrupt = gm;
Jn.prototype.transition = xx;
const vi = (e) => () => e;
function wx(e, {
  sourceEvent: t,
  target: n,
  transform: i,
  dispatch: r
}) {
  Object.defineProperties(this, {
    type: { value: e, enumerable: !0, configurable: !0 },
    sourceEvent: { value: t, enumerable: !0, configurable: !0 },
    target: { value: n, enumerable: !0, configurable: !0 },
    transform: { value: i, enumerable: !0, configurable: !0 },
    _: { value: r }
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
var lr = new lt(1, 0, 0);
ou.prototype = lt.prototype;
function ou(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return lr;
  return e.__zoom;
}
function co(e) {
  e.stopImmediatePropagation();
}
function wn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function vx(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function bx() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function Sa() {
  return this.__zoom || lr;
}
function Nx(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function jx() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Sx(e, t, n) {
  var i = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], s = e.invertY(t[0][1]) - n[0][1], a = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > i ? (i + r) / 2 : Math.min(0, i) || Math.max(0, r),
    a > s ? (s + a) / 2 : Math.min(0, s) || Math.max(0, a)
  );
}
function su() {
  var e = vx, t = bx, n = Sx, i = Nx, r = jx, s = [0, 1 / 0], a = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, u = Di, l = or("start", "zoom", "end"), d, f, p, h = 500, y = 150, w = 0, x = 10;
  function m(C) {
    C.property("__zoom", Sa).on("wheel.zoom", k, { passive: !1 }).on("mousedown.zoom", _).on("dblclick.zoom", M).filter(r).on("touchstart.zoom", I).on("touchmove.zoom", $).on("touchend.zoom touchcancel.zoom", z).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  m.transform = function(C, A, E, D) {
    var P = C.selection ? C.selection() : C;
    P.property("__zoom", Sa), C !== P ? j(C, A, E, D) : P.interrupt().each(function() {
      N(this, arguments).event(D).start().zoom(null, typeof A == "function" ? A.apply(this, arguments) : A).end();
    });
  }, m.scaleBy = function(C, A, E, D) {
    m.scaleTo(C, function() {
      var P = this.__zoom.k, T = typeof A == "function" ? A.apply(this, arguments) : A;
      return P * T;
    }, E, D);
  }, m.scaleTo = function(C, A, E, D) {
    m.transform(C, function() {
      var P = t.apply(this, arguments), T = this.__zoom, B = E == null ? v(P) : typeof E == "function" ? E.apply(this, arguments) : E, W = T.invert(B), H = typeof A == "function" ? A.apply(this, arguments) : A;
      return n(g(b(T, H), B, W), P, a);
    }, E, D);
  }, m.translateBy = function(C, A, E, D) {
    m.transform(C, function() {
      return n(this.__zoom.translate(
        typeof A == "function" ? A.apply(this, arguments) : A,
        typeof E == "function" ? E.apply(this, arguments) : E
      ), t.apply(this, arguments), a);
    }, null, D);
  }, m.translateTo = function(C, A, E, D, P) {
    m.transform(C, function() {
      var T = t.apply(this, arguments), B = this.__zoom, W = D == null ? v(T) : typeof D == "function" ? D.apply(this, arguments) : D;
      return n(lr.translate(W[0], W[1]).scale(B.k).translate(
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
  function v(C) {
    return [(+C[0][0] + +C[1][0]) / 2, (+C[0][1] + +C[1][1]) / 2];
  }
  function j(C, A, E, D) {
    C.on("start.zoom", function() {
      N(this, arguments).event(D).start();
    }).on("interrupt.zoom end.zoom", function() {
      N(this, arguments).event(D).end();
    }).tween("zoom", function() {
      var P = this, T = arguments, B = N(P, T).event(D), W = t.apply(P, T), H = E == null ? v(W) : typeof E == "function" ? E.apply(P, T) : E, Y = Math.max(W[1][0] - W[0][0], W[1][1] - W[0][1]), V = P.__zoom, q = typeof A == "function" ? A.apply(P, T) : A, G = u(V.invert(H).concat(Y / V.k), q.invert(H).concat(Y / q.k));
      return function(Z) {
        if (Z === 1) Z = q;
        else {
          var R = G(Z), X = Y / R[2];
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
      var A = We(this.that).datum();
      l.call(
        C,
        this.that,
        new wx(C, {
          sourceEvent: this.sourceEvent,
          target: m,
          transform: this.that.__zoom,
          dispatch: l
        }),
        A
      );
    }
  };
  function k(C, ...A) {
    if (!e.apply(this, arguments)) return;
    var E = N(this, A).event(C), D = this.__zoom, P = Math.max(s[0], Math.min(s[1], D.k * Math.pow(2, i.apply(this, arguments)))), T = Ye(C);
    if (E.wheel)
      (E.mouse[0][0] !== T[0] || E.mouse[0][1] !== T[1]) && (E.mouse[1] = D.invert(E.mouse[0] = T)), clearTimeout(E.wheel);
    else {
      if (D.k === P) return;
      E.mouse = [T, D.invert(T)], Pi(this), E.start();
    }
    wn(C), E.wheel = setTimeout(B, y), E.zoom("mouse", n(g(b(D, P), E.mouse[0], E.mouse[1]), E.extent, a));
    function B() {
      E.wheel = null, E.end();
    }
  }
  function _(C, ...A) {
    if (p || !e.apply(this, arguments)) return;
    var E = C.currentTarget, D = N(this, A, !0).event(C), P = We(C.view).on("mousemove.zoom", H, !0).on("mouseup.zoom", Y, !0), T = Ye(C, E), B = C.clientX, W = C.clientY;
    Bl(C.view), co(C), D.mouse = [T, this.__zoom.invert(T)], Pi(this), D.start();
    function H(V) {
      if (wn(V), !D.moved) {
        var q = V.clientX - B, G = V.clientY - W;
        D.moved = q * q + G * G > w;
      }
      D.event(V).zoom("mouse", n(g(D.that.__zoom, D.mouse[0] = Ye(V, E), D.mouse[1]), D.extent, a));
    }
    function Y(V) {
      P.on("mousemove.zoom mouseup.zoom", null), Fl(V.view, D.moved), wn(V), D.event(V).end();
    }
  }
  function M(C, ...A) {
    if (e.apply(this, arguments)) {
      var E = this.__zoom, D = Ye(C.changedTouches ? C.changedTouches[0] : C, this), P = E.invert(D), T = E.k * (C.shiftKey ? 0.5 : 2), B = n(g(b(E, T), D, P), t.apply(this, A), a);
      wn(C), c > 0 ? We(this).transition().duration(c).call(j, B, D, C) : We(this).call(m.transform, B, D, C);
    }
  }
  function I(C, ...A) {
    if (e.apply(this, arguments)) {
      var E = C.touches, D = E.length, P = N(this, A, C.changedTouches.length === D).event(C), T, B, W, H;
      for (co(C), B = 0; B < D; ++B)
        W = E[B], H = Ye(W, this), H = [H, this.__zoom.invert(H), W.identifier], P.touch0 ? !P.touch1 && P.touch0[2] !== H[2] && (P.touch1 = H, P.taps = 0) : (P.touch0 = H, T = !0, P.taps = 1 + !!d);
      d && (d = clearTimeout(d)), T && (P.taps < 2 && (f = H[0], d = setTimeout(function() {
        d = null;
      }, h)), Pi(this), P.start());
    }
  }
  function $(C, ...A) {
    if (this.__zooming) {
      var E = N(this, A).event(C), D = C.changedTouches, P = D.length, T, B, W, H;
      for (wn(C), T = 0; T < P; ++T)
        B = D[T], W = Ye(B, this), E.touch0 && E.touch0[2] === B.identifier ? E.touch0[0] = W : E.touch1 && E.touch1[2] === B.identifier && (E.touch1[0] = W);
      if (B = E.that.__zoom, E.touch1) {
        var Y = E.touch0[0], V = E.touch0[1], q = E.touch1[0], G = E.touch1[1], Z = (Z = q[0] - Y[0]) * Z + (Z = q[1] - Y[1]) * Z, R = (R = G[0] - V[0]) * R + (R = G[1] - V[1]) * R;
        B = b(B, Math.sqrt(Z / R)), W = [(Y[0] + q[0]) / 2, (Y[1] + q[1]) / 2], H = [(V[0] + G[0]) / 2, (V[1] + G[1]) / 2];
      } else if (E.touch0) W = E.touch0[0], H = E.touch0[1];
      else return;
      E.zoom("touch", n(g(B, W, H), E.extent, a));
    }
  }
  function z(C, ...A) {
    if (this.__zooming) {
      var E = N(this, A).event(C), D = C.changedTouches, P = D.length, T, B;
      for (co(C), p && clearTimeout(p), p = setTimeout(function() {
        p = null;
      }, h), T = 0; T < P; ++T)
        B = D[T], E.touch0 && E.touch0[2] === B.identifier ? delete E.touch0 : E.touch1 && E.touch1[2] === B.identifier && delete E.touch1;
      if (E.touch1 && !E.touch0 && (E.touch0 = E.touch1, delete E.touch1), E.touch0) E.touch0[1] = this.__zoom.invert(E.touch0[0]);
      else if (E.end(), E.taps === 2 && (B = Ye(B, this), Math.hypot(f[0] - B[0], f[1] - B[1]) < x)) {
        var W = We(this).on("dblclick.zoom");
        W && W.apply(this, arguments);
      }
    }
  }
  return m.wheelDelta = function(C) {
    return arguments.length ? (i = typeof C == "function" ? C : vi(+C), m) : i;
  }, m.filter = function(C) {
    return arguments.length ? (e = typeof C == "function" ? C : vi(!!C), m) : e;
  }, m.touchable = function(C) {
    return arguments.length ? (r = typeof C == "function" ? C : vi(!!C), m) : r;
  }, m.extent = function(C) {
    return arguments.length ? (t = typeof C == "function" ? C : vi([[+C[0][0], +C[0][1]], [+C[1][0], +C[1][1]]]), m) : t;
  }, m.scaleExtent = function(C) {
    return arguments.length ? (s[0] = +C[0], s[1] = +C[1], m) : [s[0], s[1]];
  }, m.translateExtent = function(C) {
    return arguments.length ? (a[0][0] = +C[0][0], a[1][0] = +C[1][0], a[0][1] = +C[0][1], a[1][1] = +C[1][1], m) : [[a[0][0], a[0][1]], [a[1][0], a[1][1]]];
  }, m.constrain = function(C) {
    return arguments.length ? (n = C, m) : n;
  }, m.duration = function(C) {
    return arguments.length ? (c = +C, m) : c;
  }, m.interpolate = function(C) {
    return arguments.length ? (u = C, m) : u;
  }, m.on = function() {
    var C = l.on.apply(l, arguments);
    return C === l ? m : C;
  }, m.clickDistance = function(C) {
    return arguments.length ? (w = (C = +C) * C, m) : Math.sqrt(w);
  }, m.tapDistance = function(C) {
    return arguments.length ? (x = +C, m) : x;
  }, m;
}
const Ke = {
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
], au = ["Enter", " ", "Escape"], cu = {
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
const lu = {
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
var Ui;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(Ui || (Ui = {}));
var oe;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(oe || (oe = {}));
const Ca = {
  [oe.Left]: oe.Right,
  [oe.Right]: oe.Left,
  [oe.Top]: oe.Bottom,
  [oe.Bottom]: oe.Top
};
function uu(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const du = (e) => "id" in e && "source" in e && "target" in e, Cx = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), hs = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), ei = (e, t = [0, 0]) => {
  const { width: n, height: i } = ft(e), r = e.origin ?? t, s = n * r[0], a = i * r[1];
  return {
    x: e.position.x - s,
    y: e.position.y - a
  };
}, Ex = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((i, r) => {
    const s = typeof r == "string";
    let a = !t.nodeLookup && !s ? r : void 0;
    t.nodeLookup && (a = s ? t.nodeLookup.get(r) : hs(r) ? r : t.nodeLookup.get(r.id));
    const c = a ? Zi(a, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return ur(i, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return dr(n);
}, ti = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, i = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = ur(n, Zi(r)), i = !0);
  }), i ? dr(n) : { x: 0, y: 0, width: 0, height: 0 };
}, gs = (e, t, [n, i, r] = [0, 0, 1], s = !1, a = !1) => {
  const c = {
    ...fn(t, [n, i, r]),
    width: t.width / r,
    height: t.height / r
  }, u = [];
  for (const l of e.values()) {
    const { measured: d, selectable: f = !0, hidden: p = !1 } = l;
    if (a && !f || p)
      continue;
    const h = d.width ?? l.width ?? l.initialWidth ?? null, y = d.height ?? l.height ?? l.initialHeight ?? null, w = Fn(c, on(l)), x = (h ?? 0) * (y ?? 0), m = s && w > 0;
    (!l.internals.handleBounds || m || w >= x || l.dragging) && u.push(l);
  }
  return u;
}, kx = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((i) => {
    n.add(i.id);
  }), t.filter((i) => n.has(i.source) || n.has(i.target));
};
function Ix(e, t) {
  const n = /* @__PURE__ */ new Map(), i = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!i || i.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function Ax({ nodes: e, width: t, height: n, panZoom: i, minZoom: r, maxZoom: s }, a) {
  if (e.size === 0)
    return !0;
  const c = Ix(e, a), u = ti(c), l = ms(u, t, n, a?.minZoom ?? r, a?.maxZoom ?? s, a?.padding ?? 0.1);
  return await i.setViewport(l, {
    duration: a?.duration,
    ease: a?.ease,
    interpolate: a?.interpolate
  }), !0;
}
function fu({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: i = [0, 0], nodeExtent: r, onError: s }) {
  const a = n.get(e), c = a.parentId ? n.get(a.parentId) : void 0, { x: u, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, d = a.origin ?? i;
  let f = a.extent || r;
  if (a.extent === "parent" && !a.expandParent)
    if (!c)
      s?.("005", Ke.error005());
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
  return (a.measured.width === void 0 || a.measured.height === void 0) && s?.("015", Ke.error015()), {
    position: {
      x: p.x - u + (a.measured.width ?? 0) * d[0],
      y: p.y - l + (a.measured.height ?? 0) * d[1]
    },
    positionAbsolute: p
  };
}
async function _x({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: i, onBeforeDelete: r }) {
  const s = new Set(e.map((p) => p.id)), a = [];
  for (const p of n) {
    if (p.deletable === !1)
      continue;
    const h = s.has(p.id), y = !h && p.parentId && a.find((w) => w.id === p.parentId);
    (h || y) && a.push(p);
  }
  const c = new Set(t.map((p) => p.id)), u = i.filter((p) => p.deletable !== !1), d = kx(a, u);
  for (const p of u)
    c.has(p.id) && !d.find((y) => y.id === p.id) && d.push(p);
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
const rn = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), Dt = (e = { x: 0, y: 0 }, t, n) => ({
  x: rn(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: rn(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function pu(e, t, n) {
  const { width: i, height: r } = ft(n), { x: s, y: a } = n.internals.positionAbsolute;
  return Dt(e, [
    [s, a],
    [s + i, a + r]
  ], t);
}
const Ea = (e, t, n) => e < t ? rn(Math.abs(e - t), 1, t) / t : e > n ? -rn(Math.abs(e - n), 1, t) / t : 0, ys = (e, t, n = 15, i = 40) => {
  const r = Ea(e.x, i, t.width - i) * n, s = Ea(e.y, i, t.height - i) * n;
  return [r, s];
}, ur = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), Po = ({ x: e, y: t, width: n, height: i }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + i
}), dr = ({ x: e, y: t, x2: n, y2: i }) => ({
  x: e,
  y: t,
  width: n - e,
  height: i - t
}), on = (e, t = [0, 0]) => {
  const { x: n, y: i } = hs(e) ? e.internals.positionAbsolute : ei(e, t);
  return {
    x: n,
    y: i,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, Zi = (e, t = [0, 0]) => {
  const { x: n, y: i } = hs(e) ? e.internals.positionAbsolute : ei(e, t);
  return {
    x: n,
    y: i,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: i + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, hu = (e, t) => dr(ur(Po(e), Po(t))), Fn = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), i = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * i);
}, ka = (e) => Ze(e.width) && Ze(e.height) && Ze(e.x) && Ze(e.y), Ze = (e) => !isNaN(e) && isFinite(e), gu = (e, t) => (n, i) => {
}, ni = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), fn = ({ x: e, y: t }, [n, i, r], s = !1, a = [1, 1]) => {
  const c = {
    x: (e - n) / r,
    y: (t - i) / r
  };
  return s ? ni(c, a) : c;
}, sn = ({ x: e, y: t }, [n, i, r]) => ({
  x: e * r + n,
  y: t * r + i
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
function Dx(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const i = Bt(e, n), r = Bt(e, t);
    return {
      top: i,
      right: r,
      bottom: i,
      left: r,
      x: r * 2,
      y: i * 2
    };
  }
  if (typeof e == "object") {
    const i = Bt(e.top ?? e.y ?? 0, n), r = Bt(e.bottom ?? e.y ?? 0, n), s = Bt(e.left ?? e.x ?? 0, t), a = Bt(e.right ?? e.x ?? 0, t);
    return { top: i, right: a, bottom: r, left: s, x: s + a, y: i + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function Tx(e, t, n, i, r, s) {
  const { x: a, y: c } = sn(e, [t, n, i]), { x: u, y: l } = sn({ x: e.x + e.width, y: e.y + e.height }, [t, n, i]), d = r - u, f = s - l;
  return {
    left: Math.floor(a),
    top: Math.floor(c),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const ms = (e, t, n, i, r, s) => {
  const a = Dx(s, t, n), c = (t - a.x) / e.width, u = (n - a.y) / e.height, l = Math.min(c, u), d = rn(l, i, r), f = e.x + e.width / 2, p = e.y + e.height / 2, h = t / 2 - f * d, y = n / 2 - p * d, w = Tx(e, h, y, d, t, n), x = {
    left: Math.min(w.left - a.left, 0),
    top: Math.min(w.top - a.top, 0),
    right: Math.min(w.right - a.right, 0),
    bottom: Math.min(w.bottom - a.bottom, 0)
  };
  return {
    x: h - x.left + x.right,
    y: y - x.top + x.bottom,
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
function yu(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function mu(e, t = { width: 0, height: 0 }, n, i, r) {
  const s = { ...e }, a = i.get(n);
  if (a) {
    const c = a.origin || r;
    s.x += a.internals.positionAbsolute.x - (t.width ?? 0) * c[0], s.y += a.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return s;
}
function Ia(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function $x() {
  let e, t;
  return { promise: new Promise((i, r) => {
    e = i, t = r;
  }), resolve: e, reject: t };
}
function Px(e) {
  return { ...cu, ...e || {} };
}
function _n(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: i, containerBounds: r }) {
  const { x: s, y: a } = Ge(e), c = fn({ x: s - (r?.left ?? 0), y: a - (r?.top ?? 0) }, i), { x: u, y: l } = n ? ni(c, t) : c;
  return {
    xSnapped: u,
    ySnapped: l,
    ...c
  };
}
const xs = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), xu = (e) => e?.getRootNode?.() || window?.document, Mx = ["INPUT", "SELECT", "TEXTAREA"];
function wu(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : Mx.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const vu = (e) => "clientX" in e, Ge = (e, t) => {
  const n = vu(e), i = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: i - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, Aa = (e, t, n, i, r) => {
  const s = t.querySelectorAll(`.${e}`);
  return !s || !s.length ? null : Array.from(s).map((a) => {
    const c = a.getBoundingClientRect();
    return {
      id: a.getAttribute("data-handleid"),
      type: e,
      nodeId: r,
      position: a.getAttribute("data-handlepos"),
      x: (c.left - n.left) / i,
      y: (c.top - n.top) / i,
      ...xs(a)
    };
  });
};
function bu({ sourceX: e, sourceY: t, targetX: n, targetY: i, sourceControlX: r, sourceControlY: s, targetControlX: a, targetControlY: c }) {
  const u = e * 0.125 + r * 0.375 + a * 0.375 + n * 0.125, l = t * 0.125 + s * 0.375 + c * 0.375 + i * 0.125, d = Math.abs(u - e), f = Math.abs(l - t);
  return [u, l, d, f];
}
function bi(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function _a({ pos: e, x1: t, y1: n, x2: i, y2: r, c: s }) {
  switch (e) {
    case oe.Left:
      return [t - bi(t - i, s), n];
    case oe.Right:
      return [t + bi(i - t, s), n];
    case oe.Top:
      return [t, n - bi(n - r, s)];
    case oe.Bottom:
      return [t, n + bi(r - n, s)];
  }
}
function Nu({ sourceX: e, sourceY: t, sourcePosition: n = oe.Bottom, targetX: i, targetY: r, targetPosition: s = oe.Top, curvature: a = 0.25 }) {
  const [c, u] = _a({
    pos: n,
    x1: e,
    y1: t,
    x2: i,
    y2: r,
    c: a
  }), [l, d] = _a({
    pos: s,
    x1: i,
    y1: r,
    x2: e,
    y2: t,
    c: a
  }), [f, p, h, y] = bu({
    sourceX: e,
    sourceY: t,
    targetX: i,
    targetY: r,
    sourceControlX: c,
    sourceControlY: u,
    targetControlX: l,
    targetControlY: d
  });
  return [
    `M${e},${t} C${c},${u} ${l},${d} ${i},${r}`,
    f,
    p,
    h,
    y
  ];
}
function ju({ sourceX: e, sourceY: t, targetX: n, targetY: i }) {
  const r = Math.abs(n - e) / 2, s = n < e ? n + r : n - r, a = Math.abs(i - t) / 2, c = i < t ? i + a : i - a;
  return [s, c, r, a];
}
function Rx({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: i = 0, elevateOnSelect: r = !1, zIndexMode: s = "basic" }) {
  if (s === "manual")
    return i;
  const a = r && n ? i + 1e3 : i, c = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return a + c;
}
function Lx({ sourceNode: e, targetNode: t, width: n, height: i, transform: r }) {
  const s = ur(Zi(e), Zi(t));
  s.x === s.x2 && (s.x2 += 1), s.y === s.y2 && (s.y2 += 1);
  const a = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: i / r[2]
  };
  return Fn(a, dr(s)) > 0;
}
const Su = ({ source: e, sourceHandle: t, target: n, targetHandle: i }) => `xy-edge__${e}${t || ""}-${n}${i || ""}`, zx = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), Vx = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Ke.error006()), t;
  const i = n.getEdgeId || Su;
  let r;
  return du(e) ? r = { ...e } : r = {
    ...e,
    id: i(e)
  }, zx(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, Ox = (e, t, n, i = { shouldReplaceId: !0 }) => {
  const { id: r, ...s } = e;
  if (!t.source || !t.target)
    return i.onError?.("006", Ke.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return i.onError?.("007", Ke.error007(r)), n;
  const c = i.getEdgeId || Su, u = {
    ...s,
    id: i.shouldReplaceId ? c(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== r).concat(u);
};
function Cu({ sourceX: e, sourceY: t, targetX: n, targetY: i }) {
  const [r, s, a, c] = ju({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: i
  });
  return [`M ${e},${t}L ${n},${i}`, r, s, a, c];
}
const Da = {
  [oe.Left]: { x: -1, y: 0 },
  [oe.Right]: { x: 1, y: 0 },
  [oe.Top]: { x: 0, y: -1 },
  [oe.Bottom]: { x: 0, y: 1 }
}, Hx = ({ source: e, sourcePosition: t = oe.Bottom, target: n }) => t === oe.Left || t === oe.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, Ta = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Wx({ source: e, sourcePosition: t = oe.Bottom, target: n, targetPosition: i = oe.Top, center: r, offset: s, stepPosition: a }) {
  const c = Da[t], u = Da[i], l = { x: e.x + c.x * s, y: e.y + c.y * s }, d = { x: n.x + u.x * s, y: n.y + u.y * s }, f = Hx({
    source: l,
    sourcePosition: t,
    target: d
  }), p = f.x !== 0 ? "x" : "y", h = f[p];
  let y = [], w, x;
  const m = { x: 0, y: 0 }, b = { x: 0, y: 0 }, [, , g, v] = ju({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[p] * u[p] === -1) {
    p === "x" ? (w = r.x ?? l.x + (d.x - l.x) * a, x = r.y ?? (l.y + d.y) / 2) : (w = r.x ?? (l.x + d.x) / 2, x = r.y ?? l.y + (d.y - l.y) * a);
    const k = [
      { x: w, y: l.y },
      { x: w, y: d.y }
    ], _ = [
      { x: l.x, y: x },
      { x: d.x, y: x }
    ];
    c[p] === h ? y = p === "x" ? k : _ : y = p === "x" ? _ : k;
  } else {
    const k = [{ x: l.x, y: d.y }], _ = [{ x: d.x, y: l.y }];
    if (p === "x" ? y = c.x === h ? _ : k : y = c.y === h ? k : _, t === i) {
      const C = Math.abs(e[p] - n[p]);
      if (C <= s) {
        const A = Math.min(s - 1, s - C);
        c[p] === h ? m[p] = (l[p] > e[p] ? -1 : 1) * A : b[p] = (d[p] > n[p] ? -1 : 1) * A;
      }
    }
    if (t !== i) {
      const C = p === "x" ? "y" : "x", A = c[p] === u[C], E = l[C] > d[C], D = l[C] < d[C];
      (c[p] === 1 && (!A && E || A && D) || c[p] !== 1 && (!A && D || A && E)) && (y = p === "x" ? k : _);
    }
    const M = { x: l.x + m.x, y: l.y + m.y }, I = { x: d.x + b.x, y: d.y + b.y }, $ = Math.max(Math.abs(M.x - y[0].x), Math.abs(I.x - y[0].x)), z = Math.max(Math.abs(M.y - y[0].y), Math.abs(I.y - y[0].y));
    $ >= z ? (w = (M.x + I.x) / 2, x = y[0].y) : (w = y[0].x, x = (M.y + I.y) / 2);
  }
  const j = { x: l.x + m.x, y: l.y + m.y }, N = { x: d.x + b.x, y: d.y + b.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...j.x !== y[0].x || j.y !== y[0].y ? [j] : [],
    ...y,
    ...N.x !== y[y.length - 1].x || N.y !== y[y.length - 1].y ? [N] : [],
    n
  ], w, x, g, v];
}
function Bx(e, t, n, i) {
  const r = Math.min(Ta(e, t) / 2, Ta(t, n) / 2, i), { x: s, y: a } = t;
  if (e.x === s && s === n.x || e.y === a && a === n.y)
    return `L${s} ${a}`;
  if (e.y === a) {
    const l = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${s + r * l},${a}Q ${s},${a} ${s},${a + r * d}`;
  }
  const c = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${s},${a + r * u}Q ${s},${a} ${s + r * c},${a}`;
}
function Gi({ sourceX: e, sourceY: t, sourcePosition: n = oe.Bottom, targetX: i, targetY: r, targetPosition: s = oe.Top, borderRadius: a = 5, centerX: c, centerY: u, offset: l = 20, stepPosition: d = 0.5 }) {
  const [f, p, h, y, w] = Wx({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: i, y: r },
    targetPosition: s,
    center: { x: c, y: u },
    offset: l,
    stepPosition: d
  });
  let x = `M${f[0].x} ${f[0].y}`;
  for (let m = 1; m < f.length - 1; m++)
    x += Bx(f[m - 1], f[m], f[m + 1], a);
  return x += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [x, p, h, y, w];
}
function $a(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function Fx(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!$a(t) || !$a(n))
    return null;
  const i = t.internals.handleBounds || Pa(t.handles), r = n.internals.handleBounds || Pa(n.handles), s = Ma(i?.source ?? [], e.sourceHandle), a = Ma(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === nn.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!s || !a)
    return e.onError?.("008", Ke.error008(s ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = s?.position || oe.Bottom, u = a?.position || oe.Top, l = $t(t, s, c), d = $t(n, a, u);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: d.x,
    targetY: d.y,
    sourcePosition: c,
    targetPosition: u
  };
}
function Pa(e) {
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
function $t(e, t, n = oe.Left, i = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, s = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: a, height: c } = t ?? ft(e);
  if (i)
    return { x: r + a / 2, y: s + c / 2 };
  switch (t?.position ?? n) {
    case oe.Top:
      return { x: r + a / 2, y: s };
    case oe.Right:
      return { x: r + a, y: s + c / 2 };
    case oe.Bottom:
      return { x: r + a / 2, y: s + c };
    case oe.Left:
      return { x: r, y: s + c / 2 };
  }
}
function Ma(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Mo(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((i) => `${i}=${e[i]}`).join("&")}` : "";
}
function Kx(e, { id: t, defaultColor: n, defaultMarkerStart: i, defaultMarkerEnd: r }) {
  const s = /* @__PURE__ */ new Set();
  return e.reduce((a, c) => ([c.markerStart || i, c.markerEnd || r].forEach((u) => {
    if (u && typeof u == "object") {
      const l = Mo(u, t);
      s.has(l) || (a.push({ id: l, color: u.color || n, ...u }), s.add(l));
    }
  }), a), []).sort((a, c) => a.id.localeCompare(c.id));
}
const Eu = 1e3, Xx = 10, ws = {
  nodeOrigin: [0, 0],
  nodeExtent: Wn,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, qx = {
  ...ws,
  checkEquality: !0
};
function vs(e, t) {
  const n = { ...e };
  for (const i in t)
    t[i] !== void 0 && (n[i] = t[i]);
  return n;
}
function Yx(e, t, n) {
  const i = vs(ws, n);
  for (const r of e.values())
    if (r.parentId)
      Ns(r, e, t, i);
    else {
      const s = ei(r, i.nodeOrigin), a = Tt(r.extent) ? r.extent : i.nodeExtent, c = Dt(s, a, ft(r));
      r.internals.positionAbsolute = c;
    }
}
function Ux(e, t) {
  if (!e.handles)
    return e.measured ? t?.internals.handleBounds : void 0;
  const n = [], i = [];
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
    r.type === "source" ? n.push(s) : r.type === "target" && i.push(s);
  }
  return {
    source: n,
    target: i
  };
}
function bs(e) {
  return e === "manual";
}
function Ro(e, t, n, i = {}) {
  const r = vs(qx, i), s = { i: 0 }, a = new Map(t), c = r?.elevateNodesOnSelect && !bs(r.zIndexMode) ? Eu : 0;
  let u = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = a.get(d.id);
    if (r.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const p = ei(d, r.nodeOrigin), h = Tt(d.extent) ? d.extent : r.nodeExtent, y = Dt(p, h, ft(d));
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
          handleBounds: Ux(d, f),
          z: ku(d, c, r.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (u = !1), d.parentId && Ns(f, t, n, i, s), l ||= d.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: l };
}
function Zx(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function Ns(e, t, n, i, r) {
  const { elevateNodesOnSelect: s, nodeOrigin: a, nodeExtent: c, zIndexMode: u } = vs(ws, i), l = e.parentId, d = t.get(l);
  if (!d) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Zx(e, n), r && !d.parentId && d.internals.rootParentIndex === void 0 && u === "auto" && (d.internals.rootParentIndex = ++r.i, d.internals.z = d.internals.z + r.i * Xx), r && d.internals.rootParentIndex !== void 0 && (r.i = d.internals.rootParentIndex);
  const f = s && !bs(u) ? Eu : 0, { x: p, y: h, z: y } = Gx(e, d, a, c, f, u), { positionAbsolute: w } = e.internals, x = p !== w.x || h !== w.y;
  (x || y !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: x ? { x: p, y: h } : w,
      z: y
    }
  });
}
function ku(e, t, n) {
  const i = Ze(e.zIndex) ? e.zIndex : 0;
  return bs(n) ? i : i + (e.selected ? t : 0);
}
function Gx(e, t, n, i, r, s) {
  const { x: a, y: c } = t.internals.positionAbsolute, u = ft(e), l = ei(e, n), d = Tt(e.extent) ? Dt(l, e.extent, u) : l;
  let f = Dt({ x: a + d.x, y: c + d.y }, i, u);
  e.extent === "parent" && (f = pu(f, u, t));
  const p = ku(e, r, s), h = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: h >= p ? h + 1 : p
  };
}
function js(e, t, n, i = [0, 0]) {
  const r = [], s = /* @__PURE__ */ new Map();
  for (const a of e) {
    const c = t.get(a.parentId);
    if (!c)
      continue;
    const u = s.get(a.parentId)?.expandedRect ?? on(c), l = hu(u, a.rect);
    s.set(a.parentId, { expandedRect: l, parent: c });
  }
  return s.size > 0 && s.forEach(({ expandedRect: a, parent: c }, u) => {
    const l = c.internals.positionAbsolute, d = ft(c), f = c.origin ?? i, p = a.x < l.x ? Math.round(Math.abs(l.x - a.x)) : 0, h = a.y < l.y ? Math.round(Math.abs(l.y - a.y)) : 0, y = Math.max(d.width, Math.round(a.width)), w = Math.max(d.height, Math.round(a.height)), x = (y - d.width) * f[0], m = (w - d.height) * f[1];
    (p > 0 || h > 0 || x || m) && (r.push({
      id: u,
      type: "position",
      position: {
        x: c.position.x - p + x,
        y: c.position.y - h + m
      }
    }), n.get(u)?.forEach((b) => {
      e.some((g) => g.id === b.id) || r.push({
        id: b.id,
        type: "position",
        position: {
          x: b.position.x + p,
          y: b.position.y + h
        }
      });
    })), (d.width < a.width || d.height < a.height || p || h) && r.push({
      id: u,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: y + (p ? f[0] * p - x : 0),
        height: w + (h ? f[1] * h - m : 0)
      }
    });
  }), r;
}
function Jx(e, t, n, i, r, s, a) {
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
    const w = xs(h.nodeElement), x = y.measured.width !== w.width || y.measured.height !== w.height;
    if (!!(w.width && w.height && (x || !y.internals.handleBounds || h.force))) {
      const b = h.nodeElement.getBoundingClientRect(), g = Tt(y.extent) ? y.extent : s;
      let { positionAbsolute: v } = y.internals;
      y.parentId && y.extent === "parent" ? v = pu(v, w, t.get(y.parentId)) : g && (v = Dt(v, g, w));
      const j = {
        ...y,
        measured: w,
        internals: {
          ...y.internals,
          positionAbsolute: v,
          handleBounds: {
            source: Aa("source", h.nodeElement, b, f, y.id),
            target: Aa("target", h.nodeElement, b, f, y.id)
          }
        }
      };
      t.set(y.id, j), y.parentId && Ns(j, t, n, { nodeOrigin: r, zIndexMode: a }), u = !0, x && (l.push({
        id: y.id,
        type: "dimensions",
        dimensions: w
      }), y.expandParent && y.parentId && p.push({
        id: y.id,
        parentId: y.parentId,
        rect: on(j, r)
      }));
    }
  }
  if (p.length > 0) {
    const h = js(p, t, n, r);
    l.push(...h);
  }
  return { changes: l, updatedInternals: u };
}
async function Qx({ delta: e, panZoom: t, transform: n, translateExtent: i, width: r, height: s }) {
  if (!t || !e.x && !e.y)
    return !1;
  const a = await t.setViewportConstrained({
    x: n[0] + e.x,
    y: n[1] + e.y,
    zoom: n[2]
  }, [
    [0, 0],
    [r, s]
  ], i);
  return !!a && (a.x !== n[0] || a.y !== n[1] || a.k !== n[2]);
}
function Ra(e, t, n, i, r, s) {
  let a = r;
  const c = i.get(a) || /* @__PURE__ */ new Map();
  i.set(a, c.set(n, t)), a = `${r}-${e}`;
  const u = i.get(a) || /* @__PURE__ */ new Map();
  if (i.set(a, u.set(n, t)), s) {
    a = `${r}-${e}-${s}`;
    const l = i.get(a) || /* @__PURE__ */ new Map();
    i.set(a, l.set(n, t));
  }
}
function Iu(e, t, n) {
  e.clear(), t.clear();
  for (const i of n) {
    const { source: r, target: s, sourceHandle: a = null, targetHandle: c = null } = i, u = { edgeId: i.id, source: r, target: s, sourceHandle: a, targetHandle: c }, l = `${r}-${a}--${s}-${c}`, d = `${s}-${c}--${r}-${a}`;
    Ra("source", u, d, e, r, a), Ra("target", u, l, e, s, c), t.set(i.id, i);
  }
}
function Au(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : Au(n, t) : !1;
}
function La(e, t, n) {
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
function ew(e, t, n, i) {
  const r = /* @__PURE__ */ new Map();
  for (const [s, a] of e)
    if ((a.selected || a.id === i) && (!a.parentId || !Au(a, e)) && (a.draggable || t && typeof a.draggable > "u")) {
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
function lo({ nodeId: e, dragItems: t, nodeLookup: n, dragging: i = !0 }) {
  const r = [];
  for (const [a, c] of t) {
    const u = n.get(a)?.internals.userNode;
    u && r.push({
      ...u,
      position: c.position,
      dragging: i
    });
  }
  if (!e)
    return [r[0], r];
  const s = n.get(e)?.internals.userNode;
  return [
    s ? {
      ...s,
      position: t.get(e)?.position || s.position,
      dragging: i
    } : r[0],
    r
  ];
}
function tw({ dragItems: e, snapGrid: t, x: n, y: i }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const s = {
    x: n - r.distance.x,
    y: i - r.distance.y
  }, a = ni(s, t);
  return {
    x: a.x - s.x,
    y: a.y - s.y
  };
}
function nw({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: i, onDragStop: r }) {
  let s = { x: null, y: null }, a = 0, c = /* @__PURE__ */ new Map(), u = !1, l = { x: 0, y: 0 }, d = null, f = !1, p = null, h = !1, y = !1, w = null;
  function x({ noDragClassName: b, handleSelector: g, domNode: v, isSelectable: j, nodeId: N, nodeClickDistance: S = 0 }) {
    p = We(v);
    function k({ x: $, y: z }) {
      const { nodeLookup: C, nodeExtent: A, snapGrid: E, snapToGrid: D, nodeOrigin: P, onNodeDrag: T, onSelectionDrag: B, onError: W, updateNodePositions: H } = t();
      s = { x: $, y: z };
      let Y = !1;
      const V = c.size > 1, q = V && A ? Po(ti(c)) : null, G = V && D ? tw({
        dragItems: c,
        snapGrid: E,
        x: $,
        y: z
      }) : null;
      for (const [Z, R] of c) {
        if (!C.has(Z))
          continue;
        let X = { x: $ - R.distance.x, y: z - R.distance.y };
        D && (X = G ? {
          x: Math.round(X.x + G.x),
          y: Math.round(X.y + G.y)
        } : ni(X, E));
        let ce = null;
        if (V && A && !R.extent && q) {
          const { positionAbsolute: ne } = R.internals, de = ne.x - q.x + A[0][0], O = ne.x + R.measured.width - q.x2 + A[1][0], te = ne.y - q.y + A[0][1], ge = ne.y + R.measured.height - q.y2 + A[1][1];
          ce = [
            [de, te],
            [O, ge]
          ];
        }
        const { position: se, positionAbsolute: ee } = fu({
          nodeId: Z,
          nextPosition: X,
          nodeLookup: C,
          nodeExtent: ce || A,
          nodeOrigin: P,
          onError: W
        });
        Y = Y || R.position.x !== se.x || R.position.y !== se.y, R.position = se, R.internals.positionAbsolute = ee;
      }
      if (y = y || Y, !!Y && (H(c, !0), w && (i || T || !N && B))) {
        const [Z, R] = lo({
          nodeId: N,
          dragItems: c,
          nodeLookup: C
        });
        i?.(w, c, Z, R), T?.(w, Z, R), N || B?.(w, R);
      }
    }
    async function _() {
      if (!d)
        return;
      const { transform: $, panBy: z, autoPanSpeed: C, autoPanOnNodeDrag: A } = t();
      if (!A) {
        u = !1, cancelAnimationFrame(a);
        return;
      }
      const [E, D] = ys(l, d, C);
      (E !== 0 || D !== 0) && (s.x = (s.x ?? 0) - E / $[2], s.y = (s.y ?? 0) - D / $[2], await z({ x: E, y: D }) && k(s)), a = requestAnimationFrame(_);
    }
    function M($) {
      const { nodeLookup: z, multiSelectionActive: C, nodesDraggable: A, transform: E, snapGrid: D, snapToGrid: P, selectNodesOnDrag: T, onNodeDragStart: B, onSelectionDragStart: W, unselectNodesAndEdges: H } = t();
      f = !0, (!T || !j) && !C && N && (z.get(N)?.selected || H()), j && T && N && e?.(N);
      const Y = _n($.sourceEvent, { transform: E, snapGrid: D, snapToGrid: P, containerBounds: d });
      if (s = Y, c = ew(z, A, Y, N), c.size > 0 && (n || B || !N && W)) {
        const [V, q] = lo({
          nodeId: N,
          dragItems: c,
          nodeLookup: z
        });
        n?.($.sourceEvent, c, V, q), B?.($.sourceEvent, V, q), N || W?.($.sourceEvent, q);
      }
    }
    const I = Kl().clickDistance(S).on("start", ($) => {
      const { domNode: z, nodeDragThreshold: C, transform: A, snapGrid: E, snapToGrid: D } = t();
      d = z?.getBoundingClientRect() || null, h = !1, y = !1, w = $.sourceEvent, C === 0 && M($), s = _n($.sourceEvent, { transform: A, snapGrid: E, snapToGrid: D, containerBounds: d }), l = Ge($.sourceEvent, d);
    }).on("drag", ($) => {
      const { autoPanOnNodeDrag: z, transform: C, snapGrid: A, snapToGrid: E, nodeDragThreshold: D, nodeLookup: P } = t(), T = _n($.sourceEvent, { transform: C, snapGrid: A, snapToGrid: E, containerBounds: d });
      if (w = $.sourceEvent, ($.sourceEvent.type === "touchmove" && $.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      N && !P.has(N)) && (h = !0), !h) {
        if (!u && z && f && (u = !0, _()), !f) {
          const B = Ge($.sourceEvent, d), W = B.x - l.x, H = B.y - l.y;
          Math.sqrt(W * W + H * H) > D && M($);
        }
        (s.x !== T.xSnapped || s.y !== T.ySnapped) && c && f && (l = Ge($.sourceEvent, d), k(T));
      }
    }).on("end", ($) => {
      if (!f || h) {
        h && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (u = !1, f = !1, cancelAnimationFrame(a), c.size > 0) {
        const { nodeLookup: z, updateNodePositions: C, onNodeDragStop: A, onSelectionDragStop: E } = t();
        if (y && (C(c, !1), y = !1), r || A || !N && E) {
          const [D, P] = lo({
            nodeId: N,
            dragItems: c,
            nodeLookup: z,
            dragging: !1
          });
          r?.($.sourceEvent, c, D, P), A?.($.sourceEvent, D, P), N || E?.($.sourceEvent, P);
        }
      }
    }).filter(($) => {
      const z = $.target;
      return !$.button && (!b || !La(z, `.${b}`, v)) && (!g || La(z, g, v));
    });
    p.call(I);
  }
  function m() {
    p?.on(".drag", null);
  }
  return {
    update: x,
    destroy: m
  };
}
function iw(e, t, n) {
  const i = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const s of t.values())
    Fn(r, on(s)) > 0 && i.push(s);
  return i;
}
const rw = 250;
function ow(e, t, n, i) {
  let r = [], s = 1 / 0;
  const a = iw(e, n, t + rw);
  for (const c of a) {
    const u = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of u) {
      if (i.nodeId === l.nodeId && i.type === l.type && i.id === l.id)
        continue;
      const { x: d, y: f } = $t(c, l, l.position, !0), p = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(f - e.y, 2));
      p > t || (p < s ? (r = [{ ...l, x: d, y: f }], s = p) : p === s && r.push({ ...l, x: d, y: f }));
    }
  }
  if (!r.length)
    return null;
  if (r.length > 1) {
    const c = i.type === "source" ? "target" : "source";
    return r.find((u) => u.type === c) ?? r[0];
  }
  return r[0];
}
function _u(e, t, n, i, r, s = !1) {
  const a = i.get(e);
  if (!a)
    return null;
  const c = r === "strict" ? a.internals.handleBounds?.[t] : [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []], u = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return u && s ? { ...u, ...$t(a, u, u.position, !0) } : u;
}
function Du(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function sw(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const Tu = () => !0;
function aw(e, { connectionMode: t, connectionRadius: n, handleId: i, nodeId: r, edgeUpdaterType: s, isTarget: a, domNode: c, nodeLookup: u, lib: l, autoPanOnConnect: d, flowId: f, panBy: p, cancelConnection: h, onConnectStart: y, onConnect: w, onConnectEnd: x, isValidConnection: m = Tu, onReconnectEnd: b, updateConnection: g, getTransform: v, getFromHandle: j, autoPanSpeed: N, dragThreshold: S = 1, handleDomNode: k }) {
  const _ = xu(e.target);
  let M = 0, I;
  const { x: $, y: z } = Ge(e), C = Du(s, k), A = c?.getBoundingClientRect();
  let E = !1;
  if (!A || !C)
    return;
  const D = _u(r, C, i, u, t);
  if (!D)
    return;
  let P = Ge(e, A), T = !1, B = null, W = !1, H = null;
  function Y() {
    if (!d || !A)
      return;
    const [se, ee] = ys(P, A, N);
    p({ x: se, y: ee }), M = requestAnimationFrame(Y);
  }
  const V = {
    ...D,
    nodeId: r,
    type: C,
    position: D.position
  }, q = u.get(r);
  let Z = {
    inProgress: !0,
    isValid: null,
    from: $t(q, V, oe.Left, !0),
    fromHandle: V,
    fromPosition: V.position,
    fromNode: q,
    to: P,
    toHandle: null,
    toPosition: Ca[V.position],
    toNode: null,
    pointer: P
  };
  function R() {
    E = !0, g(Z), y?.(e, { nodeId: r, handleId: i, handleType: C });
  }
  S === 0 && R();
  function X(se) {
    if (!E) {
      const { x: ge, y: ye } = Ge(se), Ae = ge - $, _e = ye - z;
      if (!(Ae * Ae + _e * _e > S * S))
        return;
      R();
    }
    if (!j() || !V) {
      ce(se);
      return;
    }
    const ee = v();
    P = Ge(se, A), I = ow(fn(P, ee, !1, [1, 1]), n, u, V), T || (Y(), T = !0);
    const ne = $u(se, {
      handle: I,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: i,
      fromType: a ? "target" : "source",
      isValidConnection: m,
      doc: _,
      lib: l,
      flowId: f,
      nodeLookup: u
    });
    H = ne.handleDomNode, B = ne.connection, W = sw(!!I, ne.isValid);
    const de = u.get(r), O = de ? $t(de, V, oe.Left, !0) : Z.from, te = {
      ...Z,
      from: O,
      isValid: W,
      to: ne.toHandle && W ? sn({ x: ne.toHandle.x, y: ne.toHandle.y }, ee) : P,
      toHandle: ne.toHandle,
      toPosition: W && ne.toHandle ? ne.toHandle.position : Ca[V.position],
      toNode: ne.toHandle ? u.get(ne.toHandle.nodeId) : null,
      pointer: P
    };
    g(te), Z = te;
  }
  function ce(se) {
    if (!("touches" in se && se.touches.length > 0)) {
      if (E) {
        (I || H) && B && W && w?.(B);
        const { inProgress: ee, ...ne } = Z, de = {
          ...ne,
          toPosition: Z.toHandle ? Z.toPosition : null
        };
        x?.(se, de), s && b?.(se, de);
      }
      h(), cancelAnimationFrame(M), T = !1, W = !1, B = null, H = null, _.removeEventListener("mousemove", X), _.removeEventListener("mouseup", ce), _.removeEventListener("touchmove", X), _.removeEventListener("touchend", ce);
    }
  }
  _.addEventListener("mousemove", X), _.addEventListener("mouseup", ce), _.addEventListener("touchmove", X), _.addEventListener("touchend", ce);
}
function $u(e, { handle: t, connectionMode: n, fromNodeId: i, fromHandleId: r, fromType: s, doc: a, lib: c, flowId: u, isValidConnection: l = Tu, nodeLookup: d }) {
  const f = s === "target", p = t ? a.querySelector(`.${c}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: h, y } = Ge(e), w = a.elementFromPoint(h, y), x = w?.classList.contains(`${c}-flow__handle`) ? w : p, m = {
    handleDomNode: x,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (x) {
    const b = Du(void 0, x), g = x.getAttribute("data-nodeid"), v = x.getAttribute("data-handleid"), j = x.classList.contains("connectable"), N = x.classList.contains("connectableend");
    if (!g || !b)
      return m;
    const S = {
      source: f ? g : i,
      sourceHandle: f ? v : r,
      target: f ? i : g,
      targetHandle: f ? r : v
    };
    m.connection = S;
    const _ = j && N && (n === nn.Strict ? f && b === "source" || !f && b === "target" : g !== i || v !== r);
    m.isValid = _ && l(S), m.toHandle = _u(g, b, v, d, n, !0);
  }
  return m;
}
const Lo = {
  onPointerDown: aw,
  isValid: $u
};
function cw({ domNode: e, panZoom: t, getTransform: n, getViewScale: i }) {
  const r = We(e);
  function s({ translateExtent: c, width: u, height: l, zoomStep: d = 1, pannable: f = !0, zoomable: p = !0, inversePan: h = !1 }) {
    const y = (g) => {
      if (g.sourceEvent.type !== "wheel" || !t)
        return;
      const v = n(), j = g.sourceEvent.ctrlKey && Kn() ? 10 : 1, N = -g.sourceEvent.deltaY * (g.sourceEvent.deltaMode === 1 ? 0.05 : g.sourceEvent.deltaMode ? 1 : 2e-3) * d, S = v[2] * Math.pow(2, N * j);
      t.scaleTo(S);
    };
    let w = [0, 0];
    const x = (g) => {
      (g.sourceEvent.type === "mousedown" || g.sourceEvent.type === "touchstart") && (w = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ]);
    }, m = (g) => {
      const v = n();
      if (g.sourceEvent.type !== "mousemove" && g.sourceEvent.type !== "touchmove" || !t)
        return;
      const j = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ], N = [j[0] - w[0], j[1] - w[1]];
      w = j;
      const S = i() * Math.max(v[2], Math.log(v[2])) * (h ? -1 : 1), k = {
        x: v[0] - N[0] * S,
        y: v[1] - N[1] * S
      }, _ = [
        [0, 0],
        [u, l]
      ];
      t.setViewportConstrained({
        x: k.x,
        y: k.y,
        zoom: v[2]
      }, _, c);
    }, b = su().on("start", x).on("zoom", f ? m : null).on("zoom.wheel", p ? y : null);
    r.call(b, {});
  }
  function a() {
    r.on("zoom", null);
  }
  return {
    update: s,
    destroy: a,
    pointer: Ye
  };
}
const fr = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), uo = ({ x: e, y: t, zoom: n }) => lr.translate(e, t).scale(n), Kt = (e, t) => e.target.closest(`.${t}`), Pu = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), lw = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, fo = (e, t = 0, n = lw, i = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || i(), r ? e.transition().duration(t).ease(n).on("end", i) : e;
}, Mu = (e) => {
  const t = e.ctrlKey && Kn() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function uw({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: i, panOnScrollMode: r, panOnScrollSpeed: s, zoomOnPinch: a, onPanZoomStart: c, onPanZoom: u, onPanZoomEnd: l }) {
  return (d) => {
    if (Kt(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && a) {
      const x = Ye(d), m = Mu(d), b = f * Math.pow(2, m);
      i.scaleTo(n, b, x, d);
      return;
    }
    const p = d.deltaMode === 1 ? 20 : 1;
    let h = r === Ct.Vertical ? 0 : d.deltaX * p, y = r === Ct.Horizontal ? 0 : d.deltaY * p;
    !Kn() && d.shiftKey && r !== Ct.Vertical && (h = d.deltaY * p, y = 0), i.translateBy(
      n,
      -(h / f) * s,
      -(y / f) * s,
      // @ts-ignore
      { internal: !0 }
    );
    const w = fr(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(d, w), e.panScrollTimeout = setTimeout(() => {
      l?.(d, w), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(d, w));
  };
}
function dw({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(i, r) {
    const s = i.type === "wheel", a = !t && s && !i.ctrlKey, c = Kt(i, e);
    if (i.ctrlKey && s && c && i.preventDefault(), a || c)
      return null;
    i.preventDefault(), n.call(this, i, r);
  };
}
function fw({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (i) => {
    if (i.sourceEvent?.internal)
      return;
    const r = fr(i.transform);
    e.mouseButton = i.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, i.sourceEvent?.type === "mousedown" && t(!0), n && n?.(i.sourceEvent, r);
  };
}
function pw({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: i, onPanZoom: r }) {
  return (s) => {
    e.usedRightMouseButton = !!(n && Pu(t, e.mouseButton ?? 0)), s.sourceEvent?.sync || i([s.transform.x, s.transform.y, s.transform.k]), r && !s.sourceEvent?.internal && r?.(s.sourceEvent, fr(s.transform));
  };
}
function hw({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: i, onPanZoomEnd: r, onPaneContextMenu: s }) {
  return (a) => {
    if (!a.sourceEvent?.internal && (e.isZoomingOrPanning = !1, s && Pu(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && a.sourceEvent && s(a.sourceEvent), e.usedRightMouseButton = !1, i(!1), r)) {
      const c = fr(a.transform);
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
function gw({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: i, panOnScroll: r, zoomOnDoubleClick: s, userSelectionActive: a, noWheelClassName: c, noPanClassName: u, lib: l, connectionInProgress: d }) {
  return (f) => {
    const p = e || t, h = n && f.ctrlKey, y = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (Kt(f, `${l}-flow__node`) || Kt(f, `${l}-flow__edge`)))
      return !0;
    if (!i && !p && !r && !s && !n || a || d && !y || Kt(f, c) && y || Kt(f, u) && (!y || r && y && !e) || !n && f.ctrlKey && y)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!p && !r && !h && y || !i && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(i) && !i.includes(f.button) && f.type === "mousedown")
      return !1;
    const w = Array.isArray(i) && i.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || y) && w;
  };
}
function yw({ domNode: e, minZoom: t, maxZoom: n, translateExtent: i, viewport: r, onPanZoom: s, onPanZoomStart: a, onPanZoomEnd: c, onDraggingChange: u }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = su().scaleExtent([t, n]).translateExtent(i), p = We(e).call(f);
  b({
    x: r.x,
    y: r.y,
    zoom: rn(r.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], i);
  const h = p.on("wheel.zoom"), y = p.on("dblclick.zoom");
  f.wheelDelta(Mu);
  async function w(I, $) {
    return p ? new Promise((z) => {
      f?.interpolate($?.interpolate === "linear" ? An : Di).transform(fo(p, $?.duration, $?.ease, () => z(!0)), I);
    }) : !1;
  }
  function x({ noWheelClassName: I, noPanClassName: $, onPaneContextMenu: z, userSelectionActive: C, panOnScroll: A, panOnDrag: E, panOnScrollMode: D, panOnScrollSpeed: P, preventScrolling: T, zoomOnPinch: B, zoomOnScroll: W, zoomOnDoubleClick: H, zoomActivationKeyPressed: Y, lib: V, onTransformChange: q, connectionInProgress: G, paneClickDistance: Z, selectionOnDrag: R }) {
    C && !l.isZoomingOrPanning && m();
    const X = A && !Y && !C;
    f.clickDistance(R ? 1 / 0 : !Ze(Z) || Z < 0 ? 0 : Z);
    const ce = X ? uw({
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
    }) : dw({
      noWheelClassName: I,
      preventScrolling: T,
      d3ZoomHandler: h
    });
    p.on("wheel.zoom", ce, { passive: !1 });
    const se = fw({
      zoomPanValues: l,
      onDraggingChange: u,
      onPanZoomStart: a
    });
    f.on("start", se);
    const ee = pw({
      zoomPanValues: l,
      panOnDrag: E,
      onPaneContextMenu: !!z,
      onPanZoom: s,
      onTransformChange: q
    });
    f.on("zoom", ee);
    const ne = hw({
      zoomPanValues: l,
      panOnDrag: E,
      panOnScroll: A,
      onPaneContextMenu: z,
      onPanZoomEnd: c,
      onDraggingChange: u
    });
    f.on("end", ne);
    const de = gw({
      zoomActivationKeyPressed: Y,
      panOnDrag: E,
      zoomOnScroll: W,
      panOnScroll: A,
      zoomOnDoubleClick: H,
      zoomOnPinch: B,
      userSelectionActive: C,
      noPanClassName: $,
      noWheelClassName: I,
      lib: V,
      connectionInProgress: G
    });
    f.filter(de), H ? p.on("dblclick.zoom", y) : p.on("dblclick.zoom", null);
  }
  function m() {
    f.on("zoom", null);
  }
  async function b(I, $, z) {
    const C = uo(I), A = f?.constrain()(C, $, z);
    return A && await w(A), A;
  }
  async function g(I, $) {
    const z = uo(I);
    return await w(z, $), z;
  }
  function v(I) {
    if (p) {
      const $ = uo(I), z = p.property("__zoom");
      (z.k !== I.zoom || z.x !== I.x || z.y !== I.y) && f?.transform(p, $, null, { sync: !0 });
    }
  }
  function j() {
    const I = p ? ou(p.node()) : { x: 0, y: 0, k: 1 };
    return { x: I.x, y: I.y, zoom: I.k };
  }
  async function N(I, $) {
    return p ? new Promise((z) => {
      f?.interpolate($?.interpolate === "linear" ? An : Di).scaleTo(fo(p, $?.duration, $?.ease, () => z(!0)), I);
    }) : !1;
  }
  async function S(I, $) {
    return p ? new Promise((z) => {
      f?.interpolate($?.interpolate === "linear" ? An : Di).scaleBy(fo(p, $?.duration, $?.ease, () => z(!0)), I);
    }) : !1;
  }
  function k(I) {
    f?.scaleExtent(I);
  }
  function _(I) {
    f?.translateExtent(I);
  }
  function M(I) {
    const $ = !Ze(I) || I < 0 ? 0 : I;
    f?.clickDistance($);
  }
  return {
    update: x,
    destroy: m,
    setViewport: g,
    setViewportConstrained: b,
    getViewport: j,
    scaleTo: N,
    scaleBy: S,
    setScaleExtent: k,
    setTranslateExtent: _,
    syncViewport: v,
    setClickDistance: M
  };
}
var an;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(an || (an = {}));
function mw({ width: e, prevWidth: t, height: n, prevHeight: i, affectsX: r, affectsY: s }) {
  const a = e - t, c = n - i, u = [a > 0 ? 1 : a < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return a && r && (u[0] = u[0] * -1), c && s && (u[1] = u[1] * -1), u;
}
function za(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), i = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: i,
    affectsY: r
  };
}
function ht(e, t) {
  return Math.max(0, t - e);
}
function gt(e, t) {
  return Math.max(0, e - t);
}
function Ni(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function Va(e, t) {
  return e ? !t : t;
}
function xw(e, t, n, i, r, s, a, c) {
  let { affectsX: u, affectsY: l } = t;
  const { isHorizontal: d, isVertical: f } = t, p = d && f, { xSnapped: h, ySnapped: y } = n, { minWidth: w, maxWidth: x, minHeight: m, maxHeight: b } = i, { x: g, y: v, width: j, height: N, aspectRatio: S } = e;
  let k = Math.floor(d ? h - e.pointerX : 0), _ = Math.floor(f ? y - e.pointerY : 0);
  const M = j + (u ? -k : k), I = N + (l ? -_ : _), $ = -s[0] * j, z = -s[1] * N;
  let C = Ni(M, w, x), A = Ni(I, m, b);
  if (a) {
    let P = 0, T = 0;
    u && k < 0 ? P = ht(g + k + $, a[0][0]) : !u && k > 0 && (P = gt(g + M + $, a[1][0])), l && _ < 0 ? T = ht(v + _ + z, a[0][1]) : !l && _ > 0 && (T = gt(v + I + z, a[1][1])), C = Math.max(C, P), A = Math.max(A, T);
  }
  if (c) {
    let P = 0, T = 0;
    u && k > 0 ? P = gt(g + k, c[0][0]) : !u && k < 0 && (P = ht(g + M, c[1][0])), l && _ > 0 ? T = gt(v + _, c[0][1]) : !l && _ < 0 && (T = ht(v + I, c[1][1])), C = Math.max(C, P), A = Math.max(A, T);
  }
  if (r) {
    if (d) {
      const P = Ni(M / S, m, b) * S;
      if (C = Math.max(C, P), a) {
        let T = 0;
        !u && !l || u && !l && p ? T = gt(v + z + M / S, a[1][1]) * S : T = ht(v + z + (u ? k : -k) / S, a[0][1]) * S, C = Math.max(C, T);
      }
      if (c) {
        let T = 0;
        !u && !l || u && !l && p ? T = ht(v + M / S, c[1][1]) * S : T = gt(v + (u ? k : -k) / S, c[0][1]) * S, C = Math.max(C, T);
      }
    }
    if (f) {
      const P = Ni(I * S, w, x) / S;
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
  _ = _ + (_ < 0 ? A : -A), k = k + (k < 0 ? C : -C), r && (p ? M > I * S ? _ = (Va(u, l) ? -k : k) / S : k = (Va(u, l) ? -_ : _) * S : d ? (_ = k / S, l = u) : (k = _ * S, u = l));
  const E = u ? g + k : g, D = l ? v + _ : v;
  return {
    width: j + (u ? -k : k),
    height: N + (l ? -_ : _),
    x: s[0] * k * (u ? -1 : 1) + E,
    y: s[1] * _ * (l ? -1 : 1) + D
  };
}
const Ru = { width: 0, height: 0, x: 0, y: 0 }, ww = {
  ...Ru,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function vw(e, t, n) {
  const i = t.position.x + e.position.x, r = t.position.y + e.position.y, s = e.measured.width ?? 0, a = e.measured.height ?? 0, c = n[0] * s, u = n[1] * a;
  return [
    [i - c, r - u],
    [i + s - c, r + a - u]
  ];
}
function bw({ domNode: e, nodeId: t, getStoreItems: n, onChange: i, onEnd: r }) {
  const s = We(e);
  let a = {
    controlDirection: za("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: l, boundaries: d, keepAspectRatio: f, resizeDirection: p, onResizeStart: h, onResize: y, onResizeEnd: w, shouldResize: x }) {
    let m = { ...Ru }, b = { ...ww };
    a = {
      boundaries: d,
      resizeDirection: p,
      keepAspectRatio: f,
      controlDirection: za(l)
    };
    let g, v = null, j = [], N, S, k, _ = !1;
    const M = Kl().on("start", (I) => {
      const { nodeLookup: $, transform: z, snapGrid: C, snapToGrid: A, nodeOrigin: E, paneDomNode: D } = n();
      if (g = $.get(t), !g)
        return;
      v = D?.getBoundingClientRect() ?? null;
      const { xSnapped: P, ySnapped: T } = _n(I.sourceEvent, {
        transform: z,
        snapGrid: C,
        snapToGrid: A,
        containerBounds: v
      });
      m = {
        width: g.measured.width ?? 0,
        height: g.measured.height ?? 0,
        x: g.position.x ?? 0,
        y: g.position.y ?? 0
      }, b = {
        ...m,
        pointerX: P,
        pointerY: T,
        aspectRatio: m.width / m.height
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
          const H = vw(W, g, W.origin ?? E);
          k ? k = [
            [Math.min(H[0][0], k[0][0]), Math.min(H[0][1], k[0][1])],
            [Math.max(H[1][0], k[1][0]), Math.max(H[1][1], k[1][1])]
          ] : k = H;
        }
      h?.(I, { ...m });
    }).on("drag", (I) => {
      const { transform: $, snapGrid: z, snapToGrid: C, nodeOrigin: A } = n(), E = _n(I.sourceEvent, {
        transform: $,
        snapGrid: z,
        snapToGrid: C,
        containerBounds: v
      }), D = [];
      if (!g)
        return;
      const { x: P, y: T, width: B, height: W } = m, H = {}, Y = g.origin ?? A, { width: V, height: q, x: G, y: Z } = xw(b, a.controlDirection, E, a.boundaries, a.keepAspectRatio, Y, S, k), R = V !== B, X = q !== W, ce = G !== P && R, se = Z !== T && X;
      if (!ce && !se && !R && !X)
        return;
      if ((ce || se || Y[0] === 1 || Y[1] === 1) && (H.x = ce ? G : m.x, H.y = se ? Z : m.y, m.x = H.x, m.y = H.y, j.length > 0)) {
        const O = G - P, te = Z - T;
        for (const ge of j)
          ge.position = {
            x: ge.position.x - O + Y[0] * (V - B),
            y: ge.position.y - te + Y[1] * (q - W)
          }, D.push(ge);
      }
      if ((R || X) && (H.width = R && (!a.resizeDirection || a.resizeDirection === "horizontal") ? V : m.width, H.height = X && (!a.resizeDirection || a.resizeDirection === "vertical") ? q : m.height, m.width = H.width, m.height = H.height), N && g.expandParent) {
        const O = Y[0] * (H.width ?? 0);
        H.x && H.x < O && (m.x = O, b.x = b.x - (H.x - O));
        const te = Y[1] * (H.height ?? 0);
        H.y && H.y < te && (m.y = te, b.y = b.y - (H.y - te));
      }
      const ee = mw({
        width: m.width,
        prevWidth: B,
        height: m.height,
        prevHeight: W,
        affectsX: a.controlDirection.affectsX,
        affectsY: a.controlDirection.affectsY
      }), ne = { ...m, direction: ee };
      x?.(I, ne) !== !1 && (_ = !0, y?.(I, ne), i(H, D));
    }).on("end", (I) => {
      _ && (w?.(I, { ...m }), r?.({ ...m }), _ = !1);
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
var po = { exports: {} }, ho = {}, go = { exports: {} }, yo = {};
var Oa;
function Nw() {
  if (Oa) return yo;
  Oa = 1;
  var e = Je;
  function t(f, p) {
    return f === p && (f !== 0 || 1 / f === 1 / p) || f !== f && p !== p;
  }
  var n = typeof Object.is == "function" ? Object.is : t, i = e.useState, r = e.useEffect, s = e.useLayoutEffect, a = e.useDebugValue;
  function c(f, p) {
    var h = p(), y = i({ inst: { value: h, getSnapshot: p } }), w = y[0].inst, x = y[1];
    return s(
      function() {
        w.value = h, w.getSnapshot = p, u(w) && x({ inst: w });
      },
      [f, h, p]
    ), r(
      function() {
        return u(w) && x({ inst: w }), f(function() {
          u(w) && x({ inst: w });
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
  return yo.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : d, yo;
}
var Ha;
function jw() {
  return Ha || (Ha = 1, go.exports = Nw()), go.exports;
}
var Wa;
function Sw() {
  if (Wa) return ho;
  Wa = 1;
  var e = Je, t = jw();
  function n(l, d) {
    return l === d && (l !== 0 || 1 / l === 1 / d) || l !== l && d !== d;
  }
  var i = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, s = e.useRef, a = e.useEffect, c = e.useMemo, u = e.useDebugValue;
  return ho.useSyncExternalStoreWithSelector = function(l, d, f, p, h) {
    var y = s(null);
    if (y.current === null) {
      var w = { hasValue: !1, value: null };
      y.current = w;
    } else w = y.current;
    y = c(
      function() {
        function m(N) {
          if (!b) {
            if (b = !0, g = N, N = p(N), h !== void 0 && w.hasValue) {
              var S = w.value;
              if (h(S, N))
                return v = S;
            }
            return v = N;
          }
          if (S = v, i(g, N)) return S;
          var k = p(N);
          return h !== void 0 && h(S, k) ? (g = N, S) : (g = N, v = k);
        }
        var b = !1, g, v, j = f === void 0 ? null : f;
        return [
          function() {
            return m(d());
          },
          j === null ? void 0 : function() {
            return m(j());
          }
        ];
      },
      [d, f, p, h]
    );
    var x = r(l, y[0], y[1]);
    return a(
      function() {
        w.hasValue = !0, w.value = x;
      },
      [x]
    ), u(x), x;
  }, ho;
}
var Ba;
function Cw() {
  return Ba || (Ba = 1, po.exports = Sw()), po.exports;
}
var Ew = Cw();
const kw = /* @__PURE__ */ vf(Ew), Iw = {}, Fa = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), i = (d, f) => {
    const p = typeof d == "function" ? d(t) : d;
    if (!Object.is(p, t)) {
      const h = t;
      t = f ?? (typeof p != "object" || p === null) ? p : Object.assign({}, t, p), n.forEach((y) => y(t, h));
    }
  }, r = () => t, u = { setState: i, getState: r, getInitialState: () => l, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (Iw ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(i, r, u);
  return u;
}, Aw = (e) => e ? Fa(e) : Fa, { useDebugValue: _w } = Je, { useSyncExternalStoreWithSelector: Dw } = kw, Tw = (e) => e;
function Lu(e, t = Tw, n) {
  const i = Dw(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return _w(i), i;
}
const Ka = (e, t) => {
  const n = Aw(e), i = (r, s = t) => Lu(n, r, s);
  return Object.assign(i, n), i;
}, $w = (e, t) => e ? Ka(e, t) : Ka;
function we(e, t) {
  if (Object.is(e, t))
    return !0;
  if (typeof e != "object" || e === null || typeof t != "object" || t === null)
    return !1;
  if (e instanceof Map && t instanceof Map) {
    if (e.size !== t.size) return !1;
    for (const [i, r] of e)
      if (!Object.is(r, t.get(i)))
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
var mo = { exports: {} }, Te = {};
var Xa;
function Pw() {
  if (Xa) return Te;
  Xa = 1;
  var e = Je;
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
var qa;
function Mw() {
  if (qa) return mo.exports;
  qa = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), mo.exports = Pw(), mo.exports;
}
var Rw = Mw();
const pr = Xo(null), Lw = pr.Provider, zu = Ke.error001("react");
function pe(e, t) {
  const n = qn(pr);
  if (n === null)
    throw new Error(zu);
  return Lu(n, e, t);
}
function ve() {
  const e = qn(pr);
  if (e === null)
    throw new Error(zu);
  return ue(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const Ya = { display: "none" }, zw = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, Vu = "react-flow__node-desc", Ou = "react-flow__edge-desc", Vw = "react-flow__aria-live", Ow = (e) => e.ariaLiveMessage, Hw = (e) => e.ariaLabelConfig;
function Ww({ rfId: e }) {
  const t = pe(Ow);
  return o.jsx("div", { id: `${Vw}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: zw, children: t });
}
function Bw({ rfId: e, disableKeyboardA11y: t }) {
  const n = pe(Hw);
  return o.jsxs(o.Fragment, { children: [o.jsx("div", { id: `${Vu}-${e}`, style: Ya, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), o.jsx("div", { id: `${Ou}-${e}`, style: Ya, children: n["edge.a11yDescription.default"] }), !t && o.jsx(Ww, { rfId: e })] });
}
const hr = Wc(({ position: e = "top-left", children: t, className: n, style: i, ...r }, s) => {
  const a = `${e}`.split("-");
  return o.jsx("div", { className: ke(["react-flow__panel", n, ...a]), style: i, ref: s, ...r, children: t });
});
hr.displayName = "Panel";
function Fw({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : o.jsx(hr, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: o.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const Kw = (e) => {
  const t = [], n = [];
  for (const [, i] of e.nodeLookup)
    i.selected && t.push(i.internals.userNode);
  for (const [, i] of e.edgeLookup)
    i.selected && n.push(i);
  return { selectedNodes: t, selectedEdges: n };
}, ji = (e) => e.id;
function Xw(e, t) {
  return we(e.selectedNodes.map(ji), t.selectedNodes.map(ji)) && we(e.selectedEdges.map(ji), t.selectedEdges.map(ji));
}
function qw({ onSelectionChange: e }) {
  const t = ve(), { selectedNodes: n, selectedEdges: i } = pe(Kw, Xw);
  return Q(() => {
    const r = { nodes: n, edges: i };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((s) => s(r));
  }, [n, i, e]), null;
}
const Yw = (e) => !!e.onSelectionChangeHandlers;
function Uw({ onSelectionChange: e }) {
  const t = pe(Yw);
  return e || t ? o.jsx(qw, { onSelectionChange: e }) : null;
}
const Hu = [0, 0], Zw = { x: 0, y: 0, zoom: 1 }, Gw = [
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
], Ua = [...Gw, "rfId"], Jw = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), Za = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: Wn,
  nodeOrigin: Hu,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function Qw(e) {
  const { setNodes: t, setEdges: n, setMinZoom: i, setMaxZoom: r, setTranslateExtent: s, setNodeExtent: a, reset: c, setDefaultNodesAndEdges: u } = pe(Jw, we), l = ve();
  Q(() => (u(e.defaultNodes, e.defaultEdges), () => {
    d.current = Za, c();
  }), []);
  const d = re(Za);
  return Q(
    () => {
      for (const f of Ua) {
        const p = e[f], h = d.current[f];
        p !== h && (typeof e[f] > "u" || (f === "nodes" ? t(p) : f === "edges" ? n(p) : f === "minZoom" ? i(p) : f === "maxZoom" ? r(p) : f === "translateExtent" ? s(p) : f === "nodeExtent" ? a(p) : f === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: Px(p) }) : f === "fitView" ? l.setState({ fitViewQueued: p }) : f === "fitViewOptions" ? l.setState({ fitViewOptions: p }) : l.setState({ [f]: p })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    Ua.map((f) => e[f])
  ), null;
}
function Ga() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function ev(e) {
  const [t, n] = K(e === "system" ? null : e);
  return Q(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const i = Ga(), r = () => n(i?.matches ? "dark" : "light");
    return r(), i?.addEventListener("change", r), () => {
      i?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : Ga()?.matches ? "dark" : "light";
}
const Ja = typeof document < "u" ? document : null;
function Xn(e = null, t = { target: Ja, actInsideInputWithModifier: !0 }) {
  const [n, i] = K(!1), r = re(!1), s = re(/* @__PURE__ */ new Set([])), [a, c] = ue(() => {
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
    const u = t?.target ?? Ja, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (h) => {
        if (r.current = h.ctrlKey || h.metaKey || h.shiftKey || h.altKey, (!r.current || r.current && !l) && wu(h))
          return !1;
        const w = ec(h.code, c);
        if (s.current.add(h[w]), Qa(a, s.current, !1)) {
          const x = h.composedPath?.()?.[0] || h.target, m = x?.nodeName === "BUTTON" || x?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !m) && h.preventDefault(), i(!0);
        }
      }, f = (h) => {
        const y = ec(h.code, c);
        Qa(a, s.current, !0) ? (i(!1), s.current.clear()) : s.current.delete(h[y]), h.key === "Meta" && s.current.clear(), r.current = !1;
      }, p = () => {
        s.current.clear(), i(!1);
      };
      return u?.addEventListener("keydown", d), u?.addEventListener("keyup", f), window.addEventListener("blur", p), window.addEventListener("contextmenu", p), () => {
        u?.removeEventListener("keydown", d), u?.removeEventListener("keyup", f), window.removeEventListener("blur", p), window.removeEventListener("contextmenu", p);
      };
    }
  }, [e, i]), n;
}
function Qa(e, t, n) {
  return e.filter((i) => n || i.length === t.size).some((i) => i.every((r) => t.has(r)));
}
function ec(e, t) {
  return t.includes(e) ? "code" : "key";
}
const tv = () => {
  const e = ve();
  return ue(() => ({
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
      const { transform: [i, r, s], panZoom: a } = e.getState();
      return a ? (await a.setViewport({
        x: t.x ?? i,
        y: t.y ?? r,
        zoom: t.zoom ?? s
      }, n), !0) : !1;
    },
    getViewport: () => {
      const [t, n, i] = e.getState().transform;
      return { x: t, y: n, zoom: i };
    },
    setCenter: async (t, n, i) => e.getState().setCenter(t, n, i),
    fitBounds: async (t, n) => {
      const { width: i, height: r, minZoom: s, maxZoom: a, panZoom: c } = e.getState(), u = ms(t, i, r, s, a, n?.padding ?? 0.1);
      return c ? (await c.setViewport(u, {
        duration: n?.duration,
        ease: n?.ease,
        interpolate: n?.interpolate
      }), !0) : !1;
    },
    screenToFlowPosition: (t, n = {}) => {
      const { transform: i, snapGrid: r, snapToGrid: s, domNode: a } = e.getState();
      if (!a)
        return t;
      const { x: c, y: u } = a.getBoundingClientRect(), l = {
        x: t.x - c,
        y: t.y - u
      }, d = n.snapGrid ?? r, f = n.snapToGrid ?? s;
      return fn(l, i, f, d);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: i } = e.getState();
      if (!i)
        return t;
      const { x: r, y: s } = i.getBoundingClientRect(), a = sn(t, n);
      return {
        x: a.x + r,
        y: a.y + s
      };
    }
  }), []);
};
function Wu(e, t) {
  const n = [], i = /* @__PURE__ */ new Map(), r = [];
  for (const s of e)
    if (s.type === "add") {
      r.push(s);
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
      nv(u, c);
    n.push(c);
  }
  return r.length && r.forEach((s) => {
    s.index !== void 0 ? n.splice(s.index, 0, { ...s.item }) : n.push({ ...s.item });
  }), n;
}
function nv(e, t) {
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
function Bu(e, t) {
  return Wu(e, t);
}
function Fu(e, t) {
  return Wu(e, t);
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
  for (const [r, s] of e) {
    const a = t.has(r);
    !(s.selected === void 0 && !a) && s.selected !== a && (n && (s.selected = a), i.push(Nt(s.id, a)));
  }
  return i;
}
function tc({ items: e = [], lookup: t }) {
  const n = [], i = new Map(e.map((r) => [r.id, r]));
  for (const [r, s] of e.entries()) {
    const a = t.get(s.id), c = a?.internals?.userNode ?? a;
    c !== void 0 && c !== s && n.push({ id: s.id, item: s, type: "replace" }), c === void 0 && n.push({ item: s, type: "add", index: r });
  }
  for (const [r] of t)
    i.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function nc(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const Ku = gu();
function Xu(e, t, n = {}) {
  return Vx(e, t, {
    ...n,
    onError: n.onError ?? Ku
  });
}
function iv(e, t, n, i = { shouldReplaceId: !0 }) {
  return Ox(e, t, n, {
    ...i,
    onError: i.onError ?? Ku
  });
}
const ic = (e) => Cx(e), rv = (e) => du(e);
function qu(e) {
  return Wc(e);
}
const ov = typeof window < "u" ? tf : Q;
function rc(e) {
  const [t, n] = K(BigInt(0)), [i] = K(() => sv(() => n((r) => r + BigInt(1))));
  return ov(() => {
    const r = i.get();
    r.length && (e(r), i.reset());
  }, [t]), i;
}
function sv(e) {
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
const Yu = Xo(null);
function av({ children: e }) {
  const t = ve(), n = ae((c) => {
    const { nodes: u = [], setNodes: l, hasDefaultNodes: d, onNodesChange: f, nodeLookup: p, fitViewQueued: h, onNodesChangeMiddlewareMap: y } = t.getState();
    let w = u;
    for (const m of c)
      w = typeof m == "function" ? m(w) : m;
    let x = tc({
      items: w,
      lookup: p
    });
    for (const m of y.values())
      x = m(x);
    d && l(w), x.length > 0 ? f?.(x) : h && window.requestAnimationFrame(() => {
      const { fitViewQueued: m, nodes: b, setNodes: g } = t.getState();
      m && g(b);
    });
  }, []), i = rc(n), r = ae((c) => {
    const { edges: u = [], setEdges: l, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: p } = t.getState();
    let h = u;
    for (const y of c)
      h = typeof y == "function" ? y(h) : y;
    d ? l(h) : f && f(tc({
      items: h,
      lookup: p
    }));
  }, []), s = rc(r), a = ue(() => ({ nodeQueue: i, edgeQueue: s }), []);
  return o.jsx(Yu.Provider, { value: a, children: e });
}
function cv() {
  const e = qn(Yu);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const lv = (e) => !!e.panZoom;
function Ss() {
  const e = tv(), t = ve(), n = cv(), i = pe(lv), r = ue(() => {
    const s = (f) => t.getState().nodeLookup.get(f), a = (f) => {
      n.nodeQueue.push(f);
    }, c = (f) => {
      n.edgeQueue.push(f);
    }, u = (f) => {
      const { nodeLookup: p, nodeOrigin: h } = t.getState(), y = ic(f) ? f : p.get(f.id), w = y.parentId ? mu(y.position, y.measured, y.parentId, p, h) : y.position, x = {
        ...y,
        position: w,
        width: y.measured?.width ?? y.width,
        height: y.measured?.height ?? y.height
      };
      return on(x);
    }, l = (f, p, h = { replace: !1 }) => {
      a((y) => y.map((w) => {
        if (w.id === f) {
          const x = typeof p == "function" ? p(w) : p;
          return h.replace && ic(x) ? x : { ...w, ...x };
        }
        return w;
      }));
    }, d = (f, p, h = { replace: !1 }) => {
      c((y) => y.map((w) => {
        if (w.id === f) {
          const x = typeof p == "function" ? p(w) : p;
          return h.replace && rv(x) ? x : { ...w, ...x };
        }
        return w;
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
        const { nodes: f = [], edges: p = [], transform: h } = t.getState(), [y, w, x] = h;
        return {
          nodes: f.map((m) => ({ ...m })),
          edges: p.map((m) => ({ ...m })),
          viewport: {
            x: y,
            y: w,
            zoom: x
          }
        };
      },
      deleteElements: async ({ nodes: f = [], edges: p = [] }) => {
        const { nodes: h, edges: y, onNodesDelete: w, onEdgesDelete: x, triggerNodeChanges: m, triggerEdgeChanges: b, onDelete: g, onBeforeDelete: v } = t.getState(), { nodes: j, edges: N } = await _x({
          nodesToRemove: f,
          edgesToRemove: p,
          nodes: h,
          edges: y,
          onBeforeDelete: v
        }), S = N.length > 0, k = j.length > 0;
        if (S) {
          const _ = N.map(nc);
          x?.(N), b(_);
        }
        if (k) {
          const _ = j.map(nc);
          w?.(j), m(_);
        }
        return (k || S) && g?.({ nodes: j, edges: N }), { deletedNodes: j, deletedEdges: N };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, p = !0, h) => {
        const y = ka(f), w = y ? f : u(f), x = h !== void 0;
        return w ? (h || t.getState().nodes).filter((m) => {
          const b = t.getState().nodeLookup.get(m.id);
          if (b && !y && (m.id === f.id || !b.internals.positionAbsolute))
            return !1;
          const g = on(x ? m : b), v = Fn(g, w);
          return p && v > 0 || v >= g.width * g.height || v >= w.width * w.height;
        }) : [];
      },
      isNodeIntersecting: (f, p, h = !0) => {
        const w = ka(f) ? f : u(f);
        if (!w)
          return !1;
        const x = Fn(w, p);
        return h && x > 0 || x >= p.width * p.height || x >= w.width * w.height;
      },
      updateNode: l,
      updateNodeData: (f, p, h = { replace: !1 }) => {
        l(f, (y) => {
          const w = typeof p == "function" ? p(y) : p;
          return h.replace ? { ...y, data: w } : { ...y, data: { ...y.data, ...w } };
        }, h);
      },
      updateEdge: d,
      updateEdgeData: (f, p, h = { replace: !1 }) => {
        d(f, (y) => {
          const w = typeof p == "function" ? p(y) : p;
          return h.replace ? { ...y, data: w } : { ...y, data: { ...y.data, ...w } };
        }, h);
      },
      getNodesBounds: (f) => {
        const { nodeLookup: p, nodeOrigin: h } = t.getState();
        return Ex(f, { nodeLookup: p, nodeOrigin: h });
      },
      getHandleConnections: ({ type: f, id: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}-${f}${p ? `-${p}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}${f ? p ? `-${f}-${p}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const p = t.getState().fitViewResolver ?? $x();
        return t.setState({ fitViewQueued: !0, fitViewOptions: f, fitViewResolver: p }), n.nodeQueue.push((h) => [...h]), p.promise;
      }
    };
  }, []);
  return ue(() => ({
    ...r,
    ...e,
    viewportInitialized: i
  }), [i]);
}
const oc = (e) => e.selected, uv = typeof window < "u" ? window : void 0;
function dv({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = ve(), { deleteElements: i } = Ss(), r = Xn(e, { actInsideInputWithModifier: !1 }), s = Xn(t, { target: uv });
  Q(() => {
    if (r) {
      const { edges: a, nodes: c } = n.getState();
      i({ nodes: c.filter(oc), edges: a.filter(oc) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), Q(() => {
    n.setState({ multiSelectionActive: s });
  }, [s]);
}
function fv(e) {
  const t = ve();
  Q(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const i = xs(e.current);
      (i.height === 0 || i.width === 0) && t.getState().onError?.("004", Ke.error004()), t.setState({ width: i.width || 500, height: i.height || 500 });
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
const gr = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, pv = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function hv({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: i = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: s = Ct.Free, zoomOnDoubleClick: a = !0, panOnDrag: c = !0, defaultViewport: u, translateExtent: l, minZoom: d, maxZoom: f, zoomActivationKeyCode: p, preventScrolling: h = !0, children: y, noWheelClassName: w, noPanClassName: x, onViewportChange: m, isControlledViewport: b, paneClickDistance: g, selectionOnDrag: v }) {
  const j = ve(), N = re(null), { userSelectionActive: S, lib: k, connectionInProgress: _ } = pe(pv, we), M = Xn(p), I = re();
  fv(N);
  const $ = ae((z) => {
    m?.({ x: z[0], y: z[1], zoom: z[2] }), b || j.setState({ transform: z });
  }, [m, b]);
  return Q(() => {
    if (N.current) {
      I.current = yw({
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
      const { x: z, y: C, zoom: A } = I.current.getViewport();
      return j.setState({
        panZoom: I.current,
        transform: [z, C, A],
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
      panOnScrollSpeed: r,
      panOnScrollMode: s,
      zoomOnDoubleClick: a,
      panOnDrag: c,
      zoomActivationKeyPressed: M,
      preventScrolling: h,
      noPanClassName: x,
      userSelectionActive: S,
      noWheelClassName: w,
      lib: k,
      onTransformChange: $,
      connectionInProgress: _,
      selectionOnDrag: v,
      paneClickDistance: g
    });
  }, [
    e,
    t,
    n,
    i,
    r,
    s,
    a,
    c,
    M,
    h,
    x,
    S,
    w,
    k,
    $,
    _,
    v,
    g
  ]), o.jsx("div", { className: "react-flow__renderer", ref: N, style: gr, children: y });
}
const gv = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function yv() {
  const { userSelectionActive: e, userSelectionRect: t } = pe(gv, we);
  return e && t ? o.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const xo = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, mv = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function xv({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Bn.Full, panOnDrag: i, autoPanOnSelection: r, paneClickDistance: s, selectionOnDrag: a, onSelectionStart: c, onSelectionEnd: u, onPaneClick: l, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: p, onPaneMouseMove: h, onPaneMouseLeave: y, children: w }) {
  const x = re(0), m = ve(), { userSelectionActive: b, elementsSelectable: g, dragging: v, connectionInProgress: j, panBy: N, autoPanSpeed: S } = pe(mv, we), k = g && (e || b), _ = re(null), M = re(), I = re(/* @__PURE__ */ new Set()), $ = re(/* @__PURE__ */ new Set()), z = re(!1), C = re({ x: 0, y: 0 }), A = re(!1), E = (R) => {
    if (z.current || j) {
      z.current = !1;
      return;
    }
    l?.(R), m.getState().resetSelectedElements(), m.setState({ nodesSelectionActive: !1 });
  }, D = (R) => {
    if (Array.isArray(i) && i?.includes(2)) {
      R.preventDefault();
      return;
    }
    d?.(R);
  }, P = f ? (R) => f(R) : void 0, T = (R) => {
    z.current && (R.stopPropagation(), z.current = !1);
  }, B = (R) => {
    const { domNode: X, transform: ce } = m.getState();
    if (M.current = X?.getBoundingClientRect(), !M.current)
      return;
    const se = R.target === _.current;
    if (!se && !!R.target.closest(".nokey") || !e || !(a && se || t) || R.button !== 0 || !R.isPrimary)
      return;
    R.target?.setPointerCapture?.(R.pointerId), z.current = !1;
    const { x: de, y: O } = Ge(R.nativeEvent, M.current), te = fn({ x: de, y: O }, ce);
    m.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: te.x,
        startY: te.y,
        x: de,
        y: O
      }
    }), se || (R.stopPropagation(), R.preventDefault());
  };
  function W(R, X) {
    const { userSelectionRect: ce } = m.getState();
    if (!ce)
      return;
    const { transform: se, nodeLookup: ee, edgeLookup: ne, connectionLookup: de, triggerNodeChanges: O, triggerEdgeChanges: te, defaultEdgeOptions: ge } = m.getState(), ye = { x: ce.startX, y: ce.startY }, { x: Ae, y: _e } = sn(ye, se), $e = {
      startX: ye.x,
      startY: ye.y,
      x: R < Ae ? R : Ae,
      y: X < _e ? X : _e,
      width: Math.abs(R - Ae),
      height: Math.abs(X - _e)
    }, ot = I.current, Xe = $.current;
    I.current = new Set(gs(ee, $e, se, n === Bn.Partial, !0).map((Pe) => Pe.id)), $.current = /* @__PURE__ */ new Set();
    const qe = ge?.selectable ?? !0;
    for (const Pe of I.current) {
      const Oe = de.get(Pe);
      if (Oe)
        for (const { edgeId: He } of Oe.values()) {
          const Ne = ne.get(He);
          Ne && (Ne.selectable ?? qe) && $.current.add(He);
        }
    }
    if (!Ia(ot, I.current)) {
      const Pe = Xt(ee, I.current, !0);
      O(Pe);
    }
    if (!Ia(Xe, $.current)) {
      const Pe = Xt(ne, $.current);
      te(Pe);
    }
    m.setState({
      userSelectionRect: $e,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function H() {
    if (!r || !M.current)
      return;
    const [R, X] = ys(C.current, M.current, S);
    N({ x: R, y: X }).then((ce) => {
      if (!z.current || !ce) {
        x.current = requestAnimationFrame(H);
        return;
      }
      const { x: se, y: ee } = C.current;
      W(se, ee), x.current = requestAnimationFrame(H);
    });
  }
  const Y = () => {
    cancelAnimationFrame(x.current), x.current = 0, A.current = !1;
  };
  Q(() => () => Y(), []);
  const V = (R) => {
    const { userSelectionRect: X, transform: ce, resetSelectedElements: se } = m.getState();
    if (!M.current || !X)
      return;
    const { x: ee, y: ne } = Ge(R.nativeEvent, M.current);
    C.current = { x: ee, y: ne };
    const de = sn({ x: X.startX, y: X.startY }, ce);
    if (!z.current) {
      const O = t ? 0 : s;
      if (Math.hypot(ee - de.x, ne - de.y) <= O)
        return;
      se(), c?.(R);
    }
    z.current = !0, A.current || (H(), A.current = !0), W(ee, ne);
  }, q = (R) => {
    R.button === 0 && (R.target?.releasePointerCapture?.(R.pointerId), !b && R.target === _.current && m.getState().userSelectionRect && E?.(R), m.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), z.current && (u?.(R), m.setState({
      nodesSelectionActive: I.current.size > 0
    })), Y());
  }, G = (R) => {
    R.target?.releasePointerCapture?.(R.pointerId), Y();
  }, Z = i === !0 || Array.isArray(i) && i.includes(0);
  return o.jsxs("div", { className: ke(["react-flow__pane", { draggable: Z, dragging: v, selection: e }]), onClick: k ? void 0 : xo(E, _), onContextMenu: xo(D, _), onWheel: xo(P, _), onPointerEnter: k ? void 0 : p, onPointerMove: k ? V : h, onPointerUp: k ? q : void 0, onPointerCancel: k ? G : void 0, onPointerDownCapture: k ? B : void 0, onClickCapture: k ? T : void 0, onPointerLeave: y, ref: _, style: gr, children: [w, o.jsx(yv, {})] });
}
function zo({ id: e, store: t, unselect: n = !1, nodeRef: i }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: s, multiSelectionActive: a, nodeLookup: c, onError: u } = t.getState(), l = c.get(e);
  if (!l) {
    u?.("012", Ke.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && a) && (s({ nodes: [l], edges: [] }), requestAnimationFrame(() => i?.current?.blur())) : r([e]);
}
function Uu({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: i, nodeId: r, isSelectable: s, nodeClickDistance: a }) {
  const c = ve(), [u, l] = K(!1), d = re();
  return Q(() => {
    d.current = nw({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (f) => {
        zo({
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
        nodeId: r,
        nodeClickDistance: a
      }), () => {
        d.current?.destroy();
      };
  }, [n, i, t, s, e, r, a]), u;
}
const wv = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function Zu() {
  const e = ve();
  return ae((n) => {
    const { nodeExtent: i, snapToGrid: r, snapGrid: s, nodesDraggable: a, onError: c, updateNodePositions: u, nodeLookup: l, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), p = wv(a), h = r ? s[0] : 5, y = r ? s[1] : 5, w = n.direction.x * h * n.factor, x = n.direction.y * y * n.factor;
    for (const [, m] of l) {
      if (!p(m))
        continue;
      let b = {
        x: m.internals.positionAbsolute.x + w,
        y: m.internals.positionAbsolute.y + x
      };
      r && (b = ni(b, s));
      const { position: g, positionAbsolute: v } = fu({
        nodeId: m.id,
        nextPosition: b,
        nodeLookup: l,
        nodeExtent: i,
        nodeOrigin: d,
        onError: c
      });
      m.position = g, m.internals.positionAbsolute = v, f.set(m.id, m);
    }
    u(f);
  }, []);
}
const Cs = Xo(null), vv = Cs.Provider;
Cs.Consumer;
const Gu = () => qn(Cs), bv = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), Nv = (e, t, n) => (i) => {
  const { connectionClickStartHandle: r, connectionMode: s, connection: a } = i, { fromHandle: c, toHandle: u, isValid: l } = a, d = u?.nodeId === e && u?.id === t && u?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: d,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: s === nn.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!r,
    valid: d && l
  };
};
function jv({ type: e = "source", position: t = oe.Top, isValidConnection: n, isConnectable: i = !0, isConnectableStart: r = !0, isConnectableEnd: s = !0, id: a, onConnect: c, children: u, className: l, onMouseDown: d, onTouchStart: f, ...p }, h) {
  const y = a || null, w = e === "target", x = ve(), m = Gu(), { connectOnClick: b, noPanClassName: g, rfId: v } = pe(bv, we), { connectingFrom: j, connectingTo: N, clickConnecting: S, isPossibleEndHandle: k, connectionInProcess: _, clickConnectionInProcess: M, valid: I } = pe(Nv(m, y, e), we);
  m || x.getState().onError?.("010", Ke.error010());
  const $ = (A) => {
    const { defaultEdgeOptions: E, onConnect: D, hasDefaultEdges: P } = x.getState(), T = {
      ...E,
      ...A
    };
    if (P) {
      const { edges: B, setEdges: W, onError: H } = x.getState();
      W(Xu(T, B, { onError: H }));
    }
    D?.(T), c?.(T);
  }, z = (A) => {
    if (!m)
      return;
    const E = vu(A.nativeEvent);
    if (r && (E && A.button === 0 || !E)) {
      const D = x.getState();
      Lo.onPointerDown(A.nativeEvent, {
        handleDomNode: A.currentTarget,
        autoPanOnConnect: D.autoPanOnConnect,
        connectionMode: D.connectionMode,
        connectionRadius: D.connectionRadius,
        domNode: D.domNode,
        nodeLookup: D.nodeLookup,
        lib: D.lib,
        isTarget: w,
        handleId: y,
        nodeId: m,
        flowId: D.rfId,
        panBy: D.panBy,
        cancelConnection: D.cancelConnection,
        onConnectStart: D.onConnectStart,
        onConnectEnd: (...P) => x.getState().onConnectEnd?.(...P),
        updateConnection: D.updateConnection,
        onConnect: $,
        isValidConnection: n || ((...P) => x.getState().isValidConnection?.(...P) ?? !0),
        getTransform: () => x.getState().transform,
        getFromHandle: () => x.getState().connection.fromHandle,
        autoPanSpeed: D.autoPanSpeed,
        dragThreshold: D.connectionDragThreshold
      });
    }
    E ? d?.(A) : f?.(A);
  }, C = (A) => {
    const { onClickConnectStart: E, onClickConnectEnd: D, connectionClickStartHandle: P, connectionMode: T, isValidConnection: B, lib: W, rfId: H, nodeLookup: Y, connection: V } = x.getState();
    if (!m || !P && !r)
      return;
    if (!P) {
      E?.(A.nativeEvent, { nodeId: m, handleId: y, handleType: e }), x.setState({ connectionClickStartHandle: { nodeId: m, type: e, id: y } });
      return;
    }
    const q = xu(A.target), G = n || B, { connection: Z, isValid: R } = Lo.isValid(A.nativeEvent, {
      handle: {
        nodeId: m,
        id: y,
        type: e
      },
      connectionMode: T,
      fromNodeId: P.nodeId,
      fromHandleId: P.id || null,
      fromType: P.type,
      isValidConnection: G,
      flowId: H,
      doc: q,
      lib: W,
      nodeLookup: Y
    });
    R && Z && $(Z);
    const X = structuredClone(V);
    delete X.inProgress, X.toPosition = X.toHandle ? X.toHandle.position : null, D?.(A, X), x.setState({ connectionClickStartHandle: null });
  };
  return o.jsx("div", { "data-handleid": y, "data-nodeid": m, "data-handlepos": t, "data-id": `${v}-${m}-${y}-${e}`, className: ke([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    g,
    l,
    {
      source: !w,
      target: w,
      connectable: i,
      connectablestart: r,
      connectableend: s,
      clickconnecting: S,
      connectingfrom: j,
      connectingto: N,
      valid: I,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: i && (!_ || k) && (_ || M ? s : r)
    }
  ]), onMouseDown: z, onTouchStart: z, onClick: b ? C : void 0, ref: h, ...p, children: u });
}
const cn = Se(qu(jv));
function Sv({ data: e, isConnectable: t, sourcePosition: n = oe.Bottom }) {
  return o.jsxs(o.Fragment, { children: [e?.label, o.jsx(cn, { type: "source", position: n, isConnectable: t })] });
}
function Cv({ data: e, isConnectable: t, targetPosition: n = oe.Top, sourcePosition: i = oe.Bottom }) {
  return o.jsxs(o.Fragment, { children: [o.jsx(cn, { type: "target", position: n, isConnectable: t }), e?.label, o.jsx(cn, { type: "source", position: i, isConnectable: t })] });
}
function Ev() {
  return null;
}
function kv({ data: e, isConnectable: t, targetPosition: n = oe.Top }) {
  return o.jsxs(o.Fragment, { children: [o.jsx(cn, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const Ji = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, sc = {
  input: Sv,
  default: Cv,
  output: kv,
  group: Ev
};
function Iv(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const Av = (e) => {
  const { width: t, height: n, x: i, y: r } = ti(e.nodeLookup, {
    filter: (s) => !!s.selected
  });
  return {
    width: Ze(t) ? t : null,
    height: Ze(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${i}px,${r}px)`
  };
};
function _v({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const i = ve(), { width: r, height: s, transformString: a, userSelectionActive: c } = pe(Av, we), u = Zu(), l = re(null);
  Q(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !c && r !== null && s !== null;
  if (Uu({
    nodeRef: l,
    disabled: !d
  }), !d)
    return null;
  const f = e ? (h) => {
    const y = i.getState().nodes.filter((w) => w.selected);
    e(h, y);
  } : void 0, p = (h) => {
    Object.prototype.hasOwnProperty.call(Ji, h.key) && (h.preventDefault(), u({
      direction: Ji[h.key],
      factor: h.shiftKey ? 4 : 1
    }));
  };
  return o.jsx("div", { className: ke(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: a
  }, children: o.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : p, style: {
    width: r,
    height: s
  } }) });
}
const ac = typeof window < "u" ? window : void 0, Dv = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function Ju({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: i, onPaneMouseLeave: r, onPaneContextMenu: s, onPaneScroll: a, paneClickDistance: c, deleteKeyCode: u, selectionKeyCode: l, selectionOnDrag: d, selectionMode: f, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: y, panActivationKeyCode: w, zoomActivationKeyCode: x, elementsSelectable: m, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: v, panOnScrollSpeed: j, panOnScrollMode: N, zoomOnDoubleClick: S, panOnDrag: k, autoPanOnSelection: _, defaultViewport: M, translateExtent: I, minZoom: $, maxZoom: z, preventScrolling: C, onSelectionContextMenu: A, noWheelClassName: E, noPanClassName: D, disableKeyboardA11y: P, onViewportChange: T, isControlledViewport: B }) {
  const { nodesSelectionActive: W, userSelectionActive: H } = pe(Dv, we), Y = Xn(l, { target: ac }), V = Xn(w, { target: ac }), q = V || k, G = V || v, Z = d && q !== !0, R = Y || H || Z;
  return dv({ deleteKeyCode: u, multiSelectionKeyCode: y }), o.jsx(hv, { onPaneContextMenu: s, elementsSelectable: m, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: G, panOnScrollSpeed: j, panOnScrollMode: N, zoomOnDoubleClick: S, panOnDrag: !Y && q, defaultViewport: M, translateExtent: I, minZoom: $, maxZoom: z, zoomActivationKeyCode: x, preventScrolling: C, noWheelClassName: E, noPanClassName: D, onViewportChange: T, isControlledViewport: B, paneClickDistance: c, selectionOnDrag: Z, children: o.jsxs(xv, { onSelectionStart: p, onSelectionEnd: h, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: i, onPaneMouseLeave: r, onPaneContextMenu: s, onPaneScroll: a, panOnDrag: q, autoPanOnSelection: _, isSelecting: !!R, selectionMode: f, selectionKeyPressed: Y, paneClickDistance: c, selectionOnDrag: Z, children: [e, W && o.jsx(_v, { onSelectionContextMenu: A, noPanClassName: D, disableKeyboardA11y: P })] }) });
}
Ju.displayName = "FlowRenderer";
const Tv = Se(Ju), $v = (e) => (t) => e ? gs(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function Pv(e) {
  return pe(ae($v(e), [e]), we);
}
const Mv = (e) => e.updateNodeInternals;
function Rv() {
  const e = pe(Mv), [t] = K(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
    const i = /* @__PURE__ */ new Map();
    n.forEach((r) => {
      const s = r.target.getAttribute("data-id");
      i.set(s, {
        id: s,
        nodeElement: r.target,
        force: !0
      });
    }), e(i);
  }));
  return Q(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function Lv({ node: e, nodeType: t, hasDimensions: n, resizeObserver: i }) {
  const r = ve(), s = re(null), a = re(null), c = re(e.sourcePosition), u = re(e.targetPosition), l = re(t), d = n && !!e.internals.handleBounds;
  return Q(() => {
    s.current && !e.hidden && (!d || a.current !== s.current) && (a.current && i?.unobserve(a.current), i?.observe(s.current), a.current = s.current);
  }, [d, e.hidden]), Q(() => () => {
    a.current && (i?.unobserve(a.current), a.current = null);
  }, []), Q(() => {
    if (s.current) {
      const f = l.current !== t, p = c.current !== e.sourcePosition, h = u.current !== e.targetPosition;
      (f || p || h) && (l.current = t, c.current = e.sourcePosition, u.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: s.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), s;
}
function zv({ id: e, onClick: t, onMouseEnter: n, onMouseMove: i, onMouseLeave: r, onContextMenu: s, onDoubleClick: a, nodesDraggable: c, elementsSelectable: u, nodesConnectable: l, nodesFocusable: d, resizeObserver: f, noDragClassName: p, noPanClassName: h, disableKeyboardA11y: y, rfId: w, nodeTypes: x, nodeClickDistance: m, onError: b }) {
  const { node: g, internals: v, isParent: j } = pe((R) => {
    const X = R.nodeLookup.get(e), ce = R.parentLookup.has(e);
    return {
      node: X,
      internals: X.internals,
      isParent: ce
    };
  }, we);
  let N = g.type || "default", S = x?.[N] || sc[N];
  S === void 0 && (b?.("003", Ke.error003(N)), N = "default", S = x?.default || sc.default);
  const k = !!(g.draggable || c && typeof g.draggable > "u"), _ = !!(g.selectable || u && typeof g.selectable > "u"), M = !!(g.connectable || l && typeof g.connectable > "u"), I = !!(g.focusable || d && typeof g.focusable > "u"), $ = ve(), z = yu(g), C = Lv({ node: g, nodeType: N, hasDimensions: z, resizeObserver: f }), A = Uu({
    nodeRef: C,
    disabled: g.hidden || !k,
    noDragClassName: p,
    handleSelector: g.dragHandle,
    nodeId: e,
    isSelectable: _,
    nodeClickDistance: m
  }), E = Zu();
  if (g.hidden)
    return null;
  const D = ft(g), P = Iv(g), T = _ || k || t || n || i || r, B = n ? (R) => n(R, { ...v.userNode }) : void 0, W = i ? (R) => i(R, { ...v.userNode }) : void 0, H = r ? (R) => r(R, { ...v.userNode }) : void 0, Y = s ? (R) => s(R, { ...v.userNode }) : void 0, V = a ? (R) => a(R, { ...v.userNode }) : void 0, q = (R) => {
    const { selectNodesOnDrag: X, nodeDragThreshold: ce } = $.getState();
    _ && (!X || !k || ce > 0) && zo({
      id: e,
      store: $,
      nodeRef: C
    }), t && t(R, { ...v.userNode });
  }, G = (R) => {
    if (!(wu(R.nativeEvent) || y)) {
      if (au.includes(R.key) && _) {
        const X = R.key === "Escape";
        zo({
          id: e,
          store: $,
          unselect: X,
          nodeRef: C
        });
      } else if (k && g.selected && Object.prototype.hasOwnProperty.call(Ji, R.key)) {
        R.preventDefault();
        const { ariaLabelConfig: X } = $.getState();
        $.setState({
          ariaLiveMessage: X["node.a11yDescription.ariaLiveMessage"]({
            direction: R.key.replace("Arrow", "").toLowerCase(),
            x: ~~v.positionAbsolute.x,
            y: ~~v.positionAbsolute.y
          })
        }), E({
          direction: Ji[R.key],
          factor: R.shiftKey ? 4 : 1
        });
      }
    }
  }, Z = () => {
    if (y || !C.current?.matches(":focus-visible"))
      return;
    const { transform: R, width: X, height: ce, autoPanOnNodeFocus: se, setCenter: ee } = $.getState();
    if (!se)
      return;
    gs(/* @__PURE__ */ new Map([[e, g]]), { x: 0, y: 0, width: X, height: ce }, R, !0).length > 0 || ee(g.position.x + D.width / 2, g.position.y + D.height / 2, {
      zoom: R[2]
    });
  };
  return o.jsx("div", { className: ke([
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
    zIndex: v.z,
    transform: `translate(${v.positionAbsolute.x}px,${v.positionAbsolute.y}px)`,
    pointerEvents: T ? "all" : "none",
    visibility: z ? "visible" : "hidden",
    ...g.style,
    ...P
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: B, onMouseMove: W, onMouseLeave: H, onContextMenu: Y, onClick: q, onDoubleClick: V, onKeyDown: I ? G : void 0, tabIndex: I ? 0 : void 0, onFocus: I ? Z : void 0, role: g.ariaRole ?? (I ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": y ? void 0 : `${Vu}-${w}`, "aria-label": g.ariaLabel, ...g.domAttributes, children: o.jsx(vv, { value: e, children: o.jsx(S, { id: e, data: g.data, type: N, positionAbsoluteX: v.positionAbsolute.x, positionAbsoluteY: v.positionAbsolute.y, selected: g.selected ?? !1, selectable: _, draggable: k, deletable: g.deletable ?? !0, isConnectable: M, sourcePosition: g.sourcePosition, targetPosition: g.targetPosition, dragging: A, dragHandle: g.dragHandle, zIndex: v.z, parentId: g.parentId, ...D }) }) });
}
var Vv = Se(zv);
const Ov = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function Qu(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: i, elementsSelectable: r, onError: s } = pe(Ov, we), a = Pv(e.onlyRenderVisibleElements), c = Rv();
  return o.jsx("div", { className: "react-flow__nodes", style: gr, children: a.map((u) => (
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
    o.jsx(Vv, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: i, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: s }, u)
  )) });
}
Qu.displayName = "NodeRenderer";
const Hv = Se(Qu);
function Wv(e) {
  return pe(ae((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const i = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const s = n.nodeLookup.get(r.source), a = n.nodeLookup.get(r.target);
        s && a && Lx({
          sourceNode: s,
          targetNode: a,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && i.push(r.id);
      }
    return i;
  }, [e]), we);
}
const Bv = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return o.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, Fv = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return o.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, cc = {
  [Ui.Arrow]: Bv,
  [Ui.ArrowClosed]: Fv
};
function Kv(e) {
  const t = ve();
  return ue(() => Object.prototype.hasOwnProperty.call(cc, e) ? cc[e] : (t.getState().onError?.("009", Ke.error009(e)), null), [e]);
}
const Xv = ({ id: e, type: t, color: n, width: i = 12.5, height: r = 12.5, markerUnits: s = "strokeWidth", strokeWidth: a, orient: c = "auto-start-reverse" }) => {
  const u = Kv(t);
  return u ? o.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${i}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: s, orient: c, refX: "0", refY: "0", children: o.jsx(u, { color: n, strokeWidth: a }) }) : null;
}, ed = ({ defaultColor: e, rfId: t }) => {
  const n = pe((s) => s.edges), i = pe((s) => s.defaultEdgeOptions), r = ue(() => Kx(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: i?.markerStart,
    defaultMarkerEnd: i?.markerEnd
  }), [n, i, t, e]);
  return r.length ? o.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: o.jsx("defs", { children: r.map((s) => o.jsx(Xv, { id: s.id, type: s.type, color: s.color, width: s.width, height: s.height, markerUnits: s.markerUnits, strokeWidth: s.strokeWidth, orient: s.orient }, s.id)) }) }) : null;
};
ed.displayName = "MarkerDefinitions";
var qv = Se(ed);
function td({ x: e, y: t, label: n, labelStyle: i, labelShowBg: r = !0, labelBgStyle: s, labelBgPadding: a = [2, 4], labelBgBorderRadius: c = 2, children: u, className: l, ...d }) {
  const [f, p] = K({ x: 1, y: 0, width: 0, height: 0 }), h = ke(["react-flow__edge-textwrapper", l]), y = re(null);
  return Q(() => {
    if (y.current) {
      const w = y.current.getBBox();
      p({
        x: w.x,
        y: w.y,
        width: w.width,
        height: w.height
      });
    }
  }, [n]), n ? o.jsxs("g", { transform: `translate(${e - f.width / 2} ${t - f.height / 2})`, className: h, visibility: f.width ? "visible" : "hidden", ...d, children: [r && o.jsx("rect", { width: f.width + 2 * a[0], x: -a[0], y: -a[1], height: f.height + 2 * a[1], className: "react-flow__edge-textbg", style: s, rx: c, ry: c }), o.jsx("text", { className: "react-flow__edge-text", y: f.height / 2, dy: "0.3em", ref: y, style: i, children: n }), u] }) : null;
}
td.displayName = "EdgeText";
const Yv = Se(td);
function ii({ path: e, labelX: t, labelY: n, label: i, labelStyle: r, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u, interactionWidth: l = 20, ...d }) {
  return o.jsxs(o.Fragment, { children: [o.jsx("path", { ...d, d: e, fill: "none", className: ke(["react-flow__edge-path", d.className]) }), l ? o.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, i && Ze(t) && Ze(n) ? o.jsx(Yv, { x: t, y: n, label: i, labelStyle: r, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u }) : null] });
}
function lc({ pos: e, x1: t, y1: n, x2: i, y2: r }) {
  return e === oe.Left || e === oe.Right ? [0.5 * (t + i), n] : [t, 0.5 * (n + r)];
}
function nd({ sourceX: e, sourceY: t, sourcePosition: n = oe.Bottom, targetX: i, targetY: r, targetPosition: s = oe.Top }) {
  const [a, c] = lc({
    pos: n,
    x1: e,
    y1: t,
    x2: i,
    y2: r
  }), [u, l] = lc({
    pos: s,
    x1: i,
    y1: r,
    x2: e,
    y2: t
  }), [d, f, p, h] = bu({
    sourceX: e,
    sourceY: t,
    targetX: i,
    targetY: r,
    sourceControlX: a,
    sourceControlY: c,
    targetControlX: u,
    targetControlY: l
  });
  return [
    `M${e},${t} C${a},${c} ${u},${l} ${i},${r}`,
    d,
    f,
    p,
    h
  ];
}
function id(e) {
  return Se(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, sourcePosition: a, targetPosition: c, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: w, markerStart: x, interactionWidth: m }) => {
    const [b, g, v] = nd({
      sourceX: n,
      sourceY: i,
      sourcePosition: a,
      targetX: r,
      targetY: s,
      targetPosition: c
    }), j = e.isInternal ? void 0 : t;
    return o.jsx(ii, { id: j, path: b, labelX: g, labelY: v, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: w, markerStart: x, interactionWidth: m });
  });
}
const Uv = id({ isInternal: !1 }), rd = id({ isInternal: !0 });
Uv.displayName = "SimpleBezierEdge";
rd.displayName = "SimpleBezierEdgeInternal";
function od(e) {
  return Se(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, sourcePosition: h = oe.Bottom, targetPosition: y = oe.Top, markerEnd: w, markerStart: x, pathOptions: m, interactionWidth: b }) => {
    const [g, v, j] = Gi({
      sourceX: n,
      sourceY: i,
      sourcePosition: h,
      targetX: r,
      targetY: s,
      targetPosition: y,
      borderRadius: m?.borderRadius,
      offset: m?.offset,
      stepPosition: m?.stepPosition
    }), N = e.isInternal ? void 0 : t;
    return o.jsx(ii, { id: N, path: g, labelX: v, labelY: j, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: w, markerStart: x, interactionWidth: b });
  });
}
const sd = od({ isInternal: !1 }), ad = od({ isInternal: !0 });
sd.displayName = "SmoothStepEdge";
ad.displayName = "SmoothStepEdgeInternal";
function cd(e) {
  return Se(({ id: t, ...n }) => {
    const i = e.isInternal ? void 0 : t;
    return o.jsx(sd, { ...n, id: i, pathOptions: ue(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Zv = cd({ isInternal: !1 }), ld = cd({ isInternal: !0 });
Zv.displayName = "StepEdge";
ld.displayName = "StepEdgeInternal";
function ud(e) {
  return Se(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: y, interactionWidth: w }) => {
    const [x, m, b] = Cu({ sourceX: n, sourceY: i, targetX: r, targetY: s }), g = e.isInternal ? void 0 : t;
    return o.jsx(ii, { id: g, path: x, labelX: m, labelY: b, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: y, interactionWidth: w });
  });
}
const Gv = ud({ isInternal: !1 }), dd = ud({ isInternal: !0 });
Gv.displayName = "StraightEdge";
dd.displayName = "StraightEdgeInternal";
function fd(e) {
  return Se(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, sourcePosition: a = oe.Bottom, targetPosition: c = oe.Top, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: w, markerStart: x, pathOptions: m, interactionWidth: b }) => {
    const [g, v, j] = Nu({
      sourceX: n,
      sourceY: i,
      sourcePosition: a,
      targetX: r,
      targetY: s,
      targetPosition: c,
      curvature: m?.curvature
    }), N = e.isInternal ? void 0 : t;
    return o.jsx(ii, { id: N, path: g, labelX: v, labelY: j, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: w, markerStart: x, interactionWidth: b });
  });
}
const Jv = fd({ isInternal: !1 }), pd = fd({ isInternal: !0 });
Jv.displayName = "BezierEdge";
pd.displayName = "BezierEdgeInternal";
const uc = {
  default: pd,
  straight: dd,
  step: ld,
  smoothstep: ad,
  simplebezier: rd
}, dc = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, Qv = (e, t, n) => n === oe.Left ? e - t : n === oe.Right ? e + t : e, e0 = (e, t, n) => n === oe.Top ? e - t : n === oe.Bottom ? e + t : e, fc = "react-flow__edgeupdater";
function pc({ position: e, centerX: t, centerY: n, radius: i = 10, onMouseDown: r, onMouseEnter: s, onMouseOut: a, type: c }) {
  return o.jsx("circle", { onMouseDown: r, onMouseEnter: s, onMouseOut: a, className: ke([fc, `${fc}-${c}`]), cx: Qv(t, i, e), cy: e0(n, i, e), r: i, stroke: "transparent", fill: "transparent" });
}
function t0({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: i, sourceY: r, targetX: s, targetY: a, sourcePosition: c, targetPosition: u, onReconnect: l, onReconnectStart: d, onReconnectEnd: f, setReconnecting: p, setUpdateHover: h }) {
  const y = ve(), w = (v, j) => {
    if (v.button !== 0)
      return;
    const { autoPanOnConnect: N, domNode: S, connectionMode: k, connectionRadius: _, lib: M, onConnectStart: I, cancelConnection: $, nodeLookup: z, rfId: C, panBy: A, updateConnection: E } = y.getState(), D = j.type === "target", P = (W, H) => {
      p(!1), f?.(W, n, j.type, H);
    }, T = (W) => l?.(n, W), B = (W, H) => {
      p(!0), d?.(v, n, j.type), I?.(W, H);
    };
    Lo.onPointerDown(v.nativeEvent, {
      autoPanOnConnect: N,
      connectionMode: k,
      connectionRadius: _,
      domNode: S,
      handleId: j.id,
      nodeId: j.nodeId,
      nodeLookup: z,
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
      handleDomNode: v.currentTarget
    });
  }, x = (v) => w(v, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), m = (v) => w(v, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), b = () => h(!0), g = () => h(!1);
  return o.jsxs(o.Fragment, { children: [(e === !0 || e === "source") && o.jsx(pc, { position: c, centerX: i, centerY: r, radius: t, onMouseDown: x, onMouseEnter: b, onMouseOut: g, type: "source" }), (e === !0 || e === "target") && o.jsx(pc, { position: u, centerX: s, centerY: a, radius: t, onMouseDown: m, onMouseEnter: b, onMouseOut: g, type: "target" })] });
}
function n0({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: i, onClick: r, onDoubleClick: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, rfId: y, edgeTypes: w, noPanClassName: x, onError: m, disableKeyboardA11y: b }) {
  let g = pe((ee) => ee.edgeLookup.get(e));
  const v = pe((ee) => ee.defaultEdgeOptions);
  g = v ? { ...v, ...g } : g;
  let j = g.type || "default", N = w?.[j] || uc[j];
  N === void 0 && (m?.("011", Ke.error011(j)), j = "default", N = w?.default || uc.default);
  const S = !!(g.focusable || t && typeof g.focusable > "u"), k = typeof f < "u" && (g.reconnectable || n && typeof g.reconnectable > "u"), _ = !!(g.selectable || i && typeof g.selectable > "u"), M = re(null), [I, $] = K(!1), [z, C] = K(!1), A = ve(), { zIndex: E, sourceX: D, sourceY: P, targetX: T, targetY: B, sourcePosition: W, targetPosition: H } = pe(ae((ee) => {
    const ne = ee.nodeLookup.get(g.source), de = ee.nodeLookup.get(g.target);
    if (!ne || !de)
      return {
        zIndex: g.zIndex,
        ...dc
      };
    const O = Fx({
      id: e,
      sourceNode: ne,
      targetNode: de,
      sourceHandle: g.sourceHandle || null,
      targetHandle: g.targetHandle || null,
      connectionMode: ee.connectionMode,
      onError: m
    });
    return {
      zIndex: Rx({
        selected: g.selected,
        zIndex: g.zIndex,
        sourceNode: ne,
        targetNode: de,
        elevateOnSelect: ee.elevateEdgesOnSelect,
        zIndexMode: ee.zIndexMode
      }),
      ...O || dc
    };
  }, [g.source, g.target, g.sourceHandle, g.targetHandle, g.selected, g.zIndex]), we), Y = ue(() => g.markerStart ? `url('#${Mo(g.markerStart, y)}')` : void 0, [g.markerStart, y]), V = ue(() => g.markerEnd ? `url('#${Mo(g.markerEnd, y)}')` : void 0, [g.markerEnd, y]);
  if (g.hidden || D === null || P === null || T === null || B === null)
    return null;
  const q = (ee) => {
    const { addSelectedEdges: ne, unselectNodesAndEdges: de, multiSelectionActive: O } = A.getState();
    _ && (A.setState({ nodesSelectionActive: !1 }), g.selected && O ? (de({ nodes: [], edges: [g] }), M.current?.blur()) : ne([e])), r && r(ee, g);
  }, G = s ? (ee) => {
    s(ee, { ...g });
  } : void 0, Z = a ? (ee) => {
    a(ee, { ...g });
  } : void 0, R = c ? (ee) => {
    c(ee, { ...g });
  } : void 0, X = u ? (ee) => {
    u(ee, { ...g });
  } : void 0, ce = l ? (ee) => {
    l(ee, { ...g });
  } : void 0, se = (ee) => {
    if (!b && au.includes(ee.key) && _) {
      const { unselectNodesAndEdges: ne, addSelectedEdges: de } = A.getState();
      ee.key === "Escape" ? (M.current?.blur(), ne({ edges: [g] })) : de([e]);
    }
  };
  return o.jsx("svg", { style: { zIndex: E }, children: o.jsxs("g", { className: ke([
    "react-flow__edge",
    `react-flow__edge-${j}`,
    g.className,
    x,
    {
      selected: g.selected,
      animated: g.animated,
      inactive: !_ && !r,
      updating: I,
      selectable: _
    }
  ]), onClick: q, onDoubleClick: G, onContextMenu: Z, onMouseEnter: R, onMouseMove: X, onMouseLeave: ce, onKeyDown: S ? se : void 0, tabIndex: S ? 0 : void 0, role: g.ariaRole ?? (S ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": g.ariaLabel === null ? void 0 : g.ariaLabel || `Edge from ${g.source} to ${g.target}`, "aria-describedby": S ? `${Ou}-${y}` : void 0, ref: M, ...g.domAttributes, children: [!z && o.jsx(N, { id: e, source: g.source, target: g.target, type: g.type, selected: g.selected, animated: g.animated, selectable: _, deletable: g.deletable ?? !0, label: g.label, labelStyle: g.labelStyle, labelShowBg: g.labelShowBg, labelBgStyle: g.labelBgStyle, labelBgPadding: g.labelBgPadding, labelBgBorderRadius: g.labelBgBorderRadius, sourceX: D, sourceY: P, targetX: T, targetY: B, sourcePosition: W, targetPosition: H, data: g.data, style: g.style, sourceHandleId: g.sourceHandle, targetHandleId: g.targetHandle, markerStart: Y, markerEnd: V, pathOptions: "pathOptions" in g ? g.pathOptions : void 0, interactionWidth: g.interactionWidth }), k && o.jsx(t0, { edge: g, isReconnectable: k, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, sourceX: D, sourceY: P, targetX: T, targetY: B, sourcePosition: W, targetPosition: H, setUpdateHover: $, setReconnecting: C })] }) });
}
var i0 = Se(n0);
const r0 = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function hd({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: i, noPanClassName: r, onReconnect: s, onEdgeContextMenu: a, onEdgeMouseEnter: c, onEdgeMouseMove: u, onEdgeMouseLeave: l, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: p, onReconnectStart: h, onReconnectEnd: y, disableKeyboardA11y: w }) {
  const { edgesFocusable: x, edgesReconnectable: m, elementsSelectable: b, onError: g } = pe(r0, we), v = Wv(t);
  return o.jsxs("div", { className: "react-flow__edges", children: [o.jsx(qv, { defaultColor: e, rfId: n }), v.map((j) => o.jsx(i0, { id: j, edgesFocusable: x, edgesReconnectable: m, elementsSelectable: b, noPanClassName: r, onReconnect: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, onClick: d, reconnectRadius: f, onDoubleClick: p, onReconnectStart: h, onReconnectEnd: y, rfId: n, onError: g, edgeTypes: i, disableKeyboardA11y: w }, j))] });
}
hd.displayName = "EdgeRenderer";
const o0 = Se(hd), s0 = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function a0({ children: e }) {
  const t = pe(s0);
  return o.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function c0(e) {
  const t = Ss(), n = re(!1);
  Q(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const l0 = (e) => e.panZoom?.syncViewport;
function u0(e) {
  const t = pe(l0), n = ve();
  return Q(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function d0(e) {
  return e.connection.inProgress ? { ...e.connection, to: fn(e.connection.to, e.transform) } : { ...e.connection };
}
function f0(e) {
  return d0;
}
function p0(e) {
  const t = f0();
  return pe(t, we);
}
const h0 = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function g0({ containerStyle: e, style: t, type: n, component: i }) {
  const { nodesConnectable: r, width: s, height: a, isValid: c, inProgress: u } = pe(h0, we);
  return !(s && r && u) ? null : o.jsx("svg", { style: e, width: s, height: a, className: "react-flow__connectionline react-flow__container", children: o.jsx("g", { className: ke(["react-flow__connection", uu(c)]), children: o.jsx(gd, { style: t, type: n, CustomComponent: i, isValid: c }) }) });
}
const gd = ({ style: e, type: t = yt.Bezier, CustomComponent: n, isValid: i }) => {
  const { inProgress: r, from: s, fromNode: a, fromHandle: c, fromPosition: u, to: l, toNode: d, toHandle: f, toPosition: p, pointer: h } = p0();
  if (!r)
    return;
  if (n)
    return o.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: a, fromHandle: c, fromX: s.x, fromY: s.y, toX: l.x, toY: l.y, fromPosition: u, toPosition: p, connectionStatus: uu(i), toNode: d, toHandle: f, pointer: h });
  let y = "";
  const w = {
    sourceX: s.x,
    sourceY: s.y,
    sourcePosition: u,
    targetX: l.x,
    targetY: l.y,
    targetPosition: p
  };
  switch (t) {
    case yt.Bezier:
      [y] = Nu(w);
      break;
    case yt.SimpleBezier:
      [y] = nd(w);
      break;
    case yt.Step:
      [y] = Gi({
        ...w,
        borderRadius: 0
      });
      break;
    case yt.SmoothStep:
      [y] = Gi(w);
      break;
    default:
      [y] = Cu(w);
  }
  return o.jsx("path", { d: y, fill: "none", className: "react-flow__connection-path", style: e });
};
gd.displayName = "ConnectionLine";
const y0 = {};
function hc(e = y0) {
  re(e), ve(), Q(() => {
  }, [e]);
}
function m0() {
  ve(), re(!1), Q(() => {
  }, []);
}
function yd({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: i, onEdgeClick: r, onNodeDoubleClick: s, onEdgeDoubleClick: a, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: p, onSelectionEnd: h, connectionLineType: y, connectionLineStyle: w, connectionLineComponent: x, connectionLineContainerStyle: m, selectionKeyCode: b, selectionOnDrag: g, selectionMode: v, multiSelectionKeyCode: j, panActivationKeyCode: N, zoomActivationKeyCode: S, deleteKeyCode: k, onlyRenderVisibleElements: _, elementsSelectable: M, defaultViewport: I, translateExtent: $, minZoom: z, maxZoom: C, preventScrolling: A, defaultMarkerColor: E, zoomOnScroll: D, zoomOnPinch: P, panOnScroll: T, panOnScrollSpeed: B, panOnScrollMode: W, zoomOnDoubleClick: H, panOnDrag: Y, autoPanOnSelection: V, onPaneClick: q, onPaneMouseEnter: G, onPaneMouseMove: Z, onPaneMouseLeave: R, onPaneScroll: X, onPaneContextMenu: ce, paneClickDistance: se, nodeClickDistance: ee, onEdgeContextMenu: ne, onEdgeMouseEnter: de, onEdgeMouseMove: O, onEdgeMouseLeave: te, reconnectRadius: ge, onReconnect: ye, onReconnectStart: Ae, onReconnectEnd: _e, noDragClassName: $e, noWheelClassName: ot, noPanClassName: Xe, disableKeyboardA11y: qe, nodeExtent: Pe, rfId: Oe, viewport: He, onViewportChange: Ne }) {
  return hc(e), hc(t), m0(), c0(n), u0(He), o.jsx(Tv, { onPaneClick: q, onPaneMouseEnter: G, onPaneMouseMove: Z, onPaneMouseLeave: R, onPaneContextMenu: ce, onPaneScroll: X, paneClickDistance: se, deleteKeyCode: k, selectionKeyCode: b, selectionOnDrag: g, selectionMode: v, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: j, panActivationKeyCode: N, zoomActivationKeyCode: S, elementsSelectable: M, zoomOnScroll: D, zoomOnPinch: P, zoomOnDoubleClick: H, panOnScroll: T, panOnScrollSpeed: B, panOnScrollMode: W, panOnDrag: Y, autoPanOnSelection: V, defaultViewport: I, translateExtent: $, minZoom: z, maxZoom: C, onSelectionContextMenu: f, preventScrolling: A, noDragClassName: $e, noWheelClassName: ot, noPanClassName: Xe, disableKeyboardA11y: qe, onViewportChange: Ne, isControlledViewport: !!He, children: o.jsxs(a0, { children: [o.jsx(o0, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: a, onReconnect: ye, onReconnectStart: Ae, onReconnectEnd: _e, onlyRenderVisibleElements: _, onEdgeContextMenu: ne, onEdgeMouseEnter: de, onEdgeMouseMove: O, onEdgeMouseLeave: te, reconnectRadius: ge, defaultMarkerColor: E, noPanClassName: Xe, disableKeyboardA11y: qe, rfId: Oe }), o.jsx(g0, { style: w, type: y, component: x, containerStyle: m }), o.jsx("div", { className: "react-flow__edgelabel-renderer" }), o.jsx(Hv, { nodeTypes: e, onNodeClick: i, onNodeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, nodeClickDistance: ee, onlyRenderVisibleElements: _, noPanClassName: Xe, noDragClassName: $e, disableKeyboardA11y: qe, nodeExtent: Pe, rfId: Oe }), o.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
yd.displayName = "GraphView";
const x0 = Se(yd), w0 = gu(), gc = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, width: r, height: s, fitView: a, fitViewOptions: c, minZoom: u = 0.5, maxZoom: l = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: p = "basic" } = {}) => {
  const h = /* @__PURE__ */ new Map(), y = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), m = i ?? t ?? [], b = n ?? e ?? [], g = d ?? [0, 0], v = f ?? Wn;
  Iu(w, x, m);
  const { nodesInitialized: j } = Ro(b, h, y, {
    nodeOrigin: g,
    nodeExtent: v,
    zIndexMode: p
  });
  let N = [0, 0, 1];
  if (a && r && s) {
    const S = ti(h, {
      filter: (I) => !!((I.width || I.initialWidth) && (I.height || I.initialHeight))
    }), { x: k, y: _, zoom: M } = ms(S, r, s, u, l, c?.padding ?? 0.1);
    N = [k, _, M];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: s ?? 0,
    transform: N,
    nodes: b,
    nodesInitialized: j,
    nodeLookup: h,
    parentLookup: y,
    edges: m,
    edgeLookup: x,
    connectionLookup: w,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: i !== void 0,
    panZoom: null,
    minZoom: u,
    maxZoom: l,
    translateExtent: Wn,
    nodeExtent: v,
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
    connection: { ...lu },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: w0,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: cu,
    zIndexMode: p,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, v0 = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, width: r, height: s, fitView: a, fitViewOptions: c, minZoom: u, maxZoom: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p }) => $w((h, y) => {
  async function w() {
    const { nodeLookup: x, panZoom: m, fitViewOptions: b, fitViewResolver: g, width: v, height: j, minZoom: N, maxZoom: S } = y();
    m && (await Ax({
      nodes: x,
      width: v,
      height: j,
      panZoom: m,
      minZoom: N,
      maxZoom: S
    }, b), g?.resolve(!0), h({ fitViewResolver: null }));
  }
  return {
    ...gc({
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
      defaultEdges: i,
      zIndexMode: p
    }),
    setNodes: (x) => {
      const { nodeLookup: m, parentLookup: b, nodeOrigin: g, elevateNodesOnSelect: v, fitViewQueued: j, zIndexMode: N, nodesSelectionActive: S } = y(), { nodesInitialized: k, hasSelectedNodes: _ } = Ro(x, m, b, {
        nodeOrigin: g,
        nodeExtent: f,
        elevateNodesOnSelect: v,
        checkEquality: !0,
        zIndexMode: N
      }), M = S && _;
      j && k ? (w(), h({
        nodes: x,
        nodesInitialized: k,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: M
      })) : h({ nodes: x, nodesInitialized: k, nodesSelectionActive: M });
    },
    setEdges: (x) => {
      const { connectionLookup: m, edgeLookup: b } = y();
      Iu(m, b, x), h({ edges: x });
    },
    setDefaultNodesAndEdges: (x, m) => {
      if (x) {
        const { setNodes: b } = y();
        b(x), h({ hasDefaultNodes: !0 });
      }
      if (m) {
        const { setEdges: b } = y();
        b(m), h({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (x) => {
      const { triggerNodeChanges: m, nodeLookup: b, parentLookup: g, domNode: v, nodeOrigin: j, nodeExtent: N, debug: S, fitViewQueued: k, zIndexMode: _ } = y(), { changes: M, updatedInternals: I } = Jx(x, b, g, v, j, N, _);
      I && (Yx(b, g, { nodeOrigin: j, nodeExtent: N, zIndexMode: _ }), k ? (w(), h({ fitViewQueued: !1, fitViewOptions: void 0 })) : h({}), M?.length > 0 && (S && console.log("React Flow: trigger node changes", M), m?.(M)));
    },
    updateNodePositions: (x, m = !1) => {
      const b = [];
      let g = [];
      const { nodeLookup: v, triggerNodeChanges: j, connection: N, updateConnection: S, onNodesChangeMiddlewareMap: k } = y();
      for (const [_, M] of x) {
        const I = v.get(_), $ = !!(I?.expandParent && I?.parentId && M?.position), z = {
          id: _,
          type: "position",
          position: $ ? {
            x: Math.max(0, M.position.x),
            y: Math.max(0, M.position.y)
          } : M.position,
          dragging: m
        };
        if (I && N.inProgress && N.fromNode.id === I.id) {
          const C = $t(I, N.fromHandle, oe.Left, !0);
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
        }), g.push(z);
      }
      if (b.length > 0) {
        const { parentLookup: _, nodeOrigin: M } = y(), I = js(b, v, _, M);
        g.push(...I);
      }
      for (const _ of k.values())
        g = _(g);
      j(g);
    },
    triggerNodeChanges: (x) => {
      const { onNodesChange: m, setNodes: b, nodes: g, hasDefaultNodes: v, debug: j } = y();
      if (x?.length) {
        if (v) {
          const N = Bu(x, g);
          b(N);
        }
        j && console.log("React Flow: trigger node changes", x), m?.(x);
      }
    },
    triggerEdgeChanges: (x) => {
      const { onEdgesChange: m, setEdges: b, edges: g, hasDefaultEdges: v, debug: j } = y();
      if (x?.length) {
        if (v) {
          const N = Fu(x, g);
          b(N);
        }
        j && console.log("React Flow: trigger edge changes", x), m?.(x);
      }
    },
    addSelectedNodes: (x) => {
      const { multiSelectionActive: m, edgeLookup: b, nodeLookup: g, triggerNodeChanges: v, triggerEdgeChanges: j } = y();
      if (m) {
        const N = x.map((S) => Nt(S, !0));
        v(N);
        return;
      }
      v(Xt(g, /* @__PURE__ */ new Set([...x]), !0)), j(Xt(b));
    },
    addSelectedEdges: (x) => {
      const { multiSelectionActive: m, edgeLookup: b, nodeLookup: g, triggerNodeChanges: v, triggerEdgeChanges: j } = y();
      if (m) {
        const N = x.map((S) => Nt(S, !0));
        j(N);
        return;
      }
      j(Xt(b, /* @__PURE__ */ new Set([...x]))), v(Xt(g, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: x, edges: m } = {}) => {
      const { edges: b, nodes: g, nodeLookup: v, triggerNodeChanges: j, triggerEdgeChanges: N } = y(), S = x || g, k = m || b, _ = [];
      for (const I of S) {
        if (!I.selected)
          continue;
        const $ = v.get(I.id);
        $ && ($.selected = !1), _.push(Nt(I.id, !1));
      }
      const M = [];
      for (const I of k)
        I.selected && M.push(Nt(I.id, !1));
      j(_), N(M);
    },
    setMinZoom: (x) => {
      const { panZoom: m, maxZoom: b } = y();
      m?.setScaleExtent([x, b]), h({ minZoom: x });
    },
    setMaxZoom: (x) => {
      const { panZoom: m, minZoom: b } = y();
      m?.setScaleExtent([b, x]), h({ maxZoom: x });
    },
    setTranslateExtent: (x) => {
      y().panZoom?.setTranslateExtent(x), h({ translateExtent: x });
    },
    resetSelectedElements: () => {
      const { edges: x, nodes: m, triggerNodeChanges: b, triggerEdgeChanges: g, elementsSelectable: v } = y();
      if (!v)
        return;
      const j = m.reduce((S, k) => k.selected ? [...S, Nt(k.id, !1)] : S, []), N = x.reduce((S, k) => k.selected ? [...S, Nt(k.id, !1)] : S, []);
      b(j), g(N);
    },
    setNodeExtent: (x) => {
      const { nodes: m, nodeLookup: b, parentLookup: g, nodeOrigin: v, elevateNodesOnSelect: j, nodeExtent: N, zIndexMode: S } = y();
      x[0][0] === N[0][0] && x[0][1] === N[0][1] && x[1][0] === N[1][0] && x[1][1] === N[1][1] || (Ro(m, b, g, {
        nodeOrigin: v,
        nodeExtent: x,
        elevateNodesOnSelect: j,
        checkEquality: !1,
        zIndexMode: S
      }), h({ nodeExtent: x }));
    },
    panBy: (x) => {
      const { transform: m, width: b, height: g, panZoom: v, translateExtent: j } = y();
      return Qx({ delta: x, panZoom: v, transform: m, translateExtent: j, width: b, height: g });
    },
    setCenter: async (x, m, b) => {
      const { width: g, height: v, maxZoom: j, panZoom: N } = y();
      if (!N)
        return !1;
      const S = typeof b?.zoom < "u" ? b.zoom : j;
      return await N.setViewport({
        x: g / 2 - x * S,
        y: v / 2 - m * S,
        zoom: S
      }, { duration: b?.duration, ease: b?.ease, interpolate: b?.interpolate }), !0;
    },
    cancelConnection: () => {
      h({
        connection: { ...lu }
      });
    },
    updateConnection: (x) => {
      h({ connection: x });
    },
    reset: () => h({ ...gc() })
  };
}, Object.is);
function b0({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: i, initialWidth: r, initialHeight: s, initialMinZoom: a, initialMaxZoom: c, initialFitViewOptions: u, fitView: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p, children: h }) {
  const [y] = K(() => v0({
    nodes: e,
    edges: t,
    defaultNodes: n,
    defaultEdges: i,
    width: r,
    height: s,
    fitView: l,
    minZoom: a,
    maxZoom: c,
    fitViewOptions: u,
    nodeOrigin: d,
    nodeExtent: f,
    zIndexMode: p
  }));
  return o.jsx(Lw, { value: y, children: o.jsx(av, { children: h }) });
}
function N0({ children: e, nodes: t, edges: n, defaultNodes: i, defaultEdges: r, width: s, height: a, fitView: c, fitViewOptions: u, minZoom: l, maxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h }) {
  return qn(pr) ? o.jsx(o.Fragment, { children: e }) : o.jsx(b0, { initialNodes: t, initialEdges: n, defaultNodes: i, defaultEdges: r, initialWidth: s, initialHeight: a, fitView: c, initialFitViewOptions: u, initialMinZoom: l, initialMaxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h, children: e });
}
const j0 = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function S0({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, className: r, nodeTypes: s, edgeTypes: a, onNodeClick: c, onEdgeClick: u, onInit: l, onMove: d, onMoveStart: f, onMoveEnd: p, onConnect: h, onConnectStart: y, onConnectEnd: w, onClickConnectStart: x, onClickConnectEnd: m, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: v, onNodeContextMenu: j, onNodeDoubleClick: N, onNodeDragStart: S, onNodeDrag: k, onNodeDragStop: _, onNodesDelete: M, onEdgesDelete: I, onDelete: $, onSelectionChange: z, onSelectionDragStart: C, onSelectionDrag: A, onSelectionDragStop: E, onSelectionContextMenu: D, onSelectionStart: P, onSelectionEnd: T, onBeforeDelete: B, connectionMode: W, connectionLineType: H = yt.Bezier, connectionLineStyle: Y, connectionLineComponent: V, connectionLineContainerStyle: q, deleteKeyCode: G = "Backspace", selectionKeyCode: Z = "Shift", selectionOnDrag: R = !1, selectionMode: X = Bn.Full, panActivationKeyCode: ce = "Space", multiSelectionKeyCode: se = Kn() ? "Meta" : "Control", zoomActivationKeyCode: ee = Kn() ? "Meta" : "Control", snapToGrid: ne, snapGrid: de, onlyRenderVisibleElements: O = !1, selectNodesOnDrag: te, nodesDraggable: ge, autoPanOnNodeFocus: ye, nodesConnectable: Ae, nodesFocusable: _e, nodeOrigin: $e = Hu, edgesFocusable: ot, edgesReconnectable: Xe, elementsSelectable: qe = !0, defaultViewport: Pe = Zw, minZoom: Oe = 0.5, maxZoom: He = 2, translateExtent: Ne = Wn, preventScrolling: Lt = !0, nodeExtent: De, defaultMarkerColor: hn = "#b1b1b7", zoomOnScroll: L = !0, zoomOnPinch: F = !0, panOnScroll: U = !1, panOnScrollSpeed: ie = 0.5, panOnScrollMode: le = Ct.Free, zoomOnDoubleClick: he = !0, panOnDrag: me = !0, onPaneClick: Ie, onPaneMouseEnter: Ce, onPaneMouseMove: Le, onPaneMouseLeave: xe, onPaneScroll: zt, onPaneContextMenu: xr, paneClickDistance: oi = 1, nodeClickDistance: wr = 0, children: vr, onReconnect: pt, onReconnectStart: br, onReconnectEnd: Vt, onEdgeContextMenu: Nr, onEdgeDoubleClick: Ot, onEdgeMouseEnter: jr, onEdgeMouseMove: si, onEdgeMouseLeave: ai, reconnectRadius: Sr = 10, onNodesChange: Ht, onEdgesChange: Cr, noDragClassName: Er = "nodrag", noWheelClassName: kr = "nowheel", noPanClassName: Wt = "nopan", fitView: ci, fitViewOptions: li, connectOnClick: Ir, attributionPosition: Ar, proOptions: _r, defaultEdgeOptions: Dr, elevateNodesOnSelect: Tr = !0, elevateEdgesOnSelect: $r = !1, disableKeyboardA11y: ui = !1, autoPanOnConnect: Pr, autoPanOnNodeDrag: Mr, autoPanOnSelection: Rr = !0, autoPanSpeed: Lr, connectionRadius: zr, isValidConnection: di, onError: fi, style: pi, id: gn, nodeDragThreshold: Vr, connectionDragThreshold: Or, viewport: Hr, onViewportChange: Wr, width: Br, height: Fr, colorMode: Kr = "light", debug: Xr, onScroll: hi, ariaLabelConfig: qr, zIndexMode: gi = "basic", ...Yr }, Ur) {
  const yn = gn || "1", Zr = ev(Kr), Gr = ae((bt) => {
    bt.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), hi?.(bt);
  }, [hi]);
  return o.jsx("div", { "data-testid": "rf__wrapper", ...Yr, onScroll: Gr, style: { ...pi, ...j0 }, ref: Ur, className: ke(["react-flow", r, Zr]), id: gn, role: "application", children: o.jsxs(N0, { nodes: e, edges: t, width: Br, height: Fr, fitView: ci, fitViewOptions: li, minZoom: Oe, maxZoom: He, nodeOrigin: $e, nodeExtent: De, zIndexMode: gi, children: [o.jsx(Qw, { nodes: e, edges: t, defaultNodes: n, defaultEdges: i, onConnect: h, onConnectStart: y, onConnectEnd: w, onClickConnectStart: x, onClickConnectEnd: m, nodesDraggable: ge, autoPanOnNodeFocus: ye, nodesConnectable: Ae, nodesFocusable: _e, edgesFocusable: ot, edgesReconnectable: Xe, elementsSelectable: qe, elevateNodesOnSelect: Tr, elevateEdgesOnSelect: $r, minZoom: Oe, maxZoom: He, nodeExtent: De, onNodesChange: Ht, onEdgesChange: Cr, snapToGrid: ne, snapGrid: de, connectionMode: W, translateExtent: Ne, connectOnClick: Ir, defaultEdgeOptions: Dr, fitView: ci, fitViewOptions: li, onNodesDelete: M, onEdgesDelete: I, onDelete: $, onNodeDragStart: S, onNodeDrag: k, onNodeDragStop: _, onSelectionDrag: A, onSelectionDragStart: C, onSelectionDragStop: E, onMove: d, onMoveStart: f, onMoveEnd: p, noPanClassName: Wt, nodeOrigin: $e, rfId: yn, autoPanOnConnect: Pr, autoPanOnNodeDrag: Mr, autoPanSpeed: Lr, onError: fi, connectionRadius: zr, isValidConnection: di, selectNodesOnDrag: te, nodeDragThreshold: Vr, connectionDragThreshold: Or, onBeforeDelete: B, debug: Xr, ariaLabelConfig: qr, zIndexMode: gi }), o.jsx(x0, { onInit: l, onNodeClick: c, onEdgeClick: u, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: v, onNodeContextMenu: j, onNodeDoubleClick: N, nodeTypes: s, edgeTypes: a, connectionLineType: H, connectionLineStyle: Y, connectionLineComponent: V, connectionLineContainerStyle: q, selectionKeyCode: Z, selectionOnDrag: R, selectionMode: X, deleteKeyCode: G, multiSelectionKeyCode: se, panActivationKeyCode: ce, zoomActivationKeyCode: ee, onlyRenderVisibleElements: O, defaultViewport: Pe, translateExtent: Ne, minZoom: Oe, maxZoom: He, preventScrolling: Lt, zoomOnScroll: L, zoomOnPinch: F, zoomOnDoubleClick: he, panOnScroll: U, panOnScrollSpeed: ie, panOnScrollMode: le, panOnDrag: me, autoPanOnSelection: Rr, onPaneClick: Ie, onPaneMouseEnter: Ce, onPaneMouseMove: Le, onPaneMouseLeave: xe, onPaneScroll: zt, onPaneContextMenu: xr, paneClickDistance: oi, nodeClickDistance: wr, onSelectionContextMenu: D, onSelectionStart: P, onSelectionEnd: T, onReconnect: pt, onReconnectStart: br, onReconnectEnd: Vt, onEdgeContextMenu: Nr, onEdgeDoubleClick: Ot, onEdgeMouseEnter: jr, onEdgeMouseMove: si, onEdgeMouseLeave: ai, reconnectRadius: Sr, defaultMarkerColor: hn, noDragClassName: Er, noWheelClassName: kr, noPanClassName: Wt, rfId: yn, disableKeyboardA11y: ui, nodeExtent: De, viewport: Hr, onViewportChange: Wr }), o.jsx(Uw, { onSelectionChange: z }), vr, o.jsx(Fw, { proOptions: _r, position: Ar }), o.jsx(Bw, { rfId: yn, disableKeyboardA11y: ui })] }) });
}
var md = qu(S0);
const C0 = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function E0({ children: e }) {
  const t = pe(C0);
  return t ? Rw.createPortal(e, t) : null;
}
function k0({ dimensions: e, lineWidth: t, variant: n, className: i }) {
  return o.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: ke(["react-flow__background-pattern", n, i]) });
}
function I0({ radius: e, className: t }) {
  return o.jsx("circle", { cx: e, cy: e, r: e, className: ke(["react-flow__background-pattern", "dots", t]) });
}
var xt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(xt || (xt = {}));
const A0 = {
  [xt.Dots]: 1,
  [xt.Lines]: 1,
  [xt.Cross]: 6
}, _0 = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function xd({
  id: e,
  variant: t = xt.Dots,
  // only used for dots and cross
  gap: n = 20,
  // only used for lines and cross
  size: i,
  lineWidth: r = 1,
  offset: s = 0,
  color: a,
  bgColor: c,
  style: u,
  className: l,
  patternClassName: d
}) {
  const f = re(null), { transform: p, patternId: h } = pe(_0, we), y = i || A0[t], w = t === xt.Dots, x = t === xt.Cross, m = Array.isArray(n) ? n : [n, n], b = [m[0] * p[2] || 1, m[1] * p[2] || 1], g = y * p[2], v = Array.isArray(s) ? s : [s, s], j = x ? [g, g] : b, N = [
    v[0] * p[2] || 1 + j[0] / 2,
    v[1] * p[2] || 1 + j[1] / 2
  ], S = `${h}${e || ""}`;
  return o.jsxs("svg", { className: ke(["react-flow__background", l]), style: {
    ...u,
    ...gr,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": a
  }, ref: f, "data-testid": "rf__background", children: [o.jsx("pattern", { id: S, x: p[0] % b[0], y: p[1] % b[1], width: b[0], height: b[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${N[0]},-${N[1]})`, children: w ? o.jsx(I0, { radius: g / 2, className: d }) : o.jsx(k0, { dimensions: j, lineWidth: r, variant: t, className: d }) }), o.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${S})` })] });
}
xd.displayName = "Background";
const wd = Se(xd);
function D0() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: o.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function T0() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: o.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function $0() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: o.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function P0() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: o.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function M0() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: o.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function Si({ children: e, className: t, ...n }) {
  return o.jsx("button", { type: "button", className: ke(["react-flow__controls-button", t]), ...n, children: e });
}
const R0 = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function vd({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: i = !0, fitViewOptions: r, onZoomIn: s, onZoomOut: a, onFitView: c, onInteractiveChange: u, className: l, children: d, position: f = "bottom-left", orientation: p = "vertical", "aria-label": h }) {
  const y = ve(), { isInteractive: w, minZoomReached: x, maxZoomReached: m, ariaLabelConfig: b } = pe(R0, we), { zoomIn: g, zoomOut: v, fitView: j } = Ss(), N = () => {
    g(), s?.();
  }, S = () => {
    v(), a?.();
  }, k = () => {
    j(r), c?.();
  }, _ = () => {
    y.setState({
      nodesDraggable: !w,
      nodesConnectable: !w,
      elementsSelectable: !w
    }), u?.(!w);
  }, M = p === "horizontal" ? "horizontal" : "vertical";
  return o.jsxs(hr, { className: ke(["react-flow__controls", M, l]), position: f, style: e, "data-testid": "rf__controls", "aria-label": h ?? b["controls.ariaLabel"], children: [t && o.jsxs(o.Fragment, { children: [o.jsx(Si, { onClick: N, className: "react-flow__controls-zoomin", title: b["controls.zoomIn.ariaLabel"], "aria-label": b["controls.zoomIn.ariaLabel"], disabled: m, children: o.jsx(D0, {}) }), o.jsx(Si, { onClick: S, className: "react-flow__controls-zoomout", title: b["controls.zoomOut.ariaLabel"], "aria-label": b["controls.zoomOut.ariaLabel"], disabled: x, children: o.jsx(T0, {}) })] }), n && o.jsx(Si, { className: "react-flow__controls-fitview", onClick: k, title: b["controls.fitView.ariaLabel"], "aria-label": b["controls.fitView.ariaLabel"], children: o.jsx($0, {}) }), i && o.jsx(Si, { className: "react-flow__controls-interactive", onClick: _, title: b["controls.interactive.ariaLabel"], "aria-label": b["controls.interactive.ariaLabel"], children: w ? o.jsx(M0, {}) : o.jsx(P0, {}) }), d] });
}
vd.displayName = "Controls";
const bd = Se(vd);
function L0({ id: e, x: t, y: n, width: i, height: r, style: s, color: a, strokeColor: c, strokeWidth: u, className: l, borderRadius: d, shapeRendering: f, selected: p, onClick: h }) {
  const { background: y, backgroundColor: w } = s || {}, x = a || y || w;
  return o.jsx("rect", { className: ke(["react-flow__minimap-node", { selected: p }, l]), x: t, y: n, rx: d, ry: d, width: i, height: r, style: {
    fill: x,
    stroke: c,
    strokeWidth: u
  }, shapeRendering: f, onClick: h ? (m) => h(m, e) : void 0 });
}
const z0 = Se(L0), V0 = (e) => e.nodes.map((t) => t.id), wo = (e) => e instanceof Function ? e : () => e;
function O0({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: i = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: s = z0,
  onClick: a
}) {
  const c = pe(V0, we), u = wo(t), l = wo(e), d = wo(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return o.jsx(o.Fragment, { children: c.map((p) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    o.jsx(W0, { id: p, nodeColorFunc: u, nodeStrokeColorFunc: l, nodeClassNameFunc: d, nodeBorderRadius: i, nodeStrokeWidth: r, NodeComponent: s, onClick: a, shapeRendering: f }, p)
  )) });
}
function H0({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: i, nodeBorderRadius: r, nodeStrokeWidth: s, shapeRendering: a, NodeComponent: c, onClick: u }) {
  const { node: l, x: d, y: f, width: p, height: h } = pe((y) => {
    const w = y.nodeLookup.get(e);
    if (!w)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const x = w.internals.userNode, { x: m, y: b } = w.internals.positionAbsolute, { width: g, height: v } = ft(x);
    return {
      node: x,
      x: m,
      y: b,
      width: g,
      height: v
    };
  }, we);
  return !l || l.hidden || !yu(l) ? null : o.jsx(c, { x: d, y: f, width: p, height: h, style: l.style, selected: !!l.selected, className: i(l), color: t(l), borderRadius: r, strokeColor: n(l), strokeWidth: s, shapeRendering: a, onClick: u, id: l.id });
}
const W0 = Se(H0);
var B0 = Se(O0);
const F0 = 200, K0 = 150, X0 = (e) => !e.hidden, q0 = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? hu(ti(e.nodeLookup, { filter: X0 }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, Y0 = "react-flow__minimap-desc";
function Nd({
  style: e,
  className: t,
  nodeStrokeColor: n,
  nodeColor: i,
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
  position: p = "bottom-right",
  onClick: h,
  onNodeClick: y,
  pannable: w = !1,
  zoomable: x = !1,
  ariaLabel: m,
  inversePan: b,
  zoomStep: g = 1,
  offsetScale: v = 5
}) {
  const j = ve(), N = re(null), { boundingRect: S, viewBB: k, rfId: _, panZoom: M, translateExtent: I, flowWidth: $, flowHeight: z, ariaLabelConfig: C } = pe(q0, we), A = e?.width ?? F0, E = e?.height ?? K0, D = S.width / A, P = S.height / E, T = Math.max(D, P), B = T * A, W = T * E, H = v * T, Y = S.x - (B - S.width) / 2 - H, V = S.y - (W - S.height) / 2 - H, q = B + H * 2, G = W + H * 2, Z = `${Y0}-${_}`, R = re(0), X = re();
  R.current = T, Q(() => {
    if (N.current && M)
      return X.current = cw({
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
      height: z,
      inversePan: b,
      pannable: w,
      zoomStep: g,
      zoomable: x
    });
  }, [w, x, b, g, I, $, z]);
  const ce = h ? (ne) => {
    const [de, O] = X.current?.pointer(ne) || [0, 0];
    h(ne, { x: de, y: O });
  } : void 0, se = y ? ae((ne, de) => {
    const O = j.getState().nodeLookup.get(de).internals.userNode;
    y(ne, O);
  }, []) : void 0, ee = m ?? C["minimap.ariaLabel"];
  return o.jsx(hr, { position: p, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * T : void 0,
    "--xy-minimap-node-background-color-props": typeof i == "string" ? i : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof a == "number" ? a : void 0
  }, className: ke(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: o.jsxs("svg", { width: A, height: E, viewBox: `${Y} ${V} ${q} ${G}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": Z, ref: N, onClick: ce, children: [ee && o.jsx("title", { id: Z, children: ee }), o.jsx(B0, { onClick: se, nodeColor: i, nodeStrokeColor: n, nodeBorderRadius: s, nodeClassName: r, nodeStrokeWidth: a, nodeComponent: c }), o.jsx("path", { className: "react-flow__minimap-mask", d: `M${Y - H},${V - H}h${q + H * 2}v${G + H * 2}h${-q - H * 2}z
        M${k.x},${k.y}h${k.width}v${k.height}h${-k.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
Nd.displayName = "MiniMap";
const jd = Se(Nd), U0 = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, Z0 = {
  [an.Line]: "right",
  [an.Handle]: "bottom-right"
};
function G0({ nodeId: e, position: t, variant: n = an.Handle, className: i, style: r = void 0, children: s, color: a, minWidth: c = 10, minHeight: u = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: p, autoScale: h = !0, shouldResize: y, onResizeStart: w, onResize: x, onResizeEnd: m }) {
  const b = Gu(), g = typeof e == "string" ? e : b, v = ve(), j = re(null), N = n === an.Handle, S = pe(ae(U0(N && h), [N, h]), we), k = re(null), _ = t ?? Z0[n];
  Q(() => {
    if (!(!j.current || !g))
      return k.current || (k.current = bw({
        domNode: j.current,
        nodeId: g,
        getStoreItems: () => {
          const { nodeLookup: I, transform: $, snapGrid: z, snapToGrid: C, nodeOrigin: A, domNode: E } = v.getState();
          return {
            nodeLookup: I,
            transform: $,
            snapGrid: z,
            snapToGrid: C,
            nodeOrigin: A,
            paneDomNode: E
          };
        },
        onChange: (I, $) => {
          const { triggerNodeChanges: z, nodeLookup: C, parentLookup: A, nodeOrigin: E } = v.getState(), D = [], P = { x: I.x, y: I.y }, T = C.get(g);
          if (T && T.expandParent && T.parentId) {
            const B = T.origin ?? E, W = I.width ?? T.measured.width ?? 0, H = I.height ?? T.measured.height ?? 0, Y = {
              id: T.id,
              parentId: T.parentId,
              rect: {
                width: W,
                height: H,
                ...mu({
                  x: I.x ?? T.position.x,
                  y: I.y ?? T.position.y
                }, { width: W, height: H }, T.parentId, C, B)
              }
            }, V = js([Y], C, A, E);
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
          z(D);
        },
        onEnd: ({ width: I, height: $ }) => {
          const z = {
            id: g,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: I,
              height: $
            }
          };
          v.getState().triggerNodeChanges([z]);
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
        onResizeStart: w,
        onResize: x,
        onResizeEnd: m,
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
    w,
    x,
    m,
    y
  ]);
  const M = _.split("-");
  return o.jsx("div", { className: ke(["react-flow__resize-control", "nodrag", ...M, n, i]), ref: j, style: {
    ...r,
    scale: S,
    ...a && { [N ? "backgroundColor" : "borderColor"]: a }
  }, children: s });
}
Se(G0);
function J0(e, t) {
  return {
    ...t ? { name: t } : {},
    definitionId: e.definitionId,
    state: Un(e.state),
    layout: e.layout
  };
}
function Q0(e) {
  return JSON.stringify(
    {
      state: Un(e.state),
      layout: e.layout
    },
    null,
    2
  );
}
function eb(e, t) {
  let n;
  try {
    n = JSON.parse(e);
  } catch (r) {
    return { ok: !1, error: r instanceof Error ? r.message : String(r) };
  }
  if (!n || typeof n != "object")
    return { ok: !1, error: "Workflow JSON must be an object with a 'state' property." };
  const i = n;
  return !i.state || typeof i.state != "object" ? { ok: !1, error: "Workflow JSON is missing a valid 'state' object." } : i.layout !== void 0 && !Array.isArray(i.layout) ? { ok: !1, error: "'layout' must be an array when present." } : {
    ok: !0,
    draft: {
      ...t,
      state: tr(i.state),
      layout: i.layout ?? t.layout
    }
  };
}
function tb(e, t) {
  const n = (t ?? "workflow").trim().replace(/[^\w.-]+/g, "-") || "workflow", i = new Blob([JSON.stringify(e, null, 2)], { type: "application/json" }), r = URL.createObjectURL(i), s = document.createElement("a");
  s.href = r, s.download = `${n}.json`, document.body.appendChild(s), s.click(), s.remove(), URL.revokeObjectURL(r);
}
function yc({
  document: e,
  readOnly: t,
  minHeight: n,
  ariaLabel: i,
  onChange: r
}) {
  const s = (a) => {
    t || r({ ...e, value: a.target.value });
  };
  return /* @__PURE__ */ o.jsx(
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
function nb({
  document: e,
  diagnostics: t = [],
  readOnly: n = !1,
  theme: i = "studio",
  minHeight: r = "220px",
  ariaLabel: s,
  languageAdapter: a,
  onChange: c
}) {
  const u = t.filter((p) => !p.uri || p.uri === e.uri), l = a?.displayName ?? e.language, d = a?.loadEditor, f = ue(
    () => d ? nf(d) : null,
    [d]
  );
  return /* @__PURE__ */ o.jsxs(
    "section",
    {
      className: "studio-code-editor",
      "data-language": e.language,
      "data-theme": i,
      "data-readonly": n,
      children: [
        /* @__PURE__ */ o.jsxs("div", { className: "studio-code-editor-header", children: [
          /* @__PURE__ */ o.jsx("span", { children: l }),
          /* @__PURE__ */ o.jsx("code", { children: e.uri })
        ] }),
        f ? /* @__PURE__ */ o.jsx(rf, { fallback: /* @__PURE__ */ o.jsx(
          yc,
          {
            document: e,
            readOnly: n,
            minHeight: r,
            ariaLabel: s,
            onChange: c
          }
        ), children: /* @__PURE__ */ o.jsx(
          f,
          {
            document: e,
            readOnly: n,
            theme: i,
            minHeight: r,
            ariaLabel: s,
            onChange: c
          }
        ) }) : /* @__PURE__ */ o.jsx(
          yc,
          {
            document: e,
            readOnly: n,
            minHeight: r,
            ariaLabel: s,
            onChange: c
          }
        ),
        /* @__PURE__ */ o.jsx(ib, { diagnostics: u })
      ]
    }
  );
}
function ib({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ o.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const i = t.severity ?? "info", r = rb(t);
    return /* @__PURE__ */ o.jsxs(
      "p",
      {
        className: `studio-code-editor-diagnostic ${i}`,
        children: [
          t.code ? /* @__PURE__ */ o.jsx("span", { children: t.code }) : null,
          r ? /* @__PURE__ */ o.jsx("small", { children: r }) : null,
          t.message
        ]
      },
      `${t.uri ?? "document"}-${t.code ?? "diagnostic"}-${n}`
    );
  }) });
}
function rb(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const ob = { language: "json", displayName: "JSON" };
function sb({ draft: e, onApply: t }) {
  const n = ue(() => Q0(e), [e]), [i, r] = K(n), [s, a] = K(n), [c, u] = K(null);
  Q(() => {
    r(n), a(n), u(null);
  }, [n]);
  const l = i !== s, d = c ? [{ severity: "error", message: c }] : [], f = () => u(t(i));
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-code-view", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-code-view-toolbar", children: [
      /* @__PURE__ */ o.jsx("span", { className: "wf-muted", children: "Edit the workflow definition as JSON. Changes apply to the draft when you click Apply." }),
      /* @__PURE__ */ o.jsxs("span", { className: "wf-code-view-actions", children: [
        /* @__PURE__ */ o.jsx("button", { type: "button", disabled: !l, onClick: () => {
          r(s), u(null);
        }, children: "Reset" }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", disabled: !l, onClick: f, children: [
          /* @__PURE__ */ o.jsx(ln, { size: 14 }),
          " Apply"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ o.jsx("div", { className: "wf-code-view-editor", children: /* @__PURE__ */ o.jsx(
      nb,
      {
        ariaLabel: "Workflow JSON",
        document: { uri: "elsa://workflows/definition.json", language: "json", value: i },
        languageAdapter: ob,
        diagnostics: d,
        minHeight: "100%",
        theme: "studio",
        onChange: (p) => {
          r(p.value), c && u(null);
        }
      }
    ) })
  ] });
}
const ab = ["Single", "Array", "List", "HashSet"];
function Sd(e) {
  const [t, n] = K(null), [i, r] = K(null);
  Q(() => {
    let u = !1;
    return rh(e).then(
      (l) => {
        u || n(l);
      },
      () => {
        u || n([]);
      }
    ), sh(e).then(
      (l) => {
        u || r(l);
      },
      () => {
        u || r([]);
      }
    ), () => {
      u = !0;
    };
  }, [e]);
  const s = ue(
    () => t && t.length > 0 ? t.map((u) => {
      const l = Ws(u);
      return {
        value: l,
        label: Jc(u.displayName, l),
        group: u.category?.trim() || "Other"
      };
    }) : null,
    [t]
  ), a = ue(
    () => i && i.length > 0 ? i.filter((u) => !u.deprecated).map((u) => ({
      value: u.typeName,
      label: Lf(u.displayName, u.typeName)
    })) : null,
    [i]
  ), c = ue(() => {
    const u = /* @__PURE__ */ new Map();
    for (const l of t ?? []) {
      const d = Ws(l), f = l.defaultEditor?.trim();
      d && u.set(d, f && f.length > 0 ? f : "text");
    }
    return (l) => u.get(l) ?? "text";
  }, [t]);
  return { typeOptions: s, storageOptions: a, editorForAlias: c };
}
function cb(e) {
  return !e || e.length === 0 ? void 0 : (e.find((n) => /(^|\.)String$/i.test(n.value) || n.label.toLowerCase() === "string") ?? e[0]).value;
}
function lb(e, t, n) {
  return {
    add: () => {
      const i = kf(n.namePrefix, e.map((r) => ct(r, n.nameKeys)));
      t([...e, n.create(i)]);
    },
    update: (i, r) => t(e.map((s, a) => a === i ? n.patch(s, r) : s)),
    remove: (i) => t(e.filter((r, s) => s !== i))
  };
}
function mc({ value: e, options: t, placeholder: n, allowEmpty: i, ariaLabel: r, onChange: s }) {
  if (!t)
    return /* @__PURE__ */ o.jsx(
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
  return /* @__PURE__ */ o.jsxs("select", { "aria-label": r, value: e, onChange: (l) => s(l.target.value), children: [
    i ? /* @__PURE__ */ o.jsx("option", { value: "", children: n ?? "—" }) : null,
    a ? null : /* @__PURE__ */ o.jsxs("option", { value: e, disabled: !0, children: [
      e,
      " (unresolved)"
    ] }),
    u ? c.map((l) => /* @__PURE__ */ o.jsx("optgroup", { label: l, children: t.filter((d) => d.group === l).map((d) => /* @__PURE__ */ o.jsx("option", { value: d.value, children: d.label }, d.value)) }, l)) : t.map((l) => /* @__PURE__ */ o.jsx("option", { value: l.value, children: l.label }, l.value))
  ] });
}
const ub = {
  Single: "Single",
  Array: "Array",
  List: "List",
  HashSet: "Hash set"
};
function db({ value: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ o.jsx("select", { "aria-label": t, value: e, onChange: (i) => n(i.target.value), children: ab.map((i) => /* @__PURE__ */ o.jsx("option", { value: i, children: ub[i] }, i)) });
}
function fb(e, t) {
  return t === "" ? !0 : e === "checkbox" ? t === "true" || t === "false" || t === "True" || t === "False" : e === "number" ? Number.isFinite(Number(t.trim())) && t.trim() !== "" : e === "date" ? /^\d{4}-\d{2}-\d{2}$/.test(t) : !0;
}
function pb({ value: e, editor: t, ariaLabel: n, onChange: i }) {
  const r = fb(t, e);
  return r && t === "checkbox" ? /* @__PURE__ */ o.jsx(
    "input",
    {
      type: "checkbox",
      "aria-label": n,
      checked: e === "true" || e === "True",
      onChange: (s) => i(s.target.checked ? "true" : "false")
    }
  ) : r && (t === "number" || t === "date") ? /* @__PURE__ */ o.jsx(
    "input",
    {
      type: t,
      "aria-label": n,
      value: e,
      placeholder: "(empty)",
      onChange: (s) => i(s.target.value)
    }
  ) : /* @__PURE__ */ o.jsx(
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
function hb({ title: e, addLabel: t, emptyLabel: n, headers: i, isEmpty: r, onAdd: s, children: a }) {
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-properties-section-head", children: [
      /* @__PURE__ */ o.jsx("h3", { children: e }),
      /* @__PURE__ */ o.jsxs("button", { type: "button", className: "wf-properties-add", onClick: s, children: [
        /* @__PURE__ */ o.jsx(Qt, { size: 14 }),
        " ",
        t
      ] })
    ] }),
    r ? /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: n }) : /* @__PURE__ */ o.jsxs("table", { className: "wf-properties-table", children: [
      /* @__PURE__ */ o.jsx("thead", { children: /* @__PURE__ */ o.jsxs("tr", { children: [
        i.map((c) => /* @__PURE__ */ o.jsx("th", { children: c }, c)),
        /* @__PURE__ */ o.jsx("th", { "aria-label": "Actions" })
      ] }) }),
      /* @__PURE__ */ o.jsx("tbody", { children: a })
    ] })
  ] });
}
function gb({ label: e, onRemove: t }) {
  return /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-properties-remove", "aria-label": e, title: e, onClick: t, children: /* @__PURE__ */ o.jsx(Dn, { size: 14 }) }) });
}
function yb({ checked: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ o.jsx("input", { type: "checkbox", "aria-label": t, checked: e, onChange: (i) => n(i.target.checked) });
}
function Es({
  items: e,
  typeOptions: t,
  storageOptions: n,
  editorForAlias: i,
  namePrefix: r,
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
  const { add: y, update: w, remove: x } = lb(e, h, {
    namePrefix: r,
    nameKeys: s,
    create: (g) => l(g, cb(t)),
    patch: d
  }), m = ["Name", "Type", "Collection", ...f.default ? ["Default"] : [], ...f.storage ? ["Storage"] : [], ...f.required ? ["Required"] : []], b = r.toLowerCase();
  return /* @__PURE__ */ o.jsx(
    hb,
    {
      title: a,
      addLabel: c,
      emptyLabel: u,
      headers: m,
      isEmpty: e.length === 0,
      onAdd: y,
      children: e.map((g, v) => {
        const j = ct(g, s), N = Qo(g), S = ct(g, Qc), k = S ? p?.get(S) : void 0, _ = N.collectionKind === "Single" ? i(N.alias) : "text";
        return /* @__PURE__ */ o.jsxs("tr", { children: [
          /* @__PURE__ */ o.jsxs("td", { children: [
            /* @__PURE__ */ o.jsx("input", { type: "text", "aria-label": `${r} name`, value: j, onChange: (M) => w(v, { name: M.target.value }) }),
            k ? /* @__PURE__ */ o.jsx("span", { className: "wf-properties-warning", role: "note", title: k, children: k }) : null
          ] }),
          /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            mc,
            {
              ariaLabel: `${r} type`,
              value: N.alias,
              options: t,
              placeholder: "Type",
              onChange: (M) => w(v, { type: { alias: M, collectionKind: N.collectionKind } })
            }
          ) }),
          /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            db,
            {
              ariaLabel: `${r} collection kind`,
              value: N.collectionKind,
              onChange: (M) => w(v, { type: { alias: N.alias, collectionKind: M } })
            }
          ) }),
          f.default ? /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            pb,
            {
              ariaLabel: `${r} default value`,
              value: Df(g.default),
              editor: _,
              onChange: (M) => w(v, { default: _f(M) })
            }
          ) }) : null,
          f.storage ? /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            mc,
            {
              ariaLabel: `${r} storage driver`,
              value: ct(g, Vf),
              options: n,
              placeholder: "—",
              allowEmpty: !0,
              onChange: (M) => w(v, { storageDriverType: M || null })
            }
          ) }) : null,
          f.required ? /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            yb,
            {
              ariaLabel: `${r} required`,
              checked: g.isRequired === !0,
              onChange: (M) => w(v, { isRequired: M })
            }
          ) }) : null,
          /* @__PURE__ */ o.jsx(gb, { label: `Remove ${b} ${j || v + 1}`, onRemove: () => x(v) })
        ] }, v);
      })
    }
  );
}
function Cd({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, title: r = "Variables", addLabel: s = "Add variable", emptyLabel: a = "No variables defined.", warnings: c, onChange: u }) {
  return /* @__PURE__ */ o.jsx(
    Es,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Variable",
      nameKeys: zf,
      title: r,
      addLabel: s,
      emptyLabel: a,
      create: (l, d) => If({ name: l, alias: d }),
      patch: (l, d) => Af(l, d),
      columns: { default: !0, storage: !0 },
      warnings: c,
      onChange: u
    }
  );
}
function mb({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, onChange: r }) {
  return /* @__PURE__ */ o.jsx(
    Es,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Input",
      nameKeys: es,
      title: "Inputs",
      addLabel: "Add input",
      emptyLabel: "No inputs defined.",
      create: (s, a) => Tf({ name: s, alias: a }),
      patch: (s, a) => $f(s, a),
      columns: { default: !1, storage: !0, required: !0 },
      onChange: r
    }
  );
}
function xb({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, onChange: r }) {
  return /* @__PURE__ */ o.jsx(
    Es,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Output",
      nameKeys: es,
      title: "Outputs",
      addLabel: "Add output",
      emptyLabel: "No outputs defined.",
      create: (s, a) => Pf({ name: s, alias: a }),
      patch: (s, a) => Mf(s, a),
      columns: { default: !1, storage: !1 },
      onChange: r
    }
  );
}
function Mi(e) {
  return (e ?? []).filter(Pn);
}
function wb({ context: e, variables: t, title: n, addLabel: i, emptyLabel: r, warnings: s, onChange: a }) {
  const { typeOptions: c, storageOptions: u, editorForAlias: l } = Sd(e);
  return /* @__PURE__ */ o.jsx(
    Cd,
    {
      items: Mi(t),
      typeOptions: c,
      storageOptions: u,
      editorForAlias: l,
      title: n,
      addLabel: i,
      emptyLabel: r,
      warnings: s,
      onChange: a
    }
  );
}
function vb({ definition: e, definitionId: t, onMetaChange: n }) {
  const i = !!n, [r, s] = K(e?.name ?? ""), [a, c] = K(e?.description ?? "");
  Q(() => {
    s(e?.name ?? "");
  }, [e?.name]), Q(() => {
    c(e?.description ?? "");
  }, [e?.description]);
  const u = () => {
    const d = r.trim();
    d && d !== (e?.name ?? "") ? n?.({ name: d }) : d || s(e?.name ?? "");
  }, l = () => {
    a !== (e?.description ?? "") && n?.({ description: a });
  };
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ o.jsx("h3", { children: "Information" }),
    /* @__PURE__ */ o.jsxs("dl", { className: "wf-properties-info", children: [
      /* @__PURE__ */ o.jsx("dt", { children: /* @__PURE__ */ o.jsx("label", { htmlFor: "wf-def-name", children: "Name" }) }),
      /* @__PURE__ */ o.jsx("dd", { children: i ? /* @__PURE__ */ o.jsx(
        "input",
        {
          id: "wf-def-name",
          type: "text",
          "aria-label": "Workflow name",
          value: r,
          onChange: (d) => s(d.target.value),
          onBlur: u
        }
      ) : e?.name ?? "—" }),
      /* @__PURE__ */ o.jsx("dt", { children: /* @__PURE__ */ o.jsx("label", { htmlFor: "wf-def-description", children: "Description" }) }),
      /* @__PURE__ */ o.jsx("dd", { children: i ? /* @__PURE__ */ o.jsx(
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
      ) : e?.description?.trim() ? e.description : /* @__PURE__ */ o.jsx("span", { className: "wf-muted", children: "No description" }) }),
      /* @__PURE__ */ o.jsx("dt", { children: "Definition ID" }),
      /* @__PURE__ */ o.jsx("dd", { children: /* @__PURE__ */ o.jsx("code", { children: t }) })
    ] })
  ] });
}
function bb({ details: e, draft: t, context: n, onStateChange: i, onDefinitionMetaChange: r }) {
  const { typeOptions: s, storageOptions: a, editorForAlias: c } = Sd(n), u = Mi(t.state.variables), l = Mi(t.state.inputs), d = Mi(t.state.outputs), f = e?.versions ?? [];
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-properties-view", children: [
    /* @__PURE__ */ o.jsx(
      vb,
      {
        definition: e?.definition,
        definitionId: t.definitionId,
        onMetaChange: r
      }
    ),
    /* @__PURE__ */ o.jsx(
      Cd,
      {
        items: u,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, variables: p }))
      }
    ),
    /* @__PURE__ */ o.jsx(
      mb,
      {
        items: l,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, inputs: p }))
      }
    ),
    /* @__PURE__ */ o.jsx(
      xb,
      {
        items: d,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, outputs: p }))
      }
    ),
    /* @__PURE__ */ o.jsxs("section", { className: "wf-properties-section", children: [
      /* @__PURE__ */ o.jsx("h3", { children: "Versions" }),
      f.length === 0 ? /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "No published versions yet." }) : /* @__PURE__ */ o.jsx("ul", { className: "wf-properties-versions", children: f.map((p) => /* @__PURE__ */ o.jsxs("li", { children: [
        /* @__PURE__ */ o.jsxs("span", { className: "wf-properties-version-tag", children: [
          "v",
          p.version
        ] }),
        /* @__PURE__ */ o.jsx("time", { children: Me(p.createdAt) })
      ] }, p.id)) })
    ] })
  ] });
}
const xc = "application/x-elsa-activity-version-id", Nb = 6, jb = 1200, Sb = 250, Cb = [10, 25, 50], Eb = 10, wc = "elsa-studio-workflow-palette-width", vc = "elsa-studio-workflow-inspector-width", bc = "elsa-studio-workflow-palette-collapsed", Nc = "elsa-studio-workflow-inspector-collapsed", Ed = "elsa-studio-workflow-side-panel-maximized", Cn = 180, En = 460, kb = 260, qt = 260, Yt = 560, Ib = 320, jc = 42, Ci = 16, kd = Je.createContext(null), Id = Je.createContext(null);
function Ab(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function Ad(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Pt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function Mt(e, t, n) {
  const i = t.createPrompt(n);
  return i ? (e.dispatchPrompt(i), !0) : !1;
}
function _b(e) {
  if (!e) return null;
  const t = [], n = e.match(/```(?:json)?\s*([\s\S]*?)```/i);
  n && t.push(n[1]);
  const i = e.match(/\{[\s\S]*?\}/);
  i && t.push(i[0]);
  for (const a of t)
    try {
      const c = JSON.parse(a.trim()), u = Sc(c.name, c.description);
      if (u) return u;
    } catch {
    }
  const r = e.match(/name\s*[:-]\s*(.+)/i)?.[1], s = e.match(/description\s*[:-]\s*(.+)/i)?.[1];
  return Sc(r, s);
}
function Sc(e, t) {
  const n = typeof e == "string" ? Cc(e) : void 0, i = typeof t == "string" ? Cc(t) : void 0;
  return n || i ? { name: n || void 0, description: i || void 0 } : null;
}
function Cc(e) {
  return e.trim().replace(/,$/, "").trim().replace(/^["']/, "").replace(/["']$/, "").trim();
}
function Db(e, t) {
  return e.rootActivityVersionId ?? _d(t, e.rootKind)?.activityVersionId ?? null;
}
function _d(e, t) {
  return e.find((n) => Tb(n) === t);
}
function Tb(e) {
  return e ? $b(e) ? "flowchart" : Pb(e) ? "sequence" : null : null;
}
function Vo(e) {
  return bl(e, (t) => t.category).map((t) => ({
    category: t.category,
    activities: t.items.sort((n, i) => Ee(n).localeCompare(Ee(i)))
  }));
}
function $b(e) {
  return Ee(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function Pb(e) {
  return Ee(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function Mb(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function Dd(e) {
  return Ob(e.rootActivityType) || e.rootActivityType;
}
function Rb(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function Lb(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function zb(e, t) {
  return Ec(t) - Ec(e);
}
function Ec(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function Td(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
function $d(e) {
  const t = e?.workflowExecutionId ?? e?.runId ?? e?.executionId;
  return typeof t == "string" && t.trim() ? t : null;
}
async function Vb(e) {
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
function Ob(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function Hb(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    Ei(t, n.typeName, n), Ei(t, n.name, n), Ei(t, n.displayName, n);
    const i = n.typeName.split(".").filter(Boolean).at(-1);
    Ei(t, i, n);
  }
  return t;
}
function Wb(e, t, n) {
  const i = t.get(e.activityVersionId);
  return n.get(kn(i?.activityTypeKey)) ?? n.get(kn(It(i?.activityTypeKey))) ?? n.get(kn(i?.displayName)) ?? n.get(kn(e.activityVersionId)) ?? null;
}
function Ei(e, t, n) {
  const i = kn(t);
  i && !e.has(i) && e.set(i, n);
}
function kn(e) {
  return e?.trim().toLowerCase() ?? "";
}
function kc(e, t, n, i) {
  const r = yr();
  if (!r) return t;
  const s = r.getItem(e);
  if (s == null) return t;
  const a = Number(s);
  return Number.isFinite(a) ? Ri(a, n, i) : t;
}
function Ic(e, t) {
  const n = yr();
  if (!n) return t;
  const i = n.getItem(e);
  return i === "true" ? !0 : i === "false" ? !1 : t;
}
function Bb() {
  const e = yr();
  if (!e) return null;
  const t = e.getItem(Ed);
  return t === "palette" || t === "inspector" ? t : null;
}
function yr() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function vn(e, t) {
  const n = yr();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function Ri(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function Pd(e) {
  switch (Fb(e)) {
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
function Fb(e) {
  return (e ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}
function Kb(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function Ac(e) {
  return `${Ee(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function _c(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function Xb(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function Md(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function qb(e) {
  const t = Md(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function Yb(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function Be(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function Ub(e) {
  return zd(Be(e));
}
function Zb(e, t) {
  const n = t?.get(e.activityVersionId);
  return n ? Ee(n) : It(e.activityVersionId) ?? e.activityVersionId;
}
function Gb(e, t) {
  if (e.cardinality === "single") {
    const i = e.activities[0];
    return i ? Zb(i, t) : "Empty — click to choose";
  }
  const n = e.activities.length;
  return `${n} activit${n === 1 ? "y" : "ies"}`;
}
function Rd(e, t, n = []) {
  if (!e) return n;
  const i = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: i?.activityTypeKey ?? e.activityVersionId,
    displayName: i ? Ee(i) : void 0
  });
  for (const r of Re(e, t))
    for (const s of r.activities) Rd(s, t, n);
  return n;
}
function Ld(e, t, n = []) {
  if (!e) return n;
  for (const i of pl(e))
    n.push({ source: i.source, target: i.target, sourcePort: i.sourceHandle ?? void 0, targetPort: i.targetHandle ?? void 0 });
  for (const i of Re(e, t))
    for (const r of i.activities) Ld(r, t, n);
  return n;
}
function In(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function Jb(e) {
  return `${e.id}-${zd(JSON.stringify(e.state))}`;
}
function zd(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function ks(e) {
  return e.status.toLowerCase() === "rejected";
}
function Qb(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function eN(e, t) {
  const n = e instanceof Error ? e.message : String(e);
  return tN(e, n) ? `Run ${t} was not found.` : n;
}
function tN(e, t) {
  if ((typeof e == "object" && e ? e.response?.status ?? e.status : void 0) === 404 || /\b404\b/.test(t)) return !0;
  try {
    const i = JSON.parse(t);
    return [i.error, i.title, i.detail].some((r) => typeof r == "string" && /not found/i.test(r));
  } catch {
    return /not found/i.test(t);
  }
}
function pn({ status: e, subStatus: t }) {
  return /* @__PURE__ */ o.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
const Vd = { workflowActivity: nN }, Od = { workflow: rN };
function nN({ data: e, selected: t }) {
  const n = e, i = n.runtime, r = !n.suppressFlowPorts, s = r ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], a = iN(n), u = Je.useContext(Id)?.({ activityVersionId: n.activityVersionId, activityTypeKey: n.activityTypeKey }) ?? null;
  return /* @__PURE__ */ o.jsxs(
    "div",
    {
      className: ["wf-node", t ? "selected" : "", i ? "wf-node-runtime" : "", i?.hasBlockingIncident ? "faulted" : "", u ? "wf-node-unavailable" : ""].filter(Boolean).join(" "),
      "data-icon": n.icon ?? "activity",
      children: [
        r && n.acceptsInbound ? /* @__PURE__ */ o.jsx(cn, { type: "target", position: oe.Left }) : null,
        u ? /* @__PURE__ */ o.jsx("span", { className: "wf-node-availability", title: `No longer available for new use · ${Rn(u.state)}`, children: /* @__PURE__ */ o.jsx(zi, { size: 13 }) }) : null,
        /* @__PURE__ */ o.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ o.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: rr(n.icon) }),
          /* @__PURE__ */ o.jsxs("span", { className: "wf-node-copy", children: [
            /* @__PURE__ */ o.jsx("strong", { children: n.label }),
            a ? /* @__PURE__ */ o.jsx("small", { children: a }) : null
          ] })
        ] }),
        n.childSlots.length > 0 ? n.onEnterSlot ? /* @__PURE__ */ o.jsx("span", { className: "wf-node-slot-list", children: n.childSlots.map((l) => /* @__PURE__ */ o.jsx(
          "button",
          {
            type: "button",
            className: "wf-node-slot-badge",
            onClick: (d) => {
              d.stopPropagation(), n.onEnterSlot?.(l);
            },
            children: l.label
          },
          l.id
        )) }) : /* @__PURE__ */ o.jsxs("span", { className: "wf-node-slot-badge", children: [
          n.childSlots.length,
          " slot",
          n.childSlots.length === 1 ? "" : "s"
        ] }) : null,
        i ? /* @__PURE__ */ o.jsxs("div", { className: "wf-node-runtime-strip", children: [
          i.status ? /* @__PURE__ */ o.jsx(pn, { status: i.status, subStatus: i.subStatus }) : null,
          i.incidentCount > 0 ? /* @__PURE__ */ o.jsxs("span", { className: "wf-node-runtime-count", children: [
            i.incidentCount,
            " incident",
            i.incidentCount === 1 ? "" : "s"
          ] }) : null,
          i.faultCount > 0 ? /* @__PURE__ */ o.jsxs("span", { className: "wf-node-runtime-count", children: [
            i.faultCount,
            " faults"
          ] }) : null
        ] }) : null,
        s.map((l, d) => {
          const f = `${(d + 1) / (s.length + 1) * 100}%`;
          return /* @__PURE__ */ o.jsxs(Je.Fragment, { children: [
            /* @__PURE__ */ o.jsx("span", { className: "wf-node-port-label", style: { top: f }, children: l.displayName }),
            /* @__PURE__ */ o.jsx(cn, { type: "source", position: oe.Right, id: l.name, style: { top: f } })
          ] }, l.name);
        })
      ]
    }
  );
}
function iN(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((r) => !!r).join(" · ");
}
function rN(e) {
  const {
    id: t,
    sourceX: n,
    sourceY: i,
    targetX: r,
    targetY: s,
    sourcePosition: a,
    targetPosition: c,
    markerEnd: u,
    style: l,
    label: d,
    labelStyle: f
  } = e, p = Je.useContext(kd), [h, y] = K(!1), [w, x, m] = Gi({ sourceX: n, sourceY: i, targetX: r, targetY: s, sourcePosition: a, targetPosition: c }), b = p?.highlightedEdgeId === t;
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsx(
      ii,
      {
        id: t,
        path: w,
        markerEnd: u,
        style: {
          ...l,
          strokeWidth: b ? 2.5 : l?.strokeWidth
        },
        label: d,
        labelX: x,
        labelY: m,
        labelStyle: f,
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1)
      }
    ),
    p ? /* @__PURE__ */ o.jsx(E0, { children: /* @__PURE__ */ o.jsxs(
      "div",
      {
        className: ["wf-edge-actions", h ? "visible" : "", b ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${x}px, ${m}px)` },
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1),
        children: [
          /* @__PURE__ */ o.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (g) => p.requestInsertActivity(t, g.clientX, g.clientY), children: /* @__PURE__ */ o.jsx(Qt, { size: 12 }) }),
          /* @__PURE__ */ o.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => p.deleteEdge(t), children: /* @__PURE__ */ o.jsx(Dn, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function oN({ clientX: e, clientY: t, activities: n, onPick: i, onClose: r }) {
  const [s, a] = K(""), [c, u] = K(0), l = re(null), d = re(null), f = ue(() => {
    const b = s.trim().toLowerCase(), g = n.filter(Mb);
    return b ? g.filter((v) => Ee(v).toLowerCase().includes(b) || v.activityTypeKey.toLowerCase().includes(b) || (v.category ?? "").toLowerCase().includes(b) || (v.description ?? "").toLowerCase().includes(b)) : g;
  }, [n, s]), p = ue(() => Vo(f), [f]), h = ue(() => p.flatMap((b) => b.activities), [p]);
  Q(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), Q(() => {
    const b = (v) => {
      l.current?.contains(v.target) || r();
    }, g = (v) => {
      v.key === "Escape" && r();
    };
    return document.addEventListener("mousedown", b, !0), document.addEventListener("keydown", g), () => {
      document.removeEventListener("mousedown", b, !0), document.removeEventListener("keydown", g);
    };
  }, [r]);
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
  }, w = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), x = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let m = -1;
  return /* @__PURE__ */ o.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: w, top: x }, onMouseDown: (b) => b.stopPropagation(), onClick: (b) => b.stopPropagation(), children: [
    /* @__PURE__ */ o.jsx(
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
    /* @__PURE__ */ o.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: p.length === 0 ? /* @__PURE__ */ o.jsx("p", { children: "No matching activities." }) : p.map((b) => /* @__PURE__ */ o.jsxs("section", { children: [
      /* @__PURE__ */ o.jsx("h4", { children: b.category }),
      b.activities.map((g) => {
        m += 1;
        const v = m, j = v === c;
        return /* @__PURE__ */ o.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": j,
            className: j ? "active" : "",
            onMouseEnter: () => u(v),
            onClick: () => i(g),
            children: [
              /* @__PURE__ */ o.jsx("strong", { children: Ee(g) }),
              /* @__PURE__ */ o.jsx("small", { children: g.category || g.activityTypeKey })
            ]
          },
          g.activityVersionId
        );
      })
    ] }, b.category)) })
  ] });
}
function Li({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: i
}) {
  const r = wf(t.map((s) => s.id), n, i);
  return /* @__PURE__ */ o.jsx("div", { className: "wf-panel-tab-list", role: "tablist", "aria-label": e, onKeyDown: r, children: t.map((s) => {
    const a = s.id === n;
    return /* @__PURE__ */ o.jsxs(
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
          s.icon ? /* @__PURE__ */ o.jsx("span", { className: "wf-panel-tab-icon", "aria-hidden": "true", children: s.icon }) : null,
          /* @__PURE__ */ o.jsx("span", { children: s.title })
        ]
      },
      s.id
    );
  }) });
}
function Dc(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
const sN = "Expressions/UnresolvedVariable";
function aN(e) {
  return String(e.type ?? e.code ?? "");
}
function cN(e) {
  return aN(e) === sN;
}
function lN(e) {
  const t = String(e ?? "").trim().split("/").filter(Boolean), [n, i, ...r] = t, s = n === "$workflow";
  return {
    nodeId: !n || s ? null : n,
    isWorkflowScope: s,
    bag: i === "inputs" || i === "outputs" || i === "variables" ? i : null,
    referenceKey: r.length > 0 ? r.join("/") : null
  };
}
function uN(e) {
  return (e ?? []).filter(cN).map((t) => ({
    error: t,
    path: lN(t.path),
    message: t.message ?? "Variable reference is not visible from this activity's scope."
  }));
}
function dN({ draft: e, onRepair: t }) {
  const n = e.validationErrors;
  if (!n.length)
    return /* @__PURE__ */ o.jsxs("div", { className: "wf-validation ok", children: [
      /* @__PURE__ */ o.jsx(ln, { size: 14 }),
      " No validation errors"
    ] });
  const i = uN(n), r = new Map(i.map((s) => [s.error, s]));
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-validation-summary", children: [
      /* @__PURE__ */ o.jsx(vt, { size: 14 }),
      n.length,
      " validation issue",
      n.length === 1 ? "" : "s",
      i.length > 0 ? /* @__PURE__ */ o.jsxs("span", { className: "wf-validation-variable-count", children: [
        " · ",
        i.length,
        " invalid variable reference",
        i.length === 1 ? "" : "s"
      ] }) : null
    ] }),
    /* @__PURE__ */ o.jsx("ul", { className: "wf-validation-list", children: n.map((s, a) => {
      const c = r.get(s);
      return /* @__PURE__ */ o.jsxs("li", { className: c ? "wf-validation-item repairable" : "wf-validation-item", children: [
        /* @__PURE__ */ o.jsx("span", { className: "wf-validation-message", children: s.message ?? "Validation issue." }),
        c?.path.nodeId ? /* @__PURE__ */ o.jsxs("button", { type: "button", className: "wf-validation-repair", onClick: () => t(c.path.nodeId), children: [
          /* @__PURE__ */ o.jsx(lf, { size: 12 }),
          " Repair"
        ] }) : null
      ] }, a);
    }) })
  ] });
}
function fN({
  testRun: e,
  onOpenDetails: t
}) {
  const n = ks(e);
  return /* @__PURE__ */ o.jsx("div", { className: "wf-test-run-status", "data-state": n ? "rejected" : "accepted", children: /* @__PURE__ */ o.jsxs(
    "button",
    {
      type: "button",
      className: "wf-test-run-trigger",
      onClick: t,
      children: [
        n ? /* @__PURE__ */ o.jsx(vt, { size: 16 }) : /* @__PURE__ */ o.jsx(ln, { size: 16 }),
        n ? "Test run rejected" : "Test run dispatched"
      ]
    }
  ) });
}
function pN({ testRun: e, onOpenRun: t }) {
  if (!e)
    return /* @__PURE__ */ o.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Run the draft to see Runtime Evidence." }) });
  const n = ks(e), i = e.workflowExecutionId;
  return /* @__PURE__ */ o.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ o.jsxs("section", { className: "wf-runtime-card", "data-state": n ? "rejected" : "accepted", children: [
    /* @__PURE__ */ o.jsxs("header", { children: [
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("span", { children: "Latest Test Run" }),
        /* @__PURE__ */ o.jsx("h3", { children: n ? "Rejected by the server" : "Transient run accepted" })
      ] }),
      /* @__PURE__ */ o.jsx(pn, { status: e.status, subStatus: e.commandDispatchStatus ?? void 0 })
    ] }),
    /* @__PURE__ */ o.jsx("p", { children: "Ephemeral - not saved, promoted, or published." }),
    n && e.reason ? /* @__PURE__ */ o.jsxs("div", { className: "wf-runtime-reason", children: [
      /* @__PURE__ */ o.jsx(vt, { size: 14 }),
      " ",
      e.reason
    ] }) : null,
    /* @__PURE__ */ o.jsxs("dl", { className: "wf-runtime-meta", children: [
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Dispatch" }),
        /* @__PURE__ */ o.jsx("dd", { title: e.commandDispatchStatus ?? e.status, children: e.commandDispatchStatus ?? e.status })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Test Run" }),
        /* @__PURE__ */ o.jsx("dd", { title: e.testRunId, children: e.testRunId })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Artifact" }),
        /* @__PURE__ */ o.jsx("dd", { title: e.artifactId ?? "None", children: e.artifactId ?? "None" })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Run / Instance" }),
        /* @__PURE__ */ o.jsx("dd", { title: i ?? "None", children: i ? /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => t(i), children: i }) : "None" })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Activities" }),
        /* @__PURE__ */ o.jsx("dd", { children: Tc(e.activityCount, "activity") })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ o.jsx("dd", { children: Tc(e.incidentCount, "incident") })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ o.jsx("dd", { title: e.expiresAt ? Me(e.expiresAt) : "None", children: e.expiresAt ? Me(e.expiresAt) : "None" })
      ] })
    ] })
  ] }) });
}
function Tc(e, t) {
  return typeof e != "number" ? "Available on linked Run" : `${e} ${t}${e === 1 ? "" : "s"}`;
}
function Is({ rows: e = 5 }) {
  return /* @__PURE__ */ o.jsx("div", { className: "wf-grid", "aria-busy": "true", "aria-label": "Loading", children: Array.from({ length: e }).map((t, n) => /* @__PURE__ */ o.jsx("div", { className: "wf-skeleton wf-skeleton-row", style: { width: `${90 - n % 3 * 12}%` } }, n)) });
}
function As({ icon: e, title: t, description: n, action: i }) {
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-empty-state", role: "status", children: [
    /* @__PURE__ */ o.jsx("div", { className: "wf-empty-state-icon", "aria-hidden": !0, children: e ?? /* @__PURE__ */ o.jsx(Yn, { size: 22 }) }),
    /* @__PURE__ */ o.jsx("h3", { children: t }),
    n ? /* @__PURE__ */ o.jsx("p", { children: n }) : null,
    i ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty-state-action", children: i }) : null
  ] });
}
function ri({ message: e, title: t = "Something went wrong" }) {
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-error-card", role: "alert", children: [
    /* @__PURE__ */ o.jsx(vt, { size: 18 }),
    /* @__PURE__ */ o.jsxs("div", { className: "wf-error-card-body", children: [
      /* @__PURE__ */ o.jsx("strong", { children: t }),
      /* @__PURE__ */ o.jsx("span", { children: e || "Please try again, or check that the Elsa server is reachable." })
    ] })
  ] });
}
function Hd({ status: e, run: t, compact: n = !1 }) {
  const i = () => {
    t?.workflowExecutionId && (window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(t.workflowExecutionId)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  };
  return /* @__PURE__ */ o.jsxs("div", { className: `wf-status-line${n ? " compact" : ""}`, children: [
    /* @__PURE__ */ o.jsx(ln, { size: n ? 13 : 14 }),
    /* @__PURE__ */ o.jsx("span", { children: e }),
    t?.workflowExecutionId ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: i, children: [
      "Open Run ",
      t.workflowExecutionId
    ] }) : null
  ] });
}
function Et({ value: e, ariaLabel: t, copiedLabel: n, onCopied: i, onCopyFailed: r }) {
  if (!e) return null;
  const s = async (a) => {
    a.preventDefault(), a.stopPropagation();
    try {
      await Vb(e), i(n);
    } catch {
      r(n);
    }
  };
  return /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (a) => {
    s(a);
  }, children: /* @__PURE__ */ o.jsx(uf, { size: 12 }) });
}
function hN({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: i }) {
  const [r, s] = K("loading"), [a, c] = K(""), [u, l] = K(""), [d, f] = K(null), [p, h] = K([]), y = n?.trim().toLowerCase() ?? "", w = ue(
    () => y ? p.filter((S) => Rb(S, y)) : p,
    [y, p]
  ), x = ue(
    () => Array.from(new Set(p.flatMap((S) => [
      S.definitionId,
      S.definitionVersionId,
      S.sourceId
    ]).filter((S) => !!S))).sort((S, k) => S.localeCompare(k)),
    [p]
  ), m = Pt(t, "weaver.workflows.explain-executable"), b = ae(async () => {
    s("loading"), c("");
    try {
      h(await wl(e)), s("ready");
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
      const k = await xl(e, S.artifactId), _ = $d(k);
      f({ artifactId: S.artifactId, workflowExecutionId: _ }), l(`Started ${S.artifactId}`);
    } catch (k) {
      c(k instanceof Error ? k.message : String(k));
    }
  }, v = (S) => {
    m && Mt(t, m, S) && (c(""), f(null), l(`Sent ${S.artifactId} to Weaver`));
  }, j = (S) => {
    c(""), f(null), l(`Copied ${S}`);
  }, N = (S) => {
    l(""), f(null), c(`Could not copy ${S}.`);
  };
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => {
        b();
      }, children: "Refresh" }),
      /* @__PURE__ */ o.jsxs("label", { className: "wf-search wf-executable-definition-filter", children: [
        /* @__PURE__ */ o.jsx(Qi, { size: 14 }),
        /* @__PURE__ */ o.jsx(
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
      /* @__PURE__ */ o.jsx("datalist", { id: "wf-executable-definition-options", children: x.map((S) => /* @__PURE__ */ o.jsx("option", { value: S }, S)) }),
      n ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => i(null), children: [
        /* @__PURE__ */ o.jsx(Uo, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    r === "failed" ? /* @__PURE__ */ o.jsx(ri, { message: a }) : null,
    u ? /* @__PURE__ */ o.jsx(Hd, { status: u, run: d }) : null,
    r === "loading" ? /* @__PURE__ */ o.jsx(Is, {}) : null,
    r === "ready" && w.length === 0 ? /* @__PURE__ */ o.jsx(
      As,
      {
        icon: /* @__PURE__ */ o.jsx(Jt, { size: 22 }),
        title: "No workflow executables",
        description: n ? "No executables match this definition filter." : "Publish a workflow definition to make it executable."
      }
    ) : null,
    r === "ready" && w.length > 0 ? /* @__PURE__ */ o.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ o.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ o.jsx("span", { children: "Version" }),
        /* @__PURE__ */ o.jsx("span", { children: "Source" }),
        /* @__PURE__ */ o.jsx("span", { children: "Root" }),
        /* @__PURE__ */ o.jsx("span", { children: "Published" }),
        /* @__PURE__ */ o.jsx("span", { children: "Actions" })
      ] }),
      w.map((S) => /* @__PURE__ */ o.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ o.jsxs("span", { className: "wf-artifact-cell", children: [
          /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line", children: [
            /* @__PURE__ */ o.jsx("strong", { title: S.artifactId, children: S.artifactId }),
            /* @__PURE__ */ o.jsx(Et, { value: S.artifactId, ariaLabel: `Copy artifact ID ${S.artifactId}`, copiedLabel: "artifact ID", onCopied: j, onCopyFailed: N })
          ] }),
          /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
            /* @__PURE__ */ o.jsx("small", { title: S.artifactHash, children: S.artifactHash }),
            /* @__PURE__ */ o.jsx(Et, { value: S.artifactHash, ariaLabel: `Copy artifact hash ${S.artifactHash}`, copiedLabel: "artifact hash", onCopied: j, onCopyFailed: N })
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line wf-version-cell", children: [
          /* @__PURE__ */ o.jsx("span", { children: S.artifactVersion }),
          /* @__PURE__ */ o.jsx(Et, { value: S.artifactVersion, ariaLabel: `Copy artifact version ${S.artifactVersion}`, copiedLabel: "artifact version", onCopied: j, onCopyFailed: N })
        ] }),
        /* @__PURE__ */ o.jsx(gN, { executable: S, onCopied: j, onCopyFailed: N }),
        /* @__PURE__ */ o.jsx("span", { children: Dd(S) }),
        /* @__PURE__ */ o.jsx("span", { children: Me(S.publishedAt ?? S.createdAt) }),
        /* @__PURE__ */ o.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
            g(S);
          }, children: [
            /* @__PURE__ */ o.jsx(Jt, { size: 13 }),
            " Run"
          ] }),
          m ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => v(S), children: [
            /* @__PURE__ */ o.jsx(ut, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, S.artifactId))
    ] }) : null
  ] });
}
function gN({ executable: e, onCopied: t, onCopyFailed: n }) {
  const i = e.sourceId || e.definitionVersionId || e.definitionId, r = e.sourceVersion;
  return /* @__PURE__ */ o.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ o.jsx("span", { className: "wf-source-kind", children: Td(e.sourceKind) }),
    i ? /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line", children: [
      /* @__PURE__ */ o.jsx("code", { title: i, children: i }),
      /* @__PURE__ */ o.jsx(Et, { value: i, ariaLabel: `Copy source ID ${i}`, copiedLabel: "source ID", onCopied: t, onCopyFailed: n })
    ] }) : null,
    r ? /* @__PURE__ */ o.jsxs("small", { children: [
      "Version ",
      r
    ] }) : null
  ] });
}
function yN({ context: e, ai: t, definitionId: n, publishedArtifactId: i }) {
  const [r, s] = K("loading"), [a, c] = K(""), [u, l] = K(""), [d, f] = K(null), [p, h] = K([]), y = Pt(t, "weaver.workflows.explain-executable"), w = ae(async () => {
    s("loading"), c("");
    try {
      const j = await wl(e);
      h(j.filter((N) => Lb(N, n)).sort(zb)), s("ready");
    } catch (j) {
      c(j instanceof Error ? j.message : String(j)), h([]), s("failed");
    }
  }, [e, n]);
  Q(() => {
    w();
  }, [w, i]);
  const x = async (j) => {
    l(""), f(null), c("");
    try {
      const N = await xl(e, j.artifactId);
      f({ artifactId: j.artifactId, workflowExecutionId: $d(N) }), l(`Started ${j.artifactId}`);
    } catch (N) {
      c(N instanceof Error ? N.message : String(N));
    }
  }, m = (j) => {
    y && Mt(t, y, j) && (c(""), f(null), l(`Sent ${j.artifactId} to Weaver`));
  }, b = () => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(n)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, g = (j) => {
    c(""), f(null), l(`Copied ${j}`);
  }, v = (j) => {
    l(""), f(null), c(`Could not copy ${j}.`);
  };
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-artifacts-panel", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-artifacts-toolbar", children: [
      /* @__PURE__ */ o.jsxs("span", { children: [
        p.length,
        " artifact",
        p.length === 1 ? "" : "s"
      ] }),
      /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
        w();
      }, children: [
        /* @__PURE__ */ o.jsx(Zo, { size: 13 }),
        " Refresh"
      ] }),
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: b, children: "Open list" })
    ] }),
    r === "failed" ? /* @__PURE__ */ o.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ o.jsx(vt, { size: 14 }),
      " ",
      a
    ] }) : null,
    u ? /* @__PURE__ */ o.jsx(Hd, { status: u, run: d, compact: !0 }) : null,
    r === "loading" ? /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "Loading artifacts..." }) : null,
    r === "ready" && p.length === 0 ? /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "No published artifacts for this workflow yet." }) : null,
    r === "ready" && p.length > 0 ? /* @__PURE__ */ o.jsx("div", { className: "wf-artifact-list", role: "list", "aria-label": "Workflow artifacts", children: p.map((j) => /* @__PURE__ */ o.jsxs("article", { className: "wf-artifact-card", role: "listitem", "data-active": j.artifactId === i ? "true" : void 0, children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-artifact-card-heading", children: [
        /* @__PURE__ */ o.jsxs("div", { children: [
          /* @__PURE__ */ o.jsxs("span", { className: "wf-artifact-version", children: [
            "Version ",
            j.artifactVersion
          ] }),
          j.artifactId === i ? /* @__PURE__ */ o.jsx("span", { className: "wf-chip", children: "Latest publish" }) : null
        ] }),
        /* @__PURE__ */ o.jsx("span", { children: Me(j.publishedAt ?? j.createdAt) })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { className: "wf-artifact-card-values", children: [
        /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line", children: [
          /* @__PURE__ */ o.jsx("code", { title: j.artifactId, children: j.artifactId }),
          /* @__PURE__ */ o.jsx(Et, { value: j.artifactId, ariaLabel: `Copy artifact ID ${j.artifactId}`, copiedLabel: "artifact ID", onCopied: g, onCopyFailed: v })
        ] }),
        /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ o.jsx("code", { title: j.artifactHash, children: j.artifactHash }),
          /* @__PURE__ */ o.jsx(Et, { value: j.artifactHash, ariaLabel: `Copy artifact hash ${j.artifactHash}`, copiedLabel: "artifact hash", onCopied: g, onCopyFailed: v })
        ] })
      ] }),
      /* @__PURE__ */ o.jsxs("dl", { children: [
        /* @__PURE__ */ o.jsxs("div", { children: [
          /* @__PURE__ */ o.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ o.jsxs("dd", { children: [
            Td(j.sourceKind),
            " ",
            j.sourceVersion ? `v${j.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("div", { children: [
          /* @__PURE__ */ o.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ o.jsx("dd", { children: Dd(j) })
        ] })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
          x(j);
        }, children: [
          /* @__PURE__ */ o.jsx(Jt, { size: 13 }),
          " Run"
        ] }),
        y ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => m(j), children: [
          /* @__PURE__ */ o.jsx(ut, { size: 13 }),
          " Explain"
        ] }) : null
      ] })
    ] }, j.artifactId)) }) : null
  ] });
}
function Wd() {
  const [e, t] = K(() => kc(wc, kb, Cn, En)), [n, i] = K(() => kc(vc, Ib, qt, Yt)), [r, s] = K(() => Ic(bc, !1)), [a, c] = K(() => Ic(Nc, !1)), [u, l] = K(Bb);
  Q(() => {
    vn(wc, String(e));
  }, [e]), Q(() => {
    vn(vc, String(n));
  }, [n]), Q(() => {
    vn(bc, String(r));
  }, [r]), Q(() => {
    vn(Nc, String(a));
  }, [a]), Q(() => {
    vn(Ed, u);
  }, [u]), Q(() => {
    if (!u) return;
    const g = (v) => {
      v.key === "Escape" && l(null);
    };
    return window.addEventListener("keydown", g), () => window.removeEventListener("keydown", g);
  }, [u]);
  const d = ae((g) => {
    l((v) => v === g ? null : v), g === "palette" ? s((v) => !v) : c((v) => !v);
  }, []), f = ae((g) => {
    g === "palette" ? s(!1) : c(!1), l((v) => v === g ? null : g);
  }, []), p = ae((g, v) => {
    l(null), g === "palette" ? (s(!1), t((j) => Ri(j + v, Cn, En))) : (c(!1), i((j) => Ri(j + v, qt, Yt)));
  }, []), h = ae((g, v) => {
    v.preventDefault(), l(null), g === "palette" ? s(!1) : c(!1);
    const j = v.clientX, N = g === "palette" ? e : n, S = g === "palette" ? Cn : qt, k = g === "palette" ? En : Yt;
    document.body.classList.add("wf-side-panel-resizing");
    const _ = (I) => {
      const $ = g === "palette" ? I.clientX - j : j - I.clientX, z = Ri(N + $, S, k);
      g === "palette" ? t(z) : i(z);
    }, M = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", _), window.removeEventListener("pointerup", M), window.removeEventListener("pointercancel", M);
    };
    window.addEventListener("pointermove", _), window.addEventListener("pointerup", M), window.addEventListener("pointercancel", M);
  }, [n, e]), y = ae((g, v) => {
    v.key === "ArrowLeft" ? (v.preventDefault(), p(g, g === "palette" ? -Ci : Ci)) : v.key === "ArrowRight" ? (v.preventDefault(), p(g, g === "palette" ? Ci : -Ci)) : v.key === "Home" ? (v.preventDefault(), g === "palette" ? t(Cn) : i(qt)) : v.key === "End" && (v.preventDefault(), g === "palette" ? t(En) : i(Yt));
  }, [p]), w = !r && u !== "inspector", x = !a && u !== "palette", m = [
    "wf-editor-body",
    r ? "palette-collapsed" : "",
    a ? "inspector-collapsed" : "",
    u === "palette" ? "palette-maximized" : "",
    u === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), b = {
    "--wf-palette-width": `${r ? jc : e}px`,
    "--wf-inspector-width": `${a ? jc : n}px`
  };
  return {
    paletteWidth: e,
    inspectorWidth: n,
    paletteCollapsed: r,
    inspectorCollapsed: a,
    maximizedSidePanel: u,
    setInspectorCollapsed: c,
    paletteExpanded: w,
    inspectorExpanded: x,
    editorBodyClassName: m,
    editorBodyStyle: b,
    toggleSidePanelCollapsed: d,
    toggleSidePanelMaximized: f,
    startSidePanelResize: h,
    handleSidePanelResizeKeyDown: y
  };
}
const mN = 50;
function $c() {
  return { past: [], future: [] };
}
function xN(e) {
  return e.past.length > 0;
}
function wN(e) {
  return e.future.length > 0;
}
function Pc(e, t, n = mN) {
  const i = [...e.past, t];
  return i.length > n && i.splice(0, i.length - n), { past: i, future: [] };
}
function vN(e, t) {
  if (e.past.length === 0) return null;
  const n = e.past.slice(), i = n.pop();
  return { history: { past: n, future: [...e.future, t] }, snapshot: i };
}
function bN(e, t) {
  if (e.future.length === 0) return null;
  const n = e.future.slice(), i = n.pop();
  return { history: { past: [...e.past, t], future: n }, snapshot: i };
}
function NN({ draft: e, restoreDraft: t }) {
  const n = re($c()), i = re(null), r = re(""), s = re(!1), [a, c] = K(0), u = ae((w) => {
    n.current = $c(), i.current = w ? In(w) : null, r.current = w ? Be(w) : "", s.current = !1, c(0);
  }, []);
  Q(() => {
    if (!e) return;
    if (s.current) {
      s.current = !1;
      return;
    }
    const w = Be(e);
    if (w === r.current) return;
    const x = window.setTimeout(() => {
      const m = i.current;
      m && (n.current = Pc(n.current, m), c((b) => b + 1)), i.current = In(e), r.current = w;
    }, Sb);
    return () => window.clearTimeout(x);
  }, [e]);
  const l = ae(() => {
    if (!e) return;
    const w = Be(e);
    if (w === r.current) return;
    const x = i.current;
    x && (n.current = Pc(n.current, x)), i.current = In(e), r.current = w;
  }, [e]), d = ae((w) => {
    s.current = !0, i.current = In(w), r.current = Be(w), t(w), c((x) => x + 1);
  }, [t]), f = ae(() => {
    if (!e) return;
    l();
    const w = vN(n.current, e);
    w && (n.current = w.history, d(w.snapshot));
  }, [e, l, d]), p = ae(() => {
    if (!e) return;
    l();
    const w = bN(n.current, e);
    w && (n.current = w.history, d(w.snapshot));
  }, [e, l, d]), { canUndoNow: h, canRedoNow: y } = ue(() => {
    const w = !!e && !!i.current && Be(e) !== r.current;
    return {
      canUndoNow: xN(n.current) || w,
      canRedoNow: wN(n.current) && !w
    };
  }, [e, a]);
  return { resetHistory: u, undo: f, redo: p, canUndoNow: h, canRedoNow: y };
}
const jN = {
  draft: null,
  frames: [],
  selectedNodeId: null,
  testRun: null,
  publishedArtifactId: null
};
function SN(e, t) {
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
      return { ...e, frames: [...e.frames, t.frame], selectedNodeId: null };
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
function CN() {
  const [e, t] = of(SN, jN), n = ue(() => ({
    // Load a freshly fetched / applied draft (or clear it). Used by load(), JSON apply, and undo/redo.
    loadDraft(i) {
      t({ type: "draftLoaded", draft: i });
    },
    // Replace the draft from a Weaver batch apply/undo, invalidating scope, selection, run and artifact.
    replaceDraftByBatch(i, r) {
      t({ type: "draftReplacedByBatch", draft: i, selectedNodeId: r });
    },
    // In-place draft edit that keeps scope + selection.
    editDraft(i) {
      t({ type: "draftEdited", recipe: i });
    },
    // Draft edit that also selects a node (add/wrap root, add scoped activity).
    editDraftAndSelect(i, r) {
      t({ type: "draftEditedAndSelected", recipe: i, selectedNodeId: r });
    },
    select(i) {
      t({ type: "selectionChanged", selectedNodeId: i });
    },
    // Jump to an explicit breadcrumb path (breadcrumb clicks, variable-repair navigation).
    navigateToScope(i, r = null) {
      t({ type: "scopeNavigated", frames: i, selectedNodeId: r });
    },
    resetToRoot() {
      t({ type: "scopeNavigated", frames: [], selectedNodeId: null });
    },
    enterSlot(i, r, s) {
      t({ type: "slotEntered", frame: { ownerNodeId: i.nodeId, slotId: r, label: s } });
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
const EN = 320, kN = 140;
function IN(e, t, n) {
  return n === "sequence" ? AN(e) : _N(e, t);
}
function AN(e) {
  const t = /* @__PURE__ */ new Map();
  return e.forEach((n, i) => {
    t.set(n.id, { x: i * 280, y: 0 });
  }), t;
}
function _N(e, t) {
  const n = /* @__PURE__ */ new Map();
  if (e.length === 0) return n;
  const i = new Set(e.map((d) => d.id)), r = t.filter((d) => i.has(d.source) && i.has(d.target)), s = /* @__PURE__ */ new Set();
  for (const d of r)
    s.add(d.source), s.add(d.target);
  const a = /* @__PURE__ */ new Map();
  for (const d of e) a.set(d.id, 0);
  for (let d = 0; d < e.length; d += 1) {
    let f = !1;
    for (const p of r) {
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
      n.set(p, { x: d * EN, y: h * kN });
    });
  return n;
}
function DN(e, t, n, i, r) {
  if (!e) return { kind: "becomeRoot" };
  const s = Mn(e, t, r);
  if (!s)
    return t.length > 0 ? { kind: "staleFrames" } : Re(n, i)[0] ? { kind: "wrapRoot" } : { kind: "leafError" };
  const a = s.slot.cardinality === "single" ? s.slot.activities.find((c) => c.nodeId !== n.nodeId) ?? null : null;
  return { kind: "addToSlot", slot: s.slot, replacedActivity: a };
}
const TN = "root";
function $N(e) {
  return e.length === 0 ? TN : e.map((t) => `${t.ownerNodeId}:${t.slotId}`).join("/");
}
function PN(e, t) {
  return e.length === t.length && e.every((n, i) => n.id === t[i]);
}
function MN({
  draft: e,
  scope: t,
  scopeOwner: n,
  frames: i,
  catalog: r,
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
  setError: w
}) {
  const [x, m] = K([]), [b, g] = K([]), [v, j] = K(null), [N, S] = K(null), [k, _] = K(null), M = re(null), I = re(null), $ = re(/* @__PURE__ */ new Map()), z = re(/* @__PURE__ */ new Set()), C = re(null), A = re([]), E = re(null), D = re(null), P = re(!1), T = ue(() => $N(i), [i]);
  Q(() => () => {
    v && $.current.set(T, v.getViewport());
  }, [v, T]), Q(() => {
    if (C.current = T, !n) {
      A.current = [], m([]), g([]);
      return;
    }
    const L = a ? al(n, r, e?.layout ?? []) : t ? sl(t, r, e?.layout ?? []) : { nodes: [], edges: [] };
    A.current = L.nodes.map((F) => F.id), m(L.nodes), g(L.edges);
  }, [r, e?.layout, a, t, n, T]), Q(() => {
    if (!v || C.current !== T || !PN(x, A.current)) return;
    C.current = null;
    const L = $.current.get(T), F = z.current.has(T);
    z.current.add(T), window.requestAnimationFrame(() => {
      L ? v.setViewport(L) : !F && x.length > 0 && v.fitView({ padding: 0.2 });
    });
  }, [x, v, T]);
  const B = ae((L, F, U) => U ? [
    ...L.filter((ie) => ie.nodeId !== F),
    { nodeId: F, x: Math.round(U.x), y: Math.round(U.y) }
  ] : L, []), W = ae((L, F) => {
    if (e?.state.rootActivity && a)
      return;
    const U = So(L, Ac(L)), ie = DN(e?.state.rootActivity, i, U, L, s);
    if (ie.kind === "becomeRoot") {
      f(
        ({ draft: le }) => le ? { ...le, state: { ...le.state, rootActivity: U } } : null,
        U.nodeId
      );
      return;
    }
    if (ie.kind === "leafError") {
      y(""), w("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
      return;
    }
    if (ie.kind === "staleFrames") {
      y(""), w("This slot could not be resolved — returning to the workflow root."), h();
      return;
    }
    if (ie.kind === "wrapRoot") {
      f(({ draft: le }) => {
        const he = le?.state.rootActivity;
        return he ? {
          ...le,
          layout: B(le.layout, he.nodeId, F),
          state: { ...le.state, rootActivity: Xs(U, [], [he], L) }
        } : null;
      }, e?.state.rootActivity?.nodeId ?? null), w(""), y(`Wrapped root in ${Ee(L)}`);
      return;
    }
    f(({ draft: le, frames: he }) => {
      if (!le?.state.rootActivity) return null;
      const me = Mn(le.state.rootActivity, he, s);
      if (!me) return null;
      const Ie = me.slot.cardinality === "single" ? [U] : [...me.slot.activities, U], Ce = Xs(le.state.rootActivity, he, Ie, s);
      return {
        ...le,
        layout: B(le.layout, U.nodeId, F),
        state: { ...le.state, rootActivity: Ce }
      };
    }, U.nodeId), ie.replacedActivity && (w(""), y(`Replaced ${ie.slot.label} content`));
  }, [s, e?.state.rootActivity, i, a, f, h, B, w, y]), H = ae((L, F) => {
    const U = So(L, Ac(L)), ie = {
      id: U.nodeId,
      type: "workflowActivity",
      position: F,
      selected: !0,
      data: {
        label: Ee(L),
        activityVersionId: L.activityVersionId,
        activityTypeKey: L.activityTypeKey,
        category: L.category,
        executionType: L.executionType,
        icon: dn(L),
        childSlots: Re(U, L),
        acceptsInbound: String(L.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: hl(U, L)
      }
    };
    return { activityNode: U, node: ie };
  }, []), Y = ae((L, F, U = []) => {
    a || d(({ draft: ie, frames: le }) => {
      if (!ie) return null;
      const he = cp(ie.layout, L), me = ie.state.rootActivity;
      if (!me) return { ...ie, layout: he };
      const Ie = Mn(me, le, s);
      if (!Ie) return { ...ie, layout: he };
      const Ce = sp(Ie, L, F, U), Le = Ie.slot.mode === "flowchart" ? ap(Ce, F) : Ce;
      return {
        ...ie,
        layout: he,
        state: {
          ...ie.state,
          rootActivity: op(me, le, Le, s)
        }
      };
    });
  }, [s, a, d]), V = ae((L, F) => {
    if (!M.current) return null;
    const U = M.current.getBoundingClientRect();
    return v ? v.screenToFlowPosition({ x: L, y: F }) : {
      x: L - U.left,
      y: F - U.top
    };
  }, [v]), q = ae((L, F) => document.elementFromPoint(L, F)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), G = ae((L, F, U) => {
    const ie = x.find((xe) => xe.id === F.source), le = x.find((xe) => xe.id === F.target), he = ie && le ? Xb(ie, le) : ie ? _c(ie) : U, me = H(L, he), Ce = [...x.map((xe) => xe.selected ? { ...xe, selected: !1 } : xe), me.node], Le = yp(b, F, me.node.id);
    m(Ce), g(Le), p(me.node.id), Y(Ce, Le, [me.activityNode]);
  }, [Y, H, b, x, p]), Z = ae((L, F, U) => {
    if (!u || !M.current) return !1;
    const ie = M.current.getBoundingClientRect();
    if (!(F >= ie.left && F <= ie.right && U >= ie.top && U <= ie.bottom)) return !1;
    const he = V(F, U);
    if (!he) return !1;
    if (c) {
      const me = q(F, U), Ie = me ? b.find((Ce) => Ce.id === me) : void 0;
      if (Ie)
        return G(L, Ie, he), !0;
    }
    return W(L, he), !0;
  }, [W, u, b, q, c, G, V]);
  Q(() => {
    const L = (U) => {
      const ie = E.current;
      if (!ie) return;
      Math.hypot(U.clientX - ie.startX, U.clientY - ie.startY) >= Nb && (ie.dragging = !0);
    }, F = (U) => {
      const ie = E.current;
      if (E.current = null, !ie?.dragging || !M.current || D.current) return;
      const le = M.current.getBoundingClientRect();
      U.clientX >= le.left && U.clientX <= le.right && U.clientY >= le.top && U.clientY <= le.bottom && (P.current = !0, window.setTimeout(() => {
        P.current = !1;
      }, 0), Z(ie.activity, U.clientX, U.clientY));
    };
    return window.addEventListener("pointermove", L), window.addEventListener("pointerup", F), window.addEventListener("pointercancel", F), () => {
      window.removeEventListener("pointermove", L), window.removeEventListener("pointerup", F), window.removeEventListener("pointercancel", F);
    };
  }, [v, Z]);
  const R = (L, F) => {
    D.current = { activityVersionId: F.activityVersionId, handledDrop: !1 }, L.dataTransfer.setData(xc, F.activityVersionId), L.dataTransfer.setData("text/plain", F.activityVersionId), L.dataTransfer.effectAllowed = "copy";
  }, X = (L, F) => {
    const U = D.current;
    D.current = null, !U?.handledDrop && (L.clientX === 0 && L.clientY === 0 || Z(F, L.clientX, L.clientY) && (P.current = !0, window.setTimeout(() => {
      P.current = !1;
    }, 0)));
  }, ce = (L, F) => {
    L.button === 0 && (E.current = {
      activity: F,
      startX: L.clientX,
      startY: L.clientY,
      dragging: !1
    });
  }, se = (L) => {
    P.current || u && W(L);
  }, ee = (L) => {
    if (!u) {
      L.dataTransfer.dropEffect = "none";
      return;
    }
    if (L.preventDefault(), L.dataTransfer.dropEffect = "copy", !c) return;
    const F = q(L.clientX, L.clientY);
    _(F);
  }, ne = (L) => {
    if (!M.current) return;
    const F = L.relatedTarget;
    F && M.current.contains(F) || _(null);
  }, de = (L) => {
    L.preventDefault(), _(null);
    const F = L.dataTransfer.getData(xc) || L.dataTransfer.getData("text/plain");
    if (!F || (L.stopPropagation(), D.current?.activityVersionId === F && (D.current.handledDrop = !0), !u)) return;
    const U = s.get(F);
    U && Z(U, L.clientX, L.clientY);
  }, O = (L) => {
    if (!u) return;
    if (L) {
      S({ kind: "fromEmpty", clientX: L.clientX, clientY: L.clientY });
      return;
    }
    const F = M.current?.getBoundingClientRect();
    F && S({
      kind: "fromEmpty",
      clientX: F.left + F.width / 2,
      clientY: F.top + F.height / 2
    });
  }, te = (L) => {
    const F = a ? L.filter((U) => U.type === "select") : L;
    F.length !== 0 && m((U) => Bu(F, U));
  }, ge = (L) => {
    a || g((F) => Fu(L, F));
  }, ye = (L) => !L.source || !L.target || L.source === L.target || !c ? !1 : !L.targetHandle, Ae = (L) => {
    if (!e?.state.rootActivity || !t || !c || !ye(L)) return;
    const F = Vi(L.source, L.target, L.sourceHandle ?? "Done", L.targetHandle ?? void 0), U = Xu(F, b);
    g(U), Y(x, U);
  }, _e = () => {
    Y(x, b);
  }, $e = !a && x.length > 0, ot = ae(() => {
    if (a || x.length === 0) return;
    const L = t?.slot.mode === "sequence" ? "sequence" : "flowchart", F = IN(x, b, L), U = x.map((ie) => {
      const le = F.get(ie.id);
      return le ? { ...ie, position: le } : ie;
    });
    m(U), Y(U, b), window.requestAnimationFrame(() => v?.fitView({ padding: 0.2 })), y("Rearranged the canvas.");
  }, [b, x, t, a, Y, v, y]), Xe = (L, F) => {
    if (!F.nodeId || F.handleType === "target") {
      I.current = null;
      return;
    }
    I.current = {
      nodeId: F.nodeId,
      handleId: F.handleId ?? null
    };
  }, qe = (L, F) => {
    const U = Yb(I.current, F);
    if (I.current = null, !U || !c || F.toNode || F.toHandle || qb(L)) return;
    const ie = Md(L);
    S({
      kind: "fromPort",
      sourceNodeId: U.nodeId,
      sourceHandleId: U.handleId,
      clientX: ie.x,
      clientY: ie.y
    });
  }, Pe = (L, F) => {
    if (!c || !ye(F)) return;
    const U = iv(L, {
      ...F,
      sourceHandle: F.sourceHandle ?? "Done",
      // Connection.targetHandle is string | null; null and undefined are indistinguishable downstream
      // (every consumer checks truthiness), so keep the type-correct null.
      targetHandle: F.targetHandle ?? null
    }, b, { shouldReplaceId: !1 });
    g(U), Y(x, U);
  }, Oe = (L) => {
    if (a || L.length === 0) return;
    const F = new Set(L.map((le) => le.id)), U = x.filter((le) => !F.has(le.id)), ie = b.filter((le) => !F.has(le.source) && !F.has(le.target));
    m(U), g(ie), l && F.has(l) && p(null), Y(U, ie);
  }, He = (L) => {
    if (a || L.length === 0) return;
    const F = new Set(L.map((ie) => ie.id)), U = b.filter((ie) => !F.has(ie.id));
    g(U), Y(x, U);
  }, Ne = ae((L) => {
    if (a) return;
    const F = b.filter((U) => U.id !== L);
    g(F), Y(x, F);
  }, [Y, b, a, x]), Lt = ae((L, F, U) => {
    c && S({ kind: "spliceEdge", edgeId: L, clientX: F, clientY: U });
  }, [c]), De = (L) => {
    const F = N;
    if (!F) return;
    S(null);
    const U = V(F.clientX, F.clientY) ?? { x: 0, y: 0 };
    if (F.kind === "fromEmpty") {
      const le = H(L, U), me = [...x.map((Ie) => Ie.selected ? { ...Ie, selected: !1 } : Ie), le.node];
      m(me), p(le.node.id), Y(me, b, [le.activityNode]);
      return;
    }
    if (F.kind === "fromPort") {
      const le = x.find((xe) => xe.id === F.sourceNodeId), he = le ? _c(le) : U, me = H(L, he), Ce = [...x.map((xe) => xe.selected ? { ...xe, selected: !1 } : xe), me.node], Le = [...b, Vi(F.sourceNodeId, me.node.id, F.sourceHandleId ?? "Done")];
      m(Ce), g(Le), p(me.node.id), Y(Ce, Le, [me.activityNode]);
      return;
    }
    const ie = b.find((le) => le.id === F.edgeId);
    ie && G(L, ie, U);
  }, hn = ue(() => ({
    highlightedEdgeId: k,
    deleteEdge: Ne,
    requestInsertActivity: Lt
  }), [Ne, k, Lt]);
  return {
    nodes: x,
    edges: b,
    canvasRef: M,
    setReactFlowInstance: j,
    connectMenu: N,
    setConnectMenu: S,
    edgeActions: hn,
    onNodesChange: te,
    onEdgesChange: ge,
    onNodesDelete: Oe,
    onEdgesDelete: He,
    isValidConnection: ye,
    onConnect: Ae,
    onConnectStart: Xe,
    onConnectEnd: qe,
    onReconnect: Pe,
    commitLayout: _e,
    canAutoLayout: $e,
    autoLayout: ot,
    onCanvasDragOver: ee,
    onCanvasDragLeave: ne,
    onCanvasDrop: de,
    openEmptyConnectMenu: O,
    onConnectMenuPick: De,
    addActivity: W,
    onPaletteClick: se,
    onPaletteDragStart: R,
    onPaletteDragEnd: X,
    onPalettePointerDown: ce
  };
}
const Mc = "elsa-studio:apply-workflow-graph-operation-batch", Rc = "elsa-studio:undo-workflow-graph-operation-batch", RN = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function LN(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const i = KN(e), r = Fd(i.state.rootActivity), s = /* @__PURE__ */ new Map(), a = /* @__PURE__ */ new Map(), c = [];
  for (const u of t.operations) {
    const l = FN(u.kind), d = u.parameters ?? {};
    if (l === "add-activity") {
      const f = ze(d.activityId) ?? u.temporaryReferences?.[0], p = BN(f ?? ze(d.displayName) ?? ze(d.activityType) ?? "weaver-activity", r), h = zN(u, p, n);
      a.set(p, h), c.push(p), f && s.set(f, p), i.state.rootActivity && VN(i.state.rootActivity, h);
      const y = wt(d.position) ? Oo(d.position, { x: 280, y: 160 }) : null;
      y && (i.layout = Lc(i.layout, p, y));
      continue;
    }
    if (l === "set-root") {
      const f = vo(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown root activity.");
      i.state.rootActivity = f;
      continue;
    }
    if (l === "set-designer-position") {
      const f = Rt(d.activityId, s);
      if (!f || !_s(i.state.rootActivity, f)) throw new Error("Weaver batch referenced an unknown activity position.");
      i.layout = Lc(i.layout, f, Oo(d, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const f = vo(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity property target.");
      WN(f, ze(d.propertyName) ?? "Value", d.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const f = vo(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity update target.");
      const p = wt(d.patch) ? d.patch : d;
      Object.assign(f, p);
      continue;
    }
    if (l === "remove-activity") {
      const f = Rt(d.activityId, s);
      if (!f) throw new Error("Weaver batch referenced an unknown activity remove target.");
      i.state.rootActivity = Bd(i.state.rootActivity, f), i.layout = i.layout.filter((p) => p.nodeId !== f);
      continue;
    }
    if (l === "connect-activities") {
      ON(i, d, s);
      continue;
    }
    if (l === "disconnect-activities") {
      HN(i, d, s);
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
function zN(e, t, n) {
  const i = e.parameters ?? {}, r = ze(i.activityVersionId) ?? ze(i.activityType) ?? "Elsa.Workflows.Activity", s = n.find((a) => a.activityVersionId === r || a.activityTypeKey === r || a.displayName === ze(i.displayName));
  return s ? So(s, t) : {
    nodeId: t,
    // catalogItem is provably undefined here (the truthy branch returned above), so use the id directly.
    activityVersionId: r,
    inputs: [],
    outputs: [],
    ...ze(i.displayName) ? { displayName: ze(i.displayName) } : {},
    designer: { position: Oo(i.position, { x: 280, y: 160 }) }
  };
}
function VN(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = Ds(e);
  n && !n.some((i) => i.nodeId === t.nodeId) && n.push(t);
}
function ON(e, t, n) {
  const i = e.state.rootActivity;
  if (!i?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const r = Rt(t.sourceActivityId ?? t.sourceId ?? t.from, n), s = Rt(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!r || !s) throw new Error("Weaver batch connection is missing source or target activity.");
  const a = i.structure.payload, c = Array.isArray(a.connections) ? a.connections : [], u = ze(t.connectionId) ?? `flow-${r}-${s}`;
  a.connections = [
    ...c.filter((l) => !wt(l) || l.id !== u),
    {
      id: u,
      source: { nodeId: r, port: ze(t.outcome) ?? ze(t.sourcePort) ?? "Done" },
      target: { nodeId: s }
    }
  ];
}
function HN(e, t, n) {
  const i = e.state.rootActivity, r = i?.structure?.payload.connections;
  if (!Array.isArray(r)) return;
  const s = ze(t.connectionId), a = Rt(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = Rt(t.targetActivityId ?? t.targetId ?? t.to, n);
  i.structure.payload.connections = r.filter((u) => {
    if (!wt(u)) return !0;
    if (s && u.id === s) return !1;
    const l = wt(u.source) ? u.source.nodeId : void 0, d = wt(u.target) ? u.target.nodeId : void 0;
    return l !== a || d !== c;
  });
}
function WN(e, t, n) {
  const i = wt(n);
  e[kl(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: i ? "Object" : "Literal", value: n }
  };
}
function vo(e, t, n, i) {
  const r = Rt(t, n);
  return r ? _s(e.state.rootActivity, r) ?? i.get(r) ?? null : null;
}
function Rt(e, t) {
  const n = ze(e);
  return n ? t.get(n) ?? n : null;
}
function _s(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of Kd(e)) {
    const i = _s(n, t);
    if (i) return i;
  }
  return null;
}
function Bd(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = Ds(e);
  if (n) {
    const i = n.map((r) => Bd(r, t)).filter((r) => !!r);
    n.splice(0, n.length, ...i);
  }
  return e;
}
function Fd(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of Kd(e)) Fd(n, t);
  return t;
}
function Kd(e) {
  return Ds(e) ?? [];
}
function Ds(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function Lc(e, t, n) {
  return [
    ...e.filter((i) => i.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function Oo(e, t) {
  const n = wt(e) ? e : {}, i = Number(n.x), r = Number(n.y);
  return {
    x: Number.isFinite(i) ? Math.max(40, Math.round(i)) : t.x,
    y: Number.isFinite(r) ? Math.max(40, Math.round(r)) : t.y
  };
}
function BN(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let i = n, r = 2;
  for (; t.has(i); )
    i = `${n}-${r}`, r += 1;
  return t.add(i), i;
}
function FN(e) {
  return typeof e == "number" ? RN[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function ze(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function KN(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function wt(e) {
  return typeof e == "object" && e !== null;
}
function XN({
  draft: e,
  details: t,
  catalog: n,
  replaceDraftByBatch: i,
  setStatus: r,
  setError: s
}) {
  const a = re(/* @__PURE__ */ new Map());
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
        const p = In(e), h = LN(e, d.batch, n), y = `weaver-batch-${Date.now()}`;
        a.current.set(y, p), i(h.draft, h.finalActivityIds.at(-1) ?? null), r(h.summary), s(""), d.respond({ ok: !0, result: { ...h, undoToken: y } });
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
      a.current.delete(d.undoToken), i(f, null), r("Restored workflow draft before Weaver batch."), s(""), d.respond({ ok: !0, summary: "Restored workflow draft before Weaver batch." });
    };
    return window.addEventListener(Mc, c), window.addEventListener(Rc, u), () => {
      window.removeEventListener(Mc, c), window.removeEventListener(Rc, u);
    };
  }, [n, t, e, i, r, s]);
}
function qN({ context: e, draft: t, autosaveEnabledByDefault: n = !0, editDraft: i, setStatus: r, setError: s }) {
  const [a, c] = K(n), u = re(""), l = re(0), d = re(Promise.resolve()), f = ae((h) => {
    u.current = h ? Be(h) : "";
  }, []), p = ae(async (h, y) => {
    const w = async () => {
      const m = ++l.current, b = Be(h);
      s("");
      try {
        const g = await Xp(e, h), v = Be(g);
        return u.current = v, i(({ draft: j }) => !j || j.id !== g.id ? null : Be(j) === b ? g : { ...j, validationErrors: g.validationErrors }), m === l.current && r(y), g;
      } catch (g) {
        throw m === l.current && (r(""), s(g instanceof Error ? g.message : String(g))), g;
      }
    }, x = d.current.then(w, w);
    return d.current = x.catch(() => {
    }), x;
  }, [e, i, r, s]);
  return Q(() => {
    if (!a || !t || Be(t) === u.current) return;
    r("Autosaving...");
    const y = window.setTimeout(() => {
      p(t, "Autosaved").catch(() => {
      });
    }, jb);
    return () => window.clearTimeout(y);
  }, [a, t, p, r]), { saveDraft: p, autosaveEnabled: a, setAutosaveEnabled: c, markSaved: f };
}
function YN({ context: e, definitionId: t, resetHistory: n, loadDraft: i, markSaved: r, setError: s }) {
  const [a, c] = K(null), [u, l] = K([]), [d, f] = K([]), [p, h] = K(null), [y, w] = K(Ai), [x, m] = K("loading"), b = ae(async () => {
    s(""), m("loading");
    const [g, v, j, N, S] = await Promise.all([
      Lp(e, t),
      nr(e),
      nh(e).then(
        (_) => ({ ok: !0, descriptors: _ }),
        () => ({ ok: !1, descriptors: [] })
      ),
      ih(e).then(
        (_) => ({ ok: !0, descriptors: _ }),
        () => ({ ok: !1, descriptors: Ai })
      ),
      // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
      vl(e).then(
        (_) => _,
        () => null
      )
    ]), k = g.draft ?? null;
    c(g), r(k), n(k), i(k), l(v.activities ?? []), f(j.descriptors), h(S), w(N.descriptors.length > 0 ? N.descriptors : Ai), m(j.ok ? "ready" : "failed");
  }, [e, t, n, i, r, s]);
  return Q(() => {
    b().catch((g) => s(g instanceof Error ? g.message : String(g)));
  }, [b, s]), {
    details: a,
    setDetails: c,
    catalog: u,
    activityDescriptors: d,
    availabilityDiagnostics: p,
    expressionDescriptors: y,
    descriptorStatus: x,
    reload: b
  };
}
function UN({ context: e, details: t, setDetails: n, setStatus: i }) {
  const r = re(null), s = re(null), a = re({});
  Q(() => {
    r.current = t;
  }, [t]);
  const c = ae(() => {
    s.current !== null && (window.clearTimeout(s.current), s.current = null);
    const l = a.current;
    a.current = {};
    const d = r.current?.definition;
    !d || l.name === void 0 && l.description === void 0 || Kp(e, d.id, {
      name: l.name ?? d.name,
      description: l.description ?? d.description ?? null
    }).then((f) => n((p) => p && p.definition.id === f.definition.id ? { ...p, definition: f.definition } : p)).catch(() => i("Couldn't save name/description."));
  }, [e, n, i]), u = ae((l) => {
    n((d) => d && { ...d, definition: { ...d.definition, ...l } }), a.current = { ...a.current, ...l }, s.current !== null && window.clearTimeout(s.current), s.current = window.setTimeout(c, 800);
  }, [c, n]);
  return Q(() => () => {
    c();
  }, [c]), { updateDefinitionMeta: u };
}
function ZN({
  context: e,
  draft: t,
  details: n,
  busy: i,
  saveDraft: r,
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
  const y = ae(() => {
    if (!t) return;
    const b = n?.definition.name;
    tb(J0(t, b), b), d("Exported workflow as JSON.");
  }, [t, n, d]), w = ae(async () => {
    if (!(!t || i)) {
      l("saving"), d("Saving...");
      try {
        await r(t, "Saved");
      } catch {
      } finally {
        l("idle");
      }
    }
  }, [t, i, r, l, d]), x = ae(async () => {
    if (!(!t || i)) {
      l("promoting"), d("Saving...");
      try {
        await r(t, "Saved"), d("Promoting...");
        const b = await qp(e, t.id), g = await Yp(e, b.versionId);
        u(g.artifactId), d(`Published ${g.artifactVersion}`), await s();
      } catch (b) {
        d(""), f(b instanceof Error ? b.message : String(b));
      } finally {
        l("idle");
      }
    }
  }, [t, i, e, r, s, u, l, d, f]), m = ae(async () => {
    if (!t?.state.rootActivity || i) return;
    const b = t, g = Be(b);
    c(), d("Preparing test run...");
    try {
      l("testRunPreparing"), d("Preparing test run...");
      const v = Jb(b);
      l("testRunStarting"), d("Starting test run...");
      const j = await Up(e, {
        definitionId: b.definitionId,
        snapshotId: v,
        state: b.state
      });
      a({ draftSignature: g, view: j }), p("runtime"), h(!1), d(ks(j) ? "Test run rejected" : "Test run dispatched");
    } catch (v) {
      d(""), f(v instanceof Error ? v.message : String(v));
    } finally {
      l("idle");
    }
  }, [t, i, e, c, a, p, h, l, d, f]);
  return { exportJson: y, save: w, promoteAndPublish: x, run: m };
}
function GN({
  context: e,
  draft: t,
  frames: n,
  selectedNodeId: i,
  catalog: r,
  activityDescriptors: s,
  availabilityDiagnostics: a
}) {
  const c = t?.state.rootActivity ?? null, u = ue(() => new Map(r.map((S) => [S.activityVersionId, S])), [r]), l = ae(
    (S) => Ch([S.activityVersionId, S.activityTypeKey], a),
    [a]
  ), d = ue(() => Hb(s), [s]), f = ue(() => nl(c, n, u), [c, n, u]), p = cl(f, f ? u.get(f.activityVersionId) : void 0), h = !!f && p === "unsupported", y = ue(() => h ? null : Mn(c, n, u), [c, n, u, h]), w = ue(() => h && f?.nodeId === i ? f : y?.slot.activities.find((S) => S.nodeId === i) ?? null, [h, y, f, i]), x = ue(
    () => w ? Wb(w, u, d) : null,
    [u, d, w]
  ), m = ue(
    () => w ? l({ activityVersionId: w.activityVersionId, activityTypeKey: u.get(w.activityVersionId)?.activityTypeKey }) : null,
    [l, u, w]
  ), b = w ? Re(w, u) : [], g = w ? Ep(w, u.get(w.activityVersionId)) : !1, v = Vp(e, t?.state, i, u), j = !h && y?.slot.mode === "flowchart";
  return {
    catalogByVersion: u,
    availabilityLookup: l,
    scopeOwner: f,
    isUnsupportedDesigner: h,
    scope: y,
    selectedNode: w,
    selectedDescriptor: x,
    selectedNodeAvailability: m,
    selectedSlots: b,
    selectedSupportsScopedVariables: g,
    scopedVariableAnalysis: v,
    isFlowchartDesigner: j,
    canAddActivitiesToCanvas: !c || !h
  };
}
function JN({
  details: e,
  draft: t,
  selectedNode: n,
  selectedNodeId: i,
  selectedDescriptor: r,
  catalogByVersion: s
}) {
  Q(() => {
    if (!(!e || !t))
      return window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
        workflowId: e.definition.id,
        workflowDefinitionId: e.definition.id,
        workflowVersionId: t.sourceVersionId ?? null,
        draftId: t.id,
        revision: Ub(t),
        selectedNodeId: i,
        selectedActivityType: r?.typeName ?? (n ? s.get(n.activityVersionId)?.activityTypeKey ?? n.activityVersionId : null),
        summary: e.definition.name,
        activities: Rd(t.state.rootActivity, s),
        connections: Ld(t.state.rootActivity, s),
        diagnostics: t.validationErrors.map((a) => ({ severity: a.code ?? "warning", message: a.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === e.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [s, e, t, r, n, i]);
}
function QN({
  paletteSearch: e,
  onSearchChange: t,
  groups: n,
  expandedCategories: i,
  onToggleCategory: r,
  onActivityClick: s,
  onActivityDragStart: a,
  onActivityDragEnd: c,
  onActivityPointerDown: u
}) {
  const l = e.trim().length > 0;
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-palette-body", children: [
    /* @__PURE__ */ o.jsxs("label", { className: "wf-palette-search", children: [
      /* @__PURE__ */ o.jsx(Qi, { size: 14, "aria-hidden": "true" }),
      /* @__PURE__ */ o.jsx(
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
    /* @__PURE__ */ o.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: n.length === 0 ? /* @__PURE__ */ o.jsx("p", { className: "wf-muted wf-palette-empty", children: "No matching activities." }) : n.map((d) => {
      const f = l || i.has(d.category);
      return /* @__PURE__ */ o.jsxs("div", { className: "wf-palette-category", children: [
        /* @__PURE__ */ o.jsxs(
          "button",
          {
            type: "button",
            className: "wf-palette-category-toggle",
            role: "treeitem",
            "aria-expanded": f,
            onClick: () => r(d.category),
            children: [
              f ? /* @__PURE__ */ o.jsx(qc, { size: 14 }) : /* @__PURE__ */ o.jsx(mt, { size: 14 }),
              /* @__PURE__ */ o.jsx("span", { children: d.category }),
              /* @__PURE__ */ o.jsx("small", { children: d.activities.length })
            ]
          }
        ),
        f ? /* @__PURE__ */ o.jsx("div", { className: "wf-palette-activities", role: "group", children: d.activities.map((p) => {
          const h = p.description?.trim(), y = h ? `wf-palette-description-${p.activityVersionId}` : void 0, w = Ee(p), x = dn(p);
          return /* @__PURE__ */ o.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-activity",
              role: "treeitem",
              draggable: !0,
              title: h || Ee(p),
              "aria-describedby": y,
              onClick: () => s(p),
              onDragStart: (m) => a(m, p),
              onDragEnd: (m) => c(m, p),
              onPointerDown: (m) => u(m, p),
              children: [
                /* @__PURE__ */ o.jsx("span", { className: "wf-activity-icon", "data-icon": x, "aria-hidden": "true", children: rr(x) }),
                /* @__PURE__ */ o.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ o.jsx("strong", { children: w }),
                  h ? /* @__PURE__ */ o.jsx("small", { id: y, children: h }) : null
                ] }),
                /* @__PURE__ */ o.jsx(Yc, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            p.activityVersionId
          );
        }) }) : null
      ] }, d.category);
    }) })
  ] });
}
const Xd = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]), ej = "Variable";
function tj({
  activity: e,
  descriptor: t,
  editors: n,
  expressionEditors: i,
  expressionDescriptors: r,
  descriptorStatus: s,
  visibleVariables: a,
  scopeStatus: c,
  onChange: u
}) {
  if (s === "loading")
    return /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "Loading activity properties..." });
  if (!t)
    return /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "No activity descriptor is available for this activity." });
  const l = t.inputs.filter((p) => p.isBrowsable !== !1).sort((p, h) => (p.order ?? 0) - (h.order ?? 0) || p.name.localeCompare(h.name));
  if (l.length === 0)
    return /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "This activity does not expose editable properties." });
  const d = lj(l), f = r.length > 0 ? r : Eh;
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ o.jsx("span", { className: "wf-section-label", children: "Properties" }),
    d.map((p) => /* @__PURE__ */ o.jsxs("section", { className: "wf-property-group", children: [
      d.length > 1 ? /* @__PURE__ */ o.jsx("h4", { children: p.category }) : null,
      p.inputs.map((h) => /* @__PURE__ */ o.jsx(
        nj,
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
function nj({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: i,
  expressionDescriptors: r,
  visibleVariables: s,
  scopeStatus: a,
  onChange: c
}) {
  const u = t.isReadOnly === !0, l = { activity: e, expressionDescriptors: r, readOnly: u }, d = Fo(n, t, l), f = d?.component, p = t.isWrapped !== !1 ? Il(e, t) : null, h = p?.expression.type ?? "Literal", y = _h(e, t), w = h.toLowerCase(), m = p && (w === "literal" || w === "object") && !Ph(t) ? Th(t.typeName) : null, b = m ? Fo(n, t, { ...l, scope: "collection" }) : void 0, g = p ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: r,
    readOnly: u,
    surface: "inline",
    syntax: h
  } : null, v = g ? Yd(i, g) : null, j = v?.surfaces.inline, N = v && g ? Ud(v, g, y) : [], S = m != null, k = !!(p && !S && uj(t, d?.id)), _ = !!(p && !S && dj(t, d?.id)), [M, I] = K(!1), $ = (E) => {
    const D = p ? Ih(p, E) : E;
    c(ia(e, t, D));
  }, z = (E) => {
    p && c(ia(e, t, Ah(p, E)));
  }, C = m ? b ? Ho(b.component, t, y, u, { ...l, scope: "collection" }, $) : /* @__PURE__ */ o.jsx(
    rj,
    {
      input: t,
      elementTypeName: m.elementTypeName,
      value: y,
      editors: n,
      context: l,
      disabled: u,
      onChange: $
    }
  ) : null, A = h === ej && p ? /* @__PURE__ */ o.jsx(
    aj,
    {
      value: y,
      visibleVariables: s,
      scopeStatus: a,
      disabled: u,
      onChange: $
    }
  ) : C ?? (j && g ? /* @__PURE__ */ o.jsx(
    j,
    {
      descriptor: t,
      syntax: h,
      value: y,
      disabled: u,
      context: g,
      onChange: $
    }
  ) : Ho(f, t, y, u, l, $));
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ o.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ o.jsx("span", { children: rs(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ o.jsx("p", { children: t.description }) : null,
    p && !k ? /* @__PURE__ */ o.jsx(
      Wo,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: h,
        descriptors: r,
        disabled: u,
        onChange: z
      }
    ) : null,
    k ? /* @__PURE__ */ o.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-expression-editor", children: [
        A,
        Ko(N)
      ] }),
      /* @__PURE__ */ o.jsx(
        Wo,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: h,
          descriptors: r,
          disabled: u,
          variant: "inline",
          onChange: z
        }
      ),
      _ ? /* @__PURE__ */ o.jsx(
        "button",
        {
          type: "button",
          className: "wf-expression-expand-button",
          "aria-label": `Open expanded ${t.displayName || t.name} editor`,
          title: "Open expanded editor",
          onClick: () => I(!0),
          children: /* @__PURE__ */ o.jsx(Tn, { size: 13 })
        }
      ) : null
    ] }) : /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
      A,
      Ko(N)
    ] }),
    _ && !k ? /* @__PURE__ */ o.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => I(!0),
        children: [
          /* @__PURE__ */ o.jsx(Tn, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    M ? /* @__PURE__ */ o.jsx(
      oj,
      {
        input: t,
        value: y,
        syntax: h,
        descriptors: r,
        activity: e,
        expressionEditors: i,
        disabled: u,
        onChange: $,
        onSyntaxChange: z,
        onClose: () => I(!1)
      }
    ) : null
  ] });
}
function ij(e, t, n) {
  return [
    "wf-collection-item",
    t === e ? "dragging" : "",
    t !== null && t !== e && n === e ? "drop-target" : ""
  ].filter(Boolean).join(" ");
}
function rj({
  input: e,
  elementTypeName: t,
  value: n,
  editors: i,
  context: r,
  disabled: s,
  onChange: a
}) {
  const c = Mh(n), u = Lh(e, t), l = { ...r, scope: "element" }, d = Fo(i, u, l)?.component, f = e.displayName || e.name, p = (j, N) => a(c.map((S, k) => k === j ? N : S)), [h, y] = K(null), [w, x] = K(null), m = () => {
    y(null), x(null);
  }, b = (j) => (N) => {
    y(j), N.dataTransfer.effectAllowed = "move", N.dataTransfer.setData("text/plain", String(j));
  }, g = (j) => (N) => {
    h !== null && (N.preventDefault(), N.dataTransfer.dropEffect = "move", w !== j && x(j));
  }, v = (j) => (N) => {
    N.preventDefault(), h !== null && h !== j && a(ro(c, h, j)), m();
  };
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-collection-editor", children: [
    c.length === 0 ? /* @__PURE__ */ o.jsx("p", { className: "wf-collection-empty", children: "No items yet." }) : /* @__PURE__ */ o.jsx("ul", { className: "wf-collection-items", children: c.map((j, N) => /* @__PURE__ */ o.jsxs(
      "li",
      {
        className: ij(N, h, w),
        onDragOver: g(N),
        onDrop: v(N),
        children: [
          /* @__PURE__ */ o.jsx(
            "span",
            {
              className: "wf-collection-item-handle",
              draggable: !s,
              "aria-label": `Drag ${f} item ${N + 1} to reorder`,
              title: "Drag to reorder",
              onDragStart: b(N),
              onDragEnd: m,
              children: /* @__PURE__ */ o.jsx(Yc, { size: 13, "aria-hidden": "true" })
            }
          ),
          /* @__PURE__ */ o.jsx("div", { className: "wf-collection-item-editor", children: Ho(d, u, j, s, l, (S) => p(N, S)) }),
          /* @__PURE__ */ o.jsxs("div", { className: "wf-collection-item-actions", children: [
            /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${N + 1} up`,
                disabled: s || N === 0,
                onClick: () => a(ro(c, N, N - 1)),
                children: /* @__PURE__ */ o.jsx(df, { size: 13 })
              }
            ),
            /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${N + 1} down`,
                disabled: s || N === c.length - 1,
                onClick: () => a(ro(c, N, N + 1)),
                children: /* @__PURE__ */ o.jsx(qc, { size: 13 })
              }
            ),
            /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button danger",
                "aria-label": `Remove ${f} item ${N + 1}`,
                disabled: s,
                onClick: () => a(c.filter((S, k) => k !== N)),
                children: /* @__PURE__ */ o.jsx(Dn, { size: 13 })
              }
            )
          ] })
        ]
      },
      N
    )) }),
    /* @__PURE__ */ o.jsxs(
      "button",
      {
        type: "button",
        className: "wf-collection-add",
        disabled: s,
        onClick: () => a([...c, Rh(t)]),
        children: [
          /* @__PURE__ */ o.jsx(Qt, { size: 13 }),
          " Add item"
        ]
      }
    )
  ] });
}
function oj({
  input: e,
  value: t,
  syntax: n,
  descriptors: i,
  activity: r,
  expressionEditors: s,
  disabled: a,
  onChange: c,
  onSyntaxChange: u,
  onClose: l
}) {
  const d = Bc(), f = e.displayName || e.name, p = {
    activity: r,
    descriptor: e,
    expressionDescriptors: i,
    readOnly: a,
    surface: "expanded",
    syntax: n
  }, h = Yd(s, p), y = h?.surfaces.expanded, w = h ? Ud(h, p, t) : [], x = y ? null : cj(s, p);
  return Q(() => {
    const m = (b) => {
      b.key === "Escape" && l();
    };
    return window.addEventListener("keydown", m), () => window.removeEventListener("keydown", m);
  }, [l]), /* @__PURE__ */ o.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ o.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": d, children: [
    /* @__PURE__ */ o.jsxs("header", { children: [
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ o.jsx("h3", { id: d, children: f })
      ] }),
      /* @__PURE__ */ o.jsx("button", { type: "button", "aria-label": `Close ${f} editor`, onClick: l, children: /* @__PURE__ */ o.jsx(Uo, { size: 16 }) })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ o.jsx(
          Wo,
          {
            label: `${f} expression syntax`,
            value: n,
            descriptors: i,
            disabled: a,
            onChange: u
          }
        ),
        /* @__PURE__ */ o.jsx("span", { children: rs(e.typeName) })
      ] }),
      e.description ? /* @__PURE__ */ o.jsx("p", { children: e.description }) : null,
      y ? /* @__PURE__ */ o.jsx(
        y,
        {
          descriptor: e,
          syntax: n,
          value: t,
          disabled: a,
          context: p,
          onChange: c
        }
      ) : /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
        x ? /* @__PURE__ */ o.jsx("p", { className: "wf-expression-editor-hint", children: x }) : null,
        /* @__PURE__ */ o.jsx(
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
      Ko(w)
    ] }),
    /* @__PURE__ */ o.jsxs("footer", { children: [
      /* @__PURE__ */ o.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: l, children: "Close" })
    ] })
  ] }) });
}
function Ho(e, t, n, i, r, s) {
  return e ? /* @__PURE__ */ o.jsx(
    e,
    {
      descriptor: t,
      value: n,
      disabled: i,
      context: r,
      onChange: s
    }
  ) : /* @__PURE__ */ o.jsx("input", { type: "text", value: n == null ? "" : String(n), disabled: i, onChange: (a) => s(a.target.value) });
}
function Wo({
  label: e,
  value: t,
  descriptors: n,
  disabled: i,
  variant: r = "block",
  onChange: s
}) {
  const [a, c] = K(!1), u = Bc(), l = n.find((f) => f.type === t), d = [
    "wf-syntax-picker-trigger",
    r === "inline" ? "inline" : "",
    a ? "open" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ o.jsxs("div", { className: r === "inline" ? "wf-syntax-picker inline" : "wf-syntax-picker", onBlur: (f) => {
    f.currentTarget.contains(f.relatedTarget) || c(!1);
  }, children: [
    /* @__PURE__ */ o.jsx(
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
        children: /* @__PURE__ */ o.jsx("span", { children: l?.displayName || l?.type || t })
      }
    ),
    a ? /* @__PURE__ */ o.jsx("div", { id: u, role: "listbox", className: "wf-syntax-picker-menu", "aria-label": e, children: n.map((f) => {
      const p = f.displayName || f.type, h = f.type === t;
      return /* @__PURE__ */ o.jsx(
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
const Bo = "::";
function qd(e) {
  return !e || e === Hi ? Hi : e;
}
function zc(e, t) {
  return `${qd(t)}${Bo}${e}`;
}
function sj(e) {
  const t = e.indexOf(Bo);
  if (t < 0) return null;
  const n = e.slice(t + Bo.length);
  return n ? { scopeId: e.slice(0, t), referenceKey: n } : null;
}
function aj({ value: e, visibleVariables: t, scopeStatus: n, disabled: i, onChange: r }) {
  const s = ml(e), a = !!e && typeof e == "object" || typeof e == "string" && e.trim().startsWith("{"), c = s && (a || t.some((d) => d.referenceKey === s.referenceKey)) ? s : null, u = c ? zc(c.referenceKey, c.declaringScopeId) : "", l = !!c && t.some(
    (d) => d.referenceKey === c.referenceKey && d.scopeId === qd(c.declaringScopeId)
  );
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-variable-picker", children: [
    /* @__PURE__ */ o.jsxs(
      "select",
      {
        "aria-label": "Variable reference",
        value: u,
        disabled: i,
        onChange: (d) => {
          const f = sj(d.target.value);
          f && r(Ip(f.referenceKey, f.scopeId));
        },
        children: [
          /* @__PURE__ */ o.jsx("option", { value: "", children: "Select a variable…" }),
          c && !l ? /* @__PURE__ */ o.jsxs("option", { value: u, children: [
            c.referenceKey,
            " (not visible from this scope)"
          ] }) : null,
          t.map((d) => {
            const f = zc(d.referenceKey, d.scopeId);
            return /* @__PURE__ */ o.jsxs("option", { value: f, children: [
              d.name,
              d.isWorkflowScope ? " · workflow" : " · container"
            ] }, f);
          })
        ]
      }
    ),
    n === "unavailable" ? /* @__PURE__ */ o.jsx("p", { className: "wf-variable-picker-note", children: "Variable scope information is unavailable (pending backend support). Existing references are preserved." }) : n === "ready" && t.length === 0 ? /* @__PURE__ */ o.jsx("p", { className: "wf-variable-picker-note", children: "No variables are visible here. Declare one on the workflow or a container scope." }) : null
  ] });
}
function Fo(e, t, n) {
  return [...e].sort((i, r) => (i.order ?? 500) - (r.order ?? 500)).find((i) => i.supports(t, n));
}
function Yd(e, t) {
  return [...e].sort((n, i) => (n.order ?? 500) - (i.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function Ud(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function cj(e, t) {
  if (t.syntax.toLowerCase() === "literal") return null;
  const n = [...e].sort((a, c) => (a.order ?? 500) - (c.order ?? 500)).find((a) => a.supports(t) && a.metadata)?.metadata;
  if (!n) return `No enhanced editor is registered for ${t.syntax}. Using the generic text editor.`;
  const i = n.displayName?.trim() || "enhanced editor", r = n.installHint?.trim(), s = `No ${i} is registered for ${t.syntax}. Using the generic text editor.`;
  return r ? `${s} ${r}` : s;
}
function Ko(e) {
  return e.length === 0 ? null : /* @__PURE__ */ o.jsx("div", { className: "wf-expression-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const i = t.severity ?? "info";
    return /* @__PURE__ */ o.jsxs("p", { className: `wf-expression-editor-diagnostic ${i}`, children: [
      t.code ? /* @__PURE__ */ o.jsx("span", { children: t.code }) : null,
      t.message
    ] }, `${t.code ?? "diagnostic"}-${n}`);
  }) });
}
function lj(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const i = n.category?.trim() || "General";
    t.set(i, [...t.get(i) ?? [], n]);
  }
  return [...t.entries()].map(([n, i]) => ({ category: n, inputs: i }));
}
function uj(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !Xd.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function dj(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !Xd.has(t) && n !== "multiline") return !1;
  const i = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(i) || n === "singleline" || n === "multiline";
}
function fj({
  context: e,
  selectedNode: t,
  selectedNodeLabel: n,
  selectedActivityType: i,
  selectedDescriptor: r,
  selectedNodeAvailability: s,
  selectedSlots: a,
  catalogByVersion: c,
  selectedSupportsScopedVariables: u,
  propertyEditors: l,
  expressionEditors: d,
  expressionDescriptors: f,
  descriptorStatus: p,
  scopedVariableAnalysis: h,
  onSelectedActivityChange: y,
  onEnterSlot: w
}) {
  return t ? /* @__PURE__ */ o.jsxs("div", { className: "wf-inspector-content", children: [
    /* @__PURE__ */ o.jsx("h3", { children: n }),
    /* @__PURE__ */ o.jsxs("dl", { children: [
      /* @__PURE__ */ o.jsx("dt", { children: "Node ID" }),
      /* @__PURE__ */ o.jsx("dd", { children: t.nodeId }),
      /* @__PURE__ */ o.jsx("dt", { children: "Activity type" }),
      /* @__PURE__ */ o.jsx("dd", { children: i }),
      /* @__PURE__ */ o.jsx("dt", { children: "Activity version" }),
      /* @__PURE__ */ o.jsx("dd", { children: t.activityVersionId })
    ] }),
    s ? /* @__PURE__ */ o.jsxs("div", { className: "wf-availability-notice", children: [
      /* @__PURE__ */ o.jsx(zi, { size: 14 }),
      /* @__PURE__ */ o.jsxs("span", { children: [
        "No longer available for new use · ",
        Rn(s.state)
      ] })
    ] }) : null,
    /* @__PURE__ */ o.jsx(
      tj,
      {
        activity: t,
        descriptor: r,
        editors: l,
        expressionEditors: d,
        expressionDescriptors: f,
        descriptorStatus: p,
        visibleVariables: h.visibleVariables,
        scopeStatus: h.status,
        onChange: y
      }
    ),
    u ? /* @__PURE__ */ o.jsx("div", { className: "wf-container-variables", children: /* @__PURE__ */ o.jsx(
      wb,
      {
        context: e,
        variables: yl(t),
        title: "Container variables",
        addLabel: "Add container variable",
        emptyLabel: "No container variables declared on this activity.",
        warnings: _p(h.shadowingWarnings, t.nodeId),
        onChange: (x) => y(kp(t, x))
      }
    ) }) : null,
    a.length > 0 ? /* @__PURE__ */ o.jsxs("div", { className: "wf-slot-list", children: [
      /* @__PURE__ */ o.jsx("span", { children: "Embedded slots" }),
      a.map((x) => /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => w(t, x.id, `${n} / ${x.label}`), children: [
        x.label,
        /* @__PURE__ */ o.jsx("small", { children: Gb(x, c) })
      ] }, x.id))
    ] }) : /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
  ] }) : /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
}
const Zd = [
  { value: "flowchart", label: "Flowchart", hint: "Free-form graph of connected activities." },
  { value: "sequence", label: "Sequence", hint: "Ordered list of activities that run top to bottom." }
];
function Gd({ label: e, hint: t }) {
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsx("span", { className: "wf-root-card-title", children: e }),
    /* @__PURE__ */ o.jsx("span", { className: "wf-root-card-hint", children: t })
  ] });
}
function pj({ value: e, onChange: t }) {
  return /* @__PURE__ */ o.jsx("div", { className: "wf-root-cards", role: "radiogroup", "aria-label": "Root activity", children: Zd.map((n) => {
    const i = e === n.value;
    return /* @__PURE__ */ o.jsxs("label", { className: "wf-root-card", "data-checked": i || void 0, children: [
      /* @__PURE__ */ o.jsx(
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
      /* @__PURE__ */ o.jsx(Gd, { label: n.label, hint: n.hint })
    ] }, n.value);
  }) });
}
function hj({ onPick: e }) {
  return /* @__PURE__ */ o.jsx("div", { className: "wf-root-cards", role: "group", "aria-label": "Fill this slot", children: Zd.map((t) => /* @__PURE__ */ o.jsx(
    "button",
    {
      type: "button",
      className: "wf-root-card",
      "aria-label": t.label,
      onClick: () => e(t.value),
      children: /* @__PURE__ */ o.jsx(Gd, { label: t.label, hint: t.hint })
    },
    t.value
  )) });
}
function gj({ slotLabel: e, catalog: t, onPickActivity: n, onBrowseAll: i }) {
  const r = (s) => {
    const a = _d(t, s);
    a && n(a);
  };
  return /* @__PURE__ */ o.jsx("div", { className: "wf-slot-empty", role: "group", "aria-label": `Fill ${e}`, children: /* @__PURE__ */ o.jsxs("div", { className: "wf-slot-empty-card", children: [
    /* @__PURE__ */ o.jsx("p", { className: "wf-slot-empty-title", children: "This slot is empty" }),
    /* @__PURE__ */ o.jsxs("p", { className: "wf-slot-empty-hint", children: [
      "Choose a container for ",
      /* @__PURE__ */ o.jsx("strong", { children: e }),
      ", or pick any activity."
    ] }),
    /* @__PURE__ */ o.jsx(hj, { onPick: r }),
    /* @__PURE__ */ o.jsxs(
      "button",
      {
        type: "button",
        className: "wf-slot-empty-browse",
        onClick: (s) => i({ clientX: s.clientX, clientY: s.clientY }),
        children: [
          /* @__PURE__ */ o.jsx(Yn, { size: 15 }),
          " Browse all activities…"
        ]
      }
    ),
    /* @__PURE__ */ o.jsx("p", { className: "wf-slot-empty-drag-hint", children: "Tip: you can also drag activities from the palette onto the canvas." })
  ] }) });
}
function yj({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: i,
  expressionEditors: r,
  workflowDesignerPanels: s,
  autosaveEnabledByDefault: a,
  onBack: c
}) {
  const u = CN(), { draft: l, frames: d, selectedNodeId: f, testRun: p, publishedArtifactId: h } = u.state, {
    loadDraft: y,
    replaceDraftByBatch: w,
    editDraft: x,
    editDraftAndSelect: m,
    select: b,
    navigateToScope: g,
    resetToRoot: v,
    enterSlot: j,
    startTestRun: N,
    clearTestRun: S,
    setPublishedArtifact: k
  } = u, [_, M] = K(""), [I, $] = K(""), [z, C] = K("idle"), [A, E] = K(() => /* @__PURE__ */ new Set()), [D, P] = K(""), [T, B] = K("activities"), [W, H] = K("inspector"), [Y, V] = K("designer"), {
    paletteWidth: q,
    inspectorWidth: G,
    paletteCollapsed: Z,
    inspectorCollapsed: R,
    maximizedSidePanel: X,
    setInspectorCollapsed: ce,
    paletteExpanded: se,
    inspectorExpanded: ee,
    editorBodyClassName: ne,
    editorBodyStyle: de,
    toggleSidePanelCollapsed: O,
    toggleSidePanelMaximized: te,
    startSidePanelResize: ge,
    handleSidePanelResizeKeyDown: ye
  } = Wd(), { resetHistory: Ae, undo: _e, redo: $e, canUndoNow: ot, canRedoNow: Xe } = NN({ draft: l, restoreDraft: y }), { saveDraft: qe, autosaveEnabled: Pe, setAutosaveEnabled: Oe, markSaved: He } = qN({ context: e, draft: l, autosaveEnabledByDefault: a, editDraft: x, setStatus: $, setError: M }), {
    details: Ne,
    setDetails: Lt,
    catalog: De,
    activityDescriptors: hn,
    availabilityDiagnostics: L,
    expressionDescriptors: F,
    descriptorStatus: U,
    reload: ie
  } = YN({ context: e, definitionId: t, resetHistory: Ae, loadDraft: y, markSaved: He, setError: M }), { updateDefinitionMeta: le } = UN({ context: e, details: Ne, setDetails: Lt, setStatus: $ }), {
    catalogByVersion: he,
    availabilityLookup: me,
    scopeOwner: Ie,
    isUnsupportedDesigner: Ce,
    scope: Le,
    selectedNode: xe,
    selectedDescriptor: zt,
    selectedNodeAvailability: xr,
    selectedSlots: oi,
    selectedSupportsScopedVariables: wr,
    scopedVariableAnalysis: vr,
    isFlowchartDesigner: pt,
    canAddActivitiesToCanvas: br
  } = GN({ context: e, draft: l, frames: d, selectedNodeId: f, catalog: De, activityDescriptors: hn, availabilityDiagnostics: L }), Vt = ue(() => Vo(De), [De]), Nr = ue(() => {
    const J = D.trim().toLowerCase();
    if (!J) return Vt;
    const fe = De.filter((be) => Ee(be).toLowerCase().includes(J) || be.activityTypeKey.toLowerCase().includes(J) || (be.category ?? "").toLowerCase().includes(J) || (be.description ?? "").toLowerCase().includes(J));
    return Vo(fe);
  }, [De, D, Vt]), Ot = z !== "idle", jr = !!l?.state.rootActivity && !Ot, si = Pt(n, "weaver.workflows.find-draft-risks"), ai = Pt(n, "weaver.workflows.propose-update"), Sr = MN({
    draft: l,
    scope: Le,
    scopeOwner: Ie,
    frames: d,
    catalog: De,
    catalogByVersion: he,
    isUnsupportedDesigner: Ce,
    isFlowchartDesigner: pt,
    canAddActivitiesToCanvas: br,
    selectedNodeId: f,
    editDraft: x,
    editDraftAndSelect: m,
    select: b,
    resetToRoot: v,
    setStatus: $,
    setError: M
  }), {
    nodes: Ht,
    edges: Cr,
    canvasRef: Er,
    setReactFlowInstance: kr,
    connectMenu: Wt,
    setConnectMenu: ci,
    edgeActions: li,
    onNodesChange: Ir,
    onEdgesChange: Ar,
    onNodesDelete: _r,
    onEdgesDelete: Dr,
    isValidConnection: Tr,
    onConnect: $r,
    onConnectStart: ui,
    onConnectEnd: Pr,
    onReconnect: Mr,
    commitLayout: Rr,
    canAutoLayout: Lr,
    autoLayout: zr,
    onCanvasDragOver: di,
    onCanvasDragLeave: fi,
    onCanvasDrop: pi,
    openEmptyConnectMenu: gn,
    onConnectMenuPick: Vr,
    addActivity: Or,
    onPaletteClick: Hr,
    onPaletteDragStart: Wr,
    onPaletteDragEnd: Br,
    onPalettePointerDown: Fr
  } = Sr, Kr = !Ce && d.length > 0 && !!Le && Le.slot.activities.length === 0 && Ht.length === 0;
  XN({ draft: l, details: Ne, catalog: De, replaceDraftByBatch: w, setStatus: $, setError: M }), Q(() => {
    !l?.state.rootActivity || De.length === 0 || x(({ draft: J }) => {
      if (!J?.state.rootActivity) return null;
      const fe = ul(J.state.rootActivity, he);
      return !fe || fe === J.state.rootActivity ? null : {
        ...J,
        state: {
          ...J.state,
          rootActivity: fe
        }
      };
    });
  }, [De.length, he, l?.state.rootActivity, x]), JN({ details: Ne, draft: l, selectedNode: xe, selectedNodeId: f, selectedDescriptor: zt, catalogByVersion: he }), Q(() => {
    E((J) => {
      let fe = !1;
      const be = new Set(J);
      for (const st of Vt)
        be.has(st.category) || (be.add(st.category), fe = !0);
      return fe ? be : J;
    });
  }, [Vt]);
  const { exportJson: Xr, save: hi, promoteAndPublish: qr, run: gi } = ZN({
    context: e,
    draft: l,
    details: Ne,
    busy: Ot,
    saveDraft: qe,
    reload: ie,
    startTestRun: N,
    clearTestRun: S,
    setPublishedArtifact: k,
    setOperation: C,
    setStatus: $,
    setError: M,
    setActiveRightPanelId: H,
    setInspectorCollapsed: ce
  }), Yr = ae((J) => {
    x(({ draft: fe }) => fe ? { ...fe, state: J(fe.state) } : null);
  }, [x]), Ur = ae((J) => {
    if (!l) return "No draft is loaded.";
    const fe = eb(J, l);
    return fe.ok ? (y(fe.draft), $("Applied workflow JSON."), null) : fe.error;
  }, [l, y]);
  Q(() => {
    const J = (fe) => {
      if (Y !== "designer" || !(fe.metaKey || fe.ctrlKey)) return;
      const be = fe.target;
      if (be && (be.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(be.tagName))) return;
      const st = fe.key.toLowerCase();
      st === "z" && !fe.shiftKey ? (fe.preventDefault(), _e()) : (st === "z" && fe.shiftKey || st === "y") && (fe.preventDefault(), $e());
    };
    return window.addEventListener("keydown", J), () => window.removeEventListener("keydown", J);
  }, [Y, _e, $e]);
  const yn = ae((J) => {
    x(({ draft: fe }) => {
      const be = fe?.state.rootActivity;
      return !fe || !be ? null : {
        ...fe,
        state: {
          ...fe.state,
          rootActivity: ll(be, J.nodeId, () => J, he)
        }
      };
    });
  }, [he, x]), Zr = ae((J) => {
    if (!J) return;
    const fe = l?.state.rootActivity;
    if (!fe) return;
    const be = Zf(fe, J, (st) => {
      const Rs = he.get(st.activityVersionId);
      return Rs ? Ee(Rs) : st.nodeId;
    }, he);
    be && (V("designer"), g(be, J), ce(!1));
  }, [l?.state.rootActivity, he, ce, g]), Gr = (J) => {
    E((fe) => {
      const be = new Set(fe);
      return be.has(J) ? be.delete(J) : be.add(J), be;
    });
  };
  if (!Ne || !l)
    return /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: _ || "Loading workflow editor..." });
  const bt = p?.draftSignature === Be(l) ? p.view : null, Ts = bt && I.startsWith("Test run") ? "" : I, Jd = (J) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(J)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, Qd = {
    definition: Ne.definition,
    draft: l,
    selectedActivity: xe,
    selectedActivityDescriptor: zt,
    selectedActivitySlots: oi,
    catalog: De,
    currentScopeOwner: Ie,
    frames: d
  }, $s = s.map((J) => {
    const fe = J.component;
    return {
      id: J.id,
      title: J.title,
      side: J.side,
      order: J.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ o.jsx(fe, { context: Qd })
    };
  }), Jr = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ o.jsx(Yn, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(
        QN,
        {
          paletteSearch: D,
          onSearchChange: P,
          groups: Nr,
          expandedCategories: A,
          onToggleCategory: Gr,
          onActivityClick: Hr,
          onActivityDragStart: Wr,
          onActivityDragEnd: Br,
          onActivityPointerDown: Fr
        }
      )
    },
    ...$s.filter((J) => J.side === "left")
  ].sort(Dc), Qr = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ o.jsx(qo, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(
        fj,
        {
          context: e,
          selectedNode: xe,
          selectedNodeLabel: xe ? Ht.find((J) => J.id === xe.nodeId)?.data.label ?? xe.nodeId : "",
          selectedActivityType: xe ? zt?.typeName ?? he.get(xe.activityVersionId)?.activityTypeKey ?? "Unknown" : "",
          selectedDescriptor: zt,
          selectedNodeAvailability: xr,
          selectedSlots: oi,
          catalogByVersion: he,
          selectedSupportsScopedVariables: wr,
          propertyEditors: i,
          expressionEditors: r,
          expressionDescriptors: F,
          descriptorStatus: U,
          scopedVariableAnalysis: vr,
          onSelectedActivityChange: yn,
          onEnterSlot: j
        }
      )
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: /* @__PURE__ */ o.jsx(Jt, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(pN, { testRun: bt, onOpenRun: Jd })
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ o.jsx(Uc, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(
        yN,
        {
          context: e,
          ai: n,
          definitionId: Ne.definition.id,
          publishedArtifactId: h
        }
      )
    },
    ...$s.filter((J) => J.side === "right")
  ].sort(Dc), Ps = Jr.find((J) => J.id === T) ?? Jr[0], Ms = Qr.find((J) => J.id === W) ?? Qr[0], ef = [
    { id: "designer", title: "Designer", order: 0, icon: /* @__PURE__ */ o.jsx(Zc, { size: 14 }), render: () => null },
    { id: "code", title: "Code", order: 1, icon: /* @__PURE__ */ o.jsx(yf, { size: 14 }), render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: /* @__PURE__ */ o.jsx(Yo, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-link-button", onClick: c, children: "Definitions" }),
      /* @__PURE__ */ o.jsx(mt, { size: 14 }),
      /* @__PURE__ */ o.jsx("strong", { children: Ne.definition.name }),
      /* @__PURE__ */ o.jsx("span", { className: "wf-chip", children: "Draft" }),
      Ts ? /* @__PURE__ */ o.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ o.jsx(ln, { size: 13 }),
        " ",
        Ts
      ] }) : null,
      /* @__PURE__ */ o.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ o.jsxs("div", { className: "wf-canvas-tools", role: "group", "aria-label": "Canvas tools", children: [
          /* @__PURE__ */ o.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Undo",
              title: "Undo (Ctrl+Z)",
              disabled: !ot,
              onClick: _e,
              children: /* @__PURE__ */ o.jsx(ff, { size: 16 })
            }
          ),
          /* @__PURE__ */ o.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Redo",
              title: "Redo (Ctrl+Shift+Z)",
              disabled: !Xe,
              onClick: $e,
              children: /* @__PURE__ */ o.jsx(pf, { size: 16 })
            }
          ),
          /* @__PURE__ */ o.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Auto-layout",
              title: "Auto-layout the canvas",
              disabled: !Lr,
              onClick: zr,
              children: /* @__PURE__ */ o.jsx(hf, { size: 16 })
            }
          )
        ] }),
        /* @__PURE__ */ o.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ o.jsx("input", { className: "wf-switch-input", type: "checkbox", checked: Pe, onChange: (J) => Oe(J.target.checked) }),
          /* @__PURE__ */ o.jsx("span", { children: "Autosave" })
        ] }),
        si ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Mt(n, si, { definition: Ne.definition, draft: l }), children: [
          /* @__PURE__ */ o.jsx(ut, { size: 15 }),
          " Risks"
        ] }) : null,
        ai ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Mt(n, ai, { definition: Ne.definition, draft: l }), children: [
          /* @__PURE__ */ o.jsx(ut, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ o.jsxs("button", { type: "button", title: "Export workflow as JSON", onClick: Xr, children: [
          /* @__PURE__ */ o.jsx(gf, { size: 15 }),
          " Export"
        ] }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", disabled: Ot, onClick: () => {
          hi();
        }, children: [
          /* @__PURE__ */ o.jsx(Kc, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", disabled: Ot, onClick: () => {
          qr();
        }, children: [
          /* @__PURE__ */ o.jsx(Fc, { size: 15 }),
          " Promote"
        ] }),
        bt ? /* @__PURE__ */ o.jsx(
          fN,
          {
            testRun: bt,
            onOpenDetails: () => {
              H("runtime"), ce(!1);
            }
          }
        ) : null,
        /* @__PURE__ */ o.jsxs(
          "button",
          {
            type: "button",
            disabled: !jr,
            title: l.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              gi();
            },
            children: [
              /* @__PURE__ */ o.jsx(Jt, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    _ ? /* @__PURE__ */ o.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ o.jsx(vt, { size: 16 }),
      " ",
      _
    ] }) : null,
    /* @__PURE__ */ o.jsxs("div", { className: ne, style: de, children: [
      /* @__PURE__ */ o.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ o.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ o.jsx(
            Li,
            {
              label: "Activities panel tabs",
              tabs: Jr,
              activeTabId: Ps.id,
              onSelect: B
            }
          ),
          /* @__PURE__ */ o.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": Z ? "Expand activities panel" : "Collapse activities panel",
                title: Z ? "Expand" : "Collapse",
                onClick: () => O("palette"),
                children: Z ? /* @__PURE__ */ o.jsx(mt, { size: 14 }) : /* @__PURE__ */ o.jsx($n, { size: 14 })
              }
            ),
            Z ? null : /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": X === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: X === "palette" ? "Restore" : "Maximize",
                onClick: () => te("palette"),
                children: X === "palette" ? /* @__PURE__ */ o.jsx(bo, { size: 14 }) : /* @__PURE__ */ o.jsx(Tn, { size: 14 })
              }
            )
          ] })
        ] }),
        se ? Ps.render() : null
      ] }),
      se && !X ? /* @__PURE__ */ o.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Cn,
          "aria-valuemax": En,
          "aria-valuenow": q,
          tabIndex: 0,
          onPointerDown: (J) => ge("palette", J),
          onKeyDown: (J) => ye("palette", J)
        }
      ) : /* @__PURE__ */ o.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ o.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ o.jsx("div", { className: "wf-canvas-tabs", children: /* @__PURE__ */ o.jsx(
          Li,
          {
            label: "Editor view tabs",
            tabs: ef,
            activeTabId: Y,
            onSelect: (J) => V(J)
          }
        ) }),
        Y === "code" ? /* @__PURE__ */ o.jsx(sb, { draft: l, onApply: Ur }) : Y === "properties" ? /* @__PURE__ */ o.jsx(bb, { details: Ne, draft: l, context: e, onStateChange: Yr, onDefinitionMetaChange: le }) : /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
          /* @__PURE__ */ o.jsxs("div", { className: "wf-breadcrumb", children: [
            /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => v(), children: "Root" }),
            d.map((J, fe) => /* @__PURE__ */ o.jsxs(Je.Fragment, { children: [
              /* @__PURE__ */ o.jsx(mt, { size: 13 }),
              /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => g(d.slice(0, fe + 1), null), children: J.label })
            ] }, `${J.ownerNodeId}-${J.slotId}-${fe}`))
          ] }),
          /* @__PURE__ */ o.jsxs("div", { className: "wf-canvas", ref: Er, onDragOver: di, onDragLeave: fi, onDrop: pi, children: [
            /* @__PURE__ */ o.jsx(kd.Provider, { value: li, children: /* @__PURE__ */ o.jsx(Id.Provider, { value: me, children: /* @__PURE__ */ o.jsxs(
              md,
              {
                nodes: Ht,
                edges: Cr,
                nodeTypes: Vd,
                edgeTypes: Od,
                onInit: kr,
                onNodesChange: Ir,
                onEdgesChange: Ar,
                onNodesDelete: _r,
                onEdgesDelete: Dr,
                onConnect: $r,
                onConnectStart: pt ? ui : void 0,
                onConnectEnd: pt ? Pr : void 0,
                onReconnect: pt ? Mr : void 0,
                isValidConnection: Tr,
                onDragOver: di,
                onDragLeave: fi,
                onDrop: pi,
                onPaneClick: () => b(null),
                onNodeClick: (J, fe) => b(fe.id),
                onNodeDragStop: Ce ? void 0 : Rr,
                minZoom: 0.2,
                maxZoom: 1.8,
                nodesConnectable: pt,
                nodesDraggable: !Ce,
                selectionOnDrag: !0,
                multiSelectionKeyCode: ["Shift", "Meta", "Control"],
                deleteKeyCode: Ce ? null : ["Backspace", "Delete"],
                panActivationKeyCode: null,
                defaultEdgeOptions: { type: "workflow" },
                children: [
                  /* @__PURE__ */ o.jsx(wd, { gap: 18, size: 1 }),
                  /* @__PURE__ */ o.jsx(bd, {}),
                  /* @__PURE__ */ o.jsx(jd, { pannable: !0, zoomable: !0 })
                ]
              }
            ) }) }),
            Kr ? /* @__PURE__ */ o.jsx(
              gj,
              {
                slotLabel: Le?.slot.label ?? "this slot",
                catalog: De,
                onPickActivity: Or,
                onBrowseAll: gn
              }
            ) : pt && Ht.length === 0 ? /* @__PURE__ */ o.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => gn(), children: [
              /* @__PURE__ */ o.jsx(Qt, { size: 15 }),
              " Add activity"
            ] }) : null,
            Wt ? /* @__PURE__ */ o.jsx(
              oN,
              {
                clientX: Wt.clientX,
                clientY: Wt.clientY,
                activities: De,
                onPick: Vr,
                onClose: () => ci(null)
              }
            ) : null
          ] }),
          /* @__PURE__ */ o.jsx(dN, { draft: l, onRepair: Zr })
        ] })
      ] }),
      ee && !X ? /* @__PURE__ */ o.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": qt,
          "aria-valuemax": Yt,
          "aria-valuenow": G,
          tabIndex: 0,
          onPointerDown: (J) => ge("inspector", J),
          onKeyDown: (J) => ye("inspector", J)
        }
      ) : /* @__PURE__ */ o.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ o.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ o.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ o.jsx(
            Li,
            {
              label: "Inspector panel tabs",
              tabs: Qr,
              activeTabId: Ms.id,
              onSelect: H
            }
          ),
          /* @__PURE__ */ o.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": R ? "Expand inspector panel" : "Collapse inspector panel",
                title: R ? "Expand" : "Collapse",
                onClick: () => O("inspector"),
                children: R ? /* @__PURE__ */ o.jsx($n, { size: 14 }) : /* @__PURE__ */ o.jsx(mt, { size: 14 })
              }
            ),
            R ? null : /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": X === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: X === "inspector" ? "Restore" : "Maximize",
                onClick: () => te("inspector"),
                children: X === "inspector" ? /* @__PURE__ */ o.jsx(bo, { size: 14 }) : /* @__PURE__ */ o.jsx(Tn, { size: 14 })
              }
            )
          ] })
        ] }),
        ee ? Ms.render() : null
      ] })
    ] })
  ] });
}
function mj({ page: e, pageSize: t, totalCount: n, onPageChange: i, onPageSizeChange: r }) {
  const s = Ad(n, t), a = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-pagination", "aria-label": "Workflow definition pagination", children: [
    /* @__PURE__ */ o.jsxs("span", { className: "wf-pagination-summary", "aria-live": "polite", children: [
      "Showing ",
      a,
      "-",
      c,
      " of ",
      n
    ] }),
    /* @__PURE__ */ o.jsxs("label", { className: "wf-page-size", children: [
      "Rows",
      /* @__PURE__ */ o.jsx("select", { value: t, onChange: (u) => r(Number(u.target.value)), children: Cb.map((u) => /* @__PURE__ */ o.jsx("option", { value: u, children: u }, u)) })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => i(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ o.jsx($n, { size: 14 }),
        " Previous"
      ] }),
      /* @__PURE__ */ o.jsxs("span", { children: [
        "Page ",
        e,
        " of ",
        s
      ] }),
      /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => i(e + 1), disabled: e >= s, "aria-label": "Next page", title: "Next page", children: [
        "Next ",
        /* @__PURE__ */ o.jsx(mt, { size: 14 })
      ] })
    ] })
  ] });
}
function xj({ draft: e, creating: t, ai: n, suggestMetadataAction: i, onChange: r, onClose: s, onSubmit: a }) {
  const [c, u] = K(!1), [l, d] = K(""), [f, p] = K(!1), [h, y] = K(null), [w, x] = K(null), m = re(null), b = re(e);
  b.current = e;
  const g = re(r);
  g.current = r;
  const v = ae((N) => {
    const S = { ...b.current };
    N.name && (S.name = N.name), N.description && (S.description = N.description), g.current(S), y(null), x(null);
  }, []);
  Q(() => {
    if (i)
      return n.onPromptResult((N) => {
        if (N.requestId !== m.current) return;
        if (m.current = null, p(!1), N.status !== "completed") {
          x(N.status === "cancelled" ? "Weaver needs more detail — continue in the assistant panel." : "Weaver couldn't generate a suggestion. Try again or fill the fields manually.");
          return;
        }
        const S = _b(N.text);
        if (!S) {
          x("Couldn't read a suggestion from Weaver's reply. See the assistant panel.");
          return;
        }
        N.autoApply ? v(S) : y(S);
      });
  }, [n, i, v]);
  const j = () => {
    if (!i) return;
    const N = i.createPrompt({ draft: b.current, intent: l });
    if (!N) return;
    const S = `wf-suggest-${Date.now()}-${Math.round(Math.random() * 1e6)}`;
    m.current = S, p(!0), y(null), x(null), n.dispatchPrompt({ ...N, requestId: S });
  };
  return /* @__PURE__ */ o.jsx("div", { className: "wf-dialog-backdrop", role: "presentation", children: /* @__PURE__ */ o.jsx("section", { className: "wf-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "workflow-create-title", children: /* @__PURE__ */ o.jsxs(
    "form",
    {
      onSubmit: (N) => {
        N.preventDefault(), a();
      },
      children: [
        /* @__PURE__ */ o.jsxs("div", { className: "wf-dialog-heading", children: [
          /* @__PURE__ */ o.jsx("h3", { id: "workflow-create-title", children: "Create Workflow" }),
          i ? /* @__PURE__ */ o.jsxs(
            "button",
            {
              type: "button",
              className: "wf-ai-action",
              "aria-expanded": c,
              onClick: () => u((N) => !N),
              title: i.description ?? i.label,
              children: [
                /* @__PURE__ */ o.jsx(ut, { size: 13 }),
                " ",
                i.label
              ]
            }
          ) : null
        ] }),
        i && c ? /* @__PURE__ */ o.jsxs("div", { className: "wf-ai-suggest", role: "group", "aria-label": "Suggest name and description", children: [
          /* @__PURE__ */ o.jsxs("label", { className: "wf-form-field", children: [
            /* @__PURE__ */ o.jsx("span", { children: "What should this workflow do?" }),
            /* @__PURE__ */ o.jsx(
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
          /* @__PURE__ */ o.jsx("div", { className: "wf-ai-suggest-actions", children: /* @__PURE__ */ o.jsxs("button", { type: "button", className: "wf-ai-action", onClick: j, disabled: f, children: [
            /* @__PURE__ */ o.jsx(ut, { size: 13 }),
            " ",
            f ? "Generating…" : "Generate"
          ] }) }),
          w ? /* @__PURE__ */ o.jsx("p", { className: "wf-ai-suggest-error", role: "alert", children: w }) : null,
          h ? /* @__PURE__ */ o.jsxs("div", { className: "wf-ai-suggest-preview", children: [
            h.name ? /* @__PURE__ */ o.jsx("p", { children: /* @__PURE__ */ o.jsx("strong", { children: h.name }) }) : null,
            h.description ? /* @__PURE__ */ o.jsx("p", { children: h.description }) : null,
            /* @__PURE__ */ o.jsxs("div", { className: "wf-ai-suggest-actions", children: [
              /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => v(h), children: "Apply" }),
              /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => y(null), children: "Dismiss" })
            ] })
          ] }) : null
        ] }) : null,
        /* @__PURE__ */ o.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ o.jsx("span", { children: "Display name" }),
          /* @__PURE__ */ o.jsx(
            "input",
            {
              autoFocus: !0,
              "aria-label": "Display name",
              value: e.name,
              onChange: (N) => r({ ...e, name: N.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ o.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ o.jsx("span", { children: "Description" }),
          /* @__PURE__ */ o.jsx(
            "textarea",
            {
              "aria-label": "Description",
              rows: 3,
              value: e.description,
              onChange: (N) => r({ ...e, description: N.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ o.jsxs("fieldset", { className: "wf-form-field wf-root-field", children: [
          /* @__PURE__ */ o.jsx("legend", { children: "Root activity" }),
          /* @__PURE__ */ o.jsx(
            pj,
            {
              value: e.rootKind,
              onChange: (N) => r({ ...e, rootKind: N, rootActivityVersionId: null })
            }
          )
        ] }),
        /* @__PURE__ */ o.jsxs("div", { className: "wf-dialog-actions", children: [
          /* @__PURE__ */ o.jsx("button", { type: "button", onClick: s, disabled: t, children: "Cancel" }),
          /* @__PURE__ */ o.jsx("button", { type: "submit", disabled: t || !e.name.trim(), children: t ? "Creating..." : "Create" })
        ] })
      ]
    }
  ) }) });
}
function wj({ context: e, ai: t, onOpen: n }) {
  const [i, r] = K(""), [s, a] = K("active"), [c, u] = K(1), [l, d] = K(Eb), [f, p] = K("loading"), [h, y] = K(""), [w, x] = K(""), [m, b] = K([]), [g, v] = K(0), [j, N] = K(() => /* @__PURE__ */ new Set()), [S, k] = K(null), [_, M] = K(!1), [I, $] = K([]), [z, C] = K("idle"), A = re(null), E = ue(() => m.map((O) => O.id), [m]), D = Pt(t, "weaver.workflows.suggest-create-metadata"), P = Pt(t, "weaver.workflows.explain-definition"), T = E.filter((O) => j.has(O)).length, B = E.length > 0 && T === E.length, W = ae(async () => {
    p("loading"), y("");
    try {
      const O = await Rp(e, { search: i, state: s, page: c, pageSize: l }), te = typeof O.totalCount == "number", ge = O.totalCount ?? O.definitions.length, ye = Ad(ge, l);
      if (ge > 0 && c > ye) {
        u(ye);
        return;
      }
      b(te ? O.definitions : Ab(O.definitions, c, l)), v(ge), p("ready");
    } catch (O) {
      y(O instanceof Error ? O.message : String(O)), p("failed");
    }
  }, [e, i, s, c, l]);
  Q(() => {
    W();
  }, [W]), Q(() => {
    A.current && (A.current.indeterminate = T > 0 && !B);
  }, [B, T]);
  const H = ae(async () => {
    if (!(z === "loading" || z === "ready")) {
      C("loading");
      try {
        const O = await nr(e);
        $(O.activities ?? []), C("ready");
      } catch (O) {
        C("failed"), y(O instanceof Error ? O.message : String(O));
      }
    }
  }, [z, e]), Y = () => {
    y(""), x(""), k({ name: "", description: "", rootKind: "flowchart" }), H();
  }, V = async () => {
    if (S?.name.trim()) {
      M(!0), y(""), x("");
      try {
        const O = await Hp(e, {
          name: S.name.trim(),
          description: S.description.trim() || null,
          rootKind: S.rootKind,
          rootActivityVersionId: Db(S, I)
        });
        k(null), n(O.definition.id);
      } catch (O) {
        y(O instanceof Error ? O.message : String(O));
      } finally {
        M(!1);
      }
    }
  }, q = (O) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(O)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, G = async () => {
    if (m.length === 1 && c > 1) {
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
  }, ce = (O) => {
    a(O), u(1), Z();
  }, se = (O) => {
    r(O), u(1), Z();
  }, ee = async (O) => {
    if (await Os().confirm({ message: `Delete workflow definition "${O.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" })) {
      x(""), y("");
      try {
        await Wp(e, O.id), R(O.id, !1), x(`Deleted ${O.name}`), await G();
      } catch (te) {
        y(te instanceof Error ? te.message : String(te));
      }
    }
  }, ne = async (O) => {
    x(""), y("");
    try {
      await Bp(e, O.id), R(O.id, !1), x(`Restored ${O.name}`), await G();
    } catch (te) {
      y(te instanceof Error ? te.message : String(te));
    }
  }, de = async (O) => {
    if (await Os().confirm({ message: `Permanently delete workflow definition "${O.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) {
      x(""), y("");
      try {
        await Fp(e, O.id), R(O.id, !1), x(`Permanently deleted ${O.name}`), await G();
      } catch (te) {
        y(te instanceof Error ? te.message : String(te));
      }
    }
  };
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ o.jsx("button", { type: "button", className: s === "active" ? "active" : "", "aria-selected": s === "active", onClick: () => ce("active"), children: "Active" }),
        /* @__PURE__ */ o.jsx("button", { type: "button", className: s === "deleted" ? "active" : "", "aria-selected": s === "deleted", onClick: () => ce("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ o.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ o.jsx(Qi, { size: 15 }),
        /* @__PURE__ */ o.jsx("input", { value: i, onChange: (O) => se(O.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => {
        W();
      }, children: "Refresh" }),
      /* @__PURE__ */ o.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ o.jsxs("button", { type: "button", title: "Create workflow", onClick: Y, children: [
        /* @__PURE__ */ o.jsx(Qt, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ o.jsx(ri, { message: h, title: "Couldn't load workflow definitions" }) : null,
    f !== "failed" && h ? /* @__PURE__ */ o.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ o.jsx(vt, { size: 16 }),
      " ",
      h
    ] }) : null,
    w ? /* @__PURE__ */ o.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ o.jsx(ln, { size: 14 }),
      " ",
      w
    ] }) : null,
    j.size > 0 ? /* @__PURE__ */ o.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ o.jsxs("span", { children: [
        j.size,
        " selected"
      ] }),
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: Z, children: "Clear selection" })
    ] }) : null,
    f === "loading" ? /* @__PURE__ */ o.jsx(Is, {}) : null,
    f === "ready" && m.length === 0 ? /* @__PURE__ */ o.jsx(
      As,
      {
        icon: /* @__PURE__ */ o.jsx(Uc, { size: 22 }),
        title: `No ${s} workflow definitions`,
        description: "Create a workflow to start designing automation, or adjust your filters to see more.",
        action: /* @__PURE__ */ o.jsxs("button", { type: "button", className: "wf-link-button", onClick: Y, children: [
          /* @__PURE__ */ o.jsx(Qt, { size: 15 }),
          " Create workflow"
        ] })
      }
    ) : null,
    f === "ready" && m.length > 0 ? /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ o.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ o.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ o.jsx(
            "input",
            {
              ref: A,
              type: "checkbox",
              checked: B,
              onChange: (O) => X(O.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ o.jsx("span", { children: "Name" }),
          /* @__PURE__ */ o.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ o.jsx("span", { children: s === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ o.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ o.jsx("span", { children: "Actions" })
        ] }),
        m.map((O) => /* @__PURE__ */ o.jsxs(
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
              /* @__PURE__ */ o.jsx("label", { className: "wf-row-select", onClick: (te) => te.stopPropagation(), children: /* @__PURE__ */ o.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: j.has(O.id),
                  onChange: (te) => R(O.id, te.target.checked),
                  "aria-label": `Select workflow definition ${O.name}`
                }
              ) }),
              /* @__PURE__ */ o.jsxs("span", { children: [
                /* @__PURE__ */ o.jsx("strong", { children: O.name }),
                /* @__PURE__ */ o.jsx("small", { children: O.description || O.id })
              ] }),
              /* @__PURE__ */ o.jsx("span", { children: O.latestVersion ?? "No version" }),
              /* @__PURE__ */ o.jsx("span", { children: s === "deleted" ? Me(O.deletedAt) : O.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ o.jsx("span", { children: Me(O.lastModifiedAt) }),
              /* @__PURE__ */ o.jsx("span", { className: "wf-row-actions", onClick: (te) => te.stopPropagation(), children: s === "active" ? /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
                /* @__PURE__ */ o.jsx("button", { type: "button", onClick: (te) => {
                  te.stopPropagation(), n(O.id);
                }, children: "Open" }),
                /* @__PURE__ */ o.jsx("button", { type: "button", onClick: (te) => {
                  te.stopPropagation(), q(O.id);
                }, children: "Artifacts" }),
                P ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Mt(t, P, O), children: [
                  /* @__PURE__ */ o.jsx(ut, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ o.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  ee(O);
                }, children: [
                  /* @__PURE__ */ o.jsx(Dn, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
                /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
                  ne(O);
                }, children: [
                  /* @__PURE__ */ o.jsx(Zo, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ o.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  de(O);
                }, children: [
                  /* @__PURE__ */ o.jsx(Dn, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          O.id
        ))
      ] }),
      /* @__PURE__ */ o.jsx(
        mj,
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
    S ? /* @__PURE__ */ o.jsx(
      xj,
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
function vj({ activities: e, activityCatalog: t, selectedEvidenceId: n = null, onSelectEvidence: i }) {
  const r = ue(
    () => new Map(t.map((a) => [a.activityTypeKey, a])),
    [t]
  ), s = ue(() => Nj(e), [e]);
  return s.length === 0 ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty wf-timeline-empty", children: "No activity executions recorded yet." }) : /* @__PURE__ */ o.jsx("ol", { className: "wf-timeline", "aria-label": "Execution timeline", children: s.map((a) => {
    const c = r.get(a.activityType), u = dn(c), l = c ? Ee(c) : It(a.activityType) ?? a.activityType, d = It(a.activityType) ?? a.activityType, f = jj(a.startedAt ?? a.scheduledAt), p = ss(a.startedAt, a.completedAt);
    return /* @__PURE__ */ o.jsx("li", { children: /* @__PURE__ */ o.jsxs(
      "button",
      {
        type: "button",
        className: "wf-timeline-entry",
        "data-selected": a.activityExecutionId === n,
        onClick: () => i?.(a.activityExecutionId),
        children: [
          /* @__PURE__ */ o.jsx("span", { className: "wf-timeline-icon wf-activity-icon", "data-icon": u, "aria-hidden": "true", children: rr(u) }),
          /* @__PURE__ */ o.jsxs("span", { className: "wf-timeline-body", children: [
            /* @__PURE__ */ o.jsx("strong", { title: l, children: l }),
            /* @__PURE__ */ o.jsx("small", { title: d, children: d })
          ] }),
          /* @__PURE__ */ o.jsxs("span", { className: "wf-timeline-meta", children: [
            f ? /* @__PURE__ */ o.jsx("time", { children: f }) : null,
            p ? /* @__PURE__ */ o.jsxs("small", { children: [
              "took ",
              p
            ] }) : null
          ] }),
          /* @__PURE__ */ o.jsx(bj, { status: a.status })
        ]
      }
    ) }, a.activityExecutionId);
  }) });
}
function bj({ status: e }) {
  const t = e.toLowerCase(), n = t === "completed" ? "OK" : e;
  return /* @__PURE__ */ o.jsx("span", { className: "wf-status-badge wf-timeline-pill", "data-status": t, children: n });
}
function Nj(e) {
  return e.map((t, n) => ({ activity: t, index: n })).sort((t, n) => Vc(t.activity) - Vc(n.activity) || t.index - n.index).map((t) => t.activity);
}
function Vc(e) {
  const t = e.startedAt ?? e.scheduledAt, n = t ? Date.parse(t) : Number.NaN;
  return Number.isNaN(n) ? Number.MAX_SAFE_INTEGER : n;
}
function jj(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? "" : t.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: !1 });
}
function Sj({ context: e }) {
  const [t, n] = K("loading"), [i, r] = K(""), [s, a] = K(""), [c, u] = K(""), [l, d] = K([]), f = ae(async () => {
    n("loading"), r("");
    try {
      const h = await Gp(e, {
        status: s || void 0,
        runKind: c || void 0,
        take: 100
      });
      d(h), n("ready");
    } catch (h) {
      r(h instanceof Error ? h.message : String(h)), d([]), n("failed");
    }
  }, [e, c, s]);
  Q(() => {
    f();
  }, [f]);
  const p = (h) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(h)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => {
        f();
      }, children: "Refresh" }),
      /* @__PURE__ */ o.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ o.jsx("span", { children: "Status" }),
        /* @__PURE__ */ o.jsxs("select", { "aria-label": "Workflow run status", value: s, onChange: (h) => a(h.target.value), children: [
          /* @__PURE__ */ o.jsx("option", { value: "", children: "All statuses" }),
          /* @__PURE__ */ o.jsx("option", { value: "Pending", children: "Pending" }),
          /* @__PURE__ */ o.jsx("option", { value: "Running", children: "Running" }),
          /* @__PURE__ */ o.jsx("option", { value: "Suspended", children: "Suspended" }),
          /* @__PURE__ */ o.jsx("option", { value: "Completed", children: "Completed" }),
          /* @__PURE__ */ o.jsx("option", { value: "Faulted", children: "Faulted" }),
          /* @__PURE__ */ o.jsx("option", { value: "Cancelled", children: "Cancelled" })
        ] })
      ] }),
      /* @__PURE__ */ o.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ o.jsx("span", { children: "Kind" }),
        /* @__PURE__ */ o.jsxs("select", { "aria-label": "Run Kind", value: c, onChange: (h) => u(h.target.value), children: [
          /* @__PURE__ */ o.jsx("option", { value: "", children: "All kinds" }),
          /* @__PURE__ */ o.jsx("option", { value: "TestRun", children: "Test Run" }),
          /* @__PURE__ */ o.jsx("option", { value: "PublishedRun", children: "Published Run" }),
          /* @__PURE__ */ o.jsx("option", { value: "BackgroundWeaverRun", children: "Background Weaver Run" }),
          /* @__PURE__ */ o.jsx("option", { value: "Unknown", children: "Unknown / legacy" })
        ] })
      ] })
    ] }),
    t === "failed" ? /* @__PURE__ */ o.jsx(ri, { message: i }) : null,
    t === "loading" ? /* @__PURE__ */ o.jsx(Is, {}) : null,
    t === "ready" && l.length === 0 ? /* @__PURE__ */ o.jsx(
      As,
      {
        icon: /* @__PURE__ */ o.jsx(Yn, { size: 22 }),
        title: "No workflow runs yet",
        description: "Run a published workflow executable to create execution history here."
      }
    ) : null,
    t === "ready" && l.length > 0 ? /* @__PURE__ */ o.jsxs("div", { className: "wf-grid wf-instance-grid", role: "table", "aria-label": "Workflow runs", children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ o.jsx("span", { children: "Run" }),
        /* @__PURE__ */ o.jsx("span", { children: "Kind" }),
        /* @__PURE__ */ o.jsx("span", { children: "Status" }),
        /* @__PURE__ */ o.jsx("span", { children: "Definition" }),
        /* @__PURE__ */ o.jsx("span", { children: "Activity" }),
        /* @__PURE__ */ o.jsx("span", { children: "Started" }),
        /* @__PURE__ */ o.jsx("span", { children: "Duration" })
      ] }),
      l.map((h) => /* @__PURE__ */ o.jsxs(
        "button",
        {
          type: "button",
          className: "wf-grid-row",
          role: "row",
          "aria-label": `Inspect workflow run ${h.workflowExecutionId}`,
          onClick: () => p(h.workflowExecutionId),
          children: [
            /* @__PURE__ */ o.jsxs("span", { children: [
              /* @__PURE__ */ o.jsx("strong", { children: h.workflowExecutionId }),
              /* @__PURE__ */ o.jsx("small", { children: h.artifactId })
            ] }),
            /* @__PURE__ */ o.jsx("span", { children: Pd(h.runKind) }),
            /* @__PURE__ */ o.jsx("span", { children: /* @__PURE__ */ o.jsx(pn, { status: h.status, subStatus: h.subStatus }) }),
            /* @__PURE__ */ o.jsxs("span", { children: [
              /* @__PURE__ */ o.jsx("strong", { children: h.definitionId }),
              /* @__PURE__ */ o.jsx("small", { children: h.definitionVersionId })
            ] }),
            /* @__PURE__ */ o.jsxs("span", { children: [
              /* @__PURE__ */ o.jsxs("strong", { children: [
                h.activityCount,
                " activities"
              ] }),
              /* @__PURE__ */ o.jsxs("small", { children: [
                h.incidentCount,
                " incidents"
              ] })
            ] }),
            /* @__PURE__ */ o.jsx("span", { children: Me(h.startedAt ?? h.createdAt) }),
            /* @__PURE__ */ o.jsx("span", { children: ss(h.startedAt ?? h.createdAt, h.completedAt ?? h.updatedAt) })
          ]
        },
        h.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function Cj({ context: e, ai: t, workflowExecutionId: n }) {
  const [i, r] = K("loading"), [s, a] = K(""), [c, u] = K(null), [l, d] = K(null), [f, p] = K([]), {
    inspectorWidth: h,
    inspectorCollapsed: y,
    maximizedSidePanel: w,
    inspectorExpanded: x,
    editorBodyStyle: m,
    toggleSidePanelCollapsed: b,
    toggleSidePanelMaximized: g,
    startSidePanelResize: v,
    handleSidePanelResizeKeyDown: j
  } = Wd(), N = Pt(t, "weaver.workflows.explain-instance"), S = ae(async () => {
    if (!n) {
      a("No workflow execution id was provided."), r("failed");
      return;
    }
    r("loading"), a("");
    try {
      const I = await Jp(e, n), [$, z] = await Promise.all([
        Op(e, I.instance.definitionVersionId).then(
          (C) => ({ definitionVersion: C, error: "" }),
          (C) => ({ definitionVersion: null, error: C instanceof Error ? C.message : String(C) })
        ),
        nr(e)
      ]);
      u({
        details: I,
        definitionVersion: $.definitionVersion,
        definitionVersionError: $.error,
        activityCatalog: z.activities
      }), d(null), p([]), r("ready");
    } catch (I) {
      u(null), a(eN(I, n)), r("failed");
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
    w === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: k, children: [
        /* @__PURE__ */ o.jsx($n, { size: 14 }),
        " Runs"
      ] }),
      c?.details.instance.definitionId ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: _, children: [
        /* @__PURE__ */ o.jsx(Zc, { size: 14 }),
        " Designer"
      ] }) : null,
      /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
        S();
      }, children: [
        /* @__PURE__ */ o.jsx(Zo, { size: 14 }),
        " Refresh"
      ] }),
      c && N ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Mt(t, N, c.details), children: [
        /* @__PURE__ */ o.jsx(ut, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    i === "loading" ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Loading workflow run..." }) : null,
    i === "failed" ? /* @__PURE__ */ o.jsx(ri, { message: s }) : null,
    i === "ready" && c ? /* @__PURE__ */ o.jsxs("div", { className: M, style: m, children: [
      /* @__PURE__ */ o.jsx(
        Ej,
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
      x && !w ? /* @__PURE__ */ o.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize run details panel",
          "aria-orientation": "vertical",
          "aria-valuemin": qt,
          "aria-valuemax": Yt,
          "aria-valuenow": h,
          tabIndex: 0,
          onPointerDown: (I) => v("inspector", I),
          onKeyDown: (I) => j("inspector", I)
        }
      ) : /* @__PURE__ */ o.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ o.jsx(
        Aj,
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
          graphNodeIds: c.definitionVersion ? Ij(c.definitionVersion, c.activityCatalog) : void 0,
          rootNodeId: c.definitionVersion?.state.rootActivity?.nodeId,
          collapsed: y,
          expanded: x,
          maximized: w === "inspector",
          onToggleCollapsed: () => b("inspector"),
          onToggleMaximized: () => g("inspector")
        }
      )
    ] }) : null
  ] });
}
function Ej({
  definitionVersion: e,
  definitionVersionError: t,
  activityCatalog: n,
  details: i,
  selectedEvidenceId: r,
  onSelectEvidence: s,
  frames: a,
  onNavigateToScope: c
}) {
  const u = kj(a), l = ue(() => {
    if (!e) return { nodes: [], edges: [] };
    const d = e.state.rootActivity;
    if (!d) return { nodes: [], edges: [] };
    const f = Mn(d, a, n), p = f?.owner ?? d, h = n.find((m) => m.activityVersionId === p.activityVersionId), w = cl(p, h) === "unsupported" || !f ? al(p, n, e.layout) : sl(f, n, e.layout), x = w.nodes.map((m) => ({
      ...m,
      draggable: !1,
      connectable: !1,
      deletable: !1,
      data: {
        ...m.data,
        onEnterSlot: (b) => {
          c([...a, {
            ownerNodeId: m.id,
            slotId: b.id,
            label: `${m.data.label} / ${b.label}`
          }]);
        }
      }
    }));
    return {
      nodes: rp(x, i.activities, i.incidents, r),
      edges: w.edges.map((m) => ({ ...m, deletable: !1 }))
    };
  }, [n, e, i, a, c, r]);
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-instance-canvas-shell", "aria-label": "Workflow run canvas", children: [
    /* @__PURE__ */ o.jsxs("header", { children: [
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("span", { children: "Definition version" }),
        /* @__PURE__ */ o.jsx("h3", { children: e ? /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
          e.definition.name,
          " ",
          /* @__PURE__ */ o.jsx("small", { children: e.version })
        ] }) : /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
          "Definition graph unavailable ",
          /* @__PURE__ */ o.jsx("small", { children: i.instance.definitionVersionId })
        ] }) })
      ] }),
      /* @__PURE__ */ o.jsx(pn, { status: i.instance.status, subStatus: i.instance.subStatus })
    ] }),
    e ? /* @__PURE__ */ o.jsxs("div", { className: "wf-breadcrumb wf-instance-breadcrumb", children: [
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => c([]), children: "Root" }),
      a.map((d, f) => /* @__PURE__ */ o.jsxs("span", { className: "wf-breadcrumb-segment", children: [
        /* @__PURE__ */ o.jsx(mt, { size: 13 }),
        /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => c(a.slice(0, f + 1)), children: d.label })
      ] }, `${d.ownerNodeId}-${d.slotId}-${f}`))
    ] }) : null,
    /* @__PURE__ */ o.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ o.jsxs("div", { className: "wf-empty", children: [
        "The workflow run loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ o.jsx("small", { children: Qb(t) }) : null
      ] }),
      e && l.nodes.length === 0 ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      l.nodes.length > 0 ? /* @__PURE__ */ o.jsxs(
        md,
        {
          nodes: l.nodes,
          edges: l.edges,
          nodeTypes: Vd,
          edgeTypes: Od,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (d, f) => s(f.id),
          onPaneClick: () => s(null),
          children: [
            /* @__PURE__ */ o.jsx(wd, {}),
            /* @__PURE__ */ o.jsx(jd, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ o.jsx(bd, {})
          ]
        },
        u
      ) : null
    ] })
  ] });
}
function kj(e) {
  return e.length === 0 ? "root" : e.map((t) => `${t.ownerNodeId}:${t.slotId}`).join("/");
}
function Ij(e, t) {
  const n = /* @__PURE__ */ new Set(), i = (r) => {
    if (r) {
      n.add(r.nodeId);
      for (const s of Re(r, t))
        s.activities.forEach(i);
    }
  };
  return i(e.state.rootActivity), n;
}
function Aj({
  context: e,
  ai: t,
  action: n,
  summary: i,
  details: r,
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
  onToggleCollapsed: w,
  onToggleMaximized: x
}) {
  const [m, b] = K("timeline");
  if (!i)
    return /* @__PURE__ */ o.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Select a workflow run to inspect its timeline." }) });
  const g = r?.incidents.length ?? 0, v = _j(r?.activities ?? [], c), j = (S) => {
    u?.(S), b("activity");
  }, N = [
    { id: "timeline", title: "Timeline", order: 0, icon: /* @__PURE__ */ o.jsx(qo, { size: 14 }), render: () => null },
    { id: "activity", title: "Activity", order: 1, icon: /* @__PURE__ */ o.jsx(Xc, { size: 14 }), render: () => null },
    { id: "issues", title: g > 0 ? `Issues (${g})` : "Issues", order: 2, icon: /* @__PURE__ */ o.jsx(vt, { size: 14 }), render: () => null },
    { id: "details", title: "Details", order: 3, icon: /* @__PURE__ */ o.jsx(Yo, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ o.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Run details panel", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-panel-title wf-instance-panel-title", children: [
      /* @__PURE__ */ o.jsx(Li, { label: "Run details tabs", tabs: N, activeTabId: m, onSelect: (S) => b(S) }),
      /* @__PURE__ */ o.jsxs("span", { className: "wf-panel-actions", children: [
        /* @__PURE__ */ o.jsx(
          "button",
          {
            type: "button",
            className: "wf-panel-action-button",
            "aria-label": p ? "Expand run details panel" : "Collapse run details panel",
            title: p ? "Expand" : "Collapse",
            onClick: w,
            children: p ? /* @__PURE__ */ o.jsx($n, { size: 14 }) : /* @__PURE__ */ o.jsx(mt, { size: 14 })
          }
        ),
        p ? null : /* @__PURE__ */ o.jsx(
          "button",
          {
            type: "button",
            className: "wf-panel-action-button",
            "aria-label": y ? "Restore run details panel" : "Maximize run details panel",
            title: y ? "Restore" : "Maximize",
            onClick: x,
            children: y ? /* @__PURE__ */ o.jsx(bo, { size: 14 }) : /* @__PURE__ */ o.jsx(Tn, { size: 14 })
          }
        )
      ] })
    ] }),
    h ? /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
      /* @__PURE__ */ o.jsxs("header", { children: [
        /* @__PURE__ */ o.jsxs("div", { children: [
          /* @__PURE__ */ o.jsx("span", { children: "Workflow Instance ID" }),
          /* @__PURE__ */ o.jsx("h3", { children: i.workflowExecutionId })
        ] }),
        n ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Mt(t, n, r ?? i), children: [
          /* @__PURE__ */ o.jsx(ut, { size: 13 }),
          " Explain"
        ] }) : null
      ] }),
      s === "loading" ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Loading run details..." }) : null,
      s === "failed" ? /* @__PURE__ */ o.jsx(ri, { message: a }) : null,
      s === "ready" && r ? /* @__PURE__ */ o.jsx("div", { className: "wf-instance-tab-content", children: m === "timeline" ? /* @__PURE__ */ o.jsx(
        vj,
        {
          activities: r.activities,
          activityCatalog: f,
          selectedEvidenceId: c,
          onSelectEvidence: j
        }
      ) : m === "activity" ? /* @__PURE__ */ o.jsx(Dj, { context: e, activity: v, activityCatalog: f }) : m === "issues" ? /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
        /* @__PURE__ */ o.jsx(Vj, { incidents: r.incidents, selectedEvidenceId: c, onSelectEvidence: u }),
        /* @__PURE__ */ o.jsx(Fj, { details: r, graphNodeIds: l, rootNodeId: d })
      ] }) : /* @__PURE__ */ o.jsxs("dl", { className: "wf-instance-meta", children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ o.jsx("dd", { children: /* @__PURE__ */ o.jsx(pn, { status: i.status, subStatus: i.subStatus }) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Run Kind" }),
        /* @__PURE__ */ o.jsx("dd", { children: Pd(i.runKind) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Artifact" }),
        /* @__PURE__ */ o.jsxs("dd", { children: [
          i.artifactId,
          " ",
          /* @__PURE__ */ o.jsx("small", { children: i.artifactVersion })
        ] }),
        /* @__PURE__ */ o.jsx("dt", { children: "Definition" }),
        /* @__PURE__ */ o.jsxs("dd", { children: [
          i.definitionId,
          " ",
          /* @__PURE__ */ o.jsx("small", { children: i.definitionVersionId })
        ] }),
        /* @__PURE__ */ o.jsx("dt", { children: "Created" }),
        /* @__PURE__ */ o.jsx("dd", { children: Me(i.createdAt) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Started" }),
        /* @__PURE__ */ o.jsx("dd", { children: Me(i.startedAt) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Completed" }),
        /* @__PURE__ */ o.jsx("dd", { children: Me(i.completedAt) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Correlation" }),
        /* @__PURE__ */ o.jsx("dd", { children: i.correlationId || "None" })
      ] }) }) : null
    ] }) : null
  ] });
}
function _j(e, t) {
  if (!t) return null;
  const n = e.find((r) => r.activityExecutionId === t);
  if (n) return n;
  const i = e.filter((r) => r.executableNodeId === t || r.authoredActivityId === t);
  return i.length > 0 ? gl(i) : null;
}
function Dj({ context: e, activity: t, activityCatalog: n }) {
  const i = t?.activityExecutionId ?? null, r = t?.workflowExecutionId ?? null, [s, a] = K({
    activityExecutionId: null,
    status: "idle",
    inspection: null,
    error: ""
  });
  if (Q(() => {
    if (!i || !r) {
      a({ activityExecutionId: null, status: "idle", inspection: null, error: "" });
      return;
    }
    let f = !1;
    const p = i;
    return a({ activityExecutionId: p, status: "loading", inspection: null, error: "" }), Qp(e, r, p).then(
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
  }, [e, i, r]), !t)
    return /* @__PURE__ */ o.jsxs("section", { className: "wf-instance-section", children: [
      /* @__PURE__ */ o.jsx("h4", { children: "Activity" }),
      /* @__PURE__ */ o.jsx("p", { children: "No activity selected." })
    ] });
  const u = n.find((f) => f.activityTypeKey === t.activityType)?.displayName || It(t.activityType) || t.activityType, l = t.bookmarkIds?.length ?? 0, d = t.incidentIds?.length ?? 0;
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsxs("section", { className: "wf-instance-section", children: [
      /* @__PURE__ */ o.jsx("h4", { children: "Activity" }),
      /* @__PURE__ */ o.jsxs("dl", { className: "wf-instance-meta", children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Name" }),
        /* @__PURE__ */ o.jsx("dd", { children: u }),
        /* @__PURE__ */ o.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ o.jsx("dd", { children: /* @__PURE__ */ o.jsx(pn, { status: t.status, subStatus: t.subStatus }) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Activity Execution ID" }),
        /* @__PURE__ */ o.jsx("dd", { children: t.activityExecutionId }),
        /* @__PURE__ */ o.jsx("dt", { children: "Authored Activity ID" }),
        /* @__PURE__ */ o.jsx("dd", { children: t.authoredActivityId }),
        /* @__PURE__ */ o.jsx("dt", { children: "Type" }),
        /* @__PURE__ */ o.jsxs("dd", { children: [
          It(t.activityType) ?? t.activityType,
          " ",
          /* @__PURE__ */ o.jsx("small", { children: t.activityTypeVersion })
        ] }),
        /* @__PURE__ */ o.jsx("dt", { children: "Started" }),
        /* @__PURE__ */ o.jsx("dd", { children: Me(t.startedAt) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Completed" }),
        /* @__PURE__ */ o.jsx("dd", { children: Me(t.completedAt) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Duration" }),
        /* @__PURE__ */ o.jsx("dd", { children: ss(t.startedAt, t.completedAt) || "Unknown" }),
        /* @__PURE__ */ o.jsx("dt", { children: "Bookmarks" }),
        /* @__PURE__ */ o.jsx("dd", { children: l }),
        /* @__PURE__ */ o.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ o.jsx("dd", { children: d })
      ] })
    ] }),
    /* @__PURE__ */ o.jsx(Tj, { state: s })
  ] });
}
function Tj({ state: e }) {
  if (e.status === "idle") return null;
  if (e.status === "loading")
    return /* @__PURE__ */ o.jsxs("section", { className: "wf-instance-section", children: [
      /* @__PURE__ */ o.jsx("h4", { children: "Inputs" }),
      /* @__PURE__ */ o.jsx("p", { children: "Loading runtime input evidence..." })
    ] });
  if (e.status === "failed")
    return /* @__PURE__ */ o.jsxs("section", { className: "wf-instance-section", children: [
      /* @__PURE__ */ o.jsx("h4", { children: "Inputs" }),
      /* @__PURE__ */ o.jsx("p", { children: "Runtime input evidence is unavailable." }),
      e.error ? /* @__PURE__ */ o.jsx("p", { className: "wf-instance-note", children: e.error }) : null
    ] });
  const t = (e.inspection?.valueSnapshots ?? []).filter((n) => n.subject === "ActivityInput");
  return t.length === 0 ? /* @__PURE__ */ o.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ o.jsx("h4", { children: "Inputs" }),
    /* @__PURE__ */ o.jsx("p", { children: "No runtime input snapshots were recorded for this execution." })
  ] }) : /* @__PURE__ */ o.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ o.jsx("h4", { children: "Inputs" }),
    /* @__PURE__ */ o.jsx("div", { className: "wf-runtime-input-list", children: t.map((n) => /* @__PURE__ */ o.jsx($j, { snapshot: n }, `${n.name}:${n.capturedAt}:${n.captureMode}`)) })
  ] });
}
function $j({ snapshot: e }) {
  const t = e.type?.displayName || e.type?.typeName || e.type?.alias || "Unknown", n = e.captureMode === "Payload";
  return /* @__PURE__ */ o.jsxs("article", { className: "wf-runtime-input", children: [
    /* @__PURE__ */ o.jsxs("header", { children: [
      /* @__PURE__ */ o.jsxs("span", { children: [
        /* @__PURE__ */ o.jsx("strong", { children: e.name }),
        /* @__PURE__ */ o.jsx("small", { children: t })
      ] }),
      /* @__PURE__ */ o.jsx("span", { className: "wf-runtime-capture-mode", children: Mj(e.captureMode) })
    ] }),
    n ? /* @__PURE__ */ o.jsx(Pj, { payload: e.payload }) : /* @__PURE__ */ o.jsx("p", { children: e.captureReason || "The runtime capture policy did not include this input value." }),
    e.isSensitive ? /* @__PURE__ */ o.jsx("p", { className: "wf-instance-note", children: "Marked sensitive by runtime evidence." }) : null
  ] });
}
function Pj({ payload: e }) {
  const t = Lj(e);
  return t.length <= 160 && !t.includes(`
`) ? /* @__PURE__ */ o.jsx("code", { className: "wf-runtime-input-value", children: t }) : /* @__PURE__ */ o.jsxs("details", { className: "wf-runtime-input-value-details", children: [
    /* @__PURE__ */ o.jsx("summary", { children: Rj(t) }),
    /* @__PURE__ */ o.jsx("pre", { children: t })
  ] });
}
function Mj(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2");
}
function Rj(e) {
  const t = e.split(`
`, 1)[0] || e;
  return t.length > 120 ? `${t.slice(0, 117)}...` : t;
}
function Lj(e) {
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
const zj = [
  "runtime.faultStackTrace",
  "runtime.exceptionStackTrace",
  "runtime.stackTrace",
  "faultStackTrace",
  "exceptionStackTrace",
  "stackTrace"
];
function Vj({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
  const [i, r] = K("");
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ o.jsx("h4", { children: "Incidents" }),
    e.length === 0 ? /* @__PURE__ */ o.jsx("p", { children: "No incidents recorded." }) : null,
    e.map((s) => /* @__PURE__ */ o.jsxs(
      "article",
      {
        className: "wf-instance-incident",
        "data-severity": s.severity.toLowerCase(),
        "data-selected": s.incidentId === t,
        children: [
          /* @__PURE__ */ o.jsxs(
            "button",
            {
              type: "button",
              className: "wf-instance-incident-summary",
              "aria-label": `Select incident ${s.failureType}`,
              onClick: () => n?.(s.incidentId),
              children: [
                /* @__PURE__ */ o.jsx("strong", { children: s.failureType }),
                /* @__PURE__ */ o.jsxs("span", { children: [
                  s.status,
                  " · ",
                  s.severity
                ] }),
                /* @__PURE__ */ o.jsx("p", { children: s.message })
              ]
            }
          ),
          /* @__PURE__ */ o.jsx(
            Et,
            {
              value: Kj(s),
              ariaLabel: `Copy incident ${s.failureType}`,
              copiedLabel: "incident",
              onCopied: (a) => r(`Copied ${a}`),
              onCopyFailed: (a) => r(`Could not copy ${a}.`)
            }
          ),
          /* @__PURE__ */ o.jsx(Oj, { incident: s })
        ]
      },
      s.incidentId
    )),
    i ? /* @__PURE__ */ o.jsx("p", { className: "wf-copy-status", role: "status", children: i }) : null
  ] });
}
function Oj({ incident: e }) {
  const t = Hj(e);
  return t ? /* @__PURE__ */ o.jsxs("details", { className: "wf-incident-stacktrace", children: [
    /* @__PURE__ */ o.jsx("summary", { children: Bj(t) }),
    /* @__PURE__ */ o.jsx("pre", { children: t })
  ] }) : null;
}
function Hj(e) {
  const t = Wj(e.stackTrace, e.exceptionStackTrace);
  if (t) return t;
  for (const n of zj) {
    const i = e.metadata?.[n];
    if (i && i.trim()) return i;
  }
  return null;
}
function Wj(...e) {
  return e.find((t) => t?.trim()) ?? null;
}
function Bj(e) {
  const n = (e.split(`
`).find((i) => i.trim()) ?? e).trim();
  return n.length > 120 ? `${n.slice(0, 117)}...` : n;
}
function Fj({ details: e, graphNodeIds: t, rootNodeId: n }) {
  if (!t) return null;
  const i = e.activities.filter((r) => {
    const s = Kb(r);
    return s && s !== n && !t.has(s);
  });
  return i.length === 0 ? null : /* @__PURE__ */ o.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ o.jsx("h4", { children: "Executions outside canvas" }),
    /* @__PURE__ */ o.jsx("div", { className: "wf-instance-unmatched-list", children: i.map((r) => /* @__PURE__ */ o.jsxs("div", { className: "wf-instance-unmatched", children: [
      /* @__PURE__ */ o.jsx("strong", { children: It(r.activityType) ?? r.activityType }),
      /* @__PURE__ */ o.jsx("small", { children: r.activityExecutionId })
    ] }, `activity-${r.activityExecutionId}`)) })
  ] });
}
function Kj(e) {
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
    `Created: ${Me(e.createdAt)}`,
    e.resolvedAt ? `Resolved: ${Me(e.resolvedAt)}` : "",
    "",
    e.message
  ].filter(Boolean);
  return e.metadata && Object.keys(e.metadata).length > 0 && t.push("", "Metadata:", JSON.stringify(e.metadata, null, 2)), t.join(`
`);
}
function Xj({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: i,
  workflowDesignerPanels: r,
  autosaveEnabledByDefault: s
}) {
  const [a, c] = K(Oc);
  Q(() => {
    const l = () => c(Oc());
    return window.addEventListener("popstate", l), () => window.removeEventListener("popstate", l);
  }, []);
  const u = (l) => {
    const d = l ? `/workflows/definitions?definition=${encodeURIComponent(l)}` : "/workflows/definitions";
    window.history.pushState({}, "", d), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return a ? /* @__PURE__ */ o.jsx(yj, { context: e, definitionId: a, ai: t, propertyEditors: n, expressionEditors: i, workflowDesignerPanels: r, autosaveEnabledByDefault: s, onBack: () => u(null) }) : /* @__PURE__ */ o.jsx(mr, { title: "Definitions", children: /* @__PURE__ */ o.jsx(wj, { context: e, ai: t, onOpen: u }) });
}
function qj({ context: e, ai: t }) {
  const [n, i] = K(Hc);
  Q(() => {
    const s = () => i(Hc());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const r = ae((s) => {
    const a = s?.trim() ?? "", c = new URL(window.location.href);
    a ? c.searchParams.set("definition", a) : c.searchParams.delete("definition"), i(a || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ o.jsx(mr, { title: "Executables", children: /* @__PURE__ */ o.jsx(hN, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: r }) });
}
function Yj({ context: e }) {
  return /* @__PURE__ */ o.jsx(mr, { title: "Runs", children: /* @__PURE__ */ o.jsx(Sj, { context: e }) });
}
function Uj({ context: e, ai: t }) {
  const n = Zj();
  return /* @__PURE__ */ o.jsx(mr, { title: "Run", children: /* @__PURE__ */ o.jsx(Cj, { context: e, ai: t, workflowExecutionId: n }) });
}
function mr({ title: e, children: t }) {
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ o.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ o.jsxs("div", { children: [
      /* @__PURE__ */ o.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ o.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function Oc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Hc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Zj() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function tS(e) {
  jf(e.dialogs), e.featureAreas.add({
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
        component: () => /* @__PURE__ */ o.jsx(Xj, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list(), autosaveEnabledByDefault: e.runtime.workflows?.autosaveEnabledByDefault ?? !0 })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ o.jsx(qj, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => /* @__PURE__ */ o.jsx(Yj, { context: e.backend })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => /* @__PURE__ */ o.jsx(Uj, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => /* @__PURE__ */ o.jsx(Fh, { context: e.backend })
      }
    ]
  });
}
export {
  qb as isConnectEndOverExistingWorkflowNode,
  tS as register,
  Yb as resolveConnectEndSource
};
