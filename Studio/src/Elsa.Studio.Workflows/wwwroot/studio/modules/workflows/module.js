import Ge, { useMemo as fe, useState as B, useEffect as J, memo as be, forwardRef as Ac, useRef as re, useCallback as ie, useContext as $n, createContext as Oi, useLayoutEffect as Td, lazy as Pd, Suspense as $d, useReducer as Md, useId as _c } from "react";
import { ListChecks as Rd, Save as Dc, EyeOff as Is, Shield as ks, AlertTriangle as Ao, SlidersHorizontal as Hi, Activity as Tc, Search as Uo, Check as Qt, Boxes as Zo, Zap as Ld, Play as Ht, Terminal as zd, ListTree as Wi, GitBranch as Pc, Plus as Wt, Trash2 as jn, AlertCircle as gt, Wrench as Vd, Copy as Od, X as $c, Sparkles as it, RotateCcw as Fi, ChevronDown as Mc, ChevronRight as $t, GripVertical as Rc, Maximize2 as _o, ChevronUp as Hd, Package as Lc, Undo2 as Wd, Redo2 as Fd, Network as Bd, Download as Kd, ChevronLeft as Do, Minimize2 as As, Workflow as zc, Code2 as Xd } from "lucide-react";
import { useQuery as Vc, useQueryClient as qd, useMutation as Yd } from "@tanstack/react-query";
import { useTablistKeyboard as Ud } from "@elsa-workflows/studio-ui";
function Zd(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var Jr = { exports: {} }, sn = {};
var _s;
function Gd() {
  if (_s) return sn;
  _s = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function n(o, r, s) {
    var a = null;
    if (s !== void 0 && (a = "" + s), r.key !== void 0 && (a = "" + r.key), "key" in r) {
      s = {};
      for (var c in r)
        c !== "key" && (s[c] = r[c]);
    } else s = r;
    return r = s.ref, {
      $$typeof: e,
      type: o,
      key: a,
      ref: r !== void 0 ? r : null,
      props: s
    };
  }
  return sn.Fragment = t, sn.jsx = n, sn.jsxs = n, sn;
}
var Ds;
function Jd() {
  return Ds || (Ds = 1, Jr.exports = Gd()), Jr.exports;
}
var i = Jd();
let Oc;
function Qd(e) {
  Oc = e;
}
function Ts() {
  return Oc;
}
const ef = "String", tf = "singleline";
function nf(e) {
  return e === "Single" || e === "Array" || e === "List" || e === "HashSet";
}
function Bi(e, t = "Single") {
  return { alias: (e ?? "").trim() || ef, collectionKind: t };
}
function Hc(e) {
  const t = e.type ?? e.Type;
  if (To(t))
    return { alias: typeof t.alias == "string" ? t.alias : typeof t.typeName == "string" ? t.typeName : "", collectionKind: nf(t.collectionKind) ? t.collectionKind : "Single" };
  const n = e.typeInformation ?? e.TypeInformation;
  return To(n) ? { alias: typeof n.typeName == "string" ? n.typeName : "", collectionKind: Ps(e) ? "Array" : "Single" } : { alias: typeof t == "string" ? t : "", collectionKind: Ps(e) ? "Array" : "Single" };
}
function Ps(e) {
  const t = e.isArray ?? e.IsArray;
  return t === !0 || t === "true";
}
function $s(e) {
  return (e.alias ?? e.typeName ?? "").trim();
}
function Go() {
  return typeof crypto < "u" && typeof crypto.randomUUID == "function" ? crypto.randomUUID() : `id-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
}
function of(e, t) {
  const n = new Set(t);
  let o = 1, r = `${e}${o}`;
  for (; n.has(r); )
    o += 1, r = `${e}${o}`;
  return r;
}
function rf(e) {
  return {
    referenceKey: Go(),
    name: e.name,
    type: Bi(e.alias),
    storageDriverType: e.storageDriver?.trim() || null,
    default: null
  };
}
function sf(e, t) {
  return { ...e, ...t };
}
function af(e) {
  return e === "" ? null : { value: e, expressionType: "Literal" };
}
function cf(e) {
  return e == null || e.value == null ? "" : typeof e.value == "object" ? JSON.stringify(e.value) : String(e.value);
}
function lf(e) {
  return {
    referenceKey: Go(),
    name: e.name,
    type: Bi(e.alias),
    displayName: e.name,
    description: "",
    category: "",
    uiHint: tf,
    storageDriverType: e.storageDriver ?? null,
    isRequired: !1
  };
}
function uf(e, t) {
  return { ...e, ...t };
}
function df(e) {
  return {
    referenceKey: Go(),
    name: e.name,
    type: Bi(e.alias),
    displayName: e.name,
    description: "",
    category: ""
  };
}
function ff(e, t) {
  return { ...e, ...t };
}
function pf(e) {
  const t = e.split(",")[0].trim();
  return (t.split(".").pop() ?? t).split("`")[0];
}
function Wc(e, t) {
  return e && !e.includes(",") && !e.includes(".") ? e : pf(e || t);
}
function hf(e, t) {
  return Wc(e, t).replace(/StorageDriver$/, "");
}
function To(e) {
  return !!e && typeof e == "object" && !Array.isArray(e);
}
function wn(e, t) {
  for (const n of t) {
    const o = e[n];
    if (o != null && typeof o != "object") return String(o);
  }
  return "";
}
const gf = ["name", "Name"], Fc = ["name", "Name"], yf = ["storageDriverType", "StorageDriverType"], Bc = ["referenceKey", "ReferenceKey"], mf = /* @__PURE__ */ new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);
function Mn(e) {
  return Kc(e, bf);
}
function Jo(e) {
  return Kc(e, Nf);
}
function Kc(e, t) {
  if (!e) return e;
  const n = { ...e };
  return e.rootActivity && (n.rootActivity = Xc(e.rootActivity, t)), Array.isArray(e.variables) && (n.variables = xo(e.variables, yi)), Array.isArray(e.inputs) && (n.inputs = xo(e.inputs, yi)), Array.isArray(e.outputs) && (n.outputs = xo(e.outputs, (o) => qc(o, !1))), n;
}
function Xc(e, t) {
  const n = t(e), o = n.structure;
  if (!o || !Je(o.payload)) return n;
  let r = !1;
  const s = { ...o.payload };
  for (const [a, c] of Object.entries(o.payload))
    Array.isArray(c) && c.length > 0 && c.every(If) && (s[a] = c.map((u) => Xc(u, t)), r = !0);
  return Array.isArray(o.payload.variables) && o.payload.variables.length > 0 && (s.variables = xo(o.payload.variables, yi), r = !0), r ? { ...n, structure: { ...o, payload: s } } : n;
}
function xo(e, t) {
  return e.map((n) => Je(n) && !Array.isArray(n) ? t(n) : n);
}
function yi(e) {
  return qc(e, !0);
}
const xf = [
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
function qc(e, t) {
  const n = wf(e, xf);
  return wn(e, Bc).trim() || (n.referenceKey = Go()), n.type = Hc(e), t && (n.storageDriverType = vf(e.storageDriverType ?? e.StorageDriverType)), n;
}
function wf(e, t) {
  const n = new Set(t), o = {};
  for (const [r, s] of Object.entries(e))
    n.has(r) || (o[r] = s);
  return o;
}
function vf(e) {
  if (typeof e == "string") return e.trim() ? e : null;
  if (Je(e)) {
    const t = typeof e.typeName == "string" ? e.typeName : "";
    if (!t) return null;
    const n = typeof e.namespace == "string" ? e.namespace : "";
    return n ? `${n}.${t}` : t;
  }
  return null;
}
function bf(e) {
  const t = [], n = {};
  for (const [r, s] of Object.entries(e))
    mf.has(r) || (Ef(s) ? t.push({
      referenceKey: jf(r),
      value: Cf(s.expression)
    }) : n[r] = s);
  const o = Array.isArray(e.inputs) ? e.inputs : [];
  return {
    ...n,
    nodeId: e.nodeId,
    activityVersionId: e.activityVersionId,
    inputs: [...o, ...t],
    outputs: Array.isArray(e.outputs) ? e.outputs : [],
    structure: e.structure
  };
}
function Nf(e) {
  const t = Array.isArray(e.inputs) ? e.inputs : [], n = {};
  for (const o of t) {
    if (!Je(o) || typeof o.referenceKey != "string") continue;
    const r = Je(o.value) ? o.value : {};
    n[Sf(o.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof r.expressionType == "string" ? r.expressionType : "Literal",
        value: r.value ?? ""
      }
    };
  }
  return { ...e, ...n, inputs: [] };
}
function jf(e) {
  return e && e.charAt(0).toUpperCase() + e.slice(1);
}
function Sf(e) {
  return e && e.charAt(0).toLowerCase() + e.slice(1);
}
function Cf(e) {
  const t = e.type || "Literal";
  return t === "Variable" && Je(e.value) ? { value: e.value, expressionType: t } : t === "Literal" && Je(e.value) ? { value: Ms(e.value), expressionType: "Object" } : { value: Ms(e.value), expressionType: t };
}
function Ms(e) {
  return e == null ? null : typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function Ef(e) {
  if (!Je(e) || Array.isArray(e)) return !1;
  const t = e.expression;
  return Je(t) && typeof t.type == "string";
}
function If(e) {
  return Je(e) && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Je(e) {
  return typeof e == "object" && e !== null;
}
const Rn = "elsa.sequence.structure", en = "elsa.flowchart.structure";
function Yc(e, t, n) {
  if (!e) return null;
  let o = e;
  for (const r of t) {
    const s = De(o, n).find((c) => c.id === r.slotId);
    if (!s) return null;
    const a = s.activities.find((c) => c.nodeId === r.ownerNodeId);
    if (!a) return null;
    o = a;
  }
  return o;
}
function kf(e, t, n = (r) => r.nodeId, o) {
  if (!e) return null;
  if (e.nodeId === t) return [];
  const r = (s, a) => {
    const c = De(s, o);
    if (c[0]?.activities.some((u) => u.nodeId === t)) return a;
    for (const u of c)
      for (const l of u.activities) {
        const d = r(l, [...a, { ownerNodeId: l.nodeId, slotId: u.id, label: n(l) }]);
        if (d) return d;
      }
    return null;
  };
  return r(e, []);
}
function Sn(e, t, n) {
  const o = Yc(e, t, n);
  if (!o) return null;
  const r = De(o, n)[0];
  return r ? { owner: o, slot: r } : null;
}
function De(e, t) {
  const n = e.structure;
  if (!n || !n.payload || typeof n.payload != "object") return [];
  const o = n.payload, r = Ki(Uc(e, t));
  if (r?.kind === n.kind) return Df(n, r);
  const s = Gf(n), a = un(o.activities);
  return a ? [{
    id: `${n.kind}:activities`,
    label: Jf(n),
    property: "activities",
    cardinality: "many",
    mode: s,
    activities: a
  }] : Object.entries(o).filter(([, c]) => un(c) || $o(c)).map(([c, u]) => ({
    id: `${n.kind}:${c}`,
    label: ep(c),
    property: c,
    cardinality: un(u) ? "many" : "single",
    mode: "generic",
    activities: un(u) ?? ($o(u) ? [u] : [])
  }));
}
function Ki(e) {
  for (const t of e?.designFacets ?? []) {
    if (!Qe(t)) continue;
    const n = typeof t.kind == "string" ? t.kind : "", o = typeof t.schemaVersion == "string" ? t.schemaVersion : "";
    if (!n || !o || !Qe(t.payload)) continue;
    const r = Af(t.payload);
    if (r) return { kind: n, schemaVersion: o, payload: r };
  }
  return null;
}
function Af(e) {
  const t = e.mode;
  if (t !== "sequence" && t !== "flowchart" && t !== "generic" || typeof e.supportsScopedVariables != "boolean" || !Array.isArray(e.slots) || !Qe(e.initialPayload)) return null;
  const n = e.slots.map(_f).filter((o) => o !== null);
  return {
    mode: t,
    supportsScopedVariables: e.supportsScopedVariables,
    slots: n,
    initialPayload: e.initialPayload
  };
}
function _f(e) {
  if (!Qe(e)) return null;
  const t = typeof e.name == "string" ? e.name : "", n = typeof e.property == "string" ? e.property : "", o = typeof e.displayName == "string" ? e.displayName : "", r = e.cardinality;
  return !t || !n || !o || r !== "single" && r !== "many" ? null : {
    name: t,
    property: n,
    displayName: o,
    cardinality: r,
    collectionProperty: Mt(e.collectionProperty),
    childProperty: Mt(e.childProperty),
    labelProperty: Mt(e.labelProperty),
    slotNameTemplate: Mt(e.slotNameTemplate)
  };
}
function Df(e, t) {
  return t.payload.slots.flatMap((n) => {
    if (n.collectionProperty && n.childProperty) {
      const o = e.payload[n.collectionProperty];
      return Array.isArray(o) ? o.flatMap((r, s) => {
        if (!Qe(r)) return [];
        const a = n.labelProperty ? Mt(r[n.labelProperty]) : void 0;
        return [Rs(e.kind, n, r[n.childProperty], t.payload.mode, s, a)];
      }) : [];
    }
    return [Rs(e.kind, n, e.payload[n.property], t.payload.mode)];
  });
}
function Rs(e, t, n, o, r, s) {
  const a = r === void 0 ? "" : `:${t.collectionProperty}:${t.childProperty}:${r}`;
  return {
    id: `${e}:${t.property}${a}`,
    label: r === void 0 ? t.displayName : Pf(t, r, s),
    property: t.property,
    cardinality: t.cardinality,
    mode: o,
    activities: Tf(n, t.cardinality),
    collectionProperty: t.collectionProperty,
    childProperty: t.childProperty,
    labelProperty: t.labelProperty,
    slotNameTemplate: t.slotNameTemplate,
    collectionIndex: r,
    collectionItemLabel: s
  };
}
function Tf(e, t) {
  return t === "many" ? un(e) ?? [] : $o(e) ? [e] : [];
}
function Pf(e, t, n) {
  return e.slotNameTemplate ? e.slotNameTemplate.replaceAll("{name}", e.name).replaceAll("{displayName}", e.displayName).replaceAll("{label}", n ?? String(t + 1)).replaceAll("{index}", String(t + 1)) : n ? `${e.displayName}: ${n}` : `${e.displayName} ${t + 1}`;
}
function Uc(e, t) {
  if (t)
    return t instanceof Map ? t.get(e.activityVersionId) : Array.isArray(t) ? t.find((n) => n.activityVersionId === e.activityVersionId) : t.activityVersionId === e.activityVersionId ? t : void 0;
}
function $f(e, t) {
  if (t.labelProperty && t.collectionItemLabel) {
    const n = e.map((o, r) => Qe(o) && Mt(o[t.labelProperty]) === t.collectionItemLabel ? r : -1).filter((o) => o >= 0);
    if (n.length === 1) return n[0];
  }
  return typeof t.collectionIndex == "number" && t.collectionIndex >= 0 && t.collectionIndex < e.length ? t.collectionIndex : -1;
}
function Zc(e, t, n) {
  const o = new Map(t.map((a) => [a.activityVersionId, a])), r = new Map(n.map((a) => [a.nodeId, a])), s = e.slot.activities.map((a, c) => {
    const u = o.get(a.activityVersionId), l = r.get(a.nodeId) ?? Qf(e.slot.mode, c);
    return el(a, u, { x: l.x, y: l.y });
  });
  return {
    nodes: s,
    edges: e.slot.mode === "flowchart" ? nl(e.owner) : Bf(e.slot, s)
  };
}
function mi(e, t, n) {
  const o = t.find((s) => s.activityVersionId === e.activityVersionId), r = n.find((s) => s.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [el(e, o, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function Mf(e, t, n, o = null) {
  const r = new Map(t.map((c) => [c.activityExecutionId, c])), s = zs(t, (c) => c.authoredActivityId || c.executableNodeId), a = zs(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? r.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const u = s.get(c.id) ?? [], l = a.get(c.id) ?? [];
    if (u.length === 0 && l.length === 0) return c;
    const d = rl(u), f = o === c.id || u.some((h) => h.activityExecutionId === o) || l.some((h) => h.incidentId === o), p = {
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
function Xi(e, t) {
  if (e?.structure?.kind === en || Of(t)) return "flowchart";
  if (e?.structure?.kind === Rn || Hf(t)) return "sequence";
  if (e) {
    const n = De(e, t)[0];
    if (n) return n.mode === "flowchart" ? "flowchart" : "sequence";
  }
  return "unsupported";
}
function xi(e, t, n, o) {
  if (t.length === 0) {
    const u = De(e, o)[0];
    return u ? Ft(e, u, n) : e;
  }
  const [r, ...s] = t, a = De(e, o).find((u) => u.id === r.slotId);
  if (!a) return e;
  const c = a.activities.map((u) => u.nodeId === r.ownerNodeId ? xi(u, s, n, o) : u);
  return Ft(e, a, c);
}
function Gc(e, t, n, o) {
  if (t.length === 0) return n;
  const [r, ...s] = t, a = De(e, o).find((u) => u.id === r.slotId);
  if (!a) return e;
  const c = a.activities.map((u) => u.nodeId === r.ownerNodeId ? Gc(u, s, n, o) : u);
  return Ft(e, a, c);
}
function Jc(e, t, n, o) {
  if (e.nodeId === t) return n(e);
  const r = De(e, o);
  if (r.length === 0) return e;
  let s = !1, a = e;
  for (const c of r) {
    const u = c.activities.map((l) => {
      const d = Jc(l, t, n, o);
      return d !== l && (s = !0), d;
    });
    s && (a = Ft(a, c, u));
  }
  return s ? a : e;
}
function Ft(e, t, n) {
  if (!e.structure) return e;
  const o = t.cardinality === "single" ? n[0] ?? null : n;
  if (t.collectionProperty && t.childProperty) {
    const r = e.structure.payload[t.collectionProperty];
    if (!Array.isArray(r)) return e;
    const s = $f(r, t);
    if (s < 0) return e;
    const a = r[s];
    if (!Qe(a)) return e;
    const c = [...r];
    return c[s] = {
      ...a,
      [t.childProperty]: o
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
        [t.property]: o
      }
    }
  };
}
function Rf(e, t, n, o = []) {
  const r = new Map(e.slot.activities.map((a) => [a.nodeId, a]));
  for (const a of o)
    r.set(a.nodeId, a);
  const s = t.map((a) => r.get(a.id)).filter((a) => !!a);
  return e.slot.mode === "sequence" && s.sort((a, c) => {
    const u = t.find((d) => d.id === a.nodeId), l = t.find((d) => d.id === c.nodeId);
    return (u?.position.x ?? 0) - (l?.position.x ?? 0);
  }), Ft(e.owner, e.slot, s);
}
function Lf(e, t) {
  return {
    ...e,
    structure: Ff(e.structure, t)
  };
}
function zf(e, t) {
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
function wi(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: tl(e)
  };
}
function Qc(e, t) {
  if (!e) return null;
  const n = Uc(e, t), o = e.structure ?? (n ? tl(n) : null);
  let r = o === e.structure ? e : { ...e, structure: o };
  const s = De(r, t);
  for (const a of s) {
    const c = a.activities.map((u) => Qc(u, t) ?? u);
    c.some((u, l) => u !== a.activities[l]) && (r = Ft(r, a, c));
  }
  return r;
}
function Ce(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? Wf(t) : n;
}
function el(e, t, n, o = {}) {
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
      category: t?.category,
      executionType: t?.executionType,
      icon: Qo(t),
      childSlots: De(e, t),
      acceptsInbound: Kf(e, t),
      sourcePorts: o.suppressFlowPorts ? [] : ol(e, t),
      suppressFlowPorts: o.suppressFlowPorts
    }
  };
}
function Qo(e) {
  if (!e) return "activity";
  const t = Vf(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), o = Ce(e).toLowerCase(), r = e.category?.toLowerCase() ?? "", s = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || o === "flowchart" ? "flowchart" : n.endsWith(".sequence") || o === "sequence" ? "sequence" : n.includes("writeline") || o.includes("write line") ? "terminal" : r.includes("runtime") ? "runtime" : s === "trigger" ? "trigger" : "activity";
}
function Vf(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function Of(e) {
  return !!e && (Ce(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function Hf(e) {
  return !!e && (Ce(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function Wf(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function tl(e) {
  const t = Ki(e);
  return t ? {
    kind: t.kind,
    schemaVersion: t.schemaVersion,
    payload: Uf(t.payload.initialPayload)
  } : e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: Rn,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: en,
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
function Ff(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], o = /* @__PURE__ */ new Map();
  for (const r of n) {
    if (!Qe(r)) continue;
    const s = r.id;
    typeof s == "string" && o.set(s, r);
  }
  return {
    ...e,
    payload: {
      ...e.payload,
      connections: t.map((r) => {
        const s = o.get(r.id) ?? {}, a = r.data?.vertices, { vertices: c, ...u } = s;
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
function Bf(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function nl(e) {
  if (e.structure?.kind !== en) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, s = n.target;
    if (!r?.nodeId || !s?.nodeId) return null;
    const a = Array.isArray(n.vertices) ? n.vertices.filter(Zf) : [];
    return {
      id: typeof n.id == "string" ? String(n.id) : `flow-${o}-${r.nodeId}-${s.nodeId}`,
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
function ol(e, t) {
  const n = Ls(e.cases);
  if (qf(e, t) && n.length > 0)
    return [...n.map((s) => ({ name: s, displayName: s })), { name: "Default", displayName: "Default" }];
  const o = [
    ...wo(t?.designFacets),
    ...wo(t?.ports),
    ...wo(t?.outputs)
  ];
  if (o.length > 0) return Yf(o);
  const r = Ls(e.outcomes);
  return r.length > 0 ? r.map((s) => ({ name: s, displayName: s })) : [{ name: "Done", displayName: "Done" }];
}
function Kf(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function Po(e, t, n, o) {
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
function Xf(e, t, n) {
  const o = Po(t.source, n, t.sourceHandle ?? "Done", void 0), r = Po(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((s) => s.id !== t.id).concat(o, r);
}
function un(e) {
  return Array.isArray(e) ? e.filter($o) : null;
}
function qf(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function wo(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!Qe(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...wo(n.ports));
      continue;
    }
    const o = typeof n.type == "string" ? n.type : typeof n.portType == "string" ? n.portType : "", r = n.isBrowsable !== !1 && n.browsable !== !1, s = typeof n.name == "string" ? n.name : typeof n.id == "string" ? n.id : "";
    if (r && o.toLowerCase() === "flow" && s) {
      const a = typeof n.displayName == "string" ? n.displayName : s;
      t.push({ name: s, displayName: a });
    }
  }
  return t;
}
function Yf(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function Ls(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function Mt(e) {
  return typeof e == "string" && e.length > 0 ? e : void 0;
}
function Uf(e) {
  return JSON.parse(JSON.stringify(e));
}
function zs(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const o of e) {
    const r = t(o);
    r && n.set(r, [...n.get(r) ?? [], o]);
  }
  return n;
}
function rl(e) {
  return [...e].sort((t, n) => Vs(n).localeCompare(Vs(t)))[0];
}
function Vs(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function Zf(e) {
  return Qe(e) && typeof e.x == "number" && typeof e.y == "number";
}
function Qe(e) {
  return typeof e == "object" && e !== null;
}
function $o(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Gf(e) {
  return e.kind === Rn ? "sequence" : e.kind === en ? "flowchart" : "generic";
}
function Jf(e) {
  return e.kind === Rn || e.kind === en, "Activities";
}
function Qf(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function ep(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Mo = "workflow", tp = /* @__PURE__ */ new Set([Rn, en]);
function np(e, t) {
  const n = e?.structure?.kind;
  if (!n) return !1;
  if (tp.has(n)) return !0;
  const o = t?.activityVersionId === e?.activityVersionId ? Ki(t) : null;
  return o?.kind === n && o.payload.supportsScopedVariables;
}
function il(e) {
  const t = e?.structure?.payload?.variables;
  return Array.isArray(t) ? t.filter(To) : [];
}
function op(e, t) {
  return e.structure ? {
    ...e,
    structure: {
      ...e.structure,
      payload: { ...e.structure.payload, variables: t }
    }
  } : e;
}
function rp(e, t) {
  return {
    referenceKey: e,
    declaringScopeId: t && t !== Mo ? t : Mo
  };
}
function sl(e) {
  if (e && typeof e == "object") {
    const t = e;
    return typeof t.referenceKey == "string" && t.referenceKey ? { referenceKey: t.referenceKey, declaringScopeId: typeof t.declaringScopeId == "string" ? t.declaringScopeId : null } : null;
  }
  if (typeof e == "string") {
    const t = e.trim();
    if (t.startsWith("{"))
      try {
        return sl(JSON.parse(t));
      } catch {
      }
    return t ? { referenceKey: t, declaringScopeId: null } : null;
  }
  return null;
}
function ip(e, t) {
  if (!e) return "";
  const n = [`workflow:${Os(e.variables)}`], o = (r) => {
    const s = De(r, t), a = s.flatMap((c) => c.activities.map((u) => u.nodeId));
    n.push(`${r.nodeId}:${Os(il(r))}>${a.join(",")}`), s.forEach((c) => c.activities.forEach(o));
  };
  return e.rootActivity && o(e.rootActivity), n.join(";");
}
function Os(e) {
  return (e ?? []).map((t) => `${t.referenceKey}=${t.name}`).join(",");
}
function sp(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const o of e ?? [])
    o.scopeId === t && n.set(o.referenceKey, `Shadows "${o.name}" declared in an outer scope.`);
  return n;
}
const Ne = "/_elsa/workflow-management", ap = "/publishing", vn = {
  activities: ["workflows", "activities"],
  activityAvailabilitySettings: ["workflows", "activity-availability", "settings"],
  activityAvailabilityDiagnostics: ["workflows", "activity-availability", "diagnostics"]
};
function cp(e) {
  return Vc({
    queryKey: vn.activityAvailabilitySettings,
    queryFn: () => Ip(e)
  });
}
function lp(e) {
  return Vc({
    queryKey: vn.activityAvailabilityDiagnostics,
    queryFn: () => ll(e)
  });
}
function up(e) {
  const t = qd();
  return Yd({
    mutationFn: (n) => kp(e, n),
    onSuccess: () => {
      t.invalidateQueries({ queryKey: vn.activityAvailabilitySettings }), t.invalidateQueries({ queryKey: vn.activityAvailabilityDiagnostics }), t.invalidateQueries({ queryKey: vn.activities });
    }
  });
}
async function dp(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${Ne}/definitions?${n.toString()}`);
}
async function fp(e, t) {
  const n = await e.http.getJson(`${Ne}/definitions/${encodeURIComponent(t)}`);
  return n.draft ? { ...n, draft: { ...n.draft, state: Jo(n.draft.state) } } : n;
}
async function pp(e, t, n) {
  const o = await e.http.postJson(
    `${Ne}/design/scoped-variables/analyze`,
    { state: Mn(t), nodeId: n }
  );
  return {
    visibleVariables: Array.isArray(o?.visibleVariables) ? o.visibleVariables : [],
    shadowingWarnings: Array.isArray(o?.shadowingWarnings) ? o.shadowingWarnings : []
  };
}
const Qr = (e) => ({ visibleVariables: [], shadowingWarnings: [], status: e });
function hp(e, t, n, o) {
  const r = fe(() => ip(t, o), [o, t]), [s, a] = B(() => Qr("loading"));
  return J(() => {
    if (!t) {
      a(Qr("unavailable"));
      return;
    }
    let c = !1;
    return a((u) => ({ ...u, status: "loading" })), pp(e, t, n).then(
      (u) => {
        c || a({ ...u, status: "ready" });
      },
      () => {
        c || a(Qr("unavailable"));
      }
    ), () => {
      c = !0;
    };
  }, [e, n, r]), s;
}
async function gp(e, t) {
  const n = await e.http.getJson(`${Ne}/versions/${encodeURIComponent(t)}`);
  return { ...n, state: Jo(n.state) };
}
async function yp(e, t) {
  return e.http.postJson(`${Ne}/definitions`, t);
}
async function mp(e, t) {
  await e.http.deleteJson(`${Ne}/definitions/${encodeURIComponent(t)}`);
}
async function xp(e, t) {
  await e.http.postJson(`${Ne}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function wp(e, t) {
  await e.http.deleteJson(`${Ne}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function vp(e, t, n) {
  return e.http.requestJson(
    `${Ne}/definitions/${encodeURIComponent(t)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(n)
    }
  );
}
async function bp(e, t) {
  const n = await e.http.putJson(
    `${Ne}/drafts/${encodeURIComponent(t.id)}`,
    { state: Mn(t.state), layout: t.layout }
  );
  return { ...n, state: Jo(n.state) };
}
async function Np(e, t) {
  return e.http.postJson(`${Ne}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function jp(e, t) {
  return e.http.postJson(`${Ne}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function Sp(e, t) {
  const n = { ...t, state: Mn(t.state) };
  try {
    return await e.http.postJson(`${ap}/workflows/drafts/test-runs`, n);
  } catch (o) {
    const r = Mp(o);
    if (r) return r;
    throw o;
  }
}
async function al(e, t) {
  return e.http.postJson(`${Ne}/executables/${encodeURIComponent(t)}/run`, {});
}
async function cl(e) {
  return e.http.getJson("/_demo/workflows/executables");
}
async function Cp(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.runKind && n.set("runKind", t.runKind), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const o = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${o ? `?${o}` : ""}`);
}
async function Ep(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function qi(e) {
  return e.http.getJson(`${Ne}/activities`);
}
async function Ip(e) {
  return e.http.getJson(`${Ne}/activities/availability/settings`);
}
async function kp(e, t) {
  return e.http.putJson(`${Ne}/activities/availability/settings`, t);
}
async function ll(e) {
  return e.http.getJson(`${Ne}/activities/availability/diagnostics`);
}
async function Ap(e) {
  const t = await er(e, [
    `${Ne}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? Hs(t) : Hs(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function _p(e) {
  const t = await er(e, [
    `${Ne}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : vo;
}
async function Dp(e) {
  const t = await er(e, [
    `${Ne}/descriptors/variables`,
    "/descriptors/variables"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((o) => Tp(o));
}
function Tp(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = typeof t.alias == "string" && t.alias.length > 0, o = typeof t.typeName == "string" && t.typeName.length > 0;
  return n || o;
}
async function Pp(e) {
  const t = await er(e, [
    `${Ne}/descriptors/storage-drivers`,
    "/descriptors/storage-drivers"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((o) => $p(o));
}
function $p(e) {
  return !!e && typeof e == "object" && typeof e.typeName == "string" && e.typeName.length > 0;
}
async function er(e, t) {
  let n;
  for (const o of t)
    try {
      return await e.http.getJson(o);
    } catch (r) {
      n = r;
    }
  throw n;
}
function Hs(e) {
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
function Mp(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = Ws(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return Ws(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function Ws(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const vo = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
], Rp = [
  "Available",
  "BlockedByHostBaseline",
  "HiddenByManagementSettings",
  "RemovedFromCatalog",
  "UnresolvedReference"
], Lp = {
  Available: "Available",
  BlockedByHostBaseline: "Host blocked",
  HiddenByManagementSettings: "Management hidden",
  RemovedFromCatalog: "Removed",
  UnresolvedReference: "Unresolved"
};
function xt(e) {
  return typeof e == "string" ? e : typeof e == "number" ? Rp[e] ?? "Available" : "Available";
}
function Ro(e) {
  const t = xt(e);
  return Lp[t] ?? t;
}
function zp(e) {
  return xt(e).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Vp(e) {
  return xt(e) !== "Available";
}
function Op(e) {
  return e === "Only" || e === 1 ? "Only" : "AllExcept";
}
function Hp(e) {
  return e === "Only" ? 1 : 0;
}
function Fs(e) {
  const t = e?.rules;
  return {
    mode: Op(e?.mode),
    activityTypes: t?.activityTypes ?? [],
    sets: t?.sets ?? []
  };
}
function Wp(e) {
  return e.referenceKind === 0 || e.referenceKind === "ActivityType";
}
function Fp(e) {
  return [...e?.items ?? []].filter(Wp).filter((t) => t.activityTypeKey && t.activityDefinitionId).sort((t, n) => Lo(t).localeCompare(Lo(n)));
}
function Bp(e) {
  return [...e?.items ?? []].filter((t) => {
    const n = xt(t.state);
    return n === "RemovedFromCatalog" || n === "UnresolvedReference";
  }).sort((t, n) => (t.referenceName ?? "").localeCompare(n.referenceName ?? ""));
}
function Bs(e, t) {
  return e.includes(t) ? e.filter((n) => n !== t) : [...e, t].sort((n, o) => n.localeCompare(o));
}
function Lo(e) {
  const t = e?.displayName?.trim();
  if (t) return t;
  const n = e?.activityTypeKey?.split(".").filter(Boolean).at(-1) ?? "";
  return Kp(n) || e?.activityTypeKey || "Activity";
}
function Kp(e) {
  return e.replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").replace(/([a-z0-9])([A-Z])/g, "$1 $2").trim();
}
function Xp(e, t) {
  const n = new Set(e.filter((o) => !!o));
  return (t?.items ?? []).find((o) => Vp(o.state) ? [o.activityDefinitionId, o.activityTypeKey, o.referenceName].some((r) => r && n.has(r)) : !1) ?? null;
}
function qp({ context: e }) {
  const t = cp(e), n = lp(e), o = up(e), r = t.data ?? null, s = n.data ?? null, a = t.isLoading || n.isLoading, c = o.isPending, [u, l] = B(() => Fs(r)), [d, f] = B(""), [p, h] = B(null);
  J(() => {
    l(Fs(r));
  }, [r]);
  const g = fe(() => Fp(s), [s]), m = fe(() => Bp(s), [s]), w = s?.sets ?? [], x = fe(() => {
    const _ = d.trim().toLowerCase();
    return _ ? g.filter(
      (R) => Lo(R).toLowerCase().includes(_) || (R.activityTypeKey ?? "").toLowerCase().includes(_)
    ) : g;
  }, [g, d]), b = new Set(u.activityTypes), y = new Set(u.sets), v = g.filter((_) => xt(_.state) === "BlockedByHostBaseline").length, j = g.filter((_) => xt(_.state) === "HiddenByManagementSettings").length, N = o.error ?? t.error ?? n.error, S = N instanceof Error ? N.message : N ? "Activity availability could not be loaded." : null, E = (_) => l((R) => ({ ...R, mode: _ })), D = (_) => l((R) => ({ ...R, activityTypes: Bs(R.activityTypes, _) })), L = (_) => l((R) => ({ ...R, sets: Bs(R.sets, _) })), A = () => {
    h(null), o.mutate(
      {
        scope: r?.scope ?? "host-default",
        mode: Hp(u.mode),
        rules: { activityTypes: u.activityTypes, sets: u.sets }
      },
      { onSuccess: () => h("Activity availability saved.") }
    );
  };
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-page availability-page", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-page-header", children: [
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("div", { className: "wf-kicker", children: "Workflows" }),
        /* @__PURE__ */ i.jsxs("h2", { children: [
          /* @__PURE__ */ i.jsx(Rd, { size: 18 }),
          " Activity availability"
        ] }),
        /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "Control which activities can be added to new workflows. Host baseline rules always take precedence." })
      ] }),
      /* @__PURE__ */ i.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ i.jsxs("button", { type: "button", className: "availability-save", onClick: A, disabled: a || c, children: [
        /* @__PURE__ */ i.jsx(Dc, { size: 15 }),
        c ? "Saving…" : "Save"
      ] }) })
    ] }),
    /* @__PURE__ */ i.jsxs("div", { className: "availability-body", children: [
      S && /* @__PURE__ */ i.jsx("div", { className: "availability-banner availability-banner-error", children: S }),
      p && !S && /* @__PURE__ */ i.jsx("div", { className: "availability-banner availability-banner-success", children: p }),
      /* @__PURE__ */ i.jsxs("div", { className: "availability-mode", role: "group", "aria-label": "Activity availability mode", children: [
        /* @__PURE__ */ i.jsxs("button", { type: "button", className: u.mode === "AllExcept" ? "active" : "", onClick: () => E("AllExcept"), disabled: a || c, children: [
          /* @__PURE__ */ i.jsx(Is, { size: 15 }),
          /* @__PURE__ */ i.jsxs("span", { children: [
            /* @__PURE__ */ i.jsx("strong", { children: "All except" }),
            /* @__PURE__ */ i.jsx("em", { children: "Show everything except the selected activities" })
          ] })
        ] }),
        /* @__PURE__ */ i.jsxs("button", { type: "button", className: u.mode === "Only" ? "active" : "", onClick: () => E("Only"), disabled: a || c, children: [
          /* @__PURE__ */ i.jsx(ks, { size: 15 }),
          /* @__PURE__ */ i.jsxs("span", { children: [
            /* @__PURE__ */ i.jsx("strong", { children: "Only" }),
            /* @__PURE__ */ i.jsx("em", { children: "Show only the selected activities" })
          ] })
        ] })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { className: "availability-counts", children: [
        /* @__PURE__ */ i.jsxs("span", { children: [
          /* @__PURE__ */ i.jsx(ks, { size: 14 }),
          " ",
          v,
          " host blocked"
        ] }),
        /* @__PURE__ */ i.jsxs("span", { children: [
          /* @__PURE__ */ i.jsx(Is, { size: 14 }),
          " ",
          j,
          " management hidden"
        ] }),
        /* @__PURE__ */ i.jsxs("span", { children: [
          /* @__PURE__ */ i.jsx(Ao, { size: 14 }),
          " ",
          m.length,
          " unresolved"
        ] })
      ] }),
      w.length > 0 && /* @__PURE__ */ i.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ i.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ i.jsx(Hi, { size: 14 }),
          " Sets"
        ] }),
        /* @__PURE__ */ i.jsx("div", { className: "availability-set-list", children: w.map((_) => /* @__PURE__ */ i.jsxs("label", { className: "availability-set-option", children: [
          /* @__PURE__ */ i.jsx("input", { type: "checkbox", checked: y.has(_.name), disabled: a || c, onChange: () => L(_.name) }),
          /* @__PURE__ */ i.jsx("span", { children: _.name }),
          /* @__PURE__ */ i.jsx("code", { children: (_.activityTypeKeys ?? []).length })
        ] }, _.name)) })
      ] }),
      /* @__PURE__ */ i.jsxs("section", { className: "availability-section availability-section-grow", children: [
        /* @__PURE__ */ i.jsxs("div", { className: "availability-section-head", children: [
          /* @__PURE__ */ i.jsxs("h3", { className: "wf-section-label", children: [
            /* @__PURE__ */ i.jsx(Tc, { size: 14 }),
            " Activities"
          ] }),
          /* @__PURE__ */ i.jsxs("div", { className: "wf-search availability-search", children: [
            /* @__PURE__ */ i.jsx(Uo, { size: 14 }),
            /* @__PURE__ */ i.jsx("input", { type: "search", value: d, placeholder: "Filter activities…", onChange: (_) => f(_.target.value) })
          ] })
        ] }),
        /* @__PURE__ */ i.jsxs("div", { className: "availability-activity-list", children: [
          a && g.length === 0 && /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "Loading availability…" }),
          !a && g.length === 0 && /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "No availability diagnostics reported." }),
          !a && g.length > 0 && x.length === 0 && /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "No activities match the filter." }),
          x.map((_) => {
            const C = xt(_.state) === "BlockedByHostBaseline", I = _.activityTypeKey ?? _.activityDefinitionId ?? "";
            return /* @__PURE__ */ i.jsxs("label", { className: `availability-activity-option ${C ? "disabled" : ""}`, children: [
              /* @__PURE__ */ i.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: b.has(I),
                  disabled: a || c || C,
                  onChange: () => D(I)
                }
              ),
              /* @__PURE__ */ i.jsxs("span", { className: "availability-activity-main", children: [
                /* @__PURE__ */ i.jsx("strong", { children: Lo(_) }),
                /* @__PURE__ */ i.jsx("code", { children: _.activityTypeKey })
              ] }),
              /* @__PURE__ */ i.jsx("em", { className: `availability-state ${zp(_.state)}`, children: Ro(_.state) })
            ] }, I);
          })
        ] })
      ] }),
      m.length > 0 && /* @__PURE__ */ i.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ i.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ i.jsx(Ao, { size: 14 }),
          " Unresolved references"
        ] }),
        /* @__PURE__ */ i.jsx("div", { className: "availability-unresolved-list", children: m.map((_) => /* @__PURE__ */ i.jsxs("span", { children: [
          /* @__PURE__ */ i.jsx("strong", { children: _.referenceName }),
          /* @__PURE__ */ i.jsx("em", { children: Ro(_.state) })
        ] }, `${_.layer}-${_.referenceKind}-${_.referenceName}`)) })
      ] })
    ] })
  ] });
}
function Se(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, o; n < e.length; n++)
      (o = Se(e[n])) !== "" && (t += (t && " ") + o);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var Yp = { value: () => {
} };
function tr() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new bo(n);
}
function bo(e) {
  this._ = e;
}
function Up(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
bo.prototype = tr.prototype = {
  constructor: bo,
  on: function(e, t) {
    var n = this._, o = Up(e + "", n), r, s = -1, a = o.length;
    if (arguments.length < 2) {
      for (; ++s < a; ) if ((r = (e = o[s]).type) && (r = Zp(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++s < a; )
      if (r = (e = o[s]).type) n[r] = Ks(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = Ks(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new bo(e);
  },
  call: function(e, t) {
    if ((r = arguments.length - 2) > 0) for (var n = new Array(r), o = 0, r, s; o < r; ++o) n[o] = arguments[o + 2];
    if (!this._.hasOwnProperty(e)) throw new Error("unknown type: " + e);
    for (s = this._[e], o = 0, r = s.length; o < r; ++o) s[o].value.apply(t, n);
  },
  apply: function(e, t, n) {
    if (!this._.hasOwnProperty(e)) throw new Error("unknown type: " + e);
    for (var o = this._[e], r = 0, s = o.length; r < s; ++r) o[r].value.apply(t, n);
  }
};
function Zp(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function Ks(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = Yp, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var vi = "http://www.w3.org/1999/xhtml";
const Xs = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: vi,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function nr(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Xs.hasOwnProperty(t) ? { space: Xs[t], local: e } : e;
}
function Gp(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === vi && t.documentElement.namespaceURI === vi ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Jp(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function ul(e) {
  var t = nr(e);
  return (t.local ? Jp : Gp)(t);
}
function Qp() {
}
function Yi(e) {
  return e == null ? Qp : function() {
    return this.querySelector(e);
  };
}
function eh(e) {
  typeof e != "function" && (e = Yi(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = o[r] = new Array(a), u, l, d = 0; d < a; ++d)
      (u = s[d]) && (l = e.call(u, u.__data__, d, s)) && ("__data__" in u && (l.__data__ = u.__data__), c[d] = l);
  return new Ve(o, this._parents);
}
function th(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function nh() {
  return [];
}
function dl(e) {
  return e == null ? nh : function() {
    return this.querySelectorAll(e);
  };
}
function oh(e) {
  return function() {
    return th(e.apply(this, arguments));
  };
}
function rh(e) {
  typeof e == "function" ? e = oh(e) : e = dl(e);
  for (var t = this._groups, n = t.length, o = [], r = [], s = 0; s < n; ++s)
    for (var a = t[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && (o.push(e.call(u, u.__data__, l, a)), r.push(u));
  return new Ve(o, r);
}
function fl(e) {
  return function() {
    return this.matches(e);
  };
}
function pl(e) {
  return function(t) {
    return t.matches(e);
  };
}
var ih = Array.prototype.find;
function sh(e) {
  return function() {
    return ih.call(this.children, e);
  };
}
function ah() {
  return this.firstElementChild;
}
function ch(e) {
  return this.select(e == null ? ah : sh(typeof e == "function" ? e : pl(e)));
}
var lh = Array.prototype.filter;
function uh() {
  return Array.from(this.children);
}
function dh(e) {
  return function() {
    return lh.call(this.children, e);
  };
}
function fh(e) {
  return this.selectAll(e == null ? uh : dh(typeof e == "function" ? e : pl(e)));
}
function ph(e) {
  typeof e != "function" && (e = fl(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = o[r] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new Ve(o, this._parents);
}
function hl(e) {
  return new Array(e.length);
}
function hh() {
  return new Ve(this._enter || this._groups.map(hl), this._parents);
}
function zo(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
zo.prototype = {
  constructor: zo,
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
function gh(e) {
  return function() {
    return e;
  };
}
function yh(e, t, n, o, r, s) {
  for (var a = 0, c, u = t.length, l = s.length; a < l; ++a)
    (c = t[a]) ? (c.__data__ = s[a], o[a] = c) : n[a] = new zo(e, s[a]);
  for (; a < u; ++a)
    (c = t[a]) && (r[a] = c);
}
function mh(e, t, n, o, r, s, a) {
  var c, u, l = /* @__PURE__ */ new Map(), d = t.length, f = s.length, p = new Array(d), h;
  for (c = 0; c < d; ++c)
    (u = t[c]) && (p[c] = h = a.call(u, u.__data__, c, t) + "", l.has(h) ? r[c] = u : l.set(h, u));
  for (c = 0; c < f; ++c)
    h = a.call(e, s[c], c, s) + "", (u = l.get(h)) ? (o[c] = u, u.__data__ = s[c], l.delete(h)) : n[c] = new zo(e, s[c]);
  for (c = 0; c < d; ++c)
    (u = t[c]) && l.get(p[c]) === u && (r[c] = u);
}
function xh(e) {
  return e.__data__;
}
function wh(e, t) {
  if (!arguments.length) return Array.from(this, xh);
  var n = t ? mh : yh, o = this._parents, r = this._groups;
  typeof e != "function" && (e = gh(e));
  for (var s = r.length, a = new Array(s), c = new Array(s), u = new Array(s), l = 0; l < s; ++l) {
    var d = o[l], f = r[l], p = f.length, h = vh(e.call(d, d && d.__data__, l, o)), g = h.length, m = c[l] = new Array(g), w = a[l] = new Array(g), x = u[l] = new Array(p);
    n(d, f, m, w, x, h, t);
    for (var b = 0, y = 0, v, j; b < g; ++b)
      if (v = m[b]) {
        for (b >= y && (y = b + 1); !(j = w[y]) && ++y < g; ) ;
        v._next = j || null;
      }
  }
  return a = new Ve(a, o), a._enter = c, a._exit = u, a;
}
function vh(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function bh() {
  return new Ve(this._exit || this._groups.map(hl), this._parents);
}
function Nh(e, t, n) {
  var o = this.enter(), r = this, s = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? s.remove() : n(s), o && r ? o.merge(r).order() : r;
}
function jh(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, s = o.length, a = Math.min(r, s), c = new Array(r), u = 0; u < a; ++u)
    for (var l = n[u], d = o[u], f = l.length, p = c[u] = new Array(f), h, g = 0; g < f; ++g)
      (h = l[g] || d[g]) && (p[g] = h);
  for (; u < r; ++u)
    c[u] = n[u];
  return new Ve(c, this._parents);
}
function Sh() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, s = o[r], a; --r >= 0; )
      (a = o[r]) && (s && a.compareDocumentPosition(s) ^ 4 && s.parentNode.insertBefore(a, s), s = a);
  return this;
}
function Ch(e) {
  e || (e = Eh);
  function t(f, p) {
    return f && p ? e(f.__data__, p.__data__) : !f - !p;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), s = 0; s < o; ++s) {
    for (var a = n[s], c = a.length, u = r[s] = new Array(c), l, d = 0; d < c; ++d)
      (l = a[d]) && (u[d] = l);
    u.sort(t);
  }
  return new Ve(r, this._parents).order();
}
function Eh(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function Ih() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function kh() {
  return Array.from(this);
}
function Ah() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, s = o.length; r < s; ++r) {
      var a = o[r];
      if (a) return a;
    }
  return null;
}
function _h() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function Dh() {
  return !this.node();
}
function Th(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], s = 0, a = r.length, c; s < a; ++s)
      (c = r[s]) && e.call(c, c.__data__, s, r);
  return this;
}
function Ph(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function $h(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Mh(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function Rh(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function Lh(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function zh(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function Vh(e, t) {
  var n = nr(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? $h : Ph : typeof t == "function" ? n.local ? zh : Lh : n.local ? Rh : Mh)(n, t));
}
function gl(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function Oh(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Hh(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function Wh(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function Fh(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? Oh : typeof t == "function" ? Wh : Hh)(e, t, n ?? "")) : Bt(this.node(), e);
}
function Bt(e, t) {
  return e.style.getPropertyValue(t) || gl(e).getComputedStyle(e, null).getPropertyValue(t);
}
function Bh(e) {
  return function() {
    delete this[e];
  };
}
function Kh(e, t) {
  return function() {
    this[e] = t;
  };
}
function Xh(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function qh(e, t) {
  return arguments.length > 1 ? this.each((t == null ? Bh : typeof t == "function" ? Xh : Kh)(e, t)) : this.node()[e];
}
function yl(e) {
  return e.trim().split(/^|\s+/);
}
function Ui(e) {
  return e.classList || new ml(e);
}
function ml(e) {
  this._node = e, this._names = yl(e.getAttribute("class") || "");
}
ml.prototype = {
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
function xl(e, t) {
  for (var n = Ui(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function wl(e, t) {
  for (var n = Ui(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function Yh(e) {
  return function() {
    xl(this, e);
  };
}
function Uh(e) {
  return function() {
    wl(this, e);
  };
}
function Zh(e, t) {
  return function() {
    (t.apply(this, arguments) ? xl : wl)(this, e);
  };
}
function Gh(e, t) {
  var n = yl(e + "");
  if (arguments.length < 2) {
    for (var o = Ui(this.node()), r = -1, s = n.length; ++r < s; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? Zh : t ? Yh : Uh)(n, t));
}
function Jh() {
  this.textContent = "";
}
function Qh(e) {
  return function() {
    this.textContent = e;
  };
}
function eg(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function tg(e) {
  return arguments.length ? this.each(e == null ? Jh : (typeof e == "function" ? eg : Qh)(e)) : this.node().textContent;
}
function ng() {
  this.innerHTML = "";
}
function og(e) {
  return function() {
    this.innerHTML = e;
  };
}
function rg(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function ig(e) {
  return arguments.length ? this.each(e == null ? ng : (typeof e == "function" ? rg : og)(e)) : this.node().innerHTML;
}
function sg() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function ag() {
  return this.each(sg);
}
function cg() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function lg() {
  return this.each(cg);
}
function ug(e) {
  var t = typeof e == "function" ? e : ul(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function dg() {
  return null;
}
function fg(e, t) {
  var n = typeof e == "function" ? e : ul(e), o = t == null ? dg : typeof t == "function" ? t : Yi(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function pg() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function hg() {
  return this.each(pg);
}
function gg() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function yg() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function mg(e) {
  return this.select(e ? yg : gg);
}
function xg(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function wg(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function vg(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function bg(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, s; n < r; ++n)
        s = t[n], (!e.type || s.type === e.type) && s.name === e.name ? this.removeEventListener(s.type, s.listener, s.options) : t[++o] = s;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function Ng(e, t, n) {
  return function() {
    var o = this.__on, r, s = wg(t);
    if (o) {
      for (var a = 0, c = o.length; a < c; ++a)
        if ((r = o[a]).type === e.type && r.name === e.name) {
          this.removeEventListener(r.type, r.listener, r.options), this.addEventListener(r.type, r.listener = s, r.options = n), r.value = t;
          return;
        }
    }
    this.addEventListener(e.type, s, n), r = { type: e.type, name: e.name, value: t, listener: s, options: n }, o ? o.push(r) : this.__on = [r];
  };
}
function jg(e, t, n) {
  var o = vg(e + ""), r, s = o.length, a;
  if (arguments.length < 2) {
    var c = this.node().__on;
    if (c) {
      for (var u = 0, l = c.length, d; u < l; ++u)
        for (r = 0, d = c[u]; r < s; ++r)
          if ((a = o[r]).type === d.type && a.name === d.name)
            return d.value;
    }
    return;
  }
  for (c = t ? Ng : bg, r = 0; r < s; ++r) this.each(c(o[r], t, n));
  return this;
}
function vl(e, t, n) {
  var o = gl(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function Sg(e, t) {
  return function() {
    return vl(this, e, t);
  };
}
function Cg(e, t) {
  return function() {
    return vl(this, e, t.apply(this, arguments));
  };
}
function Eg(e, t) {
  return this.each((typeof t == "function" ? Cg : Sg)(e, t));
}
function* Ig() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, s = o.length, a; r < s; ++r)
      (a = o[r]) && (yield a);
}
var bl = [null];
function Ve(e, t) {
  this._groups = e, this._parents = t;
}
function Ln() {
  return new Ve([[document.documentElement]], bl);
}
function kg() {
  return this;
}
Ve.prototype = Ln.prototype = {
  constructor: Ve,
  select: eh,
  selectAll: rh,
  selectChild: ch,
  selectChildren: fh,
  filter: ph,
  data: wh,
  enter: hh,
  exit: bh,
  join: Nh,
  merge: jh,
  selection: kg,
  order: Sh,
  sort: Ch,
  call: Ih,
  nodes: kh,
  node: Ah,
  size: _h,
  empty: Dh,
  each: Th,
  attr: Vh,
  style: Fh,
  property: qh,
  classed: Gh,
  text: tg,
  html: ig,
  raise: ag,
  lower: lg,
  append: ug,
  insert: fg,
  remove: hg,
  clone: mg,
  datum: xg,
  on: jg,
  dispatch: Eg,
  [Symbol.iterator]: Ig
};
function Re(e) {
  return typeof e == "string" ? new Ve([[document.querySelector(e)]], [document.documentElement]) : new Ve([[e]], bl);
}
function Ag(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Fe(e, t) {
  if (e = Ag(e), t === void 0 && (t = e.currentTarget), t) {
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
const _g = { passive: !1 }, Cn = { capture: !0, passive: !1 };
function ei(e) {
  e.stopImmediatePropagation();
}
function zt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Nl(e) {
  var t = e.document.documentElement, n = Re(e).on("dragstart.drag", zt, Cn);
  "onselectstart" in t ? n.on("selectstart.drag", zt, Cn) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function jl(e, t) {
  var n = e.document.documentElement, o = Re(e).on("dragstart.drag", null);
  t && (o.on("click.drag", zt, Cn), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const so = (e) => () => e;
function bi(e, {
  sourceEvent: t,
  subject: n,
  target: o,
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
    target: { value: o, enumerable: !0, configurable: !0 },
    identifier: { value: r, enumerable: !0, configurable: !0 },
    active: { value: s, enumerable: !0, configurable: !0 },
    x: { value: a, enumerable: !0, configurable: !0 },
    y: { value: c, enumerable: !0, configurable: !0 },
    dx: { value: u, enumerable: !0, configurable: !0 },
    dy: { value: l, enumerable: !0, configurable: !0 },
    _: { value: d }
  });
}
bi.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function Dg(e) {
  return !e.ctrlKey && !e.button;
}
function Tg() {
  return this.parentNode;
}
function Pg(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function $g() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Sl() {
  var e = Dg, t = Tg, n = Pg, o = $g, r = {}, s = tr("start", "drag", "end"), a = 0, c, u, l, d, f = 0;
  function p(v) {
    v.on("mousedown.drag", h).filter(o).on("touchstart.drag", w).on("touchmove.drag", x, _g).on("touchend.drag touchcancel.drag", b).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function h(v, j) {
    if (!(d || !e.call(this, v, j))) {
      var N = y(this, t.call(this, v, j), v, j, "mouse");
      N && (Re(v.view).on("mousemove.drag", g, Cn).on("mouseup.drag", m, Cn), Nl(v.view), ei(v), l = !1, c = v.clientX, u = v.clientY, N("start", v));
    }
  }
  function g(v) {
    if (zt(v), !l) {
      var j = v.clientX - c, N = v.clientY - u;
      l = j * j + N * N > f;
    }
    r.mouse("drag", v);
  }
  function m(v) {
    Re(v.view).on("mousemove.drag mouseup.drag", null), jl(v.view, l), zt(v), r.mouse("end", v);
  }
  function w(v, j) {
    if (e.call(this, v, j)) {
      var N = v.changedTouches, S = t.call(this, v, j), E = N.length, D, L;
      for (D = 0; D < E; ++D)
        (L = y(this, S, v, j, N[D].identifier, N[D])) && (ei(v), L("start", v, N[D]));
    }
  }
  function x(v) {
    var j = v.changedTouches, N = j.length, S, E;
    for (S = 0; S < N; ++S)
      (E = r[j[S].identifier]) && (zt(v), E("drag", v, j[S]));
  }
  function b(v) {
    var j = v.changedTouches, N = j.length, S, E;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), S = 0; S < N; ++S)
      (E = r[j[S].identifier]) && (ei(v), E("end", v, j[S]));
  }
  function y(v, j, N, S, E, D) {
    var L = s.copy(), A = Fe(D || N, j), _, R, C;
    if ((C = n.call(v, new bi("beforestart", {
      sourceEvent: N,
      target: p,
      identifier: E,
      active: a,
      x: A[0],
      y: A[1],
      dx: 0,
      dy: 0,
      dispatch: L
    }), S)) != null)
      return _ = C.x - A[0] || 0, R = C.y - A[1] || 0, function I(k, T, $) {
        var P = A, F;
        switch (k) {
          case "start":
            r[E] = I, F = a++;
            break;
          case "end":
            delete r[E], --a;
          // falls through
          case "drag":
            A = Fe($ || T, j), F = a;
            break;
        }
        L.call(
          k,
          v,
          new bi(k, {
            sourceEvent: T,
            subject: C,
            target: p,
            identifier: E,
            active: F,
            x: A[0] + _,
            y: A[1] + R,
            dx: A[0] - P[0],
            dy: A[1] - P[1],
            dispatch: L
          }),
          S
        );
      };
  }
  return p.filter = function(v) {
    return arguments.length ? (e = typeof v == "function" ? v : so(!!v), p) : e;
  }, p.container = function(v) {
    return arguments.length ? (t = typeof v == "function" ? v : so(v), p) : t;
  }, p.subject = function(v) {
    return arguments.length ? (n = typeof v == "function" ? v : so(v), p) : n;
  }, p.touchable = function(v) {
    return arguments.length ? (o = typeof v == "function" ? v : so(!!v), p) : o;
  }, p.on = function() {
    var v = s.on.apply(s, arguments);
    return v === s ? p : v;
  }, p.clickDistance = function(v) {
    return arguments.length ? (f = (v = +v) * v, p) : Math.sqrt(f);
  }, p;
}
function Zi(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function Cl(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function zn() {
}
var En = 0.7, Vo = 1 / En, Vt = "\\s*([+-]?\\d+)\\s*", In = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", Ze = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", Mg = /^#([0-9a-f]{3,8})$/, Rg = new RegExp(`^rgb\\(${Vt},${Vt},${Vt}\\)$`), Lg = new RegExp(`^rgb\\(${Ze},${Ze},${Ze}\\)$`), zg = new RegExp(`^rgba\\(${Vt},${Vt},${Vt},${In}\\)$`), Vg = new RegExp(`^rgba\\(${Ze},${Ze},${Ze},${In}\\)$`), Og = new RegExp(`^hsl\\(${In},${Ze},${Ze}\\)$`), Hg = new RegExp(`^hsla\\(${In},${Ze},${Ze},${In}\\)$`), qs = {
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
Zi(zn, bt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Ys,
  // Deprecated! Use color.formatHex.
  formatHex: Ys,
  formatHex8: Wg,
  formatHsl: Fg,
  formatRgb: Us,
  toString: Us
});
function Ys() {
  return this.rgb().formatHex();
}
function Wg() {
  return this.rgb().formatHex8();
}
function Fg() {
  return El(this).formatHsl();
}
function Us() {
  return this.rgb().formatRgb();
}
function bt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = Mg.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? Zs(t) : n === 3 ? new $e(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? ao(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? ao(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = Rg.exec(e)) ? new $e(t[1], t[2], t[3], 1) : (t = Lg.exec(e)) ? new $e(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = zg.exec(e)) ? ao(t[1], t[2], t[3], t[4]) : (t = Vg.exec(e)) ? ao(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = Og.exec(e)) ? Qs(t[1], t[2] / 100, t[3] / 100, 1) : (t = Hg.exec(e)) ? Qs(t[1], t[2] / 100, t[3] / 100, t[4]) : qs.hasOwnProperty(e) ? Zs(qs[e]) : e === "transparent" ? new $e(NaN, NaN, NaN, 0) : null;
}
function Zs(e) {
  return new $e(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function ao(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new $e(e, t, n, o);
}
function Bg(e) {
  return e instanceof zn || (e = bt(e)), e ? (e = e.rgb(), new $e(e.r, e.g, e.b, e.opacity)) : new $e();
}
function Ni(e, t, n, o) {
  return arguments.length === 1 ? Bg(e) : new $e(e, t, n, o ?? 1);
}
function $e(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
Zi($e, Ni, Cl(zn, {
  brighter(e) {
    return e = e == null ? Vo : Math.pow(Vo, e), new $e(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? En : Math.pow(En, e), new $e(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new $e(wt(this.r), wt(this.g), wt(this.b), Oo(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: Gs,
  // Deprecated! Use color.formatHex.
  formatHex: Gs,
  formatHex8: Kg,
  formatRgb: Js,
  toString: Js
}));
function Gs() {
  return `#${mt(this.r)}${mt(this.g)}${mt(this.b)}`;
}
function Kg() {
  return `#${mt(this.r)}${mt(this.g)}${mt(this.b)}${mt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function Js() {
  const e = Oo(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${wt(this.r)}, ${wt(this.g)}, ${wt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Oo(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function wt(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function mt(e) {
  return e = wt(e), (e < 16 ? "0" : "") + e.toString(16);
}
function Qs(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Be(e, t, n, o);
}
function El(e) {
  if (e instanceof Be) return new Be(e.h, e.s, e.l, e.opacity);
  if (e instanceof zn || (e = bt(e)), !e) return new Be();
  if (e instanceof Be) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), s = Math.max(t, n, o), a = NaN, c = s - r, u = (s + r) / 2;
  return c ? (t === s ? a = (n - o) / c + (n < o) * 6 : n === s ? a = (o - t) / c + 2 : a = (t - n) / c + 4, c /= u < 0.5 ? s + r : 2 - s - r, a *= 60) : c = u > 0 && u < 1 ? 0 : a, new Be(a, c, u, e.opacity);
}
function Xg(e, t, n, o) {
  return arguments.length === 1 ? El(e) : new Be(e, t, n, o ?? 1);
}
function Be(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
Zi(Be, Xg, Cl(zn, {
  brighter(e) {
    return e = e == null ? Vo : Math.pow(Vo, e), new Be(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? En : Math.pow(En, e), new Be(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new $e(
      ti(e >= 240 ? e - 240 : e + 120, r, o),
      ti(e, r, o),
      ti(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new Be(ea(this.h), co(this.s), co(this.l), Oo(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Oo(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${ea(this.h)}, ${co(this.s) * 100}%, ${co(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function ea(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function co(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function ti(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const Gi = (e) => () => e;
function qg(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function Yg(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function Ug(e) {
  return (e = +e) == 1 ? Il : function(t, n) {
    return n - t ? Yg(t, n, e) : Gi(isNaN(t) ? n : t);
  };
}
function Il(e, t) {
  var n = t - e;
  return n ? qg(e, n) : Gi(isNaN(e) ? t : e);
}
const Ho = (function e(t) {
  var n = Ug(t);
  function o(r, s) {
    var a = n((r = Ni(r)).r, (s = Ni(s)).r), c = n(r.g, s.g), u = n(r.b, s.b), l = Il(r.opacity, s.opacity);
    return function(d) {
      return r.r = a(d), r.g = c(d), r.b = u(d), r.opacity = l(d), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function Zg(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(s) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - s) + t[r] * s;
    return o;
  };
}
function Gg(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Jg(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), s = new Array(n), a;
  for (a = 0; a < o; ++a) r[a] = bn(e[a], t[a]);
  for (; a < n; ++a) s[a] = t[a];
  return function(c) {
    for (a = 0; a < o; ++a) s[a] = r[a](c);
    return s;
  };
}
function Qg(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(o) {
    return n.setTime(e * (1 - o) + t * o), n;
  };
}
function Ue(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function ey(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = bn(e[r], t[r]) : o[r] = t[r];
  return function(s) {
    for (r in n) o[r] = n[r](s);
    return o;
  };
}
var ji = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, ni = new RegExp(ji.source, "g");
function ty(e) {
  return function() {
    return e;
  };
}
function ny(e) {
  return function(t) {
    return e(t) + "";
  };
}
function kl(e, t) {
  var n = ji.lastIndex = ni.lastIndex = 0, o, r, s, a = -1, c = [], u = [];
  for (e = e + "", t = t + ""; (o = ji.exec(e)) && (r = ni.exec(t)); )
    (s = r.index) > n && (s = t.slice(n, s), c[a] ? c[a] += s : c[++a] = s), (o = o[0]) === (r = r[0]) ? c[a] ? c[a] += r : c[++a] = r : (c[++a] = null, u.push({ i: a, x: Ue(o, r) })), n = ni.lastIndex;
  return n < t.length && (s = t.slice(n), c[a] ? c[a] += s : c[++a] = s), c.length < 2 ? u[0] ? ny(u[0].x) : ty(t) : (t = u.length, function(l) {
    for (var d = 0, f; d < t; ++d) c[(f = u[d]).i] = f.x(l);
    return c.join("");
  });
}
function bn(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? Gi(t) : (n === "number" ? Ue : n === "string" ? (o = bt(t)) ? (t = o, Ho) : kl : t instanceof bt ? Ho : t instanceof Date ? Qg : Gg(t) ? Zg : Array.isArray(t) ? Jg : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? ey : Ue)(e, t);
}
var ta = 180 / Math.PI, Si = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function Al(e, t, n, o, r, s) {
  var a, c, u;
  return (a = Math.sqrt(e * e + t * t)) && (e /= a, t /= a), (u = e * n + t * o) && (n -= e * u, o -= t * u), (c = Math.sqrt(n * n + o * o)) && (n /= c, o /= c, u /= c), e * o < t * n && (e = -e, t = -t, u = -u, a = -a), {
    translateX: r,
    translateY: s,
    rotate: Math.atan2(t, e) * ta,
    skewX: Math.atan(u) * ta,
    scaleX: a,
    scaleY: c
  };
}
var lo;
function oy(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Si : Al(t.a, t.b, t.c, t.d, t.e, t.f);
}
function ry(e) {
  return e == null || (lo || (lo = document.createElementNS("http://www.w3.org/2000/svg", "g")), lo.setAttribute("transform", e), !(e = lo.transform.baseVal.consolidate())) ? Si : (e = e.matrix, Al(e.a, e.b, e.c, e.d, e.e, e.f));
}
function _l(e, t, n, o) {
  function r(l) {
    return l.length ? l.pop() + " " : "";
  }
  function s(l, d, f, p, h, g) {
    if (l !== f || d !== p) {
      var m = h.push("translate(", null, t, null, n);
      g.push({ i: m - 4, x: Ue(l, f) }, { i: m - 2, x: Ue(d, p) });
    } else (f || p) && h.push("translate(" + f + t + p + n);
  }
  function a(l, d, f, p) {
    l !== d ? (l - d > 180 ? d += 360 : d - l > 180 && (l += 360), p.push({ i: f.push(r(f) + "rotate(", null, o) - 2, x: Ue(l, d) })) : d && f.push(r(f) + "rotate(" + d + o);
  }
  function c(l, d, f, p) {
    l !== d ? p.push({ i: f.push(r(f) + "skewX(", null, o) - 2, x: Ue(l, d) }) : d && f.push(r(f) + "skewX(" + d + o);
  }
  function u(l, d, f, p, h, g) {
    if (l !== f || d !== p) {
      var m = h.push(r(h) + "scale(", null, ",", null, ")");
      g.push({ i: m - 4, x: Ue(l, f) }, { i: m - 2, x: Ue(d, p) });
    } else (f !== 1 || p !== 1) && h.push(r(h) + "scale(" + f + "," + p + ")");
  }
  return function(l, d) {
    var f = [], p = [];
    return l = e(l), d = e(d), s(l.translateX, l.translateY, d.translateX, d.translateY, f, p), a(l.rotate, d.rotate, f, p), c(l.skewX, d.skewX, f, p), u(l.scaleX, l.scaleY, d.scaleX, d.scaleY, f, p), l = d = null, function(h) {
      for (var g = -1, m = p.length, w; ++g < m; ) f[(w = p[g]).i] = w.x(h);
      return f.join("");
    };
  };
}
var iy = _l(oy, "px, ", "px)", "deg)"), sy = _l(ry, ", ", ")", ")"), ay = 1e-12;
function na(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function cy(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function ly(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const No = (function e(t, n, o) {
  function r(s, a) {
    var c = s[0], u = s[1], l = s[2], d = a[0], f = a[1], p = a[2], h = d - c, g = f - u, m = h * h + g * g, w, x;
    if (m < ay)
      x = Math.log(p / l) / t, w = function(S) {
        return [
          c + S * h,
          u + S * g,
          l * Math.exp(t * S * x)
        ];
      };
    else {
      var b = Math.sqrt(m), y = (p * p - l * l + o * m) / (2 * l * n * b), v = (p * p - l * l - o * m) / (2 * p * n * b), j = Math.log(Math.sqrt(y * y + 1) - y), N = Math.log(Math.sqrt(v * v + 1) - v);
      x = (N - j) / t, w = function(S) {
        var E = S * x, D = na(j), L = l / (n * b) * (D * ly(t * E + j) - cy(j));
        return [
          c + L * h,
          u + L * g,
          l * D / na(t * E + j)
        ];
      };
    }
    return w.duration = x * 1e3 * t / Math.SQRT2, w;
  }
  return r.rho = function(s) {
    var a = Math.max(1e-3, +s), c = a * a, u = c * c;
    return e(a, c, u);
  }, r;
})(Math.SQRT2, 2, 4);
var Kt = 0, dn = 0, an = 0, Dl = 1e3, Wo, fn, Fo = 0, Nt = 0, or = 0, kn = typeof performance == "object" && performance.now ? performance : Date, Tl = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function Ji() {
  return Nt || (Tl(uy), Nt = kn.now() + or);
}
function uy() {
  Nt = 0;
}
function Bo() {
  this._call = this._time = this._next = null;
}
Bo.prototype = Pl.prototype = {
  constructor: Bo,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? Ji() : +n) + (t == null ? 0 : +t), !this._next && fn !== this && (fn ? fn._next = this : Wo = this, fn = this), this._call = e, this._time = n, Ci();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Ci());
  }
};
function Pl(e, t, n) {
  var o = new Bo();
  return o.restart(e, t, n), o;
}
function dy() {
  Ji(), ++Kt;
  for (var e = Wo, t; e; )
    (t = Nt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --Kt;
}
function oa() {
  Nt = (Fo = kn.now()) + or, Kt = dn = 0;
  try {
    dy();
  } finally {
    Kt = 0, py(), Nt = 0;
  }
}
function fy() {
  var e = kn.now(), t = e - Fo;
  t > Dl && (or -= t, Fo = e);
}
function py() {
  for (var e, t = Wo, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Wo = n);
  fn = e, Ci(o);
}
function Ci(e) {
  if (!Kt) {
    dn && (dn = clearTimeout(dn));
    var t = e - Nt;
    t > 24 ? (e < 1 / 0 && (dn = setTimeout(oa, e - kn.now() - or)), an && (an = clearInterval(an))) : (an || (Fo = kn.now(), an = setInterval(fy, Dl)), Kt = 1, Tl(oa));
  }
}
function ra(e, t, n) {
  var o = new Bo();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var hy = tr("start", "end", "cancel", "interrupt"), gy = [], $l = 0, ia = 1, Ei = 2, jo = 3, sa = 4, Ii = 5, So = 6;
function rr(e, t, n, o, r, s) {
  var a = e.__transition;
  if (!a) e.__transition = {};
  else if (n in a) return;
  yy(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: hy,
    tween: gy,
    time: s.time,
    delay: s.delay,
    duration: s.duration,
    ease: s.ease,
    timer: null,
    state: $l
  });
}
function Qi(e, t) {
  var n = qe(e, t);
  if (n.state > $l) throw new Error("too late; already scheduled");
  return n;
}
function et(e, t) {
  var n = qe(e, t);
  if (n.state > jo) throw new Error("too late; already running");
  return n;
}
function qe(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function yy(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = Pl(s, 0, n.time);
  function s(l) {
    n.state = ia, n.timer.restart(a, n.delay, n.time), n.delay <= l && a(l - n.delay);
  }
  function a(l) {
    var d, f, p, h;
    if (n.state !== ia) return u();
    for (d in o)
      if (h = o[d], h.name === n.name) {
        if (h.state === jo) return ra(a);
        h.state === sa ? (h.state = So, h.timer.stop(), h.on.call("interrupt", e, e.__data__, h.index, h.group), delete o[d]) : +d < t && (h.state = So, h.timer.stop(), h.on.call("cancel", e, e.__data__, h.index, h.group), delete o[d]);
      }
    if (ra(function() {
      n.state === jo && (n.state = sa, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = Ei, n.on.call("start", e, e.__data__, n.index, n.group), n.state === Ei) {
      for (n.state = jo, r = new Array(p = n.tween.length), d = 0, f = -1; d < p; ++d)
        (h = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (r[++f] = h);
      r.length = f + 1;
    }
  }
  function c(l) {
    for (var d = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(u), n.state = Ii, 1), f = -1, p = r.length; ++f < p; )
      r[f].call(e, d);
    n.state === Ii && (n.on.call("end", e, e.__data__, n.index, n.group), u());
  }
  function u() {
    n.state = So, n.timer.stop(), delete o[t];
    for (var l in o) return;
    delete e.__transition;
  }
}
function Co(e, t) {
  var n = e.__transition, o, r, s = !0, a;
  if (n) {
    t = t == null ? null : t + "";
    for (a in n) {
      if ((o = n[a]).name !== t) {
        s = !1;
        continue;
      }
      r = o.state > Ei && o.state < Ii, o.state = So, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[a];
    }
    s && delete e.__transition;
  }
}
function my(e) {
  return this.each(function() {
    Co(this, e);
  });
}
function xy(e, t) {
  var n, o;
  return function() {
    var r = et(this, e), s = r.tween;
    if (s !== n) {
      o = n = s;
      for (var a = 0, c = o.length; a < c; ++a)
        if (o[a].name === t) {
          o = o.slice(), o.splice(a, 1);
          break;
        }
    }
    r.tween = o;
  };
}
function wy(e, t, n) {
  var o, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var s = et(this, e), a = s.tween;
    if (a !== o) {
      r = (o = a).slice();
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
function vy(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = qe(this.node(), n).tween, r = 0, s = o.length, a; r < s; ++r)
      if ((a = o[r]).name === e)
        return a.value;
    return null;
  }
  return this.each((t == null ? xy : wy)(n, e, t));
}
function es(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = et(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return qe(r, o).value[t];
  };
}
function Ml(e, t) {
  var n;
  return (typeof t == "number" ? Ue : t instanceof bt ? Ho : (n = bt(t)) ? (t = n, Ho) : kl)(e, t);
}
function by(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Ny(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function jy(e, t, n) {
  var o, r = n + "", s;
  return function() {
    var a = this.getAttribute(e);
    return a === r ? null : a === o ? s : s = t(o = a, n);
  };
}
function Sy(e, t, n) {
  var o, r = n + "", s;
  return function() {
    var a = this.getAttributeNS(e.space, e.local);
    return a === r ? null : a === o ? s : s = t(o = a, n);
  };
}
function Cy(e, t, n) {
  var o, r, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttribute(e) : (a = this.getAttribute(e), u = c + "", a === u ? null : a === o && u === r ? s : (r = u, s = t(o = a, c)));
  };
}
function Ey(e, t, n) {
  var o, r, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (a = this.getAttributeNS(e.space, e.local), u = c + "", a === u ? null : a === o && u === r ? s : (r = u, s = t(o = a, c)));
  };
}
function Iy(e, t) {
  var n = nr(e), o = n === "transform" ? sy : Ml;
  return this.attrTween(e, typeof t == "function" ? (n.local ? Ey : Cy)(n, o, es(this, "attr." + e, t)) : t == null ? (n.local ? Ny : by)(n) : (n.local ? Sy : jy)(n, o, t));
}
function ky(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function Ay(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function _y(e, t) {
  var n, o;
  function r() {
    var s = t.apply(this, arguments);
    return s !== o && (n = (o = s) && Ay(e, s)), n;
  }
  return r._value = t, r;
}
function Dy(e, t) {
  var n, o;
  function r() {
    var s = t.apply(this, arguments);
    return s !== o && (n = (o = s) && ky(e, s)), n;
  }
  return r._value = t, r;
}
function Ty(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = nr(e);
  return this.tween(n, (o.local ? _y : Dy)(o, t));
}
function Py(e, t) {
  return function() {
    Qi(this, e).delay = +t.apply(this, arguments);
  };
}
function $y(e, t) {
  return t = +t, function() {
    Qi(this, e).delay = t;
  };
}
function My(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Py : $y)(t, e)) : qe(this.node(), t).delay;
}
function Ry(e, t) {
  return function() {
    et(this, e).duration = +t.apply(this, arguments);
  };
}
function Ly(e, t) {
  return t = +t, function() {
    et(this, e).duration = t;
  };
}
function zy(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Ry : Ly)(t, e)) : qe(this.node(), t).duration;
}
function Vy(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    et(this, e).ease = t;
  };
}
function Oy(e) {
  var t = this._id;
  return arguments.length ? this.each(Vy(t, e)) : qe(this.node(), t).ease;
}
function Hy(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    et(this, e).ease = n;
  };
}
function Wy(e) {
  if (typeof e != "function") throw new Error();
  return this.each(Hy(this._id, e));
}
function Fy(e) {
  typeof e != "function" && (e = fl(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = o[r] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new st(o, this._parents, this._name, this._id);
}
function By(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, s = Math.min(o, r), a = new Array(o), c = 0; c < s; ++c)
    for (var u = t[c], l = n[c], d = u.length, f = a[c] = new Array(d), p, h = 0; h < d; ++h)
      (p = u[h] || l[h]) && (f[h] = p);
  for (; c < o; ++c)
    a[c] = t[c];
  return new st(a, this._parents, this._name, this._id);
}
function Ky(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function Xy(e, t, n) {
  var o, r, s = Ky(t) ? Qi : et;
  return function() {
    var a = s(this, e), c = a.on;
    c !== o && (r = (o = c).copy()).on(t, n), a.on = r;
  };
}
function qy(e, t) {
  var n = this._id;
  return arguments.length < 2 ? qe(this.node(), n).on.on(e) : this.each(Xy(n, e, t));
}
function Yy(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function Uy() {
  return this.on("end.remove", Yy(this._id));
}
function Zy(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Yi(e));
  for (var o = this._groups, r = o.length, s = new Array(r), a = 0; a < r; ++a)
    for (var c = o[a], u = c.length, l = s[a] = new Array(u), d, f, p = 0; p < u; ++p)
      (d = c[p]) && (f = e.call(d, d.__data__, p, c)) && ("__data__" in d && (f.__data__ = d.__data__), l[p] = f, rr(l[p], t, n, p, l, qe(d, n)));
  return new st(s, this._parents, t, n);
}
function Gy(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = dl(e));
  for (var o = this._groups, r = o.length, s = [], a = [], c = 0; c < r; ++c)
    for (var u = o[c], l = u.length, d, f = 0; f < l; ++f)
      if (d = u[f]) {
        for (var p = e.call(d, d.__data__, f, u), h, g = qe(d, n), m = 0, w = p.length; m < w; ++m)
          (h = p[m]) && rr(h, t, n, m, p, g);
        s.push(p), a.push(d);
      }
  return new st(s, a, t, n);
}
var Jy = Ln.prototype.constructor;
function Qy() {
  return new Jy(this._groups, this._parents);
}
function em(e, t) {
  var n, o, r;
  return function() {
    var s = Bt(this, e), a = (this.style.removeProperty(e), Bt(this, e));
    return s === a ? null : s === n && a === o ? r : r = t(n = s, o = a);
  };
}
function Rl(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function tm(e, t, n) {
  var o, r = n + "", s;
  return function() {
    var a = Bt(this, e);
    return a === r ? null : a === o ? s : s = t(o = a, n);
  };
}
function nm(e, t, n) {
  var o, r, s;
  return function() {
    var a = Bt(this, e), c = n(this), u = c + "";
    return c == null && (u = c = (this.style.removeProperty(e), Bt(this, e))), a === u ? null : a === o && u === r ? s : (r = u, s = t(o = a, c));
  };
}
function om(e, t) {
  var n, o, r, s = "style." + t, a = "end." + s, c;
  return function() {
    var u = et(this, e), l = u.on, d = u.value[s] == null ? c || (c = Rl(t)) : void 0;
    (l !== n || r !== d) && (o = (n = l).copy()).on(a, r = d), u.on = o;
  };
}
function rm(e, t, n) {
  var o = (e += "") == "transform" ? iy : Ml;
  return t == null ? this.styleTween(e, em(e, o)).on("end.style." + e, Rl(e)) : typeof t == "function" ? this.styleTween(e, nm(e, o, es(this, "style." + e, t))).each(om(this._id, e)) : this.styleTween(e, tm(e, o, t), n).on("end.style." + e, null);
}
function im(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function sm(e, t, n) {
  var o, r;
  function s() {
    var a = t.apply(this, arguments);
    return a !== r && (o = (r = a) && im(e, a, n)), o;
  }
  return s._value = t, s;
}
function am(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, sm(e, t, n ?? ""));
}
function cm(e) {
  return function() {
    this.textContent = e;
  };
}
function lm(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function um(e) {
  return this.tween("text", typeof e == "function" ? lm(es(this, "text", e)) : cm(e == null ? "" : e + ""));
}
function dm(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function fm(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && dm(r)), t;
  }
  return o._value = e, o;
}
function pm(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, fm(e));
}
function hm() {
  for (var e = this._name, t = this._id, n = Ll(), o = this._groups, r = o.length, s = 0; s < r; ++s)
    for (var a = o[s], c = a.length, u, l = 0; l < c; ++l)
      if (u = a[l]) {
        var d = qe(u, t);
        rr(u, e, n, l, a, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new st(o, this._parents, e, n);
}
function gm() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(s, a) {
    var c = { value: a }, u = { value: function() {
      --r === 0 && s();
    } };
    n.each(function() {
      var l = et(this, o), d = l.on;
      d !== e && (t = (e = d).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(u)), l.on = t;
    }), r === 0 && s();
  });
}
var ym = 0;
function st(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function Ll() {
  return ++ym;
}
var ot = Ln.prototype;
st.prototype = {
  constructor: st,
  select: Zy,
  selectAll: Gy,
  selectChild: ot.selectChild,
  selectChildren: ot.selectChildren,
  filter: Fy,
  merge: By,
  selection: Qy,
  transition: hm,
  call: ot.call,
  nodes: ot.nodes,
  node: ot.node,
  size: ot.size,
  empty: ot.empty,
  each: ot.each,
  on: qy,
  attr: Iy,
  attrTween: Ty,
  style: rm,
  styleTween: am,
  text: um,
  textTween: pm,
  remove: Uy,
  tween: vy,
  delay: My,
  duration: zy,
  ease: Oy,
  easeVarying: Wy,
  end: gm,
  [Symbol.iterator]: ot[Symbol.iterator]
};
function mm(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var xm = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: mm
};
function wm(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function vm(e) {
  var t, n;
  e instanceof st ? (t = e._id, e = e._name) : (t = Ll(), (n = xm).time = Ji(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, s = 0; s < r; ++s)
    for (var a = o[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && rr(u, e, t, l, a, n || wm(u, t));
  return new st(o, this._parents, e, t);
}
Ln.prototype.interrupt = my;
Ln.prototype.transition = vm;
const uo = (e) => () => e;
function bm(e, {
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
function rt(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
rt.prototype = {
  constructor: rt,
  scale: function(e) {
    return e === 1 ? this : new rt(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new rt(this.k, this.x + this.k * e, this.y + this.k * t);
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
var ir = new rt(1, 0, 0);
zl.prototype = rt.prototype;
function zl(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return ir;
  return e.__zoom;
}
function oi(e) {
  e.stopImmediatePropagation();
}
function cn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Nm(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function jm() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function aa() {
  return this.__zoom || ir;
}
function Sm(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function Cm() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Em(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], s = e.invertY(t[0][1]) - n[0][1], a = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    a > s ? (s + a) / 2 : Math.min(0, s) || Math.max(0, a)
  );
}
function Vl() {
  var e = Nm, t = jm, n = Em, o = Sm, r = Cm, s = [0, 1 / 0], a = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, u = No, l = tr("start", "zoom", "end"), d, f, p, h = 500, g = 150, m = 0, w = 10;
  function x(C) {
    C.property("__zoom", aa).on("wheel.zoom", E, { passive: !1 }).on("mousedown.zoom", D).on("dblclick.zoom", L).filter(r).on("touchstart.zoom", A).on("touchmove.zoom", _).on("touchend.zoom touchcancel.zoom", R).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  x.transform = function(C, I, k, T) {
    var $ = C.selection ? C.selection() : C;
    $.property("__zoom", aa), C !== $ ? j(C, I, k, T) : $.interrupt().each(function() {
      N(this, arguments).event(T).start().zoom(null, typeof I == "function" ? I.apply(this, arguments) : I).end();
    });
  }, x.scaleBy = function(C, I, k, T) {
    x.scaleTo(C, function() {
      var $ = this.__zoom.k, P = typeof I == "function" ? I.apply(this, arguments) : I;
      return $ * P;
    }, k, T);
  }, x.scaleTo = function(C, I, k, T) {
    x.transform(C, function() {
      var $ = t.apply(this, arguments), P = this.__zoom, F = k == null ? v($) : typeof k == "function" ? k.apply(this, arguments) : k, W = P.invert(F), O = typeof I == "function" ? I.apply(this, arguments) : I;
      return n(y(b(P, O), F, W), $, a);
    }, k, T);
  }, x.translateBy = function(C, I, k, T) {
    x.transform(C, function() {
      return n(this.__zoom.translate(
        typeof I == "function" ? I.apply(this, arguments) : I,
        typeof k == "function" ? k.apply(this, arguments) : k
      ), t.apply(this, arguments), a);
    }, null, T);
  }, x.translateTo = function(C, I, k, T, $) {
    x.transform(C, function() {
      var P = t.apply(this, arguments), F = this.__zoom, W = T == null ? v(P) : typeof T == "function" ? T.apply(this, arguments) : T;
      return n(ir.translate(W[0], W[1]).scale(F.k).translate(
        typeof I == "function" ? -I.apply(this, arguments) : -I,
        typeof k == "function" ? -k.apply(this, arguments) : -k
      ), P, a);
    }, T, $);
  };
  function b(C, I) {
    return I = Math.max(s[0], Math.min(s[1], I)), I === C.k ? C : new rt(I, C.x, C.y);
  }
  function y(C, I, k) {
    var T = I[0] - k[0] * C.k, $ = I[1] - k[1] * C.k;
    return T === C.x && $ === C.y ? C : new rt(C.k, T, $);
  }
  function v(C) {
    return [(+C[0][0] + +C[1][0]) / 2, (+C[0][1] + +C[1][1]) / 2];
  }
  function j(C, I, k, T) {
    C.on("start.zoom", function() {
      N(this, arguments).event(T).start();
    }).on("interrupt.zoom end.zoom", function() {
      N(this, arguments).event(T).end();
    }).tween("zoom", function() {
      var $ = this, P = arguments, F = N($, P).event(T), W = t.apply($, P), O = k == null ? v(W) : typeof k == "function" ? k.apply($, P) : k, Z = Math.max(W[1][0] - W[0][0], W[1][1] - W[0][1]), U = $.__zoom, te = typeof I == "function" ? I.apply($, P) : I, le = u(U.invert(O).concat(Z / U.k), te.invert(O).concat(Z / te.k));
      return function(G) {
        if (G === 1) G = te;
        else {
          var M = le(G), q = Z / M[2];
          G = new rt(q, O[0] - M[0] * q, O[1] - M[1] * q);
        }
        F.zoom(null, G);
      };
    });
  }
  function N(C, I, k) {
    return !k && C.__zooming || new S(C, I);
  }
  function S(C, I) {
    this.that = C, this.args = I, this.active = 0, this.sourceEvent = null, this.extent = t.apply(C, I), this.taps = 0;
  }
  S.prototype = {
    event: function(C) {
      return C && (this.sourceEvent = C), this;
    },
    start: function() {
      return ++this.active === 1 && (this.that.__zooming = this, this.emit("start")), this;
    },
    zoom: function(C, I) {
      return this.mouse && C !== "mouse" && (this.mouse[1] = I.invert(this.mouse[0])), this.touch0 && C !== "touch" && (this.touch0[1] = I.invert(this.touch0[0])), this.touch1 && C !== "touch" && (this.touch1[1] = I.invert(this.touch1[0])), this.that.__zoom = I, this.emit("zoom"), this;
    },
    end: function() {
      return --this.active === 0 && (delete this.that.__zooming, this.emit("end")), this;
    },
    emit: function(C) {
      var I = Re(this.that).datum();
      l.call(
        C,
        this.that,
        new bm(C, {
          sourceEvent: this.sourceEvent,
          target: x,
          transform: this.that.__zoom,
          dispatch: l
        }),
        I
      );
    }
  };
  function E(C, ...I) {
    if (!e.apply(this, arguments)) return;
    var k = N(this, I).event(C), T = this.__zoom, $ = Math.max(s[0], Math.min(s[1], T.k * Math.pow(2, o.apply(this, arguments)))), P = Fe(C);
    if (k.wheel)
      (k.mouse[0][0] !== P[0] || k.mouse[0][1] !== P[1]) && (k.mouse[1] = T.invert(k.mouse[0] = P)), clearTimeout(k.wheel);
    else {
      if (T.k === $) return;
      k.mouse = [P, T.invert(P)], Co(this), k.start();
    }
    cn(C), k.wheel = setTimeout(F, g), k.zoom("mouse", n(y(b(T, $), k.mouse[0], k.mouse[1]), k.extent, a));
    function F() {
      k.wheel = null, k.end();
    }
  }
  function D(C, ...I) {
    if (p || !e.apply(this, arguments)) return;
    var k = C.currentTarget, T = N(this, I, !0).event(C), $ = Re(C.view).on("mousemove.zoom", O, !0).on("mouseup.zoom", Z, !0), P = Fe(C, k), F = C.clientX, W = C.clientY;
    Nl(C.view), oi(C), T.mouse = [P, this.__zoom.invert(P)], Co(this), T.start();
    function O(U) {
      if (cn(U), !T.moved) {
        var te = U.clientX - F, le = U.clientY - W;
        T.moved = te * te + le * le > m;
      }
      T.event(U).zoom("mouse", n(y(T.that.__zoom, T.mouse[0] = Fe(U, k), T.mouse[1]), T.extent, a));
    }
    function Z(U) {
      $.on("mousemove.zoom mouseup.zoom", null), jl(U.view, T.moved), cn(U), T.event(U).end();
    }
  }
  function L(C, ...I) {
    if (e.apply(this, arguments)) {
      var k = this.__zoom, T = Fe(C.changedTouches ? C.changedTouches[0] : C, this), $ = k.invert(T), P = k.k * (C.shiftKey ? 0.5 : 2), F = n(y(b(k, P), T, $), t.apply(this, I), a);
      cn(C), c > 0 ? Re(this).transition().duration(c).call(j, F, T, C) : Re(this).call(x.transform, F, T, C);
    }
  }
  function A(C, ...I) {
    if (e.apply(this, arguments)) {
      var k = C.touches, T = k.length, $ = N(this, I, C.changedTouches.length === T).event(C), P, F, W, O;
      for (oi(C), F = 0; F < T; ++F)
        W = k[F], O = Fe(W, this), O = [O, this.__zoom.invert(O), W.identifier], $.touch0 ? !$.touch1 && $.touch0[2] !== O[2] && ($.touch1 = O, $.taps = 0) : ($.touch0 = O, P = !0, $.taps = 1 + !!d);
      d && (d = clearTimeout(d)), P && ($.taps < 2 && (f = O[0], d = setTimeout(function() {
        d = null;
      }, h)), Co(this), $.start());
    }
  }
  function _(C, ...I) {
    if (this.__zooming) {
      var k = N(this, I).event(C), T = C.changedTouches, $ = T.length, P, F, W, O;
      for (cn(C), P = 0; P < $; ++P)
        F = T[P], W = Fe(F, this), k.touch0 && k.touch0[2] === F.identifier ? k.touch0[0] = W : k.touch1 && k.touch1[2] === F.identifier && (k.touch1[0] = W);
      if (F = k.that.__zoom, k.touch1) {
        var Z = k.touch0[0], U = k.touch0[1], te = k.touch1[0], le = k.touch1[1], G = (G = te[0] - Z[0]) * G + (G = te[1] - Z[1]) * G, M = (M = le[0] - U[0]) * M + (M = le[1] - U[1]) * M;
        F = b(F, Math.sqrt(G / M)), W = [(Z[0] + te[0]) / 2, (Z[1] + te[1]) / 2], O = [(U[0] + le[0]) / 2, (U[1] + le[1]) / 2];
      } else if (k.touch0) W = k.touch0[0], O = k.touch0[1];
      else return;
      k.zoom("touch", n(y(F, W, O), k.extent, a));
    }
  }
  function R(C, ...I) {
    if (this.__zooming) {
      var k = N(this, I).event(C), T = C.changedTouches, $ = T.length, P, F;
      for (oi(C), p && clearTimeout(p), p = setTimeout(function() {
        p = null;
      }, h), P = 0; P < $; ++P)
        F = T[P], k.touch0 && k.touch0[2] === F.identifier ? delete k.touch0 : k.touch1 && k.touch1[2] === F.identifier && delete k.touch1;
      if (k.touch1 && !k.touch0 && (k.touch0 = k.touch1, delete k.touch1), k.touch0) k.touch0[1] = this.__zoom.invert(k.touch0[0]);
      else if (k.end(), k.taps === 2 && (F = Fe(F, this), Math.hypot(f[0] - F[0], f[1] - F[1]) < w)) {
        var W = Re(this).on("dblclick.zoom");
        W && W.apply(this, arguments);
      }
    }
  }
  return x.wheelDelta = function(C) {
    return arguments.length ? (o = typeof C == "function" ? C : uo(+C), x) : o;
  }, x.filter = function(C) {
    return arguments.length ? (e = typeof C == "function" ? C : uo(!!C), x) : e;
  }, x.touchable = function(C) {
    return arguments.length ? (r = typeof C == "function" ? C : uo(!!C), x) : r;
  }, x.extent = function(C) {
    return arguments.length ? (t = typeof C == "function" ? C : uo([[+C[0][0], +C[0][1]], [+C[1][0], +C[1][1]]]), x) : t;
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
  error008: (e, { id: t, sourceHandle: n, targetHandle: o }) => `Couldn't create edge for ${e} handle id: "${e === "source" ? n : o}", edge id: ${t}.`,
  error010: () => "Handle: No node id found. Make sure to only use a Handle inside a custom Node.",
  error011: (e) => `Edge type "${e}" not found. Using fallback type "default".`,
  error012: (e) => `Node with id "${e}" does not exist, it may have been removed. This can happen when a node is deleted before the "onNodeClick" handler is called.`,
  error013: (e = "react") => `It seems that you haven't loaded the styles. Please import '@xyflow/${e}/dist/style.css' or base.css to make sure everything is working properly.`,
  error014: () => "useNodeConnections: No node ID found. Call useNodeConnections inside a custom Node or provide a node ID.",
  error015: () => "It seems that you are trying to drag a node that is not initialized. Please use onNodesChange as explained in the docs.",
  error016: (e) => `Edge with id "${e}" does not exist, it may have been removed. This can happen when an edge is deleted before the "onEdgeClick" handler is called.`
}, An = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], Ol = ["Enter", " ", "Escape"], Hl = {
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
var Xt;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(Xt || (Xt = {}));
var vt;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(vt || (vt = {}));
var _n;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(_n || (_n = {}));
const Wl = {
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
var ft;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(ft || (ft = {}));
var Ko;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(Ko || (Ko = {}));
var ne;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(ne || (ne = {}));
const ca = {
  [ne.Left]: ne.Right,
  [ne.Right]: ne.Left,
  [ne.Top]: ne.Bottom,
  [ne.Bottom]: ne.Top
};
function Fl(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Bl = (e) => "id" in e && "source" in e && "target" in e, Im = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), ts = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), Vn = (e, t = [0, 0]) => {
  const { width: n, height: o } = at(e), r = e.origin ?? t, s = n * r[0], a = o * r[1];
  return {
    x: e.position.x - s,
    y: e.position.y - a
  };
}, km = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const s = typeof r == "string";
    let a = !t.nodeLookup && !s ? r : void 0;
    t.nodeLookup && (a = s ? t.nodeLookup.get(r) : ts(r) ? r : t.nodeLookup.get(r.id));
    const c = a ? Xo(a, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return sr(o, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return ar(n);
}, On = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = sr(n, Xo(r)), o = !0);
  }), o ? ar(n) : { x: 0, y: 0, width: 0, height: 0 };
}, ns = (e, t, [n, o, r] = [0, 0, 1], s = !1, a = !1) => {
  const c = {
    ...tn(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, u = [];
  for (const l of e.values()) {
    const { measured: d, selectable: f = !0, hidden: p = !1 } = l;
    if (a && !f || p)
      continue;
    const h = d.width ?? l.width ?? l.initialWidth ?? null, g = d.height ?? l.height ?? l.initialHeight ?? null, m = Dn(c, Yt(l)), w = (h ?? 0) * (g ?? 0), x = s && m > 0;
    (!l.internals.handleBounds || x || m >= w || l.dragging) && u.push(l);
  }
  return u;
}, Am = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function _m(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function Dm({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: s }, a) {
  if (e.size === 0)
    return !0;
  const c = _m(e, a), u = On(c), l = rs(u, t, n, a?.minZoom ?? r, a?.maxZoom ?? s, a?.padding ?? 0.1);
  return await o.setViewport(l, {
    duration: a?.duration,
    ease: a?.ease,
    interpolate: a?.interpolate
  }), !0;
}
function Kl({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: s }) {
  const a = n.get(e), c = a.parentId ? n.get(a.parentId) : void 0, { x: u, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, d = a.origin ?? o;
  let f = a.extent || r;
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
async function Tm({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const s = new Set(e.map((p) => p.id)), a = [];
  for (const p of n) {
    if (p.deletable === !1)
      continue;
    const h = s.has(p.id), g = !h && p.parentId && a.find((m) => m.id === p.parentId);
    (h || g) && a.push(p);
  }
  const c = new Set(t.map((p) => p.id)), u = o.filter((p) => p.deletable !== !1), d = Am(a, u);
  for (const p of u)
    c.has(p.id) && !d.find((g) => g.id === p.id) && d.push(p);
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
const qt = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), jt = (e = { x: 0, y: 0 }, t, n) => ({
  x: qt(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: qt(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Xl(e, t, n) {
  const { width: o, height: r } = at(n), { x: s, y: a } = n.internals.positionAbsolute;
  return jt(e, [
    [s, a],
    [s + o, a + r]
  ], t);
}
const la = (e, t, n) => e < t ? qt(Math.abs(e - t), 1, t) / t : e > n ? -qt(Math.abs(e - n), 1, t) / t : 0, os = (e, t, n = 15, o = 40) => {
  const r = la(e.x, o, t.width - o) * n, s = la(e.y, o, t.height - o) * n;
  return [r, s];
}, sr = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), ki = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), ar = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), Yt = (e, t = [0, 0]) => {
  const { x: n, y: o } = ts(e) ? e.internals.positionAbsolute : Vn(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, Xo = (e, t = [0, 0]) => {
  const { x: n, y: o } = ts(e) ? e.internals.positionAbsolute : Vn(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, ql = (e, t) => ar(sr(ki(e), ki(t))), Dn = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, ua = (e) => Ke(e.width) && Ke(e.height) && Ke(e.x) && Ke(e.y), Ke = (e) => !isNaN(e) && isFinite(e), Yl = (e, t) => (n, o) => {
}, Hn = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), tn = ({ x: e, y: t }, [n, o, r], s = !1, a = [1, 1]) => {
  const c = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return s ? Hn(c, a) : c;
}, Ut = ({ x: e, y: t }, [n, o, r]) => ({
  x: e * r + n,
  y: t * r + o
});
function Pt(e, t) {
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
function Pm(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = Pt(e, n), r = Pt(e, t);
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
    const o = Pt(e.top ?? e.y ?? 0, n), r = Pt(e.bottom ?? e.y ?? 0, n), s = Pt(e.left ?? e.x ?? 0, t), a = Pt(e.right ?? e.x ?? 0, t);
    return { top: o, right: a, bottom: r, left: s, x: s + a, y: o + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function $m(e, t, n, o, r, s) {
  const { x: a, y: c } = Ut(e, [t, n, o]), { x: u, y: l } = Ut({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), d = r - u, f = s - l;
  return {
    left: Math.floor(a),
    top: Math.floor(c),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const rs = (e, t, n, o, r, s) => {
  const a = Pm(s, t, n), c = (t - a.x) / e.width, u = (n - a.y) / e.height, l = Math.min(c, u), d = qt(l, o, r), f = e.x + e.width / 2, p = e.y + e.height / 2, h = t / 2 - f * d, g = n / 2 - p * d, m = $m(e, h, g, d, t, n), w = {
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
}, Tn = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function St(e) {
  return e != null && e !== "parent";
}
function at(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function Ul(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function Zl(e, t = { width: 0, height: 0 }, n, o, r) {
  const s = { ...e }, a = o.get(n);
  if (a) {
    const c = a.origin || r;
    s.x += a.internals.positionAbsolute.x - (t.width ?? 0) * c[0], s.y += a.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return s;
}
function da(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function Mm() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function Rm(e) {
  return { ...Hl, ...e || {} };
}
function Nn(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: s, y: a } = Xe(e), c = tn({ x: s - (r?.left ?? 0), y: a - (r?.top ?? 0) }, o), { x: u, y: l } = n ? Hn(c, t) : c;
  return {
    xSnapped: u,
    ySnapped: l,
    ...c
  };
}
const is = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), Gl = (e) => e?.getRootNode?.() || window?.document, Lm = ["INPUT", "SELECT", "TEXTAREA"];
function Jl(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : Lm.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const Ql = (e) => "clientX" in e, Xe = (e, t) => {
  const n = Ql(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, fa = (e, t, n, o, r) => {
  const s = t.querySelectorAll(`.${e}`);
  return !s || !s.length ? null : Array.from(s).map((a) => {
    const c = a.getBoundingClientRect();
    return {
      id: a.getAttribute("data-handleid"),
      type: e,
      nodeId: r,
      position: a.getAttribute("data-handlepos"),
      x: (c.left - n.left) / o,
      y: (c.top - n.top) / o,
      ...is(a)
    };
  });
};
function eu({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: s, targetControlX: a, targetControlY: c }) {
  const u = e * 0.125 + r * 0.375 + a * 0.375 + n * 0.125, l = t * 0.125 + s * 0.375 + c * 0.375 + o * 0.125, d = Math.abs(u - e), f = Math.abs(l - t);
  return [u, l, d, f];
}
function fo(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function pa({ pos: e, x1: t, y1: n, x2: o, y2: r, c: s }) {
  switch (e) {
    case ne.Left:
      return [t - fo(t - o, s), n];
    case ne.Right:
      return [t + fo(o - t, s), n];
    case ne.Top:
      return [t, n - fo(n - r, s)];
    case ne.Bottom:
      return [t, n + fo(r - n, s)];
  }
}
function tu({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: r, targetPosition: s = ne.Top, curvature: a = 0.25 }) {
  const [c, u] = pa({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: a
  }), [l, d] = pa({
    pos: s,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: a
  }), [f, p, h, g] = eu({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: r,
    sourceControlX: c,
    sourceControlY: u,
    targetControlX: l,
    targetControlY: d
  });
  return [
    `M${e},${t} C${c},${u} ${l},${d} ${o},${r}`,
    f,
    p,
    h,
    g
  ];
}
function nu({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, s = n < e ? n + r : n - r, a = Math.abs(o - t) / 2, c = o < t ? o + a : o - a;
  return [s, c, r, a];
}
function zm({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: s = "basic" }) {
  if (s === "manual")
    return o;
  const a = r && n ? o + 1e3 : o, c = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return a + c;
}
function Vm({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const s = sr(Xo(e), Xo(t));
  s.x === s.x2 && (s.x2 += 1), s.y === s.y2 && (s.y2 += 1);
  const a = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return Dn(a, ar(s)) > 0;
}
const ou = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, Om = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), Hm = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Oe.error006()), t;
  const o = n.getEdgeId || ou;
  let r;
  return Bl(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, Om(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, Wm = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: r, ...s } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", Oe.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return o.onError?.("007", Oe.error007(r)), n;
  const c = o.getEdgeId || ou, u = {
    ...s,
    id: o.shouldReplaceId ? c(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== r).concat(u);
};
function ru({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, s, a, c] = nu({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, s, a, c];
}
const ha = {
  [ne.Left]: { x: -1, y: 0 },
  [ne.Right]: { x: 1, y: 0 },
  [ne.Top]: { x: 0, y: -1 },
  [ne.Bottom]: { x: 0, y: 1 }
}, Fm = ({ source: e, sourcePosition: t = ne.Bottom, target: n }) => t === ne.Left || t === ne.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, ga = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Bm({ source: e, sourcePosition: t = ne.Bottom, target: n, targetPosition: o = ne.Top, center: r, offset: s, stepPosition: a }) {
  const c = ha[t], u = ha[o], l = { x: e.x + c.x * s, y: e.y + c.y * s }, d = { x: n.x + u.x * s, y: n.y + u.y * s }, f = Fm({
    source: l,
    sourcePosition: t,
    target: d
  }), p = f.x !== 0 ? "x" : "y", h = f[p];
  let g = [], m, w;
  const x = { x: 0, y: 0 }, b = { x: 0, y: 0 }, [, , y, v] = nu({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[p] * u[p] === -1) {
    p === "x" ? (m = r.x ?? l.x + (d.x - l.x) * a, w = r.y ?? (l.y + d.y) / 2) : (m = r.x ?? (l.x + d.x) / 2, w = r.y ?? l.y + (d.y - l.y) * a);
    const E = [
      { x: m, y: l.y },
      { x: m, y: d.y }
    ], D = [
      { x: l.x, y: w },
      { x: d.x, y: w }
    ];
    c[p] === h ? g = p === "x" ? E : D : g = p === "x" ? D : E;
  } else {
    const E = [{ x: l.x, y: d.y }], D = [{ x: d.x, y: l.y }];
    if (p === "x" ? g = c.x === h ? D : E : g = c.y === h ? E : D, t === o) {
      const C = Math.abs(e[p] - n[p]);
      if (C <= s) {
        const I = Math.min(s - 1, s - C);
        c[p] === h ? x[p] = (l[p] > e[p] ? -1 : 1) * I : b[p] = (d[p] > n[p] ? -1 : 1) * I;
      }
    }
    if (t !== o) {
      const C = p === "x" ? "y" : "x", I = c[p] === u[C], k = l[C] > d[C], T = l[C] < d[C];
      (c[p] === 1 && (!I && k || I && T) || c[p] !== 1 && (!I && T || I && k)) && (g = p === "x" ? E : D);
    }
    const L = { x: l.x + x.x, y: l.y + x.y }, A = { x: d.x + b.x, y: d.y + b.y }, _ = Math.max(Math.abs(L.x - g[0].x), Math.abs(A.x - g[0].x)), R = Math.max(Math.abs(L.y - g[0].y), Math.abs(A.y - g[0].y));
    _ >= R ? (m = (L.x + A.x) / 2, w = g[0].y) : (m = g[0].x, w = (L.y + A.y) / 2);
  }
  const j = { x: l.x + x.x, y: l.y + x.y }, N = { x: d.x + b.x, y: d.y + b.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...j.x !== g[0].x || j.y !== g[0].y ? [j] : [],
    ...g,
    ...N.x !== g[g.length - 1].x || N.y !== g[g.length - 1].y ? [N] : [],
    n
  ], m, w, y, v];
}
function Km(e, t, n, o) {
  const r = Math.min(ga(e, t) / 2, ga(t, n) / 2, o), { x: s, y: a } = t;
  if (e.x === s && s === n.x || e.y === a && a === n.y)
    return `L${s} ${a}`;
  if (e.y === a) {
    const l = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${s + r * l},${a}Q ${s},${a} ${s},${a + r * d}`;
  }
  const c = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${s},${a + r * u}Q ${s},${a} ${s + r * c},${a}`;
}
function qo({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: r, targetPosition: s = ne.Top, borderRadius: a = 5, centerX: c, centerY: u, offset: l = 20, stepPosition: d = 0.5 }) {
  const [f, p, h, g, m] = Bm({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: s,
    center: { x: c, y: u },
    offset: l,
    stepPosition: d
  });
  let w = `M${f[0].x} ${f[0].y}`;
  for (let x = 1; x < f.length - 1; x++)
    w += Km(f[x - 1], f[x], f[x + 1], a);
  return w += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [w, p, h, g, m];
}
function ya(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function Xm(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!ya(t) || !ya(n))
    return null;
  const o = t.internals.handleBounds || ma(t.handles), r = n.internals.handleBounds || ma(n.handles), s = xa(o?.source ?? [], e.sourceHandle), a = xa(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === Xt.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!s || !a)
    return e.onError?.("008", Oe.error008(s ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = s?.position || ne.Bottom, u = a?.position || ne.Top, l = Ct(t, s, c), d = Ct(n, a, u);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: d.x,
    targetY: d.y,
    sourcePosition: c,
    targetPosition: u
  };
}
function ma(e) {
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
function Ct(e, t, n = ne.Left, o = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, s = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: a, height: c } = t ?? at(e);
  if (o)
    return { x: r + a / 2, y: s + c / 2 };
  switch (t?.position ?? n) {
    case ne.Top:
      return { x: r + a / 2, y: s };
    case ne.Right:
      return { x: r + a, y: s + c / 2 };
    case ne.Bottom:
      return { x: r + a / 2, y: s + c };
    case ne.Left:
      return { x: r, y: s + c / 2 };
  }
}
function xa(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Ai(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function qm(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const s = /* @__PURE__ */ new Set();
  return e.reduce((a, c) => ([c.markerStart || o, c.markerEnd || r].forEach((u) => {
    if (u && typeof u == "object") {
      const l = Ai(u, t);
      s.has(l) || (a.push({ id: l, color: u.color || n, ...u }), s.add(l));
    }
  }), a), []).sort((a, c) => a.id.localeCompare(c.id));
}
const iu = 1e3, Ym = 10, ss = {
  nodeOrigin: [0, 0],
  nodeExtent: An,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Um = {
  ...ss,
  checkEquality: !0
};
function as(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function Zm(e, t, n) {
  const o = as(ss, n);
  for (const r of e.values())
    if (r.parentId)
      ls(r, e, t, o);
    else {
      const s = Vn(r, o.nodeOrigin), a = St(r.extent) ? r.extent : o.nodeExtent, c = jt(s, a, at(r));
      r.internals.positionAbsolute = c;
    }
}
function Gm(e, t) {
  if (!e.handles)
    return e.measured ? t?.internals.handleBounds : void 0;
  const n = [], o = [];
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
    r.type === "source" ? n.push(s) : r.type === "target" && o.push(s);
  }
  return {
    source: n,
    target: o
  };
}
function cs(e) {
  return e === "manual";
}
function _i(e, t, n, o = {}) {
  const r = as(Um, o), s = { i: 0 }, a = new Map(t), c = r?.elevateNodesOnSelect && !cs(r.zIndexMode) ? iu : 0;
  let u = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = a.get(d.id);
    if (r.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const p = Vn(d, r.nodeOrigin), h = St(d.extent) ? d.extent : r.nodeExtent, g = jt(p, h, at(d));
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
          handleBounds: Gm(d, f),
          z: su(d, c, r.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (u = !1), d.parentId && ls(f, t, n, o, s), l ||= d.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: l };
}
function Jm(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function ls(e, t, n, o, r) {
  const { elevateNodesOnSelect: s, nodeOrigin: a, nodeExtent: c, zIndexMode: u } = as(ss, o), l = e.parentId, d = t.get(l);
  if (!d) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Jm(e, n), r && !d.parentId && d.internals.rootParentIndex === void 0 && u === "auto" && (d.internals.rootParentIndex = ++r.i, d.internals.z = d.internals.z + r.i * Ym), r && d.internals.rootParentIndex !== void 0 && (r.i = d.internals.rootParentIndex);
  const f = s && !cs(u) ? iu : 0, { x: p, y: h, z: g } = Qm(e, d, a, c, f, u), { positionAbsolute: m } = e.internals, w = p !== m.x || h !== m.y;
  (w || g !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: w ? { x: p, y: h } : m,
      z: g
    }
  });
}
function su(e, t, n) {
  const o = Ke(e.zIndex) ? e.zIndex : 0;
  return cs(n) ? o : o + (e.selected ? t : 0);
}
function Qm(e, t, n, o, r, s) {
  const { x: a, y: c } = t.internals.positionAbsolute, u = at(e), l = Vn(e, n), d = St(e.extent) ? jt(l, e.extent, u) : l;
  let f = jt({ x: a + d.x, y: c + d.y }, o, u);
  e.extent === "parent" && (f = Xl(f, u, t));
  const p = su(e, r, s), h = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: h >= p ? h + 1 : p
  };
}
function us(e, t, n, o = [0, 0]) {
  const r = [], s = /* @__PURE__ */ new Map();
  for (const a of e) {
    const c = t.get(a.parentId);
    if (!c)
      continue;
    const u = s.get(a.parentId)?.expandedRect ?? Yt(c), l = ql(u, a.rect);
    s.set(a.parentId, { expandedRect: l, parent: c });
  }
  return s.size > 0 && s.forEach(({ expandedRect: a, parent: c }, u) => {
    const l = c.internals.positionAbsolute, d = at(c), f = c.origin ?? o, p = a.x < l.x ? Math.round(Math.abs(l.x - a.x)) : 0, h = a.y < l.y ? Math.round(Math.abs(l.y - a.y)) : 0, g = Math.max(d.width, Math.round(a.width)), m = Math.max(d.height, Math.round(a.height)), w = (g - d.width) * f[0], x = (m - d.height) * f[1];
    (p > 0 || h > 0 || w || x) && (r.push({
      id: u,
      type: "position",
      position: {
        x: c.position.x - p + w,
        y: c.position.y - h + x
      }
    }), n.get(u)?.forEach((b) => {
      e.some((y) => y.id === b.id) || r.push({
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
        width: g + (p ? f[0] * p - w : 0),
        height: m + (h ? f[1] * h - x : 0)
      }
    });
  }), r;
}
function ex(e, t, n, o, r, s, a) {
  const c = o?.querySelector(".xyflow__viewport");
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
    const m = is(h.nodeElement), w = g.measured.width !== m.width || g.measured.height !== m.height;
    if (!!(m.width && m.height && (w || !g.internals.handleBounds || h.force))) {
      const b = h.nodeElement.getBoundingClientRect(), y = St(g.extent) ? g.extent : s;
      let { positionAbsolute: v } = g.internals;
      g.parentId && g.extent === "parent" ? v = Xl(v, m, t.get(g.parentId)) : y && (v = jt(v, y, m));
      const j = {
        ...g,
        measured: m,
        internals: {
          ...g.internals,
          positionAbsolute: v,
          handleBounds: {
            source: fa("source", h.nodeElement, b, f, g.id),
            target: fa("target", h.nodeElement, b, f, g.id)
          }
        }
      };
      t.set(g.id, j), g.parentId && ls(j, t, n, { nodeOrigin: r, zIndexMode: a }), u = !0, w && (l.push({
        id: g.id,
        type: "dimensions",
        dimensions: m
      }), g.expandParent && g.parentId && p.push({
        id: g.id,
        parentId: g.parentId,
        rect: Yt(j, r)
      }));
    }
  }
  if (p.length > 0) {
    const h = us(p, t, n, r);
    l.push(...h);
  }
  return { changes: l, updatedInternals: u };
}
async function tx({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: s }) {
  if (!t || !e.x && !e.y)
    return !1;
  const a = await t.setViewportConstrained({
    x: n[0] + e.x,
    y: n[1] + e.y,
    zoom: n[2]
  }, [
    [0, 0],
    [r, s]
  ], o);
  return !!a && (a.x !== n[0] || a.y !== n[1] || a.k !== n[2]);
}
function wa(e, t, n, o, r, s) {
  let a = r;
  const c = o.get(a) || /* @__PURE__ */ new Map();
  o.set(a, c.set(n, t)), a = `${r}-${e}`;
  const u = o.get(a) || /* @__PURE__ */ new Map();
  if (o.set(a, u.set(n, t)), s) {
    a = `${r}-${e}-${s}`;
    const l = o.get(a) || /* @__PURE__ */ new Map();
    o.set(a, l.set(n, t));
  }
}
function au(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: s, sourceHandle: a = null, targetHandle: c = null } = o, u = { edgeId: o.id, source: r, target: s, sourceHandle: a, targetHandle: c }, l = `${r}-${a}--${s}-${c}`, d = `${s}-${c}--${r}-${a}`;
    wa("source", u, d, e, r, a), wa("target", u, l, e, s, c), t.set(o.id, o);
  }
}
function cu(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : cu(n, t) : !1;
}
function va(e, t, n) {
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
function nx(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [s, a] of e)
    if ((a.selected || a.id === o) && (!a.parentId || !cu(a, e)) && (a.draggable || t && typeof a.draggable > "u")) {
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
function ri({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
  const r = [];
  for (const [a, c] of t) {
    const u = n.get(a)?.internals.userNode;
    u && r.push({
      ...u,
      position: c.position,
      dragging: o
    });
  }
  if (!e)
    return [r[0], r];
  const s = n.get(e)?.internals.userNode;
  return [
    s ? {
      ...s,
      position: t.get(e)?.position || s.position,
      dragging: o
    } : r[0],
    r
  ];
}
function ox({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const s = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, a = Hn(s, t);
  return {
    x: a.x - s.x,
    y: a.y - s.y
  };
}
function rx({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let s = { x: null, y: null }, a = 0, c = /* @__PURE__ */ new Map(), u = !1, l = { x: 0, y: 0 }, d = null, f = !1, p = null, h = !1, g = !1, m = null;
  function w({ noDragClassName: b, handleSelector: y, domNode: v, isSelectable: j, nodeId: N, nodeClickDistance: S = 0 }) {
    p = Re(v);
    function E({ x: _, y: R }) {
      const { nodeLookup: C, nodeExtent: I, snapGrid: k, snapToGrid: T, nodeOrigin: $, onNodeDrag: P, onSelectionDrag: F, onError: W, updateNodePositions: O } = t();
      s = { x: _, y: R };
      let Z = !1;
      const U = c.size > 1, te = U && I ? ki(On(c)) : null, le = U && T ? ox({
        dragItems: c,
        snapGrid: k,
        x: _,
        y: R
      }) : null;
      for (const [G, M] of c) {
        if (!C.has(G))
          continue;
        let q = { x: _ - M.distance.x, y: R - M.distance.y };
        T && (q = le ? {
          x: Math.round(q.x + le.x),
          y: Math.round(q.y + le.y)
        } : Hn(q, k));
        let ae = null;
        if (U && I && !M.extent && te) {
          const { positionAbsolute: oe } = M.internals, de = oe.x - te.x + I[0][0], V = oe.x + M.measured.width - te.x2 + I[1][0], ee = oe.y - te.y + I[0][1], ge = oe.y + M.measured.height - te.y2 + I[1][1];
          ae = [
            [de, ee],
            [V, ge]
          ];
        }
        const { position: ce, positionAbsolute: Q } = Kl({
          nodeId: G,
          nextPosition: q,
          nodeLookup: C,
          nodeExtent: ae || I,
          nodeOrigin: $,
          onError: W
        });
        Z = Z || M.position.x !== ce.x || M.position.y !== ce.y, M.position = ce, M.internals.positionAbsolute = Q;
      }
      if (g = g || Z, !!Z && (O(c, !0), m && (o || P || !N && F))) {
        const [G, M] = ri({
          nodeId: N,
          dragItems: c,
          nodeLookup: C
        });
        o?.(m, c, G, M), P?.(m, G, M), N || F?.(m, M);
      }
    }
    async function D() {
      if (!d)
        return;
      const { transform: _, panBy: R, autoPanSpeed: C, autoPanOnNodeDrag: I } = t();
      if (!I) {
        u = !1, cancelAnimationFrame(a);
        return;
      }
      const [k, T] = os(l, d, C);
      (k !== 0 || T !== 0) && (s.x = (s.x ?? 0) - k / _[2], s.y = (s.y ?? 0) - T / _[2], await R({ x: k, y: T }) && E(s)), a = requestAnimationFrame(D);
    }
    function L(_) {
      const { nodeLookup: R, multiSelectionActive: C, nodesDraggable: I, transform: k, snapGrid: T, snapToGrid: $, selectNodesOnDrag: P, onNodeDragStart: F, onSelectionDragStart: W, unselectNodesAndEdges: O } = t();
      f = !0, (!P || !j) && !C && N && (R.get(N)?.selected || O()), j && P && N && e?.(N);
      const Z = Nn(_.sourceEvent, { transform: k, snapGrid: T, snapToGrid: $, containerBounds: d });
      if (s = Z, c = nx(R, I, Z, N), c.size > 0 && (n || F || !N && W)) {
        const [U, te] = ri({
          nodeId: N,
          dragItems: c,
          nodeLookup: R
        });
        n?.(_.sourceEvent, c, U, te), F?.(_.sourceEvent, U, te), N || W?.(_.sourceEvent, te);
      }
    }
    const A = Sl().clickDistance(S).on("start", (_) => {
      const { domNode: R, nodeDragThreshold: C, transform: I, snapGrid: k, snapToGrid: T } = t();
      d = R?.getBoundingClientRect() || null, h = !1, g = !1, m = _.sourceEvent, C === 0 && L(_), s = Nn(_.sourceEvent, { transform: I, snapGrid: k, snapToGrid: T, containerBounds: d }), l = Xe(_.sourceEvent, d);
    }).on("drag", (_) => {
      const { autoPanOnNodeDrag: R, transform: C, snapGrid: I, snapToGrid: k, nodeDragThreshold: T, nodeLookup: $ } = t(), P = Nn(_.sourceEvent, { transform: C, snapGrid: I, snapToGrid: k, containerBounds: d });
      if (m = _.sourceEvent, (_.sourceEvent.type === "touchmove" && _.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      N && !$.has(N)) && (h = !0), !h) {
        if (!u && R && f && (u = !0, D()), !f) {
          const F = Xe(_.sourceEvent, d), W = F.x - l.x, O = F.y - l.y;
          Math.sqrt(W * W + O * O) > T && L(_);
        }
        (s.x !== P.xSnapped || s.y !== P.ySnapped) && c && f && (l = Xe(_.sourceEvent, d), E(P));
      }
    }).on("end", (_) => {
      if (!f || h) {
        h && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (u = !1, f = !1, cancelAnimationFrame(a), c.size > 0) {
        const { nodeLookup: R, updateNodePositions: C, onNodeDragStop: I, onSelectionDragStop: k } = t();
        if (g && (C(c, !1), g = !1), r || I || !N && k) {
          const [T, $] = ri({
            nodeId: N,
            dragItems: c,
            nodeLookup: R,
            dragging: !1
          });
          r?.(_.sourceEvent, c, T, $), I?.(_.sourceEvent, T, $), N || k?.(_.sourceEvent, $);
        }
      }
    }).filter((_) => {
      const R = _.target;
      return !_.button && (!b || !va(R, `.${b}`, v)) && (!y || va(R, y, v));
    });
    p.call(A);
  }
  function x() {
    p?.on(".drag", null);
  }
  return {
    update: w,
    destroy: x
  };
}
function ix(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const s of t.values())
    Dn(r, Yt(s)) > 0 && o.push(s);
  return o;
}
const sx = 250;
function ax(e, t, n, o) {
  let r = [], s = 1 / 0;
  const a = ix(e, n, t + sx);
  for (const c of a) {
    const u = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of u) {
      if (o.nodeId === l.nodeId && o.type === l.type && o.id === l.id)
        continue;
      const { x: d, y: f } = Ct(c, l, l.position, !0), p = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(f - e.y, 2));
      p > t || (p < s ? (r = [{ ...l, x: d, y: f }], s = p) : p === s && r.push({ ...l, x: d, y: f }));
    }
  }
  if (!r.length)
    return null;
  if (r.length > 1) {
    const c = o.type === "source" ? "target" : "source";
    return r.find((u) => u.type === c) ?? r[0];
  }
  return r[0];
}
function lu(e, t, n, o, r, s = !1) {
  const a = o.get(e);
  if (!a)
    return null;
  const c = r === "strict" ? a.internals.handleBounds?.[t] : [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []], u = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return u && s ? { ...u, ...Ct(a, u, u.position, !0) } : u;
}
function uu(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function cx(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const du = () => !0;
function lx(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: s, isTarget: a, domNode: c, nodeLookup: u, lib: l, autoPanOnConnect: d, flowId: f, panBy: p, cancelConnection: h, onConnectStart: g, onConnect: m, onConnectEnd: w, isValidConnection: x = du, onReconnectEnd: b, updateConnection: y, getTransform: v, getFromHandle: j, autoPanSpeed: N, dragThreshold: S = 1, handleDomNode: E }) {
  const D = Gl(e.target);
  let L = 0, A;
  const { x: _, y: R } = Xe(e), C = uu(s, E), I = c?.getBoundingClientRect();
  let k = !1;
  if (!I || !C)
    return;
  const T = lu(r, C, o, u, t);
  if (!T)
    return;
  let $ = Xe(e, I), P = !1, F = null, W = !1, O = null;
  function Z() {
    if (!d || !I)
      return;
    const [ce, Q] = os($, I, N);
    p({ x: ce, y: Q }), L = requestAnimationFrame(Z);
  }
  const U = {
    ...T,
    nodeId: r,
    type: C,
    position: T.position
  }, te = u.get(r);
  let G = {
    inProgress: !0,
    isValid: null,
    from: Ct(te, U, ne.Left, !0),
    fromHandle: U,
    fromPosition: U.position,
    fromNode: te,
    to: $,
    toHandle: null,
    toPosition: ca[U.position],
    toNode: null,
    pointer: $
  };
  function M() {
    k = !0, y(G), g?.(e, { nodeId: r, handleId: o, handleType: C });
  }
  S === 0 && M();
  function q(ce) {
    if (!k) {
      const { x: ge, y: we } = Xe(ce), Ae = ge - _, Ie = we - R;
      if (!(Ae * Ae + Ie * Ie > S * S))
        return;
      M();
    }
    if (!j() || !U) {
      ae(ce);
      return;
    }
    const Q = v();
    $ = Xe(ce, I), A = ax(tn($, Q, !1, [1, 1]), n, u, U), P || (Z(), P = !0);
    const oe = fu(ce, {
      handle: A,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: a ? "target" : "source",
      isValidConnection: x,
      doc: D,
      lib: l,
      flowId: f,
      nodeLookup: u
    });
    O = oe.handleDomNode, F = oe.connection, W = cx(!!A, oe.isValid);
    const de = u.get(r), V = de ? Ct(de, U, ne.Left, !0) : G.from, ee = {
      ...G,
      from: V,
      isValid: W,
      to: oe.toHandle && W ? Ut({ x: oe.toHandle.x, y: oe.toHandle.y }, Q) : $,
      toHandle: oe.toHandle,
      toPosition: W && oe.toHandle ? oe.toHandle.position : ca[U.position],
      toNode: oe.toHandle ? u.get(oe.toHandle.nodeId) : null,
      pointer: $
    };
    y(ee), G = ee;
  }
  function ae(ce) {
    if (!("touches" in ce && ce.touches.length > 0)) {
      if (k) {
        (A || O) && F && W && m?.(F);
        const { inProgress: Q, ...oe } = G, de = {
          ...oe,
          toPosition: G.toHandle ? G.toPosition : null
        };
        w?.(ce, de), s && b?.(ce, de);
      }
      h(), cancelAnimationFrame(L), P = !1, W = !1, F = null, O = null, D.removeEventListener("mousemove", q), D.removeEventListener("mouseup", ae), D.removeEventListener("touchmove", q), D.removeEventListener("touchend", ae);
    }
  }
  D.addEventListener("mousemove", q), D.addEventListener("mouseup", ae), D.addEventListener("touchmove", q), D.addEventListener("touchend", ae);
}
function fu(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: s, doc: a, lib: c, flowId: u, isValidConnection: l = du, nodeLookup: d }) {
  const f = s === "target", p = t ? a.querySelector(`.${c}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: h, y: g } = Xe(e), m = a.elementFromPoint(h, g), w = m?.classList.contains(`${c}-flow__handle`) ? m : p, x = {
    handleDomNode: w,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (w) {
    const b = uu(void 0, w), y = w.getAttribute("data-nodeid"), v = w.getAttribute("data-handleid"), j = w.classList.contains("connectable"), N = w.classList.contains("connectableend");
    if (!y || !b)
      return x;
    const S = {
      source: f ? y : o,
      sourceHandle: f ? v : r,
      target: f ? o : y,
      targetHandle: f ? r : v
    };
    x.connection = S;
    const D = j && N && (n === Xt.Strict ? f && b === "source" || !f && b === "target" : y !== o || v !== r);
    x.isValid = D && l(S), x.toHandle = lu(y, b, v, d, n, !0);
  }
  return x;
}
const Di = {
  onPointerDown: lx,
  isValid: fu
};
function ux({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = Re(e);
  function s({ translateExtent: c, width: u, height: l, zoomStep: d = 1, pannable: f = !0, zoomable: p = !0, inversePan: h = !1 }) {
    const g = (y) => {
      if (y.sourceEvent.type !== "wheel" || !t)
        return;
      const v = n(), j = y.sourceEvent.ctrlKey && Tn() ? 10 : 1, N = -y.sourceEvent.deltaY * (y.sourceEvent.deltaMode === 1 ? 0.05 : y.sourceEvent.deltaMode ? 1 : 2e-3) * d, S = v[2] * Math.pow(2, N * j);
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
      const j = [
        y.sourceEvent.clientX ?? y.sourceEvent.touches[0].clientX,
        y.sourceEvent.clientY ?? y.sourceEvent.touches[0].clientY
      ], N = [j[0] - m[0], j[1] - m[1]];
      m = j;
      const S = o() * Math.max(v[2], Math.log(v[2])) * (h ? -1 : 1), E = {
        x: v[0] - N[0] * S,
        y: v[1] - N[1] * S
      }, D = [
        [0, 0],
        [u, l]
      ];
      t.setViewportConstrained({
        x: E.x,
        y: E.y,
        zoom: v[2]
      }, D, c);
    }, b = Vl().on("start", w).on("zoom", f ? x : null).on("zoom.wheel", p ? g : null);
    r.call(b, {});
  }
  function a() {
    r.on("zoom", null);
  }
  return {
    update: s,
    destroy: a,
    pointer: Fe
  };
}
const cr = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), ii = ({ x: e, y: t, zoom: n }) => ir.translate(e, t).scale(n), Rt = (e, t) => e.target.closest(`.${t}`), pu = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), dx = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, si = (e, t = 0, n = dx, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, hu = (e) => {
  const t = e.ctrlKey && Tn() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function fx({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: s, zoomOnPinch: a, onPanZoomStart: c, onPanZoom: u, onPanZoomEnd: l }) {
  return (d) => {
    if (Rt(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && a) {
      const w = Fe(d), x = hu(d), b = f * Math.pow(2, x);
      o.scaleTo(n, b, w, d);
      return;
    }
    const p = d.deltaMode === 1 ? 20 : 1;
    let h = r === vt.Vertical ? 0 : d.deltaX * p, g = r === vt.Horizontal ? 0 : d.deltaY * p;
    !Tn() && d.shiftKey && r !== vt.Vertical && (h = d.deltaY * p, g = 0), o.translateBy(
      n,
      -(h / f) * s,
      -(g / f) * s,
      // @ts-ignore
      { internal: !0 }
    );
    const m = cr(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(d, m), e.panScrollTimeout = setTimeout(() => {
      l?.(d, m), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(d, m));
  };
}
function px({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const s = o.type === "wheel", a = !t && s && !o.ctrlKey, c = Rt(o, e);
    if (o.ctrlKey && s && c && o.preventDefault(), a || c)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function hx({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = cr(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function gx({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (s) => {
    e.usedRightMouseButton = !!(n && pu(t, e.mouseButton ?? 0)), s.sourceEvent?.sync || o([s.transform.x, s.transform.y, s.transform.k]), r && !s.sourceEvent?.internal && r?.(s.sourceEvent, cr(s.transform));
  };
}
function yx({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: s }) {
  return (a) => {
    if (!a.sourceEvent?.internal && (e.isZoomingOrPanning = !1, s && pu(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && a.sourceEvent && s(a.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
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
function mx({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: s, userSelectionActive: a, noWheelClassName: c, noPanClassName: u, lib: l, connectionInProgress: d }) {
  return (f) => {
    const p = e || t, h = n && f.ctrlKey, g = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (Rt(f, `${l}-flow__node`) || Rt(f, `${l}-flow__edge`)))
      return !0;
    if (!o && !p && !r && !s && !n || a || d && !g || Rt(f, c) && g || Rt(f, u) && (!g || r && g && !e) || !n && f.ctrlKey && g)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!p && !r && !h && g || !o && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(o) && !o.includes(f.button) && f.type === "mousedown")
      return !1;
    const m = Array.isArray(o) && o.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || g) && m;
  };
}
function xx({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: s, onPanZoomStart: a, onPanZoomEnd: c, onDraggingChange: u }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = Vl().scaleExtent([t, n]).translateExtent(o), p = Re(e).call(f);
  b({
    x: r.x,
    y: r.y,
    zoom: qt(r.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], o);
  const h = p.on("wheel.zoom"), g = p.on("dblclick.zoom");
  f.wheelDelta(hu);
  async function m(A, _) {
    return p ? new Promise((R) => {
      f?.interpolate(_?.interpolate === "linear" ? bn : No).transform(si(p, _?.duration, _?.ease, () => R(!0)), A);
    }) : !1;
  }
  function w({ noWheelClassName: A, noPanClassName: _, onPaneContextMenu: R, userSelectionActive: C, panOnScroll: I, panOnDrag: k, panOnScrollMode: T, panOnScrollSpeed: $, preventScrolling: P, zoomOnPinch: F, zoomOnScroll: W, zoomOnDoubleClick: O, zoomActivationKeyPressed: Z, lib: U, onTransformChange: te, connectionInProgress: le, paneClickDistance: G, selectionOnDrag: M }) {
    C && !l.isZoomingOrPanning && x();
    const q = I && !Z && !C;
    f.clickDistance(M ? 1 / 0 : !Ke(G) || G < 0 ? 0 : G);
    const ae = q ? fx({
      zoomPanValues: l,
      noWheelClassName: A,
      d3Selection: p,
      d3Zoom: f,
      panOnScrollMode: T,
      panOnScrollSpeed: $,
      zoomOnPinch: F,
      onPanZoomStart: a,
      onPanZoom: s,
      onPanZoomEnd: c
    }) : px({
      noWheelClassName: A,
      preventScrolling: P,
      d3ZoomHandler: h
    });
    p.on("wheel.zoom", ae, { passive: !1 });
    const ce = hx({
      zoomPanValues: l,
      onDraggingChange: u,
      onPanZoomStart: a
    });
    f.on("start", ce);
    const Q = gx({
      zoomPanValues: l,
      panOnDrag: k,
      onPaneContextMenu: !!R,
      onPanZoom: s,
      onTransformChange: te
    });
    f.on("zoom", Q);
    const oe = yx({
      zoomPanValues: l,
      panOnDrag: k,
      panOnScroll: I,
      onPaneContextMenu: R,
      onPanZoomEnd: c,
      onDraggingChange: u
    });
    f.on("end", oe);
    const de = mx({
      zoomActivationKeyPressed: Z,
      panOnDrag: k,
      zoomOnScroll: W,
      panOnScroll: I,
      zoomOnDoubleClick: O,
      zoomOnPinch: F,
      userSelectionActive: C,
      noPanClassName: _,
      noWheelClassName: A,
      lib: U,
      connectionInProgress: le
    });
    f.filter(de), O ? p.on("dblclick.zoom", g) : p.on("dblclick.zoom", null);
  }
  function x() {
    f.on("zoom", null);
  }
  async function b(A, _, R) {
    const C = ii(A), I = f?.constrain()(C, _, R);
    return I && await m(I), I;
  }
  async function y(A, _) {
    const R = ii(A);
    return await m(R, _), R;
  }
  function v(A) {
    if (p) {
      const _ = ii(A), R = p.property("__zoom");
      (R.k !== A.zoom || R.x !== A.x || R.y !== A.y) && f?.transform(p, _, null, { sync: !0 });
    }
  }
  function j() {
    const A = p ? zl(p.node()) : { x: 0, y: 0, k: 1 };
    return { x: A.x, y: A.y, zoom: A.k };
  }
  async function N(A, _) {
    return p ? new Promise((R) => {
      f?.interpolate(_?.interpolate === "linear" ? bn : No).scaleTo(si(p, _?.duration, _?.ease, () => R(!0)), A);
    }) : !1;
  }
  async function S(A, _) {
    return p ? new Promise((R) => {
      f?.interpolate(_?.interpolate === "linear" ? bn : No).scaleBy(si(p, _?.duration, _?.ease, () => R(!0)), A);
    }) : !1;
  }
  function E(A) {
    f?.scaleExtent(A);
  }
  function D(A) {
    f?.translateExtent(A);
  }
  function L(A) {
    const _ = !Ke(A) || A < 0 ? 0 : A;
    f?.clickDistance(_);
  }
  return {
    update: w,
    destroy: x,
    setViewport: y,
    setViewportConstrained: b,
    getViewport: j,
    scaleTo: N,
    scaleBy: S,
    setScaleExtent: E,
    setTranslateExtent: D,
    syncViewport: v,
    setClickDistance: L
  };
}
var Zt;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(Zt || (Zt = {}));
function wx({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: s }) {
  const a = e - t, c = n - o, u = [a > 0 ? 1 : a < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return a && r && (u[0] = u[0] * -1), c && s && (u[1] = u[1] * -1), u;
}
function ba(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: r
  };
}
function ut(e, t) {
  return Math.max(0, t - e);
}
function dt(e, t) {
  return Math.max(0, e - t);
}
function po(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function Na(e, t) {
  return e ? !t : t;
}
function vx(e, t, n, o, r, s, a, c) {
  let { affectsX: u, affectsY: l } = t;
  const { isHorizontal: d, isVertical: f } = t, p = d && f, { xSnapped: h, ySnapped: g } = n, { minWidth: m, maxWidth: w, minHeight: x, maxHeight: b } = o, { x: y, y: v, width: j, height: N, aspectRatio: S } = e;
  let E = Math.floor(d ? h - e.pointerX : 0), D = Math.floor(f ? g - e.pointerY : 0);
  const L = j + (u ? -E : E), A = N + (l ? -D : D), _ = -s[0] * j, R = -s[1] * N;
  let C = po(L, m, w), I = po(A, x, b);
  if (a) {
    let $ = 0, P = 0;
    u && E < 0 ? $ = ut(y + E + _, a[0][0]) : !u && E > 0 && ($ = dt(y + L + _, a[1][0])), l && D < 0 ? P = ut(v + D + R, a[0][1]) : !l && D > 0 && (P = dt(v + A + R, a[1][1])), C = Math.max(C, $), I = Math.max(I, P);
  }
  if (c) {
    let $ = 0, P = 0;
    u && E > 0 ? $ = dt(y + E, c[0][0]) : !u && E < 0 && ($ = ut(y + L, c[1][0])), l && D > 0 ? P = dt(v + D, c[0][1]) : !l && D < 0 && (P = ut(v + A, c[1][1])), C = Math.max(C, $), I = Math.max(I, P);
  }
  if (r) {
    if (d) {
      const $ = po(L / S, x, b) * S;
      if (C = Math.max(C, $), a) {
        let P = 0;
        !u && !l || u && !l && p ? P = dt(v + R + L / S, a[1][1]) * S : P = ut(v + R + (u ? E : -E) / S, a[0][1]) * S, C = Math.max(C, P);
      }
      if (c) {
        let P = 0;
        !u && !l || u && !l && p ? P = ut(v + L / S, c[1][1]) * S : P = dt(v + (u ? E : -E) / S, c[0][1]) * S, C = Math.max(C, P);
      }
    }
    if (f) {
      const $ = po(A * S, m, w) / S;
      if (I = Math.max(I, $), a) {
        let P = 0;
        !u && !l || l && !u && p ? P = dt(y + A * S + _, a[1][0]) / S : P = ut(y + (l ? D : -D) * S + _, a[0][0]) / S, I = Math.max(I, P);
      }
      if (c) {
        let P = 0;
        !u && !l || l && !u && p ? P = ut(y + A * S, c[1][0]) / S : P = dt(y + (l ? D : -D) * S, c[0][0]) / S, I = Math.max(I, P);
      }
    }
  }
  D = D + (D < 0 ? I : -I), E = E + (E < 0 ? C : -C), r && (p ? L > A * S ? D = (Na(u, l) ? -E : E) / S : E = (Na(u, l) ? -D : D) * S : d ? (D = E / S, l = u) : (E = D * S, u = l));
  const k = u ? y + E : y, T = l ? v + D : v;
  return {
    width: j + (u ? -E : E),
    height: N + (l ? -D : D),
    x: s[0] * E * (u ? -1 : 1) + k,
    y: s[1] * D * (l ? -1 : 1) + T
  };
}
const gu = { width: 0, height: 0, x: 0, y: 0 }, bx = {
  ...gu,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function Nx(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, s = e.measured.width ?? 0, a = e.measured.height ?? 0, c = n[0] * s, u = n[1] * a;
  return [
    [o - c, r - u],
    [o + s - c, r + a - u]
  ];
}
function jx({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const s = Re(e);
  let a = {
    controlDirection: ba("bottom-right"),
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
    let x = { ...gu }, b = { ...bx };
    a = {
      boundaries: d,
      resizeDirection: p,
      keepAspectRatio: f,
      controlDirection: ba(l)
    };
    let y, v = null, j = [], N, S, E, D = !1;
    const L = Sl().on("start", (A) => {
      const { nodeLookup: _, transform: R, snapGrid: C, snapToGrid: I, nodeOrigin: k, paneDomNode: T } = n();
      if (y = _.get(t), !y)
        return;
      v = T?.getBoundingClientRect() ?? null;
      const { xSnapped: $, ySnapped: P } = Nn(A.sourceEvent, {
        transform: R,
        snapGrid: C,
        snapToGrid: I,
        containerBounds: v
      });
      x = {
        width: y.measured.width ?? 0,
        height: y.measured.height ?? 0,
        x: y.position.x ?? 0,
        y: y.position.y ?? 0
      }, b = {
        ...x,
        pointerX: $,
        pointerY: P,
        aspectRatio: x.width / x.height
      }, N = void 0, S = St(y.extent) ? y.extent : void 0, y.parentId && (y.extent === "parent" || y.expandParent) && (N = _.get(y.parentId)), N && y.extent === "parent" && (S = [
        [0, 0],
        [N.measured.width, N.measured.height]
      ]), j = [], E = void 0;
      for (const [F, W] of _)
        if (W.parentId === t && (j.push({
          id: F,
          position: { ...W.position },
          extent: W.extent
        }), W.extent === "parent" || W.expandParent)) {
          const O = Nx(W, y, W.origin ?? k);
          E ? E = [
            [Math.min(O[0][0], E[0][0]), Math.min(O[0][1], E[0][1])],
            [Math.max(O[1][0], E[1][0]), Math.max(O[1][1], E[1][1])]
          ] : E = O;
        }
      h?.(A, { ...x });
    }).on("drag", (A) => {
      const { transform: _, snapGrid: R, snapToGrid: C, nodeOrigin: I } = n(), k = Nn(A.sourceEvent, {
        transform: _,
        snapGrid: R,
        snapToGrid: C,
        containerBounds: v
      }), T = [];
      if (!y)
        return;
      const { x: $, y: P, width: F, height: W } = x, O = {}, Z = y.origin ?? I, { width: U, height: te, x: le, y: G } = vx(b, a.controlDirection, k, a.boundaries, a.keepAspectRatio, Z, S, E), M = U !== F, q = te !== W, ae = le !== $ && M, ce = G !== P && q;
      if (!ae && !ce && !M && !q)
        return;
      if ((ae || ce || Z[0] === 1 || Z[1] === 1) && (O.x = ae ? le : x.x, O.y = ce ? G : x.y, x.x = O.x, x.y = O.y, j.length > 0)) {
        const V = le - $, ee = G - P;
        for (const ge of j)
          ge.position = {
            x: ge.position.x - V + Z[0] * (U - F),
            y: ge.position.y - ee + Z[1] * (te - W)
          }, T.push(ge);
      }
      if ((M || q) && (O.width = M && (!a.resizeDirection || a.resizeDirection === "horizontal") ? U : x.width, O.height = q && (!a.resizeDirection || a.resizeDirection === "vertical") ? te : x.height, x.width = O.width, x.height = O.height), N && y.expandParent) {
        const V = Z[0] * (O.width ?? 0);
        O.x && O.x < V && (x.x = V, b.x = b.x - (O.x - V));
        const ee = Z[1] * (O.height ?? 0);
        O.y && O.y < ee && (x.y = ee, b.y = b.y - (O.y - ee));
      }
      const Q = wx({
        width: x.width,
        prevWidth: F,
        height: x.height,
        prevHeight: W,
        affectsX: a.controlDirection.affectsX,
        affectsY: a.controlDirection.affectsY
      }), oe = { ...x, direction: Q };
      w?.(A, oe) !== !1 && (D = !0, g?.(A, oe), o(O, T));
    }).on("end", (A) => {
      D && (m?.(A, { ...x }), r?.({ ...x }), D = !1);
    });
    s.call(L);
  }
  function u() {
    s.on(".drag", null);
  }
  return {
    update: c,
    destroy: u
  };
}
var ai = { exports: {} }, ci = {}, li = { exports: {} }, ui = {};
var ja;
function Sx() {
  if (ja) return ui;
  ja = 1;
  var e = Ge;
  function t(f, p) {
    return f === p && (f !== 0 || 1 / f === 1 / p) || f !== f && p !== p;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, s = e.useLayoutEffect, a = e.useDebugValue;
  function c(f, p) {
    var h = p(), g = o({ inst: { value: h, getSnapshot: p } }), m = g[0].inst, w = g[1];
    return s(
      function() {
        m.value = h, m.getSnapshot = p, u(m) && w({ inst: m });
      },
      [f, h, p]
    ), r(
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
  return ui.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : d, ui;
}
var Sa;
function Cx() {
  return Sa || (Sa = 1, li.exports = Sx()), li.exports;
}
var Ca;
function Ex() {
  if (Ca) return ci;
  Ca = 1;
  var e = Ge, t = Cx();
  function n(l, d) {
    return l === d && (l !== 0 || 1 / l === 1 / d) || l !== l && d !== d;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, s = e.useRef, a = e.useEffect, c = e.useMemo, u = e.useDebugValue;
  return ci.useSyncExternalStoreWithSelector = function(l, d, f, p, h) {
    var g = s(null);
    if (g.current === null) {
      var m = { hasValue: !1, value: null };
      g.current = m;
    } else m = g.current;
    g = c(
      function() {
        function x(N) {
          if (!b) {
            if (b = !0, y = N, N = p(N), h !== void 0 && m.hasValue) {
              var S = m.value;
              if (h(S, N))
                return v = S;
            }
            return v = N;
          }
          if (S = v, o(y, N)) return S;
          var E = p(N);
          return h !== void 0 && h(S, E) ? (y = N, S) : (y = N, v = E);
        }
        var b = !1, y, v, j = f === void 0 ? null : f;
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
    var w = r(l, g[0], g[1]);
    return a(
      function() {
        m.hasValue = !0, m.value = w;
      },
      [w]
    ), u(w), w;
  }, ci;
}
var Ea;
function Ix() {
  return Ea || (Ea = 1, ai.exports = Ex()), ai.exports;
}
var kx = Ix();
const Ax = /* @__PURE__ */ Zd(kx), _x = {}, Ia = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (d, f) => {
    const p = typeof d == "function" ? d(t) : d;
    if (!Object.is(p, t)) {
      const h = t;
      t = f ?? (typeof p != "object" || p === null) ? p : Object.assign({}, t, p), n.forEach((g) => g(t, h));
    }
  }, r = () => t, u = { setState: o, getState: r, getInitialState: () => l, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (_x ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(o, r, u);
  return u;
}, Dx = (e) => e ? Ia(e) : Ia, { useDebugValue: Tx } = Ge, { useSyncExternalStoreWithSelector: Px } = Ax, $x = (e) => e;
function yu(e, t = $x, n) {
  const o = Px(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return Tx(o), o;
}
const ka = (e, t) => {
  const n = Dx(e), o = (r, s = t) => yu(n, r, s);
  return Object.assign(o, n), o;
}, Mx = (e, t) => e ? ka(e, t) : ka;
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
var di = { exports: {} }, _e = {};
var Aa;
function Rx() {
  if (Aa) return _e;
  Aa = 1;
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
  return _e.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, _e.createPortal = function(u, l) {
    var d = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!l || l.nodeType !== 1 && l.nodeType !== 9 && l.nodeType !== 11)
      throw Error(t(299));
    return s(u, l, null, d);
  }, _e.flushSync = function(u) {
    var l = a.T, d = o.p;
    try {
      if (a.T = null, o.p = 2, u) return u();
    } finally {
      a.T = l, o.p = d, o.d.f();
    }
  }, _e.preconnect = function(u, l) {
    typeof u == "string" && (l ? (l = l.crossOrigin, l = typeof l == "string" ? l === "use-credentials" ? l : "" : void 0) : l = null, o.d.C(u, l));
  }, _e.prefetchDNS = function(u) {
    typeof u == "string" && o.d.D(u);
  }, _e.preinit = function(u, l) {
    if (typeof u == "string" && l && typeof l.as == "string") {
      var d = l.as, f = c(d, l.crossOrigin), p = typeof l.integrity == "string" ? l.integrity : void 0, h = typeof l.fetchPriority == "string" ? l.fetchPriority : void 0;
      d === "style" ? o.d.S(
        u,
        typeof l.precedence == "string" ? l.precedence : void 0,
        {
          crossOrigin: f,
          integrity: p,
          fetchPriority: h
        }
      ) : d === "script" && o.d.X(u, {
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
          o.d.M(u, {
            crossOrigin: d,
            integrity: typeof l.integrity == "string" ? l.integrity : void 0,
            nonce: typeof l.nonce == "string" ? l.nonce : void 0
          });
        }
      } else l == null && o.d.M(u);
  }, _e.preload = function(u, l) {
    if (typeof u == "string" && typeof l == "object" && l !== null && typeof l.as == "string") {
      var d = l.as, f = c(d, l.crossOrigin);
      o.d.L(u, d, {
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
        o.d.m(u, {
          as: typeof l.as == "string" && l.as !== "script" ? l.as : void 0,
          crossOrigin: d,
          integrity: typeof l.integrity == "string" ? l.integrity : void 0
        });
      } else o.d.m(u);
  }, _e.requestFormReset = function(u) {
    o.d.r(u);
  }, _e.unstable_batchedUpdates = function(u, l) {
    return u(l);
  }, _e.useFormState = function(u, l, d) {
    return a.H.useFormState(u, l, d);
  }, _e.useFormStatus = function() {
    return a.H.useHostTransitionStatus();
  }, _e.version = "19.2.7", _e;
}
var _a;
function Lx() {
  if (_a) return di.exports;
  _a = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), di.exports = Rx(), di.exports;
}
var zx = Lx();
const lr = Oi(null), Vx = lr.Provider, mu = Oe.error001("react");
function he(e, t) {
  const n = $n(lr);
  if (n === null)
    throw new Error(mu);
  return yu(n, e, t);
}
function xe() {
  const e = $n(lr);
  if (e === null)
    throw new Error(mu);
  return fe(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const Da = { display: "none" }, Ox = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, xu = "react-flow__node-desc", wu = "react-flow__edge-desc", Hx = "react-flow__aria-live", Wx = (e) => e.ariaLiveMessage, Fx = (e) => e.ariaLabelConfig;
function Bx({ rfId: e }) {
  const t = he(Wx);
  return i.jsx("div", { id: `${Hx}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: Ox, children: t });
}
function Kx({ rfId: e, disableKeyboardA11y: t }) {
  const n = he(Fx);
  return i.jsxs(i.Fragment, { children: [i.jsx("div", { id: `${xu}-${e}`, style: Da, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), i.jsx("div", { id: `${wu}-${e}`, style: Da, children: n["edge.a11yDescription.default"] }), !t && i.jsx(Bx, { rfId: e })] });
}
const ur = Ac(({ position: e = "top-left", children: t, className: n, style: o, ...r }, s) => {
  const a = `${e}`.split("-");
  return i.jsx("div", { className: Se(["react-flow__panel", n, ...a]), style: o, ref: s, ...r, children: t });
});
ur.displayName = "Panel";
function Xx({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : i.jsx(ur, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: i.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const qx = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, ho = (e) => e.id;
function Yx(e, t) {
  return me(e.selectedNodes.map(ho), t.selectedNodes.map(ho)) && me(e.selectedEdges.map(ho), t.selectedEdges.map(ho));
}
function Ux({ onSelectionChange: e }) {
  const t = xe(), { selectedNodes: n, selectedEdges: o } = he(qx, Yx);
  return J(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((s) => s(r));
  }, [n, o, e]), null;
}
const Zx = (e) => !!e.onSelectionChangeHandlers;
function Gx({ onSelectionChange: e }) {
  const t = he(Zx);
  return e || t ? i.jsx(Ux, { onSelectionChange: e }) : null;
}
const vu = [0, 0], Jx = { x: 0, y: 0, zoom: 1 }, Qx = [
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
], Ta = [...Qx, "rfId"], ew = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), Pa = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: An,
  nodeOrigin: vu,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function tw(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: s, setNodeExtent: a, reset: c, setDefaultNodesAndEdges: u } = he(ew, me), l = xe();
  J(() => (u(e.defaultNodes, e.defaultEdges), () => {
    d.current = Pa, c();
  }), []);
  const d = re(Pa);
  return J(
    () => {
      for (const f of Ta) {
        const p = e[f], h = d.current[f];
        p !== h && (typeof e[f] > "u" || (f === "nodes" ? t(p) : f === "edges" ? n(p) : f === "minZoom" ? o(p) : f === "maxZoom" ? r(p) : f === "translateExtent" ? s(p) : f === "nodeExtent" ? a(p) : f === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: Rm(p) }) : f === "fitView" ? l.setState({ fitViewQueued: p }) : f === "fitViewOptions" ? l.setState({ fitViewOptions: p }) : l.setState({ [f]: p })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    Ta.map((f) => e[f])
  ), null;
}
function $a() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function nw(e) {
  const [t, n] = B(e === "system" ? null : e);
  return J(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = $a(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : $a()?.matches ? "dark" : "light";
}
const Ma = typeof document < "u" ? document : null;
function Pn(e = null, t = { target: Ma, actInsideInputWithModifier: !0 }) {
  const [n, o] = B(!1), r = re(!1), s = re(/* @__PURE__ */ new Set([])), [a, c] = fe(() => {
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
    const u = t?.target ?? Ma, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (h) => {
        if (r.current = h.ctrlKey || h.metaKey || h.shiftKey || h.altKey, (!r.current || r.current && !l) && Jl(h))
          return !1;
        const m = La(h.code, c);
        if (s.current.add(h[m]), Ra(a, s.current, !1)) {
          const w = h.composedPath?.()?.[0] || h.target, x = w?.nodeName === "BUTTON" || w?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !x) && h.preventDefault(), o(!0);
        }
      }, f = (h) => {
        const g = La(h.code, c);
        Ra(a, s.current, !0) ? (o(!1), s.current.clear()) : s.current.delete(h[g]), h.key === "Meta" && s.current.clear(), r.current = !1;
      }, p = () => {
        s.current.clear(), o(!1);
      };
      return u?.addEventListener("keydown", d), u?.addEventListener("keyup", f), window.addEventListener("blur", p), window.addEventListener("contextmenu", p), () => {
        u?.removeEventListener("keydown", d), u?.removeEventListener("keyup", f), window.removeEventListener("blur", p), window.removeEventListener("contextmenu", p);
      };
    }
  }, [e, o]), n;
}
function Ra(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function La(e, t) {
  return t.includes(e) ? "code" : "key";
}
const ow = () => {
  const e = xe();
  return fe(() => ({
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
      const { transform: [o, r, s], panZoom: a } = e.getState();
      return a ? (await a.setViewport({
        x: t.x ?? o,
        y: t.y ?? r,
        zoom: t.zoom ?? s
      }, n), !0) : !1;
    },
    getViewport: () => {
      const [t, n, o] = e.getState().transform;
      return { x: t, y: n, zoom: o };
    },
    setCenter: async (t, n, o) => e.getState().setCenter(t, n, o),
    fitBounds: async (t, n) => {
      const { width: o, height: r, minZoom: s, maxZoom: a, panZoom: c } = e.getState(), u = rs(t, o, r, s, a, n?.padding ?? 0.1);
      return c ? (await c.setViewport(u, {
        duration: n?.duration,
        ease: n?.ease,
        interpolate: n?.interpolate
      }), !0) : !1;
    },
    screenToFlowPosition: (t, n = {}) => {
      const { transform: o, snapGrid: r, snapToGrid: s, domNode: a } = e.getState();
      if (!a)
        return t;
      const { x: c, y: u } = a.getBoundingClientRect(), l = {
        x: t.x - c,
        y: t.y - u
      }, d = n.snapGrid ?? r, f = n.snapToGrid ?? s;
      return tn(l, o, f, d);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: r, y: s } = o.getBoundingClientRect(), a = Ut(t, n);
      return {
        x: a.x + r,
        y: a.y + s
      };
    }
  }), []);
};
function bu(e, t) {
  const n = [], o = /* @__PURE__ */ new Map(), r = [];
  for (const s of e)
    if (s.type === "add") {
      r.push(s);
      continue;
    } else if (s.type === "remove" || s.type === "replace")
      o.set(s.id, [s]);
    else {
      const a = o.get(s.id);
      a ? a.push(s) : o.set(s.id, [s]);
    }
  for (const s of t) {
    const a = o.get(s.id);
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
      rw(u, c);
    n.push(c);
  }
  return r.length && r.forEach((s) => {
    s.index !== void 0 ? n.splice(s.index, 0, { ...s.item }) : n.push({ ...s.item });
  }), n;
}
function rw(e, t) {
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
function Nu(e, t) {
  return bu(e, t);
}
function ju(e, t) {
  return bu(e, t);
}
function yt(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function Lt(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [r, s] of e) {
    const a = t.has(r);
    !(s.selected === void 0 && !a) && s.selected !== a && (n && (s.selected = a), o.push(yt(s.id, a)));
  }
  return o;
}
function za({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((r) => [r.id, r]));
  for (const [r, s] of e.entries()) {
    const a = t.get(s.id), c = a?.internals?.userNode ?? a;
    c !== void 0 && c !== s && n.push({ id: s.id, item: s, type: "replace" }), c === void 0 && n.push({ item: s, type: "add", index: r });
  }
  for (const [r] of t)
    o.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function Va(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const Su = Yl();
function Cu(e, t, n = {}) {
  return Hm(e, t, {
    ...n,
    onError: n.onError ?? Su
  });
}
function iw(e, t, n, o = { shouldReplaceId: !0 }) {
  return Wm(e, t, n, {
    ...o,
    onError: o.onError ?? Su
  });
}
const Oa = (e) => Im(e), sw = (e) => Bl(e);
function Eu(e) {
  return Ac(e);
}
const aw = typeof window < "u" ? Td : J;
function Ha(e) {
  const [t, n] = B(BigInt(0)), [o] = B(() => cw(() => n((r) => r + BigInt(1))));
  return aw(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function cw(e) {
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
const Iu = Oi(null);
function lw({ children: e }) {
  const t = xe(), n = ie((c) => {
    const { nodes: u = [], setNodes: l, hasDefaultNodes: d, onNodesChange: f, nodeLookup: p, fitViewQueued: h, onNodesChangeMiddlewareMap: g } = t.getState();
    let m = u;
    for (const x of c)
      m = typeof x == "function" ? x(m) : x;
    let w = za({
      items: m,
      lookup: p
    });
    for (const x of g.values())
      w = x(w);
    d && l(m), w.length > 0 ? f?.(w) : h && window.requestAnimationFrame(() => {
      const { fitViewQueued: x, nodes: b, setNodes: y } = t.getState();
      x && y(b);
    });
  }, []), o = Ha(n), r = ie((c) => {
    const { edges: u = [], setEdges: l, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: p } = t.getState();
    let h = u;
    for (const g of c)
      h = typeof g == "function" ? g(h) : g;
    d ? l(h) : f && f(za({
      items: h,
      lookup: p
    }));
  }, []), s = Ha(r), a = fe(() => ({ nodeQueue: o, edgeQueue: s }), []);
  return i.jsx(Iu.Provider, { value: a, children: e });
}
function uw() {
  const e = $n(Iu);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const dw = (e) => !!e.panZoom;
function ds() {
  const e = ow(), t = xe(), n = uw(), o = he(dw), r = fe(() => {
    const s = (f) => t.getState().nodeLookup.get(f), a = (f) => {
      n.nodeQueue.push(f);
    }, c = (f) => {
      n.edgeQueue.push(f);
    }, u = (f) => {
      const { nodeLookup: p, nodeOrigin: h } = t.getState(), g = Oa(f) ? f : p.get(f.id), m = g.parentId ? Zl(g.position, g.measured, g.parentId, p, h) : g.position, w = {
        ...g,
        position: m,
        width: g.measured?.width ?? g.width,
        height: g.measured?.height ?? g.height
      };
      return Yt(w);
    }, l = (f, p, h = { replace: !1 }) => {
      a((g) => g.map((m) => {
        if (m.id === f) {
          const w = typeof p == "function" ? p(m) : p;
          return h.replace && Oa(w) ? w : { ...m, ...w };
        }
        return m;
      }));
    }, d = (f, p, h = { replace: !1 }) => {
      c((g) => g.map((m) => {
        if (m.id === f) {
          const w = typeof p == "function" ? p(m) : p;
          return h.replace && sw(w) ? w : { ...m, ...w };
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
        const { nodes: h, edges: g, onNodesDelete: m, onEdgesDelete: w, triggerNodeChanges: x, triggerEdgeChanges: b, onDelete: y, onBeforeDelete: v } = t.getState(), { nodes: j, edges: N } = await Tm({
          nodesToRemove: f,
          edgesToRemove: p,
          nodes: h,
          edges: g,
          onBeforeDelete: v
        }), S = N.length > 0, E = j.length > 0;
        if (S) {
          const D = N.map(Va);
          w?.(N), b(D);
        }
        if (E) {
          const D = j.map(Va);
          m?.(j), x(D);
        }
        return (E || S) && y?.({ nodes: j, edges: N }), { deletedNodes: j, deletedEdges: N };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, p = !0, h) => {
        const g = ua(f), m = g ? f : u(f), w = h !== void 0;
        return m ? (h || t.getState().nodes).filter((x) => {
          const b = t.getState().nodeLookup.get(x.id);
          if (b && !g && (x.id === f.id || !b.internals.positionAbsolute))
            return !1;
          const y = Yt(w ? x : b), v = Dn(y, m);
          return p && v > 0 || v >= y.width * y.height || v >= m.width * m.height;
        }) : [];
      },
      isNodeIntersecting: (f, p, h = !0) => {
        const m = ua(f) ? f : u(f);
        if (!m)
          return !1;
        const w = Dn(m, p);
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
        return km(f, { nodeLookup: p, nodeOrigin: h });
      },
      getHandleConnections: ({ type: f, id: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}-${f}${p ? `-${p}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}${f ? p ? `-${f}-${p}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const p = t.getState().fitViewResolver ?? Mm();
        return t.setState({ fitViewQueued: !0, fitViewOptions: f, fitViewResolver: p }), n.nodeQueue.push((h) => [...h]), p.promise;
      }
    };
  }, []);
  return fe(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const Wa = (e) => e.selected, fw = typeof window < "u" ? window : void 0;
function pw({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = xe(), { deleteElements: o } = ds(), r = Pn(e, { actInsideInputWithModifier: !1 }), s = Pn(t, { target: fw });
  J(() => {
    if (r) {
      const { edges: a, nodes: c } = n.getState();
      o({ nodes: c.filter(Wa), edges: a.filter(Wa) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), J(() => {
    n.setState({ multiSelectionActive: s });
  }, [s]);
}
function hw(e) {
  const t = xe();
  J(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = is(e.current);
      (o.height === 0 || o.width === 0) && t.getState().onError?.("004", Oe.error004()), t.setState({ width: o.width || 500, height: o.height || 500 });
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
const dr = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, gw = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function yw({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: s = vt.Free, zoomOnDoubleClick: a = !0, panOnDrag: c = !0, defaultViewport: u, translateExtent: l, minZoom: d, maxZoom: f, zoomActivationKeyCode: p, preventScrolling: h = !0, children: g, noWheelClassName: m, noPanClassName: w, onViewportChange: x, isControlledViewport: b, paneClickDistance: y, selectionOnDrag: v }) {
  const j = xe(), N = re(null), { userSelectionActive: S, lib: E, connectionInProgress: D } = he(gw, me), L = Pn(p), A = re();
  hw(N);
  const _ = ie((R) => {
    x?.({ x: R[0], y: R[1], zoom: R[2] }), b || j.setState({ transform: R });
  }, [x, b]);
  return J(() => {
    if (N.current) {
      A.current = xx({
        domNode: N.current,
        minZoom: d,
        maxZoom: f,
        translateExtent: l,
        viewport: u,
        onDraggingChange: (k) => j.setState((T) => T.paneDragging === k ? T : { paneDragging: k }),
        onPanZoomStart: (k, T) => {
          const { onViewportChangeStart: $, onMoveStart: P } = j.getState();
          P?.(k, T), $?.(T);
        },
        onPanZoom: (k, T) => {
          const { onViewportChange: $, onMove: P } = j.getState();
          P?.(k, T), $?.(T);
        },
        onPanZoomEnd: (k, T) => {
          const { onViewportChangeEnd: $, onMoveEnd: P } = j.getState();
          P?.(k, T), $?.(T);
        }
      });
      const { x: R, y: C, zoom: I } = A.current.getViewport();
      return j.setState({
        panZoom: A.current,
        transform: [R, C, I],
        domNode: N.current.closest(".react-flow")
      }), () => {
        A.current?.destroy();
      };
    }
  }, []), J(() => {
    A.current?.update({
      onPaneContextMenu: e,
      zoomOnScroll: t,
      zoomOnPinch: n,
      panOnScroll: o,
      panOnScrollSpeed: r,
      panOnScrollMode: s,
      zoomOnDoubleClick: a,
      panOnDrag: c,
      zoomActivationKeyPressed: L,
      preventScrolling: h,
      noPanClassName: w,
      userSelectionActive: S,
      noWheelClassName: m,
      lib: E,
      onTransformChange: _,
      connectionInProgress: D,
      selectionOnDrag: v,
      paneClickDistance: y
    });
  }, [
    e,
    t,
    n,
    o,
    r,
    s,
    a,
    c,
    L,
    h,
    w,
    S,
    m,
    E,
    _,
    D,
    v,
    y
  ]), i.jsx("div", { className: "react-flow__renderer", ref: N, style: dr, children: g });
}
const mw = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function xw() {
  const { userSelectionActive: e, userSelectionRect: t } = he(mw, me);
  return e && t ? i.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const fi = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, ww = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function vw({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = _n.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: s, selectionOnDrag: a, onSelectionStart: c, onSelectionEnd: u, onPaneClick: l, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: p, onPaneMouseMove: h, onPaneMouseLeave: g, children: m }) {
  const w = re(0), x = xe(), { userSelectionActive: b, elementsSelectable: y, dragging: v, connectionInProgress: j, panBy: N, autoPanSpeed: S } = he(ww, me), E = y && (e || b), D = re(null), L = re(), A = re(/* @__PURE__ */ new Set()), _ = re(/* @__PURE__ */ new Set()), R = re(!1), C = re({ x: 0, y: 0 }), I = re(!1), k = (M) => {
    if (R.current || j) {
      R.current = !1;
      return;
    }
    l?.(M), x.getState().resetSelectedElements(), x.setState({ nodesSelectionActive: !1 });
  }, T = (M) => {
    if (Array.isArray(o) && o?.includes(2)) {
      M.preventDefault();
      return;
    }
    d?.(M);
  }, $ = f ? (M) => f(M) : void 0, P = (M) => {
    R.current && (M.stopPropagation(), R.current = !1);
  }, F = (M) => {
    const { domNode: q, transform: ae } = x.getState();
    if (L.current = q?.getBoundingClientRect(), !L.current)
      return;
    const ce = M.target === D.current;
    if (!ce && !!M.target.closest(".nokey") || !e || !(a && ce || t) || M.button !== 0 || !M.isPrimary)
      return;
    M.target?.setPointerCapture?.(M.pointerId), R.current = !1;
    const { x: de, y: V } = Xe(M.nativeEvent, L.current), ee = tn({ x: de, y: V }, ae);
    x.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: ee.x,
        startY: ee.y,
        x: de,
        y: V
      }
    }), ce || (M.stopPropagation(), M.preventDefault());
  };
  function W(M, q) {
    const { userSelectionRect: ae } = x.getState();
    if (!ae)
      return;
    const { transform: ce, nodeLookup: Q, edgeLookup: oe, connectionLookup: de, triggerNodeChanges: V, triggerEdgeChanges: ee, defaultEdgeOptions: ge } = x.getState(), we = { x: ae.startX, y: ae.startY }, { x: Ae, y: Ie } = Ut(we, ce), Te = {
      startX: we.x,
      startY: we.y,
      x: M < Ae ? M : Ae,
      y: q < Ie ? q : Ie,
      width: Math.abs(M - Ae),
      height: Math.abs(q - Ie)
    }, tt = A.current, We = _.current;
    A.current = new Set(ns(Q, Te, ce, n === _n.Partial, !0).map((H) => H.id)), _.current = /* @__PURE__ */ new Set();
    const z = ge?.selectable ?? !0;
    for (const H of A.current) {
      const K = de.get(H);
      if (K)
        for (const { edgeId: X } of K.values()) {
          const se = oe.get(X);
          se && (se.selectable ?? z) && _.current.add(X);
        }
    }
    if (!da(tt, A.current)) {
      const H = Lt(Q, A.current, !0);
      V(H);
    }
    if (!da(We, _.current)) {
      const H = Lt(oe, _.current);
      ee(H);
    }
    x.setState({
      userSelectionRect: Te,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function O() {
    if (!r || !L.current)
      return;
    const [M, q] = os(C.current, L.current, S);
    N({ x: M, y: q }).then((ae) => {
      if (!R.current || !ae) {
        w.current = requestAnimationFrame(O);
        return;
      }
      const { x: ce, y: Q } = C.current;
      W(ce, Q), w.current = requestAnimationFrame(O);
    });
  }
  const Z = () => {
    cancelAnimationFrame(w.current), w.current = 0, I.current = !1;
  };
  J(() => () => Z(), []);
  const U = (M) => {
    const { userSelectionRect: q, transform: ae, resetSelectedElements: ce } = x.getState();
    if (!L.current || !q)
      return;
    const { x: Q, y: oe } = Xe(M.nativeEvent, L.current);
    C.current = { x: Q, y: oe };
    const de = Ut({ x: q.startX, y: q.startY }, ae);
    if (!R.current) {
      const V = t ? 0 : s;
      if (Math.hypot(Q - de.x, oe - de.y) <= V)
        return;
      ce(), c?.(M);
    }
    R.current = !0, I.current || (O(), I.current = !0), W(Q, oe);
  }, te = (M) => {
    M.button === 0 && (M.target?.releasePointerCapture?.(M.pointerId), !b && M.target === D.current && x.getState().userSelectionRect && k?.(M), x.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), R.current && (u?.(M), x.setState({
      nodesSelectionActive: A.current.size > 0
    })), Z());
  }, le = (M) => {
    M.target?.releasePointerCapture?.(M.pointerId), Z();
  }, G = o === !0 || Array.isArray(o) && o.includes(0);
  return i.jsxs("div", { className: Se(["react-flow__pane", { draggable: G, dragging: v, selection: e }]), onClick: E ? void 0 : fi(k, D), onContextMenu: fi(T, D), onWheel: fi($, D), onPointerEnter: E ? void 0 : p, onPointerMove: E ? U : h, onPointerUp: E ? te : void 0, onPointerCancel: E ? le : void 0, onPointerDownCapture: E ? F : void 0, onClickCapture: E ? P : void 0, onPointerLeave: g, ref: D, style: dr, children: [m, i.jsx(xw, {})] });
}
function Ti({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: s, multiSelectionActive: a, nodeLookup: c, onError: u } = t.getState(), l = c.get(e);
  if (!l) {
    u?.("012", Oe.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && a) && (s({ nodes: [l], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function ku({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: s, nodeClickDistance: a }) {
  const c = xe(), [u, l] = B(!1), d = re();
  return J(() => {
    d.current = rx({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (f) => {
        Ti({
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
        handleSelector: o,
        domNode: e.current,
        isSelectable: s,
        nodeId: r,
        nodeClickDistance: a
      }), () => {
        d.current?.destroy();
      };
  }, [n, o, t, s, e, r, a]), u;
}
const bw = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function Au() {
  const e = xe();
  return ie((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: s, nodesDraggable: a, onError: c, updateNodePositions: u, nodeLookup: l, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), p = bw(a), h = r ? s[0] : 5, g = r ? s[1] : 5, m = n.direction.x * h * n.factor, w = n.direction.y * g * n.factor;
    for (const [, x] of l) {
      if (!p(x))
        continue;
      let b = {
        x: x.internals.positionAbsolute.x + m,
        y: x.internals.positionAbsolute.y + w
      };
      r && (b = Hn(b, s));
      const { position: y, positionAbsolute: v } = Kl({
        nodeId: x.id,
        nextPosition: b,
        nodeLookup: l,
        nodeExtent: o,
        nodeOrigin: d,
        onError: c
      });
      x.position = y, x.internals.positionAbsolute = v, f.set(x.id, x);
    }
    u(f);
  }, []);
}
const fs = Oi(null), Nw = fs.Provider;
fs.Consumer;
const _u = () => $n(fs), jw = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), Sw = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: s, connection: a } = o, { fromHandle: c, toHandle: u, isValid: l } = a, d = u?.nodeId === e && u?.id === t && u?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: d,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: s === Xt.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!r,
    valid: d && l
  };
};
function Cw({ type: e = "source", position: t = ne.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: s = !0, id: a, onConnect: c, children: u, className: l, onMouseDown: d, onTouchStart: f, ...p }, h) {
  const g = a || null, m = e === "target", w = xe(), x = _u(), { connectOnClick: b, noPanClassName: y, rfId: v } = he(jw, me), { connectingFrom: j, connectingTo: N, clickConnecting: S, isPossibleEndHandle: E, connectionInProcess: D, clickConnectionInProcess: L, valid: A } = he(Sw(x, g, e), me);
  x || w.getState().onError?.("010", Oe.error010());
  const _ = (I) => {
    const { defaultEdgeOptions: k, onConnect: T, hasDefaultEdges: $ } = w.getState(), P = {
      ...k,
      ...I
    };
    if ($) {
      const { edges: F, setEdges: W, onError: O } = w.getState();
      W(Cu(P, F, { onError: O }));
    }
    T?.(P), c?.(P);
  }, R = (I) => {
    if (!x)
      return;
    const k = Ql(I.nativeEvent);
    if (r && (k && I.button === 0 || !k)) {
      const T = w.getState();
      Di.onPointerDown(I.nativeEvent, {
        handleDomNode: I.currentTarget,
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
        onConnect: _,
        isValidConnection: n || ((...$) => w.getState().isValidConnection?.(...$) ?? !0),
        getTransform: () => w.getState().transform,
        getFromHandle: () => w.getState().connection.fromHandle,
        autoPanSpeed: T.autoPanSpeed,
        dragThreshold: T.connectionDragThreshold
      });
    }
    k ? d?.(I) : f?.(I);
  }, C = (I) => {
    const { onClickConnectStart: k, onClickConnectEnd: T, connectionClickStartHandle: $, connectionMode: P, isValidConnection: F, lib: W, rfId: O, nodeLookup: Z, connection: U } = w.getState();
    if (!x || !$ && !r)
      return;
    if (!$) {
      k?.(I.nativeEvent, { nodeId: x, handleId: g, handleType: e }), w.setState({ connectionClickStartHandle: { nodeId: x, type: e, id: g } });
      return;
    }
    const te = Gl(I.target), le = n || F, { connection: G, isValid: M } = Di.isValid(I.nativeEvent, {
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
      flowId: O,
      doc: te,
      lib: W,
      nodeLookup: Z
    });
    M && G && _(G);
    const q = structuredClone(U);
    delete q.inProgress, q.toPosition = q.toHandle ? q.toHandle.position : null, T?.(I, q), w.setState({ connectionClickStartHandle: null });
  };
  return i.jsx("div", { "data-handleid": g, "data-nodeid": x, "data-handlepos": t, "data-id": `${v}-${x}-${g}-${e}`, className: Se([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    y,
    l,
    {
      source: !m,
      target: m,
      connectable: o,
      connectablestart: r,
      connectableend: s,
      clickconnecting: S,
      connectingfrom: j,
      connectingto: N,
      valid: A,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!D || E) && (D || L ? s : r)
    }
  ]), onMouseDown: R, onTouchStart: R, onClick: b ? C : void 0, ref: h, ...p, children: u });
}
const Gt = be(Eu(Cw));
function Ew({ data: e, isConnectable: t, sourcePosition: n = ne.Bottom }) {
  return i.jsxs(i.Fragment, { children: [e?.label, i.jsx(Gt, { type: "source", position: n, isConnectable: t })] });
}
function Iw({ data: e, isConnectable: t, targetPosition: n = ne.Top, sourcePosition: o = ne.Bottom }) {
  return i.jsxs(i.Fragment, { children: [i.jsx(Gt, { type: "target", position: n, isConnectable: t }), e?.label, i.jsx(Gt, { type: "source", position: o, isConnectable: t })] });
}
function kw() {
  return null;
}
function Aw({ data: e, isConnectable: t, targetPosition: n = ne.Top }) {
  return i.jsxs(i.Fragment, { children: [i.jsx(Gt, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const Yo = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, Fa = {
  input: Ew,
  default: Iw,
  output: Aw,
  group: kw
};
function _w(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const Dw = (e) => {
  const { width: t, height: n, x: o, y: r } = On(e.nodeLookup, {
    filter: (s) => !!s.selected
  });
  return {
    width: Ke(t) ? t : null,
    height: Ke(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function Tw({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = xe(), { width: r, height: s, transformString: a, userSelectionActive: c } = he(Dw, me), u = Au(), l = re(null);
  J(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !c && r !== null && s !== null;
  if (ku({
    nodeRef: l,
    disabled: !d
  }), !d)
    return null;
  const f = e ? (h) => {
    const g = o.getState().nodes.filter((m) => m.selected);
    e(h, g);
  } : void 0, p = (h) => {
    Object.prototype.hasOwnProperty.call(Yo, h.key) && (h.preventDefault(), u({
      direction: Yo[h.key],
      factor: h.shiftKey ? 4 : 1
    }));
  };
  return i.jsx("div", { className: Se(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: a
  }, children: i.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : p, style: {
    width: r,
    height: s
  } }) });
}
const Ba = typeof window < "u" ? window : void 0, Pw = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function Du({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: s, onPaneScroll: a, paneClickDistance: c, deleteKeyCode: u, selectionKeyCode: l, selectionOnDrag: d, selectionMode: f, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: g, panActivationKeyCode: m, zoomActivationKeyCode: w, elementsSelectable: x, zoomOnScroll: b, zoomOnPinch: y, panOnScroll: v, panOnScrollSpeed: j, panOnScrollMode: N, zoomOnDoubleClick: S, panOnDrag: E, autoPanOnSelection: D, defaultViewport: L, translateExtent: A, minZoom: _, maxZoom: R, preventScrolling: C, onSelectionContextMenu: I, noWheelClassName: k, noPanClassName: T, disableKeyboardA11y: $, onViewportChange: P, isControlledViewport: F }) {
  const { nodesSelectionActive: W, userSelectionActive: O } = he(Pw, me), Z = Pn(l, { target: Ba }), U = Pn(m, { target: Ba }), te = U || E, le = U || v, G = d && te !== !0, M = Z || O || G;
  return pw({ deleteKeyCode: u, multiSelectionKeyCode: g }), i.jsx(yw, { onPaneContextMenu: s, elementsSelectable: x, zoomOnScroll: b, zoomOnPinch: y, panOnScroll: le, panOnScrollSpeed: j, panOnScrollMode: N, zoomOnDoubleClick: S, panOnDrag: !Z && te, defaultViewport: L, translateExtent: A, minZoom: _, maxZoom: R, zoomActivationKeyCode: w, preventScrolling: C, noWheelClassName: k, noPanClassName: T, onViewportChange: P, isControlledViewport: F, paneClickDistance: c, selectionOnDrag: G, children: i.jsxs(vw, { onSelectionStart: p, onSelectionEnd: h, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: s, onPaneScroll: a, panOnDrag: te, autoPanOnSelection: D, isSelecting: !!M, selectionMode: f, selectionKeyPressed: Z, paneClickDistance: c, selectionOnDrag: G, children: [e, W && i.jsx(Tw, { onSelectionContextMenu: I, noPanClassName: T, disableKeyboardA11y: $ })] }) });
}
Du.displayName = "FlowRenderer";
const $w = be(Du), Mw = (e) => (t) => e ? ns(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function Rw(e) {
  return he(ie(Mw(e), [e]), me);
}
const Lw = (e) => e.updateNodeInternals;
function zw() {
  const e = he(Lw), [t] = B(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
    const o = /* @__PURE__ */ new Map();
    n.forEach((r) => {
      const s = r.target.getAttribute("data-id");
      o.set(s, {
        id: s,
        nodeElement: r.target,
        force: !0
      });
    }), e(o);
  }));
  return J(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function Vw({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = xe(), s = re(null), a = re(null), c = re(e.sourcePosition), u = re(e.targetPosition), l = re(t), d = n && !!e.internals.handleBounds;
  return J(() => {
    s.current && !e.hidden && (!d || a.current !== s.current) && (a.current && o?.unobserve(a.current), o?.observe(s.current), a.current = s.current);
  }, [d, e.hidden]), J(() => () => {
    a.current && (o?.unobserve(a.current), a.current = null);
  }, []), J(() => {
    if (s.current) {
      const f = l.current !== t, p = c.current !== e.sourcePosition, h = u.current !== e.targetPosition;
      (f || p || h) && (l.current = t, c.current = e.sourcePosition, u.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: s.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), s;
}
function Ow({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: s, onDoubleClick: a, nodesDraggable: c, elementsSelectable: u, nodesConnectable: l, nodesFocusable: d, resizeObserver: f, noDragClassName: p, noPanClassName: h, disableKeyboardA11y: g, rfId: m, nodeTypes: w, nodeClickDistance: x, onError: b }) {
  const { node: y, internals: v, isParent: j } = he((M) => {
    const q = M.nodeLookup.get(e), ae = M.parentLookup.has(e);
    return {
      node: q,
      internals: q.internals,
      isParent: ae
    };
  }, me);
  let N = y.type || "default", S = w?.[N] || Fa[N];
  S === void 0 && (b?.("003", Oe.error003(N)), N = "default", S = w?.default || Fa.default);
  const E = !!(y.draggable || c && typeof y.draggable > "u"), D = !!(y.selectable || u && typeof y.selectable > "u"), L = !!(y.connectable || l && typeof y.connectable > "u"), A = !!(y.focusable || d && typeof y.focusable > "u"), _ = xe(), R = Ul(y), C = Vw({ node: y, nodeType: N, hasDimensions: R, resizeObserver: f }), I = ku({
    nodeRef: C,
    disabled: y.hidden || !E,
    noDragClassName: p,
    handleSelector: y.dragHandle,
    nodeId: e,
    isSelectable: D,
    nodeClickDistance: x
  }), k = Au();
  if (y.hidden)
    return null;
  const T = at(y), $ = _w(y), P = D || E || t || n || o || r, F = n ? (M) => n(M, { ...v.userNode }) : void 0, W = o ? (M) => o(M, { ...v.userNode }) : void 0, O = r ? (M) => r(M, { ...v.userNode }) : void 0, Z = s ? (M) => s(M, { ...v.userNode }) : void 0, U = a ? (M) => a(M, { ...v.userNode }) : void 0, te = (M) => {
    const { selectNodesOnDrag: q, nodeDragThreshold: ae } = _.getState();
    D && (!q || !E || ae > 0) && Ti({
      id: e,
      store: _,
      nodeRef: C
    }), t && t(M, { ...v.userNode });
  }, le = (M) => {
    if (!(Jl(M.nativeEvent) || g)) {
      if (Ol.includes(M.key) && D) {
        const q = M.key === "Escape";
        Ti({
          id: e,
          store: _,
          unselect: q,
          nodeRef: C
        });
      } else if (E && y.selected && Object.prototype.hasOwnProperty.call(Yo, M.key)) {
        M.preventDefault();
        const { ariaLabelConfig: q } = _.getState();
        _.setState({
          ariaLiveMessage: q["node.a11yDescription.ariaLiveMessage"]({
            direction: M.key.replace("Arrow", "").toLowerCase(),
            x: ~~v.positionAbsolute.x,
            y: ~~v.positionAbsolute.y
          })
        }), k({
          direction: Yo[M.key],
          factor: M.shiftKey ? 4 : 1
        });
      }
    }
  }, G = () => {
    if (g || !C.current?.matches(":focus-visible"))
      return;
    const { transform: M, width: q, height: ae, autoPanOnNodeFocus: ce, setCenter: Q } = _.getState();
    if (!ce)
      return;
    ns(/* @__PURE__ */ new Map([[e, y]]), { x: 0, y: 0, width: q, height: ae }, M, !0).length > 0 || Q(y.position.x + T.width / 2, y.position.y + T.height / 2, {
      zoom: M[2]
    });
  };
  return i.jsx("div", { className: Se([
    "react-flow__node",
    `react-flow__node-${N}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [h]: E
    },
    y.className,
    {
      selected: y.selected,
      selectable: D,
      parent: j,
      draggable: E,
      dragging: I
    }
  ]), ref: C, style: {
    zIndex: v.z,
    transform: `translate(${v.positionAbsolute.x}px,${v.positionAbsolute.y}px)`,
    pointerEvents: P ? "all" : "none",
    visibility: R ? "visible" : "hidden",
    ...y.style,
    ...$
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: F, onMouseMove: W, onMouseLeave: O, onContextMenu: Z, onClick: te, onDoubleClick: U, onKeyDown: A ? le : void 0, tabIndex: A ? 0 : void 0, onFocus: A ? G : void 0, role: y.ariaRole ?? (A ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": g ? void 0 : `${xu}-${m}`, "aria-label": y.ariaLabel, ...y.domAttributes, children: i.jsx(Nw, { value: e, children: i.jsx(S, { id: e, data: y.data, type: N, positionAbsoluteX: v.positionAbsolute.x, positionAbsoluteY: v.positionAbsolute.y, selected: y.selected ?? !1, selectable: D, draggable: E, deletable: y.deletable ?? !0, isConnectable: L, sourcePosition: y.sourcePosition, targetPosition: y.targetPosition, dragging: I, dragHandle: y.dragHandle, zIndex: v.z, parentId: y.parentId, ...T }) }) });
}
var Hw = be(Ow);
const Ww = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function Tu(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: s } = he(Ww, me), a = Rw(e.onlyRenderVisibleElements), c = zw();
  return i.jsx("div", { className: "react-flow__nodes", style: dr, children: a.map((u) => (
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
    i.jsx(Hw, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: s }, u)
  )) });
}
Tu.displayName = "NodeRenderer";
const Fw = be(Tu);
function Bw(e) {
  return he(ie((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const s = n.nodeLookup.get(r.source), a = n.nodeLookup.get(r.target);
        s && a && Vm({
          sourceNode: s,
          targetNode: a,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && o.push(r.id);
      }
    return o;
  }, [e]), me);
}
const Kw = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return i.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, Xw = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return i.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Ka = {
  [Ko.Arrow]: Kw,
  [Ko.ArrowClosed]: Xw
};
function qw(e) {
  const t = xe();
  return fe(() => Object.prototype.hasOwnProperty.call(Ka, e) ? Ka[e] : (t.getState().onError?.("009", Oe.error009(e)), null), [e]);
}
const Yw = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: s = "strokeWidth", strokeWidth: a, orient: c = "auto-start-reverse" }) => {
  const u = qw(t);
  return u ? i.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: s, orient: c, refX: "0", refY: "0", children: i.jsx(u, { color: n, strokeWidth: a }) }) : null;
}, Pu = ({ defaultColor: e, rfId: t }) => {
  const n = he((s) => s.edges), o = he((s) => s.defaultEdgeOptions), r = fe(() => qm(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? i.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: i.jsx("defs", { children: r.map((s) => i.jsx(Yw, { id: s.id, type: s.type, color: s.color, width: s.width, height: s.height, markerUnits: s.markerUnits, strokeWidth: s.strokeWidth, orient: s.orient }, s.id)) }) }) : null;
};
Pu.displayName = "MarkerDefinitions";
var Uw = be(Pu);
function $u({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: s, labelBgPadding: a = [2, 4], labelBgBorderRadius: c = 2, children: u, className: l, ...d }) {
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
  }, [n]), n ? i.jsxs("g", { transform: `translate(${e - f.width / 2} ${t - f.height / 2})`, className: h, visibility: f.width ? "visible" : "hidden", ...d, children: [r && i.jsx("rect", { width: f.width + 2 * a[0], x: -a[0], y: -a[1], height: f.height + 2 * a[1], className: "react-flow__edge-textbg", style: s, rx: c, ry: c }), i.jsx("text", { className: "react-flow__edge-text", y: f.height / 2, dy: "0.3em", ref: g, style: o, children: n }), u] }) : null;
}
$u.displayName = "EdgeText";
const Zw = be($u);
function Wn({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u, interactionWidth: l = 20, ...d }) {
  return i.jsxs(i.Fragment, { children: [i.jsx("path", { ...d, d: e, fill: "none", className: Se(["react-flow__edge-path", d.className]) }), l ? i.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, o && Ke(t) && Ke(n) ? i.jsx(Zw, { x: t, y: n, label: o, labelStyle: r, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u }) : null] });
}
function Xa({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === ne.Left || e === ne.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function Mu({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: r, targetPosition: s = ne.Top }) {
  const [a, c] = Xa({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [u, l] = Xa({
    pos: s,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [d, f, p, h] = eu({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: r,
    sourceControlX: a,
    sourceControlY: c,
    targetControlX: u,
    targetControlY: l
  });
  return [
    `M${e},${t} C${a},${c} ${u},${l} ${o},${r}`,
    d,
    f,
    p,
    h
  ];
}
function Ru(e) {
  return be(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: s, sourcePosition: a, targetPosition: c, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: g, markerEnd: m, markerStart: w, interactionWidth: x }) => {
    const [b, y, v] = Mu({
      sourceX: n,
      sourceY: o,
      sourcePosition: a,
      targetX: r,
      targetY: s,
      targetPosition: c
    }), j = e.isInternal ? void 0 : t;
    return i.jsx(Wn, { id: j, path: b, labelX: y, labelY: v, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: g, markerEnd: m, markerStart: w, interactionWidth: x });
  });
}
const Gw = Ru({ isInternal: !1 }), Lu = Ru({ isInternal: !0 });
Gw.displayName = "SimpleBezierEdge";
Lu.displayName = "SimpleBezierEdgeInternal";
function zu(e) {
  return be(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, sourcePosition: h = ne.Bottom, targetPosition: g = ne.Top, markerEnd: m, markerStart: w, pathOptions: x, interactionWidth: b }) => {
    const [y, v, j] = qo({
      sourceX: n,
      sourceY: o,
      sourcePosition: h,
      targetX: r,
      targetY: s,
      targetPosition: g,
      borderRadius: x?.borderRadius,
      offset: x?.offset,
      stepPosition: x?.stepPosition
    }), N = e.isInternal ? void 0 : t;
    return i.jsx(Wn, { id: N, path: y, labelX: v, labelY: j, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: m, markerStart: w, interactionWidth: b });
  });
}
const Vu = zu({ isInternal: !1 }), Ou = zu({ isInternal: !0 });
Vu.displayName = "SmoothStepEdge";
Ou.displayName = "SmoothStepEdgeInternal";
function Hu(e) {
  return be(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return i.jsx(Vu, { ...n, id: o, pathOptions: fe(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Jw = Hu({ isInternal: !1 }), Wu = Hu({ isInternal: !0 });
Jw.displayName = "StepEdge";
Wu.displayName = "StepEdgeInternal";
function Fu(e) {
  return be(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: g, interactionWidth: m }) => {
    const [w, x, b] = ru({ sourceX: n, sourceY: o, targetX: r, targetY: s }), y = e.isInternal ? void 0 : t;
    return i.jsx(Wn, { id: y, path: w, labelX: x, labelY: b, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: g, interactionWidth: m });
  });
}
const Qw = Fu({ isInternal: !1 }), Bu = Fu({ isInternal: !0 });
Qw.displayName = "StraightEdge";
Bu.displayName = "StraightEdgeInternal";
function Ku(e) {
  return be(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: s, sourcePosition: a = ne.Bottom, targetPosition: c = ne.Top, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: g, markerEnd: m, markerStart: w, pathOptions: x, interactionWidth: b }) => {
    const [y, v, j] = tu({
      sourceX: n,
      sourceY: o,
      sourcePosition: a,
      targetX: r,
      targetY: s,
      targetPosition: c,
      curvature: x?.curvature
    }), N = e.isInternal ? void 0 : t;
    return i.jsx(Wn, { id: N, path: y, labelX: v, labelY: j, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: g, markerEnd: m, markerStart: w, interactionWidth: b });
  });
}
const ev = Ku({ isInternal: !1 }), Xu = Ku({ isInternal: !0 });
ev.displayName = "BezierEdge";
Xu.displayName = "BezierEdgeInternal";
const qa = {
  default: Xu,
  straight: Bu,
  step: Wu,
  smoothstep: Ou,
  simplebezier: Lu
}, Ya = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, tv = (e, t, n) => n === ne.Left ? e - t : n === ne.Right ? e + t : e, nv = (e, t, n) => n === ne.Top ? e - t : n === ne.Bottom ? e + t : e, Ua = "react-flow__edgeupdater";
function Za({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: s, onMouseOut: a, type: c }) {
  return i.jsx("circle", { onMouseDown: r, onMouseEnter: s, onMouseOut: a, className: Se([Ua, `${Ua}-${c}`]), cx: tv(t, o, e), cy: nv(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function ov({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: s, targetY: a, sourcePosition: c, targetPosition: u, onReconnect: l, onReconnectStart: d, onReconnectEnd: f, setReconnecting: p, setUpdateHover: h }) {
  const g = xe(), m = (v, j) => {
    if (v.button !== 0)
      return;
    const { autoPanOnConnect: N, domNode: S, connectionMode: E, connectionRadius: D, lib: L, onConnectStart: A, cancelConnection: _, nodeLookup: R, rfId: C, panBy: I, updateConnection: k } = g.getState(), T = j.type === "target", $ = (W, O) => {
      p(!1), f?.(W, n, j.type, O);
    }, P = (W) => l?.(n, W), F = (W, O) => {
      p(!0), d?.(v, n, j.type), A?.(W, O);
    };
    Di.onPointerDown(v.nativeEvent, {
      autoPanOnConnect: N,
      connectionMode: E,
      connectionRadius: D,
      domNode: S,
      handleId: j.id,
      nodeId: j.nodeId,
      nodeLookup: R,
      isTarget: T,
      edgeUpdaterType: j.type,
      lib: L,
      flowId: C,
      cancelConnection: _,
      panBy: I,
      isValidConnection: (...W) => g.getState().isValidConnection?.(...W) ?? !0,
      onConnect: P,
      onConnectStart: F,
      onConnectEnd: (...W) => g.getState().onConnectEnd?.(...W),
      onReconnectEnd: $,
      updateConnection: k,
      getTransform: () => g.getState().transform,
      getFromHandle: () => g.getState().connection.fromHandle,
      dragThreshold: g.getState().connectionDragThreshold,
      handleDomNode: v.currentTarget
    });
  }, w = (v) => m(v, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), x = (v) => m(v, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), b = () => h(!0), y = () => h(!1);
  return i.jsxs(i.Fragment, { children: [(e === !0 || e === "source") && i.jsx(Za, { position: c, centerX: o, centerY: r, radius: t, onMouseDown: w, onMouseEnter: b, onMouseOut: y, type: "source" }), (e === !0 || e === "target") && i.jsx(Za, { position: u, centerX: s, centerY: a, radius: t, onMouseDown: x, onMouseEnter: b, onMouseOut: y, type: "target" })] });
}
function rv({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, rfId: g, edgeTypes: m, noPanClassName: w, onError: x, disableKeyboardA11y: b }) {
  let y = he((Q) => Q.edgeLookup.get(e));
  const v = he((Q) => Q.defaultEdgeOptions);
  y = v ? { ...v, ...y } : y;
  let j = y.type || "default", N = m?.[j] || qa[j];
  N === void 0 && (x?.("011", Oe.error011(j)), j = "default", N = m?.default || qa.default);
  const S = !!(y.focusable || t && typeof y.focusable > "u"), E = typeof f < "u" && (y.reconnectable || n && typeof y.reconnectable > "u"), D = !!(y.selectable || o && typeof y.selectable > "u"), L = re(null), [A, _] = B(!1), [R, C] = B(!1), I = xe(), { zIndex: k, sourceX: T, sourceY: $, targetX: P, targetY: F, sourcePosition: W, targetPosition: O } = he(ie((Q) => {
    const oe = Q.nodeLookup.get(y.source), de = Q.nodeLookup.get(y.target);
    if (!oe || !de)
      return {
        zIndex: y.zIndex,
        ...Ya
      };
    const V = Xm({
      id: e,
      sourceNode: oe,
      targetNode: de,
      sourceHandle: y.sourceHandle || null,
      targetHandle: y.targetHandle || null,
      connectionMode: Q.connectionMode,
      onError: x
    });
    return {
      zIndex: zm({
        selected: y.selected,
        zIndex: y.zIndex,
        sourceNode: oe,
        targetNode: de,
        elevateOnSelect: Q.elevateEdgesOnSelect,
        zIndexMode: Q.zIndexMode
      }),
      ...V || Ya
    };
  }, [y.source, y.target, y.sourceHandle, y.targetHandle, y.selected, y.zIndex]), me), Z = fe(() => y.markerStart ? `url('#${Ai(y.markerStart, g)}')` : void 0, [y.markerStart, g]), U = fe(() => y.markerEnd ? `url('#${Ai(y.markerEnd, g)}')` : void 0, [y.markerEnd, g]);
  if (y.hidden || T === null || $ === null || P === null || F === null)
    return null;
  const te = (Q) => {
    const { addSelectedEdges: oe, unselectNodesAndEdges: de, multiSelectionActive: V } = I.getState();
    D && (I.setState({ nodesSelectionActive: !1 }), y.selected && V ? (de({ nodes: [], edges: [y] }), L.current?.blur()) : oe([e])), r && r(Q, y);
  }, le = s ? (Q) => {
    s(Q, { ...y });
  } : void 0, G = a ? (Q) => {
    a(Q, { ...y });
  } : void 0, M = c ? (Q) => {
    c(Q, { ...y });
  } : void 0, q = u ? (Q) => {
    u(Q, { ...y });
  } : void 0, ae = l ? (Q) => {
    l(Q, { ...y });
  } : void 0, ce = (Q) => {
    if (!b && Ol.includes(Q.key) && D) {
      const { unselectNodesAndEdges: oe, addSelectedEdges: de } = I.getState();
      Q.key === "Escape" ? (L.current?.blur(), oe({ edges: [y] })) : de([e]);
    }
  };
  return i.jsx("svg", { style: { zIndex: k }, children: i.jsxs("g", { className: Se([
    "react-flow__edge",
    `react-flow__edge-${j}`,
    y.className,
    w,
    {
      selected: y.selected,
      animated: y.animated,
      inactive: !D && !r,
      updating: A,
      selectable: D
    }
  ]), onClick: te, onDoubleClick: le, onContextMenu: G, onMouseEnter: M, onMouseMove: q, onMouseLeave: ae, onKeyDown: S ? ce : void 0, tabIndex: S ? 0 : void 0, role: y.ariaRole ?? (S ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": y.ariaLabel === null ? void 0 : y.ariaLabel || `Edge from ${y.source} to ${y.target}`, "aria-describedby": S ? `${wu}-${g}` : void 0, ref: L, ...y.domAttributes, children: [!R && i.jsx(N, { id: e, source: y.source, target: y.target, type: y.type, selected: y.selected, animated: y.animated, selectable: D, deletable: y.deletable ?? !0, label: y.label, labelStyle: y.labelStyle, labelShowBg: y.labelShowBg, labelBgStyle: y.labelBgStyle, labelBgPadding: y.labelBgPadding, labelBgBorderRadius: y.labelBgBorderRadius, sourceX: T, sourceY: $, targetX: P, targetY: F, sourcePosition: W, targetPosition: O, data: y.data, style: y.style, sourceHandleId: y.sourceHandle, targetHandleId: y.targetHandle, markerStart: Z, markerEnd: U, pathOptions: "pathOptions" in y ? y.pathOptions : void 0, interactionWidth: y.interactionWidth }), E && i.jsx(ov, { edge: y, isReconnectable: E, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, sourceX: T, sourceY: $, targetX: P, targetY: F, sourcePosition: W, targetPosition: O, setUpdateHover: _, setReconnecting: C })] }) });
}
var iv = be(rv);
const sv = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function qu({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: s, onEdgeContextMenu: a, onEdgeMouseEnter: c, onEdgeMouseMove: u, onEdgeMouseLeave: l, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: p, onReconnectStart: h, onReconnectEnd: g, disableKeyboardA11y: m }) {
  const { edgesFocusable: w, edgesReconnectable: x, elementsSelectable: b, onError: y } = he(sv, me), v = Bw(t);
  return i.jsxs("div", { className: "react-flow__edges", children: [i.jsx(Uw, { defaultColor: e, rfId: n }), v.map((j) => i.jsx(iv, { id: j, edgesFocusable: w, edgesReconnectable: x, elementsSelectable: b, noPanClassName: r, onReconnect: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, onClick: d, reconnectRadius: f, onDoubleClick: p, onReconnectStart: h, onReconnectEnd: g, rfId: n, onError: y, edgeTypes: o, disableKeyboardA11y: m }, j))] });
}
qu.displayName = "EdgeRenderer";
const av = be(qu), cv = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function lv({ children: e }) {
  const t = he(cv);
  return i.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function uv(e) {
  const t = ds(), n = re(!1);
  J(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const dv = (e) => e.panZoom?.syncViewport;
function fv(e) {
  const t = he(dv), n = xe();
  return J(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function pv(e) {
  return e.connection.inProgress ? { ...e.connection, to: tn(e.connection.to, e.transform) } : { ...e.connection };
}
function hv(e) {
  return pv;
}
function gv(e) {
  const t = hv();
  return he(t, me);
}
const yv = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function mv({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: s, height: a, isValid: c, inProgress: u } = he(yv, me);
  return !(s && r && u) ? null : i.jsx("svg", { style: e, width: s, height: a, className: "react-flow__connectionline react-flow__container", children: i.jsx("g", { className: Se(["react-flow__connection", Fl(c)]), children: i.jsx(Yu, { style: t, type: n, CustomComponent: o, isValid: c }) }) });
}
const Yu = ({ style: e, type: t = ft.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: s, fromNode: a, fromHandle: c, fromPosition: u, to: l, toNode: d, toHandle: f, toPosition: p, pointer: h } = gv();
  if (!r)
    return;
  if (n)
    return i.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: a, fromHandle: c, fromX: s.x, fromY: s.y, toX: l.x, toY: l.y, fromPosition: u, toPosition: p, connectionStatus: Fl(o), toNode: d, toHandle: f, pointer: h });
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
    case ft.Bezier:
      [g] = tu(m);
      break;
    case ft.SimpleBezier:
      [g] = Mu(m);
      break;
    case ft.Step:
      [g] = qo({
        ...m,
        borderRadius: 0
      });
      break;
    case ft.SmoothStep:
      [g] = qo(m);
      break;
    default:
      [g] = ru(m);
  }
  return i.jsx("path", { d: g, fill: "none", className: "react-flow__connection-path", style: e });
};
Yu.displayName = "ConnectionLine";
const xv = {};
function Ga(e = xv) {
  re(e), xe(), J(() => {
  }, [e]);
}
function wv() {
  xe(), re(!1), J(() => {
  }, []);
}
function Uu({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: s, onEdgeDoubleClick: a, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: p, onSelectionEnd: h, connectionLineType: g, connectionLineStyle: m, connectionLineComponent: w, connectionLineContainerStyle: x, selectionKeyCode: b, selectionOnDrag: y, selectionMode: v, multiSelectionKeyCode: j, panActivationKeyCode: N, zoomActivationKeyCode: S, deleteKeyCode: E, onlyRenderVisibleElements: D, elementsSelectable: L, defaultViewport: A, translateExtent: _, minZoom: R, maxZoom: C, preventScrolling: I, defaultMarkerColor: k, zoomOnScroll: T, zoomOnPinch: $, panOnScroll: P, panOnScrollSpeed: F, panOnScrollMode: W, zoomOnDoubleClick: O, panOnDrag: Z, autoPanOnSelection: U, onPaneClick: te, onPaneMouseEnter: le, onPaneMouseMove: G, onPaneMouseLeave: M, onPaneScroll: q, onPaneContextMenu: ae, paneClickDistance: ce, nodeClickDistance: Q, onEdgeContextMenu: oe, onEdgeMouseEnter: de, onEdgeMouseMove: V, onEdgeMouseLeave: ee, reconnectRadius: ge, onReconnect: we, onReconnectStart: Ae, onReconnectEnd: Ie, noDragClassName: Te, noWheelClassName: tt, noPanClassName: We, disableKeyboardA11y: z, nodeExtent: H, rfId: K, viewport: X, onViewportChange: se }) {
  return Ga(e), Ga(t), wv(), uv(n), fv(X), i.jsx($w, { onPaneClick: te, onPaneMouseEnter: le, onPaneMouseMove: G, onPaneMouseLeave: M, onPaneContextMenu: ae, onPaneScroll: q, paneClickDistance: ce, deleteKeyCode: E, selectionKeyCode: b, selectionOnDrag: y, selectionMode: v, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: j, panActivationKeyCode: N, zoomActivationKeyCode: S, elementsSelectable: L, zoomOnScroll: T, zoomOnPinch: $, zoomOnDoubleClick: O, panOnScroll: P, panOnScrollSpeed: F, panOnScrollMode: W, panOnDrag: Z, autoPanOnSelection: U, defaultViewport: A, translateExtent: _, minZoom: R, maxZoom: C, onSelectionContextMenu: f, preventScrolling: I, noDragClassName: Te, noWheelClassName: tt, noPanClassName: We, disableKeyboardA11y: z, onViewportChange: se, isControlledViewport: !!X, children: i.jsxs(lv, { children: [i.jsx(av, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: a, onReconnect: we, onReconnectStart: Ae, onReconnectEnd: Ie, onlyRenderVisibleElements: D, onEdgeContextMenu: oe, onEdgeMouseEnter: de, onEdgeMouseMove: V, onEdgeMouseLeave: ee, reconnectRadius: ge, defaultMarkerColor: k, noPanClassName: We, disableKeyboardA11y: z, rfId: K }), i.jsx(mv, { style: m, type: g, component: w, containerStyle: x }), i.jsx("div", { className: "react-flow__edgelabel-renderer" }), i.jsx(Fw, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, nodeClickDistance: Q, onlyRenderVisibleElements: D, noPanClassName: We, noDragClassName: Te, disableKeyboardA11y: z, nodeExtent: H, rfId: K }), i.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Uu.displayName = "GraphView";
const vv = be(Uu), bv = Yl(), Ja = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: s, fitView: a, fitViewOptions: c, minZoom: u = 0.5, maxZoom: l = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: p = "basic" } = {}) => {
  const h = /* @__PURE__ */ new Map(), g = /* @__PURE__ */ new Map(), m = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), x = o ?? t ?? [], b = n ?? e ?? [], y = d ?? [0, 0], v = f ?? An;
  au(m, w, x);
  const { nodesInitialized: j } = _i(b, h, g, {
    nodeOrigin: y,
    nodeExtent: v,
    zIndexMode: p
  });
  let N = [0, 0, 1];
  if (a && r && s) {
    const S = On(h, {
      filter: (A) => !!((A.width || A.initialWidth) && (A.height || A.initialHeight))
    }), { x: E, y: D, zoom: L } = rs(S, r, s, u, l, c?.padding ?? 0.1);
    N = [E, D, L];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: s ?? 0,
    transform: N,
    nodes: b,
    nodesInitialized: j,
    nodeLookup: h,
    parentLookup: g,
    edges: x,
    edgeLookup: w,
    connectionLookup: m,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: o !== void 0,
    panZoom: null,
    minZoom: u,
    maxZoom: l,
    translateExtent: An,
    nodeExtent: v,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: Xt.Strict,
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
    connection: { ...Wl },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: bv,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Hl,
    zIndexMode: p,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, Nv = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: s, fitView: a, fitViewOptions: c, minZoom: u, maxZoom: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p }) => Mx((h, g) => {
  async function m() {
    const { nodeLookup: w, panZoom: x, fitViewOptions: b, fitViewResolver: y, width: v, height: j, minZoom: N, maxZoom: S } = g();
    x && (await Dm({
      nodes: w,
      width: v,
      height: j,
      panZoom: x,
      minZoom: N,
      maxZoom: S
    }, b), y?.resolve(!0), h({ fitViewResolver: null }));
  }
  return {
    ...Ja({
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
      defaultEdges: o,
      zIndexMode: p
    }),
    setNodes: (w) => {
      const { nodeLookup: x, parentLookup: b, nodeOrigin: y, elevateNodesOnSelect: v, fitViewQueued: j, zIndexMode: N, nodesSelectionActive: S } = g(), { nodesInitialized: E, hasSelectedNodes: D } = _i(w, x, b, {
        nodeOrigin: y,
        nodeExtent: f,
        elevateNodesOnSelect: v,
        checkEquality: !0,
        zIndexMode: N
      }), L = S && D;
      j && E ? (m(), h({
        nodes: w,
        nodesInitialized: E,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: L
      })) : h({ nodes: w, nodesInitialized: E, nodesSelectionActive: L });
    },
    setEdges: (w) => {
      const { connectionLookup: x, edgeLookup: b } = g();
      au(x, b, w), h({ edges: w });
    },
    setDefaultNodesAndEdges: (w, x) => {
      if (w) {
        const { setNodes: b } = g();
        b(w), h({ hasDefaultNodes: !0 });
      }
      if (x) {
        const { setEdges: b } = g();
        b(x), h({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (w) => {
      const { triggerNodeChanges: x, nodeLookup: b, parentLookup: y, domNode: v, nodeOrigin: j, nodeExtent: N, debug: S, fitViewQueued: E, zIndexMode: D } = g(), { changes: L, updatedInternals: A } = ex(w, b, y, v, j, N, D);
      A && (Zm(b, y, { nodeOrigin: j, nodeExtent: N, zIndexMode: D }), E ? (m(), h({ fitViewQueued: !1, fitViewOptions: void 0 })) : h({}), L?.length > 0 && (S && console.log("React Flow: trigger node changes", L), x?.(L)));
    },
    updateNodePositions: (w, x = !1) => {
      const b = [];
      let y = [];
      const { nodeLookup: v, triggerNodeChanges: j, connection: N, updateConnection: S, onNodesChangeMiddlewareMap: E } = g();
      for (const [D, L] of w) {
        const A = v.get(D), _ = !!(A?.expandParent && A?.parentId && L?.position), R = {
          id: D,
          type: "position",
          position: _ ? {
            x: Math.max(0, L.position.x),
            y: Math.max(0, L.position.y)
          } : L.position,
          dragging: x
        };
        if (A && N.inProgress && N.fromNode.id === A.id) {
          const C = Ct(A, N.fromHandle, ne.Left, !0);
          S({ ...N, from: C });
        }
        _ && A.parentId && b.push({
          id: D,
          parentId: A.parentId,
          rect: {
            ...L.internals.positionAbsolute,
            width: L.measured.width ?? 0,
            height: L.measured.height ?? 0
          }
        }), y.push(R);
      }
      if (b.length > 0) {
        const { parentLookup: D, nodeOrigin: L } = g(), A = us(b, v, D, L);
        y.push(...A);
      }
      for (const D of E.values())
        y = D(y);
      j(y);
    },
    triggerNodeChanges: (w) => {
      const { onNodesChange: x, setNodes: b, nodes: y, hasDefaultNodes: v, debug: j } = g();
      if (w?.length) {
        if (v) {
          const N = Nu(w, y);
          b(N);
        }
        j && console.log("React Flow: trigger node changes", w), x?.(w);
      }
    },
    triggerEdgeChanges: (w) => {
      const { onEdgesChange: x, setEdges: b, edges: y, hasDefaultEdges: v, debug: j } = g();
      if (w?.length) {
        if (v) {
          const N = ju(w, y);
          b(N);
        }
        j && console.log("React Flow: trigger edge changes", w), x?.(w);
      }
    },
    addSelectedNodes: (w) => {
      const { multiSelectionActive: x, edgeLookup: b, nodeLookup: y, triggerNodeChanges: v, triggerEdgeChanges: j } = g();
      if (x) {
        const N = w.map((S) => yt(S, !0));
        v(N);
        return;
      }
      v(Lt(y, /* @__PURE__ */ new Set([...w]), !0)), j(Lt(b));
    },
    addSelectedEdges: (w) => {
      const { multiSelectionActive: x, edgeLookup: b, nodeLookup: y, triggerNodeChanges: v, triggerEdgeChanges: j } = g();
      if (x) {
        const N = w.map((S) => yt(S, !0));
        j(N);
        return;
      }
      j(Lt(b, /* @__PURE__ */ new Set([...w]))), v(Lt(y, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: w, edges: x } = {}) => {
      const { edges: b, nodes: y, nodeLookup: v, triggerNodeChanges: j, triggerEdgeChanges: N } = g(), S = w || y, E = x || b, D = [];
      for (const A of S) {
        if (!A.selected)
          continue;
        const _ = v.get(A.id);
        _ && (_.selected = !1), D.push(yt(A.id, !1));
      }
      const L = [];
      for (const A of E)
        A.selected && L.push(yt(A.id, !1));
      j(D), N(L);
    },
    setMinZoom: (w) => {
      const { panZoom: x, maxZoom: b } = g();
      x?.setScaleExtent([w, b]), h({ minZoom: w });
    },
    setMaxZoom: (w) => {
      const { panZoom: x, minZoom: b } = g();
      x?.setScaleExtent([b, w]), h({ maxZoom: w });
    },
    setTranslateExtent: (w) => {
      g().panZoom?.setTranslateExtent(w), h({ translateExtent: w });
    },
    resetSelectedElements: () => {
      const { edges: w, nodes: x, triggerNodeChanges: b, triggerEdgeChanges: y, elementsSelectable: v } = g();
      if (!v)
        return;
      const j = x.reduce((S, E) => E.selected ? [...S, yt(E.id, !1)] : S, []), N = w.reduce((S, E) => E.selected ? [...S, yt(E.id, !1)] : S, []);
      b(j), y(N);
    },
    setNodeExtent: (w) => {
      const { nodes: x, nodeLookup: b, parentLookup: y, nodeOrigin: v, elevateNodesOnSelect: j, nodeExtent: N, zIndexMode: S } = g();
      w[0][0] === N[0][0] && w[0][1] === N[0][1] && w[1][0] === N[1][0] && w[1][1] === N[1][1] || (_i(x, b, y, {
        nodeOrigin: v,
        nodeExtent: w,
        elevateNodesOnSelect: j,
        checkEquality: !1,
        zIndexMode: S
      }), h({ nodeExtent: w }));
    },
    panBy: (w) => {
      const { transform: x, width: b, height: y, panZoom: v, translateExtent: j } = g();
      return tx({ delta: w, panZoom: v, transform: x, translateExtent: j, width: b, height: y });
    },
    setCenter: async (w, x, b) => {
      const { width: y, height: v, maxZoom: j, panZoom: N } = g();
      if (!N)
        return !1;
      const S = typeof b?.zoom < "u" ? b.zoom : j;
      return await N.setViewport({
        x: y / 2 - w * S,
        y: v / 2 - x * S,
        zoom: S
      }, { duration: b?.duration, ease: b?.ease, interpolate: b?.interpolate }), !0;
    },
    cancelConnection: () => {
      h({
        connection: { ...Wl }
      });
    },
    updateConnection: (w) => {
      h({ connection: w });
    },
    reset: () => h({ ...Ja() })
  };
}, Object.is);
function jv({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: s, initialMinZoom: a, initialMaxZoom: c, initialFitViewOptions: u, fitView: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p, children: h }) {
  const [g] = B(() => Nv({
    nodes: e,
    edges: t,
    defaultNodes: n,
    defaultEdges: o,
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
  return i.jsx(Vx, { value: g, children: i.jsx(lw, { children: h }) });
}
function Sv({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: s, height: a, fitView: c, fitViewOptions: u, minZoom: l, maxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h }) {
  return $n(lr) ? i.jsx(i.Fragment, { children: e }) : i.jsx(jv, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: s, initialHeight: a, fitView: c, initialFitViewOptions: u, initialMinZoom: l, initialMaxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h, children: e });
}
const Cv = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function Ev({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: s, edgeTypes: a, onNodeClick: c, onEdgeClick: u, onInit: l, onMove: d, onMoveStart: f, onMoveEnd: p, onConnect: h, onConnectStart: g, onConnectEnd: m, onClickConnectStart: w, onClickConnectEnd: x, onNodeMouseEnter: b, onNodeMouseMove: y, onNodeMouseLeave: v, onNodeContextMenu: j, onNodeDoubleClick: N, onNodeDragStart: S, onNodeDrag: E, onNodeDragStop: D, onNodesDelete: L, onEdgesDelete: A, onDelete: _, onSelectionChange: R, onSelectionDragStart: C, onSelectionDrag: I, onSelectionDragStop: k, onSelectionContextMenu: T, onSelectionStart: $, onSelectionEnd: P, onBeforeDelete: F, connectionMode: W, connectionLineType: O = ft.Bezier, connectionLineStyle: Z, connectionLineComponent: U, connectionLineContainerStyle: te, deleteKeyCode: le = "Backspace", selectionKeyCode: G = "Shift", selectionOnDrag: M = !1, selectionMode: q = _n.Full, panActivationKeyCode: ae = "Space", multiSelectionKeyCode: ce = Tn() ? "Meta" : "Control", zoomActivationKeyCode: Q = Tn() ? "Meta" : "Control", snapToGrid: oe, snapGrid: de, onlyRenderVisibleElements: V = !1, selectNodesOnDrag: ee, nodesDraggable: ge, autoPanOnNodeFocus: we, nodesConnectable: Ae, nodesFocusable: Ie, nodeOrigin: Te = vu, edgesFocusable: tt, edgesReconnectable: We, elementsSelectable: z = !0, defaultViewport: H = Jx, minZoom: K = 0.5, maxZoom: X = 2, translateExtent: se = An, preventScrolling: pe = !0, nodeExtent: ye, defaultMarkerColor: je = "#b1b1b7", zoomOnScroll: Ee = !0, zoomOnPinch: Me = !0, panOnScroll: ke = !1, panOnScrollSpeed: hr = 0.5, panOnScrollMode: He = vt.Free, zoomOnDoubleClick: gr = !0, panOnDrag: Bn = !0, onPaneClick: At, onPaneMouseEnter: yr, onPaneMouseMove: Ye, onPaneMouseLeave: _t, onPaneScroll: mr, onPaneContextMenu: Kn, paneClickDistance: xr = 1, nodeClickDistance: wr = 0, children: ct, onReconnect: vr, onReconnectStart: Dt, onReconnectEnd: br, onEdgeContextMenu: Tt, onEdgeDoubleClick: Nr, onEdgeMouseEnter: Xn, onEdgeMouseMove: qn, onEdgeMouseLeave: jr, reconnectRadius: on = 10, onNodesChange: Sr, onEdgesChange: Cr, noDragClassName: Er = "nodrag", noWheelClassName: rn = "nowheel", noPanClassName: Yn = "nopan", fitView: Un, fitViewOptions: Zn, connectOnClick: Ir, attributionPosition: kr, proOptions: Ar, defaultEdgeOptions: _r, elevateNodesOnSelect: Dr = !0, elevateEdgesOnSelect: Tr = !1, disableKeyboardA11y: Gn = !1, autoPanOnConnect: Pr, autoPanOnNodeDrag: $r, autoPanOnSelection: Mr = !0, autoPanSpeed: Rr, connectionRadius: Jn, isValidConnection: Qn, onError: eo, style: Lr, id: to, nodeDragThreshold: zr, connectionDragThreshold: Vr, viewport: Or, onViewportChange: Hr, width: Wr, height: Fr, colorMode: Br = "light", debug: Kr, onScroll: no, ariaLabelConfig: Xr, zIndexMode: oo = "basic", ...qr }, Yr) {
  const lt = to || "1", ro = nw(Br), Ur = ie((io) => {
    io.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), no?.(io);
  }, [no]);
  return i.jsx("div", { "data-testid": "rf__wrapper", ...qr, onScroll: Ur, style: { ...Lr, ...Cv }, ref: Yr, className: Se(["react-flow", r, ro]), id: to, role: "application", children: i.jsxs(Sv, { nodes: e, edges: t, width: Wr, height: Fr, fitView: Un, fitViewOptions: Zn, minZoom: K, maxZoom: X, nodeOrigin: Te, nodeExtent: ye, zIndexMode: oo, children: [i.jsx(tw, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: h, onConnectStart: g, onConnectEnd: m, onClickConnectStart: w, onClickConnectEnd: x, nodesDraggable: ge, autoPanOnNodeFocus: we, nodesConnectable: Ae, nodesFocusable: Ie, edgesFocusable: tt, edgesReconnectable: We, elementsSelectable: z, elevateNodesOnSelect: Dr, elevateEdgesOnSelect: Tr, minZoom: K, maxZoom: X, nodeExtent: ye, onNodesChange: Sr, onEdgesChange: Cr, snapToGrid: oe, snapGrid: de, connectionMode: W, translateExtent: se, connectOnClick: Ir, defaultEdgeOptions: _r, fitView: Un, fitViewOptions: Zn, onNodesDelete: L, onEdgesDelete: A, onDelete: _, onNodeDragStart: S, onNodeDrag: E, onNodeDragStop: D, onSelectionDrag: I, onSelectionDragStart: C, onSelectionDragStop: k, onMove: d, onMoveStart: f, onMoveEnd: p, noPanClassName: Yn, nodeOrigin: Te, rfId: lt, autoPanOnConnect: Pr, autoPanOnNodeDrag: $r, autoPanSpeed: Rr, onError: eo, connectionRadius: Jn, isValidConnection: Qn, selectNodesOnDrag: ee, nodeDragThreshold: zr, connectionDragThreshold: Vr, onBeforeDelete: F, debug: Kr, ariaLabelConfig: Xr, zIndexMode: oo }), i.jsx(vv, { onInit: l, onNodeClick: c, onEdgeClick: u, onNodeMouseEnter: b, onNodeMouseMove: y, onNodeMouseLeave: v, onNodeContextMenu: j, onNodeDoubleClick: N, nodeTypes: s, edgeTypes: a, connectionLineType: O, connectionLineStyle: Z, connectionLineComponent: U, connectionLineContainerStyle: te, selectionKeyCode: G, selectionOnDrag: M, selectionMode: q, deleteKeyCode: le, multiSelectionKeyCode: ce, panActivationKeyCode: ae, zoomActivationKeyCode: Q, onlyRenderVisibleElements: V, defaultViewport: H, translateExtent: se, minZoom: K, maxZoom: X, preventScrolling: pe, zoomOnScroll: Ee, zoomOnPinch: Me, zoomOnDoubleClick: gr, panOnScroll: ke, panOnScrollSpeed: hr, panOnScrollMode: He, panOnDrag: Bn, autoPanOnSelection: Mr, onPaneClick: At, onPaneMouseEnter: yr, onPaneMouseMove: Ye, onPaneMouseLeave: _t, onPaneScroll: mr, onPaneContextMenu: Kn, paneClickDistance: xr, nodeClickDistance: wr, onSelectionContextMenu: T, onSelectionStart: $, onSelectionEnd: P, onReconnect: vr, onReconnectStart: Dt, onReconnectEnd: br, onEdgeContextMenu: Tt, onEdgeDoubleClick: Nr, onEdgeMouseEnter: Xn, onEdgeMouseMove: qn, onEdgeMouseLeave: jr, reconnectRadius: on, defaultMarkerColor: je, noDragClassName: Er, noWheelClassName: rn, noPanClassName: Yn, rfId: lt, disableKeyboardA11y: Gn, nodeExtent: ye, viewport: Or, onViewportChange: Hr }), i.jsx(Gx, { onSelectionChange: R }), ct, i.jsx(Xx, { proOptions: Ar, position: kr }), i.jsx(Kx, { rfId: lt, disableKeyboardA11y: Gn })] }) });
}
var Zu = Eu(Ev);
const Iv = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function kv({ children: e }) {
  const t = he(Iv);
  return t ? zx.createPortal(e, t) : null;
}
function Av({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return i.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Se(["react-flow__background-pattern", n, o]) });
}
function _v({ radius: e, className: t }) {
  return i.jsx("circle", { cx: e, cy: e, r: e, className: Se(["react-flow__background-pattern", "dots", t]) });
}
var pt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(pt || (pt = {}));
const Dv = {
  [pt.Dots]: 1,
  [pt.Lines]: 1,
  [pt.Cross]: 6
}, Tv = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function Gu({
  id: e,
  variant: t = pt.Dots,
  // only used for dots and cross
  gap: n = 20,
  // only used for lines and cross
  size: o,
  lineWidth: r = 1,
  offset: s = 0,
  color: a,
  bgColor: c,
  style: u,
  className: l,
  patternClassName: d
}) {
  const f = re(null), { transform: p, patternId: h } = he(Tv, me), g = o || Dv[t], m = t === pt.Dots, w = t === pt.Cross, x = Array.isArray(n) ? n : [n, n], b = [x[0] * p[2] || 1, x[1] * p[2] || 1], y = g * p[2], v = Array.isArray(s) ? s : [s, s], j = w ? [y, y] : b, N = [
    v[0] * p[2] || 1 + j[0] / 2,
    v[1] * p[2] || 1 + j[1] / 2
  ], S = `${h}${e || ""}`;
  return i.jsxs("svg", { className: Se(["react-flow__background", l]), style: {
    ...u,
    ...dr,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": a
  }, ref: f, "data-testid": "rf__background", children: [i.jsx("pattern", { id: S, x: p[0] % b[0], y: p[1] % b[1], width: b[0], height: b[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${N[0]},-${N[1]})`, children: m ? i.jsx(_v, { radius: y / 2, className: d }) : i.jsx(Av, { dimensions: j, lineWidth: r, variant: t, className: d }) }), i.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${S})` })] });
}
Gu.displayName = "Background";
const Ju = be(Gu);
function Pv() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: i.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function $v() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: i.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function Mv() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: i.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function Rv() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: i.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function Lv() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: i.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function go({ children: e, className: t, ...n }) {
  return i.jsx("button", { type: "button", className: Se(["react-flow__controls-button", t]), ...n, children: e });
}
const zv = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function Qu({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: s, onZoomOut: a, onFitView: c, onInteractiveChange: u, className: l, children: d, position: f = "bottom-left", orientation: p = "vertical", "aria-label": h }) {
  const g = xe(), { isInteractive: m, minZoomReached: w, maxZoomReached: x, ariaLabelConfig: b } = he(zv, me), { zoomIn: y, zoomOut: v, fitView: j } = ds(), N = () => {
    y(), s?.();
  }, S = () => {
    v(), a?.();
  }, E = () => {
    j(r), c?.();
  }, D = () => {
    g.setState({
      nodesDraggable: !m,
      nodesConnectable: !m,
      elementsSelectable: !m
    }), u?.(!m);
  }, L = p === "horizontal" ? "horizontal" : "vertical";
  return i.jsxs(ur, { className: Se(["react-flow__controls", L, l]), position: f, style: e, "data-testid": "rf__controls", "aria-label": h ?? b["controls.ariaLabel"], children: [t && i.jsxs(i.Fragment, { children: [i.jsx(go, { onClick: N, className: "react-flow__controls-zoomin", title: b["controls.zoomIn.ariaLabel"], "aria-label": b["controls.zoomIn.ariaLabel"], disabled: x, children: i.jsx(Pv, {}) }), i.jsx(go, { onClick: S, className: "react-flow__controls-zoomout", title: b["controls.zoomOut.ariaLabel"], "aria-label": b["controls.zoomOut.ariaLabel"], disabled: w, children: i.jsx($v, {}) })] }), n && i.jsx(go, { className: "react-flow__controls-fitview", onClick: E, title: b["controls.fitView.ariaLabel"], "aria-label": b["controls.fitView.ariaLabel"], children: i.jsx(Mv, {}) }), o && i.jsx(go, { className: "react-flow__controls-interactive", onClick: D, title: b["controls.interactive.ariaLabel"], "aria-label": b["controls.interactive.ariaLabel"], children: m ? i.jsx(Lv, {}) : i.jsx(Rv, {}) }), d] });
}
Qu.displayName = "Controls";
const ed = be(Qu);
function Vv({ id: e, x: t, y: n, width: o, height: r, style: s, color: a, strokeColor: c, strokeWidth: u, className: l, borderRadius: d, shapeRendering: f, selected: p, onClick: h }) {
  const { background: g, backgroundColor: m } = s || {}, w = a || g || m;
  return i.jsx("rect", { className: Se(["react-flow__minimap-node", { selected: p }, l]), x: t, y: n, rx: d, ry: d, width: o, height: r, style: {
    fill: w,
    stroke: c,
    strokeWidth: u
  }, shapeRendering: f, onClick: h ? (x) => h(x, e) : void 0 });
}
const Ov = be(Vv), Hv = (e) => e.nodes.map((t) => t.id), pi = (e) => e instanceof Function ? e : () => e;
function Wv({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: s = Ov,
  onClick: a
}) {
  const c = he(Hv, me), u = pi(t), l = pi(e), d = pi(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return i.jsx(i.Fragment, { children: c.map((p) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    i.jsx(Bv, { id: p, nodeColorFunc: u, nodeStrokeColorFunc: l, nodeClassNameFunc: d, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: s, onClick: a, shapeRendering: f }, p)
  )) });
}
function Fv({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: s, shapeRendering: a, NodeComponent: c, onClick: u }) {
  const { node: l, x: d, y: f, width: p, height: h } = he((g) => {
    const m = g.nodeLookup.get(e);
    if (!m)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const w = m.internals.userNode, { x, y: b } = m.internals.positionAbsolute, { width: y, height: v } = at(w);
    return {
      node: w,
      x,
      y: b,
      width: y,
      height: v
    };
  }, me);
  return !l || l.hidden || !Ul(l) ? null : i.jsx(c, { x: d, y: f, width: p, height: h, style: l.style, selected: !!l.selected, className: o(l), color: t(l), borderRadius: r, strokeColor: n(l), strokeWidth: s, shapeRendering: a, onClick: u, id: l.id });
}
const Bv = be(Fv);
var Kv = be(Wv);
const Xv = 200, qv = 150, Yv = (e) => !e.hidden, Uv = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? ql(On(e.nodeLookup, { filter: Yv }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, Zv = "react-flow__minimap-desc";
function td({
  style: e,
  className: t,
  nodeStrokeColor: n,
  nodeColor: o,
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
  onNodeClick: g,
  pannable: m = !1,
  zoomable: w = !1,
  ariaLabel: x,
  inversePan: b,
  zoomStep: y = 1,
  offsetScale: v = 5
}) {
  const j = xe(), N = re(null), { boundingRect: S, viewBB: E, rfId: D, panZoom: L, translateExtent: A, flowWidth: _, flowHeight: R, ariaLabelConfig: C } = he(Uv, me), I = e?.width ?? Xv, k = e?.height ?? qv, T = S.width / I, $ = S.height / k, P = Math.max(T, $), F = P * I, W = P * k, O = v * P, Z = S.x - (F - S.width) / 2 - O, U = S.y - (W - S.height) / 2 - O, te = F + O * 2, le = W + O * 2, G = `${Zv}-${D}`, M = re(0), q = re();
  M.current = P, J(() => {
    if (N.current && L)
      return q.current = ux({
        domNode: N.current,
        panZoom: L,
        getTransform: () => j.getState().transform,
        getViewScale: () => M.current
      }), () => {
        q.current?.destroy();
      };
  }, [L]), J(() => {
    q.current?.update({
      translateExtent: A,
      width: _,
      height: R,
      inversePan: b,
      pannable: m,
      zoomStep: y,
      zoomable: w
    });
  }, [m, w, b, y, A, _, R]);
  const ae = h ? (oe) => {
    const [de, V] = q.current?.pointer(oe) || [0, 0];
    h(oe, { x: de, y: V });
  } : void 0, ce = g ? ie((oe, de) => {
    const V = j.getState().nodeLookup.get(de).internals.userNode;
    g(oe, V);
  }, []) : void 0, Q = x ?? C["minimap.ariaLabel"];
  return i.jsx(ur, { position: p, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * P : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof a == "number" ? a : void 0
  }, className: Se(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: i.jsxs("svg", { width: I, height: k, viewBox: `${Z} ${U} ${te} ${le}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": G, ref: N, onClick: ae, children: [Q && i.jsx("title", { id: G, children: Q }), i.jsx(Kv, { onClick: ce, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: s, nodeClassName: r, nodeStrokeWidth: a, nodeComponent: c }), i.jsx("path", { className: "react-flow__minimap-mask", d: `M${Z - O},${U - O}h${te + O * 2}v${le + O * 2}h${-te - O * 2}z
        M${E.x},${E.y}h${E.width}v${E.height}h${-E.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
td.displayName = "MiniMap";
const nd = be(td), Gv = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, Jv = {
  [Zt.Line]: "right",
  [Zt.Handle]: "bottom-right"
};
function Qv({ nodeId: e, position: t, variant: n = Zt.Handle, className: o, style: r = void 0, children: s, color: a, minWidth: c = 10, minHeight: u = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: p, autoScale: h = !0, shouldResize: g, onResizeStart: m, onResize: w, onResizeEnd: x }) {
  const b = _u(), y = typeof e == "string" ? e : b, v = xe(), j = re(null), N = n === Zt.Handle, S = he(ie(Gv(N && h), [N, h]), me), E = re(null), D = t ?? Jv[n];
  J(() => {
    if (!(!j.current || !y))
      return E.current || (E.current = jx({
        domNode: j.current,
        nodeId: y,
        getStoreItems: () => {
          const { nodeLookup: A, transform: _, snapGrid: R, snapToGrid: C, nodeOrigin: I, domNode: k } = v.getState();
          return {
            nodeLookup: A,
            transform: _,
            snapGrid: R,
            snapToGrid: C,
            nodeOrigin: I,
            paneDomNode: k
          };
        },
        onChange: (A, _) => {
          const { triggerNodeChanges: R, nodeLookup: C, parentLookup: I, nodeOrigin: k } = v.getState(), T = [], $ = { x: A.x, y: A.y }, P = C.get(y);
          if (P && P.expandParent && P.parentId) {
            const F = P.origin ?? k, W = A.width ?? P.measured.width ?? 0, O = A.height ?? P.measured.height ?? 0, Z = {
              id: P.id,
              parentId: P.parentId,
              rect: {
                width: W,
                height: O,
                ...Zl({
                  x: A.x ?? P.position.x,
                  y: A.y ?? P.position.y
                }, { width: W, height: O }, P.parentId, C, F)
              }
            }, U = us([Z], C, I, k);
            T.push(...U), $.x = A.x ? Math.max(F[0] * W, A.x) : void 0, $.y = A.y ? Math.max(F[1] * O, A.y) : void 0;
          }
          if ($.x !== void 0 && $.y !== void 0) {
            const F = {
              id: y,
              type: "position",
              position: { ...$ }
            };
            T.push(F);
          }
          if (A.width !== void 0 && A.height !== void 0) {
            const W = {
              id: y,
              type: "dimensions",
              resizing: !0,
              setAttributes: p ? p === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: A.width,
                height: A.height
              }
            };
            T.push(W);
          }
          for (const F of _) {
            const W = {
              ...F,
              type: "position"
            };
            T.push(W);
          }
          R(T);
        },
        onEnd: ({ width: A, height: _ }) => {
          const R = {
            id: y,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: A,
              height: _
            }
          };
          v.getState().triggerNodeChanges([R]);
        }
      })), E.current.update({
        controlPosition: D,
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
    D,
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
  const L = D.split("-");
  return i.jsx("div", { className: Se(["react-flow__resize-control", "nodrag", ...L, n, o]), ref: j, style: {
    ...r,
    scale: S,
    ...a && { [N ? "backgroundColor" : "borderColor"]: a }
  }, children: s });
}
be(Qv);
function e0(e, t) {
  return {
    ...t ? { name: t } : {},
    definitionId: e.definitionId,
    state: Mn(e.state),
    layout: e.layout
  };
}
function t0(e) {
  return JSON.stringify(
    {
      state: Mn(e.state),
      layout: e.layout
    },
    null,
    2
  );
}
function n0(e, t) {
  let n;
  try {
    n = JSON.parse(e);
  } catch (r) {
    return { ok: !1, error: r instanceof Error ? r.message : String(r) };
  }
  if (!n || typeof n != "object")
    return { ok: !1, error: "Workflow JSON must be an object with a 'state' property." };
  const o = n;
  return !o.state || typeof o.state != "object" ? { ok: !1, error: "Workflow JSON is missing a valid 'state' object." } : o.layout !== void 0 && !Array.isArray(o.layout) ? { ok: !1, error: "'layout' must be an array when present." } : {
    ok: !0,
    draft: {
      ...t,
      state: Jo(o.state),
      layout: o.layout ?? t.layout
    }
  };
}
function o0(e, t) {
  const n = (t ?? "workflow").trim().replace(/[^\w.-]+/g, "-") || "workflow", o = new Blob([JSON.stringify(e, null, 2)], { type: "application/json" }), r = URL.createObjectURL(o), s = document.createElement("a");
  s.href = r, s.download = `${n}.json`, document.body.appendChild(s), s.click(), s.remove(), URL.revokeObjectURL(r);
}
function Qa({
  document: e,
  readOnly: t,
  minHeight: n,
  ariaLabel: o,
  onChange: r
}) {
  const s = (a) => {
    t || r({ ...e, value: a.target.value });
  };
  return /* @__PURE__ */ i.jsx(
    "textarea",
    {
      "aria-label": o,
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
function r0({
  document: e,
  diagnostics: t = [],
  readOnly: n = !1,
  theme: o = "studio",
  minHeight: r = "220px",
  ariaLabel: s,
  languageAdapter: a,
  onChange: c
}) {
  const u = t.filter((p) => !p.uri || p.uri === e.uri), l = a?.displayName ?? e.language, d = a?.loadEditor, f = fe(
    () => d ? Pd(d) : null,
    [d]
  );
  return /* @__PURE__ */ i.jsxs(
    "section",
    {
      className: "studio-code-editor",
      "data-language": e.language,
      "data-theme": o,
      "data-readonly": n,
      children: [
        /* @__PURE__ */ i.jsxs("div", { className: "studio-code-editor-header", children: [
          /* @__PURE__ */ i.jsx("span", { children: l }),
          /* @__PURE__ */ i.jsx("code", { children: e.uri })
        ] }),
        f ? /* @__PURE__ */ i.jsx($d, { fallback: /* @__PURE__ */ i.jsx(
          Qa,
          {
            document: e,
            readOnly: n,
            minHeight: r,
            ariaLabel: s,
            onChange: c
          }
        ), children: /* @__PURE__ */ i.jsx(
          f,
          {
            document: e,
            readOnly: n,
            theme: o,
            minHeight: r,
            ariaLabel: s,
            onChange: c
          }
        ) }) : /* @__PURE__ */ i.jsx(
          Qa,
          {
            document: e,
            readOnly: n,
            minHeight: r,
            ariaLabel: s,
            onChange: c
          }
        ),
        /* @__PURE__ */ i.jsx(i0, { diagnostics: u })
      ]
    }
  );
}
function i0({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ i.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const o = t.severity ?? "info", r = s0(t);
    return /* @__PURE__ */ i.jsxs(
      "p",
      {
        className: `studio-code-editor-diagnostic ${o}`,
        children: [
          t.code ? /* @__PURE__ */ i.jsx("span", { children: t.code }) : null,
          r ? /* @__PURE__ */ i.jsx("small", { children: r }) : null,
          t.message
        ]
      },
      `${t.uri ?? "document"}-${t.code ?? "diagnostic"}-${n}`
    );
  }) });
}
function s0(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const a0 = { language: "json", displayName: "JSON" };
function c0({ draft: e, onApply: t }) {
  const n = fe(() => t0(e), [e]), [o, r] = B(n), [s, a] = B(n), [c, u] = B(null);
  J(() => {
    r(n), a(n), u(null);
  }, [n]);
  const l = o !== s, d = c ? [{ severity: "error", message: c }] : [], f = () => u(t(o));
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-code-view", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-code-view-toolbar", children: [
      /* @__PURE__ */ i.jsx("span", { className: "wf-muted", children: "Edit the workflow definition as JSON. Changes apply to the draft when you click Apply." }),
      /* @__PURE__ */ i.jsxs("span", { className: "wf-code-view-actions", children: [
        /* @__PURE__ */ i.jsx("button", { type: "button", disabled: !l, onClick: () => {
          r(s), u(null);
        }, children: "Reset" }),
        /* @__PURE__ */ i.jsxs("button", { type: "button", disabled: !l, onClick: f, children: [
          /* @__PURE__ */ i.jsx(Qt, { size: 14 }),
          " Apply"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ i.jsx("div", { className: "wf-code-view-editor", children: /* @__PURE__ */ i.jsx(
      r0,
      {
        ariaLabel: "Workflow JSON",
        document: { uri: "elsa://workflows/definition.json", language: "json", value: o },
        languageAdapter: a0,
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
function ze(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function ps(e, t) {
  if (!e || !t) return "";
  const n = Date.parse(e), o = Date.parse(t);
  if (Number.isNaN(n) || Number.isNaN(o) || o < n) return "";
  const r = Math.round((o - n) / 1e3);
  if (r < 60) return `${r}s`;
  const s = Math.floor(r / 60), a = r % 60;
  if (s < 60) return a ? `${s}m ${a}s` : `${s}m`;
  const c = Math.floor(s / 60), u = s % 60;
  return u ? `${c}h ${u}m` : `${c}h`;
}
function Jt(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function hs(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ i.jsx(Pc, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ i.jsx(Wi, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ i.jsx(zd, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ i.jsx(Ht, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ i.jsx(Ld, { size: 15 });
    default:
      return /* @__PURE__ */ i.jsx(Zo, { size: 15 });
  }
}
const l0 = ["Single", "Array", "List", "HashSet"];
function od(e) {
  const [t, n] = B(null), [o, r] = B(null);
  J(() => {
    let u = !1;
    return Dp(e).then(
      (l) => {
        u || n(l);
      },
      () => {
        u || n([]);
      }
    ), Pp(e).then(
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
  const s = fe(
    () => t && t.length > 0 ? t.map((u) => {
      const l = $s(u);
      return {
        value: l,
        label: Wc(u.displayName, l),
        group: u.category?.trim() || "Other"
      };
    }) : null,
    [t]
  ), a = fe(
    () => o && o.length > 0 ? o.filter((u) => !u.deprecated).map((u) => ({
      value: u.typeName,
      label: hf(u.displayName, u.typeName)
    })) : null,
    [o]
  ), c = fe(() => {
    const u = /* @__PURE__ */ new Map();
    for (const l of t ?? []) {
      const d = $s(l), f = l.defaultEditor?.trim();
      d && u.set(d, f && f.length > 0 ? f : "text");
    }
    return (l) => u.get(l) ?? "text";
  }, [t]);
  return { typeOptions: s, storageOptions: a, editorForAlias: c };
}
function u0(e) {
  return !e || e.length === 0 ? void 0 : (e.find((n) => /(^|\.)String$/i.test(n.value) || n.label.toLowerCase() === "string") ?? e[0]).value;
}
function d0(e, t, n) {
  return {
    add: () => {
      const o = of(n.namePrefix, e.map((r) => wn(r, n.nameKeys)));
      t([...e, n.create(o)]);
    },
    update: (o, r) => t(e.map((s, a) => a === o ? n.patch(s, r) : s)),
    remove: (o) => t(e.filter((r, s) => s !== o))
  };
}
function ec({ value: e, options: t, placeholder: n, allowEmpty: o, ariaLabel: r, onChange: s }) {
  if (!t)
    return /* @__PURE__ */ i.jsx(
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
  return /* @__PURE__ */ i.jsxs("select", { "aria-label": r, value: e, onChange: (l) => s(l.target.value), children: [
    o ? /* @__PURE__ */ i.jsx("option", { value: "", children: n ?? "—" }) : null,
    a ? null : /* @__PURE__ */ i.jsxs("option", { value: e, disabled: !0, children: [
      e,
      " (unresolved)"
    ] }),
    u ? c.map((l) => /* @__PURE__ */ i.jsx("optgroup", { label: l, children: t.filter((d) => d.group === l).map((d) => /* @__PURE__ */ i.jsx("option", { value: d.value, children: d.label }, d.value)) }, l)) : t.map((l) => /* @__PURE__ */ i.jsx("option", { value: l.value, children: l.label }, l.value))
  ] });
}
const f0 = {
  Single: "Single",
  Array: "Array",
  List: "List",
  HashSet: "Hash set"
};
function p0({ value: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ i.jsx("select", { "aria-label": t, value: e, onChange: (o) => n(o.target.value), children: l0.map((o) => /* @__PURE__ */ i.jsx("option", { value: o, children: f0[o] }, o)) });
}
function h0(e, t) {
  return t === "" ? !0 : e === "checkbox" ? t === "true" || t === "false" || t === "True" || t === "False" : e === "number" ? Number.isFinite(Number(t.trim())) && t.trim() !== "" : e === "date" ? /^\d{4}-\d{2}-\d{2}$/.test(t) : !0;
}
function g0({ value: e, editor: t, ariaLabel: n, onChange: o }) {
  const r = h0(t, e);
  return r && t === "checkbox" ? /* @__PURE__ */ i.jsx(
    "input",
    {
      type: "checkbox",
      "aria-label": n,
      checked: e === "true" || e === "True",
      onChange: (s) => o(s.target.checked ? "true" : "false")
    }
  ) : r && (t === "number" || t === "date") ? /* @__PURE__ */ i.jsx(
    "input",
    {
      type: t,
      "aria-label": n,
      value: e,
      placeholder: "(empty)",
      onChange: (s) => o(s.target.value)
    }
  ) : /* @__PURE__ */ i.jsx(
    "input",
    {
      type: "text",
      "aria-label": n,
      value: e,
      placeholder: "(empty)",
      disabled: t === "none",
      onChange: (s) => o(s.target.value)
    }
  );
}
function y0({ title: e, addLabel: t, emptyLabel: n, headers: o, isEmpty: r, onAdd: s, children: a }) {
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-properties-section-head", children: [
      /* @__PURE__ */ i.jsx("h3", { children: e }),
      /* @__PURE__ */ i.jsxs("button", { type: "button", className: "wf-properties-add", onClick: s, children: [
        /* @__PURE__ */ i.jsx(Wt, { size: 14 }),
        " ",
        t
      ] })
    ] }),
    r ? /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: n }) : /* @__PURE__ */ i.jsxs("table", { className: "wf-properties-table", children: [
      /* @__PURE__ */ i.jsx("thead", { children: /* @__PURE__ */ i.jsxs("tr", { children: [
        o.map((c) => /* @__PURE__ */ i.jsx("th", { children: c }, c)),
        /* @__PURE__ */ i.jsx("th", { "aria-label": "Actions" })
      ] }) }),
      /* @__PURE__ */ i.jsx("tbody", { children: a })
    ] })
  ] });
}
function m0({ label: e, onRemove: t }) {
  return /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx("button", { type: "button", className: "wf-properties-remove", "aria-label": e, title: e, onClick: t, children: /* @__PURE__ */ i.jsx(jn, { size: 14 }) }) });
}
function x0({ checked: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ i.jsx("input", { type: "checkbox", "aria-label": t, checked: e, onChange: (o) => n(o.target.checked) });
}
function gs({
  items: e,
  typeOptions: t,
  storageOptions: n,
  editorForAlias: o,
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
  const { add: g, update: m, remove: w } = d0(e, h, {
    namePrefix: r,
    nameKeys: s,
    create: (y) => l(y, u0(t)),
    patch: d
  }), x = ["Name", "Type", "Collection", ...f.default ? ["Default"] : [], ...f.storage ? ["Storage"] : [], ...f.required ? ["Required"] : []], b = r.toLowerCase();
  return /* @__PURE__ */ i.jsx(
    y0,
    {
      title: a,
      addLabel: c,
      emptyLabel: u,
      headers: x,
      isEmpty: e.length === 0,
      onAdd: g,
      children: e.map((y, v) => {
        const j = wn(y, s), N = Hc(y), S = wn(y, Bc), E = S ? p?.get(S) : void 0, D = N.collectionKind === "Single" ? o(N.alias) : "text";
        return /* @__PURE__ */ i.jsxs("tr", { children: [
          /* @__PURE__ */ i.jsxs("td", { children: [
            /* @__PURE__ */ i.jsx("input", { type: "text", "aria-label": `${r} name`, value: j, onChange: (L) => m(v, { name: L.target.value }) }),
            E ? /* @__PURE__ */ i.jsx("span", { className: "wf-properties-warning", role: "note", title: E, children: E }) : null
          ] }),
          /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            ec,
            {
              ariaLabel: `${r} type`,
              value: N.alias,
              options: t,
              placeholder: "Type",
              onChange: (L) => m(v, { type: { alias: L, collectionKind: N.collectionKind } })
            }
          ) }),
          /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            p0,
            {
              ariaLabel: `${r} collection kind`,
              value: N.collectionKind,
              onChange: (L) => m(v, { type: { alias: N.alias, collectionKind: L } })
            }
          ) }),
          f.default ? /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            g0,
            {
              ariaLabel: `${r} default value`,
              value: cf(y.default),
              editor: D,
              onChange: (L) => m(v, { default: af(L) })
            }
          ) }) : null,
          f.storage ? /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            ec,
            {
              ariaLabel: `${r} storage driver`,
              value: wn(y, yf),
              options: n,
              placeholder: "—",
              allowEmpty: !0,
              onChange: (L) => m(v, { storageDriverType: L || null })
            }
          ) }) : null,
          f.required ? /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            x0,
            {
              ariaLabel: `${r} required`,
              checked: y.isRequired === !0,
              onChange: (L) => m(v, { isRequired: L })
            }
          ) }) : null,
          /* @__PURE__ */ i.jsx(m0, { label: `Remove ${b} ${j || v + 1}`, onRemove: () => w(v) })
        ] }, v);
      })
    }
  );
}
function rd({ items: e, typeOptions: t, storageOptions: n, editorForAlias: o, title: r = "Variables", addLabel: s = "Add variable", emptyLabel: a = "No variables defined.", warnings: c, onChange: u }) {
  return /* @__PURE__ */ i.jsx(
    gs,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: o,
      namePrefix: "Variable",
      nameKeys: gf,
      title: r,
      addLabel: s,
      emptyLabel: a,
      create: (l, d) => rf({ name: l, alias: d }),
      patch: (l, d) => sf(l, d),
      columns: { default: !0, storage: !0 },
      warnings: c,
      onChange: u
    }
  );
}
function w0({ items: e, typeOptions: t, storageOptions: n, editorForAlias: o, onChange: r }) {
  return /* @__PURE__ */ i.jsx(
    gs,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: o,
      namePrefix: "Input",
      nameKeys: Fc,
      title: "Inputs",
      addLabel: "Add input",
      emptyLabel: "No inputs defined.",
      create: (s, a) => lf({ name: s, alias: a }),
      patch: (s, a) => uf(s, a),
      columns: { default: !1, storage: !0, required: !0 },
      onChange: r
    }
  );
}
function v0({ items: e, typeOptions: t, storageOptions: n, editorForAlias: o, onChange: r }) {
  return /* @__PURE__ */ i.jsx(
    gs,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: o,
      namePrefix: "Output",
      nameKeys: Fc,
      title: "Outputs",
      addLabel: "Add output",
      emptyLabel: "No outputs defined.",
      create: (s, a) => df({ name: s, alias: a }),
      patch: (s, a) => ff(s, a),
      columns: { default: !1, storage: !1 },
      onChange: r
    }
  );
}
function Eo(e) {
  return (e ?? []).filter(To);
}
function b0({ context: e, variables: t, title: n, addLabel: o, emptyLabel: r, warnings: s, onChange: a }) {
  const { typeOptions: c, storageOptions: u, editorForAlias: l } = od(e);
  return /* @__PURE__ */ i.jsx(
    rd,
    {
      items: Eo(t),
      typeOptions: c,
      storageOptions: u,
      editorForAlias: l,
      title: n,
      addLabel: o,
      emptyLabel: r,
      warnings: s,
      onChange: a
    }
  );
}
function N0({ definition: e, definitionId: t, onMetaChange: n }) {
  const o = !!n, [r, s] = B(e?.name ?? ""), [a, c] = B(e?.description ?? "");
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
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ i.jsx("h3", { children: "Information" }),
    /* @__PURE__ */ i.jsxs("dl", { className: "wf-properties-info", children: [
      /* @__PURE__ */ i.jsx("dt", { children: /* @__PURE__ */ i.jsx("label", { htmlFor: "wf-def-name", children: "Name" }) }),
      /* @__PURE__ */ i.jsx("dd", { children: o ? /* @__PURE__ */ i.jsx(
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
      /* @__PURE__ */ i.jsx("dt", { children: /* @__PURE__ */ i.jsx("label", { htmlFor: "wf-def-description", children: "Description" }) }),
      /* @__PURE__ */ i.jsx("dd", { children: o ? /* @__PURE__ */ i.jsx(
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
      ) : e?.description?.trim() ? e.description : /* @__PURE__ */ i.jsx("span", { className: "wf-muted", children: "No description" }) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Definition ID" }),
      /* @__PURE__ */ i.jsx("dd", { children: /* @__PURE__ */ i.jsx("code", { children: t }) })
    ] })
  ] });
}
function j0({ details: e, draft: t, context: n, onStateChange: o, onDefinitionMetaChange: r }) {
  const { typeOptions: s, storageOptions: a, editorForAlias: c } = od(n), u = Eo(t.state.variables), l = Eo(t.state.inputs), d = Eo(t.state.outputs), f = e?.versions ?? [];
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-properties-view", children: [
    /* @__PURE__ */ i.jsx(
      N0,
      {
        definition: e?.definition,
        definitionId: t.definitionId,
        onMetaChange: r
      }
    ),
    /* @__PURE__ */ i.jsx(
      rd,
      {
        items: u,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => o((h) => ({ ...h, variables: p }))
      }
    ),
    /* @__PURE__ */ i.jsx(
      w0,
      {
        items: l,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => o((h) => ({ ...h, inputs: p }))
      }
    ),
    /* @__PURE__ */ i.jsx(
      v0,
      {
        items: d,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => o((h) => ({ ...h, outputs: p }))
      }
    ),
    /* @__PURE__ */ i.jsxs("section", { className: "wf-properties-section", children: [
      /* @__PURE__ */ i.jsx("h3", { children: "Versions" }),
      f.length === 0 ? /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "No published versions yet." }) : /* @__PURE__ */ i.jsx("ul", { className: "wf-properties-versions", children: f.map((p) => /* @__PURE__ */ i.jsxs("li", { children: [
        /* @__PURE__ */ i.jsxs("span", { className: "wf-properties-version-tag", children: [
          "v",
          p.version
        ] }),
        /* @__PURE__ */ i.jsx("time", { children: ze(p.createdAt) })
      ] }, p.id)) })
    ] })
  ] });
}
const tc = "application/x-elsa-activity-version-id", S0 = 6, C0 = 1200, E0 = 250, I0 = [10, 25, 50], k0 = 10, nc = "elsa-studio-workflow-palette-width", oc = "elsa-studio-workflow-inspector-width", rc = "elsa-studio-workflow-palette-collapsed", ic = "elsa-studio-workflow-inspector-collapsed", id = "elsa-studio-workflow-side-panel-maximized", pn = 180, hn = 460, A0 = 260, gn = 260, yn = 560, _0 = 320, sc = 42, yo = 16, sd = Ge.createContext(null), ad = Ge.createContext(null);
function D0(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function cd(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Et(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function It(e, t, n) {
  const o = t.createPrompt(n);
  return o ? (e.dispatchPrompt(o), !0) : !1;
}
function T0(e) {
  if (!e) return null;
  const t = [], n = e.match(/```(?:json)?\s*([\s\S]*?)```/i);
  n && t.push(n[1]);
  const o = e.match(/\{[\s\S]*?\}/);
  o && t.push(o[0]);
  for (const a of t)
    try {
      const c = JSON.parse(a.trim()), u = ac(c.name, c.description);
      if (u) return u;
    } catch {
    }
  const r = e.match(/name\s*[:-]\s*(.+)/i)?.[1], s = e.match(/description\s*[:-]\s*(.+)/i)?.[1];
  return ac(r, s);
}
function ac(e, t) {
  const n = typeof e == "string" ? cc(e) : void 0, o = typeof t == "string" ? cc(t) : void 0;
  return n || o ? { name: n || void 0, description: o || void 0 } : null;
}
function cc(e) {
  return e.trim().replace(/,$/, "").trim().replace(/^["']/, "").replace(/["']$/, "").trim();
}
function P0(e, t) {
  return e.rootActivityVersionId ?? $0(t, e.rootKind)?.activityVersionId ?? null;
}
function $0(e, t) {
  return e.find((n) => M0(n) === t);
}
function M0(e) {
  return e ? R0(e) ? "flowchart" : L0(e) ? "sequence" : null : null;
}
function Pi(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "Uncategorized";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [o]) => n.localeCompare(o)).map(([n, o]) => ({
    category: n,
    activities: o.sort((r, s) => Ce(r).localeCompare(Ce(s)))
  }));
}
function R0(e) {
  return Ce(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function L0(e) {
  return Ce(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function z0(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function ld(e) {
  return F0(e.rootActivityType) || e.rootActivityType;
}
function V0(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function O0(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function H0(e, t) {
  return lc(t) - lc(e);
}
function lc(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function ud(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
function dd(e) {
  const t = e?.workflowExecutionId ?? e?.runId ?? e?.executionId;
  return typeof t == "string" && t.trim() ? t : null;
}
async function W0(e) {
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
function F0(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function B0(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    mo(t, n.typeName, n), mo(t, n.name, n), mo(t, n.displayName, n);
    const o = n.typeName.split(".").filter(Boolean).at(-1);
    mo(t, o, n);
  }
  return t;
}
function K0(e, t, n) {
  const o = t.get(e.activityVersionId);
  return n.get(mn(o?.activityTypeKey)) ?? n.get(mn(Jt(o?.activityTypeKey))) ?? n.get(mn(o?.displayName)) ?? n.get(mn(e.activityVersionId)) ?? null;
}
function mo(e, t, n) {
  const o = mn(t);
  o && !e.has(o) && e.set(o, n);
}
function mn(e) {
  return e?.trim().toLowerCase() ?? "";
}
function uc(e, t, n, o) {
  const r = fr();
  if (!r) return t;
  const s = r.getItem(e);
  if (s == null) return t;
  const a = Number(s);
  return Number.isFinite(a) ? Io(a, n, o) : t;
}
function dc(e, t) {
  const n = fr();
  if (!n) return t;
  const o = n.getItem(e);
  return o === "true" ? !0 : o === "false" ? !1 : t;
}
function X0() {
  const e = fr();
  if (!e) return null;
  const t = e.getItem(id);
  return t === "palette" || t === "inspector" ? t : null;
}
function fr() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function ln(e, t) {
  const n = fr();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function Io(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function fd(e) {
  switch (q0(e)) {
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
function q0(e) {
  return (e ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}
function Y0(e, t) {
  const n = e.state.rootActivity;
  if (!n) return /* @__PURE__ */ new Set();
  const o = t.find((s) => s.activityVersionId === n.activityVersionId);
  if (Xi(n, o) === "unsupported") return /* @__PURE__ */ new Set([n.nodeId]);
  const r = Sn(n, [], t);
  return new Set(r?.slot.activities.map((s) => s.nodeId) ?? [n.nodeId]);
}
function fc(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function pc(e) {
  return `${Ce(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function hc(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function U0(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function pd(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function Z0(e) {
  const t = pd(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function G0(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function Le(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function J0(e) {
  return yd(Le(e));
}
function hd(e, t, n = []) {
  if (!e) return n;
  const o = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: o?.activityTypeKey ?? e.activityVersionId,
    displayName: o ? Ce(o) : void 0
  });
  for (const r of De(e, t))
    for (const s of r.activities) hd(s, t, n);
  return n;
}
function gd(e, t, n = []) {
  if (!e) return n;
  for (const o of nl(e))
    n.push({ source: o.source, target: o.target, sourcePort: o.sourceHandle ?? void 0, targetPort: o.targetHandle ?? void 0 });
  for (const o of De(e, t))
    for (const r of o.activities) gd(r, t, n);
  return n;
}
function xn(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function Q0(e) {
  return `${e.id}-${yd(JSON.stringify(e.state))}`;
}
function yd(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function ys(e) {
  return e.status.toLowerCase() === "rejected";
}
function eb(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function tb(e, t) {
  const n = e instanceof Error ? e.message : String(e);
  return nb(e, n) ? `Run ${t} was not found.` : n;
}
function nb(e, t) {
  if ((typeof e == "object" && e ? e.response?.status ?? e.status : void 0) === 404 || /\b404\b/.test(t)) return !0;
  try {
    const o = JSON.parse(t);
    return [o.error, o.title, o.detail].some((r) => typeof r == "string" && /not found/i.test(r));
  } catch {
    return /not found/i.test(t);
  }
}
function nn({ status: e, subStatus: t }) {
  return /* @__PURE__ */ i.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
const md = { workflowActivity: ob }, xd = { workflow: ib };
function ob({ data: e, selected: t }) {
  const n = e, o = n.runtime, r = !n.suppressFlowPorts, s = r ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], a = rb(n), u = Ge.useContext(ad)?.({ activityVersionId: n.activityVersionId, activityTypeKey: n.activityTypeKey }) ?? null;
  return /* @__PURE__ */ i.jsxs(
    "div",
    {
      className: ["wf-node", t ? "selected" : "", o ? "wf-node-runtime" : "", o?.hasBlockingIncident ? "faulted" : "", u ? "wf-node-unavailable" : ""].filter(Boolean).join(" "),
      "data-icon": n.icon ?? "activity",
      children: [
        r && n.acceptsInbound ? /* @__PURE__ */ i.jsx(Gt, { type: "target", position: ne.Left }) : null,
        u ? /* @__PURE__ */ i.jsx("span", { className: "wf-node-availability", title: `No longer available for new use · ${Ro(u.state)}`, children: /* @__PURE__ */ i.jsx(Ao, { size: 13 }) }) : null,
        /* @__PURE__ */ i.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ i.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: hs(n.icon) }),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-node-copy", children: [
            /* @__PURE__ */ i.jsx("strong", { children: n.label }),
            a ? /* @__PURE__ */ i.jsx("small", { children: a }) : null
          ] })
        ] }),
        n.childSlots.length > 0 ? /* @__PURE__ */ i.jsxs("span", { className: "wf-node-slot-badge", children: [
          n.childSlots.length,
          " slot",
          n.childSlots.length === 1 ? "" : "s"
        ] }) : null,
        o ? /* @__PURE__ */ i.jsxs("div", { className: "wf-node-runtime-strip", children: [
          o.status ? /* @__PURE__ */ i.jsx(nn, { status: o.status, subStatus: o.subStatus }) : null,
          o.incidentCount > 0 ? /* @__PURE__ */ i.jsxs("span", { className: "wf-node-runtime-count", children: [
            o.incidentCount,
            " incident",
            o.incidentCount === 1 ? "" : "s"
          ] }) : null,
          o.faultCount > 0 ? /* @__PURE__ */ i.jsxs("span", { className: "wf-node-runtime-count", children: [
            o.faultCount,
            " faults"
          ] }) : null
        ] }) : null,
        s.map((l, d) => {
          const f = `${(d + 1) / (s.length + 1) * 100}%`;
          return /* @__PURE__ */ i.jsxs(Ge.Fragment, { children: [
            /* @__PURE__ */ i.jsx("span", { className: "wf-node-port-label", style: { top: f }, children: l.displayName }),
            /* @__PURE__ */ i.jsx(Gt, { type: "source", position: ne.Right, id: l.name, style: { top: f } })
          ] }, l.name);
        })
      ]
    }
  );
}
function rb(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((r) => !!r).join(" · ");
}
function ib(e) {
  const {
    id: t,
    sourceX: n,
    sourceY: o,
    targetX: r,
    targetY: s,
    sourcePosition: a,
    targetPosition: c,
    markerEnd: u,
    style: l,
    label: d,
    labelStyle: f
  } = e, p = Ge.useContext(sd), [h, g] = B(!1), [m, w, x] = qo({ sourceX: n, sourceY: o, targetX: r, targetY: s, sourcePosition: a, targetPosition: c }), b = p?.highlightedEdgeId === t;
  return /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
    /* @__PURE__ */ i.jsx(
      Wn,
      {
        id: t,
        path: m,
        markerEnd: u,
        style: {
          ...l,
          strokeWidth: b ? 2.5 : l?.strokeWidth
        },
        label: d,
        labelX: w,
        labelY: x,
        labelStyle: f,
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1)
      }
    ),
    p ? /* @__PURE__ */ i.jsx(kv, { children: /* @__PURE__ */ i.jsxs(
      "div",
      {
        className: ["wf-edge-actions", h ? "visible" : "", b ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${w}px, ${x}px)` },
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1),
        children: [
          /* @__PURE__ */ i.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (y) => p.requestInsertActivity(t, y.clientX, y.clientY), children: /* @__PURE__ */ i.jsx(Wt, { size: 12 }) }),
          /* @__PURE__ */ i.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => p.deleteEdge(t), children: /* @__PURE__ */ i.jsx(jn, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function sb({ clientX: e, clientY: t, activities: n, onPick: o, onClose: r }) {
  const [s, a] = B(""), [c, u] = B(0), l = re(null), d = re(null), f = fe(() => {
    const b = s.trim().toLowerCase(), y = n.filter(z0);
    return b ? y.filter((v) => Ce(v).toLowerCase().includes(b) || v.activityTypeKey.toLowerCase().includes(b) || (v.category ?? "").toLowerCase().includes(b) || (v.description ?? "").toLowerCase().includes(b)) : y;
  }, [n, s]), p = fe(() => Pi(f), [f]), h = fe(() => p.flatMap((b) => b.activities), [p]);
  J(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), J(() => {
    const b = (v) => {
      l.current?.contains(v.target) || r();
    }, y = (v) => {
      v.key === "Escape" && r();
    };
    return document.addEventListener("mousedown", b, !0), document.addEventListener("keydown", y), () => {
      document.removeEventListener("mousedown", b, !0), document.removeEventListener("keydown", y);
    };
  }, [r]);
  const g = (b) => {
    if (b.key === "ArrowDown")
      b.preventDefault(), u((y) => Math.min(y + 1, h.length - 1));
    else if (b.key === "ArrowUp")
      b.preventDefault(), u((y) => Math.max(y - 1, 0));
    else if (b.key === "Enter") {
      b.preventDefault();
      const y = h[c];
      y && o(y);
    }
  }, m = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), w = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let x = -1;
  return /* @__PURE__ */ i.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: m, top: w }, onMouseDown: (b) => b.stopPropagation(), onClick: (b) => b.stopPropagation(), children: [
    /* @__PURE__ */ i.jsx(
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
        onKeyDown: g
      }
    ),
    /* @__PURE__ */ i.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: p.length === 0 ? /* @__PURE__ */ i.jsx("p", { children: "No matching activities." }) : p.map((b) => /* @__PURE__ */ i.jsxs("section", { children: [
      /* @__PURE__ */ i.jsx("h4", { children: b.category }),
      b.activities.map((y) => {
        x += 1;
        const v = x, j = v === c;
        return /* @__PURE__ */ i.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": j,
            className: j ? "active" : "",
            onMouseEnter: () => u(v),
            onClick: () => o(y),
            children: [
              /* @__PURE__ */ i.jsx("strong", { children: Ce(y) }),
              /* @__PURE__ */ i.jsx("small", { children: y.category || y.activityTypeKey })
            ]
          },
          y.activityVersionId
        );
      })
    ] }, b.category)) })
  ] });
}
function ko({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: o
}) {
  const r = Ud(t.map((s) => s.id), n, o);
  return /* @__PURE__ */ i.jsx("div", { className: "wf-panel-tab-list", role: "tablist", "aria-label": e, onKeyDown: r, children: t.map((s) => {
    const a = s.id === n;
    return /* @__PURE__ */ i.jsxs(
      "button",
      {
        "data-tab-id": s.id,
        type: "button",
        role: "tab",
        "aria-selected": a,
        tabIndex: a ? 0 : -1,
        className: a ? "active" : "",
        title: s.title,
        onClick: () => o(s.id),
        children: [
          s.icon ? /* @__PURE__ */ i.jsx("span", { className: "wf-panel-tab-icon", "aria-hidden": "true", children: s.icon }) : null,
          /* @__PURE__ */ i.jsx("span", { children: s.title })
        ]
      },
      s.id
    );
  }) });
}
function gc(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
const ab = "Expressions/UnresolvedVariable";
function cb(e) {
  return String(e.type ?? e.code ?? "");
}
function lb(e) {
  return cb(e) === ab;
}
function ub(e) {
  const t = String(e ?? "").trim().split("/").filter(Boolean), [n, o, ...r] = t, s = n === "$workflow";
  return {
    nodeId: !n || s ? null : n,
    isWorkflowScope: s,
    bag: o === "inputs" || o === "outputs" || o === "variables" ? o : null,
    referenceKey: r.length > 0 ? r.join("/") : null
  };
}
function db(e) {
  return (e ?? []).filter(lb).map((t) => ({
    error: t,
    path: ub(t.path),
    message: t.message ?? "Variable reference is not visible from this activity's scope."
  }));
}
function fb({ draft: e, onRepair: t }) {
  const n = e.validationErrors;
  if (!n.length)
    return /* @__PURE__ */ i.jsxs("div", { className: "wf-validation ok", children: [
      /* @__PURE__ */ i.jsx(Qt, { size: 14 }),
      " No validation errors"
    ] });
  const o = db(n), r = new Map(o.map((s) => [s.error, s]));
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-validation-summary", children: [
      /* @__PURE__ */ i.jsx(gt, { size: 14 }),
      n.length,
      " validation issue",
      n.length === 1 ? "" : "s",
      o.length > 0 ? /* @__PURE__ */ i.jsxs("span", { className: "wf-validation-variable-count", children: [
        " · ",
        o.length,
        " invalid variable reference",
        o.length === 1 ? "" : "s"
      ] }) : null
    ] }),
    /* @__PURE__ */ i.jsx("ul", { className: "wf-validation-list", children: n.map((s, a) => {
      const c = r.get(s);
      return /* @__PURE__ */ i.jsxs("li", { className: c ? "wf-validation-item repairable" : "wf-validation-item", children: [
        /* @__PURE__ */ i.jsx("span", { className: "wf-validation-message", children: s.message ?? "Validation issue." }),
        c?.path.nodeId ? /* @__PURE__ */ i.jsxs("button", { type: "button", className: "wf-validation-repair", onClick: () => t(c.path.nodeId), children: [
          /* @__PURE__ */ i.jsx(Vd, { size: 12 }),
          " Repair"
        ] }) : null
      ] }, a);
    }) })
  ] });
}
function pb({
  testRun: e,
  onOpenDetails: t
}) {
  const n = ys(e);
  return /* @__PURE__ */ i.jsx("div", { className: "wf-test-run-status", "data-state": n ? "rejected" : "accepted", children: /* @__PURE__ */ i.jsxs(
    "button",
    {
      type: "button",
      className: "wf-test-run-trigger",
      onClick: t,
      children: [
        n ? /* @__PURE__ */ i.jsx(gt, { size: 16 }) : /* @__PURE__ */ i.jsx(Qt, { size: 16 }),
        n ? "Test run rejected" : "Test run dispatched"
      ]
    }
  ) });
}
function hb({ testRun: e, onOpenRun: t }) {
  if (!e)
    return /* @__PURE__ */ i.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "Run the draft to see Runtime Evidence." }) });
  const n = ys(e), o = e.workflowExecutionId;
  return /* @__PURE__ */ i.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ i.jsxs("section", { className: "wf-runtime-card", "data-state": n ? "rejected" : "accepted", children: [
    /* @__PURE__ */ i.jsxs("header", { children: [
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("span", { children: "Latest Test Run" }),
        /* @__PURE__ */ i.jsx("h3", { children: n ? "Rejected by the server" : "Transient run accepted" })
      ] }),
      /* @__PURE__ */ i.jsx(nn, { status: e.status, subStatus: e.commandDispatchStatus ?? void 0 })
    ] }),
    /* @__PURE__ */ i.jsx("p", { children: "Ephemeral - not saved, promoted, or published." }),
    n && e.reason ? /* @__PURE__ */ i.jsxs("div", { className: "wf-runtime-reason", children: [
      /* @__PURE__ */ i.jsx(gt, { size: 14 }),
      " ",
      e.reason
    ] }) : null,
    /* @__PURE__ */ i.jsxs("dl", { className: "wf-runtime-meta", children: [
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Dispatch" }),
        /* @__PURE__ */ i.jsx("dd", { title: e.commandDispatchStatus ?? e.status, children: e.commandDispatchStatus ?? e.status })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Test Run" }),
        /* @__PURE__ */ i.jsx("dd", { title: e.testRunId, children: e.testRunId })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Artifact" }),
        /* @__PURE__ */ i.jsx("dd", { title: e.artifactId ?? "None", children: e.artifactId ?? "None" })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Run / Instance" }),
        /* @__PURE__ */ i.jsx("dd", { title: o ?? "None", children: o ? /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => t(o), children: o }) : "None" })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Activities" }),
        /* @__PURE__ */ i.jsx("dd", { children: yc(e.activityCount, "activity") })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ i.jsx("dd", { children: yc(e.incidentCount, "incident") })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ i.jsx("dd", { title: e.expiresAt ? ze(e.expiresAt) : "None", children: e.expiresAt ? ze(e.expiresAt) : "None" })
      ] })
    ] })
  ] }) });
}
function yc(e, t) {
  return typeof e != "number" ? "Available on linked Run" : `${e} ${t}${e === 1 ? "" : "s"}`;
}
function ms({ rows: e = 5 }) {
  return /* @__PURE__ */ i.jsx("div", { className: "wf-grid", "aria-busy": "true", "aria-label": "Loading", children: Array.from({ length: e }).map((t, n) => /* @__PURE__ */ i.jsx("div", { className: "wf-skeleton wf-skeleton-row", style: { width: `${90 - n % 3 * 12}%` } }, n)) });
}
function xs({ icon: e, title: t, description: n, action: o }) {
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-empty-state", role: "status", children: [
    /* @__PURE__ */ i.jsx("div", { className: "wf-empty-state-icon", "aria-hidden": !0, children: e ?? /* @__PURE__ */ i.jsx(Zo, { size: 22 }) }),
    /* @__PURE__ */ i.jsx("h3", { children: t }),
    n ? /* @__PURE__ */ i.jsx("p", { children: n }) : null,
    o ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty-state-action", children: o }) : null
  ] });
}
function Fn({ message: e, title: t = "Something went wrong" }) {
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-error-card", role: "alert", children: [
    /* @__PURE__ */ i.jsx(gt, { size: 18 }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-error-card-body", children: [
      /* @__PURE__ */ i.jsx("strong", { children: t }),
      /* @__PURE__ */ i.jsx("span", { children: e || "Please try again, or check that the Elsa server is reachable." })
    ] })
  ] });
}
function wd({ status: e, run: t, compact: n = !1 }) {
  const o = () => {
    t?.workflowExecutionId && (window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(t.workflowExecutionId)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  };
  return /* @__PURE__ */ i.jsxs("div", { className: `wf-status-line${n ? " compact" : ""}`, children: [
    /* @__PURE__ */ i.jsx(Qt, { size: n ? 13 : 14 }),
    /* @__PURE__ */ i.jsx("span", { children: e }),
    t?.workflowExecutionId ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: o, children: [
      "Open Run ",
      t.workflowExecutionId
    ] }) : null
  ] });
}
function Ot({ value: e, ariaLabel: t, copiedLabel: n, onCopied: o, onCopyFailed: r }) {
  if (!e) return null;
  const s = async (a) => {
    a.preventDefault(), a.stopPropagation();
    try {
      await W0(e), o(n);
    } catch {
      r(n);
    }
  };
  return /* @__PURE__ */ i.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (a) => {
    s(a);
  }, children: /* @__PURE__ */ i.jsx(Od, { size: 12 }) });
}
function gb({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: o }) {
  const [r, s] = B("loading"), [a, c] = B(""), [u, l] = B(""), [d, f] = B(null), [p, h] = B([]), g = n?.trim().toLowerCase() ?? "", m = fe(
    () => g ? p.filter((S) => V0(S, g)) : p,
    [g, p]
  ), w = fe(
    () => Array.from(new Set(p.flatMap((S) => [
      S.definitionId,
      S.definitionVersionId,
      S.sourceId
    ]).filter((S) => !!S))).sort((S, E) => S.localeCompare(E)),
    [p]
  ), x = Et(t, "weaver.workflows.explain-executable"), b = ie(async () => {
    s("loading"), c("");
    try {
      h(await cl(e)), s("ready");
    } catch (S) {
      c(S instanceof Error ? S.message : String(S)), s("failed");
    }
  }, [e]);
  J(() => {
    b();
  }, [b]);
  const y = async (S) => {
    l(""), f(null), c("");
    try {
      const E = await al(e, S.artifactId), D = dd(E);
      f({ artifactId: S.artifactId, workflowExecutionId: D }), l(`Started ${S.artifactId}`);
    } catch (E) {
      c(E instanceof Error ? E.message : String(E));
    }
  }, v = (S) => {
    x && It(t, x, S) && (c(""), f(null), l(`Sent ${S.artifactId} to Weaver`));
  }, j = (S) => {
    c(""), f(null), l(`Copied ${S}`);
  }, N = (S) => {
    l(""), f(null), c(`Could not copy ${S}.`);
  };
  return /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => {
        b();
      }, children: "Refresh" }),
      /* @__PURE__ */ i.jsxs("label", { className: "wf-search wf-executable-definition-filter", children: [
        /* @__PURE__ */ i.jsx(Uo, { size: 14 }),
        /* @__PURE__ */ i.jsx(
          "input",
          {
            "aria-label": "Filter executables by workflow definition",
            list: "wf-executable-definition-options",
            placeholder: "Filter by definition ID",
            value: n ?? "",
            onChange: (S) => o(S.currentTarget.value || null)
          }
        )
      ] }),
      /* @__PURE__ */ i.jsx("datalist", { id: "wf-executable-definition-options", children: w.map((S) => /* @__PURE__ */ i.jsx("option", { value: S }, S)) }),
      n ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => o(null), children: [
        /* @__PURE__ */ i.jsx($c, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    r === "failed" ? /* @__PURE__ */ i.jsx(Fn, { message: a }) : null,
    u ? /* @__PURE__ */ i.jsx(wd, { status: u, run: d }) : null,
    r === "loading" ? /* @__PURE__ */ i.jsx(ms, {}) : null,
    r === "ready" && m.length === 0 ? /* @__PURE__ */ i.jsx(
      xs,
      {
        icon: /* @__PURE__ */ i.jsx(Ht, { size: 22 }),
        title: "No workflow executables",
        description: n ? "No executables match this definition filter." : "Publish a workflow definition to make it executable."
      }
    ) : null,
    r === "ready" && m.length > 0 ? /* @__PURE__ */ i.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ i.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ i.jsx("span", { children: "Version" }),
        /* @__PURE__ */ i.jsx("span", { children: "Source" }),
        /* @__PURE__ */ i.jsx("span", { children: "Root" }),
        /* @__PURE__ */ i.jsx("span", { children: "Published" }),
        /* @__PURE__ */ i.jsx("span", { children: "Actions" })
      ] }),
      m.map((S) => /* @__PURE__ */ i.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ i.jsxs("span", { className: "wf-artifact-cell", children: [
          /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line", children: [
            /* @__PURE__ */ i.jsx("strong", { title: S.artifactId, children: S.artifactId }),
            /* @__PURE__ */ i.jsx(Ot, { value: S.artifactId, ariaLabel: `Copy artifact ID ${S.artifactId}`, copiedLabel: "artifact ID", onCopied: j, onCopyFailed: N })
          ] }),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
            /* @__PURE__ */ i.jsx("small", { title: S.artifactHash, children: S.artifactHash }),
            /* @__PURE__ */ i.jsx(Ot, { value: S.artifactHash, ariaLabel: `Copy artifact hash ${S.artifactHash}`, copiedLabel: "artifact hash", onCopied: j, onCopyFailed: N })
          ] })
        ] }),
        /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line wf-version-cell", children: [
          /* @__PURE__ */ i.jsx("span", { children: S.artifactVersion }),
          /* @__PURE__ */ i.jsx(Ot, { value: S.artifactVersion, ariaLabel: `Copy artifact version ${S.artifactVersion}`, copiedLabel: "artifact version", onCopied: j, onCopyFailed: N })
        ] }),
        /* @__PURE__ */ i.jsx(yb, { executable: S, onCopied: j, onCopyFailed: N }),
        /* @__PURE__ */ i.jsx("span", { children: ld(S) }),
        /* @__PURE__ */ i.jsx("span", { children: ze(S.publishedAt ?? S.createdAt) }),
        /* @__PURE__ */ i.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => {
            y(S);
          }, children: [
            /* @__PURE__ */ i.jsx(Ht, { size: 13 }),
            " Run"
          ] }),
          x ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => v(S), children: [
            /* @__PURE__ */ i.jsx(it, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, S.artifactId))
    ] }) : null
  ] });
}
function yb({ executable: e, onCopied: t, onCopyFailed: n }) {
  const o = e.sourceId || e.definitionVersionId || e.definitionId, r = e.sourceVersion;
  return /* @__PURE__ */ i.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ i.jsx("span", { className: "wf-source-kind", children: ud(e.sourceKind) }),
    o ? /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line", children: [
      /* @__PURE__ */ i.jsx("code", { title: o, children: o }),
      /* @__PURE__ */ i.jsx(Ot, { value: o, ariaLabel: `Copy source ID ${o}`, copiedLabel: "source ID", onCopied: t, onCopyFailed: n })
    ] }) : null,
    r ? /* @__PURE__ */ i.jsxs("small", { children: [
      "Version ",
      r
    ] }) : null
  ] });
}
function mb({ context: e, ai: t, definitionId: n, publishedArtifactId: o }) {
  const [r, s] = B("loading"), [a, c] = B(""), [u, l] = B(""), [d, f] = B(null), [p, h] = B([]), g = Et(t, "weaver.workflows.explain-executable"), m = ie(async () => {
    s("loading"), c("");
    try {
      const j = await cl(e);
      h(j.filter((N) => O0(N, n)).sort(H0)), s("ready");
    } catch (j) {
      c(j instanceof Error ? j.message : String(j)), h([]), s("failed");
    }
  }, [e, n]);
  J(() => {
    m();
  }, [m, o]);
  const w = async (j) => {
    l(""), f(null), c("");
    try {
      const N = await al(e, j.artifactId);
      f({ artifactId: j.artifactId, workflowExecutionId: dd(N) }), l(`Started ${j.artifactId}`);
    } catch (N) {
      c(N instanceof Error ? N.message : String(N));
    }
  }, x = (j) => {
    g && It(t, g, j) && (c(""), f(null), l(`Sent ${j.artifactId} to Weaver`));
  }, b = () => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(n)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, y = (j) => {
    c(""), f(null), l(`Copied ${j}`);
  }, v = (j) => {
    l(""), f(null), c(`Could not copy ${j}.`);
  };
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-artifacts-panel", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-artifacts-toolbar", children: [
      /* @__PURE__ */ i.jsxs("span", { children: [
        p.length,
        " artifact",
        p.length === 1 ? "" : "s"
      ] }),
      /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => {
        m();
      }, children: [
        /* @__PURE__ */ i.jsx(Fi, { size: 13 }),
        " Refresh"
      ] }),
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: b, children: "Open list" })
    ] }),
    r === "failed" ? /* @__PURE__ */ i.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ i.jsx(gt, { size: 14 }),
      " ",
      a
    ] }) : null,
    u ? /* @__PURE__ */ i.jsx(wd, { status: u, run: d, compact: !0 }) : null,
    r === "loading" ? /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "Loading artifacts..." }) : null,
    r === "ready" && p.length === 0 ? /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "No published artifacts for this workflow yet." }) : null,
    r === "ready" && p.length > 0 ? /* @__PURE__ */ i.jsx("div", { className: "wf-artifact-list", role: "list", "aria-label": "Workflow artifacts", children: p.map((j) => /* @__PURE__ */ i.jsxs("article", { className: "wf-artifact-card", role: "listitem", "data-active": j.artifactId === o ? "true" : void 0, children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-artifact-card-heading", children: [
        /* @__PURE__ */ i.jsxs("div", { children: [
          /* @__PURE__ */ i.jsxs("span", { className: "wf-artifact-version", children: [
            "Version ",
            j.artifactVersion
          ] }),
          j.artifactId === o ? /* @__PURE__ */ i.jsx("span", { className: "wf-chip", children: "Latest publish" }) : null
        ] }),
        /* @__PURE__ */ i.jsx("span", { children: ze(j.publishedAt ?? j.createdAt) })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { className: "wf-artifact-card-values", children: [
        /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line", children: [
          /* @__PURE__ */ i.jsx("code", { title: j.artifactId, children: j.artifactId }),
          /* @__PURE__ */ i.jsx(Ot, { value: j.artifactId, ariaLabel: `Copy artifact ID ${j.artifactId}`, copiedLabel: "artifact ID", onCopied: y, onCopyFailed: v })
        ] }),
        /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ i.jsx("code", { title: j.artifactHash, children: j.artifactHash }),
          /* @__PURE__ */ i.jsx(Ot, { value: j.artifactHash, ariaLabel: `Copy artifact hash ${j.artifactHash}`, copiedLabel: "artifact hash", onCopied: y, onCopyFailed: v })
        ] })
      ] }),
      /* @__PURE__ */ i.jsxs("dl", { children: [
        /* @__PURE__ */ i.jsxs("div", { children: [
          /* @__PURE__ */ i.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ i.jsxs("dd", { children: [
            ud(j.sourceKind),
            " ",
            j.sourceVersion ? `v${j.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ i.jsxs("div", { children: [
          /* @__PURE__ */ i.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ i.jsx("dd", { children: ld(j) })
        ] })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => {
          w(j);
        }, children: [
          /* @__PURE__ */ i.jsx(Ht, { size: 13 }),
          " Run"
        ] }),
        g ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => x(j), children: [
          /* @__PURE__ */ i.jsx(it, { size: 13 }),
          " Explain"
        ] }) : null
      ] })
    ] }, j.artifactId)) }) : null
  ] });
}
function xb() {
  const [e, t] = B(() => uc(nc, A0, pn, hn)), [n, o] = B(() => uc(oc, _0, gn, yn)), [r, s] = B(() => dc(rc, !1)), [a, c] = B(() => dc(ic, !1)), [u, l] = B(X0);
  J(() => {
    ln(nc, String(e));
  }, [e]), J(() => {
    ln(oc, String(n));
  }, [n]), J(() => {
    ln(rc, String(r));
  }, [r]), J(() => {
    ln(ic, String(a));
  }, [a]), J(() => {
    ln(id, u);
  }, [u]), J(() => {
    if (!u) return;
    const y = (v) => {
      v.key === "Escape" && l(null);
    };
    return window.addEventListener("keydown", y), () => window.removeEventListener("keydown", y);
  }, [u]);
  const d = ie((y) => {
    l((v) => v === y ? null : v), y === "palette" ? s((v) => !v) : c((v) => !v);
  }, []), f = ie((y) => {
    y === "palette" ? s(!1) : c(!1), l((v) => v === y ? null : y);
  }, []), p = ie((y, v) => {
    l(null), y === "palette" ? (s(!1), t((j) => Io(j + v, pn, hn))) : (c(!1), o((j) => Io(j + v, gn, yn)));
  }, []), h = ie((y, v) => {
    v.preventDefault(), l(null), y === "palette" ? s(!1) : c(!1);
    const j = v.clientX, N = y === "palette" ? e : n, S = y === "palette" ? pn : gn, E = y === "palette" ? hn : yn;
    document.body.classList.add("wf-side-panel-resizing");
    const D = (A) => {
      const _ = y === "palette" ? A.clientX - j : j - A.clientX, R = Io(N + _, S, E);
      y === "palette" ? t(R) : o(R);
    }, L = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", D), window.removeEventListener("pointerup", L), window.removeEventListener("pointercancel", L);
    };
    window.addEventListener("pointermove", D), window.addEventListener("pointerup", L), window.addEventListener("pointercancel", L);
  }, [n, e]), g = ie((y, v) => {
    v.key === "ArrowLeft" ? (v.preventDefault(), p(y, y === "palette" ? -yo : yo)) : v.key === "ArrowRight" ? (v.preventDefault(), p(y, y === "palette" ? yo : -yo)) : v.key === "Home" ? (v.preventDefault(), y === "palette" ? t(pn) : o(gn)) : v.key === "End" && (v.preventDefault(), y === "palette" ? t(hn) : o(yn));
  }, [p]), m = !r && u !== "inspector", w = !a && u !== "palette", x = [
    "wf-editor-body",
    r ? "palette-collapsed" : "",
    a ? "inspector-collapsed" : "",
    u === "palette" ? "palette-maximized" : "",
    u === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), b = {
    "--wf-palette-width": `${r ? sc : e}px`,
    "--wf-inspector-width": `${a ? sc : n}px`
  };
  return {
    paletteWidth: e,
    inspectorWidth: n,
    paletteCollapsed: r,
    inspectorCollapsed: a,
    maximizedSidePanel: u,
    setInspectorCollapsed: c,
    paletteExpanded: m,
    inspectorExpanded: w,
    editorBodyClassName: x,
    editorBodyStyle: b,
    toggleSidePanelCollapsed: d,
    toggleSidePanelMaximized: f,
    startSidePanelResize: h,
    handleSidePanelResizeKeyDown: g
  };
}
const wb = 50;
function mc() {
  return { past: [], future: [] };
}
function vb(e) {
  return e.past.length > 0;
}
function bb(e) {
  return e.future.length > 0;
}
function xc(e, t, n = wb) {
  const o = [...e.past, t];
  return o.length > n && o.splice(0, o.length - n), { past: o, future: [] };
}
function Nb(e, t) {
  if (e.past.length === 0) return null;
  const n = e.past.slice(), o = n.pop();
  return { history: { past: n, future: [...e.future, t] }, snapshot: o };
}
function jb(e, t) {
  if (e.future.length === 0) return null;
  const n = e.future.slice(), o = n.pop();
  return { history: { past: [...e.past, t], future: n }, snapshot: o };
}
function Sb({ draft: e, restoreDraft: t }) {
  const n = re(mc()), o = re(null), r = re(""), s = re(!1), [a, c] = B(0), u = ie((m) => {
    n.current = mc(), o.current = m ? xn(m) : null, r.current = m ? Le(m) : "", s.current = !1, c(0);
  }, []);
  J(() => {
    if (!e) return;
    if (s.current) {
      s.current = !1;
      return;
    }
    const m = Le(e);
    if (m === r.current) return;
    const w = window.setTimeout(() => {
      const x = o.current;
      x && (n.current = xc(n.current, x), c((b) => b + 1)), o.current = xn(e), r.current = m;
    }, E0);
    return () => window.clearTimeout(w);
  }, [e]);
  const l = ie(() => {
    if (!e) return;
    const m = Le(e);
    if (m === r.current) return;
    const w = o.current;
    w && (n.current = xc(n.current, w)), o.current = xn(e), r.current = m;
  }, [e]), d = ie((m) => {
    s.current = !0, o.current = xn(m), r.current = Le(m), t(m), c((w) => w + 1);
  }, [t]), f = ie(() => {
    if (!e) return;
    l();
    const m = Nb(n.current, e);
    m && (n.current = m.history, d(m.snapshot));
  }, [e, l, d]), p = ie(() => {
    if (!e) return;
    l();
    const m = jb(n.current, e);
    m && (n.current = m.history, d(m.snapshot));
  }, [e, l, d]), { canUndoNow: h, canRedoNow: g } = fe(() => {
    const m = !!e && !!o.current && Le(e) !== r.current;
    return {
      canUndoNow: vb(n.current) || m,
      canRedoNow: bb(n.current) && !m
    };
  }, [e, a]);
  return { resetHistory: u, undo: f, redo: p, canUndoNow: h, canRedoNow: g };
}
const Cb = {
  draft: null,
  frames: [],
  selectedNodeId: null,
  testRun: null,
  publishedArtifactId: null
};
function Eb(e, t) {
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
function Ib() {
  const [e, t] = Md(Eb, Cb), n = fe(() => ({
    // Load a freshly fetched / applied draft (or clear it). Used by load(), JSON apply, and undo/redo.
    loadDraft(o) {
      t({ type: "draftLoaded", draft: o });
    },
    // Replace the draft from a Weaver batch apply/undo, invalidating scope, selection, run and artifact.
    replaceDraftByBatch(o, r) {
      t({ type: "draftReplacedByBatch", draft: o, selectedNodeId: r });
    },
    // In-place draft edit that keeps scope + selection.
    editDraft(o) {
      t({ type: "draftEdited", recipe: o });
    },
    // Draft edit that also selects a node (add/wrap root, add scoped activity).
    editDraftAndSelect(o, r) {
      t({ type: "draftEditedAndSelected", recipe: o, selectedNodeId: r });
    },
    select(o) {
      t({ type: "selectionChanged", selectedNodeId: o });
    },
    // Jump to an explicit breadcrumb path (breadcrumb clicks, variable-repair navigation).
    navigateToScope(o, r = null) {
      t({ type: "scopeNavigated", frames: o, selectedNodeId: r });
    },
    resetToRoot() {
      t({ type: "scopeNavigated", frames: [], selectedNodeId: null });
    },
    enterSlot(o, r, s) {
      t({ type: "slotEntered", frame: { ownerNodeId: o.nodeId, slotId: r, label: s } });
    },
    startTestRun(o) {
      t({ type: "testRunStarted", testRun: o });
    },
    clearTestRun() {
      t({ type: "testRunCleared" });
    },
    setPublishedArtifact(o) {
      t({ type: "publishedArtifactChanged", publishedArtifactId: o });
    }
  }), []);
  return { state: e, ...n };
}
const kb = 320, Ab = 140;
function _b(e, t, n) {
  return n === "sequence" ? Db(e) : Tb(e, t);
}
function Db(e) {
  const t = /* @__PURE__ */ new Map();
  return e.forEach((n, o) => {
    t.set(n.id, { x: o * 280, y: 0 });
  }), t;
}
function Tb(e, t) {
  const n = /* @__PURE__ */ new Map();
  if (e.length === 0) return n;
  const o = new Set(e.map((d) => d.id)), r = t.filter((d) => o.has(d.source) && o.has(d.target)), s = /* @__PURE__ */ new Set();
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
      n.set(p, { x: d * kb, y: h * Ab });
    });
  return n;
}
function Pb({
  draft: e,
  scope: t,
  scopeOwner: n,
  catalog: o,
  catalogByVersion: r,
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
  const [g, m] = B([]), [w, x] = B([]), [b, y] = B(null), [v, j] = B(null), [N, S] = B(null), E = re(null), D = re(null), L = re(null), A = re(null), _ = re(!1);
  J(() => {
    if (!n) {
      m([]), x([]);
      return;
    }
    const z = s ? mi(n, o, e?.layout ?? []) : t ? Zc(t, o, e?.layout ?? []) : { nodes: [], edges: [] };
    m(z.nodes), x(z.edges);
  }, [o, e?.layout, s, t, n]);
  const R = ie((z, H) => {
    if (e?.state.rootActivity && s)
      return;
    const K = wi(z, pc(z));
    if (!e?.state.rootActivity) {
      d(
        ({ draft: X }) => X ? { ...X, state: { ...X.state, rootActivity: K } } : null,
        K.nodeId
      );
      return;
    }
    if (!t) {
      if (!De(K, z)[0]) {
        p(""), h("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      d(({ draft: se }) => {
        if (!se?.state.rootActivity) return null;
        const pe = se.state.rootActivity, ye = xi(K, [], [pe], z), je = H ? [
          ...se.layout.filter((Ee) => Ee.nodeId !== pe.nodeId),
          {
            nodeId: pe.nodeId,
            x: Math.round(H.x),
            y: Math.round(H.y)
          }
        ] : se.layout;
        return {
          ...se,
          layout: je,
          state: {
            ...se.state,
            rootActivity: ye
          }
        };
      }, e.state.rootActivity.nodeId), h(""), p(`Wrapped root in ${Ce(z)}`);
      return;
    }
    d(({ draft: X, frames: se }) => {
      if (!X?.state.rootActivity) return null;
      const pe = Sn(X.state.rootActivity, se, r);
      if (!pe) return null;
      const ye = pe.slot.cardinality === "single" ? [K] : [...pe.slot.activities, K], je = xi(X.state.rootActivity, se, ye, r), Ee = H ? [
        ...X.layout.filter((Me) => Me.nodeId !== K.nodeId),
        {
          nodeId: K.nodeId,
          x: Math.round(H.x),
          y: Math.round(H.y)
        }
      ] : X.layout;
      return {
        ...X,
        layout: Ee,
        state: {
          ...X.state,
          rootActivity: je
        }
      };
    }, K.nodeId);
  }, [r, e?.state.rootActivity, s, t, d, h, p]), C = ie((z, H) => {
    const K = wi(z, pc(z)), X = {
      id: K.nodeId,
      type: "workflowActivity",
      position: H,
      selected: !0,
      data: {
        label: Ce(z),
        activityVersionId: z.activityVersionId,
        activityTypeKey: z.activityTypeKey,
        category: z.category,
        executionType: z.executionType,
        icon: Qo(z),
        childSlots: De(K, z),
        acceptsInbound: String(z.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: ol(K, z)
      }
    };
    return { activityNode: K, node: X };
  }, []), I = ie((z, H, K = []) => {
    s || l(({ draft: X, frames: se }) => {
      if (!X) return null;
      const pe = zf(X.layout, z), ye = X.state.rootActivity;
      if (!ye) return { ...X, layout: pe };
      const je = Sn(ye, se, r);
      if (!je) return { ...X, layout: pe };
      const Ee = Rf(je, z, H, K), Me = je.slot.mode === "flowchart" ? Lf(Ee, H) : Ee;
      return {
        ...X,
        layout: pe,
        state: {
          ...X.state,
          rootActivity: Gc(ye, se, Me, r)
        }
      };
    });
  }, [r, s, l]), k = ie((z, H) => {
    if (!E.current) return null;
    const K = E.current.getBoundingClientRect();
    return b ? b.screenToFlowPosition({ x: z, y: H }) : {
      x: z - K.left,
      y: H - K.top
    };
  }, [b]), T = ie((z, H) => document.elementFromPoint(z, H)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), $ = ie((z, H, K) => {
    const X = g.find((ke) => ke.id === H.source), se = g.find((ke) => ke.id === H.target), pe = X && se ? U0(X, se) : X ? hc(X) : K, ye = C(z, pe), Ee = [...g.map((ke) => ke.selected ? { ...ke, selected: !1 } : ke), ye.node], Me = Xf(w, H, ye.node.id);
    m(Ee), x(Me), f(ye.node.id), I(Ee, Me, [ye.activityNode]);
  }, [I, C, w, g, f]), P = ie((z, H, K) => {
    if (!c || !E.current) return !1;
    const X = E.current.getBoundingClientRect();
    if (!(H >= X.left && H <= X.right && K >= X.top && K <= X.bottom)) return !1;
    const pe = k(H, K);
    if (!pe) return !1;
    if (a) {
      const ye = T(H, K), je = ye ? w.find((Ee) => Ee.id === ye) : void 0;
      if (je)
        return $(z, je, pe), !0;
    }
    return R(z, pe), !0;
  }, [R, c, w, T, a, $, k]);
  J(() => {
    const z = (K) => {
      const X = L.current;
      if (!X) return;
      Math.hypot(K.clientX - X.startX, K.clientY - X.startY) >= S0 && (X.dragging = !0);
    }, H = (K) => {
      const X = L.current;
      if (L.current = null, !X?.dragging || !E.current || A.current) return;
      const se = E.current.getBoundingClientRect();
      K.clientX >= se.left && K.clientX <= se.right && K.clientY >= se.top && K.clientY <= se.bottom && (_.current = !0, window.setTimeout(() => {
        _.current = !1;
      }, 0), P(X.activity, K.clientX, K.clientY));
    };
    return window.addEventListener("pointermove", z), window.addEventListener("pointerup", H), window.addEventListener("pointercancel", H), () => {
      window.removeEventListener("pointermove", z), window.removeEventListener("pointerup", H), window.removeEventListener("pointercancel", H);
    };
  }, [b, P]);
  const F = (z, H) => {
    A.current = { activityVersionId: H.activityVersionId, handledDrop: !1 }, z.dataTransfer.setData(tc, H.activityVersionId), z.dataTransfer.setData("text/plain", H.activityVersionId), z.dataTransfer.effectAllowed = "copy";
  }, W = (z, H) => {
    const K = A.current;
    A.current = null, !K?.handledDrop && (z.clientX === 0 && z.clientY === 0 || P(H, z.clientX, z.clientY) && (_.current = !0, window.setTimeout(() => {
      _.current = !1;
    }, 0)));
  }, O = (z, H) => {
    z.button === 0 && (L.current = {
      activity: H,
      startX: z.clientX,
      startY: z.clientY,
      dragging: !1
    });
  }, Z = (z) => {
    _.current || c && R(z);
  }, U = (z) => {
    if (!c) {
      z.dataTransfer.dropEffect = "none";
      return;
    }
    if (z.preventDefault(), z.dataTransfer.dropEffect = "copy", !a) return;
    const H = T(z.clientX, z.clientY);
    S(H);
  }, te = (z) => {
    if (!E.current) return;
    const H = z.relatedTarget;
    H && E.current.contains(H) || S(null);
  }, le = (z) => {
    z.preventDefault(), S(null);
    const H = z.dataTransfer.getData(tc) || z.dataTransfer.getData("text/plain");
    if (!H || (z.stopPropagation(), A.current?.activityVersionId === H && (A.current.handledDrop = !0), !c)) return;
    const K = r.get(H);
    K && P(K, z.clientX, z.clientY);
  }, G = () => {
    if (!a) return;
    const z = E.current?.getBoundingClientRect();
    z && j({
      kind: "fromEmpty",
      clientX: z.left + z.width / 2,
      clientY: z.top + z.height / 2
    });
  }, M = (z) => {
    const H = s ? z.filter((K) => K.type === "select") : z;
    H.length !== 0 && m((K) => Nu(H, K));
  }, q = (z) => {
    s || x((H) => ju(z, H));
  }, ae = (z) => !z.source || !z.target || z.source === z.target || !a ? !1 : !z.targetHandle, ce = (z) => {
    if (!e?.state.rootActivity || !t || !a || !ae(z)) return;
    const H = Po(z.source, z.target, z.sourceHandle ?? "Done", z.targetHandle ?? void 0), K = Cu(H, w);
    x(K), I(g, K);
  }, Q = () => {
    I(g, w);
  }, oe = !s && g.length > 0, de = ie(() => {
    if (s || g.length === 0) return;
    const z = t?.slot.mode === "sequence" ? "sequence" : "flowchart", H = _b(g, w, z), K = g.map((X) => {
      const se = H.get(X.id);
      return se ? { ...X, position: se } : X;
    });
    m(K), I(K, w), window.requestAnimationFrame(() => b?.fitView({ padding: 0.2 })), p("Rearranged the canvas.");
  }, [w, g, t, s, I, b, p]), V = (z, H) => {
    if (!H.nodeId || H.handleType === "target") {
      D.current = null;
      return;
    }
    D.current = {
      nodeId: H.nodeId,
      handleId: H.handleId ?? null
    };
  }, ee = (z, H) => {
    const K = G0(D.current, H);
    if (D.current = null, !K || !a || H.toNode || H.toHandle || Z0(z)) return;
    const X = pd(z);
    j({
      kind: "fromPort",
      sourceNodeId: K.nodeId,
      sourceHandleId: K.handleId,
      clientX: X.x,
      clientY: X.y
    });
  }, ge = (z, H) => {
    if (!a || !ae(H)) return;
    const K = iw(z, {
      ...H,
      sourceHandle: H.sourceHandle ?? "Done",
      // Connection.targetHandle is string | null; null and undefined are indistinguishable downstream
      // (every consumer checks truthiness), so keep the type-correct null.
      targetHandle: H.targetHandle ?? null
    }, w, { shouldReplaceId: !1 });
    x(K), I(g, K);
  }, we = (z) => {
    if (s || z.length === 0) return;
    const H = new Set(z.map((se) => se.id)), K = g.filter((se) => !H.has(se.id)), X = w.filter((se) => !H.has(se.source) && !H.has(se.target));
    m(K), x(X), u && H.has(u) && f(null), I(K, X);
  }, Ae = (z) => {
    if (s || z.length === 0) return;
    const H = new Set(z.map((X) => X.id)), K = w.filter((X) => !H.has(X.id));
    x(K), I(g, K);
  }, Ie = ie((z) => {
    if (s) return;
    const H = w.filter((K) => K.id !== z);
    x(H), I(g, H);
  }, [I, w, s, g]), Te = ie((z, H, K) => {
    a && j({ kind: "spliceEdge", edgeId: z, clientX: H, clientY: K });
  }, [a]), tt = (z) => {
    const H = v;
    if (!H) return;
    j(null);
    const K = k(H.clientX, H.clientY) ?? { x: 0, y: 0 };
    if (H.kind === "fromEmpty") {
      const se = C(z, K), ye = [...g.map((je) => je.selected ? { ...je, selected: !1 } : je), se.node];
      m(ye), f(se.node.id), I(ye, w, [se.activityNode]);
      return;
    }
    if (H.kind === "fromPort") {
      const se = g.find((ke) => ke.id === H.sourceNodeId), pe = se ? hc(se) : K, ye = C(z, pe), Ee = [...g.map((ke) => ke.selected ? { ...ke, selected: !1 } : ke), ye.node], Me = [...w, Po(H.sourceNodeId, ye.node.id, H.sourceHandleId ?? "Done")];
      m(Ee), x(Me), f(ye.node.id), I(Ee, Me, [ye.activityNode]);
      return;
    }
    const X = w.find((se) => se.id === H.edgeId);
    X && $(z, X, K);
  }, We = fe(() => ({
    highlightedEdgeId: N,
    deleteEdge: Ie,
    requestInsertActivity: Te
  }), [Ie, N, Te]);
  return {
    nodes: g,
    edges: w,
    canvasRef: E,
    setReactFlowInstance: y,
    connectMenu: v,
    setConnectMenu: j,
    edgeActions: We,
    onNodesChange: M,
    onEdgesChange: q,
    onNodesDelete: we,
    onEdgesDelete: Ae,
    isValidConnection: ae,
    onConnect: ce,
    onConnectStart: V,
    onConnectEnd: ee,
    onReconnect: ge,
    commitLayout: Q,
    canAutoLayout: oe,
    autoLayout: de,
    onCanvasDragOver: U,
    onCanvasDragLeave: te,
    onCanvasDrop: le,
    openEmptyConnectMenu: G,
    onConnectMenuPick: tt,
    onPaletteClick: Z,
    onPaletteDragStart: F,
    onPaletteDragEnd: W,
    onPalettePointerDown: O
  };
}
const $b = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function vd(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function ws(e) {
  return vd(e.name);
}
function Mb(e, t) {
  const n = ws(t), o = e[n];
  return t.isWrapped === !1 ? o ?? t.defaultValue ?? "" : Nd(o, t);
}
function bd(e, t) {
  return Nd(e[ws(t)], t);
}
function Rb(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function Lb(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function wc(e, t, n) {
  return {
    ...e,
    [ws(t)]: n
  };
}
function zb(e, t) {
  return t.isWrapped === !1 ? Mb(e, t) : bd(e, t).expression.value;
}
function Nd(e, t) {
  return Xb(e) ? {
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
const Vb = /* @__PURE__ */ new Set([
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
function Ob(e) {
  const t = e?.trim();
  if (!t) return null;
  const n = /^([\w.+]+)\[\]/.exec(t);
  if (n) return { elementTypeName: n[1] };
  const o = t.indexOf("`");
  if (o < 0) return null;
  const r = t.slice(0, o), s = (r.split(".").pop() ?? r).toLowerCase();
  return Vb.has(s) ? { elementTypeName: Hb(t.slice(o)) } : null;
}
function Hb(e) {
  const t = /\[\[([\w.+]+)/.exec(e);
  if (t) return t[1];
  const n = /\[([\w.+]+)/.exec(e);
  return n ? n[1] : null;
}
function Wb(e) {
  const t = e.uiHint?.toLowerCase();
  return t === "json" || t === "code" ? !0 : e.uiSpecifications?.repeater === !1;
}
function Fb(e) {
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
function Bb(e) {
  const t = (e ?? "").toLowerCase();
  return t === "system.boolean" || t === "boolean" || t === "bool" ? !1 : "";
}
function Kb(e, t) {
  return {
    ...e,
    typeName: t ?? "System.String",
    isWrapped: !1,
    // The description documents the collection as a whole; repeating it on every row is noise.
    description: null
  };
}
function hi(e, t, n) {
  if (t === n || t < 0 || n < 0 || t >= e.length || n >= e.length) return e;
  const o = [...e], [r] = o.splice(t, 1);
  return o.splice(n, 0, r), o;
}
function Xb(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
function jd(e) {
  return vs(e?.trim() ?? "") || e;
}
function vs(e) {
  if (!e) return "";
  const t = qb(e);
  if (!t) return "";
  const n = /^(.*)((?:\[\])+)$/.exec(t);
  if (n) return `${vs(n[1])}${n[2]}`;
  const o = t.indexOf("`");
  if (o >= 0) {
    const r = vc(t.slice(0, o)), s = Yb(t.slice(o));
    return s.length > 0 ? `${r}<${s.join(", ")}>` : r;
  }
  return vc(t);
}
function qb(e) {
  let t = 0;
  for (let n = 0; n < e.length; n++) {
    const o = e[n];
    if (o === "[") t++;
    else if (o === "]") t--;
    else if (o === "," && t === 0) return e.slice(0, n).trim();
  }
  return e.trim();
}
function vc(e) {
  const t = e.split(".").filter(Boolean).at(-1) ?? e;
  return t.split("+").filter(Boolean).at(-1) ?? t;
}
function Yb(e) {
  const t = e.indexOf("[");
  if (t < 0) return [];
  const n = bc(e, t);
  return n == null ? [] : Ub(n).map((o) => {
    const r = o.trim(), s = r.startsWith("[") ? bc(r, 0) ?? r : r;
    return vs(s);
  }).filter(Boolean);
}
function bc(e, t) {
  let n = 0;
  for (let o = t; o < e.length; o++)
    if (e[o] === "[") n++;
    else if (e[o] === "]" && --n === 0) return e.slice(t + 1, o);
  return null;
}
function Ub(e) {
  const t = [];
  let n = 0, o = 0;
  for (let r = 0; r < e.length; r++) {
    const s = e[r];
    s === "[" ? n++ : s === "]" ? n-- : s === "," && n === 0 && (t.push(e.slice(o, r)), o = r + 1);
  }
  return t.push(e.slice(o)), t.map((r) => r.trim()).filter(Boolean);
}
const Nc = "elsa-studio:apply-workflow-graph-operation-batch", jc = "elsa-studio:undo-workflow-graph-operation-batch", Zb = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function Gb(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const o = iN(e), r = Cd(o.state.rootActivity), s = /* @__PURE__ */ new Map(), a = /* @__PURE__ */ new Map(), c = [];
  for (const u of t.operations) {
    const l = rN(u.kind), d = u.parameters ?? {};
    if (l === "add-activity") {
      const f = Pe(d.activityId) ?? u.temporaryReferences?.[0], p = oN(f ?? Pe(d.displayName) ?? Pe(d.activityType) ?? "weaver-activity", r), h = Jb(u, p, n);
      a.set(p, h), c.push(p), f && s.set(f, p), o.state.rootActivity && Qb(o.state.rootActivity, h);
      const g = ht(d.position) ? $i(d.position, { x: 280, y: 160 }) : null;
      g && (o.layout = Sc(o.layout, p, g));
      continue;
    }
    if (l === "set-root") {
      const f = gi(o, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown root activity.");
      o.state.rootActivity = f;
      continue;
    }
    if (l === "set-designer-position") {
      const f = kt(d.activityId, s);
      if (!f || !bs(o.state.rootActivity, f)) throw new Error("Weaver batch referenced an unknown activity position.");
      o.layout = Sc(o.layout, f, $i(d, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const f = gi(o, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity property target.");
      nN(f, Pe(d.propertyName) ?? "Value", d.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const f = gi(o, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity update target.");
      const p = ht(d.patch) ? d.patch : d;
      Object.assign(f, p);
      continue;
    }
    if (l === "remove-activity") {
      const f = kt(d.activityId, s);
      if (!f) throw new Error("Weaver batch referenced an unknown activity remove target.");
      o.state.rootActivity = Sd(o.state.rootActivity, f), o.layout = o.layout.filter((p) => p.nodeId !== f);
      continue;
    }
    if (l === "connect-activities") {
      eN(o, d, s);
      continue;
    }
    if (l === "disconnect-activities") {
      tN(o, d, s);
      continue;
    }
    throw new Error(`Weaver batch operation '${String(u.kind || "unknown")}' is not supported by this designer apply path.`);
  }
  if (!o.state.rootActivity) throw new Error("Weaver batch did not produce a root activity.");
  return o.sourceVersionId = null, {
    draft: o,
    appliedCount: t.operations.length,
    finalActivityIds: c,
    temporaryReferences: Object.fromEntries(s),
    summary: `Applied ${t.operations.length} workflow operation${t.operations.length === 1 ? "" : "s"} to the working draft.`
  };
}
function Jb(e, t, n) {
  const o = e.parameters ?? {}, r = Pe(o.activityVersionId) ?? Pe(o.activityType) ?? "Elsa.Workflows.Activity", s = n.find((a) => a.activityVersionId === r || a.activityTypeKey === r || a.displayName === Pe(o.displayName));
  return s ? wi(s, t) : {
    nodeId: t,
    // catalogItem is provably undefined here (the truthy branch returned above), so use the id directly.
    activityVersionId: r,
    inputs: [],
    outputs: [],
    ...Pe(o.displayName) ? { displayName: Pe(o.displayName) } : {},
    designer: { position: $i(o.position, { x: 280, y: 160 }) }
  };
}
function Qb(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = Ns(e);
  n && !n.some((o) => o.nodeId === t.nodeId) && n.push(t);
}
function eN(e, t, n) {
  const o = e.state.rootActivity;
  if (!o?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const r = kt(t.sourceActivityId ?? t.sourceId ?? t.from, n), s = kt(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!r || !s) throw new Error("Weaver batch connection is missing source or target activity.");
  const a = o.structure.payload, c = Array.isArray(a.connections) ? a.connections : [], u = Pe(t.connectionId) ?? `flow-${r}-${s}`;
  a.connections = [
    ...c.filter((l) => !ht(l) || l.id !== u),
    {
      id: u,
      source: { nodeId: r, port: Pe(t.outcome) ?? Pe(t.sourcePort) ?? "Done" },
      target: { nodeId: s }
    }
  ];
}
function tN(e, t, n) {
  const o = e.state.rootActivity, r = o?.structure?.payload.connections;
  if (!Array.isArray(r)) return;
  const s = Pe(t.connectionId), a = kt(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = kt(t.targetActivityId ?? t.targetId ?? t.to, n);
  o.structure.payload.connections = r.filter((u) => {
    if (!ht(u)) return !0;
    if (s && u.id === s) return !1;
    const l = ht(u.source) ? u.source.nodeId : void 0, d = ht(u.target) ? u.target.nodeId : void 0;
    return l !== a || d !== c;
  });
}
function nN(e, t, n) {
  const o = ht(n);
  e[vd(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: o ? "Object" : "Literal", value: n }
  };
}
function gi(e, t, n, o) {
  const r = kt(t, n);
  return r ? bs(e.state.rootActivity, r) ?? o.get(r) ?? null : null;
}
function kt(e, t) {
  const n = Pe(e);
  return n ? t.get(n) ?? n : null;
}
function bs(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of Ed(e)) {
    const o = bs(n, t);
    if (o) return o;
  }
  return null;
}
function Sd(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = Ns(e);
  if (n) {
    const o = n.map((r) => Sd(r, t)).filter((r) => !!r);
    n.splice(0, n.length, ...o);
  }
  return e;
}
function Cd(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of Ed(e)) Cd(n, t);
  return t;
}
function Ed(e) {
  return Ns(e) ?? [];
}
function Ns(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function Sc(e, t, n) {
  return [
    ...e.filter((o) => o.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function $i(e, t) {
  const n = ht(e) ? e : {}, o = Number(n.x), r = Number(n.y);
  return {
    x: Number.isFinite(o) ? Math.max(40, Math.round(o)) : t.x,
    y: Number.isFinite(r) ? Math.max(40, Math.round(r)) : t.y
  };
}
function oN(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let o = n, r = 2;
  for (; t.has(o); )
    o = `${n}-${r}`, r += 1;
  return t.add(o), o;
}
function rN(e) {
  return typeof e == "number" ? Zb[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Pe(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function iN(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function ht(e) {
  return typeof e == "object" && e !== null;
}
function sN({
  draft: e,
  details: t,
  catalog: n,
  replaceDraftByBatch: o,
  setStatus: r,
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
        const p = xn(e), h = Gb(e, d.batch, n), g = `weaver-batch-${Date.now()}`;
        a.current.set(g, p), o(h.draft, h.finalActivityIds.at(-1) ?? null), r(h.summary), s(""), d.respond({ ok: !0, result: { ...h, undoToken: g } });
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
      a.current.delete(d.undoToken), o(f, null), r("Restored workflow draft before Weaver batch."), s(""), d.respond({ ok: !0, summary: "Restored workflow draft before Weaver batch." });
    };
    return window.addEventListener(Nc, c), window.addEventListener(jc, u), () => {
      window.removeEventListener(Nc, c), window.removeEventListener(jc, u);
    };
  }, [n, t, e, o, r, s]);
}
function aN({ context: e, draft: t, editDraft: n, setStatus: o, setError: r }) {
  const [s, a] = B(!1), c = re(""), u = re(0), l = re(Promise.resolve()), d = ie((p) => {
    c.current = p ? Le(p) : "";
  }, []), f = ie(async (p, h) => {
    const g = async () => {
      const w = ++u.current, x = Le(p);
      r("");
      try {
        const b = await bp(e, p), y = Le(b);
        return c.current = y, n(({ draft: v }) => !v || v.id !== b.id ? null : Le(v) === x ? b : { ...v, validationErrors: b.validationErrors }), w === u.current && o(h), b;
      } catch (b) {
        throw w === u.current && (o(""), r(b instanceof Error ? b.message : String(b))), b;
      }
    }, m = l.current.then(g, g);
    return l.current = m.catch(() => {
    }), m;
  }, [e, n, o, r]);
  return J(() => {
    if (!s || !t || Le(t) === c.current) return;
    o("Autosaving...");
    const h = window.setTimeout(() => {
      f(t, "Autosaved").catch(() => {
      });
    }, C0);
    return () => window.clearTimeout(h);
  }, [s, t, f, o]), { saveDraft: f, autosaveEnabled: s, setAutosaveEnabled: a, markSaved: d };
}
function cN({ context: e, definitionId: t, resetHistory: n, loadDraft: o, markSaved: r, setError: s }) {
  const [a, c] = B(null), [u, l] = B([]), [d, f] = B([]), [p, h] = B(null), [g, m] = B(vo), [w, x] = B("loading"), b = ie(async () => {
    s(""), x("loading");
    const [y, v, j, N, S] = await Promise.all([
      fp(e, t),
      qi(e),
      Ap(e).then(
        (D) => ({ ok: !0, descriptors: D }),
        () => ({ ok: !1, descriptors: [] })
      ),
      _p(e).then(
        (D) => ({ ok: !0, descriptors: D }),
        () => ({ ok: !1, descriptors: vo })
      ),
      // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
      ll(e).then(
        (D) => D,
        () => null
      )
    ]), E = y.draft ?? null;
    c(y), r(E), n(E), o(E), l(v.activities ?? []), f(j.descriptors), h(S), m(N.descriptors.length > 0 ? N.descriptors : vo), x(j.ok ? "ready" : "failed");
  }, [e, t, n, o, r, s]);
  return J(() => {
    b().catch((y) => s(y instanceof Error ? y.message : String(y)));
  }, [b, s]), {
    details: a,
    setDetails: c,
    catalog: u,
    activityDescriptors: d,
    availabilityDiagnostics: p,
    expressionDescriptors: g,
    descriptorStatus: w,
    reload: b
  };
}
function lN({ context: e, details: t, setDetails: n, setStatus: o }) {
  const r = re(null), s = re(null), a = re({});
  J(() => {
    r.current = t;
  }, [t]);
  const c = ie(() => {
    s.current !== null && (window.clearTimeout(s.current), s.current = null);
    const l = a.current;
    a.current = {};
    const d = r.current?.definition;
    !d || l.name === void 0 && l.description === void 0 || vp(e, d.id, {
      name: l.name ?? d.name,
      description: l.description ?? d.description ?? null
    }).then((f) => n((p) => p && p.definition.id === f.definition.id ? { ...p, definition: f.definition } : p)).catch(() => o("Couldn't save name/description."));
  }, [e, n, o]), u = ie((l) => {
    n((d) => d && { ...d, definition: { ...d.definition, ...l } }), a.current = { ...a.current, ...l }, s.current !== null && window.clearTimeout(s.current), s.current = window.setTimeout(c, 800);
  }, [c, n]);
  return J(() => () => {
    c();
  }, [c]), { updateDefinitionMeta: u };
}
function uN({
  context: e,
  draft: t,
  details: n,
  busy: o,
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
  const g = ie(() => {
    if (!t) return;
    const b = n?.definition.name;
    o0(e0(t, b), b), d("Exported workflow as JSON.");
  }, [t, n, d]), m = ie(async () => {
    if (!(!t || o)) {
      l("saving"), d("Saving...");
      try {
        await r(t, "Saved");
      } catch {
      } finally {
        l("idle");
      }
    }
  }, [t, o, r, l, d]), w = ie(async () => {
    if (!(!t || o)) {
      l("promoting"), d("Saving...");
      try {
        await r(t, "Saved"), d("Promoting...");
        const b = await Np(e, t.id), y = await jp(e, b.versionId);
        u(y.artifactId), d(`Published ${y.artifactVersion}`), await s();
      } catch (b) {
        d(""), f(b instanceof Error ? b.message : String(b));
      } finally {
        l("idle");
      }
    }
  }, [t, o, e, r, s, u, l, d, f]), x = ie(async () => {
    if (!t?.state.rootActivity || o) return;
    const b = t, y = Le(b);
    c(), d("Preparing test run...");
    try {
      l("testRunPreparing"), d("Preparing test run...");
      const v = Q0(b);
      l("testRunStarting"), d("Starting test run...");
      const j = await Sp(e, {
        definitionId: b.definitionId,
        snapshotId: v,
        state: b.state
      });
      a({ draftSignature: y, view: j }), p("runtime"), h(!1), d(ys(j) ? "Test run rejected" : "Test run dispatched");
    } catch (v) {
      d(""), f(v instanceof Error ? v.message : String(v));
    } finally {
      l("idle");
    }
  }, [t, o, e, c, a, p, h, l, d, f]);
  return { exportJson: g, save: m, promoteAndPublish: w, run: x };
}
function dN({
  context: e,
  draft: t,
  frames: n,
  selectedNodeId: o,
  catalog: r,
  activityDescriptors: s,
  availabilityDiagnostics: a
}) {
  const c = t?.state.rootActivity ?? null, u = fe(() => new Map(r.map((S) => [S.activityVersionId, S])), [r]), l = ie(
    (S) => Xp([S.activityVersionId, S.activityTypeKey], a),
    [a]
  ), d = fe(() => B0(s), [s]), f = fe(() => Yc(c, n, u), [c, n, u]), p = Xi(f, f ? u.get(f.activityVersionId) : void 0), h = !!f && p === "unsupported", g = fe(() => h ? null : Sn(c, n, u), [c, n, u, h]), m = fe(() => h && f?.nodeId === o ? f : g?.slot.activities.find((S) => S.nodeId === o) ?? null, [h, g, f, o]), w = fe(
    () => m ? K0(m, u, d) : null,
    [u, d, m]
  ), x = fe(
    () => m ? l({ activityVersionId: m.activityVersionId, activityTypeKey: u.get(m.activityVersionId)?.activityTypeKey }) : null,
    [l, u, m]
  ), b = m ? De(m, u) : [], y = m ? np(m, u.get(m.activityVersionId)) : !1, v = hp(e, t?.state, o, u), j = p === "flowchart" && g?.slot.mode === "flowchart";
  return {
    catalogByVersion: u,
    availabilityLookup: l,
    scopeOwner: f,
    isUnsupportedDesigner: h,
    scope: g,
    selectedNode: m,
    selectedDescriptor: w,
    selectedNodeAvailability: x,
    selectedSlots: b,
    selectedSupportsScopedVariables: y,
    scopedVariableAnalysis: v,
    isFlowchartDesigner: j,
    canAddActivitiesToCanvas: !c || !h
  };
}
function fN({
  details: e,
  draft: t,
  selectedNode: n,
  selectedNodeId: o,
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
        revision: J0(t),
        selectedNodeId: o,
        selectedActivityType: r?.typeName ?? (n ? s.get(n.activityVersionId)?.activityTypeKey ?? n.activityVersionId : null),
        summary: e.definition.name,
        activities: hd(t.state.rootActivity, s),
        connections: gd(t.state.rootActivity, s),
        diagnostics: t.validationErrors.map((a) => ({ severity: a.code ?? "warning", message: a.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === e.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [s, e, t, r, n, o]);
}
function pN({
  paletteSearch: e,
  onSearchChange: t,
  groups: n,
  expandedCategories: o,
  onToggleCategory: r,
  onActivityClick: s,
  onActivityDragStart: a,
  onActivityDragEnd: c,
  onActivityPointerDown: u
}) {
  const l = e.trim().length > 0;
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-palette-body", children: [
    /* @__PURE__ */ i.jsxs("label", { className: "wf-palette-search", children: [
      /* @__PURE__ */ i.jsx(Uo, { size: 14, "aria-hidden": "true" }),
      /* @__PURE__ */ i.jsx(
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
    /* @__PURE__ */ i.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: n.length === 0 ? /* @__PURE__ */ i.jsx("p", { className: "wf-muted wf-palette-empty", children: "No matching activities." }) : n.map((d) => {
      const f = l || o.has(d.category);
      return /* @__PURE__ */ i.jsxs("div", { className: "wf-palette-category", children: [
        /* @__PURE__ */ i.jsxs(
          "button",
          {
            type: "button",
            className: "wf-palette-category-toggle",
            role: "treeitem",
            "aria-expanded": f,
            onClick: () => r(d.category),
            children: [
              f ? /* @__PURE__ */ i.jsx(Mc, { size: 14 }) : /* @__PURE__ */ i.jsx($t, { size: 14 }),
              /* @__PURE__ */ i.jsx("span", { children: d.category }),
              /* @__PURE__ */ i.jsx("small", { children: d.activities.length })
            ]
          }
        ),
        f ? /* @__PURE__ */ i.jsx("div", { className: "wf-palette-activities", role: "group", children: d.activities.map((p) => {
          const h = p.description?.trim(), g = h ? `wf-palette-description-${p.activityVersionId}` : void 0, m = Ce(p), w = Qo(p);
          return /* @__PURE__ */ i.jsxs(
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
                /* @__PURE__ */ i.jsx("span", { className: "wf-activity-icon", "data-icon": w, "aria-hidden": "true", children: hs(w) }),
                /* @__PURE__ */ i.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ i.jsx("strong", { children: m }),
                  h ? /* @__PURE__ */ i.jsx("small", { id: g, children: h }) : null
                ] }),
                /* @__PURE__ */ i.jsx(Rc, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            p.activityVersionId
          );
        }) }) : null
      ] }, d.category);
    }) })
  ] });
}
const Id = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]), hN = "Variable";
function gN({
  activity: e,
  descriptor: t,
  editors: n,
  expressionEditors: o,
  expressionDescriptors: r,
  descriptorStatus: s,
  visibleVariables: a,
  scopeStatus: c,
  onChange: u
}) {
  if (s === "loading")
    return /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "Loading activity properties..." });
  if (!t)
    return /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "No activity descriptor is available for this activity." });
  const l = t.inputs.filter((p) => p.isBrowsable !== !1).sort((p, h) => (p.order ?? 0) - (h.order ?? 0) || p.name.localeCompare(h.name));
  if (l.length === 0)
    return /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "This activity does not expose editable properties." });
  const d = jN(l), f = r.length > 0 ? r : $b;
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ i.jsx("span", { className: "wf-section-label", children: "Properties" }),
    d.map((p) => /* @__PURE__ */ i.jsxs("section", { className: "wf-property-group", children: [
      d.length > 1 ? /* @__PURE__ */ i.jsx("h4", { children: p.category }) : null,
      p.inputs.map((h) => /* @__PURE__ */ i.jsx(
        yN,
        {
          activity: e,
          input: h,
          editors: n,
          expressionEditors: o,
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
function yN({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: o,
  expressionDescriptors: r,
  visibleVariables: s,
  scopeStatus: a,
  onChange: c
}) {
  const u = t.isReadOnly === !0, l = { activity: e, expressionDescriptors: r, readOnly: u }, d = zi(n, t, l), f = d?.component, p = t.isWrapped !== !1 ? bd(e, t) : null, h = p?.expression.type ?? "Literal", g = zb(e, t), m = h.toLowerCase(), x = p && (m === "literal" || m === "object") && !Wb(t) ? Ob(t.typeName) : null, b = x ? zi(n, t, { ...l, scope: "collection" }) : void 0, y = p ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: r,
    readOnly: u,
    surface: "inline",
    syntax: h
  } : null, v = y ? Ad(o, y) : null, j = v?.surfaces.inline, N = v && y ? _d(v, y, g) : [], S = x != null, E = !!(p && !S && SN(t, d?.id)), D = !!(p && !S && CN(t, d?.id)), [L, A] = B(!1), _ = (k) => {
    const T = p ? Rb(p, k) : k;
    c(wc(e, t, T));
  }, R = (k) => {
    p && c(wc(e, t, Lb(p, k)));
  }, C = x ? b ? Mi(b.component, t, g, u, { ...l, scope: "collection" }, _) : /* @__PURE__ */ i.jsx(
    xN,
    {
      input: t,
      elementTypeName: x.elementTypeName,
      value: g,
      editors: n,
      context: l,
      disabled: u,
      onChange: _
    }
  ) : null, I = h === hN && p ? /* @__PURE__ */ i.jsx(
    bN,
    {
      value: g,
      visibleVariables: s,
      scopeStatus: a,
      disabled: u,
      onChange: _
    }
  ) : C ?? (j && y ? /* @__PURE__ */ i.jsx(
    j,
    {
      descriptor: t,
      syntax: h,
      value: g,
      disabled: u,
      context: y,
      onChange: _
    }
  ) : Mi(f, t, g, u, l, _));
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ i.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ i.jsx("span", { children: jd(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ i.jsx("p", { children: t.description }) : null,
    p && !E ? /* @__PURE__ */ i.jsx(
      Ri,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: h,
        descriptors: r,
        disabled: u,
        onChange: R
      }
    ) : null,
    E ? /* @__PURE__ */ i.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-expression-editor", children: [
        I,
        Vi(N)
      ] }),
      /* @__PURE__ */ i.jsx(
        Ri,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: h,
          descriptors: r,
          disabled: u,
          variant: "inline",
          onChange: R
        }
      ),
      D ? /* @__PURE__ */ i.jsx(
        "button",
        {
          type: "button",
          className: "wf-expression-expand-button",
          "aria-label": `Open expanded ${t.displayName || t.name} editor`,
          title: "Open expanded editor",
          onClick: () => A(!0),
          children: /* @__PURE__ */ i.jsx(_o, { size: 13 })
        }
      ) : null
    ] }) : /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
      I,
      Vi(N)
    ] }),
    D && !E ? /* @__PURE__ */ i.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => A(!0),
        children: [
          /* @__PURE__ */ i.jsx(_o, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    L ? /* @__PURE__ */ i.jsx(
      wN,
      {
        input: t,
        value: g,
        syntax: h,
        descriptors: r,
        activity: e,
        expressionEditors: o,
        disabled: u,
        onChange: _,
        onSyntaxChange: R,
        onClose: () => A(!1)
      }
    ) : null
  ] });
}
function mN(e, t, n) {
  return [
    "wf-collection-item",
    t === e ? "dragging" : "",
    t !== null && t !== e && n === e ? "drop-target" : ""
  ].filter(Boolean).join(" ");
}
function xN({
  input: e,
  elementTypeName: t,
  value: n,
  editors: o,
  context: r,
  disabled: s,
  onChange: a
}) {
  const c = Fb(n), u = Kb(e, t), l = { ...r, scope: "element" }, d = zi(o, u, l)?.component, f = e.displayName || e.name, p = (j, N) => a(c.map((S, E) => E === j ? N : S)), [h, g] = B(null), [m, w] = B(null), x = () => {
    g(null), w(null);
  }, b = (j) => (N) => {
    g(j), N.dataTransfer.effectAllowed = "move", N.dataTransfer.setData("text/plain", String(j));
  }, y = (j) => (N) => {
    h !== null && (N.preventDefault(), N.dataTransfer.dropEffect = "move", m !== j && w(j));
  }, v = (j) => (N) => {
    N.preventDefault(), h !== null && h !== j && a(hi(c, h, j)), x();
  };
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-collection-editor", children: [
    c.length === 0 ? /* @__PURE__ */ i.jsx("p", { className: "wf-collection-empty", children: "No items yet." }) : /* @__PURE__ */ i.jsx("ul", { className: "wf-collection-items", children: c.map((j, N) => /* @__PURE__ */ i.jsxs(
      "li",
      {
        className: mN(N, h, m),
        onDragOver: y(N),
        onDrop: v(N),
        children: [
          /* @__PURE__ */ i.jsx(
            "span",
            {
              className: "wf-collection-item-handle",
              draggable: !s,
              "aria-label": `Drag ${f} item ${N + 1} to reorder`,
              title: "Drag to reorder",
              onDragStart: b(N),
              onDragEnd: x,
              children: /* @__PURE__ */ i.jsx(Rc, { size: 13, "aria-hidden": "true" })
            }
          ),
          /* @__PURE__ */ i.jsx("div", { className: "wf-collection-item-editor", children: Mi(d, u, j, s, l, (S) => p(N, S)) }),
          /* @__PURE__ */ i.jsxs("div", { className: "wf-collection-item-actions", children: [
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${N + 1} up`,
                disabled: s || N === 0,
                onClick: () => a(hi(c, N, N - 1)),
                children: /* @__PURE__ */ i.jsx(Hd, { size: 13 })
              }
            ),
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${N + 1} down`,
                disabled: s || N === c.length - 1,
                onClick: () => a(hi(c, N, N + 1)),
                children: /* @__PURE__ */ i.jsx(Mc, { size: 13 })
              }
            ),
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button danger",
                "aria-label": `Remove ${f} item ${N + 1}`,
                disabled: s,
                onClick: () => a(c.filter((S, E) => E !== N)),
                children: /* @__PURE__ */ i.jsx(jn, { size: 13 })
              }
            )
          ] })
        ]
      },
      N
    )) }),
    /* @__PURE__ */ i.jsxs(
      "button",
      {
        type: "button",
        className: "wf-collection-add",
        disabled: s,
        onClick: () => a([...c, Bb(t)]),
        children: [
          /* @__PURE__ */ i.jsx(Wt, { size: 13 }),
          " Add item"
        ]
      }
    )
  ] });
}
function wN({
  input: e,
  value: t,
  syntax: n,
  descriptors: o,
  activity: r,
  expressionEditors: s,
  disabled: a,
  onChange: c,
  onSyntaxChange: u,
  onClose: l
}) {
  const d = _c(), f = e.displayName || e.name, p = {
    activity: r,
    descriptor: e,
    expressionDescriptors: o,
    readOnly: a,
    surface: "expanded",
    syntax: n
  }, h = Ad(s, p), g = h?.surfaces.expanded, m = h ? _d(h, p, t) : [], w = g ? null : NN(s, p);
  return J(() => {
    const x = (b) => {
      b.key === "Escape" && l();
    };
    return window.addEventListener("keydown", x), () => window.removeEventListener("keydown", x);
  }, [l]), /* @__PURE__ */ i.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ i.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": d, children: [
    /* @__PURE__ */ i.jsxs("header", { children: [
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ i.jsx("h3", { id: d, children: f })
      ] }),
      /* @__PURE__ */ i.jsx("button", { type: "button", "aria-label": `Close ${f} editor`, onClick: l, children: /* @__PURE__ */ i.jsx($c, { size: 16 }) })
    ] }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ i.jsx(
          Ri,
          {
            label: `${f} expression syntax`,
            value: n,
            descriptors: o,
            disabled: a,
            onChange: u
          }
        ),
        /* @__PURE__ */ i.jsx("span", { children: jd(e.typeName) })
      ] }),
      e.description ? /* @__PURE__ */ i.jsx("p", { children: e.description }) : null,
      g ? /* @__PURE__ */ i.jsx(
        g,
        {
          descriptor: e,
          syntax: n,
          value: t,
          disabled: a,
          context: p,
          onChange: c
        }
      ) : /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
        w ? /* @__PURE__ */ i.jsx("p", { className: "wf-expression-editor-hint", children: w }) : null,
        /* @__PURE__ */ i.jsx(
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
      Vi(m)
    ] }),
    /* @__PURE__ */ i.jsxs("footer", { children: [
      /* @__PURE__ */ i.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: l, children: "Close" })
    ] })
  ] }) });
}
function Mi(e, t, n, o, r, s) {
  return e ? /* @__PURE__ */ i.jsx(
    e,
    {
      descriptor: t,
      value: n,
      disabled: o,
      context: r,
      onChange: s
    }
  ) : /* @__PURE__ */ i.jsx("input", { type: "text", value: n == null ? "" : String(n), disabled: o, onChange: (a) => s(a.target.value) });
}
function Ri({
  label: e,
  value: t,
  descriptors: n,
  disabled: o,
  variant: r = "block",
  onChange: s
}) {
  const [a, c] = B(!1), u = _c(), l = n.find((f) => f.type === t), d = [
    "wf-syntax-picker-trigger",
    r === "inline" ? "inline" : "",
    a ? "open" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ i.jsxs("div", { className: r === "inline" ? "wf-syntax-picker inline" : "wf-syntax-picker", onBlur: (f) => {
    f.currentTarget.contains(f.relatedTarget) || c(!1);
  }, children: [
    /* @__PURE__ */ i.jsx(
      "button",
      {
        type: "button",
        className: d,
        "aria-label": e,
        "aria-haspopup": "listbox",
        "aria-expanded": a,
        "aria-controls": u,
        disabled: o,
        onClick: () => c((f) => !f),
        children: /* @__PURE__ */ i.jsx("span", { children: l?.displayName || l?.type || t })
      }
    ),
    a ? /* @__PURE__ */ i.jsx("div", { id: u, role: "listbox", className: "wf-syntax-picker-menu", "aria-label": e, children: n.map((f) => {
      const p = f.displayName || f.type, h = f.type === t;
      return /* @__PURE__ */ i.jsx(
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
const Li = "::";
function kd(e) {
  return !e || e === Mo ? Mo : e;
}
function Cc(e, t) {
  return `${kd(t)}${Li}${e}`;
}
function vN(e) {
  const t = e.indexOf(Li);
  if (t < 0) return null;
  const n = e.slice(t + Li.length);
  return n ? { scopeId: e.slice(0, t), referenceKey: n } : null;
}
function bN({ value: e, visibleVariables: t, scopeStatus: n, disabled: o, onChange: r }) {
  const s = sl(e), a = !!e && typeof e == "object" || typeof e == "string" && e.trim().startsWith("{"), c = s && (a || t.some((d) => d.referenceKey === s.referenceKey)) ? s : null, u = c ? Cc(c.referenceKey, c.declaringScopeId) : "", l = !!c && t.some(
    (d) => d.referenceKey === c.referenceKey && d.scopeId === kd(c.declaringScopeId)
  );
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-variable-picker", children: [
    /* @__PURE__ */ i.jsxs(
      "select",
      {
        "aria-label": "Variable reference",
        value: u,
        disabled: o,
        onChange: (d) => {
          const f = vN(d.target.value);
          f && r(rp(f.referenceKey, f.scopeId));
        },
        children: [
          /* @__PURE__ */ i.jsx("option", { value: "", children: "Select a variable…" }),
          c && !l ? /* @__PURE__ */ i.jsxs("option", { value: u, children: [
            c.referenceKey,
            " (not visible from this scope)"
          ] }) : null,
          t.map((d) => {
            const f = Cc(d.referenceKey, d.scopeId);
            return /* @__PURE__ */ i.jsxs("option", { value: f, children: [
              d.name,
              d.isWorkflowScope ? " · workflow" : " · container"
            ] }, f);
          })
        ]
      }
    ),
    n === "unavailable" ? /* @__PURE__ */ i.jsx("p", { className: "wf-variable-picker-note", children: "Variable scope information is unavailable (pending backend support). Existing references are preserved." }) : n === "ready" && t.length === 0 ? /* @__PURE__ */ i.jsx("p", { className: "wf-variable-picker-note", children: "No variables are visible here. Declare one on the workflow or a container scope." }) : null
  ] });
}
function zi(e, t, n) {
  return [...e].sort((o, r) => (o.order ?? 500) - (r.order ?? 500)).find((o) => o.supports(t, n));
}
function Ad(e, t) {
  return [...e].sort((n, o) => (n.order ?? 500) - (o.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function _d(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function NN(e, t) {
  if (t.syntax.toLowerCase() === "literal") return null;
  const n = [...e].sort((a, c) => (a.order ?? 500) - (c.order ?? 500)).find((a) => a.supports(t) && a.metadata)?.metadata;
  if (!n) return `No enhanced editor is registered for ${t.syntax}. Using the generic text editor.`;
  const o = n.displayName?.trim() || "enhanced editor", r = n.installHint?.trim(), s = `No ${o} is registered for ${t.syntax}. Using the generic text editor.`;
  return r ? `${s} ${r}` : s;
}
function Vi(e) {
  return e.length === 0 ? null : /* @__PURE__ */ i.jsx("div", { className: "wf-expression-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const o = t.severity ?? "info";
    return /* @__PURE__ */ i.jsxs("p", { className: `wf-expression-editor-diagnostic ${o}`, children: [
      t.code ? /* @__PURE__ */ i.jsx("span", { children: t.code }) : null,
      t.message
    ] }, `${t.code ?? "diagnostic"}-${n}`);
  }) });
}
function jN(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "General";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return [...t.entries()].map(([n, o]) => ({ category: n, inputs: o }));
}
function SN(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !Id.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function CN(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !Id.has(t) && n !== "multiline") return !1;
  const o = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(o) || n === "singleline" || n === "multiline";
}
function EN({
  context: e,
  selectedNode: t,
  selectedNodeLabel: n,
  selectedActivityType: o,
  selectedDescriptor: r,
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
  return t ? /* @__PURE__ */ i.jsxs("div", { className: "wf-inspector-content", children: [
    /* @__PURE__ */ i.jsx("h3", { children: n }),
    /* @__PURE__ */ i.jsxs("dl", { children: [
      /* @__PURE__ */ i.jsx("dt", { children: "Node ID" }),
      /* @__PURE__ */ i.jsx("dd", { children: t.nodeId }),
      /* @__PURE__ */ i.jsx("dt", { children: "Activity type" }),
      /* @__PURE__ */ i.jsx("dd", { children: o }),
      /* @__PURE__ */ i.jsx("dt", { children: "Activity version" }),
      /* @__PURE__ */ i.jsx("dd", { children: t.activityVersionId })
    ] }),
    s ? /* @__PURE__ */ i.jsxs("div", { className: "wf-availability-notice", children: [
      /* @__PURE__ */ i.jsx(Ao, { size: 14 }),
      /* @__PURE__ */ i.jsxs("span", { children: [
        "No longer available for new use · ",
        Ro(s.state)
      ] })
    ] }) : null,
    /* @__PURE__ */ i.jsx(
      gN,
      {
        activity: t,
        descriptor: r,
        editors: u,
        expressionEditors: l,
        expressionDescriptors: d,
        descriptorStatus: f,
        visibleVariables: p.visibleVariables,
        scopeStatus: p.status,
        onChange: h
      }
    ),
    c ? /* @__PURE__ */ i.jsx("div", { className: "wf-container-variables", children: /* @__PURE__ */ i.jsx(
      b0,
      {
        context: e,
        variables: il(t),
        title: "Container variables",
        addLabel: "Add container variable",
        emptyLabel: "No container variables declared on this activity.",
        warnings: sp(p.shadowingWarnings, t.nodeId),
        onChange: (m) => h(op(t, m))
      }
    ) }) : null,
    a.length > 0 ? /* @__PURE__ */ i.jsxs("div", { className: "wf-slot-list", children: [
      /* @__PURE__ */ i.jsx("span", { children: "Embedded slots" }),
      a.map((m) => /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => g(t, m.id, `${n} / ${m.label}`), children: [
        m.label,
        /* @__PURE__ */ i.jsxs("small", { children: [
          m.activities.length,
          " activit",
          m.activities.length === 1 ? "y" : "ies"
        ] })
      ] }, m.id))
    ] }) : /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
  ] }) : /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
}
function IN({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: o,
  expressionEditors: r,
  workflowDesignerPanels: s,
  onBack: a
}) {
  const c = Ib(), { draft: u, frames: l, selectedNodeId: d, testRun: f, publishedArtifactId: p } = c.state, {
    loadDraft: h,
    replaceDraftByBatch: g,
    editDraft: m,
    editDraftAndSelect: w,
    select: x,
    navigateToScope: b,
    resetToRoot: y,
    enterSlot: v,
    startTestRun: j,
    clearTestRun: N,
    setPublishedArtifact: S
  } = c, [E, D] = B(""), [L, A] = B(""), [_, R] = B("idle"), [C, I] = B(() => /* @__PURE__ */ new Set()), [k, T] = B(""), [$, P] = B("activities"), [F, W] = B("inspector"), [O, Z] = B("designer"), {
    paletteWidth: U,
    inspectorWidth: te,
    paletteCollapsed: le,
    inspectorCollapsed: G,
    maximizedSidePanel: M,
    setInspectorCollapsed: q,
    paletteExpanded: ae,
    inspectorExpanded: ce,
    editorBodyClassName: Q,
    editorBodyStyle: oe,
    toggleSidePanelCollapsed: de,
    toggleSidePanelMaximized: V,
    startSidePanelResize: ee,
    handleSidePanelResizeKeyDown: ge
  } = xb(), { resetHistory: we, undo: Ae, redo: Ie, canUndoNow: Te, canRedoNow: tt } = Sb({ draft: u, restoreDraft: h }), { saveDraft: We, autosaveEnabled: z, setAutosaveEnabled: H, markSaved: K } = aN({ context: e, draft: u, editDraft: m, setStatus: A, setError: D }), {
    details: X,
    setDetails: se,
    catalog: pe,
    activityDescriptors: ye,
    availabilityDiagnostics: je,
    expressionDescriptors: Ee,
    descriptorStatus: Me,
    reload: ke
  } = cN({ context: e, definitionId: t, resetHistory: we, loadDraft: h, markSaved: K, setError: D }), { updateDefinitionMeta: hr } = lN({ context: e, details: X, setDetails: se, setStatus: A }), {
    catalogByVersion: He,
    availabilityLookup: gr,
    scopeOwner: Bn,
    isUnsupportedDesigner: At,
    scope: yr,
    selectedNode: Ye,
    selectedDescriptor: _t,
    selectedNodeAvailability: mr,
    selectedSlots: Kn,
    selectedSupportsScopedVariables: xr,
    scopedVariableAnalysis: wr,
    isFlowchartDesigner: ct,
    canAddActivitiesToCanvas: vr
  } = dN({ context: e, draft: u, frames: l, selectedNodeId: d, catalog: pe, activityDescriptors: ye, availabilityDiagnostics: je }), Dt = fe(() => Pi(pe), [pe]), br = fe(() => {
    const Y = k.trim().toLowerCase();
    if (!Y) return Dt;
    const ue = pe.filter((ve) => Ce(ve).toLowerCase().includes(Y) || ve.activityTypeKey.toLowerCase().includes(Y) || (ve.category ?? "").toLowerCase().includes(Y) || (ve.description ?? "").toLowerCase().includes(Y));
    return Pi(ue);
  }, [pe, k, Dt]), Tt = _ !== "idle", Nr = !!u?.state.rootActivity && !Tt, Xn = Et(n, "weaver.workflows.find-draft-risks"), qn = Et(n, "weaver.workflows.propose-update"), jr = Pb({
    draft: u,
    scope: yr,
    scopeOwner: Bn,
    catalog: pe,
    catalogByVersion: He,
    isUnsupportedDesigner: At,
    isFlowchartDesigner: ct,
    canAddActivitiesToCanvas: vr,
    selectedNodeId: d,
    editDraft: m,
    editDraftAndSelect: w,
    select: x,
    setStatus: A,
    setError: D
  }), {
    nodes: on,
    edges: Sr,
    canvasRef: Cr,
    setReactFlowInstance: Er,
    connectMenu: rn,
    setConnectMenu: Yn,
    edgeActions: Un,
    onNodesChange: Zn,
    onEdgesChange: Ir,
    onNodesDelete: kr,
    onEdgesDelete: Ar,
    isValidConnection: _r,
    onConnect: Dr,
    onConnectStart: Tr,
    onConnectEnd: Gn,
    onReconnect: Pr,
    commitLayout: $r,
    canAutoLayout: Mr,
    autoLayout: Rr,
    onCanvasDragOver: Jn,
    onCanvasDragLeave: Qn,
    onCanvasDrop: eo,
    openEmptyConnectMenu: Lr,
    onConnectMenuPick: to,
    onPaletteClick: zr,
    onPaletteDragStart: Vr,
    onPaletteDragEnd: Or,
    onPalettePointerDown: Hr
  } = jr;
  sN({ draft: u, details: X, catalog: pe, replaceDraftByBatch: g, setStatus: A, setError: D }), J(() => {
    !u?.state.rootActivity || pe.length === 0 || m(({ draft: Y }) => {
      if (!Y?.state.rootActivity) return null;
      const ue = Qc(Y.state.rootActivity, He);
      return !ue || ue === Y.state.rootActivity ? null : {
        ...Y,
        state: {
          ...Y.state,
          rootActivity: ue
        }
      };
    });
  }, [pe.length, He, u?.state.rootActivity, m]), fN({ details: X, draft: u, selectedNode: Ye, selectedNodeId: d, selectedDescriptor: _t, catalogByVersion: He }), J(() => {
    I((Y) => {
      let ue = !1;
      const ve = new Set(Y);
      for (const nt of Dt)
        ve.has(nt.category) || (ve.add(nt.category), ue = !0);
      return ue ? ve : Y;
    });
  }, [Dt]);
  const { exportJson: Wr, save: Fr, promoteAndPublish: Br, run: Kr } = uN({
    context: e,
    draft: u,
    details: X,
    busy: Tt,
    saveDraft: We,
    reload: ke,
    startTestRun: j,
    clearTestRun: N,
    setPublishedArtifact: S,
    setOperation: R,
    setStatus: A,
    setError: D,
    setActiveRightPanelId: W,
    setInspectorCollapsed: q
  }), no = ie((Y) => {
    m(({ draft: ue }) => ue ? { ...ue, state: Y(ue.state) } : null);
  }, [m]), Xr = ie((Y) => {
    if (!u) return "No draft is loaded.";
    const ue = n0(Y, u);
    return ue.ok ? (h(ue.draft), A("Applied workflow JSON."), null) : ue.error;
  }, [u, h]);
  J(() => {
    const Y = (ue) => {
      if (O !== "designer" || !(ue.metaKey || ue.ctrlKey)) return;
      const ve = ue.target;
      if (ve && (ve.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(ve.tagName))) return;
      const nt = ue.key.toLowerCase();
      nt === "z" && !ue.shiftKey ? (ue.preventDefault(), Ae()) : (nt === "z" && ue.shiftKey || nt === "y") && (ue.preventDefault(), Ie());
    };
    return window.addEventListener("keydown", Y), () => window.removeEventListener("keydown", Y);
  }, [O, Ae, Ie]);
  const oo = ie((Y) => {
    m(({ draft: ue }) => {
      const ve = ue?.state.rootActivity;
      return !ue || !ve ? null : {
        ...ue,
        state: {
          ...ue.state,
          rootActivity: Jc(ve, Y.nodeId, () => Y, He)
        }
      };
    });
  }, [He, m]), qr = ie((Y) => {
    if (!Y) return;
    const ue = u?.state.rootActivity;
    if (!ue) return;
    const ve = kf(ue, Y, (nt) => {
      const Es = He.get(nt.activityVersionId);
      return Es ? Ce(Es) : nt.nodeId;
    }, He);
    ve && (Z("designer"), b(ve, Y), q(!1));
  }, [u?.state.rootActivity, He, q, b]), Yr = (Y) => {
    I((ue) => {
      const ve = new Set(ue);
      return ve.has(Y) ? ve.delete(Y) : ve.add(Y), ve;
    });
  };
  if (!X || !u)
    return /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: E || "Loading workflow editor..." });
  const lt = f?.draftSignature === Le(u) ? f.view : null, ro = lt && L.startsWith("Test run") ? "" : L, Ur = (Y) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(Y)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, io = {
    definition: X.definition,
    draft: u,
    selectedActivity: Ye,
    selectedActivityDescriptor: _t,
    selectedActivitySlots: Kn,
    catalog: pe,
    currentScopeOwner: Bn,
    frames: l
  }, js = s.map((Y) => {
    const ue = Y.component;
    return {
      id: Y.id,
      title: Y.title,
      side: Y.side,
      order: Y.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ i.jsx(ue, { context: io })
    };
  }), Zr = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ i.jsx(Zo, { size: 15 }),
      render: () => /* @__PURE__ */ i.jsx(
        pN,
        {
          paletteSearch: k,
          onSearchChange: T,
          groups: br,
          expandedCategories: C,
          onToggleCategory: Yr,
          onActivityClick: zr,
          onActivityDragStart: Vr,
          onActivityDragEnd: Or,
          onActivityPointerDown: Hr
        }
      )
    },
    ...js.filter((Y) => Y.side === "left")
  ].sort(gc), Gr = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ i.jsx(Wi, { size: 15 }),
      render: () => /* @__PURE__ */ i.jsx(
        EN,
        {
          context: e,
          selectedNode: Ye,
          selectedNodeLabel: Ye ? on.find((Y) => Y.id === Ye.nodeId)?.data.label ?? Ye.nodeId : "",
          selectedActivityType: Ye ? _t?.typeName ?? He.get(Ye.activityVersionId)?.activityTypeKey ?? "Unknown" : "",
          selectedDescriptor: _t,
          selectedNodeAvailability: mr,
          selectedSlots: Kn,
          selectedSupportsScopedVariables: xr,
          propertyEditors: o,
          expressionEditors: r,
          expressionDescriptors: Ee,
          descriptorStatus: Me,
          scopedVariableAnalysis: wr,
          onSelectedActivityChange: oo,
          onEnterSlot: v
        }
      )
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: /* @__PURE__ */ i.jsx(Ht, { size: 15 }),
      render: () => /* @__PURE__ */ i.jsx(hb, { testRun: lt, onOpenRun: Ur })
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ i.jsx(Lc, { size: 15 }),
      render: () => /* @__PURE__ */ i.jsx(
        mb,
        {
          context: e,
          ai: n,
          definitionId: X.definition.id,
          publishedArtifactId: p
        }
      )
    },
    ...js.filter((Y) => Y.side === "right")
  ].sort(gc), Ss = Zr.find((Y) => Y.id === $) ?? Zr[0], Cs = Gr.find((Y) => Y.id === F) ?? Gr[0], Dd = [
    { id: "designer", title: "Designer", order: 0, icon: /* @__PURE__ */ i.jsx(zc, { size: 14 }), render: () => null },
    { id: "code", title: "Code", order: 1, icon: /* @__PURE__ */ i.jsx(Xd, { size: 14 }), render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: /* @__PURE__ */ i.jsx(Hi, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ i.jsx("button", { type: "button", className: "wf-link-button", onClick: a, children: "Definitions" }),
      /* @__PURE__ */ i.jsx($t, { size: 14 }),
      /* @__PURE__ */ i.jsx("strong", { children: X.definition.name }),
      /* @__PURE__ */ i.jsx("span", { className: "wf-chip", children: "Draft" }),
      ro ? /* @__PURE__ */ i.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ i.jsx(Qt, { size: 13 }),
        " ",
        ro
      ] }) : null,
      /* @__PURE__ */ i.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ i.jsxs("div", { className: "wf-canvas-tools", role: "group", "aria-label": "Canvas tools", children: [
          /* @__PURE__ */ i.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Undo",
              title: "Undo (Ctrl+Z)",
              disabled: !Te,
              onClick: Ae,
              children: /* @__PURE__ */ i.jsx(Wd, { size: 16 })
            }
          ),
          /* @__PURE__ */ i.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Redo",
              title: "Redo (Ctrl+Shift+Z)",
              disabled: !tt,
              onClick: Ie,
              children: /* @__PURE__ */ i.jsx(Fd, { size: 16 })
            }
          ),
          /* @__PURE__ */ i.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Auto-layout",
              title: "Auto-layout the canvas",
              disabled: !Mr,
              onClick: Rr,
              children: /* @__PURE__ */ i.jsx(Bd, { size: 16 })
            }
          )
        ] }),
        /* @__PURE__ */ i.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ i.jsx("input", { className: "wf-autosave-switch-input", type: "checkbox", checked: z, onChange: (Y) => H(Y.target.checked) }),
          /* @__PURE__ */ i.jsx("span", { children: "Autosave" })
        ] }),
        Xn ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => It(n, Xn, { definition: X.definition, draft: u }), children: [
          /* @__PURE__ */ i.jsx(it, { size: 15 }),
          " Risks"
        ] }) : null,
        qn ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => It(n, qn, { definition: X.definition, draft: u }), children: [
          /* @__PURE__ */ i.jsx(it, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ i.jsxs("button", { type: "button", title: "Export workflow as JSON", onClick: Wr, children: [
          /* @__PURE__ */ i.jsx(Kd, { size: 15 }),
          " Export"
        ] }),
        /* @__PURE__ */ i.jsxs("button", { type: "button", disabled: Tt, onClick: () => {
          Fr();
        }, children: [
          /* @__PURE__ */ i.jsx(Dc, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ i.jsxs("button", { type: "button", disabled: Tt, onClick: () => {
          Br();
        }, children: [
          /* @__PURE__ */ i.jsx(Pc, { size: 15 }),
          " Promote"
        ] }),
        lt ? /* @__PURE__ */ i.jsx(
          pb,
          {
            testRun: lt,
            onOpenDetails: () => {
              W("runtime"), q(!1);
            }
          }
        ) : null,
        /* @__PURE__ */ i.jsxs(
          "button",
          {
            type: "button",
            disabled: !Nr,
            title: u.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              Kr();
            },
            children: [
              /* @__PURE__ */ i.jsx(Ht, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    E ? /* @__PURE__ */ i.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ i.jsx(gt, { size: 16 }),
      " ",
      E
    ] }) : null,
    /* @__PURE__ */ i.jsxs("div", { className: Q, style: oe, children: [
      /* @__PURE__ */ i.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ i.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ i.jsx(
            ko,
            {
              label: "Activities panel tabs",
              tabs: Zr,
              activeTabId: Ss.id,
              onSelect: P
            }
          ),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": le ? "Expand activities panel" : "Collapse activities panel",
                title: le ? "Expand" : "Collapse",
                onClick: () => de("palette"),
                children: le ? /* @__PURE__ */ i.jsx($t, { size: 14 }) : /* @__PURE__ */ i.jsx(Do, { size: 14 })
              }
            ),
            le ? null : /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": M === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: M === "palette" ? "Restore" : "Maximize",
                onClick: () => V("palette"),
                children: M === "palette" ? /* @__PURE__ */ i.jsx(As, { size: 14 }) : /* @__PURE__ */ i.jsx(_o, { size: 14 })
              }
            )
          ] })
        ] }),
        ae ? Ss.render() : null
      ] }),
      ae && !M ? /* @__PURE__ */ i.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": pn,
          "aria-valuemax": hn,
          "aria-valuenow": U,
          tabIndex: 0,
          onPointerDown: (Y) => ee("palette", Y),
          onKeyDown: (Y) => ge("palette", Y)
        }
      ) : /* @__PURE__ */ i.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ i.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ i.jsx("div", { className: "wf-canvas-tabs", children: /* @__PURE__ */ i.jsx(
          ko,
          {
            label: "Editor view tabs",
            tabs: Dd,
            activeTabId: O,
            onSelect: (Y) => Z(Y)
          }
        ) }),
        O === "code" ? /* @__PURE__ */ i.jsx(c0, { draft: u, onApply: Xr }) : O === "properties" ? /* @__PURE__ */ i.jsx(j0, { details: X, draft: u, context: e, onStateChange: no, onDefinitionMetaChange: hr }) : /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
          /* @__PURE__ */ i.jsxs("div", { className: "wf-breadcrumb", children: [
            /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => y(), children: "Root" }),
            l.map((Y, ue) => /* @__PURE__ */ i.jsxs(Ge.Fragment, { children: [
              /* @__PURE__ */ i.jsx($t, { size: 13 }),
              /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => b(l.slice(0, ue + 1), null), children: Y.label })
            ] }, `${Y.ownerNodeId}-${Y.slotId}-${ue}`))
          ] }),
          /* @__PURE__ */ i.jsxs("div", { className: "wf-canvas", ref: Cr, onDragOver: Jn, onDragLeave: Qn, onDrop: eo, children: [
            /* @__PURE__ */ i.jsx(sd.Provider, { value: Un, children: /* @__PURE__ */ i.jsx(ad.Provider, { value: gr, children: /* @__PURE__ */ i.jsxs(
              Zu,
              {
                nodes: on,
                edges: Sr,
                nodeTypes: md,
                edgeTypes: xd,
                onInit: Er,
                onNodesChange: Zn,
                onEdgesChange: Ir,
                onNodesDelete: kr,
                onEdgesDelete: Ar,
                onConnect: Dr,
                onConnectStart: ct ? Tr : void 0,
                onConnectEnd: ct ? Gn : void 0,
                onReconnect: ct ? Pr : void 0,
                isValidConnection: _r,
                onDragOver: Jn,
                onDragLeave: Qn,
                onDrop: eo,
                onPaneClick: () => x(null),
                onNodeClick: (Y, ue) => x(ue.id),
                onNodeDragStop: At ? void 0 : $r,
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
                  /* @__PURE__ */ i.jsx(Ju, { gap: 18, size: 1 }),
                  /* @__PURE__ */ i.jsx(ed, {}),
                  /* @__PURE__ */ i.jsx(nd, { pannable: !0, zoomable: !0 })
                ]
              }
            ) }) }),
            ct && on.length === 0 ? /* @__PURE__ */ i.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => Lr(), children: [
              /* @__PURE__ */ i.jsx(Wt, { size: 15 }),
              " Add activity"
            ] }) : null,
            rn ? /* @__PURE__ */ i.jsx(
              sb,
              {
                clientX: rn.clientX,
                clientY: rn.clientY,
                activities: pe,
                onPick: to,
                onClose: () => Yn(null)
              }
            ) : null
          ] }),
          /* @__PURE__ */ i.jsx(fb, { draft: u, onRepair: qr })
        ] })
      ] }),
      ce && !M ? /* @__PURE__ */ i.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": gn,
          "aria-valuemax": yn,
          "aria-valuenow": te,
          tabIndex: 0,
          onPointerDown: (Y) => ee("inspector", Y),
          onKeyDown: (Y) => ge("inspector", Y)
        }
      ) : /* @__PURE__ */ i.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ i.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ i.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ i.jsx(
            ko,
            {
              label: "Inspector panel tabs",
              tabs: Gr,
              activeTabId: Cs.id,
              onSelect: W
            }
          ),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": G ? "Expand inspector panel" : "Collapse inspector panel",
                title: G ? "Expand" : "Collapse",
                onClick: () => de("inspector"),
                children: G ? /* @__PURE__ */ i.jsx(Do, { size: 14 }) : /* @__PURE__ */ i.jsx($t, { size: 14 })
              }
            ),
            G ? null : /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": M === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: M === "inspector" ? "Restore" : "Maximize",
                onClick: () => V("inspector"),
                children: M === "inspector" ? /* @__PURE__ */ i.jsx(As, { size: 14 }) : /* @__PURE__ */ i.jsx(_o, { size: 14 })
              }
            )
          ] })
        ] }),
        ce ? Cs.render() : null
      ] })
    ] })
  ] });
}
function kN({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const s = cd(n, t), a = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-pagination", "aria-label": "Workflow definition pagination", children: [
    /* @__PURE__ */ i.jsxs("span", { className: "wf-pagination-summary", "aria-live": "polite", children: [
      "Showing ",
      a,
      "-",
      c,
      " of ",
      n
    ] }),
    /* @__PURE__ */ i.jsxs("label", { className: "wf-page-size", children: [
      "Rows",
      /* @__PURE__ */ i.jsx("select", { value: t, onChange: (u) => r(Number(u.target.value)), children: I0.map((u) => /* @__PURE__ */ i.jsx("option", { value: u, children: u }, u)) })
    ] }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ i.jsx(Do, { size: 14 }),
        " Previous"
      ] }),
      /* @__PURE__ */ i.jsxs("span", { children: [
        "Page ",
        e,
        " of ",
        s
      ] }),
      /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => o(e + 1), disabled: e >= s, "aria-label": "Next page", title: "Next page", children: [
        "Next ",
        /* @__PURE__ */ i.jsx($t, { size: 14 })
      ] })
    ] })
  ] });
}
const AN = [
  { value: "flowchart", label: "Flowchart", hint: "Free-form graph of connected activities." },
  { value: "sequence", label: "Sequence", hint: "Ordered list of activities that run top to bottom." }
];
function _N({ draft: e, creating: t, ai: n, suggestMetadataAction: o, onChange: r, onClose: s, onSubmit: a }) {
  const [c, u] = B(!1), [l, d] = B(""), [f, p] = B(!1), [h, g] = B(null), [m, w] = B(null), x = re(null), b = re(e);
  b.current = e;
  const y = re(r);
  y.current = r;
  const v = ie((N) => {
    const S = { ...b.current };
    N.name && (S.name = N.name), N.description && (S.description = N.description), y.current(S), g(null), w(null);
  }, []);
  J(() => {
    if (o)
      return n.onPromptResult((N) => {
        if (N.requestId !== x.current) return;
        if (x.current = null, p(!1), N.status !== "completed") {
          w(N.status === "cancelled" ? "Weaver needs more detail — continue in the assistant panel." : "Weaver couldn't generate a suggestion. Try again or fill the fields manually.");
          return;
        }
        const S = T0(N.text);
        if (!S) {
          w("Couldn't read a suggestion from Weaver's reply. See the assistant panel.");
          return;
        }
        N.autoApply ? v(S) : g(S);
      });
  }, [n, o, v]);
  const j = () => {
    if (!o) return;
    const N = o.createPrompt({ draft: b.current, intent: l });
    if (!N) return;
    const S = `wf-suggest-${Date.now()}-${Math.round(Math.random() * 1e6)}`;
    x.current = S, p(!0), g(null), w(null), n.dispatchPrompt({ ...N, requestId: S });
  };
  return /* @__PURE__ */ i.jsx("div", { className: "wf-dialog-backdrop", role: "presentation", children: /* @__PURE__ */ i.jsx("section", { className: "wf-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "workflow-create-title", children: /* @__PURE__ */ i.jsxs(
    "form",
    {
      onSubmit: (N) => {
        N.preventDefault(), a();
      },
      children: [
        /* @__PURE__ */ i.jsxs("div", { className: "wf-dialog-heading", children: [
          /* @__PURE__ */ i.jsx("h3", { id: "workflow-create-title", children: "Create Workflow" }),
          o ? /* @__PURE__ */ i.jsxs(
            "button",
            {
              type: "button",
              className: "wf-ai-action",
              "aria-expanded": c,
              onClick: () => u((N) => !N),
              title: o.description ?? o.label,
              children: [
                /* @__PURE__ */ i.jsx(it, { size: 13 }),
                " ",
                o.label
              ]
            }
          ) : null
        ] }),
        o && c ? /* @__PURE__ */ i.jsxs("div", { className: "wf-ai-suggest", role: "group", "aria-label": "Suggest name and description", children: [
          /* @__PURE__ */ i.jsxs("label", { className: "wf-form-field", children: [
            /* @__PURE__ */ i.jsx("span", { children: "What should this workflow do?" }),
            /* @__PURE__ */ i.jsx(
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
          /* @__PURE__ */ i.jsx("div", { className: "wf-ai-suggest-actions", children: /* @__PURE__ */ i.jsxs("button", { type: "button", className: "wf-ai-action", onClick: j, disabled: f, children: [
            /* @__PURE__ */ i.jsx(it, { size: 13 }),
            " ",
            f ? "Generating…" : "Generate"
          ] }) }),
          m ? /* @__PURE__ */ i.jsx("p", { className: "wf-ai-suggest-error", role: "alert", children: m }) : null,
          h ? /* @__PURE__ */ i.jsxs("div", { className: "wf-ai-suggest-preview", children: [
            h.name ? /* @__PURE__ */ i.jsx("p", { children: /* @__PURE__ */ i.jsx("strong", { children: h.name }) }) : null,
            h.description ? /* @__PURE__ */ i.jsx("p", { children: h.description }) : null,
            /* @__PURE__ */ i.jsxs("div", { className: "wf-ai-suggest-actions", children: [
              /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => v(h), children: "Apply" }),
              /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => g(null), children: "Dismiss" })
            ] })
          ] }) : null
        ] }) : null,
        /* @__PURE__ */ i.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ i.jsx("span", { children: "Display name" }),
          /* @__PURE__ */ i.jsx(
            "input",
            {
              autoFocus: !0,
              "aria-label": "Display name",
              value: e.name,
              onChange: (N) => r({ ...e, name: N.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ i.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ i.jsx("span", { children: "Description" }),
          /* @__PURE__ */ i.jsx(
            "textarea",
            {
              "aria-label": "Description",
              rows: 3,
              value: e.description,
              onChange: (N) => r({ ...e, description: N.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ i.jsxs("fieldset", { className: "wf-form-field wf-root-field", children: [
          /* @__PURE__ */ i.jsx("legend", { children: "Root activity" }),
          /* @__PURE__ */ i.jsx("div", { className: "wf-root-cards", role: "radiogroup", "aria-label": "Root activity", children: AN.map((N) => {
            const S = e.rootKind === N.value;
            return /* @__PURE__ */ i.jsxs("label", { className: "wf-root-card", "data-checked": S || void 0, children: [
              /* @__PURE__ */ i.jsx(
                "input",
                {
                  type: "radio",
                  name: "wf-root-kind",
                  "aria-label": N.label,
                  value: N.value,
                  checked: S,
                  onChange: () => r({ ...e, rootKind: N.value, rootActivityVersionId: null })
                }
              ),
              /* @__PURE__ */ i.jsx("span", { className: "wf-root-card-title", children: N.label }),
              /* @__PURE__ */ i.jsx("span", { className: "wf-root-card-hint", children: N.hint })
            ] }, N.value);
          }) })
        ] }),
        /* @__PURE__ */ i.jsxs("div", { className: "wf-dialog-actions", children: [
          /* @__PURE__ */ i.jsx("button", { type: "button", onClick: s, disabled: t, children: "Cancel" }),
          /* @__PURE__ */ i.jsx("button", { type: "submit", disabled: t || !e.name.trim(), children: t ? "Creating..." : "Create" })
        ] })
      ]
    }
  ) }) });
}
function DN({ context: e, ai: t, onOpen: n }) {
  const [o, r] = B(""), [s, a] = B("active"), [c, u] = B(1), [l, d] = B(k0), [f, p] = B("loading"), [h, g] = B(""), [m, w] = B(""), [x, b] = B([]), [y, v] = B(0), [j, N] = B(() => /* @__PURE__ */ new Set()), [S, E] = B(null), [D, L] = B(!1), [A, _] = B([]), [R, C] = B("idle"), I = re(null), k = fe(() => x.map((V) => V.id), [x]), T = Et(t, "weaver.workflows.suggest-create-metadata"), $ = Et(t, "weaver.workflows.explain-definition"), P = k.filter((V) => j.has(V)).length, F = k.length > 0 && P === k.length, W = ie(async () => {
    p("loading"), g("");
    try {
      const V = await dp(e, { search: o, state: s, page: c, pageSize: l }), ee = typeof V.totalCount == "number", ge = V.totalCount ?? V.definitions.length, we = cd(ge, l);
      if (ge > 0 && c > we) {
        u(we);
        return;
      }
      b(ee ? V.definitions : D0(V.definitions, c, l)), v(ge), p("ready");
    } catch (V) {
      g(V instanceof Error ? V.message : String(V)), p("failed");
    }
  }, [e, o, s, c, l]);
  J(() => {
    W();
  }, [W]), J(() => {
    I.current && (I.current.indeterminate = P > 0 && !F);
  }, [F, P]);
  const O = ie(async () => {
    if (!(R === "loading" || R === "ready")) {
      C("loading");
      try {
        const V = await qi(e);
        _(V.activities ?? []), C("ready");
      } catch (V) {
        C("failed"), g(V instanceof Error ? V.message : String(V));
      }
    }
  }, [R, e]), Z = () => {
    g(""), w(""), E({ name: "", description: "", rootKind: "flowchart" }), O();
  }, U = async () => {
    if (S?.name.trim()) {
      L(!0), g(""), w("");
      try {
        const V = await yp(e, {
          name: S.name.trim(),
          description: S.description.trim() || null,
          rootKind: S.rootKind,
          rootActivityVersionId: P0(S, A)
        });
        E(null), n(V.definition.id);
      } catch (V) {
        g(V instanceof Error ? V.message : String(V));
      } finally {
        L(!1);
      }
    }
  }, te = (V) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(V)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, le = async () => {
    if (x.length === 1 && c > 1) {
      u(c - 1);
      return;
    }
    await W();
  }, G = () => N(/* @__PURE__ */ new Set()), M = (V, ee) => {
    N((ge) => {
      const we = new Set(ge);
      return ee ? we.add(V) : we.delete(V), we;
    });
  }, q = (V) => {
    N((ee) => {
      const ge = new Set(ee);
      for (const we of k)
        V ? ge.add(we) : ge.delete(we);
      return ge;
    });
  }, ae = (V) => {
    a(V), u(1), G();
  }, ce = (V) => {
    r(V), u(1), G();
  }, Q = async (V) => {
    if (await Ts().confirm({ message: `Delete workflow definition "${V.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" })) {
      w(""), g("");
      try {
        await mp(e, V.id), M(V.id, !1), w(`Deleted ${V.name}`), await le();
      } catch (ee) {
        g(ee instanceof Error ? ee.message : String(ee));
      }
    }
  }, oe = async (V) => {
    w(""), g("");
    try {
      await xp(e, V.id), M(V.id, !1), w(`Restored ${V.name}`), await le();
    } catch (ee) {
      g(ee instanceof Error ? ee.message : String(ee));
    }
  }, de = async (V) => {
    if (await Ts().confirm({ message: `Permanently delete workflow definition "${V.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) {
      w(""), g("");
      try {
        await wp(e, V.id), M(V.id, !1), w(`Permanently deleted ${V.name}`), await le();
      } catch (ee) {
        g(ee instanceof Error ? ee.message : String(ee));
      }
    }
  };
  return /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ i.jsx("button", { type: "button", className: s === "active" ? "active" : "", "aria-selected": s === "active", onClick: () => ae("active"), children: "Active" }),
        /* @__PURE__ */ i.jsx("button", { type: "button", className: s === "deleted" ? "active" : "", "aria-selected": s === "deleted", onClick: () => ae("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ i.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ i.jsx(Uo, { size: 15 }),
        /* @__PURE__ */ i.jsx("input", { value: o, onChange: (V) => ce(V.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => {
        W();
      }, children: "Refresh" }),
      /* @__PURE__ */ i.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ i.jsxs("button", { type: "button", title: "Create workflow", onClick: Z, children: [
        /* @__PURE__ */ i.jsx(Wt, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ i.jsx(Fn, { message: h, title: "Couldn't load workflow definitions" }) : null,
    f !== "failed" && h ? /* @__PURE__ */ i.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ i.jsx(gt, { size: 16 }),
      " ",
      h
    ] }) : null,
    m ? /* @__PURE__ */ i.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ i.jsx(Qt, { size: 14 }),
      " ",
      m
    ] }) : null,
    j.size > 0 ? /* @__PURE__ */ i.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ i.jsxs("span", { children: [
        j.size,
        " selected"
      ] }),
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: G, children: "Clear selection" })
    ] }) : null,
    f === "loading" ? /* @__PURE__ */ i.jsx(ms, {}) : null,
    f === "ready" && x.length === 0 ? /* @__PURE__ */ i.jsx(
      xs,
      {
        icon: /* @__PURE__ */ i.jsx(Lc, { size: 22 }),
        title: `No ${s} workflow definitions`,
        description: "Create a workflow to start designing automation, or adjust your filters to see more.",
        action: /* @__PURE__ */ i.jsxs("button", { type: "button", className: "wf-link-button", onClick: Z, children: [
          /* @__PURE__ */ i.jsx(Wt, { size: 15 }),
          " Create workflow"
        ] })
      }
    ) : null,
    f === "ready" && x.length > 0 ? /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ i.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ i.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ i.jsx(
            "input",
            {
              ref: I,
              type: "checkbox",
              checked: F,
              onChange: (V) => q(V.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ i.jsx("span", { children: "Name" }),
          /* @__PURE__ */ i.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ i.jsx("span", { children: s === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ i.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ i.jsx("span", { children: "Actions" })
        ] }),
        x.map((V) => /* @__PURE__ */ i.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${V.name}`,
            "aria-selected": j.has(V.id),
            tabIndex: 0,
            onClick: () => n(V.id),
            onKeyDown: (ee) => {
              ee.currentTarget === ee.target && (ee.key !== "Enter" && ee.key !== " " || (ee.preventDefault(), n(V.id)));
            },
            children: [
              /* @__PURE__ */ i.jsx("label", { className: "wf-row-select", onClick: (ee) => ee.stopPropagation(), children: /* @__PURE__ */ i.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: j.has(V.id),
                  onChange: (ee) => M(V.id, ee.target.checked),
                  "aria-label": `Select workflow definition ${V.name}`
                }
              ) }),
              /* @__PURE__ */ i.jsxs("span", { children: [
                /* @__PURE__ */ i.jsx("strong", { children: V.name }),
                /* @__PURE__ */ i.jsx("small", { children: V.description || V.id })
              ] }),
              /* @__PURE__ */ i.jsx("span", { children: V.latestVersion ?? "No version" }),
              /* @__PURE__ */ i.jsx("span", { children: s === "deleted" ? ze(V.deletedAt) : V.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ i.jsx("span", { children: ze(V.lastModifiedAt) }),
              /* @__PURE__ */ i.jsx("span", { className: "wf-row-actions", onClick: (ee) => ee.stopPropagation(), children: s === "active" ? /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
                /* @__PURE__ */ i.jsx("button", { type: "button", onClick: (ee) => {
                  ee.stopPropagation(), n(V.id);
                }, children: "Open" }),
                /* @__PURE__ */ i.jsx("button", { type: "button", onClick: (ee) => {
                  ee.stopPropagation(), te(V.id);
                }, children: "Artifacts" }),
                $ ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => It(t, $, V), children: [
                  /* @__PURE__ */ i.jsx(it, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ i.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  Q(V);
                }, children: [
                  /* @__PURE__ */ i.jsx(jn, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
                /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => {
                  oe(V);
                }, children: [
                  /* @__PURE__ */ i.jsx(Fi, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ i.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  de(V);
                }, children: [
                  /* @__PURE__ */ i.jsx(jn, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          V.id
        ))
      ] }),
      /* @__PURE__ */ i.jsx(
        kN,
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
    S ? /* @__PURE__ */ i.jsx(
      _N,
      {
        draft: S,
        creating: D,
        ai: t,
        suggestMetadataAction: T,
        onChange: (V) => E(V),
        onClose: () => E(null),
        onSubmit: U
      }
    ) : null
  ] });
}
function TN({ activities: e, activityCatalog: t, selectedEvidenceId: n = null, onSelectEvidence: o }) {
  const r = fe(
    () => new Map(t.map((a) => [a.activityTypeKey, a])),
    [t]
  ), s = fe(() => $N(e), [e]);
  return s.length === 0 ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty wf-timeline-empty", children: "No activity executions recorded yet." }) : /* @__PURE__ */ i.jsx("ol", { className: "wf-timeline", "aria-label": "Execution timeline", children: s.map((a) => {
    const c = r.get(a.activityType), u = Qo(c), l = c ? Ce(c) : Jt(a.activityType) ?? a.activityType, d = Jt(a.activityType) ?? a.activityType, f = MN(a.startedAt ?? a.scheduledAt), p = ps(a.startedAt, a.completedAt);
    return /* @__PURE__ */ i.jsx("li", { children: /* @__PURE__ */ i.jsxs(
      "button",
      {
        type: "button",
        className: "wf-timeline-entry",
        "data-selected": a.activityExecutionId === n,
        onClick: () => o?.(a.activityExecutionId),
        children: [
          /* @__PURE__ */ i.jsx("span", { className: "wf-timeline-icon wf-activity-icon", "data-icon": u, "aria-hidden": "true", children: hs(u) }),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-timeline-body", children: [
            /* @__PURE__ */ i.jsx("strong", { title: l, children: l }),
            /* @__PURE__ */ i.jsx("small", { title: d, children: d })
          ] }),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-timeline-meta", children: [
            f ? /* @__PURE__ */ i.jsx("time", { children: f }) : null,
            p ? /* @__PURE__ */ i.jsxs("small", { children: [
              "took ",
              p
            ] }) : null
          ] }),
          /* @__PURE__ */ i.jsx(PN, { status: a.status })
        ]
      }
    ) }, a.activityExecutionId);
  }) });
}
function PN({ status: e }) {
  const t = e.toLowerCase(), n = t === "completed" ? "OK" : e;
  return /* @__PURE__ */ i.jsx("span", { className: "wf-status-badge wf-timeline-pill", "data-status": t, children: n });
}
function $N(e) {
  return e.map((t, n) => ({ activity: t, index: n })).sort((t, n) => Ec(t.activity) - Ec(n.activity) || t.index - n.index).map((t) => t.activity);
}
function Ec(e) {
  const t = e.startedAt ?? e.scheduledAt, n = t ? Date.parse(t) : Number.NaN;
  return Number.isNaN(n) ? Number.MAX_SAFE_INTEGER : n;
}
function MN(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? "" : t.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: !1 });
}
function RN({ context: e }) {
  const [t, n] = B("loading"), [o, r] = B(""), [s, a] = B(""), [c, u] = B(""), [l, d] = B([]), f = ie(async () => {
    n("loading"), r("");
    try {
      const h = await Cp(e, {
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
  return /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => {
        f();
      }, children: "Refresh" }),
      /* @__PURE__ */ i.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ i.jsx("span", { children: "Status" }),
        /* @__PURE__ */ i.jsxs("select", { "aria-label": "Workflow run status", value: s, onChange: (h) => a(h.target.value), children: [
          /* @__PURE__ */ i.jsx("option", { value: "", children: "All statuses" }),
          /* @__PURE__ */ i.jsx("option", { value: "Pending", children: "Pending" }),
          /* @__PURE__ */ i.jsx("option", { value: "Running", children: "Running" }),
          /* @__PURE__ */ i.jsx("option", { value: "Suspended", children: "Suspended" }),
          /* @__PURE__ */ i.jsx("option", { value: "Completed", children: "Completed" }),
          /* @__PURE__ */ i.jsx("option", { value: "Faulted", children: "Faulted" }),
          /* @__PURE__ */ i.jsx("option", { value: "Cancelled", children: "Cancelled" })
        ] })
      ] }),
      /* @__PURE__ */ i.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ i.jsx("span", { children: "Kind" }),
        /* @__PURE__ */ i.jsxs("select", { "aria-label": "Run Kind", value: c, onChange: (h) => u(h.target.value), children: [
          /* @__PURE__ */ i.jsx("option", { value: "", children: "All kinds" }),
          /* @__PURE__ */ i.jsx("option", { value: "TestRun", children: "Test Run" }),
          /* @__PURE__ */ i.jsx("option", { value: "PublishedRun", children: "Published Run" }),
          /* @__PURE__ */ i.jsx("option", { value: "BackgroundWeaverRun", children: "Background Weaver Run" }),
          /* @__PURE__ */ i.jsx("option", { value: "Unknown", children: "Unknown / legacy" })
        ] })
      ] })
    ] }),
    t === "failed" ? /* @__PURE__ */ i.jsx(Fn, { message: o }) : null,
    t === "loading" ? /* @__PURE__ */ i.jsx(ms, {}) : null,
    t === "ready" && l.length === 0 ? /* @__PURE__ */ i.jsx(
      xs,
      {
        icon: /* @__PURE__ */ i.jsx(Zo, { size: 22 }),
        title: "No workflow runs yet",
        description: "Run a published workflow executable to create execution history here."
      }
    ) : null,
    t === "ready" && l.length > 0 ? /* @__PURE__ */ i.jsxs("div", { className: "wf-grid wf-instance-grid", role: "table", "aria-label": "Workflow runs", children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ i.jsx("span", { children: "Run" }),
        /* @__PURE__ */ i.jsx("span", { children: "Kind" }),
        /* @__PURE__ */ i.jsx("span", { children: "Status" }),
        /* @__PURE__ */ i.jsx("span", { children: "Definition" }),
        /* @__PURE__ */ i.jsx("span", { children: "Activity" }),
        /* @__PURE__ */ i.jsx("span", { children: "Started" }),
        /* @__PURE__ */ i.jsx("span", { children: "Duration" })
      ] }),
      l.map((h) => /* @__PURE__ */ i.jsxs(
        "button",
        {
          type: "button",
          className: "wf-grid-row",
          role: "row",
          "aria-label": `Inspect workflow run ${h.workflowExecutionId}`,
          onClick: () => p(h.workflowExecutionId),
          children: [
            /* @__PURE__ */ i.jsxs("span", { children: [
              /* @__PURE__ */ i.jsx("strong", { children: h.workflowExecutionId }),
              /* @__PURE__ */ i.jsx("small", { children: h.artifactId })
            ] }),
            /* @__PURE__ */ i.jsx("span", { children: fd(h.runKind) }),
            /* @__PURE__ */ i.jsx("span", { children: /* @__PURE__ */ i.jsx(nn, { status: h.status, subStatus: h.subStatus }) }),
            /* @__PURE__ */ i.jsxs("span", { children: [
              /* @__PURE__ */ i.jsx("strong", { children: h.definitionId }),
              /* @__PURE__ */ i.jsx("small", { children: h.definitionVersionId })
            ] }),
            /* @__PURE__ */ i.jsxs("span", { children: [
              /* @__PURE__ */ i.jsxs("strong", { children: [
                h.activityCount,
                " activities"
              ] }),
              /* @__PURE__ */ i.jsxs("small", { children: [
                h.incidentCount,
                " incidents"
              ] })
            ] }),
            /* @__PURE__ */ i.jsx("span", { children: ze(h.startedAt ?? h.createdAt) }),
            /* @__PURE__ */ i.jsx("span", { children: ps(h.startedAt ?? h.createdAt, h.completedAt ?? h.updatedAt) })
          ]
        },
        h.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function LN({ context: e, ai: t, workflowExecutionId: n }) {
  const [o, r] = B("loading"), [s, a] = B(""), [c, u] = B(null), [l, d] = B(null), f = Et(t, "weaver.workflows.explain-instance"), p = ie(async () => {
    if (!n) {
      a("No workflow execution id was provided."), r("failed");
      return;
    }
    r("loading"), a("");
    try {
      const m = await Ep(e, n), [w, x] = await Promise.all([
        gp(e, m.instance.definitionVersionId).then(
          (b) => ({ definitionVersion: b, error: "" }),
          (b) => ({ definitionVersion: null, error: b instanceof Error ? b.message : String(b) })
        ),
        qi(e)
      ]);
      u({
        details: m,
        definitionVersion: w.definitionVersion,
        definitionVersionError: w.error,
        activityCatalog: x.activities
      }), d(null), r("ready");
    } catch (m) {
      u(null), a(tb(m, n)), r("failed");
    }
  }, [e, n]);
  J(() => {
    p();
  }, [p]);
  const h = () => {
    window.history.pushState({}, "", "/workflows/instances"), window.dispatchEvent(new PopStateEvent("popstate"));
  }, g = () => {
    const m = c?.details.instance.definitionId;
    m && (window.history.pushState({}, "", `/workflows/definitions?definition=${encodeURIComponent(m)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  };
  return /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: h, children: [
        /* @__PURE__ */ i.jsx(Do, { size: 14 }),
        " Runs"
      ] }),
      c?.details.instance.definitionId ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: g, children: [
        /* @__PURE__ */ i.jsx(zc, { size: 14 }),
        " Designer"
      ] }) : null,
      /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => {
        p();
      }, children: [
        /* @__PURE__ */ i.jsx(Fi, { size: 14 }),
        " Refresh"
      ] }),
      c && f ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => It(t, f, c.details), children: [
        /* @__PURE__ */ i.jsx(it, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    o === "loading" ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "Loading workflow run..." }) : null,
    o === "failed" ? /* @__PURE__ */ i.jsx(Fn, { message: s }) : null,
    o === "ready" && c ? /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-detail-workbench", children: [
      /* @__PURE__ */ i.jsx(
        zN,
        {
          definitionVersion: c.definitionVersion,
          definitionVersionError: c.definitionVersionError,
          activityCatalog: c.activityCatalog,
          details: c.details,
          selectedEvidenceId: l,
          onSelectEvidence: d
        }
      ),
      /* @__PURE__ */ i.jsx(
        VN,
        {
          ai: t,
          action: f ?? void 0,
          summary: c.details.instance,
          details: c.details,
          state: "ready",
          error: "",
          selectedEvidenceId: l,
          onSelectEvidence: d,
          activityCatalog: c.activityCatalog,
          graphNodeIds: c.definitionVersion ? Y0(c.definitionVersion, c.activityCatalog) : void 0
        }
      )
    ] }) : null
  ] });
}
function zN({ definitionVersion: e, definitionVersionError: t, activityCatalog: n, details: o, selectedEvidenceId: r, onSelectEvidence: s }) {
  const a = fe(() => {
    if (!e) return { nodes: [], edges: [] };
    const c = e.state.rootActivity;
    if (!c) return { nodes: [], edges: [] };
    const u = n.find((h) => h.activityVersionId === c.activityVersionId), l = Xi(c, u), d = l === "unsupported" ? null : Sn(c, [], n), f = l === "unsupported" ? mi(c, n, e.layout) : d ? Zc(d, n, e.layout) : mi(c, n, e.layout), p = f.nodes.map((h) => ({
      ...h,
      draggable: !1,
      connectable: !1,
      deletable: !1
    }));
    return {
      nodes: Mf(p, o.activities, o.incidents, r),
      edges: f.edges.map((h) => ({ ...h, deletable: !1 }))
    };
  }, [n, e, o, r]);
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-instance-canvas-shell", "aria-label": "Workflow run canvas", children: [
    /* @__PURE__ */ i.jsxs("header", { children: [
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("span", { children: "Definition version" }),
        /* @__PURE__ */ i.jsx("h3", { children: e ? /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
          e.definition.name,
          " ",
          /* @__PURE__ */ i.jsx("small", { children: e.version })
        ] }) : /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
          "Definition graph unavailable ",
          /* @__PURE__ */ i.jsx("small", { children: o.instance.definitionVersionId })
        ] }) })
      ] }),
      /* @__PURE__ */ i.jsx(nn, { status: o.instance.status, subStatus: o.instance.subStatus })
    ] }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ i.jsxs("div", { className: "wf-empty", children: [
        "The workflow run loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ i.jsx("small", { children: eb(t) }) : null
      ] }),
      e && a.nodes.length === 0 ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      a.nodes.length > 0 ? /* @__PURE__ */ i.jsxs(
        Zu,
        {
          nodes: a.nodes,
          edges: a.edges,
          nodeTypes: md,
          edgeTypes: xd,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (c, u) => s(u.id),
          onPaneClick: () => s(null),
          children: [
            /* @__PURE__ */ i.jsx(Ju, {}),
            /* @__PURE__ */ i.jsx(nd, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ i.jsx(ed, {})
          ]
        }
      ) : null
    ] })
  ] });
}
function VN({ ai: e, action: t, summary: n, details: o, state: r, error: s, selectedEvidenceId: a = null, onSelectEvidence: c, graphNodeIds: u, activityCatalog: l = [] }) {
  const [d, f] = B("timeline");
  if (!n)
    return /* @__PURE__ */ i.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "Select a workflow run to inspect its timeline." }) });
  const p = o?.incidents.length ?? 0, h = ON(o?.activities ?? [], a), g = (w) => {
    c?.(w), f("activity");
  }, m = [
    { id: "timeline", title: "Timeline", order: 0, icon: /* @__PURE__ */ i.jsx(Wi, { size: 14 }), render: () => null },
    { id: "activity", title: "Activity", order: 1, icon: /* @__PURE__ */ i.jsx(Tc, { size: 14 }), render: () => null },
    { id: "issues", title: p > 0 ? `Issues (${p})` : "Issues", order: 2, icon: /* @__PURE__ */ i.jsx(gt, { size: 14 }), render: () => null },
    { id: "details", title: "Details", order: 3, icon: /* @__PURE__ */ i.jsx(Hi, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ i.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Workflow run details", children: [
    /* @__PURE__ */ i.jsxs("header", { children: [
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("span", { children: "Workflow Instance ID" }),
        /* @__PURE__ */ i.jsx("h3", { children: n.workflowExecutionId })
      ] }),
      t ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => It(e, t, o ?? n), children: [
        /* @__PURE__ */ i.jsx(it, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    /* @__PURE__ */ i.jsx("div", { className: "wf-instance-tabs", children: /* @__PURE__ */ i.jsx(ko, { label: "Workflow run tabs", tabs: m, activeTabId: d, onSelect: (w) => f(w) }) }),
    r === "loading" ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "Loading run details..." }) : null,
    r === "failed" ? /* @__PURE__ */ i.jsx(Fn, { message: s }) : null,
    r === "ready" && o ? /* @__PURE__ */ i.jsx("div", { className: "wf-instance-tab-content", children: d === "timeline" ? /* @__PURE__ */ i.jsx(
      TN,
      {
        activities: o.activities,
        activityCatalog: l,
        selectedEvidenceId: a,
        onSelectEvidence: g
      }
    ) : d === "activity" ? /* @__PURE__ */ i.jsx(HN, { activity: h, activityCatalog: l }) : d === "issues" ? /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
      /* @__PURE__ */ i.jsx(WN, { incidents: o.incidents, selectedEvidenceId: a, onSelectEvidence: c }),
      /* @__PURE__ */ i.jsx(FN, { details: o, graphNodeIds: u })
    ] }) : /* @__PURE__ */ i.jsxs("dl", { className: "wf-instance-meta", children: [
      /* @__PURE__ */ i.jsx("dt", { children: "Status" }),
      /* @__PURE__ */ i.jsx("dd", { children: /* @__PURE__ */ i.jsx(nn, { status: n.status, subStatus: n.subStatus }) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Run Kind" }),
      /* @__PURE__ */ i.jsx("dd", { children: fd(n.runKind) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Artifact" }),
      /* @__PURE__ */ i.jsxs("dd", { children: [
        n.artifactId,
        " ",
        /* @__PURE__ */ i.jsx("small", { children: n.artifactVersion })
      ] }),
      /* @__PURE__ */ i.jsx("dt", { children: "Definition" }),
      /* @__PURE__ */ i.jsxs("dd", { children: [
        n.definitionId,
        " ",
        /* @__PURE__ */ i.jsx("small", { children: n.definitionVersionId })
      ] }),
      /* @__PURE__ */ i.jsx("dt", { children: "Created" }),
      /* @__PURE__ */ i.jsx("dd", { children: ze(n.createdAt) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Started" }),
      /* @__PURE__ */ i.jsx("dd", { children: ze(n.startedAt) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Completed" }),
      /* @__PURE__ */ i.jsx("dd", { children: ze(n.completedAt) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Correlation" }),
      /* @__PURE__ */ i.jsx("dd", { children: n.correlationId || "None" })
    ] }) }) : null
  ] });
}
function ON(e, t) {
  if (!t) return null;
  const n = e.find((r) => r.activityExecutionId === t);
  if (n) return n;
  const o = e.filter((r) => r.executableNodeId === t || r.authoredActivityId === t);
  return o.length > 0 ? rl(o) : null;
}
function HN({ activity: e, activityCatalog: t }) {
  if (!e)
    return /* @__PURE__ */ i.jsxs("section", { className: "wf-instance-section", children: [
      /* @__PURE__ */ i.jsx("h4", { children: "Activity" }),
      /* @__PURE__ */ i.jsx("p", { children: "No activity selected." })
    ] });
  const o = t.find((r) => r.activityTypeKey === e.activityType)?.displayName || Jt(e.activityType) || e.activityType;
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ i.jsx("h4", { children: "Activity" }),
    /* @__PURE__ */ i.jsxs("dl", { className: "wf-instance-meta", children: [
      /* @__PURE__ */ i.jsx("dt", { children: "Name" }),
      /* @__PURE__ */ i.jsx("dd", { children: o }),
      /* @__PURE__ */ i.jsx("dt", { children: "Status" }),
      /* @__PURE__ */ i.jsx("dd", { children: /* @__PURE__ */ i.jsx(nn, { status: e.status, subStatus: e.subStatus }) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Activity Execution ID" }),
      /* @__PURE__ */ i.jsx("dd", { children: e.activityExecutionId }),
      /* @__PURE__ */ i.jsx("dt", { children: "Authored Activity ID" }),
      /* @__PURE__ */ i.jsx("dd", { children: e.authoredActivityId }),
      /* @__PURE__ */ i.jsx("dt", { children: "Type" }),
      /* @__PURE__ */ i.jsxs("dd", { children: [
        Jt(e.activityType) ?? e.activityType,
        " ",
        /* @__PURE__ */ i.jsx("small", { children: e.activityTypeVersion })
      ] }),
      /* @__PURE__ */ i.jsx("dt", { children: "Started" }),
      /* @__PURE__ */ i.jsx("dd", { children: ze(e.startedAt) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Completed" }),
      /* @__PURE__ */ i.jsx("dd", { children: ze(e.completedAt) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Duration" }),
      /* @__PURE__ */ i.jsx("dd", { children: ps(e.startedAt, e.completedAt) || "Unknown" }),
      /* @__PURE__ */ i.jsx("dt", { children: "Bookmarks" }),
      /* @__PURE__ */ i.jsx("dd", { children: e.bookmarkIds.length }),
      /* @__PURE__ */ i.jsx("dt", { children: "Incidents" }),
      /* @__PURE__ */ i.jsx("dd", { children: e.incidentIds.length })
    ] })
  ] });
}
function WN({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ i.jsx("h4", { children: "Incidents" }),
    e.length === 0 ? /* @__PURE__ */ i.jsx("p", { children: "No incidents recorded." }) : null,
    e.map((o) => /* @__PURE__ */ i.jsxs(
      "button",
      {
        type: "button",
        className: "wf-instance-incident",
        "data-severity": o.severity.toLowerCase(),
        "data-selected": o.incidentId === t,
        onClick: () => n?.(o.incidentId),
        children: [
          /* @__PURE__ */ i.jsx("strong", { children: o.failureType }),
          /* @__PURE__ */ i.jsxs("span", { children: [
            o.status,
            " · ",
            o.severity
          ] }),
          /* @__PURE__ */ i.jsx("p", { children: o.message })
        ]
      },
      o.incidentId
    ))
  ] });
}
function FN({ details: e, graphNodeIds: t }) {
  if (!t) return null;
  const n = new Map(e.activities.map((s) => [s.activityExecutionId, s])), o = e.activities.filter((s) => !t.has(fc(s))), r = e.incidents.filter((s) => {
    const a = s.activityExecutionId ? n.get(s.activityExecutionId) : null, c = s.executableNodeId ?? (a ? fc(a) : "");
    return !c || !t.has(c);
  });
  return o.length === 0 && r.length === 0 ? null : /* @__PURE__ */ i.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ i.jsx("h4", { children: "Unmatched runtime evidence" }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-unmatched-list", children: [
      o.map((s) => /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ i.jsx("strong", { children: Jt(s.activityType) ?? s.activityType }),
        /* @__PURE__ */ i.jsx("small", { children: s.activityExecutionId })
      ] }, `activity-${s.activityExecutionId}`)),
      r.map((s) => /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ i.jsx("strong", { children: s.failureType }),
        /* @__PURE__ */ i.jsx("small", { children: s.incidentId })
      ] }, `incident-${s.incidentId}`))
    ] })
  ] });
}
function BN({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: o,
  workflowDesignerPanels: r
}) {
  const [s, a] = B(Ic);
  J(() => {
    const u = () => a(Ic());
    return window.addEventListener("popstate", u), () => window.removeEventListener("popstate", u);
  }, []);
  const c = (u) => {
    const l = u ? `/workflows/definitions?definition=${encodeURIComponent(u)}` : "/workflows/definitions";
    window.history.pushState({}, "", l), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return s ? /* @__PURE__ */ i.jsx(IN, { context: e, definitionId: s, ai: t, propertyEditors: n, expressionEditors: o, workflowDesignerPanels: r, onBack: () => c(null) }) : /* @__PURE__ */ i.jsx(pr, { title: "Definitions", children: /* @__PURE__ */ i.jsx(DN, { context: e, ai: t, onOpen: c }) });
}
function KN({ context: e, ai: t }) {
  const [n, o] = B(kc);
  J(() => {
    const s = () => o(kc());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const r = ie((s) => {
    const a = s?.trim() ?? "", c = new URL(window.location.href);
    a ? c.searchParams.set("definition", a) : c.searchParams.delete("definition"), o(a || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ i.jsx(pr, { title: "Executables", children: /* @__PURE__ */ i.jsx(gb, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: r }) });
}
function XN({ context: e }) {
  return /* @__PURE__ */ i.jsx(pr, { title: "Runs", children: /* @__PURE__ */ i.jsx(RN, { context: e }) });
}
function qN({ context: e, ai: t }) {
  const n = YN();
  return /* @__PURE__ */ i.jsx(pr, { title: "Run", children: /* @__PURE__ */ i.jsx(LN, { context: e, ai: t, workflowExecutionId: n }) });
}
function pr({ title: e, children: t }) {
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ i.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ i.jsxs("div", { children: [
      /* @__PURE__ */ i.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ i.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function Ic() {
  return new URLSearchParams(window.location.search).get("definition");
}
function kc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function YN() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function QN(e) {
  Qd(e.dialogs), e.featureAreas.add({
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
        component: () => /* @__PURE__ */ i.jsx(BN, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list() })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ i.jsx(KN, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => /* @__PURE__ */ i.jsx(XN, { context: e.backend })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => /* @__PURE__ */ i.jsx(qN, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => /* @__PURE__ */ i.jsx(qp, { context: e.backend })
      }
    ]
  });
}
export {
  Z0 as isConnectEndOverExistingWorkflowNode,
  QN as register,
  G0 as resolveConnectEndSource
};
