import Ke, { useMemo as le, useState as K, useEffect as Q, memo as ke, forwardRef as tl, useRef as re, useCallback as se, useContext as Un, createContext as Qo, useLayoutEffect as jf, lazy as Nf, Suspense as Sf, useReducer as Cf, useId as nl } from "react";
import { Boxes as Zn, Zap as kf, Play as Jt, Terminal as Ef, ListTree as es, GitBranch as il, ListChecks as If, Save as ts, EyeOff as so, Shield as Xs, AlertTriangle as Tn, SlidersHorizontal as ns, Activity as rl, Search as rr, X as is, DatabaseZap as qs, ShieldCheck as Af, Check as ln, Plus as Qt, Trash2 as $n, AlertCircle as bt, Wrench as _f, Copy as Df, Sparkles as ut, RotateCcw as rs, ChevronDown as ol, ChevronRight as yt, GripVertical as sl, Maximize2 as Pn, ChevronUp as Tf, Repeat2 as $f, Package as al, Undo2 as Pf, Redo2 as Mf, Network as Rf, Download as Lf, ChevronLeft as Mn, Minimize2 as Eo, Workflow as cl, Code2 as zf } from "lucide-react";
import { useQuery as or, useQueryClient as ll, useMutation as ul } from "@tanstack/react-query";
import { useTablistKeyboard as Vf } from "@elsa-workflows/studio-ui";
function Of(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var ao = { exports: {} }, yn = {};
var Ys;
function Hf() {
  if (Ys) return yn;
  Ys = 1;
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
var Us;
function Wf() {
  return Us || (Us = 1, ao.exports = Hf()), ao.exports;
}
var o = Wf();
let dl;
function Ff(e) {
  dl = e;
}
function Zs() {
  return dl;
}
const Bf = "String", Kf = "singleline";
function Xf(e) {
  return e === "Single" || e === "Array" || e === "List" || e === "HashSet";
}
function os(e, t = "Single") {
  return { alias: (e ?? "").trim() || Bf, collectionKind: t };
}
function ss(e) {
  const t = e.type ?? e.Type;
  if (Rn(t))
    return { alias: typeof t.alias == "string" ? t.alias : typeof t.typeName == "string" ? t.typeName : "", collectionKind: Xf(t.collectionKind) ? t.collectionKind : "Single" };
  const n = e.typeInformation ?? e.TypeInformation;
  return Rn(n) ? { alias: typeof n.typeName == "string" ? n.typeName : "", collectionKind: Gs(e) ? "Array" : "Single" } : { alias: typeof t == "string" ? t : "", collectionKind: Gs(e) ? "Array" : "Single" };
}
function Gs(e) {
  const t = e.isArray ?? e.IsArray;
  return t === !0 || t === "true";
}
function Js(e) {
  return (e.alias ?? e.typeName ?? "").trim();
}
function sr() {
  return typeof crypto < "u" && typeof crypto.randomUUID == "function" ? crypto.randomUUID() : `id-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
}
function qf(e, t) {
  const n = new Set(t);
  let i = 1, r = `${e}${i}`;
  for (; n.has(r); )
    i += 1, r = `${e}${i}`;
  return r;
}
function Yf(e) {
  return {
    referenceKey: sr(),
    name: e.name,
    type: os(e.alias),
    storageDriverType: e.storageDriver?.trim() || null,
    default: null
  };
}
function Uf(e, t) {
  return { ...e, ...t };
}
function Zf(e) {
  return e === "" ? null : { value: e, expressionType: "Literal" };
}
function Gf(e) {
  return e == null || e.value == null ? "" : typeof e.value == "object" ? JSON.stringify(e.value) : String(e.value);
}
function Jf(e) {
  return {
    referenceKey: sr(),
    name: e.name,
    type: os(e.alias),
    displayName: e.name,
    description: "",
    category: "",
    uiHint: Kf,
    storageDriverType: e.storageDriver ?? null,
    isRequired: !1
  };
}
function Qf(e, t) {
  return { ...e, ...t };
}
function ep(e) {
  return {
    referenceKey: sr(),
    name: e.name,
    type: os(e.alias),
    displayName: e.name,
    description: "",
    category: ""
  };
}
function tp(e, t) {
  return { ...e, ...t };
}
function np(e) {
  const t = e.split(",")[0].trim();
  return (t.split(".").pop() ?? t).split("`")[0];
}
function fl(e, t) {
  return e && !e.includes(",") && !e.includes(".") ? e : np(e || t);
}
function ip(e, t) {
  return fl(e, t).replace(/StorageDriver$/, "");
}
function Rn(e) {
  return !!e && typeof e == "object" && !Array.isArray(e);
}
function ct(e, t) {
  for (const n of t) {
    const i = e[n];
    if (i != null && typeof i != "object") return String(i);
  }
  return "";
}
const rp = ["name", "Name"], as = ["name", "Name"], op = ["storageDriverType", "StorageDriverType"], pl = ["referenceKey", "ReferenceKey"], sp = /* @__PURE__ */ new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);
function Gn(e) {
  return hl(e, up);
}
function ar(e) {
  return hl(e, dp);
}
function hl(e, t) {
  if (!e) return e;
  const n = { ...e };
  return e.rootActivity && (n.rootActivity = Io(e.rootActivity, t)), Array.isArray(e.variables) && (n.variables = Ti(e.variables, Ao)), Array.isArray(e.inputs) && (n.inputs = Ti(e.inputs, Ao)), Array.isArray(e.outputs) && (n.outputs = Ti(e.outputs, (i) => gl(i, !1))), n;
}
function Io(e, t) {
  const n = t(e), i = n.structure;
  if (!i || !it(i.payload)) return n;
  let r = !1;
  const s = { ...i.payload };
  for (const [a, c] of Object.entries(i.payload))
    ea(c) ? (s[a] = Io(c, t), r = !0) : Array.isArray(c) && c.length > 0 && c.every(ea) && (s[a] = c.map((u) => Io(u, t)), r = !0);
  return Array.isArray(i.payload.variables) && i.payload.variables.length > 0 && (s.variables = Ti(i.payload.variables, Ao), r = !0), r ? { ...n, structure: { ...i, payload: s } } : n;
}
function Ti(e, t) {
  return e.map((n) => it(n) && !Array.isArray(n) ? t(n) : n);
}
function Ao(e) {
  return gl(e, !0);
}
const ap = [
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
function gl(e, t) {
  const n = cp(e, ap);
  return ct(e, pl).trim() || (n.referenceKey = sr()), n.type = ss(e), t && (n.storageDriverType = lp(e.storageDriverType ?? e.StorageDriverType)), n;
}
function cp(e, t) {
  const n = new Set(t), i = {};
  for (const [r, s] of Object.entries(e))
    n.has(r) || (i[r] = s);
  return i;
}
function lp(e) {
  if (typeof e == "string") return e.trim() ? e : null;
  if (it(e)) {
    const t = typeof e.typeName == "string" ? e.typeName : "";
    if (!t) return null;
    const n = typeof e.namespace == "string" ? e.namespace : "";
    return n ? `${n}.${t}` : t;
  }
  return null;
}
function up(e) {
  const t = [], n = {};
  for (const [r, s] of Object.entries(e))
    sp.has(r) || (gp(s) ? t.push({
      referenceKey: fp(r),
      value: hp(s.expression)
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
function dp(e) {
  const t = Array.isArray(e.inputs) ? e.inputs : [], n = {};
  for (const i of t) {
    if (!it(i) || typeof i.referenceKey != "string") continue;
    const r = it(i.value) ? i.value : {};
    n[pp(i.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof r.expressionType == "string" ? r.expressionType : "Literal",
        value: r.value ?? ""
      }
    };
  }
  return { ...e, ...n, inputs: [] };
}
function fp(e) {
  return e && e.charAt(0).toUpperCase() + e.slice(1);
}
function pp(e) {
  return e && e.charAt(0).toLowerCase() + e.slice(1);
}
function hp(e) {
  const t = e.type || "Literal";
  return t === "Variable" && it(e.value) ? { value: e.value, expressionType: t } : t === "Literal" && it(e.value) ? { value: Qs(e.value), expressionType: "Object" } : { value: Qs(e.value), expressionType: t };
}
function Qs(e) {
  return e == null ? null : typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function gp(e) {
  if (!it(e) || Array.isArray(e)) return !1;
  const t = e.expression;
  return it(t) && typeof t.type == "string";
}
function ea(e) {
  return it(e) && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function it(e) {
  return typeof e == "object" && e !== null;
}
const Jn = "elsa.sequence.structure", un = "elsa.flowchart.structure";
function ml(e, t, n) {
  if (!e) return null;
  let i = e;
  for (const r of t) {
    const s = yp(i, r.ownerNodeId, n);
    if (!s) return null;
    i = s;
  }
  return i;
}
function mp(e, t, n = (r) => r.nodeId, i) {
  if (!e) return null;
  if (e.nodeId === t) return [];
  const r = (s, a) => {
    const c = Le(s, i);
    for (const u of c) {
      if (!u.activities.some((d) => d.nodeId === t)) continue;
      const l = a.at(-1);
      return l ? [...a.slice(0, -1), { ...l, slotId: u.id }] : u.id === c[0]?.id ? a : null;
    }
    for (const u of c)
      for (const l of u.activities) {
        const f = Le(l, i)[0]?.id ?? u.id, p = r(l, [...a, { ownerNodeId: l.nodeId, slotId: f, label: n(l) }]);
        if (p) return p;
      }
    return null;
  };
  return r(e, []);
}
function yl(e, t, n) {
  const i = Le(e, n), r = t.at(-1);
  return r ? i.find((s) => s.id === r.slotId) ?? null : i[0] ?? null;
}
function Ln(e, t, n) {
  const i = ml(e, t, n);
  if (!i) return null;
  const r = yl(i, t, n);
  return r ? { owner: i, slot: r } : null;
}
function xl(e, t, n) {
  for (const i of Le(e, n)) {
    const r = i.activities.find((s) => s.nodeId === t);
    if (r) return { slot: i, child: r };
  }
  return null;
}
function yp(e, t, n) {
  return xl(e, t, n)?.child ?? null;
}
function Le(e, t) {
  const n = e.structure;
  if (!n || !n.payload || typeof n.payload != "object") return [];
  const i = n.payload, r = cs(wl(e, t));
  if (r?.kind === n.kind) return vp(n, r);
  const s = Hp(n), a = bn(i.activities);
  return a ? [{
    id: `${n.kind}:activities`,
    label: Wp(n),
    property: "activities",
    cardinality: "many",
    mode: s,
    activities: a
  }] : Object.entries(i).filter(([, c]) => bn(c) || Ki(c)).map(([c, u]) => ({
    id: `${n.kind}:${c}`,
    label: Bp(c),
    property: c,
    cardinality: bn(u) ? "many" : "single",
    mode: "generic",
    activities: bn(u) ?? (Ki(u) ? [u] : [])
  }));
}
function cs(e) {
  for (const t of e?.designFacets ?? []) {
    if (!rt(t)) continue;
    const n = typeof t.kind == "string" ? t.kind : "", i = typeof t.schemaVersion == "string" ? t.schemaVersion : "";
    if (!n || !i || !rt(t.payload)) continue;
    const r = xp(t.payload);
    if (r) return { kind: n, schemaVersion: i, payload: r };
  }
  return null;
}
function xp(e) {
  const t = e.mode;
  if (t !== "sequence" && t !== "flowchart" && t !== "generic" || typeof e.supportsScopedVariables != "boolean" || !Array.isArray(e.slots) || !rt(e.initialPayload)) return null;
  const n = e.slots.map(wp).filter((i) => i !== null);
  return {
    mode: t,
    supportsScopedVariables: e.supportsScopedVariables,
    slots: n,
    initialPayload: e.initialPayload
  };
}
function wp(e) {
  if (!rt(e)) return null;
  const t = typeof e.name == "string" ? e.name : "", n = typeof e.property == "string" ? e.property : "", i = typeof e.displayName == "string" ? e.displayName : "", r = e.cardinality;
  return !t || !n || !i || r !== "single" && r !== "many" ? null : {
    name: t,
    property: n,
    displayName: i,
    cardinality: r,
    collectionProperty: Kt(e.collectionProperty),
    childProperty: Kt(e.childProperty),
    labelProperty: Kt(e.labelProperty),
    slotNameTemplate: Kt(e.slotNameTemplate)
  };
}
function vp(e, t) {
  return t.payload.slots.flatMap((n) => {
    if (n.collectionProperty && n.childProperty) {
      const i = n.childProperty, r = e.payload[n.collectionProperty];
      return Array.isArray(r) ? r.flatMap((s, a) => {
        if (!rt(s)) return [];
        const c = n.labelProperty ? Kt(s[n.labelProperty]) : void 0;
        return [ta(e.kind, n, s[i], t.payload.mode, a, c)];
      }) : [];
    }
    return [ta(e.kind, n, e.payload[n.property], t.payload.mode)];
  });
}
function ta(e, t, n, i, r, s) {
  const a = r === void 0 ? "" : `:${t.collectionProperty}:${t.childProperty}:${r}`;
  return {
    id: `${e}:${t.property}${a}`,
    label: r === void 0 ? t.displayName : jp(t, r, s),
    property: t.property,
    cardinality: t.cardinality,
    mode: i,
    activities: bp(n, t.cardinality),
    collectionProperty: t.collectionProperty,
    childProperty: t.childProperty,
    labelProperty: t.labelProperty,
    slotNameTemplate: t.slotNameTemplate,
    collectionIndex: r,
    collectionItemLabel: s
  };
}
function bp(e, t) {
  return t === "many" ? bn(e) ?? [] : Ki(e) ? [e] : [];
}
function jp(e, t, n) {
  return e.slotNameTemplate ? e.slotNameTemplate.replaceAll("{name}", e.name).replaceAll("{displayName}", e.displayName).replaceAll("{label}", n ?? String(t + 1)).replaceAll("{index}", String(t + 1)) : n ? `${e.displayName}: ${n}` : `${e.displayName} ${t + 1}`;
}
function wl(e, t) {
  if (t)
    return t instanceof Map ? t.get(e.activityVersionId) : Array.isArray(t) ? t.find((n) => n.activityVersionId === e.activityVersionId) : t.activityVersionId === e.activityVersionId ? t : void 0;
}
function Np(e, t) {
  if (t.labelProperty && t.collectionItemLabel) {
    const n = e.map((i, r) => rt(i) && Kt(i[t.labelProperty]) === t.collectionItemLabel ? r : -1).filter((i) => i >= 0);
    if (n.length === 1) return n[0];
  }
  return typeof t.collectionIndex == "number" && t.collectionIndex >= 0 && t.collectionIndex < e.length ? t.collectionIndex : -1;
}
function vl(e, t, n) {
  const i = new Map(t.map((a) => [a.activityVersionId, a])), r = new Map(n.map((a) => [a.nodeId, a])), s = e.slot.activities.map((a, c) => {
    const u = i.get(a.activityVersionId), l = r.get(a.nodeId) ?? Fp(e.slot.mode, c);
    return Sl(a, u, { x: l.x, y: l.y });
  });
  return {
    nodes: s,
    edges: e.slot.mode === "flowchart" ? kl(e.owner) : Pp(e.slot, s)
  };
}
function bl(e, t, n) {
  const i = t.find((s) => s.activityVersionId === e.activityVersionId), r = n.find((s) => s.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [Sl(e, i, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function Sp(e, t, n, i = null) {
  const r = new Map(t.map((c) => [c.activityExecutionId, c])), s = ra(t, (c) => c.authoredActivityId || c.executableNodeId), a = ra(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? r.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const u = s.get(c.id) ?? [], l = a.get(c.id) ?? [];
    if (u.length === 0 && l.length === 0) return c;
    const d = Il(u), f = i === c.id || u.some((h) => h.activityExecutionId === i) || l.some((h) => h.incidentId === i), p = {
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
function jl(e, t) {
  if (e?.structure?.kind === un || _p(t)) return "flowchart";
  if (e?.structure?.kind === Jn || Dp(t)) return "sequence";
  if (e) {
    const n = Le(e, t)[0];
    if (n) return n.mode === "flowchart" ? "flowchart" : "sequence";
  }
  return "unsupported";
}
function na(e, t, n, i) {
  return ls(e, t, i, (r) => {
    const s = yl(r, t, i);
    return s ? dn(r, s, n) : r;
  });
}
function Cp(e, t, n, i) {
  return t.length === 0 ? n : ls(e, t, i, () => n);
}
function ls(e, t, n, i) {
  if (t.length === 0) return i(e);
  const [r, ...s] = t, a = xl(e, r.ownerNodeId, n);
  if (!a) return e;
  const c = ls(a.child, s, n, i);
  if (c === a.child) return e;
  const u = a.slot.activities.map((l) => l.nodeId === r.ownerNodeId ? c : l);
  return dn(e, a.slot, u);
}
function _o(e, t, n, i) {
  if (e.nodeId === t) return n(e);
  const r = Le(e, i);
  if (r.length === 0) return e;
  let s = !1, a = e;
  for (const c of r) {
    const u = c.activities.map((l) => {
      const d = _o(l, t, n, i);
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
    const r = e.structure.payload[t.collectionProperty];
    if (!Array.isArray(r)) return e;
    const s = Np(r, t);
    if (s < 0) return e;
    const a = r[s];
    if (!rt(a)) return e;
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
function kp(e, t, n, i = []) {
  const r = new Map(e.slot.activities.map((a) => [a.nodeId, a]));
  for (const a of i)
    r.set(a.nodeId, a);
  const s = t.map((a) => r.get(a.id)).filter((a) => !!a);
  return e.slot.mode === "sequence" && s.sort((a, c) => {
    const u = t.find((d) => d.id === a.nodeId), l = t.find((d) => d.id === c.nodeId);
    return (u?.position.x ?? 0) - (l?.position.x ?? 0);
  }), dn(e.owner, e.slot, s);
}
function Ep(e, t) {
  return {
    ...e,
    structure: $p(e.structure, t)
  };
}
function Ip(e, t) {
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
function Fi(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: Cl(e)
  };
}
function Nl(e, t) {
  if (!e) return null;
  const n = wl(e, t), i = e.structure ?? (n ? Cl(n) : null);
  let r = i === e.structure ? e : { ...e, structure: i };
  const s = Le(r, t);
  for (const a of s) {
    const c = a.activities.map((u) => Nl(u, t) ?? u);
    c.some((u, l) => u !== a.activities[l]) && (r = dn(r, a, c));
  }
  return r;
}
function Ce(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? Tp(t) : n;
}
function Sl(e, t, n, i = {}) {
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
      childSlots: Le(e, t),
      acceptsInbound: Mp(e, t),
      sourcePorts: i.suppressFlowPorts ? [] : El(e, t),
      suppressFlowPorts: i.suppressFlowPorts
    }
  };
}
function fn(e) {
  if (!e) return "activity";
  const t = Ap(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), i = Ce(e).toLowerCase(), r = e.category?.toLowerCase() ?? "", s = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || i === "flowchart" ? "flowchart" : n.endsWith(".sequence") || i === "sequence" ? "sequence" : n.includes("writeline") || i.includes("write line") ? "terminal" : r.includes("runtime") ? "runtime" : s === "trigger" ? "trigger" : "activity";
}
function Ap(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function _p(e) {
  return !!e && (Ce(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function Dp(e) {
  return !!e && (Ce(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function Tp(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function Cl(e) {
  const t = cs(e);
  return t ? {
    kind: t.kind,
    schemaVersion: t.schemaVersion,
    payload: Vp(t.payload.initialPayload)
  } : e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: Jn,
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
function $p(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], i = /* @__PURE__ */ new Map();
  for (const r of n) {
    if (!rt(r)) continue;
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
function Pp(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, i) => ({
    id: `sequence-${n.id}-${t[i + 1].id}`,
    source: n.id,
    target: t[i + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function kl(e) {
  if (e.structure?.kind !== un) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, i) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, s = n.target;
    if (!r?.nodeId || !s?.nodeId) return null;
    const a = Array.isArray(n.vertices) ? n.vertices.filter(Op) : [];
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
function El(e, t) {
  const n = ia(e.cases);
  if (Lp(e, t) && n.length > 0)
    return [...n.map((s) => ({ name: s, displayName: s })), { name: "Default", displayName: "Default" }];
  const i = [
    ...$i(t?.designFacets),
    ...$i(t?.ports),
    ...$i(t?.outputs)
  ];
  if (i.length > 0) return zp(i);
  const r = ia(e.outcomes);
  return r.length > 0 ? r.map((s) => ({ name: s, displayName: s })) : [{ name: "Done", displayName: "Done" }];
}
function Mp(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function Bi(e, t, n, i) {
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
function Rp(e, t, n) {
  const i = Bi(t.source, n, t.sourceHandle ?? "Done", void 0), r = Bi(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((s) => s.id !== t.id).concat(i, r);
}
function bn(e) {
  return Array.isArray(e) ? e.filter(Ki) : null;
}
function Lp(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, i = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || i === "FlowSwitch";
}
function $i(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!rt(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...$i(n.ports));
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
function zp(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function ia(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function Kt(e) {
  return typeof e == "string" && e.length > 0 ? e : void 0;
}
function Vp(e) {
  return JSON.parse(JSON.stringify(e));
}
function ra(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e) {
    const r = t(i);
    r && n.set(r, [...n.get(r) ?? [], i]);
  }
  return n;
}
function Il(e) {
  return [...e].sort((t, n) => oa(n).localeCompare(oa(t)))[0];
}
function oa(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function Op(e) {
  return rt(e) && typeof e.x == "number" && typeof e.y == "number";
}
function rt(e) {
  return typeof e == "object" && e !== null;
}
function Ki(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Hp(e) {
  return e.kind === Jn ? "sequence" : e.kind === un ? "flowchart" : "generic";
}
function Wp(e) {
  return e.kind === Jn || e.kind === un, "Activities";
}
function Fp(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function Bp(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Xi = "workflow", Kp = /* @__PURE__ */ new Set([Jn, un]);
function Xp(e, t) {
  const n = e?.structure?.kind;
  if (!n) return !1;
  if (Kp.has(n)) return !0;
  const i = t?.activityVersionId === e?.activityVersionId ? cs(t) : null;
  return i?.kind === n && i.payload.supportsScopedVariables;
}
function Al(e) {
  const t = e?.structure?.payload?.variables;
  return Array.isArray(t) ? t.filter(Rn) : [];
}
function qp(e, t) {
  return e.structure ? {
    ...e,
    structure: {
      ...e.structure,
      payload: { ...e.structure.payload, variables: t }
    }
  } : e;
}
function Yp(e, t) {
  return {
    referenceKey: e,
    declaringScopeId: t && t !== Xi ? t : Xi
  };
}
function _l(e) {
  if (e && typeof e == "object") {
    const t = e;
    return typeof t.referenceKey == "string" && t.referenceKey ? { referenceKey: t.referenceKey, declaringScopeId: typeof t.declaringScopeId == "string" ? t.declaringScopeId : null } : null;
  }
  if (typeof e == "string") {
    const t = e.trim();
    if (t.startsWith("{"))
      try {
        return _l(JSON.parse(t));
      } catch {
      }
    return t ? { referenceKey: t, declaringScopeId: null } : null;
  }
  return null;
}
function Up(e, t) {
  if (!e) return "";
  const n = [`workflow:${sa(e.variables)}`], i = (r) => {
    const s = Le(r, t), a = s.flatMap((c) => c.activities.map((u) => u.nodeId));
    n.push(`${r.nodeId}:${sa(Al(r))}>${a.join(",")}`), s.forEach((c) => c.activities.forEach(i));
  };
  return e.rootActivity && i(e.rootActivity), n.join(";");
}
function sa(e) {
  return (e ?? []).map((t) => `${t.referenceKey}=${t.name}`).join(",");
}
function Zp(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e ?? [])
    i.scopeId === t && n.set(i.referenceKey, `Shadows "${i.name}" declared in an outer scope.`);
  return n;
}
const be = "/_elsa/workflow-management", Gp = "/publishing", xt = {
  activities: ["workflows", "activities"],
  activityAvailabilitySettings: ["workflows", "activity-availability", "settings"],
  activityAvailabilityDiagnostics: ["workflows", "activity-availability", "diagnostics"],
  runtimeDiagnosticsSettings: ["workflows", "runtime-diagnostics", "settings"]
};
function Jp(e) {
  return or({
    queryKey: xt.activities,
    queryFn: () => cr(e),
    staleTime: 6e4
  });
}
function Qp(e) {
  return or({
    queryKey: xt.activityAvailabilitySettings,
    queryFn: () => jh(e)
  });
}
function eh(e) {
  return or({
    queryKey: xt.activityAvailabilityDiagnostics,
    queryFn: () => $l(e)
  });
}
function th(e) {
  const t = ll();
  return ul({
    mutationFn: (n) => Nh(e, n),
    onSuccess: (n) => {
      t.setQueryData(xt.activityAvailabilitySettings, n), t.invalidateQueries({ queryKey: xt.activityAvailabilityDiagnostics }), t.invalidateQueries({ queryKey: xt.activities });
    }
  });
}
function nh(e) {
  return or({
    queryKey: xt.runtimeDiagnosticsSettings,
    queryFn: () => Sh(e)
  });
}
function ih(e) {
  const t = ll();
  return ul({
    mutationFn: (n) => Ch(e, n),
    onSuccess: () => {
      t.invalidateQueries({ queryKey: xt.runtimeDiagnosticsSettings });
    }
  });
}
async function rh(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), i = t.search.trim();
  return i && n.set("search", i), e.http.getJson(`${be}/definitions?${n.toString()}`);
}
async function oh(e, t) {
  const n = await e.http.getJson(`${be}/definitions/${encodeURIComponent(t)}`);
  return n.draft ? { ...n, draft: { ...n.draft, state: ar(n.draft.state) } } : n;
}
async function sh(e, t, n) {
  const i = await e.http.postJson(
    `${be}/design/scoped-variables/analyze`,
    { state: Gn(t), nodeId: n }
  );
  return {
    visibleVariables: Array.isArray(i?.visibleVariables) ? i.visibleVariables : [],
    shadowingWarnings: Array.isArray(i?.shadowingWarnings) ? i.shadowingWarnings : []
  };
}
const co = (e) => ({ visibleVariables: [], shadowingWarnings: [], status: e });
function ah(e, t, n, i) {
  const r = le(() => Up(t, i), [i, t]), [s, a] = K(() => co("loading"));
  return Q(() => {
    if (!t) {
      a(co("unavailable"));
      return;
    }
    let c = !1;
    return a((u) => ({ ...u, status: "loading" })), sh(e, t, n).then(
      (u) => {
        c || a({ ...u, status: "ready" });
      },
      () => {
        c || a(co("unavailable"));
      }
    ), () => {
      c = !0;
    };
  }, [e, n, r]), s;
}
async function ch(e, t) {
  const n = await e.http.getJson(`${be}/versions/${encodeURIComponent(t)}`);
  return { ...n, state: ar(n.state) };
}
async function lh(e, t) {
  return e.http.postJson(`${be}/definitions`, t);
}
async function uh(e, t) {
  await e.http.deleteJson(`${be}/definitions/${encodeURIComponent(t)}`);
}
async function dh(e, t) {
  await e.http.postJson(`${be}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function fh(e, t) {
  await e.http.deleteJson(`${be}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function ph(e, t, n) {
  return e.http.requestJson(
    `${be}/definitions/${encodeURIComponent(t)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(n)
    }
  );
}
async function hh(e, t) {
  const n = await e.http.putJson(
    `${be}/drafts/${encodeURIComponent(t.id)}`,
    { state: Gn(t.state), layout: t.layout }
  );
  return { ...n, state: ar(n.state) };
}
async function gh(e, t) {
  return e.http.postJson(`${be}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function mh(e, t) {
  return e.http.postJson(`${be}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function yh(e, t) {
  const n = { ...t, state: Gn(t.state) };
  try {
    return await e.http.postJson(`${Gp}/workflows/drafts/test-runs`, n);
  } catch (i) {
    const r = Th(i);
    if (r) return r;
    throw i;
  }
}
async function Dl(e, t) {
  return e.http.postJson(`${be}/executables/${encodeURIComponent(t)}/run`, {});
}
async function Tl(e) {
  const t = [`${be}/executables`, "/_demo/workflows/executables"], n = [];
  for (const i of t)
    try {
      const r = await e.http.getJson(i);
      return xh(r);
    } catch (r) {
      n.push(r);
    }
  if (n.length > 0 && n.every(aa)) return [];
  throw n.find((i) => !aa(i)) ?? n[n.length - 1] ?? new Error("Workflow executables could not be loaded.");
}
function xh(e) {
  return Array.isArray(e) ? e : e.executables ?? [];
}
function aa(e) {
  if (!(e instanceof Error)) return !1;
  const t = e.message.toLowerCase();
  return /\b404\b/.test(t) || t.includes("not found");
}
async function wh(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.runKind && n.set("runKind", t.runKind), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const i = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${i ? `?${i}` : ""}`);
}
async function vh(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function bh(e, t, n) {
  return e.http.getJson(
    `/runtime/workflows/instances/${encodeURIComponent(t)}/activity-executions/${encodeURIComponent(n)}`
  );
}
async function cr(e) {
  return e.http.getJson(`${be}/activities`);
}
async function jh(e) {
  return e.http.getJson(`${be}/activities/availability/settings`);
}
async function Nh(e, t) {
  return e.http.putJson(`${be}/activities/availability/settings`, t);
}
async function $l(e) {
  return e.http.getJson(`${be}/activities/availability/diagnostics`);
}
async function Sh(e) {
  return e.http.getJson(`${be}/runtime-diagnostics/settings`);
}
async function Ch(e, t) {
  return e.http.putJson(`${be}/runtime-diagnostics/settings`, t);
}
async function kh(e) {
  const t = await lr(e, [
    `${be}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? ca(t) : ca(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function Eh(e) {
  const t = await lr(e, [
    `${be}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : Pi;
}
async function Ih(e) {
  const t = await lr(e, [
    `${be}/descriptors/variables`,
    "/descriptors/variables"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((i) => Ah(i));
}
function Ah(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = typeof t.alias == "string" && t.alias.length > 0, i = typeof t.typeName == "string" && t.typeName.length > 0;
  return n || i;
}
async function _h(e) {
  const t = await lr(e, [
    `${be}/descriptors/storage-drivers`,
    "/descriptors/storage-drivers"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((i) => Dh(i));
}
function Dh(e) {
  return !!e && typeof e == "object" && typeof e.typeName == "string" && e.typeName.length > 0;
}
async function lr(e, t) {
  let n;
  for (const i of t)
    try {
      return await e.http.getJson(i);
    } catch (r) {
      n = r;
    }
  throw n;
}
function ca(e) {
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
function Th(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = la(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return la(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function la(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const Pi = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function Pl(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e) {
    const r = t(i)?.trim() || "Uncategorized", s = n.get(r);
    s ? s.push(i) : n.set(r, [i]);
  }
  return [...n.entries()].map(([i, r]) => ({ category: i, items: r })).sort((i, r) => i.category.localeCompare(r.category));
}
const $h = [
  "Available",
  "BlockedByHostBaseline",
  "HiddenByManagementSettings",
  "RemovedFromCatalog",
  "UnresolvedReference"
], Ph = {
  Available: "Available",
  BlockedByHostBaseline: "Host blocked",
  HiddenByManagementSettings: "Management hidden",
  RemovedFromCatalog: "Removed",
  UnresolvedReference: "Unresolved"
}, Ml = {
  Catalog: "Catalog",
  HostBaseline: "Host baseline",
  ManagementSettings: "Management settings"
}, Mh = Object.keys(Ml);
function Rh(e) {
  const t = typeof e == "number" ? Mh[e] : e;
  return t && Ml[t] || t?.toString() || "";
}
function It(e) {
  return typeof e == "string" ? e : typeof e == "number" ? $h[e] ?? "Available" : "Available";
}
function zn(e) {
  const t = It(e);
  return Ph[t] ?? t;
}
function Rl(e) {
  return It(e).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Ll(e) {
  return It(e) !== "Available";
}
function zl(e) {
  return It(e.state) === "BlockedByHostBaseline";
}
function Lh(e) {
  return e === "Only" || e === 1 ? "Only" : "AllExcept";
}
function zh(e) {
  return e === "Only" ? 1 : 0;
}
function Do(e) {
  const t = e?.rules;
  return {
    mode: Lh(e?.mode),
    activityTypes: t?.activityTypes ?? [],
    sets: t?.sets ?? []
  };
}
function Vh(e) {
  return e.referenceKind === 0 || e.referenceKind === "ActivityType";
}
function jn(e) {
  return e.activityTypeKey ?? e.activityDefinitionId ?? "";
}
function Oh(e) {
  return [...e?.items ?? []].filter(Vh).filter((t) => t.activityTypeKey && t.activityDefinitionId).sort((t, n) => Vn(t).localeCompare(Vn(n)));
}
function Hh(e) {
  return [...e?.items ?? []].filter((t) => {
    const n = It(t.state);
    return n === "RemovedFromCatalog" || n === "UnresolvedReference";
  }).sort((t, n) => (t.referenceName ?? "").localeCompare(n.referenceName ?? ""));
}
function Wh(e) {
  return Pl(e, (t) => t.category).map((t) => ({ category: t.category, entries: t.items }));
}
function Fh(e, t, n) {
  const i = e[t].includes(n);
  return e.mode === "Only" ? i : !i;
}
function lo(e, t, n, i) {
  const r = e.mode === "Only" ? i : !i;
  return e[t].includes(n) === r ? e : { ...e, [t]: Xh(e[t], n) };
}
function Bh(e, t, n) {
  const i = new Set(n.activityTypes), r = new Set(n.sets), s = /* @__PURE__ */ new Map();
  for (const c of t)
    if (r.has(c.name))
      for (const u of c.activityTypeKeys ?? [])
        s.has(u) || s.set(u, c.name);
  const a = /* @__PURE__ */ new Map();
  for (const c of e) {
    const u = jn(c);
    if (zl(c)) {
      a.set(u, { enabled: !1, lockedBy: "host-baseline" });
      continue;
    }
    const l = s.get(u), d = i.has(u) || l !== void 0, f = n.mode === "Only" ? d : !d;
    a.set(u, l ? { enabled: f, lockedBy: "set-rule", governingSet: l } : { enabled: f, lockedBy: null });
  }
  return a;
}
function Kh(e, t) {
  const n = Do(t), i = (r) => [...r].sort((s, a) => s.localeCompare(a)).join(`
`);
  return e.mode !== n.mode || i(e.activityTypes) !== i(n.activityTypes) || i(e.sets) !== i(n.sets);
}
function Xh(e, t) {
  return e.includes(t) ? e.filter((n) => n !== t) : [...e, t].sort((n, i) => n.localeCompare(i));
}
function Vn(e) {
  const t = e?.displayName?.trim();
  if (t && t.toLowerCase() !== "activity") return t;
  const n = Yh(e?.activityTypeKey);
  return qh(n) || t || e?.activityTypeKey || "Activity";
}
function ua(e) {
  const t = Vl(e?.activityTypeKey);
  if (t.length === 0) return "";
  const n = t[t.length - 1], i = t[t.length - 2];
  return n === "Activity" && i ? `${i}.${n}` : n;
}
function da(e) {
  const t = e.description?.trim();
  if (t) return t;
  const n = e.reason?.trim();
  return !n || !Ll(e.state) ? "" : n;
}
function qh(e) {
  return e.replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/\bHttp\b/g, "HTTP").replace(/\bJson\b/g, "JSON").replace(/\bJava Script\b/g, "JavaScript").replace(/\bUrl\b/g, "URL").replace(/\bXml\b/g, "XML").trim();
}
function Yh(e) {
  const t = Vl(e), n = t[t.length - 1] ?? "";
  return n === "Activity" && t.length > 1 ? t[t.length - 2] : n;
}
function Vl(e) {
  return e?.split(".").filter(Boolean) ?? [];
}
function fa(e) {
  return (e ?? []).flatMap((t) => {
    if (!Rn(t)) return [];
    const n = (ct(t, ["displayName", "DisplayName"]) || ct(t, as)).trim();
    if (!n) return [];
    const i = (ss(t).alias || ct(t, ["typeName", "TypeName", "alias", "Alias"])).trim();
    return [{ name: n, typeName: i, description: ct(t, ["description", "Description"]).trim() }];
  });
}
function Uh(e, t) {
  const n = new Set(e.filter((i) => !!i));
  return (t?.items ?? []).find((i) => Ll(i.state) ? [i.activityDefinitionId, i.activityTypeKey, i.referenceName].some((r) => r && n.has(r)) : !1) ?? null;
}
const Zh = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function Ol(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function us(e) {
  return Ol(e.name);
}
function Gh(e, t) {
  const n = us(t), i = e[n];
  return t.isWrapped === !1 ? i ?? t.defaultValue ?? "" : Wl(i, t);
}
function Hl(e, t) {
  return Wl(e[us(t)], t);
}
function Jh(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function Qh(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function pa(e, t, n) {
  return {
    ...e,
    [us(t)]: n
  };
}
function eg(e, t) {
  return t.isWrapped === !1 ? Gh(e, t) : Hl(e, t).expression.value;
}
function Wl(e, t) {
  return cg(e) ? {
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
const tg = /* @__PURE__ */ new Set([
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
function ng(e) {
  const t = e?.trim();
  if (!t) return null;
  const n = /^([\w.+]+)\[\]/.exec(t);
  if (n) return { elementTypeName: n[1] };
  const i = t.indexOf("`");
  if (i < 0) return null;
  const r = t.slice(0, i), s = (r.split(".").pop() ?? r).toLowerCase();
  return tg.has(s) ? { elementTypeName: ig(t.slice(i)) } : null;
}
function ig(e) {
  const t = /\[\[([\w.+]+)/.exec(e);
  if (t) return t[1];
  const n = /\[([\w.+]+)/.exec(e);
  return n ? n[1] : null;
}
function rg(e) {
  const t = e.uiHint?.toLowerCase();
  return t === "json" || t === "code" ? !0 : e.uiSpecifications?.repeater === !1;
}
function og(e) {
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
function sg(e) {
  const t = (e ?? "").toLowerCase();
  return t === "system.boolean" || t === "boolean" || t === "bool" ? !1 : "";
}
function ag(e, t) {
  return {
    ...e,
    typeName: t ?? "System.String",
    isWrapped: !1,
    // The description documents the collection as a whole; repeating it on every row is noise.
    description: null
  };
}
function uo(e, t, n) {
  if (t === n || t < 0 || n < 0 || t >= e.length || n >= e.length) return e;
  const i = [...e], [r] = i.splice(t, 1);
  return i.splice(n, 0, r), i;
}
function cg(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
function ds(e) {
  return fs(e?.trim() ?? "") || e;
}
function fs(e) {
  if (!e) return "";
  const t = lg(e);
  if (!t) return "";
  const n = /^(.*)((?:\[\])+)$/.exec(t);
  if (n) return `${fs(n[1])}${n[2]}`;
  const i = t.indexOf("`");
  if (i >= 0) {
    const r = ha(t.slice(0, i)), s = ug(t.slice(i));
    return s.length > 0 ? `${r}<${s.join(", ")}>` : r;
  }
  return ha(t);
}
function lg(e) {
  let t = 0;
  for (let n = 0; n < e.length; n++) {
    const i = e[n];
    if (i === "[") t++;
    else if (i === "]") t--;
    else if (i === "," && t === 0) return e.slice(0, n).trim();
  }
  return e.trim();
}
function ha(e) {
  const t = e.split(".").filter(Boolean).at(-1) ?? e;
  return t.split("+").filter(Boolean).at(-1) ?? t;
}
function ug(e) {
  const t = e.indexOf("[");
  if (t < 0) return [];
  const n = ga(e, t);
  return n == null ? [] : dg(n).map((i) => {
    const r = i.trim(), s = r.startsWith("[") ? ga(r, 0) ?? r : r;
    return fs(s);
  }).filter(Boolean);
}
function ga(e, t) {
  let n = 0;
  for (let i = t; i < e.length; i++)
    if (e[i] === "[") n++;
    else if (e[i] === "]" && --n === 0) return e.slice(t + 1, i);
  return null;
}
function dg(e) {
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
function ps(e, t) {
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
function At(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function ur(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ o.jsx(il, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ o.jsx(es, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ o.jsx(Ef, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ o.jsx(Jt, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ o.jsx(kf, { size: 15 });
    default:
      return /* @__PURE__ */ o.jsx(Zn, { size: 15 });
  }
}
function ma(e) {
  return `${e.activityTypeKey ?? ""}|${e.activityDefinitionId ?? ""}`;
}
function fg(e, t) {
  const n = (a) => (a ?? "").split(".").map((c) => Number.parseInt(c, 10) || 0), i = n(e), r = n(t), s = Math.max(i.length, r.length);
  for (let a = 0; a < s; a++) {
    const c = (i[a] ?? 0) - (r[a] ?? 0);
    if (c !== 0) return c;
  }
  return 0;
}
function pg(e) {
  if (e.lockedBy === "host-baseline") return "Blocked by the host baseline";
  if (e.lockedBy === "set-rule") return `Controlled by the "${e.governingSet}" set rule`;
}
function Fl({ icon: e }) {
  return /* @__PURE__ */ o.jsx("span", { className: "wf-activity-icon", "data-icon": e, "aria-hidden": "true", children: ur(e) });
}
function hg({ context: e }) {
  const t = Qp(e), n = eh(e), i = Jp(e), r = th(e), s = t.data ?? null, a = n.data ?? null, c = t.isLoading || n.isLoading, u = r.isPending, [l, d] = K(() => Do(s)), [f, p] = K(""), [h, m] = K(!1), [w, v] = K(null), [x, b] = K(null);
  Q(() => {
    d(Do(s));
  }, [s]);
  const g = le(() => Oh(a), [a]), y = le(() => Hh(a), [a]), N = le(() => a?.sets ?? [], [a]), j = le(
    () => Bh(g, N, l),
    [g, N, l]
  ), S = (V) => j.get(jn(V)) ?? { enabled: !0, lockedBy: null }, E = le(() => {
    const V = /* @__PURE__ */ new Map();
    for (const q of i.data?.activities ?? []) {
      if (!q.activityTypeKey) continue;
      const J = V.get(q.activityTypeKey);
      (!J || fg(q.version, J.version) > 0) && V.set(q.activityTypeKey, q);
    }
    return V;
  }, [i.data]), _ = (V) => E.get(V.activityTypeKey ?? ""), M = le(() => {
    const V = f.trim().toLowerCase(), q = h ? g.filter((J) => !(j.get(jn(J))?.enabled ?? !0)) : g;
    return V ? q.filter((J) => [
      Vn(J),
      ua(J),
      da(J),
      J.activityTypeKey,
      J.category
    ].some((Z) => (Z ?? "").toLowerCase().includes(V))) : q;
  }, [g, f, h, j]), I = le(() => Wh(M), [M]), $ = x ? g.find((V) => ma(V) === x) ?? null : null, z = g.filter(zl).length, C = g.filter((V) => It(V.state) === "HiddenByManagementSettings").length, A = le(() => Kh(l, s), [l, s]), k = r.error ?? t.error ?? n.error, D = k instanceof Error ? k.message : k ? "Activity availability could not be loaded." : null, P = (V) => {
    v(null), d(V);
  }, T = (V) => P((q) => ({ ...q, mode: V })), F = (V, q) => P((J) => lo(J, "activityTypes", V, q)), W = (V, q) => P((J) => V.reduce((Z, R) => lo(Z, "activityTypes", R, q), J)), H = (V, q) => P((J) => lo(J, "sets", V, q)), Y = () => {
    v(null), r.mutate(
      {
        scope: s?.scope ?? "host-default",
        mode: zh(l.mode),
        rules: { activityTypes: l.activityTypes, sets: l.sets }
      },
      { onSuccess: () => v("Activity availability saved.") }
    );
  };
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-page availability-page", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-page-header", children: [
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("div", { className: "wf-kicker", children: "Workflows" }),
        /* @__PURE__ */ o.jsxs("h2", { children: [
          /* @__PURE__ */ o.jsx(If, { size: 18 }),
          " Activity availability"
        ] }),
        /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "Control which activities can be added to new workflows. Host baseline rules always take precedence." })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { className: "wf-actions", children: [
        A && !u && /* @__PURE__ */ o.jsx("span", { className: "wf-chip availability-dirty", children: "Unsaved changes" }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", className: "availability-save", onClick: Y, disabled: c || u || !A, children: [
          /* @__PURE__ */ o.jsx(ts, { size: 15 }),
          u ? "Saving…" : "Save"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "availability-body", children: [
      D && /* @__PURE__ */ o.jsx("div", { className: "availability-banner availability-banner-error", children: D }),
      w && !D && /* @__PURE__ */ o.jsx("div", { className: "availability-banner availability-banner-success", children: w }),
      /* @__PURE__ */ o.jsxs("div", { className: "availability-mode", role: "group", "aria-label": "Activity availability mode", children: [
        /* @__PURE__ */ o.jsxs("button", { type: "button", className: l.mode === "AllExcept" ? "active" : "", onClick: () => T("AllExcept"), disabled: c || u, children: [
          /* @__PURE__ */ o.jsx(so, { size: 15 }),
          /* @__PURE__ */ o.jsxs("span", { children: [
            /* @__PURE__ */ o.jsx("strong", { children: "All except" }),
            /* @__PURE__ */ o.jsx("em", { children: "Activities are available unless you turn them off" })
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", className: l.mode === "Only" ? "active" : "", onClick: () => T("Only"), disabled: c || u, children: [
          /* @__PURE__ */ o.jsx(Xs, { size: 15 }),
          /* @__PURE__ */ o.jsxs("span", { children: [
            /* @__PURE__ */ o.jsx("strong", { children: "Only" }),
            /* @__PURE__ */ o.jsx("em", { children: "Activities are hidden unless you turn them on" })
          ] })
        ] })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { className: "availability-counts", children: [
        /* @__PURE__ */ o.jsxs("span", { children: [
          /* @__PURE__ */ o.jsx(Xs, { size: 14 }),
          " ",
          z,
          " host blocked"
        ] }),
        /* @__PURE__ */ o.jsxs("span", { children: [
          /* @__PURE__ */ o.jsx(so, { size: 14 }),
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
          /* @__PURE__ */ o.jsx(ns, { size: 14 }),
          " Sets"
        ] }),
        /* @__PURE__ */ o.jsx("div", { className: "availability-set-list", children: N.map((V) => /* @__PURE__ */ o.jsxs("label", { className: "availability-set-option", children: [
          /* @__PURE__ */ o.jsx(
            "input",
            {
              type: "checkbox",
              role: "switch",
              className: "wf-switch-input",
              "aria-label": `Activities in the ${V.name} set available in new workflows`,
              checked: Fh(l, "sets", V.name),
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
            /* @__PURE__ */ o.jsx(rl, { size: 14 }),
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
                onClick: () => m((V) => !V),
                children: [
                  /* @__PURE__ */ o.jsx(so, { size: 13 }),
                  "Hidden only"
                ]
              }
            ),
            /* @__PURE__ */ o.jsxs("div", { className: "wf-search availability-search", children: [
              /* @__PURE__ */ o.jsx(rr, { size: 14 }),
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
              const q = V.entries.filter((X) => S(X).lockedBy === null), J = q.filter((X) => S(X).enabled).length, Z = q.length > 0 && J === q.length, R = J > 0 && !Z;
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
                      onChange: () => W(q.map(jn), !Z)
                    }
                  )
                ] }),
                V.entries.map((X) => {
                  const ue = jn(X), ce = ma(X), ee = It(X.state), ne = S(X), fe = ne.lockedBy !== null, O = fn(_(X)), te = Vn(X), ge = ua(X), me = da(X), _e = x === ce;
                  return /* @__PURE__ */ o.jsxs("div", { className: `availability-activity-row${_e ? " selected" : ""}${fe ? " disabled" : ""}`, children: [
                    /* @__PURE__ */ o.jsxs(
                      "button",
                      {
                        type: "button",
                        className: "availability-activity-open",
                        "aria-expanded": _e,
                        title: "Show activity details",
                        onClick: () => b(_e ? null : ce),
                        children: [
                          /* @__PURE__ */ o.jsx(Fl, { icon: O }),
                          /* @__PURE__ */ o.jsxs("span", { className: "availability-activity-main", children: [
                            /* @__PURE__ */ o.jsxs("span", { className: "availability-activity-title-line", children: [
                              /* @__PURE__ */ o.jsx("strong", { children: te }),
                              ge && /* @__PURE__ */ o.jsx("code", { title: X.activityTypeKey ?? void 0, children: ge })
                            ] }),
                            me && /* @__PURE__ */ o.jsx("span", { className: "availability-activity-description", title: me, children: me })
                          ] })
                        ]
                      }
                    ),
                    ee !== "Available" && /* @__PURE__ */ o.jsx("em", { className: `availability-state ${Rl(X.state)}`, children: zn(X.state) }),
                    /* @__PURE__ */ o.jsx(
                      "input",
                      {
                        type: "checkbox",
                        role: "switch",
                        className: "wf-switch-input",
                        "aria-label": `${te} available in new workflows`,
                        title: pg(ne),
                        checked: ne.enabled,
                        disabled: c || u || fe,
                        onChange: (De) => F(ue, De.target.checked)
                      }
                    )
                  ] }, ce);
                })
              ] }, V.category);
            })
          ] }),
          $ && /* @__PURE__ */ o.jsx(
            gg,
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
        /* @__PURE__ */ o.jsx("div", { className: "availability-unresolved-list", children: y.map((V) => /* @__PURE__ */ o.jsxs("span", { children: [
          /* @__PURE__ */ o.jsx("strong", { children: V.referenceName }),
          /* @__PURE__ */ o.jsx("em", { children: zn(V.state) })
        ] }, `${V.layer}-${V.referenceKind}-${V.referenceName}`)) })
      ] })
    ] })
  ] });
}
function gg({
  entry: e,
  catalogItem: t,
  onClose: n
}) {
  const i = Vn(e), r = e.description?.trim() || t?.description?.trim(), s = le(() => fa(t?.inputs), [t]), a = le(() => fa(t?.outputs), [t]), c = le(() => JSON.stringify({ diagnostic: e, catalog: t }, null, 2), [e, t]), u = [
    ["Type", e.activityTypeKey],
    ["Definition ID", e.activityDefinitionId],
    ["Category", e.category],
    ["Version", t?.version],
    ["Execution", t?.executionType],
    ["Policy layer", Rh(e.layer)]
  ];
  return /* @__PURE__ */ o.jsxs("aside", { className: "availability-details", "aria-label": `${i} details`, children: [
    /* @__PURE__ */ o.jsxs("header", { className: "availability-details-header", children: [
      /* @__PURE__ */ o.jsx(Fl, { icon: fn(t) }),
      /* @__PURE__ */ o.jsx("h4", { children: i }),
      /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-icon-button availability-details-close", "aria-label": "Close details", onClick: n, children: /* @__PURE__ */ o.jsx(is, { size: 14 }) })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "availability-details-body", children: [
      /* @__PURE__ */ o.jsxs("p", { className: "availability-details-status", children: [
        /* @__PURE__ */ o.jsx("em", { className: `availability-state ${Rl(e.state)}`, children: zn(e.state) }),
        e.reason?.trim() && /* @__PURE__ */ o.jsx("span", { children: e.reason })
      ] }),
      r && /* @__PURE__ */ o.jsx("p", { className: "availability-details-description", children: r }),
      /* @__PURE__ */ o.jsx("dl", { className: "availability-details-meta", children: u.map(([l, d]) => d?.trim() ? /* @__PURE__ */ o.jsxs(Ke.Fragment, { children: [
        /* @__PURE__ */ o.jsx("dt", { children: l }),
        /* @__PURE__ */ o.jsx("dd", { children: /* @__PURE__ */ o.jsx("code", { children: d }) })
      ] }, l) : null) }),
      s.length > 0 && /* @__PURE__ */ o.jsx(ya, { title: "Inputs", items: s }),
      a.length > 0 && /* @__PURE__ */ o.jsx(ya, { title: "Outputs", items: a }),
      !t && /* @__PURE__ */ o.jsx("p", { className: "wf-muted availability-details-note", children: "Catalog metadata is unavailable for this activity — it is not exposed by the currently saved policy." }),
      /* @__PURE__ */ o.jsxs("details", { className: "availability-details-raw", children: [
        /* @__PURE__ */ o.jsx("summary", { children: "Raw metadata" }),
        /* @__PURE__ */ o.jsx("pre", { children: c })
      ] })
    ] })
  ] });
}
function ya({ title: e, items: t }) {
  return /* @__PURE__ */ o.jsxs("section", { className: "availability-details-arguments", children: [
    /* @__PURE__ */ o.jsx("h5", { children: e }),
    /* @__PURE__ */ o.jsx("ul", { children: t.map((n) => /* @__PURE__ */ o.jsxs("li", { title: n.description || void 0, children: [
      /* @__PURE__ */ o.jsx("strong", { children: n.name }),
      n.typeName && /* @__PURE__ */ o.jsx("code", { children: ds(n.typeName) })
    ] }, n.name)) })
  ] });
}
const Bl = ["Off", "Metadata", "DiagnosticSnapshot", "Payload"], xa = new Map(Bl.map((e, t) => [e, t])), mg = [
  { id: "workflowInputs", label: "Workflow inputs", detail: "Start arguments and workflow-level input values." },
  { id: "workflowOutputs", label: "Workflow outputs", detail: "Values emitted as workflow outputs." },
  { id: "activityInputs", label: "Activity inputs", detail: "Materialized values passed to activities." },
  { id: "activityOutputs", label: "Activity outputs", detail: "Values produced by completed activities." },
  { id: "containerVariables", label: "Container variables", detail: "Runtime values scoped to containers." },
  { id: "durableValues", label: "Durable values", detail: "Persisted wait/resume payload evidence." },
  { id: "incidents", label: "Incidents", detail: "Fault and incident diagnostic material." },
  { id: "diagnostics", label: "Diagnostics", detail: "Internal runtime diagnostic payloads." }
];
function yg({ context: e }) {
  const t = nh(e), n = ih(e), i = t.data ?? null, r = t.isLoading, s = n.isPending, a = i?.permissions.canManage ?? !1, [c, u] = K(() => wa(i)), [l, d] = K(null);
  Q(() => {
    u(wa(i));
  }, [i]);
  const f = n.error ?? t.error, p = f instanceof Error ? f.message : f ? "Runtime diagnostics settings could not be loaded." : null, h = i?.hostPolicy.limitationReasons ?? [], m = i?.effective.limitationReasons ?? [], w = le(() => Bl.filter((g) => xg(g, i)), [i]), v = (g) => u((y) => ({ ...y, defaultLevel: g })), x = (g, y) => u((N) => {
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
          /* @__PURE__ */ o.jsx(qs, { size: 18 }),
          " Runtime diagnostics"
        ] }),
        /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "Control future runtime value evidence capture. Existing workflow runs keep their captured evidence." })
      ] }),
      /* @__PURE__ */ o.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: b, disabled: r || s || !a, children: [
        /* @__PURE__ */ o.jsx(ts, { size: 15 }),
        s ? "Saving..." : "Save"
      ] }) })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "runtime-diagnostics-body", children: [
      p && /* @__PURE__ */ o.jsx("div", { className: "runtime-diagnostics-banner error", children: p }),
      l && !p && /* @__PURE__ */ o.jsx("div", { className: "runtime-diagnostics-banner success", children: l }),
      /* @__PURE__ */ o.jsxs("section", { className: "runtime-diagnostics-section", children: [
        /* @__PURE__ */ o.jsxs("div", { className: "runtime-diagnostics-section-head", children: [
          /* @__PURE__ */ o.jsxs("h3", { className: "wf-section-label", children: [
            /* @__PURE__ */ o.jsx(Af, { size: 14 }),
            " Default capture"
          ] }),
          i && /* @__PURE__ */ o.jsxs("span", { className: "runtime-diagnostics-effective", children: [
            "Effective: ",
            Nn(i.effective.defaultLevel)
          ] })
        ] }),
        /* @__PURE__ */ o.jsx(
          vg,
          {
            value: c.defaultLevel,
            allowedLevels: w,
            disabled: r || s || !a,
            onChange: v
          }
        )
      ] }),
      /* @__PURE__ */ o.jsxs("section", { className: "runtime-diagnostics-section runtime-diagnostics-section-grow", children: [
        /* @__PURE__ */ o.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ o.jsx(qs, { size: 14 }),
          " Subject overrides"
        ] }),
        /* @__PURE__ */ o.jsx("div", { className: "runtime-diagnostics-subjects", children: mg.map((g) => {
          const y = c.subjectOverrides[g.id] ?? "inherit", N = i ? wg(i, g.id) : null;
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
                  onChange: (j) => x(g.id, j.target.value),
                  children: [
                    /* @__PURE__ */ o.jsx("option", { value: "inherit", children: "Inherit default" }),
                    w.map((j) => /* @__PURE__ */ o.jsx("option", { value: j, children: Nn(j) }, j))
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
function wa(e) {
  return {
    defaultLevel: e?.requested.defaultLevel ?? "DiagnosticSnapshot",
    subjectOverrides: { ...e?.requested.subjectOverrides ?? {} }
  };
}
function xg(e, t) {
  return t ? e === "Payload" && !t.permissions.canEnableFullPayloads ? !1 : (xa.get(e) ?? 0) <= (xa.get(t.hostPolicy.maximumLevel) ?? 0) : e !== "Payload";
}
function wg(e, t) {
  return e.effective.subjectOverrides?.[t] ?? e.effective.defaultLevel;
}
function Nn(e) {
  return e === "DiagnosticSnapshot" ? "Diagnostic snapshot" : e;
}
function vg({
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
        /* @__PURE__ */ o.jsx("em", { children: bg(r) })
      ]
    },
    r
  )) });
}
function bg(e) {
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
function Ee(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, i; n < e.length; n++)
      (i = Ee(e[n])) !== "" && (t += (t && " ") + i);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var jg = { value: () => {
} };
function dr() {
  for (var e = 0, t = arguments.length, n = {}, i; e < t; ++e) {
    if (!(i = arguments[e] + "") || i in n || /[\s.]/.test(i)) throw new Error("illegal type: " + i);
    n[i] = [];
  }
  return new Mi(n);
}
function Mi(e) {
  this._ = e;
}
function Ng(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var i = "", r = n.indexOf(".");
    if (r >= 0 && (i = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: i };
  });
}
Mi.prototype = dr.prototype = {
  constructor: Mi,
  on: function(e, t) {
    var n = this._, i = Ng(e + "", n), r, s = -1, a = i.length;
    if (arguments.length < 2) {
      for (; ++s < a; ) if ((r = (e = i[s]).type) && (r = Sg(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++s < a; )
      if (r = (e = i[s]).type) n[r] = va(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = va(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new Mi(e);
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
function Sg(e, t) {
  for (var n = 0, i = e.length, r; n < i; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function va(e, t, n) {
  for (var i = 0, r = e.length; i < r; ++i)
    if (e[i].name === t) {
      e[i] = jg, e = e.slice(0, i).concat(e.slice(i + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var To = "http://www.w3.org/1999/xhtml";
const ba = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: To,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function fr(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), ba.hasOwnProperty(t) ? { space: ba[t], local: e } : e;
}
function Cg(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === To && t.documentElement.namespaceURI === To ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function kg(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function Kl(e) {
  var t = fr(e);
  return (t.local ? kg : Cg)(t);
}
function Eg() {
}
function hs(e) {
  return e == null ? Eg : function() {
    return this.querySelector(e);
  };
}
function Ig(e) {
  typeof e != "function" && (e = hs(e));
  for (var t = this._groups, n = t.length, i = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = i[r] = new Array(a), u, l, d = 0; d < a; ++d)
      (u = s[d]) && (l = e.call(u, u.__data__, d, s)) && ("__data__" in u && (l.__data__ = u.__data__), c[d] = l);
  return new Xe(i, this._parents);
}
function Ag(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function _g() {
  return [];
}
function Xl(e) {
  return e == null ? _g : function() {
    return this.querySelectorAll(e);
  };
}
function Dg(e) {
  return function() {
    return Ag(e.apply(this, arguments));
  };
}
function Tg(e) {
  typeof e == "function" ? e = Dg(e) : e = Xl(e);
  for (var t = this._groups, n = t.length, i = [], r = [], s = 0; s < n; ++s)
    for (var a = t[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && (i.push(e.call(u, u.__data__, l, a)), r.push(u));
  return new Xe(i, r);
}
function ql(e) {
  return function() {
    return this.matches(e);
  };
}
function Yl(e) {
  return function(t) {
    return t.matches(e);
  };
}
var $g = Array.prototype.find;
function Pg(e) {
  return function() {
    return $g.call(this.children, e);
  };
}
function Mg() {
  return this.firstElementChild;
}
function Rg(e) {
  return this.select(e == null ? Mg : Pg(typeof e == "function" ? e : Yl(e)));
}
var Lg = Array.prototype.filter;
function zg() {
  return Array.from(this.children);
}
function Vg(e) {
  return function() {
    return Lg.call(this.children, e);
  };
}
function Og(e) {
  return this.selectAll(e == null ? zg : Vg(typeof e == "function" ? e : Yl(e)));
}
function Hg(e) {
  typeof e != "function" && (e = ql(e));
  for (var t = this._groups, n = t.length, i = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = i[r] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new Xe(i, this._parents);
}
function Ul(e) {
  return new Array(e.length);
}
function Wg() {
  return new Xe(this._enter || this._groups.map(Ul), this._parents);
}
function qi(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
qi.prototype = {
  constructor: qi,
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
function Fg(e) {
  return function() {
    return e;
  };
}
function Bg(e, t, n, i, r, s) {
  for (var a = 0, c, u = t.length, l = s.length; a < l; ++a)
    (c = t[a]) ? (c.__data__ = s[a], i[a] = c) : n[a] = new qi(e, s[a]);
  for (; a < u; ++a)
    (c = t[a]) && (r[a] = c);
}
function Kg(e, t, n, i, r, s, a) {
  var c, u, l = /* @__PURE__ */ new Map(), d = t.length, f = s.length, p = new Array(d), h;
  for (c = 0; c < d; ++c)
    (u = t[c]) && (p[c] = h = a.call(u, u.__data__, c, t) + "", l.has(h) ? r[c] = u : l.set(h, u));
  for (c = 0; c < f; ++c)
    h = a.call(e, s[c], c, s) + "", (u = l.get(h)) ? (i[c] = u, u.__data__ = s[c], l.delete(h)) : n[c] = new qi(e, s[c]);
  for (c = 0; c < d; ++c)
    (u = t[c]) && l.get(p[c]) === u && (r[c] = u);
}
function Xg(e) {
  return e.__data__;
}
function qg(e, t) {
  if (!arguments.length) return Array.from(this, Xg);
  var n = t ? Kg : Bg, i = this._parents, r = this._groups;
  typeof e != "function" && (e = Fg(e));
  for (var s = r.length, a = new Array(s), c = new Array(s), u = new Array(s), l = 0; l < s; ++l) {
    var d = i[l], f = r[l], p = f.length, h = Yg(e.call(d, d && d.__data__, l, i)), m = h.length, w = c[l] = new Array(m), v = a[l] = new Array(m), x = u[l] = new Array(p);
    n(d, f, w, v, x, h, t);
    for (var b = 0, g = 0, y, N; b < m; ++b)
      if (y = w[b]) {
        for (b >= g && (g = b + 1); !(N = v[g]) && ++g < m; ) ;
        y._next = N || null;
      }
  }
  return a = new Xe(a, i), a._enter = c, a._exit = u, a;
}
function Yg(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function Ug() {
  return new Xe(this._exit || this._groups.map(Ul), this._parents);
}
function Zg(e, t, n) {
  var i = this.enter(), r = this, s = this.exit();
  return typeof e == "function" ? (i = e(i), i && (i = i.selection())) : i = i.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? s.remove() : n(s), i && r ? i.merge(r).order() : r;
}
function Gg(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, i = t._groups, r = n.length, s = i.length, a = Math.min(r, s), c = new Array(r), u = 0; u < a; ++u)
    for (var l = n[u], d = i[u], f = l.length, p = c[u] = new Array(f), h, m = 0; m < f; ++m)
      (h = l[m] || d[m]) && (p[m] = h);
  for (; u < r; ++u)
    c[u] = n[u];
  return new Xe(c, this._parents);
}
function Jg() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var i = e[t], r = i.length - 1, s = i[r], a; --r >= 0; )
      (a = i[r]) && (s && a.compareDocumentPosition(s) ^ 4 && s.parentNode.insertBefore(a, s), s = a);
  return this;
}
function Qg(e) {
  e || (e = em);
  function t(f, p) {
    return f && p ? e(f.__data__, p.__data__) : !f - !p;
  }
  for (var n = this._groups, i = n.length, r = new Array(i), s = 0; s < i; ++s) {
    for (var a = n[s], c = a.length, u = r[s] = new Array(c), l, d = 0; d < c; ++d)
      (l = a[d]) && (u[d] = l);
    u.sort(t);
  }
  return new Xe(r, this._parents).order();
}
function em(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function tm() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function nm() {
  return Array.from(this);
}
function im() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var i = e[t], r = 0, s = i.length; r < s; ++r) {
      var a = i[r];
      if (a) return a;
    }
  return null;
}
function rm() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function om() {
  return !this.node();
}
function sm(e) {
  for (var t = this._groups, n = 0, i = t.length; n < i; ++n)
    for (var r = t[n], s = 0, a = r.length, c; s < a; ++s)
      (c = r[s]) && e.call(c, c.__data__, s, r);
  return this;
}
function am(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function cm(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function lm(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function um(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function dm(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function fm(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function pm(e, t) {
  var n = fr(e);
  if (arguments.length < 2) {
    var i = this.node();
    return n.local ? i.getAttributeNS(n.space, n.local) : i.getAttribute(n);
  }
  return this.each((t == null ? n.local ? cm : am : typeof t == "function" ? n.local ? fm : dm : n.local ? um : lm)(n, t));
}
function Zl(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function hm(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function gm(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function mm(e, t, n) {
  return function() {
    var i = t.apply(this, arguments);
    i == null ? this.style.removeProperty(e) : this.style.setProperty(e, i, n);
  };
}
function ym(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? hm : typeof t == "function" ? mm : gm)(e, t, n ?? "")) : en(this.node(), e);
}
function en(e, t) {
  return e.style.getPropertyValue(t) || Zl(e).getComputedStyle(e, null).getPropertyValue(t);
}
function xm(e) {
  return function() {
    delete this[e];
  };
}
function wm(e, t) {
  return function() {
    this[e] = t;
  };
}
function vm(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function bm(e, t) {
  return arguments.length > 1 ? this.each((t == null ? xm : typeof t == "function" ? vm : wm)(e, t)) : this.node()[e];
}
function Gl(e) {
  return e.trim().split(/^|\s+/);
}
function gs(e) {
  return e.classList || new Jl(e);
}
function Jl(e) {
  this._node = e, this._names = Gl(e.getAttribute("class") || "");
}
Jl.prototype = {
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
function Ql(e, t) {
  for (var n = gs(e), i = -1, r = t.length; ++i < r; ) n.add(t[i]);
}
function eu(e, t) {
  for (var n = gs(e), i = -1, r = t.length; ++i < r; ) n.remove(t[i]);
}
function jm(e) {
  return function() {
    Ql(this, e);
  };
}
function Nm(e) {
  return function() {
    eu(this, e);
  };
}
function Sm(e, t) {
  return function() {
    (t.apply(this, arguments) ? Ql : eu)(this, e);
  };
}
function Cm(e, t) {
  var n = Gl(e + "");
  if (arguments.length < 2) {
    for (var i = gs(this.node()), r = -1, s = n.length; ++r < s; ) if (!i.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? Sm : t ? jm : Nm)(n, t));
}
function km() {
  this.textContent = "";
}
function Em(e) {
  return function() {
    this.textContent = e;
  };
}
function Im(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function Am(e) {
  return arguments.length ? this.each(e == null ? km : (typeof e == "function" ? Im : Em)(e)) : this.node().textContent;
}
function _m() {
  this.innerHTML = "";
}
function Dm(e) {
  return function() {
    this.innerHTML = e;
  };
}
function Tm(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function $m(e) {
  return arguments.length ? this.each(e == null ? _m : (typeof e == "function" ? Tm : Dm)(e)) : this.node().innerHTML;
}
function Pm() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function Mm() {
  return this.each(Pm);
}
function Rm() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Lm() {
  return this.each(Rm);
}
function zm(e) {
  var t = typeof e == "function" ? e : Kl(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Vm() {
  return null;
}
function Om(e, t) {
  var n = typeof e == "function" ? e : Kl(e), i = t == null ? Vm : typeof t == "function" ? t : hs(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), i.apply(this, arguments) || null);
  });
}
function Hm() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Wm() {
  return this.each(Hm);
}
function Fm() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Bm() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Km(e) {
  return this.select(e ? Bm : Fm);
}
function Xm(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function qm(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function Ym(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", i = t.indexOf(".");
    return i >= 0 && (n = t.slice(i + 1), t = t.slice(0, i)), { type: t, name: n };
  });
}
function Um(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, i = -1, r = t.length, s; n < r; ++n)
        s = t[n], (!e.type || s.type === e.type) && s.name === e.name ? this.removeEventListener(s.type, s.listener, s.options) : t[++i] = s;
      ++i ? t.length = i : delete this.__on;
    }
  };
}
function Zm(e, t, n) {
  return function() {
    var i = this.__on, r, s = qm(t);
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
function Gm(e, t, n) {
  var i = Ym(e + ""), r, s = i.length, a;
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
  for (c = t ? Zm : Um, r = 0; r < s; ++r) this.each(c(i[r], t, n));
  return this;
}
function tu(e, t, n) {
  var i = Zl(e), r = i.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = i.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function Jm(e, t) {
  return function() {
    return tu(this, e, t);
  };
}
function Qm(e, t) {
  return function() {
    return tu(this, e, t.apply(this, arguments));
  };
}
function ey(e, t) {
  return this.each((typeof t == "function" ? Qm : Jm)(e, t));
}
function* ty() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var i = e[t], r = 0, s = i.length, a; r < s; ++r)
      (a = i[r]) && (yield a);
}
var nu = [null];
function Xe(e, t) {
  this._groups = e, this._parents = t;
}
function Qn() {
  return new Xe([[document.documentElement]], nu);
}
function ny() {
  return this;
}
Xe.prototype = Qn.prototype = {
  constructor: Xe,
  select: Ig,
  selectAll: Tg,
  selectChild: Rg,
  selectChildren: Og,
  filter: Hg,
  data: qg,
  enter: Wg,
  exit: Ug,
  join: Zg,
  merge: Gg,
  selection: ny,
  order: Jg,
  sort: Qg,
  call: tm,
  nodes: nm,
  node: im,
  size: rm,
  empty: om,
  each: sm,
  attr: pm,
  style: ym,
  property: bm,
  classed: Cm,
  text: Am,
  html: $m,
  raise: Mm,
  lower: Lm,
  append: zm,
  insert: Om,
  remove: Wm,
  clone: Km,
  datum: Xm,
  on: Gm,
  dispatch: ey,
  [Symbol.iterator]: ty
};
function Fe(e) {
  return typeof e == "string" ? new Xe([[document.querySelector(e)]], [document.documentElement]) : new Xe([[e]], nu);
}
function iy(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Ze(e, t) {
  if (e = iy(e), t === void 0 && (t = e.currentTarget), t) {
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
const ry = { passive: !1 }, On = { capture: !0, passive: !1 };
function fo(e) {
  e.stopImmediatePropagation();
}
function Zt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function iu(e) {
  var t = e.document.documentElement, n = Fe(e).on("dragstart.drag", Zt, On);
  "onselectstart" in t ? n.on("selectstart.drag", Zt, On) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function ru(e, t) {
  var n = e.document.documentElement, i = Fe(e).on("dragstart.drag", null);
  t && (i.on("click.drag", Zt, On), setTimeout(function() {
    i.on("click.drag", null);
  }, 0)), "onselectstart" in n ? i.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const bi = (e) => () => e;
function $o(e, {
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
$o.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function oy(e) {
  return !e.ctrlKey && !e.button;
}
function sy() {
  return this.parentNode;
}
function ay(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function cy() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function ou() {
  var e = oy, t = sy, n = ay, i = cy, r = {}, s = dr("start", "drag", "end"), a = 0, c, u, l, d, f = 0;
  function p(y) {
    y.on("mousedown.drag", h).filter(i).on("touchstart.drag", v).on("touchmove.drag", x, ry).on("touchend.drag touchcancel.drag", b).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function h(y, N) {
    if (!(d || !e.call(this, y, N))) {
      var j = g(this, t.call(this, y, N), y, N, "mouse");
      j && (Fe(y.view).on("mousemove.drag", m, On).on("mouseup.drag", w, On), iu(y.view), fo(y), l = !1, c = y.clientX, u = y.clientY, j("start", y));
    }
  }
  function m(y) {
    if (Zt(y), !l) {
      var N = y.clientX - c, j = y.clientY - u;
      l = N * N + j * j > f;
    }
    r.mouse("drag", y);
  }
  function w(y) {
    Fe(y.view).on("mousemove.drag mouseup.drag", null), ru(y.view, l), Zt(y), r.mouse("end", y);
  }
  function v(y, N) {
    if (e.call(this, y, N)) {
      var j = y.changedTouches, S = t.call(this, y, N), E = j.length, _, M;
      for (_ = 0; _ < E; ++_)
        (M = g(this, S, y, N, j[_].identifier, j[_])) && (fo(y), M("start", y, j[_]));
    }
  }
  function x(y) {
    var N = y.changedTouches, j = N.length, S, E;
    for (S = 0; S < j; ++S)
      (E = r[N[S].identifier]) && (Zt(y), E("drag", y, N[S]));
  }
  function b(y) {
    var N = y.changedTouches, j = N.length, S, E;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), S = 0; S < j; ++S)
      (E = r[N[S].identifier]) && (fo(y), E("end", y, N[S]));
  }
  function g(y, N, j, S, E, _) {
    var M = s.copy(), I = Ze(_ || j, N), $, z, C;
    if ((C = n.call(y, new $o("beforestart", {
      sourceEvent: j,
      target: p,
      identifier: E,
      active: a,
      x: I[0],
      y: I[1],
      dx: 0,
      dy: 0,
      dispatch: M
    }), S)) != null)
      return $ = C.x - I[0] || 0, z = C.y - I[1] || 0, function A(k, D, P) {
        var T = I, F;
        switch (k) {
          case "start":
            r[E] = A, F = a++;
            break;
          case "end":
            delete r[E], --a;
          // falls through
          case "drag":
            I = Ze(P || D, N), F = a;
            break;
        }
        M.call(
          k,
          y,
          new $o(k, {
            sourceEvent: D,
            subject: C,
            target: p,
            identifier: E,
            active: F,
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
  return p.filter = function(y) {
    return arguments.length ? (e = typeof y == "function" ? y : bi(!!y), p) : e;
  }, p.container = function(y) {
    return arguments.length ? (t = typeof y == "function" ? y : bi(y), p) : t;
  }, p.subject = function(y) {
    return arguments.length ? (n = typeof y == "function" ? y : bi(y), p) : n;
  }, p.touchable = function(y) {
    return arguments.length ? (i = typeof y == "function" ? y : bi(!!y), p) : i;
  }, p.on = function() {
    var y = s.on.apply(s, arguments);
    return y === s ? p : y;
  }, p.clickDistance = function(y) {
    return arguments.length ? (f = (y = +y) * y, p) : Math.sqrt(f);
  }, p;
}
function ms(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function su(e, t) {
  var n = Object.create(e.prototype);
  for (var i in t) n[i] = t[i];
  return n;
}
function ei() {
}
var Hn = 0.7, Yi = 1 / Hn, Gt = "\\s*([+-]?\\d+)\\s*", Wn = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", nt = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", ly = /^#([0-9a-f]{3,8})$/, uy = new RegExp(`^rgb\\(${Gt},${Gt},${Gt}\\)$`), dy = new RegExp(`^rgb\\(${nt},${nt},${nt}\\)$`), fy = new RegExp(`^rgba\\(${Gt},${Gt},${Gt},${Wn}\\)$`), py = new RegExp(`^rgba\\(${nt},${nt},${nt},${Wn}\\)$`), hy = new RegExp(`^hsl\\(${Wn},${nt},${nt}\\)$`), gy = new RegExp(`^hsla\\(${Wn},${nt},${nt},${Wn}\\)$`), ja = {
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
ms(ei, _t, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Na,
  // Deprecated! Use color.formatHex.
  formatHex: Na,
  formatHex8: my,
  formatHsl: yy,
  formatRgb: Sa,
  toString: Sa
});
function Na() {
  return this.rgb().formatHex();
}
function my() {
  return this.rgb().formatHex8();
}
function yy() {
  return au(this).formatHsl();
}
function Sa() {
  return this.rgb().formatRgb();
}
function _t(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = ly.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? Ca(t) : n === 3 ? new Oe(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? ji(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? ji(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = uy.exec(e)) ? new Oe(t[1], t[2], t[3], 1) : (t = dy.exec(e)) ? new Oe(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = fy.exec(e)) ? ji(t[1], t[2], t[3], t[4]) : (t = py.exec(e)) ? ji(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = hy.exec(e)) ? Ia(t[1], t[2] / 100, t[3] / 100, 1) : (t = gy.exec(e)) ? Ia(t[1], t[2] / 100, t[3] / 100, t[4]) : ja.hasOwnProperty(e) ? Ca(ja[e]) : e === "transparent" ? new Oe(NaN, NaN, NaN, 0) : null;
}
function Ca(e) {
  return new Oe(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function ji(e, t, n, i) {
  return i <= 0 && (e = t = n = NaN), new Oe(e, t, n, i);
}
function xy(e) {
  return e instanceof ei || (e = _t(e)), e ? (e = e.rgb(), new Oe(e.r, e.g, e.b, e.opacity)) : new Oe();
}
function Po(e, t, n, i) {
  return arguments.length === 1 ? xy(e) : new Oe(e, t, n, i ?? 1);
}
function Oe(e, t, n, i) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +i;
}
ms(Oe, Po, su(ei, {
  brighter(e) {
    return e = e == null ? Yi : Math.pow(Yi, e), new Oe(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Hn : Math.pow(Hn, e), new Oe(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new Oe(Ct(this.r), Ct(this.g), Ct(this.b), Ui(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: ka,
  // Deprecated! Use color.formatHex.
  formatHex: ka,
  formatHex8: wy,
  formatRgb: Ea,
  toString: Ea
}));
function ka() {
  return `#${St(this.r)}${St(this.g)}${St(this.b)}`;
}
function wy() {
  return `#${St(this.r)}${St(this.g)}${St(this.b)}${St((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function Ea() {
  const e = Ui(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${Ct(this.r)}, ${Ct(this.g)}, ${Ct(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Ui(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function Ct(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function St(e) {
  return e = Ct(e), (e < 16 ? "0" : "") + e.toString(16);
}
function Ia(e, t, n, i) {
  return i <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Ge(e, t, n, i);
}
function au(e) {
  if (e instanceof Ge) return new Ge(e.h, e.s, e.l, e.opacity);
  if (e instanceof ei || (e = _t(e)), !e) return new Ge();
  if (e instanceof Ge) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, i = e.b / 255, r = Math.min(t, n, i), s = Math.max(t, n, i), a = NaN, c = s - r, u = (s + r) / 2;
  return c ? (t === s ? a = (n - i) / c + (n < i) * 6 : n === s ? a = (i - t) / c + 2 : a = (t - n) / c + 4, c /= u < 0.5 ? s + r : 2 - s - r, a *= 60) : c = u > 0 && u < 1 ? 0 : a, new Ge(a, c, u, e.opacity);
}
function vy(e, t, n, i) {
  return arguments.length === 1 ? au(e) : new Ge(e, t, n, i ?? 1);
}
function Ge(e, t, n, i) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +i;
}
ms(Ge, vy, su(ei, {
  brighter(e) {
    return e = e == null ? Yi : Math.pow(Yi, e), new Ge(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Hn : Math.pow(Hn, e), new Ge(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, i = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - i;
    return new Oe(
      po(e >= 240 ? e - 240 : e + 120, r, i),
      po(e, r, i),
      po(e < 120 ? e + 240 : e - 120, r, i),
      this.opacity
    );
  },
  clamp() {
    return new Ge(Aa(this.h), Ni(this.s), Ni(this.l), Ui(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Ui(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${Aa(this.h)}, ${Ni(this.s) * 100}%, ${Ni(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function Aa(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function Ni(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function po(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const ys = (e) => () => e;
function by(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function jy(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(i) {
    return Math.pow(e + i * t, n);
  };
}
function Ny(e) {
  return (e = +e) == 1 ? cu : function(t, n) {
    return n - t ? jy(t, n, e) : ys(isNaN(t) ? n : t);
  };
}
function cu(e, t) {
  var n = t - e;
  return n ? by(e, n) : ys(isNaN(e) ? t : e);
}
const Zi = (function e(t) {
  var n = Ny(t);
  function i(r, s) {
    var a = n((r = Po(r)).r, (s = Po(s)).r), c = n(r.g, s.g), u = n(r.b, s.b), l = cu(r.opacity, s.opacity);
    return function(d) {
      return r.r = a(d), r.g = c(d), r.b = u(d), r.opacity = l(d), r + "";
    };
  }
  return i.gamma = e, i;
})(1);
function Sy(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, i = t.slice(), r;
  return function(s) {
    for (r = 0; r < n; ++r) i[r] = e[r] * (1 - s) + t[r] * s;
    return i;
  };
}
function Cy(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function ky(e, t) {
  var n = t ? t.length : 0, i = e ? Math.min(n, e.length) : 0, r = new Array(i), s = new Array(n), a;
  for (a = 0; a < i; ++a) r[a] = _n(e[a], t[a]);
  for (; a < n; ++a) s[a] = t[a];
  return function(c) {
    for (a = 0; a < i; ++a) s[a] = r[a](c);
    return s;
  };
}
function Ey(e, t) {
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
function Iy(e, t) {
  var n = {}, i = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = _n(e[r], t[r]) : i[r] = t[r];
  return function(s) {
    for (r in n) i[r] = n[r](s);
    return i;
  };
}
var Mo = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, ho = new RegExp(Mo.source, "g");
function Ay(e) {
  return function() {
    return e;
  };
}
function _y(e) {
  return function(t) {
    return e(t) + "";
  };
}
function lu(e, t) {
  var n = Mo.lastIndex = ho.lastIndex = 0, i, r, s, a = -1, c = [], u = [];
  for (e = e + "", t = t + ""; (i = Mo.exec(e)) && (r = ho.exec(t)); )
    (s = r.index) > n && (s = t.slice(n, s), c[a] ? c[a] += s : c[++a] = s), (i = i[0]) === (r = r[0]) ? c[a] ? c[a] += r : c[++a] = r : (c[++a] = null, u.push({ i: a, x: tt(i, r) })), n = ho.lastIndex;
  return n < t.length && (s = t.slice(n), c[a] ? c[a] += s : c[++a] = s), c.length < 2 ? u[0] ? _y(u[0].x) : Ay(t) : (t = u.length, function(l) {
    for (var d = 0, f; d < t; ++d) c[(f = u[d]).i] = f.x(l);
    return c.join("");
  });
}
function _n(e, t) {
  var n = typeof t, i;
  return t == null || n === "boolean" ? ys(t) : (n === "number" ? tt : n === "string" ? (i = _t(t)) ? (t = i, Zi) : lu : t instanceof _t ? Zi : t instanceof Date ? Ey : Cy(t) ? Sy : Array.isArray(t) ? ky : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Iy : tt)(e, t);
}
var _a = 180 / Math.PI, Ro = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function uu(e, t, n, i, r, s) {
  var a, c, u;
  return (a = Math.sqrt(e * e + t * t)) && (e /= a, t /= a), (u = e * n + t * i) && (n -= e * u, i -= t * u), (c = Math.sqrt(n * n + i * i)) && (n /= c, i /= c, u /= c), e * i < t * n && (e = -e, t = -t, u = -u, a = -a), {
    translateX: r,
    translateY: s,
    rotate: Math.atan2(t, e) * _a,
    skewX: Math.atan(u) * _a,
    scaleX: a,
    scaleY: c
  };
}
var Si;
function Dy(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Ro : uu(t.a, t.b, t.c, t.d, t.e, t.f);
}
function Ty(e) {
  return e == null || (Si || (Si = document.createElementNS("http://www.w3.org/2000/svg", "g")), Si.setAttribute("transform", e), !(e = Si.transform.baseVal.consolidate())) ? Ro : (e = e.matrix, uu(e.a, e.b, e.c, e.d, e.e, e.f));
}
function du(e, t, n, i) {
  function r(l) {
    return l.length ? l.pop() + " " : "";
  }
  function s(l, d, f, p, h, m) {
    if (l !== f || d !== p) {
      var w = h.push("translate(", null, t, null, n);
      m.push({ i: w - 4, x: tt(l, f) }, { i: w - 2, x: tt(d, p) });
    } else (f || p) && h.push("translate(" + f + t + p + n);
  }
  function a(l, d, f, p) {
    l !== d ? (l - d > 180 ? d += 360 : d - l > 180 && (l += 360), p.push({ i: f.push(r(f) + "rotate(", null, i) - 2, x: tt(l, d) })) : d && f.push(r(f) + "rotate(" + d + i);
  }
  function c(l, d, f, p) {
    l !== d ? p.push({ i: f.push(r(f) + "skewX(", null, i) - 2, x: tt(l, d) }) : d && f.push(r(f) + "skewX(" + d + i);
  }
  function u(l, d, f, p, h, m) {
    if (l !== f || d !== p) {
      var w = h.push(r(h) + "scale(", null, ",", null, ")");
      m.push({ i: w - 4, x: tt(l, f) }, { i: w - 2, x: tt(d, p) });
    } else (f !== 1 || p !== 1) && h.push(r(h) + "scale(" + f + "," + p + ")");
  }
  return function(l, d) {
    var f = [], p = [];
    return l = e(l), d = e(d), s(l.translateX, l.translateY, d.translateX, d.translateY, f, p), a(l.rotate, d.rotate, f, p), c(l.skewX, d.skewX, f, p), u(l.scaleX, l.scaleY, d.scaleX, d.scaleY, f, p), l = d = null, function(h) {
      for (var m = -1, w = p.length, v; ++m < w; ) f[(v = p[m]).i] = v.x(h);
      return f.join("");
    };
  };
}
var $y = du(Dy, "px, ", "px)", "deg)"), Py = du(Ty, ", ", ")", ")"), My = 1e-12;
function Da(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Ry(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Ly(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const Ri = (function e(t, n, i) {
  function r(s, a) {
    var c = s[0], u = s[1], l = s[2], d = a[0], f = a[1], p = a[2], h = d - c, m = f - u, w = h * h + m * m, v, x;
    if (w < My)
      x = Math.log(p / l) / t, v = function(S) {
        return [
          c + S * h,
          u + S * m,
          l * Math.exp(t * S * x)
        ];
      };
    else {
      var b = Math.sqrt(w), g = (p * p - l * l + i * w) / (2 * l * n * b), y = (p * p - l * l - i * w) / (2 * p * n * b), N = Math.log(Math.sqrt(g * g + 1) - g), j = Math.log(Math.sqrt(y * y + 1) - y);
      x = (j - N) / t, v = function(S) {
        var E = S * x, _ = Da(N), M = l / (n * b) * (_ * Ly(t * E + N) - Ry(N));
        return [
          c + M * h,
          u + M * m,
          l * _ / Da(t * E + N)
        ];
      };
    }
    return v.duration = x * 1e3 * t / Math.SQRT2, v;
  }
  return r.rho = function(s) {
    var a = Math.max(1e-3, +s), c = a * a, u = c * c;
    return e(a, c, u);
  }, r;
})(Math.SQRT2, 2, 4);
var tn = 0, Sn = 0, xn = 0, fu = 1e3, Gi, Cn, Ji = 0, Dt = 0, pr = 0, Fn = typeof performance == "object" && performance.now ? performance : Date, pu = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function xs() {
  return Dt || (pu(zy), Dt = Fn.now() + pr);
}
function zy() {
  Dt = 0;
}
function Qi() {
  this._call = this._time = this._next = null;
}
Qi.prototype = hu.prototype = {
  constructor: Qi,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? xs() : +n) + (t == null ? 0 : +t), !this._next && Cn !== this && (Cn ? Cn._next = this : Gi = this, Cn = this), this._call = e, this._time = n, Lo();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Lo());
  }
};
function hu(e, t, n) {
  var i = new Qi();
  return i.restart(e, t, n), i;
}
function Vy() {
  xs(), ++tn;
  for (var e = Gi, t; e; )
    (t = Dt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --tn;
}
function Ta() {
  Dt = (Ji = Fn.now()) + pr, tn = Sn = 0;
  try {
    Vy();
  } finally {
    tn = 0, Hy(), Dt = 0;
  }
}
function Oy() {
  var e = Fn.now(), t = e - Ji;
  t > fu && (pr -= t, Ji = e);
}
function Hy() {
  for (var e, t = Gi, n, i = 1 / 0; t; )
    t._call ? (i > t._time && (i = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Gi = n);
  Cn = e, Lo(i);
}
function Lo(e) {
  if (!tn) {
    Sn && (Sn = clearTimeout(Sn));
    var t = e - Dt;
    t > 24 ? (e < 1 / 0 && (Sn = setTimeout(Ta, e - Fn.now() - pr)), xn && (xn = clearInterval(xn))) : (xn || (Ji = Fn.now(), xn = setInterval(Oy, fu)), tn = 1, pu(Ta));
  }
}
function $a(e, t, n) {
  var i = new Qi();
  return t = t == null ? 0 : +t, i.restart((r) => {
    i.stop(), e(r + t);
  }, t, n), i;
}
var Wy = dr("start", "end", "cancel", "interrupt"), Fy = [], gu = 0, Pa = 1, zo = 2, Li = 3, Ma = 4, Vo = 5, zi = 6;
function hr(e, t, n, i, r, s) {
  var a = e.__transition;
  if (!a) e.__transition = {};
  else if (n in a) return;
  By(e, n, {
    name: t,
    index: i,
    // For context during callback.
    group: r,
    // For context during callback.
    on: Wy,
    tween: Fy,
    time: s.time,
    delay: s.delay,
    duration: s.duration,
    ease: s.ease,
    timer: null,
    state: gu
  });
}
function ws(e, t) {
  var n = et(e, t);
  if (n.state > gu) throw new Error("too late; already scheduled");
  return n;
}
function ot(e, t) {
  var n = et(e, t);
  if (n.state > Li) throw new Error("too late; already running");
  return n;
}
function et(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function By(e, t, n) {
  var i = e.__transition, r;
  i[t] = n, n.timer = hu(s, 0, n.time);
  function s(l) {
    n.state = Pa, n.timer.restart(a, n.delay, n.time), n.delay <= l && a(l - n.delay);
  }
  function a(l) {
    var d, f, p, h;
    if (n.state !== Pa) return u();
    for (d in i)
      if (h = i[d], h.name === n.name) {
        if (h.state === Li) return $a(a);
        h.state === Ma ? (h.state = zi, h.timer.stop(), h.on.call("interrupt", e, e.__data__, h.index, h.group), delete i[d]) : +d < t && (h.state = zi, h.timer.stop(), h.on.call("cancel", e, e.__data__, h.index, h.group), delete i[d]);
      }
    if ($a(function() {
      n.state === Li && (n.state = Ma, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = zo, n.on.call("start", e, e.__data__, n.index, n.group), n.state === zo) {
      for (n.state = Li, r = new Array(p = n.tween.length), d = 0, f = -1; d < p; ++d)
        (h = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (r[++f] = h);
      r.length = f + 1;
    }
  }
  function c(l) {
    for (var d = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(u), n.state = Vo, 1), f = -1, p = r.length; ++f < p; )
      r[f].call(e, d);
    n.state === Vo && (n.on.call("end", e, e.__data__, n.index, n.group), u());
  }
  function u() {
    n.state = zi, n.timer.stop(), delete i[t];
    for (var l in i) return;
    delete e.__transition;
  }
}
function Vi(e, t) {
  var n = e.__transition, i, r, s = !0, a;
  if (n) {
    t = t == null ? null : t + "";
    for (a in n) {
      if ((i = n[a]).name !== t) {
        s = !1;
        continue;
      }
      r = i.state > zo && i.state < Vo, i.state = zi, i.timer.stop(), i.on.call(r ? "interrupt" : "cancel", e, e.__data__, i.index, i.group), delete n[a];
    }
    s && delete e.__transition;
  }
}
function Ky(e) {
  return this.each(function() {
    Vi(this, e);
  });
}
function Xy(e, t) {
  var n, i;
  return function() {
    var r = ot(this, e), s = r.tween;
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
function qy(e, t, n) {
  var i, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var s = ot(this, e), a = s.tween;
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
function Yy(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var i = et(this.node(), n).tween, r = 0, s = i.length, a; r < s; ++r)
      if ((a = i[r]).name === e)
        return a.value;
    return null;
  }
  return this.each((t == null ? Xy : qy)(n, e, t));
}
function vs(e, t, n) {
  var i = e._id;
  return e.each(function() {
    var r = ot(this, i);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return et(r, i).value[t];
  };
}
function mu(e, t) {
  var n;
  return (typeof t == "number" ? tt : t instanceof _t ? Zi : (n = _t(t)) ? (t = n, Zi) : lu)(e, t);
}
function Uy(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Zy(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Gy(e, t, n) {
  var i, r = n + "", s;
  return function() {
    var a = this.getAttribute(e);
    return a === r ? null : a === i ? s : s = t(i = a, n);
  };
}
function Jy(e, t, n) {
  var i, r = n + "", s;
  return function() {
    var a = this.getAttributeNS(e.space, e.local);
    return a === r ? null : a === i ? s : s = t(i = a, n);
  };
}
function Qy(e, t, n) {
  var i, r, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttribute(e) : (a = this.getAttribute(e), u = c + "", a === u ? null : a === i && u === r ? s : (r = u, s = t(i = a, c)));
  };
}
function ex(e, t, n) {
  var i, r, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (a = this.getAttributeNS(e.space, e.local), u = c + "", a === u ? null : a === i && u === r ? s : (r = u, s = t(i = a, c)));
  };
}
function tx(e, t) {
  var n = fr(e), i = n === "transform" ? Py : mu;
  return this.attrTween(e, typeof t == "function" ? (n.local ? ex : Qy)(n, i, vs(this, "attr." + e, t)) : t == null ? (n.local ? Zy : Uy)(n) : (n.local ? Jy : Gy)(n, i, t));
}
function nx(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function ix(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function rx(e, t) {
  var n, i;
  function r() {
    var s = t.apply(this, arguments);
    return s !== i && (n = (i = s) && ix(e, s)), n;
  }
  return r._value = t, r;
}
function ox(e, t) {
  var n, i;
  function r() {
    var s = t.apply(this, arguments);
    return s !== i && (n = (i = s) && nx(e, s)), n;
  }
  return r._value = t, r;
}
function sx(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var i = fr(e);
  return this.tween(n, (i.local ? rx : ox)(i, t));
}
function ax(e, t) {
  return function() {
    ws(this, e).delay = +t.apply(this, arguments);
  };
}
function cx(e, t) {
  return t = +t, function() {
    ws(this, e).delay = t;
  };
}
function lx(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? ax : cx)(t, e)) : et(this.node(), t).delay;
}
function ux(e, t) {
  return function() {
    ot(this, e).duration = +t.apply(this, arguments);
  };
}
function dx(e, t) {
  return t = +t, function() {
    ot(this, e).duration = t;
  };
}
function fx(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? ux : dx)(t, e)) : et(this.node(), t).duration;
}
function px(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    ot(this, e).ease = t;
  };
}
function hx(e) {
  var t = this._id;
  return arguments.length ? this.each(px(t, e)) : et(this.node(), t).ease;
}
function gx(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    ot(this, e).ease = n;
  };
}
function mx(e) {
  if (typeof e != "function") throw new Error();
  return this.each(gx(this._id, e));
}
function yx(e) {
  typeof e != "function" && (e = ql(e));
  for (var t = this._groups, n = t.length, i = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = i[r] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new dt(i, this._parents, this._name, this._id);
}
function xx(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, i = t.length, r = n.length, s = Math.min(i, r), a = new Array(i), c = 0; c < s; ++c)
    for (var u = t[c], l = n[c], d = u.length, f = a[c] = new Array(d), p, h = 0; h < d; ++h)
      (p = u[h] || l[h]) && (f[h] = p);
  for (; c < i; ++c)
    a[c] = t[c];
  return new dt(a, this._parents, this._name, this._id);
}
function wx(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function vx(e, t, n) {
  var i, r, s = wx(t) ? ws : ot;
  return function() {
    var a = s(this, e), c = a.on;
    c !== i && (r = (i = c).copy()).on(t, n), a.on = r;
  };
}
function bx(e, t) {
  var n = this._id;
  return arguments.length < 2 ? et(this.node(), n).on.on(e) : this.each(vx(n, e, t));
}
function jx(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function Nx() {
  return this.on("end.remove", jx(this._id));
}
function Sx(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = hs(e));
  for (var i = this._groups, r = i.length, s = new Array(r), a = 0; a < r; ++a)
    for (var c = i[a], u = c.length, l = s[a] = new Array(u), d, f, p = 0; p < u; ++p)
      (d = c[p]) && (f = e.call(d, d.__data__, p, c)) && ("__data__" in d && (f.__data__ = d.__data__), l[p] = f, hr(l[p], t, n, p, l, et(d, n)));
  return new dt(s, this._parents, t, n);
}
function Cx(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Xl(e));
  for (var i = this._groups, r = i.length, s = [], a = [], c = 0; c < r; ++c)
    for (var u = i[c], l = u.length, d, f = 0; f < l; ++f)
      if (d = u[f]) {
        for (var p = e.call(d, d.__data__, f, u), h, m = et(d, n), w = 0, v = p.length; w < v; ++w)
          (h = p[w]) && hr(h, t, n, w, p, m);
        s.push(p), a.push(d);
      }
  return new dt(s, a, t, n);
}
var kx = Qn.prototype.constructor;
function Ex() {
  return new kx(this._groups, this._parents);
}
function Ix(e, t) {
  var n, i, r;
  return function() {
    var s = en(this, e), a = (this.style.removeProperty(e), en(this, e));
    return s === a ? null : s === n && a === i ? r : r = t(n = s, i = a);
  };
}
function yu(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Ax(e, t, n) {
  var i, r = n + "", s;
  return function() {
    var a = en(this, e);
    return a === r ? null : a === i ? s : s = t(i = a, n);
  };
}
function _x(e, t, n) {
  var i, r, s;
  return function() {
    var a = en(this, e), c = n(this), u = c + "";
    return c == null && (u = c = (this.style.removeProperty(e), en(this, e))), a === u ? null : a === i && u === r ? s : (r = u, s = t(i = a, c));
  };
}
function Dx(e, t) {
  var n, i, r, s = "style." + t, a = "end." + s, c;
  return function() {
    var u = ot(this, e), l = u.on, d = u.value[s] == null ? c || (c = yu(t)) : void 0;
    (l !== n || r !== d) && (i = (n = l).copy()).on(a, r = d), u.on = i;
  };
}
function Tx(e, t, n) {
  var i = (e += "") == "transform" ? $y : mu;
  return t == null ? this.styleTween(e, Ix(e, i)).on("end.style." + e, yu(e)) : typeof t == "function" ? this.styleTween(e, _x(e, i, vs(this, "style." + e, t))).each(Dx(this._id, e)) : this.styleTween(e, Ax(e, i, t), n).on("end.style." + e, null);
}
function $x(e, t, n) {
  return function(i) {
    this.style.setProperty(e, t.call(this, i), n);
  };
}
function Px(e, t, n) {
  var i, r;
  function s() {
    var a = t.apply(this, arguments);
    return a !== r && (i = (r = a) && $x(e, a, n)), i;
  }
  return s._value = t, s;
}
function Mx(e, t, n) {
  var i = "style." + (e += "");
  if (arguments.length < 2) return (i = this.tween(i)) && i._value;
  if (t == null) return this.tween(i, null);
  if (typeof t != "function") throw new Error();
  return this.tween(i, Px(e, t, n ?? ""));
}
function Rx(e) {
  return function() {
    this.textContent = e;
  };
}
function Lx(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function zx(e) {
  return this.tween("text", typeof e == "function" ? Lx(vs(this, "text", e)) : Rx(e == null ? "" : e + ""));
}
function Vx(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Ox(e) {
  var t, n;
  function i() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Vx(r)), t;
  }
  return i._value = e, i;
}
function Hx(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, Ox(e));
}
function Wx() {
  for (var e = this._name, t = this._id, n = xu(), i = this._groups, r = i.length, s = 0; s < r; ++s)
    for (var a = i[s], c = a.length, u, l = 0; l < c; ++l)
      if (u = a[l]) {
        var d = et(u, t);
        hr(u, e, n, l, a, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new dt(i, this._parents, e, n);
}
function Fx() {
  var e, t, n = this, i = n._id, r = n.size();
  return new Promise(function(s, a) {
    var c = { value: a }, u = { value: function() {
      --r === 0 && s();
    } };
    n.each(function() {
      var l = ot(this, i), d = l.on;
      d !== e && (t = (e = d).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(u)), l.on = t;
    }), r === 0 && s();
  });
}
var Bx = 0;
function dt(e, t, n, i) {
  this._groups = e, this._parents = t, this._name = n, this._id = i;
}
function xu() {
  return ++Bx;
}
var at = Qn.prototype;
dt.prototype = {
  constructor: dt,
  select: Sx,
  selectAll: Cx,
  selectChild: at.selectChild,
  selectChildren: at.selectChildren,
  filter: yx,
  merge: xx,
  selection: Ex,
  transition: Wx,
  call: at.call,
  nodes: at.nodes,
  node: at.node,
  size: at.size,
  empty: at.empty,
  each: at.each,
  on: bx,
  attr: tx,
  attrTween: sx,
  style: Tx,
  styleTween: Mx,
  text: zx,
  textTween: Hx,
  remove: Nx,
  tween: Yy,
  delay: lx,
  duration: fx,
  ease: hx,
  easeVarying: mx,
  end: Fx,
  [Symbol.iterator]: at[Symbol.iterator]
};
function Kx(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var Xx = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: Kx
};
function qx(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function Yx(e) {
  var t, n;
  e instanceof dt ? (t = e._id, e = e._name) : (t = xu(), (n = Xx).time = xs(), e = e == null ? null : e + "");
  for (var i = this._groups, r = i.length, s = 0; s < r; ++s)
    for (var a = i[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && hr(u, e, t, l, a, n || qx(u, t));
  return new dt(i, this._parents, e, t);
}
Qn.prototype.interrupt = Ky;
Qn.prototype.transition = Yx;
const Ci = (e) => () => e;
function Ux(e, {
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
var gr = new lt(1, 0, 0);
wu.prototype = lt.prototype;
function wu(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return gr;
  return e.__zoom;
}
function go(e) {
  e.stopImmediatePropagation();
}
function wn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Zx(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function Gx() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function Ra() {
  return this.__zoom || gr;
}
function Jx(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function Qx() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function ew(e, t, n) {
  var i = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], s = e.invertY(t[0][1]) - n[0][1], a = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > i ? (i + r) / 2 : Math.min(0, i) || Math.max(0, r),
    a > s ? (s + a) / 2 : Math.min(0, s) || Math.max(0, a)
  );
}
function vu() {
  var e = Zx, t = Gx, n = ew, i = Jx, r = Qx, s = [0, 1 / 0], a = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, u = Ri, l = dr("start", "zoom", "end"), d, f, p, h = 500, m = 150, w = 0, v = 10;
  function x(C) {
    C.property("__zoom", Ra).on("wheel.zoom", E, { passive: !1 }).on("mousedown.zoom", _).on("dblclick.zoom", M).filter(r).on("touchstart.zoom", I).on("touchmove.zoom", $).on("touchend.zoom touchcancel.zoom", z).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  x.transform = function(C, A, k, D) {
    var P = C.selection ? C.selection() : C;
    P.property("__zoom", Ra), C !== P ? N(C, A, k, D) : P.interrupt().each(function() {
      j(this, arguments).event(D).start().zoom(null, typeof A == "function" ? A.apply(this, arguments) : A).end();
    });
  }, x.scaleBy = function(C, A, k, D) {
    x.scaleTo(C, function() {
      var P = this.__zoom.k, T = typeof A == "function" ? A.apply(this, arguments) : A;
      return P * T;
    }, k, D);
  }, x.scaleTo = function(C, A, k, D) {
    x.transform(C, function() {
      var P = t.apply(this, arguments), T = this.__zoom, F = k == null ? y(P) : typeof k == "function" ? k.apply(this, arguments) : k, W = T.invert(F), H = typeof A == "function" ? A.apply(this, arguments) : A;
      return n(g(b(T, H), F, W), P, a);
    }, k, D);
  }, x.translateBy = function(C, A, k, D) {
    x.transform(C, function() {
      return n(this.__zoom.translate(
        typeof A == "function" ? A.apply(this, arguments) : A,
        typeof k == "function" ? k.apply(this, arguments) : k
      ), t.apply(this, arguments), a);
    }, null, D);
  }, x.translateTo = function(C, A, k, D, P) {
    x.transform(C, function() {
      var T = t.apply(this, arguments), F = this.__zoom, W = D == null ? y(T) : typeof D == "function" ? D.apply(this, arguments) : D;
      return n(gr.translate(W[0], W[1]).scale(F.k).translate(
        typeof A == "function" ? -A.apply(this, arguments) : -A,
        typeof k == "function" ? -k.apply(this, arguments) : -k
      ), T, a);
    }, D, P);
  };
  function b(C, A) {
    return A = Math.max(s[0], Math.min(s[1], A)), A === C.k ? C : new lt(A, C.x, C.y);
  }
  function g(C, A, k) {
    var D = A[0] - k[0] * C.k, P = A[1] - k[1] * C.k;
    return D === C.x && P === C.y ? C : new lt(C.k, D, P);
  }
  function y(C) {
    return [(+C[0][0] + +C[1][0]) / 2, (+C[0][1] + +C[1][1]) / 2];
  }
  function N(C, A, k, D) {
    C.on("start.zoom", function() {
      j(this, arguments).event(D).start();
    }).on("interrupt.zoom end.zoom", function() {
      j(this, arguments).event(D).end();
    }).tween("zoom", function() {
      var P = this, T = arguments, F = j(P, T).event(D), W = t.apply(P, T), H = k == null ? y(W) : typeof k == "function" ? k.apply(P, T) : k, Y = Math.max(W[1][0] - W[0][0], W[1][1] - W[0][1]), V = P.__zoom, q = typeof A == "function" ? A.apply(P, T) : A, J = u(V.invert(H).concat(Y / V.k), q.invert(H).concat(Y / q.k));
      return function(Z) {
        if (Z === 1) Z = q;
        else {
          var R = J(Z), X = Y / R[2];
          Z = new lt(X, H[0] - R[0] * X, H[1] - R[1] * X);
        }
        F.zoom(null, Z);
      };
    });
  }
  function j(C, A, k) {
    return !k && C.__zooming || new S(C, A);
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
      var A = Fe(this.that).datum();
      l.call(
        C,
        this.that,
        new Ux(C, {
          sourceEvent: this.sourceEvent,
          target: x,
          transform: this.that.__zoom,
          dispatch: l
        }),
        A
      );
    }
  };
  function E(C, ...A) {
    if (!e.apply(this, arguments)) return;
    var k = j(this, A).event(C), D = this.__zoom, P = Math.max(s[0], Math.min(s[1], D.k * Math.pow(2, i.apply(this, arguments)))), T = Ze(C);
    if (k.wheel)
      (k.mouse[0][0] !== T[0] || k.mouse[0][1] !== T[1]) && (k.mouse[1] = D.invert(k.mouse[0] = T)), clearTimeout(k.wheel);
    else {
      if (D.k === P) return;
      k.mouse = [T, D.invert(T)], Vi(this), k.start();
    }
    wn(C), k.wheel = setTimeout(F, m), k.zoom("mouse", n(g(b(D, P), k.mouse[0], k.mouse[1]), k.extent, a));
    function F() {
      k.wheel = null, k.end();
    }
  }
  function _(C, ...A) {
    if (p || !e.apply(this, arguments)) return;
    var k = C.currentTarget, D = j(this, A, !0).event(C), P = Fe(C.view).on("mousemove.zoom", H, !0).on("mouseup.zoom", Y, !0), T = Ze(C, k), F = C.clientX, W = C.clientY;
    iu(C.view), go(C), D.mouse = [T, this.__zoom.invert(T)], Vi(this), D.start();
    function H(V) {
      if (wn(V), !D.moved) {
        var q = V.clientX - F, J = V.clientY - W;
        D.moved = q * q + J * J > w;
      }
      D.event(V).zoom("mouse", n(g(D.that.__zoom, D.mouse[0] = Ze(V, k), D.mouse[1]), D.extent, a));
    }
    function Y(V) {
      P.on("mousemove.zoom mouseup.zoom", null), ru(V.view, D.moved), wn(V), D.event(V).end();
    }
  }
  function M(C, ...A) {
    if (e.apply(this, arguments)) {
      var k = this.__zoom, D = Ze(C.changedTouches ? C.changedTouches[0] : C, this), P = k.invert(D), T = k.k * (C.shiftKey ? 0.5 : 2), F = n(g(b(k, T), D, P), t.apply(this, A), a);
      wn(C), c > 0 ? Fe(this).transition().duration(c).call(N, F, D, C) : Fe(this).call(x.transform, F, D, C);
    }
  }
  function I(C, ...A) {
    if (e.apply(this, arguments)) {
      var k = C.touches, D = k.length, P = j(this, A, C.changedTouches.length === D).event(C), T, F, W, H;
      for (go(C), F = 0; F < D; ++F)
        W = k[F], H = Ze(W, this), H = [H, this.__zoom.invert(H), W.identifier], P.touch0 ? !P.touch1 && P.touch0[2] !== H[2] && (P.touch1 = H, P.taps = 0) : (P.touch0 = H, T = !0, P.taps = 1 + !!d);
      d && (d = clearTimeout(d)), T && (P.taps < 2 && (f = H[0], d = setTimeout(function() {
        d = null;
      }, h)), Vi(this), P.start());
    }
  }
  function $(C, ...A) {
    if (this.__zooming) {
      var k = j(this, A).event(C), D = C.changedTouches, P = D.length, T, F, W, H;
      for (wn(C), T = 0; T < P; ++T)
        F = D[T], W = Ze(F, this), k.touch0 && k.touch0[2] === F.identifier ? k.touch0[0] = W : k.touch1 && k.touch1[2] === F.identifier && (k.touch1[0] = W);
      if (F = k.that.__zoom, k.touch1) {
        var Y = k.touch0[0], V = k.touch0[1], q = k.touch1[0], J = k.touch1[1], Z = (Z = q[0] - Y[0]) * Z + (Z = q[1] - Y[1]) * Z, R = (R = J[0] - V[0]) * R + (R = J[1] - V[1]) * R;
        F = b(F, Math.sqrt(Z / R)), W = [(Y[0] + q[0]) / 2, (Y[1] + q[1]) / 2], H = [(V[0] + J[0]) / 2, (V[1] + J[1]) / 2];
      } else if (k.touch0) W = k.touch0[0], H = k.touch0[1];
      else return;
      k.zoom("touch", n(g(F, W, H), k.extent, a));
    }
  }
  function z(C, ...A) {
    if (this.__zooming) {
      var k = j(this, A).event(C), D = C.changedTouches, P = D.length, T, F;
      for (go(C), p && clearTimeout(p), p = setTimeout(function() {
        p = null;
      }, h), T = 0; T < P; ++T)
        F = D[T], k.touch0 && k.touch0[2] === F.identifier ? delete k.touch0 : k.touch1 && k.touch1[2] === F.identifier && delete k.touch1;
      if (k.touch1 && !k.touch0 && (k.touch0 = k.touch1, delete k.touch1), k.touch0) k.touch0[1] = this.__zoom.invert(k.touch0[0]);
      else if (k.end(), k.taps === 2 && (F = Ze(F, this), Math.hypot(f[0] - F[0], f[1] - F[1]) < v)) {
        var W = Fe(this).on("dblclick.zoom");
        W && W.apply(this, arguments);
      }
    }
  }
  return x.wheelDelta = function(C) {
    return arguments.length ? (i = typeof C == "function" ? C : Ci(+C), x) : i;
  }, x.filter = function(C) {
    return arguments.length ? (e = typeof C == "function" ? C : Ci(!!C), x) : e;
  }, x.touchable = function(C) {
    return arguments.length ? (r = typeof C == "function" ? C : Ci(!!C), x) : r;
  }, x.extent = function(C) {
    return arguments.length ? (t = typeof C == "function" ? C : Ci([[+C[0][0], +C[0][1]], [+C[1][0], +C[1][1]]]), x) : t;
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
    return arguments.length ? (w = (C = +C) * C, x) : Math.sqrt(w);
  }, x.tapDistance = function(C) {
    return arguments.length ? (v = +C, x) : v;
  }, x;
}
const qe = {
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
], bu = ["Enter", " ", "Escape"], ju = {
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
var kt;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(kt || (kt = {}));
var Kn;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Kn || (Kn = {}));
const Nu = {
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
var mt;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(mt || (mt = {}));
var er;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(er || (er = {}));
var oe;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(oe || (oe = {}));
const La = {
  [oe.Left]: oe.Right,
  [oe.Right]: oe.Left,
  [oe.Top]: oe.Bottom,
  [oe.Bottom]: oe.Top
};
function Su(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Cu = (e) => "id" in e && "source" in e && "target" in e, tw = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), bs = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), ti = (e, t = [0, 0]) => {
  const { width: n, height: i } = ft(e), r = e.origin ?? t, s = n * r[0], a = i * r[1];
  return {
    x: e.position.x - s,
    y: e.position.y - a
  };
}, nw = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((i, r) => {
    const s = typeof r == "string";
    let a = !t.nodeLookup && !s ? r : void 0;
    t.nodeLookup && (a = s ? t.nodeLookup.get(r) : bs(r) ? r : t.nodeLookup.get(r.id));
    const c = a ? tr(a, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return mr(i, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return yr(n);
}, ni = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, i = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = mr(n, tr(r)), i = !0);
  }), i ? yr(n) : { x: 0, y: 0, width: 0, height: 0 };
}, js = (e, t, [n, i, r] = [0, 0, 1], s = !1, a = !1) => {
  const c = {
    ...pn(t, [n, i, r]),
    width: t.width / r,
    height: t.height / r
  }, u = [];
  for (const l of e.values()) {
    const { measured: d, selectable: f = !0, hidden: p = !1 } = l;
    if (a && !f || p)
      continue;
    const h = d.width ?? l.width ?? l.initialWidth ?? null, m = d.height ?? l.height ?? l.initialHeight ?? null, w = Xn(c, on(l)), v = (h ?? 0) * (m ?? 0), x = s && w > 0;
    (!l.internals.handleBounds || x || w >= v || l.dragging) && u.push(l);
  }
  return u;
}, iw = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((i) => {
    n.add(i.id);
  }), t.filter((i) => n.has(i.source) || n.has(i.target));
};
function rw(e, t) {
  const n = /* @__PURE__ */ new Map(), i = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!i || i.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function ow({ nodes: e, width: t, height: n, panZoom: i, minZoom: r, maxZoom: s }, a) {
  if (e.size === 0)
    return !0;
  const c = rw(e, a), u = ni(c), l = Ss(u, t, n, a?.minZoom ?? r, a?.maxZoom ?? s, a?.padding ?? 0.1);
  return await i.setViewport(l, {
    duration: a?.duration,
    ease: a?.ease,
    interpolate: a?.interpolate
  }), !0;
}
function ku({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: i = [0, 0], nodeExtent: r, onError: s }) {
  const a = n.get(e), c = a.parentId ? n.get(a.parentId) : void 0, { x: u, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, d = a.origin ?? i;
  let f = a.extent || r;
  if (a.extent === "parent" && !a.expandParent)
    if (!c)
      s?.("005", qe.error005());
    else {
      const h = c.measured.width, m = c.measured.height;
      h && m && (f = [
        [u, l],
        [u + h, l + m]
      ]);
    }
  else c && $t(a.extent) && (f = [
    [a.extent[0][0] + u, a.extent[0][1] + l],
    [a.extent[1][0] + u, a.extent[1][1] + l]
  ]);
  const p = $t(f) ? Tt(t, f, a.measured) : t;
  return (a.measured.width === void 0 || a.measured.height === void 0) && s?.("015", qe.error015()), {
    position: {
      x: p.x - u + (a.measured.width ?? 0) * d[0],
      y: p.y - l + (a.measured.height ?? 0) * d[1]
    },
    positionAbsolute: p
  };
}
async function sw({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: i, onBeforeDelete: r }) {
  const s = new Set(e.map((p) => p.id)), a = [];
  for (const p of n) {
    if (p.deletable === !1)
      continue;
    const h = s.has(p.id), m = !h && p.parentId && a.find((w) => w.id === p.parentId);
    (h || m) && a.push(p);
  }
  const c = new Set(t.map((p) => p.id)), u = i.filter((p) => p.deletable !== !1), d = iw(a, u);
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
const rn = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), Tt = (e = { x: 0, y: 0 }, t, n) => ({
  x: rn(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: rn(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Eu(e, t, n) {
  const { width: i, height: r } = ft(n), { x: s, y: a } = n.internals.positionAbsolute;
  return Tt(e, [
    [s, a],
    [s + i, a + r]
  ], t);
}
const za = (e, t, n) => e < t ? rn(Math.abs(e - t), 1, t) / t : e > n ? -rn(Math.abs(e - n), 1, t) / t : 0, Ns = (e, t, n = 15, i = 40) => {
  const r = za(e.x, i, t.width - i) * n, s = za(e.y, i, t.height - i) * n;
  return [r, s];
}, mr = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), Oo = ({ x: e, y: t, width: n, height: i }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + i
}), yr = ({ x: e, y: t, x2: n, y2: i }) => ({
  x: e,
  y: t,
  width: n - e,
  height: i - t
}), on = (e, t = [0, 0]) => {
  const { x: n, y: i } = bs(e) ? e.internals.positionAbsolute : ti(e, t);
  return {
    x: n,
    y: i,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, tr = (e, t = [0, 0]) => {
  const { x: n, y: i } = bs(e) ? e.internals.positionAbsolute : ti(e, t);
  return {
    x: n,
    y: i,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: i + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, Iu = (e, t) => yr(mr(Oo(e), Oo(t))), Xn = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), i = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * i);
}, Va = (e) => Je(e.width) && Je(e.height) && Je(e.x) && Je(e.y), Je = (e) => !isNaN(e) && isFinite(e), Au = (e, t) => (n, i) => {
}, ii = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), pn = ({ x: e, y: t }, [n, i, r], s = !1, a = [1, 1]) => {
  const c = {
    x: (e - n) / r,
    y: (t - i) / r
  };
  return s ? ii(c, a) : c;
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
function aw(e, t, n) {
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
function cw(e, t, n, i, r, s) {
  const { x: a, y: c } = sn(e, [t, n, i]), { x: u, y: l } = sn({ x: e.x + e.width, y: e.y + e.height }, [t, n, i]), d = r - u, f = s - l;
  return {
    left: Math.floor(a),
    top: Math.floor(c),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const Ss = (e, t, n, i, r, s) => {
  const a = aw(s, t, n), c = (t - a.x) / e.width, u = (n - a.y) / e.height, l = Math.min(c, u), d = rn(l, i, r), f = e.x + e.width / 2, p = e.y + e.height / 2, h = t / 2 - f * d, m = n / 2 - p * d, w = cw(e, h, m, d, t, n), v = {
    left: Math.min(w.left - a.left, 0),
    top: Math.min(w.top - a.top, 0),
    right: Math.min(w.right - a.right, 0),
    bottom: Math.min(w.bottom - a.bottom, 0)
  };
  return {
    x: h - v.left + v.right,
    y: m - v.top + v.bottom,
    zoom: d
  };
}, qn = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function $t(e) {
  return e != null && e !== "parent";
}
function ft(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function _u(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function Du(e, t = { width: 0, height: 0 }, n, i, r) {
  const s = { ...e }, a = i.get(n);
  if (a) {
    const c = a.origin || r;
    s.x += a.internals.positionAbsolute.x - (t.width ?? 0) * c[0], s.y += a.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return s;
}
function Oa(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function lw() {
  let e, t;
  return { promise: new Promise((i, r) => {
    e = i, t = r;
  }), resolve: e, reject: t };
}
function uw(e) {
  return { ...ju, ...e || {} };
}
function Dn(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: i, containerBounds: r }) {
  const { x: s, y: a } = Qe(e), c = pn({ x: s - (r?.left ?? 0), y: a - (r?.top ?? 0) }, i), { x: u, y: l } = n ? ii(c, t) : c;
  return {
    xSnapped: u,
    ySnapped: l,
    ...c
  };
}
const Cs = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), Tu = (e) => e?.getRootNode?.() || window?.document, dw = ["INPUT", "SELECT", "TEXTAREA"];
function $u(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : dw.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const Pu = (e) => "clientX" in e, Qe = (e, t) => {
  const n = Pu(e), i = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: i - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, Ha = (e, t, n, i, r) => {
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
      ...Cs(a)
    };
  });
};
function Mu({ sourceX: e, sourceY: t, targetX: n, targetY: i, sourceControlX: r, sourceControlY: s, targetControlX: a, targetControlY: c }) {
  const u = e * 0.125 + r * 0.375 + a * 0.375 + n * 0.125, l = t * 0.125 + s * 0.375 + c * 0.375 + i * 0.125, d = Math.abs(u - e), f = Math.abs(l - t);
  return [u, l, d, f];
}
function ki(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function Wa({ pos: e, x1: t, y1: n, x2: i, y2: r, c: s }) {
  switch (e) {
    case oe.Left:
      return [t - ki(t - i, s), n];
    case oe.Right:
      return [t + ki(i - t, s), n];
    case oe.Top:
      return [t, n - ki(n - r, s)];
    case oe.Bottom:
      return [t, n + ki(r - n, s)];
  }
}
function Ru({ sourceX: e, sourceY: t, sourcePosition: n = oe.Bottom, targetX: i, targetY: r, targetPosition: s = oe.Top, curvature: a = 0.25 }) {
  const [c, u] = Wa({
    pos: n,
    x1: e,
    y1: t,
    x2: i,
    y2: r,
    c: a
  }), [l, d] = Wa({
    pos: s,
    x1: i,
    y1: r,
    x2: e,
    y2: t,
    c: a
  }), [f, p, h, m] = Mu({
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
function Lu({ sourceX: e, sourceY: t, targetX: n, targetY: i }) {
  const r = Math.abs(n - e) / 2, s = n < e ? n + r : n - r, a = Math.abs(i - t) / 2, c = i < t ? i + a : i - a;
  return [s, c, r, a];
}
function fw({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: i = 0, elevateOnSelect: r = !1, zIndexMode: s = "basic" }) {
  if (s === "manual")
    return i;
  const a = r && n ? i + 1e3 : i, c = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return a + c;
}
function pw({ sourceNode: e, targetNode: t, width: n, height: i, transform: r }) {
  const s = mr(tr(e), tr(t));
  s.x === s.x2 && (s.x2 += 1), s.y === s.y2 && (s.y2 += 1);
  const a = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: i / r[2]
  };
  return Xn(a, yr(s)) > 0;
}
const zu = ({ source: e, sourceHandle: t, target: n, targetHandle: i }) => `xy-edge__${e}${t || ""}-${n}${i || ""}`, hw = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), gw = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", qe.error006()), t;
  const i = n.getEdgeId || zu;
  let r;
  return Cu(e) ? r = { ...e } : r = {
    ...e,
    id: i(e)
  }, hw(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, mw = (e, t, n, i = { shouldReplaceId: !0 }) => {
  const { id: r, ...s } = e;
  if (!t.source || !t.target)
    return i.onError?.("006", qe.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return i.onError?.("007", qe.error007(r)), n;
  const c = i.getEdgeId || zu, u = {
    ...s,
    id: i.shouldReplaceId ? c(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== r).concat(u);
};
function Vu({ sourceX: e, sourceY: t, targetX: n, targetY: i }) {
  const [r, s, a, c] = Lu({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: i
  });
  return [`M ${e},${t}L ${n},${i}`, r, s, a, c];
}
const Fa = {
  [oe.Left]: { x: -1, y: 0 },
  [oe.Right]: { x: 1, y: 0 },
  [oe.Top]: { x: 0, y: -1 },
  [oe.Bottom]: { x: 0, y: 1 }
}, yw = ({ source: e, sourcePosition: t = oe.Bottom, target: n }) => t === oe.Left || t === oe.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, Ba = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function xw({ source: e, sourcePosition: t = oe.Bottom, target: n, targetPosition: i = oe.Top, center: r, offset: s, stepPosition: a }) {
  const c = Fa[t], u = Fa[i], l = { x: e.x + c.x * s, y: e.y + c.y * s }, d = { x: n.x + u.x * s, y: n.y + u.y * s }, f = yw({
    source: l,
    sourcePosition: t,
    target: d
  }), p = f.x !== 0 ? "x" : "y", h = f[p];
  let m = [], w, v;
  const x = { x: 0, y: 0 }, b = { x: 0, y: 0 }, [, , g, y] = Lu({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[p] * u[p] === -1) {
    p === "x" ? (w = r.x ?? l.x + (d.x - l.x) * a, v = r.y ?? (l.y + d.y) / 2) : (w = r.x ?? (l.x + d.x) / 2, v = r.y ?? l.y + (d.y - l.y) * a);
    const E = [
      { x: w, y: l.y },
      { x: w, y: d.y }
    ], _ = [
      { x: l.x, y: v },
      { x: d.x, y: v }
    ];
    c[p] === h ? m = p === "x" ? E : _ : m = p === "x" ? _ : E;
  } else {
    const E = [{ x: l.x, y: d.y }], _ = [{ x: d.x, y: l.y }];
    if (p === "x" ? m = c.x === h ? _ : E : m = c.y === h ? E : _, t === i) {
      const C = Math.abs(e[p] - n[p]);
      if (C <= s) {
        const A = Math.min(s - 1, s - C);
        c[p] === h ? x[p] = (l[p] > e[p] ? -1 : 1) * A : b[p] = (d[p] > n[p] ? -1 : 1) * A;
      }
    }
    if (t !== i) {
      const C = p === "x" ? "y" : "x", A = c[p] === u[C], k = l[C] > d[C], D = l[C] < d[C];
      (c[p] === 1 && (!A && k || A && D) || c[p] !== 1 && (!A && D || A && k)) && (m = p === "x" ? E : _);
    }
    const M = { x: l.x + x.x, y: l.y + x.y }, I = { x: d.x + b.x, y: d.y + b.y }, $ = Math.max(Math.abs(M.x - m[0].x), Math.abs(I.x - m[0].x)), z = Math.max(Math.abs(M.y - m[0].y), Math.abs(I.y - m[0].y));
    $ >= z ? (w = (M.x + I.x) / 2, v = m[0].y) : (w = m[0].x, v = (M.y + I.y) / 2);
  }
  const N = { x: l.x + x.x, y: l.y + x.y }, j = { x: d.x + b.x, y: d.y + b.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...N.x !== m[0].x || N.y !== m[0].y ? [N] : [],
    ...m,
    ...j.x !== m[m.length - 1].x || j.y !== m[m.length - 1].y ? [j] : [],
    n
  ], w, v, g, y];
}
function ww(e, t, n, i) {
  const r = Math.min(Ba(e, t) / 2, Ba(t, n) / 2, i), { x: s, y: a } = t;
  if (e.x === s && s === n.x || e.y === a && a === n.y)
    return `L${s} ${a}`;
  if (e.y === a) {
    const l = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${s + r * l},${a}Q ${s},${a} ${s},${a + r * d}`;
  }
  const c = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${s},${a + r * u}Q ${s},${a} ${s + r * c},${a}`;
}
function nr({ sourceX: e, sourceY: t, sourcePosition: n = oe.Bottom, targetX: i, targetY: r, targetPosition: s = oe.Top, borderRadius: a = 5, centerX: c, centerY: u, offset: l = 20, stepPosition: d = 0.5 }) {
  const [f, p, h, m, w] = xw({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: i, y: r },
    targetPosition: s,
    center: { x: c, y: u },
    offset: l,
    stepPosition: d
  });
  let v = `M${f[0].x} ${f[0].y}`;
  for (let x = 1; x < f.length - 1; x++)
    v += ww(f[x - 1], f[x], f[x + 1], a);
  return v += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [v, p, h, m, w];
}
function Ka(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function vw(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!Ka(t) || !Ka(n))
    return null;
  const i = t.internals.handleBounds || Xa(t.handles), r = n.internals.handleBounds || Xa(n.handles), s = qa(i?.source ?? [], e.sourceHandle), a = qa(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === nn.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!s || !a)
    return e.onError?.("008", qe.error008(s ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = s?.position || oe.Bottom, u = a?.position || oe.Top, l = Pt(t, s, c), d = Pt(n, a, u);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: d.x,
    targetY: d.y,
    sourcePosition: c,
    targetPosition: u
  };
}
function Xa(e) {
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
function Pt(e, t, n = oe.Left, i = !1) {
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
function qa(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Ho(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((i) => `${i}=${e[i]}`).join("&")}` : "";
}
function bw(e, { id: t, defaultColor: n, defaultMarkerStart: i, defaultMarkerEnd: r }) {
  const s = /* @__PURE__ */ new Set();
  return e.reduce((a, c) => ([c.markerStart || i, c.markerEnd || r].forEach((u) => {
    if (u && typeof u == "object") {
      const l = Ho(u, t);
      s.has(l) || (a.push({ id: l, color: u.color || n, ...u }), s.add(l));
    }
  }), a), []).sort((a, c) => a.id.localeCompare(c.id));
}
const Ou = 1e3, jw = 10, ks = {
  nodeOrigin: [0, 0],
  nodeExtent: Bn,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Nw = {
  ...ks,
  checkEquality: !0
};
function Es(e, t) {
  const n = { ...e };
  for (const i in t)
    t[i] !== void 0 && (n[i] = t[i]);
  return n;
}
function Sw(e, t, n) {
  const i = Es(ks, n);
  for (const r of e.values())
    if (r.parentId)
      As(r, e, t, i);
    else {
      const s = ti(r, i.nodeOrigin), a = $t(r.extent) ? r.extent : i.nodeExtent, c = Tt(s, a, ft(r));
      r.internals.positionAbsolute = c;
    }
}
function Cw(e, t) {
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
function Is(e) {
  return e === "manual";
}
function Wo(e, t, n, i = {}) {
  const r = Es(Nw, i), s = { i: 0 }, a = new Map(t), c = r?.elevateNodesOnSelect && !Is(r.zIndexMode) ? Ou : 0;
  let u = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = a.get(d.id);
    if (r.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const p = ti(d, r.nodeOrigin), h = $t(d.extent) ? d.extent : r.nodeExtent, m = Tt(p, h, ft(d));
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
          handleBounds: Cw(d, f),
          z: Hu(d, c, r.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (u = !1), d.parentId && As(f, t, n, i, s), l ||= d.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: l };
}
function kw(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function As(e, t, n, i, r) {
  const { elevateNodesOnSelect: s, nodeOrigin: a, nodeExtent: c, zIndexMode: u } = Es(ks, i), l = e.parentId, d = t.get(l);
  if (!d) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  kw(e, n), r && !d.parentId && d.internals.rootParentIndex === void 0 && u === "auto" && (d.internals.rootParentIndex = ++r.i, d.internals.z = d.internals.z + r.i * jw), r && d.internals.rootParentIndex !== void 0 && (r.i = d.internals.rootParentIndex);
  const f = s && !Is(u) ? Ou : 0, { x: p, y: h, z: m } = Ew(e, d, a, c, f, u), { positionAbsolute: w } = e.internals, v = p !== w.x || h !== w.y;
  (v || m !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: v ? { x: p, y: h } : w,
      z: m
    }
  });
}
function Hu(e, t, n) {
  const i = Je(e.zIndex) ? e.zIndex : 0;
  return Is(n) ? i : i + (e.selected ? t : 0);
}
function Ew(e, t, n, i, r, s) {
  const { x: a, y: c } = t.internals.positionAbsolute, u = ft(e), l = ti(e, n), d = $t(e.extent) ? Tt(l, e.extent, u) : l;
  let f = Tt({ x: a + d.x, y: c + d.y }, i, u);
  e.extent === "parent" && (f = Eu(f, u, t));
  const p = Hu(e, r, s), h = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: h >= p ? h + 1 : p
  };
}
function _s(e, t, n, i = [0, 0]) {
  const r = [], s = /* @__PURE__ */ new Map();
  for (const a of e) {
    const c = t.get(a.parentId);
    if (!c)
      continue;
    const u = s.get(a.parentId)?.expandedRect ?? on(c), l = Iu(u, a.rect);
    s.set(a.parentId, { expandedRect: l, parent: c });
  }
  return s.size > 0 && s.forEach(({ expandedRect: a, parent: c }, u) => {
    const l = c.internals.positionAbsolute, d = ft(c), f = c.origin ?? i, p = a.x < l.x ? Math.round(Math.abs(l.x - a.x)) : 0, h = a.y < l.y ? Math.round(Math.abs(l.y - a.y)) : 0, m = Math.max(d.width, Math.round(a.width)), w = Math.max(d.height, Math.round(a.height)), v = (m - d.width) * f[0], x = (w - d.height) * f[1];
    (p > 0 || h > 0 || v || x) && (r.push({
      id: u,
      type: "position",
      position: {
        x: c.position.x - p + v,
        y: c.position.y - h + x
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
        width: m + (p ? f[0] * p - v : 0),
        height: w + (h ? f[1] * h - x : 0)
      }
    });
  }), r;
}
function Iw(e, t, n, i, r, s, a) {
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
    const w = Cs(h.nodeElement), v = m.measured.width !== w.width || m.measured.height !== w.height;
    if (!!(w.width && w.height && (v || !m.internals.handleBounds || h.force))) {
      const b = h.nodeElement.getBoundingClientRect(), g = $t(m.extent) ? m.extent : s;
      let { positionAbsolute: y } = m.internals;
      m.parentId && m.extent === "parent" ? y = Eu(y, w, t.get(m.parentId)) : g && (y = Tt(y, g, w));
      const N = {
        ...m,
        measured: w,
        internals: {
          ...m.internals,
          positionAbsolute: y,
          handleBounds: {
            source: Ha("source", h.nodeElement, b, f, m.id),
            target: Ha("target", h.nodeElement, b, f, m.id)
          }
        }
      };
      t.set(m.id, N), m.parentId && As(N, t, n, { nodeOrigin: r, zIndexMode: a }), u = !0, v && (l.push({
        id: m.id,
        type: "dimensions",
        dimensions: w
      }), m.expandParent && m.parentId && p.push({
        id: m.id,
        parentId: m.parentId,
        rect: on(N, r)
      }));
    }
  }
  if (p.length > 0) {
    const h = _s(p, t, n, r);
    l.push(...h);
  }
  return { changes: l, updatedInternals: u };
}
async function Aw({ delta: e, panZoom: t, transform: n, translateExtent: i, width: r, height: s }) {
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
function Ya(e, t, n, i, r, s) {
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
function Wu(e, t, n) {
  e.clear(), t.clear();
  for (const i of n) {
    const { source: r, target: s, sourceHandle: a = null, targetHandle: c = null } = i, u = { edgeId: i.id, source: r, target: s, sourceHandle: a, targetHandle: c }, l = `${r}-${a}--${s}-${c}`, d = `${s}-${c}--${r}-${a}`;
    Ya("source", u, d, e, r, a), Ya("target", u, l, e, s, c), t.set(i.id, i);
  }
}
function Fu(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : Fu(n, t) : !1;
}
function Ua(e, t, n) {
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
function _w(e, t, n, i) {
  const r = /* @__PURE__ */ new Map();
  for (const [s, a] of e)
    if ((a.selected || a.id === i) && (!a.parentId || !Fu(a, e)) && (a.draggable || t && typeof a.draggable > "u")) {
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
function mo({ nodeId: e, dragItems: t, nodeLookup: n, dragging: i = !0 }) {
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
function Dw({ dragItems: e, snapGrid: t, x: n, y: i }) {
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
function Tw({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: i, onDragStop: r }) {
  let s = { x: null, y: null }, a = 0, c = /* @__PURE__ */ new Map(), u = !1, l = { x: 0, y: 0 }, d = null, f = !1, p = null, h = !1, m = !1, w = null;
  function v({ noDragClassName: b, handleSelector: g, domNode: y, isSelectable: N, nodeId: j, nodeClickDistance: S = 0 }) {
    p = Fe(y);
    function E({ x: $, y: z }) {
      const { nodeLookup: C, nodeExtent: A, snapGrid: k, snapToGrid: D, nodeOrigin: P, onNodeDrag: T, onSelectionDrag: F, onError: W, updateNodePositions: H } = t();
      s = { x: $, y: z };
      let Y = !1;
      const V = c.size > 1, q = V && A ? Oo(ni(c)) : null, J = V && D ? Dw({
        dragItems: c,
        snapGrid: k,
        x: $,
        y: z
      }) : null;
      for (const [Z, R] of c) {
        if (!C.has(Z))
          continue;
        let X = { x: $ - R.distance.x, y: z - R.distance.y };
        D && (X = J ? {
          x: Math.round(X.x + J.x),
          y: Math.round(X.y + J.y)
        } : ii(X, k));
        let ue = null;
        if (V && A && !R.extent && q) {
          const { positionAbsolute: ne } = R.internals, fe = ne.x - q.x + A[0][0], O = ne.x + R.measured.width - q.x2 + A[1][0], te = ne.y - q.y + A[0][1], ge = ne.y + R.measured.height - q.y2 + A[1][1];
          ue = [
            [fe, te],
            [O, ge]
          ];
        }
        const { position: ce, positionAbsolute: ee } = ku({
          nodeId: Z,
          nextPosition: X,
          nodeLookup: C,
          nodeExtent: ue || A,
          nodeOrigin: P,
          onError: W
        });
        Y = Y || R.position.x !== ce.x || R.position.y !== ce.y, R.position = ce, R.internals.positionAbsolute = ee;
      }
      if (m = m || Y, !!Y && (H(c, !0), w && (i || T || !j && F))) {
        const [Z, R] = mo({
          nodeId: j,
          dragItems: c,
          nodeLookup: C
        });
        i?.(w, c, Z, R), T?.(w, Z, R), j || F?.(w, R);
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
      const [k, D] = Ns(l, d, C);
      (k !== 0 || D !== 0) && (s.x = (s.x ?? 0) - k / $[2], s.y = (s.y ?? 0) - D / $[2], await z({ x: k, y: D }) && E(s)), a = requestAnimationFrame(_);
    }
    function M($) {
      const { nodeLookup: z, multiSelectionActive: C, nodesDraggable: A, transform: k, snapGrid: D, snapToGrid: P, selectNodesOnDrag: T, onNodeDragStart: F, onSelectionDragStart: W, unselectNodesAndEdges: H } = t();
      f = !0, (!T || !N) && !C && j && (z.get(j)?.selected || H()), N && T && j && e?.(j);
      const Y = Dn($.sourceEvent, { transform: k, snapGrid: D, snapToGrid: P, containerBounds: d });
      if (s = Y, c = _w(z, A, Y, j), c.size > 0 && (n || F || !j && W)) {
        const [V, q] = mo({
          nodeId: j,
          dragItems: c,
          nodeLookup: z
        });
        n?.($.sourceEvent, c, V, q), F?.($.sourceEvent, V, q), j || W?.($.sourceEvent, q);
      }
    }
    const I = ou().clickDistance(S).on("start", ($) => {
      const { domNode: z, nodeDragThreshold: C, transform: A, snapGrid: k, snapToGrid: D } = t();
      d = z?.getBoundingClientRect() || null, h = !1, m = !1, w = $.sourceEvent, C === 0 && M($), s = Dn($.sourceEvent, { transform: A, snapGrid: k, snapToGrid: D, containerBounds: d }), l = Qe($.sourceEvent, d);
    }).on("drag", ($) => {
      const { autoPanOnNodeDrag: z, transform: C, snapGrid: A, snapToGrid: k, nodeDragThreshold: D, nodeLookup: P } = t(), T = Dn($.sourceEvent, { transform: C, snapGrid: A, snapToGrid: k, containerBounds: d });
      if (w = $.sourceEvent, ($.sourceEvent.type === "touchmove" && $.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      j && !P.has(j)) && (h = !0), !h) {
        if (!u && z && f && (u = !0, _()), !f) {
          const F = Qe($.sourceEvent, d), W = F.x - l.x, H = F.y - l.y;
          Math.sqrt(W * W + H * H) > D && M($);
        }
        (s.x !== T.xSnapped || s.y !== T.ySnapped) && c && f && (l = Qe($.sourceEvent, d), E(T));
      }
    }).on("end", ($) => {
      if (!f || h) {
        h && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (u = !1, f = !1, cancelAnimationFrame(a), c.size > 0) {
        const { nodeLookup: z, updateNodePositions: C, onNodeDragStop: A, onSelectionDragStop: k } = t();
        if (m && (C(c, !1), m = !1), r || A || !j && k) {
          const [D, P] = mo({
            nodeId: j,
            dragItems: c,
            nodeLookup: z,
            dragging: !1
          });
          r?.($.sourceEvent, c, D, P), A?.($.sourceEvent, D, P), j || k?.($.sourceEvent, P);
        }
      }
    }).filter(($) => {
      const z = $.target;
      return !$.button && (!b || !Ua(z, `.${b}`, y)) && (!g || Ua(z, g, y));
    });
    p.call(I);
  }
  function x() {
    p?.on(".drag", null);
  }
  return {
    update: v,
    destroy: x
  };
}
function $w(e, t, n) {
  const i = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const s of t.values())
    Xn(r, on(s)) > 0 && i.push(s);
  return i;
}
const Pw = 250;
function Mw(e, t, n, i) {
  let r = [], s = 1 / 0;
  const a = $w(e, n, t + Pw);
  for (const c of a) {
    const u = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of u) {
      if (i.nodeId === l.nodeId && i.type === l.type && i.id === l.id)
        continue;
      const { x: d, y: f } = Pt(c, l, l.position, !0), p = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(f - e.y, 2));
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
function Bu(e, t, n, i, r, s = !1) {
  const a = i.get(e);
  if (!a)
    return null;
  const c = r === "strict" ? a.internals.handleBounds?.[t] : [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []], u = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return u && s ? { ...u, ...Pt(a, u, u.position, !0) } : u;
}
function Ku(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Rw(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const Xu = () => !0;
function Lw(e, { connectionMode: t, connectionRadius: n, handleId: i, nodeId: r, edgeUpdaterType: s, isTarget: a, domNode: c, nodeLookup: u, lib: l, autoPanOnConnect: d, flowId: f, panBy: p, cancelConnection: h, onConnectStart: m, onConnect: w, onConnectEnd: v, isValidConnection: x = Xu, onReconnectEnd: b, updateConnection: g, getTransform: y, getFromHandle: N, autoPanSpeed: j, dragThreshold: S = 1, handleDomNode: E }) {
  const _ = Tu(e.target);
  let M = 0, I;
  const { x: $, y: z } = Qe(e), C = Ku(s, E), A = c?.getBoundingClientRect();
  let k = !1;
  if (!A || !C)
    return;
  const D = Bu(r, C, i, u, t);
  if (!D)
    return;
  let P = Qe(e, A), T = !1, F = null, W = !1, H = null;
  function Y() {
    if (!d || !A)
      return;
    const [ce, ee] = Ns(P, A, j);
    p({ x: ce, y: ee }), M = requestAnimationFrame(Y);
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
    from: Pt(q, V, oe.Left, !0),
    fromHandle: V,
    fromPosition: V.position,
    fromNode: q,
    to: P,
    toHandle: null,
    toPosition: La[V.position],
    toNode: null,
    pointer: P
  };
  function R() {
    k = !0, g(Z), m?.(e, { nodeId: r, handleId: i, handleType: C });
  }
  S === 0 && R();
  function X(ce) {
    if (!k) {
      const { x: ge, y: me } = Qe(ce), _e = ge - $, De = me - z;
      if (!(_e * _e + De * De > S * S))
        return;
      R();
    }
    if (!N() || !V) {
      ue(ce);
      return;
    }
    const ee = y();
    P = Qe(ce, A), I = Mw(pn(P, ee, !1, [1, 1]), n, u, V), T || (Y(), T = !0);
    const ne = qu(ce, {
      handle: I,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: i,
      fromType: a ? "target" : "source",
      isValidConnection: x,
      doc: _,
      lib: l,
      flowId: f,
      nodeLookup: u
    });
    H = ne.handleDomNode, F = ne.connection, W = Rw(!!I, ne.isValid);
    const fe = u.get(r), O = fe ? Pt(fe, V, oe.Left, !0) : Z.from, te = {
      ...Z,
      from: O,
      isValid: W,
      to: ne.toHandle && W ? sn({ x: ne.toHandle.x, y: ne.toHandle.y }, ee) : P,
      toHandle: ne.toHandle,
      toPosition: W && ne.toHandle ? ne.toHandle.position : La[V.position],
      toNode: ne.toHandle ? u.get(ne.toHandle.nodeId) : null,
      pointer: P
    };
    g(te), Z = te;
  }
  function ue(ce) {
    if (!("touches" in ce && ce.touches.length > 0)) {
      if (k) {
        (I || H) && F && W && w?.(F);
        const { inProgress: ee, ...ne } = Z, fe = {
          ...ne,
          toPosition: Z.toHandle ? Z.toPosition : null
        };
        v?.(ce, fe), s && b?.(ce, fe);
      }
      h(), cancelAnimationFrame(M), T = !1, W = !1, F = null, H = null, _.removeEventListener("mousemove", X), _.removeEventListener("mouseup", ue), _.removeEventListener("touchmove", X), _.removeEventListener("touchend", ue);
    }
  }
  _.addEventListener("mousemove", X), _.addEventListener("mouseup", ue), _.addEventListener("touchmove", X), _.addEventListener("touchend", ue);
}
function qu(e, { handle: t, connectionMode: n, fromNodeId: i, fromHandleId: r, fromType: s, doc: a, lib: c, flowId: u, isValidConnection: l = Xu, nodeLookup: d }) {
  const f = s === "target", p = t ? a.querySelector(`.${c}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: h, y: m } = Qe(e), w = a.elementFromPoint(h, m), v = w?.classList.contains(`${c}-flow__handle`) ? w : p, x = {
    handleDomNode: v,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (v) {
    const b = Ku(void 0, v), g = v.getAttribute("data-nodeid"), y = v.getAttribute("data-handleid"), N = v.classList.contains("connectable"), j = v.classList.contains("connectableend");
    if (!g || !b)
      return x;
    const S = {
      source: f ? g : i,
      sourceHandle: f ? y : r,
      target: f ? i : g,
      targetHandle: f ? r : y
    };
    x.connection = S;
    const _ = N && j && (n === nn.Strict ? f && b === "source" || !f && b === "target" : g !== i || y !== r);
    x.isValid = _ && l(S), x.toHandle = Bu(g, b, y, d, n, !0);
  }
  return x;
}
const Fo = {
  onPointerDown: Lw,
  isValid: qu
};
function zw({ domNode: e, panZoom: t, getTransform: n, getViewScale: i }) {
  const r = Fe(e);
  function s({ translateExtent: c, width: u, height: l, zoomStep: d = 1, pannable: f = !0, zoomable: p = !0, inversePan: h = !1 }) {
    const m = (g) => {
      if (g.sourceEvent.type !== "wheel" || !t)
        return;
      const y = n(), N = g.sourceEvent.ctrlKey && qn() ? 10 : 1, j = -g.sourceEvent.deltaY * (g.sourceEvent.deltaMode === 1 ? 0.05 : g.sourceEvent.deltaMode ? 1 : 2e-3) * d, S = y[2] * Math.pow(2, j * N);
      t.scaleTo(S);
    };
    let w = [0, 0];
    const v = (g) => {
      (g.sourceEvent.type === "mousedown" || g.sourceEvent.type === "touchstart") && (w = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ]);
    }, x = (g) => {
      const y = n();
      if (g.sourceEvent.type !== "mousemove" && g.sourceEvent.type !== "touchmove" || !t)
        return;
      const N = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ], j = [N[0] - w[0], N[1] - w[1]];
      w = N;
      const S = i() * Math.max(y[2], Math.log(y[2])) * (h ? -1 : 1), E = {
        x: y[0] - j[0] * S,
        y: y[1] - j[1] * S
      }, _ = [
        [0, 0],
        [u, l]
      ];
      t.setViewportConstrained({
        x: E.x,
        y: E.y,
        zoom: y[2]
      }, _, c);
    }, b = vu().on("start", v).on("zoom", f ? x : null).on("zoom.wheel", p ? m : null);
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
const xr = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), yo = ({ x: e, y: t, zoom: n }) => gr.translate(e, t).scale(n), Xt = (e, t) => e.target.closest(`.${t}`), Yu = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Vw = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, xo = (e, t = 0, n = Vw, i = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || i(), r ? e.transition().duration(t).ease(n).on("end", i) : e;
}, Uu = (e) => {
  const t = e.ctrlKey && qn() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Ow({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: i, panOnScrollMode: r, panOnScrollSpeed: s, zoomOnPinch: a, onPanZoomStart: c, onPanZoom: u, onPanZoomEnd: l }) {
  return (d) => {
    if (Xt(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && a) {
      const v = Ze(d), x = Uu(d), b = f * Math.pow(2, x);
      i.scaleTo(n, b, v, d);
      return;
    }
    const p = d.deltaMode === 1 ? 20 : 1;
    let h = r === kt.Vertical ? 0 : d.deltaX * p, m = r === kt.Horizontal ? 0 : d.deltaY * p;
    !qn() && d.shiftKey && r !== kt.Vertical && (h = d.deltaY * p, m = 0), i.translateBy(
      n,
      -(h / f) * s,
      -(m / f) * s,
      // @ts-ignore
      { internal: !0 }
    );
    const w = xr(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(d, w), e.panScrollTimeout = setTimeout(() => {
      l?.(d, w), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(d, w));
  };
}
function Hw({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(i, r) {
    const s = i.type === "wheel", a = !t && s && !i.ctrlKey, c = Xt(i, e);
    if (i.ctrlKey && s && c && i.preventDefault(), a || c)
      return null;
    i.preventDefault(), n.call(this, i, r);
  };
}
function Ww({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (i) => {
    if (i.sourceEvent?.internal)
      return;
    const r = xr(i.transform);
    e.mouseButton = i.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, i.sourceEvent?.type === "mousedown" && t(!0), n && n?.(i.sourceEvent, r);
  };
}
function Fw({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: i, onPanZoom: r }) {
  return (s) => {
    e.usedRightMouseButton = !!(n && Yu(t, e.mouseButton ?? 0)), s.sourceEvent?.sync || i([s.transform.x, s.transform.y, s.transform.k]), r && !s.sourceEvent?.internal && r?.(s.sourceEvent, xr(s.transform));
  };
}
function Bw({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: i, onPanZoomEnd: r, onPaneContextMenu: s }) {
  return (a) => {
    if (!a.sourceEvent?.internal && (e.isZoomingOrPanning = !1, s && Yu(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && a.sourceEvent && s(a.sourceEvent), e.usedRightMouseButton = !1, i(!1), r)) {
      const c = xr(a.transform);
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
function Kw({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: i, panOnScroll: r, zoomOnDoubleClick: s, userSelectionActive: a, noWheelClassName: c, noPanClassName: u, lib: l, connectionInProgress: d }) {
  return (f) => {
    const p = e || t, h = n && f.ctrlKey, m = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (Xt(f, `${l}-flow__node`) || Xt(f, `${l}-flow__edge`)))
      return !0;
    if (!i && !p && !r && !s && !n || a || d && !m || Xt(f, c) && m || Xt(f, u) && (!m || r && m && !e) || !n && f.ctrlKey && m)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!p && !r && !h && m || !i && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(i) && !i.includes(f.button) && f.type === "mousedown")
      return !1;
    const w = Array.isArray(i) && i.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || m) && w;
  };
}
function Xw({ domNode: e, minZoom: t, maxZoom: n, translateExtent: i, viewport: r, onPanZoom: s, onPanZoomStart: a, onPanZoomEnd: c, onDraggingChange: u }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = vu().scaleExtent([t, n]).translateExtent(i), p = Fe(e).call(f);
  b({
    x: r.x,
    y: r.y,
    zoom: rn(r.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], i);
  const h = p.on("wheel.zoom"), m = p.on("dblclick.zoom");
  f.wheelDelta(Uu);
  async function w(I, $) {
    return p ? new Promise((z) => {
      f?.interpolate($?.interpolate === "linear" ? _n : Ri).transform(xo(p, $?.duration, $?.ease, () => z(!0)), I);
    }) : !1;
  }
  function v({ noWheelClassName: I, noPanClassName: $, onPaneContextMenu: z, userSelectionActive: C, panOnScroll: A, panOnDrag: k, panOnScrollMode: D, panOnScrollSpeed: P, preventScrolling: T, zoomOnPinch: F, zoomOnScroll: W, zoomOnDoubleClick: H, zoomActivationKeyPressed: Y, lib: V, onTransformChange: q, connectionInProgress: J, paneClickDistance: Z, selectionOnDrag: R }) {
    C && !l.isZoomingOrPanning && x();
    const X = A && !Y && !C;
    f.clickDistance(R ? 1 / 0 : !Je(Z) || Z < 0 ? 0 : Z);
    const ue = X ? Ow({
      zoomPanValues: l,
      noWheelClassName: I,
      d3Selection: p,
      d3Zoom: f,
      panOnScrollMode: D,
      panOnScrollSpeed: P,
      zoomOnPinch: F,
      onPanZoomStart: a,
      onPanZoom: s,
      onPanZoomEnd: c
    }) : Hw({
      noWheelClassName: I,
      preventScrolling: T,
      d3ZoomHandler: h
    });
    p.on("wheel.zoom", ue, { passive: !1 });
    const ce = Ww({
      zoomPanValues: l,
      onDraggingChange: u,
      onPanZoomStart: a
    });
    f.on("start", ce);
    const ee = Fw({
      zoomPanValues: l,
      panOnDrag: k,
      onPaneContextMenu: !!z,
      onPanZoom: s,
      onTransformChange: q
    });
    f.on("zoom", ee);
    const ne = Bw({
      zoomPanValues: l,
      panOnDrag: k,
      panOnScroll: A,
      onPaneContextMenu: z,
      onPanZoomEnd: c,
      onDraggingChange: u
    });
    f.on("end", ne);
    const fe = Kw({
      zoomActivationKeyPressed: Y,
      panOnDrag: k,
      zoomOnScroll: W,
      panOnScroll: A,
      zoomOnDoubleClick: H,
      zoomOnPinch: F,
      userSelectionActive: C,
      noPanClassName: $,
      noWheelClassName: I,
      lib: V,
      connectionInProgress: J
    });
    f.filter(fe), H ? p.on("dblclick.zoom", m) : p.on("dblclick.zoom", null);
  }
  function x() {
    f.on("zoom", null);
  }
  async function b(I, $, z) {
    const C = yo(I), A = f?.constrain()(C, $, z);
    return A && await w(A), A;
  }
  async function g(I, $) {
    const z = yo(I);
    return await w(z, $), z;
  }
  function y(I) {
    if (p) {
      const $ = yo(I), z = p.property("__zoom");
      (z.k !== I.zoom || z.x !== I.x || z.y !== I.y) && f?.transform(p, $, null, { sync: !0 });
    }
  }
  function N() {
    const I = p ? wu(p.node()) : { x: 0, y: 0, k: 1 };
    return { x: I.x, y: I.y, zoom: I.k };
  }
  async function j(I, $) {
    return p ? new Promise((z) => {
      f?.interpolate($?.interpolate === "linear" ? _n : Ri).scaleTo(xo(p, $?.duration, $?.ease, () => z(!0)), I);
    }) : !1;
  }
  async function S(I, $) {
    return p ? new Promise((z) => {
      f?.interpolate($?.interpolate === "linear" ? _n : Ri).scaleBy(xo(p, $?.duration, $?.ease, () => z(!0)), I);
    }) : !1;
  }
  function E(I) {
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
    update: v,
    destroy: x,
    setViewport: g,
    setViewportConstrained: b,
    getViewport: N,
    scaleTo: j,
    scaleBy: S,
    setScaleExtent: E,
    setTranslateExtent: _,
    syncViewport: y,
    setClickDistance: M
  };
}
var an;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(an || (an = {}));
function qw({ width: e, prevWidth: t, height: n, prevHeight: i, affectsX: r, affectsY: s }) {
  const a = e - t, c = n - i, u = [a > 0 ? 1 : a < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return a && r && (u[0] = u[0] * -1), c && s && (u[1] = u[1] * -1), u;
}
function Za(e) {
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
function Ei(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function Ga(e, t) {
  return e ? !t : t;
}
function Yw(e, t, n, i, r, s, a, c) {
  let { affectsX: u, affectsY: l } = t;
  const { isHorizontal: d, isVertical: f } = t, p = d && f, { xSnapped: h, ySnapped: m } = n, { minWidth: w, maxWidth: v, minHeight: x, maxHeight: b } = i, { x: g, y, width: N, height: j, aspectRatio: S } = e;
  let E = Math.floor(d ? h - e.pointerX : 0), _ = Math.floor(f ? m - e.pointerY : 0);
  const M = N + (u ? -E : E), I = j + (l ? -_ : _), $ = -s[0] * N, z = -s[1] * j;
  let C = Ei(M, w, v), A = Ei(I, x, b);
  if (a) {
    let P = 0, T = 0;
    u && E < 0 ? P = ht(g + E + $, a[0][0]) : !u && E > 0 && (P = gt(g + M + $, a[1][0])), l && _ < 0 ? T = ht(y + _ + z, a[0][1]) : !l && _ > 0 && (T = gt(y + I + z, a[1][1])), C = Math.max(C, P), A = Math.max(A, T);
  }
  if (c) {
    let P = 0, T = 0;
    u && E > 0 ? P = gt(g + E, c[0][0]) : !u && E < 0 && (P = ht(g + M, c[1][0])), l && _ > 0 ? T = gt(y + _, c[0][1]) : !l && _ < 0 && (T = ht(y + I, c[1][1])), C = Math.max(C, P), A = Math.max(A, T);
  }
  if (r) {
    if (d) {
      const P = Ei(M / S, x, b) * S;
      if (C = Math.max(C, P), a) {
        let T = 0;
        !u && !l || u && !l && p ? T = gt(y + z + M / S, a[1][1]) * S : T = ht(y + z + (u ? E : -E) / S, a[0][1]) * S, C = Math.max(C, T);
      }
      if (c) {
        let T = 0;
        !u && !l || u && !l && p ? T = ht(y + M / S, c[1][1]) * S : T = gt(y + (u ? E : -E) / S, c[0][1]) * S, C = Math.max(C, T);
      }
    }
    if (f) {
      const P = Ei(I * S, w, v) / S;
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
  _ = _ + (_ < 0 ? A : -A), E = E + (E < 0 ? C : -C), r && (p ? M > I * S ? _ = (Ga(u, l) ? -E : E) / S : E = (Ga(u, l) ? -_ : _) * S : d ? (_ = E / S, l = u) : (E = _ * S, u = l));
  const k = u ? g + E : g, D = l ? y + _ : y;
  return {
    width: N + (u ? -E : E),
    height: j + (l ? -_ : _),
    x: s[0] * E * (u ? -1 : 1) + k,
    y: s[1] * _ * (l ? -1 : 1) + D
  };
}
const Zu = { width: 0, height: 0, x: 0, y: 0 }, Uw = {
  ...Zu,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function Zw(e, t, n) {
  const i = t.position.x + e.position.x, r = t.position.y + e.position.y, s = e.measured.width ?? 0, a = e.measured.height ?? 0, c = n[0] * s, u = n[1] * a;
  return [
    [i - c, r - u],
    [i + s - c, r + a - u]
  ];
}
function Gw({ domNode: e, nodeId: t, getStoreItems: n, onChange: i, onEnd: r }) {
  const s = Fe(e);
  let a = {
    controlDirection: Za("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: l, boundaries: d, keepAspectRatio: f, resizeDirection: p, onResizeStart: h, onResize: m, onResizeEnd: w, shouldResize: v }) {
    let x = { ...Zu }, b = { ...Uw };
    a = {
      boundaries: d,
      resizeDirection: p,
      keepAspectRatio: f,
      controlDirection: Za(l)
    };
    let g, y = null, N = [], j, S, E, _ = !1;
    const M = ou().on("start", (I) => {
      const { nodeLookup: $, transform: z, snapGrid: C, snapToGrid: A, nodeOrigin: k, paneDomNode: D } = n();
      if (g = $.get(t), !g)
        return;
      y = D?.getBoundingClientRect() ?? null;
      const { xSnapped: P, ySnapped: T } = Dn(I.sourceEvent, {
        transform: z,
        snapGrid: C,
        snapToGrid: A,
        containerBounds: y
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
      }, j = void 0, S = $t(g.extent) ? g.extent : void 0, g.parentId && (g.extent === "parent" || g.expandParent) && (j = $.get(g.parentId)), j && g.extent === "parent" && (S = [
        [0, 0],
        [j.measured.width, j.measured.height]
      ]), N = [], E = void 0;
      for (const [F, W] of $)
        if (W.parentId === t && (N.push({
          id: F,
          position: { ...W.position },
          extent: W.extent
        }), W.extent === "parent" || W.expandParent)) {
          const H = Zw(W, g, W.origin ?? k);
          E ? E = [
            [Math.min(H[0][0], E[0][0]), Math.min(H[0][1], E[0][1])],
            [Math.max(H[1][0], E[1][0]), Math.max(H[1][1], E[1][1])]
          ] : E = H;
        }
      h?.(I, { ...x });
    }).on("drag", (I) => {
      const { transform: $, snapGrid: z, snapToGrid: C, nodeOrigin: A } = n(), k = Dn(I.sourceEvent, {
        transform: $,
        snapGrid: z,
        snapToGrid: C,
        containerBounds: y
      }), D = [];
      if (!g)
        return;
      const { x: P, y: T, width: F, height: W } = x, H = {}, Y = g.origin ?? A, { width: V, height: q, x: J, y: Z } = Yw(b, a.controlDirection, k, a.boundaries, a.keepAspectRatio, Y, S, E), R = V !== F, X = q !== W, ue = J !== P && R, ce = Z !== T && X;
      if (!ue && !ce && !R && !X)
        return;
      if ((ue || ce || Y[0] === 1 || Y[1] === 1) && (H.x = ue ? J : x.x, H.y = ce ? Z : x.y, x.x = H.x, x.y = H.y, N.length > 0)) {
        const O = J - P, te = Z - T;
        for (const ge of N)
          ge.position = {
            x: ge.position.x - O + Y[0] * (V - F),
            y: ge.position.y - te + Y[1] * (q - W)
          }, D.push(ge);
      }
      if ((R || X) && (H.width = R && (!a.resizeDirection || a.resizeDirection === "horizontal") ? V : x.width, H.height = X && (!a.resizeDirection || a.resizeDirection === "vertical") ? q : x.height, x.width = H.width, x.height = H.height), j && g.expandParent) {
        const O = Y[0] * (H.width ?? 0);
        H.x && H.x < O && (x.x = O, b.x = b.x - (H.x - O));
        const te = Y[1] * (H.height ?? 0);
        H.y && H.y < te && (x.y = te, b.y = b.y - (H.y - te));
      }
      const ee = qw({
        width: x.width,
        prevWidth: F,
        height: x.height,
        prevHeight: W,
        affectsX: a.controlDirection.affectsX,
        affectsY: a.controlDirection.affectsY
      }), ne = { ...x, direction: ee };
      v?.(I, ne) !== !1 && (_ = !0, m?.(I, ne), i(H, D));
    }).on("end", (I) => {
      _ && (w?.(I, { ...x }), r?.({ ...x }), _ = !1);
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
var wo = { exports: {} }, vo = {}, bo = { exports: {} }, jo = {};
var Ja;
function Jw() {
  if (Ja) return jo;
  Ja = 1;
  var e = Ke;
  function t(f, p) {
    return f === p && (f !== 0 || 1 / f === 1 / p) || f !== f && p !== p;
  }
  var n = typeof Object.is == "function" ? Object.is : t, i = e.useState, r = e.useEffect, s = e.useLayoutEffect, a = e.useDebugValue;
  function c(f, p) {
    var h = p(), m = i({ inst: { value: h, getSnapshot: p } }), w = m[0].inst, v = m[1];
    return s(
      function() {
        w.value = h, w.getSnapshot = p, u(w) && v({ inst: w });
      },
      [f, h, p]
    ), r(
      function() {
        return u(w) && v({ inst: w }), f(function() {
          u(w) && v({ inst: w });
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
  return jo.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : d, jo;
}
var Qa;
function Qw() {
  return Qa || (Qa = 1, bo.exports = Jw()), bo.exports;
}
var ec;
function ev() {
  if (ec) return vo;
  ec = 1;
  var e = Ke, t = Qw();
  function n(l, d) {
    return l === d && (l !== 0 || 1 / l === 1 / d) || l !== l && d !== d;
  }
  var i = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, s = e.useRef, a = e.useEffect, c = e.useMemo, u = e.useDebugValue;
  return vo.useSyncExternalStoreWithSelector = function(l, d, f, p, h) {
    var m = s(null);
    if (m.current === null) {
      var w = { hasValue: !1, value: null };
      m.current = w;
    } else w = m.current;
    m = c(
      function() {
        function x(j) {
          if (!b) {
            if (b = !0, g = j, j = p(j), h !== void 0 && w.hasValue) {
              var S = w.value;
              if (h(S, j))
                return y = S;
            }
            return y = j;
          }
          if (S = y, i(g, j)) return S;
          var E = p(j);
          return h !== void 0 && h(S, E) ? (g = j, S) : (g = j, y = E);
        }
        var b = !1, g, y, N = f === void 0 ? null : f;
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
    var v = r(l, m[0], m[1]);
    return a(
      function() {
        w.hasValue = !0, w.value = v;
      },
      [v]
    ), u(v), v;
  }, vo;
}
var tc;
function tv() {
  return tc || (tc = 1, wo.exports = ev()), wo.exports;
}
var nv = tv();
const iv = /* @__PURE__ */ Of(nv), rv = {}, nc = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), i = (d, f) => {
    const p = typeof d == "function" ? d(t) : d;
    if (!Object.is(p, t)) {
      const h = t;
      t = f ?? (typeof p != "object" || p === null) ? p : Object.assign({}, t, p), n.forEach((m) => m(t, h));
    }
  }, r = () => t, u = { setState: i, getState: r, getInitialState: () => l, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (rv ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(i, r, u);
  return u;
}, ov = (e) => e ? nc(e) : nc, { useDebugValue: sv } = Ke, { useSyncExternalStoreWithSelector: av } = iv, cv = (e) => e;
function Gu(e, t = cv, n) {
  const i = av(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return sv(i), i;
}
const ic = (e, t) => {
  const n = ov(e), i = (r, s = t) => Gu(n, r, s);
  return Object.assign(i, n), i;
}, lv = (e, t) => e ? ic(e, t) : ic;
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
var No = { exports: {} }, Te = {};
var rc;
function uv() {
  if (rc) return Te;
  rc = 1;
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
var oc;
function dv() {
  if (oc) return No.exports;
  oc = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), No.exports = uv(), No.exports;
}
var fv = dv();
const wr = Qo(null), pv = wr.Provider, Ju = qe.error001("react");
function he(e, t) {
  const n = Un(wr);
  if (n === null)
    throw new Error(Ju);
  return Gu(n, e, t);
}
function je() {
  const e = Un(wr);
  if (e === null)
    throw new Error(Ju);
  return le(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const sc = { display: "none" }, hv = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, Qu = "react-flow__node-desc", ed = "react-flow__edge-desc", gv = "react-flow__aria-live", mv = (e) => e.ariaLiveMessage, yv = (e) => e.ariaLabelConfig;
function xv({ rfId: e }) {
  const t = he(mv);
  return o.jsx("div", { id: `${gv}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: hv, children: t });
}
function wv({ rfId: e, disableKeyboardA11y: t }) {
  const n = he(yv);
  return o.jsxs(o.Fragment, { children: [o.jsx("div", { id: `${Qu}-${e}`, style: sc, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), o.jsx("div", { id: `${ed}-${e}`, style: sc, children: n["edge.a11yDescription.default"] }), !t && o.jsx(xv, { rfId: e })] });
}
const vr = tl(({ position: e = "top-left", children: t, className: n, style: i, ...r }, s) => {
  const a = `${e}`.split("-");
  return o.jsx("div", { className: Ee(["react-flow__panel", n, ...a]), style: i, ref: s, ...r, children: t });
});
vr.displayName = "Panel";
function vv({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : o.jsx(vr, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: o.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const bv = (e) => {
  const t = [], n = [];
  for (const [, i] of e.nodeLookup)
    i.selected && t.push(i.internals.userNode);
  for (const [, i] of e.edgeLookup)
    i.selected && n.push(i);
  return { selectedNodes: t, selectedEdges: n };
}, Ii = (e) => e.id;
function jv(e, t) {
  return ve(e.selectedNodes.map(Ii), t.selectedNodes.map(Ii)) && ve(e.selectedEdges.map(Ii), t.selectedEdges.map(Ii));
}
function Nv({ onSelectionChange: e }) {
  const t = je(), { selectedNodes: n, selectedEdges: i } = he(bv, jv);
  return Q(() => {
    const r = { nodes: n, edges: i };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((s) => s(r));
  }, [n, i, e]), null;
}
const Sv = (e) => !!e.onSelectionChangeHandlers;
function Cv({ onSelectionChange: e }) {
  const t = he(Sv);
  return e || t ? o.jsx(Nv, { onSelectionChange: e }) : null;
}
const td = [0, 0], kv = { x: 0, y: 0, zoom: 1 }, Ev = [
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
], ac = [...Ev, "rfId"], Iv = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), cc = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: Bn,
  nodeOrigin: td,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function Av(e) {
  const { setNodes: t, setEdges: n, setMinZoom: i, setMaxZoom: r, setTranslateExtent: s, setNodeExtent: a, reset: c, setDefaultNodesAndEdges: u } = he(Iv, ve), l = je();
  Q(() => (u(e.defaultNodes, e.defaultEdges), () => {
    d.current = cc, c();
  }), []);
  const d = re(cc);
  return Q(
    () => {
      for (const f of ac) {
        const p = e[f], h = d.current[f];
        p !== h && (typeof e[f] > "u" || (f === "nodes" ? t(p) : f === "edges" ? n(p) : f === "minZoom" ? i(p) : f === "maxZoom" ? r(p) : f === "translateExtent" ? s(p) : f === "nodeExtent" ? a(p) : f === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: uw(p) }) : f === "fitView" ? l.setState({ fitViewQueued: p }) : f === "fitViewOptions" ? l.setState({ fitViewOptions: p }) : l.setState({ [f]: p })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    ac.map((f) => e[f])
  ), null;
}
function lc() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function _v(e) {
  const [t, n] = K(e === "system" ? null : e);
  return Q(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const i = lc(), r = () => n(i?.matches ? "dark" : "light");
    return r(), i?.addEventListener("change", r), () => {
      i?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : lc()?.matches ? "dark" : "light";
}
const uc = typeof document < "u" ? document : null;
function Yn(e = null, t = { target: uc, actInsideInputWithModifier: !0 }) {
  const [n, i] = K(!1), r = re(!1), s = re(/* @__PURE__ */ new Set([])), [a, c] = le(() => {
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
    const u = t?.target ?? uc, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (h) => {
        if (r.current = h.ctrlKey || h.metaKey || h.shiftKey || h.altKey, (!r.current || r.current && !l) && $u(h))
          return !1;
        const w = fc(h.code, c);
        if (s.current.add(h[w]), dc(a, s.current, !1)) {
          const v = h.composedPath?.()?.[0] || h.target, x = v?.nodeName === "BUTTON" || v?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !x) && h.preventDefault(), i(!0);
        }
      }, f = (h) => {
        const m = fc(h.code, c);
        dc(a, s.current, !0) ? (i(!1), s.current.clear()) : s.current.delete(h[m]), h.key === "Meta" && s.current.clear(), r.current = !1;
      }, p = () => {
        s.current.clear(), i(!1);
      };
      return u?.addEventListener("keydown", d), u?.addEventListener("keyup", f), window.addEventListener("blur", p), window.addEventListener("contextmenu", p), () => {
        u?.removeEventListener("keydown", d), u?.removeEventListener("keyup", f), window.removeEventListener("blur", p), window.removeEventListener("contextmenu", p);
      };
    }
  }, [e, i]), n;
}
function dc(e, t, n) {
  return e.filter((i) => n || i.length === t.size).some((i) => i.every((r) => t.has(r)));
}
function fc(e, t) {
  return t.includes(e) ? "code" : "key";
}
const Dv = () => {
  const e = je();
  return le(() => ({
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
      const { width: i, height: r, minZoom: s, maxZoom: a, panZoom: c } = e.getState(), u = Ss(t, i, r, s, a, n?.padding ?? 0.1);
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
      return pn(l, i, f, d);
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
function nd(e, t) {
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
      Tv(u, c);
    n.push(c);
  }
  return r.length && r.forEach((s) => {
    s.index !== void 0 ? n.splice(s.index, 0, { ...s.item }) : n.push({ ...s.item });
  }), n;
}
function Tv(e, t) {
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
function id(e, t) {
  return nd(e, t);
}
function rd(e, t) {
  return nd(e, t);
}
function Nt(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function qt(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const i = [];
  for (const [r, s] of e) {
    const a = t.has(r);
    !(s.selected === void 0 && !a) && s.selected !== a && (n && (s.selected = a), i.push(Nt(s.id, a)));
  }
  return i;
}
function pc({ items: e = [], lookup: t }) {
  const n = [], i = new Map(e.map((r) => [r.id, r]));
  for (const [r, s] of e.entries()) {
    const a = t.get(s.id), c = a?.internals?.userNode ?? a;
    c !== void 0 && c !== s && n.push({ id: s.id, item: s, type: "replace" }), c === void 0 && n.push({ item: s, type: "add", index: r });
  }
  for (const [r] of t)
    i.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function hc(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const od = Au();
function sd(e, t, n = {}) {
  return gw(e, t, {
    ...n,
    onError: n.onError ?? od
  });
}
function $v(e, t, n, i = { shouldReplaceId: !0 }) {
  return mw(e, t, n, {
    ...i,
    onError: i.onError ?? od
  });
}
const gc = (e) => tw(e), Pv = (e) => Cu(e);
function ad(e) {
  return tl(e);
}
const Mv = typeof window < "u" ? jf : Q;
function mc(e) {
  const [t, n] = K(BigInt(0)), [i] = K(() => Rv(() => n((r) => r + BigInt(1))));
  return Mv(() => {
    const r = i.get();
    r.length && (e(r), i.reset());
  }, [t]), i;
}
function Rv(e) {
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
const cd = Qo(null);
function Lv({ children: e }) {
  const t = je(), n = se((c) => {
    const { nodes: u = [], setNodes: l, hasDefaultNodes: d, onNodesChange: f, nodeLookup: p, fitViewQueued: h, onNodesChangeMiddlewareMap: m } = t.getState();
    let w = u;
    for (const x of c)
      w = typeof x == "function" ? x(w) : x;
    let v = pc({
      items: w,
      lookup: p
    });
    for (const x of m.values())
      v = x(v);
    d && l(w), v.length > 0 ? f?.(v) : h && window.requestAnimationFrame(() => {
      const { fitViewQueued: x, nodes: b, setNodes: g } = t.getState();
      x && g(b);
    });
  }, []), i = mc(n), r = se((c) => {
    const { edges: u = [], setEdges: l, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: p } = t.getState();
    let h = u;
    for (const m of c)
      h = typeof m == "function" ? m(h) : m;
    d ? l(h) : f && f(pc({
      items: h,
      lookup: p
    }));
  }, []), s = mc(r), a = le(() => ({ nodeQueue: i, edgeQueue: s }), []);
  return o.jsx(cd.Provider, { value: a, children: e });
}
function zv() {
  const e = Un(cd);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Vv = (e) => !!e.panZoom;
function Ds() {
  const e = Dv(), t = je(), n = zv(), i = he(Vv), r = le(() => {
    const s = (f) => t.getState().nodeLookup.get(f), a = (f) => {
      n.nodeQueue.push(f);
    }, c = (f) => {
      n.edgeQueue.push(f);
    }, u = (f) => {
      const { nodeLookup: p, nodeOrigin: h } = t.getState(), m = gc(f) ? f : p.get(f.id), w = m.parentId ? Du(m.position, m.measured, m.parentId, p, h) : m.position, v = {
        ...m,
        position: w,
        width: m.measured?.width ?? m.width,
        height: m.measured?.height ?? m.height
      };
      return on(v);
    }, l = (f, p, h = { replace: !1 }) => {
      a((m) => m.map((w) => {
        if (w.id === f) {
          const v = typeof p == "function" ? p(w) : p;
          return h.replace && gc(v) ? v : { ...w, ...v };
        }
        return w;
      }));
    }, d = (f, p, h = { replace: !1 }) => {
      c((m) => m.map((w) => {
        if (w.id === f) {
          const v = typeof p == "function" ? p(w) : p;
          return h.replace && Pv(v) ? v : { ...w, ...v };
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
        const { nodes: f = [], edges: p = [], transform: h } = t.getState(), [m, w, v] = h;
        return {
          nodes: f.map((x) => ({ ...x })),
          edges: p.map((x) => ({ ...x })),
          viewport: {
            x: m,
            y: w,
            zoom: v
          }
        };
      },
      deleteElements: async ({ nodes: f = [], edges: p = [] }) => {
        const { nodes: h, edges: m, onNodesDelete: w, onEdgesDelete: v, triggerNodeChanges: x, triggerEdgeChanges: b, onDelete: g, onBeforeDelete: y } = t.getState(), { nodes: N, edges: j } = await sw({
          nodesToRemove: f,
          edgesToRemove: p,
          nodes: h,
          edges: m,
          onBeforeDelete: y
        }), S = j.length > 0, E = N.length > 0;
        if (S) {
          const _ = j.map(hc);
          v?.(j), b(_);
        }
        if (E) {
          const _ = N.map(hc);
          w?.(N), x(_);
        }
        return (E || S) && g?.({ nodes: N, edges: j }), { deletedNodes: N, deletedEdges: j };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, p = !0, h) => {
        const m = Va(f), w = m ? f : u(f), v = h !== void 0;
        return w ? (h || t.getState().nodes).filter((x) => {
          const b = t.getState().nodeLookup.get(x.id);
          if (b && !m && (x.id === f.id || !b.internals.positionAbsolute))
            return !1;
          const g = on(v ? x : b), y = Xn(g, w);
          return p && y > 0 || y >= g.width * g.height || y >= w.width * w.height;
        }) : [];
      },
      isNodeIntersecting: (f, p, h = !0) => {
        const w = Va(f) ? f : u(f);
        if (!w)
          return !1;
        const v = Xn(w, p);
        return h && v > 0 || v >= p.width * p.height || v >= w.width * w.height;
      },
      updateNode: l,
      updateNodeData: (f, p, h = { replace: !1 }) => {
        l(f, (m) => {
          const w = typeof p == "function" ? p(m) : p;
          return h.replace ? { ...m, data: w } : { ...m, data: { ...m.data, ...w } };
        }, h);
      },
      updateEdge: d,
      updateEdgeData: (f, p, h = { replace: !1 }) => {
        d(f, (m) => {
          const w = typeof p == "function" ? p(m) : p;
          return h.replace ? { ...m, data: w } : { ...m, data: { ...m.data, ...w } };
        }, h);
      },
      getNodesBounds: (f) => {
        const { nodeLookup: p, nodeOrigin: h } = t.getState();
        return nw(f, { nodeLookup: p, nodeOrigin: h });
      },
      getHandleConnections: ({ type: f, id: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}-${f}${p ? `-${p}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}${f ? p ? `-${f}-${p}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const p = t.getState().fitViewResolver ?? lw();
        return t.setState({ fitViewQueued: !0, fitViewOptions: f, fitViewResolver: p }), n.nodeQueue.push((h) => [...h]), p.promise;
      }
    };
  }, []);
  return le(() => ({
    ...r,
    ...e,
    viewportInitialized: i
  }), [i]);
}
const yc = (e) => e.selected, Ov = typeof window < "u" ? window : void 0;
function Hv({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = je(), { deleteElements: i } = Ds(), r = Yn(e, { actInsideInputWithModifier: !1 }), s = Yn(t, { target: Ov });
  Q(() => {
    if (r) {
      const { edges: a, nodes: c } = n.getState();
      i({ nodes: c.filter(yc), edges: a.filter(yc) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), Q(() => {
    n.setState({ multiSelectionActive: s });
  }, [s]);
}
function Wv(e) {
  const t = je();
  Q(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const i = Cs(e.current);
      (i.height === 0 || i.width === 0) && t.getState().onError?.("004", qe.error004()), t.setState({ width: i.width || 500, height: i.height || 500 });
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
const br = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, Fv = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function Bv({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: i = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: s = kt.Free, zoomOnDoubleClick: a = !0, panOnDrag: c = !0, defaultViewport: u, translateExtent: l, minZoom: d, maxZoom: f, zoomActivationKeyCode: p, preventScrolling: h = !0, children: m, noWheelClassName: w, noPanClassName: v, onViewportChange: x, isControlledViewport: b, paneClickDistance: g, selectionOnDrag: y }) {
  const N = je(), j = re(null), { userSelectionActive: S, lib: E, connectionInProgress: _ } = he(Fv, ve), M = Yn(p), I = re();
  Wv(j);
  const $ = se((z) => {
    x?.({ x: z[0], y: z[1], zoom: z[2] }), b || N.setState({ transform: z });
  }, [x, b]);
  return Q(() => {
    if (j.current) {
      I.current = Xw({
        domNode: j.current,
        minZoom: d,
        maxZoom: f,
        translateExtent: l,
        viewport: u,
        onDraggingChange: (k) => N.setState((D) => D.paneDragging === k ? D : { paneDragging: k }),
        onPanZoomStart: (k, D) => {
          const { onViewportChangeStart: P, onMoveStart: T } = N.getState();
          T?.(k, D), P?.(D);
        },
        onPanZoom: (k, D) => {
          const { onViewportChange: P, onMove: T } = N.getState();
          T?.(k, D), P?.(D);
        },
        onPanZoomEnd: (k, D) => {
          const { onViewportChangeEnd: P, onMoveEnd: T } = N.getState();
          T?.(k, D), P?.(D);
        }
      });
      const { x: z, y: C, zoom: A } = I.current.getViewport();
      return N.setState({
        panZoom: I.current,
        transform: [z, C, A],
        domNode: j.current.closest(".react-flow")
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
      noPanClassName: v,
      userSelectionActive: S,
      noWheelClassName: w,
      lib: E,
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
    v,
    S,
    w,
    E,
    $,
    _,
    y,
    g
  ]), o.jsx("div", { className: "react-flow__renderer", ref: j, style: br, children: m });
}
const Kv = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function Xv() {
  const { userSelectionActive: e, userSelectionRect: t } = he(Kv, ve);
  return e && t ? o.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const So = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, qv = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function Yv({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Kn.Full, panOnDrag: i, autoPanOnSelection: r, paneClickDistance: s, selectionOnDrag: a, onSelectionStart: c, onSelectionEnd: u, onPaneClick: l, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: p, onPaneMouseMove: h, onPaneMouseLeave: m, children: w }) {
  const v = re(0), x = je(), { userSelectionActive: b, elementsSelectable: g, dragging: y, connectionInProgress: N, panBy: j, autoPanSpeed: S } = he(qv, ve), E = g && (e || b), _ = re(null), M = re(), I = re(/* @__PURE__ */ new Set()), $ = re(/* @__PURE__ */ new Set()), z = re(!1), C = re({ x: 0, y: 0 }), A = re(!1), k = (R) => {
    if (z.current || N) {
      z.current = !1;
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
    z.current && (R.stopPropagation(), z.current = !1);
  }, F = (R) => {
    const { domNode: X, transform: ue } = x.getState();
    if (M.current = X?.getBoundingClientRect(), !M.current)
      return;
    const ce = R.target === _.current;
    if (!ce && !!R.target.closest(".nokey") || !e || !(a && ce || t) || R.button !== 0 || !R.isPrimary)
      return;
    R.target?.setPointerCapture?.(R.pointerId), z.current = !1;
    const { x: fe, y: O } = Qe(R.nativeEvent, M.current), te = pn({ x: fe, y: O }, ue);
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
    const { userSelectionRect: ue } = x.getState();
    if (!ue)
      return;
    const { transform: ce, nodeLookup: ee, edgeLookup: ne, connectionLookup: fe, triggerNodeChanges: O, triggerEdgeChanges: te, defaultEdgeOptions: ge } = x.getState(), me = { x: ue.startX, y: ue.startY }, { x: _e, y: De } = sn(me, ce), $e = {
      startX: me.x,
      startY: me.y,
      x: R < _e ? R : _e,
      y: X < De ? X : De,
      width: Math.abs(R - _e),
      height: Math.abs(X - De)
    }, st = I.current, Ye = $.current;
    I.current = new Set(js(ee, $e, ce, n === Kn.Partial, !0).map((Pe) => Pe.id)), $.current = /* @__PURE__ */ new Set();
    const Ue = ge?.selectable ?? !0;
    for (const Pe of I.current) {
      const He = fe.get(Pe);
      if (He)
        for (const { edgeId: We } of He.values()) {
          const Ne = ne.get(We);
          Ne && (Ne.selectable ?? Ue) && $.current.add(We);
        }
    }
    if (!Oa(st, I.current)) {
      const Pe = qt(ee, I.current, !0);
      O(Pe);
    }
    if (!Oa(Ye, $.current)) {
      const Pe = qt(ne, $.current);
      te(Pe);
    }
    x.setState({
      userSelectionRect: $e,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function H() {
    if (!r || !M.current)
      return;
    const [R, X] = Ns(C.current, M.current, S);
    j({ x: R, y: X }).then((ue) => {
      if (!z.current || !ue) {
        v.current = requestAnimationFrame(H);
        return;
      }
      const { x: ce, y: ee } = C.current;
      W(ce, ee), v.current = requestAnimationFrame(H);
    });
  }
  const Y = () => {
    cancelAnimationFrame(v.current), v.current = 0, A.current = !1;
  };
  Q(() => () => Y(), []);
  const V = (R) => {
    const { userSelectionRect: X, transform: ue, resetSelectedElements: ce } = x.getState();
    if (!M.current || !X)
      return;
    const { x: ee, y: ne } = Qe(R.nativeEvent, M.current);
    C.current = { x: ee, y: ne };
    const fe = sn({ x: X.startX, y: X.startY }, ue);
    if (!z.current) {
      const O = t ? 0 : s;
      if (Math.hypot(ee - fe.x, ne - fe.y) <= O)
        return;
      ce(), c?.(R);
    }
    z.current = !0, A.current || (H(), A.current = !0), W(ee, ne);
  }, q = (R) => {
    R.button === 0 && (R.target?.releasePointerCapture?.(R.pointerId), !b && R.target === _.current && x.getState().userSelectionRect && k?.(R), x.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), z.current && (u?.(R), x.setState({
      nodesSelectionActive: I.current.size > 0
    })), Y());
  }, J = (R) => {
    R.target?.releasePointerCapture?.(R.pointerId), Y();
  }, Z = i === !0 || Array.isArray(i) && i.includes(0);
  return o.jsxs("div", { className: Ee(["react-flow__pane", { draggable: Z, dragging: y, selection: e }]), onClick: E ? void 0 : So(k, _), onContextMenu: So(D, _), onWheel: So(P, _), onPointerEnter: E ? void 0 : p, onPointerMove: E ? V : h, onPointerUp: E ? q : void 0, onPointerCancel: E ? J : void 0, onPointerDownCapture: E ? F : void 0, onClickCapture: E ? T : void 0, onPointerLeave: m, ref: _, style: br, children: [w, o.jsx(Xv, {})] });
}
function Bo({ id: e, store: t, unselect: n = !1, nodeRef: i }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: s, multiSelectionActive: a, nodeLookup: c, onError: u } = t.getState(), l = c.get(e);
  if (!l) {
    u?.("012", qe.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && a) && (s({ nodes: [l], edges: [] }), requestAnimationFrame(() => i?.current?.blur())) : r([e]);
}
function ld({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: i, nodeId: r, isSelectable: s, nodeClickDistance: a }) {
  const c = je(), [u, l] = K(!1), d = re();
  return Q(() => {
    d.current = Tw({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (f) => {
        Bo({
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
const Uv = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function ud() {
  const e = je();
  return se((n) => {
    const { nodeExtent: i, snapToGrid: r, snapGrid: s, nodesDraggable: a, onError: c, updateNodePositions: u, nodeLookup: l, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), p = Uv(a), h = r ? s[0] : 5, m = r ? s[1] : 5, w = n.direction.x * h * n.factor, v = n.direction.y * m * n.factor;
    for (const [, x] of l) {
      if (!p(x))
        continue;
      let b = {
        x: x.internals.positionAbsolute.x + w,
        y: x.internals.positionAbsolute.y + v
      };
      r && (b = ii(b, s));
      const { position: g, positionAbsolute: y } = ku({
        nodeId: x.id,
        nextPosition: b,
        nodeLookup: l,
        nodeExtent: i,
        nodeOrigin: d,
        onError: c
      });
      x.position = g, x.internals.positionAbsolute = y, f.set(x.id, x);
    }
    u(f);
  }, []);
}
const Ts = Qo(null), Zv = Ts.Provider;
Ts.Consumer;
const dd = () => Un(Ts), Gv = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), Jv = (e, t, n) => (i) => {
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
function Qv({ type: e = "source", position: t = oe.Top, isValidConnection: n, isConnectable: i = !0, isConnectableStart: r = !0, isConnectableEnd: s = !0, id: a, onConnect: c, children: u, className: l, onMouseDown: d, onTouchStart: f, ...p }, h) {
  const m = a || null, w = e === "target", v = je(), x = dd(), { connectOnClick: b, noPanClassName: g, rfId: y } = he(Gv, ve), { connectingFrom: N, connectingTo: j, clickConnecting: S, isPossibleEndHandle: E, connectionInProcess: _, clickConnectionInProcess: M, valid: I } = he(Jv(x, m, e), ve);
  x || v.getState().onError?.("010", qe.error010());
  const $ = (A) => {
    const { defaultEdgeOptions: k, onConnect: D, hasDefaultEdges: P } = v.getState(), T = {
      ...k,
      ...A
    };
    if (P) {
      const { edges: F, setEdges: W, onError: H } = v.getState();
      W(sd(T, F, { onError: H }));
    }
    D?.(T), c?.(T);
  }, z = (A) => {
    if (!x)
      return;
    const k = Pu(A.nativeEvent);
    if (r && (k && A.button === 0 || !k)) {
      const D = v.getState();
      Fo.onPointerDown(A.nativeEvent, {
        handleDomNode: A.currentTarget,
        autoPanOnConnect: D.autoPanOnConnect,
        connectionMode: D.connectionMode,
        connectionRadius: D.connectionRadius,
        domNode: D.domNode,
        nodeLookup: D.nodeLookup,
        lib: D.lib,
        isTarget: w,
        handleId: m,
        nodeId: x,
        flowId: D.rfId,
        panBy: D.panBy,
        cancelConnection: D.cancelConnection,
        onConnectStart: D.onConnectStart,
        onConnectEnd: (...P) => v.getState().onConnectEnd?.(...P),
        updateConnection: D.updateConnection,
        onConnect: $,
        isValidConnection: n || ((...P) => v.getState().isValidConnection?.(...P) ?? !0),
        getTransform: () => v.getState().transform,
        getFromHandle: () => v.getState().connection.fromHandle,
        autoPanSpeed: D.autoPanSpeed,
        dragThreshold: D.connectionDragThreshold
      });
    }
    k ? d?.(A) : f?.(A);
  }, C = (A) => {
    const { onClickConnectStart: k, onClickConnectEnd: D, connectionClickStartHandle: P, connectionMode: T, isValidConnection: F, lib: W, rfId: H, nodeLookup: Y, connection: V } = v.getState();
    if (!x || !P && !r)
      return;
    if (!P) {
      k?.(A.nativeEvent, { nodeId: x, handleId: m, handleType: e }), v.setState({ connectionClickStartHandle: { nodeId: x, type: e, id: m } });
      return;
    }
    const q = Tu(A.target), J = n || F, { connection: Z, isValid: R } = Fo.isValid(A.nativeEvent, {
      handle: {
        nodeId: x,
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
      nodeLookup: Y
    });
    R && Z && $(Z);
    const X = structuredClone(V);
    delete X.inProgress, X.toPosition = X.toHandle ? X.toHandle.position : null, D?.(A, X), v.setState({ connectionClickStartHandle: null });
  };
  return o.jsx("div", { "data-handleid": m, "data-nodeid": x, "data-handlepos": t, "data-id": `${y}-${x}-${m}-${e}`, className: Ee([
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
      connectingfrom: N,
      connectingto: j,
      valid: I,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: i && (!_ || E) && (_ || M ? s : r)
    }
  ]), onMouseDown: z, onTouchStart: z, onClick: b ? C : void 0, ref: h, ...p, children: u });
}
const cn = ke(ad(Qv));
function e0({ data: e, isConnectable: t, sourcePosition: n = oe.Bottom }) {
  return o.jsxs(o.Fragment, { children: [e?.label, o.jsx(cn, { type: "source", position: n, isConnectable: t })] });
}
function t0({ data: e, isConnectable: t, targetPosition: n = oe.Top, sourcePosition: i = oe.Bottom }) {
  return o.jsxs(o.Fragment, { children: [o.jsx(cn, { type: "target", position: n, isConnectable: t }), e?.label, o.jsx(cn, { type: "source", position: i, isConnectable: t })] });
}
function n0() {
  return null;
}
function i0({ data: e, isConnectable: t, targetPosition: n = oe.Top }) {
  return o.jsxs(o.Fragment, { children: [o.jsx(cn, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const ir = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, xc = {
  input: e0,
  default: t0,
  output: i0,
  group: n0
};
function r0(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const o0 = (e) => {
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
function s0({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const i = je(), { width: r, height: s, transformString: a, userSelectionActive: c } = he(o0, ve), u = ud(), l = re(null);
  Q(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !c && r !== null && s !== null;
  if (ld({
    nodeRef: l,
    disabled: !d
  }), !d)
    return null;
  const f = e ? (h) => {
    const m = i.getState().nodes.filter((w) => w.selected);
    e(h, m);
  } : void 0, p = (h) => {
    Object.prototype.hasOwnProperty.call(ir, h.key) && (h.preventDefault(), u({
      direction: ir[h.key],
      factor: h.shiftKey ? 4 : 1
    }));
  };
  return o.jsx("div", { className: Ee(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: a
  }, children: o.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : p, style: {
    width: r,
    height: s
  } }) });
}
const wc = typeof window < "u" ? window : void 0, a0 = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function fd({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: i, onPaneMouseLeave: r, onPaneContextMenu: s, onPaneScroll: a, paneClickDistance: c, deleteKeyCode: u, selectionKeyCode: l, selectionOnDrag: d, selectionMode: f, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: m, panActivationKeyCode: w, zoomActivationKeyCode: v, elementsSelectable: x, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: y, panOnScrollSpeed: N, panOnScrollMode: j, zoomOnDoubleClick: S, panOnDrag: E, autoPanOnSelection: _, defaultViewport: M, translateExtent: I, minZoom: $, maxZoom: z, preventScrolling: C, onSelectionContextMenu: A, noWheelClassName: k, noPanClassName: D, disableKeyboardA11y: P, onViewportChange: T, isControlledViewport: F }) {
  const { nodesSelectionActive: W, userSelectionActive: H } = he(a0, ve), Y = Yn(l, { target: wc }), V = Yn(w, { target: wc }), q = V || E, J = V || y, Z = d && q !== !0, R = Y || H || Z;
  return Hv({ deleteKeyCode: u, multiSelectionKeyCode: m }), o.jsx(Bv, { onPaneContextMenu: s, elementsSelectable: x, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: J, panOnScrollSpeed: N, panOnScrollMode: j, zoomOnDoubleClick: S, panOnDrag: !Y && q, defaultViewport: M, translateExtent: I, minZoom: $, maxZoom: z, zoomActivationKeyCode: v, preventScrolling: C, noWheelClassName: k, noPanClassName: D, onViewportChange: T, isControlledViewport: F, paneClickDistance: c, selectionOnDrag: Z, children: o.jsxs(Yv, { onSelectionStart: p, onSelectionEnd: h, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: i, onPaneMouseLeave: r, onPaneContextMenu: s, onPaneScroll: a, panOnDrag: q, autoPanOnSelection: _, isSelecting: !!R, selectionMode: f, selectionKeyPressed: Y, paneClickDistance: c, selectionOnDrag: Z, children: [e, W && o.jsx(s0, { onSelectionContextMenu: A, noPanClassName: D, disableKeyboardA11y: P })] }) });
}
fd.displayName = "FlowRenderer";
const c0 = ke(fd), l0 = (e) => (t) => e ? js(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function u0(e) {
  return he(se(l0(e), [e]), ve);
}
const d0 = (e) => e.updateNodeInternals;
function f0() {
  const e = he(d0), [t] = K(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
function p0({ node: e, nodeType: t, hasDimensions: n, resizeObserver: i }) {
  const r = je(), s = re(null), a = re(null), c = re(e.sourcePosition), u = re(e.targetPosition), l = re(t), d = n && !!e.internals.handleBounds;
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
function h0({ id: e, onClick: t, onMouseEnter: n, onMouseMove: i, onMouseLeave: r, onContextMenu: s, onDoubleClick: a, nodesDraggable: c, elementsSelectable: u, nodesConnectable: l, nodesFocusable: d, resizeObserver: f, noDragClassName: p, noPanClassName: h, disableKeyboardA11y: m, rfId: w, nodeTypes: v, nodeClickDistance: x, onError: b }) {
  const { node: g, internals: y, isParent: N } = he((R) => {
    const X = R.nodeLookup.get(e), ue = R.parentLookup.has(e);
    return {
      node: X,
      internals: X.internals,
      isParent: ue
    };
  }, ve);
  let j = g.type || "default", S = v?.[j] || xc[j];
  S === void 0 && (b?.("003", qe.error003(j)), j = "default", S = v?.default || xc.default);
  const E = !!(g.draggable || c && typeof g.draggable > "u"), _ = !!(g.selectable || u && typeof g.selectable > "u"), M = !!(g.connectable || l && typeof g.connectable > "u"), I = !!(g.focusable || d && typeof g.focusable > "u"), $ = je(), z = _u(g), C = p0({ node: g, nodeType: j, hasDimensions: z, resizeObserver: f }), A = ld({
    nodeRef: C,
    disabled: g.hidden || !E,
    noDragClassName: p,
    handleSelector: g.dragHandle,
    nodeId: e,
    isSelectable: _,
    nodeClickDistance: x
  }), k = ud();
  if (g.hidden)
    return null;
  const D = ft(g), P = r0(g), T = _ || E || t || n || i || r, F = n ? (R) => n(R, { ...y.userNode }) : void 0, W = i ? (R) => i(R, { ...y.userNode }) : void 0, H = r ? (R) => r(R, { ...y.userNode }) : void 0, Y = s ? (R) => s(R, { ...y.userNode }) : void 0, V = a ? (R) => a(R, { ...y.userNode }) : void 0, q = (R) => {
    const { selectNodesOnDrag: X, nodeDragThreshold: ue } = $.getState();
    _ && (!X || !E || ue > 0) && Bo({
      id: e,
      store: $,
      nodeRef: C
    }), t && t(R, { ...y.userNode });
  }, J = (R) => {
    if (!($u(R.nativeEvent) || m)) {
      if (bu.includes(R.key) && _) {
        const X = R.key === "Escape";
        Bo({
          id: e,
          store: $,
          unselect: X,
          nodeRef: C
        });
      } else if (E && g.selected && Object.prototype.hasOwnProperty.call(ir, R.key)) {
        R.preventDefault();
        const { ariaLabelConfig: X } = $.getState();
        $.setState({
          ariaLiveMessage: X["node.a11yDescription.ariaLiveMessage"]({
            direction: R.key.replace("Arrow", "").toLowerCase(),
            x: ~~y.positionAbsolute.x,
            y: ~~y.positionAbsolute.y
          })
        }), k({
          direction: ir[R.key],
          factor: R.shiftKey ? 4 : 1
        });
      }
    }
  }, Z = () => {
    if (m || !C.current?.matches(":focus-visible"))
      return;
    const { transform: R, width: X, height: ue, autoPanOnNodeFocus: ce, setCenter: ee } = $.getState();
    if (!ce)
      return;
    js(/* @__PURE__ */ new Map([[e, g]]), { x: 0, y: 0, width: X, height: ue }, R, !0).length > 0 || ee(g.position.x + D.width / 2, g.position.y + D.height / 2, {
      zoom: R[2]
    });
  };
  return o.jsx("div", { className: Ee([
    "react-flow__node",
    `react-flow__node-${j}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [h]: E
    },
    g.className,
    {
      selected: g.selected,
      selectable: _,
      parent: N,
      draggable: E,
      dragging: A
    }
  ]), ref: C, style: {
    zIndex: y.z,
    transform: `translate(${y.positionAbsolute.x}px,${y.positionAbsolute.y}px)`,
    pointerEvents: T ? "all" : "none",
    visibility: z ? "visible" : "hidden",
    ...g.style,
    ...P
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: F, onMouseMove: W, onMouseLeave: H, onContextMenu: Y, onClick: q, onDoubleClick: V, onKeyDown: I ? J : void 0, tabIndex: I ? 0 : void 0, onFocus: I ? Z : void 0, role: g.ariaRole ?? (I ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": m ? void 0 : `${Qu}-${w}`, "aria-label": g.ariaLabel, ...g.domAttributes, children: o.jsx(Zv, { value: e, children: o.jsx(S, { id: e, data: g.data, type: j, positionAbsoluteX: y.positionAbsolute.x, positionAbsoluteY: y.positionAbsolute.y, selected: g.selected ?? !1, selectable: _, draggable: E, deletable: g.deletable ?? !0, isConnectable: M, sourcePosition: g.sourcePosition, targetPosition: g.targetPosition, dragging: A, dragHandle: g.dragHandle, zIndex: y.z, parentId: g.parentId, ...D }) }) });
}
var g0 = ke(h0);
const m0 = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function pd(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: i, elementsSelectable: r, onError: s } = he(m0, ve), a = u0(e.onlyRenderVisibleElements), c = f0();
  return o.jsx("div", { className: "react-flow__nodes", style: br, children: a.map((u) => (
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
    o.jsx(g0, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: i, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: s }, u)
  )) });
}
pd.displayName = "NodeRenderer";
const y0 = ke(pd);
function x0(e) {
  return he(se((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const i = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const s = n.nodeLookup.get(r.source), a = n.nodeLookup.get(r.target);
        s && a && pw({
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
const w0 = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return o.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, v0 = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return o.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, vc = {
  [er.Arrow]: w0,
  [er.ArrowClosed]: v0
};
function b0(e) {
  const t = je();
  return le(() => Object.prototype.hasOwnProperty.call(vc, e) ? vc[e] : (t.getState().onError?.("009", qe.error009(e)), null), [e]);
}
const j0 = ({ id: e, type: t, color: n, width: i = 12.5, height: r = 12.5, markerUnits: s = "strokeWidth", strokeWidth: a, orient: c = "auto-start-reverse" }) => {
  const u = b0(t);
  return u ? o.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${i}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: s, orient: c, refX: "0", refY: "0", children: o.jsx(u, { color: n, strokeWidth: a }) }) : null;
}, hd = ({ defaultColor: e, rfId: t }) => {
  const n = he((s) => s.edges), i = he((s) => s.defaultEdgeOptions), r = le(() => bw(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: i?.markerStart,
    defaultMarkerEnd: i?.markerEnd
  }), [n, i, t, e]);
  return r.length ? o.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: o.jsx("defs", { children: r.map((s) => o.jsx(j0, { id: s.id, type: s.type, color: s.color, width: s.width, height: s.height, markerUnits: s.markerUnits, strokeWidth: s.strokeWidth, orient: s.orient }, s.id)) }) }) : null;
};
hd.displayName = "MarkerDefinitions";
var N0 = ke(hd);
function gd({ x: e, y: t, label: n, labelStyle: i, labelShowBg: r = !0, labelBgStyle: s, labelBgPadding: a = [2, 4], labelBgBorderRadius: c = 2, children: u, className: l, ...d }) {
  const [f, p] = K({ x: 1, y: 0, width: 0, height: 0 }), h = Ee(["react-flow__edge-textwrapper", l]), m = re(null);
  return Q(() => {
    if (m.current) {
      const w = m.current.getBBox();
      p({
        x: w.x,
        y: w.y,
        width: w.width,
        height: w.height
      });
    }
  }, [n]), n ? o.jsxs("g", { transform: `translate(${e - f.width / 2} ${t - f.height / 2})`, className: h, visibility: f.width ? "visible" : "hidden", ...d, children: [r && o.jsx("rect", { width: f.width + 2 * a[0], x: -a[0], y: -a[1], height: f.height + 2 * a[1], className: "react-flow__edge-textbg", style: s, rx: c, ry: c }), o.jsx("text", { className: "react-flow__edge-text", y: f.height / 2, dy: "0.3em", ref: m, style: i, children: n }), u] }) : null;
}
gd.displayName = "EdgeText";
const S0 = ke(gd);
function ri({ path: e, labelX: t, labelY: n, label: i, labelStyle: r, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u, interactionWidth: l = 20, ...d }) {
  return o.jsxs(o.Fragment, { children: [o.jsx("path", { ...d, d: e, fill: "none", className: Ee(["react-flow__edge-path", d.className]) }), l ? o.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, i && Je(t) && Je(n) ? o.jsx(S0, { x: t, y: n, label: i, labelStyle: r, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u }) : null] });
}
function bc({ pos: e, x1: t, y1: n, x2: i, y2: r }) {
  return e === oe.Left || e === oe.Right ? [0.5 * (t + i), n] : [t, 0.5 * (n + r)];
}
function md({ sourceX: e, sourceY: t, sourcePosition: n = oe.Bottom, targetX: i, targetY: r, targetPosition: s = oe.Top }) {
  const [a, c] = bc({
    pos: n,
    x1: e,
    y1: t,
    x2: i,
    y2: r
  }), [u, l] = bc({
    pos: s,
    x1: i,
    y1: r,
    x2: e,
    y2: t
  }), [d, f, p, h] = Mu({
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
function yd(e) {
  return ke(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, sourcePosition: a, targetPosition: c, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: m, markerEnd: w, markerStart: v, interactionWidth: x }) => {
    const [b, g, y] = md({
      sourceX: n,
      sourceY: i,
      sourcePosition: a,
      targetX: r,
      targetY: s,
      targetPosition: c
    }), N = e.isInternal ? void 0 : t;
    return o.jsx(ri, { id: N, path: b, labelX: g, labelY: y, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: m, markerEnd: w, markerStart: v, interactionWidth: x });
  });
}
const C0 = yd({ isInternal: !1 }), xd = yd({ isInternal: !0 });
C0.displayName = "SimpleBezierEdge";
xd.displayName = "SimpleBezierEdgeInternal";
function wd(e) {
  return ke(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, sourcePosition: h = oe.Bottom, targetPosition: m = oe.Top, markerEnd: w, markerStart: v, pathOptions: x, interactionWidth: b }) => {
    const [g, y, N] = nr({
      sourceX: n,
      sourceY: i,
      sourcePosition: h,
      targetX: r,
      targetY: s,
      targetPosition: m,
      borderRadius: x?.borderRadius,
      offset: x?.offset,
      stepPosition: x?.stepPosition
    }), j = e.isInternal ? void 0 : t;
    return o.jsx(ri, { id: j, path: g, labelX: y, labelY: N, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: w, markerStart: v, interactionWidth: b });
  });
}
const vd = wd({ isInternal: !1 }), bd = wd({ isInternal: !0 });
vd.displayName = "SmoothStepEdge";
bd.displayName = "SmoothStepEdgeInternal";
function jd(e) {
  return ke(({ id: t, ...n }) => {
    const i = e.isInternal ? void 0 : t;
    return o.jsx(vd, { ...n, id: i, pathOptions: le(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const k0 = jd({ isInternal: !1 }), Nd = jd({ isInternal: !0 });
k0.displayName = "StepEdge";
Nd.displayName = "StepEdgeInternal";
function Sd(e) {
  return ke(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: m, interactionWidth: w }) => {
    const [v, x, b] = Vu({ sourceX: n, sourceY: i, targetX: r, targetY: s }), g = e.isInternal ? void 0 : t;
    return o.jsx(ri, { id: g, path: v, labelX: x, labelY: b, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: m, interactionWidth: w });
  });
}
const E0 = Sd({ isInternal: !1 }), Cd = Sd({ isInternal: !0 });
E0.displayName = "StraightEdge";
Cd.displayName = "StraightEdgeInternal";
function kd(e) {
  return ke(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, sourcePosition: a = oe.Bottom, targetPosition: c = oe.Top, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: m, markerEnd: w, markerStart: v, pathOptions: x, interactionWidth: b }) => {
    const [g, y, N] = Ru({
      sourceX: n,
      sourceY: i,
      sourcePosition: a,
      targetX: r,
      targetY: s,
      targetPosition: c,
      curvature: x?.curvature
    }), j = e.isInternal ? void 0 : t;
    return o.jsx(ri, { id: j, path: g, labelX: y, labelY: N, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: m, markerEnd: w, markerStart: v, interactionWidth: b });
  });
}
const I0 = kd({ isInternal: !1 }), Ed = kd({ isInternal: !0 });
I0.displayName = "BezierEdge";
Ed.displayName = "BezierEdgeInternal";
const jc = {
  default: Ed,
  straight: Cd,
  step: Nd,
  smoothstep: bd,
  simplebezier: xd
}, Nc = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, A0 = (e, t, n) => n === oe.Left ? e - t : n === oe.Right ? e + t : e, _0 = (e, t, n) => n === oe.Top ? e - t : n === oe.Bottom ? e + t : e, Sc = "react-flow__edgeupdater";
function Cc({ position: e, centerX: t, centerY: n, radius: i = 10, onMouseDown: r, onMouseEnter: s, onMouseOut: a, type: c }) {
  return o.jsx("circle", { onMouseDown: r, onMouseEnter: s, onMouseOut: a, className: Ee([Sc, `${Sc}-${c}`]), cx: A0(t, i, e), cy: _0(n, i, e), r: i, stroke: "transparent", fill: "transparent" });
}
function D0({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: i, sourceY: r, targetX: s, targetY: a, sourcePosition: c, targetPosition: u, onReconnect: l, onReconnectStart: d, onReconnectEnd: f, setReconnecting: p, setUpdateHover: h }) {
  const m = je(), w = (y, N) => {
    if (y.button !== 0)
      return;
    const { autoPanOnConnect: j, domNode: S, connectionMode: E, connectionRadius: _, lib: M, onConnectStart: I, cancelConnection: $, nodeLookup: z, rfId: C, panBy: A, updateConnection: k } = m.getState(), D = N.type === "target", P = (W, H) => {
      p(!1), f?.(W, n, N.type, H);
    }, T = (W) => l?.(n, W), F = (W, H) => {
      p(!0), d?.(y, n, N.type), I?.(W, H);
    };
    Fo.onPointerDown(y.nativeEvent, {
      autoPanOnConnect: j,
      connectionMode: E,
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
      updateConnection: k,
      getTransform: () => m.getState().transform,
      getFromHandle: () => m.getState().connection.fromHandle,
      dragThreshold: m.getState().connectionDragThreshold,
      handleDomNode: y.currentTarget
    });
  }, v = (y) => w(y, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), x = (y) => w(y, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), b = () => h(!0), g = () => h(!1);
  return o.jsxs(o.Fragment, { children: [(e === !0 || e === "source") && o.jsx(Cc, { position: c, centerX: i, centerY: r, radius: t, onMouseDown: v, onMouseEnter: b, onMouseOut: g, type: "source" }), (e === !0 || e === "target") && o.jsx(Cc, { position: u, centerX: s, centerY: a, radius: t, onMouseDown: x, onMouseEnter: b, onMouseOut: g, type: "target" })] });
}
function T0({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: i, onClick: r, onDoubleClick: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, rfId: m, edgeTypes: w, noPanClassName: v, onError: x, disableKeyboardA11y: b }) {
  let g = he((ee) => ee.edgeLookup.get(e));
  const y = he((ee) => ee.defaultEdgeOptions);
  g = y ? { ...y, ...g } : g;
  let N = g.type || "default", j = w?.[N] || jc[N];
  j === void 0 && (x?.("011", qe.error011(N)), N = "default", j = w?.default || jc.default);
  const S = !!(g.focusable || t && typeof g.focusable > "u"), E = typeof f < "u" && (g.reconnectable || n && typeof g.reconnectable > "u"), _ = !!(g.selectable || i && typeof g.selectable > "u"), M = re(null), [I, $] = K(!1), [z, C] = K(!1), A = je(), { zIndex: k, sourceX: D, sourceY: P, targetX: T, targetY: F, sourcePosition: W, targetPosition: H } = he(se((ee) => {
    const ne = ee.nodeLookup.get(g.source), fe = ee.nodeLookup.get(g.target);
    if (!ne || !fe)
      return {
        zIndex: g.zIndex,
        ...Nc
      };
    const O = vw({
      id: e,
      sourceNode: ne,
      targetNode: fe,
      sourceHandle: g.sourceHandle || null,
      targetHandle: g.targetHandle || null,
      connectionMode: ee.connectionMode,
      onError: x
    });
    return {
      zIndex: fw({
        selected: g.selected,
        zIndex: g.zIndex,
        sourceNode: ne,
        targetNode: fe,
        elevateOnSelect: ee.elevateEdgesOnSelect,
        zIndexMode: ee.zIndexMode
      }),
      ...O || Nc
    };
  }, [g.source, g.target, g.sourceHandle, g.targetHandle, g.selected, g.zIndex]), ve), Y = le(() => g.markerStart ? `url('#${Ho(g.markerStart, m)}')` : void 0, [g.markerStart, m]), V = le(() => g.markerEnd ? `url('#${Ho(g.markerEnd, m)}')` : void 0, [g.markerEnd, m]);
  if (g.hidden || D === null || P === null || T === null || F === null)
    return null;
  const q = (ee) => {
    const { addSelectedEdges: ne, unselectNodesAndEdges: fe, multiSelectionActive: O } = A.getState();
    _ && (A.setState({ nodesSelectionActive: !1 }), g.selected && O ? (fe({ nodes: [], edges: [g] }), M.current?.blur()) : ne([e])), r && r(ee, g);
  }, J = s ? (ee) => {
    s(ee, { ...g });
  } : void 0, Z = a ? (ee) => {
    a(ee, { ...g });
  } : void 0, R = c ? (ee) => {
    c(ee, { ...g });
  } : void 0, X = u ? (ee) => {
    u(ee, { ...g });
  } : void 0, ue = l ? (ee) => {
    l(ee, { ...g });
  } : void 0, ce = (ee) => {
    if (!b && bu.includes(ee.key) && _) {
      const { unselectNodesAndEdges: ne, addSelectedEdges: fe } = A.getState();
      ee.key === "Escape" ? (M.current?.blur(), ne({ edges: [g] })) : fe([e]);
    }
  };
  return o.jsx("svg", { style: { zIndex: k }, children: o.jsxs("g", { className: Ee([
    "react-flow__edge",
    `react-flow__edge-${N}`,
    g.className,
    v,
    {
      selected: g.selected,
      animated: g.animated,
      inactive: !_ && !r,
      updating: I,
      selectable: _
    }
  ]), onClick: q, onDoubleClick: J, onContextMenu: Z, onMouseEnter: R, onMouseMove: X, onMouseLeave: ue, onKeyDown: S ? ce : void 0, tabIndex: S ? 0 : void 0, role: g.ariaRole ?? (S ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": g.ariaLabel === null ? void 0 : g.ariaLabel || `Edge from ${g.source} to ${g.target}`, "aria-describedby": S ? `${ed}-${m}` : void 0, ref: M, ...g.domAttributes, children: [!z && o.jsx(j, { id: e, source: g.source, target: g.target, type: g.type, selected: g.selected, animated: g.animated, selectable: _, deletable: g.deletable ?? !0, label: g.label, labelStyle: g.labelStyle, labelShowBg: g.labelShowBg, labelBgStyle: g.labelBgStyle, labelBgPadding: g.labelBgPadding, labelBgBorderRadius: g.labelBgBorderRadius, sourceX: D, sourceY: P, targetX: T, targetY: F, sourcePosition: W, targetPosition: H, data: g.data, style: g.style, sourceHandleId: g.sourceHandle, targetHandleId: g.targetHandle, markerStart: Y, markerEnd: V, pathOptions: "pathOptions" in g ? g.pathOptions : void 0, interactionWidth: g.interactionWidth }), E && o.jsx(D0, { edge: g, isReconnectable: E, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, sourceX: D, sourceY: P, targetX: T, targetY: F, sourcePosition: W, targetPosition: H, setUpdateHover: $, setReconnecting: C })] }) });
}
var $0 = ke(T0);
const P0 = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function Id({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: i, noPanClassName: r, onReconnect: s, onEdgeContextMenu: a, onEdgeMouseEnter: c, onEdgeMouseMove: u, onEdgeMouseLeave: l, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: p, onReconnectStart: h, onReconnectEnd: m, disableKeyboardA11y: w }) {
  const { edgesFocusable: v, edgesReconnectable: x, elementsSelectable: b, onError: g } = he(P0, ve), y = x0(t);
  return o.jsxs("div", { className: "react-flow__edges", children: [o.jsx(N0, { defaultColor: e, rfId: n }), y.map((N) => o.jsx($0, { id: N, edgesFocusable: v, edgesReconnectable: x, elementsSelectable: b, noPanClassName: r, onReconnect: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, onClick: d, reconnectRadius: f, onDoubleClick: p, onReconnectStart: h, onReconnectEnd: m, rfId: n, onError: g, edgeTypes: i, disableKeyboardA11y: w }, N))] });
}
Id.displayName = "EdgeRenderer";
const M0 = ke(Id), R0 = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function L0({ children: e }) {
  const t = he(R0);
  return o.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function z0(e) {
  const t = Ds(), n = re(!1);
  Q(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const V0 = (e) => e.panZoom?.syncViewport;
function O0(e) {
  const t = he(V0), n = je();
  return Q(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function H0(e) {
  return e.connection.inProgress ? { ...e.connection, to: pn(e.connection.to, e.transform) } : { ...e.connection };
}
function W0(e) {
  return H0;
}
function F0(e) {
  const t = W0();
  return he(t, ve);
}
const B0 = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function K0({ containerStyle: e, style: t, type: n, component: i }) {
  const { nodesConnectable: r, width: s, height: a, isValid: c, inProgress: u } = he(B0, ve);
  return !(s && r && u) ? null : o.jsx("svg", { style: e, width: s, height: a, className: "react-flow__connectionline react-flow__container", children: o.jsx("g", { className: Ee(["react-flow__connection", Su(c)]), children: o.jsx(Ad, { style: t, type: n, CustomComponent: i, isValid: c }) }) });
}
const Ad = ({ style: e, type: t = mt.Bezier, CustomComponent: n, isValid: i }) => {
  const { inProgress: r, from: s, fromNode: a, fromHandle: c, fromPosition: u, to: l, toNode: d, toHandle: f, toPosition: p, pointer: h } = F0();
  if (!r)
    return;
  if (n)
    return o.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: a, fromHandle: c, fromX: s.x, fromY: s.y, toX: l.x, toY: l.y, fromPosition: u, toPosition: p, connectionStatus: Su(i), toNode: d, toHandle: f, pointer: h });
  let m = "";
  const w = {
    sourceX: s.x,
    sourceY: s.y,
    sourcePosition: u,
    targetX: l.x,
    targetY: l.y,
    targetPosition: p
  };
  switch (t) {
    case mt.Bezier:
      [m] = Ru(w);
      break;
    case mt.SimpleBezier:
      [m] = md(w);
      break;
    case mt.Step:
      [m] = nr({
        ...w,
        borderRadius: 0
      });
      break;
    case mt.SmoothStep:
      [m] = nr(w);
      break;
    default:
      [m] = Vu(w);
  }
  return o.jsx("path", { d: m, fill: "none", className: "react-flow__connection-path", style: e });
};
Ad.displayName = "ConnectionLine";
const X0 = {};
function kc(e = X0) {
  re(e), je(), Q(() => {
  }, [e]);
}
function q0() {
  je(), re(!1), Q(() => {
  }, []);
}
function _d({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: i, onEdgeClick: r, onNodeDoubleClick: s, onEdgeDoubleClick: a, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: p, onSelectionEnd: h, connectionLineType: m, connectionLineStyle: w, connectionLineComponent: v, connectionLineContainerStyle: x, selectionKeyCode: b, selectionOnDrag: g, selectionMode: y, multiSelectionKeyCode: N, panActivationKeyCode: j, zoomActivationKeyCode: S, deleteKeyCode: E, onlyRenderVisibleElements: _, elementsSelectable: M, defaultViewport: I, translateExtent: $, minZoom: z, maxZoom: C, preventScrolling: A, defaultMarkerColor: k, zoomOnScroll: D, zoomOnPinch: P, panOnScroll: T, panOnScrollSpeed: F, panOnScrollMode: W, zoomOnDoubleClick: H, panOnDrag: Y, autoPanOnSelection: V, onPaneClick: q, onPaneMouseEnter: J, onPaneMouseMove: Z, onPaneMouseLeave: R, onPaneScroll: X, onPaneContextMenu: ue, paneClickDistance: ce, nodeClickDistance: ee, onEdgeContextMenu: ne, onEdgeMouseEnter: fe, onEdgeMouseMove: O, onEdgeMouseLeave: te, reconnectRadius: ge, onReconnect: me, onReconnectStart: _e, onReconnectEnd: De, noDragClassName: $e, noWheelClassName: st, noPanClassName: Ye, disableKeyboardA11y: Ue, nodeExtent: Pe, rfId: He, viewport: We, onViewportChange: Ne }) {
  return kc(e), kc(t), q0(), z0(n), O0(We), o.jsx(c0, { onPaneClick: q, onPaneMouseEnter: J, onPaneMouseMove: Z, onPaneMouseLeave: R, onPaneContextMenu: ue, onPaneScroll: X, paneClickDistance: ce, deleteKeyCode: E, selectionKeyCode: b, selectionOnDrag: g, selectionMode: y, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: N, panActivationKeyCode: j, zoomActivationKeyCode: S, elementsSelectable: M, zoomOnScroll: D, zoomOnPinch: P, zoomOnDoubleClick: H, panOnScroll: T, panOnScrollSpeed: F, panOnScrollMode: W, panOnDrag: Y, autoPanOnSelection: V, defaultViewport: I, translateExtent: $, minZoom: z, maxZoom: C, onSelectionContextMenu: f, preventScrolling: A, noDragClassName: $e, noWheelClassName: st, noPanClassName: Ye, disableKeyboardA11y: Ue, onViewportChange: Ne, isControlledViewport: !!We, children: o.jsxs(L0, { children: [o.jsx(M0, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: a, onReconnect: me, onReconnectStart: _e, onReconnectEnd: De, onlyRenderVisibleElements: _, onEdgeContextMenu: ne, onEdgeMouseEnter: fe, onEdgeMouseMove: O, onEdgeMouseLeave: te, reconnectRadius: ge, defaultMarkerColor: k, noPanClassName: Ye, disableKeyboardA11y: Ue, rfId: He }), o.jsx(K0, { style: w, type: m, component: v, containerStyle: x }), o.jsx("div", { className: "react-flow__edgelabel-renderer" }), o.jsx(y0, { nodeTypes: e, onNodeClick: i, onNodeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, nodeClickDistance: ee, onlyRenderVisibleElements: _, noPanClassName: Ye, noDragClassName: $e, disableKeyboardA11y: Ue, nodeExtent: Pe, rfId: He }), o.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
_d.displayName = "GraphView";
const Y0 = ke(_d), U0 = Au(), Ec = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, width: r, height: s, fitView: a, fitViewOptions: c, minZoom: u = 0.5, maxZoom: l = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: p = "basic" } = {}) => {
  const h = /* @__PURE__ */ new Map(), m = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), v = /* @__PURE__ */ new Map(), x = i ?? t ?? [], b = n ?? e ?? [], g = d ?? [0, 0], y = f ?? Bn;
  Wu(w, v, x);
  const { nodesInitialized: N } = Wo(b, h, m, {
    nodeOrigin: g,
    nodeExtent: y,
    zIndexMode: p
  });
  let j = [0, 0, 1];
  if (a && r && s) {
    const S = ni(h, {
      filter: (I) => !!((I.width || I.initialWidth) && (I.height || I.initialHeight))
    }), { x: E, y: _, zoom: M } = Ss(S, r, s, u, l, c?.padding ?? 0.1);
    j = [E, _, M];
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
    edges: x,
    edgeLookup: v,
    connectionLookup: w,
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
    connection: { ...Nu },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: U0,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: ju,
    zIndexMode: p,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, Z0 = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, width: r, height: s, fitView: a, fitViewOptions: c, minZoom: u, maxZoom: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p }) => lv((h, m) => {
  async function w() {
    const { nodeLookup: v, panZoom: x, fitViewOptions: b, fitViewResolver: g, width: y, height: N, minZoom: j, maxZoom: S } = m();
    x && (await ow({
      nodes: v,
      width: y,
      height: N,
      panZoom: x,
      minZoom: j,
      maxZoom: S
    }, b), g?.resolve(!0), h({ fitViewResolver: null }));
  }
  return {
    ...Ec({
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
    setNodes: (v) => {
      const { nodeLookup: x, parentLookup: b, nodeOrigin: g, elevateNodesOnSelect: y, fitViewQueued: N, zIndexMode: j, nodesSelectionActive: S } = m(), { nodesInitialized: E, hasSelectedNodes: _ } = Wo(v, x, b, {
        nodeOrigin: g,
        nodeExtent: f,
        elevateNodesOnSelect: y,
        checkEquality: !0,
        zIndexMode: j
      }), M = S && _;
      N && E ? (w(), h({
        nodes: v,
        nodesInitialized: E,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: M
      })) : h({ nodes: v, nodesInitialized: E, nodesSelectionActive: M });
    },
    setEdges: (v) => {
      const { connectionLookup: x, edgeLookup: b } = m();
      Wu(x, b, v), h({ edges: v });
    },
    setDefaultNodesAndEdges: (v, x) => {
      if (v) {
        const { setNodes: b } = m();
        b(v), h({ hasDefaultNodes: !0 });
      }
      if (x) {
        const { setEdges: b } = m();
        b(x), h({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (v) => {
      const { triggerNodeChanges: x, nodeLookup: b, parentLookup: g, domNode: y, nodeOrigin: N, nodeExtent: j, debug: S, fitViewQueued: E, zIndexMode: _ } = m(), { changes: M, updatedInternals: I } = Iw(v, b, g, y, N, j, _);
      I && (Sw(b, g, { nodeOrigin: N, nodeExtent: j, zIndexMode: _ }), E ? (w(), h({ fitViewQueued: !1, fitViewOptions: void 0 })) : h({}), M?.length > 0 && (S && console.log("React Flow: trigger node changes", M), x?.(M)));
    },
    updateNodePositions: (v, x = !1) => {
      const b = [];
      let g = [];
      const { nodeLookup: y, triggerNodeChanges: N, connection: j, updateConnection: S, onNodesChangeMiddlewareMap: E } = m();
      for (const [_, M] of v) {
        const I = y.get(_), $ = !!(I?.expandParent && I?.parentId && M?.position), z = {
          id: _,
          type: "position",
          position: $ ? {
            x: Math.max(0, M.position.x),
            y: Math.max(0, M.position.y)
          } : M.position,
          dragging: x
        };
        if (I && j.inProgress && j.fromNode.id === I.id) {
          const C = Pt(I, j.fromHandle, oe.Left, !0);
          S({ ...j, from: C });
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
        const { parentLookup: _, nodeOrigin: M } = m(), I = _s(b, y, _, M);
        g.push(...I);
      }
      for (const _ of E.values())
        g = _(g);
      N(g);
    },
    triggerNodeChanges: (v) => {
      const { onNodesChange: x, setNodes: b, nodes: g, hasDefaultNodes: y, debug: N } = m();
      if (v?.length) {
        if (y) {
          const j = id(v, g);
          b(j);
        }
        N && console.log("React Flow: trigger node changes", v), x?.(v);
      }
    },
    triggerEdgeChanges: (v) => {
      const { onEdgesChange: x, setEdges: b, edges: g, hasDefaultEdges: y, debug: N } = m();
      if (v?.length) {
        if (y) {
          const j = rd(v, g);
          b(j);
        }
        N && console.log("React Flow: trigger edge changes", v), x?.(v);
      }
    },
    addSelectedNodes: (v) => {
      const { multiSelectionActive: x, edgeLookup: b, nodeLookup: g, triggerNodeChanges: y, triggerEdgeChanges: N } = m();
      if (x) {
        const j = v.map((S) => Nt(S, !0));
        y(j);
        return;
      }
      y(qt(g, /* @__PURE__ */ new Set([...v]), !0)), N(qt(b));
    },
    addSelectedEdges: (v) => {
      const { multiSelectionActive: x, edgeLookup: b, nodeLookup: g, triggerNodeChanges: y, triggerEdgeChanges: N } = m();
      if (x) {
        const j = v.map((S) => Nt(S, !0));
        N(j);
        return;
      }
      N(qt(b, /* @__PURE__ */ new Set([...v]))), y(qt(g, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: v, edges: x } = {}) => {
      const { edges: b, nodes: g, nodeLookup: y, triggerNodeChanges: N, triggerEdgeChanges: j } = m(), S = v || g, E = x || b, _ = [];
      for (const I of S) {
        if (!I.selected)
          continue;
        const $ = y.get(I.id);
        $ && ($.selected = !1), _.push(Nt(I.id, !1));
      }
      const M = [];
      for (const I of E)
        I.selected && M.push(Nt(I.id, !1));
      N(_), j(M);
    },
    setMinZoom: (v) => {
      const { panZoom: x, maxZoom: b } = m();
      x?.setScaleExtent([v, b]), h({ minZoom: v });
    },
    setMaxZoom: (v) => {
      const { panZoom: x, minZoom: b } = m();
      x?.setScaleExtent([b, v]), h({ maxZoom: v });
    },
    setTranslateExtent: (v) => {
      m().panZoom?.setTranslateExtent(v), h({ translateExtent: v });
    },
    resetSelectedElements: () => {
      const { edges: v, nodes: x, triggerNodeChanges: b, triggerEdgeChanges: g, elementsSelectable: y } = m();
      if (!y)
        return;
      const N = x.reduce((S, E) => E.selected ? [...S, Nt(E.id, !1)] : S, []), j = v.reduce((S, E) => E.selected ? [...S, Nt(E.id, !1)] : S, []);
      b(N), g(j);
    },
    setNodeExtent: (v) => {
      const { nodes: x, nodeLookup: b, parentLookup: g, nodeOrigin: y, elevateNodesOnSelect: N, nodeExtent: j, zIndexMode: S } = m();
      v[0][0] === j[0][0] && v[0][1] === j[0][1] && v[1][0] === j[1][0] && v[1][1] === j[1][1] || (Wo(x, b, g, {
        nodeOrigin: y,
        nodeExtent: v,
        elevateNodesOnSelect: N,
        checkEquality: !1,
        zIndexMode: S
      }), h({ nodeExtent: v }));
    },
    panBy: (v) => {
      const { transform: x, width: b, height: g, panZoom: y, translateExtent: N } = m();
      return Aw({ delta: v, panZoom: y, transform: x, translateExtent: N, width: b, height: g });
    },
    setCenter: async (v, x, b) => {
      const { width: g, height: y, maxZoom: N, panZoom: j } = m();
      if (!j)
        return !1;
      const S = typeof b?.zoom < "u" ? b.zoom : N;
      return await j.setViewport({
        x: g / 2 - v * S,
        y: y / 2 - x * S,
        zoom: S
      }, { duration: b?.duration, ease: b?.ease, interpolate: b?.interpolate }), !0;
    },
    cancelConnection: () => {
      h({
        connection: { ...Nu }
      });
    },
    updateConnection: (v) => {
      h({ connection: v });
    },
    reset: () => h({ ...Ec() })
  };
}, Object.is);
function G0({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: i, initialWidth: r, initialHeight: s, initialMinZoom: a, initialMaxZoom: c, initialFitViewOptions: u, fitView: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p, children: h }) {
  const [m] = K(() => Z0({
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
  return o.jsx(pv, { value: m, children: o.jsx(Lv, { children: h }) });
}
function J0({ children: e, nodes: t, edges: n, defaultNodes: i, defaultEdges: r, width: s, height: a, fitView: c, fitViewOptions: u, minZoom: l, maxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h }) {
  return Un(wr) ? o.jsx(o.Fragment, { children: e }) : o.jsx(G0, { initialNodes: t, initialEdges: n, defaultNodes: i, defaultEdges: r, initialWidth: s, initialHeight: a, fitView: c, initialFitViewOptions: u, initialMinZoom: l, initialMaxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h, children: e });
}
const Q0 = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function eb({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, className: r, nodeTypes: s, edgeTypes: a, onNodeClick: c, onEdgeClick: u, onInit: l, onMove: d, onMoveStart: f, onMoveEnd: p, onConnect: h, onConnectStart: m, onConnectEnd: w, onClickConnectStart: v, onClickConnectEnd: x, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: y, onNodeContextMenu: N, onNodeDoubleClick: j, onNodeDragStart: S, onNodeDrag: E, onNodeDragStop: _, onNodesDelete: M, onEdgesDelete: I, onDelete: $, onSelectionChange: z, onSelectionDragStart: C, onSelectionDrag: A, onSelectionDragStop: k, onSelectionContextMenu: D, onSelectionStart: P, onSelectionEnd: T, onBeforeDelete: F, connectionMode: W, connectionLineType: H = mt.Bezier, connectionLineStyle: Y, connectionLineComponent: V, connectionLineContainerStyle: q, deleteKeyCode: J = "Backspace", selectionKeyCode: Z = "Shift", selectionOnDrag: R = !1, selectionMode: X = Kn.Full, panActivationKeyCode: ue = "Space", multiSelectionKeyCode: ce = qn() ? "Meta" : "Control", zoomActivationKeyCode: ee = qn() ? "Meta" : "Control", snapToGrid: ne, snapGrid: fe, onlyRenderVisibleElements: O = !1, selectNodesOnDrag: te, nodesDraggable: ge, autoPanOnNodeFocus: me, nodesConnectable: _e, nodesFocusable: De, nodeOrigin: $e = td, edgesFocusable: st, edgesReconnectable: Ye, elementsSelectable: Ue = !0, defaultViewport: Pe = kv, minZoom: He = 0.5, maxZoom: We = 2, translateExtent: Ne = Bn, preventScrolling: zt = !0, nodeExtent: Ie, defaultMarkerColor: gn = "#b1b1b7", zoomOnScroll: L = !0, zoomOnPinch: B = !0, panOnScroll: U = !1, panOnScrollSpeed: ie = 0.5, panOnScrollMode: de = kt.Free, zoomOnDoubleClick: pe = !0, panOnDrag: xe = !0, onPaneClick: Ae, onPaneMouseEnter: Se, onPaneMouseMove: ze, onPaneMouseLeave: we, onPaneScroll: Vt, onPaneContextMenu: Sr, paneClickDistance: si = 1, nodeClickDistance: Cr = 0, children: kr, onReconnect: pt, onReconnectStart: Er, onReconnectEnd: Ot, onEdgeContextMenu: Ir, onEdgeDoubleClick: Ht, onEdgeMouseEnter: Ar, onEdgeMouseMove: ai, onEdgeMouseLeave: ci, reconnectRadius: _r = 10, onNodesChange: Wt, onEdgesChange: Dr, noDragClassName: Tr = "nodrag", noWheelClassName: $r = "nowheel", noPanClassName: Ft = "nopan", fitView: li, fitViewOptions: ui, connectOnClick: Pr, attributionPosition: Mr, proOptions: Rr, defaultEdgeOptions: Lr, elevateNodesOnSelect: zr = !0, elevateEdgesOnSelect: Vr = !1, disableKeyboardA11y: di = !1, autoPanOnConnect: Or, autoPanOnNodeDrag: Hr, autoPanOnSelection: Wr = !0, autoPanSpeed: Fr, connectionRadius: Br, isValidConnection: fi, onError: pi, style: hi, id: mn, nodeDragThreshold: Kr, connectionDragThreshold: Xr, viewport: qr, onViewportChange: Yr, width: Ur, height: Zr, colorMode: Gr = "light", debug: Jr, onScroll: gi, ariaLabelConfig: Qr, zIndexMode: mi = "basic", ...eo }, to) {
  const jt = mn || "1", no = _v(Gr), io = se((yi) => {
    yi.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), gi?.(yi);
  }, [gi]);
  return o.jsx("div", { "data-testid": "rf__wrapper", ...eo, onScroll: io, style: { ...hi, ...Q0 }, ref: to, className: Ee(["react-flow", r, no]), id: mn, role: "application", children: o.jsxs(J0, { nodes: e, edges: t, width: Ur, height: Zr, fitView: li, fitViewOptions: ui, minZoom: He, maxZoom: We, nodeOrigin: $e, nodeExtent: Ie, zIndexMode: mi, children: [o.jsx(Av, { nodes: e, edges: t, defaultNodes: n, defaultEdges: i, onConnect: h, onConnectStart: m, onConnectEnd: w, onClickConnectStart: v, onClickConnectEnd: x, nodesDraggable: ge, autoPanOnNodeFocus: me, nodesConnectable: _e, nodesFocusable: De, edgesFocusable: st, edgesReconnectable: Ye, elementsSelectable: Ue, elevateNodesOnSelect: zr, elevateEdgesOnSelect: Vr, minZoom: He, maxZoom: We, nodeExtent: Ie, onNodesChange: Wt, onEdgesChange: Dr, snapToGrid: ne, snapGrid: fe, connectionMode: W, translateExtent: Ne, connectOnClick: Pr, defaultEdgeOptions: Lr, fitView: li, fitViewOptions: ui, onNodesDelete: M, onEdgesDelete: I, onDelete: $, onNodeDragStart: S, onNodeDrag: E, onNodeDragStop: _, onSelectionDrag: A, onSelectionDragStart: C, onSelectionDragStop: k, onMove: d, onMoveStart: f, onMoveEnd: p, noPanClassName: Ft, nodeOrigin: $e, rfId: jt, autoPanOnConnect: Or, autoPanOnNodeDrag: Hr, autoPanSpeed: Fr, onError: pi, connectionRadius: Br, isValidConnection: fi, selectNodesOnDrag: te, nodeDragThreshold: Kr, connectionDragThreshold: Xr, onBeforeDelete: F, debug: Jr, ariaLabelConfig: Qr, zIndexMode: mi }), o.jsx(Y0, { onInit: l, onNodeClick: c, onEdgeClick: u, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: y, onNodeContextMenu: N, onNodeDoubleClick: j, nodeTypes: s, edgeTypes: a, connectionLineType: H, connectionLineStyle: Y, connectionLineComponent: V, connectionLineContainerStyle: q, selectionKeyCode: Z, selectionOnDrag: R, selectionMode: X, deleteKeyCode: J, multiSelectionKeyCode: ce, panActivationKeyCode: ue, zoomActivationKeyCode: ee, onlyRenderVisibleElements: O, defaultViewport: Pe, translateExtent: Ne, minZoom: He, maxZoom: We, preventScrolling: zt, zoomOnScroll: L, zoomOnPinch: B, zoomOnDoubleClick: pe, panOnScroll: U, panOnScrollSpeed: ie, panOnScrollMode: de, panOnDrag: xe, autoPanOnSelection: Wr, onPaneClick: Ae, onPaneMouseEnter: Se, onPaneMouseMove: ze, onPaneMouseLeave: we, onPaneScroll: Vt, onPaneContextMenu: Sr, paneClickDistance: si, nodeClickDistance: Cr, onSelectionContextMenu: D, onSelectionStart: P, onSelectionEnd: T, onReconnect: pt, onReconnectStart: Er, onReconnectEnd: Ot, onEdgeContextMenu: Ir, onEdgeDoubleClick: Ht, onEdgeMouseEnter: Ar, onEdgeMouseMove: ai, onEdgeMouseLeave: ci, reconnectRadius: _r, defaultMarkerColor: gn, noDragClassName: Tr, noWheelClassName: $r, noPanClassName: Ft, rfId: jt, disableKeyboardA11y: di, nodeExtent: Ie, viewport: qr, onViewportChange: Yr }), o.jsx(Cv, { onSelectionChange: z }), kr, o.jsx(vv, { proOptions: Rr, position: Mr }), o.jsx(wv, { rfId: jt, disableKeyboardA11y: di })] }) });
}
var Dd = ad(eb);
const tb = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function nb({ children: e }) {
  const t = he(tb);
  return t ? fv.createPortal(e, t) : null;
}
function ib({ dimensions: e, lineWidth: t, variant: n, className: i }) {
  return o.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ee(["react-flow__background-pattern", n, i]) });
}
function rb({ radius: e, className: t }) {
  return o.jsx("circle", { cx: e, cy: e, r: e, className: Ee(["react-flow__background-pattern", "dots", t]) });
}
var wt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(wt || (wt = {}));
const ob = {
  [wt.Dots]: 1,
  [wt.Lines]: 1,
  [wt.Cross]: 6
}, sb = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function Td({
  id: e,
  variant: t = wt.Dots,
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
  const f = re(null), { transform: p, patternId: h } = he(sb, ve), m = i || ob[t], w = t === wt.Dots, v = t === wt.Cross, x = Array.isArray(n) ? n : [n, n], b = [x[0] * p[2] || 1, x[1] * p[2] || 1], g = m * p[2], y = Array.isArray(s) ? s : [s, s], N = v ? [g, g] : b, j = [
    y[0] * p[2] || 1 + N[0] / 2,
    y[1] * p[2] || 1 + N[1] / 2
  ], S = `${h}${e || ""}`;
  return o.jsxs("svg", { className: Ee(["react-flow__background", l]), style: {
    ...u,
    ...br,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": a
  }, ref: f, "data-testid": "rf__background", children: [o.jsx("pattern", { id: S, x: p[0] % b[0], y: p[1] % b[1], width: b[0], height: b[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${j[0]},-${j[1]})`, children: w ? o.jsx(rb, { radius: g / 2, className: d }) : o.jsx(ib, { dimensions: N, lineWidth: r, variant: t, className: d }) }), o.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${S})` })] });
}
Td.displayName = "Background";
const $d = ke(Td);
function ab() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: o.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function cb() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: o.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function lb() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: o.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function ub() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: o.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function db() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: o.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function Ai({ children: e, className: t, ...n }) {
  return o.jsx("button", { type: "button", className: Ee(["react-flow__controls-button", t]), ...n, children: e });
}
const fb = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function Pd({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: i = !0, fitViewOptions: r, onZoomIn: s, onZoomOut: a, onFitView: c, onInteractiveChange: u, className: l, children: d, position: f = "bottom-left", orientation: p = "vertical", "aria-label": h }) {
  const m = je(), { isInteractive: w, minZoomReached: v, maxZoomReached: x, ariaLabelConfig: b } = he(fb, ve), { zoomIn: g, zoomOut: y, fitView: N } = Ds(), j = () => {
    g(), s?.();
  }, S = () => {
    y(), a?.();
  }, E = () => {
    N(r), c?.();
  }, _ = () => {
    m.setState({
      nodesDraggable: !w,
      nodesConnectable: !w,
      elementsSelectable: !w
    }), u?.(!w);
  }, M = p === "horizontal" ? "horizontal" : "vertical";
  return o.jsxs(vr, { className: Ee(["react-flow__controls", M, l]), position: f, style: e, "data-testid": "rf__controls", "aria-label": h ?? b["controls.ariaLabel"], children: [t && o.jsxs(o.Fragment, { children: [o.jsx(Ai, { onClick: j, className: "react-flow__controls-zoomin", title: b["controls.zoomIn.ariaLabel"], "aria-label": b["controls.zoomIn.ariaLabel"], disabled: x, children: o.jsx(ab, {}) }), o.jsx(Ai, { onClick: S, className: "react-flow__controls-zoomout", title: b["controls.zoomOut.ariaLabel"], "aria-label": b["controls.zoomOut.ariaLabel"], disabled: v, children: o.jsx(cb, {}) })] }), n && o.jsx(Ai, { className: "react-flow__controls-fitview", onClick: E, title: b["controls.fitView.ariaLabel"], "aria-label": b["controls.fitView.ariaLabel"], children: o.jsx(lb, {}) }), i && o.jsx(Ai, { className: "react-flow__controls-interactive", onClick: _, title: b["controls.interactive.ariaLabel"], "aria-label": b["controls.interactive.ariaLabel"], children: w ? o.jsx(db, {}) : o.jsx(ub, {}) }), d] });
}
Pd.displayName = "Controls";
const Md = ke(Pd);
function pb({ id: e, x: t, y: n, width: i, height: r, style: s, color: a, strokeColor: c, strokeWidth: u, className: l, borderRadius: d, shapeRendering: f, selected: p, onClick: h }) {
  const { background: m, backgroundColor: w } = s || {}, v = a || m || w;
  return o.jsx("rect", { className: Ee(["react-flow__minimap-node", { selected: p }, l]), x: t, y: n, rx: d, ry: d, width: i, height: r, style: {
    fill: v,
    stroke: c,
    strokeWidth: u
  }, shapeRendering: f, onClick: h ? (x) => h(x, e) : void 0 });
}
const hb = ke(pb), gb = (e) => e.nodes.map((t) => t.id), Co = (e) => e instanceof Function ? e : () => e;
function mb({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: i = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: s = hb,
  onClick: a
}) {
  const c = he(gb, ve), u = Co(t), l = Co(e), d = Co(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return o.jsx(o.Fragment, { children: c.map((p) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    o.jsx(xb, { id: p, nodeColorFunc: u, nodeStrokeColorFunc: l, nodeClassNameFunc: d, nodeBorderRadius: i, nodeStrokeWidth: r, NodeComponent: s, onClick: a, shapeRendering: f }, p)
  )) });
}
function yb({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: i, nodeBorderRadius: r, nodeStrokeWidth: s, shapeRendering: a, NodeComponent: c, onClick: u }) {
  const { node: l, x: d, y: f, width: p, height: h } = he((m) => {
    const w = m.nodeLookup.get(e);
    if (!w)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const v = w.internals.userNode, { x, y: b } = w.internals.positionAbsolute, { width: g, height: y } = ft(v);
    return {
      node: v,
      x,
      y: b,
      width: g,
      height: y
    };
  }, ve);
  return !l || l.hidden || !_u(l) ? null : o.jsx(c, { x: d, y: f, width: p, height: h, style: l.style, selected: !!l.selected, className: i(l), color: t(l), borderRadius: r, strokeColor: n(l), strokeWidth: s, shapeRendering: a, onClick: u, id: l.id });
}
const xb = ke(yb);
var wb = ke(mb);
const vb = 200, bb = 150, jb = (e) => !e.hidden, Nb = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? Iu(ni(e.nodeLookup, { filter: jb }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, Sb = "react-flow__minimap-desc";
function Rd({
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
  pannable: w = !1,
  zoomable: v = !1,
  ariaLabel: x,
  inversePan: b,
  zoomStep: g = 1,
  offsetScale: y = 5
}) {
  const N = je(), j = re(null), { boundingRect: S, viewBB: E, rfId: _, panZoom: M, translateExtent: I, flowWidth: $, flowHeight: z, ariaLabelConfig: C } = he(Nb, ve), A = e?.width ?? vb, k = e?.height ?? bb, D = S.width / A, P = S.height / k, T = Math.max(D, P), F = T * A, W = T * k, H = y * T, Y = S.x - (F - S.width) / 2 - H, V = S.y - (W - S.height) / 2 - H, q = F + H * 2, J = W + H * 2, Z = `${Sb}-${_}`, R = re(0), X = re();
  R.current = T, Q(() => {
    if (j.current && M)
      return X.current = zw({
        domNode: j.current,
        panZoom: M,
        getTransform: () => N.getState().transform,
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
      zoomable: v
    });
  }, [w, v, b, g, I, $, z]);
  const ue = h ? (ne) => {
    const [fe, O] = X.current?.pointer(ne) || [0, 0];
    h(ne, { x: fe, y: O });
  } : void 0, ce = m ? se((ne, fe) => {
    const O = N.getState().nodeLookup.get(fe).internals.userNode;
    m(ne, O);
  }, []) : void 0, ee = x ?? C["minimap.ariaLabel"];
  return o.jsx(vr, { position: p, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * T : void 0,
    "--xy-minimap-node-background-color-props": typeof i == "string" ? i : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof a == "number" ? a : void 0
  }, className: Ee(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: o.jsxs("svg", { width: A, height: k, viewBox: `${Y} ${V} ${q} ${J}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": Z, ref: j, onClick: ue, children: [ee && o.jsx("title", { id: Z, children: ee }), o.jsx(wb, { onClick: ce, nodeColor: i, nodeStrokeColor: n, nodeBorderRadius: s, nodeClassName: r, nodeStrokeWidth: a, nodeComponent: c }), o.jsx("path", { className: "react-flow__minimap-mask", d: `M${Y - H},${V - H}h${q + H * 2}v${J + H * 2}h${-q - H * 2}z
        M${E.x},${E.y}h${E.width}v${E.height}h${-E.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
Rd.displayName = "MiniMap";
const Ld = ke(Rd), Cb = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, kb = {
  [an.Line]: "right",
  [an.Handle]: "bottom-right"
};
function Eb({ nodeId: e, position: t, variant: n = an.Handle, className: i, style: r = void 0, children: s, color: a, minWidth: c = 10, minHeight: u = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: p, autoScale: h = !0, shouldResize: m, onResizeStart: w, onResize: v, onResizeEnd: x }) {
  const b = dd(), g = typeof e == "string" ? e : b, y = je(), N = re(null), j = n === an.Handle, S = he(se(Cb(j && h), [j, h]), ve), E = re(null), _ = t ?? kb[n];
  Q(() => {
    if (!(!N.current || !g))
      return E.current || (E.current = Gw({
        domNode: N.current,
        nodeId: g,
        getStoreItems: () => {
          const { nodeLookup: I, transform: $, snapGrid: z, snapToGrid: C, nodeOrigin: A, domNode: k } = y.getState();
          return {
            nodeLookup: I,
            transform: $,
            snapGrid: z,
            snapToGrid: C,
            nodeOrigin: A,
            paneDomNode: k
          };
        },
        onChange: (I, $) => {
          const { triggerNodeChanges: z, nodeLookup: C, parentLookup: A, nodeOrigin: k } = y.getState(), D = [], P = { x: I.x, y: I.y }, T = C.get(g);
          if (T && T.expandParent && T.parentId) {
            const F = T.origin ?? k, W = I.width ?? T.measured.width ?? 0, H = I.height ?? T.measured.height ?? 0, Y = {
              id: T.id,
              parentId: T.parentId,
              rect: {
                width: W,
                height: H,
                ...Du({
                  x: I.x ?? T.position.x,
                  y: I.y ?? T.position.y
                }, { width: W, height: H }, T.parentId, C, F)
              }
            }, V = _s([Y], C, A, k);
            D.push(...V), P.x = I.x ? Math.max(F[0] * W, I.x) : void 0, P.y = I.y ? Math.max(F[1] * H, I.y) : void 0;
          }
          if (P.x !== void 0 && P.y !== void 0) {
            const F = {
              id: g,
              type: "position",
              position: { ...P }
            };
            D.push(F);
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
          for (const F of $) {
            const W = {
              ...F,
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
          y.getState().triggerNodeChanges([z]);
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
        onResizeStart: w,
        onResize: v,
        onResizeEnd: x,
        shouldResize: m
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
    w,
    v,
    x,
    m
  ]);
  const M = _.split("-");
  return o.jsx("div", { className: Ee(["react-flow__resize-control", "nodrag", ...M, n, i]), ref: N, style: {
    ...r,
    scale: S,
    ...a && { [j ? "backgroundColor" : "borderColor"]: a }
  }, children: s });
}
ke(Eb);
function Ib(e, t) {
  return {
    ...t ? { name: t } : {},
    definitionId: e.definitionId,
    state: Gn(e.state),
    layout: e.layout
  };
}
function Ab(e) {
  return JSON.stringify(
    {
      state: Gn(e.state),
      layout: e.layout
    },
    null,
    2
  );
}
function _b(e, t) {
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
      state: ar(i.state),
      layout: i.layout ?? t.layout
    }
  };
}
function Db(e, t) {
  const n = (t ?? "workflow").trim().replace(/[^\w.-]+/g, "-") || "workflow", i = new Blob([JSON.stringify(e, null, 2)], { type: "application/json" }), r = URL.createObjectURL(i), s = document.createElement("a");
  s.href = r, s.download = `${n}.json`, document.body.appendChild(s), s.click(), s.remove(), URL.revokeObjectURL(r);
}
function Ic({
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
function Tb({
  document: e,
  diagnostics: t = [],
  readOnly: n = !1,
  theme: i = "studio",
  minHeight: r = "220px",
  ariaLabel: s,
  languageAdapter: a,
  onChange: c
}) {
  const u = t.filter((p) => !p.uri || p.uri === e.uri), l = a?.displayName ?? e.language, d = a?.loadEditor, f = le(
    () => d ? Nf(d) : null,
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
        f ? /* @__PURE__ */ o.jsx(Sf, { fallback: /* @__PURE__ */ o.jsx(
          Ic,
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
          Ic,
          {
            document: e,
            readOnly: n,
            minHeight: r,
            ariaLabel: s,
            onChange: c
          }
        ),
        /* @__PURE__ */ o.jsx($b, { diagnostics: u })
      ]
    }
  );
}
function $b({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ o.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const i = t.severity ?? "info", r = Pb(t);
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
function Pb(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const Mb = { language: "json", displayName: "JSON" };
function Rb({ draft: e, onApply: t }) {
  const n = le(() => Ab(e), [e]), [i, r] = K(n), [s, a] = K(n), [c, u] = K(null);
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
      Tb,
      {
        ariaLabel: "Workflow JSON",
        document: { uri: "elsa://workflows/definition.json", language: "json", value: i },
        languageAdapter: Mb,
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
const Lb = ["Single", "Array", "List", "HashSet"];
function zd(e) {
  const [t, n] = K(null), [i, r] = K(null);
  Q(() => {
    let u = !1;
    return Ih(e).then(
      (l) => {
        u || n(l);
      },
      () => {
        u || n([]);
      }
    ), _h(e).then(
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
  const s = le(
    () => t && t.length > 0 ? t.map((u) => {
      const l = Js(u);
      return {
        value: l,
        label: fl(u.displayName, l),
        group: u.category?.trim() || "Other"
      };
    }) : null,
    [t]
  ), a = le(
    () => i && i.length > 0 ? i.filter((u) => !u.deprecated).map((u) => ({
      value: u.typeName,
      label: ip(u.displayName, u.typeName)
    })) : null,
    [i]
  ), c = le(() => {
    const u = /* @__PURE__ */ new Map();
    for (const l of t ?? []) {
      const d = Js(l), f = l.defaultEditor?.trim();
      d && u.set(d, f && f.length > 0 ? f : "text");
    }
    return (l) => u.get(l) ?? "text";
  }, [t]);
  return { typeOptions: s, storageOptions: a, editorForAlias: c };
}
function zb(e) {
  return !e || e.length === 0 ? void 0 : (e.find((n) => /(^|\.)String$/i.test(n.value) || n.label.toLowerCase() === "string") ?? e[0]).value;
}
function Vb(e, t, n) {
  return {
    add: () => {
      const i = qf(n.namePrefix, e.map((r) => ct(r, n.nameKeys)));
      t([...e, n.create(i)]);
    },
    update: (i, r) => t(e.map((s, a) => a === i ? n.patch(s, r) : s)),
    remove: (i) => t(e.filter((r, s) => s !== i))
  };
}
function Ac({ value: e, options: t, placeholder: n, allowEmpty: i, ariaLabel: r, onChange: s }) {
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
const Ob = {
  Single: "Single",
  Array: "Array",
  List: "List",
  HashSet: "Hash set"
};
function Hb({ value: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ o.jsx("select", { "aria-label": t, value: e, onChange: (i) => n(i.target.value), children: Lb.map((i) => /* @__PURE__ */ o.jsx("option", { value: i, children: Ob[i] }, i)) });
}
function Wb(e, t) {
  return t === "" ? !0 : e === "checkbox" ? t === "true" || t === "false" || t === "True" || t === "False" : e === "number" ? Number.isFinite(Number(t.trim())) && t.trim() !== "" : e === "date" ? /^\d{4}-\d{2}-\d{2}$/.test(t) : !0;
}
function Fb({ value: e, editor: t, ariaLabel: n, onChange: i }) {
  const r = Wb(t, e);
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
function Bb({ title: e, addLabel: t, emptyLabel: n, headers: i, isEmpty: r, onAdd: s, children: a }) {
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
function Kb({ label: e, onRemove: t }) {
  return /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-properties-remove", "aria-label": e, title: e, onClick: t, children: /* @__PURE__ */ o.jsx($n, { size: 14 }) }) });
}
function Xb({ checked: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ o.jsx("input", { type: "checkbox", "aria-label": t, checked: e, onChange: (i) => n(i.target.checked) });
}
function $s({
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
  const { add: m, update: w, remove: v } = Vb(e, h, {
    namePrefix: r,
    nameKeys: s,
    create: (g) => l(g, zb(t)),
    patch: d
  }), x = ["Name", "Type", "Collection", ...f.default ? ["Default"] : [], ...f.storage ? ["Storage"] : [], ...f.required ? ["Required"] : []], b = r.toLowerCase();
  return /* @__PURE__ */ o.jsx(
    Bb,
    {
      title: a,
      addLabel: c,
      emptyLabel: u,
      headers: x,
      isEmpty: e.length === 0,
      onAdd: m,
      children: e.map((g, y) => {
        const N = ct(g, s), j = ss(g), S = ct(g, pl), E = S ? p?.get(S) : void 0, _ = j.collectionKind === "Single" ? i(j.alias) : "text";
        return /* @__PURE__ */ o.jsxs("tr", { children: [
          /* @__PURE__ */ o.jsxs("td", { children: [
            /* @__PURE__ */ o.jsx("input", { type: "text", "aria-label": `${r} name`, value: N, onChange: (M) => w(y, { name: M.target.value }) }),
            E ? /* @__PURE__ */ o.jsx("span", { className: "wf-properties-warning", role: "note", title: E, children: E }) : null
          ] }),
          /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            Ac,
            {
              ariaLabel: `${r} type`,
              value: j.alias,
              options: t,
              placeholder: "Type",
              onChange: (M) => w(y, { type: { alias: M, collectionKind: j.collectionKind } })
            }
          ) }),
          /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            Hb,
            {
              ariaLabel: `${r} collection kind`,
              value: j.collectionKind,
              onChange: (M) => w(y, { type: { alias: j.alias, collectionKind: M } })
            }
          ) }),
          f.default ? /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            Fb,
            {
              ariaLabel: `${r} default value`,
              value: Gf(g.default),
              editor: _,
              onChange: (M) => w(y, { default: Zf(M) })
            }
          ) }) : null,
          f.storage ? /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            Ac,
            {
              ariaLabel: `${r} storage driver`,
              value: ct(g, op),
              options: n,
              placeholder: "—",
              allowEmpty: !0,
              onChange: (M) => w(y, { storageDriverType: M || null })
            }
          ) }) : null,
          f.required ? /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            Xb,
            {
              ariaLabel: `${r} required`,
              checked: g.isRequired === !0,
              onChange: (M) => w(y, { isRequired: M })
            }
          ) }) : null,
          /* @__PURE__ */ o.jsx(Kb, { label: `Remove ${b} ${N || y + 1}`, onRemove: () => v(y) })
        ] }, y);
      })
    }
  );
}
function Vd({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, title: r = "Variables", addLabel: s = "Add variable", emptyLabel: a = "No variables defined.", warnings: c, onChange: u }) {
  return /* @__PURE__ */ o.jsx(
    $s,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Variable",
      nameKeys: rp,
      title: r,
      addLabel: s,
      emptyLabel: a,
      create: (l, d) => Yf({ name: l, alias: d }),
      patch: (l, d) => Uf(l, d),
      columns: { default: !0, storage: !0 },
      warnings: c,
      onChange: u
    }
  );
}
function qb({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, onChange: r }) {
  return /* @__PURE__ */ o.jsx(
    $s,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Input",
      nameKeys: as,
      title: "Inputs",
      addLabel: "Add input",
      emptyLabel: "No inputs defined.",
      create: (s, a) => Jf({ name: s, alias: a }),
      patch: (s, a) => Qf(s, a),
      columns: { default: !1, storage: !0, required: !0 },
      onChange: r
    }
  );
}
function Yb({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, onChange: r }) {
  return /* @__PURE__ */ o.jsx(
    $s,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Output",
      nameKeys: as,
      title: "Outputs",
      addLabel: "Add output",
      emptyLabel: "No outputs defined.",
      create: (s, a) => ep({ name: s, alias: a }),
      patch: (s, a) => tp(s, a),
      columns: { default: !1, storage: !1 },
      onChange: r
    }
  );
}
function Oi(e) {
  return (e ?? []).filter(Rn);
}
function Ub({ context: e, variables: t, title: n, addLabel: i, emptyLabel: r, warnings: s, onChange: a }) {
  const { typeOptions: c, storageOptions: u, editorForAlias: l } = zd(e);
  return /* @__PURE__ */ o.jsx(
    Vd,
    {
      items: Oi(t),
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
function Zb({ definition: e, definitionId: t, onMetaChange: n }) {
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
function Gb({ details: e, draft: t, context: n, onStateChange: i, onDefinitionMetaChange: r }) {
  const { typeOptions: s, storageOptions: a, editorForAlias: c } = zd(n), u = Oi(t.state.variables), l = Oi(t.state.inputs), d = Oi(t.state.outputs), f = e?.versions ?? [];
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-properties-view", children: [
    /* @__PURE__ */ o.jsx(
      Zb,
      {
        definition: e?.definition,
        definitionId: t.definitionId,
        onMetaChange: r
      }
    ),
    /* @__PURE__ */ o.jsx(
      Vd,
      {
        items: u,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, variables: p }))
      }
    ),
    /* @__PURE__ */ o.jsx(
      qb,
      {
        items: l,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, inputs: p }))
      }
    ),
    /* @__PURE__ */ o.jsx(
      Yb,
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
const _c = "application/x-elsa-activity-version-id", Jb = 6, Qb = 1200, ej = 250, tj = [10, 25, 50], nj = 10, Dc = "elsa-studio-workflow-palette-width", Tc = "elsa-studio-workflow-inspector-width", $c = "elsa-studio-workflow-palette-collapsed", Pc = "elsa-studio-workflow-inspector-collapsed", Od = "elsa-studio-workflow-side-panel-maximized", kn = 180, En = 460, ij = 260, Yt = 260, Ut = 560, rj = 320, Mc = 42, _i = 16, Hd = Ke.createContext(null), Wd = Ke.createContext(null), Fd = Ke.createContext(null);
function oj(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function Bd(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Mt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function Rt(e, t, n) {
  const i = t.createPrompt(n);
  return i ? (e.dispatchPrompt(i), !0) : !1;
}
function sj(e) {
  if (!e) return null;
  const t = [], n = e.match(/```(?:json)?\s*([\s\S]*?)```/i);
  n && t.push(n[1]);
  const i = e.match(/\{[\s\S]*?\}/);
  i && t.push(i[0]);
  for (const a of t)
    try {
      const c = JSON.parse(a.trim()), u = Rc(c.name, c.description);
      if (u) return u;
    } catch {
    }
  const r = e.match(/name\s*[:-]\s*(.+)/i)?.[1], s = e.match(/description\s*[:-]\s*(.+)/i)?.[1];
  return Rc(r, s);
}
function Rc(e, t) {
  const n = typeof e == "string" ? Lc(e) : void 0, i = typeof t == "string" ? Lc(t) : void 0;
  return n || i ? { name: n || void 0, description: i || void 0 } : null;
}
function Lc(e) {
  return e.trim().replace(/,$/, "").trim().replace(/^["']/, "").replace(/["']$/, "").trim();
}
function aj(e, t) {
  return e.rootActivityVersionId ?? Kd(t, e.rootKind)?.activityVersionId ?? null;
}
function Kd(e, t) {
  return e.find((n) => cj(n) === t);
}
function cj(e) {
  return e ? lj(e) ? "flowchart" : uj(e) ? "sequence" : null : null;
}
function Ko(e) {
  return Pl(e, (t) => t.category).map((t) => ({
    category: t.category,
    activities: t.items.sort((n, i) => Ce(n).localeCompare(Ce(i)))
  }));
}
function lj(e) {
  return Ce(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function uj(e) {
  return Ce(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function dj(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function Xd(e) {
  return mj(e.rootActivityType) || e.rootActivityType;
}
function fj(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function pj(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function hj(e, t) {
  return zc(t) - zc(e);
}
function zc(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function qd(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
function Yd(e) {
  const t = e?.workflowExecutionId ?? e?.runId ?? e?.executionId;
  return typeof t == "string" && t.trim() ? t : null;
}
async function gj(e) {
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
function mj(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function yj(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    Di(t, n.typeName, n), Di(t, n.name, n), Di(t, n.displayName, n);
    const i = n.typeName.split(".").filter(Boolean).at(-1);
    Di(t, i, n);
  }
  return t;
}
function xj(e, t, n) {
  const i = t.get(e.activityVersionId);
  return n.get(In(i?.activityTypeKey)) ?? n.get(In(At(i?.activityTypeKey))) ?? n.get(In(i?.displayName)) ?? n.get(In(e.activityVersionId)) ?? null;
}
function Di(e, t, n) {
  const i = In(t);
  i && !e.has(i) && e.set(i, n);
}
function In(e) {
  return e?.trim().toLowerCase() ?? "";
}
function Vc(e, t, n, i) {
  const r = jr();
  if (!r) return t;
  const s = r.getItem(e);
  if (s == null) return t;
  const a = Number(s);
  return Number.isFinite(a) ? Hi(a, n, i) : t;
}
function Oc(e, t) {
  const n = jr();
  if (!n) return t;
  const i = n.getItem(e);
  return i === "true" ? !0 : i === "false" ? !1 : t;
}
function wj() {
  const e = jr();
  if (!e) return null;
  const t = e.getItem(Od);
  return t === "palette" || t === "inspector" ? t : null;
}
function jr() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function vn(e, t) {
  const n = jr();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function Hi(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function Ud(e) {
  switch (vj(e)) {
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
function vj(e) {
  return (e ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}
function bj(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function Xo(e) {
  return `${Ce(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function Hc(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function jj(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function Zd(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function Nj(e) {
  const t = Zd(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function Sj(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function Be(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function Cj(e) {
  return Qd(Be(e));
}
function kj(e, t) {
  const n = t?.get(e.activityVersionId);
  return n ? Ce(n) : At(e.activityVersionId) ?? e.activityVersionId;
}
function Ej(e, t) {
  if (e.cardinality === "single") {
    const i = e.activities[0];
    return i ? kj(i, t) : "Empty — click to choose";
  }
  const n = e.activities.length;
  return `${n} activit${n === 1 ? "y" : "ies"}`;
}
function Gd(e, t, n = []) {
  if (!e) return n;
  const i = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: i?.activityTypeKey ?? e.activityVersionId,
    displayName: i ? Ce(i) : void 0
  });
  for (const r of Le(e, t))
    for (const s of r.activities) Gd(s, t, n);
  return n;
}
function Jd(e, t, n = []) {
  if (!e) return n;
  for (const i of kl(e))
    n.push({ source: i.source, target: i.target, sourcePort: i.sourceHandle ?? void 0, targetPort: i.targetHandle ?? void 0 });
  for (const i of Le(e, t))
    for (const r of i.activities) Jd(r, t, n);
  return n;
}
function An(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function Ij(e) {
  return `${e.id}-${Qd(JSON.stringify(e.state))}`;
}
function Qd(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function Ps(e) {
  return e.status.toLowerCase() === "rejected";
}
function Aj(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function _j(e, t) {
  const n = e instanceof Error ? e.message : String(e);
  return Dj(e, n) ? `Run ${t} was not found.` : n;
}
function Dj(e, t) {
  if ((typeof e == "object" && e ? e.response?.status ?? e.status : void 0) === 404 || /\b404\b/.test(t)) return !0;
  try {
    const i = JSON.parse(t);
    return [i.error, i.title, i.detail].some((r) => typeof r == "string" && /not found/i.test(r));
  } catch {
    return /not found/i.test(t);
  }
}
function hn({ status: e, subStatus: t }) {
  return /* @__PURE__ */ o.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
const ef = { workflowActivity: Tj }, tf = { workflow: Pj };
function Tj({ id: e, data: t, selected: n }) {
  const i = t, r = i.runtime, s = !i.suppressFlowPorts, a = s ? i.sourcePorts.length > 0 ? i.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], c = $j(i), l = Ke.useContext(Wd)?.({ activityVersionId: i.activityVersionId, activityTypeKey: i.activityTypeKey }) ?? null, d = Ke.useContext(Fd), f = i.onEnterSlot ?? (d ? (p) => d(e, i.label, p) : void 0);
  return /* @__PURE__ */ o.jsxs(
    "div",
    {
      className: ["wf-node", n ? "selected" : "", r ? "wf-node-runtime" : "", r?.hasBlockingIncident ? "faulted" : "", l ? "wf-node-unavailable" : ""].filter(Boolean).join(" "),
      "data-icon": i.icon ?? "activity",
      children: [
        s && i.acceptsInbound ? /* @__PURE__ */ o.jsx(cn, { type: "target", position: oe.Left }) : null,
        l ? /* @__PURE__ */ o.jsx("span", { className: "wf-node-availability", title: `No longer available for new use · ${zn(l.state)}`, children: /* @__PURE__ */ o.jsx(Tn, { size: 13 }) }) : null,
        /* @__PURE__ */ o.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ o.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: ur(i.icon) }),
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
          r.status ? /* @__PURE__ */ o.jsx(hn, { status: r.status, subStatus: r.subStatus }) : null,
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
          return /* @__PURE__ */ o.jsxs(Ke.Fragment, { children: [
            /* @__PURE__ */ o.jsx("span", { className: "wf-node-port-label", style: { top: m }, children: p.displayName }),
            /* @__PURE__ */ o.jsx(cn, { type: "source", position: oe.Right, id: p.name, style: { top: m } })
          ] }, p.name);
        })
      ]
    }
  );
}
function $j(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((r) => !!r).join(" · ");
}
function Pj(e) {
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
  } = e, p = Ke.useContext(Hd), [h, m] = K(!1), [w, v, x] = nr({ sourceX: n, sourceY: i, targetX: r, targetY: s, sourcePosition: a, targetPosition: c }), b = p?.highlightedEdgeId === t;
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsx(
      ri,
      {
        id: t,
        path: w,
        markerEnd: u,
        style: {
          ...l,
          strokeWidth: b ? 2.5 : l?.strokeWidth
        },
        label: d,
        labelX: v,
        labelY: x,
        labelStyle: f,
        onMouseEnter: () => m(!0),
        onMouseLeave: () => m(!1)
      }
    ),
    p ? /* @__PURE__ */ o.jsx(nb, { children: /* @__PURE__ */ o.jsxs(
      "div",
      {
        className: ["wf-edge-actions", h ? "visible" : "", b ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${v}px, ${x}px)` },
        onMouseEnter: () => m(!0),
        onMouseLeave: () => m(!1),
        children: [
          /* @__PURE__ */ o.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (g) => p.requestInsertActivity(t, g.clientX, g.clientY), children: /* @__PURE__ */ o.jsx(Qt, { size: 12 }) }),
          /* @__PURE__ */ o.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => p.deleteEdge(t), children: /* @__PURE__ */ o.jsx($n, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function nf({ clientX: e, clientY: t, activities: n, onPick: i, onClose: r }) {
  const [s, a] = K(""), [c, u] = K(0), l = re(null), d = re(null), f = le(() => {
    const b = s.trim().toLowerCase(), g = n.filter(dj);
    return b ? g.filter((y) => Ce(y).toLowerCase().includes(b) || y.activityTypeKey.toLowerCase().includes(b) || (y.category ?? "").toLowerCase().includes(b) || (y.description ?? "").toLowerCase().includes(b)) : g;
  }, [n, s]), p = le(() => Ko(f), [f]), h = le(() => p.flatMap((b) => b.activities), [p]);
  Q(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), Q(() => {
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
  }, w = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), v = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let x = -1;
  return /* @__PURE__ */ o.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: w, top: v }, onMouseDown: (b) => b.stopPropagation(), onClick: (b) => b.stopPropagation(), children: [
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
        x += 1;
        const y = x, N = y === c;
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
              /* @__PURE__ */ o.jsx("strong", { children: Ce(g) }),
              /* @__PURE__ */ o.jsx("small", { children: g.category || g.activityTypeKey })
            ]
          },
          g.activityVersionId
        );
      })
    ] }, b.category)) })
  ] });
}
function Wi({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: i
}) {
  const r = Vf(t.map((s) => s.id), n, i);
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
function Wc(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
const Mj = "Expressions/UnresolvedVariable";
function Rj(e) {
  return String(e.type ?? e.code ?? "");
}
function Lj(e) {
  return Rj(e) === Mj;
}
function zj(e) {
  const t = String(e ?? "").trim().split("/").filter(Boolean), [n, i, ...r] = t, s = n === "$workflow";
  return {
    nodeId: !n || s ? null : n,
    isWorkflowScope: s,
    bag: i === "inputs" || i === "outputs" || i === "variables" ? i : null,
    referenceKey: r.length > 0 ? r.join("/") : null
  };
}
function Vj(e) {
  return (e ?? []).filter(Lj).map((t) => ({
    error: t,
    path: zj(t.path),
    message: t.message ?? "Variable reference is not visible from this activity's scope."
  }));
}
function Oj({ draft: e, onRepair: t }) {
  const n = e.validationErrors;
  if (!n.length)
    return /* @__PURE__ */ o.jsxs("div", { className: "wf-validation ok", children: [
      /* @__PURE__ */ o.jsx(ln, { size: 14 }),
      " No validation errors"
    ] });
  const i = Vj(n), r = new Map(i.map((s) => [s.error, s]));
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-validation-summary", children: [
      /* @__PURE__ */ o.jsx(bt, { size: 14 }),
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
          /* @__PURE__ */ o.jsx(_f, { size: 12 }),
          " Repair"
        ] }) : null
      ] }, a);
    }) })
  ] });
}
function Hj({
  testRun: e,
  onOpenDetails: t
}) {
  const n = Ps(e);
  return /* @__PURE__ */ o.jsx("div", { className: "wf-test-run-status", "data-state": n ? "rejected" : "accepted", children: /* @__PURE__ */ o.jsxs(
    "button",
    {
      type: "button",
      className: "wf-test-run-trigger",
      onClick: t,
      children: [
        n ? /* @__PURE__ */ o.jsx(bt, { size: 16 }) : /* @__PURE__ */ o.jsx(ln, { size: 16 }),
        n ? "Test run rejected" : "Test run dispatched"
      ]
    }
  ) });
}
function Wj({ testRun: e, onOpenRun: t }) {
  if (!e)
    return /* @__PURE__ */ o.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Run the draft to see Runtime Evidence." }) });
  const n = Ps(e), i = e.workflowExecutionId;
  return /* @__PURE__ */ o.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ o.jsxs("section", { className: "wf-runtime-card", "data-state": n ? "rejected" : "accepted", children: [
    /* @__PURE__ */ o.jsxs("header", { children: [
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("span", { children: "Latest Test Run" }),
        /* @__PURE__ */ o.jsx("h3", { children: n ? "Rejected by the server" : "Transient run accepted" })
      ] }),
      /* @__PURE__ */ o.jsx(hn, { status: e.status, subStatus: e.commandDispatchStatus ?? void 0 })
    ] }),
    /* @__PURE__ */ o.jsx("p", { children: "Ephemeral - not saved, promoted, or published." }),
    n && e.reason ? /* @__PURE__ */ o.jsxs("div", { className: "wf-runtime-reason", children: [
      /* @__PURE__ */ o.jsx(bt, { size: 14 }),
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
        /* @__PURE__ */ o.jsx("dd", { children: Fc(e.activityCount, "activity") })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ o.jsx("dd", { children: Fc(e.incidentCount, "incident") })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ o.jsx("dd", { title: e.expiresAt ? Re(e.expiresAt) : "None", children: e.expiresAt ? Re(e.expiresAt) : "None" })
      ] })
    ] })
  ] }) });
}
function Fc(e, t) {
  return typeof e != "number" ? "Available on linked Run" : `${e} ${t}${e === 1 ? "" : "s"}`;
}
function Ms({ rows: e = 5 }) {
  return /* @__PURE__ */ o.jsx("div", { className: "wf-grid", "aria-busy": "true", "aria-label": "Loading", children: Array.from({ length: e }).map((t, n) => /* @__PURE__ */ o.jsx("div", { className: "wf-skeleton wf-skeleton-row", style: { width: `${90 - n % 3 * 12}%` } }, n)) });
}
function Rs({ icon: e, title: t, description: n, action: i }) {
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-empty-state", role: "status", children: [
    /* @__PURE__ */ o.jsx("div", { className: "wf-empty-state-icon", "aria-hidden": !0, children: e ?? /* @__PURE__ */ o.jsx(Zn, { size: 22 }) }),
    /* @__PURE__ */ o.jsx("h3", { children: t }),
    n ? /* @__PURE__ */ o.jsx("p", { children: n }) : null,
    i ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty-state-action", children: i }) : null
  ] });
}
function oi({ message: e, title: t = "Something went wrong" }) {
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-error-card", role: "alert", children: [
    /* @__PURE__ */ o.jsx(bt, { size: 18 }),
    /* @__PURE__ */ o.jsxs("div", { className: "wf-error-card-body", children: [
      /* @__PURE__ */ o.jsx("strong", { children: t }),
      /* @__PURE__ */ o.jsx("span", { children: e || "Please try again, or check that the Elsa server is reachable." })
    ] })
  ] });
}
function rf({ status: e, run: t, compact: n = !1 }) {
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
      await gj(e), i(n);
    } catch {
      r(n);
    }
  };
  return /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (a) => {
    s(a);
  }, children: /* @__PURE__ */ o.jsx(Df, { size: 12 }) });
}
function Fj({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: i }) {
  const [r, s] = K("loading"), [a, c] = K(""), [u, l] = K(""), [d, f] = K(null), [p, h] = K([]), m = n?.trim().toLowerCase() ?? "", w = le(
    () => m ? p.filter((S) => fj(S, m)) : p,
    [m, p]
  ), v = le(
    () => Array.from(new Set(p.flatMap((S) => [
      S.definitionId,
      S.definitionVersionId,
      S.sourceId
    ]).filter((S) => !!S))).sort((S, E) => S.localeCompare(E)),
    [p]
  ), x = Mt(t, "weaver.workflows.explain-executable"), b = se(async () => {
    s("loading"), c("");
    try {
      h(await Tl(e)), s("ready");
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
      const E = await Dl(e, S.artifactId), _ = Yd(E);
      f({ artifactId: S.artifactId, workflowExecutionId: _ }), l(`Started ${S.artifactId}`);
    } catch (E) {
      c(E instanceof Error ? E.message : String(E));
    }
  }, y = (S) => {
    x && Rt(t, x, S) && (c(""), f(null), l(`Sent ${S.artifactId} to Weaver`));
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
        /* @__PURE__ */ o.jsx(rr, { size: 14 }),
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
      /* @__PURE__ */ o.jsx("datalist", { id: "wf-executable-definition-options", children: v.map((S) => /* @__PURE__ */ o.jsx("option", { value: S }, S)) }),
      n ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => i(null), children: [
        /* @__PURE__ */ o.jsx(is, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    r === "failed" ? /* @__PURE__ */ o.jsx(oi, { message: a }) : null,
    u ? /* @__PURE__ */ o.jsx(rf, { status: u, run: d }) : null,
    r === "loading" ? /* @__PURE__ */ o.jsx(Ms, {}) : null,
    r === "ready" && w.length === 0 ? /* @__PURE__ */ o.jsx(
      Rs,
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
            /* @__PURE__ */ o.jsx(Et, { value: S.artifactId, ariaLabel: `Copy artifact ID ${S.artifactId}`, copiedLabel: "artifact ID", onCopied: N, onCopyFailed: j })
          ] }),
          /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
            /* @__PURE__ */ o.jsx("small", { title: S.artifactHash, children: S.artifactHash }),
            /* @__PURE__ */ o.jsx(Et, { value: S.artifactHash, ariaLabel: `Copy artifact hash ${S.artifactHash}`, copiedLabel: "artifact hash", onCopied: N, onCopyFailed: j })
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line wf-version-cell", children: [
          /* @__PURE__ */ o.jsx("span", { children: S.artifactVersion }),
          /* @__PURE__ */ o.jsx(Et, { value: S.artifactVersion, ariaLabel: `Copy artifact version ${S.artifactVersion}`, copiedLabel: "artifact version", onCopied: N, onCopyFailed: j })
        ] }),
        /* @__PURE__ */ o.jsx(Bj, { executable: S, onCopied: N, onCopyFailed: j }),
        /* @__PURE__ */ o.jsx("span", { children: Xd(S) }),
        /* @__PURE__ */ o.jsx("span", { children: Re(S.publishedAt ?? S.createdAt) }),
        /* @__PURE__ */ o.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
            g(S);
          }, children: [
            /* @__PURE__ */ o.jsx(Jt, { size: 13 }),
            " Run"
          ] }),
          x ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => y(S), children: [
            /* @__PURE__ */ o.jsx(ut, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, S.artifactId))
    ] }) : null
  ] });
}
function Bj({ executable: e, onCopied: t, onCopyFailed: n }) {
  const i = e.sourceId || e.definitionVersionId || e.definitionId, r = e.sourceVersion;
  return /* @__PURE__ */ o.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ o.jsx("span", { className: "wf-source-kind", children: qd(e.sourceKind) }),
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
function Kj({ context: e, ai: t, definitionId: n, publishedArtifactId: i }) {
  const [r, s] = K("loading"), [a, c] = K(""), [u, l] = K(""), [d, f] = K(null), [p, h] = K([]), m = Mt(t, "weaver.workflows.explain-executable"), w = se(async () => {
    s("loading"), c("");
    try {
      const N = await Tl(e);
      h(N.filter((j) => pj(j, n)).sort(hj)), s("ready");
    } catch (N) {
      c(N instanceof Error ? N.message : String(N)), h([]), s("failed");
    }
  }, [e, n]);
  Q(() => {
    w();
  }, [w, i]);
  const v = async (N) => {
    l(""), f(null), c("");
    try {
      const j = await Dl(e, N.artifactId);
      f({ artifactId: N.artifactId, workflowExecutionId: Yd(j) }), l(`Started ${N.artifactId}`);
    } catch (j) {
      c(j instanceof Error ? j.message : String(j));
    }
  }, x = (N) => {
    m && Rt(t, m, N) && (c(""), f(null), l(`Sent ${N.artifactId} to Weaver`));
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
        w();
      }, children: [
        /* @__PURE__ */ o.jsx(rs, { size: 13 }),
        " Refresh"
      ] }),
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: b, children: "Open list" })
    ] }),
    r === "failed" ? /* @__PURE__ */ o.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ o.jsx(bt, { size: 14 }),
      " ",
      a
    ] }) : null,
    u ? /* @__PURE__ */ o.jsx(rf, { status: u, run: d, compact: !0 }) : null,
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
          /* @__PURE__ */ o.jsx(Et, { value: N.artifactId, ariaLabel: `Copy artifact ID ${N.artifactId}`, copiedLabel: "artifact ID", onCopied: g, onCopyFailed: y })
        ] }),
        /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ o.jsx("code", { title: N.artifactHash, children: N.artifactHash }),
          /* @__PURE__ */ o.jsx(Et, { value: N.artifactHash, ariaLabel: `Copy artifact hash ${N.artifactHash}`, copiedLabel: "artifact hash", onCopied: g, onCopyFailed: y })
        ] })
      ] }),
      /* @__PURE__ */ o.jsxs("dl", { children: [
        /* @__PURE__ */ o.jsxs("div", { children: [
          /* @__PURE__ */ o.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ o.jsxs("dd", { children: [
            qd(N.sourceKind),
            " ",
            N.sourceVersion ? `v${N.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("div", { children: [
          /* @__PURE__ */ o.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ o.jsx("dd", { children: Xd(N) })
        ] })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
          v(N);
        }, children: [
          /* @__PURE__ */ o.jsx(Jt, { size: 13 }),
          " Run"
        ] }),
        m ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => x(N), children: [
          /* @__PURE__ */ o.jsx(ut, { size: 13 }),
          " Explain"
        ] }) : null
      ] })
    ] }, N.artifactId)) }) : null
  ] });
}
function of() {
  const [e, t] = K(() => Vc(Dc, ij, kn, En)), [n, i] = K(() => Vc(Tc, rj, Yt, Ut)), [r, s] = K(() => Oc($c, !1)), [a, c] = K(() => Oc(Pc, !1)), [u, l] = K(wj);
  Q(() => {
    vn(Dc, String(e));
  }, [e]), Q(() => {
    vn(Tc, String(n));
  }, [n]), Q(() => {
    vn($c, String(r));
  }, [r]), Q(() => {
    vn(Pc, String(a));
  }, [a]), Q(() => {
    vn(Od, u);
  }, [u]), Q(() => {
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
    l(null), g === "palette" ? (s(!1), t((N) => Hi(N + y, kn, En))) : (c(!1), i((N) => Hi(N + y, Yt, Ut)));
  }, []), h = se((g, y) => {
    y.preventDefault(), l(null), g === "palette" ? s(!1) : c(!1);
    const N = y.clientX, j = g === "palette" ? e : n, S = g === "palette" ? kn : Yt, E = g === "palette" ? En : Ut;
    document.body.classList.add("wf-side-panel-resizing");
    const _ = (I) => {
      const $ = g === "palette" ? I.clientX - N : N - I.clientX, z = Hi(j + $, S, E);
      g === "palette" ? t(z) : i(z);
    }, M = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", _), window.removeEventListener("pointerup", M), window.removeEventListener("pointercancel", M);
    };
    window.addEventListener("pointermove", _), window.addEventListener("pointerup", M), window.addEventListener("pointercancel", M);
  }, [n, e]), m = se((g, y) => {
    y.key === "ArrowLeft" ? (y.preventDefault(), p(g, g === "palette" ? -_i : _i)) : y.key === "ArrowRight" ? (y.preventDefault(), p(g, g === "palette" ? _i : -_i)) : y.key === "Home" ? (y.preventDefault(), g === "palette" ? t(kn) : i(Yt)) : y.key === "End" && (y.preventDefault(), g === "palette" ? t(En) : i(Ut));
  }, [p]), w = !r && u !== "inspector", v = !a && u !== "palette", x = [
    "wf-editor-body",
    r ? "palette-collapsed" : "",
    a ? "inspector-collapsed" : "",
    u === "palette" ? "palette-maximized" : "",
    u === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), b = {
    "--wf-palette-width": `${r ? Mc : e}px`,
    "--wf-inspector-width": `${a ? Mc : n}px`
  };
  return {
    paletteWidth: e,
    inspectorWidth: n,
    paletteCollapsed: r,
    inspectorCollapsed: a,
    maximizedSidePanel: u,
    setInspectorCollapsed: c,
    paletteExpanded: w,
    inspectorExpanded: v,
    editorBodyClassName: x,
    editorBodyStyle: b,
    toggleSidePanelCollapsed: d,
    toggleSidePanelMaximized: f,
    startSidePanelResize: h,
    handleSidePanelResizeKeyDown: m
  };
}
const Xj = 50;
function Bc() {
  return { past: [], future: [] };
}
function qj(e) {
  return e.past.length > 0;
}
function Yj(e) {
  return e.future.length > 0;
}
function Kc(e, t, n = Xj) {
  const i = [...e.past, t];
  return i.length > n && i.splice(0, i.length - n), { past: i, future: [] };
}
function Uj(e, t) {
  if (e.past.length === 0) return null;
  const n = e.past.slice(), i = n.pop();
  return { history: { past: n, future: [...e.future, t] }, snapshot: i };
}
function Zj(e, t) {
  if (e.future.length === 0) return null;
  const n = e.future.slice(), i = n.pop();
  return { history: { past: [...e.past, t], future: n }, snapshot: i };
}
function Gj({ draft: e, restoreDraft: t }) {
  const n = re(Bc()), i = re(null), r = re(""), s = re(!1), [a, c] = K(0), u = se((w) => {
    n.current = Bc(), i.current = w ? An(w) : null, r.current = w ? Be(w) : "", s.current = !1, c(0);
  }, []);
  Q(() => {
    if (!e) return;
    if (s.current) {
      s.current = !1;
      return;
    }
    const w = Be(e);
    if (w === r.current) return;
    const v = window.setTimeout(() => {
      const x = i.current;
      x && (n.current = Kc(n.current, x), c((b) => b + 1)), i.current = An(e), r.current = w;
    }, ej);
    return () => window.clearTimeout(v);
  }, [e]);
  const l = se(() => {
    if (!e) return;
    const w = Be(e);
    if (w === r.current) return;
    const v = i.current;
    v && (n.current = Kc(n.current, v)), i.current = An(e), r.current = w;
  }, [e]), d = se((w) => {
    s.current = !0, i.current = An(w), r.current = Be(w), t(w), c((v) => v + 1);
  }, [t]), f = se(() => {
    if (!e) return;
    l();
    const w = Uj(n.current, e);
    w && (n.current = w.history, d(w.snapshot));
  }, [e, l, d]), p = se(() => {
    if (!e) return;
    l();
    const w = Zj(n.current, e);
    w && (n.current = w.history, d(w.snapshot));
  }, [e, l, d]), { canUndoNow: h, canRedoNow: m } = le(() => {
    const w = !!e && !!i.current && Be(e) !== r.current;
    return {
      canUndoNow: qj(n.current) || w,
      canRedoNow: Yj(n.current) && !w
    };
  }, [e, a]);
  return { resetHistory: u, undo: f, redo: p, canUndoNow: h, canRedoNow: m };
}
const Jj = {
  draft: null,
  frames: [],
  selectedNodeId: null,
  testRun: null,
  publishedArtifactId: null
};
function Qj(e, t) {
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
function eN() {
  const [e, t] = Cf(Qj, Jj), n = le(() => ({
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
    enterSlot(i, r, s, a = null) {
      t({ type: "slotEntered", frame: { ownerNodeId: i, slotId: r, label: s }, selectedNodeId: a });
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
const tN = 320, nN = 140;
function iN(e, t, n) {
  return n === "sequence" ? rN(e) : oN(e, t);
}
function rN(e) {
  const t = /* @__PURE__ */ new Map();
  return e.forEach((n, i) => {
    t.set(n.id, { x: i * 280, y: 0 });
  }), t;
}
function oN(e, t) {
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
      n.set(p, { x: d * tN, y: h * nN });
    });
  return n;
}
function sN(e, t, n, i, r) {
  if (!e) return { kind: "becomeRoot" };
  const s = Ln(e, t, r);
  if (!s)
    return t.length > 0 ? { kind: "staleFrames" } : Le(n, i)[0] ? { kind: "wrapRoot" } : { kind: "leafError" };
  const a = s.slot.cardinality === "single" ? s.slot.activities.find((c) => c.nodeId !== n.nodeId) ?? null : null;
  return { kind: "addToSlot", slot: s.slot, replacedActivity: a };
}
const aN = "root";
function cN(e) {
  return e.length === 0 ? aN : e.map((t) => `${t.ownerNodeId}:${t.slotId}`).join("/");
}
function lN(e, t) {
  return e.length === t.length && e.every((n, i) => n.id === t[i]);
}
function uN({
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
  setError: w
}) {
  const [v, x] = K([]), [b, g] = K([]), [y, N] = K(null), [j, S] = K(null), [E, _] = K(null), M = re(null), I = re(null), $ = re(/* @__PURE__ */ new Map()), z = re(/* @__PURE__ */ new Set()), C = re(null), A = re([]), k = re(null), D = re(null), P = re(!1), T = le(() => cN(i), [i]);
  Q(() => () => {
    y && $.current.set(T, y.getViewport());
  }, [y, T]), Q(() => {
    if (C.current = T, !n) {
      A.current = [], x([]), g([]);
      return;
    }
    const L = a ? bl(n, r, e?.layout ?? []) : t ? vl(t, r, e?.layout ?? []) : { nodes: [], edges: [] };
    A.current = L.nodes.map((B) => B.id), x(L.nodes), g(L.edges);
  }, [r, e?.layout, a, t, n, T]), Q(() => {
    if (!y || C.current !== T || !lN(v, A.current)) return;
    C.current = null;
    const L = $.current.get(T), B = z.current.has(T);
    z.current.add(T), window.requestAnimationFrame(() => {
      L ? y.setViewport(L) : !B && v.length > 0 && y.fitView({ padding: 0.2 });
    });
  }, [v, y, T]);
  const F = se((L, B, U) => U ? [
    ...L.filter((ie) => ie.nodeId !== B),
    { nodeId: B, x: Math.round(U.x), y: Math.round(U.y) }
  ] : L, []), W = se((L, B) => {
    if (e?.state.rootActivity && a)
      return;
    const U = Fi(L, Xo(L)), ie = sN(e?.state.rootActivity, i, U, L, s);
    if (ie.kind === "becomeRoot") {
      f(
        ({ draft: de }) => de ? { ...de, state: { ...de.state, rootActivity: U } } : null,
        U.nodeId
      );
      return;
    }
    if (ie.kind === "leafError") {
      m(""), w("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
      return;
    }
    if (ie.kind === "staleFrames") {
      m(""), w("This slot could not be resolved — returning to the workflow root."), h();
      return;
    }
    if (ie.kind === "wrapRoot") {
      f(({ draft: de }) => {
        const pe = de?.state.rootActivity;
        return pe ? {
          ...de,
          layout: F(de.layout, pe.nodeId, B),
          state: { ...de.state, rootActivity: na(U, [], [pe], L) }
        } : null;
      }, e?.state.rootActivity?.nodeId ?? null), w(""), m(`Wrapped root in ${Ce(L)}`);
      return;
    }
    f(({ draft: de, frames: pe }) => {
      if (!de?.state.rootActivity) return null;
      const xe = Ln(de.state.rootActivity, pe, s);
      if (!xe) return null;
      const Ae = xe.slot.cardinality === "single" ? [U] : [...xe.slot.activities, U], Se = na(de.state.rootActivity, pe, Ae, s);
      return {
        ...de,
        layout: F(de.layout, U.nodeId, B),
        state: { ...de.state, rootActivity: Se }
      };
    }, U.nodeId), ie.replacedActivity && (w(""), m(`Replaced ${ie.slot.label} content`));
  }, [s, e?.state.rootActivity, i, a, f, h, F, w, m]), H = se((L, B) => {
    const U = Fi(L, Xo(L)), ie = {
      id: U.nodeId,
      type: "workflowActivity",
      position: B,
      selected: !0,
      data: {
        label: Ce(L),
        activityVersionId: L.activityVersionId,
        activityTypeKey: L.activityTypeKey,
        category: L.category,
        executionType: L.executionType,
        icon: fn(L),
        childSlots: Le(U, L),
        acceptsInbound: String(L.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: El(U, L)
      }
    };
    return { activityNode: U, node: ie };
  }, []), Y = se((L, B, U = []) => {
    a || d(({ draft: ie, frames: de }) => {
      if (!ie) return null;
      const pe = Ip(ie.layout, L), xe = ie.state.rootActivity;
      if (!xe) return { ...ie, layout: pe };
      const Ae = Ln(xe, de, s);
      if (!Ae) return { ...ie, layout: pe };
      const Se = kp(Ae, L, B, U), ze = Ae.slot.mode === "flowchart" ? Ep(Se, B) : Se;
      return {
        ...ie,
        layout: pe,
        state: {
          ...ie.state,
          rootActivity: Cp(xe, de, ze, s)
        }
      };
    });
  }, [s, a, d]), V = se((L, B) => {
    if (!M.current) return null;
    const U = M.current.getBoundingClientRect();
    return y ? y.screenToFlowPosition({ x: L, y: B }) : {
      x: L - U.left,
      y: B - U.top
    };
  }, [y]), q = se((L, B) => document.elementFromPoint(L, B)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), J = se((L, B, U) => {
    const ie = v.find((we) => we.id === B.source), de = v.find((we) => we.id === B.target), pe = ie && de ? jj(ie, de) : ie ? Hc(ie) : U, xe = H(L, pe), Se = [...v.map((we) => we.selected ? { ...we, selected: !1 } : we), xe.node], ze = Rp(b, B, xe.node.id);
    x(Se), g(ze), p(xe.node.id), Y(Se, ze, [xe.activityNode]);
  }, [Y, H, b, v, p]), Z = se((L, B, U) => {
    if (!u || !M.current) return !1;
    const ie = M.current.getBoundingClientRect();
    if (!(B >= ie.left && B <= ie.right && U >= ie.top && U <= ie.bottom)) return !1;
    const pe = V(B, U);
    if (!pe) return !1;
    if (c) {
      const xe = q(B, U), Ae = xe ? b.find((Se) => Se.id === xe) : void 0;
      if (Ae)
        return J(L, Ae, pe), !0;
    }
    return W(L, pe), !0;
  }, [W, u, b, q, c, J, V]);
  Q(() => {
    const L = (U) => {
      const ie = k.current;
      if (!ie) return;
      Math.hypot(U.clientX - ie.startX, U.clientY - ie.startY) >= Jb && (ie.dragging = !0);
    }, B = (U) => {
      const ie = k.current;
      if (k.current = null, !ie?.dragging || !M.current || D.current) return;
      const de = M.current.getBoundingClientRect();
      U.clientX >= de.left && U.clientX <= de.right && U.clientY >= de.top && U.clientY <= de.bottom && (P.current = !0, window.setTimeout(() => {
        P.current = !1;
      }, 0), Z(ie.activity, U.clientX, U.clientY));
    };
    return window.addEventListener("pointermove", L), window.addEventListener("pointerup", B), window.addEventListener("pointercancel", B), () => {
      window.removeEventListener("pointermove", L), window.removeEventListener("pointerup", B), window.removeEventListener("pointercancel", B);
    };
  }, [y, Z]);
  const R = (L, B) => {
    D.current = { activityVersionId: B.activityVersionId, handledDrop: !1 }, L.dataTransfer.setData(_c, B.activityVersionId), L.dataTransfer.setData("text/plain", B.activityVersionId), L.dataTransfer.effectAllowed = "copy";
  }, X = (L, B) => {
    const U = D.current;
    D.current = null, !U?.handledDrop && (L.clientX === 0 && L.clientY === 0 || Z(B, L.clientX, L.clientY) && (P.current = !0, window.setTimeout(() => {
      P.current = !1;
    }, 0)));
  }, ue = (L, B) => {
    L.button === 0 && (k.current = {
      activity: B,
      startX: L.clientX,
      startY: L.clientY,
      dragging: !1
    });
  }, ce = (L) => {
    P.current || u && W(L);
  }, ee = (L) => {
    if (!u) {
      L.dataTransfer.dropEffect = "none";
      return;
    }
    if (L.preventDefault(), L.dataTransfer.dropEffect = "copy", !c) return;
    const B = q(L.clientX, L.clientY);
    _(B);
  }, ne = (L) => {
    if (!M.current) return;
    const B = L.relatedTarget;
    B && M.current.contains(B) || _(null);
  }, fe = (L) => {
    L.preventDefault(), _(null);
    const B = L.dataTransfer.getData(_c) || L.dataTransfer.getData("text/plain");
    if (!B || (L.stopPropagation(), D.current?.activityVersionId === B && (D.current.handledDrop = !0), !u)) return;
    const U = s.get(B);
    U && Z(U, L.clientX, L.clientY);
  }, O = (L) => {
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
  }, te = (L) => {
    const B = a ? L.filter((U) => U.type === "select") : L;
    B.length !== 0 && x((U) => id(B, U));
  }, ge = (L) => {
    a || g((B) => rd(L, B));
  }, me = (L) => !L.source || !L.target || L.source === L.target || !c ? !1 : !L.targetHandle, _e = (L) => {
    if (!e?.state.rootActivity || !t || !c || !me(L)) return;
    const B = Bi(L.source, L.target, L.sourceHandle ?? "Done", L.targetHandle ?? void 0), U = sd(B, b);
    g(U), Y(v, U);
  }, De = () => {
    Y(v, b);
  }, $e = !a && v.length > 0, st = se(() => {
    if (a || v.length === 0) return;
    const L = t?.slot.mode === "sequence" ? "sequence" : "flowchart", B = iN(v, b, L), U = v.map((ie) => {
      const de = B.get(ie.id);
      return de ? { ...ie, position: de } : ie;
    });
    x(U), Y(U, b), window.requestAnimationFrame(() => y?.fitView({ padding: 0.2 })), m("Rearranged the canvas.");
  }, [b, v, t, a, Y, y, m]), Ye = (L, B) => {
    if (!B.nodeId || B.handleType === "target") {
      I.current = null;
      return;
    }
    I.current = {
      nodeId: B.nodeId,
      handleId: B.handleId ?? null
    };
  }, Ue = (L, B) => {
    const U = Sj(I.current, B);
    if (I.current = null, !U || !c || B.toNode || B.toHandle || Nj(L)) return;
    const ie = Zd(L);
    S({
      kind: "fromPort",
      sourceNodeId: U.nodeId,
      sourceHandleId: U.handleId,
      clientX: ie.x,
      clientY: ie.y
    });
  }, Pe = (L, B) => {
    if (!c || !me(B)) return;
    const U = $v(L, {
      ...B,
      sourceHandle: B.sourceHandle ?? "Done",
      // Connection.targetHandle is string | null; null and undefined are indistinguishable downstream
      // (every consumer checks truthiness), so keep the type-correct null.
      targetHandle: B.targetHandle ?? null
    }, b, { shouldReplaceId: !1 });
    g(U), Y(v, U);
  }, He = (L) => {
    if (a || L.length === 0) return;
    const B = new Set(L.map((de) => de.id)), U = v.filter((de) => !B.has(de.id)), ie = b.filter((de) => !B.has(de.source) && !B.has(de.target));
    x(U), g(ie), l && B.has(l) && p(null), Y(U, ie);
  }, We = (L) => {
    if (a || L.length === 0) return;
    const B = new Set(L.map((ie) => ie.id)), U = b.filter((ie) => !B.has(ie.id));
    g(U), Y(v, U);
  }, Ne = se((L) => {
    if (a) return;
    const B = b.filter((U) => U.id !== L);
    g(B), Y(v, B);
  }, [Y, b, a, v]), zt = se((L, B, U) => {
    c && S({ kind: "spliceEdge", edgeId: L, clientX: B, clientY: U });
  }, [c]), Ie = (L) => {
    const B = j;
    if (!B) return;
    S(null);
    const U = V(B.clientX, B.clientY) ?? { x: 0, y: 0 };
    if (B.kind === "fromEmpty") {
      const de = H(L, U), xe = [...v.map((Ae) => Ae.selected ? { ...Ae, selected: !1 } : Ae), de.node];
      x(xe), p(de.node.id), Y(xe, b, [de.activityNode]);
      return;
    }
    if (B.kind === "fromPort") {
      const de = v.find((we) => we.id === B.sourceNodeId), pe = de ? Hc(de) : U, xe = H(L, pe), Se = [...v.map((we) => we.selected ? { ...we, selected: !1 } : we), xe.node], ze = [...b, Bi(B.sourceNodeId, xe.node.id, B.sourceHandleId ?? "Done")];
      x(Se), g(ze), p(xe.node.id), Y(Se, ze, [xe.activityNode]);
      return;
    }
    const ie = b.find((de) => de.id === B.edgeId);
    ie && J(L, ie, U);
  }, gn = le(() => ({
    highlightedEdgeId: E,
    deleteEdge: Ne,
    requestInsertActivity: zt
  }), [Ne, E, zt]);
  return {
    nodes: v,
    edges: b,
    canvasRef: M,
    setReactFlowInstance: N,
    connectMenu: j,
    setConnectMenu: S,
    edgeActions: gn,
    onNodesChange: te,
    onEdgesChange: ge,
    onNodesDelete: He,
    onEdgesDelete: We,
    isValidConnection: me,
    onConnect: _e,
    onConnectStart: Ye,
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
    onPalettePointerDown: ue
  };
}
const Xc = "elsa-studio:apply-workflow-graph-operation-batch", qc = "elsa-studio:undo-workflow-graph-operation-batch", dN = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function fN(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const i = vN(e), r = af(i.state.rootActivity), s = /* @__PURE__ */ new Map(), a = /* @__PURE__ */ new Map(), c = [];
  for (const u of t.operations) {
    const l = wN(u.kind), d = u.parameters ?? {};
    if (l === "add-activity") {
      const f = Ve(d.activityId) ?? u.temporaryReferences?.[0], p = xN(f ?? Ve(d.displayName) ?? Ve(d.activityType) ?? "weaver-activity", r), h = pN(u, p, n);
      a.set(p, h), c.push(p), f && s.set(f, p), i.state.rootActivity && hN(i.state.rootActivity, h);
      const m = vt(d.position) ? qo(d.position, { x: 280, y: 160 }) : null;
      m && (i.layout = Yc(i.layout, p, m));
      continue;
    }
    if (l === "set-root") {
      const f = ko(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown root activity.");
      i.state.rootActivity = f;
      continue;
    }
    if (l === "set-designer-position") {
      const f = Lt(d.activityId, s);
      if (!f || !Ls(i.state.rootActivity, f)) throw new Error("Weaver batch referenced an unknown activity position.");
      i.layout = Yc(i.layout, f, qo(d, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const f = ko(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity property target.");
      yN(f, Ve(d.propertyName) ?? "Value", d.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const f = ko(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity update target.");
      const p = vt(d.patch) ? d.patch : d;
      Object.assign(f, p);
      continue;
    }
    if (l === "remove-activity") {
      const f = Lt(d.activityId, s);
      if (!f) throw new Error("Weaver batch referenced an unknown activity remove target.");
      i.state.rootActivity = sf(i.state.rootActivity, f), i.layout = i.layout.filter((p) => p.nodeId !== f);
      continue;
    }
    if (l === "connect-activities") {
      gN(i, d, s);
      continue;
    }
    if (l === "disconnect-activities") {
      mN(i, d, s);
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
function pN(e, t, n) {
  const i = e.parameters ?? {}, r = Ve(i.activityVersionId) ?? Ve(i.activityType) ?? "Elsa.Workflows.Activity", s = n.find((a) => a.activityVersionId === r || a.activityTypeKey === r || a.displayName === Ve(i.displayName));
  return s ? Fi(s, t) : {
    nodeId: t,
    // catalogItem is provably undefined here (the truthy branch returned above), so use the id directly.
    activityVersionId: r,
    inputs: [],
    outputs: [],
    ...Ve(i.displayName) ? { displayName: Ve(i.displayName) } : {},
    designer: { position: qo(i.position, { x: 280, y: 160 }) }
  };
}
function hN(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = zs(e);
  n && !n.some((i) => i.nodeId === t.nodeId) && n.push(t);
}
function gN(e, t, n) {
  const i = e.state.rootActivity;
  if (!i?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const r = Lt(t.sourceActivityId ?? t.sourceId ?? t.from, n), s = Lt(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!r || !s) throw new Error("Weaver batch connection is missing source or target activity.");
  const a = i.structure.payload, c = Array.isArray(a.connections) ? a.connections : [], u = Ve(t.connectionId) ?? `flow-${r}-${s}`;
  a.connections = [
    ...c.filter((l) => !vt(l) || l.id !== u),
    {
      id: u,
      source: { nodeId: r, port: Ve(t.outcome) ?? Ve(t.sourcePort) ?? "Done" },
      target: { nodeId: s }
    }
  ];
}
function mN(e, t, n) {
  const i = e.state.rootActivity, r = i?.structure?.payload.connections;
  if (!Array.isArray(r)) return;
  const s = Ve(t.connectionId), a = Lt(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = Lt(t.targetActivityId ?? t.targetId ?? t.to, n);
  i.structure.payload.connections = r.filter((u) => {
    if (!vt(u)) return !0;
    if (s && u.id === s) return !1;
    const l = vt(u.source) ? u.source.nodeId : void 0, d = vt(u.target) ? u.target.nodeId : void 0;
    return l !== a || d !== c;
  });
}
function yN(e, t, n) {
  const i = vt(n);
  e[Ol(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: i ? "Object" : "Literal", value: n }
  };
}
function ko(e, t, n, i) {
  const r = Lt(t, n);
  return r ? Ls(e.state.rootActivity, r) ?? i.get(r) ?? null : null;
}
function Lt(e, t) {
  const n = Ve(e);
  return n ? t.get(n) ?? n : null;
}
function Ls(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of cf(e)) {
    const i = Ls(n, t);
    if (i) return i;
  }
  return null;
}
function sf(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = zs(e);
  if (n) {
    const i = n.map((r) => sf(r, t)).filter((r) => !!r);
    n.splice(0, n.length, ...i);
  }
  return e;
}
function af(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of cf(e)) af(n, t);
  return t;
}
function cf(e) {
  return zs(e) ?? [];
}
function zs(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function Yc(e, t, n) {
  return [
    ...e.filter((i) => i.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function qo(e, t) {
  const n = vt(e) ? e : {}, i = Number(n.x), r = Number(n.y);
  return {
    x: Number.isFinite(i) ? Math.max(40, Math.round(i)) : t.x,
    y: Number.isFinite(r) ? Math.max(40, Math.round(r)) : t.y
  };
}
function xN(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let i = n, r = 2;
  for (; t.has(i); )
    i = `${n}-${r}`, r += 1;
  return t.add(i), i;
}
function wN(e) {
  return typeof e == "number" ? dN[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Ve(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function vN(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function vt(e) {
  return typeof e == "object" && e !== null;
}
function bN({
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
        const p = An(e), h = fN(e, d.batch, n), m = `weaver-batch-${Date.now()}`;
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
    return window.addEventListener(Xc, c), window.addEventListener(qc, u), () => {
      window.removeEventListener(Xc, c), window.removeEventListener(qc, u);
    };
  }, [n, t, e, i, r, s]);
}
function jN({ context: e, draft: t, autosaveEnabledByDefault: n = !0, editDraft: i, setStatus: r, setError: s }) {
  const [a, c] = K(n), u = re(""), l = re(0), d = re(Promise.resolve()), f = se((h) => {
    u.current = h ? Be(h) : "";
  }, []), p = se(async (h, m) => {
    const w = async () => {
      const x = ++l.current, b = Be(h);
      s("");
      try {
        const g = await hh(e, h), y = Be(g);
        return u.current = y, i(({ draft: N }) => !N || N.id !== g.id ? null : Be(N) === b ? g : { ...N, validationErrors: g.validationErrors }), x === l.current && r(m), g;
      } catch (g) {
        throw x === l.current && (r(""), s(g instanceof Error ? g.message : String(g))), g;
      }
    }, v = d.current.then(w, w);
    return d.current = v.catch(() => {
    }), v;
  }, [e, i, r, s]);
  return Q(() => {
    if (!a || !t || Be(t) === u.current) return;
    r("Autosaving...");
    const m = window.setTimeout(() => {
      p(t, "Autosaved").catch(() => {
      });
    }, Qb);
    return () => window.clearTimeout(m);
  }, [a, t, p, r]), { saveDraft: p, autosaveEnabled: a, setAutosaveEnabled: c, markSaved: f };
}
function NN({ context: e, definitionId: t, resetHistory: n, loadDraft: i, markSaved: r, setError: s }) {
  const [a, c] = K(null), [u, l] = K([]), [d, f] = K([]), [p, h] = K(null), [m, w] = K(Pi), [v, x] = K("loading"), b = se(async () => {
    s(""), x("loading");
    const [g, y, N, j, S] = await Promise.all([
      oh(e, t),
      cr(e),
      kh(e).then(
        (_) => ({ ok: !0, descriptors: _ }),
        () => ({ ok: !1, descriptors: [] })
      ),
      Eh(e).then(
        (_) => ({ ok: !0, descriptors: _ }),
        () => ({ ok: !1, descriptors: Pi })
      ),
      // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
      $l(e).then(
        (_) => _,
        () => null
      )
    ]), E = g.draft ?? null;
    c(g), r(E), n(E), i(E), l(y.activities ?? []), f(N.descriptors), h(S), w(j.descriptors.length > 0 ? j.descriptors : Pi), x(N.ok ? "ready" : "failed");
  }, [e, t, n, i, r, s]);
  return Q(() => {
    b().catch((g) => s(g instanceof Error ? g.message : String(g)));
  }, [b, s]), {
    details: a,
    setDetails: c,
    catalog: u,
    activityDescriptors: d,
    availabilityDiagnostics: p,
    expressionDescriptors: m,
    descriptorStatus: v,
    reload: b
  };
}
function SN({ context: e, details: t, setDetails: n, setStatus: i }) {
  const r = re(null), s = re(null), a = re({});
  Q(() => {
    r.current = t;
  }, [t]);
  const c = se(() => {
    s.current !== null && (window.clearTimeout(s.current), s.current = null);
    const l = a.current;
    a.current = {};
    const d = r.current?.definition;
    !d || l.name === void 0 && l.description === void 0 || ph(e, d.id, {
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
function CN({
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
    Db(Ib(t, b), b), d("Exported workflow as JSON.");
  }, [t, n, d]), w = se(async () => {
    if (!(!t || i)) {
      l("saving"), d("Saving...");
      try {
        await r(t, "Saved");
      } catch {
      } finally {
        l("idle");
      }
    }
  }, [t, i, r, l, d]), v = se(async () => {
    if (!(!t || i)) {
      l("promoting"), d("Saving...");
      try {
        await r(t, "Saved"), d("Promoting...");
        const b = await gh(e, t.id), g = await mh(e, b.versionId);
        u(g.artifactId), d(`Published ${g.artifactVersion}`), await s();
      } catch (b) {
        d(""), f(b instanceof Error ? b.message : String(b));
      } finally {
        l("idle");
      }
    }
  }, [t, i, e, r, s, u, l, d, f]), x = se(async () => {
    if (!t?.state.rootActivity || i) return;
    const b = t, g = Be(b);
    c(), d("Preparing test run...");
    try {
      l("testRunPreparing"), d("Preparing test run...");
      const y = Ij(b);
      l("testRunStarting"), d("Starting test run...");
      const N = await yh(e, {
        definitionId: b.definitionId,
        snapshotId: y,
        state: b.state
      });
      a({ draftSignature: g, view: N }), p("runtime"), h(!1), d(Ps(N) ? "Test run rejected" : "Test run dispatched");
    } catch (y) {
      d(""), f(y instanceof Error ? y.message : String(y));
    } finally {
      l("idle");
    }
  }, [t, i, e, c, a, p, h, l, d, f]);
  return { exportJson: m, save: w, promoteAndPublish: v, run: x };
}
function kN({
  context: e,
  draft: t,
  frames: n,
  selectedNodeId: i,
  catalog: r,
  activityDescriptors: s,
  availabilityDiagnostics: a
}) {
  const c = t?.state.rootActivity ?? null, u = le(() => new Map(r.map((S) => [S.activityVersionId, S])), [r]), l = se(
    (S) => Uh([S.activityVersionId, S.activityTypeKey], a),
    [a]
  ), d = le(() => yj(s), [s]), f = le(() => ml(c, n, u), [c, n, u]), p = jl(f, f ? u.get(f.activityVersionId) : void 0), h = !!f && p === "unsupported", m = le(() => h ? null : Ln(c, n, u), [c, n, u, h]), w = le(() => h && f?.nodeId === i ? f : m?.slot.activities.find((S) => S.nodeId === i) ?? null, [h, m, f, i]), v = le(
    () => w ? xj(w, u, d) : null,
    [u, d, w]
  ), x = le(
    () => w ? l({ activityVersionId: w.activityVersionId, activityTypeKey: u.get(w.activityVersionId)?.activityTypeKey }) : null,
    [l, u, w]
  ), b = w ? Le(w, u) : [], g = w ? Xp(w, u.get(w.activityVersionId)) : !1, y = ah(e, t?.state, i, u), N = !h && m?.slot.mode === "flowchart";
  return {
    catalogByVersion: u,
    availabilityLookup: l,
    scopeOwner: f,
    isUnsupportedDesigner: h,
    scope: m,
    selectedNode: w,
    selectedDescriptor: v,
    selectedNodeAvailability: x,
    selectedSlots: b,
    selectedSupportsScopedVariables: g,
    scopedVariableAnalysis: y,
    isFlowchartDesigner: N,
    canAddActivitiesToCanvas: !c || !h
  };
}
function EN({
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
        revision: Cj(t),
        selectedNodeId: i,
        selectedActivityType: r?.typeName ?? (n ? s.get(n.activityVersionId)?.activityTypeKey ?? n.activityVersionId : null),
        summary: e.definition.name,
        activities: Gd(t.state.rootActivity, s),
        connections: Jd(t.state.rootActivity, s),
        diagnostics: t.validationErrors.map((a) => ({ severity: a.code ?? "warning", message: a.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === e.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [s, e, t, r, n, i]);
}
function IN({
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
      /* @__PURE__ */ o.jsx(rr, { size: 14, "aria-hidden": "true" }),
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
              f ? /* @__PURE__ */ o.jsx(ol, { size: 14 }) : /* @__PURE__ */ o.jsx(yt, { size: 14 }),
              /* @__PURE__ */ o.jsx("span", { children: d.category }),
              /* @__PURE__ */ o.jsx("small", { children: d.activities.length })
            ]
          }
        ),
        f ? /* @__PURE__ */ o.jsx("div", { className: "wf-palette-activities", role: "group", children: d.activities.map((p) => {
          const h = p.description?.trim(), m = h ? `wf-palette-description-${p.activityVersionId}` : void 0, w = Ce(p), v = fn(p);
          return /* @__PURE__ */ o.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-activity",
              role: "treeitem",
              draggable: !0,
              title: h || Ce(p),
              "aria-describedby": m,
              onClick: () => s(p),
              onDragStart: (x) => a(x, p),
              onDragEnd: (x) => c(x, p),
              onPointerDown: (x) => u(x, p),
              children: [
                /* @__PURE__ */ o.jsx("span", { className: "wf-activity-icon", "data-icon": v, "aria-hidden": "true", children: ur(v) }),
                /* @__PURE__ */ o.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ o.jsx("strong", { children: w }),
                  h ? /* @__PURE__ */ o.jsx("small", { id: m, children: h }) : null
                ] }),
                /* @__PURE__ */ o.jsx(sl, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            p.activityVersionId
          );
        }) }) : null
      ] }, d.category);
    }) })
  ] });
}
const lf = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]), AN = "Variable";
function _N({
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
  const d = zN(l), f = r.length > 0 ? r : Zh;
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ o.jsx("span", { className: "wf-section-label", children: "Properties" }),
    d.map((p) => /* @__PURE__ */ o.jsxs("section", { className: "wf-property-group", children: [
      d.length > 1 ? /* @__PURE__ */ o.jsx("h4", { children: p.category }) : null,
      p.inputs.map((h) => /* @__PURE__ */ o.jsx(
        DN,
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
function DN({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: i,
  expressionDescriptors: r,
  visibleVariables: s,
  scopeStatus: a,
  onChange: c
}) {
  const u = t.isReadOnly === !0, l = { activity: e, expressionDescriptors: r, readOnly: u }, d = Go(n, t, l), f = d?.component, p = t.isWrapped !== !1 ? Hl(e, t) : null, h = p?.expression.type ?? "Literal", m = eg(e, t), w = h.toLowerCase(), x = p && (w === "literal" || w === "object") && !rg(t) ? ng(t.typeName) : null, b = x ? Go(n, t, { ...l, scope: "collection" }) : void 0, g = p ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: r,
    readOnly: u,
    surface: "inline",
    syntax: h
  } : null, y = g ? df(i, g) : null, N = y?.surfaces.inline, j = y && g ? ff(y, g, m) : [], S = x != null, E = !!(p && !S && VN(t, d?.id)), _ = !!(p && !S && ON(t, d?.id)), [M, I] = K(!1), $ = (k) => {
    const D = p ? Jh(p, k) : k;
    c(pa(e, t, D));
  }, z = (k) => {
    p && c(pa(e, t, Qh(p, k)));
  }, C = x ? b ? Yo(b.component, t, m, u, { ...l, scope: "collection" }, $) : /* @__PURE__ */ o.jsx(
    $N,
    {
      input: t,
      elementTypeName: x.elementTypeName,
      value: m,
      editors: n,
      context: l,
      disabled: u,
      onChange: $
    }
  ) : null, A = h === AN && p ? /* @__PURE__ */ o.jsx(
    RN,
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
  ) : Yo(f, t, m, u, l, $));
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ o.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ o.jsx("span", { children: ds(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ o.jsx("p", { children: t.description }) : null,
    p && !E ? /* @__PURE__ */ o.jsx(
      Uo,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: h,
        descriptors: r,
        disabled: u,
        onChange: z
      }
    ) : null,
    E ? /* @__PURE__ */ o.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-expression-editor", children: [
        A,
        Jo(j)
      ] }),
      /* @__PURE__ */ o.jsx(
        Uo,
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
          children: /* @__PURE__ */ o.jsx(Pn, { size: 13 })
        }
      ) : null
    ] }) : /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
      A,
      Jo(j)
    ] }),
    _ && !E ? /* @__PURE__ */ o.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => I(!0),
        children: [
          /* @__PURE__ */ o.jsx(Pn, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    M ? /* @__PURE__ */ o.jsx(
      PN,
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
        onClose: () => I(!1)
      }
    ) : null
  ] });
}
function TN(e, t, n) {
  return [
    "wf-collection-item",
    t === e ? "dragging" : "",
    t !== null && t !== e && n === e ? "drop-target" : ""
  ].filter(Boolean).join(" ");
}
function $N({
  input: e,
  elementTypeName: t,
  value: n,
  editors: i,
  context: r,
  disabled: s,
  onChange: a
}) {
  const c = og(n), u = ag(e, t), l = { ...r, scope: "element" }, d = Go(i, u, l)?.component, f = e.displayName || e.name, p = (N, j) => a(c.map((S, E) => E === N ? j : S)), [h, m] = K(null), [w, v] = K(null), x = () => {
    m(null), v(null);
  }, b = (N) => (j) => {
    m(N), j.dataTransfer.effectAllowed = "move", j.dataTransfer.setData("text/plain", String(N));
  }, g = (N) => (j) => {
    h !== null && (j.preventDefault(), j.dataTransfer.dropEffect = "move", w !== N && v(N));
  }, y = (N) => (j) => {
    j.preventDefault(), h !== null && h !== N && a(uo(c, h, N)), x();
  };
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-collection-editor", children: [
    c.length === 0 ? /* @__PURE__ */ o.jsx("p", { className: "wf-collection-empty", children: "No items yet." }) : /* @__PURE__ */ o.jsx("ul", { className: "wf-collection-items", children: c.map((N, j) => /* @__PURE__ */ o.jsxs(
      "li",
      {
        className: TN(j, h, w),
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
              onDragEnd: x,
              children: /* @__PURE__ */ o.jsx(sl, { size: 13, "aria-hidden": "true" })
            }
          ),
          /* @__PURE__ */ o.jsx("div", { className: "wf-collection-item-editor", children: Yo(d, u, N, s, l, (S) => p(j, S)) }),
          /* @__PURE__ */ o.jsxs("div", { className: "wf-collection-item-actions", children: [
            /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${j + 1} up`,
                disabled: s || j === 0,
                onClick: () => a(uo(c, j, j - 1)),
                children: /* @__PURE__ */ o.jsx(Tf, { size: 13 })
              }
            ),
            /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${j + 1} down`,
                disabled: s || j === c.length - 1,
                onClick: () => a(uo(c, j, j + 1)),
                children: /* @__PURE__ */ o.jsx(ol, { size: 13 })
              }
            ),
            /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button danger",
                "aria-label": `Remove ${f} item ${j + 1}`,
                disabled: s,
                onClick: () => a(c.filter((S, E) => E !== j)),
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
        onClick: () => a([...c, sg(t)]),
        children: [
          /* @__PURE__ */ o.jsx(Qt, { size: 13 }),
          " Add item"
        ]
      }
    )
  ] });
}
function PN({
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
  const d = nl(), f = e.displayName || e.name, p = {
    activity: r,
    descriptor: e,
    expressionDescriptors: i,
    readOnly: a,
    surface: "expanded",
    syntax: n
  }, h = df(s, p), m = h?.surfaces.expanded, w = h ? ff(h, p, t) : [], v = m ? null : LN(s, p);
  return Q(() => {
    const x = (b) => {
      b.key === "Escape" && l();
    };
    return window.addEventListener("keydown", x), () => window.removeEventListener("keydown", x);
  }, [l]), /* @__PURE__ */ o.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ o.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": d, children: [
    /* @__PURE__ */ o.jsxs("header", { children: [
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ o.jsx("h3", { id: d, children: f })
      ] }),
      /* @__PURE__ */ o.jsx("button", { type: "button", "aria-label": `Close ${f} editor`, onClick: l, children: /* @__PURE__ */ o.jsx(is, { size: 16 }) })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ o.jsx(
          Uo,
          {
            label: `${f} expression syntax`,
            value: n,
            descriptors: i,
            disabled: a,
            onChange: u
          }
        ),
        /* @__PURE__ */ o.jsx("span", { children: ds(e.typeName) })
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
        v ? /* @__PURE__ */ o.jsx("p", { className: "wf-expression-editor-hint", children: v }) : null,
        /* @__PURE__ */ o.jsx(
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
      Jo(w)
    ] }),
    /* @__PURE__ */ o.jsxs("footer", { children: [
      /* @__PURE__ */ o.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: l, children: "Close" })
    ] })
  ] }) });
}
function Yo(e, t, n, i, r, s) {
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
function Uo({
  label: e,
  value: t,
  descriptors: n,
  disabled: i,
  variant: r = "block",
  onChange: s
}) {
  const [a, c] = K(!1), u = nl(), l = n.find((f) => f.type === t), d = [
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
const Zo = "::";
function uf(e) {
  return !e || e === Xi ? Xi : e;
}
function Uc(e, t) {
  return `${uf(t)}${Zo}${e}`;
}
function MN(e) {
  const t = e.indexOf(Zo);
  if (t < 0) return null;
  const n = e.slice(t + Zo.length);
  return n ? { scopeId: e.slice(0, t), referenceKey: n } : null;
}
function RN({ value: e, visibleVariables: t, scopeStatus: n, disabled: i, onChange: r }) {
  const s = _l(e), a = !!e && typeof e == "object" || typeof e == "string" && e.trim().startsWith("{"), c = s && (a || t.some((d) => d.referenceKey === s.referenceKey)) ? s : null, u = c ? Uc(c.referenceKey, c.declaringScopeId) : "", l = !!c && t.some(
    (d) => d.referenceKey === c.referenceKey && d.scopeId === uf(c.declaringScopeId)
  );
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-variable-picker", children: [
    /* @__PURE__ */ o.jsxs(
      "select",
      {
        "aria-label": "Variable reference",
        value: u,
        disabled: i,
        onChange: (d) => {
          const f = MN(d.target.value);
          f && r(Yp(f.referenceKey, f.scopeId));
        },
        children: [
          /* @__PURE__ */ o.jsx("option", { value: "", children: "Select a variable…" }),
          c && !l ? /* @__PURE__ */ o.jsxs("option", { value: u, children: [
            c.referenceKey,
            " (not visible from this scope)"
          ] }) : null,
          t.map((d) => {
            const f = Uc(d.referenceKey, d.scopeId);
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
function Go(e, t, n) {
  return [...e].sort((i, r) => (i.order ?? 500) - (r.order ?? 500)).find((i) => i.supports(t, n));
}
function df(e, t) {
  return [...e].sort((n, i) => (n.order ?? 500) - (i.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function ff(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function LN(e, t) {
  if (t.syntax.toLowerCase() === "literal") return null;
  const n = [...e].sort((a, c) => (a.order ?? 500) - (c.order ?? 500)).find((a) => a.supports(t) && a.metadata)?.metadata;
  if (!n) return `No enhanced editor is registered for ${t.syntax}. Using the generic text editor.`;
  const i = n.displayName?.trim() || "enhanced editor", r = n.installHint?.trim(), s = `No ${i} is registered for ${t.syntax}. Using the generic text editor.`;
  return r ? `${s} ${r}` : s;
}
function Jo(e) {
  return e.length === 0 ? null : /* @__PURE__ */ o.jsx("div", { className: "wf-expression-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const i = t.severity ?? "info";
    return /* @__PURE__ */ o.jsxs("p", { className: `wf-expression-editor-diagnostic ${i}`, children: [
      t.code ? /* @__PURE__ */ o.jsx("span", { children: t.code }) : null,
      t.message
    ] }, `${t.code ?? "diagnostic"}-${n}`);
  }) });
}
function zN(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const i = n.category?.trim() || "General";
    t.set(i, [...t.get(i) ?? [], n]);
  }
  return [...t.entries()].map(([n, i]) => ({ category: n, inputs: i }));
}
function VN(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !lf.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function ON(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !lf.has(t) && n !== "multiline") return !1;
  const i = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(i) || n === "singleline" || n === "multiline";
}
function HN({
  context: e,
  selectedNode: t,
  selectedNodeLabel: n,
  selectedActivityType: i,
  selectedDescriptor: r,
  selectedNodeAvailability: s,
  selectedSlots: a,
  catalog: c,
  catalogByVersion: u,
  selectedSupportsScopedVariables: l,
  propertyEditors: d,
  expressionEditors: f,
  expressionDescriptors: p,
  descriptorStatus: h,
  scopedVariableAnalysis: m,
  onSelectedActivityChange: w,
  onEnterSlot: v,
  onReplaceSlotActivity: x
}) {
  const [b, g] = K(null);
  return b && b.nodeId !== t?.nodeId && g(null), t ? /* @__PURE__ */ o.jsxs("div", { className: "wf-inspector-content", children: [
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
      /* @__PURE__ */ o.jsx(Tn, { size: 14 }),
      /* @__PURE__ */ o.jsxs("span", { children: [
        "No longer available for new use · ",
        zn(s.state)
      ] })
    ] }) : null,
    /* @__PURE__ */ o.jsx(
      _N,
      {
        activity: t,
        descriptor: r,
        editors: d,
        expressionEditors: f,
        expressionDescriptors: p,
        descriptorStatus: h,
        visibleVariables: m.visibleVariables,
        scopeStatus: m.status,
        onChange: w
      }
    ),
    l ? /* @__PURE__ */ o.jsx("div", { className: "wf-container-variables", children: /* @__PURE__ */ o.jsx(
      Ub,
      {
        context: e,
        variables: Al(t),
        title: "Container variables",
        addLabel: "Add container variable",
        emptyLabel: "No container variables declared on this activity.",
        warnings: Zp(m.shadowingWarnings, t.nodeId),
        onChange: (y) => w(qp(t, y))
      }
    ) }) : null,
    a.length > 0 ? /* @__PURE__ */ o.jsxs("div", { className: "wf-slot-list", children: [
      /* @__PURE__ */ o.jsx("span", { children: "Embedded slots" }),
      a.map((y) => {
        const N = `${n} / ${y.label}`;
        return /* @__PURE__ */ o.jsxs("div", { className: "wf-slot-row", children: [
          /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => v(t.nodeId, y, N), children: [
            y.label,
            /* @__PURE__ */ o.jsx("small", { children: Ej(y, u) })
          ] }),
          y.cardinality === "single" ? /* @__PURE__ */ o.jsx(
            "button",
            {
              type: "button",
              className: "wf-slot-change",
              "aria-label": `${y.activities.length > 0 ? "Change" : "Choose"} ${y.label} activity`,
              title: y.activities.length > 0 ? "Change activity" : "Choose activity",
              onClick: (j) => g({ nodeId: t.nodeId, slotId: y.id, clientX: j.clientX, clientY: j.clientY }),
              children: /* @__PURE__ */ o.jsx($f, { size: 14 })
            }
          ) : null
        ] }, y.id);
      })
    ] }) : /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." }),
    b ? /* @__PURE__ */ o.jsx(
      nf,
      {
        clientX: b.clientX,
        clientY: b.clientY,
        activities: c,
        onPick: (y) => {
          g(null);
          const N = a.find((j) => j.id === b.slotId);
          N && x(t.nodeId, N, `${n} / ${N.label}`, y);
        },
        onClose: () => g(null)
      }
    ) : null
  ] }) : /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
}
const pf = [
  { value: "flowchart", label: "Flowchart", hint: "Free-form graph of connected activities." },
  { value: "sequence", label: "Sequence", hint: "Ordered list of activities that run top to bottom." }
];
function hf({ label: e, hint: t }) {
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsx("span", { className: "wf-root-card-title", children: e }),
    /* @__PURE__ */ o.jsx("span", { className: "wf-root-card-hint", children: t })
  ] });
}
function WN({ value: e, onChange: t }) {
  return /* @__PURE__ */ o.jsx("div", { className: "wf-root-cards", role: "radiogroup", "aria-label": "Root activity", children: pf.map((n) => {
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
      /* @__PURE__ */ o.jsx(hf, { label: n.label, hint: n.hint })
    ] }, n.value);
  }) });
}
function FN({ onPick: e }) {
  return /* @__PURE__ */ o.jsx("div", { className: "wf-root-cards", role: "group", "aria-label": "Fill this slot", children: pf.map((t) => /* @__PURE__ */ o.jsx(
    "button",
    {
      type: "button",
      className: "wf-root-card",
      "aria-label": t.label,
      onClick: () => e(t.value),
      children: /* @__PURE__ */ o.jsx(hf, { label: t.label, hint: t.hint })
    },
    t.value
  )) });
}
function BN({ slotLabel: e, catalog: t, onPickActivity: n, onBrowseAll: i }) {
  const r = (s) => {
    const a = Kd(t, s);
    a && n(a);
  };
  return /* @__PURE__ */ o.jsx("div", { className: "wf-slot-empty", role: "group", "aria-label": `Fill ${e}`, children: /* @__PURE__ */ o.jsxs("div", { className: "wf-slot-empty-card", children: [
    /* @__PURE__ */ o.jsx("p", { className: "wf-slot-empty-title", children: "This slot is empty" }),
    /* @__PURE__ */ o.jsxs("p", { className: "wf-slot-empty-hint", children: [
      "Choose a container for ",
      /* @__PURE__ */ o.jsx("strong", { children: e }),
      ", or pick any activity."
    ] }),
    /* @__PURE__ */ o.jsx(FN, { onPick: r }),
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
function KN({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: i,
  expressionEditors: r,
  workflowDesignerPanels: s,
  autosaveEnabledByDefault: a,
  onBack: c
}) {
  const u = eN(), { draft: l, frames: d, selectedNodeId: f, testRun: p, publishedArtifactId: h } = u.state, {
    loadDraft: m,
    replaceDraftByBatch: w,
    editDraft: v,
    editDraftAndSelect: x,
    select: b,
    navigateToScope: g,
    resetToRoot: y,
    enterSlot: N,
    startTestRun: j,
    clearTestRun: S,
    setPublishedArtifact: E
  } = u, [_, M] = K(""), [I, $] = K(""), [z, C] = K("idle"), [A, k] = K(() => /* @__PURE__ */ new Set()), [D, P] = K(""), [T, F] = K("activities"), [W, H] = K("inspector"), [Y, V] = K("designer"), {
    paletteWidth: q,
    inspectorWidth: J,
    paletteCollapsed: Z,
    inspectorCollapsed: R,
    maximizedSidePanel: X,
    setInspectorCollapsed: ue,
    paletteExpanded: ce,
    inspectorExpanded: ee,
    editorBodyClassName: ne,
    editorBodyStyle: fe,
    toggleSidePanelCollapsed: O,
    toggleSidePanelMaximized: te,
    startSidePanelResize: ge,
    handleSidePanelResizeKeyDown: me
  } = of(), { resetHistory: _e, undo: De, redo: $e, canUndoNow: st, canRedoNow: Ye } = Gj({ draft: l, restoreDraft: m }), { saveDraft: Ue, autosaveEnabled: Pe, setAutosaveEnabled: He, markSaved: We } = jN({ context: e, draft: l, autosaveEnabledByDefault: a, editDraft: v, setStatus: $, setError: M }), {
    details: Ne,
    setDetails: zt,
    catalog: Ie,
    activityDescriptors: gn,
    availabilityDiagnostics: L,
    expressionDescriptors: B,
    descriptorStatus: U,
    reload: ie
  } = NN({ context: e, definitionId: t, resetHistory: _e, loadDraft: m, markSaved: We, setError: M }), { updateDefinitionMeta: de } = SN({ context: e, details: Ne, setDetails: zt, setStatus: $ }), {
    catalogByVersion: pe,
    availabilityLookup: xe,
    scopeOwner: Ae,
    isUnsupportedDesigner: Se,
    scope: ze,
    selectedNode: we,
    selectedDescriptor: Vt,
    selectedNodeAvailability: Sr,
    selectedSlots: si,
    selectedSupportsScopedVariables: Cr,
    scopedVariableAnalysis: kr,
    isFlowchartDesigner: pt,
    canAddActivitiesToCanvas: Er
  } = kN({ context: e, draft: l, frames: d, selectedNodeId: f, catalog: Ie, activityDescriptors: gn, availabilityDiagnostics: L }), Ot = le(() => Ko(Ie), [Ie]), Ir = le(() => {
    const G = D.trim().toLowerCase();
    if (!G) return Ot;
    const ae = Ie.filter((ye) => Ce(ye).toLowerCase().includes(G) || ye.activityTypeKey.toLowerCase().includes(G) || (ye.category ?? "").toLowerCase().includes(G) || (ye.description ?? "").toLowerCase().includes(G));
    return Ko(ae);
  }, [Ie, D, Ot]), Ht = z !== "idle", Ar = !!l?.state.rootActivity && !Ht, ai = Mt(n, "weaver.workflows.find-draft-risks"), ci = Mt(n, "weaver.workflows.propose-update"), _r = uN({
    draft: l,
    scope: ze,
    scopeOwner: Ae,
    frames: d,
    catalog: Ie,
    catalogByVersion: pe,
    isUnsupportedDesigner: Se,
    isFlowchartDesigner: pt,
    canAddActivitiesToCanvas: Er,
    selectedNodeId: f,
    editDraft: v,
    editDraftAndSelect: x,
    select: b,
    resetToRoot: y,
    setStatus: $,
    setError: M
  }), {
    nodes: Wt,
    edges: Dr,
    canvasRef: Tr,
    setReactFlowInstance: $r,
    connectMenu: Ft,
    setConnectMenu: li,
    edgeActions: ui,
    onNodesChange: Pr,
    onEdgesChange: Mr,
    onNodesDelete: Rr,
    onEdgesDelete: Lr,
    isValidConnection: zr,
    onConnect: Vr,
    onConnectStart: di,
    onConnectEnd: Or,
    onReconnect: Hr,
    commitLayout: Wr,
    canAutoLayout: Fr,
    autoLayout: Br,
    onCanvasDragOver: fi,
    onCanvasDragLeave: pi,
    onCanvasDrop: hi,
    openEmptyConnectMenu: mn,
    onConnectMenuPick: Kr,
    addActivity: Xr,
    onPaletteClick: qr,
    onPaletteDragStart: Yr,
    onPaletteDragEnd: Ur,
    onPalettePointerDown: Zr
  } = _r, Gr = !Se && d.length > 0 && !!ze && ze.slot.activities.length === 0 && Wt.length === 0;
  bN({ draft: l, details: Ne, catalog: Ie, replaceDraftByBatch: w, setStatus: $, setError: M }), Q(() => {
    !l?.state.rootActivity || Ie.length === 0 || v(({ draft: G }) => {
      if (!G?.state.rootActivity) return null;
      const ae = Nl(G.state.rootActivity, pe);
      return !ae || ae === G.state.rootActivity ? null : {
        ...G,
        state: {
          ...G.state,
          rootActivity: ae
        }
      };
    });
  }, [Ie.length, pe, l?.state.rootActivity, v]), EN({ details: Ne, draft: l, selectedNode: we, selectedNodeId: f, selectedDescriptor: Vt, catalogByVersion: pe }), Q(() => {
    k((G) => {
      let ae = !1;
      const ye = new Set(G);
      for (const Me of Ot)
        ye.has(Me.category) || (ye.add(Me.category), ae = !0);
      return ae ? ye : G;
    });
  }, [Ot]);
  const { exportJson: Jr, save: gi, promoteAndPublish: Qr, run: mi } = CN({
    context: e,
    draft: l,
    details: Ne,
    busy: Ht,
    saveDraft: Ue,
    reload: ie,
    startTestRun: j,
    clearTestRun: S,
    setPublishedArtifact: E,
    setOperation: C,
    setStatus: $,
    setError: M,
    setActiveRightPanelId: H,
    setInspectorCollapsed: ue
  }), eo = se((G) => {
    v(({ draft: ae }) => ae ? { ...ae, state: G(ae.state) } : null);
  }, [v]), to = se((G) => {
    if (!l) return "No draft is loaded.";
    const ae = _b(G, l);
    return ae.ok ? (m(ae.draft), $("Applied workflow JSON."), null) : ae.error;
  }, [l, m]);
  Q(() => {
    const G = (ae) => {
      if (Y !== "designer" || !(ae.metaKey || ae.ctrlKey)) return;
      const ye = ae.target;
      if (ye && (ye.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(ye.tagName))) return;
      const Me = ae.key.toLowerCase();
      Me === "z" && !ae.shiftKey ? (ae.preventDefault(), De()) : (Me === "z" && ae.shiftKey || Me === "y") && (ae.preventDefault(), $e());
    };
    return window.addEventListener("keydown", G), () => window.removeEventListener("keydown", G);
  }, [Y, De, $e]);
  const jt = se((G, ae, ye) => {
    const Me = ae.cardinality === "single" ? ae.activities[0]?.nodeId ?? null : null;
    N(G, ae.id, ye, Me);
  }, [N]), no = se((G, ae, ye, Me) => {
    const wi = ae.activities.length > 0, Bs = Fi(Me, Xo(Me));
    v(({ draft: vi }) => {
      const Ks = vi?.state.rootActivity;
      return !vi || !Ks ? null : {
        ...vi,
        state: {
          ...vi.state,
          rootActivity: _o(Ks, G, (bf) => dn(bf, ae, [Bs]), pe)
        }
      };
    }), N(G, ae.id, ye, Bs.nodeId), M(""), $(wi ? `Replaced ${ae.label} content` : `Assigned ${Ce(Me)} to ${ae.label}`);
  }, [pe, v, N]), io = le(() => Se ? null : (G, ae, ye) => jt(G, ye, `${ae} / ${ye.label}`), [jt, Se]), yi = se((G) => {
    v(({ draft: ae }) => {
      const ye = ae?.state.rootActivity;
      return !ae || !ye ? null : {
        ...ae,
        state: {
          ...ae.state,
          rootActivity: _o(ye, G.nodeId, () => G, pe)
        }
      };
    });
  }, [pe, v]), mf = se((G) => {
    if (!G) return;
    const ae = l?.state.rootActivity;
    if (!ae) return;
    const ye = mp(ae, G, (Me) => {
      const wi = pe.get(Me.activityVersionId);
      return wi ? Ce(wi) : Me.nodeId;
    }, pe);
    ye && (V("designer"), g(ye, G), ue(!1));
  }, [l?.state.rootActivity, pe, ue, g]), yf = (G) => {
    k((ae) => {
      const ye = new Set(ae);
      return ye.has(G) ? ye.delete(G) : ye.add(G), ye;
    });
  };
  if (!Ne || !l)
    return /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: _ || "Loading workflow editor..." });
  const xi = p?.draftSignature === Be(l) ? p.view : null, Os = xi && I.startsWith("Test run") ? "" : I, xf = (G) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(G)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, wf = {
    definition: Ne.definition,
    draft: l,
    selectedActivity: we,
    selectedActivityDescriptor: Vt,
    selectedActivitySlots: si,
    catalog: Ie,
    currentScopeOwner: Ae,
    frames: d
  }, Hs = s.map((G) => {
    const ae = G.component;
    return {
      id: G.id,
      title: G.title,
      side: G.side,
      order: G.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ o.jsx(ae, { context: wf })
    };
  }), ro = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ o.jsx(Zn, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(
        IN,
        {
          paletteSearch: D,
          onSearchChange: P,
          groups: Ir,
          expandedCategories: A,
          onToggleCategory: yf,
          onActivityClick: qr,
          onActivityDragStart: Yr,
          onActivityDragEnd: Ur,
          onActivityPointerDown: Zr
        }
      )
    },
    ...Hs.filter((G) => G.side === "left")
  ].sort(Wc), oo = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ o.jsx(es, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(
        HN,
        {
          context: e,
          selectedNode: we,
          selectedNodeLabel: we ? Wt.find((G) => G.id === we.nodeId)?.data.label ?? we.nodeId : "",
          selectedActivityType: we ? Vt?.typeName ?? pe.get(we.activityVersionId)?.activityTypeKey ?? "Unknown" : "",
          selectedDescriptor: Vt,
          selectedNodeAvailability: Sr,
          selectedSlots: si,
          catalog: Ie,
          catalogByVersion: pe,
          selectedSupportsScopedVariables: Cr,
          propertyEditors: i,
          expressionEditors: r,
          expressionDescriptors: B,
          descriptorStatus: U,
          scopedVariableAnalysis: kr,
          onSelectedActivityChange: yi,
          onEnterSlot: jt,
          onReplaceSlotActivity: no
        }
      )
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: /* @__PURE__ */ o.jsx(Jt, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(Wj, { testRun: xi, onOpenRun: xf })
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ o.jsx(al, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(
        Kj,
        {
          context: e,
          ai: n,
          definitionId: Ne.definition.id,
          publishedArtifactId: h
        }
      )
    },
    ...Hs.filter((G) => G.side === "right")
  ].sort(Wc), Ws = ro.find((G) => G.id === T) ?? ro[0], Fs = oo.find((G) => G.id === W) ?? oo[0], vf = [
    { id: "designer", title: "Designer", order: 0, icon: /* @__PURE__ */ o.jsx(cl, { size: 14 }), render: () => null },
    { id: "code", title: "Code", order: 1, icon: /* @__PURE__ */ o.jsx(zf, { size: 14 }), render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: /* @__PURE__ */ o.jsx(ns, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-link-button", onClick: c, children: "Definitions" }),
      /* @__PURE__ */ o.jsx(yt, { size: 14 }),
      /* @__PURE__ */ o.jsx("strong", { children: Ne.definition.name }),
      /* @__PURE__ */ o.jsx("span", { className: "wf-chip", children: "Draft" }),
      Os ? /* @__PURE__ */ o.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ o.jsx(ln, { size: 13 }),
        " ",
        Os
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
              disabled: !st,
              onClick: De,
              children: /* @__PURE__ */ o.jsx(Pf, { size: 16 })
            }
          ),
          /* @__PURE__ */ o.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Redo",
              title: "Redo (Ctrl+Shift+Z)",
              disabled: !Ye,
              onClick: $e,
              children: /* @__PURE__ */ o.jsx(Mf, { size: 16 })
            }
          ),
          /* @__PURE__ */ o.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Auto-layout",
              title: "Auto-layout the canvas",
              disabled: !Fr,
              onClick: Br,
              children: /* @__PURE__ */ o.jsx(Rf, { size: 16 })
            }
          )
        ] }),
        /* @__PURE__ */ o.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ o.jsx("input", { className: "wf-switch-input", type: "checkbox", checked: Pe, onChange: (G) => He(G.target.checked) }),
          /* @__PURE__ */ o.jsx("span", { children: "Autosave" })
        ] }),
        ai ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Rt(n, ai, { definition: Ne.definition, draft: l }), children: [
          /* @__PURE__ */ o.jsx(ut, { size: 15 }),
          " Risks"
        ] }) : null,
        ci ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Rt(n, ci, { definition: Ne.definition, draft: l }), children: [
          /* @__PURE__ */ o.jsx(ut, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ o.jsxs("button", { type: "button", title: "Export workflow as JSON", onClick: Jr, children: [
          /* @__PURE__ */ o.jsx(Lf, { size: 15 }),
          " Export"
        ] }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", disabled: Ht, onClick: () => {
          gi();
        }, children: [
          /* @__PURE__ */ o.jsx(ts, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", disabled: Ht, onClick: () => {
          Qr();
        }, children: [
          /* @__PURE__ */ o.jsx(il, { size: 15 }),
          " Promote"
        ] }),
        xi ? /* @__PURE__ */ o.jsx(
          Hj,
          {
            testRun: xi,
            onOpenDetails: () => {
              H("runtime"), ue(!1);
            }
          }
        ) : null,
        /* @__PURE__ */ o.jsxs(
          "button",
          {
            type: "button",
            disabled: !Ar,
            title: l.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              mi();
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
      /* @__PURE__ */ o.jsx(bt, { size: 16 }),
      " ",
      _
    ] }) : null,
    /* @__PURE__ */ o.jsxs("div", { className: ne, style: fe, children: [
      /* @__PURE__ */ o.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ o.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ o.jsx(
            Wi,
            {
              label: "Activities panel tabs",
              tabs: ro,
              activeTabId: Ws.id,
              onSelect: F
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
                children: Z ? /* @__PURE__ */ o.jsx(yt, { size: 14 }) : /* @__PURE__ */ o.jsx(Mn, { size: 14 })
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
                children: X === "palette" ? /* @__PURE__ */ o.jsx(Eo, { size: 14 }) : /* @__PURE__ */ o.jsx(Pn, { size: 14 })
              }
            )
          ] })
        ] }),
        ce ? Ws.render() : null
      ] }),
      ce && !X ? /* @__PURE__ */ o.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": kn,
          "aria-valuemax": En,
          "aria-valuenow": q,
          tabIndex: 0,
          onPointerDown: (G) => ge("palette", G),
          onKeyDown: (G) => me("palette", G)
        }
      ) : /* @__PURE__ */ o.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ o.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ o.jsx("div", { className: "wf-canvas-tabs", children: /* @__PURE__ */ o.jsx(
          Wi,
          {
            label: "Editor view tabs",
            tabs: vf,
            activeTabId: Y,
            onSelect: (G) => V(G)
          }
        ) }),
        Y === "code" ? /* @__PURE__ */ o.jsx(Rb, { draft: l, onApply: to }) : Y === "properties" ? /* @__PURE__ */ o.jsx(Gb, { details: Ne, draft: l, context: e, onStateChange: eo, onDefinitionMetaChange: de }) : /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
          /* @__PURE__ */ o.jsxs("div", { className: "wf-breadcrumb", children: [
            /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => y(), children: "Root" }),
            d.map((G, ae) => /* @__PURE__ */ o.jsxs(Ke.Fragment, { children: [
              /* @__PURE__ */ o.jsx(yt, { size: 13 }),
              /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => g(d.slice(0, ae + 1), null), children: G.label })
            ] }, `${G.ownerNodeId}-${G.slotId}-${ae}`))
          ] }),
          /* @__PURE__ */ o.jsxs("div", { className: "wf-canvas", ref: Tr, onDragOver: fi, onDragLeave: pi, onDrop: hi, children: [
            /* @__PURE__ */ o.jsx(Hd.Provider, { value: ui, children: /* @__PURE__ */ o.jsx(Wd.Provider, { value: xe, children: /* @__PURE__ */ o.jsx(Fd.Provider, { value: io, children: /* @__PURE__ */ o.jsxs(
              Dd,
              {
                nodes: Wt,
                edges: Dr,
                nodeTypes: ef,
                edgeTypes: tf,
                onInit: $r,
                onNodesChange: Pr,
                onEdgesChange: Mr,
                onNodesDelete: Rr,
                onEdgesDelete: Lr,
                onConnect: Vr,
                onConnectStart: pt ? di : void 0,
                onConnectEnd: pt ? Or : void 0,
                onReconnect: pt ? Hr : void 0,
                isValidConnection: zr,
                onDragOver: fi,
                onDragLeave: pi,
                onDrop: hi,
                onPaneClick: () => b(null),
                onNodeClick: (G, ae) => b(ae.id),
                onNodeDragStop: Se ? void 0 : Wr,
                minZoom: 0.2,
                maxZoom: 1.8,
                nodesConnectable: pt,
                nodesDraggable: !Se,
                selectionOnDrag: !0,
                multiSelectionKeyCode: ["Shift", "Meta", "Control"],
                deleteKeyCode: Se ? null : ["Backspace", "Delete"],
                panActivationKeyCode: null,
                defaultEdgeOptions: { type: "workflow" },
                children: [
                  /* @__PURE__ */ o.jsx($d, { gap: 18, size: 1 }),
                  /* @__PURE__ */ o.jsx(Md, {}),
                  /* @__PURE__ */ o.jsx(Ld, { pannable: !0, zoomable: !0 })
                ]
              }
            ) }) }) }),
            Gr ? /* @__PURE__ */ o.jsx(
              BN,
              {
                slotLabel: ze?.slot.label ?? "this slot",
                catalog: Ie,
                onPickActivity: Xr,
                onBrowseAll: mn
              }
            ) : pt && Wt.length === 0 ? /* @__PURE__ */ o.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => mn(), children: [
              /* @__PURE__ */ o.jsx(Qt, { size: 15 }),
              " Add activity"
            ] }) : null,
            Ft ? /* @__PURE__ */ o.jsx(
              nf,
              {
                clientX: Ft.clientX,
                clientY: Ft.clientY,
                activities: Ie,
                onPick: Kr,
                onClose: () => li(null)
              }
            ) : null
          ] }),
          /* @__PURE__ */ o.jsx(Oj, { draft: l, onRepair: mf })
        ] })
      ] }),
      ee && !X ? /* @__PURE__ */ o.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Yt,
          "aria-valuemax": Ut,
          "aria-valuenow": J,
          tabIndex: 0,
          onPointerDown: (G) => ge("inspector", G),
          onKeyDown: (G) => me("inspector", G)
        }
      ) : /* @__PURE__ */ o.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ o.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ o.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ o.jsx(
            Wi,
            {
              label: "Inspector panel tabs",
              tabs: oo,
              activeTabId: Fs.id,
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
                children: R ? /* @__PURE__ */ o.jsx(Mn, { size: 14 }) : /* @__PURE__ */ o.jsx(yt, { size: 14 })
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
                children: X === "inspector" ? /* @__PURE__ */ o.jsx(Eo, { size: 14 }) : /* @__PURE__ */ o.jsx(Pn, { size: 14 })
              }
            )
          ] })
        ] }),
        ee ? Fs.render() : null
      ] })
    ] })
  ] });
}
function XN({ page: e, pageSize: t, totalCount: n, onPageChange: i, onPageSizeChange: r }) {
  const s = Bd(n, t), a = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
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
      /* @__PURE__ */ o.jsx("select", { value: t, onChange: (u) => r(Number(u.target.value)), children: tj.map((u) => /* @__PURE__ */ o.jsx("option", { value: u, children: u }, u)) })
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
        /* @__PURE__ */ o.jsx(yt, { size: 14 })
      ] })
    ] })
  ] });
}
function qN({ draft: e, creating: t, ai: n, suggestMetadataAction: i, onChange: r, onClose: s, onSubmit: a }) {
  const [c, u] = K(!1), [l, d] = K(""), [f, p] = K(!1), [h, m] = K(null), [w, v] = K(null), x = re(null), b = re(e);
  b.current = e;
  const g = re(r);
  g.current = r;
  const y = se((j) => {
    const S = { ...b.current };
    j.name && (S.name = j.name), j.description && (S.description = j.description), g.current(S), m(null), v(null);
  }, []);
  Q(() => {
    if (i)
      return n.onPromptResult((j) => {
        if (j.requestId !== x.current) return;
        if (x.current = null, p(!1), j.status !== "completed") {
          v(j.status === "cancelled" ? "Weaver needs more detail — continue in the assistant panel." : "Weaver couldn't generate a suggestion. Try again or fill the fields manually.");
          return;
        }
        const S = sj(j.text);
        if (!S) {
          v("Couldn't read a suggestion from Weaver's reply. See the assistant panel.");
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
    x.current = S, p(!0), m(null), v(null), n.dispatchPrompt({ ...j, requestId: S });
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
                onChange: (j) => d(j.target.value),
                onKeyDown: (j) => {
                  (j.metaKey || j.ctrlKey) && j.key === "Enter" && (j.preventDefault(), N());
                }
              }
            )
          ] }),
          /* @__PURE__ */ o.jsx("div", { className: "wf-ai-suggest-actions", children: /* @__PURE__ */ o.jsxs("button", { type: "button", className: "wf-ai-action", onClick: N, disabled: f, children: [
            /* @__PURE__ */ o.jsx(ut, { size: 13 }),
            " ",
            f ? "Generating…" : "Generate"
          ] }) }),
          w ? /* @__PURE__ */ o.jsx("p", { className: "wf-ai-suggest-error", role: "alert", children: w }) : null,
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
            WN,
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
function YN({ context: e, ai: t, onOpen: n }) {
  const [i, r] = K(""), [s, a] = K("active"), [c, u] = K(1), [l, d] = K(nj), [f, p] = K("loading"), [h, m] = K(""), [w, v] = K(""), [x, b] = K([]), [g, y] = K(0), [N, j] = K(() => /* @__PURE__ */ new Set()), [S, E] = K(null), [_, M] = K(!1), [I, $] = K([]), [z, C] = K("idle"), A = re(null), k = le(() => x.map((O) => O.id), [x]), D = Mt(t, "weaver.workflows.suggest-create-metadata"), P = Mt(t, "weaver.workflows.explain-definition"), T = k.filter((O) => N.has(O)).length, F = k.length > 0 && T === k.length, W = se(async () => {
    p("loading"), m("");
    try {
      const O = await rh(e, { search: i, state: s, page: c, pageSize: l }), te = typeof O.totalCount == "number", ge = O.totalCount ?? O.definitions.length, me = Bd(ge, l);
      if (ge > 0 && c > me) {
        u(me);
        return;
      }
      b(te ? O.definitions : oj(O.definitions, c, l)), y(ge), p("ready");
    } catch (O) {
      m(O instanceof Error ? O.message : String(O)), p("failed");
    }
  }, [e, i, s, c, l]);
  Q(() => {
    W();
  }, [W]), Q(() => {
    A.current && (A.current.indeterminate = T > 0 && !F);
  }, [F, T]);
  const H = se(async () => {
    if (!(z === "loading" || z === "ready")) {
      C("loading");
      try {
        const O = await cr(e);
        $(O.activities ?? []), C("ready");
      } catch (O) {
        C("failed"), m(O instanceof Error ? O.message : String(O));
      }
    }
  }, [z, e]), Y = () => {
    m(""), v(""), E({ name: "", description: "", rootKind: "flowchart" }), H();
  }, V = async () => {
    if (S?.name.trim()) {
      M(!0), m(""), v("");
      try {
        const O = await lh(e, {
          name: S.name.trim(),
          description: S.description.trim() || null,
          rootKind: S.rootKind,
          rootActivityVersionId: aj(S, I)
        });
        E(null), n(O.definition.id);
      } catch (O) {
        m(O instanceof Error ? O.message : String(O));
      } finally {
        M(!1);
      }
    }
  }, q = (O) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(O)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, J = async () => {
    if (x.length === 1 && c > 1) {
      u(c - 1);
      return;
    }
    await W();
  }, Z = () => j(/* @__PURE__ */ new Set()), R = (O, te) => {
    j((ge) => {
      const me = new Set(ge);
      return te ? me.add(O) : me.delete(O), me;
    });
  }, X = (O) => {
    j((te) => {
      const ge = new Set(te);
      for (const me of k)
        O ? ge.add(me) : ge.delete(me);
      return ge;
    });
  }, ue = (O) => {
    a(O), u(1), Z();
  }, ce = (O) => {
    r(O), u(1), Z();
  }, ee = async (O) => {
    if (await Zs().confirm({ message: `Delete workflow definition "${O.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" })) {
      v(""), m("");
      try {
        await uh(e, O.id), R(O.id, !1), v(`Deleted ${O.name}`), await J();
      } catch (te) {
        m(te instanceof Error ? te.message : String(te));
      }
    }
  }, ne = async (O) => {
    v(""), m("");
    try {
      await dh(e, O.id), R(O.id, !1), v(`Restored ${O.name}`), await J();
    } catch (te) {
      m(te instanceof Error ? te.message : String(te));
    }
  }, fe = async (O) => {
    if (await Zs().confirm({ message: `Permanently delete workflow definition "${O.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) {
      v(""), m("");
      try {
        await fh(e, O.id), R(O.id, !1), v(`Permanently deleted ${O.name}`), await J();
      } catch (te) {
        m(te instanceof Error ? te.message : String(te));
      }
    }
  };
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ o.jsx("button", { type: "button", className: s === "active" ? "active" : "", "aria-selected": s === "active", onClick: () => ue("active"), children: "Active" }),
        /* @__PURE__ */ o.jsx("button", { type: "button", className: s === "deleted" ? "active" : "", "aria-selected": s === "deleted", onClick: () => ue("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ o.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ o.jsx(rr, { size: 15 }),
        /* @__PURE__ */ o.jsx("input", { value: i, onChange: (O) => ce(O.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => {
        W();
      }, children: "Refresh" }),
      /* @__PURE__ */ o.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ o.jsxs("button", { type: "button", title: "Create workflow", onClick: Y, children: [
        /* @__PURE__ */ o.jsx(Qt, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ o.jsx(oi, { message: h, title: "Couldn't load workflow definitions" }) : null,
    f !== "failed" && h ? /* @__PURE__ */ o.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ o.jsx(bt, { size: 16 }),
      " ",
      h
    ] }) : null,
    w ? /* @__PURE__ */ o.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ o.jsx(ln, { size: 14 }),
      " ",
      w
    ] }) : null,
    N.size > 0 ? /* @__PURE__ */ o.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ o.jsxs("span", { children: [
        N.size,
        " selected"
      ] }),
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: Z, children: "Clear selection" })
    ] }) : null,
    f === "loading" ? /* @__PURE__ */ o.jsx(Ms, {}) : null,
    f === "ready" && x.length === 0 ? /* @__PURE__ */ o.jsx(
      Rs,
      {
        icon: /* @__PURE__ */ o.jsx(al, { size: 22 }),
        title: `No ${s} workflow definitions`,
        description: "Create a workflow to start designing automation, or adjust your filters to see more.",
        action: /* @__PURE__ */ o.jsxs("button", { type: "button", className: "wf-link-button", onClick: Y, children: [
          /* @__PURE__ */ o.jsx(Qt, { size: 15 }),
          " Create workflow"
        ] })
      }
    ) : null,
    f === "ready" && x.length > 0 ? /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ o.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ o.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ o.jsx(
            "input",
            {
              ref: A,
              type: "checkbox",
              checked: F,
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
        x.map((O) => /* @__PURE__ */ o.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${O.name}`,
            "aria-selected": N.has(O.id),
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
                  checked: N.has(O.id),
                  onChange: (te) => R(O.id, te.target.checked),
                  "aria-label": `Select workflow definition ${O.name}`
                }
              ) }),
              /* @__PURE__ */ o.jsxs("span", { children: [
                /* @__PURE__ */ o.jsx("strong", { children: O.name }),
                /* @__PURE__ */ o.jsx("small", { children: O.description || O.id })
              ] }),
              /* @__PURE__ */ o.jsx("span", { children: O.latestVersion ?? "No version" }),
              /* @__PURE__ */ o.jsx("span", { children: s === "deleted" ? Re(O.deletedAt) : O.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ o.jsx("span", { children: Re(O.lastModifiedAt) }),
              /* @__PURE__ */ o.jsx("span", { className: "wf-row-actions", onClick: (te) => te.stopPropagation(), children: s === "active" ? /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
                /* @__PURE__ */ o.jsx("button", { type: "button", onClick: (te) => {
                  te.stopPropagation(), n(O.id);
                }, children: "Open" }),
                /* @__PURE__ */ o.jsx("button", { type: "button", onClick: (te) => {
                  te.stopPropagation(), q(O.id);
                }, children: "Artifacts" }),
                P ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Rt(t, P, O), children: [
                  /* @__PURE__ */ o.jsx(ut, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ o.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  ee(O);
                }, children: [
                  /* @__PURE__ */ o.jsx($n, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
                /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
                  ne(O);
                }, children: [
                  /* @__PURE__ */ o.jsx(rs, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ o.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  fe(O);
                }, children: [
                  /* @__PURE__ */ o.jsx($n, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          O.id
        ))
      ] }),
      /* @__PURE__ */ o.jsx(
        XN,
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
      qN,
      {
        draft: S,
        creating: _,
        ai: t,
        suggestMetadataAction: D,
        onChange: (O) => E(O),
        onClose: () => E(null),
        onSubmit: V
      }
    ) : null
  ] });
}
function UN({ activities: e, activityCatalog: t, selectedEvidenceId: n = null, onSelectEvidence: i }) {
  const r = le(
    () => new Map(t.map((a) => [a.activityTypeKey, a])),
    [t]
  ), s = le(() => GN(e), [e]);
  return s.length === 0 ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty wf-timeline-empty", children: "No activity executions recorded yet." }) : /* @__PURE__ */ o.jsx("ol", { className: "wf-timeline", "aria-label": "Execution timeline", children: s.map((a) => {
    const c = r.get(a.activityType), u = fn(c), l = c ? Ce(c) : At(a.activityType) ?? a.activityType, d = At(a.activityType) ?? a.activityType, f = JN(a.startedAt ?? a.scheduledAt), p = ps(a.startedAt, a.completedAt);
    return /* @__PURE__ */ o.jsx("li", { children: /* @__PURE__ */ o.jsxs(
      "button",
      {
        type: "button",
        className: "wf-timeline-entry",
        "data-selected": a.activityExecutionId === n,
        onClick: () => i?.(a.activityExecutionId),
        children: [
          /* @__PURE__ */ o.jsx("span", { className: "wf-timeline-icon wf-activity-icon", "data-icon": u, "aria-hidden": "true", children: ur(u) }),
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
          /* @__PURE__ */ o.jsx(ZN, { status: a.status })
        ]
      }
    ) }, a.activityExecutionId);
  }) });
}
function ZN({ status: e }) {
  const t = e.toLowerCase(), n = t === "completed" ? "OK" : e;
  return /* @__PURE__ */ o.jsx("span", { className: "wf-status-badge wf-timeline-pill", "data-status": t, children: n });
}
function GN(e) {
  return e.map((t, n) => ({ activity: t, index: n })).sort((t, n) => Zc(t.activity) - Zc(n.activity) || t.index - n.index).map((t) => t.activity);
}
function Zc(e) {
  const t = e.startedAt ?? e.scheduledAt, n = t ? Date.parse(t) : Number.NaN;
  return Number.isNaN(n) ? Number.MAX_SAFE_INTEGER : n;
}
function JN(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? "" : t.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: !1 });
}
function QN({ context: e }) {
  const [t, n] = K("loading"), [i, r] = K(""), [s, a] = K(""), [c, u] = K(""), [l, d] = K([]), f = se(async () => {
    n("loading"), r("");
    try {
      const h = await wh(e, {
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
    t === "failed" ? /* @__PURE__ */ o.jsx(oi, { message: i }) : null,
    t === "loading" ? /* @__PURE__ */ o.jsx(Ms, {}) : null,
    t === "ready" && l.length === 0 ? /* @__PURE__ */ o.jsx(
      Rs,
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
            /* @__PURE__ */ o.jsx("span", { children: Ud(h.runKind) }),
            /* @__PURE__ */ o.jsx("span", { children: /* @__PURE__ */ o.jsx(hn, { status: h.status, subStatus: h.subStatus }) }),
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
            /* @__PURE__ */ o.jsx("span", { children: ps(h.startedAt ?? h.createdAt, h.completedAt ?? h.updatedAt) })
          ]
        },
        h.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function eS({ context: e, ai: t, workflowExecutionId: n }) {
  const [i, r] = K("loading"), [s, a] = K(""), [c, u] = K(null), [l, d] = K(null), [f, p] = K([]), {
    inspectorWidth: h,
    inspectorCollapsed: m,
    maximizedSidePanel: w,
    inspectorExpanded: v,
    editorBodyStyle: x,
    toggleSidePanelCollapsed: b,
    toggleSidePanelMaximized: g,
    startSidePanelResize: y,
    handleSidePanelResizeKeyDown: N
  } = of(), j = Mt(t, "weaver.workflows.explain-instance"), S = se(async () => {
    if (!n) {
      a("No workflow execution id was provided."), r("failed");
      return;
    }
    r("loading"), a("");
    try {
      const I = await vh(e, n), [$, z] = await Promise.all([
        ch(e, I.instance.definitionVersionId).then(
          (C) => ({ definitionVersion: C, error: "" }),
          (C) => ({ definitionVersion: null, error: C instanceof Error ? C.message : String(C) })
        ),
        cr(e)
      ]);
      u({
        details: I,
        definitionVersion: $.definitionVersion,
        definitionVersionError: $.error,
        activityCatalog: z.activities
      }), d(null), p([]), r("ready");
    } catch (I) {
      u(null), a(_j(I, n)), r("failed");
    }
  }, [e, n]);
  Q(() => {
    S();
  }, [S]);
  const E = () => {
    window.history.pushState({}, "", "/workflows/instances"), window.dispatchEvent(new PopStateEvent("popstate"));
  }, _ = () => {
    const I = c?.details.instance.definitionId;
    I && (window.history.pushState({}, "", `/workflows/definitions?definition=${encodeURIComponent(I)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  }, M = [
    "wf-instance-detail-workbench",
    m ? "inspector-collapsed" : "",
    w === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: E, children: [
        /* @__PURE__ */ o.jsx(Mn, { size: 14 }),
        " Runs"
      ] }),
      c?.details.instance.definitionId ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: _, children: [
        /* @__PURE__ */ o.jsx(cl, { size: 14 }),
        " Designer"
      ] }) : null,
      /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
        S();
      }, children: [
        /* @__PURE__ */ o.jsx(rs, { size: 14 }),
        " Refresh"
      ] }),
      c && j ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Rt(t, j, c.details), children: [
        /* @__PURE__ */ o.jsx(ut, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    i === "loading" ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Loading workflow run..." }) : null,
    i === "failed" ? /* @__PURE__ */ o.jsx(oi, { message: s }) : null,
    i === "ready" && c ? /* @__PURE__ */ o.jsxs("div", { className: M, style: x, children: [
      /* @__PURE__ */ o.jsx(
        tS,
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
      v && !w ? /* @__PURE__ */ o.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize run details panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Yt,
          "aria-valuemax": Ut,
          "aria-valuenow": h,
          tabIndex: 0,
          onPointerDown: (I) => y("inspector", I),
          onKeyDown: (I) => N("inspector", I)
        }
      ) : /* @__PURE__ */ o.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ o.jsx(
        rS,
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
          graphNodeIds: c.definitionVersion ? iS(c.definitionVersion, c.activityCatalog) : void 0,
          rootNodeId: c.definitionVersion?.state.rootActivity?.nodeId,
          collapsed: m,
          expanded: v,
          maximized: w === "inspector",
          onToggleCollapsed: () => b("inspector"),
          onToggleMaximized: () => g("inspector")
        }
      )
    ] }) : null
  ] });
}
function tS({
  definitionVersion: e,
  definitionVersionError: t,
  activityCatalog: n,
  details: i,
  selectedEvidenceId: r,
  onSelectEvidence: s,
  frames: a,
  onNavigateToScope: c
}) {
  const u = nS(a), l = le(() => {
    if (!e) return { nodes: [], edges: [] };
    const d = e.state.rootActivity;
    if (!d) return { nodes: [], edges: [] };
    const f = Ln(d, a, n), p = f?.owner ?? d, h = n.find((x) => x.activityVersionId === p.activityVersionId), w = jl(p, h) === "unsupported" || !f ? bl(p, n, e.layout) : vl(f, n, e.layout), v = w.nodes.map((x) => ({
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
      nodes: Sp(v, i.activities, i.incidents, r),
      edges: w.edges.map((x) => ({ ...x, deletable: !1 }))
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
      /* @__PURE__ */ o.jsx(hn, { status: i.instance.status, subStatus: i.instance.subStatus })
    ] }),
    e ? /* @__PURE__ */ o.jsxs("div", { className: "wf-breadcrumb wf-instance-breadcrumb", children: [
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => c([]), children: "Root" }),
      a.map((d, f) => /* @__PURE__ */ o.jsxs("span", { className: "wf-breadcrumb-segment", children: [
        /* @__PURE__ */ o.jsx(yt, { size: 13 }),
        /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => c(a.slice(0, f + 1)), children: d.label })
      ] }, `${d.ownerNodeId}-${d.slotId}-${f}`))
    ] }) : null,
    /* @__PURE__ */ o.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ o.jsxs("div", { className: "wf-empty", children: [
        "The workflow run loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ o.jsx("small", { children: Aj(t) }) : null
      ] }),
      e && l.nodes.length === 0 ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      l.nodes.length > 0 ? /* @__PURE__ */ o.jsxs(
        Dd,
        {
          nodes: l.nodes,
          edges: l.edges,
          nodeTypes: ef,
          edgeTypes: tf,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (d, f) => s(f.id),
          onPaneClick: () => s(null),
          children: [
            /* @__PURE__ */ o.jsx($d, {}),
            /* @__PURE__ */ o.jsx(Ld, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ o.jsx(Md, {})
          ]
        },
        u
      ) : null
    ] })
  ] });
}
function nS(e) {
  return e.length === 0 ? "root" : e.map((t) => `${t.ownerNodeId}:${t.slotId}`).join("/");
}
function iS(e, t) {
  const n = /* @__PURE__ */ new Set(), i = (r) => {
    if (r) {
      n.add(r.nodeId);
      for (const s of Le(r, t))
        s.activities.forEach(i);
    }
  };
  return i(e.state.rootActivity), n;
}
function rS({
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
  onToggleCollapsed: w,
  onToggleMaximized: v
}) {
  const [x, b] = K("timeline");
  if (!i)
    return /* @__PURE__ */ o.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Select a workflow run to inspect its timeline." }) });
  const g = r?.incidents.length ?? 0, y = oS(r?.activities ?? [], c), N = (S) => {
    u?.(S), b("activity");
  }, j = [
    { id: "timeline", title: "Timeline", order: 0, icon: /* @__PURE__ */ o.jsx(es, { size: 14 }), render: () => null },
    { id: "activity", title: "Activity", order: 1, icon: /* @__PURE__ */ o.jsx(rl, { size: 14 }), render: () => null },
    { id: "issues", title: g > 0 ? `Issues (${g})` : "Issues", order: 2, icon: /* @__PURE__ */ o.jsx(bt, { size: 14 }), render: () => null },
    { id: "details", title: "Details", order: 3, icon: /* @__PURE__ */ o.jsx(ns, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ o.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Run details panel", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-panel-title wf-instance-panel-title", children: [
      /* @__PURE__ */ o.jsx(Wi, { label: "Run details tabs", tabs: j, activeTabId: x, onSelect: (S) => b(S) }),
      /* @__PURE__ */ o.jsxs("span", { className: "wf-panel-actions", children: [
        /* @__PURE__ */ o.jsx(
          "button",
          {
            type: "button",
            className: "wf-panel-action-button",
            "aria-label": p ? "Expand run details panel" : "Collapse run details panel",
            title: p ? "Expand" : "Collapse",
            onClick: w,
            children: p ? /* @__PURE__ */ o.jsx(Mn, { size: 14 }) : /* @__PURE__ */ o.jsx(yt, { size: 14 })
          }
        ),
        p ? null : /* @__PURE__ */ o.jsx(
          "button",
          {
            type: "button",
            className: "wf-panel-action-button",
            "aria-label": m ? "Restore run details panel" : "Maximize run details panel",
            title: m ? "Restore" : "Maximize",
            onClick: v,
            children: m ? /* @__PURE__ */ o.jsx(Eo, { size: 14 }) : /* @__PURE__ */ o.jsx(Pn, { size: 14 })
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
        n ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Rt(t, n, r ?? i), children: [
          /* @__PURE__ */ o.jsx(ut, { size: 13 }),
          " Explain"
        ] }) : null
      ] }),
      s === "loading" ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Loading run details..." }) : null,
      s === "failed" ? /* @__PURE__ */ o.jsx(oi, { message: a }) : null,
      s === "ready" && r ? /* @__PURE__ */ o.jsx("div", { className: "wf-instance-tab-content", children: x === "timeline" ? /* @__PURE__ */ o.jsx(
        UN,
        {
          activities: r.activities,
          activityCatalog: f,
          selectedEvidenceId: c,
          onSelectEvidence: N
        }
      ) : x === "activity" ? /* @__PURE__ */ o.jsx(sS, { context: e, activity: y, activityCatalog: f }) : x === "issues" ? /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
        /* @__PURE__ */ o.jsx(xS, { incidents: r.incidents, selectedEvidenceId: c, onSelectEvidence: u }),
        /* @__PURE__ */ o.jsx(NS, { details: r, graphNodeIds: l, rootNodeId: d })
      ] }) : /* @__PURE__ */ o.jsxs("dl", { className: "wf-instance-meta", children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ o.jsx("dd", { children: /* @__PURE__ */ o.jsx(hn, { status: i.status, subStatus: i.subStatus }) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Run Kind" }),
        /* @__PURE__ */ o.jsx("dd", { children: Ud(i.runKind) }),
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
function oS(e, t) {
  if (!t) return null;
  const n = e.find((r) => r.activityExecutionId === t);
  if (n) return n;
  const i = e.filter((r) => r.executableNodeId === t || r.authoredActivityId === t);
  return i.length > 0 ? Il(i) : null;
}
function sS({ context: e, activity: t, activityCatalog: n }) {
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
    return a({ activityExecutionId: p, status: "loading", inspection: null, error: "" }), bh(e, r, p).then(
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
  const u = n.find((f) => f.activityTypeKey === t.activityType)?.displayName || At(t.activityType) || t.activityType, l = t.bookmarkIds?.length ?? 0, d = t.incidentIds?.length ?? 0;
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsxs("section", { className: "wf-instance-section", children: [
      /* @__PURE__ */ o.jsx("h4", { children: "Activity" }),
      /* @__PURE__ */ o.jsxs("dl", { className: "wf-instance-meta", children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Name" }),
        /* @__PURE__ */ o.jsx("dd", { children: u }),
        /* @__PURE__ */ o.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ o.jsx("dd", { children: /* @__PURE__ */ o.jsx(hn, { status: t.status, subStatus: t.subStatus }) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Activity Execution ID" }),
        /* @__PURE__ */ o.jsx("dd", { children: t.activityExecutionId }),
        /* @__PURE__ */ o.jsx("dt", { children: "Authored Activity ID" }),
        /* @__PURE__ */ o.jsx("dd", { children: t.authoredActivityId }),
        /* @__PURE__ */ o.jsx("dt", { children: "Type" }),
        /* @__PURE__ */ o.jsxs("dd", { children: [
          At(t.activityType) ?? t.activityType,
          " ",
          /* @__PURE__ */ o.jsx("small", { children: t.activityTypeVersion })
        ] }),
        /* @__PURE__ */ o.jsx("dt", { children: "Started" }),
        /* @__PURE__ */ o.jsx("dd", { children: Re(t.startedAt) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Completed" }),
        /* @__PURE__ */ o.jsx("dd", { children: Re(t.completedAt) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Duration" }),
        /* @__PURE__ */ o.jsx("dd", { children: ps(t.startedAt, t.completedAt) || "Unknown" }),
        /* @__PURE__ */ o.jsx("dt", { children: "Bookmarks" }),
        /* @__PURE__ */ o.jsx("dd", { children: l }),
        /* @__PURE__ */ o.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ o.jsx("dd", { children: d })
      ] })
    ] }),
    /* @__PURE__ */ o.jsx(
      Gc,
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
      Gc,
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
function Gc({
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
    /* @__PURE__ */ o.jsx("div", { className: "wf-runtime-input-list", children: a.map((c) => /* @__PURE__ */ o.jsx(aS, { snapshot: c }, `${c.name}:${c.capturedAt}:${c.captureMode}`)) })
  ] });
}
function aS({ snapshot: e }) {
  const t = e.type?.displayName || e.type?.typeName || e.type?.alias || "Unknown", n = e.snapshot ?? (e.captureMode === "DiagnosticSnapshot" ? e.payload : null), i = e.captureMode === "Payload" && e.payload !== void 0;
  return /* @__PURE__ */ o.jsxs("article", { className: "wf-runtime-input", children: [
    /* @__PURE__ */ o.jsxs("header", { children: [
      /* @__PURE__ */ o.jsxs("span", { children: [
        /* @__PURE__ */ o.jsx("strong", { children: e.name }),
        /* @__PURE__ */ o.jsx("small", { children: t })
      ] }),
      /* @__PURE__ */ o.jsx("span", { className: "wf-runtime-capture-mode", children: fS(e.captureMode) })
    ] }),
    gS(n) ? /* @__PURE__ */ o.jsx(Vs, { node: n }) : i ? /* @__PURE__ */ o.jsx(dS, { payload: e.payload }) : /* @__PURE__ */ o.jsx("p", { children: hS(e) }),
    e.isSensitive ? /* @__PURE__ */ o.jsx("p", { className: "wf-instance-note", children: "Marked sensitive by runtime evidence." }) : null
  ] });
}
function Vs({ node: e, depth: t = 0 }) {
  switch (e.kind) {
    case "null":
      return /* @__PURE__ */ o.jsx("code", { className: "wf-runtime-input-value", children: "null" });
    case "scalar":
    case "number":
      return /* @__PURE__ */ o.jsx("code", { className: "wf-runtime-input-value", children: gf(e.value) });
    case "string":
      return /* @__PURE__ */ o.jsxs("code", { className: "wf-runtime-input-value", children: [
        e.preview ?? "",
        e.truncated ? ` (${e.length ?? "unknown"} chars, truncated)` : ""
      ] });
    case "object":
      return /* @__PURE__ */ o.jsx(cS, { node: e, depth: t });
    case "array":
      return /* @__PURE__ */ o.jsx(lS, { node: e, depth: t });
    case "redacted":
    case "truncated":
    case "unsupported":
    case "error":
    case "permissionHidden":
      return /* @__PURE__ */ o.jsx(Jc, { node: e });
    case "payloadReference":
      return /* @__PURE__ */ o.jsx(uS, { node: e });
    default:
      return /* @__PURE__ */ o.jsx(Jc, { node: { kind: "unsupported", reason: `Unknown snapshot node: ${e.kind}` } });
  }
}
function cS({ node: e, depth: t }) {
  const n = e.properties ?? [];
  return n.length === 0 ? /* @__PURE__ */ o.jsx("code", { className: "wf-runtime-input-value", children: "{}" }) : /* @__PURE__ */ o.jsxs("details", { className: "wf-runtime-snapshot-node", open: t === 0, children: [
    /* @__PURE__ */ o.jsxs("summary", { children: [
      e.typeName || "Object",
      e.truncated ? " (truncated)" : ""
    ] }),
    /* @__PURE__ */ o.jsx("div", { className: "wf-runtime-snapshot-children", children: n.map((i) => /* @__PURE__ */ o.jsxs("div", { className: "wf-runtime-snapshot-property", children: [
      /* @__PURE__ */ o.jsx("span", { children: i.name }),
      /* @__PURE__ */ o.jsx(Vs, { node: i.value, depth: t + 1 })
    ] }, i.name)) })
  ] });
}
function lS({ node: e, depth: t }) {
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
      /* @__PURE__ */ o.jsx(Vs, { node: i, depth: t + 1 })
    ] }, r)) })
  ] });
}
function Jc({ node: e }) {
  const t = e.message || e.reason || e.requiredPermission || e.displayName;
  return /* @__PURE__ */ o.jsxs("span", { className: `wf-runtime-snapshot-marker ${e.kind}`, children: [
    pS(e.kind),
    t ? `: ${t}` : "",
    e.omittedCount ? ` (${e.omittedCount} omitted)` : ""
  ] });
}
function uS({ node: e }) {
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
function dS({ payload: e }) {
  const t = gf(e);
  return t.length <= 160 && !t.includes(`
`) ? /* @__PURE__ */ o.jsx("code", { className: "wf-runtime-input-value", children: t }) : /* @__PURE__ */ o.jsxs("details", { className: "wf-runtime-input-value-details", children: [
    /* @__PURE__ */ o.jsx("summary", { children: mS(t) }),
    /* @__PURE__ */ o.jsx("pre", { children: t })
  ] });
}
function fS(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2");
}
function pS(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2");
}
function hS(e) {
  return e.state === "permissionHidden" ? "Runtime value evidence is hidden by permissions." : e.state === "metadataOnly" ? e.captureReason || "Runtime value evidence is metadata-only." : e.state === "notCaptured" ? e.captureReason || "Runtime value evidence was not captured." : e.captureReason || "The runtime capture policy did not include this value.";
}
function gS(e) {
  return typeof e == "object" && e !== null && typeof e.kind == "string";
}
function mS(e) {
  const t = e.split(`
`, 1)[0] || e;
  return t.length > 120 ? `${t.slice(0, 117)}...` : t;
}
function gf(e) {
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
const yS = [
  "runtime.faultStackTrace",
  "runtime.exceptionStackTrace",
  "runtime.stackTrace",
  "faultStackTrace",
  "exceptionStackTrace",
  "stackTrace"
];
function xS({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
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
              value: SS(s),
              ariaLabel: `Copy incident ${s.failureType}`,
              copiedLabel: "incident",
              onCopied: (a) => r(`Copied ${a}`),
              onCopyFailed: (a) => r(`Could not copy ${a}.`)
            }
          ),
          /* @__PURE__ */ o.jsx(wS, { incident: s })
        ]
      },
      s.incidentId
    )),
    i ? /* @__PURE__ */ o.jsx("p", { className: "wf-copy-status", role: "status", children: i }) : null
  ] });
}
function wS({ incident: e }) {
  const t = vS(e);
  return t ? /* @__PURE__ */ o.jsxs("details", { className: "wf-incident-stacktrace", children: [
    /* @__PURE__ */ o.jsx("summary", { children: jS(t) }),
    /* @__PURE__ */ o.jsx("pre", { children: t })
  ] }) : null;
}
function vS(e) {
  const t = bS(e.stackTrace, e.exceptionStackTrace);
  if (t) return t;
  for (const n of yS) {
    const i = e.metadata?.[n];
    if (i && i.trim()) return i;
  }
  return null;
}
function bS(...e) {
  return e.find((t) => t?.trim()) ?? null;
}
function jS(e) {
  const n = (e.split(`
`).find((i) => i.trim()) ?? e).trim();
  return n.length > 120 ? `${n.slice(0, 117)}...` : n;
}
function NS({ details: e, graphNodeIds: t, rootNodeId: n }) {
  if (!t) return null;
  const i = e.activities.filter((r) => {
    const s = bj(r);
    return s && s !== n && !t.has(s);
  });
  return i.length === 0 ? null : /* @__PURE__ */ o.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ o.jsx("h4", { children: "Executions outside canvas" }),
    /* @__PURE__ */ o.jsx("div", { className: "wf-instance-unmatched-list", children: i.map((r) => /* @__PURE__ */ o.jsxs("div", { className: "wf-instance-unmatched", children: [
      /* @__PURE__ */ o.jsx("strong", { children: At(r.activityType) ?? r.activityType }),
      /* @__PURE__ */ o.jsx("small", { children: r.activityExecutionId })
    ] }, `activity-${r.activityExecutionId}`)) })
  ] });
}
function SS(e) {
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
function CS({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: i,
  workflowDesignerPanels: r,
  autosaveEnabledByDefault: s
}) {
  const [a, c] = K(Qc);
  Q(() => {
    const l = () => c(Qc());
    return window.addEventListener("popstate", l), () => window.removeEventListener("popstate", l);
  }, []);
  const u = (l) => {
    const d = l ? `/workflows/definitions?definition=${encodeURIComponent(l)}` : "/workflows/definitions";
    window.history.pushState({}, "", d), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return a ? /* @__PURE__ */ o.jsx(KN, { context: e, definitionId: a, ai: t, propertyEditors: n, expressionEditors: i, workflowDesignerPanels: r, autosaveEnabledByDefault: s, onBack: () => u(null) }) : /* @__PURE__ */ o.jsx(Nr, { title: "Definitions", children: /* @__PURE__ */ o.jsx(YN, { context: e, ai: t, onOpen: u }) });
}
function kS({ context: e, ai: t }) {
  const [n, i] = K(el);
  Q(() => {
    const s = () => i(el());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const r = se((s) => {
    const a = s?.trim() ?? "", c = new URL(window.location.href);
    a ? c.searchParams.set("definition", a) : c.searchParams.delete("definition"), i(a || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ o.jsx(Nr, { title: "Executables", children: /* @__PURE__ */ o.jsx(Fj, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: r }) });
}
function ES({ context: e }) {
  return /* @__PURE__ */ o.jsx(Nr, { title: "Runs", children: /* @__PURE__ */ o.jsx(QN, { context: e }) });
}
function IS({ context: e, ai: t }) {
  const n = AS();
  return /* @__PURE__ */ o.jsx(Nr, { title: "Run", children: /* @__PURE__ */ o.jsx(eS, { context: e, ai: t, workflowExecutionId: n }) });
}
function Nr({ title: e, children: t }) {
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ o.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ o.jsxs("div", { children: [
      /* @__PURE__ */ o.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ o.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function Qc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function el() {
  return new URLSearchParams(window.location.search).get("definition");
}
function AS() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function PS(e) {
  Ff(e.dialogs), e.featureAreas.add({
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
        component: () => /* @__PURE__ */ o.jsx(CS, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list(), autosaveEnabledByDefault: e.runtime.workflows?.autosaveEnabledByDefault ?? !0 })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ o.jsx(kS, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => /* @__PURE__ */ o.jsx(ES, { context: e.backend })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => /* @__PURE__ */ o.jsx(IS, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => /* @__PURE__ */ o.jsx(hg, { context: e.backend })
      },
      {
        id: "workflows-runtime-diagnostics",
        path: "/workflows/runtime-diagnostics",
        label: "Runtime diagnostics",
        component: () => /* @__PURE__ */ o.jsx(yg, { context: e.backend })
      }
    ]
  });
}
export {
  Nj as isConnectEndOverExistingWorkflowNode,
  PS as register,
  Sj as resolveConnectEndSource
};
