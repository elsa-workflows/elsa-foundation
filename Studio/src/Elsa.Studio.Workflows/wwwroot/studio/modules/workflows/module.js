import Fe, { useMemo as ue, useState as K, useEffect as ee, memo as Ee, forwardRef as al, useRef as oe, useCallback as se, useContext as Un, createContext as ns, useLayoutEffect as $f, lazy as Tf, Suspense as Pf, useReducer as Mf, useId as cl } from "react";
import { Boxes as Zn, Zap as Rf, Play as Zt, Terminal as Lf, ListTree as is, GitBranch as ll, ListChecks as zf, Save as rs, EyeOff as co, Shield as Zs, AlertTriangle as $n, SlidersHorizontal as os, Activity as ul, Search as sr, X as ss, DatabaseZap as Gs, ShieldCheck as Vf, Check as an, Plus as Gt, Trash2 as Tn, AlertCircle as jt, Wrench as Of, Copy as Hf, Sparkles as ft, RotateCcw as as, ChevronDown as dl, ChevronRight as xt, GripVertical as fl, Maximize2 as Pn, ChevronUp as Wf, Repeat2 as Ff, Package as pl, Undo2 as Bf, Redo2 as Kf, Network as Xf, Download as qf, ChevronLeft as Mn, Minimize2 as _o, Workflow as hl, Code2 as Yf } from "lucide-react";
import { useQuery as ar, useQueryClient as gl, useMutation as ml } from "@tanstack/react-query";
import { useTablistKeyboard as Uf } from "@elsa-workflows/studio-ui";
function Zf(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var lo = { exports: {} }, yn = {};
var Js;
function Gf() {
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
function Jf() {
  return Qs || (Qs = 1, lo.exports = Gf()), lo.exports;
}
var o = Jf();
let yl;
function Qf(e) {
  yl = e;
}
function ea() {
  return yl;
}
const ep = "String", tp = "singleline";
function np(e) {
  return e === "Single" || e === "Array" || e === "List" || e === "HashSet";
}
function cs(e, t = "Single") {
  return { alias: (e ?? "").trim() || ep, collectionKind: t };
}
function ls(e) {
  const t = e.type ?? e.Type;
  if (Rn(t))
    return { alias: typeof t.alias == "string" ? t.alias : typeof t.typeName == "string" ? t.typeName : "", collectionKind: np(t.collectionKind) ? t.collectionKind : "Single" };
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
function cr() {
  return typeof crypto < "u" && typeof crypto.randomUUID == "function" ? crypto.randomUUID() : `id-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
}
function ip(e, t) {
  const n = new Set(t);
  let i = 1, r = `${e}${i}`;
  for (; n.has(r); )
    i += 1, r = `${e}${i}`;
  return r;
}
function rp(e) {
  return {
    referenceKey: cr(),
    name: e.name,
    type: cs(e.alias),
    storageDriverType: e.storageDriver?.trim() || null,
    default: null
  };
}
function op(e, t) {
  return { ...e, ...t };
}
function sp(e) {
  return e === "" ? null : { value: e, expressionType: "Literal" };
}
function ap(e) {
  return e == null || e.value == null ? "" : typeof e.value == "object" ? JSON.stringify(e.value) : String(e.value);
}
function cp(e) {
  return {
    referenceKey: cr(),
    name: e.name,
    type: cs(e.alias),
    displayName: e.name,
    description: "",
    category: "",
    uiHint: tp,
    storageDriverType: e.storageDriver ?? null,
    isRequired: !1
  };
}
function lp(e, t) {
  return { ...e, ...t };
}
function up(e) {
  return {
    referenceKey: cr(),
    name: e.name,
    type: cs(e.alias),
    displayName: e.name,
    description: "",
    category: ""
  };
}
function dp(e, t) {
  return { ...e, ...t };
}
function fp(e) {
  const t = e.split(",")[0].trim();
  return (t.split(".").pop() ?? t).split("`")[0];
}
function xl(e, t) {
  return e && !e.includes(",") && !e.includes(".") ? e : fp(e || t);
}
function pp(e, t) {
  return xl(e, t).replace(/StorageDriver$/, "");
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
const hp = ["name", "Name"], us = ["name", "Name"], gp = ["storageDriverType", "StorageDriverType"], wl = ["referenceKey", "ReferenceKey"], mp = /* @__PURE__ */ new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);
function Gn(e) {
  return vl(e, vp);
}
function lr(e) {
  return vl(e, bp);
}
function vl(e, t) {
  if (!e) return e;
  const n = { ...e };
  return e.rootActivity && (n.rootActivity = Do(e.rootActivity, t)), Array.isArray(e.variables) && (n.variables = Pi(e.variables, $o)), Array.isArray(e.inputs) && (n.inputs = Pi(e.inputs, $o)), Array.isArray(e.outputs) && (n.outputs = Pi(e.outputs, (i) => bl(i, !1))), n;
}
function Do(e, t) {
  const n = t(e), i = n.structure;
  if (!i || !rt(i.payload)) return n;
  let r = !1;
  const s = { ...i.payload };
  for (const [a, c] of Object.entries(i.payload))
    ra(c) ? (s[a] = Do(c, t), r = !0) : Array.isArray(c) && c.length > 0 && c.every(ra) && (s[a] = c.map((u) => Do(u, t)), r = !0);
  return Array.isArray(i.payload.variables) && i.payload.variables.length > 0 && (s.variables = Pi(i.payload.variables, $o), r = !0), r ? { ...n, structure: { ...i, payload: s } } : n;
}
function Pi(e, t) {
  return e.map((n) => rt(n) && !Array.isArray(n) ? t(n) : n);
}
function $o(e) {
  return bl(e, !0);
}
const yp = [
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
function bl(e, t) {
  const n = xp(e, yp);
  return ut(e, wl).trim() || (n.referenceKey = cr()), n.type = ls(e), t && (n.storageDriverType = wp(e.storageDriverType ?? e.StorageDriverType)), n;
}
function xp(e, t) {
  const n = new Set(t), i = {};
  for (const [r, s] of Object.entries(e))
    n.has(r) || (i[r] = s);
  return i;
}
function wp(e) {
  if (typeof e == "string") return e.trim() ? e : null;
  if (rt(e)) {
    const t = typeof e.typeName == "string" ? e.typeName : "";
    if (!t) return null;
    const n = typeof e.namespace == "string" ? e.namespace : "";
    return n ? `${n}.${t}` : t;
  }
  return null;
}
function vp(e) {
  const t = [], n = {};
  for (const [r, s] of Object.entries(e))
    mp.has(r) || (Cp(s) ? t.push({
      referenceKey: jp(r),
      value: Sp(s.expression)
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
function bp(e) {
  const t = Array.isArray(e.inputs) ? e.inputs : [], n = {};
  for (const i of t) {
    if (!rt(i) || typeof i.referenceKey != "string") continue;
    const r = rt(i.value) ? i.value : {};
    n[Np(i.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof r.expressionType == "string" ? r.expressionType : "Literal",
        value: r.value ?? ""
      }
    };
  }
  return { ...e, ...n, inputs: [] };
}
function jp(e) {
  return e && e.charAt(0).toUpperCase() + e.slice(1);
}
function Np(e) {
  return e && e.charAt(0).toLowerCase() + e.slice(1);
}
function Sp(e) {
  const t = e.type || "Literal";
  return t === "Variable" && rt(e.value) ? { value: e.value, expressionType: t } : t === "Literal" && rt(e.value) ? { value: ia(e.value), expressionType: "Object" } : { value: ia(e.value), expressionType: t };
}
function ia(e) {
  return e == null ? null : typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function Cp(e) {
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
function jl(e, t, n) {
  if (!e) return null;
  let i = e;
  for (const r of t) {
    const s = Ap(i, r.ownerNodeId, n);
    if (!s) return null;
    i = s;
  }
  return i;
}
function kp(e, t, n) {
  const i = (r, s) => {
    const a = Ae(r, n);
    for (const c of a)
      if (c.activities.some((u) => u.nodeId === t)) return { hops: s, targetSlot: c };
    for (const c of a)
      for (const u of c.activities) {
        const l = i(u, [...s, { parent: r, slot: c, child: u }]);
        if (l) return l;
      }
    return null;
  };
  return i(e, []);
}
function oa(e, t) {
  if (e.slot.cardinality !== "single") return !1;
  const n = Ae(e.child, t)[0];
  return !!n && n.mode !== "generic";
}
function Ep(e, t, n = (r) => r.nodeId, i) {
  if (!e) return null;
  if (e.nodeId === t) return [];
  const r = kp(e, t, i);
  if (!r) return null;
  const { hops: s, targetSlot: a } = r;
  return s.length === 0 ? a.id === Ae(e, i)[0]?.id ? [] : null : s.map((c, u) => {
    const l = s[u + 1], d = l?.slot ?? a;
    return {
      ownerNodeId: c.child.nodeId,
      slotId: d.id,
      label: Ip(c, l, d, n, i)
    };
  });
}
function Ip(e, t, n, i, r) {
  return t && oa(t, r) ? "" : oa(e, r) && n.id === Ae(e.child, r)[0]?.id ? `${i(e.parent)} / ${e.slot.label}` : `${i(e.child)} / ${n.label}`;
}
function uo(e, t, n, i, r, s) {
  const a = i.cardinality === "single" && i.activities.length === 1 ? i.activities[0] : null, c = a ? Ae(a, s)[0] : void 0, u = a && c && c.mode !== "generic" ? a : null, l = t?.nodeId === n;
  if (l && e.length === 0)
    return t && Ae(t, s)[0]?.id !== i.id ? null : u ? { frames: [{ ownerNodeId: u.nodeId, slotId: c.id, label: r }], selectedNodeId: null } : { frames: [], selectedNodeId: a?.nodeId ?? null };
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
function Nl(e, t, n) {
  const i = Ae(e, n), r = t.at(-1);
  return r ? i.find((s) => s.id === r.slotId) ?? null : i[0] ?? null;
}
function Ln(e, t, n) {
  const i = jl(e, t, n);
  if (!i) return null;
  const r = Nl(i, t, n);
  return r ? { owner: i, slot: r } : null;
}
function Sl(e, t, n) {
  for (const i of Ae(e, n)) {
    const r = i.activities.find((s) => s.nodeId === t);
    if (r) return { slot: i, child: r };
  }
  return null;
}
function Ap(e, t, n) {
  return Sl(e, t, n)?.child ?? null;
}
function Ae(e, t) {
  const n = e.structure;
  if (!n || !n.payload || typeof n.payload != "object") return [];
  const i = n.payload, r = ds(Cl(e, t));
  if (r?.kind === n.kind) return $p(n, r);
  const s = Qp(n), a = bn(i.activities);
  return a ? [{
    id: `${n.kind}:activities`,
    label: eh(n),
    property: "activities",
    cardinality: "many",
    mode: s,
    activities: a
  }] : Object.entries(i).filter(([, c]) => bn(c) || qi(c)).map(([c, u]) => ({
    id: `${n.kind}:${c}`,
    label: nh(c),
    property: c,
    cardinality: bn(u) ? "many" : "single",
    mode: "generic",
    activities: bn(u) ?? (qi(u) ? [u] : [])
  }));
}
function ds(e) {
  for (const t of e?.designFacets ?? []) {
    if (!ot(t)) continue;
    const n = typeof t.kind == "string" ? t.kind : "", i = typeof t.schemaVersion == "string" ? t.schemaVersion : "";
    if (!n || !i || !ot(t.payload)) continue;
    const r = _p(t.payload);
    if (r) return { kind: n, schemaVersion: i, payload: r };
  }
  return null;
}
function _p(e) {
  const t = e.mode;
  if (t !== "sequence" && t !== "flowchart" && t !== "generic" || typeof e.supportsScopedVariables != "boolean" || !Array.isArray(e.slots) || !ot(e.initialPayload)) return null;
  const n = e.slots.map(Dp).filter((i) => i !== null);
  return {
    mode: t,
    supportsScopedVariables: e.supportsScopedVariables,
    slots: n,
    initialPayload: e.initialPayload
  };
}
function Dp(e) {
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
function $p(e, t) {
  return t.payload.slots.flatMap((n) => {
    if (n.collectionProperty && n.childProperty) {
      const i = n.childProperty, r = e.payload[n.collectionProperty];
      return Array.isArray(r) ? r.flatMap((s, a) => {
        if (!ot(s)) return [];
        const c = n.labelProperty ? Ft(s[n.labelProperty]) : void 0;
        return [sa(e.kind, n, s[i], t.payload.mode, a, c)];
      }) : [];
    }
    return [sa(e.kind, n, e.payload[n.property], t.payload.mode)];
  });
}
function sa(e, t, n, i, r, s) {
  const a = r === void 0 ? "" : `:${t.collectionProperty}:${t.childProperty}:${r}`;
  return {
    id: `${e}:${t.property}${a}`,
    label: r === void 0 ? t.displayName : Pp(t, r, s),
    property: t.property,
    cardinality: t.cardinality,
    mode: i,
    activities: Tp(n, t.cardinality),
    collectionProperty: t.collectionProperty,
    childProperty: t.childProperty,
    labelProperty: t.labelProperty,
    slotNameTemplate: t.slotNameTemplate,
    collectionIndex: r,
    collectionItemLabel: s
  };
}
function Tp(e, t) {
  return t === "many" ? bn(e) ?? [] : qi(e) ? [e] : [];
}
function Pp(e, t, n) {
  return e.slotNameTemplate ? e.slotNameTemplate.replaceAll("{name}", e.name).replaceAll("{displayName}", e.displayName).replaceAll("{label}", n ?? String(t + 1)).replaceAll("{index}", String(t + 1)) : n ? `${e.displayName}: ${n}` : `${e.displayName} ${t + 1}`;
}
function Cl(e, t) {
  if (t)
    return t instanceof Map ? t.get(e.activityVersionId) : Array.isArray(t) ? t.find((n) => n.activityVersionId === e.activityVersionId) : t.activityVersionId === e.activityVersionId ? t : void 0;
}
function Mp(e, t) {
  if (t.labelProperty && t.collectionItemLabel) {
    const n = e.map((i, r) => ot(i) && Ft(i[t.labelProperty]) === t.collectionItemLabel ? r : -1).filter((i) => i >= 0);
    if (n.length === 1) return n[0];
  }
  return typeof t.collectionIndex == "number" && t.collectionIndex >= 0 && t.collectionIndex < e.length ? t.collectionIndex : -1;
}
function kl(e, t, n) {
  const i = new Map(t.map((a) => [a.activityVersionId, a])), r = new Map(n.map((a) => [a.nodeId, a])), s = e.slot.activities.map((a, c) => {
    const u = i.get(a.activityVersionId), l = r.get(a.nodeId) ?? th(e.slot.mode, c);
    return _l(a, u, { x: l.x, y: l.y });
  });
  return {
    nodes: s,
    edges: e.slot.mode === "flowchart" ? $l(e.owner) : Xp(e.slot, s)
  };
}
function El(e, t, n) {
  const i = t.find((s) => s.activityVersionId === e.activityVersionId), r = n.find((s) => s.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [_l(e, i, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function Rp(e, t, n, i = null) {
  const r = new Map(t.map((c) => [c.activityExecutionId, c])), s = la(t, (c) => c.authoredActivityId || c.executableNodeId), a = la(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? r.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const u = s.get(c.id) ?? [], l = a.get(c.id) ?? [];
    if (u.length === 0 && l.length === 0) return c;
    const d = Pl(u), f = i === c.id || u.some((h) => h.activityExecutionId === i) || l.some((h) => h.incidentId === i), p = {
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
function Il(e, t) {
  if (e?.structure?.kind === cn || Wp(t)) return "flowchart";
  if (e?.structure?.kind === Jn || Fp(t)) return "sequence";
  if (e) {
    const n = Ae(e, t)[0];
    if (n) return n.mode === "flowchart" ? "flowchart" : "sequence";
  }
  return "unsupported";
}
function aa(e, t, n, i) {
  return fs(e, t, i, (r) => {
    const s = Nl(r, t, i);
    return s ? ln(r, s, n) : r;
  });
}
function Lp(e, t, n, i) {
  return t.length === 0 ? n : fs(e, t, i, () => n);
}
function fs(e, t, n, i) {
  if (t.length === 0) return i(e);
  const [r, ...s] = t, a = Sl(e, r.ownerNodeId, n);
  if (!a) return e;
  const c = fs(a.child, s, n, i);
  if (c === a.child) return e;
  const u = a.slot.activities.map((l) => l.nodeId === r.ownerNodeId ? c : l);
  return ln(e, a.slot, u);
}
function To(e, t, n, i) {
  if (e.nodeId === t) return n(e);
  const r = Ae(e, i);
  if (r.length === 0) return e;
  let s = !1, a = e;
  for (const c of r) {
    const u = c.activities.map((l) => {
      const d = To(l, t, n, i);
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
    const s = Mp(r, t);
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
function zp(e, t, n, i = []) {
  const r = new Map(e.slot.activities.map((a) => [a.nodeId, a]));
  for (const a of i)
    r.set(a.nodeId, a);
  const s = t.map((a) => r.get(a.id)).filter((a) => !!a);
  return e.slot.mode === "sequence" && s.sort((a, c) => {
    const u = t.find((d) => d.id === a.nodeId), l = t.find((d) => d.id === c.nodeId);
    return (u?.position.x ?? 0) - (l?.position.x ?? 0);
  }), ln(e.owner, e.slot, s);
}
function Vp(e, t) {
  return {
    ...e,
    structure: Kp(e.structure, t)
  };
}
function Op(e, t) {
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
function Ki(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: Dl(e)
  };
}
function Al(e, t) {
  if (!e) return null;
  const n = Cl(e, t), i = e.structure ?? (n ? Dl(n) : null);
  let r = i === e.structure ? e : { ...e, structure: i };
  const s = Ae(r, t);
  for (const a of s) {
    const c = a.activities.map((u) => Al(u, t) ?? u);
    c.some((u, l) => u !== a.activities[l]) && (r = ln(r, a, c));
  }
  return r;
}
function ke(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? Bp(t) : n;
}
function _l(e, t, n, i = {}) {
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
      childSlots: Ae(e, t),
      acceptsInbound: qp(e, t),
      sourcePorts: i.suppressFlowPorts ? [] : Tl(e, t),
      suppressFlowPorts: i.suppressFlowPorts
    }
  };
}
function un(e) {
  if (!e) return "activity";
  const t = Hp(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), i = ke(e).toLowerCase(), r = e.category?.toLowerCase() ?? "", s = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || i === "flowchart" ? "flowchart" : n.endsWith(".sequence") || i === "sequence" ? "sequence" : n.includes("writeline") || i.includes("write line") ? "terminal" : r.includes("runtime") ? "runtime" : s === "trigger" ? "trigger" : "activity";
}
function Hp(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function Wp(e) {
  return !!e && (ke(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function Fp(e) {
  return !!e && (ke(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function Bp(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function Dl(e) {
  const t = ds(e);
  return t ? {
    kind: t.kind,
    schemaVersion: t.schemaVersion,
    payload: Gp(t.payload.initialPayload)
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
function Kp(e, t) {
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
function Xp(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, i) => ({
    id: `sequence-${n.id}-${t[i + 1].id}`,
    source: n.id,
    target: t[i + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function $l(e) {
  if (e.structure?.kind !== cn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, i) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, s = n.target;
    if (!r?.nodeId || !s?.nodeId) return null;
    const a = Array.isArray(n.vertices) ? n.vertices.filter(Jp) : [];
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
  const n = ca(e.cases);
  if (Up(e, t) && n.length > 0)
    return [...n.map((s) => ({ name: s, displayName: s })), { name: "Default", displayName: "Default" }];
  const i = [
    ...Mi(t?.designFacets),
    ...Mi(t?.ports),
    ...Mi(t?.outputs)
  ];
  if (i.length > 0) return Zp(i);
  const r = ca(e.outcomes);
  return r.length > 0 ? r.map((s) => ({ name: s, displayName: s })) : [{ name: "Done", displayName: "Done" }];
}
function qp(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function Xi(e, t, n, i) {
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
function Yp(e, t, n) {
  const i = Xi(t.source, n, t.sourceHandle ?? "Done", void 0), r = Xi(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((s) => s.id !== t.id).concat(i, r);
}
function bn(e) {
  return Array.isArray(e) ? e.filter(qi) : null;
}
function Up(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, i = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || i === "FlowSwitch";
}
function Mi(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!ot(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...Mi(n.ports));
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
function Zp(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function ca(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function Ft(e) {
  return typeof e == "string" && e.length > 0 ? e : void 0;
}
function Gp(e) {
  return JSON.parse(JSON.stringify(e));
}
function la(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e) {
    const r = t(i);
    r && n.set(r, [...n.get(r) ?? [], i]);
  }
  return n;
}
function Pl(e) {
  return [...e].sort((t, n) => ua(n).localeCompare(ua(t)))[0];
}
function ua(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function Jp(e) {
  return ot(e) && typeof e.x == "number" && typeof e.y == "number";
}
function ot(e) {
  return typeof e == "object" && e !== null;
}
function qi(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Qp(e) {
  return e.kind === Jn ? "sequence" : e.kind === cn ? "flowchart" : "generic";
}
function eh(e) {
  return e.kind === Jn || e.kind === cn, "Activities";
}
function th(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function nh(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Yi = "workflow", ih = /* @__PURE__ */ new Set([Jn, cn]);
function rh(e, t) {
  const n = e?.structure?.kind;
  if (!n) return !1;
  if (ih.has(n)) return !0;
  const i = t?.activityVersionId === e?.activityVersionId ? ds(t) : null;
  return i?.kind === n && i.payload.supportsScopedVariables;
}
function Ml(e) {
  const t = e?.structure?.payload?.variables;
  return Array.isArray(t) ? t.filter(Rn) : [];
}
function oh(e, t) {
  return e.structure ? {
    ...e,
    structure: {
      ...e.structure,
      payload: { ...e.structure.payload, variables: t }
    }
  } : e;
}
function sh(e, t) {
  return {
    referenceKey: e,
    declaringScopeId: t && t !== Yi ? t : Yi
  };
}
function Rl(e) {
  if (e && typeof e == "object") {
    const t = e;
    return typeof t.referenceKey == "string" && t.referenceKey ? { referenceKey: t.referenceKey, declaringScopeId: typeof t.declaringScopeId == "string" ? t.declaringScopeId : null } : null;
  }
  if (typeof e == "string") {
    const t = e.trim();
    if (t.startsWith("{"))
      try {
        return Rl(JSON.parse(t));
      } catch {
      }
    return t ? { referenceKey: t, declaringScopeId: null } : null;
  }
  return null;
}
function ah(e, t) {
  if (!e) return "";
  const n = [`workflow:${da(e.variables)}`], i = (r) => {
    const s = Ae(r, t), a = s.flatMap((c) => c.activities.map((u) => u.nodeId));
    n.push(`${r.nodeId}:${da(Ml(r))}>${a.join(",")}`), s.forEach((c) => c.activities.forEach(i));
  };
  return e.rootActivity && i(e.rootActivity), n.join(";");
}
function da(e) {
  return (e ?? []).map((t) => `${t.referenceKey}=${t.name}`).join(",");
}
function ch(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e ?? [])
    i.scopeId === t && n.set(i.referenceKey, `Shadows "${i.name}" declared in an outer scope.`);
  return n;
}
const be = "/_elsa/workflow-management", lh = "/publishing", wt = {
  activities: ["workflows", "activities"],
  activityAvailabilitySettings: ["workflows", "activity-availability", "settings"],
  activityAvailabilityDiagnostics: ["workflows", "activity-availability", "diagnostics"],
  runtimeDiagnosticsSettings: ["workflows", "runtime-diagnostics", "settings"]
};
function uh(e) {
  return ar({
    queryKey: wt.activities,
    queryFn: () => ur(e),
    staleTime: 6e4
  });
}
function dh(e) {
  return ar({
    queryKey: wt.activityAvailabilitySettings,
    queryFn: () => Ph(e)
  });
}
function fh(e) {
  return ar({
    queryKey: wt.activityAvailabilityDiagnostics,
    queryFn: () => Vl(e)
  });
}
function ph(e) {
  const t = gl();
  return ml({
    mutationFn: (n) => Mh(e, n),
    onSuccess: (n) => {
      t.setQueryData(wt.activityAvailabilitySettings, n), t.invalidateQueries({ queryKey: wt.activityAvailabilityDiagnostics }), t.invalidateQueries({ queryKey: wt.activities });
    }
  });
}
function hh(e) {
  return ar({
    queryKey: wt.runtimeDiagnosticsSettings,
    queryFn: () => Rh(e)
  });
}
function gh(e) {
  const t = gl();
  return ml({
    mutationFn: (n) => Lh(e, n),
    onSuccess: () => {
      t.invalidateQueries({ queryKey: wt.runtimeDiagnosticsSettings });
    }
  });
}
async function mh(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), i = t.search.trim();
  return i && n.set("search", i), e.http.getJson(`${be}/definitions?${n.toString()}`);
}
async function yh(e, t) {
  const n = await e.http.getJson(`${be}/definitions/${encodeURIComponent(t)}`);
  return n.draft ? { ...n, draft: { ...n.draft, state: lr(n.draft.state) } } : n;
}
async function xh(e, t, n) {
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
function wh(e, t, n, i) {
  const r = ue(() => ah(t, i), [i, t]), [s, a] = K(() => fo("loading"));
  return ee(() => {
    if (!t) {
      a(fo("unavailable"));
      return;
    }
    let c = !1;
    return a((u) => ({ ...u, status: "loading" })), xh(e, t, n).then(
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
async function vh(e, t) {
  const n = await e.http.getJson(`${be}/versions/${encodeURIComponent(t)}`);
  return { ...n, state: lr(n.state) };
}
async function bh(e, t) {
  return e.http.postJson(`${be}/definitions`, t);
}
async function jh(e, t) {
  await e.http.deleteJson(`${be}/definitions/${encodeURIComponent(t)}`);
}
async function Nh(e, t) {
  await e.http.postJson(`${be}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function Sh(e, t) {
  await e.http.deleteJson(`${be}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function Ch(e, t, n) {
  return e.http.requestJson(
    `${be}/definitions/${encodeURIComponent(t)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(n)
    }
  );
}
async function kh(e, t) {
  const n = await e.http.putJson(
    `${be}/drafts/${encodeURIComponent(t.id)}`,
    { state: Gn(t.state), layout: t.layout }
  );
  return { ...n, state: lr(n.state) };
}
async function Eh(e, t) {
  return e.http.postJson(`${be}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function Ih(e, t) {
  return e.http.postJson(`${be}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function Ah(e, t) {
  const n = { ...t, state: Gn(t.state) };
  try {
    return await e.http.postJson(`${lh}/workflows/drafts/test-runs`, n);
  } catch (i) {
    const r = Bh(i);
    if (r) return r;
    throw i;
  }
}
async function Ll(e, t) {
  return e.http.postJson(`${be}/executables/${encodeURIComponent(t)}/run`, {});
}
async function zl(e) {
  const t = [`${be}/executables`, "/_demo/workflows/executables"], n = [];
  for (const i of t)
    try {
      const r = await e.http.getJson(i);
      return _h(r);
    } catch (r) {
      n.push(r);
    }
  if (n.length > 0 && n.every(fa)) return [];
  throw n.find((i) => !fa(i)) ?? n[n.length - 1] ?? new Error("Workflow executables could not be loaded.");
}
function _h(e) {
  return Array.isArray(e) ? e : e.executables ?? [];
}
function fa(e) {
  if (!(e instanceof Error)) return !1;
  const t = e.message.toLowerCase();
  return /\b404\b/.test(t) || t.includes("not found");
}
async function Dh(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.runKind && n.set("runKind", t.runKind), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const i = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${i ? `?${i}` : ""}`);
}
async function $h(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function Th(e, t, n) {
  return e.http.getJson(
    `/runtime/workflows/instances/${encodeURIComponent(t)}/activity-executions/${encodeURIComponent(n)}`
  );
}
async function ur(e) {
  return e.http.getJson(`${be}/activities`);
}
async function Ph(e) {
  return e.http.getJson(`${be}/activities/availability/settings`);
}
async function Mh(e, t) {
  return e.http.putJson(`${be}/activities/availability/settings`, t);
}
async function Vl(e) {
  return e.http.getJson(`${be}/activities/availability/diagnostics`);
}
async function Rh(e) {
  return e.http.getJson(`${be}/runtime-diagnostics/settings`);
}
async function Lh(e, t) {
  return e.http.putJson(`${be}/runtime-diagnostics/settings`, t);
}
async function zh(e) {
  const t = await dr(e, [
    `${be}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? pa(t) : pa(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function Vh(e) {
  const t = await dr(e, [
    `${be}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : Ri;
}
async function Oh(e) {
  const t = await dr(e, [
    `${be}/descriptors/variables`,
    "/descriptors/variables"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((i) => Hh(i));
}
function Hh(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = typeof t.alias == "string" && t.alias.length > 0, i = typeof t.typeName == "string" && t.typeName.length > 0;
  return n || i;
}
async function Wh(e) {
  const t = await dr(e, [
    `${be}/descriptors/storage-drivers`,
    "/descriptors/storage-drivers"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((i) => Fh(i));
}
function Fh(e) {
  return !!e && typeof e == "object" && typeof e.typeName == "string" && e.typeName.length > 0;
}
async function dr(e, t) {
  let n;
  for (const i of t)
    try {
      return await e.http.getJson(i);
    } catch (r) {
      n = r;
    }
  throw n;
}
function pa(e) {
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
function Bh(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = ha(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return ha(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function ha(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const Ri = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function Ol(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e) {
    const r = t(i)?.trim() || "Uncategorized", s = n.get(r);
    s ? s.push(i) : n.set(r, [i]);
  }
  return [...n.entries()].map(([i, r]) => ({ category: i, items: r })).sort((i, r) => i.category.localeCompare(r.category));
}
const Kh = [
  "Available",
  "BlockedByHostBaseline",
  "HiddenByManagementSettings",
  "RemovedFromCatalog",
  "UnresolvedReference"
], Xh = {
  Available: "Available",
  BlockedByHostBaseline: "Host blocked",
  HiddenByManagementSettings: "Management hidden",
  RemovedFromCatalog: "Removed",
  UnresolvedReference: "Unresolved"
}, Hl = {
  Catalog: "Catalog",
  HostBaseline: "Host baseline",
  ManagementSettings: "Management settings"
}, qh = Object.keys(Hl);
function Yh(e) {
  const t = typeof e == "number" ? qh[e] : e;
  return t && Hl[t] || t?.toString() || "";
}
function At(e) {
  return typeof e == "string" ? e : typeof e == "number" ? Kh[e] ?? "Available" : "Available";
}
function zn(e) {
  const t = At(e);
  return Xh[t] ?? t;
}
function Wl(e) {
  return At(e).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Fl(e) {
  return At(e) !== "Available";
}
function Bl(e) {
  return At(e.state) === "BlockedByHostBaseline";
}
function Uh(e) {
  return e === "Only" || e === 1 ? "Only" : "AllExcept";
}
function Zh(e) {
  return e === "Only" ? 1 : 0;
}
function Po(e) {
  const t = e?.rules;
  return {
    mode: Uh(e?.mode),
    activityTypes: t?.activityTypes ?? [],
    sets: t?.sets ?? []
  };
}
function Gh(e) {
  return e.referenceKind === 0 || e.referenceKind === "ActivityType";
}
function jn(e) {
  return e.activityTypeKey ?? e.activityDefinitionId ?? "";
}
function Jh(e) {
  return [...e?.items ?? []].filter(Gh).filter((t) => t.activityTypeKey && t.activityDefinitionId).sort((t, n) => Vn(t).localeCompare(Vn(n)));
}
function Qh(e) {
  return [...e?.items ?? []].filter((t) => {
    const n = At(t.state);
    return n === "RemovedFromCatalog" || n === "UnresolvedReference";
  }).sort((t, n) => (t.referenceName ?? "").localeCompare(n.referenceName ?? ""));
}
function eg(e) {
  return Ol(e, (t) => t.category).map((t) => ({ category: t.category, entries: t.items }));
}
function tg(e, t, n) {
  const i = e[t].includes(n);
  return e.mode === "Only" ? i : !i;
}
function po(e, t, n, i) {
  const r = e.mode === "Only" ? i : !i;
  return e[t].includes(n) === r ? e : { ...e, [t]: rg(e[t], n) };
}
function ng(e, t, n) {
  const i = new Set(n.activityTypes), r = new Set(n.sets), s = /* @__PURE__ */ new Map();
  for (const c of t)
    if (r.has(c.name))
      for (const u of c.activityTypeKeys ?? [])
        s.has(u) || s.set(u, c.name);
  const a = /* @__PURE__ */ new Map();
  for (const c of e) {
    const u = jn(c);
    if (Bl(c)) {
      a.set(u, { enabled: !1, lockedBy: "host-baseline" });
      continue;
    }
    const l = s.get(u), d = i.has(u) || l !== void 0, f = n.mode === "Only" ? d : !d;
    a.set(u, l ? { enabled: f, lockedBy: "set-rule", governingSet: l } : { enabled: f, lockedBy: null });
  }
  return a;
}
function ig(e, t) {
  const n = Po(t), i = (r) => [...r].sort((s, a) => s.localeCompare(a)).join(`
`);
  return e.mode !== n.mode || i(e.activityTypes) !== i(n.activityTypes) || i(e.sets) !== i(n.sets);
}
function rg(e, t) {
  return e.includes(t) ? e.filter((n) => n !== t) : [...e, t].sort((n, i) => n.localeCompare(i));
}
function Vn(e) {
  const t = e?.displayName?.trim();
  if (t && t.toLowerCase() !== "activity") return t;
  const n = sg(e?.activityTypeKey);
  return og(n) || t || e?.activityTypeKey || "Activity";
}
function ga(e) {
  const t = Kl(e?.activityTypeKey);
  if (t.length === 0) return "";
  const n = t[t.length - 1], i = t[t.length - 2];
  return n === "Activity" && i ? `${i}.${n}` : n;
}
function ma(e) {
  const t = e.description?.trim();
  if (t) return t;
  const n = e.reason?.trim();
  return !n || !Fl(e.state) ? "" : n;
}
function og(e) {
  return e.replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/\bHttp\b/g, "HTTP").replace(/\bJson\b/g, "JSON").replace(/\bJava Script\b/g, "JavaScript").replace(/\bUrl\b/g, "URL").replace(/\bXml\b/g, "XML").trim();
}
function sg(e) {
  const t = Kl(e), n = t[t.length - 1] ?? "";
  return n === "Activity" && t.length > 1 ? t[t.length - 2] : n;
}
function Kl(e) {
  return e?.split(".").filter(Boolean) ?? [];
}
function ya(e) {
  return (e ?? []).flatMap((t) => {
    if (!Rn(t)) return [];
    const n = (ut(t, ["displayName", "DisplayName"]) || ut(t, us)).trim();
    if (!n) return [];
    const i = (ls(t).alias || ut(t, ["typeName", "TypeName", "alias", "Alias"])).trim();
    return [{ name: n, typeName: i, description: ut(t, ["description", "Description"]).trim() }];
  });
}
function ag(e, t) {
  const n = new Set(e.filter((i) => !!i));
  return (t?.items ?? []).find((i) => Fl(i.state) ? [i.activityDefinitionId, i.activityTypeKey, i.referenceName].some((r) => r && n.has(r)) : !1) ?? null;
}
const cg = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function Xl(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function ps(e) {
  return Xl(e.name);
}
function lg(e, t) {
  const n = ps(t), i = e[n];
  return t.isWrapped === !1 ? i ?? t.defaultValue ?? "" : Yl(i, t);
}
function ql(e, t) {
  return Yl(e[ps(t)], t);
}
function ug(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function dg(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function xa(e, t, n) {
  return {
    ...e,
    [ps(t)]: n
  };
}
function fg(e, t) {
  return t.isWrapped === !1 ? lg(e, t) : ql(e, t).expression.value;
}
function Yl(e, t) {
  return vg(e) ? {
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
const pg = /* @__PURE__ */ new Set([
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
function hg(e) {
  const t = e?.trim();
  if (!t) return null;
  const n = /^([\w.+]+)\[\]/.exec(t);
  if (n) return { elementTypeName: n[1] };
  const i = t.indexOf("`");
  if (i < 0) return null;
  const r = t.slice(0, i), s = (r.split(".").pop() ?? r).toLowerCase();
  return pg.has(s) ? { elementTypeName: gg(t.slice(i)) } : null;
}
function gg(e) {
  const t = /\[\[([\w.+]+)/.exec(e);
  if (t) return t[1];
  const n = /\[([\w.+]+)/.exec(e);
  return n ? n[1] : null;
}
function mg(e) {
  const t = e.uiHint?.toLowerCase();
  return t === "json" || t === "code" ? !0 : e.uiSpecifications?.repeater === !1;
}
function yg(e) {
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
function xg(e) {
  const t = (e ?? "").toLowerCase();
  return t === "system.boolean" || t === "boolean" || t === "bool" ? !1 : "";
}
function wg(e, t) {
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
function vg(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
function hs(e) {
  return gs(e?.trim() ?? "") || e;
}
function gs(e) {
  if (!e) return "";
  const t = bg(e);
  if (!t) return "";
  const n = /^(.*)((?:\[\])+)$/.exec(t);
  if (n) return `${gs(n[1])}${n[2]}`;
  const i = t.indexOf("`");
  if (i >= 0) {
    const r = wa(t.slice(0, i)), s = jg(t.slice(i));
    return s.length > 0 ? `${r}<${s.join(", ")}>` : r;
  }
  return wa(t);
}
function bg(e) {
  let t = 0;
  for (let n = 0; n < e.length; n++) {
    const i = e[n];
    if (i === "[") t++;
    else if (i === "]") t--;
    else if (i === "," && t === 0) return e.slice(0, n).trim();
  }
  return e.trim();
}
function wa(e) {
  const t = e.split(".").filter(Boolean).at(-1) ?? e;
  return t.split("+").filter(Boolean).at(-1) ?? t;
}
function jg(e) {
  const t = e.indexOf("[");
  if (t < 0) return [];
  const n = va(e, t);
  return n == null ? [] : Ng(n).map((i) => {
    const r = i.trim(), s = r.startsWith("[") ? va(r, 0) ?? r : r;
    return gs(s);
  }).filter(Boolean);
}
function va(e, t) {
  let n = 0;
  for (let i = t; i < e.length; i++)
    if (e[i] === "[") n++;
    else if (e[i] === "]" && --n === 0) return e.slice(t + 1, i);
  return null;
}
function Ng(e) {
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
function fr(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ o.jsx(ll, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ o.jsx(is, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ o.jsx(Lf, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ o.jsx(Zt, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ o.jsx(Rf, { size: 15 });
    default:
      return /* @__PURE__ */ o.jsx(Zn, { size: 15 });
  }
}
function ba(e) {
  return `${e.activityTypeKey ?? ""}|${e.activityDefinitionId ?? ""}`;
}
function Sg(e, t) {
  const n = (a) => (a ?? "").split(".").map((c) => Number.parseInt(c, 10) || 0), i = n(e), r = n(t), s = Math.max(i.length, r.length);
  for (let a = 0; a < s; a++) {
    const c = (i[a] ?? 0) - (r[a] ?? 0);
    if (c !== 0) return c;
  }
  return 0;
}
function Cg(e) {
  if (e.lockedBy === "host-baseline") return "Blocked by the host baseline";
  if (e.lockedBy === "set-rule") return `Controlled by the "${e.governingSet}" set rule`;
}
function Ul({ icon: e }) {
  return /* @__PURE__ */ o.jsx("span", { className: "wf-activity-icon", "data-icon": e, "aria-hidden": "true", children: fr(e) });
}
function kg({ context: e }) {
  const t = dh(e), n = fh(e), i = uh(e), r = ph(e), s = t.data ?? null, a = n.data ?? null, c = t.isLoading || n.isLoading, u = r.isPending, [l, d] = K(() => Po(s)), [f, p] = K(""), [h, m] = K(!1), [v, w] = K(null), [y, b] = K(null);
  ee(() => {
    d(Po(s));
  }, [s]);
  const g = ue(() => Jh(a), [a]), x = ue(() => Qh(a), [a]), N = ue(() => a?.sets ?? [], [a]), j = ue(
    () => ng(g, N, l),
    [g, N, l]
  ), S = (O) => j.get(jn(O)) ?? { enabled: !0, lockedBy: null }, I = ue(() => {
    const O = /* @__PURE__ */ new Map();
    for (const q of i.data?.activities ?? []) {
      if (!q.activityTypeKey) continue;
      const J = O.get(q.activityTypeKey);
      (!J || Sg(q.version, J.version) > 0) && O.set(q.activityTypeKey, q);
    }
    return O;
  }, [i.data]), _ = (O) => I.get(O.activityTypeKey ?? ""), M = ue(() => {
    const O = f.trim().toLowerCase(), q = h ? g.filter((J) => !(j.get(jn(J))?.enabled ?? !0)) : g;
    return O ? q.filter((J) => [
      Vn(J),
      ga(J),
      ma(J),
      J.activityTypeKey,
      J.category
    ].some((G) => (G ?? "").toLowerCase().includes(O))) : q;
  }, [g, f, h, j]), k = ue(() => eg(M), [M]), T = y ? g.find((O) => ba(O) === y) ?? null : null, z = g.filter(Bl).length, C = g.filter((O) => At(O.state) === "HiddenByManagementSettings").length, A = ue(() => ig(l, s), [l, s]), E = r.error ?? t.error ?? n.error, D = E instanceof Error ? E.message : E ? "Activity availability could not be loaded." : null, P = (O) => {
    w(null), d(O);
  }, $ = (O) => P((q) => ({ ...q, mode: O })), F = (O, q) => P((J) => po(J, "activityTypes", O, q)), W = (O, q) => P((J) => O.reduce((G, R) => po(G, "activityTypes", R, q), J)), H = (O, q) => P((J) => po(J, "sets", O, q)), U = () => {
    w(null), r.mutate(
      {
        scope: s?.scope ?? "host-default",
        mode: Zh(l.mode),
        rules: { activityTypes: l.activityTypes, sets: l.sets }
      },
      { onSuccess: () => w("Activity availability saved.") }
    );
  };
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-page availability-page", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-page-header", children: [
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("div", { className: "wf-kicker", children: "Workflows" }),
        /* @__PURE__ */ o.jsxs("h2", { children: [
          /* @__PURE__ */ o.jsx(zf, { size: 18 }),
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
        /* @__PURE__ */ o.jsxs("button", { type: "button", className: l.mode === "AllExcept" ? "active" : "", onClick: () => $("AllExcept"), disabled: c || u, children: [
          /* @__PURE__ */ o.jsx(co, { size: 15 }),
          /* @__PURE__ */ o.jsxs("span", { children: [
            /* @__PURE__ */ o.jsx("strong", { children: "All except" }),
            /* @__PURE__ */ o.jsx("em", { children: "Activities are available unless you turn them off" })
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", className: l.mode === "Only" ? "active" : "", onClick: () => $("Only"), disabled: c || u, children: [
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
          /* @__PURE__ */ o.jsx(co, { size: 14 }),
          " ",
          C,
          " management hidden"
        ] }),
        /* @__PURE__ */ o.jsxs("span", { children: [
          /* @__PURE__ */ o.jsx($n, { size: 14 }),
          " ",
          x.length,
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
              checked: tg(l, "sets", O.name),
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
            /* @__PURE__ */ o.jsx(ul, { size: 14 }),
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
                  /* @__PURE__ */ o.jsx(co, { size: 13 }),
                  "Hidden only"
                ]
              }
            ),
            /* @__PURE__ */ o.jsxs("div", { className: "wf-search availability-search", children: [
              /* @__PURE__ */ o.jsx(sr, { size: 14 }),
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
                  const de = jn(X), le = ba(X), te = At(X.state), ie = S(X), fe = ie.lockedBy !== null, V = un(_(X)), ne = Vn(X), ge = ga(X), ye = ma(X), Ie = y === le;
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
                          /* @__PURE__ */ o.jsx(Ul, { icon: V }),
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
                    te !== "Available" && /* @__PURE__ */ o.jsx("em", { className: `availability-state ${Wl(X.state)}`, children: zn(X.state) }),
                    /* @__PURE__ */ o.jsx(
                      "input",
                      {
                        type: "checkbox",
                        role: "switch",
                        className: "wf-switch-input",
                        "aria-label": `${ne} available in new workflows`,
                        title: Cg(ie),
                        checked: ie.enabled,
                        disabled: c || u || fe,
                        onChange: (De) => F(de, De.target.checked)
                      }
                    )
                  ] }, le);
                })
              ] }, O.category);
            })
          ] }),
          T && /* @__PURE__ */ o.jsx(
            Eg,
            {
              entry: T,
              catalogItem: _(T),
              onClose: () => b(null)
            }
          )
        ] })
      ] }),
      x.length > 0 && /* @__PURE__ */ o.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ o.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ o.jsx($n, { size: 14 }),
          " Unresolved references"
        ] }),
        /* @__PURE__ */ o.jsx("div", { className: "availability-unresolved-list", children: x.map((O) => /* @__PURE__ */ o.jsxs("span", { children: [
          /* @__PURE__ */ o.jsx("strong", { children: O.referenceName }),
          /* @__PURE__ */ o.jsx("em", { children: zn(O.state) })
        ] }, `${O.layer}-${O.referenceKind}-${O.referenceName}`)) })
      ] })
    ] })
  ] });
}
function Eg({
  entry: e,
  catalogItem: t,
  onClose: n
}) {
  const i = Vn(e), r = e.description?.trim() || t?.description?.trim(), s = ue(() => ya(t?.inputs), [t]), a = ue(() => ya(t?.outputs), [t]), c = ue(() => JSON.stringify({ diagnostic: e, catalog: t }, null, 2), [e, t]), u = [
    ["Type", e.activityTypeKey],
    ["Definition ID", e.activityDefinitionId],
    ["Category", e.category],
    ["Version", t?.version],
    ["Execution", t?.executionType],
    ["Policy layer", Yh(e.layer)]
  ];
  return /* @__PURE__ */ o.jsxs("aside", { className: "availability-details", "aria-label": `${i} details`, children: [
    /* @__PURE__ */ o.jsxs("header", { className: "availability-details-header", children: [
      /* @__PURE__ */ o.jsx(Ul, { icon: un(t) }),
      /* @__PURE__ */ o.jsx("h4", { children: i }),
      /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-icon-button availability-details-close", "aria-label": "Close details", onClick: n, children: /* @__PURE__ */ o.jsx(ss, { size: 14 }) })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "availability-details-body", children: [
      /* @__PURE__ */ o.jsxs("p", { className: "availability-details-status", children: [
        /* @__PURE__ */ o.jsx("em", { className: `availability-state ${Wl(e.state)}`, children: zn(e.state) }),
        e.reason?.trim() && /* @__PURE__ */ o.jsx("span", { children: e.reason })
      ] }),
      r && /* @__PURE__ */ o.jsx("p", { className: "availability-details-description", children: r }),
      /* @__PURE__ */ o.jsx("dl", { className: "availability-details-meta", children: u.map(([l, d]) => d?.trim() ? /* @__PURE__ */ o.jsxs(Fe.Fragment, { children: [
        /* @__PURE__ */ o.jsx("dt", { children: l }),
        /* @__PURE__ */ o.jsx("dd", { children: /* @__PURE__ */ o.jsx("code", { children: d }) })
      ] }, l) : null) }),
      s.length > 0 && /* @__PURE__ */ o.jsx(ja, { title: "Inputs", items: s }),
      a.length > 0 && /* @__PURE__ */ o.jsx(ja, { title: "Outputs", items: a }),
      !t && /* @__PURE__ */ o.jsx("p", { className: "wf-muted availability-details-note", children: "Catalog metadata is unavailable for this activity — it is not exposed by the currently saved policy." }),
      /* @__PURE__ */ o.jsxs("details", { className: "availability-details-raw", children: [
        /* @__PURE__ */ o.jsx("summary", { children: "Raw metadata" }),
        /* @__PURE__ */ o.jsx("pre", { children: c })
      ] })
    ] })
  ] });
}
function ja({ title: e, items: t }) {
  return /* @__PURE__ */ o.jsxs("section", { className: "availability-details-arguments", children: [
    /* @__PURE__ */ o.jsx("h5", { children: e }),
    /* @__PURE__ */ o.jsx("ul", { children: t.map((n) => /* @__PURE__ */ o.jsxs("li", { title: n.description || void 0, children: [
      /* @__PURE__ */ o.jsx("strong", { children: n.name }),
      n.typeName && /* @__PURE__ */ o.jsx("code", { children: hs(n.typeName) })
    ] }, n.name)) })
  ] });
}
const Zl = ["Off", "Metadata", "DiagnosticSnapshot", "Payload"], Na = new Map(Zl.map((e, t) => [e, t])), Ig = [
  { id: "workflowInputs", label: "Workflow inputs", detail: "Start arguments and workflow-level input values." },
  { id: "workflowOutputs", label: "Workflow outputs", detail: "Values emitted as workflow outputs." },
  { id: "activityInputs", label: "Activity inputs", detail: "Materialized values passed to activities." },
  { id: "activityOutputs", label: "Activity outputs", detail: "Values produced by completed activities." },
  { id: "containerVariables", label: "Container variables", detail: "Runtime values scoped to containers." },
  { id: "durableValues", label: "Durable values", detail: "Persisted wait/resume payload evidence." },
  { id: "incidents", label: "Incidents", detail: "Fault and incident diagnostic material." },
  { id: "diagnostics", label: "Diagnostics", detail: "Internal runtime diagnostic payloads." }
];
function Ag({ context: e }) {
  const t = hh(e), n = gh(e), i = t.data ?? null, r = t.isLoading, s = n.isPending, a = i?.permissions.canManage ?? !1, [c, u] = K(() => Sa(i)), [l, d] = K(null);
  ee(() => {
    u(Sa(i));
  }, [i]);
  const f = n.error ?? t.error, p = f instanceof Error ? f.message : f ? "Runtime diagnostics settings could not be loaded." : null, h = i?.hostPolicy.limitationReasons ?? [], m = i?.effective.limitationReasons ?? [], v = ue(() => Zl.filter((g) => _g(g, i)), [i]), w = (g) => u((x) => ({ ...x, defaultLevel: g })), y = (g, x) => u((N) => {
    const j = { ...N.subjectOverrides };
    return x === "inherit" ? delete j[g] : j[g] = x, { ...N, subjectOverrides: j };
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
            /* @__PURE__ */ o.jsx(Vf, { size: 14 }),
            " Default capture"
          ] }),
          i && /* @__PURE__ */ o.jsxs("span", { className: "runtime-diagnostics-effective", children: [
            "Effective: ",
            Nn(i.effective.defaultLevel)
          ] })
        ] }),
        /* @__PURE__ */ o.jsx(
          $g,
          {
            value: c.defaultLevel,
            allowedLevels: v,
            disabled: r || s || !a,
            onChange: w
          }
        )
      ] }),
      /* @__PURE__ */ o.jsxs("section", { className: "runtime-diagnostics-section runtime-diagnostics-section-grow", children: [
        /* @__PURE__ */ o.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ o.jsx(Gs, { size: 14 }),
          " Subject overrides"
        ] }),
        /* @__PURE__ */ o.jsx("div", { className: "runtime-diagnostics-subjects", children: Ig.map((g) => {
          const x = c.subjectOverrides[g.id] ?? "inherit", N = i ? Dg(i, g.id) : null;
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
                  value: x,
                  disabled: r || s || !a,
                  onChange: (j) => y(g.id, j.target.value),
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
          /* @__PURE__ */ o.jsx($n, { size: 14 }),
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
function Sa(e) {
  return {
    defaultLevel: e?.requested.defaultLevel ?? "DiagnosticSnapshot",
    subjectOverrides: { ...e?.requested.subjectOverrides ?? {} }
  };
}
function _g(e, t) {
  return t ? e === "Payload" && !t.permissions.canEnableFullPayloads ? !1 : (Na.get(e) ?? 0) <= (Na.get(t.hostPolicy.maximumLevel) ?? 0) : e !== "Payload";
}
function Dg(e, t) {
  return e.effective.subjectOverrides?.[t] ?? e.effective.defaultLevel;
}
function Nn(e) {
  return e === "DiagnosticSnapshot" ? "Diagnostic snapshot" : e;
}
function $g({
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
        /* @__PURE__ */ o.jsx("em", { children: Tg(r) })
      ]
    },
    r
  )) });
}
function Tg(e) {
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
function _e(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, i; n < e.length; n++)
      (i = _e(e[n])) !== "" && (t += (t && " ") + i);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var Pg = { value: () => {
} };
function pr() {
  for (var e = 0, t = arguments.length, n = {}, i; e < t; ++e) {
    if (!(i = arguments[e] + "") || i in n || /[\s.]/.test(i)) throw new Error("illegal type: " + i);
    n[i] = [];
  }
  return new Li(n);
}
function Li(e) {
  this._ = e;
}
function Mg(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var i = "", r = n.indexOf(".");
    if (r >= 0 && (i = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: i };
  });
}
Li.prototype = pr.prototype = {
  constructor: Li,
  on: function(e, t) {
    var n = this._, i = Mg(e + "", n), r, s = -1, a = i.length;
    if (arguments.length < 2) {
      for (; ++s < a; ) if ((r = (e = i[s]).type) && (r = Rg(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++s < a; )
      if (r = (e = i[s]).type) n[r] = Ca(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = Ca(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new Li(e);
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
function Rg(e, t) {
  for (var n = 0, i = e.length, r; n < i; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function Ca(e, t, n) {
  for (var i = 0, r = e.length; i < r; ++i)
    if (e[i].name === t) {
      e[i] = Pg, e = e.slice(0, i).concat(e.slice(i + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var Mo = "http://www.w3.org/1999/xhtml";
const ka = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: Mo,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function hr(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), ka.hasOwnProperty(t) ? { space: ka[t], local: e } : e;
}
function Lg(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === Mo && t.documentElement.namespaceURI === Mo ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function zg(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function Gl(e) {
  var t = hr(e);
  return (t.local ? zg : Lg)(t);
}
function Vg() {
}
function ys(e) {
  return e == null ? Vg : function() {
    return this.querySelector(e);
  };
}
function Og(e) {
  typeof e != "function" && (e = ys(e));
  for (var t = this._groups, n = t.length, i = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = i[r] = new Array(a), u, l, d = 0; d < a; ++d)
      (u = s[d]) && (l = e.call(u, u.__data__, d, s)) && ("__data__" in u && (l.__data__ = u.__data__), c[d] = l);
  return new Be(i, this._parents);
}
function Hg(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function Wg() {
  return [];
}
function Jl(e) {
  return e == null ? Wg : function() {
    return this.querySelectorAll(e);
  };
}
function Fg(e) {
  return function() {
    return Hg(e.apply(this, arguments));
  };
}
function Bg(e) {
  typeof e == "function" ? e = Fg(e) : e = Jl(e);
  for (var t = this._groups, n = t.length, i = [], r = [], s = 0; s < n; ++s)
    for (var a = t[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && (i.push(e.call(u, u.__data__, l, a)), r.push(u));
  return new Be(i, r);
}
function Ql(e) {
  return function() {
    return this.matches(e);
  };
}
function eu(e) {
  return function(t) {
    return t.matches(e);
  };
}
var Kg = Array.prototype.find;
function Xg(e) {
  return function() {
    return Kg.call(this.children, e);
  };
}
function qg() {
  return this.firstElementChild;
}
function Yg(e) {
  return this.select(e == null ? qg : Xg(typeof e == "function" ? e : eu(e)));
}
var Ug = Array.prototype.filter;
function Zg() {
  return Array.from(this.children);
}
function Gg(e) {
  return function() {
    return Ug.call(this.children, e);
  };
}
function Jg(e) {
  return this.selectAll(e == null ? Zg : Gg(typeof e == "function" ? e : eu(e)));
}
function Qg(e) {
  typeof e != "function" && (e = Ql(e));
  for (var t = this._groups, n = t.length, i = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = i[r] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new Be(i, this._parents);
}
function tu(e) {
  return new Array(e.length);
}
function em() {
  return new Be(this._enter || this._groups.map(tu), this._parents);
}
function Ui(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
Ui.prototype = {
  constructor: Ui,
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
function tm(e) {
  return function() {
    return e;
  };
}
function nm(e, t, n, i, r, s) {
  for (var a = 0, c, u = t.length, l = s.length; a < l; ++a)
    (c = t[a]) ? (c.__data__ = s[a], i[a] = c) : n[a] = new Ui(e, s[a]);
  for (; a < u; ++a)
    (c = t[a]) && (r[a] = c);
}
function im(e, t, n, i, r, s, a) {
  var c, u, l = /* @__PURE__ */ new Map(), d = t.length, f = s.length, p = new Array(d), h;
  for (c = 0; c < d; ++c)
    (u = t[c]) && (p[c] = h = a.call(u, u.__data__, c, t) + "", l.has(h) ? r[c] = u : l.set(h, u));
  for (c = 0; c < f; ++c)
    h = a.call(e, s[c], c, s) + "", (u = l.get(h)) ? (i[c] = u, u.__data__ = s[c], l.delete(h)) : n[c] = new Ui(e, s[c]);
  for (c = 0; c < d; ++c)
    (u = t[c]) && l.get(p[c]) === u && (r[c] = u);
}
function rm(e) {
  return e.__data__;
}
function om(e, t) {
  if (!arguments.length) return Array.from(this, rm);
  var n = t ? im : nm, i = this._parents, r = this._groups;
  typeof e != "function" && (e = tm(e));
  for (var s = r.length, a = new Array(s), c = new Array(s), u = new Array(s), l = 0; l < s; ++l) {
    var d = i[l], f = r[l], p = f.length, h = sm(e.call(d, d && d.__data__, l, i)), m = h.length, v = c[l] = new Array(m), w = a[l] = new Array(m), y = u[l] = new Array(p);
    n(d, f, v, w, y, h, t);
    for (var b = 0, g = 0, x, N; b < m; ++b)
      if (x = v[b]) {
        for (b >= g && (g = b + 1); !(N = w[g]) && ++g < m; ) ;
        x._next = N || null;
      }
  }
  return a = new Be(a, i), a._enter = c, a._exit = u, a;
}
function sm(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function am() {
  return new Be(this._exit || this._groups.map(tu), this._parents);
}
function cm(e, t, n) {
  var i = this.enter(), r = this, s = this.exit();
  return typeof e == "function" ? (i = e(i), i && (i = i.selection())) : i = i.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? s.remove() : n(s), i && r ? i.merge(r).order() : r;
}
function lm(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, i = t._groups, r = n.length, s = i.length, a = Math.min(r, s), c = new Array(r), u = 0; u < a; ++u)
    for (var l = n[u], d = i[u], f = l.length, p = c[u] = new Array(f), h, m = 0; m < f; ++m)
      (h = l[m] || d[m]) && (p[m] = h);
  for (; u < r; ++u)
    c[u] = n[u];
  return new Be(c, this._parents);
}
function um() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var i = e[t], r = i.length - 1, s = i[r], a; --r >= 0; )
      (a = i[r]) && (s && a.compareDocumentPosition(s) ^ 4 && s.parentNode.insertBefore(a, s), s = a);
  return this;
}
function dm(e) {
  e || (e = fm);
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
function fm(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function pm() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function hm() {
  return Array.from(this);
}
function gm() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var i = e[t], r = 0, s = i.length; r < s; ++r) {
      var a = i[r];
      if (a) return a;
    }
  return null;
}
function mm() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function ym() {
  return !this.node();
}
function xm(e) {
  for (var t = this._groups, n = 0, i = t.length; n < i; ++n)
    for (var r = t[n], s = 0, a = r.length, c; s < a; ++s)
      (c = r[s]) && e.call(c, c.__data__, s, r);
  return this;
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
function bm(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function jm(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function Nm(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function Sm(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function Cm(e, t) {
  var n = hr(e);
  if (arguments.length < 2) {
    var i = this.node();
    return n.local ? i.getAttributeNS(n.space, n.local) : i.getAttribute(n);
  }
  return this.each((t == null ? n.local ? vm : wm : typeof t == "function" ? n.local ? Sm : Nm : n.local ? jm : bm)(n, t));
}
function nu(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function km(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Em(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function Im(e, t, n) {
  return function() {
    var i = t.apply(this, arguments);
    i == null ? this.style.removeProperty(e) : this.style.setProperty(e, i, n);
  };
}
function Am(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? km : typeof t == "function" ? Im : Em)(e, t, n ?? "")) : Jt(this.node(), e);
}
function Jt(e, t) {
  return e.style.getPropertyValue(t) || nu(e).getComputedStyle(e, null).getPropertyValue(t);
}
function _m(e) {
  return function() {
    delete this[e];
  };
}
function Dm(e, t) {
  return function() {
    this[e] = t;
  };
}
function $m(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function Tm(e, t) {
  return arguments.length > 1 ? this.each((t == null ? _m : typeof t == "function" ? $m : Dm)(e, t)) : this.node()[e];
}
function iu(e) {
  return e.trim().split(/^|\s+/);
}
function xs(e) {
  return e.classList || new ru(e);
}
function ru(e) {
  this._node = e, this._names = iu(e.getAttribute("class") || "");
}
ru.prototype = {
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
function ou(e, t) {
  for (var n = xs(e), i = -1, r = t.length; ++i < r; ) n.add(t[i]);
}
function su(e, t) {
  for (var n = xs(e), i = -1, r = t.length; ++i < r; ) n.remove(t[i]);
}
function Pm(e) {
  return function() {
    ou(this, e);
  };
}
function Mm(e) {
  return function() {
    su(this, e);
  };
}
function Rm(e, t) {
  return function() {
    (t.apply(this, arguments) ? ou : su)(this, e);
  };
}
function Lm(e, t) {
  var n = iu(e + "");
  if (arguments.length < 2) {
    for (var i = xs(this.node()), r = -1, s = n.length; ++r < s; ) if (!i.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? Rm : t ? Pm : Mm)(n, t));
}
function zm() {
  this.textContent = "";
}
function Vm(e) {
  return function() {
    this.textContent = e;
  };
}
function Om(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function Hm(e) {
  return arguments.length ? this.each(e == null ? zm : (typeof e == "function" ? Om : Vm)(e)) : this.node().textContent;
}
function Wm() {
  this.innerHTML = "";
}
function Fm(e) {
  return function() {
    this.innerHTML = e;
  };
}
function Bm(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function Km(e) {
  return arguments.length ? this.each(e == null ? Wm : (typeof e == "function" ? Bm : Fm)(e)) : this.node().innerHTML;
}
function Xm() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function qm() {
  return this.each(Xm);
}
function Ym() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Um() {
  return this.each(Ym);
}
function Zm(e) {
  var t = typeof e == "function" ? e : Gl(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Gm() {
  return null;
}
function Jm(e, t) {
  var n = typeof e == "function" ? e : Gl(e), i = t == null ? Gm : typeof t == "function" ? t : ys(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), i.apply(this, arguments) || null);
  });
}
function Qm() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function ey() {
  return this.each(Qm);
}
function ty() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function ny() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function iy(e) {
  return this.select(e ? ny : ty);
}
function ry(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function oy(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function sy(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", i = t.indexOf(".");
    return i >= 0 && (n = t.slice(i + 1), t = t.slice(0, i)), { type: t, name: n };
  });
}
function ay(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, i = -1, r = t.length, s; n < r; ++n)
        s = t[n], (!e.type || s.type === e.type) && s.name === e.name ? this.removeEventListener(s.type, s.listener, s.options) : t[++i] = s;
      ++i ? t.length = i : delete this.__on;
    }
  };
}
function cy(e, t, n) {
  return function() {
    var i = this.__on, r, s = oy(t);
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
function ly(e, t, n) {
  var i = sy(e + ""), r, s = i.length, a;
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
  for (c = t ? cy : ay, r = 0; r < s; ++r) this.each(c(i[r], t, n));
  return this;
}
function au(e, t, n) {
  var i = nu(e), r = i.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = i.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function uy(e, t) {
  return function() {
    return au(this, e, t);
  };
}
function dy(e, t) {
  return function() {
    return au(this, e, t.apply(this, arguments));
  };
}
function fy(e, t) {
  return this.each((typeof t == "function" ? dy : uy)(e, t));
}
function* py() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var i = e[t], r = 0, s = i.length, a; r < s; ++r)
      (a = i[r]) && (yield a);
}
var cu = [null];
function Be(e, t) {
  this._groups = e, this._parents = t;
}
function Qn() {
  return new Be([[document.documentElement]], cu);
}
function hy() {
  return this;
}
Be.prototype = Qn.prototype = {
  constructor: Be,
  select: Og,
  selectAll: Bg,
  selectChild: Yg,
  selectChildren: Jg,
  filter: Qg,
  data: om,
  enter: em,
  exit: am,
  join: cm,
  merge: lm,
  selection: hy,
  order: um,
  sort: dm,
  call: pm,
  nodes: hm,
  node: gm,
  size: mm,
  empty: ym,
  each: xm,
  attr: Cm,
  style: Am,
  property: Tm,
  classed: Lm,
  text: Hm,
  html: Km,
  raise: qm,
  lower: Um,
  append: Zm,
  insert: Jm,
  remove: ey,
  clone: iy,
  datum: ry,
  on: ly,
  dispatch: fy,
  [Symbol.iterator]: py
};
function He(e) {
  return typeof e == "string" ? new Be([[document.querySelector(e)]], [document.documentElement]) : new Be([[e]], cu);
}
function gy(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Ze(e, t) {
  if (e = gy(e), t === void 0 && (t = e.currentTarget), t) {
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
const my = { passive: !1 }, On = { capture: !0, passive: !1 };
function go(e) {
  e.stopImmediatePropagation();
}
function Yt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function lu(e) {
  var t = e.document.documentElement, n = He(e).on("dragstart.drag", Yt, On);
  "onselectstart" in t ? n.on("selectstart.drag", Yt, On) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function uu(e, t) {
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
function yy(e) {
  return !e.ctrlKey && !e.button;
}
function xy() {
  return this.parentNode;
}
function wy(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function vy() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function du() {
  var e = yy, t = xy, n = wy, i = vy, r = {}, s = pr("start", "drag", "end"), a = 0, c, u, l, d, f = 0;
  function p(x) {
    x.on("mousedown.drag", h).filter(i).on("touchstart.drag", w).on("touchmove.drag", y, my).on("touchend.drag touchcancel.drag", b).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function h(x, N) {
    if (!(d || !e.call(this, x, N))) {
      var j = g(this, t.call(this, x, N), x, N, "mouse");
      j && (He(x.view).on("mousemove.drag", m, On).on("mouseup.drag", v, On), lu(x.view), go(x), l = !1, c = x.clientX, u = x.clientY, j("start", x));
    }
  }
  function m(x) {
    if (Yt(x), !l) {
      var N = x.clientX - c, j = x.clientY - u;
      l = N * N + j * j > f;
    }
    r.mouse("drag", x);
  }
  function v(x) {
    He(x.view).on("mousemove.drag mouseup.drag", null), uu(x.view, l), Yt(x), r.mouse("end", x);
  }
  function w(x, N) {
    if (e.call(this, x, N)) {
      var j = x.changedTouches, S = t.call(this, x, N), I = j.length, _, M;
      for (_ = 0; _ < I; ++_)
        (M = g(this, S, x, N, j[_].identifier, j[_])) && (go(x), M("start", x, j[_]));
    }
  }
  function y(x) {
    var N = x.changedTouches, j = N.length, S, I;
    for (S = 0; S < j; ++S)
      (I = r[N[S].identifier]) && (Yt(x), I("drag", x, N[S]));
  }
  function b(x) {
    var N = x.changedTouches, j = N.length, S, I;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), S = 0; S < j; ++S)
      (I = r[N[S].identifier]) && (go(x), I("end", x, N[S]));
  }
  function g(x, N, j, S, I, _) {
    var M = s.copy(), k = Ze(_ || j, N), T, z, C;
    if ((C = n.call(x, new Ro("beforestart", {
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
      return T = C.x - k[0] || 0, z = C.y - k[1] || 0, function A(E, D, P) {
        var $ = k, F;
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
          x,
          new Ro(E, {
            sourceEvent: D,
            subject: C,
            target: p,
            identifier: I,
            active: F,
            x: k[0] + T,
            y: k[1] + z,
            dx: k[0] - $[0],
            dy: k[1] - $[1],
            dispatch: M
          }),
          S
        );
      };
  }
  return p.filter = function(x) {
    return arguments.length ? (e = typeof x == "function" ? x : Ni(!!x), p) : e;
  }, p.container = function(x) {
    return arguments.length ? (t = typeof x == "function" ? x : Ni(x), p) : t;
  }, p.subject = function(x) {
    return arguments.length ? (n = typeof x == "function" ? x : Ni(x), p) : n;
  }, p.touchable = function(x) {
    return arguments.length ? (i = typeof x == "function" ? x : Ni(!!x), p) : i;
  }, p.on = function() {
    var x = s.on.apply(s, arguments);
    return x === s ? p : x;
  }, p.clickDistance = function(x) {
    return arguments.length ? (f = (x = +x) * x, p) : Math.sqrt(f);
  }, p;
}
function ws(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function fu(e, t) {
  var n = Object.create(e.prototype);
  for (var i in t) n[i] = t[i];
  return n;
}
function ei() {
}
var Hn = 0.7, Zi = 1 / Hn, Ut = "\\s*([+-]?\\d+)\\s*", Wn = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", it = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", by = /^#([0-9a-f]{3,8})$/, jy = new RegExp(`^rgb\\(${Ut},${Ut},${Ut}\\)$`), Ny = new RegExp(`^rgb\\(${it},${it},${it}\\)$`), Sy = new RegExp(`^rgba\\(${Ut},${Ut},${Ut},${Wn}\\)$`), Cy = new RegExp(`^rgba\\(${it},${it},${it},${Wn}\\)$`), ky = new RegExp(`^hsl\\(${Wn},${it},${it}\\)$`), Ey = new RegExp(`^hsla\\(${Wn},${it},${it},${Wn}\\)$`), Ea = {
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
  hex: Ia,
  // Deprecated! Use color.formatHex.
  formatHex: Ia,
  formatHex8: Iy,
  formatHsl: Ay,
  formatRgb: Aa,
  toString: Aa
});
function Ia() {
  return this.rgb().formatHex();
}
function Iy() {
  return this.rgb().formatHex8();
}
function Ay() {
  return pu(this).formatHsl();
}
function Aa() {
  return this.rgb().formatRgb();
}
function Dt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = by.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? _a(t) : n === 3 ? new ze(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? Si(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? Si(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = jy.exec(e)) ? new ze(t[1], t[2], t[3], 1) : (t = Ny.exec(e)) ? new ze(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = Sy.exec(e)) ? Si(t[1], t[2], t[3], t[4]) : (t = Cy.exec(e)) ? Si(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = ky.exec(e)) ? Ta(t[1], t[2] / 100, t[3] / 100, 1) : (t = Ey.exec(e)) ? Ta(t[1], t[2] / 100, t[3] / 100, t[4]) : Ea.hasOwnProperty(e) ? _a(Ea[e]) : e === "transparent" ? new ze(NaN, NaN, NaN, 0) : null;
}
function _a(e) {
  return new ze(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function Si(e, t, n, i) {
  return i <= 0 && (e = t = n = NaN), new ze(e, t, n, i);
}
function _y(e) {
  return e instanceof ei || (e = Dt(e)), e ? (e = e.rgb(), new ze(e.r, e.g, e.b, e.opacity)) : new ze();
}
function Lo(e, t, n, i) {
  return arguments.length === 1 ? _y(e) : new ze(e, t, n, i ?? 1);
}
function ze(e, t, n, i) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +i;
}
ws(ze, Lo, fu(ei, {
  brighter(e) {
    return e = e == null ? Zi : Math.pow(Zi, e), new ze(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Hn : Math.pow(Hn, e), new ze(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new ze(kt(this.r), kt(this.g), kt(this.b), Gi(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: Da,
  // Deprecated! Use color.formatHex.
  formatHex: Da,
  formatHex8: Dy,
  formatRgb: $a,
  toString: $a
}));
function Da() {
  return `#${Ct(this.r)}${Ct(this.g)}${Ct(this.b)}`;
}
function Dy() {
  return `#${Ct(this.r)}${Ct(this.g)}${Ct(this.b)}${Ct((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function $a() {
  const e = Gi(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${kt(this.r)}, ${kt(this.g)}, ${kt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Gi(e) {
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
function pu(e) {
  if (e instanceof Ge) return new Ge(e.h, e.s, e.l, e.opacity);
  if (e instanceof ei || (e = Dt(e)), !e) return new Ge();
  if (e instanceof Ge) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, i = e.b / 255, r = Math.min(t, n, i), s = Math.max(t, n, i), a = NaN, c = s - r, u = (s + r) / 2;
  return c ? (t === s ? a = (n - i) / c + (n < i) * 6 : n === s ? a = (i - t) / c + 2 : a = (t - n) / c + 4, c /= u < 0.5 ? s + r : 2 - s - r, a *= 60) : c = u > 0 && u < 1 ? 0 : a, new Ge(a, c, u, e.opacity);
}
function $y(e, t, n, i) {
  return arguments.length === 1 ? pu(e) : new Ge(e, t, n, i ?? 1);
}
function Ge(e, t, n, i) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +i;
}
ws(Ge, $y, fu(ei, {
  brighter(e) {
    return e = e == null ? Zi : Math.pow(Zi, e), new Ge(this.h, this.s, this.l * e, this.opacity);
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
    return new Ge(Pa(this.h), Ci(this.s), Ci(this.l), Gi(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Gi(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${Pa(this.h)}, ${Ci(this.s) * 100}%, ${Ci(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function Pa(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function Ci(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function mo(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const vs = (e) => () => e;
function Ty(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function Py(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(i) {
    return Math.pow(e + i * t, n);
  };
}
function My(e) {
  return (e = +e) == 1 ? hu : function(t, n) {
    return n - t ? Py(t, n, e) : vs(isNaN(t) ? n : t);
  };
}
function hu(e, t) {
  var n = t - e;
  return n ? Ty(e, n) : vs(isNaN(e) ? t : e);
}
const Ji = (function e(t) {
  var n = My(t);
  function i(r, s) {
    var a = n((r = Lo(r)).r, (s = Lo(s)).r), c = n(r.g, s.g), u = n(r.b, s.b), l = hu(r.opacity, s.opacity);
    return function(d) {
      return r.r = a(d), r.g = c(d), r.b = u(d), r.opacity = l(d), r + "";
    };
  }
  return i.gamma = e, i;
})(1);
function Ry(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, i = t.slice(), r;
  return function(s) {
    for (r = 0; r < n; ++r) i[r] = e[r] * (1 - s) + t[r] * s;
    return i;
  };
}
function Ly(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function zy(e, t) {
  var n = t ? t.length : 0, i = e ? Math.min(n, e.length) : 0, r = new Array(i), s = new Array(n), a;
  for (a = 0; a < i; ++a) r[a] = _n(e[a], t[a]);
  for (; a < n; ++a) s[a] = t[a];
  return function(c) {
    for (a = 0; a < i; ++a) s[a] = r[a](c);
    return s;
  };
}
function Vy(e, t) {
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
function Oy(e, t) {
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
function Hy(e) {
  return function() {
    return e;
  };
}
function Wy(e) {
  return function(t) {
    return e(t) + "";
  };
}
function gu(e, t) {
  var n = zo.lastIndex = yo.lastIndex = 0, i, r, s, a = -1, c = [], u = [];
  for (e = e + "", t = t + ""; (i = zo.exec(e)) && (r = yo.exec(t)); )
    (s = r.index) > n && (s = t.slice(n, s), c[a] ? c[a] += s : c[++a] = s), (i = i[0]) === (r = r[0]) ? c[a] ? c[a] += r : c[++a] = r : (c[++a] = null, u.push({ i: a, x: nt(i, r) })), n = yo.lastIndex;
  return n < t.length && (s = t.slice(n), c[a] ? c[a] += s : c[++a] = s), c.length < 2 ? u[0] ? Wy(u[0].x) : Hy(t) : (t = u.length, function(l) {
    for (var d = 0, f; d < t; ++d) c[(f = u[d]).i] = f.x(l);
    return c.join("");
  });
}
function _n(e, t) {
  var n = typeof t, i;
  return t == null || n === "boolean" ? vs(t) : (n === "number" ? nt : n === "string" ? (i = Dt(t)) ? (t = i, Ji) : gu : t instanceof Dt ? Ji : t instanceof Date ? Vy : Ly(t) ? Ry : Array.isArray(t) ? zy : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Oy : nt)(e, t);
}
var Ma = 180 / Math.PI, Vo = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function mu(e, t, n, i, r, s) {
  var a, c, u;
  return (a = Math.sqrt(e * e + t * t)) && (e /= a, t /= a), (u = e * n + t * i) && (n -= e * u, i -= t * u), (c = Math.sqrt(n * n + i * i)) && (n /= c, i /= c, u /= c), e * i < t * n && (e = -e, t = -t, u = -u, a = -a), {
    translateX: r,
    translateY: s,
    rotate: Math.atan2(t, e) * Ma,
    skewX: Math.atan(u) * Ma,
    scaleX: a,
    scaleY: c
  };
}
var ki;
function Fy(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Vo : mu(t.a, t.b, t.c, t.d, t.e, t.f);
}
function By(e) {
  return e == null || (ki || (ki = document.createElementNS("http://www.w3.org/2000/svg", "g")), ki.setAttribute("transform", e), !(e = ki.transform.baseVal.consolidate())) ? Vo : (e = e.matrix, mu(e.a, e.b, e.c, e.d, e.e, e.f));
}
function yu(e, t, n, i) {
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
      for (var m = -1, v = p.length, w; ++m < v; ) f[(w = p[m]).i] = w.x(h);
      return f.join("");
    };
  };
}
var Ky = yu(Fy, "px, ", "px)", "deg)"), Xy = yu(By, ", ", ")", ")"), qy = 1e-12;
function Ra(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Yy(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Uy(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const zi = (function e(t, n, i) {
  function r(s, a) {
    var c = s[0], u = s[1], l = s[2], d = a[0], f = a[1], p = a[2], h = d - c, m = f - u, v = h * h + m * m, w, y;
    if (v < qy)
      y = Math.log(p / l) / t, w = function(S) {
        return [
          c + S * h,
          u + S * m,
          l * Math.exp(t * S * y)
        ];
      };
    else {
      var b = Math.sqrt(v), g = (p * p - l * l + i * v) / (2 * l * n * b), x = (p * p - l * l - i * v) / (2 * p * n * b), N = Math.log(Math.sqrt(g * g + 1) - g), j = Math.log(Math.sqrt(x * x + 1) - x);
      y = (j - N) / t, w = function(S) {
        var I = S * y, _ = Ra(N), M = l / (n * b) * (_ * Uy(t * I + N) - Yy(N));
        return [
          c + M * h,
          u + M * m,
          l * _ / Ra(t * I + N)
        ];
      };
    }
    return w.duration = y * 1e3 * t / Math.SQRT2, w;
  }
  return r.rho = function(s) {
    var a = Math.max(1e-3, +s), c = a * a, u = c * c;
    return e(a, c, u);
  }, r;
})(Math.SQRT2, 2, 4);
var Qt = 0, Sn = 0, xn = 0, xu = 1e3, Qi, Cn, er = 0, $t = 0, gr = 0, Fn = typeof performance == "object" && performance.now ? performance : Date, wu = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function bs() {
  return $t || (wu(Zy), $t = Fn.now() + gr);
}
function Zy() {
  $t = 0;
}
function tr() {
  this._call = this._time = this._next = null;
}
tr.prototype = vu.prototype = {
  constructor: tr,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? bs() : +n) + (t == null ? 0 : +t), !this._next && Cn !== this && (Cn ? Cn._next = this : Qi = this, Cn = this), this._call = e, this._time = n, Oo();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Oo());
  }
};
function vu(e, t, n) {
  var i = new tr();
  return i.restart(e, t, n), i;
}
function Gy() {
  bs(), ++Qt;
  for (var e = Qi, t; e; )
    (t = $t - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --Qt;
}
function La() {
  $t = (er = Fn.now()) + gr, Qt = Sn = 0;
  try {
    Gy();
  } finally {
    Qt = 0, Qy(), $t = 0;
  }
}
function Jy() {
  var e = Fn.now(), t = e - er;
  t > xu && (gr -= t, er = e);
}
function Qy() {
  for (var e, t = Qi, n, i = 1 / 0; t; )
    t._call ? (i > t._time && (i = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Qi = n);
  Cn = e, Oo(i);
}
function Oo(e) {
  if (!Qt) {
    Sn && (Sn = clearTimeout(Sn));
    var t = e - $t;
    t > 24 ? (e < 1 / 0 && (Sn = setTimeout(La, e - Fn.now() - gr)), xn && (xn = clearInterval(xn))) : (xn || (er = Fn.now(), xn = setInterval(Jy, xu)), Qt = 1, wu(La));
  }
}
function za(e, t, n) {
  var i = new tr();
  return t = t == null ? 0 : +t, i.restart((r) => {
    i.stop(), e(r + t);
  }, t, n), i;
}
var ex = pr("start", "end", "cancel", "interrupt"), tx = [], bu = 0, Va = 1, Ho = 2, Vi = 3, Oa = 4, Wo = 5, Oi = 6;
function mr(e, t, n, i, r, s) {
  var a = e.__transition;
  if (!a) e.__transition = {};
  else if (n in a) return;
  nx(e, n, {
    name: t,
    index: i,
    // For context during callback.
    group: r,
    // For context during callback.
    on: ex,
    tween: tx,
    time: s.time,
    delay: s.delay,
    duration: s.duration,
    ease: s.ease,
    timer: null,
    state: bu
  });
}
function js(e, t) {
  var n = et(e, t);
  if (n.state > bu) throw new Error("too late; already scheduled");
  return n;
}
function st(e, t) {
  var n = et(e, t);
  if (n.state > Vi) throw new Error("too late; already running");
  return n;
}
function et(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function nx(e, t, n) {
  var i = e.__transition, r;
  i[t] = n, n.timer = vu(s, 0, n.time);
  function s(l) {
    n.state = Va, n.timer.restart(a, n.delay, n.time), n.delay <= l && a(l - n.delay);
  }
  function a(l) {
    var d, f, p, h;
    if (n.state !== Va) return u();
    for (d in i)
      if (h = i[d], h.name === n.name) {
        if (h.state === Vi) return za(a);
        h.state === Oa ? (h.state = Oi, h.timer.stop(), h.on.call("interrupt", e, e.__data__, h.index, h.group), delete i[d]) : +d < t && (h.state = Oi, h.timer.stop(), h.on.call("cancel", e, e.__data__, h.index, h.group), delete i[d]);
      }
    if (za(function() {
      n.state === Vi && (n.state = Oa, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = Ho, n.on.call("start", e, e.__data__, n.index, n.group), n.state === Ho) {
      for (n.state = Vi, r = new Array(p = n.tween.length), d = 0, f = -1; d < p; ++d)
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
    n.state = Oi, n.timer.stop(), delete i[t];
    for (var l in i) return;
    delete e.__transition;
  }
}
function Hi(e, t) {
  var n = e.__transition, i, r, s = !0, a;
  if (n) {
    t = t == null ? null : t + "";
    for (a in n) {
      if ((i = n[a]).name !== t) {
        s = !1;
        continue;
      }
      r = i.state > Ho && i.state < Wo, i.state = Oi, i.timer.stop(), i.on.call(r ? "interrupt" : "cancel", e, e.__data__, i.index, i.group), delete n[a];
    }
    s && delete e.__transition;
  }
}
function ix(e) {
  return this.each(function() {
    Hi(this, e);
  });
}
function rx(e, t) {
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
function ox(e, t, n) {
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
function sx(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var i = et(this.node(), n).tween, r = 0, s = i.length, a; r < s; ++r)
      if ((a = i[r]).name === e)
        return a.value;
    return null;
  }
  return this.each((t == null ? rx : ox)(n, e, t));
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
function ju(e, t) {
  var n;
  return (typeof t == "number" ? nt : t instanceof Dt ? Ji : (n = Dt(t)) ? (t = n, Ji) : gu)(e, t);
}
function ax(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function cx(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function lx(e, t, n) {
  var i, r = n + "", s;
  return function() {
    var a = this.getAttribute(e);
    return a === r ? null : a === i ? s : s = t(i = a, n);
  };
}
function ux(e, t, n) {
  var i, r = n + "", s;
  return function() {
    var a = this.getAttributeNS(e.space, e.local);
    return a === r ? null : a === i ? s : s = t(i = a, n);
  };
}
function dx(e, t, n) {
  var i, r, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttribute(e) : (a = this.getAttribute(e), u = c + "", a === u ? null : a === i && u === r ? s : (r = u, s = t(i = a, c)));
  };
}
function fx(e, t, n) {
  var i, r, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (a = this.getAttributeNS(e.space, e.local), u = c + "", a === u ? null : a === i && u === r ? s : (r = u, s = t(i = a, c)));
  };
}
function px(e, t) {
  var n = hr(e), i = n === "transform" ? Xy : ju;
  return this.attrTween(e, typeof t == "function" ? (n.local ? fx : dx)(n, i, Ns(this, "attr." + e, t)) : t == null ? (n.local ? cx : ax)(n) : (n.local ? ux : lx)(n, i, t));
}
function hx(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function gx(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function mx(e, t) {
  var n, i;
  function r() {
    var s = t.apply(this, arguments);
    return s !== i && (n = (i = s) && gx(e, s)), n;
  }
  return r._value = t, r;
}
function yx(e, t) {
  var n, i;
  function r() {
    var s = t.apply(this, arguments);
    return s !== i && (n = (i = s) && hx(e, s)), n;
  }
  return r._value = t, r;
}
function xx(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var i = hr(e);
  return this.tween(n, (i.local ? mx : yx)(i, t));
}
function wx(e, t) {
  return function() {
    js(this, e).delay = +t.apply(this, arguments);
  };
}
function vx(e, t) {
  return t = +t, function() {
    js(this, e).delay = t;
  };
}
function bx(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? wx : vx)(t, e)) : et(this.node(), t).delay;
}
function jx(e, t) {
  return function() {
    st(this, e).duration = +t.apply(this, arguments);
  };
}
function Nx(e, t) {
  return t = +t, function() {
    st(this, e).duration = t;
  };
}
function Sx(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? jx : Nx)(t, e)) : et(this.node(), t).duration;
}
function Cx(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    st(this, e).ease = t;
  };
}
function kx(e) {
  var t = this._id;
  return arguments.length ? this.each(Cx(t, e)) : et(this.node(), t).ease;
}
function Ex(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    st(this, e).ease = n;
  };
}
function Ix(e) {
  if (typeof e != "function") throw new Error();
  return this.each(Ex(this._id, e));
}
function Ax(e) {
  typeof e != "function" && (e = Ql(e));
  for (var t = this._groups, n = t.length, i = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = i[r] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new pt(i, this._parents, this._name, this._id);
}
function _x(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, i = t.length, r = n.length, s = Math.min(i, r), a = new Array(i), c = 0; c < s; ++c)
    for (var u = t[c], l = n[c], d = u.length, f = a[c] = new Array(d), p, h = 0; h < d; ++h)
      (p = u[h] || l[h]) && (f[h] = p);
  for (; c < i; ++c)
    a[c] = t[c];
  return new pt(a, this._parents, this._name, this._id);
}
function Dx(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function $x(e, t, n) {
  var i, r, s = Dx(t) ? js : st;
  return function() {
    var a = s(this, e), c = a.on;
    c !== i && (r = (i = c).copy()).on(t, n), a.on = r;
  };
}
function Tx(e, t) {
  var n = this._id;
  return arguments.length < 2 ? et(this.node(), n).on.on(e) : this.each($x(n, e, t));
}
function Px(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function Mx() {
  return this.on("end.remove", Px(this._id));
}
function Rx(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = ys(e));
  for (var i = this._groups, r = i.length, s = new Array(r), a = 0; a < r; ++a)
    for (var c = i[a], u = c.length, l = s[a] = new Array(u), d, f, p = 0; p < u; ++p)
      (d = c[p]) && (f = e.call(d, d.__data__, p, c)) && ("__data__" in d && (f.__data__ = d.__data__), l[p] = f, mr(l[p], t, n, p, l, et(d, n)));
  return new pt(s, this._parents, t, n);
}
function Lx(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Jl(e));
  for (var i = this._groups, r = i.length, s = [], a = [], c = 0; c < r; ++c)
    for (var u = i[c], l = u.length, d, f = 0; f < l; ++f)
      if (d = u[f]) {
        for (var p = e.call(d, d.__data__, f, u), h, m = et(d, n), v = 0, w = p.length; v < w; ++v)
          (h = p[v]) && mr(h, t, n, v, p, m);
        s.push(p), a.push(d);
      }
  return new pt(s, a, t, n);
}
var zx = Qn.prototype.constructor;
function Vx() {
  return new zx(this._groups, this._parents);
}
function Ox(e, t) {
  var n, i, r;
  return function() {
    var s = Jt(this, e), a = (this.style.removeProperty(e), Jt(this, e));
    return s === a ? null : s === n && a === i ? r : r = t(n = s, i = a);
  };
}
function Nu(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Hx(e, t, n) {
  var i, r = n + "", s;
  return function() {
    var a = Jt(this, e);
    return a === r ? null : a === i ? s : s = t(i = a, n);
  };
}
function Wx(e, t, n) {
  var i, r, s;
  return function() {
    var a = Jt(this, e), c = n(this), u = c + "";
    return c == null && (u = c = (this.style.removeProperty(e), Jt(this, e))), a === u ? null : a === i && u === r ? s : (r = u, s = t(i = a, c));
  };
}
function Fx(e, t) {
  var n, i, r, s = "style." + t, a = "end." + s, c;
  return function() {
    var u = st(this, e), l = u.on, d = u.value[s] == null ? c || (c = Nu(t)) : void 0;
    (l !== n || r !== d) && (i = (n = l).copy()).on(a, r = d), u.on = i;
  };
}
function Bx(e, t, n) {
  var i = (e += "") == "transform" ? Ky : ju;
  return t == null ? this.styleTween(e, Ox(e, i)).on("end.style." + e, Nu(e)) : typeof t == "function" ? this.styleTween(e, Wx(e, i, Ns(this, "style." + e, t))).each(Fx(this._id, e)) : this.styleTween(e, Hx(e, i, t), n).on("end.style." + e, null);
}
function Kx(e, t, n) {
  return function(i) {
    this.style.setProperty(e, t.call(this, i), n);
  };
}
function Xx(e, t, n) {
  var i, r;
  function s() {
    var a = t.apply(this, arguments);
    return a !== r && (i = (r = a) && Kx(e, a, n)), i;
  }
  return s._value = t, s;
}
function qx(e, t, n) {
  var i = "style." + (e += "");
  if (arguments.length < 2) return (i = this.tween(i)) && i._value;
  if (t == null) return this.tween(i, null);
  if (typeof t != "function") throw new Error();
  return this.tween(i, Xx(e, t, n ?? ""));
}
function Yx(e) {
  return function() {
    this.textContent = e;
  };
}
function Ux(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function Zx(e) {
  return this.tween("text", typeof e == "function" ? Ux(Ns(this, "text", e)) : Yx(e == null ? "" : e + ""));
}
function Gx(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Jx(e) {
  var t, n;
  function i() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Gx(r)), t;
  }
  return i._value = e, i;
}
function Qx(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, Jx(e));
}
function ew() {
  for (var e = this._name, t = this._id, n = Su(), i = this._groups, r = i.length, s = 0; s < r; ++s)
    for (var a = i[s], c = a.length, u, l = 0; l < c; ++l)
      if (u = a[l]) {
        var d = et(u, t);
        mr(u, e, n, l, a, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new pt(i, this._parents, e, n);
}
function tw() {
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
var nw = 0;
function pt(e, t, n, i) {
  this._groups = e, this._parents = t, this._name = n, this._id = i;
}
function Su() {
  return ++nw;
}
var lt = Qn.prototype;
pt.prototype = {
  constructor: pt,
  select: Rx,
  selectAll: Lx,
  selectChild: lt.selectChild,
  selectChildren: lt.selectChildren,
  filter: Ax,
  merge: _x,
  selection: Vx,
  transition: ew,
  call: lt.call,
  nodes: lt.nodes,
  node: lt.node,
  size: lt.size,
  empty: lt.empty,
  each: lt.each,
  on: Tx,
  attr: px,
  attrTween: xx,
  style: Bx,
  styleTween: qx,
  text: Zx,
  textTween: Qx,
  remove: Mx,
  tween: sx,
  delay: bx,
  duration: Sx,
  ease: kx,
  easeVarying: Ix,
  end: tw,
  [Symbol.iterator]: lt[Symbol.iterator]
};
function iw(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var rw = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: iw
};
function ow(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function sw(e) {
  var t, n;
  e instanceof pt ? (t = e._id, e = e._name) : (t = Su(), (n = rw).time = bs(), e = e == null ? null : e + "");
  for (var i = this._groups, r = i.length, s = 0; s < r; ++s)
    for (var a = i[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && mr(u, e, t, l, a, n || ow(u, t));
  return new pt(i, this._parents, e, t);
}
Qn.prototype.interrupt = ix;
Qn.prototype.transition = sw;
const Ei = (e) => () => e;
function aw(e, {
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
var yr = new dt(1, 0, 0);
Cu.prototype = dt.prototype;
function Cu(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return yr;
  return e.__zoom;
}
function xo(e) {
  e.stopImmediatePropagation();
}
function wn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function cw(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function lw() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function Ha() {
  return this.__zoom || yr;
}
function uw(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function dw() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function fw(e, t, n) {
  var i = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], s = e.invertY(t[0][1]) - n[0][1], a = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > i ? (i + r) / 2 : Math.min(0, i) || Math.max(0, r),
    a > s ? (s + a) / 2 : Math.min(0, s) || Math.max(0, a)
  );
}
function ku() {
  var e = cw, t = lw, n = fw, i = uw, r = dw, s = [0, 1 / 0], a = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, u = zi, l = pr("start", "zoom", "end"), d, f, p, h = 500, m = 150, v = 0, w = 10;
  function y(C) {
    C.property("__zoom", Ha).on("wheel.zoom", I, { passive: !1 }).on("mousedown.zoom", _).on("dblclick.zoom", M).filter(r).on("touchstart.zoom", k).on("touchmove.zoom", T).on("touchend.zoom touchcancel.zoom", z).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  y.transform = function(C, A, E, D) {
    var P = C.selection ? C.selection() : C;
    P.property("__zoom", Ha), C !== P ? N(C, A, E, D) : P.interrupt().each(function() {
      j(this, arguments).event(D).start().zoom(null, typeof A == "function" ? A.apply(this, arguments) : A).end();
    });
  }, y.scaleBy = function(C, A, E, D) {
    y.scaleTo(C, function() {
      var P = this.__zoom.k, $ = typeof A == "function" ? A.apply(this, arguments) : A;
      return P * $;
    }, E, D);
  }, y.scaleTo = function(C, A, E, D) {
    y.transform(C, function() {
      var P = t.apply(this, arguments), $ = this.__zoom, F = E == null ? x(P) : typeof E == "function" ? E.apply(this, arguments) : E, W = $.invert(F), H = typeof A == "function" ? A.apply(this, arguments) : A;
      return n(g(b($, H), F, W), P, a);
    }, E, D);
  }, y.translateBy = function(C, A, E, D) {
    y.transform(C, function() {
      return n(this.__zoom.translate(
        typeof A == "function" ? A.apply(this, arguments) : A,
        typeof E == "function" ? E.apply(this, arguments) : E
      ), t.apply(this, arguments), a);
    }, null, D);
  }, y.translateTo = function(C, A, E, D, P) {
    y.transform(C, function() {
      var $ = t.apply(this, arguments), F = this.__zoom, W = D == null ? x($) : typeof D == "function" ? D.apply(this, arguments) : D;
      return n(yr.translate(W[0], W[1]).scale(F.k).translate(
        typeof A == "function" ? -A.apply(this, arguments) : -A,
        typeof E == "function" ? -E.apply(this, arguments) : -E
      ), $, a);
    }, D, P);
  };
  function b(C, A) {
    return A = Math.max(s[0], Math.min(s[1], A)), A === C.k ? C : new dt(A, C.x, C.y);
  }
  function g(C, A, E) {
    var D = A[0] - E[0] * C.k, P = A[1] - E[1] * C.k;
    return D === C.x && P === C.y ? C : new dt(C.k, D, P);
  }
  function x(C) {
    return [(+C[0][0] + +C[1][0]) / 2, (+C[0][1] + +C[1][1]) / 2];
  }
  function N(C, A, E, D) {
    C.on("start.zoom", function() {
      j(this, arguments).event(D).start();
    }).on("interrupt.zoom end.zoom", function() {
      j(this, arguments).event(D).end();
    }).tween("zoom", function() {
      var P = this, $ = arguments, F = j(P, $).event(D), W = t.apply(P, $), H = E == null ? x(W) : typeof E == "function" ? E.apply(P, $) : E, U = Math.max(W[1][0] - W[0][0], W[1][1] - W[0][1]), O = P.__zoom, q = typeof A == "function" ? A.apply(P, $) : A, J = u(O.invert(H).concat(U / O.k), q.invert(H).concat(U / q.k));
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
        new aw(C, {
          sourceEvent: this.sourceEvent,
          target: y,
          transform: this.that.__zoom,
          dispatch: l
        }),
        A
      );
    }
  };
  function I(C, ...A) {
    if (!e.apply(this, arguments)) return;
    var E = j(this, A).event(C), D = this.__zoom, P = Math.max(s[0], Math.min(s[1], D.k * Math.pow(2, i.apply(this, arguments)))), $ = Ze(C);
    if (E.wheel)
      (E.mouse[0][0] !== $[0] || E.mouse[0][1] !== $[1]) && (E.mouse[1] = D.invert(E.mouse[0] = $)), clearTimeout(E.wheel);
    else {
      if (D.k === P) return;
      E.mouse = [$, D.invert($)], Hi(this), E.start();
    }
    wn(C), E.wheel = setTimeout(F, m), E.zoom("mouse", n(g(b(D, P), E.mouse[0], E.mouse[1]), E.extent, a));
    function F() {
      E.wheel = null, E.end();
    }
  }
  function _(C, ...A) {
    if (p || !e.apply(this, arguments)) return;
    var E = C.currentTarget, D = j(this, A, !0).event(C), P = He(C.view).on("mousemove.zoom", H, !0).on("mouseup.zoom", U, !0), $ = Ze(C, E), F = C.clientX, W = C.clientY;
    lu(C.view), xo(C), D.mouse = [$, this.__zoom.invert($)], Hi(this), D.start();
    function H(O) {
      if (wn(O), !D.moved) {
        var q = O.clientX - F, J = O.clientY - W;
        D.moved = q * q + J * J > v;
      }
      D.event(O).zoom("mouse", n(g(D.that.__zoom, D.mouse[0] = Ze(O, E), D.mouse[1]), D.extent, a));
    }
    function U(O) {
      P.on("mousemove.zoom mouseup.zoom", null), uu(O.view, D.moved), wn(O), D.event(O).end();
    }
  }
  function M(C, ...A) {
    if (e.apply(this, arguments)) {
      var E = this.__zoom, D = Ze(C.changedTouches ? C.changedTouches[0] : C, this), P = E.invert(D), $ = E.k * (C.shiftKey ? 0.5 : 2), F = n(g(b(E, $), D, P), t.apply(this, A), a);
      wn(C), c > 0 ? He(this).transition().duration(c).call(N, F, D, C) : He(this).call(y.transform, F, D, C);
    }
  }
  function k(C, ...A) {
    if (e.apply(this, arguments)) {
      var E = C.touches, D = E.length, P = j(this, A, C.changedTouches.length === D).event(C), $, F, W, H;
      for (xo(C), F = 0; F < D; ++F)
        W = E[F], H = Ze(W, this), H = [H, this.__zoom.invert(H), W.identifier], P.touch0 ? !P.touch1 && P.touch0[2] !== H[2] && (P.touch1 = H, P.taps = 0) : (P.touch0 = H, $ = !0, P.taps = 1 + !!d);
      d && (d = clearTimeout(d)), $ && (P.taps < 2 && (f = H[0], d = setTimeout(function() {
        d = null;
      }, h)), Hi(this), P.start());
    }
  }
  function T(C, ...A) {
    if (this.__zooming) {
      var E = j(this, A).event(C), D = C.changedTouches, P = D.length, $, F, W, H;
      for (wn(C), $ = 0; $ < P; ++$)
        F = D[$], W = Ze(F, this), E.touch0 && E.touch0[2] === F.identifier ? E.touch0[0] = W : E.touch1 && E.touch1[2] === F.identifier && (E.touch1[0] = W);
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
      var E = j(this, A).event(C), D = C.changedTouches, P = D.length, $, F;
      for (xo(C), p && clearTimeout(p), p = setTimeout(function() {
        p = null;
      }, h), $ = 0; $ < P; ++$)
        F = D[$], E.touch0 && E.touch0[2] === F.identifier ? delete E.touch0 : E.touch1 && E.touch1[2] === F.identifier && delete E.touch1;
      if (E.touch1 && !E.touch0 && (E.touch0 = E.touch1, delete E.touch1), E.touch0) E.touch0[1] = this.__zoom.invert(E.touch0[0]);
      else if (E.end(), E.taps === 2 && (F = Ze(F, this), Math.hypot(f[0] - F[0], f[1] - F[1]) < w)) {
        var W = He(this).on("dblclick.zoom");
        W && W.apply(this, arguments);
      }
    }
  }
  return y.wheelDelta = function(C) {
    return arguments.length ? (i = typeof C == "function" ? C : Ei(+C), y) : i;
  }, y.filter = function(C) {
    return arguments.length ? (e = typeof C == "function" ? C : Ei(!!C), y) : e;
  }, y.touchable = function(C) {
    return arguments.length ? (r = typeof C == "function" ? C : Ei(!!C), y) : r;
  }, y.extent = function(C) {
    return arguments.length ? (t = typeof C == "function" ? C : Ei([[+C[0][0], +C[0][1]], [+C[1][0], +C[1][1]]]), y) : t;
  }, y.scaleExtent = function(C) {
    return arguments.length ? (s[0] = +C[0], s[1] = +C[1], y) : [s[0], s[1]];
  }, y.translateExtent = function(C) {
    return arguments.length ? (a[0][0] = +C[0][0], a[1][0] = +C[1][0], a[0][1] = +C[0][1], a[1][1] = +C[1][1], y) : [[a[0][0], a[0][1]], [a[1][0], a[1][1]]];
  }, y.constrain = function(C) {
    return arguments.length ? (n = C, y) : n;
  }, y.duration = function(C) {
    return arguments.length ? (c = +C, y) : c;
  }, y.interpolate = function(C) {
    return arguments.length ? (u = C, y) : u;
  }, y.on = function() {
    var C = l.on.apply(l, arguments);
    return C === l ? y : C;
  }, y.clickDistance = function(C) {
    return arguments.length ? (v = (C = +C) * C, y) : Math.sqrt(v);
  }, y.tapDistance = function(C) {
    return arguments.length ? (w = +C, y) : w;
  }, y;
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
], Eu = ["Enter", " ", "Escape"], Iu = {
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
const Au = {
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
var nr;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(nr || (nr = {}));
var ae;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(ae || (ae = {}));
const Wa = {
  [ae.Left]: ae.Right,
  [ae.Right]: ae.Left,
  [ae.Top]: ae.Bottom,
  [ae.Bottom]: ae.Top
};
function _u(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Du = (e) => "id" in e && "source" in e && "target" in e, pw = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), Ss = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), ti = (e, t = [0, 0]) => {
  const { width: n, height: i } = ht(e), r = e.origin ?? t, s = n * r[0], a = i * r[1];
  return {
    x: e.position.x - s,
    y: e.position.y - a
  };
}, hw = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((i, r) => {
    const s = typeof r == "string";
    let a = !t.nodeLookup && !s ? r : void 0;
    t.nodeLookup && (a = s ? t.nodeLookup.get(r) : Ss(r) ? r : t.nodeLookup.get(r.id));
    const c = a ? ir(a, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return xr(i, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return wr(n);
}, ni = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, i = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = xr(n, ir(r)), i = !0);
  }), i ? wr(n) : { x: 0, y: 0, width: 0, height: 0 };
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
    const h = d.width ?? l.width ?? l.initialWidth ?? null, m = d.height ?? l.height ?? l.initialHeight ?? null, v = Xn(c, nn(l)), w = (h ?? 0) * (m ?? 0), y = s && v > 0;
    (!l.internals.handleBounds || y || v >= w || l.dragging) && u.push(l);
  }
  return u;
}, gw = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((i) => {
    n.add(i.id);
  }), t.filter((i) => n.has(i.source) || n.has(i.target));
};
function mw(e, t) {
  const n = /* @__PURE__ */ new Map(), i = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!i || i.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function yw({ nodes: e, width: t, height: n, panZoom: i, minZoom: r, maxZoom: s }, a) {
  if (e.size === 0)
    return !0;
  const c = mw(e, a), u = ni(c), l = Es(u, t, n, a?.minZoom ?? r, a?.maxZoom ?? s, a?.padding ?? 0.1);
  return await i.setViewport(l, {
    duration: a?.duration,
    ease: a?.ease,
    interpolate: a?.interpolate
  }), !0;
}
function $u({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: i = [0, 0], nodeExtent: r, onError: s }) {
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
  const p = Pt(f) ? Tt(t, f, a.measured) : t;
  return (a.measured.width === void 0 || a.measured.height === void 0) && s?.("015", Ke.error015()), {
    position: {
      x: p.x - u + (a.measured.width ?? 0) * d[0],
      y: p.y - l + (a.measured.height ?? 0) * d[1]
    },
    positionAbsolute: p
  };
}
async function xw({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: i, onBeforeDelete: r }) {
  const s = new Set(e.map((p) => p.id)), a = [];
  for (const p of n) {
    if (p.deletable === !1)
      continue;
    const h = s.has(p.id), m = !h && p.parentId && a.find((v) => v.id === p.parentId);
    (h || m) && a.push(p);
  }
  const c = new Set(t.map((p) => p.id)), u = i.filter((p) => p.deletable !== !1), d = gw(a, u);
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
const tn = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), Tt = (e = { x: 0, y: 0 }, t, n) => ({
  x: tn(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: tn(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Tu(e, t, n) {
  const { width: i, height: r } = ht(n), { x: s, y: a } = n.internals.positionAbsolute;
  return Tt(e, [
    [s, a],
    [s + i, a + r]
  ], t);
}
const Fa = (e, t, n) => e < t ? tn(Math.abs(e - t), 1, t) / t : e > n ? -tn(Math.abs(e - n), 1, t) / t : 0, ks = (e, t, n = 15, i = 40) => {
  const r = Fa(e.x, i, t.width - i) * n, s = Fa(e.y, i, t.height - i) * n;
  return [r, s];
}, xr = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), Fo = ({ x: e, y: t, width: n, height: i }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + i
}), wr = ({ x: e, y: t, x2: n, y2: i }) => ({
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
}, ir = (e, t = [0, 0]) => {
  const { x: n, y: i } = Ss(e) ? e.internals.positionAbsolute : ti(e, t);
  return {
    x: n,
    y: i,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: i + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, Pu = (e, t) => wr(xr(Fo(e), Fo(t))), Xn = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), i = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * i);
}, Ba = (e) => Je(e.width) && Je(e.height) && Je(e.x) && Je(e.y), Je = (e) => !isNaN(e) && isFinite(e), Mu = (e, t) => (n, i) => {
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
function ww(e, t, n) {
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
function vw(e, t, n, i, r, s) {
  const { x: a, y: c } = rn(e, [t, n, i]), { x: u, y: l } = rn({ x: e.x + e.width, y: e.y + e.height }, [t, n, i]), d = r - u, f = s - l;
  return {
    left: Math.floor(a),
    top: Math.floor(c),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const Es = (e, t, n, i, r, s) => {
  const a = ww(s, t, n), c = (t - a.x) / e.width, u = (n - a.y) / e.height, l = Math.min(c, u), d = tn(l, i, r), f = e.x + e.width / 2, p = e.y + e.height / 2, h = t / 2 - f * d, m = n / 2 - p * d, v = vw(e, h, m, d, t, n), w = {
    left: Math.min(v.left - a.left, 0),
    top: Math.min(v.top - a.top, 0),
    right: Math.min(v.right - a.right, 0),
    bottom: Math.min(v.bottom - a.bottom, 0)
  };
  return {
    x: h - w.left + w.right,
    y: m - w.top + w.bottom,
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
function Ru(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function Lu(e, t = { width: 0, height: 0 }, n, i, r) {
  const s = { ...e }, a = i.get(n);
  if (a) {
    const c = a.origin || r;
    s.x += a.internals.positionAbsolute.x - (t.width ?? 0) * c[0], s.y += a.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return s;
}
function Ka(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function bw() {
  let e, t;
  return { promise: new Promise((i, r) => {
    e = i, t = r;
  }), resolve: e, reject: t };
}
function jw(e) {
  return { ...Iu, ...e || {} };
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
}), zu = (e) => e?.getRootNode?.() || window?.document, Nw = ["INPUT", "SELECT", "TEXTAREA"];
function Vu(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : Nw.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const Ou = (e) => "clientX" in e, Qe = (e, t) => {
  const n = Ou(e), i = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: i - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, Xa = (e, t, n, i, r) => {
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
function Hu({ sourceX: e, sourceY: t, targetX: n, targetY: i, sourceControlX: r, sourceControlY: s, targetControlX: a, targetControlY: c }) {
  const u = e * 0.125 + r * 0.375 + a * 0.375 + n * 0.125, l = t * 0.125 + s * 0.375 + c * 0.375 + i * 0.125, d = Math.abs(u - e), f = Math.abs(l - t);
  return [u, l, d, f];
}
function Ii(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function qa({ pos: e, x1: t, y1: n, x2: i, y2: r, c: s }) {
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
function Wu({ sourceX: e, sourceY: t, sourcePosition: n = ae.Bottom, targetX: i, targetY: r, targetPosition: s = ae.Top, curvature: a = 0.25 }) {
  const [c, u] = qa({
    pos: n,
    x1: e,
    y1: t,
    x2: i,
    y2: r,
    c: a
  }), [l, d] = qa({
    pos: s,
    x1: i,
    y1: r,
    x2: e,
    y2: t,
    c: a
  }), [f, p, h, m] = Hu({
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
function Fu({ sourceX: e, sourceY: t, targetX: n, targetY: i }) {
  const r = Math.abs(n - e) / 2, s = n < e ? n + r : n - r, a = Math.abs(i - t) / 2, c = i < t ? i + a : i - a;
  return [s, c, r, a];
}
function Sw({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: i = 0, elevateOnSelect: r = !1, zIndexMode: s = "basic" }) {
  if (s === "manual")
    return i;
  const a = r && n ? i + 1e3 : i, c = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return a + c;
}
function Cw({ sourceNode: e, targetNode: t, width: n, height: i, transform: r }) {
  const s = xr(ir(e), ir(t));
  s.x === s.x2 && (s.x2 += 1), s.y === s.y2 && (s.y2 += 1);
  const a = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: i / r[2]
  };
  return Xn(a, wr(s)) > 0;
}
const Bu = ({ source: e, sourceHandle: t, target: n, targetHandle: i }) => `xy-edge__${e}${t || ""}-${n}${i || ""}`, kw = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), Ew = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Ke.error006()), t;
  const i = n.getEdgeId || Bu;
  let r;
  return Du(e) ? r = { ...e } : r = {
    ...e,
    id: i(e)
  }, kw(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, Iw = (e, t, n, i = { shouldReplaceId: !0 }) => {
  const { id: r, ...s } = e;
  if (!t.source || !t.target)
    return i.onError?.("006", Ke.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return i.onError?.("007", Ke.error007(r)), n;
  const c = i.getEdgeId || Bu, u = {
    ...s,
    id: i.shouldReplaceId ? c(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== r).concat(u);
};
function Ku({ sourceX: e, sourceY: t, targetX: n, targetY: i }) {
  const [r, s, a, c] = Fu({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: i
  });
  return [`M ${e},${t}L ${n},${i}`, r, s, a, c];
}
const Ya = {
  [ae.Left]: { x: -1, y: 0 },
  [ae.Right]: { x: 1, y: 0 },
  [ae.Top]: { x: 0, y: -1 },
  [ae.Bottom]: { x: 0, y: 1 }
}, Aw = ({ source: e, sourcePosition: t = ae.Bottom, target: n }) => t === ae.Left || t === ae.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, Ua = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function _w({ source: e, sourcePosition: t = ae.Bottom, target: n, targetPosition: i = ae.Top, center: r, offset: s, stepPosition: a }) {
  const c = Ya[t], u = Ya[i], l = { x: e.x + c.x * s, y: e.y + c.y * s }, d = { x: n.x + u.x * s, y: n.y + u.y * s }, f = Aw({
    source: l,
    sourcePosition: t,
    target: d
  }), p = f.x !== 0 ? "x" : "y", h = f[p];
  let m = [], v, w;
  const y = { x: 0, y: 0 }, b = { x: 0, y: 0 }, [, , g, x] = Fu({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[p] * u[p] === -1) {
    p === "x" ? (v = r.x ?? l.x + (d.x - l.x) * a, w = r.y ?? (l.y + d.y) / 2) : (v = r.x ?? (l.x + d.x) / 2, w = r.y ?? l.y + (d.y - l.y) * a);
    const I = [
      { x: v, y: l.y },
      { x: v, y: d.y }
    ], _ = [
      { x: l.x, y: w },
      { x: d.x, y: w }
    ];
    c[p] === h ? m = p === "x" ? I : _ : m = p === "x" ? _ : I;
  } else {
    const I = [{ x: l.x, y: d.y }], _ = [{ x: d.x, y: l.y }];
    if (p === "x" ? m = c.x === h ? _ : I : m = c.y === h ? I : _, t === i) {
      const C = Math.abs(e[p] - n[p]);
      if (C <= s) {
        const A = Math.min(s - 1, s - C);
        c[p] === h ? y[p] = (l[p] > e[p] ? -1 : 1) * A : b[p] = (d[p] > n[p] ? -1 : 1) * A;
      }
    }
    if (t !== i) {
      const C = p === "x" ? "y" : "x", A = c[p] === u[C], E = l[C] > d[C], D = l[C] < d[C];
      (c[p] === 1 && (!A && E || A && D) || c[p] !== 1 && (!A && D || A && E)) && (m = p === "x" ? I : _);
    }
    const M = { x: l.x + y.x, y: l.y + y.y }, k = { x: d.x + b.x, y: d.y + b.y }, T = Math.max(Math.abs(M.x - m[0].x), Math.abs(k.x - m[0].x)), z = Math.max(Math.abs(M.y - m[0].y), Math.abs(k.y - m[0].y));
    T >= z ? (v = (M.x + k.x) / 2, w = m[0].y) : (v = m[0].x, w = (M.y + k.y) / 2);
  }
  const N = { x: l.x + y.x, y: l.y + y.y }, j = { x: d.x + b.x, y: d.y + b.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...N.x !== m[0].x || N.y !== m[0].y ? [N] : [],
    ...m,
    ...j.x !== m[m.length - 1].x || j.y !== m[m.length - 1].y ? [j] : [],
    n
  ], v, w, g, x];
}
function Dw(e, t, n, i) {
  const r = Math.min(Ua(e, t) / 2, Ua(t, n) / 2, i), { x: s, y: a } = t;
  if (e.x === s && s === n.x || e.y === a && a === n.y)
    return `L${s} ${a}`;
  if (e.y === a) {
    const l = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${s + r * l},${a}Q ${s},${a} ${s},${a + r * d}`;
  }
  const c = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${s},${a + r * u}Q ${s},${a} ${s + r * c},${a}`;
}
function rr({ sourceX: e, sourceY: t, sourcePosition: n = ae.Bottom, targetX: i, targetY: r, targetPosition: s = ae.Top, borderRadius: a = 5, centerX: c, centerY: u, offset: l = 20, stepPosition: d = 0.5 }) {
  const [f, p, h, m, v] = _w({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: i, y: r },
    targetPosition: s,
    center: { x: c, y: u },
    offset: l,
    stepPosition: d
  });
  let w = `M${f[0].x} ${f[0].y}`;
  for (let y = 1; y < f.length - 1; y++)
    w += Dw(f[y - 1], f[y], f[y + 1], a);
  return w += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [w, p, h, m, v];
}
function Za(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function $w(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!Za(t) || !Za(n))
    return null;
  const i = t.internals.handleBounds || Ga(t.handles), r = n.internals.handleBounds || Ga(n.handles), s = Ja(i?.source ?? [], e.sourceHandle), a = Ja(
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
function Ga(e) {
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
function Ja(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Bo(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((i) => `${i}=${e[i]}`).join("&")}` : "";
}
function Tw(e, { id: t, defaultColor: n, defaultMarkerStart: i, defaultMarkerEnd: r }) {
  const s = /* @__PURE__ */ new Set();
  return e.reduce((a, c) => ([c.markerStart || i, c.markerEnd || r].forEach((u) => {
    if (u && typeof u == "object") {
      const l = Bo(u, t);
      s.has(l) || (a.push({ id: l, color: u.color || n, ...u }), s.add(l));
    }
  }), a), []).sort((a, c) => a.id.localeCompare(c.id));
}
const Xu = 1e3, Pw = 10, As = {
  nodeOrigin: [0, 0],
  nodeExtent: Bn,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Mw = {
  ...As,
  checkEquality: !0
};
function _s(e, t) {
  const n = { ...e };
  for (const i in t)
    t[i] !== void 0 && (n[i] = t[i]);
  return n;
}
function Rw(e, t, n) {
  const i = _s(As, n);
  for (const r of e.values())
    if (r.parentId)
      $s(r, e, t, i);
    else {
      const s = ti(r, i.nodeOrigin), a = Pt(r.extent) ? r.extent : i.nodeExtent, c = Tt(s, a, ht(r));
      r.internals.positionAbsolute = c;
    }
}
function Lw(e, t) {
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
  const r = _s(Mw, i), s = { i: 0 }, a = new Map(t), c = r?.elevateNodesOnSelect && !Ds(r.zIndexMode) ? Xu : 0;
  let u = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = a.get(d.id);
    if (r.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const p = ti(d, r.nodeOrigin), h = Pt(d.extent) ? d.extent : r.nodeExtent, m = Tt(p, h, ht(d));
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
          handleBounds: Lw(d, f),
          z: qu(d, c, r.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (u = !1), d.parentId && $s(f, t, n, i, s), l ||= d.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: l };
}
function zw(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function $s(e, t, n, i, r) {
  const { elevateNodesOnSelect: s, nodeOrigin: a, nodeExtent: c, zIndexMode: u } = _s(As, i), l = e.parentId, d = t.get(l);
  if (!d) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  zw(e, n), r && !d.parentId && d.internals.rootParentIndex === void 0 && u === "auto" && (d.internals.rootParentIndex = ++r.i, d.internals.z = d.internals.z + r.i * Pw), r && d.internals.rootParentIndex !== void 0 && (r.i = d.internals.rootParentIndex);
  const f = s && !Ds(u) ? Xu : 0, { x: p, y: h, z: m } = Vw(e, d, a, c, f, u), { positionAbsolute: v } = e.internals, w = p !== v.x || h !== v.y;
  (w || m !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: w ? { x: p, y: h } : v,
      z: m
    }
  });
}
function qu(e, t, n) {
  const i = Je(e.zIndex) ? e.zIndex : 0;
  return Ds(n) ? i : i + (e.selected ? t : 0);
}
function Vw(e, t, n, i, r, s) {
  const { x: a, y: c } = t.internals.positionAbsolute, u = ht(e), l = ti(e, n), d = Pt(e.extent) ? Tt(l, e.extent, u) : l;
  let f = Tt({ x: a + d.x, y: c + d.y }, i, u);
  e.extent === "parent" && (f = Tu(f, u, t));
  const p = qu(e, r, s), h = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: h >= p ? h + 1 : p
  };
}
function Ts(e, t, n, i = [0, 0]) {
  const r = [], s = /* @__PURE__ */ new Map();
  for (const a of e) {
    const c = t.get(a.parentId);
    if (!c)
      continue;
    const u = s.get(a.parentId)?.expandedRect ?? nn(c), l = Pu(u, a.rect);
    s.set(a.parentId, { expandedRect: l, parent: c });
  }
  return s.size > 0 && s.forEach(({ expandedRect: a, parent: c }, u) => {
    const l = c.internals.positionAbsolute, d = ht(c), f = c.origin ?? i, p = a.x < l.x ? Math.round(Math.abs(l.x - a.x)) : 0, h = a.y < l.y ? Math.round(Math.abs(l.y - a.y)) : 0, m = Math.max(d.width, Math.round(a.width)), v = Math.max(d.height, Math.round(a.height)), w = (m - d.width) * f[0], y = (v - d.height) * f[1];
    (p > 0 || h > 0 || w || y) && (r.push({
      id: u,
      type: "position",
      position: {
        x: c.position.x - p + w,
        y: c.position.y - h + y
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
        width: m + (p ? f[0] * p - w : 0),
        height: v + (h ? f[1] * h - y : 0)
      }
    });
  }), r;
}
function Ow(e, t, n, i, r, s, a) {
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
    const v = Is(h.nodeElement), w = m.measured.width !== v.width || m.measured.height !== v.height;
    if (!!(v.width && v.height && (w || !m.internals.handleBounds || h.force))) {
      const b = h.nodeElement.getBoundingClientRect(), g = Pt(m.extent) ? m.extent : s;
      let { positionAbsolute: x } = m.internals;
      m.parentId && m.extent === "parent" ? x = Tu(x, v, t.get(m.parentId)) : g && (x = Tt(x, g, v));
      const N = {
        ...m,
        measured: v,
        internals: {
          ...m.internals,
          positionAbsolute: x,
          handleBounds: {
            source: Xa("source", h.nodeElement, b, f, m.id),
            target: Xa("target", h.nodeElement, b, f, m.id)
          }
        }
      };
      t.set(m.id, N), m.parentId && $s(N, t, n, { nodeOrigin: r, zIndexMode: a }), u = !0, w && (l.push({
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
    const h = Ts(p, t, n, r);
    l.push(...h);
  }
  return { changes: l, updatedInternals: u };
}
async function Hw({ delta: e, panZoom: t, transform: n, translateExtent: i, width: r, height: s }) {
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
function Qa(e, t, n, i, r, s) {
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
function Yu(e, t, n) {
  e.clear(), t.clear();
  for (const i of n) {
    const { source: r, target: s, sourceHandle: a = null, targetHandle: c = null } = i, u = { edgeId: i.id, source: r, target: s, sourceHandle: a, targetHandle: c }, l = `${r}-${a}--${s}-${c}`, d = `${s}-${c}--${r}-${a}`;
    Qa("source", u, d, e, r, a), Qa("target", u, l, e, s, c), t.set(i.id, i);
  }
}
function Uu(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : Uu(n, t) : !1;
}
function ec(e, t, n) {
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
function Ww(e, t, n, i) {
  const r = /* @__PURE__ */ new Map();
  for (const [s, a] of e)
    if ((a.selected || a.id === i) && (!a.parentId || !Uu(a, e)) && (a.draggable || t && typeof a.draggable > "u")) {
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
function Fw({ dragItems: e, snapGrid: t, x: n, y: i }) {
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
function Bw({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: i, onDragStop: r }) {
  let s = { x: null, y: null }, a = 0, c = /* @__PURE__ */ new Map(), u = !1, l = { x: 0, y: 0 }, d = null, f = !1, p = null, h = !1, m = !1, v = null;
  function w({ noDragClassName: b, handleSelector: g, domNode: x, isSelectable: N, nodeId: j, nodeClickDistance: S = 0 }) {
    p = He(x);
    function I({ x: T, y: z }) {
      const { nodeLookup: C, nodeExtent: A, snapGrid: E, snapToGrid: D, nodeOrigin: P, onNodeDrag: $, onSelectionDrag: F, onError: W, updateNodePositions: H } = t();
      s = { x: T, y: z };
      let U = !1;
      const O = c.size > 1, q = O && A ? Fo(ni(c)) : null, J = O && D ? Fw({
        dragItems: c,
        snapGrid: E,
        x: T,
        y: z
      }) : null;
      for (const [G, R] of c) {
        if (!C.has(G))
          continue;
        let X = { x: T - R.distance.x, y: z - R.distance.y };
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
        const { position: le, positionAbsolute: te } = $u({
          nodeId: G,
          nextPosition: X,
          nodeLookup: C,
          nodeExtent: de || A,
          nodeOrigin: P,
          onError: W
        });
        U = U || R.position.x !== le.x || R.position.y !== le.y, R.position = le, R.internals.positionAbsolute = te;
      }
      if (m = m || U, !!U && (H(c, !0), v && (i || $ || !j && F))) {
        const [G, R] = wo({
          nodeId: j,
          dragItems: c,
          nodeLookup: C
        });
        i?.(v, c, G, R), $?.(v, G, R), j || F?.(v, R);
      }
    }
    async function _() {
      if (!d)
        return;
      const { transform: T, panBy: z, autoPanSpeed: C, autoPanOnNodeDrag: A } = t();
      if (!A) {
        u = !1, cancelAnimationFrame(a);
        return;
      }
      const [E, D] = ks(l, d, C);
      (E !== 0 || D !== 0) && (s.x = (s.x ?? 0) - E / T[2], s.y = (s.y ?? 0) - D / T[2], await z({ x: E, y: D }) && I(s)), a = requestAnimationFrame(_);
    }
    function M(T) {
      const { nodeLookup: z, multiSelectionActive: C, nodesDraggable: A, transform: E, snapGrid: D, snapToGrid: P, selectNodesOnDrag: $, onNodeDragStart: F, onSelectionDragStart: W, unselectNodesAndEdges: H } = t();
      f = !0, (!$ || !N) && !C && j && (z.get(j)?.selected || H()), N && $ && j && e?.(j);
      const U = Dn(T.sourceEvent, { transform: E, snapGrid: D, snapToGrid: P, containerBounds: d });
      if (s = U, c = Ww(z, A, U, j), c.size > 0 && (n || F || !j && W)) {
        const [O, q] = wo({
          nodeId: j,
          dragItems: c,
          nodeLookup: z
        });
        n?.(T.sourceEvent, c, O, q), F?.(T.sourceEvent, O, q), j || W?.(T.sourceEvent, q);
      }
    }
    const k = du().clickDistance(S).on("start", (T) => {
      const { domNode: z, nodeDragThreshold: C, transform: A, snapGrid: E, snapToGrid: D } = t();
      d = z?.getBoundingClientRect() || null, h = !1, m = !1, v = T.sourceEvent, C === 0 && M(T), s = Dn(T.sourceEvent, { transform: A, snapGrid: E, snapToGrid: D, containerBounds: d }), l = Qe(T.sourceEvent, d);
    }).on("drag", (T) => {
      const { autoPanOnNodeDrag: z, transform: C, snapGrid: A, snapToGrid: E, nodeDragThreshold: D, nodeLookup: P } = t(), $ = Dn(T.sourceEvent, { transform: C, snapGrid: A, snapToGrid: E, containerBounds: d });
      if (v = T.sourceEvent, (T.sourceEvent.type === "touchmove" && T.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      j && !P.has(j)) && (h = !0), !h) {
        if (!u && z && f && (u = !0, _()), !f) {
          const F = Qe(T.sourceEvent, d), W = F.x - l.x, H = F.y - l.y;
          Math.sqrt(W * W + H * H) > D && M(T);
        }
        (s.x !== $.xSnapped || s.y !== $.ySnapped) && c && f && (l = Qe(T.sourceEvent, d), I($));
      }
    }).on("end", (T) => {
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
          r?.(T.sourceEvent, c, D, P), A?.(T.sourceEvent, D, P), j || E?.(T.sourceEvent, P);
        }
      }
    }).filter((T) => {
      const z = T.target;
      return !T.button && (!b || !ec(z, `.${b}`, x)) && (!g || ec(z, g, x));
    });
    p.call(k);
  }
  function y() {
    p?.on(".drag", null);
  }
  return {
    update: w,
    destroy: y
  };
}
function Kw(e, t, n) {
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
const Xw = 250;
function qw(e, t, n, i) {
  let r = [], s = 1 / 0;
  const a = Kw(e, n, t + Xw);
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
function Zu(e, t, n, i, r, s = !1) {
  const a = i.get(e);
  if (!a)
    return null;
  const c = r === "strict" ? a.internals.handleBounds?.[t] : [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []], u = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return u && s ? { ...u, ...Mt(a, u, u.position, !0) } : u;
}
function Gu(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Yw(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const Ju = () => !0;
function Uw(e, { connectionMode: t, connectionRadius: n, handleId: i, nodeId: r, edgeUpdaterType: s, isTarget: a, domNode: c, nodeLookup: u, lib: l, autoPanOnConnect: d, flowId: f, panBy: p, cancelConnection: h, onConnectStart: m, onConnect: v, onConnectEnd: w, isValidConnection: y = Ju, onReconnectEnd: b, updateConnection: g, getTransform: x, getFromHandle: N, autoPanSpeed: j, dragThreshold: S = 1, handleDomNode: I }) {
  const _ = zu(e.target);
  let M = 0, k;
  const { x: T, y: z } = Qe(e), C = Gu(s, I), A = c?.getBoundingClientRect();
  let E = !1;
  if (!A || !C)
    return;
  const D = Zu(r, C, i, u, t);
  if (!D)
    return;
  let P = Qe(e, A), $ = !1, F = null, W = !1, H = null;
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
    toPosition: Wa[O.position],
    toNode: null,
    pointer: P
  };
  function R() {
    E = !0, g(G), m?.(e, { nodeId: r, handleId: i, handleType: C });
  }
  S === 0 && R();
  function X(le) {
    if (!E) {
      const { x: ge, y: ye } = Qe(le), Ie = ge - T, De = ye - z;
      if (!(Ie * Ie + De * De > S * S))
        return;
      R();
    }
    if (!N() || !O) {
      de(le);
      return;
    }
    const te = x();
    P = Qe(le, A), k = qw(dn(P, te, !1, [1, 1]), n, u, O), $ || (U(), $ = !0);
    const ie = Qu(le, {
      handle: k,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: i,
      fromType: a ? "target" : "source",
      isValidConnection: y,
      doc: _,
      lib: l,
      flowId: f,
      nodeLookup: u
    });
    H = ie.handleDomNode, F = ie.connection, W = Yw(!!k, ie.isValid);
    const fe = u.get(r), V = fe ? Mt(fe, O, ae.Left, !0) : G.from, ne = {
      ...G,
      from: V,
      isValid: W,
      to: ie.toHandle && W ? rn({ x: ie.toHandle.x, y: ie.toHandle.y }, te) : P,
      toHandle: ie.toHandle,
      toPosition: W && ie.toHandle ? ie.toHandle.position : Wa[O.position],
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
        w?.(le, fe), s && b?.(le, fe);
      }
      h(), cancelAnimationFrame(M), $ = !1, W = !1, F = null, H = null, _.removeEventListener("mousemove", X), _.removeEventListener("mouseup", de), _.removeEventListener("touchmove", X), _.removeEventListener("touchend", de);
    }
  }
  _.addEventListener("mousemove", X), _.addEventListener("mouseup", de), _.addEventListener("touchmove", X), _.addEventListener("touchend", de);
}
function Qu(e, { handle: t, connectionMode: n, fromNodeId: i, fromHandleId: r, fromType: s, doc: a, lib: c, flowId: u, isValidConnection: l = Ju, nodeLookup: d }) {
  const f = s === "target", p = t ? a.querySelector(`.${c}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: h, y: m } = Qe(e), v = a.elementFromPoint(h, m), w = v?.classList.contains(`${c}-flow__handle`) ? v : p, y = {
    handleDomNode: w,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (w) {
    const b = Gu(void 0, w), g = w.getAttribute("data-nodeid"), x = w.getAttribute("data-handleid"), N = w.classList.contains("connectable"), j = w.classList.contains("connectableend");
    if (!g || !b)
      return y;
    const S = {
      source: f ? g : i,
      sourceHandle: f ? x : r,
      target: f ? i : g,
      targetHandle: f ? r : x
    };
    y.connection = S;
    const _ = N && j && (n === en.Strict ? f && b === "source" || !f && b === "target" : g !== i || x !== r);
    y.isValid = _ && l(S), y.toHandle = Zu(g, b, x, d, n, !0);
  }
  return y;
}
const Xo = {
  onPointerDown: Uw,
  isValid: Qu
};
function Zw({ domNode: e, panZoom: t, getTransform: n, getViewScale: i }) {
  const r = He(e);
  function s({ translateExtent: c, width: u, height: l, zoomStep: d = 1, pannable: f = !0, zoomable: p = !0, inversePan: h = !1 }) {
    const m = (g) => {
      if (g.sourceEvent.type !== "wheel" || !t)
        return;
      const x = n(), N = g.sourceEvent.ctrlKey && qn() ? 10 : 1, j = -g.sourceEvent.deltaY * (g.sourceEvent.deltaMode === 1 ? 0.05 : g.sourceEvent.deltaMode ? 1 : 2e-3) * d, S = x[2] * Math.pow(2, j * N);
      t.scaleTo(S);
    };
    let v = [0, 0];
    const w = (g) => {
      (g.sourceEvent.type === "mousedown" || g.sourceEvent.type === "touchstart") && (v = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ]);
    }, y = (g) => {
      const x = n();
      if (g.sourceEvent.type !== "mousemove" && g.sourceEvent.type !== "touchmove" || !t)
        return;
      const N = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ], j = [N[0] - v[0], N[1] - v[1]];
      v = N;
      const S = i() * Math.max(x[2], Math.log(x[2])) * (h ? -1 : 1), I = {
        x: x[0] - j[0] * S,
        y: x[1] - j[1] * S
      }, _ = [
        [0, 0],
        [u, l]
      ];
      t.setViewportConstrained({
        x: I.x,
        y: I.y,
        zoom: x[2]
      }, _, c);
    }, b = ku().on("start", w).on("zoom", f ? y : null).on("zoom.wheel", p ? m : null);
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
const vr = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), vo = ({ x: e, y: t, zoom: n }) => yr.translate(e, t).scale(n), Bt = (e, t) => e.target.closest(`.${t}`), ed = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Gw = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, bo = (e, t = 0, n = Gw, i = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || i(), r ? e.transition().duration(t).ease(n).on("end", i) : e;
}, td = (e) => {
  const t = e.ctrlKey && qn() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Jw({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: i, panOnScrollMode: r, panOnScrollSpeed: s, zoomOnPinch: a, onPanZoomStart: c, onPanZoom: u, onPanZoomEnd: l }) {
  return (d) => {
    if (Bt(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && a) {
      const w = Ze(d), y = td(d), b = f * Math.pow(2, y);
      i.scaleTo(n, b, w, d);
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
    const v = vr(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(d, v), e.panScrollTimeout = setTimeout(() => {
      l?.(d, v), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(d, v));
  };
}
function Qw({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(i, r) {
    const s = i.type === "wheel", a = !t && s && !i.ctrlKey, c = Bt(i, e);
    if (i.ctrlKey && s && c && i.preventDefault(), a || c)
      return null;
    i.preventDefault(), n.call(this, i, r);
  };
}
function ev({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (i) => {
    if (i.sourceEvent?.internal)
      return;
    const r = vr(i.transform);
    e.mouseButton = i.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, i.sourceEvent?.type === "mousedown" && t(!0), n && n?.(i.sourceEvent, r);
  };
}
function tv({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: i, onPanZoom: r }) {
  return (s) => {
    e.usedRightMouseButton = !!(n && ed(t, e.mouseButton ?? 0)), s.sourceEvent?.sync || i([s.transform.x, s.transform.y, s.transform.k]), r && !s.sourceEvent?.internal && r?.(s.sourceEvent, vr(s.transform));
  };
}
function nv({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: i, onPanZoomEnd: r, onPaneContextMenu: s }) {
  return (a) => {
    if (!a.sourceEvent?.internal && (e.isZoomingOrPanning = !1, s && ed(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && a.sourceEvent && s(a.sourceEvent), e.usedRightMouseButton = !1, i(!1), r)) {
      const c = vr(a.transform);
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
function iv({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: i, panOnScroll: r, zoomOnDoubleClick: s, userSelectionActive: a, noWheelClassName: c, noPanClassName: u, lib: l, connectionInProgress: d }) {
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
function rv({ domNode: e, minZoom: t, maxZoom: n, translateExtent: i, viewport: r, onPanZoom: s, onPanZoomStart: a, onPanZoomEnd: c, onDraggingChange: u }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = ku().scaleExtent([t, n]).translateExtent(i), p = He(e).call(f);
  b({
    x: r.x,
    y: r.y,
    zoom: tn(r.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], i);
  const h = p.on("wheel.zoom"), m = p.on("dblclick.zoom");
  f.wheelDelta(td);
  async function v(k, T) {
    return p ? new Promise((z) => {
      f?.interpolate(T?.interpolate === "linear" ? _n : zi).transform(bo(p, T?.duration, T?.ease, () => z(!0)), k);
    }) : !1;
  }
  function w({ noWheelClassName: k, noPanClassName: T, onPaneContextMenu: z, userSelectionActive: C, panOnScroll: A, panOnDrag: E, panOnScrollMode: D, panOnScrollSpeed: P, preventScrolling: $, zoomOnPinch: F, zoomOnScroll: W, zoomOnDoubleClick: H, zoomActivationKeyPressed: U, lib: O, onTransformChange: q, connectionInProgress: J, paneClickDistance: G, selectionOnDrag: R }) {
    C && !l.isZoomingOrPanning && y();
    const X = A && !U && !C;
    f.clickDistance(R ? 1 / 0 : !Je(G) || G < 0 ? 0 : G);
    const de = X ? Jw({
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
    }) : Qw({
      noWheelClassName: k,
      preventScrolling: $,
      d3ZoomHandler: h
    });
    p.on("wheel.zoom", de, { passive: !1 });
    const le = ev({
      zoomPanValues: l,
      onDraggingChange: u,
      onPanZoomStart: a
    });
    f.on("start", le);
    const te = tv({
      zoomPanValues: l,
      panOnDrag: E,
      onPaneContextMenu: !!z,
      onPanZoom: s,
      onTransformChange: q
    });
    f.on("zoom", te);
    const ie = nv({
      zoomPanValues: l,
      panOnDrag: E,
      panOnScroll: A,
      onPaneContextMenu: z,
      onPanZoomEnd: c,
      onDraggingChange: u
    });
    f.on("end", ie);
    const fe = iv({
      zoomActivationKeyPressed: U,
      panOnDrag: E,
      zoomOnScroll: W,
      panOnScroll: A,
      zoomOnDoubleClick: H,
      zoomOnPinch: F,
      userSelectionActive: C,
      noPanClassName: T,
      noWheelClassName: k,
      lib: O,
      connectionInProgress: J
    });
    f.filter(fe), H ? p.on("dblclick.zoom", m) : p.on("dblclick.zoom", null);
  }
  function y() {
    f.on("zoom", null);
  }
  async function b(k, T, z) {
    const C = vo(k), A = f?.constrain()(C, T, z);
    return A && await v(A), A;
  }
  async function g(k, T) {
    const z = vo(k);
    return await v(z, T), z;
  }
  function x(k) {
    if (p) {
      const T = vo(k), z = p.property("__zoom");
      (z.k !== k.zoom || z.x !== k.x || z.y !== k.y) && f?.transform(p, T, null, { sync: !0 });
    }
  }
  function N() {
    const k = p ? Cu(p.node()) : { x: 0, y: 0, k: 1 };
    return { x: k.x, y: k.y, zoom: k.k };
  }
  async function j(k, T) {
    return p ? new Promise((z) => {
      f?.interpolate(T?.interpolate === "linear" ? _n : zi).scaleTo(bo(p, T?.duration, T?.ease, () => z(!0)), k);
    }) : !1;
  }
  async function S(k, T) {
    return p ? new Promise((z) => {
      f?.interpolate(T?.interpolate === "linear" ? _n : zi).scaleBy(bo(p, T?.duration, T?.ease, () => z(!0)), k);
    }) : !1;
  }
  function I(k) {
    f?.scaleExtent(k);
  }
  function _(k) {
    f?.translateExtent(k);
  }
  function M(k) {
    const T = !Je(k) || k < 0 ? 0 : k;
    f?.clickDistance(T);
  }
  return {
    update: w,
    destroy: y,
    setViewport: g,
    setViewportConstrained: b,
    getViewport: N,
    scaleTo: j,
    scaleBy: S,
    setScaleExtent: I,
    setTranslateExtent: _,
    syncViewport: x,
    setClickDistance: M
  };
}
var on;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(on || (on = {}));
function ov({ width: e, prevWidth: t, height: n, prevHeight: i, affectsX: r, affectsY: s }) {
  const a = e - t, c = n - i, u = [a > 0 ? 1 : a < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return a && r && (u[0] = u[0] * -1), c && s && (u[1] = u[1] * -1), u;
}
function tc(e) {
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
function nc(e, t) {
  return e ? !t : t;
}
function sv(e, t, n, i, r, s, a, c) {
  let { affectsX: u, affectsY: l } = t;
  const { isHorizontal: d, isVertical: f } = t, p = d && f, { xSnapped: h, ySnapped: m } = n, { minWidth: v, maxWidth: w, minHeight: y, maxHeight: b } = i, { x: g, y: x, width: N, height: j, aspectRatio: S } = e;
  let I = Math.floor(d ? h - e.pointerX : 0), _ = Math.floor(f ? m - e.pointerY : 0);
  const M = N + (u ? -I : I), k = j + (l ? -_ : _), T = -s[0] * N, z = -s[1] * j;
  let C = Ai(M, v, w), A = Ai(k, y, b);
  if (a) {
    let P = 0, $ = 0;
    u && I < 0 ? P = gt(g + I + T, a[0][0]) : !u && I > 0 && (P = mt(g + M + T, a[1][0])), l && _ < 0 ? $ = gt(x + _ + z, a[0][1]) : !l && _ > 0 && ($ = mt(x + k + z, a[1][1])), C = Math.max(C, P), A = Math.max(A, $);
  }
  if (c) {
    let P = 0, $ = 0;
    u && I > 0 ? P = mt(g + I, c[0][0]) : !u && I < 0 && (P = gt(g + M, c[1][0])), l && _ > 0 ? $ = mt(x + _, c[0][1]) : !l && _ < 0 && ($ = gt(x + k, c[1][1])), C = Math.max(C, P), A = Math.max(A, $);
  }
  if (r) {
    if (d) {
      const P = Ai(M / S, y, b) * S;
      if (C = Math.max(C, P), a) {
        let $ = 0;
        !u && !l || u && !l && p ? $ = mt(x + z + M / S, a[1][1]) * S : $ = gt(x + z + (u ? I : -I) / S, a[0][1]) * S, C = Math.max(C, $);
      }
      if (c) {
        let $ = 0;
        !u && !l || u && !l && p ? $ = gt(x + M / S, c[1][1]) * S : $ = mt(x + (u ? I : -I) / S, c[0][1]) * S, C = Math.max(C, $);
      }
    }
    if (f) {
      const P = Ai(k * S, v, w) / S;
      if (A = Math.max(A, P), a) {
        let $ = 0;
        !u && !l || l && !u && p ? $ = mt(g + k * S + T, a[1][0]) / S : $ = gt(g + (l ? _ : -_) * S + T, a[0][0]) / S, A = Math.max(A, $);
      }
      if (c) {
        let $ = 0;
        !u && !l || l && !u && p ? $ = gt(g + k * S, c[1][0]) / S : $ = mt(g + (l ? _ : -_) * S, c[0][0]) / S, A = Math.max(A, $);
      }
    }
  }
  _ = _ + (_ < 0 ? A : -A), I = I + (I < 0 ? C : -C), r && (p ? M > k * S ? _ = (nc(u, l) ? -I : I) / S : I = (nc(u, l) ? -_ : _) * S : d ? (_ = I / S, l = u) : (I = _ * S, u = l));
  const E = u ? g + I : g, D = l ? x + _ : x;
  return {
    width: N + (u ? -I : I),
    height: j + (l ? -_ : _),
    x: s[0] * I * (u ? -1 : 1) + E,
    y: s[1] * _ * (l ? -1 : 1) + D
  };
}
const nd = { width: 0, height: 0, x: 0, y: 0 }, av = {
  ...nd,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function cv(e, t, n) {
  const i = t.position.x + e.position.x, r = t.position.y + e.position.y, s = e.measured.width ?? 0, a = e.measured.height ?? 0, c = n[0] * s, u = n[1] * a;
  return [
    [i - c, r - u],
    [i + s - c, r + a - u]
  ];
}
function lv({ domNode: e, nodeId: t, getStoreItems: n, onChange: i, onEnd: r }) {
  const s = He(e);
  let a = {
    controlDirection: tc("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: l, boundaries: d, keepAspectRatio: f, resizeDirection: p, onResizeStart: h, onResize: m, onResizeEnd: v, shouldResize: w }) {
    let y = { ...nd }, b = { ...av };
    a = {
      boundaries: d,
      resizeDirection: p,
      keepAspectRatio: f,
      controlDirection: tc(l)
    };
    let g, x = null, N = [], j, S, I, _ = !1;
    const M = du().on("start", (k) => {
      const { nodeLookup: T, transform: z, snapGrid: C, snapToGrid: A, nodeOrigin: E, paneDomNode: D } = n();
      if (g = T.get(t), !g)
        return;
      x = D?.getBoundingClientRect() ?? null;
      const { xSnapped: P, ySnapped: $ } = Dn(k.sourceEvent, {
        transform: z,
        snapGrid: C,
        snapToGrid: A,
        containerBounds: x
      });
      y = {
        width: g.measured.width ?? 0,
        height: g.measured.height ?? 0,
        x: g.position.x ?? 0,
        y: g.position.y ?? 0
      }, b = {
        ...y,
        pointerX: P,
        pointerY: $,
        aspectRatio: y.width / y.height
      }, j = void 0, S = Pt(g.extent) ? g.extent : void 0, g.parentId && (g.extent === "parent" || g.expandParent) && (j = T.get(g.parentId)), j && g.extent === "parent" && (S = [
        [0, 0],
        [j.measured.width, j.measured.height]
      ]), N = [], I = void 0;
      for (const [F, W] of T)
        if (W.parentId === t && (N.push({
          id: F,
          position: { ...W.position },
          extent: W.extent
        }), W.extent === "parent" || W.expandParent)) {
          const H = cv(W, g, W.origin ?? E);
          I ? I = [
            [Math.min(H[0][0], I[0][0]), Math.min(H[0][1], I[0][1])],
            [Math.max(H[1][0], I[1][0]), Math.max(H[1][1], I[1][1])]
          ] : I = H;
        }
      h?.(k, { ...y });
    }).on("drag", (k) => {
      const { transform: T, snapGrid: z, snapToGrid: C, nodeOrigin: A } = n(), E = Dn(k.sourceEvent, {
        transform: T,
        snapGrid: z,
        snapToGrid: C,
        containerBounds: x
      }), D = [];
      if (!g)
        return;
      const { x: P, y: $, width: F, height: W } = y, H = {}, U = g.origin ?? A, { width: O, height: q, x: J, y: G } = sv(b, a.controlDirection, E, a.boundaries, a.keepAspectRatio, U, S, I), R = O !== F, X = q !== W, de = J !== P && R, le = G !== $ && X;
      if (!de && !le && !R && !X)
        return;
      if ((de || le || U[0] === 1 || U[1] === 1) && (H.x = de ? J : y.x, H.y = le ? G : y.y, y.x = H.x, y.y = H.y, N.length > 0)) {
        const V = J - P, ne = G - $;
        for (const ge of N)
          ge.position = {
            x: ge.position.x - V + U[0] * (O - F),
            y: ge.position.y - ne + U[1] * (q - W)
          }, D.push(ge);
      }
      if ((R || X) && (H.width = R && (!a.resizeDirection || a.resizeDirection === "horizontal") ? O : y.width, H.height = X && (!a.resizeDirection || a.resizeDirection === "vertical") ? q : y.height, y.width = H.width, y.height = H.height), j && g.expandParent) {
        const V = U[0] * (H.width ?? 0);
        H.x && H.x < V && (y.x = V, b.x = b.x - (H.x - V));
        const ne = U[1] * (H.height ?? 0);
        H.y && H.y < ne && (y.y = ne, b.y = b.y - (H.y - ne));
      }
      const te = ov({
        width: y.width,
        prevWidth: F,
        height: y.height,
        prevHeight: W,
        affectsX: a.controlDirection.affectsX,
        affectsY: a.controlDirection.affectsY
      }), ie = { ...y, direction: te };
      w?.(k, ie) !== !1 && (_ = !0, m?.(k, ie), i(H, D));
    }).on("end", (k) => {
      _ && (v?.(k, { ...y }), r?.({ ...y }), _ = !1);
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
var ic;
function uv() {
  if (ic) return Co;
  ic = 1;
  var e = Fe;
  function t(f, p) {
    return f === p && (f !== 0 || 1 / f === 1 / p) || f !== f && p !== p;
  }
  var n = typeof Object.is == "function" ? Object.is : t, i = e.useState, r = e.useEffect, s = e.useLayoutEffect, a = e.useDebugValue;
  function c(f, p) {
    var h = p(), m = i({ inst: { value: h, getSnapshot: p } }), v = m[0].inst, w = m[1];
    return s(
      function() {
        v.value = h, v.getSnapshot = p, u(v) && w({ inst: v });
      },
      [f, h, p]
    ), r(
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
  return Co.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : d, Co;
}
var rc;
function dv() {
  return rc || (rc = 1, So.exports = uv()), So.exports;
}
var oc;
function fv() {
  if (oc) return No;
  oc = 1;
  var e = Fe, t = dv();
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
        function y(j) {
          if (!b) {
            if (b = !0, g = j, j = p(j), h !== void 0 && v.hasValue) {
              var S = v.value;
              if (h(S, j))
                return x = S;
            }
            return x = j;
          }
          if (S = x, i(g, j)) return S;
          var I = p(j);
          return h !== void 0 && h(S, I) ? (g = j, S) : (g = j, x = I);
        }
        var b = !1, g, x, N = f === void 0 ? null : f;
        return [
          function() {
            return y(d());
          },
          N === null ? void 0 : function() {
            return y(N());
          }
        ];
      },
      [d, f, p, h]
    );
    var w = r(l, m[0], m[1]);
    return a(
      function() {
        v.hasValue = !0, v.value = w;
      },
      [w]
    ), u(w), w;
  }, No;
}
var sc;
function pv() {
  return sc || (sc = 1, jo.exports = fv()), jo.exports;
}
var hv = pv();
const gv = /* @__PURE__ */ Zf(hv), mv = {}, ac = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), i = (d, f) => {
    const p = typeof d == "function" ? d(t) : d;
    if (!Object.is(p, t)) {
      const h = t;
      t = f ?? (typeof p != "object" || p === null) ? p : Object.assign({}, t, p), n.forEach((m) => m(t, h));
    }
  }, r = () => t, u = { setState: i, getState: r, getInitialState: () => l, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (mv ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(i, r, u);
  return u;
}, yv = (e) => e ? ac(e) : ac, { useDebugValue: xv } = Fe, { useSyncExternalStoreWithSelector: wv } = gv, vv = (e) => e;
function id(e, t = vv, n) {
  const i = wv(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return xv(i), i;
}
const cc = (e, t) => {
  const n = yv(e), i = (r, s = t) => id(n, r, s);
  return Object.assign(i, n), i;
}, bv = (e, t) => e ? cc(e, t) : cc;
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
var lc;
function jv() {
  if (lc) return Pe;
  lc = 1;
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
var uc;
function Nv() {
  if (uc) return ko.exports;
  uc = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), ko.exports = jv(), ko.exports;
}
var Sv = Nv();
const br = ns(null), Cv = br.Provider, rd = Ke.error001("react");
function he(e, t) {
  const n = Un(br);
  if (n === null)
    throw new Error(rd);
  return id(n, e, t);
}
function je() {
  const e = Un(br);
  if (e === null)
    throw new Error(rd);
  return ue(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const dc = { display: "none" }, kv = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, od = "react-flow__node-desc", sd = "react-flow__edge-desc", Ev = "react-flow__aria-live", Iv = (e) => e.ariaLiveMessage, Av = (e) => e.ariaLabelConfig;
function _v({ rfId: e }) {
  const t = he(Iv);
  return o.jsx("div", { id: `${Ev}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: kv, children: t });
}
function Dv({ rfId: e, disableKeyboardA11y: t }) {
  const n = he(Av);
  return o.jsxs(o.Fragment, { children: [o.jsx("div", { id: `${od}-${e}`, style: dc, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), o.jsx("div", { id: `${sd}-${e}`, style: dc, children: n["edge.a11yDescription.default"] }), !t && o.jsx(_v, { rfId: e })] });
}
const jr = al(({ position: e = "top-left", children: t, className: n, style: i, ...r }, s) => {
  const a = `${e}`.split("-");
  return o.jsx("div", { className: _e(["react-flow__panel", n, ...a]), style: i, ref: s, ...r, children: t });
});
jr.displayName = "Panel";
function $v({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : o.jsx(jr, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: o.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const Tv = (e) => {
  const t = [], n = [];
  for (const [, i] of e.nodeLookup)
    i.selected && t.push(i.internals.userNode);
  for (const [, i] of e.edgeLookup)
    i.selected && n.push(i);
  return { selectedNodes: t, selectedEdges: n };
}, _i = (e) => e.id;
function Pv(e, t) {
  return ve(e.selectedNodes.map(_i), t.selectedNodes.map(_i)) && ve(e.selectedEdges.map(_i), t.selectedEdges.map(_i));
}
function Mv({ onSelectionChange: e }) {
  const t = je(), { selectedNodes: n, selectedEdges: i } = he(Tv, Pv);
  return ee(() => {
    const r = { nodes: n, edges: i };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((s) => s(r));
  }, [n, i, e]), null;
}
const Rv = (e) => !!e.onSelectionChangeHandlers;
function Lv({ onSelectionChange: e }) {
  const t = he(Rv);
  return e || t ? o.jsx(Mv, { onSelectionChange: e }) : null;
}
const ad = [0, 0], zv = { x: 0, y: 0, zoom: 1 }, Vv = [
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
], fc = [...Vv, "rfId"], Ov = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), pc = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: Bn,
  nodeOrigin: ad,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function Hv(e) {
  const { setNodes: t, setEdges: n, setMinZoom: i, setMaxZoom: r, setTranslateExtent: s, setNodeExtent: a, reset: c, setDefaultNodesAndEdges: u } = he(Ov, ve), l = je();
  ee(() => (u(e.defaultNodes, e.defaultEdges), () => {
    d.current = pc, c();
  }), []);
  const d = oe(pc);
  return ee(
    () => {
      for (const f of fc) {
        const p = e[f], h = d.current[f];
        p !== h && (typeof e[f] > "u" || (f === "nodes" ? t(p) : f === "edges" ? n(p) : f === "minZoom" ? i(p) : f === "maxZoom" ? r(p) : f === "translateExtent" ? s(p) : f === "nodeExtent" ? a(p) : f === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: jw(p) }) : f === "fitView" ? l.setState({ fitViewQueued: p }) : f === "fitViewOptions" ? l.setState({ fitViewOptions: p }) : l.setState({ [f]: p })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    fc.map((f) => e[f])
  ), null;
}
function hc() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function Wv(e) {
  const [t, n] = K(e === "system" ? null : e);
  return ee(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const i = hc(), r = () => n(i?.matches ? "dark" : "light");
    return r(), i?.addEventListener("change", r), () => {
      i?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : hc()?.matches ? "dark" : "light";
}
const gc = typeof document < "u" ? document : null;
function Yn(e = null, t = { target: gc, actInsideInputWithModifier: !0 }) {
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
    const u = t?.target ?? gc, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (h) => {
        if (r.current = h.ctrlKey || h.metaKey || h.shiftKey || h.altKey, (!r.current || r.current && !l) && Vu(h))
          return !1;
        const v = yc(h.code, c);
        if (s.current.add(h[v]), mc(a, s.current, !1)) {
          const w = h.composedPath?.()?.[0] || h.target, y = w?.nodeName === "BUTTON" || w?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !y) && h.preventDefault(), i(!0);
        }
      }, f = (h) => {
        const m = yc(h.code, c);
        mc(a, s.current, !0) ? (i(!1), s.current.clear()) : s.current.delete(h[m]), h.key === "Meta" && s.current.clear(), r.current = !1;
      }, p = () => {
        s.current.clear(), i(!1);
      };
      return u?.addEventListener("keydown", d), u?.addEventListener("keyup", f), window.addEventListener("blur", p), window.addEventListener("contextmenu", p), () => {
        u?.removeEventListener("keydown", d), u?.removeEventListener("keyup", f), window.removeEventListener("blur", p), window.removeEventListener("contextmenu", p);
      };
    }
  }, [e, i]), n;
}
function mc(e, t, n) {
  return e.filter((i) => n || i.length === t.size).some((i) => i.every((r) => t.has(r)));
}
function yc(e, t) {
  return t.includes(e) ? "code" : "key";
}
const Fv = () => {
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
function cd(e, t) {
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
      Bv(u, c);
    n.push(c);
  }
  return r.length && r.forEach((s) => {
    s.index !== void 0 ? n.splice(s.index, 0, { ...s.item }) : n.push({ ...s.item });
  }), n;
}
function Bv(e, t) {
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
function ld(e, t) {
  return cd(e, t);
}
function ud(e, t) {
  return cd(e, t);
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
function xc({ items: e = [], lookup: t }) {
  const n = [], i = new Map(e.map((r) => [r.id, r]));
  for (const [r, s] of e.entries()) {
    const a = t.get(s.id), c = a?.internals?.userNode ?? a;
    c !== void 0 && c !== s && n.push({ id: s.id, item: s, type: "replace" }), c === void 0 && n.push({ item: s, type: "add", index: r });
  }
  for (const [r] of t)
    i.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function wc(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const dd = Mu();
function fd(e, t, n = {}) {
  return Ew(e, t, {
    ...n,
    onError: n.onError ?? dd
  });
}
function Kv(e, t, n, i = { shouldReplaceId: !0 }) {
  return Iw(e, t, n, {
    ...i,
    onError: i.onError ?? dd
  });
}
const vc = (e) => pw(e), Xv = (e) => Du(e);
function pd(e) {
  return al(e);
}
const qv = typeof window < "u" ? $f : ee;
function bc(e) {
  const [t, n] = K(BigInt(0)), [i] = K(() => Yv(() => n((r) => r + BigInt(1))));
  return qv(() => {
    const r = i.get();
    r.length && (e(r), i.reset());
  }, [t]), i;
}
function Yv(e) {
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
const hd = ns(null);
function Uv({ children: e }) {
  const t = je(), n = se((c) => {
    const { nodes: u = [], setNodes: l, hasDefaultNodes: d, onNodesChange: f, nodeLookup: p, fitViewQueued: h, onNodesChangeMiddlewareMap: m } = t.getState();
    let v = u;
    for (const y of c)
      v = typeof y == "function" ? y(v) : y;
    let w = xc({
      items: v,
      lookup: p
    });
    for (const y of m.values())
      w = y(w);
    d && l(v), w.length > 0 ? f?.(w) : h && window.requestAnimationFrame(() => {
      const { fitViewQueued: y, nodes: b, setNodes: g } = t.getState();
      y && g(b);
    });
  }, []), i = bc(n), r = se((c) => {
    const { edges: u = [], setEdges: l, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: p } = t.getState();
    let h = u;
    for (const m of c)
      h = typeof m == "function" ? m(h) : m;
    d ? l(h) : f && f(xc({
      items: h,
      lookup: p
    }));
  }, []), s = bc(r), a = ue(() => ({ nodeQueue: i, edgeQueue: s }), []);
  return o.jsx(hd.Provider, { value: a, children: e });
}
function Zv() {
  const e = Un(hd);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Gv = (e) => !!e.panZoom;
function Ps() {
  const e = Fv(), t = je(), n = Zv(), i = he(Gv), r = ue(() => {
    const s = (f) => t.getState().nodeLookup.get(f), a = (f) => {
      n.nodeQueue.push(f);
    }, c = (f) => {
      n.edgeQueue.push(f);
    }, u = (f) => {
      const { nodeLookup: p, nodeOrigin: h } = t.getState(), m = vc(f) ? f : p.get(f.id), v = m.parentId ? Lu(m.position, m.measured, m.parentId, p, h) : m.position, w = {
        ...m,
        position: v,
        width: m.measured?.width ?? m.width,
        height: m.measured?.height ?? m.height
      };
      return nn(w);
    }, l = (f, p, h = { replace: !1 }) => {
      a((m) => m.map((v) => {
        if (v.id === f) {
          const w = typeof p == "function" ? p(v) : p;
          return h.replace && vc(w) ? w : { ...v, ...w };
        }
        return v;
      }));
    }, d = (f, p, h = { replace: !1 }) => {
      c((m) => m.map((v) => {
        if (v.id === f) {
          const w = typeof p == "function" ? p(v) : p;
          return h.replace && Xv(w) ? w : { ...v, ...w };
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
        const { nodes: f = [], edges: p = [], transform: h } = t.getState(), [m, v, w] = h;
        return {
          nodes: f.map((y) => ({ ...y })),
          edges: p.map((y) => ({ ...y })),
          viewport: {
            x: m,
            y: v,
            zoom: w
          }
        };
      },
      deleteElements: async ({ nodes: f = [], edges: p = [] }) => {
        const { nodes: h, edges: m, onNodesDelete: v, onEdgesDelete: w, triggerNodeChanges: y, triggerEdgeChanges: b, onDelete: g, onBeforeDelete: x } = t.getState(), { nodes: N, edges: j } = await xw({
          nodesToRemove: f,
          edgesToRemove: p,
          nodes: h,
          edges: m,
          onBeforeDelete: x
        }), S = j.length > 0, I = N.length > 0;
        if (S) {
          const _ = j.map(wc);
          w?.(j), b(_);
        }
        if (I) {
          const _ = N.map(wc);
          v?.(N), y(_);
        }
        return (I || S) && g?.({ nodes: N, edges: j }), { deletedNodes: N, deletedEdges: j };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, p = !0, h) => {
        const m = Ba(f), v = m ? f : u(f), w = h !== void 0;
        return v ? (h || t.getState().nodes).filter((y) => {
          const b = t.getState().nodeLookup.get(y.id);
          if (b && !m && (y.id === f.id || !b.internals.positionAbsolute))
            return !1;
          const g = nn(w ? y : b), x = Xn(g, v);
          return p && x > 0 || x >= g.width * g.height || x >= v.width * v.height;
        }) : [];
      },
      isNodeIntersecting: (f, p, h = !0) => {
        const v = Ba(f) ? f : u(f);
        if (!v)
          return !1;
        const w = Xn(v, p);
        return h && w > 0 || w >= p.width * p.height || w >= v.width * v.height;
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
        return hw(f, { nodeLookup: p, nodeOrigin: h });
      },
      getHandleConnections: ({ type: f, id: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}-${f}${p ? `-${p}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}${f ? p ? `-${f}-${p}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const p = t.getState().fitViewResolver ?? bw();
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
const jc = (e) => e.selected, Jv = typeof window < "u" ? window : void 0;
function Qv({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = je(), { deleteElements: i } = Ps(), r = Yn(e, { actInsideInputWithModifier: !1 }), s = Yn(t, { target: Jv });
  ee(() => {
    if (r) {
      const { edges: a, nodes: c } = n.getState();
      i({ nodes: c.filter(jc), edges: a.filter(jc) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), ee(() => {
    n.setState({ multiSelectionActive: s });
  }, [s]);
}
function e0(e) {
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
const Nr = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, t0 = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function n0({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: i = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: s = Et.Free, zoomOnDoubleClick: a = !0, panOnDrag: c = !0, defaultViewport: u, translateExtent: l, minZoom: d, maxZoom: f, zoomActivationKeyCode: p, preventScrolling: h = !0, children: m, noWheelClassName: v, noPanClassName: w, onViewportChange: y, isControlledViewport: b, paneClickDistance: g, selectionOnDrag: x }) {
  const N = je(), j = oe(null), { userSelectionActive: S, lib: I, connectionInProgress: _ } = he(t0, ve), M = Yn(p), k = oe();
  e0(j);
  const T = se((z) => {
    y?.({ x: z[0], y: z[1], zoom: z[2] }), b || N.setState({ transform: z });
  }, [y, b]);
  return ee(() => {
    if (j.current) {
      k.current = rv({
        domNode: j.current,
        minZoom: d,
        maxZoom: f,
        translateExtent: l,
        viewport: u,
        onDraggingChange: (E) => N.setState((D) => D.paneDragging === E ? D : { paneDragging: E }),
        onPanZoomStart: (E, D) => {
          const { onViewportChangeStart: P, onMoveStart: $ } = N.getState();
          $?.(E, D), P?.(D);
        },
        onPanZoom: (E, D) => {
          const { onViewportChange: P, onMove: $ } = N.getState();
          $?.(E, D), P?.(D);
        },
        onPanZoomEnd: (E, D) => {
          const { onViewportChangeEnd: P, onMoveEnd: $ } = N.getState();
          $?.(E, D), P?.(D);
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
      noPanClassName: w,
      userSelectionActive: S,
      noWheelClassName: v,
      lib: I,
      onTransformChange: T,
      connectionInProgress: _,
      selectionOnDrag: x,
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
    w,
    S,
    v,
    I,
    T,
    _,
    x,
    g
  ]), o.jsx("div", { className: "react-flow__renderer", ref: j, style: Nr, children: m });
}
const i0 = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function r0() {
  const { userSelectionActive: e, userSelectionRect: t } = he(i0, ve);
  return e && t ? o.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const Eo = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, o0 = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function s0({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Kn.Full, panOnDrag: i, autoPanOnSelection: r, paneClickDistance: s, selectionOnDrag: a, onSelectionStart: c, onSelectionEnd: u, onPaneClick: l, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: p, onPaneMouseMove: h, onPaneMouseLeave: m, children: v }) {
  const w = oe(0), y = je(), { userSelectionActive: b, elementsSelectable: g, dragging: x, connectionInProgress: N, panBy: j, autoPanSpeed: S } = he(o0, ve), I = g && (e || b), _ = oe(null), M = oe(), k = oe(/* @__PURE__ */ new Set()), T = oe(/* @__PURE__ */ new Set()), z = oe(!1), C = oe({ x: 0, y: 0 }), A = oe(!1), E = (R) => {
    if (z.current || N) {
      z.current = !1;
      return;
    }
    l?.(R), y.getState().resetSelectedElements(), y.setState({ nodesSelectionActive: !1 });
  }, D = (R) => {
    if (Array.isArray(i) && i?.includes(2)) {
      R.preventDefault();
      return;
    }
    d?.(R);
  }, P = f ? (R) => f(R) : void 0, $ = (R) => {
    z.current && (R.stopPropagation(), z.current = !1);
  }, F = (R) => {
    const { domNode: X, transform: de } = y.getState();
    if (M.current = X?.getBoundingClientRect(), !M.current)
      return;
    const le = R.target === _.current;
    if (!le && !!R.target.closest(".nokey") || !e || !(a && le || t) || R.button !== 0 || !R.isPrimary)
      return;
    R.target?.setPointerCapture?.(R.pointerId), z.current = !1;
    const { x: fe, y: V } = Qe(R.nativeEvent, M.current), ne = dn({ x: fe, y: V }, de);
    y.setState({
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
    const { userSelectionRect: de } = y.getState();
    if (!de)
      return;
    const { transform: le, nodeLookup: te, edgeLookup: ie, connectionLookup: fe, triggerNodeChanges: V, triggerEdgeChanges: ne, defaultEdgeOptions: ge } = y.getState(), ye = { x: de.startX, y: de.startY }, { x: Ie, y: De } = rn(ye, le), Ve = {
      startX: ye.x,
      startY: ye.y,
      x: R < Ie ? R : Ie,
      y: X < De ? X : De,
      width: Math.abs(R - Ie),
      height: Math.abs(X - De)
    }, at = k.current, Ye = T.current;
    k.current = new Set(Cs(te, Ve, le, n === Kn.Partial, !0).map((Me) => Me.id)), T.current = /* @__PURE__ */ new Set();
    const Ue = ge?.selectable ?? !0;
    for (const Me of k.current) {
      const Oe = fe.get(Me);
      if (Oe)
        for (const { edgeId: Ne } of Oe.values()) {
          const Xe = ie.get(Ne);
          Xe && (Xe.selectable ?? Ue) && T.current.add(Ne);
        }
    }
    if (!Ka(at, k.current)) {
      const Me = Kt(te, k.current, !0);
      V(Me);
    }
    if (!Ka(Ye, T.current)) {
      const Me = Kt(ie, T.current);
      ne(Me);
    }
    y.setState({
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
        w.current = requestAnimationFrame(H);
        return;
      }
      const { x: le, y: te } = C.current;
      W(le, te), w.current = requestAnimationFrame(H);
    });
  }
  const U = () => {
    cancelAnimationFrame(w.current), w.current = 0, A.current = !1;
  };
  ee(() => () => U(), []);
  const O = (R) => {
    const { userSelectionRect: X, transform: de, resetSelectedElements: le } = y.getState();
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
    R.button === 0 && (R.target?.releasePointerCapture?.(R.pointerId), !b && R.target === _.current && y.getState().userSelectionRect && E?.(R), y.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), z.current && (u?.(R), y.setState({
      nodesSelectionActive: k.current.size > 0
    })), U());
  }, J = (R) => {
    R.target?.releasePointerCapture?.(R.pointerId), U();
  }, G = i === !0 || Array.isArray(i) && i.includes(0);
  return o.jsxs("div", { className: _e(["react-flow__pane", { draggable: G, dragging: x, selection: e }]), onClick: I ? void 0 : Eo(E, _), onContextMenu: Eo(D, _), onWheel: Eo(P, _), onPointerEnter: I ? void 0 : p, onPointerMove: I ? O : h, onPointerUp: I ? q : void 0, onPointerCancel: I ? J : void 0, onPointerDownCapture: I ? F : void 0, onClickCapture: I ? $ : void 0, onPointerLeave: m, ref: _, style: Nr, children: [v, o.jsx(r0, {})] });
}
function qo({ id: e, store: t, unselect: n = !1, nodeRef: i }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: s, multiSelectionActive: a, nodeLookup: c, onError: u } = t.getState(), l = c.get(e);
  if (!l) {
    u?.("012", Ke.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && a) && (s({ nodes: [l], edges: [] }), requestAnimationFrame(() => i?.current?.blur())) : r([e]);
}
function gd({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: i, nodeId: r, isSelectable: s, nodeClickDistance: a }) {
  const c = je(), [u, l] = K(!1), d = oe();
  return ee(() => {
    d.current = Bw({
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
const a0 = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function md() {
  const e = je();
  return se((n) => {
    const { nodeExtent: i, snapToGrid: r, snapGrid: s, nodesDraggable: a, onError: c, updateNodePositions: u, nodeLookup: l, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), p = a0(a), h = r ? s[0] : 5, m = r ? s[1] : 5, v = n.direction.x * h * n.factor, w = n.direction.y * m * n.factor;
    for (const [, y] of l) {
      if (!p(y))
        continue;
      let b = {
        x: y.internals.positionAbsolute.x + v,
        y: y.internals.positionAbsolute.y + w
      };
      r && (b = ii(b, s));
      const { position: g, positionAbsolute: x } = $u({
        nodeId: y.id,
        nextPosition: b,
        nodeLookup: l,
        nodeExtent: i,
        nodeOrigin: d,
        onError: c
      });
      y.position = g, y.internals.positionAbsolute = x, f.set(y.id, y);
    }
    u(f);
  }, []);
}
const Ms = ns(null), c0 = Ms.Provider;
Ms.Consumer;
const yd = () => Un(Ms), l0 = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), u0 = (e, t, n) => (i) => {
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
function d0({ type: e = "source", position: t = ae.Top, isValidConnection: n, isConnectable: i = !0, isConnectableStart: r = !0, isConnectableEnd: s = !0, id: a, onConnect: c, children: u, className: l, onMouseDown: d, onTouchStart: f, ...p }, h) {
  const m = a || null, v = e === "target", w = je(), y = yd(), { connectOnClick: b, noPanClassName: g, rfId: x } = he(l0, ve), { connectingFrom: N, connectingTo: j, clickConnecting: S, isPossibleEndHandle: I, connectionInProcess: _, clickConnectionInProcess: M, valid: k } = he(u0(y, m, e), ve);
  y || w.getState().onError?.("010", Ke.error010());
  const T = (A) => {
    const { defaultEdgeOptions: E, onConnect: D, hasDefaultEdges: P } = w.getState(), $ = {
      ...E,
      ...A
    };
    if (P) {
      const { edges: F, setEdges: W, onError: H } = w.getState();
      W(fd($, F, { onError: H }));
    }
    D?.($), c?.($);
  }, z = (A) => {
    if (!y)
      return;
    const E = Ou(A.nativeEvent);
    if (r && (E && A.button === 0 || !E)) {
      const D = w.getState();
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
        nodeId: y,
        flowId: D.rfId,
        panBy: D.panBy,
        cancelConnection: D.cancelConnection,
        onConnectStart: D.onConnectStart,
        onConnectEnd: (...P) => w.getState().onConnectEnd?.(...P),
        updateConnection: D.updateConnection,
        onConnect: T,
        isValidConnection: n || ((...P) => w.getState().isValidConnection?.(...P) ?? !0),
        getTransform: () => w.getState().transform,
        getFromHandle: () => w.getState().connection.fromHandle,
        autoPanSpeed: D.autoPanSpeed,
        dragThreshold: D.connectionDragThreshold
      });
    }
    E ? d?.(A) : f?.(A);
  }, C = (A) => {
    const { onClickConnectStart: E, onClickConnectEnd: D, connectionClickStartHandle: P, connectionMode: $, isValidConnection: F, lib: W, rfId: H, nodeLookup: U, connection: O } = w.getState();
    if (!y || !P && !r)
      return;
    if (!P) {
      E?.(A.nativeEvent, { nodeId: y, handleId: m, handleType: e }), w.setState({ connectionClickStartHandle: { nodeId: y, type: e, id: m } });
      return;
    }
    const q = zu(A.target), J = n || F, { connection: G, isValid: R } = Xo.isValid(A.nativeEvent, {
      handle: {
        nodeId: y,
        id: m,
        type: e
      },
      connectionMode: $,
      fromNodeId: P.nodeId,
      fromHandleId: P.id || null,
      fromType: P.type,
      isValidConnection: J,
      flowId: H,
      doc: q,
      lib: W,
      nodeLookup: U
    });
    R && G && T(G);
    const X = structuredClone(O);
    delete X.inProgress, X.toPosition = X.toHandle ? X.toHandle.position : null, D?.(A, X), w.setState({ connectionClickStartHandle: null });
  };
  return o.jsx("div", { "data-handleid": m, "data-nodeid": y, "data-handlepos": t, "data-id": `${x}-${y}-${m}-${e}`, className: _e([
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
const sn = Ee(pd(d0));
function f0({ data: e, isConnectable: t, sourcePosition: n = ae.Bottom }) {
  return o.jsxs(o.Fragment, { children: [e?.label, o.jsx(sn, { type: "source", position: n, isConnectable: t })] });
}
function p0({ data: e, isConnectable: t, targetPosition: n = ae.Top, sourcePosition: i = ae.Bottom }) {
  return o.jsxs(o.Fragment, { children: [o.jsx(sn, { type: "target", position: n, isConnectable: t }), e?.label, o.jsx(sn, { type: "source", position: i, isConnectable: t })] });
}
function h0() {
  return null;
}
function g0({ data: e, isConnectable: t, targetPosition: n = ae.Top }) {
  return o.jsxs(o.Fragment, { children: [o.jsx(sn, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const or = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, Nc = {
  input: f0,
  default: p0,
  output: g0,
  group: h0
};
function m0(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const y0 = (e) => {
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
function x0({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const i = je(), { width: r, height: s, transformString: a, userSelectionActive: c } = he(y0, ve), u = md(), l = oe(null);
  ee(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !c && r !== null && s !== null;
  if (gd({
    nodeRef: l,
    disabled: !d
  }), !d)
    return null;
  const f = e ? (h) => {
    const m = i.getState().nodes.filter((v) => v.selected);
    e(h, m);
  } : void 0, p = (h) => {
    Object.prototype.hasOwnProperty.call(or, h.key) && (h.preventDefault(), u({
      direction: or[h.key],
      factor: h.shiftKey ? 4 : 1
    }));
  };
  return o.jsx("div", { className: _e(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: a
  }, children: o.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : p, style: {
    width: r,
    height: s
  } }) });
}
const Sc = typeof window < "u" ? window : void 0, w0 = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function xd({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: i, onPaneMouseLeave: r, onPaneContextMenu: s, onPaneScroll: a, paneClickDistance: c, deleteKeyCode: u, selectionKeyCode: l, selectionOnDrag: d, selectionMode: f, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: m, panActivationKeyCode: v, zoomActivationKeyCode: w, elementsSelectable: y, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: x, panOnScrollSpeed: N, panOnScrollMode: j, zoomOnDoubleClick: S, panOnDrag: I, autoPanOnSelection: _, defaultViewport: M, translateExtent: k, minZoom: T, maxZoom: z, preventScrolling: C, onSelectionContextMenu: A, noWheelClassName: E, noPanClassName: D, disableKeyboardA11y: P, onViewportChange: $, isControlledViewport: F }) {
  const { nodesSelectionActive: W, userSelectionActive: H } = he(w0, ve), U = Yn(l, { target: Sc }), O = Yn(v, { target: Sc }), q = O || I, J = O || x, G = d && q !== !0, R = U || H || G;
  return Qv({ deleteKeyCode: u, multiSelectionKeyCode: m }), o.jsx(n0, { onPaneContextMenu: s, elementsSelectable: y, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: J, panOnScrollSpeed: N, panOnScrollMode: j, zoomOnDoubleClick: S, panOnDrag: !U && q, defaultViewport: M, translateExtent: k, minZoom: T, maxZoom: z, zoomActivationKeyCode: w, preventScrolling: C, noWheelClassName: E, noPanClassName: D, onViewportChange: $, isControlledViewport: F, paneClickDistance: c, selectionOnDrag: G, children: o.jsxs(s0, { onSelectionStart: p, onSelectionEnd: h, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: i, onPaneMouseLeave: r, onPaneContextMenu: s, onPaneScroll: a, panOnDrag: q, autoPanOnSelection: _, isSelecting: !!R, selectionMode: f, selectionKeyPressed: U, paneClickDistance: c, selectionOnDrag: G, children: [e, W && o.jsx(x0, { onSelectionContextMenu: A, noPanClassName: D, disableKeyboardA11y: P })] }) });
}
xd.displayName = "FlowRenderer";
const v0 = Ee(xd), b0 = (e) => (t) => e ? Cs(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function j0(e) {
  return he(se(b0(e), [e]), ve);
}
const N0 = (e) => e.updateNodeInternals;
function S0() {
  const e = he(N0), [t] = K(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
function C0({ node: e, nodeType: t, hasDimensions: n, resizeObserver: i }) {
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
function k0({ id: e, onClick: t, onMouseEnter: n, onMouseMove: i, onMouseLeave: r, onContextMenu: s, onDoubleClick: a, nodesDraggable: c, elementsSelectable: u, nodesConnectable: l, nodesFocusable: d, resizeObserver: f, noDragClassName: p, noPanClassName: h, disableKeyboardA11y: m, rfId: v, nodeTypes: w, nodeClickDistance: y, onError: b }) {
  const { node: g, internals: x, isParent: N } = he((R) => {
    const X = R.nodeLookup.get(e), de = R.parentLookup.has(e);
    return {
      node: X,
      internals: X.internals,
      isParent: de
    };
  }, ve);
  let j = g.type || "default", S = w?.[j] || Nc[j];
  S === void 0 && (b?.("003", Ke.error003(j)), j = "default", S = w?.default || Nc.default);
  const I = !!(g.draggable || c && typeof g.draggable > "u"), _ = !!(g.selectable || u && typeof g.selectable > "u"), M = !!(g.connectable || l && typeof g.connectable > "u"), k = !!(g.focusable || d && typeof g.focusable > "u"), T = je(), z = Ru(g), C = C0({ node: g, nodeType: j, hasDimensions: z, resizeObserver: f }), A = gd({
    nodeRef: C,
    disabled: g.hidden || !I,
    noDragClassName: p,
    handleSelector: g.dragHandle,
    nodeId: e,
    isSelectable: _,
    nodeClickDistance: y
  }), E = md();
  if (g.hidden)
    return null;
  const D = ht(g), P = m0(g), $ = _ || I || t || n || i || r, F = n ? (R) => n(R, { ...x.userNode }) : void 0, W = i ? (R) => i(R, { ...x.userNode }) : void 0, H = r ? (R) => r(R, { ...x.userNode }) : void 0, U = s ? (R) => s(R, { ...x.userNode }) : void 0, O = a ? (R) => a(R, { ...x.userNode }) : void 0, q = (R) => {
    const { selectNodesOnDrag: X, nodeDragThreshold: de } = T.getState();
    _ && (!X || !I || de > 0) && qo({
      id: e,
      store: T,
      nodeRef: C
    }), t && t(R, { ...x.userNode });
  }, J = (R) => {
    if (!(Vu(R.nativeEvent) || m)) {
      if (Eu.includes(R.key) && _) {
        const X = R.key === "Escape";
        qo({
          id: e,
          store: T,
          unselect: X,
          nodeRef: C
        });
      } else if (I && g.selected && Object.prototype.hasOwnProperty.call(or, R.key)) {
        R.preventDefault();
        const { ariaLabelConfig: X } = T.getState();
        T.setState({
          ariaLiveMessage: X["node.a11yDescription.ariaLiveMessage"]({
            direction: R.key.replace("Arrow", "").toLowerCase(),
            x: ~~x.positionAbsolute.x,
            y: ~~x.positionAbsolute.y
          })
        }), E({
          direction: or[R.key],
          factor: R.shiftKey ? 4 : 1
        });
      }
    }
  }, G = () => {
    if (m || !C.current?.matches(":focus-visible"))
      return;
    const { transform: R, width: X, height: de, autoPanOnNodeFocus: le, setCenter: te } = T.getState();
    if (!le)
      return;
    Cs(/* @__PURE__ */ new Map([[e, g]]), { x: 0, y: 0, width: X, height: de }, R, !0).length > 0 || te(g.position.x + D.width / 2, g.position.y + D.height / 2, {
      zoom: R[2]
    });
  };
  return o.jsx("div", { className: _e([
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
    zIndex: x.z,
    transform: `translate(${x.positionAbsolute.x}px,${x.positionAbsolute.y}px)`,
    pointerEvents: $ ? "all" : "none",
    visibility: z ? "visible" : "hidden",
    ...g.style,
    ...P
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: F, onMouseMove: W, onMouseLeave: H, onContextMenu: U, onClick: q, onDoubleClick: O, onKeyDown: k ? J : void 0, tabIndex: k ? 0 : void 0, onFocus: k ? G : void 0, role: g.ariaRole ?? (k ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": m ? void 0 : `${od}-${v}`, "aria-label": g.ariaLabel, ...g.domAttributes, children: o.jsx(c0, { value: e, children: o.jsx(S, { id: e, data: g.data, type: j, positionAbsoluteX: x.positionAbsolute.x, positionAbsoluteY: x.positionAbsolute.y, selected: g.selected ?? !1, selectable: _, draggable: I, deletable: g.deletable ?? !0, isConnectable: M, sourcePosition: g.sourcePosition, targetPosition: g.targetPosition, dragging: A, dragHandle: g.dragHandle, zIndex: x.z, parentId: g.parentId, ...D }) }) });
}
var E0 = Ee(k0);
const I0 = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function wd(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: i, elementsSelectable: r, onError: s } = he(I0, ve), a = j0(e.onlyRenderVisibleElements), c = S0();
  return o.jsx("div", { className: "react-flow__nodes", style: Nr, children: a.map((u) => (
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
    o.jsx(E0, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: i, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: s }, u)
  )) });
}
wd.displayName = "NodeRenderer";
const A0 = Ee(wd);
function _0(e) {
  return he(se((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const i = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const s = n.nodeLookup.get(r.source), a = n.nodeLookup.get(r.target);
        s && a && Cw({
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
const D0 = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return o.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, $0 = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return o.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Cc = {
  [nr.Arrow]: D0,
  [nr.ArrowClosed]: $0
};
function T0(e) {
  const t = je();
  return ue(() => Object.prototype.hasOwnProperty.call(Cc, e) ? Cc[e] : (t.getState().onError?.("009", Ke.error009(e)), null), [e]);
}
const P0 = ({ id: e, type: t, color: n, width: i = 12.5, height: r = 12.5, markerUnits: s = "strokeWidth", strokeWidth: a, orient: c = "auto-start-reverse" }) => {
  const u = T0(t);
  return u ? o.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${i}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: s, orient: c, refX: "0", refY: "0", children: o.jsx(u, { color: n, strokeWidth: a }) }) : null;
}, vd = ({ defaultColor: e, rfId: t }) => {
  const n = he((s) => s.edges), i = he((s) => s.defaultEdgeOptions), r = ue(() => Tw(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: i?.markerStart,
    defaultMarkerEnd: i?.markerEnd
  }), [n, i, t, e]);
  return r.length ? o.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: o.jsx("defs", { children: r.map((s) => o.jsx(P0, { id: s.id, type: s.type, color: s.color, width: s.width, height: s.height, markerUnits: s.markerUnits, strokeWidth: s.strokeWidth, orient: s.orient }, s.id)) }) }) : null;
};
vd.displayName = "MarkerDefinitions";
var M0 = Ee(vd);
function bd({ x: e, y: t, label: n, labelStyle: i, labelShowBg: r = !0, labelBgStyle: s, labelBgPadding: a = [2, 4], labelBgBorderRadius: c = 2, children: u, className: l, ...d }) {
  const [f, p] = K({ x: 1, y: 0, width: 0, height: 0 }), h = _e(["react-flow__edge-textwrapper", l]), m = oe(null);
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
bd.displayName = "EdgeText";
const R0 = Ee(bd);
function ri({ path: e, labelX: t, labelY: n, label: i, labelStyle: r, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u, interactionWidth: l = 20, ...d }) {
  return o.jsxs(o.Fragment, { children: [o.jsx("path", { ...d, d: e, fill: "none", className: _e(["react-flow__edge-path", d.className]) }), l ? o.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, i && Je(t) && Je(n) ? o.jsx(R0, { x: t, y: n, label: i, labelStyle: r, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u }) : null] });
}
function kc({ pos: e, x1: t, y1: n, x2: i, y2: r }) {
  return e === ae.Left || e === ae.Right ? [0.5 * (t + i), n] : [t, 0.5 * (n + r)];
}
function jd({ sourceX: e, sourceY: t, sourcePosition: n = ae.Bottom, targetX: i, targetY: r, targetPosition: s = ae.Top }) {
  const [a, c] = kc({
    pos: n,
    x1: e,
    y1: t,
    x2: i,
    y2: r
  }), [u, l] = kc({
    pos: s,
    x1: i,
    y1: r,
    x2: e,
    y2: t
  }), [d, f, p, h] = Hu({
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
function Nd(e) {
  return Ee(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, sourcePosition: a, targetPosition: c, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: m, markerEnd: v, markerStart: w, interactionWidth: y }) => {
    const [b, g, x] = jd({
      sourceX: n,
      sourceY: i,
      sourcePosition: a,
      targetX: r,
      targetY: s,
      targetPosition: c
    }), N = e.isInternal ? void 0 : t;
    return o.jsx(ri, { id: N, path: b, labelX: g, labelY: x, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: m, markerEnd: v, markerStart: w, interactionWidth: y });
  });
}
const L0 = Nd({ isInternal: !1 }), Sd = Nd({ isInternal: !0 });
L0.displayName = "SimpleBezierEdge";
Sd.displayName = "SimpleBezierEdgeInternal";
function Cd(e) {
  return Ee(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, sourcePosition: h = ae.Bottom, targetPosition: m = ae.Top, markerEnd: v, markerStart: w, pathOptions: y, interactionWidth: b }) => {
    const [g, x, N] = rr({
      sourceX: n,
      sourceY: i,
      sourcePosition: h,
      targetX: r,
      targetY: s,
      targetPosition: m,
      borderRadius: y?.borderRadius,
      offset: y?.offset,
      stepPosition: y?.stepPosition
    }), j = e.isInternal ? void 0 : t;
    return o.jsx(ri, { id: j, path: g, labelX: x, labelY: N, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: v, markerStart: w, interactionWidth: b });
  });
}
const kd = Cd({ isInternal: !1 }), Ed = Cd({ isInternal: !0 });
kd.displayName = "SmoothStepEdge";
Ed.displayName = "SmoothStepEdgeInternal";
function Id(e) {
  return Ee(({ id: t, ...n }) => {
    const i = e.isInternal ? void 0 : t;
    return o.jsx(kd, { ...n, id: i, pathOptions: ue(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const z0 = Id({ isInternal: !1 }), Ad = Id({ isInternal: !0 });
z0.displayName = "StepEdge";
Ad.displayName = "StepEdgeInternal";
function _d(e) {
  return Ee(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: m, interactionWidth: v }) => {
    const [w, y, b] = Ku({ sourceX: n, sourceY: i, targetX: r, targetY: s }), g = e.isInternal ? void 0 : t;
    return o.jsx(ri, { id: g, path: w, labelX: y, labelY: b, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: m, interactionWidth: v });
  });
}
const V0 = _d({ isInternal: !1 }), Dd = _d({ isInternal: !0 });
V0.displayName = "StraightEdge";
Dd.displayName = "StraightEdgeInternal";
function $d(e) {
  return Ee(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, sourcePosition: a = ae.Bottom, targetPosition: c = ae.Top, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: m, markerEnd: v, markerStart: w, pathOptions: y, interactionWidth: b }) => {
    const [g, x, N] = Wu({
      sourceX: n,
      sourceY: i,
      sourcePosition: a,
      targetX: r,
      targetY: s,
      targetPosition: c,
      curvature: y?.curvature
    }), j = e.isInternal ? void 0 : t;
    return o.jsx(ri, { id: j, path: g, labelX: x, labelY: N, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: m, markerEnd: v, markerStart: w, interactionWidth: b });
  });
}
const O0 = $d({ isInternal: !1 }), Td = $d({ isInternal: !0 });
O0.displayName = "BezierEdge";
Td.displayName = "BezierEdgeInternal";
const Ec = {
  default: Td,
  straight: Dd,
  step: Ad,
  smoothstep: Ed,
  simplebezier: Sd
}, Ic = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, H0 = (e, t, n) => n === ae.Left ? e - t : n === ae.Right ? e + t : e, W0 = (e, t, n) => n === ae.Top ? e - t : n === ae.Bottom ? e + t : e, Ac = "react-flow__edgeupdater";
function _c({ position: e, centerX: t, centerY: n, radius: i = 10, onMouseDown: r, onMouseEnter: s, onMouseOut: a, type: c }) {
  return o.jsx("circle", { onMouseDown: r, onMouseEnter: s, onMouseOut: a, className: _e([Ac, `${Ac}-${c}`]), cx: H0(t, i, e), cy: W0(n, i, e), r: i, stroke: "transparent", fill: "transparent" });
}
function F0({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: i, sourceY: r, targetX: s, targetY: a, sourcePosition: c, targetPosition: u, onReconnect: l, onReconnectStart: d, onReconnectEnd: f, setReconnecting: p, setUpdateHover: h }) {
  const m = je(), v = (x, N) => {
    if (x.button !== 0)
      return;
    const { autoPanOnConnect: j, domNode: S, connectionMode: I, connectionRadius: _, lib: M, onConnectStart: k, cancelConnection: T, nodeLookup: z, rfId: C, panBy: A, updateConnection: E } = m.getState(), D = N.type === "target", P = (W, H) => {
      p(!1), f?.(W, n, N.type, H);
    }, $ = (W) => l?.(n, W), F = (W, H) => {
      p(!0), d?.(x, n, N.type), k?.(W, H);
    };
    Xo.onPointerDown(x.nativeEvent, {
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
      cancelConnection: T,
      panBy: A,
      isValidConnection: (...W) => m.getState().isValidConnection?.(...W) ?? !0,
      onConnect: $,
      onConnectStart: F,
      onConnectEnd: (...W) => m.getState().onConnectEnd?.(...W),
      onReconnectEnd: P,
      updateConnection: E,
      getTransform: () => m.getState().transform,
      getFromHandle: () => m.getState().connection.fromHandle,
      dragThreshold: m.getState().connectionDragThreshold,
      handleDomNode: x.currentTarget
    });
  }, w = (x) => v(x, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), y = (x) => v(x, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), b = () => h(!0), g = () => h(!1);
  return o.jsxs(o.Fragment, { children: [(e === !0 || e === "source") && o.jsx(_c, { position: c, centerX: i, centerY: r, radius: t, onMouseDown: w, onMouseEnter: b, onMouseOut: g, type: "source" }), (e === !0 || e === "target") && o.jsx(_c, { position: u, centerX: s, centerY: a, radius: t, onMouseDown: y, onMouseEnter: b, onMouseOut: g, type: "target" })] });
}
function B0({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: i, onClick: r, onDoubleClick: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, rfId: m, edgeTypes: v, noPanClassName: w, onError: y, disableKeyboardA11y: b }) {
  let g = he((te) => te.edgeLookup.get(e));
  const x = he((te) => te.defaultEdgeOptions);
  g = x ? { ...x, ...g } : g;
  let N = g.type || "default", j = v?.[N] || Ec[N];
  j === void 0 && (y?.("011", Ke.error011(N)), N = "default", j = v?.default || Ec.default);
  const S = !!(g.focusable || t && typeof g.focusable > "u"), I = typeof f < "u" && (g.reconnectable || n && typeof g.reconnectable > "u"), _ = !!(g.selectable || i && typeof g.selectable > "u"), M = oe(null), [k, T] = K(!1), [z, C] = K(!1), A = je(), { zIndex: E, sourceX: D, sourceY: P, targetX: $, targetY: F, sourcePosition: W, targetPosition: H } = he(se((te) => {
    const ie = te.nodeLookup.get(g.source), fe = te.nodeLookup.get(g.target);
    if (!ie || !fe)
      return {
        zIndex: g.zIndex,
        ...Ic
      };
    const V = $w({
      id: e,
      sourceNode: ie,
      targetNode: fe,
      sourceHandle: g.sourceHandle || null,
      targetHandle: g.targetHandle || null,
      connectionMode: te.connectionMode,
      onError: y
    });
    return {
      zIndex: Sw({
        selected: g.selected,
        zIndex: g.zIndex,
        sourceNode: ie,
        targetNode: fe,
        elevateOnSelect: te.elevateEdgesOnSelect,
        zIndexMode: te.zIndexMode
      }),
      ...V || Ic
    };
  }, [g.source, g.target, g.sourceHandle, g.targetHandle, g.selected, g.zIndex]), ve), U = ue(() => g.markerStart ? `url('#${Bo(g.markerStart, m)}')` : void 0, [g.markerStart, m]), O = ue(() => g.markerEnd ? `url('#${Bo(g.markerEnd, m)}')` : void 0, [g.markerEnd, m]);
  if (g.hidden || D === null || P === null || $ === null || F === null)
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
    if (!b && Eu.includes(te.key) && _) {
      const { unselectNodesAndEdges: ie, addSelectedEdges: fe } = A.getState();
      te.key === "Escape" ? (M.current?.blur(), ie({ edges: [g] })) : fe([e]);
    }
  };
  return o.jsx("svg", { style: { zIndex: E }, children: o.jsxs("g", { className: _e([
    "react-flow__edge",
    `react-flow__edge-${N}`,
    g.className,
    w,
    {
      selected: g.selected,
      animated: g.animated,
      inactive: !_ && !r,
      updating: k,
      selectable: _
    }
  ]), onClick: q, onDoubleClick: J, onContextMenu: G, onMouseEnter: R, onMouseMove: X, onMouseLeave: de, onKeyDown: S ? le : void 0, tabIndex: S ? 0 : void 0, role: g.ariaRole ?? (S ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": g.ariaLabel === null ? void 0 : g.ariaLabel || `Edge from ${g.source} to ${g.target}`, "aria-describedby": S ? `${sd}-${m}` : void 0, ref: M, ...g.domAttributes, children: [!z && o.jsx(j, { id: e, source: g.source, target: g.target, type: g.type, selected: g.selected, animated: g.animated, selectable: _, deletable: g.deletable ?? !0, label: g.label, labelStyle: g.labelStyle, labelShowBg: g.labelShowBg, labelBgStyle: g.labelBgStyle, labelBgPadding: g.labelBgPadding, labelBgBorderRadius: g.labelBgBorderRadius, sourceX: D, sourceY: P, targetX: $, targetY: F, sourcePosition: W, targetPosition: H, data: g.data, style: g.style, sourceHandleId: g.sourceHandle, targetHandleId: g.targetHandle, markerStart: U, markerEnd: O, pathOptions: "pathOptions" in g ? g.pathOptions : void 0, interactionWidth: g.interactionWidth }), I && o.jsx(F0, { edge: g, isReconnectable: I, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, sourceX: D, sourceY: P, targetX: $, targetY: F, sourcePosition: W, targetPosition: H, setUpdateHover: T, setReconnecting: C })] }) });
}
var K0 = Ee(B0);
const X0 = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function Pd({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: i, noPanClassName: r, onReconnect: s, onEdgeContextMenu: a, onEdgeMouseEnter: c, onEdgeMouseMove: u, onEdgeMouseLeave: l, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: p, onReconnectStart: h, onReconnectEnd: m, disableKeyboardA11y: v }) {
  const { edgesFocusable: w, edgesReconnectable: y, elementsSelectable: b, onError: g } = he(X0, ve), x = _0(t);
  return o.jsxs("div", { className: "react-flow__edges", children: [o.jsx(M0, { defaultColor: e, rfId: n }), x.map((N) => o.jsx(K0, { id: N, edgesFocusable: w, edgesReconnectable: y, elementsSelectable: b, noPanClassName: r, onReconnect: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, onClick: d, reconnectRadius: f, onDoubleClick: p, onReconnectStart: h, onReconnectEnd: m, rfId: n, onError: g, edgeTypes: i, disableKeyboardA11y: v }, N))] });
}
Pd.displayName = "EdgeRenderer";
const q0 = Ee(Pd), Y0 = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function U0({ children: e }) {
  const t = he(Y0);
  return o.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function Z0(e) {
  const t = Ps(), n = oe(!1);
  ee(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const G0 = (e) => e.panZoom?.syncViewport;
function J0(e) {
  const t = he(G0), n = je();
  return ee(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function Q0(e) {
  return e.connection.inProgress ? { ...e.connection, to: dn(e.connection.to, e.transform) } : { ...e.connection };
}
function eb(e) {
  return Q0;
}
function tb(e) {
  const t = eb();
  return he(t, ve);
}
const nb = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function ib({ containerStyle: e, style: t, type: n, component: i }) {
  const { nodesConnectable: r, width: s, height: a, isValid: c, inProgress: u } = he(nb, ve);
  return !(s && r && u) ? null : o.jsx("svg", { style: e, width: s, height: a, className: "react-flow__connectionline react-flow__container", children: o.jsx("g", { className: _e(["react-flow__connection", _u(c)]), children: o.jsx(Md, { style: t, type: n, CustomComponent: i, isValid: c }) }) });
}
const Md = ({ style: e, type: t = yt.Bezier, CustomComponent: n, isValid: i }) => {
  const { inProgress: r, from: s, fromNode: a, fromHandle: c, fromPosition: u, to: l, toNode: d, toHandle: f, toPosition: p, pointer: h } = tb();
  if (!r)
    return;
  if (n)
    return o.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: a, fromHandle: c, fromX: s.x, fromY: s.y, toX: l.x, toY: l.y, fromPosition: u, toPosition: p, connectionStatus: _u(i), toNode: d, toHandle: f, pointer: h });
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
      [m] = Wu(v);
      break;
    case yt.SimpleBezier:
      [m] = jd(v);
      break;
    case yt.Step:
      [m] = rr({
        ...v,
        borderRadius: 0
      });
      break;
    case yt.SmoothStep:
      [m] = rr(v);
      break;
    default:
      [m] = Ku(v);
  }
  return o.jsx("path", { d: m, fill: "none", className: "react-flow__connection-path", style: e });
};
Md.displayName = "ConnectionLine";
const rb = {};
function Dc(e = rb) {
  oe(e), je(), ee(() => {
  }, [e]);
}
function ob() {
  je(), oe(!1), ee(() => {
  }, []);
}
function Rd({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: i, onEdgeClick: r, onNodeDoubleClick: s, onEdgeDoubleClick: a, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: p, onSelectionEnd: h, connectionLineType: m, connectionLineStyle: v, connectionLineComponent: w, connectionLineContainerStyle: y, selectionKeyCode: b, selectionOnDrag: g, selectionMode: x, multiSelectionKeyCode: N, panActivationKeyCode: j, zoomActivationKeyCode: S, deleteKeyCode: I, onlyRenderVisibleElements: _, elementsSelectable: M, defaultViewport: k, translateExtent: T, minZoom: z, maxZoom: C, preventScrolling: A, defaultMarkerColor: E, zoomOnScroll: D, zoomOnPinch: P, panOnScroll: $, panOnScrollSpeed: F, panOnScrollMode: W, zoomOnDoubleClick: H, panOnDrag: U, autoPanOnSelection: O, onPaneClick: q, onPaneMouseEnter: J, onPaneMouseMove: G, onPaneMouseLeave: R, onPaneScroll: X, onPaneContextMenu: de, paneClickDistance: le, nodeClickDistance: te, onEdgeContextMenu: ie, onEdgeMouseEnter: fe, onEdgeMouseMove: V, onEdgeMouseLeave: ne, reconnectRadius: ge, onReconnect: ye, onReconnectStart: Ie, onReconnectEnd: De, noDragClassName: Ve, noWheelClassName: at, noPanClassName: Ye, disableKeyboardA11y: Ue, nodeExtent: Me, rfId: Oe, viewport: Ne, onViewportChange: Xe }) {
  return Dc(e), Dc(t), ob(), Z0(n), J0(Ne), o.jsx(v0, { onPaneClick: q, onPaneMouseEnter: J, onPaneMouseMove: G, onPaneMouseLeave: R, onPaneContextMenu: de, onPaneScroll: X, paneClickDistance: le, deleteKeyCode: I, selectionKeyCode: b, selectionOnDrag: g, selectionMode: x, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: N, panActivationKeyCode: j, zoomActivationKeyCode: S, elementsSelectable: M, zoomOnScroll: D, zoomOnPinch: P, zoomOnDoubleClick: H, panOnScroll: $, panOnScrollSpeed: F, panOnScrollMode: W, panOnDrag: U, autoPanOnSelection: O, defaultViewport: k, translateExtent: T, minZoom: z, maxZoom: C, onSelectionContextMenu: f, preventScrolling: A, noDragClassName: Ve, noWheelClassName: at, noPanClassName: Ye, disableKeyboardA11y: Ue, onViewportChange: Xe, isControlledViewport: !!Ne, children: o.jsxs(U0, { children: [o.jsx(q0, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: a, onReconnect: ye, onReconnectStart: Ie, onReconnectEnd: De, onlyRenderVisibleElements: _, onEdgeContextMenu: ie, onEdgeMouseEnter: fe, onEdgeMouseMove: V, onEdgeMouseLeave: ne, reconnectRadius: ge, defaultMarkerColor: E, noPanClassName: Ye, disableKeyboardA11y: Ue, rfId: Oe }), o.jsx(ib, { style: v, type: m, component: w, containerStyle: y }), o.jsx("div", { className: "react-flow__edgelabel-renderer" }), o.jsx(A0, { nodeTypes: e, onNodeClick: i, onNodeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, nodeClickDistance: te, onlyRenderVisibleElements: _, noPanClassName: Ye, noDragClassName: Ve, disableKeyboardA11y: Ue, nodeExtent: Me, rfId: Oe }), o.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Rd.displayName = "GraphView";
const sb = Ee(Rd), ab = Mu(), $c = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, width: r, height: s, fitView: a, fitViewOptions: c, minZoom: u = 0.5, maxZoom: l = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: p = "basic" } = {}) => {
  const h = /* @__PURE__ */ new Map(), m = /* @__PURE__ */ new Map(), v = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), y = i ?? t ?? [], b = n ?? e ?? [], g = d ?? [0, 0], x = f ?? Bn;
  Yu(v, w, y);
  const { nodesInitialized: N } = Ko(b, h, m, {
    nodeOrigin: g,
    nodeExtent: x,
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
    edges: y,
    edgeLookup: w,
    connectionLookup: v,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: i !== void 0,
    panZoom: null,
    minZoom: u,
    maxZoom: l,
    translateExtent: Bn,
    nodeExtent: x,
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
    connection: { ...Au },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: ab,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Iu,
    zIndexMode: p,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, cb = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, width: r, height: s, fitView: a, fitViewOptions: c, minZoom: u, maxZoom: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p }) => bv((h, m) => {
  async function v() {
    const { nodeLookup: w, panZoom: y, fitViewOptions: b, fitViewResolver: g, width: x, height: N, minZoom: j, maxZoom: S } = m();
    y && (await yw({
      nodes: w,
      width: x,
      height: N,
      panZoom: y,
      minZoom: j,
      maxZoom: S
    }, b), g?.resolve(!0), h({ fitViewResolver: null }));
  }
  return {
    ...$c({
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
    setNodes: (w) => {
      const { nodeLookup: y, parentLookup: b, nodeOrigin: g, elevateNodesOnSelect: x, fitViewQueued: N, zIndexMode: j, nodesSelectionActive: S } = m(), { nodesInitialized: I, hasSelectedNodes: _ } = Ko(w, y, b, {
        nodeOrigin: g,
        nodeExtent: f,
        elevateNodesOnSelect: x,
        checkEquality: !0,
        zIndexMode: j
      }), M = S && _;
      N && I ? (v(), h({
        nodes: w,
        nodesInitialized: I,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: M
      })) : h({ nodes: w, nodesInitialized: I, nodesSelectionActive: M });
    },
    setEdges: (w) => {
      const { connectionLookup: y, edgeLookup: b } = m();
      Yu(y, b, w), h({ edges: w });
    },
    setDefaultNodesAndEdges: (w, y) => {
      if (w) {
        const { setNodes: b } = m();
        b(w), h({ hasDefaultNodes: !0 });
      }
      if (y) {
        const { setEdges: b } = m();
        b(y), h({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (w) => {
      const { triggerNodeChanges: y, nodeLookup: b, parentLookup: g, domNode: x, nodeOrigin: N, nodeExtent: j, debug: S, fitViewQueued: I, zIndexMode: _ } = m(), { changes: M, updatedInternals: k } = Ow(w, b, g, x, N, j, _);
      k && (Rw(b, g, { nodeOrigin: N, nodeExtent: j, zIndexMode: _ }), I ? (v(), h({ fitViewQueued: !1, fitViewOptions: void 0 })) : h({}), M?.length > 0 && (S && console.log("React Flow: trigger node changes", M), y?.(M)));
    },
    updateNodePositions: (w, y = !1) => {
      const b = [];
      let g = [];
      const { nodeLookup: x, triggerNodeChanges: N, connection: j, updateConnection: S, onNodesChangeMiddlewareMap: I } = m();
      for (const [_, M] of w) {
        const k = x.get(_), T = !!(k?.expandParent && k?.parentId && M?.position), z = {
          id: _,
          type: "position",
          position: T ? {
            x: Math.max(0, M.position.x),
            y: Math.max(0, M.position.y)
          } : M.position,
          dragging: y
        };
        if (k && j.inProgress && j.fromNode.id === k.id) {
          const C = Mt(k, j.fromHandle, ae.Left, !0);
          S({ ...j, from: C });
        }
        T && k.parentId && b.push({
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
        const { parentLookup: _, nodeOrigin: M } = m(), k = Ts(b, x, _, M);
        g.push(...k);
      }
      for (const _ of I.values())
        g = _(g);
      N(g);
    },
    triggerNodeChanges: (w) => {
      const { onNodesChange: y, setNodes: b, nodes: g, hasDefaultNodes: x, debug: N } = m();
      if (w?.length) {
        if (x) {
          const j = ld(w, g);
          b(j);
        }
        N && console.log("React Flow: trigger node changes", w), y?.(w);
      }
    },
    triggerEdgeChanges: (w) => {
      const { onEdgesChange: y, setEdges: b, edges: g, hasDefaultEdges: x, debug: N } = m();
      if (w?.length) {
        if (x) {
          const j = ud(w, g);
          b(j);
        }
        N && console.log("React Flow: trigger edge changes", w), y?.(w);
      }
    },
    addSelectedNodes: (w) => {
      const { multiSelectionActive: y, edgeLookup: b, nodeLookup: g, triggerNodeChanges: x, triggerEdgeChanges: N } = m();
      if (y) {
        const j = w.map((S) => St(S, !0));
        x(j);
        return;
      }
      x(Kt(g, /* @__PURE__ */ new Set([...w]), !0)), N(Kt(b));
    },
    addSelectedEdges: (w) => {
      const { multiSelectionActive: y, edgeLookup: b, nodeLookup: g, triggerNodeChanges: x, triggerEdgeChanges: N } = m();
      if (y) {
        const j = w.map((S) => St(S, !0));
        N(j);
        return;
      }
      N(Kt(b, /* @__PURE__ */ new Set([...w]))), x(Kt(g, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: w, edges: y } = {}) => {
      const { edges: b, nodes: g, nodeLookup: x, triggerNodeChanges: N, triggerEdgeChanges: j } = m(), S = w || g, I = y || b, _ = [];
      for (const k of S) {
        if (!k.selected)
          continue;
        const T = x.get(k.id);
        T && (T.selected = !1), _.push(St(k.id, !1));
      }
      const M = [];
      for (const k of I)
        k.selected && M.push(St(k.id, !1));
      N(_), j(M);
    },
    setMinZoom: (w) => {
      const { panZoom: y, maxZoom: b } = m();
      y?.setScaleExtent([w, b]), h({ minZoom: w });
    },
    setMaxZoom: (w) => {
      const { panZoom: y, minZoom: b } = m();
      y?.setScaleExtent([b, w]), h({ maxZoom: w });
    },
    setTranslateExtent: (w) => {
      m().panZoom?.setTranslateExtent(w), h({ translateExtent: w });
    },
    resetSelectedElements: () => {
      const { edges: w, nodes: y, triggerNodeChanges: b, triggerEdgeChanges: g, elementsSelectable: x } = m();
      if (!x)
        return;
      const N = y.reduce((S, I) => I.selected ? [...S, St(I.id, !1)] : S, []), j = w.reduce((S, I) => I.selected ? [...S, St(I.id, !1)] : S, []);
      b(N), g(j);
    },
    setNodeExtent: (w) => {
      const { nodes: y, nodeLookup: b, parentLookup: g, nodeOrigin: x, elevateNodesOnSelect: N, nodeExtent: j, zIndexMode: S } = m();
      w[0][0] === j[0][0] && w[0][1] === j[0][1] && w[1][0] === j[1][0] && w[1][1] === j[1][1] || (Ko(y, b, g, {
        nodeOrigin: x,
        nodeExtent: w,
        elevateNodesOnSelect: N,
        checkEquality: !1,
        zIndexMode: S
      }), h({ nodeExtent: w }));
    },
    panBy: (w) => {
      const { transform: y, width: b, height: g, panZoom: x, translateExtent: N } = m();
      return Hw({ delta: w, panZoom: x, transform: y, translateExtent: N, width: b, height: g });
    },
    setCenter: async (w, y, b) => {
      const { width: g, height: x, maxZoom: N, panZoom: j } = m();
      if (!j)
        return !1;
      const S = typeof b?.zoom < "u" ? b.zoom : N;
      return await j.setViewport({
        x: g / 2 - w * S,
        y: x / 2 - y * S,
        zoom: S
      }, { duration: b?.duration, ease: b?.ease, interpolate: b?.interpolate }), !0;
    },
    cancelConnection: () => {
      h({
        connection: { ...Au }
      });
    },
    updateConnection: (w) => {
      h({ connection: w });
    },
    reset: () => h({ ...$c() })
  };
}, Object.is);
function lb({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: i, initialWidth: r, initialHeight: s, initialMinZoom: a, initialMaxZoom: c, initialFitViewOptions: u, fitView: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p, children: h }) {
  const [m] = K(() => cb({
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
  return o.jsx(Cv, { value: m, children: o.jsx(Uv, { children: h }) });
}
function ub({ children: e, nodes: t, edges: n, defaultNodes: i, defaultEdges: r, width: s, height: a, fitView: c, fitViewOptions: u, minZoom: l, maxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h }) {
  return Un(br) ? o.jsx(o.Fragment, { children: e }) : o.jsx(lb, { initialNodes: t, initialEdges: n, defaultNodes: i, defaultEdges: r, initialWidth: s, initialHeight: a, fitView: c, initialFitViewOptions: u, initialMinZoom: l, initialMaxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h, children: e });
}
const db = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function fb({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, className: r, nodeTypes: s, edgeTypes: a, onNodeClick: c, onEdgeClick: u, onInit: l, onMove: d, onMoveStart: f, onMoveEnd: p, onConnect: h, onConnectStart: m, onConnectEnd: v, onClickConnectStart: w, onClickConnectEnd: y, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: x, onNodeContextMenu: N, onNodeDoubleClick: j, onNodeDragStart: S, onNodeDrag: I, onNodeDragStop: _, onNodesDelete: M, onEdgesDelete: k, onDelete: T, onSelectionChange: z, onSelectionDragStart: C, onSelectionDrag: A, onSelectionDragStop: E, onSelectionContextMenu: D, onSelectionStart: P, onSelectionEnd: $, onBeforeDelete: F, connectionMode: W, connectionLineType: H = yt.Bezier, connectionLineStyle: U, connectionLineComponent: O, connectionLineContainerStyle: q, deleteKeyCode: J = "Backspace", selectionKeyCode: G = "Shift", selectionOnDrag: R = !1, selectionMode: X = Kn.Full, panActivationKeyCode: de = "Space", multiSelectionKeyCode: le = qn() ? "Meta" : "Control", zoomActivationKeyCode: te = qn() ? "Meta" : "Control", snapToGrid: ie, snapGrid: fe, onlyRenderVisibleElements: V = !1, selectNodesOnDrag: ne, nodesDraggable: ge, autoPanOnNodeFocus: ye, nodesConnectable: Ie, nodesFocusable: De, nodeOrigin: Ve = ad, edgesFocusable: at, edgesReconnectable: Ye, elementsSelectable: Ue = !0, defaultViewport: Me = zv, minZoom: Oe = 0.5, maxZoom: Ne = 2, translateExtent: Xe = Bn, preventScrolling: $e = !0, nodeExtent: Nt, defaultMarkerColor: pn = "#b1b1b7", zoomOnScroll: L = !0, zoomOnPinch: B = !0, panOnScroll: Y = !1, panOnScrollSpeed: re = 0.5, panOnScrollMode: Q = Et.Free, zoomOnDoubleClick: xe = !0, panOnDrag: pe = !0, onPaneClick: we, onPaneMouseEnter: Se, onPaneMouseMove: qe, onPaneMouseLeave: Te, onPaneScroll: kr, onPaneContextMenu: tt, paneClickDistance: Er = 1, nodeClickDistance: si = 0, children: Ir, onReconnect: Ar, onReconnectStart: _r, onReconnectEnd: Dr, onEdgeContextMenu: ct, onEdgeDoubleClick: $r, onEdgeMouseEnter: Vt, onEdgeMouseMove: Tr, onEdgeMouseLeave: Ot, reconnectRadius: Pr = 10, onNodesChange: ai, onEdgesChange: ci, noDragClassName: Mr = "nodrag", noWheelClassName: Ht = "nowheel", noPanClassName: li = "nopan", fitView: ui, fitViewOptions: di, connectOnClick: hn, attributionPosition: Rr, proOptions: Lr, defaultEdgeOptions: zr, elevateNodesOnSelect: Vr = !0, elevateEdgesOnSelect: Or = !1, disableKeyboardA11y: fi = !1, autoPanOnConnect: Hr, autoPanOnNodeDrag: Wr, autoPanOnSelection: Fr = !0, autoPanSpeed: Br, connectionRadius: Kr, isValidConnection: Xr, onError: qr, style: Yr, id: gn, nodeDragThreshold: pi, connectionDragThreshold: hi, viewport: gi, onViewportChange: Ur, width: mi, height: Zr, colorMode: Gr = "light", debug: Jr, onScroll: yi, ariaLabelConfig: Qr, zIndexMode: xi = "basic", ...eo }, to) {
  const mn = gn || "1", no = Wv(Gr), io = se((wi) => {
    wi.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), yi?.(wi);
  }, [yi]);
  return o.jsx("div", { "data-testid": "rf__wrapper", ...eo, onScroll: io, style: { ...Yr, ...db }, ref: to, className: _e(["react-flow", r, no]), id: gn, role: "application", children: o.jsxs(ub, { nodes: e, edges: t, width: mi, height: Zr, fitView: ui, fitViewOptions: di, minZoom: Oe, maxZoom: Ne, nodeOrigin: Ve, nodeExtent: Nt, zIndexMode: xi, children: [o.jsx(Hv, { nodes: e, edges: t, defaultNodes: n, defaultEdges: i, onConnect: h, onConnectStart: m, onConnectEnd: v, onClickConnectStart: w, onClickConnectEnd: y, nodesDraggable: ge, autoPanOnNodeFocus: ye, nodesConnectable: Ie, nodesFocusable: De, edgesFocusable: at, edgesReconnectable: Ye, elementsSelectable: Ue, elevateNodesOnSelect: Vr, elevateEdgesOnSelect: Or, minZoom: Oe, maxZoom: Ne, nodeExtent: Nt, onNodesChange: ai, onEdgesChange: ci, snapToGrid: ie, snapGrid: fe, connectionMode: W, translateExtent: Xe, connectOnClick: hn, defaultEdgeOptions: zr, fitView: ui, fitViewOptions: di, onNodesDelete: M, onEdgesDelete: k, onDelete: T, onNodeDragStart: S, onNodeDrag: I, onNodeDragStop: _, onSelectionDrag: A, onSelectionDragStart: C, onSelectionDragStop: E, onMove: d, onMoveStart: f, onMoveEnd: p, noPanClassName: li, nodeOrigin: Ve, rfId: mn, autoPanOnConnect: Hr, autoPanOnNodeDrag: Wr, autoPanSpeed: Br, onError: qr, connectionRadius: Kr, isValidConnection: Xr, selectNodesOnDrag: ne, nodeDragThreshold: pi, connectionDragThreshold: hi, onBeforeDelete: F, debug: Jr, ariaLabelConfig: Qr, zIndexMode: xi }), o.jsx(sb, { onInit: l, onNodeClick: c, onEdgeClick: u, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: x, onNodeContextMenu: N, onNodeDoubleClick: j, nodeTypes: s, edgeTypes: a, connectionLineType: H, connectionLineStyle: U, connectionLineComponent: O, connectionLineContainerStyle: q, selectionKeyCode: G, selectionOnDrag: R, selectionMode: X, deleteKeyCode: J, multiSelectionKeyCode: le, panActivationKeyCode: de, zoomActivationKeyCode: te, onlyRenderVisibleElements: V, defaultViewport: Me, translateExtent: Xe, minZoom: Oe, maxZoom: Ne, preventScrolling: $e, zoomOnScroll: L, zoomOnPinch: B, zoomOnDoubleClick: xe, panOnScroll: Y, panOnScrollSpeed: re, panOnScrollMode: Q, panOnDrag: pe, autoPanOnSelection: Fr, onPaneClick: we, onPaneMouseEnter: Se, onPaneMouseMove: qe, onPaneMouseLeave: Te, onPaneScroll: kr, onPaneContextMenu: tt, paneClickDistance: Er, nodeClickDistance: si, onSelectionContextMenu: D, onSelectionStart: P, onSelectionEnd: $, onReconnect: Ar, onReconnectStart: _r, onReconnectEnd: Dr, onEdgeContextMenu: ct, onEdgeDoubleClick: $r, onEdgeMouseEnter: Vt, onEdgeMouseMove: Tr, onEdgeMouseLeave: Ot, reconnectRadius: Pr, defaultMarkerColor: pn, noDragClassName: Mr, noWheelClassName: Ht, noPanClassName: li, rfId: mn, disableKeyboardA11y: fi, nodeExtent: Nt, viewport: gi, onViewportChange: Ur }), o.jsx(Lv, { onSelectionChange: z }), Ir, o.jsx($v, { proOptions: Lr, position: Rr }), o.jsx(Dv, { rfId: mn, disableKeyboardA11y: fi })] }) });
}
var Ld = pd(fb);
const pb = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function hb({ children: e }) {
  const t = he(pb);
  return t ? Sv.createPortal(e, t) : null;
}
function gb({ dimensions: e, lineWidth: t, variant: n, className: i }) {
  return o.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: _e(["react-flow__background-pattern", n, i]) });
}
function mb({ radius: e, className: t }) {
  return o.jsx("circle", { cx: e, cy: e, r: e, className: _e(["react-flow__background-pattern", "dots", t]) });
}
var vt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(vt || (vt = {}));
const yb = {
  [vt.Dots]: 1,
  [vt.Lines]: 1,
  [vt.Cross]: 6
}, xb = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function zd({
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
  const f = oe(null), { transform: p, patternId: h } = he(xb, ve), m = i || yb[t], v = t === vt.Dots, w = t === vt.Cross, y = Array.isArray(n) ? n : [n, n], b = [y[0] * p[2] || 1, y[1] * p[2] || 1], g = m * p[2], x = Array.isArray(s) ? s : [s, s], N = w ? [g, g] : b, j = [
    x[0] * p[2] || 1 + N[0] / 2,
    x[1] * p[2] || 1 + N[1] / 2
  ], S = `${h}${e || ""}`;
  return o.jsxs("svg", { className: _e(["react-flow__background", l]), style: {
    ...u,
    ...Nr,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": a
  }, ref: f, "data-testid": "rf__background", children: [o.jsx("pattern", { id: S, x: p[0] % b[0], y: p[1] % b[1], width: b[0], height: b[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${j[0]},-${j[1]})`, children: v ? o.jsx(mb, { radius: g / 2, className: d }) : o.jsx(gb, { dimensions: N, lineWidth: r, variant: t, className: d }) }), o.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${S})` })] });
}
zd.displayName = "Background";
const Vd = Ee(zd);
function wb() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: o.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function vb() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: o.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function bb() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: o.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function jb() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: o.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function Nb() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: o.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function Di({ children: e, className: t, ...n }) {
  return o.jsx("button", { type: "button", className: _e(["react-flow__controls-button", t]), ...n, children: e });
}
const Sb = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function Od({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: i = !0, fitViewOptions: r, onZoomIn: s, onZoomOut: a, onFitView: c, onInteractiveChange: u, className: l, children: d, position: f = "bottom-left", orientation: p = "vertical", "aria-label": h }) {
  const m = je(), { isInteractive: v, minZoomReached: w, maxZoomReached: y, ariaLabelConfig: b } = he(Sb, ve), { zoomIn: g, zoomOut: x, fitView: N } = Ps(), j = () => {
    g(), s?.();
  }, S = () => {
    x(), a?.();
  }, I = () => {
    N(r), c?.();
  }, _ = () => {
    m.setState({
      nodesDraggable: !v,
      nodesConnectable: !v,
      elementsSelectable: !v
    }), u?.(!v);
  }, M = p === "horizontal" ? "horizontal" : "vertical";
  return o.jsxs(jr, { className: _e(["react-flow__controls", M, l]), position: f, style: e, "data-testid": "rf__controls", "aria-label": h ?? b["controls.ariaLabel"], children: [t && o.jsxs(o.Fragment, { children: [o.jsx(Di, { onClick: j, className: "react-flow__controls-zoomin", title: b["controls.zoomIn.ariaLabel"], "aria-label": b["controls.zoomIn.ariaLabel"], disabled: y, children: o.jsx(wb, {}) }), o.jsx(Di, { onClick: S, className: "react-flow__controls-zoomout", title: b["controls.zoomOut.ariaLabel"], "aria-label": b["controls.zoomOut.ariaLabel"], disabled: w, children: o.jsx(vb, {}) })] }), n && o.jsx(Di, { className: "react-flow__controls-fitview", onClick: I, title: b["controls.fitView.ariaLabel"], "aria-label": b["controls.fitView.ariaLabel"], children: o.jsx(bb, {}) }), i && o.jsx(Di, { className: "react-flow__controls-interactive", onClick: _, title: b["controls.interactive.ariaLabel"], "aria-label": b["controls.interactive.ariaLabel"], children: v ? o.jsx(Nb, {}) : o.jsx(jb, {}) }), d] });
}
Od.displayName = "Controls";
const Hd = Ee(Od);
function Cb({ id: e, x: t, y: n, width: i, height: r, style: s, color: a, strokeColor: c, strokeWidth: u, className: l, borderRadius: d, shapeRendering: f, selected: p, onClick: h }) {
  const { background: m, backgroundColor: v } = s || {}, w = a || m || v;
  return o.jsx("rect", { className: _e(["react-flow__minimap-node", { selected: p }, l]), x: t, y: n, rx: d, ry: d, width: i, height: r, style: {
    fill: w,
    stroke: c,
    strokeWidth: u
  }, shapeRendering: f, onClick: h ? (y) => h(y, e) : void 0 });
}
const kb = Ee(Cb), Eb = (e) => e.nodes.map((t) => t.id), Io = (e) => e instanceof Function ? e : () => e;
function Ib({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: i = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: s = kb,
  onClick: a
}) {
  const c = he(Eb, ve), u = Io(t), l = Io(e), d = Io(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return o.jsx(o.Fragment, { children: c.map((p) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    o.jsx(_b, { id: p, nodeColorFunc: u, nodeStrokeColorFunc: l, nodeClassNameFunc: d, nodeBorderRadius: i, nodeStrokeWidth: r, NodeComponent: s, onClick: a, shapeRendering: f }, p)
  )) });
}
function Ab({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: i, nodeBorderRadius: r, nodeStrokeWidth: s, shapeRendering: a, NodeComponent: c, onClick: u }) {
  const { node: l, x: d, y: f, width: p, height: h } = he((m) => {
    const v = m.nodeLookup.get(e);
    if (!v)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const w = v.internals.userNode, { x: y, y: b } = v.internals.positionAbsolute, { width: g, height: x } = ht(w);
    return {
      node: w,
      x: y,
      y: b,
      width: g,
      height: x
    };
  }, ve);
  return !l || l.hidden || !Ru(l) ? null : o.jsx(c, { x: d, y: f, width: p, height: h, style: l.style, selected: !!l.selected, className: i(l), color: t(l), borderRadius: r, strokeColor: n(l), strokeWidth: s, shapeRendering: a, onClick: u, id: l.id });
}
const _b = Ee(Ab);
var Db = Ee(Ib);
const $b = 200, Tb = 150, Pb = (e) => !e.hidden, Mb = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? Pu(ni(e.nodeLookup, { filter: Pb }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, Rb = "react-flow__minimap-desc";
function Wd({
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
  zoomable: w = !1,
  ariaLabel: y,
  inversePan: b,
  zoomStep: g = 1,
  offsetScale: x = 5
}) {
  const N = je(), j = oe(null), { boundingRect: S, viewBB: I, rfId: _, panZoom: M, translateExtent: k, flowWidth: T, flowHeight: z, ariaLabelConfig: C } = he(Mb, ve), A = e?.width ?? $b, E = e?.height ?? Tb, D = S.width / A, P = S.height / E, $ = Math.max(D, P), F = $ * A, W = $ * E, H = x * $, U = S.x - (F - S.width) / 2 - H, O = S.y - (W - S.height) / 2 - H, q = F + H * 2, J = W + H * 2, G = `${Rb}-${_}`, R = oe(0), X = oe();
  R.current = $, ee(() => {
    if (j.current && M)
      return X.current = Zw({
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
      width: T,
      height: z,
      inversePan: b,
      pannable: v,
      zoomStep: g,
      zoomable: w
    });
  }, [v, w, b, g, k, T, z]);
  const de = h ? (ie) => {
    const [fe, V] = X.current?.pointer(ie) || [0, 0];
    h(ie, { x: fe, y: V });
  } : void 0, le = m ? se((ie, fe) => {
    const V = N.getState().nodeLookup.get(fe).internals.userNode;
    m(ie, V);
  }, []) : void 0, te = y ?? C["minimap.ariaLabel"];
  return o.jsx(jr, { position: p, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * $ : void 0,
    "--xy-minimap-node-background-color-props": typeof i == "string" ? i : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof a == "number" ? a : void 0
  }, className: _e(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: o.jsxs("svg", { width: A, height: E, viewBox: `${U} ${O} ${q} ${J}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": G, ref: j, onClick: de, children: [te && o.jsx("title", { id: G, children: te }), o.jsx(Db, { onClick: le, nodeColor: i, nodeStrokeColor: n, nodeBorderRadius: s, nodeClassName: r, nodeStrokeWidth: a, nodeComponent: c }), o.jsx("path", { className: "react-flow__minimap-mask", d: `M${U - H},${O - H}h${q + H * 2}v${J + H * 2}h${-q - H * 2}z
        M${I.x},${I.y}h${I.width}v${I.height}h${-I.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
Wd.displayName = "MiniMap";
const Fd = Ee(Wd), Lb = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, zb = {
  [on.Line]: "right",
  [on.Handle]: "bottom-right"
};
function Vb({ nodeId: e, position: t, variant: n = on.Handle, className: i, style: r = void 0, children: s, color: a, minWidth: c = 10, minHeight: u = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: p, autoScale: h = !0, shouldResize: m, onResizeStart: v, onResize: w, onResizeEnd: y }) {
  const b = yd(), g = typeof e == "string" ? e : b, x = je(), N = oe(null), j = n === on.Handle, S = he(se(Lb(j && h), [j, h]), ve), I = oe(null), _ = t ?? zb[n];
  ee(() => {
    if (!(!N.current || !g))
      return I.current || (I.current = lv({
        domNode: N.current,
        nodeId: g,
        getStoreItems: () => {
          const { nodeLookup: k, transform: T, snapGrid: z, snapToGrid: C, nodeOrigin: A, domNode: E } = x.getState();
          return {
            nodeLookup: k,
            transform: T,
            snapGrid: z,
            snapToGrid: C,
            nodeOrigin: A,
            paneDomNode: E
          };
        },
        onChange: (k, T) => {
          const { triggerNodeChanges: z, nodeLookup: C, parentLookup: A, nodeOrigin: E } = x.getState(), D = [], P = { x: k.x, y: k.y }, $ = C.get(g);
          if ($ && $.expandParent && $.parentId) {
            const F = $.origin ?? E, W = k.width ?? $.measured.width ?? 0, H = k.height ?? $.measured.height ?? 0, U = {
              id: $.id,
              parentId: $.parentId,
              rect: {
                width: W,
                height: H,
                ...Lu({
                  x: k.x ?? $.position.x,
                  y: k.y ?? $.position.y
                }, { width: W, height: H }, $.parentId, C, F)
              }
            }, O = Ts([U], C, A, E);
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
          for (const F of T) {
            const W = {
              ...F,
              type: "position"
            };
            D.push(W);
          }
          z(D);
        },
        onEnd: ({ width: k, height: T }) => {
          const z = {
            id: g,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: k,
              height: T
            }
          };
          x.getState().triggerNodeChanges([z]);
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
        onResize: w,
        onResizeEnd: y,
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
    w,
    y,
    m
  ]);
  const M = _.split("-");
  return o.jsx("div", { className: _e(["react-flow__resize-control", "nodrag", ...M, n, i]), ref: N, style: {
    ...r,
    scale: S,
    ...a && { [j ? "backgroundColor" : "borderColor"]: a }
  }, children: s });
}
Ee(Vb);
function Ob(e, t) {
  return {
    ...t ? { name: t } : {},
    definitionId: e.definitionId,
    state: Gn(e.state),
    layout: e.layout
  };
}
function Hb(e) {
  return JSON.stringify(
    {
      state: Gn(e.state),
      layout: e.layout
    },
    null,
    2
  );
}
function Wb(e, t) {
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
      state: lr(i.state),
      layout: i.layout ?? t.layout
    }
  };
}
function Fb(e, t) {
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
function Bb({
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
        f ? /* @__PURE__ */ o.jsx(Pf, { fallback: /* @__PURE__ */ o.jsx(
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
        /* @__PURE__ */ o.jsx(Kb, { diagnostics: u })
      ]
    }
  );
}
function Kb({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ o.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const i = t.severity ?? "info", r = Xb(t);
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
function Xb(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const qb = { language: "json", displayName: "JSON" };
function Yb({ draft: e, onApply: t }) {
  const n = ue(() => Hb(e), [e]), [i, r] = K(n), [s, a] = K(n), [c, u] = K(null);
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
      Bb,
      {
        ariaLabel: "Workflow JSON",
        document: { uri: "elsa://workflows/definition.json", language: "json", value: i },
        languageAdapter: qb,
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
const Ub = ["Single", "Array", "List", "HashSet"];
function Bd(e) {
  const [t, n] = K(null), [i, r] = K(null);
  ee(() => {
    let u = !1;
    return Oh(e).then(
      (l) => {
        u || n(l);
      },
      () => {
        u || n([]);
      }
    ), Wh(e).then(
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
        label: xl(u.displayName, l),
        group: u.category?.trim() || "Other"
      };
    }) : null,
    [t]
  ), a = ue(
    () => i && i.length > 0 ? i.filter((u) => !u.deprecated).map((u) => ({
      value: u.typeName,
      label: pp(u.displayName, u.typeName)
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
function Zb(e) {
  return !e || e.length === 0 ? void 0 : (e.find((n) => /(^|\.)String$/i.test(n.value) || n.label.toLowerCase() === "string") ?? e[0]).value;
}
function Gb(e, t, n) {
  return {
    add: () => {
      const i = ip(n.namePrefix, e.map((r) => ut(r, n.nameKeys)));
      t([...e, n.create(i)]);
    },
    update: (i, r) => t(e.map((s, a) => a === i ? n.patch(s, r) : s)),
    remove: (i) => t(e.filter((r, s) => s !== i))
  };
}
function Pc({ value: e, options: t, placeholder: n, allowEmpty: i, ariaLabel: r, onChange: s }) {
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
const Jb = {
  Single: "Single",
  Array: "Array",
  List: "List",
  HashSet: "Hash set"
};
function Qb({ value: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ o.jsx("select", { "aria-label": t, value: e, onChange: (i) => n(i.target.value), children: Ub.map((i) => /* @__PURE__ */ o.jsx("option", { value: i, children: Jb[i] }, i)) });
}
function ej(e, t) {
  return t === "" ? !0 : e === "checkbox" ? t === "true" || t === "false" || t === "True" || t === "False" : e === "number" ? Number.isFinite(Number(t.trim())) && t.trim() !== "" : e === "date" ? /^\d{4}-\d{2}-\d{2}$/.test(t) : !0;
}
function tj({ value: e, editor: t, ariaLabel: n, onChange: i }) {
  const r = ej(t, e);
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
function nj({ title: e, addLabel: t, emptyLabel: n, headers: i, isEmpty: r, onAdd: s, children: a }) {
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
function ij({ label: e, onRemove: t }) {
  return /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-properties-remove", "aria-label": e, title: e, onClick: t, children: /* @__PURE__ */ o.jsx(Tn, { size: 14 }) }) });
}
function rj({ checked: e, ariaLabel: t, onChange: n }) {
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
  const { add: m, update: v, remove: w } = Gb(e, h, {
    namePrefix: r,
    nameKeys: s,
    create: (g) => l(g, Zb(t)),
    patch: d
  }), y = ["Name", "Type", "Collection", ...f.default ? ["Default"] : [], ...f.storage ? ["Storage"] : [], ...f.required ? ["Required"] : []], b = r.toLowerCase();
  return /* @__PURE__ */ o.jsx(
    nj,
    {
      title: a,
      addLabel: c,
      emptyLabel: u,
      headers: y,
      isEmpty: e.length === 0,
      onAdd: m,
      children: e.map((g, x) => {
        const N = ut(g, s), j = ls(g), S = ut(g, wl), I = S ? p?.get(S) : void 0, _ = j.collectionKind === "Single" ? i(j.alias) : "text";
        return /* @__PURE__ */ o.jsxs("tr", { children: [
          /* @__PURE__ */ o.jsxs("td", { children: [
            /* @__PURE__ */ o.jsx("input", { type: "text", "aria-label": `${r} name`, value: N, onChange: (M) => v(x, { name: M.target.value }) }),
            I ? /* @__PURE__ */ o.jsx("span", { className: "wf-properties-warning", role: "note", title: I, children: I }) : null
          ] }),
          /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            Pc,
            {
              ariaLabel: `${r} type`,
              value: j.alias,
              options: t,
              placeholder: "Type",
              onChange: (M) => v(x, { type: { alias: M, collectionKind: j.collectionKind } })
            }
          ) }),
          /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            Qb,
            {
              ariaLabel: `${r} collection kind`,
              value: j.collectionKind,
              onChange: (M) => v(x, { type: { alias: j.alias, collectionKind: M } })
            }
          ) }),
          f.default ? /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            tj,
            {
              ariaLabel: `${r} default value`,
              value: ap(g.default),
              editor: _,
              onChange: (M) => v(x, { default: sp(M) })
            }
          ) }) : null,
          f.storage ? /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            Pc,
            {
              ariaLabel: `${r} storage driver`,
              value: ut(g, gp),
              options: n,
              placeholder: "—",
              allowEmpty: !0,
              onChange: (M) => v(x, { storageDriverType: M || null })
            }
          ) }) : null,
          f.required ? /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            rj,
            {
              ariaLabel: `${r} required`,
              checked: g.isRequired === !0,
              onChange: (M) => v(x, { isRequired: M })
            }
          ) }) : null,
          /* @__PURE__ */ o.jsx(ij, { label: `Remove ${b} ${N || x + 1}`, onRemove: () => w(x) })
        ] }, x);
      })
    }
  );
}
function Kd({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, title: r = "Variables", addLabel: s = "Add variable", emptyLabel: a = "No variables defined.", warnings: c, onChange: u }) {
  return /* @__PURE__ */ o.jsx(
    Rs,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Variable",
      nameKeys: hp,
      title: r,
      addLabel: s,
      emptyLabel: a,
      create: (l, d) => rp({ name: l, alias: d }),
      patch: (l, d) => op(l, d),
      columns: { default: !0, storage: !0 },
      warnings: c,
      onChange: u
    }
  );
}
function oj({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, onChange: r }) {
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
      create: (s, a) => cp({ name: s, alias: a }),
      patch: (s, a) => lp(s, a),
      columns: { default: !1, storage: !0, required: !0 },
      onChange: r
    }
  );
}
function sj({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, onChange: r }) {
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
      create: (s, a) => up({ name: s, alias: a }),
      patch: (s, a) => dp(s, a),
      columns: { default: !1, storage: !1 },
      onChange: r
    }
  );
}
function Wi(e) {
  return (e ?? []).filter(Rn);
}
function aj({ context: e, variables: t, title: n, addLabel: i, emptyLabel: r, warnings: s, onChange: a }) {
  const { typeOptions: c, storageOptions: u, editorForAlias: l } = Bd(e);
  return /* @__PURE__ */ o.jsx(
    Kd,
    {
      items: Wi(t),
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
function cj({ definition: e, definitionId: t, onMetaChange: n }) {
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
function lj({ details: e, draft: t, context: n, onStateChange: i, onDefinitionMetaChange: r }) {
  const { typeOptions: s, storageOptions: a, editorForAlias: c } = Bd(n), u = Wi(t.state.variables), l = Wi(t.state.inputs), d = Wi(t.state.outputs), f = e?.versions ?? [];
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-properties-view", children: [
    /* @__PURE__ */ o.jsx(
      cj,
      {
        definition: e?.definition,
        definitionId: t.definitionId,
        onMetaChange: r
      }
    ),
    /* @__PURE__ */ o.jsx(
      Kd,
      {
        items: u,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, variables: p }))
      }
    ),
    /* @__PURE__ */ o.jsx(
      oj,
      {
        items: l,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, inputs: p }))
      }
    ),
    /* @__PURE__ */ o.jsx(
      sj,
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
const Mc = "application/x-elsa-activity-version-id", uj = 6, dj = 1200, fj = 250, pj = [10, 25, 50], hj = 10, Rc = "elsa-studio-workflow-palette-width", Lc = "elsa-studio-workflow-inspector-width", zc = "elsa-studio-workflow-palette-collapsed", Vc = "elsa-studio-workflow-inspector-collapsed", Xd = "elsa-studio-workflow-side-panel-maximized", kn = 180, En = 460, gj = 260, Xt = 260, qt = 560, mj = 320, Oc = 42, $i = 16, qd = Fe.createContext(null), Yd = Fe.createContext(null), Ud = Fe.createContext(null);
function yj(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function Zd(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Rt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function Lt(e, t, n) {
  const i = t.createPrompt(n);
  return i ? (e.dispatchPrompt(i), !0) : !1;
}
function xj(e) {
  if (!e) return null;
  const t = [], n = e.match(/```(?:json)?\s*([\s\S]*?)```/i);
  n && t.push(n[1]);
  const i = e.match(/\{[\s\S]*?\}/);
  i && t.push(i[0]);
  for (const a of t)
    try {
      const c = JSON.parse(a.trim()), u = Hc(c.name, c.description);
      if (u) return u;
    } catch {
    }
  const r = e.match(/name\s*[:-]\s*(.+)/i)?.[1], s = e.match(/description\s*[:-]\s*(.+)/i)?.[1];
  return Hc(r, s);
}
function Hc(e, t) {
  const n = typeof e == "string" ? Wc(e) : void 0, i = typeof t == "string" ? Wc(t) : void 0;
  return n || i ? { name: n || void 0, description: i || void 0 } : null;
}
function Wc(e) {
  return e.trim().replace(/,$/, "").trim().replace(/^["']/, "").replace(/["']$/, "").trim();
}
function wj(e, t) {
  return e.rootActivityVersionId ?? Gd(t, e.rootKind)?.activityVersionId ?? null;
}
function Gd(e, t) {
  return e.find((n) => vj(n) === t);
}
function vj(e) {
  return e ? bj(e) ? "flowchart" : jj(e) ? "sequence" : null : null;
}
function Yo(e) {
  return Ol(e, (t) => t.category).map((t) => ({
    category: t.category,
    activities: t.items.sort((n, i) => ke(n).localeCompare(ke(i)))
  }));
}
function bj(e) {
  return ke(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function jj(e) {
  return ke(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function Nj(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function Jd(e) {
  return Ij(e.rootActivityType) || e.rootActivityType;
}
function Sj(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function Cj(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function kj(e, t) {
  return Fc(t) - Fc(e);
}
function Fc(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function Qd(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
function ef(e) {
  const t = e?.workflowExecutionId ?? e?.runId ?? e?.executionId;
  return typeof t == "string" && t.trim() ? t : null;
}
async function Ej(e) {
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
function Ij(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function Aj(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    Ti(t, n.typeName, n), Ti(t, n.name, n), Ti(t, n.displayName, n);
    const i = n.typeName.split(".").filter(Boolean).at(-1);
    Ti(t, i, n);
  }
  return t;
}
function Bc(e, t, n) {
  const i = t.get(e.activityVersionId);
  return n.get(In(i?.activityTypeKey)) ?? n.get(In(_t(i?.activityTypeKey))) ?? n.get(In(i?.displayName)) ?? n.get(In(e.activityVersionId)) ?? null;
}
function Ti(e, t, n) {
  const i = In(t);
  i && !e.has(i) && e.set(i, n);
}
function In(e) {
  return e?.trim().toLowerCase() ?? "";
}
function Kc(e, t, n, i) {
  const r = Sr();
  if (!r) return t;
  const s = r.getItem(e);
  if (s == null) return t;
  const a = Number(s);
  return Number.isFinite(a) ? Fi(a, n, i) : t;
}
function Xc(e, t) {
  const n = Sr();
  if (!n) return t;
  const i = n.getItem(e);
  return i === "true" ? !0 : i === "false" ? !1 : t;
}
function _j() {
  const e = Sr();
  if (!e) return null;
  const t = e.getItem(Xd);
  return t === "palette" || t === "inspector" ? t : null;
}
function Sr() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function vn(e, t) {
  const n = Sr();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function Fi(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function tf(e) {
  switch (Dj(e)) {
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
function Dj(e) {
  return (e ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}
function $j(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function Uo(e) {
  return `${ke(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function qc(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function Tj(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function nf(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function Pj(e) {
  const t = nf(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function Mj(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function We(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function Rj(e) {
  return sf(We(e));
}
function Lj(e, t) {
  const n = t?.get(e.activityVersionId);
  return n ? ke(n) : _t(e.activityVersionId) ?? e.activityVersionId;
}
function zj(e, t) {
  if (e.cardinality === "single") {
    const i = e.activities[0];
    return i ? Lj(i, t) : "Empty — click to choose";
  }
  const n = e.activities.length;
  return `${n} activit${n === 1 ? "y" : "ies"}`;
}
function rf(e, t, n = []) {
  if (!e) return n;
  const i = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: i?.activityTypeKey ?? e.activityVersionId,
    displayName: i ? ke(i) : void 0
  });
  for (const r of Ae(e, t))
    for (const s of r.activities) rf(s, t, n);
  return n;
}
function of(e, t, n = []) {
  if (!e) return n;
  for (const i of $l(e))
    n.push({ source: i.source, target: i.target, sourcePort: i.sourceHandle ?? void 0, targetPort: i.targetHandle ?? void 0 });
  for (const i of Ae(e, t))
    for (const r of i.activities) of(r, t, n);
  return n;
}
function An(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function Vj(e) {
  return `${e.id}-${sf(JSON.stringify(e.state))}`;
}
function sf(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function Ls(e) {
  return e.status.toLowerCase() === "rejected";
}
function Oj(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function Hj(e, t) {
  const n = e instanceof Error ? e.message : String(e);
  return Wj(e, n) ? `Run ${t} was not found.` : n;
}
function Wj(e, t) {
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
const af = { workflowActivity: Fj }, cf = { workflow: Kj };
function Fj({ id: e, data: t, selected: n }) {
  const i = t, r = i.runtime, s = !i.suppressFlowPorts, a = s ? i.sourcePorts.length > 0 ? i.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], c = Bj(i), l = Fe.useContext(Yd)?.({ activityVersionId: i.activityVersionId, activityTypeKey: i.activityTypeKey }) ?? null, d = Fe.useContext(Ud), f = i.onEnterSlot ?? (d ? (p) => d(e, i.label, p) : void 0);
  return /* @__PURE__ */ o.jsxs(
    "div",
    {
      className: ["wf-node", n ? "selected" : "", r ? "wf-node-runtime" : "", r?.hasBlockingIncident ? "faulted" : "", l ? "wf-node-unavailable" : ""].filter(Boolean).join(" "),
      "data-icon": i.icon ?? "activity",
      children: [
        s && i.acceptsInbound ? /* @__PURE__ */ o.jsx(sn, { type: "target", position: ae.Left }) : null,
        l ? /* @__PURE__ */ o.jsx("span", { className: "wf-node-availability", title: `No longer available for new use · ${zn(l.state)}`, children: /* @__PURE__ */ o.jsx($n, { size: 13 }) }) : null,
        /* @__PURE__ */ o.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ o.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: fr(i.icon) }),
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
function Bj(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((r) => !!r).join(" · ");
}
function Kj(e) {
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
  } = e, p = Fe.useContext(qd), [h, m] = K(!1), [v, w, y] = rr({ sourceX: n, sourceY: i, targetX: r, targetY: s, sourcePosition: a, targetPosition: c }), b = p?.highlightedEdgeId === t;
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
        labelX: w,
        labelY: y,
        labelStyle: f,
        onMouseEnter: () => m(!0),
        onMouseLeave: () => m(!1)
      }
    ),
    p ? /* @__PURE__ */ o.jsx(hb, { children: /* @__PURE__ */ o.jsxs(
      "div",
      {
        className: ["wf-edge-actions", h ? "visible" : "", b ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${w}px, ${y}px)` },
        onMouseEnter: () => m(!0),
        onMouseLeave: () => m(!1),
        children: [
          /* @__PURE__ */ o.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (g) => p.requestInsertActivity(t, g.clientX, g.clientY), children: /* @__PURE__ */ o.jsx(Gt, { size: 12 }) }),
          /* @__PURE__ */ o.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => p.deleteEdge(t), children: /* @__PURE__ */ o.jsx(Tn, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function lf({ clientX: e, clientY: t, activities: n, onPick: i, onClose: r }) {
  const [s, a] = K(""), [c, u] = K(0), l = oe(null), d = oe(null), f = ue(() => {
    const b = s.trim().toLowerCase(), g = n.filter(Nj);
    return b ? g.filter((x) => ke(x).toLowerCase().includes(b) || x.activityTypeKey.toLowerCase().includes(b) || (x.category ?? "").toLowerCase().includes(b) || (x.description ?? "").toLowerCase().includes(b)) : g;
  }, [n, s]), p = ue(() => Yo(f), [f]), h = ue(() => p.flatMap((b) => b.activities), [p]);
  ee(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), ee(() => {
    const b = (x) => {
      l.current?.contains(x.target) || r();
    }, g = (x) => {
      x.key === "Escape" && r();
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
  }, v = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), w = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let y = -1;
  return /* @__PURE__ */ o.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: v, top: w }, onMouseDown: (b) => b.stopPropagation(), onClick: (b) => b.stopPropagation(), children: [
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
        y += 1;
        const x = y, N = x === c;
        return /* @__PURE__ */ o.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": N,
            className: N ? "active" : "",
            onMouseEnter: () => u(x),
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
function Bi({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: i
}) {
  const r = Uf(t.map((s) => s.id), n, i);
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
function Yc(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
const Xj = "Expressions/UnresolvedVariable";
function qj(e) {
  return String(e.type ?? e.code ?? "");
}
function Yj(e) {
  return qj(e) === Xj;
}
function Uj(e) {
  const t = String(e ?? "").trim().split("/").filter(Boolean), [n, i, ...r] = t, s = n === "$workflow";
  return {
    nodeId: !n || s ? null : n,
    isWorkflowScope: s,
    bag: i === "inputs" || i === "outputs" || i === "variables" ? i : null,
    referenceKey: r.length > 0 ? r.join("/") : null
  };
}
function Zj(e) {
  return (e ?? []).filter(Yj).map((t) => ({
    error: t,
    path: Uj(t.path),
    message: t.message ?? "Variable reference is not visible from this activity's scope."
  }));
}
function Gj({ draft: e, onRepair: t }) {
  const n = e.validationErrors;
  if (!n.length)
    return /* @__PURE__ */ o.jsxs("div", { className: "wf-validation ok", children: [
      /* @__PURE__ */ o.jsx(an, { size: 14 }),
      " No validation errors"
    ] });
  const i = Zj(n), r = new Map(i.map((s) => [s.error, s]));
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
          /* @__PURE__ */ o.jsx(Of, { size: 12 }),
          " Repair"
        ] }) : null
      ] }, a);
    }) })
  ] });
}
function Jj({
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
function Qj({ testRun: e, onOpenRun: t }) {
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
        /* @__PURE__ */ o.jsx("dd", { children: Uc(e.activityCount, "activity") })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ o.jsx("dd", { children: Uc(e.incidentCount, "incident") })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ o.jsx("dd", { title: e.expiresAt ? Re(e.expiresAt) : "None", children: e.expiresAt ? Re(e.expiresAt) : "None" })
      ] })
    ] })
  ] }) });
}
function Uc(e, t) {
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
function uf({ status: e, run: t, compact: n = !1 }) {
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
      await Ej(e), i(n);
    } catch {
      r(n);
    }
  };
  return /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (a) => {
    s(a);
  }, children: /* @__PURE__ */ o.jsx(Hf, { size: 12 }) });
}
function eN({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: i }) {
  const [r, s] = K("loading"), [a, c] = K(""), [u, l] = K(""), [d, f] = K(null), [p, h] = K([]), m = n?.trim().toLowerCase() ?? "", v = ue(
    () => m ? p.filter((S) => Sj(S, m)) : p,
    [m, p]
  ), w = ue(
    () => Array.from(new Set(p.flatMap((S) => [
      S.definitionId,
      S.definitionVersionId,
      S.sourceId
    ]).filter((S) => !!S))).sort((S, I) => S.localeCompare(I)),
    [p]
  ), y = Rt(t, "weaver.workflows.explain-executable"), b = se(async () => {
    s("loading"), c("");
    try {
      h(await zl(e)), s("ready");
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
      const I = await Ll(e, S.artifactId), _ = ef(I);
      f({ artifactId: S.artifactId, workflowExecutionId: _ }), l(`Started ${S.artifactId}`);
    } catch (I) {
      c(I instanceof Error ? I.message : String(I));
    }
  }, x = (S) => {
    y && Lt(t, y, S) && (c(""), f(null), l(`Sent ${S.artifactId} to Weaver`));
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
        /* @__PURE__ */ o.jsx(sr, { size: 14 }),
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
      /* @__PURE__ */ o.jsx("datalist", { id: "wf-executable-definition-options", children: w.map((S) => /* @__PURE__ */ o.jsx("option", { value: S }, S)) }),
      n ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => i(null), children: [
        /* @__PURE__ */ o.jsx(ss, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    r === "failed" ? /* @__PURE__ */ o.jsx(oi, { message: a }) : null,
    u ? /* @__PURE__ */ o.jsx(uf, { status: u, run: d }) : null,
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
        /* @__PURE__ */ o.jsx(tN, { executable: S, onCopied: N, onCopyFailed: j }),
        /* @__PURE__ */ o.jsx("span", { children: Jd(S) }),
        /* @__PURE__ */ o.jsx("span", { children: Re(S.publishedAt ?? S.createdAt) }),
        /* @__PURE__ */ o.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
            g(S);
          }, children: [
            /* @__PURE__ */ o.jsx(Zt, { size: 13 }),
            " Run"
          ] }),
          y ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => x(S), children: [
            /* @__PURE__ */ o.jsx(ft, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, S.artifactId))
    ] }) : null
  ] });
}
function tN({ executable: e, onCopied: t, onCopyFailed: n }) {
  const i = e.sourceId || e.definitionVersionId || e.definitionId, r = e.sourceVersion, s = e.definitionId, a = () => {
    window.history.pushState({}, "", `/workflows/definitions?definition=${encodeURIComponent(s)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ o.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ o.jsx("span", { className: "wf-source-kind", children: Qd(e.sourceKind) }),
    i ? /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line", children: [
      s ? /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-link-button", "aria-label": `Open source definition ${s}`, onClick: a, children: /* @__PURE__ */ o.jsx("code", { title: i, children: i }) }) : /* @__PURE__ */ o.jsx("code", { title: i, children: i }),
      /* @__PURE__ */ o.jsx(It, { value: i, ariaLabel: `Copy source ID ${i}`, copiedLabel: "source ID", onCopied: t, onCopyFailed: n })
    ] }) : null,
    r ? /* @__PURE__ */ o.jsxs("small", { children: [
      "Version ",
      r
    ] }) : null
  ] });
}
function nN({ context: e, ai: t, definitionId: n, publishedArtifactId: i }) {
  const [r, s] = K("loading"), [a, c] = K(""), [u, l] = K(""), [d, f] = K(null), [p, h] = K([]), m = Rt(t, "weaver.workflows.explain-executable"), v = se(async () => {
    s("loading"), c("");
    try {
      const N = await zl(e);
      h(N.filter((j) => Cj(j, n)).sort(kj)), s("ready");
    } catch (N) {
      c(N instanceof Error ? N.message : String(N)), h([]), s("failed");
    }
  }, [e, n]);
  ee(() => {
    v();
  }, [v, i]);
  const w = async (N) => {
    l(""), f(null), c("");
    try {
      const j = await Ll(e, N.artifactId);
      f({ artifactId: N.artifactId, workflowExecutionId: ef(j) }), l(`Started ${N.artifactId}`);
    } catch (j) {
      c(j instanceof Error ? j.message : String(j));
    }
  }, y = (N) => {
    m && Lt(t, m, N) && (c(""), f(null), l(`Sent ${N.artifactId} to Weaver`));
  }, b = () => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(n)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, g = (N) => {
    c(""), f(null), l(`Copied ${N}`);
  }, x = (N) => {
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
    u ? /* @__PURE__ */ o.jsx(uf, { status: u, run: d, compact: !0 }) : null,
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
          /* @__PURE__ */ o.jsx(It, { value: N.artifactId, ariaLabel: `Copy artifact ID ${N.artifactId}`, copiedLabel: "artifact ID", onCopied: g, onCopyFailed: x })
        ] }),
        /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ o.jsx("code", { title: N.artifactHash, children: N.artifactHash }),
          /* @__PURE__ */ o.jsx(It, { value: N.artifactHash, ariaLabel: `Copy artifact hash ${N.artifactHash}`, copiedLabel: "artifact hash", onCopied: g, onCopyFailed: x })
        ] })
      ] }),
      /* @__PURE__ */ o.jsxs("dl", { children: [
        /* @__PURE__ */ o.jsxs("div", { children: [
          /* @__PURE__ */ o.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ o.jsxs("dd", { children: [
            Qd(N.sourceKind),
            " ",
            N.sourceVersion ? `v${N.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("div", { children: [
          /* @__PURE__ */ o.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ o.jsx("dd", { children: Jd(N) })
        ] })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
          w(N);
        }, children: [
          /* @__PURE__ */ o.jsx(Zt, { size: 13 }),
          " Run"
        ] }),
        m ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => y(N), children: [
          /* @__PURE__ */ o.jsx(ft, { size: 13 }),
          " Explain"
        ] }) : null
      ] })
    ] }, N.artifactId)) }) : null
  ] });
}
function df() {
  const [e, t] = K(() => Kc(Rc, gj, kn, En)), [n, i] = K(() => Kc(Lc, mj, Xt, qt)), [r, s] = K(() => Xc(zc, !1)), [a, c] = K(() => Xc(Vc, !1)), [u, l] = K(_j);
  ee(() => {
    vn(Rc, String(e));
  }, [e]), ee(() => {
    vn(Lc, String(n));
  }, [n]), ee(() => {
    vn(zc, String(r));
  }, [r]), ee(() => {
    vn(Vc, String(a));
  }, [a]), ee(() => {
    vn(Xd, u);
  }, [u]), ee(() => {
    if (!u) return;
    const g = (x) => {
      x.key === "Escape" && l(null);
    };
    return window.addEventListener("keydown", g), () => window.removeEventListener("keydown", g);
  }, [u]);
  const d = se((g) => {
    l((x) => x === g ? null : x), g === "palette" ? s((x) => !x) : c((x) => !x);
  }, []), f = se((g) => {
    g === "palette" ? s(!1) : c(!1), l((x) => x === g ? null : g);
  }, []), p = se((g, x) => {
    l(null), g === "palette" ? (s(!1), t((N) => Fi(N + x, kn, En))) : (c(!1), i((N) => Fi(N + x, Xt, qt)));
  }, []), h = se((g, x) => {
    x.preventDefault(), l(null), g === "palette" ? s(!1) : c(!1);
    const N = x.clientX, j = g === "palette" ? e : n, S = g === "palette" ? kn : Xt, I = g === "palette" ? En : qt;
    document.body.classList.add("wf-side-panel-resizing");
    const _ = (k) => {
      const T = g === "palette" ? k.clientX - N : N - k.clientX, z = Fi(j + T, S, I);
      g === "palette" ? t(z) : i(z);
    }, M = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", _), window.removeEventListener("pointerup", M), window.removeEventListener("pointercancel", M);
    };
    window.addEventListener("pointermove", _), window.addEventListener("pointerup", M), window.addEventListener("pointercancel", M);
  }, [n, e]), m = se((g, x) => {
    x.key === "ArrowLeft" ? (x.preventDefault(), p(g, g === "palette" ? -$i : $i)) : x.key === "ArrowRight" ? (x.preventDefault(), p(g, g === "palette" ? $i : -$i)) : x.key === "Home" ? (x.preventDefault(), g === "palette" ? t(kn) : i(Xt)) : x.key === "End" && (x.preventDefault(), g === "palette" ? t(En) : i(qt));
  }, [p]), v = !r && u !== "inspector", w = !a && u !== "palette", y = [
    "wf-editor-body",
    r ? "palette-collapsed" : "",
    a ? "inspector-collapsed" : "",
    u === "palette" ? "palette-maximized" : "",
    u === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), b = {
    "--wf-palette-width": `${r ? Oc : e}px`,
    "--wf-inspector-width": `${a ? Oc : n}px`
  };
  return {
    paletteWidth: e,
    inspectorWidth: n,
    paletteCollapsed: r,
    inspectorCollapsed: a,
    maximizedSidePanel: u,
    setInspectorCollapsed: c,
    paletteExpanded: v,
    inspectorExpanded: w,
    editorBodyClassName: y,
    editorBodyStyle: b,
    toggleSidePanelCollapsed: d,
    toggleSidePanelMaximized: f,
    startSidePanelResize: h,
    handleSidePanelResizeKeyDown: m
  };
}
const iN = 50;
function Zc() {
  return { past: [], future: [] };
}
function rN(e) {
  return e.past.length > 0;
}
function oN(e) {
  return e.future.length > 0;
}
function Gc(e, t, n = iN) {
  const i = [...e.past, t];
  return i.length > n && i.splice(0, i.length - n), { past: i, future: [] };
}
function sN(e, t) {
  if (e.past.length === 0) return null;
  const n = e.past.slice(), i = n.pop();
  return { history: { past: n, future: [...e.future, t] }, snapshot: i };
}
function aN(e, t) {
  if (e.future.length === 0) return null;
  const n = e.future.slice(), i = n.pop();
  return { history: { past: [...e.past, t], future: n }, snapshot: i };
}
function cN({ draft: e, restoreDraft: t }) {
  const n = oe(Zc()), i = oe(null), r = oe(""), s = oe(!1), [a, c] = K(0), u = se((v) => {
    n.current = Zc(), i.current = v ? An(v) : null, r.current = v ? We(v) : "", s.current = !1, c(0);
  }, []);
  ee(() => {
    if (!e) return;
    if (s.current) {
      s.current = !1;
      return;
    }
    const v = We(e);
    if (v === r.current) return;
    const w = window.setTimeout(() => {
      const y = i.current;
      y && (n.current = Gc(n.current, y), c((b) => b + 1)), i.current = An(e), r.current = v;
    }, fj);
    return () => window.clearTimeout(w);
  }, [e]);
  const l = se(() => {
    if (!e) return;
    const v = We(e);
    if (v === r.current) return;
    const w = i.current;
    w && (n.current = Gc(n.current, w)), i.current = An(e), r.current = v;
  }, [e]), d = se((v) => {
    s.current = !0, i.current = An(v), r.current = We(v), t(v), c((w) => w + 1);
  }, [t]), f = se(() => {
    if (!e) return;
    l();
    const v = sN(n.current, e);
    v && (n.current = v.history, d(v.snapshot));
  }, [e, l, d]), p = se(() => {
    if (!e) return;
    l();
    const v = aN(n.current, e);
    v && (n.current = v.history, d(v.snapshot));
  }, [e, l, d]), { canUndoNow: h, canRedoNow: m } = ue(() => {
    const v = !!e && !!i.current && We(e) !== r.current;
    return {
      canUndoNow: rN(n.current) || v,
      canRedoNow: oN(n.current) && !v
    };
  }, [e, a]);
  return { resetHistory: u, undo: f, redo: p, canUndoNow: h, canRedoNow: m };
}
const lN = {
  draft: null,
  frames: [],
  selectedNodeId: null,
  testRun: null,
  publishedArtifactId: null
};
function uN(e, t) {
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
function dN() {
  const [e, t] = Mf(uN, lN), n = ue(() => ({
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
const fN = 320, pN = 140;
function hN(e, t, n) {
  return n === "sequence" ? gN(e) : mN(e, t);
}
function gN(e) {
  const t = /* @__PURE__ */ new Map();
  return e.forEach((n, i) => {
    t.set(n.id, { x: i * 280, y: 0 });
  }), t;
}
function mN(e, t) {
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
      n.set(p, { x: d * fN, y: h * pN });
    });
  return n;
}
function yN(e, t, n, i, r) {
  if (!e) return { kind: "becomeRoot" };
  const s = Ln(e, t, r);
  if (!s)
    return t.length > 0 ? { kind: "staleFrames" } : Ae(n, i)[0] ? { kind: "wrapRoot" } : { kind: "leafError" };
  const a = s.slot.cardinality === "single" ? s.slot.activities.find((c) => c.nodeId !== n.nodeId) ?? null : null;
  return { kind: "addToSlot", slot: s.slot, replacedActivity: a };
}
const xN = "root";
function wN(e) {
  return e.length === 0 ? xN : e.map((t) => `${t.ownerNodeId}:${t.slotId}`).join("/");
}
function vN(e, t) {
  return e.length === t.length && e.every((n, i) => n.id === t[i]);
}
function bN({
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
  const [w, y] = K([]), [b, g] = K([]), [x, N] = K(null), [j, S] = K(null), [I, _] = K(null), M = oe(null), k = oe(null), T = oe(/* @__PURE__ */ new Map()), z = oe(/* @__PURE__ */ new Set()), C = oe(null), A = oe([]), E = oe(null), D = oe(null), P = oe(!1), $ = ue(() => wN(i), [i]);
  ee(() => () => {
    x && T.current.set($, x.getViewport());
  }, [x, $]), ee(() => {
    if (C.current = $, !n) {
      A.current = [], y([]), g([]);
      return;
    }
    const L = a ? El(n, r, e?.layout ?? []) : t ? kl(t, r, e?.layout ?? []) : { nodes: [], edges: [] };
    A.current = L.nodes.map((B) => B.id), y(L.nodes), g(L.edges);
  }, [r, e?.layout, a, t, n, $]), ee(() => {
    if (!x || C.current !== $ || !vN(w, A.current)) return;
    C.current = null;
    const L = T.current.get($), B = z.current.has($);
    z.current.add($), window.requestAnimationFrame(() => {
      L ? x.setViewport(L) : !B && w.length > 0 && x.fitView({ padding: 0.2 });
    });
  }, [w, x, $]);
  const F = se((L, B, Y) => Y ? [
    ...L.filter((re) => re.nodeId !== B),
    { nodeId: B, x: Math.round(Y.x), y: Math.round(Y.y) }
  ] : L, []), W = se((L, B) => {
    if (e?.state.rootActivity && a)
      return null;
    const Y = Ki(L, Uo(L)), re = yN(e?.state.rootActivity, i, Y, L, s);
    return re.kind === "becomeRoot" ? (f(
      ({ draft: Q }) => Q ? { ...Q, state: { ...Q.state, rootActivity: Y } } : null,
      Y.nodeId
    ), null) : re.kind === "leafError" ? (m(""), v("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root."), null) : re.kind === "staleFrames" ? (m(""), v("This slot could not be resolved — returning to the workflow root."), h(), null) : re.kind === "wrapRoot" ? (f(({ draft: Q }) => {
      const xe = Q?.state.rootActivity;
      return xe ? {
        ...Q,
        layout: F(Q.layout, xe.nodeId, B),
        state: { ...Q.state, rootActivity: aa(Y, [], [xe], L) }
      } : null;
    }, e?.state.rootActivity?.nodeId ?? null), v(""), m(`Wrapped root in ${ke(L)}`), null) : (f(({ draft: Q, frames: xe }) => {
      if (!Q?.state.rootActivity) return null;
      const pe = Ln(Q.state.rootActivity, xe, s);
      if (!pe) return null;
      const we = pe.slot.cardinality === "single" ? [Y] : [...pe.slot.activities, Y], Se = aa(Q.state.rootActivity, xe, we, s);
      return {
        ...Q,
        layout: F(Q.layout, Y.nodeId, B),
        state: { ...Q.state, rootActivity: Se }
      };
    }, Y.nodeId), re.replacedActivity && (v(""), m(`Replaced ${re.slot.label} content`)), Y);
  }, [s, e?.state.rootActivity, i, a, f, h, F, v, m]), H = se((L, B) => {
    const Y = Ki(L, Uo(L)), re = {
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
        childSlots: Ae(Y, L),
        acceptsInbound: String(L.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: Tl(Y, L)
      }
    };
    return { activityNode: Y, node: re };
  }, []), U = se((L, B, Y = []) => {
    a || d(({ draft: re, frames: Q }) => {
      if (!re) return null;
      const xe = Op(re.layout, L), pe = re.state.rootActivity;
      if (!pe) return { ...re, layout: xe };
      const we = Ln(pe, Q, s);
      if (!we) return { ...re, layout: xe };
      const Se = zp(we, L, B, Y), qe = we.slot.mode === "flowchart" ? Vp(Se, B) : Se;
      return {
        ...re,
        layout: xe,
        state: {
          ...re.state,
          rootActivity: Lp(pe, Q, qe, s)
        }
      };
    });
  }, [s, a, d]), O = se((L, B) => {
    if (!M.current) return null;
    const Y = M.current.getBoundingClientRect();
    return x ? x.screenToFlowPosition({ x: L, y: B }) : {
      x: L - Y.left,
      y: B - Y.top
    };
  }, [x]), q = se((L, B) => document.elementFromPoint(L, B)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), J = se((L, B, Y) => {
    const re = w.find((Te) => Te.id === B.source), Q = w.find((Te) => Te.id === B.target), xe = re && Q ? Tj(re, Q) : re ? qc(re) : Y, pe = H(L, xe), Se = [...w.map((Te) => Te.selected ? { ...Te, selected: !1 } : Te), pe.node], qe = Yp(b, B, pe.node.id);
    y(Se), g(qe), p(pe.node.id), U(Se, qe, [pe.activityNode]);
  }, [U, H, b, w, p]), G = se((L, B, Y) => {
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
      Math.hypot(Y.clientX - re.startX, Y.clientY - re.startY) >= uj && (re.dragging = !0);
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
  }, [x, G]);
  const R = (L, B) => {
    D.current = { activityVersionId: B.activityVersionId, handledDrop: !1 }, L.dataTransfer.setData(Mc, B.activityVersionId), L.dataTransfer.setData("text/plain", B.activityVersionId), L.dataTransfer.effectAllowed = "copy";
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
    const B = L.dataTransfer.getData(Mc) || L.dataTransfer.getData("text/plain");
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
    B.length !== 0 && y((Y) => ld(B, Y));
  }, ge = (L) => {
    a || g((B) => ud(L, B));
  }, ye = (L) => !L.source || !L.target || L.source === L.target || !c ? !1 : !L.targetHandle, Ie = (L) => {
    if (!e?.state.rootActivity || !t || !c || !ye(L)) return;
    const B = Xi(L.source, L.target, L.sourceHandle ?? "Done", L.targetHandle ?? void 0), Y = fd(B, b);
    g(Y), U(w, Y);
  }, De = () => {
    U(w, b);
  }, Ve = !a && w.length > 0, at = se(() => {
    if (a || w.length === 0) return;
    const L = t?.slot.mode === "sequence" ? "sequence" : "flowchart", B = hN(w, b, L), Y = w.map((re) => {
      const Q = B.get(re.id);
      return Q ? { ...re, position: Q } : re;
    });
    y(Y), U(Y, b), window.requestAnimationFrame(() => x?.fitView({ padding: 0.2 })), m("Rearranged the canvas.");
  }, [b, w, t, a, U, x, m]), Ye = (L, B) => {
    if (!B.nodeId || B.handleType === "target") {
      k.current = null;
      return;
    }
    k.current = {
      nodeId: B.nodeId,
      handleId: B.handleId ?? null
    };
  }, Ue = (L, B) => {
    const Y = Mj(k.current, B);
    if (k.current = null, !Y || !c || B.toNode || B.toHandle || Pj(L)) return;
    const re = nf(L);
    S({
      kind: "fromPort",
      sourceNodeId: Y.nodeId,
      sourceHandleId: Y.handleId,
      clientX: re.x,
      clientY: re.y
    });
  }, Me = (L, B) => {
    if (!c || !ye(B)) return;
    const Y = Kv(L, {
      ...B,
      sourceHandle: B.sourceHandle ?? "Done",
      // Connection.targetHandle is string | null; null and undefined are indistinguishable downstream
      // (every consumer checks truthiness), so keep the type-correct null.
      targetHandle: B.targetHandle ?? null
    }, b, { shouldReplaceId: !1 });
    g(Y), U(w, Y);
  }, Oe = (L) => {
    if (a || L.length === 0) return;
    const B = new Set(L.map((Q) => Q.id)), Y = w.filter((Q) => !B.has(Q.id)), re = b.filter((Q) => !B.has(Q.source) && !B.has(Q.target));
    y(Y), g(re), l && B.has(l) && p(null), U(Y, re);
  }, Ne = (L) => {
    if (a || L.length === 0) return;
    const B = new Set(L.map((re) => re.id)), Y = b.filter((re) => !B.has(re.id));
    g(Y), U(w, Y);
  }, Xe = se((L) => {
    if (a) return;
    const B = b.filter((Y) => Y.id !== L);
    g(B), U(w, B);
  }, [U, b, a, w]), $e = se((L, B, Y) => {
    c && S({ kind: "spliceEdge", edgeId: L, clientX: B, clientY: Y });
  }, [c]), Nt = (L) => {
    const B = j;
    if (!B) return;
    S(null);
    const Y = O(B.clientX, B.clientY) ?? { x: 0, y: 0 };
    if (B.kind === "fromEmpty") {
      const Q = H(L, Y), pe = [...w.map((we) => we.selected ? { ...we, selected: !1 } : we), Q.node];
      y(pe), p(Q.node.id), U(pe, b, [Q.activityNode]);
      return;
    }
    if (B.kind === "fromPort") {
      const Q = w.find((Te) => Te.id === B.sourceNodeId), xe = Q ? qc(Q) : Y, pe = H(L, xe), Se = [...w.map((Te) => Te.selected ? { ...Te, selected: !1 } : Te), pe.node], qe = [...b, Xi(B.sourceNodeId, pe.node.id, B.sourceHandleId ?? "Done")];
      y(Se), g(qe), p(pe.node.id), U(Se, qe, [pe.activityNode]);
      return;
    }
    const re = b.find((Q) => Q.id === B.edgeId);
    re && J(L, re, Y);
  }, pn = ue(() => ({
    highlightedEdgeId: I,
    deleteEdge: Xe,
    requestInsertActivity: $e
  }), [Xe, I, $e]);
  return {
    nodes: w,
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
    commitLayout: De,
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
const Jc = "elsa-studio:apply-workflow-graph-operation-batch", Qc = "elsa-studio:undo-workflow-graph-operation-batch", jN = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function NN(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const i = DN(e), r = pf(i.state.rootActivity), s = /* @__PURE__ */ new Map(), a = /* @__PURE__ */ new Map(), c = [];
  for (const u of t.operations) {
    const l = _N(u.kind), d = u.parameters ?? {};
    if (l === "add-activity") {
      const f = Le(d.activityId) ?? u.temporaryReferences?.[0], p = AN(f ?? Le(d.displayName) ?? Le(d.activityType) ?? "weaver-activity", r), h = SN(u, p, n);
      a.set(p, h), c.push(p), f && s.set(f, p), i.state.rootActivity && CN(i.state.rootActivity, h);
      const m = bt(d.position) ? Zo(d.position, { x: 280, y: 160 }) : null;
      m && (i.layout = el(i.layout, p, m));
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
      i.layout = el(i.layout, f, Zo(d, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const f = Ao(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity property target.");
      IN(f, Le(d.propertyName) ?? "Value", d.value ?? "");
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
      i.state.rootActivity = ff(i.state.rootActivity, f), i.layout = i.layout.filter((p) => p.nodeId !== f);
      continue;
    }
    if (l === "connect-activities") {
      kN(i, d, s);
      continue;
    }
    if (l === "disconnect-activities") {
      EN(i, d, s);
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
function SN(e, t, n) {
  const i = e.parameters ?? {}, r = Le(i.activityVersionId) ?? Le(i.activityType) ?? "Elsa.Workflows.Activity", s = n.find((a) => a.activityVersionId === r || a.activityTypeKey === r || a.displayName === Le(i.displayName));
  return s ? Ki(s, t) : {
    nodeId: t,
    // catalogItem is provably undefined here (the truthy branch returned above), so use the id directly.
    activityVersionId: r,
    inputs: [],
    outputs: [],
    ...Le(i.displayName) ? { displayName: Le(i.displayName) } : {},
    designer: { position: Zo(i.position, { x: 280, y: 160 }) }
  };
}
function CN(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = Hs(e);
  n && !n.some((i) => i.nodeId === t.nodeId) && n.push(t);
}
function kN(e, t, n) {
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
function EN(e, t, n) {
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
function IN(e, t, n) {
  const i = bt(n);
  e[Xl(t)] = {
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
  for (const n of hf(e)) {
    const i = Os(n, t);
    if (i) return i;
  }
  return null;
}
function ff(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = Hs(e);
  if (n) {
    const i = n.map((r) => ff(r, t)).filter((r) => !!r);
    n.splice(0, n.length, ...i);
  }
  return e;
}
function pf(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of hf(e)) pf(n, t);
  return t;
}
function hf(e) {
  return Hs(e) ?? [];
}
function Hs(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function el(e, t, n) {
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
function AN(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let i = n, r = 2;
  for (; t.has(i); )
    i = `${n}-${r}`, r += 1;
  return t.add(i), i;
}
function _N(e) {
  return typeof e == "number" ? jN[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Le(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function DN(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function bt(e) {
  return typeof e == "object" && e !== null;
}
function $N({
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
        const p = An(e), h = NN(e, d.batch, n), m = `weaver-batch-${Date.now()}`;
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
    return window.addEventListener(Jc, c), window.addEventListener(Qc, u), () => {
      window.removeEventListener(Jc, c), window.removeEventListener(Qc, u);
    };
  }, [n, t, e, i, r, s]);
}
function TN({ context: e, draft: t, autosaveEnabledByDefault: n = !0, editDraft: i, setStatus: r, setError: s }) {
  const [a, c] = K(n), u = oe(""), l = oe(0), d = oe(Promise.resolve()), f = se((h) => {
    u.current = h ? We(h) : "";
  }, []), p = se(async (h, m) => {
    const v = async () => {
      const y = ++l.current, b = We(h);
      s("");
      try {
        const g = await kh(e, h), x = We(g);
        return u.current = x, i(({ draft: N }) => !N || N.id !== g.id ? null : We(N) === b ? g : { ...N, validationErrors: g.validationErrors }), y === l.current && r(m), g;
      } catch (g) {
        throw y === l.current && (r(""), s(g instanceof Error ? g.message : String(g))), g;
      }
    }, w = d.current.then(v, v);
    return d.current = w.catch(() => {
    }), w;
  }, [e, i, r, s]);
  return ee(() => {
    if (!a || !t || We(t) === u.current) return;
    r("Autosaving...");
    const m = window.setTimeout(() => {
      p(t, "Autosaved").catch(() => {
      });
    }, dj);
    return () => window.clearTimeout(m);
  }, [a, t, p, r]), { saveDraft: p, autosaveEnabled: a, setAutosaveEnabled: c, markSaved: f };
}
function PN({ context: e, definitionId: t, resetHistory: n, loadDraft: i, markSaved: r, setError: s }) {
  const [a, c] = K(null), [u, l] = K([]), [d, f] = K([]), [p, h] = K(null), [m, v] = K(Ri), [w, y] = K("loading"), b = se(async () => {
    s(""), y("loading");
    const [g, x, N, j, S] = await Promise.all([
      yh(e, t),
      ur(e),
      zh(e).then(
        (_) => ({ ok: !0, descriptors: _ }),
        () => ({ ok: !1, descriptors: [] })
      ),
      Vh(e).then(
        (_) => ({ ok: !0, descriptors: _ }),
        () => ({ ok: !1, descriptors: Ri })
      ),
      // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
      Vl(e).then(
        (_) => _,
        () => null
      )
    ]), I = g.draft ?? null;
    c(g), r(I), n(I), i(I), l(x.activities ?? []), f(N.descriptors), h(S), v(j.descriptors.length > 0 ? j.descriptors : Ri), y(N.ok ? "ready" : "failed");
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
    descriptorStatus: w,
    reload: b
  };
}
function MN({ context: e, details: t, setDetails: n, setStatus: i }) {
  const r = oe(null), s = oe(null), a = oe({});
  ee(() => {
    r.current = t;
  }, [t]);
  const c = se(() => {
    s.current !== null && (window.clearTimeout(s.current), s.current = null);
    const l = a.current;
    a.current = {};
    const d = r.current?.definition;
    !d || l.name === void 0 && l.description === void 0 || Ch(e, d.id, {
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
function RN({
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
    Fb(Ob(t, b), b), d("Exported workflow as JSON.");
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
  }, [t, i, r, l, d]), w = se(async () => {
    if (!(!t || i)) {
      l("promoting"), d("Saving...");
      try {
        await r(t, "Saved"), d("Promoting...");
        const b = await Eh(e, t.id), g = await Ih(e, b.versionId);
        u(g.artifactId), d(`Published ${g.artifactVersion}`), await s();
      } catch (b) {
        d(""), f(b instanceof Error ? b.message : String(b));
      } finally {
        l("idle");
      }
    }
  }, [t, i, e, r, s, u, l, d, f]), y = se(async () => {
    if (!t?.state.rootActivity || i) return;
    const b = t, g = We(b);
    c(), d("Preparing test run...");
    try {
      l("testRunPreparing"), d("Preparing test run...");
      const x = Vj(b);
      l("testRunStarting"), d("Starting test run...");
      const N = await Ah(e, {
        definitionId: b.definitionId,
        snapshotId: x,
        state: b.state
      });
      a({ draftSignature: g, view: N }), p("runtime"), h(!1), d(Ls(N) ? "Test run rejected" : "Test run dispatched");
    } catch (x) {
      d(""), f(x instanceof Error ? x.message : String(x));
    } finally {
      l("idle");
    }
  }, [t, i, e, c, a, p, h, l, d, f]);
  return { exportJson: m, save: v, promoteAndPublish: w, run: y };
}
function LN({
  context: e,
  draft: t,
  frames: n,
  selectedNodeId: i,
  catalog: r,
  activityDescriptors: s,
  availabilityDiagnostics: a
}) {
  const c = t?.state.rootActivity ?? null, u = ue(() => new Map(r.map((k) => [k.activityVersionId, k])), [r]), l = se(
    (k) => ag([k.activityVersionId, k.activityTypeKey], a),
    [a]
  ), d = ue(() => Aj(s), [s]), f = ue(() => jl(c, n, u), [c, n, u]), p = Il(f, f ? u.get(f.activityVersionId) : void 0), h = !!f && p === "unsupported", m = ue(() => h ? null : Ln(c, n, u), [c, n, u, h]), v = ue(() => h && f?.nodeId === i ? f : m?.slot.activities.find((k) => k.nodeId === i) ?? null, [h, m, f, i]), w = ue(
    () => v ? Bc(v, u, d) : null,
    [u, d, v]
  ), y = v ? Ae(v, u) : [], b = v ?? f, g = !v && !!f, x = ue(
    () => b ? Bc(b, u, d) : null,
    [u, d, b]
  ), N = ue(
    () => b ? l({ activityVersionId: b.activityVersionId, activityTypeKey: u.get(b.activityVersionId)?.activityTypeKey }) : null,
    [l, u, b]
  ), j = b ? Ae(b, u) : [], S = b ? rh(b, u.get(b.activityVersionId)) : !1, I = wh(e, t?.state, v?.nodeId ?? null, u), _ = !h && m?.slot.mode === "flowchart";
  return {
    catalogByVersion: u,
    availabilityLookup: l,
    scopeOwner: f,
    isUnsupportedDesigner: h,
    scope: m,
    selectedNode: v,
    selectedDescriptor: w,
    selectedSlots: y,
    inspectedNode: b,
    inspectedIsScopeOwner: g,
    inspectedDescriptor: x,
    inspectedNodeAvailability: N,
    inspectedSlots: j,
    inspectedSupportsScopedVariables: S,
    scopedVariableAnalysis: I,
    isFlowchartDesigner: _,
    canAddActivitiesToCanvas: !c || !h
  };
}
function zN({
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
        revision: Rj(t),
        selectedNodeId: i,
        selectedActivityType: r?.typeName ?? (n ? s.get(n.activityVersionId)?.activityTypeKey ?? n.activityVersionId : null),
        summary: e.definition.name,
        activities: rf(t.state.rootActivity, s),
        connections: of(t.state.rootActivity, s),
        diagnostics: t.validationErrors.map((a) => ({ severity: a.code ?? "warning", message: a.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === e.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [s, e, t, r, n, i]);
}
function VN({
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
      /* @__PURE__ */ o.jsx(sr, { size: 14, "aria-hidden": "true" }),
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
              f ? /* @__PURE__ */ o.jsx(dl, { size: 14 }) : /* @__PURE__ */ o.jsx(xt, { size: 14 }),
              /* @__PURE__ */ o.jsx("span", { children: d.category }),
              /* @__PURE__ */ o.jsx("small", { children: d.activities.length })
            ]
          }
        ),
        f ? /* @__PURE__ */ o.jsx("div", { className: "wf-palette-activities", role: "group", children: d.activities.map((p) => {
          const h = p.description?.trim(), m = h ? `wf-palette-description-${p.activityVersionId}` : void 0, v = ke(p), w = un(p);
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
              onDragStart: (y) => a(y, p),
              onDragEnd: (y) => c(y, p),
              onPointerDown: (y) => u(y, p),
              children: [
                /* @__PURE__ */ o.jsx("span", { className: "wf-activity-icon", "data-icon": w, "aria-hidden": "true", children: fr(w) }),
                /* @__PURE__ */ o.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ o.jsx("strong", { children: v }),
                  h ? /* @__PURE__ */ o.jsx("small", { id: m, children: h }) : null
                ] }),
                /* @__PURE__ */ o.jsx(fl, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            p.activityVersionId
          );
        }) }) : null
      ] }, d.category);
    }) })
  ] });
}
const gf = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]), ON = "Variable";
function HN({
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
  const d = UN(l), f = r.length > 0 ? r : cg;
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ o.jsx("span", { className: "wf-section-label", children: "Properties" }),
    d.map((p) => /* @__PURE__ */ o.jsxs("section", { className: "wf-property-group", children: [
      d.length > 1 ? /* @__PURE__ */ o.jsx("h4", { children: p.category }) : null,
      p.inputs.map((h) => /* @__PURE__ */ o.jsx(
        WN,
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
function WN({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: i,
  expressionDescriptors: r,
  visibleVariables: s,
  scopeStatus: a,
  onChange: c
}) {
  const u = t.isReadOnly === !0, l = { activity: e, expressionDescriptors: r, readOnly: u }, d = es(n, t, l), f = d?.component, p = t.isWrapped !== !1 ? ql(e, t) : null, h = p?.expression.type ?? "Literal", m = fg(e, t), v = h.toLowerCase(), y = p && (v === "literal" || v === "object") && !mg(t) ? hg(t.typeName) : null, b = y ? es(n, t, { ...l, scope: "collection" }) : void 0, g = p ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: r,
    readOnly: u,
    surface: "inline",
    syntax: h
  } : null, x = g ? yf(i, g) : null, N = x?.surfaces.inline, j = x && g ? xf(x, g, m) : [], S = y != null, I = !!(p && !S && ZN(t, d?.id)), _ = !!(p && !S && GN(t, d?.id)), [M, k] = K(!1), T = (E) => {
    const D = p ? ug(p, E) : E;
    c(xa(e, t, D));
  }, z = (E) => {
    p && c(xa(e, t, dg(p, E)));
  }, C = y ? b ? Go(b.component, t, m, u, { ...l, scope: "collection" }, T) : /* @__PURE__ */ o.jsx(
    BN,
    {
      input: t,
      elementTypeName: y.elementTypeName,
      value: m,
      editors: n,
      context: l,
      disabled: u,
      onChange: T
    }
  ) : null, A = h === ON && p ? /* @__PURE__ */ o.jsx(
    qN,
    {
      value: m,
      visibleVariables: s,
      scopeStatus: a,
      disabled: u,
      onChange: T
    }
  ) : C ?? (N && g ? /* @__PURE__ */ o.jsx(
    N,
    {
      descriptor: t,
      syntax: h,
      value: m,
      disabled: u,
      context: g,
      onChange: T
    }
  ) : Go(f, t, m, u, l, T));
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
      KN,
      {
        input: t,
        value: m,
        syntax: h,
        descriptors: r,
        activity: e,
        expressionEditors: i,
        disabled: u,
        onChange: T,
        onSyntaxChange: z,
        onClose: () => k(!1)
      }
    ) : null
  ] });
}
function FN(e, t, n) {
  return [
    "wf-collection-item",
    t === e ? "dragging" : "",
    t !== null && t !== e && n === e ? "drop-target" : ""
  ].filter(Boolean).join(" ");
}
function BN({
  input: e,
  elementTypeName: t,
  value: n,
  editors: i,
  context: r,
  disabled: s,
  onChange: a
}) {
  const c = yg(n), u = wg(e, t), l = { ...r, scope: "element" }, d = es(i, u, l)?.component, f = e.displayName || e.name, p = (N, j) => a(c.map((S, I) => I === N ? j : S)), [h, m] = K(null), [v, w] = K(null), y = () => {
    m(null), w(null);
  }, b = (N) => (j) => {
    m(N), j.dataTransfer.effectAllowed = "move", j.dataTransfer.setData("text/plain", String(N));
  }, g = (N) => (j) => {
    h !== null && (j.preventDefault(), j.dataTransfer.dropEffect = "move", v !== N && w(N));
  }, x = (N) => (j) => {
    j.preventDefault(), h !== null && h !== N && a(ho(c, h, N)), y();
  };
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-collection-editor", children: [
    c.length === 0 ? /* @__PURE__ */ o.jsx("p", { className: "wf-collection-empty", children: "No items yet." }) : /* @__PURE__ */ o.jsx("ul", { className: "wf-collection-items", children: c.map((N, j) => /* @__PURE__ */ o.jsxs(
      "li",
      {
        className: FN(j, h, v),
        onDragOver: g(j),
        onDrop: x(j),
        children: [
          /* @__PURE__ */ o.jsx(
            "span",
            {
              className: "wf-collection-item-handle",
              draggable: !s,
              "aria-label": `Drag ${f} item ${j + 1} to reorder`,
              title: "Drag to reorder",
              onDragStart: b(j),
              onDragEnd: y,
              children: /* @__PURE__ */ o.jsx(fl, { size: 13, "aria-hidden": "true" })
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
                children: /* @__PURE__ */ o.jsx(Wf, { size: 13 })
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
                children: /* @__PURE__ */ o.jsx(dl, { size: 13 })
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
                children: /* @__PURE__ */ o.jsx(Tn, { size: 13 })
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
        onClick: () => a([...c, xg(t)]),
        children: [
          /* @__PURE__ */ o.jsx(Gt, { size: 13 }),
          " Add item"
        ]
      }
    )
  ] });
}
function KN({
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
  const d = cl(), f = e.displayName || e.name, p = {
    activity: r,
    descriptor: e,
    expressionDescriptors: i,
    readOnly: a,
    surface: "expanded",
    syntax: n
  }, h = yf(s, p), m = h?.surfaces.expanded, v = h ? xf(h, p, t) : [], w = m ? null : YN(s, p);
  return ee(() => {
    const y = (b) => {
      b.key === "Escape" && l();
    };
    return window.addEventListener("keydown", y), () => window.removeEventListener("keydown", y);
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
        w ? /* @__PURE__ */ o.jsx("p", { className: "wf-expression-editor-hint", children: w }) : null,
        /* @__PURE__ */ o.jsx(
          "textarea",
          {
            "aria-label": `${f} expanded value`,
            value: t == null ? "" : String(t),
            disabled: a,
            spellCheck: !1,
            onChange: (y) => c(y.target.value)
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
  const [a, c] = K(!1), u = cl(), l = n.find((f) => f.type === t), d = [
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
function mf(e) {
  return !e || e === Yi ? Yi : e;
}
function tl(e, t) {
  return `${mf(t)}${Qo}${e}`;
}
function XN(e) {
  const t = e.indexOf(Qo);
  if (t < 0) return null;
  const n = e.slice(t + Qo.length);
  return n ? { scopeId: e.slice(0, t), referenceKey: n } : null;
}
function qN({ value: e, visibleVariables: t, scopeStatus: n, disabled: i, onChange: r }) {
  const s = Rl(e), a = !!e && typeof e == "object" || typeof e == "string" && e.trim().startsWith("{"), c = s && (a || t.some((d) => d.referenceKey === s.referenceKey)) ? s : null, u = c ? tl(c.referenceKey, c.declaringScopeId) : "", l = !!c && t.some(
    (d) => d.referenceKey === c.referenceKey && d.scopeId === mf(c.declaringScopeId)
  );
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-variable-picker", children: [
    /* @__PURE__ */ o.jsxs(
      "select",
      {
        "aria-label": "Variable reference",
        value: u,
        disabled: i,
        onChange: (d) => {
          const f = XN(d.target.value);
          f && r(sh(f.referenceKey, f.scopeId));
        },
        children: [
          /* @__PURE__ */ o.jsx("option", { value: "", children: "Select a variable…" }),
          c && !l ? /* @__PURE__ */ o.jsxs("option", { value: u, children: [
            c.referenceKey,
            " (not visible from this scope)"
          ] }) : null,
          t.map((d) => {
            const f = tl(d.referenceKey, d.scopeId);
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
function yf(e, t) {
  return [...e].sort((n, i) => (n.order ?? 500) - (i.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function xf(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function YN(e, t) {
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
function UN(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const i = n.category?.trim() || "General";
    t.set(i, [...t.get(i) ?? [], n]);
  }
  return [...t.entries()].map(([n, i]) => ({ category: n, inputs: i }));
}
function ZN(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !gf.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function GN(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !gf.has(t) && n !== "multiline") return !1;
  const i = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(i) || n === "singleline" || n === "multiline";
}
function JN({
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
  onSelectedActivityChange: w,
  onEnterSlot: y,
  onReplaceSlotActivity: b
}) {
  const [g, x] = K(null);
  return g && g.nodeId !== t?.nodeId && x(null), t ? /* @__PURE__ */ o.jsxs("div", { className: "wf-inspector-content", children: [
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
      /* @__PURE__ */ o.jsx($n, { size: 14 }),
      /* @__PURE__ */ o.jsxs("span", { children: [
        "No longer available for new use · ",
        zn(s.state)
      ] })
    ] }) : null,
    /* @__PURE__ */ o.jsx(
      HN,
      {
        activity: t,
        descriptor: r,
        editors: f,
        expressionEditors: p,
        expressionDescriptors: h,
        descriptorStatus: m,
        visibleVariables: v.visibleVariables,
        scopeStatus: v.status,
        onChange: w
      }
    ),
    d ? /* @__PURE__ */ o.jsx("div", { className: "wf-container-variables", children: /* @__PURE__ */ o.jsx(
      aj,
      {
        context: e,
        variables: Ml(t),
        title: "Container variables",
        addLabel: "Add container variable",
        emptyLabel: "No container variables declared on this activity.",
        warnings: ch(v.shadowingWarnings, t.nodeId),
        onChange: (N) => w(oh(t, N))
      }
    ) }) : null,
    a.length > 0 ? /* @__PURE__ */ o.jsxs("div", { className: "wf-slot-list", children: [
      /* @__PURE__ */ o.jsx("span", { children: "Embedded slots" }),
      a.map((N) => {
        const j = `${n} / ${N.label}`;
        return /* @__PURE__ */ o.jsxs("div", { className: "wf-slot-row", children: [
          /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => y(t.nodeId, N, j), children: [
            N.label,
            /* @__PURE__ */ o.jsx("small", { children: zj(N, l) })
          ] }),
          N.cardinality === "single" ? /* @__PURE__ */ o.jsx(
            "button",
            {
              type: "button",
              className: "wf-slot-change",
              "aria-label": `${N.activities.length > 0 ? "Change" : "Choose"} ${N.label} activity`,
              title: N.activities.length > 0 ? "Change activity" : "Choose activity",
              onClick: (S) => x({ nodeId: t.nodeId, slotId: N.id, clientX: S.clientX, clientY: S.clientY }),
              children: /* @__PURE__ */ o.jsx(Ff, { size: 14 })
            }
          ) : null
        ] }, N.id);
      })
    ] }) : /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." }),
    g ? /* @__PURE__ */ o.jsx(
      lf,
      {
        clientX: g.clientX,
        clientY: g.clientY,
        activities: u,
        onPick: (N) => {
          x(null);
          const j = a.find((S) => S.id === g.slotId);
          j && b(t.nodeId, j, `${n} / ${j.label}`, N);
        },
        onClose: () => x(null)
      }
    ) : null
  ] }) : /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
}
const wf = [
  { value: "flowchart", label: "Flowchart", hint: "Free-form graph of connected activities." },
  { value: "sequence", label: "Sequence", hint: "Ordered list of activities that run top to bottom." }
];
function vf({ label: e, hint: t }) {
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsx("span", { className: "wf-root-card-title", children: e }),
    /* @__PURE__ */ o.jsx("span", { className: "wf-root-card-hint", children: t })
  ] });
}
function QN({ value: e, onChange: t }) {
  return /* @__PURE__ */ o.jsx("div", { className: "wf-root-cards", role: "radiogroup", "aria-label": "Root activity", children: wf.map((n) => {
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
      /* @__PURE__ */ o.jsx(vf, { label: n.label, hint: n.hint })
    ] }, n.value);
  }) });
}
function eS({ onPick: e }) {
  return /* @__PURE__ */ o.jsx("div", { className: "wf-root-cards", role: "group", "aria-label": "Fill this slot", children: wf.map((t) => /* @__PURE__ */ o.jsx(
    "button",
    {
      type: "button",
      className: "wf-root-card",
      "aria-label": t.label,
      onClick: () => e(t.value),
      children: /* @__PURE__ */ o.jsx(vf, { label: t.label, hint: t.hint })
    },
    t.value
  )) });
}
function tS({ slotLabel: e, catalog: t, onPickActivity: n, onBrowseAll: i }) {
  const r = (s) => {
    const a = Gd(t, s);
    a && n(a);
  };
  return /* @__PURE__ */ o.jsx("div", { className: "wf-slot-empty", role: "group", "aria-label": `Fill ${e}`, children: /* @__PURE__ */ o.jsxs("div", { className: "wf-slot-empty-card", children: [
    /* @__PURE__ */ o.jsx("p", { className: "wf-slot-empty-title", children: "This slot is empty" }),
    /* @__PURE__ */ o.jsxs("p", { className: "wf-slot-empty-hint", children: [
      "Choose a container for ",
      /* @__PURE__ */ o.jsx("strong", { children: e }),
      ", or pick any activity."
    ] }),
    /* @__PURE__ */ o.jsx(eS, { onPick: r }),
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
function nS({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: i,
  expressionEditors: r,
  workflowDesignerPanels: s,
  autosaveEnabledByDefault: a,
  onBack: c
}) {
  const u = dN(), { draft: l, frames: d, selectedNodeId: f, testRun: p, publishedArtifactId: h } = u.state, {
    loadDraft: m,
    replaceDraftByBatch: v,
    editDraft: w,
    editDraftAndSelect: y,
    select: b,
    navigateToScope: g,
    resetToRoot: x,
    startTestRun: N,
    clearTestRun: j,
    setPublishedArtifact: S
  } = u, [I, _] = K(""), [M, k] = K(""), [T, z] = K("idle"), [C, A] = K(() => /* @__PURE__ */ new Set()), [E, D] = K(""), [P, $] = K("activities"), [F, W] = K("inspector"), [H, U] = K("designer"), {
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
  } = df(), { resetHistory: ye, undo: Ie, redo: De, canUndoNow: Ve, canRedoNow: at } = cN({ draft: l, restoreDraft: m }), { saveDraft: Ye, autosaveEnabled: Ue, setAutosaveEnabled: Me, markSaved: Oe } = TN({ context: e, draft: l, autosaveEnabledByDefault: a, editDraft: w, setStatus: k, setError: _ }), {
    details: Ne,
    setDetails: Xe,
    catalog: $e,
    activityDescriptors: Nt,
    availabilityDiagnostics: pn,
    expressionDescriptors: L,
    descriptorStatus: B,
    reload: Y
  } = PN({ context: e, definitionId: t, resetHistory: ye, loadDraft: m, markSaved: Oe, setError: _ }), { updateDefinitionMeta: re } = MN({ context: e, details: Ne, setDetails: Xe, setStatus: k }), {
    catalogByVersion: Q,
    availabilityLookup: xe,
    scopeOwner: pe,
    isUnsupportedDesigner: we,
    scope: Se,
    selectedNode: qe,
    selectedDescriptor: Te,
    selectedSlots: kr,
    inspectedNode: tt,
    inspectedIsScopeOwner: Er,
    inspectedDescriptor: si,
    inspectedNodeAvailability: Ir,
    inspectedSlots: Ar,
    inspectedSupportsScopedVariables: _r,
    scopedVariableAnalysis: Dr,
    isFlowchartDesigner: ct,
    canAddActivitiesToCanvas: $r
  } = LN({ context: e, draft: l, frames: d, selectedNodeId: f, catalog: $e, activityDescriptors: Nt, availabilityDiagnostics: pn }), Vt = ue(() => Yo($e), [$e]), Tr = ue(() => {
    const Z = E.trim().toLowerCase();
    if (!Z) return Vt;
    const ce = $e.filter((me) => ke(me).toLowerCase().includes(Z) || me.activityTypeKey.toLowerCase().includes(Z) || (me.category ?? "").toLowerCase().includes(Z) || (me.description ?? "").toLowerCase().includes(Z));
    return Yo(ce);
  }, [$e, E, Vt]), Ot = T !== "idle", Pr = !!l?.state.rootActivity && !Ot, ai = Rt(n, "weaver.workflows.find-draft-risks"), ci = Rt(n, "weaver.workflows.propose-update"), Mr = bN({
    draft: l,
    scope: Se,
    scopeOwner: pe,
    frames: d,
    catalog: $e,
    catalogByVersion: Q,
    isUnsupportedDesigner: we,
    isFlowchartDesigner: ct,
    canAddActivitiesToCanvas: $r,
    selectedNodeId: f,
    editDraft: w,
    editDraftAndSelect: y,
    select: b,
    resetToRoot: x,
    setStatus: k,
    setError: _
  }), {
    nodes: Ht,
    edges: li,
    canvasRef: ui,
    setReactFlowInstance: di,
    connectMenu: hn,
    setConnectMenu: Rr,
    edgeActions: Lr,
    onNodesChange: zr,
    onEdgesChange: Vr,
    onNodesDelete: Or,
    onEdgesDelete: fi,
    isValidConnection: Hr,
    onConnect: Wr,
    onConnectStart: Fr,
    onConnectEnd: Br,
    onReconnect: Kr,
    commitLayout: Xr,
    canAutoLayout: qr,
    autoLayout: Yr,
    onCanvasDragOver: gn,
    onCanvasDragLeave: pi,
    onCanvasDrop: hi,
    openEmptyConnectMenu: gi,
    onConnectMenuPick: Ur,
    addActivity: mi,
    onPaletteClick: Zr,
    onPaletteDragStart: Gr,
    onPaletteDragEnd: Jr,
    onPalettePointerDown: yi
  } = Mr, Qr = !we && !ct && d.length > 0 && !!Se && Se.slot.activities.length === 0 && Ht.length === 0, xi = se((Z) => {
    const ce = mi(Z);
    if (!ce || !Se || !pe || d.length === 0) return;
    const me = d[d.length - 1].label, Ce = uo(d, pe, pe.nodeId, { ...Se.slot, activities: [ce] }, me, Q);
    Ce && Ce.frames.length > d.length && g(Ce.frames, Ce.selectedNodeId);
  }, [mi, Q, d, g, Se, pe]);
  $N({ draft: l, details: Ne, catalog: $e, replaceDraftByBatch: v, setStatus: k, setError: _ }), ee(() => {
    !l?.state.rootActivity || $e.length === 0 || w(({ draft: Z }) => {
      if (!Z?.state.rootActivity) return null;
      const ce = Al(Z.state.rootActivity, Q);
      return !ce || ce === Z.state.rootActivity ? null : {
        ...Z,
        state: {
          ...Z.state,
          rootActivity: ce
        }
      };
    });
  }, [$e.length, Q, l?.state.rootActivity, w]), zN({ details: Ne, draft: l, selectedNode: qe, selectedNodeId: f, selectedDescriptor: Te, catalogByVersion: Q }), ee(() => {
    A((Z) => {
      let ce = !1;
      const me = new Set(Z);
      for (const Ce of Vt)
        me.has(Ce.category) || (me.add(Ce.category), ce = !0);
      return ce ? me : Z;
    });
  }, [Vt]);
  const { exportJson: eo, save: to, promoteAndPublish: mn, run: no } = RN({
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
  }), io = se((Z) => {
    w(({ draft: ce }) => ce ? { ...ce, state: Z(ce.state) } : null);
  }, [w]), wi = se((Z) => {
    if (!l) return "No draft is loaded.";
    const ce = Wb(Z, l);
    return ce.ok ? (m(ce.draft), k("Applied workflow JSON."), null) : ce.error;
  }, [l, m]);
  ee(() => {
    const Z = (ce) => {
      if (H !== "designer" || !(ce.metaKey || ce.ctrlKey)) return;
      const me = ce.target;
      if (me && (me.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(me.tagName))) return;
      const Ce = ce.key.toLowerCase();
      Ce === "z" && !ce.shiftKey ? (ce.preventDefault(), Ie()) : (Ce === "z" && ce.shiftKey || Ce === "y") && (ce.preventDefault(), De());
    };
    return window.addEventListener("keydown", Z), () => window.removeEventListener("keydown", Z);
  }, [H, Ie, De]);
  const ro = se((Z, ce, me) => {
    const Ce = uo(d, pe, Z, ce, me, Q);
    Ce && g(Ce.frames, Ce.selectedNodeId);
  }, [Q, d, g, pe]), jf = se((Z, ce, me, Ce) => {
    const bi = ce.activities.length > 0, Ys = Ki(Ce, Uo(Ce));
    w(({ draft: ji }) => {
      const Us = ji?.state.rootActivity;
      return !ji || !Us ? null : {
        ...ji,
        state: {
          ...ji.state,
          rootActivity: To(Us, Z, (Df) => ln(Df, ce, [Ys]), Q)
        }
      };
    });
    const ao = uo(d, pe, Z, { ...ce, activities: [Ys] }, me, Q);
    ao && g(ao.frames, ao.selectedNodeId), _(""), k(bi ? `Replaced ${ce.label} content` : `Assigned ${ke(Ce)} to ${ce.label}`);
  }, [Q, w, d, g, pe]), Nf = ue(() => we ? null : (Z, ce, me) => ro(Z, me, `${ce} / ${me.label}`), [ro, we]), Sf = se((Z) => {
    w(({ draft: ce }) => {
      const me = ce?.state.rootActivity;
      return !ce || !me ? null : {
        ...ce,
        state: {
          ...ce.state,
          rootActivity: To(me, Z.nodeId, () => Z, Q)
        }
      };
    });
  }, [Q, w]), Cf = se((Z) => {
    if (!Z) return;
    const ce = l?.state.rootActivity;
    if (!ce) return;
    const me = Ep(ce, Z, (Ce) => {
      const bi = Q.get(Ce.activityVersionId);
      return bi ? ke(bi) : Ce.nodeId;
    }, Q);
    me && (U("designer"), g(me, Z), X(!1));
  }, [l?.state.rootActivity, Q, X, g]), kf = (Z) => {
    A((ce) => {
      const me = new Set(ce);
      return me.has(Z) ? me.delete(Z) : me.add(Z), me;
    });
  };
  if (!Ne || !l)
    return /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: I || "Loading workflow editor..." });
  const Fs = tt ? Q.get(tt.activityVersionId) : void 0, Ef = tt ? Ht.find((Z) => Z.id === tt.nodeId)?.data.label ?? (Fs ? ke(Fs) : tt.nodeId) : "", vi = p?.draftSignature === We(l) ? p.view : null, Bs = vi && M.startsWith("Test run") ? "" : M, If = (Z) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(Z)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, Af = {
    definition: Ne.definition,
    draft: l,
    selectedActivity: qe,
    selectedActivityDescriptor: Te,
    selectedActivitySlots: kr,
    catalog: $e,
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
      render: () => /* @__PURE__ */ o.jsx(ce, { context: Af })
    };
  }), oo = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ o.jsx(Zn, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(
        VN,
        {
          paletteSearch: E,
          onSearchChange: D,
          groups: Tr,
          expandedCategories: C,
          onToggleCategory: kf,
          onActivityClick: Zr,
          onActivityDragStart: Gr,
          onActivityDragEnd: Jr,
          onActivityPointerDown: yi
        }
      )
    },
    ...Ks.filter((Z) => Z.side === "left")
  ].sort(Yc), so = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ o.jsx(is, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(
        JN,
        {
          context: e,
          selectedNode: tt,
          selectedNodeLabel: Ef,
          selectedActivityType: tt ? si?.typeName ?? Q.get(tt.activityVersionId)?.activityTypeKey ?? "Unknown" : "",
          selectedDescriptor: si,
          selectedNodeAvailability: Ir,
          selectedSlots: Ar,
          inspectingScopeOwner: Er,
          catalog: $e,
          catalogByVersion: Q,
          selectedSupportsScopedVariables: _r,
          propertyEditors: i,
          expressionEditors: r,
          expressionDescriptors: L,
          descriptorStatus: B,
          scopedVariableAnalysis: Dr,
          onSelectedActivityChange: Sf,
          onEnterSlot: ro,
          onReplaceSlotActivity: jf
        }
      )
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: /* @__PURE__ */ o.jsx(Zt, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(Qj, { testRun: vi, onOpenRun: If })
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ o.jsx(pl, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(
        nN,
        {
          context: e,
          ai: n,
          definitionId: Ne.definition.id,
          publishedArtifactId: h
        }
      )
    },
    ...Ks.filter((Z) => Z.side === "right")
  ].sort(Yc), Xs = oo.find((Z) => Z.id === P) ?? oo[0], qs = so.find((Z) => Z.id === F) ?? so[0], _f = [
    { id: "designer", title: "Designer", order: 0, icon: /* @__PURE__ */ o.jsx(hl, { size: 14 }), render: () => null },
    { id: "code", title: "Code", order: 1, icon: /* @__PURE__ */ o.jsx(Yf, { size: 14 }), render: () => null },
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
              children: /* @__PURE__ */ o.jsx(Bf, { size: 16 })
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
              onClick: De,
              children: /* @__PURE__ */ o.jsx(Kf, { size: 16 })
            }
          ),
          /* @__PURE__ */ o.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Auto-layout",
              title: "Auto-layout the canvas",
              disabled: !qr,
              onClick: Yr,
              children: /* @__PURE__ */ o.jsx(Xf, { size: 16 })
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
        /* @__PURE__ */ o.jsxs("button", { type: "button", title: "Export workflow as JSON", onClick: eo, children: [
          /* @__PURE__ */ o.jsx(qf, { size: 15 }),
          " Export"
        ] }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", disabled: Ot, onClick: () => {
          to();
        }, children: [
          /* @__PURE__ */ o.jsx(rs, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", disabled: Ot, onClick: () => {
          mn();
        }, children: [
          /* @__PURE__ */ o.jsx(ll, { size: 15 }),
          " Promote"
        ] }),
        vi ? /* @__PURE__ */ o.jsx(
          Jj,
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
            disabled: !Pr,
            title: l.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              no();
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
            Bi,
            {
              label: "Activities panel tabs",
              tabs: oo,
              activeTabId: Xs.id,
              onSelect: $
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
          Bi,
          {
            label: "Editor view tabs",
            tabs: _f,
            activeTabId: H,
            onSelect: (Z) => U(Z)
          }
        ) }),
        H === "code" ? /* @__PURE__ */ o.jsx(Yb, { draft: l, onApply: wi }) : H === "properties" ? /* @__PURE__ */ o.jsx(lj, { details: Ne, draft: l, context: e, onStateChange: io, onDefinitionMetaChange: re }) : /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
          /* @__PURE__ */ o.jsxs("div", { className: "wf-breadcrumb", children: [
            /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => x(), children: "Root" }),
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
            /* @__PURE__ */ o.jsx(qd.Provider, { value: Lr, children: /* @__PURE__ */ o.jsx(Yd.Provider, { value: xe, children: /* @__PURE__ */ o.jsx(Ud.Provider, { value: Nf, children: /* @__PURE__ */ o.jsxs(
              Ld,
              {
                nodes: Ht,
                edges: li,
                nodeTypes: af,
                edgeTypes: cf,
                onInit: di,
                onNodesChange: zr,
                onEdgesChange: Vr,
                onNodesDelete: Or,
                onEdgesDelete: fi,
                onConnect: Wr,
                onConnectStart: ct ? Fr : void 0,
                onConnectEnd: ct ? Br : void 0,
                onReconnect: ct ? Kr : void 0,
                isValidConnection: Hr,
                onDragOver: gn,
                onDragLeave: pi,
                onDrop: hi,
                onPaneClick: () => b(null),
                onNodeClick: (Z, ce) => b(ce.id),
                onNodeDragStop: we ? void 0 : Xr,
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
                  /* @__PURE__ */ o.jsx(Vd, { gap: 18, size: 1 }),
                  /* @__PURE__ */ o.jsx(Hd, {}),
                  /* @__PURE__ */ o.jsx(Fd, { pannable: !0, zoomable: !0 })
                ]
              }
            ) }) }) }),
            Qr ? /* @__PURE__ */ o.jsx(
              tS,
              {
                slotLabel: Se?.slot.label ?? "this slot",
                catalog: $e,
                onPickActivity: xi,
                onBrowseAll: gi
              }
            ) : ct && Ht.length === 0 ? /* @__PURE__ */ o.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => gi(), children: [
              /* @__PURE__ */ o.jsx(Gt, { size: 15 }),
              " Add activity"
            ] }) : null,
            hn ? /* @__PURE__ */ o.jsx(
              lf,
              {
                clientX: hn.clientX,
                clientY: hn.clientY,
                activities: $e,
                onPick: Ur,
                onClose: () => Rr(null)
              }
            ) : null
          ] }),
          /* @__PURE__ */ o.jsx(Gj, { draft: l, onRepair: Cf })
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
            Bi,
            {
              label: "Inspector panel tabs",
              tabs: so,
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
function iS({ page: e, pageSize: t, totalCount: n, onPageChange: i, onPageSizeChange: r }) {
  const s = Zd(n, t), a = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
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
      /* @__PURE__ */ o.jsx("select", { value: t, onChange: (u) => r(Number(u.target.value)), children: pj.map((u) => /* @__PURE__ */ o.jsx("option", { value: u, children: u }, u)) })
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
function rS({ draft: e, creating: t, ai: n, suggestMetadataAction: i, onChange: r, onClose: s, onSubmit: a }) {
  const [c, u] = K(!1), [l, d] = K(""), [f, p] = K(!1), [h, m] = K(null), [v, w] = K(null), y = oe(null), b = oe(e);
  b.current = e;
  const g = oe(r);
  g.current = r;
  const x = se((j) => {
    const S = { ...b.current };
    j.name && (S.name = j.name), j.description && (S.description = j.description), g.current(S), m(null), w(null);
  }, []);
  ee(() => {
    if (i)
      return n.onPromptResult((j) => {
        if (j.requestId !== y.current) return;
        if (y.current = null, p(!1), j.status !== "completed") {
          w(j.status === "cancelled" ? "Weaver needs more detail — continue in the assistant panel." : "Weaver couldn't generate a suggestion. Try again or fill the fields manually.");
          return;
        }
        const S = xj(j.text);
        if (!S) {
          w("Couldn't read a suggestion from Weaver's reply. See the assistant panel.");
          return;
        }
        j.autoApply ? x(S) : m(S);
      });
  }, [n, i, x]);
  const N = () => {
    if (!i) return;
    const j = i.createPrompt({ draft: b.current, intent: l });
    if (!j) return;
    const S = `wf-suggest-${Date.now()}-${Math.round(Math.random() * 1e6)}`;
    y.current = S, p(!0), m(null), w(null), n.dispatchPrompt({ ...j, requestId: S });
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
              /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => x(h), children: "Apply" }),
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
            QN,
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
function oS({ context: e, ai: t, onOpen: n }) {
  const [i, r] = K(""), [s, a] = K("active"), [c, u] = K(1), [l, d] = K(hj), [f, p] = K("loading"), [h, m] = K(""), [v, w] = K(""), [y, b] = K([]), [g, x] = K(0), [N, j] = K(() => /* @__PURE__ */ new Set()), [S, I] = K(null), [_, M] = K(!1), [k, T] = K([]), [z, C] = K("idle"), A = oe(null), E = ue(() => y.map((V) => V.id), [y]), D = Rt(t, "weaver.workflows.suggest-create-metadata"), P = Rt(t, "weaver.workflows.explain-definition"), $ = E.filter((V) => N.has(V)).length, F = E.length > 0 && $ === E.length, W = se(async () => {
    p("loading"), m("");
    try {
      const V = await mh(e, { search: i, state: s, page: c, pageSize: l }), ne = typeof V.totalCount == "number", ge = V.totalCount ?? V.definitions.length, ye = Zd(ge, l);
      if (ge > 0 && c > ye) {
        u(ye);
        return;
      }
      b(ne ? V.definitions : yj(V.definitions, c, l)), x(ge), p("ready");
    } catch (V) {
      m(V instanceof Error ? V.message : String(V)), p("failed");
    }
  }, [e, i, s, c, l]);
  ee(() => {
    W();
  }, [W]), ee(() => {
    A.current && (A.current.indeterminate = $ > 0 && !F);
  }, [F, $]);
  const H = se(async () => {
    if (!(z === "loading" || z === "ready")) {
      C("loading");
      try {
        const V = await ur(e);
        T(V.activities ?? []), C("ready");
      } catch (V) {
        C("failed"), m(V instanceof Error ? V.message : String(V));
      }
    }
  }, [z, e]), U = () => {
    m(""), w(""), I({ name: "", description: "", rootKind: "flowchart" }), H();
  }, O = async () => {
    if (S?.name.trim()) {
      M(!0), m(""), w("");
      try {
        const V = await bh(e, {
          name: S.name.trim(),
          description: S.description.trim() || null,
          rootKind: S.rootKind,
          rootActivityVersionId: wj(S, k)
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
    if (y.length === 1 && c > 1) {
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
      w(""), m("");
      try {
        await jh(e, V.id), R(V.id, !1), w(`Deleted ${V.name}`), await J();
      } catch (ne) {
        m(ne instanceof Error ? ne.message : String(ne));
      }
    }
  }, ie = async (V) => {
    w(""), m("");
    try {
      await Nh(e, V.id), R(V.id, !1), w(`Restored ${V.name}`), await J();
    } catch (ne) {
      m(ne instanceof Error ? ne.message : String(ne));
    }
  }, fe = async (V) => {
    if (await ea().confirm({ message: `Permanently delete workflow definition "${V.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) {
      w(""), m("");
      try {
        await Sh(e, V.id), R(V.id, !1), w(`Permanently deleted ${V.name}`), await J();
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
        /* @__PURE__ */ o.jsx(sr, { size: 15 }),
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
    f === "ready" && y.length === 0 ? /* @__PURE__ */ o.jsx(
      Vs,
      {
        icon: /* @__PURE__ */ o.jsx(pl, { size: 22 }),
        title: `No ${s} workflow definitions`,
        description: "Create a workflow to start designing automation, or adjust your filters to see more.",
        action: /* @__PURE__ */ o.jsxs("button", { type: "button", className: "wf-link-button", onClick: U, children: [
          /* @__PURE__ */ o.jsx(Gt, { size: 15 }),
          " Create workflow"
        ] })
      }
    ) : null,
    f === "ready" && y.length > 0 ? /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
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
        y.map((V) => /* @__PURE__ */ o.jsxs(
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
                  /* @__PURE__ */ o.jsx(Tn, { size: 13 }),
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
                  /* @__PURE__ */ o.jsx(Tn, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          V.id
        ))
      ] }),
      /* @__PURE__ */ o.jsx(
        iS,
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
      rS,
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
function sS({ activities: e, activityCatalog: t, selectedEvidenceId: n = null, onSelectEvidence: i }) {
  const r = ue(
    () => new Map(t.map((a) => [a.activityTypeKey, a])),
    [t]
  ), s = ue(() => cS(e), [e]);
  return s.length === 0 ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty wf-timeline-empty", children: "No activity executions recorded yet." }) : /* @__PURE__ */ o.jsx("ol", { className: "wf-timeline", "aria-label": "Execution timeline", children: s.map((a) => {
    const c = r.get(a.activityType), u = un(c), l = c ? ke(c) : _t(a.activityType) ?? a.activityType, d = _t(a.activityType) ?? a.activityType, f = lS(a.startedAt ?? a.scheduledAt), p = ms(a.startedAt, a.completedAt);
    return /* @__PURE__ */ o.jsx("li", { children: /* @__PURE__ */ o.jsxs(
      "button",
      {
        type: "button",
        className: "wf-timeline-entry",
        "data-selected": a.activityExecutionId === n,
        onClick: () => i?.(a.activityExecutionId),
        children: [
          /* @__PURE__ */ o.jsx("span", { className: "wf-timeline-icon wf-activity-icon", "data-icon": u, "aria-hidden": "true", children: fr(u) }),
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
          /* @__PURE__ */ o.jsx(aS, { status: a.status })
        ]
      }
    ) }, a.activityExecutionId);
  }) });
}
function aS({ status: e }) {
  const t = e.toLowerCase(), n = t === "completed" ? "OK" : e;
  return /* @__PURE__ */ o.jsx("span", { className: "wf-status-badge wf-timeline-pill", "data-status": t, children: n });
}
function cS(e) {
  return e.map((t, n) => ({ activity: t, index: n })).sort((t, n) => nl(t.activity) - nl(n.activity) || t.index - n.index).map((t) => t.activity);
}
function nl(e) {
  const t = e.startedAt ?? e.scheduledAt, n = t ? Date.parse(t) : Number.NaN;
  return Number.isNaN(n) ? Number.MAX_SAFE_INTEGER : n;
}
function lS(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? "" : t.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: !1 });
}
function uS({ context: e }) {
  const [t, n] = K("loading"), [i, r] = K(""), [s, a] = K(""), [c, u] = K(""), [l, d] = K([]), f = se(async () => {
    n("loading"), r("");
    try {
      const h = await Dh(e, {
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
            /* @__PURE__ */ o.jsx("span", { children: tf(h.runKind) }),
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
function dS({ context: e, ai: t, workflowExecutionId: n }) {
  const [i, r] = K("loading"), [s, a] = K(""), [c, u] = K(null), [l, d] = K(null), [f, p] = K([]), {
    inspectorWidth: h,
    inspectorCollapsed: m,
    maximizedSidePanel: v,
    inspectorExpanded: w,
    editorBodyStyle: y,
    toggleSidePanelCollapsed: b,
    toggleSidePanelMaximized: g,
    startSidePanelResize: x,
    handleSidePanelResizeKeyDown: N
  } = df(), j = Rt(t, "weaver.workflows.explain-instance"), S = se(async () => {
    if (!n) {
      a("No workflow execution id was provided."), r("failed");
      return;
    }
    r("loading"), a("");
    try {
      const k = await $h(e, n), [T, z] = await Promise.all([
        vh(e, k.instance.definitionVersionId).then(
          (C) => ({ definitionVersion: C, error: "" }),
          (C) => ({ definitionVersion: null, error: C instanceof Error ? C.message : String(C) })
        ),
        ur(e)
      ]);
      u({
        details: k,
        definitionVersion: T.definitionVersion,
        definitionVersionError: T.error,
        activityCatalog: z.activities
      }), d(null), p([]), r("ready");
    } catch (k) {
      u(null), a(Hj(k, n)), r("failed");
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
        /* @__PURE__ */ o.jsx(hl, { size: 14 }),
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
    i === "ready" && c ? /* @__PURE__ */ o.jsxs("div", { className: M, style: y, children: [
      /* @__PURE__ */ o.jsx(
        fS,
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
      w && !v ? /* @__PURE__ */ o.jsx(
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
          onPointerDown: (k) => x("inspector", k),
          onKeyDown: (k) => N("inspector", k)
        }
      ) : /* @__PURE__ */ o.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ o.jsx(
        gS,
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
          graphNodeIds: c.definitionVersion ? hS(c.definitionVersion, c.activityCatalog) : void 0,
          rootNodeId: c.definitionVersion?.state.rootActivity?.nodeId,
          collapsed: m,
          expanded: w,
          maximized: v === "inspector",
          onToggleCollapsed: () => b("inspector"),
          onToggleMaximized: () => g("inspector")
        }
      )
    ] }) : null
  ] });
}
function fS({
  definitionVersion: e,
  definitionVersionError: t,
  activityCatalog: n,
  details: i,
  selectedEvidenceId: r,
  onSelectEvidence: s,
  frames: a,
  onNavigateToScope: c
}) {
  const u = pS(a), l = ue(() => {
    if (!e) return { nodes: [], edges: [] };
    const d = e.state.rootActivity;
    if (!d) return { nodes: [], edges: [] };
    const f = Ln(d, a, n), p = f?.owner ?? d, h = n.find((y) => y.activityVersionId === p.activityVersionId), v = Il(p, h) === "unsupported" || !f ? El(p, n, e.layout) : kl(f, n, e.layout), w = v.nodes.map((y) => ({
      ...y,
      draggable: !1,
      connectable: !1,
      deletable: !1,
      data: {
        ...y.data,
        onEnterSlot: (b) => {
          c([...a, {
            ownerNodeId: y.id,
            slotId: b.id,
            label: `${y.data.label} / ${b.label}`
          }]);
        }
      }
    }));
    return {
      nodes: Rp(w, i.activities, i.incidents, r),
      edges: v.edges.map((y) => ({ ...y, deletable: !1 }))
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
      /* @__PURE__ */ o.jsx(fn, { status: i.instance.status, subStatus: i.instance.subStatus })
    ] }),
    e ? /* @__PURE__ */ o.jsxs("div", { className: "wf-breadcrumb wf-instance-breadcrumb", children: [
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => c([]), children: "Root" }),
      a.map((d, f) => /* @__PURE__ */ o.jsxs("span", { className: "wf-breadcrumb-segment", children: [
        /* @__PURE__ */ o.jsx(xt, { size: 13 }),
        /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => c(a.slice(0, f + 1)), children: d.label })
      ] }, `${d.ownerNodeId}-${d.slotId}-${f}`))
    ] }) : null,
    /* @__PURE__ */ o.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ o.jsxs("div", { className: "wf-empty", children: [
        "The workflow run loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ o.jsx("small", { children: Oj(t) }) : null
      ] }),
      e && l.nodes.length === 0 ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      l.nodes.length > 0 ? /* @__PURE__ */ o.jsxs(
        Ld,
        {
          nodes: l.nodes,
          edges: l.edges,
          nodeTypes: af,
          edgeTypes: cf,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (d, f) => s(f.id),
          onPaneClick: () => s(null),
          children: [
            /* @__PURE__ */ o.jsx(Vd, {}),
            /* @__PURE__ */ o.jsx(Fd, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ o.jsx(Hd, {})
          ]
        },
        u
      ) : null
    ] })
  ] });
}
function pS(e) {
  return e.length === 0 ? "root" : e.map((t) => `${t.ownerNodeId}:${t.slotId}`).join("/");
}
function hS(e, t) {
  const n = /* @__PURE__ */ new Set(), i = (r) => {
    if (r) {
      n.add(r.nodeId);
      for (const s of Ae(r, t))
        s.activities.forEach(i);
    }
  };
  return i(e.state.rootActivity), n;
}
function gS({
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
  onToggleMaximized: w
}) {
  const [y, b] = K("timeline");
  if (!i)
    return /* @__PURE__ */ o.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Select a workflow run to inspect its timeline." }) });
  const g = r?.incidents.length ?? 0, x = mS(r?.activities ?? [], c), N = (S) => {
    u?.(S), b("activity");
  }, j = [
    { id: "timeline", title: "Timeline", order: 0, icon: /* @__PURE__ */ o.jsx(is, { size: 14 }), render: () => null },
    { id: "activity", title: "Activity", order: 1, icon: /* @__PURE__ */ o.jsx(ul, { size: 14 }), render: () => null },
    { id: "issues", title: g > 0 ? `Issues (${g})` : "Issues", order: 2, icon: /* @__PURE__ */ o.jsx(jt, { size: 14 }), render: () => null },
    { id: "details", title: "Details", order: 3, icon: /* @__PURE__ */ o.jsx(os, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ o.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Run details panel", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-panel-title wf-instance-panel-title", children: [
      /* @__PURE__ */ o.jsx(Bi, { label: "Run details tabs", tabs: j, activeTabId: y, onSelect: (S) => b(S) }),
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
            onClick: w,
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
      s === "ready" && r ? /* @__PURE__ */ o.jsx("div", { className: "wf-instance-tab-content", children: y === "timeline" ? /* @__PURE__ */ o.jsx(
        sS,
        {
          activities: r.activities,
          activityCatalog: f,
          selectedEvidenceId: c,
          onSelectEvidence: N
        }
      ) : y === "activity" ? /* @__PURE__ */ o.jsx(yS, { context: e, activity: x, activityCatalog: f }) : y === "issues" ? /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
        /* @__PURE__ */ o.jsx(DS, { incidents: r.incidents, selectedEvidenceId: c, onSelectEvidence: u }),
        /* @__PURE__ */ o.jsx(RS, { details: r, graphNodeIds: l, rootNodeId: d })
      ] }) : /* @__PURE__ */ o.jsxs("dl", { className: "wf-instance-meta", children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ o.jsx("dd", { children: /* @__PURE__ */ o.jsx(fn, { status: i.status, subStatus: i.subStatus }) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Run Kind" }),
        /* @__PURE__ */ o.jsx("dd", { children: tf(i.runKind) }),
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
function mS(e, t) {
  if (!t) return null;
  const n = e.find((r) => r.activityExecutionId === t);
  if (n) return n;
  const i = e.filter((r) => r.executableNodeId === t || r.authoredActivityId === t);
  return i.length > 0 ? Pl(i) : null;
}
function yS({ context: e, activity: t, activityCatalog: n }) {
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
    return a({ activityExecutionId: p, status: "loading", inspection: null, error: "" }), Th(e, r, p).then(
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
      il,
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
      il,
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
function il({
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
    /* @__PURE__ */ o.jsx("div", { className: "wf-runtime-input-list", children: a.map((c) => /* @__PURE__ */ o.jsx(xS, { snapshot: c }, `${c.name}:${c.capturedAt}:${c.captureMode}`)) })
  ] });
}
function xS({ snapshot: e }) {
  const t = e.type?.displayName || e.type?.typeName || e.type?.alias || "Unknown", n = e.snapshot ?? (e.captureMode === "DiagnosticSnapshot" ? e.payload : null), i = e.captureMode === "Payload" && e.payload !== void 0;
  return /* @__PURE__ */ o.jsxs("article", { className: "wf-runtime-input", children: [
    /* @__PURE__ */ o.jsxs("header", { children: [
      /* @__PURE__ */ o.jsxs("span", { children: [
        /* @__PURE__ */ o.jsx("strong", { children: e.name }),
        /* @__PURE__ */ o.jsx("small", { children: t })
      ] }),
      /* @__PURE__ */ o.jsx("span", { className: "wf-runtime-capture-mode", children: CS(e.captureMode) })
    ] }),
    IS(n) ? /* @__PURE__ */ o.jsx(Ws, { node: n }) : i ? /* @__PURE__ */ o.jsx(SS, { payload: e.payload }) : /* @__PURE__ */ o.jsx("p", { children: ES(e) }),
    e.isSensitive ? /* @__PURE__ */ o.jsx("p", { className: "wf-instance-note", children: "Marked sensitive by runtime evidence." }) : null
  ] });
}
const wS = {
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
function vS(e) {
  return Object.hasOwn(wS, e.kind);
}
function Ws({ node: e, depth: t = 0 }) {
  if (!vS(e))
    return /* @__PURE__ */ o.jsx(rl, { node: { kind: "unsupported", reason: `Unknown snapshot node: ${e.kind}` } });
  switch (e.kind) {
    case "null":
      return /* @__PURE__ */ o.jsx("code", { className: "wf-runtime-input-value", children: "null" });
    case "scalar":
    case "number":
      return /* @__PURE__ */ o.jsx("code", { className: "wf-runtime-input-value", children: bf(e.value) });
    case "string":
      return /* @__PURE__ */ o.jsxs("code", { className: "wf-runtime-input-value", children: [
        e.preview ?? "",
        e.truncated ? ` (${e.length ?? "unknown"} chars, truncated)` : ""
      ] });
    case "object":
      return /* @__PURE__ */ o.jsx(bS, { node: e, depth: t });
    case "array":
      return /* @__PURE__ */ o.jsx(jS, { node: e, depth: t });
    case "redacted":
    case "truncated":
    case "unsupported":
    case "error":
    case "permissionHidden":
      return /* @__PURE__ */ o.jsx(rl, { node: e });
    case "payloadReference":
      return /* @__PURE__ */ o.jsx(NS, { node: e });
  }
}
function bS({ node: e, depth: t }) {
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
function jS({ node: e, depth: t }) {
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
function rl({ node: e }) {
  const t = e.message || e.reason || e.requiredPermission || e.displayName;
  return /* @__PURE__ */ o.jsxs("span", { className: `wf-runtime-snapshot-marker ${e.kind}`, children: [
    kS(e.kind),
    t ? `: ${t}` : "",
    e.omittedCount ? ` (${e.omittedCount} omitted)` : ""
  ] });
}
function NS({ node: e }) {
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
function SS({ payload: e }) {
  const t = bf(e);
  return t.length <= 160 && !t.includes(`
`) ? /* @__PURE__ */ o.jsx("code", { className: "wf-runtime-input-value", children: t }) : /* @__PURE__ */ o.jsxs("details", { className: "wf-runtime-input-value-details", children: [
    /* @__PURE__ */ o.jsx("summary", { children: AS(t) }),
    /* @__PURE__ */ o.jsx("pre", { children: t })
  ] });
}
function CS(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2");
}
function kS(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2");
}
function ES(e) {
  return e.state === "permissionHidden" ? "Runtime value evidence is hidden by permissions." : e.state === "metadataOnly" ? e.captureReason || "Runtime value evidence is metadata-only." : e.state === "notCaptured" ? e.captureReason || "Runtime value evidence was not captured." : e.captureReason || "The runtime capture policy did not include this value.";
}
function IS(e) {
  return typeof e == "object" && e !== null && typeof e.kind == "string";
}
function AS(e) {
  const t = e.split(`
`, 1)[0] || e;
  return t.length > 120 ? `${t.slice(0, 117)}...` : t;
}
function bf(e) {
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
const _S = [
  "runtime.faultStackTrace",
  "runtime.exceptionStackTrace",
  "runtime.stackTrace",
  "faultStackTrace",
  "exceptionStackTrace",
  "stackTrace"
];
function DS({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
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
              value: LS(s),
              ariaLabel: `Copy incident ${s.failureType}`,
              copiedLabel: "incident",
              onCopied: (a) => r(`Copied ${a}`),
              onCopyFailed: (a) => r(`Could not copy ${a}.`)
            }
          ),
          /* @__PURE__ */ o.jsx($S, { incident: s })
        ]
      },
      s.incidentId
    )),
    i ? /* @__PURE__ */ o.jsx("p", { className: "wf-copy-status", role: "status", children: i }) : null
  ] });
}
function $S({ incident: e }) {
  const t = TS(e);
  return t ? /* @__PURE__ */ o.jsxs("details", { className: "wf-incident-stacktrace", children: [
    /* @__PURE__ */ o.jsx("summary", { children: MS(t) }),
    /* @__PURE__ */ o.jsx("pre", { children: t })
  ] }) : null;
}
function TS(e) {
  const t = PS(e.stackTrace, e.exceptionStackTrace);
  if (t) return t;
  for (const n of _S) {
    const i = e.metadata?.[n];
    if (i && i.trim()) return i;
  }
  return null;
}
function PS(...e) {
  return e.find((t) => t?.trim()) ?? null;
}
function MS(e) {
  const n = (e.split(`
`).find((i) => i.trim()) ?? e).trim();
  return n.length > 120 ? `${n.slice(0, 117)}...` : n;
}
function RS({ details: e, graphNodeIds: t, rootNodeId: n }) {
  if (!t) return null;
  const i = e.activities.filter((r) => {
    const s = $j(r);
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
function LS(e) {
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
function zS({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: i,
  workflowDesignerPanels: r,
  autosaveEnabledByDefault: s
}) {
  const [a, c] = K(ol);
  ee(() => {
    const l = () => c(ol());
    return window.addEventListener("popstate", l), () => window.removeEventListener("popstate", l);
  }, []);
  const u = (l) => {
    const d = l ? `/workflows/definitions?definition=${encodeURIComponent(l)}` : "/workflows/definitions";
    window.history.pushState({}, "", d), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return a ? /* @__PURE__ */ o.jsx(nS, { context: e, definitionId: a, ai: t, propertyEditors: n, expressionEditors: i, workflowDesignerPanels: r, autosaveEnabledByDefault: s, onBack: () => u(null) }) : /* @__PURE__ */ o.jsx(Cr, { title: "Definitions", children: /* @__PURE__ */ o.jsx(oS, { context: e, ai: t, onOpen: u }) });
}
function VS({ context: e, ai: t }) {
  const [n, i] = K(sl);
  ee(() => {
    const s = () => i(sl());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const r = se((s) => {
    const a = s?.trim() ?? "", c = new URL(window.location.href);
    a ? c.searchParams.set("definition", a) : c.searchParams.delete("definition"), i(a || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ o.jsx(Cr, { title: "Executables", children: /* @__PURE__ */ o.jsx(eN, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: r }) });
}
function OS({ context: e }) {
  return /* @__PURE__ */ o.jsx(Cr, { title: "Runs", children: /* @__PURE__ */ o.jsx(uS, { context: e }) });
}
function HS({ context: e, ai: t }) {
  const n = WS();
  return /* @__PURE__ */ o.jsx(Cr, { title: "Run", children: /* @__PURE__ */ o.jsx(dS, { context: e, ai: t, workflowExecutionId: n }) });
}
function Cr({ title: e, children: t }) {
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ o.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ o.jsxs("div", { children: [
      /* @__PURE__ */ o.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ o.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function ol() {
  return new URLSearchParams(window.location.search).get("definition");
}
function sl() {
  return new URLSearchParams(window.location.search).get("definition");
}
function WS() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function qS(e) {
  Qf(e.dialogs), e.featureAreas.add({
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
        component: () => /* @__PURE__ */ o.jsx(zS, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list(), autosaveEnabledByDefault: e.runtime.workflows?.autosaveEnabledByDefault ?? !0 })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ o.jsx(VS, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => /* @__PURE__ */ o.jsx(OS, { context: e.backend })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => /* @__PURE__ */ o.jsx(HS, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => /* @__PURE__ */ o.jsx(kg, { context: e.backend })
      },
      {
        id: "workflows-runtime-diagnostics",
        path: "/workflows/runtime-diagnostics",
        label: "Runtime diagnostics",
        component: () => /* @__PURE__ */ o.jsx(Ag, { context: e.backend })
      }
    ]
  });
}
export {
  Pj as isConnectEndOverExistingWorkflowNode,
  qS as register,
  Mj as resolveConnectEndSource
};
