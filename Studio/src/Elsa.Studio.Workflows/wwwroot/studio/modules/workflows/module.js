import Fe, { useMemo as ue, useState as K, useEffect as ee, memo as Ee, forwardRef as sl, useRef as oe, useCallback as se, useContext as Un, createContext as ns, useLayoutEffect as Df, lazy as Tf, Suspense as $f, useReducer as Pf, useId as al } from "react";
import { Boxes as Zn, Zap as Mf, Play as Zt, Terminal as Rf, ListTree as is, GitBranch as cl, ListChecks as Lf, Save as rs, EyeOff as lo, Shield as Zs, AlertTriangle as Tn, SlidersHorizontal as os, Activity as ll, Search as ar, X as ss, DatabaseZap as Gs, ShieldCheck as zf, Check as an, Plus as Gt, Trash2 as $n, AlertCircle as jt, Wrench as Vf, Copy as Of, Sparkles as ft, RotateCcw as as, ChevronDown as ul, ChevronRight as xt, GripVertical as dl, Maximize2 as Pn, ChevronUp as Hf, Repeat2 as Wf, Package as fl, Undo2 as Ff, Redo2 as Bf, Network as Kf, Download as Xf, ChevronLeft as Mn, Minimize2 as _o, Workflow as pl, Code2 as qf } from "lucide-react";
import { useQuery as cr, useQueryClient as hl, useMutation as gl } from "@tanstack/react-query";
import { useTablistKeyboard as Yf } from "@elsa-workflows/studio-ui";
function Uf(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var uo = { exports: {} }, yn = {};
var Js;
function Zf() {
  if (Js) return yn;
  Js = 1;
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
  return yn.Fragment = t, yn.jsx = n, yn.jsxs = n, yn;
}
var Qs;
function Gf() {
  return Qs || (Qs = 1, uo.exports = Zf()), uo.exports;
}
var o = Gf();
let ml;
function Jf(e) {
  ml = e;
}
function ea() {
  return ml;
}
const Qf = "String", ep = "singleline";
function tp(e) {
  return e === "Single" || e === "Array" || e === "List" || e === "HashSet";
}
function cs(e, t = "Single") {
  return { alias: (e ?? "").trim() || Qf, collectionKind: t };
}
function ls(e) {
  const t = e.type ?? e.Type;
  if (Rn(t))
    return { alias: typeof t.alias == "string" ? t.alias : typeof t.typeName == "string" ? t.typeName : "", collectionKind: tp(t.collectionKind) ? t.collectionKind : "Single" };
  const n = e.typeInformation ?? e.TypeInformation;
  return Rn(n) ? { alias: typeof n.typeName == "string" ? n.typeName : "", collectionKind: ta(e) ? "Array" : "Single" } : { alias: typeof t == "string" ? t : "", collectionKind: ta(e) ? "Array" : "Single" };
}
function ta(e) {
  const t = e.isArray ?? e.IsArray;
  return t === !0 || t === "true";
}
function na(e) {
  return (e.alias ?? e.typeName ?? "").trim();
}
function lr() {
  return typeof crypto < "u" && typeof crypto.randomUUID == "function" ? crypto.randomUUID() : `id-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
}
function np(e, t) {
  const n = new Set(t);
  let i = 1, r = `${e}${i}`;
  for (; n.has(r); )
    i += 1, r = `${e}${i}`;
  return r;
}
function ip(e) {
  return {
    referenceKey: lr(),
    name: e.name,
    type: cs(e.alias),
    storageDriverType: e.storageDriver?.trim() || null,
    default: null
  };
}
function rp(e, t) {
  return { ...e, ...t };
}
function op(e) {
  return e === "" ? null : { value: e, expressionType: "Literal" };
}
function sp(e) {
  return e == null || e.value == null ? "" : typeof e.value == "object" ? JSON.stringify(e.value) : String(e.value);
}
function ap(e) {
  return {
    referenceKey: lr(),
    name: e.name,
    type: cs(e.alias),
    displayName: e.name,
    description: "",
    category: "",
    uiHint: ep,
    storageDriverType: e.storageDriver ?? null,
    isRequired: !1
  };
}
function cp(e, t) {
  return { ...e, ...t };
}
function lp(e) {
  return {
    referenceKey: lr(),
    name: e.name,
    type: cs(e.alias),
    displayName: e.name,
    description: "",
    category: ""
  };
}
function up(e, t) {
  return { ...e, ...t };
}
function dp(e) {
  const t = e.split(",")[0].trim();
  return (t.split(".").pop() ?? t).split("`")[0];
}
function yl(e, t) {
  return e && !e.includes(",") && !e.includes(".") ? e : dp(e || t);
}
function fp(e, t) {
  return yl(e, t).replace(/StorageDriver$/, "");
}
function Rn(e) {
  return !!e && typeof e == "object" && !Array.isArray(e);
}
function ut(e, t) {
  for (const n of t) {
    const i = e[n];
    if (i != null && typeof i != "object") return String(i);
  }
  return "";
}
const pp = ["name", "Name"], us = ["name", "Name"], hp = ["storageDriverType", "StorageDriverType"], xl = ["referenceKey", "ReferenceKey"], gp = /* @__PURE__ */ new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);
function Gn(e) {
  return wl(e, wp);
}
function ur(e) {
  return wl(e, vp);
}
function wl(e, t) {
  if (!e) return e;
  const n = { ...e };
  return e.rootActivity && (n.rootActivity = Do(e.rootActivity, t)), Array.isArray(e.variables) && (n.variables = Pi(e.variables, To)), Array.isArray(e.inputs) && (n.inputs = Pi(e.inputs, To)), Array.isArray(e.outputs) && (n.outputs = Pi(e.outputs, (i) => vl(i, !1))), n;
}
function Do(e, t) {
  const n = t(e), i = n.structure;
  if (!i || !rt(i.payload)) return n;
  let r = !1;
  const s = { ...i.payload };
  for (const [a, c] of Object.entries(i.payload))
    ra(c) ? (s[a] = Do(c, t), r = !0) : Array.isArray(c) && c.length > 0 && c.every(ra) && (s[a] = c.map((u) => Do(u, t)), r = !0);
  return Array.isArray(i.payload.variables) && i.payload.variables.length > 0 && (s.variables = Pi(i.payload.variables, To), r = !0), r ? { ...n, structure: { ...i, payload: s } } : n;
}
function Pi(e, t) {
  return e.map((n) => rt(n) && !Array.isArray(n) ? t(n) : n);
}
function To(e) {
  return vl(e, !0);
}
const mp = [
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
function vl(e, t) {
  const n = yp(e, mp);
  return ut(e, xl).trim() || (n.referenceKey = lr()), n.type = ls(e), t && (n.storageDriverType = xp(e.storageDriverType ?? e.StorageDriverType)), n;
}
function yp(e, t) {
  const n = new Set(t), i = {};
  for (const [r, s] of Object.entries(e))
    n.has(r) || (i[r] = s);
  return i;
}
function xp(e) {
  if (typeof e == "string") return e.trim() ? e : null;
  if (rt(e)) {
    const t = typeof e.typeName == "string" ? e.typeName : "";
    if (!t) return null;
    const n = typeof e.namespace == "string" ? e.namespace : "";
    return n ? `${n}.${t}` : t;
  }
  return null;
}
function wp(e) {
  const t = [], n = {};
  for (const [r, s] of Object.entries(e))
    gp.has(r) || (Sp(s) ? t.push({
      referenceKey: bp(r),
      value: Np(s.expression)
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
function vp(e) {
  const t = Array.isArray(e.inputs) ? e.inputs : [], n = {};
  for (const i of t) {
    if (!rt(i) || typeof i.referenceKey != "string") continue;
    const r = rt(i.value) ? i.value : {};
    n[jp(i.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof r.expressionType == "string" ? r.expressionType : "Literal",
        value: r.value ?? ""
      }
    };
  }
  return { ...e, ...n, inputs: [] };
}
function bp(e) {
  return e && e.charAt(0).toUpperCase() + e.slice(1);
}
function jp(e) {
  return e && e.charAt(0).toLowerCase() + e.slice(1);
}
function Np(e) {
  const t = e.type || "Literal";
  return t === "Variable" && rt(e.value) ? { value: e.value, expressionType: t } : t === "Literal" && rt(e.value) ? { value: ia(e.value), expressionType: "Object" } : { value: ia(e.value), expressionType: t };
}
function ia(e) {
  return e == null ? null : typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function Sp(e) {
  if (!rt(e) || Array.isArray(e)) return !1;
  const t = e.expression;
  return rt(t) && typeof t.type == "string";
}
function ra(e) {
  return rt(e) && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function rt(e) {
  return typeof e == "object" && e !== null;
}
const Jn = "elsa.sequence.structure", cn = "elsa.flowchart.structure";
function bl(e, t, n) {
  if (!e) return null;
  let i = e;
  for (const r of t) {
    const s = kp(i, r.ownerNodeId, n);
    if (!s) return null;
    i = s;
  }
  return i;
}
function Cp(e, t, n = (r) => r.nodeId, i) {
  if (!e) return null;
  if (e.nodeId === t) return [];
  const r = (s, a) => {
    const c = $e(s, i);
    for (const u of c) {
      if (!u.activities.some((d) => d.nodeId === t)) continue;
      const l = a.at(-1);
      return l ? [...a.slice(0, -1), { ...l, slotId: u.id }] : u.id === c[0]?.id ? a : null;
    }
    for (const u of c)
      for (const l of u.activities) {
        const f = $e(l, i)[0]?.id ?? u.id, p = r(l, [...a, { ownerNodeId: l.nodeId, slotId: f, label: n(l) }]);
        if (p) return p;
      }
    return null;
  };
  return r(e, []);
}
function Mi(e, t, n, i, r, s) {
  const a = i.cardinality === "single" && i.activities.length === 1 ? i.activities[0] : null, c = a ? $e(a, s)[0] : void 0, u = a && c && c.mode !== "generic" ? a : null, l = t?.nodeId === n;
  if (l && e.length === 0)
    return t && $e(t, s)[0]?.id !== i.id ? null : u ? { frames: [{ ownerNodeId: u.nodeId, slotId: c.id, label: r }], selectedNodeId: null } : { frames: [], selectedNodeId: a?.nodeId ?? null };
  const d = l ? e.slice(0, -1) : e;
  return u ? {
    frames: [
      ...d,
      { ownerNodeId: n, slotId: i.id, label: "" },
      { ownerNodeId: u.nodeId, slotId: c.id, label: r }
    ],
    selectedNodeId: null
  } : {
    frames: [...d, { ownerNodeId: n, slotId: i.id, label: r }],
    selectedNodeId: a?.nodeId ?? null
  };
}
function jl(e, t, n) {
  const i = $e(e, n), r = t.at(-1);
  return r ? i.find((s) => s.id === r.slotId) ?? null : i[0] ?? null;
}
function Ln(e, t, n) {
  const i = bl(e, t, n);
  if (!i) return null;
  const r = jl(i, t, n);
  return r ? { owner: i, slot: r } : null;
}
function Nl(e, t, n) {
  for (const i of $e(e, n)) {
    const r = i.activities.find((s) => s.nodeId === t);
    if (r) return { slot: i, child: r };
  }
  return null;
}
function kp(e, t, n) {
  return Nl(e, t, n)?.child ?? null;
}
function $e(e, t) {
  const n = e.structure;
  if (!n || !n.payload || typeof n.payload != "object") return [];
  const i = n.payload, r = ds(Sl(e, t));
  if (r?.kind === n.kind) return Ap(n, r);
  const s = Zp(n), a = bn(i.activities);
  return a ? [{
    id: `${n.kind}:activities`,
    label: Gp(n),
    property: "activities",
    cardinality: "many",
    mode: s,
    activities: a
  }] : Object.entries(i).filter(([, c]) => bn(c) || Yi(c)).map(([c, u]) => ({
    id: `${n.kind}:${c}`,
    label: Qp(c),
    property: c,
    cardinality: bn(u) ? "many" : "single",
    mode: "generic",
    activities: bn(u) ?? (Yi(u) ? [u] : [])
  }));
}
function ds(e) {
  for (const t of e?.designFacets ?? []) {
    if (!ot(t)) continue;
    const n = typeof t.kind == "string" ? t.kind : "", i = typeof t.schemaVersion == "string" ? t.schemaVersion : "";
    if (!n || !i || !ot(t.payload)) continue;
    const r = Ep(t.payload);
    if (r) return { kind: n, schemaVersion: i, payload: r };
  }
  return null;
}
function Ep(e) {
  const t = e.mode;
  if (t !== "sequence" && t !== "flowchart" && t !== "generic" || typeof e.supportsScopedVariables != "boolean" || !Array.isArray(e.slots) || !ot(e.initialPayload)) return null;
  const n = e.slots.map(Ip).filter((i) => i !== null);
  return {
    mode: t,
    supportsScopedVariables: e.supportsScopedVariables,
    slots: n,
    initialPayload: e.initialPayload
  };
}
function Ip(e) {
  if (!ot(e)) return null;
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
function Ap(e, t) {
  return t.payload.slots.flatMap((n) => {
    if (n.collectionProperty && n.childProperty) {
      const i = n.childProperty, r = e.payload[n.collectionProperty];
      return Array.isArray(r) ? r.flatMap((s, a) => {
        if (!ot(s)) return [];
        const c = n.labelProperty ? Ft(s[n.labelProperty]) : void 0;
        return [oa(e.kind, n, s[i], t.payload.mode, a, c)];
      }) : [];
    }
    return [oa(e.kind, n, e.payload[n.property], t.payload.mode)];
  });
}
function oa(e, t, n, i, r, s) {
  const a = r === void 0 ? "" : `:${t.collectionProperty}:${t.childProperty}:${r}`;
  return {
    id: `${e}:${t.property}${a}`,
    label: r === void 0 ? t.displayName : Dp(t, r, s),
    property: t.property,
    cardinality: t.cardinality,
    mode: i,
    activities: _p(n, t.cardinality),
    collectionProperty: t.collectionProperty,
    childProperty: t.childProperty,
    labelProperty: t.labelProperty,
    slotNameTemplate: t.slotNameTemplate,
    collectionIndex: r,
    collectionItemLabel: s
  };
}
function _p(e, t) {
  return t === "many" ? bn(e) ?? [] : Yi(e) ? [e] : [];
}
function Dp(e, t, n) {
  return e.slotNameTemplate ? e.slotNameTemplate.replaceAll("{name}", e.name).replaceAll("{displayName}", e.displayName).replaceAll("{label}", n ?? String(t + 1)).replaceAll("{index}", String(t + 1)) : n ? `${e.displayName}: ${n}` : `${e.displayName} ${t + 1}`;
}
function Sl(e, t) {
  if (t)
    return t instanceof Map ? t.get(e.activityVersionId) : Array.isArray(t) ? t.find((n) => n.activityVersionId === e.activityVersionId) : t.activityVersionId === e.activityVersionId ? t : void 0;
}
function Tp(e, t) {
  if (t.labelProperty && t.collectionItemLabel) {
    const n = e.map((i, r) => ot(i) && Ft(i[t.labelProperty]) === t.collectionItemLabel ? r : -1).filter((i) => i >= 0);
    if (n.length === 1) return n[0];
  }
  return typeof t.collectionIndex == "number" && t.collectionIndex >= 0 && t.collectionIndex < e.length ? t.collectionIndex : -1;
}
function Cl(e, t, n) {
  const i = new Map(t.map((a) => [a.activityVersionId, a])), r = new Map(n.map((a) => [a.nodeId, a])), s = e.slot.activities.map((a, c) => {
    const u = i.get(a.activityVersionId), l = r.get(a.nodeId) ?? Jp(e.slot.mode, c);
    return Al(a, u, { x: l.x, y: l.y });
  });
  return {
    nodes: s,
    edges: e.slot.mode === "flowchart" ? Dl(e.owner) : Fp(e.slot, s)
  };
}
function kl(e, t, n) {
  const i = t.find((s) => s.activityVersionId === e.activityVersionId), r = n.find((s) => s.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [Al(e, i, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function $p(e, t, n, i = null) {
  const r = new Map(t.map((c) => [c.activityExecutionId, c])), s = ca(t, (c) => c.authoredActivityId || c.executableNodeId), a = ca(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? r.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const u = s.get(c.id) ?? [], l = a.get(c.id) ?? [];
    if (u.length === 0 && l.length === 0) return c;
    const d = $l(u), f = i === c.id || u.some((h) => h.activityExecutionId === i) || l.some((h) => h.incidentId === i), p = {
      status: d?.status,
      subStatus: d?.subStatus,
      activityExecutionId: d?.activityExecutionId,
      faultCount: u.reduce((h, m) => h + m.faultCount + m.aggregateFaultCount, 0),
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
function El(e, t) {
  if (e?.structure?.kind === cn || Vp(t)) return "flowchart";
  if (e?.structure?.kind === Jn || Op(t)) return "sequence";
  if (e) {
    const n = $e(e, t)[0];
    if (n) return n.mode === "flowchart" ? "flowchart" : "sequence";
  }
  return "unsupported";
}
function sa(e, t, n, i) {
  return fs(e, t, i, (r) => {
    const s = jl(r, t, i);
    return s ? ln(r, s, n) : r;
  });
}
function Pp(e, t, n, i) {
  return t.length === 0 ? n : fs(e, t, i, () => n);
}
function fs(e, t, n, i) {
  if (t.length === 0) return i(e);
  const [r, ...s] = t, a = Nl(e, r.ownerNodeId, n);
  if (!a) return e;
  const c = fs(a.child, s, n, i);
  if (c === a.child) return e;
  const u = a.slot.activities.map((l) => l.nodeId === r.ownerNodeId ? c : l);
  return ln(e, a.slot, u);
}
function $o(e, t, n, i) {
  if (e.nodeId === t) return n(e);
  const r = $e(e, i);
  if (r.length === 0) return e;
  let s = !1, a = e;
  for (const c of r) {
    const u = c.activities.map((l) => {
      const d = $o(l, t, n, i);
      return d !== l && (s = !0), d;
    });
    s && (a = ln(a, c, u));
  }
  return s ? a : e;
}
function ln(e, t, n) {
  if (!e.structure) return e;
  const i = t.cardinality === "single" ? n[0] ?? null : n;
  if (t.collectionProperty && t.childProperty) {
    const r = e.structure.payload[t.collectionProperty];
    if (!Array.isArray(r)) return e;
    const s = Tp(r, t);
    if (s < 0) return e;
    const a = r[s];
    if (!ot(a)) return e;
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
function Mp(e, t, n, i = []) {
  const r = new Map(e.slot.activities.map((a) => [a.nodeId, a]));
  for (const a of i)
    r.set(a.nodeId, a);
  const s = t.map((a) => r.get(a.id)).filter((a) => !!a);
  return e.slot.mode === "sequence" && s.sort((a, c) => {
    const u = t.find((d) => d.id === a.nodeId), l = t.find((d) => d.id === c.nodeId);
    return (u?.position.x ?? 0) - (l?.position.x ?? 0);
  }), ln(e.owner, e.slot, s);
}
function Rp(e, t) {
  return {
    ...e,
    structure: Wp(e.structure, t)
  };
}
function Lp(e, t) {
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
function Xi(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: _l(e)
  };
}
function Il(e, t) {
  if (!e) return null;
  const n = Sl(e, t), i = e.structure ?? (n ? _l(n) : null);
  let r = i === e.structure ? e : { ...e, structure: i };
  const s = $e(r, t);
  for (const a of s) {
    const c = a.activities.map((u) => Il(u, t) ?? u);
    c.some((u, l) => u !== a.activities[l]) && (r = ln(r, a, c));
  }
  return r;
}
function ke(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? Hp(t) : n;
}
function Al(e, t, n, i = {}) {
  return {
    id: e.nodeId,
    type: "workflowActivity",
    position: n,
    connectable: i.connectable,
    deletable: i.deletable,
    draggable: i.draggable,
    data: {
      label: t ? ke(t) : e.activityVersionId,
      activityVersionId: e.activityVersionId,
      activityTypeKey: t?.activityTypeKey,
      category: t?.category,
      executionType: t?.executionType,
      icon: un(t),
      childSlots: $e(e, t),
      acceptsInbound: Bp(e, t),
      sourcePorts: i.suppressFlowPorts ? [] : Tl(e, t),
      suppressFlowPorts: i.suppressFlowPorts
    }
  };
}
function un(e) {
  if (!e) return "activity";
  const t = zp(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), i = ke(e).toLowerCase(), r = e.category?.toLowerCase() ?? "", s = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || i === "flowchart" ? "flowchart" : n.endsWith(".sequence") || i === "sequence" ? "sequence" : n.includes("writeline") || i.includes("write line") ? "terminal" : r.includes("runtime") ? "runtime" : s === "trigger" ? "trigger" : "activity";
}
function zp(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function Vp(e) {
  return !!e && (ke(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function Op(e) {
  return !!e && (ke(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function Hp(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function _l(e) {
  const t = ds(e);
  return t ? {
    kind: t.kind,
    schemaVersion: t.schemaVersion,
    payload: Yp(t.payload.initialPayload)
  } : e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: Jn,
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
function Wp(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], i = /* @__PURE__ */ new Map();
  for (const r of n) {
    if (!ot(r)) continue;
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
function Fp(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, i) => ({
    id: `sequence-${n.id}-${t[i + 1].id}`,
    source: n.id,
    target: t[i + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function Dl(e) {
  if (e.structure?.kind !== cn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, i) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, s = n.target;
    if (!r?.nodeId || !s?.nodeId) return null;
    const a = Array.isArray(n.vertices) ? n.vertices.filter(Up) : [];
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
function Tl(e, t) {
  const n = aa(e.cases);
  if (Xp(e, t) && n.length > 0)
    return [...n.map((s) => ({ name: s, displayName: s })), { name: "Default", displayName: "Default" }];
  const i = [
    ...Ri(t?.designFacets),
    ...Ri(t?.ports),
    ...Ri(t?.outputs)
  ];
  if (i.length > 0) return qp(i);
  const r = aa(e.outcomes);
  return r.length > 0 ? r.map((s) => ({ name: s, displayName: s })) : [{ name: "Done", displayName: "Done" }];
}
function Bp(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function qi(e, t, n, i) {
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
function Kp(e, t, n) {
  const i = qi(t.source, n, t.sourceHandle ?? "Done", void 0), r = qi(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((s) => s.id !== t.id).concat(i, r);
}
function bn(e) {
  return Array.isArray(e) ? e.filter(Yi) : null;
}
function Xp(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, i = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || i === "FlowSwitch";
}
function Ri(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!ot(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...Ri(n.ports));
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
function qp(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function aa(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function Ft(e) {
  return typeof e == "string" && e.length > 0 ? e : void 0;
}
function Yp(e) {
  return JSON.parse(JSON.stringify(e));
}
function ca(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e) {
    const r = t(i);
    r && n.set(r, [...n.get(r) ?? [], i]);
  }
  return n;
}
function $l(e) {
  return [...e].sort((t, n) => la(n).localeCompare(la(t)))[0];
}
function la(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function Up(e) {
  return ot(e) && typeof e.x == "number" && typeof e.y == "number";
}
function ot(e) {
  return typeof e == "object" && e !== null;
}
function Yi(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Zp(e) {
  return e.kind === Jn ? "sequence" : e.kind === cn ? "flowchart" : "generic";
}
function Gp(e) {
  return e.kind === Jn || e.kind === cn, "Activities";
}
function Jp(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function Qp(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Ui = "workflow", eh = /* @__PURE__ */ new Set([Jn, cn]);
function th(e, t) {
  const n = e?.structure?.kind;
  if (!n) return !1;
  if (eh.has(n)) return !0;
  const i = t?.activityVersionId === e?.activityVersionId ? ds(t) : null;
  return i?.kind === n && i.payload.supportsScopedVariables;
}
function Pl(e) {
  const t = e?.structure?.payload?.variables;
  return Array.isArray(t) ? t.filter(Rn) : [];
}
function nh(e, t) {
  return e.structure ? {
    ...e,
    structure: {
      ...e.structure,
      payload: { ...e.structure.payload, variables: t }
    }
  } : e;
}
function ih(e, t) {
  return {
    referenceKey: e,
    declaringScopeId: t && t !== Ui ? t : Ui
  };
}
function Ml(e) {
  if (e && typeof e == "object") {
    const t = e;
    return typeof t.referenceKey == "string" && t.referenceKey ? { referenceKey: t.referenceKey, declaringScopeId: typeof t.declaringScopeId == "string" ? t.declaringScopeId : null } : null;
  }
  if (typeof e == "string") {
    const t = e.trim();
    if (t.startsWith("{"))
      try {
        return Ml(JSON.parse(t));
      } catch {
      }
    return t ? { referenceKey: t, declaringScopeId: null } : null;
  }
  return null;
}
function rh(e, t) {
  if (!e) return "";
  const n = [`workflow:${ua(e.variables)}`], i = (r) => {
    const s = $e(r, t), a = s.flatMap((c) => c.activities.map((u) => u.nodeId));
    n.push(`${r.nodeId}:${ua(Pl(r))}>${a.join(",")}`), s.forEach((c) => c.activities.forEach(i));
  };
  return e.rootActivity && i(e.rootActivity), n.join(";");
}
function ua(e) {
  return (e ?? []).map((t) => `${t.referenceKey}=${t.name}`).join(",");
}
function oh(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e ?? [])
    i.scopeId === t && n.set(i.referenceKey, `Shadows "${i.name}" declared in an outer scope.`);
  return n;
}
const be = "/_elsa/workflow-management", sh = "/publishing", wt = {
  activities: ["workflows", "activities"],
  activityAvailabilitySettings: ["workflows", "activity-availability", "settings"],
  activityAvailabilityDiagnostics: ["workflows", "activity-availability", "diagnostics"],
  runtimeDiagnosticsSettings: ["workflows", "runtime-diagnostics", "settings"]
};
function ah(e) {
  return cr({
    queryKey: wt.activities,
    queryFn: () => dr(e),
    staleTime: 6e4
  });
}
function ch(e) {
  return cr({
    queryKey: wt.activityAvailabilitySettings,
    queryFn: () => Dh(e)
  });
}
function lh(e) {
  return cr({
    queryKey: wt.activityAvailabilityDiagnostics,
    queryFn: () => zl(e)
  });
}
function uh(e) {
  const t = hl();
  return gl({
    mutationFn: (n) => Th(e, n),
    onSuccess: (n) => {
      t.setQueryData(wt.activityAvailabilitySettings, n), t.invalidateQueries({ queryKey: wt.activityAvailabilityDiagnostics }), t.invalidateQueries({ queryKey: wt.activities });
    }
  });
}
function dh(e) {
  return cr({
    queryKey: wt.runtimeDiagnosticsSettings,
    queryFn: () => $h(e)
  });
}
function fh(e) {
  const t = hl();
  return gl({
    mutationFn: (n) => Ph(e, n),
    onSuccess: () => {
      t.invalidateQueries({ queryKey: wt.runtimeDiagnosticsSettings });
    }
  });
}
async function ph(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), i = t.search.trim();
  return i && n.set("search", i), e.http.getJson(`${be}/definitions?${n.toString()}`);
}
async function hh(e, t) {
  const n = await e.http.getJson(`${be}/definitions/${encodeURIComponent(t)}`);
  return n.draft ? { ...n, draft: { ...n.draft, state: ur(n.draft.state) } } : n;
}
async function gh(e, t, n) {
  const i = await e.http.postJson(
    `${be}/design/scoped-variables/analyze`,
    { state: Gn(t), nodeId: n }
  );
  return {
    visibleVariables: Array.isArray(i?.visibleVariables) ? i.visibleVariables : [],
    shadowingWarnings: Array.isArray(i?.shadowingWarnings) ? i.shadowingWarnings : []
  };
}
const fo = (e) => ({ visibleVariables: [], shadowingWarnings: [], status: e });
function mh(e, t, n, i) {
  const r = ue(() => rh(t, i), [i, t]), [s, a] = K(() => fo("loading"));
  return ee(() => {
    if (!t) {
      a(fo("unavailable"));
      return;
    }
    let c = !1;
    return a((u) => ({ ...u, status: "loading" })), gh(e, t, n).then(
      (u) => {
        c || a({ ...u, status: "ready" });
      },
      () => {
        c || a(fo("unavailable"));
      }
    ), () => {
      c = !0;
    };
  }, [e, n, r]), s;
}
async function yh(e, t) {
  const n = await e.http.getJson(`${be}/versions/${encodeURIComponent(t)}`);
  return { ...n, state: ur(n.state) };
}
async function xh(e, t) {
  return e.http.postJson(`${be}/definitions`, t);
}
async function wh(e, t) {
  await e.http.deleteJson(`${be}/definitions/${encodeURIComponent(t)}`);
}
async function vh(e, t) {
  await e.http.postJson(`${be}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function bh(e, t) {
  await e.http.deleteJson(`${be}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function jh(e, t, n) {
  return e.http.requestJson(
    `${be}/definitions/${encodeURIComponent(t)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(n)
    }
  );
}
async function Nh(e, t) {
  const n = await e.http.putJson(
    `${be}/drafts/${encodeURIComponent(t.id)}`,
    { state: Gn(t.state), layout: t.layout }
  );
  return { ...n, state: ur(n.state) };
}
async function Sh(e, t) {
  return e.http.postJson(`${be}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function Ch(e, t) {
  return e.http.postJson(`${be}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function kh(e, t) {
  const n = { ...t, state: Gn(t.state) };
  try {
    return await e.http.postJson(`${sh}/workflows/drafts/test-runs`, n);
  } catch (i) {
    const r = Hh(i);
    if (r) return r;
    throw i;
  }
}
async function Rl(e, t) {
  return e.http.postJson(`${be}/executables/${encodeURIComponent(t)}/run`, {});
}
async function Ll(e) {
  const t = [`${be}/executables`, "/_demo/workflows/executables"], n = [];
  for (const i of t)
    try {
      const r = await e.http.getJson(i);
      return Eh(r);
    } catch (r) {
      n.push(r);
    }
  if (n.length > 0 && n.every(da)) return [];
  throw n.find((i) => !da(i)) ?? n[n.length - 1] ?? new Error("Workflow executables could not be loaded.");
}
function Eh(e) {
  return Array.isArray(e) ? e : e.executables ?? [];
}
function da(e) {
  if (!(e instanceof Error)) return !1;
  const t = e.message.toLowerCase();
  return /\b404\b/.test(t) || t.includes("not found");
}
async function Ih(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.runKind && n.set("runKind", t.runKind), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const i = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${i ? `?${i}` : ""}`);
}
async function Ah(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function _h(e, t, n) {
  return e.http.getJson(
    `/runtime/workflows/instances/${encodeURIComponent(t)}/activity-executions/${encodeURIComponent(n)}`
  );
}
async function dr(e) {
  return e.http.getJson(`${be}/activities`);
}
async function Dh(e) {
  return e.http.getJson(`${be}/activities/availability/settings`);
}
async function Th(e, t) {
  return e.http.putJson(`${be}/activities/availability/settings`, t);
}
async function zl(e) {
  return e.http.getJson(`${be}/activities/availability/diagnostics`);
}
async function $h(e) {
  return e.http.getJson(`${be}/runtime-diagnostics/settings`);
}
async function Ph(e, t) {
  return e.http.putJson(`${be}/runtime-diagnostics/settings`, t);
}
async function Mh(e) {
  const t = await fr(e, [
    `${be}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? fa(t) : fa(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function Rh(e) {
  const t = await fr(e, [
    `${be}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : Li;
}
async function Lh(e) {
  const t = await fr(e, [
    `${be}/descriptors/variables`,
    "/descriptors/variables"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((i) => zh(i));
}
function zh(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = typeof t.alias == "string" && t.alias.length > 0, i = typeof t.typeName == "string" && t.typeName.length > 0;
  return n || i;
}
async function Vh(e) {
  const t = await fr(e, [
    `${be}/descriptors/storage-drivers`,
    "/descriptors/storage-drivers"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((i) => Oh(i));
}
function Oh(e) {
  return !!e && typeof e == "object" && typeof e.typeName == "string" && e.typeName.length > 0;
}
async function fr(e, t) {
  let n;
  for (const i of t)
    try {
      return await e.http.getJson(i);
    } catch (r) {
      n = r;
    }
  throw n;
}
function fa(e) {
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
function Hh(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = pa(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return pa(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function pa(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const Li = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function Vl(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e) {
    const r = t(i)?.trim() || "Uncategorized", s = n.get(r);
    s ? s.push(i) : n.set(r, [i]);
  }
  return [...n.entries()].map(([i, r]) => ({ category: i, items: r })).sort((i, r) => i.category.localeCompare(r.category));
}
const Wh = [
  "Available",
  "BlockedByHostBaseline",
  "HiddenByManagementSettings",
  "RemovedFromCatalog",
  "UnresolvedReference"
], Fh = {
  Available: "Available",
  BlockedByHostBaseline: "Host blocked",
  HiddenByManagementSettings: "Management hidden",
  RemovedFromCatalog: "Removed",
  UnresolvedReference: "Unresolved"
}, Ol = {
  Catalog: "Catalog",
  HostBaseline: "Host baseline",
  ManagementSettings: "Management settings"
}, Bh = Object.keys(Ol);
function Kh(e) {
  const t = typeof e == "number" ? Bh[e] : e;
  return t && Ol[t] || t?.toString() || "";
}
function At(e) {
  return typeof e == "string" ? e : typeof e == "number" ? Wh[e] ?? "Available" : "Available";
}
function zn(e) {
  const t = At(e);
  return Fh[t] ?? t;
}
function Hl(e) {
  return At(e).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Wl(e) {
  return At(e) !== "Available";
}
function Fl(e) {
  return At(e.state) === "BlockedByHostBaseline";
}
function Xh(e) {
  return e === "Only" || e === 1 ? "Only" : "AllExcept";
}
function qh(e) {
  return e === "Only" ? 1 : 0;
}
function Po(e) {
  const t = e?.rules;
  return {
    mode: Xh(e?.mode),
    activityTypes: t?.activityTypes ?? [],
    sets: t?.sets ?? []
  };
}
function Yh(e) {
  return e.referenceKind === 0 || e.referenceKind === "ActivityType";
}
function jn(e) {
  return e.activityTypeKey ?? e.activityDefinitionId ?? "";
}
function Uh(e) {
  return [...e?.items ?? []].filter(Yh).filter((t) => t.activityTypeKey && t.activityDefinitionId).sort((t, n) => Vn(t).localeCompare(Vn(n)));
}
function Zh(e) {
  return [...e?.items ?? []].filter((t) => {
    const n = At(t.state);
    return n === "RemovedFromCatalog" || n === "UnresolvedReference";
  }).sort((t, n) => (t.referenceName ?? "").localeCompare(n.referenceName ?? ""));
}
function Gh(e) {
  return Vl(e, (t) => t.category).map((t) => ({ category: t.category, entries: t.items }));
}
function Jh(e, t, n) {
  const i = e[t].includes(n);
  return e.mode === "Only" ? i : !i;
}
function po(e, t, n, i) {
  const r = e.mode === "Only" ? i : !i;
  return e[t].includes(n) === r ? e : { ...e, [t]: tg(e[t], n) };
}
function Qh(e, t, n) {
  const i = new Set(n.activityTypes), r = new Set(n.sets), s = /* @__PURE__ */ new Map();
  for (const c of t)
    if (r.has(c.name))
      for (const u of c.activityTypeKeys ?? [])
        s.has(u) || s.set(u, c.name);
  const a = /* @__PURE__ */ new Map();
  for (const c of e) {
    const u = jn(c);
    if (Fl(c)) {
      a.set(u, { enabled: !1, lockedBy: "host-baseline" });
      continue;
    }
    const l = s.get(u), d = i.has(u) || l !== void 0, f = n.mode === "Only" ? d : !d;
    a.set(u, l ? { enabled: f, lockedBy: "set-rule", governingSet: l } : { enabled: f, lockedBy: null });
  }
  return a;
}
function eg(e, t) {
  const n = Po(t), i = (r) => [...r].sort((s, a) => s.localeCompare(a)).join(`
`);
  return e.mode !== n.mode || i(e.activityTypes) !== i(n.activityTypes) || i(e.sets) !== i(n.sets);
}
function tg(e, t) {
  return e.includes(t) ? e.filter((n) => n !== t) : [...e, t].sort((n, i) => n.localeCompare(i));
}
function Vn(e) {
  const t = e?.displayName?.trim();
  if (t && t.toLowerCase() !== "activity") return t;
  const n = ig(e?.activityTypeKey);
  return ng(n) || t || e?.activityTypeKey || "Activity";
}
function ha(e) {
  const t = Bl(e?.activityTypeKey);
  if (t.length === 0) return "";
  const n = t[t.length - 1], i = t[t.length - 2];
  return n === "Activity" && i ? `${i}.${n}` : n;
}
function ga(e) {
  const t = e.description?.trim();
  if (t) return t;
  const n = e.reason?.trim();
  return !n || !Wl(e.state) ? "" : n;
}
function ng(e) {
  return e.replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/\bHttp\b/g, "HTTP").replace(/\bJson\b/g, "JSON").replace(/\bJava Script\b/g, "JavaScript").replace(/\bUrl\b/g, "URL").replace(/\bXml\b/g, "XML").trim();
}
function ig(e) {
  const t = Bl(e), n = t[t.length - 1] ?? "";
  return n === "Activity" && t.length > 1 ? t[t.length - 2] : n;
}
function Bl(e) {
  return e?.split(".").filter(Boolean) ?? [];
}
function ma(e) {
  return (e ?? []).flatMap((t) => {
    if (!Rn(t)) return [];
    const n = (ut(t, ["displayName", "DisplayName"]) || ut(t, us)).trim();
    if (!n) return [];
    const i = (ls(t).alias || ut(t, ["typeName", "TypeName", "alias", "Alias"])).trim();
    return [{ name: n, typeName: i, description: ut(t, ["description", "Description"]).trim() }];
  });
}
function rg(e, t) {
  const n = new Set(e.filter((i) => !!i));
  return (t?.items ?? []).find((i) => Wl(i.state) ? [i.activityDefinitionId, i.activityTypeKey, i.referenceName].some((r) => r && n.has(r)) : !1) ?? null;
}
const og = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function Kl(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function ps(e) {
  return Kl(e.name);
}
function sg(e, t) {
  const n = ps(t), i = e[n];
  return t.isWrapped === !1 ? i ?? t.defaultValue ?? "" : ql(i, t);
}
function Xl(e, t) {
  return ql(e[ps(t)], t);
}
function ag(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function cg(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function ya(e, t, n) {
  return {
    ...e,
    [ps(t)]: n
  };
}
function lg(e, t) {
  return t.isWrapped === !1 ? sg(e, t) : Xl(e, t).expression.value;
}
function ql(e, t) {
  return yg(e) ? {
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
const ug = /* @__PURE__ */ new Set([
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
function dg(e) {
  const t = e?.trim();
  if (!t) return null;
  const n = /^([\w.+]+)\[\]/.exec(t);
  if (n) return { elementTypeName: n[1] };
  const i = t.indexOf("`");
  if (i < 0) return null;
  const r = t.slice(0, i), s = (r.split(".").pop() ?? r).toLowerCase();
  return ug.has(s) ? { elementTypeName: fg(t.slice(i)) } : null;
}
function fg(e) {
  const t = /\[\[([\w.+]+)/.exec(e);
  if (t) return t[1];
  const n = /\[([\w.+]+)/.exec(e);
  return n ? n[1] : null;
}
function pg(e) {
  const t = e.uiHint?.toLowerCase();
  return t === "json" || t === "code" ? !0 : e.uiSpecifications?.repeater === !1;
}
function hg(e) {
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
function gg(e) {
  const t = (e ?? "").toLowerCase();
  return t === "system.boolean" || t === "boolean" || t === "bool" ? !1 : "";
}
function mg(e, t) {
  return {
    ...e,
    typeName: t ?? "System.String",
    isWrapped: !1,
    // The description documents the collection as a whole; repeating it on every row is noise.
    description: null
  };
}
function ho(e, t, n) {
  if (t === n || t < 0 || n < 0 || t >= e.length || n >= e.length) return e;
  const i = [...e], [r] = i.splice(t, 1);
  return i.splice(n, 0, r), i;
}
function yg(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
function hs(e) {
  return gs(e?.trim() ?? "") || e;
}
function gs(e) {
  if (!e) return "";
  const t = xg(e);
  if (!t) return "";
  const n = /^(.*)((?:\[\])+)$/.exec(t);
  if (n) return `${gs(n[1])}${n[2]}`;
  const i = t.indexOf("`");
  if (i >= 0) {
    const r = xa(t.slice(0, i)), s = wg(t.slice(i));
    return s.length > 0 ? `${r}<${s.join(", ")}>` : r;
  }
  return xa(t);
}
function xg(e) {
  let t = 0;
  for (let n = 0; n < e.length; n++) {
    const i = e[n];
    if (i === "[") t++;
    else if (i === "]") t--;
    else if (i === "," && t === 0) return e.slice(0, n).trim();
  }
  return e.trim();
}
function xa(e) {
  const t = e.split(".").filter(Boolean).at(-1) ?? e;
  return t.split("+").filter(Boolean).at(-1) ?? t;
}
function wg(e) {
  const t = e.indexOf("[");
  if (t < 0) return [];
  const n = wa(e, t);
  return n == null ? [] : vg(n).map((i) => {
    const r = i.trim(), s = r.startsWith("[") ? wa(r, 0) ?? r : r;
    return gs(s);
  }).filter(Boolean);
}
function wa(e, t) {
  let n = 0;
  for (let i = t; i < e.length; i++)
    if (e[i] === "[") n++;
    else if (e[i] === "]" && --n === 0) return e.slice(t + 1, i);
  return null;
}
function vg(e) {
  const t = [];
  let n = 0, i = 0;
  for (let r = 0; r < e.length; r++) {
    const s = e[r];
    s === "[" ? n++ : s === "]" ? n-- : s === "," && n === 0 && (t.push(e.slice(i, r)), i = r + 1);
  }
  return t.push(e.slice(i)), t.map((r) => r.trim()).filter(Boolean);
}
function Re(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function ms(e, t) {
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
function _t(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function pr(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ o.jsx(cl, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ o.jsx(is, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ o.jsx(Rf, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ o.jsx(Zt, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ o.jsx(Mf, { size: 15 });
    default:
      return /* @__PURE__ */ o.jsx(Zn, { size: 15 });
  }
}
function va(e) {
  return `${e.activityTypeKey ?? ""}|${e.activityDefinitionId ?? ""}`;
}
function bg(e, t) {
  const n = (a) => (a ?? "").split(".").map((c) => Number.parseInt(c, 10) || 0), i = n(e), r = n(t), s = Math.max(i.length, r.length);
  for (let a = 0; a < s; a++) {
    const c = (i[a] ?? 0) - (r[a] ?? 0);
    if (c !== 0) return c;
  }
  return 0;
}
function jg(e) {
  if (e.lockedBy === "host-baseline") return "Blocked by the host baseline";
  if (e.lockedBy === "set-rule") return `Controlled by the "${e.governingSet}" set rule`;
}
function Yl({ icon: e }) {
  return /* @__PURE__ */ o.jsx("span", { className: "wf-activity-icon", "data-icon": e, "aria-hidden": "true", children: pr(e) });
}
function Ng({ context: e }) {
  const t = ch(e), n = lh(e), i = ah(e), r = uh(e), s = t.data ?? null, a = n.data ?? null, c = t.isLoading || n.isLoading, u = r.isPending, [l, d] = K(() => Po(s)), [f, p] = K(""), [h, m] = K(!1), [v, x] = K(null), [w, b] = K(null);
  ee(() => {
    d(Po(s));
  }, [s]);
  const g = ue(() => Uh(a), [a]), y = ue(() => Zh(a), [a]), N = ue(() => a?.sets ?? [], [a]), j = ue(
    () => Qh(g, N, l),
    [g, N, l]
  ), S = (O) => j.get(jn(O)) ?? { enabled: !0, lockedBy: null }, I = ue(() => {
    const O = /* @__PURE__ */ new Map();
    for (const q of i.data?.activities ?? []) {
      if (!q.activityTypeKey) continue;
      const J = O.get(q.activityTypeKey);
      (!J || bg(q.version, J.version) > 0) && O.set(q.activityTypeKey, q);
    }
    return O;
  }, [i.data]), _ = (O) => I.get(O.activityTypeKey ?? ""), M = ue(() => {
    const O = f.trim().toLowerCase(), q = h ? g.filter((J) => !(j.get(jn(J))?.enabled ?? !0)) : g;
    return O ? q.filter((J) => [
      Vn(J),
      ha(J),
      ga(J),
      J.activityTypeKey,
      J.category
    ].some((G) => (G ?? "").toLowerCase().includes(O))) : q;
  }, [g, f, h, j]), k = ue(() => Gh(M), [M]), $ = w ? g.find((O) => va(O) === w) ?? null : null, z = g.filter(Fl).length, C = g.filter((O) => At(O.state) === "HiddenByManagementSettings").length, A = ue(() => eg(l, s), [l, s]), E = r.error ?? t.error ?? n.error, D = E instanceof Error ? E.message : E ? "Activity availability could not be loaded." : null, P = (O) => {
    x(null), d(O);
  }, T = (O) => P((q) => ({ ...q, mode: O })), F = (O, q) => P((J) => po(J, "activityTypes", O, q)), W = (O, q) => P((J) => O.reduce((G, R) => po(G, "activityTypes", R, q), J)), H = (O, q) => P((J) => po(J, "sets", O, q)), U = () => {
    x(null), r.mutate(
      {
        scope: s?.scope ?? "host-default",
        mode: qh(l.mode),
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
          /* @__PURE__ */ o.jsx(Lf, { size: 18 }),
          " Activity availability"
        ] }),
        /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "Control which activities can be added to new workflows. Host baseline rules always take precedence." })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { className: "wf-actions", children: [
        A && !u && /* @__PURE__ */ o.jsx("span", { className: "wf-chip availability-dirty", children: "Unsaved changes" }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", className: "availability-save", onClick: U, disabled: c || u || !A, children: [
          /* @__PURE__ */ o.jsx(rs, { size: 15 }),
          u ? "Saving…" : "Save"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "availability-body", children: [
      D && /* @__PURE__ */ o.jsx("div", { className: "availability-banner availability-banner-error", children: D }),
      v && !D && /* @__PURE__ */ o.jsx("div", { className: "availability-banner availability-banner-success", children: v }),
      /* @__PURE__ */ o.jsxs("div", { className: "availability-mode", role: "group", "aria-label": "Activity availability mode", children: [
        /* @__PURE__ */ o.jsxs("button", { type: "button", className: l.mode === "AllExcept" ? "active" : "", onClick: () => T("AllExcept"), disabled: c || u, children: [
          /* @__PURE__ */ o.jsx(lo, { size: 15 }),
          /* @__PURE__ */ o.jsxs("span", { children: [
            /* @__PURE__ */ o.jsx("strong", { children: "All except" }),
            /* @__PURE__ */ o.jsx("em", { children: "Activities are available unless you turn them off" })
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", className: l.mode === "Only" ? "active" : "", onClick: () => T("Only"), disabled: c || u, children: [
          /* @__PURE__ */ o.jsx(Zs, { size: 15 }),
          /* @__PURE__ */ o.jsxs("span", { children: [
            /* @__PURE__ */ o.jsx("strong", { children: "Only" }),
            /* @__PURE__ */ o.jsx("em", { children: "Activities are hidden unless you turn them on" })
          ] })
        ] })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { className: "availability-counts", children: [
        /* @__PURE__ */ o.jsxs("span", { children: [
          /* @__PURE__ */ o.jsx(Zs, { size: 14 }),
          " ",
          z,
          " host blocked"
        ] }),
        /* @__PURE__ */ o.jsxs("span", { children: [
          /* @__PURE__ */ o.jsx(lo, { size: 14 }),
          " ",
          C,
          " management hidden"
        ] }),
        /* @__PURE__ */ o.jsxs("span", { children: [
          /* @__PURE__ */ o.jsx(Tn, { size: 14 }),
          " ",
          y.length,
          " unresolved"
        ] })
      ] }),
      N.length > 0 && /* @__PURE__ */ o.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ o.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ o.jsx(os, { size: 14 }),
          " Sets"
        ] }),
        /* @__PURE__ */ o.jsx("div", { className: "availability-set-list", children: N.map((O) => /* @__PURE__ */ o.jsxs("label", { className: "availability-set-option", children: [
          /* @__PURE__ */ o.jsx(
            "input",
            {
              type: "checkbox",
              role: "switch",
              className: "wf-switch-input",
              "aria-label": `Activities in the ${O.name} set available in new workflows`,
              checked: Jh(l, "sets", O.name),
              disabled: c || u,
              onChange: (q) => H(O.name, q.target.checked)
            }
          ),
          /* @__PURE__ */ o.jsx("span", { children: O.name }),
          /* @__PURE__ */ o.jsx("code", { children: (O.activityTypeKeys ?? []).length })
        ] }, O.name)) })
      ] }),
      /* @__PURE__ */ o.jsxs("section", { className: "availability-section availability-section-grow", children: [
        /* @__PURE__ */ o.jsxs("div", { className: "availability-section-head", children: [
          /* @__PURE__ */ o.jsxs("h3", { className: "wf-section-label", children: [
            /* @__PURE__ */ o.jsx(ll, { size: 14 }),
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
                onClick: () => m((O) => !O),
                children: [
                  /* @__PURE__ */ o.jsx(lo, { size: 13 }),
                  "Hidden only"
                ]
              }
            ),
            /* @__PURE__ */ o.jsxs("div", { className: "wf-search availability-search", children: [
              /* @__PURE__ */ o.jsx(ar, { size: 14 }),
              /* @__PURE__ */ o.jsx("input", { type: "search", value: f, placeholder: "Filter activities…", onChange: (O) => p(O.target.value) })
            ] })
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("div", { className: "availability-activity-browser", children: [
          /* @__PURE__ */ o.jsxs("div", { className: "availability-activity-list", children: [
            c && g.length === 0 && /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "Loading availability…" }),
            !c && g.length === 0 && /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "No availability diagnostics reported." }),
            !c && g.length > 0 && M.length === 0 && /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: h && !f.trim() ? "No hidden activities — everything is turned on." : "No activities match the filter." }),
            k.map((O) => {
              const q = O.entries.filter((X) => S(X).lockedBy === null), J = q.filter((X) => S(X).enabled).length, G = q.length > 0 && J === q.length, R = J > 0 && !G;
              return /* @__PURE__ */ o.jsxs("div", { className: "availability-group", children: [
                /* @__PURE__ */ o.jsxs("div", { className: "availability-group-header", children: [
                  /* @__PURE__ */ o.jsxs("span", { className: "availability-group-title", children: [
                    O.category,
                    /* @__PURE__ */ o.jsx("small", { children: O.entries.length })
                  ] }),
                  q.length > 0 && /* @__PURE__ */ o.jsx(
                    "input",
                    {
                      type: "checkbox",
                      role: "switch",
                      className: "wf-switch-input",
                      "aria-label": `Toggle the ${q.length} listed ${O.category} activities`,
                      checked: G,
                      ref: (X) => {
                        X && (X.indeterminate = R);
                      },
                      disabled: c || u,
                      onChange: () => W(q.map(jn), !G)
                    }
                  )
                ] }),
                O.entries.map((X) => {
                  const de = jn(X), le = va(X), te = At(X.state), ie = S(X), fe = ie.lockedBy !== null, V = un(_(X)), ne = Vn(X), ge = ha(X), ye = ga(X), Ie = w === le;
                  return /* @__PURE__ */ o.jsxs("div", { className: `availability-activity-row${Ie ? " selected" : ""}${fe ? " disabled" : ""}`, children: [
                    /* @__PURE__ */ o.jsxs(
                      "button",
                      {
                        type: "button",
                        className: "availability-activity-open",
                        "aria-expanded": Ie,
                        title: "Show activity details",
                        onClick: () => b(Ie ? null : le),
                        children: [
                          /* @__PURE__ */ o.jsx(Yl, { icon: V }),
                          /* @__PURE__ */ o.jsxs("span", { className: "availability-activity-main", children: [
                            /* @__PURE__ */ o.jsxs("span", { className: "availability-activity-title-line", children: [
                              /* @__PURE__ */ o.jsx("strong", { children: ne }),
                              ge && /* @__PURE__ */ o.jsx("code", { title: X.activityTypeKey ?? void 0, children: ge })
                            ] }),
                            ye && /* @__PURE__ */ o.jsx("span", { className: "availability-activity-description", title: ye, children: ye })
                          ] })
                        ]
                      }
                    ),
                    te !== "Available" && /* @__PURE__ */ o.jsx("em", { className: `availability-state ${Hl(X.state)}`, children: zn(X.state) }),
                    /* @__PURE__ */ o.jsx(
                      "input",
                      {
                        type: "checkbox",
                        role: "switch",
                        className: "wf-switch-input",
                        "aria-label": `${ne} available in new workflows`,
                        title: jg(ie),
                        checked: ie.enabled,
                        disabled: c || u || fe,
                        onChange: (_e) => F(de, _e.target.checked)
                      }
                    )
                  ] }, le);
                })
              ] }, O.category);
            })
          ] }),
          $ && /* @__PURE__ */ o.jsx(
            Sg,
            {
              entry: $,
              catalogItem: _($),
              onClose: () => b(null)
            }
          )
        ] })
      ] }),
      y.length > 0 && /* @__PURE__ */ o.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ o.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ o.jsx(Tn, { size: 14 }),
          " Unresolved references"
        ] }),
        /* @__PURE__ */ o.jsx("div", { className: "availability-unresolved-list", children: y.map((O) => /* @__PURE__ */ o.jsxs("span", { children: [
          /* @__PURE__ */ o.jsx("strong", { children: O.referenceName }),
          /* @__PURE__ */ o.jsx("em", { children: zn(O.state) })
        ] }, `${O.layer}-${O.referenceKind}-${O.referenceName}`)) })
      ] })
    ] })
  ] });
}
function Sg({
  entry: e,
  catalogItem: t,
  onClose: n
}) {
  const i = Vn(e), r = e.description?.trim() || t?.description?.trim(), s = ue(() => ma(t?.inputs), [t]), a = ue(() => ma(t?.outputs), [t]), c = ue(() => JSON.stringify({ diagnostic: e, catalog: t }, null, 2), [e, t]), u = [
    ["Type", e.activityTypeKey],
    ["Definition ID", e.activityDefinitionId],
    ["Category", e.category],
    ["Version", t?.version],
    ["Execution", t?.executionType],
    ["Policy layer", Kh(e.layer)]
  ];
  return /* @__PURE__ */ o.jsxs("aside", { className: "availability-details", "aria-label": `${i} details`, children: [
    /* @__PURE__ */ o.jsxs("header", { className: "availability-details-header", children: [
      /* @__PURE__ */ o.jsx(Yl, { icon: un(t) }),
      /* @__PURE__ */ o.jsx("h4", { children: i }),
      /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-icon-button availability-details-close", "aria-label": "Close details", onClick: n, children: /* @__PURE__ */ o.jsx(ss, { size: 14 }) })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "availability-details-body", children: [
      /* @__PURE__ */ o.jsxs("p", { className: "availability-details-status", children: [
        /* @__PURE__ */ o.jsx("em", { className: `availability-state ${Hl(e.state)}`, children: zn(e.state) }),
        e.reason?.trim() && /* @__PURE__ */ o.jsx("span", { children: e.reason })
      ] }),
      r && /* @__PURE__ */ o.jsx("p", { className: "availability-details-description", children: r }),
      /* @__PURE__ */ o.jsx("dl", { className: "availability-details-meta", children: u.map(([l, d]) => d?.trim() ? /* @__PURE__ */ o.jsxs(Fe.Fragment, { children: [
        /* @__PURE__ */ o.jsx("dt", { children: l }),
        /* @__PURE__ */ o.jsx("dd", { children: /* @__PURE__ */ o.jsx("code", { children: d }) })
      ] }, l) : null) }),
      s.length > 0 && /* @__PURE__ */ o.jsx(ba, { title: "Inputs", items: s }),
      a.length > 0 && /* @__PURE__ */ o.jsx(ba, { title: "Outputs", items: a }),
      !t && /* @__PURE__ */ o.jsx("p", { className: "wf-muted availability-details-note", children: "Catalog metadata is unavailable for this activity — it is not exposed by the currently saved policy." }),
      /* @__PURE__ */ o.jsxs("details", { className: "availability-details-raw", children: [
        /* @__PURE__ */ o.jsx("summary", { children: "Raw metadata" }),
        /* @__PURE__ */ o.jsx("pre", { children: c })
      ] })
    ] })
  ] });
}
function ba({ title: e, items: t }) {
  return /* @__PURE__ */ o.jsxs("section", { className: "availability-details-arguments", children: [
    /* @__PURE__ */ o.jsx("h5", { children: e }),
    /* @__PURE__ */ o.jsx("ul", { children: t.map((n) => /* @__PURE__ */ o.jsxs("li", { title: n.description || void 0, children: [
      /* @__PURE__ */ o.jsx("strong", { children: n.name }),
      n.typeName && /* @__PURE__ */ o.jsx("code", { children: hs(n.typeName) })
    ] }, n.name)) })
  ] });
}
const Ul = ["Off", "Metadata", "DiagnosticSnapshot", "Payload"], ja = new Map(Ul.map((e, t) => [e, t])), Cg = [
  { id: "workflowInputs", label: "Workflow inputs", detail: "Start arguments and workflow-level input values." },
  { id: "workflowOutputs", label: "Workflow outputs", detail: "Values emitted as workflow outputs." },
  { id: "activityInputs", label: "Activity inputs", detail: "Materialized values passed to activities." },
  { id: "activityOutputs", label: "Activity outputs", detail: "Values produced by completed activities." },
  { id: "containerVariables", label: "Container variables", detail: "Runtime values scoped to containers." },
  { id: "durableValues", label: "Durable values", detail: "Persisted wait/resume payload evidence." },
  { id: "incidents", label: "Incidents", detail: "Fault and incident diagnostic material." },
  { id: "diagnostics", label: "Diagnostics", detail: "Internal runtime diagnostic payloads." }
];
function kg({ context: e }) {
  const t = dh(e), n = fh(e), i = t.data ?? null, r = t.isLoading, s = n.isPending, a = i?.permissions.canManage ?? !1, [c, u] = K(() => Na(i)), [l, d] = K(null);
  ee(() => {
    u(Na(i));
  }, [i]);
  const f = n.error ?? t.error, p = f instanceof Error ? f.message : f ? "Runtime diagnostics settings could not be loaded." : null, h = i?.hostPolicy.limitationReasons ?? [], m = i?.effective.limitationReasons ?? [], v = ue(() => Ul.filter((g) => Eg(g, i)), [i]), x = (g) => u((y) => ({ ...y, defaultLevel: g })), w = (g, y) => u((N) => {
    const j = { ...N.subjectOverrides };
    return y === "inherit" ? delete j[g] : j[g] = y, { ...N, subjectOverrides: j };
  }), b = () => {
    d(null), n.mutate(
      {
        scope: i?.requested.scope ?? "host-default",
        defaultLevel: c.defaultLevel,
        subjectOverrides: c.subjectOverrides
      },
      { onSuccess: () => d("Runtime diagnostics saved.") }
    );
  };
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-page runtime-diagnostics-page", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-page-header", children: [
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("div", { className: "wf-kicker", children: "Workflows" }),
        /* @__PURE__ */ o.jsxs("h2", { children: [
          /* @__PURE__ */ o.jsx(Gs, { size: 18 }),
          " Runtime diagnostics"
        ] }),
        /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "Control future runtime value evidence capture. Existing workflow runs keep their captured evidence." })
      ] }),
      /* @__PURE__ */ o.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: b, disabled: r || s || !a, children: [
        /* @__PURE__ */ o.jsx(rs, { size: 15 }),
        s ? "Saving..." : "Save"
      ] }) })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "runtime-diagnostics-body", children: [
      p && /* @__PURE__ */ o.jsx("div", { className: "runtime-diagnostics-banner error", children: p }),
      l && !p && /* @__PURE__ */ o.jsx("div", { className: "runtime-diagnostics-banner success", children: l }),
      /* @__PURE__ */ o.jsxs("section", { className: "runtime-diagnostics-section", children: [
        /* @__PURE__ */ o.jsxs("div", { className: "runtime-diagnostics-section-head", children: [
          /* @__PURE__ */ o.jsxs("h3", { className: "wf-section-label", children: [
            /* @__PURE__ */ o.jsx(zf, { size: 14 }),
            " Default capture"
          ] }),
          i && /* @__PURE__ */ o.jsxs("span", { className: "runtime-diagnostics-effective", children: [
            "Effective: ",
            Nn(i.effective.defaultLevel)
          ] })
        ] }),
        /* @__PURE__ */ o.jsx(
          Ag,
          {
            value: c.defaultLevel,
            allowedLevels: v,
            disabled: r || s || !a,
            onChange: x
          }
        )
      ] }),
      /* @__PURE__ */ o.jsxs("section", { className: "runtime-diagnostics-section runtime-diagnostics-section-grow", children: [
        /* @__PURE__ */ o.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ o.jsx(Gs, { size: 14 }),
          " Subject overrides"
        ] }),
        /* @__PURE__ */ o.jsx("div", { className: "runtime-diagnostics-subjects", children: Cg.map((g) => {
          const y = c.subjectOverrides[g.id] ?? "inherit", N = i ? Ig(i, g.id) : null;
          return /* @__PURE__ */ o.jsxs("label", { className: "runtime-diagnostics-subject", children: [
            /* @__PURE__ */ o.jsxs("span", { children: [
              /* @__PURE__ */ o.jsx("strong", { children: g.label }),
              /* @__PURE__ */ o.jsx("em", { children: g.detail })
            ] }),
            /* @__PURE__ */ o.jsxs("span", { className: "runtime-diagnostics-subject-controls", children: [
              N && /* @__PURE__ */ o.jsxs("code", { children: [
                "Effective: ",
                Nn(N)
              ] }),
              /* @__PURE__ */ o.jsxs(
                "select",
                {
                  value: y,
                  disabled: r || s || !a,
                  onChange: (j) => w(g.id, j.target.value),
                  children: [
                    /* @__PURE__ */ o.jsx("option", { value: "inherit", children: "Inherit default" }),
                    v.map((j) => /* @__PURE__ */ o.jsx("option", { value: j, children: Nn(j) }, j))
                  ]
                }
              )
            ] })
          ] }, g.id);
        }) })
      ] }),
      (h.length > 0 || m.length > 0 || !a) && /* @__PURE__ */ o.jsxs("section", { className: "runtime-diagnostics-section", children: [
        /* @__PURE__ */ o.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ o.jsx(Tn, { size: 14 }),
          " Policy"
        ] }),
        /* @__PURE__ */ o.jsxs("div", { className: "runtime-diagnostics-policy", children: [
          !a && /* @__PURE__ */ o.jsx("span", { children: "Current user can view settings but cannot change them." }),
          i && /* @__PURE__ */ o.jsxs("span", { children: [
            "Host maximum: ",
            Nn(i.hostPolicy.maximumLevel)
          ] }),
          m.map((g) => /* @__PURE__ */ o.jsx("span", { children: g }, `effective-${g}`)),
          h.map((g) => /* @__PURE__ */ o.jsx("span", { children: g }, `host-${g}`))
        ] })
      ] })
    ] })
  ] });
}
function Na(e) {
  return {
    defaultLevel: e?.requested.defaultLevel ?? "DiagnosticSnapshot",
    subjectOverrides: { ...e?.requested.subjectOverrides ?? {} }
  };
}
function Eg(e, t) {
  return t ? e === "Payload" && !t.permissions.canEnableFullPayloads ? !1 : (ja.get(e) ?? 0) <= (ja.get(t.hostPolicy.maximumLevel) ?? 0) : e !== "Payload";
}
function Ig(e, t) {
  return e.effective.subjectOverrides?.[t] ?? e.effective.defaultLevel;
}
function Nn(e) {
  return e === "DiagnosticSnapshot" ? "Diagnostic snapshot" : e;
}
function Ag({
  value: e,
  allowedLevels: t,
  disabled: n,
  onChange: i
}) {
  return /* @__PURE__ */ o.jsx("div", { className: "runtime-diagnostics-levels", role: "group", "aria-label": "Default runtime diagnostics level", children: t.map((r) => /* @__PURE__ */ o.jsxs(
    "button",
    {
      type: "button",
      className: e === r ? "active" : "",
      disabled: n,
      onClick: () => i(r),
      children: [
        /* @__PURE__ */ o.jsx("strong", { children: Nn(r) }),
        /* @__PURE__ */ o.jsx("em", { children: _g(r) })
      ]
    },
    r
  )) });
}
function _g(e) {
  switch (e) {
    case "Off":
      return "Do not store runtime value evidence.";
    case "Metadata":
      return "Store capture metadata without value shape.";
    case "DiagnosticSnapshot":
      return "Store bounded, sanitized value shape and previews.";
    case "Payload":
      return "Store full payloads when host policy permits it.";
  }
}
function Ae(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, i; n < e.length; n++)
      (i = Ae(e[n])) !== "" && (t += (t && " ") + i);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var Dg = { value: () => {
} };
function hr() {
  for (var e = 0, t = arguments.length, n = {}, i; e < t; ++e) {
    if (!(i = arguments[e] + "") || i in n || /[\s.]/.test(i)) throw new Error("illegal type: " + i);
    n[i] = [];
  }
  return new zi(n);
}
function zi(e) {
  this._ = e;
}
function Tg(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var i = "", r = n.indexOf(".");
    if (r >= 0 && (i = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: i };
  });
}
zi.prototype = hr.prototype = {
  constructor: zi,
  on: function(e, t) {
    var n = this._, i = Tg(e + "", n), r, s = -1, a = i.length;
    if (arguments.length < 2) {
      for (; ++s < a; ) if ((r = (e = i[s]).type) && (r = $g(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++s < a; )
      if (r = (e = i[s]).type) n[r] = Sa(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = Sa(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new zi(e);
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
function $g(e, t) {
  for (var n = 0, i = e.length, r; n < i; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function Sa(e, t, n) {
  for (var i = 0, r = e.length; i < r; ++i)
    if (e[i].name === t) {
      e[i] = Dg, e = e.slice(0, i).concat(e.slice(i + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var Mo = "http://www.w3.org/1999/xhtml";
const Ca = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: Mo,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function gr(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Ca.hasOwnProperty(t) ? { space: Ca[t], local: e } : e;
}
function Pg(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === Mo && t.documentElement.namespaceURI === Mo ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Mg(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function Zl(e) {
  var t = gr(e);
  return (t.local ? Mg : Pg)(t);
}
function Rg() {
}
function ys(e) {
  return e == null ? Rg : function() {
    return this.querySelector(e);
  };
}
function Lg(e) {
  typeof e != "function" && (e = ys(e));
  for (var t = this._groups, n = t.length, i = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = i[r] = new Array(a), u, l, d = 0; d < a; ++d)
      (u = s[d]) && (l = e.call(u, u.__data__, d, s)) && ("__data__" in u && (l.__data__ = u.__data__), c[d] = l);
  return new Be(i, this._parents);
}
function zg(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function Vg() {
  return [];
}
function Gl(e) {
  return e == null ? Vg : function() {
    return this.querySelectorAll(e);
  };
}
function Og(e) {
  return function() {
    return zg(e.apply(this, arguments));
  };
}
function Hg(e) {
  typeof e == "function" ? e = Og(e) : e = Gl(e);
  for (var t = this._groups, n = t.length, i = [], r = [], s = 0; s < n; ++s)
    for (var a = t[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && (i.push(e.call(u, u.__data__, l, a)), r.push(u));
  return new Be(i, r);
}
function Jl(e) {
  return function() {
    return this.matches(e);
  };
}
function Ql(e) {
  return function(t) {
    return t.matches(e);
  };
}
var Wg = Array.prototype.find;
function Fg(e) {
  return function() {
    return Wg.call(this.children, e);
  };
}
function Bg() {
  return this.firstElementChild;
}
function Kg(e) {
  return this.select(e == null ? Bg : Fg(typeof e == "function" ? e : Ql(e)));
}
var Xg = Array.prototype.filter;
function qg() {
  return Array.from(this.children);
}
function Yg(e) {
  return function() {
    return Xg.call(this.children, e);
  };
}
function Ug(e) {
  return this.selectAll(e == null ? qg : Yg(typeof e == "function" ? e : Ql(e)));
}
function Zg(e) {
  typeof e != "function" && (e = Jl(e));
  for (var t = this._groups, n = t.length, i = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = i[r] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new Be(i, this._parents);
}
function eu(e) {
  return new Array(e.length);
}
function Gg() {
  return new Be(this._enter || this._groups.map(eu), this._parents);
}
function Zi(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
Zi.prototype = {
  constructor: Zi,
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
function Jg(e) {
  return function() {
    return e;
  };
}
function Qg(e, t, n, i, r, s) {
  for (var a = 0, c, u = t.length, l = s.length; a < l; ++a)
    (c = t[a]) ? (c.__data__ = s[a], i[a] = c) : n[a] = new Zi(e, s[a]);
  for (; a < u; ++a)
    (c = t[a]) && (r[a] = c);
}
function em(e, t, n, i, r, s, a) {
  var c, u, l = /* @__PURE__ */ new Map(), d = t.length, f = s.length, p = new Array(d), h;
  for (c = 0; c < d; ++c)
    (u = t[c]) && (p[c] = h = a.call(u, u.__data__, c, t) + "", l.has(h) ? r[c] = u : l.set(h, u));
  for (c = 0; c < f; ++c)
    h = a.call(e, s[c], c, s) + "", (u = l.get(h)) ? (i[c] = u, u.__data__ = s[c], l.delete(h)) : n[c] = new Zi(e, s[c]);
  for (c = 0; c < d; ++c)
    (u = t[c]) && l.get(p[c]) === u && (r[c] = u);
}
function tm(e) {
  return e.__data__;
}
function nm(e, t) {
  if (!arguments.length) return Array.from(this, tm);
  var n = t ? em : Qg, i = this._parents, r = this._groups;
  typeof e != "function" && (e = Jg(e));
  for (var s = r.length, a = new Array(s), c = new Array(s), u = new Array(s), l = 0; l < s; ++l) {
    var d = i[l], f = r[l], p = f.length, h = im(e.call(d, d && d.__data__, l, i)), m = h.length, v = c[l] = new Array(m), x = a[l] = new Array(m), w = u[l] = new Array(p);
    n(d, f, v, x, w, h, t);
    for (var b = 0, g = 0, y, N; b < m; ++b)
      if (y = v[b]) {
        for (b >= g && (g = b + 1); !(N = x[g]) && ++g < m; ) ;
        y._next = N || null;
      }
  }
  return a = new Be(a, i), a._enter = c, a._exit = u, a;
}
function im(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function rm() {
  return new Be(this._exit || this._groups.map(eu), this._parents);
}
function om(e, t, n) {
  var i = this.enter(), r = this, s = this.exit();
  return typeof e == "function" ? (i = e(i), i && (i = i.selection())) : i = i.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? s.remove() : n(s), i && r ? i.merge(r).order() : r;
}
function sm(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, i = t._groups, r = n.length, s = i.length, a = Math.min(r, s), c = new Array(r), u = 0; u < a; ++u)
    for (var l = n[u], d = i[u], f = l.length, p = c[u] = new Array(f), h, m = 0; m < f; ++m)
      (h = l[m] || d[m]) && (p[m] = h);
  for (; u < r; ++u)
    c[u] = n[u];
  return new Be(c, this._parents);
}
function am() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var i = e[t], r = i.length - 1, s = i[r], a; --r >= 0; )
      (a = i[r]) && (s && a.compareDocumentPosition(s) ^ 4 && s.parentNode.insertBefore(a, s), s = a);
  return this;
}
function cm(e) {
  e || (e = lm);
  function t(f, p) {
    return f && p ? e(f.__data__, p.__data__) : !f - !p;
  }
  for (var n = this._groups, i = n.length, r = new Array(i), s = 0; s < i; ++s) {
    for (var a = n[s], c = a.length, u = r[s] = new Array(c), l, d = 0; d < c; ++d)
      (l = a[d]) && (u[d] = l);
    u.sort(t);
  }
  return new Be(r, this._parents).order();
}
function lm(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function um() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function dm() {
  return Array.from(this);
}
function fm() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var i = e[t], r = 0, s = i.length; r < s; ++r) {
      var a = i[r];
      if (a) return a;
    }
  return null;
}
function pm() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function hm() {
  return !this.node();
}
function gm(e) {
  for (var t = this._groups, n = 0, i = t.length; n < i; ++n)
    for (var r = t[n], s = 0, a = r.length, c; s < a; ++s)
      (c = r[s]) && e.call(c, c.__data__, s, r);
  return this;
}
function mm(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function ym(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function xm(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function wm(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function vm(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function bm(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function jm(e, t) {
  var n = gr(e);
  if (arguments.length < 2) {
    var i = this.node();
    return n.local ? i.getAttributeNS(n.space, n.local) : i.getAttribute(n);
  }
  return this.each((t == null ? n.local ? ym : mm : typeof t == "function" ? n.local ? bm : vm : n.local ? wm : xm)(n, t));
}
function tu(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function Nm(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Sm(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function Cm(e, t, n) {
  return function() {
    var i = t.apply(this, arguments);
    i == null ? this.style.removeProperty(e) : this.style.setProperty(e, i, n);
  };
}
function km(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? Nm : typeof t == "function" ? Cm : Sm)(e, t, n ?? "")) : Jt(this.node(), e);
}
function Jt(e, t) {
  return e.style.getPropertyValue(t) || tu(e).getComputedStyle(e, null).getPropertyValue(t);
}
function Em(e) {
  return function() {
    delete this[e];
  };
}
function Im(e, t) {
  return function() {
    this[e] = t;
  };
}
function Am(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function _m(e, t) {
  return arguments.length > 1 ? this.each((t == null ? Em : typeof t == "function" ? Am : Im)(e, t)) : this.node()[e];
}
function nu(e) {
  return e.trim().split(/^|\s+/);
}
function xs(e) {
  return e.classList || new iu(e);
}
function iu(e) {
  this._node = e, this._names = nu(e.getAttribute("class") || "");
}
iu.prototype = {
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
function ru(e, t) {
  for (var n = xs(e), i = -1, r = t.length; ++i < r; ) n.add(t[i]);
}
function ou(e, t) {
  for (var n = xs(e), i = -1, r = t.length; ++i < r; ) n.remove(t[i]);
}
function Dm(e) {
  return function() {
    ru(this, e);
  };
}
function Tm(e) {
  return function() {
    ou(this, e);
  };
}
function $m(e, t) {
  return function() {
    (t.apply(this, arguments) ? ru : ou)(this, e);
  };
}
function Pm(e, t) {
  var n = nu(e + "");
  if (arguments.length < 2) {
    for (var i = xs(this.node()), r = -1, s = n.length; ++r < s; ) if (!i.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? $m : t ? Dm : Tm)(n, t));
}
function Mm() {
  this.textContent = "";
}
function Rm(e) {
  return function() {
    this.textContent = e;
  };
}
function Lm(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function zm(e) {
  return arguments.length ? this.each(e == null ? Mm : (typeof e == "function" ? Lm : Rm)(e)) : this.node().textContent;
}
function Vm() {
  this.innerHTML = "";
}
function Om(e) {
  return function() {
    this.innerHTML = e;
  };
}
function Hm(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function Wm(e) {
  return arguments.length ? this.each(e == null ? Vm : (typeof e == "function" ? Hm : Om)(e)) : this.node().innerHTML;
}
function Fm() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function Bm() {
  return this.each(Fm);
}
function Km() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Xm() {
  return this.each(Km);
}
function qm(e) {
  var t = typeof e == "function" ? e : Zl(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Ym() {
  return null;
}
function Um(e, t) {
  var n = typeof e == "function" ? e : Zl(e), i = t == null ? Ym : typeof t == "function" ? t : ys(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), i.apply(this, arguments) || null);
  });
}
function Zm() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Gm() {
  return this.each(Zm);
}
function Jm() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Qm() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function ey(e) {
  return this.select(e ? Qm : Jm);
}
function ty(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function ny(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function iy(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", i = t.indexOf(".");
    return i >= 0 && (n = t.slice(i + 1), t = t.slice(0, i)), { type: t, name: n };
  });
}
function ry(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, i = -1, r = t.length, s; n < r; ++n)
        s = t[n], (!e.type || s.type === e.type) && s.name === e.name ? this.removeEventListener(s.type, s.listener, s.options) : t[++i] = s;
      ++i ? t.length = i : delete this.__on;
    }
  };
}
function oy(e, t, n) {
  return function() {
    var i = this.__on, r, s = ny(t);
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
function sy(e, t, n) {
  var i = iy(e + ""), r, s = i.length, a;
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
  for (c = t ? oy : ry, r = 0; r < s; ++r) this.each(c(i[r], t, n));
  return this;
}
function su(e, t, n) {
  var i = tu(e), r = i.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = i.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function ay(e, t) {
  return function() {
    return su(this, e, t);
  };
}
function cy(e, t) {
  return function() {
    return su(this, e, t.apply(this, arguments));
  };
}
function ly(e, t) {
  return this.each((typeof t == "function" ? cy : ay)(e, t));
}
function* uy() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var i = e[t], r = 0, s = i.length, a; r < s; ++r)
      (a = i[r]) && (yield a);
}
var au = [null];
function Be(e, t) {
  this._groups = e, this._parents = t;
}
function Qn() {
  return new Be([[document.documentElement]], au);
}
function dy() {
  return this;
}
Be.prototype = Qn.prototype = {
  constructor: Be,
  select: Lg,
  selectAll: Hg,
  selectChild: Kg,
  selectChildren: Ug,
  filter: Zg,
  data: nm,
  enter: Gg,
  exit: rm,
  join: om,
  merge: sm,
  selection: dy,
  order: am,
  sort: cm,
  call: um,
  nodes: dm,
  node: fm,
  size: pm,
  empty: hm,
  each: gm,
  attr: jm,
  style: km,
  property: _m,
  classed: Pm,
  text: zm,
  html: Wm,
  raise: Bm,
  lower: Xm,
  append: qm,
  insert: Um,
  remove: Gm,
  clone: ey,
  datum: ty,
  on: sy,
  dispatch: ly,
  [Symbol.iterator]: uy
};
function He(e) {
  return typeof e == "string" ? new Be([[document.querySelector(e)]], [document.documentElement]) : new Be([[e]], au);
}
function fy(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Ze(e, t) {
  if (e = fy(e), t === void 0 && (t = e.currentTarget), t) {
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
const py = { passive: !1 }, On = { capture: !0, passive: !1 };
function go(e) {
  e.stopImmediatePropagation();
}
function Yt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function cu(e) {
  var t = e.document.documentElement, n = He(e).on("dragstart.drag", Yt, On);
  "onselectstart" in t ? n.on("selectstart.drag", Yt, On) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function lu(e, t) {
  var n = e.document.documentElement, i = He(e).on("dragstart.drag", null);
  t && (i.on("click.drag", Yt, On), setTimeout(function() {
    i.on("click.drag", null);
  }, 0)), "onselectstart" in n ? i.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const Ni = (e) => () => e;
function Ro(e, {
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
Ro.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function hy(e) {
  return !e.ctrlKey && !e.button;
}
function gy() {
  return this.parentNode;
}
function my(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function yy() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function uu() {
  var e = hy, t = gy, n = my, i = yy, r = {}, s = hr("start", "drag", "end"), a = 0, c, u, l, d, f = 0;
  function p(y) {
    y.on("mousedown.drag", h).filter(i).on("touchstart.drag", x).on("touchmove.drag", w, py).on("touchend.drag touchcancel.drag", b).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function h(y, N) {
    if (!(d || !e.call(this, y, N))) {
      var j = g(this, t.call(this, y, N), y, N, "mouse");
      j && (He(y.view).on("mousemove.drag", m, On).on("mouseup.drag", v, On), cu(y.view), go(y), l = !1, c = y.clientX, u = y.clientY, j("start", y));
    }
  }
  function m(y) {
    if (Yt(y), !l) {
      var N = y.clientX - c, j = y.clientY - u;
      l = N * N + j * j > f;
    }
    r.mouse("drag", y);
  }
  function v(y) {
    He(y.view).on("mousemove.drag mouseup.drag", null), lu(y.view, l), Yt(y), r.mouse("end", y);
  }
  function x(y, N) {
    if (e.call(this, y, N)) {
      var j = y.changedTouches, S = t.call(this, y, N), I = j.length, _, M;
      for (_ = 0; _ < I; ++_)
        (M = g(this, S, y, N, j[_].identifier, j[_])) && (go(y), M("start", y, j[_]));
    }
  }
  function w(y) {
    var N = y.changedTouches, j = N.length, S, I;
    for (S = 0; S < j; ++S)
      (I = r[N[S].identifier]) && (Yt(y), I("drag", y, N[S]));
  }
  function b(y) {
    var N = y.changedTouches, j = N.length, S, I;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), S = 0; S < j; ++S)
      (I = r[N[S].identifier]) && (go(y), I("end", y, N[S]));
  }
  function g(y, N, j, S, I, _) {
    var M = s.copy(), k = Ze(_ || j, N), $, z, C;
    if ((C = n.call(y, new Ro("beforestart", {
      sourceEvent: j,
      target: p,
      identifier: I,
      active: a,
      x: k[0],
      y: k[1],
      dx: 0,
      dy: 0,
      dispatch: M
    }), S)) != null)
      return $ = C.x - k[0] || 0, z = C.y - k[1] || 0, function A(E, D, P) {
        var T = k, F;
        switch (E) {
          case "start":
            r[I] = A, F = a++;
            break;
          case "end":
            delete r[I], --a;
          // falls through
          case "drag":
            k = Ze(P || D, N), F = a;
            break;
        }
        M.call(
          E,
          y,
          new Ro(E, {
            sourceEvent: D,
            subject: C,
            target: p,
            identifier: I,
            active: F,
            x: k[0] + $,
            y: k[1] + z,
            dx: k[0] - T[0],
            dy: k[1] - T[1],
            dispatch: M
          }),
          S
        );
      };
  }
  return p.filter = function(y) {
    return arguments.length ? (e = typeof y == "function" ? y : Ni(!!y), p) : e;
  }, p.container = function(y) {
    return arguments.length ? (t = typeof y == "function" ? y : Ni(y), p) : t;
  }, p.subject = function(y) {
    return arguments.length ? (n = typeof y == "function" ? y : Ni(y), p) : n;
  }, p.touchable = function(y) {
    return arguments.length ? (i = typeof y == "function" ? y : Ni(!!y), p) : i;
  }, p.on = function() {
    var y = s.on.apply(s, arguments);
    return y === s ? p : y;
  }, p.clickDistance = function(y) {
    return arguments.length ? (f = (y = +y) * y, p) : Math.sqrt(f);
  }, p;
}
function ws(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function du(e, t) {
  var n = Object.create(e.prototype);
  for (var i in t) n[i] = t[i];
  return n;
}
function ei() {
}
var Hn = 0.7, Gi = 1 / Hn, Ut = "\\s*([+-]?\\d+)\\s*", Wn = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", it = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", xy = /^#([0-9a-f]{3,8})$/, wy = new RegExp(`^rgb\\(${Ut},${Ut},${Ut}\\)$`), vy = new RegExp(`^rgb\\(${it},${it},${it}\\)$`), by = new RegExp(`^rgba\\(${Ut},${Ut},${Ut},${Wn}\\)$`), jy = new RegExp(`^rgba\\(${it},${it},${it},${Wn}\\)$`), Ny = new RegExp(`^hsl\\(${Wn},${it},${it}\\)$`), Sy = new RegExp(`^hsla\\(${Wn},${it},${it},${Wn}\\)$`), ka = {
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
ws(ei, Dt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Ea,
  // Deprecated! Use color.formatHex.
  formatHex: Ea,
  formatHex8: Cy,
  formatHsl: ky,
  formatRgb: Ia,
  toString: Ia
});
function Ea() {
  return this.rgb().formatHex();
}
function Cy() {
  return this.rgb().formatHex8();
}
function ky() {
  return fu(this).formatHsl();
}
function Ia() {
  return this.rgb().formatRgb();
}
function Dt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = xy.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? Aa(t) : n === 3 ? new ze(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? Si(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? Si(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = wy.exec(e)) ? new ze(t[1], t[2], t[3], 1) : (t = vy.exec(e)) ? new ze(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = by.exec(e)) ? Si(t[1], t[2], t[3], t[4]) : (t = jy.exec(e)) ? Si(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = Ny.exec(e)) ? Ta(t[1], t[2] / 100, t[3] / 100, 1) : (t = Sy.exec(e)) ? Ta(t[1], t[2] / 100, t[3] / 100, t[4]) : ka.hasOwnProperty(e) ? Aa(ka[e]) : e === "transparent" ? new ze(NaN, NaN, NaN, 0) : null;
}
function Aa(e) {
  return new ze(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function Si(e, t, n, i) {
  return i <= 0 && (e = t = n = NaN), new ze(e, t, n, i);
}
function Ey(e) {
  return e instanceof ei || (e = Dt(e)), e ? (e = e.rgb(), new ze(e.r, e.g, e.b, e.opacity)) : new ze();
}
function Lo(e, t, n, i) {
  return arguments.length === 1 ? Ey(e) : new ze(e, t, n, i ?? 1);
}
function ze(e, t, n, i) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +i;
}
ws(ze, Lo, du(ei, {
  brighter(e) {
    return e = e == null ? Gi : Math.pow(Gi, e), new ze(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Hn : Math.pow(Hn, e), new ze(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new ze(kt(this.r), kt(this.g), kt(this.b), Ji(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: _a,
  // Deprecated! Use color.formatHex.
  formatHex: _a,
  formatHex8: Iy,
  formatRgb: Da,
  toString: Da
}));
function _a() {
  return `#${Ct(this.r)}${Ct(this.g)}${Ct(this.b)}`;
}
function Iy() {
  return `#${Ct(this.r)}${Ct(this.g)}${Ct(this.b)}${Ct((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function Da() {
  const e = Ji(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${kt(this.r)}, ${kt(this.g)}, ${kt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Ji(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function kt(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function Ct(e) {
  return e = kt(e), (e < 16 ? "0" : "") + e.toString(16);
}
function Ta(e, t, n, i) {
  return i <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Ge(e, t, n, i);
}
function fu(e) {
  if (e instanceof Ge) return new Ge(e.h, e.s, e.l, e.opacity);
  if (e instanceof ei || (e = Dt(e)), !e) return new Ge();
  if (e instanceof Ge) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, i = e.b / 255, r = Math.min(t, n, i), s = Math.max(t, n, i), a = NaN, c = s - r, u = (s + r) / 2;
  return c ? (t === s ? a = (n - i) / c + (n < i) * 6 : n === s ? a = (i - t) / c + 2 : a = (t - n) / c + 4, c /= u < 0.5 ? s + r : 2 - s - r, a *= 60) : c = u > 0 && u < 1 ? 0 : a, new Ge(a, c, u, e.opacity);
}
function Ay(e, t, n, i) {
  return arguments.length === 1 ? fu(e) : new Ge(e, t, n, i ?? 1);
}
function Ge(e, t, n, i) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +i;
}
ws(Ge, Ay, du(ei, {
  brighter(e) {
    return e = e == null ? Gi : Math.pow(Gi, e), new Ge(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Hn : Math.pow(Hn, e), new Ge(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, i = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - i;
    return new ze(
      mo(e >= 240 ? e - 240 : e + 120, r, i),
      mo(e, r, i),
      mo(e < 120 ? e + 240 : e - 120, r, i),
      this.opacity
    );
  },
  clamp() {
    return new Ge($a(this.h), Ci(this.s), Ci(this.l), Ji(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Ji(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${$a(this.h)}, ${Ci(this.s) * 100}%, ${Ci(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function $a(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function Ci(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function mo(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const vs = (e) => () => e;
function _y(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function Dy(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(i) {
    return Math.pow(e + i * t, n);
  };
}
function Ty(e) {
  return (e = +e) == 1 ? pu : function(t, n) {
    return n - t ? Dy(t, n, e) : vs(isNaN(t) ? n : t);
  };
}
function pu(e, t) {
  var n = t - e;
  return n ? _y(e, n) : vs(isNaN(e) ? t : e);
}
const Qi = (function e(t) {
  var n = Ty(t);
  function i(r, s) {
    var a = n((r = Lo(r)).r, (s = Lo(s)).r), c = n(r.g, s.g), u = n(r.b, s.b), l = pu(r.opacity, s.opacity);
    return function(d) {
      return r.r = a(d), r.g = c(d), r.b = u(d), r.opacity = l(d), r + "";
    };
  }
  return i.gamma = e, i;
})(1);
function $y(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, i = t.slice(), r;
  return function(s) {
    for (r = 0; r < n; ++r) i[r] = e[r] * (1 - s) + t[r] * s;
    return i;
  };
}
function Py(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function My(e, t) {
  var n = t ? t.length : 0, i = e ? Math.min(n, e.length) : 0, r = new Array(i), s = new Array(n), a;
  for (a = 0; a < i; ++a) r[a] = _n(e[a], t[a]);
  for (; a < n; ++a) s[a] = t[a];
  return function(c) {
    for (a = 0; a < i; ++a) s[a] = r[a](c);
    return s;
  };
}
function Ry(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(i) {
    return n.setTime(e * (1 - i) + t * i), n;
  };
}
function nt(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function Ly(e, t) {
  var n = {}, i = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = _n(e[r], t[r]) : i[r] = t[r];
  return function(s) {
    for (r in n) i[r] = n[r](s);
    return i;
  };
}
var zo = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, yo = new RegExp(zo.source, "g");
function zy(e) {
  return function() {
    return e;
  };
}
function Vy(e) {
  return function(t) {
    return e(t) + "";
  };
}
function hu(e, t) {
  var n = zo.lastIndex = yo.lastIndex = 0, i, r, s, a = -1, c = [], u = [];
  for (e = e + "", t = t + ""; (i = zo.exec(e)) && (r = yo.exec(t)); )
    (s = r.index) > n && (s = t.slice(n, s), c[a] ? c[a] += s : c[++a] = s), (i = i[0]) === (r = r[0]) ? c[a] ? c[a] += r : c[++a] = r : (c[++a] = null, u.push({ i: a, x: nt(i, r) })), n = yo.lastIndex;
  return n < t.length && (s = t.slice(n), c[a] ? c[a] += s : c[++a] = s), c.length < 2 ? u[0] ? Vy(u[0].x) : zy(t) : (t = u.length, function(l) {
    for (var d = 0, f; d < t; ++d) c[(f = u[d]).i] = f.x(l);
    return c.join("");
  });
}
function _n(e, t) {
  var n = typeof t, i;
  return t == null || n === "boolean" ? vs(t) : (n === "number" ? nt : n === "string" ? (i = Dt(t)) ? (t = i, Qi) : hu : t instanceof Dt ? Qi : t instanceof Date ? Ry : Py(t) ? $y : Array.isArray(t) ? My : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Ly : nt)(e, t);
}
var Pa = 180 / Math.PI, Vo = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function gu(e, t, n, i, r, s) {
  var a, c, u;
  return (a = Math.sqrt(e * e + t * t)) && (e /= a, t /= a), (u = e * n + t * i) && (n -= e * u, i -= t * u), (c = Math.sqrt(n * n + i * i)) && (n /= c, i /= c, u /= c), e * i < t * n && (e = -e, t = -t, u = -u, a = -a), {
    translateX: r,
    translateY: s,
    rotate: Math.atan2(t, e) * Pa,
    skewX: Math.atan(u) * Pa,
    scaleX: a,
    scaleY: c
  };
}
var ki;
function Oy(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Vo : gu(t.a, t.b, t.c, t.d, t.e, t.f);
}
function Hy(e) {
  return e == null || (ki || (ki = document.createElementNS("http://www.w3.org/2000/svg", "g")), ki.setAttribute("transform", e), !(e = ki.transform.baseVal.consolidate())) ? Vo : (e = e.matrix, gu(e.a, e.b, e.c, e.d, e.e, e.f));
}
function mu(e, t, n, i) {
  function r(l) {
    return l.length ? l.pop() + " " : "";
  }
  function s(l, d, f, p, h, m) {
    if (l !== f || d !== p) {
      var v = h.push("translate(", null, t, null, n);
      m.push({ i: v - 4, x: nt(l, f) }, { i: v - 2, x: nt(d, p) });
    } else (f || p) && h.push("translate(" + f + t + p + n);
  }
  function a(l, d, f, p) {
    l !== d ? (l - d > 180 ? d += 360 : d - l > 180 && (l += 360), p.push({ i: f.push(r(f) + "rotate(", null, i) - 2, x: nt(l, d) })) : d && f.push(r(f) + "rotate(" + d + i);
  }
  function c(l, d, f, p) {
    l !== d ? p.push({ i: f.push(r(f) + "skewX(", null, i) - 2, x: nt(l, d) }) : d && f.push(r(f) + "skewX(" + d + i);
  }
  function u(l, d, f, p, h, m) {
    if (l !== f || d !== p) {
      var v = h.push(r(h) + "scale(", null, ",", null, ")");
      m.push({ i: v - 4, x: nt(l, f) }, { i: v - 2, x: nt(d, p) });
    } else (f !== 1 || p !== 1) && h.push(r(h) + "scale(" + f + "," + p + ")");
  }
  return function(l, d) {
    var f = [], p = [];
    return l = e(l), d = e(d), s(l.translateX, l.translateY, d.translateX, d.translateY, f, p), a(l.rotate, d.rotate, f, p), c(l.skewX, d.skewX, f, p), u(l.scaleX, l.scaleY, d.scaleX, d.scaleY, f, p), l = d = null, function(h) {
      for (var m = -1, v = p.length, x; ++m < v; ) f[(x = p[m]).i] = x.x(h);
      return f.join("");
    };
  };
}
var Wy = mu(Oy, "px, ", "px)", "deg)"), Fy = mu(Hy, ", ", ")", ")"), By = 1e-12;
function Ma(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Ky(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Xy(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const Vi = (function e(t, n, i) {
  function r(s, a) {
    var c = s[0], u = s[1], l = s[2], d = a[0], f = a[1], p = a[2], h = d - c, m = f - u, v = h * h + m * m, x, w;
    if (v < By)
      w = Math.log(p / l) / t, x = function(S) {
        return [
          c + S * h,
          u + S * m,
          l * Math.exp(t * S * w)
        ];
      };
    else {
      var b = Math.sqrt(v), g = (p * p - l * l + i * v) / (2 * l * n * b), y = (p * p - l * l - i * v) / (2 * p * n * b), N = Math.log(Math.sqrt(g * g + 1) - g), j = Math.log(Math.sqrt(y * y + 1) - y);
      w = (j - N) / t, x = function(S) {
        var I = S * w, _ = Ma(N), M = l / (n * b) * (_ * Xy(t * I + N) - Ky(N));
        return [
          c + M * h,
          u + M * m,
          l * _ / Ma(t * I + N)
        ];
      };
    }
    return x.duration = w * 1e3 * t / Math.SQRT2, x;
  }
  return r.rho = function(s) {
    var a = Math.max(1e-3, +s), c = a * a, u = c * c;
    return e(a, c, u);
  }, r;
})(Math.SQRT2, 2, 4);
var Qt = 0, Sn = 0, xn = 0, yu = 1e3, er, Cn, tr = 0, Tt = 0, mr = 0, Fn = typeof performance == "object" && performance.now ? performance : Date, xu = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function bs() {
  return Tt || (xu(qy), Tt = Fn.now() + mr);
}
function qy() {
  Tt = 0;
}
function nr() {
  this._call = this._time = this._next = null;
}
nr.prototype = wu.prototype = {
  constructor: nr,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? bs() : +n) + (t == null ? 0 : +t), !this._next && Cn !== this && (Cn ? Cn._next = this : er = this, Cn = this), this._call = e, this._time = n, Oo();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Oo());
  }
};
function wu(e, t, n) {
  var i = new nr();
  return i.restart(e, t, n), i;
}
function Yy() {
  bs(), ++Qt;
  for (var e = er, t; e; )
    (t = Tt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --Qt;
}
function Ra() {
  Tt = (tr = Fn.now()) + mr, Qt = Sn = 0;
  try {
    Yy();
  } finally {
    Qt = 0, Zy(), Tt = 0;
  }
}
function Uy() {
  var e = Fn.now(), t = e - tr;
  t > yu && (mr -= t, tr = e);
}
function Zy() {
  for (var e, t = er, n, i = 1 / 0; t; )
    t._call ? (i > t._time && (i = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : er = n);
  Cn = e, Oo(i);
}
function Oo(e) {
  if (!Qt) {
    Sn && (Sn = clearTimeout(Sn));
    var t = e - Tt;
    t > 24 ? (e < 1 / 0 && (Sn = setTimeout(Ra, e - Fn.now() - mr)), xn && (xn = clearInterval(xn))) : (xn || (tr = Fn.now(), xn = setInterval(Uy, yu)), Qt = 1, xu(Ra));
  }
}
function La(e, t, n) {
  var i = new nr();
  return t = t == null ? 0 : +t, i.restart((r) => {
    i.stop(), e(r + t);
  }, t, n), i;
}
var Gy = hr("start", "end", "cancel", "interrupt"), Jy = [], vu = 0, za = 1, Ho = 2, Oi = 3, Va = 4, Wo = 5, Hi = 6;
function yr(e, t, n, i, r, s) {
  var a = e.__transition;
  if (!a) e.__transition = {};
  else if (n in a) return;
  Qy(e, n, {
    name: t,
    index: i,
    // For context during callback.
    group: r,
    // For context during callback.
    on: Gy,
    tween: Jy,
    time: s.time,
    delay: s.delay,
    duration: s.duration,
    ease: s.ease,
    timer: null,
    state: vu
  });
}
function js(e, t) {
  var n = et(e, t);
  if (n.state > vu) throw new Error("too late; already scheduled");
  return n;
}
function st(e, t) {
  var n = et(e, t);
  if (n.state > Oi) throw new Error("too late; already running");
  return n;
}
function et(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function Qy(e, t, n) {
  var i = e.__transition, r;
  i[t] = n, n.timer = wu(s, 0, n.time);
  function s(l) {
    n.state = za, n.timer.restart(a, n.delay, n.time), n.delay <= l && a(l - n.delay);
  }
  function a(l) {
    var d, f, p, h;
    if (n.state !== za) return u();
    for (d in i)
      if (h = i[d], h.name === n.name) {
        if (h.state === Oi) return La(a);
        h.state === Va ? (h.state = Hi, h.timer.stop(), h.on.call("interrupt", e, e.__data__, h.index, h.group), delete i[d]) : +d < t && (h.state = Hi, h.timer.stop(), h.on.call("cancel", e, e.__data__, h.index, h.group), delete i[d]);
      }
    if (La(function() {
      n.state === Oi && (n.state = Va, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = Ho, n.on.call("start", e, e.__data__, n.index, n.group), n.state === Ho) {
      for (n.state = Oi, r = new Array(p = n.tween.length), d = 0, f = -1; d < p; ++d)
        (h = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (r[++f] = h);
      r.length = f + 1;
    }
  }
  function c(l) {
    for (var d = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(u), n.state = Wo, 1), f = -1, p = r.length; ++f < p; )
      r[f].call(e, d);
    n.state === Wo && (n.on.call("end", e, e.__data__, n.index, n.group), u());
  }
  function u() {
    n.state = Hi, n.timer.stop(), delete i[t];
    for (var l in i) return;
    delete e.__transition;
  }
}
function Wi(e, t) {
  var n = e.__transition, i, r, s = !0, a;
  if (n) {
    t = t == null ? null : t + "";
    for (a in n) {
      if ((i = n[a]).name !== t) {
        s = !1;
        continue;
      }
      r = i.state > Ho && i.state < Wo, i.state = Hi, i.timer.stop(), i.on.call(r ? "interrupt" : "cancel", e, e.__data__, i.index, i.group), delete n[a];
    }
    s && delete e.__transition;
  }
}
function ex(e) {
  return this.each(function() {
    Wi(this, e);
  });
}
function tx(e, t) {
  var n, i;
  return function() {
    var r = st(this, e), s = r.tween;
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
function nx(e, t, n) {
  var i, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var s = st(this, e), a = s.tween;
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
function ix(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var i = et(this.node(), n).tween, r = 0, s = i.length, a; r < s; ++r)
      if ((a = i[r]).name === e)
        return a.value;
    return null;
  }
  return this.each((t == null ? tx : nx)(n, e, t));
}
function Ns(e, t, n) {
  var i = e._id;
  return e.each(function() {
    var r = st(this, i);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return et(r, i).value[t];
  };
}
function bu(e, t) {
  var n;
  return (typeof t == "number" ? nt : t instanceof Dt ? Qi : (n = Dt(t)) ? (t = n, Qi) : hu)(e, t);
}
function rx(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function ox(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function sx(e, t, n) {
  var i, r = n + "", s;
  return function() {
    var a = this.getAttribute(e);
    return a === r ? null : a === i ? s : s = t(i = a, n);
  };
}
function ax(e, t, n) {
  var i, r = n + "", s;
  return function() {
    var a = this.getAttributeNS(e.space, e.local);
    return a === r ? null : a === i ? s : s = t(i = a, n);
  };
}
function cx(e, t, n) {
  var i, r, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttribute(e) : (a = this.getAttribute(e), u = c + "", a === u ? null : a === i && u === r ? s : (r = u, s = t(i = a, c)));
  };
}
function lx(e, t, n) {
  var i, r, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (a = this.getAttributeNS(e.space, e.local), u = c + "", a === u ? null : a === i && u === r ? s : (r = u, s = t(i = a, c)));
  };
}
function ux(e, t) {
  var n = gr(e), i = n === "transform" ? Fy : bu;
  return this.attrTween(e, typeof t == "function" ? (n.local ? lx : cx)(n, i, Ns(this, "attr." + e, t)) : t == null ? (n.local ? ox : rx)(n) : (n.local ? ax : sx)(n, i, t));
}
function dx(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function fx(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function px(e, t) {
  var n, i;
  function r() {
    var s = t.apply(this, arguments);
    return s !== i && (n = (i = s) && fx(e, s)), n;
  }
  return r._value = t, r;
}
function hx(e, t) {
  var n, i;
  function r() {
    var s = t.apply(this, arguments);
    return s !== i && (n = (i = s) && dx(e, s)), n;
  }
  return r._value = t, r;
}
function gx(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var i = gr(e);
  return this.tween(n, (i.local ? px : hx)(i, t));
}
function mx(e, t) {
  return function() {
    js(this, e).delay = +t.apply(this, arguments);
  };
}
function yx(e, t) {
  return t = +t, function() {
    js(this, e).delay = t;
  };
}
function xx(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? mx : yx)(t, e)) : et(this.node(), t).delay;
}
function wx(e, t) {
  return function() {
    st(this, e).duration = +t.apply(this, arguments);
  };
}
function vx(e, t) {
  return t = +t, function() {
    st(this, e).duration = t;
  };
}
function bx(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? wx : vx)(t, e)) : et(this.node(), t).duration;
}
function jx(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    st(this, e).ease = t;
  };
}
function Nx(e) {
  var t = this._id;
  return arguments.length ? this.each(jx(t, e)) : et(this.node(), t).ease;
}
function Sx(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    st(this, e).ease = n;
  };
}
function Cx(e) {
  if (typeof e != "function") throw new Error();
  return this.each(Sx(this._id, e));
}
function kx(e) {
  typeof e != "function" && (e = Jl(e));
  for (var t = this._groups, n = t.length, i = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = i[r] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new pt(i, this._parents, this._name, this._id);
}
function Ex(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, i = t.length, r = n.length, s = Math.min(i, r), a = new Array(i), c = 0; c < s; ++c)
    for (var u = t[c], l = n[c], d = u.length, f = a[c] = new Array(d), p, h = 0; h < d; ++h)
      (p = u[h] || l[h]) && (f[h] = p);
  for (; c < i; ++c)
    a[c] = t[c];
  return new pt(a, this._parents, this._name, this._id);
}
function Ix(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function Ax(e, t, n) {
  var i, r, s = Ix(t) ? js : st;
  return function() {
    var a = s(this, e), c = a.on;
    c !== i && (r = (i = c).copy()).on(t, n), a.on = r;
  };
}
function _x(e, t) {
  var n = this._id;
  return arguments.length < 2 ? et(this.node(), n).on.on(e) : this.each(Ax(n, e, t));
}
function Dx(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function Tx() {
  return this.on("end.remove", Dx(this._id));
}
function $x(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = ys(e));
  for (var i = this._groups, r = i.length, s = new Array(r), a = 0; a < r; ++a)
    for (var c = i[a], u = c.length, l = s[a] = new Array(u), d, f, p = 0; p < u; ++p)
      (d = c[p]) && (f = e.call(d, d.__data__, p, c)) && ("__data__" in d && (f.__data__ = d.__data__), l[p] = f, yr(l[p], t, n, p, l, et(d, n)));
  return new pt(s, this._parents, t, n);
}
function Px(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Gl(e));
  for (var i = this._groups, r = i.length, s = [], a = [], c = 0; c < r; ++c)
    for (var u = i[c], l = u.length, d, f = 0; f < l; ++f)
      if (d = u[f]) {
        for (var p = e.call(d, d.__data__, f, u), h, m = et(d, n), v = 0, x = p.length; v < x; ++v)
          (h = p[v]) && yr(h, t, n, v, p, m);
        s.push(p), a.push(d);
      }
  return new pt(s, a, t, n);
}
var Mx = Qn.prototype.constructor;
function Rx() {
  return new Mx(this._groups, this._parents);
}
function Lx(e, t) {
  var n, i, r;
  return function() {
    var s = Jt(this, e), a = (this.style.removeProperty(e), Jt(this, e));
    return s === a ? null : s === n && a === i ? r : r = t(n = s, i = a);
  };
}
function ju(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function zx(e, t, n) {
  var i, r = n + "", s;
  return function() {
    var a = Jt(this, e);
    return a === r ? null : a === i ? s : s = t(i = a, n);
  };
}
function Vx(e, t, n) {
  var i, r, s;
  return function() {
    var a = Jt(this, e), c = n(this), u = c + "";
    return c == null && (u = c = (this.style.removeProperty(e), Jt(this, e))), a === u ? null : a === i && u === r ? s : (r = u, s = t(i = a, c));
  };
}
function Ox(e, t) {
  var n, i, r, s = "style." + t, a = "end." + s, c;
  return function() {
    var u = st(this, e), l = u.on, d = u.value[s] == null ? c || (c = ju(t)) : void 0;
    (l !== n || r !== d) && (i = (n = l).copy()).on(a, r = d), u.on = i;
  };
}
function Hx(e, t, n) {
  var i = (e += "") == "transform" ? Wy : bu;
  return t == null ? this.styleTween(e, Lx(e, i)).on("end.style." + e, ju(e)) : typeof t == "function" ? this.styleTween(e, Vx(e, i, Ns(this, "style." + e, t))).each(Ox(this._id, e)) : this.styleTween(e, zx(e, i, t), n).on("end.style." + e, null);
}
function Wx(e, t, n) {
  return function(i) {
    this.style.setProperty(e, t.call(this, i), n);
  };
}
function Fx(e, t, n) {
  var i, r;
  function s() {
    var a = t.apply(this, arguments);
    return a !== r && (i = (r = a) && Wx(e, a, n)), i;
  }
  return s._value = t, s;
}
function Bx(e, t, n) {
  var i = "style." + (e += "");
  if (arguments.length < 2) return (i = this.tween(i)) && i._value;
  if (t == null) return this.tween(i, null);
  if (typeof t != "function") throw new Error();
  return this.tween(i, Fx(e, t, n ?? ""));
}
function Kx(e) {
  return function() {
    this.textContent = e;
  };
}
function Xx(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function qx(e) {
  return this.tween("text", typeof e == "function" ? Xx(Ns(this, "text", e)) : Kx(e == null ? "" : e + ""));
}
function Yx(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Ux(e) {
  var t, n;
  function i() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Yx(r)), t;
  }
  return i._value = e, i;
}
function Zx(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, Ux(e));
}
function Gx() {
  for (var e = this._name, t = this._id, n = Nu(), i = this._groups, r = i.length, s = 0; s < r; ++s)
    for (var a = i[s], c = a.length, u, l = 0; l < c; ++l)
      if (u = a[l]) {
        var d = et(u, t);
        yr(u, e, n, l, a, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new pt(i, this._parents, e, n);
}
function Jx() {
  var e, t, n = this, i = n._id, r = n.size();
  return new Promise(function(s, a) {
    var c = { value: a }, u = { value: function() {
      --r === 0 && s();
    } };
    n.each(function() {
      var l = st(this, i), d = l.on;
      d !== e && (t = (e = d).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(u)), l.on = t;
    }), r === 0 && s();
  });
}
var Qx = 0;
function pt(e, t, n, i) {
  this._groups = e, this._parents = t, this._name = n, this._id = i;
}
function Nu() {
  return ++Qx;
}
var lt = Qn.prototype;
pt.prototype = {
  constructor: pt,
  select: $x,
  selectAll: Px,
  selectChild: lt.selectChild,
  selectChildren: lt.selectChildren,
  filter: kx,
  merge: Ex,
  selection: Rx,
  transition: Gx,
  call: lt.call,
  nodes: lt.nodes,
  node: lt.node,
  size: lt.size,
  empty: lt.empty,
  each: lt.each,
  on: _x,
  attr: ux,
  attrTween: gx,
  style: Hx,
  styleTween: Bx,
  text: qx,
  textTween: Zx,
  remove: Tx,
  tween: ix,
  delay: xx,
  duration: bx,
  ease: Nx,
  easeVarying: Cx,
  end: Jx,
  [Symbol.iterator]: lt[Symbol.iterator]
};
function ew(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var tw = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: ew
};
function nw(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function iw(e) {
  var t, n;
  e instanceof pt ? (t = e._id, e = e._name) : (t = Nu(), (n = tw).time = bs(), e = e == null ? null : e + "");
  for (var i = this._groups, r = i.length, s = 0; s < r; ++s)
    for (var a = i[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && yr(u, e, t, l, a, n || nw(u, t));
  return new pt(i, this._parents, e, t);
}
Qn.prototype.interrupt = ex;
Qn.prototype.transition = iw;
const Ei = (e) => () => e;
function rw(e, {
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
function dt(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
dt.prototype = {
  constructor: dt,
  scale: function(e) {
    return e === 1 ? this : new dt(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new dt(this.k, this.x + this.k * e, this.y + this.k * t);
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
var xr = new dt(1, 0, 0);
Su.prototype = dt.prototype;
function Su(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return xr;
  return e.__zoom;
}
function xo(e) {
  e.stopImmediatePropagation();
}
function wn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function ow(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function sw() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function Oa() {
  return this.__zoom || xr;
}
function aw(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function cw() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function lw(e, t, n) {
  var i = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], s = e.invertY(t[0][1]) - n[0][1], a = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > i ? (i + r) / 2 : Math.min(0, i) || Math.max(0, r),
    a > s ? (s + a) / 2 : Math.min(0, s) || Math.max(0, a)
  );
}
function Cu() {
  var e = ow, t = sw, n = lw, i = aw, r = cw, s = [0, 1 / 0], a = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, u = Vi, l = hr("start", "zoom", "end"), d, f, p, h = 500, m = 150, v = 0, x = 10;
  function w(C) {
    C.property("__zoom", Oa).on("wheel.zoom", I, { passive: !1 }).on("mousedown.zoom", _).on("dblclick.zoom", M).filter(r).on("touchstart.zoom", k).on("touchmove.zoom", $).on("touchend.zoom touchcancel.zoom", z).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  w.transform = function(C, A, E, D) {
    var P = C.selection ? C.selection() : C;
    P.property("__zoom", Oa), C !== P ? N(C, A, E, D) : P.interrupt().each(function() {
      j(this, arguments).event(D).start().zoom(null, typeof A == "function" ? A.apply(this, arguments) : A).end();
    });
  }, w.scaleBy = function(C, A, E, D) {
    w.scaleTo(C, function() {
      var P = this.__zoom.k, T = typeof A == "function" ? A.apply(this, arguments) : A;
      return P * T;
    }, E, D);
  }, w.scaleTo = function(C, A, E, D) {
    w.transform(C, function() {
      var P = t.apply(this, arguments), T = this.__zoom, F = E == null ? y(P) : typeof E == "function" ? E.apply(this, arguments) : E, W = T.invert(F), H = typeof A == "function" ? A.apply(this, arguments) : A;
      return n(g(b(T, H), F, W), P, a);
    }, E, D);
  }, w.translateBy = function(C, A, E, D) {
    w.transform(C, function() {
      return n(this.__zoom.translate(
        typeof A == "function" ? A.apply(this, arguments) : A,
        typeof E == "function" ? E.apply(this, arguments) : E
      ), t.apply(this, arguments), a);
    }, null, D);
  }, w.translateTo = function(C, A, E, D, P) {
    w.transform(C, function() {
      var T = t.apply(this, arguments), F = this.__zoom, W = D == null ? y(T) : typeof D == "function" ? D.apply(this, arguments) : D;
      return n(xr.translate(W[0], W[1]).scale(F.k).translate(
        typeof A == "function" ? -A.apply(this, arguments) : -A,
        typeof E == "function" ? -E.apply(this, arguments) : -E
      ), T, a);
    }, D, P);
  };
  function b(C, A) {
    return A = Math.max(s[0], Math.min(s[1], A)), A === C.k ? C : new dt(A, C.x, C.y);
  }
  function g(C, A, E) {
    var D = A[0] - E[0] * C.k, P = A[1] - E[1] * C.k;
    return D === C.x && P === C.y ? C : new dt(C.k, D, P);
  }
  function y(C) {
    return [(+C[0][0] + +C[1][0]) / 2, (+C[0][1] + +C[1][1]) / 2];
  }
  function N(C, A, E, D) {
    C.on("start.zoom", function() {
      j(this, arguments).event(D).start();
    }).on("interrupt.zoom end.zoom", function() {
      j(this, arguments).event(D).end();
    }).tween("zoom", function() {
      var P = this, T = arguments, F = j(P, T).event(D), W = t.apply(P, T), H = E == null ? y(W) : typeof E == "function" ? E.apply(P, T) : E, U = Math.max(W[1][0] - W[0][0], W[1][1] - W[0][1]), O = P.__zoom, q = typeof A == "function" ? A.apply(P, T) : A, J = u(O.invert(H).concat(U / O.k), q.invert(H).concat(U / q.k));
      return function(G) {
        if (G === 1) G = q;
        else {
          var R = J(G), X = U / R[2];
          G = new dt(X, H[0] - R[0] * X, H[1] - R[1] * X);
        }
        F.zoom(null, G);
      };
    });
  }
  function j(C, A, E) {
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
      var A = He(this.that).datum();
      l.call(
        C,
        this.that,
        new rw(C, {
          sourceEvent: this.sourceEvent,
          target: w,
          transform: this.that.__zoom,
          dispatch: l
        }),
        A
      );
    }
  };
  function I(C, ...A) {
    if (!e.apply(this, arguments)) return;
    var E = j(this, A).event(C), D = this.__zoom, P = Math.max(s[0], Math.min(s[1], D.k * Math.pow(2, i.apply(this, arguments)))), T = Ze(C);
    if (E.wheel)
      (E.mouse[0][0] !== T[0] || E.mouse[0][1] !== T[1]) && (E.mouse[1] = D.invert(E.mouse[0] = T)), clearTimeout(E.wheel);
    else {
      if (D.k === P) return;
      E.mouse = [T, D.invert(T)], Wi(this), E.start();
    }
    wn(C), E.wheel = setTimeout(F, m), E.zoom("mouse", n(g(b(D, P), E.mouse[0], E.mouse[1]), E.extent, a));
    function F() {
      E.wheel = null, E.end();
    }
  }
  function _(C, ...A) {
    if (p || !e.apply(this, arguments)) return;
    var E = C.currentTarget, D = j(this, A, !0).event(C), P = He(C.view).on("mousemove.zoom", H, !0).on("mouseup.zoom", U, !0), T = Ze(C, E), F = C.clientX, W = C.clientY;
    cu(C.view), xo(C), D.mouse = [T, this.__zoom.invert(T)], Wi(this), D.start();
    function H(O) {
      if (wn(O), !D.moved) {
        var q = O.clientX - F, J = O.clientY - W;
        D.moved = q * q + J * J > v;
      }
      D.event(O).zoom("mouse", n(g(D.that.__zoom, D.mouse[0] = Ze(O, E), D.mouse[1]), D.extent, a));
    }
    function U(O) {
      P.on("mousemove.zoom mouseup.zoom", null), lu(O.view, D.moved), wn(O), D.event(O).end();
    }
  }
  function M(C, ...A) {
    if (e.apply(this, arguments)) {
      var E = this.__zoom, D = Ze(C.changedTouches ? C.changedTouches[0] : C, this), P = E.invert(D), T = E.k * (C.shiftKey ? 0.5 : 2), F = n(g(b(E, T), D, P), t.apply(this, A), a);
      wn(C), c > 0 ? He(this).transition().duration(c).call(N, F, D, C) : He(this).call(w.transform, F, D, C);
    }
  }
  function k(C, ...A) {
    if (e.apply(this, arguments)) {
      var E = C.touches, D = E.length, P = j(this, A, C.changedTouches.length === D).event(C), T, F, W, H;
      for (xo(C), F = 0; F < D; ++F)
        W = E[F], H = Ze(W, this), H = [H, this.__zoom.invert(H), W.identifier], P.touch0 ? !P.touch1 && P.touch0[2] !== H[2] && (P.touch1 = H, P.taps = 0) : (P.touch0 = H, T = !0, P.taps = 1 + !!d);
      d && (d = clearTimeout(d)), T && (P.taps < 2 && (f = H[0], d = setTimeout(function() {
        d = null;
      }, h)), Wi(this), P.start());
    }
  }
  function $(C, ...A) {
    if (this.__zooming) {
      var E = j(this, A).event(C), D = C.changedTouches, P = D.length, T, F, W, H;
      for (wn(C), T = 0; T < P; ++T)
        F = D[T], W = Ze(F, this), E.touch0 && E.touch0[2] === F.identifier ? E.touch0[0] = W : E.touch1 && E.touch1[2] === F.identifier && (E.touch1[0] = W);
      if (F = E.that.__zoom, E.touch1) {
        var U = E.touch0[0], O = E.touch0[1], q = E.touch1[0], J = E.touch1[1], G = (G = q[0] - U[0]) * G + (G = q[1] - U[1]) * G, R = (R = J[0] - O[0]) * R + (R = J[1] - O[1]) * R;
        F = b(F, Math.sqrt(G / R)), W = [(U[0] + q[0]) / 2, (U[1] + q[1]) / 2], H = [(O[0] + J[0]) / 2, (O[1] + J[1]) / 2];
      } else if (E.touch0) W = E.touch0[0], H = E.touch0[1];
      else return;
      E.zoom("touch", n(g(F, W, H), E.extent, a));
    }
  }
  function z(C, ...A) {
    if (this.__zooming) {
      var E = j(this, A).event(C), D = C.changedTouches, P = D.length, T, F;
      for (xo(C), p && clearTimeout(p), p = setTimeout(function() {
        p = null;
      }, h), T = 0; T < P; ++T)
        F = D[T], E.touch0 && E.touch0[2] === F.identifier ? delete E.touch0 : E.touch1 && E.touch1[2] === F.identifier && delete E.touch1;
      if (E.touch1 && !E.touch0 && (E.touch0 = E.touch1, delete E.touch1), E.touch0) E.touch0[1] = this.__zoom.invert(E.touch0[0]);
      else if (E.end(), E.taps === 2 && (F = Ze(F, this), Math.hypot(f[0] - F[0], f[1] - F[1]) < x)) {
        var W = He(this).on("dblclick.zoom");
        W && W.apply(this, arguments);
      }
    }
  }
  return w.wheelDelta = function(C) {
    return arguments.length ? (i = typeof C == "function" ? C : Ei(+C), w) : i;
  }, w.filter = function(C) {
    return arguments.length ? (e = typeof C == "function" ? C : Ei(!!C), w) : e;
  }, w.touchable = function(C) {
    return arguments.length ? (r = typeof C == "function" ? C : Ei(!!C), w) : r;
  }, w.extent = function(C) {
    return arguments.length ? (t = typeof C == "function" ? C : Ei([[+C[0][0], +C[0][1]], [+C[1][0], +C[1][1]]]), w) : t;
  }, w.scaleExtent = function(C) {
    return arguments.length ? (s[0] = +C[0], s[1] = +C[1], w) : [s[0], s[1]];
  }, w.translateExtent = function(C) {
    return arguments.length ? (a[0][0] = +C[0][0], a[1][0] = +C[1][0], a[0][1] = +C[0][1], a[1][1] = +C[1][1], w) : [[a[0][0], a[0][1]], [a[1][0], a[1][1]]];
  }, w.constrain = function(C) {
    return arguments.length ? (n = C, w) : n;
  }, w.duration = function(C) {
    return arguments.length ? (c = +C, w) : c;
  }, w.interpolate = function(C) {
    return arguments.length ? (u = C, w) : u;
  }, w.on = function() {
    var C = l.on.apply(l, arguments);
    return C === l ? w : C;
  }, w.clickDistance = function(C) {
    return arguments.length ? (v = (C = +C) * C, w) : Math.sqrt(v);
  }, w.tapDistance = function(C) {
    return arguments.length ? (x = +C, w) : x;
  }, w;
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
}, Bn = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], ku = ["Enter", " ", "Escape"], Eu = {
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
var Et;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(Et || (Et = {}));
var Kn;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Kn || (Kn = {}));
const Iu = {
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
var ir;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(ir || (ir = {}));
var ae;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(ae || (ae = {}));
const Ha = {
  [ae.Left]: ae.Right,
  [ae.Right]: ae.Left,
  [ae.Top]: ae.Bottom,
  [ae.Bottom]: ae.Top
};
function Au(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const _u = (e) => "id" in e && "source" in e && "target" in e, uw = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), Ss = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), ti = (e, t = [0, 0]) => {
  const { width: n, height: i } = ht(e), r = e.origin ?? t, s = n * r[0], a = i * r[1];
  return {
    x: e.position.x - s,
    y: e.position.y - a
  };
}, dw = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((i, r) => {
    const s = typeof r == "string";
    let a = !t.nodeLookup && !s ? r : void 0;
    t.nodeLookup && (a = s ? t.nodeLookup.get(r) : Ss(r) ? r : t.nodeLookup.get(r.id));
    const c = a ? rr(a, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return wr(i, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return vr(n);
}, ni = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, i = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = wr(n, rr(r)), i = !0);
  }), i ? vr(n) : { x: 0, y: 0, width: 0, height: 0 };
}, Cs = (e, t, [n, i, r] = [0, 0, 1], s = !1, a = !1) => {
  const c = {
    ...dn(t, [n, i, r]),
    width: t.width / r,
    height: t.height / r
  }, u = [];
  for (const l of e.values()) {
    const { measured: d, selectable: f = !0, hidden: p = !1 } = l;
    if (a && !f || p)
      continue;
    const h = d.width ?? l.width ?? l.initialWidth ?? null, m = d.height ?? l.height ?? l.initialHeight ?? null, v = Xn(c, nn(l)), x = (h ?? 0) * (m ?? 0), w = s && v > 0;
    (!l.internals.handleBounds || w || v >= x || l.dragging) && u.push(l);
  }
  return u;
}, fw = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((i) => {
    n.add(i.id);
  }), t.filter((i) => n.has(i.source) || n.has(i.target));
};
function pw(e, t) {
  const n = /* @__PURE__ */ new Map(), i = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!i || i.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function hw({ nodes: e, width: t, height: n, panZoom: i, minZoom: r, maxZoom: s }, a) {
  if (e.size === 0)
    return !0;
  const c = pw(e, a), u = ni(c), l = Es(u, t, n, a?.minZoom ?? r, a?.maxZoom ?? s, a?.padding ?? 0.1);
  return await i.setViewport(l, {
    duration: a?.duration,
    ease: a?.ease,
    interpolate: a?.interpolate
  }), !0;
}
function Du({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: i = [0, 0], nodeExtent: r, onError: s }) {
  const a = n.get(e), c = a.parentId ? n.get(a.parentId) : void 0, { x: u, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, d = a.origin ?? i;
  let f = a.extent || r;
  if (a.extent === "parent" && !a.expandParent)
    if (!c)
      s?.("005", Ke.error005());
    else {
      const h = c.measured.width, m = c.measured.height;
      h && m && (f = [
        [u, l],
        [u + h, l + m]
      ]);
    }
  else c && Pt(a.extent) && (f = [
    [a.extent[0][0] + u, a.extent[0][1] + l],
    [a.extent[1][0] + u, a.extent[1][1] + l]
  ]);
  const p = Pt(f) ? $t(t, f, a.measured) : t;
  return (a.measured.width === void 0 || a.measured.height === void 0) && s?.("015", Ke.error015()), {
    position: {
      x: p.x - u + (a.measured.width ?? 0) * d[0],
      y: p.y - l + (a.measured.height ?? 0) * d[1]
    },
    positionAbsolute: p
  };
}
async function gw({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: i, onBeforeDelete: r }) {
  const s = new Set(e.map((p) => p.id)), a = [];
  for (const p of n) {
    if (p.deletable === !1)
      continue;
    const h = s.has(p.id), m = !h && p.parentId && a.find((v) => v.id === p.parentId);
    (h || m) && a.push(p);
  }
  const c = new Set(t.map((p) => p.id)), u = i.filter((p) => p.deletable !== !1), d = fw(a, u);
  for (const p of u)
    c.has(p.id) && !d.find((m) => m.id === p.id) && d.push(p);
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
const tn = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), $t = (e = { x: 0, y: 0 }, t, n) => ({
  x: tn(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: tn(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Tu(e, t, n) {
  const { width: i, height: r } = ht(n), { x: s, y: a } = n.internals.positionAbsolute;
  return $t(e, [
    [s, a],
    [s + i, a + r]
  ], t);
}
const Wa = (e, t, n) => e < t ? tn(Math.abs(e - t), 1, t) / t : e > n ? -tn(Math.abs(e - n), 1, t) / t : 0, ks = (e, t, n = 15, i = 40) => {
  const r = Wa(e.x, i, t.width - i) * n, s = Wa(e.y, i, t.height - i) * n;
  return [r, s];
}, wr = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), Fo = ({ x: e, y: t, width: n, height: i }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + i
}), vr = ({ x: e, y: t, x2: n, y2: i }) => ({
  x: e,
  y: t,
  width: n - e,
  height: i - t
}), nn = (e, t = [0, 0]) => {
  const { x: n, y: i } = Ss(e) ? e.internals.positionAbsolute : ti(e, t);
  return {
    x: n,
    y: i,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, rr = (e, t = [0, 0]) => {
  const { x: n, y: i } = Ss(e) ? e.internals.positionAbsolute : ti(e, t);
  return {
    x: n,
    y: i,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: i + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, $u = (e, t) => vr(wr(Fo(e), Fo(t))), Xn = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), i = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * i);
}, Fa = (e) => Je(e.width) && Je(e.height) && Je(e.x) && Je(e.y), Je = (e) => !isNaN(e) && isFinite(e), Pu = (e, t) => (n, i) => {
}, ii = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), dn = ({ x: e, y: t }, [n, i, r], s = !1, a = [1, 1]) => {
  const c = {
    x: (e - n) / r,
    y: (t - i) / r
  };
  return s ? ii(c, a) : c;
}, rn = ({ x: e, y: t }, [n, i, r]) => ({
  x: e * r + n,
  y: t * r + i
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
function mw(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const i = Wt(e, n), r = Wt(e, t);
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
    const i = Wt(e.top ?? e.y ?? 0, n), r = Wt(e.bottom ?? e.y ?? 0, n), s = Wt(e.left ?? e.x ?? 0, t), a = Wt(e.right ?? e.x ?? 0, t);
    return { top: i, right: a, bottom: r, left: s, x: s + a, y: i + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function yw(e, t, n, i, r, s) {
  const { x: a, y: c } = rn(e, [t, n, i]), { x: u, y: l } = rn({ x: e.x + e.width, y: e.y + e.height }, [t, n, i]), d = r - u, f = s - l;
  return {
    left: Math.floor(a),
    top: Math.floor(c),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const Es = (e, t, n, i, r, s) => {
  const a = mw(s, t, n), c = (t - a.x) / e.width, u = (n - a.y) / e.height, l = Math.min(c, u), d = tn(l, i, r), f = e.x + e.width / 2, p = e.y + e.height / 2, h = t / 2 - f * d, m = n / 2 - p * d, v = yw(e, h, m, d, t, n), x = {
    left: Math.min(v.left - a.left, 0),
    top: Math.min(v.top - a.top, 0),
    right: Math.min(v.right - a.right, 0),
    bottom: Math.min(v.bottom - a.bottom, 0)
  };
  return {
    x: h - x.left + x.right,
    y: m - x.top + x.bottom,
    zoom: d
  };
}, qn = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function Pt(e) {
  return e != null && e !== "parent";
}
function ht(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function Mu(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function Ru(e, t = { width: 0, height: 0 }, n, i, r) {
  const s = { ...e }, a = i.get(n);
  if (a) {
    const c = a.origin || r;
    s.x += a.internals.positionAbsolute.x - (t.width ?? 0) * c[0], s.y += a.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return s;
}
function Ba(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function xw() {
  let e, t;
  return { promise: new Promise((i, r) => {
    e = i, t = r;
  }), resolve: e, reject: t };
}
function ww(e) {
  return { ...Eu, ...e || {} };
}
function Dn(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: i, containerBounds: r }) {
  const { x: s, y: a } = Qe(e), c = dn({ x: s - (r?.left ?? 0), y: a - (r?.top ?? 0) }, i), { x: u, y: l } = n ? ii(c, t) : c;
  return {
    xSnapped: u,
    ySnapped: l,
    ...c
  };
}
const Is = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), Lu = (e) => e?.getRootNode?.() || window?.document, vw = ["INPUT", "SELECT", "TEXTAREA"];
function zu(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : vw.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const Vu = (e) => "clientX" in e, Qe = (e, t) => {
  const n = Vu(e), i = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: i - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, Ka = (e, t, n, i, r) => {
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
      ...Is(a)
    };
  });
};
function Ou({ sourceX: e, sourceY: t, targetX: n, targetY: i, sourceControlX: r, sourceControlY: s, targetControlX: a, targetControlY: c }) {
  const u = e * 0.125 + r * 0.375 + a * 0.375 + n * 0.125, l = t * 0.125 + s * 0.375 + c * 0.375 + i * 0.125, d = Math.abs(u - e), f = Math.abs(l - t);
  return [u, l, d, f];
}
function Ii(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function Xa({ pos: e, x1: t, y1: n, x2: i, y2: r, c: s }) {
  switch (e) {
    case ae.Left:
      return [t - Ii(t - i, s), n];
    case ae.Right:
      return [t + Ii(i - t, s), n];
    case ae.Top:
      return [t, n - Ii(n - r, s)];
    case ae.Bottom:
      return [t, n + Ii(r - n, s)];
  }
}
function Hu({ sourceX: e, sourceY: t, sourcePosition: n = ae.Bottom, targetX: i, targetY: r, targetPosition: s = ae.Top, curvature: a = 0.25 }) {
  const [c, u] = Xa({
    pos: n,
    x1: e,
    y1: t,
    x2: i,
    y2: r,
    c: a
  }), [l, d] = Xa({
    pos: s,
    x1: i,
    y1: r,
    x2: e,
    y2: t,
    c: a
  }), [f, p, h, m] = Ou({
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
    m
  ];
}
function Wu({ sourceX: e, sourceY: t, targetX: n, targetY: i }) {
  const r = Math.abs(n - e) / 2, s = n < e ? n + r : n - r, a = Math.abs(i - t) / 2, c = i < t ? i + a : i - a;
  return [s, c, r, a];
}
function bw({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: i = 0, elevateOnSelect: r = !1, zIndexMode: s = "basic" }) {
  if (s === "manual")
    return i;
  const a = r && n ? i + 1e3 : i, c = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return a + c;
}
function jw({ sourceNode: e, targetNode: t, width: n, height: i, transform: r }) {
  const s = wr(rr(e), rr(t));
  s.x === s.x2 && (s.x2 += 1), s.y === s.y2 && (s.y2 += 1);
  const a = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: i / r[2]
  };
  return Xn(a, vr(s)) > 0;
}
const Fu = ({ source: e, sourceHandle: t, target: n, targetHandle: i }) => `xy-edge__${e}${t || ""}-${n}${i || ""}`, Nw = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), Sw = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Ke.error006()), t;
  const i = n.getEdgeId || Fu;
  let r;
  return _u(e) ? r = { ...e } : r = {
    ...e,
    id: i(e)
  }, Nw(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, Cw = (e, t, n, i = { shouldReplaceId: !0 }) => {
  const { id: r, ...s } = e;
  if (!t.source || !t.target)
    return i.onError?.("006", Ke.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return i.onError?.("007", Ke.error007(r)), n;
  const c = i.getEdgeId || Fu, u = {
    ...s,
    id: i.shouldReplaceId ? c(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== r).concat(u);
};
function Bu({ sourceX: e, sourceY: t, targetX: n, targetY: i }) {
  const [r, s, a, c] = Wu({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: i
  });
  return [`M ${e},${t}L ${n},${i}`, r, s, a, c];
}
const qa = {
  [ae.Left]: { x: -1, y: 0 },
  [ae.Right]: { x: 1, y: 0 },
  [ae.Top]: { x: 0, y: -1 },
  [ae.Bottom]: { x: 0, y: 1 }
}, kw = ({ source: e, sourcePosition: t = ae.Bottom, target: n }) => t === ae.Left || t === ae.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, Ya = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Ew({ source: e, sourcePosition: t = ae.Bottom, target: n, targetPosition: i = ae.Top, center: r, offset: s, stepPosition: a }) {
  const c = qa[t], u = qa[i], l = { x: e.x + c.x * s, y: e.y + c.y * s }, d = { x: n.x + u.x * s, y: n.y + u.y * s }, f = kw({
    source: l,
    sourcePosition: t,
    target: d
  }), p = f.x !== 0 ? "x" : "y", h = f[p];
  let m = [], v, x;
  const w = { x: 0, y: 0 }, b = { x: 0, y: 0 }, [, , g, y] = Wu({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[p] * u[p] === -1) {
    p === "x" ? (v = r.x ?? l.x + (d.x - l.x) * a, x = r.y ?? (l.y + d.y) / 2) : (v = r.x ?? (l.x + d.x) / 2, x = r.y ?? l.y + (d.y - l.y) * a);
    const I = [
      { x: v, y: l.y },
      { x: v, y: d.y }
    ], _ = [
      { x: l.x, y: x },
      { x: d.x, y: x }
    ];
    c[p] === h ? m = p === "x" ? I : _ : m = p === "x" ? _ : I;
  } else {
    const I = [{ x: l.x, y: d.y }], _ = [{ x: d.x, y: l.y }];
    if (p === "x" ? m = c.x === h ? _ : I : m = c.y === h ? I : _, t === i) {
      const C = Math.abs(e[p] - n[p]);
      if (C <= s) {
        const A = Math.min(s - 1, s - C);
        c[p] === h ? w[p] = (l[p] > e[p] ? -1 : 1) * A : b[p] = (d[p] > n[p] ? -1 : 1) * A;
      }
    }
    if (t !== i) {
      const C = p === "x" ? "y" : "x", A = c[p] === u[C], E = l[C] > d[C], D = l[C] < d[C];
      (c[p] === 1 && (!A && E || A && D) || c[p] !== 1 && (!A && D || A && E)) && (m = p === "x" ? I : _);
    }
    const M = { x: l.x + w.x, y: l.y + w.y }, k = { x: d.x + b.x, y: d.y + b.y }, $ = Math.max(Math.abs(M.x - m[0].x), Math.abs(k.x - m[0].x)), z = Math.max(Math.abs(M.y - m[0].y), Math.abs(k.y - m[0].y));
    $ >= z ? (v = (M.x + k.x) / 2, x = m[0].y) : (v = m[0].x, x = (M.y + k.y) / 2);
  }
  const N = { x: l.x + w.x, y: l.y + w.y }, j = { x: d.x + b.x, y: d.y + b.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...N.x !== m[0].x || N.y !== m[0].y ? [N] : [],
    ...m,
    ...j.x !== m[m.length - 1].x || j.y !== m[m.length - 1].y ? [j] : [],
    n
  ], v, x, g, y];
}
function Iw(e, t, n, i) {
  const r = Math.min(Ya(e, t) / 2, Ya(t, n) / 2, i), { x: s, y: a } = t;
  if (e.x === s && s === n.x || e.y === a && a === n.y)
    return `L${s} ${a}`;
  if (e.y === a) {
    const l = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${s + r * l},${a}Q ${s},${a} ${s},${a + r * d}`;
  }
  const c = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${s},${a + r * u}Q ${s},${a} ${s + r * c},${a}`;
}
function or({ sourceX: e, sourceY: t, sourcePosition: n = ae.Bottom, targetX: i, targetY: r, targetPosition: s = ae.Top, borderRadius: a = 5, centerX: c, centerY: u, offset: l = 20, stepPosition: d = 0.5 }) {
  const [f, p, h, m, v] = Ew({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: i, y: r },
    targetPosition: s,
    center: { x: c, y: u },
    offset: l,
    stepPosition: d
  });
  let x = `M${f[0].x} ${f[0].y}`;
  for (let w = 1; w < f.length - 1; w++)
    x += Iw(f[w - 1], f[w], f[w + 1], a);
  return x += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [x, p, h, m, v];
}
function Ua(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function Aw(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!Ua(t) || !Ua(n))
    return null;
  const i = t.internals.handleBounds || Za(t.handles), r = n.internals.handleBounds || Za(n.handles), s = Ga(i?.source ?? [], e.sourceHandle), a = Ga(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === en.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!s || !a)
    return e.onError?.("008", Ke.error008(s ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = s?.position || ae.Bottom, u = a?.position || ae.Top, l = Mt(t, s, c), d = Mt(n, a, u);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: d.x,
    targetY: d.y,
    sourcePosition: c,
    targetPosition: u
  };
}
function Za(e) {
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
function Mt(e, t, n = ae.Left, i = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, s = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: a, height: c } = t ?? ht(e);
  if (i)
    return { x: r + a / 2, y: s + c / 2 };
  switch (t?.position ?? n) {
    case ae.Top:
      return { x: r + a / 2, y: s };
    case ae.Right:
      return { x: r + a, y: s + c / 2 };
    case ae.Bottom:
      return { x: r + a / 2, y: s + c };
    case ae.Left:
      return { x: r, y: s + c / 2 };
  }
}
function Ga(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Bo(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((i) => `${i}=${e[i]}`).join("&")}` : "";
}
function _w(e, { id: t, defaultColor: n, defaultMarkerStart: i, defaultMarkerEnd: r }) {
  const s = /* @__PURE__ */ new Set();
  return e.reduce((a, c) => ([c.markerStart || i, c.markerEnd || r].forEach((u) => {
    if (u && typeof u == "object") {
      const l = Bo(u, t);
      s.has(l) || (a.push({ id: l, color: u.color || n, ...u }), s.add(l));
    }
  }), a), []).sort((a, c) => a.id.localeCompare(c.id));
}
const Ku = 1e3, Dw = 10, As = {
  nodeOrigin: [0, 0],
  nodeExtent: Bn,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Tw = {
  ...As,
  checkEquality: !0
};
function _s(e, t) {
  const n = { ...e };
  for (const i in t)
    t[i] !== void 0 && (n[i] = t[i]);
  return n;
}
function $w(e, t, n) {
  const i = _s(As, n);
  for (const r of e.values())
    if (r.parentId)
      Ts(r, e, t, i);
    else {
      const s = ti(r, i.nodeOrigin), a = Pt(r.extent) ? r.extent : i.nodeExtent, c = $t(s, a, ht(r));
      r.internals.positionAbsolute = c;
    }
}
function Pw(e, t) {
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
function Ds(e) {
  return e === "manual";
}
function Ko(e, t, n, i = {}) {
  const r = _s(Tw, i), s = { i: 0 }, a = new Map(t), c = r?.elevateNodesOnSelect && !Ds(r.zIndexMode) ? Ku : 0;
  let u = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = a.get(d.id);
    if (r.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const p = ti(d, r.nodeOrigin), h = Pt(d.extent) ? d.extent : r.nodeExtent, m = $t(p, h, ht(d));
      f = {
        ...r.defaults,
        ...d,
        measured: {
          width: d.measured?.width,
          height: d.measured?.height
        },
        internals: {
          positionAbsolute: m,
          // if user re-initializes the node or removes `measured` for whatever reason, we reset the handleBounds so that the node gets re-measured
          handleBounds: Pw(d, f),
          z: Xu(d, c, r.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (u = !1), d.parentId && Ts(f, t, n, i, s), l ||= d.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: l };
}
function Mw(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function Ts(e, t, n, i, r) {
  const { elevateNodesOnSelect: s, nodeOrigin: a, nodeExtent: c, zIndexMode: u } = _s(As, i), l = e.parentId, d = t.get(l);
  if (!d) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Mw(e, n), r && !d.parentId && d.internals.rootParentIndex === void 0 && u === "auto" && (d.internals.rootParentIndex = ++r.i, d.internals.z = d.internals.z + r.i * Dw), r && d.internals.rootParentIndex !== void 0 && (r.i = d.internals.rootParentIndex);
  const f = s && !Ds(u) ? Ku : 0, { x: p, y: h, z: m } = Rw(e, d, a, c, f, u), { positionAbsolute: v } = e.internals, x = p !== v.x || h !== v.y;
  (x || m !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: x ? { x: p, y: h } : v,
      z: m
    }
  });
}
function Xu(e, t, n) {
  const i = Je(e.zIndex) ? e.zIndex : 0;
  return Ds(n) ? i : i + (e.selected ? t : 0);
}
function Rw(e, t, n, i, r, s) {
  const { x: a, y: c } = t.internals.positionAbsolute, u = ht(e), l = ti(e, n), d = Pt(e.extent) ? $t(l, e.extent, u) : l;
  let f = $t({ x: a + d.x, y: c + d.y }, i, u);
  e.extent === "parent" && (f = Tu(f, u, t));
  const p = Xu(e, r, s), h = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: h >= p ? h + 1 : p
  };
}
function $s(e, t, n, i = [0, 0]) {
  const r = [], s = /* @__PURE__ */ new Map();
  for (const a of e) {
    const c = t.get(a.parentId);
    if (!c)
      continue;
    const u = s.get(a.parentId)?.expandedRect ?? nn(c), l = $u(u, a.rect);
    s.set(a.parentId, { expandedRect: l, parent: c });
  }
  return s.size > 0 && s.forEach(({ expandedRect: a, parent: c }, u) => {
    const l = c.internals.positionAbsolute, d = ht(c), f = c.origin ?? i, p = a.x < l.x ? Math.round(Math.abs(l.x - a.x)) : 0, h = a.y < l.y ? Math.round(Math.abs(l.y - a.y)) : 0, m = Math.max(d.width, Math.round(a.width)), v = Math.max(d.height, Math.round(a.height)), x = (m - d.width) * f[0], w = (v - d.height) * f[1];
    (p > 0 || h > 0 || x || w) && (r.push({
      id: u,
      type: "position",
      position: {
        x: c.position.x - p + x,
        y: c.position.y - h + w
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
        width: m + (p ? f[0] * p - x : 0),
        height: v + (h ? f[1] * h - w : 0)
      }
    });
  }), r;
}
function Lw(e, t, n, i, r, s, a) {
  const c = i?.querySelector(".xyflow__viewport");
  let u = !1;
  if (!c)
    return { changes: [], updatedInternals: u };
  const l = [], d = window.getComputedStyle(c), { m22: f } = new window.DOMMatrixReadOnly(d.transform), p = [];
  for (const h of e.values()) {
    const m = t.get(h.id);
    if (!m)
      continue;
    if (m.hidden) {
      t.set(m.id, {
        ...m,
        internals: {
          ...m.internals,
          handleBounds: void 0
        }
      }), u = !0;
      continue;
    }
    const v = Is(h.nodeElement), x = m.measured.width !== v.width || m.measured.height !== v.height;
    if (!!(v.width && v.height && (x || !m.internals.handleBounds || h.force))) {
      const b = h.nodeElement.getBoundingClientRect(), g = Pt(m.extent) ? m.extent : s;
      let { positionAbsolute: y } = m.internals;
      m.parentId && m.extent === "parent" ? y = Tu(y, v, t.get(m.parentId)) : g && (y = $t(y, g, v));
      const N = {
        ...m,
        measured: v,
        internals: {
          ...m.internals,
          positionAbsolute: y,
          handleBounds: {
            source: Ka("source", h.nodeElement, b, f, m.id),
            target: Ka("target", h.nodeElement, b, f, m.id)
          }
        }
      };
      t.set(m.id, N), m.parentId && Ts(N, t, n, { nodeOrigin: r, zIndexMode: a }), u = !0, x && (l.push({
        id: m.id,
        type: "dimensions",
        dimensions: v
      }), m.expandParent && m.parentId && p.push({
        id: m.id,
        parentId: m.parentId,
        rect: nn(N, r)
      }));
    }
  }
  if (p.length > 0) {
    const h = $s(p, t, n, r);
    l.push(...h);
  }
  return { changes: l, updatedInternals: u };
}
async function zw({ delta: e, panZoom: t, transform: n, translateExtent: i, width: r, height: s }) {
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
function Ja(e, t, n, i, r, s) {
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
function qu(e, t, n) {
  e.clear(), t.clear();
  for (const i of n) {
    const { source: r, target: s, sourceHandle: a = null, targetHandle: c = null } = i, u = { edgeId: i.id, source: r, target: s, sourceHandle: a, targetHandle: c }, l = `${r}-${a}--${s}-${c}`, d = `${s}-${c}--${r}-${a}`;
    Ja("source", u, d, e, r, a), Ja("target", u, l, e, s, c), t.set(i.id, i);
  }
}
function Yu(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : Yu(n, t) : !1;
}
function Qa(e, t, n) {
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
function Vw(e, t, n, i) {
  const r = /* @__PURE__ */ new Map();
  for (const [s, a] of e)
    if ((a.selected || a.id === i) && (!a.parentId || !Yu(a, e)) && (a.draggable || t && typeof a.draggable > "u")) {
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
function wo({ nodeId: e, dragItems: t, nodeLookup: n, dragging: i = !0 }) {
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
function Ow({ dragItems: e, snapGrid: t, x: n, y: i }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const s = {
    x: n - r.distance.x,
    y: i - r.distance.y
  }, a = ii(s, t);
  return {
    x: a.x - s.x,
    y: a.y - s.y
  };
}
function Hw({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: i, onDragStop: r }) {
  let s = { x: null, y: null }, a = 0, c = /* @__PURE__ */ new Map(), u = !1, l = { x: 0, y: 0 }, d = null, f = !1, p = null, h = !1, m = !1, v = null;
  function x({ noDragClassName: b, handleSelector: g, domNode: y, isSelectable: N, nodeId: j, nodeClickDistance: S = 0 }) {
    p = He(y);
    function I({ x: $, y: z }) {
      const { nodeLookup: C, nodeExtent: A, snapGrid: E, snapToGrid: D, nodeOrigin: P, onNodeDrag: T, onSelectionDrag: F, onError: W, updateNodePositions: H } = t();
      s = { x: $, y: z };
      let U = !1;
      const O = c.size > 1, q = O && A ? Fo(ni(c)) : null, J = O && D ? Ow({
        dragItems: c,
        snapGrid: E,
        x: $,
        y: z
      }) : null;
      for (const [G, R] of c) {
        if (!C.has(G))
          continue;
        let X = { x: $ - R.distance.x, y: z - R.distance.y };
        D && (X = J ? {
          x: Math.round(X.x + J.x),
          y: Math.round(X.y + J.y)
        } : ii(X, E));
        let de = null;
        if (O && A && !R.extent && q) {
          const { positionAbsolute: ie } = R.internals, fe = ie.x - q.x + A[0][0], V = ie.x + R.measured.width - q.x2 + A[1][0], ne = ie.y - q.y + A[0][1], ge = ie.y + R.measured.height - q.y2 + A[1][1];
          de = [
            [fe, ne],
            [V, ge]
          ];
        }
        const { position: le, positionAbsolute: te } = Du({
          nodeId: G,
          nextPosition: X,
          nodeLookup: C,
          nodeExtent: de || A,
          nodeOrigin: P,
          onError: W
        });
        U = U || R.position.x !== le.x || R.position.y !== le.y, R.position = le, R.internals.positionAbsolute = te;
      }
      if (m = m || U, !!U && (H(c, !0), v && (i || T || !j && F))) {
        const [G, R] = wo({
          nodeId: j,
          dragItems: c,
          nodeLookup: C
        });
        i?.(v, c, G, R), T?.(v, G, R), j || F?.(v, R);
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
      const [E, D] = ks(l, d, C);
      (E !== 0 || D !== 0) && (s.x = (s.x ?? 0) - E / $[2], s.y = (s.y ?? 0) - D / $[2], await z({ x: E, y: D }) && I(s)), a = requestAnimationFrame(_);
    }
    function M($) {
      const { nodeLookup: z, multiSelectionActive: C, nodesDraggable: A, transform: E, snapGrid: D, snapToGrid: P, selectNodesOnDrag: T, onNodeDragStart: F, onSelectionDragStart: W, unselectNodesAndEdges: H } = t();
      f = !0, (!T || !N) && !C && j && (z.get(j)?.selected || H()), N && T && j && e?.(j);
      const U = Dn($.sourceEvent, { transform: E, snapGrid: D, snapToGrid: P, containerBounds: d });
      if (s = U, c = Vw(z, A, U, j), c.size > 0 && (n || F || !j && W)) {
        const [O, q] = wo({
          nodeId: j,
          dragItems: c,
          nodeLookup: z
        });
        n?.($.sourceEvent, c, O, q), F?.($.sourceEvent, O, q), j || W?.($.sourceEvent, q);
      }
    }
    const k = uu().clickDistance(S).on("start", ($) => {
      const { domNode: z, nodeDragThreshold: C, transform: A, snapGrid: E, snapToGrid: D } = t();
      d = z?.getBoundingClientRect() || null, h = !1, m = !1, v = $.sourceEvent, C === 0 && M($), s = Dn($.sourceEvent, { transform: A, snapGrid: E, snapToGrid: D, containerBounds: d }), l = Qe($.sourceEvent, d);
    }).on("drag", ($) => {
      const { autoPanOnNodeDrag: z, transform: C, snapGrid: A, snapToGrid: E, nodeDragThreshold: D, nodeLookup: P } = t(), T = Dn($.sourceEvent, { transform: C, snapGrid: A, snapToGrid: E, containerBounds: d });
      if (v = $.sourceEvent, ($.sourceEvent.type === "touchmove" && $.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      j && !P.has(j)) && (h = !0), !h) {
        if (!u && z && f && (u = !0, _()), !f) {
          const F = Qe($.sourceEvent, d), W = F.x - l.x, H = F.y - l.y;
          Math.sqrt(W * W + H * H) > D && M($);
        }
        (s.x !== T.xSnapped || s.y !== T.ySnapped) && c && f && (l = Qe($.sourceEvent, d), I(T));
      }
    }).on("end", ($) => {
      if (!f || h) {
        h && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (u = !1, f = !1, cancelAnimationFrame(a), c.size > 0) {
        const { nodeLookup: z, updateNodePositions: C, onNodeDragStop: A, onSelectionDragStop: E } = t();
        if (m && (C(c, !1), m = !1), r || A || !j && E) {
          const [D, P] = wo({
            nodeId: j,
            dragItems: c,
            nodeLookup: z,
            dragging: !1
          });
          r?.($.sourceEvent, c, D, P), A?.($.sourceEvent, D, P), j || E?.($.sourceEvent, P);
        }
      }
    }).filter(($) => {
      const z = $.target;
      return !$.button && (!b || !Qa(z, `.${b}`, y)) && (!g || Qa(z, g, y));
    });
    p.call(k);
  }
  function w() {
    p?.on(".drag", null);
  }
  return {
    update: x,
    destroy: w
  };
}
function Ww(e, t, n) {
  const i = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const s of t.values())
    Xn(r, nn(s)) > 0 && i.push(s);
  return i;
}
const Fw = 250;
function Bw(e, t, n, i) {
  let r = [], s = 1 / 0;
  const a = Ww(e, n, t + Fw);
  for (const c of a) {
    const u = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of u) {
      if (i.nodeId === l.nodeId && i.type === l.type && i.id === l.id)
        continue;
      const { x: d, y: f } = Mt(c, l, l.position, !0), p = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(f - e.y, 2));
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
function Uu(e, t, n, i, r, s = !1) {
  const a = i.get(e);
  if (!a)
    return null;
  const c = r === "strict" ? a.internals.handleBounds?.[t] : [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []], u = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return u && s ? { ...u, ...Mt(a, u, u.position, !0) } : u;
}
function Zu(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Kw(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const Gu = () => !0;
function Xw(e, { connectionMode: t, connectionRadius: n, handleId: i, nodeId: r, edgeUpdaterType: s, isTarget: a, domNode: c, nodeLookup: u, lib: l, autoPanOnConnect: d, flowId: f, panBy: p, cancelConnection: h, onConnectStart: m, onConnect: v, onConnectEnd: x, isValidConnection: w = Gu, onReconnectEnd: b, updateConnection: g, getTransform: y, getFromHandle: N, autoPanSpeed: j, dragThreshold: S = 1, handleDomNode: I }) {
  const _ = Lu(e.target);
  let M = 0, k;
  const { x: $, y: z } = Qe(e), C = Zu(s, I), A = c?.getBoundingClientRect();
  let E = !1;
  if (!A || !C)
    return;
  const D = Uu(r, C, i, u, t);
  if (!D)
    return;
  let P = Qe(e, A), T = !1, F = null, W = !1, H = null;
  function U() {
    if (!d || !A)
      return;
    const [le, te] = ks(P, A, j);
    p({ x: le, y: te }), M = requestAnimationFrame(U);
  }
  const O = {
    ...D,
    nodeId: r,
    type: C,
    position: D.position
  }, q = u.get(r);
  let G = {
    inProgress: !0,
    isValid: null,
    from: Mt(q, O, ae.Left, !0),
    fromHandle: O,
    fromPosition: O.position,
    fromNode: q,
    to: P,
    toHandle: null,
    toPosition: Ha[O.position],
    toNode: null,
    pointer: P
  };
  function R() {
    E = !0, g(G), m?.(e, { nodeId: r, handleId: i, handleType: C });
  }
  S === 0 && R();
  function X(le) {
    if (!E) {
      const { x: ge, y: ye } = Qe(le), Ie = ge - $, _e = ye - z;
      if (!(Ie * Ie + _e * _e > S * S))
        return;
      R();
    }
    if (!N() || !O) {
      de(le);
      return;
    }
    const te = y();
    P = Qe(le, A), k = Bw(dn(P, te, !1, [1, 1]), n, u, O), T || (U(), T = !0);
    const ie = Ju(le, {
      handle: k,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: i,
      fromType: a ? "target" : "source",
      isValidConnection: w,
      doc: _,
      lib: l,
      flowId: f,
      nodeLookup: u
    });
    H = ie.handleDomNode, F = ie.connection, W = Kw(!!k, ie.isValid);
    const fe = u.get(r), V = fe ? Mt(fe, O, ae.Left, !0) : G.from, ne = {
      ...G,
      from: V,
      isValid: W,
      to: ie.toHandle && W ? rn({ x: ie.toHandle.x, y: ie.toHandle.y }, te) : P,
      toHandle: ie.toHandle,
      toPosition: W && ie.toHandle ? ie.toHandle.position : Ha[O.position],
      toNode: ie.toHandle ? u.get(ie.toHandle.nodeId) : null,
      pointer: P
    };
    g(ne), G = ne;
  }
  function de(le) {
    if (!("touches" in le && le.touches.length > 0)) {
      if (E) {
        (k || H) && F && W && v?.(F);
        const { inProgress: te, ...ie } = G, fe = {
          ...ie,
          toPosition: G.toHandle ? G.toPosition : null
        };
        x?.(le, fe), s && b?.(le, fe);
      }
      h(), cancelAnimationFrame(M), T = !1, W = !1, F = null, H = null, _.removeEventListener("mousemove", X), _.removeEventListener("mouseup", de), _.removeEventListener("touchmove", X), _.removeEventListener("touchend", de);
    }
  }
  _.addEventListener("mousemove", X), _.addEventListener("mouseup", de), _.addEventListener("touchmove", X), _.addEventListener("touchend", de);
}
function Ju(e, { handle: t, connectionMode: n, fromNodeId: i, fromHandleId: r, fromType: s, doc: a, lib: c, flowId: u, isValidConnection: l = Gu, nodeLookup: d }) {
  const f = s === "target", p = t ? a.querySelector(`.${c}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: h, y: m } = Qe(e), v = a.elementFromPoint(h, m), x = v?.classList.contains(`${c}-flow__handle`) ? v : p, w = {
    handleDomNode: x,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (x) {
    const b = Zu(void 0, x), g = x.getAttribute("data-nodeid"), y = x.getAttribute("data-handleid"), N = x.classList.contains("connectable"), j = x.classList.contains("connectableend");
    if (!g || !b)
      return w;
    const S = {
      source: f ? g : i,
      sourceHandle: f ? y : r,
      target: f ? i : g,
      targetHandle: f ? r : y
    };
    w.connection = S;
    const _ = N && j && (n === en.Strict ? f && b === "source" || !f && b === "target" : g !== i || y !== r);
    w.isValid = _ && l(S), w.toHandle = Uu(g, b, y, d, n, !0);
  }
  return w;
}
const Xo = {
  onPointerDown: Xw,
  isValid: Ju
};
function qw({ domNode: e, panZoom: t, getTransform: n, getViewScale: i }) {
  const r = He(e);
  function s({ translateExtent: c, width: u, height: l, zoomStep: d = 1, pannable: f = !0, zoomable: p = !0, inversePan: h = !1 }) {
    const m = (g) => {
      if (g.sourceEvent.type !== "wheel" || !t)
        return;
      const y = n(), N = g.sourceEvent.ctrlKey && qn() ? 10 : 1, j = -g.sourceEvent.deltaY * (g.sourceEvent.deltaMode === 1 ? 0.05 : g.sourceEvent.deltaMode ? 1 : 2e-3) * d, S = y[2] * Math.pow(2, j * N);
      t.scaleTo(S);
    };
    let v = [0, 0];
    const x = (g) => {
      (g.sourceEvent.type === "mousedown" || g.sourceEvent.type === "touchstart") && (v = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ]);
    }, w = (g) => {
      const y = n();
      if (g.sourceEvent.type !== "mousemove" && g.sourceEvent.type !== "touchmove" || !t)
        return;
      const N = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ], j = [N[0] - v[0], N[1] - v[1]];
      v = N;
      const S = i() * Math.max(y[2], Math.log(y[2])) * (h ? -1 : 1), I = {
        x: y[0] - j[0] * S,
        y: y[1] - j[1] * S
      }, _ = [
        [0, 0],
        [u, l]
      ];
      t.setViewportConstrained({
        x: I.x,
        y: I.y,
        zoom: y[2]
      }, _, c);
    }, b = Cu().on("start", x).on("zoom", f ? w : null).on("zoom.wheel", p ? m : null);
    r.call(b, {});
  }
  function a() {
    r.on("zoom", null);
  }
  return {
    update: s,
    destroy: a,
    pointer: Ze
  };
}
const br = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), vo = ({ x: e, y: t, zoom: n }) => xr.translate(e, t).scale(n), Bt = (e, t) => e.target.closest(`.${t}`), Qu = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Yw = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, bo = (e, t = 0, n = Yw, i = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || i(), r ? e.transition().duration(t).ease(n).on("end", i) : e;
}, ed = (e) => {
  const t = e.ctrlKey && qn() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Uw({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: i, panOnScrollMode: r, panOnScrollSpeed: s, zoomOnPinch: a, onPanZoomStart: c, onPanZoom: u, onPanZoomEnd: l }) {
  return (d) => {
    if (Bt(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && a) {
      const x = Ze(d), w = ed(d), b = f * Math.pow(2, w);
      i.scaleTo(n, b, x, d);
      return;
    }
    const p = d.deltaMode === 1 ? 20 : 1;
    let h = r === Et.Vertical ? 0 : d.deltaX * p, m = r === Et.Horizontal ? 0 : d.deltaY * p;
    !qn() && d.shiftKey && r !== Et.Vertical && (h = d.deltaY * p, m = 0), i.translateBy(
      n,
      -(h / f) * s,
      -(m / f) * s,
      // @ts-ignore
      { internal: !0 }
    );
    const v = br(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(d, v), e.panScrollTimeout = setTimeout(() => {
      l?.(d, v), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(d, v));
  };
}
function Zw({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(i, r) {
    const s = i.type === "wheel", a = !t && s && !i.ctrlKey, c = Bt(i, e);
    if (i.ctrlKey && s && c && i.preventDefault(), a || c)
      return null;
    i.preventDefault(), n.call(this, i, r);
  };
}
function Gw({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (i) => {
    if (i.sourceEvent?.internal)
      return;
    const r = br(i.transform);
    e.mouseButton = i.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, i.sourceEvent?.type === "mousedown" && t(!0), n && n?.(i.sourceEvent, r);
  };
}
function Jw({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: i, onPanZoom: r }) {
  return (s) => {
    e.usedRightMouseButton = !!(n && Qu(t, e.mouseButton ?? 0)), s.sourceEvent?.sync || i([s.transform.x, s.transform.y, s.transform.k]), r && !s.sourceEvent?.internal && r?.(s.sourceEvent, br(s.transform));
  };
}
function Qw({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: i, onPanZoomEnd: r, onPaneContextMenu: s }) {
  return (a) => {
    if (!a.sourceEvent?.internal && (e.isZoomingOrPanning = !1, s && Qu(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && a.sourceEvent && s(a.sourceEvent), e.usedRightMouseButton = !1, i(!1), r)) {
      const c = br(a.transform);
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
function ev({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: i, panOnScroll: r, zoomOnDoubleClick: s, userSelectionActive: a, noWheelClassName: c, noPanClassName: u, lib: l, connectionInProgress: d }) {
  return (f) => {
    const p = e || t, h = n && f.ctrlKey, m = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (Bt(f, `${l}-flow__node`) || Bt(f, `${l}-flow__edge`)))
      return !0;
    if (!i && !p && !r && !s && !n || a || d && !m || Bt(f, c) && m || Bt(f, u) && (!m || r && m && !e) || !n && f.ctrlKey && m)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!p && !r && !h && m || !i && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(i) && !i.includes(f.button) && f.type === "mousedown")
      return !1;
    const v = Array.isArray(i) && i.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || m) && v;
  };
}
function tv({ domNode: e, minZoom: t, maxZoom: n, translateExtent: i, viewport: r, onPanZoom: s, onPanZoomStart: a, onPanZoomEnd: c, onDraggingChange: u }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = Cu().scaleExtent([t, n]).translateExtent(i), p = He(e).call(f);
  b({
    x: r.x,
    y: r.y,
    zoom: tn(r.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], i);
  const h = p.on("wheel.zoom"), m = p.on("dblclick.zoom");
  f.wheelDelta(ed);
  async function v(k, $) {
    return p ? new Promise((z) => {
      f?.interpolate($?.interpolate === "linear" ? _n : Vi).transform(bo(p, $?.duration, $?.ease, () => z(!0)), k);
    }) : !1;
  }
  function x({ noWheelClassName: k, noPanClassName: $, onPaneContextMenu: z, userSelectionActive: C, panOnScroll: A, panOnDrag: E, panOnScrollMode: D, panOnScrollSpeed: P, preventScrolling: T, zoomOnPinch: F, zoomOnScroll: W, zoomOnDoubleClick: H, zoomActivationKeyPressed: U, lib: O, onTransformChange: q, connectionInProgress: J, paneClickDistance: G, selectionOnDrag: R }) {
    C && !l.isZoomingOrPanning && w();
    const X = A && !U && !C;
    f.clickDistance(R ? 1 / 0 : !Je(G) || G < 0 ? 0 : G);
    const de = X ? Uw({
      zoomPanValues: l,
      noWheelClassName: k,
      d3Selection: p,
      d3Zoom: f,
      panOnScrollMode: D,
      panOnScrollSpeed: P,
      zoomOnPinch: F,
      onPanZoomStart: a,
      onPanZoom: s,
      onPanZoomEnd: c
    }) : Zw({
      noWheelClassName: k,
      preventScrolling: T,
      d3ZoomHandler: h
    });
    p.on("wheel.zoom", de, { passive: !1 });
    const le = Gw({
      zoomPanValues: l,
      onDraggingChange: u,
      onPanZoomStart: a
    });
    f.on("start", le);
    const te = Jw({
      zoomPanValues: l,
      panOnDrag: E,
      onPaneContextMenu: !!z,
      onPanZoom: s,
      onTransformChange: q
    });
    f.on("zoom", te);
    const ie = Qw({
      zoomPanValues: l,
      panOnDrag: E,
      panOnScroll: A,
      onPaneContextMenu: z,
      onPanZoomEnd: c,
      onDraggingChange: u
    });
    f.on("end", ie);
    const fe = ev({
      zoomActivationKeyPressed: U,
      panOnDrag: E,
      zoomOnScroll: W,
      panOnScroll: A,
      zoomOnDoubleClick: H,
      zoomOnPinch: F,
      userSelectionActive: C,
      noPanClassName: $,
      noWheelClassName: k,
      lib: O,
      connectionInProgress: J
    });
    f.filter(fe), H ? p.on("dblclick.zoom", m) : p.on("dblclick.zoom", null);
  }
  function w() {
    f.on("zoom", null);
  }
  async function b(k, $, z) {
    const C = vo(k), A = f?.constrain()(C, $, z);
    return A && await v(A), A;
  }
  async function g(k, $) {
    const z = vo(k);
    return await v(z, $), z;
  }
  function y(k) {
    if (p) {
      const $ = vo(k), z = p.property("__zoom");
      (z.k !== k.zoom || z.x !== k.x || z.y !== k.y) && f?.transform(p, $, null, { sync: !0 });
    }
  }
  function N() {
    const k = p ? Su(p.node()) : { x: 0, y: 0, k: 1 };
    return { x: k.x, y: k.y, zoom: k.k };
  }
  async function j(k, $) {
    return p ? new Promise((z) => {
      f?.interpolate($?.interpolate === "linear" ? _n : Vi).scaleTo(bo(p, $?.duration, $?.ease, () => z(!0)), k);
    }) : !1;
  }
  async function S(k, $) {
    return p ? new Promise((z) => {
      f?.interpolate($?.interpolate === "linear" ? _n : Vi).scaleBy(bo(p, $?.duration, $?.ease, () => z(!0)), k);
    }) : !1;
  }
  function I(k) {
    f?.scaleExtent(k);
  }
  function _(k) {
    f?.translateExtent(k);
  }
  function M(k) {
    const $ = !Je(k) || k < 0 ? 0 : k;
    f?.clickDistance($);
  }
  return {
    update: x,
    destroy: w,
    setViewport: g,
    setViewportConstrained: b,
    getViewport: N,
    scaleTo: j,
    scaleBy: S,
    setScaleExtent: I,
    setTranslateExtent: _,
    syncViewport: y,
    setClickDistance: M
  };
}
var on;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(on || (on = {}));
function nv({ width: e, prevWidth: t, height: n, prevHeight: i, affectsX: r, affectsY: s }) {
  const a = e - t, c = n - i, u = [a > 0 ? 1 : a < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return a && r && (u[0] = u[0] * -1), c && s && (u[1] = u[1] * -1), u;
}
function ec(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), i = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: i,
    affectsY: r
  };
}
function gt(e, t) {
  return Math.max(0, t - e);
}
function mt(e, t) {
  return Math.max(0, e - t);
}
function Ai(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function tc(e, t) {
  return e ? !t : t;
}
function iv(e, t, n, i, r, s, a, c) {
  let { affectsX: u, affectsY: l } = t;
  const { isHorizontal: d, isVertical: f } = t, p = d && f, { xSnapped: h, ySnapped: m } = n, { minWidth: v, maxWidth: x, minHeight: w, maxHeight: b } = i, { x: g, y, width: N, height: j, aspectRatio: S } = e;
  let I = Math.floor(d ? h - e.pointerX : 0), _ = Math.floor(f ? m - e.pointerY : 0);
  const M = N + (u ? -I : I), k = j + (l ? -_ : _), $ = -s[0] * N, z = -s[1] * j;
  let C = Ai(M, v, x), A = Ai(k, w, b);
  if (a) {
    let P = 0, T = 0;
    u && I < 0 ? P = gt(g + I + $, a[0][0]) : !u && I > 0 && (P = mt(g + M + $, a[1][0])), l && _ < 0 ? T = gt(y + _ + z, a[0][1]) : !l && _ > 0 && (T = mt(y + k + z, a[1][1])), C = Math.max(C, P), A = Math.max(A, T);
  }
  if (c) {
    let P = 0, T = 0;
    u && I > 0 ? P = mt(g + I, c[0][0]) : !u && I < 0 && (P = gt(g + M, c[1][0])), l && _ > 0 ? T = mt(y + _, c[0][1]) : !l && _ < 0 && (T = gt(y + k, c[1][1])), C = Math.max(C, P), A = Math.max(A, T);
  }
  if (r) {
    if (d) {
      const P = Ai(M / S, w, b) * S;
      if (C = Math.max(C, P), a) {
        let T = 0;
        !u && !l || u && !l && p ? T = mt(y + z + M / S, a[1][1]) * S : T = gt(y + z + (u ? I : -I) / S, a[0][1]) * S, C = Math.max(C, T);
      }
      if (c) {
        let T = 0;
        !u && !l || u && !l && p ? T = gt(y + M / S, c[1][1]) * S : T = mt(y + (u ? I : -I) / S, c[0][1]) * S, C = Math.max(C, T);
      }
    }
    if (f) {
      const P = Ai(k * S, v, x) / S;
      if (A = Math.max(A, P), a) {
        let T = 0;
        !u && !l || l && !u && p ? T = mt(g + k * S + $, a[1][0]) / S : T = gt(g + (l ? _ : -_) * S + $, a[0][0]) / S, A = Math.max(A, T);
      }
      if (c) {
        let T = 0;
        !u && !l || l && !u && p ? T = gt(g + k * S, c[1][0]) / S : T = mt(g + (l ? _ : -_) * S, c[0][0]) / S, A = Math.max(A, T);
      }
    }
  }
  _ = _ + (_ < 0 ? A : -A), I = I + (I < 0 ? C : -C), r && (p ? M > k * S ? _ = (tc(u, l) ? -I : I) / S : I = (tc(u, l) ? -_ : _) * S : d ? (_ = I / S, l = u) : (I = _ * S, u = l));
  const E = u ? g + I : g, D = l ? y + _ : y;
  return {
    width: N + (u ? -I : I),
    height: j + (l ? -_ : _),
    x: s[0] * I * (u ? -1 : 1) + E,
    y: s[1] * _ * (l ? -1 : 1) + D
  };
}
const td = { width: 0, height: 0, x: 0, y: 0 }, rv = {
  ...td,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function ov(e, t, n) {
  const i = t.position.x + e.position.x, r = t.position.y + e.position.y, s = e.measured.width ?? 0, a = e.measured.height ?? 0, c = n[0] * s, u = n[1] * a;
  return [
    [i - c, r - u],
    [i + s - c, r + a - u]
  ];
}
function sv({ domNode: e, nodeId: t, getStoreItems: n, onChange: i, onEnd: r }) {
  const s = He(e);
  let a = {
    controlDirection: ec("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: l, boundaries: d, keepAspectRatio: f, resizeDirection: p, onResizeStart: h, onResize: m, onResizeEnd: v, shouldResize: x }) {
    let w = { ...td }, b = { ...rv };
    a = {
      boundaries: d,
      resizeDirection: p,
      keepAspectRatio: f,
      controlDirection: ec(l)
    };
    let g, y = null, N = [], j, S, I, _ = !1;
    const M = uu().on("start", (k) => {
      const { nodeLookup: $, transform: z, snapGrid: C, snapToGrid: A, nodeOrigin: E, paneDomNode: D } = n();
      if (g = $.get(t), !g)
        return;
      y = D?.getBoundingClientRect() ?? null;
      const { xSnapped: P, ySnapped: T } = Dn(k.sourceEvent, {
        transform: z,
        snapGrid: C,
        snapToGrid: A,
        containerBounds: y
      });
      w = {
        width: g.measured.width ?? 0,
        height: g.measured.height ?? 0,
        x: g.position.x ?? 0,
        y: g.position.y ?? 0
      }, b = {
        ...w,
        pointerX: P,
        pointerY: T,
        aspectRatio: w.width / w.height
      }, j = void 0, S = Pt(g.extent) ? g.extent : void 0, g.parentId && (g.extent === "parent" || g.expandParent) && (j = $.get(g.parentId)), j && g.extent === "parent" && (S = [
        [0, 0],
        [j.measured.width, j.measured.height]
      ]), N = [], I = void 0;
      for (const [F, W] of $)
        if (W.parentId === t && (N.push({
          id: F,
          position: { ...W.position },
          extent: W.extent
        }), W.extent === "parent" || W.expandParent)) {
          const H = ov(W, g, W.origin ?? E);
          I ? I = [
            [Math.min(H[0][0], I[0][0]), Math.min(H[0][1], I[0][1])],
            [Math.max(H[1][0], I[1][0]), Math.max(H[1][1], I[1][1])]
          ] : I = H;
        }
      h?.(k, { ...w });
    }).on("drag", (k) => {
      const { transform: $, snapGrid: z, snapToGrid: C, nodeOrigin: A } = n(), E = Dn(k.sourceEvent, {
        transform: $,
        snapGrid: z,
        snapToGrid: C,
        containerBounds: y
      }), D = [];
      if (!g)
        return;
      const { x: P, y: T, width: F, height: W } = w, H = {}, U = g.origin ?? A, { width: O, height: q, x: J, y: G } = iv(b, a.controlDirection, E, a.boundaries, a.keepAspectRatio, U, S, I), R = O !== F, X = q !== W, de = J !== P && R, le = G !== T && X;
      if (!de && !le && !R && !X)
        return;
      if ((de || le || U[0] === 1 || U[1] === 1) && (H.x = de ? J : w.x, H.y = le ? G : w.y, w.x = H.x, w.y = H.y, N.length > 0)) {
        const V = J - P, ne = G - T;
        for (const ge of N)
          ge.position = {
            x: ge.position.x - V + U[0] * (O - F),
            y: ge.position.y - ne + U[1] * (q - W)
          }, D.push(ge);
      }
      if ((R || X) && (H.width = R && (!a.resizeDirection || a.resizeDirection === "horizontal") ? O : w.width, H.height = X && (!a.resizeDirection || a.resizeDirection === "vertical") ? q : w.height, w.width = H.width, w.height = H.height), j && g.expandParent) {
        const V = U[0] * (H.width ?? 0);
        H.x && H.x < V && (w.x = V, b.x = b.x - (H.x - V));
        const ne = U[1] * (H.height ?? 0);
        H.y && H.y < ne && (w.y = ne, b.y = b.y - (H.y - ne));
      }
      const te = nv({
        width: w.width,
        prevWidth: F,
        height: w.height,
        prevHeight: W,
        affectsX: a.controlDirection.affectsX,
        affectsY: a.controlDirection.affectsY
      }), ie = { ...w, direction: te };
      x?.(k, ie) !== !1 && (_ = !0, m?.(k, ie), i(H, D));
    }).on("end", (k) => {
      _ && (v?.(k, { ...w }), r?.({ ...w }), _ = !1);
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
var jo = { exports: {} }, No = {}, So = { exports: {} }, Co = {};
var nc;
function av() {
  if (nc) return Co;
  nc = 1;
  var e = Fe;
  function t(f, p) {
    return f === p && (f !== 0 || 1 / f === 1 / p) || f !== f && p !== p;
  }
  var n = typeof Object.is == "function" ? Object.is : t, i = e.useState, r = e.useEffect, s = e.useLayoutEffect, a = e.useDebugValue;
  function c(f, p) {
    var h = p(), m = i({ inst: { value: h, getSnapshot: p } }), v = m[0].inst, x = m[1];
    return s(
      function() {
        v.value = h, v.getSnapshot = p, u(v) && x({ inst: v });
      },
      [f, h, p]
    ), r(
      function() {
        return u(v) && x({ inst: v }), f(function() {
          u(v) && x({ inst: v });
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
  return Co.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : d, Co;
}
var ic;
function cv() {
  return ic || (ic = 1, So.exports = av()), So.exports;
}
var rc;
function lv() {
  if (rc) return No;
  rc = 1;
  var e = Fe, t = cv();
  function n(l, d) {
    return l === d && (l !== 0 || 1 / l === 1 / d) || l !== l && d !== d;
  }
  var i = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, s = e.useRef, a = e.useEffect, c = e.useMemo, u = e.useDebugValue;
  return No.useSyncExternalStoreWithSelector = function(l, d, f, p, h) {
    var m = s(null);
    if (m.current === null) {
      var v = { hasValue: !1, value: null };
      m.current = v;
    } else v = m.current;
    m = c(
      function() {
        function w(j) {
          if (!b) {
            if (b = !0, g = j, j = p(j), h !== void 0 && v.hasValue) {
              var S = v.value;
              if (h(S, j))
                return y = S;
            }
            return y = j;
          }
          if (S = y, i(g, j)) return S;
          var I = p(j);
          return h !== void 0 && h(S, I) ? (g = j, S) : (g = j, y = I);
        }
        var b = !1, g, y, N = f === void 0 ? null : f;
        return [
          function() {
            return w(d());
          },
          N === null ? void 0 : function() {
            return w(N());
          }
        ];
      },
      [d, f, p, h]
    );
    var x = r(l, m[0], m[1]);
    return a(
      function() {
        v.hasValue = !0, v.value = x;
      },
      [x]
    ), u(x), x;
  }, No;
}
var oc;
function uv() {
  return oc || (oc = 1, jo.exports = lv()), jo.exports;
}
var dv = uv();
const fv = /* @__PURE__ */ Uf(dv), pv = {}, sc = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), i = (d, f) => {
    const p = typeof d == "function" ? d(t) : d;
    if (!Object.is(p, t)) {
      const h = t;
      t = f ?? (typeof p != "object" || p === null) ? p : Object.assign({}, t, p), n.forEach((m) => m(t, h));
    }
  }, r = () => t, u = { setState: i, getState: r, getInitialState: () => l, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (pv ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(i, r, u);
  return u;
}, hv = (e) => e ? sc(e) : sc, { useDebugValue: gv } = Fe, { useSyncExternalStoreWithSelector: mv } = fv, yv = (e) => e;
function nd(e, t = yv, n) {
  const i = mv(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return gv(i), i;
}
const ac = (e, t) => {
  const n = hv(e), i = (r, s = t) => nd(n, r, s);
  return Object.assign(i, n), i;
}, xv = (e, t) => e ? ac(e, t) : ac;
function ve(e, t) {
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
var ko = { exports: {} }, Pe = {};
var cc;
function wv() {
  if (cc) return Pe;
  cc = 1;
  var e = Fe;
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
  return Pe.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = i, Pe.createPortal = function(u, l) {
    var d = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!l || l.nodeType !== 1 && l.nodeType !== 9 && l.nodeType !== 11)
      throw Error(t(299));
    return s(u, l, null, d);
  }, Pe.flushSync = function(u) {
    var l = a.T, d = i.p;
    try {
      if (a.T = null, i.p = 2, u) return u();
    } finally {
      a.T = l, i.p = d, i.d.f();
    }
  }, Pe.preconnect = function(u, l) {
    typeof u == "string" && (l ? (l = l.crossOrigin, l = typeof l == "string" ? l === "use-credentials" ? l : "" : void 0) : l = null, i.d.C(u, l));
  }, Pe.prefetchDNS = function(u) {
    typeof u == "string" && i.d.D(u);
  }, Pe.preinit = function(u, l) {
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
  }, Pe.preinitModule = function(u, l) {
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
  }, Pe.preload = function(u, l) {
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
  }, Pe.preloadModule = function(u, l) {
    if (typeof u == "string")
      if (l) {
        var d = c(l.as, l.crossOrigin);
        i.d.m(u, {
          as: typeof l.as == "string" && l.as !== "script" ? l.as : void 0,
          crossOrigin: d,
          integrity: typeof l.integrity == "string" ? l.integrity : void 0
        });
      } else i.d.m(u);
  }, Pe.requestFormReset = function(u) {
    i.d.r(u);
  }, Pe.unstable_batchedUpdates = function(u, l) {
    return u(l);
  }, Pe.useFormState = function(u, l, d) {
    return a.H.useFormState(u, l, d);
  }, Pe.useFormStatus = function() {
    return a.H.useHostTransitionStatus();
  }, Pe.version = "19.2.7", Pe;
}
var lc;
function vv() {
  if (lc) return ko.exports;
  lc = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), ko.exports = wv(), ko.exports;
}
var bv = vv();
const jr = ns(null), jv = jr.Provider, id = Ke.error001("react");
function he(e, t) {
  const n = Un(jr);
  if (n === null)
    throw new Error(id);
  return nd(n, e, t);
}
function je() {
  const e = Un(jr);
  if (e === null)
    throw new Error(id);
  return ue(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const uc = { display: "none" }, Nv = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, rd = "react-flow__node-desc", od = "react-flow__edge-desc", Sv = "react-flow__aria-live", Cv = (e) => e.ariaLiveMessage, kv = (e) => e.ariaLabelConfig;
function Ev({ rfId: e }) {
  const t = he(Cv);
  return o.jsx("div", { id: `${Sv}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: Nv, children: t });
}
function Iv({ rfId: e, disableKeyboardA11y: t }) {
  const n = he(kv);
  return o.jsxs(o.Fragment, { children: [o.jsx("div", { id: `${rd}-${e}`, style: uc, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), o.jsx("div", { id: `${od}-${e}`, style: uc, children: n["edge.a11yDescription.default"] }), !t && o.jsx(Ev, { rfId: e })] });
}
const Nr = sl(({ position: e = "top-left", children: t, className: n, style: i, ...r }, s) => {
  const a = `${e}`.split("-");
  return o.jsx("div", { className: Ae(["react-flow__panel", n, ...a]), style: i, ref: s, ...r, children: t });
});
Nr.displayName = "Panel";
function Av({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : o.jsx(Nr, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: o.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const _v = (e) => {
  const t = [], n = [];
  for (const [, i] of e.nodeLookup)
    i.selected && t.push(i.internals.userNode);
  for (const [, i] of e.edgeLookup)
    i.selected && n.push(i);
  return { selectedNodes: t, selectedEdges: n };
}, _i = (e) => e.id;
function Dv(e, t) {
  return ve(e.selectedNodes.map(_i), t.selectedNodes.map(_i)) && ve(e.selectedEdges.map(_i), t.selectedEdges.map(_i));
}
function Tv({ onSelectionChange: e }) {
  const t = je(), { selectedNodes: n, selectedEdges: i } = he(_v, Dv);
  return ee(() => {
    const r = { nodes: n, edges: i };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((s) => s(r));
  }, [n, i, e]), null;
}
const $v = (e) => !!e.onSelectionChangeHandlers;
function Pv({ onSelectionChange: e }) {
  const t = he($v);
  return e || t ? o.jsx(Tv, { onSelectionChange: e }) : null;
}
const sd = [0, 0], Mv = { x: 0, y: 0, zoom: 1 }, Rv = [
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
], dc = [...Rv, "rfId"], Lv = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), fc = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: Bn,
  nodeOrigin: sd,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function zv(e) {
  const { setNodes: t, setEdges: n, setMinZoom: i, setMaxZoom: r, setTranslateExtent: s, setNodeExtent: a, reset: c, setDefaultNodesAndEdges: u } = he(Lv, ve), l = je();
  ee(() => (u(e.defaultNodes, e.defaultEdges), () => {
    d.current = fc, c();
  }), []);
  const d = oe(fc);
  return ee(
    () => {
      for (const f of dc) {
        const p = e[f], h = d.current[f];
        p !== h && (typeof e[f] > "u" || (f === "nodes" ? t(p) : f === "edges" ? n(p) : f === "minZoom" ? i(p) : f === "maxZoom" ? r(p) : f === "translateExtent" ? s(p) : f === "nodeExtent" ? a(p) : f === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: ww(p) }) : f === "fitView" ? l.setState({ fitViewQueued: p }) : f === "fitViewOptions" ? l.setState({ fitViewOptions: p }) : l.setState({ [f]: p })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    dc.map((f) => e[f])
  ), null;
}
function pc() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function Vv(e) {
  const [t, n] = K(e === "system" ? null : e);
  return ee(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const i = pc(), r = () => n(i?.matches ? "dark" : "light");
    return r(), i?.addEventListener("change", r), () => {
      i?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : pc()?.matches ? "dark" : "light";
}
const hc = typeof document < "u" ? document : null;
function Yn(e = null, t = { target: hc, actInsideInputWithModifier: !0 }) {
  const [n, i] = K(!1), r = oe(!1), s = oe(/* @__PURE__ */ new Set([])), [a, c] = ue(() => {
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
  return ee(() => {
    const u = t?.target ?? hc, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (h) => {
        if (r.current = h.ctrlKey || h.metaKey || h.shiftKey || h.altKey, (!r.current || r.current && !l) && zu(h))
          return !1;
        const v = mc(h.code, c);
        if (s.current.add(h[v]), gc(a, s.current, !1)) {
          const x = h.composedPath?.()?.[0] || h.target, w = x?.nodeName === "BUTTON" || x?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !w) && h.preventDefault(), i(!0);
        }
      }, f = (h) => {
        const m = mc(h.code, c);
        gc(a, s.current, !0) ? (i(!1), s.current.clear()) : s.current.delete(h[m]), h.key === "Meta" && s.current.clear(), r.current = !1;
      }, p = () => {
        s.current.clear(), i(!1);
      };
      return u?.addEventListener("keydown", d), u?.addEventListener("keyup", f), window.addEventListener("blur", p), window.addEventListener("contextmenu", p), () => {
        u?.removeEventListener("keydown", d), u?.removeEventListener("keyup", f), window.removeEventListener("blur", p), window.removeEventListener("contextmenu", p);
      };
    }
  }, [e, i]), n;
}
function gc(e, t, n) {
  return e.filter((i) => n || i.length === t.size).some((i) => i.every((r) => t.has(r)));
}
function mc(e, t) {
  return t.includes(e) ? "code" : "key";
}
const Ov = () => {
  const e = je();
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
      const { width: i, height: r, minZoom: s, maxZoom: a, panZoom: c } = e.getState(), u = Es(t, i, r, s, a, n?.padding ?? 0.1);
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
      return dn(l, i, f, d);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: i } = e.getState();
      if (!i)
        return t;
      const { x: r, y: s } = i.getBoundingClientRect(), a = rn(t, n);
      return {
        x: a.x + r,
        y: a.y + s
      };
    }
  }), []);
};
function ad(e, t) {
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
      Hv(u, c);
    n.push(c);
  }
  return r.length && r.forEach((s) => {
    s.index !== void 0 ? n.splice(s.index, 0, { ...s.item }) : n.push({ ...s.item });
  }), n;
}
function Hv(e, t) {
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
function cd(e, t) {
  return ad(e, t);
}
function ld(e, t) {
  return ad(e, t);
}
function St(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function Kt(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const i = [];
  for (const [r, s] of e) {
    const a = t.has(r);
    !(s.selected === void 0 && !a) && s.selected !== a && (n && (s.selected = a), i.push(St(s.id, a)));
  }
  return i;
}
function yc({ items: e = [], lookup: t }) {
  const n = [], i = new Map(e.map((r) => [r.id, r]));
  for (const [r, s] of e.entries()) {
    const a = t.get(s.id), c = a?.internals?.userNode ?? a;
    c !== void 0 && c !== s && n.push({ id: s.id, item: s, type: "replace" }), c === void 0 && n.push({ item: s, type: "add", index: r });
  }
  for (const [r] of t)
    i.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function xc(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const ud = Pu();
function dd(e, t, n = {}) {
  return Sw(e, t, {
    ...n,
    onError: n.onError ?? ud
  });
}
function Wv(e, t, n, i = { shouldReplaceId: !0 }) {
  return Cw(e, t, n, {
    ...i,
    onError: i.onError ?? ud
  });
}
const wc = (e) => uw(e), Fv = (e) => _u(e);
function fd(e) {
  return sl(e);
}
const Bv = typeof window < "u" ? Df : ee;
function vc(e) {
  const [t, n] = K(BigInt(0)), [i] = K(() => Kv(() => n((r) => r + BigInt(1))));
  return Bv(() => {
    const r = i.get();
    r.length && (e(r), i.reset());
  }, [t]), i;
}
function Kv(e) {
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
const pd = ns(null);
function Xv({ children: e }) {
  const t = je(), n = se((c) => {
    const { nodes: u = [], setNodes: l, hasDefaultNodes: d, onNodesChange: f, nodeLookup: p, fitViewQueued: h, onNodesChangeMiddlewareMap: m } = t.getState();
    let v = u;
    for (const w of c)
      v = typeof w == "function" ? w(v) : w;
    let x = yc({
      items: v,
      lookup: p
    });
    for (const w of m.values())
      x = w(x);
    d && l(v), x.length > 0 ? f?.(x) : h && window.requestAnimationFrame(() => {
      const { fitViewQueued: w, nodes: b, setNodes: g } = t.getState();
      w && g(b);
    });
  }, []), i = vc(n), r = se((c) => {
    const { edges: u = [], setEdges: l, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: p } = t.getState();
    let h = u;
    for (const m of c)
      h = typeof m == "function" ? m(h) : m;
    d ? l(h) : f && f(yc({
      items: h,
      lookup: p
    }));
  }, []), s = vc(r), a = ue(() => ({ nodeQueue: i, edgeQueue: s }), []);
  return o.jsx(pd.Provider, { value: a, children: e });
}
function qv() {
  const e = Un(pd);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Yv = (e) => !!e.panZoom;
function Ps() {
  const e = Ov(), t = je(), n = qv(), i = he(Yv), r = ue(() => {
    const s = (f) => t.getState().nodeLookup.get(f), a = (f) => {
      n.nodeQueue.push(f);
    }, c = (f) => {
      n.edgeQueue.push(f);
    }, u = (f) => {
      const { nodeLookup: p, nodeOrigin: h } = t.getState(), m = wc(f) ? f : p.get(f.id), v = m.parentId ? Ru(m.position, m.measured, m.parentId, p, h) : m.position, x = {
        ...m,
        position: v,
        width: m.measured?.width ?? m.width,
        height: m.measured?.height ?? m.height
      };
      return nn(x);
    }, l = (f, p, h = { replace: !1 }) => {
      a((m) => m.map((v) => {
        if (v.id === f) {
          const x = typeof p == "function" ? p(v) : p;
          return h.replace && wc(x) ? x : { ...v, ...x };
        }
        return v;
      }));
    }, d = (f, p, h = { replace: !1 }) => {
      c((m) => m.map((v) => {
        if (v.id === f) {
          const x = typeof p == "function" ? p(v) : p;
          return h.replace && Fv(x) ? x : { ...v, ...x };
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
        const { nodes: f = [], edges: p = [], transform: h } = t.getState(), [m, v, x] = h;
        return {
          nodes: f.map((w) => ({ ...w })),
          edges: p.map((w) => ({ ...w })),
          viewport: {
            x: m,
            y: v,
            zoom: x
          }
        };
      },
      deleteElements: async ({ nodes: f = [], edges: p = [] }) => {
        const { nodes: h, edges: m, onNodesDelete: v, onEdgesDelete: x, triggerNodeChanges: w, triggerEdgeChanges: b, onDelete: g, onBeforeDelete: y } = t.getState(), { nodes: N, edges: j } = await gw({
          nodesToRemove: f,
          edgesToRemove: p,
          nodes: h,
          edges: m,
          onBeforeDelete: y
        }), S = j.length > 0, I = N.length > 0;
        if (S) {
          const _ = j.map(xc);
          x?.(j), b(_);
        }
        if (I) {
          const _ = N.map(xc);
          v?.(N), w(_);
        }
        return (I || S) && g?.({ nodes: N, edges: j }), { deletedNodes: N, deletedEdges: j };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, p = !0, h) => {
        const m = Fa(f), v = m ? f : u(f), x = h !== void 0;
        return v ? (h || t.getState().nodes).filter((w) => {
          const b = t.getState().nodeLookup.get(w.id);
          if (b && !m && (w.id === f.id || !b.internals.positionAbsolute))
            return !1;
          const g = nn(x ? w : b), y = Xn(g, v);
          return p && y > 0 || y >= g.width * g.height || y >= v.width * v.height;
        }) : [];
      },
      isNodeIntersecting: (f, p, h = !0) => {
        const v = Fa(f) ? f : u(f);
        if (!v)
          return !1;
        const x = Xn(v, p);
        return h && x > 0 || x >= p.width * p.height || x >= v.width * v.height;
      },
      updateNode: l,
      updateNodeData: (f, p, h = { replace: !1 }) => {
        l(f, (m) => {
          const v = typeof p == "function" ? p(m) : p;
          return h.replace ? { ...m, data: v } : { ...m, data: { ...m.data, ...v } };
        }, h);
      },
      updateEdge: d,
      updateEdgeData: (f, p, h = { replace: !1 }) => {
        d(f, (m) => {
          const v = typeof p == "function" ? p(m) : p;
          return h.replace ? { ...m, data: v } : { ...m, data: { ...m.data, ...v } };
        }, h);
      },
      getNodesBounds: (f) => {
        const { nodeLookup: p, nodeOrigin: h } = t.getState();
        return dw(f, { nodeLookup: p, nodeOrigin: h });
      },
      getHandleConnections: ({ type: f, id: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}-${f}${p ? `-${p}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}${f ? p ? `-${f}-${p}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const p = t.getState().fitViewResolver ?? xw();
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
const bc = (e) => e.selected, Uv = typeof window < "u" ? window : void 0;
function Zv({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = je(), { deleteElements: i } = Ps(), r = Yn(e, { actInsideInputWithModifier: !1 }), s = Yn(t, { target: Uv });
  ee(() => {
    if (r) {
      const { edges: a, nodes: c } = n.getState();
      i({ nodes: c.filter(bc), edges: a.filter(bc) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), ee(() => {
    n.setState({ multiSelectionActive: s });
  }, [s]);
}
function Gv(e) {
  const t = je();
  ee(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const i = Is(e.current);
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
const Sr = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, Jv = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function Qv({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: i = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: s = Et.Free, zoomOnDoubleClick: a = !0, panOnDrag: c = !0, defaultViewport: u, translateExtent: l, minZoom: d, maxZoom: f, zoomActivationKeyCode: p, preventScrolling: h = !0, children: m, noWheelClassName: v, noPanClassName: x, onViewportChange: w, isControlledViewport: b, paneClickDistance: g, selectionOnDrag: y }) {
  const N = je(), j = oe(null), { userSelectionActive: S, lib: I, connectionInProgress: _ } = he(Jv, ve), M = Yn(p), k = oe();
  Gv(j);
  const $ = se((z) => {
    w?.({ x: z[0], y: z[1], zoom: z[2] }), b || N.setState({ transform: z });
  }, [w, b]);
  return ee(() => {
    if (j.current) {
      k.current = tv({
        domNode: j.current,
        minZoom: d,
        maxZoom: f,
        translateExtent: l,
        viewport: u,
        onDraggingChange: (E) => N.setState((D) => D.paneDragging === E ? D : { paneDragging: E }),
        onPanZoomStart: (E, D) => {
          const { onViewportChangeStart: P, onMoveStart: T } = N.getState();
          T?.(E, D), P?.(D);
        },
        onPanZoom: (E, D) => {
          const { onViewportChange: P, onMove: T } = N.getState();
          T?.(E, D), P?.(D);
        },
        onPanZoomEnd: (E, D) => {
          const { onViewportChangeEnd: P, onMoveEnd: T } = N.getState();
          T?.(E, D), P?.(D);
        }
      });
      const { x: z, y: C, zoom: A } = k.current.getViewport();
      return N.setState({
        panZoom: k.current,
        transform: [z, C, A],
        domNode: j.current.closest(".react-flow")
      }), () => {
        k.current?.destroy();
      };
    }
  }, []), ee(() => {
    k.current?.update({
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
      noWheelClassName: v,
      lib: I,
      onTransformChange: $,
      connectionInProgress: _,
      selectionOnDrag: y,
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
    v,
    I,
    $,
    _,
    y,
    g
  ]), o.jsx("div", { className: "react-flow__renderer", ref: j, style: Sr, children: m });
}
const e0 = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function t0() {
  const { userSelectionActive: e, userSelectionRect: t } = he(e0, ve);
  return e && t ? o.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const Eo = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, n0 = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function i0({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Kn.Full, panOnDrag: i, autoPanOnSelection: r, paneClickDistance: s, selectionOnDrag: a, onSelectionStart: c, onSelectionEnd: u, onPaneClick: l, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: p, onPaneMouseMove: h, onPaneMouseLeave: m, children: v }) {
  const x = oe(0), w = je(), { userSelectionActive: b, elementsSelectable: g, dragging: y, connectionInProgress: N, panBy: j, autoPanSpeed: S } = he(n0, ve), I = g && (e || b), _ = oe(null), M = oe(), k = oe(/* @__PURE__ */ new Set()), $ = oe(/* @__PURE__ */ new Set()), z = oe(!1), C = oe({ x: 0, y: 0 }), A = oe(!1), E = (R) => {
    if (z.current || N) {
      z.current = !1;
      return;
    }
    l?.(R), w.getState().resetSelectedElements(), w.setState({ nodesSelectionActive: !1 });
  }, D = (R) => {
    if (Array.isArray(i) && i?.includes(2)) {
      R.preventDefault();
      return;
    }
    d?.(R);
  }, P = f ? (R) => f(R) : void 0, T = (R) => {
    z.current && (R.stopPropagation(), z.current = !1);
  }, F = (R) => {
    const { domNode: X, transform: de } = w.getState();
    if (M.current = X?.getBoundingClientRect(), !M.current)
      return;
    const le = R.target === _.current;
    if (!le && !!R.target.closest(".nokey") || !e || !(a && le || t) || R.button !== 0 || !R.isPrimary)
      return;
    R.target?.setPointerCapture?.(R.pointerId), z.current = !1;
    const { x: fe, y: V } = Qe(R.nativeEvent, M.current), ne = dn({ x: fe, y: V }, de);
    w.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: ne.x,
        startY: ne.y,
        x: fe,
        y: V
      }
    }), le || (R.stopPropagation(), R.preventDefault());
  };
  function W(R, X) {
    const { userSelectionRect: de } = w.getState();
    if (!de)
      return;
    const { transform: le, nodeLookup: te, edgeLookup: ie, connectionLookup: fe, triggerNodeChanges: V, triggerEdgeChanges: ne, defaultEdgeOptions: ge } = w.getState(), ye = { x: de.startX, y: de.startY }, { x: Ie, y: _e } = rn(ye, le), Ve = {
      startX: ye.x,
      startY: ye.y,
      x: R < Ie ? R : Ie,
      y: X < _e ? X : _e,
      width: Math.abs(R - Ie),
      height: Math.abs(X - _e)
    }, at = k.current, Ye = $.current;
    k.current = new Set(Cs(te, Ve, le, n === Kn.Partial, !0).map((Me) => Me.id)), $.current = /* @__PURE__ */ new Set();
    const Ue = ge?.selectable ?? !0;
    for (const Me of k.current) {
      const Oe = fe.get(Me);
      if (Oe)
        for (const { edgeId: Ne } of Oe.values()) {
          const Xe = ie.get(Ne);
          Xe && (Xe.selectable ?? Ue) && $.current.add(Ne);
        }
    }
    if (!Ba(at, k.current)) {
      const Me = Kt(te, k.current, !0);
      V(Me);
    }
    if (!Ba(Ye, $.current)) {
      const Me = Kt(ie, $.current);
      ne(Me);
    }
    w.setState({
      userSelectionRect: Ve,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function H() {
    if (!r || !M.current)
      return;
    const [R, X] = ks(C.current, M.current, S);
    j({ x: R, y: X }).then((de) => {
      if (!z.current || !de) {
        x.current = requestAnimationFrame(H);
        return;
      }
      const { x: le, y: te } = C.current;
      W(le, te), x.current = requestAnimationFrame(H);
    });
  }
  const U = () => {
    cancelAnimationFrame(x.current), x.current = 0, A.current = !1;
  };
  ee(() => () => U(), []);
  const O = (R) => {
    const { userSelectionRect: X, transform: de, resetSelectedElements: le } = w.getState();
    if (!M.current || !X)
      return;
    const { x: te, y: ie } = Qe(R.nativeEvent, M.current);
    C.current = { x: te, y: ie };
    const fe = rn({ x: X.startX, y: X.startY }, de);
    if (!z.current) {
      const V = t ? 0 : s;
      if (Math.hypot(te - fe.x, ie - fe.y) <= V)
        return;
      le(), c?.(R);
    }
    z.current = !0, A.current || (H(), A.current = !0), W(te, ie);
  }, q = (R) => {
    R.button === 0 && (R.target?.releasePointerCapture?.(R.pointerId), !b && R.target === _.current && w.getState().userSelectionRect && E?.(R), w.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), z.current && (u?.(R), w.setState({
      nodesSelectionActive: k.current.size > 0
    })), U());
  }, J = (R) => {
    R.target?.releasePointerCapture?.(R.pointerId), U();
  }, G = i === !0 || Array.isArray(i) && i.includes(0);
  return o.jsxs("div", { className: Ae(["react-flow__pane", { draggable: G, dragging: y, selection: e }]), onClick: I ? void 0 : Eo(E, _), onContextMenu: Eo(D, _), onWheel: Eo(P, _), onPointerEnter: I ? void 0 : p, onPointerMove: I ? O : h, onPointerUp: I ? q : void 0, onPointerCancel: I ? J : void 0, onPointerDownCapture: I ? F : void 0, onClickCapture: I ? T : void 0, onPointerLeave: m, ref: _, style: Sr, children: [v, o.jsx(t0, {})] });
}
function qo({ id: e, store: t, unselect: n = !1, nodeRef: i }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: s, multiSelectionActive: a, nodeLookup: c, onError: u } = t.getState(), l = c.get(e);
  if (!l) {
    u?.("012", Ke.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && a) && (s({ nodes: [l], edges: [] }), requestAnimationFrame(() => i?.current?.blur())) : r([e]);
}
function hd({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: i, nodeId: r, isSelectable: s, nodeClickDistance: a }) {
  const c = je(), [u, l] = K(!1), d = oe();
  return ee(() => {
    d.current = Hw({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (f) => {
        qo({
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
  }, []), ee(() => {
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
const r0 = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function gd() {
  const e = je();
  return se((n) => {
    const { nodeExtent: i, snapToGrid: r, snapGrid: s, nodesDraggable: a, onError: c, updateNodePositions: u, nodeLookup: l, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), p = r0(a), h = r ? s[0] : 5, m = r ? s[1] : 5, v = n.direction.x * h * n.factor, x = n.direction.y * m * n.factor;
    for (const [, w] of l) {
      if (!p(w))
        continue;
      let b = {
        x: w.internals.positionAbsolute.x + v,
        y: w.internals.positionAbsolute.y + x
      };
      r && (b = ii(b, s));
      const { position: g, positionAbsolute: y } = Du({
        nodeId: w.id,
        nextPosition: b,
        nodeLookup: l,
        nodeExtent: i,
        nodeOrigin: d,
        onError: c
      });
      w.position = g, w.internals.positionAbsolute = y, f.set(w.id, w);
    }
    u(f);
  }, []);
}
const Ms = ns(null), o0 = Ms.Provider;
Ms.Consumer;
const md = () => Un(Ms), s0 = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), a0 = (e, t, n) => (i) => {
  const { connectionClickStartHandle: r, connectionMode: s, connection: a } = i, { fromHandle: c, toHandle: u, isValid: l } = a, d = u?.nodeId === e && u?.id === t && u?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: d,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: s === en.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!r,
    valid: d && l
  };
};
function c0({ type: e = "source", position: t = ae.Top, isValidConnection: n, isConnectable: i = !0, isConnectableStart: r = !0, isConnectableEnd: s = !0, id: a, onConnect: c, children: u, className: l, onMouseDown: d, onTouchStart: f, ...p }, h) {
  const m = a || null, v = e === "target", x = je(), w = md(), { connectOnClick: b, noPanClassName: g, rfId: y } = he(s0, ve), { connectingFrom: N, connectingTo: j, clickConnecting: S, isPossibleEndHandle: I, connectionInProcess: _, clickConnectionInProcess: M, valid: k } = he(a0(w, m, e), ve);
  w || x.getState().onError?.("010", Ke.error010());
  const $ = (A) => {
    const { defaultEdgeOptions: E, onConnect: D, hasDefaultEdges: P } = x.getState(), T = {
      ...E,
      ...A
    };
    if (P) {
      const { edges: F, setEdges: W, onError: H } = x.getState();
      W(dd(T, F, { onError: H }));
    }
    D?.(T), c?.(T);
  }, z = (A) => {
    if (!w)
      return;
    const E = Vu(A.nativeEvent);
    if (r && (E && A.button === 0 || !E)) {
      const D = x.getState();
      Xo.onPointerDown(A.nativeEvent, {
        handleDomNode: A.currentTarget,
        autoPanOnConnect: D.autoPanOnConnect,
        connectionMode: D.connectionMode,
        connectionRadius: D.connectionRadius,
        domNode: D.domNode,
        nodeLookup: D.nodeLookup,
        lib: D.lib,
        isTarget: v,
        handleId: m,
        nodeId: w,
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
    const { onClickConnectStart: E, onClickConnectEnd: D, connectionClickStartHandle: P, connectionMode: T, isValidConnection: F, lib: W, rfId: H, nodeLookup: U, connection: O } = x.getState();
    if (!w || !P && !r)
      return;
    if (!P) {
      E?.(A.nativeEvent, { nodeId: w, handleId: m, handleType: e }), x.setState({ connectionClickStartHandle: { nodeId: w, type: e, id: m } });
      return;
    }
    const q = Lu(A.target), J = n || F, { connection: G, isValid: R } = Xo.isValid(A.nativeEvent, {
      handle: {
        nodeId: w,
        id: m,
        type: e
      },
      connectionMode: T,
      fromNodeId: P.nodeId,
      fromHandleId: P.id || null,
      fromType: P.type,
      isValidConnection: J,
      flowId: H,
      doc: q,
      lib: W,
      nodeLookup: U
    });
    R && G && $(G);
    const X = structuredClone(O);
    delete X.inProgress, X.toPosition = X.toHandle ? X.toHandle.position : null, D?.(A, X), x.setState({ connectionClickStartHandle: null });
  };
  return o.jsx("div", { "data-handleid": m, "data-nodeid": w, "data-handlepos": t, "data-id": `${y}-${w}-${m}-${e}`, className: Ae([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    g,
    l,
    {
      source: !v,
      target: v,
      connectable: i,
      connectablestart: r,
      connectableend: s,
      clickconnecting: S,
      connectingfrom: N,
      connectingto: j,
      valid: k,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: i && (!_ || I) && (_ || M ? s : r)
    }
  ]), onMouseDown: z, onTouchStart: z, onClick: b ? C : void 0, ref: h, ...p, children: u });
}
const sn = Ee(fd(c0));
function l0({ data: e, isConnectable: t, sourcePosition: n = ae.Bottom }) {
  return o.jsxs(o.Fragment, { children: [e?.label, o.jsx(sn, { type: "source", position: n, isConnectable: t })] });
}
function u0({ data: e, isConnectable: t, targetPosition: n = ae.Top, sourcePosition: i = ae.Bottom }) {
  return o.jsxs(o.Fragment, { children: [o.jsx(sn, { type: "target", position: n, isConnectable: t }), e?.label, o.jsx(sn, { type: "source", position: i, isConnectable: t })] });
}
function d0() {
  return null;
}
function f0({ data: e, isConnectable: t, targetPosition: n = ae.Top }) {
  return o.jsxs(o.Fragment, { children: [o.jsx(sn, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const sr = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, jc = {
  input: l0,
  default: u0,
  output: f0,
  group: d0
};
function p0(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const h0 = (e) => {
  const { width: t, height: n, x: i, y: r } = ni(e.nodeLookup, {
    filter: (s) => !!s.selected
  });
  return {
    width: Je(t) ? t : null,
    height: Je(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${i}px,${r}px)`
  };
};
function g0({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const i = je(), { width: r, height: s, transformString: a, userSelectionActive: c } = he(h0, ve), u = gd(), l = oe(null);
  ee(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !c && r !== null && s !== null;
  if (hd({
    nodeRef: l,
    disabled: !d
  }), !d)
    return null;
  const f = e ? (h) => {
    const m = i.getState().nodes.filter((v) => v.selected);
    e(h, m);
  } : void 0, p = (h) => {
    Object.prototype.hasOwnProperty.call(sr, h.key) && (h.preventDefault(), u({
      direction: sr[h.key],
      factor: h.shiftKey ? 4 : 1
    }));
  };
  return o.jsx("div", { className: Ae(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: a
  }, children: o.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : p, style: {
    width: r,
    height: s
  } }) });
}
const Nc = typeof window < "u" ? window : void 0, m0 = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function yd({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: i, onPaneMouseLeave: r, onPaneContextMenu: s, onPaneScroll: a, paneClickDistance: c, deleteKeyCode: u, selectionKeyCode: l, selectionOnDrag: d, selectionMode: f, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: m, panActivationKeyCode: v, zoomActivationKeyCode: x, elementsSelectable: w, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: y, panOnScrollSpeed: N, panOnScrollMode: j, zoomOnDoubleClick: S, panOnDrag: I, autoPanOnSelection: _, defaultViewport: M, translateExtent: k, minZoom: $, maxZoom: z, preventScrolling: C, onSelectionContextMenu: A, noWheelClassName: E, noPanClassName: D, disableKeyboardA11y: P, onViewportChange: T, isControlledViewport: F }) {
  const { nodesSelectionActive: W, userSelectionActive: H } = he(m0, ve), U = Yn(l, { target: Nc }), O = Yn(v, { target: Nc }), q = O || I, J = O || y, G = d && q !== !0, R = U || H || G;
  return Zv({ deleteKeyCode: u, multiSelectionKeyCode: m }), o.jsx(Qv, { onPaneContextMenu: s, elementsSelectable: w, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: J, panOnScrollSpeed: N, panOnScrollMode: j, zoomOnDoubleClick: S, panOnDrag: !U && q, defaultViewport: M, translateExtent: k, minZoom: $, maxZoom: z, zoomActivationKeyCode: x, preventScrolling: C, noWheelClassName: E, noPanClassName: D, onViewportChange: T, isControlledViewport: F, paneClickDistance: c, selectionOnDrag: G, children: o.jsxs(i0, { onSelectionStart: p, onSelectionEnd: h, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: i, onPaneMouseLeave: r, onPaneContextMenu: s, onPaneScroll: a, panOnDrag: q, autoPanOnSelection: _, isSelecting: !!R, selectionMode: f, selectionKeyPressed: U, paneClickDistance: c, selectionOnDrag: G, children: [e, W && o.jsx(g0, { onSelectionContextMenu: A, noPanClassName: D, disableKeyboardA11y: P })] }) });
}
yd.displayName = "FlowRenderer";
const y0 = Ee(yd), x0 = (e) => (t) => e ? Cs(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function w0(e) {
  return he(se(x0(e), [e]), ve);
}
const v0 = (e) => e.updateNodeInternals;
function b0() {
  const e = he(v0), [t] = K(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
  return ee(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function j0({ node: e, nodeType: t, hasDimensions: n, resizeObserver: i }) {
  const r = je(), s = oe(null), a = oe(null), c = oe(e.sourcePosition), u = oe(e.targetPosition), l = oe(t), d = n && !!e.internals.handleBounds;
  return ee(() => {
    s.current && !e.hidden && (!d || a.current !== s.current) && (a.current && i?.unobserve(a.current), i?.observe(s.current), a.current = s.current);
  }, [d, e.hidden]), ee(() => () => {
    a.current && (i?.unobserve(a.current), a.current = null);
  }, []), ee(() => {
    if (s.current) {
      const f = l.current !== t, p = c.current !== e.sourcePosition, h = u.current !== e.targetPosition;
      (f || p || h) && (l.current = t, c.current = e.sourcePosition, u.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: s.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), s;
}
function N0({ id: e, onClick: t, onMouseEnter: n, onMouseMove: i, onMouseLeave: r, onContextMenu: s, onDoubleClick: a, nodesDraggable: c, elementsSelectable: u, nodesConnectable: l, nodesFocusable: d, resizeObserver: f, noDragClassName: p, noPanClassName: h, disableKeyboardA11y: m, rfId: v, nodeTypes: x, nodeClickDistance: w, onError: b }) {
  const { node: g, internals: y, isParent: N } = he((R) => {
    const X = R.nodeLookup.get(e), de = R.parentLookup.has(e);
    return {
      node: X,
      internals: X.internals,
      isParent: de
    };
  }, ve);
  let j = g.type || "default", S = x?.[j] || jc[j];
  S === void 0 && (b?.("003", Ke.error003(j)), j = "default", S = x?.default || jc.default);
  const I = !!(g.draggable || c && typeof g.draggable > "u"), _ = !!(g.selectable || u && typeof g.selectable > "u"), M = !!(g.connectable || l && typeof g.connectable > "u"), k = !!(g.focusable || d && typeof g.focusable > "u"), $ = je(), z = Mu(g), C = j0({ node: g, nodeType: j, hasDimensions: z, resizeObserver: f }), A = hd({
    nodeRef: C,
    disabled: g.hidden || !I,
    noDragClassName: p,
    handleSelector: g.dragHandle,
    nodeId: e,
    isSelectable: _,
    nodeClickDistance: w
  }), E = gd();
  if (g.hidden)
    return null;
  const D = ht(g), P = p0(g), T = _ || I || t || n || i || r, F = n ? (R) => n(R, { ...y.userNode }) : void 0, W = i ? (R) => i(R, { ...y.userNode }) : void 0, H = r ? (R) => r(R, { ...y.userNode }) : void 0, U = s ? (R) => s(R, { ...y.userNode }) : void 0, O = a ? (R) => a(R, { ...y.userNode }) : void 0, q = (R) => {
    const { selectNodesOnDrag: X, nodeDragThreshold: de } = $.getState();
    _ && (!X || !I || de > 0) && qo({
      id: e,
      store: $,
      nodeRef: C
    }), t && t(R, { ...y.userNode });
  }, J = (R) => {
    if (!(zu(R.nativeEvent) || m)) {
      if (ku.includes(R.key) && _) {
        const X = R.key === "Escape";
        qo({
          id: e,
          store: $,
          unselect: X,
          nodeRef: C
        });
      } else if (I && g.selected && Object.prototype.hasOwnProperty.call(sr, R.key)) {
        R.preventDefault();
        const { ariaLabelConfig: X } = $.getState();
        $.setState({
          ariaLiveMessage: X["node.a11yDescription.ariaLiveMessage"]({
            direction: R.key.replace("Arrow", "").toLowerCase(),
            x: ~~y.positionAbsolute.x,
            y: ~~y.positionAbsolute.y
          })
        }), E({
          direction: sr[R.key],
          factor: R.shiftKey ? 4 : 1
        });
      }
    }
  }, G = () => {
    if (m || !C.current?.matches(":focus-visible"))
      return;
    const { transform: R, width: X, height: de, autoPanOnNodeFocus: le, setCenter: te } = $.getState();
    if (!le)
      return;
    Cs(/* @__PURE__ */ new Map([[e, g]]), { x: 0, y: 0, width: X, height: de }, R, !0).length > 0 || te(g.position.x + D.width / 2, g.position.y + D.height / 2, {
      zoom: R[2]
    });
  };
  return o.jsx("div", { className: Ae([
    "react-flow__node",
    `react-flow__node-${j}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [h]: I
    },
    g.className,
    {
      selected: g.selected,
      selectable: _,
      parent: N,
      draggable: I,
      dragging: A
    }
  ]), ref: C, style: {
    zIndex: y.z,
    transform: `translate(${y.positionAbsolute.x}px,${y.positionAbsolute.y}px)`,
    pointerEvents: T ? "all" : "none",
    visibility: z ? "visible" : "hidden",
    ...g.style,
    ...P
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: F, onMouseMove: W, onMouseLeave: H, onContextMenu: U, onClick: q, onDoubleClick: O, onKeyDown: k ? J : void 0, tabIndex: k ? 0 : void 0, onFocus: k ? G : void 0, role: g.ariaRole ?? (k ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": m ? void 0 : `${rd}-${v}`, "aria-label": g.ariaLabel, ...g.domAttributes, children: o.jsx(o0, { value: e, children: o.jsx(S, { id: e, data: g.data, type: j, positionAbsoluteX: y.positionAbsolute.x, positionAbsoluteY: y.positionAbsolute.y, selected: g.selected ?? !1, selectable: _, draggable: I, deletable: g.deletable ?? !0, isConnectable: M, sourcePosition: g.sourcePosition, targetPosition: g.targetPosition, dragging: A, dragHandle: g.dragHandle, zIndex: y.z, parentId: g.parentId, ...D }) }) });
}
var S0 = Ee(N0);
const C0 = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function xd(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: i, elementsSelectable: r, onError: s } = he(C0, ve), a = w0(e.onlyRenderVisibleElements), c = b0();
  return o.jsx("div", { className: "react-flow__nodes", style: Sr, children: a.map((u) => (
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
    o.jsx(S0, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: i, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: s }, u)
  )) });
}
xd.displayName = "NodeRenderer";
const k0 = Ee(xd);
function E0(e) {
  return he(se((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const i = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const s = n.nodeLookup.get(r.source), a = n.nodeLookup.get(r.target);
        s && a && jw({
          sourceNode: s,
          targetNode: a,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && i.push(r.id);
      }
    return i;
  }, [e]), ve);
}
const I0 = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return o.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, A0 = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return o.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Sc = {
  [ir.Arrow]: I0,
  [ir.ArrowClosed]: A0
};
function _0(e) {
  const t = je();
  return ue(() => Object.prototype.hasOwnProperty.call(Sc, e) ? Sc[e] : (t.getState().onError?.("009", Ke.error009(e)), null), [e]);
}
const D0 = ({ id: e, type: t, color: n, width: i = 12.5, height: r = 12.5, markerUnits: s = "strokeWidth", strokeWidth: a, orient: c = "auto-start-reverse" }) => {
  const u = _0(t);
  return u ? o.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${i}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: s, orient: c, refX: "0", refY: "0", children: o.jsx(u, { color: n, strokeWidth: a }) }) : null;
}, wd = ({ defaultColor: e, rfId: t }) => {
  const n = he((s) => s.edges), i = he((s) => s.defaultEdgeOptions), r = ue(() => _w(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: i?.markerStart,
    defaultMarkerEnd: i?.markerEnd
  }), [n, i, t, e]);
  return r.length ? o.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: o.jsx("defs", { children: r.map((s) => o.jsx(D0, { id: s.id, type: s.type, color: s.color, width: s.width, height: s.height, markerUnits: s.markerUnits, strokeWidth: s.strokeWidth, orient: s.orient }, s.id)) }) }) : null;
};
wd.displayName = "MarkerDefinitions";
var T0 = Ee(wd);
function vd({ x: e, y: t, label: n, labelStyle: i, labelShowBg: r = !0, labelBgStyle: s, labelBgPadding: a = [2, 4], labelBgBorderRadius: c = 2, children: u, className: l, ...d }) {
  const [f, p] = K({ x: 1, y: 0, width: 0, height: 0 }), h = Ae(["react-flow__edge-textwrapper", l]), m = oe(null);
  return ee(() => {
    if (m.current) {
      const v = m.current.getBBox();
      p({
        x: v.x,
        y: v.y,
        width: v.width,
        height: v.height
      });
    }
  }, [n]), n ? o.jsxs("g", { transform: `translate(${e - f.width / 2} ${t - f.height / 2})`, className: h, visibility: f.width ? "visible" : "hidden", ...d, children: [r && o.jsx("rect", { width: f.width + 2 * a[0], x: -a[0], y: -a[1], height: f.height + 2 * a[1], className: "react-flow__edge-textbg", style: s, rx: c, ry: c }), o.jsx("text", { className: "react-flow__edge-text", y: f.height / 2, dy: "0.3em", ref: m, style: i, children: n }), u] }) : null;
}
vd.displayName = "EdgeText";
const $0 = Ee(vd);
function ri({ path: e, labelX: t, labelY: n, label: i, labelStyle: r, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u, interactionWidth: l = 20, ...d }) {
  return o.jsxs(o.Fragment, { children: [o.jsx("path", { ...d, d: e, fill: "none", className: Ae(["react-flow__edge-path", d.className]) }), l ? o.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, i && Je(t) && Je(n) ? o.jsx($0, { x: t, y: n, label: i, labelStyle: r, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u }) : null] });
}
function Cc({ pos: e, x1: t, y1: n, x2: i, y2: r }) {
  return e === ae.Left || e === ae.Right ? [0.5 * (t + i), n] : [t, 0.5 * (n + r)];
}
function bd({ sourceX: e, sourceY: t, sourcePosition: n = ae.Bottom, targetX: i, targetY: r, targetPosition: s = ae.Top }) {
  const [a, c] = Cc({
    pos: n,
    x1: e,
    y1: t,
    x2: i,
    y2: r
  }), [u, l] = Cc({
    pos: s,
    x1: i,
    y1: r,
    x2: e,
    y2: t
  }), [d, f, p, h] = Ou({
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
function jd(e) {
  return Ee(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, sourcePosition: a, targetPosition: c, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: m, markerEnd: v, markerStart: x, interactionWidth: w }) => {
    const [b, g, y] = bd({
      sourceX: n,
      sourceY: i,
      sourcePosition: a,
      targetX: r,
      targetY: s,
      targetPosition: c
    }), N = e.isInternal ? void 0 : t;
    return o.jsx(ri, { id: N, path: b, labelX: g, labelY: y, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: m, markerEnd: v, markerStart: x, interactionWidth: w });
  });
}
const P0 = jd({ isInternal: !1 }), Nd = jd({ isInternal: !0 });
P0.displayName = "SimpleBezierEdge";
Nd.displayName = "SimpleBezierEdgeInternal";
function Sd(e) {
  return Ee(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, sourcePosition: h = ae.Bottom, targetPosition: m = ae.Top, markerEnd: v, markerStart: x, pathOptions: w, interactionWidth: b }) => {
    const [g, y, N] = or({
      sourceX: n,
      sourceY: i,
      sourcePosition: h,
      targetX: r,
      targetY: s,
      targetPosition: m,
      borderRadius: w?.borderRadius,
      offset: w?.offset,
      stepPosition: w?.stepPosition
    }), j = e.isInternal ? void 0 : t;
    return o.jsx(ri, { id: j, path: g, labelX: y, labelY: N, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: v, markerStart: x, interactionWidth: b });
  });
}
const Cd = Sd({ isInternal: !1 }), kd = Sd({ isInternal: !0 });
Cd.displayName = "SmoothStepEdge";
kd.displayName = "SmoothStepEdgeInternal";
function Ed(e) {
  return Ee(({ id: t, ...n }) => {
    const i = e.isInternal ? void 0 : t;
    return o.jsx(Cd, { ...n, id: i, pathOptions: ue(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const M0 = Ed({ isInternal: !1 }), Id = Ed({ isInternal: !0 });
M0.displayName = "StepEdge";
Id.displayName = "StepEdgeInternal";
function Ad(e) {
  return Ee(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: m, interactionWidth: v }) => {
    const [x, w, b] = Bu({ sourceX: n, sourceY: i, targetX: r, targetY: s }), g = e.isInternal ? void 0 : t;
    return o.jsx(ri, { id: g, path: x, labelX: w, labelY: b, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: m, interactionWidth: v });
  });
}
const R0 = Ad({ isInternal: !1 }), _d = Ad({ isInternal: !0 });
R0.displayName = "StraightEdge";
_d.displayName = "StraightEdgeInternal";
function Dd(e) {
  return Ee(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, sourcePosition: a = ae.Bottom, targetPosition: c = ae.Top, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: m, markerEnd: v, markerStart: x, pathOptions: w, interactionWidth: b }) => {
    const [g, y, N] = Hu({
      sourceX: n,
      sourceY: i,
      sourcePosition: a,
      targetX: r,
      targetY: s,
      targetPosition: c,
      curvature: w?.curvature
    }), j = e.isInternal ? void 0 : t;
    return o.jsx(ri, { id: j, path: g, labelX: y, labelY: N, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: m, markerEnd: v, markerStart: x, interactionWidth: b });
  });
}
const L0 = Dd({ isInternal: !1 }), Td = Dd({ isInternal: !0 });
L0.displayName = "BezierEdge";
Td.displayName = "BezierEdgeInternal";
const kc = {
  default: Td,
  straight: _d,
  step: Id,
  smoothstep: kd,
  simplebezier: Nd
}, Ec = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, z0 = (e, t, n) => n === ae.Left ? e - t : n === ae.Right ? e + t : e, V0 = (e, t, n) => n === ae.Top ? e - t : n === ae.Bottom ? e + t : e, Ic = "react-flow__edgeupdater";
function Ac({ position: e, centerX: t, centerY: n, radius: i = 10, onMouseDown: r, onMouseEnter: s, onMouseOut: a, type: c }) {
  return o.jsx("circle", { onMouseDown: r, onMouseEnter: s, onMouseOut: a, className: Ae([Ic, `${Ic}-${c}`]), cx: z0(t, i, e), cy: V0(n, i, e), r: i, stroke: "transparent", fill: "transparent" });
}
function O0({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: i, sourceY: r, targetX: s, targetY: a, sourcePosition: c, targetPosition: u, onReconnect: l, onReconnectStart: d, onReconnectEnd: f, setReconnecting: p, setUpdateHover: h }) {
  const m = je(), v = (y, N) => {
    if (y.button !== 0)
      return;
    const { autoPanOnConnect: j, domNode: S, connectionMode: I, connectionRadius: _, lib: M, onConnectStart: k, cancelConnection: $, nodeLookup: z, rfId: C, panBy: A, updateConnection: E } = m.getState(), D = N.type === "target", P = (W, H) => {
      p(!1), f?.(W, n, N.type, H);
    }, T = (W) => l?.(n, W), F = (W, H) => {
      p(!0), d?.(y, n, N.type), k?.(W, H);
    };
    Xo.onPointerDown(y.nativeEvent, {
      autoPanOnConnect: j,
      connectionMode: I,
      connectionRadius: _,
      domNode: S,
      handleId: N.id,
      nodeId: N.nodeId,
      nodeLookup: z,
      isTarget: D,
      edgeUpdaterType: N.type,
      lib: M,
      flowId: C,
      cancelConnection: $,
      panBy: A,
      isValidConnection: (...W) => m.getState().isValidConnection?.(...W) ?? !0,
      onConnect: T,
      onConnectStart: F,
      onConnectEnd: (...W) => m.getState().onConnectEnd?.(...W),
      onReconnectEnd: P,
      updateConnection: E,
      getTransform: () => m.getState().transform,
      getFromHandle: () => m.getState().connection.fromHandle,
      dragThreshold: m.getState().connectionDragThreshold,
      handleDomNode: y.currentTarget
    });
  }, x = (y) => v(y, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), w = (y) => v(y, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), b = () => h(!0), g = () => h(!1);
  return o.jsxs(o.Fragment, { children: [(e === !0 || e === "source") && o.jsx(Ac, { position: c, centerX: i, centerY: r, radius: t, onMouseDown: x, onMouseEnter: b, onMouseOut: g, type: "source" }), (e === !0 || e === "target") && o.jsx(Ac, { position: u, centerX: s, centerY: a, radius: t, onMouseDown: w, onMouseEnter: b, onMouseOut: g, type: "target" })] });
}
function H0({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: i, onClick: r, onDoubleClick: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, rfId: m, edgeTypes: v, noPanClassName: x, onError: w, disableKeyboardA11y: b }) {
  let g = he((te) => te.edgeLookup.get(e));
  const y = he((te) => te.defaultEdgeOptions);
  g = y ? { ...y, ...g } : g;
  let N = g.type || "default", j = v?.[N] || kc[N];
  j === void 0 && (w?.("011", Ke.error011(N)), N = "default", j = v?.default || kc.default);
  const S = !!(g.focusable || t && typeof g.focusable > "u"), I = typeof f < "u" && (g.reconnectable || n && typeof g.reconnectable > "u"), _ = !!(g.selectable || i && typeof g.selectable > "u"), M = oe(null), [k, $] = K(!1), [z, C] = K(!1), A = je(), { zIndex: E, sourceX: D, sourceY: P, targetX: T, targetY: F, sourcePosition: W, targetPosition: H } = he(se((te) => {
    const ie = te.nodeLookup.get(g.source), fe = te.nodeLookup.get(g.target);
    if (!ie || !fe)
      return {
        zIndex: g.zIndex,
        ...Ec
      };
    const V = Aw({
      id: e,
      sourceNode: ie,
      targetNode: fe,
      sourceHandle: g.sourceHandle || null,
      targetHandle: g.targetHandle || null,
      connectionMode: te.connectionMode,
      onError: w
    });
    return {
      zIndex: bw({
        selected: g.selected,
        zIndex: g.zIndex,
        sourceNode: ie,
        targetNode: fe,
        elevateOnSelect: te.elevateEdgesOnSelect,
        zIndexMode: te.zIndexMode
      }),
      ...V || Ec
    };
  }, [g.source, g.target, g.sourceHandle, g.targetHandle, g.selected, g.zIndex]), ve), U = ue(() => g.markerStart ? `url('#${Bo(g.markerStart, m)}')` : void 0, [g.markerStart, m]), O = ue(() => g.markerEnd ? `url('#${Bo(g.markerEnd, m)}')` : void 0, [g.markerEnd, m]);
  if (g.hidden || D === null || P === null || T === null || F === null)
    return null;
  const q = (te) => {
    const { addSelectedEdges: ie, unselectNodesAndEdges: fe, multiSelectionActive: V } = A.getState();
    _ && (A.setState({ nodesSelectionActive: !1 }), g.selected && V ? (fe({ nodes: [], edges: [g] }), M.current?.blur()) : ie([e])), r && r(te, g);
  }, J = s ? (te) => {
    s(te, { ...g });
  } : void 0, G = a ? (te) => {
    a(te, { ...g });
  } : void 0, R = c ? (te) => {
    c(te, { ...g });
  } : void 0, X = u ? (te) => {
    u(te, { ...g });
  } : void 0, de = l ? (te) => {
    l(te, { ...g });
  } : void 0, le = (te) => {
    if (!b && ku.includes(te.key) && _) {
      const { unselectNodesAndEdges: ie, addSelectedEdges: fe } = A.getState();
      te.key === "Escape" ? (M.current?.blur(), ie({ edges: [g] })) : fe([e]);
    }
  };
  return o.jsx("svg", { style: { zIndex: E }, children: o.jsxs("g", { className: Ae([
    "react-flow__edge",
    `react-flow__edge-${N}`,
    g.className,
    x,
    {
      selected: g.selected,
      animated: g.animated,
      inactive: !_ && !r,
      updating: k,
      selectable: _
    }
  ]), onClick: q, onDoubleClick: J, onContextMenu: G, onMouseEnter: R, onMouseMove: X, onMouseLeave: de, onKeyDown: S ? le : void 0, tabIndex: S ? 0 : void 0, role: g.ariaRole ?? (S ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": g.ariaLabel === null ? void 0 : g.ariaLabel || `Edge from ${g.source} to ${g.target}`, "aria-describedby": S ? `${od}-${m}` : void 0, ref: M, ...g.domAttributes, children: [!z && o.jsx(j, { id: e, source: g.source, target: g.target, type: g.type, selected: g.selected, animated: g.animated, selectable: _, deletable: g.deletable ?? !0, label: g.label, labelStyle: g.labelStyle, labelShowBg: g.labelShowBg, labelBgStyle: g.labelBgStyle, labelBgPadding: g.labelBgPadding, labelBgBorderRadius: g.labelBgBorderRadius, sourceX: D, sourceY: P, targetX: T, targetY: F, sourcePosition: W, targetPosition: H, data: g.data, style: g.style, sourceHandleId: g.sourceHandle, targetHandleId: g.targetHandle, markerStart: U, markerEnd: O, pathOptions: "pathOptions" in g ? g.pathOptions : void 0, interactionWidth: g.interactionWidth }), I && o.jsx(O0, { edge: g, isReconnectable: I, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, sourceX: D, sourceY: P, targetX: T, targetY: F, sourcePosition: W, targetPosition: H, setUpdateHover: $, setReconnecting: C })] }) });
}
var W0 = Ee(H0);
const F0 = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function $d({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: i, noPanClassName: r, onReconnect: s, onEdgeContextMenu: a, onEdgeMouseEnter: c, onEdgeMouseMove: u, onEdgeMouseLeave: l, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: p, onReconnectStart: h, onReconnectEnd: m, disableKeyboardA11y: v }) {
  const { edgesFocusable: x, edgesReconnectable: w, elementsSelectable: b, onError: g } = he(F0, ve), y = E0(t);
  return o.jsxs("div", { className: "react-flow__edges", children: [o.jsx(T0, { defaultColor: e, rfId: n }), y.map((N) => o.jsx(W0, { id: N, edgesFocusable: x, edgesReconnectable: w, elementsSelectable: b, noPanClassName: r, onReconnect: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, onClick: d, reconnectRadius: f, onDoubleClick: p, onReconnectStart: h, onReconnectEnd: m, rfId: n, onError: g, edgeTypes: i, disableKeyboardA11y: v }, N))] });
}
$d.displayName = "EdgeRenderer";
const B0 = Ee($d), K0 = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function X0({ children: e }) {
  const t = he(K0);
  return o.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function q0(e) {
  const t = Ps(), n = oe(!1);
  ee(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Y0 = (e) => e.panZoom?.syncViewport;
function U0(e) {
  const t = he(Y0), n = je();
  return ee(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function Z0(e) {
  return e.connection.inProgress ? { ...e.connection, to: dn(e.connection.to, e.transform) } : { ...e.connection };
}
function G0(e) {
  return Z0;
}
function J0(e) {
  const t = G0();
  return he(t, ve);
}
const Q0 = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function eb({ containerStyle: e, style: t, type: n, component: i }) {
  const { nodesConnectable: r, width: s, height: a, isValid: c, inProgress: u } = he(Q0, ve);
  return !(s && r && u) ? null : o.jsx("svg", { style: e, width: s, height: a, className: "react-flow__connectionline react-flow__container", children: o.jsx("g", { className: Ae(["react-flow__connection", Au(c)]), children: o.jsx(Pd, { style: t, type: n, CustomComponent: i, isValid: c }) }) });
}
const Pd = ({ style: e, type: t = yt.Bezier, CustomComponent: n, isValid: i }) => {
  const { inProgress: r, from: s, fromNode: a, fromHandle: c, fromPosition: u, to: l, toNode: d, toHandle: f, toPosition: p, pointer: h } = J0();
  if (!r)
    return;
  if (n)
    return o.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: a, fromHandle: c, fromX: s.x, fromY: s.y, toX: l.x, toY: l.y, fromPosition: u, toPosition: p, connectionStatus: Au(i), toNode: d, toHandle: f, pointer: h });
  let m = "";
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
      [m] = Hu(v);
      break;
    case yt.SimpleBezier:
      [m] = bd(v);
      break;
    case yt.Step:
      [m] = or({
        ...v,
        borderRadius: 0
      });
      break;
    case yt.SmoothStep:
      [m] = or(v);
      break;
    default:
      [m] = Bu(v);
  }
  return o.jsx("path", { d: m, fill: "none", className: "react-flow__connection-path", style: e });
};
Pd.displayName = "ConnectionLine";
const tb = {};
function _c(e = tb) {
  oe(e), je(), ee(() => {
  }, [e]);
}
function nb() {
  je(), oe(!1), ee(() => {
  }, []);
}
function Md({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: i, onEdgeClick: r, onNodeDoubleClick: s, onEdgeDoubleClick: a, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: p, onSelectionEnd: h, connectionLineType: m, connectionLineStyle: v, connectionLineComponent: x, connectionLineContainerStyle: w, selectionKeyCode: b, selectionOnDrag: g, selectionMode: y, multiSelectionKeyCode: N, panActivationKeyCode: j, zoomActivationKeyCode: S, deleteKeyCode: I, onlyRenderVisibleElements: _, elementsSelectable: M, defaultViewport: k, translateExtent: $, minZoom: z, maxZoom: C, preventScrolling: A, defaultMarkerColor: E, zoomOnScroll: D, zoomOnPinch: P, panOnScroll: T, panOnScrollSpeed: F, panOnScrollMode: W, zoomOnDoubleClick: H, panOnDrag: U, autoPanOnSelection: O, onPaneClick: q, onPaneMouseEnter: J, onPaneMouseMove: G, onPaneMouseLeave: R, onPaneScroll: X, onPaneContextMenu: de, paneClickDistance: le, nodeClickDistance: te, onEdgeContextMenu: ie, onEdgeMouseEnter: fe, onEdgeMouseMove: V, onEdgeMouseLeave: ne, reconnectRadius: ge, onReconnect: ye, onReconnectStart: Ie, onReconnectEnd: _e, noDragClassName: Ve, noWheelClassName: at, noPanClassName: Ye, disableKeyboardA11y: Ue, nodeExtent: Me, rfId: Oe, viewport: Ne, onViewportChange: Xe }) {
  return _c(e), _c(t), nb(), q0(n), U0(Ne), o.jsx(y0, { onPaneClick: q, onPaneMouseEnter: J, onPaneMouseMove: G, onPaneMouseLeave: R, onPaneContextMenu: de, onPaneScroll: X, paneClickDistance: le, deleteKeyCode: I, selectionKeyCode: b, selectionOnDrag: g, selectionMode: y, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: N, panActivationKeyCode: j, zoomActivationKeyCode: S, elementsSelectable: M, zoomOnScroll: D, zoomOnPinch: P, zoomOnDoubleClick: H, panOnScroll: T, panOnScrollSpeed: F, panOnScrollMode: W, panOnDrag: U, autoPanOnSelection: O, defaultViewport: k, translateExtent: $, minZoom: z, maxZoom: C, onSelectionContextMenu: f, preventScrolling: A, noDragClassName: Ve, noWheelClassName: at, noPanClassName: Ye, disableKeyboardA11y: Ue, onViewportChange: Xe, isControlledViewport: !!Ne, children: o.jsxs(X0, { children: [o.jsx(B0, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: a, onReconnect: ye, onReconnectStart: Ie, onReconnectEnd: _e, onlyRenderVisibleElements: _, onEdgeContextMenu: ie, onEdgeMouseEnter: fe, onEdgeMouseMove: V, onEdgeMouseLeave: ne, reconnectRadius: ge, defaultMarkerColor: E, noPanClassName: Ye, disableKeyboardA11y: Ue, rfId: Oe }), o.jsx(eb, { style: v, type: m, component: x, containerStyle: w }), o.jsx("div", { className: "react-flow__edgelabel-renderer" }), o.jsx(k0, { nodeTypes: e, onNodeClick: i, onNodeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, nodeClickDistance: te, onlyRenderVisibleElements: _, noPanClassName: Ye, noDragClassName: Ve, disableKeyboardA11y: Ue, nodeExtent: Me, rfId: Oe }), o.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Md.displayName = "GraphView";
const ib = Ee(Md), rb = Pu(), Dc = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, width: r, height: s, fitView: a, fitViewOptions: c, minZoom: u = 0.5, maxZoom: l = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: p = "basic" } = {}) => {
  const h = /* @__PURE__ */ new Map(), m = /* @__PURE__ */ new Map(), v = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), w = i ?? t ?? [], b = n ?? e ?? [], g = d ?? [0, 0], y = f ?? Bn;
  qu(v, x, w);
  const { nodesInitialized: N } = Ko(b, h, m, {
    nodeOrigin: g,
    nodeExtent: y,
    zIndexMode: p
  });
  let j = [0, 0, 1];
  if (a && r && s) {
    const S = ni(h, {
      filter: (k) => !!((k.width || k.initialWidth) && (k.height || k.initialHeight))
    }), { x: I, y: _, zoom: M } = Es(S, r, s, u, l, c?.padding ?? 0.1);
    j = [I, _, M];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: s ?? 0,
    transform: j,
    nodes: b,
    nodesInitialized: N,
    nodeLookup: h,
    parentLookup: m,
    edges: w,
    edgeLookup: x,
    connectionLookup: v,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: i !== void 0,
    panZoom: null,
    minZoom: u,
    maxZoom: l,
    translateExtent: Bn,
    nodeExtent: y,
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
    connection: { ...Iu },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: rb,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Eu,
    zIndexMode: p,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, ob = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, width: r, height: s, fitView: a, fitViewOptions: c, minZoom: u, maxZoom: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p }) => xv((h, m) => {
  async function v() {
    const { nodeLookup: x, panZoom: w, fitViewOptions: b, fitViewResolver: g, width: y, height: N, minZoom: j, maxZoom: S } = m();
    w && (await hw({
      nodes: x,
      width: y,
      height: N,
      panZoom: w,
      minZoom: j,
      maxZoom: S
    }, b), g?.resolve(!0), h({ fitViewResolver: null }));
  }
  return {
    ...Dc({
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
      const { nodeLookup: w, parentLookup: b, nodeOrigin: g, elevateNodesOnSelect: y, fitViewQueued: N, zIndexMode: j, nodesSelectionActive: S } = m(), { nodesInitialized: I, hasSelectedNodes: _ } = Ko(x, w, b, {
        nodeOrigin: g,
        nodeExtent: f,
        elevateNodesOnSelect: y,
        checkEquality: !0,
        zIndexMode: j
      }), M = S && _;
      N && I ? (v(), h({
        nodes: x,
        nodesInitialized: I,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: M
      })) : h({ nodes: x, nodesInitialized: I, nodesSelectionActive: M });
    },
    setEdges: (x) => {
      const { connectionLookup: w, edgeLookup: b } = m();
      qu(w, b, x), h({ edges: x });
    },
    setDefaultNodesAndEdges: (x, w) => {
      if (x) {
        const { setNodes: b } = m();
        b(x), h({ hasDefaultNodes: !0 });
      }
      if (w) {
        const { setEdges: b } = m();
        b(w), h({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (x) => {
      const { triggerNodeChanges: w, nodeLookup: b, parentLookup: g, domNode: y, nodeOrigin: N, nodeExtent: j, debug: S, fitViewQueued: I, zIndexMode: _ } = m(), { changes: M, updatedInternals: k } = Lw(x, b, g, y, N, j, _);
      k && ($w(b, g, { nodeOrigin: N, nodeExtent: j, zIndexMode: _ }), I ? (v(), h({ fitViewQueued: !1, fitViewOptions: void 0 })) : h({}), M?.length > 0 && (S && console.log("React Flow: trigger node changes", M), w?.(M)));
    },
    updateNodePositions: (x, w = !1) => {
      const b = [];
      let g = [];
      const { nodeLookup: y, triggerNodeChanges: N, connection: j, updateConnection: S, onNodesChangeMiddlewareMap: I } = m();
      for (const [_, M] of x) {
        const k = y.get(_), $ = !!(k?.expandParent && k?.parentId && M?.position), z = {
          id: _,
          type: "position",
          position: $ ? {
            x: Math.max(0, M.position.x),
            y: Math.max(0, M.position.y)
          } : M.position,
          dragging: w
        };
        if (k && j.inProgress && j.fromNode.id === k.id) {
          const C = Mt(k, j.fromHandle, ae.Left, !0);
          S({ ...j, from: C });
        }
        $ && k.parentId && b.push({
          id: _,
          parentId: k.parentId,
          rect: {
            ...M.internals.positionAbsolute,
            width: M.measured.width ?? 0,
            height: M.measured.height ?? 0
          }
        }), g.push(z);
      }
      if (b.length > 0) {
        const { parentLookup: _, nodeOrigin: M } = m(), k = $s(b, y, _, M);
        g.push(...k);
      }
      for (const _ of I.values())
        g = _(g);
      N(g);
    },
    triggerNodeChanges: (x) => {
      const { onNodesChange: w, setNodes: b, nodes: g, hasDefaultNodes: y, debug: N } = m();
      if (x?.length) {
        if (y) {
          const j = cd(x, g);
          b(j);
        }
        N && console.log("React Flow: trigger node changes", x), w?.(x);
      }
    },
    triggerEdgeChanges: (x) => {
      const { onEdgesChange: w, setEdges: b, edges: g, hasDefaultEdges: y, debug: N } = m();
      if (x?.length) {
        if (y) {
          const j = ld(x, g);
          b(j);
        }
        N && console.log("React Flow: trigger edge changes", x), w?.(x);
      }
    },
    addSelectedNodes: (x) => {
      const { multiSelectionActive: w, edgeLookup: b, nodeLookup: g, triggerNodeChanges: y, triggerEdgeChanges: N } = m();
      if (w) {
        const j = x.map((S) => St(S, !0));
        y(j);
        return;
      }
      y(Kt(g, /* @__PURE__ */ new Set([...x]), !0)), N(Kt(b));
    },
    addSelectedEdges: (x) => {
      const { multiSelectionActive: w, edgeLookup: b, nodeLookup: g, triggerNodeChanges: y, triggerEdgeChanges: N } = m();
      if (w) {
        const j = x.map((S) => St(S, !0));
        N(j);
        return;
      }
      N(Kt(b, /* @__PURE__ */ new Set([...x]))), y(Kt(g, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: x, edges: w } = {}) => {
      const { edges: b, nodes: g, nodeLookup: y, triggerNodeChanges: N, triggerEdgeChanges: j } = m(), S = x || g, I = w || b, _ = [];
      for (const k of S) {
        if (!k.selected)
          continue;
        const $ = y.get(k.id);
        $ && ($.selected = !1), _.push(St(k.id, !1));
      }
      const M = [];
      for (const k of I)
        k.selected && M.push(St(k.id, !1));
      N(_), j(M);
    },
    setMinZoom: (x) => {
      const { panZoom: w, maxZoom: b } = m();
      w?.setScaleExtent([x, b]), h({ minZoom: x });
    },
    setMaxZoom: (x) => {
      const { panZoom: w, minZoom: b } = m();
      w?.setScaleExtent([b, x]), h({ maxZoom: x });
    },
    setTranslateExtent: (x) => {
      m().panZoom?.setTranslateExtent(x), h({ translateExtent: x });
    },
    resetSelectedElements: () => {
      const { edges: x, nodes: w, triggerNodeChanges: b, triggerEdgeChanges: g, elementsSelectable: y } = m();
      if (!y)
        return;
      const N = w.reduce((S, I) => I.selected ? [...S, St(I.id, !1)] : S, []), j = x.reduce((S, I) => I.selected ? [...S, St(I.id, !1)] : S, []);
      b(N), g(j);
    },
    setNodeExtent: (x) => {
      const { nodes: w, nodeLookup: b, parentLookup: g, nodeOrigin: y, elevateNodesOnSelect: N, nodeExtent: j, zIndexMode: S } = m();
      x[0][0] === j[0][0] && x[0][1] === j[0][1] && x[1][0] === j[1][0] && x[1][1] === j[1][1] || (Ko(w, b, g, {
        nodeOrigin: y,
        nodeExtent: x,
        elevateNodesOnSelect: N,
        checkEquality: !1,
        zIndexMode: S
      }), h({ nodeExtent: x }));
    },
    panBy: (x) => {
      const { transform: w, width: b, height: g, panZoom: y, translateExtent: N } = m();
      return zw({ delta: x, panZoom: y, transform: w, translateExtent: N, width: b, height: g });
    },
    setCenter: async (x, w, b) => {
      const { width: g, height: y, maxZoom: N, panZoom: j } = m();
      if (!j)
        return !1;
      const S = typeof b?.zoom < "u" ? b.zoom : N;
      return await j.setViewport({
        x: g / 2 - x * S,
        y: y / 2 - w * S,
        zoom: S
      }, { duration: b?.duration, ease: b?.ease, interpolate: b?.interpolate }), !0;
    },
    cancelConnection: () => {
      h({
        connection: { ...Iu }
      });
    },
    updateConnection: (x) => {
      h({ connection: x });
    },
    reset: () => h({ ...Dc() })
  };
}, Object.is);
function sb({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: i, initialWidth: r, initialHeight: s, initialMinZoom: a, initialMaxZoom: c, initialFitViewOptions: u, fitView: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p, children: h }) {
  const [m] = K(() => ob({
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
  return o.jsx(jv, { value: m, children: o.jsx(Xv, { children: h }) });
}
function ab({ children: e, nodes: t, edges: n, defaultNodes: i, defaultEdges: r, width: s, height: a, fitView: c, fitViewOptions: u, minZoom: l, maxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h }) {
  return Un(jr) ? o.jsx(o.Fragment, { children: e }) : o.jsx(sb, { initialNodes: t, initialEdges: n, defaultNodes: i, defaultEdges: r, initialWidth: s, initialHeight: a, fitView: c, initialFitViewOptions: u, initialMinZoom: l, initialMaxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h, children: e });
}
const cb = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function lb({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, className: r, nodeTypes: s, edgeTypes: a, onNodeClick: c, onEdgeClick: u, onInit: l, onMove: d, onMoveStart: f, onMoveEnd: p, onConnect: h, onConnectStart: m, onConnectEnd: v, onClickConnectStart: x, onClickConnectEnd: w, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: y, onNodeContextMenu: N, onNodeDoubleClick: j, onNodeDragStart: S, onNodeDrag: I, onNodeDragStop: _, onNodesDelete: M, onEdgesDelete: k, onDelete: $, onSelectionChange: z, onSelectionDragStart: C, onSelectionDrag: A, onSelectionDragStop: E, onSelectionContextMenu: D, onSelectionStart: P, onSelectionEnd: T, onBeforeDelete: F, connectionMode: W, connectionLineType: H = yt.Bezier, connectionLineStyle: U, connectionLineComponent: O, connectionLineContainerStyle: q, deleteKeyCode: J = "Backspace", selectionKeyCode: G = "Shift", selectionOnDrag: R = !1, selectionMode: X = Kn.Full, panActivationKeyCode: de = "Space", multiSelectionKeyCode: le = qn() ? "Meta" : "Control", zoomActivationKeyCode: te = qn() ? "Meta" : "Control", snapToGrid: ie, snapGrid: fe, onlyRenderVisibleElements: V = !1, selectNodesOnDrag: ne, nodesDraggable: ge, autoPanOnNodeFocus: ye, nodesConnectable: Ie, nodesFocusable: _e, nodeOrigin: Ve = sd, edgesFocusable: at, edgesReconnectable: Ye, elementsSelectable: Ue = !0, defaultViewport: Me = Mv, minZoom: Oe = 0.5, maxZoom: Ne = 2, translateExtent: Xe = Bn, preventScrolling: De = !0, nodeExtent: Nt, defaultMarkerColor: pn = "#b1b1b7", zoomOnScroll: L = !0, zoomOnPinch: B = !0, panOnScroll: Y = !1, panOnScrollSpeed: re = 0.5, panOnScrollMode: Q = Et.Free, zoomOnDoubleClick: xe = !0, panOnDrag: pe = !0, onPaneClick: we, onPaneMouseEnter: Se, onPaneMouseMove: qe, onPaneMouseLeave: Te, onPaneScroll: Er, onPaneContextMenu: tt, paneClickDistance: Ir = 1, nodeClickDistance: si = 0, children: Ar, onReconnect: _r, onReconnectStart: Dr, onReconnectEnd: Tr, onEdgeContextMenu: ct, onEdgeDoubleClick: $r, onEdgeMouseEnter: Vt, onEdgeMouseMove: Pr, onEdgeMouseLeave: Ot, reconnectRadius: Mr = 10, onNodesChange: ai, onEdgesChange: ci, noDragClassName: Rr = "nodrag", noWheelClassName: Ht = "nowheel", noPanClassName: li = "nopan", fitView: ui, fitViewOptions: di, connectOnClick: hn, attributionPosition: Lr, proOptions: zr, defaultEdgeOptions: Vr, elevateNodesOnSelect: Or = !0, elevateEdgesOnSelect: Hr = !1, disableKeyboardA11y: fi = !1, autoPanOnConnect: Wr, autoPanOnNodeDrag: Fr, autoPanOnSelection: Br = !0, autoPanSpeed: Kr, connectionRadius: Xr, isValidConnection: qr, onError: Yr, style: Ur, id: gn, nodeDragThreshold: pi, connectionDragThreshold: hi, viewport: gi, onViewportChange: Zr, width: mi, height: Gr, colorMode: Jr = "light", debug: Qr, onScroll: yi, ariaLabelConfig: eo, zIndexMode: xi = "basic", ...to }, no) {
  const mn = gn || "1", io = Vv(Jr), ro = se((wi) => {
    wi.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), yi?.(wi);
  }, [yi]);
  return o.jsx("div", { "data-testid": "rf__wrapper", ...to, onScroll: ro, style: { ...Ur, ...cb }, ref: no, className: Ae(["react-flow", r, io]), id: gn, role: "application", children: o.jsxs(ab, { nodes: e, edges: t, width: mi, height: Gr, fitView: ui, fitViewOptions: di, minZoom: Oe, maxZoom: Ne, nodeOrigin: Ve, nodeExtent: Nt, zIndexMode: xi, children: [o.jsx(zv, { nodes: e, edges: t, defaultNodes: n, defaultEdges: i, onConnect: h, onConnectStart: m, onConnectEnd: v, onClickConnectStart: x, onClickConnectEnd: w, nodesDraggable: ge, autoPanOnNodeFocus: ye, nodesConnectable: Ie, nodesFocusable: _e, edgesFocusable: at, edgesReconnectable: Ye, elementsSelectable: Ue, elevateNodesOnSelect: Or, elevateEdgesOnSelect: Hr, minZoom: Oe, maxZoom: Ne, nodeExtent: Nt, onNodesChange: ai, onEdgesChange: ci, snapToGrid: ie, snapGrid: fe, connectionMode: W, translateExtent: Xe, connectOnClick: hn, defaultEdgeOptions: Vr, fitView: ui, fitViewOptions: di, onNodesDelete: M, onEdgesDelete: k, onDelete: $, onNodeDragStart: S, onNodeDrag: I, onNodeDragStop: _, onSelectionDrag: A, onSelectionDragStart: C, onSelectionDragStop: E, onMove: d, onMoveStart: f, onMoveEnd: p, noPanClassName: li, nodeOrigin: Ve, rfId: mn, autoPanOnConnect: Wr, autoPanOnNodeDrag: Fr, autoPanSpeed: Kr, onError: Yr, connectionRadius: Xr, isValidConnection: qr, selectNodesOnDrag: ne, nodeDragThreshold: pi, connectionDragThreshold: hi, onBeforeDelete: F, debug: Qr, ariaLabelConfig: eo, zIndexMode: xi }), o.jsx(ib, { onInit: l, onNodeClick: c, onEdgeClick: u, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: y, onNodeContextMenu: N, onNodeDoubleClick: j, nodeTypes: s, edgeTypes: a, connectionLineType: H, connectionLineStyle: U, connectionLineComponent: O, connectionLineContainerStyle: q, selectionKeyCode: G, selectionOnDrag: R, selectionMode: X, deleteKeyCode: J, multiSelectionKeyCode: le, panActivationKeyCode: de, zoomActivationKeyCode: te, onlyRenderVisibleElements: V, defaultViewport: Me, translateExtent: Xe, minZoom: Oe, maxZoom: Ne, preventScrolling: De, zoomOnScroll: L, zoomOnPinch: B, zoomOnDoubleClick: xe, panOnScroll: Y, panOnScrollSpeed: re, panOnScrollMode: Q, panOnDrag: pe, autoPanOnSelection: Br, onPaneClick: we, onPaneMouseEnter: Se, onPaneMouseMove: qe, onPaneMouseLeave: Te, onPaneScroll: Er, onPaneContextMenu: tt, paneClickDistance: Ir, nodeClickDistance: si, onSelectionContextMenu: D, onSelectionStart: P, onSelectionEnd: T, onReconnect: _r, onReconnectStart: Dr, onReconnectEnd: Tr, onEdgeContextMenu: ct, onEdgeDoubleClick: $r, onEdgeMouseEnter: Vt, onEdgeMouseMove: Pr, onEdgeMouseLeave: Ot, reconnectRadius: Mr, defaultMarkerColor: pn, noDragClassName: Rr, noWheelClassName: Ht, noPanClassName: li, rfId: mn, disableKeyboardA11y: fi, nodeExtent: Nt, viewport: gi, onViewportChange: Zr }), o.jsx(Pv, { onSelectionChange: z }), Ar, o.jsx(Av, { proOptions: zr, position: Lr }), o.jsx(Iv, { rfId: mn, disableKeyboardA11y: fi })] }) });
}
var Rd = fd(lb);
const ub = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function db({ children: e }) {
  const t = he(ub);
  return t ? bv.createPortal(e, t) : null;
}
function fb({ dimensions: e, lineWidth: t, variant: n, className: i }) {
  return o.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ae(["react-flow__background-pattern", n, i]) });
}
function pb({ radius: e, className: t }) {
  return o.jsx("circle", { cx: e, cy: e, r: e, className: Ae(["react-flow__background-pattern", "dots", t]) });
}
var vt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(vt || (vt = {}));
const hb = {
  [vt.Dots]: 1,
  [vt.Lines]: 1,
  [vt.Cross]: 6
}, gb = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function Ld({
  id: e,
  variant: t = vt.Dots,
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
  const f = oe(null), { transform: p, patternId: h } = he(gb, ve), m = i || hb[t], v = t === vt.Dots, x = t === vt.Cross, w = Array.isArray(n) ? n : [n, n], b = [w[0] * p[2] || 1, w[1] * p[2] || 1], g = m * p[2], y = Array.isArray(s) ? s : [s, s], N = x ? [g, g] : b, j = [
    y[0] * p[2] || 1 + N[0] / 2,
    y[1] * p[2] || 1 + N[1] / 2
  ], S = `${h}${e || ""}`;
  return o.jsxs("svg", { className: Ae(["react-flow__background", l]), style: {
    ...u,
    ...Sr,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": a
  }, ref: f, "data-testid": "rf__background", children: [o.jsx("pattern", { id: S, x: p[0] % b[0], y: p[1] % b[1], width: b[0], height: b[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${j[0]},-${j[1]})`, children: v ? o.jsx(pb, { radius: g / 2, className: d }) : o.jsx(fb, { dimensions: N, lineWidth: r, variant: t, className: d }) }), o.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${S})` })] });
}
Ld.displayName = "Background";
const zd = Ee(Ld);
function mb() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: o.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function yb() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: o.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function xb() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: o.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function wb() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: o.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function vb() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: o.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function Di({ children: e, className: t, ...n }) {
  return o.jsx("button", { type: "button", className: Ae(["react-flow__controls-button", t]), ...n, children: e });
}
const bb = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function Vd({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: i = !0, fitViewOptions: r, onZoomIn: s, onZoomOut: a, onFitView: c, onInteractiveChange: u, className: l, children: d, position: f = "bottom-left", orientation: p = "vertical", "aria-label": h }) {
  const m = je(), { isInteractive: v, minZoomReached: x, maxZoomReached: w, ariaLabelConfig: b } = he(bb, ve), { zoomIn: g, zoomOut: y, fitView: N } = Ps(), j = () => {
    g(), s?.();
  }, S = () => {
    y(), a?.();
  }, I = () => {
    N(r), c?.();
  }, _ = () => {
    m.setState({
      nodesDraggable: !v,
      nodesConnectable: !v,
      elementsSelectable: !v
    }), u?.(!v);
  }, M = p === "horizontal" ? "horizontal" : "vertical";
  return o.jsxs(Nr, { className: Ae(["react-flow__controls", M, l]), position: f, style: e, "data-testid": "rf__controls", "aria-label": h ?? b["controls.ariaLabel"], children: [t && o.jsxs(o.Fragment, { children: [o.jsx(Di, { onClick: j, className: "react-flow__controls-zoomin", title: b["controls.zoomIn.ariaLabel"], "aria-label": b["controls.zoomIn.ariaLabel"], disabled: w, children: o.jsx(mb, {}) }), o.jsx(Di, { onClick: S, className: "react-flow__controls-zoomout", title: b["controls.zoomOut.ariaLabel"], "aria-label": b["controls.zoomOut.ariaLabel"], disabled: x, children: o.jsx(yb, {}) })] }), n && o.jsx(Di, { className: "react-flow__controls-fitview", onClick: I, title: b["controls.fitView.ariaLabel"], "aria-label": b["controls.fitView.ariaLabel"], children: o.jsx(xb, {}) }), i && o.jsx(Di, { className: "react-flow__controls-interactive", onClick: _, title: b["controls.interactive.ariaLabel"], "aria-label": b["controls.interactive.ariaLabel"], children: v ? o.jsx(vb, {}) : o.jsx(wb, {}) }), d] });
}
Vd.displayName = "Controls";
const Od = Ee(Vd);
function jb({ id: e, x: t, y: n, width: i, height: r, style: s, color: a, strokeColor: c, strokeWidth: u, className: l, borderRadius: d, shapeRendering: f, selected: p, onClick: h }) {
  const { background: m, backgroundColor: v } = s || {}, x = a || m || v;
  return o.jsx("rect", { className: Ae(["react-flow__minimap-node", { selected: p }, l]), x: t, y: n, rx: d, ry: d, width: i, height: r, style: {
    fill: x,
    stroke: c,
    strokeWidth: u
  }, shapeRendering: f, onClick: h ? (w) => h(w, e) : void 0 });
}
const Nb = Ee(jb), Sb = (e) => e.nodes.map((t) => t.id), Io = (e) => e instanceof Function ? e : () => e;
function Cb({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: i = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: s = Nb,
  onClick: a
}) {
  const c = he(Sb, ve), u = Io(t), l = Io(e), d = Io(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return o.jsx(o.Fragment, { children: c.map((p) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    o.jsx(Eb, { id: p, nodeColorFunc: u, nodeStrokeColorFunc: l, nodeClassNameFunc: d, nodeBorderRadius: i, nodeStrokeWidth: r, NodeComponent: s, onClick: a, shapeRendering: f }, p)
  )) });
}
function kb({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: i, nodeBorderRadius: r, nodeStrokeWidth: s, shapeRendering: a, NodeComponent: c, onClick: u }) {
  const { node: l, x: d, y: f, width: p, height: h } = he((m) => {
    const v = m.nodeLookup.get(e);
    if (!v)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const x = v.internals.userNode, { x: w, y: b } = v.internals.positionAbsolute, { width: g, height: y } = ht(x);
    return {
      node: x,
      x: w,
      y: b,
      width: g,
      height: y
    };
  }, ve);
  return !l || l.hidden || !Mu(l) ? null : o.jsx(c, { x: d, y: f, width: p, height: h, style: l.style, selected: !!l.selected, className: i(l), color: t(l), borderRadius: r, strokeColor: n(l), strokeWidth: s, shapeRendering: a, onClick: u, id: l.id });
}
const Eb = Ee(kb);
var Ib = Ee(Cb);
const Ab = 200, _b = 150, Db = (e) => !e.hidden, Tb = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? $u(ni(e.nodeLookup, { filter: Db }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, $b = "react-flow__minimap-desc";
function Hd({
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
  onNodeClick: m,
  pannable: v = !1,
  zoomable: x = !1,
  ariaLabel: w,
  inversePan: b,
  zoomStep: g = 1,
  offsetScale: y = 5
}) {
  const N = je(), j = oe(null), { boundingRect: S, viewBB: I, rfId: _, panZoom: M, translateExtent: k, flowWidth: $, flowHeight: z, ariaLabelConfig: C } = he(Tb, ve), A = e?.width ?? Ab, E = e?.height ?? _b, D = S.width / A, P = S.height / E, T = Math.max(D, P), F = T * A, W = T * E, H = y * T, U = S.x - (F - S.width) / 2 - H, O = S.y - (W - S.height) / 2 - H, q = F + H * 2, J = W + H * 2, G = `${$b}-${_}`, R = oe(0), X = oe();
  R.current = T, ee(() => {
    if (j.current && M)
      return X.current = qw({
        domNode: j.current,
        panZoom: M,
        getTransform: () => N.getState().transform,
        getViewScale: () => R.current
      }), () => {
        X.current?.destroy();
      };
  }, [M]), ee(() => {
    X.current?.update({
      translateExtent: k,
      width: $,
      height: z,
      inversePan: b,
      pannable: v,
      zoomStep: g,
      zoomable: x
    });
  }, [v, x, b, g, k, $, z]);
  const de = h ? (ie) => {
    const [fe, V] = X.current?.pointer(ie) || [0, 0];
    h(ie, { x: fe, y: V });
  } : void 0, le = m ? se((ie, fe) => {
    const V = N.getState().nodeLookup.get(fe).internals.userNode;
    m(ie, V);
  }, []) : void 0, te = w ?? C["minimap.ariaLabel"];
  return o.jsx(Nr, { position: p, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * T : void 0,
    "--xy-minimap-node-background-color-props": typeof i == "string" ? i : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof a == "number" ? a : void 0
  }, className: Ae(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: o.jsxs("svg", { width: A, height: E, viewBox: `${U} ${O} ${q} ${J}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": G, ref: j, onClick: de, children: [te && o.jsx("title", { id: G, children: te }), o.jsx(Ib, { onClick: le, nodeColor: i, nodeStrokeColor: n, nodeBorderRadius: s, nodeClassName: r, nodeStrokeWidth: a, nodeComponent: c }), o.jsx("path", { className: "react-flow__minimap-mask", d: `M${U - H},${O - H}h${q + H * 2}v${J + H * 2}h${-q - H * 2}z
        M${I.x},${I.y}h${I.width}v${I.height}h${-I.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
Hd.displayName = "MiniMap";
const Wd = Ee(Hd), Pb = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, Mb = {
  [on.Line]: "right",
  [on.Handle]: "bottom-right"
};
function Rb({ nodeId: e, position: t, variant: n = on.Handle, className: i, style: r = void 0, children: s, color: a, minWidth: c = 10, minHeight: u = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: p, autoScale: h = !0, shouldResize: m, onResizeStart: v, onResize: x, onResizeEnd: w }) {
  const b = md(), g = typeof e == "string" ? e : b, y = je(), N = oe(null), j = n === on.Handle, S = he(se(Pb(j && h), [j, h]), ve), I = oe(null), _ = t ?? Mb[n];
  ee(() => {
    if (!(!N.current || !g))
      return I.current || (I.current = sv({
        domNode: N.current,
        nodeId: g,
        getStoreItems: () => {
          const { nodeLookup: k, transform: $, snapGrid: z, snapToGrid: C, nodeOrigin: A, domNode: E } = y.getState();
          return {
            nodeLookup: k,
            transform: $,
            snapGrid: z,
            snapToGrid: C,
            nodeOrigin: A,
            paneDomNode: E
          };
        },
        onChange: (k, $) => {
          const { triggerNodeChanges: z, nodeLookup: C, parentLookup: A, nodeOrigin: E } = y.getState(), D = [], P = { x: k.x, y: k.y }, T = C.get(g);
          if (T && T.expandParent && T.parentId) {
            const F = T.origin ?? E, W = k.width ?? T.measured.width ?? 0, H = k.height ?? T.measured.height ?? 0, U = {
              id: T.id,
              parentId: T.parentId,
              rect: {
                width: W,
                height: H,
                ...Ru({
                  x: k.x ?? T.position.x,
                  y: k.y ?? T.position.y
                }, { width: W, height: H }, T.parentId, C, F)
              }
            }, O = $s([U], C, A, E);
            D.push(...O), P.x = k.x ? Math.max(F[0] * W, k.x) : void 0, P.y = k.y ? Math.max(F[1] * H, k.y) : void 0;
          }
          if (P.x !== void 0 && P.y !== void 0) {
            const F = {
              id: g,
              type: "position",
              position: { ...P }
            };
            D.push(F);
          }
          if (k.width !== void 0 && k.height !== void 0) {
            const W = {
              id: g,
              type: "dimensions",
              resizing: !0,
              setAttributes: p ? p === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: k.width,
                height: k.height
              }
            };
            D.push(W);
          }
          for (const F of $) {
            const W = {
              ...F,
              type: "position"
            };
            D.push(W);
          }
          z(D);
        },
        onEnd: ({ width: k, height: $ }) => {
          const z = {
            id: g,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: k,
              height: $
            }
          };
          y.getState().triggerNodeChanges([z]);
        }
      })), I.current.update({
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
        onResize: x,
        onResizeEnd: w,
        shouldResize: m
      }), () => {
        I.current?.destroy();
      };
  }, [
    _,
    c,
    u,
    l,
    d,
    f,
    v,
    x,
    w,
    m
  ]);
  const M = _.split("-");
  return o.jsx("div", { className: Ae(["react-flow__resize-control", "nodrag", ...M, n, i]), ref: N, style: {
    ...r,
    scale: S,
    ...a && { [j ? "backgroundColor" : "borderColor"]: a }
  }, children: s });
}
Ee(Rb);
function Lb(e, t) {
  return {
    ...t ? { name: t } : {},
    definitionId: e.definitionId,
    state: Gn(e.state),
    layout: e.layout
  };
}
function zb(e) {
  return JSON.stringify(
    {
      state: Gn(e.state),
      layout: e.layout
    },
    null,
    2
  );
}
function Vb(e, t) {
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
      state: ur(i.state),
      layout: i.layout ?? t.layout
    }
  };
}
function Ob(e, t) {
  const n = (t ?? "workflow").trim().replace(/[^\w.-]+/g, "-") || "workflow", i = new Blob([JSON.stringify(e, null, 2)], { type: "application/json" }), r = URL.createObjectURL(i), s = document.createElement("a");
  s.href = r, s.download = `${n}.json`, document.body.appendChild(s), s.click(), s.remove(), URL.revokeObjectURL(r);
}
function Tc({
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
function Hb({
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
    () => d ? Tf(d) : null,
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
        f ? /* @__PURE__ */ o.jsx($f, { fallback: /* @__PURE__ */ o.jsx(
          Tc,
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
          Tc,
          {
            document: e,
            readOnly: n,
            minHeight: r,
            ariaLabel: s,
            onChange: c
          }
        ),
        /* @__PURE__ */ o.jsx(Wb, { diagnostics: u })
      ]
    }
  );
}
function Wb({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ o.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const i = t.severity ?? "info", r = Fb(t);
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
function Fb(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const Bb = { language: "json", displayName: "JSON" };
function Kb({ draft: e, onApply: t }) {
  const n = ue(() => zb(e), [e]), [i, r] = K(n), [s, a] = K(n), [c, u] = K(null);
  ee(() => {
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
          /* @__PURE__ */ o.jsx(an, { size: 14 }),
          " Apply"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ o.jsx("div", { className: "wf-code-view-editor", children: /* @__PURE__ */ o.jsx(
      Hb,
      {
        ariaLabel: "Workflow JSON",
        document: { uri: "elsa://workflows/definition.json", language: "json", value: i },
        languageAdapter: Bb,
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
const Xb = ["Single", "Array", "List", "HashSet"];
function Fd(e) {
  const [t, n] = K(null), [i, r] = K(null);
  ee(() => {
    let u = !1;
    return Lh(e).then(
      (l) => {
        u || n(l);
      },
      () => {
        u || n([]);
      }
    ), Vh(e).then(
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
      const l = na(u);
      return {
        value: l,
        label: yl(u.displayName, l),
        group: u.category?.trim() || "Other"
      };
    }) : null,
    [t]
  ), a = ue(
    () => i && i.length > 0 ? i.filter((u) => !u.deprecated).map((u) => ({
      value: u.typeName,
      label: fp(u.displayName, u.typeName)
    })) : null,
    [i]
  ), c = ue(() => {
    const u = /* @__PURE__ */ new Map();
    for (const l of t ?? []) {
      const d = na(l), f = l.defaultEditor?.trim();
      d && u.set(d, f && f.length > 0 ? f : "text");
    }
    return (l) => u.get(l) ?? "text";
  }, [t]);
  return { typeOptions: s, storageOptions: a, editorForAlias: c };
}
function qb(e) {
  return !e || e.length === 0 ? void 0 : (e.find((n) => /(^|\.)String$/i.test(n.value) || n.label.toLowerCase() === "string") ?? e[0]).value;
}
function Yb(e, t, n) {
  return {
    add: () => {
      const i = np(n.namePrefix, e.map((r) => ut(r, n.nameKeys)));
      t([...e, n.create(i)]);
    },
    update: (i, r) => t(e.map((s, a) => a === i ? n.patch(s, r) : s)),
    remove: (i) => t(e.filter((r, s) => s !== i))
  };
}
function $c({ value: e, options: t, placeholder: n, allowEmpty: i, ariaLabel: r, onChange: s }) {
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
const Ub = {
  Single: "Single",
  Array: "Array",
  List: "List",
  HashSet: "Hash set"
};
function Zb({ value: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ o.jsx("select", { "aria-label": t, value: e, onChange: (i) => n(i.target.value), children: Xb.map((i) => /* @__PURE__ */ o.jsx("option", { value: i, children: Ub[i] }, i)) });
}
function Gb(e, t) {
  return t === "" ? !0 : e === "checkbox" ? t === "true" || t === "false" || t === "True" || t === "False" : e === "number" ? Number.isFinite(Number(t.trim())) && t.trim() !== "" : e === "date" ? /^\d{4}-\d{2}-\d{2}$/.test(t) : !0;
}
function Jb({ value: e, editor: t, ariaLabel: n, onChange: i }) {
  const r = Gb(t, e);
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
function Qb({ title: e, addLabel: t, emptyLabel: n, headers: i, isEmpty: r, onAdd: s, children: a }) {
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-properties-section-head", children: [
      /* @__PURE__ */ o.jsx("h3", { children: e }),
      /* @__PURE__ */ o.jsxs("button", { type: "button", className: "wf-properties-add", onClick: s, children: [
        /* @__PURE__ */ o.jsx(Gt, { size: 14 }),
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
function ej({ label: e, onRemove: t }) {
  return /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-properties-remove", "aria-label": e, title: e, onClick: t, children: /* @__PURE__ */ o.jsx($n, { size: 14 }) }) });
}
function tj({ checked: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ o.jsx("input", { type: "checkbox", "aria-label": t, checked: e, onChange: (i) => n(i.target.checked) });
}
function Rs({
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
  const { add: m, update: v, remove: x } = Yb(e, h, {
    namePrefix: r,
    nameKeys: s,
    create: (g) => l(g, qb(t)),
    patch: d
  }), w = ["Name", "Type", "Collection", ...f.default ? ["Default"] : [], ...f.storage ? ["Storage"] : [], ...f.required ? ["Required"] : []], b = r.toLowerCase();
  return /* @__PURE__ */ o.jsx(
    Qb,
    {
      title: a,
      addLabel: c,
      emptyLabel: u,
      headers: w,
      isEmpty: e.length === 0,
      onAdd: m,
      children: e.map((g, y) => {
        const N = ut(g, s), j = ls(g), S = ut(g, xl), I = S ? p?.get(S) : void 0, _ = j.collectionKind === "Single" ? i(j.alias) : "text";
        return /* @__PURE__ */ o.jsxs("tr", { children: [
          /* @__PURE__ */ o.jsxs("td", { children: [
            /* @__PURE__ */ o.jsx("input", { type: "text", "aria-label": `${r} name`, value: N, onChange: (M) => v(y, { name: M.target.value }) }),
            I ? /* @__PURE__ */ o.jsx("span", { className: "wf-properties-warning", role: "note", title: I, children: I }) : null
          ] }),
          /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            $c,
            {
              ariaLabel: `${r} type`,
              value: j.alias,
              options: t,
              placeholder: "Type",
              onChange: (M) => v(y, { type: { alias: M, collectionKind: j.collectionKind } })
            }
          ) }),
          /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            Zb,
            {
              ariaLabel: `${r} collection kind`,
              value: j.collectionKind,
              onChange: (M) => v(y, { type: { alias: j.alias, collectionKind: M } })
            }
          ) }),
          f.default ? /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            Jb,
            {
              ariaLabel: `${r} default value`,
              value: sp(g.default),
              editor: _,
              onChange: (M) => v(y, { default: op(M) })
            }
          ) }) : null,
          f.storage ? /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            $c,
            {
              ariaLabel: `${r} storage driver`,
              value: ut(g, hp),
              options: n,
              placeholder: "—",
              allowEmpty: !0,
              onChange: (M) => v(y, { storageDriverType: M || null })
            }
          ) }) : null,
          f.required ? /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            tj,
            {
              ariaLabel: `${r} required`,
              checked: g.isRequired === !0,
              onChange: (M) => v(y, { isRequired: M })
            }
          ) }) : null,
          /* @__PURE__ */ o.jsx(ej, { label: `Remove ${b} ${N || y + 1}`, onRemove: () => x(y) })
        ] }, y);
      })
    }
  );
}
function Bd({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, title: r = "Variables", addLabel: s = "Add variable", emptyLabel: a = "No variables defined.", warnings: c, onChange: u }) {
  return /* @__PURE__ */ o.jsx(
    Rs,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Variable",
      nameKeys: pp,
      title: r,
      addLabel: s,
      emptyLabel: a,
      create: (l, d) => ip({ name: l, alias: d }),
      patch: (l, d) => rp(l, d),
      columns: { default: !0, storage: !0 },
      warnings: c,
      onChange: u
    }
  );
}
function nj({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, onChange: r }) {
  return /* @__PURE__ */ o.jsx(
    Rs,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Input",
      nameKeys: us,
      title: "Inputs",
      addLabel: "Add input",
      emptyLabel: "No inputs defined.",
      create: (s, a) => ap({ name: s, alias: a }),
      patch: (s, a) => cp(s, a),
      columns: { default: !1, storage: !0, required: !0 },
      onChange: r
    }
  );
}
function ij({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, onChange: r }) {
  return /* @__PURE__ */ o.jsx(
    Rs,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Output",
      nameKeys: us,
      title: "Outputs",
      addLabel: "Add output",
      emptyLabel: "No outputs defined.",
      create: (s, a) => lp({ name: s, alias: a }),
      patch: (s, a) => up(s, a),
      columns: { default: !1, storage: !1 },
      onChange: r
    }
  );
}
function Fi(e) {
  return (e ?? []).filter(Rn);
}
function rj({ context: e, variables: t, title: n, addLabel: i, emptyLabel: r, warnings: s, onChange: a }) {
  const { typeOptions: c, storageOptions: u, editorForAlias: l } = Fd(e);
  return /* @__PURE__ */ o.jsx(
    Bd,
    {
      items: Fi(t),
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
function oj({ definition: e, definitionId: t, onMetaChange: n }) {
  const i = !!n, [r, s] = K(e?.name ?? ""), [a, c] = K(e?.description ?? "");
  ee(() => {
    s(e?.name ?? "");
  }, [e?.name]), ee(() => {
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
function sj({ details: e, draft: t, context: n, onStateChange: i, onDefinitionMetaChange: r }) {
  const { typeOptions: s, storageOptions: a, editorForAlias: c } = Fd(n), u = Fi(t.state.variables), l = Fi(t.state.inputs), d = Fi(t.state.outputs), f = e?.versions ?? [];
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-properties-view", children: [
    /* @__PURE__ */ o.jsx(
      oj,
      {
        definition: e?.definition,
        definitionId: t.definitionId,
        onMetaChange: r
      }
    ),
    /* @__PURE__ */ o.jsx(
      Bd,
      {
        items: u,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, variables: p }))
      }
    ),
    /* @__PURE__ */ o.jsx(
      nj,
      {
        items: l,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, inputs: p }))
      }
    ),
    /* @__PURE__ */ o.jsx(
      ij,
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
        /* @__PURE__ */ o.jsx("time", { children: Re(p.createdAt) })
      ] }, p.id)) })
    ] })
  ] });
}
const Pc = "application/x-elsa-activity-version-id", aj = 6, cj = 1200, lj = 250, uj = [10, 25, 50], dj = 10, Mc = "elsa-studio-workflow-palette-width", Rc = "elsa-studio-workflow-inspector-width", Lc = "elsa-studio-workflow-palette-collapsed", zc = "elsa-studio-workflow-inspector-collapsed", Kd = "elsa-studio-workflow-side-panel-maximized", kn = 180, En = 460, fj = 260, Xt = 260, qt = 560, pj = 320, Vc = 42, Ti = 16, Xd = Fe.createContext(null), qd = Fe.createContext(null), Yd = Fe.createContext(null);
function hj(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function Ud(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Rt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function Lt(e, t, n) {
  const i = t.createPrompt(n);
  return i ? (e.dispatchPrompt(i), !0) : !1;
}
function gj(e) {
  if (!e) return null;
  const t = [], n = e.match(/```(?:json)?\s*([\s\S]*?)```/i);
  n && t.push(n[1]);
  const i = e.match(/\{[\s\S]*?\}/);
  i && t.push(i[0]);
  for (const a of t)
    try {
      const c = JSON.parse(a.trim()), u = Oc(c.name, c.description);
      if (u) return u;
    } catch {
    }
  const r = e.match(/name\s*[:-]\s*(.+)/i)?.[1], s = e.match(/description\s*[:-]\s*(.+)/i)?.[1];
  return Oc(r, s);
}
function Oc(e, t) {
  const n = typeof e == "string" ? Hc(e) : void 0, i = typeof t == "string" ? Hc(t) : void 0;
  return n || i ? { name: n || void 0, description: i || void 0 } : null;
}
function Hc(e) {
  return e.trim().replace(/,$/, "").trim().replace(/^["']/, "").replace(/["']$/, "").trim();
}
function mj(e, t) {
  return e.rootActivityVersionId ?? Zd(t, e.rootKind)?.activityVersionId ?? null;
}
function Zd(e, t) {
  return e.find((n) => yj(n) === t);
}
function yj(e) {
  return e ? xj(e) ? "flowchart" : wj(e) ? "sequence" : null : null;
}
function Yo(e) {
  return Vl(e, (t) => t.category).map((t) => ({
    category: t.category,
    activities: t.items.sort((n, i) => ke(n).localeCompare(ke(i)))
  }));
}
function xj(e) {
  return ke(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function wj(e) {
  return ke(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function vj(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function Gd(e) {
  return Cj(e.rootActivityType) || e.rootActivityType;
}
function bj(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function jj(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function Nj(e, t) {
  return Wc(t) - Wc(e);
}
function Wc(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function Jd(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
function Qd(e) {
  const t = e?.workflowExecutionId ?? e?.runId ?? e?.executionId;
  return typeof t == "string" && t.trim() ? t : null;
}
async function Sj(e) {
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
function Cj(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function kj(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    $i(t, n.typeName, n), $i(t, n.name, n), $i(t, n.displayName, n);
    const i = n.typeName.split(".").filter(Boolean).at(-1);
    $i(t, i, n);
  }
  return t;
}
function Fc(e, t, n) {
  const i = t.get(e.activityVersionId);
  return n.get(In(i?.activityTypeKey)) ?? n.get(In(_t(i?.activityTypeKey))) ?? n.get(In(i?.displayName)) ?? n.get(In(e.activityVersionId)) ?? null;
}
function $i(e, t, n) {
  const i = In(t);
  i && !e.has(i) && e.set(i, n);
}
function In(e) {
  return e?.trim().toLowerCase() ?? "";
}
function Bc(e, t, n, i) {
  const r = Cr();
  if (!r) return t;
  const s = r.getItem(e);
  if (s == null) return t;
  const a = Number(s);
  return Number.isFinite(a) ? Bi(a, n, i) : t;
}
function Kc(e, t) {
  const n = Cr();
  if (!n) return t;
  const i = n.getItem(e);
  return i === "true" ? !0 : i === "false" ? !1 : t;
}
function Ej() {
  const e = Cr();
  if (!e) return null;
  const t = e.getItem(Kd);
  return t === "palette" || t === "inspector" ? t : null;
}
function Cr() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function vn(e, t) {
  const n = Cr();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function Bi(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function ef(e) {
  switch (Ij(e)) {
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
function Ij(e) {
  return (e ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}
function Aj(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function Uo(e) {
  return `${ke(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function Xc(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function _j(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function tf(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function Dj(e) {
  const t = tf(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function Tj(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function We(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function $j(e) {
  return of(We(e));
}
function Pj(e, t) {
  const n = t?.get(e.activityVersionId);
  return n ? ke(n) : _t(e.activityVersionId) ?? e.activityVersionId;
}
function Mj(e, t) {
  if (e.cardinality === "single") {
    const i = e.activities[0];
    return i ? Pj(i, t) : "Empty — click to choose";
  }
  const n = e.activities.length;
  return `${n} activit${n === 1 ? "y" : "ies"}`;
}
function nf(e, t, n = []) {
  if (!e) return n;
  const i = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: i?.activityTypeKey ?? e.activityVersionId,
    displayName: i ? ke(i) : void 0
  });
  for (const r of $e(e, t))
    for (const s of r.activities) nf(s, t, n);
  return n;
}
function rf(e, t, n = []) {
  if (!e) return n;
  for (const i of Dl(e))
    n.push({ source: i.source, target: i.target, sourcePort: i.sourceHandle ?? void 0, targetPort: i.targetHandle ?? void 0 });
  for (const i of $e(e, t))
    for (const r of i.activities) rf(r, t, n);
  return n;
}
function An(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function Rj(e) {
  return `${e.id}-${of(JSON.stringify(e.state))}`;
}
function of(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function Ls(e) {
  return e.status.toLowerCase() === "rejected";
}
function Lj(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function zj(e, t) {
  const n = e instanceof Error ? e.message : String(e);
  return Vj(e, n) ? `Run ${t} was not found.` : n;
}
function Vj(e, t) {
  if ((typeof e == "object" && e ? e.response?.status ?? e.status : void 0) === 404 || /\b404\b/.test(t)) return !0;
  try {
    const i = JSON.parse(t);
    return [i.error, i.title, i.detail].some((r) => typeof r == "string" && /not found/i.test(r));
  } catch {
    return /not found/i.test(t);
  }
}
function fn({ status: e, subStatus: t }) {
  return /* @__PURE__ */ o.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
const sf = { workflowActivity: Oj }, af = { workflow: Wj };
function Oj({ id: e, data: t, selected: n }) {
  const i = t, r = i.runtime, s = !i.suppressFlowPorts, a = s ? i.sourcePorts.length > 0 ? i.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], c = Hj(i), l = Fe.useContext(qd)?.({ activityVersionId: i.activityVersionId, activityTypeKey: i.activityTypeKey }) ?? null, d = Fe.useContext(Yd), f = i.onEnterSlot ?? (d ? (p) => d(e, i.label, p) : void 0);
  return /* @__PURE__ */ o.jsxs(
    "div",
    {
      className: ["wf-node", n ? "selected" : "", r ? "wf-node-runtime" : "", r?.hasBlockingIncident ? "faulted" : "", l ? "wf-node-unavailable" : ""].filter(Boolean).join(" "),
      "data-icon": i.icon ?? "activity",
      children: [
        s && i.acceptsInbound ? /* @__PURE__ */ o.jsx(sn, { type: "target", position: ae.Left }) : null,
        l ? /* @__PURE__ */ o.jsx("span", { className: "wf-node-availability", title: `No longer available for new use · ${zn(l.state)}`, children: /* @__PURE__ */ o.jsx(Tn, { size: 13 }) }) : null,
        /* @__PURE__ */ o.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ o.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: pr(i.icon) }),
          /* @__PURE__ */ o.jsxs("span", { className: "wf-node-copy", children: [
            /* @__PURE__ */ o.jsx("strong", { children: i.label }),
            c ? /* @__PURE__ */ o.jsx("small", { children: c }) : null
          ] })
        ] }),
        i.childSlots.length > 0 ? f ? /* @__PURE__ */ o.jsx("span", { className: "wf-node-slot-list", children: i.childSlots.map((p) => /* @__PURE__ */ o.jsx(
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
        )) }) : /* @__PURE__ */ o.jsxs("span", { className: "wf-node-slot-badge", children: [
          i.childSlots.length,
          " slot",
          i.childSlots.length === 1 ? "" : "s"
        ] }) : null,
        r ? /* @__PURE__ */ o.jsxs("div", { className: "wf-node-runtime-strip", children: [
          r.status ? /* @__PURE__ */ o.jsx(fn, { status: r.status, subStatus: r.subStatus }) : null,
          r.incidentCount > 0 ? /* @__PURE__ */ o.jsxs("span", { className: "wf-node-runtime-count", children: [
            r.incidentCount,
            " incident",
            r.incidentCount === 1 ? "" : "s"
          ] }) : null,
          r.faultCount > 0 ? /* @__PURE__ */ o.jsxs("span", { className: "wf-node-runtime-count", children: [
            r.faultCount,
            " faults"
          ] }) : null
        ] }) : null,
        a.map((p, h) => {
          const m = `${(h + 1) / (a.length + 1) * 100}%`;
          return /* @__PURE__ */ o.jsxs(Fe.Fragment, { children: [
            /* @__PURE__ */ o.jsx("span", { className: "wf-node-port-label", style: { top: m }, children: p.displayName }),
            /* @__PURE__ */ o.jsx(sn, { type: "source", position: ae.Right, id: p.name, style: { top: m } })
          ] }, p.name);
        })
      ]
    }
  );
}
function Hj(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((r) => !!r).join(" · ");
}
function Wj(e) {
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
  } = e, p = Fe.useContext(Xd), [h, m] = K(!1), [v, x, w] = or({ sourceX: n, sourceY: i, targetX: r, targetY: s, sourcePosition: a, targetPosition: c }), b = p?.highlightedEdgeId === t;
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsx(
      ri,
      {
        id: t,
        path: v,
        markerEnd: u,
        style: {
          ...l,
          strokeWidth: b ? 2.5 : l?.strokeWidth
        },
        label: d,
        labelX: x,
        labelY: w,
        labelStyle: f,
        onMouseEnter: () => m(!0),
        onMouseLeave: () => m(!1)
      }
    ),
    p ? /* @__PURE__ */ o.jsx(db, { children: /* @__PURE__ */ o.jsxs(
      "div",
      {
        className: ["wf-edge-actions", h ? "visible" : "", b ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${x}px, ${w}px)` },
        onMouseEnter: () => m(!0),
        onMouseLeave: () => m(!1),
        children: [
          /* @__PURE__ */ o.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (g) => p.requestInsertActivity(t, g.clientX, g.clientY), children: /* @__PURE__ */ o.jsx(Gt, { size: 12 }) }),
          /* @__PURE__ */ o.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => p.deleteEdge(t), children: /* @__PURE__ */ o.jsx($n, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function cf({ clientX: e, clientY: t, activities: n, onPick: i, onClose: r }) {
  const [s, a] = K(""), [c, u] = K(0), l = oe(null), d = oe(null), f = ue(() => {
    const b = s.trim().toLowerCase(), g = n.filter(vj);
    return b ? g.filter((y) => ke(y).toLowerCase().includes(b) || y.activityTypeKey.toLowerCase().includes(b) || (y.category ?? "").toLowerCase().includes(b) || (y.description ?? "").toLowerCase().includes(b)) : g;
  }, [n, s]), p = ue(() => Yo(f), [f]), h = ue(() => p.flatMap((b) => b.activities), [p]);
  ee(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), ee(() => {
    const b = (y) => {
      l.current?.contains(y.target) || r();
    }, g = (y) => {
      y.key === "Escape" && r();
    };
    return document.addEventListener("mousedown", b, !0), document.addEventListener("keydown", g), () => {
      document.removeEventListener("mousedown", b, !0), document.removeEventListener("keydown", g);
    };
  }, [r]);
  const m = (b) => {
    if (b.key === "ArrowDown")
      b.preventDefault(), u((g) => Math.min(g + 1, h.length - 1));
    else if (b.key === "ArrowUp")
      b.preventDefault(), u((g) => Math.max(g - 1, 0));
    else if (b.key === "Enter") {
      b.preventDefault();
      const g = h[c];
      g && i(g);
    }
  }, v = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), x = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let w = -1;
  return /* @__PURE__ */ o.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: v, top: x }, onMouseDown: (b) => b.stopPropagation(), onClick: (b) => b.stopPropagation(), children: [
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
        onKeyDown: m
      }
    ),
    /* @__PURE__ */ o.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: p.length === 0 ? /* @__PURE__ */ o.jsx("p", { children: "No matching activities." }) : p.map((b) => /* @__PURE__ */ o.jsxs("section", { children: [
      /* @__PURE__ */ o.jsx("h4", { children: b.category }),
      b.activities.map((g) => {
        w += 1;
        const y = w, N = y === c;
        return /* @__PURE__ */ o.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": N,
            className: N ? "active" : "",
            onMouseEnter: () => u(y),
            onClick: () => i(g),
            children: [
              /* @__PURE__ */ o.jsx("strong", { children: ke(g) }),
              /* @__PURE__ */ o.jsx("small", { children: g.category || g.activityTypeKey })
            ]
          },
          g.activityVersionId
        );
      })
    ] }, b.category)) })
  ] });
}
function Ki({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: i
}) {
  const r = Yf(t.map((s) => s.id), n, i);
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
function qc(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
const Fj = "Expressions/UnresolvedVariable";
function Bj(e) {
  return String(e.type ?? e.code ?? "");
}
function Kj(e) {
  return Bj(e) === Fj;
}
function Xj(e) {
  const t = String(e ?? "").trim().split("/").filter(Boolean), [n, i, ...r] = t, s = n === "$workflow";
  return {
    nodeId: !n || s ? null : n,
    isWorkflowScope: s,
    bag: i === "inputs" || i === "outputs" || i === "variables" ? i : null,
    referenceKey: r.length > 0 ? r.join("/") : null
  };
}
function qj(e) {
  return (e ?? []).filter(Kj).map((t) => ({
    error: t,
    path: Xj(t.path),
    message: t.message ?? "Variable reference is not visible from this activity's scope."
  }));
}
function Yj({ draft: e, onRepair: t }) {
  const n = e.validationErrors;
  if (!n.length)
    return /* @__PURE__ */ o.jsxs("div", { className: "wf-validation ok", children: [
      /* @__PURE__ */ o.jsx(an, { size: 14 }),
      " No validation errors"
    ] });
  const i = qj(n), r = new Map(i.map((s) => [s.error, s]));
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-validation-summary", children: [
      /* @__PURE__ */ o.jsx(jt, { size: 14 }),
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
          /* @__PURE__ */ o.jsx(Vf, { size: 12 }),
          " Repair"
        ] }) : null
      ] }, a);
    }) })
  ] });
}
function Uj({
  testRun: e,
  onOpenDetails: t
}) {
  const n = Ls(e);
  return /* @__PURE__ */ o.jsx("div", { className: "wf-test-run-status", "data-state": n ? "rejected" : "accepted", children: /* @__PURE__ */ o.jsxs(
    "button",
    {
      type: "button",
      className: "wf-test-run-trigger",
      onClick: t,
      children: [
        n ? /* @__PURE__ */ o.jsx(jt, { size: 16 }) : /* @__PURE__ */ o.jsx(an, { size: 16 }),
        n ? "Test run rejected" : "Test run dispatched"
      ]
    }
  ) });
}
function Zj({ testRun: e, onOpenRun: t }) {
  if (!e)
    return /* @__PURE__ */ o.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Run the draft to see Runtime Evidence." }) });
  const n = Ls(e), i = e.workflowExecutionId;
  return /* @__PURE__ */ o.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ o.jsxs("section", { className: "wf-runtime-card", "data-state": n ? "rejected" : "accepted", children: [
    /* @__PURE__ */ o.jsxs("header", { children: [
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("span", { children: "Latest Test Run" }),
        /* @__PURE__ */ o.jsx("h3", { children: n ? "Rejected by the server" : "Transient run accepted" })
      ] }),
      /* @__PURE__ */ o.jsx(fn, { status: e.status, subStatus: e.commandDispatchStatus ?? void 0 })
    ] }),
    /* @__PURE__ */ o.jsx("p", { children: "Ephemeral - not saved, promoted, or published." }),
    n && e.reason ? /* @__PURE__ */ o.jsxs("div", { className: "wf-runtime-reason", children: [
      /* @__PURE__ */ o.jsx(jt, { size: 14 }),
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
        /* @__PURE__ */ o.jsx("dd", { children: Yc(e.activityCount, "activity") })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ o.jsx("dd", { children: Yc(e.incidentCount, "incident") })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ o.jsx("dd", { title: e.expiresAt ? Re(e.expiresAt) : "None", children: e.expiresAt ? Re(e.expiresAt) : "None" })
      ] })
    ] })
  ] }) });
}
function Yc(e, t) {
  return typeof e != "number" ? "Available on linked Run" : `${e} ${t}${e === 1 ? "" : "s"}`;
}
function zs({ rows: e = 5 }) {
  return /* @__PURE__ */ o.jsx("div", { className: "wf-grid", "aria-busy": "true", "aria-label": "Loading", children: Array.from({ length: e }).map((t, n) => /* @__PURE__ */ o.jsx("div", { className: "wf-skeleton wf-skeleton-row", style: { width: `${90 - n % 3 * 12}%` } }, n)) });
}
function Vs({ icon: e, title: t, description: n, action: i }) {
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-empty-state", role: "status", children: [
    /* @__PURE__ */ o.jsx("div", { className: "wf-empty-state-icon", "aria-hidden": !0, children: e ?? /* @__PURE__ */ o.jsx(Zn, { size: 22 }) }),
    /* @__PURE__ */ o.jsx("h3", { children: t }),
    n ? /* @__PURE__ */ o.jsx("p", { children: n }) : null,
    i ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty-state-action", children: i }) : null
  ] });
}
function oi({ message: e, title: t = "Something went wrong" }) {
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-error-card", role: "alert", children: [
    /* @__PURE__ */ o.jsx(jt, { size: 18 }),
    /* @__PURE__ */ o.jsxs("div", { className: "wf-error-card-body", children: [
      /* @__PURE__ */ o.jsx("strong", { children: t }),
      /* @__PURE__ */ o.jsx("span", { children: e || "Please try again, or check that the Elsa server is reachable." })
    ] })
  ] });
}
function lf({ status: e, run: t, compact: n = !1 }) {
  const i = () => {
    t?.workflowExecutionId && (window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(t.workflowExecutionId)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  };
  return /* @__PURE__ */ o.jsxs("div", { className: `wf-status-line${n ? " compact" : ""}`, children: [
    /* @__PURE__ */ o.jsx(an, { size: n ? 13 : 14 }),
    /* @__PURE__ */ o.jsx("span", { children: e }),
    t?.workflowExecutionId ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: i, children: [
      "Open Run ",
      t.workflowExecutionId
    ] }) : null
  ] });
}
function It({ value: e, ariaLabel: t, copiedLabel: n, onCopied: i, onCopyFailed: r }) {
  if (!e) return null;
  const s = async (a) => {
    a.preventDefault(), a.stopPropagation();
    try {
      await Sj(e), i(n);
    } catch {
      r(n);
    }
  };
  return /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (a) => {
    s(a);
  }, children: /* @__PURE__ */ o.jsx(Of, { size: 12 }) });
}
function Gj({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: i }) {
  const [r, s] = K("loading"), [a, c] = K(""), [u, l] = K(""), [d, f] = K(null), [p, h] = K([]), m = n?.trim().toLowerCase() ?? "", v = ue(
    () => m ? p.filter((S) => bj(S, m)) : p,
    [m, p]
  ), x = ue(
    () => Array.from(new Set(p.flatMap((S) => [
      S.definitionId,
      S.definitionVersionId,
      S.sourceId
    ]).filter((S) => !!S))).sort((S, I) => S.localeCompare(I)),
    [p]
  ), w = Rt(t, "weaver.workflows.explain-executable"), b = se(async () => {
    s("loading"), c("");
    try {
      h(await Ll(e)), s("ready");
    } catch (S) {
      c(S instanceof Error ? S.message : String(S)), s("failed");
    }
  }, [e]);
  ee(() => {
    b();
  }, [b]);
  const g = async (S) => {
    l(""), f(null), c("");
    try {
      const I = await Rl(e, S.artifactId), _ = Qd(I);
      f({ artifactId: S.artifactId, workflowExecutionId: _ }), l(`Started ${S.artifactId}`);
    } catch (I) {
      c(I instanceof Error ? I.message : String(I));
    }
  }, y = (S) => {
    w && Lt(t, w, S) && (c(""), f(null), l(`Sent ${S.artifactId} to Weaver`));
  }, N = (S) => {
    c(""), f(null), l(`Copied ${S}`);
  }, j = (S) => {
    l(""), f(null), c(`Could not copy ${S}.`);
  };
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => {
        b();
      }, children: "Refresh" }),
      /* @__PURE__ */ o.jsxs("label", { className: "wf-search wf-executable-definition-filter", children: [
        /* @__PURE__ */ o.jsx(ar, { size: 14 }),
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
        /* @__PURE__ */ o.jsx(ss, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    r === "failed" ? /* @__PURE__ */ o.jsx(oi, { message: a }) : null,
    u ? /* @__PURE__ */ o.jsx(lf, { status: u, run: d }) : null,
    r === "loading" ? /* @__PURE__ */ o.jsx(zs, {}) : null,
    r === "ready" && v.length === 0 ? /* @__PURE__ */ o.jsx(
      Vs,
      {
        icon: /* @__PURE__ */ o.jsx(Zt, { size: 22 }),
        title: "No workflow executables",
        description: n ? "No executables match this definition filter." : "Publish a workflow definition to make it executable."
      }
    ) : null,
    r === "ready" && v.length > 0 ? /* @__PURE__ */ o.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ o.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ o.jsx("span", { children: "Version" }),
        /* @__PURE__ */ o.jsx("span", { children: "Source" }),
        /* @__PURE__ */ o.jsx("span", { children: "Root" }),
        /* @__PURE__ */ o.jsx("span", { children: "Published" }),
        /* @__PURE__ */ o.jsx("span", { children: "Actions" })
      ] }),
      v.map((S) => /* @__PURE__ */ o.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ o.jsxs("span", { className: "wf-artifact-cell", children: [
          /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line", children: [
            /* @__PURE__ */ o.jsx("strong", { title: S.artifactId, children: S.artifactId }),
            /* @__PURE__ */ o.jsx(It, { value: S.artifactId, ariaLabel: `Copy artifact ID ${S.artifactId}`, copiedLabel: "artifact ID", onCopied: N, onCopyFailed: j })
          ] }),
          /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
            /* @__PURE__ */ o.jsx("small", { title: S.artifactHash, children: S.artifactHash }),
            /* @__PURE__ */ o.jsx(It, { value: S.artifactHash, ariaLabel: `Copy artifact hash ${S.artifactHash}`, copiedLabel: "artifact hash", onCopied: N, onCopyFailed: j })
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line wf-version-cell", children: [
          /* @__PURE__ */ o.jsx("span", { children: S.artifactVersion }),
          /* @__PURE__ */ o.jsx(It, { value: S.artifactVersion, ariaLabel: `Copy artifact version ${S.artifactVersion}`, copiedLabel: "artifact version", onCopied: N, onCopyFailed: j })
        ] }),
        /* @__PURE__ */ o.jsx(Jj, { executable: S, onCopied: N, onCopyFailed: j }),
        /* @__PURE__ */ o.jsx("span", { children: Gd(S) }),
        /* @__PURE__ */ o.jsx("span", { children: Re(S.publishedAt ?? S.createdAt) }),
        /* @__PURE__ */ o.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
            g(S);
          }, children: [
            /* @__PURE__ */ o.jsx(Zt, { size: 13 }),
            " Run"
          ] }),
          w ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => y(S), children: [
            /* @__PURE__ */ o.jsx(ft, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, S.artifactId))
    ] }) : null
  ] });
}
function Jj({ executable: e, onCopied: t, onCopyFailed: n }) {
  const i = e.sourceId || e.definitionVersionId || e.definitionId, r = e.sourceVersion;
  return /* @__PURE__ */ o.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ o.jsx("span", { className: "wf-source-kind", children: Jd(e.sourceKind) }),
    i ? /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line", children: [
      /* @__PURE__ */ o.jsx("code", { title: i, children: i }),
      /* @__PURE__ */ o.jsx(It, { value: i, ariaLabel: `Copy source ID ${i}`, copiedLabel: "source ID", onCopied: t, onCopyFailed: n })
    ] }) : null,
    r ? /* @__PURE__ */ o.jsxs("small", { children: [
      "Version ",
      r
    ] }) : null
  ] });
}
function Qj({ context: e, ai: t, definitionId: n, publishedArtifactId: i }) {
  const [r, s] = K("loading"), [a, c] = K(""), [u, l] = K(""), [d, f] = K(null), [p, h] = K([]), m = Rt(t, "weaver.workflows.explain-executable"), v = se(async () => {
    s("loading"), c("");
    try {
      const N = await Ll(e);
      h(N.filter((j) => jj(j, n)).sort(Nj)), s("ready");
    } catch (N) {
      c(N instanceof Error ? N.message : String(N)), h([]), s("failed");
    }
  }, [e, n]);
  ee(() => {
    v();
  }, [v, i]);
  const x = async (N) => {
    l(""), f(null), c("");
    try {
      const j = await Rl(e, N.artifactId);
      f({ artifactId: N.artifactId, workflowExecutionId: Qd(j) }), l(`Started ${N.artifactId}`);
    } catch (j) {
      c(j instanceof Error ? j.message : String(j));
    }
  }, w = (N) => {
    m && Lt(t, m, N) && (c(""), f(null), l(`Sent ${N.artifactId} to Weaver`));
  }, b = () => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(n)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, g = (N) => {
    c(""), f(null), l(`Copied ${N}`);
  }, y = (N) => {
    l(""), f(null), c(`Could not copy ${N}.`);
  };
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-artifacts-panel", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-artifacts-toolbar", children: [
      /* @__PURE__ */ o.jsxs("span", { children: [
        p.length,
        " artifact",
        p.length === 1 ? "" : "s"
      ] }),
      /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
        v();
      }, children: [
        /* @__PURE__ */ o.jsx(as, { size: 13 }),
        " Refresh"
      ] }),
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: b, children: "Open list" })
    ] }),
    r === "failed" ? /* @__PURE__ */ o.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ o.jsx(jt, { size: 14 }),
      " ",
      a
    ] }) : null,
    u ? /* @__PURE__ */ o.jsx(lf, { status: u, run: d, compact: !0 }) : null,
    r === "loading" ? /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "Loading artifacts..." }) : null,
    r === "ready" && p.length === 0 ? /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "No published artifacts for this workflow yet." }) : null,
    r === "ready" && p.length > 0 ? /* @__PURE__ */ o.jsx("div", { className: "wf-artifact-list", role: "list", "aria-label": "Workflow artifacts", children: p.map((N) => /* @__PURE__ */ o.jsxs("article", { className: "wf-artifact-card", role: "listitem", "data-active": N.artifactId === i ? "true" : void 0, children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-artifact-card-heading", children: [
        /* @__PURE__ */ o.jsxs("div", { children: [
          /* @__PURE__ */ o.jsxs("span", { className: "wf-artifact-version", children: [
            "Version ",
            N.artifactVersion
          ] }),
          N.artifactId === i ? /* @__PURE__ */ o.jsx("span", { className: "wf-chip", children: "Latest publish" }) : null
        ] }),
        /* @__PURE__ */ o.jsx("span", { children: Re(N.publishedAt ?? N.createdAt) })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { className: "wf-artifact-card-values", children: [
        /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line", children: [
          /* @__PURE__ */ o.jsx("code", { title: N.artifactId, children: N.artifactId }),
          /* @__PURE__ */ o.jsx(It, { value: N.artifactId, ariaLabel: `Copy artifact ID ${N.artifactId}`, copiedLabel: "artifact ID", onCopied: g, onCopyFailed: y })
        ] }),
        /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ o.jsx("code", { title: N.artifactHash, children: N.artifactHash }),
          /* @__PURE__ */ o.jsx(It, { value: N.artifactHash, ariaLabel: `Copy artifact hash ${N.artifactHash}`, copiedLabel: "artifact hash", onCopied: g, onCopyFailed: y })
        ] })
      ] }),
      /* @__PURE__ */ o.jsxs("dl", { children: [
        /* @__PURE__ */ o.jsxs("div", { children: [
          /* @__PURE__ */ o.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ o.jsxs("dd", { children: [
            Jd(N.sourceKind),
            " ",
            N.sourceVersion ? `v${N.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("div", { children: [
          /* @__PURE__ */ o.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ o.jsx("dd", { children: Gd(N) })
        ] })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
          x(N);
        }, children: [
          /* @__PURE__ */ o.jsx(Zt, { size: 13 }),
          " Run"
        ] }),
        m ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => w(N), children: [
          /* @__PURE__ */ o.jsx(ft, { size: 13 }),
          " Explain"
        ] }) : null
      ] })
    ] }, N.artifactId)) }) : null
  ] });
}
function uf() {
  const [e, t] = K(() => Bc(Mc, fj, kn, En)), [n, i] = K(() => Bc(Rc, pj, Xt, qt)), [r, s] = K(() => Kc(Lc, !1)), [a, c] = K(() => Kc(zc, !1)), [u, l] = K(Ej);
  ee(() => {
    vn(Mc, String(e));
  }, [e]), ee(() => {
    vn(Rc, String(n));
  }, [n]), ee(() => {
    vn(Lc, String(r));
  }, [r]), ee(() => {
    vn(zc, String(a));
  }, [a]), ee(() => {
    vn(Kd, u);
  }, [u]), ee(() => {
    if (!u) return;
    const g = (y) => {
      y.key === "Escape" && l(null);
    };
    return window.addEventListener("keydown", g), () => window.removeEventListener("keydown", g);
  }, [u]);
  const d = se((g) => {
    l((y) => y === g ? null : y), g === "palette" ? s((y) => !y) : c((y) => !y);
  }, []), f = se((g) => {
    g === "palette" ? s(!1) : c(!1), l((y) => y === g ? null : g);
  }, []), p = se((g, y) => {
    l(null), g === "palette" ? (s(!1), t((N) => Bi(N + y, kn, En))) : (c(!1), i((N) => Bi(N + y, Xt, qt)));
  }, []), h = se((g, y) => {
    y.preventDefault(), l(null), g === "palette" ? s(!1) : c(!1);
    const N = y.clientX, j = g === "palette" ? e : n, S = g === "palette" ? kn : Xt, I = g === "palette" ? En : qt;
    document.body.classList.add("wf-side-panel-resizing");
    const _ = (k) => {
      const $ = g === "palette" ? k.clientX - N : N - k.clientX, z = Bi(j + $, S, I);
      g === "palette" ? t(z) : i(z);
    }, M = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", _), window.removeEventListener("pointerup", M), window.removeEventListener("pointercancel", M);
    };
    window.addEventListener("pointermove", _), window.addEventListener("pointerup", M), window.addEventListener("pointercancel", M);
  }, [n, e]), m = se((g, y) => {
    y.key === "ArrowLeft" ? (y.preventDefault(), p(g, g === "palette" ? -Ti : Ti)) : y.key === "ArrowRight" ? (y.preventDefault(), p(g, g === "palette" ? Ti : -Ti)) : y.key === "Home" ? (y.preventDefault(), g === "palette" ? t(kn) : i(Xt)) : y.key === "End" && (y.preventDefault(), g === "palette" ? t(En) : i(qt));
  }, [p]), v = !r && u !== "inspector", x = !a && u !== "palette", w = [
    "wf-editor-body",
    r ? "palette-collapsed" : "",
    a ? "inspector-collapsed" : "",
    u === "palette" ? "palette-maximized" : "",
    u === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), b = {
    "--wf-palette-width": `${r ? Vc : e}px`,
    "--wf-inspector-width": `${a ? Vc : n}px`
  };
  return {
    paletteWidth: e,
    inspectorWidth: n,
    paletteCollapsed: r,
    inspectorCollapsed: a,
    maximizedSidePanel: u,
    setInspectorCollapsed: c,
    paletteExpanded: v,
    inspectorExpanded: x,
    editorBodyClassName: w,
    editorBodyStyle: b,
    toggleSidePanelCollapsed: d,
    toggleSidePanelMaximized: f,
    startSidePanelResize: h,
    handleSidePanelResizeKeyDown: m
  };
}
const eN = 50;
function Uc() {
  return { past: [], future: [] };
}
function tN(e) {
  return e.past.length > 0;
}
function nN(e) {
  return e.future.length > 0;
}
function Zc(e, t, n = eN) {
  const i = [...e.past, t];
  return i.length > n && i.splice(0, i.length - n), { past: i, future: [] };
}
function iN(e, t) {
  if (e.past.length === 0) return null;
  const n = e.past.slice(), i = n.pop();
  return { history: { past: n, future: [...e.future, t] }, snapshot: i };
}
function rN(e, t) {
  if (e.future.length === 0) return null;
  const n = e.future.slice(), i = n.pop();
  return { history: { past: [...e.past, t], future: n }, snapshot: i };
}
function oN({ draft: e, restoreDraft: t }) {
  const n = oe(Uc()), i = oe(null), r = oe(""), s = oe(!1), [a, c] = K(0), u = se((v) => {
    n.current = Uc(), i.current = v ? An(v) : null, r.current = v ? We(v) : "", s.current = !1, c(0);
  }, []);
  ee(() => {
    if (!e) return;
    if (s.current) {
      s.current = !1;
      return;
    }
    const v = We(e);
    if (v === r.current) return;
    const x = window.setTimeout(() => {
      const w = i.current;
      w && (n.current = Zc(n.current, w), c((b) => b + 1)), i.current = An(e), r.current = v;
    }, lj);
    return () => window.clearTimeout(x);
  }, [e]);
  const l = se(() => {
    if (!e) return;
    const v = We(e);
    if (v === r.current) return;
    const x = i.current;
    x && (n.current = Zc(n.current, x)), i.current = An(e), r.current = v;
  }, [e]), d = se((v) => {
    s.current = !0, i.current = An(v), r.current = We(v), t(v), c((x) => x + 1);
  }, [t]), f = se(() => {
    if (!e) return;
    l();
    const v = iN(n.current, e);
    v && (n.current = v.history, d(v.snapshot));
  }, [e, l, d]), p = se(() => {
    if (!e) return;
    l();
    const v = rN(n.current, e);
    v && (n.current = v.history, d(v.snapshot));
  }, [e, l, d]), { canUndoNow: h, canRedoNow: m } = ue(() => {
    const v = !!e && !!i.current && We(e) !== r.current;
    return {
      canUndoNow: tN(n.current) || v,
      canRedoNow: nN(n.current) && !v
    };
  }, [e, a]);
  return { resetHistory: u, undo: f, redo: p, canUndoNow: h, canRedoNow: m };
}
const sN = {
  draft: null,
  frames: [],
  selectedNodeId: null,
  testRun: null,
  publishedArtifactId: null
};
function aN(e, t) {
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
function cN() {
  const [e, t] = Pf(aN, sN), n = ue(() => ({
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
const lN = 320, uN = 140;
function dN(e, t, n) {
  return n === "sequence" ? fN(e) : pN(e, t);
}
function fN(e) {
  const t = /* @__PURE__ */ new Map();
  return e.forEach((n, i) => {
    t.set(n.id, { x: i * 280, y: 0 });
  }), t;
}
function pN(e, t) {
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
      n.set(p, { x: d * lN, y: h * uN });
    });
  return n;
}
function hN(e, t, n, i, r) {
  if (!e) return { kind: "becomeRoot" };
  const s = Ln(e, t, r);
  if (!s)
    return t.length > 0 ? { kind: "staleFrames" } : $e(n, i)[0] ? { kind: "wrapRoot" } : { kind: "leafError" };
  const a = s.slot.cardinality === "single" ? s.slot.activities.find((c) => c.nodeId !== n.nodeId) ?? null : null;
  return { kind: "addToSlot", slot: s.slot, replacedActivity: a };
}
const gN = "root";
function mN(e) {
  return e.length === 0 ? gN : e.map((t) => `${t.ownerNodeId}:${t.slotId}`).join("/");
}
function yN(e, t) {
  return e.length === t.length && e.every((n, i) => n.id === t[i]);
}
function xN({
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
  setStatus: m,
  setError: v
}) {
  const [x, w] = K([]), [b, g] = K([]), [y, N] = K(null), [j, S] = K(null), [I, _] = K(null), M = oe(null), k = oe(null), $ = oe(/* @__PURE__ */ new Map()), z = oe(/* @__PURE__ */ new Set()), C = oe(null), A = oe([]), E = oe(null), D = oe(null), P = oe(!1), T = ue(() => mN(i), [i]);
  ee(() => () => {
    y && $.current.set(T, y.getViewport());
  }, [y, T]), ee(() => {
    if (C.current = T, !n) {
      A.current = [], w([]), g([]);
      return;
    }
    const L = a ? kl(n, r, e?.layout ?? []) : t ? Cl(t, r, e?.layout ?? []) : { nodes: [], edges: [] };
    A.current = L.nodes.map((B) => B.id), w(L.nodes), g(L.edges);
  }, [r, e?.layout, a, t, n, T]), ee(() => {
    if (!y || C.current !== T || !yN(x, A.current)) return;
    C.current = null;
    const L = $.current.get(T), B = z.current.has(T);
    z.current.add(T), window.requestAnimationFrame(() => {
      L ? y.setViewport(L) : !B && x.length > 0 && y.fitView({ padding: 0.2 });
    });
  }, [x, y, T]);
  const F = se((L, B, Y) => Y ? [
    ...L.filter((re) => re.nodeId !== B),
    { nodeId: B, x: Math.round(Y.x), y: Math.round(Y.y) }
  ] : L, []), W = se((L, B) => {
    if (e?.state.rootActivity && a)
      return null;
    const Y = Xi(L, Uo(L)), re = hN(e?.state.rootActivity, i, Y, L, s);
    return re.kind === "becomeRoot" ? (f(
      ({ draft: Q }) => Q ? { ...Q, state: { ...Q.state, rootActivity: Y } } : null,
      Y.nodeId
    ), null) : re.kind === "leafError" ? (m(""), v("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root."), null) : re.kind === "staleFrames" ? (m(""), v("This slot could not be resolved — returning to the workflow root."), h(), null) : re.kind === "wrapRoot" ? (f(({ draft: Q }) => {
      const xe = Q?.state.rootActivity;
      return xe ? {
        ...Q,
        layout: F(Q.layout, xe.nodeId, B),
        state: { ...Q.state, rootActivity: sa(Y, [], [xe], L) }
      } : null;
    }, e?.state.rootActivity?.nodeId ?? null), v(""), m(`Wrapped root in ${ke(L)}`), null) : (f(({ draft: Q, frames: xe }) => {
      if (!Q?.state.rootActivity) return null;
      const pe = Ln(Q.state.rootActivity, xe, s);
      if (!pe) return null;
      const we = pe.slot.cardinality === "single" ? [Y] : [...pe.slot.activities, Y], Se = sa(Q.state.rootActivity, xe, we, s);
      return {
        ...Q,
        layout: F(Q.layout, Y.nodeId, B),
        state: { ...Q.state, rootActivity: Se }
      };
    }, Y.nodeId), re.replacedActivity && (v(""), m(`Replaced ${re.slot.label} content`)), Y);
  }, [s, e?.state.rootActivity, i, a, f, h, F, v, m]), H = se((L, B) => {
    const Y = Xi(L, Uo(L)), re = {
      id: Y.nodeId,
      type: "workflowActivity",
      position: B,
      selected: !0,
      data: {
        label: ke(L),
        activityVersionId: L.activityVersionId,
        activityTypeKey: L.activityTypeKey,
        category: L.category,
        executionType: L.executionType,
        icon: un(L),
        childSlots: $e(Y, L),
        acceptsInbound: String(L.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: Tl(Y, L)
      }
    };
    return { activityNode: Y, node: re };
  }, []), U = se((L, B, Y = []) => {
    a || d(({ draft: re, frames: Q }) => {
      if (!re) return null;
      const xe = Lp(re.layout, L), pe = re.state.rootActivity;
      if (!pe) return { ...re, layout: xe };
      const we = Ln(pe, Q, s);
      if (!we) return { ...re, layout: xe };
      const Se = Mp(we, L, B, Y), qe = we.slot.mode === "flowchart" ? Rp(Se, B) : Se;
      return {
        ...re,
        layout: xe,
        state: {
          ...re.state,
          rootActivity: Pp(pe, Q, qe, s)
        }
      };
    });
  }, [s, a, d]), O = se((L, B) => {
    if (!M.current) return null;
    const Y = M.current.getBoundingClientRect();
    return y ? y.screenToFlowPosition({ x: L, y: B }) : {
      x: L - Y.left,
      y: B - Y.top
    };
  }, [y]), q = se((L, B) => document.elementFromPoint(L, B)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), J = se((L, B, Y) => {
    const re = x.find((Te) => Te.id === B.source), Q = x.find((Te) => Te.id === B.target), xe = re && Q ? _j(re, Q) : re ? Xc(re) : Y, pe = H(L, xe), Se = [...x.map((Te) => Te.selected ? { ...Te, selected: !1 } : Te), pe.node], qe = Kp(b, B, pe.node.id);
    w(Se), g(qe), p(pe.node.id), U(Se, qe, [pe.activityNode]);
  }, [U, H, b, x, p]), G = se((L, B, Y) => {
    if (!u || !M.current) return !1;
    const re = M.current.getBoundingClientRect();
    if (!(B >= re.left && B <= re.right && Y >= re.top && Y <= re.bottom)) return !1;
    const xe = O(B, Y);
    if (!xe) return !1;
    if (c) {
      const pe = q(B, Y), we = pe ? b.find((Se) => Se.id === pe) : void 0;
      if (we)
        return J(L, we, xe), !0;
    }
    return W(L, xe), !0;
  }, [W, u, b, q, c, J, O]);
  ee(() => {
    const L = (Y) => {
      const re = E.current;
      if (!re) return;
      Math.hypot(Y.clientX - re.startX, Y.clientY - re.startY) >= aj && (re.dragging = !0);
    }, B = (Y) => {
      const re = E.current;
      if (E.current = null, !re?.dragging || !M.current || D.current) return;
      const Q = M.current.getBoundingClientRect();
      Y.clientX >= Q.left && Y.clientX <= Q.right && Y.clientY >= Q.top && Y.clientY <= Q.bottom && (P.current = !0, window.setTimeout(() => {
        P.current = !1;
      }, 0), G(re.activity, Y.clientX, Y.clientY));
    };
    return window.addEventListener("pointermove", L), window.addEventListener("pointerup", B), window.addEventListener("pointercancel", B), () => {
      window.removeEventListener("pointermove", L), window.removeEventListener("pointerup", B), window.removeEventListener("pointercancel", B);
    };
  }, [y, G]);
  const R = (L, B) => {
    D.current = { activityVersionId: B.activityVersionId, handledDrop: !1 }, L.dataTransfer.setData(Pc, B.activityVersionId), L.dataTransfer.setData("text/plain", B.activityVersionId), L.dataTransfer.effectAllowed = "copy";
  }, X = (L, B) => {
    const Y = D.current;
    D.current = null, !Y?.handledDrop && (L.clientX === 0 && L.clientY === 0 || G(B, L.clientX, L.clientY) && (P.current = !0, window.setTimeout(() => {
      P.current = !1;
    }, 0)));
  }, de = (L, B) => {
    L.button === 0 && (E.current = {
      activity: B,
      startX: L.clientX,
      startY: L.clientY,
      dragging: !1
    });
  }, le = (L) => {
    P.current || u && W(L);
  }, te = (L) => {
    if (!u) {
      L.dataTransfer.dropEffect = "none";
      return;
    }
    if (L.preventDefault(), L.dataTransfer.dropEffect = "copy", !c) return;
    const B = q(L.clientX, L.clientY);
    _(B);
  }, ie = (L) => {
    if (!M.current) return;
    const B = L.relatedTarget;
    B && M.current.contains(B) || _(null);
  }, fe = (L) => {
    L.preventDefault(), _(null);
    const B = L.dataTransfer.getData(Pc) || L.dataTransfer.getData("text/plain");
    if (!B || (L.stopPropagation(), D.current?.activityVersionId === B && (D.current.handledDrop = !0), !u)) return;
    const Y = s.get(B);
    Y && G(Y, L.clientX, L.clientY);
  }, V = (L) => {
    if (!u) return;
    if (L) {
      S({ kind: "fromEmpty", clientX: L.clientX, clientY: L.clientY });
      return;
    }
    const B = M.current?.getBoundingClientRect();
    B && S({
      kind: "fromEmpty",
      clientX: B.left + B.width / 2,
      clientY: B.top + B.height / 2
    });
  }, ne = (L) => {
    const B = a ? L.filter((Y) => Y.type === "select") : L;
    B.length !== 0 && w((Y) => cd(B, Y));
  }, ge = (L) => {
    a || g((B) => ld(L, B));
  }, ye = (L) => !L.source || !L.target || L.source === L.target || !c ? !1 : !L.targetHandle, Ie = (L) => {
    if (!e?.state.rootActivity || !t || !c || !ye(L)) return;
    const B = qi(L.source, L.target, L.sourceHandle ?? "Done", L.targetHandle ?? void 0), Y = dd(B, b);
    g(Y), U(x, Y);
  }, _e = () => {
    U(x, b);
  }, Ve = !a && x.length > 0, at = se(() => {
    if (a || x.length === 0) return;
    const L = t?.slot.mode === "sequence" ? "sequence" : "flowchart", B = dN(x, b, L), Y = x.map((re) => {
      const Q = B.get(re.id);
      return Q ? { ...re, position: Q } : re;
    });
    w(Y), U(Y, b), window.requestAnimationFrame(() => y?.fitView({ padding: 0.2 })), m("Rearranged the canvas.");
  }, [b, x, t, a, U, y, m]), Ye = (L, B) => {
    if (!B.nodeId || B.handleType === "target") {
      k.current = null;
      return;
    }
    k.current = {
      nodeId: B.nodeId,
      handleId: B.handleId ?? null
    };
  }, Ue = (L, B) => {
    const Y = Tj(k.current, B);
    if (k.current = null, !Y || !c || B.toNode || B.toHandle || Dj(L)) return;
    const re = tf(L);
    S({
      kind: "fromPort",
      sourceNodeId: Y.nodeId,
      sourceHandleId: Y.handleId,
      clientX: re.x,
      clientY: re.y
    });
  }, Me = (L, B) => {
    if (!c || !ye(B)) return;
    const Y = Wv(L, {
      ...B,
      sourceHandle: B.sourceHandle ?? "Done",
      // Connection.targetHandle is string | null; null and undefined are indistinguishable downstream
      // (every consumer checks truthiness), so keep the type-correct null.
      targetHandle: B.targetHandle ?? null
    }, b, { shouldReplaceId: !1 });
    g(Y), U(x, Y);
  }, Oe = (L) => {
    if (a || L.length === 0) return;
    const B = new Set(L.map((Q) => Q.id)), Y = x.filter((Q) => !B.has(Q.id)), re = b.filter((Q) => !B.has(Q.source) && !B.has(Q.target));
    w(Y), g(re), l && B.has(l) && p(null), U(Y, re);
  }, Ne = (L) => {
    if (a || L.length === 0) return;
    const B = new Set(L.map((re) => re.id)), Y = b.filter((re) => !B.has(re.id));
    g(Y), U(x, Y);
  }, Xe = se((L) => {
    if (a) return;
    const B = b.filter((Y) => Y.id !== L);
    g(B), U(x, B);
  }, [U, b, a, x]), De = se((L, B, Y) => {
    c && S({ kind: "spliceEdge", edgeId: L, clientX: B, clientY: Y });
  }, [c]), Nt = (L) => {
    const B = j;
    if (!B) return;
    S(null);
    const Y = O(B.clientX, B.clientY) ?? { x: 0, y: 0 };
    if (B.kind === "fromEmpty") {
      const Q = H(L, Y), pe = [...x.map((we) => we.selected ? { ...we, selected: !1 } : we), Q.node];
      w(pe), p(Q.node.id), U(pe, b, [Q.activityNode]);
      return;
    }
    if (B.kind === "fromPort") {
      const Q = x.find((Te) => Te.id === B.sourceNodeId), xe = Q ? Xc(Q) : Y, pe = H(L, xe), Se = [...x.map((Te) => Te.selected ? { ...Te, selected: !1 } : Te), pe.node], qe = [...b, qi(B.sourceNodeId, pe.node.id, B.sourceHandleId ?? "Done")];
      w(Se), g(qe), p(pe.node.id), U(Se, qe, [pe.activityNode]);
      return;
    }
    const re = b.find((Q) => Q.id === B.edgeId);
    re && J(L, re, Y);
  }, pn = ue(() => ({
    highlightedEdgeId: I,
    deleteEdge: Xe,
    requestInsertActivity: De
  }), [Xe, I, De]);
  return {
    nodes: x,
    edges: b,
    canvasRef: M,
    setReactFlowInstance: N,
    connectMenu: j,
    setConnectMenu: S,
    edgeActions: pn,
    onNodesChange: ne,
    onEdgesChange: ge,
    onNodesDelete: Oe,
    onEdgesDelete: Ne,
    isValidConnection: ye,
    onConnect: Ie,
    onConnectStart: Ye,
    onConnectEnd: Ue,
    onReconnect: Me,
    commitLayout: _e,
    canAutoLayout: Ve,
    autoLayout: at,
    onCanvasDragOver: te,
    onCanvasDragLeave: ie,
    onCanvasDrop: fe,
    openEmptyConnectMenu: V,
    onConnectMenuPick: Nt,
    addActivity: W,
    onPaletteClick: le,
    onPaletteDragStart: R,
    onPaletteDragEnd: X,
    onPalettePointerDown: de
  };
}
const Gc = "elsa-studio:apply-workflow-graph-operation-batch", Jc = "elsa-studio:undo-workflow-graph-operation-batch", wN = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function vN(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const i = IN(e), r = ff(i.state.rootActivity), s = /* @__PURE__ */ new Map(), a = /* @__PURE__ */ new Map(), c = [];
  for (const u of t.operations) {
    const l = EN(u.kind), d = u.parameters ?? {};
    if (l === "add-activity") {
      const f = Le(d.activityId) ?? u.temporaryReferences?.[0], p = kN(f ?? Le(d.displayName) ?? Le(d.activityType) ?? "weaver-activity", r), h = bN(u, p, n);
      a.set(p, h), c.push(p), f && s.set(f, p), i.state.rootActivity && jN(i.state.rootActivity, h);
      const m = bt(d.position) ? Zo(d.position, { x: 280, y: 160 }) : null;
      m && (i.layout = Qc(i.layout, p, m));
      continue;
    }
    if (l === "set-root") {
      const f = Ao(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown root activity.");
      i.state.rootActivity = f;
      continue;
    }
    if (l === "set-designer-position") {
      const f = zt(d.activityId, s);
      if (!f || !Os(i.state.rootActivity, f)) throw new Error("Weaver batch referenced an unknown activity position.");
      i.layout = Qc(i.layout, f, Zo(d, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const f = Ao(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity property target.");
      CN(f, Le(d.propertyName) ?? "Value", d.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const f = Ao(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity update target.");
      const p = bt(d.patch) ? d.patch : d;
      Object.assign(f, p);
      continue;
    }
    if (l === "remove-activity") {
      const f = zt(d.activityId, s);
      if (!f) throw new Error("Weaver batch referenced an unknown activity remove target.");
      i.state.rootActivity = df(i.state.rootActivity, f), i.layout = i.layout.filter((p) => p.nodeId !== f);
      continue;
    }
    if (l === "connect-activities") {
      NN(i, d, s);
      continue;
    }
    if (l === "disconnect-activities") {
      SN(i, d, s);
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
function bN(e, t, n) {
  const i = e.parameters ?? {}, r = Le(i.activityVersionId) ?? Le(i.activityType) ?? "Elsa.Workflows.Activity", s = n.find((a) => a.activityVersionId === r || a.activityTypeKey === r || a.displayName === Le(i.displayName));
  return s ? Xi(s, t) : {
    nodeId: t,
    // catalogItem is provably undefined here (the truthy branch returned above), so use the id directly.
    activityVersionId: r,
    inputs: [],
    outputs: [],
    ...Le(i.displayName) ? { displayName: Le(i.displayName) } : {},
    designer: { position: Zo(i.position, { x: 280, y: 160 }) }
  };
}
function jN(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = Hs(e);
  n && !n.some((i) => i.nodeId === t.nodeId) && n.push(t);
}
function NN(e, t, n) {
  const i = e.state.rootActivity;
  if (!i?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const r = zt(t.sourceActivityId ?? t.sourceId ?? t.from, n), s = zt(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!r || !s) throw new Error("Weaver batch connection is missing source or target activity.");
  const a = i.structure.payload, c = Array.isArray(a.connections) ? a.connections : [], u = Le(t.connectionId) ?? `flow-${r}-${s}`;
  a.connections = [
    ...c.filter((l) => !bt(l) || l.id !== u),
    {
      id: u,
      source: { nodeId: r, port: Le(t.outcome) ?? Le(t.sourcePort) ?? "Done" },
      target: { nodeId: s }
    }
  ];
}
function SN(e, t, n) {
  const i = e.state.rootActivity, r = i?.structure?.payload.connections;
  if (!Array.isArray(r)) return;
  const s = Le(t.connectionId), a = zt(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = zt(t.targetActivityId ?? t.targetId ?? t.to, n);
  i.structure.payload.connections = r.filter((u) => {
    if (!bt(u)) return !0;
    if (s && u.id === s) return !1;
    const l = bt(u.source) ? u.source.nodeId : void 0, d = bt(u.target) ? u.target.nodeId : void 0;
    return l !== a || d !== c;
  });
}
function CN(e, t, n) {
  const i = bt(n);
  e[Kl(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: i ? "Object" : "Literal", value: n }
  };
}
function Ao(e, t, n, i) {
  const r = zt(t, n);
  return r ? Os(e.state.rootActivity, r) ?? i.get(r) ?? null : null;
}
function zt(e, t) {
  const n = Le(e);
  return n ? t.get(n) ?? n : null;
}
function Os(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of pf(e)) {
    const i = Os(n, t);
    if (i) return i;
  }
  return null;
}
function df(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = Hs(e);
  if (n) {
    const i = n.map((r) => df(r, t)).filter((r) => !!r);
    n.splice(0, n.length, ...i);
  }
  return e;
}
function ff(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of pf(e)) ff(n, t);
  return t;
}
function pf(e) {
  return Hs(e) ?? [];
}
function Hs(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function Qc(e, t, n) {
  return [
    ...e.filter((i) => i.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function Zo(e, t) {
  const n = bt(e) ? e : {}, i = Number(n.x), r = Number(n.y);
  return {
    x: Number.isFinite(i) ? Math.max(40, Math.round(i)) : t.x,
    y: Number.isFinite(r) ? Math.max(40, Math.round(r)) : t.y
  };
}
function kN(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let i = n, r = 2;
  for (; t.has(i); )
    i = `${n}-${r}`, r += 1;
  return t.add(i), i;
}
function EN(e) {
  return typeof e == "number" ? wN[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Le(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function IN(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function bt(e) {
  return typeof e == "object" && e !== null;
}
function AN({
  draft: e,
  details: t,
  catalog: n,
  replaceDraftByBatch: i,
  setStatus: r,
  setError: s
}) {
  const a = oe(/* @__PURE__ */ new Map());
  ee(() => {
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
        const p = An(e), h = vN(e, d.batch, n), m = `weaver-batch-${Date.now()}`;
        a.current.set(m, p), i(h.draft, h.finalActivityIds.at(-1) ?? null), r(h.summary), s(""), d.respond({ ok: !0, result: { ...h, undoToken: m } });
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
    return window.addEventListener(Gc, c), window.addEventListener(Jc, u), () => {
      window.removeEventListener(Gc, c), window.removeEventListener(Jc, u);
    };
  }, [n, t, e, i, r, s]);
}
function _N({ context: e, draft: t, autosaveEnabledByDefault: n = !0, editDraft: i, setStatus: r, setError: s }) {
  const [a, c] = K(n), u = oe(""), l = oe(0), d = oe(Promise.resolve()), f = se((h) => {
    u.current = h ? We(h) : "";
  }, []), p = se(async (h, m) => {
    const v = async () => {
      const w = ++l.current, b = We(h);
      s("");
      try {
        const g = await Nh(e, h), y = We(g);
        return u.current = y, i(({ draft: N }) => !N || N.id !== g.id ? null : We(N) === b ? g : { ...N, validationErrors: g.validationErrors }), w === l.current && r(m), g;
      } catch (g) {
        throw w === l.current && (r(""), s(g instanceof Error ? g.message : String(g))), g;
      }
    }, x = d.current.then(v, v);
    return d.current = x.catch(() => {
    }), x;
  }, [e, i, r, s]);
  return ee(() => {
    if (!a || !t || We(t) === u.current) return;
    r("Autosaving...");
    const m = window.setTimeout(() => {
      p(t, "Autosaved").catch(() => {
      });
    }, cj);
    return () => window.clearTimeout(m);
  }, [a, t, p, r]), { saveDraft: p, autosaveEnabled: a, setAutosaveEnabled: c, markSaved: f };
}
function DN({ context: e, definitionId: t, resetHistory: n, loadDraft: i, markSaved: r, setError: s }) {
  const [a, c] = K(null), [u, l] = K([]), [d, f] = K([]), [p, h] = K(null), [m, v] = K(Li), [x, w] = K("loading"), b = se(async () => {
    s(""), w("loading");
    const [g, y, N, j, S] = await Promise.all([
      hh(e, t),
      dr(e),
      Mh(e).then(
        (_) => ({ ok: !0, descriptors: _ }),
        () => ({ ok: !1, descriptors: [] })
      ),
      Rh(e).then(
        (_) => ({ ok: !0, descriptors: _ }),
        () => ({ ok: !1, descriptors: Li })
      ),
      // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
      zl(e).then(
        (_) => _,
        () => null
      )
    ]), I = g.draft ?? null;
    c(g), r(I), n(I), i(I), l(y.activities ?? []), f(N.descriptors), h(S), v(j.descriptors.length > 0 ? j.descriptors : Li), w(N.ok ? "ready" : "failed");
  }, [e, t, n, i, r, s]);
  return ee(() => {
    b().catch((g) => s(g instanceof Error ? g.message : String(g)));
  }, [b, s]), {
    details: a,
    setDetails: c,
    catalog: u,
    activityDescriptors: d,
    availabilityDiagnostics: p,
    expressionDescriptors: m,
    descriptorStatus: x,
    reload: b
  };
}
function TN({ context: e, details: t, setDetails: n, setStatus: i }) {
  const r = oe(null), s = oe(null), a = oe({});
  ee(() => {
    r.current = t;
  }, [t]);
  const c = se(() => {
    s.current !== null && (window.clearTimeout(s.current), s.current = null);
    const l = a.current;
    a.current = {};
    const d = r.current?.definition;
    !d || l.name === void 0 && l.description === void 0 || jh(e, d.id, {
      name: l.name ?? d.name,
      description: l.description ?? d.description ?? null
    }).then((f) => n((p) => p && p.definition.id === f.definition.id ? { ...p, definition: f.definition } : p)).catch(() => i("Couldn't save name/description."));
  }, [e, n, i]), u = se((l) => {
    n((d) => d && { ...d, definition: { ...d.definition, ...l } }), a.current = { ...a.current, ...l }, s.current !== null && window.clearTimeout(s.current), s.current = window.setTimeout(c, 800);
  }, [c, n]);
  return ee(() => () => {
    c();
  }, [c]), { updateDefinitionMeta: u };
}
function $N({
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
  const m = se(() => {
    if (!t) return;
    const b = n?.definition.name;
    Ob(Lb(t, b), b), d("Exported workflow as JSON.");
  }, [t, n, d]), v = se(async () => {
    if (!(!t || i)) {
      l("saving"), d("Saving...");
      try {
        await r(t, "Saved");
      } catch {
      } finally {
        l("idle");
      }
    }
  }, [t, i, r, l, d]), x = se(async () => {
    if (!(!t || i)) {
      l("promoting"), d("Saving...");
      try {
        await r(t, "Saved"), d("Promoting...");
        const b = await Sh(e, t.id), g = await Ch(e, b.versionId);
        u(g.artifactId), d(`Published ${g.artifactVersion}`), await s();
      } catch (b) {
        d(""), f(b instanceof Error ? b.message : String(b));
      } finally {
        l("idle");
      }
    }
  }, [t, i, e, r, s, u, l, d, f]), w = se(async () => {
    if (!t?.state.rootActivity || i) return;
    const b = t, g = We(b);
    c(), d("Preparing test run...");
    try {
      l("testRunPreparing"), d("Preparing test run...");
      const y = Rj(b);
      l("testRunStarting"), d("Starting test run...");
      const N = await kh(e, {
        definitionId: b.definitionId,
        snapshotId: y,
        state: b.state
      });
      a({ draftSignature: g, view: N }), p("runtime"), h(!1), d(Ls(N) ? "Test run rejected" : "Test run dispatched");
    } catch (y) {
      d(""), f(y instanceof Error ? y.message : String(y));
    } finally {
      l("idle");
    }
  }, [t, i, e, c, a, p, h, l, d, f]);
  return { exportJson: m, save: v, promoteAndPublish: x, run: w };
}
function PN({
  context: e,
  draft: t,
  frames: n,
  selectedNodeId: i,
  catalog: r,
  activityDescriptors: s,
  availabilityDiagnostics: a
}) {
  const c = t?.state.rootActivity ?? null, u = ue(() => new Map(r.map((k) => [k.activityVersionId, k])), [r]), l = se(
    (k) => rg([k.activityVersionId, k.activityTypeKey], a),
    [a]
  ), d = ue(() => kj(s), [s]), f = ue(() => bl(c, n, u), [c, n, u]), p = El(f, f ? u.get(f.activityVersionId) : void 0), h = !!f && p === "unsupported", m = ue(() => h ? null : Ln(c, n, u), [c, n, u, h]), v = ue(() => h && f?.nodeId === i ? f : m?.slot.activities.find((k) => k.nodeId === i) ?? null, [h, m, f, i]), x = ue(
    () => v ? Fc(v, u, d) : null,
    [u, d, v]
  ), w = v ? $e(v, u) : [], b = v ?? f, g = !v && !!f, y = ue(
    () => b ? Fc(b, u, d) : null,
    [u, d, b]
  ), N = ue(
    () => b ? l({ activityVersionId: b.activityVersionId, activityTypeKey: u.get(b.activityVersionId)?.activityTypeKey }) : null,
    [l, u, b]
  ), j = b ? $e(b, u) : [], S = b ? th(b, u.get(b.activityVersionId)) : !1, I = mh(e, t?.state, v?.nodeId ?? null, u), _ = !h && m?.slot.mode === "flowchart";
  return {
    catalogByVersion: u,
    availabilityLookup: l,
    scopeOwner: f,
    isUnsupportedDesigner: h,
    scope: m,
    selectedNode: v,
    selectedDescriptor: x,
    selectedSlots: w,
    inspectedNode: b,
    inspectedIsScopeOwner: g,
    inspectedDescriptor: y,
    inspectedNodeAvailability: N,
    inspectedSlots: j,
    inspectedSupportsScopedVariables: S,
    scopedVariableAnalysis: I,
    isFlowchartDesigner: _,
    canAddActivitiesToCanvas: !c || !h
  };
}
function MN({
  details: e,
  draft: t,
  selectedNode: n,
  selectedNodeId: i,
  selectedDescriptor: r,
  catalogByVersion: s
}) {
  ee(() => {
    if (!(!e || !t))
      return window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
        workflowId: e.definition.id,
        workflowDefinitionId: e.definition.id,
        workflowVersionId: t.sourceVersionId ?? null,
        draftId: t.id,
        revision: $j(t),
        selectedNodeId: i,
        selectedActivityType: r?.typeName ?? (n ? s.get(n.activityVersionId)?.activityTypeKey ?? n.activityVersionId : null),
        summary: e.definition.name,
        activities: nf(t.state.rootActivity, s),
        connections: rf(t.state.rootActivity, s),
        diagnostics: t.validationErrors.map((a) => ({ severity: a.code ?? "warning", message: a.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === e.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [s, e, t, r, n, i]);
}
function RN({
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
      /* @__PURE__ */ o.jsx(ar, { size: 14, "aria-hidden": "true" }),
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
              f ? /* @__PURE__ */ o.jsx(ul, { size: 14 }) : /* @__PURE__ */ o.jsx(xt, { size: 14 }),
              /* @__PURE__ */ o.jsx("span", { children: d.category }),
              /* @__PURE__ */ o.jsx("small", { children: d.activities.length })
            ]
          }
        ),
        f ? /* @__PURE__ */ o.jsx("div", { className: "wf-palette-activities", role: "group", children: d.activities.map((p) => {
          const h = p.description?.trim(), m = h ? `wf-palette-description-${p.activityVersionId}` : void 0, v = ke(p), x = un(p);
          return /* @__PURE__ */ o.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-activity",
              role: "treeitem",
              draggable: !0,
              title: h || ke(p),
              "aria-describedby": m,
              onClick: () => s(p),
              onDragStart: (w) => a(w, p),
              onDragEnd: (w) => c(w, p),
              onPointerDown: (w) => u(w, p),
              children: [
                /* @__PURE__ */ o.jsx("span", { className: "wf-activity-icon", "data-icon": x, "aria-hidden": "true", children: pr(x) }),
                /* @__PURE__ */ o.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ o.jsx("strong", { children: v }),
                  h ? /* @__PURE__ */ o.jsx("small", { id: m, children: h }) : null
                ] }),
                /* @__PURE__ */ o.jsx(dl, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            p.activityVersionId
          );
        }) }) : null
      ] }, d.category);
    }) })
  ] });
}
const hf = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]), LN = "Variable";
function zN({
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
  const d = XN(l), f = r.length > 0 ? r : og;
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ o.jsx("span", { className: "wf-section-label", children: "Properties" }),
    d.map((p) => /* @__PURE__ */ o.jsxs("section", { className: "wf-property-group", children: [
      d.length > 1 ? /* @__PURE__ */ o.jsx("h4", { children: p.category }) : null,
      p.inputs.map((h) => /* @__PURE__ */ o.jsx(
        VN,
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
function VN({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: i,
  expressionDescriptors: r,
  visibleVariables: s,
  scopeStatus: a,
  onChange: c
}) {
  const u = t.isReadOnly === !0, l = { activity: e, expressionDescriptors: r, readOnly: u }, d = es(n, t, l), f = d?.component, p = t.isWrapped !== !1 ? Xl(e, t) : null, h = p?.expression.type ?? "Literal", m = lg(e, t), v = h.toLowerCase(), w = p && (v === "literal" || v === "object") && !pg(t) ? dg(t.typeName) : null, b = w ? es(n, t, { ...l, scope: "collection" }) : void 0, g = p ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: r,
    readOnly: u,
    surface: "inline",
    syntax: h
  } : null, y = g ? mf(i, g) : null, N = y?.surfaces.inline, j = y && g ? yf(y, g, m) : [], S = w != null, I = !!(p && !S && qN(t, d?.id)), _ = !!(p && !S && YN(t, d?.id)), [M, k] = K(!1), $ = (E) => {
    const D = p ? ag(p, E) : E;
    c(ya(e, t, D));
  }, z = (E) => {
    p && c(ya(e, t, cg(p, E)));
  }, C = w ? b ? Go(b.component, t, m, u, { ...l, scope: "collection" }, $) : /* @__PURE__ */ o.jsx(
    HN,
    {
      input: t,
      elementTypeName: w.elementTypeName,
      value: m,
      editors: n,
      context: l,
      disabled: u,
      onChange: $
    }
  ) : null, A = h === LN && p ? /* @__PURE__ */ o.jsx(
    BN,
    {
      value: m,
      visibleVariables: s,
      scopeStatus: a,
      disabled: u,
      onChange: $
    }
  ) : C ?? (N && g ? /* @__PURE__ */ o.jsx(
    N,
    {
      descriptor: t,
      syntax: h,
      value: m,
      disabled: u,
      context: g,
      onChange: $
    }
  ) : Go(f, t, m, u, l, $));
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ o.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ o.jsx("span", { children: hs(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ o.jsx("p", { children: t.description }) : null,
    p && !I ? /* @__PURE__ */ o.jsx(
      Jo,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: h,
        descriptors: r,
        disabled: u,
        onChange: z
      }
    ) : null,
    I ? /* @__PURE__ */ o.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-expression-editor", children: [
        A,
        ts(j)
      ] }),
      /* @__PURE__ */ o.jsx(
        Jo,
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
          onClick: () => k(!0),
          children: /* @__PURE__ */ o.jsx(Pn, { size: 13 })
        }
      ) : null
    ] }) : /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
      A,
      ts(j)
    ] }),
    _ && !I ? /* @__PURE__ */ o.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => k(!0),
        children: [
          /* @__PURE__ */ o.jsx(Pn, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    M ? /* @__PURE__ */ o.jsx(
      WN,
      {
        input: t,
        value: m,
        syntax: h,
        descriptors: r,
        activity: e,
        expressionEditors: i,
        disabled: u,
        onChange: $,
        onSyntaxChange: z,
        onClose: () => k(!1)
      }
    ) : null
  ] });
}
function ON(e, t, n) {
  return [
    "wf-collection-item",
    t === e ? "dragging" : "",
    t !== null && t !== e && n === e ? "drop-target" : ""
  ].filter(Boolean).join(" ");
}
function HN({
  input: e,
  elementTypeName: t,
  value: n,
  editors: i,
  context: r,
  disabled: s,
  onChange: a
}) {
  const c = hg(n), u = mg(e, t), l = { ...r, scope: "element" }, d = es(i, u, l)?.component, f = e.displayName || e.name, p = (N, j) => a(c.map((S, I) => I === N ? j : S)), [h, m] = K(null), [v, x] = K(null), w = () => {
    m(null), x(null);
  }, b = (N) => (j) => {
    m(N), j.dataTransfer.effectAllowed = "move", j.dataTransfer.setData("text/plain", String(N));
  }, g = (N) => (j) => {
    h !== null && (j.preventDefault(), j.dataTransfer.dropEffect = "move", v !== N && x(N));
  }, y = (N) => (j) => {
    j.preventDefault(), h !== null && h !== N && a(ho(c, h, N)), w();
  };
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-collection-editor", children: [
    c.length === 0 ? /* @__PURE__ */ o.jsx("p", { className: "wf-collection-empty", children: "No items yet." }) : /* @__PURE__ */ o.jsx("ul", { className: "wf-collection-items", children: c.map((N, j) => /* @__PURE__ */ o.jsxs(
      "li",
      {
        className: ON(j, h, v),
        onDragOver: g(j),
        onDrop: y(j),
        children: [
          /* @__PURE__ */ o.jsx(
            "span",
            {
              className: "wf-collection-item-handle",
              draggable: !s,
              "aria-label": `Drag ${f} item ${j + 1} to reorder`,
              title: "Drag to reorder",
              onDragStart: b(j),
              onDragEnd: w,
              children: /* @__PURE__ */ o.jsx(dl, { size: 13, "aria-hidden": "true" })
            }
          ),
          /* @__PURE__ */ o.jsx("div", { className: "wf-collection-item-editor", children: Go(d, u, N, s, l, (S) => p(j, S)) }),
          /* @__PURE__ */ o.jsxs("div", { className: "wf-collection-item-actions", children: [
            /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${j + 1} up`,
                disabled: s || j === 0,
                onClick: () => a(ho(c, j, j - 1)),
                children: /* @__PURE__ */ o.jsx(Hf, { size: 13 })
              }
            ),
            /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${j + 1} down`,
                disabled: s || j === c.length - 1,
                onClick: () => a(ho(c, j, j + 1)),
                children: /* @__PURE__ */ o.jsx(ul, { size: 13 })
              }
            ),
            /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button danger",
                "aria-label": `Remove ${f} item ${j + 1}`,
                disabled: s,
                onClick: () => a(c.filter((S, I) => I !== j)),
                children: /* @__PURE__ */ o.jsx($n, { size: 13 })
              }
            )
          ] })
        ]
      },
      j
    )) }),
    /* @__PURE__ */ o.jsxs(
      "button",
      {
        type: "button",
        className: "wf-collection-add",
        disabled: s,
        onClick: () => a([...c, gg(t)]),
        children: [
          /* @__PURE__ */ o.jsx(Gt, { size: 13 }),
          " Add item"
        ]
      }
    )
  ] });
}
function WN({
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
  const d = al(), f = e.displayName || e.name, p = {
    activity: r,
    descriptor: e,
    expressionDescriptors: i,
    readOnly: a,
    surface: "expanded",
    syntax: n
  }, h = mf(s, p), m = h?.surfaces.expanded, v = h ? yf(h, p, t) : [], x = m ? null : KN(s, p);
  return ee(() => {
    const w = (b) => {
      b.key === "Escape" && l();
    };
    return window.addEventListener("keydown", w), () => window.removeEventListener("keydown", w);
  }, [l]), /* @__PURE__ */ o.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ o.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": d, children: [
    /* @__PURE__ */ o.jsxs("header", { children: [
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ o.jsx("h3", { id: d, children: f })
      ] }),
      /* @__PURE__ */ o.jsx("button", { type: "button", "aria-label": `Close ${f} editor`, onClick: l, children: /* @__PURE__ */ o.jsx(ss, { size: 16 }) })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ o.jsx(
          Jo,
          {
            label: `${f} expression syntax`,
            value: n,
            descriptors: i,
            disabled: a,
            onChange: u
          }
        ),
        /* @__PURE__ */ o.jsx("span", { children: hs(e.typeName) })
      ] }),
      e.description ? /* @__PURE__ */ o.jsx("p", { children: e.description }) : null,
      m ? /* @__PURE__ */ o.jsx(
        m,
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
            onChange: (w) => c(w.target.value)
          }
        )
      ] }),
      ts(v)
    ] }),
    /* @__PURE__ */ o.jsxs("footer", { children: [
      /* @__PURE__ */ o.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: l, children: "Close" })
    ] })
  ] }) });
}
function Go(e, t, n, i, r, s) {
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
function Jo({
  label: e,
  value: t,
  descriptors: n,
  disabled: i,
  variant: r = "block",
  onChange: s
}) {
  const [a, c] = K(!1), u = al(), l = n.find((f) => f.type === t), d = [
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
const Qo = "::";
function gf(e) {
  return !e || e === Ui ? Ui : e;
}
function el(e, t) {
  return `${gf(t)}${Qo}${e}`;
}
function FN(e) {
  const t = e.indexOf(Qo);
  if (t < 0) return null;
  const n = e.slice(t + Qo.length);
  return n ? { scopeId: e.slice(0, t), referenceKey: n } : null;
}
function BN({ value: e, visibleVariables: t, scopeStatus: n, disabled: i, onChange: r }) {
  const s = Ml(e), a = !!e && typeof e == "object" || typeof e == "string" && e.trim().startsWith("{"), c = s && (a || t.some((d) => d.referenceKey === s.referenceKey)) ? s : null, u = c ? el(c.referenceKey, c.declaringScopeId) : "", l = !!c && t.some(
    (d) => d.referenceKey === c.referenceKey && d.scopeId === gf(c.declaringScopeId)
  );
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-variable-picker", children: [
    /* @__PURE__ */ o.jsxs(
      "select",
      {
        "aria-label": "Variable reference",
        value: u,
        disabled: i,
        onChange: (d) => {
          const f = FN(d.target.value);
          f && r(ih(f.referenceKey, f.scopeId));
        },
        children: [
          /* @__PURE__ */ o.jsx("option", { value: "", children: "Select a variable…" }),
          c && !l ? /* @__PURE__ */ o.jsxs("option", { value: u, children: [
            c.referenceKey,
            " (not visible from this scope)"
          ] }) : null,
          t.map((d) => {
            const f = el(d.referenceKey, d.scopeId);
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
function es(e, t, n) {
  return [...e].sort((i, r) => (i.order ?? 500) - (r.order ?? 500)).find((i) => i.supports(t, n));
}
function mf(e, t) {
  return [...e].sort((n, i) => (n.order ?? 500) - (i.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function yf(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function KN(e, t) {
  if (t.syntax.toLowerCase() === "literal") return null;
  const n = [...e].sort((a, c) => (a.order ?? 500) - (c.order ?? 500)).find((a) => a.supports(t) && a.metadata)?.metadata;
  if (!n) return `No enhanced editor is registered for ${t.syntax}. Using the generic text editor.`;
  const i = n.displayName?.trim() || "enhanced editor", r = n.installHint?.trim(), s = `No ${i} is registered for ${t.syntax}. Using the generic text editor.`;
  return r ? `${s} ${r}` : s;
}
function ts(e) {
  return e.length === 0 ? null : /* @__PURE__ */ o.jsx("div", { className: "wf-expression-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const i = t.severity ?? "info";
    return /* @__PURE__ */ o.jsxs("p", { className: `wf-expression-editor-diagnostic ${i}`, children: [
      t.code ? /* @__PURE__ */ o.jsx("span", { children: t.code }) : null,
      t.message
    ] }, `${t.code ?? "diagnostic"}-${n}`);
  }) });
}
function XN(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const i = n.category?.trim() || "General";
    t.set(i, [...t.get(i) ?? [], n]);
  }
  return [...t.entries()].map(([n, i]) => ({ category: n, inputs: i }));
}
function qN(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !hf.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function YN(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !hf.has(t) && n !== "multiline") return !1;
  const i = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(i) || n === "singleline" || n === "multiline";
}
function UN({
  context: e,
  selectedNode: t,
  selectedNodeLabel: n,
  selectedActivityType: i,
  selectedDescriptor: r,
  selectedNodeAvailability: s,
  selectedSlots: a,
  inspectingScopeOwner: c = !1,
  catalog: u,
  catalogByVersion: l,
  selectedSupportsScopedVariables: d,
  propertyEditors: f,
  expressionEditors: p,
  expressionDescriptors: h,
  descriptorStatus: m,
  scopedVariableAnalysis: v,
  onSelectedActivityChange: x,
  onEnterSlot: w,
  onReplaceSlotActivity: b
}) {
  const [g, y] = K(null);
  return g && g.nodeId !== t?.nodeId && y(null), t ? /* @__PURE__ */ o.jsxs("div", { className: "wf-inspector-content", children: [
    /* @__PURE__ */ o.jsx("h3", { children: n }),
    c ? /* @__PURE__ */ o.jsx("p", { className: "wf-muted wf-inspector-owner-hint", children: "Container of this canvas — select a node to inspect it instead." }) : null,
    /* @__PURE__ */ o.jsxs("dl", { children: [
      /* @__PURE__ */ o.jsx("dt", { children: "Node ID" }),
      /* @__PURE__ */ o.jsx("dd", { children: t.nodeId }),
      /* @__PURE__ */ o.jsx("dt", { children: "Activity type" }),
      /* @__PURE__ */ o.jsx("dd", { children: i }),
      /* @__PURE__ */ o.jsx("dt", { children: "Activity version" }),
      /* @__PURE__ */ o.jsx("dd", { children: t.activityVersionId })
    ] }),
    s ? /* @__PURE__ */ o.jsxs("div", { className: "wf-availability-notice", children: [
      /* @__PURE__ */ o.jsx(Tn, { size: 14 }),
      /* @__PURE__ */ o.jsxs("span", { children: [
        "No longer available for new use · ",
        zn(s.state)
      ] })
    ] }) : null,
    /* @__PURE__ */ o.jsx(
      zN,
      {
        activity: t,
        descriptor: r,
        editors: f,
        expressionEditors: p,
        expressionDescriptors: h,
        descriptorStatus: m,
        visibleVariables: v.visibleVariables,
        scopeStatus: v.status,
        onChange: x
      }
    ),
    d ? /* @__PURE__ */ o.jsx("div", { className: "wf-container-variables", children: /* @__PURE__ */ o.jsx(
      rj,
      {
        context: e,
        variables: Pl(t),
        title: "Container variables",
        addLabel: "Add container variable",
        emptyLabel: "No container variables declared on this activity.",
        warnings: oh(v.shadowingWarnings, t.nodeId),
        onChange: (N) => x(nh(t, N))
      }
    ) }) : null,
    a.length > 0 ? /* @__PURE__ */ o.jsxs("div", { className: "wf-slot-list", children: [
      /* @__PURE__ */ o.jsx("span", { children: "Embedded slots" }),
      a.map((N) => {
        const j = `${n} / ${N.label}`;
        return /* @__PURE__ */ o.jsxs("div", { className: "wf-slot-row", children: [
          /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => w(t.nodeId, N, j), children: [
            N.label,
            /* @__PURE__ */ o.jsx("small", { children: Mj(N, l) })
          ] }),
          N.cardinality === "single" ? /* @__PURE__ */ o.jsx(
            "button",
            {
              type: "button",
              className: "wf-slot-change",
              "aria-label": `${N.activities.length > 0 ? "Change" : "Choose"} ${N.label} activity`,
              title: N.activities.length > 0 ? "Change activity" : "Choose activity",
              onClick: (S) => y({ nodeId: t.nodeId, slotId: N.id, clientX: S.clientX, clientY: S.clientY }),
              children: /* @__PURE__ */ o.jsx(Wf, { size: 14 })
            }
          ) : null
        ] }, N.id);
      })
    ] }) : /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." }),
    g ? /* @__PURE__ */ o.jsx(
      cf,
      {
        clientX: g.clientX,
        clientY: g.clientY,
        activities: u,
        onPick: (N) => {
          y(null);
          const j = a.find((S) => S.id === g.slotId);
          j && b(t.nodeId, j, `${n} / ${j.label}`, N);
        },
        onClose: () => y(null)
      }
    ) : null
  ] }) : /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
}
const xf = [
  { value: "flowchart", label: "Flowchart", hint: "Free-form graph of connected activities." },
  { value: "sequence", label: "Sequence", hint: "Ordered list of activities that run top to bottom." }
];
function wf({ label: e, hint: t }) {
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsx("span", { className: "wf-root-card-title", children: e }),
    /* @__PURE__ */ o.jsx("span", { className: "wf-root-card-hint", children: t })
  ] });
}
function ZN({ value: e, onChange: t }) {
  return /* @__PURE__ */ o.jsx("div", { className: "wf-root-cards", role: "radiogroup", "aria-label": "Root activity", children: xf.map((n) => {
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
      /* @__PURE__ */ o.jsx(wf, { label: n.label, hint: n.hint })
    ] }, n.value);
  }) });
}
function GN({ onPick: e }) {
  return /* @__PURE__ */ o.jsx("div", { className: "wf-root-cards", role: "group", "aria-label": "Fill this slot", children: xf.map((t) => /* @__PURE__ */ o.jsx(
    "button",
    {
      type: "button",
      className: "wf-root-card",
      "aria-label": t.label,
      onClick: () => e(t.value),
      children: /* @__PURE__ */ o.jsx(wf, { label: t.label, hint: t.hint })
    },
    t.value
  )) });
}
function JN({ slotLabel: e, catalog: t, onPickActivity: n, onBrowseAll: i }) {
  const r = (s) => {
    const a = Zd(t, s);
    a && n(a);
  };
  return /* @__PURE__ */ o.jsx("div", { className: "wf-slot-empty", role: "group", "aria-label": `Fill ${e}`, children: /* @__PURE__ */ o.jsxs("div", { className: "wf-slot-empty-card", children: [
    /* @__PURE__ */ o.jsx("p", { className: "wf-slot-empty-title", children: "This slot is empty" }),
    /* @__PURE__ */ o.jsxs("p", { className: "wf-slot-empty-hint", children: [
      "Choose a container for ",
      /* @__PURE__ */ o.jsx("strong", { children: e }),
      ", or pick any activity."
    ] }),
    /* @__PURE__ */ o.jsx(GN, { onPick: r }),
    /* @__PURE__ */ o.jsxs(
      "button",
      {
        type: "button",
        className: "wf-slot-empty-browse",
        onClick: (s) => i({ clientX: s.clientX, clientY: s.clientY }),
        children: [
          /* @__PURE__ */ o.jsx(Zn, { size: 15 }),
          " Browse all activities…"
        ]
      }
    ),
    /* @__PURE__ */ o.jsx("p", { className: "wf-slot-empty-drag-hint", children: "Tip: you can also drag activities from the palette onto the canvas." })
  ] }) });
}
function QN({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: i,
  expressionEditors: r,
  workflowDesignerPanels: s,
  autosaveEnabledByDefault: a,
  onBack: c
}) {
  const u = cN(), { draft: l, frames: d, selectedNodeId: f, testRun: p, publishedArtifactId: h } = u.state, {
    loadDraft: m,
    replaceDraftByBatch: v,
    editDraft: x,
    editDraftAndSelect: w,
    select: b,
    navigateToScope: g,
    resetToRoot: y,
    startTestRun: N,
    clearTestRun: j,
    setPublishedArtifact: S
  } = u, [I, _] = K(""), [M, k] = K(""), [$, z] = K("idle"), [C, A] = K(() => /* @__PURE__ */ new Set()), [E, D] = K(""), [P, T] = K("activities"), [F, W] = K("inspector"), [H, U] = K("designer"), {
    paletteWidth: O,
    inspectorWidth: q,
    paletteCollapsed: J,
    inspectorCollapsed: G,
    maximizedSidePanel: R,
    setInspectorCollapsed: X,
    paletteExpanded: de,
    inspectorExpanded: le,
    editorBodyClassName: te,
    editorBodyStyle: ie,
    toggleSidePanelCollapsed: fe,
    toggleSidePanelMaximized: V,
    startSidePanelResize: ne,
    handleSidePanelResizeKeyDown: ge
  } = uf(), { resetHistory: ye, undo: Ie, redo: _e, canUndoNow: Ve, canRedoNow: at } = oN({ draft: l, restoreDraft: m }), { saveDraft: Ye, autosaveEnabled: Ue, setAutosaveEnabled: Me, markSaved: Oe } = _N({ context: e, draft: l, autosaveEnabledByDefault: a, editDraft: x, setStatus: k, setError: _ }), {
    details: Ne,
    setDetails: Xe,
    catalog: De,
    activityDescriptors: Nt,
    availabilityDiagnostics: pn,
    expressionDescriptors: L,
    descriptorStatus: B,
    reload: Y
  } = DN({ context: e, definitionId: t, resetHistory: ye, loadDraft: m, markSaved: Oe, setError: _ }), { updateDefinitionMeta: re } = TN({ context: e, details: Ne, setDetails: Xe, setStatus: k }), {
    catalogByVersion: Q,
    availabilityLookup: xe,
    scopeOwner: pe,
    isUnsupportedDesigner: we,
    scope: Se,
    selectedNode: qe,
    selectedDescriptor: Te,
    selectedSlots: Er,
    inspectedNode: tt,
    inspectedIsScopeOwner: Ir,
    inspectedDescriptor: si,
    inspectedNodeAvailability: Ar,
    inspectedSlots: _r,
    inspectedSupportsScopedVariables: Dr,
    scopedVariableAnalysis: Tr,
    isFlowchartDesigner: ct,
    canAddActivitiesToCanvas: $r
  } = PN({ context: e, draft: l, frames: d, selectedNodeId: f, catalog: De, activityDescriptors: Nt, availabilityDiagnostics: pn }), Vt = ue(() => Yo(De), [De]), Pr = ue(() => {
    const Z = E.trim().toLowerCase();
    if (!Z) return Vt;
    const ce = De.filter((me) => ke(me).toLowerCase().includes(Z) || me.activityTypeKey.toLowerCase().includes(Z) || (me.category ?? "").toLowerCase().includes(Z) || (me.description ?? "").toLowerCase().includes(Z));
    return Yo(ce);
  }, [De, E, Vt]), Ot = $ !== "idle", Mr = !!l?.state.rootActivity && !Ot, ai = Rt(n, "weaver.workflows.find-draft-risks"), ci = Rt(n, "weaver.workflows.propose-update"), Rr = xN({
    draft: l,
    scope: Se,
    scopeOwner: pe,
    frames: d,
    catalog: De,
    catalogByVersion: Q,
    isUnsupportedDesigner: we,
    isFlowchartDesigner: ct,
    canAddActivitiesToCanvas: $r,
    selectedNodeId: f,
    editDraft: x,
    editDraftAndSelect: w,
    select: b,
    resetToRoot: y,
    setStatus: k,
    setError: _
  }), {
    nodes: Ht,
    edges: li,
    canvasRef: ui,
    setReactFlowInstance: di,
    connectMenu: hn,
    setConnectMenu: Lr,
    edgeActions: zr,
    onNodesChange: Vr,
    onEdgesChange: Or,
    onNodesDelete: Hr,
    onEdgesDelete: fi,
    isValidConnection: Wr,
    onConnect: Fr,
    onConnectStart: Br,
    onConnectEnd: Kr,
    onReconnect: Xr,
    commitLayout: qr,
    canAutoLayout: Yr,
    autoLayout: Ur,
    onCanvasDragOver: gn,
    onCanvasDragLeave: pi,
    onCanvasDrop: hi,
    openEmptyConnectMenu: gi,
    onConnectMenuPick: Zr,
    addActivity: mi,
    onPaletteClick: Gr,
    onPaletteDragStart: Jr,
    onPaletteDragEnd: Qr,
    onPalettePointerDown: yi
  } = Rr, eo = !we && !ct && d.length > 0 && !!Se && Se.slot.activities.length === 0 && Ht.length === 0, xi = se((Z) => {
    const ce = mi(Z);
    if (!ce || !Se || !pe || d.length === 0) return;
    const me = d[d.length - 1].label, Ce = Mi(d, pe, pe.nodeId, { ...Se.slot, activities: [ce] }, me, Q);
    Ce && Ce.frames.length > d.length && g(Ce.frames, Ce.selectedNodeId);
  }, [mi, Q, d, g, Se, pe]);
  AN({ draft: l, details: Ne, catalog: De, replaceDraftByBatch: v, setStatus: k, setError: _ }), ee(() => {
    !l?.state.rootActivity || De.length === 0 || x(({ draft: Z }) => {
      if (!Z?.state.rootActivity) return null;
      const ce = Il(Z.state.rootActivity, Q);
      return !ce || ce === Z.state.rootActivity ? null : {
        ...Z,
        state: {
          ...Z.state,
          rootActivity: ce
        }
      };
    });
  }, [De.length, Q, l?.state.rootActivity, x]), MN({ details: Ne, draft: l, selectedNode: qe, selectedNodeId: f, selectedDescriptor: Te, catalogByVersion: Q }), ee(() => {
    A((Z) => {
      let ce = !1;
      const me = new Set(Z);
      for (const Ce of Vt)
        me.has(Ce.category) || (me.add(Ce.category), ce = !0);
      return ce ? me : Z;
    });
  }, [Vt]);
  const { exportJson: to, save: no, promoteAndPublish: mn, run: io } = $N({
    context: e,
    draft: l,
    details: Ne,
    busy: Ot,
    saveDraft: Ye,
    reload: Y,
    startTestRun: N,
    clearTestRun: j,
    setPublishedArtifact: S,
    setOperation: z,
    setStatus: k,
    setError: _,
    setActiveRightPanelId: W,
    setInspectorCollapsed: X
  }), ro = se((Z) => {
    x(({ draft: ce }) => ce ? { ...ce, state: Z(ce.state) } : null);
  }, [x]), wi = se((Z) => {
    if (!l) return "No draft is loaded.";
    const ce = Vb(Z, l);
    return ce.ok ? (m(ce.draft), k("Applied workflow JSON."), null) : ce.error;
  }, [l, m]);
  ee(() => {
    const Z = (ce) => {
      if (H !== "designer" || !(ce.metaKey || ce.ctrlKey)) return;
      const me = ce.target;
      if (me && (me.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(me.tagName))) return;
      const Ce = ce.key.toLowerCase();
      Ce === "z" && !ce.shiftKey ? (ce.preventDefault(), Ie()) : (Ce === "z" && ce.shiftKey || Ce === "y") && (ce.preventDefault(), _e());
    };
    return window.addEventListener("keydown", Z), () => window.removeEventListener("keydown", Z);
  }, [H, Ie, _e]);
  const oo = se((Z, ce, me) => {
    const Ce = Mi(d, pe, Z, ce, me, Q);
    Ce && g(Ce.frames, Ce.selectedNodeId);
  }, [Q, d, g, pe]), bf = se((Z, ce, me, Ce) => {
    const bi = ce.activities.length > 0, Ys = Xi(Ce, Uo(Ce));
    x(({ draft: ji }) => {
      const Us = ji?.state.rootActivity;
      return !ji || !Us ? null : {
        ...ji,
        state: {
          ...ji.state,
          rootActivity: $o(Us, Z, (_f) => ln(_f, ce, [Ys]), Q)
        }
      };
    });
    const co = Mi(d, pe, Z, { ...ce, activities: [Ys] }, me, Q);
    co && g(co.frames, co.selectedNodeId), _(""), k(bi ? `Replaced ${ce.label} content` : `Assigned ${ke(Ce)} to ${ce.label}`);
  }, [Q, x, d, g, pe]), jf = ue(() => we ? null : (Z, ce, me) => oo(Z, me, `${ce} / ${me.label}`), [oo, we]), Nf = se((Z) => {
    x(({ draft: ce }) => {
      const me = ce?.state.rootActivity;
      return !ce || !me ? null : {
        ...ce,
        state: {
          ...ce.state,
          rootActivity: $o(me, Z.nodeId, () => Z, Q)
        }
      };
    });
  }, [Q, x]), Sf = se((Z) => {
    if (!Z) return;
    const ce = l?.state.rootActivity;
    if (!ce) return;
    const me = Cp(ce, Z, (Ce) => {
      const bi = Q.get(Ce.activityVersionId);
      return bi ? ke(bi) : Ce.nodeId;
    }, Q);
    me && (U("designer"), g(me, Z), X(!1));
  }, [l?.state.rootActivity, Q, X, g]), Cf = (Z) => {
    A((ce) => {
      const me = new Set(ce);
      return me.has(Z) ? me.delete(Z) : me.add(Z), me;
    });
  };
  if (!Ne || !l)
    return /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: I || "Loading workflow editor..." });
  const Fs = tt ? Q.get(tt.activityVersionId) : void 0, kf = tt ? Ht.find((Z) => Z.id === tt.nodeId)?.data.label ?? (Fs ? ke(Fs) : tt.nodeId) : "", vi = p?.draftSignature === We(l) ? p.view : null, Bs = vi && M.startsWith("Test run") ? "" : M, Ef = (Z) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(Z)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, If = {
    definition: Ne.definition,
    draft: l,
    selectedActivity: qe,
    selectedActivityDescriptor: Te,
    selectedActivitySlots: Er,
    catalog: De,
    currentScopeOwner: pe,
    frames: d
  }, Ks = s.map((Z) => {
    const ce = Z.component;
    return {
      id: Z.id,
      title: Z.title,
      side: Z.side,
      order: Z.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ o.jsx(ce, { context: If })
    };
  }), so = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ o.jsx(Zn, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(
        RN,
        {
          paletteSearch: E,
          onSearchChange: D,
          groups: Pr,
          expandedCategories: C,
          onToggleCategory: Cf,
          onActivityClick: Gr,
          onActivityDragStart: Jr,
          onActivityDragEnd: Qr,
          onActivityPointerDown: yi
        }
      )
    },
    ...Ks.filter((Z) => Z.side === "left")
  ].sort(qc), ao = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ o.jsx(is, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(
        UN,
        {
          context: e,
          selectedNode: tt,
          selectedNodeLabel: kf,
          selectedActivityType: tt ? si?.typeName ?? Q.get(tt.activityVersionId)?.activityTypeKey ?? "Unknown" : "",
          selectedDescriptor: si,
          selectedNodeAvailability: Ar,
          selectedSlots: _r,
          inspectingScopeOwner: Ir,
          catalog: De,
          catalogByVersion: Q,
          selectedSupportsScopedVariables: Dr,
          propertyEditors: i,
          expressionEditors: r,
          expressionDescriptors: L,
          descriptorStatus: B,
          scopedVariableAnalysis: Tr,
          onSelectedActivityChange: Nf,
          onEnterSlot: oo,
          onReplaceSlotActivity: bf
        }
      )
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: /* @__PURE__ */ o.jsx(Zt, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(Zj, { testRun: vi, onOpenRun: Ef })
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ o.jsx(fl, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(
        Qj,
        {
          context: e,
          ai: n,
          definitionId: Ne.definition.id,
          publishedArtifactId: h
        }
      )
    },
    ...Ks.filter((Z) => Z.side === "right")
  ].sort(qc), Xs = so.find((Z) => Z.id === P) ?? so[0], qs = ao.find((Z) => Z.id === F) ?? ao[0], Af = [
    { id: "designer", title: "Designer", order: 0, icon: /* @__PURE__ */ o.jsx(pl, { size: 14 }), render: () => null },
    { id: "code", title: "Code", order: 1, icon: /* @__PURE__ */ o.jsx(qf, { size: 14 }), render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: /* @__PURE__ */ o.jsx(os, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-link-button", onClick: c, children: "Definitions" }),
      /* @__PURE__ */ o.jsx(xt, { size: 14 }),
      /* @__PURE__ */ o.jsx("strong", { children: Ne.definition.name }),
      /* @__PURE__ */ o.jsx("span", { className: "wf-chip", children: "Draft" }),
      Bs ? /* @__PURE__ */ o.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ o.jsx(an, { size: 13 }),
        " ",
        Bs
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
              disabled: !Ve,
              onClick: Ie,
              children: /* @__PURE__ */ o.jsx(Ff, { size: 16 })
            }
          ),
          /* @__PURE__ */ o.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Redo",
              title: "Redo (Ctrl+Shift+Z)",
              disabled: !at,
              onClick: _e,
              children: /* @__PURE__ */ o.jsx(Bf, { size: 16 })
            }
          ),
          /* @__PURE__ */ o.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Auto-layout",
              title: "Auto-layout the canvas",
              disabled: !Yr,
              onClick: Ur,
              children: /* @__PURE__ */ o.jsx(Kf, { size: 16 })
            }
          )
        ] }),
        /* @__PURE__ */ o.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ o.jsx("input", { className: "wf-switch-input", type: "checkbox", checked: Ue, onChange: (Z) => Me(Z.target.checked) }),
          /* @__PURE__ */ o.jsx("span", { children: "Autosave" })
        ] }),
        ai ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Lt(n, ai, { definition: Ne.definition, draft: l }), children: [
          /* @__PURE__ */ o.jsx(ft, { size: 15 }),
          " Risks"
        ] }) : null,
        ci ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Lt(n, ci, { definition: Ne.definition, draft: l }), children: [
          /* @__PURE__ */ o.jsx(ft, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ o.jsxs("button", { type: "button", title: "Export workflow as JSON", onClick: to, children: [
          /* @__PURE__ */ o.jsx(Xf, { size: 15 }),
          " Export"
        ] }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", disabled: Ot, onClick: () => {
          no();
        }, children: [
          /* @__PURE__ */ o.jsx(rs, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", disabled: Ot, onClick: () => {
          mn();
        }, children: [
          /* @__PURE__ */ o.jsx(cl, { size: 15 }),
          " Promote"
        ] }),
        vi ? /* @__PURE__ */ o.jsx(
          Uj,
          {
            testRun: vi,
            onOpenDetails: () => {
              W("runtime"), X(!1);
            }
          }
        ) : null,
        /* @__PURE__ */ o.jsxs(
          "button",
          {
            type: "button",
            disabled: !Mr,
            title: l.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              io();
            },
            children: [
              /* @__PURE__ */ o.jsx(Zt, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    I ? /* @__PURE__ */ o.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ o.jsx(jt, { size: 16 }),
      " ",
      I
    ] }) : null,
    /* @__PURE__ */ o.jsxs("div", { className: te, style: ie, children: [
      /* @__PURE__ */ o.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ o.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ o.jsx(
            Ki,
            {
              label: "Activities panel tabs",
              tabs: so,
              activeTabId: Xs.id,
              onSelect: T
            }
          ),
          /* @__PURE__ */ o.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": J ? "Expand activities panel" : "Collapse activities panel",
                title: J ? "Expand" : "Collapse",
                onClick: () => fe("palette"),
                children: J ? /* @__PURE__ */ o.jsx(xt, { size: 14 }) : /* @__PURE__ */ o.jsx(Mn, { size: 14 })
              }
            ),
            J ? null : /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": R === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: R === "palette" ? "Restore" : "Maximize",
                onClick: () => V("palette"),
                children: R === "palette" ? /* @__PURE__ */ o.jsx(_o, { size: 14 }) : /* @__PURE__ */ o.jsx(Pn, { size: 14 })
              }
            )
          ] })
        ] }),
        de ? Xs.render() : null
      ] }),
      de && !R ? /* @__PURE__ */ o.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": kn,
          "aria-valuemax": En,
          "aria-valuenow": O,
          tabIndex: 0,
          onPointerDown: (Z) => ne("palette", Z),
          onKeyDown: (Z) => ge("palette", Z)
        }
      ) : /* @__PURE__ */ o.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ o.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ o.jsx("div", { className: "wf-canvas-tabs", children: /* @__PURE__ */ o.jsx(
          Ki,
          {
            label: "Editor view tabs",
            tabs: Af,
            activeTabId: H,
            onSelect: (Z) => U(Z)
          }
        ) }),
        H === "code" ? /* @__PURE__ */ o.jsx(Kb, { draft: l, onApply: wi }) : H === "properties" ? /* @__PURE__ */ o.jsx(sj, { details: Ne, draft: l, context: e, onStateChange: ro, onDefinitionMetaChange: re }) : /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
          /* @__PURE__ */ o.jsxs("div", { className: "wf-breadcrumb", children: [
            /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => y(), children: "Root" }),
            d.map((Z, ce) => Z.label ? (
              // Unlabelled frames are descent hops planSlotNavigation tucks under the next crumb
              // (entering a slot through its single container child); the visible crumb navigates to
              // the full hop chain, so hiding them keeps the trail one-entry-per-slot.
              /* @__PURE__ */ o.jsxs(Fe.Fragment, { children: [
                /* @__PURE__ */ o.jsx(xt, { size: 13 }),
                /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => g(d.slice(0, ce + 1), null), children: Z.label })
              ] }, `${Z.ownerNodeId}-${Z.slotId}-${ce}`)
            ) : null)
          ] }),
          /* @__PURE__ */ o.jsxs("div", { className: "wf-canvas", ref: ui, onDragOver: gn, onDragLeave: pi, onDrop: hi, children: [
            /* @__PURE__ */ o.jsx(Xd.Provider, { value: zr, children: /* @__PURE__ */ o.jsx(qd.Provider, { value: xe, children: /* @__PURE__ */ o.jsx(Yd.Provider, { value: jf, children: /* @__PURE__ */ o.jsxs(
              Rd,
              {
                nodes: Ht,
                edges: li,
                nodeTypes: sf,
                edgeTypes: af,
                onInit: di,
                onNodesChange: Vr,
                onEdgesChange: Or,
                onNodesDelete: Hr,
                onEdgesDelete: fi,
                onConnect: Fr,
                onConnectStart: ct ? Br : void 0,
                onConnectEnd: ct ? Kr : void 0,
                onReconnect: ct ? Xr : void 0,
                isValidConnection: Wr,
                onDragOver: gn,
                onDragLeave: pi,
                onDrop: hi,
                onPaneClick: () => b(null),
                onNodeClick: (Z, ce) => b(ce.id),
                onNodeDragStop: we ? void 0 : qr,
                minZoom: 0.2,
                maxZoom: 1.8,
                nodesConnectable: ct,
                nodesDraggable: !we,
                selectionOnDrag: !0,
                multiSelectionKeyCode: ["Shift", "Meta", "Control"],
                deleteKeyCode: we ? null : ["Backspace", "Delete"],
                panActivationKeyCode: null,
                defaultEdgeOptions: { type: "workflow" },
                children: [
                  /* @__PURE__ */ o.jsx(zd, { gap: 18, size: 1 }),
                  /* @__PURE__ */ o.jsx(Od, {}),
                  /* @__PURE__ */ o.jsx(Wd, { pannable: !0, zoomable: !0 })
                ]
              }
            ) }) }) }),
            eo ? /* @__PURE__ */ o.jsx(
              JN,
              {
                slotLabel: Se?.slot.label ?? "this slot",
                catalog: De,
                onPickActivity: xi,
                onBrowseAll: gi
              }
            ) : ct && Ht.length === 0 ? /* @__PURE__ */ o.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => gi(), children: [
              /* @__PURE__ */ o.jsx(Gt, { size: 15 }),
              " Add activity"
            ] }) : null,
            hn ? /* @__PURE__ */ o.jsx(
              cf,
              {
                clientX: hn.clientX,
                clientY: hn.clientY,
                activities: De,
                onPick: Zr,
                onClose: () => Lr(null)
              }
            ) : null
          ] }),
          /* @__PURE__ */ o.jsx(Yj, { draft: l, onRepair: Sf })
        ] })
      ] }),
      le && !R ? /* @__PURE__ */ o.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Xt,
          "aria-valuemax": qt,
          "aria-valuenow": q,
          tabIndex: 0,
          onPointerDown: (Z) => ne("inspector", Z),
          onKeyDown: (Z) => ge("inspector", Z)
        }
      ) : /* @__PURE__ */ o.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ o.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ o.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ o.jsx(
            Ki,
            {
              label: "Inspector panel tabs",
              tabs: ao,
              activeTabId: qs.id,
              onSelect: W
            }
          ),
          /* @__PURE__ */ o.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": G ? "Expand inspector panel" : "Collapse inspector panel",
                title: G ? "Expand" : "Collapse",
                onClick: () => fe("inspector"),
                children: G ? /* @__PURE__ */ o.jsx(Mn, { size: 14 }) : /* @__PURE__ */ o.jsx(xt, { size: 14 })
              }
            ),
            G ? null : /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": R === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: R === "inspector" ? "Restore" : "Maximize",
                onClick: () => V("inspector"),
                children: R === "inspector" ? /* @__PURE__ */ o.jsx(_o, { size: 14 }) : /* @__PURE__ */ o.jsx(Pn, { size: 14 })
              }
            )
          ] })
        ] }),
        le ? qs.render() : null
      ] })
    ] })
  ] });
}
function eS({ page: e, pageSize: t, totalCount: n, onPageChange: i, onPageSizeChange: r }) {
  const s = Ud(n, t), a = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
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
      /* @__PURE__ */ o.jsx("select", { value: t, onChange: (u) => r(Number(u.target.value)), children: uj.map((u) => /* @__PURE__ */ o.jsx("option", { value: u, children: u }, u)) })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => i(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ o.jsx(Mn, { size: 14 }),
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
        /* @__PURE__ */ o.jsx(xt, { size: 14 })
      ] })
    ] })
  ] });
}
function tS({ draft: e, creating: t, ai: n, suggestMetadataAction: i, onChange: r, onClose: s, onSubmit: a }) {
  const [c, u] = K(!1), [l, d] = K(""), [f, p] = K(!1), [h, m] = K(null), [v, x] = K(null), w = oe(null), b = oe(e);
  b.current = e;
  const g = oe(r);
  g.current = r;
  const y = se((j) => {
    const S = { ...b.current };
    j.name && (S.name = j.name), j.description && (S.description = j.description), g.current(S), m(null), x(null);
  }, []);
  ee(() => {
    if (i)
      return n.onPromptResult((j) => {
        if (j.requestId !== w.current) return;
        if (w.current = null, p(!1), j.status !== "completed") {
          x(j.status === "cancelled" ? "Weaver needs more detail — continue in the assistant panel." : "Weaver couldn't generate a suggestion. Try again or fill the fields manually.");
          return;
        }
        const S = gj(j.text);
        if (!S) {
          x("Couldn't read a suggestion from Weaver's reply. See the assistant panel.");
          return;
        }
        j.autoApply ? y(S) : m(S);
      });
  }, [n, i, y]);
  const N = () => {
    if (!i) return;
    const j = i.createPrompt({ draft: b.current, intent: l });
    if (!j) return;
    const S = `wf-suggest-${Date.now()}-${Math.round(Math.random() * 1e6)}`;
    w.current = S, p(!0), m(null), x(null), n.dispatchPrompt({ ...j, requestId: S });
  };
  return /* @__PURE__ */ o.jsx("div", { className: "wf-dialog-backdrop", role: "presentation", children: /* @__PURE__ */ o.jsx("section", { className: "wf-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "workflow-create-title", children: /* @__PURE__ */ o.jsxs(
    "form",
    {
      onSubmit: (j) => {
        j.preventDefault(), a();
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
              onClick: () => u((j) => !j),
              title: i.description ?? i.label,
              children: [
                /* @__PURE__ */ o.jsx(ft, { size: 13 }),
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
                onChange: (j) => d(j.target.value),
                onKeyDown: (j) => {
                  (j.metaKey || j.ctrlKey) && j.key === "Enter" && (j.preventDefault(), N());
                }
              }
            )
          ] }),
          /* @__PURE__ */ o.jsx("div", { className: "wf-ai-suggest-actions", children: /* @__PURE__ */ o.jsxs("button", { type: "button", className: "wf-ai-action", onClick: N, disabled: f, children: [
            /* @__PURE__ */ o.jsx(ft, { size: 13 }),
            " ",
            f ? "Generating…" : "Generate"
          ] }) }),
          v ? /* @__PURE__ */ o.jsx("p", { className: "wf-ai-suggest-error", role: "alert", children: v }) : null,
          h ? /* @__PURE__ */ o.jsxs("div", { className: "wf-ai-suggest-preview", children: [
            h.name ? /* @__PURE__ */ o.jsx("p", { children: /* @__PURE__ */ o.jsx("strong", { children: h.name }) }) : null,
            h.description ? /* @__PURE__ */ o.jsx("p", { children: h.description }) : null,
            /* @__PURE__ */ o.jsxs("div", { className: "wf-ai-suggest-actions", children: [
              /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => y(h), children: "Apply" }),
              /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => m(null), children: "Dismiss" })
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
              onChange: (j) => r({ ...e, name: j.target.value })
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
              onChange: (j) => r({ ...e, description: j.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ o.jsxs("fieldset", { className: "wf-form-field wf-root-field", children: [
          /* @__PURE__ */ o.jsx("legend", { children: "Root activity" }),
          /* @__PURE__ */ o.jsx(
            ZN,
            {
              value: e.rootKind,
              onChange: (j) => r({ ...e, rootKind: j, rootActivityVersionId: null })
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
function nS({ context: e, ai: t, onOpen: n }) {
  const [i, r] = K(""), [s, a] = K("active"), [c, u] = K(1), [l, d] = K(dj), [f, p] = K("loading"), [h, m] = K(""), [v, x] = K(""), [w, b] = K([]), [g, y] = K(0), [N, j] = K(() => /* @__PURE__ */ new Set()), [S, I] = K(null), [_, M] = K(!1), [k, $] = K([]), [z, C] = K("idle"), A = oe(null), E = ue(() => w.map((V) => V.id), [w]), D = Rt(t, "weaver.workflows.suggest-create-metadata"), P = Rt(t, "weaver.workflows.explain-definition"), T = E.filter((V) => N.has(V)).length, F = E.length > 0 && T === E.length, W = se(async () => {
    p("loading"), m("");
    try {
      const V = await ph(e, { search: i, state: s, page: c, pageSize: l }), ne = typeof V.totalCount == "number", ge = V.totalCount ?? V.definitions.length, ye = Ud(ge, l);
      if (ge > 0 && c > ye) {
        u(ye);
        return;
      }
      b(ne ? V.definitions : hj(V.definitions, c, l)), y(ge), p("ready");
    } catch (V) {
      m(V instanceof Error ? V.message : String(V)), p("failed");
    }
  }, [e, i, s, c, l]);
  ee(() => {
    W();
  }, [W]), ee(() => {
    A.current && (A.current.indeterminate = T > 0 && !F);
  }, [F, T]);
  const H = se(async () => {
    if (!(z === "loading" || z === "ready")) {
      C("loading");
      try {
        const V = await dr(e);
        $(V.activities ?? []), C("ready");
      } catch (V) {
        C("failed"), m(V instanceof Error ? V.message : String(V));
      }
    }
  }, [z, e]), U = () => {
    m(""), x(""), I({ name: "", description: "", rootKind: "flowchart" }), H();
  }, O = async () => {
    if (S?.name.trim()) {
      M(!0), m(""), x("");
      try {
        const V = await xh(e, {
          name: S.name.trim(),
          description: S.description.trim() || null,
          rootKind: S.rootKind,
          rootActivityVersionId: mj(S, k)
        });
        I(null), n(V.definition.id);
      } catch (V) {
        m(V instanceof Error ? V.message : String(V));
      } finally {
        M(!1);
      }
    }
  }, q = (V) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(V)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, J = async () => {
    if (w.length === 1 && c > 1) {
      u(c - 1);
      return;
    }
    await W();
  }, G = () => j(/* @__PURE__ */ new Set()), R = (V, ne) => {
    j((ge) => {
      const ye = new Set(ge);
      return ne ? ye.add(V) : ye.delete(V), ye;
    });
  }, X = (V) => {
    j((ne) => {
      const ge = new Set(ne);
      for (const ye of E)
        V ? ge.add(ye) : ge.delete(ye);
      return ge;
    });
  }, de = (V) => {
    a(V), u(1), G();
  }, le = (V) => {
    r(V), u(1), G();
  }, te = async (V) => {
    if (await ea().confirm({ message: `Delete workflow definition "${V.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" })) {
      x(""), m("");
      try {
        await wh(e, V.id), R(V.id, !1), x(`Deleted ${V.name}`), await J();
      } catch (ne) {
        m(ne instanceof Error ? ne.message : String(ne));
      }
    }
  }, ie = async (V) => {
    x(""), m("");
    try {
      await vh(e, V.id), R(V.id, !1), x(`Restored ${V.name}`), await J();
    } catch (ne) {
      m(ne instanceof Error ? ne.message : String(ne));
    }
  }, fe = async (V) => {
    if (await ea().confirm({ message: `Permanently delete workflow definition "${V.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) {
      x(""), m("");
      try {
        await bh(e, V.id), R(V.id, !1), x(`Permanently deleted ${V.name}`), await J();
      } catch (ne) {
        m(ne instanceof Error ? ne.message : String(ne));
      }
    }
  };
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ o.jsx("button", { type: "button", className: s === "active" ? "active" : "", "aria-selected": s === "active", onClick: () => de("active"), children: "Active" }),
        /* @__PURE__ */ o.jsx("button", { type: "button", className: s === "deleted" ? "active" : "", "aria-selected": s === "deleted", onClick: () => de("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ o.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ o.jsx(ar, { size: 15 }),
        /* @__PURE__ */ o.jsx("input", { value: i, onChange: (V) => le(V.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => {
        W();
      }, children: "Refresh" }),
      /* @__PURE__ */ o.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ o.jsxs("button", { type: "button", title: "Create workflow", onClick: U, children: [
        /* @__PURE__ */ o.jsx(Gt, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ o.jsx(oi, { message: h, title: "Couldn't load workflow definitions" }) : null,
    f !== "failed" && h ? /* @__PURE__ */ o.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ o.jsx(jt, { size: 16 }),
      " ",
      h
    ] }) : null,
    v ? /* @__PURE__ */ o.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ o.jsx(an, { size: 14 }),
      " ",
      v
    ] }) : null,
    N.size > 0 ? /* @__PURE__ */ o.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ o.jsxs("span", { children: [
        N.size,
        " selected"
      ] }),
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: G, children: "Clear selection" })
    ] }) : null,
    f === "loading" ? /* @__PURE__ */ o.jsx(zs, {}) : null,
    f === "ready" && w.length === 0 ? /* @__PURE__ */ o.jsx(
      Vs,
      {
        icon: /* @__PURE__ */ o.jsx(fl, { size: 22 }),
        title: `No ${s} workflow definitions`,
        description: "Create a workflow to start designing automation, or adjust your filters to see more.",
        action: /* @__PURE__ */ o.jsxs("button", { type: "button", className: "wf-link-button", onClick: U, children: [
          /* @__PURE__ */ o.jsx(Gt, { size: 15 }),
          " Create workflow"
        ] })
      }
    ) : null,
    f === "ready" && w.length > 0 ? /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ o.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ o.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ o.jsx(
            "input",
            {
              ref: A,
              type: "checkbox",
              checked: F,
              onChange: (V) => X(V.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ o.jsx("span", { children: "Name" }),
          /* @__PURE__ */ o.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ o.jsx("span", { children: s === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ o.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ o.jsx("span", { children: "Actions" })
        ] }),
        w.map((V) => /* @__PURE__ */ o.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${V.name}`,
            "aria-selected": N.has(V.id),
            tabIndex: 0,
            onClick: () => n(V.id),
            onKeyDown: (ne) => {
              ne.currentTarget === ne.target && (ne.key !== "Enter" && ne.key !== " " || (ne.preventDefault(), n(V.id)));
            },
            children: [
              /* @__PURE__ */ o.jsx("label", { className: "wf-row-select", onClick: (ne) => ne.stopPropagation(), children: /* @__PURE__ */ o.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: N.has(V.id),
                  onChange: (ne) => R(V.id, ne.target.checked),
                  "aria-label": `Select workflow definition ${V.name}`
                }
              ) }),
              /* @__PURE__ */ o.jsxs("span", { children: [
                /* @__PURE__ */ o.jsx("strong", { children: V.name }),
                /* @__PURE__ */ o.jsx("small", { children: V.description || V.id })
              ] }),
              /* @__PURE__ */ o.jsx("span", { children: V.latestVersion ?? "No version" }),
              /* @__PURE__ */ o.jsx("span", { children: s === "deleted" ? Re(V.deletedAt) : V.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ o.jsx("span", { children: Re(V.lastModifiedAt) }),
              /* @__PURE__ */ o.jsx("span", { className: "wf-row-actions", onClick: (ne) => ne.stopPropagation(), children: s === "active" ? /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
                /* @__PURE__ */ o.jsx("button", { type: "button", onClick: (ne) => {
                  ne.stopPropagation(), n(V.id);
                }, children: "Open" }),
                /* @__PURE__ */ o.jsx("button", { type: "button", onClick: (ne) => {
                  ne.stopPropagation(), q(V.id);
                }, children: "Artifacts" }),
                P ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Lt(t, P, V), children: [
                  /* @__PURE__ */ o.jsx(ft, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ o.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  te(V);
                }, children: [
                  /* @__PURE__ */ o.jsx($n, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
                /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
                  ie(V);
                }, children: [
                  /* @__PURE__ */ o.jsx(as, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ o.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  fe(V);
                }, children: [
                  /* @__PURE__ */ o.jsx($n, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          V.id
        ))
      ] }),
      /* @__PURE__ */ o.jsx(
        eS,
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
    S ? /* @__PURE__ */ o.jsx(
      tS,
      {
        draft: S,
        creating: _,
        ai: t,
        suggestMetadataAction: D,
        onChange: (V) => I(V),
        onClose: () => I(null),
        onSubmit: O
      }
    ) : null
  ] });
}
function iS({ activities: e, activityCatalog: t, selectedEvidenceId: n = null, onSelectEvidence: i }) {
  const r = ue(
    () => new Map(t.map((a) => [a.activityTypeKey, a])),
    [t]
  ), s = ue(() => oS(e), [e]);
  return s.length === 0 ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty wf-timeline-empty", children: "No activity executions recorded yet." }) : /* @__PURE__ */ o.jsx("ol", { className: "wf-timeline", "aria-label": "Execution timeline", children: s.map((a) => {
    const c = r.get(a.activityType), u = un(c), l = c ? ke(c) : _t(a.activityType) ?? a.activityType, d = _t(a.activityType) ?? a.activityType, f = sS(a.startedAt ?? a.scheduledAt), p = ms(a.startedAt, a.completedAt);
    return /* @__PURE__ */ o.jsx("li", { children: /* @__PURE__ */ o.jsxs(
      "button",
      {
        type: "button",
        className: "wf-timeline-entry",
        "data-selected": a.activityExecutionId === n,
        onClick: () => i?.(a.activityExecutionId),
        children: [
          /* @__PURE__ */ o.jsx("span", { className: "wf-timeline-icon wf-activity-icon", "data-icon": u, "aria-hidden": "true", children: pr(u) }),
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
          /* @__PURE__ */ o.jsx(rS, { status: a.status })
        ]
      }
    ) }, a.activityExecutionId);
  }) });
}
function rS({ status: e }) {
  const t = e.toLowerCase(), n = t === "completed" ? "OK" : e;
  return /* @__PURE__ */ o.jsx("span", { className: "wf-status-badge wf-timeline-pill", "data-status": t, children: n });
}
function oS(e) {
  return e.map((t, n) => ({ activity: t, index: n })).sort((t, n) => tl(t.activity) - tl(n.activity) || t.index - n.index).map((t) => t.activity);
}
function tl(e) {
  const t = e.startedAt ?? e.scheduledAt, n = t ? Date.parse(t) : Number.NaN;
  return Number.isNaN(n) ? Number.MAX_SAFE_INTEGER : n;
}
function sS(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? "" : t.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: !1 });
}
function aS({ context: e }) {
  const [t, n] = K("loading"), [i, r] = K(""), [s, a] = K(""), [c, u] = K(""), [l, d] = K([]), f = se(async () => {
    n("loading"), r("");
    try {
      const h = await Ih(e, {
        status: s || void 0,
        runKind: c || void 0,
        take: 100
      });
      d(h), n("ready");
    } catch (h) {
      r(h instanceof Error ? h.message : String(h)), d([]), n("failed");
    }
  }, [e, c, s]);
  ee(() => {
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
    t === "failed" ? /* @__PURE__ */ o.jsx(oi, { message: i }) : null,
    t === "loading" ? /* @__PURE__ */ o.jsx(zs, {}) : null,
    t === "ready" && l.length === 0 ? /* @__PURE__ */ o.jsx(
      Vs,
      {
        icon: /* @__PURE__ */ o.jsx(Zn, { size: 22 }),
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
            /* @__PURE__ */ o.jsx("span", { children: ef(h.runKind) }),
            /* @__PURE__ */ o.jsx("span", { children: /* @__PURE__ */ o.jsx(fn, { status: h.status, subStatus: h.subStatus }) }),
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
            /* @__PURE__ */ o.jsx("span", { children: Re(h.startedAt ?? h.createdAt) }),
            /* @__PURE__ */ o.jsx("span", { children: ms(h.startedAt ?? h.createdAt, h.completedAt ?? h.updatedAt) })
          ]
        },
        h.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function cS({ context: e, ai: t, workflowExecutionId: n }) {
  const [i, r] = K("loading"), [s, a] = K(""), [c, u] = K(null), [l, d] = K(null), [f, p] = K([]), {
    inspectorWidth: h,
    inspectorCollapsed: m,
    maximizedSidePanel: v,
    inspectorExpanded: x,
    editorBodyStyle: w,
    toggleSidePanelCollapsed: b,
    toggleSidePanelMaximized: g,
    startSidePanelResize: y,
    handleSidePanelResizeKeyDown: N
  } = uf(), j = Rt(t, "weaver.workflows.explain-instance"), S = se(async () => {
    if (!n) {
      a("No workflow execution id was provided."), r("failed");
      return;
    }
    r("loading"), a("");
    try {
      const k = await Ah(e, n), [$, z] = await Promise.all([
        yh(e, k.instance.definitionVersionId).then(
          (C) => ({ definitionVersion: C, error: "" }),
          (C) => ({ definitionVersion: null, error: C instanceof Error ? C.message : String(C) })
        ),
        dr(e)
      ]);
      u({
        details: k,
        definitionVersion: $.definitionVersion,
        definitionVersionError: $.error,
        activityCatalog: z.activities
      }), d(null), p([]), r("ready");
    } catch (k) {
      u(null), a(zj(k, n)), r("failed");
    }
  }, [e, n]);
  ee(() => {
    S();
  }, [S]);
  const I = () => {
    window.history.pushState({}, "", "/workflows/instances"), window.dispatchEvent(new PopStateEvent("popstate"));
  }, _ = () => {
    const k = c?.details.instance.definitionId;
    k && (window.history.pushState({}, "", `/workflows/definitions?definition=${encodeURIComponent(k)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  }, M = [
    "wf-instance-detail-workbench",
    m ? "inspector-collapsed" : "",
    v === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: I, children: [
        /* @__PURE__ */ o.jsx(Mn, { size: 14 }),
        " Runs"
      ] }),
      c?.details.instance.definitionId ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: _, children: [
        /* @__PURE__ */ o.jsx(pl, { size: 14 }),
        " Designer"
      ] }) : null,
      /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
        S();
      }, children: [
        /* @__PURE__ */ o.jsx(as, { size: 14 }),
        " Refresh"
      ] }),
      c && j ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Lt(t, j, c.details), children: [
        /* @__PURE__ */ o.jsx(ft, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    i === "loading" ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Loading workflow run..." }) : null,
    i === "failed" ? /* @__PURE__ */ o.jsx(oi, { message: s }) : null,
    i === "ready" && c ? /* @__PURE__ */ o.jsxs("div", { className: M, style: w, children: [
      /* @__PURE__ */ o.jsx(
        lS,
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
      x && !v ? /* @__PURE__ */ o.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize run details panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Xt,
          "aria-valuemax": qt,
          "aria-valuenow": h,
          tabIndex: 0,
          onPointerDown: (k) => y("inspector", k),
          onKeyDown: (k) => N("inspector", k)
        }
      ) : /* @__PURE__ */ o.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ o.jsx(
        pS,
        {
          context: e,
          ai: t,
          action: j ?? void 0,
          summary: c.details.instance,
          details: c.details,
          state: "ready",
          error: "",
          selectedEvidenceId: l,
          onSelectEvidence: d,
          activityCatalog: c.activityCatalog,
          graphNodeIds: c.definitionVersion ? fS(c.definitionVersion, c.activityCatalog) : void 0,
          rootNodeId: c.definitionVersion?.state.rootActivity?.nodeId,
          collapsed: m,
          expanded: x,
          maximized: v === "inspector",
          onToggleCollapsed: () => b("inspector"),
          onToggleMaximized: () => g("inspector")
        }
      )
    ] }) : null
  ] });
}
function lS({
  definitionVersion: e,
  definitionVersionError: t,
  activityCatalog: n,
  details: i,
  selectedEvidenceId: r,
  onSelectEvidence: s,
  frames: a,
  onNavigateToScope: c
}) {
  const u = dS(a), l = ue(
    () => uS(e, n, i, r, a, c),
    [n, e, i, a, c, r]
  );
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
      /* @__PURE__ */ o.jsx(fn, { status: i.instance.status, subStatus: i.instance.subStatus })
    ] }),
    e ? /* @__PURE__ */ o.jsxs("div", { className: "wf-breadcrumb wf-instance-breadcrumb", children: [
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => c([]), children: "Root" }),
      a.map((d, f) => d.label ? (
        // Unlabelled frames are descent hops planSlotNavigation tucks under the next crumb
        // (entering a slot through its single container child); the visible crumb navigates to
        // the full hop chain, so hiding them keeps the trail one-entry-per-slot.
        /* @__PURE__ */ o.jsxs("span", { className: "wf-breadcrumb-segment", children: [
          /* @__PURE__ */ o.jsx(xt, { size: 13 }),
          /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => c(a.slice(0, f + 1)), children: d.label })
        ] }, `${d.ownerNodeId}-${d.slotId}-${f}`)
      ) : null)
    ] }) : null,
    /* @__PURE__ */ o.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ o.jsxs("div", { className: "wf-empty", children: [
        "The workflow run loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ o.jsx("small", { children: Lj(t) }) : null
      ] }),
      e && l.nodes.length === 0 ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      l.nodes.length > 0 ? /* @__PURE__ */ o.jsxs(
        Rd,
        {
          nodes: l.nodes,
          edges: l.edges,
          nodeTypes: sf,
          edgeTypes: af,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (d, f) => s(f.id),
          onPaneClick: () => s(null),
          children: [
            /* @__PURE__ */ o.jsx(zd, {}),
            /* @__PURE__ */ o.jsx(Wd, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ o.jsx(Od, {})
          ]
        },
        u
      ) : null
    ] })
  ] });
}
function uS(e, t, n, i, r, s) {
  const a = e?.state.rootActivity;
  if (!e || !a) return { nodes: [], edges: [] };
  const c = Ln(a, r, t), u = c?.owner ?? a, l = t.find((h) => h.activityVersionId === u.activityVersionId), f = El(u, l) === "unsupported" || !c ? kl(u, t, e.layout) : Cl(c, t, e.layout), p = f.nodes.map((h) => ({
    ...h,
    draggable: !1,
    connectable: !1,
    deletable: !1,
    data: {
      ...h.data,
      onEnterSlot: (m) => {
        const v = Mi(r, u, h.id, m, `${h.data.label} / ${m.label}`, t);
        v && s(v.frames);
      }
    }
  }));
  return {
    nodes: $p(p, n.activities, n.incidents, i),
    edges: f.edges.map((h) => ({ ...h, deletable: !1 }))
  };
}
function dS(e) {
  return e.length === 0 ? "root" : e.map((t) => `${t.ownerNodeId}:${t.slotId}`).join("/");
}
function fS(e, t) {
  const n = /* @__PURE__ */ new Set(), i = (r) => {
    if (r) {
      n.add(r.nodeId);
      for (const s of $e(r, t))
        s.activities.forEach(i);
    }
  };
  return i(e.state.rootActivity), n;
}
function pS({
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
  maximized: m = !1,
  onToggleCollapsed: v,
  onToggleMaximized: x
}) {
  const [w, b] = K("timeline");
  if (!i)
    return /* @__PURE__ */ o.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Select a workflow run to inspect its timeline." }) });
  const g = r?.incidents.length ?? 0, y = hS(r?.activities ?? [], c), N = (S) => {
    u?.(S), b("activity");
  }, j = [
    { id: "timeline", title: "Timeline", order: 0, icon: /* @__PURE__ */ o.jsx(is, { size: 14 }), render: () => null },
    { id: "activity", title: "Activity", order: 1, icon: /* @__PURE__ */ o.jsx(ll, { size: 14 }), render: () => null },
    { id: "issues", title: g > 0 ? `Issues (${g})` : "Issues", order: 2, icon: /* @__PURE__ */ o.jsx(jt, { size: 14 }), render: () => null },
    { id: "details", title: "Details", order: 3, icon: /* @__PURE__ */ o.jsx(os, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ o.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Run details panel", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-panel-title wf-instance-panel-title", children: [
      /* @__PURE__ */ o.jsx(Ki, { label: "Run details tabs", tabs: j, activeTabId: w, onSelect: (S) => b(S) }),
      /* @__PURE__ */ o.jsxs("span", { className: "wf-panel-actions", children: [
        /* @__PURE__ */ o.jsx(
          "button",
          {
            type: "button",
            className: "wf-panel-action-button",
            "aria-label": p ? "Expand run details panel" : "Collapse run details panel",
            title: p ? "Expand" : "Collapse",
            onClick: v,
            children: p ? /* @__PURE__ */ o.jsx(Mn, { size: 14 }) : /* @__PURE__ */ o.jsx(xt, { size: 14 })
          }
        ),
        p ? null : /* @__PURE__ */ o.jsx(
          "button",
          {
            type: "button",
            className: "wf-panel-action-button",
            "aria-label": m ? "Restore run details panel" : "Maximize run details panel",
            title: m ? "Restore" : "Maximize",
            onClick: x,
            children: m ? /* @__PURE__ */ o.jsx(_o, { size: 14 }) : /* @__PURE__ */ o.jsx(Pn, { size: 14 })
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
        n ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Lt(t, n, r ?? i), children: [
          /* @__PURE__ */ o.jsx(ft, { size: 13 }),
          " Explain"
        ] }) : null
      ] }),
      s === "loading" ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Loading run details..." }) : null,
      s === "failed" ? /* @__PURE__ */ o.jsx(oi, { message: a }) : null,
      s === "ready" && r ? /* @__PURE__ */ o.jsx("div", { className: "wf-instance-tab-content", children: w === "timeline" ? /* @__PURE__ */ o.jsx(
        iS,
        {
          activities: r.activities,
          activityCatalog: f,
          selectedEvidenceId: c,
          onSelectEvidence: N
        }
      ) : w === "activity" ? /* @__PURE__ */ o.jsx(gS, { context: e, activity: y, activityCatalog: f }) : w === "issues" ? /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
        /* @__PURE__ */ o.jsx(AS, { incidents: r.incidents, selectedEvidenceId: c, onSelectEvidence: u }),
        /* @__PURE__ */ o.jsx(PS, { details: r, graphNodeIds: l, rootNodeId: d })
      ] }) : /* @__PURE__ */ o.jsxs("dl", { className: "wf-instance-meta", children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ o.jsx("dd", { children: /* @__PURE__ */ o.jsx(fn, { status: i.status, subStatus: i.subStatus }) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Run Kind" }),
        /* @__PURE__ */ o.jsx("dd", { children: ef(i.runKind) }),
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
        /* @__PURE__ */ o.jsx("dd", { children: Re(i.createdAt) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Started" }),
        /* @__PURE__ */ o.jsx("dd", { children: Re(i.startedAt) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Completed" }),
        /* @__PURE__ */ o.jsx("dd", { children: Re(i.completedAt) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Correlation" }),
        /* @__PURE__ */ o.jsx("dd", { children: i.correlationId || "None" })
      ] }) }) : null
    ] }) : null
  ] });
}
function hS(e, t) {
  if (!t) return null;
  const n = e.find((r) => r.activityExecutionId === t);
  if (n) return n;
  const i = e.filter((r) => r.executableNodeId === t || r.authoredActivityId === t);
  return i.length > 0 ? $l(i) : null;
}
function gS({ context: e, activity: t, activityCatalog: n }) {
  const i = t?.activityExecutionId ?? null, r = t?.workflowExecutionId ?? null, [s, a] = K({
    activityExecutionId: null,
    status: "idle",
    inspection: null,
    error: ""
  });
  if (ee(() => {
    if (!i || !r) {
      a({ activityExecutionId: null, status: "idle", inspection: null, error: "" });
      return;
    }
    let f = !1;
    const p = i;
    return a({ activityExecutionId: p, status: "loading", inspection: null, error: "" }), _h(e, r, p).then(
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
  const u = n.find((f) => f.activityTypeKey === t.activityType)?.displayName || _t(t.activityType) || t.activityType, l = t.bookmarkIds?.length ?? 0, d = t.incidentIds?.length ?? 0;
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsxs("section", { className: "wf-instance-section", children: [
      /* @__PURE__ */ o.jsx("h4", { children: "Activity" }),
      /* @__PURE__ */ o.jsxs("dl", { className: "wf-instance-meta", children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Name" }),
        /* @__PURE__ */ o.jsx("dd", { children: u }),
        /* @__PURE__ */ o.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ o.jsx("dd", { children: /* @__PURE__ */ o.jsx(fn, { status: t.status, subStatus: t.subStatus }) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Activity Execution ID" }),
        /* @__PURE__ */ o.jsx("dd", { children: t.activityExecutionId }),
        /* @__PURE__ */ o.jsx("dt", { children: "Authored Activity ID" }),
        /* @__PURE__ */ o.jsx("dd", { children: t.authoredActivityId }),
        /* @__PURE__ */ o.jsx("dt", { children: "Type" }),
        /* @__PURE__ */ o.jsxs("dd", { children: [
          _t(t.activityType) ?? t.activityType,
          " ",
          /* @__PURE__ */ o.jsx("small", { children: t.activityTypeVersion })
        ] }),
        /* @__PURE__ */ o.jsx("dt", { children: "Started" }),
        /* @__PURE__ */ o.jsx("dd", { children: Re(t.startedAt) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Completed" }),
        /* @__PURE__ */ o.jsx("dd", { children: Re(t.completedAt) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Duration" }),
        /* @__PURE__ */ o.jsx("dd", { children: ms(t.startedAt, t.completedAt) || "Unknown" }),
        /* @__PURE__ */ o.jsx("dt", { children: "Bookmarks" }),
        /* @__PURE__ */ o.jsx("dd", { children: l }),
        /* @__PURE__ */ o.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ o.jsx("dd", { children: d })
      ] })
    ] }),
    /* @__PURE__ */ o.jsx(
      nl,
      {
        state: s,
        subject: "ActivityInput",
        title: "Inputs",
        loadingText: "Loading runtime input evidence...",
        failureText: "Runtime input evidence is unavailable.",
        emptyText: "No runtime input snapshots were recorded for this execution."
      }
    ),
    /* @__PURE__ */ o.jsx(
      nl,
      {
        state: s,
        subject: "ActivityOutput",
        title: "Outputs",
        loadingText: "Loading runtime output evidence...",
        failureText: "Runtime output evidence is unavailable.",
        emptyText: "No runtime output snapshots were recorded for this execution."
      }
    )
  ] });
}
function nl({
  state: e,
  subject: t,
  title: n,
  loadingText: i,
  failureText: r,
  emptyText: s
}) {
  if (e.status === "idle") return null;
  if (e.status === "loading")
    return /* @__PURE__ */ o.jsxs("section", { className: "wf-instance-section", children: [
      /* @__PURE__ */ o.jsx("h4", { children: n }),
      /* @__PURE__ */ o.jsx("p", { children: i })
    ] });
  if (e.status === "failed")
    return /* @__PURE__ */ o.jsxs("section", { className: "wf-instance-section", children: [
      /* @__PURE__ */ o.jsx("h4", { children: n }),
      /* @__PURE__ */ o.jsx("p", { children: r }),
      e.error ? /* @__PURE__ */ o.jsx("p", { className: "wf-instance-note", children: e.error }) : null
    ] });
  const a = (e.inspection?.valueSnapshots ?? []).filter((c) => c.subject === t);
  return a.length === 0 ? /* @__PURE__ */ o.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ o.jsx("h4", { children: n }),
    /* @__PURE__ */ o.jsx("p", { children: s })
  ] }) : /* @__PURE__ */ o.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ o.jsx("h4", { children: n }),
    /* @__PURE__ */ o.jsx("div", { className: "wf-runtime-input-list", children: a.map((c) => /* @__PURE__ */ o.jsx(mS, { snapshot: c }, `${c.name}:${c.capturedAt}:${c.captureMode}`)) })
  ] });
}
function mS({ snapshot: e }) {
  const t = e.type?.displayName || e.type?.typeName || e.type?.alias || "Unknown", n = e.snapshot ?? (e.captureMode === "DiagnosticSnapshot" ? e.payload : null), i = e.captureMode === "Payload" && e.payload !== void 0;
  return /* @__PURE__ */ o.jsxs("article", { className: "wf-runtime-input", children: [
    /* @__PURE__ */ o.jsxs("header", { children: [
      /* @__PURE__ */ o.jsxs("span", { children: [
        /* @__PURE__ */ o.jsx("strong", { children: e.name }),
        /* @__PURE__ */ o.jsx("small", { children: t })
      ] }),
      /* @__PURE__ */ o.jsx("span", { className: "wf-runtime-capture-mode", children: NS(e.captureMode) })
    ] }),
    kS(n) ? /* @__PURE__ */ o.jsx(Ws, { node: n }) : i ? /* @__PURE__ */ o.jsx(jS, { payload: e.payload }) : /* @__PURE__ */ o.jsx("p", { children: CS(e) }),
    e.isSensitive ? /* @__PURE__ */ o.jsx("p", { className: "wf-instance-note", children: "Marked sensitive by runtime evidence." }) : null
  ] });
}
const yS = {
  null: !0,
  scalar: !0,
  number: !0,
  string: !0,
  object: !0,
  array: !0,
  redacted: !0,
  truncated: !0,
  unsupported: !0,
  error: !0,
  permissionHidden: !0,
  payloadReference: !0
};
function xS(e) {
  return Object.hasOwn(yS, e.kind);
}
function Ws({ node: e, depth: t = 0 }) {
  if (!xS(e))
    return /* @__PURE__ */ o.jsx(il, { node: { kind: "unsupported", reason: `Unknown snapshot node: ${e.kind}` } });
  switch (e.kind) {
    case "null":
      return /* @__PURE__ */ o.jsx("code", { className: "wf-runtime-input-value", children: "null" });
    case "scalar":
    case "number":
      return /* @__PURE__ */ o.jsx("code", { className: "wf-runtime-input-value", children: vf(e.value) });
    case "string":
      return /* @__PURE__ */ o.jsxs("code", { className: "wf-runtime-input-value", children: [
        e.preview ?? "",
        e.truncated ? ` (${e.length ?? "unknown"} chars, truncated)` : ""
      ] });
    case "object":
      return /* @__PURE__ */ o.jsx(wS, { node: e, depth: t });
    case "array":
      return /* @__PURE__ */ o.jsx(vS, { node: e, depth: t });
    case "redacted":
    case "truncated":
    case "unsupported":
    case "error":
    case "permissionHidden":
      return /* @__PURE__ */ o.jsx(il, { node: e });
    case "payloadReference":
      return /* @__PURE__ */ o.jsx(bS, { node: e });
  }
}
function wS({ node: e, depth: t }) {
  const n = e.properties ?? [];
  return n.length === 0 ? /* @__PURE__ */ o.jsx("code", { className: "wf-runtime-input-value", children: "{}" }) : /* @__PURE__ */ o.jsxs("details", { className: "wf-runtime-snapshot-node", open: t === 0, children: [
    /* @__PURE__ */ o.jsxs("summary", { children: [
      e.typeName || "Object",
      e.truncated ? " (truncated)" : ""
    ] }),
    /* @__PURE__ */ o.jsx("div", { className: "wf-runtime-snapshot-children", children: n.map((i) => /* @__PURE__ */ o.jsxs("div", { className: "wf-runtime-snapshot-property", children: [
      /* @__PURE__ */ o.jsx("span", { children: i.name }),
      /* @__PURE__ */ o.jsx(Ws, { node: i.value, depth: t + 1 })
    ] }, i.name)) })
  ] });
}
function vS({ node: e, depth: t }) {
  const n = e.items ?? [];
  return n.length === 0 ? /* @__PURE__ */ o.jsx("code", { className: "wf-runtime-input-value", children: "[]" }) : /* @__PURE__ */ o.jsxs("details", { className: "wf-runtime-snapshot-node", open: t === 0, children: [
    /* @__PURE__ */ o.jsxs("summary", { children: [
      "Array (",
      e.itemCount ?? n.length,
      ")",
      e.truncated ? " (truncated)" : ""
    ] }),
    /* @__PURE__ */ o.jsx("div", { className: "wf-runtime-snapshot-children", children: n.map((i, r) => /* @__PURE__ */ o.jsxs("div", { className: "wf-runtime-snapshot-property", children: [
      /* @__PURE__ */ o.jsx("span", { children: r }),
      /* @__PURE__ */ o.jsx(Ws, { node: i, depth: t + 1 })
    ] }, r)) })
  ] });
}
function il({ node: e }) {
  const t = e.message || e.reason || e.requiredPermission || e.displayName;
  return /* @__PURE__ */ o.jsxs("span", { className: `wf-runtime-snapshot-marker ${e.kind}`, children: [
    SS(e.kind),
    t ? `: ${t}` : "",
    e.omittedCount ? ` (${e.omittedCount} omitted)` : ""
  ] });
}
function bS({ node: e }) {
  const t = e.displayName || e.referenceKind || "Referenced payload", n = e.resolution?.reason || "Reference resolution is not available.";
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-runtime-snapshot-reference", children: [
    /* @__PURE__ */ o.jsx("strong", { children: t }),
    e.contentType ? /* @__PURE__ */ o.jsx("small", { children: e.contentType }) : null,
    typeof e.size == "number" ? /* @__PURE__ */ o.jsxs("small", { children: [
      e.size,
      " bytes"
    ] }) : null,
    /* @__PURE__ */ o.jsx("span", { children: n })
  ] });
}
function jS({ payload: e }) {
  const t = vf(e);
  return t.length <= 160 && !t.includes(`
`) ? /* @__PURE__ */ o.jsx("code", { className: "wf-runtime-input-value", children: t }) : /* @__PURE__ */ o.jsxs("details", { className: "wf-runtime-input-value-details", children: [
    /* @__PURE__ */ o.jsx("summary", { children: ES(t) }),
    /* @__PURE__ */ o.jsx("pre", { children: t })
  ] });
}
function NS(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2");
}
function SS(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2");
}
function CS(e) {
  return e.state === "permissionHidden" ? "Runtime value evidence is hidden by permissions." : e.state === "metadataOnly" ? e.captureReason || "Runtime value evidence is metadata-only." : e.state === "notCaptured" ? e.captureReason || "Runtime value evidence was not captured." : e.captureReason || "The runtime capture policy did not include this value.";
}
function kS(e) {
  return typeof e == "object" && e !== null && typeof e.kind == "string";
}
function ES(e) {
  const t = e.split(`
`, 1)[0] || e;
  return t.length > 120 ? `${t.slice(0, 117)}...` : t;
}
function vf(e) {
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
const IS = [
  "runtime.faultStackTrace",
  "runtime.exceptionStackTrace",
  "runtime.stackTrace",
  "faultStackTrace",
  "exceptionStackTrace",
  "stackTrace"
];
function AS({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
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
            It,
            {
              value: MS(s),
              ariaLabel: `Copy incident ${s.failureType}`,
              copiedLabel: "incident",
              onCopied: (a) => r(`Copied ${a}`),
              onCopyFailed: (a) => r(`Could not copy ${a}.`)
            }
          ),
          /* @__PURE__ */ o.jsx(_S, { incident: s })
        ]
      },
      s.incidentId
    )),
    i ? /* @__PURE__ */ o.jsx("p", { className: "wf-copy-status", role: "status", children: i }) : null
  ] });
}
function _S({ incident: e }) {
  const t = DS(e);
  return t ? /* @__PURE__ */ o.jsxs("details", { className: "wf-incident-stacktrace", children: [
    /* @__PURE__ */ o.jsx("summary", { children: $S(t) }),
    /* @__PURE__ */ o.jsx("pre", { children: t })
  ] }) : null;
}
function DS(e) {
  const t = TS(e.stackTrace, e.exceptionStackTrace);
  if (t) return t;
  for (const n of IS) {
    const i = e.metadata?.[n];
    if (i && i.trim()) return i;
  }
  return null;
}
function TS(...e) {
  return e.find((t) => t?.trim()) ?? null;
}
function $S(e) {
  const n = (e.split(`
`).find((i) => i.trim()) ?? e).trim();
  return n.length > 120 ? `${n.slice(0, 117)}...` : n;
}
function PS({ details: e, graphNodeIds: t, rootNodeId: n }) {
  if (!t) return null;
  const i = e.activities.filter((r) => {
    const s = Aj(r);
    return s && s !== n && !t.has(s);
  });
  return i.length === 0 ? null : /* @__PURE__ */ o.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ o.jsx("h4", { children: "Executions outside canvas" }),
    /* @__PURE__ */ o.jsx("div", { className: "wf-instance-unmatched-list", children: i.map((r) => /* @__PURE__ */ o.jsxs("div", { className: "wf-instance-unmatched", children: [
      /* @__PURE__ */ o.jsx("strong", { children: _t(r.activityType) ?? r.activityType }),
      /* @__PURE__ */ o.jsx("small", { children: r.activityExecutionId })
    ] }, `activity-${r.activityExecutionId}`)) })
  ] });
}
function MS(e) {
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
function RS({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: i,
  workflowDesignerPanels: r,
  autosaveEnabledByDefault: s
}) {
  const [a, c] = K(rl);
  ee(() => {
    const l = () => c(rl());
    return window.addEventListener("popstate", l), () => window.removeEventListener("popstate", l);
  }, []);
  const u = (l) => {
    const d = l ? `/workflows/definitions?definition=${encodeURIComponent(l)}` : "/workflows/definitions";
    window.history.pushState({}, "", d), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return a ? /* @__PURE__ */ o.jsx(QN, { context: e, definitionId: a, ai: t, propertyEditors: n, expressionEditors: i, workflowDesignerPanels: r, autosaveEnabledByDefault: s, onBack: () => u(null) }) : /* @__PURE__ */ o.jsx(kr, { title: "Definitions", children: /* @__PURE__ */ o.jsx(nS, { context: e, ai: t, onOpen: u }) });
}
function LS({ context: e, ai: t }) {
  const [n, i] = K(ol);
  ee(() => {
    const s = () => i(ol());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const r = se((s) => {
    const a = s?.trim() ?? "", c = new URL(window.location.href);
    a ? c.searchParams.set("definition", a) : c.searchParams.delete("definition"), i(a || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ o.jsx(kr, { title: "Executables", children: /* @__PURE__ */ o.jsx(Gj, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: r }) });
}
function zS({ context: e }) {
  return /* @__PURE__ */ o.jsx(kr, { title: "Runs", children: /* @__PURE__ */ o.jsx(aS, { context: e }) });
}
function VS({ context: e, ai: t }) {
  const n = OS();
  return /* @__PURE__ */ o.jsx(kr, { title: "Run", children: /* @__PURE__ */ o.jsx(cS, { context: e, ai: t, workflowExecutionId: n }) });
}
function kr({ title: e, children: t }) {
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ o.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ o.jsxs("div", { children: [
      /* @__PURE__ */ o.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ o.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function rl() {
  return new URLSearchParams(window.location.search).get("definition");
}
function ol() {
  return new URLSearchParams(window.location.search).get("definition");
}
function OS() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function KS(e) {
  Jf(e.dialogs), e.featureAreas.add({
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
        { title: "Runtime Diagnostics", path: "/workflows/runtime-diagnostics", iconColor: "#0ea5e9" },
        { title: "Activity Availability", path: "/workflows/activity-availability", iconColor: "#0ea5e9" }
      ]
    },
    routes: [
      {
        id: "workflows-definitions",
        path: "/workflows/definitions",
        label: "Workflow definitions",
        component: () => /* @__PURE__ */ o.jsx(RS, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list(), autosaveEnabledByDefault: e.runtime.workflows?.autosaveEnabledByDefault ?? !0 })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ o.jsx(LS, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => /* @__PURE__ */ o.jsx(zS, { context: e.backend })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => /* @__PURE__ */ o.jsx(VS, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => /* @__PURE__ */ o.jsx(Ng, { context: e.backend })
      },
      {
        id: "workflows-runtime-diagnostics",
        path: "/workflows/runtime-diagnostics",
        label: "Runtime diagnostics",
        component: () => /* @__PURE__ */ o.jsx(kg, { context: e.backend })
      }
    ]
  });
}
export {
  Dj as isConnectEndOverExistingWorkflowNode,
  KS as register,
  Tj as resolveConnectEndSource
};
