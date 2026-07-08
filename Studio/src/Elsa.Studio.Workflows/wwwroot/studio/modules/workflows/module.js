import Qe, { useMemo as fe, useState as B, useEffect as G, memo as Ne, forwardRef as Tc, useRef as re, useCallback as ie, useContext as zn, createContext as Or, useLayoutEffect as Od, lazy as Hd, Suspense as Wd, useReducer as Fd, useId as $c } from "react";
import { ListChecks as Bd, Save as Pc, EyeOff as Is, Shield as As, AlertTriangle as Po, SlidersHorizontal as Hr, Activity as Mc, Search as ei, Check as on, Boxes as Vn, Zap as Kd, Play as Xt, Terminal as Xd, ListTree as Wr, GitBranch as Rc, Plus as Yt, Trash2 as In, AlertCircle as yt, Wrench as Yd, Copy as qd, X as Lc, Sparkles as at, RotateCcw as Fr, ChevronDown as zc, ChevronRight as Vt, GripVertical as Vc, Maximize2 as Mo, ChevronUp as Ud, Package as Oc, Undo2 as Zd, Redo2 as Gd, Network as Jd, Download as Qd, ChevronLeft as Ro, Minimize2 as _s, Workflow as Hc, Code2 as ef } from "lucide-react";
import { useQuery as Wc, useQueryClient as tf, useMutation as nf } from "@tanstack/react-query";
import { useTablistKeyboard as of } from "@elsa-workflows/studio-ui";
function rf(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var Ji = { exports: {} }, dn = {};
var Ds;
function sf() {
  if (Ds) return dn;
  Ds = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function n(o, i, s) {
    var a = null;
    if (s !== void 0 && (a = "" + s), i.key !== void 0 && (a = "" + i.key), "key" in i) {
      s = {};
      for (var c in i)
        c !== "key" && (s[c] = i[c]);
    } else s = i;
    return i = s.ref, {
      $$typeof: e,
      type: o,
      key: a,
      ref: i !== void 0 ? i : null,
      props: s
    };
  }
  return dn.Fragment = t, dn.jsx = n, dn.jsxs = n, dn;
}
var Ts;
function af() {
  return Ts || (Ts = 1, Ji.exports = sf()), Ji.exports;
}
var r = af();
let Fc;
function cf(e) {
  Fc = e;
}
function $s() {
  return Fc;
}
const lf = "String", uf = "singleline";
function df(e) {
  return e === "Single" || e === "Array" || e === "List" || e === "HashSet";
}
function Br(e, t = "Single") {
  return { alias: (e ?? "").trim() || lf, collectionKind: t };
}
function Bc(e) {
  const t = e.type ?? e.Type;
  if (Lo(t))
    return { alias: typeof t.alias == "string" ? t.alias : typeof t.typeName == "string" ? t.typeName : "", collectionKind: df(t.collectionKind) ? t.collectionKind : "Single" };
  const n = e.typeInformation ?? e.TypeInformation;
  return Lo(n) ? { alias: typeof n.typeName == "string" ? n.typeName : "", collectionKind: Ps(e) ? "Array" : "Single" } : { alias: typeof t == "string" ? t : "", collectionKind: Ps(e) ? "Array" : "Single" };
}
function Ps(e) {
  const t = e.isArray ?? e.IsArray;
  return t === !0 || t === "true";
}
function Ms(e) {
  return (e.alias ?? e.typeName ?? "").trim();
}
function ti() {
  return typeof crypto < "u" && typeof crypto.randomUUID == "function" ? crypto.randomUUID() : `id-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
}
function ff(e, t) {
  const n = new Set(t);
  let o = 1, i = `${e}${o}`;
  for (; n.has(i); )
    o += 1, i = `${e}${o}`;
  return i;
}
function pf(e) {
  return {
    referenceKey: ti(),
    name: e.name,
    type: Br(e.alias),
    storageDriverType: e.storageDriver?.trim() || null,
    default: null
  };
}
function hf(e, t) {
  return { ...e, ...t };
}
function gf(e) {
  return e === "" ? null : { value: e, expressionType: "Literal" };
}
function yf(e) {
  return e == null || e.value == null ? "" : typeof e.value == "object" ? JSON.stringify(e.value) : String(e.value);
}
function mf(e) {
  return {
    referenceKey: ti(),
    name: e.name,
    type: Br(e.alias),
    displayName: e.name,
    description: "",
    category: "",
    uiHint: uf,
    storageDriverType: e.storageDriver ?? null,
    isRequired: !1
  };
}
function xf(e, t) {
  return { ...e, ...t };
}
function wf(e) {
  return {
    referenceKey: ti(),
    name: e.name,
    type: Br(e.alias),
    displayName: e.name,
    description: "",
    category: ""
  };
}
function vf(e, t) {
  return { ...e, ...t };
}
function bf(e) {
  const t = e.split(",")[0].trim();
  return (t.split(".").pop() ?? t).split("`")[0];
}
function Kc(e, t) {
  return e && !e.includes(",") && !e.includes(".") ? e : bf(e || t);
}
function Nf(e, t) {
  return Kc(e, t).replace(/StorageDriver$/, "");
}
function Lo(e) {
  return !!e && typeof e == "object" && !Array.isArray(e);
}
function Sn(e, t) {
  for (const n of t) {
    const o = e[n];
    if (o != null && typeof o != "object") return String(o);
  }
  return "";
}
const jf = ["name", "Name"], Xc = ["name", "Name"], Sf = ["storageDriverType", "StorageDriverType"], Yc = ["referenceKey", "ReferenceKey"], Cf = /* @__PURE__ */ new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);
function On(e) {
  return qc(e, Af);
}
function ni(e) {
  return qc(e, _f);
}
function qc(e, t) {
  if (!e) return e;
  const n = { ...e };
  return e.rootActivity && (n.rootActivity = yr(e.rootActivity, t)), Array.isArray(e.variables) && (n.variables = jo(e.variables, mr)), Array.isArray(e.inputs) && (n.inputs = jo(e.inputs, mr)), Array.isArray(e.outputs) && (n.outputs = jo(e.outputs, (o) => Uc(o, !1))), n;
}
function yr(e, t) {
  const n = t(e), o = n.structure;
  if (!o || !et(o.payload)) return n;
  let i = !1;
  const s = { ...o.payload };
  for (const [a, c] of Object.entries(o.payload))
    Ls(c) ? (s[a] = yr(c, t), i = !0) : Array.isArray(c) && c.length > 0 && c.every(Ls) && (s[a] = c.map((u) => yr(u, t)), i = !0);
  return Array.isArray(o.payload.variables) && o.payload.variables.length > 0 && (s.variables = jo(o.payload.variables, mr), i = !0), i ? { ...n, structure: { ...o, payload: s } } : n;
}
function jo(e, t) {
  return e.map((n) => et(n) && !Array.isArray(n) ? t(n) : n);
}
function mr(e) {
  return Uc(e, !0);
}
const Ef = [
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
function Uc(e, t) {
  const n = kf(e, Ef);
  return Sn(e, Yc).trim() || (n.referenceKey = ti()), n.type = Bc(e), t && (n.storageDriverType = If(e.storageDriverType ?? e.StorageDriverType)), n;
}
function kf(e, t) {
  const n = new Set(t), o = {};
  for (const [i, s] of Object.entries(e))
    n.has(i) || (o[i] = s);
  return o;
}
function If(e) {
  if (typeof e == "string") return e.trim() ? e : null;
  if (et(e)) {
    const t = typeof e.typeName == "string" ? e.typeName : "";
    if (!t) return null;
    const n = typeof e.namespace == "string" ? e.namespace : "";
    return n ? `${n}.${t}` : t;
  }
  return null;
}
function Af(e) {
  const t = [], n = {};
  for (const [i, s] of Object.entries(e))
    Cf.has(i) || (Pf(s) ? t.push({
      referenceKey: Df(i),
      value: $f(s.expression)
    }) : n[i] = s);
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
function _f(e) {
  const t = Array.isArray(e.inputs) ? e.inputs : [], n = {};
  for (const o of t) {
    if (!et(o) || typeof o.referenceKey != "string") continue;
    const i = et(o.value) ? o.value : {};
    n[Tf(o.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof i.expressionType == "string" ? i.expressionType : "Literal",
        value: i.value ?? ""
      }
    };
  }
  return { ...e, ...n, inputs: [] };
}
function Df(e) {
  return e && e.charAt(0).toUpperCase() + e.slice(1);
}
function Tf(e) {
  return e && e.charAt(0).toLowerCase() + e.slice(1);
}
function $f(e) {
  const t = e.type || "Literal";
  return t === "Variable" && et(e.value) ? { value: e.value, expressionType: t } : t === "Literal" && et(e.value) ? { value: Rs(e.value), expressionType: "Object" } : { value: Rs(e.value), expressionType: t };
}
function Rs(e) {
  return e == null ? null : typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function Pf(e) {
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
const Hn = "elsa.sequence.structure", rn = "elsa.flowchart.structure";
function Zc(e, t, n) {
  if (!e) return null;
  let o = e;
  for (const i of t) {
    const s = Rf(o, i.ownerNodeId, n);
    if (!s) return null;
    o = s;
  }
  return o;
}
function Mf(e, t, n = (i) => i.nodeId, o) {
  if (!e) return null;
  if (e.nodeId === t) return [];
  const i = (s, a) => {
    const c = Me(s, o);
    for (const u of c) {
      if (!u.activities.some((d) => d.nodeId === t)) continue;
      const l = a.at(-1);
      return l ? [...a.slice(0, -1), { ...l, slotId: u.id }] : u.id === c[0]?.id ? a : null;
    }
    for (const u of c)
      for (const l of u.activities) {
        const f = Me(l, o)[0]?.id ?? u.id, p = i(l, [...a, { ownerNodeId: l.nodeId, slotId: f, label: n(l) }]);
        if (p) return p;
      }
    return null;
  };
  return i(e, []);
}
function Gc(e, t, n) {
  const o = Me(e, n), i = t.at(-1);
  return i ? o.find((s) => s.id === i.slotId) ?? null : o[0] ?? null;
}
function qt(e, t, n) {
  const o = Zc(e, t, n);
  if (!o) return null;
  const i = Gc(o, t, n);
  return i ? { owner: o, slot: i } : null;
}
function Jc(e, t, n) {
  for (const o of Me(e, n)) {
    const i = o.activities.find((s) => s.nodeId === t);
    if (i) return { slot: o, child: i };
  }
  return null;
}
function Rf(e, t, n) {
  return Jc(e, t, n)?.child ?? null;
}
function Me(e, t) {
  const n = e.structure;
  if (!n || !n.payload || typeof n.payload != "object") return [];
  const o = n.payload, i = Kr(Qc(e, t));
  if (i?.kind === n.kind) return Vf(n, i);
  const s = sp(n), a = gn(o.activities);
  return a ? [{
    id: `${n.kind}:activities`,
    label: ap(n),
    property: "activities",
    cardinality: "many",
    mode: s,
    activities: a
  }] : Object.entries(o).filter(([, c]) => gn(c) || Vo(c)).map(([c, u]) => ({
    id: `${n.kind}:${c}`,
    label: lp(c),
    property: c,
    cardinality: gn(u) ? "many" : "single",
    mode: "generic",
    activities: gn(u) ?? (Vo(u) ? [u] : [])
  }));
}
function Kr(e) {
  for (const t of e?.designFacets ?? []) {
    if (!tt(t)) continue;
    const n = typeof t.kind == "string" ? t.kind : "", o = typeof t.schemaVersion == "string" ? t.schemaVersion : "";
    if (!n || !o || !tt(t.payload)) continue;
    const i = Lf(t.payload);
    if (i) return { kind: n, schemaVersion: o, payload: i };
  }
  return null;
}
function Lf(e) {
  const t = e.mode;
  if (t !== "sequence" && t !== "flowchart" && t !== "generic" || typeof e.supportsScopedVariables != "boolean" || !Array.isArray(e.slots) || !tt(e.initialPayload)) return null;
  const n = e.slots.map(zf).filter((o) => o !== null);
  return {
    mode: t,
    supportsScopedVariables: e.supportsScopedVariables,
    slots: n,
    initialPayload: e.initialPayload
  };
}
function zf(e) {
  if (!tt(e)) return null;
  const t = typeof e.name == "string" ? e.name : "", n = typeof e.property == "string" ? e.property : "", o = typeof e.displayName == "string" ? e.displayName : "", i = e.cardinality;
  return !t || !n || !o || i !== "single" && i !== "many" ? null : {
    name: t,
    property: n,
    displayName: o,
    cardinality: i,
    collectionProperty: Ot(e.collectionProperty),
    childProperty: Ot(e.childProperty),
    labelProperty: Ot(e.labelProperty),
    slotNameTemplate: Ot(e.slotNameTemplate)
  };
}
function Vf(e, t) {
  return t.payload.slots.flatMap((n) => {
    if (n.collectionProperty && n.childProperty) {
      const o = n.childProperty, i = e.payload[n.collectionProperty];
      return Array.isArray(i) ? i.flatMap((s, a) => {
        if (!tt(s)) return [];
        const c = n.labelProperty ? Ot(s[n.labelProperty]) : void 0;
        return [zs(e.kind, n, s[o], t.payload.mode, a, c)];
      }) : [];
    }
    return [zs(e.kind, n, e.payload[n.property], t.payload.mode)];
  });
}
function zs(e, t, n, o, i, s) {
  const a = i === void 0 ? "" : `:${t.collectionProperty}:${t.childProperty}:${i}`;
  return {
    id: `${e}:${t.property}${a}`,
    label: i === void 0 ? t.displayName : Hf(t, i, s),
    property: t.property,
    cardinality: t.cardinality,
    mode: o,
    activities: Of(n, t.cardinality),
    collectionProperty: t.collectionProperty,
    childProperty: t.childProperty,
    labelProperty: t.labelProperty,
    slotNameTemplate: t.slotNameTemplate,
    collectionIndex: i,
    collectionItemLabel: s
  };
}
function Of(e, t) {
  return t === "many" ? gn(e) ?? [] : Vo(e) ? [e] : [];
}
function Hf(e, t, n) {
  return e.slotNameTemplate ? e.slotNameTemplate.replaceAll("{name}", e.name).replaceAll("{displayName}", e.displayName).replaceAll("{label}", n ?? String(t + 1)).replaceAll("{index}", String(t + 1)) : n ? `${e.displayName}: ${n}` : `${e.displayName} ${t + 1}`;
}
function Qc(e, t) {
  if (t)
    return t instanceof Map ? t.get(e.activityVersionId) : Array.isArray(t) ? t.find((n) => n.activityVersionId === e.activityVersionId) : t.activityVersionId === e.activityVersionId ? t : void 0;
}
function Wf(e, t) {
  if (t.labelProperty && t.collectionItemLabel) {
    const n = e.map((o, i) => tt(o) && Ot(o[t.labelProperty]) === t.collectionItemLabel ? i : -1).filter((o) => o >= 0);
    if (n.length === 1) return n[0];
  }
  return typeof t.collectionIndex == "number" && t.collectionIndex >= 0 && t.collectionIndex < e.length ? t.collectionIndex : -1;
}
function el(e, t, n) {
  const o = new Map(t.map((a) => [a.activityVersionId, a])), i = new Map(n.map((a) => [a.nodeId, a])), s = e.slot.activities.map((a, c) => {
    const u = o.get(a.activityVersionId), l = i.get(a.nodeId) ?? cp(e.slot.mode, c);
    return ol(a, u, { x: l.x, y: l.y });
  });
  return {
    nodes: s,
    edges: e.slot.mode === "flowchart" ? rl(e.owner) : Qf(e.slot, s)
  };
}
function xr(e, t, n) {
  const o = t.find((s) => s.activityVersionId === e.activityVersionId), i = n.find((s) => s.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [ol(e, o, { x: i.x, y: i.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function Ff(e, t, n, o = null) {
  const i = new Map(t.map((c) => [c.activityExecutionId, c])), s = Hs(t, (c) => c.authoredActivityId || c.executableNodeId), a = Hs(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? i.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const u = s.get(c.id) ?? [], l = a.get(c.id) ?? [];
    if (u.length === 0 && l.length === 0) return c;
    const d = al(u), f = o === c.id || u.some((h) => h.activityExecutionId === o) || l.some((h) => h.incidentId === o), p = {
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
function Xr(e, t) {
  if (e?.structure?.kind === rn || Uf(t)) return "flowchart";
  if (e?.structure?.kind === Hn || Zf(t)) return "sequence";
  if (e) {
    const n = Me(e, t)[0];
    if (n) return n.mode === "flowchart" ? "flowchart" : "sequence";
  }
  return "unsupported";
}
function Vs(e, t, n, o) {
  return Yr(e, t, o, (i) => {
    const s = Gc(i, t, o);
    return s ? Wn(i, s, n) : i;
  });
}
function Bf(e, t, n, o) {
  return t.length === 0 ? n : Yr(e, t, o, () => n);
}
function Yr(e, t, n, o) {
  if (t.length === 0) return o(e);
  const [i, ...s] = t, a = Jc(e, i.ownerNodeId, n);
  if (!a) return e;
  const c = Yr(a.child, s, n, o);
  if (c === a.child) return e;
  const u = a.slot.activities.map((l) => l.nodeId === i.ownerNodeId ? c : l);
  return Wn(e, a.slot, u);
}
function tl(e, t, n, o) {
  if (e.nodeId === t) return n(e);
  const i = Me(e, o);
  if (i.length === 0) return e;
  let s = !1, a = e;
  for (const c of i) {
    const u = c.activities.map((l) => {
      const d = tl(l, t, n, o);
      return d !== l && (s = !0), d;
    });
    s && (a = Wn(a, c, u));
  }
  return s ? a : e;
}
function Wn(e, t, n) {
  if (!e.structure) return e;
  const o = t.cardinality === "single" ? n[0] ?? null : n;
  if (t.collectionProperty && t.childProperty) {
    const i = e.structure.payload[t.collectionProperty];
    if (!Array.isArray(i)) return e;
    const s = Wf(i, t);
    if (s < 0) return e;
    const a = i[s];
    if (!tt(a)) return e;
    const c = [...i];
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
function Kf(e, t, n, o = []) {
  const i = new Map(e.slot.activities.map((a) => [a.nodeId, a]));
  for (const a of o)
    i.set(a.nodeId, a);
  const s = t.map((a) => i.get(a.id)).filter((a) => !!a);
  return e.slot.mode === "sequence" && s.sort((a, c) => {
    const u = t.find((d) => d.id === a.nodeId), l = t.find((d) => d.id === c.nodeId);
    return (u?.position.x ?? 0) - (l?.position.x ?? 0);
  }), Wn(e.owner, e.slot, s);
}
function Xf(e, t) {
  return {
    ...e,
    structure: Jf(e.structure, t)
  };
}
function Yf(e, t) {
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
function wr(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: il(e)
  };
}
function nl(e, t) {
  if (!e) return null;
  const n = Qc(e, t), o = e.structure ?? (n ? il(n) : null);
  let i = o === e.structure ? e : { ...e, structure: o };
  const s = Me(i, t);
  for (const a of s) {
    const c = a.activities.map((u) => nl(u, t) ?? u);
    c.some((u, l) => u !== a.activities[l]) && (i = Wn(i, a, c));
  }
  return i;
}
function Se(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? Gf(t) : n;
}
function ol(e, t, n, o = {}) {
  return {
    id: e.nodeId,
    type: "workflowActivity",
    position: n,
    connectable: o.connectable,
    deletable: o.deletable,
    draggable: o.draggable,
    data: {
      label: t ? Se(t) : e.activityVersionId,
      activityVersionId: e.activityVersionId,
      activityTypeKey: t?.activityTypeKey,
      category: t?.category,
      executionType: t?.executionType,
      icon: oi(t),
      childSlots: Me(e, t),
      acceptsInbound: ep(e, t),
      sourcePorts: o.suppressFlowPorts ? [] : sl(e, t),
      suppressFlowPorts: o.suppressFlowPorts
    }
  };
}
function oi(e) {
  if (!e) return "activity";
  const t = qf(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), o = Se(e).toLowerCase(), i = e.category?.toLowerCase() ?? "", s = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || o === "flowchart" ? "flowchart" : n.endsWith(".sequence") || o === "sequence" ? "sequence" : n.includes("writeline") || o.includes("write line") ? "terminal" : i.includes("runtime") ? "runtime" : s === "trigger" ? "trigger" : "activity";
}
function qf(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function Uf(e) {
  return !!e && (Se(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function Zf(e) {
  return !!e && (Se(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function Gf(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function il(e) {
  const t = Kr(e);
  return t ? {
    kind: t.kind,
    schemaVersion: t.schemaVersion,
    payload: ip(t.payload.initialPayload)
  } : e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: Hn,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: rn,
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
function Jf(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], o = /* @__PURE__ */ new Map();
  for (const i of n) {
    if (!tt(i)) continue;
    const s = i.id;
    typeof s == "string" && o.set(s, i);
  }
  return {
    ...e,
    payload: {
      ...e.payload,
      connections: t.map((i) => {
        const s = o.get(i.id) ?? {}, a = i.data?.vertices, { vertices: c, ...u } = s;
        return {
          ...u,
          id: i.id,
          source: { nodeId: i.source, port: i.sourceHandle ?? "Done" },
          target: i.targetHandle ? { nodeId: i.target, port: i.targetHandle } : { nodeId: i.target },
          ...a?.length ? { vertices: a.map((l) => ({ x: Math.round(l.x), y: Math.round(l.y) })) } : {}
        };
      })
    }
  };
}
function Qf(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function rl(e) {
  if (e.structure?.kind !== rn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const i = n.source, s = n.target;
    if (!i?.nodeId || !s?.nodeId) return null;
    const a = Array.isArray(n.vertices) ? n.vertices.filter(rp) : [];
    return {
      id: typeof n.id == "string" ? String(n.id) : `flow-${o}-${i.nodeId}-${s.nodeId}`,
      source: i.nodeId,
      target: s.nodeId,
      sourceHandle: i.port,
      targetHandle: s.port && s.port !== "Done" ? s.port : void 0,
      type: "workflow",
      label: i.port && i.port !== "Done" ? i.port : void 0,
      data: a.length ? { vertices: a } : void 0
    };
  }).filter((n) => n !== null) : [];
}
function sl(e, t) {
  const n = Os(e.cases);
  if (np(e, t) && n.length > 0)
    return [...n.map((s) => ({ name: s, displayName: s })), { name: "Default", displayName: "Default" }];
  const o = [
    ...So(t?.designFacets),
    ...So(t?.ports),
    ...So(t?.outputs)
  ];
  if (o.length > 0) return op(o);
  const i = Os(e.outcomes);
  return i.length > 0 ? i.map((s) => ({ name: s, displayName: s })) : [{ name: "Done", displayName: "Done" }];
}
function ep(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function zo(e, t, n, o) {
  const i = n ?? "Done";
  return {
    id: `flow-${e}-${t}-${i}-${crypto.randomUUID().slice(0, 8)}`,
    source: e,
    target: t,
    sourceHandle: i,
    targetHandle: o ?? void 0,
    type: "workflow",
    label: i !== "Done" ? i : void 0
  };
}
function tp(e, t, n) {
  const o = zo(t.source, n, t.sourceHandle ?? "Done", void 0), i = zo(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((s) => s.id !== t.id).concat(o, i);
}
function gn(e) {
  return Array.isArray(e) ? e.filter(Vo) : null;
}
function np(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function So(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!tt(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...So(n.ports));
      continue;
    }
    const o = typeof n.type == "string" ? n.type : typeof n.portType == "string" ? n.portType : "", i = n.isBrowsable !== !1 && n.browsable !== !1, s = typeof n.name == "string" ? n.name : typeof n.id == "string" ? n.id : "";
    if (i && o.toLowerCase() === "flow" && s) {
      const a = typeof n.displayName == "string" ? n.displayName : s;
      t.push({ name: s, displayName: a });
    }
  }
  return t;
}
function op(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function Os(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function Ot(e) {
  return typeof e == "string" && e.length > 0 ? e : void 0;
}
function ip(e) {
  return JSON.parse(JSON.stringify(e));
}
function Hs(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const o of e) {
    const i = t(o);
    i && n.set(i, [...n.get(i) ?? [], o]);
  }
  return n;
}
function al(e) {
  return [...e].sort((t, n) => Ws(n).localeCompare(Ws(t)))[0];
}
function Ws(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function rp(e) {
  return tt(e) && typeof e.x == "number" && typeof e.y == "number";
}
function tt(e) {
  return typeof e == "object" && e !== null;
}
function Vo(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function sp(e) {
  return e.kind === Hn ? "sequence" : e.kind === rn ? "flowchart" : "generic";
}
function ap(e) {
  return e.kind === Hn || e.kind === rn, "Activities";
}
function cp(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function lp(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Oo = "workflow", up = /* @__PURE__ */ new Set([Hn, rn]);
function dp(e, t) {
  const n = e?.structure?.kind;
  if (!n) return !1;
  if (up.has(n)) return !0;
  const o = t?.activityVersionId === e?.activityVersionId ? Kr(t) : null;
  return o?.kind === n && o.payload.supportsScopedVariables;
}
function cl(e) {
  const t = e?.structure?.payload?.variables;
  return Array.isArray(t) ? t.filter(Lo) : [];
}
function fp(e, t) {
  return e.structure ? {
    ...e,
    structure: {
      ...e.structure,
      payload: { ...e.structure.payload, variables: t }
    }
  } : e;
}
function pp(e, t) {
  return {
    referenceKey: e,
    declaringScopeId: t && t !== Oo ? t : Oo
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
function hp(e, t) {
  if (!e) return "";
  const n = [`workflow:${Fs(e.variables)}`], o = (i) => {
    const s = Me(i, t), a = s.flatMap((c) => c.activities.map((u) => u.nodeId));
    n.push(`${i.nodeId}:${Fs(cl(i))}>${a.join(",")}`), s.forEach((c) => c.activities.forEach(o));
  };
  return e.rootActivity && o(e.rootActivity), n.join(";");
}
function Fs(e) {
  return (e ?? []).map((t) => `${t.referenceKey}=${t.name}`).join(",");
}
function gp(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const o of e ?? [])
    o.scopeId === t && n.set(o.referenceKey, `Shadows "${o.name}" declared in an outer scope.`);
  return n;
}
const je = "/_elsa/workflow-management", yp = "/publishing", Cn = {
  activities: ["workflows", "activities"],
  activityAvailabilitySettings: ["workflows", "activity-availability", "settings"],
  activityAvailabilityDiagnostics: ["workflows", "activity-availability", "diagnostics"]
};
function mp(e) {
  return Wc({
    queryKey: Cn.activityAvailabilitySettings,
    queryFn: () => Rp(e)
  });
}
function xp(e) {
  return Wc({
    queryKey: Cn.activityAvailabilityDiagnostics,
    queryFn: () => fl(e)
  });
}
function wp(e) {
  const t = tf();
  return nf({
    mutationFn: (n) => Lp(e, n),
    onSuccess: () => {
      t.invalidateQueries({ queryKey: Cn.activityAvailabilitySettings }), t.invalidateQueries({ queryKey: Cn.activityAvailabilityDiagnostics }), t.invalidateQueries({ queryKey: Cn.activities });
    }
  });
}
async function vp(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${je}/definitions?${n.toString()}`);
}
async function bp(e, t) {
  const n = await e.http.getJson(`${je}/definitions/${encodeURIComponent(t)}`);
  return n.draft ? { ...n, draft: { ...n.draft, state: ni(n.draft.state) } } : n;
}
async function Np(e, t, n) {
  const o = await e.http.postJson(
    `${je}/design/scoped-variables/analyze`,
    { state: On(t), nodeId: n }
  );
  return {
    visibleVariables: Array.isArray(o?.visibleVariables) ? o.visibleVariables : [],
    shadowingWarnings: Array.isArray(o?.shadowingWarnings) ? o.shadowingWarnings : []
  };
}
const Qi = (e) => ({ visibleVariables: [], shadowingWarnings: [], status: e });
function jp(e, t, n, o) {
  const i = fe(() => hp(t, o), [o, t]), [s, a] = B(() => Qi("loading"));
  return G(() => {
    if (!t) {
      a(Qi("unavailable"));
      return;
    }
    let c = !1;
    return a((u) => ({ ...u, status: "loading" })), Np(e, t, n).then(
      (u) => {
        c || a({ ...u, status: "ready" });
      },
      () => {
        c || a(Qi("unavailable"));
      }
    ), () => {
      c = !0;
    };
  }, [e, n, i]), s;
}
async function Sp(e, t) {
  const n = await e.http.getJson(`${je}/versions/${encodeURIComponent(t)}`);
  return { ...n, state: ni(n.state) };
}
async function Cp(e, t) {
  return e.http.postJson(`${je}/definitions`, t);
}
async function Ep(e, t) {
  await e.http.deleteJson(`${je}/definitions/${encodeURIComponent(t)}`);
}
async function kp(e, t) {
  await e.http.postJson(`${je}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function Ip(e, t) {
  await e.http.deleteJson(`${je}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function Ap(e, t, n) {
  return e.http.requestJson(
    `${je}/definitions/${encodeURIComponent(t)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(n)
    }
  );
}
async function _p(e, t) {
  const n = await e.http.putJson(
    `${je}/drafts/${encodeURIComponent(t.id)}`,
    { state: On(t.state), layout: t.layout }
  );
  return { ...n, state: ni(n.state) };
}
async function Dp(e, t) {
  return e.http.postJson(`${je}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function Tp(e, t) {
  return e.http.postJson(`${je}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function $p(e, t) {
  const n = { ...t, state: On(t.state) };
  try {
    return await e.http.postJson(`${yp}/workflows/drafts/test-runs`, n);
  } catch (o) {
    const i = Bp(o);
    if (i) return i;
    throw o;
  }
}
async function ul(e, t) {
  return e.http.postJson(`${je}/executables/${encodeURIComponent(t)}/run`, {});
}
async function dl(e) {
  return e.http.getJson("/_demo/workflows/executables");
}
async function Pp(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.runKind && n.set("runKind", t.runKind), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const o = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${o ? `?${o}` : ""}`);
}
async function Mp(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function qr(e) {
  return e.http.getJson(`${je}/activities`);
}
async function Rp(e) {
  return e.http.getJson(`${je}/activities/availability/settings`);
}
async function Lp(e, t) {
  return e.http.putJson(`${je}/activities/availability/settings`, t);
}
async function fl(e) {
  return e.http.getJson(`${je}/activities/availability/diagnostics`);
}
async function zp(e) {
  const t = await ii(e, [
    `${je}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? Bs(t) : Bs(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function Vp(e) {
  const t = await ii(e, [
    `${je}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : Co;
}
async function Op(e) {
  const t = await ii(e, [
    `${je}/descriptors/variables`,
    "/descriptors/variables"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((o) => Hp(o));
}
function Hp(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = typeof t.alias == "string" && t.alias.length > 0, o = typeof t.typeName == "string" && t.typeName.length > 0;
  return n || o;
}
async function Wp(e) {
  const t = await ii(e, [
    `${je}/descriptors/storage-drivers`,
    "/descriptors/storage-drivers"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((o) => Fp(o));
}
function Fp(e) {
  return !!e && typeof e == "object" && typeof e.typeName == "string" && e.typeName.length > 0;
}
async function ii(e, t) {
  let n;
  for (const o of t)
    try {
      return await e.http.getJson(o);
    } catch (i) {
      n = i;
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
function Bp(e) {
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
const Co = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
], Kp = [
  "Available",
  "BlockedByHostBaseline",
  "HiddenByManagementSettings",
  "RemovedFromCatalog",
  "UnresolvedReference"
], Xp = {
  Available: "Available",
  BlockedByHostBaseline: "Host blocked",
  HiddenByManagementSettings: "Management hidden",
  RemovedFromCatalog: "Removed",
  UnresolvedReference: "Unresolved"
};
function vt(e) {
  return typeof e == "string" ? e : typeof e == "number" ? Kp[e] ?? "Available" : "Available";
}
function Ho(e) {
  const t = vt(e);
  return Xp[t] ?? t;
}
function Yp(e) {
  return vt(e).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function qp(e) {
  return vt(e) !== "Available";
}
function Up(e) {
  return e === "Only" || e === 1 ? "Only" : "AllExcept";
}
function Zp(e) {
  return e === "Only" ? 1 : 0;
}
function Xs(e) {
  const t = e?.rules;
  return {
    mode: Up(e?.mode),
    activityTypes: t?.activityTypes ?? [],
    sets: t?.sets ?? []
  };
}
function Gp(e) {
  return e.referenceKind === 0 || e.referenceKind === "ActivityType";
}
function Jp(e) {
  return [...e?.items ?? []].filter(Gp).filter((t) => t.activityTypeKey && t.activityDefinitionId).sort((t, n) => Wo(t).localeCompare(Wo(n)));
}
function Qp(e) {
  return [...e?.items ?? []].filter((t) => {
    const n = vt(t.state);
    return n === "RemovedFromCatalog" || n === "UnresolvedReference";
  }).sort((t, n) => (t.referenceName ?? "").localeCompare(n.referenceName ?? ""));
}
function Ys(e, t) {
  return e.includes(t) ? e.filter((n) => n !== t) : [...e, t].sort((n, o) => n.localeCompare(o));
}
function Wo(e) {
  const t = e?.displayName?.trim();
  if (t) return t;
  const n = e?.activityTypeKey?.split(".").filter(Boolean).at(-1) ?? "";
  return eh(n) || e?.activityTypeKey || "Activity";
}
function eh(e) {
  return e.replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").replace(/([a-z0-9])([A-Z])/g, "$1 $2").trim();
}
function th(e, t) {
  const n = new Set(e.filter((o) => !!o));
  return (t?.items ?? []).find((o) => qp(o.state) ? [o.activityDefinitionId, o.activityTypeKey, o.referenceName].some((i) => i && n.has(i)) : !1) ?? null;
}
function nh({ context: e }) {
  const t = mp(e), n = xp(e), o = wp(e), i = t.data ?? null, s = n.data ?? null, a = t.isLoading || n.isLoading, c = o.isPending, [u, l] = B(() => Xs(i)), [d, f] = B(""), [p, h] = B(null);
  G(() => {
    l(Xs(i));
  }, [i]);
  const y = fe(() => Jp(s), [s]), m = fe(() => Qp(s), [s]), x = s?.sets ?? [], w = fe(() => {
    const _ = d.trim().toLowerCase();
    return _ ? y.filter(
      (R) => Wo(R).toLowerCase().includes(_) || (R.activityTypeKey ?? "").toLowerCase().includes(_)
    ) : y;
  }, [y, d]), b = new Set(u.activityTypes), g = new Set(u.sets), v = y.filter((_) => vt(_.state) === "BlockedByHostBaseline").length, j = y.filter((_) => vt(_.state) === "HiddenByManagementSettings").length, N = o.error ?? t.error ?? n.error, S = N instanceof Error ? N.message : N ? "Activity availability could not be loaded." : null, k = (_) => l((R) => ({ ...R, mode: _ })), D = (_) => l((R) => ({ ...R, activityTypes: Ys(R.activityTypes, _) })), L = (_) => l((R) => ({ ...R, sets: Ys(R.sets, _) })), A = () => {
    h(null), o.mutate(
      {
        scope: i?.scope ?? "host-default",
        mode: Zp(u.mode),
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
          /* @__PURE__ */ r.jsx(Bd, { size: 18 }),
          " Activity availability"
        ] }),
        /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Control which activities can be added to new workflows. Host baseline rules always take precedence." })
      ] }),
      /* @__PURE__ */ r.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ r.jsxs("button", { type: "button", className: "availability-save", onClick: A, disabled: a || c, children: [
        /* @__PURE__ */ r.jsx(Pc, { size: 15 }),
        c ? "Saving…" : "Save"
      ] }) })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "availability-body", children: [
      S && /* @__PURE__ */ r.jsx("div", { className: "availability-banner availability-banner-error", children: S }),
      p && !S && /* @__PURE__ */ r.jsx("div", { className: "availability-banner availability-banner-success", children: p }),
      /* @__PURE__ */ r.jsxs("div", { className: "availability-mode", role: "group", "aria-label": "Activity availability mode", children: [
        /* @__PURE__ */ r.jsxs("button", { type: "button", className: u.mode === "AllExcept" ? "active" : "", onClick: () => k("AllExcept"), disabled: a || c, children: [
          /* @__PURE__ */ r.jsx(Is, { size: 15 }),
          /* @__PURE__ */ r.jsxs("span", { children: [
            /* @__PURE__ */ r.jsx("strong", { children: "All except" }),
            /* @__PURE__ */ r.jsx("em", { children: "Show everything except the selected activities" })
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", className: u.mode === "Only" ? "active" : "", onClick: () => k("Only"), disabled: a || c, children: [
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
          /* @__PURE__ */ r.jsx(Is, { size: 14 }),
          " ",
          j,
          " management hidden"
        ] }),
        /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx(Po, { size: 14 }),
          " ",
          m.length,
          " unresolved"
        ] })
      ] }),
      x.length > 0 && /* @__PURE__ */ r.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ r.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ r.jsx(Hr, { size: 14 }),
          " Sets"
        ] }),
        /* @__PURE__ */ r.jsx("div", { className: "availability-set-list", children: x.map((_) => /* @__PURE__ */ r.jsxs("label", { className: "availability-set-option", children: [
          /* @__PURE__ */ r.jsx("input", { type: "checkbox", checked: g.has(_.name), disabled: a || c, onChange: () => L(_.name) }),
          /* @__PURE__ */ r.jsx("span", { children: _.name }),
          /* @__PURE__ */ r.jsx("code", { children: (_.activityTypeKeys ?? []).length })
        ] }, _.name)) })
      ] }),
      /* @__PURE__ */ r.jsxs("section", { className: "availability-section availability-section-grow", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "availability-section-head", children: [
          /* @__PURE__ */ r.jsxs("h3", { className: "wf-section-label", children: [
            /* @__PURE__ */ r.jsx(Mc, { size: 14 }),
            " Activities"
          ] }),
          /* @__PURE__ */ r.jsxs("div", { className: "wf-search availability-search", children: [
            /* @__PURE__ */ r.jsx(ei, { size: 14 }),
            /* @__PURE__ */ r.jsx("input", { type: "search", value: d, placeholder: "Filter activities…", onChange: (_) => f(_.target.value) })
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("div", { className: "availability-activity-list", children: [
          a && y.length === 0 && /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Loading availability…" }),
          !a && y.length === 0 && /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No availability diagnostics reported." }),
          !a && y.length > 0 && w.length === 0 && /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No activities match the filter." }),
          w.map((_) => {
            const C = vt(_.state) === "BlockedByHostBaseline", I = _.activityTypeKey ?? _.activityDefinitionId ?? "";
            return /* @__PURE__ */ r.jsxs("label", { className: `availability-activity-option ${C ? "disabled" : ""}`, children: [
              /* @__PURE__ */ r.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: b.has(I),
                  disabled: a || c || C,
                  onChange: () => D(I)
                }
              ),
              /* @__PURE__ */ r.jsxs("span", { className: "availability-activity-main", children: [
                /* @__PURE__ */ r.jsx("strong", { children: Wo(_) }),
                /* @__PURE__ */ r.jsx("code", { children: _.activityTypeKey })
              ] }),
              /* @__PURE__ */ r.jsx("em", { className: `availability-state ${Yp(_.state)}`, children: Ho(_.state) })
            ] }, I);
          })
        ] })
      ] }),
      m.length > 0 && /* @__PURE__ */ r.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ r.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ r.jsx(Po, { size: 14 }),
          " Unresolved references"
        ] }),
        /* @__PURE__ */ r.jsx("div", { className: "availability-unresolved-list", children: m.map((_) => /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx("strong", { children: _.referenceName }),
          /* @__PURE__ */ r.jsx("em", { children: Ho(_.state) })
        ] }, `${_.layer}-${_.referenceKind}-${_.referenceName}`)) })
      ] })
    ] })
  ] });
}
function Ce(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, o; n < e.length; n++)
      (o = Ce(e[n])) !== "" && (t += (t && " ") + o);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var oh = { value: () => {
} };
function ri() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new Eo(n);
}
function Eo(e) {
  this._ = e;
}
function ih(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", i = n.indexOf(".");
    if (i >= 0 && (o = n.slice(i + 1), n = n.slice(0, i)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
Eo.prototype = ri.prototype = {
  constructor: Eo,
  on: function(e, t) {
    var n = this._, o = ih(e + "", n), i, s = -1, a = o.length;
    if (arguments.length < 2) {
      for (; ++s < a; ) if ((i = (e = o[s]).type) && (i = rh(n[i], e.name))) return i;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++s < a; )
      if (i = (e = o[s]).type) n[i] = qs(n[i], e.name, t);
      else if (t == null) for (i in n) n[i] = qs(n[i], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new Eo(e);
  },
  call: function(e, t) {
    if ((i = arguments.length - 2) > 0) for (var n = new Array(i), o = 0, i, s; o < i; ++o) n[o] = arguments[o + 2];
    if (!this._.hasOwnProperty(e)) throw new Error("unknown type: " + e);
    for (s = this._[e], o = 0, i = s.length; o < i; ++o) s[o].value.apply(t, n);
  },
  apply: function(e, t, n) {
    if (!this._.hasOwnProperty(e)) throw new Error("unknown type: " + e);
    for (var o = this._[e], i = 0, s = o.length; i < s; ++i) o[i].value.apply(t, n);
  }
};
function rh(e, t) {
  for (var n = 0, o = e.length, i; n < o; ++n)
    if ((i = e[n]).name === t)
      return i.value;
}
function qs(e, t, n) {
  for (var o = 0, i = e.length; o < i; ++o)
    if (e[o].name === t) {
      e[o] = oh, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var vr = "http://www.w3.org/1999/xhtml";
const Us = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: vr,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function si(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Us.hasOwnProperty(t) ? { space: Us[t], local: e } : e;
}
function sh(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === vr && t.documentElement.namespaceURI === vr ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function ah(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function pl(e) {
  var t = si(e);
  return (t.local ? ah : sh)(t);
}
function ch() {
}
function Ur(e) {
  return e == null ? ch : function() {
    return this.querySelector(e);
  };
}
function lh(e) {
  typeof e != "function" && (e = Ur(e));
  for (var t = this._groups, n = t.length, o = new Array(n), i = 0; i < n; ++i)
    for (var s = t[i], a = s.length, c = o[i] = new Array(a), u, l, d = 0; d < a; ++d)
      (u = s[d]) && (l = e.call(u, u.__data__, d, s)) && ("__data__" in u && (l.__data__ = u.__data__), c[d] = l);
  return new He(o, this._parents);
}
function uh(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function dh() {
  return [];
}
function hl(e) {
  return e == null ? dh : function() {
    return this.querySelectorAll(e);
  };
}
function fh(e) {
  return function() {
    return uh(e.apply(this, arguments));
  };
}
function ph(e) {
  typeof e == "function" ? e = fh(e) : e = hl(e);
  for (var t = this._groups, n = t.length, o = [], i = [], s = 0; s < n; ++s)
    for (var a = t[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && (o.push(e.call(u, u.__data__, l, a)), i.push(u));
  return new He(o, i);
}
function gl(e) {
  return function() {
    return this.matches(e);
  };
}
function yl(e) {
  return function(t) {
    return t.matches(e);
  };
}
var hh = Array.prototype.find;
function gh(e) {
  return function() {
    return hh.call(this.children, e);
  };
}
function yh() {
  return this.firstElementChild;
}
function mh(e) {
  return this.select(e == null ? yh : gh(typeof e == "function" ? e : yl(e)));
}
var xh = Array.prototype.filter;
function wh() {
  return Array.from(this.children);
}
function vh(e) {
  return function() {
    return xh.call(this.children, e);
  };
}
function bh(e) {
  return this.selectAll(e == null ? wh : vh(typeof e == "function" ? e : yl(e)));
}
function Nh(e) {
  typeof e != "function" && (e = gl(e));
  for (var t = this._groups, n = t.length, o = new Array(n), i = 0; i < n; ++i)
    for (var s = t[i], a = s.length, c = o[i] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new He(o, this._parents);
}
function ml(e) {
  return new Array(e.length);
}
function jh() {
  return new He(this._enter || this._groups.map(ml), this._parents);
}
function Fo(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
Fo.prototype = {
  constructor: Fo,
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
function Sh(e) {
  return function() {
    return e;
  };
}
function Ch(e, t, n, o, i, s) {
  for (var a = 0, c, u = t.length, l = s.length; a < l; ++a)
    (c = t[a]) ? (c.__data__ = s[a], o[a] = c) : n[a] = new Fo(e, s[a]);
  for (; a < u; ++a)
    (c = t[a]) && (i[a] = c);
}
function Eh(e, t, n, o, i, s, a) {
  var c, u, l = /* @__PURE__ */ new Map(), d = t.length, f = s.length, p = new Array(d), h;
  for (c = 0; c < d; ++c)
    (u = t[c]) && (p[c] = h = a.call(u, u.__data__, c, t) + "", l.has(h) ? i[c] = u : l.set(h, u));
  for (c = 0; c < f; ++c)
    h = a.call(e, s[c], c, s) + "", (u = l.get(h)) ? (o[c] = u, u.__data__ = s[c], l.delete(h)) : n[c] = new Fo(e, s[c]);
  for (c = 0; c < d; ++c)
    (u = t[c]) && l.get(p[c]) === u && (i[c] = u);
}
function kh(e) {
  return e.__data__;
}
function Ih(e, t) {
  if (!arguments.length) return Array.from(this, kh);
  var n = t ? Eh : Ch, o = this._parents, i = this._groups;
  typeof e != "function" && (e = Sh(e));
  for (var s = i.length, a = new Array(s), c = new Array(s), u = new Array(s), l = 0; l < s; ++l) {
    var d = o[l], f = i[l], p = f.length, h = Ah(e.call(d, d && d.__data__, l, o)), y = h.length, m = c[l] = new Array(y), x = a[l] = new Array(y), w = u[l] = new Array(p);
    n(d, f, m, x, w, h, t);
    for (var b = 0, g = 0, v, j; b < y; ++b)
      if (v = m[b]) {
        for (b >= g && (g = b + 1); !(j = x[g]) && ++g < y; ) ;
        v._next = j || null;
      }
  }
  return a = new He(a, o), a._enter = c, a._exit = u, a;
}
function Ah(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function _h() {
  return new He(this._exit || this._groups.map(ml), this._parents);
}
function Dh(e, t, n) {
  var o = this.enter(), i = this, s = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (i = t(i), i && (i = i.selection())), n == null ? s.remove() : n(s), o && i ? o.merge(i).order() : i;
}
function Th(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, i = n.length, s = o.length, a = Math.min(i, s), c = new Array(i), u = 0; u < a; ++u)
    for (var l = n[u], d = o[u], f = l.length, p = c[u] = new Array(f), h, y = 0; y < f; ++y)
      (h = l[y] || d[y]) && (p[y] = h);
  for (; u < i; ++u)
    c[u] = n[u];
  return new He(c, this._parents);
}
function $h() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], i = o.length - 1, s = o[i], a; --i >= 0; )
      (a = o[i]) && (s && a.compareDocumentPosition(s) ^ 4 && s.parentNode.insertBefore(a, s), s = a);
  return this;
}
function Ph(e) {
  e || (e = Mh);
  function t(f, p) {
    return f && p ? e(f.__data__, p.__data__) : !f - !p;
  }
  for (var n = this._groups, o = n.length, i = new Array(o), s = 0; s < o; ++s) {
    for (var a = n[s], c = a.length, u = i[s] = new Array(c), l, d = 0; d < c; ++d)
      (l = a[d]) && (u[d] = l);
    u.sort(t);
  }
  return new He(i, this._parents).order();
}
function Mh(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function Rh() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function Lh() {
  return Array.from(this);
}
function zh() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], i = 0, s = o.length; i < s; ++i) {
      var a = o[i];
      if (a) return a;
    }
  return null;
}
function Vh() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function Oh() {
  return !this.node();
}
function Hh(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var i = t[n], s = 0, a = i.length, c; s < a; ++s)
      (c = i[s]) && e.call(c, c.__data__, s, i);
  return this;
}
function Wh(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Fh(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Bh(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function Kh(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function Xh(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function Yh(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function qh(e, t) {
  var n = si(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? Fh : Wh : typeof t == "function" ? n.local ? Yh : Xh : n.local ? Kh : Bh)(n, t));
}
function xl(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function Uh(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Zh(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function Gh(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function Jh(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? Uh : typeof t == "function" ? Gh : Zh)(e, t, n ?? "")) : Ut(this.node(), e);
}
function Ut(e, t) {
  return e.style.getPropertyValue(t) || xl(e).getComputedStyle(e, null).getPropertyValue(t);
}
function Qh(e) {
  return function() {
    delete this[e];
  };
}
function eg(e, t) {
  return function() {
    this[e] = t;
  };
}
function tg(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function ng(e, t) {
  return arguments.length > 1 ? this.each((t == null ? Qh : typeof t == "function" ? tg : eg)(e, t)) : this.node()[e];
}
function wl(e) {
  return e.trim().split(/^|\s+/);
}
function Zr(e) {
  return e.classList || new vl(e);
}
function vl(e) {
  this._node = e, this._names = wl(e.getAttribute("class") || "");
}
vl.prototype = {
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
function bl(e, t) {
  for (var n = Zr(e), o = -1, i = t.length; ++o < i; ) n.add(t[o]);
}
function Nl(e, t) {
  for (var n = Zr(e), o = -1, i = t.length; ++o < i; ) n.remove(t[o]);
}
function og(e) {
  return function() {
    bl(this, e);
  };
}
function ig(e) {
  return function() {
    Nl(this, e);
  };
}
function rg(e, t) {
  return function() {
    (t.apply(this, arguments) ? bl : Nl)(this, e);
  };
}
function sg(e, t) {
  var n = wl(e + "");
  if (arguments.length < 2) {
    for (var o = Zr(this.node()), i = -1, s = n.length; ++i < s; ) if (!o.contains(n[i])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? rg : t ? og : ig)(n, t));
}
function ag() {
  this.textContent = "";
}
function cg(e) {
  return function() {
    this.textContent = e;
  };
}
function lg(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function ug(e) {
  return arguments.length ? this.each(e == null ? ag : (typeof e == "function" ? lg : cg)(e)) : this.node().textContent;
}
function dg() {
  this.innerHTML = "";
}
function fg(e) {
  return function() {
    this.innerHTML = e;
  };
}
function pg(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function hg(e) {
  return arguments.length ? this.each(e == null ? dg : (typeof e == "function" ? pg : fg)(e)) : this.node().innerHTML;
}
function gg() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function yg() {
  return this.each(gg);
}
function mg() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function xg() {
  return this.each(mg);
}
function wg(e) {
  var t = typeof e == "function" ? e : pl(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function vg() {
  return null;
}
function bg(e, t) {
  var n = typeof e == "function" ? e : pl(e), o = t == null ? vg : typeof t == "function" ? t : Ur(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function Ng() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function jg() {
  return this.each(Ng);
}
function Sg() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Cg() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Eg(e) {
  return this.select(e ? Cg : Sg);
}
function kg(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function Ig(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function Ag(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function _g(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, i = t.length, s; n < i; ++n)
        s = t[n], (!e.type || s.type === e.type) && s.name === e.name ? this.removeEventListener(s.type, s.listener, s.options) : t[++o] = s;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function Dg(e, t, n) {
  return function() {
    var o = this.__on, i, s = Ig(t);
    if (o) {
      for (var a = 0, c = o.length; a < c; ++a)
        if ((i = o[a]).type === e.type && i.name === e.name) {
          this.removeEventListener(i.type, i.listener, i.options), this.addEventListener(i.type, i.listener = s, i.options = n), i.value = t;
          return;
        }
    }
    this.addEventListener(e.type, s, n), i = { type: e.type, name: e.name, value: t, listener: s, options: n }, o ? o.push(i) : this.__on = [i];
  };
}
function Tg(e, t, n) {
  var o = Ag(e + ""), i, s = o.length, a;
  if (arguments.length < 2) {
    var c = this.node().__on;
    if (c) {
      for (var u = 0, l = c.length, d; u < l; ++u)
        for (i = 0, d = c[u]; i < s; ++i)
          if ((a = o[i]).type === d.type && a.name === d.name)
            return d.value;
    }
    return;
  }
  for (c = t ? Dg : _g, i = 0; i < s; ++i) this.each(c(o[i], t, n));
  return this;
}
function jl(e, t, n) {
  var o = xl(e), i = o.CustomEvent;
  typeof i == "function" ? i = new i(t, n) : (i = o.document.createEvent("Event"), n ? (i.initEvent(t, n.bubbles, n.cancelable), i.detail = n.detail) : i.initEvent(t, !1, !1)), e.dispatchEvent(i);
}
function $g(e, t) {
  return function() {
    return jl(this, e, t);
  };
}
function Pg(e, t) {
  return function() {
    return jl(this, e, t.apply(this, arguments));
  };
}
function Mg(e, t) {
  return this.each((typeof t == "function" ? Pg : $g)(e, t));
}
function* Rg() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], i = 0, s = o.length, a; i < s; ++i)
      (a = o[i]) && (yield a);
}
var Sl = [null];
function He(e, t) {
  this._groups = e, this._parents = t;
}
function Fn() {
  return new He([[document.documentElement]], Sl);
}
function Lg() {
  return this;
}
He.prototype = Fn.prototype = {
  constructor: He,
  select: lh,
  selectAll: ph,
  selectChild: mh,
  selectChildren: bh,
  filter: Nh,
  data: Ih,
  enter: jh,
  exit: _h,
  join: Dh,
  merge: Th,
  selection: Lg,
  order: $h,
  sort: Ph,
  call: Rh,
  nodes: Lh,
  node: zh,
  size: Vh,
  empty: Oh,
  each: Hh,
  attr: qh,
  style: Jh,
  property: ng,
  classed: sg,
  text: ug,
  html: hg,
  raise: yg,
  lower: xg,
  append: wg,
  insert: bg,
  remove: jg,
  clone: Eg,
  datum: kg,
  on: Tg,
  dispatch: Mg,
  [Symbol.iterator]: Rg
};
function ze(e) {
  return typeof e == "string" ? new He([[document.querySelector(e)]], [document.documentElement]) : new He([[e]], Sl);
}
function zg(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Ke(e, t) {
  if (e = zg(e), t === void 0 && (t = e.currentTarget), t) {
    var n = t.ownerSVGElement || t;
    if (n.createSVGPoint) {
      var o = n.createSVGPoint();
      return o.x = e.clientX, o.y = e.clientY, o = o.matrixTransform(t.getScreenCTM().inverse()), [o.x, o.y];
    }
    if (t.getBoundingClientRect) {
      var i = t.getBoundingClientRect();
      return [e.clientX - i.left - t.clientLeft, e.clientY - i.top - t.clientTop];
    }
  }
  return [e.pageX, e.pageY];
}
const Vg = { passive: !1 }, An = { capture: !0, passive: !1 };
function er(e) {
  e.stopImmediatePropagation();
}
function Ft(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Cl(e) {
  var t = e.document.documentElement, n = ze(e).on("dragstart.drag", Ft, An);
  "onselectstart" in t ? n.on("selectstart.drag", Ft, An) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function El(e, t) {
  var n = e.document.documentElement, o = ze(e).on("dragstart.drag", null);
  t && (o.on("click.drag", Ft, An), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const fo = (e) => () => e;
function br(e, {
  sourceEvent: t,
  subject: n,
  target: o,
  identifier: i,
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
    identifier: { value: i, enumerable: !0, configurable: !0 },
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
function Og(e) {
  return !e.ctrlKey && !e.button;
}
function Hg() {
  return this.parentNode;
}
function Wg(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function Fg() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function kl() {
  var e = Og, t = Hg, n = Wg, o = Fg, i = {}, s = ri("start", "drag", "end"), a = 0, c, u, l, d, f = 0;
  function p(v) {
    v.on("mousedown.drag", h).filter(o).on("touchstart.drag", x).on("touchmove.drag", w, Vg).on("touchend.drag touchcancel.drag", b).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function h(v, j) {
    if (!(d || !e.call(this, v, j))) {
      var N = g(this, t.call(this, v, j), v, j, "mouse");
      N && (ze(v.view).on("mousemove.drag", y, An).on("mouseup.drag", m, An), Cl(v.view), er(v), l = !1, c = v.clientX, u = v.clientY, N("start", v));
    }
  }
  function y(v) {
    if (Ft(v), !l) {
      var j = v.clientX - c, N = v.clientY - u;
      l = j * j + N * N > f;
    }
    i.mouse("drag", v);
  }
  function m(v) {
    ze(v.view).on("mousemove.drag mouseup.drag", null), El(v.view, l), Ft(v), i.mouse("end", v);
  }
  function x(v, j) {
    if (e.call(this, v, j)) {
      var N = v.changedTouches, S = t.call(this, v, j), k = N.length, D, L;
      for (D = 0; D < k; ++D)
        (L = g(this, S, v, j, N[D].identifier, N[D])) && (er(v), L("start", v, N[D]));
    }
  }
  function w(v) {
    var j = v.changedTouches, N = j.length, S, k;
    for (S = 0; S < N; ++S)
      (k = i[j[S].identifier]) && (Ft(v), k("drag", v, j[S]));
  }
  function b(v) {
    var j = v.changedTouches, N = j.length, S, k;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), S = 0; S < N; ++S)
      (k = i[j[S].identifier]) && (er(v), k("end", v, j[S]));
  }
  function g(v, j, N, S, k, D) {
    var L = s.copy(), A = Ke(D || N, j), _, R, C;
    if ((C = n.call(v, new br("beforestart", {
      sourceEvent: N,
      target: p,
      identifier: k,
      active: a,
      x: A[0],
      y: A[1],
      dx: 0,
      dy: 0,
      dispatch: L
    }), S)) != null)
      return _ = C.x - A[0] || 0, R = C.y - A[1] || 0, function I(E, T, $) {
        var P = A, F;
        switch (E) {
          case "start":
            i[k] = I, F = a++;
            break;
          case "end":
            delete i[k], --a;
          // falls through
          case "drag":
            A = Ke($ || T, j), F = a;
            break;
        }
        L.call(
          E,
          v,
          new br(E, {
            sourceEvent: T,
            subject: C,
            target: p,
            identifier: k,
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
    return arguments.length ? (e = typeof v == "function" ? v : fo(!!v), p) : e;
  }, p.container = function(v) {
    return arguments.length ? (t = typeof v == "function" ? v : fo(v), p) : t;
  }, p.subject = function(v) {
    return arguments.length ? (n = typeof v == "function" ? v : fo(v), p) : n;
  }, p.touchable = function(v) {
    return arguments.length ? (o = typeof v == "function" ? v : fo(!!v), p) : o;
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
function Il(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function Bn() {
}
var _n = 0.7, Bo = 1 / _n, Bt = "\\s*([+-]?\\d+)\\s*", Dn = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", Je = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", Bg = /^#([0-9a-f]{3,8})$/, Kg = new RegExp(`^rgb\\(${Bt},${Bt},${Bt}\\)$`), Xg = new RegExp(`^rgb\\(${Je},${Je},${Je}\\)$`), Yg = new RegExp(`^rgba\\(${Bt},${Bt},${Bt},${Dn}\\)$`), qg = new RegExp(`^rgba\\(${Je},${Je},${Je},${Dn}\\)$`), Ug = new RegExp(`^hsl\\(${Dn},${Je},${Je}\\)$`), Zg = new RegExp(`^hsla\\(${Dn},${Je},${Je},${Dn}\\)$`), Zs = {
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
Gr(Bn, jt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Gs,
  // Deprecated! Use color.formatHex.
  formatHex: Gs,
  formatHex8: Gg,
  formatHsl: Jg,
  formatRgb: Js,
  toString: Js
});
function Gs() {
  return this.rgb().formatHex();
}
function Gg() {
  return this.rgb().formatHex8();
}
function Jg() {
  return Al(this).formatHsl();
}
function Js() {
  return this.rgb().formatRgb();
}
function jt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = Bg.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? Qs(t) : n === 3 ? new Pe(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? po(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? po(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = Kg.exec(e)) ? new Pe(t[1], t[2], t[3], 1) : (t = Xg.exec(e)) ? new Pe(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = Yg.exec(e)) ? po(t[1], t[2], t[3], t[4]) : (t = qg.exec(e)) ? po(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = Ug.exec(e)) ? na(t[1], t[2] / 100, t[3] / 100, 1) : (t = Zg.exec(e)) ? na(t[1], t[2] / 100, t[3] / 100, t[4]) : Zs.hasOwnProperty(e) ? Qs(Zs[e]) : e === "transparent" ? new Pe(NaN, NaN, NaN, 0) : null;
}
function Qs(e) {
  return new Pe(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function po(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new Pe(e, t, n, o);
}
function Qg(e) {
  return e instanceof Bn || (e = jt(e)), e ? (e = e.rgb(), new Pe(e.r, e.g, e.b, e.opacity)) : new Pe();
}
function Nr(e, t, n, o) {
  return arguments.length === 1 ? Qg(e) : new Pe(e, t, n, o ?? 1);
}
function Pe(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
Gr(Pe, Nr, Il(Bn, {
  brighter(e) {
    return e = e == null ? Bo : Math.pow(Bo, e), new Pe(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? _n : Math.pow(_n, e), new Pe(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new Pe(bt(this.r), bt(this.g), bt(this.b), Ko(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: ea,
  // Deprecated! Use color.formatHex.
  formatHex: ea,
  formatHex8: ey,
  formatRgb: ta,
  toString: ta
}));
function ea() {
  return `#${wt(this.r)}${wt(this.g)}${wt(this.b)}`;
}
function ey() {
  return `#${wt(this.r)}${wt(this.g)}${wt(this.b)}${wt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function ta() {
  const e = Ko(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${bt(this.r)}, ${bt(this.g)}, ${bt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Ko(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function bt(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function wt(e) {
  return e = bt(e), (e < 16 ? "0" : "") + e.toString(16);
}
function na(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Xe(e, t, n, o);
}
function Al(e) {
  if (e instanceof Xe) return new Xe(e.h, e.s, e.l, e.opacity);
  if (e instanceof Bn || (e = jt(e)), !e) return new Xe();
  if (e instanceof Xe) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, i = Math.min(t, n, o), s = Math.max(t, n, o), a = NaN, c = s - i, u = (s + i) / 2;
  return c ? (t === s ? a = (n - o) / c + (n < o) * 6 : n === s ? a = (o - t) / c + 2 : a = (t - n) / c + 4, c /= u < 0.5 ? s + i : 2 - s - i, a *= 60) : c = u > 0 && u < 1 ? 0 : a, new Xe(a, c, u, e.opacity);
}
function ty(e, t, n, o) {
  return arguments.length === 1 ? Al(e) : new Xe(e, t, n, o ?? 1);
}
function Xe(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
Gr(Xe, ty, Il(Bn, {
  brighter(e) {
    return e = e == null ? Bo : Math.pow(Bo, e), new Xe(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? _n : Math.pow(_n, e), new Xe(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, i = 2 * n - o;
    return new Pe(
      tr(e >= 240 ? e - 240 : e + 120, i, o),
      tr(e, i, o),
      tr(e < 120 ? e + 240 : e - 120, i, o),
      this.opacity
    );
  },
  clamp() {
    return new Xe(oa(this.h), ho(this.s), ho(this.l), Ko(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Ko(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${oa(this.h)}, ${ho(this.s) * 100}%, ${ho(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function oa(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function ho(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function tr(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const Jr = (e) => () => e;
function ny(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function oy(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function iy(e) {
  return (e = +e) == 1 ? _l : function(t, n) {
    return n - t ? oy(t, n, e) : Jr(isNaN(t) ? n : t);
  };
}
function _l(e, t) {
  var n = t - e;
  return n ? ny(e, n) : Jr(isNaN(e) ? t : e);
}
const Xo = (function e(t) {
  var n = iy(t);
  function o(i, s) {
    var a = n((i = Nr(i)).r, (s = Nr(s)).r), c = n(i.g, s.g), u = n(i.b, s.b), l = _l(i.opacity, s.opacity);
    return function(d) {
      return i.r = a(d), i.g = c(d), i.b = u(d), i.opacity = l(d), i + "";
    };
  }
  return o.gamma = e, o;
})(1);
function ry(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), i;
  return function(s) {
    for (i = 0; i < n; ++i) o[i] = e[i] * (1 - s) + t[i] * s;
    return o;
  };
}
function sy(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function ay(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, i = new Array(o), s = new Array(n), a;
  for (a = 0; a < o; ++a) i[a] = En(e[a], t[a]);
  for (; a < n; ++a) s[a] = t[a];
  return function(c) {
    for (a = 0; a < o; ++a) s[a] = i[a](c);
    return s;
  };
}
function cy(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(o) {
    return n.setTime(e * (1 - o) + t * o), n;
  };
}
function Ge(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function ly(e, t) {
  var n = {}, o = {}, i;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (i in t)
    i in e ? n[i] = En(e[i], t[i]) : o[i] = t[i];
  return function(s) {
    for (i in n) o[i] = n[i](s);
    return o;
  };
}
var jr = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, nr = new RegExp(jr.source, "g");
function uy(e) {
  return function() {
    return e;
  };
}
function dy(e) {
  return function(t) {
    return e(t) + "";
  };
}
function Dl(e, t) {
  var n = jr.lastIndex = nr.lastIndex = 0, o, i, s, a = -1, c = [], u = [];
  for (e = e + "", t = t + ""; (o = jr.exec(e)) && (i = nr.exec(t)); )
    (s = i.index) > n && (s = t.slice(n, s), c[a] ? c[a] += s : c[++a] = s), (o = o[0]) === (i = i[0]) ? c[a] ? c[a] += i : c[++a] = i : (c[++a] = null, u.push({ i: a, x: Ge(o, i) })), n = nr.lastIndex;
  return n < t.length && (s = t.slice(n), c[a] ? c[a] += s : c[++a] = s), c.length < 2 ? u[0] ? dy(u[0].x) : uy(t) : (t = u.length, function(l) {
    for (var d = 0, f; d < t; ++d) c[(f = u[d]).i] = f.x(l);
    return c.join("");
  });
}
function En(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? Jr(t) : (n === "number" ? Ge : n === "string" ? (o = jt(t)) ? (t = o, Xo) : Dl : t instanceof jt ? Xo : t instanceof Date ? cy : sy(t) ? ry : Array.isArray(t) ? ay : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? ly : Ge)(e, t);
}
var ia = 180 / Math.PI, Sr = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function Tl(e, t, n, o, i, s) {
  var a, c, u;
  return (a = Math.sqrt(e * e + t * t)) && (e /= a, t /= a), (u = e * n + t * o) && (n -= e * u, o -= t * u), (c = Math.sqrt(n * n + o * o)) && (n /= c, o /= c, u /= c), e * o < t * n && (e = -e, t = -t, u = -u, a = -a), {
    translateX: i,
    translateY: s,
    rotate: Math.atan2(t, e) * ia,
    skewX: Math.atan(u) * ia,
    scaleX: a,
    scaleY: c
  };
}
var go;
function fy(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Sr : Tl(t.a, t.b, t.c, t.d, t.e, t.f);
}
function py(e) {
  return e == null || (go || (go = document.createElementNS("http://www.w3.org/2000/svg", "g")), go.setAttribute("transform", e), !(e = go.transform.baseVal.consolidate())) ? Sr : (e = e.matrix, Tl(e.a, e.b, e.c, e.d, e.e, e.f));
}
function $l(e, t, n, o) {
  function i(l) {
    return l.length ? l.pop() + " " : "";
  }
  function s(l, d, f, p, h, y) {
    if (l !== f || d !== p) {
      var m = h.push("translate(", null, t, null, n);
      y.push({ i: m - 4, x: Ge(l, f) }, { i: m - 2, x: Ge(d, p) });
    } else (f || p) && h.push("translate(" + f + t + p + n);
  }
  function a(l, d, f, p) {
    l !== d ? (l - d > 180 ? d += 360 : d - l > 180 && (l += 360), p.push({ i: f.push(i(f) + "rotate(", null, o) - 2, x: Ge(l, d) })) : d && f.push(i(f) + "rotate(" + d + o);
  }
  function c(l, d, f, p) {
    l !== d ? p.push({ i: f.push(i(f) + "skewX(", null, o) - 2, x: Ge(l, d) }) : d && f.push(i(f) + "skewX(" + d + o);
  }
  function u(l, d, f, p, h, y) {
    if (l !== f || d !== p) {
      var m = h.push(i(h) + "scale(", null, ",", null, ")");
      y.push({ i: m - 4, x: Ge(l, f) }, { i: m - 2, x: Ge(d, p) });
    } else (f !== 1 || p !== 1) && h.push(i(h) + "scale(" + f + "," + p + ")");
  }
  return function(l, d) {
    var f = [], p = [];
    return l = e(l), d = e(d), s(l.translateX, l.translateY, d.translateX, d.translateY, f, p), a(l.rotate, d.rotate, f, p), c(l.skewX, d.skewX, f, p), u(l.scaleX, l.scaleY, d.scaleX, d.scaleY, f, p), l = d = null, function(h) {
      for (var y = -1, m = p.length, x; ++y < m; ) f[(x = p[y]).i] = x.x(h);
      return f.join("");
    };
  };
}
var hy = $l(fy, "px, ", "px)", "deg)"), gy = $l(py, ", ", ")", ")"), yy = 1e-12;
function ra(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function my(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function xy(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const ko = (function e(t, n, o) {
  function i(s, a) {
    var c = s[0], u = s[1], l = s[2], d = a[0], f = a[1], p = a[2], h = d - c, y = f - u, m = h * h + y * y, x, w;
    if (m < yy)
      w = Math.log(p / l) / t, x = function(S) {
        return [
          c + S * h,
          u + S * y,
          l * Math.exp(t * S * w)
        ];
      };
    else {
      var b = Math.sqrt(m), g = (p * p - l * l + o * m) / (2 * l * n * b), v = (p * p - l * l - o * m) / (2 * p * n * b), j = Math.log(Math.sqrt(g * g + 1) - g), N = Math.log(Math.sqrt(v * v + 1) - v);
      w = (N - j) / t, x = function(S) {
        var k = S * w, D = ra(j), L = l / (n * b) * (D * xy(t * k + j) - my(j));
        return [
          c + L * h,
          u + L * y,
          l * D / ra(t * k + j)
        ];
      };
    }
    return x.duration = w * 1e3 * t / Math.SQRT2, x;
  }
  return i.rho = function(s) {
    var a = Math.max(1e-3, +s), c = a * a, u = c * c;
    return e(a, c, u);
  }, i;
})(Math.SQRT2, 2, 4);
var Zt = 0, yn = 0, fn = 0, Pl = 1e3, Yo, mn, qo = 0, St = 0, ai = 0, Tn = typeof performance == "object" && performance.now ? performance : Date, Ml = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function Qr() {
  return St || (Ml(wy), St = Tn.now() + ai);
}
function wy() {
  St = 0;
}
function Uo() {
  this._call = this._time = this._next = null;
}
Uo.prototype = Rl.prototype = {
  constructor: Uo,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? Qr() : +n) + (t == null ? 0 : +t), !this._next && mn !== this && (mn ? mn._next = this : Yo = this, mn = this), this._call = e, this._time = n, Cr();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Cr());
  }
};
function Rl(e, t, n) {
  var o = new Uo();
  return o.restart(e, t, n), o;
}
function vy() {
  Qr(), ++Zt;
  for (var e = Yo, t; e; )
    (t = St - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --Zt;
}
function sa() {
  St = (qo = Tn.now()) + ai, Zt = yn = 0;
  try {
    vy();
  } finally {
    Zt = 0, Ny(), St = 0;
  }
}
function by() {
  var e = Tn.now(), t = e - qo;
  t > Pl && (ai -= t, qo = e);
}
function Ny() {
  for (var e, t = Yo, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Yo = n);
  mn = e, Cr(o);
}
function Cr(e) {
  if (!Zt) {
    yn && (yn = clearTimeout(yn));
    var t = e - St;
    t > 24 ? (e < 1 / 0 && (yn = setTimeout(sa, e - Tn.now() - ai)), fn && (fn = clearInterval(fn))) : (fn || (qo = Tn.now(), fn = setInterval(by, Pl)), Zt = 1, Ml(sa));
  }
}
function aa(e, t, n) {
  var o = new Uo();
  return t = t == null ? 0 : +t, o.restart((i) => {
    o.stop(), e(i + t);
  }, t, n), o;
}
var jy = ri("start", "end", "cancel", "interrupt"), Sy = [], Ll = 0, ca = 1, Er = 2, Io = 3, la = 4, kr = 5, Ao = 6;
function ci(e, t, n, o, i, s) {
  var a = e.__transition;
  if (!a) e.__transition = {};
  else if (n in a) return;
  Cy(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: i,
    // For context during callback.
    on: jy,
    tween: Sy,
    time: s.time,
    delay: s.delay,
    duration: s.duration,
    ease: s.ease,
    timer: null,
    state: Ll
  });
}
function es(e, t) {
  var n = Ue(e, t);
  if (n.state > Ll) throw new Error("too late; already scheduled");
  return n;
}
function nt(e, t) {
  var n = Ue(e, t);
  if (n.state > Io) throw new Error("too late; already running");
  return n;
}
function Ue(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function Cy(e, t, n) {
  var o = e.__transition, i;
  o[t] = n, n.timer = Rl(s, 0, n.time);
  function s(l) {
    n.state = ca, n.timer.restart(a, n.delay, n.time), n.delay <= l && a(l - n.delay);
  }
  function a(l) {
    var d, f, p, h;
    if (n.state !== ca) return u();
    for (d in o)
      if (h = o[d], h.name === n.name) {
        if (h.state === Io) return aa(a);
        h.state === la ? (h.state = Ao, h.timer.stop(), h.on.call("interrupt", e, e.__data__, h.index, h.group), delete o[d]) : +d < t && (h.state = Ao, h.timer.stop(), h.on.call("cancel", e, e.__data__, h.index, h.group), delete o[d]);
      }
    if (aa(function() {
      n.state === Io && (n.state = la, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = Er, n.on.call("start", e, e.__data__, n.index, n.group), n.state === Er) {
      for (n.state = Io, i = new Array(p = n.tween.length), d = 0, f = -1; d < p; ++d)
        (h = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (i[++f] = h);
      i.length = f + 1;
    }
  }
  function c(l) {
    for (var d = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(u), n.state = kr, 1), f = -1, p = i.length; ++f < p; )
      i[f].call(e, d);
    n.state === kr && (n.on.call("end", e, e.__data__, n.index, n.group), u());
  }
  function u() {
    n.state = Ao, n.timer.stop(), delete o[t];
    for (var l in o) return;
    delete e.__transition;
  }
}
function _o(e, t) {
  var n = e.__transition, o, i, s = !0, a;
  if (n) {
    t = t == null ? null : t + "";
    for (a in n) {
      if ((o = n[a]).name !== t) {
        s = !1;
        continue;
      }
      i = o.state > Er && o.state < kr, o.state = Ao, o.timer.stop(), o.on.call(i ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[a];
    }
    s && delete e.__transition;
  }
}
function Ey(e) {
  return this.each(function() {
    _o(this, e);
  });
}
function ky(e, t) {
  var n, o;
  return function() {
    var i = nt(this, e), s = i.tween;
    if (s !== n) {
      o = n = s;
      for (var a = 0, c = o.length; a < c; ++a)
        if (o[a].name === t) {
          o = o.slice(), o.splice(a, 1);
          break;
        }
    }
    i.tween = o;
  };
}
function Iy(e, t, n) {
  var o, i;
  if (typeof n != "function") throw new Error();
  return function() {
    var s = nt(this, e), a = s.tween;
    if (a !== o) {
      i = (o = a).slice();
      for (var c = { name: t, value: n }, u = 0, l = i.length; u < l; ++u)
        if (i[u].name === t) {
          i[u] = c;
          break;
        }
      u === l && i.push(c);
    }
    s.tween = i;
  };
}
function Ay(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = Ue(this.node(), n).tween, i = 0, s = o.length, a; i < s; ++i)
      if ((a = o[i]).name === e)
        return a.value;
    return null;
  }
  return this.each((t == null ? ky : Iy)(n, e, t));
}
function ts(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var i = nt(this, o);
    (i.value || (i.value = {}))[t] = n.apply(this, arguments);
  }), function(i) {
    return Ue(i, o).value[t];
  };
}
function zl(e, t) {
  var n;
  return (typeof t == "number" ? Ge : t instanceof jt ? Xo : (n = jt(t)) ? (t = n, Xo) : Dl)(e, t);
}
function _y(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Dy(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Ty(e, t, n) {
  var o, i = n + "", s;
  return function() {
    var a = this.getAttribute(e);
    return a === i ? null : a === o ? s : s = t(o = a, n);
  };
}
function $y(e, t, n) {
  var o, i = n + "", s;
  return function() {
    var a = this.getAttributeNS(e.space, e.local);
    return a === i ? null : a === o ? s : s = t(o = a, n);
  };
}
function Py(e, t, n) {
  var o, i, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttribute(e) : (a = this.getAttribute(e), u = c + "", a === u ? null : a === o && u === i ? s : (i = u, s = t(o = a, c)));
  };
}
function My(e, t, n) {
  var o, i, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (a = this.getAttributeNS(e.space, e.local), u = c + "", a === u ? null : a === o && u === i ? s : (i = u, s = t(o = a, c)));
  };
}
function Ry(e, t) {
  var n = si(e), o = n === "transform" ? gy : zl;
  return this.attrTween(e, typeof t == "function" ? (n.local ? My : Py)(n, o, ts(this, "attr." + e, t)) : t == null ? (n.local ? Dy : _y)(n) : (n.local ? $y : Ty)(n, o, t));
}
function Ly(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function zy(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function Vy(e, t) {
  var n, o;
  function i() {
    var s = t.apply(this, arguments);
    return s !== o && (n = (o = s) && zy(e, s)), n;
  }
  return i._value = t, i;
}
function Oy(e, t) {
  var n, o;
  function i() {
    var s = t.apply(this, arguments);
    return s !== o && (n = (o = s) && Ly(e, s)), n;
  }
  return i._value = t, i;
}
function Hy(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = si(e);
  return this.tween(n, (o.local ? Vy : Oy)(o, t));
}
function Wy(e, t) {
  return function() {
    es(this, e).delay = +t.apply(this, arguments);
  };
}
function Fy(e, t) {
  return t = +t, function() {
    es(this, e).delay = t;
  };
}
function By(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Wy : Fy)(t, e)) : Ue(this.node(), t).delay;
}
function Ky(e, t) {
  return function() {
    nt(this, e).duration = +t.apply(this, arguments);
  };
}
function Xy(e, t) {
  return t = +t, function() {
    nt(this, e).duration = t;
  };
}
function Yy(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Ky : Xy)(t, e)) : Ue(this.node(), t).duration;
}
function qy(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    nt(this, e).ease = t;
  };
}
function Uy(e) {
  var t = this._id;
  return arguments.length ? this.each(qy(t, e)) : Ue(this.node(), t).ease;
}
function Zy(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    nt(this, e).ease = n;
  };
}
function Gy(e) {
  if (typeof e != "function") throw new Error();
  return this.each(Zy(this._id, e));
}
function Jy(e) {
  typeof e != "function" && (e = gl(e));
  for (var t = this._groups, n = t.length, o = new Array(n), i = 0; i < n; ++i)
    for (var s = t[i], a = s.length, c = o[i] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new ct(o, this._parents, this._name, this._id);
}
function Qy(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, i = n.length, s = Math.min(o, i), a = new Array(o), c = 0; c < s; ++c)
    for (var u = t[c], l = n[c], d = u.length, f = a[c] = new Array(d), p, h = 0; h < d; ++h)
      (p = u[h] || l[h]) && (f[h] = p);
  for (; c < o; ++c)
    a[c] = t[c];
  return new ct(a, this._parents, this._name, this._id);
}
function em(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function tm(e, t, n) {
  var o, i, s = em(t) ? es : nt;
  return function() {
    var a = s(this, e), c = a.on;
    c !== o && (i = (o = c).copy()).on(t, n), a.on = i;
  };
}
function nm(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Ue(this.node(), n).on.on(e) : this.each(tm(n, e, t));
}
function om(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function im() {
  return this.on("end.remove", om(this._id));
}
function rm(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Ur(e));
  for (var o = this._groups, i = o.length, s = new Array(i), a = 0; a < i; ++a)
    for (var c = o[a], u = c.length, l = s[a] = new Array(u), d, f, p = 0; p < u; ++p)
      (d = c[p]) && (f = e.call(d, d.__data__, p, c)) && ("__data__" in d && (f.__data__ = d.__data__), l[p] = f, ci(l[p], t, n, p, l, Ue(d, n)));
  return new ct(s, this._parents, t, n);
}
function sm(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = hl(e));
  for (var o = this._groups, i = o.length, s = [], a = [], c = 0; c < i; ++c)
    for (var u = o[c], l = u.length, d, f = 0; f < l; ++f)
      if (d = u[f]) {
        for (var p = e.call(d, d.__data__, f, u), h, y = Ue(d, n), m = 0, x = p.length; m < x; ++m)
          (h = p[m]) && ci(h, t, n, m, p, y);
        s.push(p), a.push(d);
      }
  return new ct(s, a, t, n);
}
var am = Fn.prototype.constructor;
function cm() {
  return new am(this._groups, this._parents);
}
function lm(e, t) {
  var n, o, i;
  return function() {
    var s = Ut(this, e), a = (this.style.removeProperty(e), Ut(this, e));
    return s === a ? null : s === n && a === o ? i : i = t(n = s, o = a);
  };
}
function Vl(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function um(e, t, n) {
  var o, i = n + "", s;
  return function() {
    var a = Ut(this, e);
    return a === i ? null : a === o ? s : s = t(o = a, n);
  };
}
function dm(e, t, n) {
  var o, i, s;
  return function() {
    var a = Ut(this, e), c = n(this), u = c + "";
    return c == null && (u = c = (this.style.removeProperty(e), Ut(this, e))), a === u ? null : a === o && u === i ? s : (i = u, s = t(o = a, c));
  };
}
function fm(e, t) {
  var n, o, i, s = "style." + t, a = "end." + s, c;
  return function() {
    var u = nt(this, e), l = u.on, d = u.value[s] == null ? c || (c = Vl(t)) : void 0;
    (l !== n || i !== d) && (o = (n = l).copy()).on(a, i = d), u.on = o;
  };
}
function pm(e, t, n) {
  var o = (e += "") == "transform" ? hy : zl;
  return t == null ? this.styleTween(e, lm(e, o)).on("end.style." + e, Vl(e)) : typeof t == "function" ? this.styleTween(e, dm(e, o, ts(this, "style." + e, t))).each(fm(this._id, e)) : this.styleTween(e, um(e, o, t), n).on("end.style." + e, null);
}
function hm(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function gm(e, t, n) {
  var o, i;
  function s() {
    var a = t.apply(this, arguments);
    return a !== i && (o = (i = a) && hm(e, a, n)), o;
  }
  return s._value = t, s;
}
function ym(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, gm(e, t, n ?? ""));
}
function mm(e) {
  return function() {
    this.textContent = e;
  };
}
function xm(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function wm(e) {
  return this.tween("text", typeof e == "function" ? xm(ts(this, "text", e)) : mm(e == null ? "" : e + ""));
}
function vm(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function bm(e) {
  var t, n;
  function o() {
    var i = e.apply(this, arguments);
    return i !== n && (t = (n = i) && vm(i)), t;
  }
  return o._value = e, o;
}
function Nm(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, bm(e));
}
function jm() {
  for (var e = this._name, t = this._id, n = Ol(), o = this._groups, i = o.length, s = 0; s < i; ++s)
    for (var a = o[s], c = a.length, u, l = 0; l < c; ++l)
      if (u = a[l]) {
        var d = Ue(u, t);
        ci(u, e, n, l, a, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new ct(o, this._parents, e, n);
}
function Sm() {
  var e, t, n = this, o = n._id, i = n.size();
  return new Promise(function(s, a) {
    var c = { value: a }, u = { value: function() {
      --i === 0 && s();
    } };
    n.each(function() {
      var l = nt(this, o), d = l.on;
      d !== e && (t = (e = d).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(u)), l.on = t;
    }), i === 0 && s();
  });
}
var Cm = 0;
function ct(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function Ol() {
  return ++Cm;
}
var rt = Fn.prototype;
ct.prototype = {
  constructor: ct,
  select: rm,
  selectAll: sm,
  selectChild: rt.selectChild,
  selectChildren: rt.selectChildren,
  filter: Jy,
  merge: Qy,
  selection: cm,
  transition: jm,
  call: rt.call,
  nodes: rt.nodes,
  node: rt.node,
  size: rt.size,
  empty: rt.empty,
  each: rt.each,
  on: nm,
  attr: Ry,
  attrTween: Hy,
  style: pm,
  styleTween: ym,
  text: wm,
  textTween: Nm,
  remove: im,
  tween: Ay,
  delay: By,
  duration: Yy,
  ease: Uy,
  easeVarying: Gy,
  end: Sm,
  [Symbol.iterator]: rt[Symbol.iterator]
};
function Em(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var km = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: Em
};
function Im(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function Am(e) {
  var t, n;
  e instanceof ct ? (t = e._id, e = e._name) : (t = Ol(), (n = km).time = Qr(), e = e == null ? null : e + "");
  for (var o = this._groups, i = o.length, s = 0; s < i; ++s)
    for (var a = o[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && ci(u, e, t, l, a, n || Im(u, t));
  return new ct(o, this._parents, e, t);
}
Fn.prototype.interrupt = Ey;
Fn.prototype.transition = Am;
const yo = (e) => () => e;
function _m(e, {
  sourceEvent: t,
  target: n,
  transform: o,
  dispatch: i
}) {
  Object.defineProperties(this, {
    type: { value: e, enumerable: !0, configurable: !0 },
    sourceEvent: { value: t, enumerable: !0, configurable: !0 },
    target: { value: n, enumerable: !0, configurable: !0 },
    transform: { value: o, enumerable: !0, configurable: !0 },
    _: { value: i }
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
var li = new st(1, 0, 0);
Hl.prototype = st.prototype;
function Hl(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return li;
  return e.__zoom;
}
function or(e) {
  e.stopImmediatePropagation();
}
function pn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Dm(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function Tm() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function ua() {
  return this.__zoom || li;
}
function $m(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function Pm() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Mm(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], i = e.invertX(t[1][0]) - n[1][0], s = e.invertY(t[0][1]) - n[0][1], a = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    i > o ? (o + i) / 2 : Math.min(0, o) || Math.max(0, i),
    a > s ? (s + a) / 2 : Math.min(0, s) || Math.max(0, a)
  );
}
function Wl() {
  var e = Dm, t = Tm, n = Mm, o = $m, i = Pm, s = [0, 1 / 0], a = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, u = ko, l = ri("start", "zoom", "end"), d, f, p, h = 500, y = 150, m = 0, x = 10;
  function w(C) {
    C.property("__zoom", ua).on("wheel.zoom", k, { passive: !1 }).on("mousedown.zoom", D).on("dblclick.zoom", L).filter(i).on("touchstart.zoom", A).on("touchmove.zoom", _).on("touchend.zoom touchcancel.zoom", R).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  w.transform = function(C, I, E, T) {
    var $ = C.selection ? C.selection() : C;
    $.property("__zoom", ua), C !== $ ? j(C, I, E, T) : $.interrupt().each(function() {
      N(this, arguments).event(T).start().zoom(null, typeof I == "function" ? I.apply(this, arguments) : I).end();
    });
  }, w.scaleBy = function(C, I, E, T) {
    w.scaleTo(C, function() {
      var $ = this.__zoom.k, P = typeof I == "function" ? I.apply(this, arguments) : I;
      return $ * P;
    }, E, T);
  }, w.scaleTo = function(C, I, E, T) {
    w.transform(C, function() {
      var $ = t.apply(this, arguments), P = this.__zoom, F = E == null ? v($) : typeof E == "function" ? E.apply(this, arguments) : E, H = P.invert(F), O = typeof I == "function" ? I.apply(this, arguments) : I;
      return n(g(b(P, O), F, H), $, a);
    }, E, T);
  }, w.translateBy = function(C, I, E, T) {
    w.transform(C, function() {
      return n(this.__zoom.translate(
        typeof I == "function" ? I.apply(this, arguments) : I,
        typeof E == "function" ? E.apply(this, arguments) : E
      ), t.apply(this, arguments), a);
    }, null, T);
  }, w.translateTo = function(C, I, E, T, $) {
    w.transform(C, function() {
      var P = t.apply(this, arguments), F = this.__zoom, H = T == null ? v(P) : typeof T == "function" ? T.apply(this, arguments) : T;
      return n(li.translate(H[0], H[1]).scale(F.k).translate(
        typeof I == "function" ? -I.apply(this, arguments) : -I,
        typeof E == "function" ? -E.apply(this, arguments) : -E
      ), P, a);
    }, T, $);
  };
  function b(C, I) {
    return I = Math.max(s[0], Math.min(s[1], I)), I === C.k ? C : new st(I, C.x, C.y);
  }
  function g(C, I, E) {
    var T = I[0] - E[0] * C.k, $ = I[1] - E[1] * C.k;
    return T === C.x && $ === C.y ? C : new st(C.k, T, $);
  }
  function v(C) {
    return [(+C[0][0] + +C[1][0]) / 2, (+C[0][1] + +C[1][1]) / 2];
  }
  function j(C, I, E, T) {
    C.on("start.zoom", function() {
      N(this, arguments).event(T).start();
    }).on("interrupt.zoom end.zoom", function() {
      N(this, arguments).event(T).end();
    }).tween("zoom", function() {
      var $ = this, P = arguments, F = N($, P).event(T), H = t.apply($, P), O = E == null ? v(H) : typeof E == "function" ? E.apply($, P) : E, U = Math.max(H[1][0] - H[0][0], H[1][1] - H[0][1]), q = $.__zoom, ne = typeof I == "function" ? I.apply($, P) : I, ce = u(q.invert(O).concat(U / q.k), ne.invert(O).concat(U / ne.k));
      return function(Z) {
        if (Z === 1) Z = ne;
        else {
          var z = ce(Z), X = U / z[2];
          Z = new st(X, O[0] - z[0] * X, O[1] - z[1] * X);
        }
        F.zoom(null, Z);
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
        new _m(C, {
          sourceEvent: this.sourceEvent,
          target: w,
          transform: this.that.__zoom,
          dispatch: l
        }),
        I
      );
    }
  };
  function k(C, ...I) {
    if (!e.apply(this, arguments)) return;
    var E = N(this, I).event(C), T = this.__zoom, $ = Math.max(s[0], Math.min(s[1], T.k * Math.pow(2, o.apply(this, arguments)))), P = Ke(C);
    if (E.wheel)
      (E.mouse[0][0] !== P[0] || E.mouse[0][1] !== P[1]) && (E.mouse[1] = T.invert(E.mouse[0] = P)), clearTimeout(E.wheel);
    else {
      if (T.k === $) return;
      E.mouse = [P, T.invert(P)], _o(this), E.start();
    }
    pn(C), E.wheel = setTimeout(F, y), E.zoom("mouse", n(g(b(T, $), E.mouse[0], E.mouse[1]), E.extent, a));
    function F() {
      E.wheel = null, E.end();
    }
  }
  function D(C, ...I) {
    if (p || !e.apply(this, arguments)) return;
    var E = C.currentTarget, T = N(this, I, !0).event(C), $ = ze(C.view).on("mousemove.zoom", O, !0).on("mouseup.zoom", U, !0), P = Ke(C, E), F = C.clientX, H = C.clientY;
    Cl(C.view), or(C), T.mouse = [P, this.__zoom.invert(P)], _o(this), T.start();
    function O(q) {
      if (pn(q), !T.moved) {
        var ne = q.clientX - F, ce = q.clientY - H;
        T.moved = ne * ne + ce * ce > m;
      }
      T.event(q).zoom("mouse", n(g(T.that.__zoom, T.mouse[0] = Ke(q, E), T.mouse[1]), T.extent, a));
    }
    function U(q) {
      $.on("mousemove.zoom mouseup.zoom", null), El(q.view, T.moved), pn(q), T.event(q).end();
    }
  }
  function L(C, ...I) {
    if (e.apply(this, arguments)) {
      var E = this.__zoom, T = Ke(C.changedTouches ? C.changedTouches[0] : C, this), $ = E.invert(T), P = E.k * (C.shiftKey ? 0.5 : 2), F = n(g(b(E, P), T, $), t.apply(this, I), a);
      pn(C), c > 0 ? ze(this).transition().duration(c).call(j, F, T, C) : ze(this).call(w.transform, F, T, C);
    }
  }
  function A(C, ...I) {
    if (e.apply(this, arguments)) {
      var E = C.touches, T = E.length, $ = N(this, I, C.changedTouches.length === T).event(C), P, F, H, O;
      for (or(C), F = 0; F < T; ++F)
        H = E[F], O = Ke(H, this), O = [O, this.__zoom.invert(O), H.identifier], $.touch0 ? !$.touch1 && $.touch0[2] !== O[2] && ($.touch1 = O, $.taps = 0) : ($.touch0 = O, P = !0, $.taps = 1 + !!d);
      d && (d = clearTimeout(d)), P && ($.taps < 2 && (f = O[0], d = setTimeout(function() {
        d = null;
      }, h)), _o(this), $.start());
    }
  }
  function _(C, ...I) {
    if (this.__zooming) {
      var E = N(this, I).event(C), T = C.changedTouches, $ = T.length, P, F, H, O;
      for (pn(C), P = 0; P < $; ++P)
        F = T[P], H = Ke(F, this), E.touch0 && E.touch0[2] === F.identifier ? E.touch0[0] = H : E.touch1 && E.touch1[2] === F.identifier && (E.touch1[0] = H);
      if (F = E.that.__zoom, E.touch1) {
        var U = E.touch0[0], q = E.touch0[1], ne = E.touch1[0], ce = E.touch1[1], Z = (Z = ne[0] - U[0]) * Z + (Z = ne[1] - U[1]) * Z, z = (z = ce[0] - q[0]) * z + (z = ce[1] - q[1]) * z;
        F = b(F, Math.sqrt(Z / z)), H = [(U[0] + ne[0]) / 2, (U[1] + ne[1]) / 2], O = [(q[0] + ce[0]) / 2, (q[1] + ce[1]) / 2];
      } else if (E.touch0) H = E.touch0[0], O = E.touch0[1];
      else return;
      E.zoom("touch", n(g(F, H, O), E.extent, a));
    }
  }
  function R(C, ...I) {
    if (this.__zooming) {
      var E = N(this, I).event(C), T = C.changedTouches, $ = T.length, P, F;
      for (or(C), p && clearTimeout(p), p = setTimeout(function() {
        p = null;
      }, h), P = 0; P < $; ++P)
        F = T[P], E.touch0 && E.touch0[2] === F.identifier ? delete E.touch0 : E.touch1 && E.touch1[2] === F.identifier && delete E.touch1;
      if (E.touch1 && !E.touch0 && (E.touch0 = E.touch1, delete E.touch1), E.touch0) E.touch0[1] = this.__zoom.invert(E.touch0[0]);
      else if (E.end(), E.taps === 2 && (F = Ke(F, this), Math.hypot(f[0] - F[0], f[1] - F[1]) < x)) {
        var H = ze(this).on("dblclick.zoom");
        H && H.apply(this, arguments);
      }
    }
  }
  return w.wheelDelta = function(C) {
    return arguments.length ? (o = typeof C == "function" ? C : yo(+C), w) : o;
  }, w.filter = function(C) {
    return arguments.length ? (e = typeof C == "function" ? C : yo(!!C), w) : e;
  }, w.touchable = function(C) {
    return arguments.length ? (i = typeof C == "function" ? C : yo(!!C), w) : i;
  }, w.extent = function(C) {
    return arguments.length ? (t = typeof C == "function" ? C : yo([[+C[0][0], +C[0][1]], [+C[1][0], +C[1][1]]]), w) : t;
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
    return arguments.length ? (m = (C = +C) * C, w) : Math.sqrt(m);
  }, w.tapDistance = function(C) {
    return arguments.length ? (x = +C, w) : x;
  }, w;
}
const We = {
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
}, $n = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], Fl = ["Enter", " ", "Escape"], Bl = {
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
var Gt;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(Gt || (Gt = {}));
var Nt;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(Nt || (Nt = {}));
var Pn;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Pn || (Pn = {}));
const Kl = {
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
var Zo;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(Zo || (Zo = {}));
var oe;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(oe || (oe = {}));
const da = {
  [oe.Left]: oe.Right,
  [oe.Right]: oe.Left,
  [oe.Top]: oe.Bottom,
  [oe.Bottom]: oe.Top
};
function Xl(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Yl = (e) => "id" in e && "source" in e && "target" in e, Rm = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), ns = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), Kn = (e, t = [0, 0]) => {
  const { width: n, height: o } = lt(e), i = e.origin ?? t, s = n * i[0], a = o * i[1];
  return {
    x: e.position.x - s,
    y: e.position.y - a
  };
}, Lm = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, i) => {
    const s = typeof i == "string";
    let a = !t.nodeLookup && !s ? i : void 0;
    t.nodeLookup && (a = s ? t.nodeLookup.get(i) : ns(i) ? i : t.nodeLookup.get(i.id));
    const c = a ? Go(a, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return ui(o, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return di(n);
}, Xn = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((i) => {
    (t.filter === void 0 || t.filter(i)) && (n = ui(n, Go(i)), o = !0);
  }), o ? di(n) : { x: 0, y: 0, width: 0, height: 0 };
}, os = (e, t, [n, o, i] = [0, 0, 1], s = !1, a = !1) => {
  const c = {
    ...sn(t, [n, o, i]),
    width: t.width / i,
    height: t.height / i
  }, u = [];
  for (const l of e.values()) {
    const { measured: d, selectable: f = !0, hidden: p = !1 } = l;
    if (a && !f || p)
      continue;
    const h = d.width ?? l.width ?? l.initialWidth ?? null, y = d.height ?? l.height ?? l.initialHeight ?? null, m = Mn(c, Qt(l)), x = (h ?? 0) * (y ?? 0), w = s && m > 0;
    (!l.internals.handleBounds || w || m >= x || l.dragging) && u.push(l);
  }
  return u;
}, zm = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function Vm(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((i) => i.id)) : null;
  return e.forEach((i) => {
    i.measured.width && i.measured.height && (t?.includeHiddenNodes || !i.hidden) && (!o || o.has(i.id)) && n.set(i.id, i);
  }), n;
}
async function Om({ nodes: e, width: t, height: n, panZoom: o, minZoom: i, maxZoom: s }, a) {
  if (e.size === 0)
    return !0;
  const c = Vm(e, a), u = Xn(c), l = rs(u, t, n, a?.minZoom ?? i, a?.maxZoom ?? s, a?.padding ?? 0.1);
  return await o.setViewport(l, {
    duration: a?.duration,
    ease: a?.ease,
    interpolate: a?.interpolate
  }), !0;
}
function ql({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: i, onError: s }) {
  const a = n.get(e), c = a.parentId ? n.get(a.parentId) : void 0, { x: u, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, d = a.origin ?? o;
  let f = a.extent || i;
  if (a.extent === "parent" && !a.expandParent)
    if (!c)
      s?.("005", We.error005());
    else {
      const h = c.measured.width, y = c.measured.height;
      h && y && (f = [
        [u, l],
        [u + h, l + y]
      ]);
    }
  else c && Et(a.extent) && (f = [
    [a.extent[0][0] + u, a.extent[0][1] + l],
    [a.extent[1][0] + u, a.extent[1][1] + l]
  ]);
  const p = Et(f) ? Ct(t, f, a.measured) : t;
  return (a.measured.width === void 0 || a.measured.height === void 0) && s?.("015", We.error015()), {
    position: {
      x: p.x - u + (a.measured.width ?? 0) * d[0],
      y: p.y - l + (a.measured.height ?? 0) * d[1]
    },
    positionAbsolute: p
  };
}
async function Hm({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: i }) {
  const s = new Set(e.map((p) => p.id)), a = [];
  for (const p of n) {
    if (p.deletable === !1)
      continue;
    const h = s.has(p.id), y = !h && p.parentId && a.find((m) => m.id === p.parentId);
    (h || y) && a.push(p);
  }
  const c = new Set(t.map((p) => p.id)), u = o.filter((p) => p.deletable !== !1), d = zm(a, u);
  for (const p of u)
    c.has(p.id) && !d.find((y) => y.id === p.id) && d.push(p);
  if (!i)
    return {
      edges: d,
      nodes: a
    };
  const f = await i({
    nodes: a,
    edges: d
  });
  return typeof f == "boolean" ? f ? { edges: d, nodes: a } : { edges: [], nodes: [] } : f;
}
const Jt = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), Ct = (e = { x: 0, y: 0 }, t, n) => ({
  x: Jt(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: Jt(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Ul(e, t, n) {
  const { width: o, height: i } = lt(n), { x: s, y: a } = n.internals.positionAbsolute;
  return Ct(e, [
    [s, a],
    [s + o, a + i]
  ], t);
}
const fa = (e, t, n) => e < t ? Jt(Math.abs(e - t), 1, t) / t : e > n ? -Jt(Math.abs(e - n), 1, t) / t : 0, is = (e, t, n = 15, o = 40) => {
  const i = fa(e.x, o, t.width - o) * n, s = fa(e.y, o, t.height - o) * n;
  return [i, s];
}, ui = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), Ir = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), di = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), Qt = (e, t = [0, 0]) => {
  const { x: n, y: o } = ns(e) ? e.internals.positionAbsolute : Kn(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, Go = (e, t = [0, 0]) => {
  const { x: n, y: o } = ns(e) ? e.internals.positionAbsolute : Kn(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, Zl = (e, t) => di(ui(Ir(e), Ir(t))), Mn = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, pa = (e) => Ye(e.width) && Ye(e.height) && Ye(e.x) && Ye(e.y), Ye = (e) => !isNaN(e) && isFinite(e), Gl = (e, t) => (n, o) => {
}, Yn = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), sn = ({ x: e, y: t }, [n, o, i], s = !1, a = [1, 1]) => {
  const c = {
    x: (e - n) / i,
    y: (t - o) / i
  };
  return s ? Yn(c, a) : c;
}, en = ({ x: e, y: t }, [n, o, i]) => ({
  x: e * i + n,
  y: t * i + o
});
function zt(e, t) {
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
function Wm(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = zt(e, n), i = zt(e, t);
    return {
      top: o,
      right: i,
      bottom: o,
      left: i,
      x: i * 2,
      y: o * 2
    };
  }
  if (typeof e == "object") {
    const o = zt(e.top ?? e.y ?? 0, n), i = zt(e.bottom ?? e.y ?? 0, n), s = zt(e.left ?? e.x ?? 0, t), a = zt(e.right ?? e.x ?? 0, t);
    return { top: o, right: a, bottom: i, left: s, x: s + a, y: o + i };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function Fm(e, t, n, o, i, s) {
  const { x: a, y: c } = en(e, [t, n, o]), { x: u, y: l } = en({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), d = i - u, f = s - l;
  return {
    left: Math.floor(a),
    top: Math.floor(c),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const rs = (e, t, n, o, i, s) => {
  const a = Wm(s, t, n), c = (t - a.x) / e.width, u = (n - a.y) / e.height, l = Math.min(c, u), d = Jt(l, o, i), f = e.x + e.width / 2, p = e.y + e.height / 2, h = t / 2 - f * d, y = n / 2 - p * d, m = Fm(e, h, y, d, t, n), x = {
    left: Math.min(m.left - a.left, 0),
    top: Math.min(m.top - a.top, 0),
    right: Math.min(m.right - a.right, 0),
    bottom: Math.min(m.bottom - a.bottom, 0)
  };
  return {
    x: h - x.left + x.right,
    y: y - x.top + x.bottom,
    zoom: d
  };
}, Rn = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function Et(e) {
  return e != null && e !== "parent";
}
function lt(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function Jl(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function Ql(e, t = { width: 0, height: 0 }, n, o, i) {
  const s = { ...e }, a = o.get(n);
  if (a) {
    const c = a.origin || i;
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
function Bm() {
  let e, t;
  return { promise: new Promise((o, i) => {
    e = o, t = i;
  }), resolve: e, reject: t };
}
function Km(e) {
  return { ...Bl, ...e || {} };
}
function kn(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: i }) {
  const { x: s, y: a } = qe(e), c = sn({ x: s - (i?.left ?? 0), y: a - (i?.top ?? 0) }, o), { x: u, y: l } = n ? Yn(c, t) : c;
  return {
    xSnapped: u,
    ySnapped: l,
    ...c
  };
}
const ss = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), eu = (e) => e?.getRootNode?.() || window?.document, Xm = ["INPUT", "SELECT", "TEXTAREA"];
function tu(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : Xm.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const nu = (e) => "clientX" in e, qe = (e, t) => {
  const n = nu(e), o = n ? e.clientX : e.touches?.[0].clientX, i = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: i - (t?.top ?? 0)
  };
}, ga = (e, t, n, o, i) => {
  const s = t.querySelectorAll(`.${e}`);
  return !s || !s.length ? null : Array.from(s).map((a) => {
    const c = a.getBoundingClientRect();
    return {
      id: a.getAttribute("data-handleid"),
      type: e,
      nodeId: i,
      position: a.getAttribute("data-handlepos"),
      x: (c.left - n.left) / o,
      y: (c.top - n.top) / o,
      ...ss(a)
    };
  });
};
function ou({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: i, sourceControlY: s, targetControlX: a, targetControlY: c }) {
  const u = e * 0.125 + i * 0.375 + a * 0.375 + n * 0.125, l = t * 0.125 + s * 0.375 + c * 0.375 + o * 0.125, d = Math.abs(u - e), f = Math.abs(l - t);
  return [u, l, d, f];
}
function mo(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function ya({ pos: e, x1: t, y1: n, x2: o, y2: i, c: s }) {
  switch (e) {
    case oe.Left:
      return [t - mo(t - o, s), n];
    case oe.Right:
      return [t + mo(o - t, s), n];
    case oe.Top:
      return [t, n - mo(n - i, s)];
    case oe.Bottom:
      return [t, n + mo(i - n, s)];
  }
}
function iu({ sourceX: e, sourceY: t, sourcePosition: n = oe.Bottom, targetX: o, targetY: i, targetPosition: s = oe.Top, curvature: a = 0.25 }) {
  const [c, u] = ya({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: i,
    c: a
  }), [l, d] = ya({
    pos: s,
    x1: o,
    y1: i,
    x2: e,
    y2: t,
    c: a
  }), [f, p, h, y] = ou({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: i,
    sourceControlX: c,
    sourceControlY: u,
    targetControlX: l,
    targetControlY: d
  });
  return [
    `M${e},${t} C${c},${u} ${l},${d} ${o},${i}`,
    f,
    p,
    h,
    y
  ];
}
function ru({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const i = Math.abs(n - e) / 2, s = n < e ? n + i : n - i, a = Math.abs(o - t) / 2, c = o < t ? o + a : o - a;
  return [s, c, i, a];
}
function Ym({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: i = !1, zIndexMode: s = "basic" }) {
  if (s === "manual")
    return o;
  const a = i && n ? o + 1e3 : o, c = Math.max(e.parentId || i && e.selected ? e.internals.z : 0, t.parentId || i && t.selected ? t.internals.z : 0);
  return a + c;
}
function qm({ sourceNode: e, targetNode: t, width: n, height: o, transform: i }) {
  const s = ui(Go(e), Go(t));
  s.x === s.x2 && (s.x2 += 1), s.y === s.y2 && (s.y2 += 1);
  const a = {
    x: -i[0] / i[2],
    y: -i[1] / i[2],
    width: n / i[2],
    height: o / i[2]
  };
  return Mn(a, di(s)) > 0;
}
const su = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, Um = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), Zm = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", We.error006()), t;
  const o = n.getEdgeId || su;
  let i;
  return Yl(e) ? i = { ...e } : i = {
    ...e,
    id: o(e)
  }, Um(i, t) ? t : (i.sourceHandle === null && delete i.sourceHandle, i.targetHandle === null && delete i.targetHandle, t.concat(i));
}, Gm = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: i, ...s } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", We.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return o.onError?.("007", We.error007(i)), n;
  const c = o.getEdgeId || su, u = {
    ...s,
    id: o.shouldReplaceId ? c(t) : i,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== i).concat(u);
};
function au({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [i, s, a, c] = ru({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, i, s, a, c];
}
const ma = {
  [oe.Left]: { x: -1, y: 0 },
  [oe.Right]: { x: 1, y: 0 },
  [oe.Top]: { x: 0, y: -1 },
  [oe.Bottom]: { x: 0, y: 1 }
}, Jm = ({ source: e, sourcePosition: t = oe.Bottom, target: n }) => t === oe.Left || t === oe.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, xa = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Qm({ source: e, sourcePosition: t = oe.Bottom, target: n, targetPosition: o = oe.Top, center: i, offset: s, stepPosition: a }) {
  const c = ma[t], u = ma[o], l = { x: e.x + c.x * s, y: e.y + c.y * s }, d = { x: n.x + u.x * s, y: n.y + u.y * s }, f = Jm({
    source: l,
    sourcePosition: t,
    target: d
  }), p = f.x !== 0 ? "x" : "y", h = f[p];
  let y = [], m, x;
  const w = { x: 0, y: 0 }, b = { x: 0, y: 0 }, [, , g, v] = ru({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[p] * u[p] === -1) {
    p === "x" ? (m = i.x ?? l.x + (d.x - l.x) * a, x = i.y ?? (l.y + d.y) / 2) : (m = i.x ?? (l.x + d.x) / 2, x = i.y ?? l.y + (d.y - l.y) * a);
    const k = [
      { x: m, y: l.y },
      { x: m, y: d.y }
    ], D = [
      { x: l.x, y: x },
      { x: d.x, y: x }
    ];
    c[p] === h ? y = p === "x" ? k : D : y = p === "x" ? D : k;
  } else {
    const k = [{ x: l.x, y: d.y }], D = [{ x: d.x, y: l.y }];
    if (p === "x" ? y = c.x === h ? D : k : y = c.y === h ? k : D, t === o) {
      const C = Math.abs(e[p] - n[p]);
      if (C <= s) {
        const I = Math.min(s - 1, s - C);
        c[p] === h ? w[p] = (l[p] > e[p] ? -1 : 1) * I : b[p] = (d[p] > n[p] ? -1 : 1) * I;
      }
    }
    if (t !== o) {
      const C = p === "x" ? "y" : "x", I = c[p] === u[C], E = l[C] > d[C], T = l[C] < d[C];
      (c[p] === 1 && (!I && E || I && T) || c[p] !== 1 && (!I && T || I && E)) && (y = p === "x" ? k : D);
    }
    const L = { x: l.x + w.x, y: l.y + w.y }, A = { x: d.x + b.x, y: d.y + b.y }, _ = Math.max(Math.abs(L.x - y[0].x), Math.abs(A.x - y[0].x)), R = Math.max(Math.abs(L.y - y[0].y), Math.abs(A.y - y[0].y));
    _ >= R ? (m = (L.x + A.x) / 2, x = y[0].y) : (m = y[0].x, x = (L.y + A.y) / 2);
  }
  const j = { x: l.x + w.x, y: l.y + w.y }, N = { x: d.x + b.x, y: d.y + b.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...j.x !== y[0].x || j.y !== y[0].y ? [j] : [],
    ...y,
    ...N.x !== y[y.length - 1].x || N.y !== y[y.length - 1].y ? [N] : [],
    n
  ], m, x, g, v];
}
function ex(e, t, n, o) {
  const i = Math.min(xa(e, t) / 2, xa(t, n) / 2, o), { x: s, y: a } = t;
  if (e.x === s && s === n.x || e.y === a && a === n.y)
    return `L${s} ${a}`;
  if (e.y === a) {
    const l = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${s + i * l},${a}Q ${s},${a} ${s},${a + i * d}`;
  }
  const c = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${s},${a + i * u}Q ${s},${a} ${s + i * c},${a}`;
}
function Jo({ sourceX: e, sourceY: t, sourcePosition: n = oe.Bottom, targetX: o, targetY: i, targetPosition: s = oe.Top, borderRadius: a = 5, centerX: c, centerY: u, offset: l = 20, stepPosition: d = 0.5 }) {
  const [f, p, h, y, m] = Qm({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: i },
    targetPosition: s,
    center: { x: c, y: u },
    offset: l,
    stepPosition: d
  });
  let x = `M${f[0].x} ${f[0].y}`;
  for (let w = 1; w < f.length - 1; w++)
    x += ex(f[w - 1], f[w], f[w + 1], a);
  return x += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [x, p, h, y, m];
}
function wa(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function tx(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!wa(t) || !wa(n))
    return null;
  const o = t.internals.handleBounds || va(t.handles), i = n.internals.handleBounds || va(n.handles), s = ba(o?.source ?? [], e.sourceHandle), a = ba(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === Gt.Strict ? i?.target ?? [] : (i?.target ?? []).concat(i?.source ?? []),
    e.targetHandle
  );
  if (!s || !a)
    return e.onError?.("008", We.error008(s ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = s?.position || oe.Bottom, u = a?.position || oe.Top, l = kt(t, s, c), d = kt(n, a, u);
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
  for (const o of e)
    o.width = o.width ?? 1, o.height = o.height ?? 1, o.type === "source" ? t.push(o) : o.type === "target" && n.push(o);
  return {
    source: t,
    target: n
  };
}
function kt(e, t, n = oe.Left, o = !1) {
  const i = (t?.x ?? 0) + e.internals.positionAbsolute.x, s = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: a, height: c } = t ?? lt(e);
  if (o)
    return { x: i + a / 2, y: s + c / 2 };
  switch (t?.position ?? n) {
    case oe.Top:
      return { x: i + a / 2, y: s };
    case oe.Right:
      return { x: i + a, y: s + c / 2 };
    case oe.Bottom:
      return { x: i + a / 2, y: s + c };
    case oe.Left:
      return { x: i, y: s + c / 2 };
  }
}
function ba(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Ar(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function nx(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: i }) {
  const s = /* @__PURE__ */ new Set();
  return e.reduce((a, c) => ([c.markerStart || o, c.markerEnd || i].forEach((u) => {
    if (u && typeof u == "object") {
      const l = Ar(u, t);
      s.has(l) || (a.push({ id: l, color: u.color || n, ...u }), s.add(l));
    }
  }), a), []).sort((a, c) => a.id.localeCompare(c.id));
}
const cu = 1e3, ox = 10, as = {
  nodeOrigin: [0, 0],
  nodeExtent: $n,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, ix = {
  ...as,
  checkEquality: !0
};
function cs(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function rx(e, t, n) {
  const o = cs(as, n);
  for (const i of e.values())
    if (i.parentId)
      us(i, e, t, o);
    else {
      const s = Kn(i, o.nodeOrigin), a = Et(i.extent) ? i.extent : o.nodeExtent, c = Ct(s, a, lt(i));
      i.internals.positionAbsolute = c;
    }
}
function sx(e, t) {
  if (!e.handles)
    return e.measured ? t?.internals.handleBounds : void 0;
  const n = [], o = [];
  for (const i of e.handles) {
    const s = {
      id: i.id,
      width: i.width ?? 1,
      height: i.height ?? 1,
      nodeId: e.id,
      x: i.x,
      y: i.y,
      position: i.position,
      type: i.type
    };
    i.type === "source" ? n.push(s) : i.type === "target" && o.push(s);
  }
  return {
    source: n,
    target: o
  };
}
function ls(e) {
  return e === "manual";
}
function _r(e, t, n, o = {}) {
  const i = cs(ix, o), s = { i: 0 }, a = new Map(t), c = i?.elevateNodesOnSelect && !ls(i.zIndexMode) ? cu : 0;
  let u = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = a.get(d.id);
    if (i.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const p = Kn(d, i.nodeOrigin), h = Et(d.extent) ? d.extent : i.nodeExtent, y = Ct(p, h, lt(d));
      f = {
        ...i.defaults,
        ...d,
        measured: {
          width: d.measured?.width,
          height: d.measured?.height
        },
        internals: {
          positionAbsolute: y,
          // if user re-initializes the node or removes `measured` for whatever reason, we reset the handleBounds so that the node gets re-measured
          handleBounds: sx(d, f),
          z: lu(d, c, i.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (u = !1), d.parentId && us(f, t, n, o, s), l ||= d.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: l };
}
function ax(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function us(e, t, n, o, i) {
  const { elevateNodesOnSelect: s, nodeOrigin: a, nodeExtent: c, zIndexMode: u } = cs(as, o), l = e.parentId, d = t.get(l);
  if (!d) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  ax(e, n), i && !d.parentId && d.internals.rootParentIndex === void 0 && u === "auto" && (d.internals.rootParentIndex = ++i.i, d.internals.z = d.internals.z + i.i * ox), i && d.internals.rootParentIndex !== void 0 && (i.i = d.internals.rootParentIndex);
  const f = s && !ls(u) ? cu : 0, { x: p, y: h, z: y } = cx(e, d, a, c, f, u), { positionAbsolute: m } = e.internals, x = p !== m.x || h !== m.y;
  (x || y !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: x ? { x: p, y: h } : m,
      z: y
    }
  });
}
function lu(e, t, n) {
  const o = Ye(e.zIndex) ? e.zIndex : 0;
  return ls(n) ? o : o + (e.selected ? t : 0);
}
function cx(e, t, n, o, i, s) {
  const { x: a, y: c } = t.internals.positionAbsolute, u = lt(e), l = Kn(e, n), d = Et(e.extent) ? Ct(l, e.extent, u) : l;
  let f = Ct({ x: a + d.x, y: c + d.y }, o, u);
  e.extent === "parent" && (f = Ul(f, u, t));
  const p = lu(e, i, s), h = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: h >= p ? h + 1 : p
  };
}
function ds(e, t, n, o = [0, 0]) {
  const i = [], s = /* @__PURE__ */ new Map();
  for (const a of e) {
    const c = t.get(a.parentId);
    if (!c)
      continue;
    const u = s.get(a.parentId)?.expandedRect ?? Qt(c), l = Zl(u, a.rect);
    s.set(a.parentId, { expandedRect: l, parent: c });
  }
  return s.size > 0 && s.forEach(({ expandedRect: a, parent: c }, u) => {
    const l = c.internals.positionAbsolute, d = lt(c), f = c.origin ?? o, p = a.x < l.x ? Math.round(Math.abs(l.x - a.x)) : 0, h = a.y < l.y ? Math.round(Math.abs(l.y - a.y)) : 0, y = Math.max(d.width, Math.round(a.width)), m = Math.max(d.height, Math.round(a.height)), x = (y - d.width) * f[0], w = (m - d.height) * f[1];
    (p > 0 || h > 0 || x || w) && (i.push({
      id: u,
      type: "position",
      position: {
        x: c.position.x - p + x,
        y: c.position.y - h + w
      }
    }), n.get(u)?.forEach((b) => {
      e.some((g) => g.id === b.id) || i.push({
        id: b.id,
        type: "position",
        position: {
          x: b.position.x + p,
          y: b.position.y + h
        }
      });
    })), (d.width < a.width || d.height < a.height || p || h) && i.push({
      id: u,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: y + (p ? f[0] * p - x : 0),
        height: m + (h ? f[1] * h - w : 0)
      }
    });
  }), i;
}
function lx(e, t, n, o, i, s, a) {
  const c = o?.querySelector(".xyflow__viewport");
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
    const m = ss(h.nodeElement), x = y.measured.width !== m.width || y.measured.height !== m.height;
    if (!!(m.width && m.height && (x || !y.internals.handleBounds || h.force))) {
      const b = h.nodeElement.getBoundingClientRect(), g = Et(y.extent) ? y.extent : s;
      let { positionAbsolute: v } = y.internals;
      y.parentId && y.extent === "parent" ? v = Ul(v, m, t.get(y.parentId)) : g && (v = Ct(v, g, m));
      const j = {
        ...y,
        measured: m,
        internals: {
          ...y.internals,
          positionAbsolute: v,
          handleBounds: {
            source: ga("source", h.nodeElement, b, f, y.id),
            target: ga("target", h.nodeElement, b, f, y.id)
          }
        }
      };
      t.set(y.id, j), y.parentId && us(j, t, n, { nodeOrigin: i, zIndexMode: a }), u = !0, x && (l.push({
        id: y.id,
        type: "dimensions",
        dimensions: m
      }), y.expandParent && y.parentId && p.push({
        id: y.id,
        parentId: y.parentId,
        rect: Qt(j, i)
      }));
    }
  }
  if (p.length > 0) {
    const h = ds(p, t, n, i);
    l.push(...h);
  }
  return { changes: l, updatedInternals: u };
}
async function ux({ delta: e, panZoom: t, transform: n, translateExtent: o, width: i, height: s }) {
  if (!t || !e.x && !e.y)
    return !1;
  const a = await t.setViewportConstrained({
    x: n[0] + e.x,
    y: n[1] + e.y,
    zoom: n[2]
  }, [
    [0, 0],
    [i, s]
  ], o);
  return !!a && (a.x !== n[0] || a.y !== n[1] || a.k !== n[2]);
}
function Na(e, t, n, o, i, s) {
  let a = i;
  const c = o.get(a) || /* @__PURE__ */ new Map();
  o.set(a, c.set(n, t)), a = `${i}-${e}`;
  const u = o.get(a) || /* @__PURE__ */ new Map();
  if (o.set(a, u.set(n, t)), s) {
    a = `${i}-${e}-${s}`;
    const l = o.get(a) || /* @__PURE__ */ new Map();
    o.set(a, l.set(n, t));
  }
}
function uu(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: i, target: s, sourceHandle: a = null, targetHandle: c = null } = o, u = { edgeId: o.id, source: i, target: s, sourceHandle: a, targetHandle: c }, l = `${i}-${a}--${s}-${c}`, d = `${s}-${c}--${i}-${a}`;
    Na("source", u, d, e, i, a), Na("target", u, l, e, s, c), t.set(o.id, o);
  }
}
function du(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : du(n, t) : !1;
}
function ja(e, t, n) {
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
function dx(e, t, n, o) {
  const i = /* @__PURE__ */ new Map();
  for (const [s, a] of e)
    if ((a.selected || a.id === o) && (!a.parentId || !du(a, e)) && (a.draggable || t && typeof a.draggable > "u")) {
      const c = e.get(s);
      c && i.set(s, {
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
  return i;
}
function ir({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
  const i = [];
  for (const [a, c] of t) {
    const u = n.get(a)?.internals.userNode;
    u && i.push({
      ...u,
      position: c.position,
      dragging: o
    });
  }
  if (!e)
    return [i[0], i];
  const s = n.get(e)?.internals.userNode;
  return [
    s ? {
      ...s,
      position: t.get(e)?.position || s.position,
      dragging: o
    } : i[0],
    i
  ];
}
function fx({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const i = e.values().next().value;
  if (!i)
    return null;
  const s = {
    x: n - i.distance.x,
    y: o - i.distance.y
  }, a = Yn(s, t);
  return {
    x: a.x - s.x,
    y: a.y - s.y
  };
}
function px({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: i }) {
  let s = { x: null, y: null }, a = 0, c = /* @__PURE__ */ new Map(), u = !1, l = { x: 0, y: 0 }, d = null, f = !1, p = null, h = !1, y = !1, m = null;
  function x({ noDragClassName: b, handleSelector: g, domNode: v, isSelectable: j, nodeId: N, nodeClickDistance: S = 0 }) {
    p = ze(v);
    function k({ x: _, y: R }) {
      const { nodeLookup: C, nodeExtent: I, snapGrid: E, snapToGrid: T, nodeOrigin: $, onNodeDrag: P, onSelectionDrag: F, onError: H, updateNodePositions: O } = t();
      s = { x: _, y: R };
      let U = !1;
      const q = c.size > 1, ne = q && I ? Ir(Xn(c)) : null, ce = q && T ? fx({
        dragItems: c,
        snapGrid: E,
        x: _,
        y: R
      }) : null;
      for (const [Z, z] of c) {
        if (!C.has(Z))
          continue;
        let X = { x: _ - z.distance.x, y: R - z.distance.y };
        T && (X = ce ? {
          x: Math.round(X.x + ce.x),
          y: Math.round(X.y + ce.y)
        } : Yn(X, E));
        let le = null;
        if (q && I && !z.extent && ne) {
          const { positionAbsolute: te } = z.internals, de = te.x - ne.x + I[0][0], V = te.x + z.measured.width - ne.x2 + I[1][0], Q = te.y - ne.y + I[0][1], he = te.y + z.measured.height - ne.y2 + I[1][1];
          le = [
            [de, Q],
            [V, he]
          ];
        }
        const { position: ae, positionAbsolute: J } = ql({
          nodeId: Z,
          nextPosition: X,
          nodeLookup: C,
          nodeExtent: le || I,
          nodeOrigin: $,
          onError: H
        });
        U = U || z.position.x !== ae.x || z.position.y !== ae.y, z.position = ae, z.internals.positionAbsolute = J;
      }
      if (y = y || U, !!U && (O(c, !0), m && (o || P || !N && F))) {
        const [Z, z] = ir({
          nodeId: N,
          dragItems: c,
          nodeLookup: C
        });
        o?.(m, c, Z, z), P?.(m, Z, z), N || F?.(m, z);
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
      const [E, T] = is(l, d, C);
      (E !== 0 || T !== 0) && (s.x = (s.x ?? 0) - E / _[2], s.y = (s.y ?? 0) - T / _[2], await R({ x: E, y: T }) && k(s)), a = requestAnimationFrame(D);
    }
    function L(_) {
      const { nodeLookup: R, multiSelectionActive: C, nodesDraggable: I, transform: E, snapGrid: T, snapToGrid: $, selectNodesOnDrag: P, onNodeDragStart: F, onSelectionDragStart: H, unselectNodesAndEdges: O } = t();
      f = !0, (!P || !j) && !C && N && (R.get(N)?.selected || O()), j && P && N && e?.(N);
      const U = kn(_.sourceEvent, { transform: E, snapGrid: T, snapToGrid: $, containerBounds: d });
      if (s = U, c = dx(R, I, U, N), c.size > 0 && (n || F || !N && H)) {
        const [q, ne] = ir({
          nodeId: N,
          dragItems: c,
          nodeLookup: R
        });
        n?.(_.sourceEvent, c, q, ne), F?.(_.sourceEvent, q, ne), N || H?.(_.sourceEvent, ne);
      }
    }
    const A = kl().clickDistance(S).on("start", (_) => {
      const { domNode: R, nodeDragThreshold: C, transform: I, snapGrid: E, snapToGrid: T } = t();
      d = R?.getBoundingClientRect() || null, h = !1, y = !1, m = _.sourceEvent, C === 0 && L(_), s = kn(_.sourceEvent, { transform: I, snapGrid: E, snapToGrid: T, containerBounds: d }), l = qe(_.sourceEvent, d);
    }).on("drag", (_) => {
      const { autoPanOnNodeDrag: R, transform: C, snapGrid: I, snapToGrid: E, nodeDragThreshold: T, nodeLookup: $ } = t(), P = kn(_.sourceEvent, { transform: C, snapGrid: I, snapToGrid: E, containerBounds: d });
      if (m = _.sourceEvent, (_.sourceEvent.type === "touchmove" && _.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      N && !$.has(N)) && (h = !0), !h) {
        if (!u && R && f && (u = !0, D()), !f) {
          const F = qe(_.sourceEvent, d), H = F.x - l.x, O = F.y - l.y;
          Math.sqrt(H * H + O * O) > T && L(_);
        }
        (s.x !== P.xSnapped || s.y !== P.ySnapped) && c && f && (l = qe(_.sourceEvent, d), k(P));
      }
    }).on("end", (_) => {
      if (!f || h) {
        h && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (u = !1, f = !1, cancelAnimationFrame(a), c.size > 0) {
        const { nodeLookup: R, updateNodePositions: C, onNodeDragStop: I, onSelectionDragStop: E } = t();
        if (y && (C(c, !1), y = !1), i || I || !N && E) {
          const [T, $] = ir({
            nodeId: N,
            dragItems: c,
            nodeLookup: R,
            dragging: !1
          });
          i?.(_.sourceEvent, c, T, $), I?.(_.sourceEvent, T, $), N || E?.(_.sourceEvent, $);
        }
      }
    }).filter((_) => {
      const R = _.target;
      return !_.button && (!b || !ja(R, `.${b}`, v)) && (!g || ja(R, g, v));
    });
    p.call(A);
  }
  function w() {
    p?.on(".drag", null);
  }
  return {
    update: x,
    destroy: w
  };
}
function hx(e, t, n) {
  const o = [], i = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const s of t.values())
    Mn(i, Qt(s)) > 0 && o.push(s);
  return o;
}
const gx = 250;
function yx(e, t, n, o) {
  let i = [], s = 1 / 0;
  const a = hx(e, n, t + gx);
  for (const c of a) {
    const u = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of u) {
      if (o.nodeId === l.nodeId && o.type === l.type && o.id === l.id)
        continue;
      const { x: d, y: f } = kt(c, l, l.position, !0), p = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(f - e.y, 2));
      p > t || (p < s ? (i = [{ ...l, x: d, y: f }], s = p) : p === s && i.push({ ...l, x: d, y: f }));
    }
  }
  if (!i.length)
    return null;
  if (i.length > 1) {
    const c = o.type === "source" ? "target" : "source";
    return i.find((u) => u.type === c) ?? i[0];
  }
  return i[0];
}
function fu(e, t, n, o, i, s = !1) {
  const a = o.get(e);
  if (!a)
    return null;
  const c = i === "strict" ? a.internals.handleBounds?.[t] : [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []], u = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return u && s ? { ...u, ...kt(a, u, u.position, !0) } : u;
}
function pu(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function mx(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const hu = () => !0;
function xx(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: i, edgeUpdaterType: s, isTarget: a, domNode: c, nodeLookup: u, lib: l, autoPanOnConnect: d, flowId: f, panBy: p, cancelConnection: h, onConnectStart: y, onConnect: m, onConnectEnd: x, isValidConnection: w = hu, onReconnectEnd: b, updateConnection: g, getTransform: v, getFromHandle: j, autoPanSpeed: N, dragThreshold: S = 1, handleDomNode: k }) {
  const D = eu(e.target);
  let L = 0, A;
  const { x: _, y: R } = qe(e), C = pu(s, k), I = c?.getBoundingClientRect();
  let E = !1;
  if (!I || !C)
    return;
  const T = fu(i, C, o, u, t);
  if (!T)
    return;
  let $ = qe(e, I), P = !1, F = null, H = !1, O = null;
  function U() {
    if (!d || !I)
      return;
    const [ae, J] = is($, I, N);
    p({ x: ae, y: J }), L = requestAnimationFrame(U);
  }
  const q = {
    ...T,
    nodeId: i,
    type: C,
    position: T.position
  }, ne = u.get(i);
  let Z = {
    inProgress: !0,
    isValid: null,
    from: kt(ne, q, oe.Left, !0),
    fromHandle: q,
    fromPosition: q.position,
    fromNode: ne,
    to: $,
    toHandle: null,
    toPosition: da[q.position],
    toNode: null,
    pointer: $
  };
  function z() {
    E = !0, g(Z), y?.(e, { nodeId: i, handleId: o, handleType: C });
  }
  S === 0 && z();
  function X(ae) {
    if (!E) {
      const { x: he, y: we } = qe(ae), Ie = he - _, Ae = we - R;
      if (!(Ie * Ie + Ae * Ae > S * S))
        return;
      z();
    }
    if (!j() || !q) {
      le(ae);
      return;
    }
    const J = v();
    $ = qe(ae, I), A = yx(sn($, J, !1, [1, 1]), n, u, q), P || (U(), P = !0);
    const te = gu(ae, {
      handle: A,
      connectionMode: t,
      fromNodeId: i,
      fromHandleId: o,
      fromType: a ? "target" : "source",
      isValidConnection: w,
      doc: D,
      lib: l,
      flowId: f,
      nodeLookup: u
    });
    O = te.handleDomNode, F = te.connection, H = mx(!!A, te.isValid);
    const de = u.get(i), V = de ? kt(de, q, oe.Left, !0) : Z.from, Q = {
      ...Z,
      from: V,
      isValid: H,
      to: te.toHandle && H ? en({ x: te.toHandle.x, y: te.toHandle.y }, J) : $,
      toHandle: te.toHandle,
      toPosition: H && te.toHandle ? te.toHandle.position : da[q.position],
      toNode: te.toHandle ? u.get(te.toHandle.nodeId) : null,
      pointer: $
    };
    g(Q), Z = Q;
  }
  function le(ae) {
    if (!("touches" in ae && ae.touches.length > 0)) {
      if (E) {
        (A || O) && F && H && m?.(F);
        const { inProgress: J, ...te } = Z, de = {
          ...te,
          toPosition: Z.toHandle ? Z.toPosition : null
        };
        x?.(ae, de), s && b?.(ae, de);
      }
      h(), cancelAnimationFrame(L), P = !1, H = !1, F = null, O = null, D.removeEventListener("mousemove", X), D.removeEventListener("mouseup", le), D.removeEventListener("touchmove", X), D.removeEventListener("touchend", le);
    }
  }
  D.addEventListener("mousemove", X), D.addEventListener("mouseup", le), D.addEventListener("touchmove", X), D.addEventListener("touchend", le);
}
function gu(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: i, fromType: s, doc: a, lib: c, flowId: u, isValidConnection: l = hu, nodeLookup: d }) {
  const f = s === "target", p = t ? a.querySelector(`.${c}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: h, y } = qe(e), m = a.elementFromPoint(h, y), x = m?.classList.contains(`${c}-flow__handle`) ? m : p, w = {
    handleDomNode: x,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (x) {
    const b = pu(void 0, x), g = x.getAttribute("data-nodeid"), v = x.getAttribute("data-handleid"), j = x.classList.contains("connectable"), N = x.classList.contains("connectableend");
    if (!g || !b)
      return w;
    const S = {
      source: f ? g : o,
      sourceHandle: f ? v : i,
      target: f ? o : g,
      targetHandle: f ? i : v
    };
    w.connection = S;
    const D = j && N && (n === Gt.Strict ? f && b === "source" || !f && b === "target" : g !== o || v !== i);
    w.isValid = D && l(S), w.toHandle = fu(g, b, v, d, n, !0);
  }
  return w;
}
const Dr = {
  onPointerDown: xx,
  isValid: gu
};
function wx({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const i = ze(e);
  function s({ translateExtent: c, width: u, height: l, zoomStep: d = 1, pannable: f = !0, zoomable: p = !0, inversePan: h = !1 }) {
    const y = (g) => {
      if (g.sourceEvent.type !== "wheel" || !t)
        return;
      const v = n(), j = g.sourceEvent.ctrlKey && Rn() ? 10 : 1, N = -g.sourceEvent.deltaY * (g.sourceEvent.deltaMode === 1 ? 0.05 : g.sourceEvent.deltaMode ? 1 : 2e-3) * d, S = v[2] * Math.pow(2, N * j);
      t.scaleTo(S);
    };
    let m = [0, 0];
    const x = (g) => {
      (g.sourceEvent.type === "mousedown" || g.sourceEvent.type === "touchstart") && (m = [
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
      ], N = [j[0] - m[0], j[1] - m[1]];
      m = j;
      const S = o() * Math.max(v[2], Math.log(v[2])) * (h ? -1 : 1), k = {
        x: v[0] - N[0] * S,
        y: v[1] - N[1] * S
      }, D = [
        [0, 0],
        [u, l]
      ];
      t.setViewportConstrained({
        x: k.x,
        y: k.y,
        zoom: v[2]
      }, D, c);
    }, b = Wl().on("start", x).on("zoom", f ? w : null).on("zoom.wheel", p ? y : null);
    i.call(b, {});
  }
  function a() {
    i.on("zoom", null);
  }
  return {
    update: s,
    destroy: a,
    pointer: Ke
  };
}
const fi = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), rr = ({ x: e, y: t, zoom: n }) => li.translate(e, t).scale(n), Ht = (e, t) => e.target.closest(`.${t}`), yu = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), vx = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, sr = (e, t = 0, n = vx, o = () => {
}) => {
  const i = typeof t == "number" && t > 0;
  return i || o(), i ? e.transition().duration(t).ease(n).on("end", o) : e;
}, mu = (e) => {
  const t = e.ctrlKey && Rn() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function bx({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: i, panOnScrollSpeed: s, zoomOnPinch: a, onPanZoomStart: c, onPanZoom: u, onPanZoomEnd: l }) {
  return (d) => {
    if (Ht(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && a) {
      const x = Ke(d), w = mu(d), b = f * Math.pow(2, w);
      o.scaleTo(n, b, x, d);
      return;
    }
    const p = d.deltaMode === 1 ? 20 : 1;
    let h = i === Nt.Vertical ? 0 : d.deltaX * p, y = i === Nt.Horizontal ? 0 : d.deltaY * p;
    !Rn() && d.shiftKey && i !== Nt.Vertical && (h = d.deltaY * p, y = 0), o.translateBy(
      n,
      -(h / f) * s,
      -(y / f) * s,
      // @ts-ignore
      { internal: !0 }
    );
    const m = fi(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(d, m), e.panScrollTimeout = setTimeout(() => {
      l?.(d, m), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(d, m));
  };
}
function Nx({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, i) {
    const s = o.type === "wheel", a = !t && s && !o.ctrlKey, c = Ht(o, e);
    if (o.ctrlKey && s && c && o.preventDefault(), a || c)
      return null;
    o.preventDefault(), n.call(this, o, i);
  };
}
function jx({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const i = fi(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = i, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, i);
  };
}
function Sx({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: i }) {
  return (s) => {
    e.usedRightMouseButton = !!(n && yu(t, e.mouseButton ?? 0)), s.sourceEvent?.sync || o([s.transform.x, s.transform.y, s.transform.k]), i && !s.sourceEvent?.internal && i?.(s.sourceEvent, fi(s.transform));
  };
}
function Cx({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: i, onPaneContextMenu: s }) {
  return (a) => {
    if (!a.sourceEvent?.internal && (e.isZoomingOrPanning = !1, s && yu(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && a.sourceEvent && s(a.sourceEvent), e.usedRightMouseButton = !1, o(!1), i)) {
      const c = fi(a.transform);
      e.prevViewport = c, clearTimeout(e.timerId), e.timerId = setTimeout(
        () => {
          i?.(a.sourceEvent, c);
        },
        // we need a setTimeout for panOnScroll to suppress multiple end events fired during scroll
        n ? 150 : 0
      );
    }
  };
}
function Ex({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: i, zoomOnDoubleClick: s, userSelectionActive: a, noWheelClassName: c, noPanClassName: u, lib: l, connectionInProgress: d }) {
  return (f) => {
    const p = e || t, h = n && f.ctrlKey, y = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (Ht(f, `${l}-flow__node`) || Ht(f, `${l}-flow__edge`)))
      return !0;
    if (!o && !p && !i && !s && !n || a || d && !y || Ht(f, c) && y || Ht(f, u) && (!y || i && y && !e) || !n && f.ctrlKey && y)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!p && !i && !h && y || !o && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(o) && !o.includes(f.button) && f.type === "mousedown")
      return !1;
    const m = Array.isArray(o) && o.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || y) && m;
  };
}
function kx({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: i, onPanZoom: s, onPanZoomStart: a, onPanZoomEnd: c, onDraggingChange: u }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = Wl().scaleExtent([t, n]).translateExtent(o), p = ze(e).call(f);
  b({
    x: i.x,
    y: i.y,
    zoom: Jt(i.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], o);
  const h = p.on("wheel.zoom"), y = p.on("dblclick.zoom");
  f.wheelDelta(mu);
  async function m(A, _) {
    return p ? new Promise((R) => {
      f?.interpolate(_?.interpolate === "linear" ? En : ko).transform(sr(p, _?.duration, _?.ease, () => R(!0)), A);
    }) : !1;
  }
  function x({ noWheelClassName: A, noPanClassName: _, onPaneContextMenu: R, userSelectionActive: C, panOnScroll: I, panOnDrag: E, panOnScrollMode: T, panOnScrollSpeed: $, preventScrolling: P, zoomOnPinch: F, zoomOnScroll: H, zoomOnDoubleClick: O, zoomActivationKeyPressed: U, lib: q, onTransformChange: ne, connectionInProgress: ce, paneClickDistance: Z, selectionOnDrag: z }) {
    C && !l.isZoomingOrPanning && w();
    const X = I && !U && !C;
    f.clickDistance(z ? 1 / 0 : !Ye(Z) || Z < 0 ? 0 : Z);
    const le = X ? bx({
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
    }) : Nx({
      noWheelClassName: A,
      preventScrolling: P,
      d3ZoomHandler: h
    });
    p.on("wheel.zoom", le, { passive: !1 });
    const ae = jx({
      zoomPanValues: l,
      onDraggingChange: u,
      onPanZoomStart: a
    });
    f.on("start", ae);
    const J = Sx({
      zoomPanValues: l,
      panOnDrag: E,
      onPaneContextMenu: !!R,
      onPanZoom: s,
      onTransformChange: ne
    });
    f.on("zoom", J);
    const te = Cx({
      zoomPanValues: l,
      panOnDrag: E,
      panOnScroll: I,
      onPaneContextMenu: R,
      onPanZoomEnd: c,
      onDraggingChange: u
    });
    f.on("end", te);
    const de = Ex({
      zoomActivationKeyPressed: U,
      panOnDrag: E,
      zoomOnScroll: H,
      panOnScroll: I,
      zoomOnDoubleClick: O,
      zoomOnPinch: F,
      userSelectionActive: C,
      noPanClassName: _,
      noWheelClassName: A,
      lib: q,
      connectionInProgress: ce
    });
    f.filter(de), O ? p.on("dblclick.zoom", y) : p.on("dblclick.zoom", null);
  }
  function w() {
    f.on("zoom", null);
  }
  async function b(A, _, R) {
    const C = rr(A), I = f?.constrain()(C, _, R);
    return I && await m(I), I;
  }
  async function g(A, _) {
    const R = rr(A);
    return await m(R, _), R;
  }
  function v(A) {
    if (p) {
      const _ = rr(A), R = p.property("__zoom");
      (R.k !== A.zoom || R.x !== A.x || R.y !== A.y) && f?.transform(p, _, null, { sync: !0 });
    }
  }
  function j() {
    const A = p ? Hl(p.node()) : { x: 0, y: 0, k: 1 };
    return { x: A.x, y: A.y, zoom: A.k };
  }
  async function N(A, _) {
    return p ? new Promise((R) => {
      f?.interpolate(_?.interpolate === "linear" ? En : ko).scaleTo(sr(p, _?.duration, _?.ease, () => R(!0)), A);
    }) : !1;
  }
  async function S(A, _) {
    return p ? new Promise((R) => {
      f?.interpolate(_?.interpolate === "linear" ? En : ko).scaleBy(sr(p, _?.duration, _?.ease, () => R(!0)), A);
    }) : !1;
  }
  function k(A) {
    f?.scaleExtent(A);
  }
  function D(A) {
    f?.translateExtent(A);
  }
  function L(A) {
    const _ = !Ye(A) || A < 0 ? 0 : A;
    f?.clickDistance(_);
  }
  return {
    update: x,
    destroy: w,
    setViewport: g,
    setViewportConstrained: b,
    getViewport: j,
    scaleTo: N,
    scaleBy: S,
    setScaleExtent: k,
    setTranslateExtent: D,
    syncViewport: v,
    setClickDistance: L
  };
}
var tn;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(tn || (tn = {}));
function Ix({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: i, affectsY: s }) {
  const a = e - t, c = n - o, u = [a > 0 ? 1 : a < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return a && i && (u[0] = u[0] * -1), c && s && (u[1] = u[1] * -1), u;
}
function Sa(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), i = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: i
  };
}
function dt(e, t) {
  return Math.max(0, t - e);
}
function ft(e, t) {
  return Math.max(0, e - t);
}
function xo(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function Ca(e, t) {
  return e ? !t : t;
}
function Ax(e, t, n, o, i, s, a, c) {
  let { affectsX: u, affectsY: l } = t;
  const { isHorizontal: d, isVertical: f } = t, p = d && f, { xSnapped: h, ySnapped: y } = n, { minWidth: m, maxWidth: x, minHeight: w, maxHeight: b } = o, { x: g, y: v, width: j, height: N, aspectRatio: S } = e;
  let k = Math.floor(d ? h - e.pointerX : 0), D = Math.floor(f ? y - e.pointerY : 0);
  const L = j + (u ? -k : k), A = N + (l ? -D : D), _ = -s[0] * j, R = -s[1] * N;
  let C = xo(L, m, x), I = xo(A, w, b);
  if (a) {
    let $ = 0, P = 0;
    u && k < 0 ? $ = dt(g + k + _, a[0][0]) : !u && k > 0 && ($ = ft(g + L + _, a[1][0])), l && D < 0 ? P = dt(v + D + R, a[0][1]) : !l && D > 0 && (P = ft(v + A + R, a[1][1])), C = Math.max(C, $), I = Math.max(I, P);
  }
  if (c) {
    let $ = 0, P = 0;
    u && k > 0 ? $ = ft(g + k, c[0][0]) : !u && k < 0 && ($ = dt(g + L, c[1][0])), l && D > 0 ? P = ft(v + D, c[0][1]) : !l && D < 0 && (P = dt(v + A, c[1][1])), C = Math.max(C, $), I = Math.max(I, P);
  }
  if (i) {
    if (d) {
      const $ = xo(L / S, w, b) * S;
      if (C = Math.max(C, $), a) {
        let P = 0;
        !u && !l || u && !l && p ? P = ft(v + R + L / S, a[1][1]) * S : P = dt(v + R + (u ? k : -k) / S, a[0][1]) * S, C = Math.max(C, P);
      }
      if (c) {
        let P = 0;
        !u && !l || u && !l && p ? P = dt(v + L / S, c[1][1]) * S : P = ft(v + (u ? k : -k) / S, c[0][1]) * S, C = Math.max(C, P);
      }
    }
    if (f) {
      const $ = xo(A * S, m, x) / S;
      if (I = Math.max(I, $), a) {
        let P = 0;
        !u && !l || l && !u && p ? P = ft(g + A * S + _, a[1][0]) / S : P = dt(g + (l ? D : -D) * S + _, a[0][0]) / S, I = Math.max(I, P);
      }
      if (c) {
        let P = 0;
        !u && !l || l && !u && p ? P = dt(g + A * S, c[1][0]) / S : P = ft(g + (l ? D : -D) * S, c[0][0]) / S, I = Math.max(I, P);
      }
    }
  }
  D = D + (D < 0 ? I : -I), k = k + (k < 0 ? C : -C), i && (p ? L > A * S ? D = (Ca(u, l) ? -k : k) / S : k = (Ca(u, l) ? -D : D) * S : d ? (D = k / S, l = u) : (k = D * S, u = l));
  const E = u ? g + k : g, T = l ? v + D : v;
  return {
    width: j + (u ? -k : k),
    height: N + (l ? -D : D),
    x: s[0] * k * (u ? -1 : 1) + E,
    y: s[1] * D * (l ? -1 : 1) + T
  };
}
const xu = { width: 0, height: 0, x: 0, y: 0 }, _x = {
  ...xu,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function Dx(e, t, n) {
  const o = t.position.x + e.position.x, i = t.position.y + e.position.y, s = e.measured.width ?? 0, a = e.measured.height ?? 0, c = n[0] * s, u = n[1] * a;
  return [
    [o - c, i - u],
    [o + s - c, i + a - u]
  ];
}
function Tx({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: i }) {
  const s = ze(e);
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
  function c({ controlPosition: l, boundaries: d, keepAspectRatio: f, resizeDirection: p, onResizeStart: h, onResize: y, onResizeEnd: m, shouldResize: x }) {
    let w = { ...xu }, b = { ..._x };
    a = {
      boundaries: d,
      resizeDirection: p,
      keepAspectRatio: f,
      controlDirection: Sa(l)
    };
    let g, v = null, j = [], N, S, k, D = !1;
    const L = kl().on("start", (A) => {
      const { nodeLookup: _, transform: R, snapGrid: C, snapToGrid: I, nodeOrigin: E, paneDomNode: T } = n();
      if (g = _.get(t), !g)
        return;
      v = T?.getBoundingClientRect() ?? null;
      const { xSnapped: $, ySnapped: P } = kn(A.sourceEvent, {
        transform: R,
        snapGrid: C,
        snapToGrid: I,
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
      }, N = void 0, S = Et(g.extent) ? g.extent : void 0, g.parentId && (g.extent === "parent" || g.expandParent) && (N = _.get(g.parentId)), N && g.extent === "parent" && (S = [
        [0, 0],
        [N.measured.width, N.measured.height]
      ]), j = [], k = void 0;
      for (const [F, H] of _)
        if (H.parentId === t && (j.push({
          id: F,
          position: { ...H.position },
          extent: H.extent
        }), H.extent === "parent" || H.expandParent)) {
          const O = Dx(H, g, H.origin ?? E);
          k ? k = [
            [Math.min(O[0][0], k[0][0]), Math.min(O[0][1], k[0][1])],
            [Math.max(O[1][0], k[1][0]), Math.max(O[1][1], k[1][1])]
          ] : k = O;
        }
      h?.(A, { ...w });
    }).on("drag", (A) => {
      const { transform: _, snapGrid: R, snapToGrid: C, nodeOrigin: I } = n(), E = kn(A.sourceEvent, {
        transform: _,
        snapGrid: R,
        snapToGrid: C,
        containerBounds: v
      }), T = [];
      if (!g)
        return;
      const { x: $, y: P, width: F, height: H } = w, O = {}, U = g.origin ?? I, { width: q, height: ne, x: ce, y: Z } = Ax(b, a.controlDirection, E, a.boundaries, a.keepAspectRatio, U, S, k), z = q !== F, X = ne !== H, le = ce !== $ && z, ae = Z !== P && X;
      if (!le && !ae && !z && !X)
        return;
      if ((le || ae || U[0] === 1 || U[1] === 1) && (O.x = le ? ce : w.x, O.y = ae ? Z : w.y, w.x = O.x, w.y = O.y, j.length > 0)) {
        const V = ce - $, Q = Z - P;
        for (const he of j)
          he.position = {
            x: he.position.x - V + U[0] * (q - F),
            y: he.position.y - Q + U[1] * (ne - H)
          }, T.push(he);
      }
      if ((z || X) && (O.width = z && (!a.resizeDirection || a.resizeDirection === "horizontal") ? q : w.width, O.height = X && (!a.resizeDirection || a.resizeDirection === "vertical") ? ne : w.height, w.width = O.width, w.height = O.height), N && g.expandParent) {
        const V = U[0] * (O.width ?? 0);
        O.x && O.x < V && (w.x = V, b.x = b.x - (O.x - V));
        const Q = U[1] * (O.height ?? 0);
        O.y && O.y < Q && (w.y = Q, b.y = b.y - (O.y - Q));
      }
      const J = Ix({
        width: w.width,
        prevWidth: F,
        height: w.height,
        prevHeight: H,
        affectsX: a.controlDirection.affectsX,
        affectsY: a.controlDirection.affectsY
      }), te = { ...w, direction: J };
      x?.(A, te) !== !1 && (D = !0, y?.(A, te), o(O, T));
    }).on("end", (A) => {
      D && (m?.(A, { ...w }), i?.({ ...w }), D = !1);
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
var ar = { exports: {} }, cr = {}, lr = { exports: {} }, ur = {};
var Ea;
function $x() {
  if (Ea) return ur;
  Ea = 1;
  var e = Qe;
  function t(f, p) {
    return f === p && (f !== 0 || 1 / f === 1 / p) || f !== f && p !== p;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, i = e.useEffect, s = e.useLayoutEffect, a = e.useDebugValue;
  function c(f, p) {
    var h = p(), y = o({ inst: { value: h, getSnapshot: p } }), m = y[0].inst, x = y[1];
    return s(
      function() {
        m.value = h, m.getSnapshot = p, u(m) && x({ inst: m });
      },
      [f, h, p]
    ), i(
      function() {
        return u(m) && x({ inst: m }), f(function() {
          u(m) && x({ inst: m });
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
var ka;
function Px() {
  return ka || (ka = 1, lr.exports = $x()), lr.exports;
}
var Ia;
function Mx() {
  if (Ia) return cr;
  Ia = 1;
  var e = Qe, t = Px();
  function n(l, d) {
    return l === d && (l !== 0 || 1 / l === 1 / d) || l !== l && d !== d;
  }
  var o = typeof Object.is == "function" ? Object.is : n, i = t.useSyncExternalStore, s = e.useRef, a = e.useEffect, c = e.useMemo, u = e.useDebugValue;
  return cr.useSyncExternalStoreWithSelector = function(l, d, f, p, h) {
    var y = s(null);
    if (y.current === null) {
      var m = { hasValue: !1, value: null };
      y.current = m;
    } else m = y.current;
    y = c(
      function() {
        function w(N) {
          if (!b) {
            if (b = !0, g = N, N = p(N), h !== void 0 && m.hasValue) {
              var S = m.value;
              if (h(S, N))
                return v = S;
            }
            return v = N;
          }
          if (S = v, o(g, N)) return S;
          var k = p(N);
          return h !== void 0 && h(S, k) ? (g = N, S) : (g = N, v = k);
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
    var x = i(l, y[0], y[1]);
    return a(
      function() {
        m.hasValue = !0, m.value = x;
      },
      [x]
    ), u(x), x;
  }, cr;
}
var Aa;
function Rx() {
  return Aa || (Aa = 1, ar.exports = Mx()), ar.exports;
}
var Lx = Rx();
const zx = /* @__PURE__ */ rf(Lx), Vx = {}, _a = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (d, f) => {
    const p = typeof d == "function" ? d(t) : d;
    if (!Object.is(p, t)) {
      const h = t;
      t = f ?? (typeof p != "object" || p === null) ? p : Object.assign({}, t, p), n.forEach((y) => y(t, h));
    }
  }, i = () => t, u = { setState: o, getState: i, getInitialState: () => l, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (Vx ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(o, i, u);
  return u;
}, Ox = (e) => e ? _a(e) : _a, { useDebugValue: Hx } = Qe, { useSyncExternalStoreWithSelector: Wx } = zx, Fx = (e) => e;
function wu(e, t = Fx, n) {
  const o = Wx(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return Hx(o), o;
}
const Da = (e, t) => {
  const n = Ox(e), o = (i, s = t) => wu(n, i, s);
  return Object.assign(o, n), o;
}, Bx = (e, t) => e ? Da(e, t) : Da;
function me(e, t) {
  if (Object.is(e, t))
    return !0;
  if (typeof e != "object" || e === null || typeof t != "object" || t === null)
    return !1;
  if (e instanceof Map && t instanceof Map) {
    if (e.size !== t.size) return !1;
    for (const [o, i] of e)
      if (!Object.is(i, t.get(o)))
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
var dr = { exports: {} }, De = {};
var Ta;
function Kx() {
  if (Ta) return De;
  Ta = 1;
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
  }, i = /* @__PURE__ */ Symbol.for("react.portal");
  function s(u, l, d) {
    var f = 3 < arguments.length && arguments[3] !== void 0 ? arguments[3] : null;
    return {
      $$typeof: i,
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
  return De.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, De.createPortal = function(u, l) {
    var d = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!l || l.nodeType !== 1 && l.nodeType !== 9 && l.nodeType !== 11)
      throw Error(t(299));
    return s(u, l, null, d);
  }, De.flushSync = function(u) {
    var l = a.T, d = o.p;
    try {
      if (a.T = null, o.p = 2, u) return u();
    } finally {
      a.T = l, o.p = d, o.d.f();
    }
  }, De.preconnect = function(u, l) {
    typeof u == "string" && (l ? (l = l.crossOrigin, l = typeof l == "string" ? l === "use-credentials" ? l : "" : void 0) : l = null, o.d.C(u, l));
  }, De.prefetchDNS = function(u) {
    typeof u == "string" && o.d.D(u);
  }, De.preinit = function(u, l) {
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
  }, De.preinitModule = function(u, l) {
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
  }, De.preload = function(u, l) {
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
  }, De.preloadModule = function(u, l) {
    if (typeof u == "string")
      if (l) {
        var d = c(l.as, l.crossOrigin);
        o.d.m(u, {
          as: typeof l.as == "string" && l.as !== "script" ? l.as : void 0,
          crossOrigin: d,
          integrity: typeof l.integrity == "string" ? l.integrity : void 0
        });
      } else o.d.m(u);
  }, De.requestFormReset = function(u) {
    o.d.r(u);
  }, De.unstable_batchedUpdates = function(u, l) {
    return u(l);
  }, De.useFormState = function(u, l, d) {
    return a.H.useFormState(u, l, d);
  }, De.useFormStatus = function() {
    return a.H.useHostTransitionStatus();
  }, De.version = "19.2.7", De;
}
var $a;
function Xx() {
  if ($a) return dr.exports;
  $a = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), dr.exports = Kx(), dr.exports;
}
var Yx = Xx();
const pi = Or(null), qx = pi.Provider, vu = We.error001("react");
function pe(e, t) {
  const n = zn(pi);
  if (n === null)
    throw new Error(vu);
  return wu(n, e, t);
}
function xe() {
  const e = zn(pi);
  if (e === null)
    throw new Error(vu);
  return fe(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const Pa = { display: "none" }, Ux = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, bu = "react-flow__node-desc", Nu = "react-flow__edge-desc", Zx = "react-flow__aria-live", Gx = (e) => e.ariaLiveMessage, Jx = (e) => e.ariaLabelConfig;
function Qx({ rfId: e }) {
  const t = pe(Gx);
  return r.jsx("div", { id: `${Zx}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: Ux, children: t });
}
function ew({ rfId: e, disableKeyboardA11y: t }) {
  const n = pe(Jx);
  return r.jsxs(r.Fragment, { children: [r.jsx("div", { id: `${bu}-${e}`, style: Pa, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), r.jsx("div", { id: `${Nu}-${e}`, style: Pa, children: n["edge.a11yDescription.default"] }), !t && r.jsx(Qx, { rfId: e })] });
}
const hi = Tc(({ position: e = "top-left", children: t, className: n, style: o, ...i }, s) => {
  const a = `${e}`.split("-");
  return r.jsx("div", { className: Ce(["react-flow__panel", n, ...a]), style: o, ref: s, ...i, children: t });
});
hi.displayName = "Panel";
function tw({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : r.jsx(hi, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: r.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const nw = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, wo = (e) => e.id;
function ow(e, t) {
  return me(e.selectedNodes.map(wo), t.selectedNodes.map(wo)) && me(e.selectedEdges.map(wo), t.selectedEdges.map(wo));
}
function iw({ onSelectionChange: e }) {
  const t = xe(), { selectedNodes: n, selectedEdges: o } = pe(nw, ow);
  return G(() => {
    const i = { nodes: n, edges: o };
    e?.(i), t.getState().onSelectionChangeHandlers.forEach((s) => s(i));
  }, [n, o, e]), null;
}
const rw = (e) => !!e.onSelectionChangeHandlers;
function sw({ onSelectionChange: e }) {
  const t = pe(rw);
  return e || t ? r.jsx(iw, { onSelectionChange: e }) : null;
}
const ju = [0, 0], aw = { x: 0, y: 0, zoom: 1 }, cw = [
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
], Ma = [...cw, "rfId"], lw = (e) => ({
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
  translateExtent: $n,
  nodeOrigin: ju,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function uw(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: i, setTranslateExtent: s, setNodeExtent: a, reset: c, setDefaultNodesAndEdges: u } = pe(lw, me), l = xe();
  G(() => (u(e.defaultNodes, e.defaultEdges), () => {
    d.current = Ra, c();
  }), []);
  const d = re(Ra);
  return G(
    () => {
      for (const f of Ma) {
        const p = e[f], h = d.current[f];
        p !== h && (typeof e[f] > "u" || (f === "nodes" ? t(p) : f === "edges" ? n(p) : f === "minZoom" ? o(p) : f === "maxZoom" ? i(p) : f === "translateExtent" ? s(p) : f === "nodeExtent" ? a(p) : f === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: Km(p) }) : f === "fitView" ? l.setState({ fitViewQueued: p }) : f === "fitViewOptions" ? l.setState({ fitViewOptions: p }) : l.setState({ [f]: p })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    Ma.map((f) => e[f])
  ), null;
}
function La() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function dw(e) {
  const [t, n] = B(e === "system" ? null : e);
  return G(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = La(), i = () => n(o?.matches ? "dark" : "light");
    return i(), o?.addEventListener("change", i), () => {
      o?.removeEventListener("change", i);
    };
  }, [e]), t !== null ? t : La()?.matches ? "dark" : "light";
}
const za = typeof document < "u" ? document : null;
function Ln(e = null, t = { target: za, actInsideInputWithModifier: !0 }) {
  const [n, o] = B(!1), i = re(!1), s = re(/* @__PURE__ */ new Set([])), [a, c] = fe(() => {
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
    const u = t?.target ?? za, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (h) => {
        if (i.current = h.ctrlKey || h.metaKey || h.shiftKey || h.altKey, (!i.current || i.current && !l) && tu(h))
          return !1;
        const m = Oa(h.code, c);
        if (s.current.add(h[m]), Va(a, s.current, !1)) {
          const x = h.composedPath?.()?.[0] || h.target, w = x?.nodeName === "BUTTON" || x?.nodeName === "A";
          t.preventDefault !== !1 && (i.current || !w) && h.preventDefault(), o(!0);
        }
      }, f = (h) => {
        const y = Oa(h.code, c);
        Va(a, s.current, !0) ? (o(!1), s.current.clear()) : s.current.delete(h[y]), h.key === "Meta" && s.current.clear(), i.current = !1;
      }, p = () => {
        s.current.clear(), o(!1);
      };
      return u?.addEventListener("keydown", d), u?.addEventListener("keyup", f), window.addEventListener("blur", p), window.addEventListener("contextmenu", p), () => {
        u?.removeEventListener("keydown", d), u?.removeEventListener("keyup", f), window.removeEventListener("blur", p), window.removeEventListener("contextmenu", p);
      };
    }
  }, [e, o]), n;
}
function Va(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((i) => t.has(i)));
}
function Oa(e, t) {
  return t.includes(e) ? "code" : "key";
}
const fw = () => {
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
      const { transform: [o, i, s], panZoom: a } = e.getState();
      return a ? (await a.setViewport({
        x: t.x ?? o,
        y: t.y ?? i,
        zoom: t.zoom ?? s
      }, n), !0) : !1;
    },
    getViewport: () => {
      const [t, n, o] = e.getState().transform;
      return { x: t, y: n, zoom: o };
    },
    setCenter: async (t, n, o) => e.getState().setCenter(t, n, o),
    fitBounds: async (t, n) => {
      const { width: o, height: i, minZoom: s, maxZoom: a, panZoom: c } = e.getState(), u = rs(t, o, i, s, a, n?.padding ?? 0.1);
      return c ? (await c.setViewport(u, {
        duration: n?.duration,
        ease: n?.ease,
        interpolate: n?.interpolate
      }), !0) : !1;
    },
    screenToFlowPosition: (t, n = {}) => {
      const { transform: o, snapGrid: i, snapToGrid: s, domNode: a } = e.getState();
      if (!a)
        return t;
      const { x: c, y: u } = a.getBoundingClientRect(), l = {
        x: t.x - c,
        y: t.y - u
      }, d = n.snapGrid ?? i, f = n.snapToGrid ?? s;
      return sn(l, o, f, d);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: i, y: s } = o.getBoundingClientRect(), a = en(t, n);
      return {
        x: a.x + i,
        y: a.y + s
      };
    }
  }), []);
};
function Su(e, t) {
  const n = [], o = /* @__PURE__ */ new Map(), i = [];
  for (const s of e)
    if (s.type === "add") {
      i.push(s);
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
      pw(u, c);
    n.push(c);
  }
  return i.length && i.forEach((s) => {
    s.index !== void 0 ? n.splice(s.index, 0, { ...s.item }) : n.push({ ...s.item });
  }), n;
}
function pw(e, t) {
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
function Cu(e, t) {
  return Su(e, t);
}
function Eu(e, t) {
  return Su(e, t);
}
function xt(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function Wt(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [i, s] of e) {
    const a = t.has(i);
    !(s.selected === void 0 && !a) && s.selected !== a && (n && (s.selected = a), o.push(xt(s.id, a)));
  }
  return o;
}
function Ha({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((i) => [i.id, i]));
  for (const [i, s] of e.entries()) {
    const a = t.get(s.id), c = a?.internals?.userNode ?? a;
    c !== void 0 && c !== s && n.push({ id: s.id, item: s, type: "replace" }), c === void 0 && n.push({ item: s, type: "add", index: i });
  }
  for (const [i] of t)
    o.get(i) === void 0 && n.push({ id: i, type: "remove" });
  return n;
}
function Wa(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const ku = Gl();
function Iu(e, t, n = {}) {
  return Zm(e, t, {
    ...n,
    onError: n.onError ?? ku
  });
}
function hw(e, t, n, o = { shouldReplaceId: !0 }) {
  return Gm(e, t, n, {
    ...o,
    onError: o.onError ?? ku
  });
}
const Fa = (e) => Rm(e), gw = (e) => Yl(e);
function Au(e) {
  return Tc(e);
}
const yw = typeof window < "u" ? Od : G;
function Ba(e) {
  const [t, n] = B(BigInt(0)), [o] = B(() => mw(() => n((i) => i + BigInt(1))));
  return yw(() => {
    const i = o.get();
    i.length && (e(i), o.reset());
  }, [t]), o;
}
function mw(e) {
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
const _u = Or(null);
function xw({ children: e }) {
  const t = xe(), n = ie((c) => {
    const { nodes: u = [], setNodes: l, hasDefaultNodes: d, onNodesChange: f, nodeLookup: p, fitViewQueued: h, onNodesChangeMiddlewareMap: y } = t.getState();
    let m = u;
    for (const w of c)
      m = typeof w == "function" ? w(m) : w;
    let x = Ha({
      items: m,
      lookup: p
    });
    for (const w of y.values())
      x = w(x);
    d && l(m), x.length > 0 ? f?.(x) : h && window.requestAnimationFrame(() => {
      const { fitViewQueued: w, nodes: b, setNodes: g } = t.getState();
      w && g(b);
    });
  }, []), o = Ba(n), i = ie((c) => {
    const { edges: u = [], setEdges: l, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: p } = t.getState();
    let h = u;
    for (const y of c)
      h = typeof y == "function" ? y(h) : y;
    d ? l(h) : f && f(Ha({
      items: h,
      lookup: p
    }));
  }, []), s = Ba(i), a = fe(() => ({ nodeQueue: o, edgeQueue: s }), []);
  return r.jsx(_u.Provider, { value: a, children: e });
}
function ww() {
  const e = zn(_u);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const vw = (e) => !!e.panZoom;
function fs() {
  const e = fw(), t = xe(), n = ww(), o = pe(vw), i = fe(() => {
    const s = (f) => t.getState().nodeLookup.get(f), a = (f) => {
      n.nodeQueue.push(f);
    }, c = (f) => {
      n.edgeQueue.push(f);
    }, u = (f) => {
      const { nodeLookup: p, nodeOrigin: h } = t.getState(), y = Fa(f) ? f : p.get(f.id), m = y.parentId ? Ql(y.position, y.measured, y.parentId, p, h) : y.position, x = {
        ...y,
        position: m,
        width: y.measured?.width ?? y.width,
        height: y.measured?.height ?? y.height
      };
      return Qt(x);
    }, l = (f, p, h = { replace: !1 }) => {
      a((y) => y.map((m) => {
        if (m.id === f) {
          const x = typeof p == "function" ? p(m) : p;
          return h.replace && Fa(x) ? x : { ...m, ...x };
        }
        return m;
      }));
    }, d = (f, p, h = { replace: !1 }) => {
      c((y) => y.map((m) => {
        if (m.id === f) {
          const x = typeof p == "function" ? p(m) : p;
          return h.replace && gw(x) ? x : { ...m, ...x };
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
        const { nodes: f = [], edges: p = [], transform: h } = t.getState(), [y, m, x] = h;
        return {
          nodes: f.map((w) => ({ ...w })),
          edges: p.map((w) => ({ ...w })),
          viewport: {
            x: y,
            y: m,
            zoom: x
          }
        };
      },
      deleteElements: async ({ nodes: f = [], edges: p = [] }) => {
        const { nodes: h, edges: y, onNodesDelete: m, onEdgesDelete: x, triggerNodeChanges: w, triggerEdgeChanges: b, onDelete: g, onBeforeDelete: v } = t.getState(), { nodes: j, edges: N } = await Hm({
          nodesToRemove: f,
          edgesToRemove: p,
          nodes: h,
          edges: y,
          onBeforeDelete: v
        }), S = N.length > 0, k = j.length > 0;
        if (S) {
          const D = N.map(Wa);
          x?.(N), b(D);
        }
        if (k) {
          const D = j.map(Wa);
          m?.(j), w(D);
        }
        return (k || S) && g?.({ nodes: j, edges: N }), { deletedNodes: j, deletedEdges: N };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, p = !0, h) => {
        const y = pa(f), m = y ? f : u(f), x = h !== void 0;
        return m ? (h || t.getState().nodes).filter((w) => {
          const b = t.getState().nodeLookup.get(w.id);
          if (b && !y && (w.id === f.id || !b.internals.positionAbsolute))
            return !1;
          const g = Qt(x ? w : b), v = Mn(g, m);
          return p && v > 0 || v >= g.width * g.height || v >= m.width * m.height;
        }) : [];
      },
      isNodeIntersecting: (f, p, h = !0) => {
        const m = pa(f) ? f : u(f);
        if (!m)
          return !1;
        const x = Mn(m, p);
        return h && x > 0 || x >= p.width * p.height || x >= m.width * m.height;
      },
      updateNode: l,
      updateNodeData: (f, p, h = { replace: !1 }) => {
        l(f, (y) => {
          const m = typeof p == "function" ? p(y) : p;
          return h.replace ? { ...y, data: m } : { ...y, data: { ...y.data, ...m } };
        }, h);
      },
      updateEdge: d,
      updateEdgeData: (f, p, h = { replace: !1 }) => {
        d(f, (y) => {
          const m = typeof p == "function" ? p(y) : p;
          return h.replace ? { ...y, data: m } : { ...y, data: { ...y.data, ...m } };
        }, h);
      },
      getNodesBounds: (f) => {
        const { nodeLookup: p, nodeOrigin: h } = t.getState();
        return Lm(f, { nodeLookup: p, nodeOrigin: h });
      },
      getHandleConnections: ({ type: f, id: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}-${f}${p ? `-${p}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}${f ? p ? `-${f}-${p}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const p = t.getState().fitViewResolver ?? Bm();
        return t.setState({ fitViewQueued: !0, fitViewOptions: f, fitViewResolver: p }), n.nodeQueue.push((h) => [...h]), p.promise;
      }
    };
  }, []);
  return fe(() => ({
    ...i,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const Ka = (e) => e.selected, bw = typeof window < "u" ? window : void 0;
function Nw({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = xe(), { deleteElements: o } = fs(), i = Ln(e, { actInsideInputWithModifier: !1 }), s = Ln(t, { target: bw });
  G(() => {
    if (i) {
      const { edges: a, nodes: c } = n.getState();
      o({ nodes: c.filter(Ka), edges: a.filter(Ka) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [i]), G(() => {
    n.setState({ multiSelectionActive: s });
  }, [s]);
}
function jw(e) {
  const t = xe();
  G(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = ss(e.current);
      (o.height === 0 || o.width === 0) && t.getState().onError?.("004", We.error004()), t.setState({ width: o.width || 500, height: o.height || 500 });
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
const gi = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, Sw = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function Cw({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: i = 0.5, panOnScrollMode: s = Nt.Free, zoomOnDoubleClick: a = !0, panOnDrag: c = !0, defaultViewport: u, translateExtent: l, minZoom: d, maxZoom: f, zoomActivationKeyCode: p, preventScrolling: h = !0, children: y, noWheelClassName: m, noPanClassName: x, onViewportChange: w, isControlledViewport: b, paneClickDistance: g, selectionOnDrag: v }) {
  const j = xe(), N = re(null), { userSelectionActive: S, lib: k, connectionInProgress: D } = pe(Sw, me), L = Ln(p), A = re();
  jw(N);
  const _ = ie((R) => {
    w?.({ x: R[0], y: R[1], zoom: R[2] }), b || j.setState({ transform: R });
  }, [w, b]);
  return G(() => {
    if (N.current) {
      A.current = kx({
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
      const { x: R, y: C, zoom: I } = A.current.getViewport();
      return j.setState({
        panZoom: A.current,
        transform: [R, C, I],
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
      panOnScroll: o,
      panOnScrollSpeed: i,
      panOnScrollMode: s,
      zoomOnDoubleClick: a,
      panOnDrag: c,
      zoomActivationKeyPressed: L,
      preventScrolling: h,
      noPanClassName: x,
      userSelectionActive: S,
      noWheelClassName: m,
      lib: k,
      onTransformChange: _,
      connectionInProgress: D,
      selectionOnDrag: v,
      paneClickDistance: g
    });
  }, [
    e,
    t,
    n,
    o,
    i,
    s,
    a,
    c,
    L,
    h,
    x,
    S,
    m,
    k,
    _,
    D,
    v,
    g
  ]), r.jsx("div", { className: "react-flow__renderer", ref: N, style: gi, children: y });
}
const Ew = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function kw() {
  const { userSelectionActive: e, userSelectionRect: t } = pe(Ew, me);
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
function Aw({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Pn.Full, panOnDrag: o, autoPanOnSelection: i, paneClickDistance: s, selectionOnDrag: a, onSelectionStart: c, onSelectionEnd: u, onPaneClick: l, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: p, onPaneMouseMove: h, onPaneMouseLeave: y, children: m }) {
  const x = re(0), w = xe(), { userSelectionActive: b, elementsSelectable: g, dragging: v, connectionInProgress: j, panBy: N, autoPanSpeed: S } = pe(Iw, me), k = g && (e || b), D = re(null), L = re(), A = re(/* @__PURE__ */ new Set()), _ = re(/* @__PURE__ */ new Set()), R = re(!1), C = re({ x: 0, y: 0 }), I = re(!1), E = (z) => {
    if (R.current || j) {
      R.current = !1;
      return;
    }
    l?.(z), w.getState().resetSelectedElements(), w.setState({ nodesSelectionActive: !1 });
  }, T = (z) => {
    if (Array.isArray(o) && o?.includes(2)) {
      z.preventDefault();
      return;
    }
    d?.(z);
  }, $ = f ? (z) => f(z) : void 0, P = (z) => {
    R.current && (z.stopPropagation(), R.current = !1);
  }, F = (z) => {
    const { domNode: X, transform: le } = w.getState();
    if (L.current = X?.getBoundingClientRect(), !L.current)
      return;
    const ae = z.target === D.current;
    if (!ae && !!z.target.closest(".nokey") || !e || !(a && ae || t) || z.button !== 0 || !z.isPrimary)
      return;
    z.target?.setPointerCapture?.(z.pointerId), R.current = !1;
    const { x: de, y: V } = qe(z.nativeEvent, L.current), Q = sn({ x: de, y: V }, le);
    w.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: Q.x,
        startY: Q.y,
        x: de,
        y: V
      }
    }), ae || (z.stopPropagation(), z.preventDefault());
  };
  function H(z, X) {
    const { userSelectionRect: le } = w.getState();
    if (!le)
      return;
    const { transform: ae, nodeLookup: J, edgeLookup: te, connectionLookup: de, triggerNodeChanges: V, triggerEdgeChanges: Q, defaultEdgeOptions: he } = w.getState(), we = { x: le.startX, y: le.startY }, { x: Ie, y: Ae } = en(we, ae), Re = {
      startX: we.x,
      startY: we.y,
      x: z < Ie ? z : Ie,
      y: X < Ae ? X : Ae,
      width: Math.abs(z - Ie),
      height: Math.abs(X - Ae)
    }, ot = A.current, Fe = _.current;
    A.current = new Set(os(J, Re, ae, n === Pn.Partial, !0).map((Te) => Te.id)), _.current = /* @__PURE__ */ new Set();
    const Be = he?.selectable ?? !0;
    for (const Te of A.current) {
      const Le = de.get(Te);
      if (Le)
        for (const { edgeId: M } of Le.values()) {
          const W = te.get(M);
          W && (W.selectable ?? Be) && _.current.add(M);
        }
    }
    if (!ha(ot, A.current)) {
      const Te = Wt(J, A.current, !0);
      V(Te);
    }
    if (!ha(Fe, _.current)) {
      const Te = Wt(te, _.current);
      Q(Te);
    }
    w.setState({
      userSelectionRect: Re,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function O() {
    if (!i || !L.current)
      return;
    const [z, X] = is(C.current, L.current, S);
    N({ x: z, y: X }).then((le) => {
      if (!R.current || !le) {
        x.current = requestAnimationFrame(O);
        return;
      }
      const { x: ae, y: J } = C.current;
      H(ae, J), x.current = requestAnimationFrame(O);
    });
  }
  const U = () => {
    cancelAnimationFrame(x.current), x.current = 0, I.current = !1;
  };
  G(() => () => U(), []);
  const q = (z) => {
    const { userSelectionRect: X, transform: le, resetSelectedElements: ae } = w.getState();
    if (!L.current || !X)
      return;
    const { x: J, y: te } = qe(z.nativeEvent, L.current);
    C.current = { x: J, y: te };
    const de = en({ x: X.startX, y: X.startY }, le);
    if (!R.current) {
      const V = t ? 0 : s;
      if (Math.hypot(J - de.x, te - de.y) <= V)
        return;
      ae(), c?.(z);
    }
    R.current = !0, I.current || (O(), I.current = !0), H(J, te);
  }, ne = (z) => {
    z.button === 0 && (z.target?.releasePointerCapture?.(z.pointerId), !b && z.target === D.current && w.getState().userSelectionRect && E?.(z), w.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), R.current && (u?.(z), w.setState({
      nodesSelectionActive: A.current.size > 0
    })), U());
  }, ce = (z) => {
    z.target?.releasePointerCapture?.(z.pointerId), U();
  }, Z = o === !0 || Array.isArray(o) && o.includes(0);
  return r.jsxs("div", { className: Ce(["react-flow__pane", { draggable: Z, dragging: v, selection: e }]), onClick: k ? void 0 : fr(E, D), onContextMenu: fr(T, D), onWheel: fr($, D), onPointerEnter: k ? void 0 : p, onPointerMove: k ? q : h, onPointerUp: k ? ne : void 0, onPointerCancel: k ? ce : void 0, onPointerDownCapture: k ? F : void 0, onClickCapture: k ? P : void 0, onPointerLeave: y, ref: D, style: gi, children: [m, r.jsx(kw, {})] });
}
function Tr({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: i, unselectNodesAndEdges: s, multiSelectionActive: a, nodeLookup: c, onError: u } = t.getState(), l = c.get(e);
  if (!l) {
    u?.("012", We.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && a) && (s({ nodes: [l], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : i([e]);
}
function Du({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: i, isSelectable: s, nodeClickDistance: a }) {
  const c = xe(), [u, l] = B(!1), d = re();
  return G(() => {
    d.current = px({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (f) => {
        Tr({
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
        handleSelector: o,
        domNode: e.current,
        isSelectable: s,
        nodeId: i,
        nodeClickDistance: a
      }), () => {
        d.current?.destroy();
      };
  }, [n, o, t, s, e, i, a]), u;
}
const _w = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function Tu() {
  const e = xe();
  return ie((n) => {
    const { nodeExtent: o, snapToGrid: i, snapGrid: s, nodesDraggable: a, onError: c, updateNodePositions: u, nodeLookup: l, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), p = _w(a), h = i ? s[0] : 5, y = i ? s[1] : 5, m = n.direction.x * h * n.factor, x = n.direction.y * y * n.factor;
    for (const [, w] of l) {
      if (!p(w))
        continue;
      let b = {
        x: w.internals.positionAbsolute.x + m,
        y: w.internals.positionAbsolute.y + x
      };
      i && (b = Yn(b, s));
      const { position: g, positionAbsolute: v } = ql({
        nodeId: w.id,
        nextPosition: b,
        nodeLookup: l,
        nodeExtent: o,
        nodeOrigin: d,
        onError: c
      });
      w.position = g, w.internals.positionAbsolute = v, f.set(w.id, w);
    }
    u(f);
  }, []);
}
const ps = Or(null), Dw = ps.Provider;
ps.Consumer;
const $u = () => zn(ps), Tw = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), $w = (e, t, n) => (o) => {
  const { connectionClickStartHandle: i, connectionMode: s, connection: a } = o, { fromHandle: c, toHandle: u, isValid: l } = a, d = u?.nodeId === e && u?.id === t && u?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: d,
    clickConnecting: i?.nodeId === e && i?.id === t && i?.type === n,
    isPossibleEndHandle: s === Gt.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!i,
    valid: d && l
  };
};
function Pw({ type: e = "source", position: t = oe.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: i = !0, isConnectableEnd: s = !0, id: a, onConnect: c, children: u, className: l, onMouseDown: d, onTouchStart: f, ...p }, h) {
  const y = a || null, m = e === "target", x = xe(), w = $u(), { connectOnClick: b, noPanClassName: g, rfId: v } = pe(Tw, me), { connectingFrom: j, connectingTo: N, clickConnecting: S, isPossibleEndHandle: k, connectionInProcess: D, clickConnectionInProcess: L, valid: A } = pe($w(w, y, e), me);
  w || x.getState().onError?.("010", We.error010());
  const _ = (I) => {
    const { defaultEdgeOptions: E, onConnect: T, hasDefaultEdges: $ } = x.getState(), P = {
      ...E,
      ...I
    };
    if ($) {
      const { edges: F, setEdges: H, onError: O } = x.getState();
      H(Iu(P, F, { onError: O }));
    }
    T?.(P), c?.(P);
  }, R = (I) => {
    if (!w)
      return;
    const E = nu(I.nativeEvent);
    if (i && (E && I.button === 0 || !E)) {
      const T = x.getState();
      Dr.onPointerDown(I.nativeEvent, {
        handleDomNode: I.currentTarget,
        autoPanOnConnect: T.autoPanOnConnect,
        connectionMode: T.connectionMode,
        connectionRadius: T.connectionRadius,
        domNode: T.domNode,
        nodeLookup: T.nodeLookup,
        lib: T.lib,
        isTarget: m,
        handleId: y,
        nodeId: w,
        flowId: T.rfId,
        panBy: T.panBy,
        cancelConnection: T.cancelConnection,
        onConnectStart: T.onConnectStart,
        onConnectEnd: (...$) => x.getState().onConnectEnd?.(...$),
        updateConnection: T.updateConnection,
        onConnect: _,
        isValidConnection: n || ((...$) => x.getState().isValidConnection?.(...$) ?? !0),
        getTransform: () => x.getState().transform,
        getFromHandle: () => x.getState().connection.fromHandle,
        autoPanSpeed: T.autoPanSpeed,
        dragThreshold: T.connectionDragThreshold
      });
    }
    E ? d?.(I) : f?.(I);
  }, C = (I) => {
    const { onClickConnectStart: E, onClickConnectEnd: T, connectionClickStartHandle: $, connectionMode: P, isValidConnection: F, lib: H, rfId: O, nodeLookup: U, connection: q } = x.getState();
    if (!w || !$ && !i)
      return;
    if (!$) {
      E?.(I.nativeEvent, { nodeId: w, handleId: y, handleType: e }), x.setState({ connectionClickStartHandle: { nodeId: w, type: e, id: y } });
      return;
    }
    const ne = eu(I.target), ce = n || F, { connection: Z, isValid: z } = Dr.isValid(I.nativeEvent, {
      handle: {
        nodeId: w,
        id: y,
        type: e
      },
      connectionMode: P,
      fromNodeId: $.nodeId,
      fromHandleId: $.id || null,
      fromType: $.type,
      isValidConnection: ce,
      flowId: O,
      doc: ne,
      lib: H,
      nodeLookup: U
    });
    z && Z && _(Z);
    const X = structuredClone(q);
    delete X.inProgress, X.toPosition = X.toHandle ? X.toHandle.position : null, T?.(I, X), x.setState({ connectionClickStartHandle: null });
  };
  return r.jsx("div", { "data-handleid": y, "data-nodeid": w, "data-handlepos": t, "data-id": `${v}-${w}-${y}-${e}`, className: Ce([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    g,
    l,
    {
      source: !m,
      target: m,
      connectable: o,
      connectablestart: i,
      connectableend: s,
      clickconnecting: S,
      connectingfrom: j,
      connectingto: N,
      valid: A,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!D || k) && (D || L ? s : i)
    }
  ]), onMouseDown: R, onTouchStart: R, onClick: b ? C : void 0, ref: h, ...p, children: u });
}
const nn = Ne(Au(Pw));
function Mw({ data: e, isConnectable: t, sourcePosition: n = oe.Bottom }) {
  return r.jsxs(r.Fragment, { children: [e?.label, r.jsx(nn, { type: "source", position: n, isConnectable: t })] });
}
function Rw({ data: e, isConnectable: t, targetPosition: n = oe.Top, sourcePosition: o = oe.Bottom }) {
  return r.jsxs(r.Fragment, { children: [r.jsx(nn, { type: "target", position: n, isConnectable: t }), e?.label, r.jsx(nn, { type: "source", position: o, isConnectable: t })] });
}
function Lw() {
  return null;
}
function zw({ data: e, isConnectable: t, targetPosition: n = oe.Top }) {
  return r.jsxs(r.Fragment, { children: [r.jsx(nn, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const Qo = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, Xa = {
  input: Mw,
  default: Rw,
  output: zw,
  group: Lw
};
function Vw(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const Ow = (e) => {
  const { width: t, height: n, x: o, y: i } = Xn(e.nodeLookup, {
    filter: (s) => !!s.selected
  });
  return {
    width: Ye(t) ? t : null,
    height: Ye(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${i}px)`
  };
};
function Hw({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = xe(), { width: i, height: s, transformString: a, userSelectionActive: c } = pe(Ow, me), u = Tu(), l = re(null);
  G(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !c && i !== null && s !== null;
  if (Du({
    nodeRef: l,
    disabled: !d
  }), !d)
    return null;
  const f = e ? (h) => {
    const y = o.getState().nodes.filter((m) => m.selected);
    e(h, y);
  } : void 0, p = (h) => {
    Object.prototype.hasOwnProperty.call(Qo, h.key) && (h.preventDefault(), u({
      direction: Qo[h.key],
      factor: h.shiftKey ? 4 : 1
    }));
  };
  return r.jsx("div", { className: Ce(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: a
  }, children: r.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : p, style: {
    width: i,
    height: s
  } }) });
}
const Ya = typeof window < "u" ? window : void 0, Ww = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function Pu({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: i, onPaneContextMenu: s, onPaneScroll: a, paneClickDistance: c, deleteKeyCode: u, selectionKeyCode: l, selectionOnDrag: d, selectionMode: f, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: y, panActivationKeyCode: m, zoomActivationKeyCode: x, elementsSelectable: w, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: v, panOnScrollSpeed: j, panOnScrollMode: N, zoomOnDoubleClick: S, panOnDrag: k, autoPanOnSelection: D, defaultViewport: L, translateExtent: A, minZoom: _, maxZoom: R, preventScrolling: C, onSelectionContextMenu: I, noWheelClassName: E, noPanClassName: T, disableKeyboardA11y: $, onViewportChange: P, isControlledViewport: F }) {
  const { nodesSelectionActive: H, userSelectionActive: O } = pe(Ww, me), U = Ln(l, { target: Ya }), q = Ln(m, { target: Ya }), ne = q || k, ce = q || v, Z = d && ne !== !0, z = U || O || Z;
  return Nw({ deleteKeyCode: u, multiSelectionKeyCode: y }), r.jsx(Cw, { onPaneContextMenu: s, elementsSelectable: w, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: ce, panOnScrollSpeed: j, panOnScrollMode: N, zoomOnDoubleClick: S, panOnDrag: !U && ne, defaultViewport: L, translateExtent: A, minZoom: _, maxZoom: R, zoomActivationKeyCode: x, preventScrolling: C, noWheelClassName: E, noPanClassName: T, onViewportChange: P, isControlledViewport: F, paneClickDistance: c, selectionOnDrag: Z, children: r.jsxs(Aw, { onSelectionStart: p, onSelectionEnd: h, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: i, onPaneContextMenu: s, onPaneScroll: a, panOnDrag: ne, autoPanOnSelection: D, isSelecting: !!z, selectionMode: f, selectionKeyPressed: U, paneClickDistance: c, selectionOnDrag: Z, children: [e, H && r.jsx(Hw, { onSelectionContextMenu: I, noPanClassName: T, disableKeyboardA11y: $ })] }) });
}
Pu.displayName = "FlowRenderer";
const Fw = Ne(Pu), Bw = (e) => (t) => e ? os(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function Kw(e) {
  return pe(ie(Bw(e), [e]), me);
}
const Xw = (e) => e.updateNodeInternals;
function Yw() {
  const e = pe(Xw), [t] = B(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
    const o = /* @__PURE__ */ new Map();
    n.forEach((i) => {
      const s = i.target.getAttribute("data-id");
      o.set(s, {
        id: s,
        nodeElement: i.target,
        force: !0
      });
    }), e(o);
  }));
  return G(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function qw({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const i = xe(), s = re(null), a = re(null), c = re(e.sourcePosition), u = re(e.targetPosition), l = re(t), d = n && !!e.internals.handleBounds;
  return G(() => {
    s.current && !e.hidden && (!d || a.current !== s.current) && (a.current && o?.unobserve(a.current), o?.observe(s.current), a.current = s.current);
  }, [d, e.hidden]), G(() => () => {
    a.current && (o?.unobserve(a.current), a.current = null);
  }, []), G(() => {
    if (s.current) {
      const f = l.current !== t, p = c.current !== e.sourcePosition, h = u.current !== e.targetPosition;
      (f || p || h) && (l.current = t, c.current = e.sourcePosition, u.current = e.targetPosition, i.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: s.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), s;
}
function Uw({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: i, onContextMenu: s, onDoubleClick: a, nodesDraggable: c, elementsSelectable: u, nodesConnectable: l, nodesFocusable: d, resizeObserver: f, noDragClassName: p, noPanClassName: h, disableKeyboardA11y: y, rfId: m, nodeTypes: x, nodeClickDistance: w, onError: b }) {
  const { node: g, internals: v, isParent: j } = pe((z) => {
    const X = z.nodeLookup.get(e), le = z.parentLookup.has(e);
    return {
      node: X,
      internals: X.internals,
      isParent: le
    };
  }, me);
  let N = g.type || "default", S = x?.[N] || Xa[N];
  S === void 0 && (b?.("003", We.error003(N)), N = "default", S = x?.default || Xa.default);
  const k = !!(g.draggable || c && typeof g.draggable > "u"), D = !!(g.selectable || u && typeof g.selectable > "u"), L = !!(g.connectable || l && typeof g.connectable > "u"), A = !!(g.focusable || d && typeof g.focusable > "u"), _ = xe(), R = Jl(g), C = qw({ node: g, nodeType: N, hasDimensions: R, resizeObserver: f }), I = Du({
    nodeRef: C,
    disabled: g.hidden || !k,
    noDragClassName: p,
    handleSelector: g.dragHandle,
    nodeId: e,
    isSelectable: D,
    nodeClickDistance: w
  }), E = Tu();
  if (g.hidden)
    return null;
  const T = lt(g), $ = Vw(g), P = D || k || t || n || o || i, F = n ? (z) => n(z, { ...v.userNode }) : void 0, H = o ? (z) => o(z, { ...v.userNode }) : void 0, O = i ? (z) => i(z, { ...v.userNode }) : void 0, U = s ? (z) => s(z, { ...v.userNode }) : void 0, q = a ? (z) => a(z, { ...v.userNode }) : void 0, ne = (z) => {
    const { selectNodesOnDrag: X, nodeDragThreshold: le } = _.getState();
    D && (!X || !k || le > 0) && Tr({
      id: e,
      store: _,
      nodeRef: C
    }), t && t(z, { ...v.userNode });
  }, ce = (z) => {
    if (!(tu(z.nativeEvent) || y)) {
      if (Fl.includes(z.key) && D) {
        const X = z.key === "Escape";
        Tr({
          id: e,
          store: _,
          unselect: X,
          nodeRef: C
        });
      } else if (k && g.selected && Object.prototype.hasOwnProperty.call(Qo, z.key)) {
        z.preventDefault();
        const { ariaLabelConfig: X } = _.getState();
        _.setState({
          ariaLiveMessage: X["node.a11yDescription.ariaLiveMessage"]({
            direction: z.key.replace("Arrow", "").toLowerCase(),
            x: ~~v.positionAbsolute.x,
            y: ~~v.positionAbsolute.y
          })
        }), E({
          direction: Qo[z.key],
          factor: z.shiftKey ? 4 : 1
        });
      }
    }
  }, Z = () => {
    if (y || !C.current?.matches(":focus-visible"))
      return;
    const { transform: z, width: X, height: le, autoPanOnNodeFocus: ae, setCenter: J } = _.getState();
    if (!ae)
      return;
    os(/* @__PURE__ */ new Map([[e, g]]), { x: 0, y: 0, width: X, height: le }, z, !0).length > 0 || J(g.position.x + T.width / 2, g.position.y + T.height / 2, {
      zoom: z[2]
    });
  };
  return r.jsx("div", { className: Ce([
    "react-flow__node",
    `react-flow__node-${N}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [h]: k
    },
    g.className,
    {
      selected: g.selected,
      selectable: D,
      parent: j,
      draggable: k,
      dragging: I
    }
  ]), ref: C, style: {
    zIndex: v.z,
    transform: `translate(${v.positionAbsolute.x}px,${v.positionAbsolute.y}px)`,
    pointerEvents: P ? "all" : "none",
    visibility: R ? "visible" : "hidden",
    ...g.style,
    ...$
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: F, onMouseMove: H, onMouseLeave: O, onContextMenu: U, onClick: ne, onDoubleClick: q, onKeyDown: A ? ce : void 0, tabIndex: A ? 0 : void 0, onFocus: A ? Z : void 0, role: g.ariaRole ?? (A ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": y ? void 0 : `${bu}-${m}`, "aria-label": g.ariaLabel, ...g.domAttributes, children: r.jsx(Dw, { value: e, children: r.jsx(S, { id: e, data: g.data, type: N, positionAbsoluteX: v.positionAbsolute.x, positionAbsoluteY: v.positionAbsolute.y, selected: g.selected ?? !1, selectable: D, draggable: k, deletable: g.deletable ?? !0, isConnectable: L, sourcePosition: g.sourcePosition, targetPosition: g.targetPosition, dragging: I, dragHandle: g.dragHandle, zIndex: v.z, parentId: g.parentId, ...T }) }) });
}
var Zw = Ne(Uw);
const Gw = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function Mu(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: i, onError: s } = pe(Gw, me), a = Kw(e.onlyRenderVisibleElements), c = Yw();
  return r.jsx("div", { className: "react-flow__nodes", style: gi, children: a.map((u) => (
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
    r.jsx(Zw, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: i, nodeClickDistance: e.nodeClickDistance, onError: s }, u)
  )) });
}
Mu.displayName = "NodeRenderer";
const Jw = Ne(Mu);
function Qw(e) {
  return pe(ie((n) => {
    if (!e)
      return n.edges.map((i) => i.id);
    const o = [];
    if (n.width && n.height)
      for (const i of n.edges) {
        const s = n.nodeLookup.get(i.source), a = n.nodeLookup.get(i.target);
        s && a && qm({
          sourceNode: s,
          targetNode: a,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && o.push(i.id);
      }
    return o;
  }, [e]), me);
}
const ev = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return r.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, tv = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return r.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, qa = {
  [Zo.Arrow]: ev,
  [Zo.ArrowClosed]: tv
};
function nv(e) {
  const t = xe();
  return fe(() => Object.prototype.hasOwnProperty.call(qa, e) ? qa[e] : (t.getState().onError?.("009", We.error009(e)), null), [e]);
}
const ov = ({ id: e, type: t, color: n, width: o = 12.5, height: i = 12.5, markerUnits: s = "strokeWidth", strokeWidth: a, orient: c = "auto-start-reverse" }) => {
  const u = nv(t);
  return u ? r.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${i}`, viewBox: "-10 -10 20 20", markerUnits: s, orient: c, refX: "0", refY: "0", children: r.jsx(u, { color: n, strokeWidth: a }) }) : null;
}, Ru = ({ defaultColor: e, rfId: t }) => {
  const n = pe((s) => s.edges), o = pe((s) => s.defaultEdgeOptions), i = fe(() => nx(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return i.length ? r.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: r.jsx("defs", { children: i.map((s) => r.jsx(ov, { id: s.id, type: s.type, color: s.color, width: s.width, height: s.height, markerUnits: s.markerUnits, strokeWidth: s.strokeWidth, orient: s.orient }, s.id)) }) }) : null;
};
Ru.displayName = "MarkerDefinitions";
var iv = Ne(Ru);
function Lu({ x: e, y: t, label: n, labelStyle: o, labelShowBg: i = !0, labelBgStyle: s, labelBgPadding: a = [2, 4], labelBgBorderRadius: c = 2, children: u, className: l, ...d }) {
  const [f, p] = B({ x: 1, y: 0, width: 0, height: 0 }), h = Ce(["react-flow__edge-textwrapper", l]), y = re(null);
  return G(() => {
    if (y.current) {
      const m = y.current.getBBox();
      p({
        x: m.x,
        y: m.y,
        width: m.width,
        height: m.height
      });
    }
  }, [n]), n ? r.jsxs("g", { transform: `translate(${e - f.width / 2} ${t - f.height / 2})`, className: h, visibility: f.width ? "visible" : "hidden", ...d, children: [i && r.jsx("rect", { width: f.width + 2 * a[0], x: -a[0], y: -a[1], height: f.height + 2 * a[1], className: "react-flow__edge-textbg", style: s, rx: c, ry: c }), r.jsx("text", { className: "react-flow__edge-text", y: f.height / 2, dy: "0.3em", ref: y, style: o, children: n }), u] }) : null;
}
Lu.displayName = "EdgeText";
const rv = Ne(Lu);
function qn({ path: e, labelX: t, labelY: n, label: o, labelStyle: i, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u, interactionWidth: l = 20, ...d }) {
  return r.jsxs(r.Fragment, { children: [r.jsx("path", { ...d, d: e, fill: "none", className: Ce(["react-flow__edge-path", d.className]) }), l ? r.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, o && Ye(t) && Ye(n) ? r.jsx(rv, { x: t, y: n, label: o, labelStyle: i, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u }) : null] });
}
function Ua({ pos: e, x1: t, y1: n, x2: o, y2: i }) {
  return e === oe.Left || e === oe.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + i)];
}
function zu({ sourceX: e, sourceY: t, sourcePosition: n = oe.Bottom, targetX: o, targetY: i, targetPosition: s = oe.Top }) {
  const [a, c] = Ua({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: i
  }), [u, l] = Ua({
    pos: s,
    x1: o,
    y1: i,
    x2: e,
    y2: t
  }), [d, f, p, h] = ou({
    sourceX: e,
    sourceY: t,
    targetX: o,
    targetY: i,
    sourceControlX: a,
    sourceControlY: c,
    targetControlX: u,
    targetControlY: l
  });
  return [
    `M${e},${t} C${a},${c} ${u},${l} ${o},${i}`,
    d,
    f,
    p,
    h
  ];
}
function Vu(e) {
  return Ne(({ id: t, sourceX: n, sourceY: o, targetX: i, targetY: s, sourcePosition: a, targetPosition: c, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: m, markerStart: x, interactionWidth: w }) => {
    const [b, g, v] = zu({
      sourceX: n,
      sourceY: o,
      sourcePosition: a,
      targetX: i,
      targetY: s,
      targetPosition: c
    }), j = e.isInternal ? void 0 : t;
    return r.jsx(qn, { id: j, path: b, labelX: g, labelY: v, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: m, markerStart: x, interactionWidth: w });
  });
}
const sv = Vu({ isInternal: !1 }), Ou = Vu({ isInternal: !0 });
sv.displayName = "SimpleBezierEdge";
Ou.displayName = "SimpleBezierEdgeInternal";
function Hu(e) {
  return Ne(({ id: t, sourceX: n, sourceY: o, targetX: i, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, sourcePosition: h = oe.Bottom, targetPosition: y = oe.Top, markerEnd: m, markerStart: x, pathOptions: w, interactionWidth: b }) => {
    const [g, v, j] = Jo({
      sourceX: n,
      sourceY: o,
      sourcePosition: h,
      targetX: i,
      targetY: s,
      targetPosition: y,
      borderRadius: w?.borderRadius,
      offset: w?.offset,
      stepPosition: w?.stepPosition
    }), N = e.isInternal ? void 0 : t;
    return r.jsx(qn, { id: N, path: g, labelX: v, labelY: j, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: m, markerStart: x, interactionWidth: b });
  });
}
const Wu = Hu({ isInternal: !1 }), Fu = Hu({ isInternal: !0 });
Wu.displayName = "SmoothStepEdge";
Fu.displayName = "SmoothStepEdgeInternal";
function Bu(e) {
  return Ne(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return r.jsx(Wu, { ...n, id: o, pathOptions: fe(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const av = Bu({ isInternal: !1 }), Ku = Bu({ isInternal: !0 });
av.displayName = "StepEdge";
Ku.displayName = "StepEdgeInternal";
function Xu(e) {
  return Ne(({ id: t, sourceX: n, sourceY: o, targetX: i, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: y, interactionWidth: m }) => {
    const [x, w, b] = au({ sourceX: n, sourceY: o, targetX: i, targetY: s }), g = e.isInternal ? void 0 : t;
    return r.jsx(qn, { id: g, path: x, labelX: w, labelY: b, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: y, interactionWidth: m });
  });
}
const cv = Xu({ isInternal: !1 }), Yu = Xu({ isInternal: !0 });
cv.displayName = "StraightEdge";
Yu.displayName = "StraightEdgeInternal";
function qu(e) {
  return Ne(({ id: t, sourceX: n, sourceY: o, targetX: i, targetY: s, sourcePosition: a = oe.Bottom, targetPosition: c = oe.Top, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: m, markerStart: x, pathOptions: w, interactionWidth: b }) => {
    const [g, v, j] = iu({
      sourceX: n,
      sourceY: o,
      sourcePosition: a,
      targetX: i,
      targetY: s,
      targetPosition: c,
      curvature: w?.curvature
    }), N = e.isInternal ? void 0 : t;
    return r.jsx(qn, { id: N, path: g, labelX: v, labelY: j, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: m, markerStart: x, interactionWidth: b });
  });
}
const lv = qu({ isInternal: !1 }), Uu = qu({ isInternal: !0 });
lv.displayName = "BezierEdge";
Uu.displayName = "BezierEdgeInternal";
const Za = {
  default: Uu,
  straight: Yu,
  step: Ku,
  smoothstep: Fu,
  simplebezier: Ou
}, Ga = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, uv = (e, t, n) => n === oe.Left ? e - t : n === oe.Right ? e + t : e, dv = (e, t, n) => n === oe.Top ? e - t : n === oe.Bottom ? e + t : e, Ja = "react-flow__edgeupdater";
function Qa({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: i, onMouseEnter: s, onMouseOut: a, type: c }) {
  return r.jsx("circle", { onMouseDown: i, onMouseEnter: s, onMouseOut: a, className: Ce([Ja, `${Ja}-${c}`]), cx: uv(t, o, e), cy: dv(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function fv({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: i, targetX: s, targetY: a, sourcePosition: c, targetPosition: u, onReconnect: l, onReconnectStart: d, onReconnectEnd: f, setReconnecting: p, setUpdateHover: h }) {
  const y = xe(), m = (v, j) => {
    if (v.button !== 0)
      return;
    const { autoPanOnConnect: N, domNode: S, connectionMode: k, connectionRadius: D, lib: L, onConnectStart: A, cancelConnection: _, nodeLookup: R, rfId: C, panBy: I, updateConnection: E } = y.getState(), T = j.type === "target", $ = (H, O) => {
      p(!1), f?.(H, n, j.type, O);
    }, P = (H) => l?.(n, H), F = (H, O) => {
      p(!0), d?.(v, n, j.type), A?.(H, O);
    };
    Dr.onPointerDown(v.nativeEvent, {
      autoPanOnConnect: N,
      connectionMode: k,
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
      isValidConnection: (...H) => y.getState().isValidConnection?.(...H) ?? !0,
      onConnect: P,
      onConnectStart: F,
      onConnectEnd: (...H) => y.getState().onConnectEnd?.(...H),
      onReconnectEnd: $,
      updateConnection: E,
      getTransform: () => y.getState().transform,
      getFromHandle: () => y.getState().connection.fromHandle,
      dragThreshold: y.getState().connectionDragThreshold,
      handleDomNode: v.currentTarget
    });
  }, x = (v) => m(v, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), w = (v) => m(v, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), b = () => h(!0), g = () => h(!1);
  return r.jsxs(r.Fragment, { children: [(e === !0 || e === "source") && r.jsx(Qa, { position: c, centerX: o, centerY: i, radius: t, onMouseDown: x, onMouseEnter: b, onMouseOut: g, type: "source" }), (e === !0 || e === "target") && r.jsx(Qa, { position: u, centerX: s, centerY: a, radius: t, onMouseDown: w, onMouseEnter: b, onMouseOut: g, type: "target" })] });
}
function pv({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: i, onDoubleClick: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, rfId: y, edgeTypes: m, noPanClassName: x, onError: w, disableKeyboardA11y: b }) {
  let g = pe((J) => J.edgeLookup.get(e));
  const v = pe((J) => J.defaultEdgeOptions);
  g = v ? { ...v, ...g } : g;
  let j = g.type || "default", N = m?.[j] || Za[j];
  N === void 0 && (w?.("011", We.error011(j)), j = "default", N = m?.default || Za.default);
  const S = !!(g.focusable || t && typeof g.focusable > "u"), k = typeof f < "u" && (g.reconnectable || n && typeof g.reconnectable > "u"), D = !!(g.selectable || o && typeof g.selectable > "u"), L = re(null), [A, _] = B(!1), [R, C] = B(!1), I = xe(), { zIndex: E, sourceX: T, sourceY: $, targetX: P, targetY: F, sourcePosition: H, targetPosition: O } = pe(ie((J) => {
    const te = J.nodeLookup.get(g.source), de = J.nodeLookup.get(g.target);
    if (!te || !de)
      return {
        zIndex: g.zIndex,
        ...Ga
      };
    const V = tx({
      id: e,
      sourceNode: te,
      targetNode: de,
      sourceHandle: g.sourceHandle || null,
      targetHandle: g.targetHandle || null,
      connectionMode: J.connectionMode,
      onError: w
    });
    return {
      zIndex: Ym({
        selected: g.selected,
        zIndex: g.zIndex,
        sourceNode: te,
        targetNode: de,
        elevateOnSelect: J.elevateEdgesOnSelect,
        zIndexMode: J.zIndexMode
      }),
      ...V || Ga
    };
  }, [g.source, g.target, g.sourceHandle, g.targetHandle, g.selected, g.zIndex]), me), U = fe(() => g.markerStart ? `url('#${Ar(g.markerStart, y)}')` : void 0, [g.markerStart, y]), q = fe(() => g.markerEnd ? `url('#${Ar(g.markerEnd, y)}')` : void 0, [g.markerEnd, y]);
  if (g.hidden || T === null || $ === null || P === null || F === null)
    return null;
  const ne = (J) => {
    const { addSelectedEdges: te, unselectNodesAndEdges: de, multiSelectionActive: V } = I.getState();
    D && (I.setState({ nodesSelectionActive: !1 }), g.selected && V ? (de({ nodes: [], edges: [g] }), L.current?.blur()) : te([e])), i && i(J, g);
  }, ce = s ? (J) => {
    s(J, { ...g });
  } : void 0, Z = a ? (J) => {
    a(J, { ...g });
  } : void 0, z = c ? (J) => {
    c(J, { ...g });
  } : void 0, X = u ? (J) => {
    u(J, { ...g });
  } : void 0, le = l ? (J) => {
    l(J, { ...g });
  } : void 0, ae = (J) => {
    if (!b && Fl.includes(J.key) && D) {
      const { unselectNodesAndEdges: te, addSelectedEdges: de } = I.getState();
      J.key === "Escape" ? (L.current?.blur(), te({ edges: [g] })) : de([e]);
    }
  };
  return r.jsx("svg", { style: { zIndex: E }, children: r.jsxs("g", { className: Ce([
    "react-flow__edge",
    `react-flow__edge-${j}`,
    g.className,
    x,
    {
      selected: g.selected,
      animated: g.animated,
      inactive: !D && !i,
      updating: A,
      selectable: D
    }
  ]), onClick: ne, onDoubleClick: ce, onContextMenu: Z, onMouseEnter: z, onMouseMove: X, onMouseLeave: le, onKeyDown: S ? ae : void 0, tabIndex: S ? 0 : void 0, role: g.ariaRole ?? (S ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": g.ariaLabel === null ? void 0 : g.ariaLabel || `Edge from ${g.source} to ${g.target}`, "aria-describedby": S ? `${Nu}-${y}` : void 0, ref: L, ...g.domAttributes, children: [!R && r.jsx(N, { id: e, source: g.source, target: g.target, type: g.type, selected: g.selected, animated: g.animated, selectable: D, deletable: g.deletable ?? !0, label: g.label, labelStyle: g.labelStyle, labelShowBg: g.labelShowBg, labelBgStyle: g.labelBgStyle, labelBgPadding: g.labelBgPadding, labelBgBorderRadius: g.labelBgBorderRadius, sourceX: T, sourceY: $, targetX: P, targetY: F, sourcePosition: H, targetPosition: O, data: g.data, style: g.style, sourceHandleId: g.sourceHandle, targetHandleId: g.targetHandle, markerStart: U, markerEnd: q, pathOptions: "pathOptions" in g ? g.pathOptions : void 0, interactionWidth: g.interactionWidth }), k && r.jsx(fv, { edge: g, isReconnectable: k, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, sourceX: T, sourceY: $, targetX: P, targetY: F, sourcePosition: H, targetPosition: O, setUpdateHover: _, setReconnecting: C })] }) });
}
var hv = Ne(pv);
const gv = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function Zu({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: i, onReconnect: s, onEdgeContextMenu: a, onEdgeMouseEnter: c, onEdgeMouseMove: u, onEdgeMouseLeave: l, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: p, onReconnectStart: h, onReconnectEnd: y, disableKeyboardA11y: m }) {
  const { edgesFocusable: x, edgesReconnectable: w, elementsSelectable: b, onError: g } = pe(gv, me), v = Qw(t);
  return r.jsxs("div", { className: "react-flow__edges", children: [r.jsx(iv, { defaultColor: e, rfId: n }), v.map((j) => r.jsx(hv, { id: j, edgesFocusable: x, edgesReconnectable: w, elementsSelectable: b, noPanClassName: i, onReconnect: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, onClick: d, reconnectRadius: f, onDoubleClick: p, onReconnectStart: h, onReconnectEnd: y, rfId: n, onError: g, edgeTypes: o, disableKeyboardA11y: m }, j))] });
}
Zu.displayName = "EdgeRenderer";
const yv = Ne(Zu), mv = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function xv({ children: e }) {
  const t = pe(mv);
  return r.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function wv(e) {
  const t = fs(), n = re(!1);
  G(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const vv = (e) => e.panZoom?.syncViewport;
function bv(e) {
  const t = pe(vv), n = xe();
  return G(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function Nv(e) {
  return e.connection.inProgress ? { ...e.connection, to: sn(e.connection.to, e.transform) } : { ...e.connection };
}
function jv(e) {
  return Nv;
}
function Sv(e) {
  const t = jv();
  return pe(t, me);
}
const Cv = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function Ev({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: i, width: s, height: a, isValid: c, inProgress: u } = pe(Cv, me);
  return !(s && i && u) ? null : r.jsx("svg", { style: e, width: s, height: a, className: "react-flow__connectionline react-flow__container", children: r.jsx("g", { className: Ce(["react-flow__connection", Xl(c)]), children: r.jsx(Gu, { style: t, type: n, CustomComponent: o, isValid: c }) }) });
}
const Gu = ({ style: e, type: t = pt.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: i, from: s, fromNode: a, fromHandle: c, fromPosition: u, to: l, toNode: d, toHandle: f, toPosition: p, pointer: h } = Sv();
  if (!i)
    return;
  if (n)
    return r.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: a, fromHandle: c, fromX: s.x, fromY: s.y, toX: l.x, toY: l.y, fromPosition: u, toPosition: p, connectionStatus: Xl(o), toNode: d, toHandle: f, pointer: h });
  let y = "";
  const m = {
    sourceX: s.x,
    sourceY: s.y,
    sourcePosition: u,
    targetX: l.x,
    targetY: l.y,
    targetPosition: p
  };
  switch (t) {
    case pt.Bezier:
      [y] = iu(m);
      break;
    case pt.SimpleBezier:
      [y] = zu(m);
      break;
    case pt.Step:
      [y] = Jo({
        ...m,
        borderRadius: 0
      });
      break;
    case pt.SmoothStep:
      [y] = Jo(m);
      break;
    default:
      [y] = au(m);
  }
  return r.jsx("path", { d: y, fill: "none", className: "react-flow__connection-path", style: e });
};
Gu.displayName = "ConnectionLine";
const kv = {};
function ec(e = kv) {
  re(e), xe(), G(() => {
  }, [e]);
}
function Iv() {
  xe(), re(!1), G(() => {
  }, []);
}
function Ju({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: i, onNodeDoubleClick: s, onEdgeDoubleClick: a, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: p, onSelectionEnd: h, connectionLineType: y, connectionLineStyle: m, connectionLineComponent: x, connectionLineContainerStyle: w, selectionKeyCode: b, selectionOnDrag: g, selectionMode: v, multiSelectionKeyCode: j, panActivationKeyCode: N, zoomActivationKeyCode: S, deleteKeyCode: k, onlyRenderVisibleElements: D, elementsSelectable: L, defaultViewport: A, translateExtent: _, minZoom: R, maxZoom: C, preventScrolling: I, defaultMarkerColor: E, zoomOnScroll: T, zoomOnPinch: $, panOnScroll: P, panOnScrollSpeed: F, panOnScrollMode: H, zoomOnDoubleClick: O, panOnDrag: U, autoPanOnSelection: q, onPaneClick: ne, onPaneMouseEnter: ce, onPaneMouseMove: Z, onPaneMouseLeave: z, onPaneScroll: X, onPaneContextMenu: le, paneClickDistance: ae, nodeClickDistance: J, onEdgeContextMenu: te, onEdgeMouseEnter: de, onEdgeMouseMove: V, onEdgeMouseLeave: Q, reconnectRadius: he, onReconnect: we, onReconnectStart: Ie, onReconnectEnd: Ae, noDragClassName: Re, noWheelClassName: ot, noPanClassName: Fe, disableKeyboardA11y: Be, nodeExtent: Te, rfId: Le, viewport: M, onViewportChange: W }) {
  return ec(e), ec(t), Iv(), wv(n), bv(M), r.jsx(Fw, { onPaneClick: ne, onPaneMouseEnter: ce, onPaneMouseMove: Z, onPaneMouseLeave: z, onPaneContextMenu: le, onPaneScroll: X, paneClickDistance: ae, deleteKeyCode: k, selectionKeyCode: b, selectionOnDrag: g, selectionMode: v, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: j, panActivationKeyCode: N, zoomActivationKeyCode: S, elementsSelectable: L, zoomOnScroll: T, zoomOnPinch: $, zoomOnDoubleClick: O, panOnScroll: P, panOnScrollSpeed: F, panOnScrollMode: H, panOnDrag: U, autoPanOnSelection: q, defaultViewport: A, translateExtent: _, minZoom: R, maxZoom: C, onSelectionContextMenu: f, preventScrolling: I, noDragClassName: Re, noWheelClassName: ot, noPanClassName: Fe, disableKeyboardA11y: Be, onViewportChange: W, isControlledViewport: !!M, children: r.jsxs(xv, { children: [r.jsx(yv, { edgeTypes: t, onEdgeClick: i, onEdgeDoubleClick: a, onReconnect: we, onReconnectStart: Ie, onReconnectEnd: Ae, onlyRenderVisibleElements: D, onEdgeContextMenu: te, onEdgeMouseEnter: de, onEdgeMouseMove: V, onEdgeMouseLeave: Q, reconnectRadius: he, defaultMarkerColor: E, noPanClassName: Fe, disableKeyboardA11y: Be, rfId: Le }), r.jsx(Ev, { style: m, type: y, component: x, containerStyle: w }), r.jsx("div", { className: "react-flow__edgelabel-renderer" }), r.jsx(Jw, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, nodeClickDistance: J, onlyRenderVisibleElements: D, noPanClassName: Fe, noDragClassName: Re, disableKeyboardA11y: Be, nodeExtent: Te, rfId: Le }), r.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Ju.displayName = "GraphView";
const Av = Ne(Ju), _v = Gl(), tc = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: i, height: s, fitView: a, fitViewOptions: c, minZoom: u = 0.5, maxZoom: l = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: p = "basic" } = {}) => {
  const h = /* @__PURE__ */ new Map(), y = /* @__PURE__ */ new Map(), m = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), w = o ?? t ?? [], b = n ?? e ?? [], g = d ?? [0, 0], v = f ?? $n;
  uu(m, x, w);
  const { nodesInitialized: j } = _r(b, h, y, {
    nodeOrigin: g,
    nodeExtent: v,
    zIndexMode: p
  });
  let N = [0, 0, 1];
  if (a && i && s) {
    const S = Xn(h, {
      filter: (A) => !!((A.width || A.initialWidth) && (A.height || A.initialHeight))
    }), { x: k, y: D, zoom: L } = rs(S, i, s, u, l, c?.padding ?? 0.1);
    N = [k, D, L];
  }
  return {
    rfId: "1",
    width: i ?? 0,
    height: s ?? 0,
    transform: N,
    nodes: b,
    nodesInitialized: j,
    nodeLookup: h,
    parentLookup: y,
    edges: w,
    edgeLookup: x,
    connectionLookup: m,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: o !== void 0,
    panZoom: null,
    minZoom: u,
    maxZoom: l,
    translateExtent: $n,
    nodeExtent: v,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: Gt.Strict,
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
    connection: { ...Kl },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: _v,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Bl,
    zIndexMode: p,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, Dv = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: i, height: s, fitView: a, fitViewOptions: c, minZoom: u, maxZoom: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p }) => Bx((h, y) => {
  async function m() {
    const { nodeLookup: x, panZoom: w, fitViewOptions: b, fitViewResolver: g, width: v, height: j, minZoom: N, maxZoom: S } = y();
    w && (await Om({
      nodes: x,
      width: v,
      height: j,
      panZoom: w,
      minZoom: N,
      maxZoom: S
    }, b), g?.resolve(!0), h({ fitViewResolver: null }));
  }
  return {
    ...tc({
      nodes: e,
      edges: t,
      width: i,
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
    setNodes: (x) => {
      const { nodeLookup: w, parentLookup: b, nodeOrigin: g, elevateNodesOnSelect: v, fitViewQueued: j, zIndexMode: N, nodesSelectionActive: S } = y(), { nodesInitialized: k, hasSelectedNodes: D } = _r(x, w, b, {
        nodeOrigin: g,
        nodeExtent: f,
        elevateNodesOnSelect: v,
        checkEquality: !0,
        zIndexMode: N
      }), L = S && D;
      j && k ? (m(), h({
        nodes: x,
        nodesInitialized: k,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: L
      })) : h({ nodes: x, nodesInitialized: k, nodesSelectionActive: L });
    },
    setEdges: (x) => {
      const { connectionLookup: w, edgeLookup: b } = y();
      uu(w, b, x), h({ edges: x });
    },
    setDefaultNodesAndEdges: (x, w) => {
      if (x) {
        const { setNodes: b } = y();
        b(x), h({ hasDefaultNodes: !0 });
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
    updateNodeInternals: (x) => {
      const { triggerNodeChanges: w, nodeLookup: b, parentLookup: g, domNode: v, nodeOrigin: j, nodeExtent: N, debug: S, fitViewQueued: k, zIndexMode: D } = y(), { changes: L, updatedInternals: A } = lx(x, b, g, v, j, N, D);
      A && (rx(b, g, { nodeOrigin: j, nodeExtent: N, zIndexMode: D }), k ? (m(), h({ fitViewQueued: !1, fitViewOptions: void 0 })) : h({}), L?.length > 0 && (S && console.log("React Flow: trigger node changes", L), w?.(L)));
    },
    updateNodePositions: (x, w = !1) => {
      const b = [];
      let g = [];
      const { nodeLookup: v, triggerNodeChanges: j, connection: N, updateConnection: S, onNodesChangeMiddlewareMap: k } = y();
      for (const [D, L] of x) {
        const A = v.get(D), _ = !!(A?.expandParent && A?.parentId && L?.position), R = {
          id: D,
          type: "position",
          position: _ ? {
            x: Math.max(0, L.position.x),
            y: Math.max(0, L.position.y)
          } : L.position,
          dragging: w
        };
        if (A && N.inProgress && N.fromNode.id === A.id) {
          const C = kt(A, N.fromHandle, oe.Left, !0);
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
        }), g.push(R);
      }
      if (b.length > 0) {
        const { parentLookup: D, nodeOrigin: L } = y(), A = ds(b, v, D, L);
        g.push(...A);
      }
      for (const D of k.values())
        g = D(g);
      j(g);
    },
    triggerNodeChanges: (x) => {
      const { onNodesChange: w, setNodes: b, nodes: g, hasDefaultNodes: v, debug: j } = y();
      if (x?.length) {
        if (v) {
          const N = Cu(x, g);
          b(N);
        }
        j && console.log("React Flow: trigger node changes", x), w?.(x);
      }
    },
    triggerEdgeChanges: (x) => {
      const { onEdgesChange: w, setEdges: b, edges: g, hasDefaultEdges: v, debug: j } = y();
      if (x?.length) {
        if (v) {
          const N = Eu(x, g);
          b(N);
        }
        j && console.log("React Flow: trigger edge changes", x), w?.(x);
      }
    },
    addSelectedNodes: (x) => {
      const { multiSelectionActive: w, edgeLookup: b, nodeLookup: g, triggerNodeChanges: v, triggerEdgeChanges: j } = y();
      if (w) {
        const N = x.map((S) => xt(S, !0));
        v(N);
        return;
      }
      v(Wt(g, /* @__PURE__ */ new Set([...x]), !0)), j(Wt(b));
    },
    addSelectedEdges: (x) => {
      const { multiSelectionActive: w, edgeLookup: b, nodeLookup: g, triggerNodeChanges: v, triggerEdgeChanges: j } = y();
      if (w) {
        const N = x.map((S) => xt(S, !0));
        j(N);
        return;
      }
      j(Wt(b, /* @__PURE__ */ new Set([...x]))), v(Wt(g, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: x, edges: w } = {}) => {
      const { edges: b, nodes: g, nodeLookup: v, triggerNodeChanges: j, triggerEdgeChanges: N } = y(), S = x || g, k = w || b, D = [];
      for (const A of S) {
        if (!A.selected)
          continue;
        const _ = v.get(A.id);
        _ && (_.selected = !1), D.push(xt(A.id, !1));
      }
      const L = [];
      for (const A of k)
        A.selected && L.push(xt(A.id, !1));
      j(D), N(L);
    },
    setMinZoom: (x) => {
      const { panZoom: w, maxZoom: b } = y();
      w?.setScaleExtent([x, b]), h({ minZoom: x });
    },
    setMaxZoom: (x) => {
      const { panZoom: w, minZoom: b } = y();
      w?.setScaleExtent([b, x]), h({ maxZoom: x });
    },
    setTranslateExtent: (x) => {
      y().panZoom?.setTranslateExtent(x), h({ translateExtent: x });
    },
    resetSelectedElements: () => {
      const { edges: x, nodes: w, triggerNodeChanges: b, triggerEdgeChanges: g, elementsSelectable: v } = y();
      if (!v)
        return;
      const j = w.reduce((S, k) => k.selected ? [...S, xt(k.id, !1)] : S, []), N = x.reduce((S, k) => k.selected ? [...S, xt(k.id, !1)] : S, []);
      b(j), g(N);
    },
    setNodeExtent: (x) => {
      const { nodes: w, nodeLookup: b, parentLookup: g, nodeOrigin: v, elevateNodesOnSelect: j, nodeExtent: N, zIndexMode: S } = y();
      x[0][0] === N[0][0] && x[0][1] === N[0][1] && x[1][0] === N[1][0] && x[1][1] === N[1][1] || (_r(w, b, g, {
        nodeOrigin: v,
        nodeExtent: x,
        elevateNodesOnSelect: j,
        checkEquality: !1,
        zIndexMode: S
      }), h({ nodeExtent: x }));
    },
    panBy: (x) => {
      const { transform: w, width: b, height: g, panZoom: v, translateExtent: j } = y();
      return ux({ delta: x, panZoom: v, transform: w, translateExtent: j, width: b, height: g });
    },
    setCenter: async (x, w, b) => {
      const { width: g, height: v, maxZoom: j, panZoom: N } = y();
      if (!N)
        return !1;
      const S = typeof b?.zoom < "u" ? b.zoom : j;
      return await N.setViewport({
        x: g / 2 - x * S,
        y: v / 2 - w * S,
        zoom: S
      }, { duration: b?.duration, ease: b?.ease, interpolate: b?.interpolate }), !0;
    },
    cancelConnection: () => {
      h({
        connection: { ...Kl }
      });
    },
    updateConnection: (x) => {
      h({ connection: x });
    },
    reset: () => h({ ...tc() })
  };
}, Object.is);
function Tv({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: i, initialHeight: s, initialMinZoom: a, initialMaxZoom: c, initialFitViewOptions: u, fitView: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p, children: h }) {
  const [y] = B(() => Dv({
    nodes: e,
    edges: t,
    defaultNodes: n,
    defaultEdges: o,
    width: i,
    height: s,
    fitView: l,
    minZoom: a,
    maxZoom: c,
    fitViewOptions: u,
    nodeOrigin: d,
    nodeExtent: f,
    zIndexMode: p
  }));
  return r.jsx(qx, { value: y, children: r.jsx(xw, { children: h }) });
}
function $v({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: i, width: s, height: a, fitView: c, fitViewOptions: u, minZoom: l, maxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h }) {
  return zn(pi) ? r.jsx(r.Fragment, { children: e }) : r.jsx(Tv, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: i, initialWidth: s, initialHeight: a, fitView: c, initialFitViewOptions: u, initialMinZoom: l, initialMaxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h, children: e });
}
const Pv = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function Mv({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: i, nodeTypes: s, edgeTypes: a, onNodeClick: c, onEdgeClick: u, onInit: l, onMove: d, onMoveStart: f, onMoveEnd: p, onConnect: h, onConnectStart: y, onConnectEnd: m, onClickConnectStart: x, onClickConnectEnd: w, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: v, onNodeContextMenu: j, onNodeDoubleClick: N, onNodeDragStart: S, onNodeDrag: k, onNodeDragStop: D, onNodesDelete: L, onEdgesDelete: A, onDelete: _, onSelectionChange: R, onSelectionDragStart: C, onSelectionDrag: I, onSelectionDragStop: E, onSelectionContextMenu: T, onSelectionStart: $, onSelectionEnd: P, onBeforeDelete: F, connectionMode: H, connectionLineType: O = pt.Bezier, connectionLineStyle: U, connectionLineComponent: q, connectionLineContainerStyle: ne, deleteKeyCode: ce = "Backspace", selectionKeyCode: Z = "Shift", selectionOnDrag: z = !1, selectionMode: X = Pn.Full, panActivationKeyCode: le = "Space", multiSelectionKeyCode: ae = Rn() ? "Meta" : "Control", zoomActivationKeyCode: J = Rn() ? "Meta" : "Control", snapToGrid: te, snapGrid: de, onlyRenderVisibleElements: V = !1, selectNodesOnDrag: Q, nodesDraggable: he, autoPanOnNodeFocus: we, nodesConnectable: Ie, nodesFocusable: Ae, nodeOrigin: Re = ju, edgesFocusable: ot, edgesReconnectable: Fe, elementsSelectable: Be = !0, defaultViewport: Te = aw, minZoom: Le = 0.5, maxZoom: M = 2, translateExtent: W = $n, preventScrolling: K = !0, nodeExtent: ee, defaultMarkerColor: se = "#b1b1b7", zoomOnScroll: ye = !0, zoomOnPinch: ge = !0, panOnScroll: Ee = !1, panOnScrollSpeed: _e = 0.5, panOnScrollMode: be = Nt.Free, zoomOnDoubleClick: ke = !0, panOnDrag: Zn = !0, onPaneClick: mt, onPaneMouseEnter: Tt, onPaneMouseMove: Ze, onPaneMouseLeave: $t, onPaneScroll: xi, onPaneContextMenu: Gn, paneClickDistance: wi = 1, nodeClickDistance: vi = 0, children: ut, onReconnect: bi, onReconnectStart: Pt, onReconnectEnd: Ni, onEdgeContextMenu: Mt, onEdgeDoubleClick: ji, onEdgeMouseEnter: Jn, onEdgeMouseMove: Qn, onEdgeMouseLeave: Si, reconnectRadius: Rt = 10, onNodesChange: Ci, onEdgesChange: Ei, noDragClassName: ki = "nodrag", noWheelClassName: cn = "nowheel", noPanClassName: eo = "nopan", fitView: to, fitViewOptions: no, connectOnClick: Ii, attributionPosition: Ai, proOptions: _i, defaultEdgeOptions: Di, elevateNodesOnSelect: Ti = !0, elevateEdgesOnSelect: $i = !1, disableKeyboardA11y: oo = !1, autoPanOnConnect: Pi, autoPanOnNodeDrag: Mi, autoPanOnSelection: Ri = !0, autoPanSpeed: Li, connectionRadius: io, isValidConnection: ro, onError: so, style: ao, id: co, nodeDragThreshold: zi, connectionDragThreshold: Vi, viewport: Oi, onViewportChange: Hi, width: Wi, height: Fi, colorMode: Bi = "light", debug: Ki, onScroll: lo, ariaLabelConfig: Xi, zIndexMode: uo = "basic", ...Yi }, qi) {
  const ln = co || "1", Ui = dw(Bi), Lt = ie((un) => {
    un.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), lo?.(un);
  }, [lo]);
  return r.jsx("div", { "data-testid": "rf__wrapper", ...Yi, onScroll: Lt, style: { ...ao, ...Pv }, ref: qi, className: Ce(["react-flow", i, Ui]), id: co, role: "application", children: r.jsxs($v, { nodes: e, edges: t, width: Wi, height: Fi, fitView: to, fitViewOptions: no, minZoom: Le, maxZoom: M, nodeOrigin: Re, nodeExtent: ee, zIndexMode: uo, children: [r.jsx(uw, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: h, onConnectStart: y, onConnectEnd: m, onClickConnectStart: x, onClickConnectEnd: w, nodesDraggable: he, autoPanOnNodeFocus: we, nodesConnectable: Ie, nodesFocusable: Ae, edgesFocusable: ot, edgesReconnectable: Fe, elementsSelectable: Be, elevateNodesOnSelect: Ti, elevateEdgesOnSelect: $i, minZoom: Le, maxZoom: M, nodeExtent: ee, onNodesChange: Ci, onEdgesChange: Ei, snapToGrid: te, snapGrid: de, connectionMode: H, translateExtent: W, connectOnClick: Ii, defaultEdgeOptions: Di, fitView: to, fitViewOptions: no, onNodesDelete: L, onEdgesDelete: A, onDelete: _, onNodeDragStart: S, onNodeDrag: k, onNodeDragStop: D, onSelectionDrag: I, onSelectionDragStart: C, onSelectionDragStop: E, onMove: d, onMoveStart: f, onMoveEnd: p, noPanClassName: eo, nodeOrigin: Re, rfId: ln, autoPanOnConnect: Pi, autoPanOnNodeDrag: Mi, autoPanSpeed: Li, onError: so, connectionRadius: io, isValidConnection: ro, selectNodesOnDrag: Q, nodeDragThreshold: zi, connectionDragThreshold: Vi, onBeforeDelete: F, debug: Ki, ariaLabelConfig: Xi, zIndexMode: uo }), r.jsx(Av, { onInit: l, onNodeClick: c, onEdgeClick: u, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: v, onNodeContextMenu: j, onNodeDoubleClick: N, nodeTypes: s, edgeTypes: a, connectionLineType: O, connectionLineStyle: U, connectionLineComponent: q, connectionLineContainerStyle: ne, selectionKeyCode: Z, selectionOnDrag: z, selectionMode: X, deleteKeyCode: ce, multiSelectionKeyCode: ae, panActivationKeyCode: le, zoomActivationKeyCode: J, onlyRenderVisibleElements: V, defaultViewport: Te, translateExtent: W, minZoom: Le, maxZoom: M, preventScrolling: K, zoomOnScroll: ye, zoomOnPinch: ge, zoomOnDoubleClick: ke, panOnScroll: Ee, panOnScrollSpeed: _e, panOnScrollMode: be, panOnDrag: Zn, autoPanOnSelection: Ri, onPaneClick: mt, onPaneMouseEnter: Tt, onPaneMouseMove: Ze, onPaneMouseLeave: $t, onPaneScroll: xi, onPaneContextMenu: Gn, paneClickDistance: wi, nodeClickDistance: vi, onSelectionContextMenu: T, onSelectionStart: $, onSelectionEnd: P, onReconnect: bi, onReconnectStart: Pt, onReconnectEnd: Ni, onEdgeContextMenu: Mt, onEdgeDoubleClick: ji, onEdgeMouseEnter: Jn, onEdgeMouseMove: Qn, onEdgeMouseLeave: Si, reconnectRadius: Rt, defaultMarkerColor: se, noDragClassName: ki, noWheelClassName: cn, noPanClassName: eo, rfId: ln, disableKeyboardA11y: oo, nodeExtent: ee, viewport: Oi, onViewportChange: Hi }), r.jsx(sw, { onSelectionChange: R }), ut, r.jsx(tw, { proOptions: _i, position: Ai }), r.jsx(ew, { rfId: ln, disableKeyboardA11y: oo })] }) });
}
var Qu = Au(Mv);
const Rv = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function Lv({ children: e }) {
  const t = pe(Rv);
  return t ? Yx.createPortal(e, t) : null;
}
function zv({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return r.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ce(["react-flow__background-pattern", n, o]) });
}
function Vv({ radius: e, className: t }) {
  return r.jsx("circle", { cx: e, cy: e, r: e, className: Ce(["react-flow__background-pattern", "dots", t]) });
}
var ht;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(ht || (ht = {}));
const Ov = {
  [ht.Dots]: 1,
  [ht.Lines]: 1,
  [ht.Cross]: 6
}, Hv = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function ed({
  id: e,
  variant: t = ht.Dots,
  // only used for dots and cross
  gap: n = 20,
  // only used for lines and cross
  size: o,
  lineWidth: i = 1,
  offset: s = 0,
  color: a,
  bgColor: c,
  style: u,
  className: l,
  patternClassName: d
}) {
  const f = re(null), { transform: p, patternId: h } = pe(Hv, me), y = o || Ov[t], m = t === ht.Dots, x = t === ht.Cross, w = Array.isArray(n) ? n : [n, n], b = [w[0] * p[2] || 1, w[1] * p[2] || 1], g = y * p[2], v = Array.isArray(s) ? s : [s, s], j = x ? [g, g] : b, N = [
    v[0] * p[2] || 1 + j[0] / 2,
    v[1] * p[2] || 1 + j[1] / 2
  ], S = `${h}${e || ""}`;
  return r.jsxs("svg", { className: Ce(["react-flow__background", l]), style: {
    ...u,
    ...gi,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": a
  }, ref: f, "data-testid": "rf__background", children: [r.jsx("pattern", { id: S, x: p[0] % b[0], y: p[1] % b[1], width: b[0], height: b[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${N[0]},-${N[1]})`, children: m ? r.jsx(Vv, { radius: g / 2, className: d }) : r.jsx(zv, { dimensions: j, lineWidth: i, variant: t, className: d }) }), r.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${S})` })] });
}
ed.displayName = "Background";
const td = Ne(ed);
function Wv() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: r.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function Fv() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: r.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function Bv() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: r.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function Kv() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: r.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function Xv() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: r.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function vo({ children: e, className: t, ...n }) {
  return r.jsx("button", { type: "button", className: Ce(["react-flow__controls-button", t]), ...n, children: e });
}
const Yv = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function nd({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: i, onZoomIn: s, onZoomOut: a, onFitView: c, onInteractiveChange: u, className: l, children: d, position: f = "bottom-left", orientation: p = "vertical", "aria-label": h }) {
  const y = xe(), { isInteractive: m, minZoomReached: x, maxZoomReached: w, ariaLabelConfig: b } = pe(Yv, me), { zoomIn: g, zoomOut: v, fitView: j } = fs(), N = () => {
    g(), s?.();
  }, S = () => {
    v(), a?.();
  }, k = () => {
    j(i), c?.();
  }, D = () => {
    y.setState({
      nodesDraggable: !m,
      nodesConnectable: !m,
      elementsSelectable: !m
    }), u?.(!m);
  }, L = p === "horizontal" ? "horizontal" : "vertical";
  return r.jsxs(hi, { className: Ce(["react-flow__controls", L, l]), position: f, style: e, "data-testid": "rf__controls", "aria-label": h ?? b["controls.ariaLabel"], children: [t && r.jsxs(r.Fragment, { children: [r.jsx(vo, { onClick: N, className: "react-flow__controls-zoomin", title: b["controls.zoomIn.ariaLabel"], "aria-label": b["controls.zoomIn.ariaLabel"], disabled: w, children: r.jsx(Wv, {}) }), r.jsx(vo, { onClick: S, className: "react-flow__controls-zoomout", title: b["controls.zoomOut.ariaLabel"], "aria-label": b["controls.zoomOut.ariaLabel"], disabled: x, children: r.jsx(Fv, {}) })] }), n && r.jsx(vo, { className: "react-flow__controls-fitview", onClick: k, title: b["controls.fitView.ariaLabel"], "aria-label": b["controls.fitView.ariaLabel"], children: r.jsx(Bv, {}) }), o && r.jsx(vo, { className: "react-flow__controls-interactive", onClick: D, title: b["controls.interactive.ariaLabel"], "aria-label": b["controls.interactive.ariaLabel"], children: m ? r.jsx(Xv, {}) : r.jsx(Kv, {}) }), d] });
}
nd.displayName = "Controls";
const od = Ne(nd);
function qv({ id: e, x: t, y: n, width: o, height: i, style: s, color: a, strokeColor: c, strokeWidth: u, className: l, borderRadius: d, shapeRendering: f, selected: p, onClick: h }) {
  const { background: y, backgroundColor: m } = s || {}, x = a || y || m;
  return r.jsx("rect", { className: Ce(["react-flow__minimap-node", { selected: p }, l]), x: t, y: n, rx: d, ry: d, width: o, height: i, style: {
    fill: x,
    stroke: c,
    strokeWidth: u
  }, shapeRendering: f, onClick: h ? (w) => h(w, e) : void 0 });
}
const Uv = Ne(qv), Zv = (e) => e.nodes.map((t) => t.id), pr = (e) => e instanceof Function ? e : () => e;
function Gv({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: i,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: s = Uv,
  onClick: a
}) {
  const c = pe(Zv, me), u = pr(t), l = pr(e), d = pr(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return r.jsx(r.Fragment, { children: c.map((p) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    r.jsx(Qv, { id: p, nodeColorFunc: u, nodeStrokeColorFunc: l, nodeClassNameFunc: d, nodeBorderRadius: o, nodeStrokeWidth: i, NodeComponent: s, onClick: a, shapeRendering: f }, p)
  )) });
}
function Jv({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: i, nodeStrokeWidth: s, shapeRendering: a, NodeComponent: c, onClick: u }) {
  const { node: l, x: d, y: f, width: p, height: h } = pe((y) => {
    const m = y.nodeLookup.get(e);
    if (!m)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const x = m.internals.userNode, { x: w, y: b } = m.internals.positionAbsolute, { width: g, height: v } = lt(x);
    return {
      node: x,
      x: w,
      y: b,
      width: g,
      height: v
    };
  }, me);
  return !l || l.hidden || !Jl(l) ? null : r.jsx(c, { x: d, y: f, width: p, height: h, style: l.style, selected: !!l.selected, className: o(l), color: t(l), borderRadius: i, strokeColor: n(l), strokeWidth: s, shapeRendering: a, onClick: u, id: l.id });
}
const Qv = Ne(Jv);
var e0 = Ne(Gv);
const t0 = 200, n0 = 150, o0 = (e) => !e.hidden, i0 = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? Zl(Xn(e.nodeLookup, { filter: o0 }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, r0 = "react-flow__minimap-desc";
function id({
  style: e,
  className: t,
  nodeStrokeColor: n,
  nodeColor: o,
  nodeClassName: i = "",
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
  pannable: m = !1,
  zoomable: x = !1,
  ariaLabel: w,
  inversePan: b,
  zoomStep: g = 1,
  offsetScale: v = 5
}) {
  const j = xe(), N = re(null), { boundingRect: S, viewBB: k, rfId: D, panZoom: L, translateExtent: A, flowWidth: _, flowHeight: R, ariaLabelConfig: C } = pe(i0, me), I = e?.width ?? t0, E = e?.height ?? n0, T = S.width / I, $ = S.height / E, P = Math.max(T, $), F = P * I, H = P * E, O = v * P, U = S.x - (F - S.width) / 2 - O, q = S.y - (H - S.height) / 2 - O, ne = F + O * 2, ce = H + O * 2, Z = `${r0}-${D}`, z = re(0), X = re();
  z.current = P, G(() => {
    if (N.current && L)
      return X.current = wx({
        domNode: N.current,
        panZoom: L,
        getTransform: () => j.getState().transform,
        getViewScale: () => z.current
      }), () => {
        X.current?.destroy();
      };
  }, [L]), G(() => {
    X.current?.update({
      translateExtent: A,
      width: _,
      height: R,
      inversePan: b,
      pannable: m,
      zoomStep: g,
      zoomable: x
    });
  }, [m, x, b, g, A, _, R]);
  const le = h ? (te) => {
    const [de, V] = X.current?.pointer(te) || [0, 0];
    h(te, { x: de, y: V });
  } : void 0, ae = y ? ie((te, de) => {
    const V = j.getState().nodeLookup.get(de).internals.userNode;
    y(te, V);
  }, []) : void 0, J = w ?? C["minimap.ariaLabel"];
  return r.jsx(hi, { position: p, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * P : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof a == "number" ? a : void 0
  }, className: Ce(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: r.jsxs("svg", { width: I, height: E, viewBox: `${U} ${q} ${ne} ${ce}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": Z, ref: N, onClick: le, children: [J && r.jsx("title", { id: Z, children: J }), r.jsx(e0, { onClick: ae, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: s, nodeClassName: i, nodeStrokeWidth: a, nodeComponent: c }), r.jsx("path", { className: "react-flow__minimap-mask", d: `M${U - O},${q - O}h${ne + O * 2}v${ce + O * 2}h${-ne - O * 2}z
        M${k.x},${k.y}h${k.width}v${k.height}h${-k.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
id.displayName = "MiniMap";
const rd = Ne(id), s0 = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, a0 = {
  [tn.Line]: "right",
  [tn.Handle]: "bottom-right"
};
function c0({ nodeId: e, position: t, variant: n = tn.Handle, className: o, style: i = void 0, children: s, color: a, minWidth: c = 10, minHeight: u = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: p, autoScale: h = !0, shouldResize: y, onResizeStart: m, onResize: x, onResizeEnd: w }) {
  const b = $u(), g = typeof e == "string" ? e : b, v = xe(), j = re(null), N = n === tn.Handle, S = pe(ie(s0(N && h), [N, h]), me), k = re(null), D = t ?? a0[n];
  G(() => {
    if (!(!j.current || !g))
      return k.current || (k.current = Tx({
        domNode: j.current,
        nodeId: g,
        getStoreItems: () => {
          const { nodeLookup: A, transform: _, snapGrid: R, snapToGrid: C, nodeOrigin: I, domNode: E } = v.getState();
          return {
            nodeLookup: A,
            transform: _,
            snapGrid: R,
            snapToGrid: C,
            nodeOrigin: I,
            paneDomNode: E
          };
        },
        onChange: (A, _) => {
          const { triggerNodeChanges: R, nodeLookup: C, parentLookup: I, nodeOrigin: E } = v.getState(), T = [], $ = { x: A.x, y: A.y }, P = C.get(g);
          if (P && P.expandParent && P.parentId) {
            const F = P.origin ?? E, H = A.width ?? P.measured.width ?? 0, O = A.height ?? P.measured.height ?? 0, U = {
              id: P.id,
              parentId: P.parentId,
              rect: {
                width: H,
                height: O,
                ...Ql({
                  x: A.x ?? P.position.x,
                  y: A.y ?? P.position.y
                }, { width: H, height: O }, P.parentId, C, F)
              }
            }, q = ds([U], C, I, E);
            T.push(...q), $.x = A.x ? Math.max(F[0] * H, A.x) : void 0, $.y = A.y ? Math.max(F[1] * O, A.y) : void 0;
          }
          if ($.x !== void 0 && $.y !== void 0) {
            const F = {
              id: g,
              type: "position",
              position: { ...$ }
            };
            T.push(F);
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
            T.push(H);
          }
          for (const F of _) {
            const H = {
              ...F,
              type: "position"
            };
            T.push(H);
          }
          R(T);
        },
        onEnd: ({ width: A, height: _ }) => {
          const R = {
            id: g,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: A,
              height: _
            }
          };
          v.getState().triggerNodeChanges([R]);
        }
      })), k.current.update({
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
        onResize: x,
        onResizeEnd: w,
        shouldResize: y
      }), () => {
        k.current?.destroy();
      };
  }, [
    D,
    c,
    u,
    l,
    d,
    f,
    m,
    x,
    w,
    y
  ]);
  const L = D.split("-");
  return r.jsx("div", { className: Ce(["react-flow__resize-control", "nodrag", ...L, n, o]), ref: j, style: {
    ...i,
    scale: S,
    ...a && { [N ? "backgroundColor" : "borderColor"]: a }
  }, children: s });
}
Ne(c0);
function l0(e, t) {
  return {
    ...t ? { name: t } : {},
    definitionId: e.definitionId,
    state: On(e.state),
    layout: e.layout
  };
}
function u0(e) {
  return JSON.stringify(
    {
      state: On(e.state),
      layout: e.layout
    },
    null,
    2
  );
}
function d0(e, t) {
  let n;
  try {
    n = JSON.parse(e);
  } catch (i) {
    return { ok: !1, error: i instanceof Error ? i.message : String(i) };
  }
  if (!n || typeof n != "object")
    return { ok: !1, error: "Workflow JSON must be an object with a 'state' property." };
  const o = n;
  return !o.state || typeof o.state != "object" ? { ok: !1, error: "Workflow JSON is missing a valid 'state' object." } : o.layout !== void 0 && !Array.isArray(o.layout) ? { ok: !1, error: "'layout' must be an array when present." } : {
    ok: !0,
    draft: {
      ...t,
      state: ni(o.state),
      layout: o.layout ?? t.layout
    }
  };
}
function f0(e, t) {
  const n = (t ?? "workflow").trim().replace(/[^\w.-]+/g, "-") || "workflow", o = new Blob([JSON.stringify(e, null, 2)], { type: "application/json" }), i = URL.createObjectURL(o), s = document.createElement("a");
  s.href = i, s.download = `${n}.json`, document.body.appendChild(s), s.click(), s.remove(), URL.revokeObjectURL(i);
}
function nc({
  document: e,
  readOnly: t,
  minHeight: n,
  ariaLabel: o,
  onChange: i
}) {
  const s = (a) => {
    t || i({ ...e, value: a.target.value });
  };
  return /* @__PURE__ */ r.jsx(
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
function p0({
  document: e,
  diagnostics: t = [],
  readOnly: n = !1,
  theme: o = "studio",
  minHeight: i = "220px",
  ariaLabel: s,
  languageAdapter: a,
  onChange: c
}) {
  const u = t.filter((p) => !p.uri || p.uri === e.uri), l = a?.displayName ?? e.language, d = a?.loadEditor, f = fe(
    () => d ? Hd(d) : null,
    [d]
  );
  return /* @__PURE__ */ r.jsxs(
    "section",
    {
      className: "studio-code-editor",
      "data-language": e.language,
      "data-theme": o,
      "data-readonly": n,
      children: [
        /* @__PURE__ */ r.jsxs("div", { className: "studio-code-editor-header", children: [
          /* @__PURE__ */ r.jsx("span", { children: l }),
          /* @__PURE__ */ r.jsx("code", { children: e.uri })
        ] }),
        f ? /* @__PURE__ */ r.jsx(Wd, { fallback: /* @__PURE__ */ r.jsx(
          nc,
          {
            document: e,
            readOnly: n,
            minHeight: i,
            ariaLabel: s,
            onChange: c
          }
        ), children: /* @__PURE__ */ r.jsx(
          f,
          {
            document: e,
            readOnly: n,
            theme: o,
            minHeight: i,
            ariaLabel: s,
            onChange: c
          }
        ) }) : /* @__PURE__ */ r.jsx(
          nc,
          {
            document: e,
            readOnly: n,
            minHeight: i,
            ariaLabel: s,
            onChange: c
          }
        ),
        /* @__PURE__ */ r.jsx(h0, { diagnostics: u })
      ]
    }
  );
}
function h0({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ r.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const o = t.severity ?? "info", i = g0(t);
    return /* @__PURE__ */ r.jsxs(
      "p",
      {
        className: `studio-code-editor-diagnostic ${o}`,
        children: [
          t.code ? /* @__PURE__ */ r.jsx("span", { children: t.code }) : null,
          i ? /* @__PURE__ */ r.jsx("small", { children: i }) : null,
          t.message
        ]
      },
      `${t.uri ?? "document"}-${t.code ?? "diagnostic"}-${n}`
    );
  }) });
}
function g0(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const y0 = { language: "json", displayName: "JSON" };
function m0({ draft: e, onApply: t }) {
  const n = fe(() => u0(e), [e]), [o, i] = B(n), [s, a] = B(n), [c, u] = B(null);
  G(() => {
    i(n), a(n), u(null);
  }, [n]);
  const l = o !== s, d = c ? [{ severity: "error", message: c }] : [], f = () => u(t(o));
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-code-view", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-code-view-toolbar", children: [
      /* @__PURE__ */ r.jsx("span", { className: "wf-muted", children: "Edit the workflow definition as JSON. Changes apply to the draft when you click Apply." }),
      /* @__PURE__ */ r.jsxs("span", { className: "wf-code-view-actions", children: [
        /* @__PURE__ */ r.jsx("button", { type: "button", disabled: !l, onClick: () => {
          i(s), u(null);
        }, children: "Reset" }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: !l, onClick: f, children: [
          /* @__PURE__ */ r.jsx(on, { size: 14 }),
          " Apply"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ r.jsx("div", { className: "wf-code-view-editor", children: /* @__PURE__ */ r.jsx(
      p0,
      {
        ariaLabel: "Workflow JSON",
        document: { uri: "elsa://workflows/definition.json", language: "json", value: o },
        languageAdapter: y0,
        diagnostics: d,
        minHeight: "100%",
        theme: "studio",
        onChange: (p) => {
          i(p.value), c && u(null);
        }
      }
    ) })
  ] });
}
function Oe(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function hs(e, t) {
  if (!e || !t) return "";
  const n = Date.parse(e), o = Date.parse(t);
  if (Number.isNaN(n) || Number.isNaN(o) || o < n) return "";
  const i = Math.round((o - n) / 1e3);
  if (i < 60) return `${i}s`;
  const s = Math.floor(i / 60), a = i % 60;
  if (s < 60) return a ? `${s}m ${a}s` : `${s}m`;
  const c = Math.floor(s / 60), u = s % 60;
  return u ? `${c}h ${u}m` : `${c}h`;
}
function It(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function gs(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ r.jsx(Rc, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ r.jsx(Wr, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ r.jsx(Xd, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ r.jsx(Xt, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ r.jsx(Kd, { size: 15 });
    default:
      return /* @__PURE__ */ r.jsx(Vn, { size: 15 });
  }
}
const x0 = ["Single", "Array", "List", "HashSet"];
function sd(e) {
  const [t, n] = B(null), [o, i] = B(null);
  G(() => {
    let u = !1;
    return Op(e).then(
      (l) => {
        u || n(l);
      },
      () => {
        u || n([]);
      }
    ), Wp(e).then(
      (l) => {
        u || i(l);
      },
      () => {
        u || i([]);
      }
    ), () => {
      u = !0;
    };
  }, [e]);
  const s = fe(
    () => t && t.length > 0 ? t.map((u) => {
      const l = Ms(u);
      return {
        value: l,
        label: Kc(u.displayName, l),
        group: u.category?.trim() || "Other"
      };
    }) : null,
    [t]
  ), a = fe(
    () => o && o.length > 0 ? o.filter((u) => !u.deprecated).map((u) => ({
      value: u.typeName,
      label: Nf(u.displayName, u.typeName)
    })) : null,
    [o]
  ), c = fe(() => {
    const u = /* @__PURE__ */ new Map();
    for (const l of t ?? []) {
      const d = Ms(l), f = l.defaultEditor?.trim();
      d && u.set(d, f && f.length > 0 ? f : "text");
    }
    return (l) => u.get(l) ?? "text";
  }, [t]);
  return { typeOptions: s, storageOptions: a, editorForAlias: c };
}
function w0(e) {
  return !e || e.length === 0 ? void 0 : (e.find((n) => /(^|\.)String$/i.test(n.value) || n.label.toLowerCase() === "string") ?? e[0]).value;
}
function v0(e, t, n) {
  return {
    add: () => {
      const o = ff(n.namePrefix, e.map((i) => Sn(i, n.nameKeys)));
      t([...e, n.create(o)]);
    },
    update: (o, i) => t(e.map((s, a) => a === o ? n.patch(s, i) : s)),
    remove: (o) => t(e.filter((i, s) => s !== o))
  };
}
function oc({ value: e, options: t, placeholder: n, allowEmpty: o, ariaLabel: i, onChange: s }) {
  if (!t)
    return /* @__PURE__ */ r.jsx(
      "input",
      {
        type: "text",
        "aria-label": i,
        value: e,
        placeholder: n,
        onChange: (l) => s(l.target.value)
      }
    );
  const a = e === "" || t.some((l) => l.value === e), c = Array.from(new Set(t.map((l) => l.group).filter((l) => !!l))), u = c.length > 0;
  return /* @__PURE__ */ r.jsxs("select", { "aria-label": i, value: e, onChange: (l) => s(l.target.value), children: [
    o ? /* @__PURE__ */ r.jsx("option", { value: "", children: n ?? "—" }) : null,
    a ? null : /* @__PURE__ */ r.jsxs("option", { value: e, disabled: !0, children: [
      e,
      " (unresolved)"
    ] }),
    u ? c.map((l) => /* @__PURE__ */ r.jsx("optgroup", { label: l, children: t.filter((d) => d.group === l).map((d) => /* @__PURE__ */ r.jsx("option", { value: d.value, children: d.label }, d.value)) }, l)) : t.map((l) => /* @__PURE__ */ r.jsx("option", { value: l.value, children: l.label }, l.value))
  ] });
}
const b0 = {
  Single: "Single",
  Array: "Array",
  List: "List",
  HashSet: "Hash set"
};
function N0({ value: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ r.jsx("select", { "aria-label": t, value: e, onChange: (o) => n(o.target.value), children: x0.map((o) => /* @__PURE__ */ r.jsx("option", { value: o, children: b0[o] }, o)) });
}
function j0(e, t) {
  return t === "" ? !0 : e === "checkbox" ? t === "true" || t === "false" || t === "True" || t === "False" : e === "number" ? Number.isFinite(Number(t.trim())) && t.trim() !== "" : e === "date" ? /^\d{4}-\d{2}-\d{2}$/.test(t) : !0;
}
function S0({ value: e, editor: t, ariaLabel: n, onChange: o }) {
  const i = j0(t, e);
  return i && t === "checkbox" ? /* @__PURE__ */ r.jsx(
    "input",
    {
      type: "checkbox",
      "aria-label": n,
      checked: e === "true" || e === "True",
      onChange: (s) => o(s.target.checked ? "true" : "false")
    }
  ) : i && (t === "number" || t === "date") ? /* @__PURE__ */ r.jsx(
    "input",
    {
      type: t,
      "aria-label": n,
      value: e,
      placeholder: "(empty)",
      onChange: (s) => o(s.target.value)
    }
  ) : /* @__PURE__ */ r.jsx(
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
function C0({ title: e, addLabel: t, emptyLabel: n, headers: o, isEmpty: i, onAdd: s, children: a }) {
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-properties-section-head", children: [
      /* @__PURE__ */ r.jsx("h3", { children: e }),
      /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-properties-add", onClick: s, children: [
        /* @__PURE__ */ r.jsx(Yt, { size: 14 }),
        " ",
        t
      ] })
    ] }),
    i ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: n }) : /* @__PURE__ */ r.jsxs("table", { className: "wf-properties-table", children: [
      /* @__PURE__ */ r.jsx("thead", { children: /* @__PURE__ */ r.jsxs("tr", { children: [
        o.map((c) => /* @__PURE__ */ r.jsx("th", { children: c }, c)),
        /* @__PURE__ */ r.jsx("th", { "aria-label": "Actions" })
      ] }) }),
      /* @__PURE__ */ r.jsx("tbody", { children: a })
    ] })
  ] });
}
function E0({ label: e, onRemove: t }) {
  return /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-properties-remove", "aria-label": e, title: e, onClick: t, children: /* @__PURE__ */ r.jsx(In, { size: 14 }) }) });
}
function k0({ checked: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ r.jsx("input", { type: "checkbox", "aria-label": t, checked: e, onChange: (o) => n(o.target.checked) });
}
function ys({
  items: e,
  typeOptions: t,
  storageOptions: n,
  editorForAlias: o,
  namePrefix: i,
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
  const { add: y, update: m, remove: x } = v0(e, h, {
    namePrefix: i,
    nameKeys: s,
    create: (g) => l(g, w0(t)),
    patch: d
  }), w = ["Name", "Type", "Collection", ...f.default ? ["Default"] : [], ...f.storage ? ["Storage"] : [], ...f.required ? ["Required"] : []], b = i.toLowerCase();
  return /* @__PURE__ */ r.jsx(
    C0,
    {
      title: a,
      addLabel: c,
      emptyLabel: u,
      headers: w,
      isEmpty: e.length === 0,
      onAdd: y,
      children: e.map((g, v) => {
        const j = Sn(g, s), N = Bc(g), S = Sn(g, Yc), k = S ? p?.get(S) : void 0, D = N.collectionKind === "Single" ? o(N.alias) : "text";
        return /* @__PURE__ */ r.jsxs("tr", { children: [
          /* @__PURE__ */ r.jsxs("td", { children: [
            /* @__PURE__ */ r.jsx("input", { type: "text", "aria-label": `${i} name`, value: j, onChange: (L) => m(v, { name: L.target.value }) }),
            k ? /* @__PURE__ */ r.jsx("span", { className: "wf-properties-warning", role: "note", title: k, children: k }) : null
          ] }),
          /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            oc,
            {
              ariaLabel: `${i} type`,
              value: N.alias,
              options: t,
              placeholder: "Type",
              onChange: (L) => m(v, { type: { alias: L, collectionKind: N.collectionKind } })
            }
          ) }),
          /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            N0,
            {
              ariaLabel: `${i} collection kind`,
              value: N.collectionKind,
              onChange: (L) => m(v, { type: { alias: N.alias, collectionKind: L } })
            }
          ) }),
          f.default ? /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            S0,
            {
              ariaLabel: `${i} default value`,
              value: yf(g.default),
              editor: D,
              onChange: (L) => m(v, { default: gf(L) })
            }
          ) }) : null,
          f.storage ? /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            oc,
            {
              ariaLabel: `${i} storage driver`,
              value: Sn(g, Sf),
              options: n,
              placeholder: "—",
              allowEmpty: !0,
              onChange: (L) => m(v, { storageDriverType: L || null })
            }
          ) }) : null,
          f.required ? /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            k0,
            {
              ariaLabel: `${i} required`,
              checked: g.isRequired === !0,
              onChange: (L) => m(v, { isRequired: L })
            }
          ) }) : null,
          /* @__PURE__ */ r.jsx(E0, { label: `Remove ${b} ${j || v + 1}`, onRemove: () => x(v) })
        ] }, v);
      })
    }
  );
}
function ad({ items: e, typeOptions: t, storageOptions: n, editorForAlias: o, title: i = "Variables", addLabel: s = "Add variable", emptyLabel: a = "No variables defined.", warnings: c, onChange: u }) {
  return /* @__PURE__ */ r.jsx(
    ys,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: o,
      namePrefix: "Variable",
      nameKeys: jf,
      title: i,
      addLabel: s,
      emptyLabel: a,
      create: (l, d) => pf({ name: l, alias: d }),
      patch: (l, d) => hf(l, d),
      columns: { default: !0, storage: !0 },
      warnings: c,
      onChange: u
    }
  );
}
function I0({ items: e, typeOptions: t, storageOptions: n, editorForAlias: o, onChange: i }) {
  return /* @__PURE__ */ r.jsx(
    ys,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: o,
      namePrefix: "Input",
      nameKeys: Xc,
      title: "Inputs",
      addLabel: "Add input",
      emptyLabel: "No inputs defined.",
      create: (s, a) => mf({ name: s, alias: a }),
      patch: (s, a) => xf(s, a),
      columns: { default: !1, storage: !0, required: !0 },
      onChange: i
    }
  );
}
function A0({ items: e, typeOptions: t, storageOptions: n, editorForAlias: o, onChange: i }) {
  return /* @__PURE__ */ r.jsx(
    ys,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: o,
      namePrefix: "Output",
      nameKeys: Xc,
      title: "Outputs",
      addLabel: "Add output",
      emptyLabel: "No outputs defined.",
      create: (s, a) => wf({ name: s, alias: a }),
      patch: (s, a) => vf(s, a),
      columns: { default: !1, storage: !1 },
      onChange: i
    }
  );
}
function Do(e) {
  return (e ?? []).filter(Lo);
}
function _0({ context: e, variables: t, title: n, addLabel: o, emptyLabel: i, warnings: s, onChange: a }) {
  const { typeOptions: c, storageOptions: u, editorForAlias: l } = sd(e);
  return /* @__PURE__ */ r.jsx(
    ad,
    {
      items: Do(t),
      typeOptions: c,
      storageOptions: u,
      editorForAlias: l,
      title: n,
      addLabel: o,
      emptyLabel: i,
      warnings: s,
      onChange: a
    }
  );
}
function D0({ definition: e, definitionId: t, onMetaChange: n }) {
  const o = !!n, [i, s] = B(e?.name ?? ""), [a, c] = B(e?.description ?? "");
  G(() => {
    s(e?.name ?? "");
  }, [e?.name]), G(() => {
    c(e?.description ?? "");
  }, [e?.description]);
  const u = () => {
    const d = i.trim();
    d && d !== (e?.name ?? "") ? n?.({ name: d }) : d || s(e?.name ?? "");
  }, l = () => {
    a !== (e?.description ?? "") && n?.({ description: a });
  };
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ r.jsx("h3", { children: "Information" }),
    /* @__PURE__ */ r.jsxs("dl", { className: "wf-properties-info", children: [
      /* @__PURE__ */ r.jsx("dt", { children: /* @__PURE__ */ r.jsx("label", { htmlFor: "wf-def-name", children: "Name" }) }),
      /* @__PURE__ */ r.jsx("dd", { children: o ? /* @__PURE__ */ r.jsx(
        "input",
        {
          id: "wf-def-name",
          type: "text",
          "aria-label": "Workflow name",
          value: i,
          onChange: (d) => s(d.target.value),
          onBlur: u
        }
      ) : e?.name ?? "—" }),
      /* @__PURE__ */ r.jsx("dt", { children: /* @__PURE__ */ r.jsx("label", { htmlFor: "wf-def-description", children: "Description" }) }),
      /* @__PURE__ */ r.jsx("dd", { children: o ? /* @__PURE__ */ r.jsx(
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
function T0({ details: e, draft: t, context: n, onStateChange: o, onDefinitionMetaChange: i }) {
  const { typeOptions: s, storageOptions: a, editorForAlias: c } = sd(n), u = Do(t.state.variables), l = Do(t.state.inputs), d = Do(t.state.outputs), f = e?.versions ?? [];
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-properties-view", children: [
    /* @__PURE__ */ r.jsx(
      D0,
      {
        definition: e?.definition,
        definitionId: t.definitionId,
        onMetaChange: i
      }
    ),
    /* @__PURE__ */ r.jsx(
      ad,
      {
        items: u,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => o((h) => ({ ...h, variables: p }))
      }
    ),
    /* @__PURE__ */ r.jsx(
      I0,
      {
        items: l,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => o((h) => ({ ...h, inputs: p }))
      }
    ),
    /* @__PURE__ */ r.jsx(
      A0,
      {
        items: d,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => o((h) => ({ ...h, outputs: p }))
      }
    ),
    /* @__PURE__ */ r.jsxs("section", { className: "wf-properties-section", children: [
      /* @__PURE__ */ r.jsx("h3", { children: "Versions" }),
      f.length === 0 ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No published versions yet." }) : /* @__PURE__ */ r.jsx("ul", { className: "wf-properties-versions", children: f.map((p) => /* @__PURE__ */ r.jsxs("li", { children: [
        /* @__PURE__ */ r.jsxs("span", { className: "wf-properties-version-tag", children: [
          "v",
          p.version
        ] }),
        /* @__PURE__ */ r.jsx("time", { children: Oe(p.createdAt) })
      ] }, p.id)) })
    ] })
  ] });
}
const ic = "application/x-elsa-activity-version-id", $0 = 6, P0 = 1200, M0 = 250, R0 = [10, 25, 50], L0 = 10, rc = "elsa-studio-workflow-palette-width", sc = "elsa-studio-workflow-inspector-width", ac = "elsa-studio-workflow-palette-collapsed", cc = "elsa-studio-workflow-inspector-collapsed", cd = "elsa-studio-workflow-side-panel-maximized", xn = 180, wn = 460, z0 = 260, vn = 260, bn = 560, V0 = 320, lc = 42, bo = 16, ld = Qe.createContext(null), ud = Qe.createContext(null);
function O0(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function dd(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function At(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function _t(e, t, n) {
  const o = t.createPrompt(n);
  return o ? (e.dispatchPrompt(o), !0) : !1;
}
function H0(e) {
  if (!e) return null;
  const t = [], n = e.match(/```(?:json)?\s*([\s\S]*?)```/i);
  n && t.push(n[1]);
  const o = e.match(/\{[\s\S]*?\}/);
  o && t.push(o[0]);
  for (const a of t)
    try {
      const c = JSON.parse(a.trim()), u = uc(c.name, c.description);
      if (u) return u;
    } catch {
    }
  const i = e.match(/name\s*[:-]\s*(.+)/i)?.[1], s = e.match(/description\s*[:-]\s*(.+)/i)?.[1];
  return uc(i, s);
}
function uc(e, t) {
  const n = typeof e == "string" ? dc(e) : void 0, o = typeof t == "string" ? dc(t) : void 0;
  return n || o ? { name: n || void 0, description: o || void 0 } : null;
}
function dc(e) {
  return e.trim().replace(/,$/, "").trim().replace(/^["']/, "").replace(/["']$/, "").trim();
}
function W0(e, t) {
  return e.rootActivityVersionId ?? fd(t, e.rootKind)?.activityVersionId ?? null;
}
function fd(e, t) {
  return e.find((n) => F0(n) === t);
}
function F0(e) {
  return e ? B0(e) ? "flowchart" : K0(e) ? "sequence" : null : null;
}
function $r(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "Uncategorized";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [o]) => n.localeCompare(o)).map(([n, o]) => ({
    category: n,
    activities: o.sort((i, s) => Se(i).localeCompare(Se(s)))
  }));
}
function B0(e) {
  return Se(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function K0(e) {
  return Se(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function X0(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function pd(e) {
  return G0(e.rootActivityType) || e.rootActivityType;
}
function Y0(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function q0(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function U0(e, t) {
  return fc(t) - fc(e);
}
function fc(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function hd(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
function gd(e) {
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
    No(t, n.typeName, n), No(t, n.name, n), No(t, n.displayName, n);
    const o = n.typeName.split(".").filter(Boolean).at(-1);
    No(t, o, n);
  }
  return t;
}
function Q0(e, t, n) {
  const o = t.get(e.activityVersionId);
  return n.get(Nn(o?.activityTypeKey)) ?? n.get(Nn(It(o?.activityTypeKey))) ?? n.get(Nn(o?.displayName)) ?? n.get(Nn(e.activityVersionId)) ?? null;
}
function No(e, t, n) {
  const o = Nn(t);
  o && !e.has(o) && e.set(o, n);
}
function Nn(e) {
  return e?.trim().toLowerCase() ?? "";
}
function pc(e, t, n, o) {
  const i = yi();
  if (!i) return t;
  const s = i.getItem(e);
  if (s == null) return t;
  const a = Number(s);
  return Number.isFinite(a) ? To(a, n, o) : t;
}
function hc(e, t) {
  const n = yi();
  if (!n) return t;
  const o = n.getItem(e);
  return o === "true" ? !0 : o === "false" ? !1 : t;
}
function eb() {
  const e = yi();
  if (!e) return null;
  const t = e.getItem(cd);
  return t === "palette" || t === "inspector" ? t : null;
}
function yi() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function hn(e, t) {
  const n = yi();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function To(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function yd(e) {
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
  const o = t.find((s) => s.activityVersionId === n.activityVersionId);
  if (Xr(n, o) === "unsupported") return /* @__PURE__ */ new Set([n.nodeId]);
  const i = qt(n, [], t);
  return new Set(i?.slot.activities.map((s) => s.nodeId) ?? [n.nodeId]);
}
function gc(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function yc(e) {
  return `${Se(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function mc(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function ob(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function md(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function ib(e) {
  const t = md(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function rb(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function Ve(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function sb(e) {
  return vd(Ve(e));
}
function ab(e, t) {
  const n = t?.get(e.activityVersionId);
  return n ? Se(n) : It(e.activityVersionId) ?? e.activityVersionId;
}
function cb(e, t) {
  if (e.cardinality === "single") {
    const o = e.activities[0];
    return o ? ab(o, t) : "Empty — click to choose";
  }
  const n = e.activities.length;
  return `${n} activit${n === 1 ? "y" : "ies"}`;
}
function xd(e, t, n = []) {
  if (!e) return n;
  const o = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: o?.activityTypeKey ?? e.activityVersionId,
    displayName: o ? Se(o) : void 0
  });
  for (const i of Me(e, t))
    for (const s of i.activities) xd(s, t, n);
  return n;
}
function wd(e, t, n = []) {
  if (!e) return n;
  for (const o of rl(e))
    n.push({ source: o.source, target: o.target, sourcePort: o.sourceHandle ?? void 0, targetPort: o.targetHandle ?? void 0 });
  for (const o of Me(e, t))
    for (const i of o.activities) wd(i, t, n);
  return n;
}
function jn(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function lb(e) {
  return `${e.id}-${vd(JSON.stringify(e.state))}`;
}
function vd(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function ms(e) {
  return e.status.toLowerCase() === "rejected";
}
function ub(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function db(e, t) {
  const n = e instanceof Error ? e.message : String(e);
  return fb(e, n) ? `Run ${t} was not found.` : n;
}
function fb(e, t) {
  if ((typeof e == "object" && e ? e.response?.status ?? e.status : void 0) === 404 || /\b404\b/.test(t)) return !0;
  try {
    const o = JSON.parse(t);
    return [o.error, o.title, o.detail].some((i) => typeof i == "string" && /not found/i.test(i));
  } catch {
    return /not found/i.test(t);
  }
}
function an({ status: e, subStatus: t }) {
  return /* @__PURE__ */ r.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
const bd = { workflowActivity: pb }, Nd = { workflow: gb };
function pb({ data: e, selected: t }) {
  const n = e, o = n.runtime, i = !n.suppressFlowPorts, s = i ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], a = hb(n), u = Qe.useContext(ud)?.({ activityVersionId: n.activityVersionId, activityTypeKey: n.activityTypeKey }) ?? null;
  return /* @__PURE__ */ r.jsxs(
    "div",
    {
      className: ["wf-node", t ? "selected" : "", o ? "wf-node-runtime" : "", o?.hasBlockingIncident ? "faulted" : "", u ? "wf-node-unavailable" : ""].filter(Boolean).join(" "),
      "data-icon": n.icon ?? "activity",
      children: [
        i && n.acceptsInbound ? /* @__PURE__ */ r.jsx(nn, { type: "target", position: oe.Left }) : null,
        u ? /* @__PURE__ */ r.jsx("span", { className: "wf-node-availability", title: `No longer available for new use · ${Ho(u.state)}`, children: /* @__PURE__ */ r.jsx(Po, { size: 13 }) }) : null,
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
        o ? /* @__PURE__ */ r.jsxs("div", { className: "wf-node-runtime-strip", children: [
          o.status ? /* @__PURE__ */ r.jsx(an, { status: o.status, subStatus: o.subStatus }) : null,
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
        s.map((l, d) => {
          const f = `${(d + 1) / (s.length + 1) * 100}%`;
          return /* @__PURE__ */ r.jsxs(Qe.Fragment, { children: [
            /* @__PURE__ */ r.jsx("span", { className: "wf-node-port-label", style: { top: f }, children: l.displayName }),
            /* @__PURE__ */ r.jsx(nn, { type: "source", position: oe.Right, id: l.name, style: { top: f } })
          ] }, l.name);
        })
      ]
    }
  );
}
function hb(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((i) => !!i).join(" · ");
}
function gb(e) {
  const {
    id: t,
    sourceX: n,
    sourceY: o,
    targetX: i,
    targetY: s,
    sourcePosition: a,
    targetPosition: c,
    markerEnd: u,
    style: l,
    label: d,
    labelStyle: f
  } = e, p = Qe.useContext(ld), [h, y] = B(!1), [m, x, w] = Jo({ sourceX: n, sourceY: o, targetX: i, targetY: s, sourcePosition: a, targetPosition: c }), b = p?.highlightedEdgeId === t;
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsx(
      qn,
      {
        id: t,
        path: m,
        markerEnd: u,
        style: {
          ...l,
          strokeWidth: b ? 2.5 : l?.strokeWidth
        },
        label: d,
        labelX: x,
        labelY: w,
        labelStyle: f,
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1)
      }
    ),
    p ? /* @__PURE__ */ r.jsx(Lv, { children: /* @__PURE__ */ r.jsxs(
      "div",
      {
        className: ["wf-edge-actions", h ? "visible" : "", b ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${x}px, ${w}px)` },
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1),
        children: [
          /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (g) => p.requestInsertActivity(t, g.clientX, g.clientY), children: /* @__PURE__ */ r.jsx(Yt, { size: 12 }) }),
          /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => p.deleteEdge(t), children: /* @__PURE__ */ r.jsx(In, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function yb({ clientX: e, clientY: t, activities: n, onPick: o, onClose: i }) {
  const [s, a] = B(""), [c, u] = B(0), l = re(null), d = re(null), f = fe(() => {
    const b = s.trim().toLowerCase(), g = n.filter(X0);
    return b ? g.filter((v) => Se(v).toLowerCase().includes(b) || v.activityTypeKey.toLowerCase().includes(b) || (v.category ?? "").toLowerCase().includes(b) || (v.description ?? "").toLowerCase().includes(b)) : g;
  }, [n, s]), p = fe(() => $r(f), [f]), h = fe(() => p.flatMap((b) => b.activities), [p]);
  G(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), G(() => {
    const b = (v) => {
      l.current?.contains(v.target) || i();
    }, g = (v) => {
      v.key === "Escape" && i();
    };
    return document.addEventListener("mousedown", b, !0), document.addEventListener("keydown", g), () => {
      document.removeEventListener("mousedown", b, !0), document.removeEventListener("keydown", g);
    };
  }, [i]);
  const y = (b) => {
    if (b.key === "ArrowDown")
      b.preventDefault(), u((g) => Math.min(g + 1, h.length - 1));
    else if (b.key === "ArrowUp")
      b.preventDefault(), u((g) => Math.max(g - 1, 0));
    else if (b.key === "Enter") {
      b.preventDefault();
      const g = h[c];
      g && o(g);
    }
  }, m = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), x = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let w = -1;
  return /* @__PURE__ */ r.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: m, top: x }, onMouseDown: (b) => b.stopPropagation(), onClick: (b) => b.stopPropagation(), children: [
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
        w += 1;
        const v = w, j = v === c;
        return /* @__PURE__ */ r.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": j,
            className: j ? "active" : "",
            onMouseEnter: () => u(v),
            onClick: () => o(g),
            children: [
              /* @__PURE__ */ r.jsx("strong", { children: Se(g) }),
              /* @__PURE__ */ r.jsx("small", { children: g.category || g.activityTypeKey })
            ]
          },
          g.activityVersionId
        );
      })
    ] }, b.category)) })
  ] });
}
function $o({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: o
}) {
  const i = of(t.map((s) => s.id), n, o);
  return /* @__PURE__ */ r.jsx("div", { className: "wf-panel-tab-list", role: "tablist", "aria-label": e, onKeyDown: i, children: t.map((s) => {
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
        onClick: () => o(s.id),
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
const mb = "Expressions/UnresolvedVariable";
function xb(e) {
  return String(e.type ?? e.code ?? "");
}
function wb(e) {
  return xb(e) === mb;
}
function vb(e) {
  const t = String(e ?? "").trim().split("/").filter(Boolean), [n, o, ...i] = t, s = n === "$workflow";
  return {
    nodeId: !n || s ? null : n,
    isWorkflowScope: s,
    bag: o === "inputs" || o === "outputs" || o === "variables" ? o : null,
    referenceKey: i.length > 0 ? i.join("/") : null
  };
}
function bb(e) {
  return (e ?? []).filter(wb).map((t) => ({
    error: t,
    path: vb(t.path),
    message: t.message ?? "Variable reference is not visible from this activity's scope."
  }));
}
function Nb({ draft: e, onRepair: t }) {
  const n = e.validationErrors;
  if (!n.length)
    return /* @__PURE__ */ r.jsxs("div", { className: "wf-validation ok", children: [
      /* @__PURE__ */ r.jsx(on, { size: 14 }),
      " No validation errors"
    ] });
  const o = bb(n), i = new Map(o.map((s) => [s.error, s]));
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-validation-summary", children: [
      /* @__PURE__ */ r.jsx(yt, { size: 14 }),
      n.length,
      " validation issue",
      n.length === 1 ? "" : "s",
      o.length > 0 ? /* @__PURE__ */ r.jsxs("span", { className: "wf-validation-variable-count", children: [
        " · ",
        o.length,
        " invalid variable reference",
        o.length === 1 ? "" : "s"
      ] }) : null
    ] }),
    /* @__PURE__ */ r.jsx("ul", { className: "wf-validation-list", children: n.map((s, a) => {
      const c = i.get(s);
      return /* @__PURE__ */ r.jsxs("li", { className: c ? "wf-validation-item repairable" : "wf-validation-item", children: [
        /* @__PURE__ */ r.jsx("span", { className: "wf-validation-message", children: s.message ?? "Validation issue." }),
        c?.path.nodeId ? /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-validation-repair", onClick: () => t(c.path.nodeId), children: [
          /* @__PURE__ */ r.jsx(Yd, { size: 12 }),
          " Repair"
        ] }) : null
      ] }, a);
    }) })
  ] });
}
function jb({
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
        n ? /* @__PURE__ */ r.jsx(yt, { size: 16 }) : /* @__PURE__ */ r.jsx(on, { size: 16 }),
        n ? "Test run rejected" : "Test run dispatched"
      ]
    }
  ) });
}
function Sb({ testRun: e, onOpenRun: t }) {
  if (!e)
    return /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Run the draft to see Runtime Evidence." }) });
  const n = ms(e), o = e.workflowExecutionId;
  return /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ r.jsxs("section", { className: "wf-runtime-card", "data-state": n ? "rejected" : "accepted", children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("span", { children: "Latest Test Run" }),
        /* @__PURE__ */ r.jsx("h3", { children: n ? "Rejected by the server" : "Transient run accepted" })
      ] }),
      /* @__PURE__ */ r.jsx(an, { status: e.status, subStatus: e.commandDispatchStatus ?? void 0 })
    ] }),
    /* @__PURE__ */ r.jsx("p", { children: "Ephemeral - not saved, promoted, or published." }),
    n && e.reason ? /* @__PURE__ */ r.jsxs("div", { className: "wf-runtime-reason", children: [
      /* @__PURE__ */ r.jsx(yt, { size: 14 }),
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
        /* @__PURE__ */ r.jsx("dd", { title: o ?? "None", children: o ? /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => t(o), children: o }) : "None" })
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
        /* @__PURE__ */ r.jsx("dd", { title: e.expiresAt ? Oe(e.expiresAt) : "None", children: e.expiresAt ? Oe(e.expiresAt) : "None" })
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
function ws({ icon: e, title: t, description: n, action: o }) {
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-empty-state", role: "status", children: [
    /* @__PURE__ */ r.jsx("div", { className: "wf-empty-state-icon", "aria-hidden": !0, children: e ?? /* @__PURE__ */ r.jsx(Vn, { size: 22 }) }),
    /* @__PURE__ */ r.jsx("h3", { children: t }),
    n ? /* @__PURE__ */ r.jsx("p", { children: n }) : null,
    o ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty-state-action", children: o }) : null
  ] });
}
function Un({ message: e, title: t = "Something went wrong" }) {
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-error-card", role: "alert", children: [
    /* @__PURE__ */ r.jsx(yt, { size: 18 }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-error-card-body", children: [
      /* @__PURE__ */ r.jsx("strong", { children: t }),
      /* @__PURE__ */ r.jsx("span", { children: e || "Please try again, or check that the Elsa server is reachable." })
    ] })
  ] });
}
function jd({ status: e, run: t, compact: n = !1 }) {
  const o = () => {
    t?.workflowExecutionId && (window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(t.workflowExecutionId)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  };
  return /* @__PURE__ */ r.jsxs("div", { className: `wf-status-line${n ? " compact" : ""}`, children: [
    /* @__PURE__ */ r.jsx(on, { size: n ? 13 : 14 }),
    /* @__PURE__ */ r.jsx("span", { children: e }),
    t?.workflowExecutionId ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: o, children: [
      "Open Run ",
      t.workflowExecutionId
    ] }) : null
  ] });
}
function Kt({ value: e, ariaLabel: t, copiedLabel: n, onCopied: o, onCopyFailed: i }) {
  if (!e) return null;
  const s = async (a) => {
    a.preventDefault(), a.stopPropagation();
    try {
      await Z0(e), o(n);
    } catch {
      i(n);
    }
  };
  return /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (a) => {
    s(a);
  }, children: /* @__PURE__ */ r.jsx(qd, { size: 12 }) });
}
function Cb({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: o }) {
  const [i, s] = B("loading"), [a, c] = B(""), [u, l] = B(""), [d, f] = B(null), [p, h] = B([]), y = n?.trim().toLowerCase() ?? "", m = fe(
    () => y ? p.filter((S) => Y0(S, y)) : p,
    [y, p]
  ), x = fe(
    () => Array.from(new Set(p.flatMap((S) => [
      S.definitionId,
      S.definitionVersionId,
      S.sourceId
    ]).filter((S) => !!S))).sort((S, k) => S.localeCompare(k)),
    [p]
  ), w = At(t, "weaver.workflows.explain-executable"), b = ie(async () => {
    s("loading"), c("");
    try {
      h(await dl(e)), s("ready");
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
      const k = await ul(e, S.artifactId), D = gd(k);
      f({ artifactId: S.artifactId, workflowExecutionId: D }), l(`Started ${S.artifactId}`);
    } catch (k) {
      c(k instanceof Error ? k.message : String(k));
    }
  }, v = (S) => {
    w && _t(t, w, S) && (c(""), f(null), l(`Sent ${S.artifactId} to Weaver`));
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
        /* @__PURE__ */ r.jsx(ei, { size: 14 }),
        /* @__PURE__ */ r.jsx(
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
      /* @__PURE__ */ r.jsx("datalist", { id: "wf-executable-definition-options", children: x.map((S) => /* @__PURE__ */ r.jsx("option", { value: S }, S)) }),
      n ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => o(null), children: [
        /* @__PURE__ */ r.jsx(Lc, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    i === "failed" ? /* @__PURE__ */ r.jsx(Un, { message: a }) : null,
    u ? /* @__PURE__ */ r.jsx(jd, { status: u, run: d }) : null,
    i === "loading" ? /* @__PURE__ */ r.jsx(xs, {}) : null,
    i === "ready" && m.length === 0 ? /* @__PURE__ */ r.jsx(
      ws,
      {
        icon: /* @__PURE__ */ r.jsx(Xt, { size: 22 }),
        title: "No workflow executables",
        description: n ? "No executables match this definition filter." : "Publish a workflow definition to make it executable."
      }
    ) : null,
    i === "ready" && m.length > 0 ? /* @__PURE__ */ r.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
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
            /* @__PURE__ */ r.jsx(Kt, { value: S.artifactId, ariaLabel: `Copy artifact ID ${S.artifactId}`, copiedLabel: "artifact ID", onCopied: j, onCopyFailed: N })
          ] }),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
            /* @__PURE__ */ r.jsx("small", { title: S.artifactHash, children: S.artifactHash }),
            /* @__PURE__ */ r.jsx(Kt, { value: S.artifactHash, ariaLabel: `Copy artifact hash ${S.artifactHash}`, copiedLabel: "artifact hash", onCopied: j, onCopyFailed: N })
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line wf-version-cell", children: [
          /* @__PURE__ */ r.jsx("span", { children: S.artifactVersion }),
          /* @__PURE__ */ r.jsx(Kt, { value: S.artifactVersion, ariaLabel: `Copy artifact version ${S.artifactVersion}`, copiedLabel: "artifact version", onCopied: j, onCopyFailed: N })
        ] }),
        /* @__PURE__ */ r.jsx(Eb, { executable: S, onCopied: j, onCopyFailed: N }),
        /* @__PURE__ */ r.jsx("span", { children: pd(S) }),
        /* @__PURE__ */ r.jsx("span", { children: Oe(S.publishedAt ?? S.createdAt) }),
        /* @__PURE__ */ r.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
            g(S);
          }, children: [
            /* @__PURE__ */ r.jsx(Xt, { size: 13 }),
            " Run"
          ] }),
          w ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => v(S), children: [
            /* @__PURE__ */ r.jsx(at, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, S.artifactId))
    ] }) : null
  ] });
}
function Eb({ executable: e, onCopied: t, onCopyFailed: n }) {
  const o = e.sourceId || e.definitionVersionId || e.definitionId, i = e.sourceVersion;
  return /* @__PURE__ */ r.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ r.jsx("span", { className: "wf-source-kind", children: hd(e.sourceKind) }),
    o ? /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line", children: [
      /* @__PURE__ */ r.jsx("code", { title: o, children: o }),
      /* @__PURE__ */ r.jsx(Kt, { value: o, ariaLabel: `Copy source ID ${o}`, copiedLabel: "source ID", onCopied: t, onCopyFailed: n })
    ] }) : null,
    i ? /* @__PURE__ */ r.jsxs("small", { children: [
      "Version ",
      i
    ] }) : null
  ] });
}
function kb({ context: e, ai: t, definitionId: n, publishedArtifactId: o }) {
  const [i, s] = B("loading"), [a, c] = B(""), [u, l] = B(""), [d, f] = B(null), [p, h] = B([]), y = At(t, "weaver.workflows.explain-executable"), m = ie(async () => {
    s("loading"), c("");
    try {
      const j = await dl(e);
      h(j.filter((N) => q0(N, n)).sort(U0)), s("ready");
    } catch (j) {
      c(j instanceof Error ? j.message : String(j)), h([]), s("failed");
    }
  }, [e, n]);
  G(() => {
    m();
  }, [m, o]);
  const x = async (j) => {
    l(""), f(null), c("");
    try {
      const N = await ul(e, j.artifactId);
      f({ artifactId: j.artifactId, workflowExecutionId: gd(N) }), l(`Started ${j.artifactId}`);
    } catch (N) {
      c(N instanceof Error ? N.message : String(N));
    }
  }, w = (j) => {
    y && _t(t, y, j) && (c(""), f(null), l(`Sent ${j.artifactId} to Weaver`));
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
        m();
      }, children: [
        /* @__PURE__ */ r.jsx(Fr, { size: 13 }),
        " Refresh"
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: b, children: "Open list" })
    ] }),
    i === "failed" ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ r.jsx(yt, { size: 14 }),
      " ",
      a
    ] }) : null,
    u ? /* @__PURE__ */ r.jsx(jd, { status: u, run: d, compact: !0 }) : null,
    i === "loading" ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Loading artifacts..." }) : null,
    i === "ready" && p.length === 0 ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No published artifacts for this workflow yet." }) : null,
    i === "ready" && p.length > 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-artifact-list", role: "list", "aria-label": "Workflow artifacts", children: p.map((j) => /* @__PURE__ */ r.jsxs("article", { className: "wf-artifact-card", role: "listitem", "data-active": j.artifactId === o ? "true" : void 0, children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-artifact-card-heading", children: [
        /* @__PURE__ */ r.jsxs("div", { children: [
          /* @__PURE__ */ r.jsxs("span", { className: "wf-artifact-version", children: [
            "Version ",
            j.artifactVersion
          ] }),
          j.artifactId === o ? /* @__PURE__ */ r.jsx("span", { className: "wf-chip", children: "Latest publish" }) : null
        ] }),
        /* @__PURE__ */ r.jsx("span", { children: Oe(j.publishedAt ?? j.createdAt) })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "wf-artifact-card-values", children: [
        /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line", children: [
          /* @__PURE__ */ r.jsx("code", { title: j.artifactId, children: j.artifactId }),
          /* @__PURE__ */ r.jsx(Kt, { value: j.artifactId, ariaLabel: `Copy artifact ID ${j.artifactId}`, copiedLabel: "artifact ID", onCopied: g, onCopyFailed: v })
        ] }),
        /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ r.jsx("code", { title: j.artifactHash, children: j.artifactHash }),
          /* @__PURE__ */ r.jsx(Kt, { value: j.artifactHash, ariaLabel: `Copy artifact hash ${j.artifactHash}`, copiedLabel: "artifact hash", onCopied: g, onCopyFailed: v })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("dl", { children: [
        /* @__PURE__ */ r.jsxs("div", { children: [
          /* @__PURE__ */ r.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ r.jsxs("dd", { children: [
            hd(j.sourceKind),
            " ",
            j.sourceVersion ? `v${j.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("div", { children: [
          /* @__PURE__ */ r.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ r.jsx("dd", { children: pd(j) })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
          x(j);
        }, children: [
          /* @__PURE__ */ r.jsx(Xt, { size: 13 }),
          " Run"
        ] }),
        y ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => w(j), children: [
          /* @__PURE__ */ r.jsx(at, { size: 13 }),
          " Explain"
        ] }) : null
      ] })
    ] }, j.artifactId)) }) : null
  ] });
}
function Ib() {
  const [e, t] = B(() => pc(rc, z0, xn, wn)), [n, o] = B(() => pc(sc, V0, vn, bn)), [i, s] = B(() => hc(ac, !1)), [a, c] = B(() => hc(cc, !1)), [u, l] = B(eb);
  G(() => {
    hn(rc, String(e));
  }, [e]), G(() => {
    hn(sc, String(n));
  }, [n]), G(() => {
    hn(ac, String(i));
  }, [i]), G(() => {
    hn(cc, String(a));
  }, [a]), G(() => {
    hn(cd, u);
  }, [u]), G(() => {
    if (!u) return;
    const g = (v) => {
      v.key === "Escape" && l(null);
    };
    return window.addEventListener("keydown", g), () => window.removeEventListener("keydown", g);
  }, [u]);
  const d = ie((g) => {
    l((v) => v === g ? null : v), g === "palette" ? s((v) => !v) : c((v) => !v);
  }, []), f = ie((g) => {
    g === "palette" ? s(!1) : c(!1), l((v) => v === g ? null : g);
  }, []), p = ie((g, v) => {
    l(null), g === "palette" ? (s(!1), t((j) => To(j + v, xn, wn))) : (c(!1), o((j) => To(j + v, vn, bn)));
  }, []), h = ie((g, v) => {
    v.preventDefault(), l(null), g === "palette" ? s(!1) : c(!1);
    const j = v.clientX, N = g === "palette" ? e : n, S = g === "palette" ? xn : vn, k = g === "palette" ? wn : bn;
    document.body.classList.add("wf-side-panel-resizing");
    const D = (A) => {
      const _ = g === "palette" ? A.clientX - j : j - A.clientX, R = To(N + _, S, k);
      g === "palette" ? t(R) : o(R);
    }, L = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", D), window.removeEventListener("pointerup", L), window.removeEventListener("pointercancel", L);
    };
    window.addEventListener("pointermove", D), window.addEventListener("pointerup", L), window.addEventListener("pointercancel", L);
  }, [n, e]), y = ie((g, v) => {
    v.key === "ArrowLeft" ? (v.preventDefault(), p(g, g === "palette" ? -bo : bo)) : v.key === "ArrowRight" ? (v.preventDefault(), p(g, g === "palette" ? bo : -bo)) : v.key === "Home" ? (v.preventDefault(), g === "palette" ? t(xn) : o(vn)) : v.key === "End" && (v.preventDefault(), g === "palette" ? t(wn) : o(bn));
  }, [p]), m = !i && u !== "inspector", x = !a && u !== "palette", w = [
    "wf-editor-body",
    i ? "palette-collapsed" : "",
    a ? "inspector-collapsed" : "",
    u === "palette" ? "palette-maximized" : "",
    u === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), b = {
    "--wf-palette-width": `${i ? lc : e}px`,
    "--wf-inspector-width": `${a ? lc : n}px`
  };
  return {
    paletteWidth: e,
    inspectorWidth: n,
    paletteCollapsed: i,
    inspectorCollapsed: a,
    maximizedSidePanel: u,
    setInspectorCollapsed: c,
    paletteExpanded: m,
    inspectorExpanded: x,
    editorBodyClassName: w,
    editorBodyStyle: b,
    toggleSidePanelCollapsed: d,
    toggleSidePanelMaximized: f,
    startSidePanelResize: h,
    handleSidePanelResizeKeyDown: y
  };
}
const Ab = 50;
function vc() {
  return { past: [], future: [] };
}
function _b(e) {
  return e.past.length > 0;
}
function Db(e) {
  return e.future.length > 0;
}
function bc(e, t, n = Ab) {
  const o = [...e.past, t];
  return o.length > n && o.splice(0, o.length - n), { past: o, future: [] };
}
function Tb(e, t) {
  if (e.past.length === 0) return null;
  const n = e.past.slice(), o = n.pop();
  return { history: { past: n, future: [...e.future, t] }, snapshot: o };
}
function $b(e, t) {
  if (e.future.length === 0) return null;
  const n = e.future.slice(), o = n.pop();
  return { history: { past: [...e.past, t], future: n }, snapshot: o };
}
function Pb({ draft: e, restoreDraft: t }) {
  const n = re(vc()), o = re(null), i = re(""), s = re(!1), [a, c] = B(0), u = ie((m) => {
    n.current = vc(), o.current = m ? jn(m) : null, i.current = m ? Ve(m) : "", s.current = !1, c(0);
  }, []);
  G(() => {
    if (!e) return;
    if (s.current) {
      s.current = !1;
      return;
    }
    const m = Ve(e);
    if (m === i.current) return;
    const x = window.setTimeout(() => {
      const w = o.current;
      w && (n.current = bc(n.current, w), c((b) => b + 1)), o.current = jn(e), i.current = m;
    }, M0);
    return () => window.clearTimeout(x);
  }, [e]);
  const l = ie(() => {
    if (!e) return;
    const m = Ve(e);
    if (m === i.current) return;
    const x = o.current;
    x && (n.current = bc(n.current, x)), o.current = jn(e), i.current = m;
  }, [e]), d = ie((m) => {
    s.current = !0, o.current = jn(m), i.current = Ve(m), t(m), c((x) => x + 1);
  }, [t]), f = ie(() => {
    if (!e) return;
    l();
    const m = Tb(n.current, e);
    m && (n.current = m.history, d(m.snapshot));
  }, [e, l, d]), p = ie(() => {
    if (!e) return;
    l();
    const m = $b(n.current, e);
    m && (n.current = m.history, d(m.snapshot));
  }, [e, l, d]), { canUndoNow: h, canRedoNow: y } = fe(() => {
    const m = !!e && !!o.current && Ve(e) !== i.current;
    return {
      canUndoNow: _b(n.current) || m,
      canRedoNow: Db(n.current) && !m
    };
  }, [e, a]);
  return { resetHistory: u, undo: f, redo: p, canUndoNow: h, canRedoNow: y };
}
const Mb = {
  draft: null,
  frames: [],
  selectedNodeId: null,
  testRun: null,
  publishedArtifactId: null
};
function Rb(e, t) {
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
function Lb() {
  const [e, t] = Fd(Rb, Mb), n = fe(() => ({
    // Load a freshly fetched / applied draft (or clear it). Used by load(), JSON apply, and undo/redo.
    loadDraft(o) {
      t({ type: "draftLoaded", draft: o });
    },
    // Replace the draft from a Weaver batch apply/undo, invalidating scope, selection, run and artifact.
    replaceDraftByBatch(o, i) {
      t({ type: "draftReplacedByBatch", draft: o, selectedNodeId: i });
    },
    // In-place draft edit that keeps scope + selection.
    editDraft(o) {
      t({ type: "draftEdited", recipe: o });
    },
    // Draft edit that also selects a node (add/wrap root, add scoped activity).
    editDraftAndSelect(o, i) {
      t({ type: "draftEditedAndSelected", recipe: o, selectedNodeId: i });
    },
    select(o) {
      t({ type: "selectionChanged", selectedNodeId: o });
    },
    // Jump to an explicit breadcrumb path (breadcrumb clicks, variable-repair navigation).
    navigateToScope(o, i = null) {
      t({ type: "scopeNavigated", frames: o, selectedNodeId: i });
    },
    resetToRoot() {
      t({ type: "scopeNavigated", frames: [], selectedNodeId: null });
    },
    enterSlot(o, i, s) {
      t({ type: "slotEntered", frame: { ownerNodeId: o.nodeId, slotId: i, label: s } });
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
const zb = 320, Vb = 140;
function Ob(e, t, n) {
  return n === "sequence" ? Hb(e) : Wb(e, t);
}
function Hb(e) {
  const t = /* @__PURE__ */ new Map();
  return e.forEach((n, o) => {
    t.set(n.id, { x: o * 280, y: 0 });
  }), t;
}
function Wb(e, t) {
  const n = /* @__PURE__ */ new Map();
  if (e.length === 0) return n;
  const o = new Set(e.map((d) => d.id)), i = t.filter((d) => o.has(d.source) && o.has(d.target)), s = /* @__PURE__ */ new Set();
  for (const d of i)
    s.add(d.source), s.add(d.target);
  const a = /* @__PURE__ */ new Map();
  for (const d of e) a.set(d.id, 0);
  for (let d = 0; d < e.length; d += 1) {
    let f = !1;
    for (const p of i) {
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
      n.set(p, { x: d * zb, y: h * Vb });
    });
  return n;
}
function Fb(e, t, n, o, i) {
  if (!e) return { kind: "becomeRoot" };
  const s = qt(e, t, i);
  if (!s)
    return t.length > 0 ? { kind: "staleFrames" } : Me(n, o)[0] ? { kind: "wrapRoot" } : { kind: "leafError" };
  const a = s.slot.cardinality === "single" ? s.slot.activities.find((c) => c.nodeId !== n.nodeId) ?? null : null;
  return { kind: "addToSlot", slot: s.slot, replacedActivity: a };
}
function Bb({
  draft: e,
  scope: t,
  scopeOwner: n,
  frames: o,
  catalog: i,
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
  setError: m
}) {
  const [x, w] = B([]), [b, g] = B([]), [v, j] = B(null), [N, S] = B(null), [k, D] = B(null), L = re(null), A = re(null), _ = re(null), R = re(null), C = re(!1);
  G(() => {
    if (!n) {
      w([]), g([]);
      return;
    }
    const M = a ? xr(n, i, e?.layout ?? []) : t ? el(t, i, e?.layout ?? []) : { nodes: [], edges: [] };
    w(M.nodes), g(M.edges);
  }, [i, e?.layout, a, t, n]);
  const I = ie((M, W, K) => K ? [
    ...M.filter((ee) => ee.nodeId !== W),
    { nodeId: W, x: Math.round(K.x), y: Math.round(K.y) }
  ] : M, []), E = ie((M, W) => {
    if (e?.state.rootActivity && a)
      return;
    const K = wr(M, yc(M)), ee = Fb(e?.state.rootActivity, o, K, M, s);
    if (ee.kind === "becomeRoot") {
      f(
        ({ draft: se }) => se ? { ...se, state: { ...se.state, rootActivity: K } } : null,
        K.nodeId
      );
      return;
    }
    if (ee.kind === "leafError") {
      y(""), m("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
      return;
    }
    if (ee.kind === "staleFrames") {
      y(""), m("This slot could not be resolved — returning to the workflow root."), h();
      return;
    }
    if (ee.kind === "wrapRoot") {
      f(({ draft: se }) => {
        const ye = se?.state.rootActivity;
        return ye ? {
          ...se,
          layout: I(se.layout, ye.nodeId, W),
          state: { ...se.state, rootActivity: Vs(K, [], [ye], M) }
        } : null;
      }, e?.state.rootActivity?.nodeId ?? null), m(""), y(`Wrapped root in ${Se(M)}`);
      return;
    }
    f(({ draft: se, frames: ye }) => {
      if (!se?.state.rootActivity) return null;
      const ge = qt(se.state.rootActivity, ye, s);
      if (!ge) return null;
      const Ee = ge.slot.cardinality === "single" ? [K] : [...ge.slot.activities, K], _e = Vs(se.state.rootActivity, ye, Ee, s);
      return {
        ...se,
        layout: I(se.layout, K.nodeId, W),
        state: { ...se.state, rootActivity: _e }
      };
    }, K.nodeId), ee.replacedActivity && (m(""), y(`Replaced ${ee.slot.label} content`));
  }, [s, e?.state.rootActivity, o, a, f, h, I, m, y]), T = ie((M, W) => {
    const K = wr(M, yc(M)), ee = {
      id: K.nodeId,
      type: "workflowActivity",
      position: W,
      selected: !0,
      data: {
        label: Se(M),
        activityVersionId: M.activityVersionId,
        activityTypeKey: M.activityTypeKey,
        category: M.category,
        executionType: M.executionType,
        icon: oi(M),
        childSlots: Me(K, M),
        acceptsInbound: String(M.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: sl(K, M)
      }
    };
    return { activityNode: K, node: ee };
  }, []), $ = ie((M, W, K = []) => {
    a || d(({ draft: ee, frames: se }) => {
      if (!ee) return null;
      const ye = Yf(ee.layout, M), ge = ee.state.rootActivity;
      if (!ge) return { ...ee, layout: ye };
      const Ee = qt(ge, se, s);
      if (!Ee) return { ...ee, layout: ye };
      const _e = Kf(Ee, M, W, K), be = Ee.slot.mode === "flowchart" ? Xf(_e, W) : _e;
      return {
        ...ee,
        layout: ye,
        state: {
          ...ee.state,
          rootActivity: Bf(ge, se, be, s)
        }
      };
    });
  }, [s, a, d]), P = ie((M, W) => {
    if (!L.current) return null;
    const K = L.current.getBoundingClientRect();
    return v ? v.screenToFlowPosition({ x: M, y: W }) : {
      x: M - K.left,
      y: W - K.top
    };
  }, [v]), F = ie((M, W) => document.elementFromPoint(M, W)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), H = ie((M, W, K) => {
    const ee = x.find((ke) => ke.id === W.source), se = x.find((ke) => ke.id === W.target), ye = ee && se ? ob(ee, se) : ee ? mc(ee) : K, ge = T(M, ye), _e = [...x.map((ke) => ke.selected ? { ...ke, selected: !1 } : ke), ge.node], be = tp(b, W, ge.node.id);
    w(_e), g(be), p(ge.node.id), $(_e, be, [ge.activityNode]);
  }, [$, T, b, x, p]), O = ie((M, W, K) => {
    if (!u || !L.current) return !1;
    const ee = L.current.getBoundingClientRect();
    if (!(W >= ee.left && W <= ee.right && K >= ee.top && K <= ee.bottom)) return !1;
    const ye = P(W, K);
    if (!ye) return !1;
    if (c) {
      const ge = F(W, K), Ee = ge ? b.find((_e) => _e.id === ge) : void 0;
      if (Ee)
        return H(M, Ee, ye), !0;
    }
    return E(M, ye), !0;
  }, [E, u, b, F, c, H, P]);
  G(() => {
    const M = (K) => {
      const ee = _.current;
      if (!ee) return;
      Math.hypot(K.clientX - ee.startX, K.clientY - ee.startY) >= $0 && (ee.dragging = !0);
    }, W = (K) => {
      const ee = _.current;
      if (_.current = null, !ee?.dragging || !L.current || R.current) return;
      const se = L.current.getBoundingClientRect();
      K.clientX >= se.left && K.clientX <= se.right && K.clientY >= se.top && K.clientY <= se.bottom && (C.current = !0, window.setTimeout(() => {
        C.current = !1;
      }, 0), O(ee.activity, K.clientX, K.clientY));
    };
    return window.addEventListener("pointermove", M), window.addEventListener("pointerup", W), window.addEventListener("pointercancel", W), () => {
      window.removeEventListener("pointermove", M), window.removeEventListener("pointerup", W), window.removeEventListener("pointercancel", W);
    };
  }, [v, O]);
  const U = (M, W) => {
    R.current = { activityVersionId: W.activityVersionId, handledDrop: !1 }, M.dataTransfer.setData(ic, W.activityVersionId), M.dataTransfer.setData("text/plain", W.activityVersionId), M.dataTransfer.effectAllowed = "copy";
  }, q = (M, W) => {
    const K = R.current;
    R.current = null, !K?.handledDrop && (M.clientX === 0 && M.clientY === 0 || O(W, M.clientX, M.clientY) && (C.current = !0, window.setTimeout(() => {
      C.current = !1;
    }, 0)));
  }, ne = (M, W) => {
    M.button === 0 && (_.current = {
      activity: W,
      startX: M.clientX,
      startY: M.clientY,
      dragging: !1
    });
  }, ce = (M) => {
    C.current || u && E(M);
  }, Z = (M) => {
    if (!u) {
      M.dataTransfer.dropEffect = "none";
      return;
    }
    if (M.preventDefault(), M.dataTransfer.dropEffect = "copy", !c) return;
    const W = F(M.clientX, M.clientY);
    D(W);
  }, z = (M) => {
    if (!L.current) return;
    const W = M.relatedTarget;
    W && L.current.contains(W) || D(null);
  }, X = (M) => {
    M.preventDefault(), D(null);
    const W = M.dataTransfer.getData(ic) || M.dataTransfer.getData("text/plain");
    if (!W || (M.stopPropagation(), R.current?.activityVersionId === W && (R.current.handledDrop = !0), !u)) return;
    const K = s.get(W);
    K && O(K, M.clientX, M.clientY);
  }, le = (M) => {
    if (!u) return;
    if (M) {
      S({ kind: "fromEmpty", clientX: M.clientX, clientY: M.clientY });
      return;
    }
    const W = L.current?.getBoundingClientRect();
    W && S({
      kind: "fromEmpty",
      clientX: W.left + W.width / 2,
      clientY: W.top + W.height / 2
    });
  }, ae = (M) => {
    const W = a ? M.filter((K) => K.type === "select") : M;
    W.length !== 0 && w((K) => Cu(W, K));
  }, J = (M) => {
    a || g((W) => Eu(M, W));
  }, te = (M) => !M.source || !M.target || M.source === M.target || !c ? !1 : !M.targetHandle, de = (M) => {
    if (!e?.state.rootActivity || !t || !c || !te(M)) return;
    const W = zo(M.source, M.target, M.sourceHandle ?? "Done", M.targetHandle ?? void 0), K = Iu(W, b);
    g(K), $(x, K);
  }, V = () => {
    $(x, b);
  }, Q = !a && x.length > 0, he = ie(() => {
    if (a || x.length === 0) return;
    const M = t?.slot.mode === "sequence" ? "sequence" : "flowchart", W = Ob(x, b, M), K = x.map((ee) => {
      const se = W.get(ee.id);
      return se ? { ...ee, position: se } : ee;
    });
    w(K), $(K, b), window.requestAnimationFrame(() => v?.fitView({ padding: 0.2 })), y("Rearranged the canvas.");
  }, [b, x, t, a, $, v, y]), we = (M, W) => {
    if (!W.nodeId || W.handleType === "target") {
      A.current = null;
      return;
    }
    A.current = {
      nodeId: W.nodeId,
      handleId: W.handleId ?? null
    };
  }, Ie = (M, W) => {
    const K = rb(A.current, W);
    if (A.current = null, !K || !c || W.toNode || W.toHandle || ib(M)) return;
    const ee = md(M);
    S({
      kind: "fromPort",
      sourceNodeId: K.nodeId,
      sourceHandleId: K.handleId,
      clientX: ee.x,
      clientY: ee.y
    });
  }, Ae = (M, W) => {
    if (!c || !te(W)) return;
    const K = hw(M, {
      ...W,
      sourceHandle: W.sourceHandle ?? "Done",
      // Connection.targetHandle is string | null; null and undefined are indistinguishable downstream
      // (every consumer checks truthiness), so keep the type-correct null.
      targetHandle: W.targetHandle ?? null
    }, b, { shouldReplaceId: !1 });
    g(K), $(x, K);
  }, Re = (M) => {
    if (a || M.length === 0) return;
    const W = new Set(M.map((se) => se.id)), K = x.filter((se) => !W.has(se.id)), ee = b.filter((se) => !W.has(se.source) && !W.has(se.target));
    w(K), g(ee), l && W.has(l) && p(null), $(K, ee);
  }, ot = (M) => {
    if (a || M.length === 0) return;
    const W = new Set(M.map((ee) => ee.id)), K = b.filter((ee) => !W.has(ee.id));
    g(K), $(x, K);
  }, Fe = ie((M) => {
    if (a) return;
    const W = b.filter((K) => K.id !== M);
    g(W), $(x, W);
  }, [$, b, a, x]), Be = ie((M, W, K) => {
    c && S({ kind: "spliceEdge", edgeId: M, clientX: W, clientY: K });
  }, [c]), Te = (M) => {
    const W = N;
    if (!W) return;
    S(null);
    const K = P(W.clientX, W.clientY) ?? { x: 0, y: 0 };
    if (W.kind === "fromEmpty") {
      const se = T(M, K), ge = [...x.map((Ee) => Ee.selected ? { ...Ee, selected: !1 } : Ee), se.node];
      w(ge), p(se.node.id), $(ge, b, [se.activityNode]);
      return;
    }
    if (W.kind === "fromPort") {
      const se = x.find((ke) => ke.id === W.sourceNodeId), ye = se ? mc(se) : K, ge = T(M, ye), _e = [...x.map((ke) => ke.selected ? { ...ke, selected: !1 } : ke), ge.node], be = [...b, zo(W.sourceNodeId, ge.node.id, W.sourceHandleId ?? "Done")];
      w(_e), g(be), p(ge.node.id), $(_e, be, [ge.activityNode]);
      return;
    }
    const ee = b.find((se) => se.id === W.edgeId);
    ee && H(M, ee, K);
  }, Le = fe(() => ({
    highlightedEdgeId: k,
    deleteEdge: Fe,
    requestInsertActivity: Be
  }), [Fe, k, Be]);
  return {
    nodes: x,
    edges: b,
    canvasRef: L,
    setReactFlowInstance: j,
    connectMenu: N,
    setConnectMenu: S,
    edgeActions: Le,
    onNodesChange: ae,
    onEdgesChange: J,
    onNodesDelete: Re,
    onEdgesDelete: ot,
    isValidConnection: te,
    onConnect: de,
    onConnectStart: we,
    onConnectEnd: Ie,
    onReconnect: Ae,
    commitLayout: V,
    canAutoLayout: Q,
    autoLayout: he,
    onCanvasDragOver: Z,
    onCanvasDragLeave: z,
    onCanvasDrop: X,
    openEmptyConnectMenu: le,
    onConnectMenuPick: Te,
    addActivity: E,
    onPaletteClick: ce,
    onPaletteDragStart: U,
    onPaletteDragEnd: q,
    onPalettePointerDown: ne
  };
}
const Kb = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function Sd(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function vs(e) {
  return Sd(e.name);
}
function Xb(e, t) {
  const n = vs(t), o = e[n];
  return t.isWrapped === !1 ? o ?? t.defaultValue ?? "" : Ed(o, t);
}
function Cd(e, t) {
  return Ed(e[vs(t)], t);
}
function Yb(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function qb(e, t) {
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
function Ub(e, t) {
  return t.isWrapped === !1 ? Xb(e, t) : Cd(e, t).expression.value;
}
function Ed(e, t) {
  return oN(e) ? {
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
const Zb = /* @__PURE__ */ new Set([
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
function Gb(e) {
  const t = e?.trim();
  if (!t) return null;
  const n = /^([\w.+]+)\[\]/.exec(t);
  if (n) return { elementTypeName: n[1] };
  const o = t.indexOf("`");
  if (o < 0) return null;
  const i = t.slice(0, o), s = (i.split(".").pop() ?? i).toLowerCase();
  return Zb.has(s) ? { elementTypeName: Jb(t.slice(o)) } : null;
}
function Jb(e) {
  const t = /\[\[([\w.+]+)/.exec(e);
  if (t) return t[1];
  const n = /\[([\w.+]+)/.exec(e);
  return n ? n[1] : null;
}
function Qb(e) {
  const t = e.uiHint?.toLowerCase();
  return t === "json" || t === "code" ? !0 : e.uiSpecifications?.repeater === !1;
}
function eN(e) {
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
function tN(e) {
  const t = (e ?? "").toLowerCase();
  return t === "system.boolean" || t === "boolean" || t === "bool" ? !1 : "";
}
function nN(e, t) {
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
  const o = [...e], [i] = o.splice(t, 1);
  return o.splice(n, 0, i), o;
}
function oN(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
function kd(e) {
  return bs(e?.trim() ?? "") || e;
}
function bs(e) {
  if (!e) return "";
  const t = iN(e);
  if (!t) return "";
  const n = /^(.*)((?:\[\])+)$/.exec(t);
  if (n) return `${bs(n[1])}${n[2]}`;
  const o = t.indexOf("`");
  if (o >= 0) {
    const i = jc(t.slice(0, o)), s = rN(t.slice(o));
    return s.length > 0 ? `${i}<${s.join(", ")}>` : i;
  }
  return jc(t);
}
function iN(e) {
  let t = 0;
  for (let n = 0; n < e.length; n++) {
    const o = e[n];
    if (o === "[") t++;
    else if (o === "]") t--;
    else if (o === "," && t === 0) return e.slice(0, n).trim();
  }
  return e.trim();
}
function jc(e) {
  const t = e.split(".").filter(Boolean).at(-1) ?? e;
  return t.split("+").filter(Boolean).at(-1) ?? t;
}
function rN(e) {
  const t = e.indexOf("[");
  if (t < 0) return [];
  const n = Sc(e, t);
  return n == null ? [] : sN(n).map((o) => {
    const i = o.trim(), s = i.startsWith("[") ? Sc(i, 0) ?? i : i;
    return bs(s);
  }).filter(Boolean);
}
function Sc(e, t) {
  let n = 0;
  for (let o = t; o < e.length; o++)
    if (e[o] === "[") n++;
    else if (e[o] === "]" && --n === 0) return e.slice(t + 1, o);
  return null;
}
function sN(e) {
  const t = [];
  let n = 0, o = 0;
  for (let i = 0; i < e.length; i++) {
    const s = e[i];
    s === "[" ? n++ : s === "]" ? n-- : s === "," && n === 0 && (t.push(e.slice(o, i)), o = i + 1);
  }
  return t.push(e.slice(o)), t.map((i) => i.trim()).filter(Boolean);
}
const Cc = "elsa-studio:apply-workflow-graph-operation-batch", Ec = "elsa-studio:undo-workflow-graph-operation-batch", aN = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function cN(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const o = yN(e), i = Ad(o.state.rootActivity), s = /* @__PURE__ */ new Map(), a = /* @__PURE__ */ new Map(), c = [];
  for (const u of t.operations) {
    const l = gN(u.kind), d = u.parameters ?? {};
    if (l === "add-activity") {
      const f = $e(d.activityId) ?? u.temporaryReferences?.[0], p = hN(f ?? $e(d.displayName) ?? $e(d.activityType) ?? "weaver-activity", i), h = lN(u, p, n);
      a.set(p, h), c.push(p), f && s.set(f, p), o.state.rootActivity && uN(o.state.rootActivity, h);
      const y = gt(d.position) ? Pr(d.position, { x: 280, y: 160 }) : null;
      y && (o.layout = kc(o.layout, p, y));
      continue;
    }
    if (l === "set-root") {
      const f = gr(o, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown root activity.");
      o.state.rootActivity = f;
      continue;
    }
    if (l === "set-designer-position") {
      const f = Dt(d.activityId, s);
      if (!f || !Ns(o.state.rootActivity, f)) throw new Error("Weaver batch referenced an unknown activity position.");
      o.layout = kc(o.layout, f, Pr(d, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const f = gr(o, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity property target.");
      pN(f, $e(d.propertyName) ?? "Value", d.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const f = gr(o, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity update target.");
      const p = gt(d.patch) ? d.patch : d;
      Object.assign(f, p);
      continue;
    }
    if (l === "remove-activity") {
      const f = Dt(d.activityId, s);
      if (!f) throw new Error("Weaver batch referenced an unknown activity remove target.");
      o.state.rootActivity = Id(o.state.rootActivity, f), o.layout = o.layout.filter((p) => p.nodeId !== f);
      continue;
    }
    if (l === "connect-activities") {
      dN(o, d, s);
      continue;
    }
    if (l === "disconnect-activities") {
      fN(o, d, s);
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
function lN(e, t, n) {
  const o = e.parameters ?? {}, i = $e(o.activityVersionId) ?? $e(o.activityType) ?? "Elsa.Workflows.Activity", s = n.find((a) => a.activityVersionId === i || a.activityTypeKey === i || a.displayName === $e(o.displayName));
  return s ? wr(s, t) : {
    nodeId: t,
    // catalogItem is provably undefined here (the truthy branch returned above), so use the id directly.
    activityVersionId: i,
    inputs: [],
    outputs: [],
    ...$e(o.displayName) ? { displayName: $e(o.displayName) } : {},
    designer: { position: Pr(o.position, { x: 280, y: 160 }) }
  };
}
function uN(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = js(e);
  n && !n.some((o) => o.nodeId === t.nodeId) && n.push(t);
}
function dN(e, t, n) {
  const o = e.state.rootActivity;
  if (!o?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const i = Dt(t.sourceActivityId ?? t.sourceId ?? t.from, n), s = Dt(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!i || !s) throw new Error("Weaver batch connection is missing source or target activity.");
  const a = o.structure.payload, c = Array.isArray(a.connections) ? a.connections : [], u = $e(t.connectionId) ?? `flow-${i}-${s}`;
  a.connections = [
    ...c.filter((l) => !gt(l) || l.id !== u),
    {
      id: u,
      source: { nodeId: i, port: $e(t.outcome) ?? $e(t.sourcePort) ?? "Done" },
      target: { nodeId: s }
    }
  ];
}
function fN(e, t, n) {
  const o = e.state.rootActivity, i = o?.structure?.payload.connections;
  if (!Array.isArray(i)) return;
  const s = $e(t.connectionId), a = Dt(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = Dt(t.targetActivityId ?? t.targetId ?? t.to, n);
  o.structure.payload.connections = i.filter((u) => {
    if (!gt(u)) return !0;
    if (s && u.id === s) return !1;
    const l = gt(u.source) ? u.source.nodeId : void 0, d = gt(u.target) ? u.target.nodeId : void 0;
    return l !== a || d !== c;
  });
}
function pN(e, t, n) {
  const o = gt(n);
  e[Sd(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: o ? "Object" : "Literal", value: n }
  };
}
function gr(e, t, n, o) {
  const i = Dt(t, n);
  return i ? Ns(e.state.rootActivity, i) ?? o.get(i) ?? null : null;
}
function Dt(e, t) {
  const n = $e(e);
  return n ? t.get(n) ?? n : null;
}
function Ns(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of _d(e)) {
    const o = Ns(n, t);
    if (o) return o;
  }
  return null;
}
function Id(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = js(e);
  if (n) {
    const o = n.map((i) => Id(i, t)).filter((i) => !!i);
    n.splice(0, n.length, ...o);
  }
  return e;
}
function Ad(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of _d(e)) Ad(n, t);
  return t;
}
function _d(e) {
  return js(e) ?? [];
}
function js(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function kc(e, t, n) {
  return [
    ...e.filter((o) => o.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function Pr(e, t) {
  const n = gt(e) ? e : {}, o = Number(n.x), i = Number(n.y);
  return {
    x: Number.isFinite(o) ? Math.max(40, Math.round(o)) : t.x,
    y: Number.isFinite(i) ? Math.max(40, Math.round(i)) : t.y
  };
}
function hN(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let o = n, i = 2;
  for (; t.has(o); )
    o = `${n}-${i}`, i += 1;
  return t.add(o), o;
}
function gN(e) {
  return typeof e == "number" ? aN[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function $e(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function yN(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function gt(e) {
  return typeof e == "object" && e !== null;
}
function mN({
  draft: e,
  details: t,
  catalog: n,
  replaceDraftByBatch: o,
  setStatus: i,
  setError: s
}) {
  const a = re(/* @__PURE__ */ new Map());
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
        const p = jn(e), h = cN(e, d.batch, n), y = `weaver-batch-${Date.now()}`;
        a.current.set(y, p), o(h.draft, h.finalActivityIds.at(-1) ?? null), i(h.summary), s(""), d.respond({ ok: !0, result: { ...h, undoToken: y } });
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
      a.current.delete(d.undoToken), o(f, null), i("Restored workflow draft before Weaver batch."), s(""), d.respond({ ok: !0, summary: "Restored workflow draft before Weaver batch." });
    };
    return window.addEventListener(Cc, c), window.addEventListener(Ec, u), () => {
      window.removeEventListener(Cc, c), window.removeEventListener(Ec, u);
    };
  }, [n, t, e, o, i, s]);
}
function xN({ context: e, draft: t, editDraft: n, setStatus: o, setError: i }) {
  const [s, a] = B(!1), c = re(""), u = re(0), l = re(Promise.resolve()), d = ie((p) => {
    c.current = p ? Ve(p) : "";
  }, []), f = ie(async (p, h) => {
    const y = async () => {
      const x = ++u.current, w = Ve(p);
      i("");
      try {
        const b = await _p(e, p), g = Ve(b);
        return c.current = g, n(({ draft: v }) => !v || v.id !== b.id ? null : Ve(v) === w ? b : { ...v, validationErrors: b.validationErrors }), x === u.current && o(h), b;
      } catch (b) {
        throw x === u.current && (o(""), i(b instanceof Error ? b.message : String(b))), b;
      }
    }, m = l.current.then(y, y);
    return l.current = m.catch(() => {
    }), m;
  }, [e, n, o, i]);
  return G(() => {
    if (!s || !t || Ve(t) === c.current) return;
    o("Autosaving...");
    const h = window.setTimeout(() => {
      f(t, "Autosaved").catch(() => {
      });
    }, P0);
    return () => window.clearTimeout(h);
  }, [s, t, f, o]), { saveDraft: f, autosaveEnabled: s, setAutosaveEnabled: a, markSaved: d };
}
function wN({ context: e, definitionId: t, resetHistory: n, loadDraft: o, markSaved: i, setError: s }) {
  const [a, c] = B(null), [u, l] = B([]), [d, f] = B([]), [p, h] = B(null), [y, m] = B(Co), [x, w] = B("loading"), b = ie(async () => {
    s(""), w("loading");
    const [g, v, j, N, S] = await Promise.all([
      bp(e, t),
      qr(e),
      zp(e).then(
        (D) => ({ ok: !0, descriptors: D }),
        () => ({ ok: !1, descriptors: [] })
      ),
      Vp(e).then(
        (D) => ({ ok: !0, descriptors: D }),
        () => ({ ok: !1, descriptors: Co })
      ),
      // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
      fl(e).then(
        (D) => D,
        () => null
      )
    ]), k = g.draft ?? null;
    c(g), i(k), n(k), o(k), l(v.activities ?? []), f(j.descriptors), h(S), m(N.descriptors.length > 0 ? N.descriptors : Co), w(j.ok ? "ready" : "failed");
  }, [e, t, n, o, i, s]);
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
function vN({ context: e, details: t, setDetails: n, setStatus: o }) {
  const i = re(null), s = re(null), a = re({});
  G(() => {
    i.current = t;
  }, [t]);
  const c = ie(() => {
    s.current !== null && (window.clearTimeout(s.current), s.current = null);
    const l = a.current;
    a.current = {};
    const d = i.current?.definition;
    !d || l.name === void 0 && l.description === void 0 || Ap(e, d.id, {
      name: l.name ?? d.name,
      description: l.description ?? d.description ?? null
    }).then((f) => n((p) => p && p.definition.id === f.definition.id ? { ...p, definition: f.definition } : p)).catch(() => o("Couldn't save name/description."));
  }, [e, n, o]), u = ie((l) => {
    n((d) => d && { ...d, definition: { ...d.definition, ...l } }), a.current = { ...a.current, ...l }, s.current !== null && window.clearTimeout(s.current), s.current = window.setTimeout(c, 800);
  }, [c, n]);
  return G(() => () => {
    c();
  }, [c]), { updateDefinitionMeta: u };
}
function bN({
  context: e,
  draft: t,
  details: n,
  busy: o,
  saveDraft: i,
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
  const y = ie(() => {
    if (!t) return;
    const b = n?.definition.name;
    f0(l0(t, b), b), d("Exported workflow as JSON.");
  }, [t, n, d]), m = ie(async () => {
    if (!(!t || o)) {
      l("saving"), d("Saving...");
      try {
        await i(t, "Saved");
      } catch {
      } finally {
        l("idle");
      }
    }
  }, [t, o, i, l, d]), x = ie(async () => {
    if (!(!t || o)) {
      l("promoting"), d("Saving...");
      try {
        await i(t, "Saved"), d("Promoting...");
        const b = await Dp(e, t.id), g = await Tp(e, b.versionId);
        u(g.artifactId), d(`Published ${g.artifactVersion}`), await s();
      } catch (b) {
        d(""), f(b instanceof Error ? b.message : String(b));
      } finally {
        l("idle");
      }
    }
  }, [t, o, e, i, s, u, l, d, f]), w = ie(async () => {
    if (!t?.state.rootActivity || o) return;
    const b = t, g = Ve(b);
    c(), d("Preparing test run...");
    try {
      l("testRunPreparing"), d("Preparing test run...");
      const v = lb(b);
      l("testRunStarting"), d("Starting test run...");
      const j = await $p(e, {
        definitionId: b.definitionId,
        snapshotId: v,
        state: b.state
      });
      a({ draftSignature: g, view: j }), p("runtime"), h(!1), d(ms(j) ? "Test run rejected" : "Test run dispatched");
    } catch (v) {
      d(""), f(v instanceof Error ? v.message : String(v));
    } finally {
      l("idle");
    }
  }, [t, o, e, c, a, p, h, l, d, f]);
  return { exportJson: y, save: m, promoteAndPublish: x, run: w };
}
function NN({
  context: e,
  draft: t,
  frames: n,
  selectedNodeId: o,
  catalog: i,
  activityDescriptors: s,
  availabilityDiagnostics: a
}) {
  const c = t?.state.rootActivity ?? null, u = fe(() => new Map(i.map((S) => [S.activityVersionId, S])), [i]), l = ie(
    (S) => th([S.activityVersionId, S.activityTypeKey], a),
    [a]
  ), d = fe(() => J0(s), [s]), f = fe(() => Zc(c, n, u), [c, n, u]), p = Xr(f, f ? u.get(f.activityVersionId) : void 0), h = !!f && p === "unsupported", y = fe(() => h ? null : qt(c, n, u), [c, n, u, h]), m = fe(() => h && f?.nodeId === o ? f : y?.slot.activities.find((S) => S.nodeId === o) ?? null, [h, y, f, o]), x = fe(
    () => m ? Q0(m, u, d) : null,
    [u, d, m]
  ), w = fe(
    () => m ? l({ activityVersionId: m.activityVersionId, activityTypeKey: u.get(m.activityVersionId)?.activityTypeKey }) : null,
    [l, u, m]
  ), b = m ? Me(m, u) : [], g = m ? dp(m, u.get(m.activityVersionId)) : !1, v = jp(e, t?.state, o, u), j = !h && y?.slot.mode === "flowchart";
  return {
    catalogByVersion: u,
    availabilityLookup: l,
    scopeOwner: f,
    isUnsupportedDesigner: h,
    scope: y,
    selectedNode: m,
    selectedDescriptor: x,
    selectedNodeAvailability: w,
    selectedSlots: b,
    selectedSupportsScopedVariables: g,
    scopedVariableAnalysis: v,
    isFlowchartDesigner: j,
    canAddActivitiesToCanvas: !c || !h
  };
}
function jN({
  details: e,
  draft: t,
  selectedNode: n,
  selectedNodeId: o,
  selectedDescriptor: i,
  catalogByVersion: s
}) {
  G(() => {
    if (!(!e || !t))
      return window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
        workflowId: e.definition.id,
        workflowDefinitionId: e.definition.id,
        workflowVersionId: t.sourceVersionId ?? null,
        draftId: t.id,
        revision: sb(t),
        selectedNodeId: o,
        selectedActivityType: i?.typeName ?? (n ? s.get(n.activityVersionId)?.activityTypeKey ?? n.activityVersionId : null),
        summary: e.definition.name,
        activities: xd(t.state.rootActivity, s),
        connections: wd(t.state.rootActivity, s),
        diagnostics: t.validationErrors.map((a) => ({ severity: a.code ?? "warning", message: a.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === e.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [s, e, t, i, n, o]);
}
function SN({
  paletteSearch: e,
  onSearchChange: t,
  groups: n,
  expandedCategories: o,
  onToggleCategory: i,
  onActivityClick: s,
  onActivityDragStart: a,
  onActivityDragEnd: c,
  onActivityPointerDown: u
}) {
  const l = e.trim().length > 0;
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-palette-body", children: [
    /* @__PURE__ */ r.jsxs("label", { className: "wf-palette-search", children: [
      /* @__PURE__ */ r.jsx(ei, { size: 14, "aria-hidden": "true" }),
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
      const f = l || o.has(d.category);
      return /* @__PURE__ */ r.jsxs("div", { className: "wf-palette-category", children: [
        /* @__PURE__ */ r.jsxs(
          "button",
          {
            type: "button",
            className: "wf-palette-category-toggle",
            role: "treeitem",
            "aria-expanded": f,
            onClick: () => i(d.category),
            children: [
              f ? /* @__PURE__ */ r.jsx(zc, { size: 14 }) : /* @__PURE__ */ r.jsx(Vt, { size: 14 }),
              /* @__PURE__ */ r.jsx("span", { children: d.category }),
              /* @__PURE__ */ r.jsx("small", { children: d.activities.length })
            ]
          }
        ),
        f ? /* @__PURE__ */ r.jsx("div", { className: "wf-palette-activities", role: "group", children: d.activities.map((p) => {
          const h = p.description?.trim(), y = h ? `wf-palette-description-${p.activityVersionId}` : void 0, m = Se(p), x = oi(p);
          return /* @__PURE__ */ r.jsxs(
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
                /* @__PURE__ */ r.jsx("span", { className: "wf-activity-icon", "data-icon": x, "aria-hidden": "true", children: gs(x) }),
                /* @__PURE__ */ r.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ r.jsx("strong", { children: m }),
                  h ? /* @__PURE__ */ r.jsx("small", { id: y, children: h }) : null
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
const Dd = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]), CN = "Variable";
function EN({
  activity: e,
  descriptor: t,
  editors: n,
  expressionEditors: o,
  expressionDescriptors: i,
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
  const d = PN(l), f = i.length > 0 ? i : Kb;
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ r.jsx("span", { className: "wf-section-label", children: "Properties" }),
    d.map((p) => /* @__PURE__ */ r.jsxs("section", { className: "wf-property-group", children: [
      d.length > 1 ? /* @__PURE__ */ r.jsx("h4", { children: p.category }) : null,
      p.inputs.map((h) => /* @__PURE__ */ r.jsx(
        kN,
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
function kN({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: o,
  expressionDescriptors: i,
  visibleVariables: s,
  scopeStatus: a,
  onChange: c
}) {
  const u = t.isReadOnly === !0, l = { activity: e, expressionDescriptors: i, readOnly: u }, d = zr(n, t, l), f = d?.component, p = t.isWrapped !== !1 ? Cd(e, t) : null, h = p?.expression.type ?? "Literal", y = Ub(e, t), m = h.toLowerCase(), w = p && (m === "literal" || m === "object") && !Qb(t) ? Gb(t.typeName) : null, b = w ? zr(n, t, { ...l, scope: "collection" }) : void 0, g = p ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: i,
    readOnly: u,
    surface: "inline",
    syntax: h
  } : null, v = g ? $d(o, g) : null, j = v?.surfaces.inline, N = v && g ? Pd(v, g, y) : [], S = w != null, k = !!(p && !S && MN(t, d?.id)), D = !!(p && !S && RN(t, d?.id)), [L, A] = B(!1), _ = (E) => {
    const T = p ? Yb(p, E) : E;
    c(Nc(e, t, T));
  }, R = (E) => {
    p && c(Nc(e, t, qb(p, E)));
  }, C = w ? b ? Mr(b.component, t, y, u, { ...l, scope: "collection" }, _) : /* @__PURE__ */ r.jsx(
    AN,
    {
      input: t,
      elementTypeName: w.elementTypeName,
      value: y,
      editors: n,
      context: l,
      disabled: u,
      onChange: _
    }
  ) : null, I = h === CN && p ? /* @__PURE__ */ r.jsx(
    TN,
    {
      value: y,
      visibleVariables: s,
      scopeStatus: a,
      disabled: u,
      onChange: _
    }
  ) : C ?? (j && g ? /* @__PURE__ */ r.jsx(
    j,
    {
      descriptor: t,
      syntax: h,
      value: y,
      disabled: u,
      context: g,
      onChange: _
    }
  ) : Mr(f, t, y, u, l, _));
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ r.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ r.jsx("span", { children: kd(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ r.jsx("p", { children: t.description }) : null,
    p && !k ? /* @__PURE__ */ r.jsx(
      Rr,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: h,
        descriptors: i,
        disabled: u,
        onChange: R
      }
    ) : null,
    k ? /* @__PURE__ */ r.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-expression-editor", children: [
        I,
        Vr(N)
      ] }),
      /* @__PURE__ */ r.jsx(
        Rr,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: h,
          descriptors: i,
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
          children: /* @__PURE__ */ r.jsx(Mo, { size: 13 })
        }
      ) : null
    ] }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
      I,
      Vr(N)
    ] }),
    D && !k ? /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => A(!0),
        children: [
          /* @__PURE__ */ r.jsx(Mo, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    L ? /* @__PURE__ */ r.jsx(
      _N,
      {
        input: t,
        value: y,
        syntax: h,
        descriptors: i,
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
function IN(e, t, n) {
  return [
    "wf-collection-item",
    t === e ? "dragging" : "",
    t !== null && t !== e && n === e ? "drop-target" : ""
  ].filter(Boolean).join(" ");
}
function AN({
  input: e,
  elementTypeName: t,
  value: n,
  editors: o,
  context: i,
  disabled: s,
  onChange: a
}) {
  const c = eN(n), u = nN(e, t), l = { ...i, scope: "element" }, d = zr(o, u, l)?.component, f = e.displayName || e.name, p = (j, N) => a(c.map((S, k) => k === j ? N : S)), [h, y] = B(null), [m, x] = B(null), w = () => {
    y(null), x(null);
  }, b = (j) => (N) => {
    y(j), N.dataTransfer.effectAllowed = "move", N.dataTransfer.setData("text/plain", String(j));
  }, g = (j) => (N) => {
    h !== null && (N.preventDefault(), N.dataTransfer.dropEffect = "move", m !== j && x(j));
  }, v = (j) => (N) => {
    N.preventDefault(), h !== null && h !== j && a(hr(c, h, j)), w();
  };
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-collection-editor", children: [
    c.length === 0 ? /* @__PURE__ */ r.jsx("p", { className: "wf-collection-empty", children: "No items yet." }) : /* @__PURE__ */ r.jsx("ul", { className: "wf-collection-items", children: c.map((j, N) => /* @__PURE__ */ r.jsxs(
      "li",
      {
        className: IN(N, h, m),
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
              onDragEnd: w,
              children: /* @__PURE__ */ r.jsx(Vc, { size: 13, "aria-hidden": "true" })
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
                children: /* @__PURE__ */ r.jsx(Ud, { size: 13 })
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
                children: /* @__PURE__ */ r.jsx(zc, { size: 13 })
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
                children: /* @__PURE__ */ r.jsx(In, { size: 13 })
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
        onClick: () => a([...c, tN(t)]),
        children: [
          /* @__PURE__ */ r.jsx(Yt, { size: 13 }),
          " Add item"
        ]
      }
    )
  ] });
}
function _N({
  input: e,
  value: t,
  syntax: n,
  descriptors: o,
  activity: i,
  expressionEditors: s,
  disabled: a,
  onChange: c,
  onSyntaxChange: u,
  onClose: l
}) {
  const d = $c(), f = e.displayName || e.name, p = {
    activity: i,
    descriptor: e,
    expressionDescriptors: o,
    readOnly: a,
    surface: "expanded",
    syntax: n
  }, h = $d(s, p), y = h?.surfaces.expanded, m = h ? Pd(h, p, t) : [], x = y ? null : $N(s, p);
  return G(() => {
    const w = (b) => {
      b.key === "Escape" && l();
    };
    return window.addEventListener("keydown", w), () => window.removeEventListener("keydown", w);
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
            descriptors: o,
            disabled: a,
            onChange: u
          }
        ),
        /* @__PURE__ */ r.jsx("span", { children: kd(e.typeName) })
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
            onChange: (w) => c(w.target.value)
          }
        )
      ] }),
      Vr(m)
    ] }),
    /* @__PURE__ */ r.jsxs("footer", { children: [
      /* @__PURE__ */ r.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: l, children: "Close" })
    ] })
  ] }) });
}
function Mr(e, t, n, o, i, s) {
  return e ? /* @__PURE__ */ r.jsx(
    e,
    {
      descriptor: t,
      value: n,
      disabled: o,
      context: i,
      onChange: s
    }
  ) : /* @__PURE__ */ r.jsx("input", { type: "text", value: n == null ? "" : String(n), disabled: o, onChange: (a) => s(a.target.value) });
}
function Rr({
  label: e,
  value: t,
  descriptors: n,
  disabled: o,
  variant: i = "block",
  onChange: s
}) {
  const [a, c] = B(!1), u = $c(), l = n.find((f) => f.type === t), d = [
    "wf-syntax-picker-trigger",
    i === "inline" ? "inline" : "",
    a ? "open" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ r.jsxs("div", { className: i === "inline" ? "wf-syntax-picker inline" : "wf-syntax-picker", onBlur: (f) => {
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
        disabled: o,
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
function Td(e) {
  return !e || e === Oo ? Oo : e;
}
function Ic(e, t) {
  return `${Td(t)}${Lr}${e}`;
}
function DN(e) {
  const t = e.indexOf(Lr);
  if (t < 0) return null;
  const n = e.slice(t + Lr.length);
  return n ? { scopeId: e.slice(0, t), referenceKey: n } : null;
}
function TN({ value: e, visibleVariables: t, scopeStatus: n, disabled: o, onChange: i }) {
  const s = ll(e), a = !!e && typeof e == "object" || typeof e == "string" && e.trim().startsWith("{"), c = s && (a || t.some((d) => d.referenceKey === s.referenceKey)) ? s : null, u = c ? Ic(c.referenceKey, c.declaringScopeId) : "", l = !!c && t.some(
    (d) => d.referenceKey === c.referenceKey && d.scopeId === Td(c.declaringScopeId)
  );
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-variable-picker", children: [
    /* @__PURE__ */ r.jsxs(
      "select",
      {
        "aria-label": "Variable reference",
        value: u,
        disabled: o,
        onChange: (d) => {
          const f = DN(d.target.value);
          f && i(pp(f.referenceKey, f.scopeId));
        },
        children: [
          /* @__PURE__ */ r.jsx("option", { value: "", children: "Select a variable…" }),
          c && !l ? /* @__PURE__ */ r.jsxs("option", { value: u, children: [
            c.referenceKey,
            " (not visible from this scope)"
          ] }) : null,
          t.map((d) => {
            const f = Ic(d.referenceKey, d.scopeId);
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
function zr(e, t, n) {
  return [...e].sort((o, i) => (o.order ?? 500) - (i.order ?? 500)).find((o) => o.supports(t, n));
}
function $d(e, t) {
  return [...e].sort((n, o) => (n.order ?? 500) - (o.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function Pd(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function $N(e, t) {
  if (t.syntax.toLowerCase() === "literal") return null;
  const n = [...e].sort((a, c) => (a.order ?? 500) - (c.order ?? 500)).find((a) => a.supports(t) && a.metadata)?.metadata;
  if (!n) return `No enhanced editor is registered for ${t.syntax}. Using the generic text editor.`;
  const o = n.displayName?.trim() || "enhanced editor", i = n.installHint?.trim(), s = `No ${o} is registered for ${t.syntax}. Using the generic text editor.`;
  return i ? `${s} ${i}` : s;
}
function Vr(e) {
  return e.length === 0 ? null : /* @__PURE__ */ r.jsx("div", { className: "wf-expression-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const o = t.severity ?? "info";
    return /* @__PURE__ */ r.jsxs("p", { className: `wf-expression-editor-diagnostic ${o}`, children: [
      t.code ? /* @__PURE__ */ r.jsx("span", { children: t.code }) : null,
      t.message
    ] }, `${t.code ?? "diagnostic"}-${n}`);
  }) });
}
function PN(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "General";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return [...t.entries()].map(([n, o]) => ({ category: n, inputs: o }));
}
function MN(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !Dd.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function RN(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !Dd.has(t) && n !== "multiline") return !1;
  const o = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(o) || n === "singleline" || n === "multiline";
}
function LN({
  context: e,
  selectedNode: t,
  selectedNodeLabel: n,
  selectedActivityType: o,
  selectedDescriptor: i,
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
  onEnterSlot: m
}) {
  return t ? /* @__PURE__ */ r.jsxs("div", { className: "wf-inspector-content", children: [
    /* @__PURE__ */ r.jsx("h3", { children: n }),
    /* @__PURE__ */ r.jsxs("dl", { children: [
      /* @__PURE__ */ r.jsx("dt", { children: "Node ID" }),
      /* @__PURE__ */ r.jsx("dd", { children: t.nodeId }),
      /* @__PURE__ */ r.jsx("dt", { children: "Activity type" }),
      /* @__PURE__ */ r.jsx("dd", { children: o }),
      /* @__PURE__ */ r.jsx("dt", { children: "Activity version" }),
      /* @__PURE__ */ r.jsx("dd", { children: t.activityVersionId })
    ] }),
    s ? /* @__PURE__ */ r.jsxs("div", { className: "wf-availability-notice", children: [
      /* @__PURE__ */ r.jsx(Po, { size: 14 }),
      /* @__PURE__ */ r.jsxs("span", { children: [
        "No longer available for new use · ",
        Ho(s.state)
      ] })
    ] }) : null,
    /* @__PURE__ */ r.jsx(
      EN,
      {
        activity: t,
        descriptor: i,
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
      _0,
      {
        context: e,
        variables: cl(t),
        title: "Container variables",
        addLabel: "Add container variable",
        emptyLabel: "No container variables declared on this activity.",
        warnings: gp(h.shadowingWarnings, t.nodeId),
        onChange: (x) => y(fp(t, x))
      }
    ) }) : null,
    a.length > 0 ? /* @__PURE__ */ r.jsxs("div", { className: "wf-slot-list", children: [
      /* @__PURE__ */ r.jsx("span", { children: "Embedded slots" }),
      a.map((x) => /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => m(t, x.id, `${n} / ${x.label}`), children: [
        x.label,
        /* @__PURE__ */ r.jsx("small", { children: cb(x, c) })
      ] }, x.id))
    ] }) : /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
  ] }) : /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
}
const Md = [
  { value: "flowchart", label: "Flowchart", hint: "Free-form graph of connected activities." },
  { value: "sequence", label: "Sequence", hint: "Ordered list of activities that run top to bottom." }
];
function Rd({ label: e, hint: t }) {
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsx("span", { className: "wf-root-card-title", children: e }),
    /* @__PURE__ */ r.jsx("span", { className: "wf-root-card-hint", children: t })
  ] });
}
function zN({ value: e, onChange: t }) {
  return /* @__PURE__ */ r.jsx("div", { className: "wf-root-cards", role: "radiogroup", "aria-label": "Root activity", children: Md.map((n) => {
    const o = e === n.value;
    return /* @__PURE__ */ r.jsxs("label", { className: "wf-root-card", "data-checked": o || void 0, children: [
      /* @__PURE__ */ r.jsx(
        "input",
        {
          type: "radio",
          name: "wf-root-kind",
          "aria-label": n.label,
          value: n.value,
          checked: o,
          onChange: () => t(n.value)
        }
      ),
      /* @__PURE__ */ r.jsx(Rd, { label: n.label, hint: n.hint })
    ] }, n.value);
  }) });
}
function VN({ onPick: e }) {
  return /* @__PURE__ */ r.jsx("div", { className: "wf-root-cards", role: "group", "aria-label": "Fill this slot", children: Md.map((t) => /* @__PURE__ */ r.jsx(
    "button",
    {
      type: "button",
      className: "wf-root-card",
      "aria-label": t.label,
      onClick: () => e(t.value),
      children: /* @__PURE__ */ r.jsx(Rd, { label: t.label, hint: t.hint })
    },
    t.value
  )) });
}
function ON({ slotLabel: e, catalog: t, onPickActivity: n, onBrowseAll: o }) {
  const i = (s) => {
    const a = fd(t, s);
    a && n(a);
  };
  return /* @__PURE__ */ r.jsx("div", { className: "wf-slot-empty", role: "group", "aria-label": `Fill ${e}`, children: /* @__PURE__ */ r.jsxs("div", { className: "wf-slot-empty-card", children: [
    /* @__PURE__ */ r.jsx("p", { className: "wf-slot-empty-title", children: "This slot is empty" }),
    /* @__PURE__ */ r.jsxs("p", { className: "wf-slot-empty-hint", children: [
      "Choose a container for ",
      /* @__PURE__ */ r.jsx("strong", { children: e }),
      ", or pick any activity."
    ] }),
    /* @__PURE__ */ r.jsx(VN, { onPick: i }),
    /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-slot-empty-browse",
        onClick: (s) => o({ clientX: s.clientX, clientY: s.clientY }),
        children: [
          /* @__PURE__ */ r.jsx(Vn, { size: 15 }),
          " Browse all activities…"
        ]
      }
    ),
    /* @__PURE__ */ r.jsx("p", { className: "wf-slot-empty-drag-hint", children: "Tip: you can also drag activities from the palette onto the canvas." })
  ] }) });
}
function HN({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: o,
  expressionEditors: i,
  workflowDesignerPanels: s,
  onBack: a
}) {
  const c = Lb(), { draft: u, frames: l, selectedNodeId: d, testRun: f, publishedArtifactId: p } = c.state, {
    loadDraft: h,
    replaceDraftByBatch: y,
    editDraft: m,
    editDraftAndSelect: x,
    select: w,
    navigateToScope: b,
    resetToRoot: g,
    enterSlot: v,
    startTestRun: j,
    clearTestRun: N,
    setPublishedArtifact: S
  } = c, [k, D] = B(""), [L, A] = B(""), [_, R] = B("idle"), [C, I] = B(() => /* @__PURE__ */ new Set()), [E, T] = B(""), [$, P] = B("activities"), [F, H] = B("inspector"), [O, U] = B("designer"), {
    paletteWidth: q,
    inspectorWidth: ne,
    paletteCollapsed: ce,
    inspectorCollapsed: Z,
    maximizedSidePanel: z,
    setInspectorCollapsed: X,
    paletteExpanded: le,
    inspectorExpanded: ae,
    editorBodyClassName: J,
    editorBodyStyle: te,
    toggleSidePanelCollapsed: de,
    toggleSidePanelMaximized: V,
    startSidePanelResize: Q,
    handleSidePanelResizeKeyDown: he
  } = Ib(), { resetHistory: we, undo: Ie, redo: Ae, canUndoNow: Re, canRedoNow: ot } = Pb({ draft: u, restoreDraft: h }), { saveDraft: Fe, autosaveEnabled: Be, setAutosaveEnabled: Te, markSaved: Le } = xN({ context: e, draft: u, editDraft: m, setStatus: A, setError: D }), {
    details: M,
    setDetails: W,
    catalog: K,
    activityDescriptors: ee,
    availabilityDiagnostics: se,
    expressionDescriptors: ye,
    descriptorStatus: ge,
    reload: Ee
  } = wN({ context: e, definitionId: t, resetHistory: we, loadDraft: h, markSaved: Le, setError: D }), { updateDefinitionMeta: _e } = vN({ context: e, details: M, setDetails: W, setStatus: A }), {
    catalogByVersion: be,
    availabilityLookup: ke,
    scopeOwner: Zn,
    isUnsupportedDesigner: mt,
    scope: Tt,
    selectedNode: Ze,
    selectedDescriptor: $t,
    selectedNodeAvailability: xi,
    selectedSlots: Gn,
    selectedSupportsScopedVariables: wi,
    scopedVariableAnalysis: vi,
    isFlowchartDesigner: ut,
    canAddActivitiesToCanvas: bi
  } = NN({ context: e, draft: u, frames: l, selectedNodeId: d, catalog: K, activityDescriptors: ee, availabilityDiagnostics: se }), Pt = fe(() => $r(K), [K]), Ni = fe(() => {
    const Y = E.trim().toLowerCase();
    if (!Y) return Pt;
    const ue = K.filter((ve) => Se(ve).toLowerCase().includes(Y) || ve.activityTypeKey.toLowerCase().includes(Y) || (ve.category ?? "").toLowerCase().includes(Y) || (ve.description ?? "").toLowerCase().includes(Y));
    return $r(ue);
  }, [K, E, Pt]), Mt = _ !== "idle", ji = !!u?.state.rootActivity && !Mt, Jn = At(n, "weaver.workflows.find-draft-risks"), Qn = At(n, "weaver.workflows.propose-update"), Si = Bb({
    draft: u,
    scope: Tt,
    scopeOwner: Zn,
    frames: l,
    catalog: K,
    catalogByVersion: be,
    isUnsupportedDesigner: mt,
    isFlowchartDesigner: ut,
    canAddActivitiesToCanvas: bi,
    selectedNodeId: d,
    editDraft: m,
    editDraftAndSelect: x,
    select: w,
    resetToRoot: g,
    setStatus: A,
    setError: D
  }), {
    nodes: Rt,
    edges: Ci,
    canvasRef: Ei,
    setReactFlowInstance: ki,
    connectMenu: cn,
    setConnectMenu: eo,
    edgeActions: to,
    onNodesChange: no,
    onEdgesChange: Ii,
    onNodesDelete: Ai,
    onEdgesDelete: _i,
    isValidConnection: Di,
    onConnect: Ti,
    onConnectStart: $i,
    onConnectEnd: oo,
    onReconnect: Pi,
    commitLayout: Mi,
    canAutoLayout: Ri,
    autoLayout: Li,
    onCanvasDragOver: io,
    onCanvasDragLeave: ro,
    onCanvasDrop: so,
    openEmptyConnectMenu: ao,
    onConnectMenuPick: co,
    addActivity: zi,
    onPaletteClick: Vi,
    onPaletteDragStart: Oi,
    onPaletteDragEnd: Hi,
    onPalettePointerDown: Wi
  } = Si, Fi = !mt && l.length > 0 && !!Tt && Tt.slot.activities.length === 0 && Rt.length === 0;
  mN({ draft: u, details: M, catalog: K, replaceDraftByBatch: y, setStatus: A, setError: D }), G(() => {
    !u?.state.rootActivity || K.length === 0 || m(({ draft: Y }) => {
      if (!Y?.state.rootActivity) return null;
      const ue = nl(Y.state.rootActivity, be);
      return !ue || ue === Y.state.rootActivity ? null : {
        ...Y,
        state: {
          ...Y.state,
          rootActivity: ue
        }
      };
    });
  }, [K.length, be, u?.state.rootActivity, m]), jN({ details: M, draft: u, selectedNode: Ze, selectedNodeId: d, selectedDescriptor: $t, catalogByVersion: be }), G(() => {
    I((Y) => {
      let ue = !1;
      const ve = new Set(Y);
      for (const it of Pt)
        ve.has(it.category) || (ve.add(it.category), ue = !0);
      return ue ? ve : Y;
    });
  }, [Pt]);
  const { exportJson: Bi, save: Ki, promoteAndPublish: lo, run: Xi } = bN({
    context: e,
    draft: u,
    details: M,
    busy: Mt,
    saveDraft: Fe,
    reload: Ee,
    startTestRun: j,
    clearTestRun: N,
    setPublishedArtifact: S,
    setOperation: R,
    setStatus: A,
    setError: D,
    setActiveRightPanelId: H,
    setInspectorCollapsed: X
  }), uo = ie((Y) => {
    m(({ draft: ue }) => ue ? { ...ue, state: Y(ue.state) } : null);
  }, [m]), Yi = ie((Y) => {
    if (!u) return "No draft is loaded.";
    const ue = d0(Y, u);
    return ue.ok ? (h(ue.draft), A("Applied workflow JSON."), null) : ue.error;
  }, [u, h]);
  G(() => {
    const Y = (ue) => {
      if (O !== "designer" || !(ue.metaKey || ue.ctrlKey)) return;
      const ve = ue.target;
      if (ve && (ve.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(ve.tagName))) return;
      const it = ue.key.toLowerCase();
      it === "z" && !ue.shiftKey ? (ue.preventDefault(), Ie()) : (it === "z" && ue.shiftKey || it === "y") && (ue.preventDefault(), Ae());
    };
    return window.addEventListener("keydown", Y), () => window.removeEventListener("keydown", Y);
  }, [O, Ie, Ae]);
  const qi = ie((Y) => {
    m(({ draft: ue }) => {
      const ve = ue?.state.rootActivity;
      return !ue || !ve ? null : {
        ...ue,
        state: {
          ...ue.state,
          rootActivity: tl(ve, Y.nodeId, () => Y, be)
        }
      };
    });
  }, [be, m]), ln = ie((Y) => {
    if (!Y) return;
    const ue = u?.state.rootActivity;
    if (!ue) return;
    const ve = Mf(ue, Y, (it) => {
      const ks = be.get(it.activityVersionId);
      return ks ? Se(ks) : it.nodeId;
    }, be);
    ve && (U("designer"), b(ve, Y), X(!1));
  }, [u?.state.rootActivity, be, X, b]), Ui = (Y) => {
    I((ue) => {
      const ve = new Set(ue);
      return ve.has(Y) ? ve.delete(Y) : ve.add(Y), ve;
    });
  };
  if (!M || !u)
    return /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: k || "Loading workflow editor..." });
  const Lt = f?.draftSignature === Ve(u) ? f.view : null, un = Lt && L.startsWith("Test run") ? "" : L, Ld = (Y) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(Y)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, zd = {
    definition: M.definition,
    draft: u,
    selectedActivity: Ze,
    selectedActivityDescriptor: $t,
    selectedActivitySlots: Gn,
    catalog: K,
    currentScopeOwner: Zn,
    frames: l
  }, Ss = s.map((Y) => {
    const ue = Y.component;
    return {
      id: Y.id,
      title: Y.title,
      side: Y.side,
      order: Y.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ r.jsx(ue, { context: zd })
    };
  }), Zi = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ r.jsx(Vn, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(
        SN,
        {
          paletteSearch: E,
          onSearchChange: T,
          groups: Ni,
          expandedCategories: C,
          onToggleCategory: Ui,
          onActivityClick: Vi,
          onActivityDragStart: Oi,
          onActivityDragEnd: Hi,
          onActivityPointerDown: Wi
        }
      )
    },
    ...Ss.filter((Y) => Y.side === "left")
  ].sort(xc), Gi = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ r.jsx(Wr, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(
        LN,
        {
          context: e,
          selectedNode: Ze,
          selectedNodeLabel: Ze ? Rt.find((Y) => Y.id === Ze.nodeId)?.data.label ?? Ze.nodeId : "",
          selectedActivityType: Ze ? $t?.typeName ?? be.get(Ze.activityVersionId)?.activityTypeKey ?? "Unknown" : "",
          selectedDescriptor: $t,
          selectedNodeAvailability: xi,
          selectedSlots: Gn,
          catalogByVersion: be,
          selectedSupportsScopedVariables: wi,
          propertyEditors: o,
          expressionEditors: i,
          expressionDescriptors: ye,
          descriptorStatus: ge,
          scopedVariableAnalysis: vi,
          onSelectedActivityChange: qi,
          onEnterSlot: v
        }
      )
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: /* @__PURE__ */ r.jsx(Xt, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(Sb, { testRun: Lt, onOpenRun: Ld })
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ r.jsx(Oc, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(
        kb,
        {
          context: e,
          ai: n,
          definitionId: M.definition.id,
          publishedArtifactId: p
        }
      )
    },
    ...Ss.filter((Y) => Y.side === "right")
  ].sort(xc), Cs = Zi.find((Y) => Y.id === $) ?? Zi[0], Es = Gi.find((Y) => Y.id === F) ?? Gi[0], Vd = [
    { id: "designer", title: "Designer", order: 0, icon: /* @__PURE__ */ r.jsx(Hc, { size: 14 }), render: () => null },
    { id: "code", title: "Code", order: 1, icon: /* @__PURE__ */ r.jsx(ef, { size: 14 }), render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: /* @__PURE__ */ r.jsx(Hr, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-link-button", onClick: a, children: "Definitions" }),
      /* @__PURE__ */ r.jsx(Vt, { size: 14 }),
      /* @__PURE__ */ r.jsx("strong", { children: M.definition.name }),
      /* @__PURE__ */ r.jsx("span", { className: "wf-chip", children: "Draft" }),
      un ? /* @__PURE__ */ r.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ r.jsx(on, { size: 13 }),
        " ",
        un
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
              disabled: !Re,
              onClick: Ie,
              children: /* @__PURE__ */ r.jsx(Zd, { size: 16 })
            }
          ),
          /* @__PURE__ */ r.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Redo",
              title: "Redo (Ctrl+Shift+Z)",
              disabled: !ot,
              onClick: Ae,
              children: /* @__PURE__ */ r.jsx(Gd, { size: 16 })
            }
          ),
          /* @__PURE__ */ r.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Auto-layout",
              title: "Auto-layout the canvas",
              disabled: !Ri,
              onClick: Li,
              children: /* @__PURE__ */ r.jsx(Jd, { size: 16 })
            }
          )
        ] }),
        /* @__PURE__ */ r.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ r.jsx("input", { className: "wf-autosave-switch-input", type: "checkbox", checked: Be, onChange: (Y) => Te(Y.target.checked) }),
          /* @__PURE__ */ r.jsx("span", { children: "Autosave" })
        ] }),
        Jn ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => _t(n, Jn, { definition: M.definition, draft: u }), children: [
          /* @__PURE__ */ r.jsx(at, { size: 15 }),
          " Risks"
        ] }) : null,
        Qn ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => _t(n, Qn, { definition: M.definition, draft: u }), children: [
          /* @__PURE__ */ r.jsx(at, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ r.jsxs("button", { type: "button", title: "Export workflow as JSON", onClick: Bi, children: [
          /* @__PURE__ */ r.jsx(Qd, { size: 15 }),
          " Export"
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: Mt, onClick: () => {
          Ki();
        }, children: [
          /* @__PURE__ */ r.jsx(Pc, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: Mt, onClick: () => {
          lo();
        }, children: [
          /* @__PURE__ */ r.jsx(Rc, { size: 15 }),
          " Promote"
        ] }),
        Lt ? /* @__PURE__ */ r.jsx(
          jb,
          {
            testRun: Lt,
            onOpenDetails: () => {
              H("runtime"), X(!1);
            }
          }
        ) : null,
        /* @__PURE__ */ r.jsxs(
          "button",
          {
            type: "button",
            disabled: !ji,
            title: u.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              Xi();
            },
            children: [
              /* @__PURE__ */ r.jsx(Xt, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    k ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ r.jsx(yt, { size: 16 }),
      " ",
      k
    ] }) : null,
    /* @__PURE__ */ r.jsxs("div", { className: J, style: te, children: [
      /* @__PURE__ */ r.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ r.jsx(
            $o,
            {
              label: "Activities panel tabs",
              tabs: Zi,
              activeTabId: Cs.id,
              onSelect: P
            }
          ),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": ce ? "Expand activities panel" : "Collapse activities panel",
                title: ce ? "Expand" : "Collapse",
                onClick: () => de("palette"),
                children: ce ? /* @__PURE__ */ r.jsx(Vt, { size: 14 }) : /* @__PURE__ */ r.jsx(Ro, { size: 14 })
              }
            ),
            ce ? null : /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": z === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: z === "palette" ? "Restore" : "Maximize",
                onClick: () => V("palette"),
                children: z === "palette" ? /* @__PURE__ */ r.jsx(_s, { size: 14 }) : /* @__PURE__ */ r.jsx(Mo, { size: 14 })
              }
            )
          ] })
        ] }),
        le ? Cs.render() : null
      ] }),
      le && !z ? /* @__PURE__ */ r.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": xn,
          "aria-valuemax": wn,
          "aria-valuenow": q,
          tabIndex: 0,
          onPointerDown: (Y) => Q("palette", Y),
          onKeyDown: (Y) => he("palette", Y)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ r.jsx("div", { className: "wf-canvas-tabs", children: /* @__PURE__ */ r.jsx(
          $o,
          {
            label: "Editor view tabs",
            tabs: Vd,
            activeTabId: O,
            onSelect: (Y) => U(Y)
          }
        ) }),
        O === "code" ? /* @__PURE__ */ r.jsx(m0, { draft: u, onApply: Yi }) : O === "properties" ? /* @__PURE__ */ r.jsx(T0, { details: M, draft: u, context: e, onStateChange: uo, onDefinitionMetaChange: _e }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
          /* @__PURE__ */ r.jsxs("div", { className: "wf-breadcrumb", children: [
            /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => g(), children: "Root" }),
            l.map((Y, ue) => /* @__PURE__ */ r.jsxs(Qe.Fragment, { children: [
              /* @__PURE__ */ r.jsx(Vt, { size: 13 }),
              /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => b(l.slice(0, ue + 1), null), children: Y.label })
            ] }, `${Y.ownerNodeId}-${Y.slotId}-${ue}`))
          ] }),
          /* @__PURE__ */ r.jsxs("div", { className: "wf-canvas", ref: Ei, onDragOver: io, onDragLeave: ro, onDrop: so, children: [
            /* @__PURE__ */ r.jsx(ld.Provider, { value: to, children: /* @__PURE__ */ r.jsx(ud.Provider, { value: ke, children: /* @__PURE__ */ r.jsxs(
              Qu,
              {
                nodes: Rt,
                edges: Ci,
                nodeTypes: bd,
                edgeTypes: Nd,
                onInit: ki,
                onNodesChange: no,
                onEdgesChange: Ii,
                onNodesDelete: Ai,
                onEdgesDelete: _i,
                onConnect: Ti,
                onConnectStart: ut ? $i : void 0,
                onConnectEnd: ut ? oo : void 0,
                onReconnect: ut ? Pi : void 0,
                isValidConnection: Di,
                onDragOver: io,
                onDragLeave: ro,
                onDrop: so,
                onPaneClick: () => w(null),
                onNodeClick: (Y, ue) => w(ue.id),
                onNodeDragStop: mt ? void 0 : Mi,
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
                  /* @__PURE__ */ r.jsx(td, { gap: 18, size: 1 }),
                  /* @__PURE__ */ r.jsx(od, {}),
                  /* @__PURE__ */ r.jsx(rd, { pannable: !0, zoomable: !0 })
                ]
              }
            ) }) }),
            Fi ? /* @__PURE__ */ r.jsx(
              ON,
              {
                slotLabel: Tt?.slot.label ?? "this slot",
                catalog: K,
                onPickActivity: zi,
                onBrowseAll: ao
              }
            ) : ut && Rt.length === 0 ? /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => ao(), children: [
              /* @__PURE__ */ r.jsx(Yt, { size: 15 }),
              " Add activity"
            ] }) : null,
            cn ? /* @__PURE__ */ r.jsx(
              yb,
              {
                clientX: cn.clientX,
                clientY: cn.clientY,
                activities: K,
                onPick: co,
                onClose: () => eo(null)
              }
            ) : null
          ] }),
          /* @__PURE__ */ r.jsx(Nb, { draft: u, onRepair: ln })
        ] })
      ] }),
      ae && !z ? /* @__PURE__ */ r.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": vn,
          "aria-valuemax": bn,
          "aria-valuenow": ne,
          tabIndex: 0,
          onPointerDown: (Y) => Q("inspector", Y),
          onKeyDown: (Y) => he("inspector", Y)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ r.jsx(
            $o,
            {
              label: "Inspector panel tabs",
              tabs: Gi,
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
                "aria-label": Z ? "Expand inspector panel" : "Collapse inspector panel",
                title: Z ? "Expand" : "Collapse",
                onClick: () => de("inspector"),
                children: Z ? /* @__PURE__ */ r.jsx(Ro, { size: 14 }) : /* @__PURE__ */ r.jsx(Vt, { size: 14 })
              }
            ),
            Z ? null : /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": z === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: z === "inspector" ? "Restore" : "Maximize",
                onClick: () => V("inspector"),
                children: z === "inspector" ? /* @__PURE__ */ r.jsx(_s, { size: 14 }) : /* @__PURE__ */ r.jsx(Mo, { size: 14 })
              }
            )
          ] })
        ] }),
        ae ? Es.render() : null
      ] })
    ] })
  ] });
}
function WN({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: i }) {
  const s = dd(n, t), a = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
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
      /* @__PURE__ */ r.jsx("select", { value: t, onChange: (u) => i(Number(u.target.value)), children: R0.map((u) => /* @__PURE__ */ r.jsx("option", { value: u, children: u }, u)) })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ r.jsx(Ro, { size: 14 }),
        " Previous"
      ] }),
      /* @__PURE__ */ r.jsxs("span", { children: [
        "Page ",
        e,
        " of ",
        s
      ] }),
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => o(e + 1), disabled: e >= s, "aria-label": "Next page", title: "Next page", children: [
        "Next ",
        /* @__PURE__ */ r.jsx(Vt, { size: 14 })
      ] })
    ] })
  ] });
}
function FN({ draft: e, creating: t, ai: n, suggestMetadataAction: o, onChange: i, onClose: s, onSubmit: a }) {
  const [c, u] = B(!1), [l, d] = B(""), [f, p] = B(!1), [h, y] = B(null), [m, x] = B(null), w = re(null), b = re(e);
  b.current = e;
  const g = re(i);
  g.current = i;
  const v = ie((N) => {
    const S = { ...b.current };
    N.name && (S.name = N.name), N.description && (S.description = N.description), g.current(S), y(null), x(null);
  }, []);
  G(() => {
    if (o)
      return n.onPromptResult((N) => {
        if (N.requestId !== w.current) return;
        if (w.current = null, p(!1), N.status !== "completed") {
          x(N.status === "cancelled" ? "Weaver needs more detail — continue in the assistant panel." : "Weaver couldn't generate a suggestion. Try again or fill the fields manually.");
          return;
        }
        const S = H0(N.text);
        if (!S) {
          x("Couldn't read a suggestion from Weaver's reply. See the assistant panel.");
          return;
        }
        N.autoApply ? v(S) : y(S);
      });
  }, [n, o, v]);
  const j = () => {
    if (!o) return;
    const N = o.createPrompt({ draft: b.current, intent: l });
    if (!N) return;
    const S = `wf-suggest-${Date.now()}-${Math.round(Math.random() * 1e6)}`;
    w.current = S, p(!0), y(null), x(null), n.dispatchPrompt({ ...N, requestId: S });
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
          o ? /* @__PURE__ */ r.jsxs(
            "button",
            {
              type: "button",
              className: "wf-ai-action",
              "aria-expanded": c,
              onClick: () => u((N) => !N),
              title: o.description ?? o.label,
              children: [
                /* @__PURE__ */ r.jsx(at, { size: 13 }),
                " ",
                o.label
              ]
            }
          ) : null
        ] }),
        o && c ? /* @__PURE__ */ r.jsxs("div", { className: "wf-ai-suggest", role: "group", "aria-label": "Suggest name and description", children: [
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
            /* @__PURE__ */ r.jsx(at, { size: 13 }),
            " ",
            f ? "Generating…" : "Generate"
          ] }) }),
          m ? /* @__PURE__ */ r.jsx("p", { className: "wf-ai-suggest-error", role: "alert", children: m }) : null,
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
              onChange: (N) => i({ ...e, name: N.target.value })
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
              onChange: (N) => i({ ...e, description: N.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ r.jsxs("fieldset", { className: "wf-form-field wf-root-field", children: [
          /* @__PURE__ */ r.jsx("legend", { children: "Root activity" }),
          /* @__PURE__ */ r.jsx(
            zN,
            {
              value: e.rootKind,
              onChange: (N) => i({ ...e, rootKind: N, rootActivityVersionId: null })
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
function BN({ context: e, ai: t, onOpen: n }) {
  const [o, i] = B(""), [s, a] = B("active"), [c, u] = B(1), [l, d] = B(L0), [f, p] = B("loading"), [h, y] = B(""), [m, x] = B(""), [w, b] = B([]), [g, v] = B(0), [j, N] = B(() => /* @__PURE__ */ new Set()), [S, k] = B(null), [D, L] = B(!1), [A, _] = B([]), [R, C] = B("idle"), I = re(null), E = fe(() => w.map((V) => V.id), [w]), T = At(t, "weaver.workflows.suggest-create-metadata"), $ = At(t, "weaver.workflows.explain-definition"), P = E.filter((V) => j.has(V)).length, F = E.length > 0 && P === E.length, H = ie(async () => {
    p("loading"), y("");
    try {
      const V = await vp(e, { search: o, state: s, page: c, pageSize: l }), Q = typeof V.totalCount == "number", he = V.totalCount ?? V.definitions.length, we = dd(he, l);
      if (he > 0 && c > we) {
        u(we);
        return;
      }
      b(Q ? V.definitions : O0(V.definitions, c, l)), v(he), p("ready");
    } catch (V) {
      y(V instanceof Error ? V.message : String(V)), p("failed");
    }
  }, [e, o, s, c, l]);
  G(() => {
    H();
  }, [H]), G(() => {
    I.current && (I.current.indeterminate = P > 0 && !F);
  }, [F, P]);
  const O = ie(async () => {
    if (!(R === "loading" || R === "ready")) {
      C("loading");
      try {
        const V = await qr(e);
        _(V.activities ?? []), C("ready");
      } catch (V) {
        C("failed"), y(V instanceof Error ? V.message : String(V));
      }
    }
  }, [R, e]), U = () => {
    y(""), x(""), k({ name: "", description: "", rootKind: "flowchart" }), O();
  }, q = async () => {
    if (S?.name.trim()) {
      L(!0), y(""), x("");
      try {
        const V = await Cp(e, {
          name: S.name.trim(),
          description: S.description.trim() || null,
          rootKind: S.rootKind,
          rootActivityVersionId: W0(S, A)
        });
        k(null), n(V.definition.id);
      } catch (V) {
        y(V instanceof Error ? V.message : String(V));
      } finally {
        L(!1);
      }
    }
  }, ne = (V) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(V)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, ce = async () => {
    if (w.length === 1 && c > 1) {
      u(c - 1);
      return;
    }
    await H();
  }, Z = () => N(/* @__PURE__ */ new Set()), z = (V, Q) => {
    N((he) => {
      const we = new Set(he);
      return Q ? we.add(V) : we.delete(V), we;
    });
  }, X = (V) => {
    N((Q) => {
      const he = new Set(Q);
      for (const we of E)
        V ? he.add(we) : he.delete(we);
      return he;
    });
  }, le = (V) => {
    a(V), u(1), Z();
  }, ae = (V) => {
    i(V), u(1), Z();
  }, J = async (V) => {
    if (await $s().confirm({ message: `Delete workflow definition "${V.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" })) {
      x(""), y("");
      try {
        await Ep(e, V.id), z(V.id, !1), x(`Deleted ${V.name}`), await ce();
      } catch (Q) {
        y(Q instanceof Error ? Q.message : String(Q));
      }
    }
  }, te = async (V) => {
    x(""), y("");
    try {
      await kp(e, V.id), z(V.id, !1), x(`Restored ${V.name}`), await ce();
    } catch (Q) {
      y(Q instanceof Error ? Q.message : String(Q));
    }
  }, de = async (V) => {
    if (await $s().confirm({ message: `Permanently delete workflow definition "${V.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) {
      x(""), y("");
      try {
        await Ip(e, V.id), z(V.id, !1), x(`Permanently deleted ${V.name}`), await ce();
      } catch (Q) {
        y(Q instanceof Error ? Q.message : String(Q));
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
        /* @__PURE__ */ r.jsx(ei, { size: 15 }),
        /* @__PURE__ */ r.jsx("input", { value: o, onChange: (V) => ae(V.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => {
        H();
      }, children: "Refresh" }),
      /* @__PURE__ */ r.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ r.jsxs("button", { type: "button", title: "Create workflow", onClick: U, children: [
        /* @__PURE__ */ r.jsx(Yt, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ r.jsx(Un, { message: h, title: "Couldn't load workflow definitions" }) : null,
    f !== "failed" && h ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ r.jsx(yt, { size: 16 }),
      " ",
      h
    ] }) : null,
    m ? /* @__PURE__ */ r.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ r.jsx(on, { size: 14 }),
      " ",
      m
    ] }) : null,
    j.size > 0 ? /* @__PURE__ */ r.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ r.jsxs("span", { children: [
        j.size,
        " selected"
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: Z, children: "Clear selection" })
    ] }) : null,
    f === "loading" ? /* @__PURE__ */ r.jsx(xs, {}) : null,
    f === "ready" && w.length === 0 ? /* @__PURE__ */ r.jsx(
      ws,
      {
        icon: /* @__PURE__ */ r.jsx(Oc, { size: 22 }),
        title: `No ${s} workflow definitions`,
        description: "Create a workflow to start designing automation, or adjust your filters to see more.",
        action: /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-link-button", onClick: U, children: [
          /* @__PURE__ */ r.jsx(Yt, { size: 15 }),
          " Create workflow"
        ] })
      }
    ) : null,
    f === "ready" && w.length > 0 ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ r.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ r.jsx(
            "input",
            {
              ref: I,
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
        w.map((V) => /* @__PURE__ */ r.jsxs(
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
              /* @__PURE__ */ r.jsx("span", { children: s === "deleted" ? Oe(V.deletedAt) : V.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ r.jsx("span", { children: Oe(V.lastModifiedAt) }),
              /* @__PURE__ */ r.jsx("span", { className: "wf-row-actions", onClick: (Q) => Q.stopPropagation(), children: s === "active" ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
                /* @__PURE__ */ r.jsx("button", { type: "button", onClick: (Q) => {
                  Q.stopPropagation(), n(V.id);
                }, children: "Open" }),
                /* @__PURE__ */ r.jsx("button", { type: "button", onClick: (Q) => {
                  Q.stopPropagation(), ne(V.id);
                }, children: "Artifacts" }),
                $ ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => _t(t, $, V), children: [
                  /* @__PURE__ */ r.jsx(at, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ r.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  J(V);
                }, children: [
                  /* @__PURE__ */ r.jsx(In, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
                /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
                  te(V);
                }, children: [
                  /* @__PURE__ */ r.jsx(Fr, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ r.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  de(V);
                }, children: [
                  /* @__PURE__ */ r.jsx(In, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          V.id
        ))
      ] }),
      /* @__PURE__ */ r.jsx(
        WN,
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
      FN,
      {
        draft: S,
        creating: D,
        ai: t,
        suggestMetadataAction: T,
        onChange: (V) => k(V),
        onClose: () => k(null),
        onSubmit: q
      }
    ) : null
  ] });
}
function KN({ activities: e, activityCatalog: t, selectedEvidenceId: n = null, onSelectEvidence: o }) {
  const i = fe(
    () => new Map(t.map((a) => [a.activityTypeKey, a])),
    [t]
  ), s = fe(() => YN(e), [e]);
  return s.length === 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty wf-timeline-empty", children: "No activity executions recorded yet." }) : /* @__PURE__ */ r.jsx("ol", { className: "wf-timeline", "aria-label": "Execution timeline", children: s.map((a) => {
    const c = i.get(a.activityType), u = oi(c), l = c ? Se(c) : It(a.activityType) ?? a.activityType, d = It(a.activityType) ?? a.activityType, f = qN(a.startedAt ?? a.scheduledAt), p = hs(a.startedAt, a.completedAt);
    return /* @__PURE__ */ r.jsx("li", { children: /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-timeline-entry",
        "data-selected": a.activityExecutionId === n,
        onClick: () => o?.(a.activityExecutionId),
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
          /* @__PURE__ */ r.jsx(XN, { status: a.status })
        ]
      }
    ) }, a.activityExecutionId);
  }) });
}
function XN({ status: e }) {
  const t = e.toLowerCase(), n = t === "completed" ? "OK" : e;
  return /* @__PURE__ */ r.jsx("span", { className: "wf-status-badge wf-timeline-pill", "data-status": t, children: n });
}
function YN(e) {
  return e.map((t, n) => ({ activity: t, index: n })).sort((t, n) => Ac(t.activity) - Ac(n.activity) || t.index - n.index).map((t) => t.activity);
}
function Ac(e) {
  const t = e.startedAt ?? e.scheduledAt, n = t ? Date.parse(t) : Number.NaN;
  return Number.isNaN(n) ? Number.MAX_SAFE_INTEGER : n;
}
function qN(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? "" : t.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: !1 });
}
function UN({ context: e }) {
  const [t, n] = B("loading"), [o, i] = B(""), [s, a] = B(""), [c, u] = B(""), [l, d] = B([]), f = ie(async () => {
    n("loading"), i("");
    try {
      const h = await Pp(e, {
        status: s || void 0,
        runKind: c || void 0,
        take: 100
      });
      d(h), n("ready");
    } catch (h) {
      i(h instanceof Error ? h.message : String(h)), d([]), n("failed");
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
    t === "failed" ? /* @__PURE__ */ r.jsx(Un, { message: o }) : null,
    t === "loading" ? /* @__PURE__ */ r.jsx(xs, {}) : null,
    t === "ready" && l.length === 0 ? /* @__PURE__ */ r.jsx(
      ws,
      {
        icon: /* @__PURE__ */ r.jsx(Vn, { size: 22 }),
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
            /* @__PURE__ */ r.jsx("span", { children: yd(h.runKind) }),
            /* @__PURE__ */ r.jsx("span", { children: /* @__PURE__ */ r.jsx(an, { status: h.status, subStatus: h.subStatus }) }),
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
            /* @__PURE__ */ r.jsx("span", { children: Oe(h.startedAt ?? h.createdAt) }),
            /* @__PURE__ */ r.jsx("span", { children: hs(h.startedAt ?? h.createdAt, h.completedAt ?? h.updatedAt) })
          ]
        },
        h.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function ZN({ context: e, ai: t, workflowExecutionId: n }) {
  const [o, i] = B("loading"), [s, a] = B(""), [c, u] = B(null), [l, d] = B(null), f = At(t, "weaver.workflows.explain-instance"), p = ie(async () => {
    if (!n) {
      a("No workflow execution id was provided."), i("failed");
      return;
    }
    i("loading"), a("");
    try {
      const m = await Mp(e, n), [x, w] = await Promise.all([
        Sp(e, m.instance.definitionVersionId).then(
          (b) => ({ definitionVersion: b, error: "" }),
          (b) => ({ definitionVersion: null, error: b instanceof Error ? b.message : String(b) })
        ),
        qr(e)
      ]);
      u({
        details: m,
        definitionVersion: x.definitionVersion,
        definitionVersionError: x.error,
        activityCatalog: w.activities
      }), d(null), i("ready");
    } catch (m) {
      u(null), a(db(m, n)), i("failed");
    }
  }, [e, n]);
  G(() => {
    p();
  }, [p]);
  const h = () => {
    window.history.pushState({}, "", "/workflows/instances"), window.dispatchEvent(new PopStateEvent("popstate"));
  }, y = () => {
    const m = c?.details.instance.definitionId;
    m && (window.history.pushState({}, "", `/workflows/definitions?definition=${encodeURIComponent(m)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  };
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: h, children: [
        /* @__PURE__ */ r.jsx(Ro, { size: 14 }),
        " Runs"
      ] }),
      c?.details.instance.definitionId ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: y, children: [
        /* @__PURE__ */ r.jsx(Hc, { size: 14 }),
        " Designer"
      ] }) : null,
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
        p();
      }, children: [
        /* @__PURE__ */ r.jsx(Fr, { size: 14 }),
        " Refresh"
      ] }),
      c && f ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => _t(t, f, c.details), children: [
        /* @__PURE__ */ r.jsx(at, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    o === "loading" ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Loading workflow run..." }) : null,
    o === "failed" ? /* @__PURE__ */ r.jsx(Un, { message: s }) : null,
    o === "ready" && c ? /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-detail-workbench", children: [
      /* @__PURE__ */ r.jsx(
        GN,
        {
          definitionVersion: c.definitionVersion,
          definitionVersionError: c.definitionVersionError,
          activityCatalog: c.activityCatalog,
          details: c.details,
          selectedEvidenceId: l,
          onSelectEvidence: d
        }
      ),
      /* @__PURE__ */ r.jsx(
        JN,
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
          graphNodeIds: c.definitionVersion ? nb(c.definitionVersion, c.activityCatalog) : void 0
        }
      )
    ] }) : null
  ] });
}
function GN({ definitionVersion: e, definitionVersionError: t, activityCatalog: n, details: o, selectedEvidenceId: i, onSelectEvidence: s }) {
  const a = fe(() => {
    if (!e) return { nodes: [], edges: [] };
    const c = e.state.rootActivity;
    if (!c) return { nodes: [], edges: [] };
    const u = n.find((h) => h.activityVersionId === c.activityVersionId), l = Xr(c, u), d = l === "unsupported" ? null : qt(c, [], n), f = l === "unsupported" ? xr(c, n, e.layout) : d ? el(d, n, e.layout) : xr(c, n, e.layout), p = f.nodes.map((h) => ({
      ...h,
      draggable: !1,
      connectable: !1,
      deletable: !1
    }));
    return {
      nodes: Ff(p, o.activities, o.incidents, i),
      edges: f.edges.map((h) => ({ ...h, deletable: !1 }))
    };
  }, [n, e, o, i]);
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
          /* @__PURE__ */ r.jsx("small", { children: o.instance.definitionVersionId })
        ] }) })
      ] }),
      /* @__PURE__ */ r.jsx(an, { status: o.instance.status, subStatus: o.instance.subStatus })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ r.jsxs("div", { className: "wf-empty", children: [
        "The workflow run loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ r.jsx("small", { children: ub(t) }) : null
      ] }),
      e && a.nodes.length === 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      a.nodes.length > 0 ? /* @__PURE__ */ r.jsxs(
        Qu,
        {
          nodes: a.nodes,
          edges: a.edges,
          nodeTypes: bd,
          edgeTypes: Nd,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (c, u) => s(u.id),
          onPaneClick: () => s(null),
          children: [
            /* @__PURE__ */ r.jsx(td, {}),
            /* @__PURE__ */ r.jsx(rd, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ r.jsx(od, {})
          ]
        }
      ) : null
    ] })
  ] });
}
function JN({ ai: e, action: t, summary: n, details: o, state: i, error: s, selectedEvidenceId: a = null, onSelectEvidence: c, graphNodeIds: u, activityCatalog: l = [] }) {
  const [d, f] = B("timeline");
  if (!n)
    return /* @__PURE__ */ r.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Select a workflow run to inspect its timeline." }) });
  const p = o?.incidents.length ?? 0, h = QN(o?.activities ?? [], a), y = (x) => {
    c?.(x), f("activity");
  }, m = [
    { id: "timeline", title: "Timeline", order: 0, icon: /* @__PURE__ */ r.jsx(Wr, { size: 14 }), render: () => null },
    { id: "activity", title: "Activity", order: 1, icon: /* @__PURE__ */ r.jsx(Mc, { size: 14 }), render: () => null },
    { id: "issues", title: p > 0 ? `Issues (${p})` : "Issues", order: 2, icon: /* @__PURE__ */ r.jsx(yt, { size: 14 }), render: () => null },
    { id: "details", title: "Details", order: 3, icon: /* @__PURE__ */ r.jsx(Hr, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ r.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Workflow run details", children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("span", { children: "Workflow Instance ID" }),
        /* @__PURE__ */ r.jsx("h3", { children: n.workflowExecutionId })
      ] }),
      t ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => _t(e, t, o ?? n), children: [
        /* @__PURE__ */ r.jsx(at, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    /* @__PURE__ */ r.jsx("div", { className: "wf-instance-tabs", children: /* @__PURE__ */ r.jsx($o, { label: "Workflow run tabs", tabs: m, activeTabId: d, onSelect: (x) => f(x) }) }),
    i === "loading" ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Loading run details..." }) : null,
    i === "failed" ? /* @__PURE__ */ r.jsx(Un, { message: s }) : null,
    i === "ready" && o ? /* @__PURE__ */ r.jsx("div", { className: "wf-instance-tab-content", children: d === "timeline" ? /* @__PURE__ */ r.jsx(
      KN,
      {
        activities: o.activities,
        activityCatalog: l,
        selectedEvidenceId: a,
        onSelectEvidence: y
      }
    ) : d === "activity" ? /* @__PURE__ */ r.jsx(ej, { activity: h, activityCatalog: l }) : d === "issues" ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
      /* @__PURE__ */ r.jsx(tj, { incidents: o.incidents, selectedEvidenceId: a, onSelectEvidence: c }),
      /* @__PURE__ */ r.jsx(nj, { details: o, graphNodeIds: u })
    ] }) : /* @__PURE__ */ r.jsxs("dl", { className: "wf-instance-meta", children: [
      /* @__PURE__ */ r.jsx("dt", { children: "Status" }),
      /* @__PURE__ */ r.jsx("dd", { children: /* @__PURE__ */ r.jsx(an, { status: n.status, subStatus: n.subStatus }) }),
      /* @__PURE__ */ r.jsx("dt", { children: "Run Kind" }),
      /* @__PURE__ */ r.jsx("dd", { children: yd(n.runKind) }),
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
      /* @__PURE__ */ r.jsx("dd", { children: Oe(n.createdAt) }),
      /* @__PURE__ */ r.jsx("dt", { children: "Started" }),
      /* @__PURE__ */ r.jsx("dd", { children: Oe(n.startedAt) }),
      /* @__PURE__ */ r.jsx("dt", { children: "Completed" }),
      /* @__PURE__ */ r.jsx("dd", { children: Oe(n.completedAt) }),
      /* @__PURE__ */ r.jsx("dt", { children: "Correlation" }),
      /* @__PURE__ */ r.jsx("dd", { children: n.correlationId || "None" })
    ] }) }) : null
  ] });
}
function QN(e, t) {
  if (!t) return null;
  const n = e.find((i) => i.activityExecutionId === t);
  if (n) return n;
  const o = e.filter((i) => i.executableNodeId === t || i.authoredActivityId === t);
  return o.length > 0 ? al(o) : null;
}
function ej({ activity: e, activityCatalog: t }) {
  if (!e)
    return /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
      /* @__PURE__ */ r.jsx("h4", { children: "Activity" }),
      /* @__PURE__ */ r.jsx("p", { children: "No activity selected." })
    ] });
  const o = t.find((i) => i.activityTypeKey === e.activityType)?.displayName || It(e.activityType) || e.activityType;
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ r.jsx("h4", { children: "Activity" }),
    /* @__PURE__ */ r.jsxs("dl", { className: "wf-instance-meta", children: [
      /* @__PURE__ */ r.jsx("dt", { children: "Name" }),
      /* @__PURE__ */ r.jsx("dd", { children: o }),
      /* @__PURE__ */ r.jsx("dt", { children: "Status" }),
      /* @__PURE__ */ r.jsx("dd", { children: /* @__PURE__ */ r.jsx(an, { status: e.status, subStatus: e.subStatus }) }),
      /* @__PURE__ */ r.jsx("dt", { children: "Activity Execution ID" }),
      /* @__PURE__ */ r.jsx("dd", { children: e.activityExecutionId }),
      /* @__PURE__ */ r.jsx("dt", { children: "Authored Activity ID" }),
      /* @__PURE__ */ r.jsx("dd", { children: e.authoredActivityId }),
      /* @__PURE__ */ r.jsx("dt", { children: "Type" }),
      /* @__PURE__ */ r.jsxs("dd", { children: [
        It(e.activityType) ?? e.activityType,
        " ",
        /* @__PURE__ */ r.jsx("small", { children: e.activityTypeVersion })
      ] }),
      /* @__PURE__ */ r.jsx("dt", { children: "Started" }),
      /* @__PURE__ */ r.jsx("dd", { children: Oe(e.startedAt) }),
      /* @__PURE__ */ r.jsx("dt", { children: "Completed" }),
      /* @__PURE__ */ r.jsx("dd", { children: Oe(e.completedAt) }),
      /* @__PURE__ */ r.jsx("dt", { children: "Duration" }),
      /* @__PURE__ */ r.jsx("dd", { children: hs(e.startedAt, e.completedAt) || "Unknown" }),
      /* @__PURE__ */ r.jsx("dt", { children: "Bookmarks" }),
      /* @__PURE__ */ r.jsx("dd", { children: e.bookmarkIds.length }),
      /* @__PURE__ */ r.jsx("dt", { children: "Incidents" }),
      /* @__PURE__ */ r.jsx("dd", { children: e.incidentIds.length })
    ] })
  ] });
}
function tj({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ r.jsx("h4", { children: "Incidents" }),
    e.length === 0 ? /* @__PURE__ */ r.jsx("p", { children: "No incidents recorded." }) : null,
    e.map((o) => /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-instance-incident",
        "data-severity": o.severity.toLowerCase(),
        "data-selected": o.incidentId === t,
        onClick: () => n?.(o.incidentId),
        children: [
          /* @__PURE__ */ r.jsx("strong", { children: o.failureType }),
          /* @__PURE__ */ r.jsxs("span", { children: [
            o.status,
            " · ",
            o.severity
          ] }),
          /* @__PURE__ */ r.jsx("p", { children: o.message })
        ]
      },
      o.incidentId
    ))
  ] });
}
function nj({ details: e, graphNodeIds: t }) {
  if (!t) return null;
  const n = new Map(e.activities.map((s) => [s.activityExecutionId, s])), o = e.activities.filter((s) => !t.has(gc(s))), i = e.incidents.filter((s) => {
    const a = s.activityExecutionId ? n.get(s.activityExecutionId) : null, c = s.executableNodeId ?? (a ? gc(a) : "");
    return !c || !t.has(c);
  });
  return o.length === 0 && i.length === 0 ? null : /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ r.jsx("h4", { children: "Unmatched runtime evidence" }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-unmatched-list", children: [
      o.map((s) => /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ r.jsx("strong", { children: It(s.activityType) ?? s.activityType }),
        /* @__PURE__ */ r.jsx("small", { children: s.activityExecutionId })
      ] }, `activity-${s.activityExecutionId}`)),
      i.map((s) => /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ r.jsx("strong", { children: s.failureType }),
        /* @__PURE__ */ r.jsx("small", { children: s.incidentId })
      ] }, `incident-${s.incidentId}`))
    ] })
  ] });
}
function oj({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: o,
  workflowDesignerPanels: i
}) {
  const [s, a] = B(_c);
  G(() => {
    const u = () => a(_c());
    return window.addEventListener("popstate", u), () => window.removeEventListener("popstate", u);
  }, []);
  const c = (u) => {
    const l = u ? `/workflows/definitions?definition=${encodeURIComponent(u)}` : "/workflows/definitions";
    window.history.pushState({}, "", l), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return s ? /* @__PURE__ */ r.jsx(HN, { context: e, definitionId: s, ai: t, propertyEditors: n, expressionEditors: o, workflowDesignerPanels: i, onBack: () => c(null) }) : /* @__PURE__ */ r.jsx(mi, { title: "Definitions", children: /* @__PURE__ */ r.jsx(BN, { context: e, ai: t, onOpen: c }) });
}
function ij({ context: e, ai: t }) {
  const [n, o] = B(Dc);
  G(() => {
    const s = () => o(Dc());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const i = ie((s) => {
    const a = s?.trim() ?? "", c = new URL(window.location.href);
    a ? c.searchParams.set("definition", a) : c.searchParams.delete("definition"), o(a || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ r.jsx(mi, { title: "Executables", children: /* @__PURE__ */ r.jsx(Cb, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: i }) });
}
function rj({ context: e }) {
  return /* @__PURE__ */ r.jsx(mi, { title: "Runs", children: /* @__PURE__ */ r.jsx(UN, { context: e }) });
}
function sj({ context: e, ai: t }) {
  const n = aj();
  return /* @__PURE__ */ r.jsx(mi, { title: "Run", children: /* @__PURE__ */ r.jsx(ZN, { context: e, ai: t, workflowExecutionId: n }) });
}
function mi({ title: e, children: t }) {
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
function aj() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function fj(e) {
  cf(e.dialogs), e.featureAreas.add({
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
        component: () => /* @__PURE__ */ r.jsx(oj, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list() })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ r.jsx(ij, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => /* @__PURE__ */ r.jsx(rj, { context: e.backend })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => /* @__PURE__ */ r.jsx(sj, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => /* @__PURE__ */ r.jsx(nh, { context: e.backend })
      }
    ]
  });
}
export {
  ib as isConnectEndOverExistingWorkflowNode,
  fj as register,
  rb as resolveConnectEndSource
};
