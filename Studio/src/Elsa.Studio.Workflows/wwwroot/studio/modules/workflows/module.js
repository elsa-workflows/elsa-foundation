import tt, { useMemo as de, useState as B, useEffect as G, memo as Se, forwardRef as Tc, useRef as ne, useCallback as re, useContext as Wn, createContext as Or, useLayoutEffect as Xd, lazy as Yd, Suspense as qd, useReducer as Ud, useId as Pc } from "react";
import { ListChecks as Zd, Save as Mc, EyeOff as ks, Shield as As, AlertTriangle as Ti, SlidersHorizontal as Hr, Activity as Rc, Search as Gi, Check as an, Boxes as Fn, Zap as Gd, Play as Zt, Terminal as Jd, ListTree as Wr, GitBranch as zc, Plus as Gt, Trash2 as An, AlertCircle as wt, Wrench as Qd, Copy as ef, X as Lc, Sparkles as lt, RotateCcw as Fr, ChevronDown as Vc, ChevronRight as yt, GripVertical as Oc, Maximize2 as _n, ChevronUp as tf, Package as Hc, Undo2 as nf, Redo2 as of, Network as rf, Download as sf, ChevronLeft as Dn, Minimize2 as yr, Workflow as Wc, Code2 as af } from "lucide-react";
import { useQuery as Fc, useQueryClient as cf, useMutation as lf } from "@tanstack/react-query";
import { useTablistKeyboard as uf } from "@elsa-workflows/studio-ui";
function df(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var Jo = { exports: {} }, hn = {};
var _s;
function ff() {
  if (_s) return hn;
  _s = 1;
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
  return hn.Fragment = t, hn.jsx = n, hn.jsxs = n, hn;
}
var Ds;
function pf() {
  return Ds || (Ds = 1, Jo.exports = ff()), Jo.exports;
}
var r = pf();
let Bc;
function hf(e) {
  Bc = e;
}
function $s() {
  return Bc;
}
const gf = "String", yf = "singleline";
function mf(e) {
  return e === "Single" || e === "Array" || e === "List" || e === "HashSet";
}
function Br(e, t = "Single") {
  return { alias: (e ?? "").trim() || gf, collectionKind: t };
}
function Kc(e) {
  const t = e.type ?? e.Type;
  if (Pi(t))
    return { alias: typeof t.alias == "string" ? t.alias : typeof t.typeName == "string" ? t.typeName : "", collectionKind: mf(t.collectionKind) ? t.collectionKind : "Single" };
  const n = e.typeInformation ?? e.TypeInformation;
  return Pi(n) ? { alias: typeof n.typeName == "string" ? n.typeName : "", collectionKind: Ts(e) ? "Array" : "Single" } : { alias: typeof t == "string" ? t : "", collectionKind: Ts(e) ? "Array" : "Single" };
}
function Ts(e) {
  const t = e.isArray ?? e.IsArray;
  return t === !0 || t === "true";
}
function Ps(e) {
  return (e.alias ?? e.typeName ?? "").trim();
}
function Ji() {
  return typeof crypto < "u" && typeof crypto.randomUUID == "function" ? crypto.randomUUID() : `id-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
}
function xf(e, t) {
  const n = new Set(t);
  let i = 1, o = `${e}${i}`;
  for (; n.has(o); )
    i += 1, o = `${e}${i}`;
  return o;
}
function wf(e) {
  return {
    referenceKey: Ji(),
    name: e.name,
    type: Br(e.alias),
    storageDriverType: e.storageDriver?.trim() || null,
    default: null
  };
}
function vf(e, t) {
  return { ...e, ...t };
}
function bf(e) {
  return e === "" ? null : { value: e, expressionType: "Literal" };
}
function Nf(e) {
  return e == null || e.value == null ? "" : typeof e.value == "object" ? JSON.stringify(e.value) : String(e.value);
}
function jf(e) {
  return {
    referenceKey: Ji(),
    name: e.name,
    type: Br(e.alias),
    displayName: e.name,
    description: "",
    category: "",
    uiHint: yf,
    storageDriverType: e.storageDriver ?? null,
    isRequired: !1
  };
}
function Sf(e, t) {
  return { ...e, ...t };
}
function Cf(e) {
  return {
    referenceKey: Ji(),
    name: e.name,
    type: Br(e.alias),
    displayName: e.name,
    description: "",
    category: ""
  };
}
function Ef(e, t) {
  return { ...e, ...t };
}
function If(e) {
  const t = e.split(",")[0].trim();
  return (t.split(".").pop() ?? t).split("`")[0];
}
function Xc(e, t) {
  return e && !e.includes(",") && !e.includes(".") ? e : If(e || t);
}
function kf(e, t) {
  return Xc(e, t).replace(/StorageDriver$/, "");
}
function Pi(e) {
  return !!e && typeof e == "object" && !Array.isArray(e);
}
function Cn(e, t) {
  for (const n of t) {
    const i = e[n];
    if (i != null && typeof i != "object") return String(i);
  }
  return "";
}
const Af = ["name", "Name"], Yc = ["name", "Name"], _f = ["storageDriverType", "StorageDriverType"], qc = ["referenceKey", "ReferenceKey"], Df = /* @__PURE__ */ new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);
function Bn(e) {
  return Uc(e, Mf);
}
function Qi(e) {
  return Uc(e, Rf);
}
function Uc(e, t) {
  if (!e) return e;
  const n = { ...e };
  return e.rootActivity && (n.rootActivity = mr(e.rootActivity, t)), Array.isArray(e.variables) && (n.variables = Ni(e.variables, xr)), Array.isArray(e.inputs) && (n.inputs = Ni(e.inputs, xr)), Array.isArray(e.outputs) && (n.outputs = Ni(e.outputs, (i) => Zc(i, !1))), n;
}
function mr(e, t) {
  const n = t(e), i = n.structure;
  if (!i || !nt(i.payload)) return n;
  let o = !1;
  const s = { ...i.payload };
  for (const [a, c] of Object.entries(i.payload))
    Rs(c) ? (s[a] = mr(c, t), o = !0) : Array.isArray(c) && c.length > 0 && c.every(Rs) && (s[a] = c.map((u) => mr(u, t)), o = !0);
  return Array.isArray(i.payload.variables) && i.payload.variables.length > 0 && (s.variables = Ni(i.payload.variables, xr), o = !0), o ? { ...n, structure: { ...i, payload: s } } : n;
}
function Ni(e, t) {
  return e.map((n) => nt(n) && !Array.isArray(n) ? t(n) : n);
}
function xr(e) {
  return Zc(e, !0);
}
const $f = [
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
function Zc(e, t) {
  const n = Tf(e, $f);
  return Cn(e, qc).trim() || (n.referenceKey = Ji()), n.type = Kc(e), t && (n.storageDriverType = Pf(e.storageDriverType ?? e.StorageDriverType)), n;
}
function Tf(e, t) {
  const n = new Set(t), i = {};
  for (const [o, s] of Object.entries(e))
    n.has(o) || (i[o] = s);
  return i;
}
function Pf(e) {
  if (typeof e == "string") return e.trim() ? e : null;
  if (nt(e)) {
    const t = typeof e.typeName == "string" ? e.typeName : "";
    if (!t) return null;
    const n = typeof e.namespace == "string" ? e.namespace : "";
    return n ? `${n}.${t}` : t;
  }
  return null;
}
function Mf(e) {
  const t = [], n = {};
  for (const [o, s] of Object.entries(e))
    Df.has(o) || (Of(s) ? t.push({
      referenceKey: zf(o),
      value: Vf(s.expression)
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
function Rf(e) {
  const t = Array.isArray(e.inputs) ? e.inputs : [], n = {};
  for (const i of t) {
    if (!nt(i) || typeof i.referenceKey != "string") continue;
    const o = nt(i.value) ? i.value : {};
    n[Lf(i.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof o.expressionType == "string" ? o.expressionType : "Literal",
        value: o.value ?? ""
      }
    };
  }
  return { ...e, ...n, inputs: [] };
}
function zf(e) {
  return e && e.charAt(0).toUpperCase() + e.slice(1);
}
function Lf(e) {
  return e && e.charAt(0).toLowerCase() + e.slice(1);
}
function Vf(e) {
  const t = e.type || "Literal";
  return t === "Variable" && nt(e.value) ? { value: e.value, expressionType: t } : t === "Literal" && nt(e.value) ? { value: Ms(e.value), expressionType: "Object" } : { value: Ms(e.value), expressionType: t };
}
function Ms(e) {
  return e == null ? null : typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function Of(e) {
  if (!nt(e) || Array.isArray(e)) return !1;
  const t = e.expression;
  return nt(t) && typeof t.type == "string";
}
function Rs(e) {
  return nt(e) && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function nt(e) {
  return typeof e == "object" && e !== null;
}
const Kn = "elsa.sequence.structure", cn = "elsa.flowchart.structure";
function Gc(e, t, n) {
  if (!e) return null;
  let i = e;
  for (const o of t) {
    const s = Wf(i, o.ownerNodeId, n);
    if (!s) return null;
    i = s;
  }
  return i;
}
function Hf(e, t, n = (o) => o.nodeId, i) {
  if (!e) return null;
  if (e.nodeId === t) return [];
  const o = (s, a) => {
    const c = Me(s, i);
    for (const u of c) {
      if (!u.activities.some((d) => d.nodeId === t)) continue;
      const l = a.at(-1);
      return l ? [...a.slice(0, -1), { ...l, slotId: u.id }] : u.id === c[0]?.id ? a : null;
    }
    for (const u of c)
      for (const l of u.activities) {
        const f = Me(l, i)[0]?.id ?? u.id, p = o(l, [...a, { ownerNodeId: l.nodeId, slotId: f, label: n(l) }]);
        if (p) return p;
      }
    return null;
  };
  return o(e, []);
}
function Jc(e, t, n) {
  const i = Me(e, n), o = t.at(-1);
  return o ? i.find((s) => s.id === o.slotId) ?? null : i[0] ?? null;
}
function $n(e, t, n) {
  const i = Gc(e, t, n);
  if (!i) return null;
  const o = Jc(i, t, n);
  return o ? { owner: i, slot: o } : null;
}
function Qc(e, t, n) {
  for (const i of Me(e, n)) {
    const o = i.activities.find((s) => s.nodeId === t);
    if (o) return { slot: i, child: o };
  }
  return null;
}
function Wf(e, t, n) {
  return Qc(e, t, n)?.child ?? null;
}
function Me(e, t) {
  const n = e.structure;
  if (!n || !n.payload || typeof n.payload != "object") return [];
  const i = n.payload, o = Kr(el(e, t));
  if (o?.kind === n.kind) return Kf(n, o);
  const s = fp(n), a = xn(i.activities);
  return a ? [{
    id: `${n.kind}:activities`,
    label: pp(n),
    property: "activities",
    cardinality: "many",
    mode: s,
    activities: a
  }] : Object.entries(i).filter(([, c]) => xn(c) || Ri(c)).map(([c, u]) => ({
    id: `${n.kind}:${c}`,
    label: gp(c),
    property: c,
    cardinality: xn(u) ? "many" : "single",
    mode: "generic",
    activities: xn(u) ?? (Ri(u) ? [u] : [])
  }));
}
function Kr(e) {
  for (const t of e?.designFacets ?? []) {
    if (!it(t)) continue;
    const n = typeof t.kind == "string" ? t.kind : "", i = typeof t.schemaVersion == "string" ? t.schemaVersion : "";
    if (!n || !i || !it(t.payload)) continue;
    const o = Ff(t.payload);
    if (o) return { kind: n, schemaVersion: i, payload: o };
  }
  return null;
}
function Ff(e) {
  const t = e.mode;
  if (t !== "sequence" && t !== "flowchart" && t !== "generic" || typeof e.supportsScopedVariables != "boolean" || !Array.isArray(e.slots) || !it(e.initialPayload)) return null;
  const n = e.slots.map(Bf).filter((i) => i !== null);
  return {
    mode: t,
    supportsScopedVariables: e.supportsScopedVariables,
    slots: n,
    initialPayload: e.initialPayload
  };
}
function Bf(e) {
  if (!it(e)) return null;
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
function Kf(e, t) {
  return t.payload.slots.flatMap((n) => {
    if (n.collectionProperty && n.childProperty) {
      const i = n.childProperty, o = e.payload[n.collectionProperty];
      return Array.isArray(o) ? o.flatMap((s, a) => {
        if (!it(s)) return [];
        const c = n.labelProperty ? Ft(s[n.labelProperty]) : void 0;
        return [zs(e.kind, n, s[i], t.payload.mode, a, c)];
      }) : [];
    }
    return [zs(e.kind, n, e.payload[n.property], t.payload.mode)];
  });
}
function zs(e, t, n, i, o, s) {
  const a = o === void 0 ? "" : `:${t.collectionProperty}:${t.childProperty}:${o}`;
  return {
    id: `${e}:${t.property}${a}`,
    label: o === void 0 ? t.displayName : Yf(t, o, s),
    property: t.property,
    cardinality: t.cardinality,
    mode: i,
    activities: Xf(n, t.cardinality),
    collectionProperty: t.collectionProperty,
    childProperty: t.childProperty,
    labelProperty: t.labelProperty,
    slotNameTemplate: t.slotNameTemplate,
    collectionIndex: o,
    collectionItemLabel: s
  };
}
function Xf(e, t) {
  return t === "many" ? xn(e) ?? [] : Ri(e) ? [e] : [];
}
function Yf(e, t, n) {
  return e.slotNameTemplate ? e.slotNameTemplate.replaceAll("{name}", e.name).replaceAll("{displayName}", e.displayName).replaceAll("{label}", n ?? String(t + 1)).replaceAll("{index}", String(t + 1)) : n ? `${e.displayName}: ${n}` : `${e.displayName} ${t + 1}`;
}
function el(e, t) {
  if (t)
    return t instanceof Map ? t.get(e.activityVersionId) : Array.isArray(t) ? t.find((n) => n.activityVersionId === e.activityVersionId) : t.activityVersionId === e.activityVersionId ? t : void 0;
}
function qf(e, t) {
  if (t.labelProperty && t.collectionItemLabel) {
    const n = e.map((i, o) => it(i) && Ft(i[t.labelProperty]) === t.collectionItemLabel ? o : -1).filter((i) => i >= 0);
    if (n.length === 1) return n[0];
  }
  return typeof t.collectionIndex == "number" && t.collectionIndex >= 0 && t.collectionIndex < e.length ? t.collectionIndex : -1;
}
function tl(e, t, n) {
  const i = new Map(t.map((a) => [a.activityVersionId, a])), o = new Map(n.map((a) => [a.nodeId, a])), s = e.slot.activities.map((a, c) => {
    const u = i.get(a.activityVersionId), l = o.get(a.nodeId) ?? hp(e.slot.mode, c);
    return sl(a, u, { x: l.x, y: l.y });
  });
  return {
    nodes: s,
    edges: e.slot.mode === "flowchart" ? cl(e.owner) : rp(e.slot, s)
  };
}
function nl(e, t, n) {
  const i = t.find((s) => s.activityVersionId === e.activityVersionId), o = n.find((s) => s.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [sl(e, i, { x: o.x, y: o.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function Uf(e, t, n, i = null) {
  const o = new Map(t.map((c) => [c.activityExecutionId, c])), s = Os(t, (c) => c.authoredActivityId || c.executableNodeId), a = Os(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? o.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const u = s.get(c.id) ?? [], l = a.get(c.id) ?? [];
    if (u.length === 0 && l.length === 0) return c;
    const d = ul(u), f = i === c.id || u.some((h) => h.activityExecutionId === i) || l.some((h) => h.incidentId === i), p = {
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
function il(e, t) {
  if (e?.structure?.kind === cn || tp(t)) return "flowchart";
  if (e?.structure?.kind === Kn || np(t)) return "sequence";
  if (e) {
    const n = Me(e, t)[0];
    if (n) return n.mode === "flowchart" ? "flowchart" : "sequence";
  }
  return "unsupported";
}
function Ls(e, t, n, i) {
  return Xr(e, t, i, (o) => {
    const s = Jc(o, t, i);
    return s ? Xn(o, s, n) : o;
  });
}
function Zf(e, t, n, i) {
  return t.length === 0 ? n : Xr(e, t, i, () => n);
}
function Xr(e, t, n, i) {
  if (t.length === 0) return i(e);
  const [o, ...s] = t, a = Qc(e, o.ownerNodeId, n);
  if (!a) return e;
  const c = Xr(a.child, s, n, i);
  if (c === a.child) return e;
  const u = a.slot.activities.map((l) => l.nodeId === o.ownerNodeId ? c : l);
  return Xn(e, a.slot, u);
}
function ol(e, t, n, i) {
  if (e.nodeId === t) return n(e);
  const o = Me(e, i);
  if (o.length === 0) return e;
  let s = !1, a = e;
  for (const c of o) {
    const u = c.activities.map((l) => {
      const d = ol(l, t, n, i);
      return d !== l && (s = !0), d;
    });
    s && (a = Xn(a, c, u));
  }
  return s ? a : e;
}
function Xn(e, t, n) {
  if (!e.structure) return e;
  const i = t.cardinality === "single" ? n[0] ?? null : n;
  if (t.collectionProperty && t.childProperty) {
    const o = e.structure.payload[t.collectionProperty];
    if (!Array.isArray(o)) return e;
    const s = qf(o, t);
    if (s < 0) return e;
    const a = o[s];
    if (!it(a)) return e;
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
function Gf(e, t, n, i = []) {
  const o = new Map(e.slot.activities.map((a) => [a.nodeId, a]));
  for (const a of i)
    o.set(a.nodeId, a);
  const s = t.map((a) => o.get(a.id)).filter((a) => !!a);
  return e.slot.mode === "sequence" && s.sort((a, c) => {
    const u = t.find((d) => d.id === a.nodeId), l = t.find((d) => d.id === c.nodeId);
    return (u?.position.x ?? 0) - (l?.position.x ?? 0);
  }), Xn(e.owner, e.slot, s);
}
function Jf(e, t) {
  return {
    ...e,
    structure: op(e.structure, t)
  };
}
function Qf(e, t) {
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
function wr(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: al(e)
  };
}
function rl(e, t) {
  if (!e) return null;
  const n = el(e, t), i = e.structure ?? (n ? al(n) : null);
  let o = i === e.structure ? e : { ...e, structure: i };
  const s = Me(o, t);
  for (const a of s) {
    const c = a.activities.map((u) => rl(u, t) ?? u);
    c.some((u, l) => u !== a.activities[l]) && (o = Xn(o, a, c));
  }
  return o;
}
function Ee(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? ip(t) : n;
}
function sl(e, t, n, i = {}) {
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
      icon: eo(t),
      childSlots: Me(e, t),
      acceptsInbound: sp(e, t),
      sourcePorts: i.suppressFlowPorts ? [] : ll(e, t),
      suppressFlowPorts: i.suppressFlowPorts
    }
  };
}
function eo(e) {
  if (!e) return "activity";
  const t = ep(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), i = Ee(e).toLowerCase(), o = e.category?.toLowerCase() ?? "", s = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || i === "flowchart" ? "flowchart" : n.endsWith(".sequence") || i === "sequence" ? "sequence" : n.includes("writeline") || i.includes("write line") ? "terminal" : o.includes("runtime") ? "runtime" : s === "trigger" ? "trigger" : "activity";
}
function ep(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function tp(e) {
  return !!e && (Ee(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function np(e) {
  return !!e && (Ee(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function ip(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function al(e) {
  const t = Kr(e);
  return t ? {
    kind: t.kind,
    schemaVersion: t.schemaVersion,
    payload: up(t.payload.initialPayload)
  } : e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: Kn,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: cn,
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
function op(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], i = /* @__PURE__ */ new Map();
  for (const o of n) {
    if (!it(o)) continue;
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
function rp(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, i) => ({
    id: `sequence-${n.id}-${t[i + 1].id}`,
    source: n.id,
    target: t[i + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function cl(e) {
  if (e.structure?.kind !== cn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, i) => {
    if (!n || typeof n != "object") return null;
    const o = n.source, s = n.target;
    if (!o?.nodeId || !s?.nodeId) return null;
    const a = Array.isArray(n.vertices) ? n.vertices.filter(dp) : [];
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
function ll(e, t) {
  const n = Vs(e.cases);
  if (cp(e, t) && n.length > 0)
    return [...n.map((s) => ({ name: s, displayName: s })), { name: "Default", displayName: "Default" }];
  const i = [
    ...ji(t?.designFacets),
    ...ji(t?.ports),
    ...ji(t?.outputs)
  ];
  if (i.length > 0) return lp(i);
  const o = Vs(e.outcomes);
  return o.length > 0 ? o.map((s) => ({ name: s, displayName: s })) : [{ name: "Done", displayName: "Done" }];
}
function sp(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function Mi(e, t, n, i) {
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
function ap(e, t, n) {
  const i = Mi(t.source, n, t.sourceHandle ?? "Done", void 0), o = Mi(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((s) => s.id !== t.id).concat(i, o);
}
function xn(e) {
  return Array.isArray(e) ? e.filter(Ri) : null;
}
function cp(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, i = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || i === "FlowSwitch";
}
function ji(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!it(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...ji(n.ports));
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
function lp(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function Vs(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function Ft(e) {
  return typeof e == "string" && e.length > 0 ? e : void 0;
}
function up(e) {
  return JSON.parse(JSON.stringify(e));
}
function Os(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e) {
    const o = t(i);
    o && n.set(o, [...n.get(o) ?? [], i]);
  }
  return n;
}
function ul(e) {
  return [...e].sort((t, n) => Hs(n).localeCompare(Hs(t)))[0];
}
function Hs(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function dp(e) {
  return it(e) && typeof e.x == "number" && typeof e.y == "number";
}
function it(e) {
  return typeof e == "object" && e !== null;
}
function Ri(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function fp(e) {
  return e.kind === Kn ? "sequence" : e.kind === cn ? "flowchart" : "generic";
}
function pp(e) {
  return e.kind === Kn || e.kind === cn, "Activities";
}
function hp(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function gp(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const zi = "workflow", yp = /* @__PURE__ */ new Set([Kn, cn]);
function mp(e, t) {
  const n = e?.structure?.kind;
  if (!n) return !1;
  if (yp.has(n)) return !0;
  const i = t?.activityVersionId === e?.activityVersionId ? Kr(t) : null;
  return i?.kind === n && i.payload.supportsScopedVariables;
}
function dl(e) {
  const t = e?.structure?.payload?.variables;
  return Array.isArray(t) ? t.filter(Pi) : [];
}
function xp(e, t) {
  return e.structure ? {
    ...e,
    structure: {
      ...e.structure,
      payload: { ...e.structure.payload, variables: t }
    }
  } : e;
}
function wp(e, t) {
  return {
    referenceKey: e,
    declaringScopeId: t && t !== zi ? t : zi
  };
}
function fl(e) {
  if (e && typeof e == "object") {
    const t = e;
    return typeof t.referenceKey == "string" && t.referenceKey ? { referenceKey: t.referenceKey, declaringScopeId: typeof t.declaringScopeId == "string" ? t.declaringScopeId : null } : null;
  }
  if (typeof e == "string") {
    const t = e.trim();
    if (t.startsWith("{"))
      try {
        return fl(JSON.parse(t));
      } catch {
      }
    return t ? { referenceKey: t, declaringScopeId: null } : null;
  }
  return null;
}
function vp(e, t) {
  if (!e) return "";
  const n = [`workflow:${Ws(e.variables)}`], i = (o) => {
    const s = Me(o, t), a = s.flatMap((c) => c.activities.map((u) => u.nodeId));
    n.push(`${o.nodeId}:${Ws(dl(o))}>${a.join(",")}`), s.forEach((c) => c.activities.forEach(i));
  };
  return e.rootActivity && i(e.rootActivity), n.join(";");
}
function Ws(e) {
  return (e ?? []).map((t) => `${t.referenceKey}=${t.name}`).join(",");
}
function bp(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e ?? [])
    i.scopeId === t && n.set(i.referenceKey, `Shadows "${i.name}" declared in an outer scope.`);
  return n;
}
const je = "/_elsa/workflow-management", Np = "/publishing", En = {
  activities: ["workflows", "activities"],
  activityAvailabilitySettings: ["workflows", "activity-availability", "settings"],
  activityAvailabilityDiagnostics: ["workflows", "activity-availability", "diagnostics"]
};
function jp(e) {
  return Fc({
    queryKey: En.activityAvailabilitySettings,
    queryFn: () => Bp(e)
  });
}
function Sp(e) {
  return Fc({
    queryKey: En.activityAvailabilityDiagnostics,
    queryFn: () => gl(e)
  });
}
function Cp(e) {
  const t = cf();
  return lf({
    mutationFn: (n) => Kp(e, n),
    onSuccess: () => {
      t.invalidateQueries({ queryKey: En.activityAvailabilitySettings }), t.invalidateQueries({ queryKey: En.activityAvailabilityDiagnostics }), t.invalidateQueries({ queryKey: En.activities });
    }
  });
}
async function Ep(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), i = t.search.trim();
  return i && n.set("search", i), e.http.getJson(`${je}/definitions?${n.toString()}`);
}
async function Ip(e, t) {
  const n = await e.http.getJson(`${je}/definitions/${encodeURIComponent(t)}`);
  return n.draft ? { ...n, draft: { ...n.draft, state: Qi(n.draft.state) } } : n;
}
async function kp(e, t, n) {
  const i = await e.http.postJson(
    `${je}/design/scoped-variables/analyze`,
    { state: Bn(t), nodeId: n }
  );
  return {
    visibleVariables: Array.isArray(i?.visibleVariables) ? i.visibleVariables : [],
    shadowingWarnings: Array.isArray(i?.shadowingWarnings) ? i.shadowingWarnings : []
  };
}
const Qo = (e) => ({ visibleVariables: [], shadowingWarnings: [], status: e });
function Ap(e, t, n, i) {
  const o = de(() => vp(t, i), [i, t]), [s, a] = B(() => Qo("loading"));
  return G(() => {
    if (!t) {
      a(Qo("unavailable"));
      return;
    }
    let c = !1;
    return a((u) => ({ ...u, status: "loading" })), kp(e, t, n).then(
      (u) => {
        c || a({ ...u, status: "ready" });
      },
      () => {
        c || a(Qo("unavailable"));
      }
    ), () => {
      c = !0;
    };
  }, [e, n, o]), s;
}
async function _p(e, t) {
  const n = await e.http.getJson(`${je}/versions/${encodeURIComponent(t)}`);
  return { ...n, state: Qi(n.state) };
}
async function Dp(e, t) {
  return e.http.postJson(`${je}/definitions`, t);
}
async function $p(e, t) {
  await e.http.deleteJson(`${je}/definitions/${encodeURIComponent(t)}`);
}
async function Tp(e, t) {
  await e.http.postJson(`${je}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function Pp(e, t) {
  await e.http.deleteJson(`${je}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function Mp(e, t, n) {
  return e.http.requestJson(
    `${je}/definitions/${encodeURIComponent(t)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(n)
    }
  );
}
async function Rp(e, t) {
  const n = await e.http.putJson(
    `${je}/drafts/${encodeURIComponent(t.id)}`,
    { state: Bn(t.state), layout: t.layout }
  );
  return { ...n, state: Qi(n.state) };
}
async function zp(e, t) {
  return e.http.postJson(`${je}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function Lp(e, t) {
  return e.http.postJson(`${je}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function Vp(e, t) {
  const n = { ...t, state: Bn(t.state) };
  try {
    return await e.http.postJson(`${Np}/workflows/drafts/test-runs`, n);
  } catch (i) {
    const o = Jp(i);
    if (o) return o;
    throw i;
  }
}
async function pl(e, t) {
  return e.http.postJson(`${je}/executables/${encodeURIComponent(t)}/run`, {});
}
async function hl(e) {
  const t = [`${je}/executables`, "/_demo/workflows/executables"], n = [];
  for (const i of t)
    try {
      const o = await e.http.getJson(i);
      return Op(o);
    } catch (o) {
      n.push(o);
    }
  if (n.length > 0 && n.every(Fs)) return [];
  throw n.find((i) => !Fs(i)) ?? n[n.length - 1] ?? new Error("Workflow executables could not be loaded.");
}
function Op(e) {
  return Array.isArray(e) ? e : e.executables ?? [];
}
function Fs(e) {
  if (!(e instanceof Error)) return !1;
  const t = e.message.toLowerCase();
  return /\b404\b/.test(t) || t.includes("not found");
}
async function Hp(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.runKind && n.set("runKind", t.runKind), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const i = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${i ? `?${i}` : ""}`);
}
async function Wp(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function Fp(e, t, n) {
  return e.http.getJson(
    `/runtime/workflows/instances/${encodeURIComponent(t)}/activity-executions/${encodeURIComponent(n)}`
  );
}
async function Yr(e) {
  return e.http.getJson(`${je}/activities`);
}
async function Bp(e) {
  return e.http.getJson(`${je}/activities/availability/settings`);
}
async function Kp(e, t) {
  return e.http.putJson(`${je}/activities/availability/settings`, t);
}
async function gl(e) {
  return e.http.getJson(`${je}/activities/availability/diagnostics`);
}
async function Xp(e) {
  const t = await to(e, [
    `${je}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? Bs(t) : Bs(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function Yp(e) {
  const t = await to(e, [
    `${je}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : Si;
}
async function qp(e) {
  const t = await to(e, [
    `${je}/descriptors/variables`,
    "/descriptors/variables"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((i) => Up(i));
}
function Up(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = typeof t.alias == "string" && t.alias.length > 0, i = typeof t.typeName == "string" && t.typeName.length > 0;
  return n || i;
}
async function Zp(e) {
  const t = await to(e, [
    `${je}/descriptors/storage-drivers`,
    "/descriptors/storage-drivers"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((i) => Gp(i));
}
function Gp(e) {
  return !!e && typeof e == "object" && typeof e.typeName == "string" && e.typeName.length > 0;
}
async function to(e, t) {
  let n;
  for (const i of t)
    try {
      return await e.http.getJson(i);
    } catch (o) {
      n = o;
    }
  throw n;
}
function Bs(e) {
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
function Jp(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = Ks(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return Ks(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function Ks(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const Si = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
], Qp = [
  "Available",
  "BlockedByHostBaseline",
  "HiddenByManagementSettings",
  "RemovedFromCatalog",
  "UnresolvedReference"
], eh = {
  Available: "Available",
  BlockedByHostBaseline: "Host blocked",
  HiddenByManagementSettings: "Management hidden",
  RemovedFromCatalog: "Removed",
  UnresolvedReference: "Unresolved"
};
function jt(e) {
  return typeof e == "string" ? e : typeof e == "number" ? Qp[e] ?? "Available" : "Available";
}
function Li(e) {
  const t = jt(e);
  return eh[t] ?? t;
}
function th(e) {
  return jt(e).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function yl(e) {
  return jt(e) !== "Available";
}
function nh(e) {
  return e === "Only" || e === 1 ? "Only" : "AllExcept";
}
function ih(e) {
  return e === "Only" ? 1 : 0;
}
function Xs(e) {
  const t = e?.rules;
  return {
    mode: nh(e?.mode),
    activityTypes: t?.activityTypes ?? [],
    sets: t?.sets ?? []
  };
}
function oh(e) {
  return e.referenceKind === 0 || e.referenceKind === "ActivityType";
}
function rh(e) {
  return [...e?.items ?? []].filter(oh).filter((t) => t.activityTypeKey && t.activityDefinitionId).sort((t, n) => Vi(t).localeCompare(Vi(n)));
}
function sh(e) {
  return [...e?.items ?? []].filter((t) => {
    const n = jt(t.state);
    return n === "RemovedFromCatalog" || n === "UnresolvedReference";
  }).sort((t, n) => (t.referenceName ?? "").localeCompare(n.referenceName ?? ""));
}
function Ys(e, t) {
  return e.includes(t) ? e.filter((n) => n !== t) : [...e, t].sort((n, i) => n.localeCompare(i));
}
function Vi(e) {
  const t = e?.displayName?.trim();
  if (t && t.toLowerCase() !== "activity") return t;
  const n = ch(e?.activityTypeKey);
  return ah(n) || t || e?.activityTypeKey || "Activity";
}
function qs(e) {
  const t = ml(e?.activityTypeKey);
  if (t.length === 0) return "";
  const n = t[t.length - 1], i = t[t.length - 2];
  return n === "Activity" && i ? `${i}.${n}` : n;
}
function Us(e) {
  const t = e.description?.trim();
  if (t) return t;
  const n = e.reason?.trim();
  return !n || !yl(e.state) ? "" : n;
}
function ah(e) {
  return e.replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/\bHttp\b/g, "HTTP").replace(/\bJson\b/g, "JSON").replace(/\bJava Script\b/g, "JavaScript").replace(/\bUrl\b/g, "URL").replace(/\bXml\b/g, "XML").trim();
}
function ch(e) {
  const t = ml(e), n = t[t.length - 1] ?? "";
  return n === "Activity" && t.length > 1 ? t[t.length - 2] : n;
}
function ml(e) {
  return e?.split(".").filter(Boolean) ?? [];
}
function lh(e, t) {
  const n = new Set(e.filter((i) => !!i));
  return (t?.items ?? []).find((i) => yl(i.state) ? [i.activityDefinitionId, i.activityTypeKey, i.referenceName].some((o) => o && n.has(o)) : !1) ?? null;
}
function uh({ context: e }) {
  const t = jp(e), n = Sp(e), i = Cp(e), o = t.data ?? null, s = n.data ?? null, a = t.isLoading || n.isLoading, c = i.isPending, [u, l] = B(() => Xs(o)), [d, f] = B(""), [p, h] = B(null);
  G(() => {
    l(Xs(o));
  }, [o]);
  const y = de(() => rh(s), [s]), w = de(() => sh(s), [s]), x = s?.sets ?? [], m = de(() => {
    const k = d.trim().toLowerCase();
    return k ? y.filter((R) => [
      Vi(R),
      qs(R),
      Us(R),
      R.activityTypeKey,
      R.category
    ].some((C) => (C ?? "").toLowerCase().includes(k))) : y;
  }, [y, d]), b = new Set(u.activityTypes), g = new Set(u.sets), v = y.filter((k) => jt(k.state) === "BlockedByHostBaseline").length, j = y.filter((k) => jt(k.state) === "HiddenByManagementSettings").length, N = i.error ?? t.error ?? n.error, S = N instanceof Error ? N.message : N ? "Activity availability could not be loaded." : null, I = (k) => l((R) => ({ ...R, mode: k })), D = (k) => l((R) => ({ ...R, activityTypes: Ys(R.activityTypes, k) })), M = (k) => l((R) => ({ ...R, sets: Ys(R.sets, k) })), A = () => {
    h(null), i.mutate(
      {
        scope: o?.scope ?? "host-default",
        mode: ih(u.mode),
        rules: { activityTypes: u.activityTypes, sets: u.sets }
      },
      { onSuccess: () => h("Activity availability saved.") }
    );
  };
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-page availability-page", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-page-header", children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("div", { className: "wf-kicker", children: "Workflows" }),
        /* @__PURE__ */ r.jsxs("h2", { children: [
          /* @__PURE__ */ r.jsx(Zd, { size: 18 }),
          " Activity availability"
        ] }),
        /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Control which activities can be added to new workflows. Host baseline rules always take precedence." })
      ] }),
      /* @__PURE__ */ r.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ r.jsxs("button", { type: "button", className: "availability-save", onClick: A, disabled: a || c, children: [
        /* @__PURE__ */ r.jsx(Mc, { size: 15 }),
        c ? "Saving…" : "Save"
      ] }) })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "availability-body", children: [
      S && /* @__PURE__ */ r.jsx("div", { className: "availability-banner availability-banner-error", children: S }),
      p && !S && /* @__PURE__ */ r.jsx("div", { className: "availability-banner availability-banner-success", children: p }),
      /* @__PURE__ */ r.jsxs("div", { className: "availability-mode", role: "group", "aria-label": "Activity availability mode", children: [
        /* @__PURE__ */ r.jsxs("button", { type: "button", className: u.mode === "AllExcept" ? "active" : "", onClick: () => I("AllExcept"), disabled: a || c, children: [
          /* @__PURE__ */ r.jsx(ks, { size: 15 }),
          /* @__PURE__ */ r.jsxs("span", { children: [
            /* @__PURE__ */ r.jsx("strong", { children: "All except" }),
            /* @__PURE__ */ r.jsx("em", { children: "Show everything except the selected activities" })
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", className: u.mode === "Only" ? "active" : "", onClick: () => I("Only"), disabled: a || c, children: [
          /* @__PURE__ */ r.jsx(As, { size: 15 }),
          /* @__PURE__ */ r.jsxs("span", { children: [
            /* @__PURE__ */ r.jsx("strong", { children: "Only" }),
            /* @__PURE__ */ r.jsx("em", { children: "Show only the selected activities" })
          ] })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "availability-counts", children: [
        /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx(As, { size: 14 }),
          " ",
          v,
          " host blocked"
        ] }),
        /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx(ks, { size: 14 }),
          " ",
          j,
          " management hidden"
        ] }),
        /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx(Ti, { size: 14 }),
          " ",
          w.length,
          " unresolved"
        ] })
      ] }),
      x.length > 0 && /* @__PURE__ */ r.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ r.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ r.jsx(Hr, { size: 14 }),
          " Sets"
        ] }),
        /* @__PURE__ */ r.jsx("div", { className: "availability-set-list", children: x.map((k) => /* @__PURE__ */ r.jsxs("label", { className: "availability-set-option", children: [
          /* @__PURE__ */ r.jsx("input", { type: "checkbox", checked: g.has(k.name), disabled: a || c, onChange: () => M(k.name) }),
          /* @__PURE__ */ r.jsx("span", { children: k.name }),
          /* @__PURE__ */ r.jsx("code", { children: (k.activityTypeKeys ?? []).length })
        ] }, k.name)) })
      ] }),
      /* @__PURE__ */ r.jsxs("section", { className: "availability-section availability-section-grow", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "availability-section-head", children: [
          /* @__PURE__ */ r.jsxs("h3", { className: "wf-section-label", children: [
            /* @__PURE__ */ r.jsx(Rc, { size: 14 }),
            " Activities"
          ] }),
          /* @__PURE__ */ r.jsxs("div", { className: "wf-search availability-search", children: [
            /* @__PURE__ */ r.jsx(Gi, { size: 14 }),
            /* @__PURE__ */ r.jsx("input", { type: "search", value: d, placeholder: "Filter activities…", onChange: (k) => f(k.target.value) })
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("div", { className: "availability-activity-list", children: [
          a && y.length === 0 && /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Loading availability…" }),
          !a && y.length === 0 && /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No availability diagnostics reported." }),
          !a && y.length > 0 && m.length === 0 && /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No activities match the filter." }),
          m.map((k) => {
            const C = jt(k.state) === "BlockedByHostBaseline", _ = k.activityTypeKey ?? k.activityDefinitionId ?? "", E = Vi(k), $ = qs(k), P = Us(k);
            return /* @__PURE__ */ r.jsxs("label", { className: `availability-activity-option ${C ? "disabled" : ""}`, children: [
              /* @__PURE__ */ r.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: b.has(_),
                  disabled: a || c || C,
                  onChange: () => D(_)
                }
              ),
              /* @__PURE__ */ r.jsxs("span", { className: "availability-activity-main", children: [
                /* @__PURE__ */ r.jsxs("span", { className: "availability-activity-title-line", children: [
                  /* @__PURE__ */ r.jsx("strong", { children: E }),
                  k.category && /* @__PURE__ */ r.jsx("span", { className: "availability-activity-category", children: k.category })
                ] }),
                P && /* @__PURE__ */ r.jsx("span", { className: "availability-activity-description", children: P }),
                $ && /* @__PURE__ */ r.jsx("span", { className: "availability-activity-meta", title: k.activityTypeKey ?? void 0, children: /* @__PURE__ */ r.jsx("code", { children: $ }) })
              ] }),
              /* @__PURE__ */ r.jsx("em", { className: `availability-state ${th(k.state)}`, children: Li(k.state) })
            ] }, _);
          })
        ] })
      ] }),
      w.length > 0 && /* @__PURE__ */ r.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ r.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ r.jsx(Ti, { size: 14 }),
          " Unresolved references"
        ] }),
        /* @__PURE__ */ r.jsx("div", { className: "availability-unresolved-list", children: w.map((k) => /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx("strong", { children: k.referenceName }),
          /* @__PURE__ */ r.jsx("em", { children: Li(k.state) })
        ] }, `${k.layer}-${k.referenceKind}-${k.referenceName}`)) })
      ] })
    ] })
  ] });
}
function Ie(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, i; n < e.length; n++)
      (i = Ie(e[n])) !== "" && (t += (t && " ") + i);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var dh = { value: () => {
} };
function no() {
  for (var e = 0, t = arguments.length, n = {}, i; e < t; ++e) {
    if (!(i = arguments[e] + "") || i in n || /[\s.]/.test(i)) throw new Error("illegal type: " + i);
    n[i] = [];
  }
  return new Ci(n);
}
function Ci(e) {
  this._ = e;
}
function fh(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var i = "", o = n.indexOf(".");
    if (o >= 0 && (i = n.slice(o + 1), n = n.slice(0, o)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: i };
  });
}
Ci.prototype = no.prototype = {
  constructor: Ci,
  on: function(e, t) {
    var n = this._, i = fh(e + "", n), o, s = -1, a = i.length;
    if (arguments.length < 2) {
      for (; ++s < a; ) if ((o = (e = i[s]).type) && (o = ph(n[o], e.name))) return o;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++s < a; )
      if (o = (e = i[s]).type) n[o] = Zs(n[o], e.name, t);
      else if (t == null) for (o in n) n[o] = Zs(n[o], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new Ci(e);
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
function ph(e, t) {
  for (var n = 0, i = e.length, o; n < i; ++n)
    if ((o = e[n]).name === t)
      return o.value;
}
function Zs(e, t, n) {
  for (var i = 0, o = e.length; i < o; ++i)
    if (e[i].name === t) {
      e[i] = dh, e = e.slice(0, i).concat(e.slice(i + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var vr = "http://www.w3.org/1999/xhtml";
const Gs = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: vr,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function io(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Gs.hasOwnProperty(t) ? { space: Gs[t], local: e } : e;
}
function hh(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === vr && t.documentElement.namespaceURI === vr ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function gh(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function xl(e) {
  var t = io(e);
  return (t.local ? gh : hh)(t);
}
function yh() {
}
function qr(e) {
  return e == null ? yh : function() {
    return this.querySelector(e);
  };
}
function mh(e) {
  typeof e != "function" && (e = qr(e));
  for (var t = this._groups, n = t.length, i = new Array(n), o = 0; o < n; ++o)
    for (var s = t[o], a = s.length, c = i[o] = new Array(a), u, l, d = 0; d < a; ++d)
      (u = s[d]) && (l = e.call(u, u.__data__, d, s)) && ("__data__" in u && (l.__data__ = u.__data__), c[d] = l);
  return new Be(i, this._parents);
}
function xh(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function wh() {
  return [];
}
function wl(e) {
  return e == null ? wh : function() {
    return this.querySelectorAll(e);
  };
}
function vh(e) {
  return function() {
    return xh(e.apply(this, arguments));
  };
}
function bh(e) {
  typeof e == "function" ? e = vh(e) : e = wl(e);
  for (var t = this._groups, n = t.length, i = [], o = [], s = 0; s < n; ++s)
    for (var a = t[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && (i.push(e.call(u, u.__data__, l, a)), o.push(u));
  return new Be(i, o);
}
function vl(e) {
  return function() {
    return this.matches(e);
  };
}
function bl(e) {
  return function(t) {
    return t.matches(e);
  };
}
var Nh = Array.prototype.find;
function jh(e) {
  return function() {
    return Nh.call(this.children, e);
  };
}
function Sh() {
  return this.firstElementChild;
}
function Ch(e) {
  return this.select(e == null ? Sh : jh(typeof e == "function" ? e : bl(e)));
}
var Eh = Array.prototype.filter;
function Ih() {
  return Array.from(this.children);
}
function kh(e) {
  return function() {
    return Eh.call(this.children, e);
  };
}
function Ah(e) {
  return this.selectAll(e == null ? Ih : kh(typeof e == "function" ? e : bl(e)));
}
function _h(e) {
  typeof e != "function" && (e = vl(e));
  for (var t = this._groups, n = t.length, i = new Array(n), o = 0; o < n; ++o)
    for (var s = t[o], a = s.length, c = i[o] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new Be(i, this._parents);
}
function Nl(e) {
  return new Array(e.length);
}
function Dh() {
  return new Be(this._enter || this._groups.map(Nl), this._parents);
}
function Oi(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
Oi.prototype = {
  constructor: Oi,
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
function $h(e) {
  return function() {
    return e;
  };
}
function Th(e, t, n, i, o, s) {
  for (var a = 0, c, u = t.length, l = s.length; a < l; ++a)
    (c = t[a]) ? (c.__data__ = s[a], i[a] = c) : n[a] = new Oi(e, s[a]);
  for (; a < u; ++a)
    (c = t[a]) && (o[a] = c);
}
function Ph(e, t, n, i, o, s, a) {
  var c, u, l = /* @__PURE__ */ new Map(), d = t.length, f = s.length, p = new Array(d), h;
  for (c = 0; c < d; ++c)
    (u = t[c]) && (p[c] = h = a.call(u, u.__data__, c, t) + "", l.has(h) ? o[c] = u : l.set(h, u));
  for (c = 0; c < f; ++c)
    h = a.call(e, s[c], c, s) + "", (u = l.get(h)) ? (i[c] = u, u.__data__ = s[c], l.delete(h)) : n[c] = new Oi(e, s[c]);
  for (c = 0; c < d; ++c)
    (u = t[c]) && l.get(p[c]) === u && (o[c] = u);
}
function Mh(e) {
  return e.__data__;
}
function Rh(e, t) {
  if (!arguments.length) return Array.from(this, Mh);
  var n = t ? Ph : Th, i = this._parents, o = this._groups;
  typeof e != "function" && (e = $h(e));
  for (var s = o.length, a = new Array(s), c = new Array(s), u = new Array(s), l = 0; l < s; ++l) {
    var d = i[l], f = o[l], p = f.length, h = zh(e.call(d, d && d.__data__, l, i)), y = h.length, w = c[l] = new Array(y), x = a[l] = new Array(y), m = u[l] = new Array(p);
    n(d, f, w, x, m, h, t);
    for (var b = 0, g = 0, v, j; b < y; ++b)
      if (v = w[b]) {
        for (b >= g && (g = b + 1); !(j = x[g]) && ++g < y; ) ;
        v._next = j || null;
      }
  }
  return a = new Be(a, i), a._enter = c, a._exit = u, a;
}
function zh(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function Lh() {
  return new Be(this._exit || this._groups.map(Nl), this._parents);
}
function Vh(e, t, n) {
  var i = this.enter(), o = this, s = this.exit();
  return typeof e == "function" ? (i = e(i), i && (i = i.selection())) : i = i.append(e + ""), t != null && (o = t(o), o && (o = o.selection())), n == null ? s.remove() : n(s), i && o ? i.merge(o).order() : o;
}
function Oh(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, i = t._groups, o = n.length, s = i.length, a = Math.min(o, s), c = new Array(o), u = 0; u < a; ++u)
    for (var l = n[u], d = i[u], f = l.length, p = c[u] = new Array(f), h, y = 0; y < f; ++y)
      (h = l[y] || d[y]) && (p[y] = h);
  for (; u < o; ++u)
    c[u] = n[u];
  return new Be(c, this._parents);
}
function Hh() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var i = e[t], o = i.length - 1, s = i[o], a; --o >= 0; )
      (a = i[o]) && (s && a.compareDocumentPosition(s) ^ 4 && s.parentNode.insertBefore(a, s), s = a);
  return this;
}
function Wh(e) {
  e || (e = Fh);
  function t(f, p) {
    return f && p ? e(f.__data__, p.__data__) : !f - !p;
  }
  for (var n = this._groups, i = n.length, o = new Array(i), s = 0; s < i; ++s) {
    for (var a = n[s], c = a.length, u = o[s] = new Array(c), l, d = 0; d < c; ++d)
      (l = a[d]) && (u[d] = l);
    u.sort(t);
  }
  return new Be(o, this._parents).order();
}
function Fh(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function Bh() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function Kh() {
  return Array.from(this);
}
function Xh() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var i = e[t], o = 0, s = i.length; o < s; ++o) {
      var a = i[o];
      if (a) return a;
    }
  return null;
}
function Yh() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function qh() {
  return !this.node();
}
function Uh(e) {
  for (var t = this._groups, n = 0, i = t.length; n < i; ++n)
    for (var o = t[n], s = 0, a = o.length, c; s < a; ++s)
      (c = o[s]) && e.call(c, c.__data__, s, o);
  return this;
}
function Zh(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Gh(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Jh(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function Qh(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function eg(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function tg(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function ng(e, t) {
  var n = io(e);
  if (arguments.length < 2) {
    var i = this.node();
    return n.local ? i.getAttributeNS(n.space, n.local) : i.getAttribute(n);
  }
  return this.each((t == null ? n.local ? Gh : Zh : typeof t == "function" ? n.local ? tg : eg : n.local ? Qh : Jh)(n, t));
}
function jl(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function ig(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function og(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function rg(e, t, n) {
  return function() {
    var i = t.apply(this, arguments);
    i == null ? this.style.removeProperty(e) : this.style.setProperty(e, i, n);
  };
}
function sg(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? ig : typeof t == "function" ? rg : og)(e, t, n ?? "")) : Jt(this.node(), e);
}
function Jt(e, t) {
  return e.style.getPropertyValue(t) || jl(e).getComputedStyle(e, null).getPropertyValue(t);
}
function ag(e) {
  return function() {
    delete this[e];
  };
}
function cg(e, t) {
  return function() {
    this[e] = t;
  };
}
function lg(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function ug(e, t) {
  return arguments.length > 1 ? this.each((t == null ? ag : typeof t == "function" ? lg : cg)(e, t)) : this.node()[e];
}
function Sl(e) {
  return e.trim().split(/^|\s+/);
}
function Ur(e) {
  return e.classList || new Cl(e);
}
function Cl(e) {
  this._node = e, this._names = Sl(e.getAttribute("class") || "");
}
Cl.prototype = {
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
function El(e, t) {
  for (var n = Ur(e), i = -1, o = t.length; ++i < o; ) n.add(t[i]);
}
function Il(e, t) {
  for (var n = Ur(e), i = -1, o = t.length; ++i < o; ) n.remove(t[i]);
}
function dg(e) {
  return function() {
    El(this, e);
  };
}
function fg(e) {
  return function() {
    Il(this, e);
  };
}
function pg(e, t) {
  return function() {
    (t.apply(this, arguments) ? El : Il)(this, e);
  };
}
function hg(e, t) {
  var n = Sl(e + "");
  if (arguments.length < 2) {
    for (var i = Ur(this.node()), o = -1, s = n.length; ++o < s; ) if (!i.contains(n[o])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? pg : t ? dg : fg)(n, t));
}
function gg() {
  this.textContent = "";
}
function yg(e) {
  return function() {
    this.textContent = e;
  };
}
function mg(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function xg(e) {
  return arguments.length ? this.each(e == null ? gg : (typeof e == "function" ? mg : yg)(e)) : this.node().textContent;
}
function wg() {
  this.innerHTML = "";
}
function vg(e) {
  return function() {
    this.innerHTML = e;
  };
}
function bg(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function Ng(e) {
  return arguments.length ? this.each(e == null ? wg : (typeof e == "function" ? bg : vg)(e)) : this.node().innerHTML;
}
function jg() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function Sg() {
  return this.each(jg);
}
function Cg() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Eg() {
  return this.each(Cg);
}
function Ig(e) {
  var t = typeof e == "function" ? e : xl(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function kg() {
  return null;
}
function Ag(e, t) {
  var n = typeof e == "function" ? e : xl(e), i = t == null ? kg : typeof t == "function" ? t : qr(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), i.apply(this, arguments) || null);
  });
}
function _g() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Dg() {
  return this.each(_g);
}
function $g() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Tg() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Pg(e) {
  return this.select(e ? Tg : $g);
}
function Mg(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function Rg(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function zg(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", i = t.indexOf(".");
    return i >= 0 && (n = t.slice(i + 1), t = t.slice(0, i)), { type: t, name: n };
  });
}
function Lg(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, i = -1, o = t.length, s; n < o; ++n)
        s = t[n], (!e.type || s.type === e.type) && s.name === e.name ? this.removeEventListener(s.type, s.listener, s.options) : t[++i] = s;
      ++i ? t.length = i : delete this.__on;
    }
  };
}
function Vg(e, t, n) {
  return function() {
    var i = this.__on, o, s = Rg(t);
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
function Og(e, t, n) {
  var i = zg(e + ""), o, s = i.length, a;
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
  for (c = t ? Vg : Lg, o = 0; o < s; ++o) this.each(c(i[o], t, n));
  return this;
}
function kl(e, t, n) {
  var i = jl(e), o = i.CustomEvent;
  typeof o == "function" ? o = new o(t, n) : (o = i.document.createEvent("Event"), n ? (o.initEvent(t, n.bubbles, n.cancelable), o.detail = n.detail) : o.initEvent(t, !1, !1)), e.dispatchEvent(o);
}
function Hg(e, t) {
  return function() {
    return kl(this, e, t);
  };
}
function Wg(e, t) {
  return function() {
    return kl(this, e, t.apply(this, arguments));
  };
}
function Fg(e, t) {
  return this.each((typeof t == "function" ? Wg : Hg)(e, t));
}
function* Bg() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var i = e[t], o = 0, s = i.length, a; o < s; ++o)
      (a = i[o]) && (yield a);
}
var Al = [null];
function Be(e, t) {
  this._groups = e, this._parents = t;
}
function Yn() {
  return new Be([[document.documentElement]], Al);
}
function Kg() {
  return this;
}
Be.prototype = Yn.prototype = {
  constructor: Be,
  select: mh,
  selectAll: bh,
  selectChild: Ch,
  selectChildren: Ah,
  filter: _h,
  data: Rh,
  enter: Dh,
  exit: Lh,
  join: Vh,
  merge: Oh,
  selection: Kg,
  order: Hh,
  sort: Wh,
  call: Bh,
  nodes: Kh,
  node: Xh,
  size: Yh,
  empty: qh,
  each: Uh,
  attr: ng,
  style: sg,
  property: ug,
  classed: hg,
  text: xg,
  html: Ng,
  raise: Sg,
  lower: Eg,
  append: Ig,
  insert: Ag,
  remove: Dg,
  clone: Pg,
  datum: Mg,
  on: Og,
  dispatch: Fg,
  [Symbol.iterator]: Bg
};
function We(e) {
  return typeof e == "string" ? new Be([[document.querySelector(e)]], [document.documentElement]) : new Be([[e]], Al);
}
function Xg(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function qe(e, t) {
  if (e = Xg(e), t === void 0 && (t = e.currentTarget), t) {
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
const Yg = { passive: !1 }, Tn = { capture: !0, passive: !1 };
function er(e) {
  e.stopImmediatePropagation();
}
function qt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function _l(e) {
  var t = e.document.documentElement, n = We(e).on("dragstart.drag", qt, Tn);
  "onselectstart" in t ? n.on("selectstart.drag", qt, Tn) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function Dl(e, t) {
  var n = e.document.documentElement, i = We(e).on("dragstart.drag", null);
  t && (i.on("click.drag", qt, Tn), setTimeout(function() {
    i.on("click.drag", null);
  }, 0)), "onselectstart" in n ? i.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const di = (e) => () => e;
function br(e, {
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
br.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function qg(e) {
  return !e.ctrlKey && !e.button;
}
function Ug() {
  return this.parentNode;
}
function Zg(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function Gg() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function $l() {
  var e = qg, t = Ug, n = Zg, i = Gg, o = {}, s = no("start", "drag", "end"), a = 0, c, u, l, d, f = 0;
  function p(v) {
    v.on("mousedown.drag", h).filter(i).on("touchstart.drag", x).on("touchmove.drag", m, Yg).on("touchend.drag touchcancel.drag", b).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function h(v, j) {
    if (!(d || !e.call(this, v, j))) {
      var N = g(this, t.call(this, v, j), v, j, "mouse");
      N && (We(v.view).on("mousemove.drag", y, Tn).on("mouseup.drag", w, Tn), _l(v.view), er(v), l = !1, c = v.clientX, u = v.clientY, N("start", v));
    }
  }
  function y(v) {
    if (qt(v), !l) {
      var j = v.clientX - c, N = v.clientY - u;
      l = j * j + N * N > f;
    }
    o.mouse("drag", v);
  }
  function w(v) {
    We(v.view).on("mousemove.drag mouseup.drag", null), Dl(v.view, l), qt(v), o.mouse("end", v);
  }
  function x(v, j) {
    if (e.call(this, v, j)) {
      var N = v.changedTouches, S = t.call(this, v, j), I = N.length, D, M;
      for (D = 0; D < I; ++D)
        (M = g(this, S, v, j, N[D].identifier, N[D])) && (er(v), M("start", v, N[D]));
    }
  }
  function m(v) {
    var j = v.changedTouches, N = j.length, S, I;
    for (S = 0; S < N; ++S)
      (I = o[j[S].identifier]) && (qt(v), I("drag", v, j[S]));
  }
  function b(v) {
    var j = v.changedTouches, N = j.length, S, I;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), S = 0; S < N; ++S)
      (I = o[j[S].identifier]) && (er(v), I("end", v, j[S]));
  }
  function g(v, j, N, S, I, D) {
    var M = s.copy(), A = qe(D || N, j), k, R, C;
    if ((C = n.call(v, new br("beforestart", {
      sourceEvent: N,
      target: p,
      identifier: I,
      active: a,
      x: A[0],
      y: A[1],
      dx: 0,
      dy: 0,
      dispatch: M
    }), S)) != null)
      return k = C.x - A[0] || 0, R = C.y - A[1] || 0, function _(E, $, P) {
        var T = A, F;
        switch (E) {
          case "start":
            o[I] = _, F = a++;
            break;
          case "end":
            delete o[I], --a;
          // falls through
          case "drag":
            A = qe(P || $, j), F = a;
            break;
        }
        M.call(
          E,
          v,
          new br(E, {
            sourceEvent: $,
            subject: C,
            target: p,
            identifier: I,
            active: F,
            x: A[0] + k,
            y: A[1] + R,
            dx: A[0] - T[0],
            dy: A[1] - T[1],
            dispatch: M
          }),
          S
        );
      };
  }
  return p.filter = function(v) {
    return arguments.length ? (e = typeof v == "function" ? v : di(!!v), p) : e;
  }, p.container = function(v) {
    return arguments.length ? (t = typeof v == "function" ? v : di(v), p) : t;
  }, p.subject = function(v) {
    return arguments.length ? (n = typeof v == "function" ? v : di(v), p) : n;
  }, p.touchable = function(v) {
    return arguments.length ? (i = typeof v == "function" ? v : di(!!v), p) : i;
  }, p.on = function() {
    var v = s.on.apply(s, arguments);
    return v === s ? p : v;
  }, p.clickDistance = function(v) {
    return arguments.length ? (f = (v = +v) * v, p) : Math.sqrt(f);
  }, p;
}
function Zr(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function Tl(e, t) {
  var n = Object.create(e.prototype);
  for (var i in t) n[i] = t[i];
  return n;
}
function qn() {
}
var Pn = 0.7, Hi = 1 / Pn, Ut = "\\s*([+-]?\\d+)\\s*", Mn = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", et = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", Jg = /^#([0-9a-f]{3,8})$/, Qg = new RegExp(`^rgb\\(${Ut},${Ut},${Ut}\\)$`), ey = new RegExp(`^rgb\\(${et},${et},${et}\\)$`), ty = new RegExp(`^rgba\\(${Ut},${Ut},${Ut},${Mn}\\)$`), ny = new RegExp(`^rgba\\(${et},${et},${et},${Mn}\\)$`), iy = new RegExp(`^hsl\\(${Mn},${et},${et}\\)$`), oy = new RegExp(`^hsla\\(${Mn},${et},${et},${Mn}\\)$`), Js = {
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
Zr(qn, It, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Qs,
  // Deprecated! Use color.formatHex.
  formatHex: Qs,
  formatHex8: ry,
  formatHsl: sy,
  formatRgb: ea,
  toString: ea
});
function Qs() {
  return this.rgb().formatHex();
}
function ry() {
  return this.rgb().formatHex8();
}
function sy() {
  return Pl(this).formatHsl();
}
function ea() {
  return this.rgb().formatRgb();
}
function It(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = Jg.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? ta(t) : n === 3 ? new Ve(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? fi(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? fi(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = Qg.exec(e)) ? new Ve(t[1], t[2], t[3], 1) : (t = ey.exec(e)) ? new Ve(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = ty.exec(e)) ? fi(t[1], t[2], t[3], t[4]) : (t = ny.exec(e)) ? fi(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = iy.exec(e)) ? oa(t[1], t[2] / 100, t[3] / 100, 1) : (t = oy.exec(e)) ? oa(t[1], t[2] / 100, t[3] / 100, t[4]) : Js.hasOwnProperty(e) ? ta(Js[e]) : e === "transparent" ? new Ve(NaN, NaN, NaN, 0) : null;
}
function ta(e) {
  return new Ve(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function fi(e, t, n, i) {
  return i <= 0 && (e = t = n = NaN), new Ve(e, t, n, i);
}
function ay(e) {
  return e instanceof qn || (e = It(e)), e ? (e = e.rgb(), new Ve(e.r, e.g, e.b, e.opacity)) : new Ve();
}
function Nr(e, t, n, i) {
  return arguments.length === 1 ? ay(e) : new Ve(e, t, n, i ?? 1);
}
function Ve(e, t, n, i) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +i;
}
Zr(Ve, Nr, Tl(qn, {
  brighter(e) {
    return e = e == null ? Hi : Math.pow(Hi, e), new Ve(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Pn : Math.pow(Pn, e), new Ve(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new Ve(St(this.r), St(this.g), St(this.b), Wi(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: na,
  // Deprecated! Use color.formatHex.
  formatHex: na,
  formatHex8: cy,
  formatRgb: ia,
  toString: ia
}));
function na() {
  return `#${Nt(this.r)}${Nt(this.g)}${Nt(this.b)}`;
}
function cy() {
  return `#${Nt(this.r)}${Nt(this.g)}${Nt(this.b)}${Nt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function ia() {
  const e = Wi(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${St(this.r)}, ${St(this.g)}, ${St(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Wi(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function St(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function Nt(e) {
  return e = St(e), (e < 16 ? "0" : "") + e.toString(16);
}
function oa(e, t, n, i) {
  return i <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Ue(e, t, n, i);
}
function Pl(e) {
  if (e instanceof Ue) return new Ue(e.h, e.s, e.l, e.opacity);
  if (e instanceof qn || (e = It(e)), !e) return new Ue();
  if (e instanceof Ue) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, i = e.b / 255, o = Math.min(t, n, i), s = Math.max(t, n, i), a = NaN, c = s - o, u = (s + o) / 2;
  return c ? (t === s ? a = (n - i) / c + (n < i) * 6 : n === s ? a = (i - t) / c + 2 : a = (t - n) / c + 4, c /= u < 0.5 ? s + o : 2 - s - o, a *= 60) : c = u > 0 && u < 1 ? 0 : a, new Ue(a, c, u, e.opacity);
}
function ly(e, t, n, i) {
  return arguments.length === 1 ? Pl(e) : new Ue(e, t, n, i ?? 1);
}
function Ue(e, t, n, i) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +i;
}
Zr(Ue, ly, Tl(qn, {
  brighter(e) {
    return e = e == null ? Hi : Math.pow(Hi, e), new Ue(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Pn : Math.pow(Pn, e), new Ue(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, i = n + (n < 0.5 ? n : 1 - n) * t, o = 2 * n - i;
    return new Ve(
      tr(e >= 240 ? e - 240 : e + 120, o, i),
      tr(e, o, i),
      tr(e < 120 ? e + 240 : e - 120, o, i),
      this.opacity
    );
  },
  clamp() {
    return new Ue(ra(this.h), pi(this.s), pi(this.l), Wi(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Wi(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${ra(this.h)}, ${pi(this.s) * 100}%, ${pi(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function ra(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function pi(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function tr(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const Gr = (e) => () => e;
function uy(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function dy(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(i) {
    return Math.pow(e + i * t, n);
  };
}
function fy(e) {
  return (e = +e) == 1 ? Ml : function(t, n) {
    return n - t ? dy(t, n, e) : Gr(isNaN(t) ? n : t);
  };
}
function Ml(e, t) {
  var n = t - e;
  return n ? uy(e, n) : Gr(isNaN(e) ? t : e);
}
const Fi = (function e(t) {
  var n = fy(t);
  function i(o, s) {
    var a = n((o = Nr(o)).r, (s = Nr(s)).r), c = n(o.g, s.g), u = n(o.b, s.b), l = Ml(o.opacity, s.opacity);
    return function(d) {
      return o.r = a(d), o.g = c(d), o.b = u(d), o.opacity = l(d), o + "";
    };
  }
  return i.gamma = e, i;
})(1);
function py(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, i = t.slice(), o;
  return function(s) {
    for (o = 0; o < n; ++o) i[o] = e[o] * (1 - s) + t[o] * s;
    return i;
  };
}
function hy(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function gy(e, t) {
  var n = t ? t.length : 0, i = e ? Math.min(n, e.length) : 0, o = new Array(i), s = new Array(n), a;
  for (a = 0; a < i; ++a) o[a] = In(e[a], t[a]);
  for (; a < n; ++a) s[a] = t[a];
  return function(c) {
    for (a = 0; a < i; ++a) s[a] = o[a](c);
    return s;
  };
}
function yy(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(i) {
    return n.setTime(e * (1 - i) + t * i), n;
  };
}
function Qe(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function my(e, t) {
  var n = {}, i = {}, o;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (o in t)
    o in e ? n[o] = In(e[o], t[o]) : i[o] = t[o];
  return function(s) {
    for (o in n) i[o] = n[o](s);
    return i;
  };
}
var jr = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, nr = new RegExp(jr.source, "g");
function xy(e) {
  return function() {
    return e;
  };
}
function wy(e) {
  return function(t) {
    return e(t) + "";
  };
}
function Rl(e, t) {
  var n = jr.lastIndex = nr.lastIndex = 0, i, o, s, a = -1, c = [], u = [];
  for (e = e + "", t = t + ""; (i = jr.exec(e)) && (o = nr.exec(t)); )
    (s = o.index) > n && (s = t.slice(n, s), c[a] ? c[a] += s : c[++a] = s), (i = i[0]) === (o = o[0]) ? c[a] ? c[a] += o : c[++a] = o : (c[++a] = null, u.push({ i: a, x: Qe(i, o) })), n = nr.lastIndex;
  return n < t.length && (s = t.slice(n), c[a] ? c[a] += s : c[++a] = s), c.length < 2 ? u[0] ? wy(u[0].x) : xy(t) : (t = u.length, function(l) {
    for (var d = 0, f; d < t; ++d) c[(f = u[d]).i] = f.x(l);
    return c.join("");
  });
}
function In(e, t) {
  var n = typeof t, i;
  return t == null || n === "boolean" ? Gr(t) : (n === "number" ? Qe : n === "string" ? (i = It(t)) ? (t = i, Fi) : Rl : t instanceof It ? Fi : t instanceof Date ? yy : hy(t) ? py : Array.isArray(t) ? gy : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? my : Qe)(e, t);
}
var sa = 180 / Math.PI, Sr = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function zl(e, t, n, i, o, s) {
  var a, c, u;
  return (a = Math.sqrt(e * e + t * t)) && (e /= a, t /= a), (u = e * n + t * i) && (n -= e * u, i -= t * u), (c = Math.sqrt(n * n + i * i)) && (n /= c, i /= c, u /= c), e * i < t * n && (e = -e, t = -t, u = -u, a = -a), {
    translateX: o,
    translateY: s,
    rotate: Math.atan2(t, e) * sa,
    skewX: Math.atan(u) * sa,
    scaleX: a,
    scaleY: c
  };
}
var hi;
function vy(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Sr : zl(t.a, t.b, t.c, t.d, t.e, t.f);
}
function by(e) {
  return e == null || (hi || (hi = document.createElementNS("http://www.w3.org/2000/svg", "g")), hi.setAttribute("transform", e), !(e = hi.transform.baseVal.consolidate())) ? Sr : (e = e.matrix, zl(e.a, e.b, e.c, e.d, e.e, e.f));
}
function Ll(e, t, n, i) {
  function o(l) {
    return l.length ? l.pop() + " " : "";
  }
  function s(l, d, f, p, h, y) {
    if (l !== f || d !== p) {
      var w = h.push("translate(", null, t, null, n);
      y.push({ i: w - 4, x: Qe(l, f) }, { i: w - 2, x: Qe(d, p) });
    } else (f || p) && h.push("translate(" + f + t + p + n);
  }
  function a(l, d, f, p) {
    l !== d ? (l - d > 180 ? d += 360 : d - l > 180 && (l += 360), p.push({ i: f.push(o(f) + "rotate(", null, i) - 2, x: Qe(l, d) })) : d && f.push(o(f) + "rotate(" + d + i);
  }
  function c(l, d, f, p) {
    l !== d ? p.push({ i: f.push(o(f) + "skewX(", null, i) - 2, x: Qe(l, d) }) : d && f.push(o(f) + "skewX(" + d + i);
  }
  function u(l, d, f, p, h, y) {
    if (l !== f || d !== p) {
      var w = h.push(o(h) + "scale(", null, ",", null, ")");
      y.push({ i: w - 4, x: Qe(l, f) }, { i: w - 2, x: Qe(d, p) });
    } else (f !== 1 || p !== 1) && h.push(o(h) + "scale(" + f + "," + p + ")");
  }
  return function(l, d) {
    var f = [], p = [];
    return l = e(l), d = e(d), s(l.translateX, l.translateY, d.translateX, d.translateY, f, p), a(l.rotate, d.rotate, f, p), c(l.skewX, d.skewX, f, p), u(l.scaleX, l.scaleY, d.scaleX, d.scaleY, f, p), l = d = null, function(h) {
      for (var y = -1, w = p.length, x; ++y < w; ) f[(x = p[y]).i] = x.x(h);
      return f.join("");
    };
  };
}
var Ny = Ll(vy, "px, ", "px)", "deg)"), jy = Ll(by, ", ", ")", ")"), Sy = 1e-12;
function aa(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Cy(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Ey(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const Ei = (function e(t, n, i) {
  function o(s, a) {
    var c = s[0], u = s[1], l = s[2], d = a[0], f = a[1], p = a[2], h = d - c, y = f - u, w = h * h + y * y, x, m;
    if (w < Sy)
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
        var I = S * m, D = aa(j), M = l / (n * b) * (D * Ey(t * I + j) - Cy(j));
        return [
          c + M * h,
          u + M * y,
          l * D / aa(t * I + j)
        ];
      };
    }
    return x.duration = m * 1e3 * t / Math.SQRT2, x;
  }
  return o.rho = function(s) {
    var a = Math.max(1e-3, +s), c = a * a, u = c * c;
    return e(a, c, u);
  }, o;
})(Math.SQRT2, 2, 4);
var Qt = 0, wn = 0, gn = 0, Vl = 1e3, Bi, vn, Ki = 0, kt = 0, oo = 0, Rn = typeof performance == "object" && performance.now ? performance : Date, Ol = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function Jr() {
  return kt || (Ol(Iy), kt = Rn.now() + oo);
}
function Iy() {
  kt = 0;
}
function Xi() {
  this._call = this._time = this._next = null;
}
Xi.prototype = Hl.prototype = {
  constructor: Xi,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? Jr() : +n) + (t == null ? 0 : +t), !this._next && vn !== this && (vn ? vn._next = this : Bi = this, vn = this), this._call = e, this._time = n, Cr();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Cr());
  }
};
function Hl(e, t, n) {
  var i = new Xi();
  return i.restart(e, t, n), i;
}
function ky() {
  Jr(), ++Qt;
  for (var e = Bi, t; e; )
    (t = kt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --Qt;
}
function ca() {
  kt = (Ki = Rn.now()) + oo, Qt = wn = 0;
  try {
    ky();
  } finally {
    Qt = 0, _y(), kt = 0;
  }
}
function Ay() {
  var e = Rn.now(), t = e - Ki;
  t > Vl && (oo -= t, Ki = e);
}
function _y() {
  for (var e, t = Bi, n, i = 1 / 0; t; )
    t._call ? (i > t._time && (i = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Bi = n);
  vn = e, Cr(i);
}
function Cr(e) {
  if (!Qt) {
    wn && (wn = clearTimeout(wn));
    var t = e - kt;
    t > 24 ? (e < 1 / 0 && (wn = setTimeout(ca, e - Rn.now() - oo)), gn && (gn = clearInterval(gn))) : (gn || (Ki = Rn.now(), gn = setInterval(Ay, Vl)), Qt = 1, Ol(ca));
  }
}
function la(e, t, n) {
  var i = new Xi();
  return t = t == null ? 0 : +t, i.restart((o) => {
    i.stop(), e(o + t);
  }, t, n), i;
}
var Dy = no("start", "end", "cancel", "interrupt"), $y = [], Wl = 0, ua = 1, Er = 2, Ii = 3, da = 4, Ir = 5, ki = 6;
function ro(e, t, n, i, o, s) {
  var a = e.__transition;
  if (!a) e.__transition = {};
  else if (n in a) return;
  Ty(e, n, {
    name: t,
    index: i,
    // For context during callback.
    group: o,
    // For context during callback.
    on: Dy,
    tween: $y,
    time: s.time,
    delay: s.delay,
    duration: s.duration,
    ease: s.ease,
    timer: null,
    state: Wl
  });
}
function Qr(e, t) {
  var n = Je(e, t);
  if (n.state > Wl) throw new Error("too late; already scheduled");
  return n;
}
function ot(e, t) {
  var n = Je(e, t);
  if (n.state > Ii) throw new Error("too late; already running");
  return n;
}
function Je(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function Ty(e, t, n) {
  var i = e.__transition, o;
  i[t] = n, n.timer = Hl(s, 0, n.time);
  function s(l) {
    n.state = ua, n.timer.restart(a, n.delay, n.time), n.delay <= l && a(l - n.delay);
  }
  function a(l) {
    var d, f, p, h;
    if (n.state !== ua) return u();
    for (d in i)
      if (h = i[d], h.name === n.name) {
        if (h.state === Ii) return la(a);
        h.state === da ? (h.state = ki, h.timer.stop(), h.on.call("interrupt", e, e.__data__, h.index, h.group), delete i[d]) : +d < t && (h.state = ki, h.timer.stop(), h.on.call("cancel", e, e.__data__, h.index, h.group), delete i[d]);
      }
    if (la(function() {
      n.state === Ii && (n.state = da, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = Er, n.on.call("start", e, e.__data__, n.index, n.group), n.state === Er) {
      for (n.state = Ii, o = new Array(p = n.tween.length), d = 0, f = -1; d < p; ++d)
        (h = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (o[++f] = h);
      o.length = f + 1;
    }
  }
  function c(l) {
    for (var d = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(u), n.state = Ir, 1), f = -1, p = o.length; ++f < p; )
      o[f].call(e, d);
    n.state === Ir && (n.on.call("end", e, e.__data__, n.index, n.group), u());
  }
  function u() {
    n.state = ki, n.timer.stop(), delete i[t];
    for (var l in i) return;
    delete e.__transition;
  }
}
function Ai(e, t) {
  var n = e.__transition, i, o, s = !0, a;
  if (n) {
    t = t == null ? null : t + "";
    for (a in n) {
      if ((i = n[a]).name !== t) {
        s = !1;
        continue;
      }
      o = i.state > Er && i.state < Ir, i.state = ki, i.timer.stop(), i.on.call(o ? "interrupt" : "cancel", e, e.__data__, i.index, i.group), delete n[a];
    }
    s && delete e.__transition;
  }
}
function Py(e) {
  return this.each(function() {
    Ai(this, e);
  });
}
function My(e, t) {
  var n, i;
  return function() {
    var o = ot(this, e), s = o.tween;
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
function Ry(e, t, n) {
  var i, o;
  if (typeof n != "function") throw new Error();
  return function() {
    var s = ot(this, e), a = s.tween;
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
function zy(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var i = Je(this.node(), n).tween, o = 0, s = i.length, a; o < s; ++o)
      if ((a = i[o]).name === e)
        return a.value;
    return null;
  }
  return this.each((t == null ? My : Ry)(n, e, t));
}
function es(e, t, n) {
  var i = e._id;
  return e.each(function() {
    var o = ot(this, i);
    (o.value || (o.value = {}))[t] = n.apply(this, arguments);
  }), function(o) {
    return Je(o, i).value[t];
  };
}
function Fl(e, t) {
  var n;
  return (typeof t == "number" ? Qe : t instanceof It ? Fi : (n = It(t)) ? (t = n, Fi) : Rl)(e, t);
}
function Ly(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Vy(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Oy(e, t, n) {
  var i, o = n + "", s;
  return function() {
    var a = this.getAttribute(e);
    return a === o ? null : a === i ? s : s = t(i = a, n);
  };
}
function Hy(e, t, n) {
  var i, o = n + "", s;
  return function() {
    var a = this.getAttributeNS(e.space, e.local);
    return a === o ? null : a === i ? s : s = t(i = a, n);
  };
}
function Wy(e, t, n) {
  var i, o, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttribute(e) : (a = this.getAttribute(e), u = c + "", a === u ? null : a === i && u === o ? s : (o = u, s = t(i = a, c)));
  };
}
function Fy(e, t, n) {
  var i, o, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (a = this.getAttributeNS(e.space, e.local), u = c + "", a === u ? null : a === i && u === o ? s : (o = u, s = t(i = a, c)));
  };
}
function By(e, t) {
  var n = io(e), i = n === "transform" ? jy : Fl;
  return this.attrTween(e, typeof t == "function" ? (n.local ? Fy : Wy)(n, i, es(this, "attr." + e, t)) : t == null ? (n.local ? Vy : Ly)(n) : (n.local ? Hy : Oy)(n, i, t));
}
function Ky(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function Xy(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function Yy(e, t) {
  var n, i;
  function o() {
    var s = t.apply(this, arguments);
    return s !== i && (n = (i = s) && Xy(e, s)), n;
  }
  return o._value = t, o;
}
function qy(e, t) {
  var n, i;
  function o() {
    var s = t.apply(this, arguments);
    return s !== i && (n = (i = s) && Ky(e, s)), n;
  }
  return o._value = t, o;
}
function Uy(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var i = io(e);
  return this.tween(n, (i.local ? Yy : qy)(i, t));
}
function Zy(e, t) {
  return function() {
    Qr(this, e).delay = +t.apply(this, arguments);
  };
}
function Gy(e, t) {
  return t = +t, function() {
    Qr(this, e).delay = t;
  };
}
function Jy(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Zy : Gy)(t, e)) : Je(this.node(), t).delay;
}
function Qy(e, t) {
  return function() {
    ot(this, e).duration = +t.apply(this, arguments);
  };
}
function em(e, t) {
  return t = +t, function() {
    ot(this, e).duration = t;
  };
}
function tm(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Qy : em)(t, e)) : Je(this.node(), t).duration;
}
function nm(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    ot(this, e).ease = t;
  };
}
function im(e) {
  var t = this._id;
  return arguments.length ? this.each(nm(t, e)) : Je(this.node(), t).ease;
}
function om(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    ot(this, e).ease = n;
  };
}
function rm(e) {
  if (typeof e != "function") throw new Error();
  return this.each(om(this._id, e));
}
function sm(e) {
  typeof e != "function" && (e = vl(e));
  for (var t = this._groups, n = t.length, i = new Array(n), o = 0; o < n; ++o)
    for (var s = t[o], a = s.length, c = i[o] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new ut(i, this._parents, this._name, this._id);
}
function am(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, i = t.length, o = n.length, s = Math.min(i, o), a = new Array(i), c = 0; c < s; ++c)
    for (var u = t[c], l = n[c], d = u.length, f = a[c] = new Array(d), p, h = 0; h < d; ++h)
      (p = u[h] || l[h]) && (f[h] = p);
  for (; c < i; ++c)
    a[c] = t[c];
  return new ut(a, this._parents, this._name, this._id);
}
function cm(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function lm(e, t, n) {
  var i, o, s = cm(t) ? Qr : ot;
  return function() {
    var a = s(this, e), c = a.on;
    c !== i && (o = (i = c).copy()).on(t, n), a.on = o;
  };
}
function um(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Je(this.node(), n).on.on(e) : this.each(lm(n, e, t));
}
function dm(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function fm() {
  return this.on("end.remove", dm(this._id));
}
function pm(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = qr(e));
  for (var i = this._groups, o = i.length, s = new Array(o), a = 0; a < o; ++a)
    for (var c = i[a], u = c.length, l = s[a] = new Array(u), d, f, p = 0; p < u; ++p)
      (d = c[p]) && (f = e.call(d, d.__data__, p, c)) && ("__data__" in d && (f.__data__ = d.__data__), l[p] = f, ro(l[p], t, n, p, l, Je(d, n)));
  return new ut(s, this._parents, t, n);
}
function hm(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = wl(e));
  for (var i = this._groups, o = i.length, s = [], a = [], c = 0; c < o; ++c)
    for (var u = i[c], l = u.length, d, f = 0; f < l; ++f)
      if (d = u[f]) {
        for (var p = e.call(d, d.__data__, f, u), h, y = Je(d, n), w = 0, x = p.length; w < x; ++w)
          (h = p[w]) && ro(h, t, n, w, p, y);
        s.push(p), a.push(d);
      }
  return new ut(s, a, t, n);
}
var gm = Yn.prototype.constructor;
function ym() {
  return new gm(this._groups, this._parents);
}
function mm(e, t) {
  var n, i, o;
  return function() {
    var s = Jt(this, e), a = (this.style.removeProperty(e), Jt(this, e));
    return s === a ? null : s === n && a === i ? o : o = t(n = s, i = a);
  };
}
function Bl(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function xm(e, t, n) {
  var i, o = n + "", s;
  return function() {
    var a = Jt(this, e);
    return a === o ? null : a === i ? s : s = t(i = a, n);
  };
}
function wm(e, t, n) {
  var i, o, s;
  return function() {
    var a = Jt(this, e), c = n(this), u = c + "";
    return c == null && (u = c = (this.style.removeProperty(e), Jt(this, e))), a === u ? null : a === i && u === o ? s : (o = u, s = t(i = a, c));
  };
}
function vm(e, t) {
  var n, i, o, s = "style." + t, a = "end." + s, c;
  return function() {
    var u = ot(this, e), l = u.on, d = u.value[s] == null ? c || (c = Bl(t)) : void 0;
    (l !== n || o !== d) && (i = (n = l).copy()).on(a, o = d), u.on = i;
  };
}
function bm(e, t, n) {
  var i = (e += "") == "transform" ? Ny : Fl;
  return t == null ? this.styleTween(e, mm(e, i)).on("end.style." + e, Bl(e)) : typeof t == "function" ? this.styleTween(e, wm(e, i, es(this, "style." + e, t))).each(vm(this._id, e)) : this.styleTween(e, xm(e, i, t), n).on("end.style." + e, null);
}
function Nm(e, t, n) {
  return function(i) {
    this.style.setProperty(e, t.call(this, i), n);
  };
}
function jm(e, t, n) {
  var i, o;
  function s() {
    var a = t.apply(this, arguments);
    return a !== o && (i = (o = a) && Nm(e, a, n)), i;
  }
  return s._value = t, s;
}
function Sm(e, t, n) {
  var i = "style." + (e += "");
  if (arguments.length < 2) return (i = this.tween(i)) && i._value;
  if (t == null) return this.tween(i, null);
  if (typeof t != "function") throw new Error();
  return this.tween(i, jm(e, t, n ?? ""));
}
function Cm(e) {
  return function() {
    this.textContent = e;
  };
}
function Em(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function Im(e) {
  return this.tween("text", typeof e == "function" ? Em(es(this, "text", e)) : Cm(e == null ? "" : e + ""));
}
function km(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Am(e) {
  var t, n;
  function i() {
    var o = e.apply(this, arguments);
    return o !== n && (t = (n = o) && km(o)), t;
  }
  return i._value = e, i;
}
function _m(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, Am(e));
}
function Dm() {
  for (var e = this._name, t = this._id, n = Kl(), i = this._groups, o = i.length, s = 0; s < o; ++s)
    for (var a = i[s], c = a.length, u, l = 0; l < c; ++l)
      if (u = a[l]) {
        var d = Je(u, t);
        ro(u, e, n, l, a, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new ut(i, this._parents, e, n);
}
function $m() {
  var e, t, n = this, i = n._id, o = n.size();
  return new Promise(function(s, a) {
    var c = { value: a }, u = { value: function() {
      --o === 0 && s();
    } };
    n.each(function() {
      var l = ot(this, i), d = l.on;
      d !== e && (t = (e = d).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(u)), l.on = t;
    }), o === 0 && s();
  });
}
var Tm = 0;
function ut(e, t, n, i) {
  this._groups = e, this._parents = t, this._name = n, this._id = i;
}
function Kl() {
  return ++Tm;
}
var at = Yn.prototype;
ut.prototype = {
  constructor: ut,
  select: pm,
  selectAll: hm,
  selectChild: at.selectChild,
  selectChildren: at.selectChildren,
  filter: sm,
  merge: am,
  selection: ym,
  transition: Dm,
  call: at.call,
  nodes: at.nodes,
  node: at.node,
  size: at.size,
  empty: at.empty,
  each: at.each,
  on: um,
  attr: By,
  attrTween: Uy,
  style: bm,
  styleTween: Sm,
  text: Im,
  textTween: _m,
  remove: fm,
  tween: zy,
  delay: Jy,
  duration: tm,
  ease: im,
  easeVarying: rm,
  end: $m,
  [Symbol.iterator]: at[Symbol.iterator]
};
function Pm(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var Mm = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: Pm
};
function Rm(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function zm(e) {
  var t, n;
  e instanceof ut ? (t = e._id, e = e._name) : (t = Kl(), (n = Mm).time = Jr(), e = e == null ? null : e + "");
  for (var i = this._groups, o = i.length, s = 0; s < o; ++s)
    for (var a = i[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && ro(u, e, t, l, a, n || Rm(u, t));
  return new ut(i, this._parents, e, t);
}
Yn.prototype.interrupt = Py;
Yn.prototype.transition = zm;
const gi = (e) => () => e;
function Lm(e, {
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
function ct(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
ct.prototype = {
  constructor: ct,
  scale: function(e) {
    return e === 1 ? this : new ct(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new ct(this.k, this.x + this.k * e, this.y + this.k * t);
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
var so = new ct(1, 0, 0);
Xl.prototype = ct.prototype;
function Xl(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return so;
  return e.__zoom;
}
function ir(e) {
  e.stopImmediatePropagation();
}
function yn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Vm(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function Om() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function fa() {
  return this.__zoom || so;
}
function Hm(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function Wm() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Fm(e, t, n) {
  var i = e.invertX(t[0][0]) - n[0][0], o = e.invertX(t[1][0]) - n[1][0], s = e.invertY(t[0][1]) - n[0][1], a = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    o > i ? (i + o) / 2 : Math.min(0, i) || Math.max(0, o),
    a > s ? (s + a) / 2 : Math.min(0, s) || Math.max(0, a)
  );
}
function Yl() {
  var e = Vm, t = Om, n = Fm, i = Hm, o = Wm, s = [0, 1 / 0], a = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, u = Ei, l = no("start", "zoom", "end"), d, f, p, h = 500, y = 150, w = 0, x = 10;
  function m(C) {
    C.property("__zoom", fa).on("wheel.zoom", I, { passive: !1 }).on("mousedown.zoom", D).on("dblclick.zoom", M).filter(o).on("touchstart.zoom", A).on("touchmove.zoom", k).on("touchend.zoom touchcancel.zoom", R).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  m.transform = function(C, _, E, $) {
    var P = C.selection ? C.selection() : C;
    P.property("__zoom", fa), C !== P ? j(C, _, E, $) : P.interrupt().each(function() {
      N(this, arguments).event($).start().zoom(null, typeof _ == "function" ? _.apply(this, arguments) : _).end();
    });
  }, m.scaleBy = function(C, _, E, $) {
    m.scaleTo(C, function() {
      var P = this.__zoom.k, T = typeof _ == "function" ? _.apply(this, arguments) : _;
      return P * T;
    }, E, $);
  }, m.scaleTo = function(C, _, E, $) {
    m.transform(C, function() {
      var P = t.apply(this, arguments), T = this.__zoom, F = E == null ? v(P) : typeof E == "function" ? E.apply(this, arguments) : E, H = T.invert(F), O = typeof _ == "function" ? _.apply(this, arguments) : _;
      return n(g(b(T, O), F, H), P, a);
    }, E, $);
  }, m.translateBy = function(C, _, E, $) {
    m.transform(C, function() {
      return n(this.__zoom.translate(
        typeof _ == "function" ? _.apply(this, arguments) : _,
        typeof E == "function" ? E.apply(this, arguments) : E
      ), t.apply(this, arguments), a);
    }, null, $);
  }, m.translateTo = function(C, _, E, $, P) {
    m.transform(C, function() {
      var T = t.apply(this, arguments), F = this.__zoom, H = $ == null ? v(T) : typeof $ == "function" ? $.apply(this, arguments) : $;
      return n(so.translate(H[0], H[1]).scale(F.k).translate(
        typeof _ == "function" ? -_.apply(this, arguments) : -_,
        typeof E == "function" ? -E.apply(this, arguments) : -E
      ), T, a);
    }, $, P);
  };
  function b(C, _) {
    return _ = Math.max(s[0], Math.min(s[1], _)), _ === C.k ? C : new ct(_, C.x, C.y);
  }
  function g(C, _, E) {
    var $ = _[0] - E[0] * C.k, P = _[1] - E[1] * C.k;
    return $ === C.x && P === C.y ? C : new ct(C.k, $, P);
  }
  function v(C) {
    return [(+C[0][0] + +C[1][0]) / 2, (+C[0][1] + +C[1][1]) / 2];
  }
  function j(C, _, E, $) {
    C.on("start.zoom", function() {
      N(this, arguments).event($).start();
    }).on("interrupt.zoom end.zoom", function() {
      N(this, arguments).event($).end();
    }).tween("zoom", function() {
      var P = this, T = arguments, F = N(P, T).event($), H = t.apply(P, T), O = E == null ? v(H) : typeof E == "function" ? E.apply(P, T) : E, X = Math.max(H[1][0] - H[0][0], H[1][1] - H[0][1]), q = P.__zoom, ee = typeof _ == "function" ? _.apply(P, T) : _, le = u(q.invert(O).concat(X / q.k), ee.invert(O).concat(X / ee.k));
      return function(U) {
        if (U === 1) U = ee;
        else {
          var z = le(U), Y = X / z[2];
          U = new ct(Y, O[0] - z[0] * Y, O[1] - z[1] * Y);
        }
        F.zoom(null, U);
      };
    });
  }
  function N(C, _, E) {
    return !E && C.__zooming || new S(C, _);
  }
  function S(C, _) {
    this.that = C, this.args = _, this.active = 0, this.sourceEvent = null, this.extent = t.apply(C, _), this.taps = 0;
  }
  S.prototype = {
    event: function(C) {
      return C && (this.sourceEvent = C), this;
    },
    start: function() {
      return ++this.active === 1 && (this.that.__zooming = this, this.emit("start")), this;
    },
    zoom: function(C, _) {
      return this.mouse && C !== "mouse" && (this.mouse[1] = _.invert(this.mouse[0])), this.touch0 && C !== "touch" && (this.touch0[1] = _.invert(this.touch0[0])), this.touch1 && C !== "touch" && (this.touch1[1] = _.invert(this.touch1[0])), this.that.__zoom = _, this.emit("zoom"), this;
    },
    end: function() {
      return --this.active === 0 && (delete this.that.__zooming, this.emit("end")), this;
    },
    emit: function(C) {
      var _ = We(this.that).datum();
      l.call(
        C,
        this.that,
        new Lm(C, {
          sourceEvent: this.sourceEvent,
          target: m,
          transform: this.that.__zoom,
          dispatch: l
        }),
        _
      );
    }
  };
  function I(C, ..._) {
    if (!e.apply(this, arguments)) return;
    var E = N(this, _).event(C), $ = this.__zoom, P = Math.max(s[0], Math.min(s[1], $.k * Math.pow(2, i.apply(this, arguments)))), T = qe(C);
    if (E.wheel)
      (E.mouse[0][0] !== T[0] || E.mouse[0][1] !== T[1]) && (E.mouse[1] = $.invert(E.mouse[0] = T)), clearTimeout(E.wheel);
    else {
      if ($.k === P) return;
      E.mouse = [T, $.invert(T)], Ai(this), E.start();
    }
    yn(C), E.wheel = setTimeout(F, y), E.zoom("mouse", n(g(b($, P), E.mouse[0], E.mouse[1]), E.extent, a));
    function F() {
      E.wheel = null, E.end();
    }
  }
  function D(C, ..._) {
    if (p || !e.apply(this, arguments)) return;
    var E = C.currentTarget, $ = N(this, _, !0).event(C), P = We(C.view).on("mousemove.zoom", O, !0).on("mouseup.zoom", X, !0), T = qe(C, E), F = C.clientX, H = C.clientY;
    _l(C.view), ir(C), $.mouse = [T, this.__zoom.invert(T)], Ai(this), $.start();
    function O(q) {
      if (yn(q), !$.moved) {
        var ee = q.clientX - F, le = q.clientY - H;
        $.moved = ee * ee + le * le > w;
      }
      $.event(q).zoom("mouse", n(g($.that.__zoom, $.mouse[0] = qe(q, E), $.mouse[1]), $.extent, a));
    }
    function X(q) {
      P.on("mousemove.zoom mouseup.zoom", null), Dl(q.view, $.moved), yn(q), $.event(q).end();
    }
  }
  function M(C, ..._) {
    if (e.apply(this, arguments)) {
      var E = this.__zoom, $ = qe(C.changedTouches ? C.changedTouches[0] : C, this), P = E.invert($), T = E.k * (C.shiftKey ? 0.5 : 2), F = n(g(b(E, T), $, P), t.apply(this, _), a);
      yn(C), c > 0 ? We(this).transition().duration(c).call(j, F, $, C) : We(this).call(m.transform, F, $, C);
    }
  }
  function A(C, ..._) {
    if (e.apply(this, arguments)) {
      var E = C.touches, $ = E.length, P = N(this, _, C.changedTouches.length === $).event(C), T, F, H, O;
      for (ir(C), F = 0; F < $; ++F)
        H = E[F], O = qe(H, this), O = [O, this.__zoom.invert(O), H.identifier], P.touch0 ? !P.touch1 && P.touch0[2] !== O[2] && (P.touch1 = O, P.taps = 0) : (P.touch0 = O, T = !0, P.taps = 1 + !!d);
      d && (d = clearTimeout(d)), T && (P.taps < 2 && (f = O[0], d = setTimeout(function() {
        d = null;
      }, h)), Ai(this), P.start());
    }
  }
  function k(C, ..._) {
    if (this.__zooming) {
      var E = N(this, _).event(C), $ = C.changedTouches, P = $.length, T, F, H, O;
      for (yn(C), T = 0; T < P; ++T)
        F = $[T], H = qe(F, this), E.touch0 && E.touch0[2] === F.identifier ? E.touch0[0] = H : E.touch1 && E.touch1[2] === F.identifier && (E.touch1[0] = H);
      if (F = E.that.__zoom, E.touch1) {
        var X = E.touch0[0], q = E.touch0[1], ee = E.touch1[0], le = E.touch1[1], U = (U = ee[0] - X[0]) * U + (U = ee[1] - X[1]) * U, z = (z = le[0] - q[0]) * z + (z = le[1] - q[1]) * z;
        F = b(F, Math.sqrt(U / z)), H = [(X[0] + ee[0]) / 2, (X[1] + ee[1]) / 2], O = [(q[0] + le[0]) / 2, (q[1] + le[1]) / 2];
      } else if (E.touch0) H = E.touch0[0], O = E.touch0[1];
      else return;
      E.zoom("touch", n(g(F, H, O), E.extent, a));
    }
  }
  function R(C, ..._) {
    if (this.__zooming) {
      var E = N(this, _).event(C), $ = C.changedTouches, P = $.length, T, F;
      for (ir(C), p && clearTimeout(p), p = setTimeout(function() {
        p = null;
      }, h), T = 0; T < P; ++T)
        F = $[T], E.touch0 && E.touch0[2] === F.identifier ? delete E.touch0 : E.touch1 && E.touch1[2] === F.identifier && delete E.touch1;
      if (E.touch1 && !E.touch0 && (E.touch0 = E.touch1, delete E.touch1), E.touch0) E.touch0[1] = this.__zoom.invert(E.touch0[0]);
      else if (E.end(), E.taps === 2 && (F = qe(F, this), Math.hypot(f[0] - F[0], f[1] - F[1]) < x)) {
        var H = We(this).on("dblclick.zoom");
        H && H.apply(this, arguments);
      }
    }
  }
  return m.wheelDelta = function(C) {
    return arguments.length ? (i = typeof C == "function" ? C : gi(+C), m) : i;
  }, m.filter = function(C) {
    return arguments.length ? (e = typeof C == "function" ? C : gi(!!C), m) : e;
  }, m.touchable = function(C) {
    return arguments.length ? (o = typeof C == "function" ? C : gi(!!C), m) : o;
  }, m.extent = function(C) {
    return arguments.length ? (t = typeof C == "function" ? C : gi([[+C[0][0], +C[0][1]], [+C[1][0], +C[1][1]]]), m) : t;
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
}, zn = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], ql = ["Enter", " ", "Escape"], Ul = {
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
var en;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(en || (en = {}));
var Ct;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(Ct || (Ct = {}));
var Ln;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Ln || (Ln = {}));
const Zl = {
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
var gt;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(gt || (gt = {}));
var Yi;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(Yi || (Yi = {}));
var ie;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(ie || (ie = {}));
const pa = {
  [ie.Left]: ie.Right,
  [ie.Right]: ie.Left,
  [ie.Top]: ie.Bottom,
  [ie.Bottom]: ie.Top
};
function Gl(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Jl = (e) => "id" in e && "source" in e && "target" in e, Bm = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), ts = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), Un = (e, t = [0, 0]) => {
  const { width: n, height: i } = dt(e), o = e.origin ?? t, s = n * o[0], a = i * o[1];
  return {
    x: e.position.x - s,
    y: e.position.y - a
  };
}, Km = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((i, o) => {
    const s = typeof o == "string";
    let a = !t.nodeLookup && !s ? o : void 0;
    t.nodeLookup && (a = s ? t.nodeLookup.get(o) : ts(o) ? o : t.nodeLookup.get(o.id));
    const c = a ? qi(a, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return ao(i, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return co(n);
}, Zn = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, i = !1;
  return e.forEach((o) => {
    (t.filter === void 0 || t.filter(o)) && (n = ao(n, qi(o)), i = !0);
  }), i ? co(n) : { x: 0, y: 0, width: 0, height: 0 };
}, ns = (e, t, [n, i, o] = [0, 0, 1], s = !1, a = !1) => {
  const c = {
    ...ln(t, [n, i, o]),
    width: t.width / o,
    height: t.height / o
  }, u = [];
  for (const l of e.values()) {
    const { measured: d, selectable: f = !0, hidden: p = !1 } = l;
    if (a && !f || p)
      continue;
    const h = d.width ?? l.width ?? l.initialWidth ?? null, y = d.height ?? l.height ?? l.initialHeight ?? null, w = Vn(c, nn(l)), x = (h ?? 0) * (y ?? 0), m = s && w > 0;
    (!l.internals.handleBounds || m || w >= x || l.dragging) && u.push(l);
  }
  return u;
}, Xm = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((i) => {
    n.add(i.id);
  }), t.filter((i) => n.has(i.source) || n.has(i.target));
};
function Ym(e, t) {
  const n = /* @__PURE__ */ new Map(), i = t?.nodes ? new Set(t.nodes.map((o) => o.id)) : null;
  return e.forEach((o) => {
    o.measured.width && o.measured.height && (t?.includeHiddenNodes || !o.hidden) && (!i || i.has(o.id)) && n.set(o.id, o);
  }), n;
}
async function qm({ nodes: e, width: t, height: n, panZoom: i, minZoom: o, maxZoom: s }, a) {
  if (e.size === 0)
    return !0;
  const c = Ym(e, a), u = Zn(c), l = os(u, t, n, a?.minZoom ?? o, a?.maxZoom ?? s, a?.padding ?? 0.1);
  return await i.setViewport(l, {
    duration: a?.duration,
    ease: a?.ease,
    interpolate: a?.interpolate
  }), !0;
}
function Ql({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: i = [0, 0], nodeExtent: o, onError: s }) {
  const a = n.get(e), c = a.parentId ? n.get(a.parentId) : void 0, { x: u, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, d = a.origin ?? i;
  let f = a.extent || o;
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
  else c && _t(a.extent) && (f = [
    [a.extent[0][0] + u, a.extent[0][1] + l],
    [a.extent[1][0] + u, a.extent[1][1] + l]
  ]);
  const p = _t(f) ? At(t, f, a.measured) : t;
  return (a.measured.width === void 0 || a.measured.height === void 0) && s?.("015", Ke.error015()), {
    position: {
      x: p.x - u + (a.measured.width ?? 0) * d[0],
      y: p.y - l + (a.measured.height ?? 0) * d[1]
    },
    positionAbsolute: p
  };
}
async function Um({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: i, onBeforeDelete: o }) {
  const s = new Set(e.map((p) => p.id)), a = [];
  for (const p of n) {
    if (p.deletable === !1)
      continue;
    const h = s.has(p.id), y = !h && p.parentId && a.find((w) => w.id === p.parentId);
    (h || y) && a.push(p);
  }
  const c = new Set(t.map((p) => p.id)), u = i.filter((p) => p.deletable !== !1), d = Xm(a, u);
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
const tn = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), At = (e = { x: 0, y: 0 }, t, n) => ({
  x: tn(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: tn(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function eu(e, t, n) {
  const { width: i, height: o } = dt(n), { x: s, y: a } = n.internals.positionAbsolute;
  return At(e, [
    [s, a],
    [s + i, a + o]
  ], t);
}
const ha = (e, t, n) => e < t ? tn(Math.abs(e - t), 1, t) / t : e > n ? -tn(Math.abs(e - n), 1, t) / t : 0, is = (e, t, n = 15, i = 40) => {
  const o = ha(e.x, i, t.width - i) * n, s = ha(e.y, i, t.height - i) * n;
  return [o, s];
}, ao = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), kr = ({ x: e, y: t, width: n, height: i }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + i
}), co = ({ x: e, y: t, x2: n, y2: i }) => ({
  x: e,
  y: t,
  width: n - e,
  height: i - t
}), nn = (e, t = [0, 0]) => {
  const { x: n, y: i } = ts(e) ? e.internals.positionAbsolute : Un(e, t);
  return {
    x: n,
    y: i,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, qi = (e, t = [0, 0]) => {
  const { x: n, y: i } = ts(e) ? e.internals.positionAbsolute : Un(e, t);
  return {
    x: n,
    y: i,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: i + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, tu = (e, t) => co(ao(kr(e), kr(t))), Vn = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), i = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * i);
}, ga = (e) => Ze(e.width) && Ze(e.height) && Ze(e.x) && Ze(e.y), Ze = (e) => !isNaN(e) && isFinite(e), nu = (e, t) => (n, i) => {
}, Gn = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), ln = ({ x: e, y: t }, [n, i, o], s = !1, a = [1, 1]) => {
  const c = {
    x: (e - n) / o,
    y: (t - i) / o
  };
  return s ? Gn(c, a) : c;
}, on = ({ x: e, y: t }, [n, i, o]) => ({
  x: e * o + n,
  y: t * o + i
});
function Wt(e, t) {
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
function Zm(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const i = Wt(e, n), o = Wt(e, t);
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
    const i = Wt(e.top ?? e.y ?? 0, n), o = Wt(e.bottom ?? e.y ?? 0, n), s = Wt(e.left ?? e.x ?? 0, t), a = Wt(e.right ?? e.x ?? 0, t);
    return { top: i, right: a, bottom: o, left: s, x: s + a, y: i + o };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function Gm(e, t, n, i, o, s) {
  const { x: a, y: c } = on(e, [t, n, i]), { x: u, y: l } = on({ x: e.x + e.width, y: e.y + e.height }, [t, n, i]), d = o - u, f = s - l;
  return {
    left: Math.floor(a),
    top: Math.floor(c),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const os = (e, t, n, i, o, s) => {
  const a = Zm(s, t, n), c = (t - a.x) / e.width, u = (n - a.y) / e.height, l = Math.min(c, u), d = tn(l, i, o), f = e.x + e.width / 2, p = e.y + e.height / 2, h = t / 2 - f * d, y = n / 2 - p * d, w = Gm(e, h, y, d, t, n), x = {
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
}, On = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function _t(e) {
  return e != null && e !== "parent";
}
function dt(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function iu(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function ou(e, t = { width: 0, height: 0 }, n, i, o) {
  const s = { ...e }, a = i.get(n);
  if (a) {
    const c = a.origin || o;
    s.x += a.internals.positionAbsolute.x - (t.width ?? 0) * c[0], s.y += a.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return s;
}
function ya(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function Jm() {
  let e, t;
  return { promise: new Promise((i, o) => {
    e = i, t = o;
  }), resolve: e, reject: t };
}
function Qm(e) {
  return { ...Ul, ...e || {} };
}
function kn(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: i, containerBounds: o }) {
  const { x: s, y: a } = Ge(e), c = ln({ x: s - (o?.left ?? 0), y: a - (o?.top ?? 0) }, i), { x: u, y: l } = n ? Gn(c, t) : c;
  return {
    xSnapped: u,
    ySnapped: l,
    ...c
  };
}
const rs = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), ru = (e) => e?.getRootNode?.() || window?.document, ex = ["INPUT", "SELECT", "TEXTAREA"];
function su(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : ex.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const au = (e) => "clientX" in e, Ge = (e, t) => {
  const n = au(e), i = n ? e.clientX : e.touches?.[0].clientX, o = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: i - (t?.left ?? 0),
    y: o - (t?.top ?? 0)
  };
}, ma = (e, t, n, i, o) => {
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
      ...rs(a)
    };
  });
};
function cu({ sourceX: e, sourceY: t, targetX: n, targetY: i, sourceControlX: o, sourceControlY: s, targetControlX: a, targetControlY: c }) {
  const u = e * 0.125 + o * 0.375 + a * 0.375 + n * 0.125, l = t * 0.125 + s * 0.375 + c * 0.375 + i * 0.125, d = Math.abs(u - e), f = Math.abs(l - t);
  return [u, l, d, f];
}
function yi(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function xa({ pos: e, x1: t, y1: n, x2: i, y2: o, c: s }) {
  switch (e) {
    case ie.Left:
      return [t - yi(t - i, s), n];
    case ie.Right:
      return [t + yi(i - t, s), n];
    case ie.Top:
      return [t, n - yi(n - o, s)];
    case ie.Bottom:
      return [t, n + yi(o - n, s)];
  }
}
function lu({ sourceX: e, sourceY: t, sourcePosition: n = ie.Bottom, targetX: i, targetY: o, targetPosition: s = ie.Top, curvature: a = 0.25 }) {
  const [c, u] = xa({
    pos: n,
    x1: e,
    y1: t,
    x2: i,
    y2: o,
    c: a
  }), [l, d] = xa({
    pos: s,
    x1: i,
    y1: o,
    x2: e,
    y2: t,
    c: a
  }), [f, p, h, y] = cu({
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
function uu({ sourceX: e, sourceY: t, targetX: n, targetY: i }) {
  const o = Math.abs(n - e) / 2, s = n < e ? n + o : n - o, a = Math.abs(i - t) / 2, c = i < t ? i + a : i - a;
  return [s, c, o, a];
}
function tx({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: i = 0, elevateOnSelect: o = !1, zIndexMode: s = "basic" }) {
  if (s === "manual")
    return i;
  const a = o && n ? i + 1e3 : i, c = Math.max(e.parentId || o && e.selected ? e.internals.z : 0, t.parentId || o && t.selected ? t.internals.z : 0);
  return a + c;
}
function nx({ sourceNode: e, targetNode: t, width: n, height: i, transform: o }) {
  const s = ao(qi(e), qi(t));
  s.x === s.x2 && (s.x2 += 1), s.y === s.y2 && (s.y2 += 1);
  const a = {
    x: -o[0] / o[2],
    y: -o[1] / o[2],
    width: n / o[2],
    height: i / o[2]
  };
  return Vn(a, co(s)) > 0;
}
const du = ({ source: e, sourceHandle: t, target: n, targetHandle: i }) => `xy-edge__${e}${t || ""}-${n}${i || ""}`, ix = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), ox = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Ke.error006()), t;
  const i = n.getEdgeId || du;
  let o;
  return Jl(e) ? o = { ...e } : o = {
    ...e,
    id: i(e)
  }, ix(o, t) ? t : (o.sourceHandle === null && delete o.sourceHandle, o.targetHandle === null && delete o.targetHandle, t.concat(o));
}, rx = (e, t, n, i = { shouldReplaceId: !0 }) => {
  const { id: o, ...s } = e;
  if (!t.source || !t.target)
    return i.onError?.("006", Ke.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return i.onError?.("007", Ke.error007(o)), n;
  const c = i.getEdgeId || du, u = {
    ...s,
    id: i.shouldReplaceId ? c(t) : o,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== o).concat(u);
};
function fu({ sourceX: e, sourceY: t, targetX: n, targetY: i }) {
  const [o, s, a, c] = uu({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: i
  });
  return [`M ${e},${t}L ${n},${i}`, o, s, a, c];
}
const wa = {
  [ie.Left]: { x: -1, y: 0 },
  [ie.Right]: { x: 1, y: 0 },
  [ie.Top]: { x: 0, y: -1 },
  [ie.Bottom]: { x: 0, y: 1 }
}, sx = ({ source: e, sourcePosition: t = ie.Bottom, target: n }) => t === ie.Left || t === ie.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, va = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function ax({ source: e, sourcePosition: t = ie.Bottom, target: n, targetPosition: i = ie.Top, center: o, offset: s, stepPosition: a }) {
  const c = wa[t], u = wa[i], l = { x: e.x + c.x * s, y: e.y + c.y * s }, d = { x: n.x + u.x * s, y: n.y + u.y * s }, f = sx({
    source: l,
    sourcePosition: t,
    target: d
  }), p = f.x !== 0 ? "x" : "y", h = f[p];
  let y = [], w, x;
  const m = { x: 0, y: 0 }, b = { x: 0, y: 0 }, [, , g, v] = uu({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[p] * u[p] === -1) {
    p === "x" ? (w = o.x ?? l.x + (d.x - l.x) * a, x = o.y ?? (l.y + d.y) / 2) : (w = o.x ?? (l.x + d.x) / 2, x = o.y ?? l.y + (d.y - l.y) * a);
    const I = [
      { x: w, y: l.y },
      { x: w, y: d.y }
    ], D = [
      { x: l.x, y: x },
      { x: d.x, y: x }
    ];
    c[p] === h ? y = p === "x" ? I : D : y = p === "x" ? D : I;
  } else {
    const I = [{ x: l.x, y: d.y }], D = [{ x: d.x, y: l.y }];
    if (p === "x" ? y = c.x === h ? D : I : y = c.y === h ? I : D, t === i) {
      const C = Math.abs(e[p] - n[p]);
      if (C <= s) {
        const _ = Math.min(s - 1, s - C);
        c[p] === h ? m[p] = (l[p] > e[p] ? -1 : 1) * _ : b[p] = (d[p] > n[p] ? -1 : 1) * _;
      }
    }
    if (t !== i) {
      const C = p === "x" ? "y" : "x", _ = c[p] === u[C], E = l[C] > d[C], $ = l[C] < d[C];
      (c[p] === 1 && (!_ && E || _ && $) || c[p] !== 1 && (!_ && $ || _ && E)) && (y = p === "x" ? I : D);
    }
    const M = { x: l.x + m.x, y: l.y + m.y }, A = { x: d.x + b.x, y: d.y + b.y }, k = Math.max(Math.abs(M.x - y[0].x), Math.abs(A.x - y[0].x)), R = Math.max(Math.abs(M.y - y[0].y), Math.abs(A.y - y[0].y));
    k >= R ? (w = (M.x + A.x) / 2, x = y[0].y) : (w = y[0].x, x = (M.y + A.y) / 2);
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
function cx(e, t, n, i) {
  const o = Math.min(va(e, t) / 2, va(t, n) / 2, i), { x: s, y: a } = t;
  if (e.x === s && s === n.x || e.y === a && a === n.y)
    return `L${s} ${a}`;
  if (e.y === a) {
    const l = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${s + o * l},${a}Q ${s},${a} ${s},${a + o * d}`;
  }
  const c = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${s},${a + o * u}Q ${s},${a} ${s + o * c},${a}`;
}
function Ui({ sourceX: e, sourceY: t, sourcePosition: n = ie.Bottom, targetX: i, targetY: o, targetPosition: s = ie.Top, borderRadius: a = 5, centerX: c, centerY: u, offset: l = 20, stepPosition: d = 0.5 }) {
  const [f, p, h, y, w] = ax({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: i, y: o },
    targetPosition: s,
    center: { x: c, y: u },
    offset: l,
    stepPosition: d
  });
  let x = `M${f[0].x} ${f[0].y}`;
  for (let m = 1; m < f.length - 1; m++)
    x += cx(f[m - 1], f[m], f[m + 1], a);
  return x += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [x, p, h, y, w];
}
function ba(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function lx(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!ba(t) || !ba(n))
    return null;
  const i = t.internals.handleBounds || Na(t.handles), o = n.internals.handleBounds || Na(n.handles), s = ja(i?.source ?? [], e.sourceHandle), a = ja(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === en.Strict ? o?.target ?? [] : (o?.target ?? []).concat(o?.source ?? []),
    e.targetHandle
  );
  if (!s || !a)
    return e.onError?.("008", Ke.error008(s ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = s?.position || ie.Bottom, u = a?.position || ie.Top, l = Dt(t, s, c), d = Dt(n, a, u);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: d.x,
    targetY: d.y,
    sourcePosition: c,
    targetPosition: u
  };
}
function Na(e) {
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
function Dt(e, t, n = ie.Left, i = !1) {
  const o = (t?.x ?? 0) + e.internals.positionAbsolute.x, s = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: a, height: c } = t ?? dt(e);
  if (i)
    return { x: o + a / 2, y: s + c / 2 };
  switch (t?.position ?? n) {
    case ie.Top:
      return { x: o + a / 2, y: s };
    case ie.Right:
      return { x: o + a, y: s + c / 2 };
    case ie.Bottom:
      return { x: o + a / 2, y: s + c };
    case ie.Left:
      return { x: o, y: s + c / 2 };
  }
}
function ja(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Ar(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((i) => `${i}=${e[i]}`).join("&")}` : "";
}
function ux(e, { id: t, defaultColor: n, defaultMarkerStart: i, defaultMarkerEnd: o }) {
  const s = /* @__PURE__ */ new Set();
  return e.reduce((a, c) => ([c.markerStart || i, c.markerEnd || o].forEach((u) => {
    if (u && typeof u == "object") {
      const l = Ar(u, t);
      s.has(l) || (a.push({ id: l, color: u.color || n, ...u }), s.add(l));
    }
  }), a), []).sort((a, c) => a.id.localeCompare(c.id));
}
const pu = 1e3, dx = 10, ss = {
  nodeOrigin: [0, 0],
  nodeExtent: zn,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, fx = {
  ...ss,
  checkEquality: !0
};
function as(e, t) {
  const n = { ...e };
  for (const i in t)
    t[i] !== void 0 && (n[i] = t[i]);
  return n;
}
function px(e, t, n) {
  const i = as(ss, n);
  for (const o of e.values())
    if (o.parentId)
      ls(o, e, t, i);
    else {
      const s = Un(o, i.nodeOrigin), a = _t(o.extent) ? o.extent : i.nodeExtent, c = At(s, a, dt(o));
      o.internals.positionAbsolute = c;
    }
}
function hx(e, t) {
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
function cs(e) {
  return e === "manual";
}
function _r(e, t, n, i = {}) {
  const o = as(fx, i), s = { i: 0 }, a = new Map(t), c = o?.elevateNodesOnSelect && !cs(o.zIndexMode) ? pu : 0;
  let u = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = a.get(d.id);
    if (o.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const p = Un(d, o.nodeOrigin), h = _t(d.extent) ? d.extent : o.nodeExtent, y = At(p, h, dt(d));
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
          handleBounds: hx(d, f),
          z: hu(d, c, o.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (u = !1), d.parentId && ls(f, t, n, i, s), l ||= d.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: l };
}
function gx(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function ls(e, t, n, i, o) {
  const { elevateNodesOnSelect: s, nodeOrigin: a, nodeExtent: c, zIndexMode: u } = as(ss, i), l = e.parentId, d = t.get(l);
  if (!d) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  gx(e, n), o && !d.parentId && d.internals.rootParentIndex === void 0 && u === "auto" && (d.internals.rootParentIndex = ++o.i, d.internals.z = d.internals.z + o.i * dx), o && d.internals.rootParentIndex !== void 0 && (o.i = d.internals.rootParentIndex);
  const f = s && !cs(u) ? pu : 0, { x: p, y: h, z: y } = yx(e, d, a, c, f, u), { positionAbsolute: w } = e.internals, x = p !== w.x || h !== w.y;
  (x || y !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: x ? { x: p, y: h } : w,
      z: y
    }
  });
}
function hu(e, t, n) {
  const i = Ze(e.zIndex) ? e.zIndex : 0;
  return cs(n) ? i : i + (e.selected ? t : 0);
}
function yx(e, t, n, i, o, s) {
  const { x: a, y: c } = t.internals.positionAbsolute, u = dt(e), l = Un(e, n), d = _t(e.extent) ? At(l, e.extent, u) : l;
  let f = At({ x: a + d.x, y: c + d.y }, i, u);
  e.extent === "parent" && (f = eu(f, u, t));
  const p = hu(e, o, s), h = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: h >= p ? h + 1 : p
  };
}
function us(e, t, n, i = [0, 0]) {
  const o = [], s = /* @__PURE__ */ new Map();
  for (const a of e) {
    const c = t.get(a.parentId);
    if (!c)
      continue;
    const u = s.get(a.parentId)?.expandedRect ?? nn(c), l = tu(u, a.rect);
    s.set(a.parentId, { expandedRect: l, parent: c });
  }
  return s.size > 0 && s.forEach(({ expandedRect: a, parent: c }, u) => {
    const l = c.internals.positionAbsolute, d = dt(c), f = c.origin ?? i, p = a.x < l.x ? Math.round(Math.abs(l.x - a.x)) : 0, h = a.y < l.y ? Math.round(Math.abs(l.y - a.y)) : 0, y = Math.max(d.width, Math.round(a.width)), w = Math.max(d.height, Math.round(a.height)), x = (y - d.width) * f[0], m = (w - d.height) * f[1];
    (p > 0 || h > 0 || x || m) && (o.push({
      id: u,
      type: "position",
      position: {
        x: c.position.x - p + x,
        y: c.position.y - h + m
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
        width: y + (p ? f[0] * p - x : 0),
        height: w + (h ? f[1] * h - m : 0)
      }
    });
  }), o;
}
function mx(e, t, n, i, o, s, a) {
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
    const w = rs(h.nodeElement), x = y.measured.width !== w.width || y.measured.height !== w.height;
    if (!!(w.width && w.height && (x || !y.internals.handleBounds || h.force))) {
      const b = h.nodeElement.getBoundingClientRect(), g = _t(y.extent) ? y.extent : s;
      let { positionAbsolute: v } = y.internals;
      y.parentId && y.extent === "parent" ? v = eu(v, w, t.get(y.parentId)) : g && (v = At(v, g, w));
      const j = {
        ...y,
        measured: w,
        internals: {
          ...y.internals,
          positionAbsolute: v,
          handleBounds: {
            source: ma("source", h.nodeElement, b, f, y.id),
            target: ma("target", h.nodeElement, b, f, y.id)
          }
        }
      };
      t.set(y.id, j), y.parentId && ls(j, t, n, { nodeOrigin: o, zIndexMode: a }), u = !0, x && (l.push({
        id: y.id,
        type: "dimensions",
        dimensions: w
      }), y.expandParent && y.parentId && p.push({
        id: y.id,
        parentId: y.parentId,
        rect: nn(j, o)
      }));
    }
  }
  if (p.length > 0) {
    const h = us(p, t, n, o);
    l.push(...h);
  }
  return { changes: l, updatedInternals: u };
}
async function xx({ delta: e, panZoom: t, transform: n, translateExtent: i, width: o, height: s }) {
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
function Sa(e, t, n, i, o, s) {
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
function gu(e, t, n) {
  e.clear(), t.clear();
  for (const i of n) {
    const { source: o, target: s, sourceHandle: a = null, targetHandle: c = null } = i, u = { edgeId: i.id, source: o, target: s, sourceHandle: a, targetHandle: c }, l = `${o}-${a}--${s}-${c}`, d = `${s}-${c}--${o}-${a}`;
    Sa("source", u, d, e, o, a), Sa("target", u, l, e, s, c), t.set(i.id, i);
  }
}
function yu(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : yu(n, t) : !1;
}
function Ca(e, t, n) {
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
function wx(e, t, n, i) {
  const o = /* @__PURE__ */ new Map();
  for (const [s, a] of e)
    if ((a.selected || a.id === i) && (!a.parentId || !yu(a, e)) && (a.draggable || t && typeof a.draggable > "u")) {
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
function or({ nodeId: e, dragItems: t, nodeLookup: n, dragging: i = !0 }) {
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
function vx({ dragItems: e, snapGrid: t, x: n, y: i }) {
  const o = e.values().next().value;
  if (!o)
    return null;
  const s = {
    x: n - o.distance.x,
    y: i - o.distance.y
  }, a = Gn(s, t);
  return {
    x: a.x - s.x,
    y: a.y - s.y
  };
}
function bx({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: i, onDragStop: o }) {
  let s = { x: null, y: null }, a = 0, c = /* @__PURE__ */ new Map(), u = !1, l = { x: 0, y: 0 }, d = null, f = !1, p = null, h = !1, y = !1, w = null;
  function x({ noDragClassName: b, handleSelector: g, domNode: v, isSelectable: j, nodeId: N, nodeClickDistance: S = 0 }) {
    p = We(v);
    function I({ x: k, y: R }) {
      const { nodeLookup: C, nodeExtent: _, snapGrid: E, snapToGrid: $, nodeOrigin: P, onNodeDrag: T, onSelectionDrag: F, onError: H, updateNodePositions: O } = t();
      s = { x: k, y: R };
      let X = !1;
      const q = c.size > 1, ee = q && _ ? kr(Zn(c)) : null, le = q && $ ? vx({
        dragItems: c,
        snapGrid: E,
        x: k,
        y: R
      }) : null;
      for (const [U, z] of c) {
        if (!C.has(U))
          continue;
        let Y = { x: k - z.distance.x, y: R - z.distance.y };
        $ && (Y = le ? {
          x: Math.round(Y.x + le.x),
          y: Math.round(Y.y + le.y)
        } : Gn(Y, E));
        let ae = null;
        if (q && _ && !z.extent && ee) {
          const { positionAbsolute: oe } = z.internals, fe = oe.x - ee.x + _[0][0], V = oe.x + z.measured.width - ee.x2 + _[1][0], Q = oe.y - ee.y + _[0][1], ge = oe.y + z.measured.height - ee.y2 + _[1][1];
          ae = [
            [fe, Q],
            [V, ge]
          ];
        }
        const { position: ce, positionAbsolute: J } = Ql({
          nodeId: U,
          nextPosition: Y,
          nodeLookup: C,
          nodeExtent: ae || _,
          nodeOrigin: P,
          onError: H
        });
        X = X || z.position.x !== ce.x || z.position.y !== ce.y, z.position = ce, z.internals.positionAbsolute = J;
      }
      if (y = y || X, !!X && (O(c, !0), w && (i || T || !N && F))) {
        const [U, z] = or({
          nodeId: N,
          dragItems: c,
          nodeLookup: C
        });
        i?.(w, c, U, z), T?.(w, U, z), N || F?.(w, z);
      }
    }
    async function D() {
      if (!d)
        return;
      const { transform: k, panBy: R, autoPanSpeed: C, autoPanOnNodeDrag: _ } = t();
      if (!_) {
        u = !1, cancelAnimationFrame(a);
        return;
      }
      const [E, $] = is(l, d, C);
      (E !== 0 || $ !== 0) && (s.x = (s.x ?? 0) - E / k[2], s.y = (s.y ?? 0) - $ / k[2], await R({ x: E, y: $ }) && I(s)), a = requestAnimationFrame(D);
    }
    function M(k) {
      const { nodeLookup: R, multiSelectionActive: C, nodesDraggable: _, transform: E, snapGrid: $, snapToGrid: P, selectNodesOnDrag: T, onNodeDragStart: F, onSelectionDragStart: H, unselectNodesAndEdges: O } = t();
      f = !0, (!T || !j) && !C && N && (R.get(N)?.selected || O()), j && T && N && e?.(N);
      const X = kn(k.sourceEvent, { transform: E, snapGrid: $, snapToGrid: P, containerBounds: d });
      if (s = X, c = wx(R, _, X, N), c.size > 0 && (n || F || !N && H)) {
        const [q, ee] = or({
          nodeId: N,
          dragItems: c,
          nodeLookup: R
        });
        n?.(k.sourceEvent, c, q, ee), F?.(k.sourceEvent, q, ee), N || H?.(k.sourceEvent, ee);
      }
    }
    const A = $l().clickDistance(S).on("start", (k) => {
      const { domNode: R, nodeDragThreshold: C, transform: _, snapGrid: E, snapToGrid: $ } = t();
      d = R?.getBoundingClientRect() || null, h = !1, y = !1, w = k.sourceEvent, C === 0 && M(k), s = kn(k.sourceEvent, { transform: _, snapGrid: E, snapToGrid: $, containerBounds: d }), l = Ge(k.sourceEvent, d);
    }).on("drag", (k) => {
      const { autoPanOnNodeDrag: R, transform: C, snapGrid: _, snapToGrid: E, nodeDragThreshold: $, nodeLookup: P } = t(), T = kn(k.sourceEvent, { transform: C, snapGrid: _, snapToGrid: E, containerBounds: d });
      if (w = k.sourceEvent, (k.sourceEvent.type === "touchmove" && k.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      N && !P.has(N)) && (h = !0), !h) {
        if (!u && R && f && (u = !0, D()), !f) {
          const F = Ge(k.sourceEvent, d), H = F.x - l.x, O = F.y - l.y;
          Math.sqrt(H * H + O * O) > $ && M(k);
        }
        (s.x !== T.xSnapped || s.y !== T.ySnapped) && c && f && (l = Ge(k.sourceEvent, d), I(T));
      }
    }).on("end", (k) => {
      if (!f || h) {
        h && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (u = !1, f = !1, cancelAnimationFrame(a), c.size > 0) {
        const { nodeLookup: R, updateNodePositions: C, onNodeDragStop: _, onSelectionDragStop: E } = t();
        if (y && (C(c, !1), y = !1), o || _ || !N && E) {
          const [$, P] = or({
            nodeId: N,
            dragItems: c,
            nodeLookup: R,
            dragging: !1
          });
          o?.(k.sourceEvent, c, $, P), _?.(k.sourceEvent, $, P), N || E?.(k.sourceEvent, P);
        }
      }
    }).filter((k) => {
      const R = k.target;
      return !k.button && (!b || !Ca(R, `.${b}`, v)) && (!g || Ca(R, g, v));
    });
    p.call(A);
  }
  function m() {
    p?.on(".drag", null);
  }
  return {
    update: x,
    destroy: m
  };
}
function Nx(e, t, n) {
  const i = [], o = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const s of t.values())
    Vn(o, nn(s)) > 0 && i.push(s);
  return i;
}
const jx = 250;
function Sx(e, t, n, i) {
  let o = [], s = 1 / 0;
  const a = Nx(e, n, t + jx);
  for (const c of a) {
    const u = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of u) {
      if (i.nodeId === l.nodeId && i.type === l.type && i.id === l.id)
        continue;
      const { x: d, y: f } = Dt(c, l, l.position, !0), p = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(f - e.y, 2));
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
function mu(e, t, n, i, o, s = !1) {
  const a = i.get(e);
  if (!a)
    return null;
  const c = o === "strict" ? a.internals.handleBounds?.[t] : [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []], u = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return u && s ? { ...u, ...Dt(a, u, u.position, !0) } : u;
}
function xu(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Cx(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const wu = () => !0;
function Ex(e, { connectionMode: t, connectionRadius: n, handleId: i, nodeId: o, edgeUpdaterType: s, isTarget: a, domNode: c, nodeLookup: u, lib: l, autoPanOnConnect: d, flowId: f, panBy: p, cancelConnection: h, onConnectStart: y, onConnect: w, onConnectEnd: x, isValidConnection: m = wu, onReconnectEnd: b, updateConnection: g, getTransform: v, getFromHandle: j, autoPanSpeed: N, dragThreshold: S = 1, handleDomNode: I }) {
  const D = ru(e.target);
  let M = 0, A;
  const { x: k, y: R } = Ge(e), C = xu(s, I), _ = c?.getBoundingClientRect();
  let E = !1;
  if (!_ || !C)
    return;
  const $ = mu(o, C, i, u, t);
  if (!$)
    return;
  let P = Ge(e, _), T = !1, F = null, H = !1, O = null;
  function X() {
    if (!d || !_)
      return;
    const [ce, J] = is(P, _, N);
    p({ x: ce, y: J }), M = requestAnimationFrame(X);
  }
  const q = {
    ...$,
    nodeId: o,
    type: C,
    position: $.position
  }, ee = u.get(o);
  let U = {
    inProgress: !0,
    isValid: null,
    from: Dt(ee, q, ie.Left, !0),
    fromHandle: q,
    fromPosition: q.position,
    fromNode: ee,
    to: P,
    toHandle: null,
    toPosition: pa[q.position],
    toNode: null,
    pointer: P
  };
  function z() {
    E = !0, g(U), y?.(e, { nodeId: o, handleId: i, handleType: C });
  }
  S === 0 && z();
  function Y(ce) {
    if (!E) {
      const { x: ge, y: me } = Ge(ce), Re = ge - k, _e = me - R;
      if (!(Re * Re + _e * _e > S * S))
        return;
      z();
    }
    if (!j() || !q) {
      ae(ce);
      return;
    }
    const J = v();
    P = Ge(ce, _), A = Sx(ln(P, J, !1, [1, 1]), n, u, q), T || (X(), T = !0);
    const oe = vu(ce, {
      handle: A,
      connectionMode: t,
      fromNodeId: o,
      fromHandleId: i,
      fromType: a ? "target" : "source",
      isValidConnection: m,
      doc: D,
      lib: l,
      flowId: f,
      nodeLookup: u
    });
    O = oe.handleDomNode, F = oe.connection, H = Cx(!!A, oe.isValid);
    const fe = u.get(o), V = fe ? Dt(fe, q, ie.Left, !0) : U.from, Q = {
      ...U,
      from: V,
      isValid: H,
      to: oe.toHandle && H ? on({ x: oe.toHandle.x, y: oe.toHandle.y }, J) : P,
      toHandle: oe.toHandle,
      toPosition: H && oe.toHandle ? oe.toHandle.position : pa[q.position],
      toNode: oe.toHandle ? u.get(oe.toHandle.nodeId) : null,
      pointer: P
    };
    g(Q), U = Q;
  }
  function ae(ce) {
    if (!("touches" in ce && ce.touches.length > 0)) {
      if (E) {
        (A || O) && F && H && w?.(F);
        const { inProgress: J, ...oe } = U, fe = {
          ...oe,
          toPosition: U.toHandle ? U.toPosition : null
        };
        x?.(ce, fe), s && b?.(ce, fe);
      }
      h(), cancelAnimationFrame(M), T = !1, H = !1, F = null, O = null, D.removeEventListener("mousemove", Y), D.removeEventListener("mouseup", ae), D.removeEventListener("touchmove", Y), D.removeEventListener("touchend", ae);
    }
  }
  D.addEventListener("mousemove", Y), D.addEventListener("mouseup", ae), D.addEventListener("touchmove", Y), D.addEventListener("touchend", ae);
}
function vu(e, { handle: t, connectionMode: n, fromNodeId: i, fromHandleId: o, fromType: s, doc: a, lib: c, flowId: u, isValidConnection: l = wu, nodeLookup: d }) {
  const f = s === "target", p = t ? a.querySelector(`.${c}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: h, y } = Ge(e), w = a.elementFromPoint(h, y), x = w?.classList.contains(`${c}-flow__handle`) ? w : p, m = {
    handleDomNode: x,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (x) {
    const b = xu(void 0, x), g = x.getAttribute("data-nodeid"), v = x.getAttribute("data-handleid"), j = x.classList.contains("connectable"), N = x.classList.contains("connectableend");
    if (!g || !b)
      return m;
    const S = {
      source: f ? g : i,
      sourceHandle: f ? v : o,
      target: f ? i : g,
      targetHandle: f ? o : v
    };
    m.connection = S;
    const D = j && N && (n === en.Strict ? f && b === "source" || !f && b === "target" : g !== i || v !== o);
    m.isValid = D && l(S), m.toHandle = mu(g, b, v, d, n, !0);
  }
  return m;
}
const Dr = {
  onPointerDown: Ex,
  isValid: vu
};
function Ix({ domNode: e, panZoom: t, getTransform: n, getViewScale: i }) {
  const o = We(e);
  function s({ translateExtent: c, width: u, height: l, zoomStep: d = 1, pannable: f = !0, zoomable: p = !0, inversePan: h = !1 }) {
    const y = (g) => {
      if (g.sourceEvent.type !== "wheel" || !t)
        return;
      const v = n(), j = g.sourceEvent.ctrlKey && On() ? 10 : 1, N = -g.sourceEvent.deltaY * (g.sourceEvent.deltaMode === 1 ? 0.05 : g.sourceEvent.deltaMode ? 1 : 2e-3) * d, S = v[2] * Math.pow(2, N * j);
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
      const S = i() * Math.max(v[2], Math.log(v[2])) * (h ? -1 : 1), I = {
        x: v[0] - N[0] * S,
        y: v[1] - N[1] * S
      }, D = [
        [0, 0],
        [u, l]
      ];
      t.setViewportConstrained({
        x: I.x,
        y: I.y,
        zoom: v[2]
      }, D, c);
    }, b = Yl().on("start", x).on("zoom", f ? m : null).on("zoom.wheel", p ? y : null);
    o.call(b, {});
  }
  function a() {
    o.on("zoom", null);
  }
  return {
    update: s,
    destroy: a,
    pointer: qe
  };
}
const lo = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), rr = ({ x: e, y: t, zoom: n }) => so.translate(e, t).scale(n), Bt = (e, t) => e.target.closest(`.${t}`), bu = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), kx = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, sr = (e, t = 0, n = kx, i = () => {
}) => {
  const o = typeof t == "number" && t > 0;
  return o || i(), o ? e.transition().duration(t).ease(n).on("end", i) : e;
}, Nu = (e) => {
  const t = e.ctrlKey && On() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Ax({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: i, panOnScrollMode: o, panOnScrollSpeed: s, zoomOnPinch: a, onPanZoomStart: c, onPanZoom: u, onPanZoomEnd: l }) {
  return (d) => {
    if (Bt(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && a) {
      const x = qe(d), m = Nu(d), b = f * Math.pow(2, m);
      i.scaleTo(n, b, x, d);
      return;
    }
    const p = d.deltaMode === 1 ? 20 : 1;
    let h = o === Ct.Vertical ? 0 : d.deltaX * p, y = o === Ct.Horizontal ? 0 : d.deltaY * p;
    !On() && d.shiftKey && o !== Ct.Vertical && (h = d.deltaY * p, y = 0), i.translateBy(
      n,
      -(h / f) * s,
      -(y / f) * s,
      // @ts-ignore
      { internal: !0 }
    );
    const w = lo(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(d, w), e.panScrollTimeout = setTimeout(() => {
      l?.(d, w), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(d, w));
  };
}
function _x({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(i, o) {
    const s = i.type === "wheel", a = !t && s && !i.ctrlKey, c = Bt(i, e);
    if (i.ctrlKey && s && c && i.preventDefault(), a || c)
      return null;
    i.preventDefault(), n.call(this, i, o);
  };
}
function Dx({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (i) => {
    if (i.sourceEvent?.internal)
      return;
    const o = lo(i.transform);
    e.mouseButton = i.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = o, i.sourceEvent?.type === "mousedown" && t(!0), n && n?.(i.sourceEvent, o);
  };
}
function $x({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: i, onPanZoom: o }) {
  return (s) => {
    e.usedRightMouseButton = !!(n && bu(t, e.mouseButton ?? 0)), s.sourceEvent?.sync || i([s.transform.x, s.transform.y, s.transform.k]), o && !s.sourceEvent?.internal && o?.(s.sourceEvent, lo(s.transform));
  };
}
function Tx({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: i, onPanZoomEnd: o, onPaneContextMenu: s }) {
  return (a) => {
    if (!a.sourceEvent?.internal && (e.isZoomingOrPanning = !1, s && bu(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && a.sourceEvent && s(a.sourceEvent), e.usedRightMouseButton = !1, i(!1), o)) {
      const c = lo(a.transform);
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
function Px({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: i, panOnScroll: o, zoomOnDoubleClick: s, userSelectionActive: a, noWheelClassName: c, noPanClassName: u, lib: l, connectionInProgress: d }) {
  return (f) => {
    const p = e || t, h = n && f.ctrlKey, y = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (Bt(f, `${l}-flow__node`) || Bt(f, `${l}-flow__edge`)))
      return !0;
    if (!i && !p && !o && !s && !n || a || d && !y || Bt(f, c) && y || Bt(f, u) && (!y || o && y && !e) || !n && f.ctrlKey && y)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!p && !o && !h && y || !i && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(i) && !i.includes(f.button) && f.type === "mousedown")
      return !1;
    const w = Array.isArray(i) && i.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || y) && w;
  };
}
function Mx({ domNode: e, minZoom: t, maxZoom: n, translateExtent: i, viewport: o, onPanZoom: s, onPanZoomStart: a, onPanZoomEnd: c, onDraggingChange: u }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = Yl().scaleExtent([t, n]).translateExtent(i), p = We(e).call(f);
  b({
    x: o.x,
    y: o.y,
    zoom: tn(o.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], i);
  const h = p.on("wheel.zoom"), y = p.on("dblclick.zoom");
  f.wheelDelta(Nu);
  async function w(A, k) {
    return p ? new Promise((R) => {
      f?.interpolate(k?.interpolate === "linear" ? In : Ei).transform(sr(p, k?.duration, k?.ease, () => R(!0)), A);
    }) : !1;
  }
  function x({ noWheelClassName: A, noPanClassName: k, onPaneContextMenu: R, userSelectionActive: C, panOnScroll: _, panOnDrag: E, panOnScrollMode: $, panOnScrollSpeed: P, preventScrolling: T, zoomOnPinch: F, zoomOnScroll: H, zoomOnDoubleClick: O, zoomActivationKeyPressed: X, lib: q, onTransformChange: ee, connectionInProgress: le, paneClickDistance: U, selectionOnDrag: z }) {
    C && !l.isZoomingOrPanning && m();
    const Y = _ && !X && !C;
    f.clickDistance(z ? 1 / 0 : !Ze(U) || U < 0 ? 0 : U);
    const ae = Y ? Ax({
      zoomPanValues: l,
      noWheelClassName: A,
      d3Selection: p,
      d3Zoom: f,
      panOnScrollMode: $,
      panOnScrollSpeed: P,
      zoomOnPinch: F,
      onPanZoomStart: a,
      onPanZoom: s,
      onPanZoomEnd: c
    }) : _x({
      noWheelClassName: A,
      preventScrolling: T,
      d3ZoomHandler: h
    });
    p.on("wheel.zoom", ae, { passive: !1 });
    const ce = Dx({
      zoomPanValues: l,
      onDraggingChange: u,
      onPanZoomStart: a
    });
    f.on("start", ce);
    const J = $x({
      zoomPanValues: l,
      panOnDrag: E,
      onPaneContextMenu: !!R,
      onPanZoom: s,
      onTransformChange: ee
    });
    f.on("zoom", J);
    const oe = Tx({
      zoomPanValues: l,
      panOnDrag: E,
      panOnScroll: _,
      onPaneContextMenu: R,
      onPanZoomEnd: c,
      onDraggingChange: u
    });
    f.on("end", oe);
    const fe = Px({
      zoomActivationKeyPressed: X,
      panOnDrag: E,
      zoomOnScroll: H,
      panOnScroll: _,
      zoomOnDoubleClick: O,
      zoomOnPinch: F,
      userSelectionActive: C,
      noPanClassName: k,
      noWheelClassName: A,
      lib: q,
      connectionInProgress: le
    });
    f.filter(fe), O ? p.on("dblclick.zoom", y) : p.on("dblclick.zoom", null);
  }
  function m() {
    f.on("zoom", null);
  }
  async function b(A, k, R) {
    const C = rr(A), _ = f?.constrain()(C, k, R);
    return _ && await w(_), _;
  }
  async function g(A, k) {
    const R = rr(A);
    return await w(R, k), R;
  }
  function v(A) {
    if (p) {
      const k = rr(A), R = p.property("__zoom");
      (R.k !== A.zoom || R.x !== A.x || R.y !== A.y) && f?.transform(p, k, null, { sync: !0 });
    }
  }
  function j() {
    const A = p ? Xl(p.node()) : { x: 0, y: 0, k: 1 };
    return { x: A.x, y: A.y, zoom: A.k };
  }
  async function N(A, k) {
    return p ? new Promise((R) => {
      f?.interpolate(k?.interpolate === "linear" ? In : Ei).scaleTo(sr(p, k?.duration, k?.ease, () => R(!0)), A);
    }) : !1;
  }
  async function S(A, k) {
    return p ? new Promise((R) => {
      f?.interpolate(k?.interpolate === "linear" ? In : Ei).scaleBy(sr(p, k?.duration, k?.ease, () => R(!0)), A);
    }) : !1;
  }
  function I(A) {
    f?.scaleExtent(A);
  }
  function D(A) {
    f?.translateExtent(A);
  }
  function M(A) {
    const k = !Ze(A) || A < 0 ? 0 : A;
    f?.clickDistance(k);
  }
  return {
    update: x,
    destroy: m,
    setViewport: g,
    setViewportConstrained: b,
    getViewport: j,
    scaleTo: N,
    scaleBy: S,
    setScaleExtent: I,
    setTranslateExtent: D,
    syncViewport: v,
    setClickDistance: M
  };
}
var rn;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(rn || (rn = {}));
function Rx({ width: e, prevWidth: t, height: n, prevHeight: i, affectsX: o, affectsY: s }) {
  const a = e - t, c = n - i, u = [a > 0 ? 1 : a < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return a && o && (u[0] = u[0] * -1), c && s && (u[1] = u[1] * -1), u;
}
function Ea(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), i = e.includes("left"), o = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: i,
    affectsY: o
  };
}
function pt(e, t) {
  return Math.max(0, t - e);
}
function ht(e, t) {
  return Math.max(0, e - t);
}
function mi(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function Ia(e, t) {
  return e ? !t : t;
}
function zx(e, t, n, i, o, s, a, c) {
  let { affectsX: u, affectsY: l } = t;
  const { isHorizontal: d, isVertical: f } = t, p = d && f, { xSnapped: h, ySnapped: y } = n, { minWidth: w, maxWidth: x, minHeight: m, maxHeight: b } = i, { x: g, y: v, width: j, height: N, aspectRatio: S } = e;
  let I = Math.floor(d ? h - e.pointerX : 0), D = Math.floor(f ? y - e.pointerY : 0);
  const M = j + (u ? -I : I), A = N + (l ? -D : D), k = -s[0] * j, R = -s[1] * N;
  let C = mi(M, w, x), _ = mi(A, m, b);
  if (a) {
    let P = 0, T = 0;
    u && I < 0 ? P = pt(g + I + k, a[0][0]) : !u && I > 0 && (P = ht(g + M + k, a[1][0])), l && D < 0 ? T = pt(v + D + R, a[0][1]) : !l && D > 0 && (T = ht(v + A + R, a[1][1])), C = Math.max(C, P), _ = Math.max(_, T);
  }
  if (c) {
    let P = 0, T = 0;
    u && I > 0 ? P = ht(g + I, c[0][0]) : !u && I < 0 && (P = pt(g + M, c[1][0])), l && D > 0 ? T = ht(v + D, c[0][1]) : !l && D < 0 && (T = pt(v + A, c[1][1])), C = Math.max(C, P), _ = Math.max(_, T);
  }
  if (o) {
    if (d) {
      const P = mi(M / S, m, b) * S;
      if (C = Math.max(C, P), a) {
        let T = 0;
        !u && !l || u && !l && p ? T = ht(v + R + M / S, a[1][1]) * S : T = pt(v + R + (u ? I : -I) / S, a[0][1]) * S, C = Math.max(C, T);
      }
      if (c) {
        let T = 0;
        !u && !l || u && !l && p ? T = pt(v + M / S, c[1][1]) * S : T = ht(v + (u ? I : -I) / S, c[0][1]) * S, C = Math.max(C, T);
      }
    }
    if (f) {
      const P = mi(A * S, w, x) / S;
      if (_ = Math.max(_, P), a) {
        let T = 0;
        !u && !l || l && !u && p ? T = ht(g + A * S + k, a[1][0]) / S : T = pt(g + (l ? D : -D) * S + k, a[0][0]) / S, _ = Math.max(_, T);
      }
      if (c) {
        let T = 0;
        !u && !l || l && !u && p ? T = pt(g + A * S, c[1][0]) / S : T = ht(g + (l ? D : -D) * S, c[0][0]) / S, _ = Math.max(_, T);
      }
    }
  }
  D = D + (D < 0 ? _ : -_), I = I + (I < 0 ? C : -C), o && (p ? M > A * S ? D = (Ia(u, l) ? -I : I) / S : I = (Ia(u, l) ? -D : D) * S : d ? (D = I / S, l = u) : (I = D * S, u = l));
  const E = u ? g + I : g, $ = l ? v + D : v;
  return {
    width: j + (u ? -I : I),
    height: N + (l ? -D : D),
    x: s[0] * I * (u ? -1 : 1) + E,
    y: s[1] * D * (l ? -1 : 1) + $
  };
}
const ju = { width: 0, height: 0, x: 0, y: 0 }, Lx = {
  ...ju,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function Vx(e, t, n) {
  const i = t.position.x + e.position.x, o = t.position.y + e.position.y, s = e.measured.width ?? 0, a = e.measured.height ?? 0, c = n[0] * s, u = n[1] * a;
  return [
    [i - c, o - u],
    [i + s - c, o + a - u]
  ];
}
function Ox({ domNode: e, nodeId: t, getStoreItems: n, onChange: i, onEnd: o }) {
  const s = We(e);
  let a = {
    controlDirection: Ea("bottom-right"),
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
    let m = { ...ju }, b = { ...Lx };
    a = {
      boundaries: d,
      resizeDirection: p,
      keepAspectRatio: f,
      controlDirection: Ea(l)
    };
    let g, v = null, j = [], N, S, I, D = !1;
    const M = $l().on("start", (A) => {
      const { nodeLookup: k, transform: R, snapGrid: C, snapToGrid: _, nodeOrigin: E, paneDomNode: $ } = n();
      if (g = k.get(t), !g)
        return;
      v = $?.getBoundingClientRect() ?? null;
      const { xSnapped: P, ySnapped: T } = kn(A.sourceEvent, {
        transform: R,
        snapGrid: C,
        snapToGrid: _,
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
      }, N = void 0, S = _t(g.extent) ? g.extent : void 0, g.parentId && (g.extent === "parent" || g.expandParent) && (N = k.get(g.parentId)), N && g.extent === "parent" && (S = [
        [0, 0],
        [N.measured.width, N.measured.height]
      ]), j = [], I = void 0;
      for (const [F, H] of k)
        if (H.parentId === t && (j.push({
          id: F,
          position: { ...H.position },
          extent: H.extent
        }), H.extent === "parent" || H.expandParent)) {
          const O = Vx(H, g, H.origin ?? E);
          I ? I = [
            [Math.min(O[0][0], I[0][0]), Math.min(O[0][1], I[0][1])],
            [Math.max(O[1][0], I[1][0]), Math.max(O[1][1], I[1][1])]
          ] : I = O;
        }
      h?.(A, { ...m });
    }).on("drag", (A) => {
      const { transform: k, snapGrid: R, snapToGrid: C, nodeOrigin: _ } = n(), E = kn(A.sourceEvent, {
        transform: k,
        snapGrid: R,
        snapToGrid: C,
        containerBounds: v
      }), $ = [];
      if (!g)
        return;
      const { x: P, y: T, width: F, height: H } = m, O = {}, X = g.origin ?? _, { width: q, height: ee, x: le, y: U } = zx(b, a.controlDirection, E, a.boundaries, a.keepAspectRatio, X, S, I), z = q !== F, Y = ee !== H, ae = le !== P && z, ce = U !== T && Y;
      if (!ae && !ce && !z && !Y)
        return;
      if ((ae || ce || X[0] === 1 || X[1] === 1) && (O.x = ae ? le : m.x, O.y = ce ? U : m.y, m.x = O.x, m.y = O.y, j.length > 0)) {
        const V = le - P, Q = U - T;
        for (const ge of j)
          ge.position = {
            x: ge.position.x - V + X[0] * (q - F),
            y: ge.position.y - Q + X[1] * (ee - H)
          }, $.push(ge);
      }
      if ((z || Y) && (O.width = z && (!a.resizeDirection || a.resizeDirection === "horizontal") ? q : m.width, O.height = Y && (!a.resizeDirection || a.resizeDirection === "vertical") ? ee : m.height, m.width = O.width, m.height = O.height), N && g.expandParent) {
        const V = X[0] * (O.width ?? 0);
        O.x && O.x < V && (m.x = V, b.x = b.x - (O.x - V));
        const Q = X[1] * (O.height ?? 0);
        O.y && O.y < Q && (m.y = Q, b.y = b.y - (O.y - Q));
      }
      const J = Rx({
        width: m.width,
        prevWidth: F,
        height: m.height,
        prevHeight: H,
        affectsX: a.controlDirection.affectsX,
        affectsY: a.controlDirection.affectsY
      }), oe = { ...m, direction: J };
      x?.(A, oe) !== !1 && (D = !0, y?.(A, oe), i(O, $));
    }).on("end", (A) => {
      D && (w?.(A, { ...m }), o?.({ ...m }), D = !1);
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
var ar = { exports: {} }, cr = {}, lr = { exports: {} }, ur = {};
var ka;
function Hx() {
  if (ka) return ur;
  ka = 1;
  var e = tt;
  function t(f, p) {
    return f === p && (f !== 0 || 1 / f === 1 / p) || f !== f && p !== p;
  }
  var n = typeof Object.is == "function" ? Object.is : t, i = e.useState, o = e.useEffect, s = e.useLayoutEffect, a = e.useDebugValue;
  function c(f, p) {
    var h = p(), y = i({ inst: { value: h, getSnapshot: p } }), w = y[0].inst, x = y[1];
    return s(
      function() {
        w.value = h, w.getSnapshot = p, u(w) && x({ inst: w });
      },
      [f, h, p]
    ), o(
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
  return ur.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : d, ur;
}
var Aa;
function Wx() {
  return Aa || (Aa = 1, lr.exports = Hx()), lr.exports;
}
var _a;
function Fx() {
  if (_a) return cr;
  _a = 1;
  var e = tt, t = Wx();
  function n(l, d) {
    return l === d && (l !== 0 || 1 / l === 1 / d) || l !== l && d !== d;
  }
  var i = typeof Object.is == "function" ? Object.is : n, o = t.useSyncExternalStore, s = e.useRef, a = e.useEffect, c = e.useMemo, u = e.useDebugValue;
  return cr.useSyncExternalStoreWithSelector = function(l, d, f, p, h) {
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
          var I = p(N);
          return h !== void 0 && h(S, I) ? (g = N, S) : (g = N, v = I);
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
    var x = o(l, y[0], y[1]);
    return a(
      function() {
        w.hasValue = !0, w.value = x;
      },
      [x]
    ), u(x), x;
  }, cr;
}
var Da;
function Bx() {
  return Da || (Da = 1, ar.exports = Fx()), ar.exports;
}
var Kx = Bx();
const Xx = /* @__PURE__ */ df(Kx), Yx = {}, $a = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), i = (d, f) => {
    const p = typeof d == "function" ? d(t) : d;
    if (!Object.is(p, t)) {
      const h = t;
      t = f ?? (typeof p != "object" || p === null) ? p : Object.assign({}, t, p), n.forEach((y) => y(t, h));
    }
  }, o = () => t, u = { setState: i, getState: o, getInitialState: () => l, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (Yx ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(i, o, u);
  return u;
}, qx = (e) => e ? $a(e) : $a, { useDebugValue: Ux } = tt, { useSyncExternalStoreWithSelector: Zx } = Xx, Gx = (e) => e;
function Su(e, t = Gx, n) {
  const i = Zx(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return Ux(i), i;
}
const Ta = (e, t) => {
  const n = qx(e), i = (o, s = t) => Su(n, o, s);
  return Object.assign(i, n), i;
}, Jx = (e, t) => e ? Ta(e, t) : Ta;
function we(e, t) {
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
var dr = { exports: {} }, De = {};
var Pa;
function Qx() {
  if (Pa) return De;
  Pa = 1;
  var e = tt;
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
  return De.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = i, De.createPortal = function(u, l) {
    var d = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!l || l.nodeType !== 1 && l.nodeType !== 9 && l.nodeType !== 11)
      throw Error(t(299));
    return s(u, l, null, d);
  }, De.flushSync = function(u) {
    var l = a.T, d = i.p;
    try {
      if (a.T = null, i.p = 2, u) return u();
    } finally {
      a.T = l, i.p = d, i.d.f();
    }
  }, De.preconnect = function(u, l) {
    typeof u == "string" && (l ? (l = l.crossOrigin, l = typeof l == "string" ? l === "use-credentials" ? l : "" : void 0) : l = null, i.d.C(u, l));
  }, De.prefetchDNS = function(u) {
    typeof u == "string" && i.d.D(u);
  }, De.preinit = function(u, l) {
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
  }, De.preinitModule = function(u, l) {
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
  }, De.preload = function(u, l) {
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
  }, De.preloadModule = function(u, l) {
    if (typeof u == "string")
      if (l) {
        var d = c(l.as, l.crossOrigin);
        i.d.m(u, {
          as: typeof l.as == "string" && l.as !== "script" ? l.as : void 0,
          crossOrigin: d,
          integrity: typeof l.integrity == "string" ? l.integrity : void 0
        });
      } else i.d.m(u);
  }, De.requestFormReset = function(u) {
    i.d.r(u);
  }, De.unstable_batchedUpdates = function(u, l) {
    return u(l);
  }, De.useFormState = function(u, l, d) {
    return a.H.useFormState(u, l, d);
  }, De.useFormStatus = function() {
    return a.H.useHostTransitionStatus();
  }, De.version = "19.2.7", De;
}
var Ma;
function ew() {
  if (Ma) return dr.exports;
  Ma = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), dr.exports = Qx(), dr.exports;
}
var tw = ew();
const uo = Or(null), nw = uo.Provider, Cu = Ke.error001("react");
function pe(e, t) {
  const n = Wn(uo);
  if (n === null)
    throw new Error(Cu);
  return Su(n, e, t);
}
function ve() {
  const e = Wn(uo);
  if (e === null)
    throw new Error(Cu);
  return de(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const Ra = { display: "none" }, iw = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, Eu = "react-flow__node-desc", Iu = "react-flow__edge-desc", ow = "react-flow__aria-live", rw = (e) => e.ariaLiveMessage, sw = (e) => e.ariaLabelConfig;
function aw({ rfId: e }) {
  const t = pe(rw);
  return r.jsx("div", { id: `${ow}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: iw, children: t });
}
function cw({ rfId: e, disableKeyboardA11y: t }) {
  const n = pe(sw);
  return r.jsxs(r.Fragment, { children: [r.jsx("div", { id: `${Eu}-${e}`, style: Ra, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), r.jsx("div", { id: `${Iu}-${e}`, style: Ra, children: n["edge.a11yDescription.default"] }), !t && r.jsx(aw, { rfId: e })] });
}
const fo = Tc(({ position: e = "top-left", children: t, className: n, style: i, ...o }, s) => {
  const a = `${e}`.split("-");
  return r.jsx("div", { className: Ie(["react-flow__panel", n, ...a]), style: i, ref: s, ...o, children: t });
});
fo.displayName = "Panel";
function lw({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : r.jsx(fo, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: r.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const uw = (e) => {
  const t = [], n = [];
  for (const [, i] of e.nodeLookup)
    i.selected && t.push(i.internals.userNode);
  for (const [, i] of e.edgeLookup)
    i.selected && n.push(i);
  return { selectedNodes: t, selectedEdges: n };
}, xi = (e) => e.id;
function dw(e, t) {
  return we(e.selectedNodes.map(xi), t.selectedNodes.map(xi)) && we(e.selectedEdges.map(xi), t.selectedEdges.map(xi));
}
function fw({ onSelectionChange: e }) {
  const t = ve(), { selectedNodes: n, selectedEdges: i } = pe(uw, dw);
  return G(() => {
    const o = { nodes: n, edges: i };
    e?.(o), t.getState().onSelectionChangeHandlers.forEach((s) => s(o));
  }, [n, i, e]), null;
}
const pw = (e) => !!e.onSelectionChangeHandlers;
function hw({ onSelectionChange: e }) {
  const t = pe(pw);
  return e || t ? r.jsx(fw, { onSelectionChange: e }) : null;
}
const ku = [0, 0], gw = { x: 0, y: 0, zoom: 1 }, yw = [
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
], za = [...yw, "rfId"], mw = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), La = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: zn,
  nodeOrigin: ku,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function xw(e) {
  const { setNodes: t, setEdges: n, setMinZoom: i, setMaxZoom: o, setTranslateExtent: s, setNodeExtent: a, reset: c, setDefaultNodesAndEdges: u } = pe(mw, we), l = ve();
  G(() => (u(e.defaultNodes, e.defaultEdges), () => {
    d.current = La, c();
  }), []);
  const d = ne(La);
  return G(
    () => {
      for (const f of za) {
        const p = e[f], h = d.current[f];
        p !== h && (typeof e[f] > "u" || (f === "nodes" ? t(p) : f === "edges" ? n(p) : f === "minZoom" ? i(p) : f === "maxZoom" ? o(p) : f === "translateExtent" ? s(p) : f === "nodeExtent" ? a(p) : f === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: Qm(p) }) : f === "fitView" ? l.setState({ fitViewQueued: p }) : f === "fitViewOptions" ? l.setState({ fitViewOptions: p }) : l.setState({ [f]: p })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    za.map((f) => e[f])
  ), null;
}
function Va() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function ww(e) {
  const [t, n] = B(e === "system" ? null : e);
  return G(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const i = Va(), o = () => n(i?.matches ? "dark" : "light");
    return o(), i?.addEventListener("change", o), () => {
      i?.removeEventListener("change", o);
    };
  }, [e]), t !== null ? t : Va()?.matches ? "dark" : "light";
}
const Oa = typeof document < "u" ? document : null;
function Hn(e = null, t = { target: Oa, actInsideInputWithModifier: !0 }) {
  const [n, i] = B(!1), o = ne(!1), s = ne(/* @__PURE__ */ new Set([])), [a, c] = de(() => {
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
  return G(() => {
    const u = t?.target ?? Oa, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (h) => {
        if (o.current = h.ctrlKey || h.metaKey || h.shiftKey || h.altKey, (!o.current || o.current && !l) && su(h))
          return !1;
        const w = Wa(h.code, c);
        if (s.current.add(h[w]), Ha(a, s.current, !1)) {
          const x = h.composedPath?.()?.[0] || h.target, m = x?.nodeName === "BUTTON" || x?.nodeName === "A";
          t.preventDefault !== !1 && (o.current || !m) && h.preventDefault(), i(!0);
        }
      }, f = (h) => {
        const y = Wa(h.code, c);
        Ha(a, s.current, !0) ? (i(!1), s.current.clear()) : s.current.delete(h[y]), h.key === "Meta" && s.current.clear(), o.current = !1;
      }, p = () => {
        s.current.clear(), i(!1);
      };
      return u?.addEventListener("keydown", d), u?.addEventListener("keyup", f), window.addEventListener("blur", p), window.addEventListener("contextmenu", p), () => {
        u?.removeEventListener("keydown", d), u?.removeEventListener("keyup", f), window.removeEventListener("blur", p), window.removeEventListener("contextmenu", p);
      };
    }
  }, [e, i]), n;
}
function Ha(e, t, n) {
  return e.filter((i) => n || i.length === t.size).some((i) => i.every((o) => t.has(o)));
}
function Wa(e, t) {
  return t.includes(e) ? "code" : "key";
}
const vw = () => {
  const e = ve();
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
      const { width: i, height: o, minZoom: s, maxZoom: a, panZoom: c } = e.getState(), u = os(t, i, o, s, a, n?.padding ?? 0.1);
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
      return ln(l, i, f, d);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: i } = e.getState();
      if (!i)
        return t;
      const { x: o, y: s } = i.getBoundingClientRect(), a = on(t, n);
      return {
        x: a.x + o,
        y: a.y + s
      };
    }
  }), []);
};
function Au(e, t) {
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
      bw(u, c);
    n.push(c);
  }
  return o.length && o.forEach((s) => {
    s.index !== void 0 ? n.splice(s.index, 0, { ...s.item }) : n.push({ ...s.item });
  }), n;
}
function bw(e, t) {
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
function _u(e, t) {
  return Au(e, t);
}
function Du(e, t) {
  return Au(e, t);
}
function bt(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function Kt(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const i = [];
  for (const [o, s] of e) {
    const a = t.has(o);
    !(s.selected === void 0 && !a) && s.selected !== a && (n && (s.selected = a), i.push(bt(s.id, a)));
  }
  return i;
}
function Fa({ items: e = [], lookup: t }) {
  const n = [], i = new Map(e.map((o) => [o.id, o]));
  for (const [o, s] of e.entries()) {
    const a = t.get(s.id), c = a?.internals?.userNode ?? a;
    c !== void 0 && c !== s && n.push({ id: s.id, item: s, type: "replace" }), c === void 0 && n.push({ item: s, type: "add", index: o });
  }
  for (const [o] of t)
    i.get(o) === void 0 && n.push({ id: o, type: "remove" });
  return n;
}
function Ba(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const $u = nu();
function Tu(e, t, n = {}) {
  return ox(e, t, {
    ...n,
    onError: n.onError ?? $u
  });
}
function Nw(e, t, n, i = { shouldReplaceId: !0 }) {
  return rx(e, t, n, {
    ...i,
    onError: i.onError ?? $u
  });
}
const Ka = (e) => Bm(e), jw = (e) => Jl(e);
function Pu(e) {
  return Tc(e);
}
const Sw = typeof window < "u" ? Xd : G;
function Xa(e) {
  const [t, n] = B(BigInt(0)), [i] = B(() => Cw(() => n((o) => o + BigInt(1))));
  return Sw(() => {
    const o = i.get();
    o.length && (e(o), i.reset());
  }, [t]), i;
}
function Cw(e) {
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
const Mu = Or(null);
function Ew({ children: e }) {
  const t = ve(), n = re((c) => {
    const { nodes: u = [], setNodes: l, hasDefaultNodes: d, onNodesChange: f, nodeLookup: p, fitViewQueued: h, onNodesChangeMiddlewareMap: y } = t.getState();
    let w = u;
    for (const m of c)
      w = typeof m == "function" ? m(w) : m;
    let x = Fa({
      items: w,
      lookup: p
    });
    for (const m of y.values())
      x = m(x);
    d && l(w), x.length > 0 ? f?.(x) : h && window.requestAnimationFrame(() => {
      const { fitViewQueued: m, nodes: b, setNodes: g } = t.getState();
      m && g(b);
    });
  }, []), i = Xa(n), o = re((c) => {
    const { edges: u = [], setEdges: l, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: p } = t.getState();
    let h = u;
    for (const y of c)
      h = typeof y == "function" ? y(h) : y;
    d ? l(h) : f && f(Fa({
      items: h,
      lookup: p
    }));
  }, []), s = Xa(o), a = de(() => ({ nodeQueue: i, edgeQueue: s }), []);
  return r.jsx(Mu.Provider, { value: a, children: e });
}
function Iw() {
  const e = Wn(Mu);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const kw = (e) => !!e.panZoom;
function ds() {
  const e = vw(), t = ve(), n = Iw(), i = pe(kw), o = de(() => {
    const s = (f) => t.getState().nodeLookup.get(f), a = (f) => {
      n.nodeQueue.push(f);
    }, c = (f) => {
      n.edgeQueue.push(f);
    }, u = (f) => {
      const { nodeLookup: p, nodeOrigin: h } = t.getState(), y = Ka(f) ? f : p.get(f.id), w = y.parentId ? ou(y.position, y.measured, y.parentId, p, h) : y.position, x = {
        ...y,
        position: w,
        width: y.measured?.width ?? y.width,
        height: y.measured?.height ?? y.height
      };
      return nn(x);
    }, l = (f, p, h = { replace: !1 }) => {
      a((y) => y.map((w) => {
        if (w.id === f) {
          const x = typeof p == "function" ? p(w) : p;
          return h.replace && Ka(x) ? x : { ...w, ...x };
        }
        return w;
      }));
    }, d = (f, p, h = { replace: !1 }) => {
      c((y) => y.map((w) => {
        if (w.id === f) {
          const x = typeof p == "function" ? p(w) : p;
          return h.replace && jw(x) ? x : { ...w, ...x };
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
        const { nodes: h, edges: y, onNodesDelete: w, onEdgesDelete: x, triggerNodeChanges: m, triggerEdgeChanges: b, onDelete: g, onBeforeDelete: v } = t.getState(), { nodes: j, edges: N } = await Um({
          nodesToRemove: f,
          edgesToRemove: p,
          nodes: h,
          edges: y,
          onBeforeDelete: v
        }), S = N.length > 0, I = j.length > 0;
        if (S) {
          const D = N.map(Ba);
          x?.(N), b(D);
        }
        if (I) {
          const D = j.map(Ba);
          w?.(j), m(D);
        }
        return (I || S) && g?.({ nodes: j, edges: N }), { deletedNodes: j, deletedEdges: N };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, p = !0, h) => {
        const y = ga(f), w = y ? f : u(f), x = h !== void 0;
        return w ? (h || t.getState().nodes).filter((m) => {
          const b = t.getState().nodeLookup.get(m.id);
          if (b && !y && (m.id === f.id || !b.internals.positionAbsolute))
            return !1;
          const g = nn(x ? m : b), v = Vn(g, w);
          return p && v > 0 || v >= g.width * g.height || v >= w.width * w.height;
        }) : [];
      },
      isNodeIntersecting: (f, p, h = !0) => {
        const w = ga(f) ? f : u(f);
        if (!w)
          return !1;
        const x = Vn(w, p);
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
        return Km(f, { nodeLookup: p, nodeOrigin: h });
      },
      getHandleConnections: ({ type: f, id: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}-${f}${p ? `-${p}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}${f ? p ? `-${f}-${p}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const p = t.getState().fitViewResolver ?? Jm();
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
const Ya = (e) => e.selected, Aw = typeof window < "u" ? window : void 0;
function _w({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = ve(), { deleteElements: i } = ds(), o = Hn(e, { actInsideInputWithModifier: !1 }), s = Hn(t, { target: Aw });
  G(() => {
    if (o) {
      const { edges: a, nodes: c } = n.getState();
      i({ nodes: c.filter(Ya), edges: a.filter(Ya) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [o]), G(() => {
    n.setState({ multiSelectionActive: s });
  }, [s]);
}
function Dw(e) {
  const t = ve();
  G(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const i = rs(e.current);
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
const po = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, $w = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function Tw({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: i = !1, panOnScrollSpeed: o = 0.5, panOnScrollMode: s = Ct.Free, zoomOnDoubleClick: a = !0, panOnDrag: c = !0, defaultViewport: u, translateExtent: l, minZoom: d, maxZoom: f, zoomActivationKeyCode: p, preventScrolling: h = !0, children: y, noWheelClassName: w, noPanClassName: x, onViewportChange: m, isControlledViewport: b, paneClickDistance: g, selectionOnDrag: v }) {
  const j = ve(), N = ne(null), { userSelectionActive: S, lib: I, connectionInProgress: D } = pe($w, we), M = Hn(p), A = ne();
  Dw(N);
  const k = re((R) => {
    m?.({ x: R[0], y: R[1], zoom: R[2] }), b || j.setState({ transform: R });
  }, [m, b]);
  return G(() => {
    if (N.current) {
      A.current = Mx({
        domNode: N.current,
        minZoom: d,
        maxZoom: f,
        translateExtent: l,
        viewport: u,
        onDraggingChange: (E) => j.setState(($) => $.paneDragging === E ? $ : { paneDragging: E }),
        onPanZoomStart: (E, $) => {
          const { onViewportChangeStart: P, onMoveStart: T } = j.getState();
          T?.(E, $), P?.($);
        },
        onPanZoom: (E, $) => {
          const { onViewportChange: P, onMove: T } = j.getState();
          T?.(E, $), P?.($);
        },
        onPanZoomEnd: (E, $) => {
          const { onViewportChangeEnd: P, onMoveEnd: T } = j.getState();
          T?.(E, $), P?.($);
        }
      });
      const { x: R, y: C, zoom: _ } = A.current.getViewport();
      return j.setState({
        panZoom: A.current,
        transform: [R, C, _],
        domNode: N.current.closest(".react-flow")
      }), () => {
        A.current?.destroy();
      };
    }
  }, []), G(() => {
    A.current?.update({
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
      noPanClassName: x,
      userSelectionActive: S,
      noWheelClassName: w,
      lib: I,
      onTransformChange: k,
      connectionInProgress: D,
      selectionOnDrag: v,
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
    x,
    S,
    w,
    I,
    k,
    D,
    v,
    g
  ]), r.jsx("div", { className: "react-flow__renderer", ref: N, style: po, children: y });
}
const Pw = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function Mw() {
  const { userSelectionActive: e, userSelectionRect: t } = pe(Pw, we);
  return e && t ? r.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const fr = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, Rw = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function zw({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Ln.Full, panOnDrag: i, autoPanOnSelection: o, paneClickDistance: s, selectionOnDrag: a, onSelectionStart: c, onSelectionEnd: u, onPaneClick: l, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: p, onPaneMouseMove: h, onPaneMouseLeave: y, children: w }) {
  const x = ne(0), m = ve(), { userSelectionActive: b, elementsSelectable: g, dragging: v, connectionInProgress: j, panBy: N, autoPanSpeed: S } = pe(Rw, we), I = g && (e || b), D = ne(null), M = ne(), A = ne(/* @__PURE__ */ new Set()), k = ne(/* @__PURE__ */ new Set()), R = ne(!1), C = ne({ x: 0, y: 0 }), _ = ne(!1), E = (z) => {
    if (R.current || j) {
      R.current = !1;
      return;
    }
    l?.(z), m.getState().resetSelectedElements(), m.setState({ nodesSelectionActive: !1 });
  }, $ = (z) => {
    if (Array.isArray(i) && i?.includes(2)) {
      z.preventDefault();
      return;
    }
    d?.(z);
  }, P = f ? (z) => f(z) : void 0, T = (z) => {
    R.current && (z.stopPropagation(), R.current = !1);
  }, F = (z) => {
    const { domNode: Y, transform: ae } = m.getState();
    if (M.current = Y?.getBoundingClientRect(), !M.current)
      return;
    const ce = z.target === D.current;
    if (!ce && !!z.target.closest(".nokey") || !e || !(a && ce || t) || z.button !== 0 || !z.isPrimary)
      return;
    z.target?.setPointerCapture?.(z.pointerId), R.current = !1;
    const { x: fe, y: V } = Ge(z.nativeEvent, M.current), Q = ln({ x: fe, y: V }, ae);
    m.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: Q.x,
        startY: Q.y,
        x: fe,
        y: V
      }
    }), ce || (z.stopPropagation(), z.preventDefault());
  };
  function H(z, Y) {
    const { userSelectionRect: ae } = m.getState();
    if (!ae)
      return;
    const { transform: ce, nodeLookup: J, edgeLookup: oe, connectionLookup: fe, triggerNodeChanges: V, triggerEdgeChanges: Q, defaultEdgeOptions: ge } = m.getState(), me = { x: ae.startX, y: ae.startY }, { x: Re, y: _e } = on(me, ce), $e = {
      startX: me.x,
      startY: me.y,
      x: z < Re ? z : Re,
      y: Y < _e ? Y : _e,
      width: Math.abs(z - Re),
      height: Math.abs(Y - _e)
    }, rt = A.current, Xe = k.current;
    A.current = new Set(ns(J, $e, ce, n === Ln.Partial, !0).map((Te) => Te.id)), k.current = /* @__PURE__ */ new Set();
    const Ye = ge?.selectable ?? !0;
    for (const Te of A.current) {
      const Oe = fe.get(Te);
      if (Oe)
        for (const { edgeId: He } of Oe.values()) {
          const Ne = oe.get(He);
          Ne && (Ne.selectable ?? Ye) && k.current.add(He);
        }
    }
    if (!ya(rt, A.current)) {
      const Te = Kt(J, A.current, !0);
      V(Te);
    }
    if (!ya(Xe, k.current)) {
      const Te = Kt(oe, k.current);
      Q(Te);
    }
    m.setState({
      userSelectionRect: $e,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function O() {
    if (!o || !M.current)
      return;
    const [z, Y] = is(C.current, M.current, S);
    N({ x: z, y: Y }).then((ae) => {
      if (!R.current || !ae) {
        x.current = requestAnimationFrame(O);
        return;
      }
      const { x: ce, y: J } = C.current;
      H(ce, J), x.current = requestAnimationFrame(O);
    });
  }
  const X = () => {
    cancelAnimationFrame(x.current), x.current = 0, _.current = !1;
  };
  G(() => () => X(), []);
  const q = (z) => {
    const { userSelectionRect: Y, transform: ae, resetSelectedElements: ce } = m.getState();
    if (!M.current || !Y)
      return;
    const { x: J, y: oe } = Ge(z.nativeEvent, M.current);
    C.current = { x: J, y: oe };
    const fe = on({ x: Y.startX, y: Y.startY }, ae);
    if (!R.current) {
      const V = t ? 0 : s;
      if (Math.hypot(J - fe.x, oe - fe.y) <= V)
        return;
      ce(), c?.(z);
    }
    R.current = !0, _.current || (O(), _.current = !0), H(J, oe);
  }, ee = (z) => {
    z.button === 0 && (z.target?.releasePointerCapture?.(z.pointerId), !b && z.target === D.current && m.getState().userSelectionRect && E?.(z), m.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), R.current && (u?.(z), m.setState({
      nodesSelectionActive: A.current.size > 0
    })), X());
  }, le = (z) => {
    z.target?.releasePointerCapture?.(z.pointerId), X();
  }, U = i === !0 || Array.isArray(i) && i.includes(0);
  return r.jsxs("div", { className: Ie(["react-flow__pane", { draggable: U, dragging: v, selection: e }]), onClick: I ? void 0 : fr(E, D), onContextMenu: fr($, D), onWheel: fr(P, D), onPointerEnter: I ? void 0 : p, onPointerMove: I ? q : h, onPointerUp: I ? ee : void 0, onPointerCancel: I ? le : void 0, onPointerDownCapture: I ? F : void 0, onClickCapture: I ? T : void 0, onPointerLeave: y, ref: D, style: po, children: [w, r.jsx(Mw, {})] });
}
function $r({ id: e, store: t, unselect: n = !1, nodeRef: i }) {
  const { addSelectedNodes: o, unselectNodesAndEdges: s, multiSelectionActive: a, nodeLookup: c, onError: u } = t.getState(), l = c.get(e);
  if (!l) {
    u?.("012", Ke.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && a) && (s({ nodes: [l], edges: [] }), requestAnimationFrame(() => i?.current?.blur())) : o([e]);
}
function Ru({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: i, nodeId: o, isSelectable: s, nodeClickDistance: a }) {
  const c = ve(), [u, l] = B(!1), d = ne();
  return G(() => {
    d.current = bx({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (f) => {
        $r({
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
  }, []), G(() => {
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
const Lw = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function zu() {
  const e = ve();
  return re((n) => {
    const { nodeExtent: i, snapToGrid: o, snapGrid: s, nodesDraggable: a, onError: c, updateNodePositions: u, nodeLookup: l, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), p = Lw(a), h = o ? s[0] : 5, y = o ? s[1] : 5, w = n.direction.x * h * n.factor, x = n.direction.y * y * n.factor;
    for (const [, m] of l) {
      if (!p(m))
        continue;
      let b = {
        x: m.internals.positionAbsolute.x + w,
        y: m.internals.positionAbsolute.y + x
      };
      o && (b = Gn(b, s));
      const { position: g, positionAbsolute: v } = Ql({
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
const fs = Or(null), Vw = fs.Provider;
fs.Consumer;
const Lu = () => Wn(fs), Ow = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), Hw = (e, t, n) => (i) => {
  const { connectionClickStartHandle: o, connectionMode: s, connection: a } = i, { fromHandle: c, toHandle: u, isValid: l } = a, d = u?.nodeId === e && u?.id === t && u?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: d,
    clickConnecting: o?.nodeId === e && o?.id === t && o?.type === n,
    isPossibleEndHandle: s === en.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!o,
    valid: d && l
  };
};
function Ww({ type: e = "source", position: t = ie.Top, isValidConnection: n, isConnectable: i = !0, isConnectableStart: o = !0, isConnectableEnd: s = !0, id: a, onConnect: c, children: u, className: l, onMouseDown: d, onTouchStart: f, ...p }, h) {
  const y = a || null, w = e === "target", x = ve(), m = Lu(), { connectOnClick: b, noPanClassName: g, rfId: v } = pe(Ow, we), { connectingFrom: j, connectingTo: N, clickConnecting: S, isPossibleEndHandle: I, connectionInProcess: D, clickConnectionInProcess: M, valid: A } = pe(Hw(m, y, e), we);
  m || x.getState().onError?.("010", Ke.error010());
  const k = (_) => {
    const { defaultEdgeOptions: E, onConnect: $, hasDefaultEdges: P } = x.getState(), T = {
      ...E,
      ..._
    };
    if (P) {
      const { edges: F, setEdges: H, onError: O } = x.getState();
      H(Tu(T, F, { onError: O }));
    }
    $?.(T), c?.(T);
  }, R = (_) => {
    if (!m)
      return;
    const E = au(_.nativeEvent);
    if (o && (E && _.button === 0 || !E)) {
      const $ = x.getState();
      Dr.onPointerDown(_.nativeEvent, {
        handleDomNode: _.currentTarget,
        autoPanOnConnect: $.autoPanOnConnect,
        connectionMode: $.connectionMode,
        connectionRadius: $.connectionRadius,
        domNode: $.domNode,
        nodeLookup: $.nodeLookup,
        lib: $.lib,
        isTarget: w,
        handleId: y,
        nodeId: m,
        flowId: $.rfId,
        panBy: $.panBy,
        cancelConnection: $.cancelConnection,
        onConnectStart: $.onConnectStart,
        onConnectEnd: (...P) => x.getState().onConnectEnd?.(...P),
        updateConnection: $.updateConnection,
        onConnect: k,
        isValidConnection: n || ((...P) => x.getState().isValidConnection?.(...P) ?? !0),
        getTransform: () => x.getState().transform,
        getFromHandle: () => x.getState().connection.fromHandle,
        autoPanSpeed: $.autoPanSpeed,
        dragThreshold: $.connectionDragThreshold
      });
    }
    E ? d?.(_) : f?.(_);
  }, C = (_) => {
    const { onClickConnectStart: E, onClickConnectEnd: $, connectionClickStartHandle: P, connectionMode: T, isValidConnection: F, lib: H, rfId: O, nodeLookup: X, connection: q } = x.getState();
    if (!m || !P && !o)
      return;
    if (!P) {
      E?.(_.nativeEvent, { nodeId: m, handleId: y, handleType: e }), x.setState({ connectionClickStartHandle: { nodeId: m, type: e, id: y } });
      return;
    }
    const ee = ru(_.target), le = n || F, { connection: U, isValid: z } = Dr.isValid(_.nativeEvent, {
      handle: {
        nodeId: m,
        id: y,
        type: e
      },
      connectionMode: T,
      fromNodeId: P.nodeId,
      fromHandleId: P.id || null,
      fromType: P.type,
      isValidConnection: le,
      flowId: O,
      doc: ee,
      lib: H,
      nodeLookup: X
    });
    z && U && k(U);
    const Y = structuredClone(q);
    delete Y.inProgress, Y.toPosition = Y.toHandle ? Y.toHandle.position : null, $?.(_, Y), x.setState({ connectionClickStartHandle: null });
  };
  return r.jsx("div", { "data-handleid": y, "data-nodeid": m, "data-handlepos": t, "data-id": `${v}-${m}-${y}-${e}`, className: Ie([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    g,
    l,
    {
      source: !w,
      target: w,
      connectable: i,
      connectablestart: o,
      connectableend: s,
      clickconnecting: S,
      connectingfrom: j,
      connectingto: N,
      valid: A,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: i && (!D || I) && (D || M ? s : o)
    }
  ]), onMouseDown: R, onTouchStart: R, onClick: b ? C : void 0, ref: h, ...p, children: u });
}
const sn = Se(Pu(Ww));
function Fw({ data: e, isConnectable: t, sourcePosition: n = ie.Bottom }) {
  return r.jsxs(r.Fragment, { children: [e?.label, r.jsx(sn, { type: "source", position: n, isConnectable: t })] });
}
function Bw({ data: e, isConnectable: t, targetPosition: n = ie.Top, sourcePosition: i = ie.Bottom }) {
  return r.jsxs(r.Fragment, { children: [r.jsx(sn, { type: "target", position: n, isConnectable: t }), e?.label, r.jsx(sn, { type: "source", position: i, isConnectable: t })] });
}
function Kw() {
  return null;
}
function Xw({ data: e, isConnectable: t, targetPosition: n = ie.Top }) {
  return r.jsxs(r.Fragment, { children: [r.jsx(sn, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const Zi = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, qa = {
  input: Fw,
  default: Bw,
  output: Xw,
  group: Kw
};
function Yw(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const qw = (e) => {
  const { width: t, height: n, x: i, y: o } = Zn(e.nodeLookup, {
    filter: (s) => !!s.selected
  });
  return {
    width: Ze(t) ? t : null,
    height: Ze(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${i}px,${o}px)`
  };
};
function Uw({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const i = ve(), { width: o, height: s, transformString: a, userSelectionActive: c } = pe(qw, we), u = zu(), l = ne(null);
  G(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !c && o !== null && s !== null;
  if (Ru({
    nodeRef: l,
    disabled: !d
  }), !d)
    return null;
  const f = e ? (h) => {
    const y = i.getState().nodes.filter((w) => w.selected);
    e(h, y);
  } : void 0, p = (h) => {
    Object.prototype.hasOwnProperty.call(Zi, h.key) && (h.preventDefault(), u({
      direction: Zi[h.key],
      factor: h.shiftKey ? 4 : 1
    }));
  };
  return r.jsx("div", { className: Ie(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: a
  }, children: r.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : p, style: {
    width: o,
    height: s
  } }) });
}
const Ua = typeof window < "u" ? window : void 0, Zw = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function Vu({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: i, onPaneMouseLeave: o, onPaneContextMenu: s, onPaneScroll: a, paneClickDistance: c, deleteKeyCode: u, selectionKeyCode: l, selectionOnDrag: d, selectionMode: f, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: y, panActivationKeyCode: w, zoomActivationKeyCode: x, elementsSelectable: m, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: v, panOnScrollSpeed: j, panOnScrollMode: N, zoomOnDoubleClick: S, panOnDrag: I, autoPanOnSelection: D, defaultViewport: M, translateExtent: A, minZoom: k, maxZoom: R, preventScrolling: C, onSelectionContextMenu: _, noWheelClassName: E, noPanClassName: $, disableKeyboardA11y: P, onViewportChange: T, isControlledViewport: F }) {
  const { nodesSelectionActive: H, userSelectionActive: O } = pe(Zw, we), X = Hn(l, { target: Ua }), q = Hn(w, { target: Ua }), ee = q || I, le = q || v, U = d && ee !== !0, z = X || O || U;
  return _w({ deleteKeyCode: u, multiSelectionKeyCode: y }), r.jsx(Tw, { onPaneContextMenu: s, elementsSelectable: m, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: le, panOnScrollSpeed: j, panOnScrollMode: N, zoomOnDoubleClick: S, panOnDrag: !X && ee, defaultViewport: M, translateExtent: A, minZoom: k, maxZoom: R, zoomActivationKeyCode: x, preventScrolling: C, noWheelClassName: E, noPanClassName: $, onViewportChange: T, isControlledViewport: F, paneClickDistance: c, selectionOnDrag: U, children: r.jsxs(zw, { onSelectionStart: p, onSelectionEnd: h, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: i, onPaneMouseLeave: o, onPaneContextMenu: s, onPaneScroll: a, panOnDrag: ee, autoPanOnSelection: D, isSelecting: !!z, selectionMode: f, selectionKeyPressed: X, paneClickDistance: c, selectionOnDrag: U, children: [e, H && r.jsx(Uw, { onSelectionContextMenu: _, noPanClassName: $, disableKeyboardA11y: P })] }) });
}
Vu.displayName = "FlowRenderer";
const Gw = Se(Vu), Jw = (e) => (t) => e ? ns(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function Qw(e) {
  return pe(re(Jw(e), [e]), we);
}
const ev = (e) => e.updateNodeInternals;
function tv() {
  const e = pe(ev), [t] = B(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
  return G(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function nv({ node: e, nodeType: t, hasDimensions: n, resizeObserver: i }) {
  const o = ve(), s = ne(null), a = ne(null), c = ne(e.sourcePosition), u = ne(e.targetPosition), l = ne(t), d = n && !!e.internals.handleBounds;
  return G(() => {
    s.current && !e.hidden && (!d || a.current !== s.current) && (a.current && i?.unobserve(a.current), i?.observe(s.current), a.current = s.current);
  }, [d, e.hidden]), G(() => () => {
    a.current && (i?.unobserve(a.current), a.current = null);
  }, []), G(() => {
    if (s.current) {
      const f = l.current !== t, p = c.current !== e.sourcePosition, h = u.current !== e.targetPosition;
      (f || p || h) && (l.current = t, c.current = e.sourcePosition, u.current = e.targetPosition, o.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: s.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), s;
}
function iv({ id: e, onClick: t, onMouseEnter: n, onMouseMove: i, onMouseLeave: o, onContextMenu: s, onDoubleClick: a, nodesDraggable: c, elementsSelectable: u, nodesConnectable: l, nodesFocusable: d, resizeObserver: f, noDragClassName: p, noPanClassName: h, disableKeyboardA11y: y, rfId: w, nodeTypes: x, nodeClickDistance: m, onError: b }) {
  const { node: g, internals: v, isParent: j } = pe((z) => {
    const Y = z.nodeLookup.get(e), ae = z.parentLookup.has(e);
    return {
      node: Y,
      internals: Y.internals,
      isParent: ae
    };
  }, we);
  let N = g.type || "default", S = x?.[N] || qa[N];
  S === void 0 && (b?.("003", Ke.error003(N)), N = "default", S = x?.default || qa.default);
  const I = !!(g.draggable || c && typeof g.draggable > "u"), D = !!(g.selectable || u && typeof g.selectable > "u"), M = !!(g.connectable || l && typeof g.connectable > "u"), A = !!(g.focusable || d && typeof g.focusable > "u"), k = ve(), R = iu(g), C = nv({ node: g, nodeType: N, hasDimensions: R, resizeObserver: f }), _ = Ru({
    nodeRef: C,
    disabled: g.hidden || !I,
    noDragClassName: p,
    handleSelector: g.dragHandle,
    nodeId: e,
    isSelectable: D,
    nodeClickDistance: m
  }), E = zu();
  if (g.hidden)
    return null;
  const $ = dt(g), P = Yw(g), T = D || I || t || n || i || o, F = n ? (z) => n(z, { ...v.userNode }) : void 0, H = i ? (z) => i(z, { ...v.userNode }) : void 0, O = o ? (z) => o(z, { ...v.userNode }) : void 0, X = s ? (z) => s(z, { ...v.userNode }) : void 0, q = a ? (z) => a(z, { ...v.userNode }) : void 0, ee = (z) => {
    const { selectNodesOnDrag: Y, nodeDragThreshold: ae } = k.getState();
    D && (!Y || !I || ae > 0) && $r({
      id: e,
      store: k,
      nodeRef: C
    }), t && t(z, { ...v.userNode });
  }, le = (z) => {
    if (!(su(z.nativeEvent) || y)) {
      if (ql.includes(z.key) && D) {
        const Y = z.key === "Escape";
        $r({
          id: e,
          store: k,
          unselect: Y,
          nodeRef: C
        });
      } else if (I && g.selected && Object.prototype.hasOwnProperty.call(Zi, z.key)) {
        z.preventDefault();
        const { ariaLabelConfig: Y } = k.getState();
        k.setState({
          ariaLiveMessage: Y["node.a11yDescription.ariaLiveMessage"]({
            direction: z.key.replace("Arrow", "").toLowerCase(),
            x: ~~v.positionAbsolute.x,
            y: ~~v.positionAbsolute.y
          })
        }), E({
          direction: Zi[z.key],
          factor: z.shiftKey ? 4 : 1
        });
      }
    }
  }, U = () => {
    if (y || !C.current?.matches(":focus-visible"))
      return;
    const { transform: z, width: Y, height: ae, autoPanOnNodeFocus: ce, setCenter: J } = k.getState();
    if (!ce)
      return;
    ns(/* @__PURE__ */ new Map([[e, g]]), { x: 0, y: 0, width: Y, height: ae }, z, !0).length > 0 || J(g.position.x + $.width / 2, g.position.y + $.height / 2, {
      zoom: z[2]
    });
  };
  return r.jsx("div", { className: Ie([
    "react-flow__node",
    `react-flow__node-${N}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [h]: I
    },
    g.className,
    {
      selected: g.selected,
      selectable: D,
      parent: j,
      draggable: I,
      dragging: _
    }
  ]), ref: C, style: {
    zIndex: v.z,
    transform: `translate(${v.positionAbsolute.x}px,${v.positionAbsolute.y}px)`,
    pointerEvents: T ? "all" : "none",
    visibility: R ? "visible" : "hidden",
    ...g.style,
    ...P
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: F, onMouseMove: H, onMouseLeave: O, onContextMenu: X, onClick: ee, onDoubleClick: q, onKeyDown: A ? le : void 0, tabIndex: A ? 0 : void 0, onFocus: A ? U : void 0, role: g.ariaRole ?? (A ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": y ? void 0 : `${Eu}-${w}`, "aria-label": g.ariaLabel, ...g.domAttributes, children: r.jsx(Vw, { value: e, children: r.jsx(S, { id: e, data: g.data, type: N, positionAbsoluteX: v.positionAbsolute.x, positionAbsoluteY: v.positionAbsolute.y, selected: g.selected ?? !1, selectable: D, draggable: I, deletable: g.deletable ?? !0, isConnectable: M, sourcePosition: g.sourcePosition, targetPosition: g.targetPosition, dragging: _, dragHandle: g.dragHandle, zIndex: v.z, parentId: g.parentId, ...$ }) }) });
}
var ov = Se(iv);
const rv = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function Ou(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: i, elementsSelectable: o, onError: s } = pe(rv, we), a = Qw(e.onlyRenderVisibleElements), c = tv();
  return r.jsx("div", { className: "react-flow__nodes", style: po, children: a.map((u) => (
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
    r.jsx(ov, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: i, elementsSelectable: o, nodeClickDistance: e.nodeClickDistance, onError: s }, u)
  )) });
}
Ou.displayName = "NodeRenderer";
const sv = Se(Ou);
function av(e) {
  return pe(re((n) => {
    if (!e)
      return n.edges.map((o) => o.id);
    const i = [];
    if (n.width && n.height)
      for (const o of n.edges) {
        const s = n.nodeLookup.get(o.source), a = n.nodeLookup.get(o.target);
        s && a && nx({
          sourceNode: s,
          targetNode: a,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && i.push(o.id);
      }
    return i;
  }, [e]), we);
}
const cv = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return r.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, lv = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return r.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Za = {
  [Yi.Arrow]: cv,
  [Yi.ArrowClosed]: lv
};
function uv(e) {
  const t = ve();
  return de(() => Object.prototype.hasOwnProperty.call(Za, e) ? Za[e] : (t.getState().onError?.("009", Ke.error009(e)), null), [e]);
}
const dv = ({ id: e, type: t, color: n, width: i = 12.5, height: o = 12.5, markerUnits: s = "strokeWidth", strokeWidth: a, orient: c = "auto-start-reverse" }) => {
  const u = uv(t);
  return u ? r.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${i}`, markerHeight: `${o}`, viewBox: "-10 -10 20 20", markerUnits: s, orient: c, refX: "0", refY: "0", children: r.jsx(u, { color: n, strokeWidth: a }) }) : null;
}, Hu = ({ defaultColor: e, rfId: t }) => {
  const n = pe((s) => s.edges), i = pe((s) => s.defaultEdgeOptions), o = de(() => ux(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: i?.markerStart,
    defaultMarkerEnd: i?.markerEnd
  }), [n, i, t, e]);
  return o.length ? r.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: r.jsx("defs", { children: o.map((s) => r.jsx(dv, { id: s.id, type: s.type, color: s.color, width: s.width, height: s.height, markerUnits: s.markerUnits, strokeWidth: s.strokeWidth, orient: s.orient }, s.id)) }) }) : null;
};
Hu.displayName = "MarkerDefinitions";
var fv = Se(Hu);
function Wu({ x: e, y: t, label: n, labelStyle: i, labelShowBg: o = !0, labelBgStyle: s, labelBgPadding: a = [2, 4], labelBgBorderRadius: c = 2, children: u, className: l, ...d }) {
  const [f, p] = B({ x: 1, y: 0, width: 0, height: 0 }), h = Ie(["react-flow__edge-textwrapper", l]), y = ne(null);
  return G(() => {
    if (y.current) {
      const w = y.current.getBBox();
      p({
        x: w.x,
        y: w.y,
        width: w.width,
        height: w.height
      });
    }
  }, [n]), n ? r.jsxs("g", { transform: `translate(${e - f.width / 2} ${t - f.height / 2})`, className: h, visibility: f.width ? "visible" : "hidden", ...d, children: [o && r.jsx("rect", { width: f.width + 2 * a[0], x: -a[0], y: -a[1], height: f.height + 2 * a[1], className: "react-flow__edge-textbg", style: s, rx: c, ry: c }), r.jsx("text", { className: "react-flow__edge-text", y: f.height / 2, dy: "0.3em", ref: y, style: i, children: n }), u] }) : null;
}
Wu.displayName = "EdgeText";
const pv = Se(Wu);
function Jn({ path: e, labelX: t, labelY: n, label: i, labelStyle: o, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u, interactionWidth: l = 20, ...d }) {
  return r.jsxs(r.Fragment, { children: [r.jsx("path", { ...d, d: e, fill: "none", className: Ie(["react-flow__edge-path", d.className]) }), l ? r.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, i && Ze(t) && Ze(n) ? r.jsx(pv, { x: t, y: n, label: i, labelStyle: o, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u }) : null] });
}
function Ga({ pos: e, x1: t, y1: n, x2: i, y2: o }) {
  return e === ie.Left || e === ie.Right ? [0.5 * (t + i), n] : [t, 0.5 * (n + o)];
}
function Fu({ sourceX: e, sourceY: t, sourcePosition: n = ie.Bottom, targetX: i, targetY: o, targetPosition: s = ie.Top }) {
  const [a, c] = Ga({
    pos: n,
    x1: e,
    y1: t,
    x2: i,
    y2: o
  }), [u, l] = Ga({
    pos: s,
    x1: i,
    y1: o,
    x2: e,
    y2: t
  }), [d, f, p, h] = cu({
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
function Bu(e) {
  return Se(({ id: t, sourceX: n, sourceY: i, targetX: o, targetY: s, sourcePosition: a, targetPosition: c, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: w, markerStart: x, interactionWidth: m }) => {
    const [b, g, v] = Fu({
      sourceX: n,
      sourceY: i,
      sourcePosition: a,
      targetX: o,
      targetY: s,
      targetPosition: c
    }), j = e.isInternal ? void 0 : t;
    return r.jsx(Jn, { id: j, path: b, labelX: g, labelY: v, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: w, markerStart: x, interactionWidth: m });
  });
}
const hv = Bu({ isInternal: !1 }), Ku = Bu({ isInternal: !0 });
hv.displayName = "SimpleBezierEdge";
Ku.displayName = "SimpleBezierEdgeInternal";
function Xu(e) {
  return Se(({ id: t, sourceX: n, sourceY: i, targetX: o, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, sourcePosition: h = ie.Bottom, targetPosition: y = ie.Top, markerEnd: w, markerStart: x, pathOptions: m, interactionWidth: b }) => {
    const [g, v, j] = Ui({
      sourceX: n,
      sourceY: i,
      sourcePosition: h,
      targetX: o,
      targetY: s,
      targetPosition: y,
      borderRadius: m?.borderRadius,
      offset: m?.offset,
      stepPosition: m?.stepPosition
    }), N = e.isInternal ? void 0 : t;
    return r.jsx(Jn, { id: N, path: g, labelX: v, labelY: j, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: w, markerStart: x, interactionWidth: b });
  });
}
const Yu = Xu({ isInternal: !1 }), qu = Xu({ isInternal: !0 });
Yu.displayName = "SmoothStepEdge";
qu.displayName = "SmoothStepEdgeInternal";
function Uu(e) {
  return Se(({ id: t, ...n }) => {
    const i = e.isInternal ? void 0 : t;
    return r.jsx(Yu, { ...n, id: i, pathOptions: de(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const gv = Uu({ isInternal: !1 }), Zu = Uu({ isInternal: !0 });
gv.displayName = "StepEdge";
Zu.displayName = "StepEdgeInternal";
function Gu(e) {
  return Se(({ id: t, sourceX: n, sourceY: i, targetX: o, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: y, interactionWidth: w }) => {
    const [x, m, b] = fu({ sourceX: n, sourceY: i, targetX: o, targetY: s }), g = e.isInternal ? void 0 : t;
    return r.jsx(Jn, { id: g, path: x, labelX: m, labelY: b, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: y, interactionWidth: w });
  });
}
const yv = Gu({ isInternal: !1 }), Ju = Gu({ isInternal: !0 });
yv.displayName = "StraightEdge";
Ju.displayName = "StraightEdgeInternal";
function Qu(e) {
  return Se(({ id: t, sourceX: n, sourceY: i, targetX: o, targetY: s, sourcePosition: a = ie.Bottom, targetPosition: c = ie.Top, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: w, markerStart: x, pathOptions: m, interactionWidth: b }) => {
    const [g, v, j] = lu({
      sourceX: n,
      sourceY: i,
      sourcePosition: a,
      targetX: o,
      targetY: s,
      targetPosition: c,
      curvature: m?.curvature
    }), N = e.isInternal ? void 0 : t;
    return r.jsx(Jn, { id: N, path: g, labelX: v, labelY: j, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: w, markerStart: x, interactionWidth: b });
  });
}
const mv = Qu({ isInternal: !1 }), ed = Qu({ isInternal: !0 });
mv.displayName = "BezierEdge";
ed.displayName = "BezierEdgeInternal";
const Ja = {
  default: ed,
  straight: Ju,
  step: Zu,
  smoothstep: qu,
  simplebezier: Ku
}, Qa = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, xv = (e, t, n) => n === ie.Left ? e - t : n === ie.Right ? e + t : e, wv = (e, t, n) => n === ie.Top ? e - t : n === ie.Bottom ? e + t : e, ec = "react-flow__edgeupdater";
function tc({ position: e, centerX: t, centerY: n, radius: i = 10, onMouseDown: o, onMouseEnter: s, onMouseOut: a, type: c }) {
  return r.jsx("circle", { onMouseDown: o, onMouseEnter: s, onMouseOut: a, className: Ie([ec, `${ec}-${c}`]), cx: xv(t, i, e), cy: wv(n, i, e), r: i, stroke: "transparent", fill: "transparent" });
}
function vv({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: i, sourceY: o, targetX: s, targetY: a, sourcePosition: c, targetPosition: u, onReconnect: l, onReconnectStart: d, onReconnectEnd: f, setReconnecting: p, setUpdateHover: h }) {
  const y = ve(), w = (v, j) => {
    if (v.button !== 0)
      return;
    const { autoPanOnConnect: N, domNode: S, connectionMode: I, connectionRadius: D, lib: M, onConnectStart: A, cancelConnection: k, nodeLookup: R, rfId: C, panBy: _, updateConnection: E } = y.getState(), $ = j.type === "target", P = (H, O) => {
      p(!1), f?.(H, n, j.type, O);
    }, T = (H) => l?.(n, H), F = (H, O) => {
      p(!0), d?.(v, n, j.type), A?.(H, O);
    };
    Dr.onPointerDown(v.nativeEvent, {
      autoPanOnConnect: N,
      connectionMode: I,
      connectionRadius: D,
      domNode: S,
      handleId: j.id,
      nodeId: j.nodeId,
      nodeLookup: R,
      isTarget: $,
      edgeUpdaterType: j.type,
      lib: M,
      flowId: C,
      cancelConnection: k,
      panBy: _,
      isValidConnection: (...H) => y.getState().isValidConnection?.(...H) ?? !0,
      onConnect: T,
      onConnectStart: F,
      onConnectEnd: (...H) => y.getState().onConnectEnd?.(...H),
      onReconnectEnd: P,
      updateConnection: E,
      getTransform: () => y.getState().transform,
      getFromHandle: () => y.getState().connection.fromHandle,
      dragThreshold: y.getState().connectionDragThreshold,
      handleDomNode: v.currentTarget
    });
  }, x = (v) => w(v, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), m = (v) => w(v, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), b = () => h(!0), g = () => h(!1);
  return r.jsxs(r.Fragment, { children: [(e === !0 || e === "source") && r.jsx(tc, { position: c, centerX: i, centerY: o, radius: t, onMouseDown: x, onMouseEnter: b, onMouseOut: g, type: "source" }), (e === !0 || e === "target") && r.jsx(tc, { position: u, centerX: s, centerY: a, radius: t, onMouseDown: m, onMouseEnter: b, onMouseOut: g, type: "target" })] });
}
function bv({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: i, onClick: o, onDoubleClick: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, rfId: y, edgeTypes: w, noPanClassName: x, onError: m, disableKeyboardA11y: b }) {
  let g = pe((J) => J.edgeLookup.get(e));
  const v = pe((J) => J.defaultEdgeOptions);
  g = v ? { ...v, ...g } : g;
  let j = g.type || "default", N = w?.[j] || Ja[j];
  N === void 0 && (m?.("011", Ke.error011(j)), j = "default", N = w?.default || Ja.default);
  const S = !!(g.focusable || t && typeof g.focusable > "u"), I = typeof f < "u" && (g.reconnectable || n && typeof g.reconnectable > "u"), D = !!(g.selectable || i && typeof g.selectable > "u"), M = ne(null), [A, k] = B(!1), [R, C] = B(!1), _ = ve(), { zIndex: E, sourceX: $, sourceY: P, targetX: T, targetY: F, sourcePosition: H, targetPosition: O } = pe(re((J) => {
    const oe = J.nodeLookup.get(g.source), fe = J.nodeLookup.get(g.target);
    if (!oe || !fe)
      return {
        zIndex: g.zIndex,
        ...Qa
      };
    const V = lx({
      id: e,
      sourceNode: oe,
      targetNode: fe,
      sourceHandle: g.sourceHandle || null,
      targetHandle: g.targetHandle || null,
      connectionMode: J.connectionMode,
      onError: m
    });
    return {
      zIndex: tx({
        selected: g.selected,
        zIndex: g.zIndex,
        sourceNode: oe,
        targetNode: fe,
        elevateOnSelect: J.elevateEdgesOnSelect,
        zIndexMode: J.zIndexMode
      }),
      ...V || Qa
    };
  }, [g.source, g.target, g.sourceHandle, g.targetHandle, g.selected, g.zIndex]), we), X = de(() => g.markerStart ? `url('#${Ar(g.markerStart, y)}')` : void 0, [g.markerStart, y]), q = de(() => g.markerEnd ? `url('#${Ar(g.markerEnd, y)}')` : void 0, [g.markerEnd, y]);
  if (g.hidden || $ === null || P === null || T === null || F === null)
    return null;
  const ee = (J) => {
    const { addSelectedEdges: oe, unselectNodesAndEdges: fe, multiSelectionActive: V } = _.getState();
    D && (_.setState({ nodesSelectionActive: !1 }), g.selected && V ? (fe({ nodes: [], edges: [g] }), M.current?.blur()) : oe([e])), o && o(J, g);
  }, le = s ? (J) => {
    s(J, { ...g });
  } : void 0, U = a ? (J) => {
    a(J, { ...g });
  } : void 0, z = c ? (J) => {
    c(J, { ...g });
  } : void 0, Y = u ? (J) => {
    u(J, { ...g });
  } : void 0, ae = l ? (J) => {
    l(J, { ...g });
  } : void 0, ce = (J) => {
    if (!b && ql.includes(J.key) && D) {
      const { unselectNodesAndEdges: oe, addSelectedEdges: fe } = _.getState();
      J.key === "Escape" ? (M.current?.blur(), oe({ edges: [g] })) : fe([e]);
    }
  };
  return r.jsx("svg", { style: { zIndex: E }, children: r.jsxs("g", { className: Ie([
    "react-flow__edge",
    `react-flow__edge-${j}`,
    g.className,
    x,
    {
      selected: g.selected,
      animated: g.animated,
      inactive: !D && !o,
      updating: A,
      selectable: D
    }
  ]), onClick: ee, onDoubleClick: le, onContextMenu: U, onMouseEnter: z, onMouseMove: Y, onMouseLeave: ae, onKeyDown: S ? ce : void 0, tabIndex: S ? 0 : void 0, role: g.ariaRole ?? (S ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": g.ariaLabel === null ? void 0 : g.ariaLabel || `Edge from ${g.source} to ${g.target}`, "aria-describedby": S ? `${Iu}-${y}` : void 0, ref: M, ...g.domAttributes, children: [!R && r.jsx(N, { id: e, source: g.source, target: g.target, type: g.type, selected: g.selected, animated: g.animated, selectable: D, deletable: g.deletable ?? !0, label: g.label, labelStyle: g.labelStyle, labelShowBg: g.labelShowBg, labelBgStyle: g.labelBgStyle, labelBgPadding: g.labelBgPadding, labelBgBorderRadius: g.labelBgBorderRadius, sourceX: $, sourceY: P, targetX: T, targetY: F, sourcePosition: H, targetPosition: O, data: g.data, style: g.style, sourceHandleId: g.sourceHandle, targetHandleId: g.targetHandle, markerStart: X, markerEnd: q, pathOptions: "pathOptions" in g ? g.pathOptions : void 0, interactionWidth: g.interactionWidth }), I && r.jsx(vv, { edge: g, isReconnectable: I, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, sourceX: $, sourceY: P, targetX: T, targetY: F, sourcePosition: H, targetPosition: O, setUpdateHover: k, setReconnecting: C })] }) });
}
var Nv = Se(bv);
const jv = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function td({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: i, noPanClassName: o, onReconnect: s, onEdgeContextMenu: a, onEdgeMouseEnter: c, onEdgeMouseMove: u, onEdgeMouseLeave: l, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: p, onReconnectStart: h, onReconnectEnd: y, disableKeyboardA11y: w }) {
  const { edgesFocusable: x, edgesReconnectable: m, elementsSelectable: b, onError: g } = pe(jv, we), v = av(t);
  return r.jsxs("div", { className: "react-flow__edges", children: [r.jsx(fv, { defaultColor: e, rfId: n }), v.map((j) => r.jsx(Nv, { id: j, edgesFocusable: x, edgesReconnectable: m, elementsSelectable: b, noPanClassName: o, onReconnect: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, onClick: d, reconnectRadius: f, onDoubleClick: p, onReconnectStart: h, onReconnectEnd: y, rfId: n, onError: g, edgeTypes: i, disableKeyboardA11y: w }, j))] });
}
td.displayName = "EdgeRenderer";
const Sv = Se(td), Cv = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function Ev({ children: e }) {
  const t = pe(Cv);
  return r.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function Iv(e) {
  const t = ds(), n = ne(!1);
  G(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const kv = (e) => e.panZoom?.syncViewport;
function Av(e) {
  const t = pe(kv), n = ve();
  return G(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function _v(e) {
  return e.connection.inProgress ? { ...e.connection, to: ln(e.connection.to, e.transform) } : { ...e.connection };
}
function Dv(e) {
  return _v;
}
function $v(e) {
  const t = Dv();
  return pe(t, we);
}
const Tv = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function Pv({ containerStyle: e, style: t, type: n, component: i }) {
  const { nodesConnectable: o, width: s, height: a, isValid: c, inProgress: u } = pe(Tv, we);
  return !(s && o && u) ? null : r.jsx("svg", { style: e, width: s, height: a, className: "react-flow__connectionline react-flow__container", children: r.jsx("g", { className: Ie(["react-flow__connection", Gl(c)]), children: r.jsx(nd, { style: t, type: n, CustomComponent: i, isValid: c }) }) });
}
const nd = ({ style: e, type: t = gt.Bezier, CustomComponent: n, isValid: i }) => {
  const { inProgress: o, from: s, fromNode: a, fromHandle: c, fromPosition: u, to: l, toNode: d, toHandle: f, toPosition: p, pointer: h } = $v();
  if (!o)
    return;
  if (n)
    return r.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: a, fromHandle: c, fromX: s.x, fromY: s.y, toX: l.x, toY: l.y, fromPosition: u, toPosition: p, connectionStatus: Gl(i), toNode: d, toHandle: f, pointer: h });
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
    case gt.Bezier:
      [y] = lu(w);
      break;
    case gt.SimpleBezier:
      [y] = Fu(w);
      break;
    case gt.Step:
      [y] = Ui({
        ...w,
        borderRadius: 0
      });
      break;
    case gt.SmoothStep:
      [y] = Ui(w);
      break;
    default:
      [y] = fu(w);
  }
  return r.jsx("path", { d: y, fill: "none", className: "react-flow__connection-path", style: e });
};
nd.displayName = "ConnectionLine";
const Mv = {};
function nc(e = Mv) {
  ne(e), ve(), G(() => {
  }, [e]);
}
function Rv() {
  ve(), ne(!1), G(() => {
  }, []);
}
function id({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: i, onEdgeClick: o, onNodeDoubleClick: s, onEdgeDoubleClick: a, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: p, onSelectionEnd: h, connectionLineType: y, connectionLineStyle: w, connectionLineComponent: x, connectionLineContainerStyle: m, selectionKeyCode: b, selectionOnDrag: g, selectionMode: v, multiSelectionKeyCode: j, panActivationKeyCode: N, zoomActivationKeyCode: S, deleteKeyCode: I, onlyRenderVisibleElements: D, elementsSelectable: M, defaultViewport: A, translateExtent: k, minZoom: R, maxZoom: C, preventScrolling: _, defaultMarkerColor: E, zoomOnScroll: $, zoomOnPinch: P, panOnScroll: T, panOnScrollSpeed: F, panOnScrollMode: H, zoomOnDoubleClick: O, panOnDrag: X, autoPanOnSelection: q, onPaneClick: ee, onPaneMouseEnter: le, onPaneMouseMove: U, onPaneMouseLeave: z, onPaneScroll: Y, onPaneContextMenu: ae, paneClickDistance: ce, nodeClickDistance: J, onEdgeContextMenu: oe, onEdgeMouseEnter: fe, onEdgeMouseMove: V, onEdgeMouseLeave: Q, reconnectRadius: ge, onReconnect: me, onReconnectStart: Re, onReconnectEnd: _e, noDragClassName: $e, noWheelClassName: rt, noPanClassName: Xe, disableKeyboardA11y: Ye, nodeExtent: Te, rfId: Oe, viewport: He, onViewportChange: Ne }) {
  return nc(e), nc(t), Rv(), Iv(n), Av(He), r.jsx(Gw, { onPaneClick: ee, onPaneMouseEnter: le, onPaneMouseMove: U, onPaneMouseLeave: z, onPaneContextMenu: ae, onPaneScroll: Y, paneClickDistance: ce, deleteKeyCode: I, selectionKeyCode: b, selectionOnDrag: g, selectionMode: v, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: j, panActivationKeyCode: N, zoomActivationKeyCode: S, elementsSelectable: M, zoomOnScroll: $, zoomOnPinch: P, zoomOnDoubleClick: O, panOnScroll: T, panOnScrollSpeed: F, panOnScrollMode: H, panOnDrag: X, autoPanOnSelection: q, defaultViewport: A, translateExtent: k, minZoom: R, maxZoom: C, onSelectionContextMenu: f, preventScrolling: _, noDragClassName: $e, noWheelClassName: rt, noPanClassName: Xe, disableKeyboardA11y: Ye, onViewportChange: Ne, isControlledViewport: !!He, children: r.jsxs(Ev, { children: [r.jsx(Sv, { edgeTypes: t, onEdgeClick: o, onEdgeDoubleClick: a, onReconnect: me, onReconnectStart: Re, onReconnectEnd: _e, onlyRenderVisibleElements: D, onEdgeContextMenu: oe, onEdgeMouseEnter: fe, onEdgeMouseMove: V, onEdgeMouseLeave: Q, reconnectRadius: ge, defaultMarkerColor: E, noPanClassName: Xe, disableKeyboardA11y: Ye, rfId: Oe }), r.jsx(Pv, { style: w, type: y, component: x, containerStyle: m }), r.jsx("div", { className: "react-flow__edgelabel-renderer" }), r.jsx(sv, { nodeTypes: e, onNodeClick: i, onNodeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, nodeClickDistance: J, onlyRenderVisibleElements: D, noPanClassName: Xe, noDragClassName: $e, disableKeyboardA11y: Ye, nodeExtent: Te, rfId: Oe }), r.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
id.displayName = "GraphView";
const zv = Se(id), Lv = nu(), ic = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, width: o, height: s, fitView: a, fitViewOptions: c, minZoom: u = 0.5, maxZoom: l = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: p = "basic" } = {}) => {
  const h = /* @__PURE__ */ new Map(), y = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), m = i ?? t ?? [], b = n ?? e ?? [], g = d ?? [0, 0], v = f ?? zn;
  gu(w, x, m);
  const { nodesInitialized: j } = _r(b, h, y, {
    nodeOrigin: g,
    nodeExtent: v,
    zIndexMode: p
  });
  let N = [0, 0, 1];
  if (a && o && s) {
    const S = Zn(h, {
      filter: (A) => !!((A.width || A.initialWidth) && (A.height || A.initialHeight))
    }), { x: I, y: D, zoom: M } = os(S, o, s, u, l, c?.padding ?? 0.1);
    N = [I, D, M];
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
    translateExtent: zn,
    nodeExtent: v,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: en.Strict,
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
    connection: { ...Zl },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: Lv,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Ul,
    zIndexMode: p,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, Vv = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, width: o, height: s, fitView: a, fitViewOptions: c, minZoom: u, maxZoom: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p }) => Jx((h, y) => {
  async function w() {
    const { nodeLookup: x, panZoom: m, fitViewOptions: b, fitViewResolver: g, width: v, height: j, minZoom: N, maxZoom: S } = y();
    m && (await qm({
      nodes: x,
      width: v,
      height: j,
      panZoom: m,
      minZoom: N,
      maxZoom: S
    }, b), g?.resolve(!0), h({ fitViewResolver: null }));
  }
  return {
    ...ic({
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
    setNodes: (x) => {
      const { nodeLookup: m, parentLookup: b, nodeOrigin: g, elevateNodesOnSelect: v, fitViewQueued: j, zIndexMode: N, nodesSelectionActive: S } = y(), { nodesInitialized: I, hasSelectedNodes: D } = _r(x, m, b, {
        nodeOrigin: g,
        nodeExtent: f,
        elevateNodesOnSelect: v,
        checkEquality: !0,
        zIndexMode: N
      }), M = S && D;
      j && I ? (w(), h({
        nodes: x,
        nodesInitialized: I,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: M
      })) : h({ nodes: x, nodesInitialized: I, nodesSelectionActive: M });
    },
    setEdges: (x) => {
      const { connectionLookup: m, edgeLookup: b } = y();
      gu(m, b, x), h({ edges: x });
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
      const { triggerNodeChanges: m, nodeLookup: b, parentLookup: g, domNode: v, nodeOrigin: j, nodeExtent: N, debug: S, fitViewQueued: I, zIndexMode: D } = y(), { changes: M, updatedInternals: A } = mx(x, b, g, v, j, N, D);
      A && (px(b, g, { nodeOrigin: j, nodeExtent: N, zIndexMode: D }), I ? (w(), h({ fitViewQueued: !1, fitViewOptions: void 0 })) : h({}), M?.length > 0 && (S && console.log("React Flow: trigger node changes", M), m?.(M)));
    },
    updateNodePositions: (x, m = !1) => {
      const b = [];
      let g = [];
      const { nodeLookup: v, triggerNodeChanges: j, connection: N, updateConnection: S, onNodesChangeMiddlewareMap: I } = y();
      for (const [D, M] of x) {
        const A = v.get(D), k = !!(A?.expandParent && A?.parentId && M?.position), R = {
          id: D,
          type: "position",
          position: k ? {
            x: Math.max(0, M.position.x),
            y: Math.max(0, M.position.y)
          } : M.position,
          dragging: m
        };
        if (A && N.inProgress && N.fromNode.id === A.id) {
          const C = Dt(A, N.fromHandle, ie.Left, !0);
          S({ ...N, from: C });
        }
        k && A.parentId && b.push({
          id: D,
          parentId: A.parentId,
          rect: {
            ...M.internals.positionAbsolute,
            width: M.measured.width ?? 0,
            height: M.measured.height ?? 0
          }
        }), g.push(R);
      }
      if (b.length > 0) {
        const { parentLookup: D, nodeOrigin: M } = y(), A = us(b, v, D, M);
        g.push(...A);
      }
      for (const D of I.values())
        g = D(g);
      j(g);
    },
    triggerNodeChanges: (x) => {
      const { onNodesChange: m, setNodes: b, nodes: g, hasDefaultNodes: v, debug: j } = y();
      if (x?.length) {
        if (v) {
          const N = _u(x, g);
          b(N);
        }
        j && console.log("React Flow: trigger node changes", x), m?.(x);
      }
    },
    triggerEdgeChanges: (x) => {
      const { onEdgesChange: m, setEdges: b, edges: g, hasDefaultEdges: v, debug: j } = y();
      if (x?.length) {
        if (v) {
          const N = Du(x, g);
          b(N);
        }
        j && console.log("React Flow: trigger edge changes", x), m?.(x);
      }
    },
    addSelectedNodes: (x) => {
      const { multiSelectionActive: m, edgeLookup: b, nodeLookup: g, triggerNodeChanges: v, triggerEdgeChanges: j } = y();
      if (m) {
        const N = x.map((S) => bt(S, !0));
        v(N);
        return;
      }
      v(Kt(g, /* @__PURE__ */ new Set([...x]), !0)), j(Kt(b));
    },
    addSelectedEdges: (x) => {
      const { multiSelectionActive: m, edgeLookup: b, nodeLookup: g, triggerNodeChanges: v, triggerEdgeChanges: j } = y();
      if (m) {
        const N = x.map((S) => bt(S, !0));
        j(N);
        return;
      }
      j(Kt(b, /* @__PURE__ */ new Set([...x]))), v(Kt(g, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: x, edges: m } = {}) => {
      const { edges: b, nodes: g, nodeLookup: v, triggerNodeChanges: j, triggerEdgeChanges: N } = y(), S = x || g, I = m || b, D = [];
      for (const A of S) {
        if (!A.selected)
          continue;
        const k = v.get(A.id);
        k && (k.selected = !1), D.push(bt(A.id, !1));
      }
      const M = [];
      for (const A of I)
        A.selected && M.push(bt(A.id, !1));
      j(D), N(M);
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
      const j = m.reduce((S, I) => I.selected ? [...S, bt(I.id, !1)] : S, []), N = x.reduce((S, I) => I.selected ? [...S, bt(I.id, !1)] : S, []);
      b(j), g(N);
    },
    setNodeExtent: (x) => {
      const { nodes: m, nodeLookup: b, parentLookup: g, nodeOrigin: v, elevateNodesOnSelect: j, nodeExtent: N, zIndexMode: S } = y();
      x[0][0] === N[0][0] && x[0][1] === N[0][1] && x[1][0] === N[1][0] && x[1][1] === N[1][1] || (_r(m, b, g, {
        nodeOrigin: v,
        nodeExtent: x,
        elevateNodesOnSelect: j,
        checkEquality: !1,
        zIndexMode: S
      }), h({ nodeExtent: x }));
    },
    panBy: (x) => {
      const { transform: m, width: b, height: g, panZoom: v, translateExtent: j } = y();
      return xx({ delta: x, panZoom: v, transform: m, translateExtent: j, width: b, height: g });
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
        connection: { ...Zl }
      });
    },
    updateConnection: (x) => {
      h({ connection: x });
    },
    reset: () => h({ ...ic() })
  };
}, Object.is);
function Ov({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: i, initialWidth: o, initialHeight: s, initialMinZoom: a, initialMaxZoom: c, initialFitViewOptions: u, fitView: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p, children: h }) {
  const [y] = B(() => Vv({
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
  return r.jsx(nw, { value: y, children: r.jsx(Ew, { children: h }) });
}
function Hv({ children: e, nodes: t, edges: n, defaultNodes: i, defaultEdges: o, width: s, height: a, fitView: c, fitViewOptions: u, minZoom: l, maxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h }) {
  return Wn(uo) ? r.jsx(r.Fragment, { children: e }) : r.jsx(Ov, { initialNodes: t, initialEdges: n, defaultNodes: i, defaultEdges: o, initialWidth: s, initialHeight: a, fitView: c, initialFitViewOptions: u, initialMinZoom: l, initialMaxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h, children: e });
}
const Wv = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function Fv({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, className: o, nodeTypes: s, edgeTypes: a, onNodeClick: c, onEdgeClick: u, onInit: l, onMove: d, onMoveStart: f, onMoveEnd: p, onConnect: h, onConnectStart: y, onConnectEnd: w, onClickConnectStart: x, onClickConnectEnd: m, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: v, onNodeContextMenu: j, onNodeDoubleClick: N, onNodeDragStart: S, onNodeDrag: I, onNodeDragStop: D, onNodesDelete: M, onEdgesDelete: A, onDelete: k, onSelectionChange: R, onSelectionDragStart: C, onSelectionDrag: _, onSelectionDragStop: E, onSelectionContextMenu: $, onSelectionStart: P, onSelectionEnd: T, onBeforeDelete: F, connectionMode: H, connectionLineType: O = gt.Bezier, connectionLineStyle: X, connectionLineComponent: q, connectionLineContainerStyle: ee, deleteKeyCode: le = "Backspace", selectionKeyCode: U = "Shift", selectionOnDrag: z = !1, selectionMode: Y = Ln.Full, panActivationKeyCode: ae = "Space", multiSelectionKeyCode: ce = On() ? "Meta" : "Control", zoomActivationKeyCode: J = On() ? "Meta" : "Control", snapToGrid: oe, snapGrid: fe, onlyRenderVisibleElements: V = !1, selectNodesOnDrag: Q, nodesDraggable: ge, autoPanOnNodeFocus: me, nodesConnectable: Re, nodesFocusable: _e, nodeOrigin: $e = ku, edgesFocusable: rt, edgesReconnectable: Xe, elementsSelectable: Ye = !0, defaultViewport: Te = gw, minZoom: Oe = 0.5, maxZoom: He = 2, translateExtent: Ne = zn, preventScrolling: Rt = !0, nodeExtent: Ae, defaultMarkerColor: dn = "#b1b1b7", zoomOnScroll: L = !0, zoomOnPinch: W = !0, panOnScroll: K = !1, panOnScrollSpeed: te = 0.5, panOnScrollMode: se = Ct.Free, zoomOnDoubleClick: he = !0, panOnDrag: ye = !0, onPaneClick: ke, onPaneMouseEnter: Ce, onPaneMouseMove: ze, onPaneMouseLeave: xe, onPaneScroll: zt, onPaneContextMenu: yo, paneClickDistance: ei = 1, nodeClickDistance: mo = 0, children: xo, onReconnect: ft, onReconnectStart: wo, onReconnectEnd: Lt, onEdgeContextMenu: vo, onEdgeDoubleClick: Vt, onEdgeMouseEnter: bo, onEdgeMouseMove: ti, onEdgeMouseLeave: ni, reconnectRadius: No = 10, onNodesChange: Ot, onEdgesChange: jo, noDragClassName: So = "nodrag", noWheelClassName: Co = "nowheel", noPanClassName: Ht = "nopan", fitView: ii, fitViewOptions: oi, connectOnClick: Eo, attributionPosition: Io, proOptions: ko, defaultEdgeOptions: Ao, elevateNodesOnSelect: _o = !0, elevateEdgesOnSelect: Do = !1, disableKeyboardA11y: ri = !1, autoPanOnConnect: $o, autoPanOnNodeDrag: To, autoPanOnSelection: Po = !0, autoPanSpeed: Mo, connectionRadius: Ro, isValidConnection: si, onError: ai, style: ci, id: fn, nodeDragThreshold: zo, connectionDragThreshold: Lo, viewport: Vo, onViewportChange: Oo, width: Ho, height: Wo, colorMode: Fo = "light", debug: Bo, onScroll: li, ariaLabelConfig: Ko, zIndexMode: ui = "basic", ...Xo }, Yo) {
  const pn = fn || "1", qo = ww(Fo), Uo = re((vt) => {
    vt.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), li?.(vt);
  }, [li]);
  return r.jsx("div", { "data-testid": "rf__wrapper", ...Xo, onScroll: Uo, style: { ...ci, ...Wv }, ref: Yo, className: Ie(["react-flow", o, qo]), id: fn, role: "application", children: r.jsxs(Hv, { nodes: e, edges: t, width: Ho, height: Wo, fitView: ii, fitViewOptions: oi, minZoom: Oe, maxZoom: He, nodeOrigin: $e, nodeExtent: Ae, zIndexMode: ui, children: [r.jsx(xw, { nodes: e, edges: t, defaultNodes: n, defaultEdges: i, onConnect: h, onConnectStart: y, onConnectEnd: w, onClickConnectStart: x, onClickConnectEnd: m, nodesDraggable: ge, autoPanOnNodeFocus: me, nodesConnectable: Re, nodesFocusable: _e, edgesFocusable: rt, edgesReconnectable: Xe, elementsSelectable: Ye, elevateNodesOnSelect: _o, elevateEdgesOnSelect: Do, minZoom: Oe, maxZoom: He, nodeExtent: Ae, onNodesChange: Ot, onEdgesChange: jo, snapToGrid: oe, snapGrid: fe, connectionMode: H, translateExtent: Ne, connectOnClick: Eo, defaultEdgeOptions: Ao, fitView: ii, fitViewOptions: oi, onNodesDelete: M, onEdgesDelete: A, onDelete: k, onNodeDragStart: S, onNodeDrag: I, onNodeDragStop: D, onSelectionDrag: _, onSelectionDragStart: C, onSelectionDragStop: E, onMove: d, onMoveStart: f, onMoveEnd: p, noPanClassName: Ht, nodeOrigin: $e, rfId: pn, autoPanOnConnect: $o, autoPanOnNodeDrag: To, autoPanSpeed: Mo, onError: ai, connectionRadius: Ro, isValidConnection: si, selectNodesOnDrag: Q, nodeDragThreshold: zo, connectionDragThreshold: Lo, onBeforeDelete: F, debug: Bo, ariaLabelConfig: Ko, zIndexMode: ui }), r.jsx(zv, { onInit: l, onNodeClick: c, onEdgeClick: u, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: v, onNodeContextMenu: j, onNodeDoubleClick: N, nodeTypes: s, edgeTypes: a, connectionLineType: O, connectionLineStyle: X, connectionLineComponent: q, connectionLineContainerStyle: ee, selectionKeyCode: U, selectionOnDrag: z, selectionMode: Y, deleteKeyCode: le, multiSelectionKeyCode: ce, panActivationKeyCode: ae, zoomActivationKeyCode: J, onlyRenderVisibleElements: V, defaultViewport: Te, translateExtent: Ne, minZoom: Oe, maxZoom: He, preventScrolling: Rt, zoomOnScroll: L, zoomOnPinch: W, zoomOnDoubleClick: he, panOnScroll: K, panOnScrollSpeed: te, panOnScrollMode: se, panOnDrag: ye, autoPanOnSelection: Po, onPaneClick: ke, onPaneMouseEnter: Ce, onPaneMouseMove: ze, onPaneMouseLeave: xe, onPaneScroll: zt, onPaneContextMenu: yo, paneClickDistance: ei, nodeClickDistance: mo, onSelectionContextMenu: $, onSelectionStart: P, onSelectionEnd: T, onReconnect: ft, onReconnectStart: wo, onReconnectEnd: Lt, onEdgeContextMenu: vo, onEdgeDoubleClick: Vt, onEdgeMouseEnter: bo, onEdgeMouseMove: ti, onEdgeMouseLeave: ni, reconnectRadius: No, defaultMarkerColor: dn, noDragClassName: So, noWheelClassName: Co, noPanClassName: Ht, rfId: pn, disableKeyboardA11y: ri, nodeExtent: Ae, viewport: Vo, onViewportChange: Oo }), r.jsx(hw, { onSelectionChange: R }), xo, r.jsx(lw, { proOptions: ko, position: Io }), r.jsx(cw, { rfId: pn, disableKeyboardA11y: ri })] }) });
}
var od = Pu(Fv);
const Bv = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function Kv({ children: e }) {
  const t = pe(Bv);
  return t ? tw.createPortal(e, t) : null;
}
function Xv({ dimensions: e, lineWidth: t, variant: n, className: i }) {
  return r.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ie(["react-flow__background-pattern", n, i]) });
}
function Yv({ radius: e, className: t }) {
  return r.jsx("circle", { cx: e, cy: e, r: e, className: Ie(["react-flow__background-pattern", "dots", t]) });
}
var mt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(mt || (mt = {}));
const qv = {
  [mt.Dots]: 1,
  [mt.Lines]: 1,
  [mt.Cross]: 6
}, Uv = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function rd({
  id: e,
  variant: t = mt.Dots,
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
  const f = ne(null), { transform: p, patternId: h } = pe(Uv, we), y = i || qv[t], w = t === mt.Dots, x = t === mt.Cross, m = Array.isArray(n) ? n : [n, n], b = [m[0] * p[2] || 1, m[1] * p[2] || 1], g = y * p[2], v = Array.isArray(s) ? s : [s, s], j = x ? [g, g] : b, N = [
    v[0] * p[2] || 1 + j[0] / 2,
    v[1] * p[2] || 1 + j[1] / 2
  ], S = `${h}${e || ""}`;
  return r.jsxs("svg", { className: Ie(["react-flow__background", l]), style: {
    ...u,
    ...po,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": a
  }, ref: f, "data-testid": "rf__background", children: [r.jsx("pattern", { id: S, x: p[0] % b[0], y: p[1] % b[1], width: b[0], height: b[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${N[0]},-${N[1]})`, children: w ? r.jsx(Yv, { radius: g / 2, className: d }) : r.jsx(Xv, { dimensions: j, lineWidth: o, variant: t, className: d }) }), r.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${S})` })] });
}
rd.displayName = "Background";
const sd = Se(rd);
function Zv() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: r.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function Gv() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: r.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function Jv() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: r.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function Qv() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: r.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function e0() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: r.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function wi({ children: e, className: t, ...n }) {
  return r.jsx("button", { type: "button", className: Ie(["react-flow__controls-button", t]), ...n, children: e });
}
const t0 = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function ad({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: i = !0, fitViewOptions: o, onZoomIn: s, onZoomOut: a, onFitView: c, onInteractiveChange: u, className: l, children: d, position: f = "bottom-left", orientation: p = "vertical", "aria-label": h }) {
  const y = ve(), { isInteractive: w, minZoomReached: x, maxZoomReached: m, ariaLabelConfig: b } = pe(t0, we), { zoomIn: g, zoomOut: v, fitView: j } = ds(), N = () => {
    g(), s?.();
  }, S = () => {
    v(), a?.();
  }, I = () => {
    j(o), c?.();
  }, D = () => {
    y.setState({
      nodesDraggable: !w,
      nodesConnectable: !w,
      elementsSelectable: !w
    }), u?.(!w);
  }, M = p === "horizontal" ? "horizontal" : "vertical";
  return r.jsxs(fo, { className: Ie(["react-flow__controls", M, l]), position: f, style: e, "data-testid": "rf__controls", "aria-label": h ?? b["controls.ariaLabel"], children: [t && r.jsxs(r.Fragment, { children: [r.jsx(wi, { onClick: N, className: "react-flow__controls-zoomin", title: b["controls.zoomIn.ariaLabel"], "aria-label": b["controls.zoomIn.ariaLabel"], disabled: m, children: r.jsx(Zv, {}) }), r.jsx(wi, { onClick: S, className: "react-flow__controls-zoomout", title: b["controls.zoomOut.ariaLabel"], "aria-label": b["controls.zoomOut.ariaLabel"], disabled: x, children: r.jsx(Gv, {}) })] }), n && r.jsx(wi, { className: "react-flow__controls-fitview", onClick: I, title: b["controls.fitView.ariaLabel"], "aria-label": b["controls.fitView.ariaLabel"], children: r.jsx(Jv, {}) }), i && r.jsx(wi, { className: "react-flow__controls-interactive", onClick: D, title: b["controls.interactive.ariaLabel"], "aria-label": b["controls.interactive.ariaLabel"], children: w ? r.jsx(e0, {}) : r.jsx(Qv, {}) }), d] });
}
ad.displayName = "Controls";
const cd = Se(ad);
function n0({ id: e, x: t, y: n, width: i, height: o, style: s, color: a, strokeColor: c, strokeWidth: u, className: l, borderRadius: d, shapeRendering: f, selected: p, onClick: h }) {
  const { background: y, backgroundColor: w } = s || {}, x = a || y || w;
  return r.jsx("rect", { className: Ie(["react-flow__minimap-node", { selected: p }, l]), x: t, y: n, rx: d, ry: d, width: i, height: o, style: {
    fill: x,
    stroke: c,
    strokeWidth: u
  }, shapeRendering: f, onClick: h ? (m) => h(m, e) : void 0 });
}
const i0 = Se(n0), o0 = (e) => e.nodes.map((t) => t.id), pr = (e) => e instanceof Function ? e : () => e;
function r0({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: i = 5,
  nodeStrokeWidth: o,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: s = i0,
  onClick: a
}) {
  const c = pe(o0, we), u = pr(t), l = pr(e), d = pr(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return r.jsx(r.Fragment, { children: c.map((p) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    r.jsx(a0, { id: p, nodeColorFunc: u, nodeStrokeColorFunc: l, nodeClassNameFunc: d, nodeBorderRadius: i, nodeStrokeWidth: o, NodeComponent: s, onClick: a, shapeRendering: f }, p)
  )) });
}
function s0({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: i, nodeBorderRadius: o, nodeStrokeWidth: s, shapeRendering: a, NodeComponent: c, onClick: u }) {
  const { node: l, x: d, y: f, width: p, height: h } = pe((y) => {
    const w = y.nodeLookup.get(e);
    if (!w)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const x = w.internals.userNode, { x: m, y: b } = w.internals.positionAbsolute, { width: g, height: v } = dt(x);
    return {
      node: x,
      x: m,
      y: b,
      width: g,
      height: v
    };
  }, we);
  return !l || l.hidden || !iu(l) ? null : r.jsx(c, { x: d, y: f, width: p, height: h, style: l.style, selected: !!l.selected, className: i(l), color: t(l), borderRadius: o, strokeColor: n(l), strokeWidth: s, shapeRendering: a, onClick: u, id: l.id });
}
const a0 = Se(s0);
var c0 = Se(r0);
const l0 = 200, u0 = 150, d0 = (e) => !e.hidden, f0 = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? tu(Zn(e.nodeLookup, { filter: d0 }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, p0 = "react-flow__minimap-desc";
function ld({
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
  pannable: w = !1,
  zoomable: x = !1,
  ariaLabel: m,
  inversePan: b,
  zoomStep: g = 1,
  offsetScale: v = 5
}) {
  const j = ve(), N = ne(null), { boundingRect: S, viewBB: I, rfId: D, panZoom: M, translateExtent: A, flowWidth: k, flowHeight: R, ariaLabelConfig: C } = pe(f0, we), _ = e?.width ?? l0, E = e?.height ?? u0, $ = S.width / _, P = S.height / E, T = Math.max($, P), F = T * _, H = T * E, O = v * T, X = S.x - (F - S.width) / 2 - O, q = S.y - (H - S.height) / 2 - O, ee = F + O * 2, le = H + O * 2, U = `${p0}-${D}`, z = ne(0), Y = ne();
  z.current = T, G(() => {
    if (N.current && M)
      return Y.current = Ix({
        domNode: N.current,
        panZoom: M,
        getTransform: () => j.getState().transform,
        getViewScale: () => z.current
      }), () => {
        Y.current?.destroy();
      };
  }, [M]), G(() => {
    Y.current?.update({
      translateExtent: A,
      width: k,
      height: R,
      inversePan: b,
      pannable: w,
      zoomStep: g,
      zoomable: x
    });
  }, [w, x, b, g, A, k, R]);
  const ae = h ? (oe) => {
    const [fe, V] = Y.current?.pointer(oe) || [0, 0];
    h(oe, { x: fe, y: V });
  } : void 0, ce = y ? re((oe, fe) => {
    const V = j.getState().nodeLookup.get(fe).internals.userNode;
    y(oe, V);
  }, []) : void 0, J = m ?? C["minimap.ariaLabel"];
  return r.jsx(fo, { position: p, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * T : void 0,
    "--xy-minimap-node-background-color-props": typeof i == "string" ? i : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof a == "number" ? a : void 0
  }, className: Ie(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: r.jsxs("svg", { width: _, height: E, viewBox: `${X} ${q} ${ee} ${le}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": U, ref: N, onClick: ae, children: [J && r.jsx("title", { id: U, children: J }), r.jsx(c0, { onClick: ce, nodeColor: i, nodeStrokeColor: n, nodeBorderRadius: s, nodeClassName: o, nodeStrokeWidth: a, nodeComponent: c }), r.jsx("path", { className: "react-flow__minimap-mask", d: `M${X - O},${q - O}h${ee + O * 2}v${le + O * 2}h${-ee - O * 2}z
        M${I.x},${I.y}h${I.width}v${I.height}h${-I.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
ld.displayName = "MiniMap";
const ud = Se(ld), h0 = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, g0 = {
  [rn.Line]: "right",
  [rn.Handle]: "bottom-right"
};
function y0({ nodeId: e, position: t, variant: n = rn.Handle, className: i, style: o = void 0, children: s, color: a, minWidth: c = 10, minHeight: u = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: p, autoScale: h = !0, shouldResize: y, onResizeStart: w, onResize: x, onResizeEnd: m }) {
  const b = Lu(), g = typeof e == "string" ? e : b, v = ve(), j = ne(null), N = n === rn.Handle, S = pe(re(h0(N && h), [N, h]), we), I = ne(null), D = t ?? g0[n];
  G(() => {
    if (!(!j.current || !g))
      return I.current || (I.current = Ox({
        domNode: j.current,
        nodeId: g,
        getStoreItems: () => {
          const { nodeLookup: A, transform: k, snapGrid: R, snapToGrid: C, nodeOrigin: _, domNode: E } = v.getState();
          return {
            nodeLookup: A,
            transform: k,
            snapGrid: R,
            snapToGrid: C,
            nodeOrigin: _,
            paneDomNode: E
          };
        },
        onChange: (A, k) => {
          const { triggerNodeChanges: R, nodeLookup: C, parentLookup: _, nodeOrigin: E } = v.getState(), $ = [], P = { x: A.x, y: A.y }, T = C.get(g);
          if (T && T.expandParent && T.parentId) {
            const F = T.origin ?? E, H = A.width ?? T.measured.width ?? 0, O = A.height ?? T.measured.height ?? 0, X = {
              id: T.id,
              parentId: T.parentId,
              rect: {
                width: H,
                height: O,
                ...ou({
                  x: A.x ?? T.position.x,
                  y: A.y ?? T.position.y
                }, { width: H, height: O }, T.parentId, C, F)
              }
            }, q = us([X], C, _, E);
            $.push(...q), P.x = A.x ? Math.max(F[0] * H, A.x) : void 0, P.y = A.y ? Math.max(F[1] * O, A.y) : void 0;
          }
          if (P.x !== void 0 && P.y !== void 0) {
            const F = {
              id: g,
              type: "position",
              position: { ...P }
            };
            $.push(F);
          }
          if (A.width !== void 0 && A.height !== void 0) {
            const H = {
              id: g,
              type: "dimensions",
              resizing: !0,
              setAttributes: p ? p === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: A.width,
                height: A.height
              }
            };
            $.push(H);
          }
          for (const F of k) {
            const H = {
              ...F,
              type: "position"
            };
            $.push(H);
          }
          R($);
        },
        onEnd: ({ width: A, height: k }) => {
          const R = {
            id: g,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: A,
              height: k
            }
          };
          v.getState().triggerNodeChanges([R]);
        }
      })), I.current.update({
        controlPosition: D,
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
        I.current?.destroy();
      };
  }, [
    D,
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
  const M = D.split("-");
  return r.jsx("div", { className: Ie(["react-flow__resize-control", "nodrag", ...M, n, i]), ref: j, style: {
    ...o,
    scale: S,
    ...a && { [N ? "backgroundColor" : "borderColor"]: a }
  }, children: s });
}
Se(y0);
function m0(e, t) {
  return {
    ...t ? { name: t } : {},
    definitionId: e.definitionId,
    state: Bn(e.state),
    layout: e.layout
  };
}
function x0(e) {
  return JSON.stringify(
    {
      state: Bn(e.state),
      layout: e.layout
    },
    null,
    2
  );
}
function w0(e, t) {
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
      state: Qi(i.state),
      layout: i.layout ?? t.layout
    }
  };
}
function v0(e, t) {
  const n = (t ?? "workflow").trim().replace(/[^\w.-]+/g, "-") || "workflow", i = new Blob([JSON.stringify(e, null, 2)], { type: "application/json" }), o = URL.createObjectURL(i), s = document.createElement("a");
  s.href = o, s.download = `${n}.json`, document.body.appendChild(s), s.click(), s.remove(), URL.revokeObjectURL(o);
}
function oc({
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
function b0({
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
    () => d ? Yd(d) : null,
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
        f ? /* @__PURE__ */ r.jsx(qd, { fallback: /* @__PURE__ */ r.jsx(
          oc,
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
          oc,
          {
            document: e,
            readOnly: n,
            minHeight: o,
            ariaLabel: s,
            onChange: c
          }
        ),
        /* @__PURE__ */ r.jsx(N0, { diagnostics: u })
      ]
    }
  );
}
function N0({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ r.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const i = t.severity ?? "info", o = j0(t);
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
function j0(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const S0 = { language: "json", displayName: "JSON" };
function C0({ draft: e, onApply: t }) {
  const n = de(() => x0(e), [e]), [i, o] = B(n), [s, a] = B(n), [c, u] = B(null);
  G(() => {
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
          /* @__PURE__ */ r.jsx(an, { size: 14 }),
          " Apply"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ r.jsx("div", { className: "wf-code-view-editor", children: /* @__PURE__ */ r.jsx(
      b0,
      {
        ariaLabel: "Workflow JSON",
        document: { uri: "elsa://workflows/definition.json", language: "json", value: i },
        languageAdapter: S0,
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
function Pe(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function ps(e, t) {
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
function $t(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function hs(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ r.jsx(zc, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ r.jsx(Wr, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ r.jsx(Jd, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ r.jsx(Zt, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ r.jsx(Gd, { size: 15 });
    default:
      return /* @__PURE__ */ r.jsx(Fn, { size: 15 });
  }
}
const E0 = ["Single", "Array", "List", "HashSet"];
function dd(e) {
  const [t, n] = B(null), [i, o] = B(null);
  G(() => {
    let u = !1;
    return qp(e).then(
      (l) => {
        u || n(l);
      },
      () => {
        u || n([]);
      }
    ), Zp(e).then(
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
      const l = Ps(u);
      return {
        value: l,
        label: Xc(u.displayName, l),
        group: u.category?.trim() || "Other"
      };
    }) : null,
    [t]
  ), a = de(
    () => i && i.length > 0 ? i.filter((u) => !u.deprecated).map((u) => ({
      value: u.typeName,
      label: kf(u.displayName, u.typeName)
    })) : null,
    [i]
  ), c = de(() => {
    const u = /* @__PURE__ */ new Map();
    for (const l of t ?? []) {
      const d = Ps(l), f = l.defaultEditor?.trim();
      d && u.set(d, f && f.length > 0 ? f : "text");
    }
    return (l) => u.get(l) ?? "text";
  }, [t]);
  return { typeOptions: s, storageOptions: a, editorForAlias: c };
}
function I0(e) {
  return !e || e.length === 0 ? void 0 : (e.find((n) => /(^|\.)String$/i.test(n.value) || n.label.toLowerCase() === "string") ?? e[0]).value;
}
function k0(e, t, n) {
  return {
    add: () => {
      const i = xf(n.namePrefix, e.map((o) => Cn(o, n.nameKeys)));
      t([...e, n.create(i)]);
    },
    update: (i, o) => t(e.map((s, a) => a === i ? n.patch(s, o) : s)),
    remove: (i) => t(e.filter((o, s) => s !== i))
  };
}
function rc({ value: e, options: t, placeholder: n, allowEmpty: i, ariaLabel: o, onChange: s }) {
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
const A0 = {
  Single: "Single",
  Array: "Array",
  List: "List",
  HashSet: "Hash set"
};
function _0({ value: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ r.jsx("select", { "aria-label": t, value: e, onChange: (i) => n(i.target.value), children: E0.map((i) => /* @__PURE__ */ r.jsx("option", { value: i, children: A0[i] }, i)) });
}
function D0(e, t) {
  return t === "" ? !0 : e === "checkbox" ? t === "true" || t === "false" || t === "True" || t === "False" : e === "number" ? Number.isFinite(Number(t.trim())) && t.trim() !== "" : e === "date" ? /^\d{4}-\d{2}-\d{2}$/.test(t) : !0;
}
function $0({ value: e, editor: t, ariaLabel: n, onChange: i }) {
  const o = D0(t, e);
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
function T0({ title: e, addLabel: t, emptyLabel: n, headers: i, isEmpty: o, onAdd: s, children: a }) {
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-properties-section-head", children: [
      /* @__PURE__ */ r.jsx("h3", { children: e }),
      /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-properties-add", onClick: s, children: [
        /* @__PURE__ */ r.jsx(Gt, { size: 14 }),
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
function P0({ label: e, onRemove: t }) {
  return /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-properties-remove", "aria-label": e, title: e, onClick: t, children: /* @__PURE__ */ r.jsx(An, { size: 14 }) }) });
}
function M0({ checked: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ r.jsx("input", { type: "checkbox", "aria-label": t, checked: e, onChange: (i) => n(i.target.checked) });
}
function gs({
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
  const { add: y, update: w, remove: x } = k0(e, h, {
    namePrefix: o,
    nameKeys: s,
    create: (g) => l(g, I0(t)),
    patch: d
  }), m = ["Name", "Type", "Collection", ...f.default ? ["Default"] : [], ...f.storage ? ["Storage"] : [], ...f.required ? ["Required"] : []], b = o.toLowerCase();
  return /* @__PURE__ */ r.jsx(
    T0,
    {
      title: a,
      addLabel: c,
      emptyLabel: u,
      headers: m,
      isEmpty: e.length === 0,
      onAdd: y,
      children: e.map((g, v) => {
        const j = Cn(g, s), N = Kc(g), S = Cn(g, qc), I = S ? p?.get(S) : void 0, D = N.collectionKind === "Single" ? i(N.alias) : "text";
        return /* @__PURE__ */ r.jsxs("tr", { children: [
          /* @__PURE__ */ r.jsxs("td", { children: [
            /* @__PURE__ */ r.jsx("input", { type: "text", "aria-label": `${o} name`, value: j, onChange: (M) => w(v, { name: M.target.value }) }),
            I ? /* @__PURE__ */ r.jsx("span", { className: "wf-properties-warning", role: "note", title: I, children: I }) : null
          ] }),
          /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            rc,
            {
              ariaLabel: `${o} type`,
              value: N.alias,
              options: t,
              placeholder: "Type",
              onChange: (M) => w(v, { type: { alias: M, collectionKind: N.collectionKind } })
            }
          ) }),
          /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            _0,
            {
              ariaLabel: `${o} collection kind`,
              value: N.collectionKind,
              onChange: (M) => w(v, { type: { alias: N.alias, collectionKind: M } })
            }
          ) }),
          f.default ? /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            $0,
            {
              ariaLabel: `${o} default value`,
              value: Nf(g.default),
              editor: D,
              onChange: (M) => w(v, { default: bf(M) })
            }
          ) }) : null,
          f.storage ? /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            rc,
            {
              ariaLabel: `${o} storage driver`,
              value: Cn(g, _f),
              options: n,
              placeholder: "—",
              allowEmpty: !0,
              onChange: (M) => w(v, { storageDriverType: M || null })
            }
          ) }) : null,
          f.required ? /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            M0,
            {
              ariaLabel: `${o} required`,
              checked: g.isRequired === !0,
              onChange: (M) => w(v, { isRequired: M })
            }
          ) }) : null,
          /* @__PURE__ */ r.jsx(P0, { label: `Remove ${b} ${j || v + 1}`, onRemove: () => x(v) })
        ] }, v);
      })
    }
  );
}
function fd({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, title: o = "Variables", addLabel: s = "Add variable", emptyLabel: a = "No variables defined.", warnings: c, onChange: u }) {
  return /* @__PURE__ */ r.jsx(
    gs,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Variable",
      nameKeys: Af,
      title: o,
      addLabel: s,
      emptyLabel: a,
      create: (l, d) => wf({ name: l, alias: d }),
      patch: (l, d) => vf(l, d),
      columns: { default: !0, storage: !0 },
      warnings: c,
      onChange: u
    }
  );
}
function R0({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, onChange: o }) {
  return /* @__PURE__ */ r.jsx(
    gs,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Input",
      nameKeys: Yc,
      title: "Inputs",
      addLabel: "Add input",
      emptyLabel: "No inputs defined.",
      create: (s, a) => jf({ name: s, alias: a }),
      patch: (s, a) => Sf(s, a),
      columns: { default: !1, storage: !0, required: !0 },
      onChange: o
    }
  );
}
function z0({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, onChange: o }) {
  return /* @__PURE__ */ r.jsx(
    gs,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Output",
      nameKeys: Yc,
      title: "Outputs",
      addLabel: "Add output",
      emptyLabel: "No outputs defined.",
      create: (s, a) => Cf({ name: s, alias: a }),
      patch: (s, a) => Ef(s, a),
      columns: { default: !1, storage: !1 },
      onChange: o
    }
  );
}
function _i(e) {
  return (e ?? []).filter(Pi);
}
function L0({ context: e, variables: t, title: n, addLabel: i, emptyLabel: o, warnings: s, onChange: a }) {
  const { typeOptions: c, storageOptions: u, editorForAlias: l } = dd(e);
  return /* @__PURE__ */ r.jsx(
    fd,
    {
      items: _i(t),
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
function V0({ definition: e, definitionId: t, onMetaChange: n }) {
  const i = !!n, [o, s] = B(e?.name ?? ""), [a, c] = B(e?.description ?? "");
  G(() => {
    s(e?.name ?? "");
  }, [e?.name]), G(() => {
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
function O0({ details: e, draft: t, context: n, onStateChange: i, onDefinitionMetaChange: o }) {
  const { typeOptions: s, storageOptions: a, editorForAlias: c } = dd(n), u = _i(t.state.variables), l = _i(t.state.inputs), d = _i(t.state.outputs), f = e?.versions ?? [];
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-properties-view", children: [
    /* @__PURE__ */ r.jsx(
      V0,
      {
        definition: e?.definition,
        definitionId: t.definitionId,
        onMetaChange: o
      }
    ),
    /* @__PURE__ */ r.jsx(
      fd,
      {
        items: u,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, variables: p }))
      }
    ),
    /* @__PURE__ */ r.jsx(
      R0,
      {
        items: l,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, inputs: p }))
      }
    ),
    /* @__PURE__ */ r.jsx(
      z0,
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
        /* @__PURE__ */ r.jsx("time", { children: Pe(p.createdAt) })
      ] }, p.id)) })
    ] })
  ] });
}
const sc = "application/x-elsa-activity-version-id", H0 = 6, W0 = 1200, F0 = 250, B0 = [10, 25, 50], K0 = 10, ac = "elsa-studio-workflow-palette-width", cc = "elsa-studio-workflow-inspector-width", lc = "elsa-studio-workflow-palette-collapsed", uc = "elsa-studio-workflow-inspector-collapsed", pd = "elsa-studio-workflow-side-panel-maximized", bn = 180, Nn = 460, X0 = 260, Xt = 260, Yt = 560, Y0 = 320, dc = 42, vi = 16, hd = tt.createContext(null), gd = tt.createContext(null);
function q0(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function yd(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Tt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function Pt(e, t, n) {
  const i = t.createPrompt(n);
  return i ? (e.dispatchPrompt(i), !0) : !1;
}
function U0(e) {
  if (!e) return null;
  const t = [], n = e.match(/```(?:json)?\s*([\s\S]*?)```/i);
  n && t.push(n[1]);
  const i = e.match(/\{[\s\S]*?\}/);
  i && t.push(i[0]);
  for (const a of t)
    try {
      const c = JSON.parse(a.trim()), u = fc(c.name, c.description);
      if (u) return u;
    } catch {
    }
  const o = e.match(/name\s*[:-]\s*(.+)/i)?.[1], s = e.match(/description\s*[:-]\s*(.+)/i)?.[1];
  return fc(o, s);
}
function fc(e, t) {
  const n = typeof e == "string" ? pc(e) : void 0, i = typeof t == "string" ? pc(t) : void 0;
  return n || i ? { name: n || void 0, description: i || void 0 } : null;
}
function pc(e) {
  return e.trim().replace(/,$/, "").trim().replace(/^["']/, "").replace(/["']$/, "").trim();
}
function Z0(e, t) {
  return e.rootActivityVersionId ?? md(t, e.rootKind)?.activityVersionId ?? null;
}
function md(e, t) {
  return e.find((n) => G0(n) === t);
}
function G0(e) {
  return e ? J0(e) ? "flowchart" : Q0(e) ? "sequence" : null : null;
}
function Tr(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const i = n.category?.trim() || "Uncategorized";
    t.set(i, [...t.get(i) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [i]) => n.localeCompare(i)).map(([n, i]) => ({
    category: n,
    activities: i.sort((o, s) => Ee(o).localeCompare(Ee(s)))
  }));
}
function J0(e) {
  return Ee(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function Q0(e) {
  return Ee(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function eb(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function xd(e) {
  return rb(e.rootActivityType) || e.rootActivityType;
}
function tb(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function nb(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function ib(e, t) {
  return hc(t) - hc(e);
}
function hc(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function wd(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
function vd(e) {
  const t = e?.workflowExecutionId ?? e?.runId ?? e?.executionId;
  return typeof t == "string" && t.trim() ? t : null;
}
async function ob(e) {
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
function rb(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function sb(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    bi(t, n.typeName, n), bi(t, n.name, n), bi(t, n.displayName, n);
    const i = n.typeName.split(".").filter(Boolean).at(-1);
    bi(t, i, n);
  }
  return t;
}
function ab(e, t, n) {
  const i = t.get(e.activityVersionId);
  return n.get(jn(i?.activityTypeKey)) ?? n.get(jn($t(i?.activityTypeKey))) ?? n.get(jn(i?.displayName)) ?? n.get(jn(e.activityVersionId)) ?? null;
}
function bi(e, t, n) {
  const i = jn(t);
  i && !e.has(i) && e.set(i, n);
}
function jn(e) {
  return e?.trim().toLowerCase() ?? "";
}
function gc(e, t, n, i) {
  const o = ho();
  if (!o) return t;
  const s = o.getItem(e);
  if (s == null) return t;
  const a = Number(s);
  return Number.isFinite(a) ? Di(a, n, i) : t;
}
function yc(e, t) {
  const n = ho();
  if (!n) return t;
  const i = n.getItem(e);
  return i === "true" ? !0 : i === "false" ? !1 : t;
}
function cb() {
  const e = ho();
  if (!e) return null;
  const t = e.getItem(pd);
  return t === "palette" || t === "inspector" ? t : null;
}
function ho() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function mn(e, t) {
  const n = ho();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function Di(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function bd(e) {
  switch (lb(e)) {
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
function lb(e) {
  return (e ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}
function ub(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function mc(e) {
  return `${Ee(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function xc(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function db(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function Nd(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function fb(e) {
  const t = Nd(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function pb(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function Fe(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function hb(e) {
  return Cd(Fe(e));
}
function gb(e, t) {
  const n = t?.get(e.activityVersionId);
  return n ? Ee(n) : $t(e.activityVersionId) ?? e.activityVersionId;
}
function yb(e, t) {
  if (e.cardinality === "single") {
    const i = e.activities[0];
    return i ? gb(i, t) : "Empty — click to choose";
  }
  const n = e.activities.length;
  return `${n} activit${n === 1 ? "y" : "ies"}`;
}
function jd(e, t, n = []) {
  if (!e) return n;
  const i = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: i?.activityTypeKey ?? e.activityVersionId,
    displayName: i ? Ee(i) : void 0
  });
  for (const o of Me(e, t))
    for (const s of o.activities) jd(s, t, n);
  return n;
}
function Sd(e, t, n = []) {
  if (!e) return n;
  for (const i of cl(e))
    n.push({ source: i.source, target: i.target, sourcePort: i.sourceHandle ?? void 0, targetPort: i.targetHandle ?? void 0 });
  for (const i of Me(e, t))
    for (const o of i.activities) Sd(o, t, n);
  return n;
}
function Sn(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function mb(e) {
  return `${e.id}-${Cd(JSON.stringify(e.state))}`;
}
function Cd(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function ys(e) {
  return e.status.toLowerCase() === "rejected";
}
function xb(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function wb(e, t) {
  const n = e instanceof Error ? e.message : String(e);
  return vb(e, n) ? `Run ${t} was not found.` : n;
}
function vb(e, t) {
  if ((typeof e == "object" && e ? e.response?.status ?? e.status : void 0) === 404 || /\b404\b/.test(t)) return !0;
  try {
    const i = JSON.parse(t);
    return [i.error, i.title, i.detail].some((o) => typeof o == "string" && /not found/i.test(o));
  } catch {
    return /not found/i.test(t);
  }
}
function un({ status: e, subStatus: t }) {
  return /* @__PURE__ */ r.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
const Ed = { workflowActivity: bb }, Id = { workflow: jb };
function bb({ data: e, selected: t }) {
  const n = e, i = n.runtime, o = !n.suppressFlowPorts, s = o ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], a = Nb(n), u = tt.useContext(gd)?.({ activityVersionId: n.activityVersionId, activityTypeKey: n.activityTypeKey }) ?? null;
  return /* @__PURE__ */ r.jsxs(
    "div",
    {
      className: ["wf-node", t ? "selected" : "", i ? "wf-node-runtime" : "", i?.hasBlockingIncident ? "faulted" : "", u ? "wf-node-unavailable" : ""].filter(Boolean).join(" "),
      "data-icon": n.icon ?? "activity",
      children: [
        o && n.acceptsInbound ? /* @__PURE__ */ r.jsx(sn, { type: "target", position: ie.Left }) : null,
        u ? /* @__PURE__ */ r.jsx("span", { className: "wf-node-availability", title: `No longer available for new use · ${Li(u.state)}`, children: /* @__PURE__ */ r.jsx(Ti, { size: 13 }) }) : null,
        /* @__PURE__ */ r.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ r.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: hs(n.icon) }),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-node-copy", children: [
            /* @__PURE__ */ r.jsx("strong", { children: n.label }),
            a ? /* @__PURE__ */ r.jsx("small", { children: a }) : null
          ] })
        ] }),
        n.childSlots.length > 0 ? n.onEnterSlot ? /* @__PURE__ */ r.jsx("span", { className: "wf-node-slot-list", children: n.childSlots.map((l) => /* @__PURE__ */ r.jsx(
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
        )) }) : /* @__PURE__ */ r.jsxs("span", { className: "wf-node-slot-badge", children: [
          n.childSlots.length,
          " slot",
          n.childSlots.length === 1 ? "" : "s"
        ] }) : null,
        i ? /* @__PURE__ */ r.jsxs("div", { className: "wf-node-runtime-strip", children: [
          i.status ? /* @__PURE__ */ r.jsx(un, { status: i.status, subStatus: i.subStatus }) : null,
          i.incidentCount > 0 ? /* @__PURE__ */ r.jsxs("span", { className: "wf-node-runtime-count", children: [
            i.incidentCount,
            " incident",
            i.incidentCount === 1 ? "" : "s"
          ] }) : null,
          i.faultCount > 0 ? /* @__PURE__ */ r.jsxs("span", { className: "wf-node-runtime-count", children: [
            i.faultCount,
            " faults"
          ] }) : null
        ] }) : null,
        s.map((l, d) => {
          const f = `${(d + 1) / (s.length + 1) * 100}%`;
          return /* @__PURE__ */ r.jsxs(tt.Fragment, { children: [
            /* @__PURE__ */ r.jsx("span", { className: "wf-node-port-label", style: { top: f }, children: l.displayName }),
            /* @__PURE__ */ r.jsx(sn, { type: "source", position: ie.Right, id: l.name, style: { top: f } })
          ] }, l.name);
        })
      ]
    }
  );
}
function Nb(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((o) => !!o).join(" · ");
}
function jb(e) {
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
  } = e, p = tt.useContext(hd), [h, y] = B(!1), [w, x, m] = Ui({ sourceX: n, sourceY: i, targetX: o, targetY: s, sourcePosition: a, targetPosition: c }), b = p?.highlightedEdgeId === t;
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsx(
      Jn,
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
    p ? /* @__PURE__ */ r.jsx(Kv, { children: /* @__PURE__ */ r.jsxs(
      "div",
      {
        className: ["wf-edge-actions", h ? "visible" : "", b ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${x}px, ${m}px)` },
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1),
        children: [
          /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (g) => p.requestInsertActivity(t, g.clientX, g.clientY), children: /* @__PURE__ */ r.jsx(Gt, { size: 12 }) }),
          /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => p.deleteEdge(t), children: /* @__PURE__ */ r.jsx(An, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function Sb({ clientX: e, clientY: t, activities: n, onPick: i, onClose: o }) {
  const [s, a] = B(""), [c, u] = B(0), l = ne(null), d = ne(null), f = de(() => {
    const b = s.trim().toLowerCase(), g = n.filter(eb);
    return b ? g.filter((v) => Ee(v).toLowerCase().includes(b) || v.activityTypeKey.toLowerCase().includes(b) || (v.category ?? "").toLowerCase().includes(b) || (v.description ?? "").toLowerCase().includes(b)) : g;
  }, [n, s]), p = de(() => Tr(f), [f]), h = de(() => p.flatMap((b) => b.activities), [p]);
  G(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), G(() => {
    const b = (v) => {
      l.current?.contains(v.target) || o();
    }, g = (v) => {
      v.key === "Escape" && o();
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
  }, w = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), x = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let m = -1;
  return /* @__PURE__ */ r.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: w, top: x }, onMouseDown: (b) => b.stopPropagation(), onClick: (b) => b.stopPropagation(), children: [
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
        m += 1;
        const v = m, j = v === c;
        return /* @__PURE__ */ r.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": j,
            className: j ? "active" : "",
            onMouseEnter: () => u(v),
            onClick: () => i(g),
            children: [
              /* @__PURE__ */ r.jsx("strong", { children: Ee(g) }),
              /* @__PURE__ */ r.jsx("small", { children: g.category || g.activityTypeKey })
            ]
          },
          g.activityVersionId
        );
      })
    ] }, b.category)) })
  ] });
}
function $i({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: i
}) {
  const o = uf(t.map((s) => s.id), n, i);
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
function wc(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
const Cb = "Expressions/UnresolvedVariable";
function Eb(e) {
  return String(e.type ?? e.code ?? "");
}
function Ib(e) {
  return Eb(e) === Cb;
}
function kb(e) {
  const t = String(e ?? "").trim().split("/").filter(Boolean), [n, i, ...o] = t, s = n === "$workflow";
  return {
    nodeId: !n || s ? null : n,
    isWorkflowScope: s,
    bag: i === "inputs" || i === "outputs" || i === "variables" ? i : null,
    referenceKey: o.length > 0 ? o.join("/") : null
  };
}
function Ab(e) {
  return (e ?? []).filter(Ib).map((t) => ({
    error: t,
    path: kb(t.path),
    message: t.message ?? "Variable reference is not visible from this activity's scope."
  }));
}
function _b({ draft: e, onRepair: t }) {
  const n = e.validationErrors;
  if (!n.length)
    return /* @__PURE__ */ r.jsxs("div", { className: "wf-validation ok", children: [
      /* @__PURE__ */ r.jsx(an, { size: 14 }),
      " No validation errors"
    ] });
  const i = Ab(n), o = new Map(i.map((s) => [s.error, s]));
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-validation-summary", children: [
      /* @__PURE__ */ r.jsx(wt, { size: 14 }),
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
          /* @__PURE__ */ r.jsx(Qd, { size: 12 }),
          " Repair"
        ] }) : null
      ] }, a);
    }) })
  ] });
}
function Db({
  testRun: e,
  onOpenDetails: t
}) {
  const n = ys(e);
  return /* @__PURE__ */ r.jsx("div", { className: "wf-test-run-status", "data-state": n ? "rejected" : "accepted", children: /* @__PURE__ */ r.jsxs(
    "button",
    {
      type: "button",
      className: "wf-test-run-trigger",
      onClick: t,
      children: [
        n ? /* @__PURE__ */ r.jsx(wt, { size: 16 }) : /* @__PURE__ */ r.jsx(an, { size: 16 }),
        n ? "Test run rejected" : "Test run dispatched"
      ]
    }
  ) });
}
function $b({ testRun: e, onOpenRun: t }) {
  if (!e)
    return /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Run the draft to see Runtime Evidence." }) });
  const n = ys(e), i = e.workflowExecutionId;
  return /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ r.jsxs("section", { className: "wf-runtime-card", "data-state": n ? "rejected" : "accepted", children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("span", { children: "Latest Test Run" }),
        /* @__PURE__ */ r.jsx("h3", { children: n ? "Rejected by the server" : "Transient run accepted" })
      ] }),
      /* @__PURE__ */ r.jsx(un, { status: e.status, subStatus: e.commandDispatchStatus ?? void 0 })
    ] }),
    /* @__PURE__ */ r.jsx("p", { children: "Ephemeral - not saved, promoted, or published." }),
    n && e.reason ? /* @__PURE__ */ r.jsxs("div", { className: "wf-runtime-reason", children: [
      /* @__PURE__ */ r.jsx(wt, { size: 14 }),
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
        /* @__PURE__ */ r.jsx("dd", { children: vc(e.activityCount, "activity") })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ r.jsx("dd", { children: vc(e.incidentCount, "incident") })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ r.jsx("dd", { title: e.expiresAt ? Pe(e.expiresAt) : "None", children: e.expiresAt ? Pe(e.expiresAt) : "None" })
      ] })
    ] })
  ] }) });
}
function vc(e, t) {
  return typeof e != "number" ? "Available on linked Run" : `${e} ${t}${e === 1 ? "" : "s"}`;
}
function ms({ rows: e = 5 }) {
  return /* @__PURE__ */ r.jsx("div", { className: "wf-grid", "aria-busy": "true", "aria-label": "Loading", children: Array.from({ length: e }).map((t, n) => /* @__PURE__ */ r.jsx("div", { className: "wf-skeleton wf-skeleton-row", style: { width: `${90 - n % 3 * 12}%` } }, n)) });
}
function xs({ icon: e, title: t, description: n, action: i }) {
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-empty-state", role: "status", children: [
    /* @__PURE__ */ r.jsx("div", { className: "wf-empty-state-icon", "aria-hidden": !0, children: e ?? /* @__PURE__ */ r.jsx(Fn, { size: 22 }) }),
    /* @__PURE__ */ r.jsx("h3", { children: t }),
    n ? /* @__PURE__ */ r.jsx("p", { children: n }) : null,
    i ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty-state-action", children: i }) : null
  ] });
}
function Qn({ message: e, title: t = "Something went wrong" }) {
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-error-card", role: "alert", children: [
    /* @__PURE__ */ r.jsx(wt, { size: 18 }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-error-card-body", children: [
      /* @__PURE__ */ r.jsx("strong", { children: t }),
      /* @__PURE__ */ r.jsx("span", { children: e || "Please try again, or check that the Elsa server is reachable." })
    ] })
  ] });
}
function kd({ status: e, run: t, compact: n = !1 }) {
  const i = () => {
    t?.workflowExecutionId && (window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(t.workflowExecutionId)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  };
  return /* @__PURE__ */ r.jsxs("div", { className: `wf-status-line${n ? " compact" : ""}`, children: [
    /* @__PURE__ */ r.jsx(an, { size: n ? 13 : 14 }),
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
      await ob(e), i(n);
    } catch {
      o(n);
    }
  };
  return /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (a) => {
    s(a);
  }, children: /* @__PURE__ */ r.jsx(ef, { size: 12 }) });
}
function Tb({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: i }) {
  const [o, s] = B("loading"), [a, c] = B(""), [u, l] = B(""), [d, f] = B(null), [p, h] = B([]), y = n?.trim().toLowerCase() ?? "", w = de(
    () => y ? p.filter((S) => tb(S, y)) : p,
    [y, p]
  ), x = de(
    () => Array.from(new Set(p.flatMap((S) => [
      S.definitionId,
      S.definitionVersionId,
      S.sourceId
    ]).filter((S) => !!S))).sort((S, I) => S.localeCompare(I)),
    [p]
  ), m = Tt(t, "weaver.workflows.explain-executable"), b = re(async () => {
    s("loading"), c("");
    try {
      h(await hl(e)), s("ready");
    } catch (S) {
      c(S instanceof Error ? S.message : String(S)), s("failed");
    }
  }, [e]);
  G(() => {
    b();
  }, [b]);
  const g = async (S) => {
    l(""), f(null), c("");
    try {
      const I = await pl(e, S.artifactId), D = vd(I);
      f({ artifactId: S.artifactId, workflowExecutionId: D }), l(`Started ${S.artifactId}`);
    } catch (I) {
      c(I instanceof Error ? I.message : String(I));
    }
  }, v = (S) => {
    m && Pt(t, m, S) && (c(""), f(null), l(`Sent ${S.artifactId} to Weaver`));
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
        /* @__PURE__ */ r.jsx(Gi, { size: 14 }),
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
      /* @__PURE__ */ r.jsx("datalist", { id: "wf-executable-definition-options", children: x.map((S) => /* @__PURE__ */ r.jsx("option", { value: S }, S)) }),
      n ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => i(null), children: [
        /* @__PURE__ */ r.jsx(Lc, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    o === "failed" ? /* @__PURE__ */ r.jsx(Qn, { message: a }) : null,
    u ? /* @__PURE__ */ r.jsx(kd, { status: u, run: d }) : null,
    o === "loading" ? /* @__PURE__ */ r.jsx(ms, {}) : null,
    o === "ready" && w.length === 0 ? /* @__PURE__ */ r.jsx(
      xs,
      {
        icon: /* @__PURE__ */ r.jsx(Zt, { size: 22 }),
        title: "No workflow executables",
        description: n ? "No executables match this definition filter." : "Publish a workflow definition to make it executable."
      }
    ) : null,
    o === "ready" && w.length > 0 ? /* @__PURE__ */ r.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ r.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ r.jsx("span", { children: "Version" }),
        /* @__PURE__ */ r.jsx("span", { children: "Source" }),
        /* @__PURE__ */ r.jsx("span", { children: "Root" }),
        /* @__PURE__ */ r.jsx("span", { children: "Published" }),
        /* @__PURE__ */ r.jsx("span", { children: "Actions" })
      ] }),
      w.map((S) => /* @__PURE__ */ r.jsxs("div", { className: "wf-grid-row", role: "row", children: [
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
        /* @__PURE__ */ r.jsx(Pb, { executable: S, onCopied: j, onCopyFailed: N }),
        /* @__PURE__ */ r.jsx("span", { children: xd(S) }),
        /* @__PURE__ */ r.jsx("span", { children: Pe(S.publishedAt ?? S.createdAt) }),
        /* @__PURE__ */ r.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
            g(S);
          }, children: [
            /* @__PURE__ */ r.jsx(Zt, { size: 13 }),
            " Run"
          ] }),
          m ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => v(S), children: [
            /* @__PURE__ */ r.jsx(lt, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, S.artifactId))
    ] }) : null
  ] });
}
function Pb({ executable: e, onCopied: t, onCopyFailed: n }) {
  const i = e.sourceId || e.definitionVersionId || e.definitionId, o = e.sourceVersion;
  return /* @__PURE__ */ r.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ r.jsx("span", { className: "wf-source-kind", children: wd(e.sourceKind) }),
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
function Mb({ context: e, ai: t, definitionId: n, publishedArtifactId: i }) {
  const [o, s] = B("loading"), [a, c] = B(""), [u, l] = B(""), [d, f] = B(null), [p, h] = B([]), y = Tt(t, "weaver.workflows.explain-executable"), w = re(async () => {
    s("loading"), c("");
    try {
      const j = await hl(e);
      h(j.filter((N) => nb(N, n)).sort(ib)), s("ready");
    } catch (j) {
      c(j instanceof Error ? j.message : String(j)), h([]), s("failed");
    }
  }, [e, n]);
  G(() => {
    w();
  }, [w, i]);
  const x = async (j) => {
    l(""), f(null), c("");
    try {
      const N = await pl(e, j.artifactId);
      f({ artifactId: j.artifactId, workflowExecutionId: vd(N) }), l(`Started ${j.artifactId}`);
    } catch (N) {
      c(N instanceof Error ? N.message : String(N));
    }
  }, m = (j) => {
    y && Pt(t, y, j) && (c(""), f(null), l(`Sent ${j.artifactId} to Weaver`));
  }, b = () => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(n)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, g = (j) => {
    c(""), f(null), l(`Copied ${j}`);
  }, v = (j) => {
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
        w();
      }, children: [
        /* @__PURE__ */ r.jsx(Fr, { size: 13 }),
        " Refresh"
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: b, children: "Open list" })
    ] }),
    o === "failed" ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ r.jsx(wt, { size: 14 }),
      " ",
      a
    ] }) : null,
    u ? /* @__PURE__ */ r.jsx(kd, { status: u, run: d, compact: !0 }) : null,
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
        /* @__PURE__ */ r.jsx("span", { children: Pe(j.publishedAt ?? j.createdAt) })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "wf-artifact-card-values", children: [
        /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line", children: [
          /* @__PURE__ */ r.jsx("code", { title: j.artifactId, children: j.artifactId }),
          /* @__PURE__ */ r.jsx(Et, { value: j.artifactId, ariaLabel: `Copy artifact ID ${j.artifactId}`, copiedLabel: "artifact ID", onCopied: g, onCopyFailed: v })
        ] }),
        /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ r.jsx("code", { title: j.artifactHash, children: j.artifactHash }),
          /* @__PURE__ */ r.jsx(Et, { value: j.artifactHash, ariaLabel: `Copy artifact hash ${j.artifactHash}`, copiedLabel: "artifact hash", onCopied: g, onCopyFailed: v })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("dl", { children: [
        /* @__PURE__ */ r.jsxs("div", { children: [
          /* @__PURE__ */ r.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ r.jsxs("dd", { children: [
            wd(j.sourceKind),
            " ",
            j.sourceVersion ? `v${j.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("div", { children: [
          /* @__PURE__ */ r.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ r.jsx("dd", { children: xd(j) })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
          x(j);
        }, children: [
          /* @__PURE__ */ r.jsx(Zt, { size: 13 }),
          " Run"
        ] }),
        y ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => m(j), children: [
          /* @__PURE__ */ r.jsx(lt, { size: 13 }),
          " Explain"
        ] }) : null
      ] })
    ] }, j.artifactId)) }) : null
  ] });
}
function Ad() {
  const [e, t] = B(() => gc(ac, X0, bn, Nn)), [n, i] = B(() => gc(cc, Y0, Xt, Yt)), [o, s] = B(() => yc(lc, !1)), [a, c] = B(() => yc(uc, !1)), [u, l] = B(cb);
  G(() => {
    mn(ac, String(e));
  }, [e]), G(() => {
    mn(cc, String(n));
  }, [n]), G(() => {
    mn(lc, String(o));
  }, [o]), G(() => {
    mn(uc, String(a));
  }, [a]), G(() => {
    mn(pd, u);
  }, [u]), G(() => {
    if (!u) return;
    const g = (v) => {
      v.key === "Escape" && l(null);
    };
    return window.addEventListener("keydown", g), () => window.removeEventListener("keydown", g);
  }, [u]);
  const d = re((g) => {
    l((v) => v === g ? null : v), g === "palette" ? s((v) => !v) : c((v) => !v);
  }, []), f = re((g) => {
    g === "palette" ? s(!1) : c(!1), l((v) => v === g ? null : g);
  }, []), p = re((g, v) => {
    l(null), g === "palette" ? (s(!1), t((j) => Di(j + v, bn, Nn))) : (c(!1), i((j) => Di(j + v, Xt, Yt)));
  }, []), h = re((g, v) => {
    v.preventDefault(), l(null), g === "palette" ? s(!1) : c(!1);
    const j = v.clientX, N = g === "palette" ? e : n, S = g === "palette" ? bn : Xt, I = g === "palette" ? Nn : Yt;
    document.body.classList.add("wf-side-panel-resizing");
    const D = (A) => {
      const k = g === "palette" ? A.clientX - j : j - A.clientX, R = Di(N + k, S, I);
      g === "palette" ? t(R) : i(R);
    }, M = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", D), window.removeEventListener("pointerup", M), window.removeEventListener("pointercancel", M);
    };
    window.addEventListener("pointermove", D), window.addEventListener("pointerup", M), window.addEventListener("pointercancel", M);
  }, [n, e]), y = re((g, v) => {
    v.key === "ArrowLeft" ? (v.preventDefault(), p(g, g === "palette" ? -vi : vi)) : v.key === "ArrowRight" ? (v.preventDefault(), p(g, g === "palette" ? vi : -vi)) : v.key === "Home" ? (v.preventDefault(), g === "palette" ? t(bn) : i(Xt)) : v.key === "End" && (v.preventDefault(), g === "palette" ? t(Nn) : i(Yt));
  }, [p]), w = !o && u !== "inspector", x = !a && u !== "palette", m = [
    "wf-editor-body",
    o ? "palette-collapsed" : "",
    a ? "inspector-collapsed" : "",
    u === "palette" ? "palette-maximized" : "",
    u === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), b = {
    "--wf-palette-width": `${o ? dc : e}px`,
    "--wf-inspector-width": `${a ? dc : n}px`
  };
  return {
    paletteWidth: e,
    inspectorWidth: n,
    paletteCollapsed: o,
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
const Rb = 50;
function bc() {
  return { past: [], future: [] };
}
function zb(e) {
  return e.past.length > 0;
}
function Lb(e) {
  return e.future.length > 0;
}
function Nc(e, t, n = Rb) {
  const i = [...e.past, t];
  return i.length > n && i.splice(0, i.length - n), { past: i, future: [] };
}
function Vb(e, t) {
  if (e.past.length === 0) return null;
  const n = e.past.slice(), i = n.pop();
  return { history: { past: n, future: [...e.future, t] }, snapshot: i };
}
function Ob(e, t) {
  if (e.future.length === 0) return null;
  const n = e.future.slice(), i = n.pop();
  return { history: { past: [...e.past, t], future: n }, snapshot: i };
}
function Hb({ draft: e, restoreDraft: t }) {
  const n = ne(bc()), i = ne(null), o = ne(""), s = ne(!1), [a, c] = B(0), u = re((w) => {
    n.current = bc(), i.current = w ? Sn(w) : null, o.current = w ? Fe(w) : "", s.current = !1, c(0);
  }, []);
  G(() => {
    if (!e) return;
    if (s.current) {
      s.current = !1;
      return;
    }
    const w = Fe(e);
    if (w === o.current) return;
    const x = window.setTimeout(() => {
      const m = i.current;
      m && (n.current = Nc(n.current, m), c((b) => b + 1)), i.current = Sn(e), o.current = w;
    }, F0);
    return () => window.clearTimeout(x);
  }, [e]);
  const l = re(() => {
    if (!e) return;
    const w = Fe(e);
    if (w === o.current) return;
    const x = i.current;
    x && (n.current = Nc(n.current, x)), i.current = Sn(e), o.current = w;
  }, [e]), d = re((w) => {
    s.current = !0, i.current = Sn(w), o.current = Fe(w), t(w), c((x) => x + 1);
  }, [t]), f = re(() => {
    if (!e) return;
    l();
    const w = Vb(n.current, e);
    w && (n.current = w.history, d(w.snapshot));
  }, [e, l, d]), p = re(() => {
    if (!e) return;
    l();
    const w = Ob(n.current, e);
    w && (n.current = w.history, d(w.snapshot));
  }, [e, l, d]), { canUndoNow: h, canRedoNow: y } = de(() => {
    const w = !!e && !!i.current && Fe(e) !== o.current;
    return {
      canUndoNow: zb(n.current) || w,
      canRedoNow: Lb(n.current) && !w
    };
  }, [e, a]);
  return { resetHistory: u, undo: f, redo: p, canUndoNow: h, canRedoNow: y };
}
const Wb = {
  draft: null,
  frames: [],
  selectedNodeId: null,
  testRun: null,
  publishedArtifactId: null
};
function Fb(e, t) {
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
function Bb() {
  const [e, t] = Ud(Fb, Wb), n = de(() => ({
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
    enterSlot(i, o, s) {
      t({ type: "slotEntered", frame: { ownerNodeId: i.nodeId, slotId: o, label: s } });
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
const Kb = 320, Xb = 140;
function Yb(e, t, n) {
  return n === "sequence" ? qb(e) : Ub(e, t);
}
function qb(e) {
  const t = /* @__PURE__ */ new Map();
  return e.forEach((n, i) => {
    t.set(n.id, { x: i * 280, y: 0 });
  }), t;
}
function Ub(e, t) {
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
      n.set(p, { x: d * Kb, y: h * Xb });
    });
  return n;
}
function Zb(e, t, n, i, o) {
  if (!e) return { kind: "becomeRoot" };
  const s = $n(e, t, o);
  if (!s)
    return t.length > 0 ? { kind: "staleFrames" } : Me(n, i)[0] ? { kind: "wrapRoot" } : { kind: "leafError" };
  const a = s.slot.cardinality === "single" ? s.slot.activities.find((c) => c.nodeId !== n.nodeId) ?? null : null;
  return { kind: "addToSlot", slot: s.slot, replacedActivity: a };
}
const Gb = "root";
function Jb(e) {
  return e.length === 0 ? Gb : e.map((t) => `${t.ownerNodeId}:${t.slotId}`).join("/");
}
function Qb(e, t) {
  return e.length === t.length && e.every((n, i) => n.id === t[i]);
}
function eN({
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
  setError: w
}) {
  const [x, m] = B([]), [b, g] = B([]), [v, j] = B(null), [N, S] = B(null), [I, D] = B(null), M = ne(null), A = ne(null), k = ne(/* @__PURE__ */ new Map()), R = ne(/* @__PURE__ */ new Set()), C = ne(null), _ = ne([]), E = ne(null), $ = ne(null), P = ne(!1), T = de(() => Jb(i), [i]);
  G(() => () => {
    v && k.current.set(T, v.getViewport());
  }, [v, T]), G(() => {
    if (C.current = T, !n) {
      _.current = [], m([]), g([]);
      return;
    }
    const L = a ? nl(n, o, e?.layout ?? []) : t ? tl(t, o, e?.layout ?? []) : { nodes: [], edges: [] };
    _.current = L.nodes.map((W) => W.id), m(L.nodes), g(L.edges);
  }, [o, e?.layout, a, t, n, T]), G(() => {
    if (!v || C.current !== T || !Qb(x, _.current)) return;
    C.current = null;
    const L = k.current.get(T), W = R.current.has(T);
    R.current.add(T), window.requestAnimationFrame(() => {
      L ? v.setViewport(L) : !W && x.length > 0 && v.fitView({ padding: 0.2 });
    });
  }, [x, v, T]);
  const F = re((L, W, K) => K ? [
    ...L.filter((te) => te.nodeId !== W),
    { nodeId: W, x: Math.round(K.x), y: Math.round(K.y) }
  ] : L, []), H = re((L, W) => {
    if (e?.state.rootActivity && a)
      return;
    const K = wr(L, mc(L)), te = Zb(e?.state.rootActivity, i, K, L, s);
    if (te.kind === "becomeRoot") {
      f(
        ({ draft: se }) => se ? { ...se, state: { ...se.state, rootActivity: K } } : null,
        K.nodeId
      );
      return;
    }
    if (te.kind === "leafError") {
      y(""), w("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
      return;
    }
    if (te.kind === "staleFrames") {
      y(""), w("This slot could not be resolved — returning to the workflow root."), h();
      return;
    }
    if (te.kind === "wrapRoot") {
      f(({ draft: se }) => {
        const he = se?.state.rootActivity;
        return he ? {
          ...se,
          layout: F(se.layout, he.nodeId, W),
          state: { ...se.state, rootActivity: Ls(K, [], [he], L) }
        } : null;
      }, e?.state.rootActivity?.nodeId ?? null), w(""), y(`Wrapped root in ${Ee(L)}`);
      return;
    }
    f(({ draft: se, frames: he }) => {
      if (!se?.state.rootActivity) return null;
      const ye = $n(se.state.rootActivity, he, s);
      if (!ye) return null;
      const ke = ye.slot.cardinality === "single" ? [K] : [...ye.slot.activities, K], Ce = Ls(se.state.rootActivity, he, ke, s);
      return {
        ...se,
        layout: F(se.layout, K.nodeId, W),
        state: { ...se.state, rootActivity: Ce }
      };
    }, K.nodeId), te.replacedActivity && (w(""), y(`Replaced ${te.slot.label} content`));
  }, [s, e?.state.rootActivity, i, a, f, h, F, w, y]), O = re((L, W) => {
    const K = wr(L, mc(L)), te = {
      id: K.nodeId,
      type: "workflowActivity",
      position: W,
      selected: !0,
      data: {
        label: Ee(L),
        activityVersionId: L.activityVersionId,
        activityTypeKey: L.activityTypeKey,
        category: L.category,
        executionType: L.executionType,
        icon: eo(L),
        childSlots: Me(K, L),
        acceptsInbound: String(L.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: ll(K, L)
      }
    };
    return { activityNode: K, node: te };
  }, []), X = re((L, W, K = []) => {
    a || d(({ draft: te, frames: se }) => {
      if (!te) return null;
      const he = Qf(te.layout, L), ye = te.state.rootActivity;
      if (!ye) return { ...te, layout: he };
      const ke = $n(ye, se, s);
      if (!ke) return { ...te, layout: he };
      const Ce = Gf(ke, L, W, K), ze = ke.slot.mode === "flowchart" ? Jf(Ce, W) : Ce;
      return {
        ...te,
        layout: he,
        state: {
          ...te.state,
          rootActivity: Zf(ye, se, ze, s)
        }
      };
    });
  }, [s, a, d]), q = re((L, W) => {
    if (!M.current) return null;
    const K = M.current.getBoundingClientRect();
    return v ? v.screenToFlowPosition({ x: L, y: W }) : {
      x: L - K.left,
      y: W - K.top
    };
  }, [v]), ee = re((L, W) => document.elementFromPoint(L, W)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), le = re((L, W, K) => {
    const te = x.find((xe) => xe.id === W.source), se = x.find((xe) => xe.id === W.target), he = te && se ? db(te, se) : te ? xc(te) : K, ye = O(L, he), Ce = [...x.map((xe) => xe.selected ? { ...xe, selected: !1 } : xe), ye.node], ze = ap(b, W, ye.node.id);
    m(Ce), g(ze), p(ye.node.id), X(Ce, ze, [ye.activityNode]);
  }, [X, O, b, x, p]), U = re((L, W, K) => {
    if (!u || !M.current) return !1;
    const te = M.current.getBoundingClientRect();
    if (!(W >= te.left && W <= te.right && K >= te.top && K <= te.bottom)) return !1;
    const he = q(W, K);
    if (!he) return !1;
    if (c) {
      const ye = ee(W, K), ke = ye ? b.find((Ce) => Ce.id === ye) : void 0;
      if (ke)
        return le(L, ke, he), !0;
    }
    return H(L, he), !0;
  }, [H, u, b, ee, c, le, q]);
  G(() => {
    const L = (K) => {
      const te = E.current;
      if (!te) return;
      Math.hypot(K.clientX - te.startX, K.clientY - te.startY) >= H0 && (te.dragging = !0);
    }, W = (K) => {
      const te = E.current;
      if (E.current = null, !te?.dragging || !M.current || $.current) return;
      const se = M.current.getBoundingClientRect();
      K.clientX >= se.left && K.clientX <= se.right && K.clientY >= se.top && K.clientY <= se.bottom && (P.current = !0, window.setTimeout(() => {
        P.current = !1;
      }, 0), U(te.activity, K.clientX, K.clientY));
    };
    return window.addEventListener("pointermove", L), window.addEventListener("pointerup", W), window.addEventListener("pointercancel", W), () => {
      window.removeEventListener("pointermove", L), window.removeEventListener("pointerup", W), window.removeEventListener("pointercancel", W);
    };
  }, [v, U]);
  const z = (L, W) => {
    $.current = { activityVersionId: W.activityVersionId, handledDrop: !1 }, L.dataTransfer.setData(sc, W.activityVersionId), L.dataTransfer.setData("text/plain", W.activityVersionId), L.dataTransfer.effectAllowed = "copy";
  }, Y = (L, W) => {
    const K = $.current;
    $.current = null, !K?.handledDrop && (L.clientX === 0 && L.clientY === 0 || U(W, L.clientX, L.clientY) && (P.current = !0, window.setTimeout(() => {
      P.current = !1;
    }, 0)));
  }, ae = (L, W) => {
    L.button === 0 && (E.current = {
      activity: W,
      startX: L.clientX,
      startY: L.clientY,
      dragging: !1
    });
  }, ce = (L) => {
    P.current || u && H(L);
  }, J = (L) => {
    if (!u) {
      L.dataTransfer.dropEffect = "none";
      return;
    }
    if (L.preventDefault(), L.dataTransfer.dropEffect = "copy", !c) return;
    const W = ee(L.clientX, L.clientY);
    D(W);
  }, oe = (L) => {
    if (!M.current) return;
    const W = L.relatedTarget;
    W && M.current.contains(W) || D(null);
  }, fe = (L) => {
    L.preventDefault(), D(null);
    const W = L.dataTransfer.getData(sc) || L.dataTransfer.getData("text/plain");
    if (!W || (L.stopPropagation(), $.current?.activityVersionId === W && ($.current.handledDrop = !0), !u)) return;
    const K = s.get(W);
    K && U(K, L.clientX, L.clientY);
  }, V = (L) => {
    if (!u) return;
    if (L) {
      S({ kind: "fromEmpty", clientX: L.clientX, clientY: L.clientY });
      return;
    }
    const W = M.current?.getBoundingClientRect();
    W && S({
      kind: "fromEmpty",
      clientX: W.left + W.width / 2,
      clientY: W.top + W.height / 2
    });
  }, Q = (L) => {
    const W = a ? L.filter((K) => K.type === "select") : L;
    W.length !== 0 && m((K) => _u(W, K));
  }, ge = (L) => {
    a || g((W) => Du(L, W));
  }, me = (L) => !L.source || !L.target || L.source === L.target || !c ? !1 : !L.targetHandle, Re = (L) => {
    if (!e?.state.rootActivity || !t || !c || !me(L)) return;
    const W = Mi(L.source, L.target, L.sourceHandle ?? "Done", L.targetHandle ?? void 0), K = Tu(W, b);
    g(K), X(x, K);
  }, _e = () => {
    X(x, b);
  }, $e = !a && x.length > 0, rt = re(() => {
    if (a || x.length === 0) return;
    const L = t?.slot.mode === "sequence" ? "sequence" : "flowchart", W = Yb(x, b, L), K = x.map((te) => {
      const se = W.get(te.id);
      return se ? { ...te, position: se } : te;
    });
    m(K), X(K, b), window.requestAnimationFrame(() => v?.fitView({ padding: 0.2 })), y("Rearranged the canvas.");
  }, [b, x, t, a, X, v, y]), Xe = (L, W) => {
    if (!W.nodeId || W.handleType === "target") {
      A.current = null;
      return;
    }
    A.current = {
      nodeId: W.nodeId,
      handleId: W.handleId ?? null
    };
  }, Ye = (L, W) => {
    const K = pb(A.current, W);
    if (A.current = null, !K || !c || W.toNode || W.toHandle || fb(L)) return;
    const te = Nd(L);
    S({
      kind: "fromPort",
      sourceNodeId: K.nodeId,
      sourceHandleId: K.handleId,
      clientX: te.x,
      clientY: te.y
    });
  }, Te = (L, W) => {
    if (!c || !me(W)) return;
    const K = Nw(L, {
      ...W,
      sourceHandle: W.sourceHandle ?? "Done",
      // Connection.targetHandle is string | null; null and undefined are indistinguishable downstream
      // (every consumer checks truthiness), so keep the type-correct null.
      targetHandle: W.targetHandle ?? null
    }, b, { shouldReplaceId: !1 });
    g(K), X(x, K);
  }, Oe = (L) => {
    if (a || L.length === 0) return;
    const W = new Set(L.map((se) => se.id)), K = x.filter((se) => !W.has(se.id)), te = b.filter((se) => !W.has(se.source) && !W.has(se.target));
    m(K), g(te), l && W.has(l) && p(null), X(K, te);
  }, He = (L) => {
    if (a || L.length === 0) return;
    const W = new Set(L.map((te) => te.id)), K = b.filter((te) => !W.has(te.id));
    g(K), X(x, K);
  }, Ne = re((L) => {
    if (a) return;
    const W = b.filter((K) => K.id !== L);
    g(W), X(x, W);
  }, [X, b, a, x]), Rt = re((L, W, K) => {
    c && S({ kind: "spliceEdge", edgeId: L, clientX: W, clientY: K });
  }, [c]), Ae = (L) => {
    const W = N;
    if (!W) return;
    S(null);
    const K = q(W.clientX, W.clientY) ?? { x: 0, y: 0 };
    if (W.kind === "fromEmpty") {
      const se = O(L, K), ye = [...x.map((ke) => ke.selected ? { ...ke, selected: !1 } : ke), se.node];
      m(ye), p(se.node.id), X(ye, b, [se.activityNode]);
      return;
    }
    if (W.kind === "fromPort") {
      const se = x.find((xe) => xe.id === W.sourceNodeId), he = se ? xc(se) : K, ye = O(L, he), Ce = [...x.map((xe) => xe.selected ? { ...xe, selected: !1 } : xe), ye.node], ze = [...b, Mi(W.sourceNodeId, ye.node.id, W.sourceHandleId ?? "Done")];
      m(Ce), g(ze), p(ye.node.id), X(Ce, ze, [ye.activityNode]);
      return;
    }
    const te = b.find((se) => se.id === W.edgeId);
    te && le(L, te, K);
  }, dn = de(() => ({
    highlightedEdgeId: I,
    deleteEdge: Ne,
    requestInsertActivity: Rt
  }), [Ne, I, Rt]);
  return {
    nodes: x,
    edges: b,
    canvasRef: M,
    setReactFlowInstance: j,
    connectMenu: N,
    setConnectMenu: S,
    edgeActions: dn,
    onNodesChange: Q,
    onEdgesChange: ge,
    onNodesDelete: Oe,
    onEdgesDelete: He,
    isValidConnection: me,
    onConnect: Re,
    onConnectStart: Xe,
    onConnectEnd: Ye,
    onReconnect: Te,
    commitLayout: _e,
    canAutoLayout: $e,
    autoLayout: rt,
    onCanvasDragOver: J,
    onCanvasDragLeave: oe,
    onCanvasDrop: fe,
    openEmptyConnectMenu: V,
    onConnectMenuPick: Ae,
    addActivity: H,
    onPaletteClick: ce,
    onPaletteDragStart: z,
    onPaletteDragEnd: Y,
    onPalettePointerDown: ae
  };
}
const tN = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function _d(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function ws(e) {
  return _d(e.name);
}
function nN(e, t) {
  const n = ws(t), i = e[n];
  return t.isWrapped === !1 ? i ?? t.defaultValue ?? "" : $d(i, t);
}
function Dd(e, t) {
  return $d(e[ws(t)], t);
}
function iN(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function oN(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function jc(e, t, n) {
  return {
    ...e,
    [ws(t)]: n
  };
}
function rN(e, t) {
  return t.isWrapped === !1 ? nN(e, t) : Dd(e, t).expression.value;
}
function $d(e, t) {
  return pN(e) ? {
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
const sN = /* @__PURE__ */ new Set([
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
function aN(e) {
  const t = e?.trim();
  if (!t) return null;
  const n = /^([\w.+]+)\[\]/.exec(t);
  if (n) return { elementTypeName: n[1] };
  const i = t.indexOf("`");
  if (i < 0) return null;
  const o = t.slice(0, i), s = (o.split(".").pop() ?? o).toLowerCase();
  return sN.has(s) ? { elementTypeName: cN(t.slice(i)) } : null;
}
function cN(e) {
  const t = /\[\[([\w.+]+)/.exec(e);
  if (t) return t[1];
  const n = /\[([\w.+]+)/.exec(e);
  return n ? n[1] : null;
}
function lN(e) {
  const t = e.uiHint?.toLowerCase();
  return t === "json" || t === "code" ? !0 : e.uiSpecifications?.repeater === !1;
}
function uN(e) {
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
function dN(e) {
  const t = (e ?? "").toLowerCase();
  return t === "system.boolean" || t === "boolean" || t === "bool" ? !1 : "";
}
function fN(e, t) {
  return {
    ...e,
    typeName: t ?? "System.String",
    isWrapped: !1,
    // The description documents the collection as a whole; repeating it on every row is noise.
    description: null
  };
}
function hr(e, t, n) {
  if (t === n || t < 0 || n < 0 || t >= e.length || n >= e.length) return e;
  const i = [...e], [o] = i.splice(t, 1);
  return i.splice(n, 0, o), i;
}
function pN(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
function Td(e) {
  return vs(e?.trim() ?? "") || e;
}
function vs(e) {
  if (!e) return "";
  const t = hN(e);
  if (!t) return "";
  const n = /^(.*)((?:\[\])+)$/.exec(t);
  if (n) return `${vs(n[1])}${n[2]}`;
  const i = t.indexOf("`");
  if (i >= 0) {
    const o = Sc(t.slice(0, i)), s = gN(t.slice(i));
    return s.length > 0 ? `${o}<${s.join(", ")}>` : o;
  }
  return Sc(t);
}
function hN(e) {
  let t = 0;
  for (let n = 0; n < e.length; n++) {
    const i = e[n];
    if (i === "[") t++;
    else if (i === "]") t--;
    else if (i === "," && t === 0) return e.slice(0, n).trim();
  }
  return e.trim();
}
function Sc(e) {
  const t = e.split(".").filter(Boolean).at(-1) ?? e;
  return t.split("+").filter(Boolean).at(-1) ?? t;
}
function gN(e) {
  const t = e.indexOf("[");
  if (t < 0) return [];
  const n = Cc(e, t);
  return n == null ? [] : yN(n).map((i) => {
    const o = i.trim(), s = o.startsWith("[") ? Cc(o, 0) ?? o : o;
    return vs(s);
  }).filter(Boolean);
}
function Cc(e, t) {
  let n = 0;
  for (let i = t; i < e.length; i++)
    if (e[i] === "[") n++;
    else if (e[i] === "]" && --n === 0) return e.slice(t + 1, i);
  return null;
}
function yN(e) {
  const t = [];
  let n = 0, i = 0;
  for (let o = 0; o < e.length; o++) {
    const s = e[o];
    s === "[" ? n++ : s === "]" ? n-- : s === "," && n === 0 && (t.push(e.slice(i, o)), i = o + 1);
  }
  return t.push(e.slice(i)), t.map((o) => o.trim()).filter(Boolean);
}
const Ec = "elsa-studio:apply-workflow-graph-operation-batch", Ic = "elsa-studio:undo-workflow-graph-operation-batch", mN = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function xN(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const i = EN(e), o = Md(i.state.rootActivity), s = /* @__PURE__ */ new Map(), a = /* @__PURE__ */ new Map(), c = [];
  for (const u of t.operations) {
    const l = CN(u.kind), d = u.parameters ?? {};
    if (l === "add-activity") {
      const f = Le(d.activityId) ?? u.temporaryReferences?.[0], p = SN(f ?? Le(d.displayName) ?? Le(d.activityType) ?? "weaver-activity", o), h = wN(u, p, n);
      a.set(p, h), c.push(p), f && s.set(f, p), i.state.rootActivity && vN(i.state.rootActivity, h);
      const y = xt(d.position) ? Pr(d.position, { x: 280, y: 160 }) : null;
      y && (i.layout = kc(i.layout, p, y));
      continue;
    }
    if (l === "set-root") {
      const f = gr(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown root activity.");
      i.state.rootActivity = f;
      continue;
    }
    if (l === "set-designer-position") {
      const f = Mt(d.activityId, s);
      if (!f || !bs(i.state.rootActivity, f)) throw new Error("Weaver batch referenced an unknown activity position.");
      i.layout = kc(i.layout, f, Pr(d, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const f = gr(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity property target.");
      jN(f, Le(d.propertyName) ?? "Value", d.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const f = gr(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity update target.");
      const p = xt(d.patch) ? d.patch : d;
      Object.assign(f, p);
      continue;
    }
    if (l === "remove-activity") {
      const f = Mt(d.activityId, s);
      if (!f) throw new Error("Weaver batch referenced an unknown activity remove target.");
      i.state.rootActivity = Pd(i.state.rootActivity, f), i.layout = i.layout.filter((p) => p.nodeId !== f);
      continue;
    }
    if (l === "connect-activities") {
      bN(i, d, s);
      continue;
    }
    if (l === "disconnect-activities") {
      NN(i, d, s);
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
function wN(e, t, n) {
  const i = e.parameters ?? {}, o = Le(i.activityVersionId) ?? Le(i.activityType) ?? "Elsa.Workflows.Activity", s = n.find((a) => a.activityVersionId === o || a.activityTypeKey === o || a.displayName === Le(i.displayName));
  return s ? wr(s, t) : {
    nodeId: t,
    // catalogItem is provably undefined here (the truthy branch returned above), so use the id directly.
    activityVersionId: o,
    inputs: [],
    outputs: [],
    ...Le(i.displayName) ? { displayName: Le(i.displayName) } : {},
    designer: { position: Pr(i.position, { x: 280, y: 160 }) }
  };
}
function vN(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = Ns(e);
  n && !n.some((i) => i.nodeId === t.nodeId) && n.push(t);
}
function bN(e, t, n) {
  const i = e.state.rootActivity;
  if (!i?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const o = Mt(t.sourceActivityId ?? t.sourceId ?? t.from, n), s = Mt(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!o || !s) throw new Error("Weaver batch connection is missing source or target activity.");
  const a = i.structure.payload, c = Array.isArray(a.connections) ? a.connections : [], u = Le(t.connectionId) ?? `flow-${o}-${s}`;
  a.connections = [
    ...c.filter((l) => !xt(l) || l.id !== u),
    {
      id: u,
      source: { nodeId: o, port: Le(t.outcome) ?? Le(t.sourcePort) ?? "Done" },
      target: { nodeId: s }
    }
  ];
}
function NN(e, t, n) {
  const i = e.state.rootActivity, o = i?.structure?.payload.connections;
  if (!Array.isArray(o)) return;
  const s = Le(t.connectionId), a = Mt(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = Mt(t.targetActivityId ?? t.targetId ?? t.to, n);
  i.structure.payload.connections = o.filter((u) => {
    if (!xt(u)) return !0;
    if (s && u.id === s) return !1;
    const l = xt(u.source) ? u.source.nodeId : void 0, d = xt(u.target) ? u.target.nodeId : void 0;
    return l !== a || d !== c;
  });
}
function jN(e, t, n) {
  const i = xt(n);
  e[_d(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: i ? "Object" : "Literal", value: n }
  };
}
function gr(e, t, n, i) {
  const o = Mt(t, n);
  return o ? bs(e.state.rootActivity, o) ?? i.get(o) ?? null : null;
}
function Mt(e, t) {
  const n = Le(e);
  return n ? t.get(n) ?? n : null;
}
function bs(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of Rd(e)) {
    const i = bs(n, t);
    if (i) return i;
  }
  return null;
}
function Pd(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = Ns(e);
  if (n) {
    const i = n.map((o) => Pd(o, t)).filter((o) => !!o);
    n.splice(0, n.length, ...i);
  }
  return e;
}
function Md(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of Rd(e)) Md(n, t);
  return t;
}
function Rd(e) {
  return Ns(e) ?? [];
}
function Ns(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function kc(e, t, n) {
  return [
    ...e.filter((i) => i.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function Pr(e, t) {
  const n = xt(e) ? e : {}, i = Number(n.x), o = Number(n.y);
  return {
    x: Number.isFinite(i) ? Math.max(40, Math.round(i)) : t.x,
    y: Number.isFinite(o) ? Math.max(40, Math.round(o)) : t.y
  };
}
function SN(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let i = n, o = 2;
  for (; t.has(i); )
    i = `${n}-${o}`, o += 1;
  return t.add(i), i;
}
function CN(e) {
  return typeof e == "number" ? mN[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Le(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function EN(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function xt(e) {
  return typeof e == "object" && e !== null;
}
function IN({
  draft: e,
  details: t,
  catalog: n,
  replaceDraftByBatch: i,
  setStatus: o,
  setError: s
}) {
  const a = ne(/* @__PURE__ */ new Map());
  G(() => {
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
        const p = Sn(e), h = xN(e, d.batch, n), y = `weaver-batch-${Date.now()}`;
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
    return window.addEventListener(Ec, c), window.addEventListener(Ic, u), () => {
      window.removeEventListener(Ec, c), window.removeEventListener(Ic, u);
    };
  }, [n, t, e, i, o, s]);
}
function kN({ context: e, draft: t, autosaveEnabledByDefault: n = !0, editDraft: i, setStatus: o, setError: s }) {
  const [a, c] = B(n), u = ne(""), l = ne(0), d = ne(Promise.resolve()), f = re((h) => {
    u.current = h ? Fe(h) : "";
  }, []), p = re(async (h, y) => {
    const w = async () => {
      const m = ++l.current, b = Fe(h);
      s("");
      try {
        const g = await Rp(e, h), v = Fe(g);
        return u.current = v, i(({ draft: j }) => !j || j.id !== g.id ? null : Fe(j) === b ? g : { ...j, validationErrors: g.validationErrors }), m === l.current && o(y), g;
      } catch (g) {
        throw m === l.current && (o(""), s(g instanceof Error ? g.message : String(g))), g;
      }
    }, x = d.current.then(w, w);
    return d.current = x.catch(() => {
    }), x;
  }, [e, i, o, s]);
  return G(() => {
    if (!a || !t || Fe(t) === u.current) return;
    o("Autosaving...");
    const y = window.setTimeout(() => {
      p(t, "Autosaved").catch(() => {
      });
    }, W0);
    return () => window.clearTimeout(y);
  }, [a, t, p, o]), { saveDraft: p, autosaveEnabled: a, setAutosaveEnabled: c, markSaved: f };
}
function AN({ context: e, definitionId: t, resetHistory: n, loadDraft: i, markSaved: o, setError: s }) {
  const [a, c] = B(null), [u, l] = B([]), [d, f] = B([]), [p, h] = B(null), [y, w] = B(Si), [x, m] = B("loading"), b = re(async () => {
    s(""), m("loading");
    const [g, v, j, N, S] = await Promise.all([
      Ip(e, t),
      Yr(e),
      Xp(e).then(
        (D) => ({ ok: !0, descriptors: D }),
        () => ({ ok: !1, descriptors: [] })
      ),
      Yp(e).then(
        (D) => ({ ok: !0, descriptors: D }),
        () => ({ ok: !1, descriptors: Si })
      ),
      // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
      gl(e).then(
        (D) => D,
        () => null
      )
    ]), I = g.draft ?? null;
    c(g), o(I), n(I), i(I), l(v.activities ?? []), f(j.descriptors), h(S), w(N.descriptors.length > 0 ? N.descriptors : Si), m(j.ok ? "ready" : "failed");
  }, [e, t, n, i, o, s]);
  return G(() => {
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
function _N({ context: e, details: t, setDetails: n, setStatus: i }) {
  const o = ne(null), s = ne(null), a = ne({});
  G(() => {
    o.current = t;
  }, [t]);
  const c = re(() => {
    s.current !== null && (window.clearTimeout(s.current), s.current = null);
    const l = a.current;
    a.current = {};
    const d = o.current?.definition;
    !d || l.name === void 0 && l.description === void 0 || Mp(e, d.id, {
      name: l.name ?? d.name,
      description: l.description ?? d.description ?? null
    }).then((f) => n((p) => p && p.definition.id === f.definition.id ? { ...p, definition: f.definition } : p)).catch(() => i("Couldn't save name/description."));
  }, [e, n, i]), u = re((l) => {
    n((d) => d && { ...d, definition: { ...d.definition, ...l } }), a.current = { ...a.current, ...l }, s.current !== null && window.clearTimeout(s.current), s.current = window.setTimeout(c, 800);
  }, [c, n]);
  return G(() => () => {
    c();
  }, [c]), { updateDefinitionMeta: u };
}
function DN({
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
  const y = re(() => {
    if (!t) return;
    const b = n?.definition.name;
    v0(m0(t, b), b), d("Exported workflow as JSON.");
  }, [t, n, d]), w = re(async () => {
    if (!(!t || i)) {
      l("saving"), d("Saving...");
      try {
        await o(t, "Saved");
      } catch {
      } finally {
        l("idle");
      }
    }
  }, [t, i, o, l, d]), x = re(async () => {
    if (!(!t || i)) {
      l("promoting"), d("Saving...");
      try {
        await o(t, "Saved"), d("Promoting...");
        const b = await zp(e, t.id), g = await Lp(e, b.versionId);
        u(g.artifactId), d(`Published ${g.artifactVersion}`), await s();
      } catch (b) {
        d(""), f(b instanceof Error ? b.message : String(b));
      } finally {
        l("idle");
      }
    }
  }, [t, i, e, o, s, u, l, d, f]), m = re(async () => {
    if (!t?.state.rootActivity || i) return;
    const b = t, g = Fe(b);
    c(), d("Preparing test run...");
    try {
      l("testRunPreparing"), d("Preparing test run...");
      const v = mb(b);
      l("testRunStarting"), d("Starting test run...");
      const j = await Vp(e, {
        definitionId: b.definitionId,
        snapshotId: v,
        state: b.state
      });
      a({ draftSignature: g, view: j }), p("runtime"), h(!1), d(ys(j) ? "Test run rejected" : "Test run dispatched");
    } catch (v) {
      d(""), f(v instanceof Error ? v.message : String(v));
    } finally {
      l("idle");
    }
  }, [t, i, e, c, a, p, h, l, d, f]);
  return { exportJson: y, save: w, promoteAndPublish: x, run: m };
}
function $N({
  context: e,
  draft: t,
  frames: n,
  selectedNodeId: i,
  catalog: o,
  activityDescriptors: s,
  availabilityDiagnostics: a
}) {
  const c = t?.state.rootActivity ?? null, u = de(() => new Map(o.map((S) => [S.activityVersionId, S])), [o]), l = re(
    (S) => lh([S.activityVersionId, S.activityTypeKey], a),
    [a]
  ), d = de(() => sb(s), [s]), f = de(() => Gc(c, n, u), [c, n, u]), p = il(f, f ? u.get(f.activityVersionId) : void 0), h = !!f && p === "unsupported", y = de(() => h ? null : $n(c, n, u), [c, n, u, h]), w = de(() => h && f?.nodeId === i ? f : y?.slot.activities.find((S) => S.nodeId === i) ?? null, [h, y, f, i]), x = de(
    () => w ? ab(w, u, d) : null,
    [u, d, w]
  ), m = de(
    () => w ? l({ activityVersionId: w.activityVersionId, activityTypeKey: u.get(w.activityVersionId)?.activityTypeKey }) : null,
    [l, u, w]
  ), b = w ? Me(w, u) : [], g = w ? mp(w, u.get(w.activityVersionId)) : !1, v = Ap(e, t?.state, i, u), j = !h && y?.slot.mode === "flowchart";
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
function TN({
  details: e,
  draft: t,
  selectedNode: n,
  selectedNodeId: i,
  selectedDescriptor: o,
  catalogByVersion: s
}) {
  G(() => {
    if (!(!e || !t))
      return window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
        workflowId: e.definition.id,
        workflowDefinitionId: e.definition.id,
        workflowVersionId: t.sourceVersionId ?? null,
        draftId: t.id,
        revision: hb(t),
        selectedNodeId: i,
        selectedActivityType: o?.typeName ?? (n ? s.get(n.activityVersionId)?.activityTypeKey ?? n.activityVersionId : null),
        summary: e.definition.name,
        activities: jd(t.state.rootActivity, s),
        connections: Sd(t.state.rootActivity, s),
        diagnostics: t.validationErrors.map((a) => ({ severity: a.code ?? "warning", message: a.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === e.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [s, e, t, o, n, i]);
}
function PN({
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
      /* @__PURE__ */ r.jsx(Gi, { size: 14, "aria-hidden": "true" }),
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
              f ? /* @__PURE__ */ r.jsx(Vc, { size: 14 }) : /* @__PURE__ */ r.jsx(yt, { size: 14 }),
              /* @__PURE__ */ r.jsx("span", { children: d.category }),
              /* @__PURE__ */ r.jsx("small", { children: d.activities.length })
            ]
          }
        ),
        f ? /* @__PURE__ */ r.jsx("div", { className: "wf-palette-activities", role: "group", children: d.activities.map((p) => {
          const h = p.description?.trim(), y = h ? `wf-palette-description-${p.activityVersionId}` : void 0, w = Ee(p), x = eo(p);
          return /* @__PURE__ */ r.jsxs(
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
                /* @__PURE__ */ r.jsx("span", { className: "wf-activity-icon", "data-icon": x, "aria-hidden": "true", children: hs(x) }),
                /* @__PURE__ */ r.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ r.jsx("strong", { children: w }),
                  h ? /* @__PURE__ */ r.jsx("small", { id: y, children: h }) : null
                ] }),
                /* @__PURE__ */ r.jsx(Oc, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            p.activityVersionId
          );
        }) }) : null
      ] }, d.category);
    }) })
  ] });
}
const zd = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]), MN = "Variable";
function RN({
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
  const d = BN(l), f = o.length > 0 ? o : tN;
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ r.jsx("span", { className: "wf-section-label", children: "Properties" }),
    d.map((p) => /* @__PURE__ */ r.jsxs("section", { className: "wf-property-group", children: [
      d.length > 1 ? /* @__PURE__ */ r.jsx("h4", { children: p.category }) : null,
      p.inputs.map((h) => /* @__PURE__ */ r.jsx(
        zN,
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
function zN({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: i,
  expressionDescriptors: o,
  visibleVariables: s,
  scopeStatus: a,
  onChange: c
}) {
  const u = t.isReadOnly === !0, l = { activity: e, expressionDescriptors: o, readOnly: u }, d = Lr(n, t, l), f = d?.component, p = t.isWrapped !== !1 ? Dd(e, t) : null, h = p?.expression.type ?? "Literal", y = rN(e, t), w = h.toLowerCase(), m = p && (w === "literal" || w === "object") && !lN(t) ? aN(t.typeName) : null, b = m ? Lr(n, t, { ...l, scope: "collection" }) : void 0, g = p ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: o,
    readOnly: u,
    surface: "inline",
    syntax: h
  } : null, v = g ? Vd(i, g) : null, j = v?.surfaces.inline, N = v && g ? Od(v, g, y) : [], S = m != null, I = !!(p && !S && KN(t, d?.id)), D = !!(p && !S && XN(t, d?.id)), [M, A] = B(!1), k = (E) => {
    const $ = p ? iN(p, E) : E;
    c(jc(e, t, $));
  }, R = (E) => {
    p && c(jc(e, t, oN(p, E)));
  }, C = m ? b ? Mr(b.component, t, y, u, { ...l, scope: "collection" }, k) : /* @__PURE__ */ r.jsx(
    VN,
    {
      input: t,
      elementTypeName: m.elementTypeName,
      value: y,
      editors: n,
      context: l,
      disabled: u,
      onChange: k
    }
  ) : null, _ = h === MN && p ? /* @__PURE__ */ r.jsx(
    WN,
    {
      value: y,
      visibleVariables: s,
      scopeStatus: a,
      disabled: u,
      onChange: k
    }
  ) : C ?? (j && g ? /* @__PURE__ */ r.jsx(
    j,
    {
      descriptor: t,
      syntax: h,
      value: y,
      disabled: u,
      context: g,
      onChange: k
    }
  ) : Mr(f, t, y, u, l, k));
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ r.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ r.jsx("span", { children: Td(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ r.jsx("p", { children: t.description }) : null,
    p && !I ? /* @__PURE__ */ r.jsx(
      Rr,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: h,
        descriptors: o,
        disabled: u,
        onChange: R
      }
    ) : null,
    I ? /* @__PURE__ */ r.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-expression-editor", children: [
        _,
        Vr(N)
      ] }),
      /* @__PURE__ */ r.jsx(
        Rr,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: h,
          descriptors: o,
          disabled: u,
          variant: "inline",
          onChange: R
        }
      ),
      D ? /* @__PURE__ */ r.jsx(
        "button",
        {
          type: "button",
          className: "wf-expression-expand-button",
          "aria-label": `Open expanded ${t.displayName || t.name} editor`,
          title: "Open expanded editor",
          onClick: () => A(!0),
          children: /* @__PURE__ */ r.jsx(_n, { size: 13 })
        }
      ) : null
    ] }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
      _,
      Vr(N)
    ] }),
    D && !I ? /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => A(!0),
        children: [
          /* @__PURE__ */ r.jsx(_n, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    M ? /* @__PURE__ */ r.jsx(
      ON,
      {
        input: t,
        value: y,
        syntax: h,
        descriptors: o,
        activity: e,
        expressionEditors: i,
        disabled: u,
        onChange: k,
        onSyntaxChange: R,
        onClose: () => A(!1)
      }
    ) : null
  ] });
}
function LN(e, t, n) {
  return [
    "wf-collection-item",
    t === e ? "dragging" : "",
    t !== null && t !== e && n === e ? "drop-target" : ""
  ].filter(Boolean).join(" ");
}
function VN({
  input: e,
  elementTypeName: t,
  value: n,
  editors: i,
  context: o,
  disabled: s,
  onChange: a
}) {
  const c = uN(n), u = fN(e, t), l = { ...o, scope: "element" }, d = Lr(i, u, l)?.component, f = e.displayName || e.name, p = (j, N) => a(c.map((S, I) => I === j ? N : S)), [h, y] = B(null), [w, x] = B(null), m = () => {
    y(null), x(null);
  }, b = (j) => (N) => {
    y(j), N.dataTransfer.effectAllowed = "move", N.dataTransfer.setData("text/plain", String(j));
  }, g = (j) => (N) => {
    h !== null && (N.preventDefault(), N.dataTransfer.dropEffect = "move", w !== j && x(j));
  }, v = (j) => (N) => {
    N.preventDefault(), h !== null && h !== j && a(hr(c, h, j)), m();
  };
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-collection-editor", children: [
    c.length === 0 ? /* @__PURE__ */ r.jsx("p", { className: "wf-collection-empty", children: "No items yet." }) : /* @__PURE__ */ r.jsx("ul", { className: "wf-collection-items", children: c.map((j, N) => /* @__PURE__ */ r.jsxs(
      "li",
      {
        className: LN(N, h, w),
        onDragOver: g(N),
        onDrop: v(N),
        children: [
          /* @__PURE__ */ r.jsx(
            "span",
            {
              className: "wf-collection-item-handle",
              draggable: !s,
              "aria-label": `Drag ${f} item ${N + 1} to reorder`,
              title: "Drag to reorder",
              onDragStart: b(N),
              onDragEnd: m,
              children: /* @__PURE__ */ r.jsx(Oc, { size: 13, "aria-hidden": "true" })
            }
          ),
          /* @__PURE__ */ r.jsx("div", { className: "wf-collection-item-editor", children: Mr(d, u, j, s, l, (S) => p(N, S)) }),
          /* @__PURE__ */ r.jsxs("div", { className: "wf-collection-item-actions", children: [
            /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${N + 1} up`,
                disabled: s || N === 0,
                onClick: () => a(hr(c, N, N - 1)),
                children: /* @__PURE__ */ r.jsx(tf, { size: 13 })
              }
            ),
            /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${N + 1} down`,
                disabled: s || N === c.length - 1,
                onClick: () => a(hr(c, N, N + 1)),
                children: /* @__PURE__ */ r.jsx(Vc, { size: 13 })
              }
            ),
            /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button danger",
                "aria-label": `Remove ${f} item ${N + 1}`,
                disabled: s,
                onClick: () => a(c.filter((S, I) => I !== N)),
                children: /* @__PURE__ */ r.jsx(An, { size: 13 })
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
        onClick: () => a([...c, dN(t)]),
        children: [
          /* @__PURE__ */ r.jsx(Gt, { size: 13 }),
          " Add item"
        ]
      }
    )
  ] });
}
function ON({
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
  const d = Pc(), f = e.displayName || e.name, p = {
    activity: o,
    descriptor: e,
    expressionDescriptors: i,
    readOnly: a,
    surface: "expanded",
    syntax: n
  }, h = Vd(s, p), y = h?.surfaces.expanded, w = h ? Od(h, p, t) : [], x = y ? null : FN(s, p);
  return G(() => {
    const m = (b) => {
      b.key === "Escape" && l();
    };
    return window.addEventListener("keydown", m), () => window.removeEventListener("keydown", m);
  }, [l]), /* @__PURE__ */ r.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ r.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": d, children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ r.jsx("h3", { id: d, children: f })
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": `Close ${f} editor`, onClick: l, children: /* @__PURE__ */ r.jsx(Lc, { size: 16 }) })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ r.jsx(
          Rr,
          {
            label: `${f} expression syntax`,
            value: n,
            descriptors: i,
            disabled: a,
            onChange: u
          }
        ),
        /* @__PURE__ */ r.jsx("span", { children: Td(e.typeName) })
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
        x ? /* @__PURE__ */ r.jsx("p", { className: "wf-expression-editor-hint", children: x }) : null,
        /* @__PURE__ */ r.jsx(
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
      Vr(w)
    ] }),
    /* @__PURE__ */ r.jsxs("footer", { children: [
      /* @__PURE__ */ r.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: l, children: "Close" })
    ] })
  ] }) });
}
function Mr(e, t, n, i, o, s) {
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
function Rr({
  label: e,
  value: t,
  descriptors: n,
  disabled: i,
  variant: o = "block",
  onChange: s
}) {
  const [a, c] = B(!1), u = Pc(), l = n.find((f) => f.type === t), d = [
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
const zr = "::";
function Ld(e) {
  return !e || e === zi ? zi : e;
}
function Ac(e, t) {
  return `${Ld(t)}${zr}${e}`;
}
function HN(e) {
  const t = e.indexOf(zr);
  if (t < 0) return null;
  const n = e.slice(t + zr.length);
  return n ? { scopeId: e.slice(0, t), referenceKey: n } : null;
}
function WN({ value: e, visibleVariables: t, scopeStatus: n, disabled: i, onChange: o }) {
  const s = fl(e), a = !!e && typeof e == "object" || typeof e == "string" && e.trim().startsWith("{"), c = s && (a || t.some((d) => d.referenceKey === s.referenceKey)) ? s : null, u = c ? Ac(c.referenceKey, c.declaringScopeId) : "", l = !!c && t.some(
    (d) => d.referenceKey === c.referenceKey && d.scopeId === Ld(c.declaringScopeId)
  );
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-variable-picker", children: [
    /* @__PURE__ */ r.jsxs(
      "select",
      {
        "aria-label": "Variable reference",
        value: u,
        disabled: i,
        onChange: (d) => {
          const f = HN(d.target.value);
          f && o(wp(f.referenceKey, f.scopeId));
        },
        children: [
          /* @__PURE__ */ r.jsx("option", { value: "", children: "Select a variable…" }),
          c && !l ? /* @__PURE__ */ r.jsxs("option", { value: u, children: [
            c.referenceKey,
            " (not visible from this scope)"
          ] }) : null,
          t.map((d) => {
            const f = Ac(d.referenceKey, d.scopeId);
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
function Lr(e, t, n) {
  return [...e].sort((i, o) => (i.order ?? 500) - (o.order ?? 500)).find((i) => i.supports(t, n));
}
function Vd(e, t) {
  return [...e].sort((n, i) => (n.order ?? 500) - (i.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function Od(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function FN(e, t) {
  if (t.syntax.toLowerCase() === "literal") return null;
  const n = [...e].sort((a, c) => (a.order ?? 500) - (c.order ?? 500)).find((a) => a.supports(t) && a.metadata)?.metadata;
  if (!n) return `No enhanced editor is registered for ${t.syntax}. Using the generic text editor.`;
  const i = n.displayName?.trim() || "enhanced editor", o = n.installHint?.trim(), s = `No ${i} is registered for ${t.syntax}. Using the generic text editor.`;
  return o ? `${s} ${o}` : s;
}
function Vr(e) {
  return e.length === 0 ? null : /* @__PURE__ */ r.jsx("div", { className: "wf-expression-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const i = t.severity ?? "info";
    return /* @__PURE__ */ r.jsxs("p", { className: `wf-expression-editor-diagnostic ${i}`, children: [
      t.code ? /* @__PURE__ */ r.jsx("span", { children: t.code }) : null,
      t.message
    ] }, `${t.code ?? "diagnostic"}-${n}`);
  }) });
}
function BN(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const i = n.category?.trim() || "General";
    t.set(i, [...t.get(i) ?? [], n]);
  }
  return [...t.entries()].map(([n, i]) => ({ category: n, inputs: i }));
}
function KN(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !zd.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function XN(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !zd.has(t) && n !== "multiline") return !1;
  const i = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(i) || n === "singleline" || n === "multiline";
}
function YN({
  context: e,
  selectedNode: t,
  selectedNodeLabel: n,
  selectedActivityType: i,
  selectedDescriptor: o,
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
  return t ? /* @__PURE__ */ r.jsxs("div", { className: "wf-inspector-content", children: [
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
      /* @__PURE__ */ r.jsx(Ti, { size: 14 }),
      /* @__PURE__ */ r.jsxs("span", { children: [
        "No longer available for new use · ",
        Li(s.state)
      ] })
    ] }) : null,
    /* @__PURE__ */ r.jsx(
      RN,
      {
        activity: t,
        descriptor: o,
        editors: l,
        expressionEditors: d,
        expressionDescriptors: f,
        descriptorStatus: p,
        visibleVariables: h.visibleVariables,
        scopeStatus: h.status,
        onChange: y
      }
    ),
    u ? /* @__PURE__ */ r.jsx("div", { className: "wf-container-variables", children: /* @__PURE__ */ r.jsx(
      L0,
      {
        context: e,
        variables: dl(t),
        title: "Container variables",
        addLabel: "Add container variable",
        emptyLabel: "No container variables declared on this activity.",
        warnings: bp(h.shadowingWarnings, t.nodeId),
        onChange: (x) => y(xp(t, x))
      }
    ) }) : null,
    a.length > 0 ? /* @__PURE__ */ r.jsxs("div", { className: "wf-slot-list", children: [
      /* @__PURE__ */ r.jsx("span", { children: "Embedded slots" }),
      a.map((x) => /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => w(t, x.id, `${n} / ${x.label}`), children: [
        x.label,
        /* @__PURE__ */ r.jsx("small", { children: yb(x, c) })
      ] }, x.id))
    ] }) : /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
  ] }) : /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
}
const Hd = [
  { value: "flowchart", label: "Flowchart", hint: "Free-form graph of connected activities." },
  { value: "sequence", label: "Sequence", hint: "Ordered list of activities that run top to bottom." }
];
function Wd({ label: e, hint: t }) {
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsx("span", { className: "wf-root-card-title", children: e }),
    /* @__PURE__ */ r.jsx("span", { className: "wf-root-card-hint", children: t })
  ] });
}
function qN({ value: e, onChange: t }) {
  return /* @__PURE__ */ r.jsx("div", { className: "wf-root-cards", role: "radiogroup", "aria-label": "Root activity", children: Hd.map((n) => {
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
      /* @__PURE__ */ r.jsx(Wd, { label: n.label, hint: n.hint })
    ] }, n.value);
  }) });
}
function UN({ onPick: e }) {
  return /* @__PURE__ */ r.jsx("div", { className: "wf-root-cards", role: "group", "aria-label": "Fill this slot", children: Hd.map((t) => /* @__PURE__ */ r.jsx(
    "button",
    {
      type: "button",
      className: "wf-root-card",
      "aria-label": t.label,
      onClick: () => e(t.value),
      children: /* @__PURE__ */ r.jsx(Wd, { label: t.label, hint: t.hint })
    },
    t.value
  )) });
}
function ZN({ slotLabel: e, catalog: t, onPickActivity: n, onBrowseAll: i }) {
  const o = (s) => {
    const a = md(t, s);
    a && n(a);
  };
  return /* @__PURE__ */ r.jsx("div", { className: "wf-slot-empty", role: "group", "aria-label": `Fill ${e}`, children: /* @__PURE__ */ r.jsxs("div", { className: "wf-slot-empty-card", children: [
    /* @__PURE__ */ r.jsx("p", { className: "wf-slot-empty-title", children: "This slot is empty" }),
    /* @__PURE__ */ r.jsxs("p", { className: "wf-slot-empty-hint", children: [
      "Choose a container for ",
      /* @__PURE__ */ r.jsx("strong", { children: e }),
      ", or pick any activity."
    ] }),
    /* @__PURE__ */ r.jsx(UN, { onPick: o }),
    /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-slot-empty-browse",
        onClick: (s) => i({ clientX: s.clientX, clientY: s.clientY }),
        children: [
          /* @__PURE__ */ r.jsx(Fn, { size: 15 }),
          " Browse all activities…"
        ]
      }
    ),
    /* @__PURE__ */ r.jsx("p", { className: "wf-slot-empty-drag-hint", children: "Tip: you can also drag activities from the palette onto the canvas." })
  ] }) });
}
function GN({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: i,
  expressionEditors: o,
  workflowDesignerPanels: s,
  autosaveEnabledByDefault: a,
  onBack: c
}) {
  const u = Bb(), { draft: l, frames: d, selectedNodeId: f, testRun: p, publishedArtifactId: h } = u.state, {
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
    setPublishedArtifact: I
  } = u, [D, M] = B(""), [A, k] = B(""), [R, C] = B("idle"), [_, E] = B(() => /* @__PURE__ */ new Set()), [$, P] = B(""), [T, F] = B("activities"), [H, O] = B("inspector"), [X, q] = B("designer"), {
    paletteWidth: ee,
    inspectorWidth: le,
    paletteCollapsed: U,
    inspectorCollapsed: z,
    maximizedSidePanel: Y,
    setInspectorCollapsed: ae,
    paletteExpanded: ce,
    inspectorExpanded: J,
    editorBodyClassName: oe,
    editorBodyStyle: fe,
    toggleSidePanelCollapsed: V,
    toggleSidePanelMaximized: Q,
    startSidePanelResize: ge,
    handleSidePanelResizeKeyDown: me
  } = Ad(), { resetHistory: Re, undo: _e, redo: $e, canUndoNow: rt, canRedoNow: Xe } = Hb({ draft: l, restoreDraft: y }), { saveDraft: Ye, autosaveEnabled: Te, setAutosaveEnabled: Oe, markSaved: He } = kN({ context: e, draft: l, autosaveEnabledByDefault: a, editDraft: x, setStatus: k, setError: M }), {
    details: Ne,
    setDetails: Rt,
    catalog: Ae,
    activityDescriptors: dn,
    availabilityDiagnostics: L,
    expressionDescriptors: W,
    descriptorStatus: K,
    reload: te
  } = AN({ context: e, definitionId: t, resetHistory: Re, loadDraft: y, markSaved: He, setError: M }), { updateDefinitionMeta: se } = _N({ context: e, details: Ne, setDetails: Rt, setStatus: k }), {
    catalogByVersion: he,
    availabilityLookup: ye,
    scopeOwner: ke,
    isUnsupportedDesigner: Ce,
    scope: ze,
    selectedNode: xe,
    selectedDescriptor: zt,
    selectedNodeAvailability: yo,
    selectedSlots: ei,
    selectedSupportsScopedVariables: mo,
    scopedVariableAnalysis: xo,
    isFlowchartDesigner: ft,
    canAddActivitiesToCanvas: wo
  } = $N({ context: e, draft: l, frames: d, selectedNodeId: f, catalog: Ae, activityDescriptors: dn, availabilityDiagnostics: L }), Lt = de(() => Tr(Ae), [Ae]), vo = de(() => {
    const Z = $.trim().toLowerCase();
    if (!Z) return Lt;
    const ue = Ae.filter((be) => Ee(be).toLowerCase().includes(Z) || be.activityTypeKey.toLowerCase().includes(Z) || (be.category ?? "").toLowerCase().includes(Z) || (be.description ?? "").toLowerCase().includes(Z));
    return Tr(ue);
  }, [Ae, $, Lt]), Vt = R !== "idle", bo = !!l?.state.rootActivity && !Vt, ti = Tt(n, "weaver.workflows.find-draft-risks"), ni = Tt(n, "weaver.workflows.propose-update"), No = eN({
    draft: l,
    scope: ze,
    scopeOwner: ke,
    frames: d,
    catalog: Ae,
    catalogByVersion: he,
    isUnsupportedDesigner: Ce,
    isFlowchartDesigner: ft,
    canAddActivitiesToCanvas: wo,
    selectedNodeId: f,
    editDraft: x,
    editDraftAndSelect: m,
    select: b,
    resetToRoot: v,
    setStatus: k,
    setError: M
  }), {
    nodes: Ot,
    edges: jo,
    canvasRef: So,
    setReactFlowInstance: Co,
    connectMenu: Ht,
    setConnectMenu: ii,
    edgeActions: oi,
    onNodesChange: Eo,
    onEdgesChange: Io,
    onNodesDelete: ko,
    onEdgesDelete: Ao,
    isValidConnection: _o,
    onConnect: Do,
    onConnectStart: ri,
    onConnectEnd: $o,
    onReconnect: To,
    commitLayout: Po,
    canAutoLayout: Mo,
    autoLayout: Ro,
    onCanvasDragOver: si,
    onCanvasDragLeave: ai,
    onCanvasDrop: ci,
    openEmptyConnectMenu: fn,
    onConnectMenuPick: zo,
    addActivity: Lo,
    onPaletteClick: Vo,
    onPaletteDragStart: Oo,
    onPaletteDragEnd: Ho,
    onPalettePointerDown: Wo
  } = No, Fo = !Ce && d.length > 0 && !!ze && ze.slot.activities.length === 0 && Ot.length === 0;
  IN({ draft: l, details: Ne, catalog: Ae, replaceDraftByBatch: w, setStatus: k, setError: M }), G(() => {
    !l?.state.rootActivity || Ae.length === 0 || x(({ draft: Z }) => {
      if (!Z?.state.rootActivity) return null;
      const ue = rl(Z.state.rootActivity, he);
      return !ue || ue === Z.state.rootActivity ? null : {
        ...Z,
        state: {
          ...Z.state,
          rootActivity: ue
        }
      };
    });
  }, [Ae.length, he, l?.state.rootActivity, x]), TN({ details: Ne, draft: l, selectedNode: xe, selectedNodeId: f, selectedDescriptor: zt, catalogByVersion: he }), G(() => {
    E((Z) => {
      let ue = !1;
      const be = new Set(Z);
      for (const st of Lt)
        be.has(st.category) || (be.add(st.category), ue = !0);
      return ue ? be : Z;
    });
  }, [Lt]);
  const { exportJson: Bo, save: li, promoteAndPublish: Ko, run: ui } = DN({
    context: e,
    draft: l,
    details: Ne,
    busy: Vt,
    saveDraft: Ye,
    reload: te,
    startTestRun: N,
    clearTestRun: S,
    setPublishedArtifact: I,
    setOperation: C,
    setStatus: k,
    setError: M,
    setActiveRightPanelId: O,
    setInspectorCollapsed: ae
  }), Xo = re((Z) => {
    x(({ draft: ue }) => ue ? { ...ue, state: Z(ue.state) } : null);
  }, [x]), Yo = re((Z) => {
    if (!l) return "No draft is loaded.";
    const ue = w0(Z, l);
    return ue.ok ? (y(ue.draft), k("Applied workflow JSON."), null) : ue.error;
  }, [l, y]);
  G(() => {
    const Z = (ue) => {
      if (X !== "designer" || !(ue.metaKey || ue.ctrlKey)) return;
      const be = ue.target;
      if (be && (be.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(be.tagName))) return;
      const st = ue.key.toLowerCase();
      st === "z" && !ue.shiftKey ? (ue.preventDefault(), _e()) : (st === "z" && ue.shiftKey || st === "y") && (ue.preventDefault(), $e());
    };
    return window.addEventListener("keydown", Z), () => window.removeEventListener("keydown", Z);
  }, [X, _e, $e]);
  const pn = re((Z) => {
    x(({ draft: ue }) => {
      const be = ue?.state.rootActivity;
      return !ue || !be ? null : {
        ...ue,
        state: {
          ...ue.state,
          rootActivity: ol(be, Z.nodeId, () => Z, he)
        }
      };
    });
  }, [he, x]), qo = re((Z) => {
    if (!Z) return;
    const ue = l?.state.rootActivity;
    if (!ue) return;
    const be = Hf(ue, Z, (st) => {
      const Is = he.get(st.activityVersionId);
      return Is ? Ee(Is) : st.nodeId;
    }, he);
    be && (q("designer"), g(be, Z), ae(!1));
  }, [l?.state.rootActivity, he, ae, g]), Uo = (Z) => {
    E((ue) => {
      const be = new Set(ue);
      return be.has(Z) ? be.delete(Z) : be.add(Z), be;
    });
  };
  if (!Ne || !l)
    return /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: D || "Loading workflow editor..." });
  const vt = p?.draftSignature === Fe(l) ? p.view : null, js = vt && A.startsWith("Test run") ? "" : A, Fd = (Z) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(Z)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, Bd = {
    definition: Ne.definition,
    draft: l,
    selectedActivity: xe,
    selectedActivityDescriptor: zt,
    selectedActivitySlots: ei,
    catalog: Ae,
    currentScopeOwner: ke,
    frames: d
  }, Ss = s.map((Z) => {
    const ue = Z.component;
    return {
      id: Z.id,
      title: Z.title,
      side: Z.side,
      order: Z.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ r.jsx(ue, { context: Bd })
    };
  }), Zo = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ r.jsx(Fn, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(
        PN,
        {
          paletteSearch: $,
          onSearchChange: P,
          groups: vo,
          expandedCategories: _,
          onToggleCategory: Uo,
          onActivityClick: Vo,
          onActivityDragStart: Oo,
          onActivityDragEnd: Ho,
          onActivityPointerDown: Wo
        }
      )
    },
    ...Ss.filter((Z) => Z.side === "left")
  ].sort(wc), Go = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ r.jsx(Wr, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(
        YN,
        {
          context: e,
          selectedNode: xe,
          selectedNodeLabel: xe ? Ot.find((Z) => Z.id === xe.nodeId)?.data.label ?? xe.nodeId : "",
          selectedActivityType: xe ? zt?.typeName ?? he.get(xe.activityVersionId)?.activityTypeKey ?? "Unknown" : "",
          selectedDescriptor: zt,
          selectedNodeAvailability: yo,
          selectedSlots: ei,
          catalogByVersion: he,
          selectedSupportsScopedVariables: mo,
          propertyEditors: i,
          expressionEditors: o,
          expressionDescriptors: W,
          descriptorStatus: K,
          scopedVariableAnalysis: xo,
          onSelectedActivityChange: pn,
          onEnterSlot: j
        }
      )
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: /* @__PURE__ */ r.jsx(Zt, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx($b, { testRun: vt, onOpenRun: Fd })
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ r.jsx(Hc, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(
        Mb,
        {
          context: e,
          ai: n,
          definitionId: Ne.definition.id,
          publishedArtifactId: h
        }
      )
    },
    ...Ss.filter((Z) => Z.side === "right")
  ].sort(wc), Cs = Zo.find((Z) => Z.id === T) ?? Zo[0], Es = Go.find((Z) => Z.id === H) ?? Go[0], Kd = [
    { id: "designer", title: "Designer", order: 0, icon: /* @__PURE__ */ r.jsx(Wc, { size: 14 }), render: () => null },
    { id: "code", title: "Code", order: 1, icon: /* @__PURE__ */ r.jsx(af, { size: 14 }), render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: /* @__PURE__ */ r.jsx(Hr, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-link-button", onClick: c, children: "Definitions" }),
      /* @__PURE__ */ r.jsx(yt, { size: 14 }),
      /* @__PURE__ */ r.jsx("strong", { children: Ne.definition.name }),
      /* @__PURE__ */ r.jsx("span", { className: "wf-chip", children: "Draft" }),
      js ? /* @__PURE__ */ r.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ r.jsx(an, { size: 13 }),
        " ",
        js
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
              disabled: !rt,
              onClick: _e,
              children: /* @__PURE__ */ r.jsx(nf, { size: 16 })
            }
          ),
          /* @__PURE__ */ r.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Redo",
              title: "Redo (Ctrl+Shift+Z)",
              disabled: !Xe,
              onClick: $e,
              children: /* @__PURE__ */ r.jsx(of, { size: 16 })
            }
          ),
          /* @__PURE__ */ r.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Auto-layout",
              title: "Auto-layout the canvas",
              disabled: !Mo,
              onClick: Ro,
              children: /* @__PURE__ */ r.jsx(rf, { size: 16 })
            }
          )
        ] }),
        /* @__PURE__ */ r.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ r.jsx("input", { className: "wf-autosave-switch-input", type: "checkbox", checked: Te, onChange: (Z) => Oe(Z.target.checked) }),
          /* @__PURE__ */ r.jsx("span", { children: "Autosave" })
        ] }),
        ti ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => Pt(n, ti, { definition: Ne.definition, draft: l }), children: [
          /* @__PURE__ */ r.jsx(lt, { size: 15 }),
          " Risks"
        ] }) : null,
        ni ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => Pt(n, ni, { definition: Ne.definition, draft: l }), children: [
          /* @__PURE__ */ r.jsx(lt, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ r.jsxs("button", { type: "button", title: "Export workflow as JSON", onClick: Bo, children: [
          /* @__PURE__ */ r.jsx(sf, { size: 15 }),
          " Export"
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: Vt, onClick: () => {
          li();
        }, children: [
          /* @__PURE__ */ r.jsx(Mc, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: Vt, onClick: () => {
          Ko();
        }, children: [
          /* @__PURE__ */ r.jsx(zc, { size: 15 }),
          " Promote"
        ] }),
        vt ? /* @__PURE__ */ r.jsx(
          Db,
          {
            testRun: vt,
            onOpenDetails: () => {
              O("runtime"), ae(!1);
            }
          }
        ) : null,
        /* @__PURE__ */ r.jsxs(
          "button",
          {
            type: "button",
            disabled: !bo,
            title: l.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              ui();
            },
            children: [
              /* @__PURE__ */ r.jsx(Zt, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    D ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ r.jsx(wt, { size: 16 }),
      " ",
      D
    ] }) : null,
    /* @__PURE__ */ r.jsxs("div", { className: oe, style: fe, children: [
      /* @__PURE__ */ r.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ r.jsx(
            $i,
            {
              label: "Activities panel tabs",
              tabs: Zo,
              activeTabId: Cs.id,
              onSelect: F
            }
          ),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": U ? "Expand activities panel" : "Collapse activities panel",
                title: U ? "Expand" : "Collapse",
                onClick: () => V("palette"),
                children: U ? /* @__PURE__ */ r.jsx(yt, { size: 14 }) : /* @__PURE__ */ r.jsx(Dn, { size: 14 })
              }
            ),
            U ? null : /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": Y === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: Y === "palette" ? "Restore" : "Maximize",
                onClick: () => Q("palette"),
                children: Y === "palette" ? /* @__PURE__ */ r.jsx(yr, { size: 14 }) : /* @__PURE__ */ r.jsx(_n, { size: 14 })
              }
            )
          ] })
        ] }),
        ce ? Cs.render() : null
      ] }),
      ce && !Y ? /* @__PURE__ */ r.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": bn,
          "aria-valuemax": Nn,
          "aria-valuenow": ee,
          tabIndex: 0,
          onPointerDown: (Z) => ge("palette", Z),
          onKeyDown: (Z) => me("palette", Z)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ r.jsx("div", { className: "wf-canvas-tabs", children: /* @__PURE__ */ r.jsx(
          $i,
          {
            label: "Editor view tabs",
            tabs: Kd,
            activeTabId: X,
            onSelect: (Z) => q(Z)
          }
        ) }),
        X === "code" ? /* @__PURE__ */ r.jsx(C0, { draft: l, onApply: Yo }) : X === "properties" ? /* @__PURE__ */ r.jsx(O0, { details: Ne, draft: l, context: e, onStateChange: Xo, onDefinitionMetaChange: se }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
          /* @__PURE__ */ r.jsxs("div", { className: "wf-breadcrumb", children: [
            /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => v(), children: "Root" }),
            d.map((Z, ue) => /* @__PURE__ */ r.jsxs(tt.Fragment, { children: [
              /* @__PURE__ */ r.jsx(yt, { size: 13 }),
              /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => g(d.slice(0, ue + 1), null), children: Z.label })
            ] }, `${Z.ownerNodeId}-${Z.slotId}-${ue}`))
          ] }),
          /* @__PURE__ */ r.jsxs("div", { className: "wf-canvas", ref: So, onDragOver: si, onDragLeave: ai, onDrop: ci, children: [
            /* @__PURE__ */ r.jsx(hd.Provider, { value: oi, children: /* @__PURE__ */ r.jsx(gd.Provider, { value: ye, children: /* @__PURE__ */ r.jsxs(
              od,
              {
                nodes: Ot,
                edges: jo,
                nodeTypes: Ed,
                edgeTypes: Id,
                onInit: Co,
                onNodesChange: Eo,
                onEdgesChange: Io,
                onNodesDelete: ko,
                onEdgesDelete: Ao,
                onConnect: Do,
                onConnectStart: ft ? ri : void 0,
                onConnectEnd: ft ? $o : void 0,
                onReconnect: ft ? To : void 0,
                isValidConnection: _o,
                onDragOver: si,
                onDragLeave: ai,
                onDrop: ci,
                onPaneClick: () => b(null),
                onNodeClick: (Z, ue) => b(ue.id),
                onNodeDragStop: Ce ? void 0 : Po,
                minZoom: 0.2,
                maxZoom: 1.8,
                nodesConnectable: ft,
                nodesDraggable: !Ce,
                selectionOnDrag: !0,
                multiSelectionKeyCode: ["Shift", "Meta", "Control"],
                deleteKeyCode: Ce ? null : ["Backspace", "Delete"],
                panActivationKeyCode: null,
                defaultEdgeOptions: { type: "workflow" },
                children: [
                  /* @__PURE__ */ r.jsx(sd, { gap: 18, size: 1 }),
                  /* @__PURE__ */ r.jsx(cd, {}),
                  /* @__PURE__ */ r.jsx(ud, { pannable: !0, zoomable: !0 })
                ]
              }
            ) }) }),
            Fo ? /* @__PURE__ */ r.jsx(
              ZN,
              {
                slotLabel: ze?.slot.label ?? "this slot",
                catalog: Ae,
                onPickActivity: Lo,
                onBrowseAll: fn
              }
            ) : ft && Ot.length === 0 ? /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => fn(), children: [
              /* @__PURE__ */ r.jsx(Gt, { size: 15 }),
              " Add activity"
            ] }) : null,
            Ht ? /* @__PURE__ */ r.jsx(
              Sb,
              {
                clientX: Ht.clientX,
                clientY: Ht.clientY,
                activities: Ae,
                onPick: zo,
                onClose: () => ii(null)
              }
            ) : null
          ] }),
          /* @__PURE__ */ r.jsx(_b, { draft: l, onRepair: qo })
        ] })
      ] }),
      J && !Y ? /* @__PURE__ */ r.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Xt,
          "aria-valuemax": Yt,
          "aria-valuenow": le,
          tabIndex: 0,
          onPointerDown: (Z) => ge("inspector", Z),
          onKeyDown: (Z) => me("inspector", Z)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ r.jsx(
            $i,
            {
              label: "Inspector panel tabs",
              tabs: Go,
              activeTabId: Es.id,
              onSelect: O
            }
          ),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": z ? "Expand inspector panel" : "Collapse inspector panel",
                title: z ? "Expand" : "Collapse",
                onClick: () => V("inspector"),
                children: z ? /* @__PURE__ */ r.jsx(Dn, { size: 14 }) : /* @__PURE__ */ r.jsx(yt, { size: 14 })
              }
            ),
            z ? null : /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": Y === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: Y === "inspector" ? "Restore" : "Maximize",
                onClick: () => Q("inspector"),
                children: Y === "inspector" ? /* @__PURE__ */ r.jsx(yr, { size: 14 }) : /* @__PURE__ */ r.jsx(_n, { size: 14 })
              }
            )
          ] })
        ] }),
        J ? Es.render() : null
      ] })
    ] })
  ] });
}
function JN({ page: e, pageSize: t, totalCount: n, onPageChange: i, onPageSizeChange: o }) {
  const s = yd(n, t), a = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
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
      /* @__PURE__ */ r.jsx("select", { value: t, onChange: (u) => o(Number(u.target.value)), children: B0.map((u) => /* @__PURE__ */ r.jsx("option", { value: u, children: u }, u)) })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => i(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ r.jsx(Dn, { size: 14 }),
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
        /* @__PURE__ */ r.jsx(yt, { size: 14 })
      ] })
    ] })
  ] });
}
function QN({ draft: e, creating: t, ai: n, suggestMetadataAction: i, onChange: o, onClose: s, onSubmit: a }) {
  const [c, u] = B(!1), [l, d] = B(""), [f, p] = B(!1), [h, y] = B(null), [w, x] = B(null), m = ne(null), b = ne(e);
  b.current = e;
  const g = ne(o);
  g.current = o;
  const v = re((N) => {
    const S = { ...b.current };
    N.name && (S.name = N.name), N.description && (S.description = N.description), g.current(S), y(null), x(null);
  }, []);
  G(() => {
    if (i)
      return n.onPromptResult((N) => {
        if (N.requestId !== m.current) return;
        if (m.current = null, p(!1), N.status !== "completed") {
          x(N.status === "cancelled" ? "Weaver needs more detail — continue in the assistant panel." : "Weaver couldn't generate a suggestion. Try again or fill the fields manually.");
          return;
        }
        const S = U0(N.text);
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
                /* @__PURE__ */ r.jsx(lt, { size: 13 }),
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
            /* @__PURE__ */ r.jsx(lt, { size: 13 }),
            " ",
            f ? "Generating…" : "Generate"
          ] }) }),
          w ? /* @__PURE__ */ r.jsx("p", { className: "wf-ai-suggest-error", role: "alert", children: w }) : null,
          h ? /* @__PURE__ */ r.jsxs("div", { className: "wf-ai-suggest-preview", children: [
            h.name ? /* @__PURE__ */ r.jsx("p", { children: /* @__PURE__ */ r.jsx("strong", { children: h.name }) }) : null,
            h.description ? /* @__PURE__ */ r.jsx("p", { children: h.description }) : null,
            /* @__PURE__ */ r.jsxs("div", { className: "wf-ai-suggest-actions", children: [
              /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => v(h), children: "Apply" }),
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
            qN,
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
function ej({ context: e, ai: t, onOpen: n }) {
  const [i, o] = B(""), [s, a] = B("active"), [c, u] = B(1), [l, d] = B(K0), [f, p] = B("loading"), [h, y] = B(""), [w, x] = B(""), [m, b] = B([]), [g, v] = B(0), [j, N] = B(() => /* @__PURE__ */ new Set()), [S, I] = B(null), [D, M] = B(!1), [A, k] = B([]), [R, C] = B("idle"), _ = ne(null), E = de(() => m.map((V) => V.id), [m]), $ = Tt(t, "weaver.workflows.suggest-create-metadata"), P = Tt(t, "weaver.workflows.explain-definition"), T = E.filter((V) => j.has(V)).length, F = E.length > 0 && T === E.length, H = re(async () => {
    p("loading"), y("");
    try {
      const V = await Ep(e, { search: i, state: s, page: c, pageSize: l }), Q = typeof V.totalCount == "number", ge = V.totalCount ?? V.definitions.length, me = yd(ge, l);
      if (ge > 0 && c > me) {
        u(me);
        return;
      }
      b(Q ? V.definitions : q0(V.definitions, c, l)), v(ge), p("ready");
    } catch (V) {
      y(V instanceof Error ? V.message : String(V)), p("failed");
    }
  }, [e, i, s, c, l]);
  G(() => {
    H();
  }, [H]), G(() => {
    _.current && (_.current.indeterminate = T > 0 && !F);
  }, [F, T]);
  const O = re(async () => {
    if (!(R === "loading" || R === "ready")) {
      C("loading");
      try {
        const V = await Yr(e);
        k(V.activities ?? []), C("ready");
      } catch (V) {
        C("failed"), y(V instanceof Error ? V.message : String(V));
      }
    }
  }, [R, e]), X = () => {
    y(""), x(""), I({ name: "", description: "", rootKind: "flowchart" }), O();
  }, q = async () => {
    if (S?.name.trim()) {
      M(!0), y(""), x("");
      try {
        const V = await Dp(e, {
          name: S.name.trim(),
          description: S.description.trim() || null,
          rootKind: S.rootKind,
          rootActivityVersionId: Z0(S, A)
        });
        I(null), n(V.definition.id);
      } catch (V) {
        y(V instanceof Error ? V.message : String(V));
      } finally {
        M(!1);
      }
    }
  }, ee = (V) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(V)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, le = async () => {
    if (m.length === 1 && c > 1) {
      u(c - 1);
      return;
    }
    await H();
  }, U = () => N(/* @__PURE__ */ new Set()), z = (V, Q) => {
    N((ge) => {
      const me = new Set(ge);
      return Q ? me.add(V) : me.delete(V), me;
    });
  }, Y = (V) => {
    N((Q) => {
      const ge = new Set(Q);
      for (const me of E)
        V ? ge.add(me) : ge.delete(me);
      return ge;
    });
  }, ae = (V) => {
    a(V), u(1), U();
  }, ce = (V) => {
    o(V), u(1), U();
  }, J = async (V) => {
    if (await $s().confirm({ message: `Delete workflow definition "${V.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" })) {
      x(""), y("");
      try {
        await $p(e, V.id), z(V.id, !1), x(`Deleted ${V.name}`), await le();
      } catch (Q) {
        y(Q instanceof Error ? Q.message : String(Q));
      }
    }
  }, oe = async (V) => {
    x(""), y("");
    try {
      await Tp(e, V.id), z(V.id, !1), x(`Restored ${V.name}`), await le();
    } catch (Q) {
      y(Q instanceof Error ? Q.message : String(Q));
    }
  }, fe = async (V) => {
    if (await $s().confirm({ message: `Permanently delete workflow definition "${V.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) {
      x(""), y("");
      try {
        await Pp(e, V.id), z(V.id, !1), x(`Permanently deleted ${V.name}`), await le();
      } catch (Q) {
        y(Q instanceof Error ? Q.message : String(Q));
      }
    }
  };
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ r.jsx("button", { type: "button", className: s === "active" ? "active" : "", "aria-selected": s === "active", onClick: () => ae("active"), children: "Active" }),
        /* @__PURE__ */ r.jsx("button", { type: "button", className: s === "deleted" ? "active" : "", "aria-selected": s === "deleted", onClick: () => ae("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ r.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ r.jsx(Gi, { size: 15 }),
        /* @__PURE__ */ r.jsx("input", { value: i, onChange: (V) => ce(V.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => {
        H();
      }, children: "Refresh" }),
      /* @__PURE__ */ r.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ r.jsxs("button", { type: "button", title: "Create workflow", onClick: X, children: [
        /* @__PURE__ */ r.jsx(Gt, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ r.jsx(Qn, { message: h, title: "Couldn't load workflow definitions" }) : null,
    f !== "failed" && h ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ r.jsx(wt, { size: 16 }),
      " ",
      h
    ] }) : null,
    w ? /* @__PURE__ */ r.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ r.jsx(an, { size: 14 }),
      " ",
      w
    ] }) : null,
    j.size > 0 ? /* @__PURE__ */ r.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ r.jsxs("span", { children: [
        j.size,
        " selected"
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: U, children: "Clear selection" })
    ] }) : null,
    f === "loading" ? /* @__PURE__ */ r.jsx(ms, {}) : null,
    f === "ready" && m.length === 0 ? /* @__PURE__ */ r.jsx(
      xs,
      {
        icon: /* @__PURE__ */ r.jsx(Hc, { size: 22 }),
        title: `No ${s} workflow definitions`,
        description: "Create a workflow to start designing automation, or adjust your filters to see more.",
        action: /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-link-button", onClick: X, children: [
          /* @__PURE__ */ r.jsx(Gt, { size: 15 }),
          " Create workflow"
        ] })
      }
    ) : null,
    f === "ready" && m.length > 0 ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ r.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ r.jsx(
            "input",
            {
              ref: _,
              type: "checkbox",
              checked: F,
              onChange: (V) => Y(V.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ r.jsx("span", { children: "Name" }),
          /* @__PURE__ */ r.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ r.jsx("span", { children: s === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ r.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ r.jsx("span", { children: "Actions" })
        ] }),
        m.map((V) => /* @__PURE__ */ r.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${V.name}`,
            "aria-selected": j.has(V.id),
            tabIndex: 0,
            onClick: () => n(V.id),
            onKeyDown: (Q) => {
              Q.currentTarget === Q.target && (Q.key !== "Enter" && Q.key !== " " || (Q.preventDefault(), n(V.id)));
            },
            children: [
              /* @__PURE__ */ r.jsx("label", { className: "wf-row-select", onClick: (Q) => Q.stopPropagation(), children: /* @__PURE__ */ r.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: j.has(V.id),
                  onChange: (Q) => z(V.id, Q.target.checked),
                  "aria-label": `Select workflow definition ${V.name}`
                }
              ) }),
              /* @__PURE__ */ r.jsxs("span", { children: [
                /* @__PURE__ */ r.jsx("strong", { children: V.name }),
                /* @__PURE__ */ r.jsx("small", { children: V.description || V.id })
              ] }),
              /* @__PURE__ */ r.jsx("span", { children: V.latestVersion ?? "No version" }),
              /* @__PURE__ */ r.jsx("span", { children: s === "deleted" ? Pe(V.deletedAt) : V.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ r.jsx("span", { children: Pe(V.lastModifiedAt) }),
              /* @__PURE__ */ r.jsx("span", { className: "wf-row-actions", onClick: (Q) => Q.stopPropagation(), children: s === "active" ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
                /* @__PURE__ */ r.jsx("button", { type: "button", onClick: (Q) => {
                  Q.stopPropagation(), n(V.id);
                }, children: "Open" }),
                /* @__PURE__ */ r.jsx("button", { type: "button", onClick: (Q) => {
                  Q.stopPropagation(), ee(V.id);
                }, children: "Artifacts" }),
                P ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => Pt(t, P, V), children: [
                  /* @__PURE__ */ r.jsx(lt, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ r.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  J(V);
                }, children: [
                  /* @__PURE__ */ r.jsx(An, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
                /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
                  oe(V);
                }, children: [
                  /* @__PURE__ */ r.jsx(Fr, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ r.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  fe(V);
                }, children: [
                  /* @__PURE__ */ r.jsx(An, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          V.id
        ))
      ] }),
      /* @__PURE__ */ r.jsx(
        JN,
        {
          page: c,
          pageSize: l,
          totalCount: g,
          onPageChange: u,
          onPageSizeChange: (V) => {
            d(V), u(1);
          }
        }
      )
    ] }) : null,
    S ? /* @__PURE__ */ r.jsx(
      QN,
      {
        draft: S,
        creating: D,
        ai: t,
        suggestMetadataAction: $,
        onChange: (V) => I(V),
        onClose: () => I(null),
        onSubmit: q
      }
    ) : null
  ] });
}
function tj({ activities: e, activityCatalog: t, selectedEvidenceId: n = null, onSelectEvidence: i }) {
  const o = de(
    () => new Map(t.map((a) => [a.activityTypeKey, a])),
    [t]
  ), s = de(() => ij(e), [e]);
  return s.length === 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty wf-timeline-empty", children: "No activity executions recorded yet." }) : /* @__PURE__ */ r.jsx("ol", { className: "wf-timeline", "aria-label": "Execution timeline", children: s.map((a) => {
    const c = o.get(a.activityType), u = eo(c), l = c ? Ee(c) : $t(a.activityType) ?? a.activityType, d = $t(a.activityType) ?? a.activityType, f = oj(a.startedAt ?? a.scheduledAt), p = ps(a.startedAt, a.completedAt);
    return /* @__PURE__ */ r.jsx("li", { children: /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-timeline-entry",
        "data-selected": a.activityExecutionId === n,
        onClick: () => i?.(a.activityExecutionId),
        children: [
          /* @__PURE__ */ r.jsx("span", { className: "wf-timeline-icon wf-activity-icon", "data-icon": u, "aria-hidden": "true", children: hs(u) }),
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
          /* @__PURE__ */ r.jsx(nj, { status: a.status })
        ]
      }
    ) }, a.activityExecutionId);
  }) });
}
function nj({ status: e }) {
  const t = e.toLowerCase(), n = t === "completed" ? "OK" : e;
  return /* @__PURE__ */ r.jsx("span", { className: "wf-status-badge wf-timeline-pill", "data-status": t, children: n });
}
function ij(e) {
  return e.map((t, n) => ({ activity: t, index: n })).sort((t, n) => _c(t.activity) - _c(n.activity) || t.index - n.index).map((t) => t.activity);
}
function _c(e) {
  const t = e.startedAt ?? e.scheduledAt, n = t ? Date.parse(t) : Number.NaN;
  return Number.isNaN(n) ? Number.MAX_SAFE_INTEGER : n;
}
function oj(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? "" : t.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: !1 });
}
function rj({ context: e }) {
  const [t, n] = B("loading"), [i, o] = B(""), [s, a] = B(""), [c, u] = B(""), [l, d] = B([]), f = re(async () => {
    n("loading"), o("");
    try {
      const h = await Hp(e, {
        status: s || void 0,
        runKind: c || void 0,
        take: 100
      });
      d(h), n("ready");
    } catch (h) {
      o(h instanceof Error ? h.message : String(h)), d([]), n("failed");
    }
  }, [e, c, s]);
  G(() => {
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
    t === "failed" ? /* @__PURE__ */ r.jsx(Qn, { message: i }) : null,
    t === "loading" ? /* @__PURE__ */ r.jsx(ms, {}) : null,
    t === "ready" && l.length === 0 ? /* @__PURE__ */ r.jsx(
      xs,
      {
        icon: /* @__PURE__ */ r.jsx(Fn, { size: 22 }),
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
            /* @__PURE__ */ r.jsx("span", { children: bd(h.runKind) }),
            /* @__PURE__ */ r.jsx("span", { children: /* @__PURE__ */ r.jsx(un, { status: h.status, subStatus: h.subStatus }) }),
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
            /* @__PURE__ */ r.jsx("span", { children: Pe(h.startedAt ?? h.createdAt) }),
            /* @__PURE__ */ r.jsx("span", { children: ps(h.startedAt ?? h.createdAt, h.completedAt ?? h.updatedAt) })
          ]
        },
        h.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function sj({ context: e, ai: t, workflowExecutionId: n }) {
  const [i, o] = B("loading"), [s, a] = B(""), [c, u] = B(null), [l, d] = B(null), [f, p] = B([]), {
    inspectorWidth: h,
    inspectorCollapsed: y,
    maximizedSidePanel: w,
    inspectorExpanded: x,
    editorBodyStyle: m,
    toggleSidePanelCollapsed: b,
    toggleSidePanelMaximized: g,
    startSidePanelResize: v,
    handleSidePanelResizeKeyDown: j
  } = Ad(), N = Tt(t, "weaver.workflows.explain-instance"), S = re(async () => {
    if (!n) {
      a("No workflow execution id was provided."), o("failed");
      return;
    }
    o("loading"), a("");
    try {
      const A = await Wp(e, n), [k, R] = await Promise.all([
        _p(e, A.instance.definitionVersionId).then(
          (C) => ({ definitionVersion: C, error: "" }),
          (C) => ({ definitionVersion: null, error: C instanceof Error ? C.message : String(C) })
        ),
        Yr(e)
      ]);
      u({
        details: A,
        definitionVersion: k.definitionVersion,
        definitionVersionError: k.error,
        activityCatalog: R.activities
      }), d(null), p([]), o("ready");
    } catch (A) {
      u(null), a(wb(A, n)), o("failed");
    }
  }, [e, n]);
  G(() => {
    S();
  }, [S]);
  const I = () => {
    window.history.pushState({}, "", "/workflows/instances"), window.dispatchEvent(new PopStateEvent("popstate"));
  }, D = () => {
    const A = c?.details.instance.definitionId;
    A && (window.history.pushState({}, "", `/workflows/definitions?definition=${encodeURIComponent(A)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  }, M = [
    "wf-instance-detail-workbench",
    y ? "inspector-collapsed" : "",
    w === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: I, children: [
        /* @__PURE__ */ r.jsx(Dn, { size: 14 }),
        " Runs"
      ] }),
      c?.details.instance.definitionId ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: D, children: [
        /* @__PURE__ */ r.jsx(Wc, { size: 14 }),
        " Designer"
      ] }) : null,
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
        S();
      }, children: [
        /* @__PURE__ */ r.jsx(Fr, { size: 14 }),
        " Refresh"
      ] }),
      c && N ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => Pt(t, N, c.details), children: [
        /* @__PURE__ */ r.jsx(lt, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    i === "loading" ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Loading workflow run..." }) : null,
    i === "failed" ? /* @__PURE__ */ r.jsx(Qn, { message: s }) : null,
    i === "ready" && c ? /* @__PURE__ */ r.jsxs("div", { className: M, style: m, children: [
      /* @__PURE__ */ r.jsx(
        aj,
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
      x && !w ? /* @__PURE__ */ r.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize run details panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Xt,
          "aria-valuemax": Yt,
          "aria-valuenow": h,
          tabIndex: 0,
          onPointerDown: (A) => v("inspector", A),
          onKeyDown: (A) => j("inspector", A)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsx(
        uj,
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
          graphNodeIds: c.definitionVersion ? lj(c.definitionVersion, c.activityCatalog) : void 0,
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
function aj({
  definitionVersion: e,
  definitionVersionError: t,
  activityCatalog: n,
  details: i,
  selectedEvidenceId: o,
  onSelectEvidence: s,
  frames: a,
  onNavigateToScope: c
}) {
  const u = cj(a), l = de(() => {
    if (!e) return { nodes: [], edges: [] };
    const d = e.state.rootActivity;
    if (!d) return { nodes: [], edges: [] };
    const f = $n(d, a, n), p = f?.owner ?? d, h = n.find((m) => m.activityVersionId === p.activityVersionId), w = il(p, h) === "unsupported" || !f ? nl(p, n, e.layout) : tl(f, n, e.layout), x = w.nodes.map((m) => ({
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
      nodes: Uf(x, i.activities, i.incidents, o),
      edges: w.edges.map((m) => ({ ...m, deletable: !1 }))
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
      /* @__PURE__ */ r.jsx(un, { status: i.instance.status, subStatus: i.instance.subStatus })
    ] }),
    e ? /* @__PURE__ */ r.jsxs("div", { className: "wf-breadcrumb wf-instance-breadcrumb", children: [
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => c([]), children: "Root" }),
      a.map((d, f) => /* @__PURE__ */ r.jsxs("span", { className: "wf-breadcrumb-segment", children: [
        /* @__PURE__ */ r.jsx(yt, { size: 13 }),
        /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => c(a.slice(0, f + 1)), children: d.label })
      ] }, `${d.ownerNodeId}-${d.slotId}-${f}`))
    ] }) : null,
    /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ r.jsxs("div", { className: "wf-empty", children: [
        "The workflow run loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ r.jsx("small", { children: xb(t) }) : null
      ] }),
      e && l.nodes.length === 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      l.nodes.length > 0 ? /* @__PURE__ */ r.jsxs(
        od,
        {
          nodes: l.nodes,
          edges: l.edges,
          nodeTypes: Ed,
          edgeTypes: Id,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (d, f) => s(f.id),
          onPaneClick: () => s(null),
          children: [
            /* @__PURE__ */ r.jsx(sd, {}),
            /* @__PURE__ */ r.jsx(ud, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ r.jsx(cd, {})
          ]
        },
        u
      ) : null
    ] })
  ] });
}
function cj(e) {
  return e.length === 0 ? "root" : e.map((t) => `${t.ownerNodeId}:${t.slotId}`).join("/");
}
function lj(e, t) {
  const n = /* @__PURE__ */ new Set(), i = (o) => {
    if (o) {
      n.add(o.nodeId);
      for (const s of Me(o, t))
        s.activities.forEach(i);
    }
  };
  return i(e.state.rootActivity), n;
}
function uj({
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
  onToggleCollapsed: w,
  onToggleMaximized: x
}) {
  const [m, b] = B("timeline");
  if (!i)
    return /* @__PURE__ */ r.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Select a workflow run to inspect its timeline." }) });
  const g = o?.incidents.length ?? 0, v = dj(o?.activities ?? [], c), j = (S) => {
    u?.(S), b("activity");
  }, N = [
    { id: "timeline", title: "Timeline", order: 0, icon: /* @__PURE__ */ r.jsx(Wr, { size: 14 }), render: () => null },
    { id: "activity", title: "Activity", order: 1, icon: /* @__PURE__ */ r.jsx(Rc, { size: 14 }), render: () => null },
    { id: "issues", title: g > 0 ? `Issues (${g})` : "Issues", order: 2, icon: /* @__PURE__ */ r.jsx(wt, { size: 14 }), render: () => null },
    { id: "details", title: "Details", order: 3, icon: /* @__PURE__ */ r.jsx(Hr, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ r.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Run details panel", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title wf-instance-panel-title", children: [
      /* @__PURE__ */ r.jsx($i, { label: "Run details tabs", tabs: N, activeTabId: m, onSelect: (S) => b(S) }),
      /* @__PURE__ */ r.jsxs("span", { className: "wf-panel-actions", children: [
        /* @__PURE__ */ r.jsx(
          "button",
          {
            type: "button",
            className: "wf-panel-action-button",
            "aria-label": p ? "Expand run details panel" : "Collapse run details panel",
            title: p ? "Expand" : "Collapse",
            onClick: w,
            children: p ? /* @__PURE__ */ r.jsx(Dn, { size: 14 }) : /* @__PURE__ */ r.jsx(yt, { size: 14 })
          }
        ),
        p ? null : /* @__PURE__ */ r.jsx(
          "button",
          {
            type: "button",
            className: "wf-panel-action-button",
            "aria-label": y ? "Restore run details panel" : "Maximize run details panel",
            title: y ? "Restore" : "Maximize",
            onClick: x,
            children: y ? /* @__PURE__ */ r.jsx(yr, { size: 14 }) : /* @__PURE__ */ r.jsx(_n, { size: 14 })
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
        n ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => Pt(t, n, o ?? i), children: [
          /* @__PURE__ */ r.jsx(lt, { size: 13 }),
          " Explain"
        ] }) : null
      ] }),
      s === "loading" ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Loading run details..." }) : null,
      s === "failed" ? /* @__PURE__ */ r.jsx(Qn, { message: a }) : null,
      s === "ready" && o ? /* @__PURE__ */ r.jsx("div", { className: "wf-instance-tab-content", children: m === "timeline" ? /* @__PURE__ */ r.jsx(
        tj,
        {
          activities: o.activities,
          activityCatalog: f,
          selectedEvidenceId: c,
          onSelectEvidence: j
        }
      ) : m === "activity" ? /* @__PURE__ */ r.jsx(fj, { context: e, activity: v, activityCatalog: f }) : m === "issues" ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
        /* @__PURE__ */ r.jsx(wj, { incidents: o.incidents, selectedEvidenceId: c, onSelectEvidence: u }),
        /* @__PURE__ */ r.jsx(vj, { details: o, graphNodeIds: l, rootNodeId: d })
      ] }) : /* @__PURE__ */ r.jsxs("dl", { className: "wf-instance-meta", children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ r.jsx("dd", { children: /* @__PURE__ */ r.jsx(un, { status: i.status, subStatus: i.subStatus }) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Run Kind" }),
        /* @__PURE__ */ r.jsx("dd", { children: bd(i.runKind) }),
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
        /* @__PURE__ */ r.jsx("dd", { children: Pe(i.createdAt) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Started" }),
        /* @__PURE__ */ r.jsx("dd", { children: Pe(i.startedAt) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Completed" }),
        /* @__PURE__ */ r.jsx("dd", { children: Pe(i.completedAt) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Correlation" }),
        /* @__PURE__ */ r.jsx("dd", { children: i.correlationId || "None" })
      ] }) }) : null
    ] }) : null
  ] });
}
function dj(e, t) {
  if (!t) return null;
  const n = e.find((o) => o.activityExecutionId === t);
  if (n) return n;
  const i = e.filter((o) => o.executableNodeId === t || o.authoredActivityId === t);
  return i.length > 0 ? ul(i) : null;
}
function fj({ context: e, activity: t, activityCatalog: n }) {
  const i = t?.activityExecutionId ?? null, o = t?.workflowExecutionId ?? null, [s, a] = B({
    activityExecutionId: null,
    status: "idle",
    inspection: null,
    error: ""
  });
  if (G(() => {
    if (!i || !o) {
      a({ activityExecutionId: null, status: "idle", inspection: null, error: "" });
      return;
    }
    let f = !1;
    const p = i;
    return a({ activityExecutionId: p, status: "loading", inspection: null, error: "" }), Fp(e, o, p).then(
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
  const u = n.find((f) => f.activityTypeKey === t.activityType)?.displayName || $t(t.activityType) || t.activityType, l = t.bookmarkIds?.length ?? 0, d = t.incidentIds?.length ?? 0;
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
      /* @__PURE__ */ r.jsx("h4", { children: "Activity" }),
      /* @__PURE__ */ r.jsxs("dl", { className: "wf-instance-meta", children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Name" }),
        /* @__PURE__ */ r.jsx("dd", { children: u }),
        /* @__PURE__ */ r.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ r.jsx("dd", { children: /* @__PURE__ */ r.jsx(un, { status: t.status, subStatus: t.subStatus }) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Activity Execution ID" }),
        /* @__PURE__ */ r.jsx("dd", { children: t.activityExecutionId }),
        /* @__PURE__ */ r.jsx("dt", { children: "Authored Activity ID" }),
        /* @__PURE__ */ r.jsx("dd", { children: t.authoredActivityId }),
        /* @__PURE__ */ r.jsx("dt", { children: "Type" }),
        /* @__PURE__ */ r.jsxs("dd", { children: [
          $t(t.activityType) ?? t.activityType,
          " ",
          /* @__PURE__ */ r.jsx("small", { children: t.activityTypeVersion })
        ] }),
        /* @__PURE__ */ r.jsx("dt", { children: "Started" }),
        /* @__PURE__ */ r.jsx("dd", { children: Pe(t.startedAt) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Completed" }),
        /* @__PURE__ */ r.jsx("dd", { children: Pe(t.completedAt) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Duration" }),
        /* @__PURE__ */ r.jsx("dd", { children: ps(t.startedAt, t.completedAt) || "Unknown" }),
        /* @__PURE__ */ r.jsx("dt", { children: "Bookmarks" }),
        /* @__PURE__ */ r.jsx("dd", { children: l }),
        /* @__PURE__ */ r.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ r.jsx("dd", { children: d })
      ] })
    ] }),
    /* @__PURE__ */ r.jsx(pj, { state: s })
  ] });
}
function pj({ state: e }) {
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
    /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-input-list", children: t.map((n) => /* @__PURE__ */ r.jsx(hj, { snapshot: n }, `${n.name}:${n.capturedAt}:${n.captureMode}`)) })
  ] });
}
function hj({ snapshot: e }) {
  const t = e.type?.displayName || e.type?.typeName || e.type?.alias || "Unknown", n = e.captureMode === "Payload";
  return /* @__PURE__ */ r.jsxs("article", { className: "wf-runtime-input", children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("span", { children: [
        /* @__PURE__ */ r.jsx("strong", { children: e.name }),
        /* @__PURE__ */ r.jsx("small", { children: t })
      ] }),
      /* @__PURE__ */ r.jsx("span", { className: "wf-runtime-capture-mode", children: yj(e.captureMode) })
    ] }),
    n ? /* @__PURE__ */ r.jsx(gj, { payload: e.payload }) : /* @__PURE__ */ r.jsx("p", { children: e.captureReason || "The runtime capture policy did not include this input value." }),
    e.isSensitive ? /* @__PURE__ */ r.jsx("p", { className: "wf-instance-note", children: "Marked sensitive by runtime evidence." }) : null
  ] });
}
function gj({ payload: e }) {
  const t = xj(e);
  return t.length <= 160 && !t.includes(`
`) ? /* @__PURE__ */ r.jsx("code", { className: "wf-runtime-input-value", children: t }) : /* @__PURE__ */ r.jsxs("details", { className: "wf-runtime-input-value-details", children: [
    /* @__PURE__ */ r.jsx("summary", { children: mj(t) }),
    /* @__PURE__ */ r.jsx("pre", { children: t })
  ] });
}
function yj(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2");
}
function mj(e) {
  const t = e.split(`
`, 1)[0] || e;
  return t.length > 120 ? `${t.slice(0, 117)}...` : t;
}
function xj(e) {
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
function wj({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
  const [i, o] = B("");
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ r.jsx("h4", { children: "Incidents" }),
    e.length === 0 ? /* @__PURE__ */ r.jsx("p", { children: "No incidents recorded." }) : null,
    e.map((s) => /* @__PURE__ */ r.jsxs(
      "div",
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
              value: bj(s),
              ariaLabel: `Copy incident ${s.failureType}`,
              copiedLabel: "incident",
              onCopied: (a) => o(`Copied ${a}`),
              onCopyFailed: (a) => o(`Could not copy ${a}.`)
            }
          )
        ]
      },
      s.incidentId
    )),
    i ? /* @__PURE__ */ r.jsx("p", { className: "wf-copy-status", role: "status", children: i }) : null
  ] });
}
function vj({ details: e, graphNodeIds: t, rootNodeId: n }) {
  if (!t) return null;
  const i = e.activities.filter((o) => {
    const s = ub(o);
    return s && s !== n && !t.has(s);
  });
  return i.length === 0 ? null : /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ r.jsx("h4", { children: "Executions outside canvas" }),
    /* @__PURE__ */ r.jsx("div", { className: "wf-instance-unmatched-list", children: i.map((o) => /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-unmatched", children: [
      /* @__PURE__ */ r.jsx("strong", { children: $t(o.activityType) ?? o.activityType }),
      /* @__PURE__ */ r.jsx("small", { children: o.activityExecutionId })
    ] }, `activity-${o.activityExecutionId}`)) })
  ] });
}
function bj(e) {
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
    `Created: ${Pe(e.createdAt)}`,
    e.resolvedAt ? `Resolved: ${Pe(e.resolvedAt)}` : "",
    "",
    e.message
  ].filter(Boolean);
  return e.metadata && Object.keys(e.metadata).length > 0 && t.push("", "Metadata:", JSON.stringify(e.metadata, null, 2)), t.join(`
`);
}
function Nj({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: i,
  workflowDesignerPanels: o,
  autosaveEnabledByDefault: s
}) {
  const [a, c] = B(Dc);
  G(() => {
    const l = () => c(Dc());
    return window.addEventListener("popstate", l), () => window.removeEventListener("popstate", l);
  }, []);
  const u = (l) => {
    const d = l ? `/workflows/definitions?definition=${encodeURIComponent(l)}` : "/workflows/definitions";
    window.history.pushState({}, "", d), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return a ? /* @__PURE__ */ r.jsx(GN, { context: e, definitionId: a, ai: t, propertyEditors: n, expressionEditors: i, workflowDesignerPanels: o, autosaveEnabledByDefault: s, onBack: () => u(null) }) : /* @__PURE__ */ r.jsx(go, { title: "Definitions", children: /* @__PURE__ */ r.jsx(ej, { context: e, ai: t, onOpen: u }) });
}
function jj({ context: e, ai: t }) {
  const [n, i] = B($c);
  G(() => {
    const s = () => i($c());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const o = re((s) => {
    const a = s?.trim() ?? "", c = new URL(window.location.href);
    a ? c.searchParams.set("definition", a) : c.searchParams.delete("definition"), i(a || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ r.jsx(go, { title: "Executables", children: /* @__PURE__ */ r.jsx(Tb, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: o }) });
}
function Sj({ context: e }) {
  return /* @__PURE__ */ r.jsx(go, { title: "Runs", children: /* @__PURE__ */ r.jsx(rj, { context: e }) });
}
function Cj({ context: e, ai: t }) {
  const n = Ej();
  return /* @__PURE__ */ r.jsx(go, { title: "Run", children: /* @__PURE__ */ r.jsx(sj, { context: e, ai: t, workflowExecutionId: n }) });
}
function go({ title: e, children: t }) {
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ r.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ r.jsxs("div", { children: [
      /* @__PURE__ */ r.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ r.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function Dc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function $c() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Ej() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function Dj(e) {
  hf(e.dialogs), e.featureAreas.add({
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
        component: () => /* @__PURE__ */ r.jsx(Nj, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list(), autosaveEnabledByDefault: e.runtime.workflows?.autosaveEnabledByDefault ?? !0 })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ r.jsx(jj, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => /* @__PURE__ */ r.jsx(Sj, { context: e.backend })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => /* @__PURE__ */ r.jsx(Cj, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => /* @__PURE__ */ r.jsx(uh, { context: e.backend })
      }
    ]
  });
}
export {
  fb as isConnectEndOverExistingWorkflowNode,
  Dj as register,
  pb as resolveConnectEndSource
};
