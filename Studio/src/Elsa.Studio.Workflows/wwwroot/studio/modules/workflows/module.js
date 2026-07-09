import Qe, { useMemo as de, useState as B, useEffect as J, memo as je, forwardRef as Rc, useRef as oe, useCallback as re, useContext as On, createContext as Ho, useLayoutEffect as Yd, lazy as qd, Suspense as Ud, useReducer as Zd, useId as zc } from "react";
import { ListChecks as Gd, Save as Lc, EyeOff as _s, Shield as Ds, AlertTriangle as Ti, SlidersHorizontal as Wo, Activity as Vc, Search as Zi, Check as an, Boxes as Hn, Zap as Jd, Play as Ut, Terminal as Qd, ListTree as Fo, GitBranch as Oc, Plus as Zt, Trash2 as In, AlertCircle as yt, Wrench as ef, Copy as tf, X as Hc, Sparkles as at, RotateCcw as Bo, ChevronDown as Wc, ChevronRight as vt, GripVertical as Fc, Maximize2 as An, ChevronUp as nf, Package as Bc, Undo2 as rf, Redo2 as of, Network as sf, Download as af, ChevronLeft as _n, Minimize2 as yo, Workflow as Kc, Code2 as cf } from "lucide-react";
import { useQuery as Xc, useQueryClient as lf, useMutation as uf } from "@tanstack/react-query";
import { useTablistKeyboard as df } from "@elsa-workflows/studio-ui";
function ff(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var Gr = { exports: {} }, pn = {};
var Ts;
function pf() {
  if (Ts) return pn;
  Ts = 1;
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
  return pn.Fragment = t, pn.jsx = n, pn.jsxs = n, pn;
}
var $s;
function hf() {
  return $s || ($s = 1, Gr.exports = pf()), Gr.exports;
}
var o = hf();
let Yc;
function gf(e) {
  Yc = e;
}
function Ps() {
  return Yc;
}
const yf = "String", mf = "singleline";
function xf(e) {
  return e === "Single" || e === "Array" || e === "List" || e === "HashSet";
}
function Ko(e, t = "Single") {
  return { alias: (e ?? "").trim() || yf, collectionKind: t };
}
function qc(e) {
  const t = e.type ?? e.Type;
  if ($i(t))
    return { alias: typeof t.alias == "string" ? t.alias : typeof t.typeName == "string" ? t.typeName : "", collectionKind: xf(t.collectionKind) ? t.collectionKind : "Single" };
  const n = e.typeInformation ?? e.TypeInformation;
  return $i(n) ? { alias: typeof n.typeName == "string" ? n.typeName : "", collectionKind: Ms(e) ? "Array" : "Single" } : { alias: typeof t == "string" ? t : "", collectionKind: Ms(e) ? "Array" : "Single" };
}
function Ms(e) {
  const t = e.isArray ?? e.IsArray;
  return t === !0 || t === "true";
}
function Rs(e) {
  return (e.alias ?? e.typeName ?? "").trim();
}
function Gi() {
  return typeof crypto < "u" && typeof crypto.randomUUID == "function" ? crypto.randomUUID() : `id-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
}
function wf(e, t) {
  const n = new Set(t);
  let i = 1, r = `${e}${i}`;
  for (; n.has(r); )
    i += 1, r = `${e}${i}`;
  return r;
}
function vf(e) {
  return {
    referenceKey: Gi(),
    name: e.name,
    type: Ko(e.alias),
    storageDriverType: e.storageDriver?.trim() || null,
    default: null
  };
}
function bf(e, t) {
  return { ...e, ...t };
}
function Nf(e) {
  return e === "" ? null : { value: e, expressionType: "Literal" };
}
function jf(e) {
  return e == null || e.value == null ? "" : typeof e.value == "object" ? JSON.stringify(e.value) : String(e.value);
}
function Sf(e) {
  return {
    referenceKey: Gi(),
    name: e.name,
    type: Ko(e.alias),
    displayName: e.name,
    description: "",
    category: "",
    uiHint: mf,
    storageDriverType: e.storageDriver ?? null,
    isRequired: !1
  };
}
function Cf(e, t) {
  return { ...e, ...t };
}
function Ef(e) {
  return {
    referenceKey: Gi(),
    name: e.name,
    type: Ko(e.alias),
    displayName: e.name,
    description: "",
    category: ""
  };
}
function kf(e, t) {
  return { ...e, ...t };
}
function If(e) {
  const t = e.split(",")[0].trim();
  return (t.split(".").pop() ?? t).split("`")[0];
}
function Uc(e, t) {
  return e && !e.includes(",") && !e.includes(".") ? e : If(e || t);
}
function Af(e, t) {
  return Uc(e, t).replace(/StorageDriver$/, "");
}
function $i(e) {
  return !!e && typeof e == "object" && !Array.isArray(e);
}
function Sn(e, t) {
  for (const n of t) {
    const i = e[n];
    if (i != null && typeof i != "object") return String(i);
  }
  return "";
}
const _f = ["name", "Name"], Zc = ["name", "Name"], Df = ["storageDriverType", "StorageDriverType"], Gc = ["referenceKey", "ReferenceKey"], Tf = /* @__PURE__ */ new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);
function Wn(e) {
  return Jc(e, Rf);
}
function Ji(e) {
  return Jc(e, zf);
}
function Jc(e, t) {
  if (!e) return e;
  const n = { ...e };
  return e.rootActivity && (n.rootActivity = mo(e.rootActivity, t)), Array.isArray(e.variables) && (n.variables = bi(e.variables, xo)), Array.isArray(e.inputs) && (n.inputs = bi(e.inputs, xo)), Array.isArray(e.outputs) && (n.outputs = bi(e.outputs, (i) => Qc(i, !1))), n;
}
function mo(e, t) {
  const n = t(e), i = n.structure;
  if (!i || !et(i.payload)) return n;
  let r = !1;
  const s = { ...i.payload };
  for (const [a, c] of Object.entries(i.payload))
    Ls(c) ? (s[a] = mo(c, t), r = !0) : Array.isArray(c) && c.length > 0 && c.every(Ls) && (s[a] = c.map((u) => mo(u, t)), r = !0);
  return Array.isArray(i.payload.variables) && i.payload.variables.length > 0 && (s.variables = bi(i.payload.variables, xo), r = !0), r ? { ...n, structure: { ...i, payload: s } } : n;
}
function bi(e, t) {
  return e.map((n) => et(n) && !Array.isArray(n) ? t(n) : n);
}
function xo(e) {
  return Qc(e, !0);
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
function Qc(e, t) {
  const n = Pf(e, $f);
  return Sn(e, Gc).trim() || (n.referenceKey = Gi()), n.type = qc(e), t && (n.storageDriverType = Mf(e.storageDriverType ?? e.StorageDriverType)), n;
}
function Pf(e, t) {
  const n = new Set(t), i = {};
  for (const [r, s] of Object.entries(e))
    n.has(r) || (i[r] = s);
  return i;
}
function Mf(e) {
  if (typeof e == "string") return e.trim() ? e : null;
  if (et(e)) {
    const t = typeof e.typeName == "string" ? e.typeName : "";
    if (!t) return null;
    const n = typeof e.namespace == "string" ? e.namespace : "";
    return n ? `${n}.${t}` : t;
  }
  return null;
}
function Rf(e) {
  const t = [], n = {};
  for (const [r, s] of Object.entries(e))
    Tf.has(r) || (Hf(s) ? t.push({
      referenceKey: Lf(r),
      value: Of(s.expression)
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
function zf(e) {
  const t = Array.isArray(e.inputs) ? e.inputs : [], n = {};
  for (const i of t) {
    if (!et(i) || typeof i.referenceKey != "string") continue;
    const r = et(i.value) ? i.value : {};
    n[Vf(i.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof r.expressionType == "string" ? r.expressionType : "Literal",
        value: r.value ?? ""
      }
    };
  }
  return { ...e, ...n, inputs: [] };
}
function Lf(e) {
  return e && e.charAt(0).toUpperCase() + e.slice(1);
}
function Vf(e) {
  return e && e.charAt(0).toLowerCase() + e.slice(1);
}
function Of(e) {
  const t = e.type || "Literal";
  return t === "Variable" && et(e.value) ? { value: e.value, expressionType: t } : t === "Literal" && et(e.value) ? { value: zs(e.value), expressionType: "Object" } : { value: zs(e.value), expressionType: t };
}
function zs(e) {
  return e == null ? null : typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function Hf(e) {
  if (!et(e) || Array.isArray(e)) return !1;
  const t = e.expression;
  return et(t) && typeof t.type == "string";
}
function Ls(e) {
  return et(e) && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function et(e) {
  return typeof e == "object" && e !== null;
}
const Fn = "elsa.sequence.structure", cn = "elsa.flowchart.structure";
function el(e, t, n) {
  if (!e) return null;
  let i = e;
  for (const r of t) {
    const s = Ff(i, r.ownerNodeId, n);
    if (!s) return null;
    i = s;
  }
  return i;
}
function Wf(e, t, n = (r) => r.nodeId, i) {
  if (!e) return null;
  if (e.nodeId === t) return [];
  const r = (s, a) => {
    const c = Me(s, i);
    for (const u of c) {
      if (!u.activities.some((d) => d.nodeId === t)) continue;
      const l = a.at(-1);
      return l ? [...a.slice(0, -1), { ...l, slotId: u.id }] : u.id === c[0]?.id ? a : null;
    }
    for (const u of c)
      for (const l of u.activities) {
        const f = Me(l, i)[0]?.id ?? u.id, p = r(l, [...a, { ownerNodeId: l.nodeId, slotId: f, label: n(l) }]);
        if (p) return p;
      }
    return null;
  };
  return r(e, []);
}
function tl(e, t, n) {
  const i = Me(e, n), r = t.at(-1);
  return r ? i.find((s) => s.id === r.slotId) ?? null : i[0] ?? null;
}
function Gt(e, t, n) {
  const i = el(e, t, n);
  if (!i) return null;
  const r = tl(i, t, n);
  return r ? { owner: i, slot: r } : null;
}
function nl(e, t, n) {
  for (const i of Me(e, n)) {
    const r = i.activities.find((s) => s.nodeId === t);
    if (r) return { slot: i, child: r };
  }
  return null;
}
function Ff(e, t, n) {
  return nl(e, t, n)?.child ?? null;
}
function Me(e, t) {
  const n = e.structure;
  if (!n || !n.payload || typeof n.payload != "object") return [];
  const i = n.payload, r = Xo(il(e, t));
  if (r?.kind === n.kind) return Xf(n, r);
  const s = pp(n), a = mn(i.activities);
  return a ? [{
    id: `${n.kind}:activities`,
    label: hp(n),
    property: "activities",
    cardinality: "many",
    mode: s,
    activities: a
  }] : Object.entries(i).filter(([, c]) => mn(c) || Mi(c)).map(([c, u]) => ({
    id: `${n.kind}:${c}`,
    label: yp(c),
    property: c,
    cardinality: mn(u) ? "many" : "single",
    mode: "generic",
    activities: mn(u) ?? (Mi(u) ? [u] : [])
  }));
}
function Xo(e) {
  for (const t of e?.designFacets ?? []) {
    if (!tt(t)) continue;
    const n = typeof t.kind == "string" ? t.kind : "", i = typeof t.schemaVersion == "string" ? t.schemaVersion : "";
    if (!n || !i || !tt(t.payload)) continue;
    const r = Bf(t.payload);
    if (r) return { kind: n, schemaVersion: i, payload: r };
  }
  return null;
}
function Bf(e) {
  const t = e.mode;
  if (t !== "sequence" && t !== "flowchart" && t !== "generic" || typeof e.supportsScopedVariables != "boolean" || !Array.isArray(e.slots) || !tt(e.initialPayload)) return null;
  const n = e.slots.map(Kf).filter((i) => i !== null);
  return {
    mode: t,
    supportsScopedVariables: e.supportsScopedVariables,
    slots: n,
    initialPayload: e.initialPayload
  };
}
function Kf(e) {
  if (!tt(e)) return null;
  const t = typeof e.name == "string" ? e.name : "", n = typeof e.property == "string" ? e.property : "", i = typeof e.displayName == "string" ? e.displayName : "", r = e.cardinality;
  return !t || !n || !i || r !== "single" && r !== "many" ? null : {
    name: t,
    property: n,
    displayName: i,
    cardinality: r,
    collectionProperty: Ht(e.collectionProperty),
    childProperty: Ht(e.childProperty),
    labelProperty: Ht(e.labelProperty),
    slotNameTemplate: Ht(e.slotNameTemplate)
  };
}
function Xf(e, t) {
  return t.payload.slots.flatMap((n) => {
    if (n.collectionProperty && n.childProperty) {
      const i = n.childProperty, r = e.payload[n.collectionProperty];
      return Array.isArray(r) ? r.flatMap((s, a) => {
        if (!tt(s)) return [];
        const c = n.labelProperty ? Ht(s[n.labelProperty]) : void 0;
        return [Vs(e.kind, n, s[i], t.payload.mode, a, c)];
      }) : [];
    }
    return [Vs(e.kind, n, e.payload[n.property], t.payload.mode)];
  });
}
function Vs(e, t, n, i, r, s) {
  const a = r === void 0 ? "" : `:${t.collectionProperty}:${t.childProperty}:${r}`;
  return {
    id: `${e}:${t.property}${a}`,
    label: r === void 0 ? t.displayName : qf(t, r, s),
    property: t.property,
    cardinality: t.cardinality,
    mode: i,
    activities: Yf(n, t.cardinality),
    collectionProperty: t.collectionProperty,
    childProperty: t.childProperty,
    labelProperty: t.labelProperty,
    slotNameTemplate: t.slotNameTemplate,
    collectionIndex: r,
    collectionItemLabel: s
  };
}
function Yf(e, t) {
  return t === "many" ? mn(e) ?? [] : Mi(e) ? [e] : [];
}
function qf(e, t, n) {
  return e.slotNameTemplate ? e.slotNameTemplate.replaceAll("{name}", e.name).replaceAll("{displayName}", e.displayName).replaceAll("{label}", n ?? String(t + 1)).replaceAll("{index}", String(t + 1)) : n ? `${e.displayName}: ${n}` : `${e.displayName} ${t + 1}`;
}
function il(e, t) {
  if (t)
    return t instanceof Map ? t.get(e.activityVersionId) : Array.isArray(t) ? t.find((n) => n.activityVersionId === e.activityVersionId) : t.activityVersionId === e.activityVersionId ? t : void 0;
}
function Uf(e, t) {
  if (t.labelProperty && t.collectionItemLabel) {
    const n = e.map((i, r) => tt(i) && Ht(i[t.labelProperty]) === t.collectionItemLabel ? r : -1).filter((i) => i >= 0);
    if (n.length === 1) return n[0];
  }
  return typeof t.collectionIndex == "number" && t.collectionIndex >= 0 && t.collectionIndex < e.length ? t.collectionIndex : -1;
}
function rl(e, t, n) {
  const i = new Map(t.map((a) => [a.activityVersionId, a])), r = new Map(n.map((a) => [a.nodeId, a])), s = e.slot.activities.map((a, c) => {
    const u = i.get(a.activityVersionId), l = r.get(a.nodeId) ?? gp(e.slot.mode, c);
    return al(a, u, { x: l.x, y: l.y });
  });
  return {
    nodes: s,
    edges: e.slot.mode === "flowchart" ? ll(e.owner) : sp(e.slot, s)
  };
}
function wo(e, t, n) {
  const i = t.find((s) => s.activityVersionId === e.activityVersionId), r = n.find((s) => s.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [al(e, i, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function Zf(e, t, n, i = null) {
  const r = new Map(t.map((c) => [c.activityExecutionId, c])), s = Ws(t, (c) => c.authoredActivityId || c.executableNodeId), a = Ws(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? r.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const u = s.get(c.id) ?? [], l = a.get(c.id) ?? [];
    if (u.length === 0 && l.length === 0) return c;
    const d = dl(u), f = i === c.id || u.some((h) => h.activityExecutionId === i) || l.some((h) => h.incidentId === i), p = {
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
function Yo(e, t) {
  if (e?.structure?.kind === cn || np(t)) return "flowchart";
  if (e?.structure?.kind === Fn || ip(t)) return "sequence";
  if (e) {
    const n = Me(e, t)[0];
    if (n) return n.mode === "flowchart" ? "flowchart" : "sequence";
  }
  return "unsupported";
}
function Os(e, t, n, i) {
  return qo(e, t, i, (r) => {
    const s = tl(r, t, i);
    return s ? Bn(r, s, n) : r;
  });
}
function Gf(e, t, n, i) {
  return t.length === 0 ? n : qo(e, t, i, () => n);
}
function qo(e, t, n, i) {
  if (t.length === 0) return i(e);
  const [r, ...s] = t, a = nl(e, r.ownerNodeId, n);
  if (!a) return e;
  const c = qo(a.child, s, n, i);
  if (c === a.child) return e;
  const u = a.slot.activities.map((l) => l.nodeId === r.ownerNodeId ? c : l);
  return Bn(e, a.slot, u);
}
function ol(e, t, n, i) {
  if (e.nodeId === t) return n(e);
  const r = Me(e, i);
  if (r.length === 0) return e;
  let s = !1, a = e;
  for (const c of r) {
    const u = c.activities.map((l) => {
      const d = ol(l, t, n, i);
      return d !== l && (s = !0), d;
    });
    s && (a = Bn(a, c, u));
  }
  return s ? a : e;
}
function Bn(e, t, n) {
  if (!e.structure) return e;
  const i = t.cardinality === "single" ? n[0] ?? null : n;
  if (t.collectionProperty && t.childProperty) {
    const r = e.structure.payload[t.collectionProperty];
    if (!Array.isArray(r)) return e;
    const s = Uf(r, t);
    if (s < 0) return e;
    const a = r[s];
    if (!tt(a)) return e;
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
function Jf(e, t, n, i = []) {
  const r = new Map(e.slot.activities.map((a) => [a.nodeId, a]));
  for (const a of i)
    r.set(a.nodeId, a);
  const s = t.map((a) => r.get(a.id)).filter((a) => !!a);
  return e.slot.mode === "sequence" && s.sort((a, c) => {
    const u = t.find((d) => d.id === a.nodeId), l = t.find((d) => d.id === c.nodeId);
    return (u?.position.x ?? 0) - (l?.position.x ?? 0);
  }), Bn(e.owner, e.slot, s);
}
function Qf(e, t) {
  return {
    ...e,
    structure: op(e.structure, t)
  };
}
function ep(e, t) {
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
function vo(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: cl(e)
  };
}
function sl(e, t) {
  if (!e) return null;
  const n = il(e, t), i = e.structure ?? (n ? cl(n) : null);
  let r = i === e.structure ? e : { ...e, structure: i };
  const s = Me(r, t);
  for (const a of s) {
    const c = a.activities.map((u) => sl(u, t) ?? u);
    c.some((u, l) => u !== a.activities[l]) && (r = Bn(r, a, c));
  }
  return r;
}
function Se(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? rp(t) : n;
}
function al(e, t, n, i = {}) {
  return {
    id: e.nodeId,
    type: "workflowActivity",
    position: n,
    connectable: i.connectable,
    deletable: i.deletable,
    draggable: i.draggable,
    data: {
      label: t ? Se(t) : e.activityVersionId,
      activityVersionId: e.activityVersionId,
      activityTypeKey: t?.activityTypeKey,
      category: t?.category,
      executionType: t?.executionType,
      icon: Qi(t),
      childSlots: Me(e, t),
      acceptsInbound: ap(e, t),
      sourcePorts: i.suppressFlowPorts ? [] : ul(e, t),
      suppressFlowPorts: i.suppressFlowPorts
    }
  };
}
function Qi(e) {
  if (!e) return "activity";
  const t = tp(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), i = Se(e).toLowerCase(), r = e.category?.toLowerCase() ?? "", s = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || i === "flowchart" ? "flowchart" : n.endsWith(".sequence") || i === "sequence" ? "sequence" : n.includes("writeline") || i.includes("write line") ? "terminal" : r.includes("runtime") ? "runtime" : s === "trigger" ? "trigger" : "activity";
}
function tp(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function np(e) {
  return !!e && (Se(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function ip(e) {
  return !!e && (Se(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function rp(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function cl(e) {
  const t = Xo(e);
  return t ? {
    kind: t.kind,
    schemaVersion: t.schemaVersion,
    payload: dp(t.payload.initialPayload)
  } : e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: Fn,
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
  for (const r of n) {
    if (!tt(r)) continue;
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
function sp(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, i) => ({
    id: `sequence-${n.id}-${t[i + 1].id}`,
    source: n.id,
    target: t[i + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function ll(e) {
  if (e.structure?.kind !== cn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, i) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, s = n.target;
    if (!r?.nodeId || !s?.nodeId) return null;
    const a = Array.isArray(n.vertices) ? n.vertices.filter(fp) : [];
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
function ul(e, t) {
  const n = Hs(e.cases);
  if (lp(e, t) && n.length > 0)
    return [...n.map((s) => ({ name: s, displayName: s })), { name: "Default", displayName: "Default" }];
  const i = [
    ...Ni(t?.designFacets),
    ...Ni(t?.ports),
    ...Ni(t?.outputs)
  ];
  if (i.length > 0) return up(i);
  const r = Hs(e.outcomes);
  return r.length > 0 ? r.map((s) => ({ name: s, displayName: s })) : [{ name: "Done", displayName: "Done" }];
}
function ap(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function Pi(e, t, n, i) {
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
function cp(e, t, n) {
  const i = Pi(t.source, n, t.sourceHandle ?? "Done", void 0), r = Pi(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((s) => s.id !== t.id).concat(i, r);
}
function mn(e) {
  return Array.isArray(e) ? e.filter(Mi) : null;
}
function lp(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, i = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || i === "FlowSwitch";
}
function Ni(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!tt(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...Ni(n.ports));
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
function up(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function Hs(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function Ht(e) {
  return typeof e == "string" && e.length > 0 ? e : void 0;
}
function dp(e) {
  return JSON.parse(JSON.stringify(e));
}
function Ws(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e) {
    const r = t(i);
    r && n.set(r, [...n.get(r) ?? [], i]);
  }
  return n;
}
function dl(e) {
  return [...e].sort((t, n) => Fs(n).localeCompare(Fs(t)))[0];
}
function Fs(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function fp(e) {
  return tt(e) && typeof e.x == "number" && typeof e.y == "number";
}
function tt(e) {
  return typeof e == "object" && e !== null;
}
function Mi(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function pp(e) {
  return e.kind === Fn ? "sequence" : e.kind === cn ? "flowchart" : "generic";
}
function hp(e) {
  return e.kind === Fn || e.kind === cn, "Activities";
}
function gp(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function yp(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Ri = "workflow", mp = /* @__PURE__ */ new Set([Fn, cn]);
function xp(e, t) {
  const n = e?.structure?.kind;
  if (!n) return !1;
  if (mp.has(n)) return !0;
  const i = t?.activityVersionId === e?.activityVersionId ? Xo(t) : null;
  return i?.kind === n && i.payload.supportsScopedVariables;
}
function fl(e) {
  const t = e?.structure?.payload?.variables;
  return Array.isArray(t) ? t.filter($i) : [];
}
function wp(e, t) {
  return e.structure ? {
    ...e,
    structure: {
      ...e.structure,
      payload: { ...e.structure.payload, variables: t }
    }
  } : e;
}
function vp(e, t) {
  return {
    referenceKey: e,
    declaringScopeId: t && t !== Ri ? t : Ri
  };
}
function pl(e) {
  if (e && typeof e == "object") {
    const t = e;
    return typeof t.referenceKey == "string" && t.referenceKey ? { referenceKey: t.referenceKey, declaringScopeId: typeof t.declaringScopeId == "string" ? t.declaringScopeId : null } : null;
  }
  if (typeof e == "string") {
    const t = e.trim();
    if (t.startsWith("{"))
      try {
        return pl(JSON.parse(t));
      } catch {
      }
    return t ? { referenceKey: t, declaringScopeId: null } : null;
  }
  return null;
}
function bp(e, t) {
  if (!e) return "";
  const n = [`workflow:${Bs(e.variables)}`], i = (r) => {
    const s = Me(r, t), a = s.flatMap((c) => c.activities.map((u) => u.nodeId));
    n.push(`${r.nodeId}:${Bs(fl(r))}>${a.join(",")}`), s.forEach((c) => c.activities.forEach(i));
  };
  return e.rootActivity && i(e.rootActivity), n.join(";");
}
function Bs(e) {
  return (e ?? []).map((t) => `${t.referenceKey}=${t.name}`).join(",");
}
function Np(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e ?? [])
    i.scopeId === t && n.set(i.referenceKey, `Shadows "${i.name}" declared in an outer scope.`);
  return n;
}
const Ne = "/_elsa/workflow-management", jp = "/publishing", Cn = {
  activities: ["workflows", "activities"],
  activityAvailabilitySettings: ["workflows", "activity-availability", "settings"],
  activityAvailabilityDiagnostics: ["workflows", "activity-availability", "diagnostics"]
};
function Sp(e) {
  return Xc({
    queryKey: Cn.activityAvailabilitySettings,
    queryFn: () => Kp(e)
  });
}
function Cp(e) {
  return Xc({
    queryKey: Cn.activityAvailabilityDiagnostics,
    queryFn: () => yl(e)
  });
}
function Ep(e) {
  const t = lf();
  return uf({
    mutationFn: (n) => Xp(e, n),
    onSuccess: () => {
      t.invalidateQueries({ queryKey: Cn.activityAvailabilitySettings }), t.invalidateQueries({ queryKey: Cn.activityAvailabilityDiagnostics }), t.invalidateQueries({ queryKey: Cn.activities });
    }
  });
}
async function kp(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), i = t.search.trim();
  return i && n.set("search", i), e.http.getJson(`${Ne}/definitions?${n.toString()}`);
}
async function Ip(e, t) {
  const n = await e.http.getJson(`${Ne}/definitions/${encodeURIComponent(t)}`);
  return n.draft ? { ...n, draft: { ...n.draft, state: Ji(n.draft.state) } } : n;
}
async function Ap(e, t, n) {
  const i = await e.http.postJson(
    `${Ne}/design/scoped-variables/analyze`,
    { state: Wn(t), nodeId: n }
  );
  return {
    visibleVariables: Array.isArray(i?.visibleVariables) ? i.visibleVariables : [],
    shadowingWarnings: Array.isArray(i?.shadowingWarnings) ? i.shadowingWarnings : []
  };
}
const Jr = (e) => ({ visibleVariables: [], shadowingWarnings: [], status: e });
function _p(e, t, n, i) {
  const r = de(() => bp(t, i), [i, t]), [s, a] = B(() => Jr("loading"));
  return J(() => {
    if (!t) {
      a(Jr("unavailable"));
      return;
    }
    let c = !1;
    return a((u) => ({ ...u, status: "loading" })), Ap(e, t, n).then(
      (u) => {
        c || a({ ...u, status: "ready" });
      },
      () => {
        c || a(Jr("unavailable"));
      }
    ), () => {
      c = !0;
    };
  }, [e, n, r]), s;
}
async function Dp(e, t) {
  const n = await e.http.getJson(`${Ne}/versions/${encodeURIComponent(t)}`);
  return { ...n, state: Ji(n.state) };
}
async function Tp(e, t) {
  return e.http.postJson(`${Ne}/definitions`, t);
}
async function $p(e, t) {
  await e.http.deleteJson(`${Ne}/definitions/${encodeURIComponent(t)}`);
}
async function Pp(e, t) {
  await e.http.postJson(`${Ne}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function Mp(e, t) {
  await e.http.deleteJson(`${Ne}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function Rp(e, t, n) {
  return e.http.requestJson(
    `${Ne}/definitions/${encodeURIComponent(t)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(n)
    }
  );
}
async function zp(e, t) {
  const n = await e.http.putJson(
    `${Ne}/drafts/${encodeURIComponent(t.id)}`,
    { state: Wn(t.state), layout: t.layout }
  );
  return { ...n, state: Ji(n.state) };
}
async function Lp(e, t) {
  return e.http.postJson(`${Ne}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function Vp(e, t) {
  return e.http.postJson(`${Ne}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function Op(e, t) {
  const n = { ...t, state: Wn(t.state) };
  try {
    return await e.http.postJson(`${jp}/workflows/drafts/test-runs`, n);
  } catch (i) {
    const r = Qp(i);
    if (r) return r;
    throw i;
  }
}
async function hl(e, t) {
  return e.http.postJson(`${Ne}/executables/${encodeURIComponent(t)}/run`, {});
}
async function gl(e) {
  const t = [`${Ne}/executables`, "/_demo/workflows/executables"], n = [];
  for (const i of t)
    try {
      const r = await e.http.getJson(i);
      return Hp(r);
    } catch (r) {
      n.push(r);
    }
  if (n.length > 0 && n.every(Ks)) return [];
  throw n.find((i) => !Ks(i)) ?? n[n.length - 1] ?? new Error("Workflow executables could not be loaded.");
}
function Hp(e) {
  return Array.isArray(e) ? e : e.executables ?? [];
}
function Ks(e) {
  if (!(e instanceof Error)) return !1;
  const t = e.message.toLowerCase();
  return /\b404\b/.test(t) || t.includes("not found");
}
async function Wp(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.runKind && n.set("runKind", t.runKind), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const i = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${i ? `?${i}` : ""}`);
}
async function Fp(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function Bp(e, t, n) {
  return e.http.getJson(
    `/runtime/workflows/instances/${encodeURIComponent(t)}/activity-executions/${encodeURIComponent(n)}`
  );
}
async function Uo(e) {
  return e.http.getJson(`${Ne}/activities`);
}
async function Kp(e) {
  return e.http.getJson(`${Ne}/activities/availability/settings`);
}
async function Xp(e, t) {
  return e.http.putJson(`${Ne}/activities/availability/settings`, t);
}
async function yl(e) {
  return e.http.getJson(`${Ne}/activities/availability/diagnostics`);
}
async function Yp(e) {
  const t = await er(e, [
    `${Ne}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? Xs(t) : Xs(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function qp(e) {
  const t = await er(e, [
    `${Ne}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : ji;
}
async function Up(e) {
  const t = await er(e, [
    `${Ne}/descriptors/variables`,
    "/descriptors/variables"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((i) => Zp(i));
}
function Zp(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = typeof t.alias == "string" && t.alias.length > 0, i = typeof t.typeName == "string" && t.typeName.length > 0;
  return n || i;
}
async function Gp(e) {
  const t = await er(e, [
    `${Ne}/descriptors/storage-drivers`,
    "/descriptors/storage-drivers"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((i) => Jp(i));
}
function Jp(e) {
  return !!e && typeof e == "object" && typeof e.typeName == "string" && e.typeName.length > 0;
}
async function er(e, t) {
  let n;
  for (const i of t)
    try {
      return await e.http.getJson(i);
    } catch (r) {
      n = r;
    }
  throw n;
}
function Xs(e) {
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
function Qp(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = Ys(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return Ys(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function Ys(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const ji = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
], eh = [
  "Available",
  "BlockedByHostBaseline",
  "HiddenByManagementSettings",
  "RemovedFromCatalog",
  "UnresolvedReference"
], th = {
  Available: "Available",
  BlockedByHostBaseline: "Host blocked",
  HiddenByManagementSettings: "Management hidden",
  RemovedFromCatalog: "Removed",
  UnresolvedReference: "Unresolved"
};
function Nt(e) {
  return typeof e == "string" ? e : typeof e == "number" ? eh[e] ?? "Available" : "Available";
}
function zi(e) {
  const t = Nt(e);
  return th[t] ?? t;
}
function nh(e) {
  return Nt(e).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function ml(e) {
  return Nt(e) !== "Available";
}
function ih(e) {
  return e === "Only" || e === 1 ? "Only" : "AllExcept";
}
function rh(e) {
  return e === "Only" ? 1 : 0;
}
function qs(e) {
  const t = e?.rules;
  return {
    mode: ih(e?.mode),
    activityTypes: t?.activityTypes ?? [],
    sets: t?.sets ?? []
  };
}
function oh(e) {
  return e.referenceKind === 0 || e.referenceKind === "ActivityType";
}
function sh(e) {
  return [...e?.items ?? []].filter(oh).filter((t) => t.activityTypeKey && t.activityDefinitionId).sort((t, n) => Li(t).localeCompare(Li(n)));
}
function ah(e) {
  return [...e?.items ?? []].filter((t) => {
    const n = Nt(t.state);
    return n === "RemovedFromCatalog" || n === "UnresolvedReference";
  }).sort((t, n) => (t.referenceName ?? "").localeCompare(n.referenceName ?? ""));
}
function Us(e, t) {
  return e.includes(t) ? e.filter((n) => n !== t) : [...e, t].sort((n, i) => n.localeCompare(i));
}
function Li(e) {
  const t = e?.displayName?.trim();
  if (t && t.toLowerCase() !== "activity") return t;
  const n = lh(e?.activityTypeKey);
  return ch(n) || t || e?.activityTypeKey || "Activity";
}
function Zs(e) {
  const t = xl(e?.activityTypeKey);
  if (t.length === 0) return "";
  const n = t[t.length - 1], i = t[t.length - 2];
  return n === "Activity" && i ? `${i}.${n}` : n;
}
function Gs(e) {
  const t = e.description?.trim();
  if (t) return t;
  const n = e.reason?.trim();
  return !n || !ml(e.state) ? "" : n;
}
function ch(e) {
  return e.replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/\bHttp\b/g, "HTTP").replace(/\bJson\b/g, "JSON").replace(/\bJava Script\b/g, "JavaScript").replace(/\bUrl\b/g, "URL").replace(/\bXml\b/g, "XML").trim();
}
function lh(e) {
  const t = xl(e), n = t[t.length - 1] ?? "";
  return n === "Activity" && t.length > 1 ? t[t.length - 2] : n;
}
function xl(e) {
  return e?.split(".").filter(Boolean) ?? [];
}
function uh(e, t) {
  const n = new Set(e.filter((i) => !!i));
  return (t?.items ?? []).find((i) => ml(i.state) ? [i.activityDefinitionId, i.activityTypeKey, i.referenceName].some((r) => r && n.has(r)) : !1) ?? null;
}
function dh({ context: e }) {
  const t = Sp(e), n = Cp(e), i = Ep(e), r = t.data ?? null, s = n.data ?? null, a = t.isLoading || n.isLoading, c = i.isPending, [u, l] = B(() => qs(r)), [d, f] = B(""), [p, h] = B(null);
  J(() => {
    l(qs(r));
  }, [r]);
  const y = de(() => sh(s), [s]), x = de(() => ah(s), [s]), m = s?.sets ?? [], w = de(() => {
    const k = d.trim().toLowerCase();
    return k ? y.filter((R) => [
      Li(R),
      Zs(R),
      Gs(R),
      R.activityTypeKey,
      R.category
    ].some((C) => (C ?? "").toLowerCase().includes(k))) : y;
  }, [y, d]), b = new Set(u.activityTypes), g = new Set(u.sets), v = y.filter((k) => Nt(k.state) === "BlockedByHostBaseline").length, j = y.filter((k) => Nt(k.state) === "HiddenByManagementSettings").length, N = i.error ?? t.error ?? n.error, S = N instanceof Error ? N.message : N ? "Activity availability could not be loaded." : null, I = (k) => l((R) => ({ ...R, mode: k })), _ = (k) => l((R) => ({ ...R, activityTypes: Us(R.activityTypes, k) })), M = (k) => l((R) => ({ ...R, sets: Us(R.sets, k) })), D = () => {
    h(null), i.mutate(
      {
        scope: r?.scope ?? "host-default",
        mode: rh(u.mode),
        rules: { activityTypes: u.activityTypes, sets: u.sets }
      },
      { onSuccess: () => h("Activity availability saved.") }
    );
  };
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-page availability-page", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-page-header", children: [
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("div", { className: "wf-kicker", children: "Workflows" }),
        /* @__PURE__ */ o.jsxs("h2", { children: [
          /* @__PURE__ */ o.jsx(Gd, { size: 18 }),
          " Activity availability"
        ] }),
        /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "Control which activities can be added to new workflows. Host baseline rules always take precedence." })
      ] }),
      /* @__PURE__ */ o.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ o.jsxs("button", { type: "button", className: "availability-save", onClick: D, disabled: a || c, children: [
        /* @__PURE__ */ o.jsx(Lc, { size: 15 }),
        c ? "Saving…" : "Save"
      ] }) })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "availability-body", children: [
      S && /* @__PURE__ */ o.jsx("div", { className: "availability-banner availability-banner-error", children: S }),
      p && !S && /* @__PURE__ */ o.jsx("div", { className: "availability-banner availability-banner-success", children: p }),
      /* @__PURE__ */ o.jsxs("div", { className: "availability-mode", role: "group", "aria-label": "Activity availability mode", children: [
        /* @__PURE__ */ o.jsxs("button", { type: "button", className: u.mode === "AllExcept" ? "active" : "", onClick: () => I("AllExcept"), disabled: a || c, children: [
          /* @__PURE__ */ o.jsx(_s, { size: 15 }),
          /* @__PURE__ */ o.jsxs("span", { children: [
            /* @__PURE__ */ o.jsx("strong", { children: "All except" }),
            /* @__PURE__ */ o.jsx("em", { children: "Show everything except the selected activities" })
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", className: u.mode === "Only" ? "active" : "", onClick: () => I("Only"), disabled: a || c, children: [
          /* @__PURE__ */ o.jsx(Ds, { size: 15 }),
          /* @__PURE__ */ o.jsxs("span", { children: [
            /* @__PURE__ */ o.jsx("strong", { children: "Only" }),
            /* @__PURE__ */ o.jsx("em", { children: "Show only the selected activities" })
          ] })
        ] })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { className: "availability-counts", children: [
        /* @__PURE__ */ o.jsxs("span", { children: [
          /* @__PURE__ */ o.jsx(Ds, { size: 14 }),
          " ",
          v,
          " host blocked"
        ] }),
        /* @__PURE__ */ o.jsxs("span", { children: [
          /* @__PURE__ */ o.jsx(_s, { size: 14 }),
          " ",
          j,
          " management hidden"
        ] }),
        /* @__PURE__ */ o.jsxs("span", { children: [
          /* @__PURE__ */ o.jsx(Ti, { size: 14 }),
          " ",
          x.length,
          " unresolved"
        ] })
      ] }),
      m.length > 0 && /* @__PURE__ */ o.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ o.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ o.jsx(Wo, { size: 14 }),
          " Sets"
        ] }),
        /* @__PURE__ */ o.jsx("div", { className: "availability-set-list", children: m.map((k) => /* @__PURE__ */ o.jsxs("label", { className: "availability-set-option", children: [
          /* @__PURE__ */ o.jsx("input", { type: "checkbox", checked: g.has(k.name), disabled: a || c, onChange: () => M(k.name) }),
          /* @__PURE__ */ o.jsx("span", { children: k.name }),
          /* @__PURE__ */ o.jsx("code", { children: (k.activityTypeKeys ?? []).length })
        ] }, k.name)) })
      ] }),
      /* @__PURE__ */ o.jsxs("section", { className: "availability-section availability-section-grow", children: [
        /* @__PURE__ */ o.jsxs("div", { className: "availability-section-head", children: [
          /* @__PURE__ */ o.jsxs("h3", { className: "wf-section-label", children: [
            /* @__PURE__ */ o.jsx(Vc, { size: 14 }),
            " Activities"
          ] }),
          /* @__PURE__ */ o.jsxs("div", { className: "wf-search availability-search", children: [
            /* @__PURE__ */ o.jsx(Zi, { size: 14 }),
            /* @__PURE__ */ o.jsx("input", { type: "search", value: d, placeholder: "Filter activities…", onChange: (k) => f(k.target.value) })
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("div", { className: "availability-activity-list", children: [
          a && y.length === 0 && /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "Loading availability…" }),
          !a && y.length === 0 && /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "No availability diagnostics reported." }),
          !a && y.length > 0 && w.length === 0 && /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "No activities match the filter." }),
          w.map((k) => {
            const C = Nt(k.state) === "BlockedByHostBaseline", A = k.activityTypeKey ?? k.activityDefinitionId ?? "", E = Li(k), T = Zs(k), $ = Gs(k);
            return /* @__PURE__ */ o.jsxs("label", { className: `availability-activity-option ${C ? "disabled" : ""}`, children: [
              /* @__PURE__ */ o.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: b.has(A),
                  disabled: a || c || C,
                  onChange: () => _(A)
                }
              ),
              /* @__PURE__ */ o.jsxs("span", { className: "availability-activity-main", children: [
                /* @__PURE__ */ o.jsxs("span", { className: "availability-activity-title-line", children: [
                  /* @__PURE__ */ o.jsx("strong", { children: E }),
                  k.category && /* @__PURE__ */ o.jsx("span", { className: "availability-activity-category", children: k.category })
                ] }),
                $ && /* @__PURE__ */ o.jsx("span", { className: "availability-activity-description", children: $ }),
                T && /* @__PURE__ */ o.jsx("span", { className: "availability-activity-meta", title: k.activityTypeKey ?? void 0, children: /* @__PURE__ */ o.jsx("code", { children: T }) })
              ] }),
              /* @__PURE__ */ o.jsx("em", { className: `availability-state ${nh(k.state)}`, children: zi(k.state) })
            ] }, A);
          })
        ] })
      ] }),
      x.length > 0 && /* @__PURE__ */ o.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ o.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ o.jsx(Ti, { size: 14 }),
          " Unresolved references"
        ] }),
        /* @__PURE__ */ o.jsx("div", { className: "availability-unresolved-list", children: x.map((k) => /* @__PURE__ */ o.jsxs("span", { children: [
          /* @__PURE__ */ o.jsx("strong", { children: k.referenceName }),
          /* @__PURE__ */ o.jsx("em", { children: zi(k.state) })
        ] }, `${k.layer}-${k.referenceKind}-${k.referenceName}`)) })
      ] })
    ] })
  ] });
}
function Ce(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, i; n < e.length; n++)
      (i = Ce(e[n])) !== "" && (t += (t && " ") + i);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var fh = { value: () => {
} };
function tr() {
  for (var e = 0, t = arguments.length, n = {}, i; e < t; ++e) {
    if (!(i = arguments[e] + "") || i in n || /[\s.]/.test(i)) throw new Error("illegal type: " + i);
    n[i] = [];
  }
  return new Si(n);
}
function Si(e) {
  this._ = e;
}
function ph(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var i = "", r = n.indexOf(".");
    if (r >= 0 && (i = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: i };
  });
}
Si.prototype = tr.prototype = {
  constructor: Si,
  on: function(e, t) {
    var n = this._, i = ph(e + "", n), r, s = -1, a = i.length;
    if (arguments.length < 2) {
      for (; ++s < a; ) if ((r = (e = i[s]).type) && (r = hh(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++s < a; )
      if (r = (e = i[s]).type) n[r] = Js(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = Js(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new Si(e);
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
function hh(e, t) {
  for (var n = 0, i = e.length, r; n < i; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function Js(e, t, n) {
  for (var i = 0, r = e.length; i < r; ++i)
    if (e[i].name === t) {
      e[i] = fh, e = e.slice(0, i).concat(e.slice(i + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var bo = "http://www.w3.org/1999/xhtml";
const Qs = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: bo,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function nr(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Qs.hasOwnProperty(t) ? { space: Qs[t], local: e } : e;
}
function gh(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === bo && t.documentElement.namespaceURI === bo ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function yh(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function wl(e) {
  var t = nr(e);
  return (t.local ? yh : gh)(t);
}
function mh() {
}
function Zo(e) {
  return e == null ? mh : function() {
    return this.querySelector(e);
  };
}
function xh(e) {
  typeof e != "function" && (e = Zo(e));
  for (var t = this._groups, n = t.length, i = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = i[r] = new Array(a), u, l, d = 0; d < a; ++d)
      (u = s[d]) && (l = e.call(u, u.__data__, d, s)) && ("__data__" in u && (l.__data__ = u.__data__), c[d] = l);
  return new Oe(i, this._parents);
}
function wh(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function vh() {
  return [];
}
function vl(e) {
  return e == null ? vh : function() {
    return this.querySelectorAll(e);
  };
}
function bh(e) {
  return function() {
    return wh(e.apply(this, arguments));
  };
}
function Nh(e) {
  typeof e == "function" ? e = bh(e) : e = vl(e);
  for (var t = this._groups, n = t.length, i = [], r = [], s = 0; s < n; ++s)
    for (var a = t[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && (i.push(e.call(u, u.__data__, l, a)), r.push(u));
  return new Oe(i, r);
}
function bl(e) {
  return function() {
    return this.matches(e);
  };
}
function Nl(e) {
  return function(t) {
    return t.matches(e);
  };
}
var jh = Array.prototype.find;
function Sh(e) {
  return function() {
    return jh.call(this.children, e);
  };
}
function Ch() {
  return this.firstElementChild;
}
function Eh(e) {
  return this.select(e == null ? Ch : Sh(typeof e == "function" ? e : Nl(e)));
}
var kh = Array.prototype.filter;
function Ih() {
  return Array.from(this.children);
}
function Ah(e) {
  return function() {
    return kh.call(this.children, e);
  };
}
function _h(e) {
  return this.selectAll(e == null ? Ih : Ah(typeof e == "function" ? e : Nl(e)));
}
function Dh(e) {
  typeof e != "function" && (e = bl(e));
  for (var t = this._groups, n = t.length, i = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = i[r] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new Oe(i, this._parents);
}
function jl(e) {
  return new Array(e.length);
}
function Th() {
  return new Oe(this._enter || this._groups.map(jl), this._parents);
}
function Vi(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
Vi.prototype = {
  constructor: Vi,
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
function Ph(e, t, n, i, r, s) {
  for (var a = 0, c, u = t.length, l = s.length; a < l; ++a)
    (c = t[a]) ? (c.__data__ = s[a], i[a] = c) : n[a] = new Vi(e, s[a]);
  for (; a < u; ++a)
    (c = t[a]) && (r[a] = c);
}
function Mh(e, t, n, i, r, s, a) {
  var c, u, l = /* @__PURE__ */ new Map(), d = t.length, f = s.length, p = new Array(d), h;
  for (c = 0; c < d; ++c)
    (u = t[c]) && (p[c] = h = a.call(u, u.__data__, c, t) + "", l.has(h) ? r[c] = u : l.set(h, u));
  for (c = 0; c < f; ++c)
    h = a.call(e, s[c], c, s) + "", (u = l.get(h)) ? (i[c] = u, u.__data__ = s[c], l.delete(h)) : n[c] = new Vi(e, s[c]);
  for (c = 0; c < d; ++c)
    (u = t[c]) && l.get(p[c]) === u && (r[c] = u);
}
function Rh(e) {
  return e.__data__;
}
function zh(e, t) {
  if (!arguments.length) return Array.from(this, Rh);
  var n = t ? Mh : Ph, i = this._parents, r = this._groups;
  typeof e != "function" && (e = $h(e));
  for (var s = r.length, a = new Array(s), c = new Array(s), u = new Array(s), l = 0; l < s; ++l) {
    var d = i[l], f = r[l], p = f.length, h = Lh(e.call(d, d && d.__data__, l, i)), y = h.length, x = c[l] = new Array(y), m = a[l] = new Array(y), w = u[l] = new Array(p);
    n(d, f, x, m, w, h, t);
    for (var b = 0, g = 0, v, j; b < y; ++b)
      if (v = x[b]) {
        for (b >= g && (g = b + 1); !(j = m[g]) && ++g < y; ) ;
        v._next = j || null;
      }
  }
  return a = new Oe(a, i), a._enter = c, a._exit = u, a;
}
function Lh(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function Vh() {
  return new Oe(this._exit || this._groups.map(jl), this._parents);
}
function Oh(e, t, n) {
  var i = this.enter(), r = this, s = this.exit();
  return typeof e == "function" ? (i = e(i), i && (i = i.selection())) : i = i.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? s.remove() : n(s), i && r ? i.merge(r).order() : r;
}
function Hh(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, i = t._groups, r = n.length, s = i.length, a = Math.min(r, s), c = new Array(r), u = 0; u < a; ++u)
    for (var l = n[u], d = i[u], f = l.length, p = c[u] = new Array(f), h, y = 0; y < f; ++y)
      (h = l[y] || d[y]) && (p[y] = h);
  for (; u < r; ++u)
    c[u] = n[u];
  return new Oe(c, this._parents);
}
function Wh() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var i = e[t], r = i.length - 1, s = i[r], a; --r >= 0; )
      (a = i[r]) && (s && a.compareDocumentPosition(s) ^ 4 && s.parentNode.insertBefore(a, s), s = a);
  return this;
}
function Fh(e) {
  e || (e = Bh);
  function t(f, p) {
    return f && p ? e(f.__data__, p.__data__) : !f - !p;
  }
  for (var n = this._groups, i = n.length, r = new Array(i), s = 0; s < i; ++s) {
    for (var a = n[s], c = a.length, u = r[s] = new Array(c), l, d = 0; d < c; ++d)
      (l = a[d]) && (u[d] = l);
    u.sort(t);
  }
  return new Oe(r, this._parents).order();
}
function Bh(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function Kh() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function Xh() {
  return Array.from(this);
}
function Yh() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var i = e[t], r = 0, s = i.length; r < s; ++r) {
      var a = i[r];
      if (a) return a;
    }
  return null;
}
function qh() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function Uh() {
  return !this.node();
}
function Zh(e) {
  for (var t = this._groups, n = 0, i = t.length; n < i; ++n)
    for (var r = t[n], s = 0, a = r.length, c; s < a; ++s)
      (c = r[s]) && e.call(c, c.__data__, s, r);
  return this;
}
function Gh(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Jh(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Qh(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function eg(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function tg(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function ng(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function ig(e, t) {
  var n = nr(e);
  if (arguments.length < 2) {
    var i = this.node();
    return n.local ? i.getAttributeNS(n.space, n.local) : i.getAttribute(n);
  }
  return this.each((t == null ? n.local ? Jh : Gh : typeof t == "function" ? n.local ? ng : tg : n.local ? eg : Qh)(n, t));
}
function Sl(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function rg(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function og(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function sg(e, t, n) {
  return function() {
    var i = t.apply(this, arguments);
    i == null ? this.style.removeProperty(e) : this.style.setProperty(e, i, n);
  };
}
function ag(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? rg : typeof t == "function" ? sg : og)(e, t, n ?? "")) : Jt(this.node(), e);
}
function Jt(e, t) {
  return e.style.getPropertyValue(t) || Sl(e).getComputedStyle(e, null).getPropertyValue(t);
}
function cg(e) {
  return function() {
    delete this[e];
  };
}
function lg(e, t) {
  return function() {
    this[e] = t;
  };
}
function ug(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function dg(e, t) {
  return arguments.length > 1 ? this.each((t == null ? cg : typeof t == "function" ? ug : lg)(e, t)) : this.node()[e];
}
function Cl(e) {
  return e.trim().split(/^|\s+/);
}
function Go(e) {
  return e.classList || new El(e);
}
function El(e) {
  this._node = e, this._names = Cl(e.getAttribute("class") || "");
}
El.prototype = {
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
function kl(e, t) {
  for (var n = Go(e), i = -1, r = t.length; ++i < r; ) n.add(t[i]);
}
function Il(e, t) {
  for (var n = Go(e), i = -1, r = t.length; ++i < r; ) n.remove(t[i]);
}
function fg(e) {
  return function() {
    kl(this, e);
  };
}
function pg(e) {
  return function() {
    Il(this, e);
  };
}
function hg(e, t) {
  return function() {
    (t.apply(this, arguments) ? kl : Il)(this, e);
  };
}
function gg(e, t) {
  var n = Cl(e + "");
  if (arguments.length < 2) {
    for (var i = Go(this.node()), r = -1, s = n.length; ++r < s; ) if (!i.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? hg : t ? fg : pg)(n, t));
}
function yg() {
  this.textContent = "";
}
function mg(e) {
  return function() {
    this.textContent = e;
  };
}
function xg(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function wg(e) {
  return arguments.length ? this.each(e == null ? yg : (typeof e == "function" ? xg : mg)(e)) : this.node().textContent;
}
function vg() {
  this.innerHTML = "";
}
function bg(e) {
  return function() {
    this.innerHTML = e;
  };
}
function Ng(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function jg(e) {
  return arguments.length ? this.each(e == null ? vg : (typeof e == "function" ? Ng : bg)(e)) : this.node().innerHTML;
}
function Sg() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function Cg() {
  return this.each(Sg);
}
function Eg() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function kg() {
  return this.each(Eg);
}
function Ig(e) {
  var t = typeof e == "function" ? e : wl(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Ag() {
  return null;
}
function _g(e, t) {
  var n = typeof e == "function" ? e : wl(e), i = t == null ? Ag : typeof t == "function" ? t : Zo(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), i.apply(this, arguments) || null);
  });
}
function Dg() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Tg() {
  return this.each(Dg);
}
function $g() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Pg() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Mg(e) {
  return this.select(e ? Pg : $g);
}
function Rg(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function zg(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function Lg(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", i = t.indexOf(".");
    return i >= 0 && (n = t.slice(i + 1), t = t.slice(0, i)), { type: t, name: n };
  });
}
function Vg(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, i = -1, r = t.length, s; n < r; ++n)
        s = t[n], (!e.type || s.type === e.type) && s.name === e.name ? this.removeEventListener(s.type, s.listener, s.options) : t[++i] = s;
      ++i ? t.length = i : delete this.__on;
    }
  };
}
function Og(e, t, n) {
  return function() {
    var i = this.__on, r, s = zg(t);
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
function Hg(e, t, n) {
  var i = Lg(e + ""), r, s = i.length, a;
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
  for (c = t ? Og : Vg, r = 0; r < s; ++r) this.each(c(i[r], t, n));
  return this;
}
function Al(e, t, n) {
  var i = Sl(e), r = i.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = i.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function Wg(e, t) {
  return function() {
    return Al(this, e, t);
  };
}
function Fg(e, t) {
  return function() {
    return Al(this, e, t.apply(this, arguments));
  };
}
function Bg(e, t) {
  return this.each((typeof t == "function" ? Fg : Wg)(e, t));
}
function* Kg() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var i = e[t], r = 0, s = i.length, a; r < s; ++r)
      (a = i[r]) && (yield a);
}
var _l = [null];
function Oe(e, t) {
  this._groups = e, this._parents = t;
}
function Kn() {
  return new Oe([[document.documentElement]], _l);
}
function Xg() {
  return this;
}
Oe.prototype = Kn.prototype = {
  constructor: Oe,
  select: xh,
  selectAll: Nh,
  selectChild: Eh,
  selectChildren: _h,
  filter: Dh,
  data: zh,
  enter: Th,
  exit: Vh,
  join: Oh,
  merge: Hh,
  selection: Xg,
  order: Wh,
  sort: Fh,
  call: Kh,
  nodes: Xh,
  node: Yh,
  size: qh,
  empty: Uh,
  each: Zh,
  attr: ig,
  style: ag,
  property: dg,
  classed: gg,
  text: wg,
  html: jg,
  raise: Cg,
  lower: kg,
  append: Ig,
  insert: _g,
  remove: Tg,
  clone: Mg,
  datum: Rg,
  on: Hg,
  dispatch: Bg,
  [Symbol.iterator]: Kg
};
function ze(e) {
  return typeof e == "string" ? new Oe([[document.querySelector(e)]], [document.documentElement]) : new Oe([[e]], _l);
}
function Yg(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Ke(e, t) {
  if (e = Yg(e), t === void 0 && (t = e.currentTarget), t) {
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
const qg = { passive: !1 }, Dn = { capture: !0, passive: !1 };
function Qr(e) {
  e.stopImmediatePropagation();
}
function Xt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Dl(e) {
  var t = e.document.documentElement, n = ze(e).on("dragstart.drag", Xt, Dn);
  "onselectstart" in t ? n.on("selectstart.drag", Xt, Dn) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function Tl(e, t) {
  var n = e.document.documentElement, i = ze(e).on("dragstart.drag", null);
  t && (i.on("click.drag", Xt, Dn), setTimeout(function() {
    i.on("click.drag", null);
  }, 0)), "onselectstart" in n ? i.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const ui = (e) => () => e;
function No(e, {
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
No.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function Ug(e) {
  return !e.ctrlKey && !e.button;
}
function Zg() {
  return this.parentNode;
}
function Gg(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function Jg() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function $l() {
  var e = Ug, t = Zg, n = Gg, i = Jg, r = {}, s = tr("start", "drag", "end"), a = 0, c, u, l, d, f = 0;
  function p(v) {
    v.on("mousedown.drag", h).filter(i).on("touchstart.drag", m).on("touchmove.drag", w, qg).on("touchend.drag touchcancel.drag", b).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function h(v, j) {
    if (!(d || !e.call(this, v, j))) {
      var N = g(this, t.call(this, v, j), v, j, "mouse");
      N && (ze(v.view).on("mousemove.drag", y, Dn).on("mouseup.drag", x, Dn), Dl(v.view), Qr(v), l = !1, c = v.clientX, u = v.clientY, N("start", v));
    }
  }
  function y(v) {
    if (Xt(v), !l) {
      var j = v.clientX - c, N = v.clientY - u;
      l = j * j + N * N > f;
    }
    r.mouse("drag", v);
  }
  function x(v) {
    ze(v.view).on("mousemove.drag mouseup.drag", null), Tl(v.view, l), Xt(v), r.mouse("end", v);
  }
  function m(v, j) {
    if (e.call(this, v, j)) {
      var N = v.changedTouches, S = t.call(this, v, j), I = N.length, _, M;
      for (_ = 0; _ < I; ++_)
        (M = g(this, S, v, j, N[_].identifier, N[_])) && (Qr(v), M("start", v, N[_]));
    }
  }
  function w(v) {
    var j = v.changedTouches, N = j.length, S, I;
    for (S = 0; S < N; ++S)
      (I = r[j[S].identifier]) && (Xt(v), I("drag", v, j[S]));
  }
  function b(v) {
    var j = v.changedTouches, N = j.length, S, I;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), S = 0; S < N; ++S)
      (I = r[j[S].identifier]) && (Qr(v), I("end", v, j[S]));
  }
  function g(v, j, N, S, I, _) {
    var M = s.copy(), D = Ke(_ || N, j), k, R, C;
    if ((C = n.call(v, new No("beforestart", {
      sourceEvent: N,
      target: p,
      identifier: I,
      active: a,
      x: D[0],
      y: D[1],
      dx: 0,
      dy: 0,
      dispatch: M
    }), S)) != null)
      return k = C.x - D[0] || 0, R = C.y - D[1] || 0, function A(E, T, $) {
        var P = D, F;
        switch (E) {
          case "start":
            r[I] = A, F = a++;
            break;
          case "end":
            delete r[I], --a;
          // falls through
          case "drag":
            D = Ke($ || T, j), F = a;
            break;
        }
        M.call(
          E,
          v,
          new No(E, {
            sourceEvent: T,
            subject: C,
            target: p,
            identifier: I,
            active: F,
            x: D[0] + k,
            y: D[1] + R,
            dx: D[0] - P[0],
            dy: D[1] - P[1],
            dispatch: M
          }),
          S
        );
      };
  }
  return p.filter = function(v) {
    return arguments.length ? (e = typeof v == "function" ? v : ui(!!v), p) : e;
  }, p.container = function(v) {
    return arguments.length ? (t = typeof v == "function" ? v : ui(v), p) : t;
  }, p.subject = function(v) {
    return arguments.length ? (n = typeof v == "function" ? v : ui(v), p) : n;
  }, p.touchable = function(v) {
    return arguments.length ? (i = typeof v == "function" ? v : ui(!!v), p) : i;
  }, p.on = function() {
    var v = s.on.apply(s, arguments);
    return v === s ? p : v;
  }, p.clickDistance = function(v) {
    return arguments.length ? (f = (v = +v) * v, p) : Math.sqrt(f);
  }, p;
}
function Jo(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function Pl(e, t) {
  var n = Object.create(e.prototype);
  for (var i in t) n[i] = t[i];
  return n;
}
function Xn() {
}
var Tn = 0.7, Oi = 1 / Tn, Yt = "\\s*([+-]?\\d+)\\s*", $n = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", Je = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", Qg = /^#([0-9a-f]{3,8})$/, ey = new RegExp(`^rgb\\(${Yt},${Yt},${Yt}\\)$`), ty = new RegExp(`^rgb\\(${Je},${Je},${Je}\\)$`), ny = new RegExp(`^rgba\\(${Yt},${Yt},${Yt},${$n}\\)$`), iy = new RegExp(`^rgba\\(${Je},${Je},${Je},${$n}\\)$`), ry = new RegExp(`^hsl\\(${$n},${Je},${Je}\\)$`), oy = new RegExp(`^hsla\\(${$n},${Je},${Je},${$n}\\)$`), ea = {
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
Jo(Xn, Ct, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: ta,
  // Deprecated! Use color.formatHex.
  formatHex: ta,
  formatHex8: sy,
  formatHsl: ay,
  formatRgb: na,
  toString: na
});
function ta() {
  return this.rgb().formatHex();
}
function sy() {
  return this.rgb().formatHex8();
}
function ay() {
  return Ml(this).formatHsl();
}
function na() {
  return this.rgb().formatRgb();
}
function Ct(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = Qg.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? ia(t) : n === 3 ? new Pe(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? di(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? di(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = ey.exec(e)) ? new Pe(t[1], t[2], t[3], 1) : (t = ty.exec(e)) ? new Pe(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = ny.exec(e)) ? di(t[1], t[2], t[3], t[4]) : (t = iy.exec(e)) ? di(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = ry.exec(e)) ? sa(t[1], t[2] / 100, t[3] / 100, 1) : (t = oy.exec(e)) ? sa(t[1], t[2] / 100, t[3] / 100, t[4]) : ea.hasOwnProperty(e) ? ia(ea[e]) : e === "transparent" ? new Pe(NaN, NaN, NaN, 0) : null;
}
function ia(e) {
  return new Pe(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function di(e, t, n, i) {
  return i <= 0 && (e = t = n = NaN), new Pe(e, t, n, i);
}
function cy(e) {
  return e instanceof Xn || (e = Ct(e)), e ? (e = e.rgb(), new Pe(e.r, e.g, e.b, e.opacity)) : new Pe();
}
function jo(e, t, n, i) {
  return arguments.length === 1 ? cy(e) : new Pe(e, t, n, i ?? 1);
}
function Pe(e, t, n, i) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +i;
}
Jo(Pe, jo, Pl(Xn, {
  brighter(e) {
    return e = e == null ? Oi : Math.pow(Oi, e), new Pe(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Tn : Math.pow(Tn, e), new Pe(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new Pe(jt(this.r), jt(this.g), jt(this.b), Hi(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: ra,
  // Deprecated! Use color.formatHex.
  formatHex: ra,
  formatHex8: ly,
  formatRgb: oa,
  toString: oa
}));
function ra() {
  return `#${bt(this.r)}${bt(this.g)}${bt(this.b)}`;
}
function ly() {
  return `#${bt(this.r)}${bt(this.g)}${bt(this.b)}${bt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function oa() {
  const e = Hi(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${jt(this.r)}, ${jt(this.g)}, ${jt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Hi(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function jt(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function bt(e) {
  return e = jt(e), (e < 16 ? "0" : "") + e.toString(16);
}
function sa(e, t, n, i) {
  return i <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Xe(e, t, n, i);
}
function Ml(e) {
  if (e instanceof Xe) return new Xe(e.h, e.s, e.l, e.opacity);
  if (e instanceof Xn || (e = Ct(e)), !e) return new Xe();
  if (e instanceof Xe) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, i = e.b / 255, r = Math.min(t, n, i), s = Math.max(t, n, i), a = NaN, c = s - r, u = (s + r) / 2;
  return c ? (t === s ? a = (n - i) / c + (n < i) * 6 : n === s ? a = (i - t) / c + 2 : a = (t - n) / c + 4, c /= u < 0.5 ? s + r : 2 - s - r, a *= 60) : c = u > 0 && u < 1 ? 0 : a, new Xe(a, c, u, e.opacity);
}
function uy(e, t, n, i) {
  return arguments.length === 1 ? Ml(e) : new Xe(e, t, n, i ?? 1);
}
function Xe(e, t, n, i) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +i;
}
Jo(Xe, uy, Pl(Xn, {
  brighter(e) {
    return e = e == null ? Oi : Math.pow(Oi, e), new Xe(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Tn : Math.pow(Tn, e), new Xe(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, i = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - i;
    return new Pe(
      eo(e >= 240 ? e - 240 : e + 120, r, i),
      eo(e, r, i),
      eo(e < 120 ? e + 240 : e - 120, r, i),
      this.opacity
    );
  },
  clamp() {
    return new Xe(aa(this.h), fi(this.s), fi(this.l), Hi(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Hi(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${aa(this.h)}, ${fi(this.s) * 100}%, ${fi(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function aa(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function fi(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function eo(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const Qo = (e) => () => e;
function dy(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function fy(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(i) {
    return Math.pow(e + i * t, n);
  };
}
function py(e) {
  return (e = +e) == 1 ? Rl : function(t, n) {
    return n - t ? fy(t, n, e) : Qo(isNaN(t) ? n : t);
  };
}
function Rl(e, t) {
  var n = t - e;
  return n ? dy(e, n) : Qo(isNaN(e) ? t : e);
}
const Wi = (function e(t) {
  var n = py(t);
  function i(r, s) {
    var a = n((r = jo(r)).r, (s = jo(s)).r), c = n(r.g, s.g), u = n(r.b, s.b), l = Rl(r.opacity, s.opacity);
    return function(d) {
      return r.r = a(d), r.g = c(d), r.b = u(d), r.opacity = l(d), r + "";
    };
  }
  return i.gamma = e, i;
})(1);
function hy(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, i = t.slice(), r;
  return function(s) {
    for (r = 0; r < n; ++r) i[r] = e[r] * (1 - s) + t[r] * s;
    return i;
  };
}
function gy(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function yy(e, t) {
  var n = t ? t.length : 0, i = e ? Math.min(n, e.length) : 0, r = new Array(i), s = new Array(n), a;
  for (a = 0; a < i; ++a) r[a] = En(e[a], t[a]);
  for (; a < n; ++a) s[a] = t[a];
  return function(c) {
    for (a = 0; a < i; ++a) s[a] = r[a](c);
    return s;
  };
}
function my(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(i) {
    return n.setTime(e * (1 - i) + t * i), n;
  };
}
function Ge(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function xy(e, t) {
  var n = {}, i = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = En(e[r], t[r]) : i[r] = t[r];
  return function(s) {
    for (r in n) i[r] = n[r](s);
    return i;
  };
}
var So = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, to = new RegExp(So.source, "g");
function wy(e) {
  return function() {
    return e;
  };
}
function vy(e) {
  return function(t) {
    return e(t) + "";
  };
}
function zl(e, t) {
  var n = So.lastIndex = to.lastIndex = 0, i, r, s, a = -1, c = [], u = [];
  for (e = e + "", t = t + ""; (i = So.exec(e)) && (r = to.exec(t)); )
    (s = r.index) > n && (s = t.slice(n, s), c[a] ? c[a] += s : c[++a] = s), (i = i[0]) === (r = r[0]) ? c[a] ? c[a] += r : c[++a] = r : (c[++a] = null, u.push({ i: a, x: Ge(i, r) })), n = to.lastIndex;
  return n < t.length && (s = t.slice(n), c[a] ? c[a] += s : c[++a] = s), c.length < 2 ? u[0] ? vy(u[0].x) : wy(t) : (t = u.length, function(l) {
    for (var d = 0, f; d < t; ++d) c[(f = u[d]).i] = f.x(l);
    return c.join("");
  });
}
function En(e, t) {
  var n = typeof t, i;
  return t == null || n === "boolean" ? Qo(t) : (n === "number" ? Ge : n === "string" ? (i = Ct(t)) ? (t = i, Wi) : zl : t instanceof Ct ? Wi : t instanceof Date ? my : gy(t) ? hy : Array.isArray(t) ? yy : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? xy : Ge)(e, t);
}
var ca = 180 / Math.PI, Co = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function Ll(e, t, n, i, r, s) {
  var a, c, u;
  return (a = Math.sqrt(e * e + t * t)) && (e /= a, t /= a), (u = e * n + t * i) && (n -= e * u, i -= t * u), (c = Math.sqrt(n * n + i * i)) && (n /= c, i /= c, u /= c), e * i < t * n && (e = -e, t = -t, u = -u, a = -a), {
    translateX: r,
    translateY: s,
    rotate: Math.atan2(t, e) * ca,
    skewX: Math.atan(u) * ca,
    scaleX: a,
    scaleY: c
  };
}
var pi;
function by(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Co : Ll(t.a, t.b, t.c, t.d, t.e, t.f);
}
function Ny(e) {
  return e == null || (pi || (pi = document.createElementNS("http://www.w3.org/2000/svg", "g")), pi.setAttribute("transform", e), !(e = pi.transform.baseVal.consolidate())) ? Co : (e = e.matrix, Ll(e.a, e.b, e.c, e.d, e.e, e.f));
}
function Vl(e, t, n, i) {
  function r(l) {
    return l.length ? l.pop() + " " : "";
  }
  function s(l, d, f, p, h, y) {
    if (l !== f || d !== p) {
      var x = h.push("translate(", null, t, null, n);
      y.push({ i: x - 4, x: Ge(l, f) }, { i: x - 2, x: Ge(d, p) });
    } else (f || p) && h.push("translate(" + f + t + p + n);
  }
  function a(l, d, f, p) {
    l !== d ? (l - d > 180 ? d += 360 : d - l > 180 && (l += 360), p.push({ i: f.push(r(f) + "rotate(", null, i) - 2, x: Ge(l, d) })) : d && f.push(r(f) + "rotate(" + d + i);
  }
  function c(l, d, f, p) {
    l !== d ? p.push({ i: f.push(r(f) + "skewX(", null, i) - 2, x: Ge(l, d) }) : d && f.push(r(f) + "skewX(" + d + i);
  }
  function u(l, d, f, p, h, y) {
    if (l !== f || d !== p) {
      var x = h.push(r(h) + "scale(", null, ",", null, ")");
      y.push({ i: x - 4, x: Ge(l, f) }, { i: x - 2, x: Ge(d, p) });
    } else (f !== 1 || p !== 1) && h.push(r(h) + "scale(" + f + "," + p + ")");
  }
  return function(l, d) {
    var f = [], p = [];
    return l = e(l), d = e(d), s(l.translateX, l.translateY, d.translateX, d.translateY, f, p), a(l.rotate, d.rotate, f, p), c(l.skewX, d.skewX, f, p), u(l.scaleX, l.scaleY, d.scaleX, d.scaleY, f, p), l = d = null, function(h) {
      for (var y = -1, x = p.length, m; ++y < x; ) f[(m = p[y]).i] = m.x(h);
      return f.join("");
    };
  };
}
var jy = Vl(by, "px, ", "px)", "deg)"), Sy = Vl(Ny, ", ", ")", ")"), Cy = 1e-12;
function la(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Ey(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function ky(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const Ci = (function e(t, n, i) {
  function r(s, a) {
    var c = s[0], u = s[1], l = s[2], d = a[0], f = a[1], p = a[2], h = d - c, y = f - u, x = h * h + y * y, m, w;
    if (x < Cy)
      w = Math.log(p / l) / t, m = function(S) {
        return [
          c + S * h,
          u + S * y,
          l * Math.exp(t * S * w)
        ];
      };
    else {
      var b = Math.sqrt(x), g = (p * p - l * l + i * x) / (2 * l * n * b), v = (p * p - l * l - i * x) / (2 * p * n * b), j = Math.log(Math.sqrt(g * g + 1) - g), N = Math.log(Math.sqrt(v * v + 1) - v);
      w = (N - j) / t, m = function(S) {
        var I = S * w, _ = la(j), M = l / (n * b) * (_ * ky(t * I + j) - Ey(j));
        return [
          c + M * h,
          u + M * y,
          l * _ / la(t * I + j)
        ];
      };
    }
    return m.duration = w * 1e3 * t / Math.SQRT2, m;
  }
  return r.rho = function(s) {
    var a = Math.max(1e-3, +s), c = a * a, u = c * c;
    return e(a, c, u);
  }, r;
})(Math.SQRT2, 2, 4);
var Qt = 0, xn = 0, hn = 0, Ol = 1e3, Fi, wn, Bi = 0, Et = 0, ir = 0, Pn = typeof performance == "object" && performance.now ? performance : Date, Hl = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function es() {
  return Et || (Hl(Iy), Et = Pn.now() + ir);
}
function Iy() {
  Et = 0;
}
function Ki() {
  this._call = this._time = this._next = null;
}
Ki.prototype = Wl.prototype = {
  constructor: Ki,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? es() : +n) + (t == null ? 0 : +t), !this._next && wn !== this && (wn ? wn._next = this : Fi = this, wn = this), this._call = e, this._time = n, Eo();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Eo());
  }
};
function Wl(e, t, n) {
  var i = new Ki();
  return i.restart(e, t, n), i;
}
function Ay() {
  es(), ++Qt;
  for (var e = Fi, t; e; )
    (t = Et - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --Qt;
}
function ua() {
  Et = (Bi = Pn.now()) + ir, Qt = xn = 0;
  try {
    Ay();
  } finally {
    Qt = 0, Dy(), Et = 0;
  }
}
function _y() {
  var e = Pn.now(), t = e - Bi;
  t > Ol && (ir -= t, Bi = e);
}
function Dy() {
  for (var e, t = Fi, n, i = 1 / 0; t; )
    t._call ? (i > t._time && (i = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Fi = n);
  wn = e, Eo(i);
}
function Eo(e) {
  if (!Qt) {
    xn && (xn = clearTimeout(xn));
    var t = e - Et;
    t > 24 ? (e < 1 / 0 && (xn = setTimeout(ua, e - Pn.now() - ir)), hn && (hn = clearInterval(hn))) : (hn || (Bi = Pn.now(), hn = setInterval(_y, Ol)), Qt = 1, Hl(ua));
  }
}
function da(e, t, n) {
  var i = new Ki();
  return t = t == null ? 0 : +t, i.restart((r) => {
    i.stop(), e(r + t);
  }, t, n), i;
}
var Ty = tr("start", "end", "cancel", "interrupt"), $y = [], Fl = 0, fa = 1, ko = 2, Ei = 3, pa = 4, Io = 5, ki = 6;
function rr(e, t, n, i, r, s) {
  var a = e.__transition;
  if (!a) e.__transition = {};
  else if (n in a) return;
  Py(e, n, {
    name: t,
    index: i,
    // For context during callback.
    group: r,
    // For context during callback.
    on: Ty,
    tween: $y,
    time: s.time,
    delay: s.delay,
    duration: s.duration,
    ease: s.ease,
    timer: null,
    state: Fl
  });
}
function ts(e, t) {
  var n = Ue(e, t);
  if (n.state > Fl) throw new Error("too late; already scheduled");
  return n;
}
function nt(e, t) {
  var n = Ue(e, t);
  if (n.state > Ei) throw new Error("too late; already running");
  return n;
}
function Ue(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function Py(e, t, n) {
  var i = e.__transition, r;
  i[t] = n, n.timer = Wl(s, 0, n.time);
  function s(l) {
    n.state = fa, n.timer.restart(a, n.delay, n.time), n.delay <= l && a(l - n.delay);
  }
  function a(l) {
    var d, f, p, h;
    if (n.state !== fa) return u();
    for (d in i)
      if (h = i[d], h.name === n.name) {
        if (h.state === Ei) return da(a);
        h.state === pa ? (h.state = ki, h.timer.stop(), h.on.call("interrupt", e, e.__data__, h.index, h.group), delete i[d]) : +d < t && (h.state = ki, h.timer.stop(), h.on.call("cancel", e, e.__data__, h.index, h.group), delete i[d]);
      }
    if (da(function() {
      n.state === Ei && (n.state = pa, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = ko, n.on.call("start", e, e.__data__, n.index, n.group), n.state === ko) {
      for (n.state = Ei, r = new Array(p = n.tween.length), d = 0, f = -1; d < p; ++d)
        (h = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (r[++f] = h);
      r.length = f + 1;
    }
  }
  function c(l) {
    for (var d = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(u), n.state = Io, 1), f = -1, p = r.length; ++f < p; )
      r[f].call(e, d);
    n.state === Io && (n.on.call("end", e, e.__data__, n.index, n.group), u());
  }
  function u() {
    n.state = ki, n.timer.stop(), delete i[t];
    for (var l in i) return;
    delete e.__transition;
  }
}
function Ii(e, t) {
  var n = e.__transition, i, r, s = !0, a;
  if (n) {
    t = t == null ? null : t + "";
    for (a in n) {
      if ((i = n[a]).name !== t) {
        s = !1;
        continue;
      }
      r = i.state > ko && i.state < Io, i.state = ki, i.timer.stop(), i.on.call(r ? "interrupt" : "cancel", e, e.__data__, i.index, i.group), delete n[a];
    }
    s && delete e.__transition;
  }
}
function My(e) {
  return this.each(function() {
    Ii(this, e);
  });
}
function Ry(e, t) {
  var n, i;
  return function() {
    var r = nt(this, e), s = r.tween;
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
function zy(e, t, n) {
  var i, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var s = nt(this, e), a = s.tween;
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
function Ly(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var i = Ue(this.node(), n).tween, r = 0, s = i.length, a; r < s; ++r)
      if ((a = i[r]).name === e)
        return a.value;
    return null;
  }
  return this.each((t == null ? Ry : zy)(n, e, t));
}
function ns(e, t, n) {
  var i = e._id;
  return e.each(function() {
    var r = nt(this, i);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return Ue(r, i).value[t];
  };
}
function Bl(e, t) {
  var n;
  return (typeof t == "number" ? Ge : t instanceof Ct ? Wi : (n = Ct(t)) ? (t = n, Wi) : zl)(e, t);
}
function Vy(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Oy(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Hy(e, t, n) {
  var i, r = n + "", s;
  return function() {
    var a = this.getAttribute(e);
    return a === r ? null : a === i ? s : s = t(i = a, n);
  };
}
function Wy(e, t, n) {
  var i, r = n + "", s;
  return function() {
    var a = this.getAttributeNS(e.space, e.local);
    return a === r ? null : a === i ? s : s = t(i = a, n);
  };
}
function Fy(e, t, n) {
  var i, r, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttribute(e) : (a = this.getAttribute(e), u = c + "", a === u ? null : a === i && u === r ? s : (r = u, s = t(i = a, c)));
  };
}
function By(e, t, n) {
  var i, r, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (a = this.getAttributeNS(e.space, e.local), u = c + "", a === u ? null : a === i && u === r ? s : (r = u, s = t(i = a, c)));
  };
}
function Ky(e, t) {
  var n = nr(e), i = n === "transform" ? Sy : Bl;
  return this.attrTween(e, typeof t == "function" ? (n.local ? By : Fy)(n, i, ns(this, "attr." + e, t)) : t == null ? (n.local ? Oy : Vy)(n) : (n.local ? Wy : Hy)(n, i, t));
}
function Xy(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function Yy(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function qy(e, t) {
  var n, i;
  function r() {
    var s = t.apply(this, arguments);
    return s !== i && (n = (i = s) && Yy(e, s)), n;
  }
  return r._value = t, r;
}
function Uy(e, t) {
  var n, i;
  function r() {
    var s = t.apply(this, arguments);
    return s !== i && (n = (i = s) && Xy(e, s)), n;
  }
  return r._value = t, r;
}
function Zy(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var i = nr(e);
  return this.tween(n, (i.local ? qy : Uy)(i, t));
}
function Gy(e, t) {
  return function() {
    ts(this, e).delay = +t.apply(this, arguments);
  };
}
function Jy(e, t) {
  return t = +t, function() {
    ts(this, e).delay = t;
  };
}
function Qy(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Gy : Jy)(t, e)) : Ue(this.node(), t).delay;
}
function em(e, t) {
  return function() {
    nt(this, e).duration = +t.apply(this, arguments);
  };
}
function tm(e, t) {
  return t = +t, function() {
    nt(this, e).duration = t;
  };
}
function nm(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? em : tm)(t, e)) : Ue(this.node(), t).duration;
}
function im(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    nt(this, e).ease = t;
  };
}
function rm(e) {
  var t = this._id;
  return arguments.length ? this.each(im(t, e)) : Ue(this.node(), t).ease;
}
function om(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    nt(this, e).ease = n;
  };
}
function sm(e) {
  if (typeof e != "function") throw new Error();
  return this.each(om(this._id, e));
}
function am(e) {
  typeof e != "function" && (e = bl(e));
  for (var t = this._groups, n = t.length, i = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = i[r] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new ct(i, this._parents, this._name, this._id);
}
function cm(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, i = t.length, r = n.length, s = Math.min(i, r), a = new Array(i), c = 0; c < s; ++c)
    for (var u = t[c], l = n[c], d = u.length, f = a[c] = new Array(d), p, h = 0; h < d; ++h)
      (p = u[h] || l[h]) && (f[h] = p);
  for (; c < i; ++c)
    a[c] = t[c];
  return new ct(a, this._parents, this._name, this._id);
}
function lm(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function um(e, t, n) {
  var i, r, s = lm(t) ? ts : nt;
  return function() {
    var a = s(this, e), c = a.on;
    c !== i && (r = (i = c).copy()).on(t, n), a.on = r;
  };
}
function dm(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Ue(this.node(), n).on.on(e) : this.each(um(n, e, t));
}
function fm(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function pm() {
  return this.on("end.remove", fm(this._id));
}
function hm(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Zo(e));
  for (var i = this._groups, r = i.length, s = new Array(r), a = 0; a < r; ++a)
    for (var c = i[a], u = c.length, l = s[a] = new Array(u), d, f, p = 0; p < u; ++p)
      (d = c[p]) && (f = e.call(d, d.__data__, p, c)) && ("__data__" in d && (f.__data__ = d.__data__), l[p] = f, rr(l[p], t, n, p, l, Ue(d, n)));
  return new ct(s, this._parents, t, n);
}
function gm(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = vl(e));
  for (var i = this._groups, r = i.length, s = [], a = [], c = 0; c < r; ++c)
    for (var u = i[c], l = u.length, d, f = 0; f < l; ++f)
      if (d = u[f]) {
        for (var p = e.call(d, d.__data__, f, u), h, y = Ue(d, n), x = 0, m = p.length; x < m; ++x)
          (h = p[x]) && rr(h, t, n, x, p, y);
        s.push(p), a.push(d);
      }
  return new ct(s, a, t, n);
}
var ym = Kn.prototype.constructor;
function mm() {
  return new ym(this._groups, this._parents);
}
function xm(e, t) {
  var n, i, r;
  return function() {
    var s = Jt(this, e), a = (this.style.removeProperty(e), Jt(this, e));
    return s === a ? null : s === n && a === i ? r : r = t(n = s, i = a);
  };
}
function Kl(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function wm(e, t, n) {
  var i, r = n + "", s;
  return function() {
    var a = Jt(this, e);
    return a === r ? null : a === i ? s : s = t(i = a, n);
  };
}
function vm(e, t, n) {
  var i, r, s;
  return function() {
    var a = Jt(this, e), c = n(this), u = c + "";
    return c == null && (u = c = (this.style.removeProperty(e), Jt(this, e))), a === u ? null : a === i && u === r ? s : (r = u, s = t(i = a, c));
  };
}
function bm(e, t) {
  var n, i, r, s = "style." + t, a = "end." + s, c;
  return function() {
    var u = nt(this, e), l = u.on, d = u.value[s] == null ? c || (c = Kl(t)) : void 0;
    (l !== n || r !== d) && (i = (n = l).copy()).on(a, r = d), u.on = i;
  };
}
function Nm(e, t, n) {
  var i = (e += "") == "transform" ? jy : Bl;
  return t == null ? this.styleTween(e, xm(e, i)).on("end.style." + e, Kl(e)) : typeof t == "function" ? this.styleTween(e, vm(e, i, ns(this, "style." + e, t))).each(bm(this._id, e)) : this.styleTween(e, wm(e, i, t), n).on("end.style." + e, null);
}
function jm(e, t, n) {
  return function(i) {
    this.style.setProperty(e, t.call(this, i), n);
  };
}
function Sm(e, t, n) {
  var i, r;
  function s() {
    var a = t.apply(this, arguments);
    return a !== r && (i = (r = a) && jm(e, a, n)), i;
  }
  return s._value = t, s;
}
function Cm(e, t, n) {
  var i = "style." + (e += "");
  if (arguments.length < 2) return (i = this.tween(i)) && i._value;
  if (t == null) return this.tween(i, null);
  if (typeof t != "function") throw new Error();
  return this.tween(i, Sm(e, t, n ?? ""));
}
function Em(e) {
  return function() {
    this.textContent = e;
  };
}
function km(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function Im(e) {
  return this.tween("text", typeof e == "function" ? km(ns(this, "text", e)) : Em(e == null ? "" : e + ""));
}
function Am(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function _m(e) {
  var t, n;
  function i() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Am(r)), t;
  }
  return i._value = e, i;
}
function Dm(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, _m(e));
}
function Tm() {
  for (var e = this._name, t = this._id, n = Xl(), i = this._groups, r = i.length, s = 0; s < r; ++s)
    for (var a = i[s], c = a.length, u, l = 0; l < c; ++l)
      if (u = a[l]) {
        var d = Ue(u, t);
        rr(u, e, n, l, a, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new ct(i, this._parents, e, n);
}
function $m() {
  var e, t, n = this, i = n._id, r = n.size();
  return new Promise(function(s, a) {
    var c = { value: a }, u = { value: function() {
      --r === 0 && s();
    } };
    n.each(function() {
      var l = nt(this, i), d = l.on;
      d !== e && (t = (e = d).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(u)), l.on = t;
    }), r === 0 && s();
  });
}
var Pm = 0;
function ct(e, t, n, i) {
  this._groups = e, this._parents = t, this._name = n, this._id = i;
}
function Xl() {
  return ++Pm;
}
var ot = Kn.prototype;
ct.prototype = {
  constructor: ct,
  select: hm,
  selectAll: gm,
  selectChild: ot.selectChild,
  selectChildren: ot.selectChildren,
  filter: am,
  merge: cm,
  selection: mm,
  transition: Tm,
  call: ot.call,
  nodes: ot.nodes,
  node: ot.node,
  size: ot.size,
  empty: ot.empty,
  each: ot.each,
  on: dm,
  attr: Ky,
  attrTween: Zy,
  style: Nm,
  styleTween: Cm,
  text: Im,
  textTween: Dm,
  remove: pm,
  tween: Ly,
  delay: Qy,
  duration: nm,
  ease: rm,
  easeVarying: sm,
  end: $m,
  [Symbol.iterator]: ot[Symbol.iterator]
};
function Mm(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var Rm = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: Mm
};
function zm(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function Lm(e) {
  var t, n;
  e instanceof ct ? (t = e._id, e = e._name) : (t = Xl(), (n = Rm).time = es(), e = e == null ? null : e + "");
  for (var i = this._groups, r = i.length, s = 0; s < r; ++s)
    for (var a = i[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && rr(u, e, t, l, a, n || zm(u, t));
  return new ct(i, this._parents, e, t);
}
Kn.prototype.interrupt = My;
Kn.prototype.transition = Lm;
const hi = (e) => () => e;
function Vm(e, {
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
function st(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
st.prototype = {
  constructor: st,
  scale: function(e) {
    return e === 1 ? this : new st(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new st(this.k, this.x + this.k * e, this.y + this.k * t);
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
var or = new st(1, 0, 0);
Yl.prototype = st.prototype;
function Yl(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return or;
  return e.__zoom;
}
function no(e) {
  e.stopImmediatePropagation();
}
function gn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Om(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function Hm() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function ha() {
  return this.__zoom || or;
}
function Wm(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function Fm() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Bm(e, t, n) {
  var i = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], s = e.invertY(t[0][1]) - n[0][1], a = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > i ? (i + r) / 2 : Math.min(0, i) || Math.max(0, r),
    a > s ? (s + a) / 2 : Math.min(0, s) || Math.max(0, a)
  );
}
function ql() {
  var e = Om, t = Hm, n = Bm, i = Wm, r = Fm, s = [0, 1 / 0], a = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, u = Ci, l = tr("start", "zoom", "end"), d, f, p, h = 500, y = 150, x = 0, m = 10;
  function w(C) {
    C.property("__zoom", ha).on("wheel.zoom", I, { passive: !1 }).on("mousedown.zoom", _).on("dblclick.zoom", M).filter(r).on("touchstart.zoom", D).on("touchmove.zoom", k).on("touchend.zoom touchcancel.zoom", R).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  w.transform = function(C, A, E, T) {
    var $ = C.selection ? C.selection() : C;
    $.property("__zoom", ha), C !== $ ? j(C, A, E, T) : $.interrupt().each(function() {
      N(this, arguments).event(T).start().zoom(null, typeof A == "function" ? A.apply(this, arguments) : A).end();
    });
  }, w.scaleBy = function(C, A, E, T) {
    w.scaleTo(C, function() {
      var $ = this.__zoom.k, P = typeof A == "function" ? A.apply(this, arguments) : A;
      return $ * P;
    }, E, T);
  }, w.scaleTo = function(C, A, E, T) {
    w.transform(C, function() {
      var $ = t.apply(this, arguments), P = this.__zoom, F = E == null ? v($) : typeof E == "function" ? E.apply(this, arguments) : E, W = P.invert(F), H = typeof A == "function" ? A.apply(this, arguments) : A;
      return n(g(b(P, H), F, W), $, a);
    }, E, T);
  }, w.translateBy = function(C, A, E, T) {
    w.transform(C, function() {
      return n(this.__zoom.translate(
        typeof A == "function" ? A.apply(this, arguments) : A,
        typeof E == "function" ? E.apply(this, arguments) : E
      ), t.apply(this, arguments), a);
    }, null, T);
  }, w.translateTo = function(C, A, E, T, $) {
    w.transform(C, function() {
      var P = t.apply(this, arguments), F = this.__zoom, W = T == null ? v(P) : typeof T == "function" ? T.apply(this, arguments) : T;
      return n(or.translate(W[0], W[1]).scale(F.k).translate(
        typeof A == "function" ? -A.apply(this, arguments) : -A,
        typeof E == "function" ? -E.apply(this, arguments) : -E
      ), P, a);
    }, T, $);
  };
  function b(C, A) {
    return A = Math.max(s[0], Math.min(s[1], A)), A === C.k ? C : new st(A, C.x, C.y);
  }
  function g(C, A, E) {
    var T = A[0] - E[0] * C.k, $ = A[1] - E[1] * C.k;
    return T === C.x && $ === C.y ? C : new st(C.k, T, $);
  }
  function v(C) {
    return [(+C[0][0] + +C[1][0]) / 2, (+C[0][1] + +C[1][1]) / 2];
  }
  function j(C, A, E, T) {
    C.on("start.zoom", function() {
      N(this, arguments).event(T).start();
    }).on("interrupt.zoom end.zoom", function() {
      N(this, arguments).event(T).end();
    }).tween("zoom", function() {
      var $ = this, P = arguments, F = N($, P).event(T), W = t.apply($, P), H = E == null ? v(W) : typeof E == "function" ? E.apply($, P) : E, q = Math.max(W[1][0] - W[0][0], W[1][1] - W[0][1]), Z = $.__zoom, ne = typeof A == "function" ? A.apply($, P) : A, le = u(Z.invert(H).concat(q / Z.k), ne.invert(H).concat(q / ne.k));
      return function(G) {
        if (G === 1) G = ne;
        else {
          var z = le(G), X = q / z[2];
          G = new st(X, H[0] - z[0] * X, H[1] - z[1] * X);
        }
        F.zoom(null, G);
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
      var A = ze(this.that).datum();
      l.call(
        C,
        this.that,
        new Vm(C, {
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
    var E = N(this, A).event(C), T = this.__zoom, $ = Math.max(s[0], Math.min(s[1], T.k * Math.pow(2, i.apply(this, arguments)))), P = Ke(C);
    if (E.wheel)
      (E.mouse[0][0] !== P[0] || E.mouse[0][1] !== P[1]) && (E.mouse[1] = T.invert(E.mouse[0] = P)), clearTimeout(E.wheel);
    else {
      if (T.k === $) return;
      E.mouse = [P, T.invert(P)], Ii(this), E.start();
    }
    gn(C), E.wheel = setTimeout(F, y), E.zoom("mouse", n(g(b(T, $), E.mouse[0], E.mouse[1]), E.extent, a));
    function F() {
      E.wheel = null, E.end();
    }
  }
  function _(C, ...A) {
    if (p || !e.apply(this, arguments)) return;
    var E = C.currentTarget, T = N(this, A, !0).event(C), $ = ze(C.view).on("mousemove.zoom", H, !0).on("mouseup.zoom", q, !0), P = Ke(C, E), F = C.clientX, W = C.clientY;
    Dl(C.view), no(C), T.mouse = [P, this.__zoom.invert(P)], Ii(this), T.start();
    function H(Z) {
      if (gn(Z), !T.moved) {
        var ne = Z.clientX - F, le = Z.clientY - W;
        T.moved = ne * ne + le * le > x;
      }
      T.event(Z).zoom("mouse", n(g(T.that.__zoom, T.mouse[0] = Ke(Z, E), T.mouse[1]), T.extent, a));
    }
    function q(Z) {
      $.on("mousemove.zoom mouseup.zoom", null), Tl(Z.view, T.moved), gn(Z), T.event(Z).end();
    }
  }
  function M(C, ...A) {
    if (e.apply(this, arguments)) {
      var E = this.__zoom, T = Ke(C.changedTouches ? C.changedTouches[0] : C, this), $ = E.invert(T), P = E.k * (C.shiftKey ? 0.5 : 2), F = n(g(b(E, P), T, $), t.apply(this, A), a);
      gn(C), c > 0 ? ze(this).transition().duration(c).call(j, F, T, C) : ze(this).call(w.transform, F, T, C);
    }
  }
  function D(C, ...A) {
    if (e.apply(this, arguments)) {
      var E = C.touches, T = E.length, $ = N(this, A, C.changedTouches.length === T).event(C), P, F, W, H;
      for (no(C), F = 0; F < T; ++F)
        W = E[F], H = Ke(W, this), H = [H, this.__zoom.invert(H), W.identifier], $.touch0 ? !$.touch1 && $.touch0[2] !== H[2] && ($.touch1 = H, $.taps = 0) : ($.touch0 = H, P = !0, $.taps = 1 + !!d);
      d && (d = clearTimeout(d)), P && ($.taps < 2 && (f = H[0], d = setTimeout(function() {
        d = null;
      }, h)), Ii(this), $.start());
    }
  }
  function k(C, ...A) {
    if (this.__zooming) {
      var E = N(this, A).event(C), T = C.changedTouches, $ = T.length, P, F, W, H;
      for (gn(C), P = 0; P < $; ++P)
        F = T[P], W = Ke(F, this), E.touch0 && E.touch0[2] === F.identifier ? E.touch0[0] = W : E.touch1 && E.touch1[2] === F.identifier && (E.touch1[0] = W);
      if (F = E.that.__zoom, E.touch1) {
        var q = E.touch0[0], Z = E.touch0[1], ne = E.touch1[0], le = E.touch1[1], G = (G = ne[0] - q[0]) * G + (G = ne[1] - q[1]) * G, z = (z = le[0] - Z[0]) * z + (z = le[1] - Z[1]) * z;
        F = b(F, Math.sqrt(G / z)), W = [(q[0] + ne[0]) / 2, (q[1] + ne[1]) / 2], H = [(Z[0] + le[0]) / 2, (Z[1] + le[1]) / 2];
      } else if (E.touch0) W = E.touch0[0], H = E.touch0[1];
      else return;
      E.zoom("touch", n(g(F, W, H), E.extent, a));
    }
  }
  function R(C, ...A) {
    if (this.__zooming) {
      var E = N(this, A).event(C), T = C.changedTouches, $ = T.length, P, F;
      for (no(C), p && clearTimeout(p), p = setTimeout(function() {
        p = null;
      }, h), P = 0; P < $; ++P)
        F = T[P], E.touch0 && E.touch0[2] === F.identifier ? delete E.touch0 : E.touch1 && E.touch1[2] === F.identifier && delete E.touch1;
      if (E.touch1 && !E.touch0 && (E.touch0 = E.touch1, delete E.touch1), E.touch0) E.touch0[1] = this.__zoom.invert(E.touch0[0]);
      else if (E.end(), E.taps === 2 && (F = Ke(F, this), Math.hypot(f[0] - F[0], f[1] - F[1]) < m)) {
        var W = ze(this).on("dblclick.zoom");
        W && W.apply(this, arguments);
      }
    }
  }
  return w.wheelDelta = function(C) {
    return arguments.length ? (i = typeof C == "function" ? C : hi(+C), w) : i;
  }, w.filter = function(C) {
    return arguments.length ? (e = typeof C == "function" ? C : hi(!!C), w) : e;
  }, w.touchable = function(C) {
    return arguments.length ? (r = typeof C == "function" ? C : hi(!!C), w) : r;
  }, w.extent = function(C) {
    return arguments.length ? (t = typeof C == "function" ? C : hi([[+C[0][0], +C[0][1]], [+C[1][0], +C[1][1]]]), w) : t;
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
    return arguments.length ? (x = (C = +C) * C, w) : Math.sqrt(x);
  }, w.tapDistance = function(C) {
    return arguments.length ? (m = +C, w) : m;
  }, w;
}
const He = {
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
}, Mn = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], Ul = ["Enter", " ", "Escape"], Zl = {
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
var St;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(St || (St = {}));
var Rn;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Rn || (Rn = {}));
const Gl = {
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
var pt;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(pt || (pt = {}));
var Xi;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(Xi || (Xi = {}));
var ie;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(ie || (ie = {}));
const ga = {
  [ie.Left]: ie.Right,
  [ie.Right]: ie.Left,
  [ie.Top]: ie.Bottom,
  [ie.Bottom]: ie.Top
};
function Jl(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Ql = (e) => "id" in e && "source" in e && "target" in e, Km = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), is = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), Yn = (e, t = [0, 0]) => {
  const { width: n, height: i } = lt(e), r = e.origin ?? t, s = n * r[0], a = i * r[1];
  return {
    x: e.position.x - s,
    y: e.position.y - a
  };
}, Xm = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((i, r) => {
    const s = typeof r == "string";
    let a = !t.nodeLookup && !s ? r : void 0;
    t.nodeLookup && (a = s ? t.nodeLookup.get(r) : is(r) ? r : t.nodeLookup.get(r.id));
    const c = a ? Yi(a, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return sr(i, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return ar(n);
}, qn = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, i = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = sr(n, Yi(r)), i = !0);
  }), i ? ar(n) : { x: 0, y: 0, width: 0, height: 0 };
}, rs = (e, t, [n, i, r] = [0, 0, 1], s = !1, a = !1) => {
  const c = {
    ...ln(t, [n, i, r]),
    width: t.width / r,
    height: t.height / r
  }, u = [];
  for (const l of e.values()) {
    const { measured: d, selectable: f = !0, hidden: p = !1 } = l;
    if (a && !f || p)
      continue;
    const h = d.width ?? l.width ?? l.initialWidth ?? null, y = d.height ?? l.height ?? l.initialHeight ?? null, x = zn(c, nn(l)), m = (h ?? 0) * (y ?? 0), w = s && x > 0;
    (!l.internals.handleBounds || w || x >= m || l.dragging) && u.push(l);
  }
  return u;
}, Ym = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((i) => {
    n.add(i.id);
  }), t.filter((i) => n.has(i.source) || n.has(i.target));
};
function qm(e, t) {
  const n = /* @__PURE__ */ new Map(), i = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!i || i.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function Um({ nodes: e, width: t, height: n, panZoom: i, minZoom: r, maxZoom: s }, a) {
  if (e.size === 0)
    return !0;
  const c = qm(e, a), u = qn(c), l = ss(u, t, n, a?.minZoom ?? r, a?.maxZoom ?? s, a?.padding ?? 0.1);
  return await i.setViewport(l, {
    duration: a?.duration,
    ease: a?.ease,
    interpolate: a?.interpolate
  }), !0;
}
function eu({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: i = [0, 0], nodeExtent: r, onError: s }) {
  const a = n.get(e), c = a.parentId ? n.get(a.parentId) : void 0, { x: u, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, d = a.origin ?? i;
  let f = a.extent || r;
  if (a.extent === "parent" && !a.expandParent)
    if (!c)
      s?.("005", He.error005());
    else {
      const h = c.measured.width, y = c.measured.height;
      h && y && (f = [
        [u, l],
        [u + h, l + y]
      ]);
    }
  else c && It(a.extent) && (f = [
    [a.extent[0][0] + u, a.extent[0][1] + l],
    [a.extent[1][0] + u, a.extent[1][1] + l]
  ]);
  const p = It(f) ? kt(t, f, a.measured) : t;
  return (a.measured.width === void 0 || a.measured.height === void 0) && s?.("015", He.error015()), {
    position: {
      x: p.x - u + (a.measured.width ?? 0) * d[0],
      y: p.y - l + (a.measured.height ?? 0) * d[1]
    },
    positionAbsolute: p
  };
}
async function Zm({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: i, onBeforeDelete: r }) {
  const s = new Set(e.map((p) => p.id)), a = [];
  for (const p of n) {
    if (p.deletable === !1)
      continue;
    const h = s.has(p.id), y = !h && p.parentId && a.find((x) => x.id === p.parentId);
    (h || y) && a.push(p);
  }
  const c = new Set(t.map((p) => p.id)), u = i.filter((p) => p.deletable !== !1), d = Ym(a, u);
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
const tn = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), kt = (e = { x: 0, y: 0 }, t, n) => ({
  x: tn(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: tn(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function tu(e, t, n) {
  const { width: i, height: r } = lt(n), { x: s, y: a } = n.internals.positionAbsolute;
  return kt(e, [
    [s, a],
    [s + i, a + r]
  ], t);
}
const ya = (e, t, n) => e < t ? tn(Math.abs(e - t), 1, t) / t : e > n ? -tn(Math.abs(e - n), 1, t) / t : 0, os = (e, t, n = 15, i = 40) => {
  const r = ya(e.x, i, t.width - i) * n, s = ya(e.y, i, t.height - i) * n;
  return [r, s];
}, sr = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), Ao = ({ x: e, y: t, width: n, height: i }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + i
}), ar = ({ x: e, y: t, x2: n, y2: i }) => ({
  x: e,
  y: t,
  width: n - e,
  height: i - t
}), nn = (e, t = [0, 0]) => {
  const { x: n, y: i } = is(e) ? e.internals.positionAbsolute : Yn(e, t);
  return {
    x: n,
    y: i,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, Yi = (e, t = [0, 0]) => {
  const { x: n, y: i } = is(e) ? e.internals.positionAbsolute : Yn(e, t);
  return {
    x: n,
    y: i,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: i + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, nu = (e, t) => ar(sr(Ao(e), Ao(t))), zn = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), i = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * i);
}, ma = (e) => Ye(e.width) && Ye(e.height) && Ye(e.x) && Ye(e.y), Ye = (e) => !isNaN(e) && isFinite(e), iu = (e, t) => (n, i) => {
}, Un = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), ln = ({ x: e, y: t }, [n, i, r], s = !1, a = [1, 1]) => {
  const c = {
    x: (e - n) / r,
    y: (t - i) / r
  };
  return s ? Un(c, a) : c;
}, rn = ({ x: e, y: t }, [n, i, r]) => ({
  x: e * r + n,
  y: t * r + i
});
function Ot(e, t) {
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
function Gm(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const i = Ot(e, n), r = Ot(e, t);
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
    const i = Ot(e.top ?? e.y ?? 0, n), r = Ot(e.bottom ?? e.y ?? 0, n), s = Ot(e.left ?? e.x ?? 0, t), a = Ot(e.right ?? e.x ?? 0, t);
    return { top: i, right: a, bottom: r, left: s, x: s + a, y: i + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function Jm(e, t, n, i, r, s) {
  const { x: a, y: c } = rn(e, [t, n, i]), { x: u, y: l } = rn({ x: e.x + e.width, y: e.y + e.height }, [t, n, i]), d = r - u, f = s - l;
  return {
    left: Math.floor(a),
    top: Math.floor(c),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const ss = (e, t, n, i, r, s) => {
  const a = Gm(s, t, n), c = (t - a.x) / e.width, u = (n - a.y) / e.height, l = Math.min(c, u), d = tn(l, i, r), f = e.x + e.width / 2, p = e.y + e.height / 2, h = t / 2 - f * d, y = n / 2 - p * d, x = Jm(e, h, y, d, t, n), m = {
    left: Math.min(x.left - a.left, 0),
    top: Math.min(x.top - a.top, 0),
    right: Math.min(x.right - a.right, 0),
    bottom: Math.min(x.bottom - a.bottom, 0)
  };
  return {
    x: h - m.left + m.right,
    y: y - m.top + m.bottom,
    zoom: d
  };
}, Ln = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function It(e) {
  return e != null && e !== "parent";
}
function lt(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function ru(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function ou(e, t = { width: 0, height: 0 }, n, i, r) {
  const s = { ...e }, a = i.get(n);
  if (a) {
    const c = a.origin || r;
    s.x += a.internals.positionAbsolute.x - (t.width ?? 0) * c[0], s.y += a.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return s;
}
function xa(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function Qm() {
  let e, t;
  return { promise: new Promise((i, r) => {
    e = i, t = r;
  }), resolve: e, reject: t };
}
function ex(e) {
  return { ...Zl, ...e || {} };
}
function kn(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: i, containerBounds: r }) {
  const { x: s, y: a } = qe(e), c = ln({ x: s - (r?.left ?? 0), y: a - (r?.top ?? 0) }, i), { x: u, y: l } = n ? Un(c, t) : c;
  return {
    xSnapped: u,
    ySnapped: l,
    ...c
  };
}
const as = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), su = (e) => e?.getRootNode?.() || window?.document, tx = ["INPUT", "SELECT", "TEXTAREA"];
function au(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : tx.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const cu = (e) => "clientX" in e, qe = (e, t) => {
  const n = cu(e), i = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: i - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, wa = (e, t, n, i, r) => {
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
      ...as(a)
    };
  });
};
function lu({ sourceX: e, sourceY: t, targetX: n, targetY: i, sourceControlX: r, sourceControlY: s, targetControlX: a, targetControlY: c }) {
  const u = e * 0.125 + r * 0.375 + a * 0.375 + n * 0.125, l = t * 0.125 + s * 0.375 + c * 0.375 + i * 0.125, d = Math.abs(u - e), f = Math.abs(l - t);
  return [u, l, d, f];
}
function gi(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function va({ pos: e, x1: t, y1: n, x2: i, y2: r, c: s }) {
  switch (e) {
    case ie.Left:
      return [t - gi(t - i, s), n];
    case ie.Right:
      return [t + gi(i - t, s), n];
    case ie.Top:
      return [t, n - gi(n - r, s)];
    case ie.Bottom:
      return [t, n + gi(r - n, s)];
  }
}
function uu({ sourceX: e, sourceY: t, sourcePosition: n = ie.Bottom, targetX: i, targetY: r, targetPosition: s = ie.Top, curvature: a = 0.25 }) {
  const [c, u] = va({
    pos: n,
    x1: e,
    y1: t,
    x2: i,
    y2: r,
    c: a
  }), [l, d] = va({
    pos: s,
    x1: i,
    y1: r,
    x2: e,
    y2: t,
    c: a
  }), [f, p, h, y] = lu({
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
function du({ sourceX: e, sourceY: t, targetX: n, targetY: i }) {
  const r = Math.abs(n - e) / 2, s = n < e ? n + r : n - r, a = Math.abs(i - t) / 2, c = i < t ? i + a : i - a;
  return [s, c, r, a];
}
function nx({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: i = 0, elevateOnSelect: r = !1, zIndexMode: s = "basic" }) {
  if (s === "manual")
    return i;
  const a = r && n ? i + 1e3 : i, c = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return a + c;
}
function ix({ sourceNode: e, targetNode: t, width: n, height: i, transform: r }) {
  const s = sr(Yi(e), Yi(t));
  s.x === s.x2 && (s.x2 += 1), s.y === s.y2 && (s.y2 += 1);
  const a = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: i / r[2]
  };
  return zn(a, ar(s)) > 0;
}
const fu = ({ source: e, sourceHandle: t, target: n, targetHandle: i }) => `xy-edge__${e}${t || ""}-${n}${i || ""}`, rx = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), ox = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", He.error006()), t;
  const i = n.getEdgeId || fu;
  let r;
  return Ql(e) ? r = { ...e } : r = {
    ...e,
    id: i(e)
  }, rx(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, sx = (e, t, n, i = { shouldReplaceId: !0 }) => {
  const { id: r, ...s } = e;
  if (!t.source || !t.target)
    return i.onError?.("006", He.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return i.onError?.("007", He.error007(r)), n;
  const c = i.getEdgeId || fu, u = {
    ...s,
    id: i.shouldReplaceId ? c(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== r).concat(u);
};
function pu({ sourceX: e, sourceY: t, targetX: n, targetY: i }) {
  const [r, s, a, c] = du({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: i
  });
  return [`M ${e},${t}L ${n},${i}`, r, s, a, c];
}
const ba = {
  [ie.Left]: { x: -1, y: 0 },
  [ie.Right]: { x: 1, y: 0 },
  [ie.Top]: { x: 0, y: -1 },
  [ie.Bottom]: { x: 0, y: 1 }
}, ax = ({ source: e, sourcePosition: t = ie.Bottom, target: n }) => t === ie.Left || t === ie.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, Na = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function cx({ source: e, sourcePosition: t = ie.Bottom, target: n, targetPosition: i = ie.Top, center: r, offset: s, stepPosition: a }) {
  const c = ba[t], u = ba[i], l = { x: e.x + c.x * s, y: e.y + c.y * s }, d = { x: n.x + u.x * s, y: n.y + u.y * s }, f = ax({
    source: l,
    sourcePosition: t,
    target: d
  }), p = f.x !== 0 ? "x" : "y", h = f[p];
  let y = [], x, m;
  const w = { x: 0, y: 0 }, b = { x: 0, y: 0 }, [, , g, v] = du({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[p] * u[p] === -1) {
    p === "x" ? (x = r.x ?? l.x + (d.x - l.x) * a, m = r.y ?? (l.y + d.y) / 2) : (x = r.x ?? (l.x + d.x) / 2, m = r.y ?? l.y + (d.y - l.y) * a);
    const I = [
      { x, y: l.y },
      { x, y: d.y }
    ], _ = [
      { x: l.x, y: m },
      { x: d.x, y: m }
    ];
    c[p] === h ? y = p === "x" ? I : _ : y = p === "x" ? _ : I;
  } else {
    const I = [{ x: l.x, y: d.y }], _ = [{ x: d.x, y: l.y }];
    if (p === "x" ? y = c.x === h ? _ : I : y = c.y === h ? I : _, t === i) {
      const C = Math.abs(e[p] - n[p]);
      if (C <= s) {
        const A = Math.min(s - 1, s - C);
        c[p] === h ? w[p] = (l[p] > e[p] ? -1 : 1) * A : b[p] = (d[p] > n[p] ? -1 : 1) * A;
      }
    }
    if (t !== i) {
      const C = p === "x" ? "y" : "x", A = c[p] === u[C], E = l[C] > d[C], T = l[C] < d[C];
      (c[p] === 1 && (!A && E || A && T) || c[p] !== 1 && (!A && T || A && E)) && (y = p === "x" ? I : _);
    }
    const M = { x: l.x + w.x, y: l.y + w.y }, D = { x: d.x + b.x, y: d.y + b.y }, k = Math.max(Math.abs(M.x - y[0].x), Math.abs(D.x - y[0].x)), R = Math.max(Math.abs(M.y - y[0].y), Math.abs(D.y - y[0].y));
    k >= R ? (x = (M.x + D.x) / 2, m = y[0].y) : (x = y[0].x, m = (M.y + D.y) / 2);
  }
  const j = { x: l.x + w.x, y: l.y + w.y }, N = { x: d.x + b.x, y: d.y + b.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...j.x !== y[0].x || j.y !== y[0].y ? [j] : [],
    ...y,
    ...N.x !== y[y.length - 1].x || N.y !== y[y.length - 1].y ? [N] : [],
    n
  ], x, m, g, v];
}
function lx(e, t, n, i) {
  const r = Math.min(Na(e, t) / 2, Na(t, n) / 2, i), { x: s, y: a } = t;
  if (e.x === s && s === n.x || e.y === a && a === n.y)
    return `L${s} ${a}`;
  if (e.y === a) {
    const l = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${s + r * l},${a}Q ${s},${a} ${s},${a + r * d}`;
  }
  const c = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${s},${a + r * u}Q ${s},${a} ${s + r * c},${a}`;
}
function qi({ sourceX: e, sourceY: t, sourcePosition: n = ie.Bottom, targetX: i, targetY: r, targetPosition: s = ie.Top, borderRadius: a = 5, centerX: c, centerY: u, offset: l = 20, stepPosition: d = 0.5 }) {
  const [f, p, h, y, x] = cx({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: i, y: r },
    targetPosition: s,
    center: { x: c, y: u },
    offset: l,
    stepPosition: d
  });
  let m = `M${f[0].x} ${f[0].y}`;
  for (let w = 1; w < f.length - 1; w++)
    m += lx(f[w - 1], f[w], f[w + 1], a);
  return m += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [m, p, h, y, x];
}
function ja(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function ux(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!ja(t) || !ja(n))
    return null;
  const i = t.internals.handleBounds || Sa(t.handles), r = n.internals.handleBounds || Sa(n.handles), s = Ca(i?.source ?? [], e.sourceHandle), a = Ca(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === en.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!s || !a)
    return e.onError?.("008", He.error008(s ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = s?.position || ie.Bottom, u = a?.position || ie.Top, l = At(t, s, c), d = At(n, a, u);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: d.x,
    targetY: d.y,
    sourcePosition: c,
    targetPosition: u
  };
}
function Sa(e) {
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
function At(e, t, n = ie.Left, i = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, s = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: a, height: c } = t ?? lt(e);
  if (i)
    return { x: r + a / 2, y: s + c / 2 };
  switch (t?.position ?? n) {
    case ie.Top:
      return { x: r + a / 2, y: s };
    case ie.Right:
      return { x: r + a, y: s + c / 2 };
    case ie.Bottom:
      return { x: r + a / 2, y: s + c };
    case ie.Left:
      return { x: r, y: s + c / 2 };
  }
}
function Ca(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function _o(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((i) => `${i}=${e[i]}`).join("&")}` : "";
}
function dx(e, { id: t, defaultColor: n, defaultMarkerStart: i, defaultMarkerEnd: r }) {
  const s = /* @__PURE__ */ new Set();
  return e.reduce((a, c) => ([c.markerStart || i, c.markerEnd || r].forEach((u) => {
    if (u && typeof u == "object") {
      const l = _o(u, t);
      s.has(l) || (a.push({ id: l, color: u.color || n, ...u }), s.add(l));
    }
  }), a), []).sort((a, c) => a.id.localeCompare(c.id));
}
const hu = 1e3, fx = 10, cs = {
  nodeOrigin: [0, 0],
  nodeExtent: Mn,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, px = {
  ...cs,
  checkEquality: !0
};
function ls(e, t) {
  const n = { ...e };
  for (const i in t)
    t[i] !== void 0 && (n[i] = t[i]);
  return n;
}
function hx(e, t, n) {
  const i = ls(cs, n);
  for (const r of e.values())
    if (r.parentId)
      ds(r, e, t, i);
    else {
      const s = Yn(r, i.nodeOrigin), a = It(r.extent) ? r.extent : i.nodeExtent, c = kt(s, a, lt(r));
      r.internals.positionAbsolute = c;
    }
}
function gx(e, t) {
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
function us(e) {
  return e === "manual";
}
function Do(e, t, n, i = {}) {
  const r = ls(px, i), s = { i: 0 }, a = new Map(t), c = r?.elevateNodesOnSelect && !us(r.zIndexMode) ? hu : 0;
  let u = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = a.get(d.id);
    if (r.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const p = Yn(d, r.nodeOrigin), h = It(d.extent) ? d.extent : r.nodeExtent, y = kt(p, h, lt(d));
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
          handleBounds: gx(d, f),
          z: gu(d, c, r.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (u = !1), d.parentId && ds(f, t, n, i, s), l ||= d.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: l };
}
function yx(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function ds(e, t, n, i, r) {
  const { elevateNodesOnSelect: s, nodeOrigin: a, nodeExtent: c, zIndexMode: u } = ls(cs, i), l = e.parentId, d = t.get(l);
  if (!d) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  yx(e, n), r && !d.parentId && d.internals.rootParentIndex === void 0 && u === "auto" && (d.internals.rootParentIndex = ++r.i, d.internals.z = d.internals.z + r.i * fx), r && d.internals.rootParentIndex !== void 0 && (r.i = d.internals.rootParentIndex);
  const f = s && !us(u) ? hu : 0, { x: p, y: h, z: y } = mx(e, d, a, c, f, u), { positionAbsolute: x } = e.internals, m = p !== x.x || h !== x.y;
  (m || y !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: m ? { x: p, y: h } : x,
      z: y
    }
  });
}
function gu(e, t, n) {
  const i = Ye(e.zIndex) ? e.zIndex : 0;
  return us(n) ? i : i + (e.selected ? t : 0);
}
function mx(e, t, n, i, r, s) {
  const { x: a, y: c } = t.internals.positionAbsolute, u = lt(e), l = Yn(e, n), d = It(e.extent) ? kt(l, e.extent, u) : l;
  let f = kt({ x: a + d.x, y: c + d.y }, i, u);
  e.extent === "parent" && (f = tu(f, u, t));
  const p = gu(e, r, s), h = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: h >= p ? h + 1 : p
  };
}
function fs(e, t, n, i = [0, 0]) {
  const r = [], s = /* @__PURE__ */ new Map();
  for (const a of e) {
    const c = t.get(a.parentId);
    if (!c)
      continue;
    const u = s.get(a.parentId)?.expandedRect ?? nn(c), l = nu(u, a.rect);
    s.set(a.parentId, { expandedRect: l, parent: c });
  }
  return s.size > 0 && s.forEach(({ expandedRect: a, parent: c }, u) => {
    const l = c.internals.positionAbsolute, d = lt(c), f = c.origin ?? i, p = a.x < l.x ? Math.round(Math.abs(l.x - a.x)) : 0, h = a.y < l.y ? Math.round(Math.abs(l.y - a.y)) : 0, y = Math.max(d.width, Math.round(a.width)), x = Math.max(d.height, Math.round(a.height)), m = (y - d.width) * f[0], w = (x - d.height) * f[1];
    (p > 0 || h > 0 || m || w) && (r.push({
      id: u,
      type: "position",
      position: {
        x: c.position.x - p + m,
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
        width: y + (p ? f[0] * p - m : 0),
        height: x + (h ? f[1] * h - w : 0)
      }
    });
  }), r;
}
function xx(e, t, n, i, r, s, a) {
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
    const x = as(h.nodeElement), m = y.measured.width !== x.width || y.measured.height !== x.height;
    if (!!(x.width && x.height && (m || !y.internals.handleBounds || h.force))) {
      const b = h.nodeElement.getBoundingClientRect(), g = It(y.extent) ? y.extent : s;
      let { positionAbsolute: v } = y.internals;
      y.parentId && y.extent === "parent" ? v = tu(v, x, t.get(y.parentId)) : g && (v = kt(v, g, x));
      const j = {
        ...y,
        measured: x,
        internals: {
          ...y.internals,
          positionAbsolute: v,
          handleBounds: {
            source: wa("source", h.nodeElement, b, f, y.id),
            target: wa("target", h.nodeElement, b, f, y.id)
          }
        }
      };
      t.set(y.id, j), y.parentId && ds(j, t, n, { nodeOrigin: r, zIndexMode: a }), u = !0, m && (l.push({
        id: y.id,
        type: "dimensions",
        dimensions: x
      }), y.expandParent && y.parentId && p.push({
        id: y.id,
        parentId: y.parentId,
        rect: nn(j, r)
      }));
    }
  }
  if (p.length > 0) {
    const h = fs(p, t, n, r);
    l.push(...h);
  }
  return { changes: l, updatedInternals: u };
}
async function wx({ delta: e, panZoom: t, transform: n, translateExtent: i, width: r, height: s }) {
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
function Ea(e, t, n, i, r, s) {
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
function yu(e, t, n) {
  e.clear(), t.clear();
  for (const i of n) {
    const { source: r, target: s, sourceHandle: a = null, targetHandle: c = null } = i, u = { edgeId: i.id, source: r, target: s, sourceHandle: a, targetHandle: c }, l = `${r}-${a}--${s}-${c}`, d = `${s}-${c}--${r}-${a}`;
    Ea("source", u, d, e, r, a), Ea("target", u, l, e, s, c), t.set(i.id, i);
  }
}
function mu(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : mu(n, t) : !1;
}
function ka(e, t, n) {
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
function vx(e, t, n, i) {
  const r = /* @__PURE__ */ new Map();
  for (const [s, a] of e)
    if ((a.selected || a.id === i) && (!a.parentId || !mu(a, e)) && (a.draggable || t && typeof a.draggable > "u")) {
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
function io({ nodeId: e, dragItems: t, nodeLookup: n, dragging: i = !0 }) {
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
function bx({ dragItems: e, snapGrid: t, x: n, y: i }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const s = {
    x: n - r.distance.x,
    y: i - r.distance.y
  }, a = Un(s, t);
  return {
    x: a.x - s.x,
    y: a.y - s.y
  };
}
function Nx({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: i, onDragStop: r }) {
  let s = { x: null, y: null }, a = 0, c = /* @__PURE__ */ new Map(), u = !1, l = { x: 0, y: 0 }, d = null, f = !1, p = null, h = !1, y = !1, x = null;
  function m({ noDragClassName: b, handleSelector: g, domNode: v, isSelectable: j, nodeId: N, nodeClickDistance: S = 0 }) {
    p = ze(v);
    function I({ x: k, y: R }) {
      const { nodeLookup: C, nodeExtent: A, snapGrid: E, snapToGrid: T, nodeOrigin: $, onNodeDrag: P, onSelectionDrag: F, onError: W, updateNodePositions: H } = t();
      s = { x: k, y: R };
      let q = !1;
      const Z = c.size > 1, ne = Z && A ? Ao(qn(c)) : null, le = Z && T ? bx({
        dragItems: c,
        snapGrid: E,
        x: k,
        y: R
      }) : null;
      for (const [G, z] of c) {
        if (!C.has(G))
          continue;
        let X = { x: k - z.distance.x, y: R - z.distance.y };
        T && (X = le ? {
          x: Math.round(X.x + le.x),
          y: Math.round(X.y + le.y)
        } : Un(X, E));
        let ae = null;
        if (Z && A && !z.extent && ne) {
          const { positionAbsolute: te } = z.internals, fe = te.x - ne.x + A[0][0], O = te.x + z.measured.width - ne.x2 + A[1][0], ee = te.y - ne.y + A[0][1], he = te.y + z.measured.height - ne.y2 + A[1][1];
          ae = [
            [fe, ee],
            [O, he]
          ];
        }
        const { position: ce, positionAbsolute: Q } = eu({
          nodeId: G,
          nextPosition: X,
          nodeLookup: C,
          nodeExtent: ae || A,
          nodeOrigin: $,
          onError: W
        });
        q = q || z.position.x !== ce.x || z.position.y !== ce.y, z.position = ce, z.internals.positionAbsolute = Q;
      }
      if (y = y || q, !!q && (H(c, !0), x && (i || P || !N && F))) {
        const [G, z] = io({
          nodeId: N,
          dragItems: c,
          nodeLookup: C
        });
        i?.(x, c, G, z), P?.(x, G, z), N || F?.(x, z);
      }
    }
    async function _() {
      if (!d)
        return;
      const { transform: k, panBy: R, autoPanSpeed: C, autoPanOnNodeDrag: A } = t();
      if (!A) {
        u = !1, cancelAnimationFrame(a);
        return;
      }
      const [E, T] = os(l, d, C);
      (E !== 0 || T !== 0) && (s.x = (s.x ?? 0) - E / k[2], s.y = (s.y ?? 0) - T / k[2], await R({ x: E, y: T }) && I(s)), a = requestAnimationFrame(_);
    }
    function M(k) {
      const { nodeLookup: R, multiSelectionActive: C, nodesDraggable: A, transform: E, snapGrid: T, snapToGrid: $, selectNodesOnDrag: P, onNodeDragStart: F, onSelectionDragStart: W, unselectNodesAndEdges: H } = t();
      f = !0, (!P || !j) && !C && N && (R.get(N)?.selected || H()), j && P && N && e?.(N);
      const q = kn(k.sourceEvent, { transform: E, snapGrid: T, snapToGrid: $, containerBounds: d });
      if (s = q, c = vx(R, A, q, N), c.size > 0 && (n || F || !N && W)) {
        const [Z, ne] = io({
          nodeId: N,
          dragItems: c,
          nodeLookup: R
        });
        n?.(k.sourceEvent, c, Z, ne), F?.(k.sourceEvent, Z, ne), N || W?.(k.sourceEvent, ne);
      }
    }
    const D = $l().clickDistance(S).on("start", (k) => {
      const { domNode: R, nodeDragThreshold: C, transform: A, snapGrid: E, snapToGrid: T } = t();
      d = R?.getBoundingClientRect() || null, h = !1, y = !1, x = k.sourceEvent, C === 0 && M(k), s = kn(k.sourceEvent, { transform: A, snapGrid: E, snapToGrid: T, containerBounds: d }), l = qe(k.sourceEvent, d);
    }).on("drag", (k) => {
      const { autoPanOnNodeDrag: R, transform: C, snapGrid: A, snapToGrid: E, nodeDragThreshold: T, nodeLookup: $ } = t(), P = kn(k.sourceEvent, { transform: C, snapGrid: A, snapToGrid: E, containerBounds: d });
      if (x = k.sourceEvent, (k.sourceEvent.type === "touchmove" && k.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      N && !$.has(N)) && (h = !0), !h) {
        if (!u && R && f && (u = !0, _()), !f) {
          const F = qe(k.sourceEvent, d), W = F.x - l.x, H = F.y - l.y;
          Math.sqrt(W * W + H * H) > T && M(k);
        }
        (s.x !== P.xSnapped || s.y !== P.ySnapped) && c && f && (l = qe(k.sourceEvent, d), I(P));
      }
    }).on("end", (k) => {
      if (!f || h) {
        h && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (u = !1, f = !1, cancelAnimationFrame(a), c.size > 0) {
        const { nodeLookup: R, updateNodePositions: C, onNodeDragStop: A, onSelectionDragStop: E } = t();
        if (y && (C(c, !1), y = !1), r || A || !N && E) {
          const [T, $] = io({
            nodeId: N,
            dragItems: c,
            nodeLookup: R,
            dragging: !1
          });
          r?.(k.sourceEvent, c, T, $), A?.(k.sourceEvent, T, $), N || E?.(k.sourceEvent, $);
        }
      }
    }).filter((k) => {
      const R = k.target;
      return !k.button && (!b || !ka(R, `.${b}`, v)) && (!g || ka(R, g, v));
    });
    p.call(D);
  }
  function w() {
    p?.on(".drag", null);
  }
  return {
    update: m,
    destroy: w
  };
}
function jx(e, t, n) {
  const i = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const s of t.values())
    zn(r, nn(s)) > 0 && i.push(s);
  return i;
}
const Sx = 250;
function Cx(e, t, n, i) {
  let r = [], s = 1 / 0;
  const a = jx(e, n, t + Sx);
  for (const c of a) {
    const u = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of u) {
      if (i.nodeId === l.nodeId && i.type === l.type && i.id === l.id)
        continue;
      const { x: d, y: f } = At(c, l, l.position, !0), p = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(f - e.y, 2));
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
function xu(e, t, n, i, r, s = !1) {
  const a = i.get(e);
  if (!a)
    return null;
  const c = r === "strict" ? a.internals.handleBounds?.[t] : [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []], u = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return u && s ? { ...u, ...At(a, u, u.position, !0) } : u;
}
function wu(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Ex(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const vu = () => !0;
function kx(e, { connectionMode: t, connectionRadius: n, handleId: i, nodeId: r, edgeUpdaterType: s, isTarget: a, domNode: c, nodeLookup: u, lib: l, autoPanOnConnect: d, flowId: f, panBy: p, cancelConnection: h, onConnectStart: y, onConnect: x, onConnectEnd: m, isValidConnection: w = vu, onReconnectEnd: b, updateConnection: g, getTransform: v, getFromHandle: j, autoPanSpeed: N, dragThreshold: S = 1, handleDomNode: I }) {
  const _ = su(e.target);
  let M = 0, D;
  const { x: k, y: R } = qe(e), C = wu(s, I), A = c?.getBoundingClientRect();
  let E = !1;
  if (!A || !C)
    return;
  const T = xu(r, C, i, u, t);
  if (!T)
    return;
  let $ = qe(e, A), P = !1, F = null, W = !1, H = null;
  function q() {
    if (!d || !A)
      return;
    const [ce, Q] = os($, A, N);
    p({ x: ce, y: Q }), M = requestAnimationFrame(q);
  }
  const Z = {
    ...T,
    nodeId: r,
    type: C,
    position: T.position
  }, ne = u.get(r);
  let G = {
    inProgress: !0,
    isValid: null,
    from: At(ne, Z, ie.Left, !0),
    fromHandle: Z,
    fromPosition: Z.position,
    fromNode: ne,
    to: $,
    toHandle: null,
    toPosition: ga[Z.position],
    toNode: null,
    pointer: $
  };
  function z() {
    E = !0, g(G), y?.(e, { nodeId: r, handleId: i, handleType: C });
  }
  S === 0 && z();
  function X(ce) {
    if (!E) {
      const { x: he, y: me } = qe(ce), Te = he - k, ke = me - R;
      if (!(Te * Te + ke * ke > S * S))
        return;
      z();
    }
    if (!j() || !Z) {
      ae(ce);
      return;
    }
    const Q = v();
    $ = qe(ce, A), D = Cx(ln($, Q, !1, [1, 1]), n, u, Z), P || (q(), P = !0);
    const te = bu(ce, {
      handle: D,
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
    H = te.handleDomNode, F = te.connection, W = Ex(!!D, te.isValid);
    const fe = u.get(r), O = fe ? At(fe, Z, ie.Left, !0) : G.from, ee = {
      ...G,
      from: O,
      isValid: W,
      to: te.toHandle && W ? rn({ x: te.toHandle.x, y: te.toHandle.y }, Q) : $,
      toHandle: te.toHandle,
      toPosition: W && te.toHandle ? te.toHandle.position : ga[Z.position],
      toNode: te.toHandle ? u.get(te.toHandle.nodeId) : null,
      pointer: $
    };
    g(ee), G = ee;
  }
  function ae(ce) {
    if (!("touches" in ce && ce.touches.length > 0)) {
      if (E) {
        (D || H) && F && W && x?.(F);
        const { inProgress: Q, ...te } = G, fe = {
          ...te,
          toPosition: G.toHandle ? G.toPosition : null
        };
        m?.(ce, fe), s && b?.(ce, fe);
      }
      h(), cancelAnimationFrame(M), P = !1, W = !1, F = null, H = null, _.removeEventListener("mousemove", X), _.removeEventListener("mouseup", ae), _.removeEventListener("touchmove", X), _.removeEventListener("touchend", ae);
    }
  }
  _.addEventListener("mousemove", X), _.addEventListener("mouseup", ae), _.addEventListener("touchmove", X), _.addEventListener("touchend", ae);
}
function bu(e, { handle: t, connectionMode: n, fromNodeId: i, fromHandleId: r, fromType: s, doc: a, lib: c, flowId: u, isValidConnection: l = vu, nodeLookup: d }) {
  const f = s === "target", p = t ? a.querySelector(`.${c}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: h, y } = qe(e), x = a.elementFromPoint(h, y), m = x?.classList.contains(`${c}-flow__handle`) ? x : p, w = {
    handleDomNode: m,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (m) {
    const b = wu(void 0, m), g = m.getAttribute("data-nodeid"), v = m.getAttribute("data-handleid"), j = m.classList.contains("connectable"), N = m.classList.contains("connectableend");
    if (!g || !b)
      return w;
    const S = {
      source: f ? g : i,
      sourceHandle: f ? v : r,
      target: f ? i : g,
      targetHandle: f ? r : v
    };
    w.connection = S;
    const _ = j && N && (n === en.Strict ? f && b === "source" || !f && b === "target" : g !== i || v !== r);
    w.isValid = _ && l(S), w.toHandle = xu(g, b, v, d, n, !0);
  }
  return w;
}
const To = {
  onPointerDown: kx,
  isValid: bu
};
function Ix({ domNode: e, panZoom: t, getTransform: n, getViewScale: i }) {
  const r = ze(e);
  function s({ translateExtent: c, width: u, height: l, zoomStep: d = 1, pannable: f = !0, zoomable: p = !0, inversePan: h = !1 }) {
    const y = (g) => {
      if (g.sourceEvent.type !== "wheel" || !t)
        return;
      const v = n(), j = g.sourceEvent.ctrlKey && Ln() ? 10 : 1, N = -g.sourceEvent.deltaY * (g.sourceEvent.deltaMode === 1 ? 0.05 : g.sourceEvent.deltaMode ? 1 : 2e-3) * d, S = v[2] * Math.pow(2, N * j);
      t.scaleTo(S);
    };
    let x = [0, 0];
    const m = (g) => {
      (g.sourceEvent.type === "mousedown" || g.sourceEvent.type === "touchstart") && (x = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ]);
    }, w = (g) => {
      const v = n();
      if (g.sourceEvent.type !== "mousemove" && g.sourceEvent.type !== "touchmove" || !t)
        return;
      const j = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ], N = [j[0] - x[0], j[1] - x[1]];
      x = j;
      const S = i() * Math.max(v[2], Math.log(v[2])) * (h ? -1 : 1), I = {
        x: v[0] - N[0] * S,
        y: v[1] - N[1] * S
      }, _ = [
        [0, 0],
        [u, l]
      ];
      t.setViewportConstrained({
        x: I.x,
        y: I.y,
        zoom: v[2]
      }, _, c);
    }, b = ql().on("start", m).on("zoom", f ? w : null).on("zoom.wheel", p ? y : null);
    r.call(b, {});
  }
  function a() {
    r.on("zoom", null);
  }
  return {
    update: s,
    destroy: a,
    pointer: Ke
  };
}
const cr = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), ro = ({ x: e, y: t, zoom: n }) => or.translate(e, t).scale(n), Wt = (e, t) => e.target.closest(`.${t}`), Nu = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Ax = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, oo = (e, t = 0, n = Ax, i = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || i(), r ? e.transition().duration(t).ease(n).on("end", i) : e;
}, ju = (e) => {
  const t = e.ctrlKey && Ln() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function _x({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: i, panOnScrollMode: r, panOnScrollSpeed: s, zoomOnPinch: a, onPanZoomStart: c, onPanZoom: u, onPanZoomEnd: l }) {
  return (d) => {
    if (Wt(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && a) {
      const m = Ke(d), w = ju(d), b = f * Math.pow(2, w);
      i.scaleTo(n, b, m, d);
      return;
    }
    const p = d.deltaMode === 1 ? 20 : 1;
    let h = r === St.Vertical ? 0 : d.deltaX * p, y = r === St.Horizontal ? 0 : d.deltaY * p;
    !Ln() && d.shiftKey && r !== St.Vertical && (h = d.deltaY * p, y = 0), i.translateBy(
      n,
      -(h / f) * s,
      -(y / f) * s,
      // @ts-ignore
      { internal: !0 }
    );
    const x = cr(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(d, x), e.panScrollTimeout = setTimeout(() => {
      l?.(d, x), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(d, x));
  };
}
function Dx({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(i, r) {
    const s = i.type === "wheel", a = !t && s && !i.ctrlKey, c = Wt(i, e);
    if (i.ctrlKey && s && c && i.preventDefault(), a || c)
      return null;
    i.preventDefault(), n.call(this, i, r);
  };
}
function Tx({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (i) => {
    if (i.sourceEvent?.internal)
      return;
    const r = cr(i.transform);
    e.mouseButton = i.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, i.sourceEvent?.type === "mousedown" && t(!0), n && n?.(i.sourceEvent, r);
  };
}
function $x({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: i, onPanZoom: r }) {
  return (s) => {
    e.usedRightMouseButton = !!(n && Nu(t, e.mouseButton ?? 0)), s.sourceEvent?.sync || i([s.transform.x, s.transform.y, s.transform.k]), r && !s.sourceEvent?.internal && r?.(s.sourceEvent, cr(s.transform));
  };
}
function Px({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: i, onPanZoomEnd: r, onPaneContextMenu: s }) {
  return (a) => {
    if (!a.sourceEvent?.internal && (e.isZoomingOrPanning = !1, s && Nu(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && a.sourceEvent && s(a.sourceEvent), e.usedRightMouseButton = !1, i(!1), r)) {
      const c = cr(a.transform);
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
function Mx({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: i, panOnScroll: r, zoomOnDoubleClick: s, userSelectionActive: a, noWheelClassName: c, noPanClassName: u, lib: l, connectionInProgress: d }) {
  return (f) => {
    const p = e || t, h = n && f.ctrlKey, y = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (Wt(f, `${l}-flow__node`) || Wt(f, `${l}-flow__edge`)))
      return !0;
    if (!i && !p && !r && !s && !n || a || d && !y || Wt(f, c) && y || Wt(f, u) && (!y || r && y && !e) || !n && f.ctrlKey && y)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!p && !r && !h && y || !i && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(i) && !i.includes(f.button) && f.type === "mousedown")
      return !1;
    const x = Array.isArray(i) && i.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || y) && x;
  };
}
function Rx({ domNode: e, minZoom: t, maxZoom: n, translateExtent: i, viewport: r, onPanZoom: s, onPanZoomStart: a, onPanZoomEnd: c, onDraggingChange: u }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = ql().scaleExtent([t, n]).translateExtent(i), p = ze(e).call(f);
  b({
    x: r.x,
    y: r.y,
    zoom: tn(r.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], i);
  const h = p.on("wheel.zoom"), y = p.on("dblclick.zoom");
  f.wheelDelta(ju);
  async function x(D, k) {
    return p ? new Promise((R) => {
      f?.interpolate(k?.interpolate === "linear" ? En : Ci).transform(oo(p, k?.duration, k?.ease, () => R(!0)), D);
    }) : !1;
  }
  function m({ noWheelClassName: D, noPanClassName: k, onPaneContextMenu: R, userSelectionActive: C, panOnScroll: A, panOnDrag: E, panOnScrollMode: T, panOnScrollSpeed: $, preventScrolling: P, zoomOnPinch: F, zoomOnScroll: W, zoomOnDoubleClick: H, zoomActivationKeyPressed: q, lib: Z, onTransformChange: ne, connectionInProgress: le, paneClickDistance: G, selectionOnDrag: z }) {
    C && !l.isZoomingOrPanning && w();
    const X = A && !q && !C;
    f.clickDistance(z ? 1 / 0 : !Ye(G) || G < 0 ? 0 : G);
    const ae = X ? _x({
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
    }) : Dx({
      noWheelClassName: D,
      preventScrolling: P,
      d3ZoomHandler: h
    });
    p.on("wheel.zoom", ae, { passive: !1 });
    const ce = Tx({
      zoomPanValues: l,
      onDraggingChange: u,
      onPanZoomStart: a
    });
    f.on("start", ce);
    const Q = $x({
      zoomPanValues: l,
      panOnDrag: E,
      onPaneContextMenu: !!R,
      onPanZoom: s,
      onTransformChange: ne
    });
    f.on("zoom", Q);
    const te = Px({
      zoomPanValues: l,
      panOnDrag: E,
      panOnScroll: A,
      onPaneContextMenu: R,
      onPanZoomEnd: c,
      onDraggingChange: u
    });
    f.on("end", te);
    const fe = Mx({
      zoomActivationKeyPressed: q,
      panOnDrag: E,
      zoomOnScroll: W,
      panOnScroll: A,
      zoomOnDoubleClick: H,
      zoomOnPinch: F,
      userSelectionActive: C,
      noPanClassName: k,
      noWheelClassName: D,
      lib: Z,
      connectionInProgress: le
    });
    f.filter(fe), H ? p.on("dblclick.zoom", y) : p.on("dblclick.zoom", null);
  }
  function w() {
    f.on("zoom", null);
  }
  async function b(D, k, R) {
    const C = ro(D), A = f?.constrain()(C, k, R);
    return A && await x(A), A;
  }
  async function g(D, k) {
    const R = ro(D);
    return await x(R, k), R;
  }
  function v(D) {
    if (p) {
      const k = ro(D), R = p.property("__zoom");
      (R.k !== D.zoom || R.x !== D.x || R.y !== D.y) && f?.transform(p, k, null, { sync: !0 });
    }
  }
  function j() {
    const D = p ? Yl(p.node()) : { x: 0, y: 0, k: 1 };
    return { x: D.x, y: D.y, zoom: D.k };
  }
  async function N(D, k) {
    return p ? new Promise((R) => {
      f?.interpolate(k?.interpolate === "linear" ? En : Ci).scaleTo(oo(p, k?.duration, k?.ease, () => R(!0)), D);
    }) : !1;
  }
  async function S(D, k) {
    return p ? new Promise((R) => {
      f?.interpolate(k?.interpolate === "linear" ? En : Ci).scaleBy(oo(p, k?.duration, k?.ease, () => R(!0)), D);
    }) : !1;
  }
  function I(D) {
    f?.scaleExtent(D);
  }
  function _(D) {
    f?.translateExtent(D);
  }
  function M(D) {
    const k = !Ye(D) || D < 0 ? 0 : D;
    f?.clickDistance(k);
  }
  return {
    update: m,
    destroy: w,
    setViewport: g,
    setViewportConstrained: b,
    getViewport: j,
    scaleTo: N,
    scaleBy: S,
    setScaleExtent: I,
    setTranslateExtent: _,
    syncViewport: v,
    setClickDistance: M
  };
}
var on;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(on || (on = {}));
function zx({ width: e, prevWidth: t, height: n, prevHeight: i, affectsX: r, affectsY: s }) {
  const a = e - t, c = n - i, u = [a > 0 ? 1 : a < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return a && r && (u[0] = u[0] * -1), c && s && (u[1] = u[1] * -1), u;
}
function Ia(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), i = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: i,
    affectsY: r
  };
}
function dt(e, t) {
  return Math.max(0, t - e);
}
function ft(e, t) {
  return Math.max(0, e - t);
}
function yi(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function Aa(e, t) {
  return e ? !t : t;
}
function Lx(e, t, n, i, r, s, a, c) {
  let { affectsX: u, affectsY: l } = t;
  const { isHorizontal: d, isVertical: f } = t, p = d && f, { xSnapped: h, ySnapped: y } = n, { minWidth: x, maxWidth: m, minHeight: w, maxHeight: b } = i, { x: g, y: v, width: j, height: N, aspectRatio: S } = e;
  let I = Math.floor(d ? h - e.pointerX : 0), _ = Math.floor(f ? y - e.pointerY : 0);
  const M = j + (u ? -I : I), D = N + (l ? -_ : _), k = -s[0] * j, R = -s[1] * N;
  let C = yi(M, x, m), A = yi(D, w, b);
  if (a) {
    let $ = 0, P = 0;
    u && I < 0 ? $ = dt(g + I + k, a[0][0]) : !u && I > 0 && ($ = ft(g + M + k, a[1][0])), l && _ < 0 ? P = dt(v + _ + R, a[0][1]) : !l && _ > 0 && (P = ft(v + D + R, a[1][1])), C = Math.max(C, $), A = Math.max(A, P);
  }
  if (c) {
    let $ = 0, P = 0;
    u && I > 0 ? $ = ft(g + I, c[0][0]) : !u && I < 0 && ($ = dt(g + M, c[1][0])), l && _ > 0 ? P = ft(v + _, c[0][1]) : !l && _ < 0 && (P = dt(v + D, c[1][1])), C = Math.max(C, $), A = Math.max(A, P);
  }
  if (r) {
    if (d) {
      const $ = yi(M / S, w, b) * S;
      if (C = Math.max(C, $), a) {
        let P = 0;
        !u && !l || u && !l && p ? P = ft(v + R + M / S, a[1][1]) * S : P = dt(v + R + (u ? I : -I) / S, a[0][1]) * S, C = Math.max(C, P);
      }
      if (c) {
        let P = 0;
        !u && !l || u && !l && p ? P = dt(v + M / S, c[1][1]) * S : P = ft(v + (u ? I : -I) / S, c[0][1]) * S, C = Math.max(C, P);
      }
    }
    if (f) {
      const $ = yi(D * S, x, m) / S;
      if (A = Math.max(A, $), a) {
        let P = 0;
        !u && !l || l && !u && p ? P = ft(g + D * S + k, a[1][0]) / S : P = dt(g + (l ? _ : -_) * S + k, a[0][0]) / S, A = Math.max(A, P);
      }
      if (c) {
        let P = 0;
        !u && !l || l && !u && p ? P = dt(g + D * S, c[1][0]) / S : P = ft(g + (l ? _ : -_) * S, c[0][0]) / S, A = Math.max(A, P);
      }
    }
  }
  _ = _ + (_ < 0 ? A : -A), I = I + (I < 0 ? C : -C), r && (p ? M > D * S ? _ = (Aa(u, l) ? -I : I) / S : I = (Aa(u, l) ? -_ : _) * S : d ? (_ = I / S, l = u) : (I = _ * S, u = l));
  const E = u ? g + I : g, T = l ? v + _ : v;
  return {
    width: j + (u ? -I : I),
    height: N + (l ? -_ : _),
    x: s[0] * I * (u ? -1 : 1) + E,
    y: s[1] * _ * (l ? -1 : 1) + T
  };
}
const Su = { width: 0, height: 0, x: 0, y: 0 }, Vx = {
  ...Su,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function Ox(e, t, n) {
  const i = t.position.x + e.position.x, r = t.position.y + e.position.y, s = e.measured.width ?? 0, a = e.measured.height ?? 0, c = n[0] * s, u = n[1] * a;
  return [
    [i - c, r - u],
    [i + s - c, r + a - u]
  ];
}
function Hx({ domNode: e, nodeId: t, getStoreItems: n, onChange: i, onEnd: r }) {
  const s = ze(e);
  let a = {
    controlDirection: Ia("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: l, boundaries: d, keepAspectRatio: f, resizeDirection: p, onResizeStart: h, onResize: y, onResizeEnd: x, shouldResize: m }) {
    let w = { ...Su }, b = { ...Vx };
    a = {
      boundaries: d,
      resizeDirection: p,
      keepAspectRatio: f,
      controlDirection: Ia(l)
    };
    let g, v = null, j = [], N, S, I, _ = !1;
    const M = $l().on("start", (D) => {
      const { nodeLookup: k, transform: R, snapGrid: C, snapToGrid: A, nodeOrigin: E, paneDomNode: T } = n();
      if (g = k.get(t), !g)
        return;
      v = T?.getBoundingClientRect() ?? null;
      const { xSnapped: $, ySnapped: P } = kn(D.sourceEvent, {
        transform: R,
        snapGrid: C,
        snapToGrid: A,
        containerBounds: v
      });
      w = {
        width: g.measured.width ?? 0,
        height: g.measured.height ?? 0,
        x: g.position.x ?? 0,
        y: g.position.y ?? 0
      }, b = {
        ...w,
        pointerX: $,
        pointerY: P,
        aspectRatio: w.width / w.height
      }, N = void 0, S = It(g.extent) ? g.extent : void 0, g.parentId && (g.extent === "parent" || g.expandParent) && (N = k.get(g.parentId)), N && g.extent === "parent" && (S = [
        [0, 0],
        [N.measured.width, N.measured.height]
      ]), j = [], I = void 0;
      for (const [F, W] of k)
        if (W.parentId === t && (j.push({
          id: F,
          position: { ...W.position },
          extent: W.extent
        }), W.extent === "parent" || W.expandParent)) {
          const H = Ox(W, g, W.origin ?? E);
          I ? I = [
            [Math.min(H[0][0], I[0][0]), Math.min(H[0][1], I[0][1])],
            [Math.max(H[1][0], I[1][0]), Math.max(H[1][1], I[1][1])]
          ] : I = H;
        }
      h?.(D, { ...w });
    }).on("drag", (D) => {
      const { transform: k, snapGrid: R, snapToGrid: C, nodeOrigin: A } = n(), E = kn(D.sourceEvent, {
        transform: k,
        snapGrid: R,
        snapToGrid: C,
        containerBounds: v
      }), T = [];
      if (!g)
        return;
      const { x: $, y: P, width: F, height: W } = w, H = {}, q = g.origin ?? A, { width: Z, height: ne, x: le, y: G } = Lx(b, a.controlDirection, E, a.boundaries, a.keepAspectRatio, q, S, I), z = Z !== F, X = ne !== W, ae = le !== $ && z, ce = G !== P && X;
      if (!ae && !ce && !z && !X)
        return;
      if ((ae || ce || q[0] === 1 || q[1] === 1) && (H.x = ae ? le : w.x, H.y = ce ? G : w.y, w.x = H.x, w.y = H.y, j.length > 0)) {
        const O = le - $, ee = G - P;
        for (const he of j)
          he.position = {
            x: he.position.x - O + q[0] * (Z - F),
            y: he.position.y - ee + q[1] * (ne - W)
          }, T.push(he);
      }
      if ((z || X) && (H.width = z && (!a.resizeDirection || a.resizeDirection === "horizontal") ? Z : w.width, H.height = X && (!a.resizeDirection || a.resizeDirection === "vertical") ? ne : w.height, w.width = H.width, w.height = H.height), N && g.expandParent) {
        const O = q[0] * (H.width ?? 0);
        H.x && H.x < O && (w.x = O, b.x = b.x - (H.x - O));
        const ee = q[1] * (H.height ?? 0);
        H.y && H.y < ee && (w.y = ee, b.y = b.y - (H.y - ee));
      }
      const Q = zx({
        width: w.width,
        prevWidth: F,
        height: w.height,
        prevHeight: W,
        affectsX: a.controlDirection.affectsX,
        affectsY: a.controlDirection.affectsY
      }), te = { ...w, direction: Q };
      m?.(D, te) !== !1 && (_ = !0, y?.(D, te), i(H, T));
    }).on("end", (D) => {
      _ && (x?.(D, { ...w }), r?.({ ...w }), _ = !1);
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
var so = { exports: {} }, ao = {}, co = { exports: {} }, lo = {};
var _a;
function Wx() {
  if (_a) return lo;
  _a = 1;
  var e = Qe;
  function t(f, p) {
    return f === p && (f !== 0 || 1 / f === 1 / p) || f !== f && p !== p;
  }
  var n = typeof Object.is == "function" ? Object.is : t, i = e.useState, r = e.useEffect, s = e.useLayoutEffect, a = e.useDebugValue;
  function c(f, p) {
    var h = p(), y = i({ inst: { value: h, getSnapshot: p } }), x = y[0].inst, m = y[1];
    return s(
      function() {
        x.value = h, x.getSnapshot = p, u(x) && m({ inst: x });
      },
      [f, h, p]
    ), r(
      function() {
        return u(x) && m({ inst: x }), f(function() {
          u(x) && m({ inst: x });
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
  return lo.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : d, lo;
}
var Da;
function Fx() {
  return Da || (Da = 1, co.exports = Wx()), co.exports;
}
var Ta;
function Bx() {
  if (Ta) return ao;
  Ta = 1;
  var e = Qe, t = Fx();
  function n(l, d) {
    return l === d && (l !== 0 || 1 / l === 1 / d) || l !== l && d !== d;
  }
  var i = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, s = e.useRef, a = e.useEffect, c = e.useMemo, u = e.useDebugValue;
  return ao.useSyncExternalStoreWithSelector = function(l, d, f, p, h) {
    var y = s(null);
    if (y.current === null) {
      var x = { hasValue: !1, value: null };
      y.current = x;
    } else x = y.current;
    y = c(
      function() {
        function w(N) {
          if (!b) {
            if (b = !0, g = N, N = p(N), h !== void 0 && x.hasValue) {
              var S = x.value;
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
            return w(d());
          },
          j === null ? void 0 : function() {
            return w(j());
          }
        ];
      },
      [d, f, p, h]
    );
    var m = r(l, y[0], y[1]);
    return a(
      function() {
        x.hasValue = !0, x.value = m;
      },
      [m]
    ), u(m), m;
  }, ao;
}
var $a;
function Kx() {
  return $a || ($a = 1, so.exports = Bx()), so.exports;
}
var Xx = Kx();
const Yx = /* @__PURE__ */ ff(Xx), qx = {}, Pa = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), i = (d, f) => {
    const p = typeof d == "function" ? d(t) : d;
    if (!Object.is(p, t)) {
      const h = t;
      t = f ?? (typeof p != "object" || p === null) ? p : Object.assign({}, t, p), n.forEach((y) => y(t, h));
    }
  }, r = () => t, u = { setState: i, getState: r, getInitialState: () => l, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (qx ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(i, r, u);
  return u;
}, Ux = (e) => e ? Pa(e) : Pa, { useDebugValue: Zx } = Qe, { useSyncExternalStoreWithSelector: Gx } = Yx, Jx = (e) => e;
function Cu(e, t = Jx, n) {
  const i = Gx(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return Zx(i), i;
}
const Ma = (e, t) => {
  const n = Ux(e), i = (r, s = t) => Cu(n, r, s);
  return Object.assign(i, n), i;
}, Qx = (e, t) => e ? Ma(e, t) : Ma;
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
var uo = { exports: {} }, Ae = {};
var Ra;
function ew() {
  if (Ra) return Ae;
  Ra = 1;
  var e = Qe;
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
  return Ae.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = i, Ae.createPortal = function(u, l) {
    var d = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!l || l.nodeType !== 1 && l.nodeType !== 9 && l.nodeType !== 11)
      throw Error(t(299));
    return s(u, l, null, d);
  }, Ae.flushSync = function(u) {
    var l = a.T, d = i.p;
    try {
      if (a.T = null, i.p = 2, u) return u();
    } finally {
      a.T = l, i.p = d, i.d.f();
    }
  }, Ae.preconnect = function(u, l) {
    typeof u == "string" && (l ? (l = l.crossOrigin, l = typeof l == "string" ? l === "use-credentials" ? l : "" : void 0) : l = null, i.d.C(u, l));
  }, Ae.prefetchDNS = function(u) {
    typeof u == "string" && i.d.D(u);
  }, Ae.preinit = function(u, l) {
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
  }, Ae.preinitModule = function(u, l) {
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
  }, Ae.preload = function(u, l) {
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
  }, Ae.preloadModule = function(u, l) {
    if (typeof u == "string")
      if (l) {
        var d = c(l.as, l.crossOrigin);
        i.d.m(u, {
          as: typeof l.as == "string" && l.as !== "script" ? l.as : void 0,
          crossOrigin: d,
          integrity: typeof l.integrity == "string" ? l.integrity : void 0
        });
      } else i.d.m(u);
  }, Ae.requestFormReset = function(u) {
    i.d.r(u);
  }, Ae.unstable_batchedUpdates = function(u, l) {
    return u(l);
  }, Ae.useFormState = function(u, l, d) {
    return a.H.useFormState(u, l, d);
  }, Ae.useFormStatus = function() {
    return a.H.useHostTransitionStatus();
  }, Ae.version = "19.2.7", Ae;
}
var za;
function tw() {
  if (za) return uo.exports;
  za = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), uo.exports = ew(), uo.exports;
}
var nw = tw();
const lr = Ho(null), iw = lr.Provider, Eu = He.error001("react");
function pe(e, t) {
  const n = On(lr);
  if (n === null)
    throw new Error(Eu);
  return Cu(n, e, t);
}
function ve() {
  const e = On(lr);
  if (e === null)
    throw new Error(Eu);
  return de(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const La = { display: "none" }, rw = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, ku = "react-flow__node-desc", Iu = "react-flow__edge-desc", ow = "react-flow__aria-live", sw = (e) => e.ariaLiveMessage, aw = (e) => e.ariaLabelConfig;
function cw({ rfId: e }) {
  const t = pe(sw);
  return o.jsx("div", { id: `${ow}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: rw, children: t });
}
function lw({ rfId: e, disableKeyboardA11y: t }) {
  const n = pe(aw);
  return o.jsxs(o.Fragment, { children: [o.jsx("div", { id: `${ku}-${e}`, style: La, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), o.jsx("div", { id: `${Iu}-${e}`, style: La, children: n["edge.a11yDescription.default"] }), !t && o.jsx(cw, { rfId: e })] });
}
const ur = Rc(({ position: e = "top-left", children: t, className: n, style: i, ...r }, s) => {
  const a = `${e}`.split("-");
  return o.jsx("div", { className: Ce(["react-flow__panel", n, ...a]), style: i, ref: s, ...r, children: t });
});
ur.displayName = "Panel";
function uw({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : o.jsx(ur, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: o.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const dw = (e) => {
  const t = [], n = [];
  for (const [, i] of e.nodeLookup)
    i.selected && t.push(i.internals.userNode);
  for (const [, i] of e.edgeLookup)
    i.selected && n.push(i);
  return { selectedNodes: t, selectedEdges: n };
}, mi = (e) => e.id;
function fw(e, t) {
  return we(e.selectedNodes.map(mi), t.selectedNodes.map(mi)) && we(e.selectedEdges.map(mi), t.selectedEdges.map(mi));
}
function pw({ onSelectionChange: e }) {
  const t = ve(), { selectedNodes: n, selectedEdges: i } = pe(dw, fw);
  return J(() => {
    const r = { nodes: n, edges: i };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((s) => s(r));
  }, [n, i, e]), null;
}
const hw = (e) => !!e.onSelectionChangeHandlers;
function gw({ onSelectionChange: e }) {
  const t = pe(hw);
  return e || t ? o.jsx(pw, { onSelectionChange: e }) : null;
}
const Au = [0, 0], yw = { x: 0, y: 0, zoom: 1 }, mw = [
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
], Va = [...mw, "rfId"], xw = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), Oa = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: Mn,
  nodeOrigin: Au,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function ww(e) {
  const { setNodes: t, setEdges: n, setMinZoom: i, setMaxZoom: r, setTranslateExtent: s, setNodeExtent: a, reset: c, setDefaultNodesAndEdges: u } = pe(xw, we), l = ve();
  J(() => (u(e.defaultNodes, e.defaultEdges), () => {
    d.current = Oa, c();
  }), []);
  const d = oe(Oa);
  return J(
    () => {
      for (const f of Va) {
        const p = e[f], h = d.current[f];
        p !== h && (typeof e[f] > "u" || (f === "nodes" ? t(p) : f === "edges" ? n(p) : f === "minZoom" ? i(p) : f === "maxZoom" ? r(p) : f === "translateExtent" ? s(p) : f === "nodeExtent" ? a(p) : f === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: ex(p) }) : f === "fitView" ? l.setState({ fitViewQueued: p }) : f === "fitViewOptions" ? l.setState({ fitViewOptions: p }) : l.setState({ [f]: p })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    Va.map((f) => e[f])
  ), null;
}
function Ha() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function vw(e) {
  const [t, n] = B(e === "system" ? null : e);
  return J(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const i = Ha(), r = () => n(i?.matches ? "dark" : "light");
    return r(), i?.addEventListener("change", r), () => {
      i?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : Ha()?.matches ? "dark" : "light";
}
const Wa = typeof document < "u" ? document : null;
function Vn(e = null, t = { target: Wa, actInsideInputWithModifier: !0 }) {
  const [n, i] = B(!1), r = oe(!1), s = oe(/* @__PURE__ */ new Set([])), [a, c] = de(() => {
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
    const u = t?.target ?? Wa, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (h) => {
        if (r.current = h.ctrlKey || h.metaKey || h.shiftKey || h.altKey, (!r.current || r.current && !l) && au(h))
          return !1;
        const x = Ba(h.code, c);
        if (s.current.add(h[x]), Fa(a, s.current, !1)) {
          const m = h.composedPath?.()?.[0] || h.target, w = m?.nodeName === "BUTTON" || m?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !w) && h.preventDefault(), i(!0);
        }
      }, f = (h) => {
        const y = Ba(h.code, c);
        Fa(a, s.current, !0) ? (i(!1), s.current.clear()) : s.current.delete(h[y]), h.key === "Meta" && s.current.clear(), r.current = !1;
      }, p = () => {
        s.current.clear(), i(!1);
      };
      return u?.addEventListener("keydown", d), u?.addEventListener("keyup", f), window.addEventListener("blur", p), window.addEventListener("contextmenu", p), () => {
        u?.removeEventListener("keydown", d), u?.removeEventListener("keyup", f), window.removeEventListener("blur", p), window.removeEventListener("contextmenu", p);
      };
    }
  }, [e, i]), n;
}
function Fa(e, t, n) {
  return e.filter((i) => n || i.length === t.size).some((i) => i.every((r) => t.has(r)));
}
function Ba(e, t) {
  return t.includes(e) ? "code" : "key";
}
const bw = () => {
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
      const { width: i, height: r, minZoom: s, maxZoom: a, panZoom: c } = e.getState(), u = ss(t, i, r, s, a, n?.padding ?? 0.1);
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
      return ln(l, i, f, d);
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
function _u(e, t) {
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
      Nw(u, c);
    n.push(c);
  }
  return r.length && r.forEach((s) => {
    s.index !== void 0 ? n.splice(s.index, 0, { ...s.item }) : n.push({ ...s.item });
  }), n;
}
function Nw(e, t) {
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
function Du(e, t) {
  return _u(e, t);
}
function Tu(e, t) {
  return _u(e, t);
}
function wt(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function Ft(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const i = [];
  for (const [r, s] of e) {
    const a = t.has(r);
    !(s.selected === void 0 && !a) && s.selected !== a && (n && (s.selected = a), i.push(wt(s.id, a)));
  }
  return i;
}
function Ka({ items: e = [], lookup: t }) {
  const n = [], i = new Map(e.map((r) => [r.id, r]));
  for (const [r, s] of e.entries()) {
    const a = t.get(s.id), c = a?.internals?.userNode ?? a;
    c !== void 0 && c !== s && n.push({ id: s.id, item: s, type: "replace" }), c === void 0 && n.push({ item: s, type: "add", index: r });
  }
  for (const [r] of t)
    i.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function Xa(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const $u = iu();
function Pu(e, t, n = {}) {
  return ox(e, t, {
    ...n,
    onError: n.onError ?? $u
  });
}
function jw(e, t, n, i = { shouldReplaceId: !0 }) {
  return sx(e, t, n, {
    ...i,
    onError: i.onError ?? $u
  });
}
const Ya = (e) => Km(e), Sw = (e) => Ql(e);
function Mu(e) {
  return Rc(e);
}
const Cw = typeof window < "u" ? Yd : J;
function qa(e) {
  const [t, n] = B(BigInt(0)), [i] = B(() => Ew(() => n((r) => r + BigInt(1))));
  return Cw(() => {
    const r = i.get();
    r.length && (e(r), i.reset());
  }, [t]), i;
}
function Ew(e) {
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
const Ru = Ho(null);
function kw({ children: e }) {
  const t = ve(), n = re((c) => {
    const { nodes: u = [], setNodes: l, hasDefaultNodes: d, onNodesChange: f, nodeLookup: p, fitViewQueued: h, onNodesChangeMiddlewareMap: y } = t.getState();
    let x = u;
    for (const w of c)
      x = typeof w == "function" ? w(x) : w;
    let m = Ka({
      items: x,
      lookup: p
    });
    for (const w of y.values())
      m = w(m);
    d && l(x), m.length > 0 ? f?.(m) : h && window.requestAnimationFrame(() => {
      const { fitViewQueued: w, nodes: b, setNodes: g } = t.getState();
      w && g(b);
    });
  }, []), i = qa(n), r = re((c) => {
    const { edges: u = [], setEdges: l, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: p } = t.getState();
    let h = u;
    for (const y of c)
      h = typeof y == "function" ? y(h) : y;
    d ? l(h) : f && f(Ka({
      items: h,
      lookup: p
    }));
  }, []), s = qa(r), a = de(() => ({ nodeQueue: i, edgeQueue: s }), []);
  return o.jsx(Ru.Provider, { value: a, children: e });
}
function Iw() {
  const e = On(Ru);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Aw = (e) => !!e.panZoom;
function ps() {
  const e = bw(), t = ve(), n = Iw(), i = pe(Aw), r = de(() => {
    const s = (f) => t.getState().nodeLookup.get(f), a = (f) => {
      n.nodeQueue.push(f);
    }, c = (f) => {
      n.edgeQueue.push(f);
    }, u = (f) => {
      const { nodeLookup: p, nodeOrigin: h } = t.getState(), y = Ya(f) ? f : p.get(f.id), x = y.parentId ? ou(y.position, y.measured, y.parentId, p, h) : y.position, m = {
        ...y,
        position: x,
        width: y.measured?.width ?? y.width,
        height: y.measured?.height ?? y.height
      };
      return nn(m);
    }, l = (f, p, h = { replace: !1 }) => {
      a((y) => y.map((x) => {
        if (x.id === f) {
          const m = typeof p == "function" ? p(x) : p;
          return h.replace && Ya(m) ? m : { ...x, ...m };
        }
        return x;
      }));
    }, d = (f, p, h = { replace: !1 }) => {
      c((y) => y.map((x) => {
        if (x.id === f) {
          const m = typeof p == "function" ? p(x) : p;
          return h.replace && Sw(m) ? m : { ...x, ...m };
        }
        return x;
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
        const { nodes: f = [], edges: p = [], transform: h } = t.getState(), [y, x, m] = h;
        return {
          nodes: f.map((w) => ({ ...w })),
          edges: p.map((w) => ({ ...w })),
          viewport: {
            x: y,
            y: x,
            zoom: m
          }
        };
      },
      deleteElements: async ({ nodes: f = [], edges: p = [] }) => {
        const { nodes: h, edges: y, onNodesDelete: x, onEdgesDelete: m, triggerNodeChanges: w, triggerEdgeChanges: b, onDelete: g, onBeforeDelete: v } = t.getState(), { nodes: j, edges: N } = await Zm({
          nodesToRemove: f,
          edgesToRemove: p,
          nodes: h,
          edges: y,
          onBeforeDelete: v
        }), S = N.length > 0, I = j.length > 0;
        if (S) {
          const _ = N.map(Xa);
          m?.(N), b(_);
        }
        if (I) {
          const _ = j.map(Xa);
          x?.(j), w(_);
        }
        return (I || S) && g?.({ nodes: j, edges: N }), { deletedNodes: j, deletedEdges: N };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, p = !0, h) => {
        const y = ma(f), x = y ? f : u(f), m = h !== void 0;
        return x ? (h || t.getState().nodes).filter((w) => {
          const b = t.getState().nodeLookup.get(w.id);
          if (b && !y && (w.id === f.id || !b.internals.positionAbsolute))
            return !1;
          const g = nn(m ? w : b), v = zn(g, x);
          return p && v > 0 || v >= g.width * g.height || v >= x.width * x.height;
        }) : [];
      },
      isNodeIntersecting: (f, p, h = !0) => {
        const x = ma(f) ? f : u(f);
        if (!x)
          return !1;
        const m = zn(x, p);
        return h && m > 0 || m >= p.width * p.height || m >= x.width * x.height;
      },
      updateNode: l,
      updateNodeData: (f, p, h = { replace: !1 }) => {
        l(f, (y) => {
          const x = typeof p == "function" ? p(y) : p;
          return h.replace ? { ...y, data: x } : { ...y, data: { ...y.data, ...x } };
        }, h);
      },
      updateEdge: d,
      updateEdgeData: (f, p, h = { replace: !1 }) => {
        d(f, (y) => {
          const x = typeof p == "function" ? p(y) : p;
          return h.replace ? { ...y, data: x } : { ...y, data: { ...y.data, ...x } };
        }, h);
      },
      getNodesBounds: (f) => {
        const { nodeLookup: p, nodeOrigin: h } = t.getState();
        return Xm(f, { nodeLookup: p, nodeOrigin: h });
      },
      getHandleConnections: ({ type: f, id: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}-${f}${p ? `-${p}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}${f ? p ? `-${f}-${p}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const p = t.getState().fitViewResolver ?? Qm();
        return t.setState({ fitViewQueued: !0, fitViewOptions: f, fitViewResolver: p }), n.nodeQueue.push((h) => [...h]), p.promise;
      }
    };
  }, []);
  return de(() => ({
    ...r,
    ...e,
    viewportInitialized: i
  }), [i]);
}
const Ua = (e) => e.selected, _w = typeof window < "u" ? window : void 0;
function Dw({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = ve(), { deleteElements: i } = ps(), r = Vn(e, { actInsideInputWithModifier: !1 }), s = Vn(t, { target: _w });
  J(() => {
    if (r) {
      const { edges: a, nodes: c } = n.getState();
      i({ nodes: c.filter(Ua), edges: a.filter(Ua) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), J(() => {
    n.setState({ multiSelectionActive: s });
  }, [s]);
}
function Tw(e) {
  const t = ve();
  J(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const i = as(e.current);
      (i.height === 0 || i.width === 0) && t.getState().onError?.("004", He.error004()), t.setState({ width: i.width || 500, height: i.height || 500 });
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
const dr = {
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
function Pw({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: i = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: s = St.Free, zoomOnDoubleClick: a = !0, panOnDrag: c = !0, defaultViewport: u, translateExtent: l, minZoom: d, maxZoom: f, zoomActivationKeyCode: p, preventScrolling: h = !0, children: y, noWheelClassName: x, noPanClassName: m, onViewportChange: w, isControlledViewport: b, paneClickDistance: g, selectionOnDrag: v }) {
  const j = ve(), N = oe(null), { userSelectionActive: S, lib: I, connectionInProgress: _ } = pe($w, we), M = Vn(p), D = oe();
  Tw(N);
  const k = re((R) => {
    w?.({ x: R[0], y: R[1], zoom: R[2] }), b || j.setState({ transform: R });
  }, [w, b]);
  return J(() => {
    if (N.current) {
      D.current = Rx({
        domNode: N.current,
        minZoom: d,
        maxZoom: f,
        translateExtent: l,
        viewport: u,
        onDraggingChange: (E) => j.setState((T) => T.paneDragging === E ? T : { paneDragging: E }),
        onPanZoomStart: (E, T) => {
          const { onViewportChangeStart: $, onMoveStart: P } = j.getState();
          P?.(E, T), $?.(T);
        },
        onPanZoom: (E, T) => {
          const { onViewportChange: $, onMove: P } = j.getState();
          P?.(E, T), $?.(T);
        },
        onPanZoomEnd: (E, T) => {
          const { onViewportChangeEnd: $, onMoveEnd: P } = j.getState();
          P?.(E, T), $?.(T);
        }
      });
      const { x: R, y: C, zoom: A } = D.current.getViewport();
      return j.setState({
        panZoom: D.current,
        transform: [R, C, A],
        domNode: N.current.closest(".react-flow")
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
      panOnScrollSpeed: r,
      panOnScrollMode: s,
      zoomOnDoubleClick: a,
      panOnDrag: c,
      zoomActivationKeyPressed: M,
      preventScrolling: h,
      noPanClassName: m,
      userSelectionActive: S,
      noWheelClassName: x,
      lib: I,
      onTransformChange: k,
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
    m,
    S,
    x,
    I,
    k,
    _,
    v,
    g
  ]), o.jsx("div", { className: "react-flow__renderer", ref: N, style: dr, children: y });
}
const Mw = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function Rw() {
  const { userSelectionActive: e, userSelectionRect: t } = pe(Mw, we);
  return e && t ? o.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const fo = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, zw = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function Lw({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Rn.Full, panOnDrag: i, autoPanOnSelection: r, paneClickDistance: s, selectionOnDrag: a, onSelectionStart: c, onSelectionEnd: u, onPaneClick: l, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: p, onPaneMouseMove: h, onPaneMouseLeave: y, children: x }) {
  const m = oe(0), w = ve(), { userSelectionActive: b, elementsSelectable: g, dragging: v, connectionInProgress: j, panBy: N, autoPanSpeed: S } = pe(zw, we), I = g && (e || b), _ = oe(null), M = oe(), D = oe(/* @__PURE__ */ new Set()), k = oe(/* @__PURE__ */ new Set()), R = oe(!1), C = oe({ x: 0, y: 0 }), A = oe(!1), E = (z) => {
    if (R.current || j) {
      R.current = !1;
      return;
    }
    l?.(z), w.getState().resetSelectedElements(), w.setState({ nodesSelectionActive: !1 });
  }, T = (z) => {
    if (Array.isArray(i) && i?.includes(2)) {
      z.preventDefault();
      return;
    }
    d?.(z);
  }, $ = f ? (z) => f(z) : void 0, P = (z) => {
    R.current && (z.stopPropagation(), R.current = !1);
  }, F = (z) => {
    const { domNode: X, transform: ae } = w.getState();
    if (M.current = X?.getBoundingClientRect(), !M.current)
      return;
    const ce = z.target === _.current;
    if (!ce && !!z.target.closest(".nokey") || !e || !(a && ce || t) || z.button !== 0 || !z.isPrimary)
      return;
    z.target?.setPointerCapture?.(z.pointerId), R.current = !1;
    const { x: fe, y: O } = qe(z.nativeEvent, M.current), ee = ln({ x: fe, y: O }, ae);
    w.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: ee.x,
        startY: ee.y,
        x: fe,
        y: O
      }
    }), ce || (z.stopPropagation(), z.preventDefault());
  };
  function W(z, X) {
    const { userSelectionRect: ae } = w.getState();
    if (!ae)
      return;
    const { transform: ce, nodeLookup: Q, edgeLookup: te, connectionLookup: fe, triggerNodeChanges: O, triggerEdgeChanges: ee, defaultEdgeOptions: he } = w.getState(), me = { x: ae.startX, y: ae.startY }, { x: Te, y: ke } = rn(me, ce), _e = {
      startX: me.x,
      startY: me.y,
      x: z < Te ? z : Te,
      y: X < ke ? X : ke,
      width: Math.abs(z - Te),
      height: Math.abs(X - ke)
    }, it = D.current, We = k.current;
    D.current = new Set(rs(Q, _e, ce, n === Rn.Partial, !0).map((De) => De.id)), k.current = /* @__PURE__ */ new Set();
    const Fe = he?.selectable ?? !0;
    for (const De of D.current) {
      const Re = fe.get(De);
      if (Re)
        for (const { edgeId: L } of Re.values()) {
          const V = te.get(L);
          V && (V.selectable ?? Fe) && k.current.add(L);
        }
    }
    if (!xa(it, D.current)) {
      const De = Ft(Q, D.current, !0);
      O(De);
    }
    if (!xa(We, k.current)) {
      const De = Ft(te, k.current);
      ee(De);
    }
    w.setState({
      userSelectionRect: _e,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function H() {
    if (!r || !M.current)
      return;
    const [z, X] = os(C.current, M.current, S);
    N({ x: z, y: X }).then((ae) => {
      if (!R.current || !ae) {
        m.current = requestAnimationFrame(H);
        return;
      }
      const { x: ce, y: Q } = C.current;
      W(ce, Q), m.current = requestAnimationFrame(H);
    });
  }
  const q = () => {
    cancelAnimationFrame(m.current), m.current = 0, A.current = !1;
  };
  J(() => () => q(), []);
  const Z = (z) => {
    const { userSelectionRect: X, transform: ae, resetSelectedElements: ce } = w.getState();
    if (!M.current || !X)
      return;
    const { x: Q, y: te } = qe(z.nativeEvent, M.current);
    C.current = { x: Q, y: te };
    const fe = rn({ x: X.startX, y: X.startY }, ae);
    if (!R.current) {
      const O = t ? 0 : s;
      if (Math.hypot(Q - fe.x, te - fe.y) <= O)
        return;
      ce(), c?.(z);
    }
    R.current = !0, A.current || (H(), A.current = !0), W(Q, te);
  }, ne = (z) => {
    z.button === 0 && (z.target?.releasePointerCapture?.(z.pointerId), !b && z.target === _.current && w.getState().userSelectionRect && E?.(z), w.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), R.current && (u?.(z), w.setState({
      nodesSelectionActive: D.current.size > 0
    })), q());
  }, le = (z) => {
    z.target?.releasePointerCapture?.(z.pointerId), q();
  }, G = i === !0 || Array.isArray(i) && i.includes(0);
  return o.jsxs("div", { className: Ce(["react-flow__pane", { draggable: G, dragging: v, selection: e }]), onClick: I ? void 0 : fo(E, _), onContextMenu: fo(T, _), onWheel: fo($, _), onPointerEnter: I ? void 0 : p, onPointerMove: I ? Z : h, onPointerUp: I ? ne : void 0, onPointerCancel: I ? le : void 0, onPointerDownCapture: I ? F : void 0, onClickCapture: I ? P : void 0, onPointerLeave: y, ref: _, style: dr, children: [x, o.jsx(Rw, {})] });
}
function $o({ id: e, store: t, unselect: n = !1, nodeRef: i }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: s, multiSelectionActive: a, nodeLookup: c, onError: u } = t.getState(), l = c.get(e);
  if (!l) {
    u?.("012", He.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && a) && (s({ nodes: [l], edges: [] }), requestAnimationFrame(() => i?.current?.blur())) : r([e]);
}
function zu({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: i, nodeId: r, isSelectable: s, nodeClickDistance: a }) {
  const c = ve(), [u, l] = B(!1), d = oe();
  return J(() => {
    d.current = Nx({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (f) => {
        $o({
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
        nodeId: r,
        nodeClickDistance: a
      }), () => {
        d.current?.destroy();
      };
  }, [n, i, t, s, e, r, a]), u;
}
const Vw = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function Lu() {
  const e = ve();
  return re((n) => {
    const { nodeExtent: i, snapToGrid: r, snapGrid: s, nodesDraggable: a, onError: c, updateNodePositions: u, nodeLookup: l, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), p = Vw(a), h = r ? s[0] : 5, y = r ? s[1] : 5, x = n.direction.x * h * n.factor, m = n.direction.y * y * n.factor;
    for (const [, w] of l) {
      if (!p(w))
        continue;
      let b = {
        x: w.internals.positionAbsolute.x + x,
        y: w.internals.positionAbsolute.y + m
      };
      r && (b = Un(b, s));
      const { position: g, positionAbsolute: v } = eu({
        nodeId: w.id,
        nextPosition: b,
        nodeLookup: l,
        nodeExtent: i,
        nodeOrigin: d,
        onError: c
      });
      w.position = g, w.internals.positionAbsolute = v, f.set(w.id, w);
    }
    u(f);
  }, []);
}
const hs = Ho(null), Ow = hs.Provider;
hs.Consumer;
const Vu = () => On(hs), Hw = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), Ww = (e, t, n) => (i) => {
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
function Fw({ type: e = "source", position: t = ie.Top, isValidConnection: n, isConnectable: i = !0, isConnectableStart: r = !0, isConnectableEnd: s = !0, id: a, onConnect: c, children: u, className: l, onMouseDown: d, onTouchStart: f, ...p }, h) {
  const y = a || null, x = e === "target", m = ve(), w = Vu(), { connectOnClick: b, noPanClassName: g, rfId: v } = pe(Hw, we), { connectingFrom: j, connectingTo: N, clickConnecting: S, isPossibleEndHandle: I, connectionInProcess: _, clickConnectionInProcess: M, valid: D } = pe(Ww(w, y, e), we);
  w || m.getState().onError?.("010", He.error010());
  const k = (A) => {
    const { defaultEdgeOptions: E, onConnect: T, hasDefaultEdges: $ } = m.getState(), P = {
      ...E,
      ...A
    };
    if ($) {
      const { edges: F, setEdges: W, onError: H } = m.getState();
      W(Pu(P, F, { onError: H }));
    }
    T?.(P), c?.(P);
  }, R = (A) => {
    if (!w)
      return;
    const E = cu(A.nativeEvent);
    if (r && (E && A.button === 0 || !E)) {
      const T = m.getState();
      To.onPointerDown(A.nativeEvent, {
        handleDomNode: A.currentTarget,
        autoPanOnConnect: T.autoPanOnConnect,
        connectionMode: T.connectionMode,
        connectionRadius: T.connectionRadius,
        domNode: T.domNode,
        nodeLookup: T.nodeLookup,
        lib: T.lib,
        isTarget: x,
        handleId: y,
        nodeId: w,
        flowId: T.rfId,
        panBy: T.panBy,
        cancelConnection: T.cancelConnection,
        onConnectStart: T.onConnectStart,
        onConnectEnd: (...$) => m.getState().onConnectEnd?.(...$),
        updateConnection: T.updateConnection,
        onConnect: k,
        isValidConnection: n || ((...$) => m.getState().isValidConnection?.(...$) ?? !0),
        getTransform: () => m.getState().transform,
        getFromHandle: () => m.getState().connection.fromHandle,
        autoPanSpeed: T.autoPanSpeed,
        dragThreshold: T.connectionDragThreshold
      });
    }
    E ? d?.(A) : f?.(A);
  }, C = (A) => {
    const { onClickConnectStart: E, onClickConnectEnd: T, connectionClickStartHandle: $, connectionMode: P, isValidConnection: F, lib: W, rfId: H, nodeLookup: q, connection: Z } = m.getState();
    if (!w || !$ && !r)
      return;
    if (!$) {
      E?.(A.nativeEvent, { nodeId: w, handleId: y, handleType: e }), m.setState({ connectionClickStartHandle: { nodeId: w, type: e, id: y } });
      return;
    }
    const ne = su(A.target), le = n || F, { connection: G, isValid: z } = To.isValid(A.nativeEvent, {
      handle: {
        nodeId: w,
        id: y,
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
      nodeLookup: q
    });
    z && G && k(G);
    const X = structuredClone(Z);
    delete X.inProgress, X.toPosition = X.toHandle ? X.toHandle.position : null, T?.(A, X), m.setState({ connectionClickStartHandle: null });
  };
  return o.jsx("div", { "data-handleid": y, "data-nodeid": w, "data-handlepos": t, "data-id": `${v}-${w}-${y}-${e}`, className: Ce([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    g,
    l,
    {
      source: !x,
      target: x,
      connectable: i,
      connectablestart: r,
      connectableend: s,
      clickconnecting: S,
      connectingfrom: j,
      connectingto: N,
      valid: D,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: i && (!_ || I) && (_ || M ? s : r)
    }
  ]), onMouseDown: R, onTouchStart: R, onClick: b ? C : void 0, ref: h, ...p, children: u });
}
const sn = je(Mu(Fw));
function Bw({ data: e, isConnectable: t, sourcePosition: n = ie.Bottom }) {
  return o.jsxs(o.Fragment, { children: [e?.label, o.jsx(sn, { type: "source", position: n, isConnectable: t })] });
}
function Kw({ data: e, isConnectable: t, targetPosition: n = ie.Top, sourcePosition: i = ie.Bottom }) {
  return o.jsxs(o.Fragment, { children: [o.jsx(sn, { type: "target", position: n, isConnectable: t }), e?.label, o.jsx(sn, { type: "source", position: i, isConnectable: t })] });
}
function Xw() {
  return null;
}
function Yw({ data: e, isConnectable: t, targetPosition: n = ie.Top }) {
  return o.jsxs(o.Fragment, { children: [o.jsx(sn, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const Ui = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, Za = {
  input: Bw,
  default: Kw,
  output: Yw,
  group: Xw
};
function qw(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const Uw = (e) => {
  const { width: t, height: n, x: i, y: r } = qn(e.nodeLookup, {
    filter: (s) => !!s.selected
  });
  return {
    width: Ye(t) ? t : null,
    height: Ye(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${i}px,${r}px)`
  };
};
function Zw({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const i = ve(), { width: r, height: s, transformString: a, userSelectionActive: c } = pe(Uw, we), u = Lu(), l = oe(null);
  J(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !c && r !== null && s !== null;
  if (zu({
    nodeRef: l,
    disabled: !d
  }), !d)
    return null;
  const f = e ? (h) => {
    const y = i.getState().nodes.filter((x) => x.selected);
    e(h, y);
  } : void 0, p = (h) => {
    Object.prototype.hasOwnProperty.call(Ui, h.key) && (h.preventDefault(), u({
      direction: Ui[h.key],
      factor: h.shiftKey ? 4 : 1
    }));
  };
  return o.jsx("div", { className: Ce(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: a
  }, children: o.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : p, style: {
    width: r,
    height: s
  } }) });
}
const Ga = typeof window < "u" ? window : void 0, Gw = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function Ou({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: i, onPaneMouseLeave: r, onPaneContextMenu: s, onPaneScroll: a, paneClickDistance: c, deleteKeyCode: u, selectionKeyCode: l, selectionOnDrag: d, selectionMode: f, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: y, panActivationKeyCode: x, zoomActivationKeyCode: m, elementsSelectable: w, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: v, panOnScrollSpeed: j, panOnScrollMode: N, zoomOnDoubleClick: S, panOnDrag: I, autoPanOnSelection: _, defaultViewport: M, translateExtent: D, minZoom: k, maxZoom: R, preventScrolling: C, onSelectionContextMenu: A, noWheelClassName: E, noPanClassName: T, disableKeyboardA11y: $, onViewportChange: P, isControlledViewport: F }) {
  const { nodesSelectionActive: W, userSelectionActive: H } = pe(Gw, we), q = Vn(l, { target: Ga }), Z = Vn(x, { target: Ga }), ne = Z || I, le = Z || v, G = d && ne !== !0, z = q || H || G;
  return Dw({ deleteKeyCode: u, multiSelectionKeyCode: y }), o.jsx(Pw, { onPaneContextMenu: s, elementsSelectable: w, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: le, panOnScrollSpeed: j, panOnScrollMode: N, zoomOnDoubleClick: S, panOnDrag: !q && ne, defaultViewport: M, translateExtent: D, minZoom: k, maxZoom: R, zoomActivationKeyCode: m, preventScrolling: C, noWheelClassName: E, noPanClassName: T, onViewportChange: P, isControlledViewport: F, paneClickDistance: c, selectionOnDrag: G, children: o.jsxs(Lw, { onSelectionStart: p, onSelectionEnd: h, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: i, onPaneMouseLeave: r, onPaneContextMenu: s, onPaneScroll: a, panOnDrag: ne, autoPanOnSelection: _, isSelecting: !!z, selectionMode: f, selectionKeyPressed: q, paneClickDistance: c, selectionOnDrag: G, children: [e, W && o.jsx(Zw, { onSelectionContextMenu: A, noPanClassName: T, disableKeyboardA11y: $ })] }) });
}
Ou.displayName = "FlowRenderer";
const Jw = je(Ou), Qw = (e) => (t) => e ? rs(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function ev(e) {
  return pe(re(Qw(e), [e]), we);
}
const tv = (e) => e.updateNodeInternals;
function nv() {
  const e = pe(tv), [t] = B(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
  return J(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function iv({ node: e, nodeType: t, hasDimensions: n, resizeObserver: i }) {
  const r = ve(), s = oe(null), a = oe(null), c = oe(e.sourcePosition), u = oe(e.targetPosition), l = oe(t), d = n && !!e.internals.handleBounds;
  return J(() => {
    s.current && !e.hidden && (!d || a.current !== s.current) && (a.current && i?.unobserve(a.current), i?.observe(s.current), a.current = s.current);
  }, [d, e.hidden]), J(() => () => {
    a.current && (i?.unobserve(a.current), a.current = null);
  }, []), J(() => {
    if (s.current) {
      const f = l.current !== t, p = c.current !== e.sourcePosition, h = u.current !== e.targetPosition;
      (f || p || h) && (l.current = t, c.current = e.sourcePosition, u.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: s.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), s;
}
function rv({ id: e, onClick: t, onMouseEnter: n, onMouseMove: i, onMouseLeave: r, onContextMenu: s, onDoubleClick: a, nodesDraggable: c, elementsSelectable: u, nodesConnectable: l, nodesFocusable: d, resizeObserver: f, noDragClassName: p, noPanClassName: h, disableKeyboardA11y: y, rfId: x, nodeTypes: m, nodeClickDistance: w, onError: b }) {
  const { node: g, internals: v, isParent: j } = pe((z) => {
    const X = z.nodeLookup.get(e), ae = z.parentLookup.has(e);
    return {
      node: X,
      internals: X.internals,
      isParent: ae
    };
  }, we);
  let N = g.type || "default", S = m?.[N] || Za[N];
  S === void 0 && (b?.("003", He.error003(N)), N = "default", S = m?.default || Za.default);
  const I = !!(g.draggable || c && typeof g.draggable > "u"), _ = !!(g.selectable || u && typeof g.selectable > "u"), M = !!(g.connectable || l && typeof g.connectable > "u"), D = !!(g.focusable || d && typeof g.focusable > "u"), k = ve(), R = ru(g), C = iv({ node: g, nodeType: N, hasDimensions: R, resizeObserver: f }), A = zu({
    nodeRef: C,
    disabled: g.hidden || !I,
    noDragClassName: p,
    handleSelector: g.dragHandle,
    nodeId: e,
    isSelectable: _,
    nodeClickDistance: w
  }), E = Lu();
  if (g.hidden)
    return null;
  const T = lt(g), $ = qw(g), P = _ || I || t || n || i || r, F = n ? (z) => n(z, { ...v.userNode }) : void 0, W = i ? (z) => i(z, { ...v.userNode }) : void 0, H = r ? (z) => r(z, { ...v.userNode }) : void 0, q = s ? (z) => s(z, { ...v.userNode }) : void 0, Z = a ? (z) => a(z, { ...v.userNode }) : void 0, ne = (z) => {
    const { selectNodesOnDrag: X, nodeDragThreshold: ae } = k.getState();
    _ && (!X || !I || ae > 0) && $o({
      id: e,
      store: k,
      nodeRef: C
    }), t && t(z, { ...v.userNode });
  }, le = (z) => {
    if (!(au(z.nativeEvent) || y)) {
      if (Ul.includes(z.key) && _) {
        const X = z.key === "Escape";
        $o({
          id: e,
          store: k,
          unselect: X,
          nodeRef: C
        });
      } else if (I && g.selected && Object.prototype.hasOwnProperty.call(Ui, z.key)) {
        z.preventDefault();
        const { ariaLabelConfig: X } = k.getState();
        k.setState({
          ariaLiveMessage: X["node.a11yDescription.ariaLiveMessage"]({
            direction: z.key.replace("Arrow", "").toLowerCase(),
            x: ~~v.positionAbsolute.x,
            y: ~~v.positionAbsolute.y
          })
        }), E({
          direction: Ui[z.key],
          factor: z.shiftKey ? 4 : 1
        });
      }
    }
  }, G = () => {
    if (y || !C.current?.matches(":focus-visible"))
      return;
    const { transform: z, width: X, height: ae, autoPanOnNodeFocus: ce, setCenter: Q } = k.getState();
    if (!ce)
      return;
    rs(/* @__PURE__ */ new Map([[e, g]]), { x: 0, y: 0, width: X, height: ae }, z, !0).length > 0 || Q(g.position.x + T.width / 2, g.position.y + T.height / 2, {
      zoom: z[2]
    });
  };
  return o.jsx("div", { className: Ce([
    "react-flow__node",
    `react-flow__node-${N}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [h]: I
    },
    g.className,
    {
      selected: g.selected,
      selectable: _,
      parent: j,
      draggable: I,
      dragging: A
    }
  ]), ref: C, style: {
    zIndex: v.z,
    transform: `translate(${v.positionAbsolute.x}px,${v.positionAbsolute.y}px)`,
    pointerEvents: P ? "all" : "none",
    visibility: R ? "visible" : "hidden",
    ...g.style,
    ...$
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: F, onMouseMove: W, onMouseLeave: H, onContextMenu: q, onClick: ne, onDoubleClick: Z, onKeyDown: D ? le : void 0, tabIndex: D ? 0 : void 0, onFocus: D ? G : void 0, role: g.ariaRole ?? (D ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": y ? void 0 : `${ku}-${x}`, "aria-label": g.ariaLabel, ...g.domAttributes, children: o.jsx(Ow, { value: e, children: o.jsx(S, { id: e, data: g.data, type: N, positionAbsoluteX: v.positionAbsolute.x, positionAbsoluteY: v.positionAbsolute.y, selected: g.selected ?? !1, selectable: _, draggable: I, deletable: g.deletable ?? !0, isConnectable: M, sourcePosition: g.sourcePosition, targetPosition: g.targetPosition, dragging: A, dragHandle: g.dragHandle, zIndex: v.z, parentId: g.parentId, ...T }) }) });
}
var ov = je(rv);
const sv = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function Hu(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: i, elementsSelectable: r, onError: s } = pe(sv, we), a = ev(e.onlyRenderVisibleElements), c = nv();
  return o.jsx("div", { className: "react-flow__nodes", style: dr, children: a.map((u) => (
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
    o.jsx(ov, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: i, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: s }, u)
  )) });
}
Hu.displayName = "NodeRenderer";
const av = je(Hu);
function cv(e) {
  return pe(re((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const i = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const s = n.nodeLookup.get(r.source), a = n.nodeLookup.get(r.target);
        s && a && ix({
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
const lv = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return o.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, uv = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return o.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Ja = {
  [Xi.Arrow]: lv,
  [Xi.ArrowClosed]: uv
};
function dv(e) {
  const t = ve();
  return de(() => Object.prototype.hasOwnProperty.call(Ja, e) ? Ja[e] : (t.getState().onError?.("009", He.error009(e)), null), [e]);
}
const fv = ({ id: e, type: t, color: n, width: i = 12.5, height: r = 12.5, markerUnits: s = "strokeWidth", strokeWidth: a, orient: c = "auto-start-reverse" }) => {
  const u = dv(t);
  return u ? o.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${i}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: s, orient: c, refX: "0", refY: "0", children: o.jsx(u, { color: n, strokeWidth: a }) }) : null;
}, Wu = ({ defaultColor: e, rfId: t }) => {
  const n = pe((s) => s.edges), i = pe((s) => s.defaultEdgeOptions), r = de(() => dx(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: i?.markerStart,
    defaultMarkerEnd: i?.markerEnd
  }), [n, i, t, e]);
  return r.length ? o.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: o.jsx("defs", { children: r.map((s) => o.jsx(fv, { id: s.id, type: s.type, color: s.color, width: s.width, height: s.height, markerUnits: s.markerUnits, strokeWidth: s.strokeWidth, orient: s.orient }, s.id)) }) }) : null;
};
Wu.displayName = "MarkerDefinitions";
var pv = je(Wu);
function Fu({ x: e, y: t, label: n, labelStyle: i, labelShowBg: r = !0, labelBgStyle: s, labelBgPadding: a = [2, 4], labelBgBorderRadius: c = 2, children: u, className: l, ...d }) {
  const [f, p] = B({ x: 1, y: 0, width: 0, height: 0 }), h = Ce(["react-flow__edge-textwrapper", l]), y = oe(null);
  return J(() => {
    if (y.current) {
      const x = y.current.getBBox();
      p({
        x: x.x,
        y: x.y,
        width: x.width,
        height: x.height
      });
    }
  }, [n]), n ? o.jsxs("g", { transform: `translate(${e - f.width / 2} ${t - f.height / 2})`, className: h, visibility: f.width ? "visible" : "hidden", ...d, children: [r && o.jsx("rect", { width: f.width + 2 * a[0], x: -a[0], y: -a[1], height: f.height + 2 * a[1], className: "react-flow__edge-textbg", style: s, rx: c, ry: c }), o.jsx("text", { className: "react-flow__edge-text", y: f.height / 2, dy: "0.3em", ref: y, style: i, children: n }), u] }) : null;
}
Fu.displayName = "EdgeText";
const hv = je(Fu);
function Zn({ path: e, labelX: t, labelY: n, label: i, labelStyle: r, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u, interactionWidth: l = 20, ...d }) {
  return o.jsxs(o.Fragment, { children: [o.jsx("path", { ...d, d: e, fill: "none", className: Ce(["react-flow__edge-path", d.className]) }), l ? o.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, i && Ye(t) && Ye(n) ? o.jsx(hv, { x: t, y: n, label: i, labelStyle: r, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u }) : null] });
}
function Qa({ pos: e, x1: t, y1: n, x2: i, y2: r }) {
  return e === ie.Left || e === ie.Right ? [0.5 * (t + i), n] : [t, 0.5 * (n + r)];
}
function Bu({ sourceX: e, sourceY: t, sourcePosition: n = ie.Bottom, targetX: i, targetY: r, targetPosition: s = ie.Top }) {
  const [a, c] = Qa({
    pos: n,
    x1: e,
    y1: t,
    x2: i,
    y2: r
  }), [u, l] = Qa({
    pos: s,
    x1: i,
    y1: r,
    x2: e,
    y2: t
  }), [d, f, p, h] = lu({
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
function Ku(e) {
  return je(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, sourcePosition: a, targetPosition: c, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: x, markerStart: m, interactionWidth: w }) => {
    const [b, g, v] = Bu({
      sourceX: n,
      sourceY: i,
      sourcePosition: a,
      targetX: r,
      targetY: s,
      targetPosition: c
    }), j = e.isInternal ? void 0 : t;
    return o.jsx(Zn, { id: j, path: b, labelX: g, labelY: v, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: x, markerStart: m, interactionWidth: w });
  });
}
const gv = Ku({ isInternal: !1 }), Xu = Ku({ isInternal: !0 });
gv.displayName = "SimpleBezierEdge";
Xu.displayName = "SimpleBezierEdgeInternal";
function Yu(e) {
  return je(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, sourcePosition: h = ie.Bottom, targetPosition: y = ie.Top, markerEnd: x, markerStart: m, pathOptions: w, interactionWidth: b }) => {
    const [g, v, j] = qi({
      sourceX: n,
      sourceY: i,
      sourcePosition: h,
      targetX: r,
      targetY: s,
      targetPosition: y,
      borderRadius: w?.borderRadius,
      offset: w?.offset,
      stepPosition: w?.stepPosition
    }), N = e.isInternal ? void 0 : t;
    return o.jsx(Zn, { id: N, path: g, labelX: v, labelY: j, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: x, markerStart: m, interactionWidth: b });
  });
}
const qu = Yu({ isInternal: !1 }), Uu = Yu({ isInternal: !0 });
qu.displayName = "SmoothStepEdge";
Uu.displayName = "SmoothStepEdgeInternal";
function Zu(e) {
  return je(({ id: t, ...n }) => {
    const i = e.isInternal ? void 0 : t;
    return o.jsx(qu, { ...n, id: i, pathOptions: de(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const yv = Zu({ isInternal: !1 }), Gu = Zu({ isInternal: !0 });
yv.displayName = "StepEdge";
Gu.displayName = "StepEdgeInternal";
function Ju(e) {
  return je(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: y, interactionWidth: x }) => {
    const [m, w, b] = pu({ sourceX: n, sourceY: i, targetX: r, targetY: s }), g = e.isInternal ? void 0 : t;
    return o.jsx(Zn, { id: g, path: m, labelX: w, labelY: b, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: y, interactionWidth: x });
  });
}
const mv = Ju({ isInternal: !1 }), Qu = Ju({ isInternal: !0 });
mv.displayName = "StraightEdge";
Qu.displayName = "StraightEdgeInternal";
function ed(e) {
  return je(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, sourcePosition: a = ie.Bottom, targetPosition: c = ie.Top, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: x, markerStart: m, pathOptions: w, interactionWidth: b }) => {
    const [g, v, j] = uu({
      sourceX: n,
      sourceY: i,
      sourcePosition: a,
      targetX: r,
      targetY: s,
      targetPosition: c,
      curvature: w?.curvature
    }), N = e.isInternal ? void 0 : t;
    return o.jsx(Zn, { id: N, path: g, labelX: v, labelY: j, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: x, markerStart: m, interactionWidth: b });
  });
}
const xv = ed({ isInternal: !1 }), td = ed({ isInternal: !0 });
xv.displayName = "BezierEdge";
td.displayName = "BezierEdgeInternal";
const ec = {
  default: td,
  straight: Qu,
  step: Gu,
  smoothstep: Uu,
  simplebezier: Xu
}, tc = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, wv = (e, t, n) => n === ie.Left ? e - t : n === ie.Right ? e + t : e, vv = (e, t, n) => n === ie.Top ? e - t : n === ie.Bottom ? e + t : e, nc = "react-flow__edgeupdater";
function ic({ position: e, centerX: t, centerY: n, radius: i = 10, onMouseDown: r, onMouseEnter: s, onMouseOut: a, type: c }) {
  return o.jsx("circle", { onMouseDown: r, onMouseEnter: s, onMouseOut: a, className: Ce([nc, `${nc}-${c}`]), cx: wv(t, i, e), cy: vv(n, i, e), r: i, stroke: "transparent", fill: "transparent" });
}
function bv({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: i, sourceY: r, targetX: s, targetY: a, sourcePosition: c, targetPosition: u, onReconnect: l, onReconnectStart: d, onReconnectEnd: f, setReconnecting: p, setUpdateHover: h }) {
  const y = ve(), x = (v, j) => {
    if (v.button !== 0)
      return;
    const { autoPanOnConnect: N, domNode: S, connectionMode: I, connectionRadius: _, lib: M, onConnectStart: D, cancelConnection: k, nodeLookup: R, rfId: C, panBy: A, updateConnection: E } = y.getState(), T = j.type === "target", $ = (W, H) => {
      p(!1), f?.(W, n, j.type, H);
    }, P = (W) => l?.(n, W), F = (W, H) => {
      p(!0), d?.(v, n, j.type), D?.(W, H);
    };
    To.onPointerDown(v.nativeEvent, {
      autoPanOnConnect: N,
      connectionMode: I,
      connectionRadius: _,
      domNode: S,
      handleId: j.id,
      nodeId: j.nodeId,
      nodeLookup: R,
      isTarget: T,
      edgeUpdaterType: j.type,
      lib: M,
      flowId: C,
      cancelConnection: k,
      panBy: A,
      isValidConnection: (...W) => y.getState().isValidConnection?.(...W) ?? !0,
      onConnect: P,
      onConnectStart: F,
      onConnectEnd: (...W) => y.getState().onConnectEnd?.(...W),
      onReconnectEnd: $,
      updateConnection: E,
      getTransform: () => y.getState().transform,
      getFromHandle: () => y.getState().connection.fromHandle,
      dragThreshold: y.getState().connectionDragThreshold,
      handleDomNode: v.currentTarget
    });
  }, m = (v) => x(v, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), w = (v) => x(v, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), b = () => h(!0), g = () => h(!1);
  return o.jsxs(o.Fragment, { children: [(e === !0 || e === "source") && o.jsx(ic, { position: c, centerX: i, centerY: r, radius: t, onMouseDown: m, onMouseEnter: b, onMouseOut: g, type: "source" }), (e === !0 || e === "target") && o.jsx(ic, { position: u, centerX: s, centerY: a, radius: t, onMouseDown: w, onMouseEnter: b, onMouseOut: g, type: "target" })] });
}
function Nv({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: i, onClick: r, onDoubleClick: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, rfId: y, edgeTypes: x, noPanClassName: m, onError: w, disableKeyboardA11y: b }) {
  let g = pe((Q) => Q.edgeLookup.get(e));
  const v = pe((Q) => Q.defaultEdgeOptions);
  g = v ? { ...v, ...g } : g;
  let j = g.type || "default", N = x?.[j] || ec[j];
  N === void 0 && (w?.("011", He.error011(j)), j = "default", N = x?.default || ec.default);
  const S = !!(g.focusable || t && typeof g.focusable > "u"), I = typeof f < "u" && (g.reconnectable || n && typeof g.reconnectable > "u"), _ = !!(g.selectable || i && typeof g.selectable > "u"), M = oe(null), [D, k] = B(!1), [R, C] = B(!1), A = ve(), { zIndex: E, sourceX: T, sourceY: $, targetX: P, targetY: F, sourcePosition: W, targetPosition: H } = pe(re((Q) => {
    const te = Q.nodeLookup.get(g.source), fe = Q.nodeLookup.get(g.target);
    if (!te || !fe)
      return {
        zIndex: g.zIndex,
        ...tc
      };
    const O = ux({
      id: e,
      sourceNode: te,
      targetNode: fe,
      sourceHandle: g.sourceHandle || null,
      targetHandle: g.targetHandle || null,
      connectionMode: Q.connectionMode,
      onError: w
    });
    return {
      zIndex: nx({
        selected: g.selected,
        zIndex: g.zIndex,
        sourceNode: te,
        targetNode: fe,
        elevateOnSelect: Q.elevateEdgesOnSelect,
        zIndexMode: Q.zIndexMode
      }),
      ...O || tc
    };
  }, [g.source, g.target, g.sourceHandle, g.targetHandle, g.selected, g.zIndex]), we), q = de(() => g.markerStart ? `url('#${_o(g.markerStart, y)}')` : void 0, [g.markerStart, y]), Z = de(() => g.markerEnd ? `url('#${_o(g.markerEnd, y)}')` : void 0, [g.markerEnd, y]);
  if (g.hidden || T === null || $ === null || P === null || F === null)
    return null;
  const ne = (Q) => {
    const { addSelectedEdges: te, unselectNodesAndEdges: fe, multiSelectionActive: O } = A.getState();
    _ && (A.setState({ nodesSelectionActive: !1 }), g.selected && O ? (fe({ nodes: [], edges: [g] }), M.current?.blur()) : te([e])), r && r(Q, g);
  }, le = s ? (Q) => {
    s(Q, { ...g });
  } : void 0, G = a ? (Q) => {
    a(Q, { ...g });
  } : void 0, z = c ? (Q) => {
    c(Q, { ...g });
  } : void 0, X = u ? (Q) => {
    u(Q, { ...g });
  } : void 0, ae = l ? (Q) => {
    l(Q, { ...g });
  } : void 0, ce = (Q) => {
    if (!b && Ul.includes(Q.key) && _) {
      const { unselectNodesAndEdges: te, addSelectedEdges: fe } = A.getState();
      Q.key === "Escape" ? (M.current?.blur(), te({ edges: [g] })) : fe([e]);
    }
  };
  return o.jsx("svg", { style: { zIndex: E }, children: o.jsxs("g", { className: Ce([
    "react-flow__edge",
    `react-flow__edge-${j}`,
    g.className,
    m,
    {
      selected: g.selected,
      animated: g.animated,
      inactive: !_ && !r,
      updating: D,
      selectable: _
    }
  ]), onClick: ne, onDoubleClick: le, onContextMenu: G, onMouseEnter: z, onMouseMove: X, onMouseLeave: ae, onKeyDown: S ? ce : void 0, tabIndex: S ? 0 : void 0, role: g.ariaRole ?? (S ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": g.ariaLabel === null ? void 0 : g.ariaLabel || `Edge from ${g.source} to ${g.target}`, "aria-describedby": S ? `${Iu}-${y}` : void 0, ref: M, ...g.domAttributes, children: [!R && o.jsx(N, { id: e, source: g.source, target: g.target, type: g.type, selected: g.selected, animated: g.animated, selectable: _, deletable: g.deletable ?? !0, label: g.label, labelStyle: g.labelStyle, labelShowBg: g.labelShowBg, labelBgStyle: g.labelBgStyle, labelBgPadding: g.labelBgPadding, labelBgBorderRadius: g.labelBgBorderRadius, sourceX: T, sourceY: $, targetX: P, targetY: F, sourcePosition: W, targetPosition: H, data: g.data, style: g.style, sourceHandleId: g.sourceHandle, targetHandleId: g.targetHandle, markerStart: q, markerEnd: Z, pathOptions: "pathOptions" in g ? g.pathOptions : void 0, interactionWidth: g.interactionWidth }), I && o.jsx(bv, { edge: g, isReconnectable: I, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, sourceX: T, sourceY: $, targetX: P, targetY: F, sourcePosition: W, targetPosition: H, setUpdateHover: k, setReconnecting: C })] }) });
}
var jv = je(Nv);
const Sv = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function nd({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: i, noPanClassName: r, onReconnect: s, onEdgeContextMenu: a, onEdgeMouseEnter: c, onEdgeMouseMove: u, onEdgeMouseLeave: l, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: p, onReconnectStart: h, onReconnectEnd: y, disableKeyboardA11y: x }) {
  const { edgesFocusable: m, edgesReconnectable: w, elementsSelectable: b, onError: g } = pe(Sv, we), v = cv(t);
  return o.jsxs("div", { className: "react-flow__edges", children: [o.jsx(pv, { defaultColor: e, rfId: n }), v.map((j) => o.jsx(jv, { id: j, edgesFocusable: m, edgesReconnectable: w, elementsSelectable: b, noPanClassName: r, onReconnect: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, onClick: d, reconnectRadius: f, onDoubleClick: p, onReconnectStart: h, onReconnectEnd: y, rfId: n, onError: g, edgeTypes: i, disableKeyboardA11y: x }, j))] });
}
nd.displayName = "EdgeRenderer";
const Cv = je(nd), Ev = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function kv({ children: e }) {
  const t = pe(Ev);
  return o.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function Iv(e) {
  const t = ps(), n = oe(!1);
  J(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Av = (e) => e.panZoom?.syncViewport;
function _v(e) {
  const t = pe(Av), n = ve();
  return J(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function Dv(e) {
  return e.connection.inProgress ? { ...e.connection, to: ln(e.connection.to, e.transform) } : { ...e.connection };
}
function Tv(e) {
  return Dv;
}
function $v(e) {
  const t = Tv();
  return pe(t, we);
}
const Pv = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function Mv({ containerStyle: e, style: t, type: n, component: i }) {
  const { nodesConnectable: r, width: s, height: a, isValid: c, inProgress: u } = pe(Pv, we);
  return !(s && r && u) ? null : o.jsx("svg", { style: e, width: s, height: a, className: "react-flow__connectionline react-flow__container", children: o.jsx("g", { className: Ce(["react-flow__connection", Jl(c)]), children: o.jsx(id, { style: t, type: n, CustomComponent: i, isValid: c }) }) });
}
const id = ({ style: e, type: t = pt.Bezier, CustomComponent: n, isValid: i }) => {
  const { inProgress: r, from: s, fromNode: a, fromHandle: c, fromPosition: u, to: l, toNode: d, toHandle: f, toPosition: p, pointer: h } = $v();
  if (!r)
    return;
  if (n)
    return o.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: a, fromHandle: c, fromX: s.x, fromY: s.y, toX: l.x, toY: l.y, fromPosition: u, toPosition: p, connectionStatus: Jl(i), toNode: d, toHandle: f, pointer: h });
  let y = "";
  const x = {
    sourceX: s.x,
    sourceY: s.y,
    sourcePosition: u,
    targetX: l.x,
    targetY: l.y,
    targetPosition: p
  };
  switch (t) {
    case pt.Bezier:
      [y] = uu(x);
      break;
    case pt.SimpleBezier:
      [y] = Bu(x);
      break;
    case pt.Step:
      [y] = qi({
        ...x,
        borderRadius: 0
      });
      break;
    case pt.SmoothStep:
      [y] = qi(x);
      break;
    default:
      [y] = pu(x);
  }
  return o.jsx("path", { d: y, fill: "none", className: "react-flow__connection-path", style: e });
};
id.displayName = "ConnectionLine";
const Rv = {};
function rc(e = Rv) {
  oe(e), ve(), J(() => {
  }, [e]);
}
function zv() {
  ve(), oe(!1), J(() => {
  }, []);
}
function rd({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: i, onEdgeClick: r, onNodeDoubleClick: s, onEdgeDoubleClick: a, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: p, onSelectionEnd: h, connectionLineType: y, connectionLineStyle: x, connectionLineComponent: m, connectionLineContainerStyle: w, selectionKeyCode: b, selectionOnDrag: g, selectionMode: v, multiSelectionKeyCode: j, panActivationKeyCode: N, zoomActivationKeyCode: S, deleteKeyCode: I, onlyRenderVisibleElements: _, elementsSelectable: M, defaultViewport: D, translateExtent: k, minZoom: R, maxZoom: C, preventScrolling: A, defaultMarkerColor: E, zoomOnScroll: T, zoomOnPinch: $, panOnScroll: P, panOnScrollSpeed: F, panOnScrollMode: W, zoomOnDoubleClick: H, panOnDrag: q, autoPanOnSelection: Z, onPaneClick: ne, onPaneMouseEnter: le, onPaneMouseMove: G, onPaneMouseLeave: z, onPaneScroll: X, onPaneContextMenu: ae, paneClickDistance: ce, nodeClickDistance: Q, onEdgeContextMenu: te, onEdgeMouseEnter: fe, onEdgeMouseMove: O, onEdgeMouseLeave: ee, reconnectRadius: he, onReconnect: me, onReconnectStart: Te, onReconnectEnd: ke, noDragClassName: _e, noWheelClassName: it, noPanClassName: We, disableKeyboardA11y: Fe, nodeExtent: De, rfId: Re, viewport: L, onViewportChange: V }) {
  return rc(e), rc(t), zv(), Iv(n), _v(L), o.jsx(Jw, { onPaneClick: ne, onPaneMouseEnter: le, onPaneMouseMove: G, onPaneMouseLeave: z, onPaneContextMenu: ae, onPaneScroll: X, paneClickDistance: ce, deleteKeyCode: I, selectionKeyCode: b, selectionOnDrag: g, selectionMode: v, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: j, panActivationKeyCode: N, zoomActivationKeyCode: S, elementsSelectable: M, zoomOnScroll: T, zoomOnPinch: $, zoomOnDoubleClick: H, panOnScroll: P, panOnScrollSpeed: F, panOnScrollMode: W, panOnDrag: q, autoPanOnSelection: Z, defaultViewport: D, translateExtent: k, minZoom: R, maxZoom: C, onSelectionContextMenu: f, preventScrolling: A, noDragClassName: _e, noWheelClassName: it, noPanClassName: We, disableKeyboardA11y: Fe, onViewportChange: V, isControlledViewport: !!L, children: o.jsxs(kv, { children: [o.jsx(Cv, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: a, onReconnect: me, onReconnectStart: Te, onReconnectEnd: ke, onlyRenderVisibleElements: _, onEdgeContextMenu: te, onEdgeMouseEnter: fe, onEdgeMouseMove: O, onEdgeMouseLeave: ee, reconnectRadius: he, defaultMarkerColor: E, noPanClassName: We, disableKeyboardA11y: Fe, rfId: Re }), o.jsx(Mv, { style: x, type: y, component: m, containerStyle: w }), o.jsx("div", { className: "react-flow__edgelabel-renderer" }), o.jsx(av, { nodeTypes: e, onNodeClick: i, onNodeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, nodeClickDistance: Q, onlyRenderVisibleElements: _, noPanClassName: We, noDragClassName: _e, disableKeyboardA11y: Fe, nodeExtent: De, rfId: Re }), o.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
rd.displayName = "GraphView";
const Lv = je(rd), Vv = iu(), oc = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, width: r, height: s, fitView: a, fitViewOptions: c, minZoom: u = 0.5, maxZoom: l = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: p = "basic" } = {}) => {
  const h = /* @__PURE__ */ new Map(), y = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), m = /* @__PURE__ */ new Map(), w = i ?? t ?? [], b = n ?? e ?? [], g = d ?? [0, 0], v = f ?? Mn;
  yu(x, m, w);
  const { nodesInitialized: j } = Do(b, h, y, {
    nodeOrigin: g,
    nodeExtent: v,
    zIndexMode: p
  });
  let N = [0, 0, 1];
  if (a && r && s) {
    const S = qn(h, {
      filter: (D) => !!((D.width || D.initialWidth) && (D.height || D.initialHeight))
    }), { x: I, y: _, zoom: M } = ss(S, r, s, u, l, c?.padding ?? 0.1);
    N = [I, _, M];
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
    edges: w,
    edgeLookup: m,
    connectionLookup: x,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: i !== void 0,
    panZoom: null,
    minZoom: u,
    maxZoom: l,
    translateExtent: Mn,
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
    connection: { ...Gl },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: Vv,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Zl,
    zIndexMode: p,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, Ov = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, width: r, height: s, fitView: a, fitViewOptions: c, minZoom: u, maxZoom: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p }) => Qx((h, y) => {
  async function x() {
    const { nodeLookup: m, panZoom: w, fitViewOptions: b, fitViewResolver: g, width: v, height: j, minZoom: N, maxZoom: S } = y();
    w && (await Um({
      nodes: m,
      width: v,
      height: j,
      panZoom: w,
      minZoom: N,
      maxZoom: S
    }, b), g?.resolve(!0), h({ fitViewResolver: null }));
  }
  return {
    ...oc({
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
    setNodes: (m) => {
      const { nodeLookup: w, parentLookup: b, nodeOrigin: g, elevateNodesOnSelect: v, fitViewQueued: j, zIndexMode: N, nodesSelectionActive: S } = y(), { nodesInitialized: I, hasSelectedNodes: _ } = Do(m, w, b, {
        nodeOrigin: g,
        nodeExtent: f,
        elevateNodesOnSelect: v,
        checkEquality: !0,
        zIndexMode: N
      }), M = S && _;
      j && I ? (x(), h({
        nodes: m,
        nodesInitialized: I,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: M
      })) : h({ nodes: m, nodesInitialized: I, nodesSelectionActive: M });
    },
    setEdges: (m) => {
      const { connectionLookup: w, edgeLookup: b } = y();
      yu(w, b, m), h({ edges: m });
    },
    setDefaultNodesAndEdges: (m, w) => {
      if (m) {
        const { setNodes: b } = y();
        b(m), h({ hasDefaultNodes: !0 });
      }
      if (w) {
        const { setEdges: b } = y();
        b(w), h({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (m) => {
      const { triggerNodeChanges: w, nodeLookup: b, parentLookup: g, domNode: v, nodeOrigin: j, nodeExtent: N, debug: S, fitViewQueued: I, zIndexMode: _ } = y(), { changes: M, updatedInternals: D } = xx(m, b, g, v, j, N, _);
      D && (hx(b, g, { nodeOrigin: j, nodeExtent: N, zIndexMode: _ }), I ? (x(), h({ fitViewQueued: !1, fitViewOptions: void 0 })) : h({}), M?.length > 0 && (S && console.log("React Flow: trigger node changes", M), w?.(M)));
    },
    updateNodePositions: (m, w = !1) => {
      const b = [];
      let g = [];
      const { nodeLookup: v, triggerNodeChanges: j, connection: N, updateConnection: S, onNodesChangeMiddlewareMap: I } = y();
      for (const [_, M] of m) {
        const D = v.get(_), k = !!(D?.expandParent && D?.parentId && M?.position), R = {
          id: _,
          type: "position",
          position: k ? {
            x: Math.max(0, M.position.x),
            y: Math.max(0, M.position.y)
          } : M.position,
          dragging: w
        };
        if (D && N.inProgress && N.fromNode.id === D.id) {
          const C = At(D, N.fromHandle, ie.Left, !0);
          S({ ...N, from: C });
        }
        k && D.parentId && b.push({
          id: _,
          parentId: D.parentId,
          rect: {
            ...M.internals.positionAbsolute,
            width: M.measured.width ?? 0,
            height: M.measured.height ?? 0
          }
        }), g.push(R);
      }
      if (b.length > 0) {
        const { parentLookup: _, nodeOrigin: M } = y(), D = fs(b, v, _, M);
        g.push(...D);
      }
      for (const _ of I.values())
        g = _(g);
      j(g);
    },
    triggerNodeChanges: (m) => {
      const { onNodesChange: w, setNodes: b, nodes: g, hasDefaultNodes: v, debug: j } = y();
      if (m?.length) {
        if (v) {
          const N = Du(m, g);
          b(N);
        }
        j && console.log("React Flow: trigger node changes", m), w?.(m);
      }
    },
    triggerEdgeChanges: (m) => {
      const { onEdgesChange: w, setEdges: b, edges: g, hasDefaultEdges: v, debug: j } = y();
      if (m?.length) {
        if (v) {
          const N = Tu(m, g);
          b(N);
        }
        j && console.log("React Flow: trigger edge changes", m), w?.(m);
      }
    },
    addSelectedNodes: (m) => {
      const { multiSelectionActive: w, edgeLookup: b, nodeLookup: g, triggerNodeChanges: v, triggerEdgeChanges: j } = y();
      if (w) {
        const N = m.map((S) => wt(S, !0));
        v(N);
        return;
      }
      v(Ft(g, /* @__PURE__ */ new Set([...m]), !0)), j(Ft(b));
    },
    addSelectedEdges: (m) => {
      const { multiSelectionActive: w, edgeLookup: b, nodeLookup: g, triggerNodeChanges: v, triggerEdgeChanges: j } = y();
      if (w) {
        const N = m.map((S) => wt(S, !0));
        j(N);
        return;
      }
      j(Ft(b, /* @__PURE__ */ new Set([...m]))), v(Ft(g, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: m, edges: w } = {}) => {
      const { edges: b, nodes: g, nodeLookup: v, triggerNodeChanges: j, triggerEdgeChanges: N } = y(), S = m || g, I = w || b, _ = [];
      for (const D of S) {
        if (!D.selected)
          continue;
        const k = v.get(D.id);
        k && (k.selected = !1), _.push(wt(D.id, !1));
      }
      const M = [];
      for (const D of I)
        D.selected && M.push(wt(D.id, !1));
      j(_), N(M);
    },
    setMinZoom: (m) => {
      const { panZoom: w, maxZoom: b } = y();
      w?.setScaleExtent([m, b]), h({ minZoom: m });
    },
    setMaxZoom: (m) => {
      const { panZoom: w, minZoom: b } = y();
      w?.setScaleExtent([b, m]), h({ maxZoom: m });
    },
    setTranslateExtent: (m) => {
      y().panZoom?.setTranslateExtent(m), h({ translateExtent: m });
    },
    resetSelectedElements: () => {
      const { edges: m, nodes: w, triggerNodeChanges: b, triggerEdgeChanges: g, elementsSelectable: v } = y();
      if (!v)
        return;
      const j = w.reduce((S, I) => I.selected ? [...S, wt(I.id, !1)] : S, []), N = m.reduce((S, I) => I.selected ? [...S, wt(I.id, !1)] : S, []);
      b(j), g(N);
    },
    setNodeExtent: (m) => {
      const { nodes: w, nodeLookup: b, parentLookup: g, nodeOrigin: v, elevateNodesOnSelect: j, nodeExtent: N, zIndexMode: S } = y();
      m[0][0] === N[0][0] && m[0][1] === N[0][1] && m[1][0] === N[1][0] && m[1][1] === N[1][1] || (Do(w, b, g, {
        nodeOrigin: v,
        nodeExtent: m,
        elevateNodesOnSelect: j,
        checkEquality: !1,
        zIndexMode: S
      }), h({ nodeExtent: m }));
    },
    panBy: (m) => {
      const { transform: w, width: b, height: g, panZoom: v, translateExtent: j } = y();
      return wx({ delta: m, panZoom: v, transform: w, translateExtent: j, width: b, height: g });
    },
    setCenter: async (m, w, b) => {
      const { width: g, height: v, maxZoom: j, panZoom: N } = y();
      if (!N)
        return !1;
      const S = typeof b?.zoom < "u" ? b.zoom : j;
      return await N.setViewport({
        x: g / 2 - m * S,
        y: v / 2 - w * S,
        zoom: S
      }, { duration: b?.duration, ease: b?.ease, interpolate: b?.interpolate }), !0;
    },
    cancelConnection: () => {
      h({
        connection: { ...Gl }
      });
    },
    updateConnection: (m) => {
      h({ connection: m });
    },
    reset: () => h({ ...oc() })
  };
}, Object.is);
function Hv({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: i, initialWidth: r, initialHeight: s, initialMinZoom: a, initialMaxZoom: c, initialFitViewOptions: u, fitView: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p, children: h }) {
  const [y] = B(() => Ov({
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
  return o.jsx(iw, { value: y, children: o.jsx(kw, { children: h }) });
}
function Wv({ children: e, nodes: t, edges: n, defaultNodes: i, defaultEdges: r, width: s, height: a, fitView: c, fitViewOptions: u, minZoom: l, maxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h }) {
  return On(lr) ? o.jsx(o.Fragment, { children: e }) : o.jsx(Hv, { initialNodes: t, initialEdges: n, defaultNodes: i, defaultEdges: r, initialWidth: s, initialHeight: a, fitView: c, initialFitViewOptions: u, initialMinZoom: l, initialMaxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h, children: e });
}
const Fv = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function Bv({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, className: r, nodeTypes: s, edgeTypes: a, onNodeClick: c, onEdgeClick: u, onInit: l, onMove: d, onMoveStart: f, onMoveEnd: p, onConnect: h, onConnectStart: y, onConnectEnd: x, onClickConnectStart: m, onClickConnectEnd: w, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: v, onNodeContextMenu: j, onNodeDoubleClick: N, onNodeDragStart: S, onNodeDrag: I, onNodeDragStop: _, onNodesDelete: M, onEdgesDelete: D, onDelete: k, onSelectionChange: R, onSelectionDragStart: C, onSelectionDrag: A, onSelectionDragStop: E, onSelectionContextMenu: T, onSelectionStart: $, onSelectionEnd: P, onBeforeDelete: F, connectionMode: W, connectionLineType: H = pt.Bezier, connectionLineStyle: q, connectionLineComponent: Z, connectionLineContainerStyle: ne, deleteKeyCode: le = "Backspace", selectionKeyCode: G = "Shift", selectionOnDrag: z = !1, selectionMode: X = Rn.Full, panActivationKeyCode: ae = "Space", multiSelectionKeyCode: ce = Ln() ? "Meta" : "Control", zoomActivationKeyCode: Q = Ln() ? "Meta" : "Control", snapToGrid: te, snapGrid: fe, onlyRenderVisibleElements: O = !1, selectNodesOnDrag: ee, nodesDraggable: he, autoPanOnNodeFocus: me, nodesConnectable: Te, nodesFocusable: ke, nodeOrigin: _e = Au, edgesFocusable: it, edgesReconnectable: We, elementsSelectable: Fe = !0, defaultViewport: De = yw, minZoom: Re = 0.5, maxZoom: L = 2, translateExtent: V = Mn, preventScrolling: K = !0, nodeExtent: Y, defaultMarkerColor: se = "#b1b1b7", zoomOnScroll: xe = !0, zoomOnPinch: ge = !0, panOnScroll: Ee = !1, panOnScrollSpeed: Ie = 0.5, panOnScrollMode: Be = St.Free, zoomOnDoubleClick: ye = !0, panOnDrag: hr = !0, onPaneClick: Jn, onPaneMouseEnter: mt, onPaneMouseMove: Pt, onPaneMouseLeave: Ze, onPaneScroll: Mt, onPaneContextMenu: gr, paneClickDistance: Qn = 1, nodeClickDistance: yr = 0, children: mr, onReconnect: ut, onReconnectStart: xr, onReconnectEnd: Rt, onEdgeContextMenu: wr, onEdgeDoubleClick: zt, onEdgeMouseEnter: vr, onEdgeMouseMove: ei, onEdgeMouseLeave: ti, reconnectRadius: br = 10, onNodesChange: Lt, onEdgesChange: Nr, noDragClassName: jr = "nodrag", noWheelClassName: Sr = "nowheel", noPanClassName: Vt = "nopan", fitView: ni, fitViewOptions: ii, connectOnClick: Cr, attributionPosition: Er, proOptions: kr, defaultEdgeOptions: Ir, elevateNodesOnSelect: Ar = !0, elevateEdgesOnSelect: _r = !1, disableKeyboardA11y: ri = !1, autoPanOnConnect: Dr, autoPanOnNodeDrag: Tr, autoPanOnSelection: $r = !0, autoPanSpeed: Pr, connectionRadius: Mr, isValidConnection: oi, onError: si, style: ai, id: dn, nodeDragThreshold: Rr, connectionDragThreshold: zr, viewport: Lr, onViewportChange: Vr, width: Or, height: Hr, colorMode: Wr = "light", debug: Fr, onScroll: ci, ariaLabelConfig: Br, zIndexMode: li = "basic", ...Kr }, Xr) {
  const fn = dn || "1", Yr = vw(Wr), qr = re((xt) => {
    xt.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), ci?.(xt);
  }, [ci]);
  return o.jsx("div", { "data-testid": "rf__wrapper", ...Kr, onScroll: qr, style: { ...ai, ...Fv }, ref: Xr, className: Ce(["react-flow", r, Yr]), id: dn, role: "application", children: o.jsxs(Wv, { nodes: e, edges: t, width: Or, height: Hr, fitView: ni, fitViewOptions: ii, minZoom: Re, maxZoom: L, nodeOrigin: _e, nodeExtent: Y, zIndexMode: li, children: [o.jsx(ww, { nodes: e, edges: t, defaultNodes: n, defaultEdges: i, onConnect: h, onConnectStart: y, onConnectEnd: x, onClickConnectStart: m, onClickConnectEnd: w, nodesDraggable: he, autoPanOnNodeFocus: me, nodesConnectable: Te, nodesFocusable: ke, edgesFocusable: it, edgesReconnectable: We, elementsSelectable: Fe, elevateNodesOnSelect: Ar, elevateEdgesOnSelect: _r, minZoom: Re, maxZoom: L, nodeExtent: Y, onNodesChange: Lt, onEdgesChange: Nr, snapToGrid: te, snapGrid: fe, connectionMode: W, translateExtent: V, connectOnClick: Cr, defaultEdgeOptions: Ir, fitView: ni, fitViewOptions: ii, onNodesDelete: M, onEdgesDelete: D, onDelete: k, onNodeDragStart: S, onNodeDrag: I, onNodeDragStop: _, onSelectionDrag: A, onSelectionDragStart: C, onSelectionDragStop: E, onMove: d, onMoveStart: f, onMoveEnd: p, noPanClassName: Vt, nodeOrigin: _e, rfId: fn, autoPanOnConnect: Dr, autoPanOnNodeDrag: Tr, autoPanSpeed: Pr, onError: si, connectionRadius: Mr, isValidConnection: oi, selectNodesOnDrag: ee, nodeDragThreshold: Rr, connectionDragThreshold: zr, onBeforeDelete: F, debug: Fr, ariaLabelConfig: Br, zIndexMode: li }), o.jsx(Lv, { onInit: l, onNodeClick: c, onEdgeClick: u, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: v, onNodeContextMenu: j, onNodeDoubleClick: N, nodeTypes: s, edgeTypes: a, connectionLineType: H, connectionLineStyle: q, connectionLineComponent: Z, connectionLineContainerStyle: ne, selectionKeyCode: G, selectionOnDrag: z, selectionMode: X, deleteKeyCode: le, multiSelectionKeyCode: ce, panActivationKeyCode: ae, zoomActivationKeyCode: Q, onlyRenderVisibleElements: O, defaultViewport: De, translateExtent: V, minZoom: Re, maxZoom: L, preventScrolling: K, zoomOnScroll: xe, zoomOnPinch: ge, zoomOnDoubleClick: ye, panOnScroll: Ee, panOnScrollSpeed: Ie, panOnScrollMode: Be, panOnDrag: hr, autoPanOnSelection: $r, onPaneClick: Jn, onPaneMouseEnter: mt, onPaneMouseMove: Pt, onPaneMouseLeave: Ze, onPaneScroll: Mt, onPaneContextMenu: gr, paneClickDistance: Qn, nodeClickDistance: yr, onSelectionContextMenu: T, onSelectionStart: $, onSelectionEnd: P, onReconnect: ut, onReconnectStart: xr, onReconnectEnd: Rt, onEdgeContextMenu: wr, onEdgeDoubleClick: zt, onEdgeMouseEnter: vr, onEdgeMouseMove: ei, onEdgeMouseLeave: ti, reconnectRadius: br, defaultMarkerColor: se, noDragClassName: jr, noWheelClassName: Sr, noPanClassName: Vt, rfId: fn, disableKeyboardA11y: ri, nodeExtent: Y, viewport: Lr, onViewportChange: Vr }), o.jsx(gw, { onSelectionChange: R }), mr, o.jsx(uw, { proOptions: kr, position: Er }), o.jsx(lw, { rfId: fn, disableKeyboardA11y: ri })] }) });
}
var od = Mu(Bv);
const Kv = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function Xv({ children: e }) {
  const t = pe(Kv);
  return t ? nw.createPortal(e, t) : null;
}
function Yv({ dimensions: e, lineWidth: t, variant: n, className: i }) {
  return o.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ce(["react-flow__background-pattern", n, i]) });
}
function qv({ radius: e, className: t }) {
  return o.jsx("circle", { cx: e, cy: e, r: e, className: Ce(["react-flow__background-pattern", "dots", t]) });
}
var ht;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(ht || (ht = {}));
const Uv = {
  [ht.Dots]: 1,
  [ht.Lines]: 1,
  [ht.Cross]: 6
}, Zv = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function sd({
  id: e,
  variant: t = ht.Dots,
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
  const f = oe(null), { transform: p, patternId: h } = pe(Zv, we), y = i || Uv[t], x = t === ht.Dots, m = t === ht.Cross, w = Array.isArray(n) ? n : [n, n], b = [w[0] * p[2] || 1, w[1] * p[2] || 1], g = y * p[2], v = Array.isArray(s) ? s : [s, s], j = m ? [g, g] : b, N = [
    v[0] * p[2] || 1 + j[0] / 2,
    v[1] * p[2] || 1 + j[1] / 2
  ], S = `${h}${e || ""}`;
  return o.jsxs("svg", { className: Ce(["react-flow__background", l]), style: {
    ...u,
    ...dr,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": a
  }, ref: f, "data-testid": "rf__background", children: [o.jsx("pattern", { id: S, x: p[0] % b[0], y: p[1] % b[1], width: b[0], height: b[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${N[0]},-${N[1]})`, children: x ? o.jsx(qv, { radius: g / 2, className: d }) : o.jsx(Yv, { dimensions: j, lineWidth: r, variant: t, className: d }) }), o.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${S})` })] });
}
sd.displayName = "Background";
const ad = je(sd);
function Gv() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: o.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function Jv() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: o.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function Qv() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: o.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function e0() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: o.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function t0() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: o.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function xi({ children: e, className: t, ...n }) {
  return o.jsx("button", { type: "button", className: Ce(["react-flow__controls-button", t]), ...n, children: e });
}
const n0 = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function cd({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: i = !0, fitViewOptions: r, onZoomIn: s, onZoomOut: a, onFitView: c, onInteractiveChange: u, className: l, children: d, position: f = "bottom-left", orientation: p = "vertical", "aria-label": h }) {
  const y = ve(), { isInteractive: x, minZoomReached: m, maxZoomReached: w, ariaLabelConfig: b } = pe(n0, we), { zoomIn: g, zoomOut: v, fitView: j } = ps(), N = () => {
    g(), s?.();
  }, S = () => {
    v(), a?.();
  }, I = () => {
    j(r), c?.();
  }, _ = () => {
    y.setState({
      nodesDraggable: !x,
      nodesConnectable: !x,
      elementsSelectable: !x
    }), u?.(!x);
  }, M = p === "horizontal" ? "horizontal" : "vertical";
  return o.jsxs(ur, { className: Ce(["react-flow__controls", M, l]), position: f, style: e, "data-testid": "rf__controls", "aria-label": h ?? b["controls.ariaLabel"], children: [t && o.jsxs(o.Fragment, { children: [o.jsx(xi, { onClick: N, className: "react-flow__controls-zoomin", title: b["controls.zoomIn.ariaLabel"], "aria-label": b["controls.zoomIn.ariaLabel"], disabled: w, children: o.jsx(Gv, {}) }), o.jsx(xi, { onClick: S, className: "react-flow__controls-zoomout", title: b["controls.zoomOut.ariaLabel"], "aria-label": b["controls.zoomOut.ariaLabel"], disabled: m, children: o.jsx(Jv, {}) })] }), n && o.jsx(xi, { className: "react-flow__controls-fitview", onClick: I, title: b["controls.fitView.ariaLabel"], "aria-label": b["controls.fitView.ariaLabel"], children: o.jsx(Qv, {}) }), i && o.jsx(xi, { className: "react-flow__controls-interactive", onClick: _, title: b["controls.interactive.ariaLabel"], "aria-label": b["controls.interactive.ariaLabel"], children: x ? o.jsx(t0, {}) : o.jsx(e0, {}) }), d] });
}
cd.displayName = "Controls";
const ld = je(cd);
function i0({ id: e, x: t, y: n, width: i, height: r, style: s, color: a, strokeColor: c, strokeWidth: u, className: l, borderRadius: d, shapeRendering: f, selected: p, onClick: h }) {
  const { background: y, backgroundColor: x } = s || {}, m = a || y || x;
  return o.jsx("rect", { className: Ce(["react-flow__minimap-node", { selected: p }, l]), x: t, y: n, rx: d, ry: d, width: i, height: r, style: {
    fill: m,
    stroke: c,
    strokeWidth: u
  }, shapeRendering: f, onClick: h ? (w) => h(w, e) : void 0 });
}
const r0 = je(i0), o0 = (e) => e.nodes.map((t) => t.id), po = (e) => e instanceof Function ? e : () => e;
function s0({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: i = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: s = r0,
  onClick: a
}) {
  const c = pe(o0, we), u = po(t), l = po(e), d = po(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return o.jsx(o.Fragment, { children: c.map((p) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    o.jsx(c0, { id: p, nodeColorFunc: u, nodeStrokeColorFunc: l, nodeClassNameFunc: d, nodeBorderRadius: i, nodeStrokeWidth: r, NodeComponent: s, onClick: a, shapeRendering: f }, p)
  )) });
}
function a0({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: i, nodeBorderRadius: r, nodeStrokeWidth: s, shapeRendering: a, NodeComponent: c, onClick: u }) {
  const { node: l, x: d, y: f, width: p, height: h } = pe((y) => {
    const x = y.nodeLookup.get(e);
    if (!x)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const m = x.internals.userNode, { x: w, y: b } = x.internals.positionAbsolute, { width: g, height: v } = lt(m);
    return {
      node: m,
      x: w,
      y: b,
      width: g,
      height: v
    };
  }, we);
  return !l || l.hidden || !ru(l) ? null : o.jsx(c, { x: d, y: f, width: p, height: h, style: l.style, selected: !!l.selected, className: i(l), color: t(l), borderRadius: r, strokeColor: n(l), strokeWidth: s, shapeRendering: a, onClick: u, id: l.id });
}
const c0 = je(a0);
var l0 = je(s0);
const u0 = 200, d0 = 150, f0 = (e) => !e.hidden, p0 = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? nu(qn(e.nodeLookup, { filter: f0 }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, h0 = "react-flow__minimap-desc";
function ud({
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
  pannable: x = !1,
  zoomable: m = !1,
  ariaLabel: w,
  inversePan: b,
  zoomStep: g = 1,
  offsetScale: v = 5
}) {
  const j = ve(), N = oe(null), { boundingRect: S, viewBB: I, rfId: _, panZoom: M, translateExtent: D, flowWidth: k, flowHeight: R, ariaLabelConfig: C } = pe(p0, we), A = e?.width ?? u0, E = e?.height ?? d0, T = S.width / A, $ = S.height / E, P = Math.max(T, $), F = P * A, W = P * E, H = v * P, q = S.x - (F - S.width) / 2 - H, Z = S.y - (W - S.height) / 2 - H, ne = F + H * 2, le = W + H * 2, G = `${h0}-${_}`, z = oe(0), X = oe();
  z.current = P, J(() => {
    if (N.current && M)
      return X.current = Ix({
        domNode: N.current,
        panZoom: M,
        getTransform: () => j.getState().transform,
        getViewScale: () => z.current
      }), () => {
        X.current?.destroy();
      };
  }, [M]), J(() => {
    X.current?.update({
      translateExtent: D,
      width: k,
      height: R,
      inversePan: b,
      pannable: x,
      zoomStep: g,
      zoomable: m
    });
  }, [x, m, b, g, D, k, R]);
  const ae = h ? (te) => {
    const [fe, O] = X.current?.pointer(te) || [0, 0];
    h(te, { x: fe, y: O });
  } : void 0, ce = y ? re((te, fe) => {
    const O = j.getState().nodeLookup.get(fe).internals.userNode;
    y(te, O);
  }, []) : void 0, Q = w ?? C["minimap.ariaLabel"];
  return o.jsx(ur, { position: p, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * P : void 0,
    "--xy-minimap-node-background-color-props": typeof i == "string" ? i : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof a == "number" ? a : void 0
  }, className: Ce(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: o.jsxs("svg", { width: A, height: E, viewBox: `${q} ${Z} ${ne} ${le}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": G, ref: N, onClick: ae, children: [Q && o.jsx("title", { id: G, children: Q }), o.jsx(l0, { onClick: ce, nodeColor: i, nodeStrokeColor: n, nodeBorderRadius: s, nodeClassName: r, nodeStrokeWidth: a, nodeComponent: c }), o.jsx("path", { className: "react-flow__minimap-mask", d: `M${q - H},${Z - H}h${ne + H * 2}v${le + H * 2}h${-ne - H * 2}z
        M${I.x},${I.y}h${I.width}v${I.height}h${-I.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
ud.displayName = "MiniMap";
const dd = je(ud), g0 = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, y0 = {
  [on.Line]: "right",
  [on.Handle]: "bottom-right"
};
function m0({ nodeId: e, position: t, variant: n = on.Handle, className: i, style: r = void 0, children: s, color: a, minWidth: c = 10, minHeight: u = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: p, autoScale: h = !0, shouldResize: y, onResizeStart: x, onResize: m, onResizeEnd: w }) {
  const b = Vu(), g = typeof e == "string" ? e : b, v = ve(), j = oe(null), N = n === on.Handle, S = pe(re(g0(N && h), [N, h]), we), I = oe(null), _ = t ?? y0[n];
  J(() => {
    if (!(!j.current || !g))
      return I.current || (I.current = Hx({
        domNode: j.current,
        nodeId: g,
        getStoreItems: () => {
          const { nodeLookup: D, transform: k, snapGrid: R, snapToGrid: C, nodeOrigin: A, domNode: E } = v.getState();
          return {
            nodeLookup: D,
            transform: k,
            snapGrid: R,
            snapToGrid: C,
            nodeOrigin: A,
            paneDomNode: E
          };
        },
        onChange: (D, k) => {
          const { triggerNodeChanges: R, nodeLookup: C, parentLookup: A, nodeOrigin: E } = v.getState(), T = [], $ = { x: D.x, y: D.y }, P = C.get(g);
          if (P && P.expandParent && P.parentId) {
            const F = P.origin ?? E, W = D.width ?? P.measured.width ?? 0, H = D.height ?? P.measured.height ?? 0, q = {
              id: P.id,
              parentId: P.parentId,
              rect: {
                width: W,
                height: H,
                ...ou({
                  x: D.x ?? P.position.x,
                  y: D.y ?? P.position.y
                }, { width: W, height: H }, P.parentId, C, F)
              }
            }, Z = fs([q], C, A, E);
            T.push(...Z), $.x = D.x ? Math.max(F[0] * W, D.x) : void 0, $.y = D.y ? Math.max(F[1] * H, D.y) : void 0;
          }
          if ($.x !== void 0 && $.y !== void 0) {
            const F = {
              id: g,
              type: "position",
              position: { ...$ }
            };
            T.push(F);
          }
          if (D.width !== void 0 && D.height !== void 0) {
            const W = {
              id: g,
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
          for (const F of k) {
            const W = {
              ...F,
              type: "position"
            };
            T.push(W);
          }
          R(T);
        },
        onEnd: ({ width: D, height: k }) => {
          const R = {
            id: g,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: D,
              height: k
            }
          };
          v.getState().triggerNodeChanges([R]);
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
        onResizeStart: x,
        onResize: m,
        onResizeEnd: w,
        shouldResize: y
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
    x,
    m,
    w,
    y
  ]);
  const M = _.split("-");
  return o.jsx("div", { className: Ce(["react-flow__resize-control", "nodrag", ...M, n, i]), ref: j, style: {
    ...r,
    scale: S,
    ...a && { [N ? "backgroundColor" : "borderColor"]: a }
  }, children: s });
}
je(m0);
function x0(e, t) {
  return {
    ...t ? { name: t } : {},
    definitionId: e.definitionId,
    state: Wn(e.state),
    layout: e.layout
  };
}
function w0(e) {
  return JSON.stringify(
    {
      state: Wn(e.state),
      layout: e.layout
    },
    null,
    2
  );
}
function v0(e, t) {
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
      state: Ji(i.state),
      layout: i.layout ?? t.layout
    }
  };
}
function b0(e, t) {
  const n = (t ?? "workflow").trim().replace(/[^\w.-]+/g, "-") || "workflow", i = new Blob([JSON.stringify(e, null, 2)], { type: "application/json" }), r = URL.createObjectURL(i), s = document.createElement("a");
  s.href = r, s.download = `${n}.json`, document.body.appendChild(s), s.click(), s.remove(), URL.revokeObjectURL(r);
}
function sc({
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
function N0({
  document: e,
  diagnostics: t = [],
  readOnly: n = !1,
  theme: i = "studio",
  minHeight: r = "220px",
  ariaLabel: s,
  languageAdapter: a,
  onChange: c
}) {
  const u = t.filter((p) => !p.uri || p.uri === e.uri), l = a?.displayName ?? e.language, d = a?.loadEditor, f = de(
    () => d ? qd(d) : null,
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
        f ? /* @__PURE__ */ o.jsx(Ud, { fallback: /* @__PURE__ */ o.jsx(
          sc,
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
          sc,
          {
            document: e,
            readOnly: n,
            minHeight: r,
            ariaLabel: s,
            onChange: c
          }
        ),
        /* @__PURE__ */ o.jsx(j0, { diagnostics: u })
      ]
    }
  );
}
function j0({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ o.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const i = t.severity ?? "info", r = S0(t);
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
function S0(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const C0 = { language: "json", displayName: "JSON" };
function E0({ draft: e, onApply: t }) {
  const n = de(() => w0(e), [e]), [i, r] = B(n), [s, a] = B(n), [c, u] = B(null);
  J(() => {
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
      N0,
      {
        ariaLabel: "Workflow JSON",
        document: { uri: "elsa://workflows/definition.json", language: "json", value: i },
        languageAdapter: C0,
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
function Ve(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function gs(e, t) {
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
function ys(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ o.jsx(Oc, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ o.jsx(Fo, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ o.jsx(Qd, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ o.jsx(Ut, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ o.jsx(Jd, { size: 15 });
    default:
      return /* @__PURE__ */ o.jsx(Hn, { size: 15 });
  }
}
const k0 = ["Single", "Array", "List", "HashSet"];
function fd(e) {
  const [t, n] = B(null), [i, r] = B(null);
  J(() => {
    let u = !1;
    return Up(e).then(
      (l) => {
        u || n(l);
      },
      () => {
        u || n([]);
      }
    ), Gp(e).then(
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
  const s = de(
    () => t && t.length > 0 ? t.map((u) => {
      const l = Rs(u);
      return {
        value: l,
        label: Uc(u.displayName, l),
        group: u.category?.trim() || "Other"
      };
    }) : null,
    [t]
  ), a = de(
    () => i && i.length > 0 ? i.filter((u) => !u.deprecated).map((u) => ({
      value: u.typeName,
      label: Af(u.displayName, u.typeName)
    })) : null,
    [i]
  ), c = de(() => {
    const u = /* @__PURE__ */ new Map();
    for (const l of t ?? []) {
      const d = Rs(l), f = l.defaultEditor?.trim();
      d && u.set(d, f && f.length > 0 ? f : "text");
    }
    return (l) => u.get(l) ?? "text";
  }, [t]);
  return { typeOptions: s, storageOptions: a, editorForAlias: c };
}
function I0(e) {
  return !e || e.length === 0 ? void 0 : (e.find((n) => /(^|\.)String$/i.test(n.value) || n.label.toLowerCase() === "string") ?? e[0]).value;
}
function A0(e, t, n) {
  return {
    add: () => {
      const i = wf(n.namePrefix, e.map((r) => Sn(r, n.nameKeys)));
      t([...e, n.create(i)]);
    },
    update: (i, r) => t(e.map((s, a) => a === i ? n.patch(s, r) : s)),
    remove: (i) => t(e.filter((r, s) => s !== i))
  };
}
function ac({ value: e, options: t, placeholder: n, allowEmpty: i, ariaLabel: r, onChange: s }) {
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
const _0 = {
  Single: "Single",
  Array: "Array",
  List: "List",
  HashSet: "Hash set"
};
function D0({ value: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ o.jsx("select", { "aria-label": t, value: e, onChange: (i) => n(i.target.value), children: k0.map((i) => /* @__PURE__ */ o.jsx("option", { value: i, children: _0[i] }, i)) });
}
function T0(e, t) {
  return t === "" ? !0 : e === "checkbox" ? t === "true" || t === "false" || t === "True" || t === "False" : e === "number" ? Number.isFinite(Number(t.trim())) && t.trim() !== "" : e === "date" ? /^\d{4}-\d{2}-\d{2}$/.test(t) : !0;
}
function $0({ value: e, editor: t, ariaLabel: n, onChange: i }) {
  const r = T0(t, e);
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
function P0({ title: e, addLabel: t, emptyLabel: n, headers: i, isEmpty: r, onAdd: s, children: a }) {
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-properties-section-head", children: [
      /* @__PURE__ */ o.jsx("h3", { children: e }),
      /* @__PURE__ */ o.jsxs("button", { type: "button", className: "wf-properties-add", onClick: s, children: [
        /* @__PURE__ */ o.jsx(Zt, { size: 14 }),
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
function M0({ label: e, onRemove: t }) {
  return /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-properties-remove", "aria-label": e, title: e, onClick: t, children: /* @__PURE__ */ o.jsx(In, { size: 14 }) }) });
}
function R0({ checked: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ o.jsx("input", { type: "checkbox", "aria-label": t, checked: e, onChange: (i) => n(i.target.checked) });
}
function ms({
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
  const { add: y, update: x, remove: m } = A0(e, h, {
    namePrefix: r,
    nameKeys: s,
    create: (g) => l(g, I0(t)),
    patch: d
  }), w = ["Name", "Type", "Collection", ...f.default ? ["Default"] : [], ...f.storage ? ["Storage"] : [], ...f.required ? ["Required"] : []], b = r.toLowerCase();
  return /* @__PURE__ */ o.jsx(
    P0,
    {
      title: a,
      addLabel: c,
      emptyLabel: u,
      headers: w,
      isEmpty: e.length === 0,
      onAdd: y,
      children: e.map((g, v) => {
        const j = Sn(g, s), N = qc(g), S = Sn(g, Gc), I = S ? p?.get(S) : void 0, _ = N.collectionKind === "Single" ? i(N.alias) : "text";
        return /* @__PURE__ */ o.jsxs("tr", { children: [
          /* @__PURE__ */ o.jsxs("td", { children: [
            /* @__PURE__ */ o.jsx("input", { type: "text", "aria-label": `${r} name`, value: j, onChange: (M) => x(v, { name: M.target.value }) }),
            I ? /* @__PURE__ */ o.jsx("span", { className: "wf-properties-warning", role: "note", title: I, children: I }) : null
          ] }),
          /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            ac,
            {
              ariaLabel: `${r} type`,
              value: N.alias,
              options: t,
              placeholder: "Type",
              onChange: (M) => x(v, { type: { alias: M, collectionKind: N.collectionKind } })
            }
          ) }),
          /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            D0,
            {
              ariaLabel: `${r} collection kind`,
              value: N.collectionKind,
              onChange: (M) => x(v, { type: { alias: N.alias, collectionKind: M } })
            }
          ) }),
          f.default ? /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            $0,
            {
              ariaLabel: `${r} default value`,
              value: jf(g.default),
              editor: _,
              onChange: (M) => x(v, { default: Nf(M) })
            }
          ) }) : null,
          f.storage ? /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            ac,
            {
              ariaLabel: `${r} storage driver`,
              value: Sn(g, Df),
              options: n,
              placeholder: "—",
              allowEmpty: !0,
              onChange: (M) => x(v, { storageDriverType: M || null })
            }
          ) }) : null,
          f.required ? /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            R0,
            {
              ariaLabel: `${r} required`,
              checked: g.isRequired === !0,
              onChange: (M) => x(v, { isRequired: M })
            }
          ) }) : null,
          /* @__PURE__ */ o.jsx(M0, { label: `Remove ${b} ${j || v + 1}`, onRemove: () => m(v) })
        ] }, v);
      })
    }
  );
}
function pd({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, title: r = "Variables", addLabel: s = "Add variable", emptyLabel: a = "No variables defined.", warnings: c, onChange: u }) {
  return /* @__PURE__ */ o.jsx(
    ms,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Variable",
      nameKeys: _f,
      title: r,
      addLabel: s,
      emptyLabel: a,
      create: (l, d) => vf({ name: l, alias: d }),
      patch: (l, d) => bf(l, d),
      columns: { default: !0, storage: !0 },
      warnings: c,
      onChange: u
    }
  );
}
function z0({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, onChange: r }) {
  return /* @__PURE__ */ o.jsx(
    ms,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Input",
      nameKeys: Zc,
      title: "Inputs",
      addLabel: "Add input",
      emptyLabel: "No inputs defined.",
      create: (s, a) => Sf({ name: s, alias: a }),
      patch: (s, a) => Cf(s, a),
      columns: { default: !1, storage: !0, required: !0 },
      onChange: r
    }
  );
}
function L0({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, onChange: r }) {
  return /* @__PURE__ */ o.jsx(
    ms,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Output",
      nameKeys: Zc,
      title: "Outputs",
      addLabel: "Add output",
      emptyLabel: "No outputs defined.",
      create: (s, a) => Ef({ name: s, alias: a }),
      patch: (s, a) => kf(s, a),
      columns: { default: !1, storage: !1 },
      onChange: r
    }
  );
}
function Ai(e) {
  return (e ?? []).filter($i);
}
function V0({ context: e, variables: t, title: n, addLabel: i, emptyLabel: r, warnings: s, onChange: a }) {
  const { typeOptions: c, storageOptions: u, editorForAlias: l } = fd(e);
  return /* @__PURE__ */ o.jsx(
    pd,
    {
      items: Ai(t),
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
function O0({ definition: e, definitionId: t, onMetaChange: n }) {
  const i = !!n, [r, s] = B(e?.name ?? ""), [a, c] = B(e?.description ?? "");
  J(() => {
    s(e?.name ?? "");
  }, [e?.name]), J(() => {
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
function H0({ details: e, draft: t, context: n, onStateChange: i, onDefinitionMetaChange: r }) {
  const { typeOptions: s, storageOptions: a, editorForAlias: c } = fd(n), u = Ai(t.state.variables), l = Ai(t.state.inputs), d = Ai(t.state.outputs), f = e?.versions ?? [];
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-properties-view", children: [
    /* @__PURE__ */ o.jsx(
      O0,
      {
        definition: e?.definition,
        definitionId: t.definitionId,
        onMetaChange: r
      }
    ),
    /* @__PURE__ */ o.jsx(
      pd,
      {
        items: u,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, variables: p }))
      }
    ),
    /* @__PURE__ */ o.jsx(
      z0,
      {
        items: l,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, inputs: p }))
      }
    ),
    /* @__PURE__ */ o.jsx(
      L0,
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
        /* @__PURE__ */ o.jsx("time", { children: Ve(p.createdAt) })
      ] }, p.id)) })
    ] })
  ] });
}
const cc = "application/x-elsa-activity-version-id", W0 = 6, F0 = 1200, B0 = 250, K0 = [10, 25, 50], X0 = 10, lc = "elsa-studio-workflow-palette-width", uc = "elsa-studio-workflow-inspector-width", dc = "elsa-studio-workflow-palette-collapsed", fc = "elsa-studio-workflow-inspector-collapsed", hd = "elsa-studio-workflow-side-panel-maximized", vn = 180, bn = 460, Y0 = 260, Bt = 260, Kt = 560, q0 = 320, pc = 42, wi = 16, gd = Qe.createContext(null), yd = Qe.createContext(null);
function U0(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function md(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Dt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function Tt(e, t, n) {
  const i = t.createPrompt(n);
  return i ? (e.dispatchPrompt(i), !0) : !1;
}
function Z0(e) {
  if (!e) return null;
  const t = [], n = e.match(/```(?:json)?\s*([\s\S]*?)```/i);
  n && t.push(n[1]);
  const i = e.match(/\{[\s\S]*?\}/);
  i && t.push(i[0]);
  for (const a of t)
    try {
      const c = JSON.parse(a.trim()), u = hc(c.name, c.description);
      if (u) return u;
    } catch {
    }
  const r = e.match(/name\s*[:-]\s*(.+)/i)?.[1], s = e.match(/description\s*[:-]\s*(.+)/i)?.[1];
  return hc(r, s);
}
function hc(e, t) {
  const n = typeof e == "string" ? gc(e) : void 0, i = typeof t == "string" ? gc(t) : void 0;
  return n || i ? { name: n || void 0, description: i || void 0 } : null;
}
function gc(e) {
  return e.trim().replace(/,$/, "").trim().replace(/^["']/, "").replace(/["']$/, "").trim();
}
function G0(e, t) {
  return e.rootActivityVersionId ?? xd(t, e.rootKind)?.activityVersionId ?? null;
}
function xd(e, t) {
  return e.find((n) => J0(n) === t);
}
function J0(e) {
  return e ? Q0(e) ? "flowchart" : eb(e) ? "sequence" : null : null;
}
function Po(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const i = n.category?.trim() || "Uncategorized";
    t.set(i, [...t.get(i) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [i]) => n.localeCompare(i)).map(([n, i]) => ({
    category: n,
    activities: i.sort((r, s) => Se(r).localeCompare(Se(s)))
  }));
}
function Q0(e) {
  return Se(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function eb(e) {
  return Se(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function tb(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function wd(e) {
  return sb(e.rootActivityType) || e.rootActivityType;
}
function nb(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function ib(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function rb(e, t) {
  return yc(t) - yc(e);
}
function yc(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function vd(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
function bd(e) {
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
function sb(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function ab(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    vi(t, n.typeName, n), vi(t, n.name, n), vi(t, n.displayName, n);
    const i = n.typeName.split(".").filter(Boolean).at(-1);
    vi(t, i, n);
  }
  return t;
}
function cb(e, t, n) {
  const i = t.get(e.activityVersionId);
  return n.get(Nn(i?.activityTypeKey)) ?? n.get(Nn(_t(i?.activityTypeKey))) ?? n.get(Nn(i?.displayName)) ?? n.get(Nn(e.activityVersionId)) ?? null;
}
function vi(e, t, n) {
  const i = Nn(t);
  i && !e.has(i) && e.set(i, n);
}
function Nn(e) {
  return e?.trim().toLowerCase() ?? "";
}
function mc(e, t, n, i) {
  const r = fr();
  if (!r) return t;
  const s = r.getItem(e);
  if (s == null) return t;
  const a = Number(s);
  return Number.isFinite(a) ? _i(a, n, i) : t;
}
function xc(e, t) {
  const n = fr();
  if (!n) return t;
  const i = n.getItem(e);
  return i === "true" ? !0 : i === "false" ? !1 : t;
}
function lb() {
  const e = fr();
  if (!e) return null;
  const t = e.getItem(hd);
  return t === "palette" || t === "inspector" ? t : null;
}
function fr() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function yn(e, t) {
  const n = fr();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function _i(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function Nd(e) {
  switch (ub(e)) {
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
function ub(e) {
  return (e ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}
function db(e, t) {
  const n = e.state.rootActivity;
  if (!n) return /* @__PURE__ */ new Set();
  const i = t.find((s) => s.activityVersionId === n.activityVersionId);
  if (Yo(n, i) === "unsupported") return /* @__PURE__ */ new Set([n.nodeId]);
  const r = Gt(n, [], t);
  return new Set(r?.slot.activities.map((s) => s.nodeId) ?? [n.nodeId]);
}
function wc(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function vc(e) {
  return `${Se(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function bc(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function fb(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function jd(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function pb(e) {
  const t = jd(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function hb(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function Le(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function gb(e) {
  return Ed(Le(e));
}
function yb(e, t) {
  const n = t?.get(e.activityVersionId);
  return n ? Se(n) : _t(e.activityVersionId) ?? e.activityVersionId;
}
function mb(e, t) {
  if (e.cardinality === "single") {
    const i = e.activities[0];
    return i ? yb(i, t) : "Empty — click to choose";
  }
  const n = e.activities.length;
  return `${n} activit${n === 1 ? "y" : "ies"}`;
}
function Sd(e, t, n = []) {
  if (!e) return n;
  const i = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: i?.activityTypeKey ?? e.activityVersionId,
    displayName: i ? Se(i) : void 0
  });
  for (const r of Me(e, t))
    for (const s of r.activities) Sd(s, t, n);
  return n;
}
function Cd(e, t, n = []) {
  if (!e) return n;
  for (const i of ll(e))
    n.push({ source: i.source, target: i.target, sourcePort: i.sourceHandle ?? void 0, targetPort: i.targetHandle ?? void 0 });
  for (const i of Me(e, t))
    for (const r of i.activities) Cd(r, t, n);
  return n;
}
function jn(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function xb(e) {
  return `${e.id}-${Ed(JSON.stringify(e.state))}`;
}
function Ed(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function xs(e) {
  return e.status.toLowerCase() === "rejected";
}
function wb(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function vb(e, t) {
  const n = e instanceof Error ? e.message : String(e);
  return bb(e, n) ? `Run ${t} was not found.` : n;
}
function bb(e, t) {
  if ((typeof e == "object" && e ? e.response?.status ?? e.status : void 0) === 404 || /\b404\b/.test(t)) return !0;
  try {
    const i = JSON.parse(t);
    return [i.error, i.title, i.detail].some((r) => typeof r == "string" && /not found/i.test(r));
  } catch {
    return /not found/i.test(t);
  }
}
function un({ status: e, subStatus: t }) {
  return /* @__PURE__ */ o.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
const kd = { workflowActivity: Nb }, Id = { workflow: Sb };
function Nb({ data: e, selected: t }) {
  const n = e, i = n.runtime, r = !n.suppressFlowPorts, s = r ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], a = jb(n), u = Qe.useContext(yd)?.({ activityVersionId: n.activityVersionId, activityTypeKey: n.activityTypeKey }) ?? null;
  return /* @__PURE__ */ o.jsxs(
    "div",
    {
      className: ["wf-node", t ? "selected" : "", i ? "wf-node-runtime" : "", i?.hasBlockingIncident ? "faulted" : "", u ? "wf-node-unavailable" : ""].filter(Boolean).join(" "),
      "data-icon": n.icon ?? "activity",
      children: [
        r && n.acceptsInbound ? /* @__PURE__ */ o.jsx(sn, { type: "target", position: ie.Left }) : null,
        u ? /* @__PURE__ */ o.jsx("span", { className: "wf-node-availability", title: `No longer available for new use · ${zi(u.state)}`, children: /* @__PURE__ */ o.jsx(Ti, { size: 13 }) }) : null,
        /* @__PURE__ */ o.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ o.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: ys(n.icon) }),
          /* @__PURE__ */ o.jsxs("span", { className: "wf-node-copy", children: [
            /* @__PURE__ */ o.jsx("strong", { children: n.label }),
            a ? /* @__PURE__ */ o.jsx("small", { children: a }) : null
          ] })
        ] }),
        n.childSlots.length > 0 ? /* @__PURE__ */ o.jsxs("span", { className: "wf-node-slot-badge", children: [
          n.childSlots.length,
          " slot",
          n.childSlots.length === 1 ? "" : "s"
        ] }) : null,
        i ? /* @__PURE__ */ o.jsxs("div", { className: "wf-node-runtime-strip", children: [
          i.status ? /* @__PURE__ */ o.jsx(un, { status: i.status, subStatus: i.subStatus }) : null,
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
          return /* @__PURE__ */ o.jsxs(Qe.Fragment, { children: [
            /* @__PURE__ */ o.jsx("span", { className: "wf-node-port-label", style: { top: f }, children: l.displayName }),
            /* @__PURE__ */ o.jsx(sn, { type: "source", position: ie.Right, id: l.name, style: { top: f } })
          ] }, l.name);
        })
      ]
    }
  );
}
function jb(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((r) => !!r).join(" · ");
}
function Sb(e) {
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
  } = e, p = Qe.useContext(gd), [h, y] = B(!1), [x, m, w] = qi({ sourceX: n, sourceY: i, targetX: r, targetY: s, sourcePosition: a, targetPosition: c }), b = p?.highlightedEdgeId === t;
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsx(
      Zn,
      {
        id: t,
        path: x,
        markerEnd: u,
        style: {
          ...l,
          strokeWidth: b ? 2.5 : l?.strokeWidth
        },
        label: d,
        labelX: m,
        labelY: w,
        labelStyle: f,
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1)
      }
    ),
    p ? /* @__PURE__ */ o.jsx(Xv, { children: /* @__PURE__ */ o.jsxs(
      "div",
      {
        className: ["wf-edge-actions", h ? "visible" : "", b ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${m}px, ${w}px)` },
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1),
        children: [
          /* @__PURE__ */ o.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (g) => p.requestInsertActivity(t, g.clientX, g.clientY), children: /* @__PURE__ */ o.jsx(Zt, { size: 12 }) }),
          /* @__PURE__ */ o.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => p.deleteEdge(t), children: /* @__PURE__ */ o.jsx(In, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function Cb({ clientX: e, clientY: t, activities: n, onPick: i, onClose: r }) {
  const [s, a] = B(""), [c, u] = B(0), l = oe(null), d = oe(null), f = de(() => {
    const b = s.trim().toLowerCase(), g = n.filter(tb);
    return b ? g.filter((v) => Se(v).toLowerCase().includes(b) || v.activityTypeKey.toLowerCase().includes(b) || (v.category ?? "").toLowerCase().includes(b) || (v.description ?? "").toLowerCase().includes(b)) : g;
  }, [n, s]), p = de(() => Po(f), [f]), h = de(() => p.flatMap((b) => b.activities), [p]);
  J(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), J(() => {
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
  }, x = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), m = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let w = -1;
  return /* @__PURE__ */ o.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: x, top: m }, onMouseDown: (b) => b.stopPropagation(), onClick: (b) => b.stopPropagation(), children: [
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
        w += 1;
        const v = w, j = v === c;
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
              /* @__PURE__ */ o.jsx("strong", { children: Se(g) }),
              /* @__PURE__ */ o.jsx("small", { children: g.category || g.activityTypeKey })
            ]
          },
          g.activityVersionId
        );
      })
    ] }, b.category)) })
  ] });
}
function Di({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: i
}) {
  const r = df(t.map((s) => s.id), n, i);
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
function Nc(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
const Eb = "Expressions/UnresolvedVariable";
function kb(e) {
  return String(e.type ?? e.code ?? "");
}
function Ib(e) {
  return kb(e) === Eb;
}
function Ab(e) {
  const t = String(e ?? "").trim().split("/").filter(Boolean), [n, i, ...r] = t, s = n === "$workflow";
  return {
    nodeId: !n || s ? null : n,
    isWorkflowScope: s,
    bag: i === "inputs" || i === "outputs" || i === "variables" ? i : null,
    referenceKey: r.length > 0 ? r.join("/") : null
  };
}
function _b(e) {
  return (e ?? []).filter(Ib).map((t) => ({
    error: t,
    path: Ab(t.path),
    message: t.message ?? "Variable reference is not visible from this activity's scope."
  }));
}
function Db({ draft: e, onRepair: t }) {
  const n = e.validationErrors;
  if (!n.length)
    return /* @__PURE__ */ o.jsxs("div", { className: "wf-validation ok", children: [
      /* @__PURE__ */ o.jsx(an, { size: 14 }),
      " No validation errors"
    ] });
  const i = _b(n), r = new Map(i.map((s) => [s.error, s]));
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-validation-summary", children: [
      /* @__PURE__ */ o.jsx(yt, { size: 14 }),
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
          /* @__PURE__ */ o.jsx(ef, { size: 12 }),
          " Repair"
        ] }) : null
      ] }, a);
    }) })
  ] });
}
function Tb({
  testRun: e,
  onOpenDetails: t
}) {
  const n = xs(e);
  return /* @__PURE__ */ o.jsx("div", { className: "wf-test-run-status", "data-state": n ? "rejected" : "accepted", children: /* @__PURE__ */ o.jsxs(
    "button",
    {
      type: "button",
      className: "wf-test-run-trigger",
      onClick: t,
      children: [
        n ? /* @__PURE__ */ o.jsx(yt, { size: 16 }) : /* @__PURE__ */ o.jsx(an, { size: 16 }),
        n ? "Test run rejected" : "Test run dispatched"
      ]
    }
  ) });
}
function $b({ testRun: e, onOpenRun: t }) {
  if (!e)
    return /* @__PURE__ */ o.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Run the draft to see Runtime Evidence." }) });
  const n = xs(e), i = e.workflowExecutionId;
  return /* @__PURE__ */ o.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ o.jsxs("section", { className: "wf-runtime-card", "data-state": n ? "rejected" : "accepted", children: [
    /* @__PURE__ */ o.jsxs("header", { children: [
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("span", { children: "Latest Test Run" }),
        /* @__PURE__ */ o.jsx("h3", { children: n ? "Rejected by the server" : "Transient run accepted" })
      ] }),
      /* @__PURE__ */ o.jsx(un, { status: e.status, subStatus: e.commandDispatchStatus ?? void 0 })
    ] }),
    /* @__PURE__ */ o.jsx("p", { children: "Ephemeral - not saved, promoted, or published." }),
    n && e.reason ? /* @__PURE__ */ o.jsxs("div", { className: "wf-runtime-reason", children: [
      /* @__PURE__ */ o.jsx(yt, { size: 14 }),
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
        /* @__PURE__ */ o.jsx("dd", { children: jc(e.activityCount, "activity") })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ o.jsx("dd", { children: jc(e.incidentCount, "incident") })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ o.jsx("dd", { title: e.expiresAt ? Ve(e.expiresAt) : "None", children: e.expiresAt ? Ve(e.expiresAt) : "None" })
      ] })
    ] })
  ] }) });
}
function jc(e, t) {
  return typeof e != "number" ? "Available on linked Run" : `${e} ${t}${e === 1 ? "" : "s"}`;
}
function ws({ rows: e = 5 }) {
  return /* @__PURE__ */ o.jsx("div", { className: "wf-grid", "aria-busy": "true", "aria-label": "Loading", children: Array.from({ length: e }).map((t, n) => /* @__PURE__ */ o.jsx("div", { className: "wf-skeleton wf-skeleton-row", style: { width: `${90 - n % 3 * 12}%` } }, n)) });
}
function vs({ icon: e, title: t, description: n, action: i }) {
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-empty-state", role: "status", children: [
    /* @__PURE__ */ o.jsx("div", { className: "wf-empty-state-icon", "aria-hidden": !0, children: e ?? /* @__PURE__ */ o.jsx(Hn, { size: 22 }) }),
    /* @__PURE__ */ o.jsx("h3", { children: t }),
    n ? /* @__PURE__ */ o.jsx("p", { children: n }) : null,
    i ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty-state-action", children: i }) : null
  ] });
}
function Gn({ message: e, title: t = "Something went wrong" }) {
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-error-card", role: "alert", children: [
    /* @__PURE__ */ o.jsx(yt, { size: 18 }),
    /* @__PURE__ */ o.jsxs("div", { className: "wf-error-card-body", children: [
      /* @__PURE__ */ o.jsx("strong", { children: t }),
      /* @__PURE__ */ o.jsx("span", { children: e || "Please try again, or check that the Elsa server is reachable." })
    ] })
  ] });
}
function Ad({ status: e, run: t, compact: n = !1 }) {
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
function qt({ value: e, ariaLabel: t, copiedLabel: n, onCopied: i, onCopyFailed: r }) {
  if (!e) return null;
  const s = async (a) => {
    a.preventDefault(), a.stopPropagation();
    try {
      await ob(e), i(n);
    } catch {
      r(n);
    }
  };
  return /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (a) => {
    s(a);
  }, children: /* @__PURE__ */ o.jsx(tf, { size: 12 }) });
}
function Pb({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: i }) {
  const [r, s] = B("loading"), [a, c] = B(""), [u, l] = B(""), [d, f] = B(null), [p, h] = B([]), y = n?.trim().toLowerCase() ?? "", x = de(
    () => y ? p.filter((S) => nb(S, y)) : p,
    [y, p]
  ), m = de(
    () => Array.from(new Set(p.flatMap((S) => [
      S.definitionId,
      S.definitionVersionId,
      S.sourceId
    ]).filter((S) => !!S))).sort((S, I) => S.localeCompare(I)),
    [p]
  ), w = Dt(t, "weaver.workflows.explain-executable"), b = re(async () => {
    s("loading"), c("");
    try {
      h(await gl(e)), s("ready");
    } catch (S) {
      c(S instanceof Error ? S.message : String(S)), s("failed");
    }
  }, [e]);
  J(() => {
    b();
  }, [b]);
  const g = async (S) => {
    l(""), f(null), c("");
    try {
      const I = await hl(e, S.artifactId), _ = bd(I);
      f({ artifactId: S.artifactId, workflowExecutionId: _ }), l(`Started ${S.artifactId}`);
    } catch (I) {
      c(I instanceof Error ? I.message : String(I));
    }
  }, v = (S) => {
    w && Tt(t, w, S) && (c(""), f(null), l(`Sent ${S.artifactId} to Weaver`));
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
        /* @__PURE__ */ o.jsx(Zi, { size: 14 }),
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
      /* @__PURE__ */ o.jsx("datalist", { id: "wf-executable-definition-options", children: m.map((S) => /* @__PURE__ */ o.jsx("option", { value: S }, S)) }),
      n ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => i(null), children: [
        /* @__PURE__ */ o.jsx(Hc, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    r === "failed" ? /* @__PURE__ */ o.jsx(Gn, { message: a }) : null,
    u ? /* @__PURE__ */ o.jsx(Ad, { status: u, run: d }) : null,
    r === "loading" ? /* @__PURE__ */ o.jsx(ws, {}) : null,
    r === "ready" && x.length === 0 ? /* @__PURE__ */ o.jsx(
      vs,
      {
        icon: /* @__PURE__ */ o.jsx(Ut, { size: 22 }),
        title: "No workflow executables",
        description: n ? "No executables match this definition filter." : "Publish a workflow definition to make it executable."
      }
    ) : null,
    r === "ready" && x.length > 0 ? /* @__PURE__ */ o.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ o.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ o.jsx("span", { children: "Version" }),
        /* @__PURE__ */ o.jsx("span", { children: "Source" }),
        /* @__PURE__ */ o.jsx("span", { children: "Root" }),
        /* @__PURE__ */ o.jsx("span", { children: "Published" }),
        /* @__PURE__ */ o.jsx("span", { children: "Actions" })
      ] }),
      x.map((S) => /* @__PURE__ */ o.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ o.jsxs("span", { className: "wf-artifact-cell", children: [
          /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line", children: [
            /* @__PURE__ */ o.jsx("strong", { title: S.artifactId, children: S.artifactId }),
            /* @__PURE__ */ o.jsx(qt, { value: S.artifactId, ariaLabel: `Copy artifact ID ${S.artifactId}`, copiedLabel: "artifact ID", onCopied: j, onCopyFailed: N })
          ] }),
          /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
            /* @__PURE__ */ o.jsx("small", { title: S.artifactHash, children: S.artifactHash }),
            /* @__PURE__ */ o.jsx(qt, { value: S.artifactHash, ariaLabel: `Copy artifact hash ${S.artifactHash}`, copiedLabel: "artifact hash", onCopied: j, onCopyFailed: N })
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line wf-version-cell", children: [
          /* @__PURE__ */ o.jsx("span", { children: S.artifactVersion }),
          /* @__PURE__ */ o.jsx(qt, { value: S.artifactVersion, ariaLabel: `Copy artifact version ${S.artifactVersion}`, copiedLabel: "artifact version", onCopied: j, onCopyFailed: N })
        ] }),
        /* @__PURE__ */ o.jsx(Mb, { executable: S, onCopied: j, onCopyFailed: N }),
        /* @__PURE__ */ o.jsx("span", { children: wd(S) }),
        /* @__PURE__ */ o.jsx("span", { children: Ve(S.publishedAt ?? S.createdAt) }),
        /* @__PURE__ */ o.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
            g(S);
          }, children: [
            /* @__PURE__ */ o.jsx(Ut, { size: 13 }),
            " Run"
          ] }),
          w ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => v(S), children: [
            /* @__PURE__ */ o.jsx(at, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, S.artifactId))
    ] }) : null
  ] });
}
function Mb({ executable: e, onCopied: t, onCopyFailed: n }) {
  const i = e.sourceId || e.definitionVersionId || e.definitionId, r = e.sourceVersion;
  return /* @__PURE__ */ o.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ o.jsx("span", { className: "wf-source-kind", children: vd(e.sourceKind) }),
    i ? /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line", children: [
      /* @__PURE__ */ o.jsx("code", { title: i, children: i }),
      /* @__PURE__ */ o.jsx(qt, { value: i, ariaLabel: `Copy source ID ${i}`, copiedLabel: "source ID", onCopied: t, onCopyFailed: n })
    ] }) : null,
    r ? /* @__PURE__ */ o.jsxs("small", { children: [
      "Version ",
      r
    ] }) : null
  ] });
}
function Rb({ context: e, ai: t, definitionId: n, publishedArtifactId: i }) {
  const [r, s] = B("loading"), [a, c] = B(""), [u, l] = B(""), [d, f] = B(null), [p, h] = B([]), y = Dt(t, "weaver.workflows.explain-executable"), x = re(async () => {
    s("loading"), c("");
    try {
      const j = await gl(e);
      h(j.filter((N) => ib(N, n)).sort(rb)), s("ready");
    } catch (j) {
      c(j instanceof Error ? j.message : String(j)), h([]), s("failed");
    }
  }, [e, n]);
  J(() => {
    x();
  }, [x, i]);
  const m = async (j) => {
    l(""), f(null), c("");
    try {
      const N = await hl(e, j.artifactId);
      f({ artifactId: j.artifactId, workflowExecutionId: bd(N) }), l(`Started ${j.artifactId}`);
    } catch (N) {
      c(N instanceof Error ? N.message : String(N));
    }
  }, w = (j) => {
    y && Tt(t, y, j) && (c(""), f(null), l(`Sent ${j.artifactId} to Weaver`));
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
        x();
      }, children: [
        /* @__PURE__ */ o.jsx(Bo, { size: 13 }),
        " Refresh"
      ] }),
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: b, children: "Open list" })
    ] }),
    r === "failed" ? /* @__PURE__ */ o.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ o.jsx(yt, { size: 14 }),
      " ",
      a
    ] }) : null,
    u ? /* @__PURE__ */ o.jsx(Ad, { status: u, run: d, compact: !0 }) : null,
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
        /* @__PURE__ */ o.jsx("span", { children: Ve(j.publishedAt ?? j.createdAt) })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { className: "wf-artifact-card-values", children: [
        /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line", children: [
          /* @__PURE__ */ o.jsx("code", { title: j.artifactId, children: j.artifactId }),
          /* @__PURE__ */ o.jsx(qt, { value: j.artifactId, ariaLabel: `Copy artifact ID ${j.artifactId}`, copiedLabel: "artifact ID", onCopied: g, onCopyFailed: v })
        ] }),
        /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ o.jsx("code", { title: j.artifactHash, children: j.artifactHash }),
          /* @__PURE__ */ o.jsx(qt, { value: j.artifactHash, ariaLabel: `Copy artifact hash ${j.artifactHash}`, copiedLabel: "artifact hash", onCopied: g, onCopyFailed: v })
        ] })
      ] }),
      /* @__PURE__ */ o.jsxs("dl", { children: [
        /* @__PURE__ */ o.jsxs("div", { children: [
          /* @__PURE__ */ o.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ o.jsxs("dd", { children: [
            vd(j.sourceKind),
            " ",
            j.sourceVersion ? `v${j.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("div", { children: [
          /* @__PURE__ */ o.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ o.jsx("dd", { children: wd(j) })
        ] })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
          m(j);
        }, children: [
          /* @__PURE__ */ o.jsx(Ut, { size: 13 }),
          " Run"
        ] }),
        y ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => w(j), children: [
          /* @__PURE__ */ o.jsx(at, { size: 13 }),
          " Explain"
        ] }) : null
      ] })
    ] }, j.artifactId)) }) : null
  ] });
}
function _d() {
  const [e, t] = B(() => mc(lc, Y0, vn, bn)), [n, i] = B(() => mc(uc, q0, Bt, Kt)), [r, s] = B(() => xc(dc, !1)), [a, c] = B(() => xc(fc, !1)), [u, l] = B(lb);
  J(() => {
    yn(lc, String(e));
  }, [e]), J(() => {
    yn(uc, String(n));
  }, [n]), J(() => {
    yn(dc, String(r));
  }, [r]), J(() => {
    yn(fc, String(a));
  }, [a]), J(() => {
    yn(hd, u);
  }, [u]), J(() => {
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
    l(null), g === "palette" ? (s(!1), t((j) => _i(j + v, vn, bn))) : (c(!1), i((j) => _i(j + v, Bt, Kt)));
  }, []), h = re((g, v) => {
    v.preventDefault(), l(null), g === "palette" ? s(!1) : c(!1);
    const j = v.clientX, N = g === "palette" ? e : n, S = g === "palette" ? vn : Bt, I = g === "palette" ? bn : Kt;
    document.body.classList.add("wf-side-panel-resizing");
    const _ = (D) => {
      const k = g === "palette" ? D.clientX - j : j - D.clientX, R = _i(N + k, S, I);
      g === "palette" ? t(R) : i(R);
    }, M = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", _), window.removeEventListener("pointerup", M), window.removeEventListener("pointercancel", M);
    };
    window.addEventListener("pointermove", _), window.addEventListener("pointerup", M), window.addEventListener("pointercancel", M);
  }, [n, e]), y = re((g, v) => {
    v.key === "ArrowLeft" ? (v.preventDefault(), p(g, g === "palette" ? -wi : wi)) : v.key === "ArrowRight" ? (v.preventDefault(), p(g, g === "palette" ? wi : -wi)) : v.key === "Home" ? (v.preventDefault(), g === "palette" ? t(vn) : i(Bt)) : v.key === "End" && (v.preventDefault(), g === "palette" ? t(bn) : i(Kt));
  }, [p]), x = !r && u !== "inspector", m = !a && u !== "palette", w = [
    "wf-editor-body",
    r ? "palette-collapsed" : "",
    a ? "inspector-collapsed" : "",
    u === "palette" ? "palette-maximized" : "",
    u === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), b = {
    "--wf-palette-width": `${r ? pc : e}px`,
    "--wf-inspector-width": `${a ? pc : n}px`
  };
  return {
    paletteWidth: e,
    inspectorWidth: n,
    paletteCollapsed: r,
    inspectorCollapsed: a,
    maximizedSidePanel: u,
    setInspectorCollapsed: c,
    paletteExpanded: x,
    inspectorExpanded: m,
    editorBodyClassName: w,
    editorBodyStyle: b,
    toggleSidePanelCollapsed: d,
    toggleSidePanelMaximized: f,
    startSidePanelResize: h,
    handleSidePanelResizeKeyDown: y
  };
}
const zb = 50;
function Sc() {
  return { past: [], future: [] };
}
function Lb(e) {
  return e.past.length > 0;
}
function Vb(e) {
  return e.future.length > 0;
}
function Cc(e, t, n = zb) {
  const i = [...e.past, t];
  return i.length > n && i.splice(0, i.length - n), { past: i, future: [] };
}
function Ob(e, t) {
  if (e.past.length === 0) return null;
  const n = e.past.slice(), i = n.pop();
  return { history: { past: n, future: [...e.future, t] }, snapshot: i };
}
function Hb(e, t) {
  if (e.future.length === 0) return null;
  const n = e.future.slice(), i = n.pop();
  return { history: { past: [...e.past, t], future: n }, snapshot: i };
}
function Wb({ draft: e, restoreDraft: t }) {
  const n = oe(Sc()), i = oe(null), r = oe(""), s = oe(!1), [a, c] = B(0), u = re((x) => {
    n.current = Sc(), i.current = x ? jn(x) : null, r.current = x ? Le(x) : "", s.current = !1, c(0);
  }, []);
  J(() => {
    if (!e) return;
    if (s.current) {
      s.current = !1;
      return;
    }
    const x = Le(e);
    if (x === r.current) return;
    const m = window.setTimeout(() => {
      const w = i.current;
      w && (n.current = Cc(n.current, w), c((b) => b + 1)), i.current = jn(e), r.current = x;
    }, B0);
    return () => window.clearTimeout(m);
  }, [e]);
  const l = re(() => {
    if (!e) return;
    const x = Le(e);
    if (x === r.current) return;
    const m = i.current;
    m && (n.current = Cc(n.current, m)), i.current = jn(e), r.current = x;
  }, [e]), d = re((x) => {
    s.current = !0, i.current = jn(x), r.current = Le(x), t(x), c((m) => m + 1);
  }, [t]), f = re(() => {
    if (!e) return;
    l();
    const x = Ob(n.current, e);
    x && (n.current = x.history, d(x.snapshot));
  }, [e, l, d]), p = re(() => {
    if (!e) return;
    l();
    const x = Hb(n.current, e);
    x && (n.current = x.history, d(x.snapshot));
  }, [e, l, d]), { canUndoNow: h, canRedoNow: y } = de(() => {
    const x = !!e && !!i.current && Le(e) !== r.current;
    return {
      canUndoNow: Lb(n.current) || x,
      canRedoNow: Vb(n.current) && !x
    };
  }, [e, a]);
  return { resetHistory: u, undo: f, redo: p, canUndoNow: h, canRedoNow: y };
}
const Fb = {
  draft: null,
  frames: [],
  selectedNodeId: null,
  testRun: null,
  publishedArtifactId: null
};
function Bb(e, t) {
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
function Kb() {
  const [e, t] = Zd(Bb, Fb), n = de(() => ({
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
const Xb = 320, Yb = 140;
function qb(e, t, n) {
  return n === "sequence" ? Ub(e) : Zb(e, t);
}
function Ub(e) {
  const t = /* @__PURE__ */ new Map();
  return e.forEach((n, i) => {
    t.set(n.id, { x: i * 280, y: 0 });
  }), t;
}
function Zb(e, t) {
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
      n.set(p, { x: d * Xb, y: h * Yb });
    });
  return n;
}
function Gb(e, t, n, i, r) {
  if (!e) return { kind: "becomeRoot" };
  const s = Gt(e, t, r);
  if (!s)
    return t.length > 0 ? { kind: "staleFrames" } : Me(n, i)[0] ? { kind: "wrapRoot" } : { kind: "leafError" };
  const a = s.slot.cardinality === "single" ? s.slot.activities.find((c) => c.nodeId !== n.nodeId) ?? null : null;
  return { kind: "addToSlot", slot: s.slot, replacedActivity: a };
}
function Jb({
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
  setError: x
}) {
  const [m, w] = B([]), [b, g] = B([]), [v, j] = B(null), [N, S] = B(null), [I, _] = B(null), M = oe(null), D = oe(null), k = oe(null), R = oe(null), C = oe(!1);
  J(() => {
    if (!n) {
      w([]), g([]);
      return;
    }
    const L = a ? wo(n, r, e?.layout ?? []) : t ? rl(t, r, e?.layout ?? []) : { nodes: [], edges: [] };
    w(L.nodes), g(L.edges);
  }, [r, e?.layout, a, t, n]);
  const A = re((L, V, K) => K ? [
    ...L.filter((Y) => Y.nodeId !== V),
    { nodeId: V, x: Math.round(K.x), y: Math.round(K.y) }
  ] : L, []), E = re((L, V) => {
    if (e?.state.rootActivity && a)
      return;
    const K = vo(L, vc(L)), Y = Gb(e?.state.rootActivity, i, K, L, s);
    if (Y.kind === "becomeRoot") {
      f(
        ({ draft: se }) => se ? { ...se, state: { ...se.state, rootActivity: K } } : null,
        K.nodeId
      );
      return;
    }
    if (Y.kind === "leafError") {
      y(""), x("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
      return;
    }
    if (Y.kind === "staleFrames") {
      y(""), x("This slot could not be resolved — returning to the workflow root."), h();
      return;
    }
    if (Y.kind === "wrapRoot") {
      f(({ draft: se }) => {
        const xe = se?.state.rootActivity;
        return xe ? {
          ...se,
          layout: A(se.layout, xe.nodeId, V),
          state: { ...se.state, rootActivity: Os(K, [], [xe], L) }
        } : null;
      }, e?.state.rootActivity?.nodeId ?? null), x(""), y(`Wrapped root in ${Se(L)}`);
      return;
    }
    f(({ draft: se, frames: xe }) => {
      if (!se?.state.rootActivity) return null;
      const ge = Gt(se.state.rootActivity, xe, s);
      if (!ge) return null;
      const Ee = ge.slot.cardinality === "single" ? [K] : [...ge.slot.activities, K], Ie = Os(se.state.rootActivity, xe, Ee, s);
      return {
        ...se,
        layout: A(se.layout, K.nodeId, V),
        state: { ...se.state, rootActivity: Ie }
      };
    }, K.nodeId), Y.replacedActivity && (x(""), y(`Replaced ${Y.slot.label} content`));
  }, [s, e?.state.rootActivity, i, a, f, h, A, x, y]), T = re((L, V) => {
    const K = vo(L, vc(L)), Y = {
      id: K.nodeId,
      type: "workflowActivity",
      position: V,
      selected: !0,
      data: {
        label: Se(L),
        activityVersionId: L.activityVersionId,
        activityTypeKey: L.activityTypeKey,
        category: L.category,
        executionType: L.executionType,
        icon: Qi(L),
        childSlots: Me(K, L),
        acceptsInbound: String(L.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: ul(K, L)
      }
    };
    return { activityNode: K, node: Y };
  }, []), $ = re((L, V, K = []) => {
    a || d(({ draft: Y, frames: se }) => {
      if (!Y) return null;
      const xe = ep(Y.layout, L), ge = Y.state.rootActivity;
      if (!ge) return { ...Y, layout: xe };
      const Ee = Gt(ge, se, s);
      if (!Ee) return { ...Y, layout: xe };
      const Ie = Jf(Ee, L, V, K), Be = Ee.slot.mode === "flowchart" ? Qf(Ie, V) : Ie;
      return {
        ...Y,
        layout: xe,
        state: {
          ...Y.state,
          rootActivity: Gf(ge, se, Be, s)
        }
      };
    });
  }, [s, a, d]), P = re((L, V) => {
    if (!M.current) return null;
    const K = M.current.getBoundingClientRect();
    return v ? v.screenToFlowPosition({ x: L, y: V }) : {
      x: L - K.left,
      y: V - K.top
    };
  }, [v]), F = re((L, V) => document.elementFromPoint(L, V)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), W = re((L, V, K) => {
    const Y = m.find((ye) => ye.id === V.source), se = m.find((ye) => ye.id === V.target), xe = Y && se ? fb(Y, se) : Y ? bc(Y) : K, ge = T(L, xe), Ie = [...m.map((ye) => ye.selected ? { ...ye, selected: !1 } : ye), ge.node], Be = cp(b, V, ge.node.id);
    w(Ie), g(Be), p(ge.node.id), $(Ie, Be, [ge.activityNode]);
  }, [$, T, b, m, p]), H = re((L, V, K) => {
    if (!u || !M.current) return !1;
    const Y = M.current.getBoundingClientRect();
    if (!(V >= Y.left && V <= Y.right && K >= Y.top && K <= Y.bottom)) return !1;
    const xe = P(V, K);
    if (!xe) return !1;
    if (c) {
      const ge = F(V, K), Ee = ge ? b.find((Ie) => Ie.id === ge) : void 0;
      if (Ee)
        return W(L, Ee, xe), !0;
    }
    return E(L, xe), !0;
  }, [E, u, b, F, c, W, P]);
  J(() => {
    const L = (K) => {
      const Y = k.current;
      if (!Y) return;
      Math.hypot(K.clientX - Y.startX, K.clientY - Y.startY) >= W0 && (Y.dragging = !0);
    }, V = (K) => {
      const Y = k.current;
      if (k.current = null, !Y?.dragging || !M.current || R.current) return;
      const se = M.current.getBoundingClientRect();
      K.clientX >= se.left && K.clientX <= se.right && K.clientY >= se.top && K.clientY <= se.bottom && (C.current = !0, window.setTimeout(() => {
        C.current = !1;
      }, 0), H(Y.activity, K.clientX, K.clientY));
    };
    return window.addEventListener("pointermove", L), window.addEventListener("pointerup", V), window.addEventListener("pointercancel", V), () => {
      window.removeEventListener("pointermove", L), window.removeEventListener("pointerup", V), window.removeEventListener("pointercancel", V);
    };
  }, [v, H]);
  const q = (L, V) => {
    R.current = { activityVersionId: V.activityVersionId, handledDrop: !1 }, L.dataTransfer.setData(cc, V.activityVersionId), L.dataTransfer.setData("text/plain", V.activityVersionId), L.dataTransfer.effectAllowed = "copy";
  }, Z = (L, V) => {
    const K = R.current;
    R.current = null, !K?.handledDrop && (L.clientX === 0 && L.clientY === 0 || H(V, L.clientX, L.clientY) && (C.current = !0, window.setTimeout(() => {
      C.current = !1;
    }, 0)));
  }, ne = (L, V) => {
    L.button === 0 && (k.current = {
      activity: V,
      startX: L.clientX,
      startY: L.clientY,
      dragging: !1
    });
  }, le = (L) => {
    C.current || u && E(L);
  }, G = (L) => {
    if (!u) {
      L.dataTransfer.dropEffect = "none";
      return;
    }
    if (L.preventDefault(), L.dataTransfer.dropEffect = "copy", !c) return;
    const V = F(L.clientX, L.clientY);
    _(V);
  }, z = (L) => {
    if (!M.current) return;
    const V = L.relatedTarget;
    V && M.current.contains(V) || _(null);
  }, X = (L) => {
    L.preventDefault(), _(null);
    const V = L.dataTransfer.getData(cc) || L.dataTransfer.getData("text/plain");
    if (!V || (L.stopPropagation(), R.current?.activityVersionId === V && (R.current.handledDrop = !0), !u)) return;
    const K = s.get(V);
    K && H(K, L.clientX, L.clientY);
  }, ae = (L) => {
    if (!u) return;
    if (L) {
      S({ kind: "fromEmpty", clientX: L.clientX, clientY: L.clientY });
      return;
    }
    const V = M.current?.getBoundingClientRect();
    V && S({
      kind: "fromEmpty",
      clientX: V.left + V.width / 2,
      clientY: V.top + V.height / 2
    });
  }, ce = (L) => {
    const V = a ? L.filter((K) => K.type === "select") : L;
    V.length !== 0 && w((K) => Du(V, K));
  }, Q = (L) => {
    a || g((V) => Tu(L, V));
  }, te = (L) => !L.source || !L.target || L.source === L.target || !c ? !1 : !L.targetHandle, fe = (L) => {
    if (!e?.state.rootActivity || !t || !c || !te(L)) return;
    const V = Pi(L.source, L.target, L.sourceHandle ?? "Done", L.targetHandle ?? void 0), K = Pu(V, b);
    g(K), $(m, K);
  }, O = () => {
    $(m, b);
  }, ee = !a && m.length > 0, he = re(() => {
    if (a || m.length === 0) return;
    const L = t?.slot.mode === "sequence" ? "sequence" : "flowchart", V = qb(m, b, L), K = m.map((Y) => {
      const se = V.get(Y.id);
      return se ? { ...Y, position: se } : Y;
    });
    w(K), $(K, b), window.requestAnimationFrame(() => v?.fitView({ padding: 0.2 })), y("Rearranged the canvas.");
  }, [b, m, t, a, $, v, y]), me = (L, V) => {
    if (!V.nodeId || V.handleType === "target") {
      D.current = null;
      return;
    }
    D.current = {
      nodeId: V.nodeId,
      handleId: V.handleId ?? null
    };
  }, Te = (L, V) => {
    const K = hb(D.current, V);
    if (D.current = null, !K || !c || V.toNode || V.toHandle || pb(L)) return;
    const Y = jd(L);
    S({
      kind: "fromPort",
      sourceNodeId: K.nodeId,
      sourceHandleId: K.handleId,
      clientX: Y.x,
      clientY: Y.y
    });
  }, ke = (L, V) => {
    if (!c || !te(V)) return;
    const K = jw(L, {
      ...V,
      sourceHandle: V.sourceHandle ?? "Done",
      // Connection.targetHandle is string | null; null and undefined are indistinguishable downstream
      // (every consumer checks truthiness), so keep the type-correct null.
      targetHandle: V.targetHandle ?? null
    }, b, { shouldReplaceId: !1 });
    g(K), $(m, K);
  }, _e = (L) => {
    if (a || L.length === 0) return;
    const V = new Set(L.map((se) => se.id)), K = m.filter((se) => !V.has(se.id)), Y = b.filter((se) => !V.has(se.source) && !V.has(se.target));
    w(K), g(Y), l && V.has(l) && p(null), $(K, Y);
  }, it = (L) => {
    if (a || L.length === 0) return;
    const V = new Set(L.map((Y) => Y.id)), K = b.filter((Y) => !V.has(Y.id));
    g(K), $(m, K);
  }, We = re((L) => {
    if (a) return;
    const V = b.filter((K) => K.id !== L);
    g(V), $(m, V);
  }, [$, b, a, m]), Fe = re((L, V, K) => {
    c && S({ kind: "spliceEdge", edgeId: L, clientX: V, clientY: K });
  }, [c]), De = (L) => {
    const V = N;
    if (!V) return;
    S(null);
    const K = P(V.clientX, V.clientY) ?? { x: 0, y: 0 };
    if (V.kind === "fromEmpty") {
      const se = T(L, K), ge = [...m.map((Ee) => Ee.selected ? { ...Ee, selected: !1 } : Ee), se.node];
      w(ge), p(se.node.id), $(ge, b, [se.activityNode]);
      return;
    }
    if (V.kind === "fromPort") {
      const se = m.find((ye) => ye.id === V.sourceNodeId), xe = se ? bc(se) : K, ge = T(L, xe), Ie = [...m.map((ye) => ye.selected ? { ...ye, selected: !1 } : ye), ge.node], Be = [...b, Pi(V.sourceNodeId, ge.node.id, V.sourceHandleId ?? "Done")];
      w(Ie), g(Be), p(ge.node.id), $(Ie, Be, [ge.activityNode]);
      return;
    }
    const Y = b.find((se) => se.id === V.edgeId);
    Y && W(L, Y, K);
  }, Re = de(() => ({
    highlightedEdgeId: I,
    deleteEdge: We,
    requestInsertActivity: Fe
  }), [We, I, Fe]);
  return {
    nodes: m,
    edges: b,
    canvasRef: M,
    setReactFlowInstance: j,
    connectMenu: N,
    setConnectMenu: S,
    edgeActions: Re,
    onNodesChange: ce,
    onEdgesChange: Q,
    onNodesDelete: _e,
    onEdgesDelete: it,
    isValidConnection: te,
    onConnect: fe,
    onConnectStart: me,
    onConnectEnd: Te,
    onReconnect: ke,
    commitLayout: O,
    canAutoLayout: ee,
    autoLayout: he,
    onCanvasDragOver: G,
    onCanvasDragLeave: z,
    onCanvasDrop: X,
    openEmptyConnectMenu: ae,
    onConnectMenuPick: De,
    addActivity: E,
    onPaletteClick: le,
    onPaletteDragStart: q,
    onPaletteDragEnd: Z,
    onPalettePointerDown: ne
  };
}
const Qb = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function Dd(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function bs(e) {
  return Dd(e.name);
}
function eN(e, t) {
  const n = bs(t), i = e[n];
  return t.isWrapped === !1 ? i ?? t.defaultValue ?? "" : $d(i, t);
}
function Td(e, t) {
  return $d(e[bs(t)], t);
}
function tN(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function nN(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function Ec(e, t, n) {
  return {
    ...e,
    [bs(t)]: n
  };
}
function iN(e, t) {
  return t.isWrapped === !1 ? eN(e, t) : Td(e, t).expression.value;
}
function $d(e, t) {
  return dN(e) ? {
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
const rN = /* @__PURE__ */ new Set([
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
function oN(e) {
  const t = e?.trim();
  if (!t) return null;
  const n = /^([\w.+]+)\[\]/.exec(t);
  if (n) return { elementTypeName: n[1] };
  const i = t.indexOf("`");
  if (i < 0) return null;
  const r = t.slice(0, i), s = (r.split(".").pop() ?? r).toLowerCase();
  return rN.has(s) ? { elementTypeName: sN(t.slice(i)) } : null;
}
function sN(e) {
  const t = /\[\[([\w.+]+)/.exec(e);
  if (t) return t[1];
  const n = /\[([\w.+]+)/.exec(e);
  return n ? n[1] : null;
}
function aN(e) {
  const t = e.uiHint?.toLowerCase();
  return t === "json" || t === "code" ? !0 : e.uiSpecifications?.repeater === !1;
}
function cN(e) {
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
function lN(e) {
  const t = (e ?? "").toLowerCase();
  return t === "system.boolean" || t === "boolean" || t === "bool" ? !1 : "";
}
function uN(e, t) {
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
function dN(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
function Pd(e) {
  return Ns(e?.trim() ?? "") || e;
}
function Ns(e) {
  if (!e) return "";
  const t = fN(e);
  if (!t) return "";
  const n = /^(.*)((?:\[\])+)$/.exec(t);
  if (n) return `${Ns(n[1])}${n[2]}`;
  const i = t.indexOf("`");
  if (i >= 0) {
    const r = kc(t.slice(0, i)), s = pN(t.slice(i));
    return s.length > 0 ? `${r}<${s.join(", ")}>` : r;
  }
  return kc(t);
}
function fN(e) {
  let t = 0;
  for (let n = 0; n < e.length; n++) {
    const i = e[n];
    if (i === "[") t++;
    else if (i === "]") t--;
    else if (i === "," && t === 0) return e.slice(0, n).trim();
  }
  return e.trim();
}
function kc(e) {
  const t = e.split(".").filter(Boolean).at(-1) ?? e;
  return t.split("+").filter(Boolean).at(-1) ?? t;
}
function pN(e) {
  const t = e.indexOf("[");
  if (t < 0) return [];
  const n = Ic(e, t);
  return n == null ? [] : hN(n).map((i) => {
    const r = i.trim(), s = r.startsWith("[") ? Ic(r, 0) ?? r : r;
    return Ns(s);
  }).filter(Boolean);
}
function Ic(e, t) {
  let n = 0;
  for (let i = t; i < e.length; i++)
    if (e[i] === "[") n++;
    else if (e[i] === "]" && --n === 0) return e.slice(t + 1, i);
  return null;
}
function hN(e) {
  const t = [];
  let n = 0, i = 0;
  for (let r = 0; r < e.length; r++) {
    const s = e[r];
    s === "[" ? n++ : s === "]" ? n-- : s === "," && n === 0 && (t.push(e.slice(i, r)), i = r + 1);
  }
  return t.push(e.slice(i)), t.map((r) => r.trim()).filter(Boolean);
}
const Ac = "elsa-studio:apply-workflow-graph-operation-batch", _c = "elsa-studio:undo-workflow-graph-operation-batch", gN = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function yN(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const i = SN(e), r = Rd(i.state.rootActivity), s = /* @__PURE__ */ new Map(), a = /* @__PURE__ */ new Map(), c = [];
  for (const u of t.operations) {
    const l = jN(u.kind), d = u.parameters ?? {};
    if (l === "add-activity") {
      const f = $e(d.activityId) ?? u.temporaryReferences?.[0], p = NN(f ?? $e(d.displayName) ?? $e(d.activityType) ?? "weaver-activity", r), h = mN(u, p, n);
      a.set(p, h), c.push(p), f && s.set(f, p), i.state.rootActivity && xN(i.state.rootActivity, h);
      const y = gt(d.position) ? Mo(d.position, { x: 280, y: 160 }) : null;
      y && (i.layout = Dc(i.layout, p, y));
      continue;
    }
    if (l === "set-root") {
      const f = go(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown root activity.");
      i.state.rootActivity = f;
      continue;
    }
    if (l === "set-designer-position") {
      const f = $t(d.activityId, s);
      if (!f || !js(i.state.rootActivity, f)) throw new Error("Weaver batch referenced an unknown activity position.");
      i.layout = Dc(i.layout, f, Mo(d, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const f = go(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity property target.");
      bN(f, $e(d.propertyName) ?? "Value", d.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const f = go(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity update target.");
      const p = gt(d.patch) ? d.patch : d;
      Object.assign(f, p);
      continue;
    }
    if (l === "remove-activity") {
      const f = $t(d.activityId, s);
      if (!f) throw new Error("Weaver batch referenced an unknown activity remove target.");
      i.state.rootActivity = Md(i.state.rootActivity, f), i.layout = i.layout.filter((p) => p.nodeId !== f);
      continue;
    }
    if (l === "connect-activities") {
      wN(i, d, s);
      continue;
    }
    if (l === "disconnect-activities") {
      vN(i, d, s);
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
function mN(e, t, n) {
  const i = e.parameters ?? {}, r = $e(i.activityVersionId) ?? $e(i.activityType) ?? "Elsa.Workflows.Activity", s = n.find((a) => a.activityVersionId === r || a.activityTypeKey === r || a.displayName === $e(i.displayName));
  return s ? vo(s, t) : {
    nodeId: t,
    // catalogItem is provably undefined here (the truthy branch returned above), so use the id directly.
    activityVersionId: r,
    inputs: [],
    outputs: [],
    ...$e(i.displayName) ? { displayName: $e(i.displayName) } : {},
    designer: { position: Mo(i.position, { x: 280, y: 160 }) }
  };
}
function xN(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = Ss(e);
  n && !n.some((i) => i.nodeId === t.nodeId) && n.push(t);
}
function wN(e, t, n) {
  const i = e.state.rootActivity;
  if (!i?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const r = $t(t.sourceActivityId ?? t.sourceId ?? t.from, n), s = $t(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!r || !s) throw new Error("Weaver batch connection is missing source or target activity.");
  const a = i.structure.payload, c = Array.isArray(a.connections) ? a.connections : [], u = $e(t.connectionId) ?? `flow-${r}-${s}`;
  a.connections = [
    ...c.filter((l) => !gt(l) || l.id !== u),
    {
      id: u,
      source: { nodeId: r, port: $e(t.outcome) ?? $e(t.sourcePort) ?? "Done" },
      target: { nodeId: s }
    }
  ];
}
function vN(e, t, n) {
  const i = e.state.rootActivity, r = i?.structure?.payload.connections;
  if (!Array.isArray(r)) return;
  const s = $e(t.connectionId), a = $t(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = $t(t.targetActivityId ?? t.targetId ?? t.to, n);
  i.structure.payload.connections = r.filter((u) => {
    if (!gt(u)) return !0;
    if (s && u.id === s) return !1;
    const l = gt(u.source) ? u.source.nodeId : void 0, d = gt(u.target) ? u.target.nodeId : void 0;
    return l !== a || d !== c;
  });
}
function bN(e, t, n) {
  const i = gt(n);
  e[Dd(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: i ? "Object" : "Literal", value: n }
  };
}
function go(e, t, n, i) {
  const r = $t(t, n);
  return r ? js(e.state.rootActivity, r) ?? i.get(r) ?? null : null;
}
function $t(e, t) {
  const n = $e(e);
  return n ? t.get(n) ?? n : null;
}
function js(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of zd(e)) {
    const i = js(n, t);
    if (i) return i;
  }
  return null;
}
function Md(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = Ss(e);
  if (n) {
    const i = n.map((r) => Md(r, t)).filter((r) => !!r);
    n.splice(0, n.length, ...i);
  }
  return e;
}
function Rd(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of zd(e)) Rd(n, t);
  return t;
}
function zd(e) {
  return Ss(e) ?? [];
}
function Ss(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function Dc(e, t, n) {
  return [
    ...e.filter((i) => i.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function Mo(e, t) {
  const n = gt(e) ? e : {}, i = Number(n.x), r = Number(n.y);
  return {
    x: Number.isFinite(i) ? Math.max(40, Math.round(i)) : t.x,
    y: Number.isFinite(r) ? Math.max(40, Math.round(r)) : t.y
  };
}
function NN(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let i = n, r = 2;
  for (; t.has(i); )
    i = `${n}-${r}`, r += 1;
  return t.add(i), i;
}
function jN(e) {
  return typeof e == "number" ? gN[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function $e(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function SN(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function gt(e) {
  return typeof e == "object" && e !== null;
}
function CN({
  draft: e,
  details: t,
  catalog: n,
  replaceDraftByBatch: i,
  setStatus: r,
  setError: s
}) {
  const a = oe(/* @__PURE__ */ new Map());
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
        const p = jn(e), h = yN(e, d.batch, n), y = `weaver-batch-${Date.now()}`;
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
    return window.addEventListener(Ac, c), window.addEventListener(_c, u), () => {
      window.removeEventListener(Ac, c), window.removeEventListener(_c, u);
    };
  }, [n, t, e, i, r, s]);
}
function EN({ context: e, draft: t, autosaveEnabledByDefault: n = !0, editDraft: i, setStatus: r, setError: s }) {
  const [a, c] = B(n), u = oe(""), l = oe(0), d = oe(Promise.resolve()), f = re((h) => {
    u.current = h ? Le(h) : "";
  }, []), p = re(async (h, y) => {
    const x = async () => {
      const w = ++l.current, b = Le(h);
      s("");
      try {
        const g = await zp(e, h), v = Le(g);
        return u.current = v, i(({ draft: j }) => !j || j.id !== g.id ? null : Le(j) === b ? g : { ...j, validationErrors: g.validationErrors }), w === l.current && r(y), g;
      } catch (g) {
        throw w === l.current && (r(""), s(g instanceof Error ? g.message : String(g))), g;
      }
    }, m = d.current.then(x, x);
    return d.current = m.catch(() => {
    }), m;
  }, [e, i, r, s]);
  return J(() => {
    if (!a || !t || Le(t) === u.current) return;
    r("Autosaving...");
    const y = window.setTimeout(() => {
      p(t, "Autosaved").catch(() => {
      });
    }, F0);
    return () => window.clearTimeout(y);
  }, [a, t, p, r]), { saveDraft: p, autosaveEnabled: a, setAutosaveEnabled: c, markSaved: f };
}
function kN({ context: e, definitionId: t, resetHistory: n, loadDraft: i, markSaved: r, setError: s }) {
  const [a, c] = B(null), [u, l] = B([]), [d, f] = B([]), [p, h] = B(null), [y, x] = B(ji), [m, w] = B("loading"), b = re(async () => {
    s(""), w("loading");
    const [g, v, j, N, S] = await Promise.all([
      Ip(e, t),
      Uo(e),
      Yp(e).then(
        (_) => ({ ok: !0, descriptors: _ }),
        () => ({ ok: !1, descriptors: [] })
      ),
      qp(e).then(
        (_) => ({ ok: !0, descriptors: _ }),
        () => ({ ok: !1, descriptors: ji })
      ),
      // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
      yl(e).then(
        (_) => _,
        () => null
      )
    ]), I = g.draft ?? null;
    c(g), r(I), n(I), i(I), l(v.activities ?? []), f(j.descriptors), h(S), x(N.descriptors.length > 0 ? N.descriptors : ji), w(j.ok ? "ready" : "failed");
  }, [e, t, n, i, r, s]);
  return J(() => {
    b().catch((g) => s(g instanceof Error ? g.message : String(g)));
  }, [b, s]), {
    details: a,
    setDetails: c,
    catalog: u,
    activityDescriptors: d,
    availabilityDiagnostics: p,
    expressionDescriptors: y,
    descriptorStatus: m,
    reload: b
  };
}
function IN({ context: e, details: t, setDetails: n, setStatus: i }) {
  const r = oe(null), s = oe(null), a = oe({});
  J(() => {
    r.current = t;
  }, [t]);
  const c = re(() => {
    s.current !== null && (window.clearTimeout(s.current), s.current = null);
    const l = a.current;
    a.current = {};
    const d = r.current?.definition;
    !d || l.name === void 0 && l.description === void 0 || Rp(e, d.id, {
      name: l.name ?? d.name,
      description: l.description ?? d.description ?? null
    }).then((f) => n((p) => p && p.definition.id === f.definition.id ? { ...p, definition: f.definition } : p)).catch(() => i("Couldn't save name/description."));
  }, [e, n, i]), u = re((l) => {
    n((d) => d && { ...d, definition: { ...d.definition, ...l } }), a.current = { ...a.current, ...l }, s.current !== null && window.clearTimeout(s.current), s.current = window.setTimeout(c, 800);
  }, [c, n]);
  return J(() => () => {
    c();
  }, [c]), { updateDefinitionMeta: u };
}
function AN({
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
  const y = re(() => {
    if (!t) return;
    const b = n?.definition.name;
    b0(x0(t, b), b), d("Exported workflow as JSON.");
  }, [t, n, d]), x = re(async () => {
    if (!(!t || i)) {
      l("saving"), d("Saving...");
      try {
        await r(t, "Saved");
      } catch {
      } finally {
        l("idle");
      }
    }
  }, [t, i, r, l, d]), m = re(async () => {
    if (!(!t || i)) {
      l("promoting"), d("Saving...");
      try {
        await r(t, "Saved"), d("Promoting...");
        const b = await Lp(e, t.id), g = await Vp(e, b.versionId);
        u(g.artifactId), d(`Published ${g.artifactVersion}`), await s();
      } catch (b) {
        d(""), f(b instanceof Error ? b.message : String(b));
      } finally {
        l("idle");
      }
    }
  }, [t, i, e, r, s, u, l, d, f]), w = re(async () => {
    if (!t?.state.rootActivity || i) return;
    const b = t, g = Le(b);
    c(), d("Preparing test run...");
    try {
      l("testRunPreparing"), d("Preparing test run...");
      const v = xb(b);
      l("testRunStarting"), d("Starting test run...");
      const j = await Op(e, {
        definitionId: b.definitionId,
        snapshotId: v,
        state: b.state
      });
      a({ draftSignature: g, view: j }), p("runtime"), h(!1), d(xs(j) ? "Test run rejected" : "Test run dispatched");
    } catch (v) {
      d(""), f(v instanceof Error ? v.message : String(v));
    } finally {
      l("idle");
    }
  }, [t, i, e, c, a, p, h, l, d, f]);
  return { exportJson: y, save: x, promoteAndPublish: m, run: w };
}
function _N({
  context: e,
  draft: t,
  frames: n,
  selectedNodeId: i,
  catalog: r,
  activityDescriptors: s,
  availabilityDiagnostics: a
}) {
  const c = t?.state.rootActivity ?? null, u = de(() => new Map(r.map((S) => [S.activityVersionId, S])), [r]), l = re(
    (S) => uh([S.activityVersionId, S.activityTypeKey], a),
    [a]
  ), d = de(() => ab(s), [s]), f = de(() => el(c, n, u), [c, n, u]), p = Yo(f, f ? u.get(f.activityVersionId) : void 0), h = !!f && p === "unsupported", y = de(() => h ? null : Gt(c, n, u), [c, n, u, h]), x = de(() => h && f?.nodeId === i ? f : y?.slot.activities.find((S) => S.nodeId === i) ?? null, [h, y, f, i]), m = de(
    () => x ? cb(x, u, d) : null,
    [u, d, x]
  ), w = de(
    () => x ? l({ activityVersionId: x.activityVersionId, activityTypeKey: u.get(x.activityVersionId)?.activityTypeKey }) : null,
    [l, u, x]
  ), b = x ? Me(x, u) : [], g = x ? xp(x, u.get(x.activityVersionId)) : !1, v = _p(e, t?.state, i, u), j = !h && y?.slot.mode === "flowchart";
  return {
    catalogByVersion: u,
    availabilityLookup: l,
    scopeOwner: f,
    isUnsupportedDesigner: h,
    scope: y,
    selectedNode: x,
    selectedDescriptor: m,
    selectedNodeAvailability: w,
    selectedSlots: b,
    selectedSupportsScopedVariables: g,
    scopedVariableAnalysis: v,
    isFlowchartDesigner: j,
    canAddActivitiesToCanvas: !c || !h
  };
}
function DN({
  details: e,
  draft: t,
  selectedNode: n,
  selectedNodeId: i,
  selectedDescriptor: r,
  catalogByVersion: s
}) {
  J(() => {
    if (!(!e || !t))
      return window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
        workflowId: e.definition.id,
        workflowDefinitionId: e.definition.id,
        workflowVersionId: t.sourceVersionId ?? null,
        draftId: t.id,
        revision: gb(t),
        selectedNodeId: i,
        selectedActivityType: r?.typeName ?? (n ? s.get(n.activityVersionId)?.activityTypeKey ?? n.activityVersionId : null),
        summary: e.definition.name,
        activities: Sd(t.state.rootActivity, s),
        connections: Cd(t.state.rootActivity, s),
        diagnostics: t.validationErrors.map((a) => ({ severity: a.code ?? "warning", message: a.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === e.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [s, e, t, r, n, i]);
}
function TN({
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
      /* @__PURE__ */ o.jsx(Zi, { size: 14, "aria-hidden": "true" }),
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
              f ? /* @__PURE__ */ o.jsx(Wc, { size: 14 }) : /* @__PURE__ */ o.jsx(vt, { size: 14 }),
              /* @__PURE__ */ o.jsx("span", { children: d.category }),
              /* @__PURE__ */ o.jsx("small", { children: d.activities.length })
            ]
          }
        ),
        f ? /* @__PURE__ */ o.jsx("div", { className: "wf-palette-activities", role: "group", children: d.activities.map((p) => {
          const h = p.description?.trim(), y = h ? `wf-palette-description-${p.activityVersionId}` : void 0, x = Se(p), m = Qi(p);
          return /* @__PURE__ */ o.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-activity",
              role: "treeitem",
              draggable: !0,
              title: h || Se(p),
              "aria-describedby": y,
              onClick: () => s(p),
              onDragStart: (w) => a(w, p),
              onDragEnd: (w) => c(w, p),
              onPointerDown: (w) => u(w, p),
              children: [
                /* @__PURE__ */ o.jsx("span", { className: "wf-activity-icon", "data-icon": m, "aria-hidden": "true", children: ys(m) }),
                /* @__PURE__ */ o.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ o.jsx("strong", { children: x }),
                  h ? /* @__PURE__ */ o.jsx("small", { id: y, children: h }) : null
                ] }),
                /* @__PURE__ */ o.jsx(Fc, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            p.activityVersionId
          );
        }) }) : null
      ] }, d.category);
    }) })
  ] });
}
const Ld = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]), $N = "Variable";
function PN({
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
  const d = WN(l), f = r.length > 0 ? r : Qb;
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ o.jsx("span", { className: "wf-section-label", children: "Properties" }),
    d.map((p) => /* @__PURE__ */ o.jsxs("section", { className: "wf-property-group", children: [
      d.length > 1 ? /* @__PURE__ */ o.jsx("h4", { children: p.category }) : null,
      p.inputs.map((h) => /* @__PURE__ */ o.jsx(
        MN,
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
function MN({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: i,
  expressionDescriptors: r,
  visibleVariables: s,
  scopeStatus: a,
  onChange: c
}) {
  const u = t.isReadOnly === !0, l = { activity: e, expressionDescriptors: r, readOnly: u }, d = Vo(n, t, l), f = d?.component, p = t.isWrapped !== !1 ? Td(e, t) : null, h = p?.expression.type ?? "Literal", y = iN(e, t), x = h.toLowerCase(), w = p && (x === "literal" || x === "object") && !aN(t) ? oN(t.typeName) : null, b = w ? Vo(n, t, { ...l, scope: "collection" }) : void 0, g = p ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: r,
    readOnly: u,
    surface: "inline",
    syntax: h
  } : null, v = g ? Od(i, g) : null, j = v?.surfaces.inline, N = v && g ? Hd(v, g, y) : [], S = w != null, I = !!(p && !S && FN(t, d?.id)), _ = !!(p && !S && BN(t, d?.id)), [M, D] = B(!1), k = (E) => {
    const T = p ? tN(p, E) : E;
    c(Ec(e, t, T));
  }, R = (E) => {
    p && c(Ec(e, t, nN(p, E)));
  }, C = w ? b ? Ro(b.component, t, y, u, { ...l, scope: "collection" }, k) : /* @__PURE__ */ o.jsx(
    zN,
    {
      input: t,
      elementTypeName: w.elementTypeName,
      value: y,
      editors: n,
      context: l,
      disabled: u,
      onChange: k
    }
  ) : null, A = h === $N && p ? /* @__PURE__ */ o.jsx(
    ON,
    {
      value: y,
      visibleVariables: s,
      scopeStatus: a,
      disabled: u,
      onChange: k
    }
  ) : C ?? (j && g ? /* @__PURE__ */ o.jsx(
    j,
    {
      descriptor: t,
      syntax: h,
      value: y,
      disabled: u,
      context: g,
      onChange: k
    }
  ) : Ro(f, t, y, u, l, k));
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ o.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ o.jsx("span", { children: Pd(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ o.jsx("p", { children: t.description }) : null,
    p && !I ? /* @__PURE__ */ o.jsx(
      zo,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: h,
        descriptors: r,
        disabled: u,
        onChange: R
      }
    ) : null,
    I ? /* @__PURE__ */ o.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-expression-editor", children: [
        A,
        Oo(N)
      ] }),
      /* @__PURE__ */ o.jsx(
        zo,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: h,
          descriptors: r,
          disabled: u,
          variant: "inline",
          onChange: R
        }
      ),
      _ ? /* @__PURE__ */ o.jsx(
        "button",
        {
          type: "button",
          className: "wf-expression-expand-button",
          "aria-label": `Open expanded ${t.displayName || t.name} editor`,
          title: "Open expanded editor",
          onClick: () => D(!0),
          children: /* @__PURE__ */ o.jsx(An, { size: 13 })
        }
      ) : null
    ] }) : /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
      A,
      Oo(N)
    ] }),
    _ && !I ? /* @__PURE__ */ o.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => D(!0),
        children: [
          /* @__PURE__ */ o.jsx(An, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    M ? /* @__PURE__ */ o.jsx(
      LN,
      {
        input: t,
        value: y,
        syntax: h,
        descriptors: r,
        activity: e,
        expressionEditors: i,
        disabled: u,
        onChange: k,
        onSyntaxChange: R,
        onClose: () => D(!1)
      }
    ) : null
  ] });
}
function RN(e, t, n) {
  return [
    "wf-collection-item",
    t === e ? "dragging" : "",
    t !== null && t !== e && n === e ? "drop-target" : ""
  ].filter(Boolean).join(" ");
}
function zN({
  input: e,
  elementTypeName: t,
  value: n,
  editors: i,
  context: r,
  disabled: s,
  onChange: a
}) {
  const c = cN(n), u = uN(e, t), l = { ...r, scope: "element" }, d = Vo(i, u, l)?.component, f = e.displayName || e.name, p = (j, N) => a(c.map((S, I) => I === j ? N : S)), [h, y] = B(null), [x, m] = B(null), w = () => {
    y(null), m(null);
  }, b = (j) => (N) => {
    y(j), N.dataTransfer.effectAllowed = "move", N.dataTransfer.setData("text/plain", String(j));
  }, g = (j) => (N) => {
    h !== null && (N.preventDefault(), N.dataTransfer.dropEffect = "move", x !== j && m(j));
  }, v = (j) => (N) => {
    N.preventDefault(), h !== null && h !== j && a(ho(c, h, j)), w();
  };
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-collection-editor", children: [
    c.length === 0 ? /* @__PURE__ */ o.jsx("p", { className: "wf-collection-empty", children: "No items yet." }) : /* @__PURE__ */ o.jsx("ul", { className: "wf-collection-items", children: c.map((j, N) => /* @__PURE__ */ o.jsxs(
      "li",
      {
        className: RN(N, h, x),
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
              onDragEnd: w,
              children: /* @__PURE__ */ o.jsx(Fc, { size: 13, "aria-hidden": "true" })
            }
          ),
          /* @__PURE__ */ o.jsx("div", { className: "wf-collection-item-editor", children: Ro(d, u, j, s, l, (S) => p(N, S)) }),
          /* @__PURE__ */ o.jsxs("div", { className: "wf-collection-item-actions", children: [
            /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${N + 1} up`,
                disabled: s || N === 0,
                onClick: () => a(ho(c, N, N - 1)),
                children: /* @__PURE__ */ o.jsx(nf, { size: 13 })
              }
            ),
            /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${N + 1} down`,
                disabled: s || N === c.length - 1,
                onClick: () => a(ho(c, N, N + 1)),
                children: /* @__PURE__ */ o.jsx(Wc, { size: 13 })
              }
            ),
            /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button danger",
                "aria-label": `Remove ${f} item ${N + 1}`,
                disabled: s,
                onClick: () => a(c.filter((S, I) => I !== N)),
                children: /* @__PURE__ */ o.jsx(In, { size: 13 })
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
        onClick: () => a([...c, lN(t)]),
        children: [
          /* @__PURE__ */ o.jsx(Zt, { size: 13 }),
          " Add item"
        ]
      }
    )
  ] });
}
function LN({
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
  const d = zc(), f = e.displayName || e.name, p = {
    activity: r,
    descriptor: e,
    expressionDescriptors: i,
    readOnly: a,
    surface: "expanded",
    syntax: n
  }, h = Od(s, p), y = h?.surfaces.expanded, x = h ? Hd(h, p, t) : [], m = y ? null : HN(s, p);
  return J(() => {
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
      /* @__PURE__ */ o.jsx("button", { type: "button", "aria-label": `Close ${f} editor`, onClick: l, children: /* @__PURE__ */ o.jsx(Hc, { size: 16 }) })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ o.jsx(
          zo,
          {
            label: `${f} expression syntax`,
            value: n,
            descriptors: i,
            disabled: a,
            onChange: u
          }
        ),
        /* @__PURE__ */ o.jsx("span", { children: Pd(e.typeName) })
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
        m ? /* @__PURE__ */ o.jsx("p", { className: "wf-expression-editor-hint", children: m }) : null,
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
      Oo(x)
    ] }),
    /* @__PURE__ */ o.jsxs("footer", { children: [
      /* @__PURE__ */ o.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: l, children: "Close" })
    ] })
  ] }) });
}
function Ro(e, t, n, i, r, s) {
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
function zo({
  label: e,
  value: t,
  descriptors: n,
  disabled: i,
  variant: r = "block",
  onChange: s
}) {
  const [a, c] = B(!1), u = zc(), l = n.find((f) => f.type === t), d = [
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
const Lo = "::";
function Vd(e) {
  return !e || e === Ri ? Ri : e;
}
function Tc(e, t) {
  return `${Vd(t)}${Lo}${e}`;
}
function VN(e) {
  const t = e.indexOf(Lo);
  if (t < 0) return null;
  const n = e.slice(t + Lo.length);
  return n ? { scopeId: e.slice(0, t), referenceKey: n } : null;
}
function ON({ value: e, visibleVariables: t, scopeStatus: n, disabled: i, onChange: r }) {
  const s = pl(e), a = !!e && typeof e == "object" || typeof e == "string" && e.trim().startsWith("{"), c = s && (a || t.some((d) => d.referenceKey === s.referenceKey)) ? s : null, u = c ? Tc(c.referenceKey, c.declaringScopeId) : "", l = !!c && t.some(
    (d) => d.referenceKey === c.referenceKey && d.scopeId === Vd(c.declaringScopeId)
  );
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-variable-picker", children: [
    /* @__PURE__ */ o.jsxs(
      "select",
      {
        "aria-label": "Variable reference",
        value: u,
        disabled: i,
        onChange: (d) => {
          const f = VN(d.target.value);
          f && r(vp(f.referenceKey, f.scopeId));
        },
        children: [
          /* @__PURE__ */ o.jsx("option", { value: "", children: "Select a variable…" }),
          c && !l ? /* @__PURE__ */ o.jsxs("option", { value: u, children: [
            c.referenceKey,
            " (not visible from this scope)"
          ] }) : null,
          t.map((d) => {
            const f = Tc(d.referenceKey, d.scopeId);
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
function Vo(e, t, n) {
  return [...e].sort((i, r) => (i.order ?? 500) - (r.order ?? 500)).find((i) => i.supports(t, n));
}
function Od(e, t) {
  return [...e].sort((n, i) => (n.order ?? 500) - (i.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function Hd(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function HN(e, t) {
  if (t.syntax.toLowerCase() === "literal") return null;
  const n = [...e].sort((a, c) => (a.order ?? 500) - (c.order ?? 500)).find((a) => a.supports(t) && a.metadata)?.metadata;
  if (!n) return `No enhanced editor is registered for ${t.syntax}. Using the generic text editor.`;
  const i = n.displayName?.trim() || "enhanced editor", r = n.installHint?.trim(), s = `No ${i} is registered for ${t.syntax}. Using the generic text editor.`;
  return r ? `${s} ${r}` : s;
}
function Oo(e) {
  return e.length === 0 ? null : /* @__PURE__ */ o.jsx("div", { className: "wf-expression-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const i = t.severity ?? "info";
    return /* @__PURE__ */ o.jsxs("p", { className: `wf-expression-editor-diagnostic ${i}`, children: [
      t.code ? /* @__PURE__ */ o.jsx("span", { children: t.code }) : null,
      t.message
    ] }, `${t.code ?? "diagnostic"}-${n}`);
  }) });
}
function WN(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const i = n.category?.trim() || "General";
    t.set(i, [...t.get(i) ?? [], n]);
  }
  return [...t.entries()].map(([n, i]) => ({ category: n, inputs: i }));
}
function FN(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !Ld.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function BN(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !Ld.has(t) && n !== "multiline") return !1;
  const i = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(i) || n === "singleline" || n === "multiline";
}
function KN({
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
  onEnterSlot: x
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
      /* @__PURE__ */ o.jsx(Ti, { size: 14 }),
      /* @__PURE__ */ o.jsxs("span", { children: [
        "No longer available for new use · ",
        zi(s.state)
      ] })
    ] }) : null,
    /* @__PURE__ */ o.jsx(
      PN,
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
      V0,
      {
        context: e,
        variables: fl(t),
        title: "Container variables",
        addLabel: "Add container variable",
        emptyLabel: "No container variables declared on this activity.",
        warnings: Np(h.shadowingWarnings, t.nodeId),
        onChange: (m) => y(wp(t, m))
      }
    ) }) : null,
    a.length > 0 ? /* @__PURE__ */ o.jsxs("div", { className: "wf-slot-list", children: [
      /* @__PURE__ */ o.jsx("span", { children: "Embedded slots" }),
      a.map((m) => /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => x(t, m.id, `${n} / ${m.label}`), children: [
        m.label,
        /* @__PURE__ */ o.jsx("small", { children: mb(m, c) })
      ] }, m.id))
    ] }) : /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
  ] }) : /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
}
const Wd = [
  { value: "flowchart", label: "Flowchart", hint: "Free-form graph of connected activities." },
  { value: "sequence", label: "Sequence", hint: "Ordered list of activities that run top to bottom." }
];
function Fd({ label: e, hint: t }) {
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsx("span", { className: "wf-root-card-title", children: e }),
    /* @__PURE__ */ o.jsx("span", { className: "wf-root-card-hint", children: t })
  ] });
}
function XN({ value: e, onChange: t }) {
  return /* @__PURE__ */ o.jsx("div", { className: "wf-root-cards", role: "radiogroup", "aria-label": "Root activity", children: Wd.map((n) => {
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
      /* @__PURE__ */ o.jsx(Fd, { label: n.label, hint: n.hint })
    ] }, n.value);
  }) });
}
function YN({ onPick: e }) {
  return /* @__PURE__ */ o.jsx("div", { className: "wf-root-cards", role: "group", "aria-label": "Fill this slot", children: Wd.map((t) => /* @__PURE__ */ o.jsx(
    "button",
    {
      type: "button",
      className: "wf-root-card",
      "aria-label": t.label,
      onClick: () => e(t.value),
      children: /* @__PURE__ */ o.jsx(Fd, { label: t.label, hint: t.hint })
    },
    t.value
  )) });
}
function qN({ slotLabel: e, catalog: t, onPickActivity: n, onBrowseAll: i }) {
  const r = (s) => {
    const a = xd(t, s);
    a && n(a);
  };
  return /* @__PURE__ */ o.jsx("div", { className: "wf-slot-empty", role: "group", "aria-label": `Fill ${e}`, children: /* @__PURE__ */ o.jsxs("div", { className: "wf-slot-empty-card", children: [
    /* @__PURE__ */ o.jsx("p", { className: "wf-slot-empty-title", children: "This slot is empty" }),
    /* @__PURE__ */ o.jsxs("p", { className: "wf-slot-empty-hint", children: [
      "Choose a container for ",
      /* @__PURE__ */ o.jsx("strong", { children: e }),
      ", or pick any activity."
    ] }),
    /* @__PURE__ */ o.jsx(YN, { onPick: r }),
    /* @__PURE__ */ o.jsxs(
      "button",
      {
        type: "button",
        className: "wf-slot-empty-browse",
        onClick: (s) => i({ clientX: s.clientX, clientY: s.clientY }),
        children: [
          /* @__PURE__ */ o.jsx(Hn, { size: 15 }),
          " Browse all activities…"
        ]
      }
    ),
    /* @__PURE__ */ o.jsx("p", { className: "wf-slot-empty-drag-hint", children: "Tip: you can also drag activities from the palette onto the canvas." })
  ] }) });
}
function UN({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: i,
  expressionEditors: r,
  workflowDesignerPanels: s,
  autosaveEnabledByDefault: a,
  onBack: c
}) {
  const u = Kb(), { draft: l, frames: d, selectedNodeId: f, testRun: p, publishedArtifactId: h } = u.state, {
    loadDraft: y,
    replaceDraftByBatch: x,
    editDraft: m,
    editDraftAndSelect: w,
    select: b,
    navigateToScope: g,
    resetToRoot: v,
    enterSlot: j,
    startTestRun: N,
    clearTestRun: S,
    setPublishedArtifact: I
  } = u, [_, M] = B(""), [D, k] = B(""), [R, C] = B("idle"), [A, E] = B(() => /* @__PURE__ */ new Set()), [T, $] = B(""), [P, F] = B("activities"), [W, H] = B("inspector"), [q, Z] = B("designer"), {
    paletteWidth: ne,
    inspectorWidth: le,
    paletteCollapsed: G,
    inspectorCollapsed: z,
    maximizedSidePanel: X,
    setInspectorCollapsed: ae,
    paletteExpanded: ce,
    inspectorExpanded: Q,
    editorBodyClassName: te,
    editorBodyStyle: fe,
    toggleSidePanelCollapsed: O,
    toggleSidePanelMaximized: ee,
    startSidePanelResize: he,
    handleSidePanelResizeKeyDown: me
  } = _d(), { resetHistory: Te, undo: ke, redo: _e, canUndoNow: it, canRedoNow: We } = Wb({ draft: l, restoreDraft: y }), { saveDraft: Fe, autosaveEnabled: De, setAutosaveEnabled: Re, markSaved: L } = EN({ context: e, draft: l, autosaveEnabledByDefault: a, editDraft: m, setStatus: k, setError: M }), {
    details: V,
    setDetails: K,
    catalog: Y,
    activityDescriptors: se,
    availabilityDiagnostics: xe,
    expressionDescriptors: ge,
    descriptorStatus: Ee,
    reload: Ie
  } = kN({ context: e, definitionId: t, resetHistory: Te, loadDraft: y, markSaved: L, setError: M }), { updateDefinitionMeta: Be } = IN({ context: e, details: V, setDetails: K, setStatus: k }), {
    catalogByVersion: ye,
    availabilityLookup: hr,
    scopeOwner: Jn,
    isUnsupportedDesigner: mt,
    scope: Pt,
    selectedNode: Ze,
    selectedDescriptor: Mt,
    selectedNodeAvailability: gr,
    selectedSlots: Qn,
    selectedSupportsScopedVariables: yr,
    scopedVariableAnalysis: mr,
    isFlowchartDesigner: ut,
    canAddActivitiesToCanvas: xr
  } = _N({ context: e, draft: l, frames: d, selectedNodeId: f, catalog: Y, activityDescriptors: se, availabilityDiagnostics: xe }), Rt = de(() => Po(Y), [Y]), wr = de(() => {
    const U = T.trim().toLowerCase();
    if (!U) return Rt;
    const ue = Y.filter((be) => Se(be).toLowerCase().includes(U) || be.activityTypeKey.toLowerCase().includes(U) || (be.category ?? "").toLowerCase().includes(U) || (be.description ?? "").toLowerCase().includes(U));
    return Po(ue);
  }, [Y, T, Rt]), zt = R !== "idle", vr = !!l?.state.rootActivity && !zt, ei = Dt(n, "weaver.workflows.find-draft-risks"), ti = Dt(n, "weaver.workflows.propose-update"), br = Jb({
    draft: l,
    scope: Pt,
    scopeOwner: Jn,
    frames: d,
    catalog: Y,
    catalogByVersion: ye,
    isUnsupportedDesigner: mt,
    isFlowchartDesigner: ut,
    canAddActivitiesToCanvas: xr,
    selectedNodeId: f,
    editDraft: m,
    editDraftAndSelect: w,
    select: b,
    resetToRoot: v,
    setStatus: k,
    setError: M
  }), {
    nodes: Lt,
    edges: Nr,
    canvasRef: jr,
    setReactFlowInstance: Sr,
    connectMenu: Vt,
    setConnectMenu: ni,
    edgeActions: ii,
    onNodesChange: Cr,
    onEdgesChange: Er,
    onNodesDelete: kr,
    onEdgesDelete: Ir,
    isValidConnection: Ar,
    onConnect: _r,
    onConnectStart: ri,
    onConnectEnd: Dr,
    onReconnect: Tr,
    commitLayout: $r,
    canAutoLayout: Pr,
    autoLayout: Mr,
    onCanvasDragOver: oi,
    onCanvasDragLeave: si,
    onCanvasDrop: ai,
    openEmptyConnectMenu: dn,
    onConnectMenuPick: Rr,
    addActivity: zr,
    onPaletteClick: Lr,
    onPaletteDragStart: Vr,
    onPaletteDragEnd: Or,
    onPalettePointerDown: Hr
  } = br, Wr = !mt && d.length > 0 && !!Pt && Pt.slot.activities.length === 0 && Lt.length === 0;
  CN({ draft: l, details: V, catalog: Y, replaceDraftByBatch: x, setStatus: k, setError: M }), J(() => {
    !l?.state.rootActivity || Y.length === 0 || m(({ draft: U }) => {
      if (!U?.state.rootActivity) return null;
      const ue = sl(U.state.rootActivity, ye);
      return !ue || ue === U.state.rootActivity ? null : {
        ...U,
        state: {
          ...U.state,
          rootActivity: ue
        }
      };
    });
  }, [Y.length, ye, l?.state.rootActivity, m]), DN({ details: V, draft: l, selectedNode: Ze, selectedNodeId: f, selectedDescriptor: Mt, catalogByVersion: ye }), J(() => {
    E((U) => {
      let ue = !1;
      const be = new Set(U);
      for (const rt of Rt)
        be.has(rt.category) || (be.add(rt.category), ue = !0);
      return ue ? be : U;
    });
  }, [Rt]);
  const { exportJson: Fr, save: ci, promoteAndPublish: Br, run: li } = AN({
    context: e,
    draft: l,
    details: V,
    busy: zt,
    saveDraft: Fe,
    reload: Ie,
    startTestRun: N,
    clearTestRun: S,
    setPublishedArtifact: I,
    setOperation: C,
    setStatus: k,
    setError: M,
    setActiveRightPanelId: H,
    setInspectorCollapsed: ae
  }), Kr = re((U) => {
    m(({ draft: ue }) => ue ? { ...ue, state: U(ue.state) } : null);
  }, [m]), Xr = re((U) => {
    if (!l) return "No draft is loaded.";
    const ue = v0(U, l);
    return ue.ok ? (y(ue.draft), k("Applied workflow JSON."), null) : ue.error;
  }, [l, y]);
  J(() => {
    const U = (ue) => {
      if (q !== "designer" || !(ue.metaKey || ue.ctrlKey)) return;
      const be = ue.target;
      if (be && (be.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(be.tagName))) return;
      const rt = ue.key.toLowerCase();
      rt === "z" && !ue.shiftKey ? (ue.preventDefault(), ke()) : (rt === "z" && ue.shiftKey || rt === "y") && (ue.preventDefault(), _e());
    };
    return window.addEventListener("keydown", U), () => window.removeEventListener("keydown", U);
  }, [q, ke, _e]);
  const fn = re((U) => {
    m(({ draft: ue }) => {
      const be = ue?.state.rootActivity;
      return !ue || !be ? null : {
        ...ue,
        state: {
          ...ue.state,
          rootActivity: ol(be, U.nodeId, () => U, ye)
        }
      };
    });
  }, [ye, m]), Yr = re((U) => {
    if (!U) return;
    const ue = l?.state.rootActivity;
    if (!ue) return;
    const be = Wf(ue, U, (rt) => {
      const As = ye.get(rt.activityVersionId);
      return As ? Se(As) : rt.nodeId;
    }, ye);
    be && (Z("designer"), g(be, U), ae(!1));
  }, [l?.state.rootActivity, ye, ae, g]), qr = (U) => {
    E((ue) => {
      const be = new Set(ue);
      return be.has(U) ? be.delete(U) : be.add(U), be;
    });
  };
  if (!V || !l)
    return /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: _ || "Loading workflow editor..." });
  const xt = p?.draftSignature === Le(l) ? p.view : null, Cs = xt && D.startsWith("Test run") ? "" : D, Bd = (U) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(U)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, Kd = {
    definition: V.definition,
    draft: l,
    selectedActivity: Ze,
    selectedActivityDescriptor: Mt,
    selectedActivitySlots: Qn,
    catalog: Y,
    currentScopeOwner: Jn,
    frames: d
  }, Es = s.map((U) => {
    const ue = U.component;
    return {
      id: U.id,
      title: U.title,
      side: U.side,
      order: U.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ o.jsx(ue, { context: Kd })
    };
  }), Ur = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ o.jsx(Hn, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(
        TN,
        {
          paletteSearch: T,
          onSearchChange: $,
          groups: wr,
          expandedCategories: A,
          onToggleCategory: qr,
          onActivityClick: Lr,
          onActivityDragStart: Vr,
          onActivityDragEnd: Or,
          onActivityPointerDown: Hr
        }
      )
    },
    ...Es.filter((U) => U.side === "left")
  ].sort(Nc), Zr = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ o.jsx(Fo, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(
        KN,
        {
          context: e,
          selectedNode: Ze,
          selectedNodeLabel: Ze ? Lt.find((U) => U.id === Ze.nodeId)?.data.label ?? Ze.nodeId : "",
          selectedActivityType: Ze ? Mt?.typeName ?? ye.get(Ze.activityVersionId)?.activityTypeKey ?? "Unknown" : "",
          selectedDescriptor: Mt,
          selectedNodeAvailability: gr,
          selectedSlots: Qn,
          catalogByVersion: ye,
          selectedSupportsScopedVariables: yr,
          propertyEditors: i,
          expressionEditors: r,
          expressionDescriptors: ge,
          descriptorStatus: Ee,
          scopedVariableAnalysis: mr,
          onSelectedActivityChange: fn,
          onEnterSlot: j
        }
      )
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: /* @__PURE__ */ o.jsx(Ut, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx($b, { testRun: xt, onOpenRun: Bd })
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ o.jsx(Bc, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(
        Rb,
        {
          context: e,
          ai: n,
          definitionId: V.definition.id,
          publishedArtifactId: h
        }
      )
    },
    ...Es.filter((U) => U.side === "right")
  ].sort(Nc), ks = Ur.find((U) => U.id === P) ?? Ur[0], Is = Zr.find((U) => U.id === W) ?? Zr[0], Xd = [
    { id: "designer", title: "Designer", order: 0, icon: /* @__PURE__ */ o.jsx(Kc, { size: 14 }), render: () => null },
    { id: "code", title: "Code", order: 1, icon: /* @__PURE__ */ o.jsx(cf, { size: 14 }), render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: /* @__PURE__ */ o.jsx(Wo, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-link-button", onClick: c, children: "Definitions" }),
      /* @__PURE__ */ o.jsx(vt, { size: 14 }),
      /* @__PURE__ */ o.jsx("strong", { children: V.definition.name }),
      /* @__PURE__ */ o.jsx("span", { className: "wf-chip", children: "Draft" }),
      Cs ? /* @__PURE__ */ o.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ o.jsx(an, { size: 13 }),
        " ",
        Cs
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
              disabled: !it,
              onClick: ke,
              children: /* @__PURE__ */ o.jsx(rf, { size: 16 })
            }
          ),
          /* @__PURE__ */ o.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Redo",
              title: "Redo (Ctrl+Shift+Z)",
              disabled: !We,
              onClick: _e,
              children: /* @__PURE__ */ o.jsx(of, { size: 16 })
            }
          ),
          /* @__PURE__ */ o.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Auto-layout",
              title: "Auto-layout the canvas",
              disabled: !Pr,
              onClick: Mr,
              children: /* @__PURE__ */ o.jsx(sf, { size: 16 })
            }
          )
        ] }),
        /* @__PURE__ */ o.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ o.jsx("input", { className: "wf-autosave-switch-input", type: "checkbox", checked: De, onChange: (U) => Re(U.target.checked) }),
          /* @__PURE__ */ o.jsx("span", { children: "Autosave" })
        ] }),
        ei ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Tt(n, ei, { definition: V.definition, draft: l }), children: [
          /* @__PURE__ */ o.jsx(at, { size: 15 }),
          " Risks"
        ] }) : null,
        ti ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Tt(n, ti, { definition: V.definition, draft: l }), children: [
          /* @__PURE__ */ o.jsx(at, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ o.jsxs("button", { type: "button", title: "Export workflow as JSON", onClick: Fr, children: [
          /* @__PURE__ */ o.jsx(af, { size: 15 }),
          " Export"
        ] }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", disabled: zt, onClick: () => {
          ci();
        }, children: [
          /* @__PURE__ */ o.jsx(Lc, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", disabled: zt, onClick: () => {
          Br();
        }, children: [
          /* @__PURE__ */ o.jsx(Oc, { size: 15 }),
          " Promote"
        ] }),
        xt ? /* @__PURE__ */ o.jsx(
          Tb,
          {
            testRun: xt,
            onOpenDetails: () => {
              H("runtime"), ae(!1);
            }
          }
        ) : null,
        /* @__PURE__ */ o.jsxs(
          "button",
          {
            type: "button",
            disabled: !vr,
            title: l.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              li();
            },
            children: [
              /* @__PURE__ */ o.jsx(Ut, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    _ ? /* @__PURE__ */ o.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ o.jsx(yt, { size: 16 }),
      " ",
      _
    ] }) : null,
    /* @__PURE__ */ o.jsxs("div", { className: te, style: fe, children: [
      /* @__PURE__ */ o.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ o.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ o.jsx(
            Di,
            {
              label: "Activities panel tabs",
              tabs: Ur,
              activeTabId: ks.id,
              onSelect: F
            }
          ),
          /* @__PURE__ */ o.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": G ? "Expand activities panel" : "Collapse activities panel",
                title: G ? "Expand" : "Collapse",
                onClick: () => O("palette"),
                children: G ? /* @__PURE__ */ o.jsx(vt, { size: 14 }) : /* @__PURE__ */ o.jsx(_n, { size: 14 })
              }
            ),
            G ? null : /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": X === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: X === "palette" ? "Restore" : "Maximize",
                onClick: () => ee("palette"),
                children: X === "palette" ? /* @__PURE__ */ o.jsx(yo, { size: 14 }) : /* @__PURE__ */ o.jsx(An, { size: 14 })
              }
            )
          ] })
        ] }),
        ce ? ks.render() : null
      ] }),
      ce && !X ? /* @__PURE__ */ o.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": vn,
          "aria-valuemax": bn,
          "aria-valuenow": ne,
          tabIndex: 0,
          onPointerDown: (U) => he("palette", U),
          onKeyDown: (U) => me("palette", U)
        }
      ) : /* @__PURE__ */ o.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ o.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ o.jsx("div", { className: "wf-canvas-tabs", children: /* @__PURE__ */ o.jsx(
          Di,
          {
            label: "Editor view tabs",
            tabs: Xd,
            activeTabId: q,
            onSelect: (U) => Z(U)
          }
        ) }),
        q === "code" ? /* @__PURE__ */ o.jsx(E0, { draft: l, onApply: Xr }) : q === "properties" ? /* @__PURE__ */ o.jsx(H0, { details: V, draft: l, context: e, onStateChange: Kr, onDefinitionMetaChange: Be }) : /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
          /* @__PURE__ */ o.jsxs("div", { className: "wf-breadcrumb", children: [
            /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => v(), children: "Root" }),
            d.map((U, ue) => /* @__PURE__ */ o.jsxs(Qe.Fragment, { children: [
              /* @__PURE__ */ o.jsx(vt, { size: 13 }),
              /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => g(d.slice(0, ue + 1), null), children: U.label })
            ] }, `${U.ownerNodeId}-${U.slotId}-${ue}`))
          ] }),
          /* @__PURE__ */ o.jsxs("div", { className: "wf-canvas", ref: jr, onDragOver: oi, onDragLeave: si, onDrop: ai, children: [
            /* @__PURE__ */ o.jsx(gd.Provider, { value: ii, children: /* @__PURE__ */ o.jsx(yd.Provider, { value: hr, children: /* @__PURE__ */ o.jsxs(
              od,
              {
                nodes: Lt,
                edges: Nr,
                nodeTypes: kd,
                edgeTypes: Id,
                onInit: Sr,
                onNodesChange: Cr,
                onEdgesChange: Er,
                onNodesDelete: kr,
                onEdgesDelete: Ir,
                onConnect: _r,
                onConnectStart: ut ? ri : void 0,
                onConnectEnd: ut ? Dr : void 0,
                onReconnect: ut ? Tr : void 0,
                isValidConnection: Ar,
                onDragOver: oi,
                onDragLeave: si,
                onDrop: ai,
                onPaneClick: () => b(null),
                onNodeClick: (U, ue) => b(ue.id),
                onNodeDragStop: mt ? void 0 : $r,
                fitView: !0,
                minZoom: 0.2,
                maxZoom: 1.8,
                nodesConnectable: ut,
                nodesDraggable: !mt,
                selectionOnDrag: !0,
                multiSelectionKeyCode: ["Shift", "Meta", "Control"],
                deleteKeyCode: mt ? null : ["Backspace", "Delete"],
                panActivationKeyCode: null,
                defaultEdgeOptions: { type: "workflow" },
                children: [
                  /* @__PURE__ */ o.jsx(ad, { gap: 18, size: 1 }),
                  /* @__PURE__ */ o.jsx(ld, {}),
                  /* @__PURE__ */ o.jsx(dd, { pannable: !0, zoomable: !0 })
                ]
              }
            ) }) }),
            Wr ? /* @__PURE__ */ o.jsx(
              qN,
              {
                slotLabel: Pt?.slot.label ?? "this slot",
                catalog: Y,
                onPickActivity: zr,
                onBrowseAll: dn
              }
            ) : ut && Lt.length === 0 ? /* @__PURE__ */ o.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => dn(), children: [
              /* @__PURE__ */ o.jsx(Zt, { size: 15 }),
              " Add activity"
            ] }) : null,
            Vt ? /* @__PURE__ */ o.jsx(
              Cb,
              {
                clientX: Vt.clientX,
                clientY: Vt.clientY,
                activities: Y,
                onPick: Rr,
                onClose: () => ni(null)
              }
            ) : null
          ] }),
          /* @__PURE__ */ o.jsx(Db, { draft: l, onRepair: Yr })
        ] })
      ] }),
      Q && !X ? /* @__PURE__ */ o.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Bt,
          "aria-valuemax": Kt,
          "aria-valuenow": le,
          tabIndex: 0,
          onPointerDown: (U) => he("inspector", U),
          onKeyDown: (U) => me("inspector", U)
        }
      ) : /* @__PURE__ */ o.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ o.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ o.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ o.jsx(
            Di,
            {
              label: "Inspector panel tabs",
              tabs: Zr,
              activeTabId: Is.id,
              onSelect: H
            }
          ),
          /* @__PURE__ */ o.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": z ? "Expand inspector panel" : "Collapse inspector panel",
                title: z ? "Expand" : "Collapse",
                onClick: () => O("inspector"),
                children: z ? /* @__PURE__ */ o.jsx(_n, { size: 14 }) : /* @__PURE__ */ o.jsx(vt, { size: 14 })
              }
            ),
            z ? null : /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": X === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: X === "inspector" ? "Restore" : "Maximize",
                onClick: () => ee("inspector"),
                children: X === "inspector" ? /* @__PURE__ */ o.jsx(yo, { size: 14 }) : /* @__PURE__ */ o.jsx(An, { size: 14 })
              }
            )
          ] })
        ] }),
        Q ? Is.render() : null
      ] })
    ] })
  ] });
}
function ZN({ page: e, pageSize: t, totalCount: n, onPageChange: i, onPageSizeChange: r }) {
  const s = md(n, t), a = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
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
      /* @__PURE__ */ o.jsx("select", { value: t, onChange: (u) => r(Number(u.target.value)), children: K0.map((u) => /* @__PURE__ */ o.jsx("option", { value: u, children: u }, u)) })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => i(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ o.jsx(_n, { size: 14 }),
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
        /* @__PURE__ */ o.jsx(vt, { size: 14 })
      ] })
    ] })
  ] });
}
function GN({ draft: e, creating: t, ai: n, suggestMetadataAction: i, onChange: r, onClose: s, onSubmit: a }) {
  const [c, u] = B(!1), [l, d] = B(""), [f, p] = B(!1), [h, y] = B(null), [x, m] = B(null), w = oe(null), b = oe(e);
  b.current = e;
  const g = oe(r);
  g.current = r;
  const v = re((N) => {
    const S = { ...b.current };
    N.name && (S.name = N.name), N.description && (S.description = N.description), g.current(S), y(null), m(null);
  }, []);
  J(() => {
    if (i)
      return n.onPromptResult((N) => {
        if (N.requestId !== w.current) return;
        if (w.current = null, p(!1), N.status !== "completed") {
          m(N.status === "cancelled" ? "Weaver needs more detail — continue in the assistant panel." : "Weaver couldn't generate a suggestion. Try again or fill the fields manually.");
          return;
        }
        const S = Z0(N.text);
        if (!S) {
          m("Couldn't read a suggestion from Weaver's reply. See the assistant panel.");
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
    w.current = S, p(!0), y(null), m(null), n.dispatchPrompt({ ...N, requestId: S });
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
                /* @__PURE__ */ o.jsx(at, { size: 13 }),
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
            /* @__PURE__ */ o.jsx(at, { size: 13 }),
            " ",
            f ? "Generating…" : "Generate"
          ] }) }),
          x ? /* @__PURE__ */ o.jsx("p", { className: "wf-ai-suggest-error", role: "alert", children: x }) : null,
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
            XN,
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
function JN({ context: e, ai: t, onOpen: n }) {
  const [i, r] = B(""), [s, a] = B("active"), [c, u] = B(1), [l, d] = B(X0), [f, p] = B("loading"), [h, y] = B(""), [x, m] = B(""), [w, b] = B([]), [g, v] = B(0), [j, N] = B(() => /* @__PURE__ */ new Set()), [S, I] = B(null), [_, M] = B(!1), [D, k] = B([]), [R, C] = B("idle"), A = oe(null), E = de(() => w.map((O) => O.id), [w]), T = Dt(t, "weaver.workflows.suggest-create-metadata"), $ = Dt(t, "weaver.workflows.explain-definition"), P = E.filter((O) => j.has(O)).length, F = E.length > 0 && P === E.length, W = re(async () => {
    p("loading"), y("");
    try {
      const O = await kp(e, { search: i, state: s, page: c, pageSize: l }), ee = typeof O.totalCount == "number", he = O.totalCount ?? O.definitions.length, me = md(he, l);
      if (he > 0 && c > me) {
        u(me);
        return;
      }
      b(ee ? O.definitions : U0(O.definitions, c, l)), v(he), p("ready");
    } catch (O) {
      y(O instanceof Error ? O.message : String(O)), p("failed");
    }
  }, [e, i, s, c, l]);
  J(() => {
    W();
  }, [W]), J(() => {
    A.current && (A.current.indeterminate = P > 0 && !F);
  }, [F, P]);
  const H = re(async () => {
    if (!(R === "loading" || R === "ready")) {
      C("loading");
      try {
        const O = await Uo(e);
        k(O.activities ?? []), C("ready");
      } catch (O) {
        C("failed"), y(O instanceof Error ? O.message : String(O));
      }
    }
  }, [R, e]), q = () => {
    y(""), m(""), I({ name: "", description: "", rootKind: "flowchart" }), H();
  }, Z = async () => {
    if (S?.name.trim()) {
      M(!0), y(""), m("");
      try {
        const O = await Tp(e, {
          name: S.name.trim(),
          description: S.description.trim() || null,
          rootKind: S.rootKind,
          rootActivityVersionId: G0(S, D)
        });
        I(null), n(O.definition.id);
      } catch (O) {
        y(O instanceof Error ? O.message : String(O));
      } finally {
        M(!1);
      }
    }
  }, ne = (O) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(O)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, le = async () => {
    if (w.length === 1 && c > 1) {
      u(c - 1);
      return;
    }
    await W();
  }, G = () => N(/* @__PURE__ */ new Set()), z = (O, ee) => {
    N((he) => {
      const me = new Set(he);
      return ee ? me.add(O) : me.delete(O), me;
    });
  }, X = (O) => {
    N((ee) => {
      const he = new Set(ee);
      for (const me of E)
        O ? he.add(me) : he.delete(me);
      return he;
    });
  }, ae = (O) => {
    a(O), u(1), G();
  }, ce = (O) => {
    r(O), u(1), G();
  }, Q = async (O) => {
    if (await Ps().confirm({ message: `Delete workflow definition "${O.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" })) {
      m(""), y("");
      try {
        await $p(e, O.id), z(O.id, !1), m(`Deleted ${O.name}`), await le();
      } catch (ee) {
        y(ee instanceof Error ? ee.message : String(ee));
      }
    }
  }, te = async (O) => {
    m(""), y("");
    try {
      await Pp(e, O.id), z(O.id, !1), m(`Restored ${O.name}`), await le();
    } catch (ee) {
      y(ee instanceof Error ? ee.message : String(ee));
    }
  }, fe = async (O) => {
    if (await Ps().confirm({ message: `Permanently delete workflow definition "${O.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) {
      m(""), y("");
      try {
        await Mp(e, O.id), z(O.id, !1), m(`Permanently deleted ${O.name}`), await le();
      } catch (ee) {
        y(ee instanceof Error ? ee.message : String(ee));
      }
    }
  };
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ o.jsx("button", { type: "button", className: s === "active" ? "active" : "", "aria-selected": s === "active", onClick: () => ae("active"), children: "Active" }),
        /* @__PURE__ */ o.jsx("button", { type: "button", className: s === "deleted" ? "active" : "", "aria-selected": s === "deleted", onClick: () => ae("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ o.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ o.jsx(Zi, { size: 15 }),
        /* @__PURE__ */ o.jsx("input", { value: i, onChange: (O) => ce(O.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => {
        W();
      }, children: "Refresh" }),
      /* @__PURE__ */ o.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ o.jsxs("button", { type: "button", title: "Create workflow", onClick: q, children: [
        /* @__PURE__ */ o.jsx(Zt, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ o.jsx(Gn, { message: h, title: "Couldn't load workflow definitions" }) : null,
    f !== "failed" && h ? /* @__PURE__ */ o.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ o.jsx(yt, { size: 16 }),
      " ",
      h
    ] }) : null,
    x ? /* @__PURE__ */ o.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ o.jsx(an, { size: 14 }),
      " ",
      x
    ] }) : null,
    j.size > 0 ? /* @__PURE__ */ o.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ o.jsxs("span", { children: [
        j.size,
        " selected"
      ] }),
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: G, children: "Clear selection" })
    ] }) : null,
    f === "loading" ? /* @__PURE__ */ o.jsx(ws, {}) : null,
    f === "ready" && w.length === 0 ? /* @__PURE__ */ o.jsx(
      vs,
      {
        icon: /* @__PURE__ */ o.jsx(Bc, { size: 22 }),
        title: `No ${s} workflow definitions`,
        description: "Create a workflow to start designing automation, or adjust your filters to see more.",
        action: /* @__PURE__ */ o.jsxs("button", { type: "button", className: "wf-link-button", onClick: q, children: [
          /* @__PURE__ */ o.jsx(Zt, { size: 15 }),
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
        w.map((O) => /* @__PURE__ */ o.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${O.name}`,
            "aria-selected": j.has(O.id),
            tabIndex: 0,
            onClick: () => n(O.id),
            onKeyDown: (ee) => {
              ee.currentTarget === ee.target && (ee.key !== "Enter" && ee.key !== " " || (ee.preventDefault(), n(O.id)));
            },
            children: [
              /* @__PURE__ */ o.jsx("label", { className: "wf-row-select", onClick: (ee) => ee.stopPropagation(), children: /* @__PURE__ */ o.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: j.has(O.id),
                  onChange: (ee) => z(O.id, ee.target.checked),
                  "aria-label": `Select workflow definition ${O.name}`
                }
              ) }),
              /* @__PURE__ */ o.jsxs("span", { children: [
                /* @__PURE__ */ o.jsx("strong", { children: O.name }),
                /* @__PURE__ */ o.jsx("small", { children: O.description || O.id })
              ] }),
              /* @__PURE__ */ o.jsx("span", { children: O.latestVersion ?? "No version" }),
              /* @__PURE__ */ o.jsx("span", { children: s === "deleted" ? Ve(O.deletedAt) : O.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ o.jsx("span", { children: Ve(O.lastModifiedAt) }),
              /* @__PURE__ */ o.jsx("span", { className: "wf-row-actions", onClick: (ee) => ee.stopPropagation(), children: s === "active" ? /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
                /* @__PURE__ */ o.jsx("button", { type: "button", onClick: (ee) => {
                  ee.stopPropagation(), n(O.id);
                }, children: "Open" }),
                /* @__PURE__ */ o.jsx("button", { type: "button", onClick: (ee) => {
                  ee.stopPropagation(), ne(O.id);
                }, children: "Artifacts" }),
                $ ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Tt(t, $, O), children: [
                  /* @__PURE__ */ o.jsx(at, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ o.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  Q(O);
                }, children: [
                  /* @__PURE__ */ o.jsx(In, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
                /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
                  te(O);
                }, children: [
                  /* @__PURE__ */ o.jsx(Bo, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ o.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  fe(O);
                }, children: [
                  /* @__PURE__ */ o.jsx(In, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          O.id
        ))
      ] }),
      /* @__PURE__ */ o.jsx(
        ZN,
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
      GN,
      {
        draft: S,
        creating: _,
        ai: t,
        suggestMetadataAction: T,
        onChange: (O) => I(O),
        onClose: () => I(null),
        onSubmit: Z
      }
    ) : null
  ] });
}
function QN({ activities: e, activityCatalog: t, selectedEvidenceId: n = null, onSelectEvidence: i }) {
  const r = de(
    () => new Map(t.map((a) => [a.activityTypeKey, a])),
    [t]
  ), s = de(() => tj(e), [e]);
  return s.length === 0 ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty wf-timeline-empty", children: "No activity executions recorded yet." }) : /* @__PURE__ */ o.jsx("ol", { className: "wf-timeline", "aria-label": "Execution timeline", children: s.map((a) => {
    const c = r.get(a.activityType), u = Qi(c), l = c ? Se(c) : _t(a.activityType) ?? a.activityType, d = _t(a.activityType) ?? a.activityType, f = nj(a.startedAt ?? a.scheduledAt), p = gs(a.startedAt, a.completedAt);
    return /* @__PURE__ */ o.jsx("li", { children: /* @__PURE__ */ o.jsxs(
      "button",
      {
        type: "button",
        className: "wf-timeline-entry",
        "data-selected": a.activityExecutionId === n,
        onClick: () => i?.(a.activityExecutionId),
        children: [
          /* @__PURE__ */ o.jsx("span", { className: "wf-timeline-icon wf-activity-icon", "data-icon": u, "aria-hidden": "true", children: ys(u) }),
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
          /* @__PURE__ */ o.jsx(ej, { status: a.status })
        ]
      }
    ) }, a.activityExecutionId);
  }) });
}
function ej({ status: e }) {
  const t = e.toLowerCase(), n = t === "completed" ? "OK" : e;
  return /* @__PURE__ */ o.jsx("span", { className: "wf-status-badge wf-timeline-pill", "data-status": t, children: n });
}
function tj(e) {
  return e.map((t, n) => ({ activity: t, index: n })).sort((t, n) => $c(t.activity) - $c(n.activity) || t.index - n.index).map((t) => t.activity);
}
function $c(e) {
  const t = e.startedAt ?? e.scheduledAt, n = t ? Date.parse(t) : Number.NaN;
  return Number.isNaN(n) ? Number.MAX_SAFE_INTEGER : n;
}
function nj(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? "" : t.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: !1 });
}
function ij({ context: e }) {
  const [t, n] = B("loading"), [i, r] = B(""), [s, a] = B(""), [c, u] = B(""), [l, d] = B([]), f = re(async () => {
    n("loading"), r("");
    try {
      const h = await Wp(e, {
        status: s || void 0,
        runKind: c || void 0,
        take: 100
      });
      d(h), n("ready");
    } catch (h) {
      r(h instanceof Error ? h.message : String(h)), d([]), n("failed");
    }
  }, [e, c, s]);
  J(() => {
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
    t === "failed" ? /* @__PURE__ */ o.jsx(Gn, { message: i }) : null,
    t === "loading" ? /* @__PURE__ */ o.jsx(ws, {}) : null,
    t === "ready" && l.length === 0 ? /* @__PURE__ */ o.jsx(
      vs,
      {
        icon: /* @__PURE__ */ o.jsx(Hn, { size: 22 }),
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
            /* @__PURE__ */ o.jsx("span", { children: Nd(h.runKind) }),
            /* @__PURE__ */ o.jsx("span", { children: /* @__PURE__ */ o.jsx(un, { status: h.status, subStatus: h.subStatus }) }),
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
            /* @__PURE__ */ o.jsx("span", { children: Ve(h.startedAt ?? h.createdAt) }),
            /* @__PURE__ */ o.jsx("span", { children: gs(h.startedAt ?? h.createdAt, h.completedAt ?? h.updatedAt) })
          ]
        },
        h.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function rj({ context: e, ai: t, workflowExecutionId: n }) {
  const [i, r] = B("loading"), [s, a] = B(""), [c, u] = B(null), [l, d] = B(null), {
    inspectorWidth: f,
    inspectorCollapsed: p,
    maximizedSidePanel: h,
    inspectorExpanded: y,
    editorBodyStyle: x,
    toggleSidePanelCollapsed: m,
    toggleSidePanelMaximized: w,
    startSidePanelResize: b,
    handleSidePanelResizeKeyDown: g
  } = _d(), v = Dt(t, "weaver.workflows.explain-instance"), j = re(async () => {
    if (!n) {
      a("No workflow execution id was provided."), r("failed");
      return;
    }
    r("loading"), a("");
    try {
      const _ = await Fp(e, n), [M, D] = await Promise.all([
        Dp(e, _.instance.definitionVersionId).then(
          (k) => ({ definitionVersion: k, error: "" }),
          (k) => ({ definitionVersion: null, error: k instanceof Error ? k.message : String(k) })
        ),
        Uo(e)
      ]);
      u({
        details: _,
        definitionVersion: M.definitionVersion,
        definitionVersionError: M.error,
        activityCatalog: D.activities
      }), d(null), r("ready");
    } catch (_) {
      u(null), a(vb(_, n)), r("failed");
    }
  }, [e, n]);
  J(() => {
    j();
  }, [j]);
  const N = () => {
    window.history.pushState({}, "", "/workflows/instances"), window.dispatchEvent(new PopStateEvent("popstate"));
  }, S = () => {
    const _ = c?.details.instance.definitionId;
    _ && (window.history.pushState({}, "", `/workflows/definitions?definition=${encodeURIComponent(_)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  }, I = [
    "wf-instance-detail-workbench",
    p ? "inspector-collapsed" : "",
    h === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: N, children: [
        /* @__PURE__ */ o.jsx(_n, { size: 14 }),
        " Runs"
      ] }),
      c?.details.instance.definitionId ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: S, children: [
        /* @__PURE__ */ o.jsx(Kc, { size: 14 }),
        " Designer"
      ] }) : null,
      /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
        j();
      }, children: [
        /* @__PURE__ */ o.jsx(Bo, { size: 14 }),
        " Refresh"
      ] }),
      c && v ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Tt(t, v, c.details), children: [
        /* @__PURE__ */ o.jsx(at, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    i === "loading" ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Loading workflow run..." }) : null,
    i === "failed" ? /* @__PURE__ */ o.jsx(Gn, { message: s }) : null,
    i === "ready" && c ? /* @__PURE__ */ o.jsxs("div", { className: I, style: x, children: [
      /* @__PURE__ */ o.jsx(
        oj,
        {
          definitionVersion: c.definitionVersion,
          definitionVersionError: c.definitionVersionError,
          activityCatalog: c.activityCatalog,
          details: c.details,
          selectedEvidenceId: l,
          onSelectEvidence: d
        }
      ),
      y && !h ? /* @__PURE__ */ o.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize run details panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Bt,
          "aria-valuemax": Kt,
          "aria-valuenow": f,
          tabIndex: 0,
          onPointerDown: (_) => b("inspector", _),
          onKeyDown: (_) => g("inspector", _)
        }
      ) : /* @__PURE__ */ o.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ o.jsx(
        sj,
        {
          context: e,
          ai: t,
          action: v ?? void 0,
          summary: c.details.instance,
          details: c.details,
          state: "ready",
          error: "",
          selectedEvidenceId: l,
          onSelectEvidence: d,
          activityCatalog: c.activityCatalog,
          graphNodeIds: c.definitionVersion ? db(c.definitionVersion, c.activityCatalog) : void 0,
          collapsed: p,
          expanded: y,
          maximized: h === "inspector",
          onToggleCollapsed: () => m("inspector"),
          onToggleMaximized: () => w("inspector")
        }
      )
    ] }) : null
  ] });
}
function oj({ definitionVersion: e, definitionVersionError: t, activityCatalog: n, details: i, selectedEvidenceId: r, onSelectEvidence: s }) {
  const a = de(() => {
    if (!e) return { nodes: [], edges: [] };
    const c = e.state.rootActivity;
    if (!c) return { nodes: [], edges: [] };
    const u = n.find((h) => h.activityVersionId === c.activityVersionId), l = Yo(c, u), d = l === "unsupported" ? null : Gt(c, [], n), f = l === "unsupported" ? wo(c, n, e.layout) : d ? rl(d, n, e.layout) : wo(c, n, e.layout), p = f.nodes.map((h) => ({
      ...h,
      draggable: !1,
      connectable: !1,
      deletable: !1
    }));
    return {
      nodes: Zf(p, i.activities, i.incidents, r),
      edges: f.edges.map((h) => ({ ...h, deletable: !1 }))
    };
  }, [n, e, i, r]);
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
      /* @__PURE__ */ o.jsx(un, { status: i.instance.status, subStatus: i.instance.subStatus })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ o.jsxs("div", { className: "wf-empty", children: [
        "The workflow run loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ o.jsx("small", { children: wb(t) }) : null
      ] }),
      e && a.nodes.length === 0 ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      a.nodes.length > 0 ? /* @__PURE__ */ o.jsxs(
        od,
        {
          nodes: a.nodes,
          edges: a.edges,
          nodeTypes: kd,
          edgeTypes: Id,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (c, u) => s(u.id),
          onPaneClick: () => s(null),
          children: [
            /* @__PURE__ */ o.jsx(ad, {}),
            /* @__PURE__ */ o.jsx(dd, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ o.jsx(ld, {})
          ]
        }
      ) : null
    ] })
  ] });
}
function sj({
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
  activityCatalog: d = [],
  collapsed: f = !1,
  expanded: p = !0,
  maximized: h = !1,
  onToggleCollapsed: y,
  onToggleMaximized: x
}) {
  const [m, w] = B("timeline");
  if (!i)
    return /* @__PURE__ */ o.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Select a workflow run to inspect its timeline." }) });
  const b = r?.incidents.length ?? 0, g = aj(r?.activities ?? [], c), v = (N) => {
    u?.(N), w("activity");
  }, j = [
    { id: "timeline", title: "Timeline", order: 0, icon: /* @__PURE__ */ o.jsx(Fo, { size: 14 }), render: () => null },
    { id: "activity", title: "Activity", order: 1, icon: /* @__PURE__ */ o.jsx(Vc, { size: 14 }), render: () => null },
    { id: "issues", title: b > 0 ? `Issues (${b})` : "Issues", order: 2, icon: /* @__PURE__ */ o.jsx(yt, { size: 14 }), render: () => null },
    { id: "details", title: "Details", order: 3, icon: /* @__PURE__ */ o.jsx(Wo, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ o.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Run details panel", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-panel-title wf-instance-panel-title", children: [
      /* @__PURE__ */ o.jsx(Di, { label: "Run details tabs", tabs: j, activeTabId: m, onSelect: (N) => w(N) }),
      /* @__PURE__ */ o.jsxs("span", { className: "wf-panel-actions", children: [
        /* @__PURE__ */ o.jsx(
          "button",
          {
            type: "button",
            className: "wf-panel-action-button",
            "aria-label": f ? "Expand run details panel" : "Collapse run details panel",
            title: f ? "Expand" : "Collapse",
            onClick: y,
            children: f ? /* @__PURE__ */ o.jsx(_n, { size: 14 }) : /* @__PURE__ */ o.jsx(vt, { size: 14 })
          }
        ),
        f ? null : /* @__PURE__ */ o.jsx(
          "button",
          {
            type: "button",
            className: "wf-panel-action-button",
            "aria-label": h ? "Restore run details panel" : "Maximize run details panel",
            title: h ? "Restore" : "Maximize",
            onClick: x,
            children: h ? /* @__PURE__ */ o.jsx(yo, { size: 14 }) : /* @__PURE__ */ o.jsx(An, { size: 14 })
          }
        )
      ] })
    ] }),
    p ? /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
      /* @__PURE__ */ o.jsxs("header", { children: [
        /* @__PURE__ */ o.jsxs("div", { children: [
          /* @__PURE__ */ o.jsx("span", { children: "Workflow Instance ID" }),
          /* @__PURE__ */ o.jsx("h3", { children: i.workflowExecutionId })
        ] }),
        n ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Tt(t, n, r ?? i), children: [
          /* @__PURE__ */ o.jsx(at, { size: 13 }),
          " Explain"
        ] }) : null
      ] }),
      s === "loading" ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Loading run details..." }) : null,
      s === "failed" ? /* @__PURE__ */ o.jsx(Gn, { message: a }) : null,
      s === "ready" && r ? /* @__PURE__ */ o.jsx("div", { className: "wf-instance-tab-content", children: m === "timeline" ? /* @__PURE__ */ o.jsx(
        QN,
        {
          activities: r.activities,
          activityCatalog: d,
          selectedEvidenceId: c,
          onSelectEvidence: v
        }
      ) : m === "activity" ? /* @__PURE__ */ o.jsx(cj, { context: e, activity: g, activityCatalog: d }) : m === "issues" ? /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
        /* @__PURE__ */ o.jsx(yj, { incidents: r.incidents, selectedEvidenceId: c, onSelectEvidence: u }),
        /* @__PURE__ */ o.jsx(bj, { details: r, graphNodeIds: l })
      ] }) : /* @__PURE__ */ o.jsxs("dl", { className: "wf-instance-meta", children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ o.jsx("dd", { children: /* @__PURE__ */ o.jsx(un, { status: i.status, subStatus: i.subStatus }) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Run Kind" }),
        /* @__PURE__ */ o.jsx("dd", { children: Nd(i.runKind) }),
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
        /* @__PURE__ */ o.jsx("dd", { children: Ve(i.createdAt) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Started" }),
        /* @__PURE__ */ o.jsx("dd", { children: Ve(i.startedAt) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Completed" }),
        /* @__PURE__ */ o.jsx("dd", { children: Ve(i.completedAt) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Correlation" }),
        /* @__PURE__ */ o.jsx("dd", { children: i.correlationId || "None" })
      ] }) }) : null
    ] }) : null
  ] });
}
function aj(e, t) {
  if (!t) return null;
  const n = e.find((r) => r.activityExecutionId === t);
  if (n) return n;
  const i = e.filter((r) => r.executableNodeId === t || r.authoredActivityId === t);
  return i.length > 0 ? dl(i) : null;
}
function cj({ context: e, activity: t, activityCatalog: n }) {
  const i = t?.activityExecutionId ?? null, r = t?.workflowExecutionId ?? null, [s, a] = B({
    activityExecutionId: null,
    status: "idle",
    inspection: null,
    error: ""
  });
  if (J(() => {
    if (!i || !r) {
      a({ activityExecutionId: null, status: "idle", inspection: null, error: "" });
      return;
    }
    let f = !1;
    const p = i;
    return a({ activityExecutionId: p, status: "loading", inspection: null, error: "" }), Bp(e, r, p).then(
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
        /* @__PURE__ */ o.jsx("dd", { children: /* @__PURE__ */ o.jsx(un, { status: t.status, subStatus: t.subStatus }) }),
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
        /* @__PURE__ */ o.jsx("dd", { children: Ve(t.startedAt) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Completed" }),
        /* @__PURE__ */ o.jsx("dd", { children: Ve(t.completedAt) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Duration" }),
        /* @__PURE__ */ o.jsx("dd", { children: gs(t.startedAt, t.completedAt) || "Unknown" }),
        /* @__PURE__ */ o.jsx("dt", { children: "Bookmarks" }),
        /* @__PURE__ */ o.jsx("dd", { children: l }),
        /* @__PURE__ */ o.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ o.jsx("dd", { children: d })
      ] })
    ] }),
    /* @__PURE__ */ o.jsx(lj, { state: s })
  ] });
}
function lj({ state: e }) {
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
    /* @__PURE__ */ o.jsx("div", { className: "wf-runtime-input-list", children: t.map((n) => /* @__PURE__ */ o.jsx(uj, { snapshot: n }, `${n.name}:${n.capturedAt}:${n.captureMode}`)) })
  ] });
}
function uj({ snapshot: e }) {
  const t = e.type?.displayName || e.type?.typeName || e.type?.alias || "Unknown", n = e.captureMode === "Payload";
  return /* @__PURE__ */ o.jsxs("article", { className: "wf-runtime-input", children: [
    /* @__PURE__ */ o.jsxs("header", { children: [
      /* @__PURE__ */ o.jsxs("span", { children: [
        /* @__PURE__ */ o.jsx("strong", { children: e.name }),
        /* @__PURE__ */ o.jsx("small", { children: t })
      ] }),
      /* @__PURE__ */ o.jsx("span", { className: "wf-runtime-capture-mode", children: fj(e.captureMode) })
    ] }),
    n ? /* @__PURE__ */ o.jsx(dj, { payload: e.payload }) : /* @__PURE__ */ o.jsx("p", { children: e.captureReason || "The runtime capture policy did not include this input value." }),
    e.isSensitive ? /* @__PURE__ */ o.jsx("p", { className: "wf-instance-note", children: "Marked sensitive by runtime evidence." }) : null
  ] });
}
function dj({ payload: e }) {
  const t = hj(e);
  return t.length <= 160 && !t.includes(`
`) ? /* @__PURE__ */ o.jsx("code", { className: "wf-runtime-input-value", children: t }) : /* @__PURE__ */ o.jsxs("details", { className: "wf-runtime-input-value-details", children: [
    /* @__PURE__ */ o.jsx("summary", { children: pj(t) }),
    /* @__PURE__ */ o.jsx("pre", { children: t })
  ] });
}
function fj(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2");
}
function pj(e) {
  const t = e.split(`
`, 1)[0] || e;
  return t.length > 120 ? `${t.slice(0, 117)}...` : t;
}
function hj(e) {
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
const gj = [
  "runtime.faultStackTrace",
  "runtime.exceptionStackTrace",
  "runtime.stackTrace",
  "faultStackTrace",
  "exceptionStackTrace",
  "stackTrace"
];
function yj({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ o.jsx("h4", { children: "Incidents" }),
    e.length === 0 ? /* @__PURE__ */ o.jsx("p", { children: "No incidents recorded." }) : null,
    e.map((i) => /* @__PURE__ */ o.jsxs(
      "article",
      {
        className: "wf-instance-incident",
        "data-severity": i.severity.toLowerCase(),
        "data-selected": i.incidentId === t,
        children: [
          /* @__PURE__ */ o.jsxs("button", { type: "button", className: "wf-instance-incident-summary", onClick: () => n?.(i.incidentId), children: [
            /* @__PURE__ */ o.jsx("strong", { children: i.failureType }),
            /* @__PURE__ */ o.jsxs("span", { children: [
              i.status,
              " · ",
              i.severity
            ] }),
            /* @__PURE__ */ o.jsx("p", { children: i.message })
          ] }),
          /* @__PURE__ */ o.jsx(mj, { incident: i })
        ]
      },
      i.incidentId
    ))
  ] });
}
function mj({ incident: e }) {
  const t = xj(e);
  return t ? /* @__PURE__ */ o.jsxs("details", { className: "wf-incident-stacktrace", children: [
    /* @__PURE__ */ o.jsx("summary", { children: vj(t) }),
    /* @__PURE__ */ o.jsx("pre", { children: t })
  ] }) : null;
}
function xj(e) {
  const t = wj(e.stackTrace, e.exceptionStackTrace);
  if (t) return t;
  for (const n of gj) {
    const i = e.metadata?.[n];
    if (i && i.trim()) return i;
  }
  return null;
}
function wj(...e) {
  return e.find((t) => t?.trim()) ?? null;
}
function vj(e) {
  const n = (e.split(`
`).find((i) => i.trim()) ?? e).trim();
  return n.length > 120 ? `${n.slice(0, 117)}...` : n;
}
function bj({ details: e, graphNodeIds: t }) {
  if (!t) return null;
  const n = new Map(e.activities.map((s) => [s.activityExecutionId, s])), i = e.activities.filter((s) => !t.has(wc(s))), r = e.incidents.filter((s) => {
    const a = s.activityExecutionId ? n.get(s.activityExecutionId) : null, c = s.executableNodeId ?? (a ? wc(a) : "");
    return !c || !t.has(c);
  });
  return i.length === 0 && r.length === 0 ? null : /* @__PURE__ */ o.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ o.jsx("h4", { children: "Unmatched runtime evidence" }),
    /* @__PURE__ */ o.jsxs("div", { className: "wf-instance-unmatched-list", children: [
      i.map((s) => /* @__PURE__ */ o.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ o.jsx("strong", { children: _t(s.activityType) ?? s.activityType }),
        /* @__PURE__ */ o.jsx("small", { children: s.activityExecutionId })
      ] }, `activity-${s.activityExecutionId}`)),
      r.map((s) => /* @__PURE__ */ o.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ o.jsx("strong", { children: s.failureType }),
        /* @__PURE__ */ o.jsx("small", { children: s.incidentId })
      ] }, `incident-${s.incidentId}`))
    ] })
  ] });
}
function Nj({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: i,
  workflowDesignerPanels: r,
  autosaveEnabledByDefault: s
}) {
  const [a, c] = B(Pc);
  J(() => {
    const l = () => c(Pc());
    return window.addEventListener("popstate", l), () => window.removeEventListener("popstate", l);
  }, []);
  const u = (l) => {
    const d = l ? `/workflows/definitions?definition=${encodeURIComponent(l)}` : "/workflows/definitions";
    window.history.pushState({}, "", d), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return a ? /* @__PURE__ */ o.jsx(UN, { context: e, definitionId: a, ai: t, propertyEditors: n, expressionEditors: i, workflowDesignerPanels: r, autosaveEnabledByDefault: s, onBack: () => u(null) }) : /* @__PURE__ */ o.jsx(pr, { title: "Definitions", children: /* @__PURE__ */ o.jsx(JN, { context: e, ai: t, onOpen: u }) });
}
function jj({ context: e, ai: t }) {
  const [n, i] = B(Mc);
  J(() => {
    const s = () => i(Mc());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const r = re((s) => {
    const a = s?.trim() ?? "", c = new URL(window.location.href);
    a ? c.searchParams.set("definition", a) : c.searchParams.delete("definition"), i(a || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ o.jsx(pr, { title: "Executables", children: /* @__PURE__ */ o.jsx(Pb, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: r }) });
}
function Sj({ context: e }) {
  return /* @__PURE__ */ o.jsx(pr, { title: "Runs", children: /* @__PURE__ */ o.jsx(ij, { context: e }) });
}
function Cj({ context: e, ai: t }) {
  const n = Ej();
  return /* @__PURE__ */ o.jsx(pr, { title: "Run", children: /* @__PURE__ */ o.jsx(rj, { context: e, ai: t, workflowExecutionId: n }) });
}
function pr({ title: e, children: t }) {
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ o.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ o.jsxs("div", { children: [
      /* @__PURE__ */ o.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ o.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function Pc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Mc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Ej() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function Dj(e) {
  gf(e.dialogs), e.featureAreas.add({
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
        component: () => /* @__PURE__ */ o.jsx(Nj, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list(), autosaveEnabledByDefault: e.runtime.workflows?.autosaveEnabledByDefault ?? !0 })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ o.jsx(jj, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => /* @__PURE__ */ o.jsx(Sj, { context: e.backend })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => /* @__PURE__ */ o.jsx(Cj, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => /* @__PURE__ */ o.jsx(dh, { context: e.backend })
      }
    ]
  });
}
export {
  pb as isConnectEndOverExistingWorkflowNode,
  Dj as register,
  hb as resolveConnectEndSource
};
