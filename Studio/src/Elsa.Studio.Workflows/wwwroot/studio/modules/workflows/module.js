import Ge, { useMemo as de, useState as B, useEffect as J, memo as Ne, forwardRef as Tc, useRef as re, useCallback as se, useContext as zn, createContext as Hr, useLayoutEffect as Vd, lazy as Od, Suspense as Hd, useReducer as Wd, useId as Pc } from "react";
import { ListChecks as Fd, Save as $c, EyeOff as ks, Shield as As, AlertTriangle as _i, SlidersHorizontal as Wr, Activity as Mc, Search as Yi, Check as nn, Boxes as Ui, Zap as Bd, Play as Bt, Terminal as Kd, ListTree as Fr, GitBranch as Rc, Plus as Kt, Trash2 as Sn, AlertCircle as ht, Wrench as Xd, Copy as qd, X as zc, Sparkles as rt, RotateCcw as Br, ChevronDown as Lc, ChevronRight as yt, GripVertical as Vc, Maximize2 as Cn, ChevronUp as Yd, Package as Oc, Undo2 as Ud, Redo2 as Zd, Network as Gd, Download as Jd, ChevronLeft as En, Minimize2 as yr, Workflow as Hc, Code2 as Qd } from "lucide-react";
import { useQuery as Wc, useQueryClient as ef, useMutation as tf } from "@tanstack/react-query";
import { useTablistKeyboard as nf } from "@elsa-workflows/studio-ui";
function of(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var Jo = { exports: {} }, ln = {};
var _s;
function rf() {
  if (_s) return ln;
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
  return ln.Fragment = t, ln.jsx = n, ln.jsxs = n, ln;
}
var Ds;
function sf() {
  return Ds || (Ds = 1, Jo.exports = rf()), Jo.exports;
}
var r = sf();
let Fc;
function af(e) {
  Fc = e;
}
function Ts() {
  return Fc;
}
const cf = "String", lf = "singleline";
function uf(e) {
  return e === "Single" || e === "Array" || e === "List" || e === "HashSet";
}
function Kr(e, t = "Single") {
  return { alias: (e ?? "").trim() || cf, collectionKind: t };
}
function Bc(e) {
  const t = e.type ?? e.Type;
  if (Di(t))
    return { alias: typeof t.alias == "string" ? t.alias : typeof t.typeName == "string" ? t.typeName : "", collectionKind: uf(t.collectionKind) ? t.collectionKind : "Single" };
  const n = e.typeInformation ?? e.TypeInformation;
  return Di(n) ? { alias: typeof n.typeName == "string" ? n.typeName : "", collectionKind: Ps(e) ? "Array" : "Single" } : { alias: typeof t == "string" ? t : "", collectionKind: Ps(e) ? "Array" : "Single" };
}
function Ps(e) {
  const t = e.isArray ?? e.IsArray;
  return t === !0 || t === "true";
}
function $s(e) {
  return (e.alias ?? e.typeName ?? "").trim();
}
function Zi() {
  return typeof crypto < "u" && typeof crypto.randomUUID == "function" ? crypto.randomUUID() : `id-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
}
function df(e, t) {
  const n = new Set(t);
  let i = 1, o = `${e}${i}`;
  for (; n.has(o); )
    i += 1, o = `${e}${i}`;
  return o;
}
function ff(e) {
  return {
    referenceKey: Zi(),
    name: e.name,
    type: Kr(e.alias),
    storageDriverType: e.storageDriver?.trim() || null,
    default: null
  };
}
function pf(e, t) {
  return { ...e, ...t };
}
function hf(e) {
  return e === "" ? null : { value: e, expressionType: "Literal" };
}
function gf(e) {
  return e == null || e.value == null ? "" : typeof e.value == "object" ? JSON.stringify(e.value) : String(e.value);
}
function yf(e) {
  return {
    referenceKey: Zi(),
    name: e.name,
    type: Kr(e.alias),
    displayName: e.name,
    description: "",
    category: "",
    uiHint: lf,
    storageDriverType: e.storageDriver ?? null,
    isRequired: !1
  };
}
function mf(e, t) {
  return { ...e, ...t };
}
function xf(e) {
  return {
    referenceKey: Zi(),
    name: e.name,
    type: Kr(e.alias),
    displayName: e.name,
    description: "",
    category: ""
  };
}
function wf(e, t) {
  return { ...e, ...t };
}
function vf(e) {
  const t = e.split(",")[0].trim();
  return (t.split(".").pop() ?? t).split("`")[0];
}
function Kc(e, t) {
  return e && !e.includes(",") && !e.includes(".") ? e : vf(e || t);
}
function bf(e, t) {
  return Kc(e, t).replace(/StorageDriver$/, "");
}
function Di(e) {
  return !!e && typeof e == "object" && !Array.isArray(e);
}
function vn(e, t) {
  for (const n of t) {
    const i = e[n];
    if (i != null && typeof i != "object") return String(i);
  }
  return "";
}
const Nf = ["name", "Name"], Xc = ["name", "Name"], jf = ["storageDriverType", "StorageDriverType"], qc = ["referenceKey", "ReferenceKey"], Sf = /* @__PURE__ */ new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);
function Ln(e) {
  return Yc(e, kf);
}
function Gi(e) {
  return Yc(e, Af);
}
function Yc(e, t) {
  if (!e) return e;
  const n = { ...e };
  return e.rootActivity && (n.rootActivity = Uc(e.rootActivity, t)), Array.isArray(e.variables) && (n.variables = wi(e.variables, mr)), Array.isArray(e.inputs) && (n.inputs = wi(e.inputs, mr)), Array.isArray(e.outputs) && (n.outputs = wi(e.outputs, (i) => Zc(i, !1))), n;
}
function Uc(e, t) {
  const n = t(e), i = n.structure;
  if (!i || !Je(i.payload)) return n;
  let o = !1;
  const s = { ...i.payload };
  for (const [a, c] of Object.entries(i.payload))
    Array.isArray(c) && c.length > 0 && c.every($f) && (s[a] = c.map((u) => Uc(u, t)), o = !0);
  return Array.isArray(i.payload.variables) && i.payload.variables.length > 0 && (s.variables = wi(i.payload.variables, mr), o = !0), o ? { ...n, structure: { ...i, payload: s } } : n;
}
function wi(e, t) {
  return e.map((n) => Je(n) && !Array.isArray(n) ? t(n) : n);
}
function mr(e) {
  return Zc(e, !0);
}
const Cf = [
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
  const n = Ef(e, Cf);
  return vn(e, qc).trim() || (n.referenceKey = Zi()), n.type = Bc(e), t && (n.storageDriverType = If(e.storageDriverType ?? e.StorageDriverType)), n;
}
function Ef(e, t) {
  const n = new Set(t), i = {};
  for (const [o, s] of Object.entries(e))
    n.has(o) || (i[o] = s);
  return i;
}
function If(e) {
  if (typeof e == "string") return e.trim() ? e : null;
  if (Je(e)) {
    const t = typeof e.typeName == "string" ? e.typeName : "";
    if (!t) return null;
    const n = typeof e.namespace == "string" ? e.namespace : "";
    return n ? `${n}.${t}` : t;
  }
  return null;
}
function kf(e) {
  const t = [], n = {};
  for (const [o, s] of Object.entries(e))
    Sf.has(o) || (Pf(s) ? t.push({
      referenceKey: _f(o),
      value: Tf(s.expression)
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
function Af(e) {
  const t = Array.isArray(e.inputs) ? e.inputs : [], n = {};
  for (const i of t) {
    if (!Je(i) || typeof i.referenceKey != "string") continue;
    const o = Je(i.value) ? i.value : {};
    n[Df(i.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof o.expressionType == "string" ? o.expressionType : "Literal",
        value: o.value ?? ""
      }
    };
  }
  return { ...e, ...n, inputs: [] };
}
function _f(e) {
  return e && e.charAt(0).toUpperCase() + e.slice(1);
}
function Df(e) {
  return e && e.charAt(0).toLowerCase() + e.slice(1);
}
function Tf(e) {
  const t = e.type || "Literal";
  return t === "Variable" && Je(e.value) ? { value: e.value, expressionType: t } : t === "Literal" && Je(e.value) ? { value: Ms(e.value), expressionType: "Object" } : { value: Ms(e.value), expressionType: t };
}
function Ms(e) {
  return e == null ? null : typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function Pf(e) {
  if (!Je(e) || Array.isArray(e)) return !1;
  const t = e.expression;
  return Je(t) && typeof t.type == "string";
}
function $f(e) {
  return Je(e) && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Je(e) {
  return typeof e == "object" && e !== null;
}
const Vn = "elsa.sequence.structure", on = "elsa.flowchart.structure";
function Gc(e, t, n) {
  if (!e) return null;
  let i = e;
  for (const o of t) {
    const s = De(i, n).find((c) => c.id === o.slotId);
    if (!s) return null;
    const a = s.activities.find((c) => c.nodeId === o.ownerNodeId);
    if (!a) return null;
    i = a;
  }
  return i;
}
function Mf(e, t, n = (o) => o.nodeId, i) {
  if (!e) return null;
  if (e.nodeId === t) return [];
  const o = (s, a) => {
    const c = De(s, i);
    if (c[0]?.activities.some((u) => u.nodeId === t)) return a;
    for (const u of c)
      for (const l of u.activities) {
        const d = o(l, [...a, { ownerNodeId: l.nodeId, slotId: u.id, label: n(l) }]);
        if (d) return d;
      }
    return null;
  };
  return o(e, []);
}
function In(e, t, n) {
  const i = Gc(e, t, n);
  if (!i) return null;
  const o = De(i, n)[0];
  return o ? { owner: i, slot: o } : null;
}
function De(e, t) {
  const n = e.structure;
  if (!n || !n.payload || typeof n.payload != "object") return [];
  const i = n.payload, o = Xr(Jc(e, t));
  if (o?.kind === n.kind) return Lf(n, o);
  const s = op(n), a = pn(i.activities);
  return a ? [{
    id: `${n.kind}:activities`,
    label: rp(n),
    property: "activities",
    cardinality: "many",
    mode: s,
    activities: a
  }] : Object.entries(i).filter(([, c]) => pn(c) || Pi(c)).map(([c, u]) => ({
    id: `${n.kind}:${c}`,
    label: ap(c),
    property: c,
    cardinality: pn(u) ? "many" : "single",
    mode: "generic",
    activities: pn(u) ?? (Pi(u) ? [u] : [])
  }));
}
function Xr(e) {
  for (const t of e?.designFacets ?? []) {
    if (!Qe(t)) continue;
    const n = typeof t.kind == "string" ? t.kind : "", i = typeof t.schemaVersion == "string" ? t.schemaVersion : "";
    if (!n || !i || !Qe(t.payload)) continue;
    const o = Rf(t.payload);
    if (o) return { kind: n, schemaVersion: i, payload: o };
  }
  return null;
}
function Rf(e) {
  const t = e.mode;
  if (t !== "sequence" && t !== "flowchart" && t !== "generic" || typeof e.supportsScopedVariables != "boolean" || !Array.isArray(e.slots) || !Qe(e.initialPayload)) return null;
  const n = e.slots.map(zf).filter((i) => i !== null);
  return {
    mode: t,
    supportsScopedVariables: e.supportsScopedVariables,
    slots: n,
    initialPayload: e.initialPayload
  };
}
function zf(e) {
  if (!Qe(e)) return null;
  const t = typeof e.name == "string" ? e.name : "", n = typeof e.property == "string" ? e.property : "", i = typeof e.displayName == "string" ? e.displayName : "", o = e.cardinality;
  return !t || !n || !i || o !== "single" && o !== "many" ? null : {
    name: t,
    property: n,
    displayName: i,
    cardinality: o,
    collectionProperty: Rt(e.collectionProperty),
    childProperty: Rt(e.childProperty),
    labelProperty: Rt(e.labelProperty),
    slotNameTemplate: Rt(e.slotNameTemplate)
  };
}
function Lf(e, t) {
  return t.payload.slots.flatMap((n) => {
    if (n.collectionProperty && n.childProperty) {
      const i = e.payload[n.collectionProperty];
      return Array.isArray(i) ? i.flatMap((o, s) => {
        if (!Qe(o)) return [];
        const a = n.labelProperty ? Rt(o[n.labelProperty]) : void 0;
        return [Rs(e.kind, n, o[n.childProperty], t.payload.mode, s, a)];
      }) : [];
    }
    return [Rs(e.kind, n, e.payload[n.property], t.payload.mode)];
  });
}
function Rs(e, t, n, i, o, s) {
  const a = o === void 0 ? "" : `:${t.collectionProperty}:${t.childProperty}:${o}`;
  return {
    id: `${e}:${t.property}${a}`,
    label: o === void 0 ? t.displayName : Of(t, o, s),
    property: t.property,
    cardinality: t.cardinality,
    mode: i,
    activities: Vf(n, t.cardinality),
    collectionProperty: t.collectionProperty,
    childProperty: t.childProperty,
    labelProperty: t.labelProperty,
    slotNameTemplate: t.slotNameTemplate,
    collectionIndex: o,
    collectionItemLabel: s
  };
}
function Vf(e, t) {
  return t === "many" ? pn(e) ?? [] : Pi(e) ? [e] : [];
}
function Of(e, t, n) {
  return e.slotNameTemplate ? e.slotNameTemplate.replaceAll("{name}", e.name).replaceAll("{displayName}", e.displayName).replaceAll("{label}", n ?? String(t + 1)).replaceAll("{index}", String(t + 1)) : n ? `${e.displayName}: ${n}` : `${e.displayName} ${t + 1}`;
}
function Jc(e, t) {
  if (t)
    return t instanceof Map ? t.get(e.activityVersionId) : Array.isArray(t) ? t.find((n) => n.activityVersionId === e.activityVersionId) : t.activityVersionId === e.activityVersionId ? t : void 0;
}
function Hf(e, t) {
  if (t.labelProperty && t.collectionItemLabel) {
    const n = e.map((i, o) => Qe(i) && Rt(i[t.labelProperty]) === t.collectionItemLabel ? o : -1).filter((i) => i >= 0);
    if (n.length === 1) return n[0];
  }
  return typeof t.collectionIndex == "number" && t.collectionIndex >= 0 && t.collectionIndex < e.length ? t.collectionIndex : -1;
}
function Qc(e, t, n) {
  const i = new Map(t.map((a) => [a.activityVersionId, a])), o = new Map(n.map((a) => [a.nodeId, a])), s = e.slot.activities.map((a, c) => {
    const u = i.get(a.activityVersionId), l = o.get(a.nodeId) ?? sp(e.slot.mode, c);
    return il(a, u, { x: l.x, y: l.y });
  });
  return {
    nodes: s,
    edges: e.slot.mode === "flowchart" ? rl(e.owner) : Gf(e.slot, s)
  };
}
function xr(e, t, n) {
  const i = t.find((s) => s.activityVersionId === e.activityVersionId), o = n.find((s) => s.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [il(e, i, { x: o.x, y: o.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function Wf(e, t, n, i = null) {
  const o = new Map(t.map((c) => [c.activityExecutionId, c])), s = Ls(t, (c) => c.authoredActivityId || c.executableNodeId), a = Ls(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? o.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const u = s.get(c.id) ?? [], l = a.get(c.id) ?? [];
    if (u.length === 0 && l.length === 0) return c;
    const d = al(u), f = i === c.id || u.some((h) => h.activityExecutionId === i) || l.some((h) => h.incidentId === i), p = {
      status: d?.status,
      subStatus: d?.subStatus,
      activityExecutionId: d?.activityExecutionId,
      faultCount: u.reduce((h, g) => h + g.faultCount + g.aggregateFaultCount, 0),
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
function qr(e, t) {
  if (e?.structure?.kind === on || qf(t)) return "flowchart";
  if (e?.structure?.kind === Vn || Yf(t)) return "sequence";
  if (e) {
    const n = De(e, t)[0];
    if (n) return n.mode === "flowchart" ? "flowchart" : "sequence";
  }
  return "unsupported";
}
function wr(e, t, n, i) {
  if (t.length === 0) {
    const u = De(e, i)[0];
    return u ? Xt(e, u, n) : e;
  }
  const [o, ...s] = t, a = De(e, i).find((u) => u.id === o.slotId);
  if (!a) return e;
  const c = a.activities.map((u) => u.nodeId === o.ownerNodeId ? wr(u, s, n, i) : u);
  return Xt(e, a, c);
}
function el(e, t, n, i) {
  if (t.length === 0) return n;
  const [o, ...s] = t, a = De(e, i).find((u) => u.id === o.slotId);
  if (!a) return e;
  const c = a.activities.map((u) => u.nodeId === o.ownerNodeId ? el(u, s, n, i) : u);
  return Xt(e, a, c);
}
function tl(e, t, n, i) {
  if (e.nodeId === t) return n(e);
  const o = De(e, i);
  if (o.length === 0) return e;
  let s = !1, a = e;
  for (const c of o) {
    const u = c.activities.map((l) => {
      const d = tl(l, t, n, i);
      return d !== l && (s = !0), d;
    });
    s && (a = Xt(a, c, u));
  }
  return s ? a : e;
}
function Xt(e, t, n) {
  if (!e.structure) return e;
  const i = t.cardinality === "single" ? n[0] ?? null : n;
  if (t.collectionProperty && t.childProperty) {
    const o = e.structure.payload[t.collectionProperty];
    if (!Array.isArray(o)) return e;
    const s = Hf(o, t);
    if (s < 0) return e;
    const a = o[s];
    if (!Qe(a)) return e;
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
function Ff(e, t, n, i = []) {
  const o = new Map(e.slot.activities.map((a) => [a.nodeId, a]));
  for (const a of i)
    o.set(a.nodeId, a);
  const s = t.map((a) => o.get(a.id)).filter((a) => !!a);
  return e.slot.mode === "sequence" && s.sort((a, c) => {
    const u = t.find((d) => d.id === a.nodeId), l = t.find((d) => d.id === c.nodeId);
    return (u?.position.x ?? 0) - (l?.position.x ?? 0);
  }), Xt(e.owner, e.slot, s);
}
function Bf(e, t) {
  return {
    ...e,
    structure: Zf(e.structure, t)
  };
}
function Kf(e, t) {
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
function vr(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: ol(e)
  };
}
function nl(e, t) {
  if (!e) return null;
  const n = Jc(e, t), i = e.structure ?? (n ? ol(n) : null);
  let o = i === e.structure ? e : { ...e, structure: i };
  const s = De(o, t);
  for (const a of s) {
    const c = a.activities.map((u) => nl(u, t) ?? u);
    c.some((u, l) => u !== a.activities[l]) && (o = Xt(o, a, c));
  }
  return o;
}
function Ce(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? Uf(t) : n;
}
function il(e, t, n, i = {}) {
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
      icon: Ji(t),
      childSlots: De(e, t),
      acceptsInbound: Jf(e, t),
      sourcePorts: i.suppressFlowPorts ? [] : sl(e, t),
      suppressFlowPorts: i.suppressFlowPorts
    }
  };
}
function Ji(e) {
  if (!e) return "activity";
  const t = Xf(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), i = Ce(e).toLowerCase(), o = e.category?.toLowerCase() ?? "", s = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || i === "flowchart" ? "flowchart" : n.endsWith(".sequence") || i === "sequence" ? "sequence" : n.includes("writeline") || i.includes("write line") ? "terminal" : o.includes("runtime") ? "runtime" : s === "trigger" ? "trigger" : "activity";
}
function Xf(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function qf(e) {
  return !!e && (Ce(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function Yf(e) {
  return !!e && (Ce(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function Uf(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function ol(e) {
  const t = Xr(e);
  return t ? {
    kind: t.kind,
    schemaVersion: t.schemaVersion,
    payload: np(t.payload.initialPayload)
  } : e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: Vn,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: on,
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
function Zf(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], i = /* @__PURE__ */ new Map();
  for (const o of n) {
    if (!Qe(o)) continue;
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
function Gf(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, i) => ({
    id: `sequence-${n.id}-${t[i + 1].id}`,
    source: n.id,
    target: t[i + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function rl(e) {
  if (e.structure?.kind !== on) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, i) => {
    if (!n || typeof n != "object") return null;
    const o = n.source, s = n.target;
    if (!o?.nodeId || !s?.nodeId) return null;
    const a = Array.isArray(n.vertices) ? n.vertices.filter(ip) : [];
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
function sl(e, t) {
  const n = zs(e.cases);
  if (ep(e, t) && n.length > 0)
    return [...n.map((s) => ({ name: s, displayName: s })), { name: "Default", displayName: "Default" }];
  const i = [
    ...vi(t?.designFacets),
    ...vi(t?.ports),
    ...vi(t?.outputs)
  ];
  if (i.length > 0) return tp(i);
  const o = zs(e.outcomes);
  return o.length > 0 ? o.map((s) => ({ name: s, displayName: s })) : [{ name: "Done", displayName: "Done" }];
}
function Jf(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function Ti(e, t, n, i) {
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
function Qf(e, t, n) {
  const i = Ti(t.source, n, t.sourceHandle ?? "Done", void 0), o = Ti(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((s) => s.id !== t.id).concat(i, o);
}
function pn(e) {
  return Array.isArray(e) ? e.filter(Pi) : null;
}
function ep(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, i = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || i === "FlowSwitch";
}
function vi(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!Qe(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...vi(n.ports));
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
function tp(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function zs(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function Rt(e) {
  return typeof e == "string" && e.length > 0 ? e : void 0;
}
function np(e) {
  return JSON.parse(JSON.stringify(e));
}
function Ls(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e) {
    const o = t(i);
    o && n.set(o, [...n.get(o) ?? [], i]);
  }
  return n;
}
function al(e) {
  return [...e].sort((t, n) => Vs(n).localeCompare(Vs(t)))[0];
}
function Vs(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function ip(e) {
  return Qe(e) && typeof e.x == "number" && typeof e.y == "number";
}
function Qe(e) {
  return typeof e == "object" && e !== null;
}
function Pi(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function op(e) {
  return e.kind === Vn ? "sequence" : e.kind === on ? "flowchart" : "generic";
}
function rp(e) {
  return e.kind === Vn || e.kind === on, "Activities";
}
function sp(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function ap(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const $i = "workflow", cp = /* @__PURE__ */ new Set([Vn, on]);
function lp(e, t) {
  const n = e?.structure?.kind;
  if (!n) return !1;
  if (cp.has(n)) return !0;
  const i = t?.activityVersionId === e?.activityVersionId ? Xr(t) : null;
  return i?.kind === n && i.payload.supportsScopedVariables;
}
function cl(e) {
  const t = e?.structure?.payload?.variables;
  return Array.isArray(t) ? t.filter(Di) : [];
}
function up(e, t) {
  return e.structure ? {
    ...e,
    structure: {
      ...e.structure,
      payload: { ...e.structure.payload, variables: t }
    }
  } : e;
}
function dp(e, t) {
  return {
    referenceKey: e,
    declaringScopeId: t && t !== $i ? t : $i
  };
}
function ll(e) {
  if (e && typeof e == "object") {
    const t = e;
    return typeof t.referenceKey == "string" && t.referenceKey ? { referenceKey: t.referenceKey, declaringScopeId: typeof t.declaringScopeId == "string" ? t.declaringScopeId : null } : null;
  }
  if (typeof e == "string") {
    const t = e.trim();
    if (t.startsWith("{"))
      try {
        return ll(JSON.parse(t));
      } catch {
      }
    return t ? { referenceKey: t, declaringScopeId: null } : null;
  }
  return null;
}
function fp(e, t) {
  if (!e) return "";
  const n = [`workflow:${Os(e.variables)}`], i = (o) => {
    const s = De(o, t), a = s.flatMap((c) => c.activities.map((u) => u.nodeId));
    n.push(`${o.nodeId}:${Os(cl(o))}>${a.join(",")}`), s.forEach((c) => c.activities.forEach(i));
  };
  return e.rootActivity && i(e.rootActivity), n.join(";");
}
function Os(e) {
  return (e ?? []).map((t) => `${t.referenceKey}=${t.name}`).join(",");
}
function pp(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e ?? [])
    i.scopeId === t && n.set(i.referenceKey, `Shadows "${i.name}" declared in an outer scope.`);
  return n;
}
const be = "/_elsa/workflow-management", hp = "/publishing", bn = {
  activities: ["workflows", "activities"],
  activityAvailabilitySettings: ["workflows", "activity-availability", "settings"],
  activityAvailabilityDiagnostics: ["workflows", "activity-availability", "diagnostics"]
};
function gp(e) {
  return Wc({
    queryKey: bn.activityAvailabilitySettings,
    queryFn: () => Mp(e)
  });
}
function yp(e) {
  return Wc({
    queryKey: bn.activityAvailabilityDiagnostics,
    queryFn: () => fl(e)
  });
}
function mp(e) {
  const t = ef();
  return tf({
    mutationFn: (n) => Rp(e, n),
    onSuccess: () => {
      t.invalidateQueries({ queryKey: bn.activityAvailabilitySettings }), t.invalidateQueries({ queryKey: bn.activityAvailabilityDiagnostics }), t.invalidateQueries({ queryKey: bn.activities });
    }
  });
}
async function xp(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), i = t.search.trim();
  return i && n.set("search", i), e.http.getJson(`${be}/definitions?${n.toString()}`);
}
async function wp(e, t) {
  const n = await e.http.getJson(`${be}/definitions/${encodeURIComponent(t)}`);
  return n.draft ? { ...n, draft: { ...n.draft, state: Gi(n.draft.state) } } : n;
}
async function vp(e, t, n) {
  const i = await e.http.postJson(
    `${be}/design/scoped-variables/analyze`,
    { state: Ln(t), nodeId: n }
  );
  return {
    visibleVariables: Array.isArray(i?.visibleVariables) ? i.visibleVariables : [],
    shadowingWarnings: Array.isArray(i?.shadowingWarnings) ? i.shadowingWarnings : []
  };
}
const Qo = (e) => ({ visibleVariables: [], shadowingWarnings: [], status: e });
function bp(e, t, n, i) {
  const o = de(() => fp(t, i), [i, t]), [s, a] = B(() => Qo("loading"));
  return J(() => {
    if (!t) {
      a(Qo("unavailable"));
      return;
    }
    let c = !1;
    return a((u) => ({ ...u, status: "loading" })), vp(e, t, n).then(
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
async function Np(e, t) {
  const n = await e.http.getJson(`${be}/versions/${encodeURIComponent(t)}`);
  return { ...n, state: Gi(n.state) };
}
async function jp(e, t) {
  return e.http.postJson(`${be}/definitions`, t);
}
async function Sp(e, t) {
  await e.http.deleteJson(`${be}/definitions/${encodeURIComponent(t)}`);
}
async function Cp(e, t) {
  await e.http.postJson(`${be}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function Ep(e, t) {
  await e.http.deleteJson(`${be}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function Ip(e, t, n) {
  return e.http.requestJson(
    `${be}/definitions/${encodeURIComponent(t)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(n)
    }
  );
}
async function kp(e, t) {
  const n = await e.http.putJson(
    `${be}/drafts/${encodeURIComponent(t.id)}`,
    { state: Ln(t.state), layout: t.layout }
  );
  return { ...n, state: Gi(n.state) };
}
async function Ap(e, t) {
  return e.http.postJson(`${be}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function _p(e, t) {
  return e.http.postJson(`${be}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function Dp(e, t) {
  const n = { ...t, state: Ln(t.state) };
  try {
    return await e.http.postJson(`${hp}/workflows/drafts/test-runs`, n);
  } catch (i) {
    const o = Fp(i);
    if (o) return o;
    throw i;
  }
}
async function ul(e, t) {
  return e.http.postJson(`${be}/executables/${encodeURIComponent(t)}/run`, {});
}
async function dl(e) {
  const t = [`${be}/executables`, "/_demo/workflows/executables"], n = [];
  for (const i of t)
    try {
      const o = await e.http.getJson(i);
      return Tp(o);
    } catch (o) {
      n.push(o);
    }
  if (n.length > 0 && n.every(Hs)) return [];
  throw n.find((i) => !Hs(i)) ?? n[n.length - 1] ?? new Error("Workflow executables could not be loaded.");
}
function Tp(e) {
  return Array.isArray(e) ? e : e.executables ?? [];
}
function Hs(e) {
  if (!(e instanceof Error)) return !1;
  const t = e.message.toLowerCase();
  return /\b404\b/.test(t) || t.includes("not found");
}
async function Pp(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.runKind && n.set("runKind", t.runKind), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const i = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${i ? `?${i}` : ""}`);
}
async function $p(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function Yr(e) {
  return e.http.getJson(`${be}/activities`);
}
async function Mp(e) {
  return e.http.getJson(`${be}/activities/availability/settings`);
}
async function Rp(e, t) {
  return e.http.putJson(`${be}/activities/availability/settings`, t);
}
async function fl(e) {
  return e.http.getJson(`${be}/activities/availability/diagnostics`);
}
async function zp(e) {
  const t = await Qi(e, [
    `${be}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? Ws(t) : Ws(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function Lp(e) {
  const t = await Qi(e, [
    `${be}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : bi;
}
async function Vp(e) {
  const t = await Qi(e, [
    `${be}/descriptors/variables`,
    "/descriptors/variables"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((i) => Op(i));
}
function Op(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = typeof t.alias == "string" && t.alias.length > 0, i = typeof t.typeName == "string" && t.typeName.length > 0;
  return n || i;
}
async function Hp(e) {
  const t = await Qi(e, [
    `${be}/descriptors/storage-drivers`,
    "/descriptors/storage-drivers"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((i) => Wp(i));
}
function Wp(e) {
  return !!e && typeof e == "object" && typeof e.typeName == "string" && e.typeName.length > 0;
}
async function Qi(e, t) {
  let n;
  for (const i of t)
    try {
      return await e.http.getJson(i);
    } catch (o) {
      n = o;
    }
  throw n;
}
function Ws(e) {
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
function Fp(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = Fs(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return Fs(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function Fs(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const bi = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
], Bp = [
  "Available",
  "BlockedByHostBaseline",
  "HiddenByManagementSettings",
  "RemovedFromCatalog",
  "UnresolvedReference"
], Kp = {
  Available: "Available",
  BlockedByHostBaseline: "Host blocked",
  HiddenByManagementSettings: "Management hidden",
  RemovedFromCatalog: "Removed",
  UnresolvedReference: "Unresolved"
};
function xt(e) {
  return typeof e == "string" ? e : typeof e == "number" ? Bp[e] ?? "Available" : "Available";
}
function Mi(e) {
  const t = xt(e);
  return Kp[t] ?? t;
}
function Xp(e) {
  return xt(e).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function pl(e) {
  return xt(e) !== "Available";
}
function qp(e) {
  return e === "Only" || e === 1 ? "Only" : "AllExcept";
}
function Yp(e) {
  return e === "Only" ? 1 : 0;
}
function Bs(e) {
  const t = e?.rules;
  return {
    mode: qp(e?.mode),
    activityTypes: t?.activityTypes ?? [],
    sets: t?.sets ?? []
  };
}
function Up(e) {
  return e.referenceKind === 0 || e.referenceKind === "ActivityType";
}
function Zp(e) {
  return [...e?.items ?? []].filter(Up).filter((t) => t.activityTypeKey && t.activityDefinitionId).sort((t, n) => Ri(t).localeCompare(Ri(n)));
}
function Gp(e) {
  return [...e?.items ?? []].filter((t) => {
    const n = xt(t.state);
    return n === "RemovedFromCatalog" || n === "UnresolvedReference";
  }).sort((t, n) => (t.referenceName ?? "").localeCompare(n.referenceName ?? ""));
}
function Ks(e, t) {
  return e.includes(t) ? e.filter((n) => n !== t) : [...e, t].sort((n, i) => n.localeCompare(i));
}
function Ri(e) {
  const t = e?.displayName?.trim();
  if (t && t.toLowerCase() !== "activity") return t;
  const n = Qp(e?.activityTypeKey);
  return Jp(n) || t || e?.activityTypeKey || "Activity";
}
function Xs(e) {
  const t = hl(e?.activityTypeKey);
  if (t.length === 0) return "";
  const n = t[t.length - 1], i = t[t.length - 2];
  return n === "Activity" && i ? `${i}.${n}` : n;
}
function qs(e) {
  const t = e.description?.trim();
  if (t) return t;
  const n = e.reason?.trim();
  return !n || !pl(e.state) ? "" : n;
}
function Jp(e) {
  return e.replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/\bHttp\b/g, "HTTP").replace(/\bJson\b/g, "JSON").replace(/\bJava Script\b/g, "JavaScript").replace(/\bUrl\b/g, "URL").replace(/\bXml\b/g, "XML").trim();
}
function Qp(e) {
  const t = hl(e), n = t[t.length - 1] ?? "";
  return n === "Activity" && t.length > 1 ? t[t.length - 2] : n;
}
function hl(e) {
  return e?.split(".").filter(Boolean) ?? [];
}
function eh(e, t) {
  const n = new Set(e.filter((i) => !!i));
  return (t?.items ?? []).find((i) => pl(i.state) ? [i.activityDefinitionId, i.activityTypeKey, i.referenceName].some((o) => o && n.has(o)) : !1) ?? null;
}
function th({ context: e }) {
  const t = gp(e), n = yp(e), i = mp(e), o = t.data ?? null, s = n.data ?? null, a = t.isLoading || n.isLoading, c = i.isPending, [u, l] = B(() => Bs(o)), [d, f] = B(""), [p, h] = B(null);
  J(() => {
    l(Bs(o));
  }, [o]);
  const g = de(() => Zp(s), [s]), m = de(() => Gp(s), [s]), w = s?.sets ?? [], x = de(() => {
    const I = d.trim().toLowerCase();
    return I ? g.filter((M) => [
      Ri(M),
      Xs(M),
      qs(M),
      M.activityTypeKey,
      M.category
    ].some((C) => (C ?? "").toLowerCase().includes(I))) : g;
  }, [g, d]), j = new Set(u.activityTypes), y = new Set(u.sets), v = g.filter((I) => xt(I.state) === "BlockedByHostBaseline").length, N = g.filter((I) => xt(I.state) === "HiddenByManagementSettings").length, b = i.error ?? t.error ?? n.error, S = b instanceof Error ? b.message : b ? "Activity availability could not be loaded." : null, E = (I) => l((M) => ({ ...M, mode: I })), _ = (I) => l((M) => ({ ...M, activityTypes: Ks(M.activityTypes, I) })), R = (I) => l((M) => ({ ...M, sets: Ks(M.sets, I) })), D = () => {
    h(null), i.mutate(
      {
        scope: o?.scope ?? "host-default",
        mode: Yp(u.mode),
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
          /* @__PURE__ */ r.jsx(Fd, { size: 18 }),
          " Activity availability"
        ] }),
        /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Control which activities can be added to new workflows. Host baseline rules always take precedence." })
      ] }),
      /* @__PURE__ */ r.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ r.jsxs("button", { type: "button", className: "availability-save", onClick: D, disabled: a || c, children: [
        /* @__PURE__ */ r.jsx($c, { size: 15 }),
        c ? "Saving…" : "Save"
      ] }) })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "availability-body", children: [
      S && /* @__PURE__ */ r.jsx("div", { className: "availability-banner availability-banner-error", children: S }),
      p && !S && /* @__PURE__ */ r.jsx("div", { className: "availability-banner availability-banner-success", children: p }),
      /* @__PURE__ */ r.jsxs("div", { className: "availability-mode", role: "group", "aria-label": "Activity availability mode", children: [
        /* @__PURE__ */ r.jsxs("button", { type: "button", className: u.mode === "AllExcept" ? "active" : "", onClick: () => E("AllExcept"), disabled: a || c, children: [
          /* @__PURE__ */ r.jsx(ks, { size: 15 }),
          /* @__PURE__ */ r.jsxs("span", { children: [
            /* @__PURE__ */ r.jsx("strong", { children: "All except" }),
            /* @__PURE__ */ r.jsx("em", { children: "Show everything except the selected activities" })
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", className: u.mode === "Only" ? "active" : "", onClick: () => E("Only"), disabled: a || c, children: [
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
          N,
          " management hidden"
        ] }),
        /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx(_i, { size: 14 }),
          " ",
          m.length,
          " unresolved"
        ] })
      ] }),
      w.length > 0 && /* @__PURE__ */ r.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ r.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ r.jsx(Wr, { size: 14 }),
          " Sets"
        ] }),
        /* @__PURE__ */ r.jsx("div", { className: "availability-set-list", children: w.map((I) => /* @__PURE__ */ r.jsxs("label", { className: "availability-set-option", children: [
          /* @__PURE__ */ r.jsx("input", { type: "checkbox", checked: y.has(I.name), disabled: a || c, onChange: () => R(I.name) }),
          /* @__PURE__ */ r.jsx("span", { children: I.name }),
          /* @__PURE__ */ r.jsx("code", { children: (I.activityTypeKeys ?? []).length })
        ] }, I.name)) })
      ] }),
      /* @__PURE__ */ r.jsxs("section", { className: "availability-section availability-section-grow", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "availability-section-head", children: [
          /* @__PURE__ */ r.jsxs("h3", { className: "wf-section-label", children: [
            /* @__PURE__ */ r.jsx(Mc, { size: 14 }),
            " Activities"
          ] }),
          /* @__PURE__ */ r.jsxs("div", { className: "wf-search availability-search", children: [
            /* @__PURE__ */ r.jsx(Yi, { size: 14 }),
            /* @__PURE__ */ r.jsx("input", { type: "search", value: d, placeholder: "Filter activities…", onChange: (I) => f(I.target.value) })
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("div", { className: "availability-activity-list", children: [
          a && g.length === 0 && /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Loading availability…" }),
          !a && g.length === 0 && /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No availability diagnostics reported." }),
          !a && g.length > 0 && x.length === 0 && /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No activities match the filter." }),
          x.map((I) => {
            const C = xt(I.state) === "BlockedByHostBaseline", k = I.activityTypeKey ?? I.activityDefinitionId ?? "", A = Ri(I), T = Xs(I), $ = qs(I);
            return /* @__PURE__ */ r.jsxs("label", { className: `availability-activity-option ${C ? "disabled" : ""}`, children: [
              /* @__PURE__ */ r.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: j.has(k),
                  disabled: a || c || C,
                  onChange: () => _(k)
                }
              ),
              /* @__PURE__ */ r.jsxs("span", { className: "availability-activity-main", children: [
                /* @__PURE__ */ r.jsxs("span", { className: "availability-activity-title-line", children: [
                  /* @__PURE__ */ r.jsx("strong", { children: A }),
                  I.category && /* @__PURE__ */ r.jsx("span", { className: "availability-activity-category", children: I.category })
                ] }),
                $ && /* @__PURE__ */ r.jsx("span", { className: "availability-activity-description", children: $ }),
                T && /* @__PURE__ */ r.jsx("span", { className: "availability-activity-meta", title: I.activityTypeKey ?? void 0, children: /* @__PURE__ */ r.jsx("code", { children: T }) })
              ] }),
              /* @__PURE__ */ r.jsx("em", { className: `availability-state ${Xp(I.state)}`, children: Mi(I.state) })
            ] }, k);
          })
        ] })
      ] }),
      m.length > 0 && /* @__PURE__ */ r.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ r.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ r.jsx(_i, { size: 14 }),
          " Unresolved references"
        ] }),
        /* @__PURE__ */ r.jsx("div", { className: "availability-unresolved-list", children: m.map((I) => /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx("strong", { children: I.referenceName }),
          /* @__PURE__ */ r.jsx("em", { children: Mi(I.state) })
        ] }, `${I.layer}-${I.referenceKind}-${I.referenceName}`)) })
      ] })
    ] })
  ] });
}
function Se(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, i; n < e.length; n++)
      (i = Se(e[n])) !== "" && (t += (t && " ") + i);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var nh = { value: () => {
} };
function eo() {
  for (var e = 0, t = arguments.length, n = {}, i; e < t; ++e) {
    if (!(i = arguments[e] + "") || i in n || /[\s.]/.test(i)) throw new Error("illegal type: " + i);
    n[i] = [];
  }
  return new Ni(n);
}
function Ni(e) {
  this._ = e;
}
function ih(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var i = "", o = n.indexOf(".");
    if (o >= 0 && (i = n.slice(o + 1), n = n.slice(0, o)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: i };
  });
}
Ni.prototype = eo.prototype = {
  constructor: Ni,
  on: function(e, t) {
    var n = this._, i = ih(e + "", n), o, s = -1, a = i.length;
    if (arguments.length < 2) {
      for (; ++s < a; ) if ((o = (e = i[s]).type) && (o = oh(n[o], e.name))) return o;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++s < a; )
      if (o = (e = i[s]).type) n[o] = Ys(n[o], e.name, t);
      else if (t == null) for (o in n) n[o] = Ys(n[o], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new Ni(e);
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
function oh(e, t) {
  for (var n = 0, i = e.length, o; n < i; ++n)
    if ((o = e[n]).name === t)
      return o.value;
}
function Ys(e, t, n) {
  for (var i = 0, o = e.length; i < o; ++i)
    if (e[i].name === t) {
      e[i] = nh, e = e.slice(0, i).concat(e.slice(i + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var br = "http://www.w3.org/1999/xhtml";
const Us = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: br,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function to(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Us.hasOwnProperty(t) ? { space: Us[t], local: e } : e;
}
function rh(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === br && t.documentElement.namespaceURI === br ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function sh(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function gl(e) {
  var t = to(e);
  return (t.local ? sh : rh)(t);
}
function ah() {
}
function Ur(e) {
  return e == null ? ah : function() {
    return this.querySelector(e);
  };
}
function ch(e) {
  typeof e != "function" && (e = Ur(e));
  for (var t = this._groups, n = t.length, i = new Array(n), o = 0; o < n; ++o)
    for (var s = t[o], a = s.length, c = i[o] = new Array(a), u, l, d = 0; d < a; ++d)
      (u = s[d]) && (l = e.call(u, u.__data__, d, s)) && ("__data__" in u && (l.__data__ = u.__data__), c[d] = l);
  return new Ve(i, this._parents);
}
function lh(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function uh() {
  return [];
}
function yl(e) {
  return e == null ? uh : function() {
    return this.querySelectorAll(e);
  };
}
function dh(e) {
  return function() {
    return lh(e.apply(this, arguments));
  };
}
function fh(e) {
  typeof e == "function" ? e = dh(e) : e = yl(e);
  for (var t = this._groups, n = t.length, i = [], o = [], s = 0; s < n; ++s)
    for (var a = t[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && (i.push(e.call(u, u.__data__, l, a)), o.push(u));
  return new Ve(i, o);
}
function ml(e) {
  return function() {
    return this.matches(e);
  };
}
function xl(e) {
  return function(t) {
    return t.matches(e);
  };
}
var ph = Array.prototype.find;
function hh(e) {
  return function() {
    return ph.call(this.children, e);
  };
}
function gh() {
  return this.firstElementChild;
}
function yh(e) {
  return this.select(e == null ? gh : hh(typeof e == "function" ? e : xl(e)));
}
var mh = Array.prototype.filter;
function xh() {
  return Array.from(this.children);
}
function wh(e) {
  return function() {
    return mh.call(this.children, e);
  };
}
function vh(e) {
  return this.selectAll(e == null ? xh : wh(typeof e == "function" ? e : xl(e)));
}
function bh(e) {
  typeof e != "function" && (e = ml(e));
  for (var t = this._groups, n = t.length, i = new Array(n), o = 0; o < n; ++o)
    for (var s = t[o], a = s.length, c = i[o] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new Ve(i, this._parents);
}
function wl(e) {
  return new Array(e.length);
}
function Nh() {
  return new Ve(this._enter || this._groups.map(wl), this._parents);
}
function zi(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
zi.prototype = {
  constructor: zi,
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
function jh(e) {
  return function() {
    return e;
  };
}
function Sh(e, t, n, i, o, s) {
  for (var a = 0, c, u = t.length, l = s.length; a < l; ++a)
    (c = t[a]) ? (c.__data__ = s[a], i[a] = c) : n[a] = new zi(e, s[a]);
  for (; a < u; ++a)
    (c = t[a]) && (o[a] = c);
}
function Ch(e, t, n, i, o, s, a) {
  var c, u, l = /* @__PURE__ */ new Map(), d = t.length, f = s.length, p = new Array(d), h;
  for (c = 0; c < d; ++c)
    (u = t[c]) && (p[c] = h = a.call(u, u.__data__, c, t) + "", l.has(h) ? o[c] = u : l.set(h, u));
  for (c = 0; c < f; ++c)
    h = a.call(e, s[c], c, s) + "", (u = l.get(h)) ? (i[c] = u, u.__data__ = s[c], l.delete(h)) : n[c] = new zi(e, s[c]);
  for (c = 0; c < d; ++c)
    (u = t[c]) && l.get(p[c]) === u && (o[c] = u);
}
function Eh(e) {
  return e.__data__;
}
function Ih(e, t) {
  if (!arguments.length) return Array.from(this, Eh);
  var n = t ? Ch : Sh, i = this._parents, o = this._groups;
  typeof e != "function" && (e = jh(e));
  for (var s = o.length, a = new Array(s), c = new Array(s), u = new Array(s), l = 0; l < s; ++l) {
    var d = i[l], f = o[l], p = f.length, h = kh(e.call(d, d && d.__data__, l, i)), g = h.length, m = c[l] = new Array(g), w = a[l] = new Array(g), x = u[l] = new Array(p);
    n(d, f, m, w, x, h, t);
    for (var j = 0, y = 0, v, N; j < g; ++j)
      if (v = m[j]) {
        for (j >= y && (y = j + 1); !(N = w[y]) && ++y < g; ) ;
        v._next = N || null;
      }
  }
  return a = new Ve(a, i), a._enter = c, a._exit = u, a;
}
function kh(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function Ah() {
  return new Ve(this._exit || this._groups.map(wl), this._parents);
}
function _h(e, t, n) {
  var i = this.enter(), o = this, s = this.exit();
  return typeof e == "function" ? (i = e(i), i && (i = i.selection())) : i = i.append(e + ""), t != null && (o = t(o), o && (o = o.selection())), n == null ? s.remove() : n(s), i && o ? i.merge(o).order() : o;
}
function Dh(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, i = t._groups, o = n.length, s = i.length, a = Math.min(o, s), c = new Array(o), u = 0; u < a; ++u)
    for (var l = n[u], d = i[u], f = l.length, p = c[u] = new Array(f), h, g = 0; g < f; ++g)
      (h = l[g] || d[g]) && (p[g] = h);
  for (; u < o; ++u)
    c[u] = n[u];
  return new Ve(c, this._parents);
}
function Th() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var i = e[t], o = i.length - 1, s = i[o], a; --o >= 0; )
      (a = i[o]) && (s && a.compareDocumentPosition(s) ^ 4 && s.parentNode.insertBefore(a, s), s = a);
  return this;
}
function Ph(e) {
  e || (e = $h);
  function t(f, p) {
    return f && p ? e(f.__data__, p.__data__) : !f - !p;
  }
  for (var n = this._groups, i = n.length, o = new Array(i), s = 0; s < i; ++s) {
    for (var a = n[s], c = a.length, u = o[s] = new Array(c), l, d = 0; d < c; ++d)
      (l = a[d]) && (u[d] = l);
    u.sort(t);
  }
  return new Ve(o, this._parents).order();
}
function $h(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function Mh() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function Rh() {
  return Array.from(this);
}
function zh() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var i = e[t], o = 0, s = i.length; o < s; ++o) {
      var a = i[o];
      if (a) return a;
    }
  return null;
}
function Lh() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function Vh() {
  return !this.node();
}
function Oh(e) {
  for (var t = this._groups, n = 0, i = t.length; n < i; ++n)
    for (var o = t[n], s = 0, a = o.length, c; s < a; ++s)
      (c = o[s]) && e.call(c, c.__data__, s, o);
  return this;
}
function Hh(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Wh(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Fh(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function Bh(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function Kh(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function Xh(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function qh(e, t) {
  var n = to(e);
  if (arguments.length < 2) {
    var i = this.node();
    return n.local ? i.getAttributeNS(n.space, n.local) : i.getAttribute(n);
  }
  return this.each((t == null ? n.local ? Wh : Hh : typeof t == "function" ? n.local ? Xh : Kh : n.local ? Bh : Fh)(n, t));
}
function vl(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function Yh(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Uh(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function Zh(e, t, n) {
  return function() {
    var i = t.apply(this, arguments);
    i == null ? this.style.removeProperty(e) : this.style.setProperty(e, i, n);
  };
}
function Gh(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? Yh : typeof t == "function" ? Zh : Uh)(e, t, n ?? "")) : qt(this.node(), e);
}
function qt(e, t) {
  return e.style.getPropertyValue(t) || vl(e).getComputedStyle(e, null).getPropertyValue(t);
}
function Jh(e) {
  return function() {
    delete this[e];
  };
}
function Qh(e, t) {
  return function() {
    this[e] = t;
  };
}
function eg(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function tg(e, t) {
  return arguments.length > 1 ? this.each((t == null ? Jh : typeof t == "function" ? eg : Qh)(e, t)) : this.node()[e];
}
function bl(e) {
  return e.trim().split(/^|\s+/);
}
function Zr(e) {
  return e.classList || new Nl(e);
}
function Nl(e) {
  this._node = e, this._names = bl(e.getAttribute("class") || "");
}
Nl.prototype = {
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
function jl(e, t) {
  for (var n = Zr(e), i = -1, o = t.length; ++i < o; ) n.add(t[i]);
}
function Sl(e, t) {
  for (var n = Zr(e), i = -1, o = t.length; ++i < o; ) n.remove(t[i]);
}
function ng(e) {
  return function() {
    jl(this, e);
  };
}
function ig(e) {
  return function() {
    Sl(this, e);
  };
}
function og(e, t) {
  return function() {
    (t.apply(this, arguments) ? jl : Sl)(this, e);
  };
}
function rg(e, t) {
  var n = bl(e + "");
  if (arguments.length < 2) {
    for (var i = Zr(this.node()), o = -1, s = n.length; ++o < s; ) if (!i.contains(n[o])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? og : t ? ng : ig)(n, t));
}
function sg() {
  this.textContent = "";
}
function ag(e) {
  return function() {
    this.textContent = e;
  };
}
function cg(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function lg(e) {
  return arguments.length ? this.each(e == null ? sg : (typeof e == "function" ? cg : ag)(e)) : this.node().textContent;
}
function ug() {
  this.innerHTML = "";
}
function dg(e) {
  return function() {
    this.innerHTML = e;
  };
}
function fg(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function pg(e) {
  return arguments.length ? this.each(e == null ? ug : (typeof e == "function" ? fg : dg)(e)) : this.node().innerHTML;
}
function hg() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function gg() {
  return this.each(hg);
}
function yg() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function mg() {
  return this.each(yg);
}
function xg(e) {
  var t = typeof e == "function" ? e : gl(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function wg() {
  return null;
}
function vg(e, t) {
  var n = typeof e == "function" ? e : gl(e), i = t == null ? wg : typeof t == "function" ? t : Ur(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), i.apply(this, arguments) || null);
  });
}
function bg() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Ng() {
  return this.each(bg);
}
function jg() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Sg() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Cg(e) {
  return this.select(e ? Sg : jg);
}
function Eg(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function Ig(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function kg(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", i = t.indexOf(".");
    return i >= 0 && (n = t.slice(i + 1), t = t.slice(0, i)), { type: t, name: n };
  });
}
function Ag(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, i = -1, o = t.length, s; n < o; ++n)
        s = t[n], (!e.type || s.type === e.type) && s.name === e.name ? this.removeEventListener(s.type, s.listener, s.options) : t[++i] = s;
      ++i ? t.length = i : delete this.__on;
    }
  };
}
function _g(e, t, n) {
  return function() {
    var i = this.__on, o, s = Ig(t);
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
function Dg(e, t, n) {
  var i = kg(e + ""), o, s = i.length, a;
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
  for (c = t ? _g : Ag, o = 0; o < s; ++o) this.each(c(i[o], t, n));
  return this;
}
function Cl(e, t, n) {
  var i = vl(e), o = i.CustomEvent;
  typeof o == "function" ? o = new o(t, n) : (o = i.document.createEvent("Event"), n ? (o.initEvent(t, n.bubbles, n.cancelable), o.detail = n.detail) : o.initEvent(t, !1, !1)), e.dispatchEvent(o);
}
function Tg(e, t) {
  return function() {
    return Cl(this, e, t);
  };
}
function Pg(e, t) {
  return function() {
    return Cl(this, e, t.apply(this, arguments));
  };
}
function $g(e, t) {
  return this.each((typeof t == "function" ? Pg : Tg)(e, t));
}
function* Mg() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var i = e[t], o = 0, s = i.length, a; o < s; ++o)
      (a = i[o]) && (yield a);
}
var El = [null];
function Ve(e, t) {
  this._groups = e, this._parents = t;
}
function On() {
  return new Ve([[document.documentElement]], El);
}
function Rg() {
  return this;
}
Ve.prototype = On.prototype = {
  constructor: Ve,
  select: ch,
  selectAll: fh,
  selectChild: yh,
  selectChildren: vh,
  filter: bh,
  data: Ih,
  enter: Nh,
  exit: Ah,
  join: _h,
  merge: Dh,
  selection: Rg,
  order: Th,
  sort: Ph,
  call: Mh,
  nodes: Rh,
  node: zh,
  size: Lh,
  empty: Vh,
  each: Oh,
  attr: qh,
  style: Gh,
  property: tg,
  classed: rg,
  text: lg,
  html: pg,
  raise: gg,
  lower: mg,
  append: xg,
  insert: vg,
  remove: Ng,
  clone: Cg,
  datum: Eg,
  on: Dg,
  dispatch: $g,
  [Symbol.iterator]: Mg
};
function Re(e) {
  return typeof e == "string" ? new Ve([[document.querySelector(e)]], [document.documentElement]) : new Ve([[e]], El);
}
function zg(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Fe(e, t) {
  if (e = zg(e), t === void 0 && (t = e.currentTarget), t) {
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
const Lg = { passive: !1 }, kn = { capture: !0, passive: !1 };
function er(e) {
  e.stopImmediatePropagation();
}
function Ht(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Il(e) {
  var t = e.document.documentElement, n = Re(e).on("dragstart.drag", Ht, kn);
  "onselectstart" in t ? n.on("selectstart.drag", Ht, kn) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function kl(e, t) {
  var n = e.document.documentElement, i = Re(e).on("dragstart.drag", null);
  t && (i.on("click.drag", Ht, kn), setTimeout(function() {
    i.on("click.drag", null);
  }, 0)), "onselectstart" in n ? i.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const ci = (e) => () => e;
function Nr(e, {
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
Nr.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function Vg(e) {
  return !e.ctrlKey && !e.button;
}
function Og() {
  return this.parentNode;
}
function Hg(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function Wg() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Al() {
  var e = Vg, t = Og, n = Hg, i = Wg, o = {}, s = eo("start", "drag", "end"), a = 0, c, u, l, d, f = 0;
  function p(v) {
    v.on("mousedown.drag", h).filter(i).on("touchstart.drag", w).on("touchmove.drag", x, Lg).on("touchend.drag touchcancel.drag", j).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function h(v, N) {
    if (!(d || !e.call(this, v, N))) {
      var b = y(this, t.call(this, v, N), v, N, "mouse");
      b && (Re(v.view).on("mousemove.drag", g, kn).on("mouseup.drag", m, kn), Il(v.view), er(v), l = !1, c = v.clientX, u = v.clientY, b("start", v));
    }
  }
  function g(v) {
    if (Ht(v), !l) {
      var N = v.clientX - c, b = v.clientY - u;
      l = N * N + b * b > f;
    }
    o.mouse("drag", v);
  }
  function m(v) {
    Re(v.view).on("mousemove.drag mouseup.drag", null), kl(v.view, l), Ht(v), o.mouse("end", v);
  }
  function w(v, N) {
    if (e.call(this, v, N)) {
      var b = v.changedTouches, S = t.call(this, v, N), E = b.length, _, R;
      for (_ = 0; _ < E; ++_)
        (R = y(this, S, v, N, b[_].identifier, b[_])) && (er(v), R("start", v, b[_]));
    }
  }
  function x(v) {
    var N = v.changedTouches, b = N.length, S, E;
    for (S = 0; S < b; ++S)
      (E = o[N[S].identifier]) && (Ht(v), E("drag", v, N[S]));
  }
  function j(v) {
    var N = v.changedTouches, b = N.length, S, E;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), S = 0; S < b; ++S)
      (E = o[N[S].identifier]) && (er(v), E("end", v, N[S]));
  }
  function y(v, N, b, S, E, _) {
    var R = s.copy(), D = Fe(_ || b, N), I, M, C;
    if ((C = n.call(v, new Nr("beforestart", {
      sourceEvent: b,
      target: p,
      identifier: E,
      active: a,
      x: D[0],
      y: D[1],
      dx: 0,
      dy: 0,
      dispatch: R
    }), S)) != null)
      return I = C.x - D[0] || 0, M = C.y - D[1] || 0, function k(A, T, $) {
        var P = D, F;
        switch (A) {
          case "start":
            o[E] = k, F = a++;
            break;
          case "end":
            delete o[E], --a;
          // falls through
          case "drag":
            D = Fe($ || T, N), F = a;
            break;
        }
        R.call(
          A,
          v,
          new Nr(A, {
            sourceEvent: T,
            subject: C,
            target: p,
            identifier: E,
            active: F,
            x: D[0] + I,
            y: D[1] + M,
            dx: D[0] - P[0],
            dy: D[1] - P[1],
            dispatch: R
          }),
          S
        );
      };
  }
  return p.filter = function(v) {
    return arguments.length ? (e = typeof v == "function" ? v : ci(!!v), p) : e;
  }, p.container = function(v) {
    return arguments.length ? (t = typeof v == "function" ? v : ci(v), p) : t;
  }, p.subject = function(v) {
    return arguments.length ? (n = typeof v == "function" ? v : ci(v), p) : n;
  }, p.touchable = function(v) {
    return arguments.length ? (i = typeof v == "function" ? v : ci(!!v), p) : i;
  }, p.on = function() {
    var v = s.on.apply(s, arguments);
    return v === s ? p : v;
  }, p.clickDistance = function(v) {
    return arguments.length ? (f = (v = +v) * v, p) : Math.sqrt(f);
  }, p;
}
function Gr(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function _l(e, t) {
  var n = Object.create(e.prototype);
  for (var i in t) n[i] = t[i];
  return n;
}
function Hn() {
}
var An = 0.7, Li = 1 / An, Wt = "\\s*([+-]?\\d+)\\s*", _n = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", Ze = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", Fg = /^#([0-9a-f]{3,8})$/, Bg = new RegExp(`^rgb\\(${Wt},${Wt},${Wt}\\)$`), Kg = new RegExp(`^rgb\\(${Ze},${Ze},${Ze}\\)$`), Xg = new RegExp(`^rgba\\(${Wt},${Wt},${Wt},${_n}\\)$`), qg = new RegExp(`^rgba\\(${Ze},${Ze},${Ze},${_n}\\)$`), Yg = new RegExp(`^hsl\\(${_n},${Ze},${Ze}\\)$`), Ug = new RegExp(`^hsla\\(${_n},${Ze},${Ze},${_n}\\)$`), Zs = {
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
Gr(Hn, bt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Gs,
  // Deprecated! Use color.formatHex.
  formatHex: Gs,
  formatHex8: Zg,
  formatHsl: Gg,
  formatRgb: Js,
  toString: Js
});
function Gs() {
  return this.rgb().formatHex();
}
function Zg() {
  return this.rgb().formatHex8();
}
function Gg() {
  return Dl(this).formatHsl();
}
function Js() {
  return this.rgb().formatRgb();
}
function bt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = Fg.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? Qs(t) : n === 3 ? new $e(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? li(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? li(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = Bg.exec(e)) ? new $e(t[1], t[2], t[3], 1) : (t = Kg.exec(e)) ? new $e(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = Xg.exec(e)) ? li(t[1], t[2], t[3], t[4]) : (t = qg.exec(e)) ? li(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = Yg.exec(e)) ? na(t[1], t[2] / 100, t[3] / 100, 1) : (t = Ug.exec(e)) ? na(t[1], t[2] / 100, t[3] / 100, t[4]) : Zs.hasOwnProperty(e) ? Qs(Zs[e]) : e === "transparent" ? new $e(NaN, NaN, NaN, 0) : null;
}
function Qs(e) {
  return new $e(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function li(e, t, n, i) {
  return i <= 0 && (e = t = n = NaN), new $e(e, t, n, i);
}
function Jg(e) {
  return e instanceof Hn || (e = bt(e)), e ? (e = e.rgb(), new $e(e.r, e.g, e.b, e.opacity)) : new $e();
}
function jr(e, t, n, i) {
  return arguments.length === 1 ? Jg(e) : new $e(e, t, n, i ?? 1);
}
function $e(e, t, n, i) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +i;
}
Gr($e, jr, _l(Hn, {
  brighter(e) {
    return e = e == null ? Li : Math.pow(Li, e), new $e(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? An : Math.pow(An, e), new $e(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new $e(wt(this.r), wt(this.g), wt(this.b), Vi(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: ea,
  // Deprecated! Use color.formatHex.
  formatHex: ea,
  formatHex8: Qg,
  formatRgb: ta,
  toString: ta
}));
function ea() {
  return `#${mt(this.r)}${mt(this.g)}${mt(this.b)}`;
}
function Qg() {
  return `#${mt(this.r)}${mt(this.g)}${mt(this.b)}${mt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function ta() {
  const e = Vi(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${wt(this.r)}, ${wt(this.g)}, ${wt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Vi(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function wt(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function mt(e) {
  return e = wt(e), (e < 16 ? "0" : "") + e.toString(16);
}
function na(e, t, n, i) {
  return i <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Be(e, t, n, i);
}
function Dl(e) {
  if (e instanceof Be) return new Be(e.h, e.s, e.l, e.opacity);
  if (e instanceof Hn || (e = bt(e)), !e) return new Be();
  if (e instanceof Be) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, i = e.b / 255, o = Math.min(t, n, i), s = Math.max(t, n, i), a = NaN, c = s - o, u = (s + o) / 2;
  return c ? (t === s ? a = (n - i) / c + (n < i) * 6 : n === s ? a = (i - t) / c + 2 : a = (t - n) / c + 4, c /= u < 0.5 ? s + o : 2 - s - o, a *= 60) : c = u > 0 && u < 1 ? 0 : a, new Be(a, c, u, e.opacity);
}
function ey(e, t, n, i) {
  return arguments.length === 1 ? Dl(e) : new Be(e, t, n, i ?? 1);
}
function Be(e, t, n, i) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +i;
}
Gr(Be, ey, _l(Hn, {
  brighter(e) {
    return e = e == null ? Li : Math.pow(Li, e), new Be(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? An : Math.pow(An, e), new Be(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, i = n + (n < 0.5 ? n : 1 - n) * t, o = 2 * n - i;
    return new $e(
      tr(e >= 240 ? e - 240 : e + 120, o, i),
      tr(e, o, i),
      tr(e < 120 ? e + 240 : e - 120, o, i),
      this.opacity
    );
  },
  clamp() {
    return new Be(ia(this.h), ui(this.s), ui(this.l), Vi(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Vi(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${ia(this.h)}, ${ui(this.s) * 100}%, ${ui(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function ia(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function ui(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function tr(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const Jr = (e) => () => e;
function ty(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function ny(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(i) {
    return Math.pow(e + i * t, n);
  };
}
function iy(e) {
  return (e = +e) == 1 ? Tl : function(t, n) {
    return n - t ? ny(t, n, e) : Jr(isNaN(t) ? n : t);
  };
}
function Tl(e, t) {
  var n = t - e;
  return n ? ty(e, n) : Jr(isNaN(e) ? t : e);
}
const Oi = (function e(t) {
  var n = iy(t);
  function i(o, s) {
    var a = n((o = jr(o)).r, (s = jr(s)).r), c = n(o.g, s.g), u = n(o.b, s.b), l = Tl(o.opacity, s.opacity);
    return function(d) {
      return o.r = a(d), o.g = c(d), o.b = u(d), o.opacity = l(d), o + "";
    };
  }
  return i.gamma = e, i;
})(1);
function oy(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, i = t.slice(), o;
  return function(s) {
    for (o = 0; o < n; ++o) i[o] = e[o] * (1 - s) + t[o] * s;
    return i;
  };
}
function ry(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function sy(e, t) {
  var n = t ? t.length : 0, i = e ? Math.min(n, e.length) : 0, o = new Array(i), s = new Array(n), a;
  for (a = 0; a < i; ++a) o[a] = Nn(e[a], t[a]);
  for (; a < n; ++a) s[a] = t[a];
  return function(c) {
    for (a = 0; a < i; ++a) s[a] = o[a](c);
    return s;
  };
}
function ay(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(i) {
    return n.setTime(e * (1 - i) + t * i), n;
  };
}
function Ue(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function cy(e, t) {
  var n = {}, i = {}, o;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (o in t)
    o in e ? n[o] = Nn(e[o], t[o]) : i[o] = t[o];
  return function(s) {
    for (o in n) i[o] = n[o](s);
    return i;
  };
}
var Sr = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, nr = new RegExp(Sr.source, "g");
function ly(e) {
  return function() {
    return e;
  };
}
function uy(e) {
  return function(t) {
    return e(t) + "";
  };
}
function Pl(e, t) {
  var n = Sr.lastIndex = nr.lastIndex = 0, i, o, s, a = -1, c = [], u = [];
  for (e = e + "", t = t + ""; (i = Sr.exec(e)) && (o = nr.exec(t)); )
    (s = o.index) > n && (s = t.slice(n, s), c[a] ? c[a] += s : c[++a] = s), (i = i[0]) === (o = o[0]) ? c[a] ? c[a] += o : c[++a] = o : (c[++a] = null, u.push({ i: a, x: Ue(i, o) })), n = nr.lastIndex;
  return n < t.length && (s = t.slice(n), c[a] ? c[a] += s : c[++a] = s), c.length < 2 ? u[0] ? uy(u[0].x) : ly(t) : (t = u.length, function(l) {
    for (var d = 0, f; d < t; ++d) c[(f = u[d]).i] = f.x(l);
    return c.join("");
  });
}
function Nn(e, t) {
  var n = typeof t, i;
  return t == null || n === "boolean" ? Jr(t) : (n === "number" ? Ue : n === "string" ? (i = bt(t)) ? (t = i, Oi) : Pl : t instanceof bt ? Oi : t instanceof Date ? ay : ry(t) ? oy : Array.isArray(t) ? sy : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? cy : Ue)(e, t);
}
var oa = 180 / Math.PI, Cr = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function $l(e, t, n, i, o, s) {
  var a, c, u;
  return (a = Math.sqrt(e * e + t * t)) && (e /= a, t /= a), (u = e * n + t * i) && (n -= e * u, i -= t * u), (c = Math.sqrt(n * n + i * i)) && (n /= c, i /= c, u /= c), e * i < t * n && (e = -e, t = -t, u = -u, a = -a), {
    translateX: o,
    translateY: s,
    rotate: Math.atan2(t, e) * oa,
    skewX: Math.atan(u) * oa,
    scaleX: a,
    scaleY: c
  };
}
var di;
function dy(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Cr : $l(t.a, t.b, t.c, t.d, t.e, t.f);
}
function fy(e) {
  return e == null || (di || (di = document.createElementNS("http://www.w3.org/2000/svg", "g")), di.setAttribute("transform", e), !(e = di.transform.baseVal.consolidate())) ? Cr : (e = e.matrix, $l(e.a, e.b, e.c, e.d, e.e, e.f));
}
function Ml(e, t, n, i) {
  function o(l) {
    return l.length ? l.pop() + " " : "";
  }
  function s(l, d, f, p, h, g) {
    if (l !== f || d !== p) {
      var m = h.push("translate(", null, t, null, n);
      g.push({ i: m - 4, x: Ue(l, f) }, { i: m - 2, x: Ue(d, p) });
    } else (f || p) && h.push("translate(" + f + t + p + n);
  }
  function a(l, d, f, p) {
    l !== d ? (l - d > 180 ? d += 360 : d - l > 180 && (l += 360), p.push({ i: f.push(o(f) + "rotate(", null, i) - 2, x: Ue(l, d) })) : d && f.push(o(f) + "rotate(" + d + i);
  }
  function c(l, d, f, p) {
    l !== d ? p.push({ i: f.push(o(f) + "skewX(", null, i) - 2, x: Ue(l, d) }) : d && f.push(o(f) + "skewX(" + d + i);
  }
  function u(l, d, f, p, h, g) {
    if (l !== f || d !== p) {
      var m = h.push(o(h) + "scale(", null, ",", null, ")");
      g.push({ i: m - 4, x: Ue(l, f) }, { i: m - 2, x: Ue(d, p) });
    } else (f !== 1 || p !== 1) && h.push(o(h) + "scale(" + f + "," + p + ")");
  }
  return function(l, d) {
    var f = [], p = [];
    return l = e(l), d = e(d), s(l.translateX, l.translateY, d.translateX, d.translateY, f, p), a(l.rotate, d.rotate, f, p), c(l.skewX, d.skewX, f, p), u(l.scaleX, l.scaleY, d.scaleX, d.scaleY, f, p), l = d = null, function(h) {
      for (var g = -1, m = p.length, w; ++g < m; ) f[(w = p[g]).i] = w.x(h);
      return f.join("");
    };
  };
}
var py = Ml(dy, "px, ", "px)", "deg)"), hy = Ml(fy, ", ", ")", ")"), gy = 1e-12;
function ra(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function yy(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function my(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const ji = (function e(t, n, i) {
  function o(s, a) {
    var c = s[0], u = s[1], l = s[2], d = a[0], f = a[1], p = a[2], h = d - c, g = f - u, m = h * h + g * g, w, x;
    if (m < gy)
      x = Math.log(p / l) / t, w = function(S) {
        return [
          c + S * h,
          u + S * g,
          l * Math.exp(t * S * x)
        ];
      };
    else {
      var j = Math.sqrt(m), y = (p * p - l * l + i * m) / (2 * l * n * j), v = (p * p - l * l - i * m) / (2 * p * n * j), N = Math.log(Math.sqrt(y * y + 1) - y), b = Math.log(Math.sqrt(v * v + 1) - v);
      x = (b - N) / t, w = function(S) {
        var E = S * x, _ = ra(N), R = l / (n * j) * (_ * my(t * E + N) - yy(N));
        return [
          c + R * h,
          u + R * g,
          l * _ / ra(t * E + N)
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
var Yt = 0, hn = 0, un = 0, Rl = 1e3, Hi, gn, Wi = 0, Nt = 0, no = 0, Dn = typeof performance == "object" && performance.now ? performance : Date, zl = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function Qr() {
  return Nt || (zl(xy), Nt = Dn.now() + no);
}
function xy() {
  Nt = 0;
}
function Fi() {
  this._call = this._time = this._next = null;
}
Fi.prototype = Ll.prototype = {
  constructor: Fi,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? Qr() : +n) + (t == null ? 0 : +t), !this._next && gn !== this && (gn ? gn._next = this : Hi = this, gn = this), this._call = e, this._time = n, Er();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Er());
  }
};
function Ll(e, t, n) {
  var i = new Fi();
  return i.restart(e, t, n), i;
}
function wy() {
  Qr(), ++Yt;
  for (var e = Hi, t; e; )
    (t = Nt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --Yt;
}
function sa() {
  Nt = (Wi = Dn.now()) + no, Yt = hn = 0;
  try {
    wy();
  } finally {
    Yt = 0, by(), Nt = 0;
  }
}
function vy() {
  var e = Dn.now(), t = e - Wi;
  t > Rl && (no -= t, Wi = e);
}
function by() {
  for (var e, t = Hi, n, i = 1 / 0; t; )
    t._call ? (i > t._time && (i = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Hi = n);
  gn = e, Er(i);
}
function Er(e) {
  if (!Yt) {
    hn && (hn = clearTimeout(hn));
    var t = e - Nt;
    t > 24 ? (e < 1 / 0 && (hn = setTimeout(sa, e - Dn.now() - no)), un && (un = clearInterval(un))) : (un || (Wi = Dn.now(), un = setInterval(vy, Rl)), Yt = 1, zl(sa));
  }
}
function aa(e, t, n) {
  var i = new Fi();
  return t = t == null ? 0 : +t, i.restart((o) => {
    i.stop(), e(o + t);
  }, t, n), i;
}
var Ny = eo("start", "end", "cancel", "interrupt"), jy = [], Vl = 0, ca = 1, Ir = 2, Si = 3, la = 4, kr = 5, Ci = 6;
function io(e, t, n, i, o, s) {
  var a = e.__transition;
  if (!a) e.__transition = {};
  else if (n in a) return;
  Sy(e, n, {
    name: t,
    index: i,
    // For context during callback.
    group: o,
    // For context during callback.
    on: Ny,
    tween: jy,
    time: s.time,
    delay: s.delay,
    duration: s.duration,
    ease: s.ease,
    timer: null,
    state: Vl
  });
}
function es(e, t) {
  var n = qe(e, t);
  if (n.state > Vl) throw new Error("too late; already scheduled");
  return n;
}
function et(e, t) {
  var n = qe(e, t);
  if (n.state > Si) throw new Error("too late; already running");
  return n;
}
function qe(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function Sy(e, t, n) {
  var i = e.__transition, o;
  i[t] = n, n.timer = Ll(s, 0, n.time);
  function s(l) {
    n.state = ca, n.timer.restart(a, n.delay, n.time), n.delay <= l && a(l - n.delay);
  }
  function a(l) {
    var d, f, p, h;
    if (n.state !== ca) return u();
    for (d in i)
      if (h = i[d], h.name === n.name) {
        if (h.state === Si) return aa(a);
        h.state === la ? (h.state = Ci, h.timer.stop(), h.on.call("interrupt", e, e.__data__, h.index, h.group), delete i[d]) : +d < t && (h.state = Ci, h.timer.stop(), h.on.call("cancel", e, e.__data__, h.index, h.group), delete i[d]);
      }
    if (aa(function() {
      n.state === Si && (n.state = la, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = Ir, n.on.call("start", e, e.__data__, n.index, n.group), n.state === Ir) {
      for (n.state = Si, o = new Array(p = n.tween.length), d = 0, f = -1; d < p; ++d)
        (h = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (o[++f] = h);
      o.length = f + 1;
    }
  }
  function c(l) {
    for (var d = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(u), n.state = kr, 1), f = -1, p = o.length; ++f < p; )
      o[f].call(e, d);
    n.state === kr && (n.on.call("end", e, e.__data__, n.index, n.group), u());
  }
  function u() {
    n.state = Ci, n.timer.stop(), delete i[t];
    for (var l in i) return;
    delete e.__transition;
  }
}
function Ei(e, t) {
  var n = e.__transition, i, o, s = !0, a;
  if (n) {
    t = t == null ? null : t + "";
    for (a in n) {
      if ((i = n[a]).name !== t) {
        s = !1;
        continue;
      }
      o = i.state > Ir && i.state < kr, i.state = Ci, i.timer.stop(), i.on.call(o ? "interrupt" : "cancel", e, e.__data__, i.index, i.group), delete n[a];
    }
    s && delete e.__transition;
  }
}
function Cy(e) {
  return this.each(function() {
    Ei(this, e);
  });
}
function Ey(e, t) {
  var n, i;
  return function() {
    var o = et(this, e), s = o.tween;
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
function Iy(e, t, n) {
  var i, o;
  if (typeof n != "function") throw new Error();
  return function() {
    var s = et(this, e), a = s.tween;
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
function ky(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var i = qe(this.node(), n).tween, o = 0, s = i.length, a; o < s; ++o)
      if ((a = i[o]).name === e)
        return a.value;
    return null;
  }
  return this.each((t == null ? Ey : Iy)(n, e, t));
}
function ts(e, t, n) {
  var i = e._id;
  return e.each(function() {
    var o = et(this, i);
    (o.value || (o.value = {}))[t] = n.apply(this, arguments);
  }), function(o) {
    return qe(o, i).value[t];
  };
}
function Ol(e, t) {
  var n;
  return (typeof t == "number" ? Ue : t instanceof bt ? Oi : (n = bt(t)) ? (t = n, Oi) : Pl)(e, t);
}
function Ay(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function _y(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Dy(e, t, n) {
  var i, o = n + "", s;
  return function() {
    var a = this.getAttribute(e);
    return a === o ? null : a === i ? s : s = t(i = a, n);
  };
}
function Ty(e, t, n) {
  var i, o = n + "", s;
  return function() {
    var a = this.getAttributeNS(e.space, e.local);
    return a === o ? null : a === i ? s : s = t(i = a, n);
  };
}
function Py(e, t, n) {
  var i, o, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttribute(e) : (a = this.getAttribute(e), u = c + "", a === u ? null : a === i && u === o ? s : (o = u, s = t(i = a, c)));
  };
}
function $y(e, t, n) {
  var i, o, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (a = this.getAttributeNS(e.space, e.local), u = c + "", a === u ? null : a === i && u === o ? s : (o = u, s = t(i = a, c)));
  };
}
function My(e, t) {
  var n = to(e), i = n === "transform" ? hy : Ol;
  return this.attrTween(e, typeof t == "function" ? (n.local ? $y : Py)(n, i, ts(this, "attr." + e, t)) : t == null ? (n.local ? _y : Ay)(n) : (n.local ? Ty : Dy)(n, i, t));
}
function Ry(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function zy(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function Ly(e, t) {
  var n, i;
  function o() {
    var s = t.apply(this, arguments);
    return s !== i && (n = (i = s) && zy(e, s)), n;
  }
  return o._value = t, o;
}
function Vy(e, t) {
  var n, i;
  function o() {
    var s = t.apply(this, arguments);
    return s !== i && (n = (i = s) && Ry(e, s)), n;
  }
  return o._value = t, o;
}
function Oy(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var i = to(e);
  return this.tween(n, (i.local ? Ly : Vy)(i, t));
}
function Hy(e, t) {
  return function() {
    es(this, e).delay = +t.apply(this, arguments);
  };
}
function Wy(e, t) {
  return t = +t, function() {
    es(this, e).delay = t;
  };
}
function Fy(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Hy : Wy)(t, e)) : qe(this.node(), t).delay;
}
function By(e, t) {
  return function() {
    et(this, e).duration = +t.apply(this, arguments);
  };
}
function Ky(e, t) {
  return t = +t, function() {
    et(this, e).duration = t;
  };
}
function Xy(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? By : Ky)(t, e)) : qe(this.node(), t).duration;
}
function qy(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    et(this, e).ease = t;
  };
}
function Yy(e) {
  var t = this._id;
  return arguments.length ? this.each(qy(t, e)) : qe(this.node(), t).ease;
}
function Uy(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    et(this, e).ease = n;
  };
}
function Zy(e) {
  if (typeof e != "function") throw new Error();
  return this.each(Uy(this._id, e));
}
function Gy(e) {
  typeof e != "function" && (e = ml(e));
  for (var t = this._groups, n = t.length, i = new Array(n), o = 0; o < n; ++o)
    for (var s = t[o], a = s.length, c = i[o] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new st(i, this._parents, this._name, this._id);
}
function Jy(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, i = t.length, o = n.length, s = Math.min(i, o), a = new Array(i), c = 0; c < s; ++c)
    for (var u = t[c], l = n[c], d = u.length, f = a[c] = new Array(d), p, h = 0; h < d; ++h)
      (p = u[h] || l[h]) && (f[h] = p);
  for (; c < i; ++c)
    a[c] = t[c];
  return new st(a, this._parents, this._name, this._id);
}
function Qy(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function em(e, t, n) {
  var i, o, s = Qy(t) ? es : et;
  return function() {
    var a = s(this, e), c = a.on;
    c !== i && (o = (i = c).copy()).on(t, n), a.on = o;
  };
}
function tm(e, t) {
  var n = this._id;
  return arguments.length < 2 ? qe(this.node(), n).on.on(e) : this.each(em(n, e, t));
}
function nm(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function im() {
  return this.on("end.remove", nm(this._id));
}
function om(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Ur(e));
  for (var i = this._groups, o = i.length, s = new Array(o), a = 0; a < o; ++a)
    for (var c = i[a], u = c.length, l = s[a] = new Array(u), d, f, p = 0; p < u; ++p)
      (d = c[p]) && (f = e.call(d, d.__data__, p, c)) && ("__data__" in d && (f.__data__ = d.__data__), l[p] = f, io(l[p], t, n, p, l, qe(d, n)));
  return new st(s, this._parents, t, n);
}
function rm(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = yl(e));
  for (var i = this._groups, o = i.length, s = [], a = [], c = 0; c < o; ++c)
    for (var u = i[c], l = u.length, d, f = 0; f < l; ++f)
      if (d = u[f]) {
        for (var p = e.call(d, d.__data__, f, u), h, g = qe(d, n), m = 0, w = p.length; m < w; ++m)
          (h = p[m]) && io(h, t, n, m, p, g);
        s.push(p), a.push(d);
      }
  return new st(s, a, t, n);
}
var sm = On.prototype.constructor;
function am() {
  return new sm(this._groups, this._parents);
}
function cm(e, t) {
  var n, i, o;
  return function() {
    var s = qt(this, e), a = (this.style.removeProperty(e), qt(this, e));
    return s === a ? null : s === n && a === i ? o : o = t(n = s, i = a);
  };
}
function Hl(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function lm(e, t, n) {
  var i, o = n + "", s;
  return function() {
    var a = qt(this, e);
    return a === o ? null : a === i ? s : s = t(i = a, n);
  };
}
function um(e, t, n) {
  var i, o, s;
  return function() {
    var a = qt(this, e), c = n(this), u = c + "";
    return c == null && (u = c = (this.style.removeProperty(e), qt(this, e))), a === u ? null : a === i && u === o ? s : (o = u, s = t(i = a, c));
  };
}
function dm(e, t) {
  var n, i, o, s = "style." + t, a = "end." + s, c;
  return function() {
    var u = et(this, e), l = u.on, d = u.value[s] == null ? c || (c = Hl(t)) : void 0;
    (l !== n || o !== d) && (i = (n = l).copy()).on(a, o = d), u.on = i;
  };
}
function fm(e, t, n) {
  var i = (e += "") == "transform" ? py : Ol;
  return t == null ? this.styleTween(e, cm(e, i)).on("end.style." + e, Hl(e)) : typeof t == "function" ? this.styleTween(e, um(e, i, ts(this, "style." + e, t))).each(dm(this._id, e)) : this.styleTween(e, lm(e, i, t), n).on("end.style." + e, null);
}
function pm(e, t, n) {
  return function(i) {
    this.style.setProperty(e, t.call(this, i), n);
  };
}
function hm(e, t, n) {
  var i, o;
  function s() {
    var a = t.apply(this, arguments);
    return a !== o && (i = (o = a) && pm(e, a, n)), i;
  }
  return s._value = t, s;
}
function gm(e, t, n) {
  var i = "style." + (e += "");
  if (arguments.length < 2) return (i = this.tween(i)) && i._value;
  if (t == null) return this.tween(i, null);
  if (typeof t != "function") throw new Error();
  return this.tween(i, hm(e, t, n ?? ""));
}
function ym(e) {
  return function() {
    this.textContent = e;
  };
}
function mm(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function xm(e) {
  return this.tween("text", typeof e == "function" ? mm(ts(this, "text", e)) : ym(e == null ? "" : e + ""));
}
function wm(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function vm(e) {
  var t, n;
  function i() {
    var o = e.apply(this, arguments);
    return o !== n && (t = (n = o) && wm(o)), t;
  }
  return i._value = e, i;
}
function bm(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, vm(e));
}
function Nm() {
  for (var e = this._name, t = this._id, n = Wl(), i = this._groups, o = i.length, s = 0; s < o; ++s)
    for (var a = i[s], c = a.length, u, l = 0; l < c; ++l)
      if (u = a[l]) {
        var d = qe(u, t);
        io(u, e, n, l, a, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new st(i, this._parents, e, n);
}
function jm() {
  var e, t, n = this, i = n._id, o = n.size();
  return new Promise(function(s, a) {
    var c = { value: a }, u = { value: function() {
      --o === 0 && s();
    } };
    n.each(function() {
      var l = et(this, i), d = l.on;
      d !== e && (t = (e = d).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(u)), l.on = t;
    }), o === 0 && s();
  });
}
var Sm = 0;
function st(e, t, n, i) {
  this._groups = e, this._parents = t, this._name = n, this._id = i;
}
function Wl() {
  return ++Sm;
}
var it = On.prototype;
st.prototype = {
  constructor: st,
  select: om,
  selectAll: rm,
  selectChild: it.selectChild,
  selectChildren: it.selectChildren,
  filter: Gy,
  merge: Jy,
  selection: am,
  transition: Nm,
  call: it.call,
  nodes: it.nodes,
  node: it.node,
  size: it.size,
  empty: it.empty,
  each: it.each,
  on: tm,
  attr: My,
  attrTween: Oy,
  style: fm,
  styleTween: gm,
  text: xm,
  textTween: bm,
  remove: im,
  tween: ky,
  delay: Fy,
  duration: Xy,
  ease: Yy,
  easeVarying: Zy,
  end: jm,
  [Symbol.iterator]: it[Symbol.iterator]
};
function Cm(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var Em = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: Cm
};
function Im(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function km(e) {
  var t, n;
  e instanceof st ? (t = e._id, e = e._name) : (t = Wl(), (n = Em).time = Qr(), e = e == null ? null : e + "");
  for (var i = this._groups, o = i.length, s = 0; s < o; ++s)
    for (var a = i[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && io(u, e, t, l, a, n || Im(u, t));
  return new st(i, this._parents, e, t);
}
On.prototype.interrupt = Cy;
On.prototype.transition = km;
const fi = (e) => () => e;
function Am(e, {
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
function ot(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
ot.prototype = {
  constructor: ot,
  scale: function(e) {
    return e === 1 ? this : new ot(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new ot(this.k, this.x + this.k * e, this.y + this.k * t);
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
var oo = new ot(1, 0, 0);
Fl.prototype = ot.prototype;
function Fl(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return oo;
  return e.__zoom;
}
function ir(e) {
  e.stopImmediatePropagation();
}
function dn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function _m(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function Dm() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function ua() {
  return this.__zoom || oo;
}
function Tm(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function Pm() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function $m(e, t, n) {
  var i = e.invertX(t[0][0]) - n[0][0], o = e.invertX(t[1][0]) - n[1][0], s = e.invertY(t[0][1]) - n[0][1], a = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    o > i ? (i + o) / 2 : Math.min(0, i) || Math.max(0, o),
    a > s ? (s + a) / 2 : Math.min(0, s) || Math.max(0, a)
  );
}
function Bl() {
  var e = _m, t = Dm, n = $m, i = Tm, o = Pm, s = [0, 1 / 0], a = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, u = ji, l = eo("start", "zoom", "end"), d, f, p, h = 500, g = 150, m = 0, w = 10;
  function x(C) {
    C.property("__zoom", ua).on("wheel.zoom", E, { passive: !1 }).on("mousedown.zoom", _).on("dblclick.zoom", R).filter(o).on("touchstart.zoom", D).on("touchmove.zoom", I).on("touchend.zoom touchcancel.zoom", M).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  x.transform = function(C, k, A, T) {
    var $ = C.selection ? C.selection() : C;
    $.property("__zoom", ua), C !== $ ? N(C, k, A, T) : $.interrupt().each(function() {
      b(this, arguments).event(T).start().zoom(null, typeof k == "function" ? k.apply(this, arguments) : k).end();
    });
  }, x.scaleBy = function(C, k, A, T) {
    x.scaleTo(C, function() {
      var $ = this.__zoom.k, P = typeof k == "function" ? k.apply(this, arguments) : k;
      return $ * P;
    }, A, T);
  }, x.scaleTo = function(C, k, A, T) {
    x.transform(C, function() {
      var $ = t.apply(this, arguments), P = this.__zoom, F = A == null ? v($) : typeof A == "function" ? A.apply(this, arguments) : A, W = P.invert(F), H = typeof k == "function" ? k.apply(this, arguments) : k;
      return n(y(j(P, H), F, W), $, a);
    }, A, T);
  }, x.translateBy = function(C, k, A, T) {
    x.transform(C, function() {
      return n(this.__zoom.translate(
        typeof k == "function" ? k.apply(this, arguments) : k,
        typeof A == "function" ? A.apply(this, arguments) : A
      ), t.apply(this, arguments), a);
    }, null, T);
  }, x.translateTo = function(C, k, A, T, $) {
    x.transform(C, function() {
      var P = t.apply(this, arguments), F = this.__zoom, W = T == null ? v(P) : typeof T == "function" ? T.apply(this, arguments) : T;
      return n(oo.translate(W[0], W[1]).scale(F.k).translate(
        typeof k == "function" ? -k.apply(this, arguments) : -k,
        typeof A == "function" ? -A.apply(this, arguments) : -A
      ), P, a);
    }, T, $);
  };
  function j(C, k) {
    return k = Math.max(s[0], Math.min(s[1], k)), k === C.k ? C : new ot(k, C.x, C.y);
  }
  function y(C, k, A) {
    var T = k[0] - A[0] * C.k, $ = k[1] - A[1] * C.k;
    return T === C.x && $ === C.y ? C : new ot(C.k, T, $);
  }
  function v(C) {
    return [(+C[0][0] + +C[1][0]) / 2, (+C[0][1] + +C[1][1]) / 2];
  }
  function N(C, k, A, T) {
    C.on("start.zoom", function() {
      b(this, arguments).event(T).start();
    }).on("interrupt.zoom end.zoom", function() {
      b(this, arguments).event(T).end();
    }).tween("zoom", function() {
      var $ = this, P = arguments, F = b($, P).event(T), W = t.apply($, P), H = A == null ? v(W) : typeof A == "function" ? A.apply($, P) : A, Y = Math.max(W[1][0] - W[0][0], W[1][1] - W[0][1]), Z = $.__zoom, ne = typeof k == "function" ? k.apply($, P) : k, le = u(Z.invert(H).concat(Y / Z.k), ne.invert(H).concat(Y / ne.k));
      return function(G) {
        if (G === 1) G = ne;
        else {
          var z = le(G), X = Y / z[2];
          G = new ot(X, H[0] - z[0] * X, H[1] - z[1] * X);
        }
        F.zoom(null, G);
      };
    });
  }
  function b(C, k, A) {
    return !A && C.__zooming || new S(C, k);
  }
  function S(C, k) {
    this.that = C, this.args = k, this.active = 0, this.sourceEvent = null, this.extent = t.apply(C, k), this.taps = 0;
  }
  S.prototype = {
    event: function(C) {
      return C && (this.sourceEvent = C), this;
    },
    start: function() {
      return ++this.active === 1 && (this.that.__zooming = this, this.emit("start")), this;
    },
    zoom: function(C, k) {
      return this.mouse && C !== "mouse" && (this.mouse[1] = k.invert(this.mouse[0])), this.touch0 && C !== "touch" && (this.touch0[1] = k.invert(this.touch0[0])), this.touch1 && C !== "touch" && (this.touch1[1] = k.invert(this.touch1[0])), this.that.__zoom = k, this.emit("zoom"), this;
    },
    end: function() {
      return --this.active === 0 && (delete this.that.__zooming, this.emit("end")), this;
    },
    emit: function(C) {
      var k = Re(this.that).datum();
      l.call(
        C,
        this.that,
        new Am(C, {
          sourceEvent: this.sourceEvent,
          target: x,
          transform: this.that.__zoom,
          dispatch: l
        }),
        k
      );
    }
  };
  function E(C, ...k) {
    if (!e.apply(this, arguments)) return;
    var A = b(this, k).event(C), T = this.__zoom, $ = Math.max(s[0], Math.min(s[1], T.k * Math.pow(2, i.apply(this, arguments)))), P = Fe(C);
    if (A.wheel)
      (A.mouse[0][0] !== P[0] || A.mouse[0][1] !== P[1]) && (A.mouse[1] = T.invert(A.mouse[0] = P)), clearTimeout(A.wheel);
    else {
      if (T.k === $) return;
      A.mouse = [P, T.invert(P)], Ei(this), A.start();
    }
    dn(C), A.wheel = setTimeout(F, g), A.zoom("mouse", n(y(j(T, $), A.mouse[0], A.mouse[1]), A.extent, a));
    function F() {
      A.wheel = null, A.end();
    }
  }
  function _(C, ...k) {
    if (p || !e.apply(this, arguments)) return;
    var A = C.currentTarget, T = b(this, k, !0).event(C), $ = Re(C.view).on("mousemove.zoom", H, !0).on("mouseup.zoom", Y, !0), P = Fe(C, A), F = C.clientX, W = C.clientY;
    Il(C.view), ir(C), T.mouse = [P, this.__zoom.invert(P)], Ei(this), T.start();
    function H(Z) {
      if (dn(Z), !T.moved) {
        var ne = Z.clientX - F, le = Z.clientY - W;
        T.moved = ne * ne + le * le > m;
      }
      T.event(Z).zoom("mouse", n(y(T.that.__zoom, T.mouse[0] = Fe(Z, A), T.mouse[1]), T.extent, a));
    }
    function Y(Z) {
      $.on("mousemove.zoom mouseup.zoom", null), kl(Z.view, T.moved), dn(Z), T.event(Z).end();
    }
  }
  function R(C, ...k) {
    if (e.apply(this, arguments)) {
      var A = this.__zoom, T = Fe(C.changedTouches ? C.changedTouches[0] : C, this), $ = A.invert(T), P = A.k * (C.shiftKey ? 0.5 : 2), F = n(y(j(A, P), T, $), t.apply(this, k), a);
      dn(C), c > 0 ? Re(this).transition().duration(c).call(N, F, T, C) : Re(this).call(x.transform, F, T, C);
    }
  }
  function D(C, ...k) {
    if (e.apply(this, arguments)) {
      var A = C.touches, T = A.length, $ = b(this, k, C.changedTouches.length === T).event(C), P, F, W, H;
      for (ir(C), F = 0; F < T; ++F)
        W = A[F], H = Fe(W, this), H = [H, this.__zoom.invert(H), W.identifier], $.touch0 ? !$.touch1 && $.touch0[2] !== H[2] && ($.touch1 = H, $.taps = 0) : ($.touch0 = H, P = !0, $.taps = 1 + !!d);
      d && (d = clearTimeout(d)), P && ($.taps < 2 && (f = H[0], d = setTimeout(function() {
        d = null;
      }, h)), Ei(this), $.start());
    }
  }
  function I(C, ...k) {
    if (this.__zooming) {
      var A = b(this, k).event(C), T = C.changedTouches, $ = T.length, P, F, W, H;
      for (dn(C), P = 0; P < $; ++P)
        F = T[P], W = Fe(F, this), A.touch0 && A.touch0[2] === F.identifier ? A.touch0[0] = W : A.touch1 && A.touch1[2] === F.identifier && (A.touch1[0] = W);
      if (F = A.that.__zoom, A.touch1) {
        var Y = A.touch0[0], Z = A.touch0[1], ne = A.touch1[0], le = A.touch1[1], G = (G = ne[0] - Y[0]) * G + (G = ne[1] - Y[1]) * G, z = (z = le[0] - Z[0]) * z + (z = le[1] - Z[1]) * z;
        F = j(F, Math.sqrt(G / z)), W = [(Y[0] + ne[0]) / 2, (Y[1] + ne[1]) / 2], H = [(Z[0] + le[0]) / 2, (Z[1] + le[1]) / 2];
      } else if (A.touch0) W = A.touch0[0], H = A.touch0[1];
      else return;
      A.zoom("touch", n(y(F, W, H), A.extent, a));
    }
  }
  function M(C, ...k) {
    if (this.__zooming) {
      var A = b(this, k).event(C), T = C.changedTouches, $ = T.length, P, F;
      for (ir(C), p && clearTimeout(p), p = setTimeout(function() {
        p = null;
      }, h), P = 0; P < $; ++P)
        F = T[P], A.touch0 && A.touch0[2] === F.identifier ? delete A.touch0 : A.touch1 && A.touch1[2] === F.identifier && delete A.touch1;
      if (A.touch1 && !A.touch0 && (A.touch0 = A.touch1, delete A.touch1), A.touch0) A.touch0[1] = this.__zoom.invert(A.touch0[0]);
      else if (A.end(), A.taps === 2 && (F = Fe(F, this), Math.hypot(f[0] - F[0], f[1] - F[1]) < w)) {
        var W = Re(this).on("dblclick.zoom");
        W && W.apply(this, arguments);
      }
    }
  }
  return x.wheelDelta = function(C) {
    return arguments.length ? (i = typeof C == "function" ? C : fi(+C), x) : i;
  }, x.filter = function(C) {
    return arguments.length ? (e = typeof C == "function" ? C : fi(!!C), x) : e;
  }, x.touchable = function(C) {
    return arguments.length ? (o = typeof C == "function" ? C : fi(!!C), x) : o;
  }, x.extent = function(C) {
    return arguments.length ? (t = typeof C == "function" ? C : fi([[+C[0][0], +C[0][1]], [+C[1][0], +C[1][1]]]), x) : t;
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
    return arguments.length ? (m = (C = +C) * C, x) : Math.sqrt(m);
  }, x.tapDistance = function(C) {
    return arguments.length ? (w = +C, x) : w;
  }, x;
}
const Oe = {
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
}, Tn = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], Kl = ["Enter", " ", "Escape"], Xl = {
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
var Ut;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(Ut || (Ut = {}));
var vt;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(vt || (vt = {}));
var Pn;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Pn || (Pn = {}));
const ql = {
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
var dt;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(dt || (dt = {}));
var Bi;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(Bi || (Bi = {}));
var ie;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(ie || (ie = {}));
const da = {
  [ie.Left]: ie.Right,
  [ie.Right]: ie.Left,
  [ie.Top]: ie.Bottom,
  [ie.Bottom]: ie.Top
};
function Yl(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Ul = (e) => "id" in e && "source" in e && "target" in e, Mm = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), ns = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), Wn = (e, t = [0, 0]) => {
  const { width: n, height: i } = at(e), o = e.origin ?? t, s = n * o[0], a = i * o[1];
  return {
    x: e.position.x - s,
    y: e.position.y - a
  };
}, Rm = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((i, o) => {
    const s = typeof o == "string";
    let a = !t.nodeLookup && !s ? o : void 0;
    t.nodeLookup && (a = s ? t.nodeLookup.get(o) : ns(o) ? o : t.nodeLookup.get(o.id));
    const c = a ? Ki(a, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return ro(i, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return so(n);
}, Fn = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, i = !1;
  return e.forEach((o) => {
    (t.filter === void 0 || t.filter(o)) && (n = ro(n, Ki(o)), i = !0);
  }), i ? so(n) : { x: 0, y: 0, width: 0, height: 0 };
}, is = (e, t, [n, i, o] = [0, 0, 1], s = !1, a = !1) => {
  const c = {
    ...rn(t, [n, i, o]),
    width: t.width / o,
    height: t.height / o
  }, u = [];
  for (const l of e.values()) {
    const { measured: d, selectable: f = !0, hidden: p = !1 } = l;
    if (a && !f || p)
      continue;
    const h = d.width ?? l.width ?? l.initialWidth ?? null, g = d.height ?? l.height ?? l.initialHeight ?? null, m = $n(c, Gt(l)), w = (h ?? 0) * (g ?? 0), x = s && m > 0;
    (!l.internals.handleBounds || x || m >= w || l.dragging) && u.push(l);
  }
  return u;
}, zm = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((i) => {
    n.add(i.id);
  }), t.filter((i) => n.has(i.source) || n.has(i.target));
};
function Lm(e, t) {
  const n = /* @__PURE__ */ new Map(), i = t?.nodes ? new Set(t.nodes.map((o) => o.id)) : null;
  return e.forEach((o) => {
    o.measured.width && o.measured.height && (t?.includeHiddenNodes || !o.hidden) && (!i || i.has(o.id)) && n.set(o.id, o);
  }), n;
}
async function Vm({ nodes: e, width: t, height: n, panZoom: i, minZoom: o, maxZoom: s }, a) {
  if (e.size === 0)
    return !0;
  const c = Lm(e, a), u = Fn(c), l = rs(u, t, n, a?.minZoom ?? o, a?.maxZoom ?? s, a?.padding ?? 0.1);
  return await i.setViewport(l, {
    duration: a?.duration,
    ease: a?.ease,
    interpolate: a?.interpolate
  }), !0;
}
function Zl({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: i = [0, 0], nodeExtent: o, onError: s }) {
  const a = n.get(e), c = a.parentId ? n.get(a.parentId) : void 0, { x: u, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, d = a.origin ?? i;
  let f = a.extent || o;
  if (a.extent === "parent" && !a.expandParent)
    if (!c)
      s?.("005", Oe.error005());
    else {
      const h = c.measured.width, g = c.measured.height;
      h && g && (f = [
        [u, l],
        [u + h, l + g]
      ]);
    }
  else c && St(a.extent) && (f = [
    [a.extent[0][0] + u, a.extent[0][1] + l],
    [a.extent[1][0] + u, a.extent[1][1] + l]
  ]);
  const p = St(f) ? jt(t, f, a.measured) : t;
  return (a.measured.width === void 0 || a.measured.height === void 0) && s?.("015", Oe.error015()), {
    position: {
      x: p.x - u + (a.measured.width ?? 0) * d[0],
      y: p.y - l + (a.measured.height ?? 0) * d[1]
    },
    positionAbsolute: p
  };
}
async function Om({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: i, onBeforeDelete: o }) {
  const s = new Set(e.map((p) => p.id)), a = [];
  for (const p of n) {
    if (p.deletable === !1)
      continue;
    const h = s.has(p.id), g = !h && p.parentId && a.find((m) => m.id === p.parentId);
    (h || g) && a.push(p);
  }
  const c = new Set(t.map((p) => p.id)), u = i.filter((p) => p.deletable !== !1), d = zm(a, u);
  for (const p of u)
    c.has(p.id) && !d.find((g) => g.id === p.id) && d.push(p);
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
const Zt = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), jt = (e = { x: 0, y: 0 }, t, n) => ({
  x: Zt(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: Zt(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Gl(e, t, n) {
  const { width: i, height: o } = at(n), { x: s, y: a } = n.internals.positionAbsolute;
  return jt(e, [
    [s, a],
    [s + i, a + o]
  ], t);
}
const fa = (e, t, n) => e < t ? Zt(Math.abs(e - t), 1, t) / t : e > n ? -Zt(Math.abs(e - n), 1, t) / t : 0, os = (e, t, n = 15, i = 40) => {
  const o = fa(e.x, i, t.width - i) * n, s = fa(e.y, i, t.height - i) * n;
  return [o, s];
}, ro = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), Ar = ({ x: e, y: t, width: n, height: i }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + i
}), so = ({ x: e, y: t, x2: n, y2: i }) => ({
  x: e,
  y: t,
  width: n - e,
  height: i - t
}), Gt = (e, t = [0, 0]) => {
  const { x: n, y: i } = ns(e) ? e.internals.positionAbsolute : Wn(e, t);
  return {
    x: n,
    y: i,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, Ki = (e, t = [0, 0]) => {
  const { x: n, y: i } = ns(e) ? e.internals.positionAbsolute : Wn(e, t);
  return {
    x: n,
    y: i,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: i + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, Jl = (e, t) => so(ro(Ar(e), Ar(t))), $n = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), i = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * i);
}, pa = (e) => Ke(e.width) && Ke(e.height) && Ke(e.x) && Ke(e.y), Ke = (e) => !isNaN(e) && isFinite(e), Ql = (e, t) => (n, i) => {
}, Bn = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), rn = ({ x: e, y: t }, [n, i, o], s = !1, a = [1, 1]) => {
  const c = {
    x: (e - n) / o,
    y: (t - i) / o
  };
  return s ? Bn(c, a) : c;
}, Jt = ({ x: e, y: t }, [n, i, o]) => ({
  x: e * o + n,
  y: t * o + i
});
function Mt(e, t) {
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
function Hm(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const i = Mt(e, n), o = Mt(e, t);
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
    const i = Mt(e.top ?? e.y ?? 0, n), o = Mt(e.bottom ?? e.y ?? 0, n), s = Mt(e.left ?? e.x ?? 0, t), a = Mt(e.right ?? e.x ?? 0, t);
    return { top: i, right: a, bottom: o, left: s, x: s + a, y: i + o };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function Wm(e, t, n, i, o, s) {
  const { x: a, y: c } = Jt(e, [t, n, i]), { x: u, y: l } = Jt({ x: e.x + e.width, y: e.y + e.height }, [t, n, i]), d = o - u, f = s - l;
  return {
    left: Math.floor(a),
    top: Math.floor(c),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const rs = (e, t, n, i, o, s) => {
  const a = Hm(s, t, n), c = (t - a.x) / e.width, u = (n - a.y) / e.height, l = Math.min(c, u), d = Zt(l, i, o), f = e.x + e.width / 2, p = e.y + e.height / 2, h = t / 2 - f * d, g = n / 2 - p * d, m = Wm(e, h, g, d, t, n), w = {
    left: Math.min(m.left - a.left, 0),
    top: Math.min(m.top - a.top, 0),
    right: Math.min(m.right - a.right, 0),
    bottom: Math.min(m.bottom - a.bottom, 0)
  };
  return {
    x: h - w.left + w.right,
    y: g - w.top + w.bottom,
    zoom: d
  };
}, Mn = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function St(e) {
  return e != null && e !== "parent";
}
function at(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function eu(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function tu(e, t = { width: 0, height: 0 }, n, i, o) {
  const s = { ...e }, a = i.get(n);
  if (a) {
    const c = a.origin || o;
    s.x += a.internals.positionAbsolute.x - (t.width ?? 0) * c[0], s.y += a.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return s;
}
function ha(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function Fm() {
  let e, t;
  return { promise: new Promise((i, o) => {
    e = i, t = o;
  }), resolve: e, reject: t };
}
function Bm(e) {
  return { ...Xl, ...e || {} };
}
function jn(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: i, containerBounds: o }) {
  const { x: s, y: a } = Xe(e), c = rn({ x: s - (o?.left ?? 0), y: a - (o?.top ?? 0) }, i), { x: u, y: l } = n ? Bn(c, t) : c;
  return {
    xSnapped: u,
    ySnapped: l,
    ...c
  };
}
const ss = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), nu = (e) => e?.getRootNode?.() || window?.document, Km = ["INPUT", "SELECT", "TEXTAREA"];
function iu(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : Km.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const ou = (e) => "clientX" in e, Xe = (e, t) => {
  const n = ou(e), i = n ? e.clientX : e.touches?.[0].clientX, o = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: i - (t?.left ?? 0),
    y: o - (t?.top ?? 0)
  };
}, ga = (e, t, n, i, o) => {
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
      ...ss(a)
    };
  });
};
function ru({ sourceX: e, sourceY: t, targetX: n, targetY: i, sourceControlX: o, sourceControlY: s, targetControlX: a, targetControlY: c }) {
  const u = e * 0.125 + o * 0.375 + a * 0.375 + n * 0.125, l = t * 0.125 + s * 0.375 + c * 0.375 + i * 0.125, d = Math.abs(u - e), f = Math.abs(l - t);
  return [u, l, d, f];
}
function pi(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function ya({ pos: e, x1: t, y1: n, x2: i, y2: o, c: s }) {
  switch (e) {
    case ie.Left:
      return [t - pi(t - i, s), n];
    case ie.Right:
      return [t + pi(i - t, s), n];
    case ie.Top:
      return [t, n - pi(n - o, s)];
    case ie.Bottom:
      return [t, n + pi(o - n, s)];
  }
}
function su({ sourceX: e, sourceY: t, sourcePosition: n = ie.Bottom, targetX: i, targetY: o, targetPosition: s = ie.Top, curvature: a = 0.25 }) {
  const [c, u] = ya({
    pos: n,
    x1: e,
    y1: t,
    x2: i,
    y2: o,
    c: a
  }), [l, d] = ya({
    pos: s,
    x1: i,
    y1: o,
    x2: e,
    y2: t,
    c: a
  }), [f, p, h, g] = ru({
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
    g
  ];
}
function au({ sourceX: e, sourceY: t, targetX: n, targetY: i }) {
  const o = Math.abs(n - e) / 2, s = n < e ? n + o : n - o, a = Math.abs(i - t) / 2, c = i < t ? i + a : i - a;
  return [s, c, o, a];
}
function Xm({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: i = 0, elevateOnSelect: o = !1, zIndexMode: s = "basic" }) {
  if (s === "manual")
    return i;
  const a = o && n ? i + 1e3 : i, c = Math.max(e.parentId || o && e.selected ? e.internals.z : 0, t.parentId || o && t.selected ? t.internals.z : 0);
  return a + c;
}
function qm({ sourceNode: e, targetNode: t, width: n, height: i, transform: o }) {
  const s = ro(Ki(e), Ki(t));
  s.x === s.x2 && (s.x2 += 1), s.y === s.y2 && (s.y2 += 1);
  const a = {
    x: -o[0] / o[2],
    y: -o[1] / o[2],
    width: n / o[2],
    height: i / o[2]
  };
  return $n(a, so(s)) > 0;
}
const cu = ({ source: e, sourceHandle: t, target: n, targetHandle: i }) => `xy-edge__${e}${t || ""}-${n}${i || ""}`, Ym = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), Um = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Oe.error006()), t;
  const i = n.getEdgeId || cu;
  let o;
  return Ul(e) ? o = { ...e } : o = {
    ...e,
    id: i(e)
  }, Ym(o, t) ? t : (o.sourceHandle === null && delete o.sourceHandle, o.targetHandle === null && delete o.targetHandle, t.concat(o));
}, Zm = (e, t, n, i = { shouldReplaceId: !0 }) => {
  const { id: o, ...s } = e;
  if (!t.source || !t.target)
    return i.onError?.("006", Oe.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return i.onError?.("007", Oe.error007(o)), n;
  const c = i.getEdgeId || cu, u = {
    ...s,
    id: i.shouldReplaceId ? c(t) : o,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== o).concat(u);
};
function lu({ sourceX: e, sourceY: t, targetX: n, targetY: i }) {
  const [o, s, a, c] = au({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: i
  });
  return [`M ${e},${t}L ${n},${i}`, o, s, a, c];
}
const ma = {
  [ie.Left]: { x: -1, y: 0 },
  [ie.Right]: { x: 1, y: 0 },
  [ie.Top]: { x: 0, y: -1 },
  [ie.Bottom]: { x: 0, y: 1 }
}, Gm = ({ source: e, sourcePosition: t = ie.Bottom, target: n }) => t === ie.Left || t === ie.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, xa = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Jm({ source: e, sourcePosition: t = ie.Bottom, target: n, targetPosition: i = ie.Top, center: o, offset: s, stepPosition: a }) {
  const c = ma[t], u = ma[i], l = { x: e.x + c.x * s, y: e.y + c.y * s }, d = { x: n.x + u.x * s, y: n.y + u.y * s }, f = Gm({
    source: l,
    sourcePosition: t,
    target: d
  }), p = f.x !== 0 ? "x" : "y", h = f[p];
  let g = [], m, w;
  const x = { x: 0, y: 0 }, j = { x: 0, y: 0 }, [, , y, v] = au({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[p] * u[p] === -1) {
    p === "x" ? (m = o.x ?? l.x + (d.x - l.x) * a, w = o.y ?? (l.y + d.y) / 2) : (m = o.x ?? (l.x + d.x) / 2, w = o.y ?? l.y + (d.y - l.y) * a);
    const E = [
      { x: m, y: l.y },
      { x: m, y: d.y }
    ], _ = [
      { x: l.x, y: w },
      { x: d.x, y: w }
    ];
    c[p] === h ? g = p === "x" ? E : _ : g = p === "x" ? _ : E;
  } else {
    const E = [{ x: l.x, y: d.y }], _ = [{ x: d.x, y: l.y }];
    if (p === "x" ? g = c.x === h ? _ : E : g = c.y === h ? E : _, t === i) {
      const C = Math.abs(e[p] - n[p]);
      if (C <= s) {
        const k = Math.min(s - 1, s - C);
        c[p] === h ? x[p] = (l[p] > e[p] ? -1 : 1) * k : j[p] = (d[p] > n[p] ? -1 : 1) * k;
      }
    }
    if (t !== i) {
      const C = p === "x" ? "y" : "x", k = c[p] === u[C], A = l[C] > d[C], T = l[C] < d[C];
      (c[p] === 1 && (!k && A || k && T) || c[p] !== 1 && (!k && T || k && A)) && (g = p === "x" ? E : _);
    }
    const R = { x: l.x + x.x, y: l.y + x.y }, D = { x: d.x + j.x, y: d.y + j.y }, I = Math.max(Math.abs(R.x - g[0].x), Math.abs(D.x - g[0].x)), M = Math.max(Math.abs(R.y - g[0].y), Math.abs(D.y - g[0].y));
    I >= M ? (m = (R.x + D.x) / 2, w = g[0].y) : (m = g[0].x, w = (R.y + D.y) / 2);
  }
  const N = { x: l.x + x.x, y: l.y + x.y }, b = { x: d.x + j.x, y: d.y + j.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...N.x !== g[0].x || N.y !== g[0].y ? [N] : [],
    ...g,
    ...b.x !== g[g.length - 1].x || b.y !== g[g.length - 1].y ? [b] : [],
    n
  ], m, w, y, v];
}
function Qm(e, t, n, i) {
  const o = Math.min(xa(e, t) / 2, xa(t, n) / 2, i), { x: s, y: a } = t;
  if (e.x === s && s === n.x || e.y === a && a === n.y)
    return `L${s} ${a}`;
  if (e.y === a) {
    const l = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${s + o * l},${a}Q ${s},${a} ${s},${a + o * d}`;
  }
  const c = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${s},${a + o * u}Q ${s},${a} ${s + o * c},${a}`;
}
function Xi({ sourceX: e, sourceY: t, sourcePosition: n = ie.Bottom, targetX: i, targetY: o, targetPosition: s = ie.Top, borderRadius: a = 5, centerX: c, centerY: u, offset: l = 20, stepPosition: d = 0.5 }) {
  const [f, p, h, g, m] = Jm({
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
    w += Qm(f[x - 1], f[x], f[x + 1], a);
  return w += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [w, p, h, g, m];
}
function wa(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function ex(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!wa(t) || !wa(n))
    return null;
  const i = t.internals.handleBounds || va(t.handles), o = n.internals.handleBounds || va(n.handles), s = ba(i?.source ?? [], e.sourceHandle), a = ba(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === Ut.Strict ? o?.target ?? [] : (o?.target ?? []).concat(o?.source ?? []),
    e.targetHandle
  );
  if (!s || !a)
    return e.onError?.("008", Oe.error008(s ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = s?.position || ie.Bottom, u = a?.position || ie.Top, l = Ct(t, s, c), d = Ct(n, a, u);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: d.x,
    targetY: d.y,
    sourcePosition: c,
    targetPosition: u
  };
}
function va(e) {
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
function Ct(e, t, n = ie.Left, i = !1) {
  const o = (t?.x ?? 0) + e.internals.positionAbsolute.x, s = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: a, height: c } = t ?? at(e);
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
function ba(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function _r(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((i) => `${i}=${e[i]}`).join("&")}` : "";
}
function tx(e, { id: t, defaultColor: n, defaultMarkerStart: i, defaultMarkerEnd: o }) {
  const s = /* @__PURE__ */ new Set();
  return e.reduce((a, c) => ([c.markerStart || i, c.markerEnd || o].forEach((u) => {
    if (u && typeof u == "object") {
      const l = _r(u, t);
      s.has(l) || (a.push({ id: l, color: u.color || n, ...u }), s.add(l));
    }
  }), a), []).sort((a, c) => a.id.localeCompare(c.id));
}
const uu = 1e3, nx = 10, as = {
  nodeOrigin: [0, 0],
  nodeExtent: Tn,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, ix = {
  ...as,
  checkEquality: !0
};
function cs(e, t) {
  const n = { ...e };
  for (const i in t)
    t[i] !== void 0 && (n[i] = t[i]);
  return n;
}
function ox(e, t, n) {
  const i = cs(as, n);
  for (const o of e.values())
    if (o.parentId)
      us(o, e, t, i);
    else {
      const s = Wn(o, i.nodeOrigin), a = St(o.extent) ? o.extent : i.nodeExtent, c = jt(s, a, at(o));
      o.internals.positionAbsolute = c;
    }
}
function rx(e, t) {
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
function ls(e) {
  return e === "manual";
}
function Dr(e, t, n, i = {}) {
  const o = cs(ix, i), s = { i: 0 }, a = new Map(t), c = o?.elevateNodesOnSelect && !ls(o.zIndexMode) ? uu : 0;
  let u = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = a.get(d.id);
    if (o.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const p = Wn(d, o.nodeOrigin), h = St(d.extent) ? d.extent : o.nodeExtent, g = jt(p, h, at(d));
      f = {
        ...o.defaults,
        ...d,
        measured: {
          width: d.measured?.width,
          height: d.measured?.height
        },
        internals: {
          positionAbsolute: g,
          // if user re-initializes the node or removes `measured` for whatever reason, we reset the handleBounds so that the node gets re-measured
          handleBounds: rx(d, f),
          z: du(d, c, o.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (u = !1), d.parentId && us(f, t, n, i, s), l ||= d.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: l };
}
function sx(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function us(e, t, n, i, o) {
  const { elevateNodesOnSelect: s, nodeOrigin: a, nodeExtent: c, zIndexMode: u } = cs(as, i), l = e.parentId, d = t.get(l);
  if (!d) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  sx(e, n), o && !d.parentId && d.internals.rootParentIndex === void 0 && u === "auto" && (d.internals.rootParentIndex = ++o.i, d.internals.z = d.internals.z + o.i * nx), o && d.internals.rootParentIndex !== void 0 && (o.i = d.internals.rootParentIndex);
  const f = s && !ls(u) ? uu : 0, { x: p, y: h, z: g } = ax(e, d, a, c, f, u), { positionAbsolute: m } = e.internals, w = p !== m.x || h !== m.y;
  (w || g !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: w ? { x: p, y: h } : m,
      z: g
    }
  });
}
function du(e, t, n) {
  const i = Ke(e.zIndex) ? e.zIndex : 0;
  return ls(n) ? i : i + (e.selected ? t : 0);
}
function ax(e, t, n, i, o, s) {
  const { x: a, y: c } = t.internals.positionAbsolute, u = at(e), l = Wn(e, n), d = St(e.extent) ? jt(l, e.extent, u) : l;
  let f = jt({ x: a + d.x, y: c + d.y }, i, u);
  e.extent === "parent" && (f = Gl(f, u, t));
  const p = du(e, o, s), h = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: h >= p ? h + 1 : p
  };
}
function ds(e, t, n, i = [0, 0]) {
  const o = [], s = /* @__PURE__ */ new Map();
  for (const a of e) {
    const c = t.get(a.parentId);
    if (!c)
      continue;
    const u = s.get(a.parentId)?.expandedRect ?? Gt(c), l = Jl(u, a.rect);
    s.set(a.parentId, { expandedRect: l, parent: c });
  }
  return s.size > 0 && s.forEach(({ expandedRect: a, parent: c }, u) => {
    const l = c.internals.positionAbsolute, d = at(c), f = c.origin ?? i, p = a.x < l.x ? Math.round(Math.abs(l.x - a.x)) : 0, h = a.y < l.y ? Math.round(Math.abs(l.y - a.y)) : 0, g = Math.max(d.width, Math.round(a.width)), m = Math.max(d.height, Math.round(a.height)), w = (g - d.width) * f[0], x = (m - d.height) * f[1];
    (p > 0 || h > 0 || w || x) && (o.push({
      id: u,
      type: "position",
      position: {
        x: c.position.x - p + w,
        y: c.position.y - h + x
      }
    }), n.get(u)?.forEach((j) => {
      e.some((y) => y.id === j.id) || o.push({
        id: j.id,
        type: "position",
        position: {
          x: j.position.x + p,
          y: j.position.y + h
        }
      });
    })), (d.width < a.width || d.height < a.height || p || h) && o.push({
      id: u,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: g + (p ? f[0] * p - w : 0),
        height: m + (h ? f[1] * h - x : 0)
      }
    });
  }), o;
}
function cx(e, t, n, i, o, s, a) {
  const c = i?.querySelector(".xyflow__viewport");
  let u = !1;
  if (!c)
    return { changes: [], updatedInternals: u };
  const l = [], d = window.getComputedStyle(c), { m22: f } = new window.DOMMatrixReadOnly(d.transform), p = [];
  for (const h of e.values()) {
    const g = t.get(h.id);
    if (!g)
      continue;
    if (g.hidden) {
      t.set(g.id, {
        ...g,
        internals: {
          ...g.internals,
          handleBounds: void 0
        }
      }), u = !0;
      continue;
    }
    const m = ss(h.nodeElement), w = g.measured.width !== m.width || g.measured.height !== m.height;
    if (!!(m.width && m.height && (w || !g.internals.handleBounds || h.force))) {
      const j = h.nodeElement.getBoundingClientRect(), y = St(g.extent) ? g.extent : s;
      let { positionAbsolute: v } = g.internals;
      g.parentId && g.extent === "parent" ? v = Gl(v, m, t.get(g.parentId)) : y && (v = jt(v, y, m));
      const N = {
        ...g,
        measured: m,
        internals: {
          ...g.internals,
          positionAbsolute: v,
          handleBounds: {
            source: ga("source", h.nodeElement, j, f, g.id),
            target: ga("target", h.nodeElement, j, f, g.id)
          }
        }
      };
      t.set(g.id, N), g.parentId && us(N, t, n, { nodeOrigin: o, zIndexMode: a }), u = !0, w && (l.push({
        id: g.id,
        type: "dimensions",
        dimensions: m
      }), g.expandParent && g.parentId && p.push({
        id: g.id,
        parentId: g.parentId,
        rect: Gt(N, o)
      }));
    }
  }
  if (p.length > 0) {
    const h = ds(p, t, n, o);
    l.push(...h);
  }
  return { changes: l, updatedInternals: u };
}
async function lx({ delta: e, panZoom: t, transform: n, translateExtent: i, width: o, height: s }) {
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
function Na(e, t, n, i, o, s) {
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
function fu(e, t, n) {
  e.clear(), t.clear();
  for (const i of n) {
    const { source: o, target: s, sourceHandle: a = null, targetHandle: c = null } = i, u = { edgeId: i.id, source: o, target: s, sourceHandle: a, targetHandle: c }, l = `${o}-${a}--${s}-${c}`, d = `${s}-${c}--${o}-${a}`;
    Na("source", u, d, e, o, a), Na("target", u, l, e, s, c), t.set(i.id, i);
  }
}
function pu(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : pu(n, t) : !1;
}
function ja(e, t, n) {
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
function ux(e, t, n, i) {
  const o = /* @__PURE__ */ new Map();
  for (const [s, a] of e)
    if ((a.selected || a.id === i) && (!a.parentId || !pu(a, e)) && (a.draggable || t && typeof a.draggable > "u")) {
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
function dx({ dragItems: e, snapGrid: t, x: n, y: i }) {
  const o = e.values().next().value;
  if (!o)
    return null;
  const s = {
    x: n - o.distance.x,
    y: i - o.distance.y
  }, a = Bn(s, t);
  return {
    x: a.x - s.x,
    y: a.y - s.y
  };
}
function fx({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: i, onDragStop: o }) {
  let s = { x: null, y: null }, a = 0, c = /* @__PURE__ */ new Map(), u = !1, l = { x: 0, y: 0 }, d = null, f = !1, p = null, h = !1, g = !1, m = null;
  function w({ noDragClassName: j, handleSelector: y, domNode: v, isSelectable: N, nodeId: b, nodeClickDistance: S = 0 }) {
    p = Re(v);
    function E({ x: I, y: M }) {
      const { nodeLookup: C, nodeExtent: k, snapGrid: A, snapToGrid: T, nodeOrigin: $, onNodeDrag: P, onSelectionDrag: F, onError: W, updateNodePositions: H } = t();
      s = { x: I, y: M };
      let Y = !1;
      const Z = c.size > 1, ne = Z && k ? Ar(Fn(c)) : null, le = Z && T ? dx({
        dragItems: c,
        snapGrid: A,
        x: I,
        y: M
      }) : null;
      for (const [G, z] of c) {
        if (!C.has(G))
          continue;
        let X = { x: I - z.distance.x, y: M - z.distance.y };
        T && (X = le ? {
          x: Math.round(X.x + le.x),
          y: Math.round(X.y + le.y)
        } : Bn(X, A));
        let ae = null;
        if (Z && k && !z.extent && ne) {
          const { positionAbsolute: oe } = z.internals, fe = oe.x - ne.x + k[0][0], V = oe.x + z.measured.width - ne.x2 + k[1][0], te = oe.y - ne.y + k[0][1], ge = oe.y + z.measured.height - ne.y2 + k[1][1];
          ae = [
            [fe, te],
            [V, ge]
          ];
        }
        const { position: ce, positionAbsolute: Q } = Zl({
          nodeId: G,
          nextPosition: X,
          nodeLookup: C,
          nodeExtent: ae || k,
          nodeOrigin: $,
          onError: W
        });
        Y = Y || z.position.x !== ce.x || z.position.y !== ce.y, z.position = ce, z.internals.positionAbsolute = Q;
      }
      if (g = g || Y, !!Y && (H(c, !0), m && (i || P || !b && F))) {
        const [G, z] = or({
          nodeId: b,
          dragItems: c,
          nodeLookup: C
        });
        i?.(m, c, G, z), P?.(m, G, z), b || F?.(m, z);
      }
    }
    async function _() {
      if (!d)
        return;
      const { transform: I, panBy: M, autoPanSpeed: C, autoPanOnNodeDrag: k } = t();
      if (!k) {
        u = !1, cancelAnimationFrame(a);
        return;
      }
      const [A, T] = os(l, d, C);
      (A !== 0 || T !== 0) && (s.x = (s.x ?? 0) - A / I[2], s.y = (s.y ?? 0) - T / I[2], await M({ x: A, y: T }) && E(s)), a = requestAnimationFrame(_);
    }
    function R(I) {
      const { nodeLookup: M, multiSelectionActive: C, nodesDraggable: k, transform: A, snapGrid: T, snapToGrid: $, selectNodesOnDrag: P, onNodeDragStart: F, onSelectionDragStart: W, unselectNodesAndEdges: H } = t();
      f = !0, (!P || !N) && !C && b && (M.get(b)?.selected || H()), N && P && b && e?.(b);
      const Y = jn(I.sourceEvent, { transform: A, snapGrid: T, snapToGrid: $, containerBounds: d });
      if (s = Y, c = ux(M, k, Y, b), c.size > 0 && (n || F || !b && W)) {
        const [Z, ne] = or({
          nodeId: b,
          dragItems: c,
          nodeLookup: M
        });
        n?.(I.sourceEvent, c, Z, ne), F?.(I.sourceEvent, Z, ne), b || W?.(I.sourceEvent, ne);
      }
    }
    const D = Al().clickDistance(S).on("start", (I) => {
      const { domNode: M, nodeDragThreshold: C, transform: k, snapGrid: A, snapToGrid: T } = t();
      d = M?.getBoundingClientRect() || null, h = !1, g = !1, m = I.sourceEvent, C === 0 && R(I), s = jn(I.sourceEvent, { transform: k, snapGrid: A, snapToGrid: T, containerBounds: d }), l = Xe(I.sourceEvent, d);
    }).on("drag", (I) => {
      const { autoPanOnNodeDrag: M, transform: C, snapGrid: k, snapToGrid: A, nodeDragThreshold: T, nodeLookup: $ } = t(), P = jn(I.sourceEvent, { transform: C, snapGrid: k, snapToGrid: A, containerBounds: d });
      if (m = I.sourceEvent, (I.sourceEvent.type === "touchmove" && I.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      b && !$.has(b)) && (h = !0), !h) {
        if (!u && M && f && (u = !0, _()), !f) {
          const F = Xe(I.sourceEvent, d), W = F.x - l.x, H = F.y - l.y;
          Math.sqrt(W * W + H * H) > T && R(I);
        }
        (s.x !== P.xSnapped || s.y !== P.ySnapped) && c && f && (l = Xe(I.sourceEvent, d), E(P));
      }
    }).on("end", (I) => {
      if (!f || h) {
        h && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (u = !1, f = !1, cancelAnimationFrame(a), c.size > 0) {
        const { nodeLookup: M, updateNodePositions: C, onNodeDragStop: k, onSelectionDragStop: A } = t();
        if (g && (C(c, !1), g = !1), o || k || !b && A) {
          const [T, $] = or({
            nodeId: b,
            dragItems: c,
            nodeLookup: M,
            dragging: !1
          });
          o?.(I.sourceEvent, c, T, $), k?.(I.sourceEvent, T, $), b || A?.(I.sourceEvent, $);
        }
      }
    }).filter((I) => {
      const M = I.target;
      return !I.button && (!j || !ja(M, `.${j}`, v)) && (!y || ja(M, y, v));
    });
    p.call(D);
  }
  function x() {
    p?.on(".drag", null);
  }
  return {
    update: w,
    destroy: x
  };
}
function px(e, t, n) {
  const i = [], o = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const s of t.values())
    $n(o, Gt(s)) > 0 && i.push(s);
  return i;
}
const hx = 250;
function gx(e, t, n, i) {
  let o = [], s = 1 / 0;
  const a = px(e, n, t + hx);
  for (const c of a) {
    const u = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of u) {
      if (i.nodeId === l.nodeId && i.type === l.type && i.id === l.id)
        continue;
      const { x: d, y: f } = Ct(c, l, l.position, !0), p = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(f - e.y, 2));
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
function hu(e, t, n, i, o, s = !1) {
  const a = i.get(e);
  if (!a)
    return null;
  const c = o === "strict" ? a.internals.handleBounds?.[t] : [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []], u = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return u && s ? { ...u, ...Ct(a, u, u.position, !0) } : u;
}
function gu(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function yx(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const yu = () => !0;
function mx(e, { connectionMode: t, connectionRadius: n, handleId: i, nodeId: o, edgeUpdaterType: s, isTarget: a, domNode: c, nodeLookup: u, lib: l, autoPanOnConnect: d, flowId: f, panBy: p, cancelConnection: h, onConnectStart: g, onConnect: m, onConnectEnd: w, isValidConnection: x = yu, onReconnectEnd: j, updateConnection: y, getTransform: v, getFromHandle: N, autoPanSpeed: b, dragThreshold: S = 1, handleDomNode: E }) {
  const _ = nu(e.target);
  let R = 0, D;
  const { x: I, y: M } = Xe(e), C = gu(s, E), k = c?.getBoundingClientRect();
  let A = !1;
  if (!k || !C)
    return;
  const T = hu(o, C, i, u, t);
  if (!T)
    return;
  let $ = Xe(e, k), P = !1, F = null, W = !1, H = null;
  function Y() {
    if (!d || !k)
      return;
    const [ce, Q] = os($, k, b);
    p({ x: ce, y: Q }), R = requestAnimationFrame(Y);
  }
  const Z = {
    ...T,
    nodeId: o,
    type: C,
    position: T.position
  }, ne = u.get(o);
  let G = {
    inProgress: !0,
    isValid: null,
    from: Ct(ne, Z, ie.Left, !0),
    fromHandle: Z,
    fromPosition: Z.position,
    fromNode: ne,
    to: $,
    toHandle: null,
    toPosition: da[Z.position],
    toNode: null,
    pointer: $
  };
  function z() {
    A = !0, y(G), g?.(e, { nodeId: o, handleId: i, handleType: C });
  }
  S === 0 && z();
  function X(ce) {
    if (!A) {
      const { x: ge, y: me } = Xe(ce), Te = ge - I, Ie = me - M;
      if (!(Te * Te + Ie * Ie > S * S))
        return;
      z();
    }
    if (!N() || !Z) {
      ae(ce);
      return;
    }
    const Q = v();
    $ = Xe(ce, k), D = gx(rn($, Q, !1, [1, 1]), n, u, Z), P || (Y(), P = !0);
    const oe = mu(ce, {
      handle: D,
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
    H = oe.handleDomNode, F = oe.connection, W = yx(!!D, oe.isValid);
    const fe = u.get(o), V = fe ? Ct(fe, Z, ie.Left, !0) : G.from, te = {
      ...G,
      from: V,
      isValid: W,
      to: oe.toHandle && W ? Jt({ x: oe.toHandle.x, y: oe.toHandle.y }, Q) : $,
      toHandle: oe.toHandle,
      toPosition: W && oe.toHandle ? oe.toHandle.position : da[Z.position],
      toNode: oe.toHandle ? u.get(oe.toHandle.nodeId) : null,
      pointer: $
    };
    y(te), G = te;
  }
  function ae(ce) {
    if (!("touches" in ce && ce.touches.length > 0)) {
      if (A) {
        (D || H) && F && W && m?.(F);
        const { inProgress: Q, ...oe } = G, fe = {
          ...oe,
          toPosition: G.toHandle ? G.toPosition : null
        };
        w?.(ce, fe), s && j?.(ce, fe);
      }
      h(), cancelAnimationFrame(R), P = !1, W = !1, F = null, H = null, _.removeEventListener("mousemove", X), _.removeEventListener("mouseup", ae), _.removeEventListener("touchmove", X), _.removeEventListener("touchend", ae);
    }
  }
  _.addEventListener("mousemove", X), _.addEventListener("mouseup", ae), _.addEventListener("touchmove", X), _.addEventListener("touchend", ae);
}
function mu(e, { handle: t, connectionMode: n, fromNodeId: i, fromHandleId: o, fromType: s, doc: a, lib: c, flowId: u, isValidConnection: l = yu, nodeLookup: d }) {
  const f = s === "target", p = t ? a.querySelector(`.${c}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: h, y: g } = Xe(e), m = a.elementFromPoint(h, g), w = m?.classList.contains(`${c}-flow__handle`) ? m : p, x = {
    handleDomNode: w,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (w) {
    const j = gu(void 0, w), y = w.getAttribute("data-nodeid"), v = w.getAttribute("data-handleid"), N = w.classList.contains("connectable"), b = w.classList.contains("connectableend");
    if (!y || !j)
      return x;
    const S = {
      source: f ? y : i,
      sourceHandle: f ? v : o,
      target: f ? i : y,
      targetHandle: f ? o : v
    };
    x.connection = S;
    const _ = N && b && (n === Ut.Strict ? f && j === "source" || !f && j === "target" : y !== i || v !== o);
    x.isValid = _ && l(S), x.toHandle = hu(y, j, v, d, n, !0);
  }
  return x;
}
const Tr = {
  onPointerDown: mx,
  isValid: mu
};
function xx({ domNode: e, panZoom: t, getTransform: n, getViewScale: i }) {
  const o = Re(e);
  function s({ translateExtent: c, width: u, height: l, zoomStep: d = 1, pannable: f = !0, zoomable: p = !0, inversePan: h = !1 }) {
    const g = (y) => {
      if (y.sourceEvent.type !== "wheel" || !t)
        return;
      const v = n(), N = y.sourceEvent.ctrlKey && Mn() ? 10 : 1, b = -y.sourceEvent.deltaY * (y.sourceEvent.deltaMode === 1 ? 0.05 : y.sourceEvent.deltaMode ? 1 : 2e-3) * d, S = v[2] * Math.pow(2, b * N);
      t.scaleTo(S);
    };
    let m = [0, 0];
    const w = (y) => {
      (y.sourceEvent.type === "mousedown" || y.sourceEvent.type === "touchstart") && (m = [
        y.sourceEvent.clientX ?? y.sourceEvent.touches[0].clientX,
        y.sourceEvent.clientY ?? y.sourceEvent.touches[0].clientY
      ]);
    }, x = (y) => {
      const v = n();
      if (y.sourceEvent.type !== "mousemove" && y.sourceEvent.type !== "touchmove" || !t)
        return;
      const N = [
        y.sourceEvent.clientX ?? y.sourceEvent.touches[0].clientX,
        y.sourceEvent.clientY ?? y.sourceEvent.touches[0].clientY
      ], b = [N[0] - m[0], N[1] - m[1]];
      m = N;
      const S = i() * Math.max(v[2], Math.log(v[2])) * (h ? -1 : 1), E = {
        x: v[0] - b[0] * S,
        y: v[1] - b[1] * S
      }, _ = [
        [0, 0],
        [u, l]
      ];
      t.setViewportConstrained({
        x: E.x,
        y: E.y,
        zoom: v[2]
      }, _, c);
    }, j = Bl().on("start", w).on("zoom", f ? x : null).on("zoom.wheel", p ? g : null);
    o.call(j, {});
  }
  function a() {
    o.on("zoom", null);
  }
  return {
    update: s,
    destroy: a,
    pointer: Fe
  };
}
const ao = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), rr = ({ x: e, y: t, zoom: n }) => oo.translate(e, t).scale(n), zt = (e, t) => e.target.closest(`.${t}`), xu = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), wx = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, sr = (e, t = 0, n = wx, i = () => {
}) => {
  const o = typeof t == "number" && t > 0;
  return o || i(), o ? e.transition().duration(t).ease(n).on("end", i) : e;
}, wu = (e) => {
  const t = e.ctrlKey && Mn() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function vx({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: i, panOnScrollMode: o, panOnScrollSpeed: s, zoomOnPinch: a, onPanZoomStart: c, onPanZoom: u, onPanZoomEnd: l }) {
  return (d) => {
    if (zt(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && a) {
      const w = Fe(d), x = wu(d), j = f * Math.pow(2, x);
      i.scaleTo(n, j, w, d);
      return;
    }
    const p = d.deltaMode === 1 ? 20 : 1;
    let h = o === vt.Vertical ? 0 : d.deltaX * p, g = o === vt.Horizontal ? 0 : d.deltaY * p;
    !Mn() && d.shiftKey && o !== vt.Vertical && (h = d.deltaY * p, g = 0), i.translateBy(
      n,
      -(h / f) * s,
      -(g / f) * s,
      // @ts-ignore
      { internal: !0 }
    );
    const m = ao(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(d, m), e.panScrollTimeout = setTimeout(() => {
      l?.(d, m), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(d, m));
  };
}
function bx({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(i, o) {
    const s = i.type === "wheel", a = !t && s && !i.ctrlKey, c = zt(i, e);
    if (i.ctrlKey && s && c && i.preventDefault(), a || c)
      return null;
    i.preventDefault(), n.call(this, i, o);
  };
}
function Nx({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (i) => {
    if (i.sourceEvent?.internal)
      return;
    const o = ao(i.transform);
    e.mouseButton = i.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = o, i.sourceEvent?.type === "mousedown" && t(!0), n && n?.(i.sourceEvent, o);
  };
}
function jx({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: i, onPanZoom: o }) {
  return (s) => {
    e.usedRightMouseButton = !!(n && xu(t, e.mouseButton ?? 0)), s.sourceEvent?.sync || i([s.transform.x, s.transform.y, s.transform.k]), o && !s.sourceEvent?.internal && o?.(s.sourceEvent, ao(s.transform));
  };
}
function Sx({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: i, onPanZoomEnd: o, onPaneContextMenu: s }) {
  return (a) => {
    if (!a.sourceEvent?.internal && (e.isZoomingOrPanning = !1, s && xu(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && a.sourceEvent && s(a.sourceEvent), e.usedRightMouseButton = !1, i(!1), o)) {
      const c = ao(a.transform);
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
function Cx({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: i, panOnScroll: o, zoomOnDoubleClick: s, userSelectionActive: a, noWheelClassName: c, noPanClassName: u, lib: l, connectionInProgress: d }) {
  return (f) => {
    const p = e || t, h = n && f.ctrlKey, g = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (zt(f, `${l}-flow__node`) || zt(f, `${l}-flow__edge`)))
      return !0;
    if (!i && !p && !o && !s && !n || a || d && !g || zt(f, c) && g || zt(f, u) && (!g || o && g && !e) || !n && f.ctrlKey && g)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!p && !o && !h && g || !i && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(i) && !i.includes(f.button) && f.type === "mousedown")
      return !1;
    const m = Array.isArray(i) && i.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || g) && m;
  };
}
function Ex({ domNode: e, minZoom: t, maxZoom: n, translateExtent: i, viewport: o, onPanZoom: s, onPanZoomStart: a, onPanZoomEnd: c, onDraggingChange: u }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = Bl().scaleExtent([t, n]).translateExtent(i), p = Re(e).call(f);
  j({
    x: o.x,
    y: o.y,
    zoom: Zt(o.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], i);
  const h = p.on("wheel.zoom"), g = p.on("dblclick.zoom");
  f.wheelDelta(wu);
  async function m(D, I) {
    return p ? new Promise((M) => {
      f?.interpolate(I?.interpolate === "linear" ? Nn : ji).transform(sr(p, I?.duration, I?.ease, () => M(!0)), D);
    }) : !1;
  }
  function w({ noWheelClassName: D, noPanClassName: I, onPaneContextMenu: M, userSelectionActive: C, panOnScroll: k, panOnDrag: A, panOnScrollMode: T, panOnScrollSpeed: $, preventScrolling: P, zoomOnPinch: F, zoomOnScroll: W, zoomOnDoubleClick: H, zoomActivationKeyPressed: Y, lib: Z, onTransformChange: ne, connectionInProgress: le, paneClickDistance: G, selectionOnDrag: z }) {
    C && !l.isZoomingOrPanning && x();
    const X = k && !Y && !C;
    f.clickDistance(z ? 1 / 0 : !Ke(G) || G < 0 ? 0 : G);
    const ae = X ? vx({
      zoomPanValues: l,
      noWheelClassName: D,
      d3Selection: p,
      d3Zoom: f,
      panOnScrollMode: T,
      panOnScrollSpeed: $,
      zoomOnPinch: F,
      onPanZoomStart: a,
      onPanZoom: s,
      onPanZoomEnd: c
    }) : bx({
      noWheelClassName: D,
      preventScrolling: P,
      d3ZoomHandler: h
    });
    p.on("wheel.zoom", ae, { passive: !1 });
    const ce = Nx({
      zoomPanValues: l,
      onDraggingChange: u,
      onPanZoomStart: a
    });
    f.on("start", ce);
    const Q = jx({
      zoomPanValues: l,
      panOnDrag: A,
      onPaneContextMenu: !!M,
      onPanZoom: s,
      onTransformChange: ne
    });
    f.on("zoom", Q);
    const oe = Sx({
      zoomPanValues: l,
      panOnDrag: A,
      panOnScroll: k,
      onPaneContextMenu: M,
      onPanZoomEnd: c,
      onDraggingChange: u
    });
    f.on("end", oe);
    const fe = Cx({
      zoomActivationKeyPressed: Y,
      panOnDrag: A,
      zoomOnScroll: W,
      panOnScroll: k,
      zoomOnDoubleClick: H,
      zoomOnPinch: F,
      userSelectionActive: C,
      noPanClassName: I,
      noWheelClassName: D,
      lib: Z,
      connectionInProgress: le
    });
    f.filter(fe), H ? p.on("dblclick.zoom", g) : p.on("dblclick.zoom", null);
  }
  function x() {
    f.on("zoom", null);
  }
  async function j(D, I, M) {
    const C = rr(D), k = f?.constrain()(C, I, M);
    return k && await m(k), k;
  }
  async function y(D, I) {
    const M = rr(D);
    return await m(M, I), M;
  }
  function v(D) {
    if (p) {
      const I = rr(D), M = p.property("__zoom");
      (M.k !== D.zoom || M.x !== D.x || M.y !== D.y) && f?.transform(p, I, null, { sync: !0 });
    }
  }
  function N() {
    const D = p ? Fl(p.node()) : { x: 0, y: 0, k: 1 };
    return { x: D.x, y: D.y, zoom: D.k };
  }
  async function b(D, I) {
    return p ? new Promise((M) => {
      f?.interpolate(I?.interpolate === "linear" ? Nn : ji).scaleTo(sr(p, I?.duration, I?.ease, () => M(!0)), D);
    }) : !1;
  }
  async function S(D, I) {
    return p ? new Promise((M) => {
      f?.interpolate(I?.interpolate === "linear" ? Nn : ji).scaleBy(sr(p, I?.duration, I?.ease, () => M(!0)), D);
    }) : !1;
  }
  function E(D) {
    f?.scaleExtent(D);
  }
  function _(D) {
    f?.translateExtent(D);
  }
  function R(D) {
    const I = !Ke(D) || D < 0 ? 0 : D;
    f?.clickDistance(I);
  }
  return {
    update: w,
    destroy: x,
    setViewport: y,
    setViewportConstrained: j,
    getViewport: N,
    scaleTo: b,
    scaleBy: S,
    setScaleExtent: E,
    setTranslateExtent: _,
    syncViewport: v,
    setClickDistance: R
  };
}
var Qt;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(Qt || (Qt = {}));
function Ix({ width: e, prevWidth: t, height: n, prevHeight: i, affectsX: o, affectsY: s }) {
  const a = e - t, c = n - i, u = [a > 0 ? 1 : a < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return a && o && (u[0] = u[0] * -1), c && s && (u[1] = u[1] * -1), u;
}
function Sa(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), i = e.includes("left"), o = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: i,
    affectsY: o
  };
}
function lt(e, t) {
  return Math.max(0, t - e);
}
function ut(e, t) {
  return Math.max(0, e - t);
}
function hi(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function Ca(e, t) {
  return e ? !t : t;
}
function kx(e, t, n, i, o, s, a, c) {
  let { affectsX: u, affectsY: l } = t;
  const { isHorizontal: d, isVertical: f } = t, p = d && f, { xSnapped: h, ySnapped: g } = n, { minWidth: m, maxWidth: w, minHeight: x, maxHeight: j } = i, { x: y, y: v, width: N, height: b, aspectRatio: S } = e;
  let E = Math.floor(d ? h - e.pointerX : 0), _ = Math.floor(f ? g - e.pointerY : 0);
  const R = N + (u ? -E : E), D = b + (l ? -_ : _), I = -s[0] * N, M = -s[1] * b;
  let C = hi(R, m, w), k = hi(D, x, j);
  if (a) {
    let $ = 0, P = 0;
    u && E < 0 ? $ = lt(y + E + I, a[0][0]) : !u && E > 0 && ($ = ut(y + R + I, a[1][0])), l && _ < 0 ? P = lt(v + _ + M, a[0][1]) : !l && _ > 0 && (P = ut(v + D + M, a[1][1])), C = Math.max(C, $), k = Math.max(k, P);
  }
  if (c) {
    let $ = 0, P = 0;
    u && E > 0 ? $ = ut(y + E, c[0][0]) : !u && E < 0 && ($ = lt(y + R, c[1][0])), l && _ > 0 ? P = ut(v + _, c[0][1]) : !l && _ < 0 && (P = lt(v + D, c[1][1])), C = Math.max(C, $), k = Math.max(k, P);
  }
  if (o) {
    if (d) {
      const $ = hi(R / S, x, j) * S;
      if (C = Math.max(C, $), a) {
        let P = 0;
        !u && !l || u && !l && p ? P = ut(v + M + R / S, a[1][1]) * S : P = lt(v + M + (u ? E : -E) / S, a[0][1]) * S, C = Math.max(C, P);
      }
      if (c) {
        let P = 0;
        !u && !l || u && !l && p ? P = lt(v + R / S, c[1][1]) * S : P = ut(v + (u ? E : -E) / S, c[0][1]) * S, C = Math.max(C, P);
      }
    }
    if (f) {
      const $ = hi(D * S, m, w) / S;
      if (k = Math.max(k, $), a) {
        let P = 0;
        !u && !l || l && !u && p ? P = ut(y + D * S + I, a[1][0]) / S : P = lt(y + (l ? _ : -_) * S + I, a[0][0]) / S, k = Math.max(k, P);
      }
      if (c) {
        let P = 0;
        !u && !l || l && !u && p ? P = lt(y + D * S, c[1][0]) / S : P = ut(y + (l ? _ : -_) * S, c[0][0]) / S, k = Math.max(k, P);
      }
    }
  }
  _ = _ + (_ < 0 ? k : -k), E = E + (E < 0 ? C : -C), o && (p ? R > D * S ? _ = (Ca(u, l) ? -E : E) / S : E = (Ca(u, l) ? -_ : _) * S : d ? (_ = E / S, l = u) : (E = _ * S, u = l));
  const A = u ? y + E : y, T = l ? v + _ : v;
  return {
    width: N + (u ? -E : E),
    height: b + (l ? -_ : _),
    x: s[0] * E * (u ? -1 : 1) + A,
    y: s[1] * _ * (l ? -1 : 1) + T
  };
}
const vu = { width: 0, height: 0, x: 0, y: 0 }, Ax = {
  ...vu,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function _x(e, t, n) {
  const i = t.position.x + e.position.x, o = t.position.y + e.position.y, s = e.measured.width ?? 0, a = e.measured.height ?? 0, c = n[0] * s, u = n[1] * a;
  return [
    [i - c, o - u],
    [i + s - c, o + a - u]
  ];
}
function Dx({ domNode: e, nodeId: t, getStoreItems: n, onChange: i, onEnd: o }) {
  const s = Re(e);
  let a = {
    controlDirection: Sa("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: l, boundaries: d, keepAspectRatio: f, resizeDirection: p, onResizeStart: h, onResize: g, onResizeEnd: m, shouldResize: w }) {
    let x = { ...vu }, j = { ...Ax };
    a = {
      boundaries: d,
      resizeDirection: p,
      keepAspectRatio: f,
      controlDirection: Sa(l)
    };
    let y, v = null, N = [], b, S, E, _ = !1;
    const R = Al().on("start", (D) => {
      const { nodeLookup: I, transform: M, snapGrid: C, snapToGrid: k, nodeOrigin: A, paneDomNode: T } = n();
      if (y = I.get(t), !y)
        return;
      v = T?.getBoundingClientRect() ?? null;
      const { xSnapped: $, ySnapped: P } = jn(D.sourceEvent, {
        transform: M,
        snapGrid: C,
        snapToGrid: k,
        containerBounds: v
      });
      x = {
        width: y.measured.width ?? 0,
        height: y.measured.height ?? 0,
        x: y.position.x ?? 0,
        y: y.position.y ?? 0
      }, j = {
        ...x,
        pointerX: $,
        pointerY: P,
        aspectRatio: x.width / x.height
      }, b = void 0, S = St(y.extent) ? y.extent : void 0, y.parentId && (y.extent === "parent" || y.expandParent) && (b = I.get(y.parentId)), b && y.extent === "parent" && (S = [
        [0, 0],
        [b.measured.width, b.measured.height]
      ]), N = [], E = void 0;
      for (const [F, W] of I)
        if (W.parentId === t && (N.push({
          id: F,
          position: { ...W.position },
          extent: W.extent
        }), W.extent === "parent" || W.expandParent)) {
          const H = _x(W, y, W.origin ?? A);
          E ? E = [
            [Math.min(H[0][0], E[0][0]), Math.min(H[0][1], E[0][1])],
            [Math.max(H[1][0], E[1][0]), Math.max(H[1][1], E[1][1])]
          ] : E = H;
        }
      h?.(D, { ...x });
    }).on("drag", (D) => {
      const { transform: I, snapGrid: M, snapToGrid: C, nodeOrigin: k } = n(), A = jn(D.sourceEvent, {
        transform: I,
        snapGrid: M,
        snapToGrid: C,
        containerBounds: v
      }), T = [];
      if (!y)
        return;
      const { x: $, y: P, width: F, height: W } = x, H = {}, Y = y.origin ?? k, { width: Z, height: ne, x: le, y: G } = kx(j, a.controlDirection, A, a.boundaries, a.keepAspectRatio, Y, S, E), z = Z !== F, X = ne !== W, ae = le !== $ && z, ce = G !== P && X;
      if (!ae && !ce && !z && !X)
        return;
      if ((ae || ce || Y[0] === 1 || Y[1] === 1) && (H.x = ae ? le : x.x, H.y = ce ? G : x.y, x.x = H.x, x.y = H.y, N.length > 0)) {
        const V = le - $, te = G - P;
        for (const ge of N)
          ge.position = {
            x: ge.position.x - V + Y[0] * (Z - F),
            y: ge.position.y - te + Y[1] * (ne - W)
          }, T.push(ge);
      }
      if ((z || X) && (H.width = z && (!a.resizeDirection || a.resizeDirection === "horizontal") ? Z : x.width, H.height = X && (!a.resizeDirection || a.resizeDirection === "vertical") ? ne : x.height, x.width = H.width, x.height = H.height), b && y.expandParent) {
        const V = Y[0] * (H.width ?? 0);
        H.x && H.x < V && (x.x = V, j.x = j.x - (H.x - V));
        const te = Y[1] * (H.height ?? 0);
        H.y && H.y < te && (x.y = te, j.y = j.y - (H.y - te));
      }
      const Q = Ix({
        width: x.width,
        prevWidth: F,
        height: x.height,
        prevHeight: W,
        affectsX: a.controlDirection.affectsX,
        affectsY: a.controlDirection.affectsY
      }), oe = { ...x, direction: Q };
      w?.(D, oe) !== !1 && (_ = !0, g?.(D, oe), i(H, T));
    }).on("end", (D) => {
      _ && (m?.(D, { ...x }), o?.({ ...x }), _ = !1);
    });
    s.call(R);
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
var Ea;
function Tx() {
  if (Ea) return ur;
  Ea = 1;
  var e = Ge;
  function t(f, p) {
    return f === p && (f !== 0 || 1 / f === 1 / p) || f !== f && p !== p;
  }
  var n = typeof Object.is == "function" ? Object.is : t, i = e.useState, o = e.useEffect, s = e.useLayoutEffect, a = e.useDebugValue;
  function c(f, p) {
    var h = p(), g = i({ inst: { value: h, getSnapshot: p } }), m = g[0].inst, w = g[1];
    return s(
      function() {
        m.value = h, m.getSnapshot = p, u(m) && w({ inst: m });
      },
      [f, h, p]
    ), o(
      function() {
        return u(m) && w({ inst: m }), f(function() {
          u(m) && w({ inst: m });
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
var Ia;
function Px() {
  return Ia || (Ia = 1, lr.exports = Tx()), lr.exports;
}
var ka;
function $x() {
  if (ka) return cr;
  ka = 1;
  var e = Ge, t = Px();
  function n(l, d) {
    return l === d && (l !== 0 || 1 / l === 1 / d) || l !== l && d !== d;
  }
  var i = typeof Object.is == "function" ? Object.is : n, o = t.useSyncExternalStore, s = e.useRef, a = e.useEffect, c = e.useMemo, u = e.useDebugValue;
  return cr.useSyncExternalStoreWithSelector = function(l, d, f, p, h) {
    var g = s(null);
    if (g.current === null) {
      var m = { hasValue: !1, value: null };
      g.current = m;
    } else m = g.current;
    g = c(
      function() {
        function x(b) {
          if (!j) {
            if (j = !0, y = b, b = p(b), h !== void 0 && m.hasValue) {
              var S = m.value;
              if (h(S, b))
                return v = S;
            }
            return v = b;
          }
          if (S = v, i(y, b)) return S;
          var E = p(b);
          return h !== void 0 && h(S, E) ? (y = b, S) : (y = b, v = E);
        }
        var j = !1, y, v, N = f === void 0 ? null : f;
        return [
          function() {
            return x(d());
          },
          N === null ? void 0 : function() {
            return x(N());
          }
        ];
      },
      [d, f, p, h]
    );
    var w = o(l, g[0], g[1]);
    return a(
      function() {
        m.hasValue = !0, m.value = w;
      },
      [w]
    ), u(w), w;
  }, cr;
}
var Aa;
function Mx() {
  return Aa || (Aa = 1, ar.exports = $x()), ar.exports;
}
var Rx = Mx();
const zx = /* @__PURE__ */ of(Rx), Lx = {}, _a = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), i = (d, f) => {
    const p = typeof d == "function" ? d(t) : d;
    if (!Object.is(p, t)) {
      const h = t;
      t = f ?? (typeof p != "object" || p === null) ? p : Object.assign({}, t, p), n.forEach((g) => g(t, h));
    }
  }, o = () => t, u = { setState: i, getState: o, getInitialState: () => l, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (Lx ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(i, o, u);
  return u;
}, Vx = (e) => e ? _a(e) : _a, { useDebugValue: Ox } = Ge, { useSyncExternalStoreWithSelector: Hx } = zx, Wx = (e) => e;
function bu(e, t = Wx, n) {
  const i = Hx(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return Ox(i), i;
}
const Da = (e, t) => {
  const n = Vx(e), i = (o, s = t) => bu(n, o, s);
  return Object.assign(i, n), i;
}, Fx = (e, t) => e ? Da(e, t) : Da;
function xe(e, t) {
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
var dr = { exports: {} }, _e = {};
var Ta;
function Bx() {
  if (Ta) return _e;
  Ta = 1;
  var e = Ge;
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
  return _e.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = i, _e.createPortal = function(u, l) {
    var d = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!l || l.nodeType !== 1 && l.nodeType !== 9 && l.nodeType !== 11)
      throw Error(t(299));
    return s(u, l, null, d);
  }, _e.flushSync = function(u) {
    var l = a.T, d = i.p;
    try {
      if (a.T = null, i.p = 2, u) return u();
    } finally {
      a.T = l, i.p = d, i.d.f();
    }
  }, _e.preconnect = function(u, l) {
    typeof u == "string" && (l ? (l = l.crossOrigin, l = typeof l == "string" ? l === "use-credentials" ? l : "" : void 0) : l = null, i.d.C(u, l));
  }, _e.prefetchDNS = function(u) {
    typeof u == "string" && i.d.D(u);
  }, _e.preinit = function(u, l) {
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
  }, _e.preinitModule = function(u, l) {
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
  }, _e.preload = function(u, l) {
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
  }, _e.preloadModule = function(u, l) {
    if (typeof u == "string")
      if (l) {
        var d = c(l.as, l.crossOrigin);
        i.d.m(u, {
          as: typeof l.as == "string" && l.as !== "script" ? l.as : void 0,
          crossOrigin: d,
          integrity: typeof l.integrity == "string" ? l.integrity : void 0
        });
      } else i.d.m(u);
  }, _e.requestFormReset = function(u) {
    i.d.r(u);
  }, _e.unstable_batchedUpdates = function(u, l) {
    return u(l);
  }, _e.useFormState = function(u, l, d) {
    return a.H.useFormState(u, l, d);
  }, _e.useFormStatus = function() {
    return a.H.useHostTransitionStatus();
  }, _e.version = "19.2.7", _e;
}
var Pa;
function Kx() {
  if (Pa) return dr.exports;
  Pa = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), dr.exports = Bx(), dr.exports;
}
var Xx = Kx();
const co = Hr(null), qx = co.Provider, Nu = Oe.error001("react");
function he(e, t) {
  const n = zn(co);
  if (n === null)
    throw new Error(Nu);
  return bu(n, e, t);
}
function we() {
  const e = zn(co);
  if (e === null)
    throw new Error(Nu);
  return de(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const $a = { display: "none" }, Yx = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, ju = "react-flow__node-desc", Su = "react-flow__edge-desc", Ux = "react-flow__aria-live", Zx = (e) => e.ariaLiveMessage, Gx = (e) => e.ariaLabelConfig;
function Jx({ rfId: e }) {
  const t = he(Zx);
  return r.jsx("div", { id: `${Ux}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: Yx, children: t });
}
function Qx({ rfId: e, disableKeyboardA11y: t }) {
  const n = he(Gx);
  return r.jsxs(r.Fragment, { children: [r.jsx("div", { id: `${ju}-${e}`, style: $a, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), r.jsx("div", { id: `${Su}-${e}`, style: $a, children: n["edge.a11yDescription.default"] }), !t && r.jsx(Jx, { rfId: e })] });
}
const lo = Tc(({ position: e = "top-left", children: t, className: n, style: i, ...o }, s) => {
  const a = `${e}`.split("-");
  return r.jsx("div", { className: Se(["react-flow__panel", n, ...a]), style: i, ref: s, ...o, children: t });
});
lo.displayName = "Panel";
function ew({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : r.jsx(lo, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: r.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const tw = (e) => {
  const t = [], n = [];
  for (const [, i] of e.nodeLookup)
    i.selected && t.push(i.internals.userNode);
  for (const [, i] of e.edgeLookup)
    i.selected && n.push(i);
  return { selectedNodes: t, selectedEdges: n };
}, gi = (e) => e.id;
function nw(e, t) {
  return xe(e.selectedNodes.map(gi), t.selectedNodes.map(gi)) && xe(e.selectedEdges.map(gi), t.selectedEdges.map(gi));
}
function iw({ onSelectionChange: e }) {
  const t = we(), { selectedNodes: n, selectedEdges: i } = he(tw, nw);
  return J(() => {
    const o = { nodes: n, edges: i };
    e?.(o), t.getState().onSelectionChangeHandlers.forEach((s) => s(o));
  }, [n, i, e]), null;
}
const ow = (e) => !!e.onSelectionChangeHandlers;
function rw({ onSelectionChange: e }) {
  const t = he(ow);
  return e || t ? r.jsx(iw, { onSelectionChange: e }) : null;
}
const Cu = [0, 0], sw = { x: 0, y: 0, zoom: 1 }, aw = [
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
], Ma = [...aw, "rfId"], cw = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), Ra = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: Tn,
  nodeOrigin: Cu,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function lw(e) {
  const { setNodes: t, setEdges: n, setMinZoom: i, setMaxZoom: o, setTranslateExtent: s, setNodeExtent: a, reset: c, setDefaultNodesAndEdges: u } = he(cw, xe), l = we();
  J(() => (u(e.defaultNodes, e.defaultEdges), () => {
    d.current = Ra, c();
  }), []);
  const d = re(Ra);
  return J(
    () => {
      for (const f of Ma) {
        const p = e[f], h = d.current[f];
        p !== h && (typeof e[f] > "u" || (f === "nodes" ? t(p) : f === "edges" ? n(p) : f === "minZoom" ? i(p) : f === "maxZoom" ? o(p) : f === "translateExtent" ? s(p) : f === "nodeExtent" ? a(p) : f === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: Bm(p) }) : f === "fitView" ? l.setState({ fitViewQueued: p }) : f === "fitViewOptions" ? l.setState({ fitViewOptions: p }) : l.setState({ [f]: p })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    Ma.map((f) => e[f])
  ), null;
}
function za() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function uw(e) {
  const [t, n] = B(e === "system" ? null : e);
  return J(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const i = za(), o = () => n(i?.matches ? "dark" : "light");
    return o(), i?.addEventListener("change", o), () => {
      i?.removeEventListener("change", o);
    };
  }, [e]), t !== null ? t : za()?.matches ? "dark" : "light";
}
const La = typeof document < "u" ? document : null;
function Rn(e = null, t = { target: La, actInsideInputWithModifier: !0 }) {
  const [n, i] = B(!1), o = re(!1), s = re(/* @__PURE__ */ new Set([])), [a, c] = de(() => {
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
  return J(() => {
    const u = t?.target ?? La, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (h) => {
        if (o.current = h.ctrlKey || h.metaKey || h.shiftKey || h.altKey, (!o.current || o.current && !l) && iu(h))
          return !1;
        const m = Oa(h.code, c);
        if (s.current.add(h[m]), Va(a, s.current, !1)) {
          const w = h.composedPath?.()?.[0] || h.target, x = w?.nodeName === "BUTTON" || w?.nodeName === "A";
          t.preventDefault !== !1 && (o.current || !x) && h.preventDefault(), i(!0);
        }
      }, f = (h) => {
        const g = Oa(h.code, c);
        Va(a, s.current, !0) ? (i(!1), s.current.clear()) : s.current.delete(h[g]), h.key === "Meta" && s.current.clear(), o.current = !1;
      }, p = () => {
        s.current.clear(), i(!1);
      };
      return u?.addEventListener("keydown", d), u?.addEventListener("keyup", f), window.addEventListener("blur", p), window.addEventListener("contextmenu", p), () => {
        u?.removeEventListener("keydown", d), u?.removeEventListener("keyup", f), window.removeEventListener("blur", p), window.removeEventListener("contextmenu", p);
      };
    }
  }, [e, i]), n;
}
function Va(e, t, n) {
  return e.filter((i) => n || i.length === t.size).some((i) => i.every((o) => t.has(o)));
}
function Oa(e, t) {
  return t.includes(e) ? "code" : "key";
}
const dw = () => {
  const e = we();
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
      const { width: i, height: o, minZoom: s, maxZoom: a, panZoom: c } = e.getState(), u = rs(t, i, o, s, a, n?.padding ?? 0.1);
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
      return rn(l, i, f, d);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: i } = e.getState();
      if (!i)
        return t;
      const { x: o, y: s } = i.getBoundingClientRect(), a = Jt(t, n);
      return {
        x: a.x + o,
        y: a.y + s
      };
    }
  }), []);
};
function Eu(e, t) {
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
      fw(u, c);
    n.push(c);
  }
  return o.length && o.forEach((s) => {
    s.index !== void 0 ? n.splice(s.index, 0, { ...s.item }) : n.push({ ...s.item });
  }), n;
}
function fw(e, t) {
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
function Iu(e, t) {
  return Eu(e, t);
}
function ku(e, t) {
  return Eu(e, t);
}
function gt(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function Lt(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const i = [];
  for (const [o, s] of e) {
    const a = t.has(o);
    !(s.selected === void 0 && !a) && s.selected !== a && (n && (s.selected = a), i.push(gt(s.id, a)));
  }
  return i;
}
function Ha({ items: e = [], lookup: t }) {
  const n = [], i = new Map(e.map((o) => [o.id, o]));
  for (const [o, s] of e.entries()) {
    const a = t.get(s.id), c = a?.internals?.userNode ?? a;
    c !== void 0 && c !== s && n.push({ id: s.id, item: s, type: "replace" }), c === void 0 && n.push({ item: s, type: "add", index: o });
  }
  for (const [o] of t)
    i.get(o) === void 0 && n.push({ id: o, type: "remove" });
  return n;
}
function Wa(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const Au = Ql();
function _u(e, t, n = {}) {
  return Um(e, t, {
    ...n,
    onError: n.onError ?? Au
  });
}
function pw(e, t, n, i = { shouldReplaceId: !0 }) {
  return Zm(e, t, n, {
    ...i,
    onError: i.onError ?? Au
  });
}
const Fa = (e) => Mm(e), hw = (e) => Ul(e);
function Du(e) {
  return Tc(e);
}
const gw = typeof window < "u" ? Vd : J;
function Ba(e) {
  const [t, n] = B(BigInt(0)), [i] = B(() => yw(() => n((o) => o + BigInt(1))));
  return gw(() => {
    const o = i.get();
    o.length && (e(o), i.reset());
  }, [t]), i;
}
function yw(e) {
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
const Tu = Hr(null);
function mw({ children: e }) {
  const t = we(), n = se((c) => {
    const { nodes: u = [], setNodes: l, hasDefaultNodes: d, onNodesChange: f, nodeLookup: p, fitViewQueued: h, onNodesChangeMiddlewareMap: g } = t.getState();
    let m = u;
    for (const x of c)
      m = typeof x == "function" ? x(m) : x;
    let w = Ha({
      items: m,
      lookup: p
    });
    for (const x of g.values())
      w = x(w);
    d && l(m), w.length > 0 ? f?.(w) : h && window.requestAnimationFrame(() => {
      const { fitViewQueued: x, nodes: j, setNodes: y } = t.getState();
      x && y(j);
    });
  }, []), i = Ba(n), o = se((c) => {
    const { edges: u = [], setEdges: l, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: p } = t.getState();
    let h = u;
    for (const g of c)
      h = typeof g == "function" ? g(h) : g;
    d ? l(h) : f && f(Ha({
      items: h,
      lookup: p
    }));
  }, []), s = Ba(o), a = de(() => ({ nodeQueue: i, edgeQueue: s }), []);
  return r.jsx(Tu.Provider, { value: a, children: e });
}
function xw() {
  const e = zn(Tu);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const ww = (e) => !!e.panZoom;
function fs() {
  const e = dw(), t = we(), n = xw(), i = he(ww), o = de(() => {
    const s = (f) => t.getState().nodeLookup.get(f), a = (f) => {
      n.nodeQueue.push(f);
    }, c = (f) => {
      n.edgeQueue.push(f);
    }, u = (f) => {
      const { nodeLookup: p, nodeOrigin: h } = t.getState(), g = Fa(f) ? f : p.get(f.id), m = g.parentId ? tu(g.position, g.measured, g.parentId, p, h) : g.position, w = {
        ...g,
        position: m,
        width: g.measured?.width ?? g.width,
        height: g.measured?.height ?? g.height
      };
      return Gt(w);
    }, l = (f, p, h = { replace: !1 }) => {
      a((g) => g.map((m) => {
        if (m.id === f) {
          const w = typeof p == "function" ? p(m) : p;
          return h.replace && Fa(w) ? w : { ...m, ...w };
        }
        return m;
      }));
    }, d = (f, p, h = { replace: !1 }) => {
      c((g) => g.map((m) => {
        if (m.id === f) {
          const w = typeof p == "function" ? p(m) : p;
          return h.replace && hw(w) ? w : { ...m, ...w };
        }
        return m;
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
        const { nodes: f = [], edges: p = [], transform: h } = t.getState(), [g, m, w] = h;
        return {
          nodes: f.map((x) => ({ ...x })),
          edges: p.map((x) => ({ ...x })),
          viewport: {
            x: g,
            y: m,
            zoom: w
          }
        };
      },
      deleteElements: async ({ nodes: f = [], edges: p = [] }) => {
        const { nodes: h, edges: g, onNodesDelete: m, onEdgesDelete: w, triggerNodeChanges: x, triggerEdgeChanges: j, onDelete: y, onBeforeDelete: v } = t.getState(), { nodes: N, edges: b } = await Om({
          nodesToRemove: f,
          edgesToRemove: p,
          nodes: h,
          edges: g,
          onBeforeDelete: v
        }), S = b.length > 0, E = N.length > 0;
        if (S) {
          const _ = b.map(Wa);
          w?.(b), j(_);
        }
        if (E) {
          const _ = N.map(Wa);
          m?.(N), x(_);
        }
        return (E || S) && y?.({ nodes: N, edges: b }), { deletedNodes: N, deletedEdges: b };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, p = !0, h) => {
        const g = pa(f), m = g ? f : u(f), w = h !== void 0;
        return m ? (h || t.getState().nodes).filter((x) => {
          const j = t.getState().nodeLookup.get(x.id);
          if (j && !g && (x.id === f.id || !j.internals.positionAbsolute))
            return !1;
          const y = Gt(w ? x : j), v = $n(y, m);
          return p && v > 0 || v >= y.width * y.height || v >= m.width * m.height;
        }) : [];
      },
      isNodeIntersecting: (f, p, h = !0) => {
        const m = pa(f) ? f : u(f);
        if (!m)
          return !1;
        const w = $n(m, p);
        return h && w > 0 || w >= p.width * p.height || w >= m.width * m.height;
      },
      updateNode: l,
      updateNodeData: (f, p, h = { replace: !1 }) => {
        l(f, (g) => {
          const m = typeof p == "function" ? p(g) : p;
          return h.replace ? { ...g, data: m } : { ...g, data: { ...g.data, ...m } };
        }, h);
      },
      updateEdge: d,
      updateEdgeData: (f, p, h = { replace: !1 }) => {
        d(f, (g) => {
          const m = typeof p == "function" ? p(g) : p;
          return h.replace ? { ...g, data: m } : { ...g, data: { ...g.data, ...m } };
        }, h);
      },
      getNodesBounds: (f) => {
        const { nodeLookup: p, nodeOrigin: h } = t.getState();
        return Rm(f, { nodeLookup: p, nodeOrigin: h });
      },
      getHandleConnections: ({ type: f, id: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}-${f}${p ? `-${p}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}${f ? p ? `-${f}-${p}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const p = t.getState().fitViewResolver ?? Fm();
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
const Ka = (e) => e.selected, vw = typeof window < "u" ? window : void 0;
function bw({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = we(), { deleteElements: i } = fs(), o = Rn(e, { actInsideInputWithModifier: !1 }), s = Rn(t, { target: vw });
  J(() => {
    if (o) {
      const { edges: a, nodes: c } = n.getState();
      i({ nodes: c.filter(Ka), edges: a.filter(Ka) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [o]), J(() => {
    n.setState({ multiSelectionActive: s });
  }, [s]);
}
function Nw(e) {
  const t = we();
  J(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const i = ss(e.current);
      (i.height === 0 || i.width === 0) && t.getState().onError?.("004", Oe.error004()), t.setState({ width: i.width || 500, height: i.height || 500 });
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
const uo = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, jw = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function Sw({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: i = !1, panOnScrollSpeed: o = 0.5, panOnScrollMode: s = vt.Free, zoomOnDoubleClick: a = !0, panOnDrag: c = !0, defaultViewport: u, translateExtent: l, minZoom: d, maxZoom: f, zoomActivationKeyCode: p, preventScrolling: h = !0, children: g, noWheelClassName: m, noPanClassName: w, onViewportChange: x, isControlledViewport: j, paneClickDistance: y, selectionOnDrag: v }) {
  const N = we(), b = re(null), { userSelectionActive: S, lib: E, connectionInProgress: _ } = he(jw, xe), R = Rn(p), D = re();
  Nw(b);
  const I = se((M) => {
    x?.({ x: M[0], y: M[1], zoom: M[2] }), j || N.setState({ transform: M });
  }, [x, j]);
  return J(() => {
    if (b.current) {
      D.current = Ex({
        domNode: b.current,
        minZoom: d,
        maxZoom: f,
        translateExtent: l,
        viewport: u,
        onDraggingChange: (A) => N.setState((T) => T.paneDragging === A ? T : { paneDragging: A }),
        onPanZoomStart: (A, T) => {
          const { onViewportChangeStart: $, onMoveStart: P } = N.getState();
          P?.(A, T), $?.(T);
        },
        onPanZoom: (A, T) => {
          const { onViewportChange: $, onMove: P } = N.getState();
          P?.(A, T), $?.(T);
        },
        onPanZoomEnd: (A, T) => {
          const { onViewportChangeEnd: $, onMoveEnd: P } = N.getState();
          P?.(A, T), $?.(T);
        }
      });
      const { x: M, y: C, zoom: k } = D.current.getViewport();
      return N.setState({
        panZoom: D.current,
        transform: [M, C, k],
        domNode: b.current.closest(".react-flow")
      }), () => {
        D.current?.destroy();
      };
    }
  }, []), J(() => {
    D.current?.update({
      onPaneContextMenu: e,
      zoomOnScroll: t,
      zoomOnPinch: n,
      panOnScroll: i,
      panOnScrollSpeed: o,
      panOnScrollMode: s,
      zoomOnDoubleClick: a,
      panOnDrag: c,
      zoomActivationKeyPressed: R,
      preventScrolling: h,
      noPanClassName: w,
      userSelectionActive: S,
      noWheelClassName: m,
      lib: E,
      onTransformChange: I,
      connectionInProgress: _,
      selectionOnDrag: v,
      paneClickDistance: y
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
    R,
    h,
    w,
    S,
    m,
    E,
    I,
    _,
    v,
    y
  ]), r.jsx("div", { className: "react-flow__renderer", ref: b, style: uo, children: g });
}
const Cw = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function Ew() {
  const { userSelectionActive: e, userSelectionRect: t } = he(Cw, xe);
  return e && t ? r.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const fr = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, Iw = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function kw({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Pn.Full, panOnDrag: i, autoPanOnSelection: o, paneClickDistance: s, selectionOnDrag: a, onSelectionStart: c, onSelectionEnd: u, onPaneClick: l, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: p, onPaneMouseMove: h, onPaneMouseLeave: g, children: m }) {
  const w = re(0), x = we(), { userSelectionActive: j, elementsSelectable: y, dragging: v, connectionInProgress: N, panBy: b, autoPanSpeed: S } = he(Iw, xe), E = y && (e || j), _ = re(null), R = re(), D = re(/* @__PURE__ */ new Set()), I = re(/* @__PURE__ */ new Set()), M = re(!1), C = re({ x: 0, y: 0 }), k = re(!1), A = (z) => {
    if (M.current || N) {
      M.current = !1;
      return;
    }
    l?.(z), x.getState().resetSelectedElements(), x.setState({ nodesSelectionActive: !1 });
  }, T = (z) => {
    if (Array.isArray(i) && i?.includes(2)) {
      z.preventDefault();
      return;
    }
    d?.(z);
  }, $ = f ? (z) => f(z) : void 0, P = (z) => {
    M.current && (z.stopPropagation(), M.current = !1);
  }, F = (z) => {
    const { domNode: X, transform: ae } = x.getState();
    if (R.current = X?.getBoundingClientRect(), !R.current)
      return;
    const ce = z.target === _.current;
    if (!ce && !!z.target.closest(".nokey") || !e || !(a && ce || t) || z.button !== 0 || !z.isPrimary)
      return;
    z.target?.setPointerCapture?.(z.pointerId), M.current = !1;
    const { x: fe, y: V } = Xe(z.nativeEvent, R.current), te = rn({ x: fe, y: V }, ae);
    x.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: te.x,
        startY: te.y,
        x: fe,
        y: V
      }
    }), ce || (z.stopPropagation(), z.preventDefault());
  };
  function W(z, X) {
    const { userSelectionRect: ae } = x.getState();
    if (!ae)
      return;
    const { transform: ce, nodeLookup: Q, edgeLookup: oe, connectionLookup: fe, triggerNodeChanges: V, triggerEdgeChanges: te, defaultEdgeOptions: ge } = x.getState(), me = { x: ae.startX, y: ae.startY }, { x: Te, y: Ie } = Jt(me, ce), Ae = {
      startX: me.x,
      startY: me.y,
      x: z < Te ? z : Te,
      y: X < Ie ? X : Ie,
      width: Math.abs(z - Te),
      height: Math.abs(X - Ie)
    }, tt = D.current, We = I.current;
    D.current = new Set(is(Q, Ae, ce, n === Pn.Partial, !0).map((O) => O.id)), I.current = /* @__PURE__ */ new Set();
    const L = ge?.selectable ?? !0;
    for (const O of D.current) {
      const K = fe.get(O);
      if (K)
        for (const { edgeId: q } of K.values()) {
          const ee = oe.get(q);
          ee && (ee.selectable ?? L) && I.current.add(q);
        }
    }
    if (!ha(tt, D.current)) {
      const O = Lt(Q, D.current, !0);
      V(O);
    }
    if (!ha(We, I.current)) {
      const O = Lt(oe, I.current);
      te(O);
    }
    x.setState({
      userSelectionRect: Ae,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function H() {
    if (!o || !R.current)
      return;
    const [z, X] = os(C.current, R.current, S);
    b({ x: z, y: X }).then((ae) => {
      if (!M.current || !ae) {
        w.current = requestAnimationFrame(H);
        return;
      }
      const { x: ce, y: Q } = C.current;
      W(ce, Q), w.current = requestAnimationFrame(H);
    });
  }
  const Y = () => {
    cancelAnimationFrame(w.current), w.current = 0, k.current = !1;
  };
  J(() => () => Y(), []);
  const Z = (z) => {
    const { userSelectionRect: X, transform: ae, resetSelectedElements: ce } = x.getState();
    if (!R.current || !X)
      return;
    const { x: Q, y: oe } = Xe(z.nativeEvent, R.current);
    C.current = { x: Q, y: oe };
    const fe = Jt({ x: X.startX, y: X.startY }, ae);
    if (!M.current) {
      const V = t ? 0 : s;
      if (Math.hypot(Q - fe.x, oe - fe.y) <= V)
        return;
      ce(), c?.(z);
    }
    M.current = !0, k.current || (H(), k.current = !0), W(Q, oe);
  }, ne = (z) => {
    z.button === 0 && (z.target?.releasePointerCapture?.(z.pointerId), !j && z.target === _.current && x.getState().userSelectionRect && A?.(z), x.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), M.current && (u?.(z), x.setState({
      nodesSelectionActive: D.current.size > 0
    })), Y());
  }, le = (z) => {
    z.target?.releasePointerCapture?.(z.pointerId), Y();
  }, G = i === !0 || Array.isArray(i) && i.includes(0);
  return r.jsxs("div", { className: Se(["react-flow__pane", { draggable: G, dragging: v, selection: e }]), onClick: E ? void 0 : fr(A, _), onContextMenu: fr(T, _), onWheel: fr($, _), onPointerEnter: E ? void 0 : p, onPointerMove: E ? Z : h, onPointerUp: E ? ne : void 0, onPointerCancel: E ? le : void 0, onPointerDownCapture: E ? F : void 0, onClickCapture: E ? P : void 0, onPointerLeave: g, ref: _, style: uo, children: [m, r.jsx(Ew, {})] });
}
function Pr({ id: e, store: t, unselect: n = !1, nodeRef: i }) {
  const { addSelectedNodes: o, unselectNodesAndEdges: s, multiSelectionActive: a, nodeLookup: c, onError: u } = t.getState(), l = c.get(e);
  if (!l) {
    u?.("012", Oe.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && a) && (s({ nodes: [l], edges: [] }), requestAnimationFrame(() => i?.current?.blur())) : o([e]);
}
function Pu({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: i, nodeId: o, isSelectable: s, nodeClickDistance: a }) {
  const c = we(), [u, l] = B(!1), d = re();
  return J(() => {
    d.current = fx({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (f) => {
        Pr({
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
  }, []), J(() => {
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
const Aw = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function $u() {
  const e = we();
  return se((n) => {
    const { nodeExtent: i, snapToGrid: o, snapGrid: s, nodesDraggable: a, onError: c, updateNodePositions: u, nodeLookup: l, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), p = Aw(a), h = o ? s[0] : 5, g = o ? s[1] : 5, m = n.direction.x * h * n.factor, w = n.direction.y * g * n.factor;
    for (const [, x] of l) {
      if (!p(x))
        continue;
      let j = {
        x: x.internals.positionAbsolute.x + m,
        y: x.internals.positionAbsolute.y + w
      };
      o && (j = Bn(j, s));
      const { position: y, positionAbsolute: v } = Zl({
        nodeId: x.id,
        nextPosition: j,
        nodeLookup: l,
        nodeExtent: i,
        nodeOrigin: d,
        onError: c
      });
      x.position = y, x.internals.positionAbsolute = v, f.set(x.id, x);
    }
    u(f);
  }, []);
}
const ps = Hr(null), _w = ps.Provider;
ps.Consumer;
const Mu = () => zn(ps), Dw = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), Tw = (e, t, n) => (i) => {
  const { connectionClickStartHandle: o, connectionMode: s, connection: a } = i, { fromHandle: c, toHandle: u, isValid: l } = a, d = u?.nodeId === e && u?.id === t && u?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: d,
    clickConnecting: o?.nodeId === e && o?.id === t && o?.type === n,
    isPossibleEndHandle: s === Ut.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!o,
    valid: d && l
  };
};
function Pw({ type: e = "source", position: t = ie.Top, isValidConnection: n, isConnectable: i = !0, isConnectableStart: o = !0, isConnectableEnd: s = !0, id: a, onConnect: c, children: u, className: l, onMouseDown: d, onTouchStart: f, ...p }, h) {
  const g = a || null, m = e === "target", w = we(), x = Mu(), { connectOnClick: j, noPanClassName: y, rfId: v } = he(Dw, xe), { connectingFrom: N, connectingTo: b, clickConnecting: S, isPossibleEndHandle: E, connectionInProcess: _, clickConnectionInProcess: R, valid: D } = he(Tw(x, g, e), xe);
  x || w.getState().onError?.("010", Oe.error010());
  const I = (k) => {
    const { defaultEdgeOptions: A, onConnect: T, hasDefaultEdges: $ } = w.getState(), P = {
      ...A,
      ...k
    };
    if ($) {
      const { edges: F, setEdges: W, onError: H } = w.getState();
      W(_u(P, F, { onError: H }));
    }
    T?.(P), c?.(P);
  }, M = (k) => {
    if (!x)
      return;
    const A = ou(k.nativeEvent);
    if (o && (A && k.button === 0 || !A)) {
      const T = w.getState();
      Tr.onPointerDown(k.nativeEvent, {
        handleDomNode: k.currentTarget,
        autoPanOnConnect: T.autoPanOnConnect,
        connectionMode: T.connectionMode,
        connectionRadius: T.connectionRadius,
        domNode: T.domNode,
        nodeLookup: T.nodeLookup,
        lib: T.lib,
        isTarget: m,
        handleId: g,
        nodeId: x,
        flowId: T.rfId,
        panBy: T.panBy,
        cancelConnection: T.cancelConnection,
        onConnectStart: T.onConnectStart,
        onConnectEnd: (...$) => w.getState().onConnectEnd?.(...$),
        updateConnection: T.updateConnection,
        onConnect: I,
        isValidConnection: n || ((...$) => w.getState().isValidConnection?.(...$) ?? !0),
        getTransform: () => w.getState().transform,
        getFromHandle: () => w.getState().connection.fromHandle,
        autoPanSpeed: T.autoPanSpeed,
        dragThreshold: T.connectionDragThreshold
      });
    }
    A ? d?.(k) : f?.(k);
  }, C = (k) => {
    const { onClickConnectStart: A, onClickConnectEnd: T, connectionClickStartHandle: $, connectionMode: P, isValidConnection: F, lib: W, rfId: H, nodeLookup: Y, connection: Z } = w.getState();
    if (!x || !$ && !o)
      return;
    if (!$) {
      A?.(k.nativeEvent, { nodeId: x, handleId: g, handleType: e }), w.setState({ connectionClickStartHandle: { nodeId: x, type: e, id: g } });
      return;
    }
    const ne = nu(k.target), le = n || F, { connection: G, isValid: z } = Tr.isValid(k.nativeEvent, {
      handle: {
        nodeId: x,
        id: g,
        type: e
      },
      connectionMode: P,
      fromNodeId: $.nodeId,
      fromHandleId: $.id || null,
      fromType: $.type,
      isValidConnection: le,
      flowId: H,
      doc: ne,
      lib: W,
      nodeLookup: Y
    });
    z && G && I(G);
    const X = structuredClone(Z);
    delete X.inProgress, X.toPosition = X.toHandle ? X.toHandle.position : null, T?.(k, X), w.setState({ connectionClickStartHandle: null });
  };
  return r.jsx("div", { "data-handleid": g, "data-nodeid": x, "data-handlepos": t, "data-id": `${v}-${x}-${g}-${e}`, className: Se([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    y,
    l,
    {
      source: !m,
      target: m,
      connectable: i,
      connectablestart: o,
      connectableend: s,
      clickconnecting: S,
      connectingfrom: N,
      connectingto: b,
      valid: D,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: i && (!_ || E) && (_ || R ? s : o)
    }
  ]), onMouseDown: M, onTouchStart: M, onClick: j ? C : void 0, ref: h, ...p, children: u });
}
const en = Ne(Du(Pw));
function $w({ data: e, isConnectable: t, sourcePosition: n = ie.Bottom }) {
  return r.jsxs(r.Fragment, { children: [e?.label, r.jsx(en, { type: "source", position: n, isConnectable: t })] });
}
function Mw({ data: e, isConnectable: t, targetPosition: n = ie.Top, sourcePosition: i = ie.Bottom }) {
  return r.jsxs(r.Fragment, { children: [r.jsx(en, { type: "target", position: n, isConnectable: t }), e?.label, r.jsx(en, { type: "source", position: i, isConnectable: t })] });
}
function Rw() {
  return null;
}
function zw({ data: e, isConnectable: t, targetPosition: n = ie.Top }) {
  return r.jsxs(r.Fragment, { children: [r.jsx(en, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const qi = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, Xa = {
  input: $w,
  default: Mw,
  output: zw,
  group: Rw
};
function Lw(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const Vw = (e) => {
  const { width: t, height: n, x: i, y: o } = Fn(e.nodeLookup, {
    filter: (s) => !!s.selected
  });
  return {
    width: Ke(t) ? t : null,
    height: Ke(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${i}px,${o}px)`
  };
};
function Ow({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const i = we(), { width: o, height: s, transformString: a, userSelectionActive: c } = he(Vw, xe), u = $u(), l = re(null);
  J(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !c && o !== null && s !== null;
  if (Pu({
    nodeRef: l,
    disabled: !d
  }), !d)
    return null;
  const f = e ? (h) => {
    const g = i.getState().nodes.filter((m) => m.selected);
    e(h, g);
  } : void 0, p = (h) => {
    Object.prototype.hasOwnProperty.call(qi, h.key) && (h.preventDefault(), u({
      direction: qi[h.key],
      factor: h.shiftKey ? 4 : 1
    }));
  };
  return r.jsx("div", { className: Se(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: a
  }, children: r.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : p, style: {
    width: o,
    height: s
  } }) });
}
const qa = typeof window < "u" ? window : void 0, Hw = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function Ru({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: i, onPaneMouseLeave: o, onPaneContextMenu: s, onPaneScroll: a, paneClickDistance: c, deleteKeyCode: u, selectionKeyCode: l, selectionOnDrag: d, selectionMode: f, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: g, panActivationKeyCode: m, zoomActivationKeyCode: w, elementsSelectable: x, zoomOnScroll: j, zoomOnPinch: y, panOnScroll: v, panOnScrollSpeed: N, panOnScrollMode: b, zoomOnDoubleClick: S, panOnDrag: E, autoPanOnSelection: _, defaultViewport: R, translateExtent: D, minZoom: I, maxZoom: M, preventScrolling: C, onSelectionContextMenu: k, noWheelClassName: A, noPanClassName: T, disableKeyboardA11y: $, onViewportChange: P, isControlledViewport: F }) {
  const { nodesSelectionActive: W, userSelectionActive: H } = he(Hw, xe), Y = Rn(l, { target: qa }), Z = Rn(m, { target: qa }), ne = Z || E, le = Z || v, G = d && ne !== !0, z = Y || H || G;
  return bw({ deleteKeyCode: u, multiSelectionKeyCode: g }), r.jsx(Sw, { onPaneContextMenu: s, elementsSelectable: x, zoomOnScroll: j, zoomOnPinch: y, panOnScroll: le, panOnScrollSpeed: N, panOnScrollMode: b, zoomOnDoubleClick: S, panOnDrag: !Y && ne, defaultViewport: R, translateExtent: D, minZoom: I, maxZoom: M, zoomActivationKeyCode: w, preventScrolling: C, noWheelClassName: A, noPanClassName: T, onViewportChange: P, isControlledViewport: F, paneClickDistance: c, selectionOnDrag: G, children: r.jsxs(kw, { onSelectionStart: p, onSelectionEnd: h, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: i, onPaneMouseLeave: o, onPaneContextMenu: s, onPaneScroll: a, panOnDrag: ne, autoPanOnSelection: _, isSelecting: !!z, selectionMode: f, selectionKeyPressed: Y, paneClickDistance: c, selectionOnDrag: G, children: [e, W && r.jsx(Ow, { onSelectionContextMenu: k, noPanClassName: T, disableKeyboardA11y: $ })] }) });
}
Ru.displayName = "FlowRenderer";
const Ww = Ne(Ru), Fw = (e) => (t) => e ? is(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function Bw(e) {
  return he(se(Fw(e), [e]), xe);
}
const Kw = (e) => e.updateNodeInternals;
function Xw() {
  const e = he(Kw), [t] = B(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
  return J(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function qw({ node: e, nodeType: t, hasDimensions: n, resizeObserver: i }) {
  const o = we(), s = re(null), a = re(null), c = re(e.sourcePosition), u = re(e.targetPosition), l = re(t), d = n && !!e.internals.handleBounds;
  return J(() => {
    s.current && !e.hidden && (!d || a.current !== s.current) && (a.current && i?.unobserve(a.current), i?.observe(s.current), a.current = s.current);
  }, [d, e.hidden]), J(() => () => {
    a.current && (i?.unobserve(a.current), a.current = null);
  }, []), J(() => {
    if (s.current) {
      const f = l.current !== t, p = c.current !== e.sourcePosition, h = u.current !== e.targetPosition;
      (f || p || h) && (l.current = t, c.current = e.sourcePosition, u.current = e.targetPosition, o.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: s.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), s;
}
function Yw({ id: e, onClick: t, onMouseEnter: n, onMouseMove: i, onMouseLeave: o, onContextMenu: s, onDoubleClick: a, nodesDraggable: c, elementsSelectable: u, nodesConnectable: l, nodesFocusable: d, resizeObserver: f, noDragClassName: p, noPanClassName: h, disableKeyboardA11y: g, rfId: m, nodeTypes: w, nodeClickDistance: x, onError: j }) {
  const { node: y, internals: v, isParent: N } = he((z) => {
    const X = z.nodeLookup.get(e), ae = z.parentLookup.has(e);
    return {
      node: X,
      internals: X.internals,
      isParent: ae
    };
  }, xe);
  let b = y.type || "default", S = w?.[b] || Xa[b];
  S === void 0 && (j?.("003", Oe.error003(b)), b = "default", S = w?.default || Xa.default);
  const E = !!(y.draggable || c && typeof y.draggable > "u"), _ = !!(y.selectable || u && typeof y.selectable > "u"), R = !!(y.connectable || l && typeof y.connectable > "u"), D = !!(y.focusable || d && typeof y.focusable > "u"), I = we(), M = eu(y), C = qw({ node: y, nodeType: b, hasDimensions: M, resizeObserver: f }), k = Pu({
    nodeRef: C,
    disabled: y.hidden || !E,
    noDragClassName: p,
    handleSelector: y.dragHandle,
    nodeId: e,
    isSelectable: _,
    nodeClickDistance: x
  }), A = $u();
  if (y.hidden)
    return null;
  const T = at(y), $ = Lw(y), P = _ || E || t || n || i || o, F = n ? (z) => n(z, { ...v.userNode }) : void 0, W = i ? (z) => i(z, { ...v.userNode }) : void 0, H = o ? (z) => o(z, { ...v.userNode }) : void 0, Y = s ? (z) => s(z, { ...v.userNode }) : void 0, Z = a ? (z) => a(z, { ...v.userNode }) : void 0, ne = (z) => {
    const { selectNodesOnDrag: X, nodeDragThreshold: ae } = I.getState();
    _ && (!X || !E || ae > 0) && Pr({
      id: e,
      store: I,
      nodeRef: C
    }), t && t(z, { ...v.userNode });
  }, le = (z) => {
    if (!(iu(z.nativeEvent) || g)) {
      if (Kl.includes(z.key) && _) {
        const X = z.key === "Escape";
        Pr({
          id: e,
          store: I,
          unselect: X,
          nodeRef: C
        });
      } else if (E && y.selected && Object.prototype.hasOwnProperty.call(qi, z.key)) {
        z.preventDefault();
        const { ariaLabelConfig: X } = I.getState();
        I.setState({
          ariaLiveMessage: X["node.a11yDescription.ariaLiveMessage"]({
            direction: z.key.replace("Arrow", "").toLowerCase(),
            x: ~~v.positionAbsolute.x,
            y: ~~v.positionAbsolute.y
          })
        }), A({
          direction: qi[z.key],
          factor: z.shiftKey ? 4 : 1
        });
      }
    }
  }, G = () => {
    if (g || !C.current?.matches(":focus-visible"))
      return;
    const { transform: z, width: X, height: ae, autoPanOnNodeFocus: ce, setCenter: Q } = I.getState();
    if (!ce)
      return;
    is(/* @__PURE__ */ new Map([[e, y]]), { x: 0, y: 0, width: X, height: ae }, z, !0).length > 0 || Q(y.position.x + T.width / 2, y.position.y + T.height / 2, {
      zoom: z[2]
    });
  };
  return r.jsx("div", { className: Se([
    "react-flow__node",
    `react-flow__node-${b}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [h]: E
    },
    y.className,
    {
      selected: y.selected,
      selectable: _,
      parent: N,
      draggable: E,
      dragging: k
    }
  ]), ref: C, style: {
    zIndex: v.z,
    transform: `translate(${v.positionAbsolute.x}px,${v.positionAbsolute.y}px)`,
    pointerEvents: P ? "all" : "none",
    visibility: M ? "visible" : "hidden",
    ...y.style,
    ...$
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: F, onMouseMove: W, onMouseLeave: H, onContextMenu: Y, onClick: ne, onDoubleClick: Z, onKeyDown: D ? le : void 0, tabIndex: D ? 0 : void 0, onFocus: D ? G : void 0, role: y.ariaRole ?? (D ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": g ? void 0 : `${ju}-${m}`, "aria-label": y.ariaLabel, ...y.domAttributes, children: r.jsx(_w, { value: e, children: r.jsx(S, { id: e, data: y.data, type: b, positionAbsoluteX: v.positionAbsolute.x, positionAbsoluteY: v.positionAbsolute.y, selected: y.selected ?? !1, selectable: _, draggable: E, deletable: y.deletable ?? !0, isConnectable: R, sourcePosition: y.sourcePosition, targetPosition: y.targetPosition, dragging: k, dragHandle: y.dragHandle, zIndex: v.z, parentId: y.parentId, ...T }) }) });
}
var Uw = Ne(Yw);
const Zw = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function zu(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: i, elementsSelectable: o, onError: s } = he(Zw, xe), a = Bw(e.onlyRenderVisibleElements), c = Xw();
  return r.jsx("div", { className: "react-flow__nodes", style: uo, children: a.map((u) => (
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
    r.jsx(Uw, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: i, elementsSelectable: o, nodeClickDistance: e.nodeClickDistance, onError: s }, u)
  )) });
}
zu.displayName = "NodeRenderer";
const Gw = Ne(zu);
function Jw(e) {
  return he(se((n) => {
    if (!e)
      return n.edges.map((o) => o.id);
    const i = [];
    if (n.width && n.height)
      for (const o of n.edges) {
        const s = n.nodeLookup.get(o.source), a = n.nodeLookup.get(o.target);
        s && a && qm({
          sourceNode: s,
          targetNode: a,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && i.push(o.id);
      }
    return i;
  }, [e]), xe);
}
const Qw = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return r.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, ev = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return r.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Ya = {
  [Bi.Arrow]: Qw,
  [Bi.ArrowClosed]: ev
};
function tv(e) {
  const t = we();
  return de(() => Object.prototype.hasOwnProperty.call(Ya, e) ? Ya[e] : (t.getState().onError?.("009", Oe.error009(e)), null), [e]);
}
const nv = ({ id: e, type: t, color: n, width: i = 12.5, height: o = 12.5, markerUnits: s = "strokeWidth", strokeWidth: a, orient: c = "auto-start-reverse" }) => {
  const u = tv(t);
  return u ? r.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${i}`, markerHeight: `${o}`, viewBox: "-10 -10 20 20", markerUnits: s, orient: c, refX: "0", refY: "0", children: r.jsx(u, { color: n, strokeWidth: a }) }) : null;
}, Lu = ({ defaultColor: e, rfId: t }) => {
  const n = he((s) => s.edges), i = he((s) => s.defaultEdgeOptions), o = de(() => tx(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: i?.markerStart,
    defaultMarkerEnd: i?.markerEnd
  }), [n, i, t, e]);
  return o.length ? r.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: r.jsx("defs", { children: o.map((s) => r.jsx(nv, { id: s.id, type: s.type, color: s.color, width: s.width, height: s.height, markerUnits: s.markerUnits, strokeWidth: s.strokeWidth, orient: s.orient }, s.id)) }) }) : null;
};
Lu.displayName = "MarkerDefinitions";
var iv = Ne(Lu);
function Vu({ x: e, y: t, label: n, labelStyle: i, labelShowBg: o = !0, labelBgStyle: s, labelBgPadding: a = [2, 4], labelBgBorderRadius: c = 2, children: u, className: l, ...d }) {
  const [f, p] = B({ x: 1, y: 0, width: 0, height: 0 }), h = Se(["react-flow__edge-textwrapper", l]), g = re(null);
  return J(() => {
    if (g.current) {
      const m = g.current.getBBox();
      p({
        x: m.x,
        y: m.y,
        width: m.width,
        height: m.height
      });
    }
  }, [n]), n ? r.jsxs("g", { transform: `translate(${e - f.width / 2} ${t - f.height / 2})`, className: h, visibility: f.width ? "visible" : "hidden", ...d, children: [o && r.jsx("rect", { width: f.width + 2 * a[0], x: -a[0], y: -a[1], height: f.height + 2 * a[1], className: "react-flow__edge-textbg", style: s, rx: c, ry: c }), r.jsx("text", { className: "react-flow__edge-text", y: f.height / 2, dy: "0.3em", ref: g, style: i, children: n }), u] }) : null;
}
Vu.displayName = "EdgeText";
const ov = Ne(Vu);
function Kn({ path: e, labelX: t, labelY: n, label: i, labelStyle: o, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u, interactionWidth: l = 20, ...d }) {
  return r.jsxs(r.Fragment, { children: [r.jsx("path", { ...d, d: e, fill: "none", className: Se(["react-flow__edge-path", d.className]) }), l ? r.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, i && Ke(t) && Ke(n) ? r.jsx(ov, { x: t, y: n, label: i, labelStyle: o, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u }) : null] });
}
function Ua({ pos: e, x1: t, y1: n, x2: i, y2: o }) {
  return e === ie.Left || e === ie.Right ? [0.5 * (t + i), n] : [t, 0.5 * (n + o)];
}
function Ou({ sourceX: e, sourceY: t, sourcePosition: n = ie.Bottom, targetX: i, targetY: o, targetPosition: s = ie.Top }) {
  const [a, c] = Ua({
    pos: n,
    x1: e,
    y1: t,
    x2: i,
    y2: o
  }), [u, l] = Ua({
    pos: s,
    x1: i,
    y1: o,
    x2: e,
    y2: t
  }), [d, f, p, h] = ru({
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
function Hu(e) {
  return Ne(({ id: t, sourceX: n, sourceY: i, targetX: o, targetY: s, sourcePosition: a, targetPosition: c, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: g, markerEnd: m, markerStart: w, interactionWidth: x }) => {
    const [j, y, v] = Ou({
      sourceX: n,
      sourceY: i,
      sourcePosition: a,
      targetX: o,
      targetY: s,
      targetPosition: c
    }), N = e.isInternal ? void 0 : t;
    return r.jsx(Kn, { id: N, path: j, labelX: y, labelY: v, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: g, markerEnd: m, markerStart: w, interactionWidth: x });
  });
}
const rv = Hu({ isInternal: !1 }), Wu = Hu({ isInternal: !0 });
rv.displayName = "SimpleBezierEdge";
Wu.displayName = "SimpleBezierEdgeInternal";
function Fu(e) {
  return Ne(({ id: t, sourceX: n, sourceY: i, targetX: o, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, sourcePosition: h = ie.Bottom, targetPosition: g = ie.Top, markerEnd: m, markerStart: w, pathOptions: x, interactionWidth: j }) => {
    const [y, v, N] = Xi({
      sourceX: n,
      sourceY: i,
      sourcePosition: h,
      targetX: o,
      targetY: s,
      targetPosition: g,
      borderRadius: x?.borderRadius,
      offset: x?.offset,
      stepPosition: x?.stepPosition
    }), b = e.isInternal ? void 0 : t;
    return r.jsx(Kn, { id: b, path: y, labelX: v, labelY: N, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: m, markerStart: w, interactionWidth: j });
  });
}
const Bu = Fu({ isInternal: !1 }), Ku = Fu({ isInternal: !0 });
Bu.displayName = "SmoothStepEdge";
Ku.displayName = "SmoothStepEdgeInternal";
function Xu(e) {
  return Ne(({ id: t, ...n }) => {
    const i = e.isInternal ? void 0 : t;
    return r.jsx(Bu, { ...n, id: i, pathOptions: de(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const sv = Xu({ isInternal: !1 }), qu = Xu({ isInternal: !0 });
sv.displayName = "StepEdge";
qu.displayName = "StepEdgeInternal";
function Yu(e) {
  return Ne(({ id: t, sourceX: n, sourceY: i, targetX: o, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: g, interactionWidth: m }) => {
    const [w, x, j] = lu({ sourceX: n, sourceY: i, targetX: o, targetY: s }), y = e.isInternal ? void 0 : t;
    return r.jsx(Kn, { id: y, path: w, labelX: x, labelY: j, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: g, interactionWidth: m });
  });
}
const av = Yu({ isInternal: !1 }), Uu = Yu({ isInternal: !0 });
av.displayName = "StraightEdge";
Uu.displayName = "StraightEdgeInternal";
function Zu(e) {
  return Ne(({ id: t, sourceX: n, sourceY: i, targetX: o, targetY: s, sourcePosition: a = ie.Bottom, targetPosition: c = ie.Top, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: g, markerEnd: m, markerStart: w, pathOptions: x, interactionWidth: j }) => {
    const [y, v, N] = su({
      sourceX: n,
      sourceY: i,
      sourcePosition: a,
      targetX: o,
      targetY: s,
      targetPosition: c,
      curvature: x?.curvature
    }), b = e.isInternal ? void 0 : t;
    return r.jsx(Kn, { id: b, path: y, labelX: v, labelY: N, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: g, markerEnd: m, markerStart: w, interactionWidth: j });
  });
}
const cv = Zu({ isInternal: !1 }), Gu = Zu({ isInternal: !0 });
cv.displayName = "BezierEdge";
Gu.displayName = "BezierEdgeInternal";
const Za = {
  default: Gu,
  straight: Uu,
  step: qu,
  smoothstep: Ku,
  simplebezier: Wu
}, Ga = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, lv = (e, t, n) => n === ie.Left ? e - t : n === ie.Right ? e + t : e, uv = (e, t, n) => n === ie.Top ? e - t : n === ie.Bottom ? e + t : e, Ja = "react-flow__edgeupdater";
function Qa({ position: e, centerX: t, centerY: n, radius: i = 10, onMouseDown: o, onMouseEnter: s, onMouseOut: a, type: c }) {
  return r.jsx("circle", { onMouseDown: o, onMouseEnter: s, onMouseOut: a, className: Se([Ja, `${Ja}-${c}`]), cx: lv(t, i, e), cy: uv(n, i, e), r: i, stroke: "transparent", fill: "transparent" });
}
function dv({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: i, sourceY: o, targetX: s, targetY: a, sourcePosition: c, targetPosition: u, onReconnect: l, onReconnectStart: d, onReconnectEnd: f, setReconnecting: p, setUpdateHover: h }) {
  const g = we(), m = (v, N) => {
    if (v.button !== 0)
      return;
    const { autoPanOnConnect: b, domNode: S, connectionMode: E, connectionRadius: _, lib: R, onConnectStart: D, cancelConnection: I, nodeLookup: M, rfId: C, panBy: k, updateConnection: A } = g.getState(), T = N.type === "target", $ = (W, H) => {
      p(!1), f?.(W, n, N.type, H);
    }, P = (W) => l?.(n, W), F = (W, H) => {
      p(!0), d?.(v, n, N.type), D?.(W, H);
    };
    Tr.onPointerDown(v.nativeEvent, {
      autoPanOnConnect: b,
      connectionMode: E,
      connectionRadius: _,
      domNode: S,
      handleId: N.id,
      nodeId: N.nodeId,
      nodeLookup: M,
      isTarget: T,
      edgeUpdaterType: N.type,
      lib: R,
      flowId: C,
      cancelConnection: I,
      panBy: k,
      isValidConnection: (...W) => g.getState().isValidConnection?.(...W) ?? !0,
      onConnect: P,
      onConnectStart: F,
      onConnectEnd: (...W) => g.getState().onConnectEnd?.(...W),
      onReconnectEnd: $,
      updateConnection: A,
      getTransform: () => g.getState().transform,
      getFromHandle: () => g.getState().connection.fromHandle,
      dragThreshold: g.getState().connectionDragThreshold,
      handleDomNode: v.currentTarget
    });
  }, w = (v) => m(v, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), x = (v) => m(v, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), j = () => h(!0), y = () => h(!1);
  return r.jsxs(r.Fragment, { children: [(e === !0 || e === "source") && r.jsx(Qa, { position: c, centerX: i, centerY: o, radius: t, onMouseDown: w, onMouseEnter: j, onMouseOut: y, type: "source" }), (e === !0 || e === "target") && r.jsx(Qa, { position: u, centerX: s, centerY: a, radius: t, onMouseDown: x, onMouseEnter: j, onMouseOut: y, type: "target" })] });
}
function fv({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: i, onClick: o, onDoubleClick: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, rfId: g, edgeTypes: m, noPanClassName: w, onError: x, disableKeyboardA11y: j }) {
  let y = he((Q) => Q.edgeLookup.get(e));
  const v = he((Q) => Q.defaultEdgeOptions);
  y = v ? { ...v, ...y } : y;
  let N = y.type || "default", b = m?.[N] || Za[N];
  b === void 0 && (x?.("011", Oe.error011(N)), N = "default", b = m?.default || Za.default);
  const S = !!(y.focusable || t && typeof y.focusable > "u"), E = typeof f < "u" && (y.reconnectable || n && typeof y.reconnectable > "u"), _ = !!(y.selectable || i && typeof y.selectable > "u"), R = re(null), [D, I] = B(!1), [M, C] = B(!1), k = we(), { zIndex: A, sourceX: T, sourceY: $, targetX: P, targetY: F, sourcePosition: W, targetPosition: H } = he(se((Q) => {
    const oe = Q.nodeLookup.get(y.source), fe = Q.nodeLookup.get(y.target);
    if (!oe || !fe)
      return {
        zIndex: y.zIndex,
        ...Ga
      };
    const V = ex({
      id: e,
      sourceNode: oe,
      targetNode: fe,
      sourceHandle: y.sourceHandle || null,
      targetHandle: y.targetHandle || null,
      connectionMode: Q.connectionMode,
      onError: x
    });
    return {
      zIndex: Xm({
        selected: y.selected,
        zIndex: y.zIndex,
        sourceNode: oe,
        targetNode: fe,
        elevateOnSelect: Q.elevateEdgesOnSelect,
        zIndexMode: Q.zIndexMode
      }),
      ...V || Ga
    };
  }, [y.source, y.target, y.sourceHandle, y.targetHandle, y.selected, y.zIndex]), xe), Y = de(() => y.markerStart ? `url('#${_r(y.markerStart, g)}')` : void 0, [y.markerStart, g]), Z = de(() => y.markerEnd ? `url('#${_r(y.markerEnd, g)}')` : void 0, [y.markerEnd, g]);
  if (y.hidden || T === null || $ === null || P === null || F === null)
    return null;
  const ne = (Q) => {
    const { addSelectedEdges: oe, unselectNodesAndEdges: fe, multiSelectionActive: V } = k.getState();
    _ && (k.setState({ nodesSelectionActive: !1 }), y.selected && V ? (fe({ nodes: [], edges: [y] }), R.current?.blur()) : oe([e])), o && o(Q, y);
  }, le = s ? (Q) => {
    s(Q, { ...y });
  } : void 0, G = a ? (Q) => {
    a(Q, { ...y });
  } : void 0, z = c ? (Q) => {
    c(Q, { ...y });
  } : void 0, X = u ? (Q) => {
    u(Q, { ...y });
  } : void 0, ae = l ? (Q) => {
    l(Q, { ...y });
  } : void 0, ce = (Q) => {
    if (!j && Kl.includes(Q.key) && _) {
      const { unselectNodesAndEdges: oe, addSelectedEdges: fe } = k.getState();
      Q.key === "Escape" ? (R.current?.blur(), oe({ edges: [y] })) : fe([e]);
    }
  };
  return r.jsx("svg", { style: { zIndex: A }, children: r.jsxs("g", { className: Se([
    "react-flow__edge",
    `react-flow__edge-${N}`,
    y.className,
    w,
    {
      selected: y.selected,
      animated: y.animated,
      inactive: !_ && !o,
      updating: D,
      selectable: _
    }
  ]), onClick: ne, onDoubleClick: le, onContextMenu: G, onMouseEnter: z, onMouseMove: X, onMouseLeave: ae, onKeyDown: S ? ce : void 0, tabIndex: S ? 0 : void 0, role: y.ariaRole ?? (S ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": y.ariaLabel === null ? void 0 : y.ariaLabel || `Edge from ${y.source} to ${y.target}`, "aria-describedby": S ? `${Su}-${g}` : void 0, ref: R, ...y.domAttributes, children: [!M && r.jsx(b, { id: e, source: y.source, target: y.target, type: y.type, selected: y.selected, animated: y.animated, selectable: _, deletable: y.deletable ?? !0, label: y.label, labelStyle: y.labelStyle, labelShowBg: y.labelShowBg, labelBgStyle: y.labelBgStyle, labelBgPadding: y.labelBgPadding, labelBgBorderRadius: y.labelBgBorderRadius, sourceX: T, sourceY: $, targetX: P, targetY: F, sourcePosition: W, targetPosition: H, data: y.data, style: y.style, sourceHandleId: y.sourceHandle, targetHandleId: y.targetHandle, markerStart: Y, markerEnd: Z, pathOptions: "pathOptions" in y ? y.pathOptions : void 0, interactionWidth: y.interactionWidth }), E && r.jsx(dv, { edge: y, isReconnectable: E, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, sourceX: T, sourceY: $, targetX: P, targetY: F, sourcePosition: W, targetPosition: H, setUpdateHover: I, setReconnecting: C })] }) });
}
var pv = Ne(fv);
const hv = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function Ju({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: i, noPanClassName: o, onReconnect: s, onEdgeContextMenu: a, onEdgeMouseEnter: c, onEdgeMouseMove: u, onEdgeMouseLeave: l, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: p, onReconnectStart: h, onReconnectEnd: g, disableKeyboardA11y: m }) {
  const { edgesFocusable: w, edgesReconnectable: x, elementsSelectable: j, onError: y } = he(hv, xe), v = Jw(t);
  return r.jsxs("div", { className: "react-flow__edges", children: [r.jsx(iv, { defaultColor: e, rfId: n }), v.map((N) => r.jsx(pv, { id: N, edgesFocusable: w, edgesReconnectable: x, elementsSelectable: j, noPanClassName: o, onReconnect: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, onClick: d, reconnectRadius: f, onDoubleClick: p, onReconnectStart: h, onReconnectEnd: g, rfId: n, onError: y, edgeTypes: i, disableKeyboardA11y: m }, N))] });
}
Ju.displayName = "EdgeRenderer";
const gv = Ne(Ju), yv = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function mv({ children: e }) {
  const t = he(yv);
  return r.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function xv(e) {
  const t = fs(), n = re(!1);
  J(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const wv = (e) => e.panZoom?.syncViewport;
function vv(e) {
  const t = he(wv), n = we();
  return J(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function bv(e) {
  return e.connection.inProgress ? { ...e.connection, to: rn(e.connection.to, e.transform) } : { ...e.connection };
}
function Nv(e) {
  return bv;
}
function jv(e) {
  const t = Nv();
  return he(t, xe);
}
const Sv = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function Cv({ containerStyle: e, style: t, type: n, component: i }) {
  const { nodesConnectable: o, width: s, height: a, isValid: c, inProgress: u } = he(Sv, xe);
  return !(s && o && u) ? null : r.jsx("svg", { style: e, width: s, height: a, className: "react-flow__connectionline react-flow__container", children: r.jsx("g", { className: Se(["react-flow__connection", Yl(c)]), children: r.jsx(Qu, { style: t, type: n, CustomComponent: i, isValid: c }) }) });
}
const Qu = ({ style: e, type: t = dt.Bezier, CustomComponent: n, isValid: i }) => {
  const { inProgress: o, from: s, fromNode: a, fromHandle: c, fromPosition: u, to: l, toNode: d, toHandle: f, toPosition: p, pointer: h } = jv();
  if (!o)
    return;
  if (n)
    return r.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: a, fromHandle: c, fromX: s.x, fromY: s.y, toX: l.x, toY: l.y, fromPosition: u, toPosition: p, connectionStatus: Yl(i), toNode: d, toHandle: f, pointer: h });
  let g = "";
  const m = {
    sourceX: s.x,
    sourceY: s.y,
    sourcePosition: u,
    targetX: l.x,
    targetY: l.y,
    targetPosition: p
  };
  switch (t) {
    case dt.Bezier:
      [g] = su(m);
      break;
    case dt.SimpleBezier:
      [g] = Ou(m);
      break;
    case dt.Step:
      [g] = Xi({
        ...m,
        borderRadius: 0
      });
      break;
    case dt.SmoothStep:
      [g] = Xi(m);
      break;
    default:
      [g] = lu(m);
  }
  return r.jsx("path", { d: g, fill: "none", className: "react-flow__connection-path", style: e });
};
Qu.displayName = "ConnectionLine";
const Ev = {};
function ec(e = Ev) {
  re(e), we(), J(() => {
  }, [e]);
}
function Iv() {
  we(), re(!1), J(() => {
  }, []);
}
function ed({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: i, onEdgeClick: o, onNodeDoubleClick: s, onEdgeDoubleClick: a, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: p, onSelectionEnd: h, connectionLineType: g, connectionLineStyle: m, connectionLineComponent: w, connectionLineContainerStyle: x, selectionKeyCode: j, selectionOnDrag: y, selectionMode: v, multiSelectionKeyCode: N, panActivationKeyCode: b, zoomActivationKeyCode: S, deleteKeyCode: E, onlyRenderVisibleElements: _, elementsSelectable: R, defaultViewport: D, translateExtent: I, minZoom: M, maxZoom: C, preventScrolling: k, defaultMarkerColor: A, zoomOnScroll: T, zoomOnPinch: $, panOnScroll: P, panOnScrollSpeed: F, panOnScrollMode: W, zoomOnDoubleClick: H, panOnDrag: Y, autoPanOnSelection: Z, onPaneClick: ne, onPaneMouseEnter: le, onPaneMouseMove: G, onPaneMouseLeave: z, onPaneScroll: X, onPaneContextMenu: ae, paneClickDistance: ce, nodeClickDistance: Q, onEdgeContextMenu: oe, onEdgeMouseEnter: fe, onEdgeMouseMove: V, onEdgeMouseLeave: te, reconnectRadius: ge, onReconnect: me, onReconnectStart: Te, onReconnectEnd: Ie, noDragClassName: Ae, noWheelClassName: tt, noPanClassName: We, disableKeyboardA11y: L, nodeExtent: O, rfId: K, viewport: q, onViewportChange: ee }) {
  return ec(e), ec(t), Iv(), xv(n), vv(q), r.jsx(Ww, { onPaneClick: ne, onPaneMouseEnter: le, onPaneMouseMove: G, onPaneMouseLeave: z, onPaneContextMenu: ae, onPaneScroll: X, paneClickDistance: ce, deleteKeyCode: E, selectionKeyCode: j, selectionOnDrag: y, selectionMode: v, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: N, panActivationKeyCode: b, zoomActivationKeyCode: S, elementsSelectable: R, zoomOnScroll: T, zoomOnPinch: $, zoomOnDoubleClick: H, panOnScroll: P, panOnScrollSpeed: F, panOnScrollMode: W, panOnDrag: Y, autoPanOnSelection: Z, defaultViewport: D, translateExtent: I, minZoom: M, maxZoom: C, onSelectionContextMenu: f, preventScrolling: k, noDragClassName: Ae, noWheelClassName: tt, noPanClassName: We, disableKeyboardA11y: L, onViewportChange: ee, isControlledViewport: !!q, children: r.jsxs(mv, { children: [r.jsx(gv, { edgeTypes: t, onEdgeClick: o, onEdgeDoubleClick: a, onReconnect: me, onReconnectStart: Te, onReconnectEnd: Ie, onlyRenderVisibleElements: _, onEdgeContextMenu: oe, onEdgeMouseEnter: fe, onEdgeMouseMove: V, onEdgeMouseLeave: te, reconnectRadius: ge, defaultMarkerColor: A, noPanClassName: We, disableKeyboardA11y: L, rfId: K }), r.jsx(Cv, { style: m, type: g, component: w, containerStyle: x }), r.jsx("div", { className: "react-flow__edgelabel-renderer" }), r.jsx(Gw, { nodeTypes: e, onNodeClick: i, onNodeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, nodeClickDistance: Q, onlyRenderVisibleElements: _, noPanClassName: We, noDragClassName: Ae, disableKeyboardA11y: L, nodeExtent: O, rfId: K }), r.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
ed.displayName = "GraphView";
const kv = Ne(ed), Av = Ql(), tc = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, width: o, height: s, fitView: a, fitViewOptions: c, minZoom: u = 0.5, maxZoom: l = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: p = "basic" } = {}) => {
  const h = /* @__PURE__ */ new Map(), g = /* @__PURE__ */ new Map(), m = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), x = i ?? t ?? [], j = n ?? e ?? [], y = d ?? [0, 0], v = f ?? Tn;
  fu(m, w, x);
  const { nodesInitialized: N } = Dr(j, h, g, {
    nodeOrigin: y,
    nodeExtent: v,
    zIndexMode: p
  });
  let b = [0, 0, 1];
  if (a && o && s) {
    const S = Fn(h, {
      filter: (D) => !!((D.width || D.initialWidth) && (D.height || D.initialHeight))
    }), { x: E, y: _, zoom: R } = rs(S, o, s, u, l, c?.padding ?? 0.1);
    b = [E, _, R];
  }
  return {
    rfId: "1",
    width: o ?? 0,
    height: s ?? 0,
    transform: b,
    nodes: j,
    nodesInitialized: N,
    nodeLookup: h,
    parentLookup: g,
    edges: x,
    edgeLookup: w,
    connectionLookup: m,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: i !== void 0,
    panZoom: null,
    minZoom: u,
    maxZoom: l,
    translateExtent: Tn,
    nodeExtent: v,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: Ut.Strict,
    domNode: null,
    paneDragging: !1,
    noPanClassName: "nopan",
    nodeOrigin: y,
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
    connection: { ...ql },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: Av,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Xl,
    zIndexMode: p,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, _v = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, width: o, height: s, fitView: a, fitViewOptions: c, minZoom: u, maxZoom: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p }) => Fx((h, g) => {
  async function m() {
    const { nodeLookup: w, panZoom: x, fitViewOptions: j, fitViewResolver: y, width: v, height: N, minZoom: b, maxZoom: S } = g();
    x && (await Vm({
      nodes: w,
      width: v,
      height: N,
      panZoom: x,
      minZoom: b,
      maxZoom: S
    }, j), y?.resolve(!0), h({ fitViewResolver: null }));
  }
  return {
    ...tc({
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
      const { nodeLookup: x, parentLookup: j, nodeOrigin: y, elevateNodesOnSelect: v, fitViewQueued: N, zIndexMode: b, nodesSelectionActive: S } = g(), { nodesInitialized: E, hasSelectedNodes: _ } = Dr(w, x, j, {
        nodeOrigin: y,
        nodeExtent: f,
        elevateNodesOnSelect: v,
        checkEquality: !0,
        zIndexMode: b
      }), R = S && _;
      N && E ? (m(), h({
        nodes: w,
        nodesInitialized: E,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: R
      })) : h({ nodes: w, nodesInitialized: E, nodesSelectionActive: R });
    },
    setEdges: (w) => {
      const { connectionLookup: x, edgeLookup: j } = g();
      fu(x, j, w), h({ edges: w });
    },
    setDefaultNodesAndEdges: (w, x) => {
      if (w) {
        const { setNodes: j } = g();
        j(w), h({ hasDefaultNodes: !0 });
      }
      if (x) {
        const { setEdges: j } = g();
        j(x), h({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (w) => {
      const { triggerNodeChanges: x, nodeLookup: j, parentLookup: y, domNode: v, nodeOrigin: N, nodeExtent: b, debug: S, fitViewQueued: E, zIndexMode: _ } = g(), { changes: R, updatedInternals: D } = cx(w, j, y, v, N, b, _);
      D && (ox(j, y, { nodeOrigin: N, nodeExtent: b, zIndexMode: _ }), E ? (m(), h({ fitViewQueued: !1, fitViewOptions: void 0 })) : h({}), R?.length > 0 && (S && console.log("React Flow: trigger node changes", R), x?.(R)));
    },
    updateNodePositions: (w, x = !1) => {
      const j = [];
      let y = [];
      const { nodeLookup: v, triggerNodeChanges: N, connection: b, updateConnection: S, onNodesChangeMiddlewareMap: E } = g();
      for (const [_, R] of w) {
        const D = v.get(_), I = !!(D?.expandParent && D?.parentId && R?.position), M = {
          id: _,
          type: "position",
          position: I ? {
            x: Math.max(0, R.position.x),
            y: Math.max(0, R.position.y)
          } : R.position,
          dragging: x
        };
        if (D && b.inProgress && b.fromNode.id === D.id) {
          const C = Ct(D, b.fromHandle, ie.Left, !0);
          S({ ...b, from: C });
        }
        I && D.parentId && j.push({
          id: _,
          parentId: D.parentId,
          rect: {
            ...R.internals.positionAbsolute,
            width: R.measured.width ?? 0,
            height: R.measured.height ?? 0
          }
        }), y.push(M);
      }
      if (j.length > 0) {
        const { parentLookup: _, nodeOrigin: R } = g(), D = ds(j, v, _, R);
        y.push(...D);
      }
      for (const _ of E.values())
        y = _(y);
      N(y);
    },
    triggerNodeChanges: (w) => {
      const { onNodesChange: x, setNodes: j, nodes: y, hasDefaultNodes: v, debug: N } = g();
      if (w?.length) {
        if (v) {
          const b = Iu(w, y);
          j(b);
        }
        N && console.log("React Flow: trigger node changes", w), x?.(w);
      }
    },
    triggerEdgeChanges: (w) => {
      const { onEdgesChange: x, setEdges: j, edges: y, hasDefaultEdges: v, debug: N } = g();
      if (w?.length) {
        if (v) {
          const b = ku(w, y);
          j(b);
        }
        N && console.log("React Flow: trigger edge changes", w), x?.(w);
      }
    },
    addSelectedNodes: (w) => {
      const { multiSelectionActive: x, edgeLookup: j, nodeLookup: y, triggerNodeChanges: v, triggerEdgeChanges: N } = g();
      if (x) {
        const b = w.map((S) => gt(S, !0));
        v(b);
        return;
      }
      v(Lt(y, /* @__PURE__ */ new Set([...w]), !0)), N(Lt(j));
    },
    addSelectedEdges: (w) => {
      const { multiSelectionActive: x, edgeLookup: j, nodeLookup: y, triggerNodeChanges: v, triggerEdgeChanges: N } = g();
      if (x) {
        const b = w.map((S) => gt(S, !0));
        N(b);
        return;
      }
      N(Lt(j, /* @__PURE__ */ new Set([...w]))), v(Lt(y, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: w, edges: x } = {}) => {
      const { edges: j, nodes: y, nodeLookup: v, triggerNodeChanges: N, triggerEdgeChanges: b } = g(), S = w || y, E = x || j, _ = [];
      for (const D of S) {
        if (!D.selected)
          continue;
        const I = v.get(D.id);
        I && (I.selected = !1), _.push(gt(D.id, !1));
      }
      const R = [];
      for (const D of E)
        D.selected && R.push(gt(D.id, !1));
      N(_), b(R);
    },
    setMinZoom: (w) => {
      const { panZoom: x, maxZoom: j } = g();
      x?.setScaleExtent([w, j]), h({ minZoom: w });
    },
    setMaxZoom: (w) => {
      const { panZoom: x, minZoom: j } = g();
      x?.setScaleExtent([j, w]), h({ maxZoom: w });
    },
    setTranslateExtent: (w) => {
      g().panZoom?.setTranslateExtent(w), h({ translateExtent: w });
    },
    resetSelectedElements: () => {
      const { edges: w, nodes: x, triggerNodeChanges: j, triggerEdgeChanges: y, elementsSelectable: v } = g();
      if (!v)
        return;
      const N = x.reduce((S, E) => E.selected ? [...S, gt(E.id, !1)] : S, []), b = w.reduce((S, E) => E.selected ? [...S, gt(E.id, !1)] : S, []);
      j(N), y(b);
    },
    setNodeExtent: (w) => {
      const { nodes: x, nodeLookup: j, parentLookup: y, nodeOrigin: v, elevateNodesOnSelect: N, nodeExtent: b, zIndexMode: S } = g();
      w[0][0] === b[0][0] && w[0][1] === b[0][1] && w[1][0] === b[1][0] && w[1][1] === b[1][1] || (Dr(x, j, y, {
        nodeOrigin: v,
        nodeExtent: w,
        elevateNodesOnSelect: N,
        checkEquality: !1,
        zIndexMode: S
      }), h({ nodeExtent: w }));
    },
    panBy: (w) => {
      const { transform: x, width: j, height: y, panZoom: v, translateExtent: N } = g();
      return lx({ delta: w, panZoom: v, transform: x, translateExtent: N, width: j, height: y });
    },
    setCenter: async (w, x, j) => {
      const { width: y, height: v, maxZoom: N, panZoom: b } = g();
      if (!b)
        return !1;
      const S = typeof j?.zoom < "u" ? j.zoom : N;
      return await b.setViewport({
        x: y / 2 - w * S,
        y: v / 2 - x * S,
        zoom: S
      }, { duration: j?.duration, ease: j?.ease, interpolate: j?.interpolate }), !0;
    },
    cancelConnection: () => {
      h({
        connection: { ...ql }
      });
    },
    updateConnection: (w) => {
      h({ connection: w });
    },
    reset: () => h({ ...tc() })
  };
}, Object.is);
function Dv({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: i, initialWidth: o, initialHeight: s, initialMinZoom: a, initialMaxZoom: c, initialFitViewOptions: u, fitView: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p, children: h }) {
  const [g] = B(() => _v({
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
  return r.jsx(qx, { value: g, children: r.jsx(mw, { children: h }) });
}
function Tv({ children: e, nodes: t, edges: n, defaultNodes: i, defaultEdges: o, width: s, height: a, fitView: c, fitViewOptions: u, minZoom: l, maxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h }) {
  return zn(co) ? r.jsx(r.Fragment, { children: e }) : r.jsx(Dv, { initialNodes: t, initialEdges: n, defaultNodes: i, defaultEdges: o, initialWidth: s, initialHeight: a, fitView: c, initialFitViewOptions: u, initialMinZoom: l, initialMaxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h, children: e });
}
const Pv = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function $v({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, className: o, nodeTypes: s, edgeTypes: a, onNodeClick: c, onEdgeClick: u, onInit: l, onMove: d, onMoveStart: f, onMoveEnd: p, onConnect: h, onConnectStart: g, onConnectEnd: m, onClickConnectStart: w, onClickConnectEnd: x, onNodeMouseEnter: j, onNodeMouseMove: y, onNodeMouseLeave: v, onNodeContextMenu: N, onNodeDoubleClick: b, onNodeDragStart: S, onNodeDrag: E, onNodeDragStop: _, onNodesDelete: R, onEdgesDelete: D, onDelete: I, onSelectionChange: M, onSelectionDragStart: C, onSelectionDrag: k, onSelectionDragStop: A, onSelectionContextMenu: T, onSelectionStart: $, onSelectionEnd: P, onBeforeDelete: F, connectionMode: W, connectionLineType: H = dt.Bezier, connectionLineStyle: Y, connectionLineComponent: Z, connectionLineContainerStyle: ne, deleteKeyCode: le = "Backspace", selectionKeyCode: G = "Shift", selectionOnDrag: z = !1, selectionMode: X = Pn.Full, panActivationKeyCode: ae = "Space", multiSelectionKeyCode: ce = Mn() ? "Meta" : "Control", zoomActivationKeyCode: Q = Mn() ? "Meta" : "Control", snapToGrid: oe, snapGrid: fe, onlyRenderVisibleElements: V = !1, selectNodesOnDrag: te, nodesDraggable: ge, autoPanOnNodeFocus: me, nodesConnectable: Te, nodesFocusable: Ie, nodeOrigin: Ae = Cu, edgesFocusable: tt, edgesReconnectable: We, elementsSelectable: L = !0, defaultViewport: O = sw, minZoom: K = 0.5, maxZoom: q = 2, translateExtent: ee = Tn, preventScrolling: ye = !0, nodeExtent: pe, defaultMarkerColor: je = "#b1b1b7", zoomOnScroll: Ee = !0, zoomOnPinch: Me = !0, panOnScroll: ke = !1, panOnScrollSpeed: ho = 0.5, panOnScrollMode: go = vt.Free, zoomOnDoubleClick: He = !0, panOnDrag: yo = !0, onPaneClick: qn, onPaneMouseEnter: At, onPaneMouseMove: mo, onPaneMouseLeave: Ye, onPaneScroll: _t, onPaneContextMenu: xo, paneClickDistance: Yn = 1, nodeClickDistance: wo = 0, children: vo, onReconnect: ct, onReconnectStart: bo, onReconnectEnd: Dt, onEdgeContextMenu: No, onEdgeDoubleClick: Tt, onEdgeMouseEnter: jo, onEdgeMouseMove: Un, onEdgeMouseLeave: Zn, reconnectRadius: So = 10, onNodesChange: an, onEdgesChange: Co, noDragClassName: Eo = "nodrag", noWheelClassName: Io = "nowheel", noPanClassName: Pt = "nopan", fitView: Gn, fitViewOptions: Jn, connectOnClick: ko, attributionPosition: Ao, proOptions: _o, defaultEdgeOptions: Do, elevateNodesOnSelect: To = !0, elevateEdgesOnSelect: Po = !1, disableKeyboardA11y: Qn = !1, autoPanOnConnect: $o, autoPanOnNodeDrag: Mo, autoPanOnSelection: Ro = !0, autoPanSpeed: zo, connectionRadius: Lo, isValidConnection: ei, onError: ti, style: ni, id: ii, nodeDragThreshold: Vo, connectionDragThreshold: Oo, viewport: Ho, onViewportChange: Wo, width: Fo, height: Bo, colorMode: Ko = "light", debug: Xo, onScroll: oi, ariaLabelConfig: qo, zIndexMode: ri = "basic", ...Yo }, Uo) {
  const cn = ii || "1", $t = uw(Ko), si = se((ai) => {
    ai.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), oi?.(ai);
  }, [oi]);
  return r.jsx("div", { "data-testid": "rf__wrapper", ...Yo, onScroll: si, style: { ...ni, ...Pv }, ref: Uo, className: Se(["react-flow", o, $t]), id: ii, role: "application", children: r.jsxs(Tv, { nodes: e, edges: t, width: Fo, height: Bo, fitView: Gn, fitViewOptions: Jn, minZoom: K, maxZoom: q, nodeOrigin: Ae, nodeExtent: pe, zIndexMode: ri, children: [r.jsx(lw, { nodes: e, edges: t, defaultNodes: n, defaultEdges: i, onConnect: h, onConnectStart: g, onConnectEnd: m, onClickConnectStart: w, onClickConnectEnd: x, nodesDraggable: ge, autoPanOnNodeFocus: me, nodesConnectable: Te, nodesFocusable: Ie, edgesFocusable: tt, edgesReconnectable: We, elementsSelectable: L, elevateNodesOnSelect: To, elevateEdgesOnSelect: Po, minZoom: K, maxZoom: q, nodeExtent: pe, onNodesChange: an, onEdgesChange: Co, snapToGrid: oe, snapGrid: fe, connectionMode: W, translateExtent: ee, connectOnClick: ko, defaultEdgeOptions: Do, fitView: Gn, fitViewOptions: Jn, onNodesDelete: R, onEdgesDelete: D, onDelete: I, onNodeDragStart: S, onNodeDrag: E, onNodeDragStop: _, onSelectionDrag: k, onSelectionDragStart: C, onSelectionDragStop: A, onMove: d, onMoveStart: f, onMoveEnd: p, noPanClassName: Pt, nodeOrigin: Ae, rfId: cn, autoPanOnConnect: $o, autoPanOnNodeDrag: Mo, autoPanSpeed: zo, onError: ti, connectionRadius: Lo, isValidConnection: ei, selectNodesOnDrag: te, nodeDragThreshold: Vo, connectionDragThreshold: Oo, onBeforeDelete: F, debug: Xo, ariaLabelConfig: qo, zIndexMode: ri }), r.jsx(kv, { onInit: l, onNodeClick: c, onEdgeClick: u, onNodeMouseEnter: j, onNodeMouseMove: y, onNodeMouseLeave: v, onNodeContextMenu: N, onNodeDoubleClick: b, nodeTypes: s, edgeTypes: a, connectionLineType: H, connectionLineStyle: Y, connectionLineComponent: Z, connectionLineContainerStyle: ne, selectionKeyCode: G, selectionOnDrag: z, selectionMode: X, deleteKeyCode: le, multiSelectionKeyCode: ce, panActivationKeyCode: ae, zoomActivationKeyCode: Q, onlyRenderVisibleElements: V, defaultViewport: O, translateExtent: ee, minZoom: K, maxZoom: q, preventScrolling: ye, zoomOnScroll: Ee, zoomOnPinch: Me, zoomOnDoubleClick: He, panOnScroll: ke, panOnScrollSpeed: ho, panOnScrollMode: go, panOnDrag: yo, autoPanOnSelection: Ro, onPaneClick: qn, onPaneMouseEnter: At, onPaneMouseMove: mo, onPaneMouseLeave: Ye, onPaneScroll: _t, onPaneContextMenu: xo, paneClickDistance: Yn, nodeClickDistance: wo, onSelectionContextMenu: T, onSelectionStart: $, onSelectionEnd: P, onReconnect: ct, onReconnectStart: bo, onReconnectEnd: Dt, onEdgeContextMenu: No, onEdgeDoubleClick: Tt, onEdgeMouseEnter: jo, onEdgeMouseMove: Un, onEdgeMouseLeave: Zn, reconnectRadius: So, defaultMarkerColor: je, noDragClassName: Eo, noWheelClassName: Io, noPanClassName: Pt, rfId: cn, disableKeyboardA11y: Qn, nodeExtent: pe, viewport: Ho, onViewportChange: Wo }), r.jsx(rw, { onSelectionChange: M }), vo, r.jsx(ew, { proOptions: _o, position: Ao }), r.jsx(Qx, { rfId: cn, disableKeyboardA11y: Qn })] }) });
}
var td = Du($v);
const Mv = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function Rv({ children: e }) {
  const t = he(Mv);
  return t ? Xx.createPortal(e, t) : null;
}
function zv({ dimensions: e, lineWidth: t, variant: n, className: i }) {
  return r.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Se(["react-flow__background-pattern", n, i]) });
}
function Lv({ radius: e, className: t }) {
  return r.jsx("circle", { cx: e, cy: e, r: e, className: Se(["react-flow__background-pattern", "dots", t]) });
}
var ft;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(ft || (ft = {}));
const Vv = {
  [ft.Dots]: 1,
  [ft.Lines]: 1,
  [ft.Cross]: 6
}, Ov = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function nd({
  id: e,
  variant: t = ft.Dots,
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
  const f = re(null), { transform: p, patternId: h } = he(Ov, xe), g = i || Vv[t], m = t === ft.Dots, w = t === ft.Cross, x = Array.isArray(n) ? n : [n, n], j = [x[0] * p[2] || 1, x[1] * p[2] || 1], y = g * p[2], v = Array.isArray(s) ? s : [s, s], N = w ? [y, y] : j, b = [
    v[0] * p[2] || 1 + N[0] / 2,
    v[1] * p[2] || 1 + N[1] / 2
  ], S = `${h}${e || ""}`;
  return r.jsxs("svg", { className: Se(["react-flow__background", l]), style: {
    ...u,
    ...uo,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": a
  }, ref: f, "data-testid": "rf__background", children: [r.jsx("pattern", { id: S, x: p[0] % j[0], y: p[1] % j[1], width: j[0], height: j[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${b[0]},-${b[1]})`, children: m ? r.jsx(Lv, { radius: y / 2, className: d }) : r.jsx(zv, { dimensions: N, lineWidth: o, variant: t, className: d }) }), r.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${S})` })] });
}
nd.displayName = "Background";
const id = Ne(nd);
function Hv() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: r.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function Wv() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: r.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function Fv() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: r.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function Bv() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: r.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function Kv() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: r.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function yi({ children: e, className: t, ...n }) {
  return r.jsx("button", { type: "button", className: Se(["react-flow__controls-button", t]), ...n, children: e });
}
const Xv = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function od({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: i = !0, fitViewOptions: o, onZoomIn: s, onZoomOut: a, onFitView: c, onInteractiveChange: u, className: l, children: d, position: f = "bottom-left", orientation: p = "vertical", "aria-label": h }) {
  const g = we(), { isInteractive: m, minZoomReached: w, maxZoomReached: x, ariaLabelConfig: j } = he(Xv, xe), { zoomIn: y, zoomOut: v, fitView: N } = fs(), b = () => {
    y(), s?.();
  }, S = () => {
    v(), a?.();
  }, E = () => {
    N(o), c?.();
  }, _ = () => {
    g.setState({
      nodesDraggable: !m,
      nodesConnectable: !m,
      elementsSelectable: !m
    }), u?.(!m);
  }, R = p === "horizontal" ? "horizontal" : "vertical";
  return r.jsxs(lo, { className: Se(["react-flow__controls", R, l]), position: f, style: e, "data-testid": "rf__controls", "aria-label": h ?? j["controls.ariaLabel"], children: [t && r.jsxs(r.Fragment, { children: [r.jsx(yi, { onClick: b, className: "react-flow__controls-zoomin", title: j["controls.zoomIn.ariaLabel"], "aria-label": j["controls.zoomIn.ariaLabel"], disabled: x, children: r.jsx(Hv, {}) }), r.jsx(yi, { onClick: S, className: "react-flow__controls-zoomout", title: j["controls.zoomOut.ariaLabel"], "aria-label": j["controls.zoomOut.ariaLabel"], disabled: w, children: r.jsx(Wv, {}) })] }), n && r.jsx(yi, { className: "react-flow__controls-fitview", onClick: E, title: j["controls.fitView.ariaLabel"], "aria-label": j["controls.fitView.ariaLabel"], children: r.jsx(Fv, {}) }), i && r.jsx(yi, { className: "react-flow__controls-interactive", onClick: _, title: j["controls.interactive.ariaLabel"], "aria-label": j["controls.interactive.ariaLabel"], children: m ? r.jsx(Kv, {}) : r.jsx(Bv, {}) }), d] });
}
od.displayName = "Controls";
const rd = Ne(od);
function qv({ id: e, x: t, y: n, width: i, height: o, style: s, color: a, strokeColor: c, strokeWidth: u, className: l, borderRadius: d, shapeRendering: f, selected: p, onClick: h }) {
  const { background: g, backgroundColor: m } = s || {}, w = a || g || m;
  return r.jsx("rect", { className: Se(["react-flow__minimap-node", { selected: p }, l]), x: t, y: n, rx: d, ry: d, width: i, height: o, style: {
    fill: w,
    stroke: c,
    strokeWidth: u
  }, shapeRendering: f, onClick: h ? (x) => h(x, e) : void 0 });
}
const Yv = Ne(qv), Uv = (e) => e.nodes.map((t) => t.id), pr = (e) => e instanceof Function ? e : () => e;
function Zv({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: i = 5,
  nodeStrokeWidth: o,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: s = Yv,
  onClick: a
}) {
  const c = he(Uv, xe), u = pr(t), l = pr(e), d = pr(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return r.jsx(r.Fragment, { children: c.map((p) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    r.jsx(Jv, { id: p, nodeColorFunc: u, nodeStrokeColorFunc: l, nodeClassNameFunc: d, nodeBorderRadius: i, nodeStrokeWidth: o, NodeComponent: s, onClick: a, shapeRendering: f }, p)
  )) });
}
function Gv({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: i, nodeBorderRadius: o, nodeStrokeWidth: s, shapeRendering: a, NodeComponent: c, onClick: u }) {
  const { node: l, x: d, y: f, width: p, height: h } = he((g) => {
    const m = g.nodeLookup.get(e);
    if (!m)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const w = m.internals.userNode, { x, y: j } = m.internals.positionAbsolute, { width: y, height: v } = at(w);
    return {
      node: w,
      x,
      y: j,
      width: y,
      height: v
    };
  }, xe);
  return !l || l.hidden || !eu(l) ? null : r.jsx(c, { x: d, y: f, width: p, height: h, style: l.style, selected: !!l.selected, className: i(l), color: t(l), borderRadius: o, strokeColor: n(l), strokeWidth: s, shapeRendering: a, onClick: u, id: l.id });
}
const Jv = Ne(Gv);
var Qv = Ne(Zv);
const e0 = 200, t0 = 150, n0 = (e) => !e.hidden, i0 = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? Jl(Fn(e.nodeLookup, { filter: n0 }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, o0 = "react-flow__minimap-desc";
function sd({
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
  onNodeClick: g,
  pannable: m = !1,
  zoomable: w = !1,
  ariaLabel: x,
  inversePan: j,
  zoomStep: y = 1,
  offsetScale: v = 5
}) {
  const N = we(), b = re(null), { boundingRect: S, viewBB: E, rfId: _, panZoom: R, translateExtent: D, flowWidth: I, flowHeight: M, ariaLabelConfig: C } = he(i0, xe), k = e?.width ?? e0, A = e?.height ?? t0, T = S.width / k, $ = S.height / A, P = Math.max(T, $), F = P * k, W = P * A, H = v * P, Y = S.x - (F - S.width) / 2 - H, Z = S.y - (W - S.height) / 2 - H, ne = F + H * 2, le = W + H * 2, G = `${o0}-${_}`, z = re(0), X = re();
  z.current = P, J(() => {
    if (b.current && R)
      return X.current = xx({
        domNode: b.current,
        panZoom: R,
        getTransform: () => N.getState().transform,
        getViewScale: () => z.current
      }), () => {
        X.current?.destroy();
      };
  }, [R]), J(() => {
    X.current?.update({
      translateExtent: D,
      width: I,
      height: M,
      inversePan: j,
      pannable: m,
      zoomStep: y,
      zoomable: w
    });
  }, [m, w, j, y, D, I, M]);
  const ae = h ? (oe) => {
    const [fe, V] = X.current?.pointer(oe) || [0, 0];
    h(oe, { x: fe, y: V });
  } : void 0, ce = g ? se((oe, fe) => {
    const V = N.getState().nodeLookup.get(fe).internals.userNode;
    g(oe, V);
  }, []) : void 0, Q = x ?? C["minimap.ariaLabel"];
  return r.jsx(lo, { position: p, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * P : void 0,
    "--xy-minimap-node-background-color-props": typeof i == "string" ? i : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof a == "number" ? a : void 0
  }, className: Se(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: r.jsxs("svg", { width: k, height: A, viewBox: `${Y} ${Z} ${ne} ${le}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": G, ref: b, onClick: ae, children: [Q && r.jsx("title", { id: G, children: Q }), r.jsx(Qv, { onClick: ce, nodeColor: i, nodeStrokeColor: n, nodeBorderRadius: s, nodeClassName: o, nodeStrokeWidth: a, nodeComponent: c }), r.jsx("path", { className: "react-flow__minimap-mask", d: `M${Y - H},${Z - H}h${ne + H * 2}v${le + H * 2}h${-ne - H * 2}z
        M${E.x},${E.y}h${E.width}v${E.height}h${-E.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
sd.displayName = "MiniMap";
const ad = Ne(sd), r0 = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, s0 = {
  [Qt.Line]: "right",
  [Qt.Handle]: "bottom-right"
};
function a0({ nodeId: e, position: t, variant: n = Qt.Handle, className: i, style: o = void 0, children: s, color: a, minWidth: c = 10, minHeight: u = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: p, autoScale: h = !0, shouldResize: g, onResizeStart: m, onResize: w, onResizeEnd: x }) {
  const j = Mu(), y = typeof e == "string" ? e : j, v = we(), N = re(null), b = n === Qt.Handle, S = he(se(r0(b && h), [b, h]), xe), E = re(null), _ = t ?? s0[n];
  J(() => {
    if (!(!N.current || !y))
      return E.current || (E.current = Dx({
        domNode: N.current,
        nodeId: y,
        getStoreItems: () => {
          const { nodeLookup: D, transform: I, snapGrid: M, snapToGrid: C, nodeOrigin: k, domNode: A } = v.getState();
          return {
            nodeLookup: D,
            transform: I,
            snapGrid: M,
            snapToGrid: C,
            nodeOrigin: k,
            paneDomNode: A
          };
        },
        onChange: (D, I) => {
          const { triggerNodeChanges: M, nodeLookup: C, parentLookup: k, nodeOrigin: A } = v.getState(), T = [], $ = { x: D.x, y: D.y }, P = C.get(y);
          if (P && P.expandParent && P.parentId) {
            const F = P.origin ?? A, W = D.width ?? P.measured.width ?? 0, H = D.height ?? P.measured.height ?? 0, Y = {
              id: P.id,
              parentId: P.parentId,
              rect: {
                width: W,
                height: H,
                ...tu({
                  x: D.x ?? P.position.x,
                  y: D.y ?? P.position.y
                }, { width: W, height: H }, P.parentId, C, F)
              }
            }, Z = ds([Y], C, k, A);
            T.push(...Z), $.x = D.x ? Math.max(F[0] * W, D.x) : void 0, $.y = D.y ? Math.max(F[1] * H, D.y) : void 0;
          }
          if ($.x !== void 0 && $.y !== void 0) {
            const F = {
              id: y,
              type: "position",
              position: { ...$ }
            };
            T.push(F);
          }
          if (D.width !== void 0 && D.height !== void 0) {
            const W = {
              id: y,
              type: "dimensions",
              resizing: !0,
              setAttributes: p ? p === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: D.width,
                height: D.height
              }
            };
            T.push(W);
          }
          for (const F of I) {
            const W = {
              ...F,
              type: "position"
            };
            T.push(W);
          }
          M(T);
        },
        onEnd: ({ width: D, height: I }) => {
          const M = {
            id: y,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: D,
              height: I
            }
          };
          v.getState().triggerNodeChanges([M]);
        }
      })), E.current.update({
        controlPosition: _,
        boundaries: {
          minWidth: c,
          minHeight: u,
          maxWidth: l,
          maxHeight: d
        },
        keepAspectRatio: f,
        resizeDirection: p,
        onResizeStart: m,
        onResize: w,
        onResizeEnd: x,
        shouldResize: g
      }), () => {
        E.current?.destroy();
      };
  }, [
    _,
    c,
    u,
    l,
    d,
    f,
    m,
    w,
    x,
    g
  ]);
  const R = _.split("-");
  return r.jsx("div", { className: Se(["react-flow__resize-control", "nodrag", ...R, n, i]), ref: N, style: {
    ...o,
    scale: S,
    ...a && { [b ? "backgroundColor" : "borderColor"]: a }
  }, children: s });
}
Ne(a0);
function c0(e, t) {
  return {
    ...t ? { name: t } : {},
    definitionId: e.definitionId,
    state: Ln(e.state),
    layout: e.layout
  };
}
function l0(e) {
  return JSON.stringify(
    {
      state: Ln(e.state),
      layout: e.layout
    },
    null,
    2
  );
}
function u0(e, t) {
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
      state: Gi(i.state),
      layout: i.layout ?? t.layout
    }
  };
}
function d0(e, t) {
  const n = (t ?? "workflow").trim().replace(/[^\w.-]+/g, "-") || "workflow", i = new Blob([JSON.stringify(e, null, 2)], { type: "application/json" }), o = URL.createObjectURL(i), s = document.createElement("a");
  s.href = o, s.download = `${n}.json`, document.body.appendChild(s), s.click(), s.remove(), URL.revokeObjectURL(o);
}
function nc({
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
function f0({
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
    () => d ? Od(d) : null,
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
        f ? /* @__PURE__ */ r.jsx(Hd, { fallback: /* @__PURE__ */ r.jsx(
          nc,
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
          nc,
          {
            document: e,
            readOnly: n,
            minHeight: o,
            ariaLabel: s,
            onChange: c
          }
        ),
        /* @__PURE__ */ r.jsx(p0, { diagnostics: u })
      ]
    }
  );
}
function p0({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ r.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const i = t.severity ?? "info", o = h0(t);
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
function h0(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const g0 = { language: "json", displayName: "JSON" };
function y0({ draft: e, onApply: t }) {
  const n = de(() => l0(e), [e]), [i, o] = B(n), [s, a] = B(n), [c, u] = B(null);
  J(() => {
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
          /* @__PURE__ */ r.jsx(nn, { size: 14 }),
          " Apply"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ r.jsx("div", { className: "wf-code-view-editor", children: /* @__PURE__ */ r.jsx(
      f0,
      {
        ariaLabel: "Workflow JSON",
        document: { uri: "elsa://workflows/definition.json", language: "json", value: i },
        languageAdapter: g0,
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
function Le(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function hs(e, t) {
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
function tn(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function gs(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ r.jsx(Rc, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ r.jsx(Fr, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ r.jsx(Kd, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ r.jsx(Bt, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ r.jsx(Bd, { size: 15 });
    default:
      return /* @__PURE__ */ r.jsx(Ui, { size: 15 });
  }
}
const m0 = ["Single", "Array", "List", "HashSet"];
function cd(e) {
  const [t, n] = B(null), [i, o] = B(null);
  J(() => {
    let u = !1;
    return Vp(e).then(
      (l) => {
        u || n(l);
      },
      () => {
        u || n([]);
      }
    ), Hp(e).then(
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
      const l = $s(u);
      return {
        value: l,
        label: Kc(u.displayName, l),
        group: u.category?.trim() || "Other"
      };
    }) : null,
    [t]
  ), a = de(
    () => i && i.length > 0 ? i.filter((u) => !u.deprecated).map((u) => ({
      value: u.typeName,
      label: bf(u.displayName, u.typeName)
    })) : null,
    [i]
  ), c = de(() => {
    const u = /* @__PURE__ */ new Map();
    for (const l of t ?? []) {
      const d = $s(l), f = l.defaultEditor?.trim();
      d && u.set(d, f && f.length > 0 ? f : "text");
    }
    return (l) => u.get(l) ?? "text";
  }, [t]);
  return { typeOptions: s, storageOptions: a, editorForAlias: c };
}
function x0(e) {
  return !e || e.length === 0 ? void 0 : (e.find((n) => /(^|\.)String$/i.test(n.value) || n.label.toLowerCase() === "string") ?? e[0]).value;
}
function w0(e, t, n) {
  return {
    add: () => {
      const i = df(n.namePrefix, e.map((o) => vn(o, n.nameKeys)));
      t([...e, n.create(i)]);
    },
    update: (i, o) => t(e.map((s, a) => a === i ? n.patch(s, o) : s)),
    remove: (i) => t(e.filter((o, s) => s !== i))
  };
}
function ic({ value: e, options: t, placeholder: n, allowEmpty: i, ariaLabel: o, onChange: s }) {
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
const v0 = {
  Single: "Single",
  Array: "Array",
  List: "List",
  HashSet: "Hash set"
};
function b0({ value: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ r.jsx("select", { "aria-label": t, value: e, onChange: (i) => n(i.target.value), children: m0.map((i) => /* @__PURE__ */ r.jsx("option", { value: i, children: v0[i] }, i)) });
}
function N0(e, t) {
  return t === "" ? !0 : e === "checkbox" ? t === "true" || t === "false" || t === "True" || t === "False" : e === "number" ? Number.isFinite(Number(t.trim())) && t.trim() !== "" : e === "date" ? /^\d{4}-\d{2}-\d{2}$/.test(t) : !0;
}
function j0({ value: e, editor: t, ariaLabel: n, onChange: i }) {
  const o = N0(t, e);
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
function S0({ title: e, addLabel: t, emptyLabel: n, headers: i, isEmpty: o, onAdd: s, children: a }) {
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-properties-section-head", children: [
      /* @__PURE__ */ r.jsx("h3", { children: e }),
      /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-properties-add", onClick: s, children: [
        /* @__PURE__ */ r.jsx(Kt, { size: 14 }),
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
function C0({ label: e, onRemove: t }) {
  return /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-properties-remove", "aria-label": e, title: e, onClick: t, children: /* @__PURE__ */ r.jsx(Sn, { size: 14 }) }) });
}
function E0({ checked: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ r.jsx("input", { type: "checkbox", "aria-label": t, checked: e, onChange: (i) => n(i.target.checked) });
}
function ys({
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
  const { add: g, update: m, remove: w } = w0(e, h, {
    namePrefix: o,
    nameKeys: s,
    create: (y) => l(y, x0(t)),
    patch: d
  }), x = ["Name", "Type", "Collection", ...f.default ? ["Default"] : [], ...f.storage ? ["Storage"] : [], ...f.required ? ["Required"] : []], j = o.toLowerCase();
  return /* @__PURE__ */ r.jsx(
    S0,
    {
      title: a,
      addLabel: c,
      emptyLabel: u,
      headers: x,
      isEmpty: e.length === 0,
      onAdd: g,
      children: e.map((y, v) => {
        const N = vn(y, s), b = Bc(y), S = vn(y, qc), E = S ? p?.get(S) : void 0, _ = b.collectionKind === "Single" ? i(b.alias) : "text";
        return /* @__PURE__ */ r.jsxs("tr", { children: [
          /* @__PURE__ */ r.jsxs("td", { children: [
            /* @__PURE__ */ r.jsx("input", { type: "text", "aria-label": `${o} name`, value: N, onChange: (R) => m(v, { name: R.target.value }) }),
            E ? /* @__PURE__ */ r.jsx("span", { className: "wf-properties-warning", role: "note", title: E, children: E }) : null
          ] }),
          /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            ic,
            {
              ariaLabel: `${o} type`,
              value: b.alias,
              options: t,
              placeholder: "Type",
              onChange: (R) => m(v, { type: { alias: R, collectionKind: b.collectionKind } })
            }
          ) }),
          /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            b0,
            {
              ariaLabel: `${o} collection kind`,
              value: b.collectionKind,
              onChange: (R) => m(v, { type: { alias: b.alias, collectionKind: R } })
            }
          ) }),
          f.default ? /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            j0,
            {
              ariaLabel: `${o} default value`,
              value: gf(y.default),
              editor: _,
              onChange: (R) => m(v, { default: hf(R) })
            }
          ) }) : null,
          f.storage ? /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            ic,
            {
              ariaLabel: `${o} storage driver`,
              value: vn(y, jf),
              options: n,
              placeholder: "—",
              allowEmpty: !0,
              onChange: (R) => m(v, { storageDriverType: R || null })
            }
          ) }) : null,
          f.required ? /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            E0,
            {
              ariaLabel: `${o} required`,
              checked: y.isRequired === !0,
              onChange: (R) => m(v, { isRequired: R })
            }
          ) }) : null,
          /* @__PURE__ */ r.jsx(C0, { label: `Remove ${j} ${N || v + 1}`, onRemove: () => w(v) })
        ] }, v);
      })
    }
  );
}
function ld({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, title: o = "Variables", addLabel: s = "Add variable", emptyLabel: a = "No variables defined.", warnings: c, onChange: u }) {
  return /* @__PURE__ */ r.jsx(
    ys,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Variable",
      nameKeys: Nf,
      title: o,
      addLabel: s,
      emptyLabel: a,
      create: (l, d) => ff({ name: l, alias: d }),
      patch: (l, d) => pf(l, d),
      columns: { default: !0, storage: !0 },
      warnings: c,
      onChange: u
    }
  );
}
function I0({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, onChange: o }) {
  return /* @__PURE__ */ r.jsx(
    ys,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Input",
      nameKeys: Xc,
      title: "Inputs",
      addLabel: "Add input",
      emptyLabel: "No inputs defined.",
      create: (s, a) => yf({ name: s, alias: a }),
      patch: (s, a) => mf(s, a),
      columns: { default: !1, storage: !0, required: !0 },
      onChange: o
    }
  );
}
function k0({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, onChange: o }) {
  return /* @__PURE__ */ r.jsx(
    ys,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Output",
      nameKeys: Xc,
      title: "Outputs",
      addLabel: "Add output",
      emptyLabel: "No outputs defined.",
      create: (s, a) => xf({ name: s, alias: a }),
      patch: (s, a) => wf(s, a),
      columns: { default: !1, storage: !1 },
      onChange: o
    }
  );
}
function Ii(e) {
  return (e ?? []).filter(Di);
}
function A0({ context: e, variables: t, title: n, addLabel: i, emptyLabel: o, warnings: s, onChange: a }) {
  const { typeOptions: c, storageOptions: u, editorForAlias: l } = cd(e);
  return /* @__PURE__ */ r.jsx(
    ld,
    {
      items: Ii(t),
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
function _0({ definition: e, definitionId: t, onMetaChange: n }) {
  const i = !!n, [o, s] = B(e?.name ?? ""), [a, c] = B(e?.description ?? "");
  J(() => {
    s(e?.name ?? "");
  }, [e?.name]), J(() => {
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
function D0({ details: e, draft: t, context: n, onStateChange: i, onDefinitionMetaChange: o }) {
  const { typeOptions: s, storageOptions: a, editorForAlias: c } = cd(n), u = Ii(t.state.variables), l = Ii(t.state.inputs), d = Ii(t.state.outputs), f = e?.versions ?? [];
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-properties-view", children: [
    /* @__PURE__ */ r.jsx(
      _0,
      {
        definition: e?.definition,
        definitionId: t.definitionId,
        onMetaChange: o
      }
    ),
    /* @__PURE__ */ r.jsx(
      ld,
      {
        items: u,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, variables: p }))
      }
    ),
    /* @__PURE__ */ r.jsx(
      I0,
      {
        items: l,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, inputs: p }))
      }
    ),
    /* @__PURE__ */ r.jsx(
      k0,
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
        /* @__PURE__ */ r.jsx("time", { children: Le(p.createdAt) })
      ] }, p.id)) })
    ] })
  ] });
}
const oc = "application/x-elsa-activity-version-id", T0 = 6, P0 = 1200, $0 = 250, M0 = [10, 25, 50], R0 = 10, rc = "elsa-studio-workflow-palette-width", sc = "elsa-studio-workflow-inspector-width", ac = "elsa-studio-workflow-palette-collapsed", cc = "elsa-studio-workflow-inspector-collapsed", ud = "elsa-studio-workflow-side-panel-maximized", yn = 180, mn = 460, z0 = 260, Vt = 260, Ot = 560, L0 = 320, lc = 42, mi = 16, dd = Ge.createContext(null), fd = Ge.createContext(null);
function V0(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function pd(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Et(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function It(e, t, n) {
  const i = t.createPrompt(n);
  return i ? (e.dispatchPrompt(i), !0) : !1;
}
function O0(e) {
  if (!e) return null;
  const t = [], n = e.match(/```(?:json)?\s*([\s\S]*?)```/i);
  n && t.push(n[1]);
  const i = e.match(/\{[\s\S]*?\}/);
  i && t.push(i[0]);
  for (const a of t)
    try {
      const c = JSON.parse(a.trim()), u = uc(c.name, c.description);
      if (u) return u;
    } catch {
    }
  const o = e.match(/name\s*[:-]\s*(.+)/i)?.[1], s = e.match(/description\s*[:-]\s*(.+)/i)?.[1];
  return uc(o, s);
}
function uc(e, t) {
  const n = typeof e == "string" ? dc(e) : void 0, i = typeof t == "string" ? dc(t) : void 0;
  return n || i ? { name: n || void 0, description: i || void 0 } : null;
}
function dc(e) {
  return e.trim().replace(/,$/, "").trim().replace(/^["']/, "").replace(/["']$/, "").trim();
}
function H0(e, t) {
  return e.rootActivityVersionId ?? W0(t, e.rootKind)?.activityVersionId ?? null;
}
function W0(e, t) {
  return e.find((n) => F0(n) === t);
}
function F0(e) {
  return e ? B0(e) ? "flowchart" : K0(e) ? "sequence" : null : null;
}
function $r(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const i = n.category?.trim() || "Uncategorized";
    t.set(i, [...t.get(i) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [i]) => n.localeCompare(i)).map(([n, i]) => ({
    category: n,
    activities: i.sort((o, s) => Ce(o).localeCompare(Ce(s)))
  }));
}
function B0(e) {
  return Ce(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function K0(e) {
  return Ce(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function X0(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function hd(e) {
  return G0(e.rootActivityType) || e.rootActivityType;
}
function q0(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function Y0(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function U0(e, t) {
  return fc(t) - fc(e);
}
function fc(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function gd(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
function yd(e) {
  const t = e?.workflowExecutionId ?? e?.runId ?? e?.executionId;
  return typeof t == "string" && t.trim() ? t : null;
}
async function Z0(e) {
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
function G0(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function J0(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    xi(t, n.typeName, n), xi(t, n.name, n), xi(t, n.displayName, n);
    const i = n.typeName.split(".").filter(Boolean).at(-1);
    xi(t, i, n);
  }
  return t;
}
function Q0(e, t, n) {
  const i = t.get(e.activityVersionId);
  return n.get(xn(i?.activityTypeKey)) ?? n.get(xn(tn(i?.activityTypeKey))) ?? n.get(xn(i?.displayName)) ?? n.get(xn(e.activityVersionId)) ?? null;
}
function xi(e, t, n) {
  const i = xn(t);
  i && !e.has(i) && e.set(i, n);
}
function xn(e) {
  return e?.trim().toLowerCase() ?? "";
}
function pc(e, t, n, i) {
  const o = fo();
  if (!o) return t;
  const s = o.getItem(e);
  if (s == null) return t;
  const a = Number(s);
  return Number.isFinite(a) ? ki(a, n, i) : t;
}
function hc(e, t) {
  const n = fo();
  if (!n) return t;
  const i = n.getItem(e);
  return i === "true" ? !0 : i === "false" ? !1 : t;
}
function eb() {
  const e = fo();
  if (!e) return null;
  const t = e.getItem(ud);
  return t === "palette" || t === "inspector" ? t : null;
}
function fo() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function fn(e, t) {
  const n = fo();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function ki(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function md(e) {
  switch (tb(e)) {
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
function tb(e) {
  return (e ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}
function nb(e, t) {
  const n = e.state.rootActivity;
  if (!n) return /* @__PURE__ */ new Set();
  const i = t.find((s) => s.activityVersionId === n.activityVersionId);
  if (qr(n, i) === "unsupported") return /* @__PURE__ */ new Set([n.nodeId]);
  const o = In(n, [], t);
  return new Set(o?.slot.activities.map((s) => s.nodeId) ?? [n.nodeId]);
}
function gc(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function yc(e) {
  return `${Ce(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function mc(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function ib(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function xd(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function ob(e) {
  const t = xd(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function rb(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function ze(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function sb(e) {
  return bd(ze(e));
}
function wd(e, t, n = []) {
  if (!e) return n;
  const i = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: i?.activityTypeKey ?? e.activityVersionId,
    displayName: i ? Ce(i) : void 0
  });
  for (const o of De(e, t))
    for (const s of o.activities) wd(s, t, n);
  return n;
}
function vd(e, t, n = []) {
  if (!e) return n;
  for (const i of rl(e))
    n.push({ source: i.source, target: i.target, sourcePort: i.sourceHandle ?? void 0, targetPort: i.targetHandle ?? void 0 });
  for (const i of De(e, t))
    for (const o of i.activities) vd(o, t, n);
  return n;
}
function wn(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function ab(e) {
  return `${e.id}-${bd(JSON.stringify(e.state))}`;
}
function bd(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function ms(e) {
  return e.status.toLowerCase() === "rejected";
}
function cb(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function lb(e, t) {
  const n = e instanceof Error ? e.message : String(e);
  return ub(e, n) ? `Run ${t} was not found.` : n;
}
function ub(e, t) {
  if ((typeof e == "object" && e ? e.response?.status ?? e.status : void 0) === 404 || /\b404\b/.test(t)) return !0;
  try {
    const i = JSON.parse(t);
    return [i.error, i.title, i.detail].some((o) => typeof o == "string" && /not found/i.test(o));
  } catch {
    return /not found/i.test(t);
  }
}
function sn({ status: e, subStatus: t }) {
  return /* @__PURE__ */ r.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
const Nd = { workflowActivity: db }, jd = { workflow: pb };
function db({ data: e, selected: t }) {
  const n = e, i = n.runtime, o = !n.suppressFlowPorts, s = o ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], a = fb(n), u = Ge.useContext(fd)?.({ activityVersionId: n.activityVersionId, activityTypeKey: n.activityTypeKey }) ?? null;
  return /* @__PURE__ */ r.jsxs(
    "div",
    {
      className: ["wf-node", t ? "selected" : "", i ? "wf-node-runtime" : "", i?.hasBlockingIncident ? "faulted" : "", u ? "wf-node-unavailable" : ""].filter(Boolean).join(" "),
      "data-icon": n.icon ?? "activity",
      children: [
        o && n.acceptsInbound ? /* @__PURE__ */ r.jsx(en, { type: "target", position: ie.Left }) : null,
        u ? /* @__PURE__ */ r.jsx("span", { className: "wf-node-availability", title: `No longer available for new use · ${Mi(u.state)}`, children: /* @__PURE__ */ r.jsx(_i, { size: 13 }) }) : null,
        /* @__PURE__ */ r.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ r.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: gs(n.icon) }),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-node-copy", children: [
            /* @__PURE__ */ r.jsx("strong", { children: n.label }),
            a ? /* @__PURE__ */ r.jsx("small", { children: a }) : null
          ] })
        ] }),
        n.childSlots.length > 0 ? /* @__PURE__ */ r.jsxs("span", { className: "wf-node-slot-badge", children: [
          n.childSlots.length,
          " slot",
          n.childSlots.length === 1 ? "" : "s"
        ] }) : null,
        i ? /* @__PURE__ */ r.jsxs("div", { className: "wf-node-runtime-strip", children: [
          i.status ? /* @__PURE__ */ r.jsx(sn, { status: i.status, subStatus: i.subStatus }) : null,
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
          return /* @__PURE__ */ r.jsxs(Ge.Fragment, { children: [
            /* @__PURE__ */ r.jsx("span", { className: "wf-node-port-label", style: { top: f }, children: l.displayName }),
            /* @__PURE__ */ r.jsx(en, { type: "source", position: ie.Right, id: l.name, style: { top: f } })
          ] }, l.name);
        })
      ]
    }
  );
}
function fb(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((o) => !!o).join(" · ");
}
function pb(e) {
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
  } = e, p = Ge.useContext(dd), [h, g] = B(!1), [m, w, x] = Xi({ sourceX: n, sourceY: i, targetX: o, targetY: s, sourcePosition: a, targetPosition: c }), j = p?.highlightedEdgeId === t;
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsx(
      Kn,
      {
        id: t,
        path: m,
        markerEnd: u,
        style: {
          ...l,
          strokeWidth: j ? 2.5 : l?.strokeWidth
        },
        label: d,
        labelX: w,
        labelY: x,
        labelStyle: f,
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1)
      }
    ),
    p ? /* @__PURE__ */ r.jsx(Rv, { children: /* @__PURE__ */ r.jsxs(
      "div",
      {
        className: ["wf-edge-actions", h ? "visible" : "", j ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${w}px, ${x}px)` },
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1),
        children: [
          /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (y) => p.requestInsertActivity(t, y.clientX, y.clientY), children: /* @__PURE__ */ r.jsx(Kt, { size: 12 }) }),
          /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => p.deleteEdge(t), children: /* @__PURE__ */ r.jsx(Sn, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function hb({ clientX: e, clientY: t, activities: n, onPick: i, onClose: o }) {
  const [s, a] = B(""), [c, u] = B(0), l = re(null), d = re(null), f = de(() => {
    const j = s.trim().toLowerCase(), y = n.filter(X0);
    return j ? y.filter((v) => Ce(v).toLowerCase().includes(j) || v.activityTypeKey.toLowerCase().includes(j) || (v.category ?? "").toLowerCase().includes(j) || (v.description ?? "").toLowerCase().includes(j)) : y;
  }, [n, s]), p = de(() => $r(f), [f]), h = de(() => p.flatMap((j) => j.activities), [p]);
  J(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), J(() => {
    const j = (v) => {
      l.current?.contains(v.target) || o();
    }, y = (v) => {
      v.key === "Escape" && o();
    };
    return document.addEventListener("mousedown", j, !0), document.addEventListener("keydown", y), () => {
      document.removeEventListener("mousedown", j, !0), document.removeEventListener("keydown", y);
    };
  }, [o]);
  const g = (j) => {
    if (j.key === "ArrowDown")
      j.preventDefault(), u((y) => Math.min(y + 1, h.length - 1));
    else if (j.key === "ArrowUp")
      j.preventDefault(), u((y) => Math.max(y - 1, 0));
    else if (j.key === "Enter") {
      j.preventDefault();
      const y = h[c];
      y && i(y);
    }
  }, m = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), w = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let x = -1;
  return /* @__PURE__ */ r.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: m, top: w }, onMouseDown: (j) => j.stopPropagation(), onClick: (j) => j.stopPropagation(), children: [
    /* @__PURE__ */ r.jsx(
      "input",
      {
        ref: d,
        type: "search",
        value: s,
        placeholder: "Search activities...",
        "aria-label": "Search activities",
        onChange: (j) => {
          a(j.target.value), u(0);
        },
        onKeyDown: g
      }
    ),
    /* @__PURE__ */ r.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: p.length === 0 ? /* @__PURE__ */ r.jsx("p", { children: "No matching activities." }) : p.map((j) => /* @__PURE__ */ r.jsxs("section", { children: [
      /* @__PURE__ */ r.jsx("h4", { children: j.category }),
      j.activities.map((y) => {
        x += 1;
        const v = x, N = v === c;
        return /* @__PURE__ */ r.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": N,
            className: N ? "active" : "",
            onMouseEnter: () => u(v),
            onClick: () => i(y),
            children: [
              /* @__PURE__ */ r.jsx("strong", { children: Ce(y) }),
              /* @__PURE__ */ r.jsx("small", { children: y.category || y.activityTypeKey })
            ]
          },
          y.activityVersionId
        );
      })
    ] }, j.category)) })
  ] });
}
function Ai({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: i
}) {
  const o = nf(t.map((s) => s.id), n, i);
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
function xc(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
const gb = "Expressions/UnresolvedVariable";
function yb(e) {
  return String(e.type ?? e.code ?? "");
}
function mb(e) {
  return yb(e) === gb;
}
function xb(e) {
  const t = String(e ?? "").trim().split("/").filter(Boolean), [n, i, ...o] = t, s = n === "$workflow";
  return {
    nodeId: !n || s ? null : n,
    isWorkflowScope: s,
    bag: i === "inputs" || i === "outputs" || i === "variables" ? i : null,
    referenceKey: o.length > 0 ? o.join("/") : null
  };
}
function wb(e) {
  return (e ?? []).filter(mb).map((t) => ({
    error: t,
    path: xb(t.path),
    message: t.message ?? "Variable reference is not visible from this activity's scope."
  }));
}
function vb({ draft: e, onRepair: t }) {
  const n = e.validationErrors;
  if (!n.length)
    return /* @__PURE__ */ r.jsxs("div", { className: "wf-validation ok", children: [
      /* @__PURE__ */ r.jsx(nn, { size: 14 }),
      " No validation errors"
    ] });
  const i = wb(n), o = new Map(i.map((s) => [s.error, s]));
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-validation-summary", children: [
      /* @__PURE__ */ r.jsx(ht, { size: 14 }),
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
          /* @__PURE__ */ r.jsx(Xd, { size: 12 }),
          " Repair"
        ] }) : null
      ] }, a);
    }) })
  ] });
}
function bb({
  testRun: e,
  onOpenDetails: t
}) {
  const n = ms(e);
  return /* @__PURE__ */ r.jsx("div", { className: "wf-test-run-status", "data-state": n ? "rejected" : "accepted", children: /* @__PURE__ */ r.jsxs(
    "button",
    {
      type: "button",
      className: "wf-test-run-trigger",
      onClick: t,
      children: [
        n ? /* @__PURE__ */ r.jsx(ht, { size: 16 }) : /* @__PURE__ */ r.jsx(nn, { size: 16 }),
        n ? "Test run rejected" : "Test run dispatched"
      ]
    }
  ) });
}
function Nb({ testRun: e, onOpenRun: t }) {
  if (!e)
    return /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Run the draft to see Runtime Evidence." }) });
  const n = ms(e), i = e.workflowExecutionId;
  return /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ r.jsxs("section", { className: "wf-runtime-card", "data-state": n ? "rejected" : "accepted", children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("span", { children: "Latest Test Run" }),
        /* @__PURE__ */ r.jsx("h3", { children: n ? "Rejected by the server" : "Transient run accepted" })
      ] }),
      /* @__PURE__ */ r.jsx(sn, { status: e.status, subStatus: e.commandDispatchStatus ?? void 0 })
    ] }),
    /* @__PURE__ */ r.jsx("p", { children: "Ephemeral - not saved, promoted, or published." }),
    n && e.reason ? /* @__PURE__ */ r.jsxs("div", { className: "wf-runtime-reason", children: [
      /* @__PURE__ */ r.jsx(ht, { size: 14 }),
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
        /* @__PURE__ */ r.jsx("dd", { children: wc(e.activityCount, "activity") })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ r.jsx("dd", { children: wc(e.incidentCount, "incident") })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ r.jsx("dd", { title: e.expiresAt ? Le(e.expiresAt) : "None", children: e.expiresAt ? Le(e.expiresAt) : "None" })
      ] })
    ] })
  ] }) });
}
function wc(e, t) {
  return typeof e != "number" ? "Available on linked Run" : `${e} ${t}${e === 1 ? "" : "s"}`;
}
function xs({ rows: e = 5 }) {
  return /* @__PURE__ */ r.jsx("div", { className: "wf-grid", "aria-busy": "true", "aria-label": "Loading", children: Array.from({ length: e }).map((t, n) => /* @__PURE__ */ r.jsx("div", { className: "wf-skeleton wf-skeleton-row", style: { width: `${90 - n % 3 * 12}%` } }, n)) });
}
function ws({ icon: e, title: t, description: n, action: i }) {
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-empty-state", role: "status", children: [
    /* @__PURE__ */ r.jsx("div", { className: "wf-empty-state-icon", "aria-hidden": !0, children: e ?? /* @__PURE__ */ r.jsx(Ui, { size: 22 }) }),
    /* @__PURE__ */ r.jsx("h3", { children: t }),
    n ? /* @__PURE__ */ r.jsx("p", { children: n }) : null,
    i ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty-state-action", children: i }) : null
  ] });
}
function Xn({ message: e, title: t = "Something went wrong" }) {
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-error-card", role: "alert", children: [
    /* @__PURE__ */ r.jsx(ht, { size: 18 }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-error-card-body", children: [
      /* @__PURE__ */ r.jsx("strong", { children: t }),
      /* @__PURE__ */ r.jsx("span", { children: e || "Please try again, or check that the Elsa server is reachable." })
    ] })
  ] });
}
function Sd({ status: e, run: t, compact: n = !1 }) {
  const i = () => {
    t?.workflowExecutionId && (window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(t.workflowExecutionId)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  };
  return /* @__PURE__ */ r.jsxs("div", { className: `wf-status-line${n ? " compact" : ""}`, children: [
    /* @__PURE__ */ r.jsx(nn, { size: n ? 13 : 14 }),
    /* @__PURE__ */ r.jsx("span", { children: e }),
    t?.workflowExecutionId ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: i, children: [
      "Open Run ",
      t.workflowExecutionId
    ] }) : null
  ] });
}
function Ft({ value: e, ariaLabel: t, copiedLabel: n, onCopied: i, onCopyFailed: o }) {
  if (!e) return null;
  const s = async (a) => {
    a.preventDefault(), a.stopPropagation();
    try {
      await Z0(e), i(n);
    } catch {
      o(n);
    }
  };
  return /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (a) => {
    s(a);
  }, children: /* @__PURE__ */ r.jsx(qd, { size: 12 }) });
}
function jb({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: i }) {
  const [o, s] = B("loading"), [a, c] = B(""), [u, l] = B(""), [d, f] = B(null), [p, h] = B([]), g = n?.trim().toLowerCase() ?? "", m = de(
    () => g ? p.filter((S) => q0(S, g)) : p,
    [g, p]
  ), w = de(
    () => Array.from(new Set(p.flatMap((S) => [
      S.definitionId,
      S.definitionVersionId,
      S.sourceId
    ]).filter((S) => !!S))).sort((S, E) => S.localeCompare(E)),
    [p]
  ), x = Et(t, "weaver.workflows.explain-executable"), j = se(async () => {
    s("loading"), c("");
    try {
      h(await dl(e)), s("ready");
    } catch (S) {
      c(S instanceof Error ? S.message : String(S)), s("failed");
    }
  }, [e]);
  J(() => {
    j();
  }, [j]);
  const y = async (S) => {
    l(""), f(null), c("");
    try {
      const E = await ul(e, S.artifactId), _ = yd(E);
      f({ artifactId: S.artifactId, workflowExecutionId: _ }), l(`Started ${S.artifactId}`);
    } catch (E) {
      c(E instanceof Error ? E.message : String(E));
    }
  }, v = (S) => {
    x && It(t, x, S) && (c(""), f(null), l(`Sent ${S.artifactId} to Weaver`));
  }, N = (S) => {
    c(""), f(null), l(`Copied ${S}`);
  }, b = (S) => {
    l(""), f(null), c(`Could not copy ${S}.`);
  };
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => {
        j();
      }, children: "Refresh" }),
      /* @__PURE__ */ r.jsxs("label", { className: "wf-search wf-executable-definition-filter", children: [
        /* @__PURE__ */ r.jsx(Yi, { size: 14 }),
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
        /* @__PURE__ */ r.jsx(zc, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    o === "failed" ? /* @__PURE__ */ r.jsx(Xn, { message: a }) : null,
    u ? /* @__PURE__ */ r.jsx(Sd, { status: u, run: d }) : null,
    o === "loading" ? /* @__PURE__ */ r.jsx(xs, {}) : null,
    o === "ready" && m.length === 0 ? /* @__PURE__ */ r.jsx(
      ws,
      {
        icon: /* @__PURE__ */ r.jsx(Bt, { size: 22 }),
        title: "No workflow executables",
        description: n ? "No executables match this definition filter." : "Publish a workflow definition to make it executable."
      }
    ) : null,
    o === "ready" && m.length > 0 ? /* @__PURE__ */ r.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ r.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ r.jsx("span", { children: "Version" }),
        /* @__PURE__ */ r.jsx("span", { children: "Source" }),
        /* @__PURE__ */ r.jsx("span", { children: "Root" }),
        /* @__PURE__ */ r.jsx("span", { children: "Published" }),
        /* @__PURE__ */ r.jsx("span", { children: "Actions" })
      ] }),
      m.map((S) => /* @__PURE__ */ r.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ r.jsxs("span", { className: "wf-artifact-cell", children: [
          /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line", children: [
            /* @__PURE__ */ r.jsx("strong", { title: S.artifactId, children: S.artifactId }),
            /* @__PURE__ */ r.jsx(Ft, { value: S.artifactId, ariaLabel: `Copy artifact ID ${S.artifactId}`, copiedLabel: "artifact ID", onCopied: N, onCopyFailed: b })
          ] }),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
            /* @__PURE__ */ r.jsx("small", { title: S.artifactHash, children: S.artifactHash }),
            /* @__PURE__ */ r.jsx(Ft, { value: S.artifactHash, ariaLabel: `Copy artifact hash ${S.artifactHash}`, copiedLabel: "artifact hash", onCopied: N, onCopyFailed: b })
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line wf-version-cell", children: [
          /* @__PURE__ */ r.jsx("span", { children: S.artifactVersion }),
          /* @__PURE__ */ r.jsx(Ft, { value: S.artifactVersion, ariaLabel: `Copy artifact version ${S.artifactVersion}`, copiedLabel: "artifact version", onCopied: N, onCopyFailed: b })
        ] }),
        /* @__PURE__ */ r.jsx(Sb, { executable: S, onCopied: N, onCopyFailed: b }),
        /* @__PURE__ */ r.jsx("span", { children: hd(S) }),
        /* @__PURE__ */ r.jsx("span", { children: Le(S.publishedAt ?? S.createdAt) }),
        /* @__PURE__ */ r.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
            y(S);
          }, children: [
            /* @__PURE__ */ r.jsx(Bt, { size: 13 }),
            " Run"
          ] }),
          x ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => v(S), children: [
            /* @__PURE__ */ r.jsx(rt, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, S.artifactId))
    ] }) : null
  ] });
}
function Sb({ executable: e, onCopied: t, onCopyFailed: n }) {
  const i = e.sourceId || e.definitionVersionId || e.definitionId, o = e.sourceVersion;
  return /* @__PURE__ */ r.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ r.jsx("span", { className: "wf-source-kind", children: gd(e.sourceKind) }),
    i ? /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line", children: [
      /* @__PURE__ */ r.jsx("code", { title: i, children: i }),
      /* @__PURE__ */ r.jsx(Ft, { value: i, ariaLabel: `Copy source ID ${i}`, copiedLabel: "source ID", onCopied: t, onCopyFailed: n })
    ] }) : null,
    o ? /* @__PURE__ */ r.jsxs("small", { children: [
      "Version ",
      o
    ] }) : null
  ] });
}
function Cb({ context: e, ai: t, definitionId: n, publishedArtifactId: i }) {
  const [o, s] = B("loading"), [a, c] = B(""), [u, l] = B(""), [d, f] = B(null), [p, h] = B([]), g = Et(t, "weaver.workflows.explain-executable"), m = se(async () => {
    s("loading"), c("");
    try {
      const N = await dl(e);
      h(N.filter((b) => Y0(b, n)).sort(U0)), s("ready");
    } catch (N) {
      c(N instanceof Error ? N.message : String(N)), h([]), s("failed");
    }
  }, [e, n]);
  J(() => {
    m();
  }, [m, i]);
  const w = async (N) => {
    l(""), f(null), c("");
    try {
      const b = await ul(e, N.artifactId);
      f({ artifactId: N.artifactId, workflowExecutionId: yd(b) }), l(`Started ${N.artifactId}`);
    } catch (b) {
      c(b instanceof Error ? b.message : String(b));
    }
  }, x = (N) => {
    g && It(t, g, N) && (c(""), f(null), l(`Sent ${N.artifactId} to Weaver`));
  }, j = () => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(n)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, y = (N) => {
    c(""), f(null), l(`Copied ${N}`);
  }, v = (N) => {
    l(""), f(null), c(`Could not copy ${N}.`);
  };
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-artifacts-panel", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-artifacts-toolbar", children: [
      /* @__PURE__ */ r.jsxs("span", { children: [
        p.length,
        " artifact",
        p.length === 1 ? "" : "s"
      ] }),
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
        m();
      }, children: [
        /* @__PURE__ */ r.jsx(Br, { size: 13 }),
        " Refresh"
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: j, children: "Open list" })
    ] }),
    o === "failed" ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ r.jsx(ht, { size: 14 }),
      " ",
      a
    ] }) : null,
    u ? /* @__PURE__ */ r.jsx(Sd, { status: u, run: d, compact: !0 }) : null,
    o === "loading" ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Loading artifacts..." }) : null,
    o === "ready" && p.length === 0 ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No published artifacts for this workflow yet." }) : null,
    o === "ready" && p.length > 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-artifact-list", role: "list", "aria-label": "Workflow artifacts", children: p.map((N) => /* @__PURE__ */ r.jsxs("article", { className: "wf-artifact-card", role: "listitem", "data-active": N.artifactId === i ? "true" : void 0, children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-artifact-card-heading", children: [
        /* @__PURE__ */ r.jsxs("div", { children: [
          /* @__PURE__ */ r.jsxs("span", { className: "wf-artifact-version", children: [
            "Version ",
            N.artifactVersion
          ] }),
          N.artifactId === i ? /* @__PURE__ */ r.jsx("span", { className: "wf-chip", children: "Latest publish" }) : null
        ] }),
        /* @__PURE__ */ r.jsx("span", { children: Le(N.publishedAt ?? N.createdAt) })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "wf-artifact-card-values", children: [
        /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line", children: [
          /* @__PURE__ */ r.jsx("code", { title: N.artifactId, children: N.artifactId }),
          /* @__PURE__ */ r.jsx(Ft, { value: N.artifactId, ariaLabel: `Copy artifact ID ${N.artifactId}`, copiedLabel: "artifact ID", onCopied: y, onCopyFailed: v })
        ] }),
        /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ r.jsx("code", { title: N.artifactHash, children: N.artifactHash }),
          /* @__PURE__ */ r.jsx(Ft, { value: N.artifactHash, ariaLabel: `Copy artifact hash ${N.artifactHash}`, copiedLabel: "artifact hash", onCopied: y, onCopyFailed: v })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("dl", { children: [
        /* @__PURE__ */ r.jsxs("div", { children: [
          /* @__PURE__ */ r.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ r.jsxs("dd", { children: [
            gd(N.sourceKind),
            " ",
            N.sourceVersion ? `v${N.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("div", { children: [
          /* @__PURE__ */ r.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ r.jsx("dd", { children: hd(N) })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
          w(N);
        }, children: [
          /* @__PURE__ */ r.jsx(Bt, { size: 13 }),
          " Run"
        ] }),
        g ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => x(N), children: [
          /* @__PURE__ */ r.jsx(rt, { size: 13 }),
          " Explain"
        ] }) : null
      ] })
    ] }, N.artifactId)) }) : null
  ] });
}
function Cd() {
  const [e, t] = B(() => pc(rc, z0, yn, mn)), [n, i] = B(() => pc(sc, L0, Vt, Ot)), [o, s] = B(() => hc(ac, !1)), [a, c] = B(() => hc(cc, !1)), [u, l] = B(eb);
  J(() => {
    fn(rc, String(e));
  }, [e]), J(() => {
    fn(sc, String(n));
  }, [n]), J(() => {
    fn(ac, String(o));
  }, [o]), J(() => {
    fn(cc, String(a));
  }, [a]), J(() => {
    fn(ud, u);
  }, [u]), J(() => {
    if (!u) return;
    const y = (v) => {
      v.key === "Escape" && l(null);
    };
    return window.addEventListener("keydown", y), () => window.removeEventListener("keydown", y);
  }, [u]);
  const d = se((y) => {
    l((v) => v === y ? null : v), y === "palette" ? s((v) => !v) : c((v) => !v);
  }, []), f = se((y) => {
    y === "palette" ? s(!1) : c(!1), l((v) => v === y ? null : y);
  }, []), p = se((y, v) => {
    l(null), y === "palette" ? (s(!1), t((N) => ki(N + v, yn, mn))) : (c(!1), i((N) => ki(N + v, Vt, Ot)));
  }, []), h = se((y, v) => {
    v.preventDefault(), l(null), y === "palette" ? s(!1) : c(!1);
    const N = v.clientX, b = y === "palette" ? e : n, S = y === "palette" ? yn : Vt, E = y === "palette" ? mn : Ot;
    document.body.classList.add("wf-side-panel-resizing");
    const _ = (D) => {
      const I = y === "palette" ? D.clientX - N : N - D.clientX, M = ki(b + I, S, E);
      y === "palette" ? t(M) : i(M);
    }, R = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", _), window.removeEventListener("pointerup", R), window.removeEventListener("pointercancel", R);
    };
    window.addEventListener("pointermove", _), window.addEventListener("pointerup", R), window.addEventListener("pointercancel", R);
  }, [n, e]), g = se((y, v) => {
    v.key === "ArrowLeft" ? (v.preventDefault(), p(y, y === "palette" ? -mi : mi)) : v.key === "ArrowRight" ? (v.preventDefault(), p(y, y === "palette" ? mi : -mi)) : v.key === "Home" ? (v.preventDefault(), y === "palette" ? t(yn) : i(Vt)) : v.key === "End" && (v.preventDefault(), y === "palette" ? t(mn) : i(Ot));
  }, [p]), m = !o && u !== "inspector", w = !a && u !== "palette", x = [
    "wf-editor-body",
    o ? "palette-collapsed" : "",
    a ? "inspector-collapsed" : "",
    u === "palette" ? "palette-maximized" : "",
    u === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), j = {
    "--wf-palette-width": `${o ? lc : e}px`,
    "--wf-inspector-width": `${a ? lc : n}px`
  };
  return {
    paletteWidth: e,
    inspectorWidth: n,
    paletteCollapsed: o,
    inspectorCollapsed: a,
    maximizedSidePanel: u,
    setInspectorCollapsed: c,
    paletteExpanded: m,
    inspectorExpanded: w,
    editorBodyClassName: x,
    editorBodyStyle: j,
    toggleSidePanelCollapsed: d,
    toggleSidePanelMaximized: f,
    startSidePanelResize: h,
    handleSidePanelResizeKeyDown: g
  };
}
const Eb = 50;
function vc() {
  return { past: [], future: [] };
}
function Ib(e) {
  return e.past.length > 0;
}
function kb(e) {
  return e.future.length > 0;
}
function bc(e, t, n = Eb) {
  const i = [...e.past, t];
  return i.length > n && i.splice(0, i.length - n), { past: i, future: [] };
}
function Ab(e, t) {
  if (e.past.length === 0) return null;
  const n = e.past.slice(), i = n.pop();
  return { history: { past: n, future: [...e.future, t] }, snapshot: i };
}
function _b(e, t) {
  if (e.future.length === 0) return null;
  const n = e.future.slice(), i = n.pop();
  return { history: { past: [...e.past, t], future: n }, snapshot: i };
}
function Db({ draft: e, restoreDraft: t }) {
  const n = re(vc()), i = re(null), o = re(""), s = re(!1), [a, c] = B(0), u = se((m) => {
    n.current = vc(), i.current = m ? wn(m) : null, o.current = m ? ze(m) : "", s.current = !1, c(0);
  }, []);
  J(() => {
    if (!e) return;
    if (s.current) {
      s.current = !1;
      return;
    }
    const m = ze(e);
    if (m === o.current) return;
    const w = window.setTimeout(() => {
      const x = i.current;
      x && (n.current = bc(n.current, x), c((j) => j + 1)), i.current = wn(e), o.current = m;
    }, $0);
    return () => window.clearTimeout(w);
  }, [e]);
  const l = se(() => {
    if (!e) return;
    const m = ze(e);
    if (m === o.current) return;
    const w = i.current;
    w && (n.current = bc(n.current, w)), i.current = wn(e), o.current = m;
  }, [e]), d = se((m) => {
    s.current = !0, i.current = wn(m), o.current = ze(m), t(m), c((w) => w + 1);
  }, [t]), f = se(() => {
    if (!e) return;
    l();
    const m = Ab(n.current, e);
    m && (n.current = m.history, d(m.snapshot));
  }, [e, l, d]), p = se(() => {
    if (!e) return;
    l();
    const m = _b(n.current, e);
    m && (n.current = m.history, d(m.snapshot));
  }, [e, l, d]), { canUndoNow: h, canRedoNow: g } = de(() => {
    const m = !!e && !!i.current && ze(e) !== o.current;
    return {
      canUndoNow: Ib(n.current) || m,
      canRedoNow: kb(n.current) && !m
    };
  }, [e, a]);
  return { resetHistory: u, undo: f, redo: p, canUndoNow: h, canRedoNow: g };
}
const Tb = {
  draft: null,
  frames: [],
  selectedNodeId: null,
  testRun: null,
  publishedArtifactId: null
};
function Pb(e, t) {
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
function $b() {
  const [e, t] = Wd(Pb, Tb), n = de(() => ({
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
const Mb = 320, Rb = 140;
function zb(e, t, n) {
  return n === "sequence" ? Lb(e) : Vb(e, t);
}
function Lb(e) {
  const t = /* @__PURE__ */ new Map();
  return e.forEach((n, i) => {
    t.set(n.id, { x: i * 280, y: 0 });
  }), t;
}
function Vb(e, t) {
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
      n.set(p, { x: d * Mb, y: h * Rb });
    });
  return n;
}
function Ob({
  draft: e,
  scope: t,
  scopeOwner: n,
  catalog: i,
  catalogByVersion: o,
  isUnsupportedDesigner: s,
  isFlowchartDesigner: a,
  canAddActivitiesToCanvas: c,
  selectedNodeId: u,
  editDraft: l,
  editDraftAndSelect: d,
  select: f,
  setStatus: p,
  setError: h
}) {
  const [g, m] = B([]), [w, x] = B([]), [j, y] = B(null), [v, N] = B(null), [b, S] = B(null), E = re(null), _ = re(null), R = re(null), D = re(null), I = re(!1);
  J(() => {
    if (!n) {
      m([]), x([]);
      return;
    }
    const L = s ? xr(n, i, e?.layout ?? []) : t ? Qc(t, i, e?.layout ?? []) : { nodes: [], edges: [] };
    m(L.nodes), x(L.edges);
  }, [i, e?.layout, s, t, n]);
  const M = se((L, O) => {
    if (e?.state.rootActivity && s)
      return;
    const K = vr(L, yc(L));
    if (!e?.state.rootActivity) {
      d(
        ({ draft: q }) => q ? { ...q, state: { ...q.state, rootActivity: K } } : null,
        K.nodeId
      );
      return;
    }
    if (!t) {
      if (!De(K, L)[0]) {
        p(""), h("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      d(({ draft: ee }) => {
        if (!ee?.state.rootActivity) return null;
        const ye = ee.state.rootActivity, pe = wr(K, [], [ye], L), je = O ? [
          ...ee.layout.filter((Ee) => Ee.nodeId !== ye.nodeId),
          {
            nodeId: ye.nodeId,
            x: Math.round(O.x),
            y: Math.round(O.y)
          }
        ] : ee.layout;
        return {
          ...ee,
          layout: je,
          state: {
            ...ee.state,
            rootActivity: pe
          }
        };
      }, e.state.rootActivity.nodeId), h(""), p(`Wrapped root in ${Ce(L)}`);
      return;
    }
    d(({ draft: q, frames: ee }) => {
      if (!q?.state.rootActivity) return null;
      const ye = In(q.state.rootActivity, ee, o);
      if (!ye) return null;
      const pe = ye.slot.cardinality === "single" ? [K] : [...ye.slot.activities, K], je = wr(q.state.rootActivity, ee, pe, o), Ee = O ? [
        ...q.layout.filter((Me) => Me.nodeId !== K.nodeId),
        {
          nodeId: K.nodeId,
          x: Math.round(O.x),
          y: Math.round(O.y)
        }
      ] : q.layout;
      return {
        ...q,
        layout: Ee,
        state: {
          ...q.state,
          rootActivity: je
        }
      };
    }, K.nodeId);
  }, [o, e?.state.rootActivity, s, t, d, h, p]), C = se((L, O) => {
    const K = vr(L, yc(L)), q = {
      id: K.nodeId,
      type: "workflowActivity",
      position: O,
      selected: !0,
      data: {
        label: Ce(L),
        activityVersionId: L.activityVersionId,
        activityTypeKey: L.activityTypeKey,
        category: L.category,
        executionType: L.executionType,
        icon: Ji(L),
        childSlots: De(K, L),
        acceptsInbound: String(L.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: sl(K, L)
      }
    };
    return { activityNode: K, node: q };
  }, []), k = se((L, O, K = []) => {
    s || l(({ draft: q, frames: ee }) => {
      if (!q) return null;
      const ye = Kf(q.layout, L), pe = q.state.rootActivity;
      if (!pe) return { ...q, layout: ye };
      const je = In(pe, ee, o);
      if (!je) return { ...q, layout: ye };
      const Ee = Ff(je, L, O, K), Me = je.slot.mode === "flowchart" ? Bf(Ee, O) : Ee;
      return {
        ...q,
        layout: ye,
        state: {
          ...q.state,
          rootActivity: el(pe, ee, Me, o)
        }
      };
    });
  }, [o, s, l]), A = se((L, O) => {
    if (!E.current) return null;
    const K = E.current.getBoundingClientRect();
    return j ? j.screenToFlowPosition({ x: L, y: O }) : {
      x: L - K.left,
      y: O - K.top
    };
  }, [j]), T = se((L, O) => document.elementFromPoint(L, O)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), $ = se((L, O, K) => {
    const q = g.find((ke) => ke.id === O.source), ee = g.find((ke) => ke.id === O.target), ye = q && ee ? ib(q, ee) : q ? mc(q) : K, pe = C(L, ye), Ee = [...g.map((ke) => ke.selected ? { ...ke, selected: !1 } : ke), pe.node], Me = Qf(w, O, pe.node.id);
    m(Ee), x(Me), f(pe.node.id), k(Ee, Me, [pe.activityNode]);
  }, [k, C, w, g, f]), P = se((L, O, K) => {
    if (!c || !E.current) return !1;
    const q = E.current.getBoundingClientRect();
    if (!(O >= q.left && O <= q.right && K >= q.top && K <= q.bottom)) return !1;
    const ye = A(O, K);
    if (!ye) return !1;
    if (a) {
      const pe = T(O, K), je = pe ? w.find((Ee) => Ee.id === pe) : void 0;
      if (je)
        return $(L, je, ye), !0;
    }
    return M(L, ye), !0;
  }, [M, c, w, T, a, $, A]);
  J(() => {
    const L = (K) => {
      const q = R.current;
      if (!q) return;
      Math.hypot(K.clientX - q.startX, K.clientY - q.startY) >= T0 && (q.dragging = !0);
    }, O = (K) => {
      const q = R.current;
      if (R.current = null, !q?.dragging || !E.current || D.current) return;
      const ee = E.current.getBoundingClientRect();
      K.clientX >= ee.left && K.clientX <= ee.right && K.clientY >= ee.top && K.clientY <= ee.bottom && (I.current = !0, window.setTimeout(() => {
        I.current = !1;
      }, 0), P(q.activity, K.clientX, K.clientY));
    };
    return window.addEventListener("pointermove", L), window.addEventListener("pointerup", O), window.addEventListener("pointercancel", O), () => {
      window.removeEventListener("pointermove", L), window.removeEventListener("pointerup", O), window.removeEventListener("pointercancel", O);
    };
  }, [j, P]);
  const F = (L, O) => {
    D.current = { activityVersionId: O.activityVersionId, handledDrop: !1 }, L.dataTransfer.setData(oc, O.activityVersionId), L.dataTransfer.setData("text/plain", O.activityVersionId), L.dataTransfer.effectAllowed = "copy";
  }, W = (L, O) => {
    const K = D.current;
    D.current = null, !K?.handledDrop && (L.clientX === 0 && L.clientY === 0 || P(O, L.clientX, L.clientY) && (I.current = !0, window.setTimeout(() => {
      I.current = !1;
    }, 0)));
  }, H = (L, O) => {
    L.button === 0 && (R.current = {
      activity: O,
      startX: L.clientX,
      startY: L.clientY,
      dragging: !1
    });
  }, Y = (L) => {
    I.current || c && M(L);
  }, Z = (L) => {
    if (!c) {
      L.dataTransfer.dropEffect = "none";
      return;
    }
    if (L.preventDefault(), L.dataTransfer.dropEffect = "copy", !a) return;
    const O = T(L.clientX, L.clientY);
    S(O);
  }, ne = (L) => {
    if (!E.current) return;
    const O = L.relatedTarget;
    O && E.current.contains(O) || S(null);
  }, le = (L) => {
    L.preventDefault(), S(null);
    const O = L.dataTransfer.getData(oc) || L.dataTransfer.getData("text/plain");
    if (!O || (L.stopPropagation(), D.current?.activityVersionId === O && (D.current.handledDrop = !0), !c)) return;
    const K = o.get(O);
    K && P(K, L.clientX, L.clientY);
  }, G = () => {
    if (!a) return;
    const L = E.current?.getBoundingClientRect();
    L && N({
      kind: "fromEmpty",
      clientX: L.left + L.width / 2,
      clientY: L.top + L.height / 2
    });
  }, z = (L) => {
    const O = s ? L.filter((K) => K.type === "select") : L;
    O.length !== 0 && m((K) => Iu(O, K));
  }, X = (L) => {
    s || x((O) => ku(L, O));
  }, ae = (L) => !L.source || !L.target || L.source === L.target || !a ? !1 : !L.targetHandle, ce = (L) => {
    if (!e?.state.rootActivity || !t || !a || !ae(L)) return;
    const O = Ti(L.source, L.target, L.sourceHandle ?? "Done", L.targetHandle ?? void 0), K = _u(O, w);
    x(K), k(g, K);
  }, Q = () => {
    k(g, w);
  }, oe = !s && g.length > 0, fe = se(() => {
    if (s || g.length === 0) return;
    const L = t?.slot.mode === "sequence" ? "sequence" : "flowchart", O = zb(g, w, L), K = g.map((q) => {
      const ee = O.get(q.id);
      return ee ? { ...q, position: ee } : q;
    });
    m(K), k(K, w), window.requestAnimationFrame(() => j?.fitView({ padding: 0.2 })), p("Rearranged the canvas.");
  }, [w, g, t, s, k, j, p]), V = (L, O) => {
    if (!O.nodeId || O.handleType === "target") {
      _.current = null;
      return;
    }
    _.current = {
      nodeId: O.nodeId,
      handleId: O.handleId ?? null
    };
  }, te = (L, O) => {
    const K = rb(_.current, O);
    if (_.current = null, !K || !a || O.toNode || O.toHandle || ob(L)) return;
    const q = xd(L);
    N({
      kind: "fromPort",
      sourceNodeId: K.nodeId,
      sourceHandleId: K.handleId,
      clientX: q.x,
      clientY: q.y
    });
  }, ge = (L, O) => {
    if (!a || !ae(O)) return;
    const K = pw(L, {
      ...O,
      sourceHandle: O.sourceHandle ?? "Done",
      // Connection.targetHandle is string | null; null and undefined are indistinguishable downstream
      // (every consumer checks truthiness), so keep the type-correct null.
      targetHandle: O.targetHandle ?? null
    }, w, { shouldReplaceId: !1 });
    x(K), k(g, K);
  }, me = (L) => {
    if (s || L.length === 0) return;
    const O = new Set(L.map((ee) => ee.id)), K = g.filter((ee) => !O.has(ee.id)), q = w.filter((ee) => !O.has(ee.source) && !O.has(ee.target));
    m(K), x(q), u && O.has(u) && f(null), k(K, q);
  }, Te = (L) => {
    if (s || L.length === 0) return;
    const O = new Set(L.map((q) => q.id)), K = w.filter((q) => !O.has(q.id));
    x(K), k(g, K);
  }, Ie = se((L) => {
    if (s) return;
    const O = w.filter((K) => K.id !== L);
    x(O), k(g, O);
  }, [k, w, s, g]), Ae = se((L, O, K) => {
    a && N({ kind: "spliceEdge", edgeId: L, clientX: O, clientY: K });
  }, [a]), tt = (L) => {
    const O = v;
    if (!O) return;
    N(null);
    const K = A(O.clientX, O.clientY) ?? { x: 0, y: 0 };
    if (O.kind === "fromEmpty") {
      const ee = C(L, K), pe = [...g.map((je) => je.selected ? { ...je, selected: !1 } : je), ee.node];
      m(pe), f(ee.node.id), k(pe, w, [ee.activityNode]);
      return;
    }
    if (O.kind === "fromPort") {
      const ee = g.find((ke) => ke.id === O.sourceNodeId), ye = ee ? mc(ee) : K, pe = C(L, ye), Ee = [...g.map((ke) => ke.selected ? { ...ke, selected: !1 } : ke), pe.node], Me = [...w, Ti(O.sourceNodeId, pe.node.id, O.sourceHandleId ?? "Done")];
      m(Ee), x(Me), f(pe.node.id), k(Ee, Me, [pe.activityNode]);
      return;
    }
    const q = w.find((ee) => ee.id === O.edgeId);
    q && $(L, q, K);
  }, We = de(() => ({
    highlightedEdgeId: b,
    deleteEdge: Ie,
    requestInsertActivity: Ae
  }), [Ie, b, Ae]);
  return {
    nodes: g,
    edges: w,
    canvasRef: E,
    setReactFlowInstance: y,
    connectMenu: v,
    setConnectMenu: N,
    edgeActions: We,
    onNodesChange: z,
    onEdgesChange: X,
    onNodesDelete: me,
    onEdgesDelete: Te,
    isValidConnection: ae,
    onConnect: ce,
    onConnectStart: V,
    onConnectEnd: te,
    onReconnect: ge,
    commitLayout: Q,
    canAutoLayout: oe,
    autoLayout: fe,
    onCanvasDragOver: Z,
    onCanvasDragLeave: ne,
    onCanvasDrop: le,
    openEmptyConnectMenu: G,
    onConnectMenuPick: tt,
    onPaletteClick: Y,
    onPaletteDragStart: F,
    onPaletteDragEnd: W,
    onPalettePointerDown: H
  };
}
const Hb = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function Ed(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function vs(e) {
  return Ed(e.name);
}
function Wb(e, t) {
  const n = vs(t), i = e[n];
  return t.isWrapped === !1 ? i ?? t.defaultValue ?? "" : kd(i, t);
}
function Id(e, t) {
  return kd(e[vs(t)], t);
}
function Fb(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function Bb(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function Nc(e, t, n) {
  return {
    ...e,
    [vs(t)]: n
  };
}
function Kb(e, t) {
  return t.isWrapped === !1 ? Wb(e, t) : Id(e, t).expression.value;
}
function kd(e, t) {
  return Qb(e) ? {
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
const Xb = /* @__PURE__ */ new Set([
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
function qb(e) {
  const t = e?.trim();
  if (!t) return null;
  const n = /^([\w.+]+)\[\]/.exec(t);
  if (n) return { elementTypeName: n[1] };
  const i = t.indexOf("`");
  if (i < 0) return null;
  const o = t.slice(0, i), s = (o.split(".").pop() ?? o).toLowerCase();
  return Xb.has(s) ? { elementTypeName: Yb(t.slice(i)) } : null;
}
function Yb(e) {
  const t = /\[\[([\w.+]+)/.exec(e);
  if (t) return t[1];
  const n = /\[([\w.+]+)/.exec(e);
  return n ? n[1] : null;
}
function Ub(e) {
  const t = e.uiHint?.toLowerCase();
  return t === "json" || t === "code" ? !0 : e.uiSpecifications?.repeater === !1;
}
function Zb(e) {
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
function Gb(e) {
  const t = (e ?? "").toLowerCase();
  return t === "system.boolean" || t === "boolean" || t === "bool" ? !1 : "";
}
function Jb(e, t) {
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
function Qb(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
function Ad(e) {
  return bs(e?.trim() ?? "") || e;
}
function bs(e) {
  if (!e) return "";
  const t = eN(e);
  if (!t) return "";
  const n = /^(.*)((?:\[\])+)$/.exec(t);
  if (n) return `${bs(n[1])}${n[2]}`;
  const i = t.indexOf("`");
  if (i >= 0) {
    const o = jc(t.slice(0, i)), s = tN(t.slice(i));
    return s.length > 0 ? `${o}<${s.join(", ")}>` : o;
  }
  return jc(t);
}
function eN(e) {
  let t = 0;
  for (let n = 0; n < e.length; n++) {
    const i = e[n];
    if (i === "[") t++;
    else if (i === "]") t--;
    else if (i === "," && t === 0) return e.slice(0, n).trim();
  }
  return e.trim();
}
function jc(e) {
  const t = e.split(".").filter(Boolean).at(-1) ?? e;
  return t.split("+").filter(Boolean).at(-1) ?? t;
}
function tN(e) {
  const t = e.indexOf("[");
  if (t < 0) return [];
  const n = Sc(e, t);
  return n == null ? [] : nN(n).map((i) => {
    const o = i.trim(), s = o.startsWith("[") ? Sc(o, 0) ?? o : o;
    return bs(s);
  }).filter(Boolean);
}
function Sc(e, t) {
  let n = 0;
  for (let i = t; i < e.length; i++)
    if (e[i] === "[") n++;
    else if (e[i] === "]" && --n === 0) return e.slice(t + 1, i);
  return null;
}
function nN(e) {
  const t = [];
  let n = 0, i = 0;
  for (let o = 0; o < e.length; o++) {
    const s = e[o];
    s === "[" ? n++ : s === "]" ? n-- : s === "," && n === 0 && (t.push(e.slice(i, o)), i = o + 1);
  }
  return t.push(e.slice(i)), t.map((o) => o.trim()).filter(Boolean);
}
const Cc = "elsa-studio:apply-workflow-graph-operation-batch", Ec = "elsa-studio:undo-workflow-graph-operation-batch", iN = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function oN(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const i = fN(e), o = Dd(i.state.rootActivity), s = /* @__PURE__ */ new Map(), a = /* @__PURE__ */ new Map(), c = [];
  for (const u of t.operations) {
    const l = dN(u.kind), d = u.parameters ?? {};
    if (l === "add-activity") {
      const f = Pe(d.activityId) ?? u.temporaryReferences?.[0], p = uN(f ?? Pe(d.displayName) ?? Pe(d.activityType) ?? "weaver-activity", o), h = rN(u, p, n);
      a.set(p, h), c.push(p), f && s.set(f, p), i.state.rootActivity && sN(i.state.rootActivity, h);
      const g = pt(d.position) ? Mr(d.position, { x: 280, y: 160 }) : null;
      g && (i.layout = Ic(i.layout, p, g));
      continue;
    }
    if (l === "set-root") {
      const f = gr(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown root activity.");
      i.state.rootActivity = f;
      continue;
    }
    if (l === "set-designer-position") {
      const f = kt(d.activityId, s);
      if (!f || !Ns(i.state.rootActivity, f)) throw new Error("Weaver batch referenced an unknown activity position.");
      i.layout = Ic(i.layout, f, Mr(d, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const f = gr(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity property target.");
      lN(f, Pe(d.propertyName) ?? "Value", d.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const f = gr(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity update target.");
      const p = pt(d.patch) ? d.patch : d;
      Object.assign(f, p);
      continue;
    }
    if (l === "remove-activity") {
      const f = kt(d.activityId, s);
      if (!f) throw new Error("Weaver batch referenced an unknown activity remove target.");
      i.state.rootActivity = _d(i.state.rootActivity, f), i.layout = i.layout.filter((p) => p.nodeId !== f);
      continue;
    }
    if (l === "connect-activities") {
      aN(i, d, s);
      continue;
    }
    if (l === "disconnect-activities") {
      cN(i, d, s);
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
function rN(e, t, n) {
  const i = e.parameters ?? {}, o = Pe(i.activityVersionId) ?? Pe(i.activityType) ?? "Elsa.Workflows.Activity", s = n.find((a) => a.activityVersionId === o || a.activityTypeKey === o || a.displayName === Pe(i.displayName));
  return s ? vr(s, t) : {
    nodeId: t,
    // catalogItem is provably undefined here (the truthy branch returned above), so use the id directly.
    activityVersionId: o,
    inputs: [],
    outputs: [],
    ...Pe(i.displayName) ? { displayName: Pe(i.displayName) } : {},
    designer: { position: Mr(i.position, { x: 280, y: 160 }) }
  };
}
function sN(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = js(e);
  n && !n.some((i) => i.nodeId === t.nodeId) && n.push(t);
}
function aN(e, t, n) {
  const i = e.state.rootActivity;
  if (!i?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const o = kt(t.sourceActivityId ?? t.sourceId ?? t.from, n), s = kt(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!o || !s) throw new Error("Weaver batch connection is missing source or target activity.");
  const a = i.structure.payload, c = Array.isArray(a.connections) ? a.connections : [], u = Pe(t.connectionId) ?? `flow-${o}-${s}`;
  a.connections = [
    ...c.filter((l) => !pt(l) || l.id !== u),
    {
      id: u,
      source: { nodeId: o, port: Pe(t.outcome) ?? Pe(t.sourcePort) ?? "Done" },
      target: { nodeId: s }
    }
  ];
}
function cN(e, t, n) {
  const i = e.state.rootActivity, o = i?.structure?.payload.connections;
  if (!Array.isArray(o)) return;
  const s = Pe(t.connectionId), a = kt(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = kt(t.targetActivityId ?? t.targetId ?? t.to, n);
  i.structure.payload.connections = o.filter((u) => {
    if (!pt(u)) return !0;
    if (s && u.id === s) return !1;
    const l = pt(u.source) ? u.source.nodeId : void 0, d = pt(u.target) ? u.target.nodeId : void 0;
    return l !== a || d !== c;
  });
}
function lN(e, t, n) {
  const i = pt(n);
  e[Ed(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: i ? "Object" : "Literal", value: n }
  };
}
function gr(e, t, n, i) {
  const o = kt(t, n);
  return o ? Ns(e.state.rootActivity, o) ?? i.get(o) ?? null : null;
}
function kt(e, t) {
  const n = Pe(e);
  return n ? t.get(n) ?? n : null;
}
function Ns(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of Td(e)) {
    const i = Ns(n, t);
    if (i) return i;
  }
  return null;
}
function _d(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = js(e);
  if (n) {
    const i = n.map((o) => _d(o, t)).filter((o) => !!o);
    n.splice(0, n.length, ...i);
  }
  return e;
}
function Dd(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of Td(e)) Dd(n, t);
  return t;
}
function Td(e) {
  return js(e) ?? [];
}
function js(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function Ic(e, t, n) {
  return [
    ...e.filter((i) => i.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function Mr(e, t) {
  const n = pt(e) ? e : {}, i = Number(n.x), o = Number(n.y);
  return {
    x: Number.isFinite(i) ? Math.max(40, Math.round(i)) : t.x,
    y: Number.isFinite(o) ? Math.max(40, Math.round(o)) : t.y
  };
}
function uN(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let i = n, o = 2;
  for (; t.has(i); )
    i = `${n}-${o}`, o += 1;
  return t.add(i), i;
}
function dN(e) {
  return typeof e == "number" ? iN[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Pe(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function fN(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function pt(e) {
  return typeof e == "object" && e !== null;
}
function pN({
  draft: e,
  details: t,
  catalog: n,
  replaceDraftByBatch: i,
  setStatus: o,
  setError: s
}) {
  const a = re(/* @__PURE__ */ new Map());
  J(() => {
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
        const p = wn(e), h = oN(e, d.batch, n), g = `weaver-batch-${Date.now()}`;
        a.current.set(g, p), i(h.draft, h.finalActivityIds.at(-1) ?? null), o(h.summary), s(""), d.respond({ ok: !0, result: { ...h, undoToken: g } });
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
    return window.addEventListener(Cc, c), window.addEventListener(Ec, u), () => {
      window.removeEventListener(Cc, c), window.removeEventListener(Ec, u);
    };
  }, [n, t, e, i, o, s]);
}
function hN({ context: e, draft: t, autosaveEnabledByDefault: n = !0, editDraft: i, setStatus: o, setError: s }) {
  const [a, c] = B(n), u = re(""), l = re(0), d = re(Promise.resolve()), f = se((h) => {
    u.current = h ? ze(h) : "";
  }, []), p = se(async (h, g) => {
    const m = async () => {
      const x = ++l.current, j = ze(h);
      s("");
      try {
        const y = await kp(e, h), v = ze(y);
        return u.current = v, i(({ draft: N }) => !N || N.id !== y.id ? null : ze(N) === j ? y : { ...N, validationErrors: y.validationErrors }), x === l.current && o(g), y;
      } catch (y) {
        throw x === l.current && (o(""), s(y instanceof Error ? y.message : String(y))), y;
      }
    }, w = d.current.then(m, m);
    return d.current = w.catch(() => {
    }), w;
  }, [e, i, o, s]);
  return J(() => {
    if (!a || !t || ze(t) === u.current) return;
    o("Autosaving...");
    const g = window.setTimeout(() => {
      p(t, "Autosaved").catch(() => {
      });
    }, P0);
    return () => window.clearTimeout(g);
  }, [a, t, p, o]), { saveDraft: p, autosaveEnabled: a, setAutosaveEnabled: c, markSaved: f };
}
function gN({ context: e, definitionId: t, resetHistory: n, loadDraft: i, markSaved: o, setError: s }) {
  const [a, c] = B(null), [u, l] = B([]), [d, f] = B([]), [p, h] = B(null), [g, m] = B(bi), [w, x] = B("loading"), j = se(async () => {
    s(""), x("loading");
    const [y, v, N, b, S] = await Promise.all([
      wp(e, t),
      Yr(e),
      zp(e).then(
        (_) => ({ ok: !0, descriptors: _ }),
        () => ({ ok: !1, descriptors: [] })
      ),
      Lp(e).then(
        (_) => ({ ok: !0, descriptors: _ }),
        () => ({ ok: !1, descriptors: bi })
      ),
      // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
      fl(e).then(
        (_) => _,
        () => null
      )
    ]), E = y.draft ?? null;
    c(y), o(E), n(E), i(E), l(v.activities ?? []), f(N.descriptors), h(S), m(b.descriptors.length > 0 ? b.descriptors : bi), x(N.ok ? "ready" : "failed");
  }, [e, t, n, i, o, s]);
  return J(() => {
    j().catch((y) => s(y instanceof Error ? y.message : String(y)));
  }, [j, s]), {
    details: a,
    setDetails: c,
    catalog: u,
    activityDescriptors: d,
    availabilityDiagnostics: p,
    expressionDescriptors: g,
    descriptorStatus: w,
    reload: j
  };
}
function yN({ context: e, details: t, setDetails: n, setStatus: i }) {
  const o = re(null), s = re(null), a = re({});
  J(() => {
    o.current = t;
  }, [t]);
  const c = se(() => {
    s.current !== null && (window.clearTimeout(s.current), s.current = null);
    const l = a.current;
    a.current = {};
    const d = o.current?.definition;
    !d || l.name === void 0 && l.description === void 0 || Ip(e, d.id, {
      name: l.name ?? d.name,
      description: l.description ?? d.description ?? null
    }).then((f) => n((p) => p && p.definition.id === f.definition.id ? { ...p, definition: f.definition } : p)).catch(() => i("Couldn't save name/description."));
  }, [e, n, i]), u = se((l) => {
    n((d) => d && { ...d, definition: { ...d.definition, ...l } }), a.current = { ...a.current, ...l }, s.current !== null && window.clearTimeout(s.current), s.current = window.setTimeout(c, 800);
  }, [c, n]);
  return J(() => () => {
    c();
  }, [c]), { updateDefinitionMeta: u };
}
function mN({
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
  const g = se(() => {
    if (!t) return;
    const j = n?.definition.name;
    d0(c0(t, j), j), d("Exported workflow as JSON.");
  }, [t, n, d]), m = se(async () => {
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
        const j = await Ap(e, t.id), y = await _p(e, j.versionId);
        u(y.artifactId), d(`Published ${y.artifactVersion}`), await s();
      } catch (j) {
        d(""), f(j instanceof Error ? j.message : String(j));
      } finally {
        l("idle");
      }
    }
  }, [t, i, e, o, s, u, l, d, f]), x = se(async () => {
    if (!t?.state.rootActivity || i) return;
    const j = t, y = ze(j);
    c(), d("Preparing test run...");
    try {
      l("testRunPreparing"), d("Preparing test run...");
      const v = ab(j);
      l("testRunStarting"), d("Starting test run...");
      const N = await Dp(e, {
        definitionId: j.definitionId,
        snapshotId: v,
        state: j.state
      });
      a({ draftSignature: y, view: N }), p("runtime"), h(!1), d(ms(N) ? "Test run rejected" : "Test run dispatched");
    } catch (v) {
      d(""), f(v instanceof Error ? v.message : String(v));
    } finally {
      l("idle");
    }
  }, [t, i, e, c, a, p, h, l, d, f]);
  return { exportJson: g, save: m, promoteAndPublish: w, run: x };
}
function xN({
  context: e,
  draft: t,
  frames: n,
  selectedNodeId: i,
  catalog: o,
  activityDescriptors: s,
  availabilityDiagnostics: a
}) {
  const c = t?.state.rootActivity ?? null, u = de(() => new Map(o.map((S) => [S.activityVersionId, S])), [o]), l = se(
    (S) => eh([S.activityVersionId, S.activityTypeKey], a),
    [a]
  ), d = de(() => J0(s), [s]), f = de(() => Gc(c, n, u), [c, n, u]), p = qr(f, f ? u.get(f.activityVersionId) : void 0), h = !!f && p === "unsupported", g = de(() => h ? null : In(c, n, u), [c, n, u, h]), m = de(() => h && f?.nodeId === i ? f : g?.slot.activities.find((S) => S.nodeId === i) ?? null, [h, g, f, i]), w = de(
    () => m ? Q0(m, u, d) : null,
    [u, d, m]
  ), x = de(
    () => m ? l({ activityVersionId: m.activityVersionId, activityTypeKey: u.get(m.activityVersionId)?.activityTypeKey }) : null,
    [l, u, m]
  ), j = m ? De(m, u) : [], y = m ? lp(m, u.get(m.activityVersionId)) : !1, v = bp(e, t?.state, i, u), N = p === "flowchart" && g?.slot.mode === "flowchart";
  return {
    catalogByVersion: u,
    availabilityLookup: l,
    scopeOwner: f,
    isUnsupportedDesigner: h,
    scope: g,
    selectedNode: m,
    selectedDescriptor: w,
    selectedNodeAvailability: x,
    selectedSlots: j,
    selectedSupportsScopedVariables: y,
    scopedVariableAnalysis: v,
    isFlowchartDesigner: N,
    canAddActivitiesToCanvas: !c || !h
  };
}
function wN({
  details: e,
  draft: t,
  selectedNode: n,
  selectedNodeId: i,
  selectedDescriptor: o,
  catalogByVersion: s
}) {
  J(() => {
    if (!(!e || !t))
      return window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
        workflowId: e.definition.id,
        workflowDefinitionId: e.definition.id,
        workflowVersionId: t.sourceVersionId ?? null,
        draftId: t.id,
        revision: sb(t),
        selectedNodeId: i,
        selectedActivityType: o?.typeName ?? (n ? s.get(n.activityVersionId)?.activityTypeKey ?? n.activityVersionId : null),
        summary: e.definition.name,
        activities: wd(t.state.rootActivity, s),
        connections: vd(t.state.rootActivity, s),
        diagnostics: t.validationErrors.map((a) => ({ severity: a.code ?? "warning", message: a.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === e.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [s, e, t, o, n, i]);
}
function vN({
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
      /* @__PURE__ */ r.jsx(Yi, { size: 14, "aria-hidden": "true" }),
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
              f ? /* @__PURE__ */ r.jsx(Lc, { size: 14 }) : /* @__PURE__ */ r.jsx(yt, { size: 14 }),
              /* @__PURE__ */ r.jsx("span", { children: d.category }),
              /* @__PURE__ */ r.jsx("small", { children: d.activities.length })
            ]
          }
        ),
        f ? /* @__PURE__ */ r.jsx("div", { className: "wf-palette-activities", role: "group", children: d.activities.map((p) => {
          const h = p.description?.trim(), g = h ? `wf-palette-description-${p.activityVersionId}` : void 0, m = Ce(p), w = Ji(p);
          return /* @__PURE__ */ r.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-activity",
              role: "treeitem",
              draggable: !0,
              title: h || Ce(p),
              "aria-describedby": g,
              onClick: () => s(p),
              onDragStart: (x) => a(x, p),
              onDragEnd: (x) => c(x, p),
              onPointerDown: (x) => u(x, p),
              children: [
                /* @__PURE__ */ r.jsx("span", { className: "wf-activity-icon", "data-icon": w, "aria-hidden": "true", children: gs(w) }),
                /* @__PURE__ */ r.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ r.jsx("strong", { children: m }),
                  h ? /* @__PURE__ */ r.jsx("small", { id: g, children: h }) : null
                ] }),
                /* @__PURE__ */ r.jsx(Vc, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            p.activityVersionId
          );
        }) }) : null
      ] }, d.category);
    }) })
  ] });
}
const Pd = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]), bN = "Variable";
function NN({
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
  const d = _N(l), f = o.length > 0 ? o : Hb;
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ r.jsx("span", { className: "wf-section-label", children: "Properties" }),
    d.map((p) => /* @__PURE__ */ r.jsxs("section", { className: "wf-property-group", children: [
      d.length > 1 ? /* @__PURE__ */ r.jsx("h4", { children: p.category }) : null,
      p.inputs.map((h) => /* @__PURE__ */ r.jsx(
        jN,
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
function jN({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: i,
  expressionDescriptors: o,
  visibleVariables: s,
  scopeStatus: a,
  onChange: c
}) {
  const u = t.isReadOnly === !0, l = { activity: e, expressionDescriptors: o, readOnly: u }, d = Vr(n, t, l), f = d?.component, p = t.isWrapped !== !1 ? Id(e, t) : null, h = p?.expression.type ?? "Literal", g = Kb(e, t), m = h.toLowerCase(), x = p && (m === "literal" || m === "object") && !Ub(t) ? qb(t.typeName) : null, j = x ? Vr(n, t, { ...l, scope: "collection" }) : void 0, y = p ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: o,
    readOnly: u,
    surface: "inline",
    syntax: h
  } : null, v = y ? Md(i, y) : null, N = v?.surfaces.inline, b = v && y ? Rd(v, y, g) : [], S = x != null, E = !!(p && !S && DN(t, d?.id)), _ = !!(p && !S && TN(t, d?.id)), [R, D] = B(!1), I = (A) => {
    const T = p ? Fb(p, A) : A;
    c(Nc(e, t, T));
  }, M = (A) => {
    p && c(Nc(e, t, Bb(p, A)));
  }, C = x ? j ? Rr(j.component, t, g, u, { ...l, scope: "collection" }, I) : /* @__PURE__ */ r.jsx(
    CN,
    {
      input: t,
      elementTypeName: x.elementTypeName,
      value: g,
      editors: n,
      context: l,
      disabled: u,
      onChange: I
    }
  ) : null, k = h === bN && p ? /* @__PURE__ */ r.jsx(
    kN,
    {
      value: g,
      visibleVariables: s,
      scopeStatus: a,
      disabled: u,
      onChange: I
    }
  ) : C ?? (N && y ? /* @__PURE__ */ r.jsx(
    N,
    {
      descriptor: t,
      syntax: h,
      value: g,
      disabled: u,
      context: y,
      onChange: I
    }
  ) : Rr(f, t, g, u, l, I));
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ r.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ r.jsx("span", { children: Ad(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ r.jsx("p", { children: t.description }) : null,
    p && !E ? /* @__PURE__ */ r.jsx(
      zr,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: h,
        descriptors: o,
        disabled: u,
        onChange: M
      }
    ) : null,
    E ? /* @__PURE__ */ r.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-expression-editor", children: [
        k,
        Or(b)
      ] }),
      /* @__PURE__ */ r.jsx(
        zr,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: h,
          descriptors: o,
          disabled: u,
          variant: "inline",
          onChange: M
        }
      ),
      _ ? /* @__PURE__ */ r.jsx(
        "button",
        {
          type: "button",
          className: "wf-expression-expand-button",
          "aria-label": `Open expanded ${t.displayName || t.name} editor`,
          title: "Open expanded editor",
          onClick: () => D(!0),
          children: /* @__PURE__ */ r.jsx(Cn, { size: 13 })
        }
      ) : null
    ] }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
      k,
      Or(b)
    ] }),
    _ && !E ? /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => D(!0),
        children: [
          /* @__PURE__ */ r.jsx(Cn, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    R ? /* @__PURE__ */ r.jsx(
      EN,
      {
        input: t,
        value: g,
        syntax: h,
        descriptors: o,
        activity: e,
        expressionEditors: i,
        disabled: u,
        onChange: I,
        onSyntaxChange: M,
        onClose: () => D(!1)
      }
    ) : null
  ] });
}
function SN(e, t, n) {
  return [
    "wf-collection-item",
    t === e ? "dragging" : "",
    t !== null && t !== e && n === e ? "drop-target" : ""
  ].filter(Boolean).join(" ");
}
function CN({
  input: e,
  elementTypeName: t,
  value: n,
  editors: i,
  context: o,
  disabled: s,
  onChange: a
}) {
  const c = Zb(n), u = Jb(e, t), l = { ...o, scope: "element" }, d = Vr(i, u, l)?.component, f = e.displayName || e.name, p = (N, b) => a(c.map((S, E) => E === N ? b : S)), [h, g] = B(null), [m, w] = B(null), x = () => {
    g(null), w(null);
  }, j = (N) => (b) => {
    g(N), b.dataTransfer.effectAllowed = "move", b.dataTransfer.setData("text/plain", String(N));
  }, y = (N) => (b) => {
    h !== null && (b.preventDefault(), b.dataTransfer.dropEffect = "move", m !== N && w(N));
  }, v = (N) => (b) => {
    b.preventDefault(), h !== null && h !== N && a(hr(c, h, N)), x();
  };
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-collection-editor", children: [
    c.length === 0 ? /* @__PURE__ */ r.jsx("p", { className: "wf-collection-empty", children: "No items yet." }) : /* @__PURE__ */ r.jsx("ul", { className: "wf-collection-items", children: c.map((N, b) => /* @__PURE__ */ r.jsxs(
      "li",
      {
        className: SN(b, h, m),
        onDragOver: y(b),
        onDrop: v(b),
        children: [
          /* @__PURE__ */ r.jsx(
            "span",
            {
              className: "wf-collection-item-handle",
              draggable: !s,
              "aria-label": `Drag ${f} item ${b + 1} to reorder`,
              title: "Drag to reorder",
              onDragStart: j(b),
              onDragEnd: x,
              children: /* @__PURE__ */ r.jsx(Vc, { size: 13, "aria-hidden": "true" })
            }
          ),
          /* @__PURE__ */ r.jsx("div", { className: "wf-collection-item-editor", children: Rr(d, u, N, s, l, (S) => p(b, S)) }),
          /* @__PURE__ */ r.jsxs("div", { className: "wf-collection-item-actions", children: [
            /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${b + 1} up`,
                disabled: s || b === 0,
                onClick: () => a(hr(c, b, b - 1)),
                children: /* @__PURE__ */ r.jsx(Yd, { size: 13 })
              }
            ),
            /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${b + 1} down`,
                disabled: s || b === c.length - 1,
                onClick: () => a(hr(c, b, b + 1)),
                children: /* @__PURE__ */ r.jsx(Lc, { size: 13 })
              }
            ),
            /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button danger",
                "aria-label": `Remove ${f} item ${b + 1}`,
                disabled: s,
                onClick: () => a(c.filter((S, E) => E !== b)),
                children: /* @__PURE__ */ r.jsx(Sn, { size: 13 })
              }
            )
          ] })
        ]
      },
      b
    )) }),
    /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-collection-add",
        disabled: s,
        onClick: () => a([...c, Gb(t)]),
        children: [
          /* @__PURE__ */ r.jsx(Kt, { size: 13 }),
          " Add item"
        ]
      }
    )
  ] });
}
function EN({
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
  }, h = Md(s, p), g = h?.surfaces.expanded, m = h ? Rd(h, p, t) : [], w = g ? null : AN(s, p);
  return J(() => {
    const x = (j) => {
      j.key === "Escape" && l();
    };
    return window.addEventListener("keydown", x), () => window.removeEventListener("keydown", x);
  }, [l]), /* @__PURE__ */ r.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ r.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": d, children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ r.jsx("h3", { id: d, children: f })
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": `Close ${f} editor`, onClick: l, children: /* @__PURE__ */ r.jsx(zc, { size: 16 }) })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ r.jsx(
          zr,
          {
            label: `${f} expression syntax`,
            value: n,
            descriptors: i,
            disabled: a,
            onChange: u
          }
        ),
        /* @__PURE__ */ r.jsx("span", { children: Ad(e.typeName) })
      ] }),
      e.description ? /* @__PURE__ */ r.jsx("p", { children: e.description }) : null,
      g ? /* @__PURE__ */ r.jsx(
        g,
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
      Or(m)
    ] }),
    /* @__PURE__ */ r.jsxs("footer", { children: [
      /* @__PURE__ */ r.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: l, children: "Close" })
    ] })
  ] }) });
}
function Rr(e, t, n, i, o, s) {
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
function zr({
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
const Lr = "::";
function $d(e) {
  return !e || e === $i ? $i : e;
}
function kc(e, t) {
  return `${$d(t)}${Lr}${e}`;
}
function IN(e) {
  const t = e.indexOf(Lr);
  if (t < 0) return null;
  const n = e.slice(t + Lr.length);
  return n ? { scopeId: e.slice(0, t), referenceKey: n } : null;
}
function kN({ value: e, visibleVariables: t, scopeStatus: n, disabled: i, onChange: o }) {
  const s = ll(e), a = !!e && typeof e == "object" || typeof e == "string" && e.trim().startsWith("{"), c = s && (a || t.some((d) => d.referenceKey === s.referenceKey)) ? s : null, u = c ? kc(c.referenceKey, c.declaringScopeId) : "", l = !!c && t.some(
    (d) => d.referenceKey === c.referenceKey && d.scopeId === $d(c.declaringScopeId)
  );
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-variable-picker", children: [
    /* @__PURE__ */ r.jsxs(
      "select",
      {
        "aria-label": "Variable reference",
        value: u,
        disabled: i,
        onChange: (d) => {
          const f = IN(d.target.value);
          f && o(dp(f.referenceKey, f.scopeId));
        },
        children: [
          /* @__PURE__ */ r.jsx("option", { value: "", children: "Select a variable…" }),
          c && !l ? /* @__PURE__ */ r.jsxs("option", { value: u, children: [
            c.referenceKey,
            " (not visible from this scope)"
          ] }) : null,
          t.map((d) => {
            const f = kc(d.referenceKey, d.scopeId);
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
function Vr(e, t, n) {
  return [...e].sort((i, o) => (i.order ?? 500) - (o.order ?? 500)).find((i) => i.supports(t, n));
}
function Md(e, t) {
  return [...e].sort((n, i) => (n.order ?? 500) - (i.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function Rd(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function AN(e, t) {
  if (t.syntax.toLowerCase() === "literal") return null;
  const n = [...e].sort((a, c) => (a.order ?? 500) - (c.order ?? 500)).find((a) => a.supports(t) && a.metadata)?.metadata;
  if (!n) return `No enhanced editor is registered for ${t.syntax}. Using the generic text editor.`;
  const i = n.displayName?.trim() || "enhanced editor", o = n.installHint?.trim(), s = `No ${i} is registered for ${t.syntax}. Using the generic text editor.`;
  return o ? `${s} ${o}` : s;
}
function Or(e) {
  return e.length === 0 ? null : /* @__PURE__ */ r.jsx("div", { className: "wf-expression-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const i = t.severity ?? "info";
    return /* @__PURE__ */ r.jsxs("p", { className: `wf-expression-editor-diagnostic ${i}`, children: [
      t.code ? /* @__PURE__ */ r.jsx("span", { children: t.code }) : null,
      t.message
    ] }, `${t.code ?? "diagnostic"}-${n}`);
  }) });
}
function _N(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const i = n.category?.trim() || "General";
    t.set(i, [...t.get(i) ?? [], n]);
  }
  return [...t.entries()].map(([n, i]) => ({ category: n, inputs: i }));
}
function DN(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !Pd.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function TN(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !Pd.has(t) && n !== "multiline") return !1;
  const i = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(i) || n === "singleline" || n === "multiline";
}
function PN({
  context: e,
  selectedNode: t,
  selectedNodeLabel: n,
  selectedActivityType: i,
  selectedDescriptor: o,
  selectedNodeAvailability: s,
  selectedSlots: a,
  selectedSupportsScopedVariables: c,
  propertyEditors: u,
  expressionEditors: l,
  expressionDescriptors: d,
  descriptorStatus: f,
  scopedVariableAnalysis: p,
  onSelectedActivityChange: h,
  onEnterSlot: g
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
      /* @__PURE__ */ r.jsx(_i, { size: 14 }),
      /* @__PURE__ */ r.jsxs("span", { children: [
        "No longer available for new use · ",
        Mi(s.state)
      ] })
    ] }) : null,
    /* @__PURE__ */ r.jsx(
      NN,
      {
        activity: t,
        descriptor: o,
        editors: u,
        expressionEditors: l,
        expressionDescriptors: d,
        descriptorStatus: f,
        visibleVariables: p.visibleVariables,
        scopeStatus: p.status,
        onChange: h
      }
    ),
    c ? /* @__PURE__ */ r.jsx("div", { className: "wf-container-variables", children: /* @__PURE__ */ r.jsx(
      A0,
      {
        context: e,
        variables: cl(t),
        title: "Container variables",
        addLabel: "Add container variable",
        emptyLabel: "No container variables declared on this activity.",
        warnings: pp(p.shadowingWarnings, t.nodeId),
        onChange: (m) => h(up(t, m))
      }
    ) }) : null,
    a.length > 0 ? /* @__PURE__ */ r.jsxs("div", { className: "wf-slot-list", children: [
      /* @__PURE__ */ r.jsx("span", { children: "Embedded slots" }),
      a.map((m) => /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => g(t, m.id, `${n} / ${m.label}`), children: [
        m.label,
        /* @__PURE__ */ r.jsxs("small", { children: [
          m.activities.length,
          " activit",
          m.activities.length === 1 ? "y" : "ies"
        ] })
      ] }, m.id))
    ] }) : /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
  ] }) : /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
}
function $N({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: i,
  expressionEditors: o,
  workflowDesignerPanels: s,
  autosaveEnabledByDefault: a,
  onBack: c
}) {
  const u = $b(), { draft: l, frames: d, selectedNodeId: f, testRun: p, publishedArtifactId: h } = u.state, {
    loadDraft: g,
    replaceDraftByBatch: m,
    editDraft: w,
    editDraftAndSelect: x,
    select: j,
    navigateToScope: y,
    resetToRoot: v,
    enterSlot: N,
    startTestRun: b,
    clearTestRun: S,
    setPublishedArtifact: E
  } = u, [_, R] = B(""), [D, I] = B(""), [M, C] = B("idle"), [k, A] = B(() => /* @__PURE__ */ new Set()), [T, $] = B(""), [P, F] = B("activities"), [W, H] = B("inspector"), [Y, Z] = B("designer"), {
    paletteWidth: ne,
    inspectorWidth: le,
    paletteCollapsed: G,
    inspectorCollapsed: z,
    maximizedSidePanel: X,
    setInspectorCollapsed: ae,
    paletteExpanded: ce,
    inspectorExpanded: Q,
    editorBodyClassName: oe,
    editorBodyStyle: fe,
    toggleSidePanelCollapsed: V,
    toggleSidePanelMaximized: te,
    startSidePanelResize: ge,
    handleSidePanelResizeKeyDown: me
  } = Cd(), { resetHistory: Te, undo: Ie, redo: Ae, canUndoNow: tt, canRedoNow: We } = Db({ draft: l, restoreDraft: g }), { saveDraft: L, autosaveEnabled: O, setAutosaveEnabled: K, markSaved: q } = hN({ context: e, draft: l, autosaveEnabledByDefault: a, editDraft: w, setStatus: I, setError: R }), {
    details: ee,
    setDetails: ye,
    catalog: pe,
    activityDescriptors: je,
    availabilityDiagnostics: Ee,
    expressionDescriptors: Me,
    descriptorStatus: ke,
    reload: ho
  } = gN({ context: e, definitionId: t, resetHistory: Te, loadDraft: g, markSaved: q, setError: R }), { updateDefinitionMeta: go } = yN({ context: e, details: ee, setDetails: ye, setStatus: I }), {
    catalogByVersion: He,
    availabilityLookup: yo,
    scopeOwner: qn,
    isUnsupportedDesigner: At,
    scope: mo,
    selectedNode: Ye,
    selectedDescriptor: _t,
    selectedNodeAvailability: xo,
    selectedSlots: Yn,
    selectedSupportsScopedVariables: wo,
    scopedVariableAnalysis: vo,
    isFlowchartDesigner: ct,
    canAddActivitiesToCanvas: bo
  } = xN({ context: e, draft: l, frames: d, selectedNodeId: f, catalog: pe, activityDescriptors: je, availabilityDiagnostics: Ee }), Dt = de(() => $r(pe), [pe]), No = de(() => {
    const U = T.trim().toLowerCase();
    if (!U) return Dt;
    const ue = pe.filter((ve) => Ce(ve).toLowerCase().includes(U) || ve.activityTypeKey.toLowerCase().includes(U) || (ve.category ?? "").toLowerCase().includes(U) || (ve.description ?? "").toLowerCase().includes(U));
    return $r(ue);
  }, [pe, T, Dt]), Tt = M !== "idle", jo = !!l?.state.rootActivity && !Tt, Un = Et(n, "weaver.workflows.find-draft-risks"), Zn = Et(n, "weaver.workflows.propose-update"), So = Ob({
    draft: l,
    scope: mo,
    scopeOwner: qn,
    catalog: pe,
    catalogByVersion: He,
    isUnsupportedDesigner: At,
    isFlowchartDesigner: ct,
    canAddActivitiesToCanvas: bo,
    selectedNodeId: f,
    editDraft: w,
    editDraftAndSelect: x,
    select: j,
    setStatus: I,
    setError: R
  }), {
    nodes: an,
    edges: Co,
    canvasRef: Eo,
    setReactFlowInstance: Io,
    connectMenu: Pt,
    setConnectMenu: Gn,
    edgeActions: Jn,
    onNodesChange: ko,
    onEdgesChange: Ao,
    onNodesDelete: _o,
    onEdgesDelete: Do,
    isValidConnection: To,
    onConnect: Po,
    onConnectStart: Qn,
    onConnectEnd: $o,
    onReconnect: Mo,
    commitLayout: Ro,
    canAutoLayout: zo,
    autoLayout: Lo,
    onCanvasDragOver: ei,
    onCanvasDragLeave: ti,
    onCanvasDrop: ni,
    openEmptyConnectMenu: ii,
    onConnectMenuPick: Vo,
    onPaletteClick: Oo,
    onPaletteDragStart: Ho,
    onPaletteDragEnd: Wo,
    onPalettePointerDown: Fo
  } = So;
  pN({ draft: l, details: ee, catalog: pe, replaceDraftByBatch: m, setStatus: I, setError: R }), J(() => {
    !l?.state.rootActivity || pe.length === 0 || w(({ draft: U }) => {
      if (!U?.state.rootActivity) return null;
      const ue = nl(U.state.rootActivity, He);
      return !ue || ue === U.state.rootActivity ? null : {
        ...U,
        state: {
          ...U.state,
          rootActivity: ue
        }
      };
    });
  }, [pe.length, He, l?.state.rootActivity, w]), wN({ details: ee, draft: l, selectedNode: Ye, selectedNodeId: f, selectedDescriptor: _t, catalogByVersion: He }), J(() => {
    A((U) => {
      let ue = !1;
      const ve = new Set(U);
      for (const nt of Dt)
        ve.has(nt.category) || (ve.add(nt.category), ue = !0);
      return ue ? ve : U;
    });
  }, [Dt]);
  const { exportJson: Bo, save: Ko, promoteAndPublish: Xo, run: oi } = mN({
    context: e,
    draft: l,
    details: ee,
    busy: Tt,
    saveDraft: L,
    reload: ho,
    startTestRun: b,
    clearTestRun: S,
    setPublishedArtifact: E,
    setOperation: C,
    setStatus: I,
    setError: R,
    setActiveRightPanelId: H,
    setInspectorCollapsed: ae
  }), qo = se((U) => {
    w(({ draft: ue }) => ue ? { ...ue, state: U(ue.state) } : null);
  }, [w]), ri = se((U) => {
    if (!l) return "No draft is loaded.";
    const ue = u0(U, l);
    return ue.ok ? (g(ue.draft), I("Applied workflow JSON."), null) : ue.error;
  }, [l, g]);
  J(() => {
    const U = (ue) => {
      if (Y !== "designer" || !(ue.metaKey || ue.ctrlKey)) return;
      const ve = ue.target;
      if (ve && (ve.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(ve.tagName))) return;
      const nt = ue.key.toLowerCase();
      nt === "z" && !ue.shiftKey ? (ue.preventDefault(), Ie()) : (nt === "z" && ue.shiftKey || nt === "y") && (ue.preventDefault(), Ae());
    };
    return window.addEventListener("keydown", U), () => window.removeEventListener("keydown", U);
  }, [Y, Ie, Ae]);
  const Yo = se((U) => {
    w(({ draft: ue }) => {
      const ve = ue?.state.rootActivity;
      return !ue || !ve ? null : {
        ...ue,
        state: {
          ...ue.state,
          rootActivity: tl(ve, U.nodeId, () => U, He)
        }
      };
    });
  }, [He, w]), Uo = se((U) => {
    if (!U) return;
    const ue = l?.state.rootActivity;
    if (!ue) return;
    const ve = Mf(ue, U, (nt) => {
      const Is = He.get(nt.activityVersionId);
      return Is ? Ce(Is) : nt.nodeId;
    }, He);
    ve && (Z("designer"), y(ve, U), ae(!1));
  }, [l?.state.rootActivity, He, ae, y]), cn = (U) => {
    A((ue) => {
      const ve = new Set(ue);
      return ve.has(U) ? ve.delete(U) : ve.add(U), ve;
    });
  };
  if (!ee || !l)
    return /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: _ || "Loading workflow editor..." });
  const $t = p?.draftSignature === ze(l) ? p.view : null, si = $t && D.startsWith("Test run") ? "" : D, ai = (U) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(U)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, zd = {
    definition: ee.definition,
    draft: l,
    selectedActivity: Ye,
    selectedActivityDescriptor: _t,
    selectedActivitySlots: Yn,
    catalog: pe,
    currentScopeOwner: qn,
    frames: d
  }, Ss = s.map((U) => {
    const ue = U.component;
    return {
      id: U.id,
      title: U.title,
      side: U.side,
      order: U.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ r.jsx(ue, { context: zd })
    };
  }), Zo = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ r.jsx(Ui, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(
        vN,
        {
          paletteSearch: T,
          onSearchChange: $,
          groups: No,
          expandedCategories: k,
          onToggleCategory: cn,
          onActivityClick: Oo,
          onActivityDragStart: Ho,
          onActivityDragEnd: Wo,
          onActivityPointerDown: Fo
        }
      )
    },
    ...Ss.filter((U) => U.side === "left")
  ].sort(xc), Go = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ r.jsx(Fr, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(
        PN,
        {
          context: e,
          selectedNode: Ye,
          selectedNodeLabel: Ye ? an.find((U) => U.id === Ye.nodeId)?.data.label ?? Ye.nodeId : "",
          selectedActivityType: Ye ? _t?.typeName ?? He.get(Ye.activityVersionId)?.activityTypeKey ?? "Unknown" : "",
          selectedDescriptor: _t,
          selectedNodeAvailability: xo,
          selectedSlots: Yn,
          selectedSupportsScopedVariables: wo,
          propertyEditors: i,
          expressionEditors: o,
          expressionDescriptors: Me,
          descriptorStatus: ke,
          scopedVariableAnalysis: vo,
          onSelectedActivityChange: Yo,
          onEnterSlot: N
        }
      )
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: /* @__PURE__ */ r.jsx(Bt, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(Nb, { testRun: $t, onOpenRun: ai })
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ r.jsx(Oc, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(
        Cb,
        {
          context: e,
          ai: n,
          definitionId: ee.definition.id,
          publishedArtifactId: h
        }
      )
    },
    ...Ss.filter((U) => U.side === "right")
  ].sort(xc), Cs = Zo.find((U) => U.id === P) ?? Zo[0], Es = Go.find((U) => U.id === W) ?? Go[0], Ld = [
    { id: "designer", title: "Designer", order: 0, icon: /* @__PURE__ */ r.jsx(Hc, { size: 14 }), render: () => null },
    { id: "code", title: "Code", order: 1, icon: /* @__PURE__ */ r.jsx(Qd, { size: 14 }), render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: /* @__PURE__ */ r.jsx(Wr, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-link-button", onClick: c, children: "Definitions" }),
      /* @__PURE__ */ r.jsx(yt, { size: 14 }),
      /* @__PURE__ */ r.jsx("strong", { children: ee.definition.name }),
      /* @__PURE__ */ r.jsx("span", { className: "wf-chip", children: "Draft" }),
      si ? /* @__PURE__ */ r.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ r.jsx(nn, { size: 13 }),
        " ",
        si
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
              disabled: !tt,
              onClick: Ie,
              children: /* @__PURE__ */ r.jsx(Ud, { size: 16 })
            }
          ),
          /* @__PURE__ */ r.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Redo",
              title: "Redo (Ctrl+Shift+Z)",
              disabled: !We,
              onClick: Ae,
              children: /* @__PURE__ */ r.jsx(Zd, { size: 16 })
            }
          ),
          /* @__PURE__ */ r.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Auto-layout",
              title: "Auto-layout the canvas",
              disabled: !zo,
              onClick: Lo,
              children: /* @__PURE__ */ r.jsx(Gd, { size: 16 })
            }
          )
        ] }),
        /* @__PURE__ */ r.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ r.jsx("input", { className: "wf-autosave-switch-input", type: "checkbox", checked: O, onChange: (U) => K(U.target.checked) }),
          /* @__PURE__ */ r.jsx("span", { children: "Autosave" })
        ] }),
        Un ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => It(n, Un, { definition: ee.definition, draft: l }), children: [
          /* @__PURE__ */ r.jsx(rt, { size: 15 }),
          " Risks"
        ] }) : null,
        Zn ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => It(n, Zn, { definition: ee.definition, draft: l }), children: [
          /* @__PURE__ */ r.jsx(rt, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ r.jsxs("button", { type: "button", title: "Export workflow as JSON", onClick: Bo, children: [
          /* @__PURE__ */ r.jsx(Jd, { size: 15 }),
          " Export"
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: Tt, onClick: () => {
          Ko();
        }, children: [
          /* @__PURE__ */ r.jsx($c, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: Tt, onClick: () => {
          Xo();
        }, children: [
          /* @__PURE__ */ r.jsx(Rc, { size: 15 }),
          " Promote"
        ] }),
        $t ? /* @__PURE__ */ r.jsx(
          bb,
          {
            testRun: $t,
            onOpenDetails: () => {
              H("runtime"), ae(!1);
            }
          }
        ) : null,
        /* @__PURE__ */ r.jsxs(
          "button",
          {
            type: "button",
            disabled: !jo,
            title: l.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              oi();
            },
            children: [
              /* @__PURE__ */ r.jsx(Bt, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    _ ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ r.jsx(ht, { size: 16 }),
      " ",
      _
    ] }) : null,
    /* @__PURE__ */ r.jsxs("div", { className: oe, style: fe, children: [
      /* @__PURE__ */ r.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ r.jsx(
            Ai,
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
                "aria-label": G ? "Expand activities panel" : "Collapse activities panel",
                title: G ? "Expand" : "Collapse",
                onClick: () => V("palette"),
                children: G ? /* @__PURE__ */ r.jsx(yt, { size: 14 }) : /* @__PURE__ */ r.jsx(En, { size: 14 })
              }
            ),
            G ? null : /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": X === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: X === "palette" ? "Restore" : "Maximize",
                onClick: () => te("palette"),
                children: X === "palette" ? /* @__PURE__ */ r.jsx(yr, { size: 14 }) : /* @__PURE__ */ r.jsx(Cn, { size: 14 })
              }
            )
          ] })
        ] }),
        ce ? Cs.render() : null
      ] }),
      ce && !X ? /* @__PURE__ */ r.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": yn,
          "aria-valuemax": mn,
          "aria-valuenow": ne,
          tabIndex: 0,
          onPointerDown: (U) => ge("palette", U),
          onKeyDown: (U) => me("palette", U)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ r.jsx("div", { className: "wf-canvas-tabs", children: /* @__PURE__ */ r.jsx(
          Ai,
          {
            label: "Editor view tabs",
            tabs: Ld,
            activeTabId: Y,
            onSelect: (U) => Z(U)
          }
        ) }),
        Y === "code" ? /* @__PURE__ */ r.jsx(y0, { draft: l, onApply: ri }) : Y === "properties" ? /* @__PURE__ */ r.jsx(D0, { details: ee, draft: l, context: e, onStateChange: qo, onDefinitionMetaChange: go }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
          /* @__PURE__ */ r.jsxs("div", { className: "wf-breadcrumb", children: [
            /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => v(), children: "Root" }),
            d.map((U, ue) => /* @__PURE__ */ r.jsxs(Ge.Fragment, { children: [
              /* @__PURE__ */ r.jsx(yt, { size: 13 }),
              /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => y(d.slice(0, ue + 1), null), children: U.label })
            ] }, `${U.ownerNodeId}-${U.slotId}-${ue}`))
          ] }),
          /* @__PURE__ */ r.jsxs("div", { className: "wf-canvas", ref: Eo, onDragOver: ei, onDragLeave: ti, onDrop: ni, children: [
            /* @__PURE__ */ r.jsx(dd.Provider, { value: Jn, children: /* @__PURE__ */ r.jsx(fd.Provider, { value: yo, children: /* @__PURE__ */ r.jsxs(
              td,
              {
                nodes: an,
                edges: Co,
                nodeTypes: Nd,
                edgeTypes: jd,
                onInit: Io,
                onNodesChange: ko,
                onEdgesChange: Ao,
                onNodesDelete: _o,
                onEdgesDelete: Do,
                onConnect: Po,
                onConnectStart: ct ? Qn : void 0,
                onConnectEnd: ct ? $o : void 0,
                onReconnect: ct ? Mo : void 0,
                isValidConnection: To,
                onDragOver: ei,
                onDragLeave: ti,
                onDrop: ni,
                onPaneClick: () => j(null),
                onNodeClick: (U, ue) => j(ue.id),
                onNodeDragStop: At ? void 0 : Ro,
                fitView: !0,
                minZoom: 0.2,
                maxZoom: 1.8,
                nodesConnectable: ct,
                nodesDraggable: !At,
                selectionOnDrag: !0,
                multiSelectionKeyCode: ["Shift", "Meta", "Control"],
                deleteKeyCode: At ? null : ["Backspace", "Delete"],
                panActivationKeyCode: null,
                defaultEdgeOptions: { type: "workflow" },
                children: [
                  /* @__PURE__ */ r.jsx(id, { gap: 18, size: 1 }),
                  /* @__PURE__ */ r.jsx(rd, {}),
                  /* @__PURE__ */ r.jsx(ad, { pannable: !0, zoomable: !0 })
                ]
              }
            ) }) }),
            ct && an.length === 0 ? /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => ii(), children: [
              /* @__PURE__ */ r.jsx(Kt, { size: 15 }),
              " Add activity"
            ] }) : null,
            Pt ? /* @__PURE__ */ r.jsx(
              hb,
              {
                clientX: Pt.clientX,
                clientY: Pt.clientY,
                activities: pe,
                onPick: Vo,
                onClose: () => Gn(null)
              }
            ) : null
          ] }),
          /* @__PURE__ */ r.jsx(vb, { draft: l, onRepair: Uo })
        ] })
      ] }),
      Q && !X ? /* @__PURE__ */ r.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Vt,
          "aria-valuemax": Ot,
          "aria-valuenow": le,
          tabIndex: 0,
          onPointerDown: (U) => ge("inspector", U),
          onKeyDown: (U) => me("inspector", U)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ r.jsx(
            Ai,
            {
              label: "Inspector panel tabs",
              tabs: Go,
              activeTabId: Es.id,
              onSelect: H
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
                children: z ? /* @__PURE__ */ r.jsx(En, { size: 14 }) : /* @__PURE__ */ r.jsx(yt, { size: 14 })
              }
            ),
            z ? null : /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": X === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: X === "inspector" ? "Restore" : "Maximize",
                onClick: () => te("inspector"),
                children: X === "inspector" ? /* @__PURE__ */ r.jsx(yr, { size: 14 }) : /* @__PURE__ */ r.jsx(Cn, { size: 14 })
              }
            )
          ] })
        ] }),
        Q ? Es.render() : null
      ] })
    ] })
  ] });
}
function MN({ page: e, pageSize: t, totalCount: n, onPageChange: i, onPageSizeChange: o }) {
  const s = pd(n, t), a = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
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
      /* @__PURE__ */ r.jsx("select", { value: t, onChange: (u) => o(Number(u.target.value)), children: M0.map((u) => /* @__PURE__ */ r.jsx("option", { value: u, children: u }, u)) })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => i(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ r.jsx(En, { size: 14 }),
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
const RN = [
  { value: "flowchart", label: "Flowchart", hint: "Free-form graph of connected activities." },
  { value: "sequence", label: "Sequence", hint: "Ordered list of activities that run top to bottom." }
];
function zN({ draft: e, creating: t, ai: n, suggestMetadataAction: i, onChange: o, onClose: s, onSubmit: a }) {
  const [c, u] = B(!1), [l, d] = B(""), [f, p] = B(!1), [h, g] = B(null), [m, w] = B(null), x = re(null), j = re(e);
  j.current = e;
  const y = re(o);
  y.current = o;
  const v = se((b) => {
    const S = { ...j.current };
    b.name && (S.name = b.name), b.description && (S.description = b.description), y.current(S), g(null), w(null);
  }, []);
  J(() => {
    if (i)
      return n.onPromptResult((b) => {
        if (b.requestId !== x.current) return;
        if (x.current = null, p(!1), b.status !== "completed") {
          w(b.status === "cancelled" ? "Weaver needs more detail — continue in the assistant panel." : "Weaver couldn't generate a suggestion. Try again or fill the fields manually.");
          return;
        }
        const S = O0(b.text);
        if (!S) {
          w("Couldn't read a suggestion from Weaver's reply. See the assistant panel.");
          return;
        }
        b.autoApply ? v(S) : g(S);
      });
  }, [n, i, v]);
  const N = () => {
    if (!i) return;
    const b = i.createPrompt({ draft: j.current, intent: l });
    if (!b) return;
    const S = `wf-suggest-${Date.now()}-${Math.round(Math.random() * 1e6)}`;
    x.current = S, p(!0), g(null), w(null), n.dispatchPrompt({ ...b, requestId: S });
  };
  return /* @__PURE__ */ r.jsx("div", { className: "wf-dialog-backdrop", role: "presentation", children: /* @__PURE__ */ r.jsx("section", { className: "wf-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "workflow-create-title", children: /* @__PURE__ */ r.jsxs(
    "form",
    {
      onSubmit: (b) => {
        b.preventDefault(), a();
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
              onClick: () => u((b) => !b),
              title: i.description ?? i.label,
              children: [
                /* @__PURE__ */ r.jsx(rt, { size: 13 }),
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
                onChange: (b) => d(b.target.value),
                onKeyDown: (b) => {
                  (b.metaKey || b.ctrlKey) && b.key === "Enter" && (b.preventDefault(), N());
                }
              }
            )
          ] }),
          /* @__PURE__ */ r.jsx("div", { className: "wf-ai-suggest-actions", children: /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-ai-action", onClick: N, disabled: f, children: [
            /* @__PURE__ */ r.jsx(rt, { size: 13 }),
            " ",
            f ? "Generating…" : "Generate"
          ] }) }),
          m ? /* @__PURE__ */ r.jsx("p", { className: "wf-ai-suggest-error", role: "alert", children: m }) : null,
          h ? /* @__PURE__ */ r.jsxs("div", { className: "wf-ai-suggest-preview", children: [
            h.name ? /* @__PURE__ */ r.jsx("p", { children: /* @__PURE__ */ r.jsx("strong", { children: h.name }) }) : null,
            h.description ? /* @__PURE__ */ r.jsx("p", { children: h.description }) : null,
            /* @__PURE__ */ r.jsxs("div", { className: "wf-ai-suggest-actions", children: [
              /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => v(h), children: "Apply" }),
              /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => g(null), children: "Dismiss" })
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
              onChange: (b) => o({ ...e, name: b.target.value })
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
              onChange: (b) => o({ ...e, description: b.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ r.jsxs("fieldset", { className: "wf-form-field wf-root-field", children: [
          /* @__PURE__ */ r.jsx("legend", { children: "Root activity" }),
          /* @__PURE__ */ r.jsx("div", { className: "wf-root-cards", role: "radiogroup", "aria-label": "Root activity", children: RN.map((b) => {
            const S = e.rootKind === b.value;
            return /* @__PURE__ */ r.jsxs("label", { className: "wf-root-card", "data-checked": S || void 0, children: [
              /* @__PURE__ */ r.jsx(
                "input",
                {
                  type: "radio",
                  name: "wf-root-kind",
                  "aria-label": b.label,
                  value: b.value,
                  checked: S,
                  onChange: () => o({ ...e, rootKind: b.value, rootActivityVersionId: null })
                }
              ),
              /* @__PURE__ */ r.jsx("span", { className: "wf-root-card-title", children: b.label }),
              /* @__PURE__ */ r.jsx("span", { className: "wf-root-card-hint", children: b.hint })
            ] }, b.value);
          }) })
        ] }),
        /* @__PURE__ */ r.jsxs("div", { className: "wf-dialog-actions", children: [
          /* @__PURE__ */ r.jsx("button", { type: "button", onClick: s, disabled: t, children: "Cancel" }),
          /* @__PURE__ */ r.jsx("button", { type: "submit", disabled: t || !e.name.trim(), children: t ? "Creating..." : "Create" })
        ] })
      ]
    }
  ) }) });
}
function LN({ context: e, ai: t, onOpen: n }) {
  const [i, o] = B(""), [s, a] = B("active"), [c, u] = B(1), [l, d] = B(R0), [f, p] = B("loading"), [h, g] = B(""), [m, w] = B(""), [x, j] = B([]), [y, v] = B(0), [N, b] = B(() => /* @__PURE__ */ new Set()), [S, E] = B(null), [_, R] = B(!1), [D, I] = B([]), [M, C] = B("idle"), k = re(null), A = de(() => x.map((V) => V.id), [x]), T = Et(t, "weaver.workflows.suggest-create-metadata"), $ = Et(t, "weaver.workflows.explain-definition"), P = A.filter((V) => N.has(V)).length, F = A.length > 0 && P === A.length, W = se(async () => {
    p("loading"), g("");
    try {
      const V = await xp(e, { search: i, state: s, page: c, pageSize: l }), te = typeof V.totalCount == "number", ge = V.totalCount ?? V.definitions.length, me = pd(ge, l);
      if (ge > 0 && c > me) {
        u(me);
        return;
      }
      j(te ? V.definitions : V0(V.definitions, c, l)), v(ge), p("ready");
    } catch (V) {
      g(V instanceof Error ? V.message : String(V)), p("failed");
    }
  }, [e, i, s, c, l]);
  J(() => {
    W();
  }, [W]), J(() => {
    k.current && (k.current.indeterminate = P > 0 && !F);
  }, [F, P]);
  const H = se(async () => {
    if (!(M === "loading" || M === "ready")) {
      C("loading");
      try {
        const V = await Yr(e);
        I(V.activities ?? []), C("ready");
      } catch (V) {
        C("failed"), g(V instanceof Error ? V.message : String(V));
      }
    }
  }, [M, e]), Y = () => {
    g(""), w(""), E({ name: "", description: "", rootKind: "flowchart" }), H();
  }, Z = async () => {
    if (S?.name.trim()) {
      R(!0), g(""), w("");
      try {
        const V = await jp(e, {
          name: S.name.trim(),
          description: S.description.trim() || null,
          rootKind: S.rootKind,
          rootActivityVersionId: H0(S, D)
        });
        E(null), n(V.definition.id);
      } catch (V) {
        g(V instanceof Error ? V.message : String(V));
      } finally {
        R(!1);
      }
    }
  }, ne = (V) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(V)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, le = async () => {
    if (x.length === 1 && c > 1) {
      u(c - 1);
      return;
    }
    await W();
  }, G = () => b(/* @__PURE__ */ new Set()), z = (V, te) => {
    b((ge) => {
      const me = new Set(ge);
      return te ? me.add(V) : me.delete(V), me;
    });
  }, X = (V) => {
    b((te) => {
      const ge = new Set(te);
      for (const me of A)
        V ? ge.add(me) : ge.delete(me);
      return ge;
    });
  }, ae = (V) => {
    a(V), u(1), G();
  }, ce = (V) => {
    o(V), u(1), G();
  }, Q = async (V) => {
    if (await Ts().confirm({ message: `Delete workflow definition "${V.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" })) {
      w(""), g("");
      try {
        await Sp(e, V.id), z(V.id, !1), w(`Deleted ${V.name}`), await le();
      } catch (te) {
        g(te instanceof Error ? te.message : String(te));
      }
    }
  }, oe = async (V) => {
    w(""), g("");
    try {
      await Cp(e, V.id), z(V.id, !1), w(`Restored ${V.name}`), await le();
    } catch (te) {
      g(te instanceof Error ? te.message : String(te));
    }
  }, fe = async (V) => {
    if (await Ts().confirm({ message: `Permanently delete workflow definition "${V.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) {
      w(""), g("");
      try {
        await Ep(e, V.id), z(V.id, !1), w(`Permanently deleted ${V.name}`), await le();
      } catch (te) {
        g(te instanceof Error ? te.message : String(te));
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
        /* @__PURE__ */ r.jsx(Yi, { size: 15 }),
        /* @__PURE__ */ r.jsx("input", { value: i, onChange: (V) => ce(V.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => {
        W();
      }, children: "Refresh" }),
      /* @__PURE__ */ r.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ r.jsxs("button", { type: "button", title: "Create workflow", onClick: Y, children: [
        /* @__PURE__ */ r.jsx(Kt, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ r.jsx(Xn, { message: h, title: "Couldn't load workflow definitions" }) : null,
    f !== "failed" && h ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ r.jsx(ht, { size: 16 }),
      " ",
      h
    ] }) : null,
    m ? /* @__PURE__ */ r.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ r.jsx(nn, { size: 14 }),
      " ",
      m
    ] }) : null,
    N.size > 0 ? /* @__PURE__ */ r.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ r.jsxs("span", { children: [
        N.size,
        " selected"
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: G, children: "Clear selection" })
    ] }) : null,
    f === "loading" ? /* @__PURE__ */ r.jsx(xs, {}) : null,
    f === "ready" && x.length === 0 ? /* @__PURE__ */ r.jsx(
      ws,
      {
        icon: /* @__PURE__ */ r.jsx(Oc, { size: 22 }),
        title: `No ${s} workflow definitions`,
        description: "Create a workflow to start designing automation, or adjust your filters to see more.",
        action: /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-link-button", onClick: Y, children: [
          /* @__PURE__ */ r.jsx(Kt, { size: 15 }),
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
              ref: k,
              type: "checkbox",
              checked: F,
              onChange: (V) => X(V.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ r.jsx("span", { children: "Name" }),
          /* @__PURE__ */ r.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ r.jsx("span", { children: s === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ r.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ r.jsx("span", { children: "Actions" })
        ] }),
        x.map((V) => /* @__PURE__ */ r.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${V.name}`,
            "aria-selected": N.has(V.id),
            tabIndex: 0,
            onClick: () => n(V.id),
            onKeyDown: (te) => {
              te.currentTarget === te.target && (te.key !== "Enter" && te.key !== " " || (te.preventDefault(), n(V.id)));
            },
            children: [
              /* @__PURE__ */ r.jsx("label", { className: "wf-row-select", onClick: (te) => te.stopPropagation(), children: /* @__PURE__ */ r.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: N.has(V.id),
                  onChange: (te) => z(V.id, te.target.checked),
                  "aria-label": `Select workflow definition ${V.name}`
                }
              ) }),
              /* @__PURE__ */ r.jsxs("span", { children: [
                /* @__PURE__ */ r.jsx("strong", { children: V.name }),
                /* @__PURE__ */ r.jsx("small", { children: V.description || V.id })
              ] }),
              /* @__PURE__ */ r.jsx("span", { children: V.latestVersion ?? "No version" }),
              /* @__PURE__ */ r.jsx("span", { children: s === "deleted" ? Le(V.deletedAt) : V.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ r.jsx("span", { children: Le(V.lastModifiedAt) }),
              /* @__PURE__ */ r.jsx("span", { className: "wf-row-actions", onClick: (te) => te.stopPropagation(), children: s === "active" ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
                /* @__PURE__ */ r.jsx("button", { type: "button", onClick: (te) => {
                  te.stopPropagation(), n(V.id);
                }, children: "Open" }),
                /* @__PURE__ */ r.jsx("button", { type: "button", onClick: (te) => {
                  te.stopPropagation(), ne(V.id);
                }, children: "Artifacts" }),
                $ ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => It(t, $, V), children: [
                  /* @__PURE__ */ r.jsx(rt, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ r.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  Q(V);
                }, children: [
                  /* @__PURE__ */ r.jsx(Sn, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
                /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
                  oe(V);
                }, children: [
                  /* @__PURE__ */ r.jsx(Br, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ r.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  fe(V);
                }, children: [
                  /* @__PURE__ */ r.jsx(Sn, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          V.id
        ))
      ] }),
      /* @__PURE__ */ r.jsx(
        MN,
        {
          page: c,
          pageSize: l,
          totalCount: y,
          onPageChange: u,
          onPageSizeChange: (V) => {
            d(V), u(1);
          }
        }
      )
    ] }) : null,
    S ? /* @__PURE__ */ r.jsx(
      zN,
      {
        draft: S,
        creating: _,
        ai: t,
        suggestMetadataAction: T,
        onChange: (V) => E(V),
        onClose: () => E(null),
        onSubmit: Z
      }
    ) : null
  ] });
}
function VN({ activities: e, activityCatalog: t, selectedEvidenceId: n = null, onSelectEvidence: i }) {
  const o = de(
    () => new Map(t.map((a) => [a.activityTypeKey, a])),
    [t]
  ), s = de(() => HN(e), [e]);
  return s.length === 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty wf-timeline-empty", children: "No activity executions recorded yet." }) : /* @__PURE__ */ r.jsx("ol", { className: "wf-timeline", "aria-label": "Execution timeline", children: s.map((a) => {
    const c = o.get(a.activityType), u = Ji(c), l = c ? Ce(c) : tn(a.activityType) ?? a.activityType, d = tn(a.activityType) ?? a.activityType, f = WN(a.startedAt ?? a.scheduledAt), p = hs(a.startedAt, a.completedAt);
    return /* @__PURE__ */ r.jsx("li", { children: /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-timeline-entry",
        "data-selected": a.activityExecutionId === n,
        onClick: () => i?.(a.activityExecutionId),
        children: [
          /* @__PURE__ */ r.jsx("span", { className: "wf-timeline-icon wf-activity-icon", "data-icon": u, "aria-hidden": "true", children: gs(u) }),
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
          /* @__PURE__ */ r.jsx(ON, { status: a.status })
        ]
      }
    ) }, a.activityExecutionId);
  }) });
}
function ON({ status: e }) {
  const t = e.toLowerCase(), n = t === "completed" ? "OK" : e;
  return /* @__PURE__ */ r.jsx("span", { className: "wf-status-badge wf-timeline-pill", "data-status": t, children: n });
}
function HN(e) {
  return e.map((t, n) => ({ activity: t, index: n })).sort((t, n) => Ac(t.activity) - Ac(n.activity) || t.index - n.index).map((t) => t.activity);
}
function Ac(e) {
  const t = e.startedAt ?? e.scheduledAt, n = t ? Date.parse(t) : Number.NaN;
  return Number.isNaN(n) ? Number.MAX_SAFE_INTEGER : n;
}
function WN(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? "" : t.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: !1 });
}
function FN({ context: e }) {
  const [t, n] = B("loading"), [i, o] = B(""), [s, a] = B(""), [c, u] = B(""), [l, d] = B([]), f = se(async () => {
    n("loading"), o("");
    try {
      const h = await Pp(e, {
        status: s || void 0,
        runKind: c || void 0,
        take: 100
      });
      d(h), n("ready");
    } catch (h) {
      o(h instanceof Error ? h.message : String(h)), d([]), n("failed");
    }
  }, [e, c, s]);
  J(() => {
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
    t === "failed" ? /* @__PURE__ */ r.jsx(Xn, { message: i }) : null,
    t === "loading" ? /* @__PURE__ */ r.jsx(xs, {}) : null,
    t === "ready" && l.length === 0 ? /* @__PURE__ */ r.jsx(
      ws,
      {
        icon: /* @__PURE__ */ r.jsx(Ui, { size: 22 }),
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
            /* @__PURE__ */ r.jsx("span", { children: md(h.runKind) }),
            /* @__PURE__ */ r.jsx("span", { children: /* @__PURE__ */ r.jsx(sn, { status: h.status, subStatus: h.subStatus }) }),
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
            /* @__PURE__ */ r.jsx("span", { children: Le(h.startedAt ?? h.createdAt) }),
            /* @__PURE__ */ r.jsx("span", { children: hs(h.startedAt ?? h.createdAt, h.completedAt ?? h.updatedAt) })
          ]
        },
        h.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function BN({ context: e, ai: t, workflowExecutionId: n }) {
  const [i, o] = B("loading"), [s, a] = B(""), [c, u] = B(null), [l, d] = B(null), {
    inspectorWidth: f,
    inspectorCollapsed: p,
    maximizedSidePanel: h,
    inspectorExpanded: g,
    editorBodyStyle: m,
    toggleSidePanelCollapsed: w,
    toggleSidePanelMaximized: x,
    startSidePanelResize: j,
    handleSidePanelResizeKeyDown: y
  } = Cd(), v = Et(t, "weaver.workflows.explain-instance"), N = se(async () => {
    if (!n) {
      a("No workflow execution id was provided."), o("failed");
      return;
    }
    o("loading"), a("");
    try {
      const _ = await $p(e, n), [R, D] = await Promise.all([
        Np(e, _.instance.definitionVersionId).then(
          (I) => ({ definitionVersion: I, error: "" }),
          (I) => ({ definitionVersion: null, error: I instanceof Error ? I.message : String(I) })
        ),
        Yr(e)
      ]);
      u({
        details: _,
        definitionVersion: R.definitionVersion,
        definitionVersionError: R.error,
        activityCatalog: D.activities
      }), d(null), o("ready");
    } catch (_) {
      u(null), a(lb(_, n)), o("failed");
    }
  }, [e, n]);
  J(() => {
    N();
  }, [N]);
  const b = () => {
    window.history.pushState({}, "", "/workflows/instances"), window.dispatchEvent(new PopStateEvent("popstate"));
  }, S = () => {
    const _ = c?.details.instance.definitionId;
    _ && (window.history.pushState({}, "", `/workflows/definitions?definition=${encodeURIComponent(_)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  }, E = [
    "wf-instance-detail-workbench",
    p ? "inspector-collapsed" : "",
    h === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: b, children: [
        /* @__PURE__ */ r.jsx(En, { size: 14 }),
        " Runs"
      ] }),
      c?.details.instance.definitionId ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: S, children: [
        /* @__PURE__ */ r.jsx(Hc, { size: 14 }),
        " Designer"
      ] }) : null,
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
        N();
      }, children: [
        /* @__PURE__ */ r.jsx(Br, { size: 14 }),
        " Refresh"
      ] }),
      c && v ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => It(t, v, c.details), children: [
        /* @__PURE__ */ r.jsx(rt, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    i === "loading" ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Loading workflow run..." }) : null,
    i === "failed" ? /* @__PURE__ */ r.jsx(Xn, { message: s }) : null,
    i === "ready" && c ? /* @__PURE__ */ r.jsxs("div", { className: E, style: m, children: [
      /* @__PURE__ */ r.jsx(
        KN,
        {
          definitionVersion: c.definitionVersion,
          definitionVersionError: c.definitionVersionError,
          activityCatalog: c.activityCatalog,
          details: c.details,
          selectedEvidenceId: l,
          onSelectEvidence: d
        }
      ),
      g && !h ? /* @__PURE__ */ r.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize run details panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Vt,
          "aria-valuemax": Ot,
          "aria-valuenow": f,
          tabIndex: 0,
          onPointerDown: (_) => j("inspector", _),
          onKeyDown: (_) => y("inspector", _)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsx(
        XN,
        {
          ai: t,
          action: v ?? void 0,
          summary: c.details.instance,
          details: c.details,
          state: "ready",
          error: "",
          selectedEvidenceId: l,
          onSelectEvidence: d,
          activityCatalog: c.activityCatalog,
          graphNodeIds: c.definitionVersion ? nb(c.definitionVersion, c.activityCatalog) : void 0,
          collapsed: p,
          expanded: g,
          maximized: h === "inspector",
          onToggleCollapsed: () => w("inspector"),
          onToggleMaximized: () => x("inspector")
        }
      )
    ] }) : null
  ] });
}
function KN({ definitionVersion: e, definitionVersionError: t, activityCatalog: n, details: i, selectedEvidenceId: o, onSelectEvidence: s }) {
  const a = de(() => {
    if (!e) return { nodes: [], edges: [] };
    const c = e.state.rootActivity;
    if (!c) return { nodes: [], edges: [] };
    const u = n.find((h) => h.activityVersionId === c.activityVersionId), l = qr(c, u), d = l === "unsupported" ? null : In(c, [], n), f = l === "unsupported" ? xr(c, n, e.layout) : d ? Qc(d, n, e.layout) : xr(c, n, e.layout), p = f.nodes.map((h) => ({
      ...h,
      draggable: !1,
      connectable: !1,
      deletable: !1
    }));
    return {
      nodes: Wf(p, i.activities, i.incidents, o),
      edges: f.edges.map((h) => ({ ...h, deletable: !1 }))
    };
  }, [n, e, i, o]);
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
      /* @__PURE__ */ r.jsx(sn, { status: i.instance.status, subStatus: i.instance.subStatus })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ r.jsxs("div", { className: "wf-empty", children: [
        "The workflow run loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ r.jsx("small", { children: cb(t) }) : null
      ] }),
      e && a.nodes.length === 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      a.nodes.length > 0 ? /* @__PURE__ */ r.jsxs(
        td,
        {
          nodes: a.nodes,
          edges: a.edges,
          nodeTypes: Nd,
          edgeTypes: jd,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (c, u) => s(u.id),
          onPaneClick: () => s(null),
          children: [
            /* @__PURE__ */ r.jsx(id, {}),
            /* @__PURE__ */ r.jsx(ad, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ r.jsx(rd, {})
          ]
        }
      ) : null
    ] })
  ] });
}
function XN({
  ai: e,
  action: t,
  summary: n,
  details: i,
  state: o,
  error: s,
  selectedEvidenceId: a = null,
  onSelectEvidence: c,
  graphNodeIds: u,
  activityCatalog: l = [],
  collapsed: d = !1,
  expanded: f = !0,
  maximized: p = !1,
  onToggleCollapsed: h,
  onToggleMaximized: g
}) {
  const [m, w] = B("timeline");
  if (!n)
    return /* @__PURE__ */ r.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Select a workflow run to inspect its timeline." }) });
  const x = i?.incidents.length ?? 0, j = qN(i?.activities ?? [], a), y = (N) => {
    c?.(N), w("activity");
  }, v = [
    { id: "timeline", title: "Timeline", order: 0, icon: /* @__PURE__ */ r.jsx(Fr, { size: 14 }), render: () => null },
    { id: "activity", title: "Activity", order: 1, icon: /* @__PURE__ */ r.jsx(Mc, { size: 14 }), render: () => null },
    { id: "issues", title: x > 0 ? `Issues (${x})` : "Issues", order: 2, icon: /* @__PURE__ */ r.jsx(ht, { size: 14 }), render: () => null },
    { id: "details", title: "Details", order: 3, icon: /* @__PURE__ */ r.jsx(Wr, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ r.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Run details panel", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title wf-instance-panel-title", children: [
      /* @__PURE__ */ r.jsx(Ai, { label: "Run details tabs", tabs: v, activeTabId: m, onSelect: (N) => w(N) }),
      /* @__PURE__ */ r.jsxs("span", { className: "wf-panel-actions", children: [
        /* @__PURE__ */ r.jsx(
          "button",
          {
            type: "button",
            className: "wf-panel-action-button",
            "aria-label": d ? "Expand run details panel" : "Collapse run details panel",
            title: d ? "Expand" : "Collapse",
            onClick: h,
            children: d ? /* @__PURE__ */ r.jsx(En, { size: 14 }) : /* @__PURE__ */ r.jsx(yt, { size: 14 })
          }
        ),
        d ? null : /* @__PURE__ */ r.jsx(
          "button",
          {
            type: "button",
            className: "wf-panel-action-button",
            "aria-label": p ? "Restore run details panel" : "Maximize run details panel",
            title: p ? "Restore" : "Maximize",
            onClick: g,
            children: p ? /* @__PURE__ */ r.jsx(yr, { size: 14 }) : /* @__PURE__ */ r.jsx(Cn, { size: 14 })
          }
        )
      ] })
    ] }),
    f ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
      /* @__PURE__ */ r.jsxs("header", { children: [
        /* @__PURE__ */ r.jsxs("div", { children: [
          /* @__PURE__ */ r.jsx("span", { children: "Workflow Instance ID" }),
          /* @__PURE__ */ r.jsx("h3", { children: n.workflowExecutionId })
        ] }),
        t ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => It(e, t, i ?? n), children: [
          /* @__PURE__ */ r.jsx(rt, { size: 13 }),
          " Explain"
        ] }) : null
      ] }),
      o === "loading" ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Loading run details..." }) : null,
      o === "failed" ? /* @__PURE__ */ r.jsx(Xn, { message: s }) : null,
      o === "ready" && i ? /* @__PURE__ */ r.jsx("div", { className: "wf-instance-tab-content", children: m === "timeline" ? /* @__PURE__ */ r.jsx(
        VN,
        {
          activities: i.activities,
          activityCatalog: l,
          selectedEvidenceId: a,
          onSelectEvidence: y
        }
      ) : m === "activity" ? /* @__PURE__ */ r.jsx(YN, { activity: j, activityCatalog: l }) : m === "issues" ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
        /* @__PURE__ */ r.jsx(UN, { incidents: i.incidents, selectedEvidenceId: a, onSelectEvidence: c }),
        /* @__PURE__ */ r.jsx(ZN, { details: i, graphNodeIds: u })
      ] }) : /* @__PURE__ */ r.jsxs("dl", { className: "wf-instance-meta", children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ r.jsx("dd", { children: /* @__PURE__ */ r.jsx(sn, { status: n.status, subStatus: n.subStatus }) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Run Kind" }),
        /* @__PURE__ */ r.jsx("dd", { children: md(n.runKind) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Artifact" }),
        /* @__PURE__ */ r.jsxs("dd", { children: [
          n.artifactId,
          " ",
          /* @__PURE__ */ r.jsx("small", { children: n.artifactVersion })
        ] }),
        /* @__PURE__ */ r.jsx("dt", { children: "Definition" }),
        /* @__PURE__ */ r.jsxs("dd", { children: [
          n.definitionId,
          " ",
          /* @__PURE__ */ r.jsx("small", { children: n.definitionVersionId })
        ] }),
        /* @__PURE__ */ r.jsx("dt", { children: "Created" }),
        /* @__PURE__ */ r.jsx("dd", { children: Le(n.createdAt) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Started" }),
        /* @__PURE__ */ r.jsx("dd", { children: Le(n.startedAt) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Completed" }),
        /* @__PURE__ */ r.jsx("dd", { children: Le(n.completedAt) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Correlation" }),
        /* @__PURE__ */ r.jsx("dd", { children: n.correlationId || "None" })
      ] }) }) : null
    ] }) : null
  ] });
}
function qN(e, t) {
  if (!t) return null;
  const n = e.find((o) => o.activityExecutionId === t);
  if (n) return n;
  const i = e.filter((o) => o.executableNodeId === t || o.authoredActivityId === t);
  return i.length > 0 ? al(i) : null;
}
function YN({ activity: e, activityCatalog: t }) {
  if (!e)
    return /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
      /* @__PURE__ */ r.jsx("h4", { children: "Activity" }),
      /* @__PURE__ */ r.jsx("p", { children: "No activity selected." })
    ] });
  const i = t.find((a) => a.activityTypeKey === e.activityType)?.displayName || tn(e.activityType) || e.activityType, o = e.bookmarkIds?.length ?? 0, s = e.incidentIds?.length ?? 0;
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ r.jsx("h4", { children: "Activity" }),
    /* @__PURE__ */ r.jsxs("dl", { className: "wf-instance-meta", children: [
      /* @__PURE__ */ r.jsx("dt", { children: "Name" }),
      /* @__PURE__ */ r.jsx("dd", { children: i }),
      /* @__PURE__ */ r.jsx("dt", { children: "Status" }),
      /* @__PURE__ */ r.jsx("dd", { children: /* @__PURE__ */ r.jsx(sn, { status: e.status, subStatus: e.subStatus }) }),
      /* @__PURE__ */ r.jsx("dt", { children: "Activity Execution ID" }),
      /* @__PURE__ */ r.jsx("dd", { children: e.activityExecutionId }),
      /* @__PURE__ */ r.jsx("dt", { children: "Authored Activity ID" }),
      /* @__PURE__ */ r.jsx("dd", { children: e.authoredActivityId }),
      /* @__PURE__ */ r.jsx("dt", { children: "Type" }),
      /* @__PURE__ */ r.jsxs("dd", { children: [
        tn(e.activityType) ?? e.activityType,
        " ",
        /* @__PURE__ */ r.jsx("small", { children: e.activityTypeVersion })
      ] }),
      /* @__PURE__ */ r.jsx("dt", { children: "Started" }),
      /* @__PURE__ */ r.jsx("dd", { children: Le(e.startedAt) }),
      /* @__PURE__ */ r.jsx("dt", { children: "Completed" }),
      /* @__PURE__ */ r.jsx("dd", { children: Le(e.completedAt) }),
      /* @__PURE__ */ r.jsx("dt", { children: "Duration" }),
      /* @__PURE__ */ r.jsx("dd", { children: hs(e.startedAt, e.completedAt) || "Unknown" }),
      /* @__PURE__ */ r.jsx("dt", { children: "Bookmarks" }),
      /* @__PURE__ */ r.jsx("dd", { children: o }),
      /* @__PURE__ */ r.jsx("dt", { children: "Incidents" }),
      /* @__PURE__ */ r.jsx("dd", { children: s })
    ] })
  ] });
}
function UN({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ r.jsx("h4", { children: "Incidents" }),
    e.length === 0 ? /* @__PURE__ */ r.jsx("p", { children: "No incidents recorded." }) : null,
    e.map((i) => /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-instance-incident",
        "data-severity": i.severity.toLowerCase(),
        "data-selected": i.incidentId === t,
        onClick: () => n?.(i.incidentId),
        children: [
          /* @__PURE__ */ r.jsx("strong", { children: i.failureType }),
          /* @__PURE__ */ r.jsxs("span", { children: [
            i.status,
            " · ",
            i.severity
          ] }),
          /* @__PURE__ */ r.jsx("p", { children: i.message })
        ]
      },
      i.incidentId
    ))
  ] });
}
function ZN({ details: e, graphNodeIds: t }) {
  if (!t) return null;
  const n = new Map(e.activities.map((s) => [s.activityExecutionId, s])), i = e.activities.filter((s) => !t.has(gc(s))), o = e.incidents.filter((s) => {
    const a = s.activityExecutionId ? n.get(s.activityExecutionId) : null, c = s.executableNodeId ?? (a ? gc(a) : "");
    return !c || !t.has(c);
  });
  return i.length === 0 && o.length === 0 ? null : /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ r.jsx("h4", { children: "Unmatched runtime evidence" }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-unmatched-list", children: [
      i.map((s) => /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ r.jsx("strong", { children: tn(s.activityType) ?? s.activityType }),
        /* @__PURE__ */ r.jsx("small", { children: s.activityExecutionId })
      ] }, `activity-${s.activityExecutionId}`)),
      o.map((s) => /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ r.jsx("strong", { children: s.failureType }),
        /* @__PURE__ */ r.jsx("small", { children: s.incidentId })
      ] }, `incident-${s.incidentId}`))
    ] })
  ] });
}
function GN({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: i,
  workflowDesignerPanels: o,
  autosaveEnabledByDefault: s
}) {
  const [a, c] = B(_c);
  J(() => {
    const l = () => c(_c());
    return window.addEventListener("popstate", l), () => window.removeEventListener("popstate", l);
  }, []);
  const u = (l) => {
    const d = l ? `/workflows/definitions?definition=${encodeURIComponent(l)}` : "/workflows/definitions";
    window.history.pushState({}, "", d), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return a ? /* @__PURE__ */ r.jsx($N, { context: e, definitionId: a, ai: t, propertyEditors: n, expressionEditors: i, workflowDesignerPanels: o, autosaveEnabledByDefault: s, onBack: () => u(null) }) : /* @__PURE__ */ r.jsx(po, { title: "Definitions", children: /* @__PURE__ */ r.jsx(LN, { context: e, ai: t, onOpen: u }) });
}
function JN({ context: e, ai: t }) {
  const [n, i] = B(Dc);
  J(() => {
    const s = () => i(Dc());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const o = se((s) => {
    const a = s?.trim() ?? "", c = new URL(window.location.href);
    a ? c.searchParams.set("definition", a) : c.searchParams.delete("definition"), i(a || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ r.jsx(po, { title: "Executables", children: /* @__PURE__ */ r.jsx(jb, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: o }) });
}
function QN({ context: e }) {
  return /* @__PURE__ */ r.jsx(po, { title: "Runs", children: /* @__PURE__ */ r.jsx(FN, { context: e }) });
}
function ej({ context: e, ai: t }) {
  const n = tj();
  return /* @__PURE__ */ r.jsx(po, { title: "Run", children: /* @__PURE__ */ r.jsx(BN, { context: e, ai: t, workflowExecutionId: n }) });
}
function po({ title: e, children: t }) {
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ r.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ r.jsxs("div", { children: [
      /* @__PURE__ */ r.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ r.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function _c() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Dc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function tj() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function sj(e) {
  af(e.dialogs), e.featureAreas.add({
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
        component: () => /* @__PURE__ */ r.jsx(GN, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list(), autosaveEnabledByDefault: e.runtime.workflows?.autosaveEnabledByDefault ?? !0 })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ r.jsx(JN, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => /* @__PURE__ */ r.jsx(QN, { context: e.backend })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => /* @__PURE__ */ r.jsx(ej, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => /* @__PURE__ */ r.jsx(th, { context: e.backend })
      }
    ]
  });
}
export {
  ob as isConnectEndOverExistingWorkflowNode,
  sj as register,
  rb as resolveConnectEndSource
};
