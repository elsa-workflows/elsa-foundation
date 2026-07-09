import Ue, { useMemo as ue, useState as K, useEffect as te, memo as je, forwardRef as Kc, useRef as ae, useCallback as se, useContext as Kn, createContext as qo, useLayoutEffect as nf, lazy as rf, Suspense as of, useReducer as sf, useId as Xc } from "react";
import { Boxes as Xn, Zap as af, Play as Gt, Terminal as cf, ListTree as Yo, GitBranch as qc, ListChecks as lf, Save as Yc, EyeOff as eo, Shield as Vs, AlertTriangle as zi, SlidersHorizontal as Uo, Activity as Uc, Search as Ji, X as Zo, Check as ln, Plus as Jt, Trash2 as _n, AlertCircle as mt, Wrench as uf, Copy as df, Sparkles as ct, RotateCcw as Go, ChevronDown as Zc, ChevronRight as bt, GripVertical as Gc, Maximize2 as Dn, ChevronUp as ff, Package as Jc, Undo2 as pf, Redo2 as hf, Network as gf, Download as yf, ChevronLeft as Tn, Minimize2 as bo, Workflow as Qc, Code2 as mf } from "lucide-react";
import { useQuery as Jo, useQueryClient as xf, useMutation as wf } from "@tanstack/react-query";
import { useTablistKeyboard as vf } from "@elsa-workflows/studio-ui";
function bf(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var to = { exports: {} }, yn = {};
var Os;
function Nf() {
  if (Os) return yn;
  Os = 1;
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
var Hs;
function jf() {
  return Hs || (Hs = 1, to.exports = Nf()), to.exports;
}
var o = jf();
let el;
function Sf(e) {
  el = e;
}
function Ws() {
  return el;
}
const Cf = "String", Ef = "singleline";
function kf(e) {
  return e === "Single" || e === "Array" || e === "List" || e === "HashSet";
}
function Qo(e, t = "Single") {
  return { alias: (e ?? "").trim() || Cf, collectionKind: t };
}
function es(e) {
  const t = e.type ?? e.Type;
  if ($n(t))
    return { alias: typeof t.alias == "string" ? t.alias : typeof t.typeName == "string" ? t.typeName : "", collectionKind: kf(t.collectionKind) ? t.collectionKind : "Single" };
  const n = e.typeInformation ?? e.TypeInformation;
  return $n(n) ? { alias: typeof n.typeName == "string" ? n.typeName : "", collectionKind: Bs(e) ? "Array" : "Single" } : { alias: typeof t == "string" ? t : "", collectionKind: Bs(e) ? "Array" : "Single" };
}
function Bs(e) {
  const t = e.isArray ?? e.IsArray;
  return t === !0 || t === "true";
}
function Fs(e) {
  return (e.alias ?? e.typeName ?? "").trim();
}
function Qi() {
  return typeof crypto < "u" && typeof crypto.randomUUID == "function" ? crypto.randomUUID() : `id-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
}
function If(e, t) {
  const n = new Set(t);
  let i = 1, r = `${e}${i}`;
  for (; n.has(r); )
    i += 1, r = `${e}${i}`;
  return r;
}
function Af(e) {
  return {
    referenceKey: Qi(),
    name: e.name,
    type: Qo(e.alias),
    storageDriverType: e.storageDriver?.trim() || null,
    default: null
  };
}
function _f(e, t) {
  return { ...e, ...t };
}
function Df(e) {
  return e === "" ? null : { value: e, expressionType: "Literal" };
}
function Tf(e) {
  return e == null || e.value == null ? "" : typeof e.value == "object" ? JSON.stringify(e.value) : String(e.value);
}
function $f(e) {
  return {
    referenceKey: Qi(),
    name: e.name,
    type: Qo(e.alias),
    displayName: e.name,
    description: "",
    category: "",
    uiHint: Ef,
    storageDriverType: e.storageDriver ?? null,
    isRequired: !1
  };
}
function Pf(e, t) {
  return { ...e, ...t };
}
function Mf(e) {
  return {
    referenceKey: Qi(),
    name: e.name,
    type: Qo(e.alias),
    displayName: e.name,
    description: "",
    category: ""
  };
}
function Rf(e, t) {
  return { ...e, ...t };
}
function zf(e) {
  const t = e.split(",")[0].trim();
  return (t.split(".").pop() ?? t).split("`")[0];
}
function tl(e, t) {
  return e && !e.includes(",") && !e.includes(".") ? e : zf(e || t);
}
function Lf(e, t) {
  return tl(e, t).replace(/StorageDriver$/, "");
}
function $n(e) {
  return !!e && typeof e == "object" && !Array.isArray(e);
}
function st(e, t) {
  for (const n of t) {
    const i = e[n];
    if (i != null && typeof i != "object") return String(i);
  }
  return "";
}
const Vf = ["name", "Name"], ts = ["name", "Name"], Of = ["storageDriverType", "StorageDriverType"], nl = ["referenceKey", "ReferenceKey"], Hf = /* @__PURE__ */ new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);
function qn(e) {
  return il(e, Kf);
}
function er(e) {
  return il(e, Xf);
}
function il(e, t) {
  if (!e) return e;
  const n = { ...e };
  return e.rootActivity && (n.rootActivity = No(e.rootActivity, t)), Array.isArray(e.variables) && (n.variables = Ei(e.variables, jo)), Array.isArray(e.inputs) && (n.inputs = Ei(e.inputs, jo)), Array.isArray(e.outputs) && (n.outputs = Ei(e.outputs, (i) => rl(i, !1))), n;
}
function No(e, t) {
  const n = t(e), i = n.structure;
  if (!i || !et(i.payload)) return n;
  let r = !1;
  const s = { ...i.payload };
  for (const [a, c] of Object.entries(i.payload))
    Xs(c) ? (s[a] = No(c, t), r = !0) : Array.isArray(c) && c.length > 0 && c.every(Xs) && (s[a] = c.map((u) => No(u, t)), r = !0);
  return Array.isArray(i.payload.variables) && i.payload.variables.length > 0 && (s.variables = Ei(i.payload.variables, jo), r = !0), r ? { ...n, structure: { ...i, payload: s } } : n;
}
function Ei(e, t) {
  return e.map((n) => et(n) && !Array.isArray(n) ? t(n) : n);
}
function jo(e) {
  return rl(e, !0);
}
const Wf = [
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
function rl(e, t) {
  const n = Bf(e, Wf);
  return st(e, nl).trim() || (n.referenceKey = Qi()), n.type = es(e), t && (n.storageDriverType = Ff(e.storageDriverType ?? e.StorageDriverType)), n;
}
function Bf(e, t) {
  const n = new Set(t), i = {};
  for (const [r, s] of Object.entries(e))
    n.has(r) || (i[r] = s);
  return i;
}
function Ff(e) {
  if (typeof e == "string") return e.trim() ? e : null;
  if (et(e)) {
    const t = typeof e.typeName == "string" ? e.typeName : "";
    if (!t) return null;
    const n = typeof e.namespace == "string" ? e.namespace : "";
    return n ? `${n}.${t}` : t;
  }
  return null;
}
function Kf(e) {
  const t = [], n = {};
  for (const [r, s] of Object.entries(e))
    Hf.has(r) || (Zf(s) ? t.push({
      referenceKey: qf(r),
      value: Uf(s.expression)
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
function Xf(e) {
  const t = Array.isArray(e.inputs) ? e.inputs : [], n = {};
  for (const i of t) {
    if (!et(i) || typeof i.referenceKey != "string") continue;
    const r = et(i.value) ? i.value : {};
    n[Yf(i.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof r.expressionType == "string" ? r.expressionType : "Literal",
        value: r.value ?? ""
      }
    };
  }
  return { ...e, ...n, inputs: [] };
}
function qf(e) {
  return e && e.charAt(0).toUpperCase() + e.slice(1);
}
function Yf(e) {
  return e && e.charAt(0).toLowerCase() + e.slice(1);
}
function Uf(e) {
  const t = e.type || "Literal";
  return t === "Variable" && et(e.value) ? { value: e.value, expressionType: t } : t === "Literal" && et(e.value) ? { value: Ks(e.value), expressionType: "Object" } : { value: Ks(e.value), expressionType: t };
}
function Ks(e) {
  return e == null ? null : typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function Zf(e) {
  if (!et(e) || Array.isArray(e)) return !1;
  const t = e.expression;
  return et(t) && typeof t.type == "string";
}
function Xs(e) {
  return et(e) && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function et(e) {
  return typeof e == "object" && e !== null;
}
const Yn = "elsa.sequence.structure", un = "elsa.flowchart.structure";
function ol(e, t, n) {
  if (!e) return null;
  let i = e;
  for (const r of t) {
    const s = Jf(i, r.ownerNodeId, n);
    if (!s) return null;
    i = s;
  }
  return i;
}
function Gf(e, t, n = (r) => r.nodeId, i) {
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
function sl(e, t, n) {
  const i = Me(e, n), r = t.at(-1);
  return r ? i.find((s) => s.id === r.slotId) ?? null : i[0] ?? null;
}
function Qt(e, t, n) {
  const i = ol(e, t, n);
  if (!i) return null;
  const r = sl(i, t, n);
  return r ? { owner: i, slot: r } : null;
}
function al(e, t, n) {
  for (const i of Me(e, n)) {
    const r = i.activities.find((s) => s.nodeId === t);
    if (r) return { slot: i, child: r };
  }
  return null;
}
function Jf(e, t, n) {
  return al(e, t, n)?.child ?? null;
}
function Me(e, t) {
  const n = e.structure;
  if (!n || !n.payload || typeof n.payload != "object") return [];
  const i = n.payload, r = ns(cl(e, t));
  if (r?.kind === n.kind) return tp(n, r);
  const s = Np(n), a = vn(i.activities);
  return a ? [{
    id: `${n.kind}:activities`,
    label: jp(n),
    property: "activities",
    cardinality: "many",
    mode: s,
    activities: a
  }] : Object.entries(i).filter(([, c]) => vn(c) || Vi(c)).map(([c, u]) => ({
    id: `${n.kind}:${c}`,
    label: Cp(c),
    property: c,
    cardinality: vn(u) ? "many" : "single",
    mode: "generic",
    activities: vn(u) ?? (Vi(u) ? [u] : [])
  }));
}
function ns(e) {
  for (const t of e?.designFacets ?? []) {
    if (!tt(t)) continue;
    const n = typeof t.kind == "string" ? t.kind : "", i = typeof t.schemaVersion == "string" ? t.schemaVersion : "";
    if (!n || !i || !tt(t.payload)) continue;
    const r = Qf(t.payload);
    if (r) return { kind: n, schemaVersion: i, payload: r };
  }
  return null;
}
function Qf(e) {
  const t = e.mode;
  if (t !== "sequence" && t !== "flowchart" && t !== "generic" || typeof e.supportsScopedVariables != "boolean" || !Array.isArray(e.slots) || !tt(e.initialPayload)) return null;
  const n = e.slots.map(ep).filter((i) => i !== null);
  return {
    mode: t,
    supportsScopedVariables: e.supportsScopedVariables,
    slots: n,
    initialPayload: e.initialPayload
  };
}
function ep(e) {
  if (!tt(e)) return null;
  const t = typeof e.name == "string" ? e.name : "", n = typeof e.property == "string" ? e.property : "", i = typeof e.displayName == "string" ? e.displayName : "", r = e.cardinality;
  return !t || !n || !i || r !== "single" && r !== "many" ? null : {
    name: t,
    property: n,
    displayName: i,
    cardinality: r,
    collectionProperty: Wt(e.collectionProperty),
    childProperty: Wt(e.childProperty),
    labelProperty: Wt(e.labelProperty),
    slotNameTemplate: Wt(e.slotNameTemplate)
  };
}
function tp(e, t) {
  return t.payload.slots.flatMap((n) => {
    if (n.collectionProperty && n.childProperty) {
      const i = n.childProperty, r = e.payload[n.collectionProperty];
      return Array.isArray(r) ? r.flatMap((s, a) => {
        if (!tt(s)) return [];
        const c = n.labelProperty ? Wt(s[n.labelProperty]) : void 0;
        return [qs(e.kind, n, s[i], t.payload.mode, a, c)];
      }) : [];
    }
    return [qs(e.kind, n, e.payload[n.property], t.payload.mode)];
  });
}
function qs(e, t, n, i, r, s) {
  const a = r === void 0 ? "" : `:${t.collectionProperty}:${t.childProperty}:${r}`;
  return {
    id: `${e}:${t.property}${a}`,
    label: r === void 0 ? t.displayName : ip(t, r, s),
    property: t.property,
    cardinality: t.cardinality,
    mode: i,
    activities: np(n, t.cardinality),
    collectionProperty: t.collectionProperty,
    childProperty: t.childProperty,
    labelProperty: t.labelProperty,
    slotNameTemplate: t.slotNameTemplate,
    collectionIndex: r,
    collectionItemLabel: s
  };
}
function np(e, t) {
  return t === "many" ? vn(e) ?? [] : Vi(e) ? [e] : [];
}
function ip(e, t, n) {
  return e.slotNameTemplate ? e.slotNameTemplate.replaceAll("{name}", e.name).replaceAll("{displayName}", e.displayName).replaceAll("{label}", n ?? String(t + 1)).replaceAll("{index}", String(t + 1)) : n ? `${e.displayName}: ${n}` : `${e.displayName} ${t + 1}`;
}
function cl(e, t) {
  if (t)
    return t instanceof Map ? t.get(e.activityVersionId) : Array.isArray(t) ? t.find((n) => n.activityVersionId === e.activityVersionId) : t.activityVersionId === e.activityVersionId ? t : void 0;
}
function rp(e, t) {
  if (t.labelProperty && t.collectionItemLabel) {
    const n = e.map((i, r) => tt(i) && Wt(i[t.labelProperty]) === t.collectionItemLabel ? r : -1).filter((i) => i >= 0);
    if (n.length === 1) return n[0];
  }
  return typeof t.collectionIndex == "number" && t.collectionIndex >= 0 && t.collectionIndex < e.length ? t.collectionIndex : -1;
}
function ll(e, t, n) {
  const i = new Map(t.map((a) => [a.activityVersionId, a])), r = new Map(n.map((a) => [a.nodeId, a])), s = e.slot.activities.map((a, c) => {
    const u = i.get(a.activityVersionId), l = r.get(a.nodeId) ?? Sp(e.slot.mode, c);
    return fl(a, u, { x: l.x, y: l.y });
  });
  return {
    nodes: s,
    edges: e.slot.mode === "flowchart" ? hl(e.owner) : gp(e.slot, s)
  };
}
function So(e, t, n) {
  const i = t.find((s) => s.activityVersionId === e.activityVersionId), r = n.find((s) => s.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [fl(e, i, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function op(e, t, n, i = null) {
  const r = new Map(t.map((c) => [c.activityExecutionId, c])), s = Zs(t, (c) => c.authoredActivityId || c.executableNodeId), a = Zs(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? r.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const u = s.get(c.id) ?? [], l = a.get(c.id) ?? [];
    if (u.length === 0 && l.length === 0) return c;
    const d = yl(u), f = i === c.id || u.some((h) => h.activityExecutionId === i) || l.some((h) => h.incidentId === i), p = {
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
function is(e, t) {
  if (e?.structure?.kind === un || dp(t)) return "flowchart";
  if (e?.structure?.kind === Yn || fp(t)) return "sequence";
  if (e) {
    const n = Me(e, t)[0];
    if (n) return n.mode === "flowchart" ? "flowchart" : "sequence";
  }
  return "unsupported";
}
function Ys(e, t, n, i) {
  return rs(e, t, i, (r) => {
    const s = sl(r, t, i);
    return s ? Un(r, s, n) : r;
  });
}
function sp(e, t, n, i) {
  return t.length === 0 ? n : rs(e, t, i, () => n);
}
function rs(e, t, n, i) {
  if (t.length === 0) return i(e);
  const [r, ...s] = t, a = al(e, r.ownerNodeId, n);
  if (!a) return e;
  const c = rs(a.child, s, n, i);
  if (c === a.child) return e;
  const u = a.slot.activities.map((l) => l.nodeId === r.ownerNodeId ? c : l);
  return Un(e, a.slot, u);
}
function ul(e, t, n, i) {
  if (e.nodeId === t) return n(e);
  const r = Me(e, i);
  if (r.length === 0) return e;
  let s = !1, a = e;
  for (const c of r) {
    const u = c.activities.map((l) => {
      const d = ul(l, t, n, i);
      return d !== l && (s = !0), d;
    });
    s && (a = Un(a, c, u));
  }
  return s ? a : e;
}
function Un(e, t, n) {
  if (!e.structure) return e;
  const i = t.cardinality === "single" ? n[0] ?? null : n;
  if (t.collectionProperty && t.childProperty) {
    const r = e.structure.payload[t.collectionProperty];
    if (!Array.isArray(r)) return e;
    const s = rp(r, t);
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
function ap(e, t, n, i = []) {
  const r = new Map(e.slot.activities.map((a) => [a.nodeId, a]));
  for (const a of i)
    r.set(a.nodeId, a);
  const s = t.map((a) => r.get(a.id)).filter((a) => !!a);
  return e.slot.mode === "sequence" && s.sort((a, c) => {
    const u = t.find((d) => d.id === a.nodeId), l = t.find((d) => d.id === c.nodeId);
    return (u?.position.x ?? 0) - (l?.position.x ?? 0);
  }), Un(e.owner, e.slot, s);
}
function cp(e, t) {
  return {
    ...e,
    structure: hp(e.structure, t)
  };
}
function lp(e, t) {
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
function Co(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: pl(e)
  };
}
function dl(e, t) {
  if (!e) return null;
  const n = cl(e, t), i = e.structure ?? (n ? pl(n) : null);
  let r = i === e.structure ? e : { ...e, structure: i };
  const s = Me(r, t);
  for (const a of s) {
    const c = a.activities.map((u) => dl(u, t) ?? u);
    c.some((u, l) => u !== a.activities[l]) && (r = Un(r, a, c));
  }
  return r;
}
function Se(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? pp(t) : n;
}
function fl(e, t, n, i = {}) {
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
      icon: dn(t),
      childSlots: Me(e, t),
      acceptsInbound: yp(e, t),
      sourcePorts: i.suppressFlowPorts ? [] : gl(e, t),
      suppressFlowPorts: i.suppressFlowPorts
    }
  };
}
function dn(e) {
  if (!e) return "activity";
  const t = up(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), i = Se(e).toLowerCase(), r = e.category?.toLowerCase() ?? "", s = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || i === "flowchart" ? "flowchart" : n.endsWith(".sequence") || i === "sequence" ? "sequence" : n.includes("writeline") || i.includes("write line") ? "terminal" : r.includes("runtime") ? "runtime" : s === "trigger" ? "trigger" : "activity";
}
function up(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function dp(e) {
  return !!e && (Se(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function fp(e) {
  return !!e && (Se(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function pp(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function pl(e) {
  const t = ns(e);
  return t ? {
    kind: t.kind,
    schemaVersion: t.schemaVersion,
    payload: vp(t.payload.initialPayload)
  } : e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: Yn,
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
function hp(e, t) {
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
function gp(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, i) => ({
    id: `sequence-${n.id}-${t[i + 1].id}`,
    source: n.id,
    target: t[i + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function hl(e) {
  if (e.structure?.kind !== un) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, i) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, s = n.target;
    if (!r?.nodeId || !s?.nodeId) return null;
    const a = Array.isArray(n.vertices) ? n.vertices.filter(bp) : [];
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
function gl(e, t) {
  const n = Us(e.cases);
  if (xp(e, t) && n.length > 0)
    return [...n.map((s) => ({ name: s, displayName: s })), { name: "Default", displayName: "Default" }];
  const i = [
    ...ki(t?.designFacets),
    ...ki(t?.ports),
    ...ki(t?.outputs)
  ];
  if (i.length > 0) return wp(i);
  const r = Us(e.outcomes);
  return r.length > 0 ? r.map((s) => ({ name: s, displayName: s })) : [{ name: "Done", displayName: "Done" }];
}
function yp(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function Li(e, t, n, i) {
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
function mp(e, t, n) {
  const i = Li(t.source, n, t.sourceHandle ?? "Done", void 0), r = Li(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((s) => s.id !== t.id).concat(i, r);
}
function vn(e) {
  return Array.isArray(e) ? e.filter(Vi) : null;
}
function xp(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, i = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || i === "FlowSwitch";
}
function ki(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!tt(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...ki(n.ports));
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
function wp(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function Us(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function Wt(e) {
  return typeof e == "string" && e.length > 0 ? e : void 0;
}
function vp(e) {
  return JSON.parse(JSON.stringify(e));
}
function Zs(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e) {
    const r = t(i);
    r && n.set(r, [...n.get(r) ?? [], i]);
  }
  return n;
}
function yl(e) {
  return [...e].sort((t, n) => Gs(n).localeCompare(Gs(t)))[0];
}
function Gs(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function bp(e) {
  return tt(e) && typeof e.x == "number" && typeof e.y == "number";
}
function tt(e) {
  return typeof e == "object" && e !== null;
}
function Vi(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Np(e) {
  return e.kind === Yn ? "sequence" : e.kind === un ? "flowchart" : "generic";
}
function jp(e) {
  return e.kind === Yn || e.kind === un, "Activities";
}
function Sp(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function Cp(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Oi = "workflow", Ep = /* @__PURE__ */ new Set([Yn, un]);
function kp(e, t) {
  const n = e?.structure?.kind;
  if (!n) return !1;
  if (Ep.has(n)) return !0;
  const i = t?.activityVersionId === e?.activityVersionId ? ns(t) : null;
  return i?.kind === n && i.payload.supportsScopedVariables;
}
function ml(e) {
  const t = e?.structure?.payload?.variables;
  return Array.isArray(t) ? t.filter($n) : [];
}
function Ip(e, t) {
  return e.structure ? {
    ...e,
    structure: {
      ...e.structure,
      payload: { ...e.structure.payload, variables: t }
    }
  } : e;
}
function Ap(e, t) {
  return {
    referenceKey: e,
    declaringScopeId: t && t !== Oi ? t : Oi
  };
}
function xl(e) {
  if (e && typeof e == "object") {
    const t = e;
    return typeof t.referenceKey == "string" && t.referenceKey ? { referenceKey: t.referenceKey, declaringScopeId: typeof t.declaringScopeId == "string" ? t.declaringScopeId : null } : null;
  }
  if (typeof e == "string") {
    const t = e.trim();
    if (t.startsWith("{"))
      try {
        return xl(JSON.parse(t));
      } catch {
      }
    return t ? { referenceKey: t, declaringScopeId: null } : null;
  }
  return null;
}
function _p(e, t) {
  if (!e) return "";
  const n = [`workflow:${Js(e.variables)}`], i = (r) => {
    const s = Me(r, t), a = s.flatMap((c) => c.activities.map((u) => u.nodeId));
    n.push(`${r.nodeId}:${Js(ml(r))}>${a.join(",")}`), s.forEach((c) => c.activities.forEach(i));
  };
  return e.rootActivity && i(e.rootActivity), n.join(";");
}
function Js(e) {
  return (e ?? []).map((t) => `${t.referenceKey}=${t.name}`).join(",");
}
function Dp(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e ?? [])
    i.scopeId === t && n.set(i.referenceKey, `Shadows "${i.name}" declared in an outer scope.`);
  return n;
}
const Ne = "/_elsa/workflow-management", Tp = "/publishing", qt = {
  activities: ["workflows", "activities"],
  activityAvailabilitySettings: ["workflows", "activity-availability", "settings"],
  activityAvailabilityDiagnostics: ["workflows", "activity-availability", "diagnostics"]
};
function $p(e) {
  return Jo({
    queryKey: qt.activities,
    queryFn: () => tr(e),
    staleTime: 6e4
  });
}
function Pp(e) {
  return Jo({
    queryKey: qt.activityAvailabilitySettings,
    queryFn: () => th(e)
  });
}
function Mp(e) {
  return Jo({
    queryKey: qt.activityAvailabilityDiagnostics,
    queryFn: () => bl(e)
  });
}
function Rp(e) {
  const t = xf();
  return wf({
    mutationFn: (n) => nh(e, n),
    onSuccess: (n) => {
      t.setQueryData(qt.activityAvailabilitySettings, n), t.invalidateQueries({ queryKey: qt.activityAvailabilityDiagnostics }), t.invalidateQueries({ queryKey: qt.activities });
    }
  });
}
async function zp(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), i = t.search.trim();
  return i && n.set("search", i), e.http.getJson(`${Ne}/definitions?${n.toString()}`);
}
async function Lp(e, t) {
  const n = await e.http.getJson(`${Ne}/definitions/${encodeURIComponent(t)}`);
  return n.draft ? { ...n, draft: { ...n.draft, state: er(n.draft.state) } } : n;
}
async function Vp(e, t, n) {
  const i = await e.http.postJson(
    `${Ne}/design/scoped-variables/analyze`,
    { state: qn(t), nodeId: n }
  );
  return {
    visibleVariables: Array.isArray(i?.visibleVariables) ? i.visibleVariables : [],
    shadowingWarnings: Array.isArray(i?.shadowingWarnings) ? i.shadowingWarnings : []
  };
}
const no = (e) => ({ visibleVariables: [], shadowingWarnings: [], status: e });
function Op(e, t, n, i) {
  const r = ue(() => _p(t, i), [i, t]), [s, a] = K(() => no("loading"));
  return te(() => {
    if (!t) {
      a(no("unavailable"));
      return;
    }
    let c = !1;
    return a((u) => ({ ...u, status: "loading" })), Vp(e, t, n).then(
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
async function Hp(e, t) {
  const n = await e.http.getJson(`${Ne}/versions/${encodeURIComponent(t)}`);
  return { ...n, state: er(n.state) };
}
async function Wp(e, t) {
  return e.http.postJson(`${Ne}/definitions`, t);
}
async function Bp(e, t) {
  await e.http.deleteJson(`${Ne}/definitions/${encodeURIComponent(t)}`);
}
async function Fp(e, t) {
  await e.http.postJson(`${Ne}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function Kp(e, t) {
  await e.http.deleteJson(`${Ne}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function Xp(e, t, n) {
  return e.http.requestJson(
    `${Ne}/definitions/${encodeURIComponent(t)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(n)
    }
  );
}
async function qp(e, t) {
  const n = await e.http.putJson(
    `${Ne}/drafts/${encodeURIComponent(t.id)}`,
    { state: qn(t.state), layout: t.layout }
  );
  return { ...n, state: er(n.state) };
}
async function Yp(e, t) {
  return e.http.postJson(`${Ne}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function Up(e, t) {
  return e.http.postJson(`${Ne}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function Zp(e, t) {
  const n = { ...t, state: qn(t.state) };
  try {
    return await e.http.postJson(`${Tp}/workflows/drafts/test-runs`, n);
  } catch (i) {
    const r = lh(i);
    if (r) return r;
    throw i;
  }
}
async function wl(e, t) {
  return e.http.postJson(`${Ne}/executables/${encodeURIComponent(t)}/run`, {});
}
async function vl(e) {
  const t = [`${Ne}/executables`, "/_demo/workflows/executables"], n = [];
  for (const i of t)
    try {
      const r = await e.http.getJson(i);
      return Gp(r);
    } catch (r) {
      n.push(r);
    }
  if (n.length > 0 && n.every(Qs)) return [];
  throw n.find((i) => !Qs(i)) ?? n[n.length - 1] ?? new Error("Workflow executables could not be loaded.");
}
function Gp(e) {
  return Array.isArray(e) ? e : e.executables ?? [];
}
function Qs(e) {
  if (!(e instanceof Error)) return !1;
  const t = e.message.toLowerCase();
  return /\b404\b/.test(t) || t.includes("not found");
}
async function Jp(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.runKind && n.set("runKind", t.runKind), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const i = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${i ? `?${i}` : ""}`);
}
async function Qp(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function eh(e, t, n) {
  return e.http.getJson(
    `/runtime/workflows/instances/${encodeURIComponent(t)}/activity-executions/${encodeURIComponent(n)}`
  );
}
async function tr(e) {
  return e.http.getJson(`${Ne}/activities`);
}
async function th(e) {
  return e.http.getJson(`${Ne}/activities/availability/settings`);
}
async function nh(e, t) {
  return e.http.putJson(`${Ne}/activities/availability/settings`, t);
}
async function bl(e) {
  return e.http.getJson(`${Ne}/activities/availability/diagnostics`);
}
async function ih(e) {
  const t = await nr(e, [
    `${Ne}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? ea(t) : ea(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function rh(e) {
  const t = await nr(e, [
    `${Ne}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : Ii;
}
async function oh(e) {
  const t = await nr(e, [
    `${Ne}/descriptors/variables`,
    "/descriptors/variables"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((i) => sh(i));
}
function sh(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = typeof t.alias == "string" && t.alias.length > 0, i = typeof t.typeName == "string" && t.typeName.length > 0;
  return n || i;
}
async function ah(e) {
  const t = await nr(e, [
    `${Ne}/descriptors/storage-drivers`,
    "/descriptors/storage-drivers"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((i) => ch(i));
}
function ch(e) {
  return !!e && typeof e == "object" && typeof e.typeName == "string" && e.typeName.length > 0;
}
async function nr(e, t) {
  let n;
  for (const i of t)
    try {
      return await e.http.getJson(i);
    } catch (r) {
      n = r;
    }
  throw n;
}
function ea(e) {
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
function lh(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = ta(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return ta(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function ta(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const Ii = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function Nl(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e) {
    const r = t(i)?.trim() || "Uncategorized", s = n.get(r);
    s ? s.push(i) : n.set(r, [i]);
  }
  return [...n.entries()].map(([i, r]) => ({ category: i, items: r })).sort((i, r) => i.category.localeCompare(r.category));
}
const uh = [
  "Available",
  "BlockedByHostBaseline",
  "HiddenByManagementSettings",
  "RemovedFromCatalog",
  "UnresolvedReference"
], dh = {
  Available: "Available",
  BlockedByHostBaseline: "Host blocked",
  HiddenByManagementSettings: "Management hidden",
  RemovedFromCatalog: "Removed",
  UnresolvedReference: "Unresolved"
}, jl = {
  Catalog: "Catalog",
  HostBaseline: "Host baseline",
  ManagementSettings: "Management settings"
}, fh = Object.keys(jl);
function ph(e) {
  const t = typeof e == "number" ? fh[e] : e;
  return t && jl[t] || t?.toString() || "";
}
function Ct(e) {
  return typeof e == "string" ? e : typeof e == "number" ? uh[e] ?? "Available" : "Available";
}
function Pn(e) {
  const t = Ct(e);
  return dh[t] ?? t;
}
function Sl(e) {
  return Ct(e).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Cl(e) {
  return Ct(e) !== "Available";
}
function El(e) {
  return Ct(e.state) === "BlockedByHostBaseline";
}
function hh(e) {
  return e === "Only" || e === 1 ? "Only" : "AllExcept";
}
function gh(e) {
  return e === "Only" ? 1 : 0;
}
function Eo(e) {
  const t = e?.rules;
  return {
    mode: hh(e?.mode),
    activityTypes: t?.activityTypes ?? [],
    sets: t?.sets ?? []
  };
}
function yh(e) {
  return e.referenceKind === 0 || e.referenceKind === "ActivityType";
}
function bn(e) {
  return e.activityTypeKey ?? e.activityDefinitionId ?? "";
}
function mh(e) {
  return [...e?.items ?? []].filter(yh).filter((t) => t.activityTypeKey && t.activityDefinitionId).sort((t, n) => Mn(t).localeCompare(Mn(n)));
}
function xh(e) {
  return [...e?.items ?? []].filter((t) => {
    const n = Ct(t.state);
    return n === "RemovedFromCatalog" || n === "UnresolvedReference";
  }).sort((t, n) => (t.referenceName ?? "").localeCompare(n.referenceName ?? ""));
}
function wh(e) {
  return Nl(e, (t) => t.category).map((t) => ({ category: t.category, entries: t.items }));
}
function vh(e, t, n) {
  const i = e[t].includes(n);
  return e.mode === "Only" ? i : !i;
}
function io(e, t, n, i) {
  const r = e.mode === "Only" ? i : !i;
  return e[t].includes(n) === r ? e : { ...e, [t]: jh(e[t], n) };
}
function bh(e, t, n) {
  const i = new Set(n.activityTypes), r = new Set(n.sets), s = /* @__PURE__ */ new Map();
  for (const c of t)
    if (r.has(c.name))
      for (const u of c.activityTypeKeys ?? [])
        s.has(u) || s.set(u, c.name);
  const a = /* @__PURE__ */ new Map();
  for (const c of e) {
    const u = bn(c);
    if (El(c)) {
      a.set(u, { enabled: !1, lockedBy: "host-baseline" });
      continue;
    }
    const l = s.get(u), d = i.has(u) || l !== void 0, f = n.mode === "Only" ? d : !d;
    a.set(u, l ? { enabled: f, lockedBy: "set-rule", governingSet: l } : { enabled: f, lockedBy: null });
  }
  return a;
}
function Nh(e, t) {
  const n = Eo(t), i = (r) => [...r].sort((s, a) => s.localeCompare(a)).join(`
`);
  return e.mode !== n.mode || i(e.activityTypes) !== i(n.activityTypes) || i(e.sets) !== i(n.sets);
}
function jh(e, t) {
  return e.includes(t) ? e.filter((n) => n !== t) : [...e, t].sort((n, i) => n.localeCompare(i));
}
function Mn(e) {
  const t = e?.displayName?.trim();
  if (t && t.toLowerCase() !== "activity") return t;
  const n = Ch(e?.activityTypeKey);
  return Sh(n) || t || e?.activityTypeKey || "Activity";
}
function na(e) {
  const t = kl(e?.activityTypeKey);
  if (t.length === 0) return "";
  const n = t[t.length - 1], i = t[t.length - 2];
  return n === "Activity" && i ? `${i}.${n}` : n;
}
function ia(e) {
  const t = e.description?.trim();
  if (t) return t;
  const n = e.reason?.trim();
  return !n || !Cl(e.state) ? "" : n;
}
function Sh(e) {
  return e.replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/\bHttp\b/g, "HTTP").replace(/\bJson\b/g, "JSON").replace(/\bJava Script\b/g, "JavaScript").replace(/\bUrl\b/g, "URL").replace(/\bXml\b/g, "XML").trim();
}
function Ch(e) {
  const t = kl(e), n = t[t.length - 1] ?? "";
  return n === "Activity" && t.length > 1 ? t[t.length - 2] : n;
}
function kl(e) {
  return e?.split(".").filter(Boolean) ?? [];
}
function ra(e) {
  return (e ?? []).flatMap((t) => {
    if (!$n(t)) return [];
    const n = (st(t, ["displayName", "DisplayName"]) || st(t, ts)).trim();
    if (!n) return [];
    const i = (es(t).alias || st(t, ["typeName", "TypeName", "alias", "Alias"])).trim();
    return [{ name: n, typeName: i, description: st(t, ["description", "Description"]).trim() }];
  });
}
function Eh(e, t) {
  const n = new Set(e.filter((i) => !!i));
  return (t?.items ?? []).find((i) => Cl(i.state) ? [i.activityDefinitionId, i.activityTypeKey, i.referenceName].some((r) => r && n.has(r)) : !1) ?? null;
}
const kh = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function Il(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function os(e) {
  return Il(e.name);
}
function Ih(e, t) {
  const n = os(t), i = e[n];
  return t.isWrapped === !1 ? i ?? t.defaultValue ?? "" : _l(i, t);
}
function Al(e, t) {
  return _l(e[os(t)], t);
}
function Ah(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function _h(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function oa(e, t, n) {
  return {
    ...e,
    [os(t)]: n
  };
}
function Dh(e, t) {
  return t.isWrapped === !1 ? Ih(e, t) : Al(e, t).expression.value;
}
function _l(e, t) {
  return Vh(e) ? {
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
const Th = /* @__PURE__ */ new Set([
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
function $h(e) {
  const t = e?.trim();
  if (!t) return null;
  const n = /^([\w.+]+)\[\]/.exec(t);
  if (n) return { elementTypeName: n[1] };
  const i = t.indexOf("`");
  if (i < 0) return null;
  const r = t.slice(0, i), s = (r.split(".").pop() ?? r).toLowerCase();
  return Th.has(s) ? { elementTypeName: Ph(t.slice(i)) } : null;
}
function Ph(e) {
  const t = /\[\[([\w.+]+)/.exec(e);
  if (t) return t[1];
  const n = /\[([\w.+]+)/.exec(e);
  return n ? n[1] : null;
}
function Mh(e) {
  const t = e.uiHint?.toLowerCase();
  return t === "json" || t === "code" ? !0 : e.uiSpecifications?.repeater === !1;
}
function Rh(e) {
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
function zh(e) {
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
function Vh(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
function ss(e) {
  return as(e?.trim() ?? "") || e;
}
function as(e) {
  if (!e) return "";
  const t = Oh(e);
  if (!t) return "";
  const n = /^(.*)((?:\[\])+)$/.exec(t);
  if (n) return `${as(n[1])}${n[2]}`;
  const i = t.indexOf("`");
  if (i >= 0) {
    const r = sa(t.slice(0, i)), s = Hh(t.slice(i));
    return s.length > 0 ? `${r}<${s.join(", ")}>` : r;
  }
  return sa(t);
}
function Oh(e) {
  let t = 0;
  for (let n = 0; n < e.length; n++) {
    const i = e[n];
    if (i === "[") t++;
    else if (i === "]") t--;
    else if (i === "," && t === 0) return e.slice(0, n).trim();
  }
  return e.trim();
}
function sa(e) {
  const t = e.split(".").filter(Boolean).at(-1) ?? e;
  return t.split("+").filter(Boolean).at(-1) ?? t;
}
function Hh(e) {
  const t = e.indexOf("[");
  if (t < 0) return [];
  const n = aa(e, t);
  return n == null ? [] : Wh(n).map((i) => {
    const r = i.trim(), s = r.startsWith("[") ? aa(r, 0) ?? r : r;
    return as(s);
  }).filter(Boolean);
}
function aa(e, t) {
  let n = 0;
  for (let i = t; i < e.length; i++)
    if (e[i] === "[") n++;
    else if (e[i] === "]" && --n === 0) return e.slice(t + 1, i);
  return null;
}
function Wh(e) {
  const t = [];
  let n = 0, i = 0;
  for (let r = 0; r < e.length; r++) {
    const s = e[r];
    s === "[" ? n++ : s === "]" ? n-- : s === "," && n === 0 && (t.push(e.slice(i, r)), i = r + 1);
  }
  return t.push(e.slice(i)), t.map((r) => r.trim()).filter(Boolean);
}
function Ve(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function cs(e, t) {
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
function Et(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function ir(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ o.jsx(qc, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ o.jsx(Yo, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ o.jsx(cf, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ o.jsx(Gt, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ o.jsx(af, { size: 15 });
    default:
      return /* @__PURE__ */ o.jsx(Xn, { size: 15 });
  }
}
function ca(e) {
  return `${e.activityTypeKey ?? ""}|${e.activityDefinitionId ?? ""}`;
}
function Bh(e, t) {
  const n = (a) => (a ?? "").split(".").map((c) => Number.parseInt(c, 10) || 0), i = n(e), r = n(t), s = Math.max(i.length, r.length);
  for (let a = 0; a < s; a++) {
    const c = (i[a] ?? 0) - (r[a] ?? 0);
    if (c !== 0) return c;
  }
  return 0;
}
function Fh(e) {
  if (e.lockedBy === "host-baseline") return "Blocked by the host baseline";
  if (e.lockedBy === "set-rule") return `Controlled by the "${e.governingSet}" set rule`;
}
function Dl({ icon: e }) {
  return /* @__PURE__ */ o.jsx("span", { className: "wf-activity-icon", "data-icon": e, "aria-hidden": "true", children: ir(e) });
}
function Kh({ context: e }) {
  const t = Pp(e), n = Mp(e), i = $p(e), r = Rp(e), s = t.data ?? null, a = n.data ?? null, c = t.isLoading || n.isLoading, u = r.isPending, [l, d] = K(() => Eo(s)), [f, p] = K(""), [h, y] = K(!1), [w, m] = K(null), [x, b] = K(null);
  te(() => {
    d(Eo(s));
  }, [s]);
  const g = ue(() => mh(a), [a]), v = ue(() => xh(a), [a]), j = ue(() => a?.sets ?? [], [a]), N = ue(
    () => bh(g, j, l),
    [g, j, l]
  ), S = (H) => N.get(bn(H)) ?? { enabled: !0, lockedBy: null }, k = ue(() => {
    const H = /* @__PURE__ */ new Map();
    for (const q of i.data?.activities ?? []) {
      if (!q.activityTypeKey) continue;
      const Q = H.get(q.activityTypeKey);
      (!Q || Bh(q.version, Q.version) > 0) && H.set(q.activityTypeKey, q);
    }
    return H;
  }, [i.data]), A = (H) => k.get(H.activityTypeKey ?? ""), M = ue(() => {
    const H = f.trim().toLowerCase(), q = h ? g.filter((Q) => !(N.get(bn(Q))?.enabled ?? !0)) : g;
    return H ? q.filter((Q) => [
      Mn(Q),
      na(Q),
      ia(Q),
      Q.activityTypeKey,
      Q.category
    ].some((U) => (U ?? "").toLowerCase().includes(H))) : q;
  }, [g, f, h, N]), _ = ue(() => wh(M), [M]), T = x ? g.find((H) => ca(H) === x) ?? null : null, L = g.filter(El).length, C = g.filter((H) => Ct(H.state) === "HiddenByManagementSettings").length, I = ue(() => Nh(l, s), [l, s]), E = r.error ?? t.error ?? n.error, D = E instanceof Error ? E.message : E ? "Activity availability could not be loaded." : null, $ = (H) => {
    m(null), d(H);
  }, P = (H) => $((q) => ({ ...q, mode: H })), F = (H, q) => $((Q) => io(Q, "activityTypes", H, q)), B = (H, q) => $((Q) => H.reduce((U, R) => io(U, "activityTypes", R, q), Q)), W = (H, q) => $((Q) => io(Q, "sets", H, q)), G = () => {
    m(null), r.mutate(
      {
        scope: s?.scope ?? "host-default",
        mode: gh(l.mode),
        rules: { activityTypes: l.activityTypes, sets: l.sets }
      },
      { onSuccess: () => m("Activity availability saved.") }
    );
  };
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-page availability-page", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-page-header", children: [
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("div", { className: "wf-kicker", children: "Workflows" }),
        /* @__PURE__ */ o.jsxs("h2", { children: [
          /* @__PURE__ */ o.jsx(lf, { size: 18 }),
          " Activity availability"
        ] }),
        /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "Control which activities can be added to new workflows. Host baseline rules always take precedence." })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { className: "wf-actions", children: [
        I && !u && /* @__PURE__ */ o.jsx("span", { className: "wf-chip availability-dirty", children: "Unsaved changes" }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", className: "availability-save", onClick: G, disabled: c || u || !I, children: [
          /* @__PURE__ */ o.jsx(Yc, { size: 15 }),
          u ? "Saving…" : "Save"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "availability-body", children: [
      D && /* @__PURE__ */ o.jsx("div", { className: "availability-banner availability-banner-error", children: D }),
      w && !D && /* @__PURE__ */ o.jsx("div", { className: "availability-banner availability-banner-success", children: w }),
      /* @__PURE__ */ o.jsxs("div", { className: "availability-mode", role: "group", "aria-label": "Activity availability mode", children: [
        /* @__PURE__ */ o.jsxs("button", { type: "button", className: l.mode === "AllExcept" ? "active" : "", onClick: () => P("AllExcept"), disabled: c || u, children: [
          /* @__PURE__ */ o.jsx(eo, { size: 15 }),
          /* @__PURE__ */ o.jsxs("span", { children: [
            /* @__PURE__ */ o.jsx("strong", { children: "All except" }),
            /* @__PURE__ */ o.jsx("em", { children: "Activities are available unless you turn them off" })
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", className: l.mode === "Only" ? "active" : "", onClick: () => P("Only"), disabled: c || u, children: [
          /* @__PURE__ */ o.jsx(Vs, { size: 15 }),
          /* @__PURE__ */ o.jsxs("span", { children: [
            /* @__PURE__ */ o.jsx("strong", { children: "Only" }),
            /* @__PURE__ */ o.jsx("em", { children: "Activities are hidden unless you turn them on" })
          ] })
        ] })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { className: "availability-counts", children: [
        /* @__PURE__ */ o.jsxs("span", { children: [
          /* @__PURE__ */ o.jsx(Vs, { size: 14 }),
          " ",
          L,
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
          /* @__PURE__ */ o.jsx(Uo, { size: 14 }),
          " Sets"
        ] }),
        /* @__PURE__ */ o.jsx("div", { className: "availability-set-list", children: j.map((H) => /* @__PURE__ */ o.jsxs("label", { className: "availability-set-option", children: [
          /* @__PURE__ */ o.jsx(
            "input",
            {
              type: "checkbox",
              role: "switch",
              className: "wf-switch-input",
              "aria-label": `Activities in the ${H.name} set available in new workflows`,
              checked: vh(l, "sets", H.name),
              disabled: c || u,
              onChange: (q) => W(H.name, q.target.checked)
            }
          ),
          /* @__PURE__ */ o.jsx("span", { children: H.name }),
          /* @__PURE__ */ o.jsx("code", { children: (H.activityTypeKeys ?? []).length })
        ] }, H.name)) })
      ] }),
      /* @__PURE__ */ o.jsxs("section", { className: "availability-section availability-section-grow", children: [
        /* @__PURE__ */ o.jsxs("div", { className: "availability-section-head", children: [
          /* @__PURE__ */ o.jsxs("h3", { className: "wf-section-label", children: [
            /* @__PURE__ */ o.jsx(Uc, { size: 14 }),
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
                onClick: () => y((H) => !H),
                children: [
                  /* @__PURE__ */ o.jsx(eo, { size: 13 }),
                  "Hidden only"
                ]
              }
            ),
            /* @__PURE__ */ o.jsxs("div", { className: "wf-search availability-search", children: [
              /* @__PURE__ */ o.jsx(Ji, { size: 14 }),
              /* @__PURE__ */ o.jsx("input", { type: "search", value: f, placeholder: "Filter activities…", onChange: (H) => p(H.target.value) })
            ] })
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("div", { className: "availability-activity-browser", children: [
          /* @__PURE__ */ o.jsxs("div", { className: "availability-activity-list", children: [
            c && g.length === 0 && /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "Loading availability…" }),
            !c && g.length === 0 && /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "No availability diagnostics reported." }),
            !c && g.length > 0 && M.length === 0 && /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: h && !f.trim() ? "No hidden activities — everything is turned on." : "No activities match the filter." }),
            _.map((H) => {
              const q = H.entries.filter((X) => S(X).lockedBy === null), Q = q.filter((X) => S(X).enabled).length, U = q.length > 0 && Q === q.length, R = Q > 0 && !U;
              return /* @__PURE__ */ o.jsxs("div", { className: "availability-group", children: [
                /* @__PURE__ */ o.jsxs("div", { className: "availability-group-header", children: [
                  /* @__PURE__ */ o.jsxs("span", { className: "availability-group-title", children: [
                    H.category,
                    /* @__PURE__ */ o.jsx("small", { children: H.entries.length })
                  ] }),
                  q.length > 0 && /* @__PURE__ */ o.jsx(
                    "input",
                    {
                      type: "checkbox",
                      role: "switch",
                      className: "wf-switch-input",
                      "aria-label": `Toggle the ${q.length} listed ${H.category} activities`,
                      checked: U,
                      ref: (X) => {
                        X && (X.indeterminate = R);
                      },
                      disabled: c || u,
                      onChange: () => B(q.map(bn), !U)
                    }
                  )
                ] }),
                H.entries.map((X) => {
                  const ce = bn(X), oe = ca(X), ee = Ct(X.state), ie = S(X), de = ie.lockedBy !== null, V = dn(A(X)), ne = Mn(X), he = na(X), ge = ia(X), Ee = x === oe;
                  return /* @__PURE__ */ o.jsxs("div", { className: `availability-activity-row${Ee ? " selected" : ""}${de ? " disabled" : ""}`, children: [
                    /* @__PURE__ */ o.jsxs(
                      "button",
                      {
                        type: "button",
                        className: "availability-activity-open",
                        "aria-expanded": Ee,
                        title: "Show activity details",
                        onClick: () => b(Ee ? null : oe),
                        children: [
                          /* @__PURE__ */ o.jsx(Dl, { icon: V }),
                          /* @__PURE__ */ o.jsxs("span", { className: "availability-activity-main", children: [
                            /* @__PURE__ */ o.jsxs("span", { className: "availability-activity-title-line", children: [
                              /* @__PURE__ */ o.jsx("strong", { children: ne }),
                              he && /* @__PURE__ */ o.jsx("code", { title: X.activityTypeKey ?? void 0, children: he })
                            ] }),
                            ge && /* @__PURE__ */ o.jsx("span", { className: "availability-activity-description", title: ge, children: ge })
                          ] })
                        ]
                      }
                    ),
                    ee !== "Available" && /* @__PURE__ */ o.jsx("em", { className: `availability-state ${Sl(X.state)}`, children: Pn(X.state) }),
                    /* @__PURE__ */ o.jsx(
                      "input",
                      {
                        type: "checkbox",
                        role: "switch",
                        className: "wf-switch-input",
                        "aria-label": `${ne} available in new workflows`,
                        title: Fh(ie),
                        checked: ie.enabled,
                        disabled: c || u || de,
                        onChange: (ke) => F(ce, ke.target.checked)
                      }
                    )
                  ] }, oe);
                })
              ] }, H.category);
            })
          ] }),
          T && /* @__PURE__ */ o.jsx(
            Xh,
            {
              entry: T,
              catalogItem: A(T),
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
        /* @__PURE__ */ o.jsx("div", { className: "availability-unresolved-list", children: v.map((H) => /* @__PURE__ */ o.jsxs("span", { children: [
          /* @__PURE__ */ o.jsx("strong", { children: H.referenceName }),
          /* @__PURE__ */ o.jsx("em", { children: Pn(H.state) })
        ] }, `${H.layer}-${H.referenceKind}-${H.referenceName}`)) })
      ] })
    ] })
  ] });
}
function Xh({
  entry: e,
  catalogItem: t,
  onClose: n
}) {
  const i = Mn(e), r = e.description?.trim() || t?.description?.trim(), s = ue(() => ra(t?.inputs), [t]), a = ue(() => ra(t?.outputs), [t]), c = ue(() => JSON.stringify({ diagnostic: e, catalog: t }, null, 2), [e, t]), u = [
    ["Type", e.activityTypeKey],
    ["Definition ID", e.activityDefinitionId],
    ["Category", e.category],
    ["Version", t?.version],
    ["Execution", t?.executionType],
    ["Policy layer", ph(e.layer)]
  ];
  return /* @__PURE__ */ o.jsxs("aside", { className: "availability-details", "aria-label": `${i} details`, children: [
    /* @__PURE__ */ o.jsxs("header", { className: "availability-details-header", children: [
      /* @__PURE__ */ o.jsx(Dl, { icon: dn(t) }),
      /* @__PURE__ */ o.jsx("h4", { children: i }),
      /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-icon-button availability-details-close", "aria-label": "Close details", onClick: n, children: /* @__PURE__ */ o.jsx(Zo, { size: 14 }) })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "availability-details-body", children: [
      /* @__PURE__ */ o.jsxs("p", { className: "availability-details-status", children: [
        /* @__PURE__ */ o.jsx("em", { className: `availability-state ${Sl(e.state)}`, children: Pn(e.state) }),
        e.reason?.trim() && /* @__PURE__ */ o.jsx("span", { children: e.reason })
      ] }),
      r && /* @__PURE__ */ o.jsx("p", { className: "availability-details-description", children: r }),
      /* @__PURE__ */ o.jsx("dl", { className: "availability-details-meta", children: u.map(([l, d]) => d?.trim() ? /* @__PURE__ */ o.jsxs(Ue.Fragment, { children: [
        /* @__PURE__ */ o.jsx("dt", { children: l }),
        /* @__PURE__ */ o.jsx("dd", { children: /* @__PURE__ */ o.jsx("code", { children: d }) })
      ] }, l) : null) }),
      s.length > 0 && /* @__PURE__ */ o.jsx(la, { title: "Inputs", items: s }),
      a.length > 0 && /* @__PURE__ */ o.jsx(la, { title: "Outputs", items: a }),
      !t && /* @__PURE__ */ o.jsx("p", { className: "wf-muted availability-details-note", children: "Catalog metadata is unavailable for this activity — it is not exposed by the currently saved policy." }),
      /* @__PURE__ */ o.jsxs("details", { className: "availability-details-raw", children: [
        /* @__PURE__ */ o.jsx("summary", { children: "Raw metadata" }),
        /* @__PURE__ */ o.jsx("pre", { children: c })
      ] })
    ] })
  ] });
}
function la({ title: e, items: t }) {
  return /* @__PURE__ */ o.jsxs("section", { className: "availability-details-arguments", children: [
    /* @__PURE__ */ o.jsx("h5", { children: e }),
    /* @__PURE__ */ o.jsx("ul", { children: t.map((n) => /* @__PURE__ */ o.jsxs("li", { title: n.description || void 0, children: [
      /* @__PURE__ */ o.jsx("strong", { children: n.name }),
      n.typeName && /* @__PURE__ */ o.jsx("code", { children: ss(n.typeName) })
    ] }, n.name)) })
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
var qh = { value: () => {
} };
function rr() {
  for (var e = 0, t = arguments.length, n = {}, i; e < t; ++e) {
    if (!(i = arguments[e] + "") || i in n || /[\s.]/.test(i)) throw new Error("illegal type: " + i);
    n[i] = [];
  }
  return new Ai(n);
}
function Ai(e) {
  this._ = e;
}
function Yh(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var i = "", r = n.indexOf(".");
    if (r >= 0 && (i = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: i };
  });
}
Ai.prototype = rr.prototype = {
  constructor: Ai,
  on: function(e, t) {
    var n = this._, i = Yh(e + "", n), r, s = -1, a = i.length;
    if (arguments.length < 2) {
      for (; ++s < a; ) if ((r = (e = i[s]).type) && (r = Uh(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++s < a; )
      if (r = (e = i[s]).type) n[r] = ua(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = ua(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new Ai(e);
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
function Uh(e, t) {
  for (var n = 0, i = e.length, r; n < i; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function ua(e, t, n) {
  for (var i = 0, r = e.length; i < r; ++i)
    if (e[i].name === t) {
      e[i] = qh, e = e.slice(0, i).concat(e.slice(i + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var ko = "http://www.w3.org/1999/xhtml";
const da = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: ko,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function or(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), da.hasOwnProperty(t) ? { space: da[t], local: e } : e;
}
function Zh(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === ko && t.documentElement.namespaceURI === ko ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Gh(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function Tl(e) {
  var t = or(e);
  return (t.local ? Gh : Zh)(t);
}
function Jh() {
}
function ls(e) {
  return e == null ? Jh : function() {
    return this.querySelector(e);
  };
}
function Qh(e) {
  typeof e != "function" && (e = ls(e));
  for (var t = this._groups, n = t.length, i = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = i[r] = new Array(a), u, l, d = 0; d < a; ++d)
      (u = s[d]) && (l = e.call(u, u.__data__, d, s)) && ("__data__" in u && (l.__data__ = u.__data__), c[d] = l);
  return new Oe(i, this._parents);
}
function eg(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function tg() {
  return [];
}
function $l(e) {
  return e == null ? tg : function() {
    return this.querySelectorAll(e);
  };
}
function ng(e) {
  return function() {
    return eg(e.apply(this, arguments));
  };
}
function ig(e) {
  typeof e == "function" ? e = ng(e) : e = $l(e);
  for (var t = this._groups, n = t.length, i = [], r = [], s = 0; s < n; ++s)
    for (var a = t[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && (i.push(e.call(u, u.__data__, l, a)), r.push(u));
  return new Oe(i, r);
}
function Pl(e) {
  return function() {
    return this.matches(e);
  };
}
function Ml(e) {
  return function(t) {
    return t.matches(e);
  };
}
var rg = Array.prototype.find;
function og(e) {
  return function() {
    return rg.call(this.children, e);
  };
}
function sg() {
  return this.firstElementChild;
}
function ag(e) {
  return this.select(e == null ? sg : og(typeof e == "function" ? e : Ml(e)));
}
var cg = Array.prototype.filter;
function lg() {
  return Array.from(this.children);
}
function ug(e) {
  return function() {
    return cg.call(this.children, e);
  };
}
function dg(e) {
  return this.selectAll(e == null ? lg : ug(typeof e == "function" ? e : Ml(e)));
}
function fg(e) {
  typeof e != "function" && (e = Pl(e));
  for (var t = this._groups, n = t.length, i = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = i[r] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new Oe(i, this._parents);
}
function Rl(e) {
  return new Array(e.length);
}
function pg() {
  return new Oe(this._enter || this._groups.map(Rl), this._parents);
}
function Hi(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
Hi.prototype = {
  constructor: Hi,
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
function hg(e) {
  return function() {
    return e;
  };
}
function gg(e, t, n, i, r, s) {
  for (var a = 0, c, u = t.length, l = s.length; a < l; ++a)
    (c = t[a]) ? (c.__data__ = s[a], i[a] = c) : n[a] = new Hi(e, s[a]);
  for (; a < u; ++a)
    (c = t[a]) && (r[a] = c);
}
function yg(e, t, n, i, r, s, a) {
  var c, u, l = /* @__PURE__ */ new Map(), d = t.length, f = s.length, p = new Array(d), h;
  for (c = 0; c < d; ++c)
    (u = t[c]) && (p[c] = h = a.call(u, u.__data__, c, t) + "", l.has(h) ? r[c] = u : l.set(h, u));
  for (c = 0; c < f; ++c)
    h = a.call(e, s[c], c, s) + "", (u = l.get(h)) ? (i[c] = u, u.__data__ = s[c], l.delete(h)) : n[c] = new Hi(e, s[c]);
  for (c = 0; c < d; ++c)
    (u = t[c]) && l.get(p[c]) === u && (r[c] = u);
}
function mg(e) {
  return e.__data__;
}
function xg(e, t) {
  if (!arguments.length) return Array.from(this, mg);
  var n = t ? yg : gg, i = this._parents, r = this._groups;
  typeof e != "function" && (e = hg(e));
  for (var s = r.length, a = new Array(s), c = new Array(s), u = new Array(s), l = 0; l < s; ++l) {
    var d = i[l], f = r[l], p = f.length, h = wg(e.call(d, d && d.__data__, l, i)), y = h.length, w = c[l] = new Array(y), m = a[l] = new Array(y), x = u[l] = new Array(p);
    n(d, f, w, m, x, h, t);
    for (var b = 0, g = 0, v, j; b < y; ++b)
      if (v = w[b]) {
        for (b >= g && (g = b + 1); !(j = m[g]) && ++g < y; ) ;
        v._next = j || null;
      }
  }
  return a = new Oe(a, i), a._enter = c, a._exit = u, a;
}
function wg(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function vg() {
  return new Oe(this._exit || this._groups.map(Rl), this._parents);
}
function bg(e, t, n) {
  var i = this.enter(), r = this, s = this.exit();
  return typeof e == "function" ? (i = e(i), i && (i = i.selection())) : i = i.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? s.remove() : n(s), i && r ? i.merge(r).order() : r;
}
function Ng(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, i = t._groups, r = n.length, s = i.length, a = Math.min(r, s), c = new Array(r), u = 0; u < a; ++u)
    for (var l = n[u], d = i[u], f = l.length, p = c[u] = new Array(f), h, y = 0; y < f; ++y)
      (h = l[y] || d[y]) && (p[y] = h);
  for (; u < r; ++u)
    c[u] = n[u];
  return new Oe(c, this._parents);
}
function jg() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var i = e[t], r = i.length - 1, s = i[r], a; --r >= 0; )
      (a = i[r]) && (s && a.compareDocumentPosition(s) ^ 4 && s.parentNode.insertBefore(a, s), s = a);
  return this;
}
function Sg(e) {
  e || (e = Cg);
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
function Cg(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function Eg() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function kg() {
  return Array.from(this);
}
function Ig() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var i = e[t], r = 0, s = i.length; r < s; ++r) {
      var a = i[r];
      if (a) return a;
    }
  return null;
}
function Ag() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function _g() {
  return !this.node();
}
function Dg(e) {
  for (var t = this._groups, n = 0, i = t.length; n < i; ++n)
    for (var r = t[n], s = 0, a = r.length, c; s < a; ++s)
      (c = r[s]) && e.call(c, c.__data__, s, r);
  return this;
}
function Tg(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function $g(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Pg(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function Mg(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function Rg(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function zg(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function Lg(e, t) {
  var n = or(e);
  if (arguments.length < 2) {
    var i = this.node();
    return n.local ? i.getAttributeNS(n.space, n.local) : i.getAttribute(n);
  }
  return this.each((t == null ? n.local ? $g : Tg : typeof t == "function" ? n.local ? zg : Rg : n.local ? Mg : Pg)(n, t));
}
function zl(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function Vg(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Og(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function Hg(e, t, n) {
  return function() {
    var i = t.apply(this, arguments);
    i == null ? this.style.removeProperty(e) : this.style.setProperty(e, i, n);
  };
}
function Wg(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? Vg : typeof t == "function" ? Hg : Og)(e, t, n ?? "")) : en(this.node(), e);
}
function en(e, t) {
  return e.style.getPropertyValue(t) || zl(e).getComputedStyle(e, null).getPropertyValue(t);
}
function Bg(e) {
  return function() {
    delete this[e];
  };
}
function Fg(e, t) {
  return function() {
    this[e] = t;
  };
}
function Kg(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function Xg(e, t) {
  return arguments.length > 1 ? this.each((t == null ? Bg : typeof t == "function" ? Kg : Fg)(e, t)) : this.node()[e];
}
function Ll(e) {
  return e.trim().split(/^|\s+/);
}
function us(e) {
  return e.classList || new Vl(e);
}
function Vl(e) {
  this._node = e, this._names = Ll(e.getAttribute("class") || "");
}
Vl.prototype = {
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
function Ol(e, t) {
  for (var n = us(e), i = -1, r = t.length; ++i < r; ) n.add(t[i]);
}
function Hl(e, t) {
  for (var n = us(e), i = -1, r = t.length; ++i < r; ) n.remove(t[i]);
}
function qg(e) {
  return function() {
    Ol(this, e);
  };
}
function Yg(e) {
  return function() {
    Hl(this, e);
  };
}
function Ug(e, t) {
  return function() {
    (t.apply(this, arguments) ? Ol : Hl)(this, e);
  };
}
function Zg(e, t) {
  var n = Ll(e + "");
  if (arguments.length < 2) {
    for (var i = us(this.node()), r = -1, s = n.length; ++r < s; ) if (!i.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? Ug : t ? qg : Yg)(n, t));
}
function Gg() {
  this.textContent = "";
}
function Jg(e) {
  return function() {
    this.textContent = e;
  };
}
function Qg(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function ey(e) {
  return arguments.length ? this.each(e == null ? Gg : (typeof e == "function" ? Qg : Jg)(e)) : this.node().textContent;
}
function ty() {
  this.innerHTML = "";
}
function ny(e) {
  return function() {
    this.innerHTML = e;
  };
}
function iy(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function ry(e) {
  return arguments.length ? this.each(e == null ? ty : (typeof e == "function" ? iy : ny)(e)) : this.node().innerHTML;
}
function oy() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function sy() {
  return this.each(oy);
}
function ay() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function cy() {
  return this.each(ay);
}
function ly(e) {
  var t = typeof e == "function" ? e : Tl(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function uy() {
  return null;
}
function dy(e, t) {
  var n = typeof e == "function" ? e : Tl(e), i = t == null ? uy : typeof t == "function" ? t : ls(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), i.apply(this, arguments) || null);
  });
}
function fy() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function py() {
  return this.each(fy);
}
function hy() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function gy() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function yy(e) {
  return this.select(e ? gy : hy);
}
function my(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function xy(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function wy(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", i = t.indexOf(".");
    return i >= 0 && (n = t.slice(i + 1), t = t.slice(0, i)), { type: t, name: n };
  });
}
function vy(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, i = -1, r = t.length, s; n < r; ++n)
        s = t[n], (!e.type || s.type === e.type) && s.name === e.name ? this.removeEventListener(s.type, s.listener, s.options) : t[++i] = s;
      ++i ? t.length = i : delete this.__on;
    }
  };
}
function by(e, t, n) {
  return function() {
    var i = this.__on, r, s = xy(t);
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
function Ny(e, t, n) {
  var i = wy(e + ""), r, s = i.length, a;
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
  for (c = t ? by : vy, r = 0; r < s; ++r) this.each(c(i[r], t, n));
  return this;
}
function Wl(e, t, n) {
  var i = zl(e), r = i.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = i.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function jy(e, t) {
  return function() {
    return Wl(this, e, t);
  };
}
function Sy(e, t) {
  return function() {
    return Wl(this, e, t.apply(this, arguments));
  };
}
function Cy(e, t) {
  return this.each((typeof t == "function" ? Sy : jy)(e, t));
}
function* Ey() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var i = e[t], r = 0, s = i.length, a; r < s; ++r)
      (a = i[r]) && (yield a);
}
var Bl = [null];
function Oe(e, t) {
  this._groups = e, this._parents = t;
}
function Zn() {
  return new Oe([[document.documentElement]], Bl);
}
function ky() {
  return this;
}
Oe.prototype = Zn.prototype = {
  constructor: Oe,
  select: Qh,
  selectAll: ig,
  selectChild: ag,
  selectChildren: dg,
  filter: fg,
  data: xg,
  enter: pg,
  exit: vg,
  join: bg,
  merge: Ng,
  selection: ky,
  order: jg,
  sort: Sg,
  call: Eg,
  nodes: kg,
  node: Ig,
  size: Ag,
  empty: _g,
  each: Dg,
  attr: Lg,
  style: Wg,
  property: Xg,
  classed: Zg,
  text: ey,
  html: ry,
  raise: sy,
  lower: cy,
  append: ly,
  insert: dy,
  remove: py,
  clone: yy,
  datum: my,
  on: Ny,
  dispatch: Cy,
  [Symbol.iterator]: Ey
};
function ze(e) {
  return typeof e == "string" ? new Oe([[document.querySelector(e)]], [document.documentElement]) : new Oe([[e]], Bl);
}
function Iy(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Ke(e, t) {
  if (e = Iy(e), t === void 0 && (t = e.currentTarget), t) {
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
const Ay = { passive: !1 }, Rn = { capture: !0, passive: !1 };
function oo(e) {
  e.stopImmediatePropagation();
}
function Yt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Fl(e) {
  var t = e.document.documentElement, n = ze(e).on("dragstart.drag", Yt, Rn);
  "onselectstart" in t ? n.on("selectstart.drag", Yt, Rn) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function Kl(e, t) {
  var n = e.document.documentElement, i = ze(e).on("dragstart.drag", null);
  t && (i.on("click.drag", Yt, Rn), setTimeout(function() {
    i.on("click.drag", null);
  }, 0)), "onselectstart" in n ? i.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const gi = (e) => () => e;
function Io(e, {
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
Io.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function _y(e) {
  return !e.ctrlKey && !e.button;
}
function Dy() {
  return this.parentNode;
}
function Ty(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function $y() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Xl() {
  var e = _y, t = Dy, n = Ty, i = $y, r = {}, s = rr("start", "drag", "end"), a = 0, c, u, l, d, f = 0;
  function p(v) {
    v.on("mousedown.drag", h).filter(i).on("touchstart.drag", m).on("touchmove.drag", x, Ay).on("touchend.drag touchcancel.drag", b).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function h(v, j) {
    if (!(d || !e.call(this, v, j))) {
      var N = g(this, t.call(this, v, j), v, j, "mouse");
      N && (ze(v.view).on("mousemove.drag", y, Rn).on("mouseup.drag", w, Rn), Fl(v.view), oo(v), l = !1, c = v.clientX, u = v.clientY, N("start", v));
    }
  }
  function y(v) {
    if (Yt(v), !l) {
      var j = v.clientX - c, N = v.clientY - u;
      l = j * j + N * N > f;
    }
    r.mouse("drag", v);
  }
  function w(v) {
    ze(v.view).on("mousemove.drag mouseup.drag", null), Kl(v.view, l), Yt(v), r.mouse("end", v);
  }
  function m(v, j) {
    if (e.call(this, v, j)) {
      var N = v.changedTouches, S = t.call(this, v, j), k = N.length, A, M;
      for (A = 0; A < k; ++A)
        (M = g(this, S, v, j, N[A].identifier, N[A])) && (oo(v), M("start", v, N[A]));
    }
  }
  function x(v) {
    var j = v.changedTouches, N = j.length, S, k;
    for (S = 0; S < N; ++S)
      (k = r[j[S].identifier]) && (Yt(v), k("drag", v, j[S]));
  }
  function b(v) {
    var j = v.changedTouches, N = j.length, S, k;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), S = 0; S < N; ++S)
      (k = r[j[S].identifier]) && (oo(v), k("end", v, j[S]));
  }
  function g(v, j, N, S, k, A) {
    var M = s.copy(), _ = Ke(A || N, j), T, L, C;
    if ((C = n.call(v, new Io("beforestart", {
      sourceEvent: N,
      target: p,
      identifier: k,
      active: a,
      x: _[0],
      y: _[1],
      dx: 0,
      dy: 0,
      dispatch: M
    }), S)) != null)
      return T = C.x - _[0] || 0, L = C.y - _[1] || 0, function I(E, D, $) {
        var P = _, F;
        switch (E) {
          case "start":
            r[k] = I, F = a++;
            break;
          case "end":
            delete r[k], --a;
          // falls through
          case "drag":
            _ = Ke($ || D, j), F = a;
            break;
        }
        M.call(
          E,
          v,
          new Io(E, {
            sourceEvent: D,
            subject: C,
            target: p,
            identifier: k,
            active: F,
            x: _[0] + T,
            y: _[1] + L,
            dx: _[0] - P[0],
            dy: _[1] - P[1],
            dispatch: M
          }),
          S
        );
      };
  }
  return p.filter = function(v) {
    return arguments.length ? (e = typeof v == "function" ? v : gi(!!v), p) : e;
  }, p.container = function(v) {
    return arguments.length ? (t = typeof v == "function" ? v : gi(v), p) : t;
  }, p.subject = function(v) {
    return arguments.length ? (n = typeof v == "function" ? v : gi(v), p) : n;
  }, p.touchable = function(v) {
    return arguments.length ? (i = typeof v == "function" ? v : gi(!!v), p) : i;
  }, p.on = function() {
    var v = s.on.apply(s, arguments);
    return v === s ? p : v;
  }, p.clickDistance = function(v) {
    return arguments.length ? (f = (v = +v) * v, p) : Math.sqrt(f);
  }, p;
}
function ds(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function ql(e, t) {
  var n = Object.create(e.prototype);
  for (var i in t) n[i] = t[i];
  return n;
}
function Gn() {
}
var zn = 0.7, Wi = 1 / zn, Ut = "\\s*([+-]?\\d+)\\s*", Ln = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", Qe = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", Py = /^#([0-9a-f]{3,8})$/, My = new RegExp(`^rgb\\(${Ut},${Ut},${Ut}\\)$`), Ry = new RegExp(`^rgb\\(${Qe},${Qe},${Qe}\\)$`), zy = new RegExp(`^rgba\\(${Ut},${Ut},${Ut},${Ln}\\)$`), Ly = new RegExp(`^rgba\\(${Qe},${Qe},${Qe},${Ln}\\)$`), Vy = new RegExp(`^hsl\\(${Ln},${Qe},${Qe}\\)$`), Oy = new RegExp(`^hsla\\(${Ln},${Qe},${Qe},${Ln}\\)$`), fa = {
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
ds(Gn, kt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: pa,
  // Deprecated! Use color.formatHex.
  formatHex: pa,
  formatHex8: Hy,
  formatHsl: Wy,
  formatRgb: ha,
  toString: ha
});
function pa() {
  return this.rgb().formatHex();
}
function Hy() {
  return this.rgb().formatHex8();
}
function Wy() {
  return Yl(this).formatHsl();
}
function ha() {
  return this.rgb().formatRgb();
}
function kt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = Py.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? ga(t) : n === 3 ? new Pe(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? yi(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? yi(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = My.exec(e)) ? new Pe(t[1], t[2], t[3], 1) : (t = Ry.exec(e)) ? new Pe(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = zy.exec(e)) ? yi(t[1], t[2], t[3], t[4]) : (t = Ly.exec(e)) ? yi(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = Vy.exec(e)) ? xa(t[1], t[2] / 100, t[3] / 100, 1) : (t = Oy.exec(e)) ? xa(t[1], t[2] / 100, t[3] / 100, t[4]) : fa.hasOwnProperty(e) ? ga(fa[e]) : e === "transparent" ? new Pe(NaN, NaN, NaN, 0) : null;
}
function ga(e) {
  return new Pe(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function yi(e, t, n, i) {
  return i <= 0 && (e = t = n = NaN), new Pe(e, t, n, i);
}
function By(e) {
  return e instanceof Gn || (e = kt(e)), e ? (e = e.rgb(), new Pe(e.r, e.g, e.b, e.opacity)) : new Pe();
}
function Ao(e, t, n, i) {
  return arguments.length === 1 ? By(e) : new Pe(e, t, n, i ?? 1);
}
function Pe(e, t, n, i) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +i;
}
ds(Pe, Ao, ql(Gn, {
  brighter(e) {
    return e = e == null ? Wi : Math.pow(Wi, e), new Pe(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? zn : Math.pow(zn, e), new Pe(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new Pe(jt(this.r), jt(this.g), jt(this.b), Bi(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: ya,
  // Deprecated! Use color.formatHex.
  formatHex: ya,
  formatHex8: Fy,
  formatRgb: ma,
  toString: ma
}));
function ya() {
  return `#${Nt(this.r)}${Nt(this.g)}${Nt(this.b)}`;
}
function Fy() {
  return `#${Nt(this.r)}${Nt(this.g)}${Nt(this.b)}${Nt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function ma() {
  const e = Bi(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${jt(this.r)}, ${jt(this.g)}, ${jt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Bi(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function jt(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function Nt(e) {
  return e = jt(e), (e < 16 ? "0" : "") + e.toString(16);
}
function xa(e, t, n, i) {
  return i <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Xe(e, t, n, i);
}
function Yl(e) {
  if (e instanceof Xe) return new Xe(e.h, e.s, e.l, e.opacity);
  if (e instanceof Gn || (e = kt(e)), !e) return new Xe();
  if (e instanceof Xe) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, i = e.b / 255, r = Math.min(t, n, i), s = Math.max(t, n, i), a = NaN, c = s - r, u = (s + r) / 2;
  return c ? (t === s ? a = (n - i) / c + (n < i) * 6 : n === s ? a = (i - t) / c + 2 : a = (t - n) / c + 4, c /= u < 0.5 ? s + r : 2 - s - r, a *= 60) : c = u > 0 && u < 1 ? 0 : a, new Xe(a, c, u, e.opacity);
}
function Ky(e, t, n, i) {
  return arguments.length === 1 ? Yl(e) : new Xe(e, t, n, i ?? 1);
}
function Xe(e, t, n, i) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +i;
}
ds(Xe, Ky, ql(Gn, {
  brighter(e) {
    return e = e == null ? Wi : Math.pow(Wi, e), new Xe(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? zn : Math.pow(zn, e), new Xe(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, i = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - i;
    return new Pe(
      so(e >= 240 ? e - 240 : e + 120, r, i),
      so(e, r, i),
      so(e < 120 ? e + 240 : e - 120, r, i),
      this.opacity
    );
  },
  clamp() {
    return new Xe(wa(this.h), mi(this.s), mi(this.l), Bi(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Bi(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${wa(this.h)}, ${mi(this.s) * 100}%, ${mi(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function wa(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function mi(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function so(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const fs = (e) => () => e;
function Xy(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function qy(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(i) {
    return Math.pow(e + i * t, n);
  };
}
function Yy(e) {
  return (e = +e) == 1 ? Ul : function(t, n) {
    return n - t ? qy(t, n, e) : fs(isNaN(t) ? n : t);
  };
}
function Ul(e, t) {
  var n = t - e;
  return n ? Xy(e, n) : fs(isNaN(e) ? t : e);
}
const Fi = (function e(t) {
  var n = Yy(t);
  function i(r, s) {
    var a = n((r = Ao(r)).r, (s = Ao(s)).r), c = n(r.g, s.g), u = n(r.b, s.b), l = Ul(r.opacity, s.opacity);
    return function(d) {
      return r.r = a(d), r.g = c(d), r.b = u(d), r.opacity = l(d), r + "";
    };
  }
  return i.gamma = e, i;
})(1);
function Uy(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, i = t.slice(), r;
  return function(s) {
    for (r = 0; r < n; ++r) i[r] = e[r] * (1 - s) + t[r] * s;
    return i;
  };
}
function Zy(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Gy(e, t) {
  var n = t ? t.length : 0, i = e ? Math.min(n, e.length) : 0, r = new Array(i), s = new Array(n), a;
  for (a = 0; a < i; ++a) r[a] = In(e[a], t[a]);
  for (; a < n; ++a) s[a] = t[a];
  return function(c) {
    for (a = 0; a < i; ++a) s[a] = r[a](c);
    return s;
  };
}
function Jy(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(i) {
    return n.setTime(e * (1 - i) + t * i), n;
  };
}
function Je(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function Qy(e, t) {
  var n = {}, i = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = In(e[r], t[r]) : i[r] = t[r];
  return function(s) {
    for (r in n) i[r] = n[r](s);
    return i;
  };
}
var _o = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, ao = new RegExp(_o.source, "g");
function em(e) {
  return function() {
    return e;
  };
}
function tm(e) {
  return function(t) {
    return e(t) + "";
  };
}
function Zl(e, t) {
  var n = _o.lastIndex = ao.lastIndex = 0, i, r, s, a = -1, c = [], u = [];
  for (e = e + "", t = t + ""; (i = _o.exec(e)) && (r = ao.exec(t)); )
    (s = r.index) > n && (s = t.slice(n, s), c[a] ? c[a] += s : c[++a] = s), (i = i[0]) === (r = r[0]) ? c[a] ? c[a] += r : c[++a] = r : (c[++a] = null, u.push({ i: a, x: Je(i, r) })), n = ao.lastIndex;
  return n < t.length && (s = t.slice(n), c[a] ? c[a] += s : c[++a] = s), c.length < 2 ? u[0] ? tm(u[0].x) : em(t) : (t = u.length, function(l) {
    for (var d = 0, f; d < t; ++d) c[(f = u[d]).i] = f.x(l);
    return c.join("");
  });
}
function In(e, t) {
  var n = typeof t, i;
  return t == null || n === "boolean" ? fs(t) : (n === "number" ? Je : n === "string" ? (i = kt(t)) ? (t = i, Fi) : Zl : t instanceof kt ? Fi : t instanceof Date ? Jy : Zy(t) ? Uy : Array.isArray(t) ? Gy : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Qy : Je)(e, t);
}
var va = 180 / Math.PI, Do = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function Gl(e, t, n, i, r, s) {
  var a, c, u;
  return (a = Math.sqrt(e * e + t * t)) && (e /= a, t /= a), (u = e * n + t * i) && (n -= e * u, i -= t * u), (c = Math.sqrt(n * n + i * i)) && (n /= c, i /= c, u /= c), e * i < t * n && (e = -e, t = -t, u = -u, a = -a), {
    translateX: r,
    translateY: s,
    rotate: Math.atan2(t, e) * va,
    skewX: Math.atan(u) * va,
    scaleX: a,
    scaleY: c
  };
}
var xi;
function nm(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Do : Gl(t.a, t.b, t.c, t.d, t.e, t.f);
}
function im(e) {
  return e == null || (xi || (xi = document.createElementNS("http://www.w3.org/2000/svg", "g")), xi.setAttribute("transform", e), !(e = xi.transform.baseVal.consolidate())) ? Do : (e = e.matrix, Gl(e.a, e.b, e.c, e.d, e.e, e.f));
}
function Jl(e, t, n, i) {
  function r(l) {
    return l.length ? l.pop() + " " : "";
  }
  function s(l, d, f, p, h, y) {
    if (l !== f || d !== p) {
      var w = h.push("translate(", null, t, null, n);
      y.push({ i: w - 4, x: Je(l, f) }, { i: w - 2, x: Je(d, p) });
    } else (f || p) && h.push("translate(" + f + t + p + n);
  }
  function a(l, d, f, p) {
    l !== d ? (l - d > 180 ? d += 360 : d - l > 180 && (l += 360), p.push({ i: f.push(r(f) + "rotate(", null, i) - 2, x: Je(l, d) })) : d && f.push(r(f) + "rotate(" + d + i);
  }
  function c(l, d, f, p) {
    l !== d ? p.push({ i: f.push(r(f) + "skewX(", null, i) - 2, x: Je(l, d) }) : d && f.push(r(f) + "skewX(" + d + i);
  }
  function u(l, d, f, p, h, y) {
    if (l !== f || d !== p) {
      var w = h.push(r(h) + "scale(", null, ",", null, ")");
      y.push({ i: w - 4, x: Je(l, f) }, { i: w - 2, x: Je(d, p) });
    } else (f !== 1 || p !== 1) && h.push(r(h) + "scale(" + f + "," + p + ")");
  }
  return function(l, d) {
    var f = [], p = [];
    return l = e(l), d = e(d), s(l.translateX, l.translateY, d.translateX, d.translateY, f, p), a(l.rotate, d.rotate, f, p), c(l.skewX, d.skewX, f, p), u(l.scaleX, l.scaleY, d.scaleX, d.scaleY, f, p), l = d = null, function(h) {
      for (var y = -1, w = p.length, m; ++y < w; ) f[(m = p[y]).i] = m.x(h);
      return f.join("");
    };
  };
}
var rm = Jl(nm, "px, ", "px)", "deg)"), om = Jl(im, ", ", ")", ")"), sm = 1e-12;
function ba(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function am(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function cm(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const _i = (function e(t, n, i) {
  function r(s, a) {
    var c = s[0], u = s[1], l = s[2], d = a[0], f = a[1], p = a[2], h = d - c, y = f - u, w = h * h + y * y, m, x;
    if (w < sm)
      x = Math.log(p / l) / t, m = function(S) {
        return [
          c + S * h,
          u + S * y,
          l * Math.exp(t * S * x)
        ];
      };
    else {
      var b = Math.sqrt(w), g = (p * p - l * l + i * w) / (2 * l * n * b), v = (p * p - l * l - i * w) / (2 * p * n * b), j = Math.log(Math.sqrt(g * g + 1) - g), N = Math.log(Math.sqrt(v * v + 1) - v);
      x = (N - j) / t, m = function(S) {
        var k = S * x, A = ba(j), M = l / (n * b) * (A * cm(t * k + j) - am(j));
        return [
          c + M * h,
          u + M * y,
          l * A / ba(t * k + j)
        ];
      };
    }
    return m.duration = x * 1e3 * t / Math.SQRT2, m;
  }
  return r.rho = function(s) {
    var a = Math.max(1e-3, +s), c = a * a, u = c * c;
    return e(a, c, u);
  }, r;
})(Math.SQRT2, 2, 4);
var tn = 0, Nn = 0, mn = 0, Ql = 1e3, Ki, jn, Xi = 0, It = 0, sr = 0, Vn = typeof performance == "object" && performance.now ? performance : Date, eu = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function ps() {
  return It || (eu(lm), It = Vn.now() + sr);
}
function lm() {
  It = 0;
}
function qi() {
  this._call = this._time = this._next = null;
}
qi.prototype = tu.prototype = {
  constructor: qi,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? ps() : +n) + (t == null ? 0 : +t), !this._next && jn !== this && (jn ? jn._next = this : Ki = this, jn = this), this._call = e, this._time = n, To();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, To());
  }
};
function tu(e, t, n) {
  var i = new qi();
  return i.restart(e, t, n), i;
}
function um() {
  ps(), ++tn;
  for (var e = Ki, t; e; )
    (t = It - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --tn;
}
function Na() {
  It = (Xi = Vn.now()) + sr, tn = Nn = 0;
  try {
    um();
  } finally {
    tn = 0, fm(), It = 0;
  }
}
function dm() {
  var e = Vn.now(), t = e - Xi;
  t > Ql && (sr -= t, Xi = e);
}
function fm() {
  for (var e, t = Ki, n, i = 1 / 0; t; )
    t._call ? (i > t._time && (i = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Ki = n);
  jn = e, To(i);
}
function To(e) {
  if (!tn) {
    Nn && (Nn = clearTimeout(Nn));
    var t = e - It;
    t > 24 ? (e < 1 / 0 && (Nn = setTimeout(Na, e - Vn.now() - sr)), mn && (mn = clearInterval(mn))) : (mn || (Xi = Vn.now(), mn = setInterval(dm, Ql)), tn = 1, eu(Na));
  }
}
function ja(e, t, n) {
  var i = new qi();
  return t = t == null ? 0 : +t, i.restart((r) => {
    i.stop(), e(r + t);
  }, t, n), i;
}
var pm = rr("start", "end", "cancel", "interrupt"), hm = [], nu = 0, Sa = 1, $o = 2, Di = 3, Ca = 4, Po = 5, Ti = 6;
function ar(e, t, n, i, r, s) {
  var a = e.__transition;
  if (!a) e.__transition = {};
  else if (n in a) return;
  gm(e, n, {
    name: t,
    index: i,
    // For context during callback.
    group: r,
    // For context during callback.
    on: pm,
    tween: hm,
    time: s.time,
    delay: s.delay,
    duration: s.duration,
    ease: s.ease,
    timer: null,
    state: nu
  });
}
function hs(e, t) {
  var n = Ze(e, t);
  if (n.state > nu) throw new Error("too late; already scheduled");
  return n;
}
function nt(e, t) {
  var n = Ze(e, t);
  if (n.state > Di) throw new Error("too late; already running");
  return n;
}
function Ze(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function gm(e, t, n) {
  var i = e.__transition, r;
  i[t] = n, n.timer = tu(s, 0, n.time);
  function s(l) {
    n.state = Sa, n.timer.restart(a, n.delay, n.time), n.delay <= l && a(l - n.delay);
  }
  function a(l) {
    var d, f, p, h;
    if (n.state !== Sa) return u();
    for (d in i)
      if (h = i[d], h.name === n.name) {
        if (h.state === Di) return ja(a);
        h.state === Ca ? (h.state = Ti, h.timer.stop(), h.on.call("interrupt", e, e.__data__, h.index, h.group), delete i[d]) : +d < t && (h.state = Ti, h.timer.stop(), h.on.call("cancel", e, e.__data__, h.index, h.group), delete i[d]);
      }
    if (ja(function() {
      n.state === Di && (n.state = Ca, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = $o, n.on.call("start", e, e.__data__, n.index, n.group), n.state === $o) {
      for (n.state = Di, r = new Array(p = n.tween.length), d = 0, f = -1; d < p; ++d)
        (h = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (r[++f] = h);
      r.length = f + 1;
    }
  }
  function c(l) {
    for (var d = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(u), n.state = Po, 1), f = -1, p = r.length; ++f < p; )
      r[f].call(e, d);
    n.state === Po && (n.on.call("end", e, e.__data__, n.index, n.group), u());
  }
  function u() {
    n.state = Ti, n.timer.stop(), delete i[t];
    for (var l in i) return;
    delete e.__transition;
  }
}
function $i(e, t) {
  var n = e.__transition, i, r, s = !0, a;
  if (n) {
    t = t == null ? null : t + "";
    for (a in n) {
      if ((i = n[a]).name !== t) {
        s = !1;
        continue;
      }
      r = i.state > $o && i.state < Po, i.state = Ti, i.timer.stop(), i.on.call(r ? "interrupt" : "cancel", e, e.__data__, i.index, i.group), delete n[a];
    }
    s && delete e.__transition;
  }
}
function ym(e) {
  return this.each(function() {
    $i(this, e);
  });
}
function mm(e, t) {
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
function xm(e, t, n) {
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
function wm(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var i = Ze(this.node(), n).tween, r = 0, s = i.length, a; r < s; ++r)
      if ((a = i[r]).name === e)
        return a.value;
    return null;
  }
  return this.each((t == null ? mm : xm)(n, e, t));
}
function gs(e, t, n) {
  var i = e._id;
  return e.each(function() {
    var r = nt(this, i);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return Ze(r, i).value[t];
  };
}
function iu(e, t) {
  var n;
  return (typeof t == "number" ? Je : t instanceof kt ? Fi : (n = kt(t)) ? (t = n, Fi) : Zl)(e, t);
}
function vm(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function bm(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Nm(e, t, n) {
  var i, r = n + "", s;
  return function() {
    var a = this.getAttribute(e);
    return a === r ? null : a === i ? s : s = t(i = a, n);
  };
}
function jm(e, t, n) {
  var i, r = n + "", s;
  return function() {
    var a = this.getAttributeNS(e.space, e.local);
    return a === r ? null : a === i ? s : s = t(i = a, n);
  };
}
function Sm(e, t, n) {
  var i, r, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttribute(e) : (a = this.getAttribute(e), u = c + "", a === u ? null : a === i && u === r ? s : (r = u, s = t(i = a, c)));
  };
}
function Cm(e, t, n) {
  var i, r, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (a = this.getAttributeNS(e.space, e.local), u = c + "", a === u ? null : a === i && u === r ? s : (r = u, s = t(i = a, c)));
  };
}
function Em(e, t) {
  var n = or(e), i = n === "transform" ? om : iu;
  return this.attrTween(e, typeof t == "function" ? (n.local ? Cm : Sm)(n, i, gs(this, "attr." + e, t)) : t == null ? (n.local ? bm : vm)(n) : (n.local ? jm : Nm)(n, i, t));
}
function km(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function Im(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function Am(e, t) {
  var n, i;
  function r() {
    var s = t.apply(this, arguments);
    return s !== i && (n = (i = s) && Im(e, s)), n;
  }
  return r._value = t, r;
}
function _m(e, t) {
  var n, i;
  function r() {
    var s = t.apply(this, arguments);
    return s !== i && (n = (i = s) && km(e, s)), n;
  }
  return r._value = t, r;
}
function Dm(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var i = or(e);
  return this.tween(n, (i.local ? Am : _m)(i, t));
}
function Tm(e, t) {
  return function() {
    hs(this, e).delay = +t.apply(this, arguments);
  };
}
function $m(e, t) {
  return t = +t, function() {
    hs(this, e).delay = t;
  };
}
function Pm(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Tm : $m)(t, e)) : Ze(this.node(), t).delay;
}
function Mm(e, t) {
  return function() {
    nt(this, e).duration = +t.apply(this, arguments);
  };
}
function Rm(e, t) {
  return t = +t, function() {
    nt(this, e).duration = t;
  };
}
function zm(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Mm : Rm)(t, e)) : Ze(this.node(), t).duration;
}
function Lm(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    nt(this, e).ease = t;
  };
}
function Vm(e) {
  var t = this._id;
  return arguments.length ? this.each(Lm(t, e)) : Ze(this.node(), t).ease;
}
function Om(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    nt(this, e).ease = n;
  };
}
function Hm(e) {
  if (typeof e != "function") throw new Error();
  return this.each(Om(this._id, e));
}
function Wm(e) {
  typeof e != "function" && (e = Pl(e));
  for (var t = this._groups, n = t.length, i = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = i[r] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new lt(i, this._parents, this._name, this._id);
}
function Bm(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, i = t.length, r = n.length, s = Math.min(i, r), a = new Array(i), c = 0; c < s; ++c)
    for (var u = t[c], l = n[c], d = u.length, f = a[c] = new Array(d), p, h = 0; h < d; ++h)
      (p = u[h] || l[h]) && (f[h] = p);
  for (; c < i; ++c)
    a[c] = t[c];
  return new lt(a, this._parents, this._name, this._id);
}
function Fm(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function Km(e, t, n) {
  var i, r, s = Fm(t) ? hs : nt;
  return function() {
    var a = s(this, e), c = a.on;
    c !== i && (r = (i = c).copy()).on(t, n), a.on = r;
  };
}
function Xm(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Ze(this.node(), n).on.on(e) : this.each(Km(n, e, t));
}
function qm(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function Ym() {
  return this.on("end.remove", qm(this._id));
}
function Um(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = ls(e));
  for (var i = this._groups, r = i.length, s = new Array(r), a = 0; a < r; ++a)
    for (var c = i[a], u = c.length, l = s[a] = new Array(u), d, f, p = 0; p < u; ++p)
      (d = c[p]) && (f = e.call(d, d.__data__, p, c)) && ("__data__" in d && (f.__data__ = d.__data__), l[p] = f, ar(l[p], t, n, p, l, Ze(d, n)));
  return new lt(s, this._parents, t, n);
}
function Zm(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = $l(e));
  for (var i = this._groups, r = i.length, s = [], a = [], c = 0; c < r; ++c)
    for (var u = i[c], l = u.length, d, f = 0; f < l; ++f)
      if (d = u[f]) {
        for (var p = e.call(d, d.__data__, f, u), h, y = Ze(d, n), w = 0, m = p.length; w < m; ++w)
          (h = p[w]) && ar(h, t, n, w, p, y);
        s.push(p), a.push(d);
      }
  return new lt(s, a, t, n);
}
var Gm = Zn.prototype.constructor;
function Jm() {
  return new Gm(this._groups, this._parents);
}
function Qm(e, t) {
  var n, i, r;
  return function() {
    var s = en(this, e), a = (this.style.removeProperty(e), en(this, e));
    return s === a ? null : s === n && a === i ? r : r = t(n = s, i = a);
  };
}
function ru(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function ex(e, t, n) {
  var i, r = n + "", s;
  return function() {
    var a = en(this, e);
    return a === r ? null : a === i ? s : s = t(i = a, n);
  };
}
function tx(e, t, n) {
  var i, r, s;
  return function() {
    var a = en(this, e), c = n(this), u = c + "";
    return c == null && (u = c = (this.style.removeProperty(e), en(this, e))), a === u ? null : a === i && u === r ? s : (r = u, s = t(i = a, c));
  };
}
function nx(e, t) {
  var n, i, r, s = "style." + t, a = "end." + s, c;
  return function() {
    var u = nt(this, e), l = u.on, d = u.value[s] == null ? c || (c = ru(t)) : void 0;
    (l !== n || r !== d) && (i = (n = l).copy()).on(a, r = d), u.on = i;
  };
}
function ix(e, t, n) {
  var i = (e += "") == "transform" ? rm : iu;
  return t == null ? this.styleTween(e, Qm(e, i)).on("end.style." + e, ru(e)) : typeof t == "function" ? this.styleTween(e, tx(e, i, gs(this, "style." + e, t))).each(nx(this._id, e)) : this.styleTween(e, ex(e, i, t), n).on("end.style." + e, null);
}
function rx(e, t, n) {
  return function(i) {
    this.style.setProperty(e, t.call(this, i), n);
  };
}
function ox(e, t, n) {
  var i, r;
  function s() {
    var a = t.apply(this, arguments);
    return a !== r && (i = (r = a) && rx(e, a, n)), i;
  }
  return s._value = t, s;
}
function sx(e, t, n) {
  var i = "style." + (e += "");
  if (arguments.length < 2) return (i = this.tween(i)) && i._value;
  if (t == null) return this.tween(i, null);
  if (typeof t != "function") throw new Error();
  return this.tween(i, ox(e, t, n ?? ""));
}
function ax(e) {
  return function() {
    this.textContent = e;
  };
}
function cx(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function lx(e) {
  return this.tween("text", typeof e == "function" ? cx(gs(this, "text", e)) : ax(e == null ? "" : e + ""));
}
function ux(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function dx(e) {
  var t, n;
  function i() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && ux(r)), t;
  }
  return i._value = e, i;
}
function fx(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, dx(e));
}
function px() {
  for (var e = this._name, t = this._id, n = ou(), i = this._groups, r = i.length, s = 0; s < r; ++s)
    for (var a = i[s], c = a.length, u, l = 0; l < c; ++l)
      if (u = a[l]) {
        var d = Ze(u, t);
        ar(u, e, n, l, a, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new lt(i, this._parents, e, n);
}
function hx() {
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
var gx = 0;
function lt(e, t, n, i) {
  this._groups = e, this._parents = t, this._name = n, this._id = i;
}
function ou() {
  return ++gx;
}
var ot = Zn.prototype;
lt.prototype = {
  constructor: lt,
  select: Um,
  selectAll: Zm,
  selectChild: ot.selectChild,
  selectChildren: ot.selectChildren,
  filter: Wm,
  merge: Bm,
  selection: Jm,
  transition: px,
  call: ot.call,
  nodes: ot.nodes,
  node: ot.node,
  size: ot.size,
  empty: ot.empty,
  each: ot.each,
  on: Xm,
  attr: Em,
  attrTween: Dm,
  style: ix,
  styleTween: sx,
  text: lx,
  textTween: fx,
  remove: Ym,
  tween: wm,
  delay: Pm,
  duration: zm,
  ease: Vm,
  easeVarying: Hm,
  end: hx,
  [Symbol.iterator]: ot[Symbol.iterator]
};
function yx(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var mx = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: yx
};
function xx(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function wx(e) {
  var t, n;
  e instanceof lt ? (t = e._id, e = e._name) : (t = ou(), (n = mx).time = ps(), e = e == null ? null : e + "");
  for (var i = this._groups, r = i.length, s = 0; s < r; ++s)
    for (var a = i[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && ar(u, e, t, l, a, n || xx(u, t));
  return new lt(i, this._parents, e, t);
}
Zn.prototype.interrupt = ym;
Zn.prototype.transition = wx;
const wi = (e) => () => e;
function vx(e, {
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
function at(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
at.prototype = {
  constructor: at,
  scale: function(e) {
    return e === 1 ? this : new at(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new at(this.k, this.x + this.k * e, this.y + this.k * t);
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
var cr = new at(1, 0, 0);
su.prototype = at.prototype;
function su(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return cr;
  return e.__zoom;
}
function co(e) {
  e.stopImmediatePropagation();
}
function xn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function bx(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function Nx() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function Ea() {
  return this.__zoom || cr;
}
function jx(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function Sx() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Cx(e, t, n) {
  var i = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], s = e.invertY(t[0][1]) - n[0][1], a = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > i ? (i + r) / 2 : Math.min(0, i) || Math.max(0, r),
    a > s ? (s + a) / 2 : Math.min(0, s) || Math.max(0, a)
  );
}
function au() {
  var e = bx, t = Nx, n = Cx, i = jx, r = Sx, s = [0, 1 / 0], a = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, u = _i, l = rr("start", "zoom", "end"), d, f, p, h = 500, y = 150, w = 0, m = 10;
  function x(C) {
    C.property("__zoom", Ea).on("wheel.zoom", k, { passive: !1 }).on("mousedown.zoom", A).on("dblclick.zoom", M).filter(r).on("touchstart.zoom", _).on("touchmove.zoom", T).on("touchend.zoom touchcancel.zoom", L).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  x.transform = function(C, I, E, D) {
    var $ = C.selection ? C.selection() : C;
    $.property("__zoom", Ea), C !== $ ? j(C, I, E, D) : $.interrupt().each(function() {
      N(this, arguments).event(D).start().zoom(null, typeof I == "function" ? I.apply(this, arguments) : I).end();
    });
  }, x.scaleBy = function(C, I, E, D) {
    x.scaleTo(C, function() {
      var $ = this.__zoom.k, P = typeof I == "function" ? I.apply(this, arguments) : I;
      return $ * P;
    }, E, D);
  }, x.scaleTo = function(C, I, E, D) {
    x.transform(C, function() {
      var $ = t.apply(this, arguments), P = this.__zoom, F = E == null ? v($) : typeof E == "function" ? E.apply(this, arguments) : E, B = P.invert(F), W = typeof I == "function" ? I.apply(this, arguments) : I;
      return n(g(b(P, W), F, B), $, a);
    }, E, D);
  }, x.translateBy = function(C, I, E, D) {
    x.transform(C, function() {
      return n(this.__zoom.translate(
        typeof I == "function" ? I.apply(this, arguments) : I,
        typeof E == "function" ? E.apply(this, arguments) : E
      ), t.apply(this, arguments), a);
    }, null, D);
  }, x.translateTo = function(C, I, E, D, $) {
    x.transform(C, function() {
      var P = t.apply(this, arguments), F = this.__zoom, B = D == null ? v(P) : typeof D == "function" ? D.apply(this, arguments) : D;
      return n(cr.translate(B[0], B[1]).scale(F.k).translate(
        typeof I == "function" ? -I.apply(this, arguments) : -I,
        typeof E == "function" ? -E.apply(this, arguments) : -E
      ), P, a);
    }, D, $);
  };
  function b(C, I) {
    return I = Math.max(s[0], Math.min(s[1], I)), I === C.k ? C : new at(I, C.x, C.y);
  }
  function g(C, I, E) {
    var D = I[0] - E[0] * C.k, $ = I[1] - E[1] * C.k;
    return D === C.x && $ === C.y ? C : new at(C.k, D, $);
  }
  function v(C) {
    return [(+C[0][0] + +C[1][0]) / 2, (+C[0][1] + +C[1][1]) / 2];
  }
  function j(C, I, E, D) {
    C.on("start.zoom", function() {
      N(this, arguments).event(D).start();
    }).on("interrupt.zoom end.zoom", function() {
      N(this, arguments).event(D).end();
    }).tween("zoom", function() {
      var $ = this, P = arguments, F = N($, P).event(D), B = t.apply($, P), W = E == null ? v(B) : typeof E == "function" ? E.apply($, P) : E, G = Math.max(B[1][0] - B[0][0], B[1][1] - B[0][1]), H = $.__zoom, q = typeof I == "function" ? I.apply($, P) : I, Q = u(H.invert(W).concat(G / H.k), q.invert(W).concat(G / q.k));
      return function(U) {
        if (U === 1) U = q;
        else {
          var R = Q(U), X = G / R[2];
          U = new at(X, W[0] - R[0] * X, W[1] - R[1] * X);
        }
        F.zoom(null, U);
      };
    });
  }
  function N(C, I, E) {
    return !E && C.__zooming || new S(C, I);
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
      var I = ze(this.that).datum();
      l.call(
        C,
        this.that,
        new vx(C, {
          sourceEvent: this.sourceEvent,
          target: x,
          transform: this.that.__zoom,
          dispatch: l
        }),
        I
      );
    }
  };
  function k(C, ...I) {
    if (!e.apply(this, arguments)) return;
    var E = N(this, I).event(C), D = this.__zoom, $ = Math.max(s[0], Math.min(s[1], D.k * Math.pow(2, i.apply(this, arguments)))), P = Ke(C);
    if (E.wheel)
      (E.mouse[0][0] !== P[0] || E.mouse[0][1] !== P[1]) && (E.mouse[1] = D.invert(E.mouse[0] = P)), clearTimeout(E.wheel);
    else {
      if (D.k === $) return;
      E.mouse = [P, D.invert(P)], $i(this), E.start();
    }
    xn(C), E.wheel = setTimeout(F, y), E.zoom("mouse", n(g(b(D, $), E.mouse[0], E.mouse[1]), E.extent, a));
    function F() {
      E.wheel = null, E.end();
    }
  }
  function A(C, ...I) {
    if (p || !e.apply(this, arguments)) return;
    var E = C.currentTarget, D = N(this, I, !0).event(C), $ = ze(C.view).on("mousemove.zoom", W, !0).on("mouseup.zoom", G, !0), P = Ke(C, E), F = C.clientX, B = C.clientY;
    Fl(C.view), co(C), D.mouse = [P, this.__zoom.invert(P)], $i(this), D.start();
    function W(H) {
      if (xn(H), !D.moved) {
        var q = H.clientX - F, Q = H.clientY - B;
        D.moved = q * q + Q * Q > w;
      }
      D.event(H).zoom("mouse", n(g(D.that.__zoom, D.mouse[0] = Ke(H, E), D.mouse[1]), D.extent, a));
    }
    function G(H) {
      $.on("mousemove.zoom mouseup.zoom", null), Kl(H.view, D.moved), xn(H), D.event(H).end();
    }
  }
  function M(C, ...I) {
    if (e.apply(this, arguments)) {
      var E = this.__zoom, D = Ke(C.changedTouches ? C.changedTouches[0] : C, this), $ = E.invert(D), P = E.k * (C.shiftKey ? 0.5 : 2), F = n(g(b(E, P), D, $), t.apply(this, I), a);
      xn(C), c > 0 ? ze(this).transition().duration(c).call(j, F, D, C) : ze(this).call(x.transform, F, D, C);
    }
  }
  function _(C, ...I) {
    if (e.apply(this, arguments)) {
      var E = C.touches, D = E.length, $ = N(this, I, C.changedTouches.length === D).event(C), P, F, B, W;
      for (co(C), F = 0; F < D; ++F)
        B = E[F], W = Ke(B, this), W = [W, this.__zoom.invert(W), B.identifier], $.touch0 ? !$.touch1 && $.touch0[2] !== W[2] && ($.touch1 = W, $.taps = 0) : ($.touch0 = W, P = !0, $.taps = 1 + !!d);
      d && (d = clearTimeout(d)), P && ($.taps < 2 && (f = W[0], d = setTimeout(function() {
        d = null;
      }, h)), $i(this), $.start());
    }
  }
  function T(C, ...I) {
    if (this.__zooming) {
      var E = N(this, I).event(C), D = C.changedTouches, $ = D.length, P, F, B, W;
      for (xn(C), P = 0; P < $; ++P)
        F = D[P], B = Ke(F, this), E.touch0 && E.touch0[2] === F.identifier ? E.touch0[0] = B : E.touch1 && E.touch1[2] === F.identifier && (E.touch1[0] = B);
      if (F = E.that.__zoom, E.touch1) {
        var G = E.touch0[0], H = E.touch0[1], q = E.touch1[0], Q = E.touch1[1], U = (U = q[0] - G[0]) * U + (U = q[1] - G[1]) * U, R = (R = Q[0] - H[0]) * R + (R = Q[1] - H[1]) * R;
        F = b(F, Math.sqrt(U / R)), B = [(G[0] + q[0]) / 2, (G[1] + q[1]) / 2], W = [(H[0] + Q[0]) / 2, (H[1] + Q[1]) / 2];
      } else if (E.touch0) B = E.touch0[0], W = E.touch0[1];
      else return;
      E.zoom("touch", n(g(F, B, W), E.extent, a));
    }
  }
  function L(C, ...I) {
    if (this.__zooming) {
      var E = N(this, I).event(C), D = C.changedTouches, $ = D.length, P, F;
      for (co(C), p && clearTimeout(p), p = setTimeout(function() {
        p = null;
      }, h), P = 0; P < $; ++P)
        F = D[P], E.touch0 && E.touch0[2] === F.identifier ? delete E.touch0 : E.touch1 && E.touch1[2] === F.identifier && delete E.touch1;
      if (E.touch1 && !E.touch0 && (E.touch0 = E.touch1, delete E.touch1), E.touch0) E.touch0[1] = this.__zoom.invert(E.touch0[0]);
      else if (E.end(), E.taps === 2 && (F = Ke(F, this), Math.hypot(f[0] - F[0], f[1] - F[1]) < m)) {
        var B = ze(this).on("dblclick.zoom");
        B && B.apply(this, arguments);
      }
    }
  }
  return x.wheelDelta = function(C) {
    return arguments.length ? (i = typeof C == "function" ? C : wi(+C), x) : i;
  }, x.filter = function(C) {
    return arguments.length ? (e = typeof C == "function" ? C : wi(!!C), x) : e;
  }, x.touchable = function(C) {
    return arguments.length ? (r = typeof C == "function" ? C : wi(!!C), x) : r;
  }, x.extent = function(C) {
    return arguments.length ? (t = typeof C == "function" ? C : wi([[+C[0][0], +C[0][1]], [+C[1][0], +C[1][1]]]), x) : t;
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
    return arguments.length ? (m = +C, x) : m;
  }, x;
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
}, On = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], cu = ["Enter", " ", "Escape"], lu = {
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
var St;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(St || (St = {}));
var Hn;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Hn || (Hn = {}));
const uu = {
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
var ht;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(ht || (ht = {}));
var Yi;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(Yi || (Yi = {}));
var re;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(re || (re = {}));
const ka = {
  [re.Left]: re.Right,
  [re.Right]: re.Left,
  [re.Top]: re.Bottom,
  [re.Bottom]: re.Top
};
function du(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const fu = (e) => "id" in e && "source" in e && "target" in e, Ex = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), ys = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), Jn = (e, t = [0, 0]) => {
  const { width: n, height: i } = ut(e), r = e.origin ?? t, s = n * r[0], a = i * r[1];
  return {
    x: e.position.x - s,
    y: e.position.y - a
  };
}, kx = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((i, r) => {
    const s = typeof r == "string";
    let a = !t.nodeLookup && !s ? r : void 0;
    t.nodeLookup && (a = s ? t.nodeLookup.get(r) : ys(r) ? r : t.nodeLookup.get(r.id));
    const c = a ? Ui(a, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return lr(i, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return ur(n);
}, Qn = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, i = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = lr(n, Ui(r)), i = !0);
  }), i ? ur(n) : { x: 0, y: 0, width: 0, height: 0 };
}, ms = (e, t, [n, i, r] = [0, 0, 1], s = !1, a = !1) => {
  const c = {
    ...fn(t, [n, i, r]),
    width: t.width / r,
    height: t.height / r
  }, u = [];
  for (const l of e.values()) {
    const { measured: d, selectable: f = !0, hidden: p = !1 } = l;
    if (a && !f || p)
      continue;
    const h = d.width ?? l.width ?? l.initialWidth ?? null, y = d.height ?? l.height ?? l.initialHeight ?? null, w = Wn(c, on(l)), m = (h ?? 0) * (y ?? 0), x = s && w > 0;
    (!l.internals.handleBounds || x || w >= m || l.dragging) && u.push(l);
  }
  return u;
}, Ix = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((i) => {
    n.add(i.id);
  }), t.filter((i) => n.has(i.source) || n.has(i.target));
};
function Ax(e, t) {
  const n = /* @__PURE__ */ new Map(), i = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!i || i.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function _x({ nodes: e, width: t, height: n, panZoom: i, minZoom: r, maxZoom: s }, a) {
  if (e.size === 0)
    return !0;
  const c = Ax(e, a), u = Qn(c), l = ws(u, t, n, a?.minZoom ?? r, a?.maxZoom ?? s, a?.padding ?? 0.1);
  return await i.setViewport(l, {
    duration: a?.duration,
    ease: a?.ease,
    interpolate: a?.interpolate
  }), !0;
}
function pu({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: i = [0, 0], nodeExtent: r, onError: s }) {
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
  else c && _t(a.extent) && (f = [
    [a.extent[0][0] + u, a.extent[0][1] + l],
    [a.extent[1][0] + u, a.extent[1][1] + l]
  ]);
  const p = _t(f) ? At(t, f, a.measured) : t;
  return (a.measured.width === void 0 || a.measured.height === void 0) && s?.("015", He.error015()), {
    position: {
      x: p.x - u + (a.measured.width ?? 0) * d[0],
      y: p.y - l + (a.measured.height ?? 0) * d[1]
    },
    positionAbsolute: p
  };
}
async function Dx({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: i, onBeforeDelete: r }) {
  const s = new Set(e.map((p) => p.id)), a = [];
  for (const p of n) {
    if (p.deletable === !1)
      continue;
    const h = s.has(p.id), y = !h && p.parentId && a.find((w) => w.id === p.parentId);
    (h || y) && a.push(p);
  }
  const c = new Set(t.map((p) => p.id)), u = i.filter((p) => p.deletable !== !1), d = Ix(a, u);
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
const rn = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), At = (e = { x: 0, y: 0 }, t, n) => ({
  x: rn(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: rn(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function hu(e, t, n) {
  const { width: i, height: r } = ut(n), { x: s, y: a } = n.internals.positionAbsolute;
  return At(e, [
    [s, a],
    [s + i, a + r]
  ], t);
}
const Ia = (e, t, n) => e < t ? rn(Math.abs(e - t), 1, t) / t : e > n ? -rn(Math.abs(e - n), 1, t) / t : 0, xs = (e, t, n = 15, i = 40) => {
  const r = Ia(e.x, i, t.width - i) * n, s = Ia(e.y, i, t.height - i) * n;
  return [r, s];
}, lr = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), Mo = ({ x: e, y: t, width: n, height: i }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + i
}), ur = ({ x: e, y: t, x2: n, y2: i }) => ({
  x: e,
  y: t,
  width: n - e,
  height: i - t
}), on = (e, t = [0, 0]) => {
  const { x: n, y: i } = ys(e) ? e.internals.positionAbsolute : Jn(e, t);
  return {
    x: n,
    y: i,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, Ui = (e, t = [0, 0]) => {
  const { x: n, y: i } = ys(e) ? e.internals.positionAbsolute : Jn(e, t);
  return {
    x: n,
    y: i,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: i + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, gu = (e, t) => ur(lr(Mo(e), Mo(t))), Wn = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), i = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * i);
}, Aa = (e) => qe(e.width) && qe(e.height) && qe(e.x) && qe(e.y), qe = (e) => !isNaN(e) && isFinite(e), yu = (e, t) => (n, i) => {
}, ei = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), fn = ({ x: e, y: t }, [n, i, r], s = !1, a = [1, 1]) => {
  const c = {
    x: (e - n) / r,
    y: (t - i) / r
  };
  return s ? ei(c, a) : c;
}, sn = ({ x: e, y: t }, [n, i, r]) => ({
  x: e * r + n,
  y: t * r + i
});
function Ht(e, t) {
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
function Tx(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const i = Ht(e, n), r = Ht(e, t);
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
    const i = Ht(e.top ?? e.y ?? 0, n), r = Ht(e.bottom ?? e.y ?? 0, n), s = Ht(e.left ?? e.x ?? 0, t), a = Ht(e.right ?? e.x ?? 0, t);
    return { top: i, right: a, bottom: r, left: s, x: s + a, y: i + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function $x(e, t, n, i, r, s) {
  const { x: a, y: c } = sn(e, [t, n, i]), { x: u, y: l } = sn({ x: e.x + e.width, y: e.y + e.height }, [t, n, i]), d = r - u, f = s - l;
  return {
    left: Math.floor(a),
    top: Math.floor(c),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const ws = (e, t, n, i, r, s) => {
  const a = Tx(s, t, n), c = (t - a.x) / e.width, u = (n - a.y) / e.height, l = Math.min(c, u), d = rn(l, i, r), f = e.x + e.width / 2, p = e.y + e.height / 2, h = t / 2 - f * d, y = n / 2 - p * d, w = $x(e, h, y, d, t, n), m = {
    left: Math.min(w.left - a.left, 0),
    top: Math.min(w.top - a.top, 0),
    right: Math.min(w.right - a.right, 0),
    bottom: Math.min(w.bottom - a.bottom, 0)
  };
  return {
    x: h - m.left + m.right,
    y: y - m.top + m.bottom,
    zoom: d
  };
}, Bn = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function _t(e) {
  return e != null && e !== "parent";
}
function ut(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function mu(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function xu(e, t = { width: 0, height: 0 }, n, i, r) {
  const s = { ...e }, a = i.get(n);
  if (a) {
    const c = a.origin || r;
    s.x += a.internals.positionAbsolute.x - (t.width ?? 0) * c[0], s.y += a.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return s;
}
function _a(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function Px() {
  let e, t;
  return { promise: new Promise((i, r) => {
    e = i, t = r;
  }), resolve: e, reject: t };
}
function Mx(e) {
  return { ...lu, ...e || {} };
}
function An(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: i, containerBounds: r }) {
  const { x: s, y: a } = Ye(e), c = fn({ x: s - (r?.left ?? 0), y: a - (r?.top ?? 0) }, i), { x: u, y: l } = n ? ei(c, t) : c;
  return {
    xSnapped: u,
    ySnapped: l,
    ...c
  };
}
const vs = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), wu = (e) => e?.getRootNode?.() || window?.document, Rx = ["INPUT", "SELECT", "TEXTAREA"];
function vu(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : Rx.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const bu = (e) => "clientX" in e, Ye = (e, t) => {
  const n = bu(e), i = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: i - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, Da = (e, t, n, i, r) => {
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
      ...vs(a)
    };
  });
};
function Nu({ sourceX: e, sourceY: t, targetX: n, targetY: i, sourceControlX: r, sourceControlY: s, targetControlX: a, targetControlY: c }) {
  const u = e * 0.125 + r * 0.375 + a * 0.375 + n * 0.125, l = t * 0.125 + s * 0.375 + c * 0.375 + i * 0.125, d = Math.abs(u - e), f = Math.abs(l - t);
  return [u, l, d, f];
}
function vi(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function Ta({ pos: e, x1: t, y1: n, x2: i, y2: r, c: s }) {
  switch (e) {
    case re.Left:
      return [t - vi(t - i, s), n];
    case re.Right:
      return [t + vi(i - t, s), n];
    case re.Top:
      return [t, n - vi(n - r, s)];
    case re.Bottom:
      return [t, n + vi(r - n, s)];
  }
}
function ju({ sourceX: e, sourceY: t, sourcePosition: n = re.Bottom, targetX: i, targetY: r, targetPosition: s = re.Top, curvature: a = 0.25 }) {
  const [c, u] = Ta({
    pos: n,
    x1: e,
    y1: t,
    x2: i,
    y2: r,
    c: a
  }), [l, d] = Ta({
    pos: s,
    x1: i,
    y1: r,
    x2: e,
    y2: t,
    c: a
  }), [f, p, h, y] = Nu({
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
function Su({ sourceX: e, sourceY: t, targetX: n, targetY: i }) {
  const r = Math.abs(n - e) / 2, s = n < e ? n + r : n - r, a = Math.abs(i - t) / 2, c = i < t ? i + a : i - a;
  return [s, c, r, a];
}
function zx({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: i = 0, elevateOnSelect: r = !1, zIndexMode: s = "basic" }) {
  if (s === "manual")
    return i;
  const a = r && n ? i + 1e3 : i, c = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return a + c;
}
function Lx({ sourceNode: e, targetNode: t, width: n, height: i, transform: r }) {
  const s = lr(Ui(e), Ui(t));
  s.x === s.x2 && (s.x2 += 1), s.y === s.y2 && (s.y2 += 1);
  const a = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: i / r[2]
  };
  return Wn(a, ur(s)) > 0;
}
const Cu = ({ source: e, sourceHandle: t, target: n, targetHandle: i }) => `xy-edge__${e}${t || ""}-${n}${i || ""}`, Vx = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), Ox = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", He.error006()), t;
  const i = n.getEdgeId || Cu;
  let r;
  return fu(e) ? r = { ...e } : r = {
    ...e,
    id: i(e)
  }, Vx(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, Hx = (e, t, n, i = { shouldReplaceId: !0 }) => {
  const { id: r, ...s } = e;
  if (!t.source || !t.target)
    return i.onError?.("006", He.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return i.onError?.("007", He.error007(r)), n;
  const c = i.getEdgeId || Cu, u = {
    ...s,
    id: i.shouldReplaceId ? c(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== r).concat(u);
};
function Eu({ sourceX: e, sourceY: t, targetX: n, targetY: i }) {
  const [r, s, a, c] = Su({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: i
  });
  return [`M ${e},${t}L ${n},${i}`, r, s, a, c];
}
const $a = {
  [re.Left]: { x: -1, y: 0 },
  [re.Right]: { x: 1, y: 0 },
  [re.Top]: { x: 0, y: -1 },
  [re.Bottom]: { x: 0, y: 1 }
}, Wx = ({ source: e, sourcePosition: t = re.Bottom, target: n }) => t === re.Left || t === re.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, Pa = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Bx({ source: e, sourcePosition: t = re.Bottom, target: n, targetPosition: i = re.Top, center: r, offset: s, stepPosition: a }) {
  const c = $a[t], u = $a[i], l = { x: e.x + c.x * s, y: e.y + c.y * s }, d = { x: n.x + u.x * s, y: n.y + u.y * s }, f = Wx({
    source: l,
    sourcePosition: t,
    target: d
  }), p = f.x !== 0 ? "x" : "y", h = f[p];
  let y = [], w, m;
  const x = { x: 0, y: 0 }, b = { x: 0, y: 0 }, [, , g, v] = Su({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[p] * u[p] === -1) {
    p === "x" ? (w = r.x ?? l.x + (d.x - l.x) * a, m = r.y ?? (l.y + d.y) / 2) : (w = r.x ?? (l.x + d.x) / 2, m = r.y ?? l.y + (d.y - l.y) * a);
    const k = [
      { x: w, y: l.y },
      { x: w, y: d.y }
    ], A = [
      { x: l.x, y: m },
      { x: d.x, y: m }
    ];
    c[p] === h ? y = p === "x" ? k : A : y = p === "x" ? A : k;
  } else {
    const k = [{ x: l.x, y: d.y }], A = [{ x: d.x, y: l.y }];
    if (p === "x" ? y = c.x === h ? A : k : y = c.y === h ? k : A, t === i) {
      const C = Math.abs(e[p] - n[p]);
      if (C <= s) {
        const I = Math.min(s - 1, s - C);
        c[p] === h ? x[p] = (l[p] > e[p] ? -1 : 1) * I : b[p] = (d[p] > n[p] ? -1 : 1) * I;
      }
    }
    if (t !== i) {
      const C = p === "x" ? "y" : "x", I = c[p] === u[C], E = l[C] > d[C], D = l[C] < d[C];
      (c[p] === 1 && (!I && E || I && D) || c[p] !== 1 && (!I && D || I && E)) && (y = p === "x" ? k : A);
    }
    const M = { x: l.x + x.x, y: l.y + x.y }, _ = { x: d.x + b.x, y: d.y + b.y }, T = Math.max(Math.abs(M.x - y[0].x), Math.abs(_.x - y[0].x)), L = Math.max(Math.abs(M.y - y[0].y), Math.abs(_.y - y[0].y));
    T >= L ? (w = (M.x + _.x) / 2, m = y[0].y) : (w = y[0].x, m = (M.y + _.y) / 2);
  }
  const j = { x: l.x + x.x, y: l.y + x.y }, N = { x: d.x + b.x, y: d.y + b.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...j.x !== y[0].x || j.y !== y[0].y ? [j] : [],
    ...y,
    ...N.x !== y[y.length - 1].x || N.y !== y[y.length - 1].y ? [N] : [],
    n
  ], w, m, g, v];
}
function Fx(e, t, n, i) {
  const r = Math.min(Pa(e, t) / 2, Pa(t, n) / 2, i), { x: s, y: a } = t;
  if (e.x === s && s === n.x || e.y === a && a === n.y)
    return `L${s} ${a}`;
  if (e.y === a) {
    const l = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${s + r * l},${a}Q ${s},${a} ${s},${a + r * d}`;
  }
  const c = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${s},${a + r * u}Q ${s},${a} ${s + r * c},${a}`;
}
function Zi({ sourceX: e, sourceY: t, sourcePosition: n = re.Bottom, targetX: i, targetY: r, targetPosition: s = re.Top, borderRadius: a = 5, centerX: c, centerY: u, offset: l = 20, stepPosition: d = 0.5 }) {
  const [f, p, h, y, w] = Bx({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: i, y: r },
    targetPosition: s,
    center: { x: c, y: u },
    offset: l,
    stepPosition: d
  });
  let m = `M${f[0].x} ${f[0].y}`;
  for (let x = 1; x < f.length - 1; x++)
    m += Fx(f[x - 1], f[x], f[x + 1], a);
  return m += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [m, p, h, y, w];
}
function Ma(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function Kx(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!Ma(t) || !Ma(n))
    return null;
  const i = t.internals.handleBounds || Ra(t.handles), r = n.internals.handleBounds || Ra(n.handles), s = za(i?.source ?? [], e.sourceHandle), a = za(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === nn.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!s || !a)
    return e.onError?.("008", He.error008(s ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = s?.position || re.Bottom, u = a?.position || re.Top, l = Dt(t, s, c), d = Dt(n, a, u);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: d.x,
    targetY: d.y,
    sourcePosition: c,
    targetPosition: u
  };
}
function Ra(e) {
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
function Dt(e, t, n = re.Left, i = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, s = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: a, height: c } = t ?? ut(e);
  if (i)
    return { x: r + a / 2, y: s + c / 2 };
  switch (t?.position ?? n) {
    case re.Top:
      return { x: r + a / 2, y: s };
    case re.Right:
      return { x: r + a, y: s + c / 2 };
    case re.Bottom:
      return { x: r + a / 2, y: s + c };
    case re.Left:
      return { x: r, y: s + c / 2 };
  }
}
function za(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Ro(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((i) => `${i}=${e[i]}`).join("&")}` : "";
}
function Xx(e, { id: t, defaultColor: n, defaultMarkerStart: i, defaultMarkerEnd: r }) {
  const s = /* @__PURE__ */ new Set();
  return e.reduce((a, c) => ([c.markerStart || i, c.markerEnd || r].forEach((u) => {
    if (u && typeof u == "object") {
      const l = Ro(u, t);
      s.has(l) || (a.push({ id: l, color: u.color || n, ...u }), s.add(l));
    }
  }), a), []).sort((a, c) => a.id.localeCompare(c.id));
}
const ku = 1e3, qx = 10, bs = {
  nodeOrigin: [0, 0],
  nodeExtent: On,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Yx = {
  ...bs,
  checkEquality: !0
};
function Ns(e, t) {
  const n = { ...e };
  for (const i in t)
    t[i] !== void 0 && (n[i] = t[i]);
  return n;
}
function Ux(e, t, n) {
  const i = Ns(bs, n);
  for (const r of e.values())
    if (r.parentId)
      Ss(r, e, t, i);
    else {
      const s = Jn(r, i.nodeOrigin), a = _t(r.extent) ? r.extent : i.nodeExtent, c = At(s, a, ut(r));
      r.internals.positionAbsolute = c;
    }
}
function Zx(e, t) {
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
function js(e) {
  return e === "manual";
}
function zo(e, t, n, i = {}) {
  const r = Ns(Yx, i), s = { i: 0 }, a = new Map(t), c = r?.elevateNodesOnSelect && !js(r.zIndexMode) ? ku : 0;
  let u = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = a.get(d.id);
    if (r.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const p = Jn(d, r.nodeOrigin), h = _t(d.extent) ? d.extent : r.nodeExtent, y = At(p, h, ut(d));
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
          handleBounds: Zx(d, f),
          z: Iu(d, c, r.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (u = !1), d.parentId && Ss(f, t, n, i, s), l ||= d.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: l };
}
function Gx(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function Ss(e, t, n, i, r) {
  const { elevateNodesOnSelect: s, nodeOrigin: a, nodeExtent: c, zIndexMode: u } = Ns(bs, i), l = e.parentId, d = t.get(l);
  if (!d) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Gx(e, n), r && !d.parentId && d.internals.rootParentIndex === void 0 && u === "auto" && (d.internals.rootParentIndex = ++r.i, d.internals.z = d.internals.z + r.i * qx), r && d.internals.rootParentIndex !== void 0 && (r.i = d.internals.rootParentIndex);
  const f = s && !js(u) ? ku : 0, { x: p, y: h, z: y } = Jx(e, d, a, c, f, u), { positionAbsolute: w } = e.internals, m = p !== w.x || h !== w.y;
  (m || y !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: m ? { x: p, y: h } : w,
      z: y
    }
  });
}
function Iu(e, t, n) {
  const i = qe(e.zIndex) ? e.zIndex : 0;
  return js(n) ? i : i + (e.selected ? t : 0);
}
function Jx(e, t, n, i, r, s) {
  const { x: a, y: c } = t.internals.positionAbsolute, u = ut(e), l = Jn(e, n), d = _t(e.extent) ? At(l, e.extent, u) : l;
  let f = At({ x: a + d.x, y: c + d.y }, i, u);
  e.extent === "parent" && (f = hu(f, u, t));
  const p = Iu(e, r, s), h = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: h >= p ? h + 1 : p
  };
}
function Cs(e, t, n, i = [0, 0]) {
  const r = [], s = /* @__PURE__ */ new Map();
  for (const a of e) {
    const c = t.get(a.parentId);
    if (!c)
      continue;
    const u = s.get(a.parentId)?.expandedRect ?? on(c), l = gu(u, a.rect);
    s.set(a.parentId, { expandedRect: l, parent: c });
  }
  return s.size > 0 && s.forEach(({ expandedRect: a, parent: c }, u) => {
    const l = c.internals.positionAbsolute, d = ut(c), f = c.origin ?? i, p = a.x < l.x ? Math.round(Math.abs(l.x - a.x)) : 0, h = a.y < l.y ? Math.round(Math.abs(l.y - a.y)) : 0, y = Math.max(d.width, Math.round(a.width)), w = Math.max(d.height, Math.round(a.height)), m = (y - d.width) * f[0], x = (w - d.height) * f[1];
    (p > 0 || h > 0 || m || x) && (r.push({
      id: u,
      type: "position",
      position: {
        x: c.position.x - p + m,
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
        width: y + (p ? f[0] * p - m : 0),
        height: w + (h ? f[1] * h - x : 0)
      }
    });
  }), r;
}
function Qx(e, t, n, i, r, s, a) {
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
    const w = vs(h.nodeElement), m = y.measured.width !== w.width || y.measured.height !== w.height;
    if (!!(w.width && w.height && (m || !y.internals.handleBounds || h.force))) {
      const b = h.nodeElement.getBoundingClientRect(), g = _t(y.extent) ? y.extent : s;
      let { positionAbsolute: v } = y.internals;
      y.parentId && y.extent === "parent" ? v = hu(v, w, t.get(y.parentId)) : g && (v = At(v, g, w));
      const j = {
        ...y,
        measured: w,
        internals: {
          ...y.internals,
          positionAbsolute: v,
          handleBounds: {
            source: Da("source", h.nodeElement, b, f, y.id),
            target: Da("target", h.nodeElement, b, f, y.id)
          }
        }
      };
      t.set(y.id, j), y.parentId && Ss(j, t, n, { nodeOrigin: r, zIndexMode: a }), u = !0, m && (l.push({
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
    const h = Cs(p, t, n, r);
    l.push(...h);
  }
  return { changes: l, updatedInternals: u };
}
async function ew({ delta: e, panZoom: t, transform: n, translateExtent: i, width: r, height: s }) {
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
function La(e, t, n, i, r, s) {
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
function Au(e, t, n) {
  e.clear(), t.clear();
  for (const i of n) {
    const { source: r, target: s, sourceHandle: a = null, targetHandle: c = null } = i, u = { edgeId: i.id, source: r, target: s, sourceHandle: a, targetHandle: c }, l = `${r}-${a}--${s}-${c}`, d = `${s}-${c}--${r}-${a}`;
    La("source", u, d, e, r, a), La("target", u, l, e, s, c), t.set(i.id, i);
  }
}
function _u(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : _u(n, t) : !1;
}
function Va(e, t, n) {
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
function tw(e, t, n, i) {
  const r = /* @__PURE__ */ new Map();
  for (const [s, a] of e)
    if ((a.selected || a.id === i) && (!a.parentId || !_u(a, e)) && (a.draggable || t && typeof a.draggable > "u")) {
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
function nw({ dragItems: e, snapGrid: t, x: n, y: i }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const s = {
    x: n - r.distance.x,
    y: i - r.distance.y
  }, a = ei(s, t);
  return {
    x: a.x - s.x,
    y: a.y - s.y
  };
}
function iw({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: i, onDragStop: r }) {
  let s = { x: null, y: null }, a = 0, c = /* @__PURE__ */ new Map(), u = !1, l = { x: 0, y: 0 }, d = null, f = !1, p = null, h = !1, y = !1, w = null;
  function m({ noDragClassName: b, handleSelector: g, domNode: v, isSelectable: j, nodeId: N, nodeClickDistance: S = 0 }) {
    p = ze(v);
    function k({ x: T, y: L }) {
      const { nodeLookup: C, nodeExtent: I, snapGrid: E, snapToGrid: D, nodeOrigin: $, onNodeDrag: P, onSelectionDrag: F, onError: B, updateNodePositions: W } = t();
      s = { x: T, y: L };
      let G = !1;
      const H = c.size > 1, q = H && I ? Mo(Qn(c)) : null, Q = H && D ? nw({
        dragItems: c,
        snapGrid: E,
        x: T,
        y: L
      }) : null;
      for (const [U, R] of c) {
        if (!C.has(U))
          continue;
        let X = { x: T - R.distance.x, y: L - R.distance.y };
        D && (X = Q ? {
          x: Math.round(X.x + Q.x),
          y: Math.round(X.y + Q.y)
        } : ei(X, E));
        let ce = null;
        if (H && I && !R.extent && q) {
          const { positionAbsolute: ie } = R.internals, de = ie.x - q.x + I[0][0], V = ie.x + R.measured.width - q.x2 + I[1][0], ne = ie.y - q.y + I[0][1], he = ie.y + R.measured.height - q.y2 + I[1][1];
          ce = [
            [de, ne],
            [V, he]
          ];
        }
        const { position: oe, positionAbsolute: ee } = pu({
          nodeId: U,
          nextPosition: X,
          nodeLookup: C,
          nodeExtent: ce || I,
          nodeOrigin: $,
          onError: B
        });
        G = G || R.position.x !== oe.x || R.position.y !== oe.y, R.position = oe, R.internals.positionAbsolute = ee;
      }
      if (y = y || G, !!G && (W(c, !0), w && (i || P || !N && F))) {
        const [U, R] = lo({
          nodeId: N,
          dragItems: c,
          nodeLookup: C
        });
        i?.(w, c, U, R), P?.(w, U, R), N || F?.(w, R);
      }
    }
    async function A() {
      if (!d)
        return;
      const { transform: T, panBy: L, autoPanSpeed: C, autoPanOnNodeDrag: I } = t();
      if (!I) {
        u = !1, cancelAnimationFrame(a);
        return;
      }
      const [E, D] = xs(l, d, C);
      (E !== 0 || D !== 0) && (s.x = (s.x ?? 0) - E / T[2], s.y = (s.y ?? 0) - D / T[2], await L({ x: E, y: D }) && k(s)), a = requestAnimationFrame(A);
    }
    function M(T) {
      const { nodeLookup: L, multiSelectionActive: C, nodesDraggable: I, transform: E, snapGrid: D, snapToGrid: $, selectNodesOnDrag: P, onNodeDragStart: F, onSelectionDragStart: B, unselectNodesAndEdges: W } = t();
      f = !0, (!P || !j) && !C && N && (L.get(N)?.selected || W()), j && P && N && e?.(N);
      const G = An(T.sourceEvent, { transform: E, snapGrid: D, snapToGrid: $, containerBounds: d });
      if (s = G, c = tw(L, I, G, N), c.size > 0 && (n || F || !N && B)) {
        const [H, q] = lo({
          nodeId: N,
          dragItems: c,
          nodeLookup: L
        });
        n?.(T.sourceEvent, c, H, q), F?.(T.sourceEvent, H, q), N || B?.(T.sourceEvent, q);
      }
    }
    const _ = Xl().clickDistance(S).on("start", (T) => {
      const { domNode: L, nodeDragThreshold: C, transform: I, snapGrid: E, snapToGrid: D } = t();
      d = L?.getBoundingClientRect() || null, h = !1, y = !1, w = T.sourceEvent, C === 0 && M(T), s = An(T.sourceEvent, { transform: I, snapGrid: E, snapToGrid: D, containerBounds: d }), l = Ye(T.sourceEvent, d);
    }).on("drag", (T) => {
      const { autoPanOnNodeDrag: L, transform: C, snapGrid: I, snapToGrid: E, nodeDragThreshold: D, nodeLookup: $ } = t(), P = An(T.sourceEvent, { transform: C, snapGrid: I, snapToGrid: E, containerBounds: d });
      if (w = T.sourceEvent, (T.sourceEvent.type === "touchmove" && T.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      N && !$.has(N)) && (h = !0), !h) {
        if (!u && L && f && (u = !0, A()), !f) {
          const F = Ye(T.sourceEvent, d), B = F.x - l.x, W = F.y - l.y;
          Math.sqrt(B * B + W * W) > D && M(T);
        }
        (s.x !== P.xSnapped || s.y !== P.ySnapped) && c && f && (l = Ye(T.sourceEvent, d), k(P));
      }
    }).on("end", (T) => {
      if (!f || h) {
        h && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (u = !1, f = !1, cancelAnimationFrame(a), c.size > 0) {
        const { nodeLookup: L, updateNodePositions: C, onNodeDragStop: I, onSelectionDragStop: E } = t();
        if (y && (C(c, !1), y = !1), r || I || !N && E) {
          const [D, $] = lo({
            nodeId: N,
            dragItems: c,
            nodeLookup: L,
            dragging: !1
          });
          r?.(T.sourceEvent, c, D, $), I?.(T.sourceEvent, D, $), N || E?.(T.sourceEvent, $);
        }
      }
    }).filter((T) => {
      const L = T.target;
      return !T.button && (!b || !Va(L, `.${b}`, v)) && (!g || Va(L, g, v));
    });
    p.call(_);
  }
  function x() {
    p?.on(".drag", null);
  }
  return {
    update: m,
    destroy: x
  };
}
function rw(e, t, n) {
  const i = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const s of t.values())
    Wn(r, on(s)) > 0 && i.push(s);
  return i;
}
const ow = 250;
function sw(e, t, n, i) {
  let r = [], s = 1 / 0;
  const a = rw(e, n, t + ow);
  for (const c of a) {
    const u = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of u) {
      if (i.nodeId === l.nodeId && i.type === l.type && i.id === l.id)
        continue;
      const { x: d, y: f } = Dt(c, l, l.position, !0), p = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(f - e.y, 2));
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
function Du(e, t, n, i, r, s = !1) {
  const a = i.get(e);
  if (!a)
    return null;
  const c = r === "strict" ? a.internals.handleBounds?.[t] : [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []], u = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return u && s ? { ...u, ...Dt(a, u, u.position, !0) } : u;
}
function Tu(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function aw(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const $u = () => !0;
function cw(e, { connectionMode: t, connectionRadius: n, handleId: i, nodeId: r, edgeUpdaterType: s, isTarget: a, domNode: c, nodeLookup: u, lib: l, autoPanOnConnect: d, flowId: f, panBy: p, cancelConnection: h, onConnectStart: y, onConnect: w, onConnectEnd: m, isValidConnection: x = $u, onReconnectEnd: b, updateConnection: g, getTransform: v, getFromHandle: j, autoPanSpeed: N, dragThreshold: S = 1, handleDomNode: k }) {
  const A = wu(e.target);
  let M = 0, _;
  const { x: T, y: L } = Ye(e), C = Tu(s, k), I = c?.getBoundingClientRect();
  let E = !1;
  if (!I || !C)
    return;
  const D = Du(r, C, i, u, t);
  if (!D)
    return;
  let $ = Ye(e, I), P = !1, F = null, B = !1, W = null;
  function G() {
    if (!d || !I)
      return;
    const [oe, ee] = xs($, I, N);
    p({ x: oe, y: ee }), M = requestAnimationFrame(G);
  }
  const H = {
    ...D,
    nodeId: r,
    type: C,
    position: D.position
  }, q = u.get(r);
  let U = {
    inProgress: !0,
    isValid: null,
    from: Dt(q, H, re.Left, !0),
    fromHandle: H,
    fromPosition: H.position,
    fromNode: q,
    to: $,
    toHandle: null,
    toPosition: ka[H.position],
    toNode: null,
    pointer: $
  };
  function R() {
    E = !0, g(U), y?.(e, { nodeId: r, handleId: i, handleType: C });
  }
  S === 0 && R();
  function X(oe) {
    if (!E) {
      const { x: he, y: ge } = Ye(oe), Ee = he - T, ke = ge - L;
      if (!(Ee * Ee + ke * ke > S * S))
        return;
      R();
    }
    if (!j() || !H) {
      ce(oe);
      return;
    }
    const ee = v();
    $ = Ye(oe, I), _ = sw(fn($, ee, !1, [1, 1]), n, u, H), P || (G(), P = !0);
    const ie = Pu(oe, {
      handle: _,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: i,
      fromType: a ? "target" : "source",
      isValidConnection: x,
      doc: A,
      lib: l,
      flowId: f,
      nodeLookup: u
    });
    W = ie.handleDomNode, F = ie.connection, B = aw(!!_, ie.isValid);
    const de = u.get(r), V = de ? Dt(de, H, re.Left, !0) : U.from, ne = {
      ...U,
      from: V,
      isValid: B,
      to: ie.toHandle && B ? sn({ x: ie.toHandle.x, y: ie.toHandle.y }, ee) : $,
      toHandle: ie.toHandle,
      toPosition: B && ie.toHandle ? ie.toHandle.position : ka[H.position],
      toNode: ie.toHandle ? u.get(ie.toHandle.nodeId) : null,
      pointer: $
    };
    g(ne), U = ne;
  }
  function ce(oe) {
    if (!("touches" in oe && oe.touches.length > 0)) {
      if (E) {
        (_ || W) && F && B && w?.(F);
        const { inProgress: ee, ...ie } = U, de = {
          ...ie,
          toPosition: U.toHandle ? U.toPosition : null
        };
        m?.(oe, de), s && b?.(oe, de);
      }
      h(), cancelAnimationFrame(M), P = !1, B = !1, F = null, W = null, A.removeEventListener("mousemove", X), A.removeEventListener("mouseup", ce), A.removeEventListener("touchmove", X), A.removeEventListener("touchend", ce);
    }
  }
  A.addEventListener("mousemove", X), A.addEventListener("mouseup", ce), A.addEventListener("touchmove", X), A.addEventListener("touchend", ce);
}
function Pu(e, { handle: t, connectionMode: n, fromNodeId: i, fromHandleId: r, fromType: s, doc: a, lib: c, flowId: u, isValidConnection: l = $u, nodeLookup: d }) {
  const f = s === "target", p = t ? a.querySelector(`.${c}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: h, y } = Ye(e), w = a.elementFromPoint(h, y), m = w?.classList.contains(`${c}-flow__handle`) ? w : p, x = {
    handleDomNode: m,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (m) {
    const b = Tu(void 0, m), g = m.getAttribute("data-nodeid"), v = m.getAttribute("data-handleid"), j = m.classList.contains("connectable"), N = m.classList.contains("connectableend");
    if (!g || !b)
      return x;
    const S = {
      source: f ? g : i,
      sourceHandle: f ? v : r,
      target: f ? i : g,
      targetHandle: f ? r : v
    };
    x.connection = S;
    const A = j && N && (n === nn.Strict ? f && b === "source" || !f && b === "target" : g !== i || v !== r);
    x.isValid = A && l(S), x.toHandle = Du(g, b, v, d, n, !0);
  }
  return x;
}
const Lo = {
  onPointerDown: cw,
  isValid: Pu
};
function lw({ domNode: e, panZoom: t, getTransform: n, getViewScale: i }) {
  const r = ze(e);
  function s({ translateExtent: c, width: u, height: l, zoomStep: d = 1, pannable: f = !0, zoomable: p = !0, inversePan: h = !1 }) {
    const y = (g) => {
      if (g.sourceEvent.type !== "wheel" || !t)
        return;
      const v = n(), j = g.sourceEvent.ctrlKey && Bn() ? 10 : 1, N = -g.sourceEvent.deltaY * (g.sourceEvent.deltaMode === 1 ? 0.05 : g.sourceEvent.deltaMode ? 1 : 2e-3) * d, S = v[2] * Math.pow(2, N * j);
      t.scaleTo(S);
    };
    let w = [0, 0];
    const m = (g) => {
      (g.sourceEvent.type === "mousedown" || g.sourceEvent.type === "touchstart") && (w = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ]);
    }, x = (g) => {
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
      }, A = [
        [0, 0],
        [u, l]
      ];
      t.setViewportConstrained({
        x: k.x,
        y: k.y,
        zoom: v[2]
      }, A, c);
    }, b = au().on("start", m).on("zoom", f ? x : null).on("zoom.wheel", p ? y : null);
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
const dr = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), uo = ({ x: e, y: t, zoom: n }) => cr.translate(e, t).scale(n), Bt = (e, t) => e.target.closest(`.${t}`), Mu = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), uw = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, fo = (e, t = 0, n = uw, i = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || i(), r ? e.transition().duration(t).ease(n).on("end", i) : e;
}, Ru = (e) => {
  const t = e.ctrlKey && Bn() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function dw({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: i, panOnScrollMode: r, panOnScrollSpeed: s, zoomOnPinch: a, onPanZoomStart: c, onPanZoom: u, onPanZoomEnd: l }) {
  return (d) => {
    if (Bt(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && a) {
      const m = Ke(d), x = Ru(d), b = f * Math.pow(2, x);
      i.scaleTo(n, b, m, d);
      return;
    }
    const p = d.deltaMode === 1 ? 20 : 1;
    let h = r === St.Vertical ? 0 : d.deltaX * p, y = r === St.Horizontal ? 0 : d.deltaY * p;
    !Bn() && d.shiftKey && r !== St.Vertical && (h = d.deltaY * p, y = 0), i.translateBy(
      n,
      -(h / f) * s,
      -(y / f) * s,
      // @ts-ignore
      { internal: !0 }
    );
    const w = dr(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(d, w), e.panScrollTimeout = setTimeout(() => {
      l?.(d, w), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(d, w));
  };
}
function fw({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(i, r) {
    const s = i.type === "wheel", a = !t && s && !i.ctrlKey, c = Bt(i, e);
    if (i.ctrlKey && s && c && i.preventDefault(), a || c)
      return null;
    i.preventDefault(), n.call(this, i, r);
  };
}
function pw({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (i) => {
    if (i.sourceEvent?.internal)
      return;
    const r = dr(i.transform);
    e.mouseButton = i.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, i.sourceEvent?.type === "mousedown" && t(!0), n && n?.(i.sourceEvent, r);
  };
}
function hw({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: i, onPanZoom: r }) {
  return (s) => {
    e.usedRightMouseButton = !!(n && Mu(t, e.mouseButton ?? 0)), s.sourceEvent?.sync || i([s.transform.x, s.transform.y, s.transform.k]), r && !s.sourceEvent?.internal && r?.(s.sourceEvent, dr(s.transform));
  };
}
function gw({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: i, onPanZoomEnd: r, onPaneContextMenu: s }) {
  return (a) => {
    if (!a.sourceEvent?.internal && (e.isZoomingOrPanning = !1, s && Mu(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && a.sourceEvent && s(a.sourceEvent), e.usedRightMouseButton = !1, i(!1), r)) {
      const c = dr(a.transform);
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
function yw({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: i, panOnScroll: r, zoomOnDoubleClick: s, userSelectionActive: a, noWheelClassName: c, noPanClassName: u, lib: l, connectionInProgress: d }) {
  return (f) => {
    const p = e || t, h = n && f.ctrlKey, y = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (Bt(f, `${l}-flow__node`) || Bt(f, `${l}-flow__edge`)))
      return !0;
    if (!i && !p && !r && !s && !n || a || d && !y || Bt(f, c) && y || Bt(f, u) && (!y || r && y && !e) || !n && f.ctrlKey && y)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!p && !r && !h && y || !i && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(i) && !i.includes(f.button) && f.type === "mousedown")
      return !1;
    const w = Array.isArray(i) && i.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || y) && w;
  };
}
function mw({ domNode: e, minZoom: t, maxZoom: n, translateExtent: i, viewport: r, onPanZoom: s, onPanZoomStart: a, onPanZoomEnd: c, onDraggingChange: u }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = au().scaleExtent([t, n]).translateExtent(i), p = ze(e).call(f);
  b({
    x: r.x,
    y: r.y,
    zoom: rn(r.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], i);
  const h = p.on("wheel.zoom"), y = p.on("dblclick.zoom");
  f.wheelDelta(Ru);
  async function w(_, T) {
    return p ? new Promise((L) => {
      f?.interpolate(T?.interpolate === "linear" ? In : _i).transform(fo(p, T?.duration, T?.ease, () => L(!0)), _);
    }) : !1;
  }
  function m({ noWheelClassName: _, noPanClassName: T, onPaneContextMenu: L, userSelectionActive: C, panOnScroll: I, panOnDrag: E, panOnScrollMode: D, panOnScrollSpeed: $, preventScrolling: P, zoomOnPinch: F, zoomOnScroll: B, zoomOnDoubleClick: W, zoomActivationKeyPressed: G, lib: H, onTransformChange: q, connectionInProgress: Q, paneClickDistance: U, selectionOnDrag: R }) {
    C && !l.isZoomingOrPanning && x();
    const X = I && !G && !C;
    f.clickDistance(R ? 1 / 0 : !qe(U) || U < 0 ? 0 : U);
    const ce = X ? dw({
      zoomPanValues: l,
      noWheelClassName: _,
      d3Selection: p,
      d3Zoom: f,
      panOnScrollMode: D,
      panOnScrollSpeed: $,
      zoomOnPinch: F,
      onPanZoomStart: a,
      onPanZoom: s,
      onPanZoomEnd: c
    }) : fw({
      noWheelClassName: _,
      preventScrolling: P,
      d3ZoomHandler: h
    });
    p.on("wheel.zoom", ce, { passive: !1 });
    const oe = pw({
      zoomPanValues: l,
      onDraggingChange: u,
      onPanZoomStart: a
    });
    f.on("start", oe);
    const ee = hw({
      zoomPanValues: l,
      panOnDrag: E,
      onPaneContextMenu: !!L,
      onPanZoom: s,
      onTransformChange: q
    });
    f.on("zoom", ee);
    const ie = gw({
      zoomPanValues: l,
      panOnDrag: E,
      panOnScroll: I,
      onPaneContextMenu: L,
      onPanZoomEnd: c,
      onDraggingChange: u
    });
    f.on("end", ie);
    const de = yw({
      zoomActivationKeyPressed: G,
      panOnDrag: E,
      zoomOnScroll: B,
      panOnScroll: I,
      zoomOnDoubleClick: W,
      zoomOnPinch: F,
      userSelectionActive: C,
      noPanClassName: T,
      noWheelClassName: _,
      lib: H,
      connectionInProgress: Q
    });
    f.filter(de), W ? p.on("dblclick.zoom", y) : p.on("dblclick.zoom", null);
  }
  function x() {
    f.on("zoom", null);
  }
  async function b(_, T, L) {
    const C = uo(_), I = f?.constrain()(C, T, L);
    return I && await w(I), I;
  }
  async function g(_, T) {
    const L = uo(_);
    return await w(L, T), L;
  }
  function v(_) {
    if (p) {
      const T = uo(_), L = p.property("__zoom");
      (L.k !== _.zoom || L.x !== _.x || L.y !== _.y) && f?.transform(p, T, null, { sync: !0 });
    }
  }
  function j() {
    const _ = p ? su(p.node()) : { x: 0, y: 0, k: 1 };
    return { x: _.x, y: _.y, zoom: _.k };
  }
  async function N(_, T) {
    return p ? new Promise((L) => {
      f?.interpolate(T?.interpolate === "linear" ? In : _i).scaleTo(fo(p, T?.duration, T?.ease, () => L(!0)), _);
    }) : !1;
  }
  async function S(_, T) {
    return p ? new Promise((L) => {
      f?.interpolate(T?.interpolate === "linear" ? In : _i).scaleBy(fo(p, T?.duration, T?.ease, () => L(!0)), _);
    }) : !1;
  }
  function k(_) {
    f?.scaleExtent(_);
  }
  function A(_) {
    f?.translateExtent(_);
  }
  function M(_) {
    const T = !qe(_) || _ < 0 ? 0 : _;
    f?.clickDistance(T);
  }
  return {
    update: m,
    destroy: x,
    setViewport: g,
    setViewportConstrained: b,
    getViewport: j,
    scaleTo: N,
    scaleBy: S,
    setScaleExtent: k,
    setTranslateExtent: A,
    syncViewport: v,
    setClickDistance: M
  };
}
var an;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(an || (an = {}));
function xw({ width: e, prevWidth: t, height: n, prevHeight: i, affectsX: r, affectsY: s }) {
  const a = e - t, c = n - i, u = [a > 0 ? 1 : a < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return a && r && (u[0] = u[0] * -1), c && s && (u[1] = u[1] * -1), u;
}
function Oa(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), i = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: i,
    affectsY: r
  };
}
function ft(e, t) {
  return Math.max(0, t - e);
}
function pt(e, t) {
  return Math.max(0, e - t);
}
function bi(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function Ha(e, t) {
  return e ? !t : t;
}
function ww(e, t, n, i, r, s, a, c) {
  let { affectsX: u, affectsY: l } = t;
  const { isHorizontal: d, isVertical: f } = t, p = d && f, { xSnapped: h, ySnapped: y } = n, { minWidth: w, maxWidth: m, minHeight: x, maxHeight: b } = i, { x: g, y: v, width: j, height: N, aspectRatio: S } = e;
  let k = Math.floor(d ? h - e.pointerX : 0), A = Math.floor(f ? y - e.pointerY : 0);
  const M = j + (u ? -k : k), _ = N + (l ? -A : A), T = -s[0] * j, L = -s[1] * N;
  let C = bi(M, w, m), I = bi(_, x, b);
  if (a) {
    let $ = 0, P = 0;
    u && k < 0 ? $ = ft(g + k + T, a[0][0]) : !u && k > 0 && ($ = pt(g + M + T, a[1][0])), l && A < 0 ? P = ft(v + A + L, a[0][1]) : !l && A > 0 && (P = pt(v + _ + L, a[1][1])), C = Math.max(C, $), I = Math.max(I, P);
  }
  if (c) {
    let $ = 0, P = 0;
    u && k > 0 ? $ = pt(g + k, c[0][0]) : !u && k < 0 && ($ = ft(g + M, c[1][0])), l && A > 0 ? P = pt(v + A, c[0][1]) : !l && A < 0 && (P = ft(v + _, c[1][1])), C = Math.max(C, $), I = Math.max(I, P);
  }
  if (r) {
    if (d) {
      const $ = bi(M / S, x, b) * S;
      if (C = Math.max(C, $), a) {
        let P = 0;
        !u && !l || u && !l && p ? P = pt(v + L + M / S, a[1][1]) * S : P = ft(v + L + (u ? k : -k) / S, a[0][1]) * S, C = Math.max(C, P);
      }
      if (c) {
        let P = 0;
        !u && !l || u && !l && p ? P = ft(v + M / S, c[1][1]) * S : P = pt(v + (u ? k : -k) / S, c[0][1]) * S, C = Math.max(C, P);
      }
    }
    if (f) {
      const $ = bi(_ * S, w, m) / S;
      if (I = Math.max(I, $), a) {
        let P = 0;
        !u && !l || l && !u && p ? P = pt(g + _ * S + T, a[1][0]) / S : P = ft(g + (l ? A : -A) * S + T, a[0][0]) / S, I = Math.max(I, P);
      }
      if (c) {
        let P = 0;
        !u && !l || l && !u && p ? P = ft(g + _ * S, c[1][0]) / S : P = pt(g + (l ? A : -A) * S, c[0][0]) / S, I = Math.max(I, P);
      }
    }
  }
  A = A + (A < 0 ? I : -I), k = k + (k < 0 ? C : -C), r && (p ? M > _ * S ? A = (Ha(u, l) ? -k : k) / S : k = (Ha(u, l) ? -A : A) * S : d ? (A = k / S, l = u) : (k = A * S, u = l));
  const E = u ? g + k : g, D = l ? v + A : v;
  return {
    width: j + (u ? -k : k),
    height: N + (l ? -A : A),
    x: s[0] * k * (u ? -1 : 1) + E,
    y: s[1] * A * (l ? -1 : 1) + D
  };
}
const zu = { width: 0, height: 0, x: 0, y: 0 }, vw = {
  ...zu,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function bw(e, t, n) {
  const i = t.position.x + e.position.x, r = t.position.y + e.position.y, s = e.measured.width ?? 0, a = e.measured.height ?? 0, c = n[0] * s, u = n[1] * a;
  return [
    [i - c, r - u],
    [i + s - c, r + a - u]
  ];
}
function Nw({ domNode: e, nodeId: t, getStoreItems: n, onChange: i, onEnd: r }) {
  const s = ze(e);
  let a = {
    controlDirection: Oa("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: l, boundaries: d, keepAspectRatio: f, resizeDirection: p, onResizeStart: h, onResize: y, onResizeEnd: w, shouldResize: m }) {
    let x = { ...zu }, b = { ...vw };
    a = {
      boundaries: d,
      resizeDirection: p,
      keepAspectRatio: f,
      controlDirection: Oa(l)
    };
    let g, v = null, j = [], N, S, k, A = !1;
    const M = Xl().on("start", (_) => {
      const { nodeLookup: T, transform: L, snapGrid: C, snapToGrid: I, nodeOrigin: E, paneDomNode: D } = n();
      if (g = T.get(t), !g)
        return;
      v = D?.getBoundingClientRect() ?? null;
      const { xSnapped: $, ySnapped: P } = An(_.sourceEvent, {
        transform: L,
        snapGrid: C,
        snapToGrid: I,
        containerBounds: v
      });
      x = {
        width: g.measured.width ?? 0,
        height: g.measured.height ?? 0,
        x: g.position.x ?? 0,
        y: g.position.y ?? 0
      }, b = {
        ...x,
        pointerX: $,
        pointerY: P,
        aspectRatio: x.width / x.height
      }, N = void 0, S = _t(g.extent) ? g.extent : void 0, g.parentId && (g.extent === "parent" || g.expandParent) && (N = T.get(g.parentId)), N && g.extent === "parent" && (S = [
        [0, 0],
        [N.measured.width, N.measured.height]
      ]), j = [], k = void 0;
      for (const [F, B] of T)
        if (B.parentId === t && (j.push({
          id: F,
          position: { ...B.position },
          extent: B.extent
        }), B.extent === "parent" || B.expandParent)) {
          const W = bw(B, g, B.origin ?? E);
          k ? k = [
            [Math.min(W[0][0], k[0][0]), Math.min(W[0][1], k[0][1])],
            [Math.max(W[1][0], k[1][0]), Math.max(W[1][1], k[1][1])]
          ] : k = W;
        }
      h?.(_, { ...x });
    }).on("drag", (_) => {
      const { transform: T, snapGrid: L, snapToGrid: C, nodeOrigin: I } = n(), E = An(_.sourceEvent, {
        transform: T,
        snapGrid: L,
        snapToGrid: C,
        containerBounds: v
      }), D = [];
      if (!g)
        return;
      const { x: $, y: P, width: F, height: B } = x, W = {}, G = g.origin ?? I, { width: H, height: q, x: Q, y: U } = ww(b, a.controlDirection, E, a.boundaries, a.keepAspectRatio, G, S, k), R = H !== F, X = q !== B, ce = Q !== $ && R, oe = U !== P && X;
      if (!ce && !oe && !R && !X)
        return;
      if ((ce || oe || G[0] === 1 || G[1] === 1) && (W.x = ce ? Q : x.x, W.y = oe ? U : x.y, x.x = W.x, x.y = W.y, j.length > 0)) {
        const V = Q - $, ne = U - P;
        for (const he of j)
          he.position = {
            x: he.position.x - V + G[0] * (H - F),
            y: he.position.y - ne + G[1] * (q - B)
          }, D.push(he);
      }
      if ((R || X) && (W.width = R && (!a.resizeDirection || a.resizeDirection === "horizontal") ? H : x.width, W.height = X && (!a.resizeDirection || a.resizeDirection === "vertical") ? q : x.height, x.width = W.width, x.height = W.height), N && g.expandParent) {
        const V = G[0] * (W.width ?? 0);
        W.x && W.x < V && (x.x = V, b.x = b.x - (W.x - V));
        const ne = G[1] * (W.height ?? 0);
        W.y && W.y < ne && (x.y = ne, b.y = b.y - (W.y - ne));
      }
      const ee = xw({
        width: x.width,
        prevWidth: F,
        height: x.height,
        prevHeight: B,
        affectsX: a.controlDirection.affectsX,
        affectsY: a.controlDirection.affectsY
      }), ie = { ...x, direction: ee };
      m?.(_, ie) !== !1 && (A = !0, y?.(_, ie), i(W, D));
    }).on("end", (_) => {
      A && (w?.(_, { ...x }), r?.({ ...x }), A = !1);
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
var Wa;
function jw() {
  if (Wa) return yo;
  Wa = 1;
  var e = Ue;
  function t(f, p) {
    return f === p && (f !== 0 || 1 / f === 1 / p) || f !== f && p !== p;
  }
  var n = typeof Object.is == "function" ? Object.is : t, i = e.useState, r = e.useEffect, s = e.useLayoutEffect, a = e.useDebugValue;
  function c(f, p) {
    var h = p(), y = i({ inst: { value: h, getSnapshot: p } }), w = y[0].inst, m = y[1];
    return s(
      function() {
        w.value = h, w.getSnapshot = p, u(w) && m({ inst: w });
      },
      [f, h, p]
    ), r(
      function() {
        return u(w) && m({ inst: w }), f(function() {
          u(w) && m({ inst: w });
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
var Ba;
function Sw() {
  return Ba || (Ba = 1, go.exports = jw()), go.exports;
}
var Fa;
function Cw() {
  if (Fa) return ho;
  Fa = 1;
  var e = Ue, t = Sw();
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
        function x(N) {
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
            return x(d());
          },
          j === null ? void 0 : function() {
            return x(j());
          }
        ];
      },
      [d, f, p, h]
    );
    var m = r(l, y[0], y[1]);
    return a(
      function() {
        w.hasValue = !0, w.value = m;
      },
      [m]
    ), u(m), m;
  }, ho;
}
var Ka;
function Ew() {
  return Ka || (Ka = 1, po.exports = Cw()), po.exports;
}
var kw = Ew();
const Iw = /* @__PURE__ */ bf(kw), Aw = {}, Xa = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), i = (d, f) => {
    const p = typeof d == "function" ? d(t) : d;
    if (!Object.is(p, t)) {
      const h = t;
      t = f ?? (typeof p != "object" || p === null) ? p : Object.assign({}, t, p), n.forEach((y) => y(t, h));
    }
  }, r = () => t, u = { setState: i, getState: r, getInitialState: () => l, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (Aw ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(i, r, u);
  return u;
}, _w = (e) => e ? Xa(e) : Xa, { useDebugValue: Dw } = Ue, { useSyncExternalStoreWithSelector: Tw } = Iw, $w = (e) => e;
function Lu(e, t = $w, n) {
  const i = Tw(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return Dw(i), i;
}
const qa = (e, t) => {
  const n = _w(e), i = (r, s = t) => Lu(n, r, s);
  return Object.assign(i, n), i;
}, Pw = (e, t) => e ? qa(e, t) : qa;
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
var mo = { exports: {} }, _e = {};
var Ya;
function Mw() {
  if (Ya) return _e;
  Ya = 1;
  var e = Ue;
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
var Ua;
function Rw() {
  if (Ua) return mo.exports;
  Ua = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), mo.exports = Mw(), mo.exports;
}
var zw = Rw();
const fr = qo(null), Lw = fr.Provider, Vu = He.error001("react");
function pe(e, t) {
  const n = Kn(fr);
  if (n === null)
    throw new Error(Vu);
  return Lu(n, e, t);
}
function ve() {
  const e = Kn(fr);
  if (e === null)
    throw new Error(Vu);
  return ue(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const Za = { display: "none" }, Vw = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, Ou = "react-flow__node-desc", Hu = "react-flow__edge-desc", Ow = "react-flow__aria-live", Hw = (e) => e.ariaLiveMessage, Ww = (e) => e.ariaLabelConfig;
function Bw({ rfId: e }) {
  const t = pe(Hw);
  return o.jsx("div", { id: `${Ow}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: Vw, children: t });
}
function Fw({ rfId: e, disableKeyboardA11y: t }) {
  const n = pe(Ww);
  return o.jsxs(o.Fragment, { children: [o.jsx("div", { id: `${Ou}-${e}`, style: Za, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), o.jsx("div", { id: `${Hu}-${e}`, style: Za, children: n["edge.a11yDescription.default"] }), !t && o.jsx(Bw, { rfId: e })] });
}
const pr = Kc(({ position: e = "top-left", children: t, className: n, style: i, ...r }, s) => {
  const a = `${e}`.split("-");
  return o.jsx("div", { className: Ce(["react-flow__panel", n, ...a]), style: i, ref: s, ...r, children: t });
});
pr.displayName = "Panel";
function Kw({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : o.jsx(pr, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: o.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const Xw = (e) => {
  const t = [], n = [];
  for (const [, i] of e.nodeLookup)
    i.selected && t.push(i.internals.userNode);
  for (const [, i] of e.edgeLookup)
    i.selected && n.push(i);
  return { selectedNodes: t, selectedEdges: n };
}, Ni = (e) => e.id;
function qw(e, t) {
  return we(e.selectedNodes.map(Ni), t.selectedNodes.map(Ni)) && we(e.selectedEdges.map(Ni), t.selectedEdges.map(Ni));
}
function Yw({ onSelectionChange: e }) {
  const t = ve(), { selectedNodes: n, selectedEdges: i } = pe(Xw, qw);
  return te(() => {
    const r = { nodes: n, edges: i };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((s) => s(r));
  }, [n, i, e]), null;
}
const Uw = (e) => !!e.onSelectionChangeHandlers;
function Zw({ onSelectionChange: e }) {
  const t = pe(Uw);
  return e || t ? o.jsx(Yw, { onSelectionChange: e }) : null;
}
const Wu = [0, 0], Gw = { x: 0, y: 0, zoom: 1 }, Jw = [
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
], Ga = [...Jw, "rfId"], Qw = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), Ja = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: On,
  nodeOrigin: Wu,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function ev(e) {
  const { setNodes: t, setEdges: n, setMinZoom: i, setMaxZoom: r, setTranslateExtent: s, setNodeExtent: a, reset: c, setDefaultNodesAndEdges: u } = pe(Qw, we), l = ve();
  te(() => (u(e.defaultNodes, e.defaultEdges), () => {
    d.current = Ja, c();
  }), []);
  const d = ae(Ja);
  return te(
    () => {
      for (const f of Ga) {
        const p = e[f], h = d.current[f];
        p !== h && (typeof e[f] > "u" || (f === "nodes" ? t(p) : f === "edges" ? n(p) : f === "minZoom" ? i(p) : f === "maxZoom" ? r(p) : f === "translateExtent" ? s(p) : f === "nodeExtent" ? a(p) : f === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: Mx(p) }) : f === "fitView" ? l.setState({ fitViewQueued: p }) : f === "fitViewOptions" ? l.setState({ fitViewOptions: p }) : l.setState({ [f]: p })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    Ga.map((f) => e[f])
  ), null;
}
function Qa() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function tv(e) {
  const [t, n] = K(e === "system" ? null : e);
  return te(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const i = Qa(), r = () => n(i?.matches ? "dark" : "light");
    return r(), i?.addEventListener("change", r), () => {
      i?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : Qa()?.matches ? "dark" : "light";
}
const ec = typeof document < "u" ? document : null;
function Fn(e = null, t = { target: ec, actInsideInputWithModifier: !0 }) {
  const [n, i] = K(!1), r = ae(!1), s = ae(/* @__PURE__ */ new Set([])), [a, c] = ue(() => {
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
  return te(() => {
    const u = t?.target ?? ec, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (h) => {
        if (r.current = h.ctrlKey || h.metaKey || h.shiftKey || h.altKey, (!r.current || r.current && !l) && vu(h))
          return !1;
        const w = nc(h.code, c);
        if (s.current.add(h[w]), tc(a, s.current, !1)) {
          const m = h.composedPath?.()?.[0] || h.target, x = m?.nodeName === "BUTTON" || m?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !x) && h.preventDefault(), i(!0);
        }
      }, f = (h) => {
        const y = nc(h.code, c);
        tc(a, s.current, !0) ? (i(!1), s.current.clear()) : s.current.delete(h[y]), h.key === "Meta" && s.current.clear(), r.current = !1;
      }, p = () => {
        s.current.clear(), i(!1);
      };
      return u?.addEventListener("keydown", d), u?.addEventListener("keyup", f), window.addEventListener("blur", p), window.addEventListener("contextmenu", p), () => {
        u?.removeEventListener("keydown", d), u?.removeEventListener("keyup", f), window.removeEventListener("blur", p), window.removeEventListener("contextmenu", p);
      };
    }
  }, [e, i]), n;
}
function tc(e, t, n) {
  return e.filter((i) => n || i.length === t.size).some((i) => i.every((r) => t.has(r)));
}
function nc(e, t) {
  return t.includes(e) ? "code" : "key";
}
const nv = () => {
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
      const { width: i, height: r, minZoom: s, maxZoom: a, panZoom: c } = e.getState(), u = ws(t, i, r, s, a, n?.padding ?? 0.1);
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
function Bu(e, t) {
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
      iv(u, c);
    n.push(c);
  }
  return r.length && r.forEach((s) => {
    s.index !== void 0 ? n.splice(s.index, 0, { ...s.item }) : n.push({ ...s.item });
  }), n;
}
function iv(e, t) {
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
function Fu(e, t) {
  return Bu(e, t);
}
function Ku(e, t) {
  return Bu(e, t);
}
function vt(e, t) {
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
    !(s.selected === void 0 && !a) && s.selected !== a && (n && (s.selected = a), i.push(vt(s.id, a)));
  }
  return i;
}
function ic({ items: e = [], lookup: t }) {
  const n = [], i = new Map(e.map((r) => [r.id, r]));
  for (const [r, s] of e.entries()) {
    const a = t.get(s.id), c = a?.internals?.userNode ?? a;
    c !== void 0 && c !== s && n.push({ id: s.id, item: s, type: "replace" }), c === void 0 && n.push({ item: s, type: "add", index: r });
  }
  for (const [r] of t)
    i.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function rc(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const Xu = yu();
function qu(e, t, n = {}) {
  return Ox(e, t, {
    ...n,
    onError: n.onError ?? Xu
  });
}
function rv(e, t, n, i = { shouldReplaceId: !0 }) {
  return Hx(e, t, n, {
    ...i,
    onError: i.onError ?? Xu
  });
}
const oc = (e) => Ex(e), ov = (e) => fu(e);
function Yu(e) {
  return Kc(e);
}
const sv = typeof window < "u" ? nf : te;
function sc(e) {
  const [t, n] = K(BigInt(0)), [i] = K(() => av(() => n((r) => r + BigInt(1))));
  return sv(() => {
    const r = i.get();
    r.length && (e(r), i.reset());
  }, [t]), i;
}
function av(e) {
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
const Uu = qo(null);
function cv({ children: e }) {
  const t = ve(), n = se((c) => {
    const { nodes: u = [], setNodes: l, hasDefaultNodes: d, onNodesChange: f, nodeLookup: p, fitViewQueued: h, onNodesChangeMiddlewareMap: y } = t.getState();
    let w = u;
    for (const x of c)
      w = typeof x == "function" ? x(w) : x;
    let m = ic({
      items: w,
      lookup: p
    });
    for (const x of y.values())
      m = x(m);
    d && l(w), m.length > 0 ? f?.(m) : h && window.requestAnimationFrame(() => {
      const { fitViewQueued: x, nodes: b, setNodes: g } = t.getState();
      x && g(b);
    });
  }, []), i = sc(n), r = se((c) => {
    const { edges: u = [], setEdges: l, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: p } = t.getState();
    let h = u;
    for (const y of c)
      h = typeof y == "function" ? y(h) : y;
    d ? l(h) : f && f(ic({
      items: h,
      lookup: p
    }));
  }, []), s = sc(r), a = ue(() => ({ nodeQueue: i, edgeQueue: s }), []);
  return o.jsx(Uu.Provider, { value: a, children: e });
}
function lv() {
  const e = Kn(Uu);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const uv = (e) => !!e.panZoom;
function Es() {
  const e = nv(), t = ve(), n = lv(), i = pe(uv), r = ue(() => {
    const s = (f) => t.getState().nodeLookup.get(f), a = (f) => {
      n.nodeQueue.push(f);
    }, c = (f) => {
      n.edgeQueue.push(f);
    }, u = (f) => {
      const { nodeLookup: p, nodeOrigin: h } = t.getState(), y = oc(f) ? f : p.get(f.id), w = y.parentId ? xu(y.position, y.measured, y.parentId, p, h) : y.position, m = {
        ...y,
        position: w,
        width: y.measured?.width ?? y.width,
        height: y.measured?.height ?? y.height
      };
      return on(m);
    }, l = (f, p, h = { replace: !1 }) => {
      a((y) => y.map((w) => {
        if (w.id === f) {
          const m = typeof p == "function" ? p(w) : p;
          return h.replace && oc(m) ? m : { ...w, ...m };
        }
        return w;
      }));
    }, d = (f, p, h = { replace: !1 }) => {
      c((y) => y.map((w) => {
        if (w.id === f) {
          const m = typeof p == "function" ? p(w) : p;
          return h.replace && ov(m) ? m : { ...w, ...m };
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
        const { nodes: f = [], edges: p = [], transform: h } = t.getState(), [y, w, m] = h;
        return {
          nodes: f.map((x) => ({ ...x })),
          edges: p.map((x) => ({ ...x })),
          viewport: {
            x: y,
            y: w,
            zoom: m
          }
        };
      },
      deleteElements: async ({ nodes: f = [], edges: p = [] }) => {
        const { nodes: h, edges: y, onNodesDelete: w, onEdgesDelete: m, triggerNodeChanges: x, triggerEdgeChanges: b, onDelete: g, onBeforeDelete: v } = t.getState(), { nodes: j, edges: N } = await Dx({
          nodesToRemove: f,
          edgesToRemove: p,
          nodes: h,
          edges: y,
          onBeforeDelete: v
        }), S = N.length > 0, k = j.length > 0;
        if (S) {
          const A = N.map(rc);
          m?.(N), b(A);
        }
        if (k) {
          const A = j.map(rc);
          w?.(j), x(A);
        }
        return (k || S) && g?.({ nodes: j, edges: N }), { deletedNodes: j, deletedEdges: N };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, p = !0, h) => {
        const y = Aa(f), w = y ? f : u(f), m = h !== void 0;
        return w ? (h || t.getState().nodes).filter((x) => {
          const b = t.getState().nodeLookup.get(x.id);
          if (b && !y && (x.id === f.id || !b.internals.positionAbsolute))
            return !1;
          const g = on(m ? x : b), v = Wn(g, w);
          return p && v > 0 || v >= g.width * g.height || v >= w.width * w.height;
        }) : [];
      },
      isNodeIntersecting: (f, p, h = !0) => {
        const w = Aa(f) ? f : u(f);
        if (!w)
          return !1;
        const m = Wn(w, p);
        return h && m > 0 || m >= p.width * p.height || m >= w.width * w.height;
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
        return kx(f, { nodeLookup: p, nodeOrigin: h });
      },
      getHandleConnections: ({ type: f, id: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}-${f}${p ? `-${p}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}${f ? p ? `-${f}-${p}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const p = t.getState().fitViewResolver ?? Px();
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
const ac = (e) => e.selected, dv = typeof window < "u" ? window : void 0;
function fv({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = ve(), { deleteElements: i } = Es(), r = Fn(e, { actInsideInputWithModifier: !1 }), s = Fn(t, { target: dv });
  te(() => {
    if (r) {
      const { edges: a, nodes: c } = n.getState();
      i({ nodes: c.filter(ac), edges: a.filter(ac) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), te(() => {
    n.setState({ multiSelectionActive: s });
  }, [s]);
}
function pv(e) {
  const t = ve();
  te(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const i = vs(e.current);
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
const hr = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, hv = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function gv({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: i = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: s = St.Free, zoomOnDoubleClick: a = !0, panOnDrag: c = !0, defaultViewport: u, translateExtent: l, minZoom: d, maxZoom: f, zoomActivationKeyCode: p, preventScrolling: h = !0, children: y, noWheelClassName: w, noPanClassName: m, onViewportChange: x, isControlledViewport: b, paneClickDistance: g, selectionOnDrag: v }) {
  const j = ve(), N = ae(null), { userSelectionActive: S, lib: k, connectionInProgress: A } = pe(hv, we), M = Fn(p), _ = ae();
  pv(N);
  const T = se((L) => {
    x?.({ x: L[0], y: L[1], zoom: L[2] }), b || j.setState({ transform: L });
  }, [x, b]);
  return te(() => {
    if (N.current) {
      _.current = mw({
        domNode: N.current,
        minZoom: d,
        maxZoom: f,
        translateExtent: l,
        viewport: u,
        onDraggingChange: (E) => j.setState((D) => D.paneDragging === E ? D : { paneDragging: E }),
        onPanZoomStart: (E, D) => {
          const { onViewportChangeStart: $, onMoveStart: P } = j.getState();
          P?.(E, D), $?.(D);
        },
        onPanZoom: (E, D) => {
          const { onViewportChange: $, onMove: P } = j.getState();
          P?.(E, D), $?.(D);
        },
        onPanZoomEnd: (E, D) => {
          const { onViewportChangeEnd: $, onMoveEnd: P } = j.getState();
          P?.(E, D), $?.(D);
        }
      });
      const { x: L, y: C, zoom: I } = _.current.getViewport();
      return j.setState({
        panZoom: _.current,
        transform: [L, C, I],
        domNode: N.current.closest(".react-flow")
      }), () => {
        _.current?.destroy();
      };
    }
  }, []), te(() => {
    _.current?.update({
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
      noWheelClassName: w,
      lib: k,
      onTransformChange: T,
      connectionInProgress: A,
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
    w,
    k,
    T,
    A,
    v,
    g
  ]), o.jsx("div", { className: "react-flow__renderer", ref: N, style: hr, children: y });
}
const yv = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function mv() {
  const { userSelectionActive: e, userSelectionRect: t } = pe(yv, we);
  return e && t ? o.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const xo = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, xv = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function wv({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Hn.Full, panOnDrag: i, autoPanOnSelection: r, paneClickDistance: s, selectionOnDrag: a, onSelectionStart: c, onSelectionEnd: u, onPaneClick: l, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: p, onPaneMouseMove: h, onPaneMouseLeave: y, children: w }) {
  const m = ae(0), x = ve(), { userSelectionActive: b, elementsSelectable: g, dragging: v, connectionInProgress: j, panBy: N, autoPanSpeed: S } = pe(xv, we), k = g && (e || b), A = ae(null), M = ae(), _ = ae(/* @__PURE__ */ new Set()), T = ae(/* @__PURE__ */ new Set()), L = ae(!1), C = ae({ x: 0, y: 0 }), I = ae(!1), E = (R) => {
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
  }, $ = f ? (R) => f(R) : void 0, P = (R) => {
    L.current && (R.stopPropagation(), L.current = !1);
  }, F = (R) => {
    const { domNode: X, transform: ce } = x.getState();
    if (M.current = X?.getBoundingClientRect(), !M.current)
      return;
    const oe = R.target === A.current;
    if (!oe && !!R.target.closest(".nokey") || !e || !(a && oe || t) || R.button !== 0 || !R.isPrimary)
      return;
    R.target?.setPointerCapture?.(R.pointerId), L.current = !1;
    const { x: de, y: V } = Ye(R.nativeEvent, M.current), ne = fn({ x: de, y: V }, ce);
    x.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: ne.x,
        startY: ne.y,
        x: de,
        y: V
      }
    }), oe || (R.stopPropagation(), R.preventDefault());
  };
  function B(R, X) {
    const { userSelectionRect: ce } = x.getState();
    if (!ce)
      return;
    const { transform: oe, nodeLookup: ee, edgeLookup: ie, connectionLookup: de, triggerNodeChanges: V, triggerEdgeChanges: ne, defaultEdgeOptions: he } = x.getState(), ge = { x: ce.startX, y: ce.startY }, { x: Ee, y: ke } = sn(ge, oe), De = {
      startX: ge.x,
      startY: ge.y,
      x: R < Ee ? R : Ee,
      y: X < ke ? X : ke,
      width: Math.abs(R - Ee),
      height: Math.abs(X - ke)
    }, it = _.current, We = T.current;
    _.current = new Set(ms(ee, De, oe, n === Hn.Partial, !0).map((Te) => Te.id)), T.current = /* @__PURE__ */ new Set();
    const Be = he?.selectable ?? !0;
    for (const Te of _.current) {
      const Re = de.get(Te);
      if (Re)
        for (const { edgeId: z } of Re.values()) {
          const O = ie.get(z);
          O && (O.selectable ?? Be) && T.current.add(z);
        }
    }
    if (!_a(it, _.current)) {
      const Te = Ft(ee, _.current, !0);
      V(Te);
    }
    if (!_a(We, T.current)) {
      const Te = Ft(ie, T.current);
      ne(Te);
    }
    x.setState({
      userSelectionRect: De,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function W() {
    if (!r || !M.current)
      return;
    const [R, X] = xs(C.current, M.current, S);
    N({ x: R, y: X }).then((ce) => {
      if (!L.current || !ce) {
        m.current = requestAnimationFrame(W);
        return;
      }
      const { x: oe, y: ee } = C.current;
      B(oe, ee), m.current = requestAnimationFrame(W);
    });
  }
  const G = () => {
    cancelAnimationFrame(m.current), m.current = 0, I.current = !1;
  };
  te(() => () => G(), []);
  const H = (R) => {
    const { userSelectionRect: X, transform: ce, resetSelectedElements: oe } = x.getState();
    if (!M.current || !X)
      return;
    const { x: ee, y: ie } = Ye(R.nativeEvent, M.current);
    C.current = { x: ee, y: ie };
    const de = sn({ x: X.startX, y: X.startY }, ce);
    if (!L.current) {
      const V = t ? 0 : s;
      if (Math.hypot(ee - de.x, ie - de.y) <= V)
        return;
      oe(), c?.(R);
    }
    L.current = !0, I.current || (W(), I.current = !0), B(ee, ie);
  }, q = (R) => {
    R.button === 0 && (R.target?.releasePointerCapture?.(R.pointerId), !b && R.target === A.current && x.getState().userSelectionRect && E?.(R), x.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), L.current && (u?.(R), x.setState({
      nodesSelectionActive: _.current.size > 0
    })), G());
  }, Q = (R) => {
    R.target?.releasePointerCapture?.(R.pointerId), G();
  }, U = i === !0 || Array.isArray(i) && i.includes(0);
  return o.jsxs("div", { className: Ce(["react-flow__pane", { draggable: U, dragging: v, selection: e }]), onClick: k ? void 0 : xo(E, A), onContextMenu: xo(D, A), onWheel: xo($, A), onPointerEnter: k ? void 0 : p, onPointerMove: k ? H : h, onPointerUp: k ? q : void 0, onPointerCancel: k ? Q : void 0, onPointerDownCapture: k ? F : void 0, onClickCapture: k ? P : void 0, onPointerLeave: y, ref: A, style: hr, children: [w, o.jsx(mv, {})] });
}
function Vo({ id: e, store: t, unselect: n = !1, nodeRef: i }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: s, multiSelectionActive: a, nodeLookup: c, onError: u } = t.getState(), l = c.get(e);
  if (!l) {
    u?.("012", He.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && a) && (s({ nodes: [l], edges: [] }), requestAnimationFrame(() => i?.current?.blur())) : r([e]);
}
function Zu({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: i, nodeId: r, isSelectable: s, nodeClickDistance: a }) {
  const c = ve(), [u, l] = K(!1), d = ae();
  return te(() => {
    d.current = iw({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (f) => {
        Vo({
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
  }, []), te(() => {
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
const vv = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function Gu() {
  const e = ve();
  return se((n) => {
    const { nodeExtent: i, snapToGrid: r, snapGrid: s, nodesDraggable: a, onError: c, updateNodePositions: u, nodeLookup: l, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), p = vv(a), h = r ? s[0] : 5, y = r ? s[1] : 5, w = n.direction.x * h * n.factor, m = n.direction.y * y * n.factor;
    for (const [, x] of l) {
      if (!p(x))
        continue;
      let b = {
        x: x.internals.positionAbsolute.x + w,
        y: x.internals.positionAbsolute.y + m
      };
      r && (b = ei(b, s));
      const { position: g, positionAbsolute: v } = pu({
        nodeId: x.id,
        nextPosition: b,
        nodeLookup: l,
        nodeExtent: i,
        nodeOrigin: d,
        onError: c
      });
      x.position = g, x.internals.positionAbsolute = v, f.set(x.id, x);
    }
    u(f);
  }, []);
}
const ks = qo(null), bv = ks.Provider;
ks.Consumer;
const Ju = () => Kn(ks), Nv = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), jv = (e, t, n) => (i) => {
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
function Sv({ type: e = "source", position: t = re.Top, isValidConnection: n, isConnectable: i = !0, isConnectableStart: r = !0, isConnectableEnd: s = !0, id: a, onConnect: c, children: u, className: l, onMouseDown: d, onTouchStart: f, ...p }, h) {
  const y = a || null, w = e === "target", m = ve(), x = Ju(), { connectOnClick: b, noPanClassName: g, rfId: v } = pe(Nv, we), { connectingFrom: j, connectingTo: N, clickConnecting: S, isPossibleEndHandle: k, connectionInProcess: A, clickConnectionInProcess: M, valid: _ } = pe(jv(x, y, e), we);
  x || m.getState().onError?.("010", He.error010());
  const T = (I) => {
    const { defaultEdgeOptions: E, onConnect: D, hasDefaultEdges: $ } = m.getState(), P = {
      ...E,
      ...I
    };
    if ($) {
      const { edges: F, setEdges: B, onError: W } = m.getState();
      B(qu(P, F, { onError: W }));
    }
    D?.(P), c?.(P);
  }, L = (I) => {
    if (!x)
      return;
    const E = bu(I.nativeEvent);
    if (r && (E && I.button === 0 || !E)) {
      const D = m.getState();
      Lo.onPointerDown(I.nativeEvent, {
        handleDomNode: I.currentTarget,
        autoPanOnConnect: D.autoPanOnConnect,
        connectionMode: D.connectionMode,
        connectionRadius: D.connectionRadius,
        domNode: D.domNode,
        nodeLookup: D.nodeLookup,
        lib: D.lib,
        isTarget: w,
        handleId: y,
        nodeId: x,
        flowId: D.rfId,
        panBy: D.panBy,
        cancelConnection: D.cancelConnection,
        onConnectStart: D.onConnectStart,
        onConnectEnd: (...$) => m.getState().onConnectEnd?.(...$),
        updateConnection: D.updateConnection,
        onConnect: T,
        isValidConnection: n || ((...$) => m.getState().isValidConnection?.(...$) ?? !0),
        getTransform: () => m.getState().transform,
        getFromHandle: () => m.getState().connection.fromHandle,
        autoPanSpeed: D.autoPanSpeed,
        dragThreshold: D.connectionDragThreshold
      });
    }
    E ? d?.(I) : f?.(I);
  }, C = (I) => {
    const { onClickConnectStart: E, onClickConnectEnd: D, connectionClickStartHandle: $, connectionMode: P, isValidConnection: F, lib: B, rfId: W, nodeLookup: G, connection: H } = m.getState();
    if (!x || !$ && !r)
      return;
    if (!$) {
      E?.(I.nativeEvent, { nodeId: x, handleId: y, handleType: e }), m.setState({ connectionClickStartHandle: { nodeId: x, type: e, id: y } });
      return;
    }
    const q = wu(I.target), Q = n || F, { connection: U, isValid: R } = Lo.isValid(I.nativeEvent, {
      handle: {
        nodeId: x,
        id: y,
        type: e
      },
      connectionMode: P,
      fromNodeId: $.nodeId,
      fromHandleId: $.id || null,
      fromType: $.type,
      isValidConnection: Q,
      flowId: W,
      doc: q,
      lib: B,
      nodeLookup: G
    });
    R && U && T(U);
    const X = structuredClone(H);
    delete X.inProgress, X.toPosition = X.toHandle ? X.toHandle.position : null, D?.(I, X), m.setState({ connectionClickStartHandle: null });
  };
  return o.jsx("div", { "data-handleid": y, "data-nodeid": x, "data-handlepos": t, "data-id": `${v}-${x}-${y}-${e}`, className: Ce([
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
      valid: _,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: i && (!A || k) && (A || M ? s : r)
    }
  ]), onMouseDown: L, onTouchStart: L, onClick: b ? C : void 0, ref: h, ...p, children: u });
}
const cn = je(Yu(Sv));
function Cv({ data: e, isConnectable: t, sourcePosition: n = re.Bottom }) {
  return o.jsxs(o.Fragment, { children: [e?.label, o.jsx(cn, { type: "source", position: n, isConnectable: t })] });
}
function Ev({ data: e, isConnectable: t, targetPosition: n = re.Top, sourcePosition: i = re.Bottom }) {
  return o.jsxs(o.Fragment, { children: [o.jsx(cn, { type: "target", position: n, isConnectable: t }), e?.label, o.jsx(cn, { type: "source", position: i, isConnectable: t })] });
}
function kv() {
  return null;
}
function Iv({ data: e, isConnectable: t, targetPosition: n = re.Top }) {
  return o.jsxs(o.Fragment, { children: [o.jsx(cn, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const Gi = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, cc = {
  input: Cv,
  default: Ev,
  output: Iv,
  group: kv
};
function Av(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const _v = (e) => {
  const { width: t, height: n, x: i, y: r } = Qn(e.nodeLookup, {
    filter: (s) => !!s.selected
  });
  return {
    width: qe(t) ? t : null,
    height: qe(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${i}px,${r}px)`
  };
};
function Dv({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const i = ve(), { width: r, height: s, transformString: a, userSelectionActive: c } = pe(_v, we), u = Gu(), l = ae(null);
  te(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !c && r !== null && s !== null;
  if (Zu({
    nodeRef: l,
    disabled: !d
  }), !d)
    return null;
  const f = e ? (h) => {
    const y = i.getState().nodes.filter((w) => w.selected);
    e(h, y);
  } : void 0, p = (h) => {
    Object.prototype.hasOwnProperty.call(Gi, h.key) && (h.preventDefault(), u({
      direction: Gi[h.key],
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
const lc = typeof window < "u" ? window : void 0, Tv = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function Qu({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: i, onPaneMouseLeave: r, onPaneContextMenu: s, onPaneScroll: a, paneClickDistance: c, deleteKeyCode: u, selectionKeyCode: l, selectionOnDrag: d, selectionMode: f, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: y, panActivationKeyCode: w, zoomActivationKeyCode: m, elementsSelectable: x, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: v, panOnScrollSpeed: j, panOnScrollMode: N, zoomOnDoubleClick: S, panOnDrag: k, autoPanOnSelection: A, defaultViewport: M, translateExtent: _, minZoom: T, maxZoom: L, preventScrolling: C, onSelectionContextMenu: I, noWheelClassName: E, noPanClassName: D, disableKeyboardA11y: $, onViewportChange: P, isControlledViewport: F }) {
  const { nodesSelectionActive: B, userSelectionActive: W } = pe(Tv, we), G = Fn(l, { target: lc }), H = Fn(w, { target: lc }), q = H || k, Q = H || v, U = d && q !== !0, R = G || W || U;
  return fv({ deleteKeyCode: u, multiSelectionKeyCode: y }), o.jsx(gv, { onPaneContextMenu: s, elementsSelectable: x, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: Q, panOnScrollSpeed: j, panOnScrollMode: N, zoomOnDoubleClick: S, panOnDrag: !G && q, defaultViewport: M, translateExtent: _, minZoom: T, maxZoom: L, zoomActivationKeyCode: m, preventScrolling: C, noWheelClassName: E, noPanClassName: D, onViewportChange: P, isControlledViewport: F, paneClickDistance: c, selectionOnDrag: U, children: o.jsxs(wv, { onSelectionStart: p, onSelectionEnd: h, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: i, onPaneMouseLeave: r, onPaneContextMenu: s, onPaneScroll: a, panOnDrag: q, autoPanOnSelection: A, isSelecting: !!R, selectionMode: f, selectionKeyPressed: G, paneClickDistance: c, selectionOnDrag: U, children: [e, B && o.jsx(Dv, { onSelectionContextMenu: I, noPanClassName: D, disableKeyboardA11y: $ })] }) });
}
Qu.displayName = "FlowRenderer";
const $v = je(Qu), Pv = (e) => (t) => e ? ms(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function Mv(e) {
  return pe(se(Pv(e), [e]), we);
}
const Rv = (e) => e.updateNodeInternals;
function zv() {
  const e = pe(Rv), [t] = K(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
  return te(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function Lv({ node: e, nodeType: t, hasDimensions: n, resizeObserver: i }) {
  const r = ve(), s = ae(null), a = ae(null), c = ae(e.sourcePosition), u = ae(e.targetPosition), l = ae(t), d = n && !!e.internals.handleBounds;
  return te(() => {
    s.current && !e.hidden && (!d || a.current !== s.current) && (a.current && i?.unobserve(a.current), i?.observe(s.current), a.current = s.current);
  }, [d, e.hidden]), te(() => () => {
    a.current && (i?.unobserve(a.current), a.current = null);
  }, []), te(() => {
    if (s.current) {
      const f = l.current !== t, p = c.current !== e.sourcePosition, h = u.current !== e.targetPosition;
      (f || p || h) && (l.current = t, c.current = e.sourcePosition, u.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: s.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), s;
}
function Vv({ id: e, onClick: t, onMouseEnter: n, onMouseMove: i, onMouseLeave: r, onContextMenu: s, onDoubleClick: a, nodesDraggable: c, elementsSelectable: u, nodesConnectable: l, nodesFocusable: d, resizeObserver: f, noDragClassName: p, noPanClassName: h, disableKeyboardA11y: y, rfId: w, nodeTypes: m, nodeClickDistance: x, onError: b }) {
  const { node: g, internals: v, isParent: j } = pe((R) => {
    const X = R.nodeLookup.get(e), ce = R.parentLookup.has(e);
    return {
      node: X,
      internals: X.internals,
      isParent: ce
    };
  }, we);
  let N = g.type || "default", S = m?.[N] || cc[N];
  S === void 0 && (b?.("003", He.error003(N)), N = "default", S = m?.default || cc.default);
  const k = !!(g.draggable || c && typeof g.draggable > "u"), A = !!(g.selectable || u && typeof g.selectable > "u"), M = !!(g.connectable || l && typeof g.connectable > "u"), _ = !!(g.focusable || d && typeof g.focusable > "u"), T = ve(), L = mu(g), C = Lv({ node: g, nodeType: N, hasDimensions: L, resizeObserver: f }), I = Zu({
    nodeRef: C,
    disabled: g.hidden || !k,
    noDragClassName: p,
    handleSelector: g.dragHandle,
    nodeId: e,
    isSelectable: A,
    nodeClickDistance: x
  }), E = Gu();
  if (g.hidden)
    return null;
  const D = ut(g), $ = Av(g), P = A || k || t || n || i || r, F = n ? (R) => n(R, { ...v.userNode }) : void 0, B = i ? (R) => i(R, { ...v.userNode }) : void 0, W = r ? (R) => r(R, { ...v.userNode }) : void 0, G = s ? (R) => s(R, { ...v.userNode }) : void 0, H = a ? (R) => a(R, { ...v.userNode }) : void 0, q = (R) => {
    const { selectNodesOnDrag: X, nodeDragThreshold: ce } = T.getState();
    A && (!X || !k || ce > 0) && Vo({
      id: e,
      store: T,
      nodeRef: C
    }), t && t(R, { ...v.userNode });
  }, Q = (R) => {
    if (!(vu(R.nativeEvent) || y)) {
      if (cu.includes(R.key) && A) {
        const X = R.key === "Escape";
        Vo({
          id: e,
          store: T,
          unselect: X,
          nodeRef: C
        });
      } else if (k && g.selected && Object.prototype.hasOwnProperty.call(Gi, R.key)) {
        R.preventDefault();
        const { ariaLabelConfig: X } = T.getState();
        T.setState({
          ariaLiveMessage: X["node.a11yDescription.ariaLiveMessage"]({
            direction: R.key.replace("Arrow", "").toLowerCase(),
            x: ~~v.positionAbsolute.x,
            y: ~~v.positionAbsolute.y
          })
        }), E({
          direction: Gi[R.key],
          factor: R.shiftKey ? 4 : 1
        });
      }
    }
  }, U = () => {
    if (y || !C.current?.matches(":focus-visible"))
      return;
    const { transform: R, width: X, height: ce, autoPanOnNodeFocus: oe, setCenter: ee } = T.getState();
    if (!oe)
      return;
    ms(/* @__PURE__ */ new Map([[e, g]]), { x: 0, y: 0, width: X, height: ce }, R, !0).length > 0 || ee(g.position.x + D.width / 2, g.position.y + D.height / 2, {
      zoom: R[2]
    });
  };
  return o.jsx("div", { className: Ce([
    "react-flow__node",
    `react-flow__node-${N}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [h]: k
    },
    g.className,
    {
      selected: g.selected,
      selectable: A,
      parent: j,
      draggable: k,
      dragging: I
    }
  ]), ref: C, style: {
    zIndex: v.z,
    transform: `translate(${v.positionAbsolute.x}px,${v.positionAbsolute.y}px)`,
    pointerEvents: P ? "all" : "none",
    visibility: L ? "visible" : "hidden",
    ...g.style,
    ...$
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: F, onMouseMove: B, onMouseLeave: W, onContextMenu: G, onClick: q, onDoubleClick: H, onKeyDown: _ ? Q : void 0, tabIndex: _ ? 0 : void 0, onFocus: _ ? U : void 0, role: g.ariaRole ?? (_ ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": y ? void 0 : `${Ou}-${w}`, "aria-label": g.ariaLabel, ...g.domAttributes, children: o.jsx(bv, { value: e, children: o.jsx(S, { id: e, data: g.data, type: N, positionAbsoluteX: v.positionAbsolute.x, positionAbsoluteY: v.positionAbsolute.y, selected: g.selected ?? !1, selectable: A, draggable: k, deletable: g.deletable ?? !0, isConnectable: M, sourcePosition: g.sourcePosition, targetPosition: g.targetPosition, dragging: I, dragHandle: g.dragHandle, zIndex: v.z, parentId: g.parentId, ...D }) }) });
}
var Ov = je(Vv);
const Hv = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function ed(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: i, elementsSelectable: r, onError: s } = pe(Hv, we), a = Mv(e.onlyRenderVisibleElements), c = zv();
  return o.jsx("div", { className: "react-flow__nodes", style: hr, children: a.map((u) => (
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
    o.jsx(Ov, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: i, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: s }, u)
  )) });
}
ed.displayName = "NodeRenderer";
const Wv = je(ed);
function Bv(e) {
  return pe(se((n) => {
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
const Fv = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return o.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, Kv = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return o.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, uc = {
  [Yi.Arrow]: Fv,
  [Yi.ArrowClosed]: Kv
};
function Xv(e) {
  const t = ve();
  return ue(() => Object.prototype.hasOwnProperty.call(uc, e) ? uc[e] : (t.getState().onError?.("009", He.error009(e)), null), [e]);
}
const qv = ({ id: e, type: t, color: n, width: i = 12.5, height: r = 12.5, markerUnits: s = "strokeWidth", strokeWidth: a, orient: c = "auto-start-reverse" }) => {
  const u = Xv(t);
  return u ? o.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${i}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: s, orient: c, refX: "0", refY: "0", children: o.jsx(u, { color: n, strokeWidth: a }) }) : null;
}, td = ({ defaultColor: e, rfId: t }) => {
  const n = pe((s) => s.edges), i = pe((s) => s.defaultEdgeOptions), r = ue(() => Xx(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: i?.markerStart,
    defaultMarkerEnd: i?.markerEnd
  }), [n, i, t, e]);
  return r.length ? o.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: o.jsx("defs", { children: r.map((s) => o.jsx(qv, { id: s.id, type: s.type, color: s.color, width: s.width, height: s.height, markerUnits: s.markerUnits, strokeWidth: s.strokeWidth, orient: s.orient }, s.id)) }) }) : null;
};
td.displayName = "MarkerDefinitions";
var Yv = je(td);
function nd({ x: e, y: t, label: n, labelStyle: i, labelShowBg: r = !0, labelBgStyle: s, labelBgPadding: a = [2, 4], labelBgBorderRadius: c = 2, children: u, className: l, ...d }) {
  const [f, p] = K({ x: 1, y: 0, width: 0, height: 0 }), h = Ce(["react-flow__edge-textwrapper", l]), y = ae(null);
  return te(() => {
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
nd.displayName = "EdgeText";
const Uv = je(nd);
function ti({ path: e, labelX: t, labelY: n, label: i, labelStyle: r, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u, interactionWidth: l = 20, ...d }) {
  return o.jsxs(o.Fragment, { children: [o.jsx("path", { ...d, d: e, fill: "none", className: Ce(["react-flow__edge-path", d.className]) }), l ? o.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, i && qe(t) && qe(n) ? o.jsx(Uv, { x: t, y: n, label: i, labelStyle: r, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u }) : null] });
}
function dc({ pos: e, x1: t, y1: n, x2: i, y2: r }) {
  return e === re.Left || e === re.Right ? [0.5 * (t + i), n] : [t, 0.5 * (n + r)];
}
function id({ sourceX: e, sourceY: t, sourcePosition: n = re.Bottom, targetX: i, targetY: r, targetPosition: s = re.Top }) {
  const [a, c] = dc({
    pos: n,
    x1: e,
    y1: t,
    x2: i,
    y2: r
  }), [u, l] = dc({
    pos: s,
    x1: i,
    y1: r,
    x2: e,
    y2: t
  }), [d, f, p, h] = Nu({
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
function rd(e) {
  return je(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, sourcePosition: a, targetPosition: c, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: w, markerStart: m, interactionWidth: x }) => {
    const [b, g, v] = id({
      sourceX: n,
      sourceY: i,
      sourcePosition: a,
      targetX: r,
      targetY: s,
      targetPosition: c
    }), j = e.isInternal ? void 0 : t;
    return o.jsx(ti, { id: j, path: b, labelX: g, labelY: v, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: w, markerStart: m, interactionWidth: x });
  });
}
const Zv = rd({ isInternal: !1 }), od = rd({ isInternal: !0 });
Zv.displayName = "SimpleBezierEdge";
od.displayName = "SimpleBezierEdgeInternal";
function sd(e) {
  return je(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, sourcePosition: h = re.Bottom, targetPosition: y = re.Top, markerEnd: w, markerStart: m, pathOptions: x, interactionWidth: b }) => {
    const [g, v, j] = Zi({
      sourceX: n,
      sourceY: i,
      sourcePosition: h,
      targetX: r,
      targetY: s,
      targetPosition: y,
      borderRadius: x?.borderRadius,
      offset: x?.offset,
      stepPosition: x?.stepPosition
    }), N = e.isInternal ? void 0 : t;
    return o.jsx(ti, { id: N, path: g, labelX: v, labelY: j, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: w, markerStart: m, interactionWidth: b });
  });
}
const ad = sd({ isInternal: !1 }), cd = sd({ isInternal: !0 });
ad.displayName = "SmoothStepEdge";
cd.displayName = "SmoothStepEdgeInternal";
function ld(e) {
  return je(({ id: t, ...n }) => {
    const i = e.isInternal ? void 0 : t;
    return o.jsx(ad, { ...n, id: i, pathOptions: ue(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Gv = ld({ isInternal: !1 }), ud = ld({ isInternal: !0 });
Gv.displayName = "StepEdge";
ud.displayName = "StepEdgeInternal";
function dd(e) {
  return je(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: y, interactionWidth: w }) => {
    const [m, x, b] = Eu({ sourceX: n, sourceY: i, targetX: r, targetY: s }), g = e.isInternal ? void 0 : t;
    return o.jsx(ti, { id: g, path: m, labelX: x, labelY: b, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: y, interactionWidth: w });
  });
}
const Jv = dd({ isInternal: !1 }), fd = dd({ isInternal: !0 });
Jv.displayName = "StraightEdge";
fd.displayName = "StraightEdgeInternal";
function pd(e) {
  return je(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, sourcePosition: a = re.Bottom, targetPosition: c = re.Top, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: w, markerStart: m, pathOptions: x, interactionWidth: b }) => {
    const [g, v, j] = ju({
      sourceX: n,
      sourceY: i,
      sourcePosition: a,
      targetX: r,
      targetY: s,
      targetPosition: c,
      curvature: x?.curvature
    }), N = e.isInternal ? void 0 : t;
    return o.jsx(ti, { id: N, path: g, labelX: v, labelY: j, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: w, markerStart: m, interactionWidth: b });
  });
}
const Qv = pd({ isInternal: !1 }), hd = pd({ isInternal: !0 });
Qv.displayName = "BezierEdge";
hd.displayName = "BezierEdgeInternal";
const fc = {
  default: hd,
  straight: fd,
  step: ud,
  smoothstep: cd,
  simplebezier: od
}, pc = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, e0 = (e, t, n) => n === re.Left ? e - t : n === re.Right ? e + t : e, t0 = (e, t, n) => n === re.Top ? e - t : n === re.Bottom ? e + t : e, hc = "react-flow__edgeupdater";
function gc({ position: e, centerX: t, centerY: n, radius: i = 10, onMouseDown: r, onMouseEnter: s, onMouseOut: a, type: c }) {
  return o.jsx("circle", { onMouseDown: r, onMouseEnter: s, onMouseOut: a, className: Ce([hc, `${hc}-${c}`]), cx: e0(t, i, e), cy: t0(n, i, e), r: i, stroke: "transparent", fill: "transparent" });
}
function n0({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: i, sourceY: r, targetX: s, targetY: a, sourcePosition: c, targetPosition: u, onReconnect: l, onReconnectStart: d, onReconnectEnd: f, setReconnecting: p, setUpdateHover: h }) {
  const y = ve(), w = (v, j) => {
    if (v.button !== 0)
      return;
    const { autoPanOnConnect: N, domNode: S, connectionMode: k, connectionRadius: A, lib: M, onConnectStart: _, cancelConnection: T, nodeLookup: L, rfId: C, panBy: I, updateConnection: E } = y.getState(), D = j.type === "target", $ = (B, W) => {
      p(!1), f?.(B, n, j.type, W);
    }, P = (B) => l?.(n, B), F = (B, W) => {
      p(!0), d?.(v, n, j.type), _?.(B, W);
    };
    Lo.onPointerDown(v.nativeEvent, {
      autoPanOnConnect: N,
      connectionMode: k,
      connectionRadius: A,
      domNode: S,
      handleId: j.id,
      nodeId: j.nodeId,
      nodeLookup: L,
      isTarget: D,
      edgeUpdaterType: j.type,
      lib: M,
      flowId: C,
      cancelConnection: T,
      panBy: I,
      isValidConnection: (...B) => y.getState().isValidConnection?.(...B) ?? !0,
      onConnect: P,
      onConnectStart: F,
      onConnectEnd: (...B) => y.getState().onConnectEnd?.(...B),
      onReconnectEnd: $,
      updateConnection: E,
      getTransform: () => y.getState().transform,
      getFromHandle: () => y.getState().connection.fromHandle,
      dragThreshold: y.getState().connectionDragThreshold,
      handleDomNode: v.currentTarget
    });
  }, m = (v) => w(v, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), x = (v) => w(v, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), b = () => h(!0), g = () => h(!1);
  return o.jsxs(o.Fragment, { children: [(e === !0 || e === "source") && o.jsx(gc, { position: c, centerX: i, centerY: r, radius: t, onMouseDown: m, onMouseEnter: b, onMouseOut: g, type: "source" }), (e === !0 || e === "target") && o.jsx(gc, { position: u, centerX: s, centerY: a, radius: t, onMouseDown: x, onMouseEnter: b, onMouseOut: g, type: "target" })] });
}
function i0({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: i, onClick: r, onDoubleClick: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, rfId: y, edgeTypes: w, noPanClassName: m, onError: x, disableKeyboardA11y: b }) {
  let g = pe((ee) => ee.edgeLookup.get(e));
  const v = pe((ee) => ee.defaultEdgeOptions);
  g = v ? { ...v, ...g } : g;
  let j = g.type || "default", N = w?.[j] || fc[j];
  N === void 0 && (x?.("011", He.error011(j)), j = "default", N = w?.default || fc.default);
  const S = !!(g.focusable || t && typeof g.focusable > "u"), k = typeof f < "u" && (g.reconnectable || n && typeof g.reconnectable > "u"), A = !!(g.selectable || i && typeof g.selectable > "u"), M = ae(null), [_, T] = K(!1), [L, C] = K(!1), I = ve(), { zIndex: E, sourceX: D, sourceY: $, targetX: P, targetY: F, sourcePosition: B, targetPosition: W } = pe(se((ee) => {
    const ie = ee.nodeLookup.get(g.source), de = ee.nodeLookup.get(g.target);
    if (!ie || !de)
      return {
        zIndex: g.zIndex,
        ...pc
      };
    const V = Kx({
      id: e,
      sourceNode: ie,
      targetNode: de,
      sourceHandle: g.sourceHandle || null,
      targetHandle: g.targetHandle || null,
      connectionMode: ee.connectionMode,
      onError: x
    });
    return {
      zIndex: zx({
        selected: g.selected,
        zIndex: g.zIndex,
        sourceNode: ie,
        targetNode: de,
        elevateOnSelect: ee.elevateEdgesOnSelect,
        zIndexMode: ee.zIndexMode
      }),
      ...V || pc
    };
  }, [g.source, g.target, g.sourceHandle, g.targetHandle, g.selected, g.zIndex]), we), G = ue(() => g.markerStart ? `url('#${Ro(g.markerStart, y)}')` : void 0, [g.markerStart, y]), H = ue(() => g.markerEnd ? `url('#${Ro(g.markerEnd, y)}')` : void 0, [g.markerEnd, y]);
  if (g.hidden || D === null || $ === null || P === null || F === null)
    return null;
  const q = (ee) => {
    const { addSelectedEdges: ie, unselectNodesAndEdges: de, multiSelectionActive: V } = I.getState();
    A && (I.setState({ nodesSelectionActive: !1 }), g.selected && V ? (de({ nodes: [], edges: [g] }), M.current?.blur()) : ie([e])), r && r(ee, g);
  }, Q = s ? (ee) => {
    s(ee, { ...g });
  } : void 0, U = a ? (ee) => {
    a(ee, { ...g });
  } : void 0, R = c ? (ee) => {
    c(ee, { ...g });
  } : void 0, X = u ? (ee) => {
    u(ee, { ...g });
  } : void 0, ce = l ? (ee) => {
    l(ee, { ...g });
  } : void 0, oe = (ee) => {
    if (!b && cu.includes(ee.key) && A) {
      const { unselectNodesAndEdges: ie, addSelectedEdges: de } = I.getState();
      ee.key === "Escape" ? (M.current?.blur(), ie({ edges: [g] })) : de([e]);
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
      inactive: !A && !r,
      updating: _,
      selectable: A
    }
  ]), onClick: q, onDoubleClick: Q, onContextMenu: U, onMouseEnter: R, onMouseMove: X, onMouseLeave: ce, onKeyDown: S ? oe : void 0, tabIndex: S ? 0 : void 0, role: g.ariaRole ?? (S ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": g.ariaLabel === null ? void 0 : g.ariaLabel || `Edge from ${g.source} to ${g.target}`, "aria-describedby": S ? `${Hu}-${y}` : void 0, ref: M, ...g.domAttributes, children: [!L && o.jsx(N, { id: e, source: g.source, target: g.target, type: g.type, selected: g.selected, animated: g.animated, selectable: A, deletable: g.deletable ?? !0, label: g.label, labelStyle: g.labelStyle, labelShowBg: g.labelShowBg, labelBgStyle: g.labelBgStyle, labelBgPadding: g.labelBgPadding, labelBgBorderRadius: g.labelBgBorderRadius, sourceX: D, sourceY: $, targetX: P, targetY: F, sourcePosition: B, targetPosition: W, data: g.data, style: g.style, sourceHandleId: g.sourceHandle, targetHandleId: g.targetHandle, markerStart: G, markerEnd: H, pathOptions: "pathOptions" in g ? g.pathOptions : void 0, interactionWidth: g.interactionWidth }), k && o.jsx(n0, { edge: g, isReconnectable: k, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, sourceX: D, sourceY: $, targetX: P, targetY: F, sourcePosition: B, targetPosition: W, setUpdateHover: T, setReconnecting: C })] }) });
}
var r0 = je(i0);
const o0 = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function gd({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: i, noPanClassName: r, onReconnect: s, onEdgeContextMenu: a, onEdgeMouseEnter: c, onEdgeMouseMove: u, onEdgeMouseLeave: l, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: p, onReconnectStart: h, onReconnectEnd: y, disableKeyboardA11y: w }) {
  const { edgesFocusable: m, edgesReconnectable: x, elementsSelectable: b, onError: g } = pe(o0, we), v = Bv(t);
  return o.jsxs("div", { className: "react-flow__edges", children: [o.jsx(Yv, { defaultColor: e, rfId: n }), v.map((j) => o.jsx(r0, { id: j, edgesFocusable: m, edgesReconnectable: x, elementsSelectable: b, noPanClassName: r, onReconnect: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, onClick: d, reconnectRadius: f, onDoubleClick: p, onReconnectStart: h, onReconnectEnd: y, rfId: n, onError: g, edgeTypes: i, disableKeyboardA11y: w }, j))] });
}
gd.displayName = "EdgeRenderer";
const s0 = je(gd), a0 = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function c0({ children: e }) {
  const t = pe(a0);
  return o.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function l0(e) {
  const t = Es(), n = ae(!1);
  te(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const u0 = (e) => e.panZoom?.syncViewport;
function d0(e) {
  const t = pe(u0), n = ve();
  return te(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function f0(e) {
  return e.connection.inProgress ? { ...e.connection, to: fn(e.connection.to, e.transform) } : { ...e.connection };
}
function p0(e) {
  return f0;
}
function h0(e) {
  const t = p0();
  return pe(t, we);
}
const g0 = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function y0({ containerStyle: e, style: t, type: n, component: i }) {
  const { nodesConnectable: r, width: s, height: a, isValid: c, inProgress: u } = pe(g0, we);
  return !(s && r && u) ? null : o.jsx("svg", { style: e, width: s, height: a, className: "react-flow__connectionline react-flow__container", children: o.jsx("g", { className: Ce(["react-flow__connection", du(c)]), children: o.jsx(yd, { style: t, type: n, CustomComponent: i, isValid: c }) }) });
}
const yd = ({ style: e, type: t = ht.Bezier, CustomComponent: n, isValid: i }) => {
  const { inProgress: r, from: s, fromNode: a, fromHandle: c, fromPosition: u, to: l, toNode: d, toHandle: f, toPosition: p, pointer: h } = h0();
  if (!r)
    return;
  if (n)
    return o.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: a, fromHandle: c, fromX: s.x, fromY: s.y, toX: l.x, toY: l.y, fromPosition: u, toPosition: p, connectionStatus: du(i), toNode: d, toHandle: f, pointer: h });
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
    case ht.Bezier:
      [y] = ju(w);
      break;
    case ht.SimpleBezier:
      [y] = id(w);
      break;
    case ht.Step:
      [y] = Zi({
        ...w,
        borderRadius: 0
      });
      break;
    case ht.SmoothStep:
      [y] = Zi(w);
      break;
    default:
      [y] = Eu(w);
  }
  return o.jsx("path", { d: y, fill: "none", className: "react-flow__connection-path", style: e });
};
yd.displayName = "ConnectionLine";
const m0 = {};
function yc(e = m0) {
  ae(e), ve(), te(() => {
  }, [e]);
}
function x0() {
  ve(), ae(!1), te(() => {
  }, []);
}
function md({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: i, onEdgeClick: r, onNodeDoubleClick: s, onEdgeDoubleClick: a, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: p, onSelectionEnd: h, connectionLineType: y, connectionLineStyle: w, connectionLineComponent: m, connectionLineContainerStyle: x, selectionKeyCode: b, selectionOnDrag: g, selectionMode: v, multiSelectionKeyCode: j, panActivationKeyCode: N, zoomActivationKeyCode: S, deleteKeyCode: k, onlyRenderVisibleElements: A, elementsSelectable: M, defaultViewport: _, translateExtent: T, minZoom: L, maxZoom: C, preventScrolling: I, defaultMarkerColor: E, zoomOnScroll: D, zoomOnPinch: $, panOnScroll: P, panOnScrollSpeed: F, panOnScrollMode: B, zoomOnDoubleClick: W, panOnDrag: G, autoPanOnSelection: H, onPaneClick: q, onPaneMouseEnter: Q, onPaneMouseMove: U, onPaneMouseLeave: R, onPaneScroll: X, onPaneContextMenu: ce, paneClickDistance: oe, nodeClickDistance: ee, onEdgeContextMenu: ie, onEdgeMouseEnter: de, onEdgeMouseMove: V, onEdgeMouseLeave: ne, reconnectRadius: he, onReconnect: ge, onReconnectStart: Ee, onReconnectEnd: ke, noDragClassName: De, noWheelClassName: it, noPanClassName: We, disableKeyboardA11y: Be, nodeExtent: Te, rfId: Re, viewport: z, onViewportChange: O }) {
  return yc(e), yc(t), x0(), l0(n), d0(z), o.jsx($v, { onPaneClick: q, onPaneMouseEnter: Q, onPaneMouseMove: U, onPaneMouseLeave: R, onPaneContextMenu: ce, onPaneScroll: X, paneClickDistance: oe, deleteKeyCode: k, selectionKeyCode: b, selectionOnDrag: g, selectionMode: v, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: j, panActivationKeyCode: N, zoomActivationKeyCode: S, elementsSelectable: M, zoomOnScroll: D, zoomOnPinch: $, zoomOnDoubleClick: W, panOnScroll: P, panOnScrollSpeed: F, panOnScrollMode: B, panOnDrag: G, autoPanOnSelection: H, defaultViewport: _, translateExtent: T, minZoom: L, maxZoom: C, onSelectionContextMenu: f, preventScrolling: I, noDragClassName: De, noWheelClassName: it, noPanClassName: We, disableKeyboardA11y: Be, onViewportChange: O, isControlledViewport: !!z, children: o.jsxs(c0, { children: [o.jsx(s0, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: a, onReconnect: ge, onReconnectStart: Ee, onReconnectEnd: ke, onlyRenderVisibleElements: A, onEdgeContextMenu: ie, onEdgeMouseEnter: de, onEdgeMouseMove: V, onEdgeMouseLeave: ne, reconnectRadius: he, defaultMarkerColor: E, noPanClassName: We, disableKeyboardA11y: Be, rfId: Re }), o.jsx(y0, { style: w, type: y, component: m, containerStyle: x }), o.jsx("div", { className: "react-flow__edgelabel-renderer" }), o.jsx(Wv, { nodeTypes: e, onNodeClick: i, onNodeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, nodeClickDistance: ee, onlyRenderVisibleElements: A, noPanClassName: We, noDragClassName: De, disableKeyboardA11y: Be, nodeExtent: Te, rfId: Re }), o.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
md.displayName = "GraphView";
const w0 = je(md), v0 = yu(), mc = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, width: r, height: s, fitView: a, fitViewOptions: c, minZoom: u = 0.5, maxZoom: l = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: p = "basic" } = {}) => {
  const h = /* @__PURE__ */ new Map(), y = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), m = /* @__PURE__ */ new Map(), x = i ?? t ?? [], b = n ?? e ?? [], g = d ?? [0, 0], v = f ?? On;
  Au(w, m, x);
  const { nodesInitialized: j } = zo(b, h, y, {
    nodeOrigin: g,
    nodeExtent: v,
    zIndexMode: p
  });
  let N = [0, 0, 1];
  if (a && r && s) {
    const S = Qn(h, {
      filter: (_) => !!((_.width || _.initialWidth) && (_.height || _.initialHeight))
    }), { x: k, y: A, zoom: M } = ws(S, r, s, u, l, c?.padding ?? 0.1);
    N = [k, A, M];
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
    edges: x,
    edgeLookup: m,
    connectionLookup: w,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: i !== void 0,
    panZoom: null,
    minZoom: u,
    maxZoom: l,
    translateExtent: On,
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
    connection: { ...uu },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: v0,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: lu,
    zIndexMode: p,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, b0 = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, width: r, height: s, fitView: a, fitViewOptions: c, minZoom: u, maxZoom: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p }) => Pw((h, y) => {
  async function w() {
    const { nodeLookup: m, panZoom: x, fitViewOptions: b, fitViewResolver: g, width: v, height: j, minZoom: N, maxZoom: S } = y();
    x && (await _x({
      nodes: m,
      width: v,
      height: j,
      panZoom: x,
      minZoom: N,
      maxZoom: S
    }, b), g?.resolve(!0), h({ fitViewResolver: null }));
  }
  return {
    ...mc({
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
      const { nodeLookup: x, parentLookup: b, nodeOrigin: g, elevateNodesOnSelect: v, fitViewQueued: j, zIndexMode: N, nodesSelectionActive: S } = y(), { nodesInitialized: k, hasSelectedNodes: A } = zo(m, x, b, {
        nodeOrigin: g,
        nodeExtent: f,
        elevateNodesOnSelect: v,
        checkEquality: !0,
        zIndexMode: N
      }), M = S && A;
      j && k ? (w(), h({
        nodes: m,
        nodesInitialized: k,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: M
      })) : h({ nodes: m, nodesInitialized: k, nodesSelectionActive: M });
    },
    setEdges: (m) => {
      const { connectionLookup: x, edgeLookup: b } = y();
      Au(x, b, m), h({ edges: m });
    },
    setDefaultNodesAndEdges: (m, x) => {
      if (m) {
        const { setNodes: b } = y();
        b(m), h({ hasDefaultNodes: !0 });
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
    updateNodeInternals: (m) => {
      const { triggerNodeChanges: x, nodeLookup: b, parentLookup: g, domNode: v, nodeOrigin: j, nodeExtent: N, debug: S, fitViewQueued: k, zIndexMode: A } = y(), { changes: M, updatedInternals: _ } = Qx(m, b, g, v, j, N, A);
      _ && (Ux(b, g, { nodeOrigin: j, nodeExtent: N, zIndexMode: A }), k ? (w(), h({ fitViewQueued: !1, fitViewOptions: void 0 })) : h({}), M?.length > 0 && (S && console.log("React Flow: trigger node changes", M), x?.(M)));
    },
    updateNodePositions: (m, x = !1) => {
      const b = [];
      let g = [];
      const { nodeLookup: v, triggerNodeChanges: j, connection: N, updateConnection: S, onNodesChangeMiddlewareMap: k } = y();
      for (const [A, M] of m) {
        const _ = v.get(A), T = !!(_?.expandParent && _?.parentId && M?.position), L = {
          id: A,
          type: "position",
          position: T ? {
            x: Math.max(0, M.position.x),
            y: Math.max(0, M.position.y)
          } : M.position,
          dragging: x
        };
        if (_ && N.inProgress && N.fromNode.id === _.id) {
          const C = Dt(_, N.fromHandle, re.Left, !0);
          S({ ...N, from: C });
        }
        T && _.parentId && b.push({
          id: A,
          parentId: _.parentId,
          rect: {
            ...M.internals.positionAbsolute,
            width: M.measured.width ?? 0,
            height: M.measured.height ?? 0
          }
        }), g.push(L);
      }
      if (b.length > 0) {
        const { parentLookup: A, nodeOrigin: M } = y(), _ = Cs(b, v, A, M);
        g.push(..._);
      }
      for (const A of k.values())
        g = A(g);
      j(g);
    },
    triggerNodeChanges: (m) => {
      const { onNodesChange: x, setNodes: b, nodes: g, hasDefaultNodes: v, debug: j } = y();
      if (m?.length) {
        if (v) {
          const N = Fu(m, g);
          b(N);
        }
        j && console.log("React Flow: trigger node changes", m), x?.(m);
      }
    },
    triggerEdgeChanges: (m) => {
      const { onEdgesChange: x, setEdges: b, edges: g, hasDefaultEdges: v, debug: j } = y();
      if (m?.length) {
        if (v) {
          const N = Ku(m, g);
          b(N);
        }
        j && console.log("React Flow: trigger edge changes", m), x?.(m);
      }
    },
    addSelectedNodes: (m) => {
      const { multiSelectionActive: x, edgeLookup: b, nodeLookup: g, triggerNodeChanges: v, triggerEdgeChanges: j } = y();
      if (x) {
        const N = m.map((S) => vt(S, !0));
        v(N);
        return;
      }
      v(Ft(g, /* @__PURE__ */ new Set([...m]), !0)), j(Ft(b));
    },
    addSelectedEdges: (m) => {
      const { multiSelectionActive: x, edgeLookup: b, nodeLookup: g, triggerNodeChanges: v, triggerEdgeChanges: j } = y();
      if (x) {
        const N = m.map((S) => vt(S, !0));
        j(N);
        return;
      }
      j(Ft(b, /* @__PURE__ */ new Set([...m]))), v(Ft(g, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: m, edges: x } = {}) => {
      const { edges: b, nodes: g, nodeLookup: v, triggerNodeChanges: j, triggerEdgeChanges: N } = y(), S = m || g, k = x || b, A = [];
      for (const _ of S) {
        if (!_.selected)
          continue;
        const T = v.get(_.id);
        T && (T.selected = !1), A.push(vt(_.id, !1));
      }
      const M = [];
      for (const _ of k)
        _.selected && M.push(vt(_.id, !1));
      j(A), N(M);
    },
    setMinZoom: (m) => {
      const { panZoom: x, maxZoom: b } = y();
      x?.setScaleExtent([m, b]), h({ minZoom: m });
    },
    setMaxZoom: (m) => {
      const { panZoom: x, minZoom: b } = y();
      x?.setScaleExtent([b, m]), h({ maxZoom: m });
    },
    setTranslateExtent: (m) => {
      y().panZoom?.setTranslateExtent(m), h({ translateExtent: m });
    },
    resetSelectedElements: () => {
      const { edges: m, nodes: x, triggerNodeChanges: b, triggerEdgeChanges: g, elementsSelectable: v } = y();
      if (!v)
        return;
      const j = x.reduce((S, k) => k.selected ? [...S, vt(k.id, !1)] : S, []), N = m.reduce((S, k) => k.selected ? [...S, vt(k.id, !1)] : S, []);
      b(j), g(N);
    },
    setNodeExtent: (m) => {
      const { nodes: x, nodeLookup: b, parentLookup: g, nodeOrigin: v, elevateNodesOnSelect: j, nodeExtent: N, zIndexMode: S } = y();
      m[0][0] === N[0][0] && m[0][1] === N[0][1] && m[1][0] === N[1][0] && m[1][1] === N[1][1] || (zo(x, b, g, {
        nodeOrigin: v,
        nodeExtent: m,
        elevateNodesOnSelect: j,
        checkEquality: !1,
        zIndexMode: S
      }), h({ nodeExtent: m }));
    },
    panBy: (m) => {
      const { transform: x, width: b, height: g, panZoom: v, translateExtent: j } = y();
      return ew({ delta: m, panZoom: v, transform: x, translateExtent: j, width: b, height: g });
    },
    setCenter: async (m, x, b) => {
      const { width: g, height: v, maxZoom: j, panZoom: N } = y();
      if (!N)
        return !1;
      const S = typeof b?.zoom < "u" ? b.zoom : j;
      return await N.setViewport({
        x: g / 2 - m * S,
        y: v / 2 - x * S,
        zoom: S
      }, { duration: b?.duration, ease: b?.ease, interpolate: b?.interpolate }), !0;
    },
    cancelConnection: () => {
      h({
        connection: { ...uu }
      });
    },
    updateConnection: (m) => {
      h({ connection: m });
    },
    reset: () => h({ ...mc() })
  };
}, Object.is);
function N0({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: i, initialWidth: r, initialHeight: s, initialMinZoom: a, initialMaxZoom: c, initialFitViewOptions: u, fitView: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p, children: h }) {
  const [y] = K(() => b0({
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
  return o.jsx(Lw, { value: y, children: o.jsx(cv, { children: h }) });
}
function j0({ children: e, nodes: t, edges: n, defaultNodes: i, defaultEdges: r, width: s, height: a, fitView: c, fitViewOptions: u, minZoom: l, maxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h }) {
  return Kn(fr) ? o.jsx(o.Fragment, { children: e }) : o.jsx(N0, { initialNodes: t, initialEdges: n, defaultNodes: i, defaultEdges: r, initialWidth: s, initialHeight: a, fitView: c, initialFitViewOptions: u, initialMinZoom: l, initialMaxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h, children: e });
}
const S0 = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function C0({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, className: r, nodeTypes: s, edgeTypes: a, onNodeClick: c, onEdgeClick: u, onInit: l, onMove: d, onMoveStart: f, onMoveEnd: p, onConnect: h, onConnectStart: y, onConnectEnd: w, onClickConnectStart: m, onClickConnectEnd: x, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: v, onNodeContextMenu: j, onNodeDoubleClick: N, onNodeDragStart: S, onNodeDrag: k, onNodeDragStop: A, onNodesDelete: M, onEdgesDelete: _, onDelete: T, onSelectionChange: L, onSelectionDragStart: C, onSelectionDrag: I, onSelectionDragStop: E, onSelectionContextMenu: D, onSelectionStart: $, onSelectionEnd: P, onBeforeDelete: F, connectionMode: B, connectionLineType: W = ht.Bezier, connectionLineStyle: G, connectionLineComponent: H, connectionLineContainerStyle: q, deleteKeyCode: Q = "Backspace", selectionKeyCode: U = "Shift", selectionOnDrag: R = !1, selectionMode: X = Hn.Full, panActivationKeyCode: ce = "Space", multiSelectionKeyCode: oe = Bn() ? "Meta" : "Control", zoomActivationKeyCode: ee = Bn() ? "Meta" : "Control", snapToGrid: ie, snapGrid: de, onlyRenderVisibleElements: V = !1, selectNodesOnDrag: ne, nodesDraggable: he, autoPanOnNodeFocus: ge, nodesConnectable: Ee, nodesFocusable: ke, nodeOrigin: De = Wu, edgesFocusable: it, edgesReconnectable: We, elementsSelectable: Be = !0, defaultViewport: Te = Gw, minZoom: Re = 0.5, maxZoom: z = 2, translateExtent: O = On, preventScrolling: Y = !0, nodeExtent: Z, defaultMarkerColor: le = "#b1b1b7", zoomOnScroll: xe = !0, zoomOnPinch: ye = !0, panOnScroll: Ie = !1, panOnScrollSpeed: Ae = 0.5, panOnScrollMode: Fe = St.Free, zoomOnDoubleClick: me = !0, panOnDrag: mr = !0, onPaneClick: ii, onPaneMouseEnter: xt, onPaneMouseMove: Mt, onPaneMouseLeave: Ge, onPaneScroll: Rt, onPaneContextMenu: xr, paneClickDistance: ri = 1, nodeClickDistance: wr = 0, children: vr, onReconnect: dt, onReconnectStart: br, onReconnectEnd: zt, onEdgeContextMenu: Nr, onEdgeDoubleClick: Lt, onEdgeMouseEnter: jr, onEdgeMouseMove: oi, onEdgeMouseLeave: si, reconnectRadius: Sr = 10, onNodesChange: Vt, onEdgesChange: Cr, noDragClassName: Er = "nodrag", noWheelClassName: kr = "nowheel", noPanClassName: Ot = "nopan", fitView: ai, fitViewOptions: ci, connectOnClick: Ir, attributionPosition: Ar, proOptions: _r, defaultEdgeOptions: Dr, elevateNodesOnSelect: Tr = !0, elevateEdgesOnSelect: $r = !1, disableKeyboardA11y: li = !1, autoPanOnConnect: Pr, autoPanOnNodeDrag: Mr, autoPanOnSelection: Rr = !0, autoPanSpeed: zr, connectionRadius: Lr, isValidConnection: ui, onError: di, style: fi, id: hn, nodeDragThreshold: Vr, connectionDragThreshold: Or, viewport: Hr, onViewportChange: Wr, width: Br, height: Fr, colorMode: Kr = "light", debug: Xr, onScroll: pi, ariaLabelConfig: qr, zIndexMode: hi = "basic", ...Yr }, Ur) {
  const gn = hn || "1", Zr = tv(Kr), Gr = se((wt) => {
    wt.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), pi?.(wt);
  }, [pi]);
  return o.jsx("div", { "data-testid": "rf__wrapper", ...Yr, onScroll: Gr, style: { ...fi, ...S0 }, ref: Ur, className: Ce(["react-flow", r, Zr]), id: hn, role: "application", children: o.jsxs(j0, { nodes: e, edges: t, width: Br, height: Fr, fitView: ai, fitViewOptions: ci, minZoom: Re, maxZoom: z, nodeOrigin: De, nodeExtent: Z, zIndexMode: hi, children: [o.jsx(ev, { nodes: e, edges: t, defaultNodes: n, defaultEdges: i, onConnect: h, onConnectStart: y, onConnectEnd: w, onClickConnectStart: m, onClickConnectEnd: x, nodesDraggable: he, autoPanOnNodeFocus: ge, nodesConnectable: Ee, nodesFocusable: ke, edgesFocusable: it, edgesReconnectable: We, elementsSelectable: Be, elevateNodesOnSelect: Tr, elevateEdgesOnSelect: $r, minZoom: Re, maxZoom: z, nodeExtent: Z, onNodesChange: Vt, onEdgesChange: Cr, snapToGrid: ie, snapGrid: de, connectionMode: B, translateExtent: O, connectOnClick: Ir, defaultEdgeOptions: Dr, fitView: ai, fitViewOptions: ci, onNodesDelete: M, onEdgesDelete: _, onDelete: T, onNodeDragStart: S, onNodeDrag: k, onNodeDragStop: A, onSelectionDrag: I, onSelectionDragStart: C, onSelectionDragStop: E, onMove: d, onMoveStart: f, onMoveEnd: p, noPanClassName: Ot, nodeOrigin: De, rfId: gn, autoPanOnConnect: Pr, autoPanOnNodeDrag: Mr, autoPanSpeed: zr, onError: di, connectionRadius: Lr, isValidConnection: ui, selectNodesOnDrag: ne, nodeDragThreshold: Vr, connectionDragThreshold: Or, onBeforeDelete: F, debug: Xr, ariaLabelConfig: qr, zIndexMode: hi }), o.jsx(w0, { onInit: l, onNodeClick: c, onEdgeClick: u, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: v, onNodeContextMenu: j, onNodeDoubleClick: N, nodeTypes: s, edgeTypes: a, connectionLineType: W, connectionLineStyle: G, connectionLineComponent: H, connectionLineContainerStyle: q, selectionKeyCode: U, selectionOnDrag: R, selectionMode: X, deleteKeyCode: Q, multiSelectionKeyCode: oe, panActivationKeyCode: ce, zoomActivationKeyCode: ee, onlyRenderVisibleElements: V, defaultViewport: Te, translateExtent: O, minZoom: Re, maxZoom: z, preventScrolling: Y, zoomOnScroll: xe, zoomOnPinch: ye, zoomOnDoubleClick: me, panOnScroll: Ie, panOnScrollSpeed: Ae, panOnScrollMode: Fe, panOnDrag: mr, autoPanOnSelection: Rr, onPaneClick: ii, onPaneMouseEnter: xt, onPaneMouseMove: Mt, onPaneMouseLeave: Ge, onPaneScroll: Rt, onPaneContextMenu: xr, paneClickDistance: ri, nodeClickDistance: wr, onSelectionContextMenu: D, onSelectionStart: $, onSelectionEnd: P, onReconnect: dt, onReconnectStart: br, onReconnectEnd: zt, onEdgeContextMenu: Nr, onEdgeDoubleClick: Lt, onEdgeMouseEnter: jr, onEdgeMouseMove: oi, onEdgeMouseLeave: si, reconnectRadius: Sr, defaultMarkerColor: le, noDragClassName: Er, noWheelClassName: kr, noPanClassName: Ot, rfId: gn, disableKeyboardA11y: li, nodeExtent: Z, viewport: Hr, onViewportChange: Wr }), o.jsx(Zw, { onSelectionChange: L }), vr, o.jsx(Kw, { proOptions: _r, position: Ar }), o.jsx(Fw, { rfId: gn, disableKeyboardA11y: li })] }) });
}
var xd = Yu(C0);
const E0 = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function k0({ children: e }) {
  const t = pe(E0);
  return t ? zw.createPortal(e, t) : null;
}
function I0({ dimensions: e, lineWidth: t, variant: n, className: i }) {
  return o.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ce(["react-flow__background-pattern", n, i]) });
}
function A0({ radius: e, className: t }) {
  return o.jsx("circle", { cx: e, cy: e, r: e, className: Ce(["react-flow__background-pattern", "dots", t]) });
}
var gt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(gt || (gt = {}));
const _0 = {
  [gt.Dots]: 1,
  [gt.Lines]: 1,
  [gt.Cross]: 6
}, D0 = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function wd({
  id: e,
  variant: t = gt.Dots,
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
  const f = ae(null), { transform: p, patternId: h } = pe(D0, we), y = i || _0[t], w = t === gt.Dots, m = t === gt.Cross, x = Array.isArray(n) ? n : [n, n], b = [x[0] * p[2] || 1, x[1] * p[2] || 1], g = y * p[2], v = Array.isArray(s) ? s : [s, s], j = m ? [g, g] : b, N = [
    v[0] * p[2] || 1 + j[0] / 2,
    v[1] * p[2] || 1 + j[1] / 2
  ], S = `${h}${e || ""}`;
  return o.jsxs("svg", { className: Ce(["react-flow__background", l]), style: {
    ...u,
    ...hr,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": a
  }, ref: f, "data-testid": "rf__background", children: [o.jsx("pattern", { id: S, x: p[0] % b[0], y: p[1] % b[1], width: b[0], height: b[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${N[0]},-${N[1]})`, children: w ? o.jsx(A0, { radius: g / 2, className: d }) : o.jsx(I0, { dimensions: j, lineWidth: r, variant: t, className: d }) }), o.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${S})` })] });
}
wd.displayName = "Background";
const vd = je(wd);
function T0() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: o.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function $0() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: o.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function P0() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: o.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function M0() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: o.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function R0() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: o.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function ji({ children: e, className: t, ...n }) {
  return o.jsx("button", { type: "button", className: Ce(["react-flow__controls-button", t]), ...n, children: e });
}
const z0 = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function bd({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: i = !0, fitViewOptions: r, onZoomIn: s, onZoomOut: a, onFitView: c, onInteractiveChange: u, className: l, children: d, position: f = "bottom-left", orientation: p = "vertical", "aria-label": h }) {
  const y = ve(), { isInteractive: w, minZoomReached: m, maxZoomReached: x, ariaLabelConfig: b } = pe(z0, we), { zoomIn: g, zoomOut: v, fitView: j } = Es(), N = () => {
    g(), s?.();
  }, S = () => {
    v(), a?.();
  }, k = () => {
    j(r), c?.();
  }, A = () => {
    y.setState({
      nodesDraggable: !w,
      nodesConnectable: !w,
      elementsSelectable: !w
    }), u?.(!w);
  }, M = p === "horizontal" ? "horizontal" : "vertical";
  return o.jsxs(pr, { className: Ce(["react-flow__controls", M, l]), position: f, style: e, "data-testid": "rf__controls", "aria-label": h ?? b["controls.ariaLabel"], children: [t && o.jsxs(o.Fragment, { children: [o.jsx(ji, { onClick: N, className: "react-flow__controls-zoomin", title: b["controls.zoomIn.ariaLabel"], "aria-label": b["controls.zoomIn.ariaLabel"], disabled: x, children: o.jsx(T0, {}) }), o.jsx(ji, { onClick: S, className: "react-flow__controls-zoomout", title: b["controls.zoomOut.ariaLabel"], "aria-label": b["controls.zoomOut.ariaLabel"], disabled: m, children: o.jsx($0, {}) })] }), n && o.jsx(ji, { className: "react-flow__controls-fitview", onClick: k, title: b["controls.fitView.ariaLabel"], "aria-label": b["controls.fitView.ariaLabel"], children: o.jsx(P0, {}) }), i && o.jsx(ji, { className: "react-flow__controls-interactive", onClick: A, title: b["controls.interactive.ariaLabel"], "aria-label": b["controls.interactive.ariaLabel"], children: w ? o.jsx(R0, {}) : o.jsx(M0, {}) }), d] });
}
bd.displayName = "Controls";
const Nd = je(bd);
function L0({ id: e, x: t, y: n, width: i, height: r, style: s, color: a, strokeColor: c, strokeWidth: u, className: l, borderRadius: d, shapeRendering: f, selected: p, onClick: h }) {
  const { background: y, backgroundColor: w } = s || {}, m = a || y || w;
  return o.jsx("rect", { className: Ce(["react-flow__minimap-node", { selected: p }, l]), x: t, y: n, rx: d, ry: d, width: i, height: r, style: {
    fill: m,
    stroke: c,
    strokeWidth: u
  }, shapeRendering: f, onClick: h ? (x) => h(x, e) : void 0 });
}
const V0 = je(L0), O0 = (e) => e.nodes.map((t) => t.id), wo = (e) => e instanceof Function ? e : () => e;
function H0({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: i = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: s = V0,
  onClick: a
}) {
  const c = pe(O0, we), u = wo(t), l = wo(e), d = wo(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return o.jsx(o.Fragment, { children: c.map((p) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    o.jsx(B0, { id: p, nodeColorFunc: u, nodeStrokeColorFunc: l, nodeClassNameFunc: d, nodeBorderRadius: i, nodeStrokeWidth: r, NodeComponent: s, onClick: a, shapeRendering: f }, p)
  )) });
}
function W0({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: i, nodeBorderRadius: r, nodeStrokeWidth: s, shapeRendering: a, NodeComponent: c, onClick: u }) {
  const { node: l, x: d, y: f, width: p, height: h } = pe((y) => {
    const w = y.nodeLookup.get(e);
    if (!w)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const m = w.internals.userNode, { x, y: b } = w.internals.positionAbsolute, { width: g, height: v } = ut(m);
    return {
      node: m,
      x,
      y: b,
      width: g,
      height: v
    };
  }, we);
  return !l || l.hidden || !mu(l) ? null : o.jsx(c, { x: d, y: f, width: p, height: h, style: l.style, selected: !!l.selected, className: i(l), color: t(l), borderRadius: r, strokeColor: n(l), strokeWidth: s, shapeRendering: a, onClick: u, id: l.id });
}
const B0 = je(W0);
var F0 = je(H0);
const K0 = 200, X0 = 150, q0 = (e) => !e.hidden, Y0 = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? gu(Qn(e.nodeLookup, { filter: q0 }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, U0 = "react-flow__minimap-desc";
function jd({
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
  zoomable: m = !1,
  ariaLabel: x,
  inversePan: b,
  zoomStep: g = 1,
  offsetScale: v = 5
}) {
  const j = ve(), N = ae(null), { boundingRect: S, viewBB: k, rfId: A, panZoom: M, translateExtent: _, flowWidth: T, flowHeight: L, ariaLabelConfig: C } = pe(Y0, we), I = e?.width ?? K0, E = e?.height ?? X0, D = S.width / I, $ = S.height / E, P = Math.max(D, $), F = P * I, B = P * E, W = v * P, G = S.x - (F - S.width) / 2 - W, H = S.y - (B - S.height) / 2 - W, q = F + W * 2, Q = B + W * 2, U = `${U0}-${A}`, R = ae(0), X = ae();
  R.current = P, te(() => {
    if (N.current && M)
      return X.current = lw({
        domNode: N.current,
        panZoom: M,
        getTransform: () => j.getState().transform,
        getViewScale: () => R.current
      }), () => {
        X.current?.destroy();
      };
  }, [M]), te(() => {
    X.current?.update({
      translateExtent: _,
      width: T,
      height: L,
      inversePan: b,
      pannable: w,
      zoomStep: g,
      zoomable: m
    });
  }, [w, m, b, g, _, T, L]);
  const ce = h ? (ie) => {
    const [de, V] = X.current?.pointer(ie) || [0, 0];
    h(ie, { x: de, y: V });
  } : void 0, oe = y ? se((ie, de) => {
    const V = j.getState().nodeLookup.get(de).internals.userNode;
    y(ie, V);
  }, []) : void 0, ee = x ?? C["minimap.ariaLabel"];
  return o.jsx(pr, { position: p, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * P : void 0,
    "--xy-minimap-node-background-color-props": typeof i == "string" ? i : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof a == "number" ? a : void 0
  }, className: Ce(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: o.jsxs("svg", { width: I, height: E, viewBox: `${G} ${H} ${q} ${Q}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": U, ref: N, onClick: ce, children: [ee && o.jsx("title", { id: U, children: ee }), o.jsx(F0, { onClick: oe, nodeColor: i, nodeStrokeColor: n, nodeBorderRadius: s, nodeClassName: r, nodeStrokeWidth: a, nodeComponent: c }), o.jsx("path", { className: "react-flow__minimap-mask", d: `M${G - W},${H - W}h${q + W * 2}v${Q + W * 2}h${-q - W * 2}z
        M${k.x},${k.y}h${k.width}v${k.height}h${-k.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
jd.displayName = "MiniMap";
const Sd = je(jd), Z0 = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, G0 = {
  [an.Line]: "right",
  [an.Handle]: "bottom-right"
};
function J0({ nodeId: e, position: t, variant: n = an.Handle, className: i, style: r = void 0, children: s, color: a, minWidth: c = 10, minHeight: u = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: p, autoScale: h = !0, shouldResize: y, onResizeStart: w, onResize: m, onResizeEnd: x }) {
  const b = Ju(), g = typeof e == "string" ? e : b, v = ve(), j = ae(null), N = n === an.Handle, S = pe(se(Z0(N && h), [N, h]), we), k = ae(null), A = t ?? G0[n];
  te(() => {
    if (!(!j.current || !g))
      return k.current || (k.current = Nw({
        domNode: j.current,
        nodeId: g,
        getStoreItems: () => {
          const { nodeLookup: _, transform: T, snapGrid: L, snapToGrid: C, nodeOrigin: I, domNode: E } = v.getState();
          return {
            nodeLookup: _,
            transform: T,
            snapGrid: L,
            snapToGrid: C,
            nodeOrigin: I,
            paneDomNode: E
          };
        },
        onChange: (_, T) => {
          const { triggerNodeChanges: L, nodeLookup: C, parentLookup: I, nodeOrigin: E } = v.getState(), D = [], $ = { x: _.x, y: _.y }, P = C.get(g);
          if (P && P.expandParent && P.parentId) {
            const F = P.origin ?? E, B = _.width ?? P.measured.width ?? 0, W = _.height ?? P.measured.height ?? 0, G = {
              id: P.id,
              parentId: P.parentId,
              rect: {
                width: B,
                height: W,
                ...xu({
                  x: _.x ?? P.position.x,
                  y: _.y ?? P.position.y
                }, { width: B, height: W }, P.parentId, C, F)
              }
            }, H = Cs([G], C, I, E);
            D.push(...H), $.x = _.x ? Math.max(F[0] * B, _.x) : void 0, $.y = _.y ? Math.max(F[1] * W, _.y) : void 0;
          }
          if ($.x !== void 0 && $.y !== void 0) {
            const F = {
              id: g,
              type: "position",
              position: { ...$ }
            };
            D.push(F);
          }
          if (_.width !== void 0 && _.height !== void 0) {
            const B = {
              id: g,
              type: "dimensions",
              resizing: !0,
              setAttributes: p ? p === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: _.width,
                height: _.height
              }
            };
            D.push(B);
          }
          for (const F of T) {
            const B = {
              ...F,
              type: "position"
            };
            D.push(B);
          }
          L(D);
        },
        onEnd: ({ width: _, height: T }) => {
          const L = {
            id: g,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: _,
              height: T
            }
          };
          v.getState().triggerNodeChanges([L]);
        }
      })), k.current.update({
        controlPosition: A,
        boundaries: {
          minWidth: c,
          minHeight: u,
          maxWidth: l,
          maxHeight: d
        },
        keepAspectRatio: f,
        resizeDirection: p,
        onResizeStart: w,
        onResize: m,
        onResizeEnd: x,
        shouldResize: y
      }), () => {
        k.current?.destroy();
      };
  }, [
    A,
    c,
    u,
    l,
    d,
    f,
    w,
    m,
    x,
    y
  ]);
  const M = A.split("-");
  return o.jsx("div", { className: Ce(["react-flow__resize-control", "nodrag", ...M, n, i]), ref: j, style: {
    ...r,
    scale: S,
    ...a && { [N ? "backgroundColor" : "borderColor"]: a }
  }, children: s });
}
je(J0);
function Q0(e, t) {
  return {
    ...t ? { name: t } : {},
    definitionId: e.definitionId,
    state: qn(e.state),
    layout: e.layout
  };
}
function eb(e) {
  return JSON.stringify(
    {
      state: qn(e.state),
      layout: e.layout
    },
    null,
    2
  );
}
function tb(e, t) {
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
      state: er(i.state),
      layout: i.layout ?? t.layout
    }
  };
}
function nb(e, t) {
  const n = (t ?? "workflow").trim().replace(/[^\w.-]+/g, "-") || "workflow", i = new Blob([JSON.stringify(e, null, 2)], { type: "application/json" }), r = URL.createObjectURL(i), s = document.createElement("a");
  s.href = r, s.download = `${n}.json`, document.body.appendChild(s), s.click(), s.remove(), URL.revokeObjectURL(r);
}
function xc({
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
function ib({
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
    () => d ? rf(d) : null,
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
        f ? /* @__PURE__ */ o.jsx(of, { fallback: /* @__PURE__ */ o.jsx(
          xc,
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
          xc,
          {
            document: e,
            readOnly: n,
            minHeight: r,
            ariaLabel: s,
            onChange: c
          }
        ),
        /* @__PURE__ */ o.jsx(rb, { diagnostics: u })
      ]
    }
  );
}
function rb({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ o.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const i = t.severity ?? "info", r = ob(t);
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
function ob(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const sb = { language: "json", displayName: "JSON" };
function ab({ draft: e, onApply: t }) {
  const n = ue(() => eb(e), [e]), [i, r] = K(n), [s, a] = K(n), [c, u] = K(null);
  te(() => {
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
      ib,
      {
        ariaLabel: "Workflow JSON",
        document: { uri: "elsa://workflows/definition.json", language: "json", value: i },
        languageAdapter: sb,
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
const cb = ["Single", "Array", "List", "HashSet"];
function Cd(e) {
  const [t, n] = K(null), [i, r] = K(null);
  te(() => {
    let u = !1;
    return oh(e).then(
      (l) => {
        u || n(l);
      },
      () => {
        u || n([]);
      }
    ), ah(e).then(
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
      const l = Fs(u);
      return {
        value: l,
        label: tl(u.displayName, l),
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
      const d = Fs(l), f = l.defaultEditor?.trim();
      d && u.set(d, f && f.length > 0 ? f : "text");
    }
    return (l) => u.get(l) ?? "text";
  }, [t]);
  return { typeOptions: s, storageOptions: a, editorForAlias: c };
}
function lb(e) {
  return !e || e.length === 0 ? void 0 : (e.find((n) => /(^|\.)String$/i.test(n.value) || n.label.toLowerCase() === "string") ?? e[0]).value;
}
function ub(e, t, n) {
  return {
    add: () => {
      const i = If(n.namePrefix, e.map((r) => st(r, n.nameKeys)));
      t([...e, n.create(i)]);
    },
    update: (i, r) => t(e.map((s, a) => a === i ? n.patch(s, r) : s)),
    remove: (i) => t(e.filter((r, s) => s !== i))
  };
}
function wc({ value: e, options: t, placeholder: n, allowEmpty: i, ariaLabel: r, onChange: s }) {
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
const db = {
  Single: "Single",
  Array: "Array",
  List: "List",
  HashSet: "Hash set"
};
function fb({ value: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ o.jsx("select", { "aria-label": t, value: e, onChange: (i) => n(i.target.value), children: cb.map((i) => /* @__PURE__ */ o.jsx("option", { value: i, children: db[i] }, i)) });
}
function pb(e, t) {
  return t === "" ? !0 : e === "checkbox" ? t === "true" || t === "false" || t === "True" || t === "False" : e === "number" ? Number.isFinite(Number(t.trim())) && t.trim() !== "" : e === "date" ? /^\d{4}-\d{2}-\d{2}$/.test(t) : !0;
}
function hb({ value: e, editor: t, ariaLabel: n, onChange: i }) {
  const r = pb(t, e);
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
function gb({ title: e, addLabel: t, emptyLabel: n, headers: i, isEmpty: r, onAdd: s, children: a }) {
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-properties-section-head", children: [
      /* @__PURE__ */ o.jsx("h3", { children: e }),
      /* @__PURE__ */ o.jsxs("button", { type: "button", className: "wf-properties-add", onClick: s, children: [
        /* @__PURE__ */ o.jsx(Jt, { size: 14 }),
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
function yb({ label: e, onRemove: t }) {
  return /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-properties-remove", "aria-label": e, title: e, onClick: t, children: /* @__PURE__ */ o.jsx(_n, { size: 14 }) }) });
}
function mb({ checked: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ o.jsx("input", { type: "checkbox", "aria-label": t, checked: e, onChange: (i) => n(i.target.checked) });
}
function Is({
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
  const { add: y, update: w, remove: m } = ub(e, h, {
    namePrefix: r,
    nameKeys: s,
    create: (g) => l(g, lb(t)),
    patch: d
  }), x = ["Name", "Type", "Collection", ...f.default ? ["Default"] : [], ...f.storage ? ["Storage"] : [], ...f.required ? ["Required"] : []], b = r.toLowerCase();
  return /* @__PURE__ */ o.jsx(
    gb,
    {
      title: a,
      addLabel: c,
      emptyLabel: u,
      headers: x,
      isEmpty: e.length === 0,
      onAdd: y,
      children: e.map((g, v) => {
        const j = st(g, s), N = es(g), S = st(g, nl), k = S ? p?.get(S) : void 0, A = N.collectionKind === "Single" ? i(N.alias) : "text";
        return /* @__PURE__ */ o.jsxs("tr", { children: [
          /* @__PURE__ */ o.jsxs("td", { children: [
            /* @__PURE__ */ o.jsx("input", { type: "text", "aria-label": `${r} name`, value: j, onChange: (M) => w(v, { name: M.target.value }) }),
            k ? /* @__PURE__ */ o.jsx("span", { className: "wf-properties-warning", role: "note", title: k, children: k }) : null
          ] }),
          /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            wc,
            {
              ariaLabel: `${r} type`,
              value: N.alias,
              options: t,
              placeholder: "Type",
              onChange: (M) => w(v, { type: { alias: M, collectionKind: N.collectionKind } })
            }
          ) }),
          /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            fb,
            {
              ariaLabel: `${r} collection kind`,
              value: N.collectionKind,
              onChange: (M) => w(v, { type: { alias: N.alias, collectionKind: M } })
            }
          ) }),
          f.default ? /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            hb,
            {
              ariaLabel: `${r} default value`,
              value: Tf(g.default),
              editor: A,
              onChange: (M) => w(v, { default: Df(M) })
            }
          ) }) : null,
          f.storage ? /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            wc,
            {
              ariaLabel: `${r} storage driver`,
              value: st(g, Of),
              options: n,
              placeholder: "—",
              allowEmpty: !0,
              onChange: (M) => w(v, { storageDriverType: M || null })
            }
          ) }) : null,
          f.required ? /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            mb,
            {
              ariaLabel: `${r} required`,
              checked: g.isRequired === !0,
              onChange: (M) => w(v, { isRequired: M })
            }
          ) }) : null,
          /* @__PURE__ */ o.jsx(yb, { label: `Remove ${b} ${j || v + 1}`, onRemove: () => m(v) })
        ] }, v);
      })
    }
  );
}
function Ed({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, title: r = "Variables", addLabel: s = "Add variable", emptyLabel: a = "No variables defined.", warnings: c, onChange: u }) {
  return /* @__PURE__ */ o.jsx(
    Is,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Variable",
      nameKeys: Vf,
      title: r,
      addLabel: s,
      emptyLabel: a,
      create: (l, d) => Af({ name: l, alias: d }),
      patch: (l, d) => _f(l, d),
      columns: { default: !0, storage: !0 },
      warnings: c,
      onChange: u
    }
  );
}
function xb({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, onChange: r }) {
  return /* @__PURE__ */ o.jsx(
    Is,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Input",
      nameKeys: ts,
      title: "Inputs",
      addLabel: "Add input",
      emptyLabel: "No inputs defined.",
      create: (s, a) => $f({ name: s, alias: a }),
      patch: (s, a) => Pf(s, a),
      columns: { default: !1, storage: !0, required: !0 },
      onChange: r
    }
  );
}
function wb({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, onChange: r }) {
  return /* @__PURE__ */ o.jsx(
    Is,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Output",
      nameKeys: ts,
      title: "Outputs",
      addLabel: "Add output",
      emptyLabel: "No outputs defined.",
      create: (s, a) => Mf({ name: s, alias: a }),
      patch: (s, a) => Rf(s, a),
      columns: { default: !1, storage: !1 },
      onChange: r
    }
  );
}
function Pi(e) {
  return (e ?? []).filter($n);
}
function vb({ context: e, variables: t, title: n, addLabel: i, emptyLabel: r, warnings: s, onChange: a }) {
  const { typeOptions: c, storageOptions: u, editorForAlias: l } = Cd(e);
  return /* @__PURE__ */ o.jsx(
    Ed,
    {
      items: Pi(t),
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
function bb({ definition: e, definitionId: t, onMetaChange: n }) {
  const i = !!n, [r, s] = K(e?.name ?? ""), [a, c] = K(e?.description ?? "");
  te(() => {
    s(e?.name ?? "");
  }, [e?.name]), te(() => {
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
function Nb({ details: e, draft: t, context: n, onStateChange: i, onDefinitionMetaChange: r }) {
  const { typeOptions: s, storageOptions: a, editorForAlias: c } = Cd(n), u = Pi(t.state.variables), l = Pi(t.state.inputs), d = Pi(t.state.outputs), f = e?.versions ?? [];
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-properties-view", children: [
    /* @__PURE__ */ o.jsx(
      bb,
      {
        definition: e?.definition,
        definitionId: t.definitionId,
        onMetaChange: r
      }
    ),
    /* @__PURE__ */ o.jsx(
      Ed,
      {
        items: u,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, variables: p }))
      }
    ),
    /* @__PURE__ */ o.jsx(
      xb,
      {
        items: l,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, inputs: p }))
      }
    ),
    /* @__PURE__ */ o.jsx(
      wb,
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
const vc = "application/x-elsa-activity-version-id", jb = 6, Sb = 1200, Cb = 250, Eb = [10, 25, 50], kb = 10, bc = "elsa-studio-workflow-palette-width", Nc = "elsa-studio-workflow-inspector-width", jc = "elsa-studio-workflow-palette-collapsed", Sc = "elsa-studio-workflow-inspector-collapsed", kd = "elsa-studio-workflow-side-panel-maximized", Sn = 180, Cn = 460, Ib = 260, Kt = 260, Xt = 560, Ab = 320, Cc = 42, Si = 16, Id = Ue.createContext(null), Ad = Ue.createContext(null);
function _b(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function _d(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Tt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function $t(e, t, n) {
  const i = t.createPrompt(n);
  return i ? (e.dispatchPrompt(i), !0) : !1;
}
function Db(e) {
  if (!e) return null;
  const t = [], n = e.match(/```(?:json)?\s*([\s\S]*?)```/i);
  n && t.push(n[1]);
  const i = e.match(/\{[\s\S]*?\}/);
  i && t.push(i[0]);
  for (const a of t)
    try {
      const c = JSON.parse(a.trim()), u = Ec(c.name, c.description);
      if (u) return u;
    } catch {
    }
  const r = e.match(/name\s*[:-]\s*(.+)/i)?.[1], s = e.match(/description\s*[:-]\s*(.+)/i)?.[1];
  return Ec(r, s);
}
function Ec(e, t) {
  const n = typeof e == "string" ? kc(e) : void 0, i = typeof t == "string" ? kc(t) : void 0;
  return n || i ? { name: n || void 0, description: i || void 0 } : null;
}
function kc(e) {
  return e.trim().replace(/,$/, "").trim().replace(/^["']/, "").replace(/["']$/, "").trim();
}
function Tb(e, t) {
  return e.rootActivityVersionId ?? Dd(t, e.rootKind)?.activityVersionId ?? null;
}
function Dd(e, t) {
  return e.find((n) => $b(n) === t);
}
function $b(e) {
  return e ? Pb(e) ? "flowchart" : Mb(e) ? "sequence" : null : null;
}
function Oo(e) {
  return Nl(e, (t) => t.category).map((t) => ({
    category: t.category,
    activities: t.items.sort((n, i) => Se(n).localeCompare(Se(i)))
  }));
}
function Pb(e) {
  return Se(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function Mb(e) {
  return Se(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function Rb(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function Td(e) {
  return Hb(e.rootActivityType) || e.rootActivityType;
}
function zb(e, t) {
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
function Vb(e, t) {
  return Ic(t) - Ic(e);
}
function Ic(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function $d(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
function Pd(e) {
  const t = e?.workflowExecutionId ?? e?.runId ?? e?.executionId;
  return typeof t == "string" && t.trim() ? t : null;
}
async function Ob(e) {
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
function Hb(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function Wb(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    Ci(t, n.typeName, n), Ci(t, n.name, n), Ci(t, n.displayName, n);
    const i = n.typeName.split(".").filter(Boolean).at(-1);
    Ci(t, i, n);
  }
  return t;
}
function Bb(e, t, n) {
  const i = t.get(e.activityVersionId);
  return n.get(En(i?.activityTypeKey)) ?? n.get(En(Et(i?.activityTypeKey))) ?? n.get(En(i?.displayName)) ?? n.get(En(e.activityVersionId)) ?? null;
}
function Ci(e, t, n) {
  const i = En(t);
  i && !e.has(i) && e.set(i, n);
}
function En(e) {
  return e?.trim().toLowerCase() ?? "";
}
function Ac(e, t, n, i) {
  const r = gr();
  if (!r) return t;
  const s = r.getItem(e);
  if (s == null) return t;
  const a = Number(s);
  return Number.isFinite(a) ? Mi(a, n, i) : t;
}
function _c(e, t) {
  const n = gr();
  if (!n) return t;
  const i = n.getItem(e);
  return i === "true" ? !0 : i === "false" ? !1 : t;
}
function Fb() {
  const e = gr();
  if (!e) return null;
  const t = e.getItem(kd);
  return t === "palette" || t === "inspector" ? t : null;
}
function gr() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function wn(e, t) {
  const n = gr();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function Mi(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function Md(e) {
  switch (Kb(e)) {
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
function Kb(e) {
  return (e ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}
function Xb(e, t) {
  const n = e.state.rootActivity;
  if (!n) return /* @__PURE__ */ new Set();
  const i = t.find((s) => s.activityVersionId === n.activityVersionId);
  if (is(n, i) === "unsupported") return /* @__PURE__ */ new Set([n.nodeId]);
  const r = Qt(n, [], t);
  return new Set(r?.slot.activities.map((s) => s.nodeId) ?? [n.nodeId]);
}
function Dc(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function Tc(e) {
  return `${Se(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function $c(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function qb(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function Rd(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function Yb(e) {
  const t = Rd(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function Ub(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function Le(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function Zb(e) {
  return Vd(Le(e));
}
function Gb(e, t) {
  const n = t?.get(e.activityVersionId);
  return n ? Se(n) : Et(e.activityVersionId) ?? e.activityVersionId;
}
function Jb(e, t) {
  if (e.cardinality === "single") {
    const i = e.activities[0];
    return i ? Gb(i, t) : "Empty — click to choose";
  }
  const n = e.activities.length;
  return `${n} activit${n === 1 ? "y" : "ies"}`;
}
function zd(e, t, n = []) {
  if (!e) return n;
  const i = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: i?.activityTypeKey ?? e.activityVersionId,
    displayName: i ? Se(i) : void 0
  });
  for (const r of Me(e, t))
    for (const s of r.activities) zd(s, t, n);
  return n;
}
function Ld(e, t, n = []) {
  if (!e) return n;
  for (const i of hl(e))
    n.push({ source: i.source, target: i.target, sourcePort: i.sourceHandle ?? void 0, targetPort: i.targetHandle ?? void 0 });
  for (const i of Me(e, t))
    for (const r of i.activities) Ld(r, t, n);
  return n;
}
function kn(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function Qb(e) {
  return `${e.id}-${Vd(JSON.stringify(e.state))}`;
}
function Vd(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function As(e) {
  return e.status.toLowerCase() === "rejected";
}
function eN(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function tN(e, t) {
  const n = e instanceof Error ? e.message : String(e);
  return nN(e, n) ? `Run ${t} was not found.` : n;
}
function nN(e, t) {
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
const Od = { workflowActivity: iN }, Hd = { workflow: oN };
function iN({ data: e, selected: t }) {
  const n = e, i = n.runtime, r = !n.suppressFlowPorts, s = r ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], a = rN(n), u = Ue.useContext(Ad)?.({ activityVersionId: n.activityVersionId, activityTypeKey: n.activityTypeKey }) ?? null;
  return /* @__PURE__ */ o.jsxs(
    "div",
    {
      className: ["wf-node", t ? "selected" : "", i ? "wf-node-runtime" : "", i?.hasBlockingIncident ? "faulted" : "", u ? "wf-node-unavailable" : ""].filter(Boolean).join(" "),
      "data-icon": n.icon ?? "activity",
      children: [
        r && n.acceptsInbound ? /* @__PURE__ */ o.jsx(cn, { type: "target", position: re.Left }) : null,
        u ? /* @__PURE__ */ o.jsx("span", { className: "wf-node-availability", title: `No longer available for new use · ${Pn(u.state)}`, children: /* @__PURE__ */ o.jsx(zi, { size: 13 }) }) : null,
        /* @__PURE__ */ o.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ o.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: ir(n.icon) }),
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
          return /* @__PURE__ */ o.jsxs(Ue.Fragment, { children: [
            /* @__PURE__ */ o.jsx("span", { className: "wf-node-port-label", style: { top: f }, children: l.displayName }),
            /* @__PURE__ */ o.jsx(cn, { type: "source", position: re.Right, id: l.name, style: { top: f } })
          ] }, l.name);
        })
      ]
    }
  );
}
function rN(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((r) => !!r).join(" · ");
}
function oN(e) {
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
  } = e, p = Ue.useContext(Id), [h, y] = K(!1), [w, m, x] = Zi({ sourceX: n, sourceY: i, targetX: r, targetY: s, sourcePosition: a, targetPosition: c }), b = p?.highlightedEdgeId === t;
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsx(
      ti,
      {
        id: t,
        path: w,
        markerEnd: u,
        style: {
          ...l,
          strokeWidth: b ? 2.5 : l?.strokeWidth
        },
        label: d,
        labelX: m,
        labelY: x,
        labelStyle: f,
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1)
      }
    ),
    p ? /* @__PURE__ */ o.jsx(k0, { children: /* @__PURE__ */ o.jsxs(
      "div",
      {
        className: ["wf-edge-actions", h ? "visible" : "", b ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${m}px, ${x}px)` },
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1),
        children: [
          /* @__PURE__ */ o.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (g) => p.requestInsertActivity(t, g.clientX, g.clientY), children: /* @__PURE__ */ o.jsx(Jt, { size: 12 }) }),
          /* @__PURE__ */ o.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => p.deleteEdge(t), children: /* @__PURE__ */ o.jsx(_n, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function sN({ clientX: e, clientY: t, activities: n, onPick: i, onClose: r }) {
  const [s, a] = K(""), [c, u] = K(0), l = ae(null), d = ae(null), f = ue(() => {
    const b = s.trim().toLowerCase(), g = n.filter(Rb);
    return b ? g.filter((v) => Se(v).toLowerCase().includes(b) || v.activityTypeKey.toLowerCase().includes(b) || (v.category ?? "").toLowerCase().includes(b) || (v.description ?? "").toLowerCase().includes(b)) : g;
  }, [n, s]), p = ue(() => Oo(f), [f]), h = ue(() => p.flatMap((b) => b.activities), [p]);
  te(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), te(() => {
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
  }, w = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), m = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let x = -1;
  return /* @__PURE__ */ o.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: w, top: m }, onMouseDown: (b) => b.stopPropagation(), onClick: (b) => b.stopPropagation(), children: [
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
        x += 1;
        const v = x, j = v === c;
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
function Ri({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: i
}) {
  const r = vf(t.map((s) => s.id), n, i);
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
function Pc(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
const aN = "Expressions/UnresolvedVariable";
function cN(e) {
  return String(e.type ?? e.code ?? "");
}
function lN(e) {
  return cN(e) === aN;
}
function uN(e) {
  const t = String(e ?? "").trim().split("/").filter(Boolean), [n, i, ...r] = t, s = n === "$workflow";
  return {
    nodeId: !n || s ? null : n,
    isWorkflowScope: s,
    bag: i === "inputs" || i === "outputs" || i === "variables" ? i : null,
    referenceKey: r.length > 0 ? r.join("/") : null
  };
}
function dN(e) {
  return (e ?? []).filter(lN).map((t) => ({
    error: t,
    path: uN(t.path),
    message: t.message ?? "Variable reference is not visible from this activity's scope."
  }));
}
function fN({ draft: e, onRepair: t }) {
  const n = e.validationErrors;
  if (!n.length)
    return /* @__PURE__ */ o.jsxs("div", { className: "wf-validation ok", children: [
      /* @__PURE__ */ o.jsx(ln, { size: 14 }),
      " No validation errors"
    ] });
  const i = dN(n), r = new Map(i.map((s) => [s.error, s]));
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-validation-summary", children: [
      /* @__PURE__ */ o.jsx(mt, { size: 14 }),
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
          /* @__PURE__ */ o.jsx(uf, { size: 12 }),
          " Repair"
        ] }) : null
      ] }, a);
    }) })
  ] });
}
function pN({
  testRun: e,
  onOpenDetails: t
}) {
  const n = As(e);
  return /* @__PURE__ */ o.jsx("div", { className: "wf-test-run-status", "data-state": n ? "rejected" : "accepted", children: /* @__PURE__ */ o.jsxs(
    "button",
    {
      type: "button",
      className: "wf-test-run-trigger",
      onClick: t,
      children: [
        n ? /* @__PURE__ */ o.jsx(mt, { size: 16 }) : /* @__PURE__ */ o.jsx(ln, { size: 16 }),
        n ? "Test run rejected" : "Test run dispatched"
      ]
    }
  ) });
}
function hN({ testRun: e, onOpenRun: t }) {
  if (!e)
    return /* @__PURE__ */ o.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Run the draft to see Runtime Evidence." }) });
  const n = As(e), i = e.workflowExecutionId;
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
      /* @__PURE__ */ o.jsx(mt, { size: 14 }),
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
        /* @__PURE__ */ o.jsx("dd", { children: Mc(e.activityCount, "activity") })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ o.jsx("dd", { children: Mc(e.incidentCount, "incident") })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ o.jsx("dd", { title: e.expiresAt ? Ve(e.expiresAt) : "None", children: e.expiresAt ? Ve(e.expiresAt) : "None" })
      ] })
    ] })
  ] }) });
}
function Mc(e, t) {
  return typeof e != "number" ? "Available on linked Run" : `${e} ${t}${e === 1 ? "" : "s"}`;
}
function _s({ rows: e = 5 }) {
  return /* @__PURE__ */ o.jsx("div", { className: "wf-grid", "aria-busy": "true", "aria-label": "Loading", children: Array.from({ length: e }).map((t, n) => /* @__PURE__ */ o.jsx("div", { className: "wf-skeleton wf-skeleton-row", style: { width: `${90 - n % 3 * 12}%` } }, n)) });
}
function Ds({ icon: e, title: t, description: n, action: i }) {
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-empty-state", role: "status", children: [
    /* @__PURE__ */ o.jsx("div", { className: "wf-empty-state-icon", "aria-hidden": !0, children: e ?? /* @__PURE__ */ o.jsx(Xn, { size: 22 }) }),
    /* @__PURE__ */ o.jsx("h3", { children: t }),
    n ? /* @__PURE__ */ o.jsx("p", { children: n }) : null,
    i ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty-state-action", children: i }) : null
  ] });
}
function ni({ message: e, title: t = "Something went wrong" }) {
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-error-card", role: "alert", children: [
    /* @__PURE__ */ o.jsx(mt, { size: 18 }),
    /* @__PURE__ */ o.jsxs("div", { className: "wf-error-card-body", children: [
      /* @__PURE__ */ o.jsx("strong", { children: t }),
      /* @__PURE__ */ o.jsx("span", { children: e || "Please try again, or check that the Elsa server is reachable." })
    ] })
  ] });
}
function Wd({ status: e, run: t, compact: n = !1 }) {
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
function Zt({ value: e, ariaLabel: t, copiedLabel: n, onCopied: i, onCopyFailed: r }) {
  if (!e) return null;
  const s = async (a) => {
    a.preventDefault(), a.stopPropagation();
    try {
      await Ob(e), i(n);
    } catch {
      r(n);
    }
  };
  return /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (a) => {
    s(a);
  }, children: /* @__PURE__ */ o.jsx(df, { size: 12 }) });
}
function gN({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: i }) {
  const [r, s] = K("loading"), [a, c] = K(""), [u, l] = K(""), [d, f] = K(null), [p, h] = K([]), y = n?.trim().toLowerCase() ?? "", w = ue(
    () => y ? p.filter((S) => zb(S, y)) : p,
    [y, p]
  ), m = ue(
    () => Array.from(new Set(p.flatMap((S) => [
      S.definitionId,
      S.definitionVersionId,
      S.sourceId
    ]).filter((S) => !!S))).sort((S, k) => S.localeCompare(k)),
    [p]
  ), x = Tt(t, "weaver.workflows.explain-executable"), b = se(async () => {
    s("loading"), c("");
    try {
      h(await vl(e)), s("ready");
    } catch (S) {
      c(S instanceof Error ? S.message : String(S)), s("failed");
    }
  }, [e]);
  te(() => {
    b();
  }, [b]);
  const g = async (S) => {
    l(""), f(null), c("");
    try {
      const k = await wl(e, S.artifactId), A = Pd(k);
      f({ artifactId: S.artifactId, workflowExecutionId: A }), l(`Started ${S.artifactId}`);
    } catch (k) {
      c(k instanceof Error ? k.message : String(k));
    }
  }, v = (S) => {
    x && $t(t, x, S) && (c(""), f(null), l(`Sent ${S.artifactId} to Weaver`));
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
        /* @__PURE__ */ o.jsx(Ji, { size: 14 }),
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
        /* @__PURE__ */ o.jsx(Zo, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    r === "failed" ? /* @__PURE__ */ o.jsx(ni, { message: a }) : null,
    u ? /* @__PURE__ */ o.jsx(Wd, { status: u, run: d }) : null,
    r === "loading" ? /* @__PURE__ */ o.jsx(_s, {}) : null,
    r === "ready" && w.length === 0 ? /* @__PURE__ */ o.jsx(
      Ds,
      {
        icon: /* @__PURE__ */ o.jsx(Gt, { size: 22 }),
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
            /* @__PURE__ */ o.jsx(Zt, { value: S.artifactId, ariaLabel: `Copy artifact ID ${S.artifactId}`, copiedLabel: "artifact ID", onCopied: j, onCopyFailed: N })
          ] }),
          /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
            /* @__PURE__ */ o.jsx("small", { title: S.artifactHash, children: S.artifactHash }),
            /* @__PURE__ */ o.jsx(Zt, { value: S.artifactHash, ariaLabel: `Copy artifact hash ${S.artifactHash}`, copiedLabel: "artifact hash", onCopied: j, onCopyFailed: N })
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line wf-version-cell", children: [
          /* @__PURE__ */ o.jsx("span", { children: S.artifactVersion }),
          /* @__PURE__ */ o.jsx(Zt, { value: S.artifactVersion, ariaLabel: `Copy artifact version ${S.artifactVersion}`, copiedLabel: "artifact version", onCopied: j, onCopyFailed: N })
        ] }),
        /* @__PURE__ */ o.jsx(yN, { executable: S, onCopied: j, onCopyFailed: N }),
        /* @__PURE__ */ o.jsx("span", { children: Td(S) }),
        /* @__PURE__ */ o.jsx("span", { children: Ve(S.publishedAt ?? S.createdAt) }),
        /* @__PURE__ */ o.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
            g(S);
          }, children: [
            /* @__PURE__ */ o.jsx(Gt, { size: 13 }),
            " Run"
          ] }),
          x ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => v(S), children: [
            /* @__PURE__ */ o.jsx(ct, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, S.artifactId))
    ] }) : null
  ] });
}
function yN({ executable: e, onCopied: t, onCopyFailed: n }) {
  const i = e.sourceId || e.definitionVersionId || e.definitionId, r = e.sourceVersion;
  return /* @__PURE__ */ o.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ o.jsx("span", { className: "wf-source-kind", children: $d(e.sourceKind) }),
    i ? /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line", children: [
      /* @__PURE__ */ o.jsx("code", { title: i, children: i }),
      /* @__PURE__ */ o.jsx(Zt, { value: i, ariaLabel: `Copy source ID ${i}`, copiedLabel: "source ID", onCopied: t, onCopyFailed: n })
    ] }) : null,
    r ? /* @__PURE__ */ o.jsxs("small", { children: [
      "Version ",
      r
    ] }) : null
  ] });
}
function mN({ context: e, ai: t, definitionId: n, publishedArtifactId: i }) {
  const [r, s] = K("loading"), [a, c] = K(""), [u, l] = K(""), [d, f] = K(null), [p, h] = K([]), y = Tt(t, "weaver.workflows.explain-executable"), w = se(async () => {
    s("loading"), c("");
    try {
      const j = await vl(e);
      h(j.filter((N) => Lb(N, n)).sort(Vb)), s("ready");
    } catch (j) {
      c(j instanceof Error ? j.message : String(j)), h([]), s("failed");
    }
  }, [e, n]);
  te(() => {
    w();
  }, [w, i]);
  const m = async (j) => {
    l(""), f(null), c("");
    try {
      const N = await wl(e, j.artifactId);
      f({ artifactId: j.artifactId, workflowExecutionId: Pd(N) }), l(`Started ${j.artifactId}`);
    } catch (N) {
      c(N instanceof Error ? N.message : String(N));
    }
  }, x = (j) => {
    y && $t(t, y, j) && (c(""), f(null), l(`Sent ${j.artifactId} to Weaver`));
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
        /* @__PURE__ */ o.jsx(Go, { size: 13 }),
        " Refresh"
      ] }),
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: b, children: "Open list" })
    ] }),
    r === "failed" ? /* @__PURE__ */ o.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ o.jsx(mt, { size: 14 }),
      " ",
      a
    ] }) : null,
    u ? /* @__PURE__ */ o.jsx(Wd, { status: u, run: d, compact: !0 }) : null,
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
          /* @__PURE__ */ o.jsx(Zt, { value: j.artifactId, ariaLabel: `Copy artifact ID ${j.artifactId}`, copiedLabel: "artifact ID", onCopied: g, onCopyFailed: v })
        ] }),
        /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ o.jsx("code", { title: j.artifactHash, children: j.artifactHash }),
          /* @__PURE__ */ o.jsx(Zt, { value: j.artifactHash, ariaLabel: `Copy artifact hash ${j.artifactHash}`, copiedLabel: "artifact hash", onCopied: g, onCopyFailed: v })
        ] })
      ] }),
      /* @__PURE__ */ o.jsxs("dl", { children: [
        /* @__PURE__ */ o.jsxs("div", { children: [
          /* @__PURE__ */ o.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ o.jsxs("dd", { children: [
            $d(j.sourceKind),
            " ",
            j.sourceVersion ? `v${j.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("div", { children: [
          /* @__PURE__ */ o.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ o.jsx("dd", { children: Td(j) })
        ] })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
          m(j);
        }, children: [
          /* @__PURE__ */ o.jsx(Gt, { size: 13 }),
          " Run"
        ] }),
        y ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => x(j), children: [
          /* @__PURE__ */ o.jsx(ct, { size: 13 }),
          " Explain"
        ] }) : null
      ] })
    ] }, j.artifactId)) }) : null
  ] });
}
function Bd() {
  const [e, t] = K(() => Ac(bc, Ib, Sn, Cn)), [n, i] = K(() => Ac(Nc, Ab, Kt, Xt)), [r, s] = K(() => _c(jc, !1)), [a, c] = K(() => _c(Sc, !1)), [u, l] = K(Fb);
  te(() => {
    wn(bc, String(e));
  }, [e]), te(() => {
    wn(Nc, String(n));
  }, [n]), te(() => {
    wn(jc, String(r));
  }, [r]), te(() => {
    wn(Sc, String(a));
  }, [a]), te(() => {
    wn(kd, u);
  }, [u]), te(() => {
    if (!u) return;
    const g = (v) => {
      v.key === "Escape" && l(null);
    };
    return window.addEventListener("keydown", g), () => window.removeEventListener("keydown", g);
  }, [u]);
  const d = se((g) => {
    l((v) => v === g ? null : v), g === "palette" ? s((v) => !v) : c((v) => !v);
  }, []), f = se((g) => {
    g === "palette" ? s(!1) : c(!1), l((v) => v === g ? null : g);
  }, []), p = se((g, v) => {
    l(null), g === "palette" ? (s(!1), t((j) => Mi(j + v, Sn, Cn))) : (c(!1), i((j) => Mi(j + v, Kt, Xt)));
  }, []), h = se((g, v) => {
    v.preventDefault(), l(null), g === "palette" ? s(!1) : c(!1);
    const j = v.clientX, N = g === "palette" ? e : n, S = g === "palette" ? Sn : Kt, k = g === "palette" ? Cn : Xt;
    document.body.classList.add("wf-side-panel-resizing");
    const A = (_) => {
      const T = g === "palette" ? _.clientX - j : j - _.clientX, L = Mi(N + T, S, k);
      g === "palette" ? t(L) : i(L);
    }, M = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", A), window.removeEventListener("pointerup", M), window.removeEventListener("pointercancel", M);
    };
    window.addEventListener("pointermove", A), window.addEventListener("pointerup", M), window.addEventListener("pointercancel", M);
  }, [n, e]), y = se((g, v) => {
    v.key === "ArrowLeft" ? (v.preventDefault(), p(g, g === "palette" ? -Si : Si)) : v.key === "ArrowRight" ? (v.preventDefault(), p(g, g === "palette" ? Si : -Si)) : v.key === "Home" ? (v.preventDefault(), g === "palette" ? t(Sn) : i(Kt)) : v.key === "End" && (v.preventDefault(), g === "palette" ? t(Cn) : i(Xt));
  }, [p]), w = !r && u !== "inspector", m = !a && u !== "palette", x = [
    "wf-editor-body",
    r ? "palette-collapsed" : "",
    a ? "inspector-collapsed" : "",
    u === "palette" ? "palette-maximized" : "",
    u === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), b = {
    "--wf-palette-width": `${r ? Cc : e}px`,
    "--wf-inspector-width": `${a ? Cc : n}px`
  };
  return {
    paletteWidth: e,
    inspectorWidth: n,
    paletteCollapsed: r,
    inspectorCollapsed: a,
    maximizedSidePanel: u,
    setInspectorCollapsed: c,
    paletteExpanded: w,
    inspectorExpanded: m,
    editorBodyClassName: x,
    editorBodyStyle: b,
    toggleSidePanelCollapsed: d,
    toggleSidePanelMaximized: f,
    startSidePanelResize: h,
    handleSidePanelResizeKeyDown: y
  };
}
const xN = 50;
function Rc() {
  return { past: [], future: [] };
}
function wN(e) {
  return e.past.length > 0;
}
function vN(e) {
  return e.future.length > 0;
}
function zc(e, t, n = xN) {
  const i = [...e.past, t];
  return i.length > n && i.splice(0, i.length - n), { past: i, future: [] };
}
function bN(e, t) {
  if (e.past.length === 0) return null;
  const n = e.past.slice(), i = n.pop();
  return { history: { past: n, future: [...e.future, t] }, snapshot: i };
}
function NN(e, t) {
  if (e.future.length === 0) return null;
  const n = e.future.slice(), i = n.pop();
  return { history: { past: [...e.past, t], future: n }, snapshot: i };
}
function jN({ draft: e, restoreDraft: t }) {
  const n = ae(Rc()), i = ae(null), r = ae(""), s = ae(!1), [a, c] = K(0), u = se((w) => {
    n.current = Rc(), i.current = w ? kn(w) : null, r.current = w ? Le(w) : "", s.current = !1, c(0);
  }, []);
  te(() => {
    if (!e) return;
    if (s.current) {
      s.current = !1;
      return;
    }
    const w = Le(e);
    if (w === r.current) return;
    const m = window.setTimeout(() => {
      const x = i.current;
      x && (n.current = zc(n.current, x), c((b) => b + 1)), i.current = kn(e), r.current = w;
    }, Cb);
    return () => window.clearTimeout(m);
  }, [e]);
  const l = se(() => {
    if (!e) return;
    const w = Le(e);
    if (w === r.current) return;
    const m = i.current;
    m && (n.current = zc(n.current, m)), i.current = kn(e), r.current = w;
  }, [e]), d = se((w) => {
    s.current = !0, i.current = kn(w), r.current = Le(w), t(w), c((m) => m + 1);
  }, [t]), f = se(() => {
    if (!e) return;
    l();
    const w = bN(n.current, e);
    w && (n.current = w.history, d(w.snapshot));
  }, [e, l, d]), p = se(() => {
    if (!e) return;
    l();
    const w = NN(n.current, e);
    w && (n.current = w.history, d(w.snapshot));
  }, [e, l, d]), { canUndoNow: h, canRedoNow: y } = ue(() => {
    const w = !!e && !!i.current && Le(e) !== r.current;
    return {
      canUndoNow: wN(n.current) || w,
      canRedoNow: vN(n.current) && !w
    };
  }, [e, a]);
  return { resetHistory: u, undo: f, redo: p, canUndoNow: h, canRedoNow: y };
}
const SN = {
  draft: null,
  frames: [],
  selectedNodeId: null,
  testRun: null,
  publishedArtifactId: null
};
function CN(e, t) {
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
function EN() {
  const [e, t] = sf(CN, SN), n = ue(() => ({
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
const kN = 320, IN = 140;
function AN(e, t, n) {
  return n === "sequence" ? _N(e) : DN(e, t);
}
function _N(e) {
  const t = /* @__PURE__ */ new Map();
  return e.forEach((n, i) => {
    t.set(n.id, { x: i * 280, y: 0 });
  }), t;
}
function DN(e, t) {
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
      n.set(p, { x: d * kN, y: h * IN });
    });
  return n;
}
function TN(e, t, n, i, r) {
  if (!e) return { kind: "becomeRoot" };
  const s = Qt(e, t, r);
  if (!s)
    return t.length > 0 ? { kind: "staleFrames" } : Me(n, i)[0] ? { kind: "wrapRoot" } : { kind: "leafError" };
  const a = s.slot.cardinality === "single" ? s.slot.activities.find((c) => c.nodeId !== n.nodeId) ?? null : null;
  return { kind: "addToSlot", slot: s.slot, replacedActivity: a };
}
function $N({
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
  const [m, x] = K([]), [b, g] = K([]), [v, j] = K(null), [N, S] = K(null), [k, A] = K(null), M = ae(null), _ = ae(null), T = ae(null), L = ae(null), C = ae(!1);
  te(() => {
    if (!n) {
      x([]), g([]);
      return;
    }
    const z = a ? So(n, r, e?.layout ?? []) : t ? ll(t, r, e?.layout ?? []) : { nodes: [], edges: [] };
    x(z.nodes), g(z.edges);
  }, [r, e?.layout, a, t, n]);
  const I = se((z, O, Y) => Y ? [
    ...z.filter((Z) => Z.nodeId !== O),
    { nodeId: O, x: Math.round(Y.x), y: Math.round(Y.y) }
  ] : z, []), E = se((z, O) => {
    if (e?.state.rootActivity && a)
      return;
    const Y = Co(z, Tc(z)), Z = TN(e?.state.rootActivity, i, Y, z, s);
    if (Z.kind === "becomeRoot") {
      f(
        ({ draft: le }) => le ? { ...le, state: { ...le.state, rootActivity: Y } } : null,
        Y.nodeId
      );
      return;
    }
    if (Z.kind === "leafError") {
      y(""), w("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
      return;
    }
    if (Z.kind === "staleFrames") {
      y(""), w("This slot could not be resolved — returning to the workflow root."), h();
      return;
    }
    if (Z.kind === "wrapRoot") {
      f(({ draft: le }) => {
        const xe = le?.state.rootActivity;
        return xe ? {
          ...le,
          layout: I(le.layout, xe.nodeId, O),
          state: { ...le.state, rootActivity: Ys(Y, [], [xe], z) }
        } : null;
      }, e?.state.rootActivity?.nodeId ?? null), w(""), y(`Wrapped root in ${Se(z)}`);
      return;
    }
    f(({ draft: le, frames: xe }) => {
      if (!le?.state.rootActivity) return null;
      const ye = Qt(le.state.rootActivity, xe, s);
      if (!ye) return null;
      const Ie = ye.slot.cardinality === "single" ? [Y] : [...ye.slot.activities, Y], Ae = Ys(le.state.rootActivity, xe, Ie, s);
      return {
        ...le,
        layout: I(le.layout, Y.nodeId, O),
        state: { ...le.state, rootActivity: Ae }
      };
    }, Y.nodeId), Z.replacedActivity && (w(""), y(`Replaced ${Z.slot.label} content`));
  }, [s, e?.state.rootActivity, i, a, f, h, I, w, y]), D = se((z, O) => {
    const Y = Co(z, Tc(z)), Z = {
      id: Y.nodeId,
      type: "workflowActivity",
      position: O,
      selected: !0,
      data: {
        label: Se(z),
        activityVersionId: z.activityVersionId,
        activityTypeKey: z.activityTypeKey,
        category: z.category,
        executionType: z.executionType,
        icon: dn(z),
        childSlots: Me(Y, z),
        acceptsInbound: String(z.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: gl(Y, z)
      }
    };
    return { activityNode: Y, node: Z };
  }, []), $ = se((z, O, Y = []) => {
    a || d(({ draft: Z, frames: le }) => {
      if (!Z) return null;
      const xe = lp(Z.layout, z), ye = Z.state.rootActivity;
      if (!ye) return { ...Z, layout: xe };
      const Ie = Qt(ye, le, s);
      if (!Ie) return { ...Z, layout: xe };
      const Ae = ap(Ie, z, O, Y), Fe = Ie.slot.mode === "flowchart" ? cp(Ae, O) : Ae;
      return {
        ...Z,
        layout: xe,
        state: {
          ...Z.state,
          rootActivity: sp(ye, le, Fe, s)
        }
      };
    });
  }, [s, a, d]), P = se((z, O) => {
    if (!M.current) return null;
    const Y = M.current.getBoundingClientRect();
    return v ? v.screenToFlowPosition({ x: z, y: O }) : {
      x: z - Y.left,
      y: O - Y.top
    };
  }, [v]), F = se((z, O) => document.elementFromPoint(z, O)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), B = se((z, O, Y) => {
    const Z = m.find((me) => me.id === O.source), le = m.find((me) => me.id === O.target), xe = Z && le ? qb(Z, le) : Z ? $c(Z) : Y, ye = D(z, xe), Ae = [...m.map((me) => me.selected ? { ...me, selected: !1 } : me), ye.node], Fe = mp(b, O, ye.node.id);
    x(Ae), g(Fe), p(ye.node.id), $(Ae, Fe, [ye.activityNode]);
  }, [$, D, b, m, p]), W = se((z, O, Y) => {
    if (!u || !M.current) return !1;
    const Z = M.current.getBoundingClientRect();
    if (!(O >= Z.left && O <= Z.right && Y >= Z.top && Y <= Z.bottom)) return !1;
    const xe = P(O, Y);
    if (!xe) return !1;
    if (c) {
      const ye = F(O, Y), Ie = ye ? b.find((Ae) => Ae.id === ye) : void 0;
      if (Ie)
        return B(z, Ie, xe), !0;
    }
    return E(z, xe), !0;
  }, [E, u, b, F, c, B, P]);
  te(() => {
    const z = (Y) => {
      const Z = T.current;
      if (!Z) return;
      Math.hypot(Y.clientX - Z.startX, Y.clientY - Z.startY) >= jb && (Z.dragging = !0);
    }, O = (Y) => {
      const Z = T.current;
      if (T.current = null, !Z?.dragging || !M.current || L.current) return;
      const le = M.current.getBoundingClientRect();
      Y.clientX >= le.left && Y.clientX <= le.right && Y.clientY >= le.top && Y.clientY <= le.bottom && (C.current = !0, window.setTimeout(() => {
        C.current = !1;
      }, 0), W(Z.activity, Y.clientX, Y.clientY));
    };
    return window.addEventListener("pointermove", z), window.addEventListener("pointerup", O), window.addEventListener("pointercancel", O), () => {
      window.removeEventListener("pointermove", z), window.removeEventListener("pointerup", O), window.removeEventListener("pointercancel", O);
    };
  }, [v, W]);
  const G = (z, O) => {
    L.current = { activityVersionId: O.activityVersionId, handledDrop: !1 }, z.dataTransfer.setData(vc, O.activityVersionId), z.dataTransfer.setData("text/plain", O.activityVersionId), z.dataTransfer.effectAllowed = "copy";
  }, H = (z, O) => {
    const Y = L.current;
    L.current = null, !Y?.handledDrop && (z.clientX === 0 && z.clientY === 0 || W(O, z.clientX, z.clientY) && (C.current = !0, window.setTimeout(() => {
      C.current = !1;
    }, 0)));
  }, q = (z, O) => {
    z.button === 0 && (T.current = {
      activity: O,
      startX: z.clientX,
      startY: z.clientY,
      dragging: !1
    });
  }, Q = (z) => {
    C.current || u && E(z);
  }, U = (z) => {
    if (!u) {
      z.dataTransfer.dropEffect = "none";
      return;
    }
    if (z.preventDefault(), z.dataTransfer.dropEffect = "copy", !c) return;
    const O = F(z.clientX, z.clientY);
    A(O);
  }, R = (z) => {
    if (!M.current) return;
    const O = z.relatedTarget;
    O && M.current.contains(O) || A(null);
  }, X = (z) => {
    z.preventDefault(), A(null);
    const O = z.dataTransfer.getData(vc) || z.dataTransfer.getData("text/plain");
    if (!O || (z.stopPropagation(), L.current?.activityVersionId === O && (L.current.handledDrop = !0), !u)) return;
    const Y = s.get(O);
    Y && W(Y, z.clientX, z.clientY);
  }, ce = (z) => {
    if (!u) return;
    if (z) {
      S({ kind: "fromEmpty", clientX: z.clientX, clientY: z.clientY });
      return;
    }
    const O = M.current?.getBoundingClientRect();
    O && S({
      kind: "fromEmpty",
      clientX: O.left + O.width / 2,
      clientY: O.top + O.height / 2
    });
  }, oe = (z) => {
    const O = a ? z.filter((Y) => Y.type === "select") : z;
    O.length !== 0 && x((Y) => Fu(O, Y));
  }, ee = (z) => {
    a || g((O) => Ku(z, O));
  }, ie = (z) => !z.source || !z.target || z.source === z.target || !c ? !1 : !z.targetHandle, de = (z) => {
    if (!e?.state.rootActivity || !t || !c || !ie(z)) return;
    const O = Li(z.source, z.target, z.sourceHandle ?? "Done", z.targetHandle ?? void 0), Y = qu(O, b);
    g(Y), $(m, Y);
  }, V = () => {
    $(m, b);
  }, ne = !a && m.length > 0, he = se(() => {
    if (a || m.length === 0) return;
    const z = t?.slot.mode === "sequence" ? "sequence" : "flowchart", O = AN(m, b, z), Y = m.map((Z) => {
      const le = O.get(Z.id);
      return le ? { ...Z, position: le } : Z;
    });
    x(Y), $(Y, b), window.requestAnimationFrame(() => v?.fitView({ padding: 0.2 })), y("Rearranged the canvas.");
  }, [b, m, t, a, $, v, y]), ge = (z, O) => {
    if (!O.nodeId || O.handleType === "target") {
      _.current = null;
      return;
    }
    _.current = {
      nodeId: O.nodeId,
      handleId: O.handleId ?? null
    };
  }, Ee = (z, O) => {
    const Y = Ub(_.current, O);
    if (_.current = null, !Y || !c || O.toNode || O.toHandle || Yb(z)) return;
    const Z = Rd(z);
    S({
      kind: "fromPort",
      sourceNodeId: Y.nodeId,
      sourceHandleId: Y.handleId,
      clientX: Z.x,
      clientY: Z.y
    });
  }, ke = (z, O) => {
    if (!c || !ie(O)) return;
    const Y = rv(z, {
      ...O,
      sourceHandle: O.sourceHandle ?? "Done",
      // Connection.targetHandle is string | null; null and undefined are indistinguishable downstream
      // (every consumer checks truthiness), so keep the type-correct null.
      targetHandle: O.targetHandle ?? null
    }, b, { shouldReplaceId: !1 });
    g(Y), $(m, Y);
  }, De = (z) => {
    if (a || z.length === 0) return;
    const O = new Set(z.map((le) => le.id)), Y = m.filter((le) => !O.has(le.id)), Z = b.filter((le) => !O.has(le.source) && !O.has(le.target));
    x(Y), g(Z), l && O.has(l) && p(null), $(Y, Z);
  }, it = (z) => {
    if (a || z.length === 0) return;
    const O = new Set(z.map((Z) => Z.id)), Y = b.filter((Z) => !O.has(Z.id));
    g(Y), $(m, Y);
  }, We = se((z) => {
    if (a) return;
    const O = b.filter((Y) => Y.id !== z);
    g(O), $(m, O);
  }, [$, b, a, m]), Be = se((z, O, Y) => {
    c && S({ kind: "spliceEdge", edgeId: z, clientX: O, clientY: Y });
  }, [c]), Te = (z) => {
    const O = N;
    if (!O) return;
    S(null);
    const Y = P(O.clientX, O.clientY) ?? { x: 0, y: 0 };
    if (O.kind === "fromEmpty") {
      const le = D(z, Y), ye = [...m.map((Ie) => Ie.selected ? { ...Ie, selected: !1 } : Ie), le.node];
      x(ye), p(le.node.id), $(ye, b, [le.activityNode]);
      return;
    }
    if (O.kind === "fromPort") {
      const le = m.find((me) => me.id === O.sourceNodeId), xe = le ? $c(le) : Y, ye = D(z, xe), Ae = [...m.map((me) => me.selected ? { ...me, selected: !1 } : me), ye.node], Fe = [...b, Li(O.sourceNodeId, ye.node.id, O.sourceHandleId ?? "Done")];
      x(Ae), g(Fe), p(ye.node.id), $(Ae, Fe, [ye.activityNode]);
      return;
    }
    const Z = b.find((le) => le.id === O.edgeId);
    Z && B(z, Z, Y);
  }, Re = ue(() => ({
    highlightedEdgeId: k,
    deleteEdge: We,
    requestInsertActivity: Be
  }), [We, k, Be]);
  return {
    nodes: m,
    edges: b,
    canvasRef: M,
    setReactFlowInstance: j,
    connectMenu: N,
    setConnectMenu: S,
    edgeActions: Re,
    onNodesChange: oe,
    onEdgesChange: ee,
    onNodesDelete: De,
    onEdgesDelete: it,
    isValidConnection: ie,
    onConnect: de,
    onConnectStart: ge,
    onConnectEnd: Ee,
    onReconnect: ke,
    commitLayout: V,
    canAutoLayout: ne,
    autoLayout: he,
    onCanvasDragOver: U,
    onCanvasDragLeave: R,
    onCanvasDrop: X,
    openEmptyConnectMenu: ce,
    onConnectMenuPick: Te,
    addActivity: E,
    onPaletteClick: Q,
    onPaletteDragStart: G,
    onPaletteDragEnd: H,
    onPalettePointerDown: q
  };
}
const Lc = "elsa-studio:apply-workflow-graph-operation-batch", Vc = "elsa-studio:undo-workflow-graph-operation-batch", PN = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function MN(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const i = BN(e), r = Kd(i.state.rootActivity), s = /* @__PURE__ */ new Map(), a = /* @__PURE__ */ new Map(), c = [];
  for (const u of t.operations) {
    const l = WN(u.kind), d = u.parameters ?? {};
    if (l === "add-activity") {
      const f = $e(d.activityId) ?? u.temporaryReferences?.[0], p = HN(f ?? $e(d.displayName) ?? $e(d.activityType) ?? "weaver-activity", r), h = RN(u, p, n);
      a.set(p, h), c.push(p), f && s.set(f, p), i.state.rootActivity && zN(i.state.rootActivity, h);
      const y = yt(d.position) ? Ho(d.position, { x: 280, y: 160 }) : null;
      y && (i.layout = Oc(i.layout, p, y));
      continue;
    }
    if (l === "set-root") {
      const f = vo(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown root activity.");
      i.state.rootActivity = f;
      continue;
    }
    if (l === "set-designer-position") {
      const f = Pt(d.activityId, s);
      if (!f || !Ts(i.state.rootActivity, f)) throw new Error("Weaver batch referenced an unknown activity position.");
      i.layout = Oc(i.layout, f, Ho(d, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const f = vo(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity property target.");
      ON(f, $e(d.propertyName) ?? "Value", d.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const f = vo(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity update target.");
      const p = yt(d.patch) ? d.patch : d;
      Object.assign(f, p);
      continue;
    }
    if (l === "remove-activity") {
      const f = Pt(d.activityId, s);
      if (!f) throw new Error("Weaver batch referenced an unknown activity remove target.");
      i.state.rootActivity = Fd(i.state.rootActivity, f), i.layout = i.layout.filter((p) => p.nodeId !== f);
      continue;
    }
    if (l === "connect-activities") {
      LN(i, d, s);
      continue;
    }
    if (l === "disconnect-activities") {
      VN(i, d, s);
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
function RN(e, t, n) {
  const i = e.parameters ?? {}, r = $e(i.activityVersionId) ?? $e(i.activityType) ?? "Elsa.Workflows.Activity", s = n.find((a) => a.activityVersionId === r || a.activityTypeKey === r || a.displayName === $e(i.displayName));
  return s ? Co(s, t) : {
    nodeId: t,
    // catalogItem is provably undefined here (the truthy branch returned above), so use the id directly.
    activityVersionId: r,
    inputs: [],
    outputs: [],
    ...$e(i.displayName) ? { displayName: $e(i.displayName) } : {},
    designer: { position: Ho(i.position, { x: 280, y: 160 }) }
  };
}
function zN(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = $s(e);
  n && !n.some((i) => i.nodeId === t.nodeId) && n.push(t);
}
function LN(e, t, n) {
  const i = e.state.rootActivity;
  if (!i?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const r = Pt(t.sourceActivityId ?? t.sourceId ?? t.from, n), s = Pt(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!r || !s) throw new Error("Weaver batch connection is missing source or target activity.");
  const a = i.structure.payload, c = Array.isArray(a.connections) ? a.connections : [], u = $e(t.connectionId) ?? `flow-${r}-${s}`;
  a.connections = [
    ...c.filter((l) => !yt(l) || l.id !== u),
    {
      id: u,
      source: { nodeId: r, port: $e(t.outcome) ?? $e(t.sourcePort) ?? "Done" },
      target: { nodeId: s }
    }
  ];
}
function VN(e, t, n) {
  const i = e.state.rootActivity, r = i?.structure?.payload.connections;
  if (!Array.isArray(r)) return;
  const s = $e(t.connectionId), a = Pt(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = Pt(t.targetActivityId ?? t.targetId ?? t.to, n);
  i.structure.payload.connections = r.filter((u) => {
    if (!yt(u)) return !0;
    if (s && u.id === s) return !1;
    const l = yt(u.source) ? u.source.nodeId : void 0, d = yt(u.target) ? u.target.nodeId : void 0;
    return l !== a || d !== c;
  });
}
function ON(e, t, n) {
  const i = yt(n);
  e[Il(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: i ? "Object" : "Literal", value: n }
  };
}
function vo(e, t, n, i) {
  const r = Pt(t, n);
  return r ? Ts(e.state.rootActivity, r) ?? i.get(r) ?? null : null;
}
function Pt(e, t) {
  const n = $e(e);
  return n ? t.get(n) ?? n : null;
}
function Ts(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of Xd(e)) {
    const i = Ts(n, t);
    if (i) return i;
  }
  return null;
}
function Fd(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = $s(e);
  if (n) {
    const i = n.map((r) => Fd(r, t)).filter((r) => !!r);
    n.splice(0, n.length, ...i);
  }
  return e;
}
function Kd(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of Xd(e)) Kd(n, t);
  return t;
}
function Xd(e) {
  return $s(e) ?? [];
}
function $s(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function Oc(e, t, n) {
  return [
    ...e.filter((i) => i.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function Ho(e, t) {
  const n = yt(e) ? e : {}, i = Number(n.x), r = Number(n.y);
  return {
    x: Number.isFinite(i) ? Math.max(40, Math.round(i)) : t.x,
    y: Number.isFinite(r) ? Math.max(40, Math.round(r)) : t.y
  };
}
function HN(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let i = n, r = 2;
  for (; t.has(i); )
    i = `${n}-${r}`, r += 1;
  return t.add(i), i;
}
function WN(e) {
  return typeof e == "number" ? PN[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function $e(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function BN(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function yt(e) {
  return typeof e == "object" && e !== null;
}
function FN({
  draft: e,
  details: t,
  catalog: n,
  replaceDraftByBatch: i,
  setStatus: r,
  setError: s
}) {
  const a = ae(/* @__PURE__ */ new Map());
  te(() => {
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
        const p = kn(e), h = MN(e, d.batch, n), y = `weaver-batch-${Date.now()}`;
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
    return window.addEventListener(Lc, c), window.addEventListener(Vc, u), () => {
      window.removeEventListener(Lc, c), window.removeEventListener(Vc, u);
    };
  }, [n, t, e, i, r, s]);
}
function KN({ context: e, draft: t, autosaveEnabledByDefault: n = !0, editDraft: i, setStatus: r, setError: s }) {
  const [a, c] = K(n), u = ae(""), l = ae(0), d = ae(Promise.resolve()), f = se((h) => {
    u.current = h ? Le(h) : "";
  }, []), p = se(async (h, y) => {
    const w = async () => {
      const x = ++l.current, b = Le(h);
      s("");
      try {
        const g = await qp(e, h), v = Le(g);
        return u.current = v, i(({ draft: j }) => !j || j.id !== g.id ? null : Le(j) === b ? g : { ...j, validationErrors: g.validationErrors }), x === l.current && r(y), g;
      } catch (g) {
        throw x === l.current && (r(""), s(g instanceof Error ? g.message : String(g))), g;
      }
    }, m = d.current.then(w, w);
    return d.current = m.catch(() => {
    }), m;
  }, [e, i, r, s]);
  return te(() => {
    if (!a || !t || Le(t) === u.current) return;
    r("Autosaving...");
    const y = window.setTimeout(() => {
      p(t, "Autosaved").catch(() => {
      });
    }, Sb);
    return () => window.clearTimeout(y);
  }, [a, t, p, r]), { saveDraft: p, autosaveEnabled: a, setAutosaveEnabled: c, markSaved: f };
}
function XN({ context: e, definitionId: t, resetHistory: n, loadDraft: i, markSaved: r, setError: s }) {
  const [a, c] = K(null), [u, l] = K([]), [d, f] = K([]), [p, h] = K(null), [y, w] = K(Ii), [m, x] = K("loading"), b = se(async () => {
    s(""), x("loading");
    const [g, v, j, N, S] = await Promise.all([
      Lp(e, t),
      tr(e),
      ih(e).then(
        (A) => ({ ok: !0, descriptors: A }),
        () => ({ ok: !1, descriptors: [] })
      ),
      rh(e).then(
        (A) => ({ ok: !0, descriptors: A }),
        () => ({ ok: !1, descriptors: Ii })
      ),
      // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
      bl(e).then(
        (A) => A,
        () => null
      )
    ]), k = g.draft ?? null;
    c(g), r(k), n(k), i(k), l(v.activities ?? []), f(j.descriptors), h(S), w(N.descriptors.length > 0 ? N.descriptors : Ii), x(j.ok ? "ready" : "failed");
  }, [e, t, n, i, r, s]);
  return te(() => {
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
function qN({ context: e, details: t, setDetails: n, setStatus: i }) {
  const r = ae(null), s = ae(null), a = ae({});
  te(() => {
    r.current = t;
  }, [t]);
  const c = se(() => {
    s.current !== null && (window.clearTimeout(s.current), s.current = null);
    const l = a.current;
    a.current = {};
    const d = r.current?.definition;
    !d || l.name === void 0 && l.description === void 0 || Xp(e, d.id, {
      name: l.name ?? d.name,
      description: l.description ?? d.description ?? null
    }).then((f) => n((p) => p && p.definition.id === f.definition.id ? { ...p, definition: f.definition } : p)).catch(() => i("Couldn't save name/description."));
  }, [e, n, i]), u = se((l) => {
    n((d) => d && { ...d, definition: { ...d.definition, ...l } }), a.current = { ...a.current, ...l }, s.current !== null && window.clearTimeout(s.current), s.current = window.setTimeout(c, 800);
  }, [c, n]);
  return te(() => () => {
    c();
  }, [c]), { updateDefinitionMeta: u };
}
function YN({
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
  const y = se(() => {
    if (!t) return;
    const b = n?.definition.name;
    nb(Q0(t, b), b), d("Exported workflow as JSON.");
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
  }, [t, i, r, l, d]), m = se(async () => {
    if (!(!t || i)) {
      l("promoting"), d("Saving...");
      try {
        await r(t, "Saved"), d("Promoting...");
        const b = await Yp(e, t.id), g = await Up(e, b.versionId);
        u(g.artifactId), d(`Published ${g.artifactVersion}`), await s();
      } catch (b) {
        d(""), f(b instanceof Error ? b.message : String(b));
      } finally {
        l("idle");
      }
    }
  }, [t, i, e, r, s, u, l, d, f]), x = se(async () => {
    if (!t?.state.rootActivity || i) return;
    const b = t, g = Le(b);
    c(), d("Preparing test run...");
    try {
      l("testRunPreparing"), d("Preparing test run...");
      const v = Qb(b);
      l("testRunStarting"), d("Starting test run...");
      const j = await Zp(e, {
        definitionId: b.definitionId,
        snapshotId: v,
        state: b.state
      });
      a({ draftSignature: g, view: j }), p("runtime"), h(!1), d(As(j) ? "Test run rejected" : "Test run dispatched");
    } catch (v) {
      d(""), f(v instanceof Error ? v.message : String(v));
    } finally {
      l("idle");
    }
  }, [t, i, e, c, a, p, h, l, d, f]);
  return { exportJson: y, save: w, promoteAndPublish: m, run: x };
}
function UN({
  context: e,
  draft: t,
  frames: n,
  selectedNodeId: i,
  catalog: r,
  activityDescriptors: s,
  availabilityDiagnostics: a
}) {
  const c = t?.state.rootActivity ?? null, u = ue(() => new Map(r.map((S) => [S.activityVersionId, S])), [r]), l = se(
    (S) => Eh([S.activityVersionId, S.activityTypeKey], a),
    [a]
  ), d = ue(() => Wb(s), [s]), f = ue(() => ol(c, n, u), [c, n, u]), p = is(f, f ? u.get(f.activityVersionId) : void 0), h = !!f && p === "unsupported", y = ue(() => h ? null : Qt(c, n, u), [c, n, u, h]), w = ue(() => h && f?.nodeId === i ? f : y?.slot.activities.find((S) => S.nodeId === i) ?? null, [h, y, f, i]), m = ue(
    () => w ? Bb(w, u, d) : null,
    [u, d, w]
  ), x = ue(
    () => w ? l({ activityVersionId: w.activityVersionId, activityTypeKey: u.get(w.activityVersionId)?.activityTypeKey }) : null,
    [l, u, w]
  ), b = w ? Me(w, u) : [], g = w ? kp(w, u.get(w.activityVersionId)) : !1, v = Op(e, t?.state, i, u), j = !h && y?.slot.mode === "flowchart";
  return {
    catalogByVersion: u,
    availabilityLookup: l,
    scopeOwner: f,
    isUnsupportedDesigner: h,
    scope: y,
    selectedNode: w,
    selectedDescriptor: m,
    selectedNodeAvailability: x,
    selectedSlots: b,
    selectedSupportsScopedVariables: g,
    scopedVariableAnalysis: v,
    isFlowchartDesigner: j,
    canAddActivitiesToCanvas: !c || !h
  };
}
function ZN({
  details: e,
  draft: t,
  selectedNode: n,
  selectedNodeId: i,
  selectedDescriptor: r,
  catalogByVersion: s
}) {
  te(() => {
    if (!(!e || !t))
      return window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
        workflowId: e.definition.id,
        workflowDefinitionId: e.definition.id,
        workflowVersionId: t.sourceVersionId ?? null,
        draftId: t.id,
        revision: Zb(t),
        selectedNodeId: i,
        selectedActivityType: r?.typeName ?? (n ? s.get(n.activityVersionId)?.activityTypeKey ?? n.activityVersionId : null),
        summary: e.definition.name,
        activities: zd(t.state.rootActivity, s),
        connections: Ld(t.state.rootActivity, s),
        diagnostics: t.validationErrors.map((a) => ({ severity: a.code ?? "warning", message: a.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === e.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [s, e, t, r, n, i]);
}
function GN({
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
      /* @__PURE__ */ o.jsx(Ji, { size: 14, "aria-hidden": "true" }),
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
              f ? /* @__PURE__ */ o.jsx(Zc, { size: 14 }) : /* @__PURE__ */ o.jsx(bt, { size: 14 }),
              /* @__PURE__ */ o.jsx("span", { children: d.category }),
              /* @__PURE__ */ o.jsx("small", { children: d.activities.length })
            ]
          }
        ),
        f ? /* @__PURE__ */ o.jsx("div", { className: "wf-palette-activities", role: "group", children: d.activities.map((p) => {
          const h = p.description?.trim(), y = h ? `wf-palette-description-${p.activityVersionId}` : void 0, w = Se(p), m = dn(p);
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
              onDragStart: (x) => a(x, p),
              onDragEnd: (x) => c(x, p),
              onPointerDown: (x) => u(x, p),
              children: [
                /* @__PURE__ */ o.jsx("span", { className: "wf-activity-icon", "data-icon": m, "aria-hidden": "true", children: ir(m) }),
                /* @__PURE__ */ o.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ o.jsx("strong", { children: w }),
                  h ? /* @__PURE__ */ o.jsx("small", { id: y, children: h }) : null
                ] }),
                /* @__PURE__ */ o.jsx(Gc, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            p.activityVersionId
          );
        }) }) : null
      ] }, d.category);
    }) })
  ] });
}
const qd = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]), JN = "Variable";
function QN({
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
  const d = aj(l), f = r.length > 0 ? r : kh;
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ o.jsx("span", { className: "wf-section-label", children: "Properties" }),
    d.map((p) => /* @__PURE__ */ o.jsxs("section", { className: "wf-property-group", children: [
      d.length > 1 ? /* @__PURE__ */ o.jsx("h4", { children: p.category }) : null,
      p.inputs.map((h) => /* @__PURE__ */ o.jsx(
        ej,
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
function ej({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: i,
  expressionDescriptors: r,
  visibleVariables: s,
  scopeStatus: a,
  onChange: c
}) {
  const u = t.isReadOnly === !0, l = { activity: e, expressionDescriptors: r, readOnly: u }, d = Ko(n, t, l), f = d?.component, p = t.isWrapped !== !1 ? Al(e, t) : null, h = p?.expression.type ?? "Literal", y = Dh(e, t), w = h.toLowerCase(), x = p && (w === "literal" || w === "object") && !Mh(t) ? $h(t.typeName) : null, b = x ? Ko(n, t, { ...l, scope: "collection" }) : void 0, g = p ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: r,
    readOnly: u,
    surface: "inline",
    syntax: h
  } : null, v = g ? Ud(i, g) : null, j = v?.surfaces.inline, N = v && g ? Zd(v, g, y) : [], S = x != null, k = !!(p && !S && cj(t, d?.id)), A = !!(p && !S && lj(t, d?.id)), [M, _] = K(!1), T = (E) => {
    const D = p ? Ah(p, E) : E;
    c(oa(e, t, D));
  }, L = (E) => {
    p && c(oa(e, t, _h(p, E)));
  }, C = x ? b ? Wo(b.component, t, y, u, { ...l, scope: "collection" }, T) : /* @__PURE__ */ o.jsx(
    nj,
    {
      input: t,
      elementTypeName: x.elementTypeName,
      value: y,
      editors: n,
      context: l,
      disabled: u,
      onChange: T
    }
  ) : null, I = h === JN && p ? /* @__PURE__ */ o.jsx(
    oj,
    {
      value: y,
      visibleVariables: s,
      scopeStatus: a,
      disabled: u,
      onChange: T
    }
  ) : C ?? (j && g ? /* @__PURE__ */ o.jsx(
    j,
    {
      descriptor: t,
      syntax: h,
      value: y,
      disabled: u,
      context: g,
      onChange: T
    }
  ) : Wo(f, t, y, u, l, T));
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ o.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ o.jsx("span", { children: ss(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ o.jsx("p", { children: t.description }) : null,
    p && !k ? /* @__PURE__ */ o.jsx(
      Bo,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: h,
        descriptors: r,
        disabled: u,
        onChange: L
      }
    ) : null,
    k ? /* @__PURE__ */ o.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-expression-editor", children: [
        I,
        Xo(N)
      ] }),
      /* @__PURE__ */ o.jsx(
        Bo,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: h,
          descriptors: r,
          disabled: u,
          variant: "inline",
          onChange: L
        }
      ),
      A ? /* @__PURE__ */ o.jsx(
        "button",
        {
          type: "button",
          className: "wf-expression-expand-button",
          "aria-label": `Open expanded ${t.displayName || t.name} editor`,
          title: "Open expanded editor",
          onClick: () => _(!0),
          children: /* @__PURE__ */ o.jsx(Dn, { size: 13 })
        }
      ) : null
    ] }) : /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
      I,
      Xo(N)
    ] }),
    A && !k ? /* @__PURE__ */ o.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => _(!0),
        children: [
          /* @__PURE__ */ o.jsx(Dn, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    M ? /* @__PURE__ */ o.jsx(
      ij,
      {
        input: t,
        value: y,
        syntax: h,
        descriptors: r,
        activity: e,
        expressionEditors: i,
        disabled: u,
        onChange: T,
        onSyntaxChange: L,
        onClose: () => _(!1)
      }
    ) : null
  ] });
}
function tj(e, t, n) {
  return [
    "wf-collection-item",
    t === e ? "dragging" : "",
    t !== null && t !== e && n === e ? "drop-target" : ""
  ].filter(Boolean).join(" ");
}
function nj({
  input: e,
  elementTypeName: t,
  value: n,
  editors: i,
  context: r,
  disabled: s,
  onChange: a
}) {
  const c = Rh(n), u = Lh(e, t), l = { ...r, scope: "element" }, d = Ko(i, u, l)?.component, f = e.displayName || e.name, p = (j, N) => a(c.map((S, k) => k === j ? N : S)), [h, y] = K(null), [w, m] = K(null), x = () => {
    y(null), m(null);
  }, b = (j) => (N) => {
    y(j), N.dataTransfer.effectAllowed = "move", N.dataTransfer.setData("text/plain", String(j));
  }, g = (j) => (N) => {
    h !== null && (N.preventDefault(), N.dataTransfer.dropEffect = "move", w !== j && m(j));
  }, v = (j) => (N) => {
    N.preventDefault(), h !== null && h !== j && a(ro(c, h, j)), x();
  };
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-collection-editor", children: [
    c.length === 0 ? /* @__PURE__ */ o.jsx("p", { className: "wf-collection-empty", children: "No items yet." }) : /* @__PURE__ */ o.jsx("ul", { className: "wf-collection-items", children: c.map((j, N) => /* @__PURE__ */ o.jsxs(
      "li",
      {
        className: tj(N, h, w),
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
              onDragEnd: x,
              children: /* @__PURE__ */ o.jsx(Gc, { size: 13, "aria-hidden": "true" })
            }
          ),
          /* @__PURE__ */ o.jsx("div", { className: "wf-collection-item-editor", children: Wo(d, u, j, s, l, (S) => p(N, S)) }),
          /* @__PURE__ */ o.jsxs("div", { className: "wf-collection-item-actions", children: [
            /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${N + 1} up`,
                disabled: s || N === 0,
                onClick: () => a(ro(c, N, N - 1)),
                children: /* @__PURE__ */ o.jsx(ff, { size: 13 })
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
                children: /* @__PURE__ */ o.jsx(Zc, { size: 13 })
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
                children: /* @__PURE__ */ o.jsx(_n, { size: 13 })
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
        onClick: () => a([...c, zh(t)]),
        children: [
          /* @__PURE__ */ o.jsx(Jt, { size: 13 }),
          " Add item"
        ]
      }
    )
  ] });
}
function ij({
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
  const d = Xc(), f = e.displayName || e.name, p = {
    activity: r,
    descriptor: e,
    expressionDescriptors: i,
    readOnly: a,
    surface: "expanded",
    syntax: n
  }, h = Ud(s, p), y = h?.surfaces.expanded, w = h ? Zd(h, p, t) : [], m = y ? null : sj(s, p);
  return te(() => {
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
      /* @__PURE__ */ o.jsx("button", { type: "button", "aria-label": `Close ${f} editor`, onClick: l, children: /* @__PURE__ */ o.jsx(Zo, { size: 16 }) })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ o.jsx(
          Bo,
          {
            label: `${f} expression syntax`,
            value: n,
            descriptors: i,
            disabled: a,
            onChange: u
          }
        ),
        /* @__PURE__ */ o.jsx("span", { children: ss(e.typeName) })
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
            onChange: (x) => c(x.target.value)
          }
        )
      ] }),
      Xo(w)
    ] }),
    /* @__PURE__ */ o.jsxs("footer", { children: [
      /* @__PURE__ */ o.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: l, children: "Close" })
    ] })
  ] }) });
}
function Wo(e, t, n, i, r, s) {
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
function Bo({
  label: e,
  value: t,
  descriptors: n,
  disabled: i,
  variant: r = "block",
  onChange: s
}) {
  const [a, c] = K(!1), u = Xc(), l = n.find((f) => f.type === t), d = [
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
const Fo = "::";
function Yd(e) {
  return !e || e === Oi ? Oi : e;
}
function Hc(e, t) {
  return `${Yd(t)}${Fo}${e}`;
}
function rj(e) {
  const t = e.indexOf(Fo);
  if (t < 0) return null;
  const n = e.slice(t + Fo.length);
  return n ? { scopeId: e.slice(0, t), referenceKey: n } : null;
}
function oj({ value: e, visibleVariables: t, scopeStatus: n, disabled: i, onChange: r }) {
  const s = xl(e), a = !!e && typeof e == "object" || typeof e == "string" && e.trim().startsWith("{"), c = s && (a || t.some((d) => d.referenceKey === s.referenceKey)) ? s : null, u = c ? Hc(c.referenceKey, c.declaringScopeId) : "", l = !!c && t.some(
    (d) => d.referenceKey === c.referenceKey && d.scopeId === Yd(c.declaringScopeId)
  );
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-variable-picker", children: [
    /* @__PURE__ */ o.jsxs(
      "select",
      {
        "aria-label": "Variable reference",
        value: u,
        disabled: i,
        onChange: (d) => {
          const f = rj(d.target.value);
          f && r(Ap(f.referenceKey, f.scopeId));
        },
        children: [
          /* @__PURE__ */ o.jsx("option", { value: "", children: "Select a variable…" }),
          c && !l ? /* @__PURE__ */ o.jsxs("option", { value: u, children: [
            c.referenceKey,
            " (not visible from this scope)"
          ] }) : null,
          t.map((d) => {
            const f = Hc(d.referenceKey, d.scopeId);
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
function Ko(e, t, n) {
  return [...e].sort((i, r) => (i.order ?? 500) - (r.order ?? 500)).find((i) => i.supports(t, n));
}
function Ud(e, t) {
  return [...e].sort((n, i) => (n.order ?? 500) - (i.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function Zd(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function sj(e, t) {
  if (t.syntax.toLowerCase() === "literal") return null;
  const n = [...e].sort((a, c) => (a.order ?? 500) - (c.order ?? 500)).find((a) => a.supports(t) && a.metadata)?.metadata;
  if (!n) return `No enhanced editor is registered for ${t.syntax}. Using the generic text editor.`;
  const i = n.displayName?.trim() || "enhanced editor", r = n.installHint?.trim(), s = `No ${i} is registered for ${t.syntax}. Using the generic text editor.`;
  return r ? `${s} ${r}` : s;
}
function Xo(e) {
  return e.length === 0 ? null : /* @__PURE__ */ o.jsx("div", { className: "wf-expression-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const i = t.severity ?? "info";
    return /* @__PURE__ */ o.jsxs("p", { className: `wf-expression-editor-diagnostic ${i}`, children: [
      t.code ? /* @__PURE__ */ o.jsx("span", { children: t.code }) : null,
      t.message
    ] }, `${t.code ?? "diagnostic"}-${n}`);
  }) });
}
function aj(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const i = n.category?.trim() || "General";
    t.set(i, [...t.get(i) ?? [], n]);
  }
  return [...t.entries()].map(([n, i]) => ({ category: n, inputs: i }));
}
function cj(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !qd.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function lj(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !qd.has(t) && n !== "multiline") return !1;
  const i = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(i) || n === "singleline" || n === "multiline";
}
function uj({
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
        Pn(s.state)
      ] })
    ] }) : null,
    /* @__PURE__ */ o.jsx(
      QN,
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
      vb,
      {
        context: e,
        variables: ml(t),
        title: "Container variables",
        addLabel: "Add container variable",
        emptyLabel: "No container variables declared on this activity.",
        warnings: Dp(h.shadowingWarnings, t.nodeId),
        onChange: (m) => y(Ip(t, m))
      }
    ) }) : null,
    a.length > 0 ? /* @__PURE__ */ o.jsxs("div", { className: "wf-slot-list", children: [
      /* @__PURE__ */ o.jsx("span", { children: "Embedded slots" }),
      a.map((m) => /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => w(t, m.id, `${n} / ${m.label}`), children: [
        m.label,
        /* @__PURE__ */ o.jsx("small", { children: Jb(m, c) })
      ] }, m.id))
    ] }) : /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
  ] }) : /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
}
const Gd = [
  { value: "flowchart", label: "Flowchart", hint: "Free-form graph of connected activities." },
  { value: "sequence", label: "Sequence", hint: "Ordered list of activities that run top to bottom." }
];
function Jd({ label: e, hint: t }) {
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsx("span", { className: "wf-root-card-title", children: e }),
    /* @__PURE__ */ o.jsx("span", { className: "wf-root-card-hint", children: t })
  ] });
}
function dj({ value: e, onChange: t }) {
  return /* @__PURE__ */ o.jsx("div", { className: "wf-root-cards", role: "radiogroup", "aria-label": "Root activity", children: Gd.map((n) => {
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
      /* @__PURE__ */ o.jsx(Jd, { label: n.label, hint: n.hint })
    ] }, n.value);
  }) });
}
function fj({ onPick: e }) {
  return /* @__PURE__ */ o.jsx("div", { className: "wf-root-cards", role: "group", "aria-label": "Fill this slot", children: Gd.map((t) => /* @__PURE__ */ o.jsx(
    "button",
    {
      type: "button",
      className: "wf-root-card",
      "aria-label": t.label,
      onClick: () => e(t.value),
      children: /* @__PURE__ */ o.jsx(Jd, { label: t.label, hint: t.hint })
    },
    t.value
  )) });
}
function pj({ slotLabel: e, catalog: t, onPickActivity: n, onBrowseAll: i }) {
  const r = (s) => {
    const a = Dd(t, s);
    a && n(a);
  };
  return /* @__PURE__ */ o.jsx("div", { className: "wf-slot-empty", role: "group", "aria-label": `Fill ${e}`, children: /* @__PURE__ */ o.jsxs("div", { className: "wf-slot-empty-card", children: [
    /* @__PURE__ */ o.jsx("p", { className: "wf-slot-empty-title", children: "This slot is empty" }),
    /* @__PURE__ */ o.jsxs("p", { className: "wf-slot-empty-hint", children: [
      "Choose a container for ",
      /* @__PURE__ */ o.jsx("strong", { children: e }),
      ", or pick any activity."
    ] }),
    /* @__PURE__ */ o.jsx(fj, { onPick: r }),
    /* @__PURE__ */ o.jsxs(
      "button",
      {
        type: "button",
        className: "wf-slot-empty-browse",
        onClick: (s) => i({ clientX: s.clientX, clientY: s.clientY }),
        children: [
          /* @__PURE__ */ o.jsx(Xn, { size: 15 }),
          " Browse all activities…"
        ]
      }
    ),
    /* @__PURE__ */ o.jsx("p", { className: "wf-slot-empty-drag-hint", children: "Tip: you can also drag activities from the palette onto the canvas." })
  ] }) });
}
function hj({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: i,
  expressionEditors: r,
  workflowDesignerPanels: s,
  autosaveEnabledByDefault: a,
  onBack: c
}) {
  const u = EN(), { draft: l, frames: d, selectedNodeId: f, testRun: p, publishedArtifactId: h } = u.state, {
    loadDraft: y,
    replaceDraftByBatch: w,
    editDraft: m,
    editDraftAndSelect: x,
    select: b,
    navigateToScope: g,
    resetToRoot: v,
    enterSlot: j,
    startTestRun: N,
    clearTestRun: S,
    setPublishedArtifact: k
  } = u, [A, M] = K(""), [_, T] = K(""), [L, C] = K("idle"), [I, E] = K(() => /* @__PURE__ */ new Set()), [D, $] = K(""), [P, F] = K("activities"), [B, W] = K("inspector"), [G, H] = K("designer"), {
    paletteWidth: q,
    inspectorWidth: Q,
    paletteCollapsed: U,
    inspectorCollapsed: R,
    maximizedSidePanel: X,
    setInspectorCollapsed: ce,
    paletteExpanded: oe,
    inspectorExpanded: ee,
    editorBodyClassName: ie,
    editorBodyStyle: de,
    toggleSidePanelCollapsed: V,
    toggleSidePanelMaximized: ne,
    startSidePanelResize: he,
    handleSidePanelResizeKeyDown: ge
  } = Bd(), { resetHistory: Ee, undo: ke, redo: De, canUndoNow: it, canRedoNow: We } = jN({ draft: l, restoreDraft: y }), { saveDraft: Be, autosaveEnabled: Te, setAutosaveEnabled: Re, markSaved: z } = KN({ context: e, draft: l, autosaveEnabledByDefault: a, editDraft: m, setStatus: T, setError: M }), {
    details: O,
    setDetails: Y,
    catalog: Z,
    activityDescriptors: le,
    availabilityDiagnostics: xe,
    expressionDescriptors: ye,
    descriptorStatus: Ie,
    reload: Ae
  } = XN({ context: e, definitionId: t, resetHistory: Ee, loadDraft: y, markSaved: z, setError: M }), { updateDefinitionMeta: Fe } = qN({ context: e, details: O, setDetails: Y, setStatus: T }), {
    catalogByVersion: me,
    availabilityLookup: mr,
    scopeOwner: ii,
    isUnsupportedDesigner: xt,
    scope: Mt,
    selectedNode: Ge,
    selectedDescriptor: Rt,
    selectedNodeAvailability: xr,
    selectedSlots: ri,
    selectedSupportsScopedVariables: wr,
    scopedVariableAnalysis: vr,
    isFlowchartDesigner: dt,
    canAddActivitiesToCanvas: br
  } = UN({ context: e, draft: l, frames: d, selectedNodeId: f, catalog: Z, activityDescriptors: le, availabilityDiagnostics: xe }), zt = ue(() => Oo(Z), [Z]), Nr = ue(() => {
    const J = D.trim().toLowerCase();
    if (!J) return zt;
    const fe = Z.filter((be) => Se(be).toLowerCase().includes(J) || be.activityTypeKey.toLowerCase().includes(J) || (be.category ?? "").toLowerCase().includes(J) || (be.description ?? "").toLowerCase().includes(J));
    return Oo(fe);
  }, [Z, D, zt]), Lt = L !== "idle", jr = !!l?.state.rootActivity && !Lt, oi = Tt(n, "weaver.workflows.find-draft-risks"), si = Tt(n, "weaver.workflows.propose-update"), Sr = $N({
    draft: l,
    scope: Mt,
    scopeOwner: ii,
    frames: d,
    catalog: Z,
    catalogByVersion: me,
    isUnsupportedDesigner: xt,
    isFlowchartDesigner: dt,
    canAddActivitiesToCanvas: br,
    selectedNodeId: f,
    editDraft: m,
    editDraftAndSelect: x,
    select: b,
    resetToRoot: v,
    setStatus: T,
    setError: M
  }), {
    nodes: Vt,
    edges: Cr,
    canvasRef: Er,
    setReactFlowInstance: kr,
    connectMenu: Ot,
    setConnectMenu: ai,
    edgeActions: ci,
    onNodesChange: Ir,
    onEdgesChange: Ar,
    onNodesDelete: _r,
    onEdgesDelete: Dr,
    isValidConnection: Tr,
    onConnect: $r,
    onConnectStart: li,
    onConnectEnd: Pr,
    onReconnect: Mr,
    commitLayout: Rr,
    canAutoLayout: zr,
    autoLayout: Lr,
    onCanvasDragOver: ui,
    onCanvasDragLeave: di,
    onCanvasDrop: fi,
    openEmptyConnectMenu: hn,
    onConnectMenuPick: Vr,
    addActivity: Or,
    onPaletteClick: Hr,
    onPaletteDragStart: Wr,
    onPaletteDragEnd: Br,
    onPalettePointerDown: Fr
  } = Sr, Kr = !xt && d.length > 0 && !!Mt && Mt.slot.activities.length === 0 && Vt.length === 0;
  FN({ draft: l, details: O, catalog: Z, replaceDraftByBatch: w, setStatus: T, setError: M }), te(() => {
    !l?.state.rootActivity || Z.length === 0 || m(({ draft: J }) => {
      if (!J?.state.rootActivity) return null;
      const fe = dl(J.state.rootActivity, me);
      return !fe || fe === J.state.rootActivity ? null : {
        ...J,
        state: {
          ...J.state,
          rootActivity: fe
        }
      };
    });
  }, [Z.length, me, l?.state.rootActivity, m]), ZN({ details: O, draft: l, selectedNode: Ge, selectedNodeId: f, selectedDescriptor: Rt, catalogByVersion: me }), te(() => {
    E((J) => {
      let fe = !1;
      const be = new Set(J);
      for (const rt of zt)
        be.has(rt.category) || (be.add(rt.category), fe = !0);
      return fe ? be : J;
    });
  }, [zt]);
  const { exportJson: Xr, save: pi, promoteAndPublish: qr, run: hi } = YN({
    context: e,
    draft: l,
    details: O,
    busy: Lt,
    saveDraft: Be,
    reload: Ae,
    startTestRun: N,
    clearTestRun: S,
    setPublishedArtifact: k,
    setOperation: C,
    setStatus: T,
    setError: M,
    setActiveRightPanelId: W,
    setInspectorCollapsed: ce
  }), Yr = se((J) => {
    m(({ draft: fe }) => fe ? { ...fe, state: J(fe.state) } : null);
  }, [m]), Ur = se((J) => {
    if (!l) return "No draft is loaded.";
    const fe = tb(J, l);
    return fe.ok ? (y(fe.draft), T("Applied workflow JSON."), null) : fe.error;
  }, [l, y]);
  te(() => {
    const J = (fe) => {
      if (G !== "designer" || !(fe.metaKey || fe.ctrlKey)) return;
      const be = fe.target;
      if (be && (be.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(be.tagName))) return;
      const rt = fe.key.toLowerCase();
      rt === "z" && !fe.shiftKey ? (fe.preventDefault(), ke()) : (rt === "z" && fe.shiftKey || rt === "y") && (fe.preventDefault(), De());
    };
    return window.addEventListener("keydown", J), () => window.removeEventListener("keydown", J);
  }, [G, ke, De]);
  const gn = se((J) => {
    m(({ draft: fe }) => {
      const be = fe?.state.rootActivity;
      return !fe || !be ? null : {
        ...fe,
        state: {
          ...fe.state,
          rootActivity: ul(be, J.nodeId, () => J, me)
        }
      };
    });
  }, [me, m]), Zr = se((J) => {
    if (!J) return;
    const fe = l?.state.rootActivity;
    if (!fe) return;
    const be = Gf(fe, J, (rt) => {
      const Ls = me.get(rt.activityVersionId);
      return Ls ? Se(Ls) : rt.nodeId;
    }, me);
    be && (H("designer"), g(be, J), ce(!1));
  }, [l?.state.rootActivity, me, ce, g]), Gr = (J) => {
    E((fe) => {
      const be = new Set(fe);
      return be.has(J) ? be.delete(J) : be.add(J), be;
    });
  };
  if (!O || !l)
    return /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: A || "Loading workflow editor..." });
  const wt = p?.draftSignature === Le(l) ? p.view : null, Ps = wt && _.startsWith("Test run") ? "" : _, Qd = (J) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(J)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, ef = {
    definition: O.definition,
    draft: l,
    selectedActivity: Ge,
    selectedActivityDescriptor: Rt,
    selectedActivitySlots: ri,
    catalog: Z,
    currentScopeOwner: ii,
    frames: d
  }, Ms = s.map((J) => {
    const fe = J.component;
    return {
      id: J.id,
      title: J.title,
      side: J.side,
      order: J.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ o.jsx(fe, { context: ef })
    };
  }), Jr = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ o.jsx(Xn, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(
        GN,
        {
          paletteSearch: D,
          onSearchChange: $,
          groups: Nr,
          expandedCategories: I,
          onToggleCategory: Gr,
          onActivityClick: Hr,
          onActivityDragStart: Wr,
          onActivityDragEnd: Br,
          onActivityPointerDown: Fr
        }
      )
    },
    ...Ms.filter((J) => J.side === "left")
  ].sort(Pc), Qr = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ o.jsx(Yo, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(
        uj,
        {
          context: e,
          selectedNode: Ge,
          selectedNodeLabel: Ge ? Vt.find((J) => J.id === Ge.nodeId)?.data.label ?? Ge.nodeId : "",
          selectedActivityType: Ge ? Rt?.typeName ?? me.get(Ge.activityVersionId)?.activityTypeKey ?? "Unknown" : "",
          selectedDescriptor: Rt,
          selectedNodeAvailability: xr,
          selectedSlots: ri,
          catalogByVersion: me,
          selectedSupportsScopedVariables: wr,
          propertyEditors: i,
          expressionEditors: r,
          expressionDescriptors: ye,
          descriptorStatus: Ie,
          scopedVariableAnalysis: vr,
          onSelectedActivityChange: gn,
          onEnterSlot: j
        }
      )
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: /* @__PURE__ */ o.jsx(Gt, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(hN, { testRun: wt, onOpenRun: Qd })
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ o.jsx(Jc, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(
        mN,
        {
          context: e,
          ai: n,
          definitionId: O.definition.id,
          publishedArtifactId: h
        }
      )
    },
    ...Ms.filter((J) => J.side === "right")
  ].sort(Pc), Rs = Jr.find((J) => J.id === P) ?? Jr[0], zs = Qr.find((J) => J.id === B) ?? Qr[0], tf = [
    { id: "designer", title: "Designer", order: 0, icon: /* @__PURE__ */ o.jsx(Qc, { size: 14 }), render: () => null },
    { id: "code", title: "Code", order: 1, icon: /* @__PURE__ */ o.jsx(mf, { size: 14 }), render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: /* @__PURE__ */ o.jsx(Uo, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-link-button", onClick: c, children: "Definitions" }),
      /* @__PURE__ */ o.jsx(bt, { size: 14 }),
      /* @__PURE__ */ o.jsx("strong", { children: O.definition.name }),
      /* @__PURE__ */ o.jsx("span", { className: "wf-chip", children: "Draft" }),
      Ps ? /* @__PURE__ */ o.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ o.jsx(ln, { size: 13 }),
        " ",
        Ps
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
              children: /* @__PURE__ */ o.jsx(pf, { size: 16 })
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
              onClick: De,
              children: /* @__PURE__ */ o.jsx(hf, { size: 16 })
            }
          ),
          /* @__PURE__ */ o.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Auto-layout",
              title: "Auto-layout the canvas",
              disabled: !zr,
              onClick: Lr,
              children: /* @__PURE__ */ o.jsx(gf, { size: 16 })
            }
          )
        ] }),
        /* @__PURE__ */ o.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ o.jsx("input", { className: "wf-switch-input", type: "checkbox", checked: Te, onChange: (J) => Re(J.target.checked) }),
          /* @__PURE__ */ o.jsx("span", { children: "Autosave" })
        ] }),
        oi ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => $t(n, oi, { definition: O.definition, draft: l }), children: [
          /* @__PURE__ */ o.jsx(ct, { size: 15 }),
          " Risks"
        ] }) : null,
        si ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => $t(n, si, { definition: O.definition, draft: l }), children: [
          /* @__PURE__ */ o.jsx(ct, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ o.jsxs("button", { type: "button", title: "Export workflow as JSON", onClick: Xr, children: [
          /* @__PURE__ */ o.jsx(yf, { size: 15 }),
          " Export"
        ] }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", disabled: Lt, onClick: () => {
          pi();
        }, children: [
          /* @__PURE__ */ o.jsx(Yc, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", disabled: Lt, onClick: () => {
          qr();
        }, children: [
          /* @__PURE__ */ o.jsx(qc, { size: 15 }),
          " Promote"
        ] }),
        wt ? /* @__PURE__ */ o.jsx(
          pN,
          {
            testRun: wt,
            onOpenDetails: () => {
              W("runtime"), ce(!1);
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
              hi();
            },
            children: [
              /* @__PURE__ */ o.jsx(Gt, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    A ? /* @__PURE__ */ o.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ o.jsx(mt, { size: 16 }),
      " ",
      A
    ] }) : null,
    /* @__PURE__ */ o.jsxs("div", { className: ie, style: de, children: [
      /* @__PURE__ */ o.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ o.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ o.jsx(
            Ri,
            {
              label: "Activities panel tabs",
              tabs: Jr,
              activeTabId: Rs.id,
              onSelect: F
            }
          ),
          /* @__PURE__ */ o.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": U ? "Expand activities panel" : "Collapse activities panel",
                title: U ? "Expand" : "Collapse",
                onClick: () => V("palette"),
                children: U ? /* @__PURE__ */ o.jsx(bt, { size: 14 }) : /* @__PURE__ */ o.jsx(Tn, { size: 14 })
              }
            ),
            U ? null : /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": X === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: X === "palette" ? "Restore" : "Maximize",
                onClick: () => ne("palette"),
                children: X === "palette" ? /* @__PURE__ */ o.jsx(bo, { size: 14 }) : /* @__PURE__ */ o.jsx(Dn, { size: 14 })
              }
            )
          ] })
        ] }),
        oe ? Rs.render() : null
      ] }),
      oe && !X ? /* @__PURE__ */ o.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Sn,
          "aria-valuemax": Cn,
          "aria-valuenow": q,
          tabIndex: 0,
          onPointerDown: (J) => he("palette", J),
          onKeyDown: (J) => ge("palette", J)
        }
      ) : /* @__PURE__ */ o.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ o.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ o.jsx("div", { className: "wf-canvas-tabs", children: /* @__PURE__ */ o.jsx(
          Ri,
          {
            label: "Editor view tabs",
            tabs: tf,
            activeTabId: G,
            onSelect: (J) => H(J)
          }
        ) }),
        G === "code" ? /* @__PURE__ */ o.jsx(ab, { draft: l, onApply: Ur }) : G === "properties" ? /* @__PURE__ */ o.jsx(Nb, { details: O, draft: l, context: e, onStateChange: Yr, onDefinitionMetaChange: Fe }) : /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
          /* @__PURE__ */ o.jsxs("div", { className: "wf-breadcrumb", children: [
            /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => v(), children: "Root" }),
            d.map((J, fe) => /* @__PURE__ */ o.jsxs(Ue.Fragment, { children: [
              /* @__PURE__ */ o.jsx(bt, { size: 13 }),
              /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => g(d.slice(0, fe + 1), null), children: J.label })
            ] }, `${J.ownerNodeId}-${J.slotId}-${fe}`))
          ] }),
          /* @__PURE__ */ o.jsxs("div", { className: "wf-canvas", ref: Er, onDragOver: ui, onDragLeave: di, onDrop: fi, children: [
            /* @__PURE__ */ o.jsx(Id.Provider, { value: ci, children: /* @__PURE__ */ o.jsx(Ad.Provider, { value: mr, children: /* @__PURE__ */ o.jsxs(
              xd,
              {
                nodes: Vt,
                edges: Cr,
                nodeTypes: Od,
                edgeTypes: Hd,
                onInit: kr,
                onNodesChange: Ir,
                onEdgesChange: Ar,
                onNodesDelete: _r,
                onEdgesDelete: Dr,
                onConnect: $r,
                onConnectStart: dt ? li : void 0,
                onConnectEnd: dt ? Pr : void 0,
                onReconnect: dt ? Mr : void 0,
                isValidConnection: Tr,
                onDragOver: ui,
                onDragLeave: di,
                onDrop: fi,
                onPaneClick: () => b(null),
                onNodeClick: (J, fe) => b(fe.id),
                onNodeDragStop: xt ? void 0 : Rr,
                fitView: !0,
                minZoom: 0.2,
                maxZoom: 1.8,
                nodesConnectable: dt,
                nodesDraggable: !xt,
                selectionOnDrag: !0,
                multiSelectionKeyCode: ["Shift", "Meta", "Control"],
                deleteKeyCode: xt ? null : ["Backspace", "Delete"],
                panActivationKeyCode: null,
                defaultEdgeOptions: { type: "workflow" },
                children: [
                  /* @__PURE__ */ o.jsx(vd, { gap: 18, size: 1 }),
                  /* @__PURE__ */ o.jsx(Nd, {}),
                  /* @__PURE__ */ o.jsx(Sd, { pannable: !0, zoomable: !0 })
                ]
              }
            ) }) }),
            Kr ? /* @__PURE__ */ o.jsx(
              pj,
              {
                slotLabel: Mt?.slot.label ?? "this slot",
                catalog: Z,
                onPickActivity: Or,
                onBrowseAll: hn
              }
            ) : dt && Vt.length === 0 ? /* @__PURE__ */ o.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => hn(), children: [
              /* @__PURE__ */ o.jsx(Jt, { size: 15 }),
              " Add activity"
            ] }) : null,
            Ot ? /* @__PURE__ */ o.jsx(
              sN,
              {
                clientX: Ot.clientX,
                clientY: Ot.clientY,
                activities: Z,
                onPick: Vr,
                onClose: () => ai(null)
              }
            ) : null
          ] }),
          /* @__PURE__ */ o.jsx(fN, { draft: l, onRepair: Zr })
        ] })
      ] }),
      ee && !X ? /* @__PURE__ */ o.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Kt,
          "aria-valuemax": Xt,
          "aria-valuenow": Q,
          tabIndex: 0,
          onPointerDown: (J) => he("inspector", J),
          onKeyDown: (J) => ge("inspector", J)
        }
      ) : /* @__PURE__ */ o.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ o.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ o.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ o.jsx(
            Ri,
            {
              label: "Inspector panel tabs",
              tabs: Qr,
              activeTabId: zs.id,
              onSelect: W
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
                onClick: () => V("inspector"),
                children: R ? /* @__PURE__ */ o.jsx(Tn, { size: 14 }) : /* @__PURE__ */ o.jsx(bt, { size: 14 })
              }
            ),
            R ? null : /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": X === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: X === "inspector" ? "Restore" : "Maximize",
                onClick: () => ne("inspector"),
                children: X === "inspector" ? /* @__PURE__ */ o.jsx(bo, { size: 14 }) : /* @__PURE__ */ o.jsx(Dn, { size: 14 })
              }
            )
          ] })
        ] }),
        ee ? zs.render() : null
      ] })
    ] })
  ] });
}
function gj({ page: e, pageSize: t, totalCount: n, onPageChange: i, onPageSizeChange: r }) {
  const s = _d(n, t), a = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
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
      /* @__PURE__ */ o.jsx("select", { value: t, onChange: (u) => r(Number(u.target.value)), children: Eb.map((u) => /* @__PURE__ */ o.jsx("option", { value: u, children: u }, u)) })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => i(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ o.jsx(Tn, { size: 14 }),
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
        /* @__PURE__ */ o.jsx(bt, { size: 14 })
      ] })
    ] })
  ] });
}
function yj({ draft: e, creating: t, ai: n, suggestMetadataAction: i, onChange: r, onClose: s, onSubmit: a }) {
  const [c, u] = K(!1), [l, d] = K(""), [f, p] = K(!1), [h, y] = K(null), [w, m] = K(null), x = ae(null), b = ae(e);
  b.current = e;
  const g = ae(r);
  g.current = r;
  const v = se((N) => {
    const S = { ...b.current };
    N.name && (S.name = N.name), N.description && (S.description = N.description), g.current(S), y(null), m(null);
  }, []);
  te(() => {
    if (i)
      return n.onPromptResult((N) => {
        if (N.requestId !== x.current) return;
        if (x.current = null, p(!1), N.status !== "completed") {
          m(N.status === "cancelled" ? "Weaver needs more detail — continue in the assistant panel." : "Weaver couldn't generate a suggestion. Try again or fill the fields manually.");
          return;
        }
        const S = Db(N.text);
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
    x.current = S, p(!0), y(null), m(null), n.dispatchPrompt({ ...N, requestId: S });
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
                /* @__PURE__ */ o.jsx(ct, { size: 13 }),
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
            /* @__PURE__ */ o.jsx(ct, { size: 13 }),
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
            dj,
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
function mj({ context: e, ai: t, onOpen: n }) {
  const [i, r] = K(""), [s, a] = K("active"), [c, u] = K(1), [l, d] = K(kb), [f, p] = K("loading"), [h, y] = K(""), [w, m] = K(""), [x, b] = K([]), [g, v] = K(0), [j, N] = K(() => /* @__PURE__ */ new Set()), [S, k] = K(null), [A, M] = K(!1), [_, T] = K([]), [L, C] = K("idle"), I = ae(null), E = ue(() => x.map((V) => V.id), [x]), D = Tt(t, "weaver.workflows.suggest-create-metadata"), $ = Tt(t, "weaver.workflows.explain-definition"), P = E.filter((V) => j.has(V)).length, F = E.length > 0 && P === E.length, B = se(async () => {
    p("loading"), y("");
    try {
      const V = await zp(e, { search: i, state: s, page: c, pageSize: l }), ne = typeof V.totalCount == "number", he = V.totalCount ?? V.definitions.length, ge = _d(he, l);
      if (he > 0 && c > ge) {
        u(ge);
        return;
      }
      b(ne ? V.definitions : _b(V.definitions, c, l)), v(he), p("ready");
    } catch (V) {
      y(V instanceof Error ? V.message : String(V)), p("failed");
    }
  }, [e, i, s, c, l]);
  te(() => {
    B();
  }, [B]), te(() => {
    I.current && (I.current.indeterminate = P > 0 && !F);
  }, [F, P]);
  const W = se(async () => {
    if (!(L === "loading" || L === "ready")) {
      C("loading");
      try {
        const V = await tr(e);
        T(V.activities ?? []), C("ready");
      } catch (V) {
        C("failed"), y(V instanceof Error ? V.message : String(V));
      }
    }
  }, [L, e]), G = () => {
    y(""), m(""), k({ name: "", description: "", rootKind: "flowchart" }), W();
  }, H = async () => {
    if (S?.name.trim()) {
      M(!0), y(""), m("");
      try {
        const V = await Wp(e, {
          name: S.name.trim(),
          description: S.description.trim() || null,
          rootKind: S.rootKind,
          rootActivityVersionId: Tb(S, _)
        });
        k(null), n(V.definition.id);
      } catch (V) {
        y(V instanceof Error ? V.message : String(V));
      } finally {
        M(!1);
      }
    }
  }, q = (V) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(V)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, Q = async () => {
    if (x.length === 1 && c > 1) {
      u(c - 1);
      return;
    }
    await B();
  }, U = () => N(/* @__PURE__ */ new Set()), R = (V, ne) => {
    N((he) => {
      const ge = new Set(he);
      return ne ? ge.add(V) : ge.delete(V), ge;
    });
  }, X = (V) => {
    N((ne) => {
      const he = new Set(ne);
      for (const ge of E)
        V ? he.add(ge) : he.delete(ge);
      return he;
    });
  }, ce = (V) => {
    a(V), u(1), U();
  }, oe = (V) => {
    r(V), u(1), U();
  }, ee = async (V) => {
    if (await Ws().confirm({ message: `Delete workflow definition "${V.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" })) {
      m(""), y("");
      try {
        await Bp(e, V.id), R(V.id, !1), m(`Deleted ${V.name}`), await Q();
      } catch (ne) {
        y(ne instanceof Error ? ne.message : String(ne));
      }
    }
  }, ie = async (V) => {
    m(""), y("");
    try {
      await Fp(e, V.id), R(V.id, !1), m(`Restored ${V.name}`), await Q();
    } catch (ne) {
      y(ne instanceof Error ? ne.message : String(ne));
    }
  }, de = async (V) => {
    if (await Ws().confirm({ message: `Permanently delete workflow definition "${V.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) {
      m(""), y("");
      try {
        await Kp(e, V.id), R(V.id, !1), m(`Permanently deleted ${V.name}`), await Q();
      } catch (ne) {
        y(ne instanceof Error ? ne.message : String(ne));
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
        /* @__PURE__ */ o.jsx(Ji, { size: 15 }),
        /* @__PURE__ */ o.jsx("input", { value: i, onChange: (V) => oe(V.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => {
        B();
      }, children: "Refresh" }),
      /* @__PURE__ */ o.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ o.jsxs("button", { type: "button", title: "Create workflow", onClick: G, children: [
        /* @__PURE__ */ o.jsx(Jt, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ o.jsx(ni, { message: h, title: "Couldn't load workflow definitions" }) : null,
    f !== "failed" && h ? /* @__PURE__ */ o.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ o.jsx(mt, { size: 16 }),
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
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: U, children: "Clear selection" })
    ] }) : null,
    f === "loading" ? /* @__PURE__ */ o.jsx(_s, {}) : null,
    f === "ready" && x.length === 0 ? /* @__PURE__ */ o.jsx(
      Ds,
      {
        icon: /* @__PURE__ */ o.jsx(Jc, { size: 22 }),
        title: `No ${s} workflow definitions`,
        description: "Create a workflow to start designing automation, or adjust your filters to see more.",
        action: /* @__PURE__ */ o.jsxs("button", { type: "button", className: "wf-link-button", onClick: G, children: [
          /* @__PURE__ */ o.jsx(Jt, { size: 15 }),
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
              ref: I,
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
        x.map((V) => /* @__PURE__ */ o.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${V.name}`,
            "aria-selected": j.has(V.id),
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
                  checked: j.has(V.id),
                  onChange: (ne) => R(V.id, ne.target.checked),
                  "aria-label": `Select workflow definition ${V.name}`
                }
              ) }),
              /* @__PURE__ */ o.jsxs("span", { children: [
                /* @__PURE__ */ o.jsx("strong", { children: V.name }),
                /* @__PURE__ */ o.jsx("small", { children: V.description || V.id })
              ] }),
              /* @__PURE__ */ o.jsx("span", { children: V.latestVersion ?? "No version" }),
              /* @__PURE__ */ o.jsx("span", { children: s === "deleted" ? Ve(V.deletedAt) : V.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ o.jsx("span", { children: Ve(V.lastModifiedAt) }),
              /* @__PURE__ */ o.jsx("span", { className: "wf-row-actions", onClick: (ne) => ne.stopPropagation(), children: s === "active" ? /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
                /* @__PURE__ */ o.jsx("button", { type: "button", onClick: (ne) => {
                  ne.stopPropagation(), n(V.id);
                }, children: "Open" }),
                /* @__PURE__ */ o.jsx("button", { type: "button", onClick: (ne) => {
                  ne.stopPropagation(), q(V.id);
                }, children: "Artifacts" }),
                $ ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => $t(t, $, V), children: [
                  /* @__PURE__ */ o.jsx(ct, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ o.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  ee(V);
                }, children: [
                  /* @__PURE__ */ o.jsx(_n, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
                /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
                  ie(V);
                }, children: [
                  /* @__PURE__ */ o.jsx(Go, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ o.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  de(V);
                }, children: [
                  /* @__PURE__ */ o.jsx(_n, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          V.id
        ))
      ] }),
      /* @__PURE__ */ o.jsx(
        gj,
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
      yj,
      {
        draft: S,
        creating: A,
        ai: t,
        suggestMetadataAction: D,
        onChange: (V) => k(V),
        onClose: () => k(null),
        onSubmit: H
      }
    ) : null
  ] });
}
function xj({ activities: e, activityCatalog: t, selectedEvidenceId: n = null, onSelectEvidence: i }) {
  const r = ue(
    () => new Map(t.map((a) => [a.activityTypeKey, a])),
    [t]
  ), s = ue(() => vj(e), [e]);
  return s.length === 0 ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty wf-timeline-empty", children: "No activity executions recorded yet." }) : /* @__PURE__ */ o.jsx("ol", { className: "wf-timeline", "aria-label": "Execution timeline", children: s.map((a) => {
    const c = r.get(a.activityType), u = dn(c), l = c ? Se(c) : Et(a.activityType) ?? a.activityType, d = Et(a.activityType) ?? a.activityType, f = bj(a.startedAt ?? a.scheduledAt), p = cs(a.startedAt, a.completedAt);
    return /* @__PURE__ */ o.jsx("li", { children: /* @__PURE__ */ o.jsxs(
      "button",
      {
        type: "button",
        className: "wf-timeline-entry",
        "data-selected": a.activityExecutionId === n,
        onClick: () => i?.(a.activityExecutionId),
        children: [
          /* @__PURE__ */ o.jsx("span", { className: "wf-timeline-icon wf-activity-icon", "data-icon": u, "aria-hidden": "true", children: ir(u) }),
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
          /* @__PURE__ */ o.jsx(wj, { status: a.status })
        ]
      }
    ) }, a.activityExecutionId);
  }) });
}
function wj({ status: e }) {
  const t = e.toLowerCase(), n = t === "completed" ? "OK" : e;
  return /* @__PURE__ */ o.jsx("span", { className: "wf-status-badge wf-timeline-pill", "data-status": t, children: n });
}
function vj(e) {
  return e.map((t, n) => ({ activity: t, index: n })).sort((t, n) => Wc(t.activity) - Wc(n.activity) || t.index - n.index).map((t) => t.activity);
}
function Wc(e) {
  const t = e.startedAt ?? e.scheduledAt, n = t ? Date.parse(t) : Number.NaN;
  return Number.isNaN(n) ? Number.MAX_SAFE_INTEGER : n;
}
function bj(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? "" : t.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: !1 });
}
function Nj({ context: e }) {
  const [t, n] = K("loading"), [i, r] = K(""), [s, a] = K(""), [c, u] = K(""), [l, d] = K([]), f = se(async () => {
    n("loading"), r("");
    try {
      const h = await Jp(e, {
        status: s || void 0,
        runKind: c || void 0,
        take: 100
      });
      d(h), n("ready");
    } catch (h) {
      r(h instanceof Error ? h.message : String(h)), d([]), n("failed");
    }
  }, [e, c, s]);
  te(() => {
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
    t === "failed" ? /* @__PURE__ */ o.jsx(ni, { message: i }) : null,
    t === "loading" ? /* @__PURE__ */ o.jsx(_s, {}) : null,
    t === "ready" && l.length === 0 ? /* @__PURE__ */ o.jsx(
      Ds,
      {
        icon: /* @__PURE__ */ o.jsx(Xn, { size: 22 }),
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
            /* @__PURE__ */ o.jsx("span", { children: Md(h.runKind) }),
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
            /* @__PURE__ */ o.jsx("span", { children: Ve(h.startedAt ?? h.createdAt) }),
            /* @__PURE__ */ o.jsx("span", { children: cs(h.startedAt ?? h.createdAt, h.completedAt ?? h.updatedAt) })
          ]
        },
        h.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function jj({ context: e, ai: t, workflowExecutionId: n }) {
  const [i, r] = K("loading"), [s, a] = K(""), [c, u] = K(null), [l, d] = K(null), {
    inspectorWidth: f,
    inspectorCollapsed: p,
    maximizedSidePanel: h,
    inspectorExpanded: y,
    editorBodyStyle: w,
    toggleSidePanelCollapsed: m,
    toggleSidePanelMaximized: x,
    startSidePanelResize: b,
    handleSidePanelResizeKeyDown: g
  } = Bd(), v = Tt(t, "weaver.workflows.explain-instance"), j = se(async () => {
    if (!n) {
      a("No workflow execution id was provided."), r("failed");
      return;
    }
    r("loading"), a("");
    try {
      const A = await Qp(e, n), [M, _] = await Promise.all([
        Hp(e, A.instance.definitionVersionId).then(
          (T) => ({ definitionVersion: T, error: "" }),
          (T) => ({ definitionVersion: null, error: T instanceof Error ? T.message : String(T) })
        ),
        tr(e)
      ]);
      u({
        details: A,
        definitionVersion: M.definitionVersion,
        definitionVersionError: M.error,
        activityCatalog: _.activities
      }), d(null), r("ready");
    } catch (A) {
      u(null), a(tN(A, n)), r("failed");
    }
  }, [e, n]);
  te(() => {
    j();
  }, [j]);
  const N = () => {
    window.history.pushState({}, "", "/workflows/instances"), window.dispatchEvent(new PopStateEvent("popstate"));
  }, S = () => {
    const A = c?.details.instance.definitionId;
    A && (window.history.pushState({}, "", `/workflows/definitions?definition=${encodeURIComponent(A)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  }, k = [
    "wf-instance-detail-workbench",
    p ? "inspector-collapsed" : "",
    h === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: N, children: [
        /* @__PURE__ */ o.jsx(Tn, { size: 14 }),
        " Runs"
      ] }),
      c?.details.instance.definitionId ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: S, children: [
        /* @__PURE__ */ o.jsx(Qc, { size: 14 }),
        " Designer"
      ] }) : null,
      /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
        j();
      }, children: [
        /* @__PURE__ */ o.jsx(Go, { size: 14 }),
        " Refresh"
      ] }),
      c && v ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => $t(t, v, c.details), children: [
        /* @__PURE__ */ o.jsx(ct, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    i === "loading" ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Loading workflow run..." }) : null,
    i === "failed" ? /* @__PURE__ */ o.jsx(ni, { message: s }) : null,
    i === "ready" && c ? /* @__PURE__ */ o.jsxs("div", { className: k, style: w, children: [
      /* @__PURE__ */ o.jsx(
        Sj,
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
          "aria-valuemin": Kt,
          "aria-valuemax": Xt,
          "aria-valuenow": f,
          tabIndex: 0,
          onPointerDown: (A) => b("inspector", A),
          onKeyDown: (A) => g("inspector", A)
        }
      ) : /* @__PURE__ */ o.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ o.jsx(
        Cj,
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
          graphNodeIds: c.definitionVersion ? Xb(c.definitionVersion, c.activityCatalog) : void 0,
          collapsed: p,
          expanded: y,
          maximized: h === "inspector",
          onToggleCollapsed: () => m("inspector"),
          onToggleMaximized: () => x("inspector")
        }
      )
    ] }) : null
  ] });
}
function Sj({ definitionVersion: e, definitionVersionError: t, activityCatalog: n, details: i, selectedEvidenceId: r, onSelectEvidence: s }) {
  const a = ue(() => {
    if (!e) return { nodes: [], edges: [] };
    const c = e.state.rootActivity;
    if (!c) return { nodes: [], edges: [] };
    const u = n.find((h) => h.activityVersionId === c.activityVersionId), l = is(c, u), d = l === "unsupported" ? null : Qt(c, [], n), f = l === "unsupported" ? So(c, n, e.layout) : d ? ll(d, n, e.layout) : So(c, n, e.layout), p = f.nodes.map((h) => ({
      ...h,
      draggable: !1,
      connectable: !1,
      deletable: !1
    }));
    return {
      nodes: op(p, i.activities, i.incidents, r),
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
      /* @__PURE__ */ o.jsx(pn, { status: i.instance.status, subStatus: i.instance.subStatus })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ o.jsxs("div", { className: "wf-empty", children: [
        "The workflow run loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ o.jsx("small", { children: eN(t) }) : null
      ] }),
      e && a.nodes.length === 0 ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      a.nodes.length > 0 ? /* @__PURE__ */ o.jsxs(
        xd,
        {
          nodes: a.nodes,
          edges: a.edges,
          nodeTypes: Od,
          edgeTypes: Hd,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (c, u) => s(u.id),
          onPaneClick: () => s(null),
          children: [
            /* @__PURE__ */ o.jsx(vd, {}),
            /* @__PURE__ */ o.jsx(Sd, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ o.jsx(Nd, {})
          ]
        }
      ) : null
    ] })
  ] });
}
function Cj({
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
  onToggleMaximized: w
}) {
  const [m, x] = K("timeline");
  if (!i)
    return /* @__PURE__ */ o.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Select a workflow run to inspect its timeline." }) });
  const b = r?.incidents.length ?? 0, g = Ej(r?.activities ?? [], c), v = (N) => {
    u?.(N), x("activity");
  }, j = [
    { id: "timeline", title: "Timeline", order: 0, icon: /* @__PURE__ */ o.jsx(Yo, { size: 14 }), render: () => null },
    { id: "activity", title: "Activity", order: 1, icon: /* @__PURE__ */ o.jsx(Uc, { size: 14 }), render: () => null },
    { id: "issues", title: b > 0 ? `Issues (${b})` : "Issues", order: 2, icon: /* @__PURE__ */ o.jsx(mt, { size: 14 }), render: () => null },
    { id: "details", title: "Details", order: 3, icon: /* @__PURE__ */ o.jsx(Uo, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ o.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Run details panel", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-panel-title wf-instance-panel-title", children: [
      /* @__PURE__ */ o.jsx(Ri, { label: "Run details tabs", tabs: j, activeTabId: m, onSelect: (N) => x(N) }),
      /* @__PURE__ */ o.jsxs("span", { className: "wf-panel-actions", children: [
        /* @__PURE__ */ o.jsx(
          "button",
          {
            type: "button",
            className: "wf-panel-action-button",
            "aria-label": f ? "Expand run details panel" : "Collapse run details panel",
            title: f ? "Expand" : "Collapse",
            onClick: y,
            children: f ? /* @__PURE__ */ o.jsx(Tn, { size: 14 }) : /* @__PURE__ */ o.jsx(bt, { size: 14 })
          }
        ),
        f ? null : /* @__PURE__ */ o.jsx(
          "button",
          {
            type: "button",
            className: "wf-panel-action-button",
            "aria-label": h ? "Restore run details panel" : "Maximize run details panel",
            title: h ? "Restore" : "Maximize",
            onClick: w,
            children: h ? /* @__PURE__ */ o.jsx(bo, { size: 14 }) : /* @__PURE__ */ o.jsx(Dn, { size: 14 })
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
        n ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => $t(t, n, r ?? i), children: [
          /* @__PURE__ */ o.jsx(ct, { size: 13 }),
          " Explain"
        ] }) : null
      ] }),
      s === "loading" ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Loading run details..." }) : null,
      s === "failed" ? /* @__PURE__ */ o.jsx(ni, { message: a }) : null,
      s === "ready" && r ? /* @__PURE__ */ o.jsx("div", { className: "wf-instance-tab-content", children: m === "timeline" ? /* @__PURE__ */ o.jsx(
        xj,
        {
          activities: r.activities,
          activityCatalog: d,
          selectedEvidenceId: c,
          onSelectEvidence: v
        }
      ) : m === "activity" ? /* @__PURE__ */ o.jsx(kj, { context: e, activity: g, activityCatalog: d }) : m === "issues" ? /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
        /* @__PURE__ */ o.jsx(Pj, { incidents: r.incidents, selectedEvidenceId: c, onSelectEvidence: u }),
        /* @__PURE__ */ o.jsx(Mj, { details: r, graphNodeIds: l })
      ] }) : /* @__PURE__ */ o.jsxs("dl", { className: "wf-instance-meta", children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ o.jsx("dd", { children: /* @__PURE__ */ o.jsx(pn, { status: i.status, subStatus: i.subStatus }) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Run Kind" }),
        /* @__PURE__ */ o.jsx("dd", { children: Md(i.runKind) }),
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
function Ej(e, t) {
  if (!t) return null;
  const n = e.find((r) => r.activityExecutionId === t);
  if (n) return n;
  const i = e.filter((r) => r.executableNodeId === t || r.authoredActivityId === t);
  return i.length > 0 ? yl(i) : null;
}
function kj({ context: e, activity: t, activityCatalog: n }) {
  const i = t?.activityExecutionId ?? null, r = t?.workflowExecutionId ?? null, [s, a] = K({
    activityExecutionId: null,
    status: "idle",
    inspection: null,
    error: ""
  });
  if (te(() => {
    if (!i || !r) {
      a({ activityExecutionId: null, status: "idle", inspection: null, error: "" });
      return;
    }
    let f = !1;
    const p = i;
    return a({ activityExecutionId: p, status: "loading", inspection: null, error: "" }), eh(e, r, p).then(
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
  const u = n.find((f) => f.activityTypeKey === t.activityType)?.displayName || Et(t.activityType) || t.activityType, l = t.bookmarkIds?.length ?? 0, d = t.incidentIds?.length ?? 0;
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
          Et(t.activityType) ?? t.activityType,
          " ",
          /* @__PURE__ */ o.jsx("small", { children: t.activityTypeVersion })
        ] }),
        /* @__PURE__ */ o.jsx("dt", { children: "Started" }),
        /* @__PURE__ */ o.jsx("dd", { children: Ve(t.startedAt) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Completed" }),
        /* @__PURE__ */ o.jsx("dd", { children: Ve(t.completedAt) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Duration" }),
        /* @__PURE__ */ o.jsx("dd", { children: cs(t.startedAt, t.completedAt) || "Unknown" }),
        /* @__PURE__ */ o.jsx("dt", { children: "Bookmarks" }),
        /* @__PURE__ */ o.jsx("dd", { children: l }),
        /* @__PURE__ */ o.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ o.jsx("dd", { children: d })
      ] })
    ] }),
    /* @__PURE__ */ o.jsx(Ij, { state: s })
  ] });
}
function Ij({ state: e }) {
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
    /* @__PURE__ */ o.jsx("div", { className: "wf-runtime-input-list", children: t.map((n) => /* @__PURE__ */ o.jsx(Aj, { snapshot: n }, `${n.name}:${n.capturedAt}:${n.captureMode}`)) })
  ] });
}
function Aj({ snapshot: e }) {
  const t = e.type?.displayName || e.type?.typeName || e.type?.alias || "Unknown", n = e.captureMode === "Payload";
  return /* @__PURE__ */ o.jsxs("article", { className: "wf-runtime-input", children: [
    /* @__PURE__ */ o.jsxs("header", { children: [
      /* @__PURE__ */ o.jsxs("span", { children: [
        /* @__PURE__ */ o.jsx("strong", { children: e.name }),
        /* @__PURE__ */ o.jsx("small", { children: t })
      ] }),
      /* @__PURE__ */ o.jsx("span", { className: "wf-runtime-capture-mode", children: Dj(e.captureMode) })
    ] }),
    n ? /* @__PURE__ */ o.jsx(_j, { payload: e.payload }) : /* @__PURE__ */ o.jsx("p", { children: e.captureReason || "The runtime capture policy did not include this input value." }),
    e.isSensitive ? /* @__PURE__ */ o.jsx("p", { className: "wf-instance-note", children: "Marked sensitive by runtime evidence." }) : null
  ] });
}
function _j({ payload: e }) {
  const t = $j(e);
  return t.length <= 160 && !t.includes(`
`) ? /* @__PURE__ */ o.jsx("code", { className: "wf-runtime-input-value", children: t }) : /* @__PURE__ */ o.jsxs("details", { className: "wf-runtime-input-value-details", children: [
    /* @__PURE__ */ o.jsx("summary", { children: Tj(t) }),
    /* @__PURE__ */ o.jsx("pre", { children: t })
  ] });
}
function Dj(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2");
}
function Tj(e) {
  const t = e.split(`
`, 1)[0] || e;
  return t.length > 120 ? `${t.slice(0, 117)}...` : t;
}
function $j(e) {
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
function Pj({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ o.jsx("h4", { children: "Incidents" }),
    e.length === 0 ? /* @__PURE__ */ o.jsx("p", { children: "No incidents recorded." }) : null,
    e.map((i) => /* @__PURE__ */ o.jsxs(
      "button",
      {
        type: "button",
        className: "wf-instance-incident",
        "data-severity": i.severity.toLowerCase(),
        "data-selected": i.incidentId === t,
        onClick: () => n?.(i.incidentId),
        children: [
          /* @__PURE__ */ o.jsx("strong", { children: i.failureType }),
          /* @__PURE__ */ o.jsxs("span", { children: [
            i.status,
            " · ",
            i.severity
          ] }),
          /* @__PURE__ */ o.jsx("p", { children: i.message })
        ]
      },
      i.incidentId
    ))
  ] });
}
function Mj({ details: e, graphNodeIds: t }) {
  if (!t) return null;
  const n = new Map(e.activities.map((s) => [s.activityExecutionId, s])), i = e.activities.filter((s) => !t.has(Dc(s))), r = e.incidents.filter((s) => {
    const a = s.activityExecutionId ? n.get(s.activityExecutionId) : null, c = s.executableNodeId ?? (a ? Dc(a) : "");
    return !c || !t.has(c);
  });
  return i.length === 0 && r.length === 0 ? null : /* @__PURE__ */ o.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ o.jsx("h4", { children: "Unmatched runtime evidence" }),
    /* @__PURE__ */ o.jsxs("div", { className: "wf-instance-unmatched-list", children: [
      i.map((s) => /* @__PURE__ */ o.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ o.jsx("strong", { children: Et(s.activityType) ?? s.activityType }),
        /* @__PURE__ */ o.jsx("small", { children: s.activityExecutionId })
      ] }, `activity-${s.activityExecutionId}`)),
      r.map((s) => /* @__PURE__ */ o.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ o.jsx("strong", { children: s.failureType }),
        /* @__PURE__ */ o.jsx("small", { children: s.incidentId })
      ] }, `incident-${s.incidentId}`))
    ] })
  ] });
}
function Rj({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: i,
  workflowDesignerPanels: r,
  autosaveEnabledByDefault: s
}) {
  const [a, c] = K(Bc);
  te(() => {
    const l = () => c(Bc());
    return window.addEventListener("popstate", l), () => window.removeEventListener("popstate", l);
  }, []);
  const u = (l) => {
    const d = l ? `/workflows/definitions?definition=${encodeURIComponent(l)}` : "/workflows/definitions";
    window.history.pushState({}, "", d), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return a ? /* @__PURE__ */ o.jsx(hj, { context: e, definitionId: a, ai: t, propertyEditors: n, expressionEditors: i, workflowDesignerPanels: r, autosaveEnabledByDefault: s, onBack: () => u(null) }) : /* @__PURE__ */ o.jsx(yr, { title: "Definitions", children: /* @__PURE__ */ o.jsx(mj, { context: e, ai: t, onOpen: u }) });
}
function zj({ context: e, ai: t }) {
  const [n, i] = K(Fc);
  te(() => {
    const s = () => i(Fc());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const r = se((s) => {
    const a = s?.trim() ?? "", c = new URL(window.location.href);
    a ? c.searchParams.set("definition", a) : c.searchParams.delete("definition"), i(a || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ o.jsx(yr, { title: "Executables", children: /* @__PURE__ */ o.jsx(gN, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: r }) });
}
function Lj({ context: e }) {
  return /* @__PURE__ */ o.jsx(yr, { title: "Runs", children: /* @__PURE__ */ o.jsx(Nj, { context: e }) });
}
function Vj({ context: e, ai: t }) {
  const n = Oj();
  return /* @__PURE__ */ o.jsx(yr, { title: "Run", children: /* @__PURE__ */ o.jsx(jj, { context: e, ai: t, workflowExecutionId: n }) });
}
function yr({ title: e, children: t }) {
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ o.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ o.jsxs("div", { children: [
      /* @__PURE__ */ o.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ o.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function Bc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Fc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Oj() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function Kj(e) {
  Sf(e.dialogs), e.featureAreas.add({
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
        component: () => /* @__PURE__ */ o.jsx(Rj, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list(), autosaveEnabledByDefault: e.runtime.workflows?.autosaveEnabledByDefault ?? !0 })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ o.jsx(zj, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => /* @__PURE__ */ o.jsx(Lj, { context: e.backend })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => /* @__PURE__ */ o.jsx(Vj, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => /* @__PURE__ */ o.jsx(Kh, { context: e.backend })
      }
    ]
  });
}
export {
  Yb as isConnectEndOverExistingWorkflowNode,
  Kj as register,
  Ub as resolveConnectEndSource
};
